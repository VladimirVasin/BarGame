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
- one Home-wide F9 debug shortcut, installed even for the locked opening
  `ClockHold`: an accepted press directly loads `City`, starts the session
  clock at `06:00` if it is still frozen, prepares the player-home exterior
  return and sets one scene-crossing map request. `CityGameRoot` waits until
  that transition is complete, enables map test teleport, opens the map and
  clears the request. The ordinary Wake and Home -> Stairwell -> City paths
  remain unchanged;
- one session-owned in-game clock that starts every fresh run frozen at
  `05:59`, advances only after the successful startup Wake or accepted Home
  F9 debug skip sets it to `06:00`, and persists through Single-mode scene
  loads. It advances on scaled time at
  `1.0` game minute per real second, so a full `24 h` cycle takes exactly
  `1440` real seconds (`24` minutes), crosses midnight with a day index and
  naturally pauses wherever gameplay sets `timeScale` to zero. The Home clock
  and inventory Status panel both show its current `HH:MM`;
- a finite, seed-reproducible coastal city driven by one immutable blueprint:
  the default preserves all 144 former road-and-lot cells inside a `13 x 12`
  urban envelope, using the added central column for a north-south river and
  shifting the eastern half one cell outward. It retains the full-width
  northern beach and sea strip. Active cells, roads and surfaces may form a
  connected sparse, non-rectangular footprint inside their map bounds;
- one immutable river contract splits that default urban envelope with a
  `10 m` channel. Two continuous `3 m` promenades flank it; an `8 m` Works
  road bridge and an `8 m` Mouth road bridge carry ordinary Street traffic
  across its south and north edges, while a separate `2.8 m` timber
  ParkPath bridge reconnects the two `2 x 4` halves of the 16-cell central
  park. Each road bridge owns one staircase and lower waterside platform per
  bank, producing four navigable lower landings. River-owned parapets stop at
  the bank-road pads and preserve those four stair openings; generic road-edge
  fences treat bridge decks as support-only and do not duplicate them. Route
  01 may use the road bridges but never the timber crossing;
- one immutable `CityElevationPlan` produced after 2D topology and before any
  lot, surface or access is materialized. The default coastal blueprint spans
  about `8.1 m` across its generated road nodes, peaks near `10.08 m`, gives
  every urban district at least `1.5 m` of local elevation variation, keeps
  the sea at datum `0`, gives the river a monotonic descent into it and places
  the lake in a local elevated basin whose blocked physical shoreline drop is
  about `0.4 m`. `CityTerrainSurfacePlan` is the authoritative sampled top for
  `BuildableGround`, `ParkGround`, `OpenGround` and `Beach`;
  `CityTerrainSurfaceWorldBuilder` turns it into triangulated render meshes and
  matching mesh colliders, eliminating vertical cell-slab seams and giving the
  beach a continuous waterward slope. Declared flat special surfaces and
  legacy/custom blueprints retain their flat fallbacks. Park plazas conform to
  the lawn/path profile, while district public places keep exact flat pads with
  `4 m` blended approaches. Buildings and public slabs extend their foundations
  downward without moving their authored tops. The samplers are authoritative for node,
  cell, road, sidewalk, entrance, return, open-area and debug-teleport height;
- one authored bar-side yard composition in the walkable roadless gap
  immediately left of the bar across the player's home frontage, between the
  bar and its neighbouring supermarket and distinct from the five typed fringe
  `Yard` areas. A worn 24-segment ring surrounds the dead tree and
  carries the pipe-backed wheelchair rider. One fixed cold near-white Spot on
  the supermarket's yard-facing wall runs at intensity `240`, twenty times the `12`-intensity
  street practical. Its bright inner cone contains the complete circuit with
  only `6°` total feather, while range is the greater of `1.5x` the sampled
  throw and sampled throw plus `3 m`. Hard shadows use `0.95` strength and high
  resolution. The HDR lens is boosted `4.8x` and its source halo is larger and
  brighter, but there is no volumetric beam. It stays constant through day and
  night and never tracks the rider, while the old yard lamp remains a dead
  physical trace;
