using System;
using VRCDroneOSC.Models;

namespace VRCDroneOSC.Services;

public class PhysicsState
{
    public double PositionX, PositionY, PositionZ;
    public double VelocityX, VelocityY, VelocityZ;
    public double AngularVelocityX, AngularVelocityY, AngularVelocityZ;
    public double Pitch, Roll, Yaw;
}

public class DroneOutputParameters
{
    public double Throttle { get; set; }
    public double Pitch { get; set; }
    public double Roll { get; set; }
    public double Yaw { get; set; }
}

public class PhysicsEngine
{
    private readonly PhysicsSettings _settings;
    private readonly PhysicsState _state = new();
    private double _lastThrottle, _lastPitch, _lastRoll, _lastYaw;
    private const double DegToRad = Math.PI / 180.0;

    public PhysicsEngine(PhysicsSettings settings) { _settings = settings; }

    public void Tick(double throttle, double pitch, double roll, double yaw, double dt)
    {
        _lastThrottle = throttle; _lastPitch = pitch; _lastRoll = roll; _lastYaw = yaw;
        double maxRotRate = 500.0;

        // Angular velocity with angular drag
        double angDragFactor = Math.Exp(-_settings.AngularDrag * dt);
        _state.AngularVelocityX = pitch * maxRotRate + (_state.AngularVelocityX - pitch * maxRotRate) * angDragFactor;
        _state.AngularVelocityY = yaw * maxRotRate + (_state.AngularVelocityY - yaw * maxRotRate) * angDragFactor;
        _state.AngularVelocityZ = roll * maxRotRate + (_state.AngularVelocityZ - roll * maxRotRate) * angDragFactor;

        _state.Pitch += _state.AngularVelocityX * dt;
        _state.Yaw += _state.AngularVelocityY * dt;
        _state.Roll += _state.AngularVelocityZ * dt;
        _state.Pitch = WrapAngle(_state.Pitch);
        _state.Yaw = WrapAngle(_state.Yaw);
        _state.Roll = WrapAngle(_state.Roll);

        // Thrust (hover-balanced: at hoverThrottle, thrust = gravity * mass)
        double thrustMagnitude = 0;
        if (throttle > 0.01)
        {
            double normalizedThrust = throttle * throttle * _settings.ThrustMultiplier;
            double hoverThrust = _settings.HoverThrottle * _settings.HoverThrottle * _settings.ThrustMultiplier;
            thrustMagnitude = (normalizedThrust / hoverThrust) * _settings.Gravity * _settings.Mass;
        }

        double pitchRad = _state.Pitch * DegToRad;
        double rollRad = _state.Roll * DegToRad;
        double thrustX = thrustMagnitude * Math.Sin(rollRad);
        double thrustY = thrustMagnitude * Math.Cos(pitchRad) * Math.Cos(rollRad);
        double thrustZ = -thrustMagnitude * Math.Sin(pitchRad) * Math.Cos(rollRad);

        double accelX = thrustX / _settings.Mass;
        double accelY = (thrustY - _settings.Gravity * _settings.Mass) / _settings.Mass;
        double accelZ = thrustZ / _settings.Mass;

        _state.VelocityX += accelX * dt;
        _state.VelocityY += accelY * dt;
        _state.VelocityZ += accelZ * dt;

        // Linear drag
        double speed = Math.Sqrt(_state.VelocityX * _state.VelocityX + _state.VelocityY * _state.VelocityY + _state.VelocityZ * _state.VelocityZ);
        if (speed > 0.001)
        {
            double dragDecel = _settings.LinearDrag * speed * dt / _settings.Mass;
            double dragFactor = Math.Max(0, 1.0 - dragDecel / speed);
            _state.VelocityX *= dragFactor;
            _state.VelocityY *= dragFactor;
            _state.VelocityZ *= dragFactor;
        }

        _state.PositionX += _state.VelocityX * dt;
        _state.PositionY += _state.VelocityY * dt;
        _state.PositionZ += _state.VelocityZ * dt;
    }

    public PhysicsState GetState() => _state;

    public DroneOutputParameters GetOutputParameters() => new()
    {
        Throttle = RateCurve.Clamp(_lastThrottle, 0.0, 1.0),
        Pitch = RateCurve.Clamp(_lastPitch, -1.0, 1.0),
        Roll = RateCurve.Clamp(_lastRoll, -1.0, 1.0),
        Yaw = RateCurve.Clamp(_lastYaw, -1.0, 1.0)
    };

    public void Reset()
    {
        _state.PositionX = _state.PositionY = _state.PositionZ = 0;
        _state.VelocityX = _state.VelocityY = _state.VelocityZ = 0;
        _state.AngularVelocityX = _state.AngularVelocityY = _state.AngularVelocityZ = 0;
        _state.Pitch = _state.Roll = _state.Yaw = 0;
        _lastThrottle = _lastPitch = _lastRoll = _lastYaw = 0;
    }

    public void UpdateSettings(PhysicsSettings s)
    {
        _settings.Mass = s.Mass; _settings.LinearDrag = s.LinearDrag;
        _settings.AngularDrag = s.AngularDrag; _settings.Gravity = s.Gravity;
        _settings.HoverThrottle = s.HoverThrottle; _settings.ThrustMultiplier = s.ThrustMultiplier;
    }

    private static double WrapAngle(double angle) => ((angle % 360) + 540) % 360 - 180;
}
