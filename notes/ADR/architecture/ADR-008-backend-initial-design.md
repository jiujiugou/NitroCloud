# ADR-008：后端初版设计（解决方案结构 · 模块职责 · 关键接口）

## 问题
DESIGN.md v0.1 只有模块表，缺可落地的初版结构：`src/` 骨架、模块职责边界、关键接口与上下行数据流。M1 开工无从下手。

## 范围
覆盖 `src/` + `tests/` 骨架、模块职责、关键接口（初版、可演化）、上下行数据流、API 清单、可观测性。作为 M1~M5 的初版基线（草案待评审）。

## 设计决策

### D1 解决方案与分层
- `NitroCloud.slnx`（沿用网关惯例），单向依赖、无环引用。
- **唯一宿主 = `NitroCloud.Api`**：REST + SignalR Hub + 组合根；`Ingest`、`Command` 以 `BackgroundService` 注册进同一宿主；`Persistence`、`Influx` 是 `Storage` 接口的实现，在组合根注入。单机 Docker 一个 center 容器跑全套。
- 依赖方向：`Domain`/`Shared` 纯库 ← `Storage`（纯接口）← `Persistence`/`Influx`（实现）；`Ingest`/`Api`/`Command` 只依赖 `Storage` 接口 + `Domain`，不碰具体实现。
- **推送解耦**：`Ingest`/`Command` 不直接引用 SignalR，只依赖 `Storage` 里的 `IRealtimeNotifier` 纯接口（由 `Api` 用 `IHubContext` 实现），保证单向依赖不破环。

### D2 模块职责与关键类型
| 模块 | 职责 | 关键类型（初版） |
| --- | --- | --- |
| `Domain` | 实体/枚举，纯 C# | `Site` `Device` `Point` `AlarmRecord` `CommandRecord` `MeasurementRecord`(契约模型)、`AlarmSeverity/State`、`DeviceStatus` |
| `Shared` | 通用 | `OperationResult<T>`、`TimeUtil`、`TopicUtil`（topic 解析/构造） |
| `Storage` | 纯接口，只增不删 | `ITimeseriesStore` `ILatestValueCache` `IAlarmStore` `ICommandStore` `IRealtimeNotifier` |
| `Persistence` | SQLite 元数据 + FluentMigrator | EF Core `AppDbContext`；实现 `IAlarmStore` `ICommandStore`（告警/命令落库） |
| `Influx` | InfluxDB 实现 | `InfluxTimeseriesStore`（批量写 + Flux 查询封装）、`BatchWriter` |
| `Ingest` | MQTT 订阅 + 解析 + 写库 | `MqttIngestHostedService`（去重 → 缓存/推送 → 批量写 + 重试队列） |
| `Command` | 反向写值 + 回执 | `ICommandDispatcher`、`MqttCommandClient`、`CommandManager`（超时重试/幂等） |
| `Api` | REST + SignalR + 组合根 | `NitroCloudHub`、Controllers、`IRealtimeNotifier` 实现、HealthChecks、审计中间件 |
| `Telemetry` | 日志/指标/追踪 | Serilog 扩展、Prometheus 指标、Activity 工厂 |

> 元数据初版直接用 EF Core `DbContext`（Api 注入），不额外包仓储接口——低频、单机、非性能关键；`Storage` 只管"时序/告警/命令/最近值"这类跨实现或性能敏感的存储。

### D3 关键接口（初版签名，落码后只增不删）
```csharp
// Storage：时序
public interface ITimeseriesStore
{
    Task WriteAsync(IReadOnlyList<MeasurementRecord> records, CancellationToken ct);
    Task<IReadOnlyList<MeasurementPoint>> QueryAsync(TimeseriesQuery q, CancellationToken ct);
}

// Storage：最近值缓存（内存实现，供 Api/大屏读实时值）
public interface ILatestValueCache
{
    void Update(IReadOnlyList<MeasurementRecord> records);
    IReadOnlyList<LatestValue> GetSite(string siteId);
    LatestValue? GetPoint(string siteId, string deviceId, string devicePointId);
}

// Storage：告警（SQLite 实现）
public interface IAlarmStore
{
    Task AddAsync(AlarmRecord alarm, CancellationToken ct);
    Task<IReadOnlyList<AlarmRecord>> QueryAsync(AlarmQuery q, CancellationToken ct);
    Task AckAsync(string alarmId, string ackBy, CancellationToken ct);
}

// Storage：命令状态机持久化（SQLite 实现）
public interface ICommandStore
{
    Task AddAsync(CommandRecord cmd, CancellationToken ct);
    Task UpdateStatusAsync(Guid commandId, CommandStatus status, string? error, CancellationToken ct);
    Task<CommandRecord?> GetAsync(Guid commandId, CancellationToken ct);
}

// Storage：实时推送（Api 用 IHubContext 实现，Ingest/Command 只依赖此接口）
public interface IRealtimeNotifier
{
    Task NotifyMeasurementsAsync(string siteId, IReadOnlyList<MeasurementRecord> records, CancellationToken ct);
    Task NotifyAlarmAsync(AlarmRecord alarm, CancellationToken ct);
    Task NotifyDeviceStatusAsync(string siteId, DeviceStatus status, CancellationToken ct);
    Task NotifyCommandAckAsync(CommandAck ack, CancellationToken ct);
}
```

