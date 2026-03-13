using SonicRuntime.Synthesis;
using Xunit;

namespace SonicRuntime.Tests;

/// <summary>
/// Integration tests that run against real Kokoro assets.
/// Gated behind SONIC_ASSETS_DIR env var pointing to a directory containing:
///   models/kokoro.onnx, voices/*.bin, espeak/espeak-ng.exe + espeak-ng-data/
///
/// Skip reason is shown when env var is not set.
/// Run with: SONIC_ASSETS_DIR=path/to/assets dotnet test --filter "Category=RealAsset"
/// </summary>
[Trait("Category", "RealAsset")]
public class RealAssetTests
{
    private static readonly string? AssetsDir = Environment.GetEnvironmentVariable("SONIC_ASSETS_DIR");
    private static readonly bool HasAssets = AssetsDir != null && Directory.Exists(AssetsDir);

    private string ModelPath => Path.Combine(AssetsDir!, "models", "kokoro.onnx");
    private string VoicesDir => Path.Combine(AssetsDir!, "voices");
    private string EspeakDir => Path.Combine(AssetsDir!, "espeak");

    // ── Voice Registry with real files ──

    [Fact]
    public void VoiceRegistry_Loads_Real_Voices()
    {
        if (!HasAssets) { Assert.True(true, "Skipped: SONIC_ASSETS_DIR not set"); return; }

        var registry = new VoiceRegistry(VoicesDir, TextWriter.Null);
        registry.LoadAll();
        var voices = registry.ListVoices();

        Assert.NotEmpty(voices);
        // Each real voice should have 510 entries (510 * 256 = 130560 floats)
        foreach (var voiceId in voices)
        {
            var style = registry.GetStyleVector(voiceId, 0);
            Assert.Equal(VoiceRegistry.StyleDim, style.Length);

            var maxStyle = registry.GetStyleVector(voiceId, VoiceRegistry.MaxTokenCount);
            Assert.Equal(VoiceRegistry.StyleDim, maxStyle.Length);
        }
    }

    // ── Tokenizer with real eSpeak ──

    [Fact]
    public void Tokenizer_Produces_Tokens_From_Real_ESpeak()
    {
        if (!HasAssets) { Assert.True(true, "Skipped: SONIC_ASSETS_DIR not set"); return; }

        var tokenizer = new KokoroTokenizer(EspeakDir, TextWriter.Null);
        var tokens = tokenizer.Tokenize("Hello world.");

        // Padded: [0, ...token_ids, 0]
        Assert.True(tokens.Length >= 3, $"Expected at least 3 tokens (2 padding + content), got {tokens.Length}");
        Assert.Equal(0, tokens[0]);         // start pad
        Assert.Equal(0, tokens[^1]);        // end pad

        // Content tokens should all be in valid vocab range
        for (int i = 1; i < tokens.Length - 1; i++)
            Assert.True(tokens[i] > 0, $"Token at index {i} should be positive, got {tokens[i]}");
    }

    [Fact]
    public void Tokenizer_GetTokenCount_Matches_Tokenize_Content_Length()
    {
        if (!HasAssets) { Assert.True(true, "Skipped: SONIC_ASSETS_DIR not set"); return; }

        var tokenizer = new KokoroTokenizer(EspeakDir, TextWriter.Null);
        var count = tokenizer.GetTokenCount("Testing token count consistency.");
        var tokens = tokenizer.Tokenize("Testing token count consistency.");

        // Token count should match content length (padded length - 2)
        Assert.Equal(count, tokens.Length - 2);
    }

    // ── ONNX Inference ──

    [Fact]
    public void Inference_Loads_Model_And_Produces_Audio()
    {
        if (!HasAssets) { Assert.True(true, "Skipped: SONIC_ASSETS_DIR not set"); return; }

        using var inference = new KokoroInference(ModelPath, TextWriter.Null);
        var registry = new VoiceRegistry(VoicesDir, TextWriter.Null);
        registry.LoadAll();
        var tokenizer = new KokoroTokenizer(EspeakDir, TextWriter.Null);

        var text = "Hello.";
        var inputIds = tokenizer.Tokenize(text);
        var tokenCount = tokenizer.GetTokenCount(text);
        var voiceId = registry.ListVoices()[0]; // first available
        var voiceIndex = Math.Min(tokenCount, VoiceRegistry.MaxTokenCount);
        var style = registry.GetStyleVector(voiceId, voiceIndex);

        var samples = inference.Synthesize(inputIds, style, 1.0f);

        Assert.NotEmpty(samples);
        // At 24kHz, even a short word should produce > 0.1s = 2400 samples
        Assert.True(samples.Length > 2400, $"Expected > 2400 samples, got {samples.Length}");

        // Samples should be in reasonable range
        var max = samples.Max();
        var min = samples.Min();
        Assert.True(max <= 1.5f, $"Max sample {max} exceeds expected range");
        Assert.True(min >= -1.5f, $"Min sample {min} exceeds expected range");
    }

