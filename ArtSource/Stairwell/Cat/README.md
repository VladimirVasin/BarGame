# Stairwell cat art source

`StairwellCatSource.png` is the built-in image-generation output on a flat
magenta chroma-key background. `StairwellCatSource-alpha.png` is the approved
background-removed source used by `tools/build-stairwell-cat-atlas.py`.
`StairwellCatGroomingSource.png` and its `-alpha` derivative contain the
matching eight-frame paw-lick and face-wash sequence.

The runtime builder normalizes both generated sheets into one point-filtered
8 x 4 atlas of 64 x 64 cells:

- row 0: the seated back pose looking screen-left;
- row 1: the seated back pose looking straight away;
- row 2: the seated back pose looking screen-right;
- row 3: the complete grooming sequence;
- in rows 0-2, columns 0-3 breathe, columns 4-5 flick the tail and
  columns 6-7 twitch an ear.

The final runtime asset is
`Assets/Resources/Stairwell/Cat/StairwellCatAtlas.png`.
