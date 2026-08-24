namespace NitroCloud.Persistence.Entities;

/// <summary>devices 表实体（EF Core 映射到 <see cref="Domain.Devices.Device"/>）</summary>
public sealed class DeviceEntity
{
    /// <summary>设备 ID（Guid 字符串，= 上行 topic 第四段）</summary>
    public string Id { get; set; } = "";

    /// <summary>所属站点</summary>
    public string SiteId { get; set; } = "";

    /// <summary>设备显示名</summary>
    public string Name { get; set; } = "";

    /// <summary>设备型号</summary>
    public string Model { get; set; } = "";

    /// <summary>最近上报时间（O 格式 UTC 字符串，null = 从未上报）</summary>
    public string? LastSeenAt { get; set; }

    /// <summary>创建时间（O 格式 UTC 字符串）</summary>
    public string CreatedAt { get; set; } = "";
}
