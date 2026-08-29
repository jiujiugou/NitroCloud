namespace NitroCloud.Persistence.Entities;

/// <summary>
/// command_records 表实体（EF Core 映射到 <see cref="Domain.Commands.CommandRecord"/>）。
/// 时间列统一存 O 格式 UTC 字符串（字典序即时间序，与网关/alarm_records 一致）。
/// </summary>
public sealed class CommandRecordEntity
{
    /// <summary>命令唯一标识（幂等键，Guid 字符串）</summary>
    public string CommandId { get; set; } = "";

    /// <summary>命令类型（当前仅 WritePoint）</summary>
    public string Type { get; set; } = "WritePoint";

    /// <summary>所属站点</summary>
    public string SiteId { get; set; } = "";

    /// <summary>目标设备 ID</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>目标点位 ID</summary>
    public string PointId { get; set; } = "";

    /// <summary>写入值（数值型）</summary>
    public double Value { get; set; }

    /// <summary>发起人（登录用户名，审计用，ADR-015）；存量命令为 null</summary>
    public string? RequestedBy { get; set; }

    /// <summary>发起时间（O 格式 UTC 字符串）</summary>
    public string RequestedAt { get; set; } = "";

    /// <summary>当前状态（CommandStatus 枚举名）</summary>
    public string Status { get; set; } = nameof(Domain.Commands.CommandStatus.Pending);

    /// <summary>最近一次失败/超时原因</summary>
    public string? Error { get; set; }

    /// <summary>已重试次数</summary>
    public int Attempts { get; set; }

    /// <summary>首次发布成功时间（O 格式 UTC 字符串，null = 尚未发布）</summary>
    public string? SentAt { get; set; }

    /// <summary>收到回执时间（O 格式 UTC 字符串，null = 未回执）</summary>
    public string? AckedAt { get; set; }
}
