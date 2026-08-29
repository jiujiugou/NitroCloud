# ADR-010：回写功能（Command 模块）设计——反向写值闭环落地

- 问题：DoD 5「反向写值从云端发起，收到网关回执，闭环打通」是 6 条 DoD 中唯一未完成项——`src/NitroCloud.Command` 不存在、未进 slnx/Api 组合根，`Storage` 缺 `ICommandStore`，`Persistence` 缺 CommandStore 与 `command_records` 表，Api 缺 `CommandsController`。ADR-003/008 已拍板契约与状态机，本次补齐「可落地的实现设计」。
- 前置决策（沿用，不重新拍板）：契约见 DESIGN.md §4.3（commands / commands/ack，QoS1）；状态机 Pending → Sent → Acked/Failed/Timeout、重试不换 commandId（ADR-003 载荷墙、ADR-008 D6）；命令落 SQLite（ADR-001）。

## 现状盘点（已就绪，勿重复造）
| 层 | 已就绪 |
| --- | --- |
| Domain | `CommandRecord` / `CommandAck` / `CommandRequest` / `CommandResult` / `CommandStatus` 齐全 |
| Shared | `TopicUtil` 命令/回执 topic 构造与解析（`Commands` / `CommandAck` / `CommandAckSubscription`）齐全 |
| Api Dtos | `WriteValueRequestDto` / `WriteValueResponseDto` / `CommandRecordDto` + `ApiMappings.ToDto(CommandRecord)` |
| Api 序列化 | SignalR/JSON camelCase + 枚举序列化为名称（前端 types.ts 对齐） |
| Telemetry | `CloudMetrics.CommandSentTotal` / `CommandAckTotal` / `CommandTimeoutTotal` 已定义 |
| 前端 | `commands.ts` / `useCommand.ts` / `CommandDialog.vue` / `types.ts` / `signalr.ts`（`OnCommandAck` 已接线）——无需改动 |
| 配置 | `appsettings.json` `Command` 段已存在（MqttHost/Port/ClientId/ReconnectDelayMs/TimeoutSeconds/MaxAttempts/PollIntervalMs） |

## 待实现清单（本 ADR 设计范围）
1. 新项目 `src/NitroCloud.Command`（含 CommandOptions / ICommandDispatcher / MqttCommandClient / CommandManager / CommandHostedService）
2. slnx 注册 + Api.csproj 引用 + Program.cs `AddNitroCommand`
3. Storage 新增 `ICommandStore`（只增不删）
4. Persistence 新增 `CommandStore` + FluentMigrator **M005** 重建 `command_records`
5. Api 新增 `CommandsController`
6. Command 模块单元测试
7. 跨项目前置：NitroGateway 命令处理器 + `tools/mqtt-simulator` 回执模拟（演示用）

## 设计决策

### D1 模块与依赖
- `src/NitroCloud.Command` 引用 Domain/Shared/Storage/Telemetry（与 Ingest 同构）；只经 `IRealtimeNotifier` 推送，不引 SignalR。
- 关键类型（职责边界）：
  - `CommandOptions`：绑定 `Command` 配置段，启动校验并钳制数值（沿用 Ingest 快照读取模式）。
  - `ICommandDispatcher`：抽象「发布命令」，Api 注入触发；发布失败**不改 Pending 语义**（由后台扫描重发兜底）。
  - `MqttCommandClient`：发布 `commands` + 订阅 `commands/ack`（QoS1），断线重连，状态日志。
  - `CommandManager`：状态机 + 超时重试 + 回执处理 + 幂等（核心逻辑，纯类可单测）。
  - `CommandHostedService`：后台服务——ack 回执处理循环 + Pending/Sent 超时扫描轮询。
- 唯一宿主 = Api；Command 以 `BackgroundService` 注册进同一宿主（ADR-008 D1）。

### D2 存储与迁移
```csharp
// Storage/ICommandStore.cs（只增不删）
public interface ICommandStore
{
    Task AddAsync(CommandRecord cmd, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid commandId, CommandStatus status, string? error, CancellationToken ct = default); // 终态不覆盖
    Task<CommandRecord?> GetAsync(Guid commandId, CancellationToken ct = default);
    Task<IReadOnlyList<CommandRecord>> QueryInFlightAsync(CancellationToken ct = default); // Pending/Sent，供后台扫描
}
```
- `CommandStore`（Persistence/EF Core，模式同 `AlarmStore`）。
- 迁移 **M005** 重建 `command_records`：M003 曾建、M004 删（当时 Command 未落地、避免死表）；本次功能落地需重建，表结构沿用 M003（command_id PK / type / site_id / device_id / point_id / value / requested_at / status / error / attempts / sent_at / acked_at，索引 `(site_id, status)`）。
- ⚠ **数据模型变更（重建表）属 G1 项**，落码前一句话确认。

### D3 状态机与幂等（沿用已拍板约束，落代码注释）
- `Pending → Sent → Acked / Failed / Timeout`；终态不覆盖（UpdateStatusAsync 幂等）。
- 幂等键 `commandId`：重试不换；网关按 commandId 去重；重复回执忽略。
- 网关离线：命令保持 Pending，后台扫描重发；达上限标 Timeout（可人工重发）。
- 回执 `result`/`error` 必填（ADR-003 载荷墙）。

