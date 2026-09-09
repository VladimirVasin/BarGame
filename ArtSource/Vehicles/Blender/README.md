# Vehicle sources

`LastRouteCoin3D.blend` contains the Ferryman's unchanged octagonal coin and
the glovebox pile made from it. Rebuild through the pinned toolchain:

```powershell
python tools/run-blender.py tools/build-last-route-coin-3d-model.py --expect Assets/Resources/Vehicles/LastRouteCoin3D.fbx --expect Assets/Resources/Vehicles/LastRouteCoin3D.json
```

The two bare mesh assets bake their scale and axes to Unity metres. The pile
is hinge-relative, stays behind the closed lid and clear of the bulb; it has
no animation or physics. Runtime shares the existing coin material and tint.

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
