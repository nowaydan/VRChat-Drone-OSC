using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCDroneOSC.Models;
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
        var controllers = _main.InputManager.GetAvailableControllers();
        AvailableControllers.Clear();
        foreach (var name in controllers)
            AvailableControllers.Add(name);

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
        if (value >= 0)
            _main.InputManager.OpenController(value);
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
