# docs/ —— NitroCloud 认识与设计文档

> 本目录沉淀「让人 / 让 AI 看懂项目」的文档，不承担运行时职责。设计基线 = 仓库根 [`DESIGN.md`](../DESIGN.md)（v0.1 草案待评审），决策档案 = [`notes/ADR/`](../notes/ADR/)（按模块归档），入场纪律 = [`AGENTS.md`](../AGENTS.md)。

## 现状（2026-08-24）
- 项目已开工，M1 骨架落地并提交：后端 8 模块 + Vue 3 前端 + 单元测试（git `61b1d89`）；Docker 单机部署（git `0dcb236`：`docker-compose.yml`、`.env.example`、`.dockerignore`、center/web Dockerfile、web nginx 同源反代）。
- 组合根 = `src/NitroCloud.Api`（单宿主），Ingest 以 `BackgroundService` 注册进同一宿主；SignalR Hub `/hubs/cloud`；`docker compose up -d --build` 可拉起 broker + influx + center + web 四服务。
- **DoD 5（反向写值闭环）未完成**：`NitroCloud.Command` 项目未实现（`NitroCloud.slnx` 无该项目），compose 中 `Command__*` 为前瞻配置。无登录鉴权（初版不涉及），CORS 仅放行 dev 源，生产走 nginx 同源反代（已就绪）。
- **已知问题**：单元测试 69 通过 / 6 既有失败（`MeasurementPipelineTests` 5 + `__DiagTests` 1），根因 `MeasurementRecord.SiteId` 为 `required` 而上行 `records[]` 单条不带 siteId，见 `notes/worklog/2026-08-23.md`「既有问题」（待单独排期，涉及上行契约/解析器，需 G1 确认）。
- **已知出入（文档先行、代码未落地）**：根 [`README.md`](../README.md) 与本目录 [`20-部署与演示.md`](20-部署与演示.md) 描述的 `deploy.sh`、`docker compose --profile full/demo`、`sim` 服务、`tools/NitroCloud.GatewaySim` 尚未与仓库一致——`deploy.sh` 未创建、当前 `docker-compose.yml` 无 profile/sim 服务、`tools/GatewaySim` 已从工作区删除（仍被 git 跟踪）。阅读时以仓库实际为准，落地后再同步本文档。

## 文档索引（编号即命名，不预建空文件）

| 编号 | 内容 | 状态 |
| --- | --- | --- |
| 01-盘点.md | 接手审查：技术栈、目录结构、怎么跑（M1 骨架后回填） | 待建 |
| 02-架构与数据流.md | 模块边界、数据流（MQTT → Ingest → InfluxDB → SignalR → 大屏 / 命令闭环） | 待建 |
| 03-功能清单.md | 已有功能 + 代码位置 + 触发方式 | 待建 |
| 04-疑点清单.md | 待验证疑点，验证后回填结论 | 待建 |
| 10-数据契约.md | 上行/下行/告警 topic 与载荷（以网关侧为准，暂见 DESIGN.md §4） | 待建 |
| 20-部署与演示.md | Docker Compose 单机拉起 + 演示脚本 + 运维 FAQ | 已建（2026-08-24；含待补齐项，见上） |
| 30-面试.md | 项目讲法、Top 决策、常见追问 | 待建 |

## 约定
- 文档以「记忆在文档，不在聊天」为原则：每轮会话结论写进 docs/ 对应条目或 `notes/worklog/`
- 功能/设计结论必须标注代码位置；未经验证的写成「待验证」，不写成结论
- 设计基线改动顺序：先 `notes/ADR/` 拍板 → 再同步 `DESIGN.md` → 再落代码
