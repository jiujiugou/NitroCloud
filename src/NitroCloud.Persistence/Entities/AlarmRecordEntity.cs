namespace NitroCloud.Persistence.Entities;

/// <summary>alarm_records 表实体（EF Core 映射到 <see cref="Domain.Alarms.AlarmRecord"/>）</summary>
public sealed class AlarmRecordEntity
{
    /// <summary>告警 ID（网关 alarmId）</summary>
    public string Id { get; set; } = "";

    /// <summary>触发规则 ID</summary>
    public string RuleId { get; set; } = "";

    /// <summary>所属站点</summary>
    public string SiteId { get; set; } = "";

    /// <summary>所属设备</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>所属点位</summary>
    public string PointId { get; set; } = "";

    /// <summary>触发值</summary>
    public double TriggerValue { get; set; }

    /// <summary>阈值</summary>
    public double Threshold { get; set; }

    /// <summary>严重级别（AlarmSeverity 枚举名）</summary>
    public string Severity { get; set; } = "Warning";

    /// <summary>告警消息</summary>
    public string Message { get; set; } = "";

    /// <summary>状态（AlarmState 枚举名）</summary>
    public string State { get; set; } = "Active";

    /// <summary>发生时间（O 格式 UTC 字符串）</summary>
    public string OccurredAt { get; set; } = "";

    /// <summary>云端确认时间（O 格式 UTC 字符串，null = 未确认）</summary>
    public string? AckedAt { get; set; }
}
