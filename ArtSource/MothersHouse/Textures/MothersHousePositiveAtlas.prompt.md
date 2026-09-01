# MothersHousePositiveAtlas generation record

- Generated: 2026-09-01
- Mode: built-in ImageGen
- Runtime output: `Assets/Resources/MothersHouse/Textures/MothersHousePositiveAtlas.png`
- Delivery: `1254 x 1254`, sRGB, one 4 by 4 atlas

The atlas is unique to the mother's-house room surfaces. The exact Kettle Hat
NPC prefab is intentionally outside this atlas and keeps its original meshes,
materials and texture, because the table kettle must be literally the same
model as the NPC's head.

## Exact prompt used

```text
Use case: stylized-concept
Asset type: 4 by 4 tileable game texture atlas for a low-poly PS1-style Unity interior
Primary request: create one completely original, clean, bright and positive material atlas for an elderly mother's cozy mountain home; sixteen equal square swatches arranged in an exact 4 columns by 4 rows grid
Style/medium: hand-painted low-poly game albedo textures, restrained PS1-era detail, clean surfaces, gentle handmade character, no photographed grime
Composition/framing: perfectly front-facing orthographic flat material swatches; exact edge-to-edge 4x4 square grid; every cell is one independent seamless tile; no gutters, borders, labels, objects, perspective, highlights or cast shadows
Lighting/mood: albedo-only neutral illumination, bright cozy daytime readability, warm and calm rather than gloomy
Color palette and cell order, left to right:
row 1: warm ivory clean lime plaster; pale cream clean ceiling plaster; light honey-oak plank floor; warm medium maple furniture wood
row 2: soft sage-green fine woven upholstery; pale apricot clean linen; cream woven rug with muted cornflower blue, sage and terracotta geometric pattern; clean light oatmeal sandstone
row 3: ivory glazed ceramic with tiny restrained cornflower-blue floral flecks; soft muted warm brass/painted metal; cool clear blue window glass with only very subtle clean frost; orange-yellow ember and flame color field
row 4: light blue book cloth; clean golden wicker weave; off-white tea cloth; pale warm wood variation
Materials/textures: subtle tactile grain only; well cared for and clean; very low dirt and wear; no soot, mold, damp stains, scratches, cracks, dark grunge, sepia wash or muddy brown overlay
Constraints: every swatch must tile seamlessly within its own square; exact uniform 4x4 grid; no text; no symbols; no logos; no watermark; no room scene; no furniture silhouettes; no lighting baked into albedo
```

## Runtime cell mapping

The top three visual rows map to the twelve authored room sheets:

| Visual row | Column 1 | Column 2 | Column 3 | Column 4 |
| --- | --- | --- | --- | --- |
| 1 | `Wallpaper` | `CeilingPlaster` | `PlankFloor` | `DarkWood` |
| 2 | `Upholstery` | `BedLinen` | `Rug` | `Concrete` |
| 3 | `Ceramic` | `PaintedMetal` | `Glass` | `Fire` |
| 4 | reserved | reserved | reserved | reserved |

Unity addresses rows from the bottom. The importer therefore reverses the
visual row index and applies a two-pixel inset; the PNG itself is not reordered.
