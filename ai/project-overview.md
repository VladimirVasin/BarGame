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
  `Assets/Scenes/SupermarketInterior.unity`,
  `Assets/Scenes/StairwellInterior.unity` and
  `Assets/Scenes/HomeInterior.unity`.
- Runtime assembly: `BarPromenade.Runtime`.
- Test assemblies: shared `BarPromenade.TestSupport` infrastructure plus
  `BarPromenade.EditModeTests` and `BarPromenade.PlayModeTests`. A run-level
  callback silences listener output for every automated test and restores the
  previous listener volume when the run finishes.

## Implemented MVP

A runtime-composed 3D city in which a sprite-based player walks along roads,
approaches interactive bars, a supermarket and their nearby home, enters
separate interiors, and returns to the matching exterior entrance.

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
  `48 m` camera visibility cap. The Windows player explicitly retains the
  runtime-only Exp2 shader variant instead of relying on build-scene scanning,
  plus lifted geometry values, cold moonlight and a retuned
  Bloom/ColorAdjustments/Vignette/FilmGrain profile;
- a default `640x360` PS1 world composite with four-tap footprint averaging,
  exact 2x/3x scaling at 720p/1080p, a 35% perceptual-space RGB555 blend
  without a screen-space dither grid, point upscaling and percentage-driven
  intoxication vignette, ghost/chromatic image, warp, warmth and exposure
  pulse; lower `426x240` and `320x180` presets remain available;
- a crisp retro IMGUI layer after the world composite: prompts, HUD and city
  map use a logical `640x360` canvas, while the information-dense cocktail
  interface keeps responsive sizing; persistent key-binding guides and
  control-hint footers are intentionally absent from menus, modal inspectors,
  the map and minigame views; every active contextual prompt is a full pointer
  click target routed through the same guarded action as keyboard/gamepad
  interaction;
- a localized PS1-style pause menu in City, Bar, Supermarket, Home and
  Stairwell gameplay:
  Escape or gamepad Start captures the shared modal lock, freezes scaled time,
  pauses non-UI audio, hides gameplay HUD/input and offers only Resume, Start
  Over and Quit Game. Restart and quit require an explicit default-No warning
  because no save/load system exists; child modals retain first ownership of
  Escape, the Home opening stays exclusive and the bar arrival reveal cannot
  be skipped by opening pause;
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
  `StairwellInterior`; Home adds an optional
  `Resources/Audio/HomeMusic/home_theme` loop. Every scene theme starts at
  zero gain, waits for its background-streamed clip data and fades in over one
  unscaled second. The apartment theme alone caps its source volume at `0.35`
  instead of the shared `0.65` scene-music level. Before a Single-load
  activates, the destination preloads while the current scene remains alive
  long enough for its theme to fade fully out. Home pauses `home_theme` after
  its fade-out whenever the Balcony shot owns the doorway-hysteresis zone,
  then resumes the same sample through a fade-in only after returning indoors.
  Home also owns an optional interaction-local
  `Resources/Audio/SmokingMusic/smoking_theme` loop: it starts from the
  beginning with a `3.2 s` fade when the balcony-smoking vignette begins and
  fades out with its `2 s` exit. Missing optional tracks are silent-safe. All
  themes route through the shared `Music` mixer group, receive a mild
  low-pass treatment and remain owned by their scene or interaction;
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
  plus a five-source Home spatial soundscape and a three-source Stairwell
  soundscape. Home combines a calm room bed, synchronized co-located closed
  and open refrigerator loops, balcony night air and sparse soft wood,
  radiator, radio and bathroom details, plus a dedicated fluorescent crackle
  source co-located with the bathroom tube. The crackle is triggered by the
  same applied-factor changes that drive the visible tube, halo, point and
  spill lights. Both refrigerator timbres are `4 dB` louder than the original
  mix and retain their equal-power door crossfade. Stairwell combines a
  concrete room bed, ventilation and electrical buzz with rarer pipe knocks,
  metal stress,
  distant water and movement. Both use deterministic schedules, `22050 Hz`
  mono clips, deliberately quantized retro waveforms and layout-derived
  anchors;
