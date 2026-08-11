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
- Player presentation: one modular `Player3D.prefab` in all five gameplay
  roots, with independent mesh parts, a Generic in-place action set,
  same-prefab first-person subsets, a dedicated portrait, real mesh shadows
  and an analytic contact patch.
- Test assemblies: shared `BarPromenade.TestSupport` infrastructure plus
  `BarPromenade.EditModeTests` and `BarPromenade.PlayModeTests`. A run-level
  callback silences listener output for every automated test and restores the
  previous listener volume when the run finishes.

## Implemented MVP

A runtime-composed 3D city in which one modular low-poly 3D hero walks along
roads, approaches interactive bars, a supermarket and his nearby home, enters
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
  Up alone switches it to solid `06:00`, starts the session clock and alarm,
  and hides the menu. After three more unscaled seconds on the clock and
  sleeping loop, the alarm stops; only then does the six-second,
  two-times-slower opening wake begin, gliding to the sleeper over `2.25 s`
  and easing onward into the active gameplay shot without a cut;
- one session-owned in-game clock that starts every fresh run frozen at
  `05:59`, advances only after the successful startup Wake sets it to `06:00`,
  and persists through Single-mode scene loads. It advances on scaled time at
  `1.0` game minute per real second, so a full `24 h` cycle takes exactly
  `1440` real seconds (`24` minutes), crosses midnight with a day index and
  naturally pauses wherever gameplay sets `timeScale` to zero. The Home clock
  and inventory Status panel both show its current `HH:MM`;
- a finite, seed-reproducible coastal city driven by one immutable blueprint:
  the default keeps a connected `12 x 12` road-and-lot core, adds a full-width
  northern beach and water strip, and anchors the central park at world/map
  center. Active cells, roads and surfaces may form a connected sparse,
  non-rectangular footprint inside their map bounds;
- one shared MVP day/night lighting cycle for City, the Home window and the
  Home balcony exterior: night before `06:00`, smooth dawn from `06:00` to
  `07:00`, day until `18:00`, smooth dusk until `19:00`, then night again.
  It blends directional/ambient/reflection lighting and the bounded City/Home
  exterior night fixtures; Bar, Supermarket and Stairwell lighting remain
  unchanged. The `0.070` exponential-squared luminous gray-green fog,
  fog-matched terminal camera backdrop, City-only `48 m` visibility cap,
  `CityFogField` and `CityNoirVolumeProfile` stay fixed across the cycle. The
  Windows player explicitly retains the runtime-only Exp2 shader variant;
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
- four opaque generated exterior albedos: dark compacted soil for exposed
  ground between buildings, dark ordinary asphalt, the former light road
  texture reassigned to sidewalks, and worn white traffic paint. They retain
  Repeat/Bilinear/mipmap import settings and use XZ planar UVs at `12 m` for
  soil and asphalt, `6 m` for sidewalks and `2 m` for markings through
  material property blocks on the one shared `RuntimePrimitiveLit`, without
  material instances. The same soil recipe covers the bounded Home exterior
  ground reconstruction. A deterministic
  `CityStreetSurfacePlan` applies the Road v2 `8 m` ordinary-street footprint:
  a `6 m` carriageway plus two raised `1 m` sidewalks, with an `8 x 8 m`
  intersection core and a clear `6 x 6 m` ordinary carriageway apron. Road
  v2.1 deterministically reserves eligible perpendicular two-way corners and
  three- or four-way bus nodes by moving their four `1 m` corner sidewalk pads
  outward onto clear adjacent ground, exposing the complete `8 x 8 m` asphalt
  apron and cutting each real raised curb back by `4.5 m`. At a selected
  three-way node, the missing side closes outside
  that apron with a continuous `1 x 8 m` raised sidewalk joining both corner
  pads. It keeps park paths separate, textures the center dashes white and adds
  zebra crossings on up to six selected ordinary intersections. A bus apron may
  share flat zebra paint and paired signals; retained bus maneuvers sample the
  inflated body against both actual pole positions at a conservative `0.30 m`
  fixture radius. City colliders and the bounded Home reconstruction consume
  the same geometry plan;
