using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using VRCDroneOSC.Models;

namespace VRCDroneOSC.Services;

public class FlightLoop : IDisposable
{
    private readonly InputManager _input;
    private readonly PhysicsEngine _physics;
    private readonly OscTransport _osc;
    private readonly Func<FlightProfile> _getProfile;
    private Thread? _thread;
    private volatile bool _running;

    private const double TickRate = 120.0;
    private const double TickDt = 1.0 / TickRate;

    public double ThrottleOut { get; private set; }
    public double PitchOut { get; private set; }
    public double RollOut { get; private set; }
    public double YawOut { get; private set; }
    public double StickThrottle { get; private set; }
    public double StickPitch { get; private set; }
    public double StickRoll { get; private set; }
    public double StickYaw { get; private set; }

    public FlightLoop(InputManager input, PhysicsEngine physics,
                      OscTransport osc, Func<FlightProfile> getProfile)
    {
        _input = input;
        _physics = physics;
        _osc = osc;
        _getProfile = getProfile;
    }

    public void Start()
    {
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "FlightLoop", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Loop()
    {
        var sw = Stopwatch.StartNew();
        double accumulator = 0;
        long lastTicks = sw.ElapsedTicks;

        while (_running)
        {
            long now = sw.ElapsedTicks;
            double elapsed = (now - lastTicks) / (double)Stopwatch.Frequency;
            lastTicks = now;
            accumulator += elapsed;

            while (accumulator >= TickDt)
            {
                Tick();
                accumulator -= TickDt;
            }

            Thread.Sleep(1);
        }
    }

    private void Tick()
    {
        var profile = _getProfile();

        // Thread-safe snapshot of bindings
        List<ControllerBinding> bindings;
        lock (profile)
        {
            bindings = new List<ControllerBinding>(profile.ControllerBindings);
        }

        double rawThrottle = 0, rawPitch = 0, rawRoll = 0, rawYaw = 0;

        foreach (var binding in bindings)
        {
            foreach (var control in binding.Controls)
            {
                double raw = _input.GetControlValue(control);
                raw = InputNormalizer.ApplyDeadzone(raw, 0.02);

                switch (binding.ParameterAddress)
                {
                    // Flight axes: use raw value (-1..1). The rate curves
                    // (ApplyThrottle / Apply) handle conversion to output range.
                    // Using the normalized value here would double-convert.
                    case "/avatar/parameters/DroneThrottle": rawThrottle = raw; break;
                    case "/avatar/parameters/DronePitch": rawPitch = raw; break;
                    case "/avatar/parameters/DroneRoll": rawRoll = raw; break;
                    case "/avatar/parameters/DroneYaw": rawYaw = raw; break;
                    default:
                        // Non-flight bindings: apply the binding's normalization
                        double normalized = InputNormalizer.Normalize(raw, control.NormalizeMethod);
                        _osc.QueueValue(binding.ParameterAddress, (float)normalized);
                        break;
                }
            }
        }

        StickThrottle = rawThrottle;
        StickPitch = rawPitch;
        StickRoll = rawRoll;
        StickYaw = rawYaw;

        double throttle = RateCurve.ApplyThrottle(rawThrottle, profile.ThrottleRate.Mid, profile.ThrottleRate.Expo);
        double pitch = RateCurve.Apply(rawPitch, profile.PitchRate.Center, profile.PitchRate.MaxRate, profile.PitchRate.Expo);
        double roll = RateCurve.Apply(rawRoll, profile.RollRate.Center, profile.RollRate.MaxRate, profile.RollRate.Expo);
        double yaw = RateCurve.Apply(rawYaw, profile.YawRate.Center, profile.YawRate.MaxRate, profile.YawRate.Expo);

        double pitchNorm = profile.PitchRate.MaxRate > 0 ? pitch / profile.PitchRate.MaxRate : 0;
        double rollNorm = profile.RollRate.MaxRate > 0 ? roll / profile.RollRate.MaxRate : 0;
        double yawNorm = profile.YawRate.MaxRate > 0 ? yaw / profile.YawRate.MaxRate : 0;

        _physics.Tick(throttle, pitchNorm, rollNorm, yawNorm, TickDt);

        var output = _physics.GetOutputParameters();
        ThrottleOut = output.Throttle;
        PitchOut = output.Pitch;
        RollOut = output.Roll;
        YawOut = output.Yaw;

        _osc.QueueValue("/avatar/parameters/DroneThrottle", (float)output.Throttle);
        _osc.QueueValue("/avatar/parameters/DronePitch", (float)output.Pitch);
        _osc.QueueValue("/avatar/parameters/DroneRoll", (float)output.Roll);
        _osc.QueueValue("/avatar/parameters/DroneYaw", (float)output.Yaw);
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(timeout: TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop();
}
