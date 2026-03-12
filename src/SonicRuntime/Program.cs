using SonicRuntime.Engine;
using SonicRuntime.Protocol;

// sonic-runtime entry point.
// No IHost. No DI container. No app host machinery. No architectural lasagna.

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
