using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SonicRuntime.Synthesis;

/// <summary>
/// Converts text to Kokoro phoneme token IDs.
/// Pipeline: text → normalize → eSpeak G2P → IPA → token IDs → pad.
///
/// The vocab is the fixed 178-token Kokoro vocabulary.
/// eSpeak-NG is spawned as a child process (not P/Invoke) for G2P.
/// </summary>
public sealed class KokoroTokenizer
{
    private static readonly Dictionary<char, int> Vocab = BuildVocab();
    private readonly string _espeakPath;
    private readonly TextWriter _log;

    /// <summary>Max tokens before padding (model limit is 512 including 2 pad tokens).</summary>
    public const int MaxTokens = 510;

    /// <param name="espeakPath">
    /// Path to eSpeak-NG directory containing the binary and espeak-ng-data.
    /// </param>
    public KokoroTokenizer(string espeakPath, TextWriter? log = null)
    {
        _espeakPath = espeakPath;
        _log = log ?? Console.Error;
    }

    /// <summary>Whether the eSpeak-NG binary is reachable.</summary>
    public bool IsEspeakAvailable => FindEspeakBinary() != null;

    /// <summary>
    /// Convert text to padded token IDs ready for model input.
    /// Returns int64 array: [0, ...token_ids, 0].
    /// </summary>
    public long[] Tokenize(string text, string langCode = "en-us")
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed", "Text must not be empty", retryable: false);

        var normalized = PreprocessText(text);
        var phonemes = Phonemize(normalized, langCode);
        var filtered = PostProcessPhonemes(phonemes);
        var tokenIds = MapToTokenIds(filtered);

        if (tokenIds.Length > MaxTokens)
        {
            _log.WriteLine($"[tokenizer] Warning: {tokenIds.Length} tokens exceeds max {MaxTokens}, truncating");
            tokenIds = tokenIds[..MaxTokens];
        }

        // Pad with 0 at start and end
        var padded = new long[tokenIds.Length + 2];
        padded[0] = 0;
        for (int i = 0; i < tokenIds.Length; i++)
            padded[i + 1] = tokenIds[i];
        padded[^1] = 0;

