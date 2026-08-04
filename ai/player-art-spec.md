# Player art specification

## Current prototype lock

- Silhouette: lean, weary adult man; messy near-black hair; heavy work boots.
- Clothing: faded dark-burgundy overshirt over a charcoal shirt, desaturated
  navy trousers and black footwear.
- Persistent asymmetry:
  - pale bandage on the character's **left** forearm;
  - muted ochre patch on the **right** shoulder;
  - dark diagonal satchel strap across the torso and back.
- Mood: restrained PS1 survival-horror pixel art with a readable dark outline
  and value separation for gray-green city fog and the warm bar interior.
- Do not mirror frames. Preserve physical left/right details in every view.

## Atlas contracts

- Every bespoke contextual player atlas that replaces the ordinary rig must
  also follow the endpoint, handedness, pivot and no-fade rules in
  `ai/contextual-animation-standard.md`.
- Visual reference:
  `Assets/Resources/Player/PlayerDirectionalAtlas.png`.
  It is `512x96`, with eight `64x96` columns at PPU 48.
- Runtime puppet:
  `Assets/Resources/Player/PlayerDirectionalPartsAtlas.png`.
  It is `512x864`: the same eight columns and nine `64x96` layer rows.
- Body expressions:
  `Assets/Resources/Player/PlayerDirectionalBodyExpressionsAtlas.png`.
  It is `512x480`: the same eight columns and five full-body rows in Unity
  order: `Neutral`, `HalfBlink`, `ClosedBlink`, `Watchful`, `Tense`.
- Detailed balance falls:
  `Assets/Resources/Player/Falls/PlayerDetailedFall*Atlas.png` contains 16
  atlases: all eight `PlayerViewDirection` views times separately authored
  screen-left and screen-right variants. Every atlas is `1280x768`, arranged
  as 10 columns by 8 chronological rows of `128x96` cells at PPU 48. Logical
  frame zero is the top-left cell; frames read left-to-right, top-to-bottom.
  The 80-frame runtime budget is `14` falling, `36` down and `30` rising.
- Layer order:
  `Body`, `LeftUpperArm`, `LeftLowerArm`, `RightUpperArm`,
  `RightLowerArm`, `LeftUpperLeg`, `LeftLowerLeg`, `RightUpperLeg`,
  `RightLowerLeg`.
- Column order:
  `Front`, `FrontRight`, `Right`, `BackRight`,
  `Back`, `BackLeft`, `Left`, `FrontLeft`.
- Every frame has the same scale and a foot pivot 4 pixels above the bottom.
- Import with Point filtering, Clamp wrapping, no mipmaps and no compression.
- Body/limb pixels at rest must composite exactly to the visual reference.
  Arms are parented at shoulder/elbow pivots and legs at hip/knee pivots.
- The neutral expression row must match the puppet `Body` layer exactly.
  Expression variants may change only their explicit opaque eye, lid, brow and
  mouth pixel whitelists. Alpha, silhouette, clothing and asymmetry stay
  unchanged; `BackRight`, `Back` and `BackLeft` must remain byte-identical to
  neutral.
- Every turntable-authored head, face and neck pixel is fully opaque,
  including all four diagonal views. Chroma-key conversion must not infer
  magenta spill from the red channel alone because that removes skin tones.
- Every turntable-authored lower-arm, bandage and hand pixel is fully opaque
  in all eight views; the same skin-aware chroma-key rule applies there.

The current puppet contains one static authored pose per direction and uses
runtime sagittal joint rotation, bob and rock for walking. The sagittal axis is
projected into screen space for side views, depth for front/back views and both
for diagonals; left/right limbs alternate and arms oppose the same-side legs.
Procedural idle adds readable breathing, weight shift and a rare gesture that
alternates between the left and right arms. A deterministic timer swaps the
existing body renderer through stronger half/closed blinks in the five
visible-face directions, plus watchful and tense expressions after sustained
idle. Locomotion, intoxication above `0.35`, an active balance lean or a fall
cancels those two idle-only expressions while ordinary blink timing continues.
Percentage-driven intoxication reuses the same nine-part puppet: procedural
body sway, arm spread and knee bend intensify continuously and the balance
arrow adds signed lean. Failure reuses the existing body renderer for one
authored full-body fall frame, hides the other eight visible layers and keeps
the physical player root upright. The matching directional-light shadow does
the same with its existing body caster. Atlases load and slice lazily, and the
nine-part puppet is restored when recovery completes or presentation is
disabled; no tenth renderer is introduced.
A future frame-animation pass should preserve the column/layer order and pivot
positions while adding consistent idle/walk frames.

## Experimental 3D authoring model

- `tools/build-player-3d-model.py` can construct a standalone low-poly Blender
  interpretation of this same locked design. It is an authoring experiment and
  does not replace or feed the current Unity sprite runtime.
- The canonical visible height remains `1.75 m`; shoulder, elbow, hip and knee
  heights derive from the current front puppet pivots. Blender uses Z-up, the
  character faces `-Y`, and physical left is `+X` so the bandage remains `.L`
  while the ochre shoulder patch remains `.R` without mirroring.
- Head, neck, torso, pelvis, upper/lower limbs, hands and feet are independent
  rigidly weighted mesh objects. Hair, clothes, facial pieces, bandage wraps,
  patch, strap, pockets, cuffs and soles also remain separate. Every granular
  object maps back to one of the existing nine puppet parts through
  semantic `bp_sprite_part` metadata; this is not a per-view atlas import map,
  because the 2D builder retains its stable image-space slots while Blender
  `.L`/`.R` is always anatomical.
- The generated armature, closed/outward-wound mesh checks, unique datablocks,
  weights, exact requested height, triangle budget and signature asymmetry
  validate inside Blender before the `.blend` or optional FBX/GLB is written.
  Authoring instructions live in `ArtSource/Player/Blender/README.md`.

## Source and rebuild

- Locked source: `ArtSource/Player/PlayerDirectionalTurntable.png`.
- Deterministic builder: `python tools/build-player-puppet-atlas.py`.
- Experimental 3D builder: run `tools/build-player-3d-model.py` through
  Blender; it is deterministic for a given height, pose and hair seed but its
  `.blend` bytes are not a locked runtime artifact.
- The builder restores only pixels lost from the head and lower arms, derives
  the nine layers and five body-expression rows, and fails unless the neutral
  composites, exact facial edit whitelists, rear views, alpha and asymmetry
  contracts all hold.
