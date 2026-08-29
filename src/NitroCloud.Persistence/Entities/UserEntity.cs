namespace NitroCloud.Persistence.Entities;

/// <summary>
/// users 表实体（ADR-015 一层认证；完整 RBAC/roles 表留演进）。
/// 密码只存 PBKDF2 哈希串（见 Api/Auth/PasswordHasher），不落明文。
/// </summary>
public sealed class UserEntity
{
    /// <summary>用户主键（Guid 字符串）</summary>
    public string Id { get; set; } = "";

    /// <summary>登录名（唯一）</summary>
    public string Username { get; set; } = "";

    /// <summary>显示名（管理面板展示）</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>密码哈希（PBKDF2$迭代$salt$hash）</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>角色（一层暂不强制；Admin/User，供后续 RBAC 演进）</summary>
    public string Role { get; set; } = "User";

    /// <summary>是否启用（禁用后登录被拒）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>创建时间（O 格式 UTC 字符串）</summary>
    public string CreatedAt { get; set; } = "";
}
