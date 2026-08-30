namespace NitroCloud.Api;

/// <summary>
/// Api 模块配置（appsettings 段 <c>Api</c>，环境变量 Api__* 可覆盖）。
/// 目前仅承载离线判定阈值（ADR-007：最后上报时间 + 阈值）。
/// </summary>
public sealed class ApiOptions
{
    /// <summary>离线判定阈值秒（ADR-007，默认 60）：超过该时长未收到上报即判 Offline</summary>
    public int OfflineThresholdSeconds { get; set; } = 60;

    /// <summary>最近值缓存容量上限（默认 100_000 个点位，超出按站点收敛）</summary>
    public int LatestValueCacheCapacity { get; set; } = 100_000;
}

/// <summary>
/// 元数据管理约束配置（appsettings 段 <c>Metadata</c>，环境变量 Metadata__* 可覆盖；ADR-017）。
/// 站点/设备/点位由上行数据自动注册驱动（ADR-013），默认只读——禁止手动新增/删除，保留改名/补全与命令写值。
/// </summary>
public sealed class MetadataOptions
{
    /// <summary>
    /// 是否允许手动新增/删除元数据（默认 false = 只读）。
    /// 关闭时 Sites/Devices/Points 的 POST/DELETE 返回 403 MetadataReadOnly；PUT（改名/补全）始终可用。
    /// </summary>
    public bool AllowManualCreate { get; set; }
}
