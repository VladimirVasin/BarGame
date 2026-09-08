# Alpine cold frost mask provenance

- Asset: `Assets/Resources/Textures/AlpineColdFrostMask.png`.
- Created `2026-09-08` with the built-in `image_gen` tool under the explicit
  user request for more natural ice. This is a source bitmap, not runtime
  image generation or a Blender model.
- Source: `$CODEX_HOME/generated_images/01a08085-7d9c-7411-b903-2b547b9f959b/exec-93677525-02b4-4d6f-9fd0-5ecce6cfa849.png`.
- Published unchanged: `1536 x 1024`, RGB PNG, `1,918,385` bytes.
- SHA-256: `40e499563c770a191be7709016543fc0764f550d56dafcb07d0b0ee6a15de0bf`.
- Import: default texture, linear (`sRGBTexture=false`), Clamp on all axes,
  Bilinear, no mipmaps, uncompressed, max size `2048`, no alpha.
- The shader reads one channel as a numerical mask. The image has small RGB
  differences and some diagonal tips beyond the requested clear rectangle;
  the shader independently enforces the `8.5–12 %` edge/`18 %` corner limit
  and clean `uv[.18,.82]²` centre. The whole bitmap maps to the visible game
  image window, including aspect crop, never the black bars or HUD.
  The bitmap remains unchanged by the later user refinement: runtime reveals
  it with an irregular growth front. A separately filtered footprint joins
  needle-sized gaps into an ice film; quarter-resolution Gaussian passes blur
  the background beneath it, while the original bitmap draws crisp crystals.
  A soft film fringe extends up to `0.03` beyond the crystal reach, still
  inside the `0.18` bound. Large clear gaps and the centre remain sharp.
  Gaussian sigma is `0.045 * FrostAmount` image height; the final blend also
  scales by `FrostAmount`, so radius and strength recede together on thaw.
- Source-size/hash/import-setting checks were performed without Unity.
  Current focused `AlpineFrostDiffusion` passes in `3.761200 s` in
  `TestResults/alpine-frost-diffusion.xml`: same-frame GPU comparisons hold
  crystal drawing fixed, verify increasing blur at 25/50/100%, and keep the
  centre and black 4:3 bars unchanged. Half/full thaw exactly match half
  growth/clear pixels. Current stills and visually inspected A/B are
  `Captures/MothersHouseInterior/diffusion-*.png`.
  The earlier `AlpineFrostJourney` passed in `52.339248 s` in
  `TestResults/mothers-hearth-frost.xml`; its `frost-*.png` captures and
  eight-second thaw video show the previous renderer and verify unchanged
  journey timing and sound behavior.

## Final generation prompt

```text
Use case: photorealistic-natural.
Asset type: 1536 x 1024 landscape grayscale shader opacity mask for subtle ice frost at the four outer edges of a video game view.
Primary request: An orthographic, flat, high-detail photographic macro texture of real fern frost crystals growing naturally inward from ALL FOUR outer image edges on a perfectly pure black background. White and light grey mean actual ice opacity; dark grey is extremely thin translucent rime. The central rectangle spanning the middle 70 percent of the image width AND height must be ENTIRELY PURE BLACK (#000000), with absolutely no marks, haze or gradients inside it.
Materials and form: Extremely fine irregular crystalline dendrites, delicate feathery ice, varied needle widths, fine natural fractal branching, tiny frozen grain and patchy semi-transparent rime. Unequal organic clusters densest in corners, sparse broken clusters along the edges, uneven depth, gaps of pure black. Crystals vary in size and angle, with many shallow lateral wisps along the edges and fine small branches pointing inward. Keep the coating mainly within the outermost 10 to 12 percent of each edge, slightly deeper in corners but never crossing into the clear central 70 percent rectangle. Natural frost photographed on a cold window in the dark, rendered ONLY as an isolated greyscale opacity texture; not an actual window or a scene.
Composition: Flat texture fills the rectangular canvas exactly, no perspective. No separate rectangular outline. Very sparse hair-fine feathered inner tips and soft rime transitions. Natural asymmetry between all edges and corners. Detailed but restrained, not an opaque white wall.
Constraints: grayscale only; pure black empty centre and black between clusters; no reflections, no lens flare, no blue tint, no glass panel border, no text, no watermark, no objects or environment. Avoid snowflake icons, geometric snowflakes, stars, schematic white pine-tree branches, repeated fir silhouettes, vector art, evenly spaced patterns, thick stems, ornamental decorative frames, cartoon ice, hard white rim, opaque solid border. This must look like real microscopic frost crystals, not a drawn pattern.
```
