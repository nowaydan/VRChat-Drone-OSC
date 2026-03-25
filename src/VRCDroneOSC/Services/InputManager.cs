using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Vortice.DirectInput;
using VRCDroneOSC.Models;

namespace VRCDroneOSC.Services;

public class ControllerInfo
{
    public string Name { get; set; } = "Not Connected";
    public bool IsConnected { get; set; }
    public int AxisCount { get; set; }
    public int ButtonCount { get; set; }
}

public class DeviceEntry
{
    public string Name { get; set; } = "";
    public Guid InstanceGuid { get; set; }
    public Guid ProductGuid { get; set; }
    public string DevicePath { get; set; } = "";
    public bool IsGameController { get; set; }
}

public class InputManager : IDisposable
{
    // --- Raw Input + HID P/Invoke for device name resolution ---

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [In, Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
        ref uint puiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfoW(
        IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(
        SafeFileHandle HidDeviceObject, byte[] Buffer, uint BufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }

    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIM_TYPEHID = 2;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;

    // --- DirectInput for actual joystick I/O ---

    private IDirectInput8? _directInput;
    private IDirectInputDevice8? _device;
    private Thread? _pollThread;
    private volatile bool _running;
    private readonly object _lock = new();

    // Cached state
    private int[] _axes = Array.Empty<int>(); // DirectInput axes are int (-10000 to 10000 or 0 to 65535)
    private bool[] _buttons = Array.Empty<bool>();
    private List<DeviceEntry> _lastDeviceList = new();
    private Guid _openedInstanceGuid = Guid.Empty;

    public ControllerInfo ControllerInfo { get; } = new();
    public event Action<ControllerInfo>? ControllerChanged;

    public void Start()
    {
        try
        {
            _directInput = DInput.DirectInput8Create();
            Debug.WriteLine("[InputManager] DirectInput8 created OK");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InputManager] DirectInput8 init failed: {ex.Message}");
            _directInput = null;
        }

        _running = true;
        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "InputManager.PollLoop" };
        _pollThread.Start();
    }

