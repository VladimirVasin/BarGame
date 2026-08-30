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

Hero V2 is the production default. All eight gameplay roots, prefab-derived
first-person subsets and the inventory portrait resolve through
`Player3DVariant.ProductionV2`. Hero V1 remains packaged as an explicit
fallback and is not deleted.

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
  `1,984` triangles, with the same `31` bones, six sockets and `37` bone-only
  production actions.
- One curved head surface uses a `4 x 4` face atlas. `Neutral`, `HalfBlink`,
  `ClosedBlink`, `Watchful` and `Tense` remain readable without separate 3D
  features. Neutral is weary, flat and predominantly depressive, never guilty,
  tearful or theatrical; the smaller cranium preserves extra vertical room for
  the existing nose, mouth, jaw and chin identity.

## Production model and prefab

- Editable source: `ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend`.
- Deterministic generator: `tools/build-player-3d-model-v2.py`.
- Unity source data:
  `Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx`, its JSON manifest, the
  separate `Assets/Player3D/V2/Animations/PlayerCharacter3DV2Animations.fbx`
  and the face/clothing atlases under `Assets/Player3D/V2/Textures`.
- Runtime source of truth: one
  `Assets/Resources/Player/Player3DV2.prefab`, loaded through the default
  `Player3DResources`/`PlayerFactory` route in all eight gameplay roots.
- Canonical height is `1.75 m`. The production bind pose is an A-pose; Unity
  imports a Generic rig, preserves the hierarchy, disables root motion and
  keeps the Animator free of gameplay-owned transitions and events.
- The generated asset contains 34 independent mesh parts, 1,984 triangles and
  a 31-bone armature, including six non-deforming sockets. At minimum, these 16
  anatomical parts remain
  independently addressable through `Player3DAssetRegistry`:
  `Head`, `Neck`, `Torso`, `Pelvis`, left/right upper arm, forearm, hand,
  thigh, shin and foot.
- Geometry owns the body-changing hair, jacket, sleeve, trouser, boot and flush
  bandage silhouettes. Face states, jacket construction, patch, bandage wraps,
  cuffs and boot details are atlas pixels. Meshes use unique source datablocks
  and deterministic rigid bone weights; Unity reuses shared PS1-lit materials
  and merge-safe `MaterialPropertyBlock` texture transforms.
- The registry serializes mesh-to-bone bindings, the 16 anatomical bindings,
  animation bindings, source metrics and head/chest/pelvis/feet/grip/mouth
  anchors. Runtime code does not reconstruct the production hierarchy with
  name searches.

## Retained Hero V1 fallback

- `ArtSource/Player`, `Assets/Player3D/{Models,Animations}`, the original
  `Assets/Resources/Player/Player3D.prefab` and `Player3DPortrait.png` remain
  intact. `Player3DVariant.ProductionV1` is the only runtime path that selects it.
- V1 keeps its 73-part burgundy overshirt, diagonal strap and bone-driven face
  solely for rollback and legacy contract checks. Ordinary gameplay,
  first-person subsets and inventory never select it after the V2 promotion.

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
  `Face_Tense` preserve deterministic facial timing. Hero V2 resolves authored
  clip keys through its five-cell atlas; the retained V1 resolves the same
  states through registered face bones.
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
  fixed, and the exit returns to the exact `Relaxed` seam.
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
  `Assets/Resources/Player/Player3DV2Portrait.png`; UI uses its full UV rectangle
  rather than cropping a directional sprite atlas.
- World meshes cast and receive ordinary URP lighting/shadows. The grounded
  analytic `PlayerContactShadow` remains as a stable foot-contact cue and
  remains expanded/offset during both authored and physics fall phases. No
  sprite shadow proxy is part of the active player runtime.

## Source and rebuild

- Rebuild production through Blender with `tools/build-player-3d-model-v2.py`;
  it owns the V2 anatomy, silhouette, atlas, garment and compatibility
  validators and reuses the shared action/bed checks from
  `tools/build-player-3d-model.py`. Together the validators
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
