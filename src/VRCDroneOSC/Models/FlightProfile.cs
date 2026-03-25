using System.Collections.Generic;

namespace VRCDroneOSC.Models;

public class FlightProfile
{
    public ThrottleRateSettings ThrottleRate { get; set; } = new();
    public RateSettings PitchRate { get; set; } = new();
    public RateSettings RollRate { get; set; } = new();
    public RateSettings YawRate { get; set; } = new();
    public PhysicsSettings Physics { get; set; } = new();
    public List<ControllerBinding> ControllerBindings { get; set; } = new();
}