    /// <summary>
    /// Lists all input devices using Raw Input API for names + DirectInput for game controllers.
    /// </summary>
    public List<DeviceEntry> GetAvailableDevices()
    {
        var devices = new List<DeviceEntry>();
        var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // DirectInput game controllers (these are the ones we can actually open)
        if (_directInput != null)
        {
            try
            {
                var diDevices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
                foreach (var di in diDevices)
                {
                    string name = di.InstanceName ?? di.ProductName ?? "Unknown Controller";
                    if (nameSet.Contains(name)) continue;
                    nameSet.Add(name);

                    devices.Add(new DeviceEntry
                    {
                        Name = name,
                        InstanceGuid = di.InstanceGuid,
                        ProductGuid = di.ProductGuid,
                        IsGameController = true
                    });
                    Debug.WriteLine($"[InputManager] DInput: '{name}' guid={di.InstanceGuid}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InputManager] DInput enumeration failed: {ex.Message}");
            }
        }

        // Raw Input HID devices for additional entries (non-game-controllers)
        try
        {
            uint deviceCount = 0;
            uint structSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            GetRawInputDeviceList(null, ref deviceCount, structSize);

            if (deviceCount > 0)
            {
                var rawDevices = new RAWINPUTDEVICELIST[deviceCount];
                GetRawInputDeviceList(rawDevices, ref deviceCount, structSize);

                for (uint i = 0; i < deviceCount; i++)
                {
                    var rawDev = rawDevices[i];

                    uint pathSize = 0;
                    GetRawInputDeviceInfoW(rawDev.hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref pathSize);
                    if (pathSize == 0) continue;

                    IntPtr pathBuffer = Marshal.AllocHGlobal((int)(pathSize * 2));
                    try
                    {
                        GetRawInputDeviceInfoW(rawDev.hDevice, RIDI_DEVICENAME, pathBuffer, ref pathSize);
                        string devicePath = Marshal.PtrToStringUni(pathBuffer) ?? "";

                        if (devicePath.Contains("RDP_", StringComparison.OrdinalIgnoreCase)) continue;
                        if (devicePath.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase)) continue;

                        string name = GetDeviceFriendlyName(devicePath, rawDev.dwType);
                        if (string.IsNullOrWhiteSpace(name) || nameSet.Contains(name)) continue;
                        nameSet.Add(name);

                        devices.Add(new DeviceEntry
                        {
                            Name = name,
                            DevicePath = devicePath,
                            IsGameController = false
                        });
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pathBuffer);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InputManager] Raw Input enumeration failed: {ex.Message}");
        }

        _lastDeviceList = devices;
        return devices;
    }

    /// <summary>
    /// Opens a device from the device list by index.
    /// Only DirectInput game controllers can actually be opened for input.
    /// </summary>
    public void OpenDeviceByListIndex(int listIndex)
    {
        if (listIndex < 0 || listIndex >= _lastDeviceList.Count) return;
        var entry = _lastDeviceList[listIndex];

        if (entry.IsGameController && entry.InstanceGuid != Guid.Empty)
        {
            OpenDirectInputDevice(entry.InstanceGuid, entry.Name);
        }
        else
        {
            // Not a game controller — try to find a DirectInput match by name
            if (_directInput != null)
            {
                var diDevices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
                foreach (var di in diDevices)
                {
                    string diName = di.InstanceName ?? di.ProductName ?? "";
                    if (entry.Name.Contains(diName, StringComparison.OrdinalIgnoreCase) ||
                        diName.Contains(entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        OpenDirectInputDevice(di.InstanceGuid, entry.Name);
                        return;
                    }
                }
            }

            ControllerInfo.Name = entry.Name;
            ControllerInfo.IsConnected = false;
            ControllerChanged?.Invoke(ControllerInfo);
        }
    }

    private void OpenDirectInputDevice(Guid instanceGuid, string displayName)
    {
        if (_directInput == null) return;

        lock (_lock)
        {
            // Close existing device
            _device?.Unacquire();
            _device?.Dispose();
            _device = null;

            try
            {
                _device = _directInput.CreateDevice(instanceGuid);
                _device.SetDataFormat<RawJoystickState>();
                _device.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background | CooperativeLevel.NonExclusive);

                // Get capabilities
                var caps = _device.Capabilities;
                int axisCount = caps.AxeCount;
                int buttonCount = caps.ButtonCount;

                _axes = new int[axisCount > 0 ? axisCount : 0];
                _buttons = new bool[buttonCount > 0 ? buttonCount : 0];

                _device.Acquire();
                _openedInstanceGuid = instanceGuid;

                ControllerInfo.Name = displayName;
                ControllerInfo.IsConnected = true;
                ControllerInfo.AxisCount = axisCount;
                ControllerInfo.ButtonCount = buttonCount;

                Debug.WriteLine($"[InputManager] Opened DirectInput '{displayName}': {axisCount} axes, {buttonCount} buttons");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InputManager] Failed to open DirectInput device: {ex.Message}");
                _device?.Dispose();
                _device = null;
                _axes = Array.Empty<int>();
                _buttons = Array.Empty<bool>();
                ControllerInfo.Name = "Not Connected";
                ControllerInfo.IsConnected = false;
                ControllerInfo.AxisCount = 0;
                ControllerInfo.ButtonCount = 0;
            }
        }

        ControllerChanged?.Invoke(ControllerInfo);
    }

    public void DetectController()
    {
        // Re-enumerate and open the first game controller found
        if (_directInput == null) return;

        try
        {
            var diDevices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
            if (diDevices.Count > 0)
            {
                var first = diDevices[0];
                OpenDirectInputDevice(first.InstanceGuid, first.InstanceName ?? first.ProductName ?? "Controller");
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InputManager] DetectController failed: {ex.Message}");
        }

        ControllerInfo.Name = "Not Connected";
        ControllerInfo.IsConnected = false;
        ControllerChanged?.Invoke(ControllerInfo);
    }