- four signature exterior stair streets, one in Old Town, Residential,
  Industrial and Nightlife. Each owns `6-12` visible collider-free steps at
  `0.15-0.17 m` rise and `0.30-0.34 m` tread, two `1.5 m` landings, physical
  rails/retaining walls and exactly one hidden seam-free ramp collider per
  flight. Road-to-ground and ground-to-ground transitions use sampled endpoint
  heights for the same step-safe decision, and segmented guards follow every
  unsafe physical edge. `CityVerticalTraversalPlan` inventories those seams
  and authored road frontages and proves their authorized component from the
  spawn road. The first version deliberately permits only one walkable surface
  at any XZ projection;
- one shared MVP day/night lighting cycle for City, the Home window and the
  Home balcony exterior: night before `06:00`, smooth dawn from `06:00` to
  `07:00`, day until `18:00`, smooth dusk until `19:00`, then night again.
  It blends directional/ambient/reflection lighting and the bounded City/Home
  exterior night fixtures; the fixed neighbour-wall yard Spot is deliberately
  outside `NightFactor` and stays on. The atmosphere pool remains capped at
  `12` local realtime lights. One active bus may add `4`, the single pooled
  helmet-lamp pedestrian `1`, the yard `1` and the drying yard's night-only
  pole floodlight `1`, for a bounded worst case of
  `19`; the scene Directional and transient lightning Directional are counted
  separately. Bar, Supermarket and Stairwell lighting remain unchanged. The
  `0.070` exponential-squared luminous gray-green fog,
  fog-matched terminal camera backdrop, City-only `48 m` visibility cap,
  `CityFogField` and `CityNoirVolumeProfile` stay fixed across the cycle. The
  Windows player explicitly retains the runtime-only Exp2 shader variant;
- a default `640x360` PS1 world composite with four-tap footprint averaging,
  exact 2x/3x scaling at 720p/1080p, a 35% perceptual-space RGB555 blend
  without a screen-space dither grid, point upscaling and percentage-driven
  intoxication vignette, ghost/chromatic image, warp, warmth and exposure
  pulse; lower `426x240` and `320x180` presets remain available;
- a crisp retro IMGUI layer after the world composite: prompts, HUD and city
  map use a logical `640x360` canvas; persistent key-binding guides and
  control-hint footers are intentionally absent from menus, modal inspectors
  and the map; every active contextual prompt is a full pointer
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
  ground reconstruction;
- eight opaque generated Central Park albedos from
  `tools/build-city-park-textures.py` (trodden turf, the sand-stone walk,
  dirty plaza slabs on a `4x4` joint grid, trunk bark, leaf mass, bench
  timber, jointless park masonry and chipped municipal paint over steel)
  applied by `CityParkSurfaceAppearance` on the shared
  `RuntimePrimitiveLit` through material property blocks. Like the cemetery
  set they ride UVs baked into the batched meshes rather than a per-renderer
  UV transform, so neighbouring trees and hedge runs decorrelate by world
  position: the lawn, paths and conforming plaza discs project down at
  `3.0 / 2.2 / 2.8 m`, while the upright objects — trunks, canopies, hedges
  and bench timbers at `1.2 / 1.6 / 1.4 m` — use the new
  `RuntimeWorldUvMode.BoxProjected` baking, which picks the projection plane
  per face so a twenty-metre hedge shows leaves along its whole length
  instead of one stretched line of the sheet. The park's authored flat
  colours are unchanged and brightened by the solved per-sheet compensation,
  so the tinted sheets keep the brightness the flat surfaces had. The park
  path recipe also covers the bounded Home exterior reconstruction. The
  stone, timber and painted-metal sheets reach the four park landmarks
  inside the otherwise flat city decoration layer: `CityDecorationWorldBuilder`
  batches on a second axis, so a park part keeps the batch colour it always
  had and only adds its sheet, while every other district's decoration stays
  a flat fill. Those batches sit on a chunk-offset transform, so the chunk
  origin goes back into the baked UVs and a landmark split across two chunks
  still tiles as one surface. The fountain's standing water stays flat, like
  the river, the lake and the sea;
