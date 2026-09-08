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
fails the build if the two ever drift apart. Every published constant must be a
**plain literal**: the mirror's regex reads nothing else, and
`TheHeaderPublishesLiterals_BecauseTheMirrorCannotReadExpressions` now fails the
build on an expression — which is how `IntermittentHz` and `ShutterHz` were
published as products and checked against nothing for a whole release.

### The track is a geometry, not a channel with a waveshaper on it

The first tuning modelled the print as a linear channel with a gentle `tanh` at
the end, and that is a topology error rather than a tuning one. A variable-area
trace carries the programme as the WIDTH of a clear wedge and gives it back as
the AREA of light through a slit; it cannot open past the mask nor close past
base fog. Everything harsh about a worn print falls out of that, and none of it
is referenced to dBFS — which is exactly what went wrong. `tanh(2.8x)/2.8` at
the levels a game bus carries is a straight line: the shipped print measured
**1.4–4 % distortion, sat 7 dB under the world it replaced, and kept 3.5 % of
its energy in 1–6 kHz.** Quiet, dull and warm — the opposite of what the mode is
for. It now measures **37 % distortion**, sits about 3 LU under the game's own
themes and carries 11 % of its energy in 1–6 kHz.

In order of processing:

1. **The channels fold to one track.** A release print carries one track.
2. **The track swims in the gate**, `1.25 %` peak speed error. The
   oversampler's own group delay is subtracted here, so the wet path's total
   displacement is still `a·(base + swim)`.
3. **The galvanometer**, one TPT pole of the `10 → 300 Hz` highpass, ahead of
   the emulsion: an optical recorder cannot write much low end, and bass that
   reaches the mask eats the whole modulation ceiling and drags the mids down.
4. **The meter and the mixer's hand.** A film has no opinion about dBFS; it has
   a slit that is 100 % modulated at a reference level and a dubbing mixer
   riding a PEAK meter to it. The hand rides the dub so peaks sit
   `TargetOvermodulationDb = 9 dB` over full modulation, and it is a SLEW in
   decibels — `0.16 dB` per picture up, `2.00` down — and he has his level
   before the take rather than finding it during it, or six seconds of every
   reel go by with nothing able to tear. **Peak and not RMS**: an
   RMS reference is crest-blind, so a 3 dB-crest sine would never reach the
   knee while a 12 dB-crest bed tore, and the amount of grind would be set by
   the source's crest factor instead of by the medium. Below
   `ExposureHoldPeak` the hand holds rather than chasing a quiet room to the
   top of its travel.
5. **Record-side pre-emphasis, never undone**: a `4×` shelf with its zero at
   `1200 Hz`. Prints were cut bright to survive the Academy curve and nobody
   installed a de-emphasis filter — the slit and the horn WERE the de-emphasis,
   and they are stages 9 and 10 here. It also decides which band drives the
   emulsion, so the odd harmonics are made from 1–3 kHz material and land where
   the slit still passes them.
6. **Cross-modulation**, the classic named distortion of an optical track and
   the one mechanism that makes this a print rather than a pedal. The printed
   edge sits where the smeared exposure crosses the stock's threshold, so a
   high-frequency wiggle's smeared peaks never reach full exposure and the mean
   edge shifts with the wiggle's AMPLITUDE. The detector is BANDED at
   `min(6000, rate/6)`, not merely split: rectifying anything above `rate/6`
   folds dense inharmonic products back into the passband, worst at 22050.
7. **The emulsion**, at twice the rate, in double, with first-order
   antiderivative antialiasing. An asymmetric soft-shouldered clip on
   modulation: the mask closes from `ModulationRest` to `1.0` on `w^0.55`, so
   the track is tearing well inside the fifteen seconds, and printing spreads
   the clear side `20 %` wider than the dark one — **the only reason a second
   harmonic exists at all**, since an odd-symmetric curve cannot make one at
   any drive. Store the raw previous MODULATION sample and evaluate both
   antiderivatives with the CURRENT sample's mask: the mask moves every sample,
   so a cached `F(m[n-1])` carries a term divided by a difference that is
   smallest exactly where the material is quietest.
8. **The output transformer**, the second TPT pole of the highpass, which
   removes the offset the asymmetric mask leaves. A TPT subtractive one-pole
   has exactly zero gain at DC, so one pole removes it completely.
9. **The projector's fader**, `ProjectorGainDb`, FIXED. See below.
10. The **surface and its dust**, injected here in output units: grain is a
    property of the developed emulsion and dirt is in the gate, so neither is
    recorded through the characteristic curve and neither is scaled by the
    mixer's hand. Then the **frame line**, the **lamp's ripple**, the **slit**,
    the **cone in the lid**, the **transport** and the **rail**.

