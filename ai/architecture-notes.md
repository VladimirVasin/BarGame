# Architecture notes

Decisions marked `Proposed` become accepted only after implementation confirms them.

## Current facts

- **Accepted:** Unity `6000.5.5f1` with URP `17.5.0`.
- **Accepted:** New Input System is enabled.
- **Accepted:** Gameplay and transition presentation are composed at runtime
  in seven explicit build scenes.
- **Accepted:** City, Bar, Supermarket, Home and Stairwell instantiate one
  `Resources/Player/Player3D` modular hero prefab through `PlayerFactory`.
  Its Generic rig, independent mesh parts, in-place Actions, prefab-derived
  first-person subsets, dedicated 3D portrait, real mesh shadows and analytic
  contact patch are the active player presentation. A runtime-composed
  13-body companion ragdoll temporarily owns those same bones during failed
  balance falls; no alternate hero or renderer swap is used.

## MVP decisions

- **Accepted — Data-first generation:** A pure `CityLayout` is validated before GameObjects are created.
- **Accepted — Stable local randomness:** Road stages and lot coordinates use stable hashes; Unity global random state is not used.
- **Accepted — Finite connected graph:** Kruskal-style spanning tree plus deterministic optional loops.
- **Accepted — Stable blueprint identity over position:** An immutable
  `CityBlueprint` owns stable area IDs, category, reusable visual archetype,
  placement policy and per-cell topology. Movable urban areas may swap cell
  sets without changing roads; `CityDistrictKind` remains a presentation and
  legacy-system archetype, not canonical area identity. The session persists
  one blueprint ID and seed shared by City and Home.
- **Accepted — Anchored coastal sparse city:** The default blueprint keeps a
  connected `12 x 12` road-and-lot core with four urban areas and a fixed
  central `4 x 4` park, then adds a full-width walkable northern beach and a
  continuous non-walkable water row. The playable default also extends east
  with a `4 x 4` lake (walkable shore around blocked water) and a `3 x 2`
  cemetery; both use the shared open-area street-access contract and own
  deterministic runtime-composed landmarks. Roads, ground, navigation and map
  drawing consume only active cells, so connected holes and non-rectangular
  outlines remain real voids.
- **Accepted — Graph-separated accessible bars:** Buildable lots get street
  frontage and bar return points are validated against it. The default four
  bars occupy different urban districts and every pair is separated by at
  least `120 m` of weighted street/park-path travel rather than straight-line
  distance.
- **Accepted — Bar-adjacent player home and fresh spawn:** With at least one
  generated bar, one non-bar building lot becomes the player home. Selection
  first prefers a residential lot across the selected bar's actual frontage,
  placing the default fresh spawn `13 m` from their shared street approach.
  If that placement is unavailable, the deterministic fallback still validates
  a maximum traversable approach distance of `48 m`. The default home mass is
  `8.8 m` tall and its City facade uses the shared third-floor
  balcony/window/door geometry. A fresh city starts on that home's frontage
  node. Bar-free custom layouts retain the central-road fallback and no home.
- **Accepted — One deterministic city supermarket:** After bars, the player
  home, district public places and primary landmarks are reserved, the default
  layout chooses exactly one remaining street-front building lot. Selection
  prefers Residential, then the shortest traversable route from the home, then
  a stable seeded rank. The supermarket cannot also be a bar, home, public
  place or primary landmark; a tiny custom layout may omit it when no ordinary
  eligible lot remains. Its branded storefront, walkable apron, interaction
  trigger, fence opening and return point derive from the canonical lot and
  frontage data.
- **Accepted — Data-driven indexed walkable mask:** Player motion is
  constrained to a spatially indexed union of XZ streets, park lawn,
  blueprint `OpenLand` and the complete logical `BuildableGround` regions.
  Radius-safe connector rectangles overlap road-to-ground and adjacent
  ground-to-ground seams for the maximum `0.35 m` agent radius. Water,
  unmapped cells and space outside the active footprint remain excluded;
  buildings and other visible obstacles are governed by their physical
  colliders instead of being carved out of the macro walkable mask.
- **Accepted — Road v2 deterministic street corridor:** The canonical default
  street footprint is `8 m`, so `CityStreetSurfacePlanner` partitions every
  ordinary street into a dark `6 m` carriageway and two `1 m` sidewalks raised
  from the `0.08 m` road top to `0.14 m`; ParkPath remains independent. The
  default grid step is therefore `26 m` for an `18 m` block. The pure plan also
  owns center dashes, an `8 x 8 m` intersection core with a clear `6 x 6 m`
  carriageway apron, intersection corner pavement, radius-query rectangles and
  four-stripe zebra approaches. One shared stable selector chooses at most six
  degree-3+ Street-only intersections that are clear of ParkPath and blocked
  public space, so paired signals and crossings cannot drift. Dashes are
  excluded from intersection and zebra bounds. City builds physical sidewalk
  surfaces in `48 m` chunks; Home consumes the same plan in local space without
  collision. Generation settings reject widths that leave no positive
  carriageway between the two sidewalks. Asphalt, sidewalk and white paint use
  three packaged albedos through MPBs on the shared Lit material. Road v2.1 adds
  a second stable selector for eligible Street-only perpendicular two-way
  corners and three- or four-way nodes whose four setbacks have supported,
  building-free ground. At those nodes the four `1 m` corner pads move outward,
  the complete `8 x 8 m` asphalt core is exposed and raised curbs stop `4.5 m`
  short on every real approach, where the pedestrian line continues across a
  flush shared apron. A Road v2.1 node may also own the flat zebra paint and
  paired signal fixtures. Every retained bus maneuver samples its inflated body
  against both actual signal positions with a conservative `0.30 m` fixture
  radius; overlap becomes `StaticFixtureOverlap` and rejects that maneuver.
  Pedestrian corner links follow the displaced pads and the Home reconstruction
  includes every surface whose bounds touch its retained road slice. Ordinary
  intersections retain their `6 x 6 m` clear apron.
- **Accepted — One passive real-scale ambient bus on canonical Route 01:** City
  layout produces immutable `bus-route:default-coastal:route-01`, one
  deterministic right-hand, Street-only closed winding service loop. The target
  sequence contains every district point of interest that actually exists and
  then `PlayerHome`; the default is Industrial, Nightlife, Residential, Old
  Town and Home. Each semantic stop is assigned to a safe straight on the
  target frontage or one connected road edge away. Its roadside cell differs
  from the target cell, a POI stop remains in that district, and the physical
  blue `01` pole stays outside POI public/access bounds or the Home footprint.
  The selected target straights are connected through one deterministic
  accepted-link graph. Ordinary straights and analytic `6 m`-radius left turns
  are retained after full-body surface and signal-fixture clearance. At selected
  Road v2.1 nodes only, a two-edge safe-right macro starts at the incoming
  Street departure, uses a long symmetric S-merge toward the centerline,
  follows a `4.5 m` quarter-turn through the clear core and uses a second long
  symmetric S to the outgoing Street arrival. The macro marks both physical
  road edges occupied so pathfinding cannot use it to bypass a selected stop
  edge. Ordinary unselected `3 m` right turns remain rejected as
  `CurvatureTooTight`. Every retained path is sampled at `0.1 m` against the
  inflated envelope of the real `8.25 x 2.38 x 2.95 m` body, `4.5 m` wheelbase
  and `0.1 m` clearance margin. A physical link may recur in a connector, but
  every ordered route occurrence receives unique link/node IDs and exactly one
  successor. Runtime never chooses a random branch or steers the route toward
  the player.
  The default layout owns five target-derived stops with stable IDs,
  localization keys, lap distances and roadside poses; random roadside
  decoration does not emit bus shelters. The actor serves every stop once per
  lap, clears its service set at the loop seam and holds a fixed `10 s` total
  dwell, including the existing `0.70 s` opening and `0.70 s` closing
  transitions for both doors. Nightlife's Last Route Island has a working
  Route 01 pole nearby but outside its public ground and approaches. The
  abandoned island structures remain a distinct place rather than becoming
  the live pole.
  Runtime owns exactly one reusable actor/model slot. Obstacle-safe activation
  prefers the fixed-fog `76-86 m` band and falls back to `56-86 m` only when
  forward travel on the same loop can approach the player; no spawn is accepted
  when the route is directed away from every encounter sample. Recycling waits
  until the closest point of the complete oriented body is at least `92 m` from
  the player. The slot cap means at most one active or potentially visible
  vehicle, not a guarantee that a bus is always visible. It yields to predicted
  player and pedestrian motion and never uses camera direction, frustum
  membership or far-clip state for spawning or recycling. The kinematic box
  body uses the dedicated `CityBus` layer: it collides with the player and
  pedestrians, ignores another bus and is excluded from camera and interaction
  queries. The passive production prefab stays collider- and `Light`-free, owns
  a modeled driver area, dashboard, twelve passenger seats, rails and two
  double-leaf doors, and matches the same real dimensions without runtime scale
  correction.
  Each doorway keeps its outer posts fixed while independently hinged leaves
  rotate in opposite directions around the vehicle vertical and fold inward.
  Presentation inserts one runtime-only `Suspension Visual` pivot above the
  imported body and keeps all four wheel assemblies outside that sprung
  hierarchy. Travel distance and speed drive cartoon road heave capped at
  `0.045 m`, acceleration contributes pitch capped at `0.8` degrees, and
  steering plus the road wave contribute roll capped at `1` degree. The wheels
  remain grounded, while the authoritative actor transform, kinematic collider
  and route pose stay unchanged. Presentation also articulates steering and
  wheel travel, synthesizes a `22050 Hz` engine loop, and scales head,
  tail/brake and cabin emission with motion and current night factor. At runtime
  only, it creates two shadowless headlight `Spot` lights and two soft
  shadowless cabin `Spot` lights under the sprung body. The shared `NightFactor`
  scales them, disables them at zero and resets them off with the rest of the
  pooled presentation. These four bus-owned lights sit outside the fixed
  12-light city-atmosphere pool, so one active bus keeps the exterior bounded at
  16 realtime lights. The City map
  consumes the same immutable plan, simplifies its closed geometry, draws a blue
  ink-outlined loop below the orange player itinerary and adds five numbered
  localized stops in the default layout plus a compact legend. It deliberately
  has no live bus marker. The bus has no prompt, boarding, persistence or traffic-signal
  simulation. Its runtime scope is City-only. A valid Home/Balcony route would
  require a real Street pass-through whose two complete-body seams both lie at
  or beyond the fog-hidden `56 m` boundary; none exists, and the default home
  facade faces a visible road terminal. Extending that road only for
  presentation would falsify the generated city, while enabling or pooling a
  bus with the Balcony camera would create a visible camera-dependent pop and
  violate the lifecycle contract. Home therefore keeps its pedestrian exterior
  runtime and reconstructs the nearby Home stop as a static collider-free pole,
  but composes no bus actor or director.
