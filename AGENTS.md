# AGENTS.md — NitroCloud

## 角色定位（最高优先级，先读这条）
- 本 agent 的核心职责是**规划项目的实施原则**（怎么把活干对的做事规则：契约先行、小步可验证、红绿对照、收尾全绿、决策留档、范围硬边界），**不是盘点项目**。
- **禁止默认盘点**：未经用户明确指示，不遍历仓库、不盘点文件/模块/测试/提交/ADR 现状、不输出项目状态报告。
- 用户问「之后呢 / 下一步 / 怎么干」时，默认给出**实施原则层面的规划**，而不是任务清单或现状分析。
- 需要项目具体信息时，先问用户；用户明确要求后再查。

## 项目
设备上云中心平台（.NET 10）：订阅 N 个 NitroGateway 边缘网关的 MQTT 上行数据 → InfluxDB 时序存储 → Vue 3 实时大屏/管理面板 → 云端告警汇总 → 反向写值闭环（云 → 网关 → PLC，带回执）。

- 状态: **未开工**——当前仅 `DESIGN.md`（设计草案 v0.1，待评审），本文件为入场纪律基线；构建/测试/入口命令待 M1 骨架落地后回填
- 上游: NitroGateway（边缘采集+转发，`D:\Code\NitroGateway`），topic/载荷契约以网关侧为准
- 技术栈（设计选定）: .NET 10 / ASP.NET Core / EMQX(mosquitto) / InfluxDB 2.x / SQLite(EF Core + FluentMigrator) / SignalR / Vue 3 + Element Plus + ECharts / Docker Compose
- 设计基线: 仓库根 `DESIGN.md`（范围、DoD、架构、契约、领域模型、模块划分、里程碑）；`docs/` 为认识/设计文档，`notes/ADR/` 为决策档案

## 模块（规划，来自 DESIGN.md §6；开工后按实际路径回填）
| 模块 | 职责 | 路径（规划） |
| --- | --- | --- |
| Domain | 站点/设备/点位/告警 领域模型（纯 C#，不引用基础设施） | src/NitroCloud.Domain |
| Shared | OperationResult / 时间工具 | src/NitroCloud.Shared |
| Ingest | MQTT 订阅 + 解析 + 写 InfluxDB + 最近值缓存（HostedService） | src/NitroCloud.Ingest |
| Storage | 时序/告警存储纯接口（接口只增不删） | src/NitroCloud.Storage |
| Persistence | SQLite 元数据 + FluentMigrator 迁移 | src/NitroCloud.Persistence |
| Influx | InfluxDB 实现（批量写入、查询封装） | src/NitroCloud.Influx |
| Api | REST API + SignalR + 健康检查 + 审计 | src/NitroCloud.Api |
| Command | 命令下发 + 回执（MQTT client） | src/NitroCloud.Command |
| Telemetry | Prometheus + Serilog + Activity | src/NitroCloud.Telemetry |
| web/ | Vue 3 大屏 + 管理面板 | web/src |
| tests/ | UnitTests + IntegrationTests | tests/ |

## 雷区（不要违反）
- 未开工前不创建 src/web 代码结构；设计改动先走 `notes/ADR/` 拍板，再同步 `DESIGN.md`，再落代码（单向依赖：设计 → ADR → 代码）
- `Storage/` 只放纯接口，接口只增不删；`Domain/` 不引用基础设施
- 库结构变更走 FluentMigrator 迁移，不手动改库；`*.db`/`*.db-shm`/`*.db-wal` 运行时文件不提交、不手动编辑
- 时序数据只进 InfluxDB（bucket `nitrocloud`，measurement `device_point`，tag 见 DESIGN.md §5.2），不落 SQLite
- 上行/下行契约以 NitroGateway 侧为准，云侧不单方面改契约（命令契约需网关补处理器，属可接受小改动）
- 不升级/降级依赖包，除非用户明确要求
- `bin/`、`obj/`、`logs/`、`node_modules/`、`dist/` 不修改、不提交
- 以 DESIGN.md §1.4 的 6 条 DoD 为硬边界，不扩大范围

## 轻量规则
1. 动手前三问（对话内一句话，不建文档）：为什么做 / 验收标准是什么 / 不做会怎样
2. G1 确认：破坏性操作、接口/数据模型变更、依赖版本、行为变更、安全相关，先一句话说明再动手；其余直接做
3. 验证：改动附测试，关键逻辑红绿对照，收尾跑构建 + 全量测试
4. 记忆在 notes/：结论与当前目标写 `notes/worklog/YYYY-MM-DD.md`（当前目标段放最近日期文件头部），决策/扫描/排查问题写 `notes/ADR/`（按模块归入对应文件夹，见 `notes/ADR/README.md`），不建 spec/plan/tasks 文档
5. ADR 按模块归入 `notes/ADR/<模块>/ADR-NNN-标题.md`（模块名以模块表为准；跨模块 → architecture/，CI/CD/运维 → deploy/，性能/数据量 → performance/，杂项 → misc/），一屏内；问题 + 代码位置 + 修复方向；修完从 ADR 删除该条；网上搜得到的通用知识不记
6. git 提交/推送默认由用户执行；用户明确指示时 AI 可代执行，执行后须在 `notes/worklog/YYYY-MM-DD.md` 记录提交哈希、分支/远程与结果
7. 详情写注释：类/方法/属性的细节（含义、默认值、边界、设计意图）写进代码 XML 注释、随代码维护
