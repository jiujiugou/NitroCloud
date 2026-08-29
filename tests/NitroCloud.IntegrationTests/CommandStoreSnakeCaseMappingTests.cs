using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NitroCloud.Domain.Commands;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Entities;
using NitroCloud.Persistence.Sqlite;
using Xunit;

namespace NitroCloud.IntegrationTests;

/// <summary>
/// EF Core ↔ FluentMigrator 列名映射回归测试（线上报错 `no such column: c.CommandId`，ADR-012）。
/// 建表走 FluentMigrator（snake_case：command_id/site_id/requested_at…），
/// EF Core 默认按属性名生成 PascalCase 列名 —— 修复后两者必须对齐。
/// 修复前本测试红（AddAsync/QueryInFlightAsync 抛 SqliteException 'no such column'）。
/// </summary>
public sealed class CommandStoreSnakeCaseMappingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nitrocloud-test-{Guid.NewGuid():N}.db");

    /// <summary>
    /// 完整闭环：FluentMigrator 建库（M001-M005）→ EF Core 读写 command_records。
    /// 同时断言 EF 模型列名与 FluentMigrator 的 snake_case 一致（防回退）。
    /// </summary>
    [Fact]
    public async Task CommandStore_roundtrip_against_fluent_migrator_schema()
    {
        // Pooling=False：避免连接池占住库文件句柄，Dispose 时无法删除临时库
        var connectionString = $"Data Source={_dbPath};Pooling=False";
        MigrationRunner.Run(connectionString);

        await using var db = CreateContext(connectionString);

        // 修复点：EF 列名必须落到 FluentMigrator 的 snake_case 列上
        var entityType = db.Model.FindEntityType(typeof(CommandRecordEntity))!;
        Assert.Equal("command_id", entityType.FindProperty(nameof(CommandRecordEntity.CommandId))!.GetColumnName());
        Assert.Equal("site_id", entityType.FindProperty(nameof(CommandRecordEntity.SiteId))!.GetColumnName());
        Assert.Equal("requested_at", entityType.FindProperty(nameof(CommandRecordEntity.RequestedAt))!.GetColumnName());

        var store = new CommandStore(db, NullLogger<CommandStore>.Instance);
        var command = new CommandRecord
        {
            Type = "WritePoint",
            SiteId = "site-1",
            DeviceId = "dev-1",
            PointId = "pt-1",
            Value = 42.5,
            RequestedAt = DateTime.UtcNow,
            Status = CommandStatus.Pending
        };

        await store.AddAsync(command);
        var inFlight = await store.QueryInFlightAsync();
        var loaded = Assert.Single(inFlight);

        Assert.Equal(command.CommandId, loaded.CommandId);
        Assert.Equal(command.SiteId, loaded.SiteId);
        Assert.Equal(command.PointId, loaded.PointId);
        Assert.Equal(command.Value, loaded.Value);
        Assert.Equal(CommandStatus.Pending, loaded.Status);
    }

    private static AppDbContext CreateContext(string connectionString)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);

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
