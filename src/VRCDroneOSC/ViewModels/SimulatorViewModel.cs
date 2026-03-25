using System;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCDroneOSC.Services;

namespace VRCDroneOSC.ViewModels;

public partial class SimulatorViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _uiTimer;

    // Position and rotation exposed for code-behind to drive 3D transforms
    [ObservableProperty] private double _posX;
    [ObservableProperty] private double _posY;
    [ObservableProperty] private double _posZ;
    [ObservableProperty] private double _rotPitch;
    [ObservableProperty] private double _rotRoll;
    [ObservableProperty] private double _rotYaw;

    public event Action? StateUpdated;

    public SimulatorViewModel(MainViewModel main)
    {
        _main = main;
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += (_, _) => Poll();
        _uiTimer.Start();
    }

    private void Poll()
    {
        var state = _main.PhysicsEngine.GetState();

        PosX = state.PositionX;
        PosY = state.PositionY;
        PosZ = state.PositionZ;

        RotPitch = state.Pitch;
        RotRoll  = state.Roll;
        RotYaw   = state.Yaw;

        StateUpdated?.Invoke();
    }

    [RelayCommand]
    private void ResetSimulator()
    {
        _main.PhysicsEngine.Reset();
    }
}
