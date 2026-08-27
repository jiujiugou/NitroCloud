# NitroCloud 设备上云中心平台 — 设计文档

> 状态：草案 v0.1（待评审） ｜ 日期：2026-08-23 ｜ 关联项目：NitroGateway（边缘网关，上游数据源）

---

## 0. 这份文档解决什么

NitroGateway 只覆盖了「边缘采集 + 转发」这一半，缺「云端接入」这另一半。本平台补齐后半段，
两个项目串成一条完整链路：**现场 → 边缘 → 云**。它既是第二个可演示项目，也是面试时讲"我能做设备采集上云"的完整证据。

---

## 1. 背景与目标

### 1.1 为什么做
- 招聘对口的"设备上云 / 物联网平台"公司要的是**整条链路**，不是单一能力。
- 无经验候选人最缺的不是"会写代码"，而是"有一条能跑通的主线"；两个项目同生态、可串讲，价值大于两个不相干项目。
- NitroGateway 的 README 与 ADR-054 已把「中心」明确为**待独立项目**，本平台是既定路线图的落地。

### 1.2 目标
1. 订阅 N 个 NitroGateway 的 MQTT 上行数据，做**多站点设备接入**。
2. **时序存储**（InfluxDB）+ 实时监控大屏 + 历史查询/导出。
3. **云端告警汇总**（所有现场告警汇聚到一处）。
4. **反向控制闭环**：云 → MQTT → 网关 → PLC 写值，带回执。

### 1.3 非目标（明确不做，防止范围膨胀）
- 不做边缘采集、不做工业协议驱动（Modbus/S7/OPC UA 是 NitroGateway 的活）。
- 不做网关本地管理 UI（那是边缘 web 的活）。
- 不做高可用集群/水平扩容（本期单机 Docker 演示优先，架构上预留）。
- 不接真实 PLC/物理设备（演示用模拟网关 + 模拟写值回执）。

### 1.4 验收标准（DoD，全绿才算完）
1. 2 个网关实例接入，`siteId` 能区分现场。
2. 实时数据经 SignalR 推到大屏，秒级刷新。
3. 历史曲线可查、可导出 CSV。
4. 云端告警汇总页面可用（按站点/级别过滤）。
5. 反向写值从云端发起，收到网关回执，闭环打通。✅ 已落地（云侧 Command 模块 + `POST /api/commands/write`；端到端回执联调待 NitroGateway 命令处理器 / mqtt-simulator 回执模拟，见 ADR-010 D8）。
6. 全链路演示脚本写进 README，一条命令可拉起。

---

## 2. 系统架构与边界

### 2.1 总体拓扑

```
 [NitroGateway A / 模拟现场A] ─┐
 [NitroGateway B / 模拟现场B] ─┼─ MQTT QoS1 ─┐   ┌──────────────┐
 [NitroGateway C / 现场真实]  ─┘            └──→│  MQTT Broker  │
                                               └──────┬───────┘
                                                      │ subscribe
                                               ┌──────▼───────┐
                                               │  Ingest 接入  │  解析/校验/去重
                                               └──────┬───────┘
                                            write     │     最近值缓存
                                               ┌──────▼───────┐        │ SignalR
                                               │  InfluxDB    │◄───────┘    │
                                               │  时序存储     │            ▼
                                               └──────┬───────┘   ┌──────────────┐
                                                      │ REST      │  Vue3 大屏    │
                                               ┌──────▼───────┐   │  管理面板     │
                                               │ 中心 WebAPI  │   └──────────────┘
                                               └──────┬───────┘
                                                      │ commands topic (云→网关)
                                               ┌──────▼───────┐
                                               │ NitroGateway │ → PLC 写值 → 回执
                                               └──────────────┘
```

### 2.2 与 NitroGateway 的分工

| 层 | 项目 | 职责 |
| --- | --- | --- |
| 边缘 | NitroGateway | 协议采集、本地缓存、告警评估、MQTT 上行、命令执行 |
| 云 | NitroCloud | 设备接入、时序存储、实时展示、告警汇总、命令下发 |

### 2.3 部署形态
- 开发/演示：Docker Compose 单机（broker + InfluxDB + center + web）。
- 生产（预留）：broker / ingest / api 可拆多机，本期不做。

---

## 3. 技术选型

