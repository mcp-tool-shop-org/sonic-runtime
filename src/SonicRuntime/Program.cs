using SonicRuntime.Engine;
using SonicRuntime.Protocol;
using SonicRuntime.Synthesis;

// sonic-runtime entry point.
// No IHost. No DI container. No app host machinery. No architectural lasagna.

// Initialize OpenAL Soft audio backend (replaces SoundFlow per ADR-0010)
using var audioBackend = new OpenAlBackend();
Console.Error.WriteLine("[sonic-runtime] OpenAL Soft audio backend initialized");

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
using var playback = new PlaybackEngine(state, audioBackend, events: events);
var devices = new DeviceManager(audioBackend);
using var synthesis = new SynthesisEngine(
    state, playback, tokenizer, voiceRegistry, inference, events: events);
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
