# Work log

Entries are reverse chronological. Record outcomes and verification, not a transcript.

## 2026-08-12 — Two pedestrian archetypes and bespoke locomotion

- Added the city-wide Chair Carrier (`chair_carrier_v1`): an upright low-poly
  passer-by carrying an inverted cafe chair whose legs cage the head. The
  existing Lampshade Walker now keeps a pronounced C-curve, bent knees and
  withdrawn neck in both idle and its short asymmetric walk, so stopping no
  longer snaps it back to the hero's upright pose.
- Added one animation-only Generic locomotion library with dedicated
  `LampshadeIdle`, `LampshadeWalk`, `ChairCarrierIdle` and `ChairCarrierWalk`
  loops. Both model FBXs remain animation-free, copy the production Player
  Avatar and use the shared `Player3DLit` material; their four palette variants
  remain property-block driven.
- Replaced the single hard-coded runtime presentation with an explicit ordered
  archetype catalog and one pooled instance per design. The spawn seed chooses
  among free presentations, applies archetype-specific movement and cadence,
  and preserves the existing two-walker daytime / one-walker night caps and the
  shared City/Home Balcony lifecycle. Added a `0.15 s` idle/walk blend and
  geometry-based grounding for both boot naming conventions.

Verification:

- The deterministic Blender 5 generator completed with `CITY PEDESTRIAN ART
  BUILD OK`: Lampshade `38` meshes / `1160` triangles, Chair Carrier `35` /
  `1032`, four 31-bone loops, zero root translation, zero loop error and zero
  per-frame sole gap or penetration.
- Unity rebuilt and validated both production prefabs successfully. Focused
  EditMode coverage passed `3/3` in `0.41 s`: two parameterized asset/rig/clip/
  grounding cases plus the catalog spawn, distinct-design pool and speed-range
  case. Complete suites, player build and packaged smoke were intentionally
  omitted in fast mode.

## 2026-08-12 — Route 01 passenger MVP

- Added standard localized E/Enter/gamepad/pointer boarding through both fully
  open passenger doors and exit from fixed window seat `07` on the side
  opposite the driver. The bus
  now holds its service dwell during each visible transfer, admits one
  owner-scoped passenger and refuses recycling or actor release until passenger
  cleanup has completed.
- Added deterministic front/rear exterior entry/exit docks, nearest-door
  selection, a retained door-specific live waypoint and seat `07` binding.
  The exterior clearance now keeps the waiting player capsule outside the bus
  obstacle corridor, preventing a self-created yield before the service dwell;
  each entry/exit root now derives its height from the deterministic physical
  street-surface plan, choosing the raised sidewalk or flat road apron at that
  exact door position. A curb-height difference within the real
  `CharacterController.stepOffset`
  remains a visible, reachable positioned approach instead of hiding the
  prompt.
  Boarding uses `BusBoardEnter`, travel holds `BusRideLoop`, and the exit prompt
  becomes available only after the service ordinal advances to the next or any
  later stop before `BusAlightExit` returns the hero through the selected door
  to a validated grounded roadside pose.
- Extended the shared positioned-interaction controller with a moving pelvis
  target plus an independently requested exit pose. The production 3D hero
  remains visible across the transfer and follows the sprung seat instead of
  using a hidden teleport or renderer fade.
- Added a seat-following seated ride camera whose safe aisle-side default looks
  through the nearest window instead of inward/down. Its horizon stays level in
  world space while suspension pitches/rolls, and direction-vector blending
  avoids transient roll during boarding/alighting. RMB mouse look and the
  gamepad right stick now rotate independent bounded yaw/pitch in place, reuse the
  ordinary modal orbit-input gate and preserve a continuous blend back to the
  chase pose. The gameplay root remains in its original
  hierarchy and late-synchronizes to the actor-local seat after bus movement,
  avoiding forbidden parent/sibling mutations when the bus slot or scene is
  deactivated. Normal exit, cancellation, scene teardown and forced bus cleanup
  restore the player motor, collider, contact shadow and camera while releasing
  both service and passenger ownership.
- Kept the MVP City-only and deliberately limited to one fixed seat. Fare and
  payment, destination selection, NPC passengers, passenger persistence and a
  live map marker remain deferred.

Verification:

- The deterministic Blender player generator/export validator completed with
  `26` Actions, including the new three-second board, looping two-second ride
  and three-second alight clips on the production Generic rig.
- Unity imported the regenerated animation FBX, rebuilt the production Player3D
  prefab with all three bus clips and compiled Runtime, Editor, EditMode and
  PlayMode assemblies without errors.
- Focused PlayMode regression
  `Passenger_BoardsRidesAndExitsAtLaterStop` was extended to exercise ordinary
  `PlayerInteractor` discovery at both exterior doors, nearest rear-door
  selection, same-stop rejection, attached movement without self-yield or
  recycling, later-stop exit through the retained door and exact player/camera
  restoration. The updated focused selection passed `1/1` in `0.56 s`, including
  the real localized clickable `InteractionPromptView` at both doors. Focused
  actor-ownership and moving-pelvis regressions remain in place; complete
  suites, a player build and a packaged smoke check remain intentionally
  omitted in fast mode.
- Production-city regression
  `ProductionCityRoute_AllStopsExposeBothDoorPrompts` passed `1/1` in `1.05 s`.
  It covers the default seed's five stops and both doors, the real localized
  clickable prompt from road height, and a passenger waiting before arrival
  while the real `CityBusDirector` resolves obstacles and still reaches its
  open-door dwell.
- Focused physical-ground regression
  `ProductionCityDoorDocks_MatchPhysicalSurfaceHeight` passed `1/1` in `1.50 s`.
  It compared all five stops, both doors and both entry/exit poses against the
  real generated colliders: nine door points use sidewalk top `0.14`, while
  Home/front correctly uses apron top `0.08` and grounded root `Y=0.12`.
  The strengthened production prompt regression then passed `1/1` in `1.08 s`,
  including a real click at that Home/front dock and the next-frame transition
  from `Positioning` to `Entering`.
- The focused ride regression was extended with stable actor-local following
  while Player retains its original parent, followed by bus-slot deactivation
  during `Riding`. It passed `1/1` in `0.78 s`, restoring passenger/service
  ownership, motor, collider, shadow and camera without either Unity hierarchy
  error.
- The same regression now also requires a level default view through the
  nearest window and feeds real queued RMB mouse delta plus gamepad right-stick
  input through the passenger camera. Runtime and PlayMode test projects
  compile with `0` warnings and `0` errors.
- Corrected the fixed seat from same-side `Seat_01` to opposite-driver
  `Seat_07`, and strengthened the same regression to prove the side contract,
  a level horizon on every boarding-blend frame and under forced suspension
  pitch/roll, plus unmixed direction-correct X/Y input for both mouse and right
  stick. The focused selection passed `1/1` in `0.97 s`.

## 2026-08-11 — Route 01 production driver

- Added the separate passive `CityBusDriver3D`: a normal low-poly head with
  long horizontal eyes, the shared `Player3DLit` material and the exact 31-bone
  rig used as a procedural presentation target.
- Added seated IK that keeps both hands on the rotating steering-wheel grips.
  The deterministic door timeline moves the right hand to the dashboard button
  for each open/close command, drives its real `12 mm` travel while the left
  hand stays planted, and now holds the real head turn for the complete open
  phase before returning during closing.
- Added deterministic blinking and proximity focus on the main player's real
  head at the outside of the front entrance. The connected neck/head segment
  stretches up to `0.10 m` with a `1.35x` limit and restores its exact local
  scale when focus ends or the bus returns to its pool.
- Preserved the fixed `10 s` stop dwell and `0.70 s` opening/closing transitions.
  Wheel, button, hands, head/look and timeline state now reset with the bus pool.

Verification:

- The deterministic Blender driver generator/export validator completed, and
  focused `CityBusDriverAssetContractTests` verification passed `1/1`.
- The rebuilt production bus prefab passed `CityBusAssetSetup.RunBatch`; focused
  `DriverPresentation_TracksWheelPressesButtonAndLooksAtDoor` verification then
  passed `1/1`, covering wheel/grip contact, both button presses, the actual
  face-bone direction throughout the open hold, player focus/stretch, blinking
  and pool reset. Complete Unity suites, a player build and a packaged smoke
  check were intentionally omitted in fast mode.

## 2026-08-11 — Bus headlights and soft cabin light

- Added two warm, shadowless runtime headlight Spots that follow the sprung bus
  body and illuminate the road ahead, plus two short wide downward Spots for a
  soft readable cabin wash. The production art prefab remains `Light`-free.
- Scaled all four sources with the existing shared `NightFactor`, preserving
  the current dawn/dusk blend, and disabled them completely during daytime,
  presentation disable and pool reset. Existing head/tail/cabin emission and
  brake-light behavior remain unchanged.
- Kept the city-atmosphere pool capped at 12 shadowless lights; the sole active
  bus may add only its four owned Spots, bounding the exterior total at 16.

Verification:

- Focused Unity EditMode
  `PresentationNightLights_AreSprungScaledAndPoolSafe` passed `1/1`, covering
  light count, sprung hierarchy, direction, `0 / 0.5 / 1` night scaling and
  exact pooled shutdown. Full EditMode/PlayMode suites, player build and smoke
  were intentionally omitted in fast mode.
- Scoped `git diff --check` passed. The full dirty-worktree check still reports
  the pre-existing Unity serializer whitespace churn in prefab, material and
  FBX meta files, which this change preserves.

## 2026-08-11 — Bus suspension corrected to runtime axes

- Fixed the production FBX basis turning the intended vertical suspension
  heave into a visible forward/backward slide. The sprung pivot now captures
  its neutral pose relative to the bus presentation and applies heave along the
  runtime bus vertical, with pitch and roll composed around the runtime right
  and forward axes before the imported neutral rotation.
- Added a production-prefab regression that requires non-zero heave to project
  only onto bus height, rejects longitudinal or lateral drift, verifies the
  pitch/roll rotation basis and checks exact pooled reset. The actor transform,
  collider, wheel contacts and ten-second dwell are unchanged.

Verification:

- `dotnet build BarPromenade.EditModeTests.csproj -nologo` succeeded with zero
  errors and the existing `32` JSON-manifest `CS0649` warnings.
- The exact Unity EditMode production-prefab regression
  `SuspensionPresentation_UsesBusVerticalAndBodyAxes` passed `1/1`; scoped
  `git diff --check` passed for the bug-fix files.

## 2026-08-11 — City bus rides on cartoon suspension

- Added a presentation-only `Suspension Visual` pivot around the bus body.
  The four wheel assemblies remain grounded outside that pivot while the body,
  doors, cabin and lights receive a bounded distance-driven heave, acceleration
  and braking pitch, and steering roll. The route transform, kinematic body,
  collider, planner bounds and recycling distances remain unchanged. Door
  hinges preserve their production neutral axis and follow the sprung body
  vertical while it is pitched or rolled.
- Capped the authored ride at `0.045 m` heave, `0.8°` pitch and `1°` roll,
  eased it back to neutral at rest and restored the exact neutral hierarchy,
  articulation and procedural phase whenever the model returns to its pool.
- Replaced the seeded `3-5 s` stop range with one fixed `10 s` total dwell.
  The existing `0.70 s` door opening and closing transitions remain inside
  those ten seconds.
- Added focused regressions for a moving body over grounded wheel contacts,
  unchanged actor/collider state, exact pooled reset and the ten-second dwell
  boundary.

Verification:

- Focused Unity EditMode `CityBusRuntimeTests` passed `15/15`. Fast mode
  passed `15/15`; the one production-prefab door regression passed `1/1`
  after the runtime hierarchy change. Fast mode intentionally omitted the full
  EditMode/PlayMode suites, a player build and a packaged smoke check.
- Scoped `git diff --check` passed for the implementation, tests and
  documentation. The full dirty-worktree check still reports the pre-existing
  Unity serializer whitespace churn in modified prefab, material and FBX meta
  files, which this change deliberately preserves.

## 2026-08-11 — Bus doors fold inward from real hinges

- Rebuilt both production bus doorways as independent double-leaf assemblies.
  Each leaf now owns its panel, glass and moving trim on an outer hinge, while
  the doorway's outer posts remain fixed to the body instead of rotating as one
  wide central slab.
- Updated the runtime registry, prefab builder and presentation to bind all four
  leaves. Opposed world-space rotations use the bus vertical, fold into the
  cabin and restore the exact authored pose before pooling. The deterministic
  Blender source/FBX/manifest and Resources prefab now share generator version
  `1.1.0`, `41` meshes and `3804` triangles.
- Added a production-prefab regression that checks both doorways, vertical
  rotation, equal opposed angles, inward movement, fixed posts and exact reset.

Verification:

- The Blender generator validator completed and a dedicated fully-open review
  render showed two clear, upright doorways with both leaf pairs folded inward.
- Focused Unity EditMode
  `CityBusAssetImportTests.DoorPresentation_UsesOpposedInwardHingedLeaves`
  passed `1/1`. Fast mode intentionally omitted the full EditMode/PlayMode
  suites, a player build and a packaged smoke check.

## 2026-08-11 — Winding Route 01 reaches district places and Home

- Replaced the Central Park ring selection with a deterministic target-derived
  Route 01. The planner orders every actual district point of interest and then
  `PlayerHome`; the default city now owns five semantic stops in Industrial,
  Nightlife, Residential, Old Town and Home order. Each stop chooses a safe
  straight on the target frontage or one connected edge away, keeps its pole on
  another roadside cell and outside the target public/access bounds or Home
  footprint, and carries explicit target kind, ID and cell metadata.
- Connected those target straights through one accepted closed graph. Retained
  links include ordinary straights, proven `6 m` left turns and a selected-apron
  two-edge safe-right macro: a long S-merge over the full incoming Street, a
  `4.5 m` quarter-turn through the clear core and a symmetric S-return over the
  outgoing Street. The macro marks both physical edges occupied so it cannot
  bypass a stop edge. Ordinary unselected `3 m` rights remain rejected.
- Expanded Road v2.1 eligibility to safe perpendicular two-way corners as well
  as three- and four-way nodes. Signal intersections remain eligible because
  excluding them disconnects the production target graph; every retained
  maneuver now proves its inflated body against both actual signal poles at a
  conservative `0.30 m` radius, rejecting collisions as
  `StaticFixtureOverlap`. The physical apron remains `4.5 m` long.
- Gave every ordered route occurrence unique link/node IDs even when a physical
  section repeats. Nightlife's Last Route Island now has a working Route 01 pole
  nearby but outside the POI, while its abandoned island composition remains
  distinct. The City map consumes the five default stop descriptors without a
  live bus marker.
- Reused the stop visual builder in Home: the bounded exterior selects the
  `PlayerHome` target and reconstructs its blue `01` pole in local space without
  colliders. Home still creates no bus actor or director. Added the Home stop
  localization and focused planner/Home composition coverage.

Verification:

- The focused Unity EditMode `CityBusPlannerTests` fixture passed `6/6`, covering
  deterministic non-empty generation, the closed winding loop, accepted
  straight/left/wide-right clearance including real signal fixtures, semantic
  POI/Home stops and stop-edge ownership.
- The focused Home exterior integration regression passed `1/1`, proving the
  nearby `PlayerHome` pole is reconstructed in local space without colliders,
  a bus actor or a bus director.
- Scoped documentation review and the full-worktree `git diff --check` passed
  after serializer-only import churn was removed. Fast mode intentionally
  omitted the full EditMode/PlayMode suites, a player build and a rendered
  walkthrough.

## 2026-08-11 — Repository artifact cleanup

- Removed the unused stock URP tutorial scaffold (`Assets/Readme.asset` and
  `Assets/TutorialInfo`). Its GUIDs and editor types had no references outside
  the scaffold, and it was unrelated to the runtime-composed project.
- Removed three superseded Stairwell albedos and their metas from `Resources`:
  wall paint, corroded metal and door paint. Runtime, tests and documentation
  use only their active `V2` replacements, so the old versions unnecessarily
  increased repository and packaged Resource size by about `6.8 MB`.
- Cleared ignored, reproducible local output: two old player builds, 829 test
  result files, Python bytecode and five stale diagnostic logs. Active Unity
  caches, IDE project files and user settings remain untouched. Total local
  space reclaimed was about `550 MB`.

Verification:

- Asset GUID/path audit found no external references to the deleted tracked
  files; all remaining Unity assets retain matching metas and unique GUIDs.
- `BarPromenade.Runtime.csproj` built with `0` warnings and `0` errors, and the
  scoped staged diff check passed. A focused Unity runner exited before test
  discovery and produced no result XML, so no Unity test result is claimed.

## 2026-08-11 — Road v2.1 three-way pedestrian junction fix

- Fixed the Home-loading exception introduced when Road v2.1 began accepting
  safe three-way bus aprons. The pedestrian graph and its physical closed-side
  sidewalk now share the displaced `4.5 m` corner coordinate, so every link
  remains axis-aligned instead of connecting a new corner to the old `3.5 m`
  mouth.
- The closed side is a continuous `1 x 8 m` raised strip outside the clear
  `8 x 8 m` bus core. It meets both corner pads, retains the exact `1 m`
  pedestrian corridor and does not occupy any real bus approach.

Verification:

- Focused Unity EditMode regressions for the production Home pedestrian graph
  and the physical three-way sidewalk mouth passed `2/2`; the shared Road v2
  apron and raised-sidewalk contracts also passed `2/2`. Fast mode intentionally
  omitted the full EditMode/PlayMode suites and a player build.

## 2026-08-11 — Canonical Route 01, physical stops and map overlay

- Replaced the retained branching bus graph with the immutable
  `bus-route:default-coastal:ring-01:ccw`: one right-hand counter-clockwise
  Street ring around Central Park. Every link now has one ordered successor and
  every lap repeats Industrial, Nightlife, Residential and Old Town without
  route RNG or player pursuit. Sampled full-body clearance still admits the
  proven straight and `6 m` left-turn geometry and rejects unsafe tight turns.
- Added four semantic route-owned stops on safe straights in that district
  order, including stable IDs, localization keys, lap distances and roadside
  poses. `CityBusStopWorldBuilder` gives each one a physical blue Route `01`
  pole; the random roadside decoration selector no longer emits bus shelters.
  The actor serves every stop once per lap with its existing seeded `3-5 s`
  two-door dwell, then resets service state at the loop seam.
- The canonical ring deliberately traverses the frontage street beside
  Nightlife's Last Route Island, superseding the earlier edge exclusion, while
  stop placement still excludes that frontage. The island therefore remains a
  non-working abandoned stop rather than becoming Route 01 infrastructure.
- Reworked one-slot activation around the fixed loop. Dynamic obstacle-safe
  poses prefer the fog-hidden `76-86 m` band and fall back to `56-86 m` only
  when forward loop distance reaches a player-side encounter sample; a loop
  with no forward encounter sample is rejected. Recycling still waits for `92 m`
  complete-body clearance, and camera/frustum state remains irrelevant.
- Added an immutable simplified bus-map overlay. The City map draws the blue
  ink-outlined loop below the orange player itinerary, four numbered localized
  hover stops and a compact route/stop legend; a live bus marker and boarding
  remain deferred.
- Expanded Road v2.1 apron selection from safe four-way nodes to safe three- or
  four-way nodes, while retaining full-core, real-approach and pedestrian
  clearance checks. Added focused planner, runtime, map-overlay, localization
  and scene-composition coverage for the new contracts.

Verification:

- The focused Unity EditMode selection covered the planner, fixed-loop
  runtime, map overlay, random-decoration exclusion and RU/EN catalogs:
  `25/26` passed initially. Its only failure exposed an over-permissive
  synthetic road-edge fixture, not production behavior; after narrowing that
  fixture to its intended spawn segment, the exact failed regression passed
  `1/1` and the complete focused `CityBusRuntimeTests` fixture passed `13/13`.
- Scoped source/documentation diff review and `git diff --check` passed. Fast
  mode intentionally omitted full EditMode/PlayMode suites, a player build and
  a rendered walkthrough.

## 2026-08-11 — Ambient city bus and Road v2.1 junctions

- Added the accepted production design vehicle at its real
  `8.25 x 2.38 x 2.95 m` dimensions and `4.5 m` wheelbase. The generated FBX,
  manifest and Resources prefab contain the exterior shell plus a visible
  driver area, dashboard, twelve passenger seats, rails, two articulated doors,
  four wheels, steering pivots and registered head/tail/cabin light renderers;
  runtime never shrinks the model to make the road fit.
- Extended the shared street surface to Road v2.1. A stable selector reserves
  eligible Street-only four-way nodes outside the zebra/signal set, moves their
  four `1 x 1 m` corner sidewalk pads onto clear adjacent ground, exposes a
  full `8 x 8 m` asphalt core and cuts each raised approach curb back by
  `4.5 m`. The resulting flush shared apron preserves the pedestrian line while
  clearing the bus's rear-body sweep. Home retains the same geometry in its
  bounded reconstruction.
- Added a deterministic right-hand, Street-only bus graph with sampled
  long-body clearance. It admits straight links and analytic `6 m`-radius left
  turns through Road v2.1 aprons, rejects the tighter `3 m` right-turn
  candidates and retains a cyclic strongly connected route. Compatible
  roadside bus shelters map to stops first; when the strict retained route has
  none, it receives exactly one deterministic route-native stop on a safe
  retained straight. That fallback owns `CityBusStopOrigin.RouteNative` and an
  empty `SourceDecorationId`, never a fabricated shelter identity. The
  Nightlife last-route-island frontage is intentionally outside the drivable
  graph. Route, anchor and mapped-shelter stop counts stay derived data rather
  than content constants.
- Added one pooled ambient-bus slot in City. Obstacle-safe spawning prefers the
  fog-hidden `76-86 m` band; initial routing approaches the player and then
  releases into ordinary roam.
  The bus yields to the player and active pedestrians, serves stops with a
  randomized `3-5 s` dwell and two-door animation, and recycles only after the
  closest point of its complete body reaches `92 m`. Camera direction, frustum
  state and far clip never drive this lifecycle. The one-slot cap deliberately
  permits intervals with no active or visible bus.
- Kept the runtime deliberately out of Home. No real Street pass-through in
  the balcony exterior has both complete-body seams at or beyond the hidden
  `56 m` boundary, and the default facade faces a visible road terminal.
  Fabricating another road would contradict the generated city; owning spawn
  or pooling from the Balcony camera would create a visible pop. The existing
  pedestrian exterior runtime remains unchanged.
- Added a kinematic physical body on the dedicated `CityBus` layer, rolling and
  steering wheels, brake/night-sensitive emission and a generated `22050 Hz`
  engine loop. Presentation and audio reset before pooling; the passive prefab
  itself remains collider-free and non-interactive.

Verification:

- Focused Unity EditMode selection passed `13/13`: `CityBusPlannerTests`,
  `CityBusRuntimeTests`, `CityBusAssetImportTests`, the Road v2.1 surface
  regression and the pedestrian-apron regression.
- Focused Unity PlayMode City scene smoke passed `1/1`.
- Fast mode intentionally omits complete EditMode/PlayMode suites, a player
  build and a broad rendered walkthrough.

## 2026-08-11 — Road v2 street cross-section

- Raised the canonical default street footprint from `6 m` to `8 m`. With the
  existing two `1 m` sidewalks, ordinary streets now expose a `6 m`
  carriageway, an `8 x 8 m` junction core and a clear `6 x 6 m` carriageway
  apron. The unchanged `18 m` blocks now produce a `26 m` grid step and a
  `312 m` default 12-block core span.
- Kept the migration data-first: entrances and sidewalk arrivals, pedestrian
  lanes, fences, night fixtures, decoration clearance, map projection and the
  bounded Home reconstruction continue to derive from `RoadWidth` and
  `NodeSpacing`, so no duplicated scene geometry was introduced.
- Replaced the pedestrian production regression's obsolete fixed home
  coordinate with the generated sidewalk arrival, and added focused Road v2
  coverage for the default width, pitch, carriageway, junction apron and
  widened zebra.
- Recorded the scope boundary explicitly: the cross-section is ready for a
  vehicle route plan, but a long bus still requires a swept-turn proof using
  its final body, axle and steering dimensions before bus runtime is added.

Verification:

- Focused Unity EditMode selection passed `4/4`: the Road v2 surface contract,
  default city dimensions, stationary-player pedestrian approach and Home
  exterior pedestrian transform.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City walkthrough.

## 2026-08-11 — Stationary-player pedestrian encounters

- Confirmed from the reported session log that City initialized 210 pedestrian
  anchors without errors and then ran for roughly 104 seconds without a visible
  encounter. The presentation prefab and all 38 renderer bindings remained
  valid; the failure was in the distance lifecycle.
- Kept obstacle-safe `76-86 m` as the preferred hidden spawn band. The reported
  home-return position exposed that both anchors in that ring belonged to
  sidewalk components whose closest point was still `38.5 m` away, while the
  player-linked component had anchors only at roughly `34-49 m`. Added a
  dense-fog `32-86 m` connected fallback for that topology.
- Added a one-shot approach phase: until a walker first reaches `24 m`, eligible
  non-backtracking turns follow shortest physical graph distance to the nearest
  player-side node in their own connected component. Once reached,
  that slot permanently returns to seeded random roaming for the rest of the
  spawn, while its independent zebra decision remains intact.
- Extended hidden daytime acceleration down to `32 m`, still inside dense fog,
  so the guaranteed approach does not spend most of its time beyond the `48 m`
  camera. Night keeps its authored movement speed and sparse timing.
- Added focused coverage for a branch whose seeded ordinary choice points away,
  guided zebra decisions, the bounded stationary-player approach, the exact
  default seed/home-return graph and the no-reacquisition contract.

Verification:

- `dotnet build BarPromenade.EditModeTests.csproj -nologo` passed with zero
  errors and 15 existing `CS0649` manifest-field warnings.
