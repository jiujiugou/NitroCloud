namespace NitroCloud.Domain.Devices;

/// <summary>
/// 云侧设备在线状态（ADR-007：由「最后上报时间 + 阈值」推导，非网关连接状态）。
/// 阈值超时判 Offline；从未上报为 Unknown；最近上报在阈值内为 Online。
/// </summary>
public enum DeviceStatus
{
    /// <summary>尚未有上报数据，状态未知</summary>
    Unknown,

    /// <summary>最近上报在离线阈值内，在线</summary>
    Online,

    /// <summary>超过离线阈值未上报，离线</summary>
    Offline
}
