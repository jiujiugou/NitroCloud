namespace NitroCloud.Api.Auth;

/// <summary>自研签名 Token 认证方案的 Scheme 名（ADR-015）</summary>
public static class TokenAuthenticationDefaults
{
    /// <summary>认证 Scheme 名，亦作 JWT 标准方案落地前的过渡标识</summary>
    public const string Scheme = "NitroToken";
}
