using System.Text.Json.Serialization;

namespace NitroCloud.Ingest.Parsing;

/// <summary>
/// 上行告警载荷（DESIGN.md §4.2 字段，camelCase JSON，以网关侧为准）。
/// 由网关在本地完成阈值评估后上行的告警记录（非云端计算）。字段与 NitroGateway 告警契约对齐，
/// 云侧只做忠实反序列化，不做业务推断。
///
/// 注意：站点标识（siteId）不在此载荷中，由调用方按 topic 第三段提取（ADR-004），
/// 因此本类型不含 SiteId 属性，避免与 topic 事实来源产生二义。
/// </summary>
public sealed record AlarmPayload
{
    /// <summary>告警唯一 ID（网关生成，用于幂等/查重；缺失时解析判失败）</summary>
    [JsonPropertyName("alarmId")]
    public string AlarmId { get; init; } = "";

    /// <summary>触发该告警的规则 ID（网关侧告警规则标识）</summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; init; } = "";

    /// <summary>告警所属设备 ID（Guid 字符串形式）</summary>
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; init; } = "";

    /// <summary>告警所属点位 ID（Guid 字符串形式）</summary>
    [JsonPropertyName("pointId")]
    public string PointId { get; init; } = "";

    /// <summary>触发告警时的点位值（数值；触发时刻的采集快照）</summary>
    [JsonPropertyName("triggerValue")]
    public double TriggerValue { get; init; }

    /// <summary>触发告警的阈值（规则配置值，与 <see cref="TriggerValue"/> 对比得出告警）</summary>
    [JsonPropertyName("threshold")]
    public double Threshold { get; init; }

    /// <summary>
    /// 严重级别（Info/Warning/Critical/Emergency，字符串形式）。
    /// 反序列化后由调用方映射到 <c>AlarmSeverity</c> 枚举；未知值按 Warning 兜底。
    /// </summary>
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "Warning";

    /// <summary>告警描述消息（人工可读，用于展示/通知）</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    /// <summary>告警状态（Active/Resolved）；Resolved 表示该规则已恢复、告警解除</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "Active";

    /// <summary>告警发生时间（UTC，经 <see cref="UtcDateTimeConverter"/> 归一为 Kind=Utc）</summary>
    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; init; }
}
