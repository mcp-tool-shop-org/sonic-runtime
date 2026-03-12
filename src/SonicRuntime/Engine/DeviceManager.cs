using SoundFlow.Abstracts;
using SonicRuntime.Protocol;

namespace SonicRuntime.Engine;

/// <summary>
/// Real device manager backed by SoundFlow/MiniAudio.
/// Enumerates output devices and tracks the current selection.
/// </summary>
public sealed class DeviceManager
{
    private readonly bool _audioEnabled;
    private string _currentDeviceId = "";

    public DeviceManager(bool audioEnabled = true)
    {
        _audioEnabled = audioEnabled;
    }

    public Task<Protocol.DeviceInfo[]> ListDevicesAsync()
    {
        if (!_audioEnabled)
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

        var engine = AudioEngine.Instance;
        var sfDevices = engine.PlaybackDevices;

        var result = new Protocol.DeviceInfo[sfDevices.Length];
        for (int i = 0; i < sfDevices.Length; i++)
        {
            var d = sfDevices[i];
            var deviceId = d.Id.ToString();
            result[i] = new Protocol.DeviceInfo
            {
                DeviceId = deviceId,
                Name = d.Name,
                Kind = "output",
                IsDefault = d.IsDefault,
                Channels = 2,
                SampleRates = [44100, 48000]
            };

            if (d.IsDefault && string.IsNullOrEmpty(_currentDeviceId))
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

        var engine = AudioEngine.Instance;
        var found = false;
        foreach (var d in engine.PlaybackDevices)
        {
            if (d.Id.ToString() == deviceId)
            {
                found = true;
                break;
            }
        }

        if (!found)
            throw new RuntimeException("device_unavailable", $"Device not found: {deviceId}", retryable: true);

        _currentDeviceId = deviceId;
        // Note: SoundFlow v1.1.1 singleton engine model doesn't support
        // per-playback device routing. Device switching would require
        // engine re-initialization. For v1, we track the selection and
        // apply it on next engine init. Full hot-switch is a v2 concern.
        return Task.CompletedTask;
    }

    public string CurrentDeviceId => _currentDeviceId;
}
