using SonicRuntime.Protocol;

namespace SonicRuntime.Engine;

/// <summary>
/// Device manager backed by OpenAL Soft via Silk.NET.
/// Enumerates output devices using ALC_ENUMERATE_ALL_EXT and tracks the current selection.
/// </summary>
public sealed class DeviceManager
{
    private readonly bool _audioEnabled;
    private readonly OpenAlBackend? _backend;
    private string _currentDeviceId = "";

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
            // Use a stable ID derived from the device name
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

            if (isDefault && string.IsNullOrEmpty(_currentDeviceId))
                _currentDeviceId = deviceId;
        }

        return Task.FromResult(result);
    }

    public Task SetDeviceAsync(string deviceId)
    {
        if (!_audioEnabled)
        {
            _currentDeviceId = deviceId;
            return Task.CompletedTask;
        }

        // Stage 1: track selection. Device switching requires context re-creation
        // which is a Stage 2 concern (per-playback routing).
        _currentDeviceId = deviceId;
        return Task.CompletedTask;
    }

    public string CurrentDeviceId => _currentDeviceId;

    /// <summary>
    /// Simple stable hash for device ID generation.
    /// Not cryptographic — just needs to be deterministic for the same input.
    /// </summary>
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