- one player-following `CityFogField`, capped at 36 more visible slowly
  drifting particles, plus depth-tested soft halos around lamps, bar lights
  and active signals;
- one deterministic radius-safe sidewalk/crosswalk navigation graph with
  spawn anchors on long pavement segments. At most two low-poly walkers are
  active near the player: one randomized runtime event activates one slot at a
  randomly ranked obstacle-safe anchor in the preferred fog-hidden `76-86 m`
  band. If that band contains no anchor in a graph component capable of reaching
  the player, a linked dense-fog fallback may use `32-86 m`. The
  first event waits `1.25-7.5 s`; every later slot or replacement waits its own
  `3.5-12.5 s`, so the two walkers are deliberately staggered. Each walks
  forward through available turns, has an independent 50% choice at a zebra,
  and is recycled only after moving beyond `88 m` from the hero. Before a fresh
  walker first reaches `24 m`, eligible non-backtracking turns follow the
  shortest physical graph distance to the closest node in that sidewalk
  component; after that first encounter the guidance stays
  off and ordinary random roaming resumes. By day, its hidden distant
  simulation smoothly accelerates from authored pace at `32 m`
  to at most `2.75x` from `76 m`, bringing an approaching walker inward sooner
  and sending an outward walker back to the pool sooner. Camera
  direction, frustum membership and far-clip settings do not participate in
  spawn or recycling. Before `06:00` and from `19:00`, fresh population is
  capped at one slot and uses much longer `15-35 s` initial and `30-70 s`
  replacement delays without distant acceleration; the clock never removes an
  already active dusk walker.
  A slot's `CharacterController` is enabled only after a unique, obstacle-safe
  spawn and disabled before pooling. The dedicated layer collides with the player,
  ignores other pedestrians and is excluded from camera/interaction queries.
  Both slots reuse one lampshade-hood model, four material-property-block
  palettes and the hero's shared in-place `Idle`/`Walk` clips on a compatible
  Generic rig. Home transforms that same graph into its local exterior,
  retains a bounded `100 m` fog-hidden approach context beyond the facade while
  rendering its existing `48 m` street slice, and runs the slots only while the
  Balcony shot is active;
- one passive ambient midibus with a strict single-slot cap. The production
  model uses its real `8.25 x 2.38 x 2.95 m` body and `4.5 m` wheelbase rather
  than a hidden gameplay scale, and exposes a modeled driver area, twelve
  passenger seats, rails, dashboard, two animated doors, rolling wheels and
  front steering. Canonical Route 01 is an immutable right-hand, Street-only
  closed winding service loop. Its target planner orders every district point
  of interest that actually exists, followed by `PlayerHome`; the default
  sequence is Industrial, Nightlife, Residential, Old Town and Home. It assigns
  one safe straight to every target on its frontage or one connected road edge
  away, keeps the roadside pole on another cell and outside the POI
  public/access bounds or Home footprint, then connects the selected straights
  through the deterministic accepted-link graph. Full-body-clear ordinary
  straights and proven `6 m`-radius left turns enter the loop. At selected Road
  v2.1 nodes only, a clearance-proven two-edge right-turn macro uses a long
  S-merge across the full incoming Street, a `4.5 m` quarter-turn in the clear
  core and a symmetric S-return across the outgoing Street. The macro owns both
  physical edges, so a connector cannot use it to bypass a selected stop edge;
  ordinary tight `3 m` right turns remain rejected. A physical street link may
  recur in a connector, but every ordered occurrence receives a unique route
  link/node ID. Route selection has no random branch or player pursuit. The
  default five semantic stops each have a physical blue `01` pole and are served
  once per lap with a randomized `3-5 s` two-door dwell. Random roadside
  decoration does not emit bus shelters. Nightlife's last-route island now has a working pole nearby
  but outside its public ground and approaches, leaving the abandoned island
  structures distinct from the live stop. A pooled actor prefers obstacle-safe
  fog-hidden route poses `76-86 m` from the player and falls back to `56-86 m`
  only when forward travel on the same loop can approach the player. The cap
  means at most one bus can be active or potentially visible rather than
  guaranteeing that one is always on screen. It yields to the player and
  pedestrians and recycles only when its full body is at least `92 m` away.
  Wheel/steering articulation, a synthesized engine loop and night-scaled head,
  tail and cabin emission reset with the pool. Camera direction and frustum
  membership never participate in the lifecycle. The moving ambient-bus runtime
  is City-only. Home's bounded exterior regenerates the same route plan and
  reconstructs the nearby Home stop as a static collider-free pole, but it has
  no bus actor or director: no real Street pass-through offers both complete-body
  seams at or beyond the fog-hidden `56 m` boundary, and the default facade
  faces a visible road terminal. A fabricated continuation or
  Balcony-camera-owned activation would create a visible pop, so neither is
  introduced;
