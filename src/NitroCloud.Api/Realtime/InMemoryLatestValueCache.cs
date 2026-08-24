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
/// 同时维护 site/device 级最近上报时间（ADR-007：在线状态判定），O(1) 读取。
/// </summary>
public sealed class InMemoryLatestValueCache : ILatestValueCache
{
    private readonly int _maxEntries;
    private readonly ILogger<InMemoryLatestValueCache> _logger;

    private readonly ConcurrentDictionary<(string SiteId, string DeviceId, string DevicePointId), LatestValue> _points = new();
    private readonly ConcurrentDictionary<(string SiteId, string DeviceId), DateTime> _deviceLastSeen = new();
    private readonly ConcurrentDictionary<string, DateTime> _siteLastSeen = new();

    /// <summary>创建缓存</summary>
    public InMemoryLatestValueCache(IOptions<ApiOptions> options, ILogger<InMemoryLatestValueCache> logger)
    {
        _maxEntries = Math.Max(1000, options.Value.LatestValueCacheCapacity);
        _logger = logger;
    }

    /// <inheritdoc />
    public void Update(IReadOnlyList<MeasurementRecord> records)
    {
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

            // 设备/站点最近上报时间只前进不后退（取时间戳最大值）
            _deviceLastSeen.AddOrUpdate((siteId, deviceId), ts, (_, old) => ts > old ? ts : old);
            _siteLastSeen.AddOrUpdate(siteId, ts, (_, old) => ts > old ? ts : old);
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
