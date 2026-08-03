# Player cat-feeding source

This directory owns the approved player contact sheet for feeding the
stairwell cat. The deterministic builder validates and packs authored art; it
does not create poses, in-betweens or placeholder assets.

## Expected source

The default input is exactly:

`ArtSource/Player/CatFeeding/PlayerCatFeedingSource-alpha.png`

It must be an RGBA PNG containing one `8 x 8` pose grid. The current approved
image-generation handoff is a square `1254 x 1254` alpha sheet. The minimum
accepted sheet is `1024 x 768`; generated dimensions do not need to divide by
eight because integer half-up boundaries split the grid deterministically.
Every computed cell is inset by three source pixels so faint contact-sheet
separators cannot enter the runtime atlas. Every cell must contain visible art
plus transparent background. Run chroma-key removal before approval; the raw
magenta-backed sibling `PlayerCatFeedingSource.png` is provenance only and an
opaque/magenta player sheet is rejected by this builder.

Every source cell uses the same normalized pivot as a runtime `128 x 96`
interaction cell: Unity bottom-origin hip `(64, 40)`, equivalently 50 percent
from the left and `56/96` from the top. The builder applies the same nearest-
neighbour contain rule to all 64 complete source cells, then aligns that
normalized hip. Cells can differ by one source pixel when the sheet dimensions
do not divide evenly. This preserves proportions and avoids clipping rather
than stretching a square cell to `128 x 96`.

Eight-connected foreground components below
`max(4 pixels, 0.04 percent of the inset source-cell area)` are discarded as
separator residue or tiny artifacts. Every component at or above the threshold
is retained, including the separate food can in the handoff frames.

Alpha below `128` becomes transparent black and alpha from `128` upward becomes
fully opaque. Keep the established player design in every applicable pose:
dark-burgundy overshirt, desaturated navy trousers, black boots, pale bandage
on the physical left forearm, ochre patch on the physical right shoulder and
the diagonal satchel strap. Do not mirror frames. The can/bowl handoff must be
authored consistently with the cat sheet so the same prop does not appear in
both tracks at once.

The approved source faces image-right and remains unmodified here. The
MiddleFlight runtime shot places the cat camera-left, so
`StairwellCatInteraction` presents this atlas with `TextureFlipX = true`; this
puts the hero's face and the can toward the cat without altering source art.

## Source ordering and phases

Source frames use conventional top-left row-major order:

```text
top     00 01 02 03 04 05 06 07
        08 09 10 11 12 13 14 15
        16 17 18 19 20 21 22 23
        24 25 26 27 28 29 30 31
        32 33 34 35 36 37 38 39
        40 41 42 43 44 45 46 47
        48 49 50 51 52 53 54 55
bottom  56 57 58 59 60 61 62 63
```

The logical animation ranges are inclusive:

| Frames | Phase |
| --- | --- |
| `000-023` | Present/place the open can |
| `024-039` | Feeding action/hold poses |
| `040-063` | Return to the ordinary interaction stance |

Runtime timing and any repeated action frames are code-owned. The authored
sheet always supplies all 64 logical frames.

## Runtime packing contract

The output is:

`Assets/Resources/Player/PlayerCatFeedingAtlas.png`

It is a `1024 x 768` RGBA PNG containing `8 x 8` point-sampled `128 x 96`
cells. The builder reverses source row placement so logical frame `0` is the
lower-left PNG cell and subsequent logical rows advance upward. This is the
exact layout consumed by `PlayerAnimatedInteractionController`.

```text
visible PNG top     56 57 58 59 60 61 62 63
                    ...
visible PNG bottom  00 01 02 03 04 05 06 07
```

## Validate and build

From the repository root, validate without writing:

```powershell
python tools/build-player-cat-feeding-atlas.py --validate-only
```

Build the runtime atlas:

```powershell
python tools/build-player-cat-feeding-atlas.py
```

Custom paths are available for isolated tooling checks:

```powershell
python tools/build-player-cat-feeding-atlas.py `
  --source C:\path\to\PlayerCatFeedingSource-alpha.png `
  --output C:\path\to\PlayerCatFeedingAtlas.png
```

The builder reads the source without changing it, validates every cell, packs
in memory, verifies exact frame order and binary alpha, and writes the chosen
output through an atomic PNG round trip. `--validate-only` never writes or
creates the output.