- deterministic street lamps with geometry batched into `48 m` spatial
  chunks, focused lower-pole collision proxies, shadowless spot-light pools
  and slow out-of-phase amber traffic signals generated from the road graph;
- scene-local looping music: `city_theme` loads only from
  `Resources/Audio/CityMusic` in `City`, while `bar_theme` loads only from
  `Resources/Audio/BarMusic` in `BarInterior`; the optional
  `supermarket_theme` slot loads only from
  `Resources/Audio/SupermarketMusic` in `SupermarketInterior`, and the
  optional `stairwell_theme` slot loads only from
  `Resources/Audio/StairwellMusic` in `StairwellInterior`; Home adds an optional
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
- an immutable `CityBlueprint`/builder/catalog boundary with stable blueprint
  and area IDs. Area definitions separate `UrbanBuilt` districts from
  `NonUrbanOpen` areas, retain a reusable visual archetype, declare movable,
  center-anchor or north-edge placement and assign buildable, park, open-land
  or water topology per cell. The legacy rectangular blueprint remains an
  explicit compatibility path;
- a spanning-tree road graph over only the sparse road footprint, with
  deterministic loops, filtered cross-city arterials, required open-area
  access edges and a connected park-path cross;
- four readable built areas—Old Town, Residential, Industrial and
  Nightlife—plus a fixed central `4 x 4`-block park with lawn, plaza, trees,
  benches, hedges and four continuously walkable gates;
- one mandatory north-edge waterfront in the default blueprint: its connected
  beach has a deterministic street approach and remains walkable to the water
  line, while the continuous northern water row is rendered and mapped but is
  excluded from player navigation and night-fixture placement;
- reusable Lake and Cemetery non-urban profiles that are now present on the
  default city's eastern edge. The `4 x 4` lake has a walkable shore around a
  blocked `2 x 2` water center, while the `3 x 2` cemetery is walkable ground;
  each requires one street-linked open-area approach and exposes the same data
  to world, fence, navigation, map and deterministic landmark consumers;
- one deterministic city-decoration plan with a distinct silhouette or facade
  treatment on every ordinary building lot, four primary urban landmarks, two
  park landmarks and optional frontage, roadside and park clusters. Its 24
  visual-family catalog includes chimneys, scaffolding, balconies, laundry,
  tanks, pipe racks, billboards, fire escapes, markets, discarded furniture,
  cargo, vending queues, a legacy shelter recipe, phone booths, roadworks, a
  fountain/statue, bandstand, chess tables and playground equipment. The
  ordinary random roadside pool deliberately omits bus shelters because
  Route 01 owns its target-derived physical stop poles;
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
  Decoration geometry is shadowless, reuses the two packaged shared materials
  and combines at most six style batches per `48 m` chunk. A deterministic
  `None`/`Detail`/`Blocking` catalog gives grounded structural and bulky
  recipes one to four simple box proxies, while rooftop, hanging and small
  narrative details stay non-physical. Park benches/hedges, the home mailbox
  and lower lamp/signal poles also own focused proxies. The bounded Home
  balcony exterior rebuilds the same descriptors in Home-local space but
  deliberately remains collision-free;
