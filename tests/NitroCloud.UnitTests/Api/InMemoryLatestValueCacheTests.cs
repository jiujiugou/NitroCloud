using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NitroCloud.Api;
using NitroCloud.Api.Realtime;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;

namespace NitroCloud.UnitTests.Api;

/// <summary>
/// InMemoryLatestValueCache 最近值缓存单测（ADR-005：实时面板读内存缓存；容量满按站点收敛）。
/// </summary>
public class InMemoryLatestValueCacheTests
{
    private static MeasurementRecord MakeRecord(string siteId, Guid deviceId, Guid devicePointId,
        string pointName, object? value, DateTime timestamp)
        => new()
        {
            SiteId = siteId,
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            DevicePointId = devicePointId,
            PointName = pointName,
            Value = value,
            DataType = DataType.Float,
            Timestamp = timestamp,
            ReceivedAt = timestamp,
            Quality = Quality.Good
        };

    private static InMemoryLatestValueCache Create(int capacity = 100_000, Func<DateTime>? clock = null)
        => new(
            Options.Create(new ApiOptions { LatestValueCacheCapacity = capacity }),
            NullLogger<InMemoryLatestValueCache>.Instance,
            clock);

    private sealed class MutableClock(DateTime start)
    {
        private DateTime _now = start;
        public void Set(DateTime t) => _now = t;
        public DateTime Now() => _now;
    }

    [Fact]
    public void Update_Then_GetSite_ReturnsAllPoints()
    {
        var cache = Create();
        var dev = Guid.NewGuid();
        var pt1 = Guid.NewGuid();
        var pt2 = Guid.NewGuid();
        var ts = new DateTime(2026, 8, 23, 1, 0, 0, DateTimeKind.Utc);

        cache.Update(new[]
        {
            MakeRecord("site-1", dev, pt1, "Temp", 23.5, ts),
            MakeRecord("site-1", dev, pt2, "Pressure", 101.3, ts)
        });

        var values = cache.GetSite("site-1");
        Assert.Equal(2, values.Count);
        // GetSite 按 DevicePointId 升序返回
        Assert.Equal(
            values.Select(v => v.DevicePointId).OrderBy(x => x),
            values.Select(v => v.DevicePointId));

        var p1 = cache.GetPoint("site-1", dev.ToString(), pt1.ToString());
        Assert.NotNull(p1);
        Assert.Equal("Temp", p1.PointName);
        Assert.Equal(23.5, (double)p1.Value!);
        Assert.Equal(Quality.Good, p1.Quality);
        Assert.Equal(ts, p1.Timestamp);
    }

    [Fact]
    public void GetPoint_Miss_And_GetSite_Unknown_ReturnEmptyOrNull()
    {
        var cache = Create();

        Assert.Null(cache.GetPoint("site-1", "d1", "p1"));
        Assert.Empty(cache.GetSite("site-1"));
        Assert.Empty(cache.GetSite(""));
    }

    [Fact]
    public void LastSeen_UsesReceiveTime_NotSourceTimestamp()
    {
        var received = new DateTime(2026, 8, 23, 3, 0, 0, DateTimeKind.Utc);
        var cache = Create(clock: () => received);
        var dev = Guid.NewGuid();
        var pt = Guid.NewGuid();
        // 源时间戳是 2 小时前（模拟补发历史 / 网关时钟偏差）
        var staleSource = received.AddHours(-2);

        cache.Update(new[] { MakeRecord("site-1", dev, pt, "Temp", 1.0, staleSource) });

        // 在线判定用「接收时间」，不随源时间戳变旧
        Assert.Equal(received, cache.GetSiteLastSeen("site-1")!.Value);
        Assert.Equal(received, cache.GetDeviceLastSeen("site-1", dev.ToString())!.Value);
        // 展示用源时间戳保持不变（数据新鲜度语义）
        Assert.Equal(staleSource, cache.GetPoint("site-1", dev.ToString(), pt.ToString())!.Timestamp);
        // 未命中
        Assert.Null(cache.GetSiteLastSeen("nope"));
        Assert.Null(cache.GetDeviceLastSeen("nope", dev.ToString()));
    }

    [Fact]
    public void OldSourceTimestamp_StillJudgeOnline()
    {
        // 回归：补发/带旧源时间戳的批次，设备仍应判在线（用接收时间）
        var received = DateTime.UtcNow;
        var cache = Create(clock: () => received);
        var dev = Guid.NewGuid();

        cache.Update(new[] { MakeRecord("site-1", dev, Guid.NewGuid(), "Temp", 1.0, received.AddHours(-2)) });

        var svc = new OnlineStatusService(cache, Options.Create(new ApiOptions { OfflineThresholdSeconds = 60 }));
        Assert.Equal("Online", svc.GetDeviceStatus("site-1", dev.ToString()));
    }

    [Fact]
    public void CapacityFull_EvictsOldestSite()
    {
        var early = new DateTime(2026, 8, 23, 1, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2026, 8, 23, 2, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock(early);
        var cache = Create(capacity: 1000, clock: clock.Now);

        // 站点 a：灌满 1000 个点（接收时间 early）
        for (int i = 0; i < 1000; i++)
        {
            cache.Update(new[]
            {
                MakeRecord("a", Guid.NewGuid(), Guid.NewGuid(), $"pt-{i}", 1.0, early)
            });
        }
        Assert.Equal(1000, cache.GetSite("a").Count);

        // 时钟前进，站点 b 首个点位触发收敛：逐出「最近接收」最旧的站点 a
        clock.Set(late);
        cache.Update(new[] { MakeRecord("b", Guid.NewGuid(), Guid.NewGuid(), "pt", 1.0, late) });

        Assert.Empty(cache.GetSite("a"));
        Assert.Single(cache.GetSite("b"));
        Assert.Null(cache.GetSiteLastSeen("a"));
        Assert.NotNull(cache.GetSiteLastSeen("b"));
    }
}
