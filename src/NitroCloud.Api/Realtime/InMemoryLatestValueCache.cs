using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NitroCloud.Domain.Measurements;
using NitroCloud.Storage;
using NitroCloud.Storage.Models;

namespace NitroCloud.Api.Realtime;

/// <summary>
/// 最近值内存缓存实现（ADR-005：实时面板不查库，读内存缓存；容量有上限，按站点收敛）。
/// 由 Ingest 在解析通过后更新（与 InfluxDB 写路径同一批数据）；重启丢缓存可接受（面板自恢复）。
/// 同时维护 site/device 级最近「接收」时间（ADR-007 修订：在线判定以云端接收时间为准，与源时间戳解耦），O(1) 读取。
/// </summary>
public sealed class InMemoryLatestValueCache : ILatestValueCache
{
    private readonly int _maxEntries;
    private readonly ILogger<InMemoryLatestValueCache> _logger;
    /// <summary>接收时间时钟注入点（测试可替换为可控时钟）；默认 <see cref="DateTime.UtcNow"/>。</summary>
    private readonly Func<DateTime> _utcNow;

    private readonly ConcurrentDictionary<(string SiteId, string DeviceId, string DevicePointId), LatestValue> _points = new();
    private readonly ConcurrentDictionary<(string SiteId, string DeviceId), DateTime> _deviceLastSeen = new();
    private readonly ConcurrentDictionary<string, DateTime> _siteLastSeen = new();

    /// <summary>创建缓存</summary>
    /// <param name="options">Api 配置（容量上限等）</param>
    /// <param name="logger">日志</param>
    /// <param name="utcNow">接收时间时钟注入（测试用）；缺省 <see cref="DateTime.UtcNow"/></param>
    public InMemoryLatestValueCache(
        IOptions<ApiOptions> options,
        ILogger<InMemoryLatestValueCache> logger,
        Func<DateTime>? utcNow = null)
    {
        _maxEntries = Math.Max(1000, options.Value.LatestValueCacheCapacity);
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <inheritdoc />
    public void Update(IReadOnlyList<MeasurementRecord> records)
    {
        // 在线判定用「云端接收时间」（record 到达云端、本方法被调用的时刻），
        // 而非测量源时间戳：补发历史 / 网关时钟偏差不再造成误判离线。
        // 源时间戳仍保留在 LatestValue.Timestamp，供展示与数据新鲜度使用（二者语义分离）。
        var receivedAt = _utcNow();
        foreach (var record in records)
        {
            var ts = record.Timestamp.ToUniversalTime();
            var siteId = record.SiteId;
            var deviceId = record.DeviceId.ToString();
            var devicePointId = record.DevicePointId.ToString();
            var key = (siteId, deviceId, devicePointId);

            var value = new LatestValue
            {
                SiteId = siteId,
                DeviceId = deviceId,
                DevicePointId = devicePointId,
                PointName = record.PointName,
                Value = record.Value,
                DataType = record.DataType,
                Quality = record.Quality,
                Timestamp = ts
            };

            if (!_points.ContainsKey(key))
                EnsureCapacity();

            _points[key] = value;

            // 设备/站点最近「接收」时间只前进不后退（时钟可能回拨，取最大值）
            _deviceLastSeen.AddOrUpdate((siteId, deviceId), receivedAt, (_, old) => receivedAt > old ? receivedAt : old);
            _siteLastSeen.AddOrUpdate(siteId, receivedAt, (_, old) => receivedAt > old ? receivedAt : old);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<LatestValue> GetSite(string siteId)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            return Array.Empty<LatestValue>();

        return _points.Where(kv => kv.Key.SiteId == siteId)
            .Select(kv => kv.Value)
            .OrderBy(v => v.DevicePointId)
            .ToList();
    }

    /// <inheritdoc />
    public LatestValue? GetPoint(string siteId, string deviceId, string devicePointId)
        => _points.TryGetValue((siteId, deviceId, devicePointId), out var value) ? value : null;

    /// <inheritdoc />
    public DateTime? GetSiteLastSeen(string siteId)
        => _siteLastSeen.TryGetValue(siteId, out var ts) ? ts : null;

    /// <inheritdoc />
    public DateTime? GetDeviceLastSeen(string siteId, string deviceId)
        => _deviceLastSeen.TryGetValue((siteId, deviceId), out var ts) ? ts : null;

    /// <summary>容量满时按站点收敛：移除最近上报最旧的整个站点（含其全部点位与 lastSeen 记录）</summary>
    private void EnsureCapacity()
    {
        if (_points.Count < _maxEntries)
            return;

        if (_siteLastSeen.IsEmpty)
        {
            _points.Clear();
            return;
        }

        // 选最近上报最旧的站点做收敛（按站点收敛，ADR-005 载荷墙）
        var oldestSite = _siteLastSeen.OrderBy(kv => kv.Value).First().Key;
        foreach (var kv in _points)
        {
            if (kv.Key.SiteId == oldestSite)
                _points.TryRemove(kv.Key, out _);
        }

        foreach (var kv in _deviceLastSeen)
        {
            if (kv.Key.SiteId == oldestSite)
                _deviceLastSeen.TryRemove(kv.Key, out _);
        }

        _siteLastSeen.TryRemove(oldestSite, out _);
        _logger.LogWarning("最近值缓存容量满（{Count}），按站点收敛移除 {SiteId}", _maxEntries, oldestSite);
    }
}