- **Accepted — Local player-relative street pedestrians:** City layout and session
  seed produce one immutable, radius-safe graph over sidewalk lanes, junction
  turns and explicit three-link zebra connectors. Recursive 2-core pruning
  removes every reachable dead-end branch before runtime. Long sidewalk links
  expose deterministic spawn anchors; two reusable actor/presentation slots are the
  complete runtime population. A director-local runtime random stream mixes
  plan seed, activation time and instance identity, then independently samples
  candidate rank, motion/palette values and every delay. The first event waits
  `1.25-7.5 s`; each later slot or replacement waits `3.5-12.5 s`, and one
  event activates at most one slot. Failed searches retry after a randomized
  `0.8-2.4 s`. A slot may activate only at a unique, obstacle-safe anchor
  in a preferred `76-86 m` band from the player. With City's fixed `0.070` Exp2 fog, the inner edge
  retains less than `0.2%` scene transmittance even at the widest production
  `70-degree` 16:9 frustum corner after a conservative combined `6 m` camera
  and full visual-envelope depth offset. When that narrow band offers only
  disconnected sidewalk components whose closest point remains outside the
  `24 m` encounter radius, selection falls back to a connected obstacle-safe
  anchor at `32-86 m`; the nearer bound remains inside dense fog. It remains active
  regardless of camera direction or frustum membership and returns to the pool
  only after crossing beyond `88 m` from the player. To prevent both daytime
  slots remaining occupied by invisible actors, each new walker temporarily
  follows the eligible non-backtracking continuation with the shortest physical
  graph distance to the nearest player-side node in its connected component
  until it first enters a `24 m` encounter radius. This guidance
  then stays disabled for that spawn so ordinary seeded roaming resumes instead
  of turning into pursuit. Player-distance-only simulation acceleration rises
  smoothly from authored pace at `32 m` to
  `2.75x` at and beyond `76 m`; this advances both inward approaches and
  outward recycling without reintroducing a camera dependency. Strict night is
  before `06:00` and from `19:00`: it keeps authored simulation pace, allows
  only one new population slot, waits
  `15-35 s` for the first event and `30-70 s` for a replacement, and retries
  failed searches after `4-10 s`. A second walker already alive at dusk is not
  culled by the clock and leaves only through the same distance rule. Actors
  never reverse at artificial route endpoints: they continue through graph turns,
  avoid immediate backtracking and make one seeded 50% cross/don't-cross
  choice when passing each zebra entry. The one `1.75 m` lampshade-hood model
  copies the production Generic Avatar contract and directly references the
  hero's looping in-place `Idle` and `Walk` clips. Its `CharacterController`
  becomes physical only after a successful spawn and presentation bind, and
  is disabled before pooling. The dedicated `CityPedestrian` layer collides
  with the player but not other pedestrians; camera collision and interaction
  queries ignore it. Stable slot order owns head-on yielding. Walkers retain no
  prompts, persistence or gameplay reactions, and their four muted palettes
  use the shared material through property blocks. Home transforms this same
  graph and navigation mask through `PlayerHomeBalconyGeometry`, filters spawn
  anchors to a bounded `100 m` fog-hidden approach context wholly beyond the
  facade while retaining the existing `48 m` rendered street slice, and enables
  the director only for the Balcony shot as a Home scene-composition boundary.
  The director itself has no camera dependency, while vertical capsule overlap
  keeps the player four storeys above from blocking walkers on the street below.
- **Accepted — Logical terminal road boundary:** A pure planner retains rails
  only along street-union intervals whose outward side is water, unmapped or
  outside the active non-water footprint, plus full-road-width caps at true
  degree-one Street terminals. Degree counts both Street and ParkPath edges,
  so a street continuing into the park is never mistaken for a dead end.
  Existing entrance/gate/public/open-area opening descriptors remain metadata
  for decoration clearance. Runtime rails own combined `MeshCollider`s while
  narrow posts remain visual-only; both stay batched in `48 m` chunks.
- **Accepted — First-class open district points of interest:** City land use,
  not the late visual-decoration pass, owns public places. After bars, the
  player home and one buildable primary-landmark cell per urban district are
  reserved, `CityLayoutGenerator` deterministically chooses at most one other
  street-connected lot in each district. It prefers more street sides, then
  greater separation from the district's primary landmark, then a stable
  seeded rank. The default layout yields four: Old Town's waterworks court,
  Residential's drying yard, Industrial's weighbridge and Nightlife's
  last-route island. The authored recipes require both `BlockWidth` and
  `BlockDepth` to be at least
  `CityLayoutGenerator.MinimumDistrictPointLotDimension` (`18 m`); a smaller
  custom layout omits all four safely, and a compact eligible district may
  still omit its place when no safe candidate exists. Each
  `CityDistrictPointOfInterestDescriptor` is the
  canonical stable ID/cell/kind/public-bounds/access contract. Its matching lot
  has `CityLandUseKind.DistrictPointOfInterest`, has no building and cannot be a
  bar, home, park cell or primary landmark. `RoadWalkableArea` includes its
  active ground and approach rectangles, while `RoadFencePlanner` treats the
  complete non-water public surface as support and emits no street-side rail;
  `CityNightFixturePlanner` excludes lamps and signals from the reserved
  ground/approaches. A dedicated world builder creates the
  physical paving, free-standing recipe and intentional solid colliders; the
  bounded Home exterior rebuilds nearby descriptors through the same
  world-to-local transform without gameplay colliders. The last-route island
  owns no emissive recipe parts: its previously unsupported departure board
  meets the paving through two visible legs and feet, while dull route plates,
  layered posters, a waste bin, bottles, a discarded timetable and a lost
  scarf replace the repeated neon-strip treatment.
- **Accepted — Data-first seeded city decoration:** `CityDecorationPlanner`
  consumes the validated layout plus fence and night-fixture plans and emits a
  stable ordered plan without using Unity global random state. Every ordinary
  building lot receives exactly one district silhouette/facade descriptor;
  supermarket and public-place lots are excluded because they own a dedicated
  facade or have no building. Every urban
  district retains one primary building landmark, and the enabled park still
  receives a fountain/statue plus bandstand.
  Optional frontage, roadside and park clusters use per-kind footprint
  clearances around entrances, gates, lamps, signals, trees and benches. The
  ordinary random roadside selector deliberately omits `RoadsideBusShelter`;
  visible Route 01 poles are built from the bus plan, so stop identity cannot
  drift away from service. The 24-family catalog retains the legacy shelter
  recipe for compatibility. Recipes orient to actual road frontage and expand
  as light-free, shadowless visuals in at most six shared-material batches per
  `48 m` chunk.
  A per-kind `None`/`Detail`/`Blocking` catalog adds one to four simple box
  proxies only for grounded structural or bulky recipes; rooftop, hanging and
  small narrative details remain non-physical. Park benches and hedges, the
  home mailbox, plus the lower sections of lamp and signal poles have focused
  static proxies. Home regenerates and filters the same visual descriptors
  after its world-to-local transform and exterior half-space clip, but its
  bounded exterior reconstruction deliberately creates no gameplay collision.
- **Superseded 2026-08-04 — Sprite physical/visual split:** The
  `CharacterController` still stays on the player root, but the former
  camera-facing nine-renderer sprite child has been replaced by the
  collider-free modular 3D prefab and `IPlayerPresentation` contract.
- **Accepted — Explicit scene allow-list:** Only `MainMenu`, `City`,
  `DoorTransition`, `BarInterior`, `SupermarketInterior`, `HomeInterior` and
  `StairwellInterior` install their matching roots. Directly opening
  `DoorTransition` installs an idle presentation root; only the transition
  service initializes and plays it.