- rendered streets, park paths, lawn and plaza own matching static colliders,
  so the existing `0.28 m` controller step climbs their real height changes
  instead of letting the character mesh intersect raised surfaces;
- deterministic ochre guard rails, batched into `48 m` spatial chunks, only
  where a street faces water, unmapped space or the active map boundary, plus
  full-width caps at true degree-one street dead ends. Street-to-park
  continuations count as connected and are not capped. Rails are physical;
  their narrow posts remain visual-only, and all former entrance/gate/public
  opening descriptors remain available as decoration-clearance metadata;
- player navigation now includes complete logical `BuildableGround` regions
  as well as existing streets, park and `OpenLand`. Radius-safe seams connect
  road-to-ground and adjacent ground cells for the maximum `0.35 m` agent;
  water, unmapped cells and outside space stay excluded, while real building,
  prop, vegetation, pole, fence and pedestrian colliders decide local
  obstruction;
- 144 land-use lots in the default road-grid core, including 16 park cells and
  4 open district points of interest, plus 32 northern beach/water surface
  cells, 16 lake cells and 6 cemetery cells. The core still contains exactly 4 reachable bars in four different
  stable urban area IDs and one non-bar player home beside one bar street,
  plus exactly one ordinary
  street-front supermarket. Its deterministic selection prefers Residential,
  then the shortest traversable route from the home, without consuming a bar,
  public-place or primary-landmark lot. Every bar pair is at least `120 m`
  apart by traversable graph distance, while stable row-major order assigns
  cocktail mixing, beer pong, Split the G and Tinctures in a Row;
- a default `8.8 m` player-home mass with a recognizable third-floor balcony,
  open door and window; the City facade uses the same balcony geometry as the
  Home interior's exterior opening;
- when the opening route first reaches the City, the hero starts on the road
  node beside the deterministic player home and its neighboring bar, `13 m`
  from their shared street approach under default spacing; custom-layout
  fallback placement remains bounded to `48 m` by traversable street distance,
  and returning from a bar, home or supermarket interior restores that
  entrance's own sidewalk arrival point rather than the road centerline;
- diegetic bar identification through warm windows, framed entrances and
  shared camera-facing pixel mug signs;
- one production `Resources/Player/Player3D` prefab used by City, BarInterior,
  SupermarketInterior, HomeInterior and StairwellInterior. Its `1.75 m`
  low-poly Generic rig preserves 73 independent mesh objects, 16 explicitly
  registered anatomical parts, the left-forearm bandage, right-shoulder patch
  and diagonal strap. One shared URP/Lit material plus per-mesh property blocks
  retain the palette without per-instance materials;
- one manual PlayableGraph presentation that damp-blends the in-place
  four-second `Idle` and one-second `Walk` actions from actual planar speed.
  Idle alternates readable breathing and weight shifts; Walk uses full
  contact/down/passing/up phases with independently flexing elbows, knees and
  ankles. Start and stop use `0.14 s`/`0.20 s` smooth envelopes, and visible
  gait cadence follows the blended weight. Root motion stays disabled while
  registered face bones drive neutral, half/closed blink, watchful and tense
  states. A failed balance check may temporarily suspend this graph while the
  same registered bones are owned by the bounded ragdoll. Intoxication sway,
  arm spread, knee bend and balance lean are
  additive rotational/limb poses, preserve the authored pelvis position in
  the actor ground plane and reset through the same lifecycle cleanup. After
  the ordinary and additive pose is sampled, a cached rigid boot-sole contour
  offsets only the pelvis vertically so the lower visible sole stays at its
  neutral grounded height; the physical player root, model root and contextual
  clips remain untouched;
