using Microsoft.Extensions.Options;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Sites;
using NitroCloud.Persistence.Entities;
using NitroCloud.Storage;

namespace NitroCloud.Api.Realtime;

/// <summary>
/// 在线状态计算服务（ADR-007：离线判定用「最后上报时间 + 阈值」，阈值可配）。
/// 站点/设备的 lastSeen 来自 <see cref="ILatestValueCache"/>（测量数据时间戳为准），
/// 不额外加保活协议；站点的「运营状态」与在线状态分离（Maintenance = 元数据 Disabled）。
/// </summary>
public sealed class OnlineStatusService
{
    private readonly ILatestValueCache _cache;
    private readonly TimeSpan _offlineThreshold;

    /// <summary>创建在线状态服务</summary>
    public OnlineStatusService(ILatestValueCache cache, IOptions<ApiOptions> options)
    {
        _cache = cache;
        _offlineThreshold = TimeSpan.FromSeconds(Math.Max(5, options.Value.OfflineThresholdSeconds));
    }

    /// <summary>计算设备在线状态（Online/Offline/Unknown；Error 初版不产出）</summary>
    public string GetDeviceStatus(string siteId, string deviceId)
    {
        var lastSeen = _cache.GetDeviceLastSeen(siteId, deviceId);
        if (lastSeen is null)
            return nameof(DeviceStatus.Unknown);
        return IsOnline(lastSeen.Value) ? nameof(DeviceStatus.Online) : nameof(DeviceStatus.Offline);
    }

    /// <summary>计算站点展示状态（Maintenance = 元数据 Disabled；否则按最后上报 + 阈值）</summary>
    public string GetSiteStatus(SiteEntity site)
    {
        if (site.Status == nameof(SiteStatus.Disabled))
            return "Maintenance";

        var lastSeen = _cache.GetSiteLastSeen(site.Id);
        if (lastSeen is null)
            return "Unknown";
        return IsOnline(lastSeen.Value) ? "Online" : "Offline";
    }

    /// <summary>取站点最近上报时间（O 格式字符串；从未上报返回 null）</summary>
    public string? GetSiteLastReportAt(string siteId)
        => _cache.GetSiteLastSeen(siteId)?.ToUniversalTime().ToString("O");

    /// <summary>取设备最近上报时间（O 格式字符串；从未上报返回 null）</summary>
    public string? GetDeviceLastSeenAt(string siteId, string deviceId)
        => _cache.GetDeviceLastSeen(siteId, deviceId)?.ToUniversalTime().ToString("O");

    private bool IsOnline(DateTime lastSeen) => DateTime.UtcNow - lastSeen.ToUniversalTime() <= _offlineThreshold;
}
