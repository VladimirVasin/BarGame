# Player art specification

## Current production lock

- Silhouette: lean, weary adult man; messy near-black hair; heavy work boots.
- Clothing: faded dark-burgundy overshirt over a charcoal shirt, desaturated
  navy trousers and black footwear.
- Persistent physical asymmetry:
  - pale bandage on the character's **left** forearm;
  - muted ochre patch on the **right** shoulder;
  - dark diagonal satchel strap across the torso and back.
- Mood: restrained low-poly PS1 survival horror, with readable dark outlines
  and value separation against gray-green City fog and the warm Bar interior.
- Do not mirror the character. Blender anatomical left is `+X`, the character
  faces `-Y` in source space, and imported `.L/.R` names remain physical sides.

## Production model and prefab

- Editable source: `ArtSource/Player/Blender/PlayerCharacter3D.blend`.
- Deterministic generator: `tools/build-player-3d-model.py`.
- Unity source data:
  `Assets/Player3D/Models/PlayerCharacter3D.fbx`, its JSON manifest and the
  separate `Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx`.
- Runtime source of truth: one
  `Assets/Resources/Player/Player3D.prefab`, loaded through
  `Player3DResources` in City, Bar, Supermarket, Home and Stairwell.
- Canonical height is `1.75 m`. The production bind pose is an A-pose; Unity
  imports a Generic rig, preserves the hierarchy, disables root motion and
  keeps the Animator free of gameplay-owned transitions and events.
- The generated asset currently contains 73 independent mesh objects and a
  31-bone armature, including six non-deforming sockets. At minimum, these 16
  anatomical parts remain
  independently addressable through `Player3DAssetRegistry`:
  `Head`, `Neck`, `Torso`, `Pelvis`, left/right upper arm, forearm, hand,
  thigh, shin and foot.
- Hair, face pieces, clothing, bandage wraps, shoulder patch, strap, pockets,
  cuffs and soles remain separate mesh objects. Meshes use unique source
  datablocks and deterministic rigid bone weights; Unity reuses one shared
  URP/Lit material and applies the palette with `MaterialPropertyBlock`.
- The registry serializes mesh-to-bone bindings, the 16 anatomical bindings,
  animation bindings, source metrics and head/chest/pelvis/feet/grip/mouth
  anchors. Runtime code does not reconstruct the production hierarchy with
  name searches.

## Animation contract

- `Relaxed`, the four-second `Idle` and the one-second `Walk` own ordinary
  in-place presentation. Idle returns to the exact Relaxed seam while two
  asymmetric breath/weight-shift phrases move the pelvis, spine, chest, head,
  arms and softly loaded knees. Walk uses contact/down/passing/up phases for
  both sides with independent elbow, knee and ankle articulation, opposite arm
  swing and a closed neutral-root loop. Both locomotion clips and the three bed
  clips use auto-clamped Bezier interpolation; the remaining contextual and
  fall timing stays linear. The bed clips additionally stagger their keys, so
  the pelvis and legs take a landmark first and the chest, head, arms and face
  reach it a few frames later. Both endpoints still key the whole rig.
- `Face_Neutral`, `Face_HalfBlink`, `Face_ClosedBlink`, `Face_Watchful` and
  `Face_Tense` preserve deterministic facial timing on registered face bones.
- Left and right balance failures use `FallLeft/Right`, `DownLeft/Right` and
  `RiseLeft/Right`. Negative status direction selects Left; positive selects
  Right. The Fall clip supplies the directional lead-in, the current pose then
  transfers to the runtime ragdoll for impact/down, and a short kinematic blend
  reaches the exact side-down first Rise sample before authored recovery. Each
  physical side owns a distinct full-body, `50`-source-frame (`1.67 s`)
  `Rise` action: the hero braces and rolls prone, holds on both hands and knees,
  steps one lead foot under the body, passes through a low crouch and settles
  into the exact `Relaxed` seam. Every landmark authors the complete body pose,
  so no limb can fall back to a Generic bind/A/T-like pose between keys;
  neither side is produced by runtime mirroring. These are samples inside the
  existing `Rising` phase, not new gameplay states. The physical player root
  remains upright and fixed throughout.