- Focused Unity EditMode `CityPedestrianRuntimeTests` passed `13/13`, including
  the exact `20260727` home-return stationary-player regression.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City walkthrough.

## 2026-08-11 — Restored readable stairwell textures

- Corrected the first stairwell texture pass after live inspection showed that
  URP/Lit multiplied the new maps by palette colors authored for a white map,
  removing another `56-74%` of surface light and crushing texture variation.
- Added per-recipe linear-albedo compensation (`2.17x-3.98x`) to map the
  original semantic color to a display tint whose textured mean matches the
  former flat-color brightness. Lighting, post exposure, hero/cat presentation
  and emissive fixtures remain unchanged.
- Added higher-macro-contrast ImageGen V2 wall, door and corroded-metal maps;
  the original lower-contrast sources remain beside them. The active eight-map
  set now enforces opaque RGB storage, Repeat-safe edges, at least `24/255`
  sampled `p95-p05` contrast and a compensated linear mean within `0.08` of
  the original brightness.

Verification:

- `dotnet build BarPromenade.EditModeTests.csproj -nologo` passed with zero
  errors and 15 existing `CS0649` manifest-field warnings.
- A direct validator passed all eight active sources for opacity, contrast,
  Repeat-edge delta and compensated mean brightness.
- Focused Unity EditMode `StairwellSurfaceAppearanceTests` passed `20/20`,
  including active-map imports, compensated brightness, projection-aware
  tiling and enabled-renderer coverage.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered Stairwell walkthrough.

## 2026-08-11 — Textured stairwell surfaces

- Added eight opaque RGB ImageGen albedos under
  `Resources/Stairwell/Textures`: wall paint, ordinary concrete, worn stair
  concrete, corroded metal, door paint, damp/damage, dirty wood and mixed
  debris. Unity imports each at runtime as `512x512` sRGB with Repeat, Bilinear
  filtering, mipmaps, anisotropy `4`, no compression and no readable CPU copy.
- Added `StairwellSurfaceAppearance` as the single recipe/cache boundary. It
  retains native primitive UVs, maps visible box planes and cylinder
  circumference/length explicitly, derives deterministic physical scale and
  stable hierarchy-based offsets, and writes `_BaseMap`, `_BaseMap_ST`,
  smoothness and metallic through material property blocks while preserving
  the existing `_BaseColor`/`_Color` tint and shared `RuntimePrimitiveLit`
  material.
- Routed every enabled ordinary renderer from `StairwellWorldBuilder` and
  `StairwellDressingBuilder` through the new surface wrappers: walls and dirty
  bands; ground, ceiling and columns; steps and landings; rails, grilles, doors
  and frames; pipes, vents, cabinets and radiator; damage, litter and upper
  debris; and all non-emissive fluorescent hardware.
- Left hidden walkable ramps and the upper safety blocker untextured, and kept
  emissive tubes, halos, the production hero, cat and dust/VFX on their existing
  specialized presentation paths. Geometry, colliders, cameras, lighting and
  stairwell traversal did not change.

Verification:

- Focused Unity EditMode `StairwellSurfaceAppearanceTests` passed `20/20`,
  including tall-cylinder and first/last-step texel-density regressions.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a manual rendered Stairwell walkthrough.

## 2026-08-11 — Daytime pedestrian encounter cadence

- Confirmed that the fresh `06:00` start already selects daytime pedestrian
  rules; the strict night boundary remains `<06:00` / `>=19:00`.
- Fixed two distant actors monopolizing the complete daytime pool outside the
  `48 m` City view. Actor simulation now accelerates smoothly from `1x` at
  `56 m` to at most `2.75x` from `76 m`, so an inward route reaches the player
  sooner and an outward route crosses the existing `88 m` recycle boundary
  sooner. Spawn anchors, two-slot cap, randomized delays and camera-independent
  lifecycle remain unchanged.
- Kept night actors at authored pace in addition to their existing one-slot cap
  and longer delays.
- Added a focused straight-approach regression that bounds the hidden daytime
  transit and verifies ordinary near-range and night movement speeds.

Verification:

- Focused Unity EditMode
  `CityPedestrianRuntimeTests.Factory_DaytimeFastForwardsOnlyFogDistantWalkers`
  passed `1/1`; Unity compiled the affected Runtime and EditMode assemblies.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City/Home walkthrough.

## 2026-08-10 — Textured ground between city buildings

- Added one opaque generated compacted-soil albedo at
  `Resources/Textures/CityGroundSoilAlbedo`. Unity imports it at runtime as
  `512x512` sRGB with Repeat, Bilinear filtering, mipmaps, anisotropy `4`, no
  compression and no readable CPU copy.
- Applied the soil through `12 m` world-aligned XZ UVs and a material property
  block on the shared `RuntimePrimitiveLit`. The City keeps the existing
  collider-backed `Active Land` combined mesh, while the clipped Home exterior
  reconstruction uses the same visual recipe without adding a collider.
- Left beach, lake-shore, cemetery, water, park lawn and street treatments
  unchanged, and expanded the parameterized exterior-surface contract to cover
  the new resource, import, seam, UV, shared-material and MPB settings.

Verification:

- Focused Unity EditMode `RuntimePrimitiveFactoryTests` passed `9/9`; Unity
  compiled the affected Runtime and EditMode assemblies without errors.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City/Home walkthrough.

## 2026-08-10 — Scrollable city-map line clipping

- Fixed the scrollable full-screen City map leaking and scattering roads, park
  paths and short landmark strokes across the title and surrounding panel.
- Composed the rotated line transform around the active map-group origin under
  the retro canvas matrix, then clipped each visible segment to the local
  viewport while accounting for its direction and thickness. Route-panel
  legend lines remain outside that map-only clipping context.
- Extended the existing line-rendering coverage with the nested scaled-group
  transform and horizontal, vertical, diagonal, fully external and already
  visible clipping cases.

Verification:

- Focused Unity EditMode map-line selection passed `2/2`; Unity compiled the
  affected Runtime and EditMode assemblies without errors.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City walkthrough.

## 2026-08-10 — Player-relative pedestrian lifecycle

- Removed the Main Camera from `CityPedestrianDirector` and factory inputs.
  Spawn selection, active lifetime and pooling no longer read camera direction,
  frustum membership or far-clip settings.
- Moved unique obstacle-safe spawns into the `76-86 m` player-relative band.
  At its inner edge the fixed `0.070` Exp2 City fog retains less than `0.2%`
  scene transmittance even at the widest production `70-degree` 16:9 frustum
  corner after a conservative combined `6 m` camera and full visual-envelope
  depth offset; actors remain active through camera turns and return to the
  pool only after moving beyond `88 m` from the hero.
- Replaced the immediate deterministic fill with a director-local runtime
  random stream for candidate rank, motion/palette variation and timing. The
  first one-slot event waits `1.25-7.5 s`, each later slot or replacement gets
  a separate `3.5-12.5 s` delay, and failed searches retry after `0.8-2.4 s`.
- Added a strict `<06:00` / `>=19:00` spawn mode with one fresh-population slot,
  `15-35 s` initial delays, `30-70 s` replacement delays and `4-10 s` retries.
  Entering night does not cull either of two walkers already active at dusk.
- Kept Home's Balcony-only enable/disable as a scene-composition boundary while
  applying the same distance lifecycle whenever its local street runtime is
  active. Its transformed graph now retains a bounded `100 m` approach-anchor
  context beyond the facade while the rendered street slice remains `48 m`.
  Replaced the old seen/left-view assertions with player-distance,
  camera-independence and staggered-scheduling coverage.

Verification:

- Focused Unity EditMode selection covering staggered/random and night spawn
  schedules, strict time boundaries, camera-independent distance recycling,
  stable head-on yielding and the expanded Home anchor context passed `11/11`;
  Unity also compiled the affected Runtime, EditMode and PlayMode assemblies.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and scene smoke.

## 2026-08-10 — Balcony street pedestrians

- Added a Home-local projection of the seeded City pedestrian graph. Nodes and
  navigation rectangles use the existing City-to-Home facade transform, while
  spawn anchors are retained only on the bounded nearby-road set and only when
  the complete pedestrian radius lies beyond the apartment facade.
- Composed the existing two-slot pedestrian factory under `HomeInteriorRoot`
  with the real Main Camera and the player as its locality focus. The balcony
  atmosphere now enables the director only for the Balcony shot and disables
  it before restoring indoor visibility, immediately releasing presentations
  and `CharacterController`s on exit, disable or destruction.
- Moved pedestrian visibility sampling after all Home contextual camera owners
  and made player collision/yield checks require vertical capsule overlap, so
  the player four storeys above does not block street-level spawn or movement.
- Added a pure graph-transform/filter regression and a focused Home PlayMode
  lifecycle covering dormant MainRoom slots, unique off-frustum Balcony spawn
  and complete recycling after returning indoors.

Verification:

- Focused Unity EditMode test
  `ExteriorPedestrians_TransformCityGraphAndFilterSpawnAnchors` passed `1/1`.
- Focused Unity PlayMode test
  `HomeScene_SpawnsPedestriansOnlyOnBalcony` passed `1/1`.
- The older broad Home balcony presentation test was also attempted but stops
  before the new pedestrian assertions at its pre-existing collider-free
  exterior assertion: current street-lamp chunks contain `BoxCollider`s. That
  unrelated contract was left unchanged. Fast mode omitted complete suites, a
  player build and scene smoke.

## 2026-08-10 — Local camera-aware street pedestrians

- Replaced the 12 always-simulated two-point routes and six-model distance
  pool with one deterministic sidewalk graph and exactly two reusable runtime
  slots. The graph joins street lanes through radius-safe corner turns, prunes
  all reachable dead ends to its 2-core and consumes explicit zebra descriptors
  as three-link curb/carriageway connectors.
- Walkers now spawn only at unique, obstacle-clear anchors inside the player's
  local far-clip window and fully outside a conservative camera-frustum bound.
  An offscreen approach remains alive until first seen; after that, leaving the
  frame releases its controller and presentation after a short grace. An
  unseen timeout reclaims paths that never enter the shot.
- Reworked actors as resettable slots that continue forward through graph
  turns without endpoint reversals. At each zebra entry they make one seeded
  50% cross/don't-cross choice and automatically complete a chosen crossing.
  Ordinary despawn disables the `CharacterController` before returning the
  still-live PlayableGraph presentation to its pool.
- Added focused planner/runtime coverage for deterministic topology,
  radius-safe links, dead-end removal, narrow-road zebra rejection, turns,
  both zebra decisions, unique max-two offscreen spawning, static obstruction,
  slot yielding and the seen-to-exit lifecycle. Updated the scene smoke
  assertion for the valid initial population range of zero through two.

Verification:

- Focused Unity EditMode selection
  `CityPedestrianPlannerTests;CityPedestrianRuntimeTests` passed `15/15`.
  Unity compiled the affected runtime and EditMode test code in that run.
- Fast mode intentionally omitted the complete EditMode/PlayMode suites, a
  player build and scene smoke.

## 2026-08-10 — Upright pedestrian endpoint steering

- Fixed a latent 3D-facing error exposed by the raised sidewalks. Near a route
  endpoint, a small `CharacterController` height correction could dominate the
  remaining horizontal distance; feeding that vector to `LookRotation` pitched
  the complete actor and pooled visual close to horizontal throughout the
  endpoint pause.
- Pedestrian route distance, facing, final placement and endpoint completion
  now operate strictly in XZ and preserve the controller's current Y. The
  existing turn phase already used planar travel direction and remains
  unchanged.
- Added a focused regression that injects a vertical mismatch beside the final
  waypoint and verifies an upright root through endpoint pause, turning and
  reversed walking.

Verification:

- `dotnet build BarPromenade.EditModeTests.csproj -nologo` passed with zero
  errors; the 15 `CS0649` manifest DTO warnings are pre-existing.
- Focused Unity EditMode regression
  `Actor_VerticalContactCorrectionAtEndpointKeepsRootUpright` passed `1/1`
  after the user closed the Editor. Fast mode omitted broader suites, a player
  build and scene smoke.

## 2026-08-10 — Asphalt carriageways, sidewalks and zebra crossings

- Added a pure `CityStreetSurfacePlan` that keeps the canonical road footprint
  but partitions ordinary `6 m` streets into a dark `4 m` carriageway and two
  raised `1 m` sidewalks. It also plans intersection pavement, white center
  dashes, four-stripe zebra approaches, sidewalk/crosswalk walkable rectangles
  and explicit ParkPath surfaces before GameObjects exist. Generation now
  rejects widths that cannot leave a positive carriageway.
- Extracted the deterministic, public-space-safe degree-3+ intersection
  selector from the night fixture planner. Traffic signals and zebra crossings
  now share the same ordered set of at most six nodes, and center dashes are
  omitted from all intersection and crosswalk bounds.
- Reassigned the previous light road albedo to
  `Resources/Textures/CitySidewalkAlbedo` while preserving its Unity GUID, and
  added generated dark-asphalt and worn-white-paint albedos. All three use
  Repeat XZ UV recipes and material property blocks on the shared
  `RuntimePrimitiveLit`; no material instances were added.
- Updated the chunked City builder with physical sidewalk meshes and
  collider-free markings. Entrance aprons now terminate at the near sidewalk,
  match its `0.08-0.14 m` curb bounds and return the player to its center rather
  than the road axis. The bounded Home exterior consumes the same surface plan
  in local space without collision.
- Shifted the 12 deterministic ambient routes to sidewalk centers and replaced
  their separate street mask with the plan's sidewalk-only rectangles. Current
  walkers still use single-edge routes and stop before intersections; the
  crosswalk rectangles are available for a later multi-edge connector phase.

Verification:

- Focused Unity EditMode selection passed `28/28`: street-surface geometry,
  shared signal/crosswalk selection, texture import/seams/MPBs, sidewalk NPC
  containment, and deterministic frontage sidewalk arrivals.
- Unity compiled Runtime, Editor, EditMode and PlayMode assemblies during that
  invocation. Fast mode intentionally omitted complete suites, a player build
  and scene smoke.
- Scoped changed-source/document whitespace review is clean. Repository-wide
  `git diff --check` remains noisy only in the unrelated pre-existing
  `CityPedestrian3D` prefab/FBX-meta edits.

## 2026-08-10 — Physical city obstacles and open ground traversal

- Expanded the player's indexed macro walkable area from streets and explicit
  approaches to complete logical `BuildableGround` plus existing `OpenLand`.
  Overlapping road-to-ground and adjacent-ground connectors preserve
  continuity for the maximum `0.35 m` agent radius; water, unmapped cells and
  outside space remain excluded. Buildings and props now rely on their actual
  colliders instead of invisible road-only limits.
- Reclassified the 24 city-decoration families through deterministic
  `None`/`Detail`/`Blocking` tiers. Grounded structural and bulky recipes build
  one to four simple chunk-owned box proxies; rooftop, hanging and small
  narrative details stay non-physical. Added focused collision for park
  benches and hedges, the home mailbox, and lower lamp/signal poles while the
  Home exterior reconstruction remains presentation-only.
- Replaced continuous road-edge fencing with physical rails only at water,
  unmapped and active-map boundaries plus full-width true Street dead ends.
  Terminal degree includes ParkPath edges, so streets entering the park remain
  open. Existing entrance/gate/public/open-area descriptors remain available
  as decoration-clearance metadata; narrow posts remain visual-only.
- Added a dedicated `CityPedestrian` layer and presentation-gated
  `CharacterController` to pooled walkers. The controller activates only after
  an overlap-safe bind and disables before pooling; pedestrians collide with
  the player, ignore one another, are excluded from camera/interaction queries
  and retain a separate street-only navigation mask with stable head-on yield.

Verification:

- Added focused EditMode contracts for collision tiers/proxies, pedestrian
  layer and pooling lifecycle, boundary/dead-end fence classification,
  physical rails and radius-safe ground continuity.
- Passed the focused PlayMode test
  `SceneFlowSmokeTests.CityScene_GroundTraversalUsesPhysicalBoundaries`
  (`1/1`): the real player capsule crossed from a street into a clear yard,
  stopped against building mass, and the scene exposed the intended fence,
  park, mailbox, fixture, decoration and visible-pedestrian colliders.
- The initial targeted EditMode command completed script compilation but quit
  during its first asset refresh before emitting test results. Per fast-mode
  scope, no full suite, player build or additional smoke was run.

## 2026-08-10 — Textured city asphalt

- Added one opaque generated asphalt albedo at
  `Resources/Textures/CityRoadAsphaltAlbedo`. Unity imports it at runtime
  `512x512` with sRGB, Repeat, Bilinear filtering, mipmaps, anisotropy `4`,
  no compression and no readable CPU copy.
- Extended `RuntimePrimitiveFactory` with opt-in XZ planar UVs and applied a
  stable `12 m` tile size only to the City street batches and their
  collider-free Home exterior reconstruction.
- Kept the one shared `RuntimePrimitiveLit` material. Road renderers receive
  the albedo, white tint, `0.10` smoothness and zero metallic through their
  existing material property blocks, without per-surface material instances.
  Park paths, road dashes and City collider mesh ownership remain unchanged.
- Expanded focused `RuntimePrimitiveFactoryTests` coverage for the packaged
  asset/importer, opaque PNG and Repeat-edge seam threshold, road MPB and
  shared material, XZ UV density and unchanged shared collider mesh.

Verification:

- Focused Unity EditMode
  `BarPromenade.Tests.EditMode.RuntimePrimitiveFactoryTests` passed `6/6`
  tests in Unity `6000.5.5f1`.
- Documentation diff review and `git diff --check` passed.
- Fast mode intentionally omitted complete Unity suites, a player build and
  startup smoke.

## 2026-08-10 — Ambient city street pedestrians

- Added 12 deterministic short pedestrian routes to `CityGameRoot`, biased
  toward bar/home/supermarket frontages, district public places, open-area
  accesses and park gates. Endpoints remain outside intersections using the
  actual road width plus actor radius; every virtual actor continuously walks,
  pauses and turns while staying inside the street mask.
- Added a bounded pool of six visible presentations with outer-fog activation,
  camera-relative hysteresis and lightweight yielding near the player or
  another presented walker. The actors own no colliders, rigidbodies,
  interactions, prompts or persistent gameplay state, and scaled zero delta
  freezes route and animation progress.
- Authored the first resident, the `1.75 m` Lampshade Walker: a long dark-green
  coat, recessed face with one amber mark, rigid parcel bag, mismatched boots
  and a trapezoid hood. Its deterministic Blender source produces 38 rigidly
  skinned parts at 1,160 triangles on the exact 31-bone Player hierarchy, with
  no Actions, colliders, lights or emissive parts.
- Imported that model through the production Player Generic Avatar, one shared
  instanced `Player3DLit` material and four muted MPB palettes. Each pooled
  presentation directly references the Player animation FBX's looping `Idle`
  and `Walk`, keeps root motion off and grounds the animated boot-sole geometry
  while route motion remains code-owned. Explicit teardown now destroys every
  manual PlayableGraph in scene, test and failed-factory lifecycles; mutual
  builder guards prevent the pedestrian and Player importers from requeuing
  each other indefinitely.

Verification:

- Blender 5.0.1 deterministic build/validator passed: 31 matching bones,
  38 meshes, 1,160/1,200 triangles, grounded `1.75 m` bounds and zero Actions;
  generated signature
  `0e29c300259a698cba443f2d2ae9f37f9ac30c18478edf966f68d19b20a90b5d`.
- Unity importer/prefab validator passed in batch mode with the external Player
  Avatar, shared material and direct `Idle`/`Walk` references.
- Focused EditMode pedestrian selection passed 9/9 in 0.89 seconds, including
  plan stability/safety/function bias, active-pool cap/hysteresis, pause/turn,
  passive prefab contracts and 12 sampled Walk sole-contact phases. The final
  run exited without leaked PlayableGraphs.
- Fast mode intentionally omitted complete Unity suites, a player build,
  startup smoke and a rendered City walkthrough.

## 2026-08-10 — Clock-driven hunger and fatigue

- Connected hunger and fatigue to the one persistent scaled session clock.
  After the startup Wake, hunger fills from `0` to `100` over `1440` game
  minutes and fatigue over `1080`; progression freezes with the clock before
  Wake and at `timeScale = 0`, but otherwise survives and continues through
  ordinary interactions, transitions and scene loads.
- Added a pure double-precision fractional progression state, keeping large and
  small time steps deterministic and discarding overflow at the `100` cap.
  Public session values and the existing four-bar inventory Status card remain
  clamped integers; no hunger or fatigue debuff is applied yet.
- Made value-setting transactions clear their corresponding hidden fraction:
  committed food clears the hunger remainder, a normally completed bed wake
  clears fatigue and its remainder, and a new game clears both. Cancelled sleep
  preserves the accumulated fatigue instead of treating the rest as completed.
- Kept diagnostics boundary-based by recording passive need changes only when
  a visible integer level changes instead of logging each frame.

Verification:

- Focused EditMode progression and session-state selection: 12/12 passed in
  0.29 seconds.
- Focused PlayMode
  `InventoryPlayModeTests.Open_ShowsCurrentGameTimeAndFreezesIt`: 1/1 passed in
  1.12 seconds.
- Fast mode intentionally omitted complete Unity suites, a player build and
  startup smoke.

## 2026-08-10 — Session fatigue and completed bed rest

- Added session-owned fatigue as a clamped integer `0-100` value where higher
  is worse. New games start at zero, ordinary scene loads preserve it, manual
  diagnostics record it and a dedicated mutation boundary is ready for a
  future accumulation system; no runtime source raises it yet.
- Expanded the inventory Status card to four compact bars and added localized
  `УСТАЛОСТЬ` / `FATIGUE` captions without moving cash or session time outside
  the existing `150 x 172` panel.
- Added an explicit normal-completion event to the shared animated-interaction
  controller. `HomeBedInteraction` resets fatigue only after the terminal
  `BedExit`; an accepted wake that is then cancelled by transition, disable or
  lifecycle cleanup preserves the prior value.
- Extended session, localization, diagnostic and real Home-bed regression
  coverage for defaults, clamping, successful rest and cancellation atomicity.

Verification:

- Focused PlayMode
  `HomeBedInteractionPlayModeTests.Bed_FatigueResetsOnlyAfterCompletedWake`:
  1/1 passed in 2.01 seconds.
- Focused EditMode selection for fatigue state, diagnostics and localization:
  8/8 passed in 0.42 seconds.
- Fast mode intentionally omitted complete Unity suites, a player build and
  startup smoke.

## 2026-08-10 — Bounded hybrid ragdoll for drunken falls

- Added a runtime-composed 13-body ragdoll over the production Generic rig.
  `PlayerFactory` builds kinematic rigidbodies, owned colliders and constrained
  joints from serialized anatomical bindings, so rebuilding `Player3D.prefab`
  cannot erase the setup and no alternate hero is introduced.
- A failed balance check now plays `0.16 s` of the directional Fall action,
  suspends manual PlayableGraph/late-pose writes and transfers the current bones
  to physics for the rest of Falling plus Down. Owned colliders ignore each
  other and the upright `CharacterController`; a `0.68 m` pelvis tether keeps
  the physical pose near the fixed gameplay root.
- Recovery freezes physics, disables its colliders and blends the complete bone
  hierarchy for `0.16 s` into the matching Rise start before returning control
  to animation. Re-authored both physical sides as distinct full-body,
  `50`-source-frame (`1.67 s`) Rise actions: exact side-down start, brace and
  prone roll, a held hands-and-knees pose, lead-foot plant, low crouch and an
  exact `Relaxed` endpoint. Every landmark supplies the full body pose, avoiding
  the former bind/A/T-like limbs; the all-fours hold remains inside the existing
  `Rising` state rather than adding a gameplay phase.
- Completion, intoxication cancellation, transition and lifecycle cleanup
  restore the neutral graph-owned rig, kinematic bodies, input and fall-aware
  contact shadow. The fixed gameplay root remains authoritative throughout.
- Extended deterministic Blender validation and the focused failed-balance
  PlayMode contracts around full-body Down/Rise seams, all-fours support,
  every imported Rise frame's visible floor boundary, physical chest motion,
  bounded pelvis, owned collision policy, exact recovery and input cleanup.

Verification:

- Blender 5.0.1 production generation and the embedded recovery validator:
  passed (`BP3D BUILD OK`, 23 actions, 1,534/4,500 triangles). Both Rise sides
  preserve their full-rig seams and place both hands and knees in the supported
  all-fours band before the lead-foot plant.
- Focused Unity PlayMode
  `FailedBalanceCheck_FallsRecoversAndSchedulesCooldown`: 1/1 passed in
  7.15 seconds after importing the final animation FBX and runtime prefab.
  The separate imported-pose contract
  `RiseClips_PassThroughGroundedAllFoursBeforeNeutral` also passed 1/1 in
  0.28 seconds for both physical sides, including a dense 41-frame floor sweep.
- Fast mode intentionally omitted complete Unity suites, a player build and
  startup smoke.

## 2026-08-10 — Grounded and laterally anchored intoxicated 3D walking

- Restored the grounded-pose contract lost in the sprite-to-3D transition.
  The ordinary presentation now caches the neutral deformed boot-sole contour
  and offsets only the pelvis after Walk plus additive status bones, keeping
  the lower visible sole at its grounded height without moving the
  CharacterController root or `ModelRoot`.
- Made the procedural intoxication/balance layer idempotent by restoring its
  clean locomotion pose before every graph evaluation, clip sample, repeated
  late-pose application and lifecycle teardown. Removed the old unconditional
  intoxication pelvis drop; contextual Fall/Down/Rise and interaction clips
  remain outside ordinary grounding.
- Removed procedural pelvis X translation from intoxication and balance. Its
  intended `0.018` local stagger was multiplied by the imported rig's `100x`
  hierarchy scale and slid the complete visible skeleton by up to `1.8 m`;
  pelvis/chest rotation, arm stagger and knee articulation retain the sway
  without moving the authored horizontal rig anchor.
