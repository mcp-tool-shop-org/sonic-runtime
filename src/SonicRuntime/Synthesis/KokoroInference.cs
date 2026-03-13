using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SonicRuntime.Synthesis;

/// <summary>
/// Runs Kokoro ONNX inference.
/// Inputs: input_ids (int64, 1×N), style (float32, 1×256), speed (float32, 1).
/// Output: waveform (float32, 1×samples) at 24kHz.
///
/// Model is lazy-loaded on first inference and held for process lifetime.
/// No per-request reloads. No LINQ on the inference path.
/// </summary>
public sealed class KokoroInference : IDisposable
{
    private InferenceSession? _session;
    private readonly string _modelPath;
    private readonly TextWriter _log;
    private readonly object _lock = new();
    private bool _disposed;
    private long _loadTimeMs;
    private int _inferenceCount;

    /// <summary>Output sample rate of the Kokoro model.</summary>
    public const int SampleRate = 24000;

    /// <summary>Whether the model session is currently loaded.</summary>
    public bool IsLoaded { get { lock (_lock) { return _session != null; } } }

    /// <summary>Time taken to load the model, in ms. 0 if not loaded.</summary>
    public long LoadTimeMs => Interlocked.Read(ref _loadTimeMs);

    /// <summary>Total inference calls completed.</summary>
    public int InferenceCount => _inferenceCount;

    /// <summary>Path to the ONNX model file.</summary>
    public string ModelPath => _modelPath;

    public KokoroInference(string modelPath, TextWriter? log = null)
    {
        _modelPath = modelPath;
        _log = log ?? Console.Error;
    }

    /// <summary>
    /// Run inference. Returns raw float32 PCM samples at 24kHz.
    /// Thread-safe via lock — concurrent synthesis requests are serialized.
    /// </summary>
    public float[] Synthesize(long[] inputIds, float[] styleVector, float speed)
    {
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(KokoroInference));

            EnsureModelLoaded();

            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var styleTensor = new DenseTensor<float>(styleVector, new[] { 1, VoiceRegistry.StyleDim });
            var speedTensor = new DenseTensor<float>(new[] { speed }, new[] { 1 });

            var inputs = new NamedOnnxValue[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("style", styleTensor),
                NamedOnnxValue.CreateFromTensor("speed", speedTensor)
            };

            _log.WriteLine($"[inference] Running: {inputIds.Length} tokens, speed={speed}");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var results = _session!.Run(inputs);

            // Output is at index 0: waveform (1, num_samples)
            var enumerator = results.GetEnumerator();
            enumerator.MoveNext();
            var output = enumerator.Current;
            var outputTensor = output.AsTensor<float>();

            // Get the underlying buffer — DenseTensor gives direct access
            float[] samples;
            if (outputTensor is DenseTensor<float> dense)
            {
                // Zero-copy path: DenseTensor exposes its buffer directly
                var buffer = dense.Buffer;
                samples = new float[buffer.Length];
                buffer.Span.CopyTo(samples);
            }
            else
            {
                // Fallback: copy element by element using flat enumeration
                samples = new float[outputTensor.Length];
                int idx = 0;
                foreach (var val in outputTensor)
                    samples[idx++] = val;
            }

            sw.Stop();
            Interlocked.Increment(ref _inferenceCount);
            _log.WriteLine($"[inference] Done: {samples.Length} samples ({samples.Length / (float)SampleRate:F2}s audio) in {sw.ElapsedMilliseconds}ms");

            return samples;
        }
    }

    private void EnsureModelLoaded()
    {
        if (_session != null) return;

        if (!File.Exists(_modelPath))
            throw new Protocol.RuntimeException(
                "synthesis_model_missing",
                $"ONNX model not found: {_modelPath}",
                retryable: false);

        _log.WriteLine($"[inference] Loading model: {_modelPath}");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var opts = new SessionOptions();
            // CPU execution provider is the default — no additional config needed.
            // DirectML can be added later with: opts.AppendExecutionProvider_DML(0);
            _session = new InferenceSession(_modelPath, opts);
        }
        catch (Exception ex)
        {
            throw new Protocol.RuntimeException(
                "synthesis_model_load_failed",
                $"Failed to load ONNX model: {ex.Message}",
                retryable: false);
        }

        sw.Stop();
        Interlocked.Exchange(ref _loadTimeMs, sw.ElapsedMilliseconds);
        _log.WriteLine($"[inference] Model loaded in {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Force model load now instead of on first inference.
    /// Returns load time in ms. If already loaded, returns 0.
    /// </summary>
    public long Preload()
    {
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(KokoroInference));
            if (_session != null) return 0;
            EnsureModelLoaded();
            return _loadTimeMs;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
    }
}