- eight district wall albedos plus one shared roof cap, generated by
  `tools/build-city-facade-textures.py` and the first scripted world albedos in
  the project. Two per buildable district carry that district's two material
  axes: Old Town brick and blown render, Residential cool and warm painted
  panel, Industrial sheet and utilitarian brick, Nightlife shopfront and
  service side. Each sheet is authored at `1024` as four bays by four floors so
  Unity's import to `512` is an exact 2:1 downsample and the cell grid stays
  pixel-exact. `CityFacadeAppearance` tiles them by the building's own window
  grid rather than by metres, so one authored cell covers exactly one pane bay
  and one `2.35 m` storey and the baked window band, sill and grime run land on
  the geometry at every lot size; a stable per-lot whole-cell bay and floor
  rotation varies which cell lands where without disturbing that. The sheets
  hold a mean linear luminance of `0.35` and the night facade tint is
  brightened by `1 / 0.62`, which preserves the pre-texture wall brightness
  through URP's linear multiply and never clamps the brightest lot, a bar. A
  deterministic
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
  The pool holds one presentation per registered design — Lampshade Walker,
  Chair Carrier, Kettle Hat Walker, Long-Arm Walker and Helmet Lamp Hopper —
  each with four material-property-block palettes. All five use the compatible Generic Avatar
  and dedicated in-place `Idle`/`Walk` loops: the Lampshade keeps a persistent
  hunch and uneven short step, the chair-burdened walker stays upright with a
  quicker high-knee gait, the stout Kettle Hat Walker moves at `0.90-1.02 m/s`
  on `1.08-1.18x` clips with a constant waddle and counter-phased belly and
  kettle, and the narrow Long-Arm Walker is the slowest at `0.72-0.84 m/s` on
  `0.86-0.94x` clips, shuffling on barely lifted feet while its ground-reaching
  bare forearms swing a quarter cycle behind the legs and never fully settle in
  idle, and the Helmet Lamp Hopper is the fastest at `1.32-1.48 m/s` on
  `0.94-1.06x` clips, never taking a step at all: it coils on `0.46 m` hind
  feet and covers ground in two-footed bounds with a proven `0.24 m` apex. It
  is also the only walker allowed a working light — one always-on shadowless
  `7.5 m` Spot bolted to its miner's helmet and parented to the animated head
  bone, capped at one instance because the pool holds a single hopper. Every
  design keeps the shared `1.75 m` envelope and the fixed `1.7 m`
  collider: the kettle walker's overhanging belly, tiny legs and oversized
  skewed kettle carry its short read, because a genuinely shorter walker would
  need its own collider parameterisation. A seeded choice among free
  presentations fills one slot per event, so the pool is twice the two-actor
  cap and concurrent walkers keep distinct silhouettes while repeat encounters
  vary the pair. Home transforms that same graph into its local exterior,
  retains a bounded `100 m` fog-hidden approach context beyond the facade while
  rendering its existing `48 m` street slice, and runs the slots only while the
  Balcony shot is active;
