using SonicRuntime.Protocol;

namespace SonicRuntime.Engine;

/// <summary>
/// Device manager backed by OpenAL Soft via Silk.NET.
/// Enumerates output devices using ALC_ENUMERATE_ALL_EXT.
/// Provides reverse lookup from opaque device_id to OpenAL device name.
/// </summary>
public sealed class DeviceManager
{
    private readonly bool _audioEnabled;
    private readonly OpenAlBackend? _backend;
    private string _currentDeviceId = "";

    // Reverse map: opaque device_id → OpenAL device name (for per-playback routing)
    private readonly Dictionary<string, string> _deviceIdToName = new();

    public DeviceManager(OpenAlBackend? backend = null, bool audioEnabled = true)
    {
        _backend = backend;
        _audioEnabled = audioEnabled;
    }

    public Task<Protocol.DeviceInfo[]> ListDevicesAsync()
    {
        if (!_audioEnabled || _backend is null)
        {
            return Task.FromResult(new[]
            {
                new Protocol.DeviceInfo
                {
                    DeviceId = "device_default",
                    Name = "Default Output",
                    Kind = "output",
                    IsDefault = true,
                    Channels = 2,
                    SampleRates = [44100, 48000]
                }
            });
        }

        var devices = _backend.EnumerateDevices();
        var result = new Protocol.DeviceInfo[devices.Count];

        for (int i = 0; i < devices.Count; i++)
        {
            var (name, isDefault) = devices[i];
            var deviceId = $"openal_{i}_{StableHash(name):x8}";
            result[i] = new Protocol.DeviceInfo
            {
                DeviceId = deviceId,
                Name = name,
                Kind = "output",
                IsDefault = isDefault,
                Channels = 2,
                SampleRates = [44100, 48000]
            };

            // Build reverse lookup
            _deviceIdToName[deviceId] = name;

            if (isDefault && string.IsNullOrEmpty(_currentDeviceId))
                _currentDeviceId = deviceId;
        }

        return Task.FromResult(result);
    }

    public Task SetDeviceAsync(string deviceId)
    {
        _currentDeviceId = deviceId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolve an opaque device_id to the OpenAL device name string.
    /// Returns null if the ID is unknown (not yet enumerated or invalid).
    /// </summary>
    public string? ResolveDeviceName(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return null; // null/empty = default device

        if (_deviceIdToName.TryGetValue(deviceId, out var name))
            return name;

        return null; // Unknown device — caller should throw device_unavailable
    }

    /// <summary>
    /// Check if a device_id is known (has been enumerated).
    /// </summary>
    public bool IsKnownDevice(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return true; // default is always known
        return _deviceIdToName.ContainsKey(deviceId);
    }

    public string CurrentDeviceId => _currentDeviceId;

    private static uint StableHash(string input)
    {
        uint hash = 2166136261;
        foreach (char c in input)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }
}
