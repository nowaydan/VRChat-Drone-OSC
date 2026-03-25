using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

/// <summary>
/// Represents a device discovered either via SDL or the Windows Multimedia Joystick API (winmm).
/// </summary>
public class DeviceEntry
{
    public string Name { get; set; } = "";
    /// <summary>SDL joystick index, or -1 if this device was found via winmm only.</summary>
    public int SdlIndex { get; set; } = -1;
    /// <summary>True if this device came from SDL enumeration (and can be opened as a joystick).</summary>
    public bool IsSdlDevice { get; set; }
    /// <summary>winmm joystick ID, or -1 if not a winmm device.</summary>
    public int WinmmId { get; set; } = -1;
}

public unsafe class InputManager : IDisposable
{
    // --- winmm P/Invoke declarations ---
    [DllImport("winmm.dll", EntryPoint = "joyGetNumDevs")]
    private static extern uint JoyGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern uint JoyGetDevCaps(uint uJoyID, ref JOYCAPSW pjc, uint cbjc);

    [DllImport("winmm.dll", EntryPoint = "joyGetPosEx")]
    private static extern uint JoyGetPosEx(uint uJoyID, ref JOYINFOEX pji);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JOYCAPSW
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax;
        public uint wNumButtons;
        public uint wPeriodMin, wPeriodMax;
        public uint wRmin, wRmax, wUmin, wUmax, wVmin, wVmax;
        public uint wCaps;
        public uint wMaxAxes, wNumAxes, wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos, dwYpos, dwZpos;
        public uint dwRpos, dwUpos, dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1, dwReserved2;
    }

    private const uint JOYERR_NOERROR = 0;
    private const uint JOY_RETURNALL = 0xFF;

    private Sdl? _sdl;
    private Joystick* _joystick;
    private System.Threading.Thread? _pollThread;
    private volatile bool _running;
    private readonly object _lock = new();

    // Cached axis/button state (written by poll thread, read by callers)
    private short[] _axes = Array.Empty<short>();
    private byte[] _buttons = Array.Empty<byte>();

    // Last enumerated device list (kept in sync with GetAvailableDevices)
    private List<DeviceEntry> _lastDeviceList = new();

    public ControllerInfo ControllerInfo { get; } = new();
    public event Action<ControllerInfo>? ControllerChanged;

    public void Start()
    {
        try
        {
            _sdl = Sdl.GetApi();
            // Initialise both Joystick and GameController subsystems.
            // GameController is a superset that also handles HID gamepads
            // that SDL wouldn't enumerate as plain joysticks.
            // InitEvents is required for SDL_PumpEvents / SDL_JoystickUpdate.
            int result = _sdl.Init(Sdl.InitJoystick | Sdl.InitGamecontroller | Sdl.InitEvents);
            if (result != 0)
            {
                Debug.WriteLine($"[InputManager] SDL_Init failed with code {result}: {_sdl.GetErrorS()}");
            }
            else
            {
                Debug.WriteLine("[InputManager] SDL initialised OK");
            }
        }
        catch (Exception ex)
        {
            // SDL not available — controller will show as disconnected
            Debug.WriteLine($"[InputManager] SDL init exception: {ex.Message}");
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
    /// Returns a list of controller/joystick names detected by SDL.
    /// Calls JoystickUpdate first to ensure the device list is current.
    /// </summary>
    public List<string> GetAvailableControllers()
    {
        if (_sdl == null) return new List<string>();

        // Pump events so SDL refreshes its internal device list
        _sdl.JoystickUpdate();

        var list = new List<string>();
        int count = _sdl.NumJoysticks();
        Debug.WriteLine($"[InputManager] SDL NumJoysticks = {count}");
        for (int i = 0; i < count; i++)
        {
            string name = _sdl.JoystickNameForIndexS(i) ?? $"Controller {i}";
            list.Add(name);
            Debug.WriteLine($"[InputManager]   [{i}] {name}");
        }
        return list;
    }

    /// <summary>
    /// Returns a combined list of devices from SDL joystick enumeration
    /// plus the Windows Multimedia Joystick API (winmm).  SDL devices appear first,
    /// followed by any winmm-only joysticks not already covered by SDL.
    /// Deduplicates by name so the same physical device isn't listed twice.
    /// </summary>
    public List<DeviceEntry> GetAvailableDevices()
    {
        var devices = new List<DeviceEntry>();
        var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // --- SDL devices ---
        if (_sdl != null)
        {
            _sdl.JoystickUpdate();
            int count = _sdl.NumJoysticks();
            Debug.WriteLine($"[InputManager] SDL NumJoysticks = {count}");
            for (int i = 0; i < count; i++)
            {
                string name = _sdl.JoystickNameForIndexS(i) ?? $"Controller {i}";
                devices.Add(new DeviceEntry { Name = name, SdlIndex = i, IsSdlDevice = true });
                nameSet.Add(name);
                Debug.WriteLine($"[InputManager]   SDL [{i}] {name}");
            }
        }

        // --- winmm Joystick API fallback ---
        try
        {
            uint numDevs = JoyGetNumDevs();
            Debug.WriteLine($"[InputManager] winmm JoyGetNumDevs = {numDevs}");
            for (uint id = 0; id < numDevs; id++)
            {
                // Check if a joystick is actually connected at this ID
                var info = new JOYINFOEX
                {
                    dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                    dwFlags = JOY_RETURNALL
                };
                uint result = JoyGetPosEx(id, ref info);
                if (result != JOYERR_NOERROR) continue;

                // Device is connected — get its capabilities and name
                var caps = new JOYCAPSW();
                uint capsResult = JoyGetDevCaps(id, ref caps, (uint)Marshal.SizeOf<JOYCAPSW>());
                if (capsResult != JOYERR_NOERROR) continue;

                string name = GetFullJoystickName(caps);
                if (string.IsNullOrWhiteSpace(name))
                    name = $"Joystick {id}";

                // Deduplicate: skip if SDL already found a device with the same name
                if (nameSet.Contains(name))
                {
                    Debug.WriteLine($"[InputManager]   winmm [{id}] {name} (duplicate of SDL, skipped)");
                    continue;
                }

                devices.Add(new DeviceEntry { Name = name, SdlIndex = -1, IsSdlDevice = false, WinmmId = (int)id });
                nameSet.Add(name);
                Debug.WriteLine($"[InputManager]   winmm [{id}] {name}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InputManager] winmm joystick enumeration failed: {ex.Message}");
        }

        _lastDeviceList = devices;
        return devices;
    }

    /// <summary>
    /// Attempts to retrieve the full product name for a winmm joystick by reading
    /// the OEMName value from the registry.  Falls back to the truncated szPname
    /// (32-char limit) if the registry lookup fails.
    /// </summary>
    private string GetFullJoystickName(JOYCAPSW caps)
    {
        string shortName = caps.szPname?.Trim() ?? "";

        // Try to get full name from registry
        try
        {
            string regKey = caps.szRegKey?.Trim() ?? "";
            if (!string.IsNullOrEmpty(regKey))
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\{regKey}");
                if (key != null)
                {
                    string? oemName = key.GetValue("OEMName") as string;
                    if (!string.IsNullOrEmpty(oemName))
                        return oemName;
                }

                // Try HKLM as fallback
                using var lmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\{regKey}");
                if (lmKey != null)
                {
                    string? oemName = lmKey.GetValue("OEMName") as string;
                    if (!string.IsNullOrEmpty(oemName))
                        return oemName;
                }
            }
        }
        catch
        {
            // Registry access may fail — fall through to short name
        }

        return shortName;
    }

    /// <summary>
    /// Opens the joystick at the specified SDL index (0-based) and updates controller state.
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

            // Pump events before querying count
            _sdl.JoystickUpdate();

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
                    string err = _sdl.GetErrorS() ?? "unknown error";
                    Debug.WriteLine($"[InputManager] JoystickOpen({index}) failed: {err}");
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

                    Debug.WriteLine($"[InputManager] Opened '{name}': {axisCount} axes, {buttonCount} buttons");
                }
            }
        }

        ControllerChanged?.Invoke(ControllerInfo);
    }

    /// <summary>
    /// Opens a device selected from the combined device list (by list index).
    /// If the device is an SDL joystick, opens it directly.
    /// If it's a winmm-only device, attempts to match it to an SDL joystick by name.
    /// </summary>
    public void OpenDeviceByListIndex(int listIndex)
    {
        if (listIndex < 0 || listIndex >= _lastDeviceList.Count) return;

        var entry = _lastDeviceList[listIndex];
        if (entry.IsSdlDevice && entry.SdlIndex >= 0)
        {
            OpenController(entry.SdlIndex);
        }
        else
        {
            // winmm-only device — try to find a matching SDL joystick by name substring
            if (_sdl != null)
            {
                _sdl.JoystickUpdate();
                int count = _sdl.NumJoysticks();
                for (int i = 0; i < count; i++)
                {
                    string sdlName = _sdl.JoystickNameForIndexS(i) ?? "";
                    if (entry.Name.Contains(sdlName, StringComparison.OrdinalIgnoreCase) ||
                        sdlName.Contains(entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[InputManager] Matched winmm device '{entry.Name}' to SDL [{i}] '{sdlName}'");
                        OpenController(i);
                        return;
                    }
                }
            }

            // Could not match to SDL — report as not openable but show correct name
            Debug.WriteLine($"[InputManager] Device '{entry.Name}' (winmm ID={entry.WinmmId}) has no SDL joystick match");
            ControllerInfo.Name = $"{entry.Name} (no joystick driver)";
            ControllerInfo.IsConnected = false;
            ControllerInfo.AxisCount = 0;
            ControllerInfo.ButtonCount = 0;
            ControllerChanged?.Invoke(ControllerInfo);
        }
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

            // Pump events so SDL sees recently-connected devices
            _sdl.JoystickUpdate();

            int count = _sdl.NumJoysticks();
            Debug.WriteLine($"[InputManager] DetectController: NumJoysticks = {count}");
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
                    string err = _sdl.GetErrorS() ?? "unknown error";
                    Debug.WriteLine($"[InputManager] JoystickOpen(0) failed: {err}");
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

                    Debug.WriteLine($"[InputManager] Auto-detected '{name}': {axisCount} axes, {buttonCount} buttons");
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

                    // Pump SDL events so joystick hotplug detection works
                    _sdl.JoystickUpdate();

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
            catch (Exception ex)
            {
                Debug.WriteLine($"[InputManager] Poll error: {ex.Message}");
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
