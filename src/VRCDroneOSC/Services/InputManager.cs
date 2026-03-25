using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
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
/// Represents a device discovered via the Windows Raw Input API + HID API,
/// optionally matched to an SDL joystick for axis/button reading.
/// </summary>
public class DeviceEntry
{
    public string Name { get; set; } = "";
    /// <summary>SDL joystick index, or -1 if no SDL match was found.</summary>
    public int SdlIndex { get; set; } = -1;
    /// <summary>True if this device was matched to an SDL joystick.</summary>
    public bool IsSdlDevice { get; set; }
    /// <summary>Raw Input device path (e.g. \\?\HID#VID_xxxx&amp;PID_xxxx...).</summary>
    public string DevicePath { get; set; } = "";
}

public unsafe class InputManager : IDisposable
{
    // --- Raw Input P/Invoke declarations ---

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [In, Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfoW(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    // --- HID P/Invoke declarations ---

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(
        SafeFileHandle HidDeviceObject,
        byte[] Buffer,
        uint BufferLength);

    // --- Kernel32 for opening HID device handles ---

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIM_TYPEHID = 2;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;

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

            // Set hints before init for better device detection
            _sdl.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            _sdl.SetHint("SDL_HINT_JOYSTICK_RAWINPUT", "1");

            int result = _sdl.Init(Sdl.InitJoystick | Sdl.InitGamecontroller | Sdl.InitEvents);
            if (result != 0)
            {
                Debug.WriteLine($"[InputManager] SDL_Init failed with code {result}: {_sdl.GetErrorS()}");
            }
            else
            {
                Debug.WriteLine("[InputManager] SDL initialised OK");
                // Pump events immediately to populate device list
                _sdl.PumpEvents();
                _sdl.JoystickUpdate();
                int count = _sdl.NumJoysticks();
                Debug.WriteLine($"[InputManager] Initial SDL NumJoysticks = {count}");
                for (int i = 0; i < count; i++)
                    Debug.WriteLine($"[InputManager]   SDL[{i}] = {_sdl.JoystickNameForIndexS(i)}");
            }
        }
        catch (Exception ex)
        {
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
    /// Enumerates ALL input devices using the Windows Raw Input API and resolves
    /// friendly names via the HID product string. Each HID device is also matched
    /// against SDL joysticks so it can be opened for axis/button input.
    /// Mouse and Keyboard entries are included with generic names (deduplicated).
    /// </summary>
    public List<DeviceEntry> GetAvailableDevices()
    {
        var devices = new List<DeviceEntry>();
        var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Step 1: Get Raw Input device list
        uint deviceCount = 0;
        uint structSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref deviceCount, structSize);

        if (deviceCount == 0)
        {
            Debug.WriteLine("[InputManager] Raw Input: 0 devices");
            _lastDeviceList = devices;
            return devices;
        }

        var rawDevices = new RAWINPUTDEVICELIST[deviceCount];
        GetRawInputDeviceList(rawDevices, ref deviceCount, structSize);
        Debug.WriteLine($"[InputManager] Raw Input: {deviceCount} devices");

        for (uint i = 0; i < deviceCount; i++)
        {
            var rawDev = rawDevices[i];

            // Get device path
            uint pathSize = 0;
            GetRawInputDeviceInfoW(rawDev.hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref pathSize);
            if (pathSize == 0) continue;

            IntPtr pathBuffer = Marshal.AllocHGlobal((int)(pathSize * 2));
            try
            {
                GetRawInputDeviceInfoW(rawDev.hDevice, RIDI_DEVICENAME, pathBuffer, ref pathSize);
                string devicePath = Marshal.PtrToStringUni(pathBuffer) ?? "";

                // Skip RDP/virtual devices
                if (devicePath.Contains("RDP_", StringComparison.OrdinalIgnoreCase)) continue;
                if (devicePath.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase)) continue;

                // Get friendly name
                string name = GetDeviceFriendlyName(devicePath, rawDev.dwType);

                if (string.IsNullOrWhiteSpace(name) || nameSet.Contains(name)) continue;
                nameSet.Add(name);

                // Try to match to SDL joystick
                int sdlIndex = TryMatchSdlJoystick(name);

                devices.Add(new DeviceEntry
                {
                    Name = name,
                    SdlIndex = sdlIndex,
                    IsSdlDevice = sdlIndex >= 0,
                    DevicePath = devicePath
                });

                Debug.WriteLine($"[InputManager]   [{i}] \"{name}\" SDL={sdlIndex} path={devicePath}");
            }
            finally
            {
                Marshal.FreeHGlobal(pathBuffer);
            }
        }

        _lastDeviceList = devices;
        return devices;
    }

    /// <summary>
    /// Resolves a human-readable device name from a Raw Input device path.
    /// For HID devices, opens the device handle and reads the USB product string.
    /// For mice and keyboards, returns generic type names.
    /// </summary>
    private string GetDeviceFriendlyName(string devicePath, uint deviceType)
    {
        // For HID devices, try to read USB product string
        if (deviceType == RIM_TYPEHID)
        {
            try
            {
                // Open device with sharing for reading
                using var handle = CreateFileW(
                    devicePath,
                    0, // No access rights needed for HidD_GetProductString
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    byte[] buffer = new byte[512];
                    if (HidD_GetProductString(handle, buffer, (uint)buffer.Length))
                    {
                        string name = Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }
                }
            }
            catch
            {
                // Some devices refuse to open — fall through to path extraction
            }

            // Fallback: extract something useful from the device path
            return ExtractNameFromPath(devicePath);
        }

        // For mice and keyboards, use generic names (they share paths)
        if (deviceType == RIM_TYPEMOUSE) return "Mouse";
        if (deviceType == RIM_TYPEKEYBOARD) return "Keyboard";

        return "";
    }

