using System.Reflection;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NitroCloud.Persistence.Sqlite;

namespace NitroCloud.Persistence;

/// <summary>
/// FluentMigrator 迁移执行器，应用启动时调用一次（幂等）。
/// 流程：提取库文件路径 → 迁移前备份（保留 5 份）→ 应用 PRAGMA → MigrateUp → 记录 app 版本。
/// </summary>
public static class MigrationRunner
{
    /// <summary>
    /// 执行全部待运行迁移。
    /// </summary>
    /// <param name="connectionString">SQLite 连接串（须含 Data Source）</param>
    /// <param name="logger">迁移/备份日志输出，可空</param>
    public static void Run(string connectionString, ILogger? logger = null)
    {
        var dbPath = ExtractDataSource(connectionString);
        var dbExists = File.Exists(dbPath);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        SqlitePragmas.Apply(connection);

        if (dbExists)
            BackupDatabase(connection, dbPath, logger);

        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunner).Assembly).For.Migrations())
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();

        RecordVersion(connection, logger);
    }

    /// <summary>
    /// 迁移前备份：WAL 下先 checkpoint(TRUNCATE) 合并已提交数据再复制，保证备份一致。
    /// 只保留最近 5 份；备份失败直接让启动失败（迁移前必须有可回退现场）。
    /// </summary>
    private static void BackupDatabase(SqliteConnection connection, string dbPath, ILogger? logger)
    {
        var backupDir = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "backups");
        Directory.CreateDirectory(backupDir);

        using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }

        var backupPath = Path.Combine(backupDir, $"nitrocloud.{DateTime.Now:yyyyMMddHHmmss}.bak");
        File.Copy(dbPath, backupPath, overwrite: true);
        logger?.LogInformation("数据库已备份: {BackupPath}", backupPath);

        foreach (var old in Directory.GetFiles(backupDir, "nitrocloud.*.bak").OrderByDescending(f => f).Skip(5))
        {
            File.Delete(old);
            logger?.LogDebug("清理旧备份: {Old}", old);
        }
    }

    /// <summary>把当前程序集版本写入 app_meta（UPSERT）</summary>
    private static void RecordVersion(SqliteConnection connection, ILogger? logger)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO app_meta (key, value, updated_at)
                VALUES ('app_version', @v, @ts)
                ON CONFLICT(key) DO UPDATE SET value=@v, updated_at=@ts
                """;
            cmd.Parameters.AddWithValue("@v", version);
            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table"))
        {
            logger?.LogDebug("app_meta 表尚未创建，跳过版本记录");
        }
    }

    /// <summary>从连接串中提取库文件路径（用 SqliteConnectionStringBuilder 解析）</summary>
    internal static string ExtractDataSource(string connectionString)
        => new SqliteConnectionStringBuilder(connectionString).DataSource;
}
