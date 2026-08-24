namespace NitroCloud.Telemetry.Tracing;

/// <summary>
/// Activity 名称常量（ADR-008 D8：Ingest 批次链路、命令闭环链路）。
/// 与网关 Telemetry 命名风格一致（GatewayActivities）。
/// </summary>
public static class CloudActivities
{
    /// <summary>Ingest 单批测量处理链路（解析 → 缓存/推送 → 写库）</summary>
    public const string IngestBatch = "cloud.ingest.batch";

    /// <summary>命令发布链路（API 发起 → MQTT 发布 → 回执）</summary>
    public const string CommandPublish = "cloud.command.publish";

    /// <summary>命令回执处理链路</summary>
    public const string CommandAck = "cloud.command.ack";
}
