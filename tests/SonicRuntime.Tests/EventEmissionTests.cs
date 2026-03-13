using System.Text.Json;
using SonicRuntime.Engine;
using SonicRuntime.Protocol;
using Xunit;

namespace SonicRuntime.Tests;

/// <summary>
/// Tests that engines emit the correct events via IEventWriter.
/// Uses a recording event writer to capture events without touching stdout.
/// </summary>
public class EventEmissionTests
{
    // ── Recording event writer for test assertions ──

    private sealed class RecordingEventWriter : IEventWriter
    {
        public List<(string Type, object? Data)> Events { get; } = [];

        public void Write(string eventType, object? data)
        {
            Events.Add((eventType, data));
        }
    }

    // ── Synthesis events ──

    [Fact]
    public void Synthesize_Emits_Started_And_Completed_In_Stub_Mode()
    {
        var recorder = new RecordingEventWriter();
        var state = new RuntimeState();
        using var engine = new SynthesisEngine(state, audioEnabled: false, events: recorder);

        var result = engine.SynthesizeSync("kokoro", "af_heart", "Hello world", 1.0f);

        Assert.Equal(2, recorder.Events.Count);

        // First event: synthesis_started
        Assert.Equal("synthesis_started", recorder.Events[0].Type);
        var started = (SynthesisStartedData)recorder.Events[0].Data!;
        Assert.Equal(result.Handle, started.Handle);
        Assert.Equal("kokoro", started.Engine);
        Assert.Equal("af_heart", started.Voice);

        // Second event: synthesis_completed
        Assert.Equal("synthesis_completed", recorder.Events[1].Type);
        var completed = (SynthesisTimingData)recorder.Events[1].Data!;
        Assert.Equal(result.Handle, completed.Handle);
    }

    [Fact]
    public void Synthesize_Emits_Failed_On_Bad_Engine()
    {
        var recorder = new RecordingEventWriter();
        var state = new RuntimeState();
        using var engine = new SynthesisEngine(state, audioEnabled: false, events: recorder);

        Assert.Throws<RuntimeException>(() =>
            engine.SynthesizeSync("bad_engine", "af_heart", "Hello", 1.0f));

        // Validation fails before handle allocation, so no events emitted
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public void Synthesize_Emits_Failed_On_Missing_Components_When_Audio_Enabled()
    {
        var recorder = new RecordingEventWriter();
        var state = new RuntimeState();
        // audioEnabled=true but no tokenizer/voiceRegistry/inference
        using var engine = new SynthesisEngine(state, audioEnabled: true, events: recorder);

        Assert.Throws<RuntimeException>(() =>
            engine.SynthesizeSync("kokoro", "af_heart", "Hello", 1.0f));

        Assert.Equal(2, recorder.Events.Count);
        Assert.Equal("synthesis_started", recorder.Events[0].Type);
        Assert.Equal("synthesis_failed", recorder.Events[1].Type);
        var failed = (SynthesisFailedData)recorder.Events[1].Data!;
        Assert.Equal("synthesis_validation_failed", failed.Code);
    }

    // ── Playback events ──

    [Fact]
    public async Task Stop_Emits_Playback_Ended()
    {
        var recorder = new RecordingEventWriter();
        var state = new RuntimeState();
        var playback = new PlaybackEngine(state, audioEnabled: false, events: recorder);

        var handle = await playback.LoadAssetAsync("test.wav");
        await playback.PlayAsync(handle, 1.0f, 0.0f, 0, false);
        await playback.StopAsync(handle, 0);

        Assert.Single(recorder.Events);
        Assert.Equal("playback_ended", recorder.Events[0].Type);
        var data = (PlaybackEndedData)recorder.Events[0].Data!;
        Assert.Equal(handle, data.Handle);
        Assert.Equal("stopped", data.Reason);
    }

    [Fact]
    public async Task Play_And_Pause_Do_Not_Emit_Events()
    {
        var recorder = new RecordingEventWriter();
        var state = new RuntimeState();
        var playback = new PlaybackEngine(state, audioEnabled: false, events: recorder);

        var handle = await playback.LoadAssetAsync("test.wav");
        await playback.PlayAsync(handle, 1.0f, 0.0f, 0, false);
        await playback.PauseAsync(handle, 0);
        await playback.ResumeAsync(handle, 0);

        Assert.Empty(recorder.Events);
    }

    // ── NullEventWriter ──

    [Fact]
    public void NullEventWriter_Does_Not_Throw()
    {
        NullEventWriter.Instance.Write("anything", new { foo = "bar" });
        // Just verifying no exception
    }

    // ── CommandLoopEventWriter late binding ──

    [Fact]
    public void CommandLoopEventWriter_Drops_Events_Before_Connect()
    {
        var writer = new CommandLoopEventWriter();
        // Should not throw — silently drops
        writer.Write("test_event", new { foo = 1 });
    }

    [Fact]
    public void CommandLoopEventWriter_Forwards_Events_After_Connect()
    {
        var stdout = new StringWriter();
        var state = new RuntimeState();
        var playback = new PlaybackEngine(state, audioEnabled: false);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state, audioEnabled: false);
        var dispatcher = new CommandDispatcher(playback, devices, synthesis);
        var loop = new CommandLoop(dispatcher, new StringReader(""), stdout, TextWriter.Null);

        var writer = new CommandLoopEventWriter();
        writer.Connect(loop);
        writer.Write("test_event", new PlaybackEndedData { Handle = "h_test", Reason = "test" });

        var output = stdout.ToString().Trim();
        var parsed = JsonSerializer.Deserialize<JsonElement>(output);
        Assert.Equal("test_event", parsed.GetProperty("event").GetString());
        Assert.False(parsed.TryGetProperty("id", out _));
    }