- **Accepted — Black startup boundary and one-shot Home opening:**
  `MainMenu` is build index `0` and owns only a black launch camera. After one
  frame it resets the complete run, prepares `HomeArrivalKind.OpeningSleep`
  and Single-loads the existing `HomeInterior`. Home consumes that value once,
  starts the existing bed interaction directly in its sleeping loop, captures
  modal input and holds the first rendered Home frame on a silent `05:59`
  clock. Its complete display flickers briefly at three-second intervals.
  For five seconds no menu choice or input path exists; the localized
  PS1-style Wake Up/Quit menu then appears without changing the silent,
  flickering `05:59` display or leaving the clock shot. Wake Up alone switches
  the clock to solid `06:00`, starts the session clock and mechanical ring,
  and hides the menu.
  The camera and persistent sleep loop hold for three more unscaled seconds;
  only when the ring stops does the existing 24-frame exit begin with a `3x`
  duration multiplier (`6 s` instead of the ordinary `2 s`). The camera then
  glides to the sleeper along a `2.25 s` smootherstep quadratic path and eases
  continuously into the active Home shot. It reaches that gameplay pose
  before ordinary Home input, HUD and fixed-camera state return without a
  reload; normal
  Home arrivals never install the opening controller.
  Editor Play pins its start scene to `MainMenu`; the exact temporary
  `InitTestScene{GUID}` bootstrap used by Unity Test Framework suppresses that
  override for PlayMode tests and restores it after returning to Edit Mode.
- **Accepted — Persistent transition context:** Static subsystem-reset session
  state carries the seed, active bar context, explicit
  bar/home/supermarket city return kind, the next stairwell arrival side and
  the consumed Home arrival kind plus current game time/day index
  between Single-mode scene loads. `BeginNewGame` restores all of those values
  together with route, visits, wallet, drinking and balance state.
- **Accepted — Wake-started scaled session clock:** `GameTimeState` resets to
  frozen `05:59`. Only the successful startup Wake atomically moves it to
  `06:00` and starts it; later bed interactions do not reset or pause it.
  `GameTimeRuntime` persists across Single-mode loads and advances through
  `Time.deltaTime` at `1.0` game minute per real second, making one full `24 h`
  day exactly `1440` real seconds (`24` minutes). Midnight increments a
  session day index. `GameTimeState.Advance` also returns the actually elapsed
  game minutes so `GameSessionState` advances clock and needs from one delta;
  any owner that sets `timeScale` to zero naturally freezes both.
- **Accepted — Bounded structured diagnostics:** Runtime support logging uses
  one fail-safe UTF-8 NDJSON stream with a versioned envelope, monotonic
  sequence, session/scene/seed context and explicit transition/minigame
  correlation IDs. Only state boundaries and results are instrumented;
  per-frame simulation and ordinary Unity log messages are excluded. Editor
  and development runs default to verbose, release players to basic, and
  batch/command-line tests to off. Files rotate at 5 MiB with three retained
  archives, while `F8` writes and flushes a manual state snapshot.
- **Accepted — Separate classic door transition:** Bar, supermarket, home and
  stairwell doors
  reserve one transition guard for the complete
  `source -> DoorTransition -> destination` chain. The intermediate scene
  runs a deterministic `3.15 s` unscaled handle/door/camera timeline in a
  black void while the destination loads asynchronously with activation held
  at the preload boundary. Activation is released only after the sequence is
  complete and fully black; a missing presentation root falls back to the
  requested destination. The door opens outward toward the fixed camera and a
  black sprite keeps the revealed doorway opaque; direction changes only the
  warm/cold lighting treatment and does not own persistent gameplay state.
- **Accepted — Compact separate home interior:** `HomeInterior` owns a
  validated `10 x 8 x 3.4 m` runtime-composed main room and bathroom with six
  main-room furniture footprints plus toilet, shower and sink, protected
  entry/main/bathroom-access paths and a solid ajar bathroom door. Dedicated
  blocking junk owns the otherwise reachable camera corner; the toilet keeps
  its cistern against the right wall and its bowl facing into the room. It
  reuses the common player, intoxication HUD, interaction and door-transition
  contracts while putting the single Main Camera into three authored fixed
  poses with doorway/balcony hysteresis and a main-room fallback. The camera
  poses remain unchanged. A separate cold, hard-shadow ForcePixel Spot starts
  just inside the bathroom threshold and projects through the ajar door onto
  the existing apartment exit area. It shares one deterministic unscaled
  fluorescent-failure flicker with the bathroom point pool, HDR tube and halo;
  the main practical and time-of-day window-cookie Spot remain. One separate
  shadowless warm ForcePixel Spot is co-located with the compact amber fixture
  above the entrance and aims its full-strength cone over the door and entry
  floor, for at most five atmosphere-owned local realtime lights in addition
  to the scene Directional light. The exit door's
  geometry and material remain unchanged. Exiting sends
  the player from the apartment into `StairwellInterior`; only the stairwell's
  street door sets the home return kind and restores the matching city
  approach, without altering route, visit, cash or drinking progress.
- **Accepted — Data-first interactive Home refrigerator:** The refrigerator is
  derived from the kitchen footprint as its own validated plan instead of
  remaining an indistinct cube embedded in the counter. The counter is split
  around its `1.08 x 0.76 m` footprint and the table moves `0.30 m` deeper in
  Home-local Z, leaving a player-width waypoint route and a dedicated approach
  trigger. Runtime composition builds a hollow worn cabinet, three shelves,
  lower drawer and two door bins around six cavity plus two door storage slots;
  stable slot IDs initially place one vodka bottle, chicken egg and open stew
  can. `HomeRefrigeratorItemCatalog` owns each occupant's localized name,
  description and preview transform, while `HomeRefrigeratorItemView` registers
  its original root, renderers and tight trigger collider. Stable slot IDs now
  map to session-owned world-item IDs; collected slots remain physically empty
  when Home is rebuilt after a scene round trip.
  `HomeRefrigeratorInteraction` owns a separate unscaled
  `CameraApproach -> Reach -> Unsealing -> Opening -> Inspecting -> Closing ->
  Sealing -> CameraReturn` timeline. It captures the shared modal lock, keeps
  the normal 3D hero and contact shadow through the camera approach, then
  acquires their owner-scoped visibility lease in the same presentation frame
  that a right-arm subset filtered from the production prefab appears.
  It holds the `102°` open state until explicit close input; while the ordinary
  interactor is suspended, its clickable close prompt binds directly to the
  same `RequestClose` guard used by keyboard/gamepad input. It disposes the
  subset and lease at the start of `CameraReturn`, restoring the exact world
  mesh and contact-shadow state; the active fixed-camera shot, input and HUD
  restore on completion, disable or destroy. A cold
  emissive strip plus `CityLightHalo` reveals the contents without increasing
  Home's realtime-light count; generated seal, hinge and thunk cues use the
  existing spatial audio contracts.
- **Accepted — Nested PS1 refrigerator item inspection:** Item browsing exists
  only while the outer refrigerator timeline holds `Inspecting` and reuses its
  captured modal lock. A pointer ray against registered trigger colliders
  applies a reversible `MaterialPropertyBlock` hover tint and draws the
  localized name beside the cursor; keyboard/gamepad cycling plus confirm is an
  equivalent path. Selecting the vodka, egg or open stew can advances a pure
  unscaled `Browsing -> FlyingIn -> Inspecting -> FlyingOut` timeline, reparents
  the model through a camera-relative pivot, eases it to a centered preview,
  rotates it at `18°/s` and reveals a dark camera-facing backdrop. Crisp
  post-composite UI shows the localized title, short description and
  `Take`/`Use`/`Back`. `Take` atomically adds the matching inventory item and
  stable collected-source marker, unregisters the refrigerator item and removes
  its physical model; `Use` remains unavailable. Back or an outer close request
  returns an untaken item before the door can close. Normal return, cancel, disable and
  destroy restore its exact parent, sibling index, local position/rotation/
  scale, selection-collider state and original renderer colors, then clear the
  temporary presentation state.
- **Accepted — Session hero inventory:** One pure ordered stack model and code
  catalog own the current run's apartment keys, lighter and collected Home or
  purchased supermarket food and drink items. `GameSessionState` exposes
  read-only stacks plus atomic add,
  remove and world-source collection operations; `BeginNewGame` resets starter
  possessions and every collected source, while ordinary scene transitions do
  neither. `InventoryController` is installed beside pause in all five gameplay
  roots, opens on `I` or gamepad North only during free input, captures the
  existing fullscreen modal lock and exact time scale, and restores both on
  close or lifecycle cleanup. Its `640x360` IMGUI view keeps generated
  point-filtered icons in the bounded five-column grid, uses the dedicated
  transparent portrait rendered from the production 3D hero and
  shows the selected item through a live point-filtered 3D render in both the
  lower description and Examine views. The preview reuses the same procedural
  models as physical refrigerator contents, adds matching keys and lighter,
  rotates on unscaled time and owns a hidden camera/light/RenderTexture stage
  that is inactive outside the inventory. Status keeps the portrait and cash,
  then fits intoxication, hunger, stress and fatigue into four compact `0-100`
  bars.
  Consumables expose a contextual Eat/Drink command through pointer, `U` and
  gamepad West; unsupported Equip, Combine and Drop commands remain absent.
  Pause executes before
  inventory so Escape sees the occupied lock, then inventory closes later in
  the same frame without leaking that press into pause.
