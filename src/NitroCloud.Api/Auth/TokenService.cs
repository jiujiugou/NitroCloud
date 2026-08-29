using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NitroCloud.Api.Auth;

/// <summary>
/// 自研 HMAC-SHA256 签名 Token（ADR-015，不引 JWT 包，后续要 JWT 标准可平滑替换）。
/// 格式 <c>base64url(payload).base64url(HMAC-SHA256(secret, payload))</c>；
/// payload 为 JSON：sub(userId)/name(username)/role/iat/exp。
/// 单例、无状态：只依赖 <see cref="AuthOptions.Secret"/>。
/// </summary>
public sealed class TokenService
{
    private readonly byte[] _key;
    private readonly int _ttlHours;

    /// <summary>创建 Token 服务（密钥来自配置 Auth:Secret）</summary>
    public TokenService(IOptions<AuthOptions> options)
    {
        var secret = options.Value.Secret;
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
            throw new InvalidOperationException("Auth:Secret 未配置或过短（至少 16 字符）。生产必须用环境变量覆盖。");
        _key = Encoding.UTF8.GetBytes(secret);
        _ttlHours = options.Value.TokenTtlHours > 0 ? options.Value.TokenTtlHours : 12;
    }

    /// <summary>
    /// 签发 Token。
    /// </summary>
    /// <param name="userId">用户主键。</param>
    /// <param name="username">用户名（进 Name claim）。</param>
    /// <param name="role">角色（一层暂不强制，进 Role claim 供后续 RBAC 演进）。</param>
    /// <returns>签名 Token + 过期时间（UTC）。</returns>
    public IssuedToken Issue(string userId, string username, string role)
    {
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = DateTimeOffset.UtcNow.AddHours(_ttlHours).ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sub"] = userId,
            ["name"] = username,
            ["role"] = role,
            ["iat"] = iat,
            ["exp"] = exp
        });
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        return new IssuedToken($"{payloadB64}.{Sign(payloadB64)}", DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime);
    }

    /// <summary>
    /// 校验并解析 Token。签名不符 / 已过期 / 格式非法返回 null。
    /// </summary>
    /// <param name="token">客户端提交的 Token。</param>
    /// <returns>携带 sub/name/role 声明的 <see cref="ClaimsPrincipal"/>；无效为 null。</returns>
    public ClaimsPrincipal? Validate(string token)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
            return null;

        var payloadB64 = token[..dot];
        var signature = token[(dot + 1)..];
        if (!FixedTimeEquals(Sign(payloadB64), signature))
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(payloadB64));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("exp", out var expProp) || expProp.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return null;

            var userId = root.TryGetProperty("sub", out var sub) ? sub.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
            var role = root.TryGetProperty("role", out var rl) ? rl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(userId))
                return null;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(ClaimTypes.Name, name),
                new(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, TokenAuthenticationDefaults.Scheme);
            return new ClaimsPrincipal(identity);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string Sign(string payloadB64)
    {
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        return Base64UrlEncode(hash);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2:
                b64 += "==";
                break;
            case 3:
                b64 += "=";
                break;
        }
        return Convert.FromBase64String(b64);
    }
}

/// <summary>签发结果（Token 串 + 过期时间，供登录响应返回）</summary>
public sealed record IssuedToken(string Token, DateTime ExpiresAt);
