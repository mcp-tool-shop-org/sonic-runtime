namespace SonicRuntime.Synthesis;

/// <summary>
/// Reads a WAV file and extracts raw PCM data for OpenAL buffer loading.
/// Minimal parser — handles standard RIFF/WAV with PCM format.
/// </summary>
public static class WavReader
{
    public readonly struct WavData
    {
        public byte[] PcmBytes { get; init; }
        public int SampleRate { get; init; }
        public int Channels { get; init; }
        public int BitsPerSample { get; init; }
    }

    public static WavData Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Parse(bytes);
    }

    public static WavData Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    private static WavData Parse(byte[] data)
    {
        if (data.Length < 44)
            throw new Protocol.RuntimeException(
                "unsupported_format", "WAV file too small to contain valid header", retryable: false);

        // Verify RIFF header
        if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F')
            throw new Protocol.RuntimeException(
                "unsupported_format", "Not a valid WAV file (missing RIFF header)", retryable: false);

        if (data[8] != 'W' || data[9] != 'A' || data[10] != 'V' || data[11] != 'E')
            throw new Protocol.RuntimeException(
                "unsupported_format", "Not a valid WAV file (missing WAVE marker)", retryable: false);

        // Find fmt chunk
        int pos = 12;
        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        byte[]? pcmData = null;

        while (pos + 8 <= data.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            var chunkSize = BitConverter.ToInt32(data, pos + 4);

            if (chunkId == "fmt ")
            {
                var audioFormat = BitConverter.ToInt16(data, pos + 8);
                if (audioFormat != 1)
                    throw new Protocol.RuntimeException(
                        "unsupported_format",
                        $"Only PCM WAV is supported, got format code {audioFormat}",
                        retryable: false);

                channels = BitConverter.ToInt16(data, pos + 10);
                sampleRate = BitConverter.ToInt32(data, pos + 12);
                bitsPerSample = BitConverter.ToInt16(data, pos + 22);
            }
            else if (chunkId == "data")
            {
                pcmData = new byte[chunkSize];
                Array.Copy(data, pos + 8, pcmData, 0, chunkSize);
            }

            pos += 8 + chunkSize;
            // Chunks are word-aligned
            if (chunkSize % 2 != 0) pos++;
        }

        if (pcmData == null || sampleRate == 0)
            throw new Protocol.RuntimeException(
                "unsupported_format", "WAV file missing fmt or data chunk", retryable: false);

        return new WavData
        {
            PcmBytes = pcmData,
            SampleRate = sampleRate,
            Channels = channels,
            BitsPerSample = bitsPerSample
        };
    }
}
