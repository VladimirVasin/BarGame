# Project overview

## Current

- Product name: **Барный Променад** (Bar Promenade).
- Engine: Unity `6000.5.10f1`.
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
  `Assets/Scenes/StairwellInterior.unity`,
  `Assets/Scenes/HomeInterior.unity`,
  `Assets/Scenes/MountainRoad.unity` and
  `Assets/Scenes/AreaLoading.unity`, followed by
  `Assets/Scenes/ChurchInterior.unity` and
  `Assets/Scenes/AlpineVillage.unity`, then
  `Assets/Scenes/MothersHouseInterior.unity`. The last five are appended at
  build indices `7`, `8`, `9`, `10` and `11`, preserving every previous index.
- Runtime assembly: `BarPromenade.Runtime`.
- Player presentation: one modular `Player3DV2.prefab` in all nine gameplay
  roots, with independent mesh parts, a Generic in-place action set,
  same-prefab first-person subsets, a dedicated portrait, real mesh shadows
  and an analytic contact patch.
- Test assemblies: shared `BarPromenade.TestSupport` infrastructure plus
  `BarPromenade.EditModeTests` and `BarPromenade.PlayModeTests`. A run-level
  callback silences listener output for every automated test and restores the
  previous listener volume when the run finishes.

## Implemented MVP

A runtime-composed 3D coastal city, a separately loaded mountain road, the
village above its cableway and the accessible interior of the house at its
head, in which
one modular low-poly 3D hero walks the streets, the climb and the village lane, approaches the
interactive home-adjacent bar, a supermarket, his home and the church north of
the cemetery,
enters separate interiors, and returns to the matching exterior entrance.

The vertical slice contains:

- one separate `MothersHouseInterior` gameplay root entered through the
  existing summit-house exterior door and exited back to a one-shot safe
  arrival outside that same threshold. Its deterministic imported room keeps
  a low central tea table, north rocking chair, west sofa, a burning fireplace
  between two north windows and a light, clean, cared-for but old and modest
  domestic dressing. Behind the sofa, a real north-entry stair rises south
  through a split slab to a narrow west corridor and exactly two separate,
  accessible, currently empty upper rooms. One hidden plan-owned ramp makes the
  visible collider-free steps continuously walkable; structural slabs,
  partitions, door openings and well guards are runtime collision. Four
  height-aware fixed shots cover the ground room, stair/corridor and both rooms.
  Repairs, fading and soft wear carry age without dirt or abandonment. One dedicated `MothersHousePositiveAtlas`
  owns every room-authored surface instead of reusing Home or City albedos.
  Its centred south-wall entrance faces the north-wall hearth and spawns the
  hero looking north. Cool window spill and a shaded floor lamp create one
  restrained local pool without displacing the hearth as
  the warm key or adding invisible ceiling fill. Fire, muffled wind,
  alternating clock ticks and sparse timber settling form its calm sound bed.
  The table
  instantiates the literal Kettle Hat pedestrian
  prefab and leaves only its ten authored kettle renderers visible, preserving
  the source meshes, material and detail atlas as the explicit atlas exception;
  mother, cat, dinner and dialogue
  remain outside this MVP;
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
  `1440` real seconds (`24` minutes), crosses midnight with a zero-based day
  index exposed to the player as one-based `DAY N`, and naturally pauses
  wherever gameplay sets `timeScale` to zero. The Home clock shows `HH:MM`;
  the inventory Status panel shows `DAY N · HH:MM`, while one persistent view
  briefly announces day 1 after Wake and every later midnight;
- a separately runtime-composed `MountainRoad` area. The hero arrives `6 m`
  inside a `9 m` exit tunnel, then follows one continuous `620 m` uphill road
  ribbon dimensioned for the `4.83 x 1.80 m` LastRouteCar: `4.8 m` wide on
  ordinary stretches and `6.4 m` through ten `7.5 m`-radius hairpins. It rises
  `26.1 m` at no more than an `8%` grade. After five lower hairpins the
  mandatory route crosses a `50 m`-long high mountain bridge whose `5.8 m`
  deck surrounds the `4.8 m` clear roadway; the terrain opens to a gorge floor
  at world `Y=-16`, at least `25 m` below both bridge ends. Its last `25 m`
  are level - a `20 m` terrace run that carries the road out of the switchback
  field before the `5 m` entry lead - and share the actual road-mesh entry
  vertices with the same irregular roughly `42 x 27 m` terminal plateau, so
  there is no height step, open seam or transverse collider wall. The terrace
  run keeps the raised pad off the climbing road: parked any closer, its rim
  reached over the outer arc of the ninth switchback and buried it in snow. A separate dark asphalt mesh, offset `0.025 m`
  above that shared physical surface and extended `0.45 m` back over the road
  seam, makes the whole entry and R`7.5 m` turning pocket visible without
  adding a second collider. The terrain envelope keeps a `76 m` margin.
  Generic middle/far ridges sit around the outer perimeter of the complete
  route bounds, ground their bases against the minimum sampled terrain beneath
  each oriented footprint and reserve those footprints from both the road
  corridor and tree crowns. The left side owns an enterable glass cafe with a
  bespoke four-role cast: its attendant stays silent, the drinking couple share
  a private localized text conversation, and the lone patron intermittently
  wakes to call his wife home without a response. Four deliberate stools remain
  empty and rare deterministic gesture beats continue. Behind them, a passive
  kitchen run carries a hinge-ready closed refrigerator, a stove and pan, an
  extended cabinet and a cutting-board station; neither the cast nor the player
  uses it yet. The right
  side owns an operating `230 m` cableway with nine supports and eight cabins,
  with every cable height rebased from the raised terminal rather than old
  absolute world heights. At the normal `2.6 m/s`
  walk speed the route takes about `238.5 s`, or `3 min 58 s`; continuous
  `4.2 m/s` run input takes about `148 s`, or `2 min 28 s`. The Ferryman's
  Last Route car reads and drives the same route. Layered forest keeps its
  physical/mid/far budgets but now uses three deterministic crown silhouettes.
  It yields locally at three measured hairpins, the bridge and the terminal
  approach; surrounding far trees and both ridge rings keep those openings on
  the climbed road rather than turning them into extra vistas. Natural debris
  gathers into five unequal chapters with deliberate pauses at the same
  structural beats, and a bounded deterministic resolver preserves `0.35 m`
  between conservative footprints and existing roadside furniture. The
  first deliberate misc migration replaces the visible runtime-box geometry
  of `FallenLog`, `Stump`, `DeadTree`, `GuardRail`, `SnowPole`,
  `ConvexMirror`, `UtilityCabinet` and `AbandonedChair`: `102` plan placements
  select `19` deterministic Blender meshes and arrive as `12` combined
  renderers, while transforms, semantic IDs and simple collision proxies stay
  plan-owned. Boulders, culvert, utility cable and tunnel lamp remain on their
  previous builders. Five
  causal positional sound anchors belong to identifiable visible sources,
  including one loose bridge rail; one tunnel practical visibly flickers. Its
  root may generate the pure City layout/mountain plan needed by the City map
  tab, but it never calls a City world builder or creates City GameObjects;