- **Accepted — Session needs and atomic inventory consumption:** Hunger, stress
  and fatigue are clamped integer `0-100` session values where higher is worse.
  All start at `0`, survive ordinary scene loads and reset to `0` with a new
  game. Once the startup Wake starts the persistent scaled clock, one pure
  double-precision progression state raises hunger by `100 / 1440` and fatigue
  by `100 / 1080` per elapsed game minute. Fractional state makes the result
  independent of frame partitioning; reaching `100` discards overflow instead
  of banking hidden growth. The clock path keeps both frozen before Wake and
  at `timeScale = 0`, while ordinary interactions, transitions and scene loads
  do not create another pause rule. The inventory exposes the clamped integer
  values, but neither need applies a gameplay debuff yet. A normally completed
  bed exit resets fatigue and its fraction through the shared interaction
  completion boundary; cancellation and lifecycle cleanup do not. A
  data-first consumable catalog gives all present food an explicit relief
  value plus a poor-food minimum hunger of `20`, so repeated cheap food can
  never fully satisfy the
  hero and food at or below its floor is not consumed. The supermarket vodka
  bottle is one atomic four-serving use. `GameSessionState` preflights every
  use, removes exactly one stack only after success is known, then commits food
  relief and clears the hunger fraction, or commits drinking, stress and
  intoxication together. Maximum intoxication,
  a stale stack or a no-effect food use mutates nothing. Refrigerator `Use`
  remains unavailable; items are taken into the hero inventory first.
- **Accepted — Separate finite-stock supermarket interior:**
  `SupermarketInterior` owns one validated `16 x 11 x 3.6 m` runtime-composed
  room with protected aisles, three shelf sections, a stockroom facade and a
  decorative checkout plus cashier. The cashier and register do not process
  sales. Each shelf owns an authored fixed camera and one interaction station.
  One continuous modal browser cycles through every available physical product
  in deterministic shelf/slot order, skips empty shelves and never releases its
  captured player/camera state while changing shelves. The selected shelf keeps
  its authored camera position and field of view while the rotation targets the
  combined world renderer bounds of the selected product. Muted clickable
  previous/next arrows follow the product's projected screen bounds; pointer,
  keyboard and gamepad all use the same navigation path without replacing or
  fading or replacing the player presentation.
  `SupermarketProductCatalog` offers exactly one physical chicken egg, vodka
  bottle, closed stew can, instant noodles and day-old loaf. Each product has a
  stable source ID. `GameSessionState.TryPurchaseWorldItem` is the sole commit
  boundary: pure rules validate source, offer, cash and stack capacity, then
  the transaction records the source, adds one inventory item and deducts cash,
  rolling the source back if inventory insertion fails. Success immediately
  removes the shelf model and collider; scene rebuilding filters committed
  sources, so sold products stay gone until `BeginNewGame`. Failure mutates no
  cash, source or inventory state. `ClosedStewCan` is deliberately distinct
  from the refrigerator's `OpenStewCan`, so buying sealed stew cannot satisfy
  the cat-feeding requirement. Entry and exit use the shared DoorTransition and
  restore the supermarket's own City return point.
- **Accepted — Reusable inventory-backed target interaction:** A target-specific
  adapter supplies one validated `InventoryItemRequirement`, localized Talk,
  confirmation and missing-item keys, and an idempotent
  `IInventoryTargetInteractionHandler`. The pure model owns only
  `Closed/Choice/Confirmation/Executing` state, defaults to Talk and No, and
  cannot execute twice. The shared scene-local controller owns pointer,
  keyboard and gamepad input, captures the existing modal lock, disables the
  ordinary prompt and restores exact player/camera/HUD state. A Yes path first
  calls the handler's preparation boundary, then rechecks and atomically removes
  the required stack through `GameSessionState`, then begins presentation;
  failed preparation or a stale stack consumes nothing, and a thrown startup
  refunds the just-committed stack before cleanup. Normal completion and
  abnormal cancel/disable/destroy use separate controller exits but the same
  handler cleanup contract. Each adapter tracks the presentation resources it
  actually acquired and restores only that owned prepared/active work before
  modal input returns. This first version intentionally supports
  one item stack per definition; multi-item recipes require a later atomic
  inventory transaction rather than sequential removal.
- **Accepted and implemented 2026-08-04 — Continuous modular 3D hero:** Every
  production runtime and UI representation of the main hero now derives from
  the generated modular 3D character. `PlayerFactory` preserves the
  authoritative `PlayerMotor`/`CharacterController` root and instantiates one
  `Resources/Player/Player3D` prefab in all five gameplay roots. Its Generic
  Animator uses no root motion; the prefab contains a 31-bone armature with six
  non-deforming sockets, while `Player3DAssetRegistry` serializes 73 mesh
  bindings, 16 required anatomical parts, metrics and 23 in-place Actions.
  `Player3DCharacterPresentation` owns locomotion, face,
  intoxication/balance and authored fall sampling, including the full-body
  side-down-to-all-fours-to-stand Rise actions; the companion ragdoll owns only
  the bounded physics interval and its `0.16 s` return bridge. Bed, smoking and
  cat feeding drive continuous full-body clips on that same rig; bar drinking
  and refrigerator reach filter camera-local arms from the prefab, and
  inventory loads the dedicated transparent 3D portrait. Real meshes cast URP
  shadows while the analytic contact patch remains grounded and fall-aware.
  Guided approach,
  independent entry/action/exit poses, neutral settle, terminal hold, atomic
  preparation and owned lifecycle cleanup remain mandatory.
- **Accepted — Grounded endpoint contract for contextual 3D animation:**
  This is the mandatory project-wide authoring and runtime contract for every
  future interaction in this class; the normative checklist lives in
  `ai/contextual-animation-standard.md`, and deviations require an explicit
  user-approved accepted exception here.
  Interactive bed sleep, balcony smoking and cat feeding share a visible
  `Positioning` phase before `Entering -> Looping -> Exiting`. Each adapter
  provides independent entry root/pelvis/facing, action pelvis and exit
  root/pelvis/facing data. `PlayerMotor` advances the ordinary rig with its real
  `CharacterController`, walkable constraint, gait, facing and footsteps;
  manual input cannot redirect that approach. The authored Y level must already
  be reachable from the current grounded root, so a vertical mismatch refuses
  startup rather than teleporting. A constrained move with no measurable root
  or rotation progress for `1.5 s` marks the approach stalled and restores the
  captured state; scene transition, disable and destroy boundaries cancel it
  through the same ownership cleanup.
  Exact entry alignment activates the shared presentation handoff lock, resets
  locomotion, face and additive status bones, and keeps the neutral rig visible
  for one rendered frame. The controller then samples the registered Generic
  enter/loop/exit clip before aligning its serialized pelvis anchor to the
  authored world target. Animator transitions, Animation Events and root motion
  own no gameplay transaction. The timeline guarantees that its terminal exit
  pose is presented before becoming idle; the physical root is then placed at
  the independent exit and the neutral ordinary rig stays locked until its
  final `LateUpdate` restoration frame. Normal and abnormal exits end the clip,
  reset its model-root spatial offset and restore owned presentation state.
- **Accepted — Separate vertical home stairwell:** The exterior home door and
  apartment door connect through `StairwellInterior`, a deterministic
  `8.6 x 9.6 x 6.25 m` runtime-composed space with street, middle and apartment
  elevations. Three continuous physical ramps support 48 collider-free visible
  steps, while overlapping navigation corridors remain wider than the
  controller diameter at every floor/flight seam; this keeps the real
  `PlayerMotor` route continuous without changing the staircase silhouette.
  A full-width, full-standing-height invisible safety collider backed by
  visible furniture, mesh, planks and sacks seals the upward flight above the
  hero's floor. Side-aware arrival state chooses the correct spawn; only the
  street exit prepares the City home-return point. A separate 3D selector
  hard-cuts the shared camera between lower-flight, middle-flight and
  apartment-landing poses with vertical hold-zone hysteresis because the
  lower and upper flights overlap in XZ. Each pose keeps its exposed suspended
  HDR fluorescent tube and halo in frame; stronger co-located flickering
  practicals, post-processing, bounded dust, ventilation/electrical/pipe
  ambience and an optional scene-local `stairwell_theme` slot remain
  scene-owned.
- **Accepted — Perched interactive stairwell cat:** One seated pixel-art cat
  owns a non-blocking authored perch on the upper bar of the
  `Middle Landing Back Rail` and a separate walkable interaction point. Its
  depth-tested `BillboardSprite` aligns to each fixed camera plane while the
  artwork keeps the body turned away from the viewer and selects a head turn
  toward the player. The point-filtered
  `Resources/Stairwell/Cat/StairwellCatAtlas` is exactly `512x256`, an `8x4`
  grid used for ordinary idle motion and a rare eight-frame grooming sequence
  roughly every 36 seconds. `StairwellCatInteraction` remains the
  `IInteractable` adapter but now opens the reusable inventory-target menu.
  Talk closes the modal and emits the original localized response. Interact
  requires `OpenStewCan x1`; absence emits bounded localized feedback, while
  presence opens default-No confirmation. Yes uses the two-phase preparation
  boundary before the controller atomically removes one can. The adapter
  visibly guides the player to a grounded, validated middle-shot entry point,
  settles the neutral 3D rig for one rendered frame and samples
  `CatFeedEnter`, `CatFeedLoop` and `CatFeedExit` on that continuous rig. On the
  player loop boundary it begins the cat's independent top-first `512x128`,
  `8x2`, 16-frame sprite track at `6 fps`; ordinary cat idle/look is paused and
  restored afterward. Player presentation/contact shadow, cat presentation,
  modal ownership, camera, HUD and input restore on normal completion and every
  lifecycle abort. The cat's keyed source and packing contract remain under
  `ArtSource/Stairwell/Cat/Feeding` and
  `tools/build-stairwell-cat-feeding-atlas.py`.
