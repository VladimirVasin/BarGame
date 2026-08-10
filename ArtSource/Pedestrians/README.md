# City pedestrian 3D source

`Blender/CityPedestrian3D.blend` is the generated editable source for the first
street pedestrian archetype, the **Lampshade Walker** (`lampshade_walker_v1`).
The adjacent PNG is the deterministic review render.

Rebuild from the repository root with Blender 5:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' `
  --background --python tools/build-city-pedestrian-3d-model.py
```

The generator validates the `1.75 m` grounded silhouette, `800-1200` triangle
budget, one source material, rigid weights, non-emissive/collider-free export,
no local Actions and the exact 31-bone Generic names, hierarchy and rest pose
of `PlayerCharacter3D`. It writes the production FBX and manifest under
`Assets/Pedestrians/Models/`.

Unity imports the model by copying the production Player Avatar. The generated
runtime prefab at `Resources/Pedestrians/CityPedestrian3D` assigns the one
shared `Player3DLit` material and keeps direct references to the Player
animation FBX's looping `Idle` and `Walk` clips; animation data is never copied
into the pedestrian model.
