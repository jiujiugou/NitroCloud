using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NitroCloud.Domain.Alarms;
using NitroCloud.Domain.Commands;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;
using NitroCloud.Domain.Sites;
using NitroCloud.Persistence.Entities;

namespace NitroCloud.Persistence;

/// <summary>
/// 元数据 SQLite 上下文（ADR-008 D2：元数据初版直接用 EF Core DbContext）。
/// 承载 sites/devices/points/alarm_records/command_records 五张表；
/// 时序数据不在此（只进 InfluxDB，ADR-001 载荷墙）。
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>站点</summary>
    public DbSet<SiteEntity> Sites => Set<SiteEntity>();

    /// <summary>设备</summary>
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();

    /// <summary>点位</summary>
    public DbSet<PointEntity> Points => Set<PointEntity>();

    /// <summary>告警汇总</summary>
    public DbSet<AlarmRecordEntity> AlarmRecords => Set<AlarmRecordEntity>();

    /// <summary>命令记录（回写闭环，ADR-010 D2）</summary>
    public DbSet<CommandRecordEntity> CommandRecords => Set<CommandRecordEntity>();

    /// <summary>创建上下文</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── 全局时间转换：DateTime ⇄ "O" 格式 UTC 字符串（字典序即时间序，与网关一致）──
        var utcDateTime = new ValueConverter<DateTime, string>(
            v => v.ToUniversalTime().ToString("O"),
            v => DateTime.Parse(v, null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime());
        var utcNullableDateTime = new ValueConverter<DateTime?, string?>(
            v => v.HasValue ? v.Value.ToUniversalTime().ToString("O") : null,
            v => v == null ? null : DateTime.Parse(v, null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime());

        var entityTypes = modelBuilder.Model.GetEntityTypes();
        foreach (var entityType in entityTypes)
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(utcDateTime);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(utcNullableDateTime);
            }
        }

        // ── sites ──
        modelBuilder.Entity<SiteEntity>(e =>
        {
            e.ToTable("sites");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(128);
        });

        // ── devices ──
        modelBuilder.Entity<DeviceEntity>(e =>
        {
            e.ToTable("devices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.SiteId);
        });

        // ── points ──
        modelBuilder.Entity<PointEntity>(e =>
        {
            e.ToTable("points");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.DeviceId);
        });

        // ── alarm_records ──
        modelBuilder.Entity<AlarmRecordEntity>(e =>
        {
            e.ToTable("alarm_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Severity).HasMaxLength(32);
            e.Property(x => x.State).HasMaxLength(32);
            e.HasIndex(x => new { x.SiteId, x.State });
        });

        // ── command_records ──
        modelBuilder.Entity<CommandRecordEntity>(e =>
        {
            e.ToTable("command_records");
            e.HasKey(x => x.CommandId);
            e.Property(x => x.CommandId).HasMaxLength(64);
            e.Property(x => x.Type).HasMaxLength(32);
            e.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
            e.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
            e.Property(x => x.PointId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Error).HasMaxLength(512);
            e.HasIndex(x => new { x.SiteId, x.Status });
        });
    }
}
