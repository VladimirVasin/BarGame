# Player balcony-smoking source frames

This directory owns the authored source sequence for the player's balcony
smoking interaction. The deterministic extractor registers the approved
contact-sheet poses, matches their proportions to the ordinary side-profile
rig and creates the exact idle handoff described below. The packer does not
draw, invent, mirror or repair animation art: it only validates the complete
source sequence, applies one shared nearest-neighbour fit/crop, normalizes
alpha and packs the runtime atlas.

Do not commit draft contact sheets or generated placeholders as
`frame-*.png`. A frame filename means that the corresponding pose below is
finished and approved.

## Locked visual contract

- Supply exactly 64 single-image RGBA PNGs named `frame-000.png` through
  `frame-063.png`. Extra `frame-*.png` files are rejected.
- Every source frame must use the same canvas dimensions. A `128x96` source
  is accepted directly; larger canvases are supported when every frame uses
  the same size and normalized hip position.
- The runtime cell is exactly `128x96`. Its hip pivot is `(64, 40)` measured
  from the bottom-left in Unity sprite space. On a `128x96` PNG this is
  `(64, 56)` in top-left image coordinates. A `256x192` source therefore uses
  `(128, 80)` from the bottom-left, or `(128, 112)` from the top-left.
- Keep the body registered to the hip in every frame. Body motion belongs
  inside the cell; do not bake balcony placement or camera motion into the
  sprite.
- Use a transparent background. Do not draw the balcony, rail, room, camera
  matte, color grade or depth mask into a frame.
- Preserve hard pixel clusters. Smoke is sparse gray-green dithering, not a
  translucent blurred cloud. The ember is at most one or two warm pixels.
- The builder thresholds alpha after nearest resampling: alpha below `128`
  becomes transparent black and alpha `128` or above becomes fully opaque.
  Every processed frame must contain the complete visible character and may
  not be empty or a placeholder.

### Physical asymmetry and balcony orientation

The character design is physically asymmetric and that asymmetry is part of
the animation contract:

- the pale bandage stays on the character's **physical left forearm**;
- the ochre patch stays on the **physical right shoulder**;
- the diagonal satchel strap keeps the same physical direction;
- the cigarette is handled by the physical left hand;
- the lighter is handled by the physical right hand.

The balcony dock and its camera select the ordinary rig's
`PlayerViewDirection.Right` reference (direction cell `2`). That cell visibly
faces **texture-left**, and user playtesting established that this is the
correct city-facing screen direction. Frames `000` and `063` copy that exact
cell without mirroring. All generated smoking poses use the same texture-left
orientation, the extractor mirror is off, and the smoking animation definition
uses `flipX == false` at runtime. Do not pre-mirror the sequence, do not
alternate mirrored frames and do not swap the bandage, shoulder patch,
cigarette or lighter between hands to compensate frame by frame. The packer
never mirrors pixels.

## Exact 64-frame animation contract

Logical animation ranges are inclusive and contiguous:

| Frames | Phase | Runtime intent |
| --- | --- | --- |
| `000-023` | Enter | Slow preparation, cigarette, lighter and first drag |
| `024-047` | Loop | Melancholic rest, drag, breath hold and side exhale |
| `048-063` | Exit | Safe neutral bridge, discard, lingering look and idle |

Every logical frame has an authored purpose:

| Frame | Pose and change from the preceding frame |
| --- | --- |
| `000` | Exact balcony-facing ordinary idle match: grounded hips, relaxed arms, no cigarette visible. |
| `001` | First ordered-dither bridge from exact idle; gaze begins to settle. |
| `002` | Idle colors and silhouette continue blending into the tired stance. |
| `003` | Weight transfer becomes readable while feet stay on the shared baseline. |
| `004` | The generated stance becomes dominant without a one-frame scale pop. |
| `005` | Shoulders finish dropping as the exact-idle bridge continues. |
| `006` | Physical left elbow separates while the last idle pixels recede. |
| `007` | Final bridge frame; bandaged left forearm starts crossing inward. |
| `008` | First wholly generated pose; fingertips reach the pocket opening. |
| `009` | Left hand enters the pocket and the shirt fabric compresses subtly. |
| `010` | Left hand begins withdrawing with the cigarette barely visible. |
| `011` | Cigarette clears the pocket between the left fingers. |
| `012` | Left wrist turns so the cigarette is readable without enlarging it. |
| `013` | Left hand begins its slow rise toward the face. |
| `014` | Cigarette passes chest height; head inclines by a pixel toward it. |
| `015` | Cigarette reaches the lips; mouth and hand meet without a pop. |
| `016` | Physical right hand moves toward the lighter pocket. |
| `017` | Right hand raises the lighter and begins sheltering it near the face. |
| `018` | Both hands form the windbreak pose; cigarette remains at the lips. |
| `019` | One-frame restrained lighter spark; no large flash or glow halo. |
| `020` | First inhalation starts; ember gains its first warm pixel. |
| `021` | First inhalation peaks; chest rises slightly and ember is brightest. |
| `022` | Right hand closes and lowers the lighter while breath is held. |
| `023` | Left hand lowers with the lit cigarette; pose bridges exactly into frame `024`. |
| `024` | Loop rest begins with the cigarette low and gaze beyond the rail. |
| `025` | Minimal breath/cloth motion; hips and planted feet remain locked. |
| `026` | Head lowers by a pixel, deepening the distant downward gaze. |
| `027` | Quietest rest pose, designed for the long `+2.00 s` pause. |
| `028` | Left elbow releases from rest and begins the next lift. |
| `029` | Cigarette rises past the waist in the bandaged left hand. |
| `030` | Left forearm crosses the torso; shoulders remain slumped. |
| `031` | Cigarette approaches the face as the head inclines slightly. |
| `032` | Cigarette arrives at the lips without changing planted foot contact. |
| `033` | Drag begins; cheeks/chest tighten minimally and ember warms. |
| `034` | Inhalation deepens; ember reaches the brighter two-pixel state. |
| `035` | Peak drag, designed for the `+0.65 s` held inhalation. |
| `036` | Left hand leaves the lips and starts lowering; ember dims. |
| `037` | Hand continues down while the character keeps the breath held. |
| `038` | Still breath-hold pose, designed for the `+0.55 s` pause. |
| `039` | Head turns a fraction away from the camera before the exhale. |
| `040` | First tight smoke cluster leaves the mouth sideways. |
| `041` | Exhale lengthens; smoke remains sparse and wind-directed. |
| `042` | Smoke trail grows and bends, with no soft transparency. |
| `043` | Main smoke mass separates from the mouth. |
| `044` | Exhale finishes while the detached smoke begins breaking apart. |
| `045` | Remaining smoke fragments thin as head and chest relax. |
| `046` | Left hand and shoulders settle back toward the rest silhouette. |
| `047` | Exact loop return pose with the long `+2.30 s` pause; it must transition cleanly to `024`. |
| `048` | Safe neutral exit bridge derived from the current rest silhouette, never a sudden raised-hand cut. |
| `049` | Eyes move from the city toward the dim ember. |
| `050` | Head lowers further; left wrist rotates the cigarette into view. |
| `051` | Brief resigned inspection of the ember with both shoulders still. |
| `052` | Left hand begins extending outward beyond the rail line. |
| `053` | Arm reaches its furthest comfortable extension; cigarette stays pinched. |
| `054` | Fingers release/flick the cigarette downward with a restrained ember pixel. |
| `055` | Cigarette begins falling; left hand remains suspended. |
| `056` | Falling ember dims below the hand while the gaze follows it. |
| `057` | Ember disappears; gaze lingers down for one last beat. |
| `058` | Empty left hand starts returning and the reverse idle bridge begins. |
| `059` | Left elbow folds back while exact-idle colors start replacing the smoking pose. |
| `060` | Hand drops alongside the body as the silhouette narrows toward ordinary idle. |
| `061` | Shoulders and weight ease into the exact ordinary-rig registration. |
| `062` | Final ordered-dither bridge: no cigarette, ember, lighter or smoke remains. |
| `063` | Exact ordinary idle-rig silhouette match for a seamless visual handoff. |

The intended timing is `6 fps` for frames `000-047` and `8 fps` for frames
`048-063`. Loop holds are authored by runtime timing, not by inserting extra
or duplicated files: `027 +2.00 s`, `035 +0.65 s`, `038 +0.55 s` and
`047 +2.30 s`. The resulting loop is `9.50 s`.

## Deterministic extraction from the approved sheets

