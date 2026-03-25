using System;
using System.Collections.Generic;
using System.Threading;
using Silk.NET.SDL;
using VRCDroneOSC.Models;

namespace VRCDroneOSC.Services;

public class ControllerInfo
{
    public string Name { get; set; } = "Not Connected";
    public bool IsConnected { get; set; }
    public int AxisCount { get; set; }
    public int ButtonCount { get; set; }
}

public unsafe class InputManager : IDisposable
{
    private Sdl? _sdl;
    private Joystick* _joystick;
    private System.Threading.Thread? _pollThread;
    private volatile bool _running;
    private readonly object _lock = new();

    // Cached axis/button state (written by poll thread, read by callers)
    private short[] _axes = Array.Empty<short>();
    private byte[] _buttons = Array.Empty<byte>();

    public ControllerInfo ControllerInfo { get; } = new();
    public event Action<ControllerInfo>? ControllerChanged;

    public void Start()
    {
        try
        {
            _sdl = Sdl.GetApi();
            _sdl.Init(Sdl.InitJoystick);
        }
        catch (Exception)
        {
            // SDL not available — controller will show as disconnected
            _sdl = null;
        }

        _running = true;
        _pollThread = new System.Threading.Thread(PollLoop)
        {
            IsBackground = true,
            Name = "InputManager.PollLoop"
        };
        _pollThread.Start();
    }

    /// <summary>
    /// Returns a list of all available joystick/controller names detected by SDL.
    /// </summary>
    public List<string> GetAvailableControllers()
    {
        if (_sdl == null) return new List<string>();
        var list = new List<string>();
        int count = _sdl.NumJoysticks();
        for (int i = 0; i < count; i++)
        {
            string name = _sdl.JoystickNameForIndexS(i) ?? $"Controller {i}";
            list.Add(name);
        }
        return list;
    }

    /// <summary>
    /// Opens the joystick at the specified index (0-based) and updates controller state.
    /// </summary>
    public void OpenController(int index)
    {
        if (_sdl == null) return;

        lock (_lock)
        {
            // Close existing joystick if open
            if (_joystick != null)
            {
                _sdl.JoystickClose(_joystick);
                _joystick = null;
            }

            int count = _sdl.NumJoysticks();
            if (index < 0 || index >= count)
            {
                _axes = Array.Empty<short>();
                _buttons = Array.Empty<byte>();
                ControllerInfo.Name = "Not Connected";
                ControllerInfo.IsConnected = false;
                ControllerInfo.AxisCount = 0;
                ControllerInfo.ButtonCount = 0;
            }
            else
            {
                _joystick = _sdl.JoystickOpen(index);
                if (_joystick == null)
                {
                    ControllerInfo.Name = "Not Connected";
                    ControllerInfo.IsConnected = false;
                    ControllerInfo.AxisCount = 0;
                    ControllerInfo.ButtonCount = 0;
                }
                else
                {
                    int axisCount = _sdl.JoystickNumAxes(_joystick);
                    int buttonCount = _sdl.JoystickNumButtons(_joystick);
                    string name = _sdl.JoystickNameForIndexS(index) ?? $"Controller {index}";

                    _axes = new short[axisCount > 0 ? axisCount : 0];
                    _buttons = new byte[buttonCount > 0 ? buttonCount : 0];

                    ControllerInfo.Name = name;
                    ControllerInfo.IsConnected = true;
                    ControllerInfo.AxisCount = axisCount;
                    ControllerInfo.ButtonCount = buttonCount;
                }
            }
        }

        ControllerChanged?.Invoke(ControllerInfo);
    }

