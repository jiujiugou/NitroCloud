# ADR-013：测量数据到达时自动注册元数据（站点/设备/点位）

- 日期：2026-08-29
- 模块：architecture（跨 Ingest / Storage / Persistence）

## 问题
- 线上 SQLite 元数据表（sites/devices/points）为空，前端大屏按站点/设备/点位维度展示时一片空白。
- 现有设计为 M2 手动 CRUD（管理面板/API 创建），但网关测量数据已实时入库（InfluxDB 有 site-web-1 / 23763b14-... / 空压机_H001~H040），手动创建与真实数据对不上、体验差。

## 决策
- Ingest 在 flush 循环每批记录写 InfluxDB 之前，旁路调用新增纯接口 `IMetadataStore.EnsureRegisteredAsync(records)` 幂等注册元数据（站点 → 设备 → 点位），best-effort：失败只记 Warning + 指标，不阻塞时序写入。
- 数据来源只取测量记录已有字段：SiteId（topic 第三段）、DeviceId、DevicePointId、PointName、DataType、Timestamp；站点/设备显示名用 id 兜底（Name=Id），后续可在管理面板完善。不新增、不修改网关契约。
- 幂等与性能：`MetadataStore` 实现为 singleton，内部持进程级 `ConcurrentDictionary` 已注册键缓存（低基数）+ DB 双检，避免每条消息查库；重复插入的唯一键冲突被捕获忽略。
- 开关：`Ingest:AutoRegisterMetadata`（默认 true），可关闭。

## 代码位置与改动
- 新增 `src/NitroCloud.Storage/IMetadataStore.cs`（纯接口，只增不删）。
- 新增 `src/NitroCloud.Persistence/Sqlite/MetadataStore.cs`（EF Core 实现，snake_case 映射复用 AppDbContext）。
- 改 `MqttIngestHostedService.RunFlushLoopAsync`：写时序前注册元数据；`IngestOptions` 加开关；`CloudMetrics` 加注册失败计数。
- 集成测试：临时库验证建行 + 幂等。

## 不做
- 不做网关侧注册 topic（改契约需网关配合，非必要）。
- 不改现有手动 CRUD 能力（两者并存）。
