// Audio backend spike — test SoundFlow v1.1.1 NativeAOT compatibility
// Run with: dotnet run
// Publish with: dotnet publish -c Release -r win-x64

using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;

Console.Error.WriteLine("=== SoundFlow v1.1.1 NativeAOT Spike ===");

// 1. Initialize audio engine (singleton pattern)
Console.Error.WriteLine("\n--- Engine Init ---");
using var engine = new MiniAudioEngine(48000, Capability.Playback);
Console.Error.WriteLine($"Engine initialized: {AudioEngine.Instance.SampleRate}Hz, {AudioEngine.Channels}ch");

// 2. Device enumeration
Console.Error.WriteLine("\n--- Device Enumeration ---");
var devices = AudioEngine.Instance.PlaybackDevices;
Console.Error.WriteLine($"Playback devices: {devices.Length}");
foreach (var d in devices)
{
    Console.Error.WriteLine($"  [{d.Id}] {d.Name} (default: {d.IsDefault})");
}

// 3. Generate a test tone WAV file (440Hz sine, 2 seconds)
Console.Error.WriteLine("\n--- Generating Test Tone ---");
var sampleRate = 48000;
var durationSec = 2.0f;
var frequency = 440.0f;
var totalSamples = (int)(sampleRate * durationSec);

var tempPath = Path.Combine(Path.GetTempPath(), "sonic_spike_tone.wav");
using (var fs = File.Create(tempPath))
using (var bw = new BinaryWriter(fs))
{
    var dataSize = totalSamples * 2 * 2; // 16-bit stereo
    bw.Write("RIFF"u8);
    bw.Write(36 + dataSize);
    bw.Write("WAVE"u8);
    bw.Write("fmt "u8);
    bw.Write(16);
    bw.Write((short)1); // PCM
    bw.Write((short)2); // channels
    bw.Write(sampleRate);
    bw.Write(sampleRate * 2 * 2);
    bw.Write((short)4);
    bw.Write((short)16);
    bw.Write("data"u8);
    bw.Write(dataSize);

    for (int i = 0; i < totalSamples; i++)
    {
        var sample = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * short.MaxValue * 0.3);
        bw.Write(sample); // left
        bw.Write(sample); // right
    }
}

// 4. Play
Console.Error.WriteLine("\n--- Playback Test ---");
var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
using var dataProvider = new StreamDataProvider(stream);
var player = new SoundPlayer(dataProvider);

Mixer.Master.AddComponent(player);
player.Play();
Console.Error.WriteLine("Playing 440Hz tone...");
Thread.Sleep(1000);

// 5. Volume control
Console.Error.WriteLine("Setting volume to 0.3...");
player.Volume = 0.3f;
Thread.Sleep(500);

// 6. Pan control (SoundFlow v1.1.1 uses 0.0=left, 0.5=center, 1.0=right)
Console.Error.WriteLine("Panning left...");
player.Pan = 0.0f;
Thread.Sleep(300);
Console.Error.WriteLine("Panning right...");
player.Pan = 1.0f;
Thread.Sleep(300);
Console.Error.WriteLine("Panning center...");
player.Pan = 0.5f;
Thread.Sleep(200);

// 7. Stop
player.Stop();
Console.Error.WriteLine("Stopped.");
Mixer.Master.RemoveComponent(player);

// 8. Rapid play/stop churn
Console.Error.WriteLine("\n--- Rapid Play/Stop Churn (10 cycles) ---");
for (int i = 0; i < 10; i++)
{
    var churnStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
    using var churnProvider = new StreamDataProvider(churnStream);
    var churnPlayer = new SoundPlayer(churnProvider);

    Mixer.Master.AddComponent(churnPlayer);
    churnPlayer.Play();
    Thread.Sleep(50);
    churnPlayer.Stop();
    Mixer.Master.RemoveComponent(churnPlayer);
    Console.Error.Write($"{i + 1} ");
}
Console.Error.WriteLine("\nChurn complete.");

// Cleanup
try { File.Delete(tempPath); } catch { }

Console.Error.WriteLine("\n=== Spike Complete ===");
