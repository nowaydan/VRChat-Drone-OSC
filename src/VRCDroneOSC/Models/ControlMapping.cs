namespace VRCDroneOSC.Models;

public class ControlMapping
{
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public int JoystickOffset { get; set; }
    public int? SdlAxis { get; set; }
    public int? SdlButton { get; set; }
    public NormalizeMethod NormalizeMethod { get; set; } = NormalizeMethod.None;
    public BindingBehavior Behavior { get; set; } = BindingBehavior.Continuous;
}

public enum NormalizeMethod
{
    None = 0,
    ZeroToOne = 1,
    MinusOneToOne = 2,
    Binary = 3
}

public enum BindingBehavior
{
    Continuous = 0,
    Toggle = 1,
    OneShot = 2
}
