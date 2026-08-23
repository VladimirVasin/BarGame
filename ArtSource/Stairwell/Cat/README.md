# Stairwell Cat 3D (Cheshire trickster)

The last sprite conversion: the middle-landing rail cat is now a
deterministic low-poly 3D model with no armature — pivot empties only.
The retired 64px atlas pipeline (idle 8x4, feeding 8x2, the
`build-stairwell-cat-atlas*.py` tools and the `*Source*.png` sheets)
is gone; this folder now holds the Blender source of truth.

## Rebuild

```
blender --background --factory-startup --python \
    tools/build-stairwell-cat-3d-model.py
```

Outputs:

- `Blender/StairwellCat3D.blend` — editable source (saved by the
  generator; the generator script is the actual source of truth).
- `Blender/StairwellCat3D.png` — back-quarter review render matching
  the MiddleFlight framing (rail perch, hanging tail).
- `Blender/StairwellCat3D-face.png` — face render: the grin only
  exists from the front, and a review that cannot see the design's
  whole point is no review.
- `Assets/Stairwell/Cat/Models/StairwellCat3D.fbx` + `.json` manifest.

Then the Editor `Bar Promenade/Stairwell Cat 3D/Build Runtime Prefab`
menu (queued automatically on import) builds
`Assets/Stairwell/Cat/Prefabs/StairwellCat.prefab` and binds
`Assets/Resources/Stairwell/StairwellCatProvider.asset`.

## Design contract

- Sitting near-black shaggy cat, ~0.56 m to the ear tips, perched on
  the 0.10 m-deep back rail: haunches settled on the top, toes just
  over the front edge, tail hanging down the camera-side face.
- Articulation pivots, exported flat beside the meshes with every
  pivot-bound mesh's origin ON its pivot (the wheelchair mechanism
  pattern; the runtime actor adopts and articulates them):
  `PIVOT_Chest` (breathing scale), `PIVOT_Head` (tracking and the
  grin turn), `PIVOT_Ear.L/R`, `PIVOT_Tail.01..03`, plus
  `ANCHOR_Muzzle` for feeding props.
- `ACC_Grin` — the Cheshire signature: a crescent of teeth WIDER THAN
  THE HEAD (0.30 m over a 0.17 m head), floating in front of the
  muzzle, on its own `M_StairwellCatGrin` material slot that Unity
  rebinds to `Assets/Resources/Materials/StairwellCatGrin.mat`. The
  mesh bakes normalized arc length into UV x (0 = left tip, 0.5 =
  center, 1 = right tip; `grin_uv_arc: arclength_u_v1`) so the shader
  draws the smile in from the middle outward. The renderer ships
  disabled: by default the grin does not exist.
- Body meshes share the Blender Object Info material and re-bind to
  the one shared `Assets/Player3D/Materials/Player3DLit.mat`;
  per-part color travels through the manifest into
  MaterialPropertyBlocks.
- Triangle budget 400–1600 (currently ~908); every validator lives in
  the generator and fails the build loudly.
