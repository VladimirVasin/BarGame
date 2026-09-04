# Intoxication VHS native audio effect

`AudioPluginIntoxicationVhs.cpp` exports one Unity mixer effect named
`Intoxication VHS`. `TapeProcessor.h` owns its channel-coherent tape transport.
Only instance creation allocates memory; the audio callback uses bounded
two-second history, with at most 750 ms of playback displacement. Each damaged
episode rejoins the current transport with an envelope, so delay cannot accumulate.
Zero intensity is an exact, zero-delay bypass. Pause, epoch changes, DSP schedule
gaps and all-zero input clear history; no noise is generated.

Parameters: `Intensity` (0–1, externally shaped), `Paused` (0–1), `Reset`
(0–1,000,000 epoch). The host supplies the exponential intoxication curve and
updates the epoch at lifecycle boundaries. Up to eight channels share the same
transport; unsupported channel counts pass through dry.

On Windows with Visual Studio's Desktop C++ workload, Python and NumPy installed:

```powershell
./tools/audio-vhs/build.ps1 -Validate
```

The deterministic MSVC x64 `/MT /Brepro` build writes the redistributable plugin
to `Assets/Plugins/AudioVhs/x86_64/AudioPluginIntoxicationVhs.dll`. Intermediate
objects, validation report and six comparative WAVs stay in ignored
`Captures/AudioVhs`. These synthetic reference clips are for listening, not game
content. The validator calls the actual DLL through Unity's published ABI.

The vendored `AudioPluginInterface.h` and `LICENSE.Unity.txt` are unchanged from
Unity Technologies' NativeAudioPlugins commit
`188a776e2d9f217af1afa574b92a11cd82600271` (API `0x010402`, compatible with this
project's Unity 6000.6). Upstream:
https://github.com/Unity-Technologies/NativeAudioPlugins/tree/188a776e2d9f217af1afa574b92a11cd82600271

The later upstream `0x010403` API targets Unity 6.7 and is intentionally not used.
Unity's MIT license is preserved beside the header. The plugin currently targets
the repository's Windows x86_64 editor/player; other platforms require a native
build and matching PluginImporter platform entry before shipping there.