- ordinary URP mesh shadows cast from the real character geometry in every
  gameplay root, plus one small light-independent analytic contact patch fixed
  to the grounded player root. The patch follows foot plant and expands,
  rotates and offsets for left/right falls without moving the physical root;
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
  dressing, door and balcony-rail groups. Five 3D presentation samples cover
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
- continuously escalating movement slowdown, 3D bone sway, arm spread, knee
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
- a failed balance check begins with the matching registered
  `FallLeft/Right` action, then hands its current pose to a 13-body runtime
  ragdoll after a `0.16 s` directional lead-in. Physics owns the rest of the
  `0.45 s` falling phase and the `1.2 s` down phase; the pelvis is tethered to
  a `0.68 m` sphere around the fixed gameplay root, every ragdoll collider
  ignores both its peers and the upright `CharacterController`, and the
  expanded analytic contact shadow remains fall-aware. A `0.16 s` kinematic
  pose blend returns the complete hierarchy to the exact side-down first
  `RiseLeft/Right` sample. The distinct left/right `50`-source-frame
  (`1.67 s`) full-body actions then brace and roll prone, hold on all fours,
  place a lead foot under the body, pass through a low crouch and finish at the
  exact `Relaxed` seam without any bind/A/T-like fallback. All-fours remains an
  authored landmark inside the existing `Rising` phase rather than a new
  gameplay state. Completion, cancellation, transition, disable and destroy
  all restore the graph, neutral rig, kinematic bodies, disabled ragdoll
  colliders and ordinary contact shadow;
- a full-screen city map projected from the blueprint's centered map bounds,
  with area colors and labels anchored on real active cells, distinct park,
  beach, water, lake-shore and cemetery surfaces and paths, player/bar markers,
  a dedicated labeled home icon, a distinct
  grocery-shop marker and four kind-specific public-place
  markers with a localized legend. Hovering a bar, home, shop or public-place
  marker shows its localized name in a bounded high-contrast tooltip. Public
  lots are drawn as open ground rather than buildings, and all landmark data
  comes directly from the canonical validated layout used by the world
  builder. It also draws the canonical Route 01 loop as a blue ink-outlined
  line below the orange player itinerary, adds five numbered stop markers in
  the default layout with localized hover labels and keeps both symbols in a
  compact legend. The map deliberately has no live bus marker. With the
  City-only F9 test-teleport toggle enabled, every map lot becomes selectable,
  the side panel asks for an explicit confirmation and a
  confirmed target moves the hero to that lot's street-front return point or
  its nearest generated route when no frontage edge exists.
  Keep at least `22` logical pixels per map cell; clip overflowing content and
  pan it independently on X/Y with WASD, the right stick, mouse-wheel gestures
  or middle/right-button dragging while drawing scroll indicators only for
  overflowing axes. Bar visits, ordinary ordered route editing and deterministic shortest paths
  remain unchanged outside this debug mode and are constrained to the generated
  road graph;
- localized RU/EN interaction prompts whose pointer, keyboard and gamepad
  activation share one action path;
