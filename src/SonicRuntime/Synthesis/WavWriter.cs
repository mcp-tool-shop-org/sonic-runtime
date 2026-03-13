namespace SonicRuntime.Synthesis;

/// <summary>
/// Writes raw float32 PCM samples to a WAV file.
/// Minimal, allocation-light, no dependencies.
/// Output: 16-bit PCM WAV at the specified sample rate.
/// </summary>
public static class WavWriter
{
    /// <summary>
    /// Write float32 samples to a 16-bit PCM WAV file.
    /// Samples are clamped to [-1.0, 1.0] before conversion.
    /// </summary>
    public static void Write(string path, float[] samples, int sampleRate, int channels = 1)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Write(fs, samples, sampleRate, channels);
    }

    /// <summary>
    /// Write float32 samples to a 16-bit PCM WAV stream.
    /// </summary>
    public static void Write(Stream stream, float[] samples, int sampleRate, int channels = 1)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels * (bitsPerSample / 8);
        int dataSize = samples.Length * (bitsPerSample / 8);
        int fileSize = 36 + dataSize;

        // RIFF header
        w.Write("RIFF"u8);
        w.Write(fileSize);
        w.Write("WAVE"u8);

        // fmt chunk
        w.Write("fmt "u8);
        w.Write(16);            // chunk size
        w.Write((short)1);      // PCM format
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bitsPerSample);

        // data chunk
        w.Write("data"u8);
        w.Write(dataSize);

        // Convert float32 [-1,1] to int16 — no allocations, no LINQ
        for (int i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1.0f, 1.0f);
            var sample16 = (short)(clamped * 32767.0f);
            w.Write(sample16);
        }
    }
}
