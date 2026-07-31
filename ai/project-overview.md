# Project overview

## Current

- Product name: **Барный Променад** (Bar Promenade).
- Engine: Unity `6000.5.5f1`.
- Rendering: Universal Render Pipeline package `17.5.0` with one PC quality
  level and one PC render-pipeline profile. PC is the current and default
  quality at index `0` for every serialized platform key, has no platform
  exclusions, and applies the PS1 composite after URP post-processing.
- Input: Input System `1.19.0`; keyboard, mouse and gamepad are supported
  across movement, interaction and modal interfaces.
- Build scenes: `Assets/Scenes/MainMenu.unity`,
  `Assets/Scenes/City.unity`,
  `Assets/Scenes/DoorTransition.unity`,
  `Assets/Scenes/BarInterior.unity`,
  `Assets/Scenes/StairwellInterior.unity` and
  `Assets/Scenes/HomeInterior.unity`.
- Runtime assembly: `BarPromenade.Runtime`.
- Test assemblies: `BarPromenade.EditModeTests` and
  `BarPromenade.PlayModeTests`.

## Implemented MVP

A runtime-composed 3D city in which a sprite-based player walks along roads,
approaches interactive bars and their nearby home, enters separate interiors,
and returns to the matching exterior entrance.

The vertical slice contains:

- a black `MainMenu` launch boundary at build index `0` that resets every
  session-owned value, writes the one-shot `OpeningSleep` Home arrival and
  Single-loads the existing `HomeInterior` instead of duplicating the room;
  ordinary Editor Play also enters through `MainMenu` regardless of which
  scene is currently open;
- a cinematic waking opening inside that Home: the hero begins directly in
  the persistent bed-sleep loop while the first rendered Home frame fixes on
  the silent alarm clock at `05:59`; its whole display flickers briefly at
  long intervals while no menu input exists for five seconds. A localized
  PS1-style `ПРОСНУТЬСЯ / WAKE UP` or `ВЫЙТИ / QUIT` menu then appears over
  the same held shot while the silent display keeps flickering `05:59`. Wake
  Up alone switches it to solid `06:00`, starts the alarm and hides the menu.
  After three more unscaled seconds on the clock and sleeping loop, the alarm
  stops; only then does the six-second, three-times-slower opening wake begin,
  gliding to the sleeper over `2.25 s` and easing onward into the active
  gameplay shot without a cut;
- a finite, seed-reproducible connected `12 x 12`-block city spanning roughly
  `288 x 288 m`;
- a fixed atmospheric noir night with `0.070` exponential-squared luminous
  gray-green fog, a fog-matched terminal camera backdrop and a City-only
  `48 m` camera visibility cap, plus lifted geometry values, cold moonlight
  and a retuned
  Bloom/ColorAdjustments/Vignette/FilmGrain profile;
- a default `640x360` PS1 world composite with four-tap footprint averaging,
  exact 2x/3x scaling at 720p/1080p, a 35% perceptual-space RGB555 blend
  without a screen-space dither grid, point upscaling and percentage-driven
  intoxication vignette, ghost/chromatic image, warp, warmth and exposure
  pulse; lower `426x240` and `320x180` presets remain available;
- a crisp retro IMGUI layer after the world composite: prompts, HUD and city
  map use a logical `640x360` canvas, while the information-dense cocktail
  interface keeps responsive sizing;
- shared 8-sided cylinder geometry, one explicitly packaged shared URP/Lit
  material for ordinary runtime primitives, hard directional shadows and
  disabled camera MSAA for a deliberate low-poly silhouette;
- one player-following `CityFogField`, capped at 36 more visible slowly
  drifting particles, plus depth-tested soft halos around lamps, bar lights
  and active signals;
- deterministic collider-free street lamps with geometry batched into
  `48 m` spatial chunks, shadowless spot-light pools and slow out-of-phase
  amber traffic signals generated from the road graph;
- scene-local looping music: `city_theme` loads only from
  `Resources/Audio/CityMusic` in `City`, while `bar_theme` loads only from
  `Resources/Audio/BarMusic` in `BarInterior` and the optional
  `stairwell_theme` slot loads only from `Resources/Audio/StairwellMusic` in
  `StairwellInterior`; all three import as background-streamed clips, receive
  a mild low-pass treatment, route through the shared `Music` mixer group and
  are destroyed by the next Single-mode scene load;
