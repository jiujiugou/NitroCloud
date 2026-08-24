using NitroCloud.Domain.Devices;

namespace NitroCloud.Domain.Measurements;

/// <summary>
/// 单点测量记录（云侧契约模型，来自网关上行载荷的 records[]）。
/// Value 为 object? 忠实保留网关契约（按点位数据类型可能是 number/bool/string）；
/// 写 InfluxDB 前用 Shared.ValueCoercion 归一为数值。
/// </summary>
public sealed record MeasurementRecord
{
    /// <summary>所属站点（来自 topic 第三段，ADR-004；记录级冗余，便于缓存/写库按站点隔离）</summary>
    public required string SiteId { get; init; }

    /// <summary>记录唯一标识（网关生成）</summary>
    public Guid Id { get; init; }

    /// <summary>所属设备 ID（Guid）</summary>
    public Guid DeviceId { get; init; }

    /// <summary>所属点位 ID（Guid）</summary>
    public Guid DevicePointId { get; init; }

    /// <summary>点位名称（冗余字段，便于查询展示）</summary>
    public required string PointName { get; init; }

    /// <summary>采集到的值（契约原样，类型见 DataType）</summary>
    public object? Value { get; init; }

    /// <summary>数据类型（冗余字段，便于解析值）</summary>
    public DataType DataType { get; init; } = DataType.Float;

    /// <summary>采集时间戳（UTC，InfluxDB 写入时间基准）</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>网关接收到该数据的时间（UTC）</summary>
    public DateTime ReceivedAt { get; init; }

    /// <summary>数据质量标记</summary>
    public Quality Quality { get; init; } = Quality.Good;
}
