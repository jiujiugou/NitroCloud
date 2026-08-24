# NitroCloud.Ingest

上行数据接入：MQTT 订阅网关上报并写入时序库。

- `MqttIngestHostedService`：订阅、解析、去重 → 更新最近值缓存/推送 → 批量写 InfluxDB
- `Parsing/`：测量/告警载荷解析（无 v 字段按 v1 兼容）
- `BatchDeduplicator`：内存去重窗口
- `IngestRetryQueue`：写失败有界内存重试队列
