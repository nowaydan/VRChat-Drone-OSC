using System;
using VRCDroneOSC.Models;
using VRCDroneOSC.Services;
using Xunit;

namespace VRCDroneOSC.Tests;

public class PhysicsEngineTests
{
    private PhysicsEngine CreateEngine(PhysicsSettings? settings = null)
    {
        return new PhysicsEngine(settings ?? new PhysicsSettings());
    }

    [Fact]
    public void InitialState_IsZero()
    {
        var engine = CreateEngine();
        var state = engine.GetState();
        Assert.Equal(0, state.PositionX);
        Assert.Equal(0, state.PositionY);
        Assert.Equal(0, state.PositionZ);
    }

    [Fact]
    public void NoThrottle_FallsDueToGravity()
    {
        var engine = CreateEngine();
        for (int i = 0; i < 120; i++)
            engine.Tick(throttle: 0, pitch: 0, roll: 0, yaw: 0, dt: 1.0 / 120.0);
        var state = engine.GetState();
        Assert.True(state.PositionY < -1.0, $"Should have fallen: Y={state.PositionY}");
    }

    [Fact]
    public void HoverThrottle_MaintainsAltitude()
    {
        var settings = new PhysicsSettings { HoverThrottle = 0.5 };
        var engine = CreateEngine(settings);
        for (int i = 0; i < 240; i++)
            engine.Tick(throttle: 0.5, pitch: 0, roll: 0, yaw: 0, dt: 1.0 / 120.0);
        var state = engine.GetState();
        Assert.InRange(state.PositionY, -2.0, 2.0);
    }

    [Fact]
    public void FullThrottle_Climbs()
    {
        var engine = CreateEngine();
        for (int i = 0; i < 120; i++)
            engine.Tick(throttle: 1.0, pitch: 0, roll: 0, yaw: 0, dt: 1.0 / 120.0);
        Assert.True(engine.GetState().PositionY > 0);
    }

    [Fact]
    public void LinearDrag_SlowsDown()
    {
        var settings = new PhysicsSettings { LinearDrag = 5.0 };
        var engine = CreateEngine(settings);
        for (int i = 0; i < 60; i++)
            engine.Tick(throttle: 1.0, pitch: 0.5, roll: 0, yaw: 0, dt: 1.0 / 120.0);
        var s1 = engine.GetState();
        double speedBefore = Math.Sqrt(s1.VelocityX * s1.VelocityX + s1.VelocityZ * s1.VelocityZ);
        for (int i = 0; i < 120; i++)
            engine.Tick(throttle: 0, pitch: 0, roll: 0, yaw: 0, dt: 1.0 / 120.0);
        var s2 = engine.GetState();
        double speedAfter = Math.Sqrt(s2.VelocityX * s2.VelocityX + s2.VelocityZ * s2.VelocityZ);
        Assert.True(speedAfter < speedBefore * 0.5);
    }

    [Fact]
    public void AngularDrag_DampensRotation()
    {
        var settings = new PhysicsSettings { AngularDrag = 10.0 };
        var engine = CreateEngine(settings);
        for (int i = 0; i < 60; i++)
            engine.Tick(throttle: 0.5, pitch: 0, roll: 0, yaw: 1.0, dt: 1.0 / 120.0);
        double yawDuring = engine.GetState().AngularVelocityY;
        for (int i = 0; i < 120; i++)
            engine.Tick(throttle: 0.5, pitch: 0, roll: 0, yaw: 0, dt: 1.0 / 120.0);
        double yawAfter = engine.GetState().AngularVelocityY;
        Assert.True(Math.Abs(yawAfter) < Math.Abs(yawDuring) * 0.3);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var engine = CreateEngine();
        for (int i = 0; i < 60; i++)
            engine.Tick(throttle: 1.0, pitch: 0.5, roll: 0.5, yaw: 0.5, dt: 1.0 / 120.0);
        engine.Reset();
        var state = engine.GetState();
        Assert.Equal(0, state.PositionX);
        Assert.Equal(0, state.PositionY);
    }

    [Fact]
    public void GetOutputParameters_ReturnsNormalizedValues()
    {
        var engine = CreateEngine();
        var output = engine.GetOutputParameters();
        Assert.InRange(output.Throttle, 0.0, 1.0);
        Assert.InRange(output.Pitch, -1.0, 1.0);
    }
}
