using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NitroCloud.Domain.Alarms;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Entities;
using NitroCloud.Persistence.Sqlite;
using NitroCloud.Storage.Models;
using Xunit;

namespace NitroCloud.IntegrationTests;

/// <summary>
/// alarm_records 时间字段（DateTime + 全局 ValueConverter）查询回归测试。
/// 线上异常：AlarmsController.Summary() 的
/// <c>string.CompareOrdinal(a.OccurredAt, ...)</c> 无法被 EF Core SQLite 翻译
/// （InvalidOperationException ... could not be translated）。
/// 修复后 OccurredAt 为 DateTime，比较语句走 ValueConverter 转列比较，留在数据库端执行。
/// 本测试红绿对照：修复前 Summary 形态的 CountAsync 抛翻译异常，修复后正常返回并过滤正确。
/// </summary>
public sealed class AlarmStoreDateTimeQueryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nitrocloud-alarm-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Alarm_time_queries_translate_and_filter_correctly()
    {
        // Pooling=False：避免连接池占用库文件句柄，Dispose 时无法删除临时库
        var connectionString = $"Data Source={_dbPath};Pooling=False";
        MigrationRunner.Run(connectionString);
        await using var db = CreateContext(connectionString);

        // 防御：EF 列名必须落到 FluentMigrator 的 snake_case 列上
        var entityType = db.Model.FindEntityType(typeof(AlarmRecordEntity))!;
        Assert.Equal("occurred_at", entityType.FindProperty(nameof(AlarmRecordEntity.OccurredAt))!.GetColumnName());
        Assert.Equal("acked_at", entityType.FindProperty(nameof(AlarmRecordEntity.AckedAt))!.GetColumnName());

        var store = new AlarmStore(db, NullLogger<AlarmStore>.Instance);

        // 与 AlarmsController.Summary() 相同的“今日 0 点（本地时区）→ UTC”口径
        var now = DateTime.Now;
        var localTodayStart = new DateTime(now.Year, now.Month, now.Day);
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localTodayStart);
        var todayTime = todayStartUtc.AddHours(1);   // 今天
        var oldTime = todayStartUtc.AddDays(-3);     // 3 天前

        var todayAlarm = NewAlarm("alarm-today", todayTime);
        var oldAlarm = NewAlarm("alarm-old", oldTime);
        await store.AddAsync(todayAlarm);
        await store.AddAsync(oldAlarm);

        // ① 崩溃点同形态：Summary 的“今日发生数”必须能翻译（修复前此处抛 InvalidOperationException）
        var todayCount = await db.AlarmRecords.AsNoTracking()
            .CountAsync(a => a.OccurredAt >= todayStartUtc);
        Assert.Equal(1, todayCount);

        // ② QueryAsync 的 From/To 过滤（同样曾用 string.CompareOrdinal）
        var fromTo = await store.QueryAsync(new AlarmQuery
        {
            From = oldTime.AddMinutes(-1),
            To = oldTime.AddMinutes(1)
        });
        var only = Assert.Single(fromTo);
        Assert.Equal("alarm-old", only.Id);
        Assert.Equal(oldTime, only.OccurredAt);

        var sinceToday = await store.QueryAsync(new AlarmQuery { From = todayStartUtc });
        Assert.Single(sinceToday);
        Assert.Equal("alarm-today", sinceToday[0].Id);

        // ③ DateTime 往返一致（O 格式 100ns 精度无损）
        var loadedToday = await store.QueryAsync(new AlarmQuery { Limit = 100 });
        var todayRow = Assert.Single(loadedToday, a => a.Id == "alarm-today");
        Assert.Equal(todayTime, todayRow.OccurredAt);
        Assert.Null(todayRow.AckedAt);

        // ④ AckAsync 置 DateTime（非空）
        await store.AckAsync("alarm-today", "test");
        var acked = await store.QueryAsync(new AlarmQuery { Limit = 100 });
        var ackedRow = Assert.Single(acked, a => a.Id == "alarm-today");
        Assert.Equal(AlarmState.Acknowledged, ackedRow.State);
        Assert.NotNull(ackedRow.AckedAt);
    }

    private static AlarmRecord NewAlarm(string id, DateTime occurredAt) => new()
    {
        Id = id,
        RuleId = "rule-1",
        SiteId = "site-1",
        DeviceId = "dev-1",
        PointId = "pt-1",
        TriggerValue = 90,
        Threshold = 80,
        Severity = AlarmSeverity.Warning,
        Message = $"alarm {id}",
        State = AlarmState.Active,
        OccurredAt = occurredAt
    };

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