| 组件 | 选型 | 理由 |
| --- | --- | --- |
| 运行时 | .NET 10 / ASP.NET Core | 与 NitroGateway 同栈，零学习成本 |
| MQTT Broker | EMQX（Docker） | 开源、多站点支持好、自带控制台；本地也可用 mosquitto |
| 时序库 | InfluxDB 2.x（Docker） | 高写入、聚合查询、自动保留策略；简历新关键词 |
| 关系库 | SQLite（EF Core） | 站点/设备/点位/用户等元数据；演示零外部依赖 |
| 实时推送 | SignalR（WebSocket） | 与网关 web 同栈，经验可复用 |
| 前端 | Vue 3 + Element Plus + ECharts | 与网关 web 同栈，大屏组件可直接复用 |
| 指标/日志 | Prometheus + Serilog + Activity | 复用网关 Telemetry 模式 |
| 测试 | xUnit + Testcontainers | InfluxDB/EMQX 可容器化，关键逻辑红绿对照 |

> 决策原则：**新组件只加一个核心（InfluxDB）**，其余全部沿用网关已验证的选型，控制学习成本。

---

## 4. 数据契约（与 NitroGateway 的接口，以网关侧为准）

### 4.1 上行测量 topic
`nitrogateway/{siteId}/{deviceId}/measurements`（QoS 1，JSON camelCase）

载荷（`BatchMeasurements` v1）：
```json
{
  "siteId": "site-1",
  "v": 1,
  "id": "3f2a...",
  "deviceId": "a1b2...",
  "scanStartedAt": "2026-08-23T10:00:00+08:00",
  "scanCompletedAt": "2026-08-23T10:00:00+08:00",
  "records": [
    {
      "id": "r1...",
      "deviceId": "a1b2...",
      "devicePointId": "p1...",
      "pointName": "主轴温度",
      "value": 62.5,
      "dataType": "Float",
      "timestamp": "2026-08-23T10:00:00+08:00",
      "receivedAt": "2026-08-23T10:00:00.123+08:00",
      "quality": "Good"
    }
  ]
}
```

接入规则：
- 以 topic 第三段 `siteId` 为准；载荷内 `siteId` 作**冗余校验**，不一致记告警。
- 载荷带 `v` 版本号（当前 1）；旧版无 `v` 字段按 v1 兼容解析。
- `quality != Good` 的 record 照常入库，但打上质量标签，面板上灰显。

### 4.2 上行告警 topic
`nitrogateway/{siteId}/{deviceId}/alarms`（QoS 1）

载荷字段：`alarmId`、`ruleId`、`deviceId`、`pointId`、`triggerValue`、`threshold`、`severity`、`message`、`state`、`occurredAt`。

### 4.3 下行命令 topic（新增契约，云 → 网关）

命令：`nitrogateway/{siteId}/{deviceId}/commands`（QoS 1）
```json
{
  "commandId": "guid",
  "type": "WritePoint",
  "pointId": "guid",
  "value": 42,
  "requestedAt": "2026-08-23T10:05:00+08:00"
}
```

回执：`nitrogateway/{siteId}/{deviceId}/commands/ack`
```json
{
  "commandId": "guid",
  "result": "Success",
  "error": "",
  "at": "2026-08-23T10:05:00.200+08:00"
}
```

> 注意：命令契约需要 NitroGateway 侧补一个 MQTT 订阅处理器（小改动），属可接受范围。

---

## 5. 领域模型

| 实体 | 归属存储 | 关键字段 |
| --- | --- | --- |
| Site（站点/现场） | SQLite | Id, Name, Location, Status, CreatedAt |
| Device（设备） | SQLite | Id, SiteId, Name, Model, LastSeenAt（在线判定） |
| Point（点位） | SQLite | Id, DeviceId, Name, DataType, Unit, AlarmEnabled |
| Measurement（时序数据） | InfluxDB | 见 5.2 |
| AlarmRecord（告警汇总） | SQLite | Id, SiteId, DeviceId, PointId, Severity, State, OccurredAt, AckedAt |
| User / Role | SQLite | 复用网关 RBAC 思路 |

### 5.1 关系库（SQLite）
- `sites` / `devices` / `points` / `alarm_records` / `users` / `roles`。
- 结构变更走 **FluentMigrator** 迁移（沿用网关惯例），不手动改库。

### 5.2 InfluxDB 设计
- bucket：`nitrocloud`
- measurement：`device_point`
- tag：`siteId`、`deviceId`、`devicePointId`、`pointName`、`quality`
- field：`value`
- timestamp：取 `MeasurementRecord.Timestamp`
- 保留策略：原始数据 30 天，可配置下采样（后续演进）

---

## 6. 模块划分（项目结构）

