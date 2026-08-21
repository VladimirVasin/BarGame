# Player turntable source

`PlayerDirectionalTurntable.png` is the locked source image for the current
eight-direction player prototype.

- SHA256:
  `EC51D909A4D950C39C9B2309AAAF3BCC8B19CDE171A6E0EE0F8D5EC31FB3F70F`
- Layout: four views on the first row and four on the second:
  `Front`, `FrontRight`, `Right`, `BackRight`, `Back`, `BackLeft`, `Left`,
  `FrontLeft`.
- Design lock: burgundy overshirt, navy trousers, black boots, left-forearm
  bandage, right-shoulder ochre patch and diagonal strap.

Generate the runtime reference and layered puppet atlases with:

```powershell
python tools/build-player-puppet-atlas.py
```

The builder repairs only head and lower-arm pixels lost by the original
chroma-key pass, then derives body plus eight jointed limb layers and five
body-expression rows: neutral, half blink, closed blink, watchful and tense.
It asserts that all nine neutral layers composite exactly to the corrected
reference frame and that every facial edit stays on its direction-specific
pixel whitelist while all rear views remain unchanged.

The modular production 3D authoring source and Unity import FBXs can be rebuilt
with Blender:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' `
  --background --factory-startup `
  --python tools\build-player-3d-model.py -- `
  --output ArtSource\Player\Blender\PlayerCharacter3D.blend `
  --preview ArtSource\Player\Blender\PlayerCharacter3D.png `
  --portrait Assets\Resources\Player\Player3DPortrait.png `
  --manifest Assets\Player3D\Models\PlayerCharacter3D.json `
  --fbx Assets\Player3D\Models\PlayerCharacter3D.fbx `
  --animation-fbx Assets\Player3D\Animations\PlayerCharacter3DAnimations.fbx
```

It preserves independently rigged anatomical, clothing and signature-detail
meshes, uses an A-pose bind skeleton, and emits 32 bone-only in-place Actions
plus stable hand/mouth prop sockets. See `Blender/README.md` for the hierarchy,
side convention, outputs and built-in validation.
