using System;

namespace VRCDroneOSC.Services;

public static class RateCurve
{
    public static double Apply(double stickInput, double center, double maxRate, double expo)
    {
        double rc = Clamp(stickInput, -1.0, 1.0);
        double absRc = Math.Abs(rc);
        double expoFactor = absRc * (1.0 - expo + expo * absRc * absRc);
        double rate = center + (maxRate - center) * expoFactor;
        return rate * Math.Sign(rc) * absRc;
    }

    public static double ApplyThrottle(double stickInput, double mid, double expo)
    {
        double rc = Clamp(stickInput, -1.0, 1.0);
        double normalized = (rc + 1.0) / 2.0;
        double deviation = normalized - mid;
        double expoDeviation = deviation * (1.0 - expo + expo * Math.Abs(deviation) * 4.0);
        double result = mid + expoDeviation;
        return Clamp(result, 0.0, 1.0);
    }

    public static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
