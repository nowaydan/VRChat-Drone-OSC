using System;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VRCDroneOSC.ViewModels;

public partial class FlightViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _uiTimer;

    [ObservableProperty] private double _stickLeftX;
    [ObservableProperty] private double _stickLeftY;
    [ObservableProperty] private double _stickRightX;
    [ObservableProperty] private double _stickRightY;
    [ObservableProperty] private double _throttlePercent;
    [ObservableProperty] private double _pitchOut;
    [ObservableProperty] private double _rollOut;
    [ObservableProperty] private double _yawOut;
    [ObservableProperty] private string _controllerStatus = "Not Connected";
    [ObservableProperty] private string _oscStatus = "Not Connected";
    [ObservableProperty] private bool _controllerConnected;
    [ObservableProperty] private bool _oscConnected;

    public FlightViewModel(MainViewModel main)
    {
        _main = main;
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += (_, _) => UpdateTelemetry();
        _uiTimer.Start();
    }

    private void UpdateTelemetry()
    {
        var loop = _main.FlightLoop;
        if (loop == null) return;

        StickLeftX = loop.StickYaw;
        StickLeftY = loop.StickThrottle;
        StickRightX = loop.StickRoll;
        StickRightY = loop.StickPitch;

        ThrottlePercent = loop.ThrottleOut;
        PitchOut = loop.PitchOut;
        RollOut = loop.RollOut;
        YawOut = loop.YawOut;

        ControllerConnected = _main.ControllerConnected;
        OscConnected = _main.OscConnected;
        ControllerStatus = _main.ControllerConnected ? _main.ControllerName : "Not Connected";
        OscStatus = _main.OscConnected ? "Connected (127.0.0.1:9000)" : "Not Connected";
    }
}
