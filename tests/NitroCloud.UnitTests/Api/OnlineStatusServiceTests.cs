using Microsoft.Extensions.Options;
using NitroCloud.Api;
using NitroCloud.Api.Realtime;
using NitroCloud.Domain.Measurements;
using NitroCloud.Persistence.Entities;
using NitroCloud.Storage;
using NitroCloud.Storage.Models;

namespace NitroCloud.UnitTests.Api;

/// <summary>
/// OnlineStatusService 在线状态计算单测（ADR-007：最后上报时间 + 阈值判离线；Maintenance = 元数据 Disabled）。
/// </summary>
public class OnlineStatusServiceTests
{
    private sealed class FakeLatestValueCache : ILatestValueCache
    {
        public Dictionary<string, DateTime> Sites { get; } = new();
        public Dictionary<(string SiteId, string DeviceId), DateTime> Devices { get; } = new();

        public void Update(IReadOnlyList<MeasurementRecord> records) { }
        public IReadOnlyList<LatestValue> GetSite(string siteId) => Array.Empty<LatestValue>();
        public LatestValue? GetPoint(string siteId, string deviceId, string devicePointId) => null;
        public DateTime? GetSiteLastSeen(string siteId) => Sites.TryGetValue(siteId, out var t) ? t : null;
        public DateTime? GetDeviceLastSeen(string siteId, string deviceId)
            => Devices.TryGetValue((siteId, deviceId), out var t) ? t : null;
    }

    private static OnlineStatusService Create(FakeLatestValueCache cache, int thresholdSeconds = 60)
        => new(cache, Options.Create(new ApiOptions { OfflineThresholdSeconds = thresholdSeconds }));

    private static SiteEntity Site(string status = "Active") => new() { Id = "s1", Status = status };

    [Fact]
    public void SiteStatus_NoData_Unknown()
    {
        var svc = Create(new FakeLatestValueCache());
        Assert.Equal("Unknown", svc.GetSiteStatus(Site()));
    }

    [Fact]
    public void SiteStatus_Fresh_Online_Stale_Offline()
    {
        var cache = new FakeLatestValueCache();
        var svc = Create(cache);

        cache.Sites["s1"] = DateTime.UtcNow.AddSeconds(-2);
        Assert.Equal("Online", svc.GetSiteStatus(Site()));

        cache.Sites["s1"] = DateTime.UtcNow.AddSeconds(-120);
        Assert.Equal("Offline", svc.GetSiteStatus(Site()));
    }

    [Fact]
    public void SiteStatus_Disabled_IsMaintenance_EvenWhenFresh()
    {
        var cache = new FakeLatestValueCache();
        cache.Sites["s1"] = DateTime.UtcNow;

        var svc = Create(cache);
        Assert.Equal("Maintenance", svc.GetSiteStatus(Site("Disabled")));
    }

    [Fact]
    public void DeviceStatus_And_LastSeenAt()
    {
        var cache = new FakeLatestValueCache();
        var svc = Create(cache);

        // 无数据 → Unknown；无上报时间 → null
        Assert.Equal("Unknown", svc.GetDeviceStatus("s1", "d1"));
        Assert.Null(svc.GetSiteLastReportAt("s1"));
        Assert.Null(svc.GetDeviceLastSeenAt("s1", "d1"));

        // 阈值内 → Online
        cache.Devices[("s1", "d1")] = DateTime.UtcNow.AddSeconds(-2);
        Assert.Equal("Online", svc.GetDeviceStatus("s1", "d1"));

        // 超阈值 → Offline
        cache.Devices[("s1", "d1")] = DateTime.UtcNow.AddSeconds(-120);
        Assert.Equal("Offline", svc.GetDeviceStatus("s1", "d1"));

        // 上报时间以 O 格式输出（UTC）
        cache.Devices[("s1", "d1")] = DateTime.UtcNow.AddSeconds(-2);
        var at = svc.GetDeviceLastSeenAt("s1", "d1");
        Assert.NotNull(at);
        Assert.EndsWith("Z", at);

        cache.Sites["s1"] = DateTime.UtcNow.AddSeconds(-2);
        var reportAt = svc.GetSiteLastReportAt("s1");
        Assert.NotNull(reportAt);
        Assert.EndsWith("Z", reportAt);
    }
}
