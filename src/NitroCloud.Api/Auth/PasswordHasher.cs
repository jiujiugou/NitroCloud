using System.Security.Cryptography;

namespace NitroCloud.Api.Auth;

/// <summary>
/// 密码哈希（PBKDF2-SHA256，BCL <c>Rfc2898DeriveBytes</c>，不引第三方包，ADR-015）。
/// 存储格式 <c>PBKDF2$迭代次数$salt(base64)$hash(base64)</c>，验证用常量时间比较防时序攻击。
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "PBKDF2";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>
    /// 生成密码哈希。每次随机盐，同一密码两次哈希结果不同（正常）。
    /// </summary>
    /// <param name="password">明文密码（非空）。</param>
    /// <returns>可持久化的哈希串。</returns>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// 校验密码与存储哈希是否匹配。
    /// </summary>
    /// <param name="password">待校验明文。</param>
    /// <param name="stored">存储的哈希串（格式不符返回 false，不抛）。</param>
    /// <returns>匹配为 true。</returns>
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
            return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