- Added a focused PlayMode regression that bakes the production foot meshes,
  covers a complete Walk cycle and the full `6.38 s` maximum-intoxication
  horizontal stagger period, rejects floor penetration, hovering and any
  lateral pelvis envelope beyond the authored Walk, and locks root/model-root
  stability plus repeated-pose idempotence.

Verification:

- Focused PlayMode verification passed `1/1`:
  `Player3DOrdinaryPresentationPlayModeTests.MaximumIntoxicationWalk_KeepsVisibleRigAnchored`
  in `8.37 s`.
- Fast mode intentionally omitted complete Unity suites, a player build,
  startup smoke and manual rendered review.

## 2026-08-09 — Placed a permanent ashtray under the balcony flick

- Sampled the shipped `SmokeExit` discard pose and placed a `0.26 m`
  low-poly worn enamel ashtray at Home-local `(7.25, 1.12, -1.67)`. Its base
  rests on the outer rail cap and its dish covers the animated ember point
  around `(7.14, 1.30, -1.67)`.
- Composed the visual-only body, dark basin and ash remnant under the permanent
  `Home Balcony` hierarchy. The prop owns no collider, light, particles or
  interaction lifecycle and is deliberately excluded from the rail dither
  group, so it remains active before, during and after smoking.
- Extended the existing smoking PlayMode regression to lock shared-material
  reuse, rail contact, exact plan placement, exit-flick coverage and continued
  visibility after the interaction restores.

Verification:

- `dotnet build BarPromenade.PlayModeTests.csproj -nologo` compiled runtime and
  PlayMode test assemblies with `0` warnings and `0` errors.
- The focused Unity test invocation could not acquire the project because the
  user's Unity editor was already open, so it exited before compilation and
  produced no test-result XML; the running editor was left untouched.
- `git diff --check` passed. Fast mode intentionally
  omitted complete Unity suites, a player build, startup smoke and manual
  rendered review.

## 2026-08-09 — Restored periodic balcony-smoking exhale smoke

- Added a deterministic runtime mouth plume to the existing 3D smoking loop.
  One `16`-particle burst starts at loop-local frame `16`, repeats with the
  held `9.5 s` cadence and reuses the shared procedural atmosphere material.
  The emitter follows the registered mouth socket without inheriting its FBX
  scale, while world-space particles travel cityward, expand and fade before
  the next loop under a `32`-particle cap. Larger particles, stronger opacity,
  broader procedural coverage and longer lifetimes keep the plume readable
  through the low-resolution PS1 composite.
- Integrated the effect with smoking ownership: positioning and entry remain
  clear, Looping starts the scheduled emitter, Exiting stops new emission but
  lets the detached plume dissipate, and completion, cancellation, disable,
  destroy or reinitialization clear every remaining particle.
- Extended the existing smoking PlayMode regression to prove two separated
  bursts one complete loop apart, outward mouth alignment and velocity,
  queued exit at an unsafe frame, lingering world-space smoke during exit and
  exact cleanup afterward.

Verification:

- Focused PlayMode verification passed `1/1`:
  `HomeBalconySmokingInteractionPlayModeTests.Smoking_ClickableExitQueuesAtCalmFrameAndRestores`.
  The run completed in `9.95 s` with no compilation or test errors.
- `git diff --check` passed. Fast mode intentionally omitted complete Unity
  suites, a player build, startup smoke and manual rendered review.

## 2026-08-09 — Added a compact lamp above the apartment entrance

- Added a deterministic `Home Entry Door Lamp` assembly to the generated Home
  interior: a narrow dark housing and hood, a shared HDR emissive amber lens
  and a shared depth-tested halo. It is centered in the existing transom above
  the door, remains under `0.35 m` wide and has no collider.
- Added a co-located shadowless warm ForcePixel Spot aimed down and into the
  room. Its full-strength cone reaches both the entrance door and the floor in
  front of it, so the fixture produces a real local pool instead of only an
  emissive dot. The explicit Home atmosphere budget is now five local lights.
- Extended the existing Home presentation regression to lock the hierarchy,
  shared materials, bloom threshold, transom placement, Full-HD main-camera
  framing, lack of collision, co-located light, illuminated door/floor targets
  and the five-light realtime budget. The old `Home Exit Header` absence
  contract remains in place.

Verification:

- Focused atmosphere PlayMode verification passed `1/1` and confirmed the real
  Spot's position, direction, intensity, range, cone, warm color and five-light
  ownership budget.
- The focused Home presentation test reached and passed every entry-lamp
  integration assertion, including full-strength coverage of the door and
  floor, then failed later in the pre-existing player-framing assertion with
  `minX = -0.0799` while the worktree contains the separate in-progress bed and
  player-animation changes. No lamp assertion failed.
- `git diff --check` passed. Fast mode intentionally omitted complete Unity
  suites, a player build and a startup smoke.

## 2026-08-09 — Re-authored balcony smoking around a real inhale

- Replaced the two-pose smoking motion with authored Blender sequences for a
  settled cityward stance, jacket reach, cigarette draw, mouth contact, cupped
  first light, held inhale, lowered-hand exhale and a rail-side exit flick.
  The existing four-second enter/loop, two-second exit, `9.5 s` held loop and
  calm exit boundaries remain unchanged.
- Corrected the socket prop from a backward `120 x 10 mm` cylinder with an
  embedded ember to a roughly `74 mm` cigarette aligned along socket-local
  `+Y`: `70 x 6.5 mm` paper plus a contiguous `4 x 7 mm` ember. It now appears
  only after the hand leaves the coat and disappears on the exit flick. The
  prop root cancels Unity's inherited FBX bone scale so those dimensions also
  remain exact in world space instead of expanding by `100x` in play mode.
- Bumped the deterministic Blender generator to `2.4.0` and added smoking
  validation for every Action's fixed root, source-facing socket contract,
  low-hand rest clearance, mouth contact/alignment and exact loop seam. Unity
  coverage now measures the animated head-to-nose direction against the real
  Home-local `+X` city vector instead of trusting only the gameplay root.

Verification:

- Blender `5.0.1` regenerated and self-validated `73` separate meshes, `31`
  bones, six sockets, `23` in-place Actions and `1,534` triangles. Inhale
  socket-to-mouth distance is `5.275 mm`, socket-axis alignment is `0.9385`,
  and both root and loop-seam error are zero. Eight key poses plus side views
  were inspected without hand/face intersections.
- Unity rebuilt `Resources/Player/Player3D.prefab` at generator `2.4.0`.
  Focused PlayMode verification passed `1/1`:
  `HomeBalconySmokingInteractionPlayModeTests.Smoking_ClickableExitQueuesAtCalmFrameAndRestores`.
  Its geometry check measures the live paper and ember in world space through
  the imported animated socket hierarchy.
- `git diff --check` passed. Fast mode intentionally omitted complete Unity
  suites, a player build and a startup smoke.

## 2026-08-09 — Rebuilt 3D bed entry and wake around a real bedside sit

- Replaced the old foot-end dock with a clear segment of the long bed edge
  nearest the apartment door. The hero now approaches facing into the room
  with his back to the mattress, and both normal interaction and opening wake
  restore to that same grounded side dock.
- Added an optional held pelvis waypoint to the shared animated-interaction
  controller. Bed entry reaches a low seated hip, holds while both feet remain
  planted, then moves inward; wake reaches the same point from the bed centre,
  holds through the supported sit and only then proceeds to standing. This
  keeps runtime pelvis alignment synchronized with the authored Blender keys
  instead of sliding the seated pose through a direct centre-to-dock lerp.
- Re-authored three-second `BedEnter` and `BedExit` Actions. Entry sits first,
  braces on the mattress, swings the legs up and lowers through the side;
  exit wakes, rolls toward the door side, pushes the chest up, drops both legs,
  settles upright, releases the hands, leans weight over the feet and rises.
  `BedSleepLoop` keeps the head at the pillow, face upward and eyes closed.
- Bumped the deterministic Blender generator to `2.3.0`. Its validation now
  checks the new source `-X -> +X` sleep orientation. Blender regenerated the
  editable source, model and animation FBXs, manifest, preview and portrait.
  The ordinary transition is now three seconds; the opening multiplier is
  two, preserving its established six-second wake.
- Replaced the legacy sprite-extent assertions with production 3D checks. The
  focused bed regression now samples the real rig both in the sleep loop and
  at the door-side seated waypoint.

Verification:

- Blender `5.0.1` regenerated and self-validated `73` separate meshes, `31`
  bones, six sockets, `23` in-place Actions and `1,534` triangles. Entry and
  exit key poses were rendered against a diagnostic mattress; seated feet,
  hand support, forward weight transfer and final stand were inspected.
- Focused Unity PlayMode verification passed `1/1`:
  `HomeBedInteractionPlayModeTests.Bed_ProgrammaticSleepStartsInLoopAndWakeRestoresPlayer`.
  It sampled the production head/feet orientation and the real pelvis at the
  held door-side seated waypoint before confirming final control restoration.
- Fast mode intentionally omitted complete Unity suites, a player build and a
  startup smoke.

## 2026-08-04 — Articulated 3D walk and stronger idle

- Re-authored the production Blender locomotion Actions. Walk now uses eight
  contact/down/passing/up phases with opposite arm swing and independent
  forearm, hand, thigh, shin and foot rotation; both elbows remain flexed and
  each swing knee reaches a readable passing pose. Idle is now a four-second
  two-sided breathing and weight-shift loop that moves the pelvis, torso,
  head, arms and softly loaded knees while retaining the exact Relaxed seam
  required by contextual handoffs.
- Limited auto-clamped Bezier interpolation to Idle and Walk, leaving Relaxed
  plus all contextual, fall and facial Actions on their linear timing. Blender
  regenerated the editable source, both production FBXs, manifest, preview
  and portrait under generator `2.1.0`; Unity rebuilt the stamped runtime
  prefab with the new four-second Idle binding.
- Replaced the linear locomotion weight step with damped `0.14 s` start and
  `0.20 s` stop envelopes. Walk playback speed now follows the visible blend,
  so a hard release does not change cadence while the gait is still fading.
  The focused ordinary-presentation regression now checks monotonic
  intermediate weights and imported elbow, knee and ankle excursions.

Verification:

- Blender `5.0.1` regenerated and self-validated `73` separate meshes, `31`
  bones, six sockets, `23` in-place Actions and `1,534` triangles. Eight Walk
  phases were inspected from front/three-quarter and side views, together with
  the strengthened Idle phases; no joint flip or blocking mesh separation was
  found.
- Focused Unity PlayMode verification passed `1/1`:
  `Player3DOrdinaryPresentationPlayModeTests.FactoryCreatesModular3DPlayerAndDrivesLocomotion`.
  The dedicated asset setup completed successfully and rebuilt the production
  prefab at generator `2.1.0` with `Idle = 4.0 s`.
- `git diff --check` passed. In fast mode, no complete Unity suite, player
  build or startup smoke was run.

## 2026-08-04 — Correct 3D hero facing

- Rotated the imported FBX model by `180°` at the generated runtime-prefab
  boundary, so the visible anatomical front now follows the authoritative
  player root and its actual planar movement. `PlayerMotor`, camera-relative
  controls, in-place clips and root motion remain unchanged.
- Made the player asset regression compare the head-to-nose direction against
  the prefab's declared forward vector and validate the bandage/shoulder patch
  on physical left/right relative to that direction. The visual-capture helper
  now frames the prefab-space forward direction instead of applying the model
  adapter twice.

Verification:

- Unity rebuilt `Resources/Player/Player3D.prefab` successfully and compiled
  Runtime, Editor, EditMode and PlayMode assemblies without errors.
- Focused EditMode verification passed `1/1`:
  `Player3DAssetImportTests.ProductionModel_HasDeterministicRuntimePrefabContract`.
- `git diff --check` passed. In fast mode, no complete Unity suite, player
  build or startup smoke was run.

## 2026-08-04 — Complete modular 3D hero migration

- Promoted the Blender hero experiment into the production player asset path.
  The deterministic source now emits a `1.75 m` A-pose model, separate model
  and animation FBXs, a manifest, 23 in-place Generic Actions and a transparent
  portrait. The Unity prefab keeps 73 independent mesh objects, 16 required
  anatomical bindings, a 31-bone armature with six non-deforming sockets and
  one shared URP/Lit material with a property-block palette.
- Replaced the active hero presentation in City, Bar, Supermarket, Home and
  Stairwell with one `Resources/Player/Player3D.prefab` instantiated by
  `PlayerFactory`. A presentation-neutral seam now feeds locomotion and status
  state into the 3D PlayableGraph, preserves physical left/right details,
  drives face/intoxication/balance bones and samples left/right fall/down/rise
  clips while the gameplay root remains authoritative.
- Migrated bed sleep, balcony smoking and cat feeding to deterministic
  enter/loop/exit clips on the same continuous world rig. The shared contextual
  controller retains grounded positioning, neutral settle, sample-then-pelvis
  alignment, terminal holds, deferred unlock, atomic preparation and owned
  cleanup. The smoking prop uses the registered right-hand cigarette socket;
  the cat keeps its independent NPC sprite track.
- Rebuilt bar-drinking arms and the refrigerator reach as filtered camera-local
  subsets of the same production prefab. Owner-scoped visibility leases restore
  the exact world meshes and contact shadow. Inventory now uses the dedicated
  transparent 3D portrait rather than cropping the retired directional atlas.
- Removed the 22 legacy runtime player atlas PNGs together with the obsolete
  sprite-rig/dynamic-shadow code and shaders. Real hero meshes cast URP shadows
  and the analytic ground-contact patch remains planted and expands/offsets
  during falls. Historical player source art and tools remain only as retired
  lineage; NPC, cat and minigame sprites are unchanged.

Verification:

- Blender `5.0.1` regenerated and self-validated the production source, model
  and animation FBXs: `73` separate meshes, `31` bones, six sockets, `23`
  in-place Actions, `1,534` triangles and an exact `1.750 m` height. Unity then
  imported the assets, compiled the affected assemblies and rebuilt the
  runtime prefab successfully from its dependency signature.
- The focused GPU-backed PlayMode selection passed `15/16` on its combined
  run. Its sole failure was the new contact-sheet foreground threshold; after
  correcting the isolated capture lighting and background, that exact visual
  regression passed `1/1`. Thus all `16` selected gameplay-scene, ordinary
  presentation, contextual-animation, first-person, shadow and visual
  contracts passed in the final code state, and the resulting four-pose
  contact sheet was inspected manually.
- `git diff --check` passed. In accordance with fast mode, no complete Unity
  suite, packaged player build or startup smoke was run.

## 2026-08-04 — Experimental modular Blender hero

- Added a Blender-native low-poly generator for the locked player design. It
  derives the `1.75 m` proportions and primary joint heights from the current
  puppet, keeps the weary head-heavy silhouette and preserves the burgundy
  overshirt, charcoal shirt, navy trousers, heavy boots, left-forearm bandage,
  right-shoulder patch and diagonal strap without mirroring.
- Kept 16 core anatomical meshes plus hair, clothes, facial pieces and
  signature details independently editable. All 3D objects retain unique mesh
  datablocks, rigid armature weights and an explicit mapping to the existing
  nine `PlayerPuppetPart` groups; preview objects cannot enter FBX/GLB export.
- Documented background generation, relaxed/A-pose, height/seed controls,
  optional preview/manifest/FBX/GLB outputs and the anatomical side convention.
  The experiment remains outside `Assets` and is not integrated into runtime.

Verification:

- Blender `5.0.1` generated, self-validated, rendered and saved the relaxed
  model: `73` separate mesh objects, `1,534` triangles, ground contact at
  `Z=0`, exact `1.750 m` hair-tip height, outward face winding and correct
  bandage/patch sides. Temporary selection-only GLB and FBX exports also
  completed successfully; a `1.60 m` A-pose/alternate-seed run reached its
  requested height exactly under the same validator.
- The generated validation `.blend`, PNG and JSON stayed under ignored
  `TestResults`; Unity tests and a player build were not run for this isolated
  authoring-tool change.

## 2026-08-04 — Playable lake, cemetery and scrollable city map

- Extended the playable `default-coastal` blueprint east without changing its
  `12 x 12` road/lot core: a `4 x 4` Lake now surrounds `2 x 2` blocked water
  with walkable shore, and a `3 x 2` walkable Cemetery occupies the
  south-eastern edge. Both receive deterministic street approaches; the
  northern beach/water pair now spans the complete `16`-cell city width.
- Added one bounded data-first open-area decoration plan. Lake builds a stone
  water edge, reeds, rocks and a weathered boat; Cemetery builds a clear entry
  path, gated iron perimeter, ordered graves and sparse dark trees. Blocking
  geometry is batched by eight shared styles in `48 m` chunks, stays out of
  water and preserves each canonical access corridor.
- Made the city-map viewport retain a readable `22 px/cell` logical scale,
  clip overflow and pan independently on both axes. It focuses on the player
  when opened and supports WASD, right stick, wheel/Shift+wheel and
  middle/right-button dragging with per-axis scroll indicators.

Verification:

- `BarPromenade.EditModeTests.csproj` compiled the affected Runtime and
  EditMode test assemblies successfully with `0` warnings and `0` errors,
  including the new viewport and open-area planner sources.
- Focused Unity EditMode verification passed `4/4`: the expanded coastal
  blueprint, deterministic Lake/Cemetery decoration plan and both map viewport
  overflow/clamping contracts.
- `git diff --check` passed. Full suites and a player build were not run.

## 2026-08-04 — F9 city-map test teleport

- Added a City-only test-teleport toggle to the existing F9 debug window while
  retaining its BarInterior minigame and intoxication tools unchanged.
- Made every canonical map lot selectable in debug teleport mode, including
  ordinary lots, public places, home, supermarket and bars. The map replaces
  its route sidebar with an explicit `Teleport? / Yes` confirmation.
- Confirming closes the map, restores modal input ownership, teleports the hero
  to the selected lot's street-front return point (or nearest generated route
  fallback), faces the lot and rebuilds any planned route from the new
  position. Normal bar-route selection remains unchanged while the mode is
  disabled.

Verification:

- Focused PlayMode verification passed `1/1` for the F9-owned toggle, selection
  of a non-bar lot and the resulting physical player relocation/input
  restoration; the run also compiled the affected Runtime and test assemblies.
- Both localization catalogs parse with all six new keys, no duplicates, and
  `git diff --check` passes.
- Full EditMode/PlayMode suites and a player build were not run.

## 2026-08-04 — Blueprint-driven coastal city MVP

- Added an immutable `CityBlueprint` model and fluent builder with stable
  blueprint/area IDs, `UrbanBuilt` versus `NonUrbanOpen` classification,
  reusable archetypes, placement policies and per-cell buildable, park,
  open-land or water topology. The catalog now owns the playable
  `default-coastal` blueprint and an explicit legacy rectangular path.
- Made the road graph, lots, validation, world/map bounds and ground surfaces
  consume the connected sparse footprint rather than assuming every bounding
  cell exists. The existing `4 x 4` park stays fixed on the blueprint center
  anchor while built-area placements can be rearranged independently.
- Extended the default city north with one connected walkable beach row and a
  continuous water row. A deterministic street approach opens its road fence,
  the player can reach the water line, water remains outside navigation and
  night fixtures reject water positions.
- Added generic Lake and Cemetery profiles for authored blueprints. Lake shore,
  water and cemetery ground receive typed surfaces, map presentation and one
  canonical street-linked approach, without claiming bespoke landmark or prop
  art in this MVP.
- Propagated area IDs through generated lots, district descriptors, bars and
  public-place descriptors, and persisted the selected blueprint ID in the
  session. City and the Home balcony exterior now regenerate from the same
  blueprint ID and seed.

Verification:

- Focused Unity EditMode verification passed `28/28` for
  `CityLayoutGeneratorTests`, including the coastal default, urban-area swap
  and irregular Lake/Cemetery blueprint contracts; Unity also compiled the
  Runtime, EditModeTests and PlayModeTests assemblies for the run.
- Localization catalogs parsed successfully and `git diff --check` passed.
- Full EditMode/PlayMode suites and a player build were not run.

## 2026-08-04 — Day/night runtime optimization

- Made City and Home day/night presentation change-driven: advancing through
  a stable day or night sample now updates the observed minute without
  reapplying identical lighting, bulb, halo or realtime-light state.
- Reused the active `RenderSettings.sun`, removed recurring
  `DynamicGI.UpdateEnvironment` calls from ordinary phase updates and retained
  environment refreshes for forced setup and Balcony lifecycle boundaries.
- Made night-factor writes idempotent, reused one bulb
  `MaterialPropertyBlock`, kept forced refresh semantics and stopped the
  disabled daytime street-light pool from rescanning the City's `438` lamp
  anchors. A `0 -> visible` transition refreshes the pool once; inactive Home
  exterior lighting waits for its existing Balcony activation refresh.

Verification:

- Focused EditMode day/night sample coverage passed `9/9`.
- Focused City day/night and Home Balcony PlayMode coverage passed `2/2`;
  after tightening the near-zero visibility guard, the final City regression
  rerun passed `1/1`.
- `git diff --check` passed. Full suites and a player build were not run.

## 2026-08-04 — Wake-started session clock and MVP day/night

- Added session-owned game time that resets frozen at `05:59`; a successful
  startup Wake sets `06:00` and starts the only persistent scaled-time driver.
  It advances at `1.0` game minute per real second, so one in-game day is
  exactly `1440` real seconds (`24` minutes), including midnight/day-index
  rollover and continuity across scene loads.
- Made the Home alarm clock follow current session hours and minutes after the
  opening handoff and on later Home visits; the inventory Status panel now
  exposes the same current `HH:MM`.
- Added shared night/dawn/day/dusk lighting samples for City, the Home window
  and the reconstructed Balcony exterior. City/Home exterior lamps, bar lights
  and halos fade with the night factor; Bar, Supermarket and Stairwell visuals
  remain unchanged.
- Kept City fog settings, matching background, `48 m` far clip,
  `CityFogField` and `CityNoirVolumeProfile` outside the time-of-day system.

Verification:

- Focused EditMode game-time/day-night rules: `13/13` passed.
- Focused PlayMode wake/clock, Home balcony and City fog-invariant paths:
  `4/4` passed; the repaired post-Wake cancellation path also passed its
  focused rerun `1/1`.
- After adding the inventory `HH:MM`, the PlayMode test project build passed
  with `0` warnings/errors and both localization catalogs parsed as valid JSON.
- `git diff --check` passed. Full suites and a player build were not run.

## 2026-08-04 — Hunger, stress and usable provisions

- Added session-owned hunger and stress scales with explicit `0/100` defaults
  for every new run. This MVP adds no passive or event-driven growth yet.
- Added data-first consumable values and one atomic inventory-use boundary.
  Cheap supermarket food relieves hunger only down to `20/100`; a vodka
  bottle represents four servings and applies its intoxication, drink count
  and stress relief together without consuming the item on a failed use.
- Routed actual alcoholic servings from direct purchases, cocktails, Beer
  Pong, Split the G and Tincture Match through the shared stress-relief commit,
  including fractional Split the G consumption and duplicate-snapshot guards.
- Kept the existing compact status card and added hunger/stress bars beside
  the portrait, while removing the redundant textual intoxication-stage label.
  Inventory now exposes localized contextual Eat/Drink actions, `U`/gamepad-
  West input, disabled no-effect food and inline result feedback.
- Added bounded hunger/stress diagnostics, focused domain/session/UI coverage
  and updated the current architecture, system and release documentation.

Verification:

- Focused EditMode coverage for needs rules, consumable/drink catalogs, session
  transactions and localization passed `102/102`.
- Review-driven stale/duplicate snapshot and saturated-counter regressions in
  `GameSessionStateTests` passed `39/39` after the final guard was tightened.
- Focused PlayMode
  `InventoryPlayModeTests.UKey_DrinksAtZeroStressAndKeepsMenuOpen` passed
  `1/1` with the graphics device required by the existing inventory preview.
- `git diff --check` passed. Full suites, player build, startup smoke and manual
  rendered review were intentionally not run under the fast-mode policy.

## 2026-08-04 — Optional supermarket music slot

- Added the optional `Resources/Audio/SupermarketMusic/supermarket_theme`
  composition slot and installed its scene-owned player under the runtime
  supermarket root.
- Reused the shared music mixer route, mild low-pass treatment, background
  loading, one-second unscaled fade envelope and scene-transition fade gate;
  the shop remains silent-safe if its track is unavailable.
- Added the supplied `supermarket_theme.mp3` with streaming, background-load
  and no-preload import settings.
- Added the resource-folder handoff instructions and focused scene-bootstrap
  coverage that works both before and after the clip is supplied.

Verification:

- Focused PlayMode
  `SupermarketPurchasePersistencePlayModeTests.Scene_BootstrapsOptionalMusicThroughSharedMixer`
  passed `1/1`.
- Full suites, player build, startup smoke and manual audio review were
  intentionally not run under the fast-mode verification policy.

## 2026-08-04 — Grocery-shop marker and map hover names

- Added the canonical `CityLayout.Supermarket` to the city map as a distinct
  high-contrast shopping-bag marker without making it a bar route stop.
- Registered bars, the player home, supermarket and district public places as
  localized hover targets. Overlapping hitboxes resolve by nearest marker and
  deterministic priority, while one wrapped retro tooltip flips and clamps to
  remain inside the map.
- Added RU/EN grocery-shop map text and focused coverage for canonical layout
  integration, hover arbitration, edge-safe tooltip placement and localization.

Verification:

- Focused EditMode `CityMapDistrictPresentationTests` and
  `LocalizationCatalogTests` passed `28/28` in the primary Unity invocation.
- The review-driven nearest-marker edge-case regression passed `1/1` in one
  narrow follow-up invocation.
- Full suites, player build, startup smoke and manual rendered review were
  intentionally not run under the fast-mode verification policy.

## 2026-08-04 — Product-centered cross-shelf supermarket browsing

