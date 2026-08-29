using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NitroCloud.Api.Auth;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Models;
using NitroCloud.Persistence;

namespace NitroCloud.Api.Controllers;

/// <summary>
/// 登录 API（ADR-015 一层认证）：POST /api/auth/login 校验用户名/密码 → 签发签名 Token。
/// 匿名可访问；管理端点由 <c>[Authorize]</c> 保护，前端走 Bearer 注入。
/// </summary>
[ApiController, Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;

    /// <summary>创建认证控制器</summary>
    public AuthController(AppDbContext db, TokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    /// <summary>
    /// 登录：校验用户名/密码（PBKDF2）→ 签发 Token。
    /// 失败统一返回 401 InvalidCredentials（不区分「用户不存在/密码错误」，防账号探测）。
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Login(LoginRequestDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResponse<LoginResponseDto>.Fail("InvalidCredentials", "用户名和密码必填"));

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == request.Username.Trim(), ct);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            return Unauthorized(ApiResponse<LoginResponseDto>.Fail("InvalidCredentials", "用户名或密码错误"));

        if (!user.IsActive)
            return Unauthorized(ApiResponse<LoginResponseDto>.Fail("UserDisabled", "账号已禁用"));

        var issued = _tokens.Issue(user.Id, user.Username, user.Role);
        return Ok(ApiResponse<LoginResponseDto>.Ok(new LoginResponseDto
        {
            Token = issued.Token,
            Username = user.Username,
            Role = user.Role,
            ExpiresAt = issued.ExpiresAt.ToUniversalTime().ToString("O")
        }));
    }
}
