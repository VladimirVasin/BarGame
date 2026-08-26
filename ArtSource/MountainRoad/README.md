# Mountain Road surface albedos

Source of record for the six sheets the mountain road prints for itself, in
`Assets/Resources/Textures/MountainRoad*.png`, and for the nine sheets it
borrows from families that already ship. Regenerate with:

```
python tools/build-mountain-road-textures.py
python tools/build-mountain-road-textures.py --verify   # validate, write nothing
```

Pillow is the only dependency. The build is deterministic: same script, same
bytes, same SHA256s in `mountain-road-textures.json`.

## What is printed

Six grammars the City families do not cover. Each is a seamless 1024x1024 RGB
source that Unity imports at 512, Repeat, sRGB, mipmapped, bilinear, aniso 4.

| Sheet | Reads as | Metres per tile |
| --- | --- | --- |
| `MountainRoadAsphaltAlbedo` | cold frost-damaged blacktop | `3.5` |
| `MountainRoadForestFloorAlbedo` | damp humus, needle litter, scree | `5.0` |
| `MountainRoadSnowAlbedo` | wind-packed snow with carried grit | `5.0` |
| `MountainRoadStoneAlbedo` | coarse bedded mountain rock | `6.0` |
| `MountainRoadNeedleAlbedo` | dark uneven conifer needle mass | `2.5` |
| `MountainRoadBarkAlbedo` | ridge-and-furrow bark and deadwood | `2.5` |

The asphalt is deliberately non-directional. The road climbs through ten
hairpins, so wheel bands or a travel direction would run across the
carriageway half the time; the sheet carries aggregate, frost map-cracking,
cut repairs and washed grit, and nothing that points anywhere.

The forest floor and the wind snow each cover the whole `76 m` terrain
envelope, so both are contrast-compressed before the measured normalization —
structure that reads well on a prop is noise across a hillside.

## What is borrowed, and why that is not the same as sharing a file

The bridge, the cableway and the cafe are made of concrete, rusted iron,
painted metal, masonry, linoleum, timber and wall paint. Those sheets already
exist, so the mountain road reads them rather than printing near-duplicates:

| Kind | Source sheet |
| --- | --- |
| `Concrete` | `CityFringeConcreteAlbedo` |
| `RustedIron` | `CityRiverIronAlbedo` |
| `PaintedMetal`, `PaleEnamel` | `CityParkPaintedMetalAlbedo` |
| `Masonry` | `CityFringeMasonryAlbedo` |
| `Linoleum` | `SupermarketLinoleumAlbedo` |
| `Timber` | `CityParkTimberAlbedo` |
| `WallPaint`, `InteriorPaint` | `SupermarketWallPaintAlbedo` |

What a borrowed kind does **not** inherit is the source family's
compensation. Compensation is the constant a builder's flat colour is
brightened by so that multiplying it against a mean-controlled sheet leaves
the surface as bright as the flat colour was; it is fitted to the TINTS, not
to the PNG. The same masonry that serves a city retaining wall at
`0.335, 0.350, 0.325` has to serve a cafe's brick gable at
`0.290, 0.105, 0.065`, and one constant cannot hold both. So this tool
measures the borrowed PNG, re-solves the constant against the mountain's own
tints, and fails the build if the result would clamp a channel or shift
brightness by more than `8%`.

Two kinds may therefore name one file and still differ: `PaintedMetal` and
`PaleEnamel` read the park's painted metal at opposite ends of its tint
range, as do `WallPaint` and `InteriorPaint`.

The borrowed entries record the source sheet's SHA256, so regenerating a City
or Supermarket family is caught here rather than silently shifting a mountain
surface.

## The numbers that are contracts, not taste

For every builder tint channel at or above `0.09`, the solved compensation
must satisfy `linear(min(1, ch * c)) * mean == linear(ch)` within `8%`, and
must never drive a channel past one. Channels below that floor sit in the
sRGB toe where relative error is meaningless and are held to the clamp check
only. Every sheet's mean linear luminance is normalized to its target within
`0.02`, its outer lines must not diverge more than `2.5x` its strongest
interior transition, and it must span its declared contrast floor so it
survives the `640x360` composite.

`Assets/Tests/EditMode/MountainRoadSurfaceAppearanceTests.cs` cross-checks
the C# recipe constants against this manifest, re-measures the PNGs, and then
walks the whole built area to prove every ordinary opaque surface carries one
of the fifteen sheets.

## Seamlessness is by construction

Noise is periodic (a 3x3-tiled lattice, centre crop), every stamp goes
through a wrapping helper, and the two grammars with structure that crosses
the whole sheet — the stone's bedding partings and the bark's furrows — use
a wander summed from whole-number harmonics of the sheet width, so the line
meets itself across the repeat instead of arriving somewhere else. Bed and
plate widths are randomly partitioned rather than evenly pitched: at
`640x360` an even pitch reads as corduroy long before it reads as rock or
bark.

## Generated alongside

- `mountain-road-textures.json` — the measured contract: mean linear
  luminance, compensation, metre pitch, surface response, edge and seam
  metrics, every builder tint, and a SHA256 per sheet.
- `mountain-road-contact-sheet.png` — the six printed sheets tiled two by two
  beside their grayscale reading.
