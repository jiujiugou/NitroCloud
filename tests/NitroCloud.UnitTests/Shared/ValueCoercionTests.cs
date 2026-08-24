using NitroCloud.Shared;

namespace NitroCloud.UnitTests.Shared;

/// <summary>
/// ValueCoercion 测量值类型归一化单测：number/bool/string 载荷统一转 double，不可转返回 false。
/// </summary>
public class ValueCoercionTests
{
    [Fact]
    public void NumericTypes_CoerceToDouble()
    {
        Assert.True(ValueCoercion.TryGetDouble(3.14, out var d1));
        Assert.Equal(3.14, d1);

        Assert.True(ValueCoercion.TryGetDouble(3.5f, out var d2));
        Assert.Equal(3.5, d2);

        Assert.True(ValueCoercion.TryGetDouble(42, out var d3));
        Assert.Equal(42, d3);

        Assert.True(ValueCoercion.TryGetDouble(5_000_000_000L, out var d4));
        Assert.Equal(5_000_000_000L, d4);

        Assert.True(ValueCoercion.TryGetDouble(1.5m, out var d5));
        Assert.Equal(1.5, d5);
    }

    [Fact]
    public void Bool_CoercesToOneOrZero()
    {
        Assert.True(ValueCoercion.TryGetDouble(true, out var one));
        Assert.Equal(1, one);

        Assert.True(ValueCoercion.TryGetDouble(false, out var zero));
        Assert.Equal(0, zero);
    }

    [Fact]
    public void NumericStrings_CoerceToDouble()
    {
        Assert.True(ValueCoercion.TryGetDouble("3.14", out var d1));
        Assert.Equal(3.14, d1);

        Assert.True(ValueCoercion.TryGetDouble("  42.5  ", out var d2));
        Assert.Equal(42.5, d2);

        Assert.True(ValueCoercion.TryGetDouble("1,000", out var d3));
        Assert.Equal(1000, d3);
    }

    [Fact]
    public void Null_ReturnsFalse()
    {
        Assert.False(ValueCoercion.TryGetDouble(null, out _));
    }

    [Fact]
    public void NonFinite_And_Unparseable_ReturnFalse()
    {
        Assert.False(ValueCoercion.TryGetDouble(double.NaN, out _));
        Assert.False(ValueCoercion.TryGetDouble(double.PositiveInfinity, out _));
        Assert.False(ValueCoercion.TryGetDouble(double.NegativeInfinity, out _));
        Assert.False(ValueCoercion.TryGetDouble("abc", out _));
        Assert.False(ValueCoercion.TryGetDouble("", out _));
        Assert.False(ValueCoercion.TryGetDouble(new object(), out _));
    }
}
