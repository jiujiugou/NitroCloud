# NitroCloud 设备上云中心平台

![CI/CD](https://github.com/jiujiugou/NitroCloud/actions/workflows/ci.yml/badge.svg)

订阅 N 个 NitroGateway 边缘网关的 MQTT 上行数据 → InfluxDB 时序存储 → Vue 3 实时大屏/管理面板 → 云端告警汇总 → 反向写值闭环（云 → 网关 → PLC，带回执）。

技术栈：.NET 10 / ASP.NET Core · EMQX(MQTT) · InfluxDB 2.x · SQLite(EF Core) · SignalR · Vue 3 + Element Plus + ECharts · Docker Compose。设计基线见 [`DESIGN.md`](DESIGN.md)（v0.1 草案待评审），认识/设计文档见 [`docs/`](docs/README.md)，决策档案见 [`notes/ADR/`](notes/ADR/README.md)。

---

## 一行命令部署（DoD 6）

云服务器（Ubuntu/Debian，已装 Docker）：

```bash
git clone https://github.com/jiujiugou/NitroCloud.git && cd NitroCloud && bash deploy.sh
```

启动后访问：

| 入口 | 地址 |
| --- | --- |
| 大屏/管理面板 | `http://<服务器IP>:8088` |
| center 健康检查 | `http://<服务器IP>:5100/healthz` |
| center 指标 | `http://<服务器IP>:5100/metrics` |
| EMQX 控制台 | `http://<服务器IP>:18083`（admin / admin123456） |
| InfluxDB UI | `http://<服务器IP>:8086`（admin / admin123456） |

带模拟网关（DoD 1/6 演示数据源，云上无 .NET SDK 也能跑全链路）：

```bash
bash deploy.sh --demo
```

> 首次运行自动从 `.env.example` 生成 `.env`。生产环境**务必先编辑 `.env` 改掉默认口令/Token** 再执行；InfluxDB 凭据首次启动即固化，改动需 `docker compose down -v` 重建卷。默认口令仅限演示，详见 [`docs/20-部署与演示.md`](docs/20-部署与演示.md)。

## 本地快速体验

```bash
# 仅基础服务（broker + InfluxDB），供本地 dev 联调
docker compose up -d

# 全链路（含 center + web 容器）
docker compose --profile full up -d --build

# 全链路 + 模拟网关
docker compose --profile demo up -d --build

# 清理（含数据卷）
docker compose down -v
```

前端本地开发（Vite 热更，代理 `/api`、`/hubs` → 127.0.0.1:5100）：

```bash
cd web && npm ci && npm run dev     # http://localhost:5173
```

## 架构与数据流

```
[NitroGateway / 模拟网关] ──MQTT QoS1──▶ [EMQX broker]
                                             │ subscribe
                                ┌────────────▼────────────┐
                                │ center (NitroCloud.Api)  │
                                │  Ingest 解析/去重/批量写   │
                                └───┬────────────────┬─────┘
                               write │          最近值缓存
                          ┌──────────▼────┐          │ SignalR
                          │   InfluxDB    │◄─────────┘
                          │  时序存储      │
                          └──────────┬────┘
                                     │ REST /api
                          ┌──────────▼────┐
                          │ web (Vue3)     │  nginx 同源反代 /api、/hubs
                          │ 大屏/管理面板    │
                          └────────────────┘
```

- 上行：`nitrogateway/+/+/…` 通配订阅 → 解析/校验/去重 → 批量写 InfluxDB（measurement `device_point`）→ 最近值缓存 → SignalR 推送大屏
- 下行：命令 topic → 网关写值 → 回执闭环（**Command 模块未实现**，见下方「现状」）
- 元数据（站点/设备/点位/告警）落 SQLite，时序只进 InfluxDB（不落 SQLite）

## 模块

| 模块 | 职责 | 路径 |
| --- | --- | --- |
| Domain | 站点/设备/点位/告警领域模型（纯 C#） | `src/NitroCloud.Domain` |
| Shared | OperationResult / 时间 / topic 工具 | `src/NitroCloud.Shared` |
| Storage | 时序/告警/最近值/推送纯接口（只增不删） | `src/NitroCloud.Storage` |
| Persistence | SQLite 元数据 + FluentMigrator 迁移 | `src/NitroCloud.Persistence` |
| Influx | InfluxDB 批量写入 + Flux 查询封装 | `src/NitroCloud.Influx` |
| Ingest | MQTT 订阅 + 解析 + 写 InfluxDB + 缓存（HostedService） | `src/NitroCloud.Ingest` |
| Api | REST API + SignalR + 健康检查 + 组合根 | `src/NitroCloud.Api` |
| Telemetry | Prometheus /metrics + Serilog + Activity | `src/NitroCloud.Telemetry` |
| web | Vue 3 大屏 + 管理面板 | `web/` |
| tools/GatewaySim | 模拟网关（演示数据源 + 命令回执） | `tools/NitroCloud.GatewaySim` |
| tests | UnitTests + IntegrationTests | `tests/` |

## 现状

- M1 骨架已落地：8 个后端模块 + Vue 3 前端 + 单元测试；`docker compose --profile full/demo` 可一条命令拉起全链路（DoD 1/2/3/4/6 可演示）。
- **DoD 5（反向写值闭环）已完成**：云侧 `Command` 模块 + `POST /api/commands/write` + 回执闭环已落地（ADR-010）。端到端回执联调依赖 NitroGateway 命令处理器（已落地，ADR-069）/ mqtt-simulator 回执模拟（待补）。
- 无登录鉴权（初版不涉及）；CORS 仅放行 dev 源，生产走 nginx 同源反代（已就绪）。

## CI/CD（GitHub Actions + GHCR，ADR-011）

流水线定义在 `.github/workflows/ci.yml`，单文件统一入口：

- **CI（每次 push / PR）**：`build-server`（后端 build + 全量单测）+ `build-web`（vue-tsc 类型检查 + vite build）+ `validate-compose`（校验 `docker-compose.yml` 与 `docker-compose.cd.yml` 合并形态）。
- **CD（仅 push master / `v*` tag 且 CI 全绿）**：`build-images` 用 Buildx 构建 `center`（`src/NitroCloud.Api/Dockerfile`）与 `web`（`web/Dockerfile`），推送到 `ghcr.io/jiujiugou/nitrocloud-{center,web}`。
- **镜像 tag 策略**：`master` → `latest` + `sha-<7>`；`vX.Y.Z` tag → `vX.Y.Z` + `sha-<7>`（tag 即版本，发布可追溯）。

部署机从 GHCR 拉取发布产物（不再现场构建，消除「本地能跑、部署机编译环境不一致」风险）：

```bash
docker compose -f docker-compose.yml -f docker-compose.cd.yml pull
docker compose -f docker-compose.yml -f docker-compose.cd.yml up -d
```

`docker-compose.cd.yml` 仅覆盖镜像来源（`image:` + `build: !reset` + `pull_policy: always`），broker/influx/端口/卷/环境变量仍由 `docker-compose.yml` 定义；本地开发仍 `docker compose up -d`。

## 开发约定

- 入场纪律见 [`AGENTS.md`](AGENTS.md)（雷区、轻量规则、DoD 硬边界）。
- 改设计先走 `notes/ADR/` 拍板 → 同步 `DESIGN.md` → 再落代码（单向依赖）。
- 库结构变更走 FluentMigrator 迁移，不手动改库；`*.db` 运行时文件不提交。
