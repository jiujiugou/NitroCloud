namespace NitroCloud.Domain.Commands;

/// <summary>
/// 命令记录（云 → 网关反向写值，DESIGN.md §4.3 + ADR-008 D6）。
/// 幂等键 = CommandId；落 SQLite（审计 + 查询）；状态机见 <see cref="CommandStatus"/>。
/// </summary>
public sealed class CommandRecord
{
    /// <summary>命令唯一标识（幂等键，重试不更换）</summary>
    public Guid CommandId { get; init; } = Guid.NewGuid();

    /// <summary>命令类型（当前仅 WritePoint）</summary>
    public string Type { get; set; } = "WritePoint";

    /// <summary>所属站点</summary>
    public required string SiteId { get; init; }

    /// <summary>目标设备 ID</summary>
    public required string DeviceId { get; init; }

    /// <summary>目标点位 ID</summary>
    public required string PointId { get; init; }

    /// <summary>写入值（数值型）</summary>
    public double Value { get; init; }

    /// <summary>发起时间（UTC）</summary>
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

    /// <summary>当前状态</summary>
    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    /// <summary>最近一次失败/超时原因</summary>
    public string? Error { get; set; }

    /// <summary>已重试次数</summary>
    public int Attempts { get; set; }

    /// <summary>首次发布成功时间（UTC，null = 尚未发布）</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>收到回执时间（UTC，null = 未回执）</summary>
    public DateTime? AckedAt { get; set; }

    /// <summary>是否已进入最终状态（Acked/Failed/Timeout）</summary>
    public bool IsFinal => Status is CommandStatus.Acked or CommandStatus.Failed or CommandStatus.Timeout;
}
