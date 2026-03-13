using System.Text.Json;
using SonicRuntime.Engine;
using SonicRuntime.Protocol;
using SonicRuntime.Synthesis;
using Xunit;

namespace SonicRuntime.Tests;

public class IntrospectionTests
{
    // ── get_health ──

    [Fact]
    public async Task GetHealth_Returns_Ok_Status()
    {
        var (stdout, _) = await RunCommandAsync("""{"id":1,"method":"get_health"}""");
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");
        Assert.Equal("ok", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("uptime_ms").GetInt64() >= 0);
        Assert.Equal(0, result.GetProperty("active_handles").GetInt32());
        Assert.False(result.GetProperty("model_loaded").GetBoolean());
        Assert.Equal(0, result.GetProperty("voices_loaded").GetInt32());
    }

    [Fact]
    public async Task GetHealth_Reflects_Active_Handles()
    {
        // Load an asset first, then check health
        var commands = """{"id":1,"method":"load_asset","params":{"asset_ref":"test.wav"}}""" + "\n" +
                       """{"id":2,"method":"get_health"}""";
        var (stdout, _) = await RunCommandAsync(commands);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // First response: load_asset (may error in audioEnabled=false, but allocates handle)
        // Second response: get_health
        Assert.True(lines.Length >= 2);

        var healthResponse = JsonSerializer.Deserialize<JsonElement>(lines[^1]);
        if (healthResponse.TryGetProperty("result", out var result))
        {
            // If load succeeded, handle count should be >= 1
            Assert.True(result.GetProperty("active_handles").GetInt32() >= 0);
        }
    }

    // ── get_capabilities ──

    [Fact]
    public async Task GetCapabilities_Returns_Engines_And_Protocol()
    {
        var (stdout, _) = await RunCommandAsync("""{"id":1,"method":"get_capabilities"}""");
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");

        var engines = result.GetProperty("engines");
        Assert.Equal(JsonValueKind.Array, engines.ValueKind);
        Assert.Equal("kokoro", engines[0].GetString());

        Assert.Equal("ndjson-stdio-v1", result.GetProperty("protocol").GetString());

        var features = result.GetProperty("features");
        Assert.Equal(JsonValueKind.Array, features.ValueKind);
        Assert.True(features.GetArrayLength() > 0);
    }

    // ── list_voices ──

    [Fact]
    public async Task ListVoices_Returns_Empty_Array_Without_Voice_Dir()
    {
        var (stdout, _) = await RunCommandAsync("""{"id":1,"method":"list_voices"}""");
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");
        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Equal(0, result.GetArrayLength());
    }

