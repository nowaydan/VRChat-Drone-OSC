using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VRCDroneOSC.Models;
using VRCDroneOSC.Services;
using Xunit;

namespace VRCDroneOSC.Tests;

public class ConfigServiceTests
{
    private readonly string _tempDir;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "VRCDroneOSC_Tests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var svc = new ConfigService(Path.Combine(_tempDir, "Settings.json"));
        var config = svc.Load();
        Assert.Equal("Default", config.ActiveProfile);
        Assert.Single(config.Profiles);
        Assert.True(config.Profiles.ContainsKey("Default"));
    }

    [Fact]
    public void Load_NewFormat_LoadsProfiles()
    {
        var path = Path.Combine(_tempDir, "Settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            controllerName = "Test Controller",
            activeProfile = "Racing",
            profiles = new Dictionary<string, object>
            {
                ["Racing"] = new
                {
                    throttleRate = new { mid = 0.0, expo = 0.3 },
                    pitchRate = new { center = 300.0, maxRate = 800.0, expo = 0.7 },
                    rollRate = new { center = 200.0, maxRate = 700.0, expo = 0.5 },
                    yawRate = new { center = 200.0, maxRate = 700.0, expo = 0.5 },
                    physics = new { mass = 0.5, linearDrag = 2.0, angularDrag = 5.0, gravity = 9.81, hoverThrottle = 0.5, thrustMultiplier = 1.0 },
                    controllerBindings = new object[0]
                }
            }
        }));
        var svc = new ConfigService(path);
        var config = svc.Load();
        Assert.Equal("Test Controller", config.ControllerName);
        Assert.Equal("Racing", config.ActiveProfile);
        Assert.True(config.Profiles.ContainsKey("Racing"));
        Assert.Equal(0.7, config.Profiles["Racing"].PitchRate.Expo);
    }

    [Fact]
    public void Load_OldFormat_MigratesAsDefaultProfile()
    {
        var path = Path.Combine(_tempDir, "Settings.json");
        File.WriteAllText(path, @"{
            ""controllerName"": ""Radiomaster Pocket Joystick"",
            ""lastOpenedPage"": 1,
            ""throttleRate"": { ""mid"": 0, ""expo"": 0 },
            ""pitchRate"": { ""center"": 200, ""maxRate"": 700, ""expo"": 0.5 },
            ""rollRate"": { ""center"": 200, ""maxRate"": 700, ""expo"": 0.5 },
            ""yawRate"": { ""center"": 200, ""maxRate"": 700, ""expo"": 0.5 },
            ""controllerBindings"": [
                {
                    ""guid"": ""d0f438e2-5ac4-46ce-9e08-e84f4b8f54bd"",
                    ""displayName"": ""Throttle"",
                    ""parameterAddress"": ""/avatar/parameters/DroneThrottle"",
                    ""controls"": [{
                        ""guid"": ""b82f4557-8a6c-4e7e-8d82-927452d9c4b7"",
                        ""joystickOffset"": 8,
                        ""normalizeMethod"": 1,
                        ""behavior"": 0
                    }]
                }
            ]
        }");
        var svc = new ConfigService(path);
        var config = svc.Load();
        Assert.Equal("Default", config.ActiveProfile);
        Assert.True(config.Profiles.ContainsKey("Default"));
        var profile = config.Profiles["Default"];
        Assert.Equal(0.5, profile.PitchRate.Expo);
        Assert.Single(profile.ControllerBindings);
        Assert.Equal("Throttle", profile.ControllerBindings[0].DisplayName);
    }

    [Fact]
    public void Load_OldFormat_MigratesParameterNames()
    {
        var path = Path.Combine(_tempDir, "Settings.json");
        File.WriteAllText(path, @"{
            ""controllerName"": ""Test"",
            ""throttleRate"": { ""mid"": 0, ""expo"": 0 },
            ""pitchRate"": { ""center"": 200, ""maxRate"": 700, ""expo"": 0.5 },
            ""rollRate"": { ""center"": 200, ""maxRate"": 700, ""expo"": 0.5 },
            ""yawRate"": { ""center"": 200, ""maxRate"": 700, ""expo"": 0.5 },
            ""controllerBindings"": [
                { ""guid"": ""aaa"", ""displayName"": ""DroneToggle"", ""parameterAddress"": ""/avatar/parameters/DroneToggle"", ""controls"": [] },
                { ""guid"": ""bbb"", ""displayName"": ""Camera"", ""parameterAddress"": ""/avatar/parameters/DroneCamToggle"", ""controls"": [] },
                { ""guid"": ""ccc"", ""displayName"": ""Collider"", ""parameterAddress"": ""/avatar/parameters/DroneCollider"", ""controls"": [] }
            ]
        }");
        var svc = new ConfigService(path);
        var config = svc.Load();
        var bindings = config.Profiles["Default"].ControllerBindings;
        Assert.Equal("/avatar/parameters/ToggleDrone", bindings[0].ParameterAddress);
        Assert.Equal("/avatar/parameters/ToggleDroneCamera", bindings[1].ParameterAddress);
        Assert.Equal("/avatar/parameters/DronePhysics/ColliderToggle", bindings[2].ParameterAddress);
    }

    [Fact]
    public void Save_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "Settings.json");
        var svc = new ConfigService(path);
        var config = new AppConfig
        {
            ControllerName = "Test",
            ActiveProfile = "Custom",
            Profiles = new()
            {
                ["Custom"] = new FlightProfile
                {
                    PitchRate = new RateSettings { Center = 300, MaxRate = 900, Expo = 0.8 }
                }
            }
        };
        svc.Save(config);
        var loaded = svc.Load();
        Assert.Equal("Test", loaded.ControllerName);
        Assert.Equal("Custom", loaded.ActiveProfile);
        Assert.Equal(900, loaded.Profiles["Custom"].PitchRate.MaxRate);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "Settings.json");
        File.WriteAllText(path, "NOT VALID JSON {{{");
        var svc = new ConfigService(path);
        var config = svc.Load();
        Assert.Equal("Default", config.ActiveProfile);
        Assert.Single(config.Profiles);
    }
}
