# Audio Backend Evaluation

Spike results for choosing the real audio backend behind PlaybackEngine and DeviceManager.

**Hard gate:** must build and run under NativeAOT (`dotnet publish -c Release -r win-x64`).
If it doesn't survive publish, nothing else matters.

## Acceptance Criteria

Each candidate must pass ALL of these to qualify:

- [x] Builds under NativeAOT (no trimming/reflection traps)
- [x] Enumerates output devices (names, IDs, default)
- [x] Plays a WAV file cleanly
- [x] Stops with fade-out (no click/pop)
- [x] Pans left/right without obvious zipper noise
- [x] Survives rapid play/stop churn (10+ cycles, no crash/leak)
- [x] No runtime feature traps at publish time (no `rd.xml` hacks)
- [x] Reasonable dependency footprint (sidecar stays lean)

---

## Candidate: NAudio

**Package:** `NAudio` (NuGet)
**Version tested:** 2.2.1
**NativeAOT result:** FAIL — publishes but crashes at runtime
**Device enumeration:** BLOCKED — `MMDeviceEnumeratorComObject` throws `InvalidProgramException`
**Playback (WAV):** BLOCKED (depends on WASAPI COM interop)
**Fade-out on stop:** BLOCKED
**Pan control:** BLOCKED
**Rapid churn:** BLOCKED
**Dependency footprint:** 2.2MB binary (acceptable)
**Blockers:** NAudio's WASAPI support uses COM interop (`MMDeviceEnumeratorComObject`) that emits IL incompatible with NativeAOT. The ILC warns during publish, then the binary crashes immediately at `MMDeviceEnumerator..ctor()`. This is a [known issue](https://github.com/naudio/NAudio/issues/1211) with no workaround. Also discussed in [NAudio NativeAOT discussion #1103](https://github.com/naudio/NAudio/discussions/1103).
**Verdict:** REJECTED — fundamentally incompatible with NativeAOT due to COM interop IL.

---

## Candidate: CSCore

**Package:** `CSCore` (NuGet)
**Version tested:** — (not tested)
**NativeAOT result:** NOT TESTED
**Blockers:** Last updated for .NET Framework 4.5. Library is unmaintained (last commit years ago). Uses similar COM interop patterns to NAudio for WASAPI. High probability of same NativeAOT failure. Skipped in favor of SoundFlow which explicitly supports NativeAOT.
**Verdict:** SKIPPED — unmaintained, likely same COM interop problem.

---

## Candidate: SoundFlow

**Package:** `SoundFlow` (NuGet) — [GitHub](https://github.com/LSXPrime/SoundFlow)
**Version tested:** 1.1.1
**NativeAOT result:** PASS — publishes cleanly (one trim warning, non-blocking)
**Device enumeration:** PASS — detected 3 real devices (Realtek speakers, DisplayPort monitor, USB-C dock headphones), correct default identification
**Playback (WAV):** PASS — 440Hz test tone played cleanly through default device
**Fade-out on stop:** PASS — volume ramp from 1.0 → 0.3 smooth, no click on stop
**Pan control:** PASS — left/center/right pan worked. Note: v1.1.1 uses 0.0–1.0 range (0.0=left, 0.5=center, 1.0=right), not -1.0 to 1.0. Runtime must translate.
**Rapid churn:** PASS — 10 rapid play/stop cycles completed cleanly, no crash, no leak
**Dependency footprint:** 2.2MB exe + ~300KB `miniaudio.dll` (two files total, excellent for sidecar)
**Blockers:** None for v1 requirements.

**Observations:**
- Uses MiniAudio as native backend (P/Invoke, not COM interop) — this is why NativeAOT works
- Singleton `AudioEngine.Instance` pattern (one engine per process — fine for sidecar)
- Static `Mixer.Master` for the audio graph root
- `SoundPlayer` does not implement `IDisposable` — must manage lifecycle explicitly
- Maintainer on hiatus Jan 2026 – Feb 2027, but library is stable at v1.1.1
- Cross-platform native runtimes included (win-x64, linux-x64, osx-arm64, etc.)
- 436 GitHub stars, MIT license, .NET 8+ target

**Pan range note for runtime integration:**
sonic-core uses -1.0 to 1.0 (standard audio convention).
SoundFlow v1.1.1 uses 0.0 to 1.0.
Mapping: `soundflowPan = (corePan + 1.0) / 2.0`

**Verdict:** ACCEPTED — passes all acceptance criteria. Clean NativeAOT build, real device enumeration, playback with volume/pan control, survives churn.

---

## Final Decision

**Winner:** SoundFlow v1.1.1
**Date:** 2026-03-12
**Rationale:** Only candidate that passes the NativeAOT hard gate. NAudio fails due to COM interop. CSCore is unmaintained with the same likely failure mode. SoundFlow uses MiniAudio (native P/Invoke) which avoids COM entirely, builds and runs cleanly under NativeAOT, enumerates real devices, plays audio with volume/pan control, and survives rapid lifecycle churn. Two-file deployment (exe + miniaudio.dll) fits the sidecar model. Pan range difference (0–1 vs -1–1) is a trivial mapping in the runtime layer.
