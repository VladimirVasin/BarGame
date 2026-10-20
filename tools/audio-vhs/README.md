# Native audio effect

`AudioPluginIntoxicationVhs.cpp` exports the single `Intoxication VHS` Unity mixer effect.
Its native DSP remains independent of graphics styles.

## Intoxication VHS

`TapeProcessor.h` owns its channel-coherent tape transport.
Only instance creation allocates memory; the audio callback uses bounded
two-second history, with at most 750 ms of playback displacement. Each damaged
episode rejoins the current transport with an envelope, so delay cannot accumulate.
Settled zero intensity is an exact, zero-delay bypass. Pause, epoch changes,
DSP schedule gaps and all-zero input clear history; no noise is generated.

Parameters: `Intensity` (0–1, externally shaped), `Paused` (0–1), `Reset`
(0–1,000,000 epoch). The host supplies the exponential intoxication curve and
updates the epoch at lifecycle boundaries. Up to eight channels share the same
transport; unsupported channel counts pass through dry.

The native intensity passes through two cascaded one-pole smoothers, each with
`tau = 0.22 s`. Changes ease through roughly the first second; a return to zero
fades fully dry over about four seconds, then takes the exact bypass once both
filter values fall below `0.00001`. This is additional audio-only smoothing;
it does not change the host's world tempo or visual response.

Episodes still last `0.4–1 s`, with onset spacing `2–4 s` at maximum intensity.
A quintic envelope uses the full episode: `45%` attack and `55%` release, with
no hard leading edge. Two playback heads crossfade repetition seams over up
to `55 ms`; the repeated cursor integrates its current speed instead of
recomputing travelled distance when intensity changes. The audible handoff
stays smooth at episode boundaries and during changes of strength.

## Build and validation

On Windows with the pinned Visual Studio Desktop C++ workload, Python and NumPy:

```powershell
./tools/audio-vhs/build.ps1 -Validate
```

`tools/toolchain.json` pins Python, NumPy, MSVC and the Windows SDK. The script
checks them, compiles a deterministic x64 `/MT /Brepro` candidate, validates its
actual Unity ABI and tape behavior, then publishes it through `asset_pipeline.py`
to `Assets/Plugins/AudioVhs/x86_64/AudioPluginIntoxicationVhs.dll`. Failure preserves
the shipping DLL and its `.meta`. Validation/publication is the default;
`-CompileOnly` stages a candidate in `Captures/AudioVhs/native-build` without publishing.

The validator covers supported sample rates, bounded/channel-coherent transport,
zero intensity and silence bypass, pause/epoch/gap resets, transitions and finite
output. It writes a report, six comparative levels and one smooth-transition WAV
under temporary `Captures/AudioVhs`. Inspect and delete task outputs afterwards.

The vendored `AudioPluginInterface.h` and `LICENSE.Unity.txt` are unchanged from
Unity Technologies' NativeAudioPlugins commit
`188a776e2d9f217af1afa574b92a11cd82600271` (API `0x010402`, compatible with this
project's Unity 6000.6). Upstream:
https://github.com/Unity-Technologies/NativeAudioPlugins/tree/188a776e2d9f217af1afa574b92a11cd82600271

The later upstream `0x010403` API targets Unity 6.7 and is intentionally not used.
Unity's MIT license is preserved beside the header. The plugin currently targets
the repository's Windows x86_64 editor/player; other platforms require a native
build and matching PluginImporter platform entry before shipping there.
