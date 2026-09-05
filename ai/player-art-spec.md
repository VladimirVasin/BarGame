# Player art specification

## Current production lock

- Silhouette: lean, weary adult man; messy near-black hair; heavy work boots.
- Clothing: faded, unfastened dark olive-drab field jacket with long sleeves
  over a charcoal shirt, desaturated navy trousers and dark military boots.
- Persistent physical asymmetry:
  - pale bandage over the character's **left** jacket forearm;
  - muted ochre patch on the **right** shoulder;
  - no diagonal strap, buckle or copied military insignia.
- Mood: restrained low-poly PS1 survival horror, with readable dark outlines
  and value separation against gray-green City fog and the warm Bar interior.
- Do not mirror the character. Blender anatomical left is `+X`, the character
  faces `-Y` in source space, and imported `.L/.R` names remain physical sides.

## Production Hero V2

Hero V2 is the sole packaged production player. All nine gameplay roots,
prefab-derived first-person subsets and the inventory portrait resolve through
the no-variant `Player3DResources` / `PlayerFactory` path to `Player3DV2`.

- The same `1.75 m` lean adult is `7.4946` heads tall. His head is
  `0.2335 x 0.176 m`, the shoulder joints span `0.41814 m` (`2.3758` head
  widths), and `0.04543 m` of neck remains visible between a `0.148 m` base
  and `0.127 m` top. The `0.543 m` torso broadens subtly from a `0.166 m`
  half-waist to a `0.187 m` half-chest without becoming athletic.
- Relaxed wrists sit at `0.838-0.845 m` and fingertips at `0.742-0.750 m`.
  The pelvis joint span is `0.1845 m`; each leg reads as a `0.1645 m` upper
  thigh, `0.1072 m` knee, `0.1330 m` calf and `0.0813 m` ankle. The painted
  military boots end at `0.263/0.267 m`, so they no longer dominate the leg.
- Clothing is a faded, unfastened dark olive-drab field jacket with long
  sleeves over the charcoal shirt, desaturated navy trousers and dark military
  boots. There is no satchel strap or buckle. The left-forearm bandage lies
  over the sleeve and the right-shoulder ochre repair patch remains; neither is
  mirrored. No insignia, text or literal film-costume marking assigns the hero
  a military history.
- Geometry owns only silhouette: body, hair, jacket, sleeve segments, trousers,
  boots and one flush bandage shell. A full-colour `256 x 256` point-filtered
  clothing atlas paints the open jacket edges, pockets, seams, patch, bandage
  wraps, cuffs and boot construction. The result has `34` mesh parts and
  `2,384` triangles, with the same `31` bones, six sockets and `41` bone-only
  production actions.
- One curved head surface uses a `4 x 4` face atlas with nine cells. The five
  sober faces `Neutral`, `HalfBlink`, `ClosedBlink`, `Watchful` and `Tense`
  (Unity cells `c0r3`, `c1r3`, `c2r3`, `c0r2`, `c1r2`) remain readable without
  separate 3D features, and the drink's four sit beside them: `Drowsy` (`c2r2`,
  lids a pixel high, brows down, the mouth let go), `Glazed` (`c3r2`, one lid
  lower, the pupils drifted apart), `Slack` (`c0r1`, one brow up and one down,
  a dark slit of open mouth) and `Grimace` (`c1r1`, brows knitted, the corners
  of the mouth pulled down). Python draws rows from the top, so a manifest
  row is `3 - r`. Neutral is weary, flat and predominantly depressive, never
  guilty, tearful or theatrical; the smaller cranium preserves extra vertical
  room for the existing nose, mouth, jaw and chin identity.

## Production model and prefab

- Editable source: `ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend`.
- Deterministic generator: `tools/build-player-3d-model-v2.py`.
- Unity source data:
  `Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx`, its JSON manifest, the
  separate `Assets/Player3D/V2/Animations/PlayerCharacter3DV2Animations.fbx`
  and the face/clothing atlases under `Assets/Player3D/V2/Textures`.
