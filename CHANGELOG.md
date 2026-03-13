# Changelog

## v0.4.0

- **Audio backend replaced: SoundFlow → OpenAL Soft via Silk.NET** (ADR-0010)
- OpenAlBackend class wraps all unsafe pointer operations behind safe API
- Device enumeration via ALC_ENUMERATE_ALL_EXT (real hardware endpoints)
- Playback via OpenAL source/buffer model (replaces SoundPlayer/Mixer.Master)
- Pan maps -1..1 directly via SourceRelative + X position (no conversion needed)
- Completion detection via 10ms polling of AL_SOURCE_STATE (replaces event callback)
- WavReader for parsing WAV files into OpenAL buffers
- SynthesisEngine delegates slot loading to PlaybackEngine
- Removes SoundFlow dependency entirely
- 95 tests passing

## v0.3.1

- Engine event emission via IEventWriter (synthesis_started, synthesis_completed, playback_ended)
- CommandLoopEventWriter with late-binding Connect() for transport decoupling
- NullEventWriter for test isolation
- 84 tests (11 new event emission tests)

## v0.3.0

- Runtime introspection: get_health, get_capabilities, list_voices, preload_model, get_model_status
- Thread-safe event infrastructure (IEventWriter interface, concurrent queue)
- Source-generated JSON serialization for all event/introspection types
- 73 tests

## v0.2.0

- Kokoro ONNX TTS: text → eSpeak G2P → ONNX inference → WAV → playback
- VoiceRegistry (510 voices, raw float32 embeddings)
- KokoroTokenizer (eSpeak-NG child process)
- KokoroInference (ONNX Runtime, NativeAOT-compatible)
- ~5× realtime on CPU

## v0.1.1

- Phase 3 hardening: 23 edge-case tests
- SoundFlow wired into PlaybackEngine and DeviceManager

## v0.1.0

- Initial scaffold: C#/.NET 8 NativeAOT sidecar
- ndjson-stdio-v1 protocol (CommandLoop, CommandDispatcher)
- Playback: load, play, pause, resume, stop, seek, fade, volume, pan, loop
- Device enumeration and hot-plug handling
- Audio backend evaluation: SoundFlow selected over NAudio (NativeAOT compatibility)