- a separately runtime-composed `AlpineVillage` area above the cableway, on the
  same plan/validator/builder shape. One crooked lane climbs `82.1 m` and
  `6.4 m` — an average `7.8%`, under the `8.3%` pedestrian ceiling, with no
  step anywhere on it — from the cableway station on the lowest terrace to the
  house at its head, which is the highest thing in the village and the only
  thing the composition points at. Twelve houses stand either side; the chapel
  over the source and the head of the spring sit on side spurs; the adit and
  the burial ground stood there until the lead took both out of the village
  and out of the story, and the father's grave went with the cemetery. The whole bowl is walkable: the mask is `TerrainBounds` grown
  by the sampler's own `RidgeStandoff` — the line where the `74°` rise begins,
  so the terrain holds the perimeter — minus every plot's rotated footprint
  (the burial ground excepted; it is ground, and the adit gained the
  `Physical Shell` it never had) and minus the cableway cut, the one
  `7-28°` walkable way out of the village. Each
  `AlpineVillagePathDescriptor` remains a visible compacted strip and the
  route's clearance envelope against those footprints — including each
  household threshold and the narrow turn to the chapel water outlet — but no
  longer decides where a person may stand. Beside each of them the snow has a
  depth: `AlpineVillageSnowDrift` is a pure field over the shared "distance
  outside the nearest trodden route", laid as one colliderless mesh rather
  than as a term in the height contract; `TerrainCell` still owns the station
  apron and the cableway cut's entrance. The field is `0.45 m` deep wherever
  nothing has walked and zero on trodden ground, rising
  to that depth over `1.3 m` on the face the gale loads and `3.2 m` on the face
  it scours — a field with trenches worn into it, not the raked bank the art
  bible refuses. Fitted ribbons carry the rise along each route and a `1 m`
  sheet carries the saturated remainder, overlapping by a cell and drawn under
  it so the ribbon wins the join. `AlpineVillageSnowTreading` keeps one float
  per vertex so a boot presses the snow down and the snowfall fills it back
  in - CPU-side, because vertex displacement would need a third verbatim
  `Ps1Lit` clone and the snap would quantise the amplitude a print is made of.
  `IPlayerFootstepSurface` lets the village claim each step the motor takes
  and pick `FootstepSnow` or `FootstepSoil` by the depth it can see, which is
  what makes a route audible. The lane skin and the path ribbons sample the
  ground at every vertex and ride `LaneSkinLift` over it: laid flat at their
  centreline's height they were cut open by ground that curves, which is the
  pale wedges that were reported as snow lying on the street.
  Authored distance/side/yaw beats form frontage clusters and pauses; exact OBB
  validation, three `7.2-7.5 m` rear-row depth beats and a bounded symmetric
  local correction keep every seeded rotated footprint out of its neighbours
  and the lane without cascading the whole frontage away from the street.
  `AlpineVillageTerrainSampler` is the one height contract shared by planning,
  validation, the ground mesh and the map's teleport ground. Its shelves ease
  back to the macro slope over the `3.6 m` the constant names — they ran at
  `0.347 m` until `Mathf.SmoothStep` was found being fed a distance where it
  wants a `0-1` fraction — so the lane reads as a worn hollow rather than a
  ribbon on a flat field, and the ground carries no vertex colours because
  `Ps1Lit` inherits URP Lit's `COLOR`-free `Attributes` and never read them; its enclosing
  ridge starts `15 m` outside the top house's envelope (`TerrainMargin 12`
  plus `RidgeStandoff 3`) and climbs at `3.6` (`74°`) to a `60 m` crest
  `16.7 m` past the toe, deliberately steeper than the hero's own `45°` slope
  limit — the mean silhouette from mid-lane is `34.1°` and reaches `43°` on
  the nearest bearings. `TerrainBounds` remains the inhabited bowl
  (and the map's chart patch, now the bowl plus `12 m`) while the larger
  `TerrainMeshBounds` builds the complete physical rise, hidden crest and
  sampled cableway brink, so the bowl and upper turn are closed by the
  mountain and not only by a mask. The ground is one mesh, one collider, two
  submeshes: the floor on the ordinary primitive material, the rise on
  `AlpineVillageRidgeAppearance`'s `CityMountainPhysical` material (village
  haze colour, breathing density, `0.40` visibility floor and a stable opaque
  colour handoff over `96-108 m`, a cold snow-shadow tint, no shadow because
  the shader has no caster pass). The village opts into the floor's PS1 vertex
  snap, floor and rise share exact edge indices instead of a buried overlap
  ring, and floor/rise/lying-snow world UVs bake the `WindSnow` metre pitch
  once under an identity `_BaseMap_ST`; neither screen-space coverage nor a
  second renderer-size scale can make the distant lower wall crawl. The
  shared shader's zero-default City path retains its existing clip dither.
  The cableway valley remains part of the rise rather than a floor-material
  stripe — a pale strip in a dark wall reads as a hole, not a gorge. Warm fog and warm key light are the zone's whole
  signature, and the fog breathes: `RuntimeSceneSetup.EvaluateAlpineVillageFogDensity`
  runs `0.017` between gusts (`9 %` of the mother's door at `91 m` from the
  platform) to `0.045` at a crest (`41 m` left at `3 %` — the far half of
  the lane closes for seconds and the top house vanishes, then returns) on
  a wave keyed to the raw shared gust rhythm with a `0.5 s` attack and
  `1.0 s` release, written every frame by `AlpineVillageRoot.ApplyVisibility`
  under a `110 m` far plane. The §12 dimming grade is held at `0`, but its per-minute
  apply already drives isolated garland loss, seeded window darkness, dirtying
  snow, five practicals and all six causal sounds together. The weather now
  stays deliberately severe independently of that grade: a village-only
  stretched `Blizzard` profile keeps snow in `.88–1`, the shaped shared wind
  stays in `.82–1`, and a second terrain-sampled layer carries low spindrift
  with a continuous wind bed on the same deterministic bearing and gust
  rhythm. `AlpineVillagePeripheralStormPlan` measures every point against the
  complete lane/path network and a widening station-to-house aperture that
  encloses the mother's whole footprint; its presentation field lays large
  soft curtains over the exposed side fields and closes rapidly behind the
  house. It never writes global fog and never changes collision, damage,
  speed or the walkable mask. Walking off a route raises the field's overall
  visual pressure and additionally gathers new sheets near the player, so
  free traversal remains the contract and the existing
  crest/trough visibility cycle of the house is unchanged. The nine batched garland spans read that shaped wind through bounded
  vertex deformation: both anchors remain fixed while bulbs and the two real
  lights follow each moving midpoint. The enclosing ridge closes sight and traversal rather than sheltering
  the bowl; only the station canopy and moving cabin are locally dry.
  Particle alpha stays capped, and the haze wave is the one thing allowed to
  close the top of the lane — only for the seconds of a gust, with the
  uphill axis and the nearest walls readable throughout and the house back
  at `>= 5 %` from the platform at every running trough. The cableway carries in
  both directions. Pressing `E` on the platform brakes the line to
  a stop with a cabin on the boarding point - a distance-driven profile, so
  it comes to rest ON the point rather than near it - seats the hero on the
  cabin bench in first person, lets the line go, and fades behind the snow
  ridge into `AreaArrivalToken.Cableway` at the far terminal, which holds the
  arrival under a black screen until the transition flag clears. The two
  terminals differ by `MountainCablewayStationKind`: drive below with motor,
  reducer and shaft, tension carriage and weight stack above with no motor at
  all. Boarding is outboard of the outbound track, because the gap between the
  two tracks is filled by the bullwheel pedestal. The village wears a fourth
  deterministic Blender kit - generator contract `v3.0.0` /
  `village_house_archetypes_v3`, `17` assemblies / `43` role meshes. Two
  ordinary closed-shell house archetypes replace the former four cosmetic
  variants: one is a low dark timber block on a heavy stone plinth with sparse
  irregular openings; the other raises a bracketed projecting timber upper
  storey over a high masonry base and uses a more regular opening rhythm. The
  distinct top house is a third type, a broad timber main mass with a
  weathered whitewashed masonry side wing, larger and tidier without heraldry,
  frescoes or tourist-chalet ornament. Roof snow, facade repairs/shutters,
  garland posts, cable gate, rail bridge and a plain stone catch basin remain
  alongside the chapel, mine cart, adit frame, grave markers and firewood.
  Every house and the chapel render as a closed outward-facing Blender shell
  on all four sides; roofs and facade dressing do not survive around an
  invisible wall volume. The redesign preserves the planner's normalized
  bounds, footprints, collision proxies, routes, landmark aperture and story
  meaning. It raises no new
  surface family and ships no doors or window panes, because both scale with
  the descriptor across plots from `4.2` to `7 m` and are drawn by the world
  builder at real metres on the wall face the mesh's own bounds report.
  Garlands are emissive geometry; two cords and three windows own the five real
  village lights. Six bounded spatial voices belong to the visible station,
  one wire, the cable gate, water catch basin, firewood and a house wall;
- a finite, seed-reproducible coastal city driven by one immutable blueprint:
  the default preserves all 144 former road-and-lot cells inside a `13 x 12`
  urban envelope, using the added central column for a north-south river and
  shifting the eastern half one cell outward. It retains the full-width
  northern beach and sea strip. Active cells, roads and surfaces may form a
  connected sparse, non-rectangular footprint inside their map bounds;
- one default-blueprint-only mountain boundary plan closes the physical west
  and south edges with deterministic flat-shaded low-poly ridge strips whose
  toes sample the authoritative terrain top. The south skyline remains closed
  over one low, dark `10 m`-wide river-cave water mouth; surrounding rock
  terminates both bank routes physically, and the cave itself is never
  walkable. A
  separate gate-free `8 x 5.5 m` tunnel portal derived from
  `yard-south-west-access` has a terrain-overlapping raised floor and
  non-coplanar, closed wall/ceiling joints. Its `72 m` faceted shell stays
  straight and physical for `12 m`, then bends west until neither its open end
  nor the camera-relative mountain shell can enter the sightline. The first
  `11 m` belong to player navigation. Walking through the decision plane at
  `8 m` shows a localized unavailable-travel thought and guides the ordinary
  rig back to `6.5 m`; the river cave still has no interaction, and the tunnel
  still has no physical transition handler even though the separate mountain
  destination now exists. The north remains the open sea
  edge and the east remains deliberately unbounded for a separate pass. One
  two-layer camera-relative presentation shell adds only west/south ridge silhouette at
  `39.4-43.2 m`, inside the unchanged `48 m` far plane; it keeps fixed world
  azimuth and has no collider/light/navigation/world-bounds role. Physical
  ridge chunks use one shared opaque `CityMountainPhysical` material with the
  deterministic `CityMountainRockAlbedo`: matching forward/depth/depth-normal
  passes dither the horizontal-distance handoff from `43 m` to `31 m`, retain
  a restrained `0.10` visibility floor only after native Exp2 reaches it and
  return to native fog on approach. The fog-exempt shell mixes `0.86` toward
  the City fog colour, leaving only a faint distant mass while close physical
  rock becomes readable. At the south-west turn, the mountain plan owns a
  validated city-side corner earthwork over the otherwise omitted `(-1,-1)`
  blueprint cell instead of leaving a void. The closure is one continuous
  textured natural-soil slope, not a stair, terrace or platform; its centre is
  about `16.2°` and remains outside navigation behind the ordinary L-shaped
  road fence whose two physical legs meet at the exact corner. Its outer wings retain the sampled west/south terrain seams and
  exact diagonal toe, and the
  diagonal ridge interpolates the full west-to-south cross-section, welding
  its ground bond, shoulder, crest and back at both ends without changing the
  northern/eastern openings.
  A separate default-only `CornerGuard` pair closes the north-east urban-core
  road cap beside lot `[12,11]` as an ordinary physical `4 m + 4 m` L. It does
  not add ground or extend a boundary along the yards/waterfront, so their
  authored approaches remain open.
  Portal, physical entry and visual continuation pieces deliberately retain
  `RuntimePrimitiveLit`;
- one immutable default-only `CityFringeYardPlan` turns all five typed Yard
  areas into authored middle ground derived from their canonical bounds,
  declared access and sampled terrain. Four west/south variants share an old
  municipal service-belt grammar of graded maintenance trace, drainage,
  retaining work, sparse poles/cables and bounded repair/rockfall pockets.
  Their complete roughly `22 m` road-to-toe terrain is a separate conforming
  batch with a quiet compacted-fill albedo. A narrow terrain-conforming
  `0-4 m` road-shoulder trace,
  three cross-field service traces and three or four seeded meso anchors per
  strip fill the `4-14 m` working band; no longitudinal anchor gap exceeds
  `40 m`, while the established paired-trace-or-flood-drain/retaining language
  stays in the `14-22 m` toe band. All four mountain strips omit broad
  earth-textured longitudinal service-track overlays.
  Their macro anchors are a stepped culvert terrace, an industrial repair
  frame, the `6.9 m` tunnel forecourt with a validated `>6 m` clear terrain
  lane, narrow embedded marks, two continuous grounded concrete return wings,
  a two-post service frame and a crown-mounted floodlight, and caged floodworks
  with a gauge; four dedicated measured sheets distinguish
  compacted forefield, service aggregate, board-formed concrete and old
  masonry at close range. Four small
  emissive practicals remain separate from the combined geometry, while the
  nearest supported anchor within `20 m` can lease the last existing street
  Spot from `CityNightAtmosphere`; its `12`-Light pool is unchanged. The tunnel
  moves that lease to the faulty second ceiling fixture, keeps a `0.22`
  daytime floor and applies the same sparse flicker to its lens and pooled
  light. Four additional emissive-only fixtures continue into the bend; the
  faulty ballast owns a positional `5.6 m` buzz and crackles only on visible
  power dips. The
  eastern variant stays a separate low, unlit road/drain/pole/shed/berm utility
  edge and creates no ridge. A late life pass adds only one grounded,
  unoccupied mason cart at the west stone terraces. The former winch-service,
  tunnel-repair, flood-maintenance and open-hood-car sets are absent; the other
  typed Yards and the residual north-east former-lake Yard receive no separate
  vignette. Large masses are
  physical, small traces and cables are visual only. Every physical ridge
  overlaps beneath its sampled terrain
  toe and extends the near-toe collider across that join; only the open
  tunnel portal and low river water mouth interrupt the rock, while the bank
  ends close against it. Every level-safe ring-road seam into the four
  mountain Yards is walkable, while true drops retain rails; three `6 m` routes reserve
  capsule-clear cuts through the retaining line to the rock toe. Two use broad
  gravel aprons, while the south-east flood route uses a narrow embedded trace
  on continuous terrain; the fourth route continues through the open tunnel
  forecourt. The fringe plan adds no destination, Light component,
  north/east mountain or world-bounds expansion. Portal frame, segmented
  lining, bounded navigation and future-travel decision remain owned by the
  mountain/tunnel runtime contracts;