- Kept the shelf browser under one modal ownership while extending previous/
  next selection across the deterministic dry, pantry/spirits and cold shelf
  order. Empty shelves are skipped in both directions, and buying a shelf's
  final product continues at the next stocked shelf instead of closing early.
- Reused every shelf's authored fixed camera position and field of view, but now
  aim it at the combined world renderer bounds of the highlighted product on
  open, selection, shelf transfer and post-purchase fallback.
- Added low-contrast `<`/`>` controls immediately beside the selected model's
  projected screen bounds. They brighten only on hover, share the existing
  keyboard/gamepad navigation action and block click-through into world stock;
  no footer control hint was added.

Verification:

- Focused PlayMode `SupermarketPurchasePersistencePlayModeTests` passed `2/2`,
  covering product centering, arrow placement/hit blocking, bidirectional shelf
  transfer, empty-shelf skipping, continued browsing after purchase, exact
  modal/camera/input restoration and the existing reload persistence contract.
- Full suites, player build and startup smoke were intentionally not run under
  the fast-mode verification policy.

## 2026-08-04 — Finite-stock supermarket

- Added `SupermarketInterior` as a seventh build scene and registered its
  runtime root. The default city now reserves one deterministic eligible
  street-front supermarket, preferring Residential and the shortest traversable
  route from the home; its dedicated facade, apron, fence opening, interaction
  trigger and return point use the canonical lot/frontage data.
- Added a validated `16 x 11 x 3.6 m` shop plan and runtime world with protected
  circulation, three shelf sections, a stockroom facade, a decorative checkout
  and one decorative cashier. The cashier/register remain scenery; purchases
  begin at a shelf and use its authored fixed product view.
- Added five localized finite product offers and shared inventory models/icons:
  chicken egg, vodka bottle, closed stew can, instant noodles and day-old loaf.
  The sealed `ClosedStewCan` remains a distinct inventory ID from the cat-ready
  refrigerator `OpenStewCan`.
- Added one atomic world-item purchase boundary for source validity, catalog
  membership, affordability and stack capacity. Success records the stable
  source, adds one inventory item, deducts cash and immediately removes the
  physical shelf product; rebuilding the scene filters purchased sources until
  `BeginNewGame`. Every failure leaves cash, inventory and shelf persistence
  unchanged.
- Added shelf pointer/keyboard/gamepad selection, localized price/balance/error
  UI, exact modal/camera/player restoration, supermarket inventory/pause/status
  installation and the separate City round-trip context.

Verification:

- Targeted EditMode passed `21/21` across
  `SupermarketPurchaseRulesTests`, `SupermarketInteriorLayoutTests`,
  `SupermarketCityPlanningTests` and `ProjectBuildSceneTests`.
- Focused PlayMode `SupermarketPurchasePersistencePlayModeTests` passed `1/1`.
  Full suites, player build and startup smoke were intentionally not run under
  the fast-mode verification policy.

## 2026-08-03 — Minimal verification by default

- Audited the canonical workflows and repository instructions after ordinary
  feature work expanded into `777` EditMode tests, `164` PlayMode tests, three
  redundant project builds, a Windows player build and a startup smoke. The
  delay came from automatic release-style verification, not from retaining the
  tests themselves.
- Made FAST verification the default even for shared and cross-system changes.
  A normal request now gets one primary check; only a shared-framework change
  may add one focused check. Documentation uses diff-check only; deterministic
  art/data uses its validator; C# uses one narrow EditMode/PlayMode selection,
  or the highest affected project build when no suitable test exists.
- Full suites now require an explicit full-regression/release request. A player
  build runs only when requested as the deliverable or gate; smoke is reserved
  for an explicit request or changed packaged-startup behavior. Existing tests
  remain available for targeted and release use instead of being deleted.
- Clarified that the contextual-animation standard defines coverage that must
  exist, not a list that must be executed on every animation change. Generic
  stall/cancel/hitch/handoff cases remain owned by the shared pipeline; each new
  animation now extends its unique validator and adds at most one happy-path
  PlayMode interaction when existing parameterized coverage cannot represent
  its scene wiring, plus atomicity coverage only for a new resource contract.

Verification:

- Documentation-only policy change: reviewed the instruction diff and ran
  `git diff --check`; no Unity test, project build or player smoke was run.

## 2026-08-03 — Mandatory future contextual-animation standard

- Added `ai/contextual-animation-standard.md` as the normative contract for
  every future `E`/area/prompt interaction that replaces the ordinary player
  rig with a bespoke sprite atlas. It requires independent authored entry,
  action and exit data; visible constrained positioning; exact neutral endpoint
  frames; a direct zero-fade handoff; terminal-frame presentation; camera-plane
  or world-up pivot correctness; owned lifecycle cleanup; and deterministic
  asset, timeline, EditMode and PlayMode coverage.
- Linked the standard from the project entry point, AI memory index, repository
  Unity rules, accepted architecture decision and player art specification so a
  future implementation cannot treat the current bed/smoking/cat behavior as a
  one-off. Deviations now require an explicit user decision recorded as an
  accepted architecture exception.

Verification:

- Documentation links and scope were reviewed against the implemented shared
  interaction pipeline; `git diff --check` passed. No runtime files or Unity
  assets changed in this documentation-only follow-up.

## 2026-08-03 — Authored entry and exit for contextual animations

- Added a shared visible `Positioning` phase for the bed, balcony-smoking and
  cat-feeding interactions. Pressing `E` now captures modal ownership while the
  ordinary articulated hero walks and turns through `PlayerMotor` to a grounded
  authored entry root; manual movement cannot redirect the approach. Separate
  entry/action/exit root, hip and facing data replace the former implicit stand
  anchor, and unreachable height, stalled motion, scene transition, disable or
  destroy paths cancel through the same state-restoring cleanup.
- Added a deterministic ordinary-rig handoff lock. Exact entry alignment selects
  the nearest eight-way direction without hysteresis, clears gait/breath/face
  offsets, holds one neutral rendered frame, then switches directly to the atlas.
  Bed and cat use exact preflipped `FrontLeft` endpoints; smoking uses the actual
  Balcony-view `BackRight` endpoint. All three installed definitions now use zero
  sprite alpha crossfade. Exit holds the atlas's terminal frame, restores the
  separately authored exit pose and defers rig unlock through its final
  `LateUpdate` render frame.
- Kept camera-plane and world-up handoffs physically aligned. Bed and cat resolve
  their upright hip references against live camera up and refresh after camera
  `LateUpdate`; Balcony ordinary and smoking sprites stay world-up. The grounded
  player-root offset is explicit in all three plans, and Cat interaction
  availability rejects a player on another stairwell level.
- Rebuilt and locked the three 64-frame player atlases, source contracts and
  hashes. Smoking frames `000/063` now match ordinary `BackRight` cell `3` exactly
  without the retired endpoint dissolve; bed and cat endpoints match ordinary
  `FrontLeft` cell `7`. Updated plans, runtime lifecycle tests and AI system docs.

Verification:

- All smoking extractor/packer, bed-atlas and cat-atlas validators passed. Runtime,
  EditModeTests and PlayModeTests projects compiled with zero errors; the final
  sequential EditModeTests and PlayModeTests builds had zero warnings.
- Complete Unity EditMode coverage passed `777/777`. Complete PlayMode coverage
  passed `162/164`; every changed bed/smoking/cat positioning, hard-handoff,
  cancellation and paired-feeding scenario passed. The two unrelated suite
  failures were existing timing-sensitive checks: the bar arrival was already
  not playing at its full-suite assertion and passed on the immediate isolated
  retry;
  the hungry-cat prompt's gameplay state passed but its batchmode-only
  `HasRenderedLayout` assertion still did not receive an `OnGUI` event.
- The Windows player built successfully at `226,017,372` bytes with zero warnings.
  A hidden 15-second D3D11 startup smoke stayed alive and logged no error,
  exception, assertion or crash before its exact launched PID was stopped.

## 2026-08-03 — Inventory-backed cat feeding

- Added a reusable single-stack inventory-target definition, pure
  `Choice -> Confirmation -> Executing` model and scene-local modal controller.
  Talk/Interact, default-No confirmation, pointer/keyboard/gamepad input,
  temporary prompt feedback, stale-requirement rejection and lifecycle cleanup
  now share one contract that other world targets can reuse.
- Added read-only inventory count/requirement queries and retained the existing
  atomic `TryRemoveInventoryItem` commit. A handler prepares every required
  presentation resource before removal, so failed setup, No, missing stew or an
  item disappearing during confirmation cannot start a free interaction or
  consume a partial requirement. The shared player animation now exposes a
  non-starting resource/anchor preflight; a thrown start refunds the committed
  stack, and target cleanup cancels only resources that adapter acquired.
- Replaced the cat's direct placeholder response with the shared choice menu.
  Talk preserves the old response; Interact without stew shows the localized
  hunger thought; Interact with stew asks `Feed the cat?` and consumes exactly
  one `OpenStewCan` only after Yes.
- Added a validated middle-shot feeding dock and paired presentation. The
  point-filtered `1024x768` player atlas plays 24 present, 16 action and 24
  return frames; the cat begins its independent top-first `512x128`, 16-frame
  `6 fps` track at the player loop while ordinary idle/look is paused. Normal
  completion and abnormal modal/target lifecycle paths restore the player rig,
  shadows, cat, camera, HUD, input and lock ownership.
- Added raw and keyed source sheets plus explicit contracts under
  `ArtSource/Player/CatFeeding` and `ArtSource/Stairwell/Cat/Feeding`. New
  deterministic validators/packers are
  `tools/build-player-cat-feeding-atlas.py` and
  `tools/build-stairwell-cat-feeding-atlas.py`; their runtime outputs are
  `Resources/Player/PlayerCatFeedingAtlas` and
  `Resources/Stairwell/Cat/StairwellCatFeedingAtlas`.
- Replaced the prompt's fixed `180x24` layout with a centered responsive panel:
  it expands up to `520` logical pixels, enables wrapping and grows vertically
  when required. Added an exact long-Russian-feedback regression that checks
  expansion, wrapping height and containment inside the `640x360` UI canvas.
- Corrected the player feeding presentation to use the shared authored
  horizontal mirror. The source sheet faces image-right while the MiddleFlight
  cat is camera-left; `TextureFlipX = true` now turns the hero and can toward
  the cat. EditMode and runtime PlayMode contracts cover both the applied flip
  and the camera-space cat/player ordering.

Verification:

- Focused inventory-target, session, localization, animated-player, interaction,
  cat runtime and feeding-asset EditMode coverage passed `97/97`.
- Focused GPU Stairwell PlayMode coverage passed `6/6`, including Talk,
  missing-stew feedback, default-No confirmation, atomic one-can consumption,
  paired animation visibility and exact completion cleanup.
- Complete EditMode coverage passed `769/769`. Both complete GPU D3D12
  PlayMode runs passed `157/158`; the only failure was the pre-existing bar
  arrival smoke assertion after its presentation had already received skip
  input from shared suite state. That exact unrelated test passed `1/1` in a
  fresh isolated GPU run, while all six Stairwell/cat tests passed in every run.
- Runtime/EditModeTests and PlayModeTests projects built with zero warnings or
  errors. A Windows x64 player built successfully at
  `Build/Windows/BarPromenade.exe` (`226,003,548` bytes); its single warning is
  Unity URP's `Hidden/Core/DebugOccluder` D3D11 truncation warning. The player
  remained healthy through a 15-second D3D12 startup smoke with no gameplay
  exceptions or assertions.
- The follow-up responsive-prompt change compiled through Runtime,
  EditModeTests and PlayModeTests with zero warnings or errors. The focused
  non-batch graphical PlayMode test passed `1/1` in the working project,
  exercising the actual `OnGUI` path and confirming that the localized hungry-
  cat text expands beyond the old width, fits and stays inside the canvas.

## 2026-08-03 — Quieter apartment music

- Reduced only the looping Home theme's source-volume ceiling from the shared
  `0.65` scene-music level to `0.35`, leaving City, Bar, Stairwell and the
  separate balcony-smoking vignette mix unchanged.
- Added focused coverage for the final Home source volume after fade-in.

Verification:

- Unity runtime, EditMode and PlayMode assemblies compiled successfully.
- Focused `HomeMusicPlayerPlayModeTests` passed `3/3`; `git diff --check`
  passed.

## 2026-08-03 — Inventory presentation fidelity

- Moved the clickable slot hit target behind inventory contents so all five
  generated point-filtered item icons remain visible above interaction state.
- Replaced the separately painted inventory portrait with a direct upper-body
  crop from the canonical neutral front player atlas cell and standardized the
  Russian cash label on the session's dollar currency.
- Added one hidden lifecycle-owned `160x128` orthographic RenderTexture stage
  with warm/cool local lighting and unscaled rotation. The lower selected-item
  panel and Examine screen now show the live 3D model while gameplay is paused.
- Extracted the refrigerator's vodka, egg and open-stew geometry into a shared
  collider-free item factory and added matching low-poly apartment keys and
  lighter models. The refrigerator retains its exact roots, dimensions,
  selection colliders and shared-material contract.
- Added presentation coverage for visible icon pixels, canonical portrait
  provenance, all five finite collider-free models, dollar localization,
  selection/model synchronization, paused-time rotation, GPU-visible preview
  pixels and preview cleanup.

Verification:

- Unity runtime, EditMode and PlayMode assemblies compiled successfully.
- Focused inventory presentation/localization EditMode passed `19/19`.
- Focused inventory/refrigerator PlayMode passed `7/7`, including a direct GPU
  RenderTexture readback of the selected model.
- Full EditMode passed `741/741`; full PlayMode passed `156/156` with no
  failed, skipped or inconclusive tests.
- A Windows x64 release player built successfully at
  `Build/Windows/BarPromenade.exe` (`222,570,079` bytes). The only build warning
  was the package-owned URP `Hidden/Core/DebugOccluder` D3D11 truncation
  warning. A hidden D3D12 smoke reached a ready `MainMenu` at `1280x720` and
  emitted no runtime warning, assertion, error or exception; the direct
  PlayMode GPU check covers the actual open inventory preview path.

## 2026-08-03 — Static decorative UI labels

- Corrected the shared retro label style so decorative `GUI.Label` text keeps
  its authored color through normal, hover, active and focused pointer states.
  The yellow Home-opening title remains yellow, while its black offset shadow
  can no longer turn yellow on hover and appear as a duplicate title.
- Interactive button styles retain their existing hover and pressed colors.

Verification:

- Runtime and EditMode test assemblies built with zero warnings and errors.
- Focused `RetroUiThemeTests` passed `12/12`; `git diff --check` passed.

## 2026-08-03 — PS1 hero inventory and refrigerator pickup

- Added one localized fullscreen `640x360` inventory to City, BarInterior,
  HomeInterior and StairwellInterior. `I` or gamepad North captures the shared
  modal lock, freezes scaled time, hides movement/interaction/camera/HUD input
  and restores the exact captured state on toggle, cancel, transition, disable
  or destroy. Pause executes first, so Escape closes inventory without opening
  pause in the same frame.
- Added a pure item catalog, ordered bounded stack state and menu model. Fresh
  sessions begin with apartment keys and a lighter; status shows the current
  intoxication stage/level and cash. The IMGUI presentation uses generated
  point-filtered portrait/item textures and exposes only working Examine and
  Close commands.
- Replaced the refrigerator `Take` placeholder with an atomic stable-source
  transfer for vodka, egg and open stew. A taken item is removed from the live
  refrigerator registry/model, added to the session inventory and omitted when
  Home is reconstructed after a scene round trip. `Use` remains unavailable
  until item-use rules exist.

Verification:

- Unity 6000.5.5f1 compiled Runtime, EditModeTests and PlayModeTests; direct
  `dotnet build Assembly-CSharp.csproj` completed with zero warnings/errors.
- Focused inventory/session/localization EditMode passed `43/43`; focused
  inventory/refrigerator PlayMode passed `12/12`, followed by the updated
  inventory controller lifecycle set at `4/4`.
- Full EditMode passed `728/728`; full PlayMode passed `155/155` with no failed,
  skipped or inconclusive tests.
- A Windows x64 player built successfully at
  `Builds/InventorySmoke/BarPromenade.exe`. A hidden D3D12 release-player smoke
  reached `MainMenu -> HomeInterior`, initialized Home in about `1.2 s` and
  emitted no runtime exception. Null-GPU `-nographics` remains unsupported by
  the project's packaged URP material contract and was not used for the valid
  smoke result.

## 2026-08-03 — Shared-lock gameplay pause menu

- Added one localized PS1-style Pause/Resume/Start Over/Quit interface to the
  runtime UI roots in City, BarInterior, HomeInterior and StairwellInterior.
  Restart and quit use a separate default-No confirmation page; save/load and
  settings remain absent.
- Pause captures the existing fullscreen modal lock, exact input/camera/HUD
  state, time scale and listener-pause flag. It freezes scaled gameplay and
  non-UI audio while the UI SFX pool remains audible, restores safely after a
  one-frame resume guard and restores immediately on lifecycle/destructive
  paths.
- Existing child modals keep first ownership of Escape, the Home opening keeps
  its exclusive lock and the Bar-specific gate prevents pause from skipping
  the arrival reveal.

Verification:

- Unity 6000.5.5f1 imported and compiled Runtime, EditModeTests and
  PlayModeTests; direct .NET builds completed with zero warnings and errors.
- Focused pause tests passed `5/5` EditMode and `5/5` PlayMode.
- Full EditMode passed `721/721`.
- Full PlayMode passed `144` active tests with the five existing ignored tests;
  one unrelated existing motor-inertia test failed because its queued key
  release was not processed before the first braking sample. The same failure
  reproduced in an isolated rerun; every pause and four-scene installation
  check passed.
- A Windows x64 player build completed successfully at
  `Temp/PauseMenuBuild/BarPromenade.exe`.

## 2026-08-03 — Silent automated test runs

- Added one shared Unity Test Framework run callback used by both EditMode and
  PlayMode assemblies. It captures the current global listener volume, keeps
  output at zero throughout the run and restores the captured value afterward.
- Muting uses `AudioListener.volume` rather than pausing audio, so source play
  state, samples, fades, scheduling and DSP-dependent assertions keep their
  ordinary semantics. The callback is preserved for standalone player tests.

Verification:

- Unity script compilation completed with `Tundra build success`.
- Focused EditMode mute registration passed `1/1`.
- Focused PlayMode mute plus existing scene/Home music lifecycle coverage
  passed `13/13`.
- TestSupport, EditModeTests and PlayModeTests projects built with zero
  warnings and zero errors.

## 2026-08-03 — Eight-direction detailed fall animations

- Added 16 transparent detailed fall atlases: all eight existing player views
  with separately authored screen-left and screen-right variants. Each atlas
  exposes 80 `128x96` cells, for 1280 runtime sprites without mirroring the
  physical left-arm bandage or right-shoulder patch.
- Added an explicit unscaled `14`-frame fall, `36`-frame down and `30`-frame
  rise mapping. The rig lazily slices only requested atlases, reuses its body
  renderer, hides the other eight layers and restores the ordinary puppet.
  Dynamic shadows use the matching full-body frame without adding renderers.
- Added a deterministic importer for Point/Clamp, no mipmaps and uncompressed
  Standalone texture data.

Verification:

- Validated all 16 RGBA files at `1280x768`: all 1280 cells contain visible
  pixels, transparent corners are clean and no green fringe remains.
- Runtime, EditMode and PlayMode C# projects built with zero warnings/errors.
- Focused fall tests passed `14/14` EditMode and `2/2` PlayMode.
- Full suites passed `715/715` EditMode and `139/139` active PlayMode tests;
  the existing five ignored PlayMode cases remained ignored. No player build
  was produced.

## 2026-08-03 — Moving balance checks

- Added a motor-input policy to the shared modal lock. Fullscreen presentations
  still stop locomotion, while the balance-specific option preserves it during
  warning and active challenge phases.
- A failed balance check now disables the motor only when the fall begins and
  restores the captured input state after rising or cancellation.
- Updated the focused PlayMode contract to require movement during the check
  and movement blocking during the actual fall.

Verification:

- Runtime and PlayMode-test C# projects compiled with zero warnings or errors;
  Unity script compilation completed with `Tundra build success`.
- The focused intoxication PlayMode class passed `3/3`, covering movement in
  Warning/Active, motor stop on failure and exact restoration after recovery.
- Full PlayMode and player-build checks were intentionally deferred in fast
  mode.

## 2026-08-02 — Accelerating intoxication recovery

- Added session-owned fractional recovery that lowers the integer intoxication
  level during free gameplay on unscaled time and persists across gameplay
  scene changes.
- Recovery takes about `12 s` per point at level `100` and accelerates
  continuously to `3 s` per point near sober. It clamps at zero, preserves the
  last-drink and consumed-drink context, and clears balance scheduling at the
  existing threshold.
- Paused recovery while a modal lock owns gameplay because the current bar
  minigames commit absolute intoxication snapshots.

Verification:

- Runtime and EditMode-test C# projects compiled with zero warnings or errors.
- Focused intoxication rules/session EditMode tests passed `51/51`.
- Full PlayMode and player-build checks were intentionally deferred in fast
  mode.

## 2026-08-02 — Runtime fog shader variant retained

- Traced the Editor/player mismatch to built-in shader stripping: every build
  scene serializes fog off, while `RuntimeSceneSetup` enables Exp2 only after
  loading. The previous build reduced `City Atmosphere Particle` from eight
  variants to one.
- Switched Graphics fog stripping from Automatic to Custom and retained only
  the used Exponential Squared mode. Added an EditMode build-contract test for
  those serialized settings.

Verification:

- The EditMode test assembly compiled with zero warnings or errors. During the
  requested rebuild, the shader compiled four internal D3D11 programs instead
  of the previous two, confirming that the fogged variant was retained.
- The Windows rebuild was stopped at the user's request so they can perform
  the final player build manually; no completed build is claimed here.

## 2026-08-02 — Apartment ambience and guarded music fades

- Raised both synchronized Home refrigerator layers by exactly `4 dB` while
  preserving their co-located equal-power door crossfade.
- Added a fifth spatial Home detail source at the bathroom tube. Every one of
  the seven applied visual flicker edges now triggers one deterministic
  `55 ms` electrical crackle; unchanged factors do not retrigger it.
- Added the optional `Resources/Audio/HomeMusic/home_theme` slot and
  `HomeMusicPlayer`. The track fades in indoors, fades out to a real pause in
  the fixed-camera Balcony zone, and resumes from the same sample on return.
- Reworked shared scene music around an unscaled smooth one-second envelope.
  Streaming clips wait for loaded audio data before playback; Single scene
  loads hold destination activation until outgoing music reaches silence.
  Missing, failed or disabled players complete safely, and a bounded fallback
  prevents the activation gate from deadlocking.
- Added deterministic envelope, preserved-sample, never-started-source,
  camera-boundary, flicker-edge, root-binding and real scene-transition gate
  coverage. Updated audio placement notes, architecture facts and player-facing
  release notes.

Verification:

- Runtime, EditMode-test and PlayMode-test C# projects compiled with zero
  warnings or errors.
- Focused synthesis EditMode tests passed `4/4`; focused audio/Home PlayMode
  tests passed `12/12`; the final never-started-source plus City transition
  regression run passed `4/4`.
- The final complete EditMode suite passed `698/698`. The final complete
  PlayMode suite passed all `137` runnable tests with `0` failures; five
  graphics-output tests remained intentionally ignored under `-nographics`.
- A fresh `StandaloneWindows64` build completed successfully at
  `156,379,088` bytes with zero build warnings. `git diff --check` passed.

## 2026-08-02 — City-biased balcony-smoking close framing

- Increased `CameraCityLookOffset` from `0.18 m` to `0.33 m`, adding
  `0.15 m` along Home-local `+X` so the close shot looks farther toward the
  reconstructed city instead of centering primarily on the hero. The target
  yaw changes from about `8.03°` to `13.12°`, an increase of about `5.09°`.
- Kept the authored close-camera position, `38°` FOV, slow harmonic drift and
  exact two-second Balcony-shot restoration unchanged.
- Tightened the framing regression: the hero resolves near `0.37` viewport X
  at `16:9` and must remain inside `0.28-0.43` across supported desktop aspect
  ratios, while a probe `1 m` farther along the city-facing direction must
  stay in frame and project to his screen-right. A semantic direction check
  also requires the close-camera forward dot with city-local `+X` to exceed
  `0.19`.

Verification:

- A fresh isolated Unity `6000.5.5f1` copy passed the focused smoking-plan
  EditMode tests (`2/2`) and the complete smoking-interaction PlayMode test
  (`1/1`), including city-biased viewport composition, drift and exact exit
  restoration.
- Runtime, EditMode-test and PlayMode-test C# assemblies compiled with zero
  warnings or errors, and `git diff --check` passed. A new
  `StandaloneWindows64` build was not repeated for this data-only framing
  adjustment; the immediately preceding smoking-camera batch built cleanly.

## 2026-08-02 — Slow balcony-smoking camera drift

- Layered a smoking-local deterministic camera drift over the existing
  quadratic Balcony-to-close-shot path instead of changing generic
  `PlayerCameraFollow`. Local X/Y/Z position amplitudes are
  `0.016 / 0.007 / 0.005 m`; pitch/yaw/roll amplitudes are
  `0.12° / 0.20° / 0.08°`.
- Each position and rotation channel combines paired low-frequency harmonics
  with periods between `13 s` and `23 s`. One presentation clock continues
  across Entering, Looping and Exiting, preventing a motion restart at phase
  boundaries.
- Reused `CameraBlend` as the drift envelope. The offset arrives with the
  existing camera push, fades back to exactly zero through the two-second exit
  and leaves the captured Balcony pose and existing FOV interpolation intact;
  there is no FOV pulse.

Verification:

- A fresh isolated Unity `6000.5.5f1` copy passed the complete EditMode suite
  (`697/697`). The first complete PlayMode run passed `132/133`; its only
  failure was the new test using `Quaternion.Angle`, which rounded the
  sub-centidegree drift to `0°`. After replacing that assertion with a stable
  small-angle calculation, the focused smoking test passed (`1/1`) and the
  complete PlayMode rerun passed (`133/133`).
- A fresh `StandaloneWindows64` player build completed successfully at
  `156,367,888` bytes with zero build warnings. The C# runtime and affected
  test assemblies also compiled without warnings, and `git diff --check`
  passed.

## 2026-08-02 — Balcony-smoking plane, facing and idle-handoff correction

- Corrected the final Balcony-shot orientation without changing the physical
  `+X` city-facing player root. The smoking definition now opts out of the
  shared/default texture mirror with `TextureFlipX = false`, matching the
  projected handedness of the actual Balcony view; the bed/default contract
  remains mirrored and keeps its existing presentation.
- Split billboard plane alignment from texture handedness in the shared
  animated-interaction definition. Smoking now sets
  `AlignBillboardToCameraPlane = false`, preserving world up and rotating only
  around yaw, so the standing silhouette and feet no longer lean with the
  pitched close camera. The default remains exact camera-plane alignment for
  the bed, where the reclining silhouette must avoid fixed-shot foreshortening.
- Rebuilt the atlas handoff around the ordinary directional rig. Frames `000`
  and `063` now match the `PlayerDirectionalAtlas` right-direction idle
  pixel-for-pixel at the same hip/foot pivot. Frames `001-007` use a
  deterministic `8 x 8` Bayer/RGB bridge into the generated smoking art,
  frame `008` is fully authored smoking art, and frames `058-062` reverse the
  bridge before the exact final idle. The authored smoking silhouette was
  also normalized to the ordinary side-view proportions.
- Added an edge-only `0.35 s` visual crossfade to the reusable animated
  interaction definition. On entry the ordinary nine-part rig fades out as
  the smoking atlas fades in; the final `0.35 s` of exit reverses the same
  handoff. Dynamic and contact shadows remain disabled for the complete
  active interaction because neither supports the alpha blend, then restore
  from their captured states only when completion returns control.

Verification:

- The in-memory extractor validation passed all 64 frames, exact ordinary-idle
  endpoints, orientation, pivot and bounded handoff-step checks. The corrected
  extracted-frame pixel SHA-256 is
  `AECBD7E0486EE89042A58C6BF7D0A561E4311C5AF23F5FD340FCD5BCF64E1C65`.
- The in-memory atlas validation passed all 64 RGBA `128 x 96` sources and the
  `8 x 8` layout. The corrected atlas-pixel SHA-256 is
  `90AA87008702C81A41259B4D60E3D9912BD4E42E23DE247A9EA2CDA16CC131A5`.
- A fresh isolated Unity `6000.5.5f1` copy after the world-up correction
  passed the complete EditMode suite (`695/695`) and complete PlayMode suite
  (`133/133`). The smoking PlayMode contract now verifies a materially pitched
  close camera, world-vertical presentation and the animated feet remaining
  within `0.01 m` of the authored Balcony dock contact.
- A fresh `StandaloneWindows64` player build completed successfully at
  `156,365,840` bytes with zero build warnings. Final extractor/atlas
  validation and `git diff --check` also passed.

## 2026-08-02 — Melancholic balcony-smoking vignette

- Added one reachable interaction point around Home-local
  `(6.60, 0.12, -1.45)`. The first `E` docks and locks the hero facing the
  city along `+X`; the view handedness and upright presentation are resolved
  by the corrective follow-up above.
- Added a dedicated 64-frame, point-filtered sequence: 24 slow cigarette-draw,
  lighter and first-drag enter frames, a 24-frame rest/drag/breath-hold/side-
  exhale loop with deliberate pauses for a `9.5 s` cycle, and 16 discard and
  idle-handoff exit frames. The retained generated/keyed sources and strict
  atlas builder provide a reproducible `8 x 8`, `1024 x 768` runtime atlas.
- The second `E` is accepted immediately but waits for a calm loop boundary
  before starting the exit, avoiding a cut during the raised-hand drag or
  active exhale. Modal input, rig and shadows restore through the existing
  animated-interaction cleanup contract.
- Added a brief hold and smooth quadratic camera push to a close `38°` FOV,
  followed by a two-second eased restoration to the captured Balcony shot.
- Added the separate optional
  `Assets/Resources/Audio/SmokingMusic/smoking_theme` slot. It restarts at
  zero gain, fades in over `3.2 s`, loops through the shared `Music` group and
  fades out with the exit; the vignette remains silent-safe until the user
  places an OGG, WAV or MP3 file in that folder.
- Added deterministic plan/timeline/asset coverage and PlayMode coverage for
  modal entry, queued safe-frame exit, camera/music envelopes and complete
  restoration.

Initial implementation verification before the corrective pass:

- Strict atlas validation passed for all 64 RGBA `128 x 96` sources, the
  shared `(64, 40)` Unity hip pivot and the `8 x 8` lower-row-first layout;
  the validated atlas-pixel SHA-256 is
  `B29D7C5963AC1DEBC89BF933DE119EF6FFE472BC8502393DF22C0FDE325B18EE`.
- The generated loop was normalized before final packing: logical frames
  `047 -> 024` are pixel-identical at the held rest bridge, while the
  `031 -> 032` mouth-pose join has only `0.03085` alpha XOR. The retained
  profile family used the then-current generated proportions; the corrective
  pass above replaced its edge handoff and smoking-specific flip contract.
- An isolated Unity `6000.5.5f1` verification copy passed the complete
  EditMode suite (`693/693`) and the complete PlayMode suite (`133/133`),
  including the then-current smoking lifecycle and city-facing projection,
  optional audio source and restoration checks. The final foot-pivot assertion
  also passed in a focused EditMode rerun (`2/2`).
- A clean `StandaloneWindows64` player build completed successfully at
  `151,678,544` bytes with zero build warnings. Final localization JSON
  parsing, Unity GUID uniqueness and `git diff --check` also passed.

## 2026-08-02 — City-parity view from the Home balcony

- Removed the balcony view's separate dark exterior recipe. City and Home now
  share one deterministic Lit palette for ground, roads, building masses,
  roofs and window states, plus one passive bar-facade builder that preserves
  the neighboring bar's door, frame, canopy, bracket and landmark without
  adding an entrance trigger or collision to Home.
- Added a balcony-only exterior atmosphere controller. The Balcony shot uses
  City's exact exponential-squared fog, fog-colored background, `48 m` camera
  cap, moonlight, reflection level and post-process values, plus one seeded
  36-particle fog field and the retained bounded street/bar light pool.
  MainRoom, Bathroom, component disable and destruction restore the captured
  Home fog, camera and lighting state and deactivate every exterior light and
  halo.
- Kept the reconstructed exterior visual-only: it still creates no second
  City root, player, camera, listener, gameplay entrance or collider. Nearby
  district public places now use the same ordinary Lit material as City while
  retaining collider-free Home presentation.
- Extended the Home balcony regression to cover the exact City fog, grade,
  moonlight and reflection contract, exterior-light activation and cleanup,
  shared Lit materials, passive neighboring-bar identity and indoor-state
  restoration.

Verification:

- The focused Home balcony PlayMode regression passed `1/1` after the final
  lighting lifecycle changes.
- A temporary GPU-backed `1280 x 720` sRGB capture test passed `1/1`; manual
  review confirmed the expected gray-green distance haze, illuminated facade
  masses and neighboring bar light. The temporary test was removed afterward.
- The complete EditMode suite passed `685/685`.
- The complete GPU-backed PlayMode suite passed `128/128`.
- A Windows build of all six configured scenes succeeded at `148,517,792`
  bytes with zero warnings.
- Runtime, Editor, EditModeTests and PlayModeTests `.csproj` builds each passed
  with zero warnings and zero errors.
- `git diff --check` passed.

## 2026-08-02 — Grounded last-route island dressing

- Removed all eight emissive magenta/cyan recipe pieces from Nightlife's
  last-route island: five repeated canopy strips, both totem halves and the
  single departure-board line. The broken canopy ring and open traversal
  grammar remain unchanged.
- Grounded the floating departure board with two visible posts and feet that
  meet both the island paving and the board shell.
- Replaced the neon repetition with two weathered canopy route plates, layered
  paper posters on the totem, three faded schedule rows, a waste bin, two
  bottles, a discarded timetable and one lost scarf. Only the bin adds a new
  intentional obstacle collider; public approaches stay open.
- Extended the City presentation regression to reject emissive island
  materials and removed part names, prove both board supports meet their
  surfaces and require the new grounded details.

Verification:

- `dotnet build BarPromenade.Runtime.csproj -nologo` passed with zero warnings
  and zero errors.
- `dotnet build BarPromenade.PlayModeTests.csproj -nologo` passed with zero
  warnings and zero errors.
- `CityNightPresentationPlayModeTests` passed `3/3`, including the new
  no-emission, grounded-support and open-approach regression coverage.
- `HomeBalconyPresentationPlayModeTests` passed `1/1`, confirming that the
  shared last-route recipe still composes correctly in the apartment exterior
  view.
- GPU visual review of `Logs/CityLastRouteIsland.png` confirmed that the old
  board is visibly supported, all cyan/magenta bars are absent and the dull
  replacement dressing reads in the live City fog and lighting.
- `git diff --check` passed.

## 2026-08-02 — Flickering bathroom spill in the Home main shot

- Replaced the isolated warm apartment-exit accent with a cold hard-shadow
  ForcePixel Spot staged just inside the bathroom threshold and aimed through
  the solid ajar door toward the existing exit area. The Home atmosphere still
  owns at most four local realtime lights and all three fixed camera poses,
  room geometry and door materials remain unchanged.
- Added one deterministic unscaled `6.4 s` fluorescent-failure cycle. The
  bathroom point pool and doorway spill stay steady for most of the cycle,
  then share one brief irregular series of deep dips.
- Connected the visible HDR tube and depth-tested halo to the same factor
  through a dedicated fixture component. The emitter uses one reused material
  property block and keeps the shared emissive material; the halo only hides
  during the deepest dip.
- Updated the focused atmosphere and complete Home-presentation regressions to
  cover source placement inside the bathroom, cold hard-shadow direction,
  bounded light count, deterministic timing and fixture wiring.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests assemblies compiled in
  Unity with no compiler errors or warnings.
- The complete filtered Home PlayMode set passed `28/28` on the final code.
- A temporary GPU-backed `1280 x 720` capture test passed `1/1`; manual review
  of the real MainRoom camera confirmed a bounded cold pool across the entry
  floor, a matching cold bathroom threshold and no whole-room overexposure.
  The temporary capture test was removed after verification.
- `git diff --check` passed.

## 2026-08-01 — Home player visibility through foreground objects

- Added one explicit Home occlusion registry populated by the runtime world,
  dressing, bathroom and balcony builders. Logical furniture, decoration,
  door and visible rail groups own stable IDs, kinds, renderer membership and
  authored minimum visibility, while the room shell, glass, lights and safety
  colliders remain outside the presentation system. The tall box on the sofa
  joins the sofa group, and the alarm-clock nightstand plus opaque clock shell
  form their own group while the emissive digits remain untouched.
- Corrected multi-object registration to accumulate renderers from every
  supplied source instead of retaining only the last source, with a dedicated
  regression proving that all parts of a composite object stay together.
- Added a pure bounds resolver with five camera-plane player samples. Rays to
  the head, left/right chest and pelvis protect the readable body; the feet
  sample remains diagnostic so low foreground objects may preserve natural
  depth.
- Added a Home-owned controller that fades an entire blocking group through
  one shared opaque alpha-clip dither material. It uses a `0.15 s` fade-out,
  `0.12 s` clear hold and `0.30 s` restoration, preserves existing property
  block colors and never changes colliders or GameObject state.
- Kept the replacement material compatible with the active PC Forward+
  renderer: clustered additional lights, cookies, light layers and reflection
  probes remain available, and clipped shadow, depth and depth-normal passes
  keep the fade coherent with shadows and SSAO.
- Opening, refrigerator and animated Home interactions suspend the cutaway and
  restore full opacity. Controller cleanup restores the original shared
  materials.
- Added registry/resolver contracts, a synthetic grouped controller lifecycle
  regression, a GPU coverage check for the dither shader and a balcony-scene
  presentation regression.

Verification:

- Focused Home occlusion EditMode checks passed, including the `12/12`
  registry contract suite; the complete EditMode suite passed `685/685`.
- Focused controller/GPU checks passed `3/3`, including real dither coverage
  and clustered Forward+ additional-light rendering; all Home PlayMode checks
  passed `27/27` and the complete PlayMode suite passed `128/128`.
- Runtime, Editor, EditModeTests and PlayModeTests assemblies built with zero
  compiler warnings and errors.
- Windows x64 player build succeeded at `148,501,936` bytes. Its one warning is
  the package-owned `Hidden/Core/DebugOccluder` vector-truncation warning, not
  a project shader warning.

## 2026-08-01 — First-class open district points of interest

- Replaced the temporary four facade POIs with a canonical layout-owned public
  land use. After bars, the player home and primary landmark cells are fixed,
  the generator selects at most one separate street-connected lot per urban
  district by access count, primary-landmark separation and a stable seeded
  rank. The default city provides all four. Authored sites require both lot
  dimensions to meet `MinimumDistrictPointLotDimension` (`18 m`); smaller
  custom blocks omit all four safely, while eligible compact layouts omit only
  a district with no safe candidate.
- Added stable public-place and access descriptors for Old Town's waterworks
  court, Residential's drying yard, Industrial's weighbridge and Nightlife's
  last-route island. A public lot contains no building, bar, home or primary
  landmark. Its full ground and street approaches enter the walkable mask,
  every adjacent street side becomes a complete fence opening, and lamp/signal
  planning keeps both the ground and approaches clear.
- Added a dedicated physical world builder. The four places use distinct
  free-standing forms and movement grammars—asymmetric basin and standpipe,
  parallel drying frames, axial weighbridge and broken-ring transit island—with
  deliberate surface/obstacle colliders instead of collider-free facade props.
  The bounded Home exterior reconstructs nearby sites from the same canonical
  descriptors without gameplay colliders.
- Returned the ordinary decoration catalog to its original 24 families and
  four primary urban landmarks. Decoration planning now excludes public lots
  naturally because they have no building.
- Rewired the city map to consume `CityLayout.DistrictPointsOfInterest`
  directly, render each public lot as open ground and show a distinct marker
  shape plus localized RU/EN name for each kind. POIs remain informational and
  do not enter route selection, pathfinding or visited-bar progress.
- Added deterministic EditMode and PlayMode coverage for reservations,
  validation, walkable approaches, complete fence openings, fixture clearance,
  world/Home construction and canonical map integration.

Verification:

- A fresh isolated Unity import and compilation completed successfully.
- Full Unity EditMode passed `668/668`.
- Full Unity PlayMode passed `125/125`.
- Windows x64 Player build succeeded at `141.5 MB`.
- `git diff --check` passed.
- A graphical or manual camera review was not run in this verification pass.

## 2026-08-01 — City zone art-direction bible

- Added a current-versus-target art bible for Old Town, Residential,
  Industrial, Nightlife and Central Park.
- Locked each zone's emotional role, spatial and facade grammar, material
  aging, light, sound, human traces, bar threshold and explicit anti-goals.
- Defined one-block visual transition bands, shared city constants,
  determinism rules, implementation slices and objective recognition checks.
- Kept the current topology, localization names, bar activity assignment,
  global noir presentation and runtime contracts unchanged.

Verification:

- Documentation-only change; reviewed against the current district generator,
  decoration plan, world builder, map localization and project memory.

## 2026-08-01 — Seeded city silhouettes, landmarks and street details

- Added a pure, version-independent city-decoration plan with stable IDs,
  independent hash salts, explicit anchor/palette/visibility contracts and a
  hard `420`-descriptor cap. Every ordinary building receives one district
  visual; the four urban districts receive one landmark each and Central Park
  receives a fountain/statue plus bandstand.
- Implemented 24 low-poly recipe families spanning rooftop silhouettes,
  facade depth, frontage stories, common roadside furniture and park features.
  Windows and facade details now use the lot's real road frontage instead of a
  fixed world direction, while ordinary facade tint keeps district color at
  night.
- Expanded static details through one dedicated builder into at most six
  shared-material batches per `48 m` chunk. The layer adds no colliders,
  realtime lights, audio sources, particles or shadows; per-kind footprints
  protect entrances, gates and existing night fixtures, and narrow frontage
  recipes stay inside the real street/building pocket.
- Reused the same seeded descriptors and recipes in the bounded Home balcony
  exterior after Home-local conversion and half-space clipping. Removed the
  superseded ordinary-lot district detail call so legacy planters, vents and
  signs cannot overlap the new compositions.
- Added pure coverage for determinism, seed variation, all 24 kinds, ordinary
  lot and landmark quotas, stable finite data and protected clearances. Added
  City/Home scene contracts for batching, shared materials and the visual-only
  component budget.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings; `git diff --check` passes.
- Focused decoration EditMode passed 6/6; focused City presentation passed 3/3
  and Home balcony presentation passed 1/1.
- Complete EditMode passed 649/649 and complete PlayMode passed 125/125.
- Windows x64 Player build finished successfully with no build-warning markers.
- A temporary D3D11 RenderTexture smoke captured and visually inspected all
  four urban landmarks plus a street market, bus shelter and park fountain;
  the temporary capture test was removed after verification.

## 2026-08-01 — Readable apartment exit lighting

- Added one separate warm, shadowless ForcePixel Spot named
  `Home Exit Door Light`, aimed at the existing stairwell door so it reads on
  the right side of the ordinary MainRoom shot. The two practical lights and
  cold shadowed window-cookie Spot remain; `HomeInteriorAtmosphere` now owns at
  most four local realtime lights, while the scene Directional light remains
  separate.
- Kept the three existing MainRoom, Bathroom and Balcony camera poses intact.
  The door geometry and material are also unchanged.
- Added PlayMode coverage for the door light's type, placement, direction,
  warm color, range, shadowless ForcePixel setup and atmosphere-owned light
  budget, plus presentation checks that it reaches and points at the door.

Verification:

- `BarPromenade.PlayModeTests.csproj` compiles with 0 errors and 0 warnings.
- Focused Home atmosphere and presentation PlayMode checks passed 2/2 and 1/1.
- The full EditMode suite passed 643/643 and the full PlayMode suite passed
  125/125 under D3D11.
- A focused D3D11 visual-capture PlayMode check passed 1/1 and confirmed that
  the added light makes the unchanged door readable in the unchanged MainRoom
  composition.

## 2026-07-31 — Clickable contextual interaction prompts

- Turned the shared bottom `InteractionPromptView` panel into a full pointer
  click target while preserving its localized `E — action` label and keyboard
  and gamepad controls.
- Routed both pointer and input activation through one cached
  `PlayerInteractor` action that rechecks input ownership, scene transitions,
  destroyed targets and `CanInteract` immediately before dispatch. All eleven
  ordinary `IInteractable` implementations inherit that path.
- Bound the modal refrigerator close prompt directly to its existing
  `RequestClose` guard while the ordinary interactor is disabled, and clear
  every stored callback when its prompt is hidden or replaced.
- Added callback-lifecycle EditMode coverage plus generic cat-prompt and
  refrigerator open/close click dispatch regressions.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings; `git diff --check` passes.
- Focused prompt EditMode passed 2/2; focused refrigerator and stairwell
  PlayMode passed 10/10, including both shared and modal prompt callbacks.
- Complete EditMode passed 643/643.
- Complete headless PlayMode passed 120 tests, skipped the same 3
  graphics-device-only tests and failed the same 2 unrelated synthetic-input
  `PlayerMotorHeadingPlayModeTests` seen before this change; all changed
  interaction fixtures passed.
- Windows x64 Player build succeeded at `148322048` bytes with 0 warnings.

## 2026-07-31 — Removed persistent control hints

- Removed key-binding guide strips from the opening menu, refrigerator browser
  and inspection, drink shop, city map, cocktail view, beer pong, Split the G,
  Tinctures in a Row and the F9 minigame window.
- Kept contextual world-interaction prompts and gameplay state text. Clickable
  serve, finish and continue actions now use action-only RU/EN labels, and the
  balance state no longer embeds arrow-key instructions.
- Deleted the retired localization entries and added an EditMode catalog
  regression that prevents those control-hint keys from returning.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests generated projects compile
  with 0 errors and 0 warnings; both localization catalogs parse and
  `git diff --check` passes.
- Complete EditMode passed 641/641, including all 5 localization catalog
  checks and the new retired-control-hint regression.
- Complete headless PlayMode passed 120 tests, skipped 3 graphics-only tests
  and failed 2 unrelated input-timing-sensitive player-motor checks. The
  isolated motor-class retry passed 5/6: the opposite-input case recovered,
  while the pre-existing held-then-released synthetic-input assertion still
  failed without any player-motor changes in this session.
- Windows x64 Player build succeeded at `148321536` bytes with 0 warnings.

## 2026-07-31 — Physical first-person bar drink service

- Replaced the retail drink shop's full-screen list with a compact overlay on
  a seated first-person presentation. A pure unscaled timeline now owns camera
  approach, persistent bottle browsing, pickup, vessel placement, pouring,
  bottle return, an exact three-second drink, empty-vessel return and an
  explicit-exit camera return.
- Derived one validated service plan from every generated bar layout and
  reserved its lower central back-bar shelf for all nine retail offers. Each
  offer builds as a separate low-poly bottle root with registered renderers,
  stable slot ID, mouth anchor, solid collider, selection trigger and
  kinematic Rigidbody; mouse rays accept both bottle colliders and respect
  solid occluders.
- Added reusable low-poly tumbler, pint, wine glass, shot glass and snifter
  meshes, shared transparent glass/liquid materials, bottom-anchored liquid
  fill and one reusable world-space pour stream. The presentation catalog maps
  water, beers, wines, vodkas and cognacs to their correct vessel, bottle style,
  colors and target fill.
- Added procedural camera-local arms: the right hand grips and tilts the exact
  selected bottle while the left hand presents and lifts the active vessel.
  The ordinary player rig plus dynamic/contact shadows hand off cleanly and
  restore with the camera.
- Preserved the existing atomic transaction at confirmation. Failed purchase
  validation remains in browsing; a successful purchase deducts and records
  exactly once, rejects exit until service completes, returns to the same
  browser after the vessel reaches the counter, and is never refunded by
  disable/destroy/scene cleanup. Repeat orders reuse the same modal ownership;
  only explicit Exit restores camera, input, HUD and player presentation.
- Integrated the presentation into `BarInteriorRoot`, protected committed
  service from the F9 debug launcher, retained immediate pre-commit replacement
  and localized the browse/serve/pour/drink/vessel-return states plus the
  dedicated `EXIT` / `ВЫЙТИ` action in RU/EN.
- Added pure catalog/timeline coverage and PlayMode contracts for nine physical
  bottles, five vessel mappings, shared resources, real fill/stream state,
  first-person lifecycle, exactly-once debit, committed cancellation rules and
  production scene flow. Also made bottle teardown avoid Unity's forbidden
  sibling reorder during ancestor deactivation and made two pre-existing
  queued-input timing checks independent from slow complete-suite wall-clock
  frames.
- Final edge-case auditing tightened the bottle row and widened the seated shot
  so complete renderer bounds retain a tested safe margin at 16:10, and added
  an exact vessel-transform snapshot/reset plus a regression that reuses pint,
  wine-glass, shot-glass and snifter instances after animation scaling.
- Raised the service camera from chest/counter height to a natural seated eye
  height and reduced its upward pitch. The counter's green floor marker and
  emissive sign now snapshot their exact renderer states, remain hidden through
  browsing, repeated serving cycles and camera return, and restore on every
  explicit-exit or close/disable path.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings; localization JSON parses and `git diff --check`
  passes.
- Focused drink-service timeline coverage passed 12/12; complete EditMode
  passed 639/639.
- Complete headless PlayMode passed 122/122 executed tests with 3 GPU-only
  tests skipped and 0 failures. All 8 bar-drink presentation, integration,
  world-builder and production-scene tests passed; the focused repeat-order
  path passed 4/4.
- The Windows x64 Player build succeeded at `148327776` bytes with 0 warnings.

## 2026-07-31 — Refrigerator hum states and PS1 item inspection

- Replaced the single refrigerator response with distinct deterministic
  closed-cabinet and open motor/fan loops. Two co-located spatial sources start
  at the same DSP time and follow the live door amount through cosine/sine
  equal-power gains; initialization, disable and scene cleanup keep both loops
  under the existing scene-local audio ownership contract.
- Added stable catalog metadata and runtime item views for the vodka bottle,
  chicken egg and open stew can. Each item registers its original root,
  renderers and a tight trigger collider without changing the eight-slot
  storage contract or introducing inventory persistence.
- Added pointer ray hover with a reversible `MaterialPropertyBlock` tint and a
  localized cursor label. Keyboard/gamepad cycling remains available as a
  fallback and uses the same registered item set.
- Added a nested unscaled `Browsing -> FlyingIn -> Inspecting -> FlyingOut`
  timeline under the refrigerator's existing modal lock. The chosen model
  eases into a camera-relative centered pivot, rotates slowly over a dark
  backdrop and receives localized name/description plus `Take`, `Use` and
  `Back` actions. `Take` and `Use` currently show a localized unavailable
  placeholder and do not mutate storage or session state.
- Normal return, outer close, cancel, component disable and destruction restore
  the exact parent, sibling index, local transform, collider state and renderer
  colors before releasing the outer interaction. The scaled retro UI now
  converts pointer coordinates into its logical `640 x 360` canvas, and a
  disabled nested controller rejects direct input calls.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings; `git diff --check` passes.
