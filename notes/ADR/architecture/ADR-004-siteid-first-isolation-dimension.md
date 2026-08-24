# ADR-004：siteId 作为第一隔离维度
- 问题：多现场多网关数据如何路由 / 隔离 / 归属？
- 可选方案：A. 仅 deviceId 全局唯一；B. siteId 第一维度 + deviceId；C. 全 topic 路径解析。
- 决定：B——siteId 第一隔离维度，topic 第三段即 siteId，载荷内 siteId 作冗余校验。
- ⚠️ 载荷墙：所有查询/告警/命令以 siteId 为强制过滤维度；siteId 与载荷不一致记告警，不静默丢弃；InfluxDB tag 必带 siteId。
- 变更记录：2026-08-23 自 DESIGN.md C-004 转正，状态：草案待评审（DESIGN.md v0.1）。
