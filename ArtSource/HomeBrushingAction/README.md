# Home brushing sink and liquid kit

`tools/build-home-brushing-action-3d-model.py` authors all five meshes in Blender:

- `SinkBasin`: fixed `0.85 × 0.20 × 0.35 m` outer envelope, continuous outer wall, rim, inward slope and ceramic bottom. It replaces the visible Home sink basin without changing the original player BoxCollider.
- `SinkDrain`: `46 mm` perforated grate at the genuine bottom. The existing `Home Bathroom Sink Hollow` object retains its registered name but its former broad visual insert is replaced entirely with this small mesh.
- `Droplet`: normalized unit volume, longitudinal axis `+Z`; runtime uses millimetres.
- `Splash`: normalized unit `XY` patch, surface normal `+Z`; runtime uses centimetres.
- `BrushHandle`: fixed `12 mm` diameter and `140 mm` length along local `Y`, centred at zero. At runtime local centre `Y=0.045`, its ends lie at `Y=-0.025` and `Y=0.115`, meeting the existing bristle head without extending beyond it.

The unchanged sink origin is `(2.075, 0.78, 3.425)`. Its real central ceramic floor lies at `(1.995, 0.720, 3.425)`. The drain object origin is `(1.995, 0.724, 3.425)` with its top surface at `Y=0.726`. The empty cavity starts below the `Y=0.880` rim. Incoming liquid must collide with the actual visible triangles, never the solid player collision proxy.

`HomeBrushingResources.Mesh(name)` combines the imported hierarchy's matrices once into shared readable metre-scale meshes. `HomeBrushingResources.Foam` supplies one muted cream PS1 Lit material; it adds no light. The basin retains the Home enamel material and palette.

The deterministic generator reuses the existing toilet authoring module's flat-shaded mesh/export helpers and normalized liquid profile construction. It does not change or re-export any toilet asset. The `.blend` preserves export roots and a separate inspection composition; the PNG is an authoring view, not an in-game lighting reference.

Generate:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' --background --factory-startup --python tools/build-home-brushing-action-3d-model.py
```

Validate the exported contract:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' --background --factory-startup --python tools/build-home-brushing-action-3d-model.py -- --validate-only
```

The focused validator covers deterministic geometry, nondegenerate flat-shaded triangles, the exact fixture envelope, sixteen downward cavity rays, three incoming trajectory clearances, normalized effects, the fixed brush handle and its placed endpoint range, importer Read/Write/unit/axis settings and actual FBX vertex/anchor round trips. Current output: **5 FBX models, 5 meshes, 1,060 triangles**. `--only-model BrushHandle` exports only that FBX while refreshing the complete manifests and Blender source.