- **Accepted — Same-scene third-floor Home balcony:** The Home right wall owns
  a real window and open glazed door connected to a walkable balcony at
  `4.7 m` street elevation. The room threshold and deck extend the same
  `RoadWalkableArea`; open-looking rails retain invisible safety colliders.
  `HomeInterior` does not additively load `City`: it regenerates the city plan
  from the preserved seed, transforms only a bounded slice of the player-home
  street into Home-local coordinates and renders nearby roads, lots, stable
  windows, lamps and signals without collision, a second City root, player,
  camera or realtime street-light pool. One shared geometry contract keeps
  the Home opening and default `8.8 m` City facade balcony aligned.
- **Accepted — Modal city-facing balcony-smoking vignette:** A data-first
  `HomeBalconySmokingPlan` derives one walkable Home-local dock around
  `(6.60, 0.04, -1.45)` from the balcony bounds. On the first `E`, modal
  ownership disables manual input but keeps the ordinary 3D rig plus its mesh
  and contact shadows visible while `PlayerMotor` walks the physical root
  through the existing
  CharacterController/walkable constraint to the explicit entry point and
  turns it along `+X`, out toward the reconstructed city. Only exact entry
  alignment begins continuous sampling of `SmokeEnter`, `SmokeLoop` and
  `SmokeExit` on that same rig. A separately authored exit root, pelvis and
  facing receive the ordinary rig before control returns; entry and exit may
  currently coincide without sharing one implicit stand anchor. The cigarette
  prop follows the serialized `SOCKET_Cigarette.R` bone. A visual-only worn
  enamel ashtray is composed permanently with the balcony at Home-local
  `(7.25, 1.12, -1.67)`: its base rests on the outer rail cap and its dish
  covers the ember at the authored exit flick. It is not owned by the
  interaction or registered with the rail dither group, so it remains visible
  before, during and after smoking. Real mesh and contact shadows remain active
  throughout because no alternate player renderer is introduced. Existing
  loop-local frames `3`, `11`, `14` and `23` produce the
  `9.5 s` rest/drag/breath/exhale cadence without duplicate art. A bounded
  deterministic ParticleSystem starts one gray-green burst at loop-local frame
  `16` from the registered mouth socket. Its emitter follows the animated mouth
  without inheriting the FBX bone scale, while world-space particles drift
  cityward and finish fading before the next loop. Exit stops new emission but
  lets the detached plume dissipate until owned cleanup. A second `E` queues
  immediately but reaches the exit sequence only at calm loop-local frames
  `0-3` or `21-23`, so no active drag or smoke plume is cut. The camera holds
  for `0.35 s` and follows a smooth quadratic push-in to `38°` FOV. Its close
  look target uses a `0.33 m` Home-local `+X` cityward offset: the resulting
  target yaw is about `13.12°`, the hero remains at about `0.37` viewport X
  at `16:9` and inside the `0.28-0.43` safety band across supported desktop
  aspects, and a point `1 m` farther toward the city stays visible to his
  screen-right. The close-camera forward direction's dot with Home-local `+X`
  stays above `0.19`, proving a material cityward component. This framing
  changes only the look target; camera position and FOV remain unchanged. A
  smoking-local
  deterministic drift is layered over that base path with
  local position amplitudes of `0.016 / 0.007 / 0.005 m` on X/Y/Z and
  pitch/yaw/roll amplitudes of `0.12° / 0.20° / 0.08°`. Each channel combines
  paired harmonics with periods from `13-23 s`, while one presentation clock
  stays continuous across Entering, Looping and Exiting so phase changes never
  restart or snap the motion. The existing camera blend is also the drift
  envelope: it brings the offset in with the push and returns it exactly to
  zero while the captured Balcony shot restores over the `2 s` exit. FOV keeps
  the original interpolation and receives no pulse. This overlay is owned by
  the smoking interaction and does not change generic `PlayerCameraFollow`
  behavior. A separate non-spatial
  `HomeSmokingMusicPlayer` loads only the optional user-supplied
  `Resources/Audio/SmokingMusic/smoking_theme`, fades from zero over `3.2 s`,
  fades with the exit and treats a missing clip as a silent no-op.
- **Accepted — Diegetic Home practicals and fixed 3D shots:** The Home
  atmosphere retains exactly two shadowless practical realtime lights. A
  visible HDR emitter and depth-tested halo are physically co-located with
  each practical so the warm hanging lamp and cold bathroom tube read as
  actual sources. The bathroom point pool, tube and halo share a deterministic
  unscaled `6.4 s` cycle with a separate cold hard-shadow Spot staged just
  inside the bathroom threshold; the group remains steady for most of the
  cycle, then stutters together briefly while the Spot projects through the
  ajar door toward the apartment exit. A separate warm shadowless Spot is
  physically co-located with the compact emissive entrance lamp and illuminates
  the door plus entry floor. One shadowed cookie Spot projects through the
  window; only its color and intensity blend from the existing cold night
  shaft to warm daylight under `HomeDayNightController`, while the room
  practicals retain their existing behavior. The atmosphere owns at most five
  local realtime lights. Window/door panes reuse one shared transparent glass
  shader/material. During Home fixed-camera
  ownership, the same world-oriented modular 3D hero remains active in
  MainRoom, Bathroom and Balcony; no player billboard-plane mode is required.
- **Accepted — Explicit Home foreground cutaway:** Runtime Home builders
  register logical renderer groups for furniture and its attached dressing,
  bathroom and balcony doors, soft bathroom dressing and the visible balcony
  rail sections. Each group owns a validated kind and minimum visibility;
  renderers cannot belong to two groups. The structural room shell, glass,
  practical lights and invisible safety colliders are deliberately absent from
  the registry. `HomeOcclusionResolver` derives five camera-plane points from
  the combined live player-renderer bounds: head, left chest, right chest,
  pelvis and feet. Only the first four are protected, so low furniture may
  still hide the feet without flattening scene depth. If any registered
  renderer bounds intersects a camera segment to a protected point,
  `HomePlayerOcclusionController` fades the whole logical group toward its
  authored `0.15-0.23` floor. One shared opaque, depth-writing alpha-clip
  dither material receives `_HomeOcclusionVisibility` through one reused
  `MaterialPropertyBlock`; no per-renderer materials are created and
  existing color properties survive. Its ForwardLit variants retain the PC
  renderer's clustered additional lights, light cookies, light layers and
  reflection probes, while matching alpha-clipped shadow, depth and depth-normal
  passes keep shadows and SSAO coherent with the visible pattern. Fade-out
  takes `0.15 s`, a cleared group
  holds for `0.12 s`, and restoration takes `0.30 s`, all in unscaled time.
  Renderer presentation never disables or changes colliders, triggers or
  GameObjects. Opening, refrigerator and animated Home interactions suspend
  the cutaway and restore full opacity; disable, destruction and camera release
  also clear presentation state, with original shared materials restored when
  the controller is destroyed.
- **Accepted — Ordered session route:** The current itinerary is a unique
  ordered list of stable `BarId` values. A separate visited-ID set survives
  scene loads for the same city. A terminal bar activity reports completion
  through `IBarMinigame`; the interior root marks that bar visited and removes
  the stop, while entering, cancelling or leaving early does not.
  Both route and visited progress reset when the city seed changes.
- **Accepted — Road-graph route planning:** Each itinerary leg uses
  deterministic weighted Dijkstra with a binary min-heap over street and park
  path edges; player and bar endpoints are projected onto their segments
  without NavMesh.
- **Accepted — Modal schematic city map:** A runtime IMGUI overlay fits the
  complete finite city in one view, colors and labels all five districts,
  distinguishes park land and paths, exposes mouse/keyboard/gamepad editing,
  and temporarily suspends motor, interaction, camera orbit, cinematic camera
  motion and the HUD. It consumes `CityLayout.DistrictPointsOfInterest`
  directly, draws those lots as open public ground and gives the waterworks,
  drying yard, weighbridge and last-route island distinct non-interactive
  marker shapes plus a localized name legend. The same overlay reads the
  canonical `CityLayout.Supermarket`, draws it as a non-route grocery-shop
  landmark and resolves pointer hover across bars, home, shop and POIs by
  nearest marker, with deterministic priority ties. It also consumes the
  immutable bus plan and draws Route 01 below the orange player itinerary as a
  blue ink-outlined closed winding loop, with five numbered localized stop
  markers in the default layout and a compact route/stop legend; it does not
  track the live pooled bus. Localized
  hover names use one high-contrast tooltip that flips and clamps inside the
  map. Shop and POI landmark markers remain context for the orange player
  itinerary: POIs independently own nearby Route 01 stop targets, but the
  landmark markers do not enter the visited set or count and do not change bar
  selection or player pathfinding.
