namespace SonicRuntime.Synthesis;

/// <summary>
/// Manages voice embeddings for Kokoro synthesis.
/// Voice files are raw float32 binary: shape (511, 1, 256).
/// Each index corresponds to a token count, giving a (1, 256) style vector.
/// </summary>
public sealed class VoiceRegistry
{
    private readonly Dictionary<string, float[]> _voices = new();
    private readonly string _voicesDir;
    private readonly TextWriter _log;

    /// <summary>Embedding dimension per voice style vector.</summary>
    public const int StyleDim = 256;

    /// <summary>Max token count supported (voice file has 510 entries, indices 0..509).</summary>
    public const int MaxTokenCount = 509;

    public VoiceRegistry(string voicesDir, TextWriter? log = null)
    {
        _voicesDir = voicesDir;
        _log = log ?? Console.Error;
    }

    /// <summary>
    /// Load all .bin voice files from the voices directory.
    /// Each file is named {voice_id}.bin (e.g., af_heart.bin).
    /// </summary>
    public void LoadAll()
    {
        if (!Directory.Exists(_voicesDir))
        {
            _log.WriteLine($"[voice] Voices directory not found: {_voicesDir}");
            return;
        }

        var files = Directory.GetFiles(_voicesDir, "*.bin");
        foreach (var file in files)
        {
            var voiceId = Path.GetFileNameWithoutExtension(file);
            try
            {
                var bytes = File.ReadAllBytes(file);
                var floatCount = bytes.Length / sizeof(float);

                // Validate: should be 511 * 256 = 130816 floats
                if (floatCount < StyleDim)
                {
                    _log.WriteLine($"[voice] Skipping {voiceId}: file too small ({floatCount} floats)");
                    continue;
                }

                var data = new float[floatCount];
                Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
                _voices[voiceId] = data;
                _log.WriteLine($"[voice] Loaded {voiceId} ({floatCount} floats, {floatCount / StyleDim} entries)");
            }
            catch (Exception ex)
            {
                _log.WriteLine($"[voice] Failed to load {voiceId}: {ex.Message}");
            }
        }

        _log.WriteLine($"[voice] Registry: {_voices.Count} voices loaded");
    }

    /// <summary>
    /// Get the style embedding for a voice at a specific token count.
    /// Returns a float[256] slice.
    /// </summary>
    public float[] GetStyleVector(string voiceId, int tokenCount)
    {
        if (!_voices.TryGetValue(voiceId, out var data))
            throw new Protocol.RuntimeException(
                "synthesis_voice_not_found",
                $"Voice not found: {voiceId}",
                retryable: false);

        if (tokenCount < 0 || tokenCount > MaxTokenCount)
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed",
                $"Token count {tokenCount} out of range (0..{MaxTokenCount})",
                retryable: false);

        var offset = tokenCount * StyleDim;
        if (offset + StyleDim > data.Length)
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed",
                $"Voice {voiceId} does not have entry for token count {tokenCount}",
                retryable: false);

        var style = new float[StyleDim];
        Array.Copy(data, offset, style, 0, StyleDim);
        return style;
    }

    /// <summary>List all loaded voice IDs.</summary>
    public string[] ListVoices() => _voices.Keys.ToArray();

    /// <summary>Check if a voice ID is loaded.</summary>
    public bool HasVoice(string voiceId) => _voices.ContainsKey(voiceId);
}