- Focused item/audio/catalog EditMode coverage passed 17/17, and the final
  refrigerator interaction PlayMode fixture passed 6/6 with real Input System
  mouse events, hover tint/shared-material assertions, backdrop alpha, item
  framing, placeholder actions and lifecycle restoration.
- Complete EditMode passed 612/612 and complete D3D12 PlayMode passed 118/118.
- The Windows x64 Player build succeeded at `148258576` bytes with 0 warnings.

## 2026-07-31 — Refrigerator puppet handoff timing

- Kept the ordinary player rig and both shadow presentations visible during
  the refrigerator camera approach, then hid them in the exact frame that
  the low-poly first-person hand becomes visible.
- Restored the rig and shadows as soon as the sealed door enters camera return;
  modal input ownership and HUD restoration still wait for the existing
  interaction completion boundary.
- Updated the focused PlayMode regression to cover the visible approach, hand
  handoff, hidden inspection/close and immediate camera-return restoration.

Verification:

- Runtime and PlayMode test projects compile with 0 errors and 0 warnings.
- Focused refrigerator interaction PlayMode passed 3/3; the neighboring pure
  refrigerator timeline EditMode fixture passed 8/8.

## 2026-07-31 — Interactive first-person Home refrigerator

- Replaced the dark counter-embedded refrigerator placeholder with one
  data-first `HomeRefrigeratorPlan` and a dedicated runtime builder/view. The
  enlarged worn-enamel cabinet now has a real hollow liner, three stained
  shelves, lower drawer, two door bins, frost, rust and grime; the counter is
  physically split around its footprint instead of covering its lower half.
- Defined six cavity slots and two door slots with stable IDs and item
  envelopes. Three initial shelf occupants build as distinct low-poly models:
  a vodka bottle, one chicken egg and an open can of stew. The contract is
  ready for later item placement without claiming a global inventory system.
- Moved the table `0.30 m` deeper in Home-local Z and added validated approach
  bounds/waypoints, trigger, camera, light and sound anchors so the real player
  can reach the refrigerator and it reads clearly from the main fixed shot.
- Added a frame-rate-independent modal open/inspect/close timeline. The camera
  follows a first-person quadratic approach, the normal puppet and shadows are
  hidden, and a procedural low-poly sleeved hand reaches for and turns the
  handle before the door unseals and swings to `102°`. Inspection persists
  until close input; completion and cancellation restore the exact Home shot,
  player renderers, shadows, controls and HUD.
- Added a cold emissive interior strip and depth-tested halo without adding a
  realtime `Light`, localized open/close prompts, generated seal/hinge/thunk
  cues and a door-open volume/low-pass response on the existing spatial
  refrigerator hum.

Verification:

- Added deterministic EditMode coverage for plan geometry, slot occupancy,
  approach clearance, timeline endpoints/chunk invariance and cue contracts,
  plus PlayMode coverage for generated storage, first-person camera/open state,
  item framing/non-intersection, sound response and modal
  restoration/cancellation.
- Runtime and PlayModeTests generated projects compile with 0 errors and
  0 warnings; focused refrigerator PlayMode passed 3/3 and the independently
  rerun pre-existing bed-interaction fixture passed 2/2.
- Complete EditMode passed 601/601 and complete D3D12 PlayMode passed 114/114.
- A 960x540 rendered-frame smoke checked the closed main-room silhouette,
  first-person hand grip and fully open inspection framing. The bottle, egg,
  stew can, shelves, drawer and interior light were all readable in-frame.
- The Windows x64 Player build succeeded at `148229232` bytes with 0 warnings.

## 2026-07-31 — Locked alarm-clock menu beat and extended first wake

- Replaced the automatic clock-to-sleeper reveal with a clock shot that stays
  active until the player chooses Wake Up. The first rendered Home frame shows
  `05:59`; the opening discards its first potentially load-inflated delta and
  then holds five unscaled seconds with no menu rendering or input path.
- Rebuilt the clock face as one reusable 28-segment display. At the timing
  boundary it reveals Wake Up/Quit without changing `05:59`, starting the
  alarm or creating new display geometry. All digits and both colon elements
  keep flickering off for only `0.16 s` at three-second intervals before and
  after the menu appears, by toggling their renderers without material
  instances or hierarchy churn.
- Wake Up alone changes the display to solid `06:00`, hides the menu and
  starts the spatial mechanical ring. The clock shot and persistent sleeping
  loop remain unchanged for exactly three unscaled seconds; the alarm then
  stops, the existing 24-frame exit starts with a `3x` duration multiplier
  (`6 s` instead of the ordinary `2 s`), and the camera glides from the clock
  to the sleeper through a `2.25 s` smootherstep quadratic path before easing
  into the active MainRoom shot. The final pose already matches gameplay when
  control returns, so there is no handoff cut.
- Cancellation during the locked clock beat restores the silent ordinary
  `05:59` display together with normal Home input; only a successful Wake
  request can switch this opening clock to `06:00`.
- Stabilized the ordinary-bed input proof by making its virtual keyboard
  current at each tested press, explicitly processing and holding the movement
  event across frames, and asserting restored movement rather than a transient
  raw key flag.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings.
- Focused opening/animated-interaction EditMode tests passed 23/23.
- Real MainMenu/opening, alarm and ordinary-bed PlayMode tests passed 7/7,
  including the five-second input gate, silent menu, Wake-only time change,
  exact three-second alarm hold, cleanup during the ring, segment counts,
  persistent flicker timing, both camera-move stages, cut-free gameplay
  handoff and the `3x`/`1x` wake-duration split.
- Complete EditMode passed 583/583 and complete D3D12 PlayMode passed 111/111.
- The Windows Player build succeeded at `148173632` bytes with 0 warnings.

## 2026-07-31 — Windows Player runtime-material repair

- Reproduced the all-magenta Windows Player and traced it to runtime-composed
  primitives retaining `GameObject.CreatePrimitive`'s implicit material.
  Editor-only URP defaults made that path look valid in Play Mode, while the
  Player data contained no `Universal Render Pipeline/Lit` shader.
- Added one serialized `RuntimePrimitiveLit` Resources material and made
  `RuntimePrimitiveFactory` assign it to every primitive without an explicit
  specialized material. Per-instance colors remain in
  `MaterialPropertyBlock`, and explicit emissive, atmosphere and glass
  materials remain unchanged.
- Added a regression contract that boxes and cylinders resolve the same
  supported shared URP/Lit material.

Verification:

- Focused `RuntimePrimitiveFactoryTests` passed 4/4; complete EditMode passed
  581/581.
- Complete PlayMode passed 110/110 on D3D12, including the graphics-device
  RenderGraph test and the real Unity Test Framework bootstrap path.
- A full Windows x64 Player build succeeded at `148166976` bytes with
  0 warnings. Its serialized Player data now contains
  `Universal Render Pipeline/Lit` and `RuntimePrimitiveLit`.
- An eight-second D3D12 built-player visual smoke reached the waking Home
  menu with the room, hero, clock and furniture correctly shaded instead of
  magenta; its Player log contained no shader/material error or runtime
  exception.

## 2026-07-31 — PS1 waking opening and Home alarm clock

- Added `MainMenu` as build scene index `0`. Its black launch camera holds the
  initial frame while `MainMenuRoot` resets the complete session, prepares the
  one-shot `HomeArrivalKind.OpeningSleep` value and Single-loads the existing
  `HomeInterior`.
- Added a frame-rate-independent Home opening that starts the existing bed
  interaction directly in its persistent sleep loop, captures modal input,
  reveals clock and sleeper fixed shots, and presents localized PS1-style
  Wake Up/Quit choices.
- Wake Up stops the alarm, requests the existing 24-frame wake sequence and
  restores ordinary Home input, HUD, shadows and active fixed-camera shot
  without reloading the room. Direct and later Home arrivals consume
  `Normal`, so they never replay the opening.
- Added one validated bed-relative nightstand and low-poly 3D alarm clock to
  every Home composition. The opening uses a generated looping mono
  `22050 Hz` mechanical ring on a fully spatial `SFX/World` source plus a
  bounded visual rattle; ordinary visits keep the clock silent.
- Extended the editor scene setup, build-scene contract, RU/EN localization,
  new-session/arrival state, opening timeline, alarm plan/synthesis/lifecycle
  and Home scene-flow coverage.
- Pinned ordinary Unity Editor Play to `MainMenu` from every currently open
  scene. Unity Test Framework's exact temporary `InitTestScene{GUID}` path
  suppresses the override while PlayMode tests start, then restores it on the
  return to Edit Mode.

Verification:

- Opening-focused EditMode passed 52/52; the complete EditMode suite passed
  575/575.
- The opening/alarm/input/transition PlayMode paths passed, including a real
  `E` press and Home-to-Stairwell handoff; the complete PlayMode suite passed
  110/110 after making the older bed movement assertion process its queued
  Input System event explicitly.
- `BarPromenade.Runtime`, EditMode tests and PlayMode tests compile through
  their generated projects with 0 errors and 0 warnings.
- Windows Player build succeeded at `147903696` bytes with 0 warnings.
- A 12-second built-player smoke recorded `MainMenu` at build index `0`, the
  complete new-game reset, `OpeningSleep` prepare/consume and initialized
  `HomeInterior` at build index `5`, with no runtime exception in the Player
  log.

## 2026-07-31 — Player-home frontage regression repair

- Restricted preferred home placement to the selected bar's actual frontage
  instead of accepting any street edge around that bar's lot.
- Applied the `48 m` traversable-route bound before accepting preferred
  candidates as well as fallback candidates. Default layouts retain their
  shared approach and `12 m` fresh-spawn distance.
- Added a direct regression assertion that the default player home shares a
  frontage road with a selected bar.

Verification:

- Runtime, EditModeTests and PlayModeTests assemblies compiled with 0 errors
  and 0 warnings.
- Targeted `CityLayoutGeneratorTests` passed 21/21, targeted
  `PlayerHomeLayoutTests` passed 28/28 and complete EditMode passed 563/563.
- Relevant `SceneFlowSmokeTests` passed 10/10. Two complete PlayMode runs
  reported 104/105 only because the pre-existing bed-input timing assertion
  at `HomeBedInteractionPlayModeTests.cs:292` did not observe a queued key
  within one frame; that test passed 1/1 in isolation.
- Windows x64 Player build succeeded at `147880324` bytes with 0 warnings. A
  15-second headless startup smoke contained no error, exception, assertion,
  failure or missing-asset messages.

## 2026-07-30 — Shared PS1-horror audio mix and interior soundscapes

- Added one canonical `BarPromenadeAudio` mixer with Music, two ambience
  layers, world/gameplay SFX and dry UI groups, plus dedicated environment
  reverb and stereo echo returns under a headroom-preserving master
  compressor.
- Added City, Bar, Stairwell, Home and DoorTransition snapshots. Home uses a
  short damped no-echo space; Stairwell uses the longest, strongest and
  deliberately dark reverb plus restrained echo. Non-door profile changes
  preserve wet tails through a `0.25 s` transition.
- Routed all existing scene music, procedural ambience, pooled SFX, UI and bar
  soundscape sources through their canonical groups. Existing music now
  background-streams instead of fully decompressing on scene load.
- Reworked the Home and Stairwell base loops into steady room beds, then added
  exactly three spatial sources and six generated clips to each interior.
  Home layers refrigerator/night air and sparse domestic details; Stairwell
  layers ventilation/electrical buzz and sparse pipe, metal, water and
  movement cues.
- Kept synthesis deterministic and data-first: pure seeded schedules bound
  delay/pitch/gain, layout planners provide world anchors, quantized
  `22050 Hz` mono generation provides the low-resolution character and
  scene-local cleanup releases every runtime clip/source.
- Added an idempotent Unity editor setup for the mixer. Required DSP
  effects/sends fail fast, and the EditMode contract validates the exact
  topology, send targets, stereo echo parameters and scene snapshot values.

Verification:

- Two consecutive mixer setup runs exited successfully without duplicate
  groups, snapshots, effects or sends; the dedicated mixer contract passed
  9/9.
- Targeted soundscape EditMode passed 10/10 and targeted component PlayMode
  passed 2/2.
- Complete solution build: 0 errors, 0 warnings.
- Complete EditMode reported 559/563; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Complete PlayMode passed 105/105.
- Windows x64 Player build succeeded at `147879812` bytes with 0 warnings.
- The final Player stayed healthy through a 15-second headless startup smoke;
  its log contained no error, exception, assertion, failure or missing-asset
  messages.

## 2026-07-30 — Interactive stairwell cat

- Added one seated rear-view pixel-art cat to the upper bar of the middle
  landing rail. Its exact camera-plane billboard stays composed in all three
  fixed stairwell shots, while directional rows turn the head toward the
  player.
- Built a point-filtered `512x256` atlas from retained image-generation source
  sheets. The first three rows provide breathing, tail and ear idle variants;
  the fourth provides a complete eight-frame paw-lick and face-wash sequence.
- Added a deterministic idle timeline: the first grooming cycle starts after
  24 seconds, lasts 2 seconds at 4 fps and repeats every 36 seconds.
- Added a non-blocking `IInteractable` trigger on a radius-safe middle-landing
  approach. `E` temporarily replaces the localized prompt with the short
  placeholder “Кот молча смотрит.” / “The cat watches.” without locking player
  input.
- Added atlas/import, layout, look hysteresis, idle/grooming, interaction,
  localization and scene-presentation coverage. The fixed-shot assertion now
  checks both viewport composition and an unobstructed physics line of sight.

Verification:

- Final stairwell-cat EditMode selection passed 16/16.
- Final stairwell presentation PlayMode passed 4/4.
- Complete EditMode reported 540/544; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Complete DX12 PlayMode reported 101/102; the sole failure was the unrelated
  input-timing-sensitive Home bed test, which passed its immediate targeted
  DX12 retry 1/1.
- The complete solution built with 0 errors and 0 warnings.
- Windows build succeeded at `147846797` bytes with one package-owned URP
  `Hidden/Core/DebugOccluder` shader warning.

## 2026-07-30 — Stairwell scene-music slot

- Added an optional scene-local `stairwell_theme` resource slot under
  `Resources/Audio/StairwellMusic`.
- Added `StairwellMusicPlayer` to the runtime-composed stairwell root. It uses
  the shared looping non-spatial music setup, remains silent when the track is
  absent and is destroyed on the next Single-mode scene transition.
- Added an in-folder delivery README plus PlayMode coverage for the player,
  source configuration, filter, resource path and scene ownership.
- Updated the current overview, architecture, system map/tree, README and
  release notes with the new music contract.

Verification:

- Stairwell presentation PlayMode passed 3/3.
- Runtime project built with 0 errors and 0 warnings.
- `git diff --check` passed.

## 2026-07-30 — Stairwell traversal, fixed-camera and lighting correction

- Reproduced the blocked first flight through the real `PlayerMotor` path
  instead of the earlier direct-`CharacterController` helper. The physical
  ramp was sound, but independently radius-eroded walkable rectangles left a
  gap at each floor/flight seam.
- Extended the three stair navigation corridors into both adjacent landings by
  more than the controller diameter and added dense radius-aware sampling over
  the complete street-to-apartment route.
- Added a stairwell-specific 3D fixed-camera selector because the lower and
  blocked upper flights share XZ coordinates. Three hard-cut shots cover the
  lower flight, middle/second flight and apartment landing; overlapping
  vertical hold zones prevent rapid cuts at landing thresholds, and the
  player billboard uses exact camera-plane alignment while the controller is
  active.
- Corrected the fluorescent fixtures: opaque housings no longer swallow the
  emissive tubes, two sources were lowered into their camera frames, suspension
  hardware was added, HDR output/halos were strengthened and the three
  co-located flickering point lights received larger readable ranges.
- Added PlayMode coverage using queued keyboard `W` through the actual motor,
  camera-shot changes during the physical climb, per-shot emitter viewport
  visibility, fixture separation/alignment and the unchanged upper blocker.
- Stabilized one unrelated existing Home bed test discovered by the complete
  run: queued `D` input now waits for the next player frame, matching the
  other input assertions in that test instead of forcing a manual input update.
- Updated current architecture, overview, system map/tree, README and release
  notes to record the fixed navigation, cameras and visible practicals.

Verification:

- Stairwell layout/camera EditMode passed 7/7.
- Stairwell presentation, real-motor seam traversal, physical climb/blocker
  and complete home round trip passed 4/4.
- Complete PlayMode passed 101/101.
- Complete EditMode reported 527/531; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Runtime, Editor, EditModeTests and PlayModeTests projects each built with
  0 errors and 0 warnings.
- Windows build succeeded at `145532748` bytes with one package-owned URP
  `Hidden/Core/DebugOccluder` shader warning.
- `git diff --check` passed; Unity left no `InitTestScene` asset behind.

## 2026-07-30 — Industrial home stairwell

- Added `StairwellInterior` as the fifth runtime-composed build scene and
  rerouted home travel through
  `City -> StairwellInterior -> HomeInterior -> StairwellInterior -> City`.
  A consumed side-aware arrival value places the player by the street or
  apartment door without prematurely setting the City home-return state.
- Added a validated three-elevation layout with a ground lobby, middle landing,
  apartment landing and three stair flights. The 48 visible steps use three
  invisible continuous ramp colliders so the `CharacterController` cannot
  catch on individual riser seams.
- Sealed the flight above the apartment floor with a full-width,
  full-standing-height safety collider backed by a visible pile of furniture,
  wire mesh, planks and sacks.
- Built the decayed industrial-horror dressing from shared runtime primitives:
  stained concrete, rusty rails, exposed pipes, ventilation, grilles,
  electrical cabinets, radiators, damp damage and trash.
- Added three bounded flickering practical lights, a green desaturated
  Bloom/color/vignette/grain profile, at most 14 dust motes, and a dedicated
  music-free procedural ventilation/mains/pipe/knock/drip ambience.
- Added RU/EN building/apartment prompts, four distinct door-transition
  directions, deterministic layout/state/audio coverage, full round-trip
  scene-flow coverage and a physical PlayMode climb/blocker test.
- Updated the scene helper, Build Settings, Windows scene list, README,
  project overview, system tree/map, architecture notes and release notes.

Verification:

- Stairwell-focused EditMode passed 62/62.
- Stairwell presentation, physical climb/blocker and complete home round trip
  passed 3/3; complete PlayMode passed 100/100.
- Complete EditMode reported 525/529; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Runtime, Editor, EditModeTests and PlayModeTests projects each built with
  0 errors and 0 warnings.
- Windows build succeeded at `145526908` bytes with one package-owned URP
  `Hidden/Core/DebugOccluder` shader warning.
- `git diff --check` passed.

## 2026-07-30 — PC-only quality/render configuration

- Removed the unused `Mobile_RPAsset` and `Mobile_Renderer` assets after
  confirming that the project is currently Windows/PC-targeted.
- Reduced `QualitySettings` to one PC quality level at index `0`, made it the
  current and default level for every serialized platform key, and left its
  platform exclusion list empty.
- Kept mobile quality and renderer parity explicitly deferred.

Verification:

- Complete EditMode reported 517/521; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Complete PlayMode passed 98/98.
- Windows build succeeded at `145489763` bytes with one package-owned URP
  `Hidden/Core/DebugOccluder` shader warning.
- No project references to either removed Mobile asset GUID remain, and
  `git diff --check` passed.

## 2026-07-30 — Home balcony wall-leak correction

- Clipped the Home exterior presentation to
  `x >= HomeFacadeX + WallThickness / 2 + 0.01`: ground, roads, haze,
  buildings and windows now stay outside the facade, while lamps and signals
  use an additional wall-clearance filter.
- Added an opaque collider-free ceiling plus south/north return walls to close
  the remaining shell gaps without changing the window, open door or walkable
  balcony.
- Removed the obsolete emissive `Home Exit Header` that projected into the
  upper-right camera edge and sealed the `0.50 m` side gaps plus `0.34 m`
  transom gap around the front city-exit door with opaque wall sections.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds completed with 0 errors
  and 0 warnings.
- Focused `HomeBalconyLayoutTests` EditMode coverage passed 8/8.
- Focused `HomeBalconyPresentationPlayModeTests` and
  `HomeInteriorPresentationPlayModeTests` passed 2/2; the follow-up front-entry
  correction and Home-to-City transition coverage passed 3/3.
- Inspected a `1516x824` D3D control render matching the reported frame: the
  orange corner marker and unintended street sightline were absent.

## 2026-07-30 — Same-scene third-floor Home balcony

- Replaced the black Home right wall with a real window and open glazed door
  leading directly to a walkable balcony at `4.7 m` street elevation without
  loading another scene.
- Extended the validated Home walkable mask through the doorway threshold and
  across the balcony deck. Open-looking rails retain invisible solid safety
  colliders so the player can occupy the exterior space without falling.
- Reconstructed a bounded view of the actual player-home street from the
  preserved city seed: nearby roads, lots, stable windows, lamps and signals
  are transformed into Home-local coordinates without spawning a second City
  root, player, camera or realtime street-light pool.
- Added the matching balcony/window/door treatment to the generated City home
  facade and raised its default mass to `8.8 m`, with shared transform and
  dimension helpers keeping both representations aligned.
- Added one cold shadowed cookie Spot aimed through the window while preserving
  exactly the two existing shadowless practical lights. Window and door panes
  reuse one shared transparent URP glass shader/material.
- Added a third fixed Home camera shot for the balcony and expanded data-first,
  scene-presentation, lighting, glass, collider and camera regressions.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests compilation completed with
  0 errors and 0 warnings.
- Focused EditMode coverage passed 35/35; focused PlayMode coverage passed 9/9.
- Inspected the D3D visual capture of the finished window, directed night
  light, exterior street and balcony presentation.
- Complete EditMode passed 516/520; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Complete PlayMode reported 94 passed, 1 failed and 3 skipped; the sole
  failure was the pre-existing batch-input second-`E` assertion in
  `HomeBedInteractionPlayModeTests`.
- Windows build succeeded at `145488227` bytes with 0 warnings.
- `git diff --check` passed.

## 2026-07-30 — Animated bed sleep interaction

- Added a reusable frame-rate-independent animated-interaction timeline with
  `Idle -> Entering -> Looping -> Exiting` phases and a player controller that
  presents atlas frames on the camera plane between authored stand/action hip
  anchors.
- Split presentation into an outer camera-facing root and an inner sprite
  visual. Contextual interactions can now perspective-project an authored
  world axis through `WorldToScreenPoint`, preserve texture handedness and ease
  the inner visual into and out of the required camera-plane roll.
- The controller locks the motor for the complete interaction, hides the
  ordinary nine-layer rig plus dynamic/contact shadows, enables ordinary
  interaction input only during the persistent loop for the second `E`, and
  restores every captured state on completion, owner cancellation, disable or
  scene cleanup.
- Added one data-first trigger on the reachable `xMax` side of the Home bed.
  The first localized `E` plays 24 lie-down frames at `12 fps`, 16 sleeping
  frames loop indefinitely at `4 fps`, with an extra `0.25 s` delay on
  full-inhale frame `034` and an extra `0.75 s` post-exhale rest on frame
  `027` for one `5 s` breath cycle; the second localized `E` plays 24
  dedicated wake-up frames at `12 fps`. The physical player root stays safely
  beside the bed, and the crumpled shirt is hidden only while the sequence is
  active.
- Aligned the sleep pose with the bed's world `+X` head-to-foot axis after
  perspective projection, kept the authored head-left side at the `xMin`
  pillow, and moved the action hip `0.135 m` footward so the loop has balanced
  approximately `7.7 cm` head and foot margins.
- Added a shared contextual-sprite overlay shader with `ZWrite Off` and
  `ZTest Always`, eliminating bed depth-buffer clipping across the complete
  lie/sleep/wake sequence. Restored the natural `0.045 m` action-hip surface
  clearance instead of compensating with an artificial vertical lift.
- Made the bed own only the sequence it started. A disabled or destroyed bed
  now cancels persistent sleep and restores movement, interaction input, the
  rig, both shadows and the per-interaction captured clutter state; a disabled
  or uninitialized animation controller no longer advertises the prompt.
- Added the point-filtered `1024 x 768` player sleep atlas as 64 row-major
  `128 x 96` cells with pivot `(64, 40)`, all 64 source frames, keyed/generated
  working sheets and deterministic extraction, validation and packing tools.
- Added pure timeline, bed-plan and asset-contract EditMode coverage plus a
  real Home-scene `InputTestFixture` PlayMode regression for both `E` presses,
  motor locking, persistent sleep, owner-disable cancellation and complete
  movement/visual restoration.

Verification:

- After the depth-independent overlay correction, the focused
  `PlayerBedSleepAssetTests` passed 3/3, including shader support, render queue,
  pass lookup and explicit depth-state checks.
- Atlas validation passed with file SHA256
  `11EE6886B4BDB439CEB183EEE42F699B275A1362B527E61AEF06F0CC2AA4B56B`.
- Sequential .NET builds of `BarPromenade.Runtime`, `BarPromenade.Editor`,
  `BarPromenade.EditModeTests` and `BarPromenade.PlayModeTests` completed with
  0 errors and 0 warnings.
- After the breathing-timing follow-up, the Runtime, EditModeTests and
  PlayModeTests .NET builds repeated with 0 errors and 0 warnings; the focused
  animated-interaction timeline passed 16/16 and the real DX12 bed-sleep
  scenario passed 1/1.
- Two complete DX12 PlayMode runs each passed 96/97. Their only failure was the
  existing batch-only synthetic `Keyboard.dKey` assertion in the bed test;
  the same complete bed scenario passes 1/1 in isolation.
- Complete EditMode repeated at 502/506; exactly the same four pre-existing
  `CityLayoutGeneratorTests` remain red.
- The earlier feature Windows build succeeded at `145453839` bytes with
  0 warnings.
- Reviewed the complete atlas visually for the lie-down, breathing loop and
  dedicated wake-up sequence.
- Final `git diff --check` passed.

## 2026-07-30 — Approved Home framing and visible practicals

