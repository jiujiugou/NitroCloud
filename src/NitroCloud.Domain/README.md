# NitroCloud.Domain

纯 C# 领域模型，不引用任何基础设施，全系统共享的实体与枚举。

- `Sites` / `Devices`：站点、设备、点位元数据模型
- `Alarms`：告警记录与级别/状态枚举
- `Commands`：命令请求、记录、状态、回执模型
- `Measurements`：上行测量契约模型（`BatchMeasurements` / `MeasurementRecord` / `Quality`）
