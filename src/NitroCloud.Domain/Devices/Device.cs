namespace NitroCloud.Domain.Devices;

/// <summary>
/// 设备（网关）。一个站点下可有多个设备；DeviceId 在网关侧是 Guid（上行 topic 第四段）。
/// LastSeenAt 用于离线判定（ADR-007：最后上报时间 + 阈值），以测量数据时间戳为准。
/// 设备是低频元数据，落 SQLite。
/// </summary>
public sealed class Device
{
    /// <summary>设备唯一标识（Guid，上行 topic 第四段）</summary>
    public required string Id { get; init; }

    /// <summary>所属站点</summary>
    public required string SiteId { get; init; }

    /// <summary>设备显示名</summary>
    public string Name { get; set; } = "";

    /// <summary>设备型号</summary>
    public string Model { get; set; } = "";

    /// <summary>最近一次上报时间（UTC，来自测量数据时间戳）；null = 从未上报</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>创建时间（UTC）</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