- one shared `BarPromenadeAudio` mixer with `Music`, `Ambience/Beds`,
  `Ambience/Details`, `SFX/World`, `SFX/Gameplay` and dry `UI` groups;
  City, Bar, Stairwell, Home and DoorTransition snapshots keep `-6 dB`
  headroom under a master compressor, feed dedicated reverb/echo returns and
  switch with a short `0.25 s` wet-tail transition outside the immediate
  DoorTransition blackout;
- deterministic generated mono retro SFX at `22050 Hz`, including a separate
  door latch and sustained hinge creak, distinct beer-pong
  throw/bounce/rim/sink and tincture swap/match/moonshine cues, with bounded
  category pools, per-effect cooldowns and voice limits, all routed through
  canonical mixer groups;
- separate scene-local procedural City, Bar, Home and Stairwell ambience beds,
  plus three-source spatial soundscapes in Home and Stairwell. Home combines a
  calm room bed, refrigerator and balcony night air with sparse soft wood,
  radiator, radio and bathroom details; Stairwell combines a concrete room
  bed, ventilation and electrical buzz with rarer pipe knocks, metal stress,
  distant water and movement. Both use deterministic schedules, `22050 Hz`
  mono clips, deliberately quantized retro waveforms and layout-derived
  anchors;
- a spanning-tree road graph with deterministic loops, cross-city arterials
  and a connected park-path cross;
- five readable districts: Old Town, Residential, Industrial, Nightlife and
  a central `4 x 4`-block park with lawn, plaza, trees, benches, hedges and
  four continuously walkable gates;
- rendered streets, park paths, lawn and plaza own matching static colliders,
  so the existing `0.28 m` controller step climbs their real height changes
  instead of letting the puppet intersect raised surfaces;
- deterministic collider-free ochre guard rails, batched into `48 m` spatial
  chunks, that trace only street boundaries, close dead ends and leave clear
  openings around every bar approach and park gate;
- 144 land-use lots by default, including 16 park cells, exactly 4 reachable
  bars in four different urban districts and one non-bar player home beside
  one bar street; every bar pair is at
  least `120 m` apart by traversable graph distance, while stable row-major
  order assigns cocktail mixing, beer pong, Split the G and Tinctures in a
  Row;
- a default `8.8 m` player-home mass with a recognizable third-floor balcony,
  open door and window; the City facade uses the same balcony geometry as the
  Home interior's exterior opening;
- when the opening route first reaches the City, the hero starts on the road
  node beside the deterministic player home and its neighboring bar, `12 m`
  from their shared street approach under default spacing; custom-layout
  fallback placement remains bounded to `48 m` by traversable street distance,
  and returning from either interior restores that entrance's own return point;
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
  whole-puppet sway in City, BarInterior, HomeInterior and StairwellInterior,
  plus one small light-independent analytic contact patch fixed to the
  grounded player root;
  neither changes the nine visible renderers;
- one deterministic five-state body-expression atlas that swaps the existing
  body sprite for stronger half/closed blinks plus watchful and tense idle
  expressions in the five visible-face directions without adding a tenth
  renderer or inventing faces in rear views;
- camera-relative road-constrained movement with a `2.6 m/s` maximum,
  `6.5 m/s²` acceleration and `11 m/s²` braking; ordinary release coasts,
  hard modal/transition/teleport stops remain immediate, constrained
  displacement cannot store hidden momentum, and the last actual movement
  heading is preserved while idle;
- in City and BarInterior, a very close freely orbiting perspective
  third-person chase camera with
  `2.6 m / 53°` exterior and `2.2 m / 57°` interior framing, deliberately
  raised `1.4 m / 1.3 m` focus points that keep the hero in the lower frame,
  weighty yaw/focus damping, bounded focus lag, teleport snapping, subtle
  deterministic idle/walk motion and smoothly recovering obstacle-aware
  distance; cinematic motion fades out for fullscreen modals, while the
  balance-specific lock keeps its intoxication and fall reactions visible;