    private void PollLoop()
    {
        while (_running)
        {
            try
            {
                lock (_lock)
                {
                    if (_device == null) goto Sleep;

                    try
                    {
                        _device.Poll();
                        var state = _device.GetCurrentJoystickState();

                        // Read axes (X, Y, Z, RotationX, RotationY, RotationZ, Sliders)
                        // DirectInput JoystickState reports 0-65535 range
                        int[] axisValues = {
                            state.X, state.Y, state.Z,
                            state.RotationX, state.RotationY, state.RotationZ,
                            state.Sliders.Length > 0 ? state.Sliders[0] : 0,
                            state.Sliders.Length > 1 ? state.Sliders[1] : 0
                        };

                        for (int i = 0; i < Math.Min(axisValues.Length, _axes.Length); i++)
                            _axes[i] = axisValues[i];

                        // Read buttons
                        var btnState = state.Buttons;
                        for (int i = 0; i < Math.Min(btnState.Length, _buttons.Length); i++)
                            _buttons[i] = btnState[i];
                    }
                    catch (SharpGen.Runtime.SharpGenException)
                    {
                        // Device disconnected — try to reacquire
                        try { _device.Acquire(); }
                        catch
                        {
                            _device.Dispose();
                            _device = null;
                            _axes = Array.Empty<int>();
                            _buttons = Array.Empty<bool>();
                            ControllerInfo.Name = "Not Connected";
                            ControllerInfo.IsConnected = false;
                            ControllerChanged?.Invoke(ControllerInfo);
                        }
                    }
                }

                Sleep:
                Thread.Sleep(2); // ~500 Hz
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InputManager] Poll error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Returns normalized axis value [-1.0 to 1.0].
    /// DirectInput raw range is 0-65535, center at 32767.
    /// </summary>
    public double GetAxisValue(int axisIndex)
    {
        lock (_lock)
        {
            if (axisIndex < 0 || axisIndex >= _axes.Length) return 0.0;
            // Normalize from 0-65535 to -1.0 to 1.0
            return Math.Clamp((_axes[axisIndex] - 32767.5) / 32767.5, -1.0, 1.0);
        }
    }

    public bool GetButtonValue(int buttonIndex)
    {
        lock (_lock)
        {
            if (buttonIndex < 0 || buttonIndex >= _buttons.Length) return false;
            return _buttons[buttonIndex];
        }
    }

    public double GetControlValue(ControlMapping mapping)
    {
        if (mapping.SdlButton.HasValue)
            return GetButtonValue(mapping.SdlButton.Value) ? 1.0 : 0.0;

        if (mapping.SdlAxis.HasValue)
            return GetAxisValue(mapping.SdlAxis.Value);

        // Fallback: use JoystickOffset as axis index
        // DirectInput offsets: 0=X, 4=Y, 8=Z, 12=RX, 16=RY, 20=RZ, 24=Slider0, 28=Slider1
        int axisIdx = mapping.JoystickOffset / 4;
        return GetAxisValue(axisIdx);
    }

    private string GetDeviceFriendlyName(string devicePath, uint deviceType)
    {
        if (deviceType == RIM_TYPEHID)
        {
            try
            {
                using var handle = CreateFileW(devicePath, 0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (!handle.IsInvalid)
                {
                    byte[] buffer = new byte[512];
                    if (HidD_GetProductString(handle, buffer, (uint)buffer.Length))
                    {
                        string name = Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
                        if (!string.IsNullOrEmpty(name)) return name;
                    }
                }
            }
            catch { }

            int vidIdx = devicePath.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            int pidIdx = devicePath.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (vidIdx >= 0 && pidIdx >= 0)
            {
                string vid = devicePath.Substring(vidIdx + 4, Math.Min(4, devicePath.Length - vidIdx - 4)).Split('&')[0];
                string pid = devicePath.Substring(pidIdx + 4, Math.Min(4, devicePath.Length - pidIdx - 4)).Split('&')[0];
                return $"HID Device (VID_{vid}&PID_{pid})";
            }
            return "Unknown HID Device";
        }

        if (deviceType == RIM_TYPEMOUSE) return "Mouse";
        if (deviceType == RIM_TYPEKEYBOARD) return "Keyboard";
        return "";
    }

    public void Stop()
    {
        _running = false;
        _pollThread?.Join(500);
        _pollThread = null;

        lock (_lock)
        {
            _device?.Unacquire();
            _device?.Dispose();
            _device = null;
        }

        _directInput?.Dispose();
        _directInput = null;
    }

    public void Dispose() => Stop();
}