- guarded asynchronous transitions and persistent blueprint ID,
  seed/bar/route/visited context for the current city, with an explicit
  bar/home/supermarket return
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
  radiators, damp damage, trash and dense upper-floor junk. Eight opaque RGB
  ImageGen albedos under `Resources/Stairwell/Textures` cover wall paint,
  ordinary concrete, stair concrete, corroded metal, door paint, damage, dirty
  wood and mixed debris; the active wall, door and metal variants use stronger
  macro contrast for the low-resolution presentation. `StairwellSurfaceAppearance`
  caches those recipes and applies projection-aware deterministic per-renderer
  native-UV `_BaseMap_ST` scale/offset plus smoothness and metallic through
  material property blocks. Per-recipe linear-albedo compensation converts the
  original semantic tint into a brighter display tint so Lit multiplication
  preserves the former flat-color mean brightness and the one shared
  `RuntimePrimitiveLit` material. Every enabled ordinary shell and dressing
  renderer is covered, including walls/bands, floors, steps, landings, rails,
  doors/frames, pipes, vents, cabinets, radiator, damage, litter, upper debris
  and non-emissive fluorescent parts. Hidden walkable ramps and the upper
  safety blocker, emissive tubes and halos, the hero, cat and dust/VFX remain
  outside that surface layer. Three bounded fixed camera shots cut between the
  lower flight, middle flight and apartment landing with height hysteresis;
  each shot keeps its exposed suspended HDR fluorescent tube and halo visible,
  while three stronger flickering practical-light pools, a green desaturated
  Bloom/vignette/grain profile, at most 14 dust particles, a concrete ambience
  bed, spatial ventilation and electrical layers, sparse positional industrial
  cues, a long dark reverb/moderate echo snapshot and the separate optional
  `stairwell_theme` music slot establish the atmosphere;
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
  `OpenStewCan`, visibly walks the ordinary 3D hero to an authored middle-shot
  entry pose and samples `CatFeedEnter`, `CatFeedLoop` and `CatFeedExit` on the
  continuous world rig. The cat keeps its independent point-filtered
  `512x128` `Resources/Stairwell/Cat/StairwellCatFeedingAtlas` (`8x2`, 16 frames
  at `6 fps`). Its track starts with the player's loop, pauses ordinary cat
  idle/look and restores the hero, cat, contact shadow, input, HUD, camera and
  modal ownership after normal completion or abnormal cleanup;
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
  station: activating a shelf opens its authored fixed product view centered on
  the first available physical product. The modal browser keeps one continuous
  lock while previous/next input cycles through every stocked shelf, skips
  empty shelves and moves the camera to the selected shelf while aiming at the
  selected model's visual bounds. Muted clickable arrows sit directly beside
  the product instead of adding another footer hint; keyboard and gamepad use
  the same navigation path;
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
  unscaled first-person Bezier approach while the ordinary 3D hero remains
  visible, then acquires an owner-scoped world-visibility lease in the same
  frame that a camera-local right-arm subset from the production player prefab
  appears to turn the handle before the sealed door opens to `102°`. The open
  inspection persists until the clickable close prompt, a second
  keyboard/gamepad interaction or cancel requests the same guarded close path,
  then closes and seals; releasing the subset restores the exact world mesh and
  contact-shadow states as camera return begins, while input and HUD restore on
  completion at the fixed Home shot. A cold emissive strip and halo reveal the
  contents without adding another realtime `Light`; generated seal, hinge and
  closing-thunk
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
  opening pause on the same frame. The logical `640x360` screen combines a
  dedicated transparent portrait rendered from the production 3D hero, four
  compact intoxication/hunger/stress/fatigue bars, dollar cash, a five-column point-filtered
  icon grid, selected item description and contextual Eat/Drink, Examine and
  Close commands. Hunger, stress and fatigue are session-owned `0-100` values
  that start at zero and survive ordinary scene loads. Once the startup Wake
  starts the shared scaled session clock, hunger rises from `0` to `100` over
  `1440` game minutes and fatigue over `1080`; both use hidden double-precision
  fractions while the existing menu bars expose only clamped integers. The
  same `Time.deltaTime` path freezes progression before Wake and whenever
  `timeScale` is zero, but otherwise continues through scenes, transitions and
  ordinary interactions. Food clears the hunger fraction when it applies its
  relief, while only a normally completed bed sleep clears fatigue and its
  fraction. Neither need has a gameplay debuff yet. The selected item is a
  live low-resolution 3D model in both the lower panel and Examine view; its
  hidden preview stage rotates on unscaled time and reuses the same procedural
  bottle, egg and open-can geometry as the refrigerator, plus the supermarket's
  closed can, noodles and loaf, alongside inventory key and lighter models. A
  pure catalog and ordered stack state begin every new run with apartment keys
  and a lighter, persist across scene loads and reset with the session. Current
  food has explicit relief but cannot reduce hunger below `20`; food with no
  effect remains in its stack. Alcohol has separate stress-relief values, and
  the inventory vodka bottle commits four servings atomically while maximum
  intoxication leaves it untouched. Only commands backed by implemented rules
  are shown;
