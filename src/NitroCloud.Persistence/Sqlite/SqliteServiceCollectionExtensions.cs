using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NitroCloud.Storage;

namespace NitroCloud.Persistence.Sqlite;

/// <summary>
/// Persistence 模块 DI 扩展（组合根在 Api 调用）。
/// 注册 EF Core DbContext + 迁移执行 + 告警/命令存储实现。
/// </summary>
public static class SqliteServiceCollectionExtensions
{
    /// <summary>
    /// 注册 SQLite 元数据基础设施。配置键 Persistence:ConnectionString（Data Source=...）必填。
    /// 首次启动自动执行 FluentMigrator 迁移（幂等）。
    /// </summary>
    public static IServiceCollection AddNitroSqlite(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["Persistence:ConnectionString"]
            ?? throw new InvalidOperationException("Persistence:ConnectionString 未配置。");

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAlarmStore, AlarmStore>();
        services.AddScoped<ICommandStore, CommandStore>();

        // 启动时执行迁移（幂等）；失败快速失败，避免带不完整 Schema 起服务
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetService<ILoggerFactory>()?.CreateLogger("MigrationRunner");
        MigrationRunner.Run(connectionString, logger);

        return services;
    }
}
