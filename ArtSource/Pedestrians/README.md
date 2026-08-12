# City pedestrian 3D source

The generated editable sources are:

- `Blender/CityPedestrian3D.blend`: **Lampshade Walker**
  (`lampshade_walker_v1`);
- `Blender/ChairCarrierPedestrian3D.blend`: **Chair Carrier**
  (`chair_carrier_v1`), with an upside-down cafe chair strapped behind the
  shoulders and its four legs forming a cage around the head;
- `Blender/CityPedestrianLocomotion.blend`: the shared animation-only source.

Each model has an adjacent deterministic review PNG.
`Blender/CityPedestrianLocomotionContactSheet.png` shows, left to right, Idle
and two opposite Walk phases; Lampshade is the top row and Chair Carrier the
bottom row.

Rebuild from the repository root with Blender 5:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' `
  --background --python tools/build-city-pedestrian-3d-model.py
```

The generator validates each `1.75 m` grounded silhouette, its lightweight
triangle budget, one source material, rigid weights, non-emissive and
collider-free model exports, no model-local Actions, and the exact 31-bone
Generic names, hierarchy and rest pose of `PlayerCharacter3D`. It writes the
production FBXs and manifests under `Assets/Pedestrians/Models/`.

The same run writes `Assets/Pedestrians/Animations/CityPedestrianLocomotion.fbx`
with four looping, in-place, bone-only clips: `LampshadeIdle`,
`LampshadeWalk`, `ChairCarrierIdle` and `ChairCarrierWalk`. Its validator checks
all 31 keyed bones, closed loop endpoints, zero gameplay-root translation and
footwear grounding at every exported frame. Model manifests name their own two
clips; no animation data is copied into either model FBX.

Unity imports both models and the locomotion library by copying the production
Player Avatar. The generated prefabs at
`Resources/Pedestrians/CityPedestrian3D` and
`Resources/Pedestrians/ChairCarrierPedestrian3D` bind those dedicated clips and
the one shared `Player3DLit` material.
