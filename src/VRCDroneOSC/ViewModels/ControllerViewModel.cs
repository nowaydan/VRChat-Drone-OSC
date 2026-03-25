using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCDroneOSC.Models;
using VRCDroneOSC.Services;
using VRCDroneOSC.Views;

namespace VRCDroneOSC.ViewModels;

public partial class ControllerViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string _controllerName = "Not Connected";
    [ObservableProperty] private bool _controllerConnected;
    [ObservableProperty] private ObservableCollection<ControllerBinding> _bindings = new();
    [ObservableProperty] private ObservableCollection<string> _availableControllers = new();
    [ObservableProperty] private int _selectedControllerIndex = -1;
    [ObservableProperty] private bool _deviceListVisible = true;

    // Parallel list of DeviceEntry objects matching AvailableControllers by index
    private List<DeviceEntry> _deviceEntries = new();

    public ControllerViewModel(MainViewModel main)
    {
        _main = main;
        RefreshControllerList();
        RefreshStatus();
        LoadBindings();

        _main.InputManager.ControllerChanged += info =>
        {
            ControllerName = info.IsConnected ? info.Name : "Not Connected";
            ControllerConnected = info.IsConnected;
        };
    }

    private void RefreshStatus()
    {
        ControllerConnected = _main.ControllerConnected;
        ControllerName = _main.ControllerConnected ? _main.ControllerName : "Not Connected";
    }

    private void LoadBindings()
    {
        Bindings.Clear();
        foreach (var b in _main.ActiveProfile.ControllerBindings)
            Bindings.Add(b);
    }

    public void RefreshControllerList()
    {
        // Use the combined SDL + winmm device enumeration
        _deviceEntries = _main.InputManager.GetAvailableDevices();
        AvailableControllers.Clear();
        foreach (var entry in _deviceEntries)
            AvailableControllers.Add(entry.Name);

        Debug.WriteLine($"[ControllerVM] Enumerated {_deviceEntries.Count} devices");

        // Pre-select the currently connected controller if there is one
        if (_main.ControllerConnected && AvailableControllers.Count > 0)
        {
            // Try to match by name first
            int matchIdx = -1;
            for (int i = 0; i < AvailableControllers.Count; i++)
            {
                if (AvailableControllers[i] == _main.ControllerName)
                {
                    matchIdx = i;
                    break;
                }
            }
            SelectedControllerIndex = matchIdx >= 0 ? matchIdx : 0;
        }
        else
        {
            SelectedControllerIndex = -1;
        }
    }

    partial void OnSelectedControllerIndexChanged(int value)
    {
        if (value >= 0 && value < _deviceEntries.Count)
        {
            var entry = _deviceEntries[value];
            Debug.WriteLine($"[ControllerVM] Selected device [{value}]: {entry.Name} (SDL={entry.IsSdlDevice}, idx={entry.SdlIndex})");

            // Use the device-list-aware open method which handles both SDL and WMI devices
            _main.InputManager.OpenDeviceByListIndex(value);
        }
    }

    [RelayCommand]
    private void RefreshController()
    {
        RefreshControllerList();
        if (SelectedControllerIndex < 0 && AvailableControllers.Count == 0)
        {
            _main.InputManager.DetectController();
        }
        RefreshStatus();
        // Ensure device list is visible after refresh
        DeviceListVisible = true;
    }

    [RelayCommand]
    private void ToggleDeviceList()
    {
        DeviceListVisible = !DeviceListVisible;
    }

    [RelayCommand]
    private void AddBinding()
    {
        var dialog = new BindingDialog();
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _main.ActiveProfile.ControllerBindings.Add(dialog.Result);
            Bindings.Add(dialog.Result);
            _main.SaveConfig();
        }
    }

    [RelayCommand]
    private void EditBinding(ControllerBinding binding)
    {
        var dialog = new BindingDialog(binding);
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            int index = _main.ActiveProfile.ControllerBindings.IndexOf(binding);
            if (index >= 0)
                _main.ActiveProfile.ControllerBindings[index] = dialog.Result;

            int uiIndex = Bindings.IndexOf(binding);
            if (uiIndex >= 0)
                Bindings[uiIndex] = dialog.Result;

            _main.SaveConfig();
        }
    }

    [RelayCommand]
    private void DeleteBinding(ControllerBinding binding)
    {
        _main.ActiveProfile.ControllerBindings.Remove(binding);
        Bindings.Remove(binding);
        _main.SaveConfig();
    }
}
