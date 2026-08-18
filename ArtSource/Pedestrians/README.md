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

The staged source is deliberately separate from that registered five-design
library:

- `Blender/PipebackRoller3D.blend`: **Pipeback Roller**
  (`pipeback_roller_v1`), a self-propelled wheelchair user in dark burgundy
  clothes. Two large drive wheels, small front casters and raised hand levers
  support an asymmetrical fan of tarnished organ pipes behind the backrest;
  bellows under the seat and pipe shutters make the chair, rather than the
  rider's disability, the bizarre element;
- `Blender/LakeFisherman3D.blend`: **Lake Fisherman** (`lake_fisherman_v1`),
  a hooded man in a municipal-yellow oilskin standing at the head of the
  boat-station pier, tipped out over its end board. A stiff peaked hood owns
  the top of the envelope over a storm yoke; the coat hem stops at mid-thigh
  because it rides the pelvis and could not follow a leading knee. Both hand
  props are hard geometry rather than togglable ones — he has exactly one role
  and never puts either down. The pipe leaves `SOCKET_Mouth`, bends down and stands its bowl
  back up in front of the beard; `ACC_PipeEmber` is a named contract, since the
  runtime finds it to light the coal. The rod is bound rigidly to `hand.R`, so the
  left hand is brought onto the same axis by the pose instead — and those six
  arm angles were *fitted* against `ACC_RodGrip`/`ACC_RodTip` by coordinate
  descent rather than set by eye, because a hand a couple of centimetres off
  the stick is exactly what reads as a man pretending to fish. The line that
  falls from the tip is struck at runtime to the waterline the lake plan
  measured;
- `Blender/ParkChessPlayer3D.blend`: **Park Chess Player**
  (`park_chess_player_v1`), an old man perched on a plank of the Central Park
  chess set with his elbows on the board and his head in both hands. The chess
  reference runs on two independent channels, because colour alone is not
  allowed to carry a read: a king's tulle worn where a hat would be — band,
  tapering body, collar, knop and a crooked cross that sets the canonical
  `1.75 m` — and a check on the scarf tails and both lapels, drawn as separate
  light squares standing proud of the dark cloth for the same reason the table
  board is 64 boxes. Nothing on him is white: the light square is the park's
  cold bone at `0.615`, kept there deliberately because he sits under the one
  burning lamp, which is exactly where the fisherman's slicker clipped. The
  coat stops just below the hips — a stiff skirt rides the pelvis and cannot
  follow a knee, and this design spends its life with both knees folded. He is
  the first design to declare `perch_seat_height_m`, and both his loops are
  reviewed in the seated stance rather than in the bind A-pose;
- `Blender/CityPedestrianLocomotion.blend` also owns its two staged Actions.
  `PipebackIdle` keeps the head level over a slow body breath that pumps the
  bellows under the pipe load; `PipebackRoll` is an in-place two-handed lever
  push, release and recovery with a forward body lean and swaying pipe load.

Both staged clips carry the same exact 31 keyed Generic bones as the production
library and no auxiliary animation curves. The non-deforming
`PIVOT_Wheel.L/R`, `PIVOT_Caster.L/R`, `PIVOT_Bellows` and `PIVOT_PipeBank`
transforms are passive anchors for a future displacement-driven presentation
layer; today the frame stays root-bound while bellows and pipes follow the
authored pelvis/chest motion. The model has an adjacent deterministic review
PNG and the shared locomotion contact sheet includes its staged row.

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
and two opposite locomotion phases, with one row per authored design:
Lampshade, Chair Carrier, Kettle Hat, Long-Arm, Helmet Lamp, then the staged
designs, ending with the Park Chess Player. The sheet grows a row automatically when an authored design is
added; appearing here does not register a staged design with the runtime pool.

Two designs are previewed posed rather than in the bind A-pose, because their
whole content is a posture: the Pipeback Roller, who would otherwise stand
through his own chair, and the Park Chess Player, who would otherwise be a man
staring at nothing with his arms out. A perched design is additionally set down
onto the review floor, since in the world it is a bench that carries him.

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

The Pipeback Roller takes a separate staged output branch:

- `Assets/Pedestrians/Staged/Models/PipebackRoller3D.fbx` and `.json`;
- `Assets/Pedestrians/Staged/Prefabs/PipebackRoller3D.prefab`.

