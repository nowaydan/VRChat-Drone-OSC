using System;
using VRCDroneOSC.Models;

namespace VRCDroneOSC.Services;

public static class InputNormalizer
{
    public static double Normalize(double rawValue, NormalizeMethod method)
    {
        return method switch
        {
            NormalizeMethod.ZeroToOne => (rawValue + 1.0) / 2.0,
            NormalizeMethod.MinusOneToOne => rawValue,
            NormalizeMethod.Binary => Math.Abs(rawValue) > 0.5 ? 1.0 : 0.0,
            _ => rawValue
        };
    }

    public static double ApplyDeadzone(double value, double deadzone)
    {
        if (Math.Abs(value) < deadzone)
            return 0.0;
        double sign = Math.Sign(value);
        double adjusted = (Math.Abs(value) - deadzone) / (1.0 - deadzone);
        return sign * adjusted;
    }
}