- one route-driven ambient midibus with a strict single-slot cap. The production
  model uses its real `8.25 x 2.38 x 2.95 m` body and `4.5 m` wheelbase rather
  than a hidden gameplay scale, and exposes a modeled driver area, twelve
  passenger seats, rails, dashboard, two animated double-leaf doors, rolling
  wheels and front steering. Each door keeps its outer posts fixed while its
  two independently hinged leaves fold inward around the bus vertical. The
  separate passive `CityBusDriver3D` uses the shared `Player3DLit` material, an
  exact 31-bone rig, a normal low-poly head and long horizontal eyes.
  Procedural seated IK keeps both hands on the rotating wheel grips. For each
  door command, a deterministic timeline moves only the right hand to the
  dashboard button with `12 mm` travel while the left stays planted, and turns
  the driver toward the front door for the open-door hold before returning
  during closing. The long eyes blink on a deterministic cycle. A nearby hero
  on the outside of the front entrance takes focus priority: the driver tracks
  the hero's real head while the neck/head segment extends by up to `0.10 m`
  with a `1.35x` cap, then restores its exact base scale when the hero leaves. A
  runtime-only sprung presentation pivot gives the moving body speed-scaled
  cartoon heave up to `0.045 m`, acceleration/road pitch up to `0.8` degrees
  and steering/road roll up to `1` degree. All four wheel assemblies stay
  outside that pivot and grounded while the actor, collider and route pose
  remain unchanged. Canonical Route 01 is an immutable right-hand, Street-only
  closed winding service loop. Its target planner orders every district point
  of interest that actually exists, followed by `PlayerHome`; the default
  sequence is Industrial, Nightlife, Residential, Old Town and Home. It assigns
  one safe straight to every target. Home stays on its frontage or one connected
  road edge away. In the river layout a district POI may use the nearest
  same-district cyclic Street up to five grid edges and `120 m` from its public
  access; this bounded fallback keeps the two-bridge service loop closed without
  moving the Home stop. The roadside pole remains outside the POI public/access
  bounds or Home footprint. Full-body-clear ordinary
  straights and proven `6 m`-radius left turns enter the loop. At selected Road
  v2.1 nodes only, a clearance-proven two-edge right-turn macro uses a long
  S-merge across the full incoming Street, a `4.5 m` quarter-turn in the clear
  core and a symmetric S-return across the outgoing Street. The macro owns both
  physical edges, so a connector cannot use it to bypass a selected stop edge;
  ordinary tight `3 m` right turns remain rejected. A physical street link may
  recur in a connector, but every ordered occurrence receives a unique route
  link/node ID. Route selection has no random branch or player pursuit. The
  default five semantic stops each have a physical blue `01` pole and are served
  once per lap by that deterministic door/driver timeline with a fixed `10 s`
  total dwell, including `0.70 s` opening and `0.70 s` closing transitions for
  both doors. Random roadside decoration does not emit bus shelters.
  Nightlife's last-route island now has a working pole
  nearby but outside its public ground and approaches, leaving the abandoned
  island structures distinct from the live stop. A pooled actor prefers
  obstacle-safe fog-hidden route poses `76-86 m` from the player and falls back
  to `56-86 m` only when forward travel on the same loop can approach the
  player. The cap means at most one bus can be active or potentially visible
  rather than guaranteeing that one is always on screen. While the hero is
  outside, it yields to the player and pedestrians and recycles only when its
  full body is at least `92 m` away. While the hero is attached as a passenger,
  the director omits that hero from bus-obstacle prediction and prevents
  recycling or slot release, while pedestrian yielding remains active.
  Suspension, wheel/steering articulation, button travel, driver hands/look, the
  door timeline, a synthesized engine loop and night-scaled head, tail and cabin
  emission reset with the pool. Two
  shadowless runtime headlight Spots illuminate the road ahead and two soft
  downward cabin Spots light the interior; the shared night factor scales all
  four and pooling switches them fully off. Camera direction and frustum
  membership never participate in the lifecycle. At either fully open
  passenger door, the standard localized E/Enter/gamepad/pointer prompt begins
  the passenger MVP. The controller chooses the closest valid front- or
  rear-door exterior dock and records that door-specific transfer; the bus
  holds its dwell while the visible three-second `BusBoardEnter` action carries
  the hero through the selected live doorway and aligns the ordinary rig's
  pelvis to window seat `07`, the first fixed anchor on the side opposite the
  driver. The controller then keeps
  the gameplay root in its original hierarchy, disables ordinary locomotion,
  collision and the contact patch. After each bus update it late-synchronizes
  the root's world pose to that actor-local seat, keeps the two-second
  `BusRideLoop` aligned to the live seat and owns a seat-relative seated camera.
  Its position follows suspension, but its world-level horizon and independent
  yaw/pitch axes do not inherit body roll. The default aisle-side pose looks
  through the nearest window, while
  the same RMB mouse input and gamepad right stick as ordinary orbit control
  rotate a bounded yaw/pitch view in place inside the cabin. The exit prompt is
  withheld until
  `ServiceOrdinal` exceeds the boarding value, so it is available at the next
  or any later stop. A second service hold keeps the doors open while the
  three-second `BusAlightExit` uses the same selected live doorway waypoint and
  an independent validated grounded roadside exit pose, then blends back to the
  ordinary chase camera. Entry and exit root heights are resolved from the same
  deterministic street-surface plan that builds the physical raised sidewalks
  and flat bus aprons.
  Normal completion and cancellation, scene teardown or forced actor cleanup
  restore the player motor, `CharacterController`, contact shadow and camera
  exactly once without mutating its hierarchy, with a safe exterior fallback
  when needed. This passenger runtime remains City-only and uses the first
  fixed opposite-driver seat; it has no fare, destination picker, NPC
  passengers, passenger
  persistence or live map marker.
  Home's bounded exterior regenerates
  the same route plan and reconstructs the nearby Home stop as a static
  collider-free pole, but it has no bus actor or director: no real Street
  pass-through offers both complete-body seams at or beyond the fog-hidden
  `56 m` boundary, and the default facade faces a visible road terminal. A
  fabricated continuation or
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
  door latch and sustained hinge creak, with bounded
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
  access edges and exactly three declared river crossings;
