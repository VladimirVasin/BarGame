# Home surface albedos

The twelve apartment surface sheets in `Assets/Resources/Home/Textures/`
are built, validated and hashed by `tools/build-home-textures.py`
(Pillow is the only dependency):

```bash
python tools/build-home-textures.py            # write sheets + manifest
python tools/build-home-textures.py --verify   # build and validate only
```

The build is byte-deterministic: every sheet's SHA256 is recorded in
[`home-textures.json`](home-textures.json), and a re-run must reproduce it
exactly. `home-contact-sheet.png` shows each sheet tiled two by two, in
colour and in grayscale.

## The numbers that are contracts, not taste

Both are asserted by `Assets/Tests/EditMode/HomeSurfaceAppearanceTests.cs`
and by the generator's own `validate()`:

* **1024 source, imported at 512.** Unity imports these at 512 and only a
  1024 source gives an exact 2:1 box downsample; every pattern pitch in the
  generator divides 1024 so nothing restarts mid-unit at the wrap.
* **Per-sheet mean linear luminance and compensation.** URP/Lit multiplies
  `_BaseColor` by `_BaseMap` in linear space. `HomeSurfaceAppearance`
  brightens each builder's flat colour by the sheet's `albedoCompensation`
  so the textured surface keeps the brightness the flat colour used to
  have. The constant is solved per sheet with the **city-facade linear
  rule** — `linear(min(1, ch * c)) * mean == linear(ch)` within 8% for
  every real builder tint channel at or above `0.09`, never clamping any
  channel — NOT the stairwell gamma rule, which would have over-brightened
  the apartment's dark palette by up to 2x. Channels below `0.09` sit in
  the sRGB toe where relative error is meaningless (their absolute linear
  values are thousandths); they are held to the clamp check only.

Because the compensation is solved against the exact `Color` constants the
seven `Home*` builders pass (transcribed into the generator's spec table
and mirrored in the EditMode tint table), editing a builder colour outside
the tested range fails the build here rather than silently shifting the
room's brightness.

## Seamlessness is by construction

Value noise is tiled 3x3 and centre-cropped, every stamp is drawn through
wrapping helpers, convolution pads by real neighbours, and features that
sit on a tile boundary (plank gaps, tile grout) are centred across it. The
edge and seam-ratio checks in `validate()` are regression gates, not the
proof.
