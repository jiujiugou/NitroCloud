namespace NitroCloud.Domain.Devices;

/// <summary>
/// 点位（测点）。描述一个设备上的可采集/可写值。
/// 点位是低频元数据，落 SQLite；measurement 载荷中带 devicePointId/pointName 冗余字段，便于查询展示。
/// </summary>
public sealed class DevicePoint
{
    /// <summary>点位唯一标识（Guid）</summary>
    public required string Id { get; init; }

    /// <summary>所属设备</summary>
    public required string DeviceId { get; init; }

    /// <summary>点位名称</summary>
    public string Name { get; set; } = "";

    /// <summary>数据类型（与网关 DataType 对齐，决定值解析方式）</summary>
    public DataType DataType { get; set; } = DataType.Float;

    /// <summary>工程单位（℃、MPa、% 等）</summary>
    public string Unit { get; set; } = "";

    /// <summary>是否启用告警（云端汇总时按此过滤）</summary>
    public bool AlarmEnabled { get; set; }

    /// <summary>是否允许反向写值</summary>
    public bool Writable { get; set; }
}
