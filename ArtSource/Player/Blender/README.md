# Experimental modular 3D player

`tools/build-player-3d-model.py` generates a standalone low-poly Blender model
from the locked player design. This is an authoring experiment and is not used
by the Unity runtime.

## Design and coordinate contract

- The model is `1.75 m` from the ground to the canonical hair silhouette.
- Blender uses Z-up; the character faces `-Y`.
- Anatomical left is `+X`. The bandage is always on `.L`; the ochre shoulder
  patch is always on `.R`. The completed character is never mirrored.
- The visual baseline is the lean, weary hero in
  `ArtSource/Player/PlayerDirectionalTurntable.png`: messy near-black hair,
  faded burgundy overshirt, charcoal shirt, desaturated navy trousers, heavy
  dark work boots and a diagonal strap.
- The head-heavy proportions and joint heights deliberately follow the current
  `64x96` puppet pivots instead of a realistic adult anatomy chart.

## Generate

From the repository root in PowerShell:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' `
  --background --factory-startup `
  --python tools\build-player-3d-model.py -- `
  --output ArtSource\Player\Blender\PlayerCharacter3D.blend `
  --preview ArtSource\Player\Blender\PlayerCharacter3D.png `
  --manifest ArtSource\Player\Blender\PlayerCharacter3D.json
```

The script has no third-party Python dependencies. It was verified with
Blender `5.0.1` and avoids UI-only APIs so it can run in the background.

Useful optional arguments:

- `--pose relaxed|apose` selects the canonical resting stance or an animation-
  friendly A-pose;
- `--height 1.75` scales geometry and armature together;
- `--seed 7301` controls only millimetre-scale asymmetric hair variation;
- `--glb path.glb` exports only the character hierarchy;
- `--fbx path.fbx` exports only the character hierarchy;
- `--preview path.png` renders the non-export presentation scene;
- `--manifest path.json` writes object, bone, material, bound and triangle data.

Run `--help` after Blender's `--` separator for the complete CLI.

## Separate-part contract

The generator never joins the model. Core anatomical meshes remain separate:

```text
Head / Neck / Torso / Pelvis
UpperArm.L -> Forearm.L -> Hand.L
UpperArm.R -> Forearm.R -> Hand.R
Thigh.L -> Shin.L -> Foot.L
Thigh.R -> Shin.R -> Foot.R
```

Hair cap and tufts, jacket body/panels/sleeves/cuffs/lapels, facial features,
bandage wraps, right-shoulder patch, strap sections, pockets, jeans cuffs and
boot soles are additional independent objects. Each export mesh owns a unique
mesh datablock, one rigid vertex group and one Armature modifier.

The custom property `bp_sprite_part` maps every granular 3D object back to one
of the current nine Unity puppet groups:

```text
Body
LeftUpperArm / LeftLowerArm
RightUpperArm / RightLowerArm
LeftUpperLeg / LeftLowerLeg
RightUpperLeg / RightLowerLeg
```

This is semantic compatibility metadata, not a direct atlas-import table. The
current directional sprite builder uses stable image-space limb slots in each
view, while Blender `.L`/`.R` always means the character's anatomical side.

The generated `RIG_Player` uses Blender `.L`/`.R` anatomical naming. Preview
camera, lights and ground live under `PRESENTATION_Player`, carry
`bp_export = false` and are excluded from FBX/GLB selection.

## Built-in validation

Before saving, the script fails with a non-zero Blender exit code unless:

- every required body object and bone exists exactly once;
- every body part has its own non-empty, closed, outward-wound mesh datablock;
- every mesh is rigidly weighted to its declared bone and retains the matching
  armature modifier;
- feet meet `Z=0`, generated height remains within tolerance and the relaxed
  silhouette has plausible width;
- the model stays below the `4,500`-triangle cap;
- the bandage remains on physical left, the patch on physical right and the
  front strap crosses the torso centre line;
- presentation-only objects cannot enter an export selection.

The default model currently builds as 73 independent mesh objects and `1,534`
triangles. Exact counts are reported by the script and optional manifest.