- one percentage-driven intoxication profile shared by City, BarInterior,
  HomeInterior and StairwellInterior:
  `1–20` Light Buzz / «Лёгкий хмель», `21–40` Tipsy / «Навеселе»,
  `41–60` Drunk / «Подшофе», `61–80` Unsteady / «Шатает» and `81–100`
  Very Drunk / «В стельку»; `0` is Sober and hides the HUD;
- continuously escalating movement slowdown, puppet sway, arm spread, knee
  bend, camera roll and world-image distortion within those ranges, with the
  HUD rendered as five independently filling 20-point segments; presentation
  eases toward a changed level over about `0.7 s`;
- deterministic balance checks only above `60`: a crisp `140°` arc appears
  above the player with a green center sector, moving arrow and red risk
  meter; arrows, A/D, D-pad or left stick counter the arrow, while higher
  intoxication narrows the safe sector, strengthens disturbances and makes
  checks longer and more frequent;
- a failed balance check visually drops the jointed puppet to the arrow
  side, keeps the upright player root stationary, expands and offsets the
  contact shadow, then recovers through `0.45 s` falling, `1.2 s` down and
  `1.0 s` rising states before restoring movement;
- a full-screen city map with district colors and labels, distinct park land
  and paths, player/bar markers, a dedicated labeled home icon, persistent
  green completed visits, ordered route editing and deterministic shortest
  paths constrained to the generated road graph;
- localized interaction prompts from RU/EN JSON catalogs;
- guarded asynchronous transitions and persistent seed/bar/route/visited
  context for the current city, with an explicit bar-or-home return kind, a
  separate stairwell-arrival side and a consumed `Normal`/`OpeningSleep` Home
  arrival value;
- a dedicated `3.15 s` `DoorTransition` scene between connected locations:
  an unscaled fixed-camera handle/door sequence opens the leaf outward toward
  the camera against a solid black doorway while the destination preloads,
  then activates only after the final blackout;
- one deterministic runtime-composed `8.6 x 9.6 x 6.25 m`
  `StairwellInterior` between the exterior home entrance and the apartment:
  the route climbs from a ground-floor lobby through a middle landing to the
  hero's third-floor door, while a fully sealed debris pile makes the next
  upward flight impassable; 48 visible steps use three seam-free walkable ramp
  colliders, radius-safe overlapping navigation corridors keep every
  floor/flight seam traversable by the real `PlayerMotor`, and side-aware
  arrival restores the player beside the correct door;
- a decayed industrial-horror stairwell treatment with stained concrete,
  rusty rails, exposed pipes, ventilation grilles, electrical cabinets,
  radiators, damp damage, trash and dense upper-floor junk; three bounded
  fixed camera shots cut between the lower flight, middle flight and apartment
  landing with height hysteresis; each shot keeps its exposed suspended HDR
  fluorescent tube and halo visible, while three stronger flickering
  practical-light pools, a green desaturated Bloom/vignette/grain profile, at
  most 14 dust particles, a concrete ambience bed, spatial ventilation and
  electrical layers, sparse positional industrial cues, a long dark
  reverb/moderate echo snapshot and the separate optional `stairwell_theme`
  music slot establish the atmosphere;
- one clickable pixel-art cat sits with its back to the camera on the
  `Middle Landing Back Rail`; a camera-plane billboard preserves that
  composition through the stairwell's fixed shots while authored look
  variants keep its head turned toward the player. The point-filtered
  `512x256` `Resources/Stairwell/Cat/StairwellCatAtlas` supplies an `8x4`
  grid for ordinary idle motion and a rare eight-frame grooming sequence
  roughly every 36 seconds, while the shared `IInteractable` path exposes a
  localized temporary text placeholder without blocking movement;
- one deterministic shared `22 x 16 x 4.8 m` bar interior with seven authored
  zones and four validated circulation paths; its long layered counter,
  bottle-backed mirrors, three booths, four high tables, stage, entrance
  dressing and dedicated activity bay are composed at runtime from one
  validated layout plan;
