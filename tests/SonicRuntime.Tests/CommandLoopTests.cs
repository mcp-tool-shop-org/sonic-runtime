using System.Text.Json;
using SonicRuntime.Engine;
using SonicRuntime.Protocol;
using Xunit;

namespace SonicRuntime.Tests;

public class CommandLoopTests
{
    /// <summary>
    /// Smoke test: send a version request on stdin, verify we get
    /// a valid JSON response on stdout with the right id and result shape.
    /// </summary>
    [Fact]
    public async Task Version_Request_Returns_Valid_Response()
    {
        var request = """{"id":1,"method":"version"}""";
        var (stdout, _) = await RunCommandAsync(request);

        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal(1, response.GetProperty("id").GetInt32());

        var result = response.GetProperty("result");
        Assert.Equal("sonic-runtime", result.GetProperty("name").GetString());
        Assert.Equal("ndjson-stdio-v1", result.GetProperty("protocol").GetString());
    }

    /// <summary>
    /// Send a load_asset request, verify we get back a handle.
    /// </summary>
    [Fact]
    public async Task LoadAsset_Returns_Handle()
    {
        var request = """{"id":2,"method":"load_asset","params":{"asset_ref":"file:///test.wav"}}""";
        var (stdout, _) = await RunCommandAsync(request);

        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal(2, response.GetProperty("id").GetInt32());

        var result = response.GetProperty("result");
        var handle = result.GetProperty("handle").GetString();
        Assert.NotNull(handle);
        Assert.StartsWith("h_", handle);
    }

    /// <summary>
    /// Send an unknown method, verify we get an error response.
    /// </summary>
    [Fact]
    public async Task Unknown_Method_Returns_Error()
    {
        var request = """{"id":3,"method":"explode"}""";
        var (stdout, _) = await RunCommandAsync(request);

        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        Assert.Equal(3, response.GetProperty("id").GetInt32());

        var error = response.GetProperty("error");
        Assert.Equal("method_not_found", error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }

    /// <summary>
    /// Load then play: verify the full sequence works without error.
    /// </summary>
    [Fact]
    public async Task Load_Then_Play_Sequence()
    {
        var lines = new[]
        {
            """{"id":1,"method":"load_asset","params":{"asset_ref":"file:///rain.wav"}}""",
            """{"id":2,"method":"play","params":{"handle":"h_000000000001","volume":0.8,"pan":0.0,"loop":true}}"""
        };

        var input = string.Join("\n", lines) + "\n";
        var (stdout, _) = await RunCommandsAsync(input);
        var responses = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, responses.Length);

        // First response: load_asset returns handle
        var r1 = JsonSerializer.Deserialize<JsonElement>(responses[0]);
        Assert.Equal(1, r1.GetProperty("id").GetInt32());
        Assert.Equal("h_000000000001", r1.GetProperty("result").GetProperty("handle").GetString());

        // Second response: play returns null result
        var r2 = JsonSerializer.Deserialize<JsonElement>(responses[1]);
        Assert.Equal(2, r2.GetProperty("id").GetInt32());
    }

    /// <summary>
    /// Verify list_devices returns at least the default device.
    /// </summary>
    [Fact]
    public async Task ListDevices_Returns_Default()
    {
        var request = """{"id":5,"method":"list_devices"}""";
        var (stdout, _) = await RunCommandAsync(request);

        var response = JsonSerializer.Deserialize<JsonElement>(stdout);
        var result = response.GetProperty("result");
        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.True(result.GetArrayLength() >= 1);

        var first = result[0];
        Assert.Equal("device_default", first.GetProperty("device_id").GetString());
        Assert.True(first.GetProperty("is_default").GetBoolean());
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
        var synthesis = new SynthesisEngine(state, audioEnabled: false);
        var dispatcher = new CommandDispatcher(playback, devices, synthesis);
        var loop = new CommandLoop(dispatcher, stdin, stdout, stderr);

        await loop.RunAsync();

        return (stdout.ToString().Trim(), stderr.ToString().Trim());
    }
}
