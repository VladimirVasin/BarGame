# Intoxication VHS native audio effect

`AudioPluginIntoxicationVhs.cpp` exports one Unity mixer effect named
`Intoxication VHS`. `TapeProcessor.h` owns its channel-coherent tape transport.
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

On Windows with Visual Studio's Desktop C++ workload, Python and NumPy installed:

```powershell
./tools/audio-vhs/build.ps1 -Validate
```

`tools/toolchain.json` pins Python, NumPy, MSVC and the Windows SDK. The script
checks them before compilation, validates the staged DLL, then replaces the
shipping DLL. Failure preserves the previous DLL and its `.meta`. Validation
is also the default without switches; `-CompileOnly` leaves a candidate in
`Captures/AudioVhs/native-build` without publishing it.

The deterministic MSVC x64 `/MT /Brepro` build writes the redistributable plugin
to `Assets/Plugins/AudioVhs/x86_64/AudioPluginIntoxicationVhs.dll`. Intermediate
objects, validation report and seven comparative WAVs stay in ignored
`Captures/AudioVhs`: six fixed levels and `intoxication-smooth-transitions.wav`.
These synthetic reference clips are for listening, not game content. The
validator calls the actual DLL through Unity's published ABI and checks
onset/recovery, episode transitions and rapid target reversals during a repeat,
alongside dry bypass, lifecycle clearing, channel coherence and bounded output.

The vendored `AudioPluginInterface.h` and `LICENSE.Unity.txt` are unchanged from
Unity Technologies' NativeAudioPlugins commit
`188a776e2d9f217af1afa574b92a11cd82600271` (API `0x010402`, compatible with this
project's Unity 6000.6). Upstream:
https://github.com/Unity-Technologies/NativeAudioPlugins/tree/188a776e2d9f217af1afa574b92a11cd82600271

The later upstream `0x010403` API targets Unity 6.7 and is intentionally not used.
Unity's MIT license is preserved beside the header. The plugin currently targets
the repository's Windows x86_64 editor/player; other platforms require a native
build and matching PluginImporter platform entry before shipping there.
