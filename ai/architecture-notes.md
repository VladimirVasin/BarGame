# Architecture notes

Decisions marked `Proposed` become accepted only after implementation confirms them.

## Current facts

- **Accepted:** Unity `6000.5.9f1` with URP `17.5.0`.
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
  connected `13 x 12` urban envelope with four urban areas and 144 land-use
  cells: an added central river column splits the former `12 x 12` grid while
  the eastern half shifts outward without losing a lot. The 16-cell central
  park becomes two `2 x 4` regions joined by its own footbridge. A full-width
  walkable northern beach and continuous non-walkable water row remain. The
  playable default also extends east
  with a `4 x 4` lake (walkable shore around blocked water) and a `3 x 2`
  cemetery; both use the shared open-area street-access contract and own
  deterministic runtime-composed landmarks. Roads, ground, navigation and map
  drawing consume only active cells, so connected holes and non-rectangular
  outlines remain real voids.
- **Accepted — Water is a surface the engine does not ship:** Unity has a
  full water system, but only in HDRP; URP 17 has no official water package
  and Unity's own URP samples author water as an ordinary Shader Graph.
  `Assets/Resources/Shaders/CityRiverWater.shader` is therefore hand-written
  HLSL like the project's seven others, and is written as *the* water shader
  rather than the river's: every quantity is derived from world position, and
  `_FlowDirection` is a parameter, so the sea and the lake can adopt it
  without a second shader. Deriving from world position rather than UV is
  also what makes a segment boundary invisible — two adjacent sheets agree on
  the wave and the ripple because both are functions of where they are.
- **Accepted — The water blends itself:** The surface is queued at
  `Transparent-100` but writes opaque pixels with `ZWrite On` and `Blend Off`,
  compositing against `_CameraOpaqueTexture` and `_CameraDepthTexture` in the
  fragment. Both are already required by `PC_RPAsset`. Blending by hand costs
  one texture read over alpha blending and buys three things: the water never
  sorts against anything, it stays a correct depth occluder for the
  `CityLightHalo` particles that draw at 3000, and absorption can be a
  function of measured water thickness rather than a constant alpha. The
  refracted sample is rejected when it lands nearer than the surface,
  otherwise anything standing in front of the river bleeds into it. Banding
  is applied to the specular, foam and rain terms rather than to the whole
  colour, so the sheets still read through the PS1 composite. One consequence
  is easy to get wrong: because the shader composites its own colour rather
  than being lit, `_BaseColor` and `_DeepColor` are **rendered tones, not
  albedos**. Reusing a flat surface's authored albedo here emits at full
  value what that surface reaches the screen at a fraction of, and the water
  ends up the brightest thing in the city. The same correction is owed to the
  sea and the lake when they adopt this shader.
- **Accepted — The channel has a floor:** The city deliberately emits no
  terrain under a river cell, so while the water was an opaque lid the
  channel was a hole with a lid on it. `CityRiverWorldBuilder.BuildRiverbed`
  now lays a silt floor `RiverBedDepth = 1.10 m` under the water top plus two
  submerged sides starting `SubmergedSideTop = 0.08 m` down, which laps the
  full quay wall's underside at `0.12 m` by four centimetres. The alternative
  — extending the quay wall skirt — would have re-pinned wall geometry that
  was corrected twice in the same week. Bridge piers now bottom out on that
  floor; they used to stop at the plan datum, a hand's width *above* the
  waterline, which only an opaque surface was hiding.
