using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace VRCDroneOSC.Services;

public class OscTransport : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private UdpClient? _client;
    private Timer? _sendTimer;
    private Timer? _reconnectTimer;
    private readonly object _lock = new();
    private readonly Dictionary<string, object> _pendingValues = new();
    private bool _connected;
    private bool _physicsInitSent;

    private static readonly Dictionary<string, float> NeutralPhysicsParams = new()
    {
        ["/avatar/parameters/DronePhysics/Mass"] = 0.0000001f,
        ["/avatar/parameters/DronePhysics/Drag"] = 0f,
        ["/avatar/parameters/DronePhysics/Gravity"] = 0f,
        ["/avatar/parameters/DronePhysics/ThrottleSpeed"] = 1f,
        ["/avatar/parameters/DronePhysics/PitchSpeed"] = 1f,
        ["/avatar/parameters/DronePhysics/RollSpeed"] = 1f,
        ["/avatar/parameters/DronePhysics/YawSpeed"] = 1f,
    };

    public bool IsConnected => _connected;
    public string? ConnectionError { get; private set; }
    public event Action<bool>? ConnectionChanged;

    public OscTransport(string host = "127.0.0.1", int port = 9000)
    {
        _host = host;
        _port = port;
    }

    public void Start()
    {
        TryConnect();
        _reconnectTimer = new Timer(_ =>
        {
            if (!_connected) TryConnect();
        }, null, 3000, 3000);
    }

    private void TryConnect()
    {
        try
        {
            _client?.Dispose();
            _client = new UdpClient();
            _client.Connect(_host, _port);
            _connected = true;
            ConnectionError = null;
            _physicsInitSent = false;
            ConnectionChanged?.Invoke(true);
            _sendTimer ??= new Timer(SendPending, null, 0, 16);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            _connected = false;
            ConnectionError = "Port 9000 in use — close the other OSC app";
            ConnectionChanged?.Invoke(false);
        }
        catch
        {
            _connected = false;
            ConnectionError = "VRChat not detected";
            ConnectionChanged?.Invoke(false);
        }
    }

    public void QueueValue(string address, float value)
    {
        lock (_lock) { _pendingValues[address] = value; }
    }

    public void QueueValue(string address, bool value)
    {
        lock (_lock) { _pendingValues[address] = value; }
    }

    private void SendPending(object? state)
    {
        if (!_connected || _client == null) return;

        if (!_physicsInitSent)
        {
            foreach (var (address, value) in NeutralPhysicsParams)
                SendOscFloat(address, value);
            _physicsInitSent = true;
        }

        Dictionary<string, object> snapshot;
        lock (_lock)
        {
            if (_pendingValues.Count == 0) return;
            snapshot = new Dictionary<string, object>(_pendingValues);
            _pendingValues.Clear();
        }

        foreach (var (address, value) in snapshot)
        {
            try
            {
                if (value is float f) SendOscFloat(address, f);
                else if (value is bool b) SendOscBool(address, b);
            }
            catch
            {
                _connected = false;
                ConnectionChanged?.Invoke(false);
                return;
            }
        }
    }

    private void SendOscFloat(string address, float value)
    {
        var bytes = BuildOscMessage(address, ",f\0\0", BitConverter.GetBytes(BSwap(value)));
        _client!.Send(bytes, bytes.Length);
    }

    private void SendOscBool(string address, bool value)
    {
        var tag = value ? ",T\0\0" : ",F\0\0";
        var bytes = BuildOscMessage(address, tag, Array.Empty<byte>());
        _client!.Send(bytes, bytes.Length);
    }

    private static byte[] BuildOscMessage(string address, string typeTag, byte[] data)
    {
        var addrBytes = PadToFour(Encoding.ASCII.GetBytes(address + '\0'));
        var tagBytes = PadToFour(Encoding.ASCII.GetBytes(typeTag));
        var result = new byte[addrBytes.Length + tagBytes.Length + data.Length];
        Buffer.BlockCopy(addrBytes, 0, result, 0, addrBytes.Length);
        Buffer.BlockCopy(tagBytes, 0, result, addrBytes.Length, tagBytes.Length);
        Buffer.BlockCopy(data, 0, result, addrBytes.Length + tagBytes.Length, data.Length);
        return result;
    }

    private static byte[] PadToFour(byte[] input)
    {
        int padded = (input.Length + 3) & ~3;
        if (padded == input.Length) return input;
        var result = new byte[padded];
        Buffer.BlockCopy(input, 0, result, 0, input.Length);
        return result;
    }

    private static float BSwap(float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0);
    }

    public void Stop()
    {
        _sendTimer?.Dispose(); _sendTimer = null;
        _reconnectTimer?.Dispose(); _reconnectTimer = null;
        _client?.Dispose(); _client = null;
        _connected = false;
    }

    public void Dispose() => Stop();
}