- six shadowless practical light pools, a bar-only Bloom/color/vignette/grain
  grade, local dust, a slow ceiling fan and a skippable `1.35 s` single-camera
  Bezier reveal establish the interior without changing the chase-camera
  contract or the fog-free `220 m` bar range;
- one compact validated `10 x 8 x 3.4 m` home interior with explicit main-room
  and bathroom zones, clear entry/main/bathroom paths, six main-room furniture
  groups and separate toilet, shower and sink footprints; its runtime-built
  shell, stained surfaces, dirty dishes, bottles, cans,
  ashtray, worn clothes, newspapers, old radio and personal remnants establish
  a neglected impoverished old alcoholic's bachelor flat, while the dedicated
  blocking camera-corner junk keeps the authored camera pocket unreachable;
- a real window and open glazed door in the Home right wall leading, without
  another scene load, onto a walkable third-floor balcony at `4.7 m` street
  elevation; open-looking rails retain invisible safety colliders, while the
  view rebuilds only a bounded same-seed slice of the actual street's roads,
  lots, windows, lamps and signals and never creates a second City root,
  player, camera or realtime street-light pool;
- one reusable animated-interaction timeline and player controller with
  `Idle -> Entering -> Looping -> Exiting` phases; its outer presentation root
  stays camera-facing while an inner visual preserves authored handedness and
  can perspective-project a contextual world axis into the camera plane,
  can opt into a dedicated depth-independent shared sprite material,
  supports optional extra holds on individual loop frames and a validated
  per-request exit-duration multiplier that resets after each interaction,
  interpolates between stand/action hip anchors while preserving the physical
  player root, locks movement through the complete interaction, allows
  interaction input again only in the persistent loop, and safely restores
  the nine-layer rig plus realtime and contact shadows on completion or
  cleanup;
- one reachable bed interaction on the open `xMax` side of the Home bed:
  the first `E` plays 24 lie-down frames at `12 fps`, 16 sleeping frames loop
  indefinitely at `4 fps` with a short full-inhale hold and a longer
  post-exhale rest for one `5 s` breath cycle, and the second `E` plays 24
  wake-up frames at `12 fps`; the sleep pose follows the bed's
  perspective-projected head-to-foot
  axis with the hero's head at the `xMin` pillow, balanced head/foot margins
  and the complete opaque sleeping silhouette above the bedding; disabling or
  destroying the owning bed safely cancels sleep and restores the player; the
  point-filtered `8 x 8` atlas contains 64 `128 x 96` cells, uses localized
  RU/EN sleep/wake prompts and retains all 64 source frames plus deterministic
  extraction and atlas-build tools;
- one bed-relative low-poly nightstand and 3D alarm clock that remain visible
  as ordinary Home dressing. Its reusable 28-segment display begins the
  one-shot opening at `05:59` and flickers all digits and punctuation briefly
  at long intervals. After a silent five-second input lock it reveals the menu
  without changing the time or starting the alarm. Choosing Wake Up changes
  the display to solid `06:00`, generates a looping mono `22050 Hz` mechanical
  ring, rattles visibly and routes its fully spatial source through
  `SFX/World`. The clock shot and sleeping loop remain fixed for three
  unscaled seconds; the ring then stops and only then does the camera glide to
  the sleeper and smoothly settle into the active Home shot while the existing
  24-frame wake sequence plays over six seconds instead of the ordinary two,
  restoring normal control without a camera cut or another scene load;
- one fully built bathroom with tiled surfaces, an ajar doorway, toilet,
  shower tray and curtain, pedestal sink, cracked mirror, exposed rusty pipes,
  leak stains and floor drain; the toilet cistern sits against the right wall
  and its bowl faces into the room;
- one Home-only atmosphere with a weaker hard-shadow directional/ambient base,
  exactly two unchanged shadowless practical pools whose visible HDR emitters
  and halos are physically aligned with their lights—a dirty-yellow hanging
  lamp and a cold bathroom tube—plus one cold shadowed cookie Spot projecting
  night light through the window, a shared transparent glass shader/material,
  a restrained Bloom/color/exposure/vignette/film-grain runtime volume, at
  most 12 sparse dust motes, a calm room bed, spatial refrigerator/balcony
  layers, sparse domestic details and a short damped reverb snapshot without
  echo;