- **Accepted — Shared-lock gameplay pause:** City, BarInterior,
  SupermarketInterior, HomeInterior and StairwellInterior each attach one
  runtime `PauseMenuController` to their existing UI root. Escape or gamepad
  Start can open it only when no other
  `BarMinigameModalLock` or scene transition owns gameplay; pause therefore
  never steals Escape from maps, refrigerator inspection or minigames, remains
  unavailable during the Home opening, and uses a Bar-specific gate rather
  than skipping the arrival reveal. Opening captures the prior time scale,
  listener-pause flag, motor/interactor/orbit/cinematic/HUD state, then sets
  scaled time to zero and pauses listener-owned gameplay audio. UI-pool sources
  alone ignore listener pause. Resume restores captured state after a one-frame
  input guard; disable, destroy, restart and quit restore it immediately.
  The localized `640x360` menu exposes only Resume, Start Over and Quit Game;
  restart and quit require explicit default-No confirmation, while save/load,
  settings and a visible main-menu destination remain unimplemented.
- **Accepted — Independent player heading:** The motor rotates the player root
  only toward non-zero actual planar movement and preserves that heading while
  idle. The chase camera orbits independently and never writes player yaw.
- **Accepted — Bounded inertial locomotion:** Camera-relative input targets
  a `2.6 m/s` maximum through `6.5 m/s²` acceleration and
  `11 m/s²` braking. The motor feeds actual constrained displacement back
  into its next velocity step, so road edges and collisions cannot store a
  hidden impulse. Normal input release coasts, while modal ownership, scene
  transitions, input disable and teleport still stop planar motion
  immediately.
- **Accepted — Bounded cinematic chase camera:** Exterior/interior framing uses
  `2.6 m / 53°` and `2.2 m / 57°` profiles with `1.4 m / 1.3 m` raised focus
  points that compose the hero below frame center. Orbit yaw and target
  focus use deliberately weighty `0.20 s` and `0.18 s` damping; focus stays
  within `0.45 m` and snaps on jumps beyond `1.75 m`.
  Deterministic low-frequency idle drift and speed-driven bob affect only
  focus, pitch and roll; requested yaw and FOV remain stable. Collision
  shortens the arm immediately, restores it with `0.32 s` damping and fades
  cinematic motion during fullscreen modal ownership. Balance checks disable
  orbit input but deliberately retain cinematic motion so intoxication lean
  and fall reactions remain visible.
- **Superseded 2026-08-04 — Eight-direction player presentation:** A corrected
  point-filtered `512x96` reference and a derived `512x864` layered atlas
  provide eight explicit `64x96` views at PPU 48. Each view has one body layer
  and upper/lower segments for both arms and legs. A signed player-camera
  angle selects the view in 45-degree sectors with 5-degree hysteresis; views
  are never mirrored and share one foot pivot. Jointed walking projects the
  actor's sagittal plane into the active billboard view: side views swing in
  screen space, front/back views swing in depth, diagonal views blend both,
  and contralateral limbs remain in opposite gait phases. Explicit
  atlas-derived contact points keep whichever foot is lower pinned to the
  visual ground plane; a `5 mm` base clearance is consumed by the footfall
  compression instead of adding an always-positive whole-puppet bob.
  Breathing and impact compression offset only the body and arm roots, while
  the projected joints retain grounded feet plus readable weight transfer and
  deterministic alternating left/right arm gestures while idle. A separate
  `512x480` body-expression
  atlas provides neutral, half-blink, closed-blink, watchful and tense rows;
  the stronger blink remains available during locomotion, watchful/tense
  states require sustained idle below strong intoxication and outside a
  balance/fall state, runtime swaps only the existing body renderer, and all
  rear variants remain neutral. The same joint hierarchy accepts continuous
  intoxication sway, arm spread, knee bend and balance lean. A failed balance
  check temporarily reuses its body renderer for a full-body `128x96` frame,
  disables the other eight visible layers and lazily slices one of 16
  point-filtered `10x8` fall atlases. Eight camera-relative views each own
  separately authored screen-left and screen-right variants, so the physical
  bandage/patch asymmetry never relies on mirroring. Explicit
  `Falling`/`Down`/`Rising` progress maps to `14`/`36`/`30` frames and restores
  the original nine-part puppet without changing renderer count.
- **Superseded 2026-08-04 — Camera-independent sprite shadow:** One collider-free
  nine-part `ShadowsOnly` puppet reuses the directional part sprites and a
  shared alpha-clipped URP shadow-caster material. It selects its authored view
  from the signed player-to-main-light angle, faces the directional light
  rather than the camera and remaps the live joint angles into that view, so
  City and BarInterior receive the animated gait, upper-body compression and
  whole-puppet sway without changing the nine visible puppet renderers. A
  separate shared four-vertex analytic contact quad stays on the player root
  instead of following `PoseRoot`, remains visible when realtime shadows are
  unavailable and supplies the stable ambient-occlusion cue beneath the feet.
  Practical street/bar lights remain shadowless.
- **Accepted — Runtime presentation:** City geometry, primitive colors and the
  shared interior are built at runtime. The hero and ambient City pedestrians
  load as low-poly 3D prefabs; cocktail, beer-pong, Split-the-G, tincture, Bar
  NPC and stairwell-cat bitmaps still load from `Resources` and are sliced or
  drawn at runtime.
- **Accepted — Shared rendering state:** Primitive colors use
  `MaterialPropertyBlock`; every ordinary runtime primitive explicitly shares
  the serialized Resources `RuntimePrimitiveLit` URP material so Player builds
  do not depend on Editor-only primitive defaults. Emissive and atmosphere
  effects reuse their cached specialized resources, with no per-instance
  materials or runtime `Shader.Find`.
- **Accepted — PC PS1 world composite:** The active PC renderer runs one native
  Unity 6 RenderGraph feature at `AfterRenderingPostProcessing` for the final
  Game camera. It footprint-averages the world to `640x360` by default, blends
  35% perceptual-space RGB555 into the original tone without a screen-space
  dither overlay, then point-upscales it, producing exact 2x/3x scaling at
  720p/1080p. Before quantization, the same pass consumes shared intoxication
  state for animated UV warp, ghost/chromatic sampling, warmth, exposure pulse
  and vignette. Lower `426x240` and `320x180` presets remain available; mobile
  renderer integration is deferred.
- **Accepted — Crisp UI after the composite:** Runtime IMGUI is intentionally
  drawn after the world composite instead of being degraded with the 3D image.
  Prompts, segmented intoxication HUD, overhead balance gauge, map, beer pong,
  Split the G and Tinctures in a Row use a logical `640x360` canvas; the
  denser cocktail view remains responsive while sharing the same palette,
  stepped frames and point-filtered accents. Menus, modal inspectors, the map
  and minigames omit persistent key-binding guides and control-hint footers.
  Clickable modal actions keep action-only labels; every active contextual
  prompt is also a full pointer target and invokes the exact same guarded action
  path as E, Enter or gamepad South instead of duplicating interaction logic.
- **Accepted — Shared low-poly cylinder:** Runtime cylinder requests replace
  the stock visual mesh with one cached flat-shaded 8-sided mesh while
  preserving the primitive collider contract. No per-instance mesh or
  material is created.
- **Accepted — Shared MVP exterior day/night lighting:**
  `GameTimeDayNightRules` returns night before `06:00`, a smooth dawn from
  `06:00-07:00`, day through `18:00`, a smooth dusk from `18:00-19:00`, and
  night from `19:00`. `CityDayNightController` applies its directional color,
  intensity/rotation, ambient, reflection and shadow sample, while
  `CityNightAtmosphere` scales the bounded lamps, bar lights, emissive bulbs
  and halos with the sample's night factor. `HomeDayNightController` applies
  the same sample to the apartment window shaft and reconstructed Balcony
  exterior. Bar, Supermarket and Stairwell visual profiles remain unchanged.
  Presentation updates are change-driven: stable day/night samples perform no
  lighting work, ordinary dawn/dusk updates do not regenerate the environment
  cubemap, and a zero-factor street-light pool does not scan lamp anchors.
  Forced setup and Home Balcony entry/restore retain their complete refresh.
  This cycle does not own visibility: `0.070` luminous gray-green Exp2 fog,
  the matching terminal camera color, `48 m` far clip, `CityFogField` and
  dedicated `CityNoirVolumeProfile` stay unchanged at every hour. Custom fog
  stripping still retains the runtime Exp2 variant, and interiors keep their
  existing fog/range contracts.
- **Accepted — Bounded local fog:** One seeded, player-following
  `CityFogField` adds slowly drifting world-space fog with at most 36 particles
  and a bounded `0.120` peak alpha. It reuses the shared atmosphere material
  and has no collision, trails or particle lights.
- **Accepted — Depth-tested light bloom:** Each active street/bar light and
  amber signal lens can own a two-particle `CityLightHalo`. The shared
  `Resources` shader softens depth intersections, so glow diffuses in fog
  without remaining visible through solid geometry.
- **Accepted — Data-first night fixtures:** `CityNightFixturePlanner` derives
  two lamps per road edge and consumes the shared street-intersection selector
  for at most six signalized degree-3+ intersections before GameObjects exist.
  The same ordered nodes own zebra crossings. Visual lamp fixtures and bulbs
  are combined into separate `48 m` meshes while lightweight anchors preserve
  the pooled-light contract.
- **Accepted — Bounded practical lights:** All bulbs and signal lenses reuse
  one HDR URP Unlit material; a player-relative pool of directed street spot
  lights plus bar entrance point lights keeps the city-atmosphere pool at no
  more than 12 shadowless realtime lights. The sole active bus may add only its
  four runtime-owned shadowless Spots, for a bounded exterior maximum of 16.
- **Accepted — Safe signal rhythm:** Each selected intersection uses one
  seed-phased controller for two heads and flashes amber below 1 Hz; red and
  green lenses remain dimly visible without realtime lights.
