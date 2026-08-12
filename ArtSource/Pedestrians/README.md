# City pedestrian 3D source

The generated editable sources are:

- `Blender/CityPedestrian3D.blend`: **Lampshade Walker**
  (`lampshade_walker_v1`);
- `Blender/ChairCarrierPedestrian3D.blend`: **Chair Carrier**
  (`chair_carrier_v1`), with an upside-down cafe chair strapped behind the
  shoulders and its four legs forming a cage around the head;
- `Blender/KettleHatPedestrian3D.blend`: **Kettle Hat Walker**
  (`kettle_hat_walker_v1`), a stout short-legged figure whose overhanging
  belly hides the upper legs and whose oversized skewed enamel kettle, spout
  and handle arc own the top of the silhouette while the face stays visible
  under the rim;
- `Blender/LongArmPedestrian3D.blend`: **Long-Arm Walker**
  (`long_arm_walker_v1`), narrow and tall in cold steel blue, with a small
  skull sunk into raised shoulders, eyes almost at the hairline, no mouth, and
  bare pale forearms roughly `3.3x` their bone length hanging to the ankles
  under oversized hands. It is the only design whose strangeness is the body
  itself rather than a worn or carried object;
- `Blender/HelmetLampPedestrian3D.blend`: **Helmet Lamp Hopper**
  (`helmet_lamp_hopper_v1`), a squat miner in ochre work wear with a hi-vis
  band, a battered helmet, a lamp housing wired to a belt battery box, and
  `0.46 m` hind feet. It never takes a step: its clips are a two-footed rabbit
  hop. The Unity prefab hangs the one worn Spot the pedestrian contract allows
  off its head bone;
- `Blender/CityPedestrianLocomotion.blend`: the shared animation-only source.

Every walker keeps the same `1.75 m` envelope and the same `31`-bone rig, so
"short" is authored as proportion rather than scale: the Kettle Hat Walker's
human mass stops near `1.40 m` and the kettle fills the rest. Lowering the
visible torso further would leave the arms swinging around bone pivots they no
longer sit near, and shrinking the rig would require its own collider and
collision parameterisation.

The Long-Arm Walker's forearm hangs almost straight down from the elbow instead
of following the outward A-pose bone axis. Extending it along the bone would
push the rest silhouette past the `1.65 m` width guard, and hanging a long
segment below its own pivot is exactly what makes it swing as a pendulum once
the shoulder rotates. Its hair must never widen past the skull: an overhanging
brim would echo the Lampshade Walker, the one silhouette this design has to
stay clear of.

Each model has an adjacent deterministic review PNG.
`Blender/CityPedestrianLocomotionContactSheet.png` shows, left to right, Idle
and two opposite Walk phases, with one row per archetype in catalog order:
Lampshade, Chair Carrier, Kettle Hat, Long-Arm, then Helmet Lamp. The sheet grows a row
automatically when an archetype is added.

Rebuild from the repository root with Blender 5:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' `
  --background --python tools/build-city-pedestrian-3d-model.py
```

Add `-- --archetype kettle_hat` to iterate on one design without rebuilding the
shared library or the contact sheet.

The generator validates each `1.75 m` grounded silhouette, its lightweight
triangle budget, one source material, rigid weights, non-emissive and
collider-free model exports, no model-local Actions, and the exact 31-bone
Generic names, hierarchy and rest pose of `PlayerCharacter3D`. It writes the
production FBXs and manifests under `Assets/Pedestrians/Models/`.

The same run writes `Assets/Pedestrians/Animations/CityPedestrianLocomotion.fbx`
with fourteen looping, in-place, bone-only clips — an `Idle` and a `Walk` per
registered design, plus one `Sit` for each design that declares a Route 01
ride. Its validator checks all 31 keyed bones, closed loop endpoints, zero
gameplay-root translation and footwear grounding at every exported frame.

A seated clip is the one exception to footwear grounding, and it is declared
rather than assumed. Its feet leave the pavement plane on purpose, so it is
excluded from the pelvis bake and proves a different contract: its measured
headroom above the seated pelvis must fall inside the archetype's
`seated_clearance_m` band, and nothing may hang more than the `0.41 m` cushion
height below that pelvis. The runtime seats every design by aligning the shared
`0.70 m` rest pelvis to the cushion anchor, which is why one seat rule serves
four different proportions and why the per-design work is the authored posture,
not the maths. A design that declares no band — the Helmet Lamp Hopper — owns
no seated clip and stays on the pavement.

Grounding is proved per archetype: each design is rebuilt in its own scene, and
only its own clips are baked and verified against its own footwear. A clip
grounded against another design's boots is not grounded at all, because sole
height, length and deformation all differ. The baked pelvis track is captured
as plain per-frame data and re-keyed onto the shared library, so the exported
clips carry exactly the correction that was proved. Model manifests name their
own two clips; no animation data is copied into any model FBX.

An archetype may additionally declare `hand_clearance_m`, an animated
hand-to-pavement band checked on every frame. Footwear grounding cannot express
it: a design whose hands hang near the ankles will happily push them through
the road while every sole still reports perfect contact. The Long-Arm Walker
declares `0.020-0.140 m`, meaning its hands must never come closer than `20 mm`
to the pavement and must approach within `140 mm` at some point in the cycle,
so the reach is real rather than nominal.

An archetype may instead declare `airborne_lift_m`, which replaces the
every-frame sole contact rule. Its clips are lifted by one constant offset
rather than corrected per frame — a per-frame correction pins the lowest sole
to the road on every sample and silently turns a hop into a shuffle — and they
must never penetrate, must land at least once, and must reach the declared
apex band in at least one clip. The Helmet Lamp Hopper declares
`0.080-0.400 m` and ships a `0.241 m` apex.

Three contracts elsewhere had to be relaxed the same way, by declaration
rather than by blanket exception: the clip importer stops locking root height
for airborne clips (their arc is authored on the pelvis, which this Avatar
treats as the motion node, so locking it stripped the hop outright),
`CityPedestrianPresentation` stops grounding their soles every frame, and the
prefab validator now checks a declared light count instead of forbidding
lights, so an accidental extra Light still fails.

Unity imports every model and the locomotion library by copying the production
Player Avatar. The generated prefabs at
`Resources/Pedestrians/CityPedestrian3D`,
`Resources/Pedestrians/ChairCarrierPedestrian3D`,
`Resources/Pedestrians/KettleHatPedestrian3D`,
`Resources/Pedestrians/LongArmPedestrian3D` and
`Resources/Pedestrians/HelmetLampPedestrian3D` bind those dedicated clips and
the one shared `Player3DLit` material.