    // ── E2E: events interleaved with request/response on stdout ──

    [Fact]
    public async Task Events_Interleave_With_Responses_On_Stdout()
    {
        // Synthesize in stub mode should produce: synthesis_started event, synthesis_completed event,
        // then the synthesize response — all on stdout
        var commands = """{"id":1,"method":"synthesize","params":{"engine":"kokoro","voice":"af_heart","text":"Hello","speed":1.0}}""";
        var stdin = new StringReader(commands + "\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var state = new RuntimeState();
        var eventWriter = new CommandLoopEventWriter();
        var playback = new PlaybackEngine(state, audioEnabled: false);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state, audioEnabled: false, events: eventWriter);
        var dispatcher = new CommandDispatcher(playback, devices, synthesis, state);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);
        eventWriter.Connect(loop);

        await loop.RunAsync();

        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Should have 3 lines: synthesis_started, synthesis_completed, and the response
        Assert.Equal(3, lines.Length);

        var first = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.Equal("synthesis_started", first.GetProperty("event").GetString());
        Assert.False(first.TryGetProperty("id", out _));

        var second = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        Assert.Equal("synthesis_completed", second.GetProperty("event").GetString());
        Assert.False(second.TryGetProperty("id", out _));

        var third = JsonSerializer.Deserialize<JsonElement>(lines[2]);
        Assert.True(third.TryGetProperty("id", out var idProp));
        Assert.Equal(1, idProp.GetInt32());
        Assert.True(third.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task Stop_Event_Interleaves_With_Response()
    {
        // Load, play, stop — should see playback_ended event + stop response
        var commands =
            """{"id":1,"method":"load_asset","params":{"asset_ref":"test.wav"}}""" + "\n" +
            """{"id":2,"method":"play","params":{"handle":"h_000000000001","volume":1.0}}""" + "\n" +
            """{"id":3,"method":"stop","params":{"handle":"h_000000000001"}}""";

        var stdin = new StringReader(commands + "\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var state = new RuntimeState();
        var eventWriter = new CommandLoopEventWriter();
        var playback = new PlaybackEngine(state, audioEnabled: false, events: eventWriter);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state, audioEnabled: false);
        var dispatcher = new CommandDispatcher(playback, devices, synthesis, state);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);
        eventWriter.Connect(loop);

        await loop.RunAsync();

        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // load response, play response, playback_ended event, stop response
        Assert.Equal(4, lines.Length);

        // Third line should be the event
        var eventLine = JsonSerializer.Deserialize<JsonElement>(lines[2]);
        Assert.Equal("playback_ended", eventLine.GetProperty("event").GetString());
        Assert.False(eventLine.TryGetProperty("id", out _));
        var data = eventLine.GetProperty("data");
        Assert.Equal("h_000000000001", data.GetProperty("handle").GetString());
        Assert.Equal("stopped", data.GetProperty("reason").GetString());

        // Fourth line should be the stop response
        var stopResponse = JsonSerializer.Deserialize<JsonElement>(lines[3]);
        Assert.Equal(3, stopResponse.GetProperty("id").GetInt32());
    }

    // ── stderr isolation ──

    [Fact]
    public async Task Events_Do_Not_Leak_To_Stderr()
    {
        var commands = """{"id":1,"method":"synthesize","params":{"engine":"kokoro","voice":"af_heart","text":"Hello","speed":1.0}}""";
        var stdin = new StringReader(commands + "\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var state = new RuntimeState();
        var eventWriter = new CommandLoopEventWriter();
        var playback = new PlaybackEngine(state, audioEnabled: false);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state, audioEnabled: false, events: eventWriter);
        var dispatcher = new CommandDispatcher(playback, devices, synthesis, state);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);
        eventWriter.Connect(loop);

        await loop.RunAsync();

        // stderr should not contain any event JSON
        var stderrOutput = stderr.ToString();
        Assert.DoesNotContain("synthesis_started", stderrOutput);
        Assert.DoesNotContain("synthesis_completed", stderrOutput);
    }
}
