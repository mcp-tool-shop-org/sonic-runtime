using SoundFlow.Components;
using SoundFlow.Providers;
using SonicRuntime.Synthesis;

namespace SonicRuntime.Engine;

/// <summary>
/// Real synthesis engine backed by Kokoro ONNX inference.
/// Pipeline: text → tokenize → voice embedding → ONNX inference → PCM → WAV → playable handle.
///
/// Synthesis runs off the playback hot path. The output is a WAV file
/// registered as a PlaybackSlot, playable through the existing SoundFlow path.
/// </summary>
public sealed class SynthesisEngine : IDisposable
{
    private readonly RuntimeState _state;
    private readonly KokoroTokenizer? _tokenizer;
    private readonly VoiceRegistry? _voiceRegistry;
    private readonly KokoroInference? _inference;
    private readonly bool _audioEnabled;
    private readonly TextWriter _log;
    private readonly string _tempDir;

    /// <param name="state">Runtime state store</param>
    /// <param name="tokenizer">Kokoro tokenizer (null when audioEnabled=false)</param>
    /// <param name="voiceRegistry">Voice embedding registry (null when audioEnabled=false)</param>
    /// <param name="inference">ONNX inference engine (null when audioEnabled=false)</param>
    /// <param name="audioEnabled">When false, return stub handles without running inference</param>
    /// <param name="log">Diagnostic output (stderr)</param>
    public SynthesisEngine(
        RuntimeState state,
        KokoroTokenizer? tokenizer = null,
        VoiceRegistry? voiceRegistry = null,
        KokoroInference? inference = null,
        bool audioEnabled = true,
        TextWriter? log = null)
    {
        _state = state;
        _tokenizer = tokenizer;
        _voiceRegistry = voiceRegistry;
        _inference = inference;
        _audioEnabled = audioEnabled;
        _log = log ?? Console.Error;
        _tempDir = Path.Combine(Path.GetTempPath(), "sonic-runtime", "synth");
    }

    public SynthesizeResult SynthesizeSync(string engine, string voice, string text, float speed)
    {
        // Validate engine
        if (engine != "kokoro")
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed",
                $"Unsupported synthesis engine: {engine}",
                retryable: false);

        // Validate text
        if (string.IsNullOrWhiteSpace(text))
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed",
                "Text must not be empty",
                retryable: false);

        // Validate speed
        if (speed < 0.5f || speed > 2.0f)
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed",
                $"Speed must be between 0.5 and 2.0, got {speed}",
                retryable: false);

        // Allocate handle early
        var handle = _state.AllocateHandle();
        var slot = _state.GetSlot(handle);
        slot.AssetRef = $"synth://{engine}/{voice}";

        if (!_audioEnabled)
        {
            return new SynthesizeResult
            {
                Handle = handle,
                DurationMs = null,
                SampleRate = KokoroInference.SampleRate,
                Channels = 1
            };
        }

        if (_tokenizer == null || _voiceRegistry == null || _inference == null)
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed",
                "Synthesis components not initialized",
                retryable: false);

        // Validate voice exists
        if (!_voiceRegistry.HasVoice(voice))
            throw new Protocol.RuntimeException(
                "synthesis_voice_not_found",
                $"Voice not found: {voice}. Available: {string.Join(", ", _voiceRegistry.ListVoices())}",
                retryable: false);

        // Tokenize
        _log.WriteLine($"[synthesis] Tokenizing: \"{text}\"");
        var tokenCount = _tokenizer.GetTokenCount(text);
        var inputIds = _tokenizer.Tokenize(text);
        _log.WriteLine($"[synthesis] Tokens: {tokenCount} (padded: {inputIds.Length})");

        // Load voice embedding for this token count (clamped to voice file range)
        var voiceIndex = Math.Min(tokenCount, VoiceRegistry.MaxTokenCount);
        var styleVector = _voiceRegistry.GetStyleVector(voice, voiceIndex);

        // Run ONNX inference
        float[] pcmSamples;
        try
        {
            pcmSamples = _inference.Synthesize(inputIds, styleVector, speed);
        }
        catch (Protocol.RuntimeException)
        {
            throw; // pass through structured errors
        }
        catch (Exception ex)
        {
            throw new Protocol.RuntimeException(
                "synthesis_inference_failed",
                $"Inference failed: {ex.Message}",
                retryable: true);
        }

        if (pcmSamples.Length == 0)
            throw new Protocol.RuntimeException(
                "synthesis_inference_failed",
                "Inference produced no audio samples",
                retryable: true);

        // Write WAV to temp file
        Directory.CreateDirectory(_tempDir);
        var wavPath = Path.Combine(_tempDir, $"{handle}.wav");
        WavWriter.Write(wavPath, pcmSamples, KokoroInference.SampleRate);
        _log.WriteLine($"[synthesis] WAV written: {wavPath} ({pcmSamples.Length} samples)");

        // Register as playable slot (same as PlaybackEngine.LoadAssetAsync)
        var stream = new FileStream(wavPath, FileMode.Open, FileAccess.Read);
        var provider = new StreamDataProvider(stream);
        var player = new SoundPlayer(provider);

        slot.AudioStream = stream;
        slot.DataProvider = provider;
        slot.Player = player;

        var durationMs = (long)(pcmSamples.Length / (float)KokoroInference.SampleRate * 1000.0f);

        return new SynthesizeResult
        {
            Handle = handle,
            DurationMs = durationMs,
            SampleRate = KokoroInference.SampleRate,
            Channels = 1
        };
    }

    public Task<SynthesizeResult> SynthesizeAsync(string engine, string voice, string text, float speed)
    {
        // Run synchronously — inference is CPU-bound, not I/O-bound.
        // The lock inside KokoroInference serializes concurrent requests.
        var result = SynthesizeSync(engine, voice, text, speed);
        return Task.FromResult(result);
    }

    public void Dispose()
    {
        _inference?.Dispose();
    }
}

public sealed class SynthesizeResult
{
    public required string Handle { get; init; }
    public long? DurationMs { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
}
