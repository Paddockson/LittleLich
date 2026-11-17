using System;
using System.Text.Json;
using LittleLich.Core;
using Xunit;

namespace LittleLich.Tests;

public class NumTests
{
    [Fact]
    public void Arithmetic_and_comparison()
    {
        Num a = 10, b = 4;
        Assert.Equal(new Num(14), a + b);
        Assert.Equal(new Num(6), a - b);
        Assert.Equal(new Num(40), a * b);
        Assert.Equal(new Num(2.5), a / b);
        Assert.True(a > b);
        Assert.True(b <= a);
    }

    [Fact]
    public void Holds_level_999_xp_magnitude()
    {
        // Flat 10% more per level to 999: 1.1^998 is ~1e41, times a base cost; must not overflow.
        Num xp = Num.Pow(1.1, 998) * 100;
        Assert.True(xp.IsFinite);
        Assert.True(xp > new Num(1e42));
    }

    [Fact]
    public void Necromancer_scale_counts_stay_exact()
    {
        Num n = 100_000_000_000L;
        n += 1;
        Assert.Equal(100_000_000_001L, n.ToLongClamped());
    }

    [Fact]
    public void NaN_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Num(double.NaN));
        Assert.Throws<ArgumentException>(() => { Num z = 0; _ = z / z; });
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_234, "1.23K")]
    [InlineData(999_999, "999K")]
    [InlineData(45_670_000, "45.6M")]
    [InlineData(2_400_000_000, "2.4B")]
    [InlineData(100_000_000_000, "100B")]
    [InlineData(1.234e43, "1.23e43")]
    public void Short_string(double value, string expected) =>
        Assert.Equal(expected, new Num(value).ToShortString());

    [Fact]
    public void Json_round_trip_as_plain_number()
    {
        var json = JsonSerializer.Serialize(new Num(1.5e40));
        Assert.Equal("1.5E+40", json);
        Assert.Equal(new Num(1.5e40), JsonSerializer.Deserialize<Num>(json));
    }
}
