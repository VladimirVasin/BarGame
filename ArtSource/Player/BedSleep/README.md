# Player bed-sleep source frames

This directory owns the authored source sequence for the player's bed
interaction. The deterministic builder does not invent or modify source art;
it only validates, nearest-resamples and packs the completed sequence.

The approved imagegen contact sheets live under `Generated/`. Their keyed
RGBA versions live under `Keyed/`. Rebuild the 64 aligned source frames first:

```powershell
python tools/extract-player-bed-sleep-frames.py
```

The extraction schedule uses all 16 primary lie-down poses plus eight
additional in-betweens, all 16 authored sleeping-loop poses, and all 16
primary wake-up poses plus eight additional in-betweens. It aligns every
authored sprite around the shared Unity hip without baking the bed-space
translation into individual cells. After normalization, frames `000` and
`063` are replaced with one exact ordinary-rig endpoint; frames `001-062`
remain the complete keyed poses.

## Source contract

- Supply exactly 64 single-image RGBA PNGs named `frame-000.png` through
  `frame-063.png`. Extra `frame-*.png` files are rejected.
- Every source frame must use the same canvas dimensions. `128x96` is accepted
  directly; larger canvases are supported.
- Keep the hip at the normalized equivalent of `(64, 40)` in a `128x96`
  canvas, measured from the bottom-left to match Unity sprite-pivot space. For
  example, a `256x192` source uses hip anchor `(128, 80)` from the bottom-left
  (the same point is `(128, 112)` in top-left PNG coordinates).
- The builder uses one nearest-neighbour cover/crop transform for the complete
  sequence. Horizontal excess is centered. Vertical excess is cropped around
  the shared hip anchor so it remains at Unity bottom-origin cell coordinate
  `(64, 40)`.
- Alpha is made binary after resampling: values below `128` become transparent
  black and values from `128` upward become fully opaque.
- Every processed frame must contain at least one opaque pixel.
- Preserve the character lock in every applicable authored pose: dark-burgundy
  overshirt, desaturated navy trousers, black boots, pale bandage on the
  physical left forearm, ochre patch on the physical right shoulder and the
  diagonal satchel strap. Do not mirror authored frames `001-062`.
- Frames `000` and `063` must be pixel-identical exact endpoints derived from
  `PlayerDirectionalAtlas` direction cell `FrontLeft` (`7`). The extractor
  horizontally preflips that `64x96` cell, then centers it at `(32, 0)` in the
  `128x96` canvas. This is intentional: the bed definition applies
  `TextureFlipX = true` at runtime, restoring the visible endpoint to the
  ordinary `FrontLeft` orientation without a fade or blended pixels.

Logical animation ranges are inclusive:

| Frames | Phase |
| --- | --- |
| `000` | Exact preflipped ordinary `FrontLeft` entry endpoint |
| `001-023` | Lie down |
| `024-039` | Sleeping loop |
| `040-062` | Wake up |
| `063` | Exact preflipped ordinary `FrontLeft` exit endpoint |

At runtime, the sleeping range plays at `4 fps` with an extra `0.25 s` hold
on full-inhale frame `034` and an extra `0.75 s` rest on post-exhale frame
`027`. The resulting five-second loop preserves all 16 authored poses.

## Validate and build

From the repository root, validate the complete source sequence without
writing any file:

```powershell
python tools/build-player-bed-sleep-atlas.py --validate-only
```

Build the runtime atlas:

```powershell
python tools/build-player-bed-sleep-atlas.py
```

The output is
`Assets/Resources/Player/PlayerBedSleepAtlas.png`: an `8x8`, `1024x768` RGBA
PNG with `128x96` cells. Logical frame `0` is stored in the lower-left PNG
cell, frames advance left-to-right, and subsequent logical rows advance
upward. This matches Unity texture-space frame zero at `y=0`.

Custom paths are available for isolated validation or tooling tests:

```powershell
python tools/build-player-bed-sleep-atlas.py `
  --source-dir C:\path\to\frames `
  --output C:\path\to\PlayerBedSleepAtlas.png
```

The builder reads source files without changing them and writes only the
chosen output, using an atomic replacement after PNG round-trip validation.
It never creates placeholder frames or a placeholder final atlas.