- a real window and open glazed door in the Home right wall leading, without
  another scene load, onto a walkable third-floor balcony at `4.7 m` street
  elevation; open-looking rails retain invisible safety colliders, while the
  view rebuilds only a bounded same-blueprint-and-seed slice of the actual
  street's asphalt, sidewalks, road markings, lots, windows, lamps and
  signals. City and Home share the exterior ground, street-surface, facade,
  window and passive bar-front appearance
  recipe. It also reconstructs the target-derived Home stop as the same static
  blue `01` pole in Home-local space, deliberately without colliders. The balcony
  shot temporarily applies City's exact exponential-squared fog, matching
  background, `48 m` visibility cap, current time-of-day lighting, grading,
  local fog field and bounded `12`-light street/bar pool, then restores the
  captured Home visibility and lighting for MainRoom, Bathroom, disable and
  destroy. During that shot only, the same two-slot pedestrian runtime supplies
  distance-managed passers-by in the fog-hidden band on the reconstructed
  street below; leaving the shot immediately pools them. The static stop is not
  a vehicle activation boundary: the balcony does not compose a bus actor or
  director because its real street context has no two-ended, complete-body
  fog-hidden pass-through. Fog and the City grade remain
  identical at every
  hour. It never creates a second City root, player or camera;
- one modal balcony-smoking vignette at the Home-local dock around
  `(6.60, 0.04, -1.45)`: the first `E` locks manual input while the ordinary 3D
  rig walks to entry and turns toward the city along `+X`; exact alignment
  holds one neutral rendered frame, then the same visible rig samples
  `SmokeEnter`, `SmokeLoop` and `SmokeExit`. The authored motion settles the
  hero toward the outer rail, draws the right hand from his coat, brings the
  cigarette to his lips for a held inhale, lowers it for an outward exhale and
  returns through a deliberate discard. Once per `9.5 s` loop, local frame
  `16` emits a bounded gray-green world-space plume from the registered mouth
  socket; it travels toward the city, grows and fades before the next loop,
  while queued exit stops new emission and lets the detached smoke dissipate.
  The approximately `74 mm` cigarette
  follows the registered right-hand socket along its outward local axis; it is
  revealed only after leaving the coat and hidden on the exit flick. A
  permanent worn enamel ashtray rests on the outer rail at Home-local
  `(7.25, 1.12, -1.67)`, directly below that authored flick, and remains
  visible independently of the smoking lifecycle. The
  deterministic timeline retains its melancholic `9.5 s` loop; a second `E`
  queues until a calm low-hand boundary, preventing a raised-hand or inhale
  cut. No renderer swap, sprite fade or shadow handoff is needed;
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
  root/pelvis/facing, action pelvis and exit root/pelvis/facing poses, plus an
  optional pelvis waypoint with independent arrival/hold timing in each
  transition. The
  ordinary rig uses the constrained motor, gait, turn and footsteps to reach
  entry. Exact grounded alignment clears locomotion, facial and additive status
  offsets and holds the neutral 3D endpoint for one rendered frame. The
  controller then samples the registered enter/loop/exit Generic clips on that
  same rig and aligns its pelvis anchor to the authored world target after each
  sample. Gameplay owns clip time and terminal holds; root motion, Animator
  transitions and Animation Events own no transaction. The terminal exit pose
  is presented before the physical root moves to the independent exit, and the
  neutral rig stays locked through its final `LateUpdate`. Extra loop holds and
  the opening-only exit-duration multiplier remain deterministic. Unreachable
  height, no-progress approach, failed preparation, transition, disable and
  destroy all reset clip spatial offsets and restore control, camera, HUD,
  props, partner animation and contact shadow through owned cleanup;
- one reachable bed interaction on the long `zMin` side nearest the Home door:
  the first `E` walks and turns the 3D hero to a clear foot-side segment of
  that edge, holds neutral for one frame and plays the three-second `BedEnter`
  on the continuous rig. A dedicated low seated-pelvis waypoint holds the
  character on the mattress edge with both feet grounded before movement can
  continue between the standing dock and bed centre. The
  deterministic `BedSleepLoop` repeats with the existing breathing holds until
  a second `E` plays `BedExit`; the opening can begin directly in that loop and
  apply its one-shot wake-duration multiplier. Per-sample pelvis alignment
  keeps the sleeper at the authored bed action anchor, with the head at the
  `xMin` pillow. Only a normally completed `BedExit` resets session fatigue;
  cancellation, transition, disable and destroy preserve it. Localized prompts
  and all normal/abnormal cleanup remain;
