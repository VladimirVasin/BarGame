# Project overview

## Current

- Product name: **Барный Променад** (Bar Promenade).
- Engine: Unity `6000.5.5f1`.
- Rendering: Universal Render Pipeline package `17.5.0` with a PC-targeted
  PS1 composite after URP post-processing.
- Input: Input System `1.19.0`; keyboard, mouse and gamepad are supported
  across movement, interaction and modal interfaces.
- Build scenes: `Assets/Scenes/City.unity`,
  `Assets/Scenes/DoorTransition.unity` and
  `Assets/Scenes/BarInterior.unity`.
- Runtime assembly: `BarPromenade.Runtime`.
- Test assemblies: `BarPromenade.EditModeTests` and
  `BarPromenade.PlayModeTests`.

## Implemented MVP

A runtime-composed 3D city in which a sprite-based player walks along roads,
approaches interactive bars, enters a separate interior, and returns to the
same place.

The vertical slice contains:

- a finite, seed-reproducible connected city;
- a fixed atmospheric noir night with `0.070` exponential-squared luminous
  gray-green fog, a fog-matched terminal camera backdrop and a City-only
  `48 m` camera visibility cap, plus lifted geometry values, cold moonlight
  and a retuned
  Bloom/ColorAdjustments/Vignette/FilmGrain profile;
- a default `640x360` PS1 world composite with four-tap footprint averaging,
  exact 2x/3x scaling at 720p/1080p, a 35% perceptual-space RGB555 blend
  without a screen-space dither grid, and point upscaling; lower `426x240`
  and `320x180` presets remain available;
- a crisp retro IMGUI layer after the world composite: prompts, HUD and city
  map use a logical `640x360` canvas, while the information-dense cocktail
  interface keeps responsive sizing;
- shared 8-sided cylinder geometry, hard directional shadows and disabled
  camera MSAA for a deliberate low-poly silhouette;
- one player-following `CityFogField`, capped at 36 more visible slowly
  drifting particles, plus depth-tested soft halos around lamps, bar lights
  and active signals;
- deterministic collider-free street lamps with shadowless spot-light pools
  and slow out-of-phase amber traffic signals generated from the road graph;
- scene-local looping music: `city_theme` loads only from
  `Resources/Audio/CityMusic` in `City`, while `bar_theme` loads only from
  `Resources/Audio/BarMusic` in `BarInterior`; both receive a mild low-pass
  treatment and each player is destroyed by the next Single-mode scene load;
- deterministic generated mono retro SFX at `22050 Hz`, including a separate
  door latch and sustained hinge creak, distinct beer-pong
  throw/bounce/rim/sink and tincture swap/match/moonshine cues, with bounded
  category pools, per-effect cooldowns and voice limits, plus separate
  scene-local procedural city and bar ambience;
- a spanning-tree road graph with deterministic loops;
- deterministic collider-free ochre guard rails, batched into two meshes,
  that trace only the exposed boundary of the road union, close dead ends and
  leave a `3.30 m` opening around every generated bar approach;
- 16 building lots by default, including exactly 4 reachable bars; stable
  row-major order assigns cocktail mixing, beer pong, Split the G and
  Tinctures in a Row;
- diegetic bar identification through warm windows, framed entrances and
  shared camera-facing pixel mug signs;
- one nine-layer billboard puppet with a body plus upper/lower segments for
  both arms and legs, eight unique 64x96 directional views, 5-degree sector
  hysteresis and contralateral joint-driven walking projected into screen
  space for side views and depth for front/back views; atlas-derived foot
  contacts keep the lower stance foot pinned while the upper body compresses
  at each footfall instead of lifting the whole puppet; readable procedural
  breathing, weight shift and an alternating left/right arm gesture keep the
  same rig alive while idle without lifting the feet;
- one camera-independent realtime player shadow that mirrors all nine
  articulated puppet parts in the authored view relative to the main light,
  faces them toward that light and reproduces live gait, compression and
  whole-puppet sway in both City and BarInterior, plus one small
  light-independent analytic contact patch fixed to the grounded player root;
  neither changes the nine visible renderers;
- one deterministic five-state body-expression atlas that swaps the existing
  body sprite for stronger half/closed blinks plus watchful and tense idle
  expressions in the five visible-face directions without adding a tenth
  renderer or inventing faces in rear views;
- camera-relative road-constrained movement with a `5.2 m/s` maximum,
  `6.5 m/s²` acceleration and `11 m/s²` braking; ordinary release coasts,
  hard modal/transition/teleport stops remain immediate, constrained
  displacement cannot store hidden momentum, and the last actual movement
  heading is preserved while idle;
- a very close freely orbiting perspective third-person chase camera with
  `2.6 m / 53°` exterior and `2.2 m / 57°` interior framing, deliberately
  weighty yaw/focus damping, bounded focus lag, teleport snapping, subtle
  deterministic idle/walk motion and smoothly recovering obstacle-aware
  distance; cinematic motion fades out while a modal interface owns input;
