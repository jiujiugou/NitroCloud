using Microsoft.Data.Sqlite;

namespace NitroCloud.Persistence.Sqlite;

/// <summary>
/// SQLite PRAGMA 设置（沿用网关 ADR-002 P2-1：WAL + synchronous=NORMAL + busy_timeout）。
/// WAL 为库级持久设置，迁移时应用一次即生效；busy_timeout 为连接级，逐连接应用。
/// </summary>
public static class SqlitePragmas
{
    /// <summary>
    /// 在给定连接上应用 PRAGMA。WAL 模式下先启用 journal_mode=WAL 再设同步级别与忙等待。
    /// </summary>
    public static void Apply(SqliteConnection connection)
    {
        Exec(connection, "PRAGMA journal_mode=WAL;");
        Exec(connection, "PRAGMA synchronous=NORMAL;");
        Exec(connection, "PRAGMA busy_timeout=5000;");
        Exec(connection, "PRAGMA foreign_keys=ON;");
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