That prefab is intentionally outside every `Resources` directory. It reuses
the one shared `Player3DLit` material and is passive: no collider,
`Rigidbody`, runtime pedestrian component, light, audio or interaction. Its
ordinary `CityPedestrianAssetRegistry` binds the Animator, clips and body
anchors; `CityWheelchairNpcAssetRegistry` is passive metadata for the six
mechanism anchors. It declares no `Sit` clip. The generator appends the two
staged loops to the shared animation-only `CityPedestrianLocomotion.fbx`, but
importing them does not add a sixth entry to `CityPedestrianResources`.

The staged Lake Fisherman takes the same separate branch:

- `Assets/Pedestrians/Staged/Models/LakeFisherman3D.fbx` and `.json`;
- `Assets/Pedestrians/Staged/Prefabs/LakeFisherman3D.prefab`, bound into
  `Assets/Resources/City/LakeFishermanProvider.asset`.

So does the staged Park Chess Player, into
`Assets/Resources/City/ParkChessPlayerProvider.asset`. His manifest carries one
field no other design has, `perch_seat_height_m`. A bench sitter is neither
sole-grounded — his hips are higher than his boots — nor cabin-seated, since a
park bench has no roof to measure headroom against, so what the build proves
instead is the distance from the underside of his hips to his soles: it has to
equal the height of the plank the chess recipe draws (`0.540 m`). The validator
also reports which part actually reaches the ground, because a seated design
has two candidate feet and they are not interchangeable — if the tucked foot
outreaches the planted one the pose reads as a man balanced on one toe.

That prefab is passive like every other staged one — no collider, light, audio
or interaction — which is exactly why its burning pipe and its fishing line are
built by `LakeFishermanFactory` at runtime instead of being authored into the
art. It carries one extra passive component, `LakeFishermanRigAnchors`, holding
two anchors the prefab build measures off the imported meshes in the bind pose:
the top of the pipe bowl, parented to `head`, and the point of the rod,
parented to `hand.R`. Both are drawn by rigidly skinned vertices and so have no
Transform of their own; reconstructing them at runtime would mean re-deriving
the FBX axis conversion and the prefab's own `180°` model flip in gameplay
code, twice, and again whenever the art moves.

Its grounding proof is wheel-specific. Both drive tyres must meet the ground
without penetration, their centres and radii must stay stable, both feet must
remain on the footplates and both hands must meet the raised push levers during
the roll cycle. The clips must also close exactly and keep zero gameplay-root
travel.
This contract must not be weakened to the production walker's lowest-sole bake:
the rider is already seated and the chair, not either shoe, establishes ground
contact.

The same run writes `Assets/Pedestrians/Animations/CityPedestrianLocomotion.fbx`
with one looping, in-place `Idle` and `Walk` per registered or staged design,
plus one `Sit` for each design that declares a Route 01 ride. Every clip keys only the exact 31 Generic bones. The
staged model separately exposes six passive mechanism anchors for later
procedural wheel/caster motion. The validator checks closed loop endpoints and
zero gameplay-root translation, then applies the owning design's footwear,
seated, airborne or wheel-contact proof rather than pretending one grounding
rule fits all four.

`FishermanLean` carries a timing contract. It is keyed on an exact
quarter-loop breath grid — rest at every quarter of the lap, full inhale at
every eighth between them — so `frac(normalized * 4)` is the breath phase and
`0.5` is the top of the draw. `LakeFishermanPresentation.BreathsPerLoop`
mirrors that number, and the pipe's ember, its light and its plume are all
read off it. Re-timing the clip without re-timing that constant detaches the
smoke from the chest.

That breath is keyed on the spine chain alone, and so is the one rod
correction in the lap. Both clavicles hang off the chest, so anything authored
there swings both arms and the rod as one piece and the two-handed grip
survives; the same motion authored per-arm, or on the clavicles, opens his
hands off the stick once every eight seconds.

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

The staged Pipeback prefab is not ready to join that list. Production use is
deferred until the pedestrian graph can exclude stairs and prove curb/turn
clearance, the actor owns a wheelchair footprint rather than the ordinary
`0.35 m` capsule, runtime presentation derives drive-wheel rotation and caster
steering from travelled motion, and Route 01 has an explicit accessible
boarding/securement design. The existing pelvis-to-seat passenger transfer is
not a substitute for transporting a rider who remains in their chair.