- one immutable river contract splits that default urban envelope with a
  `10 m` channel. Two continuous `3 m` promenades flank it; an `8 m` Works
  road bridge and an `8 m` Mouth road bridge carry ordinary Street traffic
  across its south and north edges, while a separate `2.8 m` timber
  ParkPath bridge reconnects the two `2 x 4` halves of the 16-cell central
  park. Each road bridge owns one staircase and lower waterside platform per
  bank, producing four navigable lower landings. Each promenade cut has a
  collidered, box-projected Quay lining along the landward stair profile,
  platform side and terminal face; its railed waterside stays open to the
  river. Across the main spans, lowered landing frontages and cave approach,
  the visible Quay face projects `0.03 m` waterward of the Paving and Bed
  side faces that used to be coplanar, while its landward face stays fixed
  beneath the rail seat. River-owned parapets stop at the bank-road pads and
  preserve those four stair openings; generic road-edge fences treat bridge
  decks as support-only and do not duplicate them. Route
  01 may use the road bridges but never the timber crossing. In
  `default-coastal`, water and its silt bed continue more than `48 m` behind
  the southern mountain so their end remains beyond visibility. Both
  promenades extend walkably from world `Z=-156` to physical rock stops at
  `Z=-182`; the cave water and space behind those stops never enter the
  navigation mask. Low waterside lanterns at `13 m` pitch keep their emissive
  lenses and fog halos lit around the clock; their pooled realtime spill and
  the sparse upper promenade lamps ride the §20 fixture floor — always
  burning, two thirds by day;
- one immutable `CityElevationPlan` produced after 2D topology and before any
  lot, surface or access is materialized. The default coastal blueprint spans
  about `8.1 m` across its generated road nodes, peaks near `10.08 m`, gives
  every urban district at least `1.5 m` of local elevation variation, keeps
  the sea at datum `0` and gives the river a monotonic descent into it.
  The seacoast's raised decks — the mol, the sea pier and the mouth
  footbridge — are contributed to the walkable mask by
  `CitySeacoastPlanner.AppendWalkableFootprints`, since a `Water` cell is
  otherwise never walkable and the player would be clamped at the cell
  boundary; the open sea itself is never contributed.
  `CityTerrainSurfacePlan` is the authoritative sampled top for
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
- deterministic exterior weather also drives one transient surface-film state
  shared by City and the Home balcony view. Ground, roads, sidewalks and road
  markings darken and gain smoothness quickly under rain, then dry at a much
  slower fixed rate; authored dry tints survive because the response is
  multiplicative through material property blocks on the existing shared
  material. Scene handoffs advance the film from absolute game time and a new
  session resets it. City roads add at most `42` deterministic top-only puddle
  quads, batched into one collider-free mesh `3 mm` above their source roads;
- one shared generated exterior cloud ceiling in City, Mountain Road, Alpine
  Village and the active Home balcony shot. A passive `220`-triangle hemisphere
  and one packed linear density texture feed three property-block profiles;
  City/Home reuse the same seed, canonical frame and absolute-time phase, while
  the road and village only change density, scale and colour. The shell follows
  camera translation, so its `47 / 119 / 109 m` radii inside the current
  `48 / 120 / 110 m` far planes are render distance rather than low physical
  altitude. It owns no fog, grade, Light, shadow or collider, and Home disables
  it everywhere except the balcony;
- a default `640x360` PS1 world composite with four-tap footprint averaging,
  exact 2x/3x scaling at 720p/1080p, a 35% perceptual-space RGB555 blend
  without a screen-space dither grid, point upscaling and percentage-driven
  intoxication vignette, ghost/chromatic image, warp, warmth and exposure
  pulse; lower `426x240` and `320x180` presets remain available;
- a crisp interface-only soot/charcoal/dirty-bone IMGUI layer after the world
  composite: prompts, pause/start, inventory, shops, inspectors, journal,
  loading, HUD and all map modes share a logical `640x360` canvas, flat
  rectangular panels, thin nested frames, stable panel grain and
  grayscale-readable focus. Packaged `Roboto-Regular` is the deterministic
  RU/EN primary face; runtime UI has no installed-OS-font dependency, and
  Unity's legacy face is emergency fallback only. Persistent
  key-binding guides and control-hint footers are intentionally absent from
  menus, modal inspectors and the map; every active contextual prompt is a full pointer
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
  the river and the sea;
- eight legacy district wall albedos plus one shared roof cap, generated by
  `tools/build-city-facade-textures.py`, remain the box/sheet contract for the
  supermarket's crossing-only fallback and a prototype crossing the clipped
  Home half-space. They are no longer the visible surface
  path for whole ordinary buildings. Two per buildable district carry that district's two
  material axes: Old Town brick and blown render, Residential cool and warm
  painted panel, Industrial sheet and utilitarian brick, Nightlife shopfront
  and service side. Each sheet is authored at `1024` as four bays by four floors
  so Unity's import to `512` is an exact 2:1 downsample and the cell grid stays
  pixel-exact. `CityFacadeAppearance` tiles those remaining consumers by their
  own window grid rather than by metres, so one authored cell covers exactly
  one pane bay and one `2.35 m` storey and the baked window band, sill and grime
  run land on the fallback geometry; a stable per-lot whole-cell bay and floor
  rotation varies which cell lands where without disturbing that. The sheets
  hold a mean linear luminance of `0.35` and the night facade tint is brightened
  by `1 / 0.62`, which preserves the pre-texture wall brightness through URP's
  linear multiply and never clamps the brightest lot, a bar. Whole ordinary
  Blender prototypes instead use the later 24-sheet semantic surface contract
  described below. A
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
  the same geometry plan. Ordinary facade panes also consume the first channel
  of the pure `CityDistrictPresentationPlanner`: each district keeps its own
  lit ratio, while every authored facade row quantizes that ratio to a stable
  warm/dark selection. At least one warm street-lamp-coloured pane reaches
  every floor and side, and a multi-pane row always retains darkness. Bar,
  Home and Supermarket panes retain their authored material families;
