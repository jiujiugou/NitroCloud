# ADR-016：上行载荷携带点位权限，云端回填可写性并展示

- 日期：2026-08-29
- 模块：architecture（跨网关契约 / Ingest / Persistence / web）

## 问题
- 自动注册点位（ADR-013）云侧 `Writable` 恒为 false：即使网关已把点位配成「读写」，大屏也无写值按钮，回写闭环（DoD 5）悬空。
- ADR-013 原约束「不新增/不修改网关契约」导致上行载荷没有权限信息可依。

## 决策
- 网关上行载荷 records[] 携带 `access`（三态：ReadOnly=0 / WriteOnly=1 / ReadWrite=2，序列化为数字；JsonMessageSerializer 无枚举转换器）。
- 云端解析：`MeasurementRecord.Access`（缺省 ReadOnly，旧版载荷向后兼容；`JsonStringEnumConverter` 数字/字符串双兼容）。
- 自动注册：`Access is ReadWrite or WriteOnly → PointEntity.Writable = true`（复用既有 `writable` 列，**不新增迁移**）。
- 云侧数据模型保持「Writable 布尔」两态：WriteOnly 并入可写，控制器已有同款口径（「初版无 WriteOnly」）；三态完整化留待真正需要时再走迁移。
- 大屏点位行显示权限徽标（只读/读写）；写值按钮条件 = access ≠ ReadOnly。

## 载荷墙
- 网关侧：`NitroGateway.Domain.Devices.PointAccess`；`PointValuePipeline`/`DataDispatcher` 透传；JSON `access` 为数字。
- 云侧：`NitroCloud.Domain.Devices.PointAccess`（值对齐网关）；`MeasurementBatchParser.Options`（JsonStringEnumConverter）；`MetadataStore` 回填；`ApiMappings.ToDto`（Writable → ReadWrite/ReadOnly）；`web PointRow.vue`。

## 不做
- 不改写值/回执契约（命令 topic + 回执不变，ADR-003/ADR-010）。
- 已注册点位不回填（保持 first-seen 语义：已存在行不改写，避免覆盖管理端手动修改；旧点位需管理端改权限或删除后重新注册）。
