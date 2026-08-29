# notes/ADR — 决策档案导航

> 纪律见 `AGENTS.md` 轻量规则 5 + adr-governance skill：ADR 是给「改生产的人」看的决策档案，只记「想清楚了、拍板了」的事，通用知识不记；改代码前先查对应模块的载荷墙。
> 本目录下只放本导航文件，ADR 按模块归入子目录 `notes/ADR/<模块>/ADR-NNN-标题.md`。

## 按模块索引

### storage/
- ADR-001 时序用 InfluxDB，元数据用 SQLite —— 已转正（DESIGN.md C-001）｜读

### ingest/
- ADR-002 接入只走 MQTT 订阅，不直连网关 API —— 已转正（C-002）｜读
- ADR-006 Ingest 写失败进内存重试队列（有上限）—— 已转正（C-006）｜读

### command/
- ADR-003 反向控制走命令 topic + 回执 —— 已转正（C-003）｜读
- ADR-010 回写功能（Command 模块）设计：反向写值闭环落地 —— 已落地（云侧代码 + 单测 17/17；端到端联调待网关侧前置）｜读

### architecture/
- ADR-004 siteId 作为第一隔离维度 —— 已转正（C-004）｜读
- ADR-008 后端初版设计（模块/接口/数据流）—— 已拍板、M1 骨架已落地｜读
- ADR-013 测量数据到达时自动注册元数据（站点/设备/点位，替代前端手动创建）—— 已落地（MetadataStore + Ingest 接入 + 集成测试）｜读

### api/
- ADR-005 最近值放内存缓存，实时面板不查库 —— 已转正（C-005）｜读
- ADR-007 离线判定用「最后上报时间 + 阈值」—— 已转正（C-007）｜读

### web/
- ADR-009 前端初版设计（大屏 + 管理面板）—— 已拍板、前端已落地｜读

### deploy/
- ADR-011 CI/CD 流水线（GitHub Actions + GHCR 镜像发布）—— 已拍板、待落码实施｜读

### persistence/
- ADR-012 EF Core 列名统一按 snake_case 映射（对齐 FluentMigrator Schema）—— 已落地（AppDbContext.cs + 集成回归测试）｜读
- ADR-014 alarm_records 时间字段由 string 改 DateTime（复用全局 ValueConverter）—— 已落地（实体/Store/Controller + 集成回归测试）｜读

## 约定
- 新 ADR 按模块归入 `notes/ADR/<模块>/ADR-NNN-标题.md`：模块名以 AGENTS.md 模块表为准；跨模块 → `architecture/`，CI/CD/运维 → `deploy/`，性能/数据量 → `performance/`，杂项 → `misc/`。不存在的模块目录自动创建，不预建空目录。
- ADR 只记决策：问题 + 可选方案 + 决定 + 载荷墙 + 变更记录，一屏内；新增后在对应模块组加一行（一句话 + 状态 + 读不读）。
- 仓库根 `notes/ADR/` 只放本导航文件，不放散落的 ADR。