### How the print ARRIVES, which is a separate question from what it does

The first version of this chain was judged by ear and rejected a second time:
«я ощущаю изменения только при 100 % применении фильтра». The measurement
agreed — distortion across the ramp went `0 → 0.79 → 1.4 → 16 → 37 %`, so five
sixths of it happened in the last fifth. Three separate causes, all of them the
same mistake in different clothes: **a quantity that is heard on a logarithmic
scale was interpolated linearly against the weight, so its audible part was
spent at the end.**

- **The drive.** The mixer's ride is a RATIO against the mask, and scaling it
  by the weight put the whole travel into the last quarter: at half weight the
  dub was still twelve decibels under the reference, and nothing can tear
  however wide the mask stands. It is now a fixed `ArrivalHeadroomDb = 20`
  closing on the reference as `w^0.20` — **fixed decibels and not a fraction of
  the ride**, because the ride depends on how loud the room happens to be and
  the shape of an arrival must not. The projector's fader takes back exactly
  what the early drive adds, so the published gain across the ramp is
  unchanged: only the ratio the emulsion sees moves forward.
- **The bands.** They sweep four and a half octaves at the bottom and under two
  at the top, so at half weight the slit stood at `11.8 kHz` and the highpass at
  `55 Hz` — neither of which can be heard leaving. They now arrive as `w^0.50`.
- **The amplifier's voice.** It was on `w²`, put there to hold down a swell in
  the middle of the ramp. Measured, the swell moved `0.1 LU` — and the print's
  whole bite moved into the last fifth. It arrives with the weight now.

Measured on a 440 Hz tone at `−20 dBFS`, distortion across the ramp is now
`0 → 3.1 → 11.4 → 18.1 → 23.2 → 27.4 → 31.3 → 37.3 %` at weights
`0, .1, .2, .35, .5, .65, .8, 1`, and the presence share on the bed rises
throughout instead of jumping at the end. The validator holds the shape: more
than `4 %` a fifth of the way in, `8 %` a third of the way, `15 %` at half.

### The fader is fixed, and that is the whole level story

`ProjectorGainDb` is a constant, deliberately NOT the reciprocal of the mixer's
ride. A reciprocal make-up was tried and is wrong twice over: it pins the output
peak to the bus level, so the rail becomes a permanent second clipper on a hot
bus, and it is a content-dependent compensation for a band loss, so mid-heavy
material leaves hot with nothing able to see it. Fixed, the printed peak is the
mask's own height times the fader **for any input at all** — a whisper, a
full-scale square, DC, noise — so the medium's density sets the level and the
rail is genuine headroom that provably never engages. The validator asserts
exactly that: a square, full-scale noise and a quiet room print at peaks of
`0.153`, `0.209` and `0.181` — within a factor of three of each other and never
reaching the `0.501` rail — while DC and anything below the meter's hold peak
print as silence does.

The price, stated plainly: **a bus quieter than the reference prints LOUDER**,
bounded by `ExposureMaxGainDb + ProjectorGainDb`, and a hotter one quieter. That
is what a fixed-density medium does. It also means the fader must be referenced
to the level the GAME carries: set on the validator's bed, which is six decibels
under the themes, it put the print `7.3 LU` below the music it was replacing —
which reads as the game turning itself down rather than as a projector standing
in the room.

### The bands, and why the topology is part of the contract

Every filter is TPT. **This is not cosmetic.** The exponential one-pole
`y += c(x−y)` has a stopband FLOOR of `c/(2−c)`, which at 22050 Hz with a
6300 Hz corner is `−2.9 dB` per pole: four of them could never attenuate more
than 12 dB, and the print would have been bright and full-band at the one rate
the old spectral loop never visited. TPT places a zero at Nyquist and prewarps
the corner, so the published number means the same thing at 22050, 44100, 48000
and 96000 — and the validator now measures the response at all four.

The high end is the SLIT itself: 16 mm at 24 pictures runs `182.9 mm/s` past the
sound head, so a worn, badly focused `29 µm` slit puts its first aperture null at
`6307 Hz`. Four cascaded poles there are `−3 dB` at `2740 Hz`, within 2 % of the
true sinc aperture. The old three poles at `3800 Hz` were `−3 dB` at `1937 Hz` —
a telephone, not a slit, and they threw the harsh band away before the emulsion
ever saw it.

