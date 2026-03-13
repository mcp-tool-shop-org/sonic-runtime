# Security Policy

## Threat Model

sonic-runtime is a local-only NativeAOT sidecar. It communicates exclusively over stdin/stdout with its parent process (sonic-core). There are no network sockets, no HTTP endpoints, and no remote API.

**Attack surface:**
- File paths for audio assets (WAV files) — validated but loaded from operator-controlled paths
- ONNX model files — loaded from operator-configured asset directory
- eSpeak-NG subprocess — spawned for phoneme conversion, operator-installed
- stdio protocol — local IPC only, parent process controls all input

**Out of scope:**
- Network attacks (no listening sockets)
- Authentication (no auth layer; controlled by parent process)
- Multi-user access (single-user local tool)

## No Telemetry

sonic-runtime collects no telemetry, analytics, or usage data. No network requests are made. All communication is over stdin/stdout with the parent process.

## Reporting a Vulnerability

If you discover a security issue, please email [64996768+mcp-tool-shop@users.noreply.github.com](mailto:64996768+mcp-tool-shop@users.noreply.github.com) with details. We will respond within 7 days.

Please do not open public issues for security vulnerabilities.
