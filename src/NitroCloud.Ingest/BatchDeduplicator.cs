using System.Collections.Concurrent;

namespace NitroCloud.Ingest;

/// <summary>
/// 批次去重器（ADR-008 D5：batchId 内存窗口去重，TTL 后惰性清理）。
/// 网关以 QoS1 投递，可能重复投递同一批次；按 batchId（Guid）在 TTL 窗口内去重。
///
/// 设计要点：
/// - 线程安全：底层 <see cref="ConcurrentDictionary{TKey,TValue}"/> 原子读写，可被 MQTT 消息并发调用；
/// - 只存「最近一次看到的时间」，不存载荷，内存占用与去重条数成正比、与批次大小无关；
/// - 惰性清理：仅在条目数超阈值时顺带扫一遍过期项，避免每次登记都全量扫描拖慢热路径。
/// </summary>
public sealed class BatchDeduplicator
{
    // key = batchId，value = 最近一次看到该批次的时间（UTC，用于 TTL 窗口判定）
    private readonly ConcurrentDictionary<Guid, DateTime> _lastSeen = new();
    /// <summary>去重窗口时长；窗口内重复的 batchId 判为重复并丢弃</summary>
    private readonly TimeSpan _ttl;

    /// <summary>惰性清理触发阈值：跟踪条目数超过该值才做一次全量清理，避免频繁扫描</summary>
    private const int CleanupThreshold = 10_000;

    /// <summary>创建去重器</summary>
    /// <param name="ttl">去重窗口；窗口内重复的 batchId 视为重复</param>
    public BatchDeduplicator(TimeSpan ttl) => _ttl = ttl;

    /// <summary>
    /// 判断并登记一个批次（原子操作，线程安全）：
    /// 已存在且未超窗口 → 判重复返回 false（丢弃）；
    /// 已存在但已超窗口 → 更新登记时间并放行（窗口滑动，同 id 超窗后允许重新出现）；
    /// 首次出现 → 登记并放行，并触发可能的惰性清理。
    /// </summary>
    /// <param name="batchId">批次唯一标识；Guid.Empty 由调用方跳过去重（见 MeasurementPipeline）</param>
    /// <param name="now">当前时间（UTC），供窗口判定与登记</param>
    /// <returns>true = 首次出现，放行；false = TTL 窗口内已出现过，丢弃</returns>
    public bool TryRegister(Guid batchId, DateTime now)
    {
        if (_lastSeen.TryGetValue(batchId, out var last))
        {
            if (now - last <= _ttl)
                return false;
            _lastSeen.TryUpdate(batchId, now, last);
            return true;
        }

        _lastSeen[batchId] = now;
        MaybeCleanup(now);
        return true;
    }

    /// <summary>
    /// TTL 过期条目的惰性清理：条目数超阈值时顺带做一次全量清理。
    /// 只删「登记时间距今超过窗口」的项；并发下 TryRemove 失败的项留待下次再清。
    /// </summary>
    private void MaybeCleanup(DateTime now)
    {
        if (_lastSeen.Count < CleanupThreshold)
            return;

        foreach (var pair in _lastSeen)
        {
            if (now - pair.Value > _ttl)
                _lastSeen.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>当前跟踪的 batchId 数量（测试/观测用）</summary>
    public int Count => _lastSeen.Count;
}
