using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCDroneOSC.Models;
using VRCDroneOSC.Services;
using VRCDroneOSC.Views;

namespace VRCDroneOSC.ViewModels;

// ─── Display models for live debug panel ────────────────────────────────────

public partial class AxisDisplay : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _value;

    // Normalized to 0..1 for ProgressBar width
    public double NormalizedValue => (Value + 1.0) / 2.0;

    partial void OnValueChanged(double value)
        => OnPropertyChanged(nameof(NormalizedValue));
}

public partial class ButtonDisplay : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private bool _isPressed;
}

// ─── ControllerViewModel ─────────────────────────────────────────────────────

public partial class ControllerViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _suppressSelectionHandler;
    private DispatcherTimer? _debugTimer;

    [ObservableProperty] private string _controllerName = "Not Connected";
    [ObservableProperty] private bool _controllerConnected;
    [ObservableProperty] private ObservableCollection<ControllerBinding> _bindings = new();
    [ObservableProperty] private ObservableCollection<string> _availableControllers = new();
    [ObservableProperty] private int _selectedControllerIndex = -1;

    // Debug panel
    [ObservableProperty] private ObservableCollection<AxisDisplay> _axisValues = new();
    [ObservableProperty] private ObservableCollection<ButtonDisplay> _buttonStates = new();
    [ObservableProperty] private bool _isDebugVisible;

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
            RebuildDebugCollections(info);
        };

        // Initialize collections for whatever is already connected
        RebuildDebugCollections(_main.InputManager.ControllerInfo);

        // 30 fps debug timer
        _debugTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _debugTimer.Tick += OnDebugTick;
        _debugTimer.Start();
    }

    // ─── Debug panel helpers ──────────────────────────────────────────────

    private void RebuildDebugCollections(ControllerInfo info)
    {
        AxisValues.Clear();
        ButtonStates.Clear();

        if (!info.IsConnected) return;

        for (int i = 0; i < info.AxisCount; i++)
            AxisValues.Add(new AxisDisplay { Name = $"Axis {i}" });

        for (int i = 0; i < info.ButtonCount; i++)
            ButtonStates.Add(new ButtonDisplay { Index = i });
    }

    private void OnDebugTick(object? sender, EventArgs e)
    {
        if (!IsDebugVisible) return;

        var im = _main.InputManager;
        for (int i = 0; i < AxisValues.Count; i++)
            AxisValues[i].Value = im.GetAxisValue(i);

        for (int i = 0; i < ButtonStates.Count; i++)
            ButtonStates[i].IsPressed = im.GetButtonValue(i);
    }

    [RelayCommand]
    private void ToggleDebug() => IsDebugVisible = !IsDebugVisible;

    // ─── Controller list / status ─────────────────────────────────────────

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
        _suppressSelectionHandler = true;

        _deviceEntries = _main.InputManager.GetAvailableDevices();
        AvailableControllers.Clear();
        foreach (var entry in _deviceEntries)
            AvailableControllers.Add(entry.Name);

        Debug.WriteLine($"[ControllerVM] Enumerated {_deviceEntries.Count} devices");

        // Restore selection to the currently connected controller
        int matchIdx = -1;
        if (_main.ControllerConnected)
        {
            for (int i = 0; i < AvailableControllers.Count; i++)
            {
                if (AvailableControllers[i] == _main.ControllerName)
                {
                    matchIdx = i;
                    break;
                }
            }
        }

        // Also try matching by saved controller name in config
        if (matchIdx < 0 && !string.IsNullOrEmpty(_main.Config.ControllerName))
        {
            for (int i = 0; i < AvailableControllers.Count; i++)
            {
                if (AvailableControllers[i] == _main.Config.ControllerName)
                {
                    matchIdx = i;
                    break;
                }
            }
        }

        SelectedControllerIndex = matchIdx;
        _suppressSelectionHandler = false;
    }

    partial void OnSelectedControllerIndexChanged(int value)
    {
        if (_suppressSelectionHandler) return;
        if (value >= 0 && value < _deviceEntries.Count)
        {
            var entry = _deviceEntries[value];
            Debug.WriteLine($"[ControllerVM] Selected device [{value}]: {entry.Name}");

            _main.InputManager.OpenDeviceByListIndex(value);

            // Save the selected controller name so it persists
            _main.Config.ControllerName = entry.Name;
            _main.SaveConfig();
        }
    }

    [RelayCommand]
    private void RefreshController()
    {
        // Re-scan devices but keep the current connection alive
        // Only call DetectController if nothing is currently connected
        RefreshControllerList();
        RefreshStatus();

        // If we found the previously selected controller, re-select it
        if (SelectedControllerIndex >= 0 && !_main.ControllerConnected)
        {
            // Try to connect the selected device
            _suppressSelectionHandler = false;
            OnSelectedControllerIndexChanged(SelectedControllerIndex);
        }
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