```
src/
  NitroCloud.Domain/       站点/设备/点位/告警 领域模型（纯 C#，不引用基础设施）
  NitroCloud.Shared/       OperationResult / 时间工具
  NitroCloud.Ingest/       MQTT 订阅 + 解析 + 写 InfluxDB + 最近值缓存（HostedService）
  NitroCloud.Storage/      时序/告警存储纯接口（接口只增不删）
  NitroCloud.Persistence/  SQLite 元数据 + FluentMigrator 迁移
  NitroCloud.Influx/       InfluxDB 实现（批量写入、查询封装）
  NitroCloud.Api/          REST API + SignalR + 健康检查 + 审计
  NitroCloud.Command/      命令下发 + 回执（MQTT client）
  NitroCloud.Telemetry/    Prometheus + Serilog + Activity
web/                       Vue3 大屏 + 管理面板
tests/                     UnitTests + IntegrationTests
```

- 单向依赖、无环引用（沿用网关分层纪律）。
- `Storage/` 只放接口，**接口只增不删**。
- `Domain/` 不引用基础设施。

---

## 7. 关键设计决策（ADR 预览）

| 编号 | 决策 | 理由 |
| --- | --- | --- |
| C-001 | 时序用 InfluxDB，元数据用 SQLite | 高写入 + 聚合查询；元数据低频变更 |
| C-002 | 接入只走 MQTT 订阅，不直连网关 API | 解耦、多网关易扩展、网关可离线 |
| C-003 | 反向控制走命令 topic + 回执 | 异步可靠、可重试、不阻塞 |
| C-004 | `siteId` 作为第一隔离维度 | 多现场路由，topic 第三段即可分片 |
| C-005 | 最近值放内存缓存，实时面板不查库 | 秒级刷新避免打爆时序库 |
| C-006 | Ingest 写失败进内存重试队列（有上限） | 类比网关 ForwardBuffer，防瞬时抖动丢数据 |
| C-007 | 离线判定用「最后上报时间 + 阈值」 | 零侵入，不额外加保活协议 |

---

## 8. 里程碑（2~4 周）

| 里程碑 | 内容 | 验收 |
| --- | --- | --- |
| M1 骨架 | 仓库 + 目录 + Docker Compose（EMQX+InfluxDB）+ MQTT 订阅 + 写库 | 模拟网关数据能进 InfluxDB |
| M2 元数据 | Site/Device/Point 管理 API + SQLite 迁移 | CRUD 可用 |
| M3 实时 | SignalR 推送 + 大屏 + 历史曲线/导出 | 2 台模拟网关实时曲线 |
| M4 告警+控制 | 告警汇总 + 反向写值闭环 + 回执 | 云端改值，网关侧收到并回执 ✅ 云侧已落地（ADR-010）；回执端到端联调待网关侧前置 |
| M5 收尾 | 测试 + 演示脚本 + README | 6 条 DoD 全绿 |

---

## 9. 风险与对策

| 风险 | 对策 |
| --- | --- |
| InfluxDB 学习成本 | 只做写入 + 基础查询，不碰复杂 Flux |
| EMQX 配置复杂 | 本地可用 mosquitto，topic 契约不变 |
| 网关命令契约未实现 | NitroGateway 补一个命令订阅处理器（小改动） |
| 演示依赖网络 | 全 Docker 本地，离线可跑 |
| 范围膨胀 | 以 6 条 DoD 为硬边界，做完就停 |

---

## 10. 面试话术

> 一句话：自研工业物联网全链路——NitroGateway 边缘网关采集 Modbus/S7/OPC UA 数据，MQTT 上云；
> NitroCloud 中心平台做多站点接入、InfluxDB 时序存储、实时大屏、告警汇总和云端反向写值闭环。

可能被问：
- 为什么时序数据不用 SQLite？→ 高频写入 + 聚合查询场景，InfluxDB 压缩比高、查询快；元数据仍用 SQLite。
- 网关离线怎么判断？→ 最后上报时间 + 心跳阈值（C-007）。
- 反向控制怎么保证可靠？→ 命令 topic + 回执 + 超时重试（C-003）。
- 实时面板怎么不卡？→ 最近值内存缓存 + SignalR 推送，查询只落时序库（C-005）。
- 多现场怎么隔离？→ siteId 第一维度，topic 第三段路由（C-004）。

---

## 11. 演进方向（非本期）
- 多租户 + 完整鉴权
- 数据下采样 / 长期归档
- 云端规则引擎下沉
- 第三方对接（微信告警、阿里云 IoT）