- a spanning-tree road graph with deterministic loops, cross-city arterials
  and a connected park-path cross;
- five readable districts: Old Town, Residential, Industrial, Nightlife and
  a central `4 x 4`-block park with lawn, plaza, trees, benches, hedges and
  four continuously walkable gates;
- one deterministic city-decoration plan with a distinct silhouette or facade
  treatment on every ordinary building lot, four primary urban landmarks, two
  park landmarks and optional frontage, roadside and park clusters. Its 24
  visual families include chimneys, scaffolding, balconies, laundry, tanks,
  pipe racks, billboards, fire escapes, markets, discarded furniture, cargo,
  vending queues, shelters, phone booths, roadworks, a fountain/statue,
  bandstand, chess tables and playground equipment;
- four first-class open district points of interest on their own full-block
  land-use lots: Old Town's waterworks court, Residential's drying yard,
  Industrial's weighbridge and Nightlife's last-route island. Their canonical
  layout descriptors reserve public ground and every adjacent street access;
  the lots contain no building, bar, player home or primary landmark. A
  dedicated physical builder gives each place a different free-standing
  silhouette and movement grammar, while the Home exterior reconstructs the
  same nearby descriptors in local space. Nightlife's island keeps its broken
  canopy ring but uses no emissive strips: the old departure board is visibly
  grounded on two supports and weathered route plates, layered posters, a
  waste bin, bottles, a discarded timetable and a lost scarf replace the
  repeated neon. These authored recipes require both
  lot dimensions to meet
  `CityLayoutGenerator.MinimumDistrictPointLotDimension` (`18 m`); smaller
  custom blocks omit the district POIs safely;
- frontage-aware windows and facade details now face each lot's actual road.
  Decoration geometry is visual-only, shadowless and collider-free, reuses
  the two packaged shared materials and combines at most six style batches per
  `48 m` chunk. The bounded Home balcony exterior rebuilds the same seeded
  descriptors in Home-local space instead of showing a simpler parallel city;
- rendered streets, park paths, lawn and plaza own matching static colliders,
  so the existing `0.28 m` controller step climbs their real height changes
  instead of letting the puppet intersect raised surfaces;
- deterministic collider-free ochre guard rails, batched into `48 m` spatial
  chunks, that trace only street boundaries, close dead ends and leave clear
  openings around every bar, supermarket and park-gate approach, while
  removing the full fence interval from every public-place side that meets a
  street;
- 144 land-use lots by default, including 16 park cells, 4 open district points
  of interest, exactly 4 reachable bars in four different urban districts and
  one non-bar player home beside one bar street, plus exactly one ordinary
  street-front supermarket. Its deterministic selection prefers Residential,
  then the shortest traversable route from the home, without consuming a bar,
  public-place or primary-landmark lot. Every bar pair is at least `120 m`
  apart by traversable graph distance, while stable row-major order assigns
  cocktail mixing, beer pong, Split the G and Tinctures in a Row;
- a default `8.8 m` player-home mass with a recognizable third-floor balcony,
  open door and window; the City facade uses the same balcony geometry as the
  Home interior's exterior opening;
- when the opening route first reaches the City, the hero starts on the road
  node beside the deterministic player home and its neighboring bar, `12 m`
  from their shared street approach under default spacing; custom-layout
  fallback placement remains bounded to `48 m` by traversable street distance,
  and returning from a bar, home or supermarket interior restores that
  entrance's own return point;
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
  whole-puppet sway in City, BarInterior, SupermarketInterior, HomeInterior and
  StairwellInterior, plus one small light-independent analytic contact patch
  fixed to the grounded player root;
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
- Home keeps its three authored fixed shots and now protects the player from
  foreground occlusion through one explicit registry of logical furniture,
  dressing, door and balcony-rail groups. Five camera-to-sprite samples cover
  the head, both sides of the chest, pelvis and feet; the first four drive
  reveal decisions, while low objects may still hide the feet naturally.
  Blocking groups fade to their authored `0.15-0.23` visibility floors through
  one shared opaque alpha-clip dither material, with a `0.15 s` fade,
  `0.12 s` clear hold and `0.30 s` restore. The material retains Forward+
  practical lights, cookies, shadows and SSAO. The system never changes colliders,
  glass, lights or the room shell, and restores full opacity while the opening,
  refrigerator or animated Home interactions own presentation;
