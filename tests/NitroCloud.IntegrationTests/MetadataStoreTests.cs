using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Sqlite;
using NitroCloud.Storage;
using Xunit;

namespace NitroCloud.IntegrationTests;

/// <summary>
/// 元数据自动注册集成测试（ADR-013）：测量记录到达 → <see cref="MetadataStore"/> 幂等注册站点/设备/点位。
/// 验证：①首次注册建行（站点名/设备名兜底 = Id）；②重复调用幂等（不产生重复行）；③新批次只补新点位；
/// ④空/无效 ID 记录被跳过不污染元数据；⑤可写权限（ReadWrite/WriteOnly → Writable=true）回填。
/// 建表走 FluentMigrator（snake_case），复用 ADR-012 回归模式。
/// </summary>
public sealed class MetadataStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nitrocloud-test-{Guid.NewGuid():N}.db");

    /// <summary>完整闭环：FluentMigrator 建库 → MetadataStore 注册 → EF Core 读回断言。</summary>
    [Fact]
    public async Task EnsureRegisteredAsync_creates_site_device_points_idempotently()
    {
        // Pooling=False：避免连接池占住库文件句柄，Dispose 时无法删除临时库
        var connectionString = $"Data Source={_dbPath};Pooling=False";
        MigrationRunner.Run(connectionString);

        await using var provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString))
            .BuildServiceProvider();

        var store = new MetadataStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MetadataStore>.Instance);

        var deviceId = Guid.NewGuid();
        var point1 = Guid.NewGuid();
        var point2 = Guid.NewGuid();
        var records = new List<MeasurementRecord>
        {
            MakeRecord("site-web-1", deviceId, point1, "空压机_H001", DataType.Float, PointAccess.ReadWrite),
            MakeRecord("site-web-1", deviceId, point2, "空压机_H002", DataType.Int32, PointAccess.ReadOnly)
        };

        // ① 首次注册建行
        await store.EnsureRegisteredAsync(records);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var site = Assert.Single(db.Sites);
            Assert.Equal("site-web-1", site.Id);
            Assert.Equal("site-web-1", site.Name);          // 站点显示名兜底 = Id
            Assert.Equal("Active", site.Status);

            var device = Assert.Single(db.Devices);
            Assert.Equal(deviceId.ToString(), device.Id);
            Assert.Equal("site-web-1", device.SiteId);
            Assert.Equal(deviceId.ToString(), device.Name); // 设备显示名兜底 = Id

            Assert.Equal(2, db.Points.Count());
            var p1 = Assert.Single(db.Points, p => p.Id == point1.ToString());
            Assert.Equal("空压机_H001", p1.Name);
            Assert.Equal("Float", p1.DataType);
            Assert.True(p1.Writable, "ReadWrite 点位应回填 Writable=true");
            var p2 = Assert.Single(db.Points, p => p.Id == point2.ToString());
            Assert.Equal("空压机_H002", p2.Name);
            Assert.Equal("Int32", p2.DataType);
            Assert.False(p2.Writable, "ReadOnly 点位应保持 Writable=false");
        }

        // ② 重复调用幂等：不产生重复行
        await store.EnsureRegisteredAsync(records);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(db.Sites);
            Assert.Single(db.Devices);
            Assert.Equal(2, db.Points.Count());
        }

        // ③ 新批次只补新点位，站点/设备不重复
        var point3 = Guid.NewGuid();
        await store.EnsureRegisteredAsync(new[] { MakeRecord("site-web-1", deviceId, point3, "空压机_H003", DataType.Double) });
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(db.Sites);
            Assert.Single(db.Devices);
            Assert.Equal(3, db.Points.Count());
            Assert.Contains(db.Points, p => p.Id == point3.ToString() && p.Name == "空压机_H003" && p.DataType == "Double");
        }
    }

    /// <summary>空/无效 ID（Guid.Empty / 空白 siteId）记录跳过，不污染元数据。</summary>
    [Fact]
    public async Task EnsureRegisteredAsync_skips_records_with_invalid_ids()
    {
        var connectionString = $"Data Source={_dbPath};Pooling=False";
        MigrationRunner.Run(connectionString);

        await using var provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString))
            .BuildServiceProvider();

        var store = new MetadataStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MetadataStore>.Instance);

        var invalid = new List<MeasurementRecord>
        {
            MakeRecord("", Guid.NewGuid(), Guid.NewGuid(), "无效site", DataType.Float),
            MakeRecord("site-x", Guid.Empty, Guid.NewGuid(), "无效设备", DataType.Float),
            MakeRecord("site-x", Guid.NewGuid(), Guid.Empty, "无效点位", DataType.Float)
        };

        await store.EnsureRegisteredAsync(invalid);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.Sites);
        Assert.Empty(db.Devices);
        Assert.Empty(db.Points);
    }

    /// <summary>自动注册据上行权限回填可写性：ReadWrite/WriteOnly → Writable=true，ReadOnly/缺省 → false；重复注册幂等。</summary>
    [Fact]
    public async Task EnsureRegisteredAsync_backfills_writable_from_access()
    {
        var connectionString = $"Data Source={_dbPath};Pooling=False";
        MigrationRunner.Run(connectionString);

        await using var provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString))
            .BuildServiceProvider();

        var store = new MetadataStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MetadataStore>.Instance);

        var deviceId = Guid.NewGuid();
        var rw = Guid.NewGuid();
        var wo = Guid.NewGuid();
        var ro = Guid.NewGuid();
        var defaultRo = Guid.NewGuid();
        var records = new List<MeasurementRecord>
        {
            MakeRecord("site-web-2", deviceId, rw, "可读写点位", DataType.Float, PointAccess.ReadWrite),
            MakeRecord("site-web-2", deviceId, wo, "只写点位", DataType.Float, PointAccess.WriteOnly),
            MakeRecord("site-web-2", deviceId, ro, "只读点位", DataType.Float, PointAccess.ReadOnly),
            // 缺省 access（旧版载荷）按只读兼容
            MakeRecord("site-web-2", deviceId, defaultRo, "缺省点位", DataType.Float)
        };

        await store.EnsureRegisteredAsync(records);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(Assert.Single(db.Points, p => p.Id == rw.ToString()).Writable);
            Assert.True(Assert.Single(db.Points, p => p.Id == wo.ToString()).Writable);
            Assert.False(Assert.Single(db.Points, p => p.Id == ro.ToString()).Writable);
            Assert.False(Assert.Single(db.Points, p => p.Id == defaultRo.ToString()).Writable);
        }

        // 幂等：重复注册不改变已建行的可写性
        await store.EnsureRegisteredAsync(records);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(4, db.Points.Count());
            Assert.True(Assert.Single(db.Points, p => p.Id == rw.ToString()).Writable);
            Assert.False(Assert.Single(db.Points, p => p.Id == ro.ToString()).Writable);
        }
    }

    private static MeasurementRecord MakeRecord(
        string siteId, Guid deviceId, Guid pointId, string pointName, DataType dataType,
        PointAccess access = PointAccess.ReadOnly)
        => new()
        {
            SiteId = siteId,
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            DevicePointId = pointId,
            PointName = pointName,
            Value = 1.0,
            DataType = dataType,
            Access = access,
            Timestamp = DateTime.UtcNow
        };

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = _dbPath + suffix;
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}
