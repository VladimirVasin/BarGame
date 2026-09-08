# Native audio effects

`AudioPluginIntoxicationVhs.cpp` exports two Unity mixer effects from one DLL:
`Intoxication VHS` and `Begotten Optical`. They share the plug-in plumbing —
one lock-free three-parameter mailbox, one epoch, one pause — and differ only
in the processor between them, so the shared callbacks are a template over the
processor type.

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

## Begotten Optical

`OpticalProcessor.h` owns the optical soundtrack of the Begotten print. Unlike
the tape it also SPEAKS: a print has a surface and a projector has a transport,
and both are audible over a silent room, so **an all-zero input is not a reason
to bypass here** — only a settled zero weight is. Everything it generates is
mono, because a 16 mm release print carries one variable-area track.

Parameters: `Weight` (0–1, the host's already-eased ramp), `Paused` (0–1),
`Reset` (0–1,000,000 epoch). The published contract is the block of
`static constexpr` at the top of `OpticalProcessor.h`;
`Assets/Scripts/Rules/BegottenAudioRules.cs` mirrors it and
`BegottenAudioRulesTests.TheRules_MirrorTheNativeHeader` reads this header and
fails the build if the two ever drift apart.

In order of processing: the channels fold to one track; the print's own surface
(`-30 dBFS` hiss and `-24 dBFS` dust, on the print and therefore shaped by the
print's band) is injected; the track swims in the gate; the optical band closes
(`10 → 200 Hz` over two poles, `22000 → 3800 Hz` over three, both interpolated
in log frequency, because a single pole left most of the energy above 5 kHz and
a linear sweep would be inaudible for most of the ramp); the emulsion saturates;
the frame line dips the track once per picture; the track is compressed `2.5:1`
above `-18 dBFS` with no make-up gain, because the print is quieter and Unity's
master headroom stays authoritative. Only then does the projector join, at
`-31 dBFS`: it stands in the room with the audience rather than on the film, so
it is neither band-limited nor compressed by the track's own limiting.

**The surface levels are PRE-band, and that is why the first tuning was
inaudible.** A three-pole gate throws away roughly nine tenths of white noise's
energy, so the original `-42 dBFS` hiss reached the ear at about `-51 dBFS` —
measured, not guessed. Do not tune these by their dB names; tune them by the
validator's `quiet_floor_rms`, which is the settled floor as heard.

Wow and flutter are `1.25 %` peak speed error: a serviceable machine holds
`0.3 %` and a tired portable reaches `1.2 %`, and this is a worn dupe through a
tired machine. Its four components are spool eccentricity at `0.9 Hz`, the
flywheel and sound drum at `2.4 Hz`, **the intermittent at `24 Hz`** and the
shutter at `48 Hz`, with shares summing to one so the depth is the peak error.

**The frame line is the strongest tie the ear has to the screen**: `22 %` of
the track dips away once per picture as the frame line crosses the sound slit,
which is the put-put a worn print makes on a tired machine. It is a dip and
never a gate — the sound head reads a continuous track, and a stutter would be
a lie about the medium, since picture and sound are read at different points of
the film precisely so the intermittent cannot reach the sound.

The frame line, the dust and the transport are all on the twenty-four per
second grid, counted with a fractional accumulator because `22050 / 24` is not
an integer.

The stages answer the one weight with different curves — the band closes and the
track folds first, the swim follows, the surface arrives as `w²` and the
apparatus as `w³` — so the fifteen seconds read as the world receding, and only
then as the world being projected.

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
objects, validation report and fourteen comparative WAVs stay in ignored
`Captures/AudioVhs`: six tape levels plus `intoxication-smooth-transitions.wav`,
and six print weights plus `begotten-fifteen-second-arrival.wav`, which is the
mode's real ramp — fifteen seconds in on the eased curve and three back out.
These synthetic reference clips are for listening, not game content. The
validator calls the actual DLL through Unity's published ABI and checks
onset/recovery, episode transitions and rapid target reversals during a repeat,
alongside dry bypass, lifecycle clearing, channel coherence and bounded output.

**The print's section of the validator is deliberately not a copy of the
tape's.** Three of the tape's assertions are false for an effect that generates
sound and must not be transplanted: silence in no longer means silence out; the
output may exceed the input peak, by the print's declared noise floor and no
more; and the tape's DC arrival probe cannot be used at all, because an optical
track has no DC and the print's own highpass would read as an abrupt arrival. A
tone above that highpass, measured as a windowed level rather than as a sample
difference, replaces it — a sample-difference measure would be reading the dust
clicks, which are deliberate transients. In their place the print is held to
things the tape has no opinion about: that it speaks over silence at all, that
its transport is locked to twenty-four pictures a second at `22050` and `48000`
Hz alike, and that across six weights the gate closes, the surface arrives and
the two channels fold into one track, each monotonically. A plain
difference-from-dry measure is useless here and is not used: the swim is a
VARIABLE delay, so on tonal material that difference is a phase reading which
rises and falls rather than growing.

Smoothness is likewise measured in a shape the print cannot fool. Windows are
one picture long, so the frame line's dip cancels exactly instead of reading as
roughness; the settled stretches are judged by the MEDIAN curvature, because
eleven dust clicks a second are deliberate content and a maximum would simply
find them; and the arrival is judged only by never stepping upward, because the
de-zipper's own bend is legitimate curvature and a threshold tight enough to
forbid it would forbid the effect itself.

The vendored `AudioPluginInterface.h` and `LICENSE.Unity.txt` are unchanged from
Unity Technologies' NativeAudioPlugins commit
`188a776e2d9f217af1afa574b92a11cd82600271` (API `0x010402`, compatible with this
project's Unity 6000.6). Upstream:
https://github.com/Unity-Technologies/NativeAudioPlugins/tree/188a776e2d9f217af1afa574b92a11cd82600271

The later upstream `0x010403` API targets Unity 6.7 and is intentionally not used.
Unity's MIT license is preserved beside the header. The plugin currently targets
the repository's Windows x86_64 editor/player; other platforms require a native
build and matching PluginImporter platform entry before shipping there.
