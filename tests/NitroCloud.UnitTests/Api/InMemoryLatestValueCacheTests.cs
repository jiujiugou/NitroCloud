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

    private static InMemoryLatestValueCache Create(int capacity = 100_000)
        => new(
            Options.Create(new ApiOptions { LatestValueCacheCapacity = capacity }),
            NullLogger<InMemoryLatestValueCache>.Instance);

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
    public void LastSeen_TakesMaxTimestamp_AcrossUpdates()
    {
        var cache = Create();
        var dev = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var t1 = new DateTime(2026, 8, 23, 1, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 8, 23, 2, 0, 0, DateTimeKind.Utc);

        cache.Update(new[] { MakeRecord("site-1", dev, pt, "Temp", 1.0, t1) });
        cache.Update(new[] { MakeRecord("site-1", dev, pt, "Temp", 2.0, t2) });

        Assert.Equal(t2, cache.GetSiteLastSeen("site-1")!.Value);
        Assert.Equal(t2, cache.GetDeviceLastSeen("site-1", dev.ToString())!.Value);
        Assert.Null(cache.GetSiteLastSeen("nope"));
        Assert.Null(cache.GetDeviceLastSeen("nope", dev.ToString()));
    }

    [Fact]
    public void CapacityFull_EvictsOldestSite()
    {
        var cache = Create(capacity: 1000);
        var early = new DateTime(2026, 8, 23, 1, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2026, 8, 23, 2, 0, 0, DateTimeKind.Utc);

        // 站点 a：灌满 1000 个点（时间戳最早）
        for (int i = 0; i < 1000; i++)
        {
            cache.Update(new[]
            {
                MakeRecord("a", Guid.NewGuid(), Guid.NewGuid(), $"pt-{i}", 1.0, early)
            });
        }
        Assert.Equal(1000, cache.GetSite("a").Count);

        // 站点 b 首个点位触发收敛：逐出最旧站点 a（含其全部点位与 lastSeen 记录）
        cache.Update(new[] { MakeRecord("b", Guid.NewGuid(), Guid.NewGuid(), "pt", 1.0, late) });

        Assert.Empty(cache.GetSite("a"));
        Assert.Single(cache.GetSite("b"));
        Assert.Null(cache.GetSiteLastSeen("a"));
        Assert.NotNull(cache.GetSiteLastSeen("b"));
    }
}