- four readable built areas—Old Town, Residential, Industrial and
  Nightlife—plus a fixed 16-cell central park split into two `2 x 4` regions
  with lawn, plazas, trees, benches and hedges. A dedicated timber footbridge
  joins their ParkPath graph across the river;
- one mandatory north-edge waterfront in the default blueprint: its connected
  beach has a deterministic street approach and remains walkable to the water
  line, while the continuous northern water row is rendered and mapped but is
  excluded from player navigation and night-fixture placement;
- reusable Lake and Cemetery non-urban profiles that are now present on the
  default city's eastern edge. The `4 x 4` lake has a walkable shore around a
  blocked `2 x 2` water center, while the `3 x 2` cemetery is walkable ground;
  each requires one street-linked open-area approach and exposes the same data
  to world, fence, navigation, map and deterministic landmark consumers. The
  cemetery is dressed by its own dedicated plan: a textured gravel alley
  network from an arched, leaf-open iron gate, hash-varied graves in six
  monument silhouettes (stele, arched stone, Orthodox cross, obelisk, family
  monument, overgrown mound) over three stone tints with оградка enclosures
  on grounded corner posts and offerings, perimeter birches and firs,
  sittable timber benches beside the main alley at the cross alleys and its
  far end (they join the shared bench-sit pass through the world result's
  cemetery plan), and a chain of night-scaled gas lamps walking the whole
  main alley on alternating sides (a pair at the gate, one roughly every
  `15 m`, one at the far end); ground, alleys, mounds and stone carry
  dedicated `CityCemetery*Albedo` sheets;
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
  repeated neon. Four scripted opaque POI albedos from
  `tools/build-city-poi-textures.py` (yard paving slabs, painted metal,
  laundry cloth, worn timber) ride the shared `RuntimePrimitiveLit`
  through `CityPointOfInterestSurfaceAppearance` property blocks with the
  same linear albedo compensation as the facade/home/supermarket sheets:
  all four public grounds are paved, and the drying yard's frames, lines,
  hanging simulated wash (which keeps its shared two-sided cloth material
  and matte specular), bench and fixtures are fully textured. The drying
  yard also carries the one authored POI realtime light: a communal
  floodlight on its own `4.3 m` pole at the street-side corner opposite
  the shared bench, washing all three drying frames with a cold
  near-white shadowless `72°` Spot (range `16`, night intensity `150` — the
  7-12 m throw needs floodlight wattage for the far row to reach
  street-lamp brightness through the night grade and fog) plus a
  fog halo and a dead-by-day emissive lens; `CityNightSiteLightRegistry`
  scales and disables it with the shared night factor, its lower pole
  owns a focused collider outside every access approach, and the Home
  vista rebuilds only the pole/head/lens geometry without a light. On the
  yard's west strip, upwind of the wash, stands the Soviet carpet-beating
  rack — painted-metal posts and crossbar with two hung carpets textured
  by the shared Home rug albedo. In the city each carpet is real
  simulated cloth pinned over the bar (heavy: stiff and damped,
  deliberately outside the laundry's weather-wind registry; the vista
  keeps static boxes), and the yard's three authored staged NPCs work
  around it: two babushka grandmothers beat those carpets from opposite
  sides with the forward-biased bright plastic beater (one shared
  `yard_babushka_v1` model, desynchronized `BabushkaBeat` loops at
  different speeds and phases), each strike driving a short
  deterministic acceleration pulse through exactly the carpet she
  faces via `CityDryingYardCarpetRegistry`; the third strolls a
  cloth-free corridor between the rack and the drying frames back and
  forth past the beaters at `0.36 m/s`, gesturing emphatically on the
  four-step `4 s` `BabushkaSmoke` loop with one cigarette drag per
  lap, turning smoothly at the corridor ends. They follow the rider's
  staged contract: authored with the shared pedestrian art library,
  outside Resources and the pool, colliderless with attention magnets,
  spawned from the same POI descriptor transform as the drawn carpets
  so they land inside the drying yard of any generated city. These
  authored recipes require both
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
- 144 land-use lots in the default urban core, including 16 park cells and
  4 open district points of interest, plus 34 northern beach/water surface
  cells, 16 lake cells and 6 cemetery cells. The core still contains exactly 4 reachable bars in four different
  stable urban area IDs and one non-bar player home beside one bar street,
  plus exactly one ordinary
  street-front supermarket. Its deterministic selection prefers Residential,
  then the shortest traversable route from the home, without consuming a bar,
  public-place or primary-landmark lot. Every bar pair is at least `120 m`
  apart by traversable graph distance, while stable row-major order assigns
  each bar its interior activity flavour (dressing only — the minigames
  themselves are cut);
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
- in City, BarInterior and ordinary Supermarket play, a very close freely
  orbiting perspective third-person chase camera with
  `2.6 m / 53°` exterior and `2.2 m / 57°` interior framing, deliberately
  raised `1.4 m / 1.3 m` focus points that keep the hero in the lower frame,
  weighty yaw/pitch/focus damping, a player-controlled vertical orbit bounded
  to `-20°..55°`, bounded focus lag, teleport snapping, subtle deterministic
  idle/walk motion and smoothly recovering obstacle-aware distance. RMB mouse
  motion and the gamepad right stick drive both orbit axes in City, Bar and
  ordinary Supermarket play; cinematic motion fades out for fullscreen modals,
  while the balance-specific lock keeps its intoxication and fall reactions
  visible;
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
  beach, sea, river, promenade, lake-shore and cemetery surfaces and paths,
  plus separate map treatments for the Works, Mouth and timber bridges,
  player/bar markers,
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
  test teleport enabled through the City F9 toggle or the Home F9 arrival,
  every map lot becomes selectable,
  the side panel asks for an explicit confirmation and a
  confirmed target moves the hero to that lot's street-front return point or
  its nearest generated route when no frontage edge exists.
  Keep at least `22` logical pixels per map cell; clip overflowing content and
  pan it independently on X/Y with WASD, the right stick, mouse-wheel gestures
  or middle/right-button dragging while drawing scroll indicators only for
  overflowing axes. Ordinary ordered route editing and deterministic shortest paths
  remain unchanged outside this debug mode and are constrained to the generated
  road graph;
