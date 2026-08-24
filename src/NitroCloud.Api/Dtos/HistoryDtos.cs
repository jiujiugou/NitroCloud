namespace NitroCloud.Api.Dtos;

/// <summary>
/// 时序查询返回的单点 DTO（前端 web/src/api/types.ts 的 PointSnapshot 对齐）。
/// 由 Storage.Models.MeasurementPoint（Time 属性）映射而来，Time → Timestamp。
/// </summary>
public sealed record PointSnapshotDto
{
    /// <summary>所属站点</summary>
    public required string SiteId { get; init; }

    /// <summary>所属设备</summary>
    public required string DeviceId { get; init; }

    /// <summary>所属点位</summary>
    public required string DevicePointId { get; init; }

    /// <summary>数值</summary>
    public double Value { get; init; }

    /// <summary>质量标签（Good/Uncertain/Bad）</summary>
    public required string Quality { get; init; }

    /// <summary>采集时间（UTC，O 格式）</summary>
    public required string Timestamp { get; init; }
}

/// <summary>时序查询请求参数（GET /api/history、/api/history/export）</summary>
public sealed record HistoryQueryDto
{
    /// <summary>站点（必填，第一隔离维度，ADR-004）</summary>
    public string? SiteId { get; init; }

    /// <summary>设备（可选过滤）</summary>
    public string? DeviceId { get; init; }

    /// <summary>点位（可选过滤）</summary>
    public string? DevicePointId { get; init; }

    /// <summary>起始时间（ISO 8601，可选，默认 24h 前）</summary>
    public string? From { get; init; }

    /// <summary>结束时间（ISO 8601，可选，默认当前）</summary>
    public string? To { get; init; }

    /// <summary>返回上限（可选，实现夹紧到 [1,5000]）</summary>
    public int? Limit { get; init; }
}
