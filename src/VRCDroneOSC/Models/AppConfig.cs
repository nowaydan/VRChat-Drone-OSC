using System.Collections.Generic;

namespace VRCDroneOSC.Models;

public class AppConfig
{
    public string ControllerName { get; set; } = "";
    public int LastOpenedPage { get; set; }
    public string ActiveProfile { get; set; } = "Default";
    public Dictionary<string, FlightProfile> Profiles { get; set; } = new()
    {
        ["Default"] = new FlightProfile()
    };
}