- one player-following `CityFogField`, capped at 36 more visible slowly
  drifting particles, plus depth-tested soft halos around lamps, bar lights
  and active signals. The same field, unchanged, now also runs on the mountain
  road and in the alpine village, where the area's own weather owner clears
  and refills it under a roof instead of a second controller; only the Exp2
  haze behind it stays per-area, and only the village's breathes with the
  gale;
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
  The on-disk humanoid-NPC asset set comprises `27` rigged designs: five
  production-folder pedestrian models, `17` staged residents, the active
  ordinary and retained inactive six-armed bartenders, the active normal and
  retained inactive Watcher cashiers, and the bus driver. The active humanoid
  cast does not grow because both ordinary replacements are one-for-one. Every
  rigged design uses
  `NpcHumanV2`, the exact Hero V2
  31-bone A-pose hierarchy and Avatar copied from
  `Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx`, with a common
  `0.835 m` rest pelvis. The five pooled and nine ordinary staged model
  manifests plus the `37`-clip `CityPedestrianLocomotion` bank use `4.0.0`.
  The four Mountain Road cafe models and their separate `10`-clip bank use
  `4.5.2`; the shelter trio and dedicated three-loop bank use `4.2.0`.
  The active bartender manifest uses `3.0.0`, while the retained six-armed
  bartender and bus-driver manifests use `2.0.0`; the cashier generator
  owns the active `1.0.0` normal output (`1.75 m`, `40` meshes / `1,244`
  triangles) and the retained `2.2.0` Watcher output (`2.05 m`, `44` /
  `1,588`) over one shared `256 px` garment-detail atlas. The generated FBXs were
  reimported and every production prefab/provider output rebuilt; runtime
  therefore consumes the replaced models, not legacy prefabs behind revised
  authoring data. Every modular `SkinnedMeshRenderer` is governed by
  `NpcSkinnedMeshCullingGuard`: the seven humanoid authoring pipelines serialize
  dynamic bounds, and the six registry families reassert that contract once
  when an instance wakes. Clip-driven limbs and procedural bones therefore
  cannot be frustum-culled by the small A-pose boxes imported from the separate
  model FBXs. The other special active models include the ordinary two-armed
  `39`-mesh/`1,136`-triangle full-body `1.75 m` bartender and the
  `48`-mesh/`1,496`-triangle driver. This is a shared adult anatomical substrate,
  not a flattening of character identity: the mouthless Long-Arm, kettle and
  hopper silhouettes remain, the inactive legacy bartender alone keeps six
  arms, and the driver keeps his horizontal eyes. The active cashier keeps the same uniform, face, blink
  and attentive gaze, but uses an ordinary head and non-scaling human neck;
  the former `18 m` treatment survives only in the inactive
  `watcher_cashier_v1` asset. The appearance catalog covers all `29` designs on
  disk — `8` bizarre and `21` normal. The active bartender and cashier are
  normal; their retained six-armed/Watcher predecessors are bizarre and never
  create duplicate active roles.
  The pool holds one presentation per registered design — Lampshade Walker,
  Chair Carrier, Kettle Hat Walker, Long-Arm Walker and Helmet Lamp Hopper —
  each with four material-property-block palettes. All five use dedicated
  in-place `Idle`/`Walk` loops: the Lampshade keeps a persistent
  hunch and uneven short step, the chair-burdened walker stays upright with a
  quicker high-knee gait, the stout Kettle Hat Walker moves at `0.90-1.02 m/s`
  on `1.08-1.18x` clips with a constant waddle and counter-phased belly and
  kettle — a kettle that is always on the boil: an editor-built lid pivot
  under the head bone (the lid and knob re-skinned to it, no bone added)
  trembles and jumps on a seeded `2.2-3.1 s` vent cycle driven by the
  presentation's own delta, and a factory-attached grey steam plume leaves the
  spout anchor, in every state including the Route 01 ride, with no light and
  no sound; he is the first pooled walker with a `256 px` detail atlas
  (greys multiplied by the palette tint through the shared material, `2,004`
  triangles / `52` meshes with real sleeves, cuffs, lapels, thumbs and booted
  toes) — and the narrow Long-Arm Walker is the slowest at `0.72-0.84 m/s` on
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
  separate passive `CityBusDriver3D` uses the shared `Player3DLit` material
  and the `NpcHumanV2` rig/Avatar copied from Hero V2, with a normal low-poly
  head and its canonical long horizontal eyes.
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
  remain unchanged. Its bounded audio presentation is equally physical: a
  fully spatial rear-engine loop uses a linear `24-48 m` tail tied to
  `RuntimeSceneSetup.CityFarClipPlane`: it is silent through the `76-86 m`
  fog-hidden activation band and becomes readable only after entering the
  rendered street slice. A second rear-mounted structure-borne loop fades in
  only for the hero aboard. The front and rear
  doorways each own a dedicated spatial pneumatic voice; opening and closing
  clips are fired once from the actual door-phase edges and pool cleanly with
  the actor. Canonical Route 01 is an immutable right-hand, Street-only
  closed grand city loop. Its target planner serves every district point of
  interest and `PlayerHome`, and on a dressed layout adds coverage targets:
  every open-area access gate (cemetery, all yards, the waterfront), two
  eastern waterfront spread anchors so the whole `400 m` beach front is
  driven, the outermost park gate on each river bank and the supermarket.
  Targets are ordered by their station along the road-grid perimeter,
  counter-clockwise from home, so the right-hand doors face the outer
  precincts and the served sequence crosses the river exactly twice — once
  per road bridge. Coverage targets prefer the kerb that faces their
  precinct over raw edge adjacency, anchor beside the gate rather than in
  its `8.8 m` approach throat, may drift up to four road edges and `120 m`
  when their own frontage ends in a river-flank or map-corner stub, and are
  dropped (never fatal) if no cycle-capable candidate survives. District
  POIs keep their bounded same-district river fallback and Home stays on
  its frontage or one edge away. Connectors are length-weighted shortest
  paths that may not drive any selected stop's own direction — the bus
  never cruises past its own pole — while the opposite direction of a stop
  street stays open, as on any real carriageway. Stop furniture keeps
  `7 m` of clearance (pole and shelter wall centre) from every bar, home
  and supermarket door sidewalk point: a blocked placement slides along
  its link to the nearest clear spot and a link with no clear spot is
  rejected, so no shelter ever crowds an entrance. After the ring closes,
  a coalescing pass drops the less essential stop of any pair closer than
  `80 m` along the loop (home and the district POIs are never dropped; the
  surviving pole serves both destinations), then a spacing pass walks the
  loop and inserts plain kerbside stops wherever the along-loop gap exceeds
  `200 m`, aiming at `150 m` and never landing two inserted stops closer
  than `80 m` or on a directed street the loop drives twice. A second,
  planar coalesce enforces a `35 m` floor between any two poles anywhere
  on the map — the loop folds back on itself, and along-loop distance
  cannot see two shelters sharing a corner. The veto is empirical, not
  predictive: a drop is tried, the spacing pass refills the torn
  stretch, and only if some gap still exceeds three ideal intervals is
  the drop rolled back (then the pair's other member, then protection). Full-body-clear ordinary straights and proven `6 m`-radius left
  turns enter the loop. At selected Road v2.1 nodes only — whose corner
  pads may now stand on open yard ground, which is what lets the loop turn
  along the city fringe — a clearance-proven two-edge right-turn macro uses
  a long S-merge across the full incoming Street, a `4.5 m` quarter-turn in
  the clear core and a symmetric S-return across the outgoing Street;
  ordinary tight `3 m` right turns remain rejected. A physical street link
  may recur in a connector, but every ordered occurrence receives a unique
  route link/node ID. Route selection has no random branch or player
  pursuit. On the production layout the loop runs about `5.6 km` and serves
  about twenty-eight named stops — semantic, gate and numbered street stops —
  each with a physical blue `01` pole, served once per lap by that
  deterministic door/driver timeline with a fixed `10 s` total dwell,
  including `0.70 s` opening and `0.70 s` closing transitions for both
  doors. Random roadside decoration does not emit bus shelters.
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
  the same RMB mouse input, gamepad right stick and arrow keys as ordinary
  orbit control rotate a bounded yaw/pitch view in place inside the cabin. The exit prompt is
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
  `Resources/Audio/StairwellMusic` in `StairwellInterior`, and the optional
  `church_theme` slot only from `Resources/Audio/ChurchMusic` in
  `ChurchInterior`; Home adds an optional
  `Resources/Audio/HomeMusic/home_theme` loop. One mixing rule (`MusicMix`)
  governs every music change, whether the hero loads into it or walks into
  it: a theme always leaves through an unscaled `4 s` fade-out, and no other
  theme may sound a note until that tail reaches zero, after which it starts
  from silence with an unscaled `1 s` fade-in.
  Every scene theme waits for its imported clip data before that entry. The
  six present masters are measured independently because their raw integrated
  loudness spans roughly `8 LUFS`; per-track source trims plus the shared
  `-5.5 dB` Music bus place them all near `-30.5 LUFS` after the `-6 dB`
  master headroom. A `12 kHz` low-pass leaves the upper transient band to
  actions without removing the themes' melodic presence. A departing theme detaches from its
  scene into the persistent mix, so a Single-load never cuts the fade-out
  short and never waits for it either: the tail finishes over the door
  presentation while the destination streams in, and the destination theme
  enters after it. Home pauses `home_theme` after the same `4 s` fade-out
  whenever the Balcony shot owns the doorway-hysteresis zone, then resumes the
  same sample through the `1 s` fade-in only after returning indoors and only
  once the mix is clear. Home also owns an optional interaction-local
  `Resources/Audio/SmokingMusic/smoking_theme` loop: it holds silent until the
  apartment theme has finished leaving, eases in over the shared `1 s`, and
  leaves through the shared `4 s` fade-out when the vignette exits.
  Music is also bound to places, not only to scenes: `City` hands the mix to
  an optional place theme whenever the hero stands on grounds that have one,
  and takes it back on the way out. The one slot today is
  `Resources/Audio/CemeteryMusic/cemetery_theme` over
  `CityCemeteryPlan.Grounds`; the place keeps the mix until the hero is `4 m`
  clear of those grounds, and both tracks resume from their own sample.
  Missing optional tracks are silent-safe — an empty place slot simply leaves
  the city theme playing. All
  themes route through the shared `Music` mixer group, receive a mild
  low-pass treatment and remain owned by their scene or interaction;
- one shared `BarPromenadeAudio` mixer with `Music`, `Ambience/Beds`,
  `Ambience/Details`, `SFX/World`, `SFX/Gameplay` and dry `UI` groups;
  every snapshot authors the same causal gain hierarchy over `-6 dB` master
  headroom: Music `-5.5 dB`, Beds `-4 dB`, Details `+0.5 dB`, World
  `+2 dB`, Gameplay `+2.5 dB` and UI `+1.5 dB`. City, Bar, Stairwell, Home
  and DoorTransition still feed dedicated reverb/echo returns and
  switch with a short `0.25 s` wet-tail transition outside the immediate
  DoorTransition blackout. Details/world sends are offset against their dry
  boosts, so foreground transients rise without making rooms wetter;
- deterministic generated mono retro SFX at `22050 Hz`, including a separate
  door latch and sustained hinge creak, with bounded
  category pools, per-effect cooldowns and voice limits, all routed through
  canonical mixer groups;
- separate scene-local procedural City, Bar, Home and Stairwell ambience beds,
  plus a nine-voice causal City runtime, a five-source Home spatial soundscape
  and a three-source Stairwell soundscape. City's former non-spatial electrical
  content is gone: its bed is only a quiet diffuse air floor. Ten immutable
  descriptors bind five loops, three autonomous details and two physical-action
  cues to the exact visible waterworks, drying rack, carpet, weighbridge,
  last-route speaker and park-fountain bounds. The director owns five loop,
  three scheduled and one action voice, creates only deterministic quantized
  mono `22050 Hz` clips, activates them inside their finite radii and applies
  coarse building-mass occlusion. Carpet impacts fire on the authored contact
  frame; weighbridge stress fires only when its real needle crosses the loaded
  threshold. The unbound park swing deliberately remains silent.
  Surf is one fully spatial voice following the nearest point of the finite
  waterline and reuses the same building-mass attenuation; thunder is placed
  at the deterministic lightning azimuth, and
  rain alone stays diffuse because it is a field around the listener. Home
  combines a calm room bed, synchronized co-located closed
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
  deterministic optional interior loops, filtered cross-city arterials,
  required open-area access edges and exactly three declared river crossings.
  `default-coastal` additionally requires every exterior road-grid frontage as
  Street after the seeded passes; the two road bridges and continuous bank
  roads therefore close one drivable outer circuit instead of leaving random
  gaps at the city edge;
- four readable built areas—Old Town, Residential, Industrial and
  Nightlife—plus a fixed 16-cell central park split into two `2 x 4` regions
  with lawn, plazas, trees, benches and hedges. A dedicated timber footbridge
  joins their ParkPath graph across the river. `CityParkBenchPlanner` derives
  four ordinary benches per region from real non-bridge path runs; one
  descriptor keeps the oriented timber, collider and path-facing sit dock in
  agreement;
- one fixed Nightlife survival pocket in the walkable ground gap between
  presentation cells `[10;5]` and `[11;5]`. A closed, non-walkable service
  bridge spans the two ordinary buildings as a full `11.602 m`-deep arch while
  the ground remains traversable: a northern ten-step flight resolves the
  native `1.562 m` terrain difference and meets one `7.30614 x 8.851 m`
  supported service terrace. The slab runs from that stair seam into the east
  facade support and south to the raw wall end, so it is not a detached central
  plinth. It contains the visible `1.50 m` upper landing, barrel, standing and
  seated warmers, bedding and sleeper; its masonry mass reaches the lower datum,
  while sparse mundane clutter remains on the lower ground.
  Fifteen exact-name renderers reuse measured masonry, concrete, paving,
  metal, timber, cloth, paper, enamel and roof albedos through MPBs. The three
  residents are detailed Hero-Avatar `NpcHumanV2` prefabs with separate
  `256 px` garment/face atlases and independent standing-warm, seated-warm and
  sleeping-breath loops; they stay colliderless and never read the player. One
  full-depth west `2.2 m` route remains on that lower ground. The terrace is a
  stair-only dead end: `1.09 m` north/south guards plus a west guard south of the stair
  close every sampled `0.41-1.562 m` drop instead of pretending the east side
  is a seamless second route. The only opening is the stair band. Five
  independently moving emissive flame/ember parts, a transparent ground spill,
  deterministic sparks and one synchronized strong warm realtime Point Light
  give the barrel a causal moving pool, plus one bounded synthesized crackle;
  a local particle trigger removes rain only under the roof and does not add
  local fog;
- one mandatory north-edge waterfront in the default blueprint, dressed as
  the seacoast precinct (`CitySeacoastPlanner`): its connected beach has a
  deterministic street approach and remains walkable to the water line,
  while the northern water row carries an animated sea (a third material of
  the shared water shader over chunked sheets and a shelving silt bed whose
  foam line draws the surf) and stays excluded from player navigation and
  night-fixture placement. The shore is zoned around the river mouth — a
  dead port with a concrete mol and a frozen derrick
  to the west; a granite esplanade with sparse glow lamps, benches and the
  abandoned municipal boat station (hut, «ПРОКАТ ЛОДОК» board, sea pier
  with the fisherman, chained slipway, hauled hulls) at the centre; rotten
  breakwater piles, driftwood, dune grass and a stranded barge on the wild
  east — with a timber footbridge over the mouth, full-width quay thresholds
  adopting both river promenades' north ends while short transverse rails
  visibly close only their non-walkable waterside lips, a coast pedestrian lane in the walker
  graph, seacoast texture sheets, map landmarks and a synthesized surf
  ambience bed. Offshore, at the edge of visibility in the fog, stands
  the abandoned lighthouse island (`CityLighthouseIslandPlanner`): a
  fixed presentation-only rock mound with ruined fishermen's shacks, a
  wrecked hull and a ~15 m banded lighthouse whose lantern genuinely
  rotates (two opposed additive beams, pure seed+minute rules,
  night-gated), rendered on its own no-fog shaders with a
  camera-distance self-fade inside the 48 m far plane — visible from
  the esplanade, sand and pier head, gone from every street;
- a reusable Cemetery non-urban profile on the default city's eastern edge
  (the church occupies the next `4 x 2` rows and the former lake block above
  that remains a plain `4 x 4` north-east yard),
  where the `3 x 2` cemetery is walkable ground;
  it requires one street-linked open-area approach and exposes the same data
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
  dedicated `CityCemetery*Albedo` sheets. The watchman at the gate carries
  the game's one paid job, and it is four worked acts on a vacant plot —
  dig, lower, fill, set the stone — each a modal session that takes the
  camera down onto the grave, hides the hero and animates a procedural spade
  alone. He gives that job over and over: `CemeteryGravediggingRegister`
  hands out the next free plot outward from his post, up to three unfinished
  holes at once, and settles up for every closed grave in one sum. Nothing is
  committed until an act finishes, so every worksite in the yard stays a pure
  function of its own rung in the book of work (`CemeteryGraveWorkLedger`,
  one stage and one epitaph per plot). From the session's first sealed
  grave onward, two ordinary procedural ravens hold to the yard: one on
  the crown of that first mound, the other a few steps off on vacant
  ground, facing the grave. They shift their weight, preen, rarely give a
  dry synthesized caw, follow a passer-by with their heads, flush into the
  fog when the hero closes within arm's reach and settle back on the same
  two points once he is nearly out of sight; while a grave-work session
  runs they neither startle nor call. Sparse pairs of the same wintering
  ravens also hold open bird-logic spots across all three outdoor areas
  from the first day — always already perched, never arriving in frame and
  never anywhere the story touches;
- one deterministic Church precinct on the `4 x 2` open area immediately
  north of that cemetery. Its sole street frontage and exterior entrance face
  west; the altar end faces east. City loads the `44 x 23 x 32 m`
  Blender-authored Catholic exterior at `0.55` scale and a `10 m` setback from
  its west street — neo-Gothic tower and spire, buttresses, lancet windows,
  rose window, pitched roofs and Latin crosses — with emissive windows but no
  new realtime exterior Light.
  `CityChurchCourtyardPlan` gives the site a stone approach/forecourt and a
  restrained north lawn/garden with two sittable benches, two small trees, six
  clipped shrubs and two modest beds. `CityChurchCemeteryPassagePlan` continues the
  cemetery's middle cross alley through one maintained `3 m` north-fence
  opening into the south church path while preserving the west cemetery gate
  as its only street gate and the route used by the mourner, watchman and grave
  work. The safe shared threshold is an internal connection, not another
  `CityOpenAreaAccessDescriptor`, and adds no sound, lore or realtime Light.
  A completed door action routes through the
  shared `DoorTransition` into `ChurchInterior`, whose validated plan owns a
  narthex, nave, crossing/choir, four piers, two side aisles, pew rows,
  confessionals, font, votive stands and an inaccessible sanctuary with high
  altar, tabernacle and crucifix. Exiting returns to the exact church frontage.
  One deterministic
  Blender source exports independent exterior/interior FBX and Resources
  prefabs so the City never loads the furnishings;
- one deterministic city-decoration plan with a distinct silhouette or facade
  treatment on every ordinary building lot, four primary urban landmarks, two
  park landmarks and optional frontage, roadside and park clusters. Its 24
  visual-family catalog includes chimneys, scaffolding, balconies, laundry,
  tanks, pipe racks, billboards, fire escapes, markets, discarded furniture,
  cargo, vending queues, a legacy shelter recipe, phone booths, roadworks, a
  fountain/statue, bandstand, chess tables and playground equipment. The
  playground's two swing seats are the one decoration that is not baked
  into a batch: each hangs from the top beam as a hinged rigid body the
  hero pushes by walking into it. The
  ordinary random roadside pool deliberately omits bus shelters because
  Route 01 owns its target-derived physical stop poles. Ground-level
  frontage and roadside descriptors sample the rendered terrain at their
  final XZ anchor, so their geometry, collision proxies and interaction
  docks share the actual pavement height rather than the lot datum;
- one deterministic residential-courtyard pass selects at most four shallow
  facade-side pockets, each no more than `1.05 m` deep. Six imported variants
  cover a Nardi table, bicycle repair, balcony basket/pulley, chair repair,
  sweeping kit and a quiet bench with planters. The planner keeps their full
  proxies clear of doors, accesses, district POIs, existing blocking geometry
  and drying lines; because those proxies enter the shared static-collision
  plan before wind dressing, laundry moves or is omitted instead of intersecting
  a pocket. Selected active pockets may receive generic, colliderless
  residents, capped at five; balcony-basket and quiet pocket compositions remain unoccupied,
  and fringe Yards receive no residents. They add no text, interaction, light,
  audio or story reaction;
- every ordinary Residential building contributes one deterministic passive
  balcony-smoker candidate, while a per-session director rolls local
  appearances around the moving player on the fog-readable lowest balcony row,
  prefers docks `12-22 m` across the street and ahead of travel, keeps at most
  two active and one
  per building, and releases them by distance; the player home is never
  eligible. Each candidate varies the current roaming
  archetype, samples the literal Hero V2 `SmokeLoop` with its authored holds on
  a hidden native driver, transfers all `31` canonical local bone channels,
  and reuses the existing Blender cigarette/ember meshes plus its mouth-exhale
  plume on the shared rig. Home instead keeps its bounded deterministic
  selection only where complete prototypes fit the reconstructed half-space,
  and enables those actors only for the Balcony shot.
  They have no collision, interaction, speech, sound, light or story state;
- one deterministic `city_misc_citywide_v4` mesh library at generator version
  `4.9.0` supplies the passive visuals for the broad City misc pass: `82`
  semantic kinds resolve to `122` assemblies, `259` role meshes and
  `46,542` triangles. It covers the 24-family
  decoration layer and park landmarks, street lamps and signal housings,
  Route 01 shelters/poles, the eastern yard, cemetery graves and vegetation,
    the church-yard surfaces and planting plus the modified cemetery
    north-fence posts and rails,
  seacoast boats/barge/driftwood, fringe utility dressing, the static shells
  of all four district points of interest and the Nightlife arch-shelter kit,
  including its full-height masonry platform support and worn slab. Its former
  bar, supermarket and player-home shells remain in the catalog only for v4
  compatibility and are not instantiated. These are role meshes rather than
  world prefabs:
  validated plans still own placement, terrain, collision,
  dynamics, interactions, realtime lights/halos, cloth and NPCs. The standing,
  seated and sleeping Nightlife shelter meshes remain compatibility-only and
  are not instantiated. The live trio are staged `NpcHumanV2` adults on the
  Hero V2 Avatar, with three separate `256 px` detail atlases and an isolated
  three-loop idle bank. Fifteen shell, terrace, barrel, bedding and clutter
  renderers reuse measured City surface albedos through property blocks.
  Tilted cemetery monuments intentionally retain their legacy visual builder;
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
- city-wide wind dressing: one cross-zone plan
  (`CityWindDressing{Plan,Planner,Validator,WorldBuilder}`) hangs up to `64`
  simulated cloth and rope pieces off structures the other dressers already
  draw — torn awning rags on up to four of Old Town's market valances (two
  per stall), grey shrouds on up to three scaffoldings' outer guard rails,
  up to six residential courtyard drying lines on their own drawn poles and
  parabola-sagged rope (`CityRopeSpanGeometry`, walk-through wash in the
  cloth body registry, lines 18 m apart), dark tarpaulin curtains and sling
  ends on up to four industrial pipe racks' street-side ties (the rooftop
  gantry is skipped — its beam runs tens of metres up the landmark tower,
  where cloth is sub-pixel), eight faded fire-escape banners and two rope
  ends in Nightlife (no billboard skirts — every nightlife billboard rides
  a ~50 m tower), exactly one
  remnant pennant on the park bandstand, net rags on the pier rail and
  tarred mooring ends on the slipway chain (the pier head's fisherman
  composition stays clear), two dark wreath ribbons on cemetery enclosure
  posts (offering graves first), service tarps on the fringe repair gantry
  and utility shed plus dead cable tails off the crossarms. Anchors are
  picked by stride across each district's full anchor list, and a
  street-level per-district floor is test-pinned so the dressing meets an
  ordinary walk instead of clustering once per city. Every piece is
  a `ClothPanelFactory` panel on the weather-wind registry; rope-width
  strips (`<= 0.12 m`) keep the factory's flat colour while wider pieces
  ride the shared POI cloth sheet; only line poles collide (batched
  collider). Residential lines use the free frontage bay opposite their
  furniture anchor and are omitted when that complete corridor would meet a
  blocking decoration, leave the block or crowd the entrance. The bar-side
  yard, lighthouse island, drained-lake block,
  tunnel forecourt, flood works and stone terraces hang nothing by
  authored rule;
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
- ordinary `BuildingLot` planning and collision envelopes retain their own
  `36–52 m` height range and the existing district bands inside it; visible
  fixed prototype heights are described below. From the conservative
  `4 m` maximum chase-camera height, the lowest roof is at least `32 m` deep
  and retains only about `0.66%` of its source colour under City's fixed
  `0.070` Exp2 fog. The bar keeps its former `5–13 m` envelope, the player home
  stays `8.8 m`, and the supermarket stays `6.4 m`; public places and the park
  remain open land rather than receiving a tall mass;
