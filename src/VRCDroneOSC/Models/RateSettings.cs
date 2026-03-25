namespace VRCDroneOSC.Models;

public class RateSettings
{
    public double Center { get; set; } = 200;
    public double MaxRate { get; set; } = 700;
    public double Expo { get; set; } = 0.5;
}

public class ThrottleRateSettings
{
    public double Mid { get; set; }
    public double Expo { get; set; }
}
