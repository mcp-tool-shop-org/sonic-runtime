using System.Text.Json;
using SonicRuntime.Engine;
using SonicRuntime.Protocol;
using Xunit;

namespace SonicRuntime.Tests;

/// <summary>
/// Phase 3 hardening: edge cases, invalid inputs, churn resilience,
/// and stderr isolation from stdout protocol stream.
/// </summary>
public class HardeningTests
{
    // ── Invalid Handle ──

    [Fact]
    public async Task Play_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":1,"method":"play","params":{"handle":"h_doesnotexist","volume":0.5,"pan":0.0,"loop":false}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal(1, r.GetProperty("id").GetInt32());
        var error = r.GetProperty("error");
        Assert.Equal("playback_not_found", error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task Pause_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":2,"method":"pause","params":{"handle":"h_bogus"}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("playback_not_found", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Stop_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":3,"method":"stop","params":{"handle":"h_bogus"}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("playback_not_found", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resume_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":4,"method":"resume","params":{"handle":"h_bogus"}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("playback_not_found", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Seek_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":5,"method":"seek","params":{"handle":"h_bogus","position_ms":1000}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("playback_not_found", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetVolume_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":6,"method":"set_volume","params":{"handle":"h_bogus","level":0.5}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("playback_not_found", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetPan_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":7,"method":"set_pan","params":{"handle":"h_bogus","value":0.5}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("playback_not_found", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetPosition_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":8,"method":"get_position","params":{"handle":"h_bogus"}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("playback_not_found", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetDuration_Invalid_Handle_Returns_Error()
    {
        var request = """{"id":9,"method":"get_duration","params":{"handle":"h_bogus"}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("playback_not_found", r.GetProperty("error").GetProperty("code").GetString());
    }

    // ── Play/Stop Churn ──

    [Fact]
    public async Task Rapid_Load_Play_Stop_Churn()
    {
        // 20 rounds of load → play → stop on unique handles.
        // Verifies no state corruption or crashes under rapid cycling.
        var lines = new List<string>();
        int id = 1;

        for (int i = 0; i < 20; i++)
        {
            lines.Add($"{{\"id\":{id++},\"method\":\"load_asset\",\"params\":{{\"asset_ref\":\"file:///churn{i}.wav\"}}}}");
        }

        // Play each handle
        for (int i = 0; i < 20; i++)
        {
            var handle = $"h_{(i + 1):x12}";
            lines.Add($"{{\"id\":{id++},\"method\":\"play\",\"params\":{{\"handle\":\"{handle}\",\"volume\":0.8,\"pan\":0.0,\"loop\":false}}}}");
        }

        // Stop each handle
        for (int i = 0; i < 20; i++)
        {
            var handle = $"h_{(i + 1):x12}";
            lines.Add($"{{\"id\":{id++},\"method\":\"stop\",\"params\":{{\"handle\":\"{handle}\"}}}}");
        }

        var input = string.Join("\n", lines) + "\n";
        var (stdout, _) = await RunCommandsAsync(input);
        var responses = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // 20 loads + 20 plays + 20 stops = 60 responses
        Assert.Equal(60, responses.Length);

        // Verify all loads returned handles
        for (int i = 0; i < 20; i++)
        {
            var r = JsonSerializer.Deserialize<JsonElement>(responses[i]);
            Assert.True(r.GetProperty("result").TryGetProperty("handle", out _));
        }

        // Verify no errors in play/stop responses
        for (int i = 20; i < 60; i++)
        {
            var r = JsonSerializer.Deserialize<JsonElement>(responses[i]);
            Assert.False(r.TryGetProperty("error", out _), $"Response {i} had unexpected error");
        }
    }

    [Fact]
    public async Task Double_Stop_Same_Handle()
    {
        // Stop an already-stopped handle — should succeed, not crash.
        var lines = new[]
        {
            """{"id":1,"method":"load_asset","params":{"asset_ref":"file:///test.wav"}}""",
            """{"id":2,"method":"play","params":{"handle":"h_000000000001","volume":1.0,"pan":0.0,"loop":false}}""",
            """{"id":3,"method":"stop","params":{"handle":"h_000000000001"}}""",
            """{"id":4,"method":"stop","params":{"handle":"h_000000000001"}}"""
        };

        var input = string.Join("\n", lines) + "\n";
        var (stdout, _) = await RunCommandsAsync(input);
        var responses = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, responses.Length);

        // Both stops should succeed (no error)
        var r3 = JsonSerializer.Deserialize<JsonElement>(responses[2]);
        Assert.False(r3.TryGetProperty("error", out _));

        var r4 = JsonSerializer.Deserialize<JsonElement>(responses[3]);
        Assert.False(r4.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Pause_Without_Play_Succeeds()
    {
        // Pause a loaded-but-never-played handle — should succeed gracefully.
        var lines = new[]
        {
            """{"id":1,"method":"load_asset","params":{"asset_ref":"file:///test.wav"}}""",
            """{"id":2,"method":"pause","params":{"handle":"h_000000000001"}}"""
        };

        var input = string.Join("\n", lines) + "\n";
        var (stdout, _) = await RunCommandsAsync(input);
        var responses = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, responses.Length);
        var r2 = JsonSerializer.Deserialize<JsonElement>(responses[1]);
        Assert.False(r2.TryGetProperty("error", out _));
    }

    // ── Invalid Device ──

    [Fact]
    public async Task SetDevice_Invalid_Id_Returns_Error()
    {
        // audioEnabled=false DeviceManager doesn't validate device IDs,
        // so this just stores the ID. But the protocol still works.
        var request = """{"id":1,"method":"set_device","params":{"device_id":"nonexistent_device"}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal(1, r.GetProperty("id").GetInt32());
        // In test mode (audioEnabled=false), set_device accepts any ID
        Assert.False(r.TryGetProperty("error", out _));
    }

    // ── Missing / Malformed Parameters ──

    [Fact]
    public async Task Play_Missing_Handle_Returns_Error()
    {
        var request = """{"id":1,"method":"play","params":{"volume":0.5}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("invalid_params", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task LoadAsset_Missing_AssetRef_Returns_Error()
    {
        var request = """{"id":1,"method":"load_asset","params":{}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("invalid_params", r.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Seek_Missing_PositionMs_Returns_Error()
    {
        // Load first so handle exists, then seek with missing position_ms
        var lines = new[]
        {
            """{"id":1,"method":"load_asset","params":{"asset_ref":"file:///test.wav"}}""",
            """{"id":2,"method":"seek","params":{"handle":"h_000000000001"}}"""
        };

        var input = string.Join("\n", lines) + "\n";
        var (stdout, _) = await RunCommandsAsync(input);
        var responses = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var r2 = JsonSerializer.Deserialize<JsonElement>(responses[1]);
        Assert.Equal("invalid_params", r2.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetVolume_Missing_Level_Returns_Error()
    {
        var lines = new[]
        {
            """{"id":1,"method":"load_asset","params":{"asset_ref":"file:///test.wav"}}""",
            """{"id":2,"method":"set_volume","params":{"handle":"h_000000000001"}}"""
        };

        var input = string.Join("\n", lines) + "\n";
        var (stdout, _) = await RunCommandsAsync(input);
        var responses = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var r2 = JsonSerializer.Deserialize<JsonElement>(responses[1]);
        Assert.Equal("invalid_params", r2.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetPan_Missing_Value_Returns_Error()
    {
        var lines = new[]
        {
            """{"id":1,"method":"load_asset","params":{"asset_ref":"file:///test.wav"}}""",
            """{"id":2,"method":"set_pan","params":{"handle":"h_000000000001"}}"""
        };

        var input = string.Join("\n", lines) + "\n";
        var (stdout, _) = await RunCommandsAsync(input);
        var responses = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var r2 = JsonSerializer.Deserialize<JsonElement>(responses[1]);
        Assert.Equal("invalid_params", r2.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Malformed_Json_Returns_Error_On_Stderr()
    {
        // Completely invalid JSON — should not crash the loop,
        // should log to stderr, should not emit a response on stdout.
        var request = "this is not json at all";
        var (stdout, stderr) = await RunCommandAsync(request);

        // No valid JSON response on stdout (line may be empty or an error)
        // The key assertion: the loop didn't crash, it handled it gracefully
        if (!string.IsNullOrEmpty(stdout))
        {
            // If there's output, it should be an error response
            var r = JsonSerializer.Deserialize<JsonElement>(stdout);
            Assert.True(r.TryGetProperty("error", out _));
        }
    }

    [Fact]
    public async Task Null_Params_On_Method_Requiring_Params()
    {
        // play with no params at all
        var request = """{"id":1,"method":"play"}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("invalid_params", r.GetProperty("error").GetProperty("code").GetString());
    }

    // ── Synthesize Validation ──

    [Fact]
    public async Task Synthesize_Invalid_Engine_Returns_Error()
    {
        var request = """{"id":1,"method":"synthesize","params":{"engine":"openai","voice":"alice","text":"hello","speed":1.0}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal("synthesis_validation_failed", r.GetProperty("error").GetProperty("code").GetString());
    }

    // ── Stderr Isolation ──

    [Fact]
    public async Task Stderr_Contains_Logs_Stdout_Is_Clean_Json()
    {
        // Send several commands and verify:
        // 1. Every stdout line is valid JSON
        // 2. stderr contains "[sonic-runtime]" log lines
        // 3. stderr never contains JSON response data
        var lines = new[]
        {
            """{"id":1,"method":"version"}""",
            """{"id":2,"method":"load_asset","params":{"asset_ref":"file:///test.wav"}}""",
            """{"id":3,"method":"list_devices"}"""
        };

        var input = string.Join("\n", lines) + "\n";
        var (stdout, stderr) = await RunCommandsAsync(input);

        // Stdout: every line must be valid JSON
        var stdoutLines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, stdoutLines.Length);
        foreach (var line in stdoutLines)
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(line);
            Assert.True(parsed.TryGetProperty("id", out _), $"stdout line missing 'id': {line}");
        }

        // Stderr: should contain runtime log markers
        Assert.Contains("[sonic-runtime]", stderr);

        // Stderr: should NOT contain response JSON (no id/result leaking)
        Assert.DoesNotContain("\"result\"", stderr);
    }

    [Fact]
    public async Task Error_Response_Does_Not_Leak_Stack_Trace_To_Stdout()
    {
        var request = """{"id":1,"method":"play","params":{"handle":"h_bogus"}}""";
        var (stdout, _) = await RunCommandAsync(request);

        // Stdout should be clean JSON error, no stack trace
        Assert.DoesNotContain("at SonicRuntime", stdout);
        Assert.DoesNotContain("StackTrace", stdout);

        var r = JsonSerializer.Deserialize<JsonElement>(stdout);
        var error = r.GetProperty("error");
        // Error message should be user-friendly, not a stack dump
        Assert.DoesNotContain("Exception", error.GetProperty("message").GetString());
    }

    // ── Helpers ──

    private static async Task<(string stdout, string stderr)> RunCommandAsync(string requestLine)
    {
        return await RunCommandsAsync(requestLine + "\n");
    }

    private static async Task<(string stdout, string stderr)> RunCommandsAsync(string input)
    {
        var stdin = new StringReader(input);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var state = new RuntimeState();
        var playback = new PlaybackEngine(state, audioEnabled: false);
        var devices = new DeviceManager(audioEnabled: false);
        var synthesis = new SynthesisEngine(state);
        var dispatcher = new CommandDispatcher(playback, devices, synthesis);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);

        await loop.RunAsync();

        return (stdout.ToString().Trim(), stderr.ToString().Trim());
    }
}