- **Accepted — One canonical audio mixer:** `GameAudioMixer` loads one
  `Resources/Audio/Mixers/BarPromenadeAudio` asset and resolves exact
  `Music`, `Ambience/Beds`, `Ambience/Details`, `SFX/World`,
  `SFX/Gameplay` and dry `UI` groups. Scene roots select City, Bar,
  Stairwell, Home or DoorTransition snapshots; non-door profiles transition
  over `0.25 s`, while DoorTransition cuts immediately at blackout.
  The mixer keeps `-6 dB` master headroom under a compressor and owns
  dedicated Receive/Reverb and Receive/Echo returns. Music, ambience details
  and world SFX feed scene-specific send levels; Home stays short, damped and
  echo-free, while Stairwell uses the longest/strongest reverb with a dark
  high-frequency rolloff plus restrained stereo echo.
- **Accepted — Reproducible mixer authoring:** The committed mixer is created
  and updated through `AudioMixerAssetSetup`, which uses Unity's editor API,
  preserves one exact topology across repeated runs and fails when a required
  effect, send or echo parameter is unavailable. EditMode coverage validates
  the DSP graph, send targets and critical snapshot values rather than only
  checking group names.
- **Accepted — Scene-local music with guarded fades:** `CityMusicPlayer` loads only `city_theme`
  from `Resources/Audio/CityMusic`, while `BarMusicPlayer` loads only
  `bar_theme` from `Resources/Audio/BarMusic` and
  `SupermarketMusicPlayer` optionally loads only `supermarket_theme` from
  `Resources/Audio/SupermarketMusic`;
  `StairwellMusicPlayer` optionally loads only `stairwell_theme` from
  `Resources/Audio/StairwellMusic`; `HomeMusicPlayer` optionally loads only
  `home_theme` from `Resources/Audio/HomeMusic`. Each clip background-streams
  through a non-spatial looping `AudioSource`, mild low-pass filter and the
  shared `Music` group under its matching scene root. `SceneMusicPlayer` owns
  a one-second smooth unscaled gain envelope, waits for background-streamed
  clip data before starting that envelope and fails silent if loading fails.
  `SceneTransitionService`
  preloads the destination with activation held until every active-scene
  theme reaches zero, then the new scene begins its own fade-in. Disabled or
  missing players complete that handshake immediately, with a bounded safety
  timeout preventing scene activation from deadlocking. Home alone reads the fixed-camera
  Balcony shot: it fades `home_theme` to zero, pauses while preserving the
  sample position and resumes through the same envelope only after the shot
  returns indoors. The interaction-local `smoking_theme` retains its separate
  animation-driven envelope.
- **Accepted — Generated retro SFX:** `RetroSfx` deterministically synthesizes
  the mono `22050 Hz` UI, footstep, door latch, sustained hinge creak,
  cocktail, beer-pong, Split-the-G and tincture clips in memory.
  `RetroAudioService` persists across scene loads, reuses bounded UI/world/bar
  source pools, routes them to dry UI, world SFX or gameplay SFX, and applies
  per-effect cooldown and concurrent-voice limits.
- **Accepted — Diegetic opening alarm:** Home always builds one validated
  bed-relative nightstand and collider-free low-poly alarm clock. The clock
  owns one reusable four-digit, 28-segment display that keeps briefly
  flickering `05:59` before and after the five-second menu boundary, then
  switches to solid `06:00` only when Wake Up is chosen without rebuilding
  geometry, plus one generated looping mono `22050 Hz` mechanical ring on a fully
  spatial `SFX/World` source and applies a bounded visual rattle while active.
  The menu remains silent; choosing Wake Up starts the ring together with
  `06:00` and the session clock, keeps the clock shot and sleeping loop for
  exactly three unscaled seconds, then stops the ring before camera motion and
  the wake animation begin. The display follows current session hours/minutes
  thereafter and on later Home visits while remaining silent.
- **Accepted — Layered scene-local procedural ambience:** Every playable root
  owns one quiet deterministic `22050 Hz` ambience bed and tone filter routed
  to `Ambience/Beds`. Home additionally owns five spatial
  `Ambience/Details` sources and eight runtime clips: distinct closed/open
  refrigerator loops, a balcony loop, four sparse cue types and one dedicated
  bathroom-tube crackle. The crackle source is co-located with the visible
  tube and reacts in the same frame to every applied flicker-factor change.
  The two refrigerator layers are raised by `4 dB`; their sources remain
  co-located, scheduled at the same DSP time and
  mixed from door openness with clamped cosine/sine equal-power gains; closed
  and open states therefore use different deterministic eight-second mono
  timbres instead of filtering one clip. Stairwell retains three spatial
  sources and six clips: two quantized eight-second loops plus four cues. Pure
  seeded schedules bound pitch, gain and delay; a data-first anchor planner
  maps sources to refrigerator/balcony/domestic fixtures at Home and
  ventilation/electrical/pipe/debris/water/door fixtures in Stairwell.
  Reinitialization reuses owned sources and clips, enable/disable restarts or
  stops both synchronized refrigerator loops together, and Single-mode cleanup
  destroys every scene-local source and runtime clip while the persistent
  pooled SFX service remains available to the next scene.
- **Accepted — Diegetic bar identity:** Bar lots keep their warm body color and
  add amber windows, a framed canopy and one collider-free pixel mug sign.
  Active signs share one generated sprite and use the existing upright
  billboard behavior, so recognition does not depend on color alone.
- **Accepted — Activity-specific same-scene minigame:** Every bar carries a
  stable `BarActivityKind` through the transition. One pure ordinal resolver
  assigns the first four row-major bars to cocktails, beer pong, Split the G
  and Tinctures in a Row, then assigns later bars to cocktails. `BarInterior`
  constructs exactly one matching controller and `BarActivityStation`; all
  implement one completion/cancellation contract and share a state-preserving
  modal lock.
- **Accepted — Explicit shared minigame catalog:** `BarMinigameCatalog` owns
  the ordered definitions and factories used for both normal interiors and
  debug instances. Cocktail mixing, beer pong, Split the G and Tinctures in a
  Row are built-ins; registering a unique future activity definition makes it
  available to the F9 window without changing that window.
- **Accepted — Isolated F9 debug launch:** Both runtime roots install the same
  minigame debug window. Opening it closes a conflicting city map and cancels
  a scene minigame or prior debug game before capturing the modal state.
  Debug-created controllers start with fresh drinking state, do not write
  intoxication or drink progress, and are not subscribed to bar-visit
  completion; closing either window or game restores the previously captured
  input/HUD state. The window itself also owns deliberate test controls:
  the Left/Right arrow keys or clickable buttons change the real session
  intoxication by `-20/+20`, clamp at `0/100`, and preserve last-drink and
  consumed-drink context.
- **Accepted — Three served cocktails:** A complete session contains exactly
  three rounds unless intoxication reaches 100. Each round selects beer, wine,
  vodka or cognac as its base, then accepts 2–4 unique additions before serving.
- **Accepted — Pure cocktail domain:** Compatibility, scoring, deterministic
  shelves and round/session progression live under `Runtime/Cocktails` without
  Unity scene dependencies. Every shelf has seven additions: four compatible
  choices and three traps.
- **Accepted — Score compatible ingredients:** A round scores at most 100 and a
  session at most 300; every incompatible addition subtracts 15 points.
- **Accepted — Atlas-backed IMGUI presentation:** The runtime loads one real
  4x4 pixel-art atlas from `Resources/Cocktails` and draws its cells by UV.
  The same-scene view adds a large filling glass, ingredient travel/tilt,
  pouring, good/bad particles, shaking, three-stage progress and a final rank.
- **Accepted — Deterministic 2.5D beer pong:** Beer-pong state remains plain
  runtime data. A fixed `120 Hz` simulation integrates x/y/z ball motion,
  swept table and cup-mouth contacts, table and rim restitution, settlement,
  timeout and out-of-bounds results. IMGUI projects that physical state onto
  one point-filtered 640x360 table backdrop and a 4x4 gameplay sprite atlas.
- **Accepted — Beer-pong scoring and penalty:** A rack contains six cups and a
  session allows ten throws. A clean sink awards 100, a bank adds 50, and
  clearing early awards 50 for each unused throw. Every miss immediately adds
  8 intoxication and one `LightBeer`; clearing the rack, spending all throws or
  reaching 100 intoxication ends the activity.
- **Accepted — Frame-rate-independent Split the G:** The third bar uses a pure
  normalized-level session with Normal settings: target `0.50`, drain speed
  `0.22/s`, `4.8 s` maximum sip, `1.4 s` foam settling and at most three fresh
  glasses. Level derives from total held time rather than repeated subtraction.
  Releasing is irreversible; error bands are 1/3/6/10 percent and score falls
  linearly from 100 to zero across the 10-percent scoring window.
- **Accepted — Hidden-level one-sip presentation:** Space, an in-canvas LMB
  press or gamepad South starts a tracked hold only after countdown and a fresh
  press. The tilted pint, hand and foam obscure the exact liquid boundary
  during Drinking and Settling. World-camera motion remains disabled by the
  modal lock; the presentation animates only its logical `640x360` canvas.
- **Accepted — Split the G drinking persistence:** Each non-empty attempt is a
  new `DarkBeer` drinking event. Its actual consumed fraction is converted to
  proportional intoxication and committed immediately on release, so Cancel
  cannot refund it. Best score remains local to the open minigame because the
  project has no long-term save/high-score subsystem.
