using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Entities;

namespace NitroCloud.Api.Auth;

/// <summary>
/// 引导管理员播种（ADR-015）：首次启动时若不存在 <c>Auth:AdminUsername</c> 对应账号则创建。
/// 密码只在该账号首次创建时哈希一次，此后修改配置不会覆盖已有账号（防误改线上密码）。
/// </summary>
public static class AuthSeeding
{
    /// <summary>
    /// 确保管理员账号存在（在迁移执行后调用）。
    /// </summary>
    /// <param name="db">元数据上下文。</param>
    /// <param name="options">认证配置。</param>
    /// <param name="logger">日志。</param>
    public static void EnsureAdmin(AppDbContext db, AuthOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.AdminUsername) || string.IsNullOrWhiteSpace(options.AdminPassword))
            throw new InvalidOperationException("Auth:AdminUsername / Auth:AdminPassword 必须配置。");

        var exists = db.Users.Any(u => u.Username == options.AdminUsername.Trim());
        if (exists)
            return;

        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = options.AdminUsername.Trim(),
            DisplayName = "系统管理员",
            PasswordHash = PasswordHasher.Hash(options.AdminPassword),
            Role = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("O")
        });
        db.SaveChanges();
        logger.LogWarning("已播种引导管理员账号 {Username}（默认密码仅限本地开发，生产请用 Auth__AdminPassword 覆盖）", options.AdminUsername);
    }
}
