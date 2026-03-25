using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCDroneOSC.Models;

namespace VRCDroneOSC.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [ObservableProperty] private ObservableCollection<string> _profileNames = new();
    [ObservableProperty] private string _activeProfileName = "";
    [ObservableProperty] private string _selectedProfileName = "";
    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private bool _isNamingNewProfile;

    // Summary fields for selected profile
    [ObservableProperty] private string _summaryPitch  = "";
    [ObservableProperty] private string _summaryRoll   = "";
    [ObservableProperty] private string _summaryYaw    = "";
    [ObservableProperty] private string _summaryPhysics = "";
    [ObservableProperty] private int    _summaryBindingCount;

    public ProfilesViewModel(MainViewModel main)
    {
        _main = main;
        Refresh();
    }

    private void Refresh()
    {
        ProfileNames.Clear();
        foreach (var key in _main.Config.Profiles.Keys)
            ProfileNames.Add(key);

        ActiveProfileName  = _main.Config.ActiveProfile;
        SelectedProfileName = ActiveProfileName;
        UpdateSummary();
    }

    partial void OnSelectedProfileNameChanged(string value) => UpdateSummary();

    private void UpdateSummary()
    {
        if (!_main.Config.Profiles.TryGetValue(SelectedProfileName, out var profile))
        {
            SummaryPitch  = "";
            SummaryRoll   = "";
            SummaryYaw    = "";
            SummaryPhysics = "";
            SummaryBindingCount = 0;
            return;
        }

        SummaryPitch  = $"Center {profile.PitchRate.Center:F0}  Max {profile.PitchRate.MaxRate:F0}  Expo {profile.PitchRate.Expo:F2}";
        SummaryRoll   = $"Center {profile.RollRate.Center:F0}  Max {profile.RollRate.MaxRate:F0}  Expo {profile.RollRate.Expo:F2}";
        SummaryYaw    = $"Center {profile.YawRate.Center:F0}  Max {profile.YawRate.MaxRate:F0}  Expo {profile.YawRate.Expo:F2}";
        SummaryPhysics = $"Mass {profile.Physics.Mass:F2}  Drag {profile.Physics.LinearDrag:F1}  Hover {profile.Physics.HoverThrottle:F2}";
        SummaryBindingCount = profile.ControllerBindings.Count;
    }

    [RelayCommand]
    private void SwitchProfile(string name)
    {
        _main.SwitchProfile(name);
        ActiveProfileName   = _main.Config.ActiveProfile;
        SelectedProfileName = name;
    }

    [RelayCommand]
    private void BeginNewProfile()
    {
        NewProfileName      = "";
        IsNamingNewProfile  = true;
    }

    [RelayCommand]
    private void ConfirmNewProfile()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrEmpty(name) || _main.Config.Profiles.ContainsKey(name))
            return;

        _main.Config.Profiles[name] = new FlightProfile();
        _main.SaveConfig();
        ProfileNames.Add(name);
        SelectedProfileName = name;
        IsNamingNewProfile  = false;
    }

    [RelayCommand]
    private void CancelNewProfile()
    {
        IsNamingNewProfile = false;
        NewProfileName     = "";
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        var sourceName = SelectedProfileName;
        if (!_main.Config.Profiles.TryGetValue(sourceName, out var source)) return;

        // Deep-copy via JSON round-trip
        var json  = JsonSerializer.Serialize(source, JsonOpts);
        var clone = JsonSerializer.Deserialize<FlightProfile>(json, JsonOpts) ?? new FlightProfile();

        // Find a unique name
        string baseName = $"{sourceName} (copy)";
        string newName  = baseName;
        int counter = 2;
        while (_main.Config.Profiles.ContainsKey(newName))
            newName = $"{baseName} {counter++}";

        _main.Config.Profiles[newName] = clone;
        _main.SaveConfig();
        ProfileNames.Add(newName);
        SelectedProfileName = newName;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (_main.Config.Profiles.Count <= 1) return; // prevent deleting last profile
        if (!_main.Config.Profiles.ContainsKey(SelectedProfileName)) return;

        _main.Config.Profiles.Remove(SelectedProfileName);
        ProfileNames.Remove(SelectedProfileName);

        // If we deleted the active profile, switch to the first remaining one
        if (ActiveProfileName == SelectedProfileName)
        {
            var first = _main.Config.Profiles.Keys.First();
            _main.SwitchProfile(first);
            ActiveProfileName = first;
        }

        SelectedProfileName = _main.Config.Profiles.Keys.First();
        _main.SaveConfig();
        UpdateSummary();
    }
}