- one percentage-driven intoxication profile shared by City, BarInterior,
  SupermarketInterior, HomeInterior and StairwellInterior:
  `1–20` Light Buzz / «Лёгкий хмель», `21–40` Tipsy / «Навеселе»,
  `41–60` Drunk / «Подшофе», `61–80` Unsteady / «Шатает» and `81–100`
  Very Drunk / «В стельку»; `0` is Sober and hides the HUD. Free gameplay
  continuously lowers the session level on unscaled time, from about `12 s`
  per point at `100` to `3 s` per point near sober; modal interactions pause
  this recovery so their committed drinking snapshots remain authoritative;
- continuously escalating movement slowdown, puppet sway, arm spread, knee
  bend, camera roll and world-image distortion within those ranges, with the
  HUD rendered as five independently filling 20-point segments; presentation
  eases toward a changed level over about `0.7 s`;
- deterministic balance checks only above `60`: a crisp `140°` arc appears
  above the player with a green center sector, moving arrow and red risk
  meter; arrows, A/D, D-pad or left stick counter the arrow, while higher
  intoxication narrows the safe sector, strengthens disturbances and makes
  checks longer and more frequent. Warning and active checks keep locomotion
  enabled, so those same directional controls move the hero while steering
  the balance arrow;
- a failed balance check switches the existing body renderer from the jointed
  puppet to one of 16 detailed no-mirror fall atlases: eight camera-relative
  views times separate screen-left/screen-right trajectories, with 80
  `128x96` frames per atlas. The upright player root remains stationary, the
  contact shadow expands and offsets, the light-facing shadow uses the same
  authored frame, and the sequence recovers through `0.45 s` falling,
  `1.2 s` down and `1.0 s` rising before restoring all nine puppet layers;
- a full-screen city map with district colors and labels, distinct park land
  and paths, player/bar markers, a dedicated labeled home icon and four
  non-interactive, kind-specific public-place markers with a localized legend.
  Public lots are drawn as open ground rather than buildings, and both lot
  cells and markers come directly from the canonical validated layout used by
  the world builder. Bar visits, ordered route editing and deterministic
  shortest paths remain separate from the POI presentation and are constrained
  to the generated road graph;
- localized RU/EN interaction prompts whose pointer, keyboard and gamepad
  activation share one action path;
- guarded asynchronous transitions and persistent seed/bar/route/visited
  context for the current city, with an explicit bar/home/supermarket return
  kind, a separate stairwell-arrival side and a consumed
  `Normal`/`OpeningSleep` Home arrival value;
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
  roughly every 36 seconds. Activating the shared `IInteractable` path now
  opens a localized default-Talk `Talk`/`Interact` target menu: Talk preserves
  the existing temporary cat response, while Interact checks the run inventory
  for one open stew can and either shows a localized missing-item thought or
  opens a default-No `Feed the cat?` confirmation;
- one reusable inventory-backed target-interaction model and controller own the
  pure item requirement, `Choice -> Confirmation -> Executing -> Closed`
  states, pointer/keyboard/gamepad choices, shared modal lock, temporary prompt
  feedback and lifecycle cleanup. A confirmed handler prepares every visual
  resource before `GameSessionState` atomically removes the required stack;
  failed preparation or a stale requirement consumes nothing. The stairwell
  cat is the first adapter: accepting the feed consumes exactly one
  `OpenStewCan`, visibly walks the ordinary hero to an authored middle-shot
  entry pose and pairs
  the `1024x768` `Resources/Player/PlayerCatFeedingAtlas` (`8x8`, 64 frames)
  with the cat's point-filtered `512x128`
  `Resources/Stairwell/Cat/StairwellCatFeedingAtlas` (`8x2`, 16 frames at
  `6 fps`). The cat track starts with the player's action loop, pauses the
  ordinary cat idle/look state and restores both actors, shadows, input, HUD,
  camera and modal ownership after normal completion or abnormal cleanup;
