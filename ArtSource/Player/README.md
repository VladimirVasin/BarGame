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

The turntable remains the visual-lineage reference for the production Hero V2.
Its dedicated authoring source and Unity imports can be rebuilt with Blender:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' `
  --background --factory-startup `
  --python tools\build-player-3d-model-v2.py
```

The V2 generator owns the only packaged hero model, its 41-action bank,
inventory portrait, atlases and runtime prefab inputs. Shared action and bed
validation helpers live in `tools/player_3d_model_common.py`, which has no
standalone model entry point.
