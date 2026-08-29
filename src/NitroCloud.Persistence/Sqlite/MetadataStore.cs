using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;
using NitroCloud.Persistence.Entities;
using NitroCloud.Storage;

namespace NitroCloud.Persistence.Sqlite;

/// <summary>
/// 元数据自动注册（SQLite / EF Core，ADR-013）。
/// 单例生命周期：内部用 <see cref="IServiceScopeFactory"/> 按操作开 scope 解析 scoped 的
/// <see cref="AppDbContext"/>，避免从根容器解析 scoped。
///
/// 幂等与性能：
/// - 进程级 <see cref="ConcurrentDictionary{TKey,TValue}"/> 缓存已注册键（站点/设备/点位三张缓存表），
///   低基数、内存极小；缓存未命中才查库（DB 双检），查到即补缓存；
/// - 插入统一用一次 <see cref="DbContext.SaveChangesAsync"/> 提交；
/// - 唯一键冲突（并发/手动重复创建）捕获后整批回滚、不缓存，真正缺失的键留待下批 DB 双检重试。
///
/// best-effort 契约：本类不把唯一键冲突当错误抛出；真正的 DB 故障上抛给调用方
/// （Ingest 捕获记 Warning + 指标，不阻塞时序写入）。
/// </summary>
public sealed class MetadataStore : IMetadataStore
{
    /// <summary>作用域工厂：按操作解析 scoped 的 <see cref="AppDbContext"/></summary>
    private readonly IServiceScopeFactory _scopeFactory;
    /// <summary>日志</summary>
    private readonly ILogger<MetadataStore> _logger;

    /// <summary>已注册站点键缓存（进程内幂等防抖，避免每条消息查库）</summary>
    private readonly ConcurrentDictionary<string, byte> _sites = new(StringComparer.Ordinal);
    /// <summary>已注册设备键缓存（键 = DeviceId 字符串）</summary>
    private readonly ConcurrentDictionary<string, byte> _devices = new(StringComparer.Ordinal);
    /// <summary>已注册点位键缓存（键 = DevicePointId 字符串）</summary>
    private readonly ConcurrentDictionary<string, byte> _points = new(StringComparer.Ordinal);

    /// <summary>
    /// 创建元数据存储。
    /// </summary>
    /// <param name="scopeFactory">作用域工厂：解析 scoped 的 <see cref="AppDbContext"/></param>
    /// <param name="logger">日志</param>
    public MetadataStore(IServiceScopeFactory scopeFactory, ILogger<MetadataStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnsureRegisteredAsync(IReadOnlyList<MeasurementRecord> records, CancellationToken ct = default)
    {
        // 1. 汇总本批去重元数据键；空/无效 ID（Guid.Empty / 空白 siteId）跳过，防 0000… 垃圾行污染元数据。
        var siteIds = new HashSet<string>(StringComparer.Ordinal);
        var deviceSites = new Dictionary<string, string>(StringComparer.Ordinal);  // deviceId → siteId
        var pointMeta = new Dictionary<string, PointMeta>(StringComparer.Ordinal); // pointId → meta

        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.SiteId) || r.DeviceId == Guid.Empty || r.DevicePointId == Guid.Empty)
                continue;

            siteIds.Add(r.SiteId);
            deviceSites.TryAdd(r.DeviceId.ToString(), r.SiteId);
            pointMeta.TryAdd(r.DevicePointId.ToString(), new PointMeta(r.DeviceId.ToString(), r.PointName, r.DataType));
        }

        // 2. 只处理缓存未命中的键（进程内幂等防抖）
        var newSiteIds = siteIds.Where(x => !_sites.ContainsKey(x)).ToList();
        var newDeviceSites = deviceSites.Where(x => !_devices.ContainsKey(x.Key)).ToList();
        var newPointMeta = pointMeta.Where(x => !_points.ContainsKey(x.Key)).ToList();

        if (newSiteIds.Count == 0 && newDeviceSites.Count == 0 && newPointMeta.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow.ToString("O");

        // 3. DB 双检：缓存未命中不代表库里没有（重启后缓存为空 / 此前手动 CRUD 创建过）
        var newDeviceIds = newDeviceSites.Select(x => x.Key).ToList();
        var newPointIds = newPointMeta.Select(x => x.Key).ToList();

        var existingSiteIds = newSiteIds.Count > 0
            ? await db.Sites.AsNoTracking().Where(s => newSiteIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct)
            : [];
        var existingDeviceIds = newDeviceIds.Count > 0
            ? await db.Devices.AsNoTracking().Where(d => newDeviceIds.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct)
            : [];
        var existingPointIds = newPointIds.Count > 0
            ? await db.Points.AsNoTracking().Where(p => newPointIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct)
            : [];

        var existingSiteSet = existingSiteIds.ToHashSet(StringComparer.Ordinal);
        var existingDeviceSet = existingDeviceIds.ToHashSet(StringComparer.Ordinal);
        var existingPointSet = existingPointIds.ToHashSet(StringComparer.Ordinal);

        // 4. 补插缺失行；站点/设备显示名用 Id 兜底（ADR-013），点位名/类型取测量记录
        foreach (var id in newSiteIds.Where(id => !existingSiteSet.Contains(id)))
            db.Sites.Add(new SiteEntity { Id = id, Name = id, Status = "Active", CreatedAt = now });

        foreach (var (deviceId, siteId) in newDeviceSites.Where(x => !existingDeviceSet.Contains(x.Key)))
            db.Devices.Add(new DeviceEntity { Id = deviceId, SiteId = siteId, Name = deviceId, CreatedAt = now });

        foreach (var (pointId, meta) in newPointMeta.Where(x => !existingPointSet.Contains(x.Key)))
            db.Points.Add(new PointEntity
            {
                Id = pointId,
                DeviceId = meta.DeviceId,
                Name = meta.Name,
                DataType = meta.DataType.ToString()
            });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 唯一键冲突（并发/手动重复创建）：整批回滚。不缓存——真正缺失的键下批 DB 双检会重试，
            // 已存在的键下批查库命中后自然补缓存（best-effort，不抛给调用方）。
            _logger.LogDebug(
                "元数据注册唯一键冲突，整批回滚待下批重试（Sites={Sites}, Devices={Devices}, Points={Points}）",
                newSiteIds.Count, newDeviceSites.Count, newPointMeta.Count);
            return;
        }

        // 5. 成功：已存在 + 新插入的全部补缓存（幂等防抖生效）
        foreach (var id in existingSiteIds) _sites.TryAdd(id, 0);
        foreach (var id in newSiteIds) _sites.TryAdd(id, 0);
        foreach (var id in existingDeviceIds) _devices.TryAdd(id, 0);
        foreach (var (deviceId, _) in newDeviceSites) _devices.TryAdd(deviceId, 0);
        foreach (var id in existingPointIds) _points.TryAdd(id, 0);
        foreach (var (pointId, _) in newPointMeta) _points.TryAdd(pointId, 0);
    }

    /// <summary>点位注册所需的元数据（设备归属 + 名称 + 数据类型）</summary>
    private readonly record struct PointMeta(string DeviceId, string Name, DataType DataType);
}
