using VRCDroneOSC.Models;
using VRCDroneOSC.Services;
using Xunit;

namespace VRCDroneOSC.Tests;

public class NormalizationTests
{
    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.5)]
    [InlineData(1.0, 1.0)]
    public void ZeroToOne_MapsCorrectly(double input, double expected)
    {
        Assert.Equal(expected, InputNormalizer.Normalize(input, NormalizeMethod.ZeroToOne), precision: 5);
    }

    [Theory]
    [InlineData(-1.0, -1.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    public void MinusOneToOne_PassesThrough(double input, double expected)
    {
        Assert.Equal(expected, InputNormalizer.Normalize(input, NormalizeMethod.MinusOneToOne), precision: 5);
    }

    [Theory]
    [InlineData(0.3, 0.0)]
    [InlineData(0.6, 1.0)]
    [InlineData(-0.3, 0.0)]
    public void Binary_ThresholdsAt05(double input, double expected)
    {
        Assert.Equal(expected, InputNormalizer.Normalize(input, NormalizeMethod.Binary), precision: 5);
    }

    [Fact]
    public void ApplyDeadzone_InsideDeadzone_ReturnsZero()
    {
        Assert.Equal(0.0, InputNormalizer.ApplyDeadzone(0.04, 0.05));
    }

    [Fact]
    public void ApplyDeadzone_OutsideDeadzone_RemapsRange()
    {
        double result = InputNormalizer.ApplyDeadzone(1.0, 0.1);
        Assert.Equal(1.0, result, precision: 3);
    }
}
