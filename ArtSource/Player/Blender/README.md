# Modular 3D player authoring source

`tools/build-player-3d-model.py` generates a standalone low-poly Blender model
from the locked player design. The generated FBX and manifest are the source
inputs for the Unity `Player3D` import pipeline; the `.blend` remains the
editable authoring source.

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
  --portrait Assets\Resources\Player\Player3DPortrait.png `
  --manifest Assets\Player3D\Models\PlayerCharacter3D.json `
  --fbx Assets\Player3D\Models\PlayerCharacter3D.fbx `
  --animation-fbx Assets\Player3D\Animations\PlayerCharacter3DAnimations.fbx
```

The script has no third-party Python dependencies. It was verified with
Blender `5.0.1` and avoids UI-only APIs so it can run in the background.

Useful optional arguments:

- `--pose apose|relaxed` selects the bind pose. `apose` is the production
  default; `relaxed` is retained only for compatibility previews;
- `--height 1.75` scales geometry and armature together;
- `--seed 7301` controls only millimetre-scale asymmetric hair variation;
- `--glb path.glb` exports only the character hierarchy;
- `--fbx path.fbx` exports the character hierarchy with no animation;
- `--animation-fbx path.fbx` exports the armature and every generated Action,
  but no meshes;
- `--preview path.png` renders the non-export presentation scene;
- `--portrait path.png` renders a deterministic `192x256` transparent
  head-and-upper-torso inventory portrait in the `Relaxed` Action;
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

The production rest pose is a symmetric A-pose. `Relaxed` is an Action, so the
same FBX bind skeleton can drive ordinary locomotion, first-person limb clones
and every contextual animation. Six non-deforming attachment bones survive
FBX export: `SOCKET_Grip.L`, `SOCKET_Grip.R`, `SOCKET_Cigarette.R`,
`SOCKET_Bottle.R`, `SOCKET_Vessel.L` and `SOCKET_Mouth`.

## Generated Action library

All Actions animate bones only, keep the `root` bone fixed and store exact
duration/loop/source-rate metadata as `bp_*` custom properties. The production
library contains:

- locomotion: `Relaxed`, `Idle`, `Walk`;
- face: `Face_Neutral`, `Face_HalfBlink`, `Face_ClosedBlink`,
  `Face_Watchful`, `Face_Tense`;
- falls: `FallLeft/Right`, `DownLeft/Right`, `RiseLeft/Right`;
- bed: `BedEnter`, `BedSleepLoop`, `BedExit`;
- smoking: `SmokeEnter`, `SmokeLoop`, `SmokeExit`;
- cat feeding: `CatFeedEnter`, `CatFeedLoop`, `CatFeedExit`;
- bus riding: `BusBoardEnter`, `BusRideLoop`, `BusAlightExit`;
- park game tables: `ChessSeatEnter`, `ChessSeatPlayLoop`,
  `ChessSeatExit`.

`Idle` is a four-second loop with an exact `Relaxed` seam. Two asymmetric
breaths shift weight through the pelvis and legs while the spine, chest, head,
upper arms and forearms counter-move. `Walk` is a one-second eight-phase gait:
each side passes through contact, down, passing and up poses with independent
elbow, knee and ankle articulation and opposite arm swing. These two
locomotion Actions alone use auto-clamped Bezier curves; contextual, facial
and fall Actions plus `Relaxed` retain their authored linear timing.

With the Home bed dock facing the room, the bed's source-space headboard
direction is `-X` and the door-side long edge is `-Y`. `BedEnter` first seats
the hero on that long edge, then swings both legs onto the mattress and lowers
through an arm-supported side pose; `BedSleepLoop` keeps the head toward `-X`,
the face upward and both eyes closed. `BedExit` wakes and rolls the hero,
pushes the chest up while the legs leave the mattress, holds a grounded seated
pose on the edge, leans weight over the planted feet and only then stands. The
same Actions drive ordinary bed use and the slower opening wake.

`SmokeEnter` settles the stance, reaches to the jacket, draws the cigarette,
raises the right hand to the mouth, cups the first light with the left hand,
inhales and lowers both hands. `SmokeLoop` holds an exact low-hand rest over
source frames `0-3`, lifts to a mouth-contact inhale over frames `10-14`,
breathes through the chest, lowers into an exhale and returns to the exact rest
over frames `21-23` for a seamless loop. `SmokeExit` inspects the cigarette,
extends it away from the body, flicks it, follows it with the gaze and returns
to `Relaxed`. The cigarette runs from the right socket head along its local
`+Y` head-to-tail axis; the mouth-contact pose aligns that axis with
`SOCKET_Mouth`.

`BusBoardEnter` starts at the exact `Relaxed` pose, climbs through an in-place
two-step boarding gesture and settles into a forward-facing passenger seat.
`BusRideLoop` is a two-second seated breathing and road-sway loop with an exact
full-rig seam. `BusAlightExit` starts at that identical seated endpoint, rises,
steps down and returns exactly to `Relaxed`. Runtime owns the moving bus-local
action anchor; all three source Actions keep the root bone fixed.

`ChessSeatEnter` is the bus boarding turned through ninety degrees. A
park chess plank has a stone table standing where a seat is normally
backed onto, so the body goes in past the end of the plank instead: it
steps out to anatomical left, swings the leading leg over the timber,
perches on the plank end with the trailing hand braced behind it, brings
both legs in under the slab and slides to the middle of the board. Rig
handedness was measured rather than assumed for these poses — a negative
bone-local `z` on either thigh abducts that leg towards `+X`, and a
negative `y` on the pelvis turns the whole body the same way.
`ChessSeatPlayLoop` is a four-second study over the board: two leans in,
one hand carried out over the men and brought back without touching one,
and a breath, on an exact full-rig seam. `ChessSeatExit` reverses the
entry to `Relaxed`. Both boards wear the same three Actions; only the
men on them differ.

The Actions intentionally contain no gameplay events or root motion. Unity's
deterministic interaction timelines own exact playback, terminal holds,
inventory commits and cancellation.

## Built-in validation

Before saving, the script fails with a non-zero Blender exit code unless:

- every required body object and bone exists exactly once;
- every required face control, socket and Action exists with the expected
  deform/root-motion contract;
- every body part has its own non-empty, closed, outward-wound mesh datablock;
- every mesh is rigidly weighted to its declared bone and retains the matching
  armature modifier;
- feet meet `Z=0`, generated height remains within tolerance and the relaxed
  silhouette has plausible width;
- the model stays below the `4,500`-triangle cap;
- the bandage remains on physical left, the patch on physical right and the
  front strap crosses the torso centre line;
- the sleeping head points toward source `-X`, the feet toward `+X`, the face
  remains upward and both eyes remain closed;
- the smoking loop keeps its exact full-rig seam and fixed root, preserves
  source facing `-Y`, holds the cigarette at least `0.40 m` from the mouth at
  rest, and brings it within `25 mm` with socket-axis alignment above `0.85`
  during the inhale;
- the bus boarding, seated loop and alighting Actions keep the root fixed and
  match across the complete `Relaxed -> seated -> Relaxed` endpoint chain;
- presentation-only objects cannot enter an export selection.

The production model currently builds as 73 independent mesh objects, 31 bones
(including six sockets), 26 Actions and `1,534` triangles. Exact counts are
reported by the script and manifest.
