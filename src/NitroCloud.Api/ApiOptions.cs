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