Above the slit sits the **cone in the lid** and the slit-loss compensation every
16 mm amplifier carried: a bell at `4200 Hz`, `Q 1.60`, `+10 dB`. It and the
pre-emphasis are the AMPLIFIER's voice rather than the film's, so they arrive on
`w²` like the transport — arriving with the weight they added their gain while
the slit was still open, and the middle of the ramp measured LOUDER than the
world it was replacing.

The measured linear response at full weight, relative to 1 kHz, is `−18 dB` at
120 Hz, `+3.5` at 2 k, `+7.5` at 3.15 k, **`+11.2` at 4.2 k** and `−0.2` at
6.3 k, falling to `−15` at 10 k: a presence peak with a cliff immediately above
it. That shape is the target — not a bright print.

### Surface, apparatus and the picture grid

Wow and flutter are `1.25 %` peak speed error: a serviceable machine holds
`0.3 %` and a tired portable reaches `1.2 %`, and this is a worn dupe through a
tired machine. Its four components are spool eccentricity at `0.9 Hz`, the
flywheel and sound drum at `2.4 Hz`, **the intermittent at `24 Hz`** and the
shutter at `48 Hz`, with shares summing to one so the depth is the peak error.

**The frame line is the strongest tie the ear has to the screen**: `34 %` of the
track dips away once per picture as the frame line crosses the sound slit. At
the first tuning's `22 %` and sharpness `14` its 24 Hz comb line was a quarter
of a decibel — well under the roughness threshold, which is why it read as
nothing at all. It is a dip and never a gate: picture and sound are read at
different points of the film precisely so the intermittent cannot reach the
sound, and it stays AFTER the emulsion because a clipper flattens any modulation
applied before it.

The **exciter lamp** on a tired smoothing capacitor modulates the LIGHT, and
light follows power, so its ripple is at twice mains. It multiplies rather than
adds, which is why a sick projector buzzes rather than hums. Its depth is
policed: the validator asserts that **everything loud in the apparatus sits on
the picture grid** and that nothing off that grid is louder than the picture
itself. 48 Hz is not a rival — the claw pulls once a picture and the shutter's
second blade falls halfway through it, so 48 Hz is the picture's own second
harmonic.

The surface levels are POST-fader and in output units. Do not tune them by their
dB names; tune them by the validator's `quiet_floor_rms`, which is the settled
floor as heard and is held to a window of `0.004–0.020`.

### What the validator measures, and why it is not the tape's list

**The print's section of the validator is deliberately not a copy of the
tape's.** Three of the tape's assertions are false for an effect that generates
sound and tears what it is given: silence in no longer means silence out; the
output is bounded ABSOLUTELY rather than by the input peak, because the medium's
density sets the level; and the tape's DC arrival probe cannot be used at all,
because an optical track has no DC.

Everything is measured on the SIGNAL PATH, `effect(x) − effect(zeros)` from two
identically seeded fresh instances. That difference is exact because the noise
rides a counter that advances once per sample whatever the input and joins after
the emulsion — but **only while the rail is idle**, which every probe asserts.
Without it, the old `4.13 % THD at −26 dBFS` was not distortion at all: it was
the print's own hiss landing in the harmonic bands.

- **The print tears**, above 20 % on a 440 Hz tone at every bus level from
  `−34` to `−8 dBFS`, and **within 40 % of itself across that whole range** —
  the measured spread is nil, because the ride is referenced to the medium. It
  arrives monotonically across the ramp (`0 → 0.8 → 6 → 37 %`) and is exactly
  zero at rest.
- **The gate is measured LINEARLY**, with a multitone BELOW the meter's hold
  peak so the mixer's hand stays off the fader altogether, at all four rates.
  The old measure ran loud broadband noise through a clipper and called the
  result a filter response.
- **Every threshold on the ramp is relative**, because an absolute one silently
  loosens the moment the print's level moves. The settled stretches are judged
  by MEDIAN curvature (eleven dust clicks a second are deliberate content and a
  maximum would simply find them), the arrival by never stepping upward, and
  the recovery by what happens AFTER the de-zipper — the sixty milliseconds
  themselves are the ramp, not a slam, and measuring them would only be
  measuring `DezipperSeconds`.
- Probes are sized from the header: the slowest published behaviour is the
  mixer's hand, whose whole travel is `ExposureMaxGainDb / (GainRiseDbPerPicture
  × PicturesPerSecond)` ≈ 11.7 s, and a shorter probe measures an unconverged
  ride and biases every distortion reading low.
- The validator parses the header for its own bounds rather than keeping a
  fourth copy of them.

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
