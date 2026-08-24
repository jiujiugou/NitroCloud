using NitroCloud.Ingest;

namespace NitroCloud.UnitTests.Ingest;

/// <summary>
/// BatchDeduplicator 批次去重单测（ADR-008 D5：batchId 内存窗口 + TTL，超阈值惰性清理）。
/// </summary>
public class BatchDeduplicatorTests
{
    private static readonly DateTime T0 = new(2026, 8, 23, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstRegistration_True_DuplicateWithinTtl_False_AfterTtl_True()
    {
        var dedup = new BatchDeduplicator(TimeSpan.FromSeconds(60));
        var id = Guid.NewGuid();

        Assert.True(dedup.TryRegister(id, T0));
        Assert.Equal(1, dedup.Count);

        // TTL 窗口内重复 → 判重，丢弃
        Assert.False(dedup.TryRegister(id, T0.AddSeconds(5)));
        Assert.Equal(1, dedup.Count);

        // TTL 过期后再次出现 → 视为新批次
        Assert.True(dedup.TryRegister(id, T0.AddSeconds(61)));
        Assert.Equal(1, dedup.Count);
    }

    [Fact]
    public void DifferentIds_AllRegistered()
    {
        var dedup = new BatchDeduplicator(TimeSpan.FromSeconds(60));
        for (int i = 0; i < 100; i++)
            Assert.True(dedup.TryRegister(Guid.NewGuid(), T0));

        Assert.Equal(100, dedup.Count);
    }

    [Fact]
    public void Cleanup_EvictsExpired_WhenCountExceedsThreshold()
    {
        var dedup = new BatchDeduplicator(TimeSpan.FromSeconds(60));
        var old = Guid.NewGuid();

        Assert.True(dedup.TryRegister(old, T0));
        for (int i = 1; i <= 10_000; i++)
            dedup.TryRegister(Guid.NewGuid(), T0);

        // 超过阈值但 TTL 未过：清理只移除过期项，old 仍在 → 重复仍判 false
        Assert.False(dedup.TryRegister(old, T0.AddSeconds(5)));

        // 时间越过 TTL 后灌入大量新批次 → 惰性清理逐出全部旧批次
        for (int i = 0; i <= 10_000; i++)
            dedup.TryRegister(Guid.NewGuid(), T0.AddSeconds(61));
        Assert.True(dedup.Count <= 10_001);

        // 旧批次已被逐出 → old 可重新注册
        Assert.True(dedup.TryRegister(old, T0.AddSeconds(62)));
    }
}