The four approved RGB imagegen contact sheets live under `Generated/`. Their
RGBA copies under `Keyed/` were processed with the imagegen skill's official
`remove_chroma_key.py` helper using border key sampling, soft matte,
thresholds `12/220` and despill. The generated backgrounds vary slightly
around green, so fixed `#00ff00` equality is not sufficient.

`tools/extract-player-balcony-smoking-frames.py` slices the keyed sheets as a
top-left, row-major `4x4` grid. The ordinary raw numbering is:

| Raw frames | Keyed sheet |
| --- | --- |
| `000-015` | `enter-000-015.png` |
| `016-031` | `enter-loop-016-031.png` |
| `032-047` | `loop-032-047.png` |
| `048-063` | `exit-048-063.png` |

Raw `024-031` belong to a larger three-quarter-view family and do not join
the strict side-profile loop at either boundary. The extractor therefore
uses this locked, deterministic reuse map for those eight logical frames:

| Logical frame | Raw source | Purpose |
| --- | --- | --- |
| `024` | `047` | Clean low-cigarette rest; exact loop-wrap bridge |
| `025` | `048` | Adjacent clean low-cigarette side rest |
| `026` | `038` | Clean low-cigarette side rest |
| `027` | `047` | Long rest; pixel-identical to logical `024` |
| `028` | `038` | Start the reversed lowering arc as a lift |
| `029` | `037` | Continue the lift |
| `030` | `036` | Approach the face |
| `031` | `035` | Mouth pose nearest unchanged logical `032` |

Logical `032-047` remain their original raw `032-047` poses. Consequently
the loop wrap `047 -> 024` is pixel-identical, while `031 -> 032` is a
near-identical mouth-pose transition. None of the reused initial-rest poses
contains exhaled smoke, and every one retains the cigarette.

One base scale is derived for the sequence. The third and fourth generated
sheets depict the same character about seven percent smaller, so their cells
receive a locked `1.075x` family correction before the common foot alignment.
The generated figure is normalized to an `84 px` character height and a
locked `0.620x` horizontal scale so its three-quarter source proportions do
not pop wider than the ordinary side-profile idle. Every pose is registered by
its planted-foot center to pivot x-coordinate `64` and planted-foot baseline
`y=92`; the shared anatomy/scale keeps the hip at the intended top-left PNG
y-coordinate `56` (Unity bottom-origin `(64,40)`).

The extractor reads `PlayerDirectionalAtlas.png`, copies ordinary direction
cell `2` into frames `000` and `063` pixel-for-pixel, and keeps the same feet
and hip registration. Frames `001-007` use a deterministic `8x8` Bayer
ordered-dither/RGB bridge from that idle to their corresponding generated
poses; frame `008` is wholly generated. Frames `058-062` apply the inverse
bridge, ending on exact idle at `063`. This turns the former direct idle cut
into several bounded pixel-art steps at both ends. No random state or
generative rerun is involved. The extractor and runtime smoking definition
both leave `flipX` off, preserving the approved texture-left orientation.

Re-extract all 64 source frames, or validate the result entirely in memory:

```powershell
python tools/extract-player-balcony-smoking-frames.py
python tools/extract-player-balcony-smoking-frames.py --validate-only
```

For isolated tooling tests, an alternative reference atlas can be supplied
with `--idle-atlas`; production extraction always uses
`Assets/Resources/Player/PlayerDirectionalAtlas.png`.

## Validate and build

From the repository root, validate all sources and build the atlas only in
memory:

```powershell
python tools/build-player-balcony-smoking-atlas.py --validate-only
```

Build the runtime atlas:

```powershell
python tools/build-player-balcony-smoking-atlas.py
```

The only default output is
`Assets/Resources/Player/PlayerBalconySmokingAtlas.png`: an `8x8`,
`1024x768` RGBA PNG with `128x96` cells. Logical frame `000` is stored in the
lower-left PNG cell. Frames advance left-to-right; later logical rows advance
upward so Unity texture-space frame zero is at `y=0`.

Custom paths are available for isolated validation and tooling tests:

```powershell
python tools/build-player-balcony-smoking-atlas.py `
  --source-dir C:\path\to\frames `
  --output C:\path\to\PlayerBalconySmokingAtlas.png
```

The builder reads source files without changing them and writes only the
chosen output. A normal build atomically replaces the destination after a PNG
round-trip check. `--validate-only` never writes. Missing, extra, unreadable,
wrong-mode, differently sized or empty frames are hard failures; the builder
never creates source frames or a placeholder atlas.
