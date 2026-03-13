import type { SiteConfig } from '@mcptoolshop/site-theme';

export const config: SiteConfig = {
  title: 'sonic-runtime',
  description: 'Native audio engine sidecar — C# NativeAOT binary with OpenAL Soft, per-playback device routing, and Kokoro TTS synthesis',
  logoBadge: 'SR',
  brandName: 'sonic-runtime',
  repoUrl: 'https://github.com/mcp-tool-shop-org/sonic-runtime',
  footerText: 'MIT Licensed — built by <a href="https://mcp-tool-shop.github.io/" style="color:var(--color-muted);text-decoration:underline">MCP Tool Shop</a>',

  hero: {
    badge: 'NativeAOT sidecar',
    headline: 'Native audio engine',
    headlineAccent: 'for sonic-core.',
    description: 'C# NativeAOT binary that handles real audio — playback, device routing, and Kokoro TTS synthesis. Communicates with sonic-core over ndjson-stdio. Single native executable, no JIT, no runtime dependency.',
    primaryCta: { href: '#quickstart', label: 'Get started' },
    secondaryCta: { href: 'handbook/', label: 'Read the Handbook' },
    previews: [
      { label: 'Build', code: 'dotnet build' },
      { label: 'Publish', code: 'dotnet publish src/SonicRuntime \\\n  -c Release -r win-x64' },
      { label: 'Protocol', code: '→ {"id":1,"method":"version"}\n← {"name":"sonic-runtime",\n   "version":"0.5.0",\n   "protocol":"ndjson-stdio-v1"}' },
    ],
  },

  sections: [
    {
      kind: 'features',
      id: 'features',
      title: 'Features',
      subtitle: 'Everything the native audio layer provides.',
      features: [
        {
          title: 'OpenAL Soft',
          desc: 'Low-latency audio via Silk.NET OpenAL bindings. Source/buffer model with per-source volume, pan, and looping.',
        },
        {
          title: 'NativeAOT',
          desc: 'Single native executable via .NET 8 NativeAOT. No JIT warmup, no runtime dependency, sub-millisecond startup.',
        },
        {
          title: 'Per-Playback Device Routing',
          desc: 'Route individual playbacks to specific audio output devices. Multiple device/context pairs managed lazily.',
        },
        {
          title: 'Kokoro TTS',
          desc: 'Text-to-speech via ONNX inference with eSpeak G2P. 510 voices, ~5x realtime on CPU.',
        },
        {
          title: 'ndjson-stdio Protocol',
          desc: 'Strict JSON-over-stdio wire format with versioned handshake. Request/response + unsolicited events. No network sockets.',
        },
        {
          title: '95 Tests',
          desc: 'Comprehensive xUnit suite covering playback, device management, synthesis, protocol, and event emission.',
        },
      ],
    },
    {
      kind: 'code-cards',
      id: 'quickstart',
      title: 'Quick Start',
      cards: [
        {
          title: 'Build',
          code: '# Clone and build\ngit clone https://github.com/mcp-tool-shop-org/sonic-runtime\ncd sonic-runtime\ndotnet build\ndotnet test',
        },
        {
          title: 'Publish',
          code: '# Build NativeAOT single-file binary\ndotnet publish src/SonicRuntime \\\n  -c Release -r win-x64\n\n# Output:\n# src/SonicRuntime/bin/Release/\n#   net8.0/win-x64/publish/SonicRuntime.exe',
        },
        {
          title: 'Wire Protocol',
          code: '# Version handshake\n→ {"id":1,"method":"version"}\n← {"id":1,"result":{...}}\n\n# Load + play\n→ {"id":2,"method":"load_asset",\n   "params":{"asset_ref":"file:///rain.wav"}}\n← {"id":2,"result":{"handle":"h_000001"}}\n\n→ {"id":3,"method":"play",\n   "params":{"handle":"h_000001",\n   "volume":0.8,"loop":true}}\n← {"id":3,"result":null}',
        },
      ],
    },
  ],
};
