namespace NitroCloud.Storage.Models;

/// <summary>
/// 时序查询参数（ADR-004：siteId 为强制过滤维度）。
/// deviceId/devicePointId 可选；缺省查站点下全部。
/// </summary>
public sealed record TimeseriesQuery
{
    /// <summary>站点（必填，第一隔离维度）</summary>
    public required string SiteId { get; init; }

    /// <summary>设备（可选过滤）</summary>
    public string? DeviceId { get; init; }

    /// <summary>点位（可选过滤）</summary>
    public string? DevicePointId { get; init; }

    /// <summary>起始时间（UTC，含）</summary>
    public DateTime From { get; init; }

    /// <summary>结束时间（UTC，含）</summary>
    public DateTime To { get; init; }

    /// <summary>返回上限（实现应夹紧到 [1, 5000]），默认 1000</summary>
    public int Limit { get; init; } = 1000;
}
