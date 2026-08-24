namespace NitroCloud.Domain.Alarms;

/// <summary>
/// 云端告警汇总记录（DESIGN.md §4.2 上行告警载荷 + §5 领域模型）。
/// 落 SQLite（alarm_records）；网关可能重复推送同一告警，落库时按 alarmId 幂等 upsert。
/// </summary>
public sealed class AlarmRecord
{
    /// <summary>告警唯一标识（网关生成 alarmId）</summary>
    public required string Id { get; init; }

    /// <summary>触发告警的规则 ID</summary>
    public required string RuleId { get; init; }

    /// <summary>所属站点（topic 第三段）</summary>
    public required string SiteId { get; init; }

    /// <summary>所属设备 ID</summary>
    public required string DeviceId { get; init; }

    /// <summary>所属点位 ID</summary>
    public required string PointId { get; init; }

    /// <summary>触发时的值</summary>
    public double TriggerValue { get; set; }

    /// <summary>规则阈值</summary>
    public double Threshold { get; set; }

    /// <summary>告警严重等级</summary>
    public AlarmSeverity Severity { get; set; }

    /// <summary>告警消息</summary>
    public string Message { get; set; } = "";

    /// <summary>当前生命周期状态</summary>
    public AlarmState State { get; set; } = AlarmState.Active;

    /// <summary>告警发生时间（UTC）</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>云端确认时间（UTC，null = 未确认）</summary>
    public DateTime? AckedAt { get; set; }
}
