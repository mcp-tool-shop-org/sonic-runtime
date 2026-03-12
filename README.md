# sonic-runtime

Native audio runtime sidecar for [sonic-core](https://github.com/mcp-tool-shop-org/sonic-core).

## What This Is

A subprocess sidecar that handles the native audio concerns sonic-core delegates:

- **Playback** — load, play, pause, resume, stop, seek, fade, volume, pan, loop
- **Device control** — enumerate outputs, switch devices, handle hot-plug
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

- **C# / .NET 8 LTS**
- **NativeAOT** — single native binary, no JIT, no runtime dependency
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
← {"id":1,"result":{"name":"sonic-runtime","version":"0.1.0","protocol":"ndjson-stdio-v1"}}

→ {"id":2,"method":"load_asset","params":{"asset_ref":"file:///rain.wav"}}
← {"id":2,"result":{"handle":"h_000000000001"}}

→ {"id":3,"method":"play","params":{"handle":"h_000000000001","volume":0.8,"loop":true}}
← {"id":3,"result":null}
```

## Architecture

```
stdin (JSON) → CommandLoop → CommandDispatcher → Engine components → stdout (JSON)
                                                  ├─ PlaybackEngine
                                                  ├─ DeviceManager
                                                  ├─ SynthesisEngine
                                                  └─ RuntimeState
```

All diagnostic output goes to **stderr**. stdout is exclusively for protocol messages.

## Status

Scaffold phase. Stub engines track state but produce no audio. Next steps:

1. Validate NativeAOT compatibility with chosen audio library (NAudio or CSCore)
2. Wire real WASAPI playback through PlaybackEngine
3. Wire ONNX Runtime for Kokoro synthesis
4. Add SidecarBackend to sonic-core
5. Integration tests over real subprocess

## License

MIT