        return padded;
    }

    /// <summary>
    /// Get the unpadded token count (for voice embedding index lookup).
    /// </summary>
    public int GetTokenCount(string text, string langCode = "en-us")
    {
        var normalized = PreprocessText(text);
        var phonemes = Phonemize(normalized, langCode);
        var filtered = PostProcessPhonemes(phonemes);
        var tokenIds = MapToTokenIds(filtered);
        return Math.Min(tokenIds.Length, MaxTokens);
    }

    // ── Text normalization ──

    public static string PreprocessText(string text)
    {
        // Currency
        text = Regex.Replace(text, @"\$(\d+(?:\.\d+)?)", m =>
        {
            var parts = m.Groups[1].Value.Split('.');
            if (parts.Length == 2 && parts[1] != "00")
                return $"{parts[0]} dollar {parts[1]}";
            return $"{parts[0]} dollar";
        });

        // Titles
        text = Regex.Replace(text, @"\bDr\.", "Doctor");
        text = Regex.Replace(text, @"\bMr\.", "Mister");
        text = Regex.Replace(text, @"\bMrs\.", "Missus");
        text = Regex.Replace(text, @"\bMs\.", "Miss");

        // Time: 12:30 → 12 30
        text = Regex.Replace(text, @"(\d{1,2}):(\d{2})", "$1 $2");

        // Decimals: 3.14 → 3 point 1 4
        text = Regex.Replace(text, @"(\d+)\.(\d+)", m =>
        {
            var integer = m.Groups[1].Value;
            var dec = string.Join(" ", m.Groups[2].Value.ToCharArray());
            return $"{integer} point {dec}";
        });

        // Clean up whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    // ── eSpeak G2P ──

    private string Phonemize(string text, string langCode)
    {
        var exePath = FindEspeakBinary();
        if (exePath == null)
            throw new Protocol.RuntimeException(
                "synthesis_validation_failed",
                $"eSpeak-NG binary not found in {_espeakPath}",
                retryable: false);

        var dataPath = Path.Combine(_espeakPath, "espeak-ng-data");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"--ipa=3 -b 1 -q -v {langCode} --stdin",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (Directory.Exists(dataPath))
            psi.Environment["ESPEAK_DATA_PATH"] = dataPath;

        using var proc = Process.Start(psi)
            ?? throw new Protocol.RuntimeException(
                "synthesis_validation_failed", "Failed to start eSpeak-NG", retryable: true);

        proc.StandardInput.Write(text);
        proc.StandardInput.Close();

        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();

        if (!proc.WaitForExit(10_000))
        {
            proc.Kill();
            throw new Protocol.RuntimeException(
                "synthesis_inference_failed", "eSpeak-NG timed out", retryable: true);
        }

        if (!string.IsNullOrEmpty(error))
            _log.WriteLine($"[tokenizer] eSpeak stderr: {error.Trim()}");

        return output.Trim();
    }

    private string? FindEspeakBinary()
    {
        // Try platform-specific names
        string[] candidates =
        {
            Path.Combine(_espeakPath, "espeak-ng.exe"),
            Path.Combine(_espeakPath, "espeak-ng"),
            Path.Combine(_espeakPath, "espeak-ng-win-amd64.dll"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // Fallback: try PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "espeak-ng",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(3000);
                if (p.ExitCode == 0) return "espeak-ng";
            }
        }
        catch { }

        return null;
    }

    // ── Post-processing ──

    private static char[] PostProcessPhonemes(string phonemes)
    {
        // Filter to only chars in our vocab
        var result = new List<char>(phonemes.Length);
        foreach (var c in phonemes)
        {
            if (Vocab.ContainsKey(c))
                result.Add(c);
        }
        return result.ToArray();
    }

    // ── Token mapping ──

    private static int[] MapToTokenIds(char[] phonemes)
    {
        var ids = new int[phonemes.Length];
        for (int i = 0; i < phonemes.Length; i++)
        {
            ids[i] = Vocab.TryGetValue(phonemes[i], out var id) ? id : 0;
        }
        return ids;
    }

    // ── Vocabulary ──
    // Fixed 178-token Kokoro vocab. Derived from the model's config.json.

    private static Dictionary<char, int> BuildVocab()
    {
        return new Dictionary<char, int>
        {
            // Punctuation / whitespace
            [' '] = 16,
            ['.'] = 4,
            ['!'] = 5,
            ['?'] = 6,
            [','] = 7,
            [';'] = 8,
            [':'] = 9,
            ['-'] = 10,
            ['\''] = 11,
            ['"'] = 12,
            ['('] = 13,
            [')'] = 14,

            // Basic Latin (lowercase)
            ['a'] = 43,
            ['b'] = 44,
            ['c'] = 45,
            ['d'] = 46,
            ['e'] = 47,
            ['f'] = 48,
            ['g'] = 49,
            ['h'] = 50,
            ['i'] = 51,
            ['j'] = 52,
            ['k'] = 53,
            ['l'] = 54,
            ['m'] = 55,
            ['n'] = 56,
            ['o'] = 57,
            ['p'] = 58,
            ['q'] = 59,
            ['r'] = 60,
            ['s'] = 61,
            ['t'] = 62,
            ['u'] = 63,
            ['v'] = 64,
            ['w'] = 65,
            ['x'] = 66,
            ['y'] = 67,
            ['z'] = 68,

            // IPA vowels
            ['\u00E6'] = 72,   // æ (TRAP)
            ['\u0251'] = 73,   // ɑ (PALM)
            ['\u0252'] = 74,   // ɒ (LOT - British)
            ['\u0254'] = 76,   // ɔ (THOUGHT)
            ['\u0259'] = 83,   // ə (schwa)
            ['\u025A'] = 84,   // ɚ (r-colored schwa)
            ['\u025B'] = 85,   // ɛ (DRESS)
            ['\u025C'] = 86,   // ɜ (NURSE)
            ['\u025D'] = 87,   // ɝ (r-colored NURSE)
            ['\u026A'] = 95,   // ɪ (KIT)
            ['\u028A'] = 120,  // ʊ (FOOT)
            ['\u028C'] = 121,  // ʌ (STRUT)
            ['\u0250'] = 71,   // ɐ (near-open central)

            // IPA consonants
            ['\u0261'] = 91,   // ɡ (voiced velar stop)
            ['\u014B'] = 112,  // ŋ (eng/velar nasal)
            ['\u0279'] = 116,  // ɹ (alveolar approximant)
            ['\u0280'] = 117,  // ʀ (uvular trill)
            ['\u027E'] = 115,  // ɾ (alveolar tap)
            ['\u0283'] = 131,  // ʃ (voiceless postalveolar fricative)
            ['\u0292'] = 137,  // ʒ (voiced postalveolar fricative)
            ['\u03B8'] = 119,  // θ (voiceless dental fricative)
            ['\u00F0'] = 81,   // ð (voiced dental fricative)
            ['\u0294'] = 133,  // ʔ (glottal stop)
            ['\u0267'] = 92,   // ɧ (simultaneous ʃ and x)
            ['\u0255'] = 77,   // ɕ (voiceless alveolo-palatal fricative)
            ['\u0291'] = 136,  // ʑ (voiced alveolo-palatal fricative)
            ['\u026D'] = 101,  // ɭ (retroflex lateral)
            ['\u026E'] = 102,  // ɮ (lateral fricative)
            ['\u0271'] = 108,  // ɱ (labiodental nasal)
            ['\u0273'] = 110,  // ɳ (retroflex nasal)
            ['\u0272'] = 109,  // ɲ (palatal nasal)
            ['\u0274'] = 111,  // ɴ (uvular nasal)
            ['\u027B'] = 116,  // ɻ (retroflex approximant - map to ɹ)
            ['\u027D'] = 114,  // ɽ (retroflex flap)
            ['\u0278'] = 113,  // ɸ (voiceless bilabial fricative)
            ['\u0282'] = 130,  // ʂ (voiceless retroflex fricative)
            ['\u0290'] = 135,  // ʐ (voiced retroflex fricative)
            ['\u029D'] = 140,  // ʝ (voiced palatal fricative)
            ['\u0263'] = 90,   // ɣ (voiced velar fricative)
            ['\u0281'] = 118,  // ʁ (voiced uvular fricative)
            ['\u0266'] = 93,   // ɦ (voiced glottal fricative)
            ['\u026C'] = 100,  // ɬ (voiceless lateral fricative)
            ['\u0265'] = 92,   // ɥ (labial-palatal approximant)
            ['\u028B'] = 123,  // ʋ (labiodental approximant)
            ['\u0270'] = 107,  // ɰ (velar approximant)
            ['\u026B'] = 99,   // ɫ (velarized lateral)

            // IPA diacritics & suprasegmentals
            ['\u02C8'] = 156,  // ˈ (primary stress)
            ['\u02CC'] = 157,  // ˌ (secondary stress)
            ['\u02D0'] = 158,  // ː (length mark)
            ['\u0303'] = 144,  // ̃ (nasalization, combining tilde)
            ['\u0325'] = 146,  // ̥ (voiceless, combining ring below)
            ['\u032A'] = 147,  // ̪ (dental, combining bridge below)
            ['\u0324'] = 145,  // ̤ (breathy voice)
            ['\u02D1'] = 159,  // ˑ (half-length)
            ['\u0361'] = 160,  // ͡ (tie bar / affricate)
            ['\u035C'] = 161,  // ͜ (tie bar below)

            // Additional IPA vowels
            ['\u00F8'] = 75,   // ø (close-mid front rounded)
            ['\u0153'] = 78,   // œ (open-mid front rounded)
            ['\u0258'] = 82,   // ɘ (close-mid central)
            ['\u025E'] = 88,   // ɞ (open-mid central rounded)
            ['\u0264'] = 89,   // ɤ (close-mid back unrounded)
            ['\u026F'] = 105,  // ɯ (close back unrounded)
            ['\u0268'] = 94,   // ɨ (close central unrounded)
            ['\u0289'] = 122,  // ʉ (close central rounded)
            ['\u028E'] = 126,  // ʎ (palatal lateral)

            // Clicks and implosives
            ['\u0298'] = 132,  // ʘ (bilabial click)
            ['\u01C0'] = 162,  // ǀ (dental click)
            ['\u01C1'] = 163,  // ǁ (lateral click)
            ['\u01C2'] = 164,  // ǂ (palatal click)
            ['\u01C3'] = 165,  // ǃ (alveolar click)
            ['\u0253'] = 75,   // ɓ (bilabial implosive)
            ['\u0257'] = 80,   // ɗ (alveolar implosive)
            ['\u0260'] = 91,   // ɠ (velar implosive)
            ['\u029B'] = 138,  // ʛ (uvular implosive)

            // Tone letters (Chao)
            ['\u02E5'] = 166,  // ˥ (extra-high)
            ['\u02E6'] = 167,  // ˦ (high)
            ['\u02E7'] = 168,  // ˧ (mid)
            ['\u02E8'] = 169,  // ˨ (low)
            ['\u02E9'] = 170,  // ˩ (extra-low)
        };
    }
}