- **Accepted — Deterministic tincture board:** The fourth bar starts from a
  seeded `7x7` board containing five normal flavors, no existing matches,
  exactly one `XXX` and at least three legal normal swaps. Only accepted swaps
  spend one of 15 moves. Unique matched cells clear once, then gravity, seeded
  refill and subsequent waves resolve completely before new input; cascade
  score multipliers cap at `x5`, and a stable dead board is deterministically
  reshuffled while preserving the zero-or-one-`XXX` invariant.
- **Accepted — Single `XXX` special:** A first-wave run of four or more or an
  intersecting horizontal/vertical match creates `XXX` only when none remains;
  the same pattern awards a bonus instead of creating a second special.
  Swapping `XXX` with a normal flavor is always an accepted move and clears
  every shot of that flavor plus the special.
- **Accepted — Tincture input, presentation and persistence:** Mouse click/drag,
  keyboard navigation/selection and gamepad stick/D-pad/South share one modal
  controller. The logical `640x360` view uses a deterministic point-filtered
  backdrop and 4x4 shot/effect atlas, interpolates swap/gravity/refill from
  immutable domain snapshots, and synchronizes generated swap, match and
  moonshine-burst SFX. Normal matches are customer orders and do not alter
  drinking state; only `XXX` activation immediately commits one `Moonshine`,
  +24 intoxication and one consumed drink, so Cancel cannot refund it. A
  terminal move remains completed when the modal closes during its cascade.
  Reaching 100 finishes after that cascade without adding a separate timed
  state. F9 launches use the same factory with persistence disabled.
- **Accepted — Session-only drinking persistence:** Intoxication, last alcoholic
  drink, total consumed-drink count and explicit alcohol-value stress relief
  are committed through
  `GameSessionState` after every cocktail serving, beer-pong miss and completed
  Split the G sip, plus every activated tincture `XXX`; they survive scene
  loads and reset when the application subsystem restarts. Cocktails sum only
  alcohol actually served, Split the G scales relief by the consumed fraction,
  water relieves no stress and bad-mix penalties add no relief. Aggregate debug
  state changes do not simulate a drink. The remaining
  balance-check delay and consumed deterministic sequence share that
  scene-persistent session lifetime.
- **Accepted — Physical first-person bar retail:** Every generated bar derives
  one validated `BarDrinkServicePlan` from its existing counter/back-bar
  layout. The world builder reserves the lower central shelf for exactly nine
  stable retail bottle roots and builds each with renderers, a solid collider,
  a larger selection trigger, a kinematic non-gravity Rigidbody and a mouth
  anchor. Five shared low-poly vessel meshes cover tumbler, pint, wine glass,
  shot glass and snifter; transparent glass and liquid resources are shared,
  while per-drink colors and highlights use property blocks. A pure unscaled
  timeline owns camera approach, persistent browsing, pickup, vessel
  placement, pour/fill, bottle return, an exact three-second drink, empty
  vessel return and the explicit-exit camera return. The player self-pours with
  left/right camera-local arm subsets filtered from the production prefab,
  deterministic kinematic poses and one reusable world-space liquid stream
  rather than a free physics/fluid simulation. Their owned visibility lease
  hides and restores the complete world presentation. Confirmation remains the
  sole transaction boundary: cash and
  drinking state commit exactly once before service and exit is then rejected
  until the empty vessel reaches the counter. Completing service clears only
  that order and returns to the same seated browser so another purchase can be
  made; only the dedicated Exit action starts camera return and releases the
  modal presentation. Lifecycle cleanup never refunds but always restores the
  selected bottle, vessel, camera, player presentation, controls and HUD. The
  F9 debug window may replace only pre-commit browsing and refuses to interrupt
  committed service. The validated seated framing keeps all bottle renderer
  bounds inside a 16:10 viewport, and every reusable vessel snapshots and
  restores its authored transform so repeated orders cannot compound scale.
  The camera is placed above the counter at seated eye height with a shallow
  upward pitch; the counter's floor marker and emissive sign participate in
  the controller's captured presentation state and remain hidden through
  repeated orders until the explicit camera return finishes.
- **Accepted — Session wallet and immediate bar purchases:** A fresh runtime
  session starts with `$999` in integer cash and preserves that balance across
  city/bar/supermarket scene loads and city-seed changes. Every bar owns one separate
  counter station and localized nine-item retail modal. Pure purchase rules
  validate the offer, affordability and maximum intoxication before one
  `GameSessionState` transaction deducts cash and immediately records the
  drink; failures mutate nothing and cash cannot become negative. Water costs
  `$2`, increments consumed drinks, does not sober the player and preserves
  the last alcoholic drink. `None` and the Tinctures-only `Moonshine` are not
  sold. Purchased drinks are consumed at the counter instead of being added to
  the hero inventory. Supermarket purchases use the same wallet but add their
  finite physical item to inventory rather than consuming it. Earnings and
  long-term wallet/save persistence remain deferred, and a purchase never
  completes a bar visit or changes its route.
- **Accepted — Five percentage-driven intoxication ranges:** `0` is Sober.
  Positive values map through `IntoxicationStageRules` as `1–20` Light Buzz /
  «Лёгкий хмель», `21–40` Tipsy / «Навеселе», `41–60` Drunk / «Подшофе»,
  `61–80` Unsteady / «Шатает» and `81–100` Very Drunk / «В стельку».
  Parameters interpolate linearly between the 20-point boundaries instead of
  jumping only when a name changes:

  | Range and stage | Speed | 3D bone sway | Camera roll | Vignette | Ghost | Warp |
  | --- | ---: | ---: | ---: | ---: | ---: | ---: |
  | `1–20` Light Buzz | `1.00` | `0.5°` | `0°` | `0.03` | `0 px` | `0` |
  | `21–40` Tipsy | `0.97` | `2°` | `0.15°` | `0.06` | `0.5 px` | `0.0005` |
  | `41–60` Drunk | `0.92` | `4°` | `0.6°` | `0.12` | `1 px` | `0.0025` |
  | `61–80` Unsteady | `0.82` | `7°` | `1.5°` | `0.20` | `2 px` | `0.009` |
  | `81–100` Very Drunk | `0.70` | `10°` | `2.5°` | `0.28` | `3 px` | `0.015` |

  Values shown are each range's upper-bound profile; the lower bound continues
  from the preceding row. Warmth rises to `0.10` and exposure pulse to `0.08`
  at 100. The 3D presentation progressively suppresses idle-only expressions,
  spreads the registered arms and adds pelvis/chest sway plus knee bend before
  signed balance lean or fall clips are considered. Runtime presentation eases
  a full-scale change over about `0.7 s`. The HUD is hidden at zero and otherwise
  shows the localized
  stage beside five separately filling 20-point segments. Percentage is the
  only source of persistent slowdown and visual intoxication; there is no
  independent expiring status.
- **Accepted — Deterministic balance challenge above 60:** `60` never starts a
  check; `61–100` does. A city-seed/sequence hash schedules checks from
  `18–28 s` near the threshold down to `7–12 s` at 100 and seeds a fixed
  `120 Hz` inertial arrow simulation. The crisp overhead gauge spans `140°`
  with a centered green sector, arrow and red failure-risk track. Arrow
  keys, A/D, D-pad and left stick provide signed acceleration. Difficulty
  continuously shortens warning from `1.0` to `0.65 s`, lengthens the active
  hold from `3.0` to `4.5 s`, narrows the safe sector from `48°` to `22°`,
  increases arrow disturbance/frequency and risk gain, and reduces player
  authority.
- **Accepted and implemented 2026-08-10 — State-preserving hybrid balance
  fall:** A balance-specific
  modal lock leaves motor input live during warning and active play while
  stopping interaction and camera orbit; the intoxication HUD and cinematic
  camera motion remain visible. Scene transitions,
  fullscreen modals, disabled controls or ungrounded movement prevent a check;
  returning from an external block grants at least `3 s` before it can start.
  Success schedules the next normal interval. Failure stops the motor, chooses
  the arrow side and samples the matching registered `FallLeft/Right` for a
  `0.16 s` directional lead-in. `Player3DRagdollController` then suspends the
  manual PlayableGraph and transfers the current pose to 13 runtime-composed,
  initially kinematic bodies for the remainder of the `0.45 s` fall and the
  full `1.2 s` down phase. Owned colliders ignore each other and the upright
  `CharacterController`; a kinematic root joint limits pelvis displacement to
  `0.68 m` while the gameplay root stays fixed and the existing analytic
  contact shadow remains expanded. Physics is frozen and the complete bone
  hierarchy blends for `0.16 s` into the exact side-down first sample of the
  matching `RiseLeft/Right`. One existing `Rising` gameplay phase then samples
  its distinct full-body, `50`-source-frame (`1.67 s`) action through brace,
  prone roll, a two-key all-fours hold, lead-foot plant, low crouch and the exact
  `Relaxed` seam. Every landmark supplies a complete pose rather than allowing
  an omitted limb to fall back to a Generic bind/A/T-like pose, and no runtime
  mirroring is used. All-fours is not a separate gameplay state, event or root
  transaction. Completion, cancellation, transition, disable and destroy clear
  velocities, return every body to kinematic, disable owned colliders, resume
  graph ownership and restore the neutral rig. Recovery adds `6 s` to the next
  normal interval. Dropping intoxication to `60` or below safely cancels the
  challenge and clears its delay.