- the live ordinary-building path now instantiates one fixed-metre Blender
  prototype for each urban district: Old Town
  `14 x 13.5 x 42 m`, Residential `11.5 x 11.5 x 40 m`, Industrial
  `14 x 13.5 x 36 m` and Nightlife `12.5 x 12 x 48 m`. Their four wrapper
  prefabs expose `28` passive semantic meshes — `FacadePrimary`,
  `FacadeSecondary`, `Plinth`, `Roof`, `Metal`, `WindowFrame` and
  `WindowGlass` per district — plus front/roof/facade attachment
  metadata and `194` addressable opening slots through one Resources provider.
  Residential owns eight additional semantic balcony records: two on each of
  four facade levels, each pairing a `2.5 x 1.2 m` deck and resident dock with
  exactly one person-height glazed door and its adjacent apartment window.
  Every ordinary lot selects its district wrapper, keeps authored metre scale
  and aligns the wrapper's `+Z` front anchor to the generated door. Generator
  `2.1.0` exports `4,218` triangles and gives the two facade roles a four-side, non-repeating height atlas,
  every authored plinth face its own complete non-repeating `0..1` projection,
  roof/metal/frame physically scaled repeat UVs and every glass face its own
  `0..1` pane UV. Unity binds
  `24` deterministic district/surface sheets to the six opaque roles through
  one shared material plus MPBs; the UV2-slot shader separately preserves
  deterministic warm/dark row selection and its shared §20 fixture factor
  without making the FBX readable. Lit panes keep explicit emission at the
  two-thirds day floor. The generator rejects positive-area, same-facing
  exterior coplanar overlaps and broad axis-aligned opaque overlaps with less
  than `0.03 m` relief; join faces are omitted and rail/trim layers are
  depth-separated by `0.035–0.065 m`. Unity retains a small terrain foundation skirt inset by
  `0.08 m` on every horizontal side and the former lot envelope as an
  invisible collider; navigation and sound still use `BuildingLot`. A primary
  landmark and the lot's required ordinary core always own complementary
  surfaces: facade under the three roof landmarks, roof above Nightlife's
  facade cinema. Roof and facade decoration anchors otherwise follow fixed
  prototype mounts. The
  bounded Home exterior reuses the exact pose for whole exterior models, omits
  hidden models and keeps the old clipped silhouette only where a non-readable
  model crosses the apartment half-space;
