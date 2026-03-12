namespace SonicRuntime.Engine;

/// <summary>
/// Stub synthesis engine. Will load ONNX models and run Kokoro inference.
/// Synthesis runs off the playback hot path — it produces a handle
/// pointing to rendered PCM that PlaybackEngine can then play.
/// </summary>
public sealed class SynthesisEngine
{
    private readonly RuntimeState _state;

    public SynthesisEngine(RuntimeState state)
    {
        _state = state;
    }

    public Task<string> SynthesizeAsync(string engine, string voice, string text, float speed)
    {
        if (engine != "kokoro")
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed",
                $"Unsupported synthesis engine: {engine}",
                retryable: false);

        // TODO: load ONNX model, run inference, produce PCM buffer
        // For now, allocate a handle representing a future rendered asset
        var handle = _state.AllocateHandle();
        var slot = _state.GetSlot(handle);
        slot.AssetRef = $"synth://{engine}/{voice}";
        // Synthesis result duration will be known after inference
        slot.DurationMs = null;

        return Task.FromResult(handle);
    }
}
