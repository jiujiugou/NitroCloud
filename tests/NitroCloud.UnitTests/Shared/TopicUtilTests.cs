using NitroCloud.Shared;

namespace NitroCloud.UnitTests.Shared;

/// <summary>
/// TopicUtil topic 解析/构造单测（ADR-008 D4 数据流契约：上行 measurements/alarms、下行 commands/commands/ack）。
/// </summary>
public class TopicUtilTests
{
    [Theory]
    [InlineData("nitrogateway/site-1/dev-1/measurements", TopicKind.Measurements)]
    [InlineData("nitrogateway/site-1/dev-1/alarms", TopicKind.Alarms)]
    [InlineData("nitrogateway/site-1/dev-1/commands", TopicKind.Commands)]
    [InlineData("nitrogateway/site-1/dev-1/commands/ack", TopicKind.CommandAck)]
    public void Parse_ValidTopics_ReturnsKindAndIds(string topic, TopicKind expected)
    {
        var parsed = TopicUtil.Parse(topic);

        Assert.True(parsed.HasValue);
        Assert.Equal(expected, parsed.Value.Kind);
        Assert.Equal("site-1", parsed.Value.SiteId);
        Assert.Equal("dev-1", parsed.Value.DeviceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("other/site-1/dev-1/measurements")]
    [InlineData("nitrogateway/site-1")]
    [InlineData("nitrogateway/site-1/dev-1")]
    [InlineData("nitrogateway/si+te/dev-1/measurements")]
    [InlineData("nitrogateway/site-1/de#v-1/measurements")]
    [InlineData("nitrogateway/site-1/dev-1/foo")]
    [InlineData("nitrogateway/site-1/dev-1/measurements/extra")]
    [InlineData("nitrogateway/site-1/dev-1/commands/ack/extra")]
    [InlineData("nitrogateway/+/+/measurements")]
    [InlineData("nitrogateway/site-1/dev-1/alarms/extra")]
    public void Parse_InvalidTopics_ReturnsNull(string? topic)
    {
        Assert.Null(TopicUtil.Parse(topic!));
    }

    [Fact]
    public void Builders_ProduceExpectedTopics()
    {
        Assert.Equal("nitrogateway/site-1/dev-1/measurements", TopicUtil.Measurements("site-1", "dev-1"));
        Assert.Equal("nitrogateway/site-1/dev-1/alarms", TopicUtil.Alarms("site-1", "dev-1"));
        Assert.Equal("nitrogateway/site-1/dev-1/commands", TopicUtil.Commands("site-1", "dev-1"));
        Assert.Equal("nitrogateway/site-1/dev-1/commands/ack", TopicUtil.CommandAck("site-1", "dev-1"));
    }

    [Fact]
    public void SubscriptionConstants_UseWildcards()
    {
        Assert.Equal("nitrogateway/+/+/measurements", TopicUtil.MeasurementsSubscription);
        Assert.Equal("nitrogateway/+/+/alarms", TopicUtil.AlarmsSubscription);
        Assert.Equal("nitrogateway/+/+/commands/ack", TopicUtil.CommandAckSubscription);
        Assert.Equal("commands/ack", TopicUtil.AckSuffix);
    }
}