- the bar now uses the complete fixed-metre `bar_exterior_v2` from the shared
  bar Blender pipeline instead of the City misc `BarBuildingShell` and generic
  window bands. Its authored `12.2645 x 13.5237 x 9.3435 m`
  width/depth/height envelope is a two-storey late-Victorian neighbourhood pub:
  old brick and faded render, a pitched slate roof, unequal chimneys, a lower
  service wing, bottle-green and oxblood faceted shopfront, individual sash
  windows, gutters/downpipes and the retained pictorial tankard. Solid timber
  cheeks between the door and bay windows, one recessed flank panel at each
  outer bay edge and full-depth jamb returns close every oblique sightline into
  the empty shell. It is placed at unit scale from the unchanged door and sign
  anchors. Unity still owns the `0.08 m` front/side-inset, box-projected
  `ExteriorBrick` foundation skirt, renderer-free full-size logical collider,
  entrance apron, trigger,
  transition and existing bar light. A fully visible Home reconstruction reuses
  the same complete collider-free model; only a pub crossing the apartment
  half-space keeps the clipped legacy silhouette;
- the supermarket now uses complete fixed-metre
  `supermarket_exterior_v1` rather than its City misc shell, generic apartment
  window bands and runtime-box storefront. The passive `15.5 x 15.5 x 6.4 m`
  neighbourhood-store body owns dark brick piers, a recessed double entrance,
  four framed glazing bays, a `9.2 m` canopy, integrated original
  cream/ochre/green/burgundy fascia, the authored `ПРОДУКТЫ` sign, service
  elevations, parapet and low roof plant. Four dedicated sheets split unique
  wall/fascia atlases from physically repeated brick/metal; roof and warm
  supermarket glazing reuse their shared families. Unity aligns the
  `exterior_door` anchor to the unchanged lot door and retains the full logical
  collider, `4.8 m` apron, trigger, transition and side-wall-seated yard
  spotlight. Its
  terrain skirt sits `0.14 m` inside every horizontal face. Full Home reuses
  the collider-free model and a half-space crossing alone keeps the clipped
  fallback. The old City misc supermarket shell remains catalogued only for
  compatibility;
- the player home uses complete passive `player_home_exterior_v1`, a
  fixed-metre interpretation of the restrained Georgian Series 209-1 type.
  Its `13 x 12 x 8.8 m` body owns repaired cold stucco, a brick plinth, pitched
  slate roof, irregular framed openings, recessed entry and supported upper
  gallery. The canonical deck projects `2.3 m` past the door plane, so visual
  bounds extend from local `-Z 6 m` to `+Z 8.3 m` while the lot, inset
  `0.08 m` foundation and renderer-free logical collider remain body-sized.
  Forty-seven semantic meshes bind nine dedicated sheets through authored or
  metre-scaled UVs; competing opaque layers keep at least `0.03 m` clearance.
  Exactly the upper street window immediately left of the balcony is
  emissive, and every other pane is dark. City aligns `exterior_door` to the
  unchanged lot door and retains the walkway, mailbox, lamp, number `7`,
  beacon, trigger and transition. Home reconstructs the same materials, exact
  visible window positions, recessed entry and outward physical balcony, but
  omits the narrow street-only front eave fascia that crossed its fixed camera
  as a foreground beam; the pitched roof edge remains;
  the old City misc shell remains compatibility-only;
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
  cells, 16 north-east yard cells and 6 cemetery cells. The core contains
  exactly one reachable Residential bar at `(12,6)`, one non-bar player home
  across its shared street frontage at `(12,5)`, and exactly one ordinary
  street-front supermarket. The retained bar keeps stable ID
  `bar-01352777-12-06` and `SplitTheG` interior dressing; the former Industrial,
  Nightlife and Old Town bar lots are ordinary buildings again. Custom
  generated layouts may still request multiple graph-separated bars;
- a default `8.8 m` player-home body with a recognizable supported upper
  gallery, open balcony door and one lit window to its street-left; City and
  Home share the same `4.7 m` floor, door/window positions and `2.3 m` outward
  deck geometry;
- when the opening route first reaches the City, the hero starts on the road
  node beside the deterministic player home and its neighboring bar, `13 m`
  from their shared street approach under default spacing; custom-layout
  fallback placement remains bounded to `48 m` by traversable street distance,
  and returning from a bar, home or supermarket interior restores that
  entrance's own sidewalk arrival point rather than the road centerline;
- diegetic bar identification through warm windows, framed entrances and
  shared camera-facing pixel mug signs;
- one production `Resources/Player/Player3DV2` prefab selected by all nine
  gameplay roots, prefab-derived first-person subsets and the inventory
  portrait. It keeps the `1.75 m`, 31-bone contract with 38 bone-only Actions
  in 34 mesh parts and 1,984 triangles, but uses adult `7.4946`-head
  proportions, an atlas-driven
  five-state face and a full-colour point-filtered clothing atlas. Its open
  olive field jacket has long sleeves and no strap; painted garment and boot
  construction replace protruding detail meshes;
- one retained `Resources/Player/Player3D` Hero V1 prefab and its portrait.
  `Player3DVariant.ProductionV1` can still select that byte-frozen burgundy,
  strapped model and its `37` Actions explicitly for fallback and legacy
  contract checks, but no ordinary gameplay or inventory route selects it;
- one manual PlayableGraph presentation that damp-blends the in-place
  four-second `Idle`, one-second `Walk` and `0.75 s`/18-frame `Run` actions
  from actual constrained planar speed.
  Idle alternates readable breathing and weight shifts; Walk uses full
  contact/down/passing/up phases with independently flexing elbows, knees and
  ankles. Run is a separate heavy, weary gait with a forward load, stronger
  arm swing, deeper knee lift and short flight phase. Start and stop use
  `0.14 s`/`0.20 s` smooth envelopes, and visible gait cadence follows the
  blended weight. Root motion stays disabled while
  the face atlas drives neutral, half/closed blink, watchful and tense states;
  V1 retains its bone fallback. A failed balance check may temporarily suspend
  this graph while the
  same registered bones are owned by the bounded ragdoll. Intoxication sway,
  arm spread, knee bend and balance lean are
  additive rotational/limb poses, preserve the authored pelvis position in
  the actor ground plane and reset through the same lifecycle cleanup. After
  the ordinary and additive pose is sampled, reused CPU bake buffers measure
  the actual deformed vertices of the registered foot meshes, then offset only
  the pelvis vertically so the lower visible sole stays at its neutral grounded
  height; the physical player root, model root and contextual clips remain
  untouched. Run weight progressively releases downward grounding, and at full
  Run it only lifts sole penetration instead of pulling both airborne boots
  down to the floor;
- one shared source-scene action for all ten ordinary bar, supermarket,
  home, stairwell and mother's-house location doors. After the existing interact command, the
  constrained motor guides the visible hero to an explicit grounded dock and
  facing, holds a neutral frame, then plays the planted
  `DoorUseEnter/Loop/Exit` lean and short physical-right-hand press. Only the
  terminal neutral completion may request the separate scene transition. The
  City supermarket entrance alone opts into a calculated `0.242 m` initial
  vertical tolerance covering the complete road/curb/graded prompt reach;
  every other door retains the common `0.02 m` default;
- ordinary URP mesh shadows cast from the real character geometry in every
  gameplay root, plus one small light-independent analytic contact patch fixed
  to the grounded player root. The patch follows foot plant and expands,
  rotates and offsets for left/right falls without moving the physical root;
- tank-control road-constrained movement: `W` walks along the hero's own
  forward axis to a `2.6 m/s` maximum; holding either Shift or gamepad L3 with
  positive forward input raises that target to `4.2 m/s`. `S` backs him up at
  `1.4 m/s` with a
  dedicated backpedal gait, `A`/`D` yaw him in place at `150°/s` with
  step-turn clips (and steer the arc while moving), all through `6.5 m/s²`
  acceleration and `11 m/s²` braking; ordinary release coasts, hard
  modal/transition/teleport stops remain immediate, constrained displacement
  cannot store hidden momentum, and the camera never steers locomotion.
  Intoxication scales walk/run speed, fatigue adds no movement debuff, and
  scripted interaction approaches remain at walking pace;
