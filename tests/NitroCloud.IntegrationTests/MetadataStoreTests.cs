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
/// ④空/无效 ID 记录被跳过不污染元数据。建表走 FluentMigrator（snake_case），复用 ADR-012 回归模式。
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
            MakeRecord("site-web-1", deviceId, point1, "空压机_H001", DataType.Float),
            MakeRecord("site-web-1", deviceId, point2, "空压机_H002", DataType.Int32)
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
            Assert.Contains(db.Points, p => p.Id == point1.ToString() && p.Name == "空压机_H001" && p.DataType == "Float");
            Assert.Contains(db.Points, p => p.Id == point2.ToString() && p.Name == "空压机_H002" && p.DataType == "Int32");
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

    private static MeasurementRecord MakeRecord(
        string siteId, Guid deviceId, Guid pointId, string pointName, DataType dataType)
        => new()
        {
            SiteId = siteId,
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            DevicePointId = pointId,
            PointName = pointName,
            Value = 1.0,
            DataType = dataType,
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