- one bed-relative low-poly nightstand and 3D alarm clock that remain visible
  as ordinary Home dressing. Its reusable 28-segment display begins the
  one-shot opening at `05:59` and flickers all digits and punctuation briefly
  at long intervals. After a silent five-second input lock it reveals the menu
  without changing the time or starting the alarm. Choosing Wake Up changes
  the display to solid `06:00`, starts the shared session time, generates a
  looping mono `22050 Hz` mechanical ring, rattles visibly and routes its fully
  spatial source through `SFX/World`. From that handoff onward the display
  follows the persistent session hour/minute, including after leaving and
  returning Home. The clock shot and sleeping loop remain fixed for three
  unscaled seconds; the ring then stops and only then does the camera glide to
  the sleeper and smoothly settle into the active Home shot while the existing
  36-frame wake sequence plays over six seconds instead of the ordinary three,
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
  time-of-day light through the window, blending from the existing cold night
  shaft to a warm daylight shaft. A fifth shadowless warm Spot is physically
  co-located with the compact amber lamp above the entrance and casts a local
  pool over the door, wall and floor. These remain capped at five
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
- while the Home fixed-camera controller is active, the same world-oriented 3D
  hero remains visible in MainRoom, Bathroom and Balcony; the shots no longer
  require camera-plane or yaw-billboard modes to preserve a sprite aspect;
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
  interrupted through this debug path. In `City` the same window also owns a
  persistent scene-local test-teleport toggle consumed by the city map;
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
- session-persistent intoxication, stress relief, last-alcohol context and
  consumed-drink count plus the deterministic balance-check delay/sequence;
  alcohol value is independent from price and intoxication. Every beer-pong
  miss consumes a light beer, each Split the G attempt records the actual
  dark-beer fraction and proportional stress relief, cocktails count only the
  alcohol actually served, and only an activated `XXX` in Tinctures in a Row
  immediately consumes `Moonshine` for 24 intoxication; reaching `100`
  terminates the applicable minigame at maximum intoxication without creating
  a separate timed status;
- a session-only cash wallet starting at `$999`, shared by finite supermarket
  stock and a localized physical
  nine-item counter menu in every bar. Interaction glides into a seated
  first-person shot with left/right arm subsets filtered from the production
  player prefab and a full-width row of nine
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
  collider, camera, world-presentation, shadow, input and HUD state without refunding an
  already committed purchase. The whole bottle row remains inside the seated
  shot at 16:9 and 16:10, and repeated orders restore reusable vessels to their
  authored scale. The service camera sits above the counter at natural seated
  eye height, while the green order marker and sign remain hidden across
  repeated orders until the explicit camera return completes.

## Deferred

- Infinite streaming world and floating origin.
- Weather, rain, puddles and volumetric light shafts beyond the implemented
  MVP day/night lighting cycle.
- Player-drivable vehicles, a broader traffic simulation, or skating physics;
  the implemented City-only ambient bus remains route-driven and
  non-interactive, with boarding and live map tracking deferred.
- Multiple bespoke bar interiors.
- Mobile quality/render-profile parity; the current Windows/PC-targeted project
  retains only its PC quality level, render-pipeline asset and renderer.
- Additional bespoke 3D animation polish beyond the current in-place
  locomotion, face, hybrid fall, bed, smoking and cat-feeding action set.
- Minimap, in-world GPS trail, route autopilot, and manual map zoom/pan.
- Sobering mechanics, long-term save data, income/jobs, a broader
  economy, dialogue, quests, combat, save slots, and online features.
- Final bespoke art and audio masters, accessibility, localization coverage,
  and platform release work.
- Split the G Easy/Hard profiles, persistent best scores and streaks.

South City Rollers/Skaters is a design reference only for procedural-world and sprite-character approaches; its code and assets are not present in this repository.
