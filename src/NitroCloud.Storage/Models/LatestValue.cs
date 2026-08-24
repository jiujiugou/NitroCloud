namespace NitroCloud.Storage.Models;

/// <summary>
/// 最近值缓存条目（ADR-005：实时面板不查库，读内存缓存）。
/// Value 保留契约原样（object?），展示时按 DataType 格式化。
/// </summary>
public sealed record LatestValue
{
    /// <summary>所属站点</summary>
    public required string SiteId { get; init; }

    /// <summary>所属设备</summary>
    public required string DeviceId { get; init; }

    /// <summary>所属点位</summary>
    public required string DevicePointId { get; init; }

    /// <summary>点位名称</summary>
    public string PointName { get; init; } = "";

    /// <summary>最近值（契约原样）</summary>
    public object? Value { get; init; }

    /// <summary>数据类型（决定展示格式）</summary>
    public Domain.Devices.DataType DataType { get; init; }

    /// <summary>质量</summary>
    public Domain.Measurements.Quality Quality { get; init; }

    /// <summary>最近上报时间（UTC）</summary>
    public DateTime Timestamp { get; init; }
}
