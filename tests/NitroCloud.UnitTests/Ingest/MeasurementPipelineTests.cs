using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NitroCloud.Domain.Alarms;
using NitroCloud.Domain.Commands;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;
using NitroCloud.Ingest;
using NitroCloud.Ingest.Parsing;
using NitroCloud.Storage;
using NitroCloud.Storage.Models;

namespace NitroCloud.UnitTests.Ingest;

/// <summary>
/// MeasurementPipeline 测量处理管线单测（ADR-008 D5）：
/// 去重、实时路径 best-effort（推送失败不丢持久化）、batch.Id=Guid.Empty 跳过去重、解析失败不抛。
/// </summary>
public class MeasurementPipelineTests
{
    private class FakeCache : ILatestValueCache
    {
        public List<IReadOnlyList<MeasurementRecord>> Updates { get; } = new();

        public virtual void Update(IReadOnlyList<MeasurementRecord> records) => Updates.Add(records);

        public IReadOnlyList<LatestValue> GetSite(string siteId) => Array.Empty<LatestValue>();
        public LatestValue? GetPoint(string siteId, string deviceId, string devicePointId) => null;
        public DateTime? GetSiteLastSeen(string siteId) => null;
        public DateTime? GetDeviceLastSeen(string siteId, string deviceId) => null;
    }

    private sealed class FakeNotifier : IRealtimeNotifier
    {
        public List<IReadOnlyList<MeasurementRecord>> MeasurementPushes { get; } = new();

        /// <summary>为 true 时 NotifyMeasurementsAsync 抛异常（模拟 SignalR 故障）</summary>
        public bool ThrowOnMeasurements { get; set; }

        public Task NotifyMeasurementsAsync(string siteId, IReadOnlyList<MeasurementRecord> records, CancellationToken ct = default)
        {
            if (ThrowOnMeasurements)
                throw new InvalidOperationException("signalr unavailable");
            MeasurementPushes.Add(records);
            return Task.CompletedTask;
        }

        public Task NotifyAlarmAsync(AlarmRecord alarm, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyDeviceStatusAsync(string siteId, DeviceStatus status, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyCommandAckAsync(CommandAck ack, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ThrowingCache : FakeCache
    {
        public override void Update(IReadOnlyList<MeasurementRecord> records)
            => throw new InvalidOperationException("cache unavailable");
    }

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>构造一批测量 JSON；id=null 表示缺省 id（反序列化得 Guid.Empty）</summary>
    private static string BatchJson(string? id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", string siteId = "site-1")
    {
        var idField = id is null ? "" : $"\"id\": \"{id}\",";
        return $$"""
            {
              "siteId": "{{siteId}}",
              {{idField}}
              "deviceId": "11111111-1111-1111-1111-111111111111",
              "records": [
                {
                  "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                  "deviceId": "11111111-1111-1111-1111-111111111111",
                  "devicePointId": "22222222-2222-2222-2222-222222222222",
                  "pointName": "Temp",
                  "value": 23.5,
                  "dataType": "Float",
                  "timestamp": "2026-08-23T01:00:00Z",
                  "quality": "Good"
                }
              ]
            }
            """;
    }

    private static MeasurementPipeline Create(
        FakeCache cache,
        FakeNotifier notifier,
        List<IReadOnlyList<MeasurementRecord>> enqueued,
        BatchDeduplicator? deduplicator = null)
        => new(
            new MeasurementBatchParser(),
            deduplicator ?? new BatchDeduplicator(TimeSpan.FromSeconds(60)),
            cache,
            notifier,
            NullLogger.Instance,
            enqueued.Add);

    [Fact]
    public async Task ValidBatch_UpdatesCache_PushesOnce_EnqueuesOnce()
    {
        var cache = new FakeCache();
        var notifier = new FakeNotifier();
        var enqueued = new List<IReadOnlyList<MeasurementRecord>>();
        var pipeline = Create(cache, notifier, enqueued);

        await pipeline.HandleAsync(Utf8(BatchJson()), "site-1");

        Assert.Single(cache.Updates);
        Assert.Single(notifier.MeasurementPushes);
        Assert.Single(enqueued);
        Assert.Equal("site-1", enqueued[0][0].SiteId);
    }

    [Fact]
    public async Task NotifierThrows_BatchStillEnqueued_NoExceptionPropagates()
    {
        // 红绿对照（#1 修复）：实时推送失败不能弄丢持久化路径
        var notifier = new FakeNotifier { ThrowOnMeasurements = true };
        var enqueued = new List<IReadOnlyList<MeasurementRecord>>();
        var pipeline = Create(new FakeCache(), notifier, enqueued);

        await pipeline.HandleAsync(Utf8(BatchJson()), "site-1");

        Assert.Single(enqueued); // 仍入队，DB 写/重试路径不受实时失败影响
        Assert.Empty(notifier.MeasurementPushes);
    }

    [Fact]
    public async Task CacheThrows_BatchStillEnqueued()
    {
        // 红绿对照（#1 修复）：最近值缓存更新失败也不能弄丢持久化路径
        var notifier = new FakeNotifier();
        var enqueued = new List<IReadOnlyList<MeasurementRecord>>();
        var pipeline = Create(new ThrowingCache(), notifier, enqueued);

        await pipeline.HandleAsync(Utf8(BatchJson()), "site-1");

        Assert.Single(enqueued);
        Assert.Empty(notifier.MeasurementPushes);
    }

    [Fact]
    public async Task MissingBatchId_SkipsDedup_AllBatchesAccepted()
    {
        // 红绿对照（#2 修复）：batch.Id=Guid.Empty 时不进去重，避免撞同一 key 被误杀
        var enqueued = new List<IReadOnlyList<MeasurementRecord>>();
        var pipeline = Create(new FakeCache(), new FakeNotifier(), enqueued);

        await pipeline.HandleAsync(Utf8(BatchJson(id: null)), "site-1");
        await pipeline.HandleAsync(Utf8(BatchJson(id: null)), "site-1");

        Assert.Equal(2, enqueued.Count);
    }

    [Fact]
    public async Task DuplicateBatchId_WithinTtl_Deduplicated()
    {
        var notifier = new FakeNotifier();
        var enqueued = new List<IReadOnlyList<MeasurementRecord>>();
        var pipeline = Create(new FakeCache(), notifier, enqueued);

        await pipeline.HandleAsync(Utf8(BatchJson()), "site-1");
        await pipeline.HandleAsync(Utf8(BatchJson()), "site-1"); // 同 id、TTL 内重复投递

        Assert.Single(enqueued);
        Assert.Single(notifier.MeasurementPushes);
    }

    [Fact]
    public async Task ParseFailure_EnqueuesNothing_NoException()
    {
        var cache = new FakeCache();
        var notifier = new FakeNotifier();
        var enqueued = new List<IReadOnlyList<MeasurementRecord>>();
        var pipeline = Create(cache, notifier, enqueued);

        await pipeline.HandleAsync(Utf8("{ not json"), "site-1");

        Assert.Empty(enqueued);
        Assert.Empty(cache.Updates);
        Assert.Empty(notifier.MeasurementPushes);
    }
}
