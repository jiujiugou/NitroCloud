namespace NitroCloud.Persistence.Entities;

/// <summary>points 表实体（EF Core 映射到 <see cref="Domain.Devices.DevicePoint"/>）</summary>
public sealed class PointEntity
{
    /// <summary>点位 ID（Guid 字符串）</summary>
    public string Id { get; set; } = "";

    /// <summary>所属设备</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>点位名称</summary>
    public string Name { get; set; } = "";

    /// <summary>数据类型（DataType 枚举名）</summary>
    public string DataType { get; set; } = "Float";

    /// <summary>工程单位</summary>
    public string Unit { get; set; } = "";

    /// <summary>是否启用告警</summary>
    public bool AlarmEnabled { get; set; }

    /// <summary>是否允许反向写值</summary>
    public bool Writable { get; set; }
}
