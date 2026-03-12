using SonicRuntime.Protocol;

namespace SonicRuntime.Engine;

/// <summary>
/// Stub device manager. Returns a default null device.
/// Real implementation will enumerate WASAPI/DirectSound devices.
/// </summary>
public sealed class DeviceManager
{
    private string _currentDeviceId = "device_default";

    public Task<DeviceInfo[]> ListDevicesAsync()
    {
        // TODO: enumerate real audio output devices
        var devices = new[]
        {
            new DeviceInfo
            {
                DeviceId = "device_default",
                Name = "Default Output",
                Kind = "output",
                IsDefault = true,
                Channels = 2,
                SampleRates = [44100, 48000]
            }
        };
        return Task.FromResult(devices);
    }

    public Task SetDeviceAsync(string deviceId)
    {
        // TODO: validate device exists, switch output
        _currentDeviceId = deviceId;
        return Task.CompletedTask;
    }

    public string CurrentDeviceId => _currentDeviceId;
}