- a full-screen city map with player/bar markers, persistent green completed
  visits, ordered route editing and deterministic shortest paths constrained
  to the generated road graph;
- localized interaction prompts from RU/EN JSON catalogs;
- guarded asynchronous transitions and persistent seed/bar/route/visited
  context for the current city;
- a dedicated `3.15 s` `DoorTransition` scene between the city and bar:
  an unscaled fixed-camera handle/door sequence opens the leaf outward toward
  the camera against a solid black doorway while the destination preloads,
  then activates only after the final blackout;
- one generated shared bar-interior scene whose furniture and interaction
  station adapt to the active bar activity, plus one exit;
- one explicit `BarMinigameCatalog` whose ordered definitions and factories
  create both normal and debug minigame instances; cocktail mixing, beer pong
  Split the G and Tinctures in a Row are registered now, and future
  registrations appear in the debug list;
- an `F9` minigame debug window in both `City` and `BarInterior`; opening it
  closes a conflicting map or minigame before taking the modal lock, while
  launched debug instances neither complete bar visits nor persist
  intoxication, drinks or the `Wasted` effect;
- a same-scene modal cocktail minigame at the counter: exactly three served
  cocktails unless intoxication reaches 100, each built from one of four bases
  and 2–4 unique additions chosen from a deterministic seven-item shelf; its
  accepted final result marks the active bar as visited;
- explicit cocktail compatibility and scoring up to 100 per round/300 total,
  with a 15-point penalty for each incompatible addition;
- a real 4x4 pixel-art ingredient atlas, animated pouring/filling/serving
  feedback, three-stage progress and a final rank;
- a same-scene 2D beer-pong minigame at the second bar with six sprite cups,
  ten aimed throws, deterministic 120 Hz 2.5D ball physics, real table/rim
  bounces, clean/bank scoring and an early-clear bonus;
- a dedicated pixel-art beer-pong backdrop and 4x4 gameplay atlas for the
  ball, shadow, throwing hand, cups, hit reactions and opponent silhouettes;
- a same-scene Split the G timing minigame at the third bar: hold Space, LMB or
  gamepad South for one irreversible sip, wait for the foam to settle, and
  compare the frame-rate-independent remaining level with the center of the G;
- three fresh dark-beer attempts with immediate proportional drinking
  persistence, five accuracy bands, a session-best score, an early Continue
  option and automatic completion after the third glass;
- a dedicated point-filtered `640x360` Split the G backdrop, transparent 4x4
  pint/hand/foam/effect atlas and generated retro gulp cue;
- a same-scene fourth-bar Tinctures in a Row minigame with a `7x7` board, five
  visually distinct infusion flavors, 15 accepted swaps and at most one
  `XXX` moonshine shot;
- deterministic seeded board generation without starting matches and with at
  least three legal normal swaps, frame-independent match/cascade resolution,
  invalid-swap rollback and automatic deterministic reshuffling when no
  normal move remains;
- long runs and T/L intersections create `XXX` only when none is present;
  swapping `XXX` with a normal flavor clears every shot of that flavor, while
  ordinary matches remain customer orders and do not count as drinking;
- mouse click/drag, keyboard and gamepad controls, a point-filtered `640x360`
  backdrop, transparent 4x4 shot/effect atlas, interpolated swap/gravity/refill
  motion, RU/EN interface and generated swap, match and moonshine-burst cues;
- session-persistent intoxication, last-alcohol context and consumed-drink
  count; every beer-pong miss consumes a light beer, each Split the G attempt
  records the actual dark-beer fraction, only an activated `XXX` in Tinctures
  in a Row immediately consumes `Moonshine` for 24 intoxication, and the
  cocktail and Tinctures in a Row paths defer their timed `Wasted` debuff
  until the modal closes.

## Deferred

- Infinite streaming world and floating origin.
- Dynamic day/night, weather, rain, puddles and volumetric light shafts.
- Vehicle or skating physics.
- Multiple bespoke bar interiors.
- Mobile renderer parity for the PS1 composite; the current presentation
  feature targets the PC renderer.
- Full multi-frame eight-direction player animation; the current vertical
  prototype uses one authored view per direction plus runtime joint walking,
  procedural living-idle motion and body-sprite blink variants.
- Minimap, in-world GPS trail, route autopilot, and manual map zoom/pan.
- Sobering mechanics, long-term save data, economy, dialogue, quests, combat,
  save slots, and online features.
- Final bespoke art and audio masters, accessibility, localization coverage,
  and platform release work.
- Split the G Easy/Hard profiles, persistent best scores and streaks.

South City Rollers/Skaters is a design reference only for procedural-world and sprite-character approaches; its code and assets are not present in this repository.