### D4 API 与数据流
- `POST /api/commands/write`：
  1. 校验 siteId/deviceId/pointId 非空 + point 存在且 writable → 否则 400；
  2. 构造 `CommandRecord{Pending}` → `ICommandStore.AddAsync`；
  3. `ICommandDispatcher.DispatchAsync` 发布 `commands` topic；
  4. 返回 `WriteValueResponseDto{commandId, status, requestedAt}`。
- `GET /api/commands/{commandId}` → `CommandRecordDto`（前端超时轮询兜底用）。
- 回执流：`MqttCommandClient` 收到 `commands/ack` → `CommandManager.HandleAckAsync`：解析 topic(siteId/deviceId) + payload(commandId/result/error/at) → 幂等校验 → `UpdateStatusAsync(Acked/Failed)` → `IRealtimeNotifier.NotifyCommandAckAsync` → 前端 `OnCommandAck`。

### D5 超时与重试
- `TimeoutSeconds=10`（与前端 `ACK_TIMEOUT_MS` 对齐）、`MaxAttempts=3`、`PollIntervalMs=1000`（appsettings 已配）。
- 后台扫描：Sent 超时未回执 → 重发（attempts+1，不换 commandId）→ 达上限标 Timeout；Pending 超时未进展同样进入重试/超时判定（防 Api 触发发布丢失）。
- 指标：复用 `CloudMetrics.CommandSentTotal / CommandAckTotal / CommandTimeoutTotal`。

### D6 MQTT 连接
- Command 独立 MQTT client（ClientId=`nitrocloud-command`），模块内自持连接/重连循环（延迟 = ReconnectDelayMs）。
- **不跨模块引用 Ingest**（单向依赖约束）：不引 `MqttConnectRetryPolicy`，也不新增 Polly 依赖——单连接用简单退避循环即可，避免给纯工具层/新模块加依赖。
- 订阅 `nitrogateway/+/+/commands/ack`（`TopicUtil.CommandAckSubscription`）。

### D7 配置与部署
- 复用 `appsettings.json` `Command` 段；docker-compose `center` 环境补 `Command__MqttHost: broker` / `Command__MqttPort: "1883"`（对齐 Ingest）。（2026-08-29 已补：此前 compose 仅配了 Ingest__*，Command 回落 localhost 导致容器内连不上 broker。）

### D8 跨项目前置与演示
- 契约以 DESIGN.md §4.3 为准，云侧不单方面改；NitroGateway 需补 `commands` 订阅处理器 + 写值 + 回 `commands/ack`（跨项目小改动，属可接受，AGENTS.md 明示）。
- `tools/mqtt-simulator` 补命令回执模拟：订阅 `nitrogateway/+/+/commands`，收到 WritePoint 回 `commands/ack`（Success/Failure 可配）→ 支撑 DoD 5 无真实网关演示。
- （2026-08-27 更新）NitroGateway 命令处理器已落地（NitroGateway ADR-069）：订阅 `commands` + WriteService 写值 + 幂等回执 `commands/ack`，单测 780/780；剩余仅 `tools/mqtt-simulator` 回执模拟与端到端联调。

## 载荷墙（改了会破坏什么）
- `Storage` 接口只增不删；`Domain` 不引基础设施。
- 命令契约以网关侧为准；重试不换 commandId；回执 result/error 必填。
- 命令落 SQLite（`command_records`），时序只进 InfluxDB。
- 依赖包不升级/降级（Command 新增引用沿用 MQTTnet 5.1.0.1559，不新增 Polly）。
- 单向依赖无环（Command 不依赖 Api/Ingest）。

## 验收标准（DoD 5 展开）
1. `POST /api/commands/write` 创建并发布命令，返回 commandId；
2. 模拟网关回 ack → 状态 Acked → SignalR `OnCommandAck` → 前端 toast；
3. 不回执：超时重试 3 次 → Timeout，前端轮询兜底显示；
4. 失败回执 → Failed；
5. `GET /api/commands/{id}` 可查状态；
6. 命令落 SQLite；单元测试红绿对照 + `dotnet build/test` 全绿。

## 测试计划
- UnitTests/Command：
  - `CommandManagerTests`：状态机迁移、超时重试上限、幂等（重复回执忽略 / 终态不覆盖）、未知 commandId 忽略；
  - `CommandRequestSerializerTests`：发布 JSON 契约（camelCase、字段齐）；
  - `CommandAckParserTests`：ack 载荷 + topic 解析。
- 集成/联调（实现阶段）：`mqtt-simulator` 回执模拟 + docker compose 全链路。

## 变更记录
- 2026-08-27 新建；状态：设计已拍板，待实现（落码前同步 DESIGN.md DoD5/C-003 状态，再动手）。
- 2026-08-27 实现完成（已落地）：新增 `src/NitroCloud.Command`（8 文件：CommandOptions/ICommandDispatcher/MqttCommandClient/CommandManager/CommandHostedService/序列化与回执解析/DI 扩展）+ `Storage/ICommandStore` + Persistence `CommandStore` 与 M005 重建 `command_records` + Api `CommandsController`（POST write / GET {commandId}）+ 17 个 Command 单测（状态机/序列化/回执解析）；`dotnet build` 0 警告 0 错误，Command 单测 17/17 全绿（全量 90 通过，仅 2 项既有 ValueCoercion 失败、非本次引入）。状态：**已落地**（云侧闭环代码齐备；回执端到端联调依赖 NitroGateway 命令处理器 + `tools/mqtt-simulator` 回执模拟，见 D8，属跨项目前置）。
