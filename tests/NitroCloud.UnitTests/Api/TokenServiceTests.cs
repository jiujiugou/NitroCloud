using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NitroCloud.Api.Auth;

namespace NitroCloud.UnitTests.Api;

/// <summary>
/// TokenService 单测（ADR-015）：HMAC 签名 Token 签发/校验红绿对照。
/// 正确签发可解析出 sub/name/role；篡改 / 过期 / 畸形输入均返回 null。
/// </summary>
public class TokenServiceTests
{
    private const string Secret = "test-secret-0123456789";

    private static TokenService CreateService(int ttlHours = 12)
        => new(Options.Create(new AuthOptions { Secret = Secret, TokenTtlHours = ttlHours }));

    [Fact]
    public void Issue_Then_Validate_ReturnsClaims()
    {
        var issued = CreateService().Issue("u-1", "admin", "Admin");
        Assert.Contains('.', issued.Token);
        Assert.True(issued.ExpiresAt > DateTime.UtcNow);

        var principal = CreateService().Validate(issued.Token);
        Assert.NotNull(principal);
        Assert.Equal("u-1", principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("admin", principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal("Admin", principal.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public void Validate_TamperedToken_ReturnsNull()
    {
        var issued = CreateService().Issue("u-1", "admin", "Admin");
        var tampered = issued.Token[..^2] + "xx";
        Assert.Null(CreateService().Validate(tampered));
    }

    [Fact]
    public void Validate_ExpiredToken_ReturnsNull()
    {
        var token = BuildTokenWithExpiry(-3600); // 1 小时前已过期
        Assert.Null(CreateService().Validate(token));
    }

    [Fact]
    public void Validate_FutureExpiry_Passes()
    {
        var token = BuildTokenWithExpiry(+3600);
        Assert.NotNull(CreateService().Validate(token));
    }

    [Fact]
    public void Validate_Garbage_ReturnsNull()
    {
        var service = CreateService();
        Assert.Null(service.Validate(""));
        Assert.Null(service.Validate("no-dot-here"));
        Assert.Null(service.Validate("a.b.c"));
        Assert.Null(service.Validate("not base64!!.!!"));
    }

    [Fact]
    public void Constructor_ShortSecret_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new TokenService(Options.Create(new AuthOptions { Secret = "short" })));
    }

    /// <summary>手工构造一个签名正确但 exp 为 now+offsetSeconds 的 Token（覆盖过期判定）</summary>
    private static string BuildTokenWithExpiry(long offsetSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sub"] = "u-1",
            ["name"] = "admin",
            ["role"] = "Admin",
            ["iat"] = now.AddMinutes(-5).ToUnixTimeSeconds(),
            ["exp"] = now.ToUnixTimeSeconds() + offsetSeconds
        });
        var payloadB64 = ToBase64Url(Encoding.UTF8.GetBytes(payload));
        byte[] sig;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret)))
            sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        return $"{payloadB64}.{ToBase64Url(sig)}";
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