- one deterministic shared `22 x 16 x 4.8 m` bar interior with seven authored
  zones and four validated circulation paths; its long layered counter,
  bottle-backed mirrors, three booths, four high tables, stage, entrance
  dressing and dedicated activity bay are composed at runtime from one
  validated layout plan;
- six shadowless practical light pools, a bar-only Bloom/color/vignette/grain
  grade, local dust, a slow ceiling fan and a skippable `1.35 s` single-camera
  Bezier reveal establish the interior without changing the chase-camera
  contract or the fog-free `220 m` bar range;
- one separate runtime-composed `16 x 11 x 3.6 m` `SupermarketInterior` with
  protected aisles, three shelf sections, a stockroom facade and a decorative
  checkout staffed by one decorative cashier. The checkout is not a purchase
  station: activating a shelf opens its authored fixed product view and lets
  mouse, keyboard or gamepad selection operate directly on the shelf's
  physical goods;
- the three supermarket shelves contain exactly one finite physical unit of
  five catalog products: instant noodles and a day-old loaf, vodka and a closed
  stew can, plus one chicken egg on the cold shelf. Confirming a purchase
  atomically deducts its integer price, adds one matching inventory item and
  commits the product's stable world-source ID. The bought model and collider
  disappear immediately, and the source filter keeps that shelf position empty
  after leaving and re-entering until `BeginNewGame`. Failures for insufficient
  cash, a full stack or an already-bought source mutate nothing. Closed stew is
  a separate `ClosedStewCan` item from the refrigerator's `OpenStewCan` and
  therefore cannot satisfy the stairwell cat's feeding requirement;
- one compact validated `10 x 8 x 3.4 m` home interior with explicit main-room
  and bathroom zones, clear entry/main/bathroom paths, six main-room furniture
  groups and separate toilet, shower and sink footprints; its runtime-built
  shell, stained surfaces, dirty dishes, bottles, cans,
  ashtray, worn clothes, newspapers, old radio and personal remnants establish
  a neglected impoverished old alcoholic's bachelor flat, while the dedicated
  blocking camera-corner junk keeps the authored camera pocket unreachable;
- one visually prominent data-first refrigerator fitted into a split kitchen
  counter, with a validated player-width approach created by moving the table
  deeper into the room; its runtime-built worn enamel cabinet contains a
  hollow liner, three stained shelves, a lower drawer, frost, grime and two
  door bins. Six cavity slots and two door slots form the storage contract;
  the initial occupied slots hold a vodka bottle, one chicken egg and an open
  can of stew. Each occupant owns stable catalog metadata, registered
  renderers and a tight non-blocking selection trigger. A successful `Take`
  now transfers the item into the run inventory, removes the physical model
  and persists the stable collected slot across scene round trips;
- one localized modal refrigerator interaction: the Home camera follows an
  unscaled first-person Bezier approach while the ordinary puppet remains
  visible, then hides its rig and shadows in the same frame that a low-poly
  sleeved hand first appears to turn the handle before the sealed door opens
  to `102°`. The open inspection persists until the clickable close prompt, a
  second keyboard/gamepad interaction or cancel requests the same guarded
  close path, then closes and seals; the rig and shadows return as soon as
  camera return begins, while input and HUD restore on completion at the exact
  fixed Home shot. A cold emissive strip and halo reveal the contents without
  adding another realtime `Light`; generated seal, hinge and closing-thunk
  cues accompany an equal-power crossfade between synchronized closed-door
  and open-door spatial refrigerator loops;