    public void DetectController()
    {
        if (_sdl == null) return;

        lock (_lock)
        {
            // Close existing joystick if open
            if (_joystick != null)
            {
                _sdl.JoystickClose(_joystick);
                _joystick = null;
            }

            int count = _sdl.NumJoysticks();
            if (count <= 0)
            {
                _axes = Array.Empty<short>();
                _buttons = Array.Empty<byte>();
                ControllerInfo.Name = "Not Connected";
                ControllerInfo.IsConnected = false;
                ControllerInfo.AxisCount = 0;
                ControllerInfo.ButtonCount = 0;
            }
            else
            {
                _joystick = _sdl.JoystickOpen(0);
                if (_joystick == null)
                {
                    ControllerInfo.Name = "Not Connected";
                    ControllerInfo.IsConnected = false;
                    ControllerInfo.AxisCount = 0;
                    ControllerInfo.ButtonCount = 0;
                }
                else
                {
                    int axisCount = _sdl.JoystickNumAxes(_joystick);
                    int buttonCount = _sdl.JoystickNumButtons(_joystick);
                    string name = _sdl.JoystickNameForIndexS(0) ?? "Unknown Controller";

                    _axes = new short[axisCount > 0 ? axisCount : 0];
                    _buttons = new byte[buttonCount > 0 ? buttonCount : 0];

                    ControllerInfo.Name = name;
                    ControllerInfo.IsConnected = true;
                    ControllerInfo.AxisCount = axisCount;
                    ControllerInfo.ButtonCount = buttonCount;
                }
            }
        }

        ControllerChanged?.Invoke(ControllerInfo);
    }

    private void PollLoop()
    {
        while (_running)
        {
            try
            {
                bool wasConnected = ControllerInfo.IsConnected;

                lock (_lock)
                {
                    if (_sdl == null) goto Sleep;

                    // If no joystick open, try to detect one
                    if (_joystick == null)
                    {
                        int count = _sdl.NumJoysticks();
                        if (count > 0)
                        {
                            // Release lock to call DetectController cleanly
                            goto DetectOutside;
                        }
                        else if (wasConnected)
                        {
                            // Was connected, now lost
                            ControllerInfo.Name = "Not Connected";
                            ControllerInfo.IsConnected = false;
                            ControllerInfo.AxisCount = 0;
                            ControllerInfo.ButtonCount = 0;
                            goto NotifyOutside;
                        }
                        goto Sleep;
                    }

                    // Poll axis values
                    for (int i = 0; i < _axes.Length; i++)
                        _axes[i] = _sdl.JoystickGetAxis(_joystick, i);

                    // Poll button values
                    for (int i = 0; i < _buttons.Length; i++)
                        _buttons[i] = _sdl.JoystickGetButton(_joystick, i);

                    goto Sleep;
                }

                DetectOutside:
                DetectController();
                goto Sleep;

                NotifyOutside:
                ControllerChanged?.Invoke(ControllerInfo);

                Sleep:
                System.Threading.Thread.Sleep(2); // ~500 Hz
            }
            catch (Exception)
            {
                // Swallow errors in poll loop; will retry on next iteration
                System.Threading.Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Returns a normalized axis value in the range [-1.0, 1.0].
    /// SDL raw values are in the range [-32768, 32767].
    /// </summary>
    public double GetAxisValue(int sdlAxisIndex)
    {
        lock (_lock)
        {
            if (sdlAxisIndex < 0 || sdlAxisIndex >= _axes.Length) return 0.0;
            return Math.Clamp(_axes[sdlAxisIndex] / 32768.0, -1.0, 1.0);
        }
    }

    /// <summary>
    /// Returns true if the specified SDL button is currently pressed.
    /// </summary>
    public bool GetButtonValue(int sdlButtonIndex)
    {
        lock (_lock)
        {
            if (sdlButtonIndex < 0 || sdlButtonIndex >= _buttons.Length) return false;
            return _buttons[sdlButtonIndex] != 0;
        }
    }

    /// <summary>
    /// Returns a control value for the given mapping. Checks SdlButton first,
    /// then SdlAxis, then derives from JoystickOffset as an axis index fallback.
    /// </summary>
    public double GetControlValue(ControlMapping mapping)
    {
        if (mapping.SdlButton.HasValue)
        {
            return GetButtonValue(mapping.SdlButton.Value) ? 1.0 : 0.0;
        }

        if (mapping.SdlAxis.HasValue)
        {
            return GetAxisValue(mapping.SdlAxis.Value);
        }

        // Fallback: treat JoystickOffset as an axis index
        return GetAxisValue(mapping.JoystickOffset);
    }

    public void Stop()
    {
        _running = false;
        _pollThread?.Join(500);
        _pollThread = null;

        lock (_lock)
        {
            if (_sdl != null && _joystick != null)
            {
                _sdl.JoystickClose(_joystick);
                _joystick = null;
            }
        }

        _sdl?.Quit();
        _sdl?.Dispose();
        _sdl = null;
    }

    public void Dispose() => Stop();
}
