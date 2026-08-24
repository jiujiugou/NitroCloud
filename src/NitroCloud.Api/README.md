# NitroCloud.Api

唯一宿主：REST API + SignalR 实时推送 + 组合根，装配全部模块。

- `Controllers/`：站点/设备/点位/历史/告警/命令 六组 REST 接口
- `Hubs/NitroCloudHub`：站点级订阅与实时出站事件
- `Realtime/`：`ILatestValueCache` 内存实现、在线状态判定、`IRealtimeNotifier` SignalR 实现
- `Program.cs`：组合根（注入 Persistence/Influx/Ingest/Command），健康检查、`ApiResponse<T>` 外壳