- while the outer refrigerator interaction holds the lit open state, one
  nested PS1-style item browser highlights the vodka, egg or open stew can and
  shows its localized name beside the pointer; keyboard/gamepad cycling and
  confirm provide the same selection path. Click or confirm flies the chosen
  model into the center of the camera, fades in a dark camera-facing backdrop,
  rotates it slowly and presents a localized title, short description and
  `Take`/`Use`/`Back` actions. `Take` atomically commits the stable world-item
  source and inventory stack before removing the model; `Use` remains
  unavailable inside refrigerator inspection, while target-owned uses such as
  feeding the stairwell cat begin at that world target. `Back`
  returns an untaken item before the refrigerator can close. Normal return, cancel,
  disable and destroy restore the exact parent, sibling index, local transform,
  selection collider and original renderer colors without acquiring a second
  modal lock;
- one localized fullscreen PS1-style hero inventory in City, BarInterior,
  SupermarketInterior, HomeInterior and StairwellInterior. `I` or gamepad North
  captures the shared modal lock, freezes scaled time, hides the gameplay HUD
  and preserves the ambient audio bed; Escape, the same toggle or gamepad East
  restores the exact prior input, camera, HUD and time-scale state without
  opening pause on the same frame. The logical `640x360` screen combines a hero portrait cropped
  directly from the canonical neutral front player sprite, intoxication
  status, dollar cash, a five-column point-filtered icon grid, selected item
  description and contextual Examine/Close commands. The selected item is a
  live low-resolution 3D model in both the lower panel and Examine view; its
  hidden preview stage rotates on unscaled time and reuses the same procedural
  bottle, egg and open-can geometry as the refrigerator, plus the supermarket's
  closed can, noodles and loaf, alongside inventory key and lighter models. A
  pure catalog and ordered stack state begin every new run with apartment keys
  and a lighter, persist across scene loads and reset with the session; only
  commands backed by implemented rules are shown;
- a real window and open glazed door in the Home right wall leading, without
  another scene load, onto a walkable third-floor balcony at `4.7 m` street
  elevation; open-looking rails retain invisible safety colliders, while the
  view rebuilds only a bounded same-seed slice of the actual street's roads,
  lots, windows, lamps and signals. City and Home share the exterior ground,
  road, facade, window and passive bar-front appearance recipe. The balcony
  shot temporarily applies City's exact exponential-squared fog, matching
  background, `48 m` visibility cap, moonlight, grading, local fog field and
  bounded `12`-light street/bar pool, then restores the captured Home
  visibility and lighting for MainRoom, Bathroom, disable and destroy. It
  never creates a second City root, player or camera;
- one modal balcony-smoking vignette at the Home-local dock around
  `(6.60, 0.04, -1.45)`: the first `E` locks manual input while the ordinary
  rig walks to the entry point and turns toward the city along `+X`; only then
  does the smoking atlas begin. Its separate authored exit pose receives the
  ordinary rig after the final frame. The smoking definition uses
  `TextureFlipX = false` because
  the Balcony view projects the authored profile with the opposite apparent
  handedness; this is an interaction-specific override, not the shared
  animated-renderer default. It also sets
  `AlignBillboardToCameraPlane = false`, so the smoking billboard faces the
  camera with yaw only and retains world up through the pitched close shot.
  Bed and cat feeding instead use the exact camera plane because their
  authored silhouettes must remain uncompressed in their fixed views.
  Its point-filtered 64-frame atlas starts and ends on the exact ordinary
  `BackRight` idle with the same hip/foot pivot; frames `1-62` retain the
  authored keyed motion without Bayer/RGB dissolve frames. Visibility changes
  directly between the ordinary nine-part rig and atlas at those matching
  endpoints, with no sprite alpha crossfade. Dynamic and contact shadows stay
  visible during the approach, turn off only for atlas playback and restore at
  the exit handoff. The atlas plays a slow 24-frame
  draw/light/first-drag entrance at `6 fps`, then a 24-frame melancholic
  `9.5 s` drag, breath-hold and side-exhale loop at `6 fps` with deliberate
  `2.00 s`, `0.65 s`, `0.55 s` and `2.30 s` frame holds. A second `E` is
  accepted immediately but queues the 16-frame `8 fps` discard and return
  until a calm loop boundary, preventing a raised-hand or smoke cut;
  the camera holds briefly and eases along a quadratic push-in to `38°` FOV.
  Its close look target is biased `0.33 m` toward the city along Home-local
  `+X`, turning the target yaw to about `13.12°`: the hero stays prominent at
  about `0.37` viewport X while a point `1 m` cityward remains visible to his
  right. Camera position and FOV are unchanged. A smoking-local deterministic
  drift overlays that path with restrained
  X/Y/Z position amplitudes of `0.016 / 0.007 / 0.005 m` and pitch/yaw/roll
  amplitudes of `0.12° / 0.20° / 0.08°`. Paired `13-23 s` harmonics share one
  clock across every interaction phase, while the camera blend fades their
  envelope in and back to exactly zero during the smooth `2 s` Balcony-shot
  restoration. FOV has no extra pulse and generic `PlayerCameraFollow` remains
  unchanged. The
  separate optional `smoking_theme` music starts from silence and fades with
  the camera; without a supplied clip, the complete interaction remains
  playable and silent;
