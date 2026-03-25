using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRCDroneOSC.Models;

namespace VRCDroneOSC.Services;

public class ConfigService
{
    private readonly string _path;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly Dictionary<string, string> ParameterMigration = new()
    {
        ["/avatar/parameters/DroneToggle"] = "/avatar/parameters/ToggleDrone",
        ["/avatar/parameters/DroneCamToggle"] = "/avatar/parameters/ToggleDroneCamera",
        ["/avatar/parameters/DroneCollider"] = "/avatar/parameters/DronePhysics/ColliderToggle"
    };

    public ConfigService(string path)
    {
        _path = path;
    }

    public AppConfig Load()
    {
        if (!File.Exists(_path))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(_path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("controllerBindings", out _) &&
                !root.TryGetProperty("profiles", out _))
            {
                return MigrateOldFormat(root);
            }

            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            return config ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, json);
    }

    private AppConfig MigrateOldFormat(JsonElement root)
    {
        var config = new AppConfig();

        if (root.TryGetProperty("controllerName", out var cn))
            config.ControllerName = cn.GetString() ?? "";
        if (root.TryGetProperty("lastOpenedPage", out var lop))
            config.LastOpenedPage = lop.GetInt32();

        var profile = new FlightProfile();

        if (root.TryGetProperty("throttleRate", out var tr))
            profile.ThrottleRate = JsonSerializer.Deserialize<ThrottleRateSettings>(tr.GetRawText(), JsonOptions) ?? new();
        if (root.TryGetProperty("pitchRate", out var pr))
            profile.PitchRate = JsonSerializer.Deserialize<RateSettings>(pr.GetRawText(), JsonOptions) ?? new();
        if (root.TryGetProperty("rollRate", out var rr))
            profile.RollRate = JsonSerializer.Deserialize<RateSettings>(rr.GetRawText(), JsonOptions) ?? new();
        if (root.TryGetProperty("yawRate", out var yr))
            profile.YawRate = JsonSerializer.Deserialize<RateSettings>(yr.GetRawText(), JsonOptions) ?? new();

        if (root.TryGetProperty("controllerBindings", out var bindings))
        {
            var list = JsonSerializer.Deserialize<List<ControllerBinding>>(
                bindings.GetRawText(), JsonOptions) ?? new();

            foreach (var binding in list)
            {
                if (ParameterMigration.TryGetValue(binding.ParameterAddress, out var corrected))
                    binding.ParameterAddress = corrected;
            }

            profile.ControllerBindings = list;
        }

        config.ActiveProfile = "Default";
        config.Profiles = new Dictionary<string, FlightProfile> { ["Default"] = profile };

        return config;
    }
}