- in City, BarInterior and ordinary Supermarket play, a very close freely
  orbiting perspective third-person chase camera with
  `2.6 m / 53°` exterior and `2.2 m / 57°` interior framing, deliberately
  raised `1.4 m / 1.3 m` focus points that keep the hero in the lower frame,
  weighty yaw/pitch/focus damping, a player-controlled vertical orbit bounded
  to `-20°..55°`, bounded focus lag, teleport snapping, subtle deterministic
  idle/walk motion and smoothly recovering obstacle-aware distance. RMB mouse
  motion, the gamepad right stick and the arrow keys (a stick-style
  per-second axis; walking is WASD-only) drive both orbit axes in City, Bar
  and ordinary Supermarket play; the seated park board game opts the arrows
  out of its camera sample because they own the board cursor there;
  cinematic motion fades out for fullscreen modals,
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
- a full-screen city map projected from a display envelope seeded by the
  blueprint's centered map bounds,
  with area colors and labels anchored on real active cells, distinct park,
  beach, sea, river, promenade and cemetery surfaces and paths, the one
  cemetery/church internal passage cut into both precinct outlines without
  becoming a street gate or teleport anchor, the
  seacoast's landmarks (mol, pier, hut, piles, barge, and the
  lighthouse island's dot pinned to the chart's north border at its
  true easting),
  plus separate map treatments for the Works, Mouth and timber bridges,
  player/home-bar markers,
  a dedicated labeled home icon, a distinct
  grocery-shop marker and four kind-specific public-place
  markers with a localized legend. Hovering a bar, home, shop or public-place
  marker shows its localized name in a bounded high-contrast tooltip. Public
  lots are drawn as open ground rather than buildings, and all landmark data
  comes directly from the canonical validated layout used by the world
  builder. It consumes `CityWorldResult.MountainBoundaryPlan`, expands its
  display bounds only toward west and south, explicitly including the visible
  cave approach and the first `12 m` of the tunnel, and draws each ridge as a toe-to-outer-foot
  hatch while carrying only the visible narrow river approach into its
  mountain mouth. The hidden cave continuation is not drawn as open map space.
  The open portal uses a fixed `19 x 17` uncrossed arch
  marker with a localized hover label; when its world position is
  outside the scrolling viewport, the marker clamps to the visible edge as a
  direction indicator. North and east keep the layout's original map maxima.
  It also draws the canonical Route 01 loop as a pale neutral line below the
  darker bone-toned player itinerary, adds five numbered stop markers in the
  default layout with localized hover labels and keeps both symbols in a
  compact legend. The map deliberately has no live bus marker. With the
  ordinary area tabs, City and Mountain Road can each be inspected as a
  separate schematic; the player marker is drawn only on the current area's
  tab. An ordinary observational `XYZ` mode is available on both tabs through
  the panel button, keyboard `C` or gamepad north/`Y`. A click selects the
  point under the pointer; Left/Right or D-pad Left/Right cycle the complete
  active-area catalog, persistently highlight the selection and automatically
  centre keyboard-picked points. The side panel gives the localized point
  name, its area and invariant world `X/Y/Z` to one decimal place.
  City's catalog owns every `BuildingLot`, every open-area arrival, every bus
  stop, the current player, the city mountain-tunnel portal and the boat
  station hut. Special lots replace rather than duplicate their generic lot:
  the bar exposes `ReturnPosition`, home and supermarket expose `Center`, and each
  district POI exposes its authored position. Mountain Road owns the current
  player, tunnel, every authored hairpin apex, bridge centre, plateau endpoint,
  cafe, cableway and brink. Road and itinerary polylines, intermediate route samples
  and mountain hatches remain decorative. Point inspection is mutually
  exclusive with debug teleport and suppresses route editing, area travel and
  teleport confirmation until the player exits `XYZ` mode. Confirming the
  other area requests a map arrival, Single-loads the black
  `AreaLoading` scene, advances its progress bar and only then Single-loads the
  destination. The source area is therefore unloaded before the destination
  world is composed: City and Mountain Road are never resident or rendered
  together. Mountain Road is drawn as its exit tunnel, complete winding route,
  all ten authored hairpins, distinct mountain bridge, enlarged endpoint
  terminal and surrounding mountain hatch. Hairpins and bridge come from the
  same pure route plan used by the world. The terminal plan also supplies
  distinct localized cafe, cableway and brink landmarks; the map does not
  infer them from runtime GameObjects. The physical terminal
  keeps a clear `7.5 m` vehicle circle on its irregular roughly `42 x 27 m`
  plateau. On the left, one five-sided Nighthawks-inspired glass cafe is
  enterable without a scene load. Its lone patron, neighbouring couple and
  attendant are four dedicated staged models rather than pedestrian-pool
  substitutes. Its deterministic fixed-metre Blender set contains `61` meshes /
  `5,794` triangles, `52` semantic anchors and seven dynamic prop assemblies.
  The rear service wall now reads as one kitchen run: an extended cabinet and
  worktop with `CuttingBoardDock`, a compact stove and pan at `StovePanDock`,
  and a refrigerator cavity with two shelves. `FridgeDoor` is the sixth
  dynamic prop, rooted at the authored `FridgeDoorPivot` with child
  `Grip.FridgeDoor`; it remains the sixth prop, ships closed and has no runtime
  driver, Animator, Rigidbody or attendant/player interaction. The napkin
  dispenser, sugar shaker and salt shaker have moved away from the hero's
  counter place so they no longer occupy the menu handoff area. The seventh
  prop is a thin open `Menu.Hero`, with its own hand grip, counter dock and
  three item/one selection anchors; it begins hidden and is presented only by
  the cafe menu runtime. At runtime its `menu_pages` role uses plain warm paper
  rather than the green-banded shared props sheet, and the TMP readable face
  and horizontal glyph direction are resolved against the actual focus camera.
  Seven stools follow the main counter and return with their seat tops at
  `0.8175 m`: three are occupied with real butt contact and four remain empty.
  The hero may take the middle main-row gap; its dock remains in the aisle while
  its independent facing now looks at the counter. The lone patron nearest the
  entrance sleeps with his head on strongly crossed forearms, visibly stacked
  one above the other without intersecting, on the counter. He owns no
  cup, never drinks and never enters the attendant's service queue. Only the
  pair's two cups visibly drain during their Drink clips. Both handles face the
  side opposite the earlier build; each refitted Grip stays exactly on the
  animated hand while the open rim tips toward the owner's live mouth socket,
  independent of imported bone axes, then returns exactly to the centre of its
  own saucer. The attendant's Wipe reaches the real
  counter surface, while the complete right hand and pot stay clear through
  carry, lift and action blends. During Pour, separate runtime geometry is
  rebuilt every frame from the animated pot spout to the active cup's
  `PourTarget`; the stream is not baked into any clip. A pure clock arms on the
  player's first entry into the cafe's `16 m` entrance radius, which excludes
  every earlier hairpin. Role-local windows and distinct fill/consumption
  profiles and non-overlapping windows keep the pair out of lockstep while the
  first visible sip still crosses the refill threshold within one minute. The
  attendant wipes between episodes, then walks and pours when either of those
  two cups crosses that threshold. Completing
  the hero-stool sit switches to a bounded eye-level first-person view of the
  counter and restores the exact prior follow-camera state on exit. It also
  requests one silent attendant handoff: `MountainRoadCafeMenuController`
  advances its pure model from delivery to open only after the physical menu
  reaches `MenuDock.Hero`, while `MountainRoadCafeMenuPresentation` keeps the
  booklet and its localized TMP text in world space. When
  `HeroMenuPlaced` becomes true, the existing `MountainRoadCafeSeatView`
  blends the fixed camera over `0.45 s` along the current seated sight line
  to a `40`-degree view `0.50 m` from the page. The close-up preserves
  world-up without roll and suppresses every look source. `W/S` (or the D-pad)
  still wraps
  through the three items and `Space`/gamepad West confirms; the ordinary
  `E`/`Enter`/gamepad South interaction remains available for standing.
  Confirmation keeps the visible `X`, releases focus back to the saved seated
  view and idempotently requests retrieval. Standing instead restores the
  exact pre-seat follow-camera state immediately and requests retrieval with
  no committed item. The shared attendant timeline serializes that return as
  `WalkToMenu -> TakeMenu` (`2.5 s`) `-> CarryMenuBack`; the booklet remains
  at the counter until the physical take, follows the hand and is hidden at
  its service dock. The handoff/return is one-shot for the scene and creates
  no order, product, payment, inventory item, food, drink, dialogue, reaction,
  sound or story state. The
  ten-clip cast (`2` lone-patron clips, `2 + 2` pair clips, `4` attendant
  clips) has no NPC voice bed; the hero never enters its pair-only cup/refill
  queue. Only while the player is
  inside the plan's physical cafe volume, the pair follows the fixed localized
  text order `Man01 -> Woman01 -> ... -> Man10 -> Woman10 -> loop`; each role
  owns ten stable keys in both Russian and English. A queued turn survives
  either patron's Drink and the woman's cigarette lift/smoke window without
  consuming or skipping its key, then resumes after the authored action. One over-head bubble
  belongs to the active speaker, whose additive neck/head look turns toward the
  partner before the line appears. A PairMan -> PairWoman exchange completes
  only when both lines have been fully displayed. After every third completed
  exchange (`3/6/9...`, continuously across the ten-pair pool wrap), and once
  both mutual looks have returned to idle, the lone patron — the
  woman's strongly drunk husband — raises his head, waves his right hand toward
  the pair, shows one line from a separate four-key RU/EN pool and returns to
  the exact sleeping seam. The pair completely ignores him: it receives no
  look, answer or reaction gesture, and its pending ManNN key is unchanged
  (`Man04` after `Woman03`, `Man01` after `Woman10`).
  The attendant has no lines. The pair's default seated idles remain legible around the conversation:
  the man makes
  three uneven left-hand taps with no impact sound and may continue tapping
  beneath his text, while the woman takes one cigarette drag and settles into a
  restrained exhale. The authored ember glow follows the drag; a separate
  world-space plume follows `SOCKET_Mouth` through the exhale window. Both read
  the same live normalized idle phase and own no separate timer, Light or
  AudioSource. The bubbles likewise add no voice or
  other AudioSource. Her fingers hold the filter rather than the ember; the
  filter still reaches the mouth and the burning tip points clear of hand and
  face. The
  plan continues to own entry,
  shelter, anchors, lighting and exactly `17` logical colliders. The existing
  two visible practicals and one shadowless technical wash are redistributed,
  not multiplied: the warm key reaches the sleeping husband, while
  `Light.ColdService` now starts inside the visible task fixture over the stove
  with a cold emissive lens. Its widened bisector cone contains the stove and
  pan task surface as well as all four figures, where it works with the wash to
  keep the tableau readable. These remain the cafe's three runtime Lights and
  still miss the terrace, parapet and black brink. Six neutral semantic
  detail sheets partition exterior, interior, counter, metal, props and glass;
  authored UV regions and a zero-overlap validator prevent repeated samples,
  stretching and flicker without a new base hue, readable brand, `PHILLIES`,
  `5¢`, price, city background or object text beyond the menu's exactly three
  localized item names.
  The four cast members also use role-specific `256 px` detail atlases and
  curved/multi-segment geometry at the current Hero V2 fidelity level; these
  atlases cover face, clothing, hair/headwear and shoes through the shared
  player lit material rather than adding per-instance materials.
  On the right, a `230 m` continuously looping cableway moves eight
  colliderless cabins over nine supports while its upper return remains beyond
  the Mountain Road draw range. The cafe interior and lower station participate
  in the shared weather-shelter query. The rest of the pad is a dressed
  transfer yard of `85` batched parts on existing sheets and existing tints:
  a ploughed snow bank and grit bin, the last road board and a seized
  barrier, winter furniture on the cafe threshold, a freight dock with one
  abandoned suitcase, and a `0.66 m` retaining wall whose two three-riser
  flights climb to a back terrace. A `1.02 m` parapet closes that terrace
  `0.35 m` inside the rim, carrying a sittable bench, a survey pillar, a
  memorial plate and a windsock mast, with one chained gap. Past it the
  terrain is cut `26 m` down through a `-27` degree wedge — the one sector
  the ridges leave clear inside the `120 m` far plane, so none is moved and
  both flanking masses become its jambs — and a fixed matte at `81-105 m` on
  the lighthouse island's shaders shows the valley bed, the switchback he
  climbed and a grain of city, all measured from the tunnel mouth's own
  height and lit after dark with no Light at all. One mercury practical burns
  over the freight dock and the brink stays dark. The Ferryman answers on the
  summit from a second repertoire that offers nothing, the road having
  ended. With the
  test teleport enabled through the City F9 toggle or the Home F9 arrival,
  every map lot becomes selectable,
  the side panel asks for an explicit confirmation and a
  confirmed target moves the hero to that lot's street-front return point or
  its nearest generated route when no frontage edge exists. City arrival
  rejects both building footprints and the church-yard fixtures.
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
- a dedicated `3.15 s` `DoorTransition` scene after that source-scene gesture:
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
- one clickable 3D near-black cat — the game's last sprite conversion — sits
  with its back to the camera on the `Middle Landing Back Rail`. It has no
  armature: the actor articulates authored pivot empties, so the untouched
  deterministic idle timeline still breathes, flicks the tail, twitches an
  ear and grooms roughly every 36 seconds, while a continuous hysteresis
  head-yaw model (65° clamp) tracks the player. A default-hidden Cheshire
  grin — a tooth crescent wider than the head on an arc-length reveal
  shader — can draw itself in from the center outward while the head turns
  over the shoulder to the live camera; only a future script triggers it
  through `StairwellInteriorRoot.CatGrin`. Activating the shared
  `IInteractable` path opens a localized default-Talk `Talk`/`Interact`
  target menu: Talk preserves the existing temporary cat response, while
  Interact checks the run inventory for one open stew can and either shows a
  localized missing-item thought or opens a default-No `Feed the cat?`
  confirmation;
- one reusable inventory-backed target-interaction model and controller own the
  pure item requirement, `Choice -> Confirmation -> Executing -> Closed`
  states, pointer/keyboard/gamepad choices, shared modal lock, temporary prompt
  feedback and lifecycle cleanup. A confirmed handler prepares every visual
  resource before `GameSessionState` atomically removes the required stack;
  failed preparation or a stale requirement consumes nothing. The stairwell
  cat is the first adapter: accepting the feed consumes exactly one
  `OpenStewCan`, visibly walks the ordinary 3D hero to an authored middle-shot
  entry pose and samples `CatFeedEnter`, `CatFeedLoop` and `CatFeedExit` on the
  continuous world rig. The cat keeps its independent 16-step `6 fps` feeding
  timeline, now posed as a head-down eating dip with chew alternation. The
  track starts with the player's loop, pauses ordinary cat idle/look and
  restores the hero, cat, contact shadow, input, HUD, camera and modal
  ownership after normal completion or abnormal cleanup;
