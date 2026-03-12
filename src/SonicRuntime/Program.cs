using SonicRuntime.Engine;
using SonicRuntime.Protocol;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;

// sonic-runtime entry point.
// No IHost. No DI container. No app host machinery. No architectural lasagna.

// Initialize SoundFlow audio engine (singleton, must be first)
using var audioEngine = new MiniAudioEngine(48000, Capability.Playback);
Console.Error.WriteLine("[sonic-runtime] audio engine initialized: 48000Hz stereo");

var state = new RuntimeState();
var playback = new PlaybackEngine(state);
var devices = new DeviceManager();
var synthesis = new SynthesisEngine(state);
var dispatcher = new CommandDispatcher(playback, devices, synthesis);
var loop = new CommandLoop(dispatcher);

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await loop.RunAsync(cts.Token);
