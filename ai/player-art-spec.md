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

- `Relaxed`, `Idle` and `Walk` own ordinary in-place presentation.
- `Face_Neutral`, `Face_HalfBlink`, `Face_ClosedBlink`, `Face_Watchful` and
  `Face_Tense` preserve deterministic facial timing on registered face bones.
- Left and right balance failures use `FallLeft/Right`, `DownLeft/Right` and
  `RiseLeft/Right`. Negative status direction selects Left; positive selects
  Right. The physical player root remains upright while the clip and analytic
  contact patch present the fall.
- Bed uses `BedEnter`, `BedSleepLoop`, `BedExit`; balcony smoking uses
  `SmokeEnter`, `SmokeLoop`, `SmokeExit`; cat feeding uses `CatFeedEnter`,
  `CatFeedLoop`, `CatFeedExit`.
- Every production action is bone-only and in-place. Gameplay owns normalized
  clip sampling, pelvis alignment and terminal holds; root motion and Animation
  Events remain disabled.
- Intoxication sway, arm spread, knee bend and signed balance lean are additive
  bone presentation over ordinary locomotion and reset to neutral through the
  shared lifecycle cleanup.

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
  expands/offsets during falls. No sprite shadow proxy is part of the active
  player runtime.

## Source and rebuild

- Rebuild through Blender with `tools/build-player-3d-model.py`; its validators
  own exact height, outward winding, unique mesh data, weights, triangle budget,
  required parts/bones/sockets/actions, no root motion and signature asymmetry.
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
art, but they are not the production player presentation. NPC, cat and
minigame sprites are unaffected by the hero migration.
