# NitroCloud.Influx

InfluxDB 时序存储实现（bucket `nitrocloud` / measurement `device_point`）。

- `InfluxTimeseriesStore`：实现 `ITimeseriesStore`，批量写入 + Flux 查询
- `BatchWriter`：攒批 flush 批量写入
- `FluxQueryBuilder`：Flux 查询语句封装
