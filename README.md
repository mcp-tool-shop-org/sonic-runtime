<p align="center">
  <a href="README.ja.md">日本語</a> | <a href="README.zh.md">中文</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | <a href="README.hi.md">हिन्दी</a> | <a href="README.it.md">Italiano</a> | <a href="README.pt-BR.md">Português (BR)</a>
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/mcp-tool-shop-org/brand/main/logos/sonic-runtime/readme.png" width="400" alt="sonic-runtime" />
</p>

<p align="center">
  <a href="https://github.com/mcp-tool-shop-org/sonic-runtime/actions/workflows/ci.yml"><img src="https://github.com/mcp-tool-shop-org/sonic-runtime/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <a href="https://github.com/mcp-tool-shop-org/sonic-runtime/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT License" /></a>
  <a href="https://mcp-tool-shop-org.github.io/sonic-runtime/"><img src="https://img.shields.io/badge/Landing_Page-live-blue" alt="Landing Page" /></a>
</p>

Native audio runtime sidecar for [sonic-core](https://github.com/mcp-tool-shop-org/sonic-core). C# NativeAOT binary that handles playback, device routing, and synthesis over ndjson-stdio.

## What This Is

A subprocess sidecar that handles the native audio concerns sonic-core delegates:

- **Playback** — load, play, pause, resume, stop, seek, fade, volume, pan, loop
- **Device control** — enumerate outputs, per-playback device routing, handle hot-plug
- **Synthesis** — Kokoro/ONNX TTS inference producing playable audio

sonic-core launches this as a child process and communicates over newline-delimited JSON on stdio.

## What This Is Not

- Not a standalone application
- Not a UI
- Not a session manager, preset store, or product layer
- No leases (owned by sonic-core)
- No user state, therapy concepts, or product semantics

See [ADR-0005](https://github.com/mcp-tool-shop-org/sonic-core/blob/main/docs/adr/0005-native-runtime-boundary.md) and [ADR-0006](https://github.com/mcp-tool-shop-org/sonic-core/blob/main/docs/adr/0006-runtime-language-stack.md) for the architectural rationale.

## Stack

- **C# / .NET 8 LTS** with **NativeAOT** — single native binary, no JIT, no runtime dependency
- **OpenAL Soft** via **Silk.NET** — low-latency audio playback and device management
- **ONNX Runtime 1.22.0** — Kokoro TTS inference via managed C# binding (NativeAOT-compatible)
- **eSpeak-NG 1.52.0** — grapheme-to-phoneme conversion (spawned as child process)
- **Windows-first** (v1)

## Build

```bash
dotnet build
```

## Test

```bash
dotnet test
```

## Publish (NativeAOT)

```bash
dotnet publish src/SonicRuntime -c Release -r win-x64
```

Output: `src/SonicRuntime/bin/Release/net8.0/win-x64/publish/SonicRuntime.exe`

## Protocol

See [docs/protocol.md](docs/protocol.md) for the full wire protocol specification.

Quick example:

```
→ {"id":1,"method":"version"}
← {"id":1,"result":{"name":"sonic-runtime","version":"1.0.1","protocol":"ndjson-stdio-v1"}}

→ {"id":2,"method":"load_asset","params":{"asset_ref":"file:///rain.wav"}}
← {"id":2,"result":{"handle":"h_000000000001"}}

→ {"id":3,"method":"play","params":{"handle":"h_000000000001","volume":0.8,"loop":true}}
← {"id":3,"result":null}
```

## Architecture

```
stdin (JSON) → CommandLoop → CommandDispatcher → Engine components → stdout (JSON)
                    │                             ├─ PlaybackEngine (OpenAL Soft)
                    │                             ├─ DeviceManager (hot-plug, enumeration)
                    │                             ├─ SynthesisEngine (Kokoro ONNX → WAV → playback)
                    │                             │   ├─ KokoroTokenizer (eSpeak G2P)
                    │                             │   ├─ KokoroInference (ONNX Runtime)
                    │                             │   └─ VoiceRegistry (510 voices, raw float32)
                    │                             └─ RuntimeState (handle tracking, h_XXXXXXXXXXXX format)
                    └─ IEventWriter → stdout (unsolicited events)
```

All diagnostic output goes to **stderr**. stdout is exclusively for protocol messages and runtime events.

## Synthesis Assets

Real synthesis requires model files, voice embeddings, and eSpeak-NG. See [docs/synthesis-assets.md](docs/synthesis-assets.md) for the full operator contract.

## Status

**v1.0.1** — Production-stable. OpenAL Soft playback, per-playback device routing, Kokoro TTS, 98 tests.

## License

MIT — see [LICENSE](LICENSE).

---

Built by [MCP Tool Shop](https://mcp-tool-shop.github.io/)
