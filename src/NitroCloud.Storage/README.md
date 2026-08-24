# NitroCloud.Storage

存储纯接口层（只增不删），定义跨实现或性能敏感的存储契约，具体实现由其他模块提供。

- `ITimeseriesStore`：时序读写（Influx 实现）
- `ILatestValueCache`：最近值缓存（Api 内存实现）
- `IAlarmStore` / `ICommandStore`：告警、命令状态持久化（Persistence 实现）
- `IRealtimeNotifier`：实时推送接口（Api 用 SignalR 实现），解耦 Ingest/Command 与 SignalR
