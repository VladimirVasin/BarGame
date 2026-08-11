# CityBus3D source

`CityBus3D.blend` is the editable production source for the Road v2 midibus.
Rebuild the Blender file, Unity FBX, manifest and review render from the project
root with Blender 5:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' `
  --background --factory-startup `
  --python tools\build-city-bus-3d-model.py
```

The source uses metres, Z-up and forward `-Y`. Unity preserves the FBX
hierarchy and the prefab setup rotates its `Model` child so runtime forward is
local `+Z`. Door, wheel-roll and front-steering pivots are intentionally empty
objects; do not apply or collapse their hierarchy. The production prefab is
collider-free because the runtime bus actor owns its simple collision volumes.
