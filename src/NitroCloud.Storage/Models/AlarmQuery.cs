namespace NitroCloud.Storage.Models;

/// <summary>
/// 告警查询参数（DESIGN.md §4：云端告警汇总按站点/级别/状态过滤）。
/// </summary>
public sealed record AlarmQuery
{
    /// <summary>站点（可选过滤；null = 全部站点）</summary>
    public string? SiteId { get; init; }

    /// <summary>严重级别（可选过滤）</summary>
    public Domain.Alarms.AlarmSeverity? Severity { get; init; }

    /// <summary>状态（可选过滤）</summary>
    public Domain.Alarms.AlarmState? State { get; init; }

    /// <summary>起始时间（可选）</summary>
    public DateTime? From { get; init; }

    /// <summary>结束时间（可选）</summary>
    public DateTime? To { get; init; }

    /// <summary>分页参数（默认 50）</summary>
    public int Limit { get; init; } = 50;

    /// <summary>分页偏移（默认 0）</summary>
    public int Offset { get; init; }
}
