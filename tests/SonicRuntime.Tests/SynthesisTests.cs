using System.Text.Json;
using SonicRuntime.Engine;
using SonicRuntime.Protocol;
using SonicRuntime.Synthesis;
using Xunit;

namespace SonicRuntime.Tests;

public class SynthesisTests
{
    // ── Validation tests (no audio, no model needed) ──

    [Fact]
    public async Task Synthesize_Rejects_Unknown_Engine()
    {
        var request = """{"id":1,"method":"synthesize","params":{"engine":"piper","voice":"test","text":"hello"}}""";
        var (stdout, _) = await RunCommandAsync(request);
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var error = response.GetProperty("error");
        Assert.Equal("synthesis_validation_failed", error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task Synthesize_Rejects_Empty_Text()
    {
        var request = """{"id":1,"method":"synthesize","params":{"engine":"kokoro","voice":"test","text":""}}""";
        var (stdout, _) = await RunCommandAsync(request);
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var error = response.GetProperty("error");
        Assert.Equal("synthesis_validation_failed", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Synthesize_Rejects_Whitespace_Only_Text()
    {
        var request = """{"id":1,"method":"synthesize","params":{"engine":"kokoro","voice":"test","text":"   "}}""";
        var (stdout, _) = await RunCommandAsync(request);
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var error = response.GetProperty("error");
        Assert.Equal("synthesis_validation_failed", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Synthesize_Rejects_Speed_Too_Low()
    {
        var request = """{"id":1,"method":"synthesize","params":{"engine":"kokoro","voice":"test","text":"hello","speed":0.1}}""";
        var (stdout, _) = await RunCommandAsync(request);
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var error = response.GetProperty("error");
        Assert.Equal("synthesis_validation_failed", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Synthesize_Rejects_Speed_Too_High()
    {
        var request = """{"id":1,"method":"synthesize","params":{"engine":"kokoro","voice":"test","text":"hello","speed":5.0}}""";
        var (stdout, _) = await RunCommandAsync(request);
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var error = response.GetProperty("error");
        Assert.Equal("synthesis_validation_failed", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Synthesize_Stub_Returns_Handle_When_Audio_Disabled()
    {
        var request = """{"id":1,"method":"synthesize","params":{"engine":"kokoro","voice":"test","text":"hello world"}}""";
        var (stdout, _) = await RunCommandAsync(request);
        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");
        var handle = result.GetProperty("handle").GetString();
        Assert.NotNull(handle);
        Assert.StartsWith("h_", handle);
        Assert.Equal(24000, result.GetProperty("sample_rate").GetInt32());
        Assert.Equal(1, result.GetProperty("channels").GetInt32());
    }

    // ── Text preprocessing tests ──

    [Fact]
    public void PreprocessText_Handles_Currency()
    {
        var result = KokoroTokenizer.PreprocessText("That costs $3.50 please");
        Assert.Contains("3 dollar 50", result);
    }

    [Fact]
    public void PreprocessText_Handles_Titles()
    {
        var result = KokoroTokenizer.PreprocessText("Dr. Smith and Mr. Jones");
        Assert.Contains("Doctor", result);
        Assert.Contains("Mister", result);
    }

    [Fact]
    public void PreprocessText_Handles_Time()
    {
        var result = KokoroTokenizer.PreprocessText("Meet at 12:30 tomorrow");
        Assert.Contains("12 30", result);
    }

    [Fact]
    public void PreprocessText_Handles_Decimals()
    {
        var result = KokoroTokenizer.PreprocessText("Pi is 3.14159");
        Assert.Contains("3 point 1 4 1 5 9", result);
    }

    // ── VoiceRegistry tests ──

    [Fact]
    public void VoiceRegistry_Returns_Empty_When_Dir_Missing()
    {
        var registry = new VoiceRegistry("/nonexistent/path", TextWriter.Null);
        registry.LoadAll();
        Assert.Empty(registry.ListVoices());
    }

    [Fact]
    public void VoiceRegistry_Throws_For_Unknown_Voice()
    {
        var registry = new VoiceRegistry("/nonexistent/path", TextWriter.Null);
        registry.LoadAll();
        var ex = Assert.Throws<RuntimeException>(() => registry.GetStyleVector("ghost_voice", 10));
        Assert.Equal("synthesis_voice_not_found", ex.Code);
    }

    [Fact]
    public void VoiceRegistry_Loads_Synthetic_Voice_File()
    {
        // Create a temp directory with a synthetic voice file
        var tempDir = Path.Combine(Path.GetTempPath(), "sonic-test-voices-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a small synthetic voice file: 10 entries × 256 floats
            var entries = 10;
            var data = new float[entries * VoiceRegistry.StyleDim];
            for (int i = 0; i < data.Length; i++)
                data[i] = i * 0.001f; // deterministic values

            var bytes = new byte[data.Length * sizeof(float)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(tempDir, "test_voice.bin"), bytes);

            var registry = new VoiceRegistry(tempDir, TextWriter.Null);
            registry.LoadAll();

            Assert.True(registry.HasVoice("test_voice"));
            Assert.Single(registry.ListVoices());

            var style = registry.GetStyleVector("test_voice", 5);
            Assert.Equal(VoiceRegistry.StyleDim, style.Length);
            // Verify the style vector is from index 5 (offset = 5 * 256 = 1280)
            Assert.Equal(1280 * 0.001f, style[0], 4);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void VoiceRegistry_Rejects_Out_Of_Range_TokenCount()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sonic-test-voices-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create small voice file with only 5 entries
            var entries = 5;
            var data = new float[entries * VoiceRegistry.StyleDim];
            var bytes = new byte[data.Length * sizeof(float)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(tempDir, "small.bin"), bytes);

            var registry = new VoiceRegistry(tempDir, TextWriter.Null);
            registry.LoadAll();

            // Index 4 is valid (5 entries: 0,1,2,3,4)
            var style = registry.GetStyleVector("small", 4);
            Assert.Equal(VoiceRegistry.StyleDim, style.Length);

            // Index 5 is out of range
            var ex = Assert.Throws<RuntimeException>(() => registry.GetStyleVector("small", 5));
            Assert.Equal("synthesis_validation_failed", ex.Code);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── WavWriter tests ──

    [Fact]
    public void WavWriter_Produces_Valid_Wav_Header()
    {
        using var ms = new MemoryStream();
        var samples = new float[] { 0.0f, 0.5f, -0.5f, 1.0f, -1.0f };
        WavWriter.Write(ms, samples, 24000);

        ms.Position = 0;
        using var r = new BinaryReader(ms);

        // RIFF header
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)));
        var fileSize = r.ReadInt32();
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)));

        // fmt chunk
        Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)));
        Assert.Equal(16, r.ReadInt32());   // chunk size
        Assert.Equal(1, r.ReadInt16());    // PCM
        Assert.Equal(1, r.ReadInt16());    // mono
        Assert.Equal(24000, r.ReadInt32()); // sample rate
        Assert.Equal(48000, r.ReadInt32()); // byte rate (24000 * 1 * 2)
        Assert.Equal(2, r.ReadInt16());    // block align
        Assert.Equal(16, r.ReadInt16());   // bits per sample

        // data chunk
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)));
        var dataSize = r.ReadInt32();
        Assert.Equal(samples.Length * 2, dataSize);

        // Samples
        Assert.Equal(0, r.ReadInt16());         // 0.0
        Assert.Equal(16383, r.ReadInt16());     // 0.5 * 32767 ≈ 16383
        Assert.Equal(-16383, r.ReadInt16());    // -0.5 * 32767 ≈ -16383
        Assert.Equal(32767, r.ReadInt16());     // 1.0 clamped
        Assert.Equal(-32767, r.ReadInt16());    // -1.0 clamped
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
        var dispatcher = new CommandDispatcher(playback, devices, synthesis);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);

        await loop.RunAsync();

        return (stdout.ToString().Trim(), stderr.ToString().Trim());
    }
}
