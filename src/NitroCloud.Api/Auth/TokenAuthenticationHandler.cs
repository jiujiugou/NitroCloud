using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace NitroCloud.Api.Auth;

/// <summary>
/// 自研签名 Token 的认证处理器（ADR-015，基于共享框架 <c>AuthenticationHandler</c>，不引 JwtBearer 包）。
/// 从 <c>Authorization: Bearer {token}</c> 提取 Token 交给 <see cref="TokenService"/> 校验，
/// 成功后构造带 sub/name/role 声明的票据，使 <c>[Authorize]</c> 正常生效。
/// </summary>
public sealed class TokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly TokenService _tokens;

    /// <summary>创建认证处理器</summary>
    public TokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TokenService tokens)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var header = values.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.Fail("Token 为空"));

        var principal = _tokens.Validate(token);
        if (principal is null)
            return Task.FromResult(AuthenticateResult.Fail("Token 无效或已过期"));

        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