- Runtime source of truth: one
  `Assets/Resources/Player/Player3DV2.prefab`, loaded through the default
  `Player3DResources`/`PlayerFactory` route in all nine gameplay roots.
- Canonical height is `1.75 m`. The production bind pose is an A-pose; Unity
  imports a Generic rig, preserves the hierarchy, disables root motion and
  keeps the Animator free of gameplay-owned transitions and events.
- The generated asset contains 34 independent mesh parts, 2,384 triangles and
  a 31-bone armature, including six non-deforming sockets. At minimum, these 16
  anatomical parts remain
  independently addressable through `Player3DAssetRegistry`:
  `Head`, `Neck`, `Torso`, `Pelvis`, left/right upper arm, forearm, hand,
  thigh, shin and foot.
- Geometry owns the body-changing hair, jacket, sleeve, trouser, boot and flush
  bandage silhouettes. Face states, jacket construction, patch, bandage wraps,
  cuffs and boot details are atlas pixels. Meshes use unique source datablocks
  and deterministic bone weights. The continuous `GEO_Torso` shirt and
  `CLO_JacketBody` shell have horizontal rings over three regions: pelvis,
  lower spine and chest, with smooth transitions using at most two adjacent
  bone influences per vertex. Their registered `chest` binding is the gameplay
  anchor, not their only skin influence. Other parts keep rigid weights.
  Unity reuses shared PS1-lit materials and merge-safe `MaterialPropertyBlock`
  texture transforms.
- The registry serializes mesh-to-bone bindings, the 16 anatomical bindings,
  animation bindings, source metrics and head/chest/pelvis/feet/grip/mouth
  anchors, including the explicit lower `Spine` anchor. Runtime code does not
  reconstruct the production hierarchy with
  name searches.

## Animation contract

- All `41` existing actions are regenerated with the independent
  `pelvis -> spine -> chest` tracks on the preserved 31-bone hierarchy. Their
  timings, sockets, hand/foot contacts and contextual seams remain the same;
  the newly weighted shirt and jacket now visibly follow each spinal region.
  The ordinary additive torso bend is shared `40/60` between spine and chest
  and both local poses participate in the same capture/restore lifecycle.
- The independent pedestrian locomotion bank remains at `37` actions; the
  production hero's `Run` is not added to it.
- `Relaxed`, the four-second `Idle`, the one-second `Walk` and the `0.75 s`
  (`18` frames at `24 fps`) `Run` own ordinary in-place presentation. Idle
  returns to the exact Relaxed seam while two
  asymmetric breath/weight-shift phrases move the pelvis, spine, chest, head,
  arms and softly loaded knees. Walk uses contact/down/passing/up phases for
  both sides with independent elbow, knee and ankle articulation, opposite arm
  swing and a closed neutral-root loop. Run is a separate heavy, weary gait:
  forward torso load, stronger opposing arm swing, deeper knee lift and a
  short two-foot flight phase, never an accelerated Walk. All three locomotion
  clips and the three bed clips use auto-clamped Bezier interpolation; the
  remaining contextual and
  fall timing stays linear. The bed clips additionally stagger their keys, so
  the pelvis and legs take a landmark first and the chest, head, arms and face
  reach it a few frames later. Both endpoints still key the whole rig.
- `Face_Neutral`, `Face_HalfBlink`, `Face_ClosedBlink`, `Face_Watchful` and
  `Face_Tense` preserve deterministic facial timing. Hero V2 resolves authored
  clip keys through its atlas. The fall's clips (`Fall`, `Down`, `Rise`) no longer own
  the face at all: the presentation reads the moment — the fight for balance,
  the floor, the stir, the crawl — and the level, and draws it, under the
  ragdoll too.