- one deterministic `22 x 16 x 4.8 m` bar interior with seven authored zones
  and four validated circulation paths. Its visible permanent environment is
  the passive fixed-metre `bar_interior_v3` Blender asset at generator `3.1.0`
  (`179` semantic meshes / `12,804` triangles, signature
  `f7e7ada5e36bf24a505efcb710d3e2c724d9bc1bbfc2ca557042f1915ac85cce`):
  a long dark panelled counter with a right-hand
  return, taps and brass foot rail, a mirror-and-bottle backbar, three booths
  with a snug, four small round pub tables, a reduced music pocket, heavy
  curtains, worn carpet/plank and low warm practicals. Unity retains the plan,
  collision, lighting, state and interaction authority rather than rebuilding
  the furniture from runtime primitives. The Residential identity and
  `SplitTheG` dressing remain without turning the British-pub reference into a
  flag, brand, readable advertisement or new lore. Its visible counter top is
  now `1.16 m` rather than `1.56 m`, matching the plan's `0.50 m` centre /
  `1.00 m` height. The hero stool top is `0.96 m`, the authored eye and look
  target are `1.76 / 1.86 m`, and the menu/vessel docks track the surface at
  `1.185 / 1.175 m`. Fifteen deterministic `1024 px` source albedo families
  import at `512 px`; all non-emissive interior parts use all fifteen
  recognized sheets, while service parts use five. Their measured world-metric
  UV scale survives import and the same textures feed the `.blend` preview
  nodes. These are diverse material albedos with scalar surface response, not
  a separate set of PBR maps;
- six shadowless practical light pools, a bar-only Bloom/color/vignette/grain
  grade, local dust, a slow ceiling fan and a skippable `1.35 s` single-camera
  Bezier reveal establish the interior without changing the chase-camera
  contract or the fog-free `220 m` bar range;
- one separate `16 x 11 x 3.6 m` `SupermarketInterior` whose fixed shell,
  profiled shelf bodies, recessed cold cabinet, checkout, stockroom facade,
  fluorescent housings and articulated CCTV pivots are one deterministic
  passive Blender asset. The validated layout plan still owns protected
  aisles and the placement data for collision, product slots and interactions;
  runtime composition owns the actual colliders, finite-product lifetimes,
  practical lights and interaction objects. Five trimmed counter-clockwise
  skirting sections bury their rear and bottom faces `3 mm` into the wall/floor
  and meet without coplanar corner overlap. Its
  decorative checkout is staffed by `supermarket_cashier_v1` — a passive
  ordinary-proportioned `1.75 m`, `40`-mesh / `1,244`-triangle 3D clerk on the
  shared `NpcHumanV2` 31-bone Hero V2 Avatar at a `0.835 m` rest pelvis. He keeps the former uniform, detail atlas,
  attentive face, planted hands and hunch, but his ordinary neck never scales:
  only the eyes and head follow the hero through a bounded `28°` turn while
  the complete body remains behind the register. He retains the rare blink and
  a separate `E — talk` placeholder. The former `watcher_cashier_v1` with its
  five-segment `18 m` pursuit neck remains a named model/prefab asset but is not
  provider-reachable or instantiated in ordinary gameplay. Four chunky
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
  selected model's visual bounds. Renderer-only owner leases hide the hero and
  active cashier during this view without disabling either gameplay root, then
  restore every renderer's captured state on all exit paths. Muted clickable
  arrows sit directly beside the product instead of adding another footer hint;
  keyboard and gamepad use the same navigation path;
- one deterministic passive `supermarket_product_pack_v1` Blender asset owns
  the six generic, unbranded and text-free product models shared by the shop,
  inventory previews, Home refrigerator and cat-feeding flow: instant noodles,
  day-old loaf, vodka bottle, closed stew can, open stew can and chicken egg.
  Its `33` meshes / `2,276` triangles feed an asset setup responsible for one
  Resources prefab per item; runtime supplies selection collision and lifetime.
  The three shop shelves contain exactly one finite physical unit of each of
  only five offers: noodles and loaf, vodka and closed stew, plus the egg on
  the cold shelf. Their
  bottom-centre pivots sit on exact authored tier anchors instead of floating
  or overhanging. The `0.46 m` vodka source uses a `0.37 m` shop fit envelope on
  the unobstructed third/top tier and remains below the shelving-unit top during
  selection; closed stew occupies the first tier. The open can's world source
  remains the Home/cat flow, and it does not add a sixth store offer. Confirming a purchase
  atomically deducts its integer price, adds one matching inventory item and
  commits the product's stable world-source ID. The bought model and collider
  disappear immediately, and the source filter keeps that shelf position empty
  after leaving and re-entering until `BeginNewGame`. Failures for insufficient
  cash, a full stack or an already-bought source mutate nothing. The product's
  child price tag disappears with it instead of remaining on an empty shelf.
  Closed stew is
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
  can of stew, instantiated from the same passive six-item product pack used by
  shop and inventory presentation. Each occupant owns stable catalog metadata,
  registered renderers and a tight non-blocking selection trigger. A successful `Take`
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
  window and complete passive pub-exterior appearance
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
  separate optional `smoking_theme` music starts from silence once the
  mixing rule clears the apartment theme, and leaves through the same shared
  `4 s` fade-out rather than the camera ramp; without a supplied clip, the
  complete interaction remains playable and silent;
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
  that edge, holds neutral for one frame and plays the `3.75 s` `BedEnter`
  on the continuous rig — the one contextual action authored on eased curves
  with staggered keys, so the pelvis and legs lead each landmark and the
  chest, head, arms and face reach it a few frames behind. A dedicated seated
  pelvis waypoint holds the character on the mattress edge with both feet
  grounded before movement can continue between the standing dock and bed
  centre. The
  deterministic `BedSleepLoop` repeats with the existing breathing holds until
  a second `E` plays the `6.0 s` `BedExit`; the opening can begin directly in
  that loop and apply its one-shot wake-duration multiplier. Waking is a
  four-beat sit-up rather than a roll: he curls onto his elbows into a
  half-crouch on the mattress with both boots drawn under him, drops the right
  leg over the near edge, then the left, and only then stands. Per-sample pelvis alignment
  keeps the sleeper at the authored bed action anchor, with the head at the
  `xMin` pillow. Both hip anchors are the mattress top plus a measured body
  offset rather than a clearance guess: the generator reports how far the
  supine back, the lifted head and the seated weight hang below the pelvis
  bone, `PlayerCharacterDimensions` mirrors those numbers, and the pillow,
  blanket and crumpled shirt are placed around the pose instead of through it.
  The mattress and pillow tops are deformable vertex grids: they dent under
  the sleeper by his parts' actual penetration into the rest plane, his hip
  target descends by the same sink depth so he lies in the dent rather than
  hovering over it, and the dent visibly refills over about a second and a
  half once he is up — the bed behaves like thick cloth, not a box. Sitting
  on the edge dents nothing, because that pose is pinned by both boots on
  the floor.
  Only a normally completed `BedExit` resets session fatigue;
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
  through the shared pedestrian presentation;
- one dedicated full-body `bar_bartender_v2` occupies the authored bartender
  anchor. `BarBartenderProvider` selects the rebuilt
  `Assets/Bar/Bartender/Prefabs/BarBartenderOrdinary.prefab` and retains the
  former six-armed prefab only as an inactive legacy reference. The active
  `1.75 m`, `39`-mesh / `1,136`-triangle ordinary two-armed publican wears a
  dark-green waistcoat, rolled sleeves and apron and reuses
  `CafeAttendantWipe`, `CafeAttendantWalk`, `CafeAttendantPour` and
  `CafeAttendantNotice`; `BarBartenderServiceChoreography` binds the right hand
  to the selected bottle and the left to the physical menu or vessel while the
  existing service timeline remains authoritative. Ordinary one-bottle service
  is live; multi-ingredient mixing and multi-bottle choreography remain
  deferred;
- a scene-local spatial crowd bed plus rare glass/chair cues consume their
  layout radius/gain data and coexist with the existing bar music and
  procedural ambience inside a four-source budget;
- one exit and one ordinary-drink counter station remain authoritative; the
  activity fixture (beer-pong table, stage) survives purely as layout
  dressing. The bar-visited mechanic is removed entirely: the map route is
  edited only by hand and entering a bar changes nothing about it;
- an `F9` debug window in `City`, `BarInterior` and `MountainRoad`; opening it closes a
  conflicting map before taking the modal lock; clickable controls or the
  Left/Right arrow keys change the session
  intoxication by `-20/+20`, clamped to `0–100`, without changing the
  last-drink or consumed-drink context. Seven direct buttons select displayed
  game days `1–7` while preserving the current `HH:MM`, running state and
  needs; the ordinary calendar itself remains unbounded. A committed physical drink service
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
  stock and the bar's nine-item drink catalog. The bar now uses the same
  physical counter-seat and `CounterMenu` behavior as the Mountain Road cafe:
  the world rig walks to the authored stool and sits, the bartender carries an
  open booklet from the passive `bar_service_props_v1` `1.2.0` library
  (`29` meshes / `2,280` triangles, signature
  `4c98dce2cdfd017922c236f88849862f8823bd000380b62a26601dbc744c0026`)
  to its dock. The shared model/input/page/focus/hint/prop-motion layer opens
  the bar spread over `0.45 s` at `1.10 m` and FOV `60`; the cafe keeps its
  independent `0.50 m`, FOV-40 framing.
  `W/S` or D-pad wraps selection and `Space`/West confirms; `E`/`Enter`/South
  leaves the seat through the physical exit. The bar adapter fills five rows
  on the left page and four on the right with the nine localized drink names
  and their fixed prices, while the cafe adapter remains three selection-only
  item names. A rejected bar purchase leaves the booklet open with the existing
  failure feedback. A successful confirmation atomically deducts the price,
  leaves `X`, restores the seated view and physically returns the menu before
  the existing one-bottle service: the right hand picks up and tilts that exact
  bottle, a world-space stream fills the matching reusable 3D tumbler, pint,
  wine glass, shot glass or snifter, and the hero's left hand holds it at the
  mouth for an exact three-second drink before returning the empty vessel.
  Completing an order reopens the same physical menu for another selection;
  exiting restores the exact pre-seat camera and world rig.
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
  bed, lightning flashes, thunder, wet street film and puddles:
  weather-driven ambient lighting or grading changes, wind-driven debris and
  volumetric light shafts.
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
  future bar activities start from a new design. The cemetery gravedigging
  acts are the first minigames built on that footing — city-side, and on the
  surviving `BarMinigameModalLock` rather than the removed catalog.
- Multi-ingredient cocktail ordering, mixture state/UI and multi-bottle
  choreography. The active ordinary bartender, physical menu and ordinary
  one-bottle service are already present; the former six-arm return chord is a
  superseded legacy proposal, not the upgrade path.

South City Rollers/Skaters is a design reference only for procedural-world and sprite-character approaches; its code and assets are not present in this repository.
