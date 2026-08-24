using NitroCloud.Influx;
using NitroCloud.Storage.Models;

namespace NitroCloud.UnitTests.Influx;

/// <summary>
/// FluxQueryBuilder 查询语句构造单测（ADR-008 D2：Flux 查询封装，纯字符串构建）。
/// siteId 为强制过滤（ADR-004），device/point 可选，limit 收敛到 [1, 5000]。
/// </summary>
public class FluxQueryBuilderTests
{
    private static TimeseriesQuery Query(string siteId, string? deviceId = null, string? devicePointId = null, int limit = 100)
        => new()
        {
            SiteId = siteId,
            DeviceId = deviceId,
            DevicePointId = devicePointId,
            From = new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 8, 23, 23, 59, 59, DateTimeKind.Utc),
            Limit = limit
        };

    [Fact]
    public void Build_IncludesBucketSiteAndTimeRange()
    {
        var flux = FluxQueryBuilder.Build(Query("site-1"), "nitrocloud", "device_point");

        Assert.Contains("from(bucket: \"nitrocloud\")", flux);
        Assert.Contains("r._measurement == \"device_point\"", flux);
        Assert.Contains("r.siteId == \"site-1\"", flux);
        Assert.Contains("range(start: \"2026-08-23T00:00:00.0000000Z\"", flux);
        Assert.Contains("stop: \"2026-08-23T23:59:59.0000000Z\"", flux);
        Assert.Contains("limit(n: 100)", flux);

        // 未指定 device/point → 不生成对应过滤
        Assert.DoesNotContain("r.deviceId", flux);
        Assert.DoesNotContain("r.devicePointId", flux);
    }

    [Fact]
    public void Build_IncludesOptionalDeviceAndPointFilters()
    {
        var flux = FluxQueryBuilder.Build(Query("site-1", deviceId: "dev-1", devicePointId: "pt-1"), "nitrocloud", "device_point");

        Assert.Contains("r.deviceId == \"dev-1\"", flux);
        Assert.Contains("r.devicePointId == \"pt-1\"", flux);
    }

    [Theory]
    [InlineData(999_999, "limit(n: 5000)")]
    [InlineData(0, "limit(n: 1)")]
    [InlineData(1, "limit(n: 1)")]
    [InlineData(5000, "limit(n: 5000)")]
    [InlineData(250, "limit(n: 250)")]
    public void Build_ClampsLimit(int limit, string expected)
    {
        var flux = FluxQueryBuilder.Build(Query("site-1", limit: limit), "nitrocloud", "device_point");
        Assert.Contains(expected, flux);
    }

    [Fact]
    public void Build_EscapesSpecialCharacters()
    {
        // siteId 含双引号 → Flux 字符串内转义为 \"
        var flux = FluxQueryBuilder.Build(Query("site\"1"), "nitrocloud", "device_point");
        Assert.Contains("r.siteId == \"site\\\"1\"", flux);
    }

    [Fact]
    public void Build_NullQuery_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FluxQueryBuilder.Build(null!, "b", "m"));
    }
}
