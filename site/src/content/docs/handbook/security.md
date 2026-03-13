---
title: Security
description: Threat model, asset verification, and security posture.
sidebar:
  order: 4
---

## Threat model

sonic-runtime is a **local-only** NativeAOT sidecar. It communicates exclusively over stdin/stdout with its parent process. There are no network sockets, no HTTP endpoints, and no remote API.

### Attack surface

| Surface | Risk | Mitigation |
|---------|------|------------|
| Audio file paths | Path traversal | Validated by the runtime; operator controls source directories |
| ONNX model files | Model tampering | Operator-installed; loaded from configured asset directory |
| eSpeak-NG subprocess | Command injection | Fixed argument format; no user-controlled parameters |
| stdio protocol | Message injection | Local IPC only; parent process controls all input |

### Out of scope

- **Network attacks** — no listening sockets exist
- **Authentication** — no auth layer; access controlled by the parent process
- **Multi-user** — single-user local tool

## Asset verification

Synthesis assets (ONNX models, voice embeddings, eSpeak data) are loaded from an operator-configured directory. The runtime validates file existence and format but does not verify cryptographic integrity of assets. Operators should verify asset provenance before deployment.

## No telemetry

sonic-runtime collects **no telemetry**, analytics, or usage data. No network requests are made. All communication is over stdin/stdout with the parent process.

## Reporting vulnerabilities

If you discover a security issue, email [64996768+mcp-tool-shop@users.noreply.github.com](mailto:64996768+mcp-tool-shop@users.noreply.github.com). We will respond within 7 days.

Do not open public issues for security vulnerabilities.
