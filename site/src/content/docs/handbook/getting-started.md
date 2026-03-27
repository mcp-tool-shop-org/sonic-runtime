---
title: Getting Started
description: Build, publish, and run the sonic-runtime binary.
sidebar:
  order: 1
---

## Prerequisites

- **.NET 8 SDK** — sonic-runtime targets .NET 8 LTS with NativeAOT
- **Windows** (v1) — the published binary targets win-x64

## Build

```bash
git clone https://github.com/mcp-tool-shop-org/sonic-runtime
cd sonic-runtime
dotnet build
```

## Test

```bash
dotnet test
```

The test suite (98 tests) covers playback, device management, synthesis, protocol parsing, event emission, and version alignment. Tests that need real audio assets look for files in `assets-test/`.

## Publish (NativeAOT)

```bash
dotnet publish src/SonicRuntime -c Release -r win-x64
```

Output: `src/SonicRuntime/bin/Release/net8.0/win-x64/publish/SonicRuntime.exe`

This produces a single native executable — no .NET runtime installation needed on the target machine.

## Run with sonic-core

Set the `SONIC_RUNTIME_PATH` environment variable in your sonic-core project:

```bash
export SONIC_RUNTIME_PATH=/path/to/SonicRuntime.exe
```

sonic-core's SidecarBackend will spawn the binary, complete the version handshake, and begin accepting audio commands.

## Synthesis assets

For TTS synthesis, sonic-runtime needs additional assets placed relative to the published binary:

- **Kokoro ONNX model** — placed in `models/kokoro.onnx` (relative to binary)
- **Voice embeddings** — `.bin` files in `voices/` (e.g., `af_heart.bin`)
- **eSpeak-NG** — binary and data in `espeak/`, or installed on system PATH

Playback works without synthesis assets. Only synthesis commands require the model, voices, and eSpeak.

See [docs/synthesis-assets.md](https://github.com/mcp-tool-shop-org/sonic-runtime/blob/main/docs/synthesis-assets.md) for the full operator contract. Playback works without synthesis assets.
