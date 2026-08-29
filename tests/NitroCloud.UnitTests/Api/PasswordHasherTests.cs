using NitroCloud.Api.Auth;

namespace NitroCloud.UnitTests.Api;

/// <summary>
/// PasswordHasher 单测（ADR-015）：PBKDF2 哈希/校验红绿对照。
/// 格式 PBKDF2$迭代$salt$hash；随机盐 → 同密码两次哈希不同；错误密码/畸形串均 false 不抛。
/// </summary>
public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesExpectedFormat()
    {
        var hash = PasswordHasher.Hash("admin123");
        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("PBKDF2", parts[0]);
        Assert.True(int.Parse(parts[1]) > 0);
        Assert.False(string.IsNullOrEmpty(parts[2]));
        Assert.False(string.IsNullOrEmpty(parts[3]));
    }

    [Fact]
    public void Hash_SamePassword_TwoHashesDiffer_RandomSalt()
    {
        var a = PasswordHasher.Hash("admin123");
        var b = PasswordHasher.Hash("admin123");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Verify_CorrectPassword_True()
    {
        var hash = PasswordHasher.Hash("admin123");
        Assert.True(PasswordHasher.Verify("admin123", hash));
    }

    [Fact]
    public void Verify_WrongPassword_False()
    {
        var hash = PasswordHasher.Hash("admin123");
        Assert.False(PasswordHasher.Verify("admin124", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Verify_MalformedStored_ReturnsFalse_NoThrow()
    {
        Assert.False(PasswordHasher.Verify("x", "not-a-valid-hash"));
        Assert.False(PasswordHasher.Verify("x", ""));
        Assert.False(PasswordHasher.Verify("x", "PBKDF2$abc$!!!$!!!"));
        Assert.False(PasswordHasher.Verify("x", "SHA1$100000$c2FsdA==$aGFzaA=="));
    }
}