    /// <summary>
    /// Last-resort name extraction from a Raw Input device path.
    /// Paths look like: \\?\HID#VID_2770&amp;PID_0901&amp;MI_00#...
    /// </summary>
    private static string ExtractNameFromPath(string path)
    {
        // Try to extract VID/PID for a minimal identifier
        int vidIdx = path.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        int pidIdx = path.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
        if (vidIdx >= 0 && pidIdx >= 0)
        {
            string vid = path.Substring(vidIdx + 4, Math.Min(4, path.Length - vidIdx - 4)).Split('&')[0];
            string pid = path.Substring(pidIdx + 4, Math.Min(4, path.Length - pidIdx - 4)).Split('&')[0];
            return $"HID Device (VID_{vid}&PID_{pid})";
        }
        return "Unknown HID Device";
    }

    /// <summary>
    /// Attempts to find an SDL joystick whose name matches the given device name.
    /// Returns the SDL joystick index, or -1 if no match is found.
    /// </summary>
    private int TryMatchSdlJoystick(string name)
    {
        if (_sdl == null) return -1;
        _sdl.PumpEvents();
        _sdl.JoystickUpdate();
        int count = _sdl.NumJoysticks();
        for (int i = 0; i < count; i++)
        {
            string sdlName = _sdl.JoystickNameForIndexS(i) ?? "";
            if (string.IsNullOrEmpty(sdlName)) continue;
            if (name.Equals(sdlName, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(sdlName, StringComparison.OrdinalIgnoreCase) ||
                sdlName.Contains(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
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
    /// If the device has an SDL joystick match, opens it directly.
    /// Otherwise, attempts to match it to an SDL joystick by name.
    /// </summary>
    public void OpenDeviceByListIndex(int listIndex)
    {
        if (listIndex < 0 || listIndex >= _lastDeviceList.Count) return;

        var entry = _lastDeviceList[listIndex];

        // Strategy 1: If already matched to SDL, open directly
        if (entry.IsSdlDevice && entry.SdlIndex >= 0)
        {
            OpenController(entry.SdlIndex);
            return;
        }

        if (_sdl == null) goto NotFound;

        // Strategy 2: Pump events aggressively and re-scan
        _sdl.PumpEvents();
        _sdl.JoystickUpdate();
        System.Threading.Thread.Sleep(50); // Give SDL a moment
        _sdl.PumpEvents();
        _sdl.JoystickUpdate();

        int count = _sdl.NumJoysticks();
        Debug.WriteLine($"[InputManager] OpenDevice: SDL sees {count} joysticks after pump");

        // Strategy 3: Try name matching against all SDL joysticks
        for (int i = 0; i < count; i++)
        {
            string sdlName = _sdl.JoystickNameForIndexS(i) ?? "";
            Debug.WriteLine($"[InputManager]   SDL[{i}] = '{sdlName}'");
            if (!string.IsNullOrEmpty(sdlName) && (
                entry.Name.Contains(sdlName, StringComparison.OrdinalIgnoreCase) ||
                sdlName.Contains(entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.WriteLine($"[InputManager] Name-matched '{entry.Name}' → SDL[{i}] '{sdlName}'");
                OpenController(i);
                return;
            }
        }

        // Strategy 4: If SDL has exactly 1 joystick, assume it's the one the user selected
        // (common case: only one controller plugged in)
        if (count == 1)
        {
            Debug.WriteLine($"[InputManager] Only 1 SDL joystick — assuming it's '{entry.Name}'");
            OpenController(0);
            return;
        }

        // Strategy 5: If SDL has any joysticks and the device path suggests it's a game controller,
        // try opening each one and check if it has axes (a real joystick)
        if (count > 1)
        {
            for (int i = 0; i < count; i++)
            {
                OpenController(i);
                if (ControllerInfo.IsConnected && ControllerInfo.AxisCount > 0)
                {
                    Debug.WriteLine($"[InputManager] Found joystick with axes at SDL[{i}]");
                    // Update the display name to what the user selected
                    ControllerInfo.Name = entry.Name;
                    ControllerChanged?.Invoke(ControllerInfo);
                    return;
                }
            }
        }

        // Strategy 6: Force-try opening SDL indices 0-15 even if NumJoysticks said 0
        // Some SDL builds on Windows don't enumerate properly but can still open
        if (count == 0)
        {
            for (int i = 0; i < 16; i++)
            {
                lock (_lock)
                {
                    if (_joystick != null)
                    {
                        _sdl.JoystickClose(_joystick);
                        _joystick = null;
                    }

                    var js = _sdl.JoystickOpen(i);
                    if (js != null)
                    {
                        int axes = _sdl.JoystickNumAxes(js);
                        int buttons = _sdl.JoystickNumButtons(js);
                        if (axes > 0 || buttons > 0)
                        {
                            _joystick = js;
                            _axes = new short[axes > 0 ? axes : 0];
                            _buttons = new byte[buttons > 0 ? buttons : 0];
                            ControllerInfo.Name = entry.Name;
                            ControllerInfo.IsConnected = true;
                            ControllerInfo.AxisCount = axes;
                            ControllerInfo.ButtonCount = buttons;
                            Debug.WriteLine($"[InputManager] Force-opened SDL[{i}]: {axes} axes, {buttons} buttons");
                            ControllerChanged?.Invoke(ControllerInfo);
                            return;
                        }
                        _sdl.JoystickClose(js);
                    }
                }
            }
        }

        NotFound:
        Debug.WriteLine($"[InputManager] Device '{entry.Name}' could not be opened via SDL");
        ControllerInfo.Name = entry.Name;
        ControllerInfo.IsConnected = false;
        ControllerInfo.AxisCount = 0;
        ControllerInfo.ButtonCount = 0;
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