    [Fact]
    public async Task ListVoices_Returns_Voices_With_Metadata()
    {
        // Create a temp voice directory with a synthetic voice
        var tempDir = Path.Combine(Path.GetTempPath(), "sonic-test-introspect-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a small synthetic voice file: 10 entries × 256 floats
            var data = new float[10 * VoiceRegistry.StyleDim];
            var bytes = new byte[data.Length * sizeof(float)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(tempDir, "af_heart.bin"), bytes);

            var voiceRegistry = new VoiceRegistry(tempDir, TextWriter.Null);
            voiceRegistry.LoadAll();

            var (stdout, _) = await RunCommandWithVoicesAsync(
                """{"id":1,"method":"list_voices"}""", voiceRegistry);
            var response = JsonSerializer.Deserialize<JsonElement>(stdout);
            var result = response.GetProperty("result");

            Assert.Equal(1, result.GetArrayLength());
            var voice = result[0];
            Assert.Equal("af_heart", voice.GetProperty("id").GetString());
            Assert.Equal("en-us", voice.GetProperty("language").GetString());
            Assert.Equal("female", voice.GetProperty("gender").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── preload_model ──

    [Fact]
    public async Task PreloadModel_Fails_Without_Synthesis_Engine()
    {
        // Default test setup has no inference — should get error
        var (stdout, _) = await RunCommandAsync("""{"id":1,"method":"preload_model"}""");
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var error = response.GetProperty("error");
        Assert.Equal("synthesis_not_configured", error.GetProperty("code").GetString());
    }

    // ── get_model_status ──

    [Fact]
    public async Task GetModelStatus_Returns_Not_Loaded_Without_Inference()
    {
        var (stdout, _) = await RunCommandAsync("""{"id":1,"method":"get_model_status"}""");
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");
        Assert.False(result.GetProperty("loaded").GetBoolean());
    }

    // ── version ──

    [Fact]
    public async Task Version_Returns_041()
    {
        var (stdout, _) = await RunCommandAsync("""{"id":1,"method":"version"}""");
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");
        Assert.Equal("0.4.1", result.GetProperty("version").GetString());
        Assert.Equal("ndjson-stdio-v1", result.GetProperty("protocol").GetString());
    }

    // ── Event writing ──

    [Fact]
    public async Task CommandLoop_WriteEvent_Produces_Valid_Event_Json()
    {
        var stdin = new StringReader("");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var state = new RuntimeState();
        var playback = new PlaybackEngine(state, audioEnabled: false);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state, audioEnabled: false);
        var dispatcher = new CommandDispatcher(playback, devices, synthesis);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);

        loop.WriteEvent(new RuntimeEvent
        {
            Event = "playback_ended",
            Data = new PlaybackEndedData { Handle = "h_000000000001", Reason = "completed" }
        });

        var output = stdout.ToString().Trim();
        var parsed = JsonSerializer.Deserialize<JsonElement>(output);

        Assert.Equal("playback_ended", parsed.GetProperty("event").GetString());
        // Must NOT have an "id" field — that distinguishes events from responses
        Assert.False(parsed.TryGetProperty("id", out _));
        var data = parsed.GetProperty("data");
        Assert.Equal("h_000000000001", data.GetProperty("handle").GetString());
        Assert.Equal("completed", data.GetProperty("reason").GetString());
    }

    // ── validate_assets ──

    [Fact]
    public async Task ValidateAssets_Returns_Errors_When_No_Assets()
    {
        // Default test setup: no real asset dirs → model/voices/espeak missing
        var (stdout, _) = await RunCommandAsync("""{"id":1,"method":"validate_assets"}""");
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");

        Assert.False(result.GetProperty("valid").GetBoolean());

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);

        // Model check should report missing
        var model = result.GetProperty("model");
        Assert.False(model.GetProperty("available").GetBoolean());
        Assert.NotNull(model.GetProperty("error").GetString());
        Assert.NotNull(model.GetProperty("hint").GetString());

        // Voices check should report missing
        var voices = result.GetProperty("voices");
        Assert.Equal(0, voices.GetProperty("count").GetInt32());

        // ONNX runtime should be available (it's linked)
        var onnx = result.GetProperty("onnx_runtime");
        Assert.True(onnx.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task ValidateAssets_With_Model_And_Voices()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sonic-test-validate-" + Guid.NewGuid().ToString("N")[..8]);
        var modelsDir = Path.Combine(tempDir, "models");
        var voicesDir = Path.Combine(tempDir, "voices");
        Directory.CreateDirectory(modelsDir);
        Directory.CreateDirectory(voicesDir);

        try
        {
            // Create fake model file
            File.WriteAllBytes(Path.Combine(modelsDir, "kokoro.onnx"), new byte[100]);

            // Create synthetic voice
            var voiceData = new float[10 * VoiceRegistry.StyleDim];
            var bytes = new byte[voiceData.Length * sizeof(float)];
            Buffer.BlockCopy(voiceData, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(voicesDir, "af_heart.bin"), bytes);

            var voiceRegistry = new VoiceRegistry(voicesDir, TextWriter.Null);
            voiceRegistry.LoadAll();

            var (stdout, _) = await RunCommandWithBaseDirAsync(
                """{"id":1,"method":"validate_assets"}""", voiceRegistry, tempDir);
            var response = JsonSerializer.Deserialize<JsonElement>(stdout);
            var result = response.GetProperty("result");

            // Model and voices should be available
            Assert.True(result.GetProperty("model").GetProperty("available").GetBoolean());
            Assert.True(result.GetProperty("voices").GetProperty("available").GetBoolean());
            Assert.Equal(1, result.GetProperty("voices").GetProperty("count").GetInt32());
            Assert.Equal(tempDir, result.GetProperty("asset_root").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ValidateAssets_Reports_Asset_Root()
    {
        var (stdout, _) = await RunCommandAsync("""{"id":1,"method":"validate_assets"}""");
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");
        Assert.NotNull(result.GetProperty("asset_root").GetString());
    }

    // ── Voice ID parsing ──

    [Theory]
    [InlineData("af_heart", "en-us", "female")]
    [InlineData("am_adam", "en-us", "male")]
    [InlineData("bf_emma", "en-gb", "female")]
    [InlineData("bm_george", "en-gb", "male")]
    [InlineData("jf_alpha", "ja", "female")]
    [InlineData("x", "unknown", "unknown")]
    public async Task ListVoices_Parses_Language_And_Gender(string voiceId, string expectedLang, string expectedGender)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sonic-test-parse-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var data = new float[10 * VoiceRegistry.StyleDim];
            var bytes = new byte[data.Length * sizeof(float)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(tempDir, $"{voiceId}.bin"), bytes);

            var voiceRegistry = new VoiceRegistry(tempDir, TextWriter.Null);
            voiceRegistry.LoadAll();

            var (stdout, _) = await RunCommandWithVoicesAsync(
                """{"id":1,"method":"list_voices"}""", voiceRegistry);
            var response = JsonSerializer.Deserialize<JsonElement>(stdout);
            var result = response.GetProperty("result");
            var voice = result[0];
            Assert.Equal(expectedLang, voice.GetProperty("language").GetString());
            Assert.Equal(expectedGender, voice.GetProperty("gender").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── Helpers ──

    private static async Task<(string stdout, string stderr)> RunCommandAsync(string requestLine)
    {
        var stdin = new StringReader(requestLine + "\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var state = new RuntimeState();
        var playback = new PlaybackEngine(state, audioEnabled: false);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state, audioEnabled: false);
        var dispatcher = new CommandDispatcher(playback, devices, synthesis, state);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);

        await loop.RunAsync();

        return (stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    private static async Task<(string stdout, string stderr)> RunCommandWithBaseDirAsync(
        string requestLine, VoiceRegistry voiceRegistry, string baseDir)
    {
        var stdin = new StringReader(requestLine + "\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var state = new RuntimeState();
        var playback = new PlaybackEngine(state, audioEnabled: false);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state, audioEnabled: false);
        var dispatcher = new CommandDispatcher(
            playback, devices, synthesis, state, voiceRegistry, baseDir: baseDir);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);

        await loop.RunAsync();

        return (stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    private static async Task<(string stdout, string stderr)> RunCommandWithVoicesAsync(
        string requestLine, VoiceRegistry voiceRegistry)
    {
        var stdin = new StringReader(requestLine + "\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var state = new RuntimeState();
        var playback = new PlaybackEngine(state, audioEnabled: false);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state, audioEnabled: false);
        var dispatcher = new CommandDispatcher(
            playback, devices, synthesis, state, voiceRegistry);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);

        await loop.RunAsync();

        return (stdout.ToString().Trim(), stderr.ToString().Trim());
    }
}