### D4 数据流
上行（测量/告警）：
```
NitroGateway --MQTT QoS1--> EMQX --subscribe--> Ingest
  → 解析/校验（topic 第三段 siteId 与 payload 冗余校验；无 v 字段按 v1 解析）
  → 去重（batchId 内存窗口，TTL 默认 60s，可配）
  → ① 更新 ILatestValueCache + IRealtimeNotifier 推 OnMeasurements（实时优先）
  → ② ITimeseriesStore.WriteAsync 批量写 InfluxDB（失败进内存重试队列，有上限，满则丢最旧 + 指标）
  → 告警 topic → IAlarmStore.Add + OnAlarm 推送
```
下行（反向写值）：
```
Api POST /api/commands/write → ICommandStore.Add(Pending) → ICommandDispatcher 发布 commands topic
  → 网关执行 → commands/ack 回执 → CommandManager 收执 → 状态 Acked/Failed → OnCommandAck
  → 超时（默认 10s）重试（上限 3 次，不换 commandId，网关侧按 commandId 去重），超上限标记 Timeout
```

### D5 Ingest 管线要点
- 订阅：`nitrogateway/+/+/measurements`、`nitrogateway/+/+/alarms`（QoS1），通配天然多站点。
- 去重：内存 `ConcurrentDictionary<batchId, DateTime>`，TTL 后惰性清理。
- 批量写：攒批 flush（初版 200 条 / 1s，可配），InfluxDB 批量写入。
- 重试队列：有上限内存队列（初版 10_000 批次），指数退避；写失败/队列满记指标 `ingest_retry_queue_depth` / `ingest_dropped_total`。
- 实时与持久解耦：解析通过即先更新缓存 + 推送（面板新鲜），InfluxDB 写异步重试（持久可靠）；两者短暂不一致可接受（演示），用指标暴露。

### D6 反向写值（Command）
- 幂等键 `commandId`；状态机 `Pending → Sent → Acked/Failed/Timeout`。
- 重试不换 `commandId`（网关侧去重）；命令记录落 SQLite（审计 + 查询）。
- 网关离线时命令保留 `Pending`，可人工重发。
- 契约：`nitrogateway/{siteId}/{deviceId}/commands` + `commands/ack`（DESIGN.md §4.3，需网关补订阅处理器）。

### D7 API 初版清单
| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/sites` | 站点列表（含在线状态/最后上报） |
| POST/PUT/DELETE | `/api/sites[/{id}]` | 站点管理 |
| GET | `/api/sites/{siteId}/devices` | 设备列表 |
| GET | `/api/devices/{id}/points` | 点位列表 |
| GET | `/api/sites/{siteId}/latest` | 最近值（`ILatestValueCache`） |
| GET | `/api/history` | 时序查询（`ITimeseriesStore`） |
| GET | `/api/history/export` | CSV 导出 |
| GET | `/api/alarms` | 告警查询（按站点/级别/状态过滤） |
| POST | `/api/alarms/{id}/ack` | 告警确认 |
| POST | `/api/commands/write` | 发起反向写值 |
| GET | `/api/commands/{id}` | 命令状态 |
| GET | `/healthz` `/metrics` | 健康 / 指标 |

SignalR Hub `NitroCloudHub`：入站 `SubscribeSite(siteId)/UnsubscribeSite`、`JoinGlobal`；出站 `OnMeasurements` `OnAlarm` `OnDeviceStatus` `OnCommandAck`（按 `site:{siteId}` 分组）。

### D8 可观测性
- Serilog：控制台 + 文件（复用网关模式）。
- Prometheus 指标（ASP.NET Core metrics `/metrics`）：ingest 吞吐/延迟/队列深度/丢弃、SignalR 连接数、命令 sent/ack/timeout、MQTT 重连次数。
- `Activity`：Ingest 批次链路、命令闭环链路。

## 载荷墙
- `Storage` 接口只增不删；`Domain` 不引用基础设施。
- 时序只进 InfluxDB（bucket `nitrocloud`/measurement `device_point`）；元数据/告警/命令落 SQLite，结构变更走 FluentMigrator，不手动改库。
- 契约以网关侧为准；命令契约需网关补订阅处理器（小改动，可接受）。
- 6 条 DoD 为硬边界，不扩大范围；依赖包不升级/降级。

## 待验证 / 开放问题
- [ ] 缓存/推送与 DB 写解耦后，DB 写失败但已推送的短暂不一致是否可接受（初版接受 + 指标）。
- [ ] 去重 TTL（60s？）与批量参数（200/1s）待压测校准。
- [ ] 重试队列满的降级策略（丢最旧 vs 阻塞）——初版选丢最旧 + 指标。
- [ ] 元数据直接用 EF Core 是否需要补仓储接口（低频，先不加）。

## 变更记录
- 2026-08-23 新建，状态：草案待评审（M1 开工前评审，评审后同步 DESIGN.md）。