- localized RU/EN interaction prompts whose pointer, keyboard and gamepad
  activation share one action path;
- guarded asynchronous transitions and persistent blueprint ID,
  seed/bar/route context for the current city, with an explicit
  bar/home/supermarket return
  kind, a separate stairwell-arrival side and a consumed
  `Normal`/`OpeningSleep` Home arrival value and a resettable one-shot
  `DebugCityMapOnArrivalRequested` flag;
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
  ImageGen albedos (the city facade set is scripted instead) under
  `Resources/Stairwell/Textures` cover wall paint,
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
  checkout staffed by the Watcher Cashier — a bespoke animation-free 3D
  clerk on the shared 31-bone Avatar whose five-segment neck lies along a
  pursuit curve: his body never leaves the register, but the head travels
  the whole hall — up to `18 m` of neck — to hover beside the hero,
  the sample-verified curve climbing over any shelf or counter in the
  way; he snap-retracts with hysteresis when
  the hero turns to look (pupils pinched, blinking suppressed) and
  otherwise blinks once per `6.5 s`; a
  separate `E — talk` stub answers with a placeholder stare. Four chunky
  ceiling-corner CCTV units servo their lenses after the hero from the
  first frame (fake-emissive recording LEDs, no colliders, no real
  lights), and the hall runs on a fluorescent light budget: six
  shadowless practicals under the directional fill — one per tube row, a
  warm checkout accent, a cool cold-shelf spill — with one row
  flickering deterministically. The checkout
  is not a purchase station: activating a shelf opens its authored fixed product view centered on
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
- a lived-in bathroom: a rebuilt shower stall (folded curtain on an
  L-rail, mixer, hose, tilted head, soap shelf) and three modal scenes on
  one shared skeleton — a tactful toilet privacy cut with an off-frame
  flush, a curtained shower with water, steam and a crossfaded hiss loop,
  and a mirror teeth-brushing close-up shot from the mirror plane with a
  procedural brushing arm, foam and a rinse; toilet/shower relieve
  stress on completion, brushing once per game day;