- one Main Camera that hard-cuts between the user-approved main-room pose at
  `(-4.48, 3.00, -3.25)`, Euler `(28°, 55°, 0°)` and `64°` FOV and the
  bathroom pose at `(1.82, 2.20, 0.86)`, Euler `(30°, 38°, 0°)` and `92°`
  FOV, plus a third balcony shot; separate activation and wider hold bounds
  provide threshold hysteresis, while each fixed position ignores
  orbit/follow input and retains only quarter-strength intoxication, balance
  and fall rotation;
- while the Home fixed-camera controller is active, the nine-layer
  `BillboardSprite` opts into exact camera-plane alignment using
  `-camera.forward` and `camera.up`; this preserves the authored `64 x 96`
  aspect instead of compressing the sprite in steep views, and the mode is
  reset when the controller releases the camera;
- a deterministic 12-person bar crowd with bartender, booth patrons,
  performer, standing groups and one bounded walker; six shared point-filtered
  pixel characters use lightweight billboards, centralized `8 Hz` decisions,
  role-specific idle actions and player-relative depth sorting;
- a scene-local spatial crowd bed plus rare glass/chair cues consume their
  layout radius/gain data and coexist with the existing bar music and
  procedural ambience inside a four-source budget;
- one exit, one activity station and one ordinary-drink counter station remain
  authoritative; the activity fixture adapts to cocktail mixing, beer pong,
  Split the G or Tinctures in a Row, while the separate counter point keeps a
  validated approach in every variant;
- one explicit `BarMinigameCatalog` whose ordered definitions and factories
  create both normal and debug minigame instances; cocktail mixing, beer pong
  Split the G and Tinctures in a Row are registered now, and future
  registrations appear in the debug list;
- an `F9` minigame debug window in both `City` and `BarInterior`; opening it
  closes a conflicting map or minigame before taking the modal lock, while
  launched debug instances neither complete bar visits nor persist drinking
  progress; clickable controls or the Left/Right arrow keys change the session
  intoxication by `-20/+20`, clamped to `0–100`, without changing the
  last-drink or consumed-drink context;
- bounded structured session diagnostics in `debug.log`: stable NDJSON
  envelopes correlate scene transitions, generated-city/bar/home initialization,
  route and visit state, minigame runs, drinking and balance outcomes, plus
  Unity warnings/errors; `F8` writes an immediate state snapshot and
  `Shift+F8` opens the log directory;
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
  count plus the deterministic balance-check delay/sequence; every beer-pong
  miss consumes a light beer, each Split the G attempt records the actual
  dark-beer fraction, and only an activated `XXX` in Tinctures in a Row
  immediately consumes `Moonshine` for 24 intoxication; reaching `100`
  terminates the applicable minigame at maximum intoxication without creating
  a separate timed status;
- a session-only cash wallet starting at `$999` and a localized nine-item
  counter menu in every bar; purchases atomically deduct a fixed integer price
  and immediately consume the selected drink, while water costs `$2`, counts
  as consumed, does not sober the player and preserves the last-alcohol
  context.

## Deferred

- Infinite streaming world and floating origin.
- Dynamic day/night, weather, rain, puddles and volumetric light shafts.
- Vehicle or skating physics.
- Multiple bespoke bar interiors.
- Mobile quality/render-profile parity; the current Windows/PC-targeted project
  retains only its PC quality level, render-pipeline asset and renderer.
- Full multi-frame eight-direction locomotion animation; the current vertical
  prototype uses one authored view per direction plus runtime joint walking,
  procedural living-idle motion and body-sprite blink variants, while the
  implemented bed interaction uses its own contextual 64-frame sequence.
- Minimap, in-world GPS trail, route autopilot, and manual map zoom/pan.
- Sobering mechanics, long-term save data, income/jobs, inventory, a broader
  economy, dialogue, quests, combat, save slots, and online features.
- Final bespoke art and audio masters, accessibility, localization coverage,
  and platform release work.
- Split the G Easy/Hard profiles, persistent best scores and streaks.

South City Rollers/Skaters is a design reference only for procedural-world and sprite-character approaches; its code and assets are not present in this repository.
