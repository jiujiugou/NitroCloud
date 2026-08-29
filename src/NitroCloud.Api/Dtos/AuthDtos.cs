namespace NitroCloud.Api.Dtos;

/// <summary>
/// 登录请求体（前端 web/src/api/types.ts 的 LoginRequest 对齐，ADR-015）。
/// </summary>
public sealed record LoginRequestDto
{
    /// <summary>用户名</summary>
    public string? Username { get; init; }

    /// <summary>密码</summary>
    public string? Password { get; init; }
}

/// <summary>
/// 登录响应（Token + 用户基本信息；前端存 localStorage，走 Bearer 注入，ADR-015）。
/// </summary>
public sealed record LoginResponseDto
{
    /// <summary>签名 Token（Bearer 头用）</summary>
    public required string Token { get; init; }

    /// <summary>用户名</summary>
    public required string Username { get; init; }

    /// <summary>角色（一层暂不强制，预留）</summary>
    public required string Role { get; init; }

    /// <summary>Token 过期时间（UTC，O 格式）</summary>
    public required string ExpiresAt { get; init; }
}
