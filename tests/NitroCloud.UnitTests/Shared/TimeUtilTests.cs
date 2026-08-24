using NitroCloud.Shared;

namespace NitroCloud.UnitTests.Shared;

/// <summary>
/// TimeUtil 时间工具单测：ISO 8601 解析统一转 UTC（云侧 UTC 约定），ToIso 输出 O 格式。
/// </summary>
public class TimeUtilTests
{
    [Fact]
    public void FromIso_WithOffset_ConvertsToUtc()
    {
        var t = TimeUtil.FromIso("2026-08-23T10:00:00+08:00");
        Assert.Equal(new DateTime(2026, 8, 23, 2, 0, 0, DateTimeKind.Utc), t);
    }

    [Fact]
    public void FromIso_Zulu_And_NoOffset_AssumeUtc()
    {
        var z = TimeUtil.FromIso("2026-08-23T10:00:00Z");
        Assert.Equal(new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc), z);

        var noOffset = TimeUtil.FromIso("2026-08-23T10:00:00");
        Assert.Equal(new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc), noOffset);
    }

    [Fact]
    public void FromIso_Invalid_ReturnsNull()
    {
        Assert.Null(TimeUtil.FromIso(null));
        Assert.Null(TimeUtil.FromIso(""));
        Assert.Null(TimeUtil.FromIso("not-a-date"));
        Assert.Null(TimeUtil.FromIso("2026-13-99"));
    }

    [Fact]
    public void ToIso_ProducesRoundTripUtc()
    {
        var utc = new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);
        var s = TimeUtil.ToIso(utc);

        Assert.Equal("2026-08-23T10:00:00.0000000Z", s);
        Assert.EndsWith("Z", s);
        Assert.Equal(utc, TimeUtil.FromIso(s));
    }
}
