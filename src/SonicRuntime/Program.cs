using SonicRuntime.Engine;
using SonicRuntime.Protocol;
using SonicRuntime.Synthesis;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;

// sonic-runtime entry point.
// No IHost. No DI container. No app host machinery. No architectural lasagna.

// Initialize SoundFlow audio engine (singleton, must be first)
using var audioEngine = new MiniAudioEngine(48000, Capability.Playback);
Console.Error.WriteLine("[sonic-runtime] audio engine initialized: 48000Hz stereo");

// Resolve paths relative to the binary
var baseDir = AppContext.BaseDirectory;
var modelsDir = Path.Combine(baseDir, "models");
var voicesDir = Path.Combine(baseDir, "voices");
var espeakDir = Path.Combine(baseDir, "espeak");

// Initialize synthesis components
var voiceRegistry = new VoiceRegistry(voicesDir);
voiceRegistry.LoadAll();

var tokenizer = new KokoroTokenizer(espeakDir);
var modelPath = Path.Combine(modelsDir, "kokoro.onnx");
using var inference = new KokoroInference(modelPath);

var state = new RuntimeState();
var events = new CommandLoopEventWriter();
var playback = new PlaybackEngine(state, events: events);
var devices = new DeviceManager();
using var synthesis = new SynthesisEngine(
    state, tokenizer, voiceRegistry, inference, events: events);
var dispatcher = new CommandDispatcher(
    playback, devices, synthesis,
    state, voiceRegistry, inference, tokenizer, baseDir);
var loop = new CommandLoop(dispatcher);
events.Connect(loop);

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await loop.RunAsync(cts.Token);