- **Accepted — The water sheets are not albedos:** `CityRiverWaterNormal` is
  a derivative map stored as `(-dH/du, -dH/dv, 1)` and imported **linear**
  (`sRGBTexture: 0`, the project's first such texture); `CityRiverWaterFoam`
  is a mask. Neither answers to the mean-luminance rule, the compensation
  solve or the channel-ratio bound, all of which describe a diffuse colour
  multiplied by a builder tint, so both live in a separate `waterSheets`
  block of `ArtSource/City/river-textures.json` and bypass
  `CityRiverSurfaceAppearance` entirely — they are set on the water material
  itself. They are still validated for wrap, the one contract they share.
  The fourth albedo, `CityRiverBedAlbedo`, goes through the ordinary path.
- **Accepted — River as typed infrastructure, not a district:**
  `CityRiverDefinition` belongs to the immutable blueprint and declares the
  north-south corridor plus exactly two Road bridges and one ParkPath
  footbridge. `CityRiverPlan` materializes the same contract as a `10 m`
  channel, two continuous `3 m` promenades, distinct `8 m` Works and Mouth
  road decks, a `2.8 m` timber park deck and four lower waterside landings.
  Every bridge carries two footprints: `DeckBounds` is the bank-road to
  bank-road crossing that pedestrians, the map and furniture exclusions read,
  while `SpanBounds` is the structure itself — the channel plus one
  `CityRiverPlanner.QuayEdgeOffset` seat on each quay wall. Deck planks,
  undersides, girders, piers and parapets are built on the span, so no part
  of a bridge stands on an embankment. A crossing is flat by construction —
  both bank nodes resolve to the same elevation — so the street plan's road
  or park path across it tops out at exactly `AverageY + RoadTop`. Bridge
  structure must never share that plane or those side planes:
  `CityRiverWorldBuilder.SurfaceClearance` lifts the timber deck above the path
  and widens it past the path sides, and recesses the road underside inside the
  carriageway sides and ends rather than past them, which would expose the
  parapet post bases that sit on the underside's own top plane. That surface
  is the span too: `CityStreetSurfacePlanner` insets a crossing edge to
  `SpanBounds` instead of half its own width, so the carriageway or path is
  exactly the deck and the promenade paving carries the approach — otherwise
  a crossing runs bank node to bank node and lays `8 m` of road or path over
  each embankment and into the junction pad it shares a plane with.
  Every bridge member is textured as what it is made of: `Iron` for the Works
  crossing, `Quay` for the Mouth crossing, and the park's own `Timber` sheet
  for the footbridge, which belongs to the park's family rather than the
  embankment's three.
  Each road bridge owns one bank-facing stair on both shores; the park bridge
  has no vehicle or lower-landing role. Only the two road bridges enter Route
  01, and bridge-adjacent furniture/spawn exclusions stay derived from the
  declared crossing metadata. World construction, navigation, pedestrians,
  bus routing and the City map consume the same validated plan rather than
  rediscovering the corridor from coordinates.
- **Accepted — Typed yards instead of boundary voids:** The former unmapped
  gaps behind the eastern, southern and western boundary streets are five
  `Yard` areas: one `4 x 6` pocket east of the player's home and four
  one-cell perimeter strips, each halved so it aligns to its own access
  datum on the terraced perimeter. They reuse the open-area contract
  wholesale — one declared street access, `OpenGround` surface, walkable
  `OpenLand`, guard rails on the unsafe spans — and carry no decoration in
  v1: they are authored placeholders, filled in later one at a time. The
  lot and road-grid footprint is still normalized to `(0,0)` because every
  per-cell random stream hashes raw cell coordinates; only the
  `OpenLand`/`Water` fringe may reach `-1`, and the `(-1,-1)` corner stays
  void. Yards are excluded from signature stairs and from bus-stop corner
  eligibility so the canonical city's stairs, Route 01 and home stop do not
  drift.
- **Accepted — The bar-side yard is an inter-building gap, not a fringe Yard:**
  `CityOpenAreaDecorationPlanner` derives the authored composition from the
  bar directly across `PlayerHome`'s shared street frontage, then occupies the
  walkable roadless gap immediately left of that bar, between it and the
  neighbouring supermarket. The dead tree and sparse traces therefore stay
  beside the bar instead of using the large eastern `Yard` precinct; all five
  typed fringe yards remain separate bare areas. The rider's circuit is deliberately
  unmarked — no drawn ring — and lives only in the yard site contract that
  the wheelchair plan, the slot clearances and the leaning utilities share. The same pure plan owns one
  stable wall-light descriptor. `CityOpenAreaWorldBuilder` mounts its static
  cold near-white Spot on the supermarket's yard-facing wall at intensity `240`, twenty times the
  ordinary `12`-intensity street practical. Range is the greater of `1.5x` the
  sampled throw and sampled throw plus `3 m`; only `6°` of total cone feather
  keeps the full wheelchair circuit inside the bright inner cone. The one
  source uses hard `0.95`-strength high-resolution shadows, a `4.8x` HDR lens
  and a larger, brighter source halo, but no volumetric beam. It stays enabled
  at constant intensity through day and night and never reads or tracks the
  runtime rider transform. The two `YardDeadLamp` geometry parts remain
  non-emissive and create no `Light`.
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
- **Accepted — One deterministic city elevation plan:** The
  existing 2D blueprint and road topology remain the first generation stage,
  but the default coastal blueprint now creates one immutable
  `CityElevationPlan` before lots and surfaces are spatially materialized.
  Its river branch replaces the former north-profile/east-bias formula: each
  bank starts `1.8 m` above the local water and terrain rises `0.98 m` per
  node away from the channel. Default road nodes therefore span about
  `8.1 m`, peak near `10.08 m`, and retain at least `1.5 m` within every urban
  district. Sea water stays at datum `0`; the lake is one local elevated basin
  set from its access rather than from a global datum, with an intentionally
  blocked physical shore-to-water drop of about `0.4 m`. The river descends
  monotonically from `2.4 m` in the south to the sea datum. Legacy/custom
  blueprints retain the exact flat fallback.
  `CityTerrainSurfacePlan` is the authoritative top sampler for
  `BuildableGround`, `ParkGround`, `OpenGround` and `Beach`. It interpolates
  the surrounding road-node datums across each cell and keeps the road-edge
  plateaus, so adjacent cells form one continuous terrain surface instead of
  stacked slabs with vertical seams; the beach has its own continuous descent
  to the waterline. `CityTerrainSurfaceWorldBuilder` materializes that contract
  as triangulated render meshes with matching mesh colliders, while declared
  special-purpose flat surfaces keep their flat contracts. Building and public
  place foundations extend downward without moving their authored tops. Park
  plazas use closed terrain-conforming meshes; each district public place owns
  an exact flat terrain pad under its slab and a `4 m` smooth blend back to the
  continuous cell, so neither feature is pierced by the surrounding slope.
  All node/cell positions, surface datums, doors, returns, stops, waiting
  slots, pedestrians and debug teleports read the elevation or terrain sampler
  rather than adding an absolute Y.
  Declared bridge decks may cross non-walkable water and river stairs may
  reach their own lower platforms. Tunnels and overlapping walkable levels at
  the same XZ projection remain outside this navigation architecture.
  Street intersections and stop pads are level. Between their `4 m` setbacks,
  oriented road/sidewalk strips may grade up to `6%` for Street/Route 01 and
  `8.3%` for pedestrian ParkPath. The bus route excludes non-bus transitions,
  samples Y and grade tangent, pitches without roll and grounds stop/ride
  anchors locally. Every urban district also owns one signature sidewalk
  stair: `6-12` visible collider-free steps, `0.15-0.17 m` rise,
  `0.30-0.34 m` tread, two `1.5 m` landings, physical rails/retaining walls
  and exactly one continuous hidden ramp collider. A grade-safe roadway stays
  beside it for Route 01. One shared `CityRoadGroundBoundaryPlan` classifies
  every road-to-ground seam from its sampled endpoint heights: a span becomes
  a radius-safe walkable connector only while the complete edge stays within
  the `0.28 m` controller step, otherwise segmented physical guards follow the
  actual terrain slope. Ground-to-ground connectors use the same sampled-edge
  predicate. Signature stair footprints are the explicit guarded-cut
  exception. The player
  contact patch raycasts to the live surface normal, and balance checks refuse
  to start above a `12°` surface angle so a stair flight cannot begin a fall
  sequence it was never designed to recover on.
- **Accepted — Data-driven indexed walkable mask:** Player motion is
  constrained to a spatially indexed union of XZ streets, park lawn,
  blueprint `OpenLand` and the complete logical `BuildableGround` regions.
  Radius-safe connector rectangles overlap only road-to-ground and
  ground-to-ground spans whose sampled physical edge is step-safe; every other
  span is physically guarded. The graded physical road, continuous terrain and
  one hidden ramp per stair flight own all climbing. `CityVerticalTraversalAudit`
  inventories every ground seam and authored road frontage, classifies it as
  authorized, unsafe or unclassified, and records deterministic reachability
  from the spawn road component in `CityVerticalTraversalPlan`. Water,
  unmapped cells and space outside the active footprint remain excluded;
  buildings and other visible obstacles are governed by their physical
  colliders instead of being carved out of the macro walkable mask.
- **Accepted — Road v2 deterministic street corridor:** The canonical default
  street footprint is `8 m`, so `CityStreetSurfacePlanner` partitions every
  ordinary street into a dark `6 m` carriageway and two `1 m` sidewalks raised
  from the local datum's `+0.08 m` road top to `+0.14 m`; ParkPath remains
  independent. The
  default grid step is therefore `26 m` for an `18 m` block. The pure plan also
  owns center dashes, an `8 x 8 m` intersection core with a clear `6 x 6 m`
  carriageway apron, intersection corner pavement, radius-query rectangles and
  four-stripe zebra approaches. One shared stable selector chooses at most six
  degree-3+ Street-only intersections that are clear of ParkPath and blocked
  public space, so paired signals and crossings cannot drift. Dashes are
  excluded from intersection and zebra bounds. City builds physical graded
  strips as combined oriented meshes with level node pads; Home consumes the
  same plan in local space without collision. Generation settings reject
  widths that leave no positive
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
- **Accepted — One route-driven real-scale bus on canonical Route 01:** City
  layout produces immutable `bus-route:default-coastal:route-01`, one
  deterministic right-hand, Street-only closed winding service loop. The target
  set contains every district point of interest that actually exists plus
  `PlayerHome`, and its **order is a shortest closed tour over the target
  centres**, not the district enum. The enum order was nominal — Industrial,
  Nightlife, Residential, Old Town, home — and on the default layout it ran
  west, south centre, far north-east, back west and out east again: two full
  crossings, `1166 m` of straight-line tour where `754 m` was available, and a
  `2592 m` road loop. Reordering alone brought that loop to `1798 m`, a `31%`
  cut, without touching a single clearance or right-hand proof. Five targets
  are solved exactly by fixing the first and permuting the rest; a layout with
  more than `8` falls back to nearest neighbour plus 2-opt. Equal-length tours
  are broken by the ordered target IDs, and the cycle is rotated so
  `PlayerHome` is served first with its direction fixed the same way, because
  a closed loop has no last stop to lose and home is the one stop the hero can
  name. The default is therefore Home, Residential, Old Town, Industrial and
  Nightlife. Each semantic stop is assigned to a safe straight on the
  target frontage or one connected road edge away. The river layout preserves
  that Home rule but lets a POI use the nearest cyclic Street in the same
  district, bounded to five grid edges and `120 m` from its public access. Its
  roadside cell differs from the target cell, and the physical blue `01` pole
  stays outside POI public/access bounds or the Home footprint.
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
  vehicle, not a guarantee that a bus is always visible. While the hero is
  outside it yields to predicted player and pedestrian motion. That prediction
  extrapolates `0.75 s` ahead, so its input is smoothed over `0.2 s` and
  clamped to the `5.2 m/s` the motor can actually produce: an unsmoothed
  single-frame delta turned a `0.05 m` wobble on a `5 ms` frame into `10 m/s`
  and braked the bus for a phantom six metres away, and a frame that moves the
  hero past `4 m` is read as a teleport and yields no velocity at all.
  Route travel likewise carries any sub-`DistanceTolerance` remainder into the
  next frame instead of discarding it. Dropping it was a latch rather than a
  rounding loss: approach speed rides the service-braking curve exactly, so at
  `60 fps` a frame moves under `0.02 m` once the stop is within `0.31 m`, and
  because the discarded travel left the distance unchanged the speed cap never
  recovered. The bus parked a third of a metre short of the stop node,
  `BeginDwell` never ran, and the dwell-driven doors stayed shut until a long
  enough frame happened along. While the hero
  is attached as the sole passenger, that same hero is omitted from obstacle
  prediction but pedestrian yielding remains active. Camera direction, frustum
  membership and far-clip state never control spawning or recycling. The kinematic box
  body uses the dedicated `CityBus` layer: it collides with the player and
  pedestrians, ignores another bus and is excluded from camera and interaction
  queries. The passive production prefab stays collider- and `Light`-free, owns
  a modeled driver area, dashboard, twelve passenger seats, rails and two
  double-leaf doors, and matches the same real dimensions without runtime scale
  correction.
  The separate passive `CityBusDriver3D` production prefab uses the shared
  `Player3DLit` material and an exact 31-bone rig. Its normal low-poly head keeps
  the slightly bizarre identity in long horizontal eyes rather than distorted
  anatomy. Runtime procedural seated IK keeps both hands aligned to the rotating
  steering-wheel grips. The deterministic door/driver timeline moves only the
  right hand to the physical dashboard button for opening and closing, drives
  its real `12 mm` press travel and leaves the left hand on the wheel. During
  `Opening` the driver turns toward the front door, holds that look through the
  open phase, then returns during `Closing`. The four separate eye renderers
  provide deterministic pooled blinking. A hero within the `2.25-2.75 m`
  front-entry focus band on the door side overrides the static look anchor;
  the driver tracks the hero head and extends the connected neck/head segment
  by up to `0.10 m`, capped at `1.35x`, before restoring position and scale. The
  timeline preserves the fixed `10 s` dwell and `0.70 s` opening/closing
  transitions; driver bones, wheel, button, look and timeline all return to
  neutral when the actor enters its pool.
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
  12-light city-atmosphere pool, so atmosphere plus one active bus reaches a
  16-light subtotal. The single pooled helmet Spot and the fixed yard Spot can
  add one each, making the bounded worst case `18` local realtime lights; the
  scene Directional and transient lightning Directional are separate. The City
  map consumes the same immutable plan, simplifies its closed geometry, draws
  a blue ink-outlined loop below the orange player itinerary and adds five
  numbered localized stops in the default layout plus a compact legend. It
  deliberately has no live bus marker.
  The City-only passenger MVP uses three ordinary Default-layer trigger children
  instead of admitting the solid `CityBus` body to general interaction queries:
  one at each front/rear passenger door and one at passenger seat `07`
  (zero-based anchor index `6`), the first window seat on the lateral side
  opposite the driver.
  Both exterior triggers use the ordinary `PlayerInteractor` and
  `InteractionPromptView` E/Enter/gamepad/pointer path. A board prompt is valid
  only while the bus is dwelling with doors fully open and a deterministic
  walkable external dock exists. Each dock keeps the complete player capsule
  outside the bus obstacle corridor, so a waiting passenger does not make the
  bus yield before reaching its service pose. The controller resolves the
  closest valid door-specific plan and retains that front/rear choice for the
  later exit. Every entry and exit candidate resolves its grounded root height
  from the same deterministic `CityStreetSurfacePlan.Sidewalks` bounds used to
  construct physical City geometry: a raised sidewalk uses its `Bounds.max.y`,
  while a cut-back bus-intersection apron uses `RoadTop`, in both cases plus the
  player grounded-root offset. A road-to-sidewalk curb delta is accepted only
  when it fits the live `CharacterController.stepOffset`; the shared positioned
  approach still rejects genuinely unreachable levels.
  `CityBusRideController` acquires an owner-scoped service hold so the `10 s`
  dwell cannot close the doors mid-transfer, then uses the shared
  positioned-interaction path and `BusBoardEnter` to pass through the selected
  live doorway waypoint into that fixed opposite-driver seat. The production
  rig remains
  visible; its pelvis follows the sprung seat Transform during entry and the
  looping `BusRideLoop`. At loop handoff the gameplay root remains under its
  original parent while ordinary motor, `CharacterController` and contact
  shadow are disabled. `CityBusRideController` runs after the director and
  late-synchronizes that root to the stable actor-local seat pose; this preserves
  moving-frame alignment without making Player a child of a slot that may be
  deactivated. A seated camera follows the sprung seat position from a fixed,
  safe aisle-side offset, but derives orientation from actor forward and world
  up so suspension pitch/roll cannot tilt its horizon or couple its axes. Its
  zero-input direction is derived from the selected seat's lateral side and
  looks through the nearest window instead of inward or down. Entry and exit
  rotation blends interpolate look directions and reconstruct against world up,
  avoiding transient quaternion roll. While riding, the controller consumes
  the shared orbit sample: RMB
  mouse movement and the gamepad right stick rotate bounded yaw and pitch in
  place, while the existing orbit-input flag remains a modal-lock gate rather
  than bus ownership. Entry/exit blends and exact ordinary-camera restoration
  remain fixed-pose transactions.
  The actor records one passenger owner and cannot be pooled or released while
  that owner remains. The director owns a passenger-cleanup callback for forced
  disable/shutdown. The exit prompt is unavailable until the service ordinal is
  strictly greater than its boarding value, which permits the next or any later
  stop but rejects the same dwell. Exiting reacquires the service hold, detaches
  the logical moving-frame binding, freezes the moving seat target and requests
  the independent `BusAlightExit` pose through the same selected live door
  waypoint onto a walkable grounded roadside dock. The camera blends to the
  ordinary resolved chase pose. Normal completion and every
  cancellation/lifecycle path restore motor, collider, contact shadow and
  camera exactly once without Transform hierarchy mutation, release
  service/passenger ownership and use the last safe exterior dock when an
  authored exit cannot finish. Fare/payment, destination selection, passenger
  persistence, traffic-signal simulation and live bus tracking remain deferred.
- **Accepted — A three-place cabin counting the hero, with declared seated
  riders:** `CityBusActor` owns `CabinCapacity = 3` occupants and a shared
  per-owner service hold. Both replace single-owner fields, and both had to:
  with one exclusive hold, an ambient passenger stepping through the doorway
  would silently have made the hero's own `E` prompt fail, and
  `CityBusDirector`'s passenger cleanup would have thrown on its second
  registrant. Cleanup is therefore multicast, while the release
  post-condition is unchanged — no occupant may remain when the presentation
  is pooled. Ambient passengers may take at most `CabinCapacity - 1` places, so
  seat `07` stays reserved and the hero is never locked out of his own bus;
  they fill a stable order of the other eleven anchors biased to the
  driver-side row and rear bench, which are the seats a hero in `07` actually
  sees. The cabin reserves a place logically and lets
  `CityBusRidePlan` refuse a seat index the registry cannot resolve, rather
  than duplicating that check. Recycling keys on `HasPlayerPassenger`, not
  `HasPassenger`: only the hero pins the single actor slot to the world, and
  blocking it for a rider `92 m` away behind fog would strand the bus for a
  whole lap, so ambient passengers are released with it through the same
  cleanup. `CityBusRidePlan.TryCreate` proved agent-agnostic apart from two
  hard-coded facts; parameterising seat index, agent radius and grounded-root
  offset reuses the whole validated dock ladder — including the
  `CityStreetSurfacePlan` curb/apron height resolution — for a walker whose
  root already sits on the pavement surface.
  `CityBusStopWaitPlanner` derives one wait point per stop that has usable
  pavement: the sidewalk centreline `RoadsidePoleOutsideRoadEdge +
  SidewalkWidth * 0.5 = 0.70 m` road-ward of the blue `01` pole, because the
  pole itself stands outside the walkable strip and carries a collider. Two
  slots per stop queue along the lane at `+0.30 m` and `+1.40 m` from the halt
  pose — never abreast, since a `1 m` pavement minus a `0.35 m` agent is the
  same geometry that already rules out passing, and both offsets lie in the
  clear span between the `+3.05 m` front and `-1.34 m` rear door entries. Each
  wait point also owns a single-source Dijkstra field over the pedestrian
  graph. Stops never move, so that search is solved once in the plan instead
  of being re-run every `4 m` the way player guidance must be, and routing then
  reuses the director's existing `approachTarget` and node-distance guidance
  seeded at the stop.
  A newly activated bus seats a seeded `0-2` ambient passengers before it is
  ever seen. It spawns in the same fixed-fog `76-86 m` band the population
  director already proved hides an appearing walker, so there is no visible
  pop, and a bus that has notionally been running its loop for a while does not
  always arrive empty. That preload needs no stop and no dock:
  `CityBusRidePlan.TryCreateSeatedPose` resolves the actor-local seat floor
  from the seat anchor and the cabin-floor door anchor alone, because a full
  ride plan requires a served stop and two validated roadside docks that do not
  exist while the bus is cruising. The static clearance probe is skipped for
  exactly these spawns — the capsule overlaps the bus body on purpose, which is
  what the probe exists to reject everywhere else. The draw spans `0` to
  `MaximumNpcOccupants` inclusive, so an empty bus stays a real outcome and the
  hero's place is never taken.
  `CityBusNpcPassengerController` recruits an ordinary roaming walker within
  `55 m` of a stop along the graph so the hero can watch the whole approach,
  and activates a waiter straight onto its slot only where the stop is already
  beyond the proven `76 m` fog band. Boarding takes the shared hold, runs a
  short scripted doorway walk whose budget is measured rather than assumed: a
  flat constant cannot serve an aisle leg of `1.16-2.56 m` walked at
  `0.72-1.30 m/s`, so it is derived per transfer from the real
  `pavement -> door -> seat` path and that walker's own pace and clamped to
  `3 s` up to one whole dwell. The door is chosen by the whole journey, not by
  which one the walker stands nearer: the doors are `4.39 m` apart on the same
  kerb, and choosing by the outside leg alone sends a passenger `6.60 m` down
  the aisle where `2.56 m` is available. Authored pace is kept rather than
  hurried, because each design owns its cadence and speeding the root reads as
  foot-sliding. An overrun aborts back to the pavement rather than stalling
  the fixed `10 s` dwell; alighting draws a seeded
  strictly later service ordinal, the same rule the hero's exit prompt already
  enforces. NPC transfers deliberately do not use
  `PlayerAnimatedInteractionController`: it is bound to `PlayerRuntime` and
  `IPlayerClipPresentation`, and `ai/contextual-animation-standard.md`
  explicitly does not govern NPC animation. A route-bound walker is exempt from
  the `88 m` pedestrian recycle rule, from distant simulation acceleration and
  from the bus's own pedestrian yielding, which would otherwise make the bus
  stop for its own passenger.
  Seating is one rule for the whole catalog because every design copies the
  hero's exact 31-bone rig at a `0.70 m` rest pelvis: `CityPedestrianPresentation`
  aligns that bone to the cushion anchor, the same technique
  `CityBusDriverPresentation` already uses for the driver. Sole pinning is
  switched off while seated — on a seat it would drag the model down until the
  boots touched the cabin floor — and the mixer gains a third Sit input.
  **Accepted — Declared seated rides over a blanket allowance:** a design may
  ride only by declaring `CityPedestrianArchetype.SeatedRide`, which owns its
  pelvis lift, back offset and headroom, and by owning an authored `Sit` loop
  in the shared locomotion library. The Helmet Lamp Hopper declares none: it
  has no seated posture to author on `0.46 m` hind feet, and its worn Spot is
  the one working light the pedestrian contract allows. A seated clip is
  excluded from the footwear bake, since it leaves the pavement plane on
  purpose, and proves a different contract instead — measured headroom above
  the seated pelvis inside a declared `seated_clearance_m` band, and nothing
  hanging more than the `0.41 m` cushion height below it. The four riders
  measure `1.030 / 1.055 / 1.050 / 1.050 m` of headroom and `0.354-0.374 m` of
  drop against a `2.05 m` cabin, so the catalog clears the roof with room to
  spare.
  The moving runtime remains City-only. A valid Home/Balcony route would
  require a real Street pass-through whose two complete-body seams both lie at
  or beyond the fog-hidden `56 m` boundary; none exists, and the default home
  facade faces a visible road terminal. Extending that road only for
  presentation would falsify the generated city, while enabling or pooling a
  bus with the Balcony camera would create a visible camera-dependent pop and
  violate the lifecycle contract. Home therefore keeps its pedestrian exterior
  runtime and reconstructs the nearby Home stop as a static collider-free pole,
  but composes no bus actor or director.
- **Accepted — a contextual effect reads its host clip's phase, never its own
  timer:** the lake fisherman's pipe ember, its point light and its plume are
  all functions of `LakeFishermanPresentation.BreathPhase`, derived from the
  leaning clip's normalized time against a constant that mirrors the authored
  key grid. A second free-running timer would be simpler and is wrong within a
  second of watching him: smoke that swells while the ribs are still filling
  reads as a particle system parented to a man rather than as smoking. The
  same rule is why the plume's lag is expressed as a fraction of a breath
  rather than as seconds. It also constrains what the clip may key: his breath
  moves the spine chain only, because both clavicles hang off the chest and a
  breath authored on them would open his two-handed grip on the rod once per
  lap.
- **Accepted — the second bench sitter duplicates his neighbour's driver
  rather than sharing one:** `ParkCheckersPlayer{Plan,Factory,Presentation}`
  is a near-copy of the chess player's quartet, and that is chosen rather than
  inherited. Every staged character in the library already owns its own
  passive quartet, `ValidatePassivePresentation` alone is duplicated ten times,
  and `CityGameRoot.ParkChessPlayer` is typed on the concrete presentation, so
  extracting a base would edit a shipped, validated character for no
  player-visible gain. The stronger reason is that the two numbers worth
  sharing must not be: `PerchPelvisLiftMeters` and `FocusHeightMeters` are
  measurements off one design's own meshes and one design's own pose, and a
  shared constant that drifted would fail no test — it would merely sink a man
  an inch into his bench. They come out equal here (`0.0651`) only because the
  geometry below the neck was authored identical on purpose, and each file
  names its own source for the value. **A third bench sitter is the point at
  which the extraction earns itself; two is not.**
- **Accepted — the draught takes the 1.75 m envelope lying down:** the
  canonical height is enforced to ten microns on every archetype, so a hat
  cannot simply be lower than the neighbour's to read as lower. The checkers
  player's silhouette therefore inverts on axis and width rather than on
  height: where the king's cross reaches the ceiling standing straight up, one
  thick draught reaches it raked back, spending the whole allowance sideways.
  Its rake is a derived quantity — given the radius and where the piece beds on
  its band there is exactly one angle whose raised edge lands on `1.750`. What
  actually sets the radius is the face, not the read: a bench sitter's head is
  below the player's eye, so a wide plate lying near-horizontal curtains the
  face from the only angle the game offers, and three review renders were spent
  discovering that the radius governs that far more than the rake does.
- **Accepted — two loops on one bench differ in rhythm, not only in phase:**
  the two men are seen together from the park approach, and a phase offset on
  one shared clip gives itself away to anyone who stands between them long
  enough. `CheckersMull` is therefore its own authored clip with a shallower
  breath and its settle at a different point in the lap. This is also forced
  rather than merely preferred: actions are handed to a design by `design_id`
  and `ACTION_BY_NAME` is keyed on the clip name alone, so a shared name would
  either leave the new archetype with nothing baked or overwrite its
  neighbour's entry. The cost is paid back — the perch validator now proves all
  288 frames against this design's own meshes instead of somebody else's.
- **Accepted — a bench sitter is proved against its seat, not against the
  pavement or a cabin:** the park chess player is the art library's first
  design whose idle is seated on world furniture, and neither existing
  grounding rule describes him. The walker rule pins the lowest sole to the
  ground plane, which would drag him down until he stood on the lawn; the bus
  rule (`seated_clearance_m`) measures headroom from the seated pelvis to a
  roof a park bench does not have. He therefore declares
  `perch_seat_height_m`, and the quantity it measures is deliberately not a
  bone position but the distance from the underside of the hip geometry — the
  seat of the coat, which is what physically rests on timber — down to the
  soles. That distance IS the height of the drawn plank (`0.540 m`), so the
  contract can be checked directly against the world instead of against a
  guessed flesh allowance, and the runtime's correction is the same number
  read the other way. Thighs are excluded from the hip measurement on purpose:
  on a high bench they slope down toward the knees, so including them would
  report the knee rather than the seat. The validator additionally reports
  which part reaches the ground, because a seated design has two candidate
  feet and a tucked foot that outreaches the planted one silently describes
  the wrong leg while still passing a height band.
  **Contrast with the removed `perch_clearance_m`:** the fisherman grew a
  seated contract and lost it when he was authored standing, on the rule that
  an unused declared contract is worse than none. This one is used by the only
  design that declares it and is read by the runtime that seats him.
- **Accepted architecture exception — there are pieces on the boards, and
  they are in the starting position:** `ai/city-zones-art-bible.md` §10 listed
  «фигуры на досках» among the things the chess set may not have, next to a
  second player and a third sitter. The reasoning was sound and is preserved:
  a position mid-game states that somebody opposite is moving, which is the
  one thing the whole precinct is built to deny. Both sets are therefore laid
  out untouched, and no runtime path moves a man. The result sharpens the
  emptiness rather than spending it — the plank opposite each of them stops
  being a table nobody plays at and becomes a game nobody started. What the
  ban was actually protecting is now stated positively in the bible: no
  position but the opening, no captured men beside the board, no clock, no
  scoresheet, and no draughts king, because a king is evidence of a game.
- **Accepted — the drawn board's dark parity is derived from the seats, not
  chosen:** both planks face along the chess recipe's `+Forward` and
  `Tangent = (-Forward.z, 0, Forward.x)` runs to the left of anyone facing
  that way, so the near player's right-hand corner is `(file 0, rank 0)` and
  the far player's is `(7, 7)`. A board is correct only when both are light,
  and on an even board a half turn maps corner to corner, so the two share a
  parity and one rule settles both: the dark squares are the odd ones. That
  is also what puts `a1` dark and the white queen on a light `d1`. The recipe
  originally drew the even parity, which is a board that fails at a glance to
  anybody who plays — and which was invisible for exactly as long as the
  boards were empty. `CityChessBoardGeometry` now owns the rule and the five
  numbers the lattice and the men both need; two copies of `0.15` in two
  files is how a set ends up half a square off its own squares.
- **Accepted — the chess set is imported geometry, and the knight is why:**
  everything else this city draws is a box or a stack of them, and a turned
  chess piece could have been stacked cylinders. A knight cannot. Its whole
  job is to be told apart from a bishop at four metres, and it is a drawn
  profile: chest, throat, jaw, nose, the stop under the brow, forehead, poll,
  crest and mane, extruded across four slices with the outer two scaled in.
  The first attempt built it from five rotated boxes and rendered as a flag
  on a pole. Since one design already had to be authored, all seven are,
  which also buys a single triangle budget, a single height-ladder validator
  and one review render where the six silhouettes are checked side by side.
  The runtime cost is nil: `RuntimePrimitiveFactory.CreateCombinedMeshes`
  bakes `56` men into four meshes on the same world-UV contract the box
  batches use.
- **Accepted — the men are imported through a mesh provider rather than a
  prefab, and that is what forces three non-default import settings:** every
  other model in the project instantiates its imported hierarchy, and that
  hierarchy quietly carries two corrections — a `scale = 100` root and the
  Z-up-to-Y-up rotation. Fifty-six GameObjects on a park table is not a thing
  to do, so the meshes are used bare and both corrections have to be baked
  into the file instead: `apply_scale_options="FBX_SCALE_ALL"` and
  `bake_space_transform=True` on export. The third is `isReadable = true` on
  the importer, because `Mesh.CombineMeshes` reads vertices at runtime and an
  unreadable source combines into nothing — silently, in a player build, on a
  board that comes up empty. All three fail invisibly: the model preview
  looks perfect either way, which is why the editor validator asserts imported
  height, origin and readability rather than trusting the art manifest.
- **Accepted — the quarrel's shout is an authored clip, not a procedural bone
  overlay:** the project has both idioms. Every stationary NPC beat
  (`WatchmanWatch`, `MournerMourn`, `BabushkaBeat`, `WeigherCheck`) is a baked
  clip the art build validates on posture and loop closure; separately, four
  files (`BarPatronDrinkingArmPose`, `BarBartenderPresentation`,
  `SupermarketCashierPresentation`, `HomeTeethBrushingArmPose`) carry a
  recorded exception that solves bones in `LateUpdate`. The shout takes the
  first. `CityPedestrianAssetRegistry` exposes only `Head`, `Pelvis` and the
  two feet, so an overlay would have had to find `neck` and `upper_arm.L` by
  name walk, and it would have left the one thing these two designs do out of
  the clip manifest, out of the perch validator and out of every review
  render. The four existing exceptions are also all *continuous* additions to
  a held pose; this is a beat with its own silhouette, which is what a clip
  is for. Cost: a `ChessJeer`/`CheckersJeer` pair, a third optional clip slot
  on the shared registry, and an `~11 MB` rebake of the shared library.
- **Accepted — both men shout with the same unmirrored pose:** the natural
  assumption is that seats facing each other need mirrored turns, and it is
  wrong here. The chess seat is `-seat-a1` at local `(-1.85, -1.10)` facing
  `+Forward`; the draughts seat is `-seat-b2` at `(+1.85, +1.10)` facing
  `-Forward`. Because `Tangent = (-Forward.z, 0, Forward.x)` points to the
  left of anybody facing `+Forward`, projecting the separation onto each
  man's own frame gives the same pair of numbers twice: the neighbour is
  `2.2 m` ahead and `3.7 m` to his **left**. The seats are a 180-degree
  rotation of each other, so each sees the other over the same shoulder.
  Both therefore turn left and both throw the left arm — the arm goes up on
  the side the head went — and `park_jeer` is one pose function used twice,
  in the same way `checkers_player_base_pose()` already returns the chess
  player's body. Two clip *names* remain necessary because `ACTION_BY_NAME`
  is keyed on the name and actions are handed to a design by `design_id`.
  **This is the fact to re-derive if either seat ever moves**, because
  authoring it the wrong way round leaves one of the two shouting into a
  hedge and no test can see it.
- **Accepted — the line appears on the clip's phase, and the panel is
  measured before it is typed:** the shout opens the bubble the frame
  `TauntPhase` crosses `0.22`, the authored full-extension key, rather than
  when the controller decided to shout. That is the fisherman's rule applied
  to a second host, and it is why `CityParkQuarrelController` declares
  `[DefaultExecutionOrder(320)]` — both presentations evaluate their graphs in
  an unordered `LateUpdate`, so a reader of this frame's phase has to be
  behind them. Once open, the typewriter runs on the view's own unscaled
  clock: that is UI time, not world time, and the rule does not reach it.
  The panel is sized once from the whole line and only the drawn substring
  grows, because `GUIStyle.CalcHeight` on a growing string steps the box a
  row taller mid-word.
- **Accepted — a line nobody said to the hero is drawn over the speaker, not
  in the prompt panel:** `InteractionPromptView` is the hero's own channel —
  what he can do, and what he was just told when he asked. A quarrel he is
  merely standing next to has no business in it, and putting it there would
  also make two men look like they were addressing him. `NpcSpeechBubbleView`
  is a separate IMGUI layer at `GUI.depth = -75`: above the intoxication HUD,
  below the interaction prompt, the city map and the pause menu, so it never
  covers anything the player operates. It is not uGUI/TextMeshPro — that
  would be the project's first `Canvas`, an asmdef reference and a committed
  Cyrillic font atlas, where the built-in IMGUI font already renders all
  `261` Russian entries. It is not world-space geometry either:
  `Ps1CompositeRendererFeature` averages the frame to `640x360` and quantizes
  to RGB555 *before* UI is drawn, so a panel in the world would be crushed
  while this one stays sharp.
- **Accepted — the chess lamp's wire crosses the set rather than running along
  it:** the obvious span is along the line of the two tables, one lamp over
  each board. The park's own geometry refuses it. Trees are planted on an 8x8
  grid and the decoration planner then rejects any chess-set position within
  `4.8 m` of a trunk, which seats the set in a gap between tree rows: on the
  table line the nearest trunks stand about five metres off-axis, so a wire
  between them passes beside the set and hangs its lamp over grass. Across the
  set the same field offers a pair almost on the line. So the span runs on the
  set's forward axis and its one working lamp hangs over the middle, covering
  both boards — which is also the wider circle the fixture was chosen for. The
  knot takes the trunk face nearest the set (`TrunkTieInsetMeters`), which is
  both physically how a wire is tied and worth `0.26 m` of centring. A hook
  pole is the fallback for a seed whose tree field offers nothing on one side;
  it is a fallback, and the authored city is pinned to using real trees.
- **Accepted — a head-in-hands loop breathes on the chest, never on the neck:**
  the exact inversion of the fisherman's rule and for the same underlying
  reason. His hands held a rod carried by his own fist, so his breath was free
  to move the neck and head and had to avoid the clavicles. This design's
  hands hold his head: the skull rides chest -> neck -> head and both palms
  ride chest -> clavicle -> arm, so keying the spine and chest carries all
  three as one rigid piece, while a breath on the neck or head slides his face
  out of his palms once a lap. The settle is small for a second reason the
  fisherman never had — his elbows rest on a fixed board, and every degree at
  the chest slides them about five millimetres across it.
- **Accepted — A staged wheelchair NPC is not a production archetype:**
  `pipeback_roller_v1` is a complete passive presentation asset, but it lives
  under `Assets/Pedestrians/Staged/` rather than `Resources`. Its strangeness
  belongs to the chair's asymmetrical organ-pipe back and breathing bellows,
  not to the rider's disability. The seated rider preserves the exact
  production 31-bone Generic skeleton contract, and the whole model reuses the
  `Player3DLit` material;
  the non-deforming `PIVOT_Wheel.L/R`, `PIVOT_Caster.L/R`, `PIVOT_Bellows`
  and `PIVOT_PipeBank` transforms declare future procedural anchors without
  changing the Avatar. Its current `PipebackIdle` and `PipebackRoll` loops are
  exact 31-bone, in-place skeletal clips: the raised levers frame the hand
  path, the root-bound chair stays planted, and the bellows/pipe load follows
  pelvis/chest motion. No auxiliary pivot curves or distance-driven wheel
  motion are claimed at this staged milestone. The prefab can be inspected and
  sampled through its `CityPedestrianAssetRegistry` and passive
  `CityWheelchairNpcAssetRegistry` bindings without acquiring a runtime actor,
  collider, light, audio source, interaction or persistence.
  Isolation is structural rather than conventional:
  `CityPedestrianResources.OrderedArchetypes` remains the only production
  catalog, no directory scan discovers staged prefabs, and the City/Home pool
  compositions remain `13` and `8`. Consequently the Pipeback Roller cannot
  roam either exterior, wait at a stop or occupy Route 01 merely because its
  imported asset exists.
  **Deferred — production wheelchair locomotion:** registration requires an
  accessible graph that excludes stairs and proves curb/turn clearances, a
  chair footprint instead of the ordinary `0.35 m` pedestrian capsule, and
  wheel-contact presentation that derives independent drive-wheel rotation and
  caster steering from actual motion instead of grounding a pair of shoes.
  Route 01 also needs an explicit accessible boarding and securement design;
  the ordinary pelvis-to-seat passenger transfer is not applicable to a rider
  who remains in their chair. Until those contracts exist, moving the prefab
  into `Resources`, adding it to the catalog or declaring a seated ride is an
  architecture change, not an asset toggle.
- **Accepted — Local player-relative street pedestrians:** City layout and session
  seed produce one immutable, radius-safe graph over sidewalk lanes, junction
  turns and explicit three-link zebra connectors. Recursive 2-core pruning
  removes every reachable dead-end branch before runtime. Long sidewalk links
  expose deterministic spawn anchors. A `CityPedestrianPopulationProfile` owns
  the complete runtime population, so City and the Home balcony scale
  independently instead of sharing one constant: City runs `8` daytime and `3`
  night slots over a `13`-presentation pool, Home runs `5` and `2` over `8`.
  A director-local runtime random stream mixes
  plan seed, activation time and instance identity, then independently samples
  candidate rank, motion/palette values and every delay. The first event waits
  `1.25-7.5 s`. One event activates at most `2` slots, and while the population
  is below its profile target the next event follows in `0.4-2 s`; once the
  target is complete, only replacements remain and the long `3.5-12.5 s`
  cadence returns. Failed searches retry after a randomized
  `0.8-2.4 s`. A slot may activate only at a unique, obstacle-safe anchor
  in the preferred `76-86 m` band from the player. With City's fixed `0.070`
  Exp2 fog, the inner edge
  retains less than `0.2%` scene transmittance even at the widest production
  `70-degree` 16:9 frustum corner after a conservative combined `6 m` camera
  and full visual-envelope depth offset. A nearer ring was evaluated and
  rejected: that proof measures depth *along the view axis* at the frustum
  corner, only `0.574` of the radial distance, so the same bound is not met
  until roughly `72 m` radially — a `44-56 m` ring leaves `16%` transmittance,
  which is a visible silhouette rather than fog. Population size, batch fill
  and forward bias close the street instead of a shorter approach.
  Candidate search collects eligible anchors into reusable buffers and probes
  static clearance on at most `4` sampled picks rather than on every anchor,
  and one `Physics.SyncTransforms` covers a whole spawn batch. A candidate must
  also disperse: it keeps `12 m` from every active walker and at most `2`
  walkers share one sidewalk lane, which is one side of one street edge. The
  fallback ladder drops connectivity before it drops dispersion, because a
  walker farther away still reads as city life while two stacked on one lane
  does not; only the last resort relaxes both. When that band offers only
  disconnected sidewalk components whose closest point remains outside the
  `24 m` encounter radius, selection falls back to a connected obstacle-safe
  anchor at `32-86 m`; the nearer bound remains inside dense fog. A walker
  remains active
  regardless of camera direction or frustum membership and returns to the pool
  only after crossing beyond `88 m` from the player. To prevent daytime
  slots remaining occupied by invisible actors, at most `2` walkers at a time
  follow the eligible non-backtracking continuation with the shortest physical
  graph distance to the nearest player-side node in its connected component
  until they first enter a `24 m` encounter radius. This guidance
  then stays disabled for that spawn so ordinary seeded roaming resumes instead
  of turning into pursuit. Every other walker takes a seeded `50/50` initial
  direction with no player-proximity preference at all, so a busy street shows
  opposing streams rather than a crowd converging on the hero. That shared
  guidance search is one multi-source Dijkstra over the whole graph, so it uses
  a binary heap and recomputes only when the player has moved past `4 m` and
  the nearest node per component actually changed. When the player travels
  faster than `3 m/s` — a bus ride, above all — candidate selection prefers
  anchors in the smoothed forward half-plane, because anything spawned behind a
  `6 m/s` vehicle is outrun before it can be seen; a per-frame jump beyond
  `12 m` is read as a teleport and clears that heading instead of biasing it.
  Player-distance-only simulation acceleration rises
  smoothly from authored pace at `32 m` to
  `2.75x` at and beyond `76 m`; this advances both inward approaches and
  outward recycling without reintroducing a camera dependency. Strict night is
  before `06:00` and from `19:00`: it keeps authored simulation pace, activates
  exactly one walker per event, waits
  `15-35 s` for the first event and `30-70 s` for every later one including
  while still below its own population, and retries
  failed searches after `4-10 s`. Walkers already alive at dusk are not
  culled by the clock and leave only through the same distance rule. Actors
  never reverse at artificial route endpoints: they continue through graph turns,
  avoid immediate backtracking and make one seeded 50% cross/don't-cross
  choice when passing each zebra entry. An explicit stable catalog owns one
  pooled `1.75 m` presentation per design: Lampshade Walker, Chair Carrier,
  Kettle Hat Walker, Long-Arm Walker and Helmet Lamp Hopper. Each copies the production Generic
  Avatar contract while
  directly referencing its own looping in-place `Idle` and `Walk` clips from the
  animation-only pedestrian locomotion FBX. Lampshade clips preserve a hunched
  C-curve and short asymmetric step in both states; Chair Carrier clips balance
  the inverted cafe chair with an upright, precise high-knee gait; Kettle Hat
  clips keep a low stout stance with fast short steps and a constant waddle
  whose belly and kettle roll against each other; Long-Arm clips hold a narrow
  still torso over a slow shuffle while the ground-reaching forearms swing a
  quarter cycle behind the legs and keep a residual sway through idle; Helmet
  Lamp clips take no step at all and hop on both hind feet through crouch,
  launch, a tucked airborne apex and landing. Every
  clip is baked and verified against its own archetype's footwear rather than
  a shared model, and a design may declare an animated hand-to-pavement
  clearance band that footwear grounding alone cannot express.
  **Accepted — Declared pedestrian exceptions over blanket bans:** a walker
  may leave the pavement or wear one working light, but only by declaring it.
  An airborne archetype declares an apex band; its clips receive a single
  constant pelvis lift instead of a per-frame correction, must never penetrate
  and must land at least once. Its root height is still baked into the pose at
  import: the presentation runs its Animator with `applyRootMotion = false`, so
  height left unbaked is extracted as root motion and discarded, taking the hop
  with it. `CityPedestrianPresentation` then lowers the design by one declared
  `CityPedestrianArchetype.GroundTrim` instead of pinning its sole every frame,
  which would flatten the arc. That trim exists because every other walker's
  per-frame pin also silently absorbs the height the shared Generic Avatar
  introduces when it retargets a skeleton whose proportions differ from the
  hero's; an airborne design has no such pin, so the residual is declared and
  tuned by eye. It is deliberately not derived from the clip: a sole's true
  height depends on foot rotation, and the idle and hop clips answer a single
  world-space offset by different amounts, so no measured constant grounds both
  exactly. A lamp-bearing archetype declares exactly one shadowless Spot bounded
  to `7.5 m` and `3.6` intensity, parented to the animated head bone; the
  prefab validator now checks the declared count rather than forbidding lights
  outright, so an accidental extra light still fails. The pool holds one
  instance per design, which is what caps the worn lights in the world at one.
  The director
  selects among free catalog presentations from the spawn seed and applies the
  selected archetype's movement/cadence range; when both day slots are active,
  their silhouettes are therefore distinct. A `0.15 s` locomotion blend avoids
  idle/walk pose pops while the first spawned frame starts directly in Walk.
  Its `CharacterController`
  becomes physical only after a successful spawn and presentation bind, and
  is disabled before pooling. The dedicated `CityPedestrian` layer collides
  with the player but not other pedestrians; camera collision and interaction
  queries ignore it. Stable slot order still owns head-on yielding, but
  yielding is the last answer rather than the only one. A `1 m` sidewalk minus
  a `0.35 m` agent leaves `±0.15 m` of lateral room, and two walkers would need
  `0.70 m` of separation to pass, so walking around each other across the lane
  is geometrically impossible and avoidance works along it instead: a walker
  leans away from an obstruction by up to that `0.15 m` shoulder-shift and
  re-centres itself once clear; one travelling the same way drops to the pace
  of whoever is in front and queues rather than stopping and restarting; and a
  walker that wants to move but does not — pinned against a prop or nose to
  nose with another walker, which are indistinguishable from the actor's side —
  turns back after `1.5 s`. Turning back is the only resolution the pavement
  affords, and it is self-clearing: the node behind offers its other branches
  because ordinary continuation already refuses to backtrack. Walkers retain no
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
  River promenades count as supporting travel surfaces, and declared bridge
  edges are support-only rather than generic fence sources: the river builder
  alone owns their parapets, trims them before the bank-road pads and preserves
  the four authored stair openings.
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
  For five seconds no ordinary menu choice or gameplay input path exists; the
  localized PS1-style Wake Up/Quit menu then appears without changing the
  silent, flickering `05:59` display or leaving the clock shot. Wake Up alone
  switches the clock to solid `06:00`, starts the session clock and mechanical
  ring, and hides the menu.
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
- **Accepted — One-shot Home F9 entry to the City debug map:**
  `HomeInteriorRoot` always installs `HomeDebugCityMapShortcut`, including for
  the locked opening `ClockHold`. An accepted F9 disables the current motor,
  directly requests `City`, starts the session clock from `06:00` if it is
  still frozen, prepares `CityReturnKind.PlayerHome` and sets
  `DebugCityMapOnArrivalRequested`; a rejected or duplicate transition does
  not mutate that handoff. `CityGameRoot` waits until the transition guard is
  clear, enables `CityMapController` test teleport and then uses a
  success-driven retry window bounded to `2 s` of realtime. It accepts an
  already-open map immediately and otherwise retries `Open` only after both
  the scene transition and the previous scene's `BarMinigameModalLock` have
  released. Success clears the request exactly once; timeout also consumes the
  one-shot and records the final lock, transition and attempt state instead of
  leaking the request into a later City load. The debug branch preserves the
  fresh seed, cash, needs and starter inventory and does not alter the ordinary
  Wake/Quit or Home -> Stairwell -> City path.
- **Accepted — Persistent transition context:** Static subsystem-reset session
  state carries the seed, active bar context, explicit
  bar/home/supermarket city return kind, the next stairwell arrival side and
  the consumed Home arrival kind, one-shot debug-map arrival request and
  current game time/day index
  between Single-mode scene loads. `BeginNewGame` restores all of those values
  together with route, visits, wallet, drinking and balance state.
- **Accepted — Wake-started scaled session clock:** `GameTimeState` resets to
  frozen `05:59`. A successful startup Wake or accepted Home F9 debug skip
  atomically moves it to `06:00` and starts it; later bed interactions do not
  reset or pause it.
  `GameTimeRuntime` persists across Single-mode loads and advances through
  `Time.deltaTime` at `1.0` game minute per real second, making one full `24 h`
  day exactly `1440` real seconds (`24` minutes). Midnight increments a
  session day index. `GameTimeState.Advance` also returns the actually elapsed
  game minutes so `GameSessionState` advances clock and needs from one delta;
  any owner that sets `timeScale` to zero naturally freezes both.
- **Accepted — Bounded structured diagnostics:** Runtime support logging uses
  one fail-safe UTF-8 NDJSON stream with a versioned envelope, monotonic
  sequence, session/scene/seed context and explicit transition
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
  decorative unstaffed checkout. The sprite cashier is removed with the rest
  of the sprite NPCs; a dedicated 3D cashier arrives in a later pass, and the
  register does not process
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
  ordered list of stable `BarId` values, edited only by hand on the map.
  The former bar-visited mechanic is removed entirely: no visit tracking,
  no map highlight or counter, and entering a bar changes nothing about the
  route. The route resets when the city seed changes.
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
  landmark markers do not change bar
  selection or player pathfinding.
- **Accepted — Shared-lock gameplay pause:** City, BarInterior,
  SupermarketInterior, HomeInterior and StairwellInterior each attach one
  runtime `PauseMenuController` to their existing UI root. Escape or gamepad
  Start can open it only when no other
  `BarMinigameModalLock` or scene transition owns gameplay; pause therefore
  never steals Escape from maps, refrigerator inspection or modal shops, remains
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
  points that compose the hero below frame center. RMB mouse motion and the
  gamepad right stick drive independent yaw and pitch in ordinary City, Bar
  and Supermarket follow; pitch is clamped to `-20°..55°`. Orbit yaw, pitch and
  target focus use deliberately weighty `0.20 s`, `0.18 s` and `0.18 s`
  damping; focus stays within `0.45 m` and snaps on jumps beyond `1.75 m`.
  Deterministic low-frequency idle drift and speed-driven bob affect only
  focus, pitch and roll; requested yaw and FOV remain stable. Collision
  shortens the arm immediately, restores it with `0.32 s` damping and fades
  cinematic motion during fullscreen modal ownership. Balance checks disable
  orbit input but deliberately retain cinematic motion so intoxication lean
  and fall reactions remain visible. Fixed Home/Stairwell and contextual
  camera owners remain non-orbiting; the bus keeps its separate seated bounds.
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
  load as low-poly 3D prefabs; the stairwell-cat bitmaps still load from
  `Resources` and are sliced or drawn at runtime.
- **Accepted — Shared rendering state:** Primitive colors use
  `MaterialPropertyBlock`; every ordinary runtime primitive explicitly shares
  the serialized Resources `RuntimePrimitiveLit` URP material so Player builds
  do not depend on Editor-only primitive defaults. Emissive and atmosphere
  effects reuse their cached specialized resources, with no per-instance
  materials or runtime `Shader.Find`.
- **Accepted — Geometry-locked district facade albedos:** City building masses
  wear one of eight district wall albedos, two per buildable district, through
  `MaterialPropertyBlock`s on the same shared `RuntimePrimitiveLit`. They are
  not tiled by metres. `CityFacadeAppearance` derives `_BaseMap_ST` from
  `CityFacadeGrid`, the single source of the bay and floor pitch that the
  window builders also read, so one authored cell covers exactly one pane bay
  and one `2.35 m` storey. Horizontal phase follows the pane-count parity; the
  vertical phase is independent of building height and takes one of four
  values, and it must include the `0.08 m` mass base or every window band
  slides up the wall. A stable per-lot whole-cell bay and floor rotation adds
  sixteen presentations per sheet without disturbing that alignment. Sheets are
  authored at `1024` rather than the project's `1254` so Unity's import to
  `512` is an exact 2:1 downsample; band and mullion edges are the whole point
  of the texture and a 2.449:1 resample softens them. Baked floors continuing
  above the topmost geometric window row is intended, not a bug: it fills the
  blank cap tall lots used to show.
- **Accepted — Linear-space facade compensation:** Facade albedos hold a mean
  linear luminance of `0.35` and the night facade tint is brightened by
  `1 / 0.62` before it reaches `_BaseColor`, which preserves the brightness the
  flat colour had and never clamps the brightest lot the generator can make, a
  bar at `0.616`. This deliberately does **not** reuse
  `StairwellSurfaceAppearance`'s `compensation = 1 / meanLinearLuminance`: that
  rule assumes the tint and texture multiply in gamma space, while URP converts
  both to linear first. Applied here it would have called for a mean of `0.64`
  and made every facade in the city almost twice as bright as before — a pale,
  chalky wall rather than a grimy one. The bound and the preserved brightness
  are both swept from the live colour ranges by
  `CityFacadeAppearanceTests`, so widening a district palette fails there.
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
  Prompts, segmented intoxication HUD, overhead balance gauge and map use a
  logical `640x360` canvas with a shared palette,
  stepped frames and point-filtered accents. Menus, modal inspectors and the
  map omit persistent key-binding guides and control-hint footers.
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
  exterior. The neighbour-wall yard Spot is the explicit City-local exception:
  it lives with the open-area composition, outside `Night.Root`, and remains
  enabled at its authored intensity at every sample. Bar, Supermarket and
  Stairwell visual profiles remain unchanged.
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
- **Accepted — Deterministic slot weather, presentation-only:**
  `GameWeatherRules` is a pure function of the city seed and the absolute
  session minutes: `90`-game-minute slots hash into Clear (`55%`), LightRain
  (`27%`), HeavyRain (`12%`) or Thunderstorm (`6%`), and the continuous
  intensity smoothsteps between the slot targets (`0` / `0.45` / `1.0` /
  `1.0`) over the first `5` game minutes of a slot. Because the sample is
  derived, no new session state is persisted and City and the Home balcony
  can never disagree. Presentation is a seeded player-following
  `CityRainField` (at most `420` stretched streak particles over a `26 m`
  box on the shared atmosphere material, no collision) plus a deterministic
  crossfaded noise loop (`CityRainSound`, `Ambience/Beds`), driven per frame
  by `CityWeatherController` in City and by `HomeBalconyExteriorAtmosphere`
  on the balcony; balcony audio and flashes are gated to the active Balcony
  shot. While `CityBusRideController.IsPassengerAboard`, the emitter switches
  to a donut with a `10 m` rain-free core so streaks never spawn inside the
  cabin. The rain deliberately does not touch `GameTimeDayNightRules`,
  `RuntimeSceneSetup.ApplyCityExteriorLighting`, fog, grade or far clip: those
  contracts are asserted exactly by the existing City/Home PlayMode suites,
  and coupling weather into them is a separate future decision (daylight
  dimming and wet surfaces remain open gaps).
- **Accepted — Deterministic lightning outside the pooled light budget:**
  Lightning shares the pure schedule: each `12`-game-minute window hashes
  into at most one strike (`70%`) whose start offset, azimuth and distance
  are gated to fully developed Thunderstorm slots, so every scene evaluates
  the identical storm without extra state. The flash is one transient
  shadowless directional `Light` (`CityLightningFlashLight`, peak `1.9`
  scaled down to `45%` at the far distance band) that stays disabled outside
  its `0.5`-game-minute flicker envelope and lives outside `Night.Root`.
  Because it is directional and transient, it is counted separately from the
  bounded `18` local realtime lights and does not change the pooled
  atmosphere/bus assertions. Thunder is a deterministic synthesized one-shot
  (`CityThunderSound`, `Ambience/Details`, two rotating voices on child
  objects because a low-pass filter processes its whole GameObject) played
  `0.6-3 s` after the flash with distance-scaled volume and cutoff. A frozen
  clock — pre-wake or `timeScale = 0` — suppresses the flash instead of
  holding it lit, and strike IDs deduplicate thunder across frames.
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
  more than `12` shadowless realtime lights. The sole active bus may add `4`
  runtime-owned shadowless Spots, the single pooled helmet-lamp pedestrian may
  add `1`, and the fixed always-on yard Spot adds `1`, for a bounded worst case
  of `18` local realtime lights. The scene Directional and transient lightning
  Directional are separate from this local-light count.
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
  the mono `22050 Hz` UI, footstep, door latch and sustained hinge creak
  clips in memory.
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
- **Accepted — Both spade acts are a choice, not a swing:** Digging and
  filling ask the hero to pick one of six squares and press `E`; there is no
  timing bar in either. There was one, with five kinds of ground behind it
  (turf, loam, clay, stone, root) each with its own bite window, and it is
  gone at the user's direction along with the whole soil system: `18` timed
  swings is the same shot demanded eighteen times, and a kind of ground the
  hero cannot see and cannot answer differently is not detail. What carries
  the act instead is the lattice rule — no segment may go deeper than its
  shallowest neighbour — which is a decision about where to work and is
  visible in the hole, because the segment under the spade is outlined down
  there by `CityCemeterySegmentFrameWorldBuilder` rather than on a panel. The
  HUD for both acts is one line naming two keys. The one timed swing left in
  the job is the three blows that set the stone, and `CemeterySwingProfile`
  now states its shape where it is used instead of in a table of soils.
- **Accepted — The gravedigging animates the tool, not the hero:** Every act
  of the gravedigger's job runs as a modal session that drives one procedural
  spade (`CityGravediggerShovelWorldBuilder` +
  `CemeteryShovelAnimator`) and never touches the player rig. The camera takes
  the hero's own eye line and `PlayerPresentationVisibility` leases his body
  out of sight for the duration, so there is no visible hero to disagree with
  the tool and no authored clip to keep in step with it. This is an explicit
  user decision and a deliberate deviation from
  `ai/contextual-animation-standard.md`, which governs interactions that take
  ownership of the hero presentation; this one takes ownership of the camera
  and hides the presentation instead. The framing is load bearing, not art
  direction: any shot that put the hero back in frame would show a man
  standing perfectly still beside a spade digging by itself. The upgrade path
  is already in the project if it is ever wanted —
  `Player3DAssetRegistry.Anchors.RightGrip` with
  `HomeTeethBrushingInteraction.EnsureProps` for the prop and
  `HomeTeethBrushingArmPose` for a procedural arm, no Blender work required.
- **Accepted — Minigames return, in the city and by a new design:** The
  gravedigging acts are the first minigames since the sprite-era cut below.
  That cut removed four *bar* minigames along with the sprite art they were
  built on and left `BarMinigameModalLock` standing "as the generic gameplay
  modal lock"; `ai/project-overview.md` records that any future activity
  starts from a new design. These are that new design, they live in the city
  rather than the bar, and they reuse the surviving modal lock rather than
  reviving `BarMinigameCatalog`/`IBarMinigame`.
- **Accepted — Sprite era ends, minigames cut:** All four bar minigames
  (cocktails, beer pong, Split the G, Tinctures in a Row), their domains,
  presentations, atlases and localization keys are removed from the project
  entirely, together with the sprite bar crowd and the sprite supermarket
  cashier. `BarActivityKind` and its pure ordinal resolver survive purely as
  interior flavour: the assigned kind still selects layout dressing (beer-pong
  table, stage) but constructs no controller and no `BarActivityStation`.
  `BarMinigameModalLock` survives as the generic gameplay modal lock used by
  maps, shops and inspectors.
- **Accepted — 3D bar patrons from the city pool:** `BarPatronWorldBuilder`
  instantiates the pooled city pedestrian prefabs on the deterministic layout
  NPC anchors, applies each anchor's palette variant and seats `SeatedPatron`
  roles through the shared `CityPedestrianSeatedRide` contract at `0.46 m`;
  standing roles idle in place. The `Bartender` anchor is deliberately left
  empty until a dedicated 3D bartender pass, mirroring the empty supermarket
  checkout.
- **Accepted — The Watcher Cashier:** The supermarket checkout is staffed
  by one bespoke animation-free 3D clerk (`watcher_cashier_v1`) built by
  `tools/build-supermarket-cashier-3d-model.py` on the exact shared 31-bone
  Player Avatar. His signature long neck is five rigid segments on exported
  `PIVOT_Neck.01..05` empties: the runtime re-parents the segments under the
  pivots and folds the pivots into a chain off the neck bone (the wheelchair
  mechanism pattern), so the shared Avatar and every 31-bone validator stay
  untouched while the chain stretches to `2.4x`, bends serpentine on
  per-segment shares and carries the deliberately undersized head after its
  tip. The prefab lives outside Resources behind
  `SupermarketCashierProvider` (the yard-wheelchair provider pattern), is
  validated passive (no Collider/Rigidbody/AudioSource/Light/Camera) and
  gets a `PlayerAttentionMagnet` at `2.0 m` so the hero and the clerk can
  catch each other staring.
- **Accepted — Pursuit-curve neck solve:** The chain is not rotated by
  per-joint shares; each frame the five pivots are laid explicitly along a
  cubic staple from the neck base to the head target — the hero's face
  plus a `0.85 m` standoff and `0.25 m` lift, clamped to the hall box.
  The reach is effectively unbounded (`18 m` cap), so the face follows
  the hero to every corner of the `16 x 11` hall. When the curve touches
  any margin-expanded (`0.22 m`) shelf or fixture AABB, both cubic
  controls lift to a shared clearance height (`tallest obstruction +
  0.5 m`) at `t = 0.2/0.8`, making the chain climb out of the counter
  fast, travel above the aisles and descend only at the hero; the
  resulting curve is re-sampled against every obstacle and the clearance
  raised until nothing clips (up to four attempts, ceiling-clamped).
  Each pivot turns its rest up-axis onto the local
  curve direction and scales its rigid segment to span the gap; the head is
  pinned to the curve tip by its authored neck-attachment point
  (`InverseTransformPoint` captured at bind), so head rotation happens
  around that joint — never around the distant canonical head bone — and
  the head cannot tear off the chain.
- **Accepted — Cashier surveillance logic is pure:**
  `SupermarketCashierSurveillanceState` owns the numbers — a pursuit
  weight that saturates whenever the hero is present, asymmetric creep
  `0.9/s` vs guilty retract `2.4/s`, a caught-looking startle entered at
  `dot > cos 22°` held `0.15 s` and released at `dot < cos 30°` held
  `0.8 s` that caps extension at `0.30`, freezes the idle scan, pinches the
  pupils and suppresses blinking `1.2 s` past release —
  and `SupermarketCashierBlinkState` owns the rare `6.5 s` blink cycle that
  restarts from zero after every suppression, so the stare after being
  watched is always a full unbroken cycle. Both are Unity-free and covered
  by EditMode tests; the presentation only renders their outputs, restoring
  the imported rest pose every frame like the bus driver.
- **Accepted — Cashier talk stub:** A separate trigger object in front of
  the register carries the booth/dumpster placeholder contract
  (`interaction.talk_cashier` / `supermarket.cashier.placeholder`); the
  passive prefab itself stays collider-free.
- **Accepted — Bathroom scene skeleton and standard exceptions:** The
  full-body clip set is closed (adding clips regenerates the production
  hero FBX), so the three bathroom scenes run on one shared skeleton
  (`HomeBathroomSceneInteraction`) that keeps the runtime contract of
  `ai/contextual-animation-standard.md` — constrained visible walk-in via
  `PlayerMotor.MoveTowardsInteractionPose`, one neutral settle frame,
  modal `BarMinigameModalLock` capture, Bézier camera from the pinned
  bathroom shot with the shared smoking drift, debounced stop input,
  commit only on completion, idempotent restore + `ReapplyActiveShot` —
  while replacing authored clips with three RECORDED EXCEPTIONS:
  (a) the shower hides the standing Idle hero behind the drawn curtain
  group (scale-x animation of the folded panels); (b) the toilet is a
  privacy cut — the camera retreats to the ajar-door frame and the hero
  stays off-frame in Idle while the cistern and flush play; (c) teeth
  brushing poses a procedural additive CCD right arm atop Idle
  (`HomeTeethBrushingArmPose`, capture-solve-slerp each LateUpdate at
  order 300 — the bus-driver/cashier idiom) driving a RightGrip
  toothbrush prop oscillating at the Mouth anchor with a head
  counter-yaw.
- **Accepted — Mirror-camera brushing scene:** The close-up shoots from
  7 cm in front of the mirror plane back into the hero's face (FOV 36) —
  the PS1 "reflection" without RenderTextures; the pinned bathroom shot
  is never edited, scene poses are transient. Foam blobs ride the Mouth
  anchor; the rinse dips the camera look-at to the basin over two Pour
  beats. Stress relief is gated once per `GameDayIndex`
  (`TryCommitTeethBrushingRelief`); toilet and shower commit ungated
  (`CommitBathroomStressRelief`) — always on completion, never on
  cancel.
- **Accepted — Shower stall rebuild:** The stall keeps its footprint,
  tray collider and pinned names while gaining an L-rail, a
  four-fold animatable curtain group (gathered scale 0.55 <-> drawn 1.0)
  plus a static side run, a wall mixer with red/blue cross handles, a
  four-segment sagging hose, a tilted bell head with a dark nozzle
  plate, tray rims, a drain and a soap shelf. The water is a sixth
  owned `HomeSoundscape` source with a seamless loop-phase hiss
  (`SetShowerWaterAmount`, volume + low-pass crossfade) plus code-built
  stream/steam particles on the shared atmosphere material — no lights,
  no colliders.
- **Accepted — Clock-driven apartment mood:** `HomeDayNightController`
  now modulates the whole indoor mood, not just the window. The window
  keeps its exact day (`8.25`, warm) and night (`5.25`, blue) poles —
  test-pinned — and passes through a dusk amber
  (`1.0/0.56/0.30`, blend `0.65`) that peaks mid-transition and vanishes
  at both endpoints. The main lamp swings `2.30 (day) -> 4.10 (night)`
  and deepens its orange, the entry spot lifts `8.0 -> 9.4` (never
  below the test floor of `8`), and `RenderSettings.ambientLight` plus
  the directional fill lerp from a warm bright day to a cold dark night
  (`0.44 -> 0.22`, blue-grey). The Balcony shot is respected: the
  indoor ambient/sun mood is skipped while the balcony borrows City
  lighting and reasserts itself the moment the shot returns inside.
  Light count is unchanged — the five-light budget stands.
- **Accepted — Corner CCTV heads:** Four camera units hang in the room
  corners, positions resolved purely from the plan
  (`RoomSize/WallThickness/RoomHeight`, inset `0.55 m`, drop `0.42 m`).
  Each head snaps onto the hero at initialization and then servos at
  `240°/s` (`Quaternion.RotateTowards`), so the lens is always trained on
  him. The units are dressing under the supermarket's one-directional
  light budget: primitive boxes, a fake-emissive recording LED with
  shadows off, no Collider and no Light.
- **Accepted — Supermarket fluorescent light budget:** The hall moves off
  the single flat directional onto an explicit six-practical budget
  (`SupermarketInteriorAtmosphere`): one shadowless cold point under each
  of the four fluorescent rows (`1.05` intensity, `7.6 m` range), one
  warm accent over the checkout — deliberately the only warmth in the
  hall, pooled on the Watcher Cashier — and one cool spill by the cold
  shelf. The directional key steps down `0.48 -> 0.36` and stays the
  scene's only shadow caster. Row two flickers on a deterministic
  stepped pattern (`0.11 s` steps, dips to `0.30`), dimming both its
  light and its fake-emissive tube tint through a MaterialPropertyBlock.
  The budget is test-enforced: six lights, none directional, all
  shadowless.
- **Accepted — Debug window without a launcher:** The City and BarInterior
  roots still install the F9 debug window, but it owns only deliberate test
  controls: the Left/Right arrow keys or clickable buttons change the real
  session intoxication by `-20/+20` and clamp at `0/100` while preserving
  last-drink and consumed-drink context, and City exposes the test-teleport
  toggle. Opening it still closes a conflicting city map or drink service
  before capturing the modal state.
- **Accepted — Session-only drinking persistence:** Intoxication, last
  alcoholic drink, total consumed-drink count and explicit alcohol-value
  stress relief are committed through `GameSessionState` by the physical bar
  counter service and inventory consumption; they survive scene loads and
  reset when the application subsystem restarts. Water relieves no stress.
  Aggregate debug state changes do not simulate a drink. The remaining
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
  the last alcoholic drink. `None` and `Moonshine` (a stable legacy ID kept
  for persisted state) are not sold. Purchased drinks are consumed at the counter instead of being added to
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

- **Accepted — One drive for every body of water:** The river and the lake are
  two materials of one shader, and water carries no per-renderer variation at
  all — no property blocks anywhere — so night factor and rain intensity have
  to be written on the material itself. With one body that could live in
  `CityRiverResources`; with two it cannot, because the registries that push
  those values (`CityNightGlowRegistry`, `CityWeatherController`) have no
  business knowing how many bodies exist. `CityWaterResources` owns the drive
  and nothing else: each body registers its own material and is brought up to
  the last pushed values on the spot, which is what lets a lake built halfway
  through a rain slot arrive already wet. They are not merged into one material
  because the difference is structural, not cosmetic — zero flow changes what
  the vertex stage computes, and the lake's ripple sheet is isotropic where the
  river's is deliberately smeared along its flow.
- **Accepted — The lake's edge is authored, and its bank is off the nav graph:**
  The `0.40 m` shore-to-water drop exceeds `CityRoadGroundBoundaryPlanner.MaximumSafeStep`,
  so `CityTerrainSafetyWorldBuilder` used to ring the whole lake with a generic
  `1.05 m` guard rail. Once `CityLakeWorldBuilder` authors a walkable bank down
  to an inset waterline, that rail stands on ground which visibly continues
  past it — the invisible perimeter this project does not build, and the exact
  failure the park fix removed. The skip that already existed for `RiverWater`
  is therefore widened to authored water edges, and the precinct owes a visible
  barrier in its place: a continuous timber revetment standing clear of the
  safe step, cut only where the pier deck bridges it or the chained slipway
  closes it, under a `ValidateOrThrow` contract that checks the whole perimeter
  is boarded or bridged to within `0.05 m`.
- **Corrected — the lake bank is in the walkable mask, and has to be:** this
  entry previously recorded the opposite as an accepted asymmetry — that the
  bank was physically walkable but deliberately absent from the nav graph
  because it sits on `Water` cells, so pedestrians would never stray onto it.
  That reasoning weighed only pedestrians. `PlayerMotor` clamps against the
  same mask, so the effect was a `52 x 52 m` invisible box sealing the player
  out of the entire precinct. `CityLakePlanner.AppendWalkableFootprints` now
  contributes the bank ring and the pier deck to the mask — never the pond —
  and a shared `TryCreateSetup` guarantees the ground the world builder draws
  and the ground the mask admits are derived from one basin. Pedestrians are
  unaffected in practice: they consume the mask only as a clamp, not as a
  source of destinations.
- **Accepted — the park boards are played by a pure engine the presentation
  never argues with:** the two games live under `Assets/Scripts/Runtime/Games/`
  as plain C# — position, legal moves, apply, search — and the scene layer
  only ever asks for the current placement and the description of one applied
  move. Three consequences were worth the split. Chess correctness is provable
  by perft rather than by playing it, which is the only honest way to know a
  move generator is right. The presentation can be wrong for at most one move:
  every carry ends with the board re-read from the rules, so a mis-animated
  chain repairs itself instead of compounding into a corrupt game. And the
  engine's strength is one number in one struct rather than a property of the
  code, which is what let the opponent be tuned down to a plausible old man —
  a slack window at the root, a blunder roll, and an explicit exception that
  never throws away a forced mate.
- **Accepted — the hero is dark at both boards because the set already was:**
  nothing chooses sides. The free plank at each table is the one the drawn
  `CityChessSetPlan` filled with dark men, so the man opposite is White at his
  own board and therefore opens. A test asserts the live opening position
  equals the drawn placement piece for piece, which keeps the two from ever
  drifting — the moment somebody sits down, the set they were looking at is
  the position they are playing.
- **Accepted — a first-person camera hides the head by rig rule, never by
  mesh:** the hero's head is twenty-two separate meshes on this model — skull,
  neck, hair cap and fourteen tufts, ears, nose, stubble, under-eye shadows,
  and then eyes, pupils, brows and a mouth on their own `face.*` bones. The
  first seated view hid the two anatomical parts named Head and Neck and left
  the player looking at the inside of his own hair, which is the exact class of
  bug that a list of mesh names reintroduces every time the model grows a part.
  `Player3DHeadVisibility` therefore classifies by bone (`head`, `neck`,
  `face.*`), so a part added later is covered by where it is weighted rather
  than by somebody remembering to add it. Nothing below the collar is ever
  hidden: the body is what tells the player he is sitting at the board, and the
  camera is moved out of the skull instead.