- The Rise keys obey one rule the generator now enforces frame by frame for
  Hero V2: every shin's `armature_direction` runs KNEE TO ANKLE so the knee
  bends toward the character's front (`cross(thigh, shin).x >= 0`), no knee or
  elbow opens more than `8°` past straight or folds past `130°`, and no visible
  vertex of the lie or the rise passes more than `2 cm` under the neutral
  floor. Foot turns between two keys are quarter turns, never half turns.
- Left and right balance failures use `RiseLeft/Right`; `FallLeft/Right` and
  `DownLeft/Right` remain authored and registered but the 3D hero no longer
  plays them — the balance model's own topple (the lunge, the arms out for the
  ground, the pelvis on the pendulum's arc) is the lead-in, and the runtime
  ragdoll takes the bones from that pose with the topple's motion. The SIDE of
  the rise is chosen where he lies (the lower shoulder), not where he fell;
  negative selects Left, positive Right. Each physical side owns a distinct
  full-body, `50`-source-frame (`1.67 s`) `Rise` action: the hero braces and
  rolls prone, holds on both hands and knees, steps one lead foot under the
  body, passes through a low crouch and settles into the exact `Relaxed` seam.
  Every landmark authors the complete body pose, so no limb can fall back to a
  Generic bind/A/T-like pose between keys; neither side is produced by runtime
  mirroring. At runtime the clip is not played at its authored rate:
  `PlayerRiseModel` scrubs it stage by stage (`0 → 0.10` while he stirs,
  `0.10 → 0.38` pushing up, with slumps that run it back, `0.38 → 0.64`
  kneeling, `0.64 → 1` standing), and the late layer draws the hands on the
  probed floor, one hand on the knee, the lead boot on its step and the head's
  lift on top of it. The physical player root is brought under the lying body
  and turned to match it before he stirs, and stays upright.
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
  the clip being asked to dodge bedding placed independently of it. Both are
  deformable grids at runtime: they dent under his weight by each part's
  actual penetration, the sleeping hip target descends by
  `HomeInteriorWorldBuilder.BedSleeperSinkDepth` so he lies in that dent, and
  the dent slowly refills after he rises. The bedside seat takes no dent —
  it is pinned by both boots on the floor. Balcony smoking uses `SmokeEnter`, `SmokeLoop`, `SmokeExit`: the
  right hand retrieves a socket-bound cigarette, brings its mouth end to the
  lips for a held inhale, lowers for an outward exhale and discards it before
  returning to `Relaxed`. Cat feeding uses `CatFeedEnter`, `CatFeedLoop`,
  `CatFeedExit`. Ordinary location doors use the planted
  `DoorUseEnter`, `DoorUseLoop`, `DoorUseExit` trio: the chest inclines toward
  the door while the physical right hand makes one short press, both feet stay
  fixed, and the exit returns to the exact `Relaxed` seam. After an exterior
  scene handoff, the destination door's same authored axis is reversed before
  camera initialization: the relaxed hero starts with his back to the leaf and
  the chase camera behind his shoulder.
- Every production action is bone-only and in-place. Gameplay owns normalized
  clip sampling, pelvis alignment and terminal holds; root motion and Animation
  Events remain disabled. The ragdoll is a procedural runtime phase rather than
  an Action; its ownership flag prevents the manual PlayableGraph and additive
  late pose from writing the same bones until the body stirs, when the frozen
  lying pose is blended into the clip while the late pass draws the rise's
  limbs. The ragdoll has `14` physical bodies: an `8 kg` lower-spine segment
  and `10 kg` chest replace the former single `18 kg` torso body. Separate
  bounded joints and fitted adjacent boxes keep the lower back articulated
  through the physics handoff and recovery.
