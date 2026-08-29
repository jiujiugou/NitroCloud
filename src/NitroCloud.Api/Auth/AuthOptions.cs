namespace NitroCloud.Api.Auth;

/// <summary>
/// 认证配置（appsettings 段 <c>Auth</c>，环境变量 Auth__* 可覆盖，ADR-015）。
/// 仅承载一层认证所需：Token 签名密钥 + 有效期 + 引导管理员账号。
/// </summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Token 签名密钥（HMAC-SHA256）。生产必须经环境变量 Auth__Secret 覆盖，默认值仅限本地开发。
    /// </summary>
    public string Secret { get; set; } = "dev-only-secret-change-me";

    /// <summary>Token 有效期（小时，默认 12）。过期后需重新登录。</summary>
    public int TokenTtlHours { get; set; } = 12;

    /// <summary>引导管理员用户名（启动时若不存在则播种，默认 admin）</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>
    /// 引导管理员密码（默认 admin123，仅限本地开发；生产必须经 Auth__AdminPassword 覆盖）。
    /// 只在首次播种时用于生成哈希，已存在的账号不受配置改动影响。
    /// </summary>
    public string AdminPassword { get; set; } = "admin123";
}
