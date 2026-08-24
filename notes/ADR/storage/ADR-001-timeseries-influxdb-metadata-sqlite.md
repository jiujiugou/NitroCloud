# ADR-001：时序用 InfluxDB，元数据用 SQLite
- 问题：测量数据高频写入 + 聚合查询，SQLite 不适合；站点/设备/点位元数据低频变更。
- 可选方案：A. 全 SQLite（时序与元数据同库）；B. 全 InfluxDB；C. InfluxDB 存时序 + SQLite 存元数据。
- 决定：C——时序高写入/压缩/聚合是 InfluxDB 强项，元数据低频走 SQLite 零外部依赖。
- ⚠️ 载荷墙：时序数据只进 InfluxDB bucket `nitrocloud`（measurement `device_point`，tag=siteId/deviceId/devicePointId/pointName/quality）；SQLite 只放元数据；表结构变更走 FluentMigrator；Storage 接口只增不删。
- 变更记录：2026-08-23 自 DESIGN.md C-001 转正，状态：草案待评审（DESIGN.md v0.1）。
