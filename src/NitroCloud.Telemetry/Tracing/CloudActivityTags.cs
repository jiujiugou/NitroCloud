namespace NitroCloud.Telemetry.Tracing;

/// <summary>Activity 标签名常量，便于索引与日志关联</summary>
public static class CloudActivityTags
{
    /// <summary>批次 ID（Guid）</summary>
    public const string BatchId = "batch.id";

    /// <summary>站点 ID</summary>
    public const string SiteId = "site.id";

    /// <summary>批次内记录数</summary>
    public const string RecordCount = "ingest.record_count";

    /// <summary>命令 ID（Guid）</summary>
    public const string CommandId = "command.id";

    /// <summary>命令状态</summary>
    public const string CommandStatus = "command.status";
}
