using System.Collections.Generic;

namespace VRCDroneOSC.Models;

public class ControllerBinding
{
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = "";
    public string ParameterAddress { get; set; } = "";
    public List<ControlMapping> Controls { get; set; } = new();
}
