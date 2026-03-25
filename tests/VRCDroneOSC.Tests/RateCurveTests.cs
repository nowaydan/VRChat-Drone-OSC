using VRCDroneOSC.Services;
using Xunit;

namespace VRCDroneOSC.Tests;

public class RateCurveTests
{
    [Fact]
    public void ZeroInput_ReturnsZero()
    {
        double result = RateCurve.Apply(0.0, center: 200, maxRate: 700, expo: 0.5);
        Assert.Equal(0.0, result, precision: 5);
    }

    [Fact]
    public void FullInput_ReturnsMaxRate()
    {
        double result = RateCurve.Apply(1.0, center: 200, maxRate: 700, expo: 0.5);
        Assert.Equal(700.0, result, precision: 1);
    }

    [Fact]
    public void NegativeFullInput_ReturnsNegativeMaxRate()
    {
        double result = RateCurve.Apply(-1.0, center: 200, maxRate: 700, expo: 0.5);
        Assert.Equal(-700.0, result, precision: 1);
    }

    [Fact]
    public void HighExpo_ReducesCenterSensitivity()
    {
        double lowExpo = RateCurve.Apply(0.3, center: 200, maxRate: 700, expo: 0.0);
        double highExpo = RateCurve.Apply(0.3, center: 200, maxRate: 700, expo: 0.9);
        Assert.True(highExpo < lowExpo, $"High expo ({highExpo}) should be less than low expo ({lowExpo}) at small input");
    }

    [Fact]
    public void ThrottleExpo_ZeroInput_ReturnsMid()
    {
        double result = RateCurve.ApplyThrottle(0.0, mid: 0.5, expo: 0.0);
        Assert.Equal(0.5, result, precision: 3);
    }

    [Fact]
    public void ThrottleExpo_FullInput_ReturnsOne()
    {
        double result = RateCurve.ApplyThrottle(1.0, mid: 0.5, expo: 0.0);
        Assert.Equal(1.0, result, precision: 3);
    }

    [Fact]
    public void ThrottleExpo_MinInput_ReturnsZero()
    {
        double result = RateCurve.ApplyThrottle(-1.0, mid: 0.5, expo: 0.0);
        Assert.Equal(0.0, result, precision: 3);
    }

    [Fact]
    public void Clamp_ClampsValues()
    {
        Assert.Equal(1.0, RateCurve.Clamp(1.5, -1.0, 1.0));
        Assert.Equal(-1.0, RateCurve.Clamp(-1.5, -1.0, 1.0));
    }
}
