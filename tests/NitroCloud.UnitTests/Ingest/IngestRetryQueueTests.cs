using NitroCloud.Domain.Measurements;
using NitroCloud.Ingest;

namespace NitroCloud.UnitTests.Ingest;

/// <summary>
/// IngestRetryQueue 重试队列单测（ADR-008 D5：有界 DropOldest + 指数退避 + 重试次数上限）。
/// </summary>
public class IngestRetryQueueTests
{
    private static IReadOnlyList<MeasurementRecord> MakeBatch(string siteId)
        => new[]
        {
            new MeasurementRecord
            {
                SiteId = siteId,
                PointName = "pt",
                DeviceId = Guid.NewGuid(),
                DevicePointId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow
            }
        };

    [Fact]
    public void EnqueueDequeue_RoundTrip()
    {
        var q = new IngestRetryQueue(capacity: 5, maxAttempts: 3, TimeSpan.FromSeconds(1));
        var batch = MakeBatch("site-1");

        q.TryEnqueue(batch);

        Assert.Equal(1, q.Count);
        Assert.True(q.TryDequeue(out var item));
        Assert.Same(batch, item.Batch);
        Assert.Equal(1, item.Attempt);
        Assert.False(q.TryDequeue(out _));
    }

    [Fact]
    public void FullQueue_DropsOldest()
    {
        var q = new IngestRetryQueue(capacity: 2, maxAttempts: 3, TimeSpan.FromSeconds(1));
        q.TryEnqueue(new IngestRetryItem(MakeBatch("a"), 1, DateTime.UtcNow));
        q.TryEnqueue(new IngestRetryItem(MakeBatch("b"), 1, DateTime.UtcNow));
        q.TryEnqueue(new IngestRetryItem(MakeBatch("c"), 1, DateTime.UtcNow));

        // DropOldest：容量满时丢弃最旧（a），队列上限=2
        Assert.Equal(2, q.Count);
        Assert.True(q.TryDequeue(out var first));
        Assert.Equal("b", first.Batch[0].SiteId);
        Assert.True(q.TryDequeue(out var second));
        Assert.Equal("c", second.Batch[0].SiteId);
    }

    [Fact]
    public void IsExhausted_RespectsMaxAttempts()
    {
        var q = new IngestRetryQueue(capacity: 5, maxAttempts: 3, TimeSpan.FromSeconds(1));

        Assert.Equal(3, q.MaxAttempts);
        Assert.False(q.IsExhausted(3));
        Assert.True(q.IsExhausted(4));
    }

    [Fact]
    public void ComputeNextAttemptAt_ExponentialBackoff()
    {
        var q = new IngestRetryQueue(capacity: 5, maxAttempts: 3, TimeSpan.FromSeconds(1));
        var a1 = q.ComputeNextAttemptAt(1);
        var a2 = q.ComputeNextAttemptAt(2);
        var a3 = q.ComputeNextAttemptAt(3);

        // base * 2^(attempt-1)：1s → 2s → 4s（相邻两次调用的时钟差忽略不计）
        Assert.InRange((a2 - a1).TotalSeconds, 0.8, 1.2);
        Assert.InRange((a3 - a2).TotalSeconds, 1.8, 2.2);
    }
}
