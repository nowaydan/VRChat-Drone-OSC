namespace VRCDroneOSC.Models;

public class PhysicsSettings
{
    public double Mass { get; set; } = 0.5;
    public double LinearDrag { get; set; } = 2.0;
    public double AngularDrag { get; set; } = 5.0;
    public double Gravity { get; set; } = 9.81;
    public double HoverThrottle { get; set; } = 0.5;
    public double ThrustMultiplier { get; set; } = 1.0;
}