- Intoxication sway and arm spread are additive bone presentation over ordinary
  locomotion, on the game clock, and reset to neutral through the shared
  lifecycle cleanup. The old symmetric knee bend is gone: heavy knees come from
  a lowered pelvis while both boots are held to the ground by the late leg
  layer, so they bend anatomically and asymmetrically on uneven ground. The
  balance model's lean, arm reaction, crouch, recovery steps and wall hand land
  on the same layer additively. The arms swing in the ACTOR's frame, never the
  bone's: abduction is a turn about the actor's planar forward through the
  shoulder, the raise to the front a turn about the actor's right, and the
  raise goes on FIRST so the hands keep their forward reach at any spread
  (raised after the abduction it degenerates into a roll of the upper arm
  once the arm is out wide). Measured on
  the V2 rig, no local axis of `upper_arm.L/R` is the abduction axis, and a
  turn about local forward sent both hands backward into the ribs — the drunk
  hugged himself instead of balancing. Blind drunk he now holds them out
  tightrope fashion (`40°` times the SQUARE of the status level, so a light
  buzz barely shows; up to `45°` more from the model's reaction; `0.3` of that
  as a forward raise; the arm away from the lean higher by `0.8°` per degree
  of roll; a `±6°` forward/back hunt from the ambient stagger; clamped to
  `0..85°` so no arm rises above the shoulder line); the arm reaching for a
  wall gives its spread back as the reach takes hold, both arms fade in with
  the layer's `0.2 s` blend after a clip, and every term is exactly zero
  sober. Every ordinary clip keeps its
  authored feet and the layer only corrects them to the probed surface (heel
  and toe rays under each boot, each surface smoothed relative to the actor
  root so the body's own descent is never rate-limited, the pelvis led by the
  walkable ground under the capsule — the hidden ramp on a flight of stairs,
  the lower boot only where no such ground is found — and dipped further for a
  boot left out of its leg's reach, the clip's own lift preserved relative to
  the other boot, run flight released with the Run weight).
- Runtime blends Idle, Walk and Run from actual constrained planar speed with
  damped `0.14 s` start and `0.20 s` stop envelopes. The Run blend begins above
  the `2.6 m/s` walk ceiling and reaches full weight at `4.2 m/s`; collision,
  walkable-area clamping and intoxication therefore affect the visible gait
  before it is sampled. During the short flight phase ordinary grounding
  progressively releases its downward correction with Run weight; at full Run
  it may lift penetrated soles but must not pull both raised boots to the floor.
  Walk and Run cadence follow their visible weights, so releasing Shift cannot
  abruptly slow a still-visible gait.

## Derived player representations

- Refrigerator reach uses a camera-local arm subset instantiated from the same
  production prefab. `Player3DFirstPersonSubset` enables only the registered
  side's upper-arm, forearm, hand, clothing/detail meshes and grip socket,
  disables unused renderers/colliders/lights and never creates a second hero
  design. Bar drinking keeps the seated world body and uses three nested
  full-body actions on that rig: `BarDrinkPickupEnter` (`2 s`),
  `BarDrinkSipLoop` (`3 s`) and `BarDrinkReturnExit` (`2 s`). The vessel follows
  the world hands, and completion returns control to the owning seated loop.
- The inventory portrait is the dedicated transparent render
  `Assets/Resources/Player/Player3DV2Portrait.png`; UI uses its full UV rectangle
  rather than cropping a directional sprite atlas.
- World meshes cast and receive ordinary URP lighting/shadows. The grounded
  analytic `PlayerContactShadow` remains as a stable foot-contact cue and
  remains expanded/offset during both authored and physics fall phases. No
  sprite shadow proxy is part of the active player runtime.

## Source and rebuild

- Rebuild production through Blender with `tools/build-player-3d-model-v2.py`;
  it owns the V2 anatomy, silhouette, atlas, garment and compatibility
  validators and imports shared rig, action, export and bed checks from
  `tools/player_3d_model_common.py`. Together the validators
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
  Door-use validation owns the complete action-family seams, fixed root and
  feet, physical-right grip side, bounded forward reach and subtle chest lean.
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
