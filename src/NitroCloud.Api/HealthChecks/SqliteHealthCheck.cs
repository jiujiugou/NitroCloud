using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NitroCloud.Persistence;

namespace NitroCloud.Api.HealthChecks;

/// <summary>
/// SQLite 元数据库健康检查（挂到 /healthz，tag: db/ready）。
/// 用 CanConnectAsync 探活，不执行额外查询，避免给元数据库加压力。
/// </summary>
public sealed class SqliteHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    /// <summary>创建健康检查</summary>
    public SqliteHealthCheck(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.Database.CanConnectAsync(cancellationToken);
            return HealthCheckResult.Healthy("SQLite 元数据库可连接");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQLite 元数据库不可连接", ex);
        }
    }
}