- one reusable animated-interaction timeline and player controller with a
  visible `Positioning` pre-phase followed by
  `Entering -> Looping -> Exiting`; each interaction supplies separate entry
  root/hip/facing, action hip and exit root/hip/facing poses. The ordinary rig
  uses the normal constrained motor, gait, turn and footsteps to reach entry;
  atlas playback starts only after exact grounded alignment. A shared handoff
  lock snaps the ordinary puppet to the deterministic nearest eight-way view,
  clears gait, idle, intoxication and facial offsets to its neutral rest pose,
  and presents that endpoint for one render frame before `Entering`. Bed and
  cat feeding use matching `FrontLeft` endpoints; smoking uses `BackRight`.
  The terminal exit frame is likewise presented before the physical root is
  placed at the separate exit pose, and the neutral ordinary puppet remains
  locked through its final `LateUpdate` restoration frame. Its outer
  presentation root can face the camera either with a world-up yaw billboard
  or with exact camera-plane alignment selected per definition, while an inner visual
  preserves authored handedness and can perspective-project a contextual
  world axis into the presentation plane,
  supports a per-definition horizontal flip. Camera-plane definitions resolve
  their upright entry/exit hip against live `camera.up`, then refresh the atlas
  root again in `LateUpdate` so camera motion cannot leave a one-frame pivot
  mismatch; world-up definitions retain the authored upright hip unchanged.
  Bed, smoking and cat feeding all use a hard endpoint-matched handoff with
  zero sprite alpha fading. The controller can opt into a dedicated
  depth-independent shared sprite material, supports optional extra holds on
  individual loop frames and a validated
  per-request exit-duration multiplier that resets after each interaction,
  interpolates entry/action/exit hip anchors while keeping the physical player
  root at its aligned dock during atlas playback, locks manual movement through
  the complete interaction, allows
  interaction input again only in the persistent loop, and safely restores
  the nine-layer rig plus realtime and contact shadows on completion or
  cleanup. Entry must remain at the authored grounded height: a vertical
  mismatch is rejected without teleporting, while a constrained approach that
  makes no position or rotation progress times out and cancels cleanly. Scene
  transition, disable and destroy boundaries also stop `Positioning`, release
  the handoff lock and restore captured control/presentation state;