- Bed uses a `3.75 s` `BedEnter` and a `6.0 s` `BedExit` around the persistent
  `BedSleepLoop`. The hero sits on the long edge nearest the apartment door,
  swings both legs onto the mattress and lowers through a supported side pose
  with his head toward the pillow. Waking is a sit-up rather than a roll and
  has four separate beats: he curls onto his elbows and pushes up into a
  half-crouch on the mattress with both boots drawn under him, drops the right
  leg over the near edge, then the left, and only then stands. The right leg
  goes first because it is the one nearest the door-side edge; the left is
  held up on the bedding until the right boot is down. Because runtime pins these clips by the pelvis bone and grounds
  nothing while they play, the generator measures how far his back, the back of
  his lifted head and his seated weight hang below that bone, and
  `PlayerCharacterDimensions` mirrors those three numbers. The mattress is the
  surface he rests on and the pillow's top is built at his head, rather than
  the clip being asked to dodge bedding placed independently of it. Balcony smoking uses `SmokeEnter`, `SmokeLoop`, `SmokeExit`: the
  right hand retrieves a socket-bound cigarette, brings its mouth end to the
  lips for a held inhale, lowers for an outward exhale and discards it before
  returning to `Relaxed`. Cat feeding uses `CatFeedEnter`, `CatFeedLoop`,
  `CatFeedExit`.
- Every production action is bone-only and in-place. Gameplay owns normalized
  clip sampling, pelvis alignment and terminal holds; root motion and Animation
  Events remain disabled. The ragdoll is a procedural runtime phase rather than
  an Action; its ownership flag prevents the manual PlayableGraph and additive
  late pose from writing the same bones until recovery.
- Intoxication sway, arm spread, knee bend and signed balance lean are additive
  bone presentation over ordinary locomotion and reset to neutral through the
  shared lifecycle cleanup.
- Runtime blends Idle and Walk from actual planar speed with damped `0.14 s`
  start and `0.20 s` stop envelopes. Walk cadence follows that visible blend,
  so releasing movement cannot abruptly slow a still-visible gait.

## Derived player representations

- Bar drinking and refrigerator reach use camera-local arm subsets instantiated
  from the same production prefab. `Player3DFirstPersonSubset` enables only the
  registered side's upper-arm, forearm, hand, clothing/detail meshes and grip
  socket, disables unused renderers/colliders/lights and never creates a second
  hero design.
- The inventory portrait is the dedicated transparent render
  `Assets/Resources/Player/Player3DPortrait.png`; UI uses its full UV rectangle
  rather than cropping a directional sprite atlas.
- World meshes cast and receive ordinary URP lighting/shadows. The grounded
  analytic `PlayerContactShadow` remains as a stable foot-contact cue and
  remains expanded/offset during both authored and physics fall phases. No
  sprite shadow proxy is part of the active player runtime.

## Source and rebuild

- Rebuild through Blender with `tools/build-player-3d-model.py`; its validators
  own exact height, outward winding, unique mesh data, weights, triangle budget,
  required parts/bones/sockets/actions, no root motion, signature asymmetry and
  the bed loop's head-to-foot, face-up and closed-eye orientation. Bed support
  validation additionally measures the supine, head and seated offsets against
  the real posed meshes, refuses eased-curve drift on the three bed clips, and
  proves that nothing breaks the mattress plane through the sleep loop or the
  stretches either side of it, that the seated landmark plants both boots
  without hovering, and that the head-side hand reaches the bed while the
  torso lowers. Fall
  validation also owns full-body `Down`/`Rise` seams, the two-key all-fours
  hold, grounded hand/knee/foot contacts, every exported Rise frame's visible
  floor boundary and the exact final `Relaxed` pose.
- Direct `.blend` and GLB imports are not production paths. The deterministic
  FBX, animation FBX and JSON manifest are the Unity inputs; the generated
  transparent portrait is a separate Resources asset.
- The locked 2D design source remains
  `ArtSource/Player/PlayerDirectionalTurntable.png` for visual lineage and
  regression reference.

## Retired 2D player contract

The former active hero used
`PlayerDirectionalAtlas`, `PlayerDirectionalPartsAtlas`,
`PlayerDirectionalBodyExpressionsAtlas`, 16 detailed fall atlases and separate
bed/smoking/cat-feeding atlases. Their historical cell order, no-mirror rules,
foot pivots and deterministic builders remain in repository history and source
art, but they are not the production player presentation. The stairwell-cat
sprites are unaffected by the hero migration; the former bar-NPC and minigame
sprites were later removed together with those systems.
