# Synthesis Asset Contract

## Required directory layout

Assets are resolved relative to the binary (`AppContext.BaseDirectory`):

```
publish/
├── SonicRuntime.exe
├── onnxruntime.dll
├── models/
│   └── kokoro.onnx          # Required — Kokoro v1.0 ONNX model
├── voices/
│   ├── af_heart.bin          # At least one voice required
│   ├── am_onyx.bin
│   └── ...                   # Any number of .bin voice files
└── espeak/
    ├── espeak-ng.exe         # Required — eSpeak-NG binary
    ├── libespeak-ng.dll      # Required — eSpeak-NG library
    └── espeak-ng-data/       # Required — language data
        ├── en_dict
        └── ...
```

## Model file

| Field | Value |
|-------|-------|
| Filename | `models/kokoro.onnx` |
| Source | [onnx-community/Kokoro-82M-v1.0-ONNX](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX) |
| Recommended variant | `onnx/model.onnx` (FP32, 326 MB) |
| Inputs | `input_ids` (int64, 1×N), `style` (float32, 1×256), `speed` (float32, 1) |
| Output | `waveform` (float32, 1×N) at 24 kHz |
| Load behavior | Lazy — loaded on first synthesis request, held for process lifetime |
| Missing model error | `synthesis_model_missing` (not retryable) |

### Alternative model variants

Smaller models trade quality for size/speed:

| Variant | Size | Notes |
|---------|------|-------|
| `model.onnx` | 326 MB | FP32, highest quality |
| `model_fp16.onnx` | 163 MB | FP16, good quality |
| `model_quantized.onnx` | 92 MB | INT8, smallest |

All variants must be renamed to `kokoro.onnx` in the `models/` directory.

## Voice files

| Field | Value |
|-------|-------|
| Directory | `voices/` |
| Format | Raw float32 binary, little-endian |
| Shape | 510 entries × 256 floats = 130,560 floats = 522,240 bytes |
| Entry semantics | Index = token count (0–509), value = style vector (float[256]) |
| Voice ID | Filename without extension (e.g., `af_heart.bin` → `af_heart`) |
| Source | [onnx-community/Kokoro-82M-v1.0-ONNX/voices/](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/tree/main/voices) |

### Voice naming convention

| Prefix | Meaning |
|--------|---------|
| `af_*` | American Female |
| `am_*` | American Male |
| `bf_*` | British Female |
| `bm_*` | British Male |
| `ef_*` | Spanish Female |
| `em_*` | Spanish Male |
| `ff_*` | French Female |
| `hf_*` | Hindi Female |
| `hm_*` | Hindi Male |
| `if_*` | Italian Female |
| `im_*` | Italian Male |
| `jf_*` | Japanese Female |
| `jm_*` | Japanese Male |
| `pf_*` | Portuguese Female |
| `pm_*` | Portuguese Male |
| `zf_*` | Chinese Female |

### Voice loading behavior

- All `.bin` files in `voices/` are loaded into memory at startup
- Files smaller than 1,024 bytes (256 floats) are skipped with a warning
- Missing `voices/` directory logs a warning but does not crash
- Voice ID validation happens at synthesis time, not startup

## eSpeak-NG

| Field | Value |
|-------|-------|
| Directory | `espeak/` |
| Binary | `espeak-ng.exe` (Windows) or `espeak-ng` (Linux/macOS) |
| Data | `espeak/espeak-ng-data/` (set via `ESPEAK_DATA_PATH`) |
| Version | 1.52.0 recommended |
| Purpose | Grapheme-to-phoneme (G2P) conversion |
| Invocation | Spawned as child process per synthesis request |
| Timeout | 10 seconds per G2P call |

### eSpeak binary discovery order

1. `espeak/espeak-ng.exe` (Windows)
2. `espeak/espeak-ng` (Linux/macOS)
3. `espeak/espeak-ng-win-amd64.dll` (alternate Windows)
4. `espeak-ng` on system PATH (fallback)

### Missing eSpeak error

If eSpeak-NG cannot be found, synthesis returns:
```json
{"error": {"code": "synthesis_validation_failed", "message": "eSpeak-NG binary not found in ...", "retryable": false}}
```

## Startup errors

| Condition | Behavior |
|-----------|----------|
| `models/` missing | No error at startup; `synthesis_model_missing` on first synthesis |
| `voices/` missing | Warning logged, no voices loaded; `synthesis_voice_not_found` on synthesis |
| `espeak/` missing | No error at startup; `synthesis_validation_failed` on synthesis |
| Model load fails | `synthesis_model_load_failed` on first synthesis |
| Voice file corrupt | Warning logged, voice skipped |

## Performance baselines (RTX 5080 / i7-14700K)

| Metric | Value |
|--------|-------|
| Model load | ~750 ms (cold, FP32) |
| Short text (7 tokens) | ~270 ms → 1.35s audio (5× realtime) |
| Medium text (28 tokens) | ~360 ms → 1.98s audio (5.5× realtime) |
| eSpeak G2P | < 50 ms per call |
| Voice embedding lookup | < 1 ms |

Model loads once and stays in memory. Subsequent synthesis calls skip the 750ms load cost.

## Downloading assets

```bash
# Model (pick one variant, rename to kokoro.onnx)
curl -L https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model.onnx \
  -o publish/models/kokoro.onnx

# Voices (download as many as needed)
curl -L https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices/af_heart.bin \
  -o publish/voices/af_heart.bin

# eSpeak-NG: install via winget (Windows) or apt (Linux), then copy files
# Windows: winget install eSpeak-NG.eSpeak-NG
# Linux: apt install espeak-ng
```
