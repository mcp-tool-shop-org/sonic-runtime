---
title: Handbook
description: Complete guide to building and operating sonic-runtime.
sidebar:
  order: 0
---

Welcome to the sonic-runtime handbook. This is the complete guide to building, publishing, and operating the native audio sidecar.

## What's inside

- **[Getting Started](/sonic-runtime/handbook/getting-started/)** — Build, publish, and run the binary
- **[Architecture](/sonic-runtime/handbook/architecture/)** — Engine components, OpenAL, and the command loop
- **[Protocol](/sonic-runtime/handbook/protocol/)** — ndjson-stdio-v1 wire format, commands, and events
- **[Security](/sonic-runtime/handbook/security/)** — Threat model and asset verification
- **[Beginners Guide](/sonic-runtime/handbook/beginners/)** — Step-by-step introduction for newcomers

## What sonic-runtime is

sonic-runtime is the native audio engine that [sonic-core](https://github.com/mcp-tool-shop-org/sonic-core) delegates to. It handles the work that TypeScript cannot do efficiently: low-latency audio playback via OpenAL Soft, per-playback device routing, and Kokoro ONNX TTS synthesis.

sonic-core spawns sonic-runtime as a child process and communicates over ndjson-stdio-v1 — newline-delimited JSON on stdin/stdout.

## What sonic-runtime is not

- Not a standalone application — it requires a parent process to send commands
- Not a UI or media player
- Not a session manager or product layer
- No user state, leases, or business logic (those live in sonic-core)
