using Microsoft.Extensions.Logging;
using NitroCloud.Domain.Measurements;
using NitroCloud.Ingest.Parsing;
using NitroCloud.Shared;
using NitroCloud.Storage;
using NitroCloud.Telemetry;
using NitroCloud.Telemetry.Tracing;

namespace NitroCloud.Ingest;

/// <summary>
/// 测量消息处理管线（ADR-008 D4 解析后步骤）：解析 → 去重 → 实时路径（缓存+推送，best-effort）→ 攒批入队。
/// 独立成类以便单测：宿主服务绑定 MQTT 收发与后台循环，不易直接构造；本类只依赖纯接口，可直接注入假件验证。
/// 并发说明：MQTT 消息处理器可并发调用 <see cref="HandleAsync"/>，本类内部状态（去重器、警告位）均线程安全，
/// 攒批入队回调由调用方保证加锁。
/// </summary>
internal sealed class MeasurementPipeline
{
    /// <summary>测量/告警载荷解析器（JSON → 领域模型）</summary>
    private readonly MeasurementBatchParser _parser;
    /// <summary>批次去重器（QoS1 重复投递去重，ADR-008 D5）</summary>
    private readonly BatchDeduplicator _deduplicator;
    /// <summary>最近值缓存（实时面板读内存，不查库）</summary>
    private readonly ILatestValueCache _cache;
    /// <summary>实时推送接口（Api 用 SignalR 实现）</summary>
    private readonly IRealtimeNotifier _notifier;
    /// <summary>日志（解析告警、实时路径失败告警等）</summary>
    private readonly ILogger _logger;

    /// <summary>攒批回调（由宿主服务把记录追加进 flush 缓冲；抽成委托便于单测捕获）</summary>
    private readonly Action<IReadOnlyList<MeasurementRecord>> _enqueue;

    /// <summary>是否已提示过"批次缺少 id"（只警告一次，避免刷日志）</summary>
    private int _warnedEmptyId;

    /// <summary>创建测量处理管线</summary>
    /// <param name="parser">测量/告警载荷解析器</param>
    /// <param name="deduplicator">批次去重器（QoS1 重复投递）</param>
    /// <param name="cache">最近值缓存（实时面板读内存，不查库）</param>
    /// <param name="notifier">实时推送接口（Api 用 SignalR 实现）</param>
    /// <param name="logger">日志</param>
    /// <param name="enqueue">攒批入队回调（把记录追加进 flush 缓冲）</param>
    public MeasurementPipeline(
        MeasurementBatchParser parser,
        BatchDeduplicator deduplicator,
        ILatestValueCache cache,
        IRealtimeNotifier notifier,
        ILogger logger,
        Action<IReadOnlyList<MeasurementRecord>> enqueue)
    {
        _parser = parser;
        _deduplicator = deduplicator;
        _cache = cache;
        _notifier = notifier;
        _logger = logger;
        _enqueue = enqueue;
    }

    /// <summary>
    /// 处理一条上行测量消息（ADR-008 D5）：
    /// 解析/校验 → 去重 → ①实时优先（缓存+推送，best-effort，失败不阻塞持久化）→ ②攒批入队。
    /// 解析失败或命中去重时静默返回（只记指标/日志），不抛异常。
    /// 全程不抛出——内部已把可预期失败（解析失败/实时路径异常）收敛为指标与日志，调用方无需 try/catch。
    /// </summary>
    /// <param name="payload">MQTT 载荷字节（UTF-8 JSON）</param>
    /// <param name="siteId">topic 第三段 siteId（作冗余校验与站点隔离基准）</param>
    public async Task HandleAsync(byte[] payload, string siteId)
    {
        using var activity = CloudActivitySource.Source.StartActivity(CloudActivities.IngestBatch);
        activity?.SetTag(CloudActivityTags.SiteId, siteId);

        var result = _parser.Parse(payload, siteId);
        if (!result.IsSuccess)
        {
            CloudMetrics.IngestBatchesTotal.WithLabels("parse_failed").Inc();
            _logger.LogWarning("测量载荷解析失败: {Error}", result.Error);
            return;
        }

        foreach (var warning in result.Warnings)
            _logger.LogWarning("Ingest 警告（site={SiteId}）: {Warning}", siteId, warning);

        var batch = result.Batch!;
        activity?.SetTag(CloudActivityTags.BatchId, batch.Id.ToString());
        activity?.SetTag(CloudActivityTags.RecordCount, batch.Records.Count);

        // 去重（QoS1 重复投递）。batch.Id 缺失（Guid.Empty）时跳过去重：
        // 若全部批次都撞 Guid.Empty 同一 key，TTL 窗口内只有第一批能过、其余被当重复误杀（静默丢数据）。
        if (batch.Id == Guid.Empty)
        {
            if (Interlocked.Exchange(ref _warnedEmptyId, 1) == 0)
                _logger.LogWarning("测量批次缺少 id（batchId=Guid.Empty，site={SiteId}），已跳过去重，请确认网关是否下发 batch id", siteId);
        }
        else if (!_deduplicator.TryRegister(batch.Id, DateTime.UtcNow))
        {
            CloudMetrics.IngestBatchesTotal.WithLabels("deduped").Inc();
            return;
        }

        // ① 实时优先（ADR-008 D5）：更新最近值缓存 + 推送（面板新鲜）。
        // best-effort：缓存/推送失败不阻塞持久化——异常上抛会跳过下面攒批入队，导致整批既不入时序库也不进重试队列。
        if (batch.Records.Count > 0)
        {
            try
            {
                _cache.Update(batch.Records);
                await _notifier.NotifyMeasurementsAsync(siteId, batch.Records);
            }
            catch (Exception ex)
            {
                CloudMetrics.IngestRealtimeFailureTotal.Inc();
                _logger.LogWarning(ex, "实时路径失败（缓存/推送），不阻塞持久化（site={SiteId}, batch={BatchId}）", siteId, batch.Id);
            }
        }

        // ② 攒批入队（flush 循环批量写 InfluxDB，失败进重试队列）
        _enqueue(batch.Records);
        CloudMetrics.IngestBatchesTotal.WithLabels("success").Inc();
    }
}