- 3D bar patrons drawn from the same pooled city pedestrian prefabs: guests
  sit in booths through the shared seated-ride contract and stand at tables on
  the deterministic layout anchors with per-anchor palette variants, idling
  through the shared pedestrian presentation; the bartender anchor stays
  deliberately empty until a dedicated 3D bartender pass;
- a scene-local spatial crowd bed plus rare glass/chair cues consume their
  layout radius/gain data and coexist with the existing bar music and
  procedural ambience inside a four-source budget;
- one exit and one ordinary-drink counter station remain authoritative; the
  activity fixture (beer-pong table, stage) survives purely as layout
  dressing. The bar-visited mechanic is removed entirely: the map route is
  edited only by hand and entering a bar changes nothing about it;
- an `F9` debug window in both `City` and `BarInterior`; opening it closes a
  conflicting map before taking the modal lock; clickable controls or the
  Left/Right arrow keys change the session
  intoxication by `-20/+20`, clamped to `0–100`, without changing the
  last-drink or consumed-drink context; a committed physical drink service
  cannot be interrupted through this debug path. In `City` the same window
  also owns a
  persistent scene-local test-teleport toggle consumed by the city map. In
  `HomeInterior`, where that window is not installed, F9 instead uses
  `HomeDebugCityMapShortcut` to bypass Home and Stairwell and arrive beside
  the home with the debug-teleport map already open;
- bounded structured session diagnostics in `debug.log`: stable NDJSON
  envelopes correlate scene transitions, generated-city/bar/home initialization,
  route state, drinking and balance outcomes, plus
  Unity warnings/errors; `F8` writes an immediate state snapshot and
  `Shift+F8` opens the log directory;
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
- Weather beyond the implemented deterministic
  clear/light-rain/heavy-rain/thunderstorm schedule with its rain field, rain
  bed, lightning flashes and thunder: puddles, wet surfaces, weather-driven
  ambient lighting or grading changes, wind-driven debris and volumetric
  light shafts.
- Player-drivable vehicles, a broader traffic simulation, or skating physics;
  the implemented City-only Route 01 passenger MVP remains route-driven and
  limited to one fixed seat. Fares/payment, destination selection, NPC
  passengers, passenger persistence and live map tracking are deferred.
- Multiple bespoke bar interiors.
- Mobile quality/render-profile parity; the current Windows/PC-targeted project
  retains only its PC quality level, render-pipeline asset and renderer.
- Additional bespoke 3D animation polish beyond the current in-place
  locomotion, face, hybrid fall, bed, smoking, cat-feeding and bus-passenger
  action set.
- Minimap, in-world GPS trail, route autopilot, and manual map zoom/pan.
- Sobering mechanics, long-term save data, income/jobs, a broader
  economy, dialogue, quests, combat, save slots, and online features.
- Final bespoke art and audio masters, accessibility, localization coverage,
  and platform release work.
- Bar minigames: the original four sprite minigames are cut entirely; any
  future bar activities start from a new design.
- A dedicated 3D bartender (planned next pass; the supermarket cashier
  shipped as the Watcher Cashier).

South City Rollers/Skaters is a design reference only for procedural-world and sprite-character approaches; its code and assets are not present in this repository.