- Finalized the runtime-composed interior as an impoverished, cluttered old
  alcoholic's flat with six main-room furniture groups and three bathroom
  fixtures. Dedicated blocking camera-corner junk now makes the authored
  bed-side camera pocket physically unreachable.
- Locked the user-approved main-room shot to
  `(-4.48, 3.00, -3.25)`, Euler `(28°, 55°, 0°)`, `64°` FOV and the
  bathroom shot to `(1.82, 2.20, 0.86)`, Euler `(30°, 38°, 0°)`, `92°` FOV.
- Made both practical sources explicit in the image: the warm hanging bulb and
  cold bathroom tube use visible HDR emitters and depth-tested halos physically
  aligned with their two shadowless `Light` components.
- Reoriented the toilet with its cistern against the right wall and its bowl
  facing into the bathroom.
- Added an opt-in fixed-camera plane mode to `BillboardSprite`. Home enables
  exact `-camera.forward`/`camera.up` alignment so the `64 x 96` hero does not
  compress in either steep shot, and resets the mode when fixed control ends.
- Extended the focused regressions to cover exact camera poses, the blocking
  corner footprint, practical emitter/light/halo alignment and visibility,
  toilet orientation and projected sprite aspect. The corner junk was widened
  after the final nearest-reachable-point review so the complete player frame
  stays on screen without changing the approved camera pose.

Verification:

- `BarPromenade.Runtime`, `BarPromenade.Editor` and
  `BarPromenade.EditModeTests` .NET builds completed with 0 errors and
  0 warnings; Unity compiled the PlayMode assembly during the test runs.
- Focused `PlayerHomeLayoutTests` passed 21/21 and focused Home PlayMode
  coverage passed 8/8, including the camera-plane aspect, full-frame visibility
  at the nearest reachable main-room point and corrected toilet orientation.
- Complete PlayMode passed 96/96.
- Complete EditMode repeated at 481/485; the same four pre-existing
  `CityLayoutGeneratorTests` remain red, while all 21 Home layout tests pass.
- Reviewed the final `960 x 540` main-room and bathroom renders; both visible
  practical sources read in-frame and the bathroom character is neither
  compressed nor cropped.
- Windows build succeeded at `142280031` bytes with one package-owned URP
  `DebugOccluder.shader` vector-truncation warning and no project-code warning.
- `git diff --check` passed.

## 2026-07-29 — Neglected home, bathroom and fixed cameras

- Expanded the deterministic `10 x 8 x 3.4 m` home plan with explicit
  main-room/bathroom zones, protected entry/main/bathroom-access paths and
  validated toilet, shower and sink footprints.
- Rebuilt the runtime interior as a dim impoverished bachelor flat with worn
  furniture, stained and peeling surfaces, a boarded dead window, bottles,
  cans, ashtray, dirty dishes, clothes, papers, an old radio and restrained
  personal remnants. Small narrative clutter is collider-free so it does not
  invalidate the authored circulation.
- Added a complete tiled bathroom with an ajar opening, toilet, shower tray
  and curtain, pedestal sink, cracked mirror, exposed rusty pipes, leak damage
  and a floor drain.
- Added a bounded Home-only atmosphere: a weaker hard-shadow
  directional/ambient base, two shadowless dirty-yellow/cold practical lights,
  a cleaned-up runtime Bloom/color/exposure/vignette/grain profile and at most
  12 shared-material dust motes.
- Split Home ambience from the bar loop with deterministic refrigerator,
  mains, pipe and drip layers.
- Added two authored fixed camera poses for the main room and bathroom. The
  controller applies immediate hard cuts, keeps each shot through a wider
  hold area for doorway hysteresis, ignores orbit/follow movement and retains
  reduced intoxication, balance and fall rotation around the fixed position.
- Refreshes the player's billboard plane in the same hard cut, preventing the
  nine-layer sprite from briefly rendering edge-on to a newly active shot.
- Blocks the otherwise reachable pocket beneath the main camera with the sofa,
  falls back to the main shot for all legitimate non-bathroom floor, keeps the
  ajar bathroom door solid with capsule-width clearance and aligns narrative
  clutter to the actual furniture surfaces.
- Added focused EditMode/PlayMode coverage for bathroom layout and scene
  construction, Home ambience/atmosphere bounds and cleanup, fixed-pose
  behavior, shot hysteresis and the existing home round trip.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds: 0 errors,
  0 warnings.
- Focused home-layout and retro-SFX EditMode checks: 20/20 and 28/28 passed.
- Focused Home fixed-camera, atmosphere, presentation and round-trip PlayMode
  checks: 9/9 passed; the shared billboard check passed 1/1.
- Reviewed final `960 x 540` renders from both authored shots and the nearest
  reachable point to the main anchor. This caught and drove corrections to
  the initial underexposure, bathroom angle/door occlusion, stale
  player-sprite plane and the original camera blind pocket.
- Windows build succeeded at `142277983` bytes with 0 warnings.
- Complete EditMode repeated at 480/484: four unchanged
  `CityLayoutGeneratorTests` fail on the pre-existing bar-distance contract
  (the isolated class repeats 17/21).
- Complete PlayMode passed 96/96 after the Home presentation fixture was made
  to unload its Single-mode scene during teardown; the previously affected
  legacy intoxication/debug-window/motor classes also pass 16/16 in isolation.
- `git diff --check` passed.

## 2026-07-29 — Player home MVP

- Added one deterministic non-bar player home beside a generated bar street,
  validated within `48 m` of a bar by traversable route distance, and moved the
  fresh-session spawn to its frontage node.
- Gave the exterior a distinct teal/cool-lit facade, porch, mailbox, chimney,
  walkable approach, fence opening, localized interaction and labeled house
  marker on the full-screen map.
- Added the separate runtime-composed `HomeInterior` scene with a validated
  `10 x 8 x 3.4 m` layout, five furniture groups, warm practical lighting,
  quiet ambience, shared player/intoxication systems and one exit.
- Extended the shared door transition and session contract with explicit
  bar/home return kinds, preserving the city seed, route, visits, cash and
  drinking progress while returning to the matching exterior entrance.
- Updated editor scene setup, build settings, diagnostics, localization,
  EditMode coverage and the full `City -> HomeInterior -> City` PlayMode smoke
  path.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds: 0 errors, 0 warnings.
- RU/EN localization catalogs parse successfully.
- Unity EditMode and PlayMode suites: pending while the project is open in an
  interactive Unity Editor instance.
- `git diff --check` passed.

## 2026-07-29 — Bar-adjacent fresh city spawn

- Replaced the fresh session's central-city spawn with a deterministic node on
  one generated bar's frontage road, placing the player `12 m` from that bar
  under default spacing.
- Preserved the existing `SpawnNode`/`SpawnWorldPosition` contract, full
  player-radius walkability and the central-node fallback for custom layouts
  with no bars.
- Kept the separate return path unchanged, so leaving an interior still
  restores the active bar's exact return position.
- Added seed-varied pure generation coverage and a real City bootstrap
  assertion for spawn position, road travel distance and controller clearance.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused city-layout EditMode checks: 21/21 passed.
- Focused generated-City PlayMode bootstrap: 1/1 passed.
- Complete Unity EditMode suite: 460/460 passed.
- Complete headless Unity PlayMode suite: all 84 runnable checks passed; the
  three graphics-only checks were skipped as expected.
- `git diff --check` passed.

## 2026-07-29 — Bar drink purchases and session wallet

- Added a session-only integer cash wallet starting at `$999`, a fixed
  nine-item retail catalog and pure purchase results that reject unsupported,
  unaffordable or maximum-intoxication alcohol without mutating state.
- Added one atomic `GameSessionState` purchase boundary: successful orders
  deduct cash and immediately commit drinking progress; water costs `$2`,
  counts as consumed, does not sober the player and preserves the last
  alcoholic drink.
- Added a localized retro shop modal and a separate counter interaction point
  in all four bar variants. The data-first layout reserves its trigger, removes
  one nearby stool for access and validates it against furniture, the activity
  station and exit.
- Integrated shop ownership with the shared modal lock and F9 window without
  adding the shop to the minigame catalog or bar-visit completion flow.
- Added cash and purchase outcomes to structured diagnostics and updated the
  current architecture, system maps and player-facing notes.

Verification:

- Complete generated .NET solution build: 0 errors, 0 warnings.
- Focused economy/layout/localization EditMode checks: 50/50 passed.
- Focused shop/F9/four-bar scene PlayMode checks: 9/9 passed.
- Complete Unity EditMode suite: 456/456 passed.
- Complete headless PlayMode suite ran twice: all changed-path checks and all
  non-motor checks passed, with three expected graphics-only skips. The first
  run passed 83/84 runnable checks and the second 82/84; the unchanged
  `PlayerMotorHeadingPlayModeTests` release-timing assertion remained flaky.
  Its isolated failing method passed 1/1; the complete motor class passed 5/6.
- `git diff --check` passed.

## 2026-07-29 — Opaque player hands

- Compared all eight runtime directions against the locked player turntable
  and restored 439 lower-arm skin/bandage pixels that the original chroma-key
  pass had made transparent.
- Extended the deterministic atlas builder with a lower-arm capsule repair
  constrained to skin-colored source pixels, then rebuilt the reference and
  nine-part/body-expression atlases without changing facial artwork.
- Added EditMode regression samples covering the repaired lower arms in every
  direction and verifying that the runtime lower-arm layers retain the exact
  reference colors.

Verification:

- Deterministic atlas builder migration: 439 lower-arm repairs; immediate
  rerun: 0 repairs with stable output.
- Runtime/EditModeTests .NET build: 0 errors, 0 warnings.
- Focused player-atlas EditMode checks: 9/9 passed.
- Focused player-rig PlayMode checks: 15/15 passed.
- Visual comparison of all eight corrected frames against the locked source:
  hands and bandage are opaque without magenta-background spill.

## 2026-07-29 — Bar dust particle error cleanup

- Configured all three BarInterior dust velocity axes as
  `TwoConstants` before enabling Velocity over Lifetime. The zero Z range now
  matches the randomized X/Y modes instead of leaving Unity's default
  `Constant` curve and emitting a native validation error continuously.
- Added a focused PlayMode regression that runs the real dust system for a
  frame, checks the three curve modes and rejects unexpected Unity logs.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused dust-velocity PlayMode check: 1/1 passed.
- BarInterior scene bootstrap PlayMode check: 1/1 passed.
- Both Unity logs contained zero instances of
  `Particle Velocity curves must all be in the same mode`.

## 2026-07-29 — Lower third-person camera framing

- Raised the exterior/interior chase-camera focus heights from `1.1 m / 1.05 m`
  to `1.4 m / 1.3 m`, placing the player below frame center without changing
  distance, FOV, orbit damping or collision handling.
- Extended both camera-profile PlayMode regressions with explicit viewport
  composition bounds and updated nearby focus, collision and teleport checks.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused camera-framing PlayMode check: 1/1 passed.
- Full player/camera presentation PlayMode checks: 15/15 passed.
- `git diff --check` passed.

## 2026-07-29 — Physically raised city walking surfaces

- Kept the authored height differences and added static mesh colliders to the
  same chunked street and park-path geometry used for rendering.
- Made the park lawn a box-collider surface and the central octagonal plaza a
  shared-mesh collider surface. The existing `0.28 m` controller step now
  climbs the `0.04 m` lawn-to-path rise instead of passing through the path.
- Reverted the provisional whole-puppet pixel lift; the correction now belongs
  to world collision, preserving the grounded atlas and shadow contracts.
- Added factory coverage for optional collidable combined meshes and a City
  PlayMode regression that settles on the lawn, crosses the path edge and
  verifies the controller rises to its physical top.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Focused primitive-factory EditMode checks: 3/3 passed.
- City presentation and walkable-surface PlayMode checks: 3/3 passed.
- Nearby city park/marker presentation check: 1/1 passed.
- `git diff --check` passed.

## 2026-07-29 — Structured session diagnostics

- Added a fail-safe NDJSON session logger with stable envelopes, typed fields,
  build/session/scene/seed context and basic/verbose/off runtime profiles.
- Instrumented session mutations, city and bar initialization, scene
  transitions, interactions, map lifecycle, all four minigames, intoxication
  stages and balance challenges at state-changing boundaries.
- Added correlation IDs for transitions, minigame runs, balance sequences and
  manual snapshots. `F8` captures and flushes a support snapshot; `Shift+F8`
  opens the active log directory.
- Added 5 MiB copy-and-truncate rotation with three retained archives, bounded
  field strings, exact-repeat suppression and separate bounded Unity warning
  and error budgets with dropped-count summaries.
- Documented locations, profiles, schema, event categories, retention, support
  workflow and privacy considerations in `ai/debug-log.md`.

Verification:

- Runtime, EditModeTests, PlayModeTests and complete Assembly-CSharp .NET
  builds: 0 errors, 0 warnings.
- Complete Unity EditMode suite: 423/423 passed.
- Complete headless Unity PlayMode suite: all 77 runnable checks passed;
  three graphics-only checks were skipped by the null graphics device.
- Verbose City → BarInterior → City integration: 1/1 passed. All 79 physical
  log lines parsed as schema-v1 JSON, used one session, had contiguous
  sequence numbers and correctly correlated both completed transitions.
- `git diff --check` passed.

## 2026-07-29 — District-scale city and traversable central park

- Expanded the deterministic default layout from `4 x 4` to `12 x 12` blocks
  and added mandatory cross-city arterials.
- Added four distinct urban districts plus a `4 x 4` central park with a lawn,
  plaza, trees, benches, hedges, crossing paths and four connected gates.
- Replaced shuffled bar placement with deterministic max-min selection over
  weighted graph travel. The four default bars occupy distinct districts and
  every pair is at least `120 m` apart along traversable paths.
- Updated the city map with localized district labels, district land colors
  and separate park-path presentation.
- Kept the larger world bounded by batching roads, fence rails/posts and lamp
  fixtures/bulbs into `48 m` spatial chunks. Added a BVH-style index for
  walkable rectangles and a binary min-heap for route finding.
- Extended pure and scene-level coverage for districts, park topology,
  continuous gate traversal, bar travel distance, map presentation, fence
  openings/chunks, walkability indexing and lamp batching.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Complete Unity EditMode suite: 388/388 passed.
- Complete headless Unity PlayMode suite: all 77 runnable checks passed;
  three graphics-only render tests were skipped by the null graphics device.
- Focused expanded-city scene checks: 3/3 passed, including all four park
  gates and spatially chunked fence geometry.
- `git diff --check` passed.

## 2026-07-28 — Cinematic expanded bar interior

- Rebuilt the runtime-composed bar around a deterministic `22 x 16 x 4.8 m`
  layout with seven named zones, four protected circulation paths and validated
  fixture clearances.
- Added a longer counter and backbar, bottle shelves and mirror, three booths,
  four social tables, a curtained stage, activity bay, entrance dressing,
  posters, ceiling beams, fan, practical fixtures and a dedicated service door.
- Populated the room with 12 lightweight NPCs: bartender, performer, booth
  groups, standing patrons and a walker. Six shared PS1/noir sprites, centralized
  low-frequency decisions and depth-aware billboard sorting keep the crowd
  readable without adding colliders or per-NPC materials.
- Added a six-light shadowless practical-light budget, warm URP grading, bloom,
  vignette, film grain, local dust and a skippable `1.35 s` arrival reveal.
- Added a two-source spatial soundscape for the crowd bed and rare bar-service
  cues while preserving the existing music and ambience source budget.
- Kept Beer Pong, Split G and Tincture variants functional, with exactly one
  activity station and one exit. Fixed modal ownership so interacting during
  the arrival reveal cannot restore stale input state.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Complete Unity EditMode suite: 365/365 passed.
- Complete Unity PlayMode suite: 80/80 passed, 0 skipped.
- D3D captures inspected the arrival, expanded room, crowd, stage, backbar and
  activity dressing; a 32-sample physics check covers the full camera reveal.
- Windows Player build: succeeded, `142098662` bytes, 0 warnings.

## 2026-07-28 — Five-stage intoxication and balance

- Replaced the independent timed intoxication status with a single persistent
  `0–100` value evaluated through five named 20-point ranges. Added continuous
  speed, jointed-puppet, chase-camera and PS1 world-composite parameters plus a
  crisp five-segment localized HUD.
- Added deterministic balance scheduling above `60`, a fixed-step inertial
  arrow model, the overhead arc/safe-sector/risk presentation and a
  balance-specific modal lock for arrows, A/D, D-pad and left stick.
- Added visual fall/down/rise recovery, camera response and a fall-aware contact
  shadow while keeping the physical player root upright and stationary.
- Preserved the maximum-intoxication terminal behavior of all four minigames
  without applying a separate expiring status after completion or cancellation.
- Extended the shared F9 window with clickable and Left/Right-arrow `-20/+20`
  controls that clamp session intoxication while preserving drink context.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Complete Unity EditMode suite: 332/332 passed.
- Complete headless Unity PlayMode suite: all 71 runnable checks passed;
  three RenderGraph/realtime-shadow pixel checks were ignored because the
  null graphics device cannot execute them.
- Focused intoxication status cycle: 3/3 passed, including threshold cancel
  and failure/fall/recovery/cooldown.
- Source inspection found no remaining timed-status identifier or localized
  key under `Assets`; `git diff --check` passed.

## 2026-07-28 — Contextual intoxication HUD

- Kept the intoxication HUD completely hidden at `0` while preserving its
  existing modal visibility state.
- Made the panel appear immediately for any positive intoxication value and
  disappear again if drinking progress resets to zero.
- Added a focused state-transition regression check.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Focused intoxication-HUD EditMode check: 1/1 passed.

## 2026-07-28 — Reduced player maximum speed

- Halved the player motor's maximum planar speed from `5.2 m/s` to
  `2.6 m/s`.
- Kept the existing `6.5 m/s²` acceleration, `11 m/s²` braking, camera
  response and procedural gait unchanged. No sprint state or run animation
  was added.
- Updated the inertial movement checks for the new cruising speed, stopping
  distance and direction-reversal timing.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused player-motor PlayMode suite: 6/6 passed.

## 2026-07-28 — Classic fixed-camera door transition

- Added `DoorTransition` as a dedicated third build scene and taught the
  runtime bootstrap and editor scene setup to install its matching root.
- Routed both bar entry and exit through one guarded
  `source -> DoorTransition -> destination` chain. The destination preloads
  while activation remains blocked until the complete `3.15 s` sequence
  returns to black.
- Added a deterministic unscaled timeline for reveal, handle turn, low-poly
  door opening, camera push and final fade. The doorway remains a solid black
  sprite while the leaf swings outward toward the player instead of receding
  into the opening. Entering uses warm door lighting; exiting uses a colder
  gray-green treatment.
- Split the existing door sound into the short latch cue plus a generated
  sustained hinge creak, played from the animated door.
- Extended the scene-flow contract to observe both transition directions and
  reject a second request while the first chain owns the transition guard.

Verification:

- Runtime, Editor, EditModeTests, PlayModeTests and Assembly-CSharp .NET
  builds: 0 errors, 0 warnings.
- Complete Unity EditMode suite: 310/310 passed.
- Focused door-scene and full round-trip PlayMode checks: 2/2 passed; the
  deterministic door timeline passed 4/4 EditMode checks.
- Complete headless Unity PlayMode suite: 65 passed, 0 failed and 3
  graphics-only tests skipped. All three skipped GPU tests passed in the D3D
  run; its only failure was the pre-existing real-time-sensitive opposite
  input motor check, which passed on a focused retry.
- D3D visual capture inspected the open outward pose and confirmed that the
  black doorway sprite fully covers the aperture beneath the frame.
- Windows Player build: succeeded, `135687344` bytes, 0 warnings.

## 2026-07-28 — Restricted city fog visibility

- Increased the City-only exponential-squared fog density from `0.048` to
  `0.070` and limited the exterior camera far plane from `220 m` to `48 m`.
  The fog reaches near-total opacity before the clip plane.
- Matched the solid City camera background exactly to the terminal fog color.
  Empty pixels beyond finite or clipped geometry now continue the gray-green
  haze instead of exposing the former dark sky as a world boundary.
- Raised the existing local fog gradient from `0.060 / 0.082` to
  `0.080 / 0.120` alpha without increasing its 36-particle budget, emission
  rate or footprint.
- Kept `BarInterior` fog-free and made its return to the default `220 m`
  camera range an explicit regression contract.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Focused City/BarInterior atmosphere PlayMode: 2/2 passed.
- The initial D3D11 density capture kept the player and current junction
  readable, while a follow-up live `1920x1080` Game-view check exposed the
  darker clear color between distant buildings and drove the terminal
  backdrop correction.
- Complete Unity EditMode suite: 304/304 passed.
- Complete headless Unity PlayMode retry: 64 passed, 0 failed and 3
  graphics-device checks skipped as expected; one pre-existing inertial motor
  test was transient on the first run, then passed both isolated and in the
  complete retry.

## 2026-07-28 — Opaque diagonal player heads

- Compared the four diagonal atlas frames with the locked turntable and
  restored exactly 51 genuine subject pixels left transparent by the original
  chroma-key pass: 12 `FrontRight`, 13 `BackRight`, 14 `BackLeft` and 12
  `FrontLeft`.
- Rebuilt the reference, nine-part and five-expression atlases
  deterministically. All 51 reference changes are binary `0 -> 255` alpha;
  the body layer receives the same pixels and every expression row preserves
  them.
- Split heuristic face-scan and explicit edge-repair validation in the atlas
  builder, retaining both migration from the previous atlas and idempotent
  zero-change rebuilds.

Verification:

- EditModeTests .NET build: 0 errors, 0 warnings.
- Deterministic builder rerun: 0 repairs; output SHA-256 remained stable.
- Legacy-atlas migration reproduced the committed expression-atlas SHA-256.
- Visual check of all four diagonal frames against a contrasting background:
  no internal head/neck transparency remains.
- Turned-head alpha regression: 1/1 passed; complete player asset suite: 8/8
  passed.
- Complete Unity EditMode suite: 304/304 passed.
- Facial runtime and full player presentation PlayMode: 1/1 and 15/15 passed.
- Complete headless Unity PlayMode suite: 64 passed, 0 failed and 3
  graphics-device checks skipped as expected.
- D3D graphics shadow render checks: 2/2 passed.
- Windows x64 Player build: succeeded, 0 warnings, 135,673,543 bytes.

## 2026-07-28 — Articulated directional player shadow

- Replaced the single static full-body shadow card with nine collider-free
  `ShadowsOnly` body and limb renderers.
- Kept the shadow view authored relative to the main light while remapping the
  live smoothed joint angles into that view. Walking, footfall compression,
  idle gestures and the then-current intoxication motion now reshape the
  projected silhouette.
- Added component lifecycle handling plus regressions for actor translation,
  opposite gait phases and the actual D3D receiver pixels.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Complete Unity EditMode suite: 303/303 passed.
- Complete headless Unity PlayMode suite: 64 passed, 0 failed and 3
  graphics-device checks skipped as expected.
- Player shadow behavior PlayMode: 3/3 passed.
- Full player presentation PlayMode: 15/15 passed.
- D3D graphics shadow render checks: 2/2 passed, including a pixel-level
  animated-silhouette comparison.
- Windows x64 Player build: succeeded, 0 warnings, 135,673,543 bytes.

## 2026-07-28 — Grounded player foot contact

- Lowered the runtime visual baseline from `0.04 m` to `0.005 m`, matching the
  atlas foot pivot instead of leaving a permanent visible gap above the
  controller ground origin.
- Added explicit left/right sole contacts derived from the eight directional
  atlas poses. Each frame grounds the lower support contact; the other foot
  remains free to swing and the support alternates across half-cycles.
- Replaced the always-positive `0.035 m` whole-puppet bob with a `0.012 m`
  upper-body impact compression and `0.005 m` sole compression. Idle
  breathing now offsets only the body and arm roots.
- Added one collider-free shared four-vertex contact quad with a dedicated
  transparent URP shader. It follows the player root, not `PoseRoot`, and
  remains available independently of the realtime directional shadow.
- Hardened the generated shadow lifecycle: disabling the contact component
  hides its renderer, while subsystem resets dispose and lazily rebuild shared
  generated materials and mesh resources.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Complete Unity EditMode suite: 303/303 passed.
- Complete headless Unity PlayMode suite: 63 passed, 0 failed and 3
  graphics-device checks skipped as expected.
- Deterministic grounded gait PlayMode: 1/1 passed.
- Player shadow behavior PlayMode: 2/2 passed.
- Full player presentation PlayMode: 15/15 passed.
- D3D graphics shadow render checks: 2/2 passed.
- City footfall/opposite-support visual capture: 1/1 passed.
- Windows x64 Player build: succeeded, 0 warnings, 135,672,519 bytes.

## 2026-07-28 — Heavy inertial locomotion

- Replaced one-frame planar speed changes with `6.5 m/s²` acceleration toward
  the existing `5.2 m/s` maximum and `11 m/s²` braking. A normal release now
  produces about `1.23 m` of controllable coasting from full speed.
- Fed actual `CharacterController` displacement back into the velocity state
  and removed its minimum movement threshold, preserving frame-independent
  low-speed acceleration without storing pressure at road edges or
  collisions.
- Kept input disable, modal ownership, scene transitions and teleport as hard
  planar stops. Direction reversal brakes old momentum before accelerating in
  the opposite direction.
- Replaced the fixed `2.2 cycles/s` gait with one cycle per `2.7 m` travelled,
  matched full animation amplitude to `5.2 m/s`, softened joint settling from
  `12` to `8` and increased body rock from `1.4°` to `1.8°`.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Focused inertial motor PlayMode: 6/6 passed.
- Full player presentation PlayMode: 15/15 passed.
- Facial-state locomotion regression: 1/1 passed.
- Complete City/BarInterior scene flow: 9/9 passed.
- D3D11 acceleration/braking visual sequence: 1/1 passed; sampled speeds were
  `0.00 → 2.17 → 4.12 → 5.20 → 3.21 → 0.00 m/s`.