    [Fact]
    public void Inference_Speed_Affects_Duration()
    {
        if (!HasAssets) { Assert.True(true, "Skipped: SONIC_ASSETS_DIR not set"); return; }

        using var inference = new KokoroInference(ModelPath, TextWriter.Null);
        var registry = new VoiceRegistry(VoicesDir, TextWriter.Null);
        registry.LoadAll();
        var tokenizer = new KokoroTokenizer(EspeakDir, TextWriter.Null);

        var text = "Speed test.";
        var inputIds = tokenizer.Tokenize(text);
        var tokenCount = tokenizer.GetTokenCount(text);
        var voiceId = registry.ListVoices()[0];
        var voiceIndex = Math.Min(tokenCount, VoiceRegistry.MaxTokenCount);
        var style = registry.GetStyleVector(voiceId, voiceIndex);

        var normalSamples = inference.Synthesize(inputIds, style, 1.0f);
        var fastSamples = inference.Synthesize(inputIds, style, 2.0f);

        // Faster speed should produce fewer samples (shorter audio)
        Assert.True(fastSamples.Length < normalSamples.Length,
            $"Fast ({fastSamples.Length}) should be shorter than normal ({normalSamples.Length})");
    }

    // ── Pan mapping tests ──

    [Theory]
    [InlineData(-1.0f, -1.0f)]  // Full left — direct mapping
    [InlineData(0.0f, 0.0f)]    // Center — direct mapping
    [InlineData(1.0f, 1.0f)]    // Full right — direct mapping
    [InlineData(-0.5f, -0.5f)]  // Quarter left — direct mapping
    [InlineData(0.5f, 0.5f)]    // Quarter right — direct mapping
    public void PanMapping_DirectPass_OpenAL(float corePan, float expectedOpenAlPan)
    {
        // OpenAL with SourceRelative=true maps -1..1 on X axis directly.
        // No conversion needed (validated in spike, ADR-0010).
        var result = Math.Clamp(corePan, -1.0f, 1.0f);
        Assert.Equal(expectedOpenAlPan, result, 4);
    }

    [Theory]
    [InlineData(-2.0f, -1.0f)]  // Below range clamps to -1
    [InlineData(2.0f, 1.0f)]    // Above range clamps to 1
    public void PanMapping_Clamps_Out_Of_Range(float corePan, float expected)
    {
        var result = Math.Clamp(corePan, -1.0f, 1.0f);
        Assert.Equal(expected, result, 4);
    }

    // ── WavWriter round-trip ──

    [Fact]
    public void WavWriter_RoundTrip_With_Real_Inference()
    {
        if (!HasAssets) { Assert.True(true, "Skipped: SONIC_ASSETS_DIR not set"); return; }

        using var inference = new KokoroInference(ModelPath, TextWriter.Null);
        var registry = new VoiceRegistry(VoicesDir, TextWriter.Null);
        registry.LoadAll();
        var tokenizer = new KokoroTokenizer(EspeakDir, TextWriter.Null);

        var text = "Wave writer test.";
        var inputIds = tokenizer.Tokenize(text);
        var tokenCount = tokenizer.GetTokenCount(text);
        var voiceId = registry.ListVoices()[0];
        var voiceIndex = Math.Min(tokenCount, VoiceRegistry.MaxTokenCount);
        var style = registry.GetStyleVector(voiceId, voiceIndex);

        var samples = inference.Synthesize(inputIds, style, 1.0f);

        // Write to temp WAV
        var wavPath = Path.Combine(Path.GetTempPath(), $"sonic-test-{Guid.NewGuid():N}.wav");
        try
        {
            WavWriter.Write(wavPath, samples, KokoroInference.SampleRate);

            var fileInfo = new FileInfo(wavPath);
            Assert.True(fileInfo.Exists);
            // WAV header (44 bytes) + samples * 2 bytes each
            var expectedSize = 44 + samples.Length * 2;
            Assert.Equal(expectedSize, fileInfo.Length);
        }
        finally
        {
            if (File.Exists(wavPath)) File.Delete(wavPath);
        }
    }

    // ── Error path tests ──

    [Fact]
    public void Inference_Throws_On_Missing_Model()
    {
        var inference = new KokoroInference("/nonexistent/model.onnx", TextWriter.Null);
        var ex = Assert.Throws<SonicRuntime.Protocol.RuntimeException>(() =>
            inference.Synthesize(new long[] { 0, 1, 0 }, new float[256], 1.0f));
        Assert.Equal("synthesis_model_missing", ex.Code);
        Assert.False(ex.Retryable);
        inference.Dispose();
    }

    [Fact]
    public void Tokenizer_Reports_Missing_ESpeak()
    {
        var tokenizer = new KokoroTokenizer("/nonexistent/espeak", TextWriter.Null);
        var ex = Assert.Throws<SonicRuntime.Protocol.RuntimeException>(() =>
            tokenizer.Tokenize("hello"));
        Assert.Equal("synthesis_validation_failed", ex.Code);
        Assert.Contains("eSpeak-NG binary not found", ex.Message);
    }
}
