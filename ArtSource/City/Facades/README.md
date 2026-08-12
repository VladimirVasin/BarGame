# City facade albedos

Source of record for the eight district wall textures and the shared roof cap
in `Assets/Resources/Textures/`. Regenerate with:

```
python tools/build-city-facade-textures.py
python tools/build-city-facade-textures.py --verify   # validate, write nothing
```

Pillow is the only dependency. The build is deterministic: same script, same
bytes, same SHA256s in `city-facade-textures.json`.

## What is authored

| Sheet | District | Wall |
|---|---|---|
| `CityFacadeOldTownBrickAlbedo` | Old Town | dark brick bond, soot plumes, drip runs, a bricked-up opening |
| `CityFacadeOldTownStoneAlbedo` | Old Town | plaster blown off the brick shell, the ghost of a removed sign |
| `CityFacadeResidentialCoolAlbedo` | Residential | cold painted concrete, panel seams, streaks under every window |
| `CityFacadeResidentialWarmAlbedo` | Residential | the same block repainted, one repaired panel, rust at the fixings |
| `CityFacadeIndustrialSteelAlbedo` | Industrial | corrugated sheet, soot on the horizontals, rust only at the joints |
| `CityFacadeIndustrialRustAlbedo` | Industrial | utilitarian brick, boarded openings, a marking painted flat over |
| `CityFacadeNightlifeMagentaAlbedo` | Nightlife | old shell under a commercial layer, dead sign mounts, bills in layers |
| `CityFacadeNightlifeCyanAlbedo` | Nightlife | the service side of the same shell, dirtier, no commercial layer |
| `CityRoofAlbedo` | shared | felt strips, ponding, gravel |

Two per district because the art bible rules out a street of identical blocks
outright, and one sheet cannot carry both of a district's material axes — brick
and render, cool and warm paint, sheet and masonry, shopfront and service side.

## The cell grid

Each facade sheet is `1024x1024` holding **4 bays across by 4 floors up**, so
one cell is `256x256`. The runtime does not tile these by metres. It tiles them
by the building's own window grid, so one authored cell always covers exactly
one real pane bay and one real `2.35 m` storey, and the baked window band, sill
and grime run land on the geometry on every lot. `CityFacadeAppearance` solves
the scale and phase; `CityFacadeGrid` owns the pitch both it and the window
builder read.

Because a whole-cell shift preserves that alignment exactly, the runtime also
rotates each lot's bays and floors, giving 16 presentations per sheet before
per-lot colour and size are counted. Every cell therefore carries an aperture —
only its state varies (open, curtained, boarded, bricked up, shrunk, postered),
since any cell can end up hosting a real pane on some building.

## Two numbers that are contracts, not taste

**`1024`, not the `1254` the other world albedos use.** Unity imports these at
`512`, and only `1024` gives an exact 2:1 box downsample; `1254` is a 2.449:1
resample that softens precisely the band and mullion edges the sheet exists to
place. `1024 / 4` also keeps the cell grid pixel-exact.

**Mean linear luminance `0.35`.** URP multiplies `_BaseColor` by `_BaseMap`
after converting both to linear, and the runtime brightens the night facade
tint by `1 / 0.62` so a textured wall keeps the brightness the flat colour had.
Preserving that needs `linear(tint * compensation) * mean == linear(tint)`,
which solves to `0.35` and holds within 4.5% across every lot kind. Note this
is *not* `1 / compensation`, the rule the stairwell surfaces use — that rule
assumes a gamma-space multiply and would have said `0.64`, making every facade
in the city almost twice as bright as it is now.

The brightening factor itself is set by the brightest channel any lot can
reach, a bar's red at `0.616`. Anything larger clamps and crushes the hue.

## Generated alongside

- `city-facade-textures.json` — the measured contract: mean linear luminance,
  compensation, cell counts, edge and seam metrics, SHA256 per sheet.
  `Assets/Tests/EditMode/CityFacadeAppearanceTests.cs` cross-checks the C#
  constants against it and re-measures the PNGs.
- `city-facade-contact-sheet.png` — every sheet tiled 2x2 beside its grayscale
  reading, which is the check the art bible asks every district difference to
  survive.

## Known limitation

A tiling facade cannot carry a plinth. The sheet repeats vertically with an
arbitrary phase per building, so there is no cell that is reliably the ground
floor, and the art bible's heavier, darker base is not expressible here. The
grime runs darken the lower part of every floor cell instead, which reads
similarly at street distance. A dedicated plinth box would be the real answer
and is not in this change.
