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
- Visible face pixels are fully opaque. Chroma-key conversion must not infer
  magenta spill from the red channel alone because that removes skin tones.

The current puppet contains one static authored pose per direction and uses
runtime sagittal joint rotation, bob and rock for walking. The sagittal axis is
projected into screen space for side views, depth for front/back views and both
for diagonals; left/right limbs alternate and arms oppose the same-side legs.
Procedural idle adds readable breathing, weight shift and a rare gesture that
alternates between the left and right arms. A deterministic timer swaps the
existing body renderer through stronger half/closed blinks in the five
visible-face directions, plus watchful and tense expressions after sustained
idle. Locomotion or `Wasted` cancels those two idle-only expressions.
A future frame-animation pass should preserve the column/layer order and pivot
positions while adding consistent idle/walk frames.

## Source and rebuild

- Locked source: `ArtSource/Player/PlayerDirectionalTurntable.png`.
- Deterministic builder: `python tools/build-player-puppet-atlas.py`.
- The builder restores only pixels lost from the face, derives the nine layers
  and five body-expression rows, and fails unless the neutral composites,
  exact facial edit whitelists, rear views, alpha and asymmetry contracts all
  hold.
