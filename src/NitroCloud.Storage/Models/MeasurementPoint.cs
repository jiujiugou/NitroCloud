namespace NitroCloud.Storage.Models;

/// <summary>
/// 时序查询返回的单点数据（来自 InfluxDB device_point）。
/// Value 为数值（Influx field 强类型）；Time 为 UTC 时间戳。
/// </summary>
public sealed record MeasurementPoint
{
    /// <summary>所属站点</summary>
    public required string SiteId { get; init; }

    /// <summary>所属设备</summary>
    public required string DeviceId { get; init; }

    /// <summary>所属点位</summary>
    public required string DevicePointId { get; init; }

    /// <summary>点位名称</summary>
    public string PointName { get; init; } = "";

    /// <summary>数值</summary>
    public double Value { get; init; }

    /// <summary>质量标签（Good/Uncertain/Bad）</summary>
    public string Quality { get; init; } = "Good";

    /// <summary>采集时间（UTC）</summary>
    public DateTime Time { get; init; }
}