- one reachable bed interaction on the open `xMax` side of the Home bed:
  the first `E` first walks and turns the ordinary hero to the open-side entry
  pose, settles on the matching neutral `FrontLeft` endpoint for one rendered
  frame, then directly hands off with zero sprite fade to 24 lie-down frames at
  `12 fps`; 16 sleeping frames loop
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
  exactly two bounded shadowless practical pools whose visible HDR emitters
  and halos are physically aligned with their lights—a dirty-yellow hanging
  lamp and a cold bathroom tube. The bathroom point pool, tube and halo share
  one deterministic unscaled flicker: they stay steady for most of each
  `6.4 s` cycle, then briefly stutter through a `0.52 s` fluorescent-failure
  burst while a co-located spatial electrical crackle follows every actually
  rendered factor change. One separate cold shadowed ForcePixel Spot starts just inside the
  bathroom threshold, shares the same flicker and projects through the ajar
  door onto the apartment exit area; another cold shadowed cookie Spot casts
  night light through the window. These remain capped at four
  atmosphere-owned local realtime lights; the scene Directional light is
  separate. The atmosphere also owns a shared transparent glass
  shader/material, a restrained Bloom/color/exposure/vignette/film-grain
  runtime volume, at most 12 sparse dust motes, a calm room bed, spatial
  refrigerator/balcony layers, sparse domestic details and a short damped
  reverb snapshot without echo;
- one unchanged Main Camera setup that hard-cuts among three authored poses:
  the user-approved MainRoom pose at `(-4.48, 3.00, -3.25)`, Euler
  `(28°, 55°, 0°)` and `64°` FOV, the Bathroom pose at
  `(1.82, 2.20, 0.86)`, Euler `(30°, 38°, 0°)` and `92°` FOV, and the Balcony
  pose; separate activation and wider hold bounds provide threshold
  hysteresis, while each fixed position ignores
  orbit/follow input and retains only quarter-strength intoxication, balance
  and fall rotation;
- while the Home fixed-camera controller is active, the nine-layer
  `BillboardSprite` uses exact `-camera.forward`/`camera.up` plane alignment in
  MainRoom and Bathroom, preserving the authored `64 x 96` aspect in their
  steep views. Balcony deliberately switches the ordinary rig to a world-up
  yaw billboard, and the shared default is restored when fixed control ends;
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
  last-drink or consumed-drink context; an unpaid bar-menu presentation may
  be replaced immediately, but a committed physical drink service cannot be
  interrupted through this debug path;
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
- a session-only cash wallet starting at `$999`, shared by finite supermarket
  stock and a localized physical
  nine-item counter menu in every bar. Interaction glides into a seated
  first-person shot with procedural low-poly arms and a full-width row of nine
  individually selectable 3D bottle objects; every bottle owns a solid
  collider, selection trigger, kinematic Rigidbody and mouth anchor. Confirm
  atomically deducts the fixed integer price and consumes the selected drink
  once, then locks ordinary cancellation while the right hand picks up and
  tilts that exact bottle, a world-space stream fills the matching reusable 3D
  tumbler, pint, wine glass, shot glass or snifter. The left hand holds it at
  the mouth for an exact three-second drink, then returns the empty vessel to
  the counter. Completing an order stays in the same seated browser for
  another selection; only the dedicated Exit action (`Esc` / gamepad `B` or
  the visible button) starts camera return and leaves the menu.
  Water costs `$2`, counts as consumed, does not sober the player and preserves
  the last-alcohol context; lifecycle teardown restores every transform,
  collider, camera, rig, shadow, input and HUD state without refunding an
  already committed purchase. The whole bottle row remains inside the seated
  shot at 16:9 and 16:10, and repeated orders restore reusable vessels to their
  authored scale. The service camera sits above the counter at natural seated
  eye height, while the green order marker and sign remain hidden across
  repeated orders until the explicit camera return completes.

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
  implemented bed, balcony-smoking and cat-feeding interactions use their own
  contextual 64-frame sequences.
- Minimap, in-world GPS trail, route autopilot, and manual map zoom/pan.
- Sobering mechanics, long-term save data, income/jobs, a broader
  economy, dialogue, quests, combat, save slots, and online features.
- Final bespoke art and audio masters, accessibility, localization coverage,
  and platform release work.
- Split the G Easy/Hard profiles, persistent best scores and streaks.

South City Rollers/Skaters is a design reference only for procedural-world and sprite-character approaches; its code and assets are not present in this repository.
