using System.Text;
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

    /// <summary>用户（登录账号，ADR-015 一层认证）</summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

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

        // ── 列名映射（ADR-012）：EF Core 默认按属性名(PascalCase)生成列名，而建表走 FluentMigrator 为
        //    snake_case（command_id/site_id/requested_at…），不一致会报 `no such column: c.XXX`。
        //    统一按属性名转 snake_case 对齐：零依赖、不动库；个别列需特例时用 HasColumnName 覆盖（本循环在前，显式配置后写优先）。
        var snakeCaseEntityTypes = modelBuilder.Model.GetEntityTypes();
        foreach (var entityType in snakeCaseEntityTypes)
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
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

        // ── users ──
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.Role).HasMaxLength(32);
            e.HasIndex(x => x.Username).IsUnique();
        });
    }

    /// <summary>
    /// PascalCase → snake_case 列名：CommandId→command_id、DataType→data_type、Id→id。
    /// 供 <see cref="OnModelCreating"/> 全局列名映射使用（ADR-012）。
    /// </summary>
    /// <param name="name">CLR 属性名（PascalCase）。</param>
    /// <returns>snake_case 列名。</returns>
    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