- Windows x64 Player build: succeeded, 0 warnings, 135,663,527 bytes.

## 2026-07-28 — Much closer, weightier chase camera

- Reduced the centered exterior camera arm from `3.6 m` to `2.6 m` and the
  interior arm from `2.7 m` to `2.2 m`, preserving the existing `53° / 57°`
  FOV pair and complete full-body composition in both runtime scenes.
- Increased orbit yaw, target focus, outward obstacle recovery, cinematic
  blend and movement-response damping for a heavier, smoother feel. Focus lag
  remains capped at `0.45 m`.
- Preserved immediate inward obstacle avoidance, teleport snapping, stable
  requested yaw/FOV and camera-independent player heading.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused Unity PlayMode player/camera presentation: 14/14 passed.
- D3D11 City/BarInterior framing capture: 1/1 passed; the hero remains fully
  visible in both review frames.
- Windows x64 Player build: succeeded, 0 warnings, 135,663,015 bytes.

## 2026-07-28 — Camera-independent dynamic player shadow

- Added one collider-free `ShadowsOnly` sprite proxy to every runtime player
  in City and BarInterior. It reuses the existing eight-direction full-body
  atlas, selects a silhouette from the player/main-light angle and faces the
  main directional light instead of the orbiting camera.
- Copied whole-puppet bob, weight shift and the then-current intoxication sway
  to the shadow proxy while leaving the nine visible jointed renderers and
  their materials unchanged.
- Added one cached shared runtime material backed by an alpha-clipped URP
  `ShadowCaster` pass. Practical street/bar lights remain shadowless, and the
  caster adds no collider, texture atlas or per-frame allocation.
- Added resource, behavior, scene-integration and graphics-device render
  checks. The render check compares the same receiver with the caster enabled
  and disabled to verify actual shadow-map darkening.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused Unity EditMode: 1/1 passed.
- Focused Unity PlayMode behavior: 1/1 passed.
- City/BarInterior PlayMode integration: 2/2 passed.
- D3D11 realtime shadow render check: 1/1 passed.
- Windows x64 Player build: succeeded, 0 warnings, 135,663,015 bytes.

## 2026-07-28 — Tinctures in a Row minigame

- Added a seeded `7x7` match-three domain with five normal infusion flavors,
  exactly one starting `XXX`, no initial matches, at least three legal normal
  swaps and 15 accepted moves. Invalid swaps preserve the board, score and
  move count.
- Added unique-cell match resolution, gravity, seeded refill, deterministic
  cascades with a multiplier capped at `x5`, long-run/intersection special
  creation and deterministic dead-board reshuffling while enforcing at most
  one `XXX`.
- Added the modal fourth-bar controller with mouse click/drag,
  keyboard and gamepad input. Normal matches represent customer orders; only
  activating `XXX` immediately commits one `Moonshine`, +24 intoxication and
  one consumed drink. Cancel cannot refund it, and F9 runs stay isolated.
- Registered `tincture-match` in the shared catalog and assigned the fourth
  stable row-major bar through the common resolver. Added the dedicated
  station, tray/shot/`XXX` decor, RU/EN UI, deterministic point-filtered
  `640x360` backdrop and 4x4 atlas, plus generated swap, match and
  moonshine-burst SFX.
- Added eased swap, gravity and refill motion, clipped board entry, cascade
  particles and reshuffle feedback. Match audio now starts with the matching
  clear animation.
- Terminal moves remain completed if the player closes during their cascade;
  the then-current 45-second intoxication effect started only when the modal
  closed. Reopening advances a deterministic per-controller board sequence.

Verification:

- Focused Unity EditMode integration suite: 70/70 passed, with 0 compile
  errors or warnings.
- Runtime, Editor, EditModeTests, PlayModeTests and Assembly-CSharp .NET
  builds: 0 errors, 0 warnings.
- Unity EditMode: 302/302 passed.
- Unity PlayMode in `-nographics`: 54/54 runnable tests passed; the existing
  graphics-device-only RenderGraph test was ignored by design.
- Windows x64 Player build: succeeded, 0 warnings, 135,654,019 bytes.

## 2026-07-28 — Split the G minigame

- Added a pure frame-rate-independent one-sip session with Normal timing,
  irreversible release, foam settling, five accuracy bands, linear 0–100
  scoring, three fresh glasses and a session-best result.
- Assigned the third stable row-major bar to Split the G through one shared
  ordinal resolver used by generation and validation. Registered the activity
  in the common catalog, so normal interiors and isolated F9 launches use the
  same factory.
- Added Space, in-canvas LMB and gamepad South hold/release input with
  post-countdown fresh-press protection. The exact level stays obscured while
  drinking and settling; each non-empty sip immediately persists its actual
  dark-beer fraction and Cancel cannot refund it.
- Added the activity-specific interior station and pint display, RU/EN UI,
  deterministic `640x360` pixel-art backdrop, transparent 4x4 gameplay atlas,
  generated gulp SFX and focused domain/presentation/input/scene-flow tests.

Verification:

- Runtime, Editor, EditModeTests, PlayModeTests and Assembly-CSharp .NET
  builds: 0 errors, 0 warnings.
- Unity EditMode: 261/261 passed.
- Unity PlayMode in `-nographics`: 43/43 runnable tests passed; the existing
  graphics-device-only RenderGraph test was ignored by design.
- Windows x64 Player build: succeeded, 0 warnings, 133,861,007 bytes.

## 2026-07-28 — Expressive living idle and facial states

- Increased the readable breathing, weight-transfer and body-rock amplitudes
  at the closer camera distance. The short cuff/strap gesture now alternates
  deterministically between the left and right arms while keeping the
  direction-projected sagittal plane and depth sorting.
- Expanded the body-expression atlas from three to five rows: stronger
  half/closed blinks plus `Watchful` and `Tense`. The two new expressions use
  explicit direction-specific eye, brow and mouth edits, run only after
  sustained idle and reset during locomotion or the then-current strong
  intoxication state; blink timing continues in either state.
- Kept the nine-renderer hierarchy, authored asymmetry and non-mirrored views.
  All three rear directions remain byte-identical to neutral.
- Extended regression coverage for exact atlas output, pairwise-distinct
  facial states, blink-under-motion, strong-intoxication suppression,
  sagittal depth sorting and the left-then-right idle gesture sequence.

Verification:

- Deterministic expression atlas rebuild:
  SHA256 `6FDFB6744B9F74F0EFE67BC30C528B8C654ABEC3444EA815FA5F94DD034A7688`.
- Unity EditMode: 222/222 passed.
- Unity PlayMode in `-nographics`: 35/35 runnable tests passed; the existing
  graphics-device-only RenderGraph test was ignored by design.
- Runtime, EditModeTests, PlayModeTests and Assembly-CSharp .NET builds:
  0 errors, 0 warnings.
- Windows x64 Player build: succeeded, 0 warnings, 132,090,607 bytes.

## 2026-07-28 — Tighter centered chase framing

- Reduced the centered exterior camera arm from `4.6 m` to `3.6 m` and the
  interior arm from `3.3 m` to `2.7 m`.
- Kept the existing FOV, focus heights, orbit, damping, cinematic motion and
  obstacle handling unchanged; no shoulder offset was introduced.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused Unity PlayMode player/camera presentation: 13/13 passed.

## 2026-07-28 — Cinematic camera, living idle and facial blink

- Tightened exterior/interior chase framing to `4.6 m / 53°` and
  `3.3 m / 57°`, added bounded focus damping with teleport snapping, and
  layered deterministic low-frequency idle drift with speed-driven walk bob.
- Preserved immediate inward collision response, smooth outward recovery,
  player-independent orbit yaw and stable FOV. The shared modal lock now fades
  cinematic motion out and restores its captured state.
- Added procedural breathing, weight transfer and a rare left-arm fidget to
  the existing direction-projected joint pose. Idle blends away during
  locomotion and was suppressed under the then-current strong intoxication
  state without changing scale, heading or the nine-renderer hierarchy.
- Extended the deterministic player builder with a `512x288` body-expression
  atlas. Neutral matches `Body`; half/closed blinks edit only explicit eye
  pixels in five visible-face directions, while three rear views remain
  unchanged. Runtime swaps the existing body sprite on a deterministic timer.

Verification:

- Unity EditMode: 218/218 passed.
- Unity PlayMode in `-nographics`: 35/35 runnable tests passed; the existing
  graphics-device-only RenderGraph test was ignored by design.
- Runtime, EditModeTests, PlayModeTests and Assembly-CSharp .NET builds:
  0 errors, 0 warnings.
- Windows x64 Player build: succeeded, 0 warnings, 131,695,855 bytes.

## 2026-07-28 — View-correct projected player gait

- Replaced the single screen-plane joint axis with a view-projected sagittal
  axis: side views retain their lateral swing, front/back views rotate in
  depth, and diagonal views combine both components.
- Made the gait phases explicit: left/right legs oppose one another and each
  arm opposes the leg on the same side.
- Added phase-aware near/far sorting so depth-projected limbs pass behind or
  in front of the torso without changing sprites or mirroring.
- Moved final pose application after direction selection in `LateUpdate` and
  retained walk bob/rock, idle settling and the then-current intoxication sway.
- Added PlayMode regression coverage for cardinal/diagonal projection,
  contralateral phasing and depth sorting.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused Unity PlayMode player presentation: 9/9 passed.
- Full Unity PlayMode with D3D11: 31/31 passed, including complete
  `City`/`BarInterior` scene flow.

## 2026-07-28 — Jointed eight-direction player correction

- Kept the locked eight-view character design and restored exactly 259 facial
  pixels that the original chroma-key pass had incorrectly made transparent;
  every pre-existing atlas pixel outside that repair mask remains unchanged.
- Derived a `512x864` puppet atlas from the corrected reference: body plus
  upper/lower layers for both arms and legs in all eight directions. The
  deterministic builder verifies that the nine neutral layers composite
  pixel-for-pixel to the reference frame.
- Replaced the temporary single-renderer presentation with nine visual-only
  `SpriteRenderer` components in four parent/child joint chains. Walking now
  rotates shoulders, elbows, hips and knees, while bob/rock and the
  then-current intoxication sway affect the complete puppet.
- Preserved camera-independent actor heading, 5-degree view hysteresis,
  non-mirrored asymmetry, PPU 48 and the shared four-pixel foot pivot.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 213/213 passed.
- Unity PlayMode with D3D11: 29/29 passed, including all nine puppet layers,
  joint animation, eight camera sectors and the complete
  `City`/`BarInterior` scene flow.
- Windows x64 Player build: succeeded, 0 warnings, 131,098,367 bytes.

## 2026-07-28 — Eight-direction player prototype

- Replaced the procedural 13-part player rig with one point-filtered
  `SpriteRenderer` backed by eight explicit `64x96` atlas views at PPU 48.
- Locked a grim burgundy/navy character design with persistent left-arm
  bandage, right-shoulder patch and diagonal strap details; every view has the
  same scale and 4-pixel foot pivot and is used without mirroring.
- Added a pure eight-sector selector with 5-degree hysteresis, full wraparound
  and explicit front/side/back ordering.
- Removed both camera-to-player yaw writes. The motor now faces actual planar
  movement, preserves heading while idle and remains camera-relative.
- Preserved prototype walking bob/rock and the then-current intoxication sway
  through the single-renderer hierarchy in both runtime scenes.
- Updated player/camera tests, import checks, architecture maps, player art
  specification and release notes. Full multi-frame idle/walk animation remains
  the next art pass after approval of the static vertical prototype.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 211/211 passed.
- Unity PlayMode with D3D11: 29/29 passed, including all eight view centers,
  camera-independent heading, input movement, prototype intoxication motion,
  obstacle avoidance and the complete `City`/`BarInterior` flow.
- Manual atlas contrast check: all eight silhouettes and the bandage/patch/strap
  asymmetry remained readable against gray-green fog and warm bar tones.
- Windows x64 Player build: succeeded, 0 warnings, 129,322,495 bytes.

## 2026-07-28 — Entrance-aware road-edge fences

- Added a deterministic pure-data fence plan over the exposed perimeter of
  the complete road-rectangle union. It closes outer edges and dead ends
  without placing barriers inside turns, T-junctions or intersections.
- Added a `3.30 m` opening on the facing road side of every bar, driven from
  the same frontage and shared walkway geometry used by the generated facade.
- Added low ochre two-rail visuals with dark inset posts. They remain
  collider-free so the road/apron mask is authoritative and are combined into
  two generated meshes instead of hundreds of individual renderers.
- Added deterministic, sparse-topology, perimeter-completeness, opening,
  batching, collider and entrance-walkability regression coverage.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 189/189 passed.
- Unity PlayMode with graphics: 25/25 passed, including the generated fence
  hierarchy and every bar's clear walkable approach.
- Windows x64 Player build: succeeded, 0 warnings, 129,125,439 bytes.

## 2026-07-28 — F9 minigame debug launcher

- Added one localized retro debug window to `City` and `BarInterior`; `F9`
  opens it and launches the registered cocktail or beer-pong activity directly.
- Added one explicit `BarMinigameCatalog` for normal and debug construction.
  A future game appears after registering its unique definition and factory.
- Opening the window closes a conflicting map or minigame before taking the
  state-preserving modal lock, and closing restores the captured player,
  camera and HUD state.
- Debug instances use fresh transient drinking state, do not mark a bar
  visited and do not persist intoxication, drinks or the then-current
  temporary status.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 181/181 passed.
- Unity PlayMode: 24/24 passed, including a real Input System F9 press,
  both isolated debug launches and the complete scene flow.
- Windows x64 Player build: succeeded, 0 warnings, 129,113,951 bytes.

## 2026-07-28 — Second-bar beer pong

- Added stable per-bar activities: the second row-major city bar opens beer
  pong while the other two retain the cocktail mixer.
- Replaced the counter-only coupling with one `IBarMinigame` contract, shared
  modal lock and activity station; the generated interior now builds only the
  matching game and furniture.
- Added a deterministic six-cup, ten-throw session and fixed `120 Hz` 2.5D
  simulation with swept table/cup-mouth contacts, table/rim bounces, settlement,
  timeout and out-of-bounds results.
- Added clean/bank/early-clear scoring. Every miss consumes a light beer,
  adds 8 intoxication and commits the drinking state immediately.
- Added a point-filtered 640x360 beer-pong backdrop, 4x4 ball/hand/cup/effect
  atlas, projected flight/shadow/cup feedback, discrete aiming/power UI and
  dedicated generated throw, bounce, rim and sink SFX.
- Completion marks the captured bar ID visited and removes it from the route;
  cancellation restores prior input/HUD state without completing the visit.

Verification:

- Complete .NET solution build: 0 errors, 0 warnings.
- Unity EditMode: 177/177 passed.
- Unity PlayMode: 21/21 passed.
- Windows x64 Player build: succeeded, 0 warnings, 129,102,372 bytes.

## 2026-07-28 — Completed-bar map progress

- Added a current-city visited-bar set to `GameSessionState`, preserved across
  scene transitions and reset together with the route when the seed changes.
- Moved visit completion from bar-interior loading to the cocktail
  minigame's accepted final result. Entering, cancelling or leaving early no
  longer removes the bar from the route.
- Added persistent green numbered markers and a visited counter to the city
  map; amber corner badges now carry route order independently.
- Added regression coverage for idempotent visit persistence, final-result
  completion, cancellation and an unfinished bar round trip.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 141/141 passed.
- Unity PlayMode: 20/20 passed.

## 2026-07-28 — PS1 tonal response correction

- Moved RGB555 quantization from linear-light values to perceptual sRGB space,
  then blended it at 35% instead of replacing the source color.
- Preserved the original HDR delta and restored continuous dark fog, shadow
  detail and lamp falloff while keeping the low-resolution pixel structure.
- Strengthened the GPU test with a one-pixel checker plus exact dark and bright
  tone fields, isolating the test camera from scene fog and post-processing.

Verification:

- Unity EditMode: 141/141 passed.
- Unity PlayMode: 20/20 passed.
- Windows x64 Player build: succeeded, 0 warnings, 124,166,277 bytes.

## 2026-07-28 — PS1 readability and map projection correction

- Raised the default internal world frame from `320x180` to `640x360`, removed
  the visible screen-space Bayer grid and added four-tap footprint averaging
  before RGB555 quantization.
- Kept point upscaling and lower-resolution presets available, but made the
  default output substantially cleaner at 720p and 1080p.
- Replaced the city map's nested `GUIUtility.RotateAroundPivot` calls with one
  logical line transform composed under the retro canvas matrix.
- Restored road and route alignment and replaced the player's short heading
  line with a clear chevron arrow.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 141/141 passed.
- Unity PlayMode: 20/20 passed, including the updated D3D composite test.
- Windows x64 Player build: succeeded, 0 warnings, 124,165,413 bytes.

## 2026-07-28 — PS1-inspired presentation and audio

- Added a PC RenderGraph world composite with a default `320x180` internal
  frame, RGB555 quantization, stable 4x4 Bayer dithering and point upscaling;
  retained `426x240` as an optional readability preset.
- Restyled the interaction prompt, intoxication HUD, city map and cocktail
  minigame with one burgundy/amber retro theme while keeping the UI crisp
  above the pixelated world.
- Replaced smooth runtime cylinder visuals with one shared flat-shaded
  8-sided mesh, enabled hard directional shadows and disabled camera MSAA.
- Added deterministic generated `22050 Hz` retro SFX, bounded source pools,
  cooldowns, scene-local city/bar ambience and mild filtering for the existing
  correctly routed music themes.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 137/137 passed.
- Unity PlayMode: 20/20 passed, including the D3D GPU composite smoke test.
- Windows x64 Player build: succeeded, 0 warnings, 124,164,821 bytes.

## 2026-07-28 — Cocktail glass liquid polish

- Realigned the procedural liquid to the transparent inner cavity of the
  pixel-art glass instead of drawing it across the glass frame.
- Replaced the flat glowing rectangle with darker pixel rows, a stepped
  meniscus, tapered lower rows and restrained animated highlights.
- Capped the visible fill below the rim and kept the glass sprite as the top
  layer so its outline and reflections mask the liquid naturally.

Verification:

- Runtime .NET build: 0 errors, 0 warnings.
- The open Unity Editor completed its domain reload without compiler errors.

## 2026-07-27 — Three-cocktail mixing minigame

- Replaced the five-pick drink-chain game with a same-scene modal cocktail
  mixer launched at the counter edge.
- Added three rounds with a beer/wine/vodka/cognac base, 2–4 unique additions,
  and a deterministic seven-item shelf containing four compatible choices and
  three traps.
- Added per-round scoring up to 100, a 300-point session maximum and a
  15-point penalty for every incompatible addition.
- Committed intoxication, last alcohol and the served-cocktail count through
  `GameSessionState` after each serving; bad served mixtures then deferred a
  45-second intoxication effect until finish/close, and 100 intoxication ended
  early.
- Added a 4x4 pixel-art glass/ingredient atlas plus animated glass fill,
  ingredient travel/tilt, pouring, sparks, bad bubbles, shaking, stage progress
  and final rank.
- Added mouse, keyboard and gamepad controls under the existing modal input
  lock, and replaced the old minigame text with RU/EN cocktail keys.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 106/106 passed.
- Unity PlayMode: 15/15 passed.
- Windows x64 Player build: succeeded, 0 warnings, 124,131,406 bytes.

## 2026-07-27 — Correct scene music routing

- Assigned `city_theme` exclusively to `Resources/Audio/CityMusic` and
  `bar_theme` exclusively to `Resources/Audio/BarMusic`.
- Kept both looping players under their matching scene roots, so Single-mode
  transitions stop the previous scene's theme automatically.

Verification:

- The open Unity Editor imported both resource slots and compiled Runtime,
  Editor, EditModeTests and PlayModeTests successfully with no C# errors.
- Resource layout contains only `city_theme` in `CityMusic` and only
  `bar_theme` in `BarMusic`.

## 2026-07-27 — Fog-forward readability pass

- Lifted the exterior palette, ambient/moon contribution and shadow floor while
  changing the city fog to a denser luminous gray-green.
- Added one seeded, player-following `CityFogField` capped at 36 slow
  world-space particles.
- Added a shared depth-tested atmosphere shader and two-layer `CityLightHalo`
  effects for pooled street lights, bar lights and active amber signals.
- Changed the street-light pool to directed spot lights while keeping bar
  entrances on point lights and the complete realtime-light budget bounded.
- Retuned the City-only Bloom, ColorAdjustments, Vignette and FilmGrain profile;
  the bar interior remains free of exterior fog and halo objects.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests assemblies:
  0 errors, 0 warnings.
- Unity EditMode: 82/82 passed.
- Unity PlayMode: 14/14 passed.
- Manual 1280x720 chase-camera renders checked forward and side street
  readability, distance fog and depth-tested light diffusion.
- Windows x64 Player build: succeeded, 0 warnings, 117,808,526 bytes.

## 2026-07-27 — Scene-local city music slot

- Added `Resources/Audio/CityMusic` as the documented drop folder for a WAV,
  OGG or MP3 exterior theme.
- Added a deterministic non-spatial looping player under `CityGameRoot`.
- Kept the player scene-local, so loading `BarInterior` destroys it and stops
  exterior music without persistent audio state.

## 2026-07-27 — Noir city night

- Reworked the generated exterior into a fixed blue-black night with
  exponential fog, cold moonlight, varied dark/cool windows and a dedicated
  ACES/Bloom/ColorAdjustments/Vignette profile.
- Added deterministic collider-free street lamps and seed-phased amber
  traffic signals derived from the road graph.
- Added one shared HDR emissive material and capped the complete city at 12
  shadowless realtime point lights, prioritizing nearby lamps and bar entries.
- Kept bar interiors fog-free and preserved map, navigation, interaction and
  third-person camera behavior.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests assemblies:
  0 errors, 0 warnings.
- Unity EditMode: 82/82 passed.
- Unity PlayMode: 14/14 passed.
- Manual rendered checks: spawn street, bar facade and lit traffic signal.
- Windows x64 Player build: succeeded, 0 warnings, 105,759,742 bytes.

## 2026-07-27 — City map and bar itinerary

- Added a full-screen schematic city map with roads, blocks, player direction
  and stable numbered bar markers.
- Added ordered add/remove/reorder controls for mouse, keyboard and gamepad,
  with modal player/camera locking and a persistent map-open hint.
- Added deterministic weighted road-graph routing from the player through all
  selected bars, including session persistence and visited-stop removal.
- Added RU/EN strings plus EditMode and PlayMode regression coverage.

Verification:

- All solution assemblies: 0 errors, 0 warnings.
- Unity EditMode: 75/75 passed.
- Unity PlayMode: 12/12 passed.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Visible bar landmarks

- Added one shared procedural pixel mug sign to every generated bar and kept
  it upright and camera-facing through the existing billboard behavior.
- Added warm amber bar windows plus collider-free gold door frames and
  entrance canopies.
- Kept city layout, road navigation, entrance triggers and scene flow unchanged.
- Added PlayMode coverage for bar/marker identity, sprite sharing contract,
  ordinary-building exclusion, decorative colliders and camera yaw.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests assemblies:
  0 errors, 0 warnings.
- Unity EditMode: 63/63 passed.
- Unity PlayMode: 11/11 passed.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Counter drink minigame

- Added a counter-edge interaction station and an in-scene modal drink game
  with four cycling offers and five selections.
- Added a pure drink catalog, compatibility rules, deterministic offers,
  one-use water reset and completed/bad-mix outcomes.
- Persisted intoxication, last alcohol and total drink count across city/bar
  scene loads for the current application run.
- Added the then-current 45-second intoxication debuff with `0.75` movement
  speed, sprite sway, localized HUD and result feedback.
- Added RU/EN catalog entries plus EditMode and PlayMode regression coverage.

Verification:

- Runtime, EditModeTests and PlayModeTests assemblies: 0 errors, 0 warnings.
- Unity EditMode: 63/63 passed.
- Unity PlayMode: 10/10 passed, including minigame outcomes and scene persistence.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Third-person chase camera

- Replaced the fixed orthographic/isometric view with a perspective camera
  directly behind the player's camera-relative heading.
- Added right-mouse/right-stick yaw and sphere-cast obstacle avoidance for
  both city and bar interior distances.
- Added a PlayMode regression check for perspective projection and behind-player
  alignment.

Verification:

- Runtime and PlayMode test assemblies: 0 errors, 0 warnings.
- Unity PlayMode: 8/8 passed, including yaw/collision camera regressions and
  the complete `City -> BarInterior -> City` cycle.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Playable MVP vertical slice

- Added deterministic data-first city generation with a connected road graph,
  building lots, stable bars and validated frontage return points.
- Added runtime world construction, road/apron navigation, a modular 13-part
  sprite player, camera-relative movement and billboard presentation.
- Added nearby interaction, RU/EN catalogs, guarded scene transitions,
  persistent seed/bar context and a generated shared bar interior.
- Replaced the build scene with `City`, added `BarInterior`, runtime/editor/test
  assemblies, and a Windows build helper.
- Added deterministic EditMode tests and PlayMode presentation/round-trip smoke
  tests.

Verification:

- `dotnet build` for Runtime, Editor, EditModeTests and PlayModeTests:
  0 errors, 0 warnings.
- Unity EditMode: 15/15 passed.
- Unity PlayMode: 6/6 passed, including `City -> BarInterior -> City`.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Repository foundation

- Initialized an empty Git repository.
- Added Unity-oriented ignore and attribute rules.
- Added repository instructions and the initial AI memory set.
- Inspected Unity version, packages, build scenes, and stock assets.
- Confirmed that the Bar Promenade MVP remains planned and has not yet been implemented.

Verification: documentation paths and baseline facts were checked against `ProjectVersion.txt`, `Packages/manifest.json`, `EditorBuildSettings.asset`, and the current `Assets` tree.
