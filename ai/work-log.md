# Work log

Entries are reverse chronological. Record outcomes and verification, not a transcript.

Entries from months before the previous full month live in `ai/archive/`;
see [`ai/README.md`](README.md) for the retention rule.
Earlier entries: [`work-log-2026-07.md`](archive/work-log-2026-07.md).

## 2026-08-17 — Cemetery: grounded оградки and truly sittable benches

- User-reported: the grave enclosures visually hovered — the rail band
  sits at knee height (0.24-0.66 m) with nothing carrying it. Each
  enclosure now stands on four grounded corner posts
  (`0.07 x 0.68 m`, ids `rail-post-{a..d}`), and the planner test
  asserts exactly four posts per enclosure with bottoms on
  `GroundTopY`. Part budget re-derived `480 -> 560`.
- The alley benches joined the shared bench-sit system instead of
  being scenery: `CityWorldResult` now carries the nullable
  `CemeteryPlan`, `CityBenchSitPlan.CreateAll` takes it as a required
  parameter and reads one `CityBenchSeat` per `cemetery-bench-*-seat`
  part (top centre, plank size, `GroundTopY`, facing from the part's
  own rotation) — the same read-back-from-the-plan contract as the
  bar-side yard bench, docked through `ResolveSeatDockGround` onto the
  alley edge. `CityGameRoot` passes `World.CemeteryPlan`; the NPC
  bench-rest planner sees the seats too but graph reach naturally
  keeps roamers out of the cemetery.
- Tests: `CityBenchRestTests` call sites updated to the new signature;
  `CityCemeteryPlannerTests` gained the enclosure-post assertions and
  `DefaultCity_CemeteryBenchesJoinTheSittableSeats` (one sit offer per
  drawn seat plank, inside the grounds, pelvis on the plank at
  `0.49 m + SeatClearance`).
- Verification: bundled-dotnet compiles green; one focused batchmode
  run of `CityCemeteryPlannerTests` + `CityBenchRestTests`.

## 2026-08-17 — Cemetery follow-up: the lamp chain and alley benches

- The three fixed lamps became a chain along the whole main alley: the
  symmetric gate pair stays, then one lamp per `LampSpacing = 15.4 m`
  on alternating sides, plus a far-end lamp when the chain stops more
  than half a spacing short of the fence (default city: 6 lamps on the
  long orientation, 4-5 on the short one). `TryAddLamp` now also
  rejects spots on the gravel itself (alley-overlap check) and lamp
  IDs are `D2`-padded so ordinal order stays lexical.
- New `Bench` part kind + `Timber` style: a painted-plank bench with
  iron legs (4 parts) beside the main alley just before each cross
  alley and one near the far fence, facing the gravel. Benches prefer
  alternating sides and flip across the alley when a lamp already
  holds theirs; their footprints join the lamp footprints in one
  `reserved` list that graves and trees now avoid (renamed from
  `lampFootprints`).
- Validator: a full cemetery now expects `3-9` lamps.
  `CityCemeteryPlannerTests` asserts `4-9` lamps, a `> 12 m` lamp
  spread along the alley (not clustered at the gate) and `>= 8` bench
  parts. Art bible §10c, README, overview, systems map and release
  notes updated to the chain-plus-benches picture.
- Verification: bundled-dotnet compiles of Runtime + EditModeTests
  green, then the focused `CityCemeteryPlannerTests` batchmode run
  (deferred behind a lockfile watch until the interactive editor
  closed). The first run caught a real defect: the new lamp-vs-alley
  overlap guard rejected every lamp, because at the old
  `half + 0.45 m` edge offset the lamp footprint grazed the expanded
  alley band exactly at the seam. Lamps moved to `half + 0.65 m` off
  the gravel; second run all green (7/7).

## 2026-08-17 — The cemetery gets its own module, variety and light

- Extracted the cemetery out of `CityOpenAreaDecorationPlan` (which
  kept the lake and bar-side yard; its budget dropped `420 -> 260`)
  into a dedicated conventional triad: `CityCemeteryPlan` (oriented
  part descriptors with rotation — the open-area AABB descriptor could
  not carry tilted crosses or swung gate leaves), `CityCemeteryPlanner`
  (pure, `StableHash`-seeded, `ValidateOrThrow`), and
  `CityCemeteryWorldBuilder` (48 m chunk × style batches via
  `CreateCombinedOrientedBoxes`). Budget `480` parts; the default city
  plans ~`420`.
- The planner works in a gate-relative depth/lateral frame so one
  algorithm serves all four gate orientations: main gravel alley from
  the gate plus cross alleys every `20 m` (chunk-split slabs); a
  jittered grave grid (`4.0 x 5.0 m` pitch, `48%` hash acceptance)
  with six monument variants — the first six accepted plots cycle all
  variants so the gate row is a showcase and the contract is testable —
  three stone tints, back-tilt up to `6°` in the rows deepest from the
  gate, `35%` оградка enclosures and `25%` offerings; fence ported
  intact plus four corner pillars; the gate gained a `2.4 m` pillar
  pair, an iron arch with plaque (overhead parts are exempt from the
  approach-clearance rule by a `2.1 m` bottom-height test) and two
  nearly-open lattice leaves whose `8°` opening angle keeps their
  lateral reach inside the `0.35 m` margin to the expanded approach;
  hash-thinned perimeter/interior birches and firs and grave-side
  bushes; lamps are planned first so graves and trees avoid them.
- Trap fixed along the way: `CityOpenAreaAccessDescriptor.
  OutwardNormal` points from the street *into* the grounds despite its
  name (the lake and the original cemetery pass both read it that
  way); the first draft inverted it, which put the gate on the far
  side and walled off the real approach — caught by `ValidateOrThrow`
  in the first test run.
- Three alley lamps follow the island-floodlight recipe (emissive
  mantle + `CityNightGlowRegistry`, `CityLightHalo`, point light
  `LightShadows.None` + `CityNightSiteLightRegistry`), so they die by
  day; the pole is the only collider.
- New deterministic texture pass `tools/build-cemetery-textures.py`
  (engine-imported from `build-home-textures.py`): four validated
  1024² sheets — low-contrast speckled granite (planar-XZ UVs smear
  vertically on monument faces; the quiet sheet reads as weathering),
  cracked/lichened stone, pebble gravel, leaf-litter soil — with
  manifest `ArtSource/City/cemetery-textures.json`.
  `CityCemeterySurfaceAppearance` transcribes the solved recipes
  (compensations `1.398/1.397/1.4055/1.4755`) and applies them to
  combined meshes over `RuntimePrimitiveFactory`'s world-planar UVs
  (no per-renderer UV offset needed — world position decorrelates).
  "Cemetery Ground" in `CityWorldBuilder` now carries the soil sheet
  the way roads carry asphalt. Hand-authored the four texture `.meta`
  files from the POI template (512 import, Repeat).
- Art bible gained §10c «Кладбище — город, который никуда не спешит»;
  release notes, project overview, systems map and system tree
  updated.
- Verification: bundled-dotnet compiles of Runtime + EditModeTests,
  then one focused batchmode EditMode run —
  `CityCemeteryPlannerTests` (determinism, `>= 30` graves, all six
  variants and three tints, gate dressing counts, slab non-overlap,
  approach clearance, textured build, day-dead lamps) plus the
  trimmed `CityOpenAreaDecorationPlannerTests` — all 10 green.

## 2026-08-17 — Last route island: the inner route ring joins the paving

- Reversed the one texturing exception on the island: the `7.2 m` inner
  route ring was left a flat painted marking band, but between the
  textured platform and the textured centre disc it read in-game as a
  missing texture rather than as paint. It now carries the paving sheet
  (`CylinderCapXZ`) under its existing dark `NightlifeFrame` tint —
  paint over the same paving — so the island is textured with no
  exceptions. `Build_TexturesTheLastRouteIsland` paving count `2 -> 3`.
- Verification: headless runtime and EditMode-test compiles green; the
  EditMode run itself was blocked by an open editor session.

## 2026-08-17 — The weighbridge is attended, and the scale answers weight

- The Industrial cold weighbridge received its authored pair — the last
  of the four canonical POIs to get residents — built strictly on the
  babushka mould (`Plan`/`Presentation`/`Factory`/`Provider` in a new
  `City/Weighbridge/`, staged model outside `Resources`, passivity
  guard, attention magnets at `1.60 m`). The weigher (palette 0) stands
  beside the mechanism at recipe-local `(3.05, 1.60)` — beside the
  axis, never across it, per the art bible's not-a-checkpoint rule,
  now also a guardrail test — looping a `6 s` check: crane up at the
  dial, lean to the linkage, crouch and chalk the deck edge (the chalk
  marks get their author; `ACC_Chalk` is role-enabled like the
  babushka props). The weighed worker (palette 2, `0.97x`, `+0.85 s`)
  paces the deck's long axis on the deck top with his corridor
  position slaved to his clip's normalized time
  (`EvaluateCorridorProgress`, pure), so pose and travel cannot drift:
  one `12 s` loop = one half round trip, direction flips on iteration
  parity, and normalized `0.36-0.64` holds him square and still at the
  deck centre — cross-commented on both sides of the python/runtime
  boundary like `StrikeNormalizedTime`.
- The scale answers weight. `BuildWeighbridge` registers the `Scale
  Needle` transform in `CityWeighbridgeIndicatorRegistry` (carpet
  registry pattern; City build only — the Home vista never claims the
  slot), and `CityWeighbridgeNeedleController` on the City root eases
  the needle off its captured authored `28°` rest by up to `34°` local
  roll while the worker's pause or the hero stands on the walkable
  deck (`TryDescribeWeighbridgeDeck` rect + foot band), exponential
  `0.45 s` attack / `0.90 s` release, and settles it back when the
  deck empties. Needle logic lives outside the NPC prefab: the
  attendants stay passive and are only polled (`IsWeighingNow`).
- Art: new staged `weigh_attendant` archetype (`842519`, `960` tris,
  exact `1.75 m` cap crown) — quilted grey-green jacket with seams,
  buttons and hip pockets, deliberately no authority markers — plus
  `WeigherCheck`/`WeighedPace` in the shared locomotion library
  (`18 -> 20` clips). `CityPedestrianModelImporter` taught the new
  model path (the miss surfaced as a real import failure: without
  `bakeAxisConversion` + avatar copy the root rest transform diverged
  and `ValidateDescriptor` threw — the explicit-list contract worked).
  `Rebuild Staged Weigh Attendant` menu builds prefab + provider.
- Verification: headless runtime/editor compiles green;
  `WeighbridgeAttendantTests` (stances in bounds, axis guardrail,
  needle registration, `0.30 m` obstacle sweep, deck-rect point tests,
  monotone attack/release easing, pause-window hold, provider binding)
  green; `DryingYardBabushkaTests` re-run green after the shared
  library regeneration; Blender build `CITY PEDESTRIAN ART BUILD OK`,
  determinism check passing, existing model manifests byte-identical.

## 2026-08-17 — Last route island: textured end to end, mast floodlight

- The island joins the drying yard as a fully textured public place. A
  fifth scripted POI albedo, `CityPoiPaperAlbedo` (new `poi_paper`
  grammar: paper fibre, faded print rows, creases, glue/bleach
  staining), covers the island's paper layer — totem map backing, torn
  posters, weathered route plates, schedule rows and the discarded
  timetable. Painted metal covers all fifteen canopy members, the mast
  group, the departure board frame, the bench base, the waste bin and
  the new floodlight metalwork; the empty bench seat is timber; the
  lost scarf and all six simulated canopy rags go through the cloth
  path (`ApplyClothPanel`, previously laundry-only); the island
  platform and empty centre disc are paved (`CylinderCapXZ`), while
  the inner route ring stays a flat painted marking band.
- The generator specs now transcribe the island tints (island paving,
  nightlife frame/waste/seat, rag and poster colours), so every sheet
  compensation re-solved: paving `1.422 -> 1.4205`, painted metal
  `1.4465 -> 1.479`, cloth `1.396 -> 1.4105`, timber `1.433 -> 1.445`,
  paper `1.4215`, all within the `8%` generator limit (worst `7.6%`,
  cloth). Existing sheet images are byte-identical — only tints and
  compensations moved in `ArtSource/City/poi-textures.json`.
- The island gained the second authored POI realtime light: an old
  service floodlight bracketed off the route mast under the broken
  totem (head at recipe-local `-2.40, 4.42, -1.05`), aimed across the
  empty centre at the empty bench. Cold violet-grey white
  (`0.80, 0.74, 0.92`) — the district's magenta/cyan bleached to a
  service tone, deliberately distinct from the drying yard's blue-white
  — same shadowless `72°` Spot family, range `16`, night intensity
  `150`, fog halo and boosted HDR lens, night-scaled through
  `CityNightSiteLightRegistry` so nothing electric burns by day. It
  adds no collider: the mast base already owns the island obstacle,
  so approach clearance is untouched. The Home vista rebuilds
  bracket/housing/lens geometry only. Documented worst-case realtime
  light budget moves `19 -> 20` (night only). Art bible: the island's
  "no glow of its own" is now scoped to neon — the mast floodlight is
  the one working electric fixture, serving the emptiness, not a
  stage.
- Verification: `python tools/build-city-poi-textures.py --verify`
  passes all five sheets. Focused Unity EditMode selection green
  `18/18` (`CityPointOfInterestSurfaceAppearanceTests` incl. new
  island coverage counts and island floodlight contract +
  `LastRouteCanopyRagTests`). PlayMode POI assertions updated — the
  public places carry the two named floodlights with their two halo
  particle systems, and the island's zero-emissive rule is now "the
  mast floodlight lens is the island's only emissive surface" —
  `CityNightPresentationPlayModeTests` re-run green `4/4`.

## 2026-08-16 — The swing is probed, not guessed: backswing over the shoulder

- Two in-game reviews in a row caught the beat swinging wrong — first
  into her own skirt, then "chopping" — because the arm keys were
  authored by analogy with other designs' poses, and the analogies
  lied: the pipeback "rim reach" actually puts hands down-back on the
  wheel rims, not forward. The fix was to stop guessing: a scratch
  Blender probe now loads the real generator, applies candidate
  `BonePose` keys to the built babushka rig and prints the world
  position of `hand.R` plus the rigid beater's world direction and
  tip. Six probe rounds established the rig's actual upper-arm axes
  (local X raises the sideways A-pose arm, local Z swings it forward,
  local Y twists the paddle) and produced verified keys, now recorded
  as coordinates in the pose comments.
- The final cycle: backswing `(70, -70, 12)` with a `-30°` wrist twist
  — hand beside the ear at `(-0.55, -0.36, 1.32)`, paddle swept back
  over the shoulder (direction `+0.90` on +Y, tip behind the back at
  height `1.29`) — then the forward whack `(18, 35, 60)` landing the
  hand at `(-0.59, -0.41, 1.31)` with the paddle pointing almost
  straight forward (`-0.96` on -Y), tip `0.94 m` in front at carpet
  height `1.16`; recoil bounces off the cloth and the lift arcs back
  up through the front. Art, prefab and tests re-verified
  (EditMode `26/26`).

## 2026-08-16 — The beat lands on the carpet, the carpet answers

- In-game review caught the first babushka cut whipping herself: the
  beater was authored hanging straight down (the A-pose width gate),
  so the strike folded it back into her own skirt. The carry
  direction is now forward-biased `(0, -0.6, -0.8)` — still inside
  the `1.65 m` envelope because it leans into -Y, not X — and the
  beat keys gained a real wrist snap (`hand.R` X `-42° -> +34°`), so
  the extended paddle lands out in front, on the carpet. Verified on
  the regenerated contact sheet.
- The carpets now answer the blows: in the city each is a simulated
  cloth panel pinned over the rack bar (6x6, stiff, damping `0.82`,
  textured with the Home rug albedo over its plain panel UVs, plus a
  static fold cap on the bar), registered in the new named-slot
  `CityDryingYardCarpetRegistry` and deliberately outside the
  weather-wind registry — heavy pile does not flap like laundry. Each
  beater's presentation fires a `0.16 s` decaying
  `externalAcceleration` pulse away from her whenever her loop
  crosses the authored strike moment (`0.28`), so the exact carpet
  she faces shudders under every whack, in her own rhythm. The
  balcony vista keeps cheap static carpet boxes.
- The third babushka no longer stands far off at the east edge: she
  strolls a cloth-free corridor between the rack and the west
  drying-frame posts, back and forth past both beaters at `0.36 m/s`
  with a smooth `220°/s` turn at each end. `BabushkaSmoke` was
  re-authored from a stationary `8.5 s` watch into a `4 s` four-step
  shuffle under emphatic left-arm talk — palm-open sweep, inward
  chop, open again — with the cigarette held ready at chest height
  and one drag per lap. The stance API now emits her corridor
  (`TryDescribeBabushkaStances` gained the path end), the plan
  carries per-stance path/speed/carpet wiring, and the stance tests
  sample the whole corridor against every yard obstacle.

## 2026-08-16 — Three babushkas populate the drying yard

- The drying yard gained its authored population: two grandmothers
  beat hung carpets with the classic Soviet plastic beater and a
  third stands apart at the east edge, smoking and watching. They
  are staged NPCs in the rider's mould — outside the pedestrian
  pool, colliderless with `PlayerAttentionMagnet`s, always present
  while the City lives.
- Art: one new `yard_babushka` archetype in
  `tools/build-city-pedestrian-3d-model.py` (seed `715233`, staged,
  38 meshes / 928 triangles, budget `900-2000`): housecoat, apron,
  skirt, rust headscarf whose folded crown owns the exact `1.75 m`
  envelope, felt boots — and both hand props on `hand.R`: the bright
  plastic beater (authored hanging straight down, because the A-pose
  envelope allows barely 5 cm past the fingertips on X) and a
  cigarette along the canonical `SOCKET_Cigarette.R` axis. The
  runtime enables exactly one prop per role.
- Two new authored loops join the shared locomotion FBX (16 -> 18
  Actions): `BabushkaBeat` (`1.5 s` — ear-height wind-up, forward
  rim-reach strike into the carpet, rocking recovery) and
  `BabushkaSmoke` (`8.5 s` — left arm folded under the right elbow,
  raise, held drag, chin-up exhale, weight shift). Both keep the
  feet planted and ride the ordinary walker sole bake. The first
  strike cut swung sideways out of the A-pose; the corrected keys
  reuse the pipeback rim-reach fold that provably lands hands
  forward. Verified on the regenerated locomotion contact sheet.
- The recipe grew the Soviet carpet-beating rack on the west strip,
  upwind of the wash: two painted-metal posts, a crossbar and two
  hung carpets textured with the shared Home rug albedo
  (`HomeSurfaceKind.Rug` — a hung carpet is the same object indoors
  and out), all with obstacle colliders proven outside every access
  approach. New `TryDescribeBabushkaStances` mirrors the bench-seat
  contract so the NPC plan and the drawn carpets can never drift.
- Runtime: `DryingYardBabushka{Provider,Plan,Presentation,Factory}`
  under `City/Yard` — pure stance plan off the POI descriptor
  (safe-absent for custom blueprints), one-clip manual PlayableGraph
  per instance with per-instance palette variant, playback speed
  (`1.0/0.91`) and phase offsets so the two beaters never strike in
  lockstep; spawn from `CityGameRoot` after the rider. Editor:
  staged descriptor + importer registration + `Rebuild Staged Yard
  Babushka` menu whose build also creates/rewires
  `Resources/City/DryingYardBabushkaProvider.asset`, closing the
  rider pipeline's manual provider-binding gap. The staged manifest
  validation was split from the wheelchair-specific checks it
  wrongly bundled.
- Verification pending Unity access (the editor was open through
  this session): the Blender build validates and is deterministic;
  runtime/editor/test assemblies compile; `DryingYardBabushkaTests`
  (stances inside the yard, opposed desynchronized beaters, watching
  smoker, rack presence + rug texture, approach- and stance-clear
  colliders, provider contract) plus the updated
  `CityPedestrianRuntimeTests` clip census (`16 -> 18`) need one
  EditMode run after the editor closes, and the editor must build
  the staged prefab (auto-queued or via the menu) before the
  provider test passes.

## 2026-08-16 — POI surface textures and the drying yard floodlight

- Four scripted opaque POI albedos join the facade/home/supermarket
  texture family: `tools/build-city-poi-textures.py` (importing the
  shared home pipeline) emits yard paving slabs (new `poi_paving`
  grammar), painted metal, laundry cloth (`linen`) and worn timber
  (`planks`) into `Assets/Resources/Textures/CityPoi*Albedo.png`,
  with the measured contract in `ArtSource/City/poi-textures.json`
  (compensations `1.422/1.4465/1.396/1.433`).
- `CityPointOfInterestSurfaceAppearance` (hash salt `5000`) applies
  them through property blocks on the shared primitive material. All
  four public grounds are paved with their district tints; the drying
  yard is textured end to end — frames, lines, posts, bench legs and
  floodlight metalwork on painted metal, the bench seat on timber,
  and the simulated laundry through a new `ApplyClothPanel` path that
  keeps the shared two-sided cloth material, matte specular and
  metre-tiles the panel's authored width/height (a new
  dimension-explicit `SurfaceAppearanceCore.CreateBaseMapTransform`
  overload, since skinned panels have no `MeshFilter`).
- The drying yard gained the one authored POI realtime light: a
  communal floodlight on its own `4.3 m` pole at the street-side
  corner opposite the shared bench (recipe-local `4.10, 4.55`),
  aimed across all three drying frames — a cold near-white
  shadowless `72°` Spot, range `16`, night intensity `150`, with fog
  halo and a boosted HDR lens that dies by day. The first cut ran at
  street-practical intensity `34` and read as unlit in game: spread
  over a `72°` cone with a `7-12 m` throw that is under half a street
  lamp's pavement level, invisible through the night grade, fog and
  PS1 composite (the always-on bar-side yard spot needs `240` for the
  same reason). Floodlight wattage is the honest unit for a beam this
  long. New
  `CityNightSiteLightRegistry` (glow-registry pattern, wired into
  `CityNightWorldResult.SetNightFactor`) scales the light and halo
  with the shared night factor and disables them below `0.02`, so
  nothing electric burns by day; the always-on bar-side yard
  spotlight deliberately stays outside it. The lower pole owns a
  focused obstacle collider proven outside every access approach;
  the Home vista rebuilds pole/head/lens geometry only. Documented
  worst-case realtime light budget moves `18 -> 19` (night only).
- `LastRouteCanopyRagTests` was stale from the laundry-cloth commit
  (it required every POI cloth to be a canopy rag, but the drying
  yard has hung simulated laundry since then and the test was not
  re-run); it now admits exactly the two cloth families.
- Verification: EditMode `14/14`
  (`CityPointOfInterestSurfaceAppearanceTests` — recipe/import/PNG
  contract incl. compensation-vs-builder-tints, apply path, salt
  separation from supermarket, per-site paving + drying-yard
  coverage counts, floodlight contract with night-factor
  scaling/disable and approach-clear pole collider, light- and
  collider-free vista + `LastRouteCanopyRagTests`).
  `CityNightPresentationPlayModeTests` POI assertions updated (the
  public places now carry exactly one Light — the floodlight — and
  only its halo particles) and re-run green `4/4`. The full EditMode
  suite's 14 unrelated failures reproduce identically on a clean
  HEAD stash and predate this change.

## 2026-08-16 — Cloth and wind: torn rags on the broken canopy

- Unity's built-in cloth entered the project the honest way: the
  `cloth`/`wind`/`physics` modules were already in the manifest, and
  since PhysX cloth ignores `WindZone` entirely, the wind is our own
  deterministic schedule instead. `GameWeatherRules` grew a pure
  `WindSample` path (slot-hashed bearing with the same smoothstep
  transitions as rain, strength from the slot's weather kind
  `0.15/0.40/0.65/0.95`, continuous seeded gusts at `7.3/1.9` game
  minutes and a `±9°` sway at `3.1`), sampled by
  `CityWeatherController` every frame before the visual-equivalence
  early-out, exactly like lightning.
- `ClothPanelFactory` builds skinned cloth panels at runtime — the
  project's first runtime `SkinnedMeshRenderer`: terrain-idiom mesh,
  one root bone, top row pinned through `ClothSkinningCoefficient`
  (`maxDistance` capped at `0.35 x height` as the explosion clamp)
  and torn hems as a pure hash of the variant.
  `CityClothWindRegistry` (glow-registry pattern) turns the wind
  sample into `externalAcceleration` (`7.5 m/s²` at full strength)
  plus gust/lift `randomAcceleration`.
- Double-siding was rebuilt after an in-game report of sparkling
  rags: the first cut duplicated reversed triangles over the same
  vertices, but cloth recomputes particle normals from EVERY
  triangle each frame, so the opposing windings cancelled the
  normals into glinting garbage. The simulated topology is now
  strictly single-sided; the back face renders through one shared
  cull-off clone of the primitive material (per-panel colour still
  on the MPB, smoothness/metallic zeroed so live cloth normals never
  catch specular).
- Six authored rags now hang from the Last Route island's broken
  canopy (city build only — the home-exterior vista stays
  cloth-free), and `CityRainField.SetWindDrift` replaces the
  hardcoded `x = 0.4..1.0` drift so rain in City and on the balcony
  leans the same way the rags blow.
- Two batch-mode traps burned and documented: cloth pauses while its
  renderer is culled, and in `-batchmode` a camera only truly renders
  into a `RenderTexture` — the simulation PlayMode test needs both;
  and `cloth.vertices` reports authored particle rest poses, so live
  deformation must be read via `SkinnedMeshRenderer.BakeMesh`.
- Verification: EditMode `19/19` (wind rules determinism/range/
  storm-vs-clear/boundary smoothness, factory mesh/pinning/torn
  variants, island rag presence + vista exclusion + registry count,
  rain drift alignment); PlayMode `2/2` — free hem moves, pinned row
  holds, all vertices finite. Storm-strength captures (removed after
  use) show five canopy segments with crumpled, wind-thrown rags and
  no geometry explosions.

## 2026-08-16 — Booth seating, booth scale and the jukebox move

- Three placement bugs fixed together. The seated-pair anchors sat
  at x −8.25/−8.75 — inside the booth tables (−8.77..−7.59) or the
  gap beside the bench (−10.64..−8.92) — and their z values missed
  the authored booth centers by up to 1.3 m; the pairs now sit ON
  the bench (x −9.7, z = booth center ± 0.55) facing the table.
- The booths themselves were furniture for giants: seat depth
  1.72 m and a 1.55 m back, with the cushion topping out at 0.77
  against the guests' 0.46 seat height. Now: one-seat-deep bench
  (0.78), banquette back (0.95), cushion top at ~0.47 so seated
  pelvises actually rest on it.
- The jukebox had been planted at (−9.72, 3.15) — the center of
  booth-3's footprint, unreachable inside the bench. It moved to
  the front wall east of the entrance (6.4, −6.78), rotated to face
  the hall, with its approach asserted inside walkable bounds.
- Verification: bar layout planner + surface suites `22/22`, bar
  smoke `1/1`; captures (removed after use) show pairs seated on
  the cushions at the tables and the glowing jukebox free-standing
  and approachable by the entrance.

## 2026-08-16 — Bar hall relight: the readability pass

- The first relight was too timid; the hall floor still sank to
  black. Three compounding causes fixed decisively: the bar's own
  post grade ran the same trap the Home grade once did (negative
  exposure −0.05 under contrast +9 — now +0.30 over contrast 5,
  vignette eased), the scene ambient/fill was shy (ambient to
  `(0.28, 0.20, 0.17)`, directional `0.72 → 0.95` at shadow
  strength `0.42`), and the floor albedo was a 5% mirror of nothing
  (`0.095 → 0.14` red-brown, worn-plank sheet regenerated with the
  new tint, compensation `1.485 → 1.4575`).
- Verification: bar surface + identity EditMode `9/9`, bar smoke
  and drink-service integration green, and the Home atmosphere
  fixture re-proved `2/2` standalone (its two batch failures were
  audio-listener log leakage between scene loads, not lighting).
  Before/after captures (removed after use): the plank floor now
  carries the pendant pools, wallpaper walls and every guest read
  across the hall, and the noir palette survives.

## 2026-08-16 — «Огонёк»: the Residential bar authored

- The bar by the hero's home has a name: «Огонёк» ("The Ogonyok"),
  replacing the literal placeholder. It is the first fully authored
  district identity — a bar for people without money.
- Texturing: `tools/build-bar-textures.py` emits four validated
  sheets entirely on existing home grammars (trodden planks, old
  wallpaper, tired dark veneer, upholstery rubbed to the weave);
  `BarSurfaceAppearance` (salt 4000) applies them and
  `BarInteriorWorldBuilder` dresses the floor, all five walls, the
  counter and its panels, the backbar, booth bases/cushions/backs
  and the stage — but only when the identity asks for the Worn
  surface set. Other bars keep flat tints untouched (asserted).
- Lighting: the bar scene gets the Home readability rule tuned
  darker (ambient floor ~×1.8, shadow strength 0.72 → 0.52) and the
  counter pendants now burn through the district identity — the
  «Огонёк» runs its bulbs a step warmer and 10% dimmer; the other
  identities keep the exact authored amber.
- New furniture: the coin jukebox by the stage in every bar — arched
  corpus, glowing amber panel, two glow tubes, speaker grille and a
  key row, with `BarJukeboxInteraction` as the interactive stub
  (prompt, use counter, panel flash, confirm cue; track selection
  over `BarMusicPlayer` is a later pass).
- Verification: bar surface contract, district identity and
  localization suites `16/16`; bar smoke + drink service
  integration `2/2`; temporary captures (removed after use) show
  the wallpaper walls, warm dim pendants, seated booth guests and
  the glowing jukebox with a patron standing at it.

## 2026-08-16 — Bar district split: the technical base

- The plumbing for per-district bar identities, values deliberately
  unchanged until the art passes author real differences.
  `BarDistrictIdentityCatalog` serves a `BarDistrictIdentity` per
  bar district (mood per the zone art bible — Memory / Household /
  AfterShift / Escape — display-name localization key, palette and
  light hooks, crowd density scale); every other district
  normalizes to the Nightlife fallback the direct-loaded bar has
  always effectively been.
- The district flows the whole way: `BuildingLot.District` →
  `BarEntrance.Configure` → `GameSessionState.EnterBar` (new
  `ActiveBarDistrict`, reset with the other bar state on
  home/supermarket entry and new game) →
  `BarInteriorLayoutPlanner.Generate(..., district)` →
  `BarInteriorLayoutPlan.District` / `.DistrictIdentity`, with the
  layout validator refusing non-bar districts. Four
  `bar.district.*` name keys landed in both localization catalogs.
- Verification: `BarDistrictIdentityTests` (catalog coverage and
  distinctness, normalization, plan threading incl. the legacy
  entry point, session lifecycle) plus localization and bar layout
  suites passed `28/28`; the bar smoke passed `1/1` on the
  fallback path.

## 2026-08-16 — The bartender pours: service pass landed

- Pass 3 of the bartender spec. The bottle never flies to the hero's
  hand anymore: `BarDrinkShopController` retires the first-person
  right-arm grip (the hero keeps only the left-hand drink lift) and
  carries the committed bottle from its shelf to the authored
  `BottlePourPose` with a small lift arc — the same timeline
  channels, a different destination. The vessel no longer scale-pops
  at the counter: it slides in flat along the brass from past the
  left edge of the seated frame (`VesselSlideEntryOffset`,
  `VesselVisibility` is the slide) before the pour fills it.
- `BarBartenderServiceChoreography` puts his hands on all of it:
  the brass-banded mid-right chain CCD-rides the carried bottle,
  the mid-left chain rides the sliding vessel and steadies it
  through the pour, and while the hero merely browses, the lower
  pair reaches back and fingers whichever bottle is hovered —
  arms as readers of the authored motion, never drivers. Idle
  amplitudes roughly doubled after the first pass proved invisible
  at hall distance.
- Verification: drink-service integration and the bar smoke passed
  `2/2`; the three `BarDrinkPhysicalShopPlayModeTests` failures were
  proven pre-existing by rerunning them on a clean stashed HEAD
  (NUnit `Has.Count` against the array-backed
  `player.Visual.Renderers` — unrelated to this pass). Temporary
  captures (removed after use) show the vessel mid-slide down the
  counter and the filled wine glass with the bartender's arm on it
  at mid-pour.

## 2026-08-16 — Bartender reads from the hall

- The two-metre rebuild alone was not enough: behind the ~1.56 m
  brass counter top only his pale head cleared the line and, point
  for point, read as one more backbar bottle (an isolation render
  proved the prefab itself was fine — the camouflage was the bug).
  Three coordinated fixes make him legible from anywhere in the
  hall: the model grew to a cashier-class 2.0 m with the long neck
  stub, he now works from a 0.42 m service duckboard so the
  shoulders and the whole extra-arm fan clear the counter, and the
  center counter pendant re-hung directly over his board so the
  head and moustache catch warm light against the dark backbar. The
  waistcoat palette brightened a step, the anchor moved beside the
  hero's counter station, and the canonical hands now rest ON the
  counter top (root-local rest points ride the duckboard height).
- Verification: bartender asset contract `1/1`, bar layout planner
  `17/17` with the moved anchor and pendant, bar smoke `1/1`;
  iterative temporary captures (removed after use) confirmed the
  hall sightline finally shows a lit face, cap and moustache above
  the counter instead of an anonymous bottle.

## 2026-08-15 — Six-Armed Bartender: model pass and bar presence

- Passes 1–2 of [`ai/bartender-spec.md`](bartender-spec.md) landed.
  `tools/build-bartender-3d-model.py` (Blender, subclassing the
  shared `PedestrianBuilder` like the cashier tool) builds the
  publican on the exact canonical 31-bone skeleton: broad torso,
  waistcoat/apron/flat cap/moustache, and two extra arm pairs as
  twelve rigid segments on sixteen `PIVOT_Arm{2,3}.{L,R}.*` empties
  (the cashier-neck/wheelchair mechanism) plus the brass band on the
  mid-right pouring arm. 50 meshes, 1436 triangles of the 3400
  budget; FBX + manifest + preview under `Assets/Bar/Bartender` and
  `ArtSource/Bar/Bartender`.
- The C# pipeline mirrors the cashier end to end:
  `BarBartenderModelImporter`, `BarBartenderAssetSetup` (manifest
  contract validation, prefab build, provider binding),
  `BarBartenderAssetRegistry` with the four serialized arm chains,
  and the addressable `BarBartenderProvider`.
- `BarBartenderPresentation` re-parents the chains under their
  pivots beneath the chest, folds the canonical pair to a counter
  rest via world-space two-bone solving (imported FBX bone axes are
  not trustworthy for local Euler folds — the first capture proved
  it), runs desynchronized per-chain idle business and head sway,
  and already exposes `SetChainTarget` CCD reaching for the service
  pass. `BarBartenderWorldBuilder` stands him on the authored
  Bartender anchor facing the hall (the sprite-era anchor yaw runs
  along the service alley); `BarInteriorRoot.Bartender` exposes him.

Verification:

- The Blender build validates the full contract (canonical skeleton,
  16 pivots, part markers, budget, grounding) and the Unity batch
  `BuildOrThrow` build passed including its own post-build
  validation. `BarBartenderAssetTests` EditMode passed `1/1`;
  the bar smoke test with new bartender assertions passed `1/1`.
- Temporary D3D11 captures (removed after use) were visually
  inspected across compass views: he stands the service alley at
  the counter, faces the guests, the canonical hands meet over the
  counter and the extra-arm fan reads at PS1 resolution.
- Remaining per the spec: pass 3 (service choreography — hover
  touch, carry, steady) and pass 4 (cocktails).

## 2026-08-15 — Six-armed bartender spec authored

- Wrote [`ai/bartender-spec.md`](bartender-spec.md): the design for
  the dedicated 3D bartender pass — a three-pair-armed figure on the
  cashier model pipeline, six independent CCD arm layers over a
  manually-advanced idle, service choreography where the authored
  `BarDrinkServiceTimeline` channels keep driving the bottles while
  the bartender's hands visibly touch, carry and steady them, and a
  2–3 ingredient cocktail order model with per-ingredient arms and a
  simultaneous bottle-return finale. Four independently-green build
  passes; nothing implemented yet.

## 2026-08-15 — Grocery lettering and the hero home anchor

- The supermarket signs now spell. `CitySignLettering` is a pure
  blocky segment font (П Р О Д У К Т Ы plus the house digit) laid out
  on a facade plane; the storefront band replaces its five anonymous
  glowing blocks with the word `ПРОДУКТЫ`, and a new vertical blade
  sign hangs off the storefront corner — one glyph per row, lettered
  on both street faces with per-face mirroring so the asymmetric
  glyphs always read forward. Both signs ride the shared glow
  registry and the home-exterior clipping path.
- The hero's building is now findable: a warm entrance lamp under a
  small canopy, the lit deep-blue house-number plaque (`7`) beside
  the door, and a rooftop antenna mast with a `0.3 m` red beacon
  (`2.3` HDR red, `~3.5 m` above the roof) that survives the city fog
  from blocks away. Everything registers with the night glow
  registry, so it dims by day with the rest of the city.

Verification:

- `CitySignLetteringTests` EditMode passed `3/3` (word layout bounds
  and centering, per-cell scaling determinism, glyph coverage
  including the house digit, unknown-glyph rejection).
- A temporary City-scene capture (removed after use) was visually
  inspected across four street viewpoints: the storefront word reads
  head-on, the blade sign reads top-down through fog from down the
  block, the entrance lamp and plaque mark the door, and the red
  beacon shows above the roofline from `26 m` away.
- Sign/anchor object assertions were added to the City smoke test;
  the suite currently fails earlier on the pre-existing `12x12`
  envelope expectation, which the in-flight `17x14` city expansion
  on this branch has not yet updated — unrelated to the signage.

## 2026-08-15 — Supermarket surface textures and fluorescent lighting

- The supermarket hall now carries real packaged albedos instead of flat
  tints. `tools/build-supermarket-textures.py` imports the entire home
  texture contract (linear luminance rule, wrap-by-construction drawing,
  compensation solving, validation) from `build-home-textures.py` and
  adds three market grammars — worn 4x4 linoleum squares with traffic
  scuffs, suspended ceiling panels over whitewash, corrugated cardboard
  with a tape band — reusing the home stucco / painted-metal / laminate
  grammars for walls, shelving and the counter. Six validated 1024
  sheets live in `Assets/Resources/Supermarket/Textures`, the measured
  contract in `ArtSource/Supermarket/supermarket-textures.json`.
- `SupermarketSurfaceAppearance` mirrors the home appearance class
  (metre-scale projected tiling, compensated display tint, hash salt
  3000) and the world builder resurfaces every big surface: floor and
  patches, all five wall segments plus the entrance header, ceiling,
  gondola frames/backings/tiers, the cold case, the checkout base and
  trim, and the stockroom cartons. Decals, stripes and small props stay
  deliberately flat.
- Lighting got the Home readability treatment: scene ambient rose from
  `(0.078, 0.098, 0.083)` to `(0.21, 0.25, 0.225)`, the directional
  fill `0.36 -> 0.72` with shadow strength `0.58 -> 0.45` so the
  ceiling-shadowed key survives indoors, fluorescent rows
  `1.05 -> 1.45` (range `8.4`), the checkout warm accent
  `0.78 -> 1.05` and the cold-shelf spill `0.55 -> 0.75`. The tired
  ballast flicker row is untouched.

Verification:

- The generator validates all six sheets (seam, mean luminance,
  compensation cap, contrast, chroma; worst brightness error `4.9%`).
- Focused EditMode passed `17/17`: the new
  `SupermarketSurfaceAppearanceTests` contract (recipes vs the
  generated constants, importer settings, tint compensation against
  the builder palette measured on the real PNGs, and a world-builder
  audit that all six sheets land on the hall), plus atmosphere and
  layout suites. `SupermarketPurchasePersistencePlayModeTests` passed
  `3/3` end-to-end on the textured scene.
- A temporary PlayMode capture (removed after use) rendered the live
  scene from the gameplay camera and an eye-height aisle view: the
  linoleum grid, wall mottle, ceiling panels, shelf metal, cartons and
  counter all read, the aisles and the hero stay legible between the
  fluorescent pools, and the green noir palette survives.

## 2026-08-15 — Returned the wheelchair yard to the bar

- Re-anchored the authored wheelchair yard to the bar directly across the
  player home's shared street frontage, then selected only its roadless left
  side. The resulting walkable gap lies between that bar and the neighbouring
  supermarket; the five typed fringe yards remain unrelated and undecorated.
- Split the narrative `PlayerHome` owner from the physical bar anchor in the
  shared site contract. The circuit dressing and leaning phone booth/dumpster
  now follow the bar, while the existing sampled spotlight mounts flush to the
  supermarket's yard-facing wall and covers the complete rider circuit.

Verification:

- Focused Unity EditMode regression
  `DefaultCity_DressesTheRoadlessGapDirectlyLeftOfABar` passed `1/1`.
  Broad suites and a player build were intentionally not run in fast mode.

## 2026-08-15 — Continuous city terrain and traversal audit

- Replaced the default city's isolated Buildable/Park/Open/Beach cell slabs
  with one sampled continuous-top contract and triangulated mesh colliders.
  Beach cells now share a canonical waterward profile; the lake is a local
  elevated basin instead of a deep pit to the global water datum.
- Road/ground and ground/ground connectors and guards now classify the same
  sampled physical edge. Unsafe guards follow the slope in segments and own
  retaining collision; park plazas conform to the terrain, district public
  places receive flat pads with `4 m` blends, and building foundations extend
  down without moving their authored tops.
- Added the deterministic `CityVerticalTraversalPlan` seam/frontage audit and
  fixed all eight river-park gates: their former centers sat over internal
  `ParkPath` corridors and left only `0.4 m` lawn slivers, so they now occupy
  capsule-wide, step-safe cell frontages.

Verification:

- Focused Unity EditMode category `CityTraversal` passed `7/7` on production
  seed `20260727`. Unity also compiled the shared runtime, EditMode and
  PlayMode assemblies in that invocation; broad suites and a player build were
  intentionally not run in fast mode.

## 2026-08-15 — Bar patrons drink from the bar's own bottles

- Bar guests now visibly drink. `BarPatronDrinkTimeline` (pure,
  seeded) loops Rest → Raise → Sip → Lower with per-patron randomized
  rests (`3.5–9.5 s`), sips (`1.1–2.2 s`) and an initial stagger so
  the crowd never moves in unison. `BarPatronDrinkingArmPose` is the
  procedural additive layer atop the authored Idle/Sit loops: each
  LateUpdate it captures the animated right arm, CCD-steers the held
  bottle's mouth onto the pedestrian `SOCKET_Mouth` anchor, tips the
  bottle up to `38°` with a `7°` head-back counter-tilt and slerps by
  the timeline weight — the teeth-brushing/bus-driver idiom.
- The bottles are the bar's own: `BarDrinkServiceWorldBuilder` exposes
  `BuildBottleVisual`, rebuilding the exact shelf silhouettes
  (beer longnecks, vodka, pepper vodka, cognac — picked by seed) as
  hand-scale props (`0.42×`) riding the canonical `SOCKET_Bottle.R`,
  gripped at `45 %` of bottle height, neck up. Every third guest stays
  deliberately empty-handed; a seeded ~30 % of sips play the existing
  `DrinkGulp` retro SFX at the lips so the room murmurs, not gurgles.
- `BarPatron` exposes the optional `Drinking` layer; designs missing
  the canonical sockets log and simply hold nothing.

Verification:

- Focused `BarPatronDrinkTimelineTests` EditMode passed `3/3`
  (cadence bounds, per-seed determinism plus cross-seed stagger, gulp
  one-shot discipline). `SceneFlowSmokeTests.BarInteriorScene_…`
  passed `1/1` with new assertions: some guests drink, some don't,
  and every held bottle is a visible prop riding the guest's hand.
- A temporary D3D11 RenderTexture capture (removed after use, per
  convention) was visually inspected: mid-sip the bottle mouth sits
  at the lips (asserted `< 0.12 m`), the bottle tips toward the face,
  the rest pose leaves the authored idle untouched. Batch-mode note:
  the capture rig must force `AnimatorCullingMode.AlwaysAnimate` —
  with no live cameras the culling-driven pedestrian animator holds
  the bind pose.

## 2026-08-15 — Brush-tip contact and interruptible bathroom scenes

- The brushing CCD now steers a `Brush Tip` effector anchored at the
  toothbrush bristles instead of the RightGrip socket, so the brush head —
  not the gripping fist — works the mouth in the mirror close-up. The
  mouth forward offset dropped from `6 cm` to `1.5 cm` to suit the new
  effector; `HomeTeethBrushingArmPose` falls back to the grip when no
  effector is assigned.
- All three bathroom scenes are now interruptible from any pre-wind-down
  phase via the shared stop input. Timelines keep visual continuity on
  abort: the teeth/toilet cameras walk home scaled from their actual
  blend, the shower curtain reverses from its current scale and water
  fades from its current amount. A stop during brushing still passes the
  rinse beat; a toilet abort during the camera retreat suppresses the
  flush. Foam no longer pops in during a rinse that never brushed past
  the foam threshold.
- The minimum times (`4 s` brush, `6 s` hold, `2.5 s` privacy) now gate
  only the stress reward — an early interrupt ends gracefully, commits
  nothing and leaves the once-per-day teeth gate unconsumed. The base
  `OnRequestStop` returns acceptance, fixing a latch where a refused stop
  press set `StopQueued` forever and swallowed all later presses. The
  toilet gained a visible stop prompt (`interaction.stop_toilet`, en/ru).

Verification:

- Unity `6000.5.5f1` batch compile passed with no compiler errors.
- Focused EditMode (bathroom timelines + localization catalog) passed
  `18/18`, including new coverage for aborts before the minimums, camera
  blend continuity on interrupt and the suppressed abort flush. Focused
  `HomeBathroomInteractionsPlayModeTests` passed `3/3`.

## 2026-08-15 — Readable home interior lighting floor

- Raised the Home interior readability floor that left most of the flat and
  the moving player nearly black. `HomeDayNightController` ambient rose about
  threefold (day `0.26/0.235/0.205`, night `0.145/0.14/0.17`), the interior
  directional fill went from `0.44/0.22` to `0.85/0.42` (day/night), and
  `RuntimeSceneSetup.EnsureHomeInterior` now uses shadow strength `0.45`
  instead of `0.62` so the ceiling-shadowed directional survives indoors as
  usable fill; its bootstrap ambient matches the new floor.
- Extended the main practical lamp range from `6 m` to `9 m` so its
  inverse-square falloff reaches the far walls of the roughly `9 x 7 m` flat,
  and the entry-door light from `4 m` to `5.5 m`. The day lamp intensity rose
  `2.30 -> 2.90` and night `4.10 -> 4.40`; the window key light is unchanged
  so the day/night window hierarchy stays intact.
- Lifted the interior grade out of compounding darkness: post exposure
  `-0.08 -> +0.25`, contrast `7 -> 5`, a lighter color filter and vignette
  `0.24 -> 0.18`. Bloom, grain and saturation are untouched.
- Fixed a pre-existing `CS0177` in `CityTerrainSurfacePlan` corner-elevation
  short-circuit that blocked batch compilation.

Verification:

- Unity `6000.5.5f1` batch compile passed with no compiler errors.
- Focused Home PlayMode: `HomeInteriorAtmospherePlayModeTests` passed `2/2`
  with updated expectations (lamp range `9`, entry range `5.5`, positive
  exposure). Three scene tests failed on this branch's unrelated
  work-in-progress: balcony street-lamp colliders, pedestrian count `8 != 5`,
  and a bathroom-lamp viewport framing check at `-0.08` — none are affected
  by light intensity, ambient or grade values.

## 2026-08-14 — River fence ownership and stair access correction

- Corrected the post-river collision conflict between `RoadFencePlanner` and
  `CityRiverWorldBuilder`. Declared river bridges are now support-only inputs
  to the generic road-boundary planner, while both promenade bounds support
  their adjoining bank roads. Generic colliders no longer duplicate the
  authored bridge parapets or close their four stair gaps.
- Trimmed the Works, Mouth and timber bridge guards to the inner edges of the
  two `8 m` bank-road pads, including the half-width of their end posts. Decks
  and structural members still meet the road nodes; only the obstructing guard
  geometry is shortened.
- Added river-layout regression coverage for fence ownership and physical
  bridge-guard bounds. The focused `CityRiver` EditMode category passed
  `12/12`; broad EditMode/PlayMode suites and a player build were intentionally
  not run in fast mode.

## 2026-08-14 — North-south river and three bridge hierarchy

- Expanded the default urban envelope from `12 x 12` to `13 x 12` while
  preserving all 144 land-use cells: the new central column is a declared
  north-south river corridor and the eastern city shifts one cell outward.
  `CityRiverDefinition`/`CityRiverPlan` own the `10 m` channel, two `3 m`
  promenades, three typed crossings and elevation-aware geometry descriptors.
- Added two distinct Road v2 bridges at the Works and Mouth edges plus one
  `2.8 m` timber ParkPath footbridge. Central Park remains 16 cells as two
  `2 x 4` regions connected by that footbridge. Route 01 uses both road
  bridges exactly once and never the timber bridge; bus furniture and ambient
  pedestrian spawns stay clear of the reserved crossings. Home keeps its
  frontage-adjacent stop; river-layout POIs use a same-district cyclic Street
  bounded to five grid edges and `120 m` from their public access.
- Built animated night/rain-responsive water, physical upper promenades,
  retaining edges, bridge decks and parapets. Each road bridge has one
  physical stair flight and lower platform on each bank, for four waterside
  landings total. River-proximity audio was not added in this pass.
- Extended the player and pedestrian walkable plans through the embankments
  and declared bridge graph. Updated the City map to draw the river and both
  promenades below roads, then overlay distinct Works, Mouth and planked timber
  bridge styles from the same layout metadata.
- Focused EditMode verification passed all `11/11` tests in the `CityRiver`
  category, covering topology, grades, walkability, physical river geometry,
  pedestrians, Route 01 and the map. Broad EditMode/PlayMode suites and a
  player build were intentionally not run in fast mode.

## 2026-08-15 — Bathroom: rebuilt shower and three modal scenes

- `HomeBathroomBuilder.BuildShower` rebuilt (~25 new parts, tray
  collider and pinned names kept): L-rail over both open sides, an
  animatable four-fold curtain group (pivot at the left front corner,
  gathered `scale.x 0.55` <-> drawn `1.0`) plus a static side run, wall
  mixer with red/blue cross handles and a spout, a four-segment sagging
  hose, riser/arm/neck and a tilted bell head with a dark nozzle plate,
  tray rims, drain, soap shelf. `HomeSurfaceAppearanceTests` palettes
  (`BedLinen += CurtainLight`) and exempt list extended.
- One shared scene skeleton (`HomeBathroomSceneInteraction`): modal
  capture, guided walk-in via `MoveTowardsInteractionPose` (stall →
  cancel), settle frame, Bézier camera from the pinned bathroom shot
  with the smoking drift, debounced stop with release re-arm,
  idempotent restore + `ReapplyActiveShot`, commit only on completed
  walk-out. Three recorded exceptions to the animation standard (no
  new clips — the set is closed): curtained Idle, off-frame Idle,
  procedural CCD arm.
- Toilet: privacy cut to the ajar-door frame (FOV 60), cistern hiss on
  the new shower-water loop at 0.35, one-shot `ToiletFlush` beat at
  3.6 s with the flush handle dipping, stress −6.
- Shower: hero walks into the tray, curtain draws shut, water/steam
  particles (code-built, shared atmosphere material) + crossfaded
  seamless hiss loop (`SetShowerWaterAmount`, 6th owned source,
  counts 5→6/8→9), corner frame FOV 54, min 6 s / auto 10 s,
  stress −12. The bathroom light flicker keeps running through it.
- Teeth brushing: camera from the mirror plane into the hero's face
  (FOV 36), `HomeTeethBrushingArmPose` (order 300, capture-solve-slerp
  CCD of the right arm to the Mouth anchor, 5.5 Hz oscillation, head
  counter-yaw), RightGrip toothbrush + Mouth foam props with the
  cigarette inverse-scale correction, scrub cues every 0.55 s, rinse
  with two Pour beats and a camera dip to the basin, relief gated once
  per game day (`TryCommitTeethBrushingRelief`, reset on new game).
- New SFX `ToiletFlush` (rush → gurgle → refill hiss) and
  `TeethBrushScrub` (two-stroke band noise); +5 localization keys
  (197 each, symmetric).

- **In-game bug and fix:** the first live test showed `E` doing
  nothing at all three spots. Root cause: the scene docks are authored
  at floor level `y = 0`, but the grounded controller root rides at
  `y ≈ 0.12`, and `PlayerMotor.MoveTowardsInteractionPose` demands a
  `2 cm` vertical match to complete — the walk-in arrived planar,
  could never finish, hit the stall timeout and silently cancelled the
  scene. The shared skeleton now grounds every walk target to the
  hero's current height (gravity owns the vertical, tray step
  included) and logs `bathroom_scene_started/rejected/stalled/
  completed` so the next silent failure reads straight out of
  `debug.log`. A PlayMode fixture
  (`HomeBathroomInteractionsPlayModeTests`) replays the exact `E` path
  for all three scenes.

- **Second live-path bug:** the brushing walk-in stalled 1 cm short of
  its dock — the capsule (radius `0.32` + skin width) met the sink
  basin collider at `z 3.25` while the guided walk demands an exact
  planar arrival. The stall diagnostics (player/target coordinates in
  `bathroom_scene_stalled`) pinpointed it; the dock moved to
  `z 2.78`.

Verification:

- Runtime, EditModeTests and PlayModeTests compile with 0 errors.
- Focused EditMode batch (scene timelines + surfaces + home layout +
  localization): 71/72 on the first pass — the one failure was a test
  authoring bug (phase-overshoot in the brushing fixture), fixed;
  the timeline fixture then passed 7/7.
- Focused PlayMode `HomeBathroomInteractionsPlayModeTests`: 3/3 —
  toilet privacy cut commits once, shower draws the curtain, runs
  water and restores, brushing replays with the day-gated relief.

## 2026-08-15 — Apartment lighting follows the session clock

- `HomeDayNightController` grew from window-only to a full indoor mood
  pass, all within the existing five-light budget (no new lights):
  window color gains a dusk amber phase (`1.0/0.56/0.30`, blend `0.65`)
  peaking mid-transition and exactly zero at the test-pinned day/night
  poles; the main lamp swings `2.30 -> 4.10` day to night and deepens
  its orange; the entry spot lifts `8.0 -> 9.4` (the presentation test
  floor of `>= 8` holds at all hours); `RenderSettings.ambientLight`
  and the directional fill lerp warm-bright day to cold-dark night
  (`0.44 -> 0.22`, blue-grey `0.60/0.66/0.82`).
- Balcony discipline: the ambient/sun mood is skipped while
  `HomeBalconyExteriorAtmosphere` has the balcony visibility active
  (the shot borrows City lighting) and reasserts itself on the
  visibility flip back indoors.

Verification:

- Runtime and PlayModeTests compile with 0 errors. Focused PlayMode
  batch: `HomeInteriorAtmospherePlayModeTests` fully green, and the
  balcony test's day-pole lighting assertions (WindowDayFactor `1`,
  exact day window color/intensity) pass before its failure point.
- Two `HomeBalconyPresentationPlayModeTests` failures are pre-existing
  and unrelated to lighting: the collider-free exterior view now
  contains `Street Lamp Chunk` BoxColliders (lamp chunks from the
  committed checkpoint `79572db` are not stripped by the exterior view
  builder), and the balcony pedestrian count is `8` where the test
  pins `5` (population changes from the committed city batches).
  Recorded, not fixed in this lighting pass.

## 2026-08-15 — Cashier neck: whole-hall reach, honest counter avoidance

- The neck's `4.5 m` cap still read as a limit in-game — the head
  stalled short of a hero deep in the aisles. The cap is now `18 m`
  (`MaximumNeckLengthMeters`), enough for every corner of the
  `16 x 11` hall: the face simply always arrives. Tool manifest ratio
  refreshed to `32.7` — geometry untouched, identical signature.
- Neck segments and the head were still visible through counters: the
  single-control quadratic sagged near its endpoints and clipped
  shelf edges the midpoint lift never covered. The solver is now a
  cubic staple — both controls rise to a shared clearance height at
  `t = 0.2/0.8`, so the chain climbs out of the register fast, rides
  above the aisles and descends only at the hero — and the resulting
  curve is re-sampled against every margin-expanded (`0.22 m`)
  shelf/fixture AABB, raising the clearance (up to four attempts,
  ceiling-clamped) until nothing clips.

Verification:

- Runtime compiles with 0 errors; Blender manifest regeneration
  reproduced the same build signature. Focused supermarket EditMode
  batch (cashier state/asset, cameras, atmosphere): 19/19 passed.

## 2026-08-15 — Bigger CCTV and a fluorescent light budget

- The corner cameras nearly doubled: thick `0.13 m` stems, a
  `0.27 x 0.27 x 0.62 m` body with hood, lens and iris, a `0.05 m`
  recording LED, corner inset widened to `0.62 m` and the head dropped
  to `0.50 m` below the ceiling — readable from the shop floor.
- `SupermarketInteriorAtmosphere`: the hall leaves the single flat
  directional behind. Six shadowless practicals — a cold point under
  each fluorescent row (`1.05/7.6 m`), one warm accent over the
  checkout (the only warmth in the hall, pooled on the Watcher
  Cashier), one cool cold-shelf spill — while the directional key steps
  down `0.48 -> 0.36` and remains the only shadow caster. Row two
  flickers on a deterministic `0.11 s` stepped pattern (dips to
  `0.30`), dimming both its light and its fake-emissive tube tint via
  MaterialPropertyBlock. Installed by the interior root right after the
  world build.
- `SupermarketInteriorAtmosphereTests`: the installed budget is exactly
  six lights, none directional, all shadowless, flicker present; the
  flicker pattern visibly dips below `0.9` and returns to `1.0` within
  bounds.

Verification:

- Runtime, EditModeTests and PlayModeTests compile with 0 errors.
- Focused EditMode batch (cashier pursuit state + cashier asset
  contract + CCTV + atmosphere + supermarket layout + open-area
  decorations): 22/23 passed — every supermarket fixture green,
  including the reworked pursuit-state tests.
- The 1 failure is pre-existing and unrelated:
  `CityOpenAreaDecorationPlannerTests
  .DefaultCity_DressesOnlyTheHomeYardWithACircuitAndTraces` finds
  `YardSpotlight.HasValue == false` on the default seed. This batch
  never touches the yard planner; the planner's last change is the
  river/envelope commit `8b84db7` (12x12 -> 13x12), which evidently
  moved the default home yard out of the spotlight condition.
  Recorded, not fixed here — river area.

## 2026-08-15 — Nightlife neon panes dressed with dark glyphs

- The last bare glowing quads among the misc decorations were the
  nightlife neon family: the billboard's two poster panes, the cinema's
  two lightbox one-sheets and the vending machines' front windows, plus
  the vending queue's glowing handrails. Following the phone-booth
  lightbox idiom, every pane now carries dark half-embedded strokes over
  the glow — headline/body/photo blocks on the billboard posters, a
  figure block and title strokes on the movie posters, vitrine grid
  mullions on the machine fronts — so they read as printed backlit
  signage instead of untextured rectangles. The queue handrails became
  painted steel: a glowing handrail at street level was noise, not
  signage. Thin marquee and letter strips stay neon — those are tubes by
  design. Day-night gating is untouched (the glyphs are Street-style
  batches, only the panes remain registered electric glows).

Verification:

- Runtime and EditModeTests compile with 0 errors; deterministic
  recipes changed geometry only, no plan or validator contract moved.
  Focused decoration fixtures pending the editor lock release.

## 2026-08-15 — Corner CCTV cameras track the hero

- `SupermarketSecurityCameraWorldBuilder` hangs four camera units in the
  hall corners, positions resolved purely from the layout plan (half
  room size minus wall minus `0.55 m` inset, `0.42 m` below the
  ceiling). Each unit: ceiling stem, boxy head with hood and dark lens,
  and a fake-emissive red recording LED with shadows off — primitive
  boxes on the shared runtime material, no Collider, no Light, so the
  one-directional-light budget is untouched.
- `SupermarketSecurityCamera` snaps its head onto the hero at
  initialization (never caught pointing at a wall) and then servos at
  `240°/s` via `Quaternion.RotateTowards` in `LateUpdate`; `Track` is
  public so EditMode drives it without a play loop. Built by the
  interior root right after the cashier, tracking the same body
  transform.
- `SupermarketSecurityCameraTests`: the four resolved corners are
  symmetric, distinct and under the ceiling; `ResolveAim` points the
  lens forward vector at the focus and survives a degenerate target;
  a real build under a temp root aims all four heads at a fake hero,
  follows him after `Track`, and stays collider- and light-free.

Verification:

- Runtime and EditModeTests assemblies compile with 0 errors. The
  focused EditMode batch (cameras + cashier state/asset + supermarket
  layout) is still pending: the open Unity editor holds the project
  lock; run it with the editor closed.

## 2026-08-15 — Cashier neck: head reattached, pursuit over the shelves

- In-game check showed the head tearing off the chain: head rotation ran
  around the canonical head bone, which rests `~0.5 m` below the authored
  face, so every pitch swung the skull off the neck. The head is now
  pinned to the curve tip by its authored neck-attachment point captured
  at bind (`InverseTransformPoint`), and rotates around that joint.
- The neck no longer just elongates — it pursues. The five pivots are
  laid along a quadratic curve from the neck base to a hover point beside
  the hero's face (`0.85 m` standoff, `0.25 m` lift), capped at `4.5 m`
  of neck; when the straight line crosses a shelf or fixture AABB, the
  curve's control point lifts above the tallest obstruction `+0.45 m`,
  so the chain arcs over the aisles instead of clipping through them.
  Obstacles and the hall roam box come from the layout plan through the
  root and factory into the presentation.
- `SupermarketCashierSurveillanceState` reworked to a pursuit weight:
  saturates to `1` whenever the hero is present (no more distance
  periscope or `ProximityCrane`), creeps at `0.9/s`, reels back at
  `2.4/s` under the caught-looking startle (cap `0.30`), blink
  suppression unchanged. Tool manifest ratio updated to `8.2`
  (`4.5 m / 0.55 m`) — geometry untouched, signature identical, prefab
  stays valid.

Verification:

- Runtime, EditModeTests and PlayModeTests compile with 0 errors; the
  Blender manifest regeneration reproduced the same build signature.
- The focused EditMode batch (cashier state/asset + supermarket layout)
  could not run this pass: the Unity editor held the project lock.
  Compile-clean recorded; run the fixtures with the editor closed.

## 2026-08-14 — The Watcher Cashier staffs the supermarket checkout

- Authored `tools/build-supermarket-cashier-3d-model.py` on the
  bus-driver pattern: subclasses the pedestrian `PedestrianBuilder`,
  keeps the exact 31-bone Player A-pose skeleton and exports an
  animation-free FBX + manifest (`watcher_cashier_v1`, 44 meshes,
  1588/2200 triangles, resting height `2.05 m`, signature-stamped).
  The design: hunched clerk, tiny head, five `0.11 m` neck segments
  with vertebra rings on `PIVOT_Neck.01..05` empties, a strangling
  collar narrower than the neck, one saturated name tag, enormous
  bulging eye whites (the right 8% larger) with pinprick pupils on
  the `face.eye.*` bones. A standalone validator owns the cashier's
  numbers (the shared one now demands an `ArchetypeSpec`).
- Runtime set under `Runtime/Supermarket/Cashier/`: provider asset in
  Resources referencing the off-Resources prefab (wheelchair pattern),
  registry with bones + pivots + manifest colors, factory with
  passivity guard, `PlayerAttentionMagnet` (2.0 m) and spawn logging,
  and a fully procedural presentation — restore rest pose each frame,
  hunch, CCD palms onto the counter, re-parent segments under pivots,
  fold pivots into a chain off the neck bone, stretch to `2.4x` on
  per-segment shares, serpentine yaw/pitch distribution, head hard on
  the chain tip delta inside a clamp box, pupil darts by bone
  translation (the eye bones sit `0.39 m` below the authored face, so
  rotations would sling pupils off the face), startle pupil pinch and
  `forceRenderingOff` blink.
- Pure logic split for tests: `SupermarketCashierSurveillanceState`
  (periscope `smoothstep 2..9 m`, extend `0.9/s` vs retract `2.4/s`,
  caught-looking hysteresis `cos 22°/0.15 s` in — `cos 30°/0.8 s`
  out, extension cap `0.30`, blink resume delay `1.2 s`) and
  `SupermarketCashierBlinkState` (`6.5 s` cycle, close/hold/open
  `0.09/0.16/0.14`, suppression restarts the cycle).
- Editor pass: `SupermarketCashierModelImporter` (shared Player
  Avatar via CopyFromOther) + `SupermarketCashierAssetSetup` (manifest
  contract incl. `neck_segment_count == 5` and pivot names, prefab
  build with forced shared material and bindings in manifest order,
  provider binding, menu items). Prefab built headless via
  `-executeMethod` and passed its own `ValidateOrThrow`.
- `SupermarketInteriorRoot.BuildCashier()` spawns the clerk on the
  authored `cashier-main` plan anchor after the player exists and
  plants the `E — заговорить` talk stub (booth/dumpster contract) in
  front of the register; +2 localization keys in ru/en (192 each).

Verification:

- Blender 5.0.1 headless build OK (44 meshes, 1588 triangles, 5
  pivots, deterministic signature); preview render checked visually.
- Runtime, Editor and EditModeTests assemblies compile with 0 errors.
- Unity batch `SupermarketCashierAssetSetup.Run` built and validated
  the prefab + provider. Focused EditMode batch:
  `SupermarketCashierStateTests` + `SupermarketCashierAssetTests` +
  `SupermarketInteriorLayoutTests` + `LocalizationCatalogTests` —
  23/23 passed.

## 2026-08-14 — Bar-visited mechanic removed entirely

- Cut the visit tracking from `GameSessionState`: the `visitedBars` set,
  `VisitedBarCount`, `MarkBarVisited`, `IsBarVisited` and
  `ClearVisitedBars` are gone, together with the `bar_visited` /
  `visited_bars_cleared` log events and every `visited_count` field in
  seed/blueprint-change, City-init and F8-snapshot logging. Entering a
  bar no longer touches the planned route — `RemoveRouteStop` stays as a
  manual map edit only.
- The city map lost the green visited marker colour, the visited legend
  swatch and the «`N`/4 посещено» counter; `CityMapController` lost
  `VisitedBarCount`/`IsBarVisited`. `map.visited_count` removed from
  both localization catalogs (190 keys each, still symmetric).
- Tests reworked: `GameSessionStateTests` dropped the MarkBarVisited
  fixture and visit asserts (seed/blueprint tests now cover the route
  only), `SceneFlowSmokeTests` dropped all visit asserts and setup,
  five Home PlayMode fixtures dropped their `ClearVisitedBars`
  hygiene calls, `LocalizationCatalogTests` required list updated.

Verification:

- Runtime, EditModeTests and PlayModeTests compile with 0 errors via
  the bundled dotnet SDK. Focused EditMode batch run:
  `GameSessionStateTests` + `LocalizationCatalogTests` 51/53 passed.
  The 2 failures are pre-existing and unrelated:
  `FoodUse_ClearsFractionalHungerProgress` and
  `CheapFoodUse_StopsAtFloorAndKeepsUnusedItem` expect a free stew can,
  but the committed `FeedTheCat` starter quest (cfd4993) reserves
  `OpenStewCan`, so `TryConsumeInventoryItem` returns
  `ReservedForQuest`. Recorded, not fixed here — quest-journal area.

## 2026-08-14 — Sprite NPCs and bar minigames cut; 3D guests seated

- Removed the sprite NPC engine (`Bar/NPC`: actor, director, factory,
  planner, sprite library, types) and both of its populations: the bar
  crowd with its bartender and the supermarket cashier. The layout data
  survives — `BarNpcAnchor`/`BarNpcRole` live in the interior layout
  plan, and `SupermarketCashierPlan` keeps its authored spot for the
  future dedicated 3D cashier/bartender pass.
- Added `BarPatronWorldBuilder`: the production 3D pedestrians take the
  same authored anchors — `SeatedPatron` anchors get a bench-style seat
  anchor and the archetype's `SeatedRide` pose, everyone else stands on
  idle; `BarPatronAnimator` advances the loops; bartender anchors stay
  empty by design. `SceneFlowSmokeTests` now asserts 3D guests and no
  seated bartender.
- Cut all four minigames wholesale: `BeerPong`, `Cocktails`,
  `SplitTheG`, `TinctureMatch` runtime folders, the UI controllers and
  sprite libraries, `BarMinigameCatalog`/`IBarMinigame`/
  `BarActivityStation`, their Resources atlases, art generators and
  every dedicated fixture (about 60 files). `BarActivityKind` survives
  purely as the interior layout flavour (stage, beer pong table stay as
  dressing), normalized locally instead of via the catalog.
  `MarkBarVisited` moved from minigame completion to bar entry.
- `MinigameDebugWindow` kept its real duties — F9 modal with
  intoxication adjustment, City-map test-teleport toggle, F8
  diagnostics, drink-shop modal exclusivity — and lost the launcher
  list; `BarMinigameModalLock` stays as the generic modal capture.
- Localization: 114 minigame keys removed from both catalogs and the
  required list (kept symmetric at 191 keys); stale format assertions
  dropped.

Verification:

- Runtime, EditModeTests and PlayModeTests all compile with 0 errors
  via the bundled dotnet SDK; focused bar/supermarket/localization
  fixtures run recorded below.

## 2026-08-14 — Full-height street lamps with matched luminous power

- Scaled the street lamp assembly `1.6x` in `CityNightWorldBuilder`:
  a `5.30 m` mast (was `3.30`), thicker pole, longer arm, larger head
  and lantern. Every part offset is measured from the same planned
  base position, so no lamp moved; the light anchor rose with the
  lantern to `4.70 m`.
- Scaled the luminous power to the new height in
  `CityNightAtmosphere`: the source sits `1.61x` higher, so the
  inverse-square law sets intensity `12 -> 31` for the same pavement
  illuminance, range `10.5 -> 16.5 m`, and the fog halos grew with the
  lantern (`1.15/3.10`). Spot angles and the bar entrance lights are
  unchanged; the lower-pole collider already outsizes the thicker
  mast.
- Retired the stale "twenty street practicals" phrasing around the
  yard spotlight ratio; its authored `240` intensity is untouched.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `CityNightFixturePlannerTests` +
  `CityOpenAreaDecorationPlannerTests` passed `8/8` — lamp placement
  contracts and the yard-spotlight contracts both hold with the grown
  masts.

## 2026-08-14 — Pedestrians rest on benches

- Extended `CityPedestrianActor` with a bench lifecycle mirroring the
  Route 01 machinery. The pavement network ends at the kerb and
  `Constrain` never lets a walker off it, so the lifecycle is
  graph-then-crossing: `ApproachingBench` walks the Dijkstra guidance
  to the bench's own node, `WalkingToBenchSeat` is a short scripted
  off-network crossing (capsule released, like a bus doorway) onto the
  slot, `WaitingAtBench` hands to `BeginBenchSit` (presentation seated
  on an anchor exactly like a bus seat) and `StandUpFromBench` walks
  the same crossing back before `ResumeRoaming`. Cancellation from any
  crossing phase re-plants the walker on the bench node first. Bus
  logic is untouched: its guards key on the stop states.
- Added `CityBenchRestPlanner`: from the same `CityBenchSitPlan` seats
  the hero uses, it keeps only benches whose slot is within a `6 m`
  crossing of a graph node (reusing the bus wait planner's
  now-internal Dijkstra and nearest-node helpers) — which naturally
  excludes the hero's yard bench and anything the network cannot
  honestly reach. Each point carries the stand slot, seat top, sit
  facing and distance field.
- Added `CityBenchNpcRestController` in `CityGameRoot`: every `3.5 s`
  it may (p = `0.4`, xorshift seeded by the city seed) send the nearest
  eligible walker (walking, seatable archetype, within `30 m` of graph
  walk) to a free bench; on arrival it seats him for `15-30 s`, then
  stands him back onto the slot. Approaches time out at `45 s`;
  recycled walkers release their seats; at most `2` rest at once.
- Added `CityBenchSeatClaims`, a shared claim registry: the rest
  controller claims per rest, and `CityBenchSitInteraction` now claims
  on begin/releases on idle-or-cancel and hides its prompt for a seat
  claimed by another — the hero and the walkers can never share a
  plank.
- Added `CityBenchRestTests` (reachable seats, yard exclusion, claim
  exclusivity) and re-ran the pedestrian runtime fixture as the state
  machine regression.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `CityBenchRestTests` passed `2/2` and the full
  `CityPedestrianRuntimeTests` fixture passed `22/22` as the actor
  state-machine regression. The first run caught two honest design
  holes: the pedestrian walkable network never contained the bench
  slots (which produced the crossing design) and the yard bench
  qualified through a nearby node (now excluded by id, by decision).

## 2026-08-14 — Silent Hill attention: the hero's head finds targets

- Added the attention system in `PlayerAttention.cs`. Pure
  `PlayerAttentionRules` define the notice cone (`3.6 m`, `±75°`), the
  wider release cone (`4.2 m`, `±100°`) so a held target never
  flickers at the edge, the people-first ordering (`0.8x` effective
  distance) and the neck limits (`±68°` yaw, `±32°` pitch).
- `PlayerAttentionController` (installed by `PlayerFactory`) scans at
  `0.18 s` intervals: one physics overlap finds every `CanInteract`
  interactable and the pedestrians by their collision layer, and a
  static `PlayerAttentionMagnet` registry covers colliderless
  characters — the yard rider gets a magnet at seated head height in
  his factory. Between scans the held target is tracked live, so a
  walking passer-by keeps the head on him.
- `Player3DCharacterPresentation` applies the glance post-animation in
  `LateUpdate` with the established capture/restore base pattern:
  yaw/pitch shared `62/38` between head and neck bones,
  `SmoothDampAngle` turns, `0.22 s` ease-in / `0.38 s` ease-out, a
  fresh glance starting on target, and full stand-down whenever a
  modal clip, interaction handoff or ragdoll owns the body. Axis signs
  are named constants after the wheel-roll lesson.
- Added `PlayerAttentionTests`: cone and hysteresis contracts, neck
  clamps, and a controller pass proving people outrank closer objects,
  the fallback to interactables, and the release behind the back.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `PlayerAttentionTests` passed `3/3` (after
  marking the magnet `[ExecuteAlways]` so edit-mode registration works)
  and `YardWheelchairMotionTests` passed `10/10` in the same batch —
  clearing the previously blocked roll-sign and ground-profile
  contracts as well.
- In-game check: only the magnet-driven rider drew the head. The scan's
  self-filter compared `transform.root`, and every gameplay scene
  parents the player and the whole world under one composition root —
  so all colliderful targets read as "self". Replaced it with
  `IsChildOf(player transform)` and re-rooted the controller test so
  the player and its targets share one root like a real scene; the
  batch rerun was blocked by the open editor at the time of writing.
- Second in-game check: the hero craned his neck upward in the
  apartment. Points of interest never hang overhead by design, so the
  rules now reject any focus more than `2.1 m` above the hero's feet
  and the pitch clamp became asymmetric — the chin still drops `32°`
  for floor items but rises at most `10°`. Both are covered by the
  rules fixture.
- Third in-game check exposed the real culprit behind both cranes: the
  pitch axis sign. NPC faces sit at eye height (pitch about zero) and
  looked right, while interactables sit low — the intended `32°` chin
  drop applied inverted as a `32°` crane. Positive local X on the
  imported neck/head bones pitches the face up, so
  `AttentionPitchSign` flipped to `+1` (the wheel-roll lesson again);
  the overhead-focus and `+10°` up-clamp guards stay as safety.

## 2026-08-14 — The rider's lap follows the real ground

- In-game check found the chair hovering where the yard straddles two
  terraces: the plan's single flat `GroundY` came from the home cell's
  datum while the neighbour half of the circle can sit on another
  terrace. `YardWheelchairPlan.Create` now optionally takes the
  `CityElevationPlan` and samples `64` ground heights around the ring
  (`GroundDatum` + `GroundTopOffset`, falling back to the site ground
  where sampling misses); `Sample` reads the interpolated profile, so a
  terrace lip reads as a short ramp instead of a hover. `CityGameRoot`
  passes the layout's elevation plan.
- Extended the motion fixture: a synthetic stepped profile must carry
  the pose off the flat plane exactly along the interpolation, and the
  default-city elevated plan must match the elevation samples at every
  probed angle.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK. The focused `YardWheelchairMotionTests` batch run was
  blocked by the open editor holding the project lock; the two new
  deterministic contracts run with the next unlocked suite.

## 2026-08-14 — The drawn yard ring is removed; the rider keeps its circuit

- Removed the 24-chord `YardRingTrack` geometry, its `YardWornTrack`
  style, the packed-earth albedo, its generator tool and its focused
  test: the rider now circles the dead tree on bare ground with nothing
  drawn for the lap (reversing the same-day albedo work by user
  decision).
- Rewired `YardWheelchairPlan.Create` from ring read-back onto the yard
  site contract: `HomeYardSite.RingCenter/RingRadius/GroundY` are the
  circuit, with the dead tree still required at the centre. Slot
  clearances, utility anchors and the spotlight already used the same
  contract, so every keep-off-the-lap rule survives unchanged.
- Updated the open-area and wheelchair fixtures: no `home-yard-ring-`
  descriptors may exist, and the plan must equal the site ring exactly.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `YardWheelchairMotionTests` +
  `CityOpenAreaDecorationPlannerTests` + `CityDecorationPlannerTests`
  passed `22/22` under Unity `6000.5.5f1`, which also exercised the
  earlier roll-sign regression assertion for the first time in batch.

## 2026-08-14 — Turning wheels and a ragged push rhythm for the rider

- Made the pivot articulation real: `YardWheelchairPresentation` now
  adopts the static chair meshes (`ACC_WheelTyre/PushRim/WheelSpokes`,
  `ACC_CasterTyre/CasterHub`) under their authored `PIVOT_*` empties at
  initialize. The exporter deliberately ships them beside the pivots
  (parenting a skinned FBX mesh through an Empty double-converts units)
  with each mesh origin on its pivot, so the runtime reparent is exact —
  the existing distance-locked pivot rotations finally turn visible
  geometry, differential and caster swivel included. Bellows and organ
  pipes stay bone-skinned and keep riding the body animation.
- Added the hand-push cycle to `YardWheelchairMotion`: a `1.35 m`
  ground-locked cycle (`PushDistance`), smooth surge to `1.42x` through
  the `24%` stroke and a long bleed to `0.62x` through the coast,
  multiplied over the existing lap sway. Defined on distance, not time,
  so wheels, pace, the arm loop (speed clamp widened to `0.30-1.60`)
  and the bellows pump (now driven by `PushPhase`) can never drift
  apart. Minimum sampled speed stays above the `0.5 m/s` contract.
- Extended the motion fixture: a push cycle must surge at least `1.8x`
  over its trough and repeat exactly one push-distance later, and the
  presentation must adopt the wheel meshes under their pivots and turn
  them with covered distance.
- In-game check found the tyres rolling backwards: the baked FBX axis
  conversion leaves the axle on local X with positive spin reversed.
  Added `YardWheelchairPresentation.RollSign = -1` applied to both
  drive wheels and the caster roll, with an exact-rotation regression
  assertion so the sign cannot silently flip back.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `YardWheelchairMotionTests` passed `9/9` under
  Unity `6000.5.5f1` — the two new contracts plus every pre-existing
  motion invariant (circuit hold, drift flip, lap time, wheel
  differential, minimum speed). The subsequent roll-sign fix compiled
  clean; its batch rerun was blocked by the open editor holding the
  project, and the sign assertion mirrors the presentation formula
  exactly.

## 2026-08-14 — Packed-earth albedo for the yard wheelchair circuit

- Added `tools/build-city-yard-track-texture.py`: a deterministic 512
  seamless sheet of compacted bare earth (pressed hollows, wheel-polished
  dust, pressed-in stones, fine grain), isotropic on purpose — a circle
  has no single rut direction under world-planar mapping. Authored at
  mean RGB `120/104/80`, about twice as bright and warmer than
  `CityGroundSoilAlbedo` (`53/52/40`), so the trace contrasts against
  the yard soil while reading as trodden dirt.
- The `YardWornTrack` batch in `CityOpenAreaWorldBuilder` now builds
  through the planar-UV combine path (`1.8 m` tile) and receives the
  sheet via the shared `CityExteriorAppearance.ApplyYardTrackSurface`
  recipe (white tint, `0.05` smoothness, shared `RuntimePrimitiveLit`);
  every other open-area style stays a flat colour. Ring geometry, the
  rider's derived circuit and collision are untouched.
- Extended the open-area fixture: worn-track chunks must carry the
  packed-earth albedo with authored UVs on the shared material, and
  non-track chunks must stay textureless.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `CityOpenAreaDecorationPlannerTests` passed `4/4`
  under Unity `6000.5.5f1`, including the new worn-track albedo
  contract.

## 2026-08-14 — Placeholder interactions on every booth and dumpster

- Added `CityStreetUtilityInteraction`: an `IInteractable` stub standing
  on the recipe-derived dock of every phone booth door and dumpster lid.
  It offers the real prompts (`interaction.use_phone_booth`,
  `interaction.search_dumpster`) and answers through
  `PlayerInteractor.ShowFeedback` with `city.phone_booth.placeholder` /
  `city.dumpster.placeholder` for `2.5 s` — the same stub contract the
  stairwell cat used before feeding shipped. A future pass swaps only
  `Interact`; the trigger and dock stay.
- Added `CityStreetUtilityWorldBuilder` mirroring the bench sit pass:
  one oriented trigger volume per dock under
  `City Street Utility Interactions`, wired in `CityGameRoot` right
  after the bench sits from `CityStreetUtilityDock.CreateAll`.
- Added the four localization keys to both catalogs and the required-key
  list; extended `CityStreetUtilityPlanTests` with a builder contract
  (one placeholder per dock, kind-matched prompt, trigger volume).

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `CityStreetUtilityPlanTests` +
  `LocalizationCatalogTests` EditMode passed `10/10` under Unity
  `6000.5.5f1`.

## 2026-08-14 — Audit: every exterior electric glow joins the night clock

- Audited all `CityNoirEmission` users. Interiors (bar, home, stairwell,
  supermarket, fridge, alarm clock) legitimately own their light; the
  exterior stragglers were the nightlife neon batches, the booth backlit
  signs, the supermarket sign/letters and its two flat glowing storefront
  slabs, the home porch light, the hero's lit balcony window, the
  balcony-view lower facade panes and the POI lamps (waterworks
  `Working Lamp`, weighbridge `Cold Service Lamp`).
- Added `CityNightGlowRegistry`: builders register each electric renderer
  with its lit colour; `CityNightWorldResult.SetNightFactor` lerps them
  between a `0.10x` dead-fixture tint and full glow and prunes destroyed
  renderers, covering City and the bounded Home exterior. Deliberate
  exceptions stay always-on: traffic signals, the weighbridge
  `Scale Indicator Face` (`alwaysLit`) and the authored yard spotlight;
  the bus already dims through `CityBusPresentation.SetNightFactor`, and
  the Home-view terminal haze is a backdrop, not a fixture.
- Rebuilt the supermarket storefront glass as real glazing: the panels now
  use the shared Supermarket window-family material with the plain-glass
  quadrant of the window sheet (`CityWindowAppearance.ApplyPlainPane`), so
  they are framed, textured and follow the clock for free.
- Added `CityNightGlowRegistryTests` covering the lit/dead lerp contract
  and dead-renderer pruning.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK.
- Focused EditMode under Unity `6000.5.5f1` passed `15/15`:
  `CityNightGlowRegistryTests`, `CityWindowAppearanceTests` and
  `CityDecorationPlannerTests` together, re-proving the decoration
  build path with the registered neon batches.

## 2026-08-14 — Textured facade windows on the night-factor clock

- Added `tools/build-city-window-textures.py`: a deterministic 512 sheet of
  four pane variants (plain, curtains, blinds, lamp) authored light-glass /
  dark-frame in the facade sheets' doctrine and tone family, pre-corrected
  for the pane's 3.5:1 stretch, shipped as
  `Resources/Textures/CityWindowAlbedo.png`.
- Added `CityWindowAppearance`: one shared runtime material per lit window
  family (Cold/Warm/Bar/Home/Supermarket) cloned from the packaged unlit
  emission material with the sheet as `_BaseMap`; per-pane variety is an
  MPB `_BaseMap_ST` quadrant only, so the material keeps colour authority.
  `SetNightFactor` lerps each family colour between unlit `DayGlass` and its
  lit hue — the whole city's windows dim through five materials.
  `CityNightWorldResult.SetNightFactor` calls it, which covers both the City
  and the bounded Home exterior clocks. Dark panes keep the default lit
  material and get the same sheet via MPB, so they read as glazing all day.
- Replaced `CityExteriorAppearance.ResolveWindowColor` with
  `ResolveWindowFamily` (same hash, same 65/25/10 mix — seeds light the same
  rooms) and refactored both window builders onto it. Added
  `RuntimePrimitiveFactory.CreateMaterialBox`, a box that writes no colour
  property block so material-wide changes reach it.
- Added `CityWindowAppearanceTests` covering family determinism and mix,
  shared-material identity and night-factor lerp, variant quadrant bounds
  and the shipped sheet.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK.
- Focused EditMode under Unity `6000.5.5f1`: `CityDecorationPlannerTests`
  passed `10/10` alongside the new fixture (also re-proving the booth
  lightbox build path); `CityWindowAppearanceTests` passed `4/4` after
  switching an exact `Color` equality to per-channel tolerance
  (`Mathf.Lerp(a, b, 1f)` is not bit-exact `b`).

## 2026-08-14 — Leaning yard utilities and citywide booth/dumpster coverage

- Added `HomeYardUtilityPlanner` to the shared yard site contract: it leans a
  phone booth against the hero's own wall (door into the yard) and the shared
  dumpster at the far end of the same wall. Both anchors keep their whole
  footprint `ring radius + 1.4 m` off the wheelchair circuit and never
  overlap each other; the yard slot objects now treat those footprints as
  reserved ground.
- The city decoration planner consumes the same anchors as ordinary
  `RoadsidePhoneBooth`/`RoadsideDumpsterAndUtility` descriptors
  (`…-homeyard-booth`/`…-homeyard-dumpster`), so recipes, night neon,
  chunked collision proxies and the home balcony exterior view all come from
  the existing street catalogue.
- Made booths and dumpsters repeat like infrastructure. Random roadside
  clusters demote a crowding utility to roadwork (booths never closer than
  `55 m`, dumpsters `40 m`), and a new row-major coverage pass fills the gaps
  (a booth within about `90 m` of every ordinary lot; dumpsters within `65 m`
  in Residential/Industrial and `100 m` elsewhere).
- Prepared interactivity: new `CityStreetUtilityDock.CreateAll` mirrors the
  bench-seat read-back and derives one dock per booth door and dumpster lid
  from shared recipe constants, so a future interaction pass can install
  triggers exactly like the bench sit pass does.
- Replaced the booth's bare floating neon slab with a municipal lightbox on
  the roof fascia: a dark `Street` housing, one recessed panel in a new
  seventh `BacklitSign` batch style (pale fluorescent `1.22/1.36/1.18` HDR
  glow on the shared emissive material, quieter than nightlife neon) and
  seven dark glyph strokes that read as the sign's word abstractly, matching
  the supermarket block-letter idiom. Batching stays per-chunk shared-material
  only.
- Extended pure coverage: spacing/coverage invariants across seeds, the
  hero-wall lean and circuit clearance contracts, yard dressing staying off
  the reserved utility ground, and dock determinism/ownership/reach.

Verification:

- Focused `BarPromenade.Tests.EditMode` run over `CityDecorationPlannerTests`,
  `CityOpenAreaDecorationPlannerTests` and `CityStreetUtilityPlanTests`
  passed `15/15` under Unity `6000.5.5f1`, including the unchanged strict
  yard dressing counts.
- `PlansAcrossSeeds_ExerciseCompleteRecipeCatalog` was stale since the
  roadside pool became shelter-free and now expects the route-owned shelter
  to stay absent from ambient decoration.
- Known unrelated failure, not addressed here: `CityBusPlannerTests
  .OrderedRoute_IsStreetOnlyRightHandAndOneInOneOut` misses its `1.5 m`
  departure-lane assertion by `0.0011` on this branch; the bus planner reads
  only the decoration plan's seed, so it is independent of this change.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Turned the permanent yard light into a noir key

- Strengthened the same single static neighbour-wall Spot from the ordinary
  street-practical level to intensity `240` (`20x` the street value of `12`) and
  retained its cold near-white color and day/night independence.
- Set range to the greater of `1.5x` the sampled throw and sampled throw plus
  `3 m`, then tightened the cone so the complete wheelchair circuit stays
  inside the bright inner region with only `6°` of total feather. The source
  now casts hard shadows at `0.95` strength and high resolution; its HDR lens
  multiplier is `4.8x` and its halo is larger and brighter.
- Kept the presentation architectural: there is no volumetric beam, no rider
  tracking and no second `Light`. The old yard lamp remains dead, so the
  bounded worst case stays `18` local realtime lights.

Verification:

- Focused `BarPromenade.Tests.EditMode`
  `CityOpenAreaDecorationPlannerTests`
  `.DefaultCity_DressesOnlyTheHomeYardWithACircuitAndTraces`: passed `1/1`
  under Unity `6000.5.5f1`.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Enabled vertical orbit on the ordinary chase camera

- `PlayerCameraFollow` now consumes both components of its existing RMB mouse
  and gamepad right-stick sample. The new pitch target is smoothed over
  `0.18 s`, clamped to `-20°..55°` and retained across fixed-camera ownership,
  just like the independent chase yaw.
- City, Bar and ordinary Supermarket follow gain vertical orbit. Home,
  Stairwell, contextual fixed shots and the bus's separately bounded seated
  view keep their existing ownership and limits. Modal `OrbitInputEnabled`
  suppression still gates both axes.

Verification:

- Focused `PlayerCameraPresentationPlayModeTests`
  `.ExteriorCamera_VerticalOrbitConsumesMouseInputAndClamps`: passed `1/1`
  under Unity `6000.5.5f1` in an isolated HEAD-based project copy carrying
  only the camera runtime and regression-test changes.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Put a permanent neighbour spotlight over the home yard

- Corrected the authored home-yard contract to the world that is actually
  built: the wheelchair circuit occupies the walkable roadless gap between the
  hero's building and its neighbour, not the large eastern fringe `Yard`.
- Added one stable wall-mounted spotlight to the same data-first composition.
  Its fixed shadowless cone covers the complete worn circuit at constant
  intensity through day and night, stays outside `NightFactor` and never tracks
  the rider. The old two-part yard lamp remains dead geometry with no emitter.
- Accounted for the permanent source without shrinking the atmosphere pool:
  `12` atmosphere lights + `4` bus Spots + `1` pooled helmet Spot + `1` yard
  Spot gives a bounded worst case of `18` local realtime lights. The scene
  Directional and transient lightning Directional remain separate.

Verification:

- `BarPromenade.Tests.EditMode.CityOpenAreaDecorationPlannerTests`
  `.DefaultCity_DressesOnlyTheHomeYardWithACircuitAndTraces`: passed `1/1`
  under Unity `6000.5.5f1`.
- `BarPromenade.Tests.PlayMode.CityNightPresentationPlayModeTests`
  `.CityDayNight_ChangesLightingWithoutChangingFog`: passed `1/1` under Unity
  `6000.5.5f1`.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Restored the Pipeback Roller's wheelchair at full size

- Fixed the staged Unity prefab build that multiplied all `17` root-bound
  wheelchair `MeshRenderer` transforms by an extra `0.01`. The FBX importer
  already honours the model's metre units, so the duplicate conversion left
  the wheels, rims, spokes, casters, frame, seat, backrest, armrests,
  footrests and push levers at one percent of their authored size while the
  skinned rider remained full-size.
- Removed that additional scale conversion and rebuilt
  `Assets/Pedestrians/Staged/Prefabs/PipebackRoller3D.prefab`; the complete
  chair is visible again without changing the isolated provider or ambient
  pedestrian pool contract.
- This fix restores the static mechanism geometry only. The passive
  `PIVOT_*` anchors are still not transform parents of their meshes, so
  procedural wheel/caster/mechanism articulation remains a separate
  limitation.

Verification:

- Focused EditMode
  `StagedPipebackRoller_ImportsPassiveWheelchairAndRemainsOutsidePool` passed
  `1/1` after the staged prefab rebuild.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Added a one-key Home-to-City debug map entry

- `HomeInteriorRoot` now always installs `HomeDebugCityMapShortcut`. F9 works
  from any Home phase, including the opening's locked `ClockHold`, and uses the
  guarded direct scene-transition path instead of playing the apartment and
  stairwell presentations.
- If the session clock is still frozen, an accepted shortcut starts it from
  `06:00`, prepares the normal player-home City return and sets the resettable
  `GameSessionState.DebugCityMapOnArrivalRequested` handoff. Seed, cash, needs
  and starter inventory are otherwise untouched.
- A real runtime skip from the opening's `AwaitingWake` menu exposed a
  lifecycle race: City enabled test teleport after the scene transition, but
  its single map-open attempt ran while the previous Home opening still owned
  `BarMinigameModalLock`, so the map remained closed.
- `CityGameRoot` now enables test teleport after the transition and uses a
  success-driven retry window bounded to `2 s` of realtime. It opens only
  after both the transition and previous modal lock release, consumes the
  one-shot on success, and also clears it with diagnostic state on timeout so
  a failed request cannot leak into a later City load. Ordinary Wake/Quit and
  Home -> Stairwell -> City behavior is unchanged.

Verification:

- The focused PlayMode
  `BarPromenade.Tests.PlayMode.HomeOpeningPlayModeTests.MainMenu_F9SkipsHomeAndOpensCityDebugTeleportMap`
  now waits for `AwaitingWake` and asserts the active modal-lock precondition
  before F9. It passed `1/1` in an isolated Unity project copy while the main
  editor remained open.
- No broader suite or player build was run in fast mode.

## 2026-08-13 — Staged the Pipeback Roller wheelchair NPC

- Added `pipeback_roller_v1` / Pipeback Roller («Трубный седок») as a complete
  staged presentation rather than a sixth production pedestrian. The ordinary
  seated rider wears dark burgundy; the bizarre silhouette belongs to the
  wheelchair's two large drive wheels, nervous front casters, under-seat
  bellows and asymmetrical fan of tarnished organ pipes.
- Extended the deterministic pedestrian generator with
  `Assets/Pedestrians/Staged/Models/PipebackRoller3D.{fbx,json}` and the
  adjacent editable source/preview. The rider preserves the exact production
  31-bone Generic hierarchy and shared `Player3DLit` material. Six passive
  `PIVOT_Wheel.L/R`, `PIVOT_Caster.L/R`, `PIVOT_Bellows` and `PIVOT_PipeBank`
  anchors expose the future procedural mechanism contract without adding
  deform bones or auxiliary curves to the Avatar.
- Added two in-place Actions to the shared animation-only locomotion library.
  `PipebackIdle` keeps the head level over a slow breath under the pipe load;
  `PipebackRoll` stages a two-handed raised-lever push, forward body lean,
  release and recovery. Bellows and pipes follow the authored pelvis/chest
  motion; wheel/caster rotation remains intentionally procedural and deferred.
  The staged design deliberately owns no `Sit` clip.
- Added the passive staged prefab at
  `Assets/Pedestrians/Staged/Prefabs/PipebackRoller3D.prefab`. It is outside
  `Resources` and carries only the shared `CityPedestrianAssetRegistry` plus a
  passive `CityWheelchairNpcAssetRegistry` for those six mechanism pivots. It
  has no runtime actor, collider, Rigidbody, light, audio or interaction, and
  `CityPedestrianResources.OrderedArchetypes` remains the five-design
  production catalog. City and Home therefore keep their existing `13`- and
  `8`-presentation pools, and the staged NPC cannot roam, wait for or ride
  Route 01.
- Production registration is deferred until the graph can exclude stairs and
  prove curb/turn clearance, the actor has a wheelchair footprint rather than
  the ordinary `0.35 m` capsule, runtime derives wheel/caster motion from
  travelled distance, and Route 01 owns an accessible boarding and securement
  design instead of the ordinary pelvis-to-seat transfer.

Verification:

- Blender 5.0.1 completed the full generator/validator, rendered the six-row
  contact sheet and matched repeated model signatures. Pipeback measures `52`
  meshes / `2388` triangles at exactly `1.75 m`; both clips remain in-place and
  loop-closed with wheel contact `0.000 m`, footrest clearance `0.268 m`, seat
  gap at most `0.023 m` and hand-to-lever distance below `0.10 m`.
- Unity rebuilt the passive staged prefab and the one focused EditMode contract
  `StagedPipebackRoller_ImportsPassiveWheelchairAndRemainsOutsidePool` passed
  `1/1`, including avatar/material/passivity, the two clips, six pivots, absence
  from `Resources`/catalog, and unchanged City `13` / Home `8` pool isolation.
  Complete suites and a player build were intentionally omitted in fast mode.

## 2026-08-14 — Moved the yard composition to where the hero actually stands

- Reported from play: the yard by the home read as empty ground. The
  dressing was anchored to the centroid of the yard area, but `yard-east`
  is the whole eastern pocket (`4 x 6` cells, over `100 x 150 m`), so the
  centroid landed ~`65 m` east of the home — past the `48 m` far clip and
  the fog. The ground was there; every object was invisible.
- The composition is now anchored to the yard's street entrance, which the
  layout puts at cell `(12,5)`, `17 m` from the door. The ring is offset
  past the approach (`approach reach + clearance + radius + margin`) so
  the worn circuit stays unbroken instead of losing segments to the
  entrance, and the dressed rect is built along the inward normal from the
  entrance rather than over the whole pocket.
- Measured after the change (default seed): dead tree `27.6 m` from the
  home, bin `20 m`, everything else `23-35 m`; yard datum `7.00` against
  the home's `7.44`, so the `0.44 m` step is a kerb, not the cliff a
  far-side access would have produced.
- Guards added so this cannot regress silently: every yard part must be
  within `46 m` of the home, the dead tree within `34 m`, and the ring must
  keep all `24` segments.

## 2026-08-14 — Put the Pipeback Roller on the yard circuit, drifting

- New `Assets/Scripts/Runtime/City/Yard/`: `YardWheelchairMotion` (pure
  pose math), `YardWheelchairPlan` (reads the circuit back out of the
  authored dressing — trunk gives the centre and ground height, the worn
  ring segments give the radius, so rider and track can never drift
  apart), `YardWheelchairPresentation`, `YardWheelchairActor`,
  `YardWheelchairFactory`, `YardWheelchairProvider`.
- The drift is the whole point: the chassis is yawed `19° ± 7.5°` into the
  circle *against* the direction of travel, the slip breathes over `0.37`
  laps, the pace sags and recovers in the same phase (`1.05 m/s ± 8%`), the
  ridden line wanders `±0.14 m` off the worn ring over `0.83` laps, and the
  body holds a `4.5°` outward lean. Wheels turn from real distance with an
  inner/outer differential plus a scrub factor from the slip angle;
  casters trail round to point where the chair is actually going rather
  than where it faces; bellows pump and the pipe bank rocks.
- Isolation respected exactly as specified: the prefab stays at
  `Assets/Pedestrians/Staged/Prefabs/`, out of `Resources`, out of
  `CityPedestrianResources`/`OrderedArchetypes`, and is never passed to
  `CityPedestrianFactory`. The only reference is a serialized
  `YardWheelchairProvider` asset at `Resources/City/`, which is what the
  factory loads — the prefab itself is never `Resources.Load`ed.
- `CityPedestrianPresentation` was deliberately NOT reused: it grounds a
  walker by its shoe soles, and this NPC has to sit on its wheels. The new
  presentation builds its own two-clip manual `PlayableGraph`
  (`PipebackIdle`/`PipebackRoll`) with an `AnimationPlayableOutput`, since
  the staged prefab ships no `AnimatorController`. Prefab passivity is
  re-validated at instantiation.
- Wired in `CityGameRoot` beside the bus, with `yard_wheelchair_present`
  and `yard_wheelchair_radius` in the init log.
- Tests: new `YardWheelchairMotionTests` — the plan matches the authored
  ring segment-by-segment, the pose holds the circuit and always carries a
  slip angle, the drift mirrors with direction, a lap returns to its start,
  non-positive steps are ignored, and the wheel differential favours the
  outer wheel and grows with distance.

## 2026-08-13 — Dressed the home yard around a circuit nobody else uses

- First authored yard composition, in `CityOpenAreaDecorationPlanner`:
  a third `BuildYards` call beside `BuildLake`/`BuildCemetery`, dressing
  only `yard-east` (the other four wait for their own descriptions).
  Nine new `CityOpenAreaDecorationKind` values and four styles
  (`YardWornTrack`, `YardTimber`, `YardPipe`, `YardPaint`) with colours in
  `CityOpenAreaWorldBuilder.ResolveColor`; the flat ring and the dropped
  toy are declared non-blocking.
- Composition follows the art bible's rule that this city is made bleak by
  subtraction, not by piling on rubbish: a bare dead trunk with two broken
  limbs at the centre, a 24-chord worn ring at radius `6 m` around it, and
  seven edge traces — repaired bench (one leg swapped for painted pipe),
  carpet-beating frame, empty sandpit, one child's toy as the only
  saturated colour, dead lamp post, bin beside the entrance, one bottle.
  The yard emits no light at all, by design.
- Placement rules: everything derives from the union of the yard's surface
  bounds and the declared access; edge objects rotate their authored angle
  in quarter turns until the footprint clears the street approach, and the
  bin is offset sideways from the entrance rather than standing in it (the
  first version put it straight in the approach and was rejected by the
  planner's own clearance check). Ring chords are short, so nothing spans
  a `48 m` batching chunk. Randomness is one salted `StableHash`
  (`0x59415244`) that only spins the edge ring.
- The middle of the ring is deliberately left empty for the wheelchair
  rider; the model is still being authored, so no actor, presentation or
  placeholder was added in this pass. The character contract is recorded
  in the plan: chair as an unrigged `GEO_*`/`PIVOT_Wheel*` prop (the shared
  31-bone rig has no wheel bones), rider on the shared rig, registered
  outside the pedestrian catalog.
- Tests: `CityOpenAreaDecorationPlannerTests` gained a yard fixture
  (determinism, per-kind counts, containment in the yard ground, ring stays
  non-blocking, circuit centre free of props) and its clearance loop was
  fixed — `.Single(access.Feature == descriptor.Feature)` threw once five
  yards shared one feature. `CityLayoutGeneratorTests`' "yards carry no
  decoration" assertion is inverted to "only `yard-east` is dressed".
- Art bible gained §10a for the yard in the same shape as the four public
  places (essence, movement grammar «замкнутый круг», light, Нельзя,
  Проверка).

## 2026-08-13 — Typed the boundary voids as Yards

- The three unmapped regions behind the boundary streets are now five typed
  `Yard` areas: `yard-east` (`RectInt(12,2,4,6)`, the pocket beside the
  player's home between cemetery and lake) plus `yard-south-{west,east}`
  and `yard-west-{south,north}` — one-cell perimeter strips halved so each
  aligns to its own access datum on the terraced perimeter. `(-1,-1)` and
  the `x15/z0-1` notch stay void. Blueprint cells `198 -> 246`; yard open-area
  accesses `5`.
- New `CityAreaFeatureKind.Yard` + `CityDistrictKind.Yard` wired through
  every gate that defaults to throw: combination/topology/structural
  validation, `IsSpecialArchetype`, the `CreateUrbanArea` guard, the
  required-access lists in `CitySurfacePlan` and `CityLayout`, elevation
  datum + preferred stair connections, `RequiresAuthoredAccess`, the world
  ground bucket (`OpenGround` -> new `YardGround` colour) and the map
  (`YardLand` fill + `map.district.yard` = «Двор»/"Yard" in ru/en).
  `CitySurfaceKind.OpenGround` — previously unreachable — is now the yard's
  surface kind.
- Stage 2 relaxed one declared invariant deliberately: only the lot and
  road-grid footprint must normalize to `(0,0)` (every per-cell random
  stream hashes raw coordinates, so shifting the grid would regenerate a
  different city); the `OpenLand`/`Water` fringe may reach `-1`, bounded by
  a named constant. `ValidateNorthWaterfront` and its test now scan the
  normalized `x >= 0` range.
- Three determinism hazards were identified up front and neutralized:
  yards are excluded from `TryResolveSignatureStairOwner` (else the four
  district stairs re-rank), from bus-corner support in
  `CityBusIntersectionSelector` (else the home stop drifts to the new
  boundary corners), and the new `EnsureYardAccessEdges` runs *after* every
  RNG consumer so it is a no-op on the canonical city instead of re-seeding
  the road graph.
- Zero decoration by construction: `CityOpenAreaDecorationPlanner` still
  only builds lake and cemetery, and a test asserts no yard descriptor
  exists. The yards are placeholders awaiting authored content.
- Verification: EditMode city suites green (`75/75` across
  `CityLayoutGeneratorTests`, `CityElevationPlannerTests`,
  `RoadFencePlannerTests`, `CityOpenAreaDecorationPlannerTests`,
  `CityMapDistrictPresentationTests`, `LocalizationCatalogTests`),
  including new `DefaultCoastalBlueprint_CreatesReachableEastYard`,
  `...CreatesReachablePerimeterYards` and a
  `DefaultSeed_KeepsCanonicalHomePlacement` canary (home still `(11,5)` at
  `(143,-13)`, partner bar `(11,6)`, four public places). Nine unrelated
  failures in the full suite (bus/pedestrian/GameSessionState/day-night)
  reproduce on clean `HEAD` without these changes and are not caused here.
  PlayMode not run.

## 2026-08-13 — Gave the default city terrain and exterior stairs

- Added a pure immutable `CityElevationPlan` between blueprint topology and
  spatial materialization. The default coastal blueprint now spans `12 m`,
  every urban district has at least `1.5 m` of local terrace variation, water
  keeps declared sea/lake datums, and legacy/custom blueprints stay exactly
  flat. One sampler now grounds nodes, cells, lots, entrances, returns, public
  places, open-area access, stops, waiting slots and debug teleports.
- Rebuilt City ground as deep terrace slabs and streets as oriented graded
  road/sidewalk/paint meshes with level junction pads. One shared boundary
  plan emits radius-safe connectors where road and ground differ by at most
  the `0.28 m` controller step and physical guards everywhere else; decorations, facade proxies, fences, night
  fixtures, park dressing, lake/cemetery dressing and Home's same-seed exterior
  transform inherit their local datum.
- Added one validated signature stair street in Old Town, Residential,
  Industrial and Nightlife. Each has `6-12` visible collider-free steps,
  `0.15-0.17 m` rise, `0.30-0.34 m` tread, two `1.5 m` landings, physical
  rails/retaining walls and exactly one hidden continuous ramp collider. The
  pedestrian graph includes both stair directions while a parallel grade-safe
  Street edge preserves Route 01.
- Made Route 01 elevation-aware end to end: grade-filtered links and 3D
  samples, level turns, local stops/waiters/boarding docks and actor pitch with
  roll locked. Fresh/return spawns and map test-teleport resolve the same live
  surface. The player contact patch now follows the collider normal; a balance
  check refuses to start on slopes above `12°`.

Verification:

- `dotnet build BarPromenade.Runtime.csproj -nologo --verbosity quiet` passed
  with `0` warnings and `0` errors.
- Focused EditMode `CityElevationPlannerTests` passed `8/8`; focused EditMode
  `CityExteriorStairModuleTests` passed `4/4`. Focused PlayMode
  `CityBusRidePlayModeTests.ProductionCityDoorDocks_MatchPhysicalSurfaceHeight`
  passed `1/1` after proving every Route 01 transfer dock against the built
  collider surface. Full suites and a player build were intentionally omitted
  in fast mode.

## 2026-08-13 — Textured the player's apartment

- Twelve deterministic seamless apartment albedos (wallpaper, ceiling
  plaster, painted planks, dark wood, worn laminate, upholstery, bed linen,
  bathroom tile, white enamel, painted metal, concrete, entry rug) from a
  new Pillow generator `tools/build-home-textures.py` — facade-pipeline
  structure (1024 source / 512 import, periodic-by-construction noise,
  `--verify`, SHA256 manifest in `ArtSource/Home/home-textures.json`,
  contact sheet). Compensation constants are solved per sheet with the
  city-facade **linear** rule against the exact builder tints (channels
  below `0.09` clamp-checked only — sRGB toe); the stairwell gamma rule
  would have over-brightened the dark home palette up to 2x.
- Extracted `SurfaceAppearanceCore` (projection enum, metre tiling,
  stable-hash UV offsets, display tint) out of
  `StairwellSurfaceAppearance`, which now delegates with bit-identical
  hash order; new `HomeSurfaceAppearance` (12 recipes, lazy cache,
  `[RuntimeInitializeOnLoadMethod]` reset, hash salt `1000 + kind`) plus
  `HomeSurfacePrimitives.CreateBox/CreateCylinder` wrappers.
- Threaded through the builders keeping every existing `Color` as the
  tint: `HomeInteriorWorldBuilder` (shell, facade piers, furniture),
  `HomeBathroomBuilder` (walls, three tile planes, porcelain fixtures,
  pipes), `HomeBalconyWorldBuilder` (facades, deck, rails, frames, door
  leaf), `HomeInteriorDressingBuilder` (window boards, radio, radiator
  pipes), `HomeRefrigeratorWorldBuilder` (cabinet, front frame, cavity
  liners, shelves, door). Decal overlays (damp/peel/stains), sub-`0.45 m`
  props, the alarm clock and fridge food stay flat-tinted by an explicit
  exemption list; `HomeExteriorViewBuilder` untouched (already textured by
  the city systems).
- `HomeOccluderDither.shader` now declares and samples `[MainTexture]
  _BaseMap` (default white) plus `_Smoothness`/`_Metallic` in ForwardLit,
  so textured furniture keeps its albedo through visibility fades instead
  of flashing to the compensated flat tint — the MPB survives the
  controller's `sharedMaterial` swap untouched.
- New EditMode `HomeSurfaceAppearanceTests`: 12 import contracts + raw-PNG
  opacity/seam/contrast checks, a C# re-derivation of the linear
  compensation rule from the shared tint table, MPB apply/stability/
  projection tests, a dither-shader `_BaseMap` regression guard, and a
  full `HomeInteriorWorldBuilder.Build` walk asserting every ordinary
  renderer is textured or on the exemption list with all 12 sheets seen.
  Unity-side test runs still pending (editor import of the new PNGs
  required); generator `--verify` passes twice with identical hashes.

## 2026-08-13 — Quest system, journal menu and the feed-the-cat gate

- Added a minimal data-first quest core: `QuestId`/`QuestStatus`/
  `QuestDefinition`/`QuestCatalog` plus a pure `QuestLogState`
  (activate-once, complete-once), owned by `GameSessionState` next to the
  inventory. `ResetToDefaults` seeds `FeedTheCat` as active on every new
  game; activation and completion are logged under the `quest` channel.
- `StairwellCatInteraction` completes `FeedTheCat` at the moment the
  prepared feeding actually begins (the can is already consumed and the cat
  eats), not at the exit clip, so an aborted exit cannot lose the
  completion.
- New `StairwellQuestDescentBlocker` in the stairwell root: while the quest
  is active, crossing `0.35 m` below the middle-landing elevation on the way
  down shows the localized `quest.feed_cat.block.descend` line through the
  existing `InteractionPromptView` feedback panel and drives the hero back
  to a landing return pose via `PlayerMotor.MoveTowardsInteractionPose`
  with input locked; stall detection ends the walk gracefully. Crossing
  detection (previous sample above, current below) means a spawn below the
  threshold can never trap the player.
- While the quest is active `GameSessionState.EvaluateInventoryItemUse`
  returns the new `InventoryItemUseStatus.ReservedForQuest` for
  `OpenStewCan`, so the inventory eat action refuses with
  `inventory.use.failure.reserved_for_quest` and keeps the can; the closed
  can stays edible. The cat feeding itself removes the item through the
  target-interaction path, which is deliberately unaffected.
- New `JournalController` (J / gamepad RB — Select was already the map)
  in all five gameplay roots: shared `BarMinigameModalLock`, frozen time
  scale like the inventory, localized quest list with per-status
  description and IN PROGRESS/DONE tags. Eleven new ru/en localization
  entries.
- Focused new EditMode `QuestLogTests` passed `5/5` (new-game activation,
  one-shot completion, reserved/released/closed-can consumption rules);
  the run also compiled the runtime assembly. Full suites and a player
  build were intentionally not run. Batchmode serializer churn in
  `Assets/Vehicles/Materials` was reverted, not committed.

## 2026-08-13 — Working bus windshield wipers

- The bus carried two static wiper cylinders welded into one `GEO_Wipers`
  mesh, so nothing could move when the new weather rained on the windshield.
  Bumped the deterministic generator to `1.4.0`: each wiper is now its own
  arm-and-blade mesh under an authored base pivot (`PIVOT_WiperL/R`, roles
  `left_wiper`/`right_wiper`) on the body at the exact old base points, with
  rest geometry matching the old diagonal pose. Generator validation gained
  wiper control-pivot and single-owned-mesh checks; 47 meshes, 4176
  triangles, new signature.
- `CityBusAssetRegistry` binds both wiper pivots (validated by
  `CityBusAssetSetup` alongside the other articulation bindings and reset by
  `ResetArticulation`). `CityBusPresentation.AdvanceWipers(rain, dt)` sweeps
  them `±40°` in mirrored directions around a model-derived windshield-normal
  axis (`ResolveForwardAxisLocal`, same lesson as the wheel vertical axis):
  sweep rate lerps `0.35-1.15 Hz` with rain intensity, a dry frame parks the
  blades at `110°/s` instead of freezing them, and a rain restart re-enters
  the sine sweep at the parked angle's own phase so blades never teleport.
- `CityBusActor.Advance` gained an optional `rainIntensity` argument fed by
  `CityBusDirector` from a new provider that defaults to the pure
  `GameWeatherRules` schedule, mirroring the night-factor provider; existing
  three-argument call sites keep compiling with parked wipers.

Verification:

- Blender 5.0.1 regenerated and self-validated the model
  (`CITY BUS 3D BUILD OK`); batch `CityBusAssetSetup.RunBatch` rebuilt and
  validated the prefab (`CITY BUS UNITY ASSET BUILD OK`).
- Focused EditMode `CityBusAssetImportTests` + `CityBusRuntimeTests` passed
  `29/29` with zero C# warnings, including the new
  `PresentationWipers_SweepWithRainAndParkWhenDry` (sweep bounds, mirrored
  blades, smooth parking, pool reset) and the extended import checks for the
  wiper pivots, bindings and `ResetArticulation`. The first run exposed that
  the synthetic `RuntimeFixture` registry predated the new optional wiper
  bindings; the fixture now authors both pivots. PlayMode, full suites and a
  player build were intentionally omitted in fast mode.

## 2026-08-13 — Thunderstorms and balcony weather audio

- Extended the same-session weather schedule with a fourth slot kind:
  `Thunderstorm` (`6%`, carved from heavy rain, which drops to `12%` and
  light rain to `27%`). A storm carries full heavy-rain intensity plus
  lightning from the same pure schedule: each `12`-game-minute window of a
  fully developed storm slot hashes into at most one strike (`70%`) with a
  deterministic start offset, azimuth and distance band
  (`GameWeatherRules.EvaluateLightning`), so City and the Home balcony flash
  the identical storm without any new session state.
- The flash is one transient shadowless directional light
  (`CityLightningFlashLight`) with a flickering `0.5`-game-minute decay
  envelope, peak intensity `1.9` scaled down to `45%` at the far distance
  band. It stays disabled outside a flash and lives outside `Night.Root`, so
  the pooled 12+4 light budget and the existing light-count assertions are
  untouched. A frozen clock (pre-wake `05:59`, pause `timeScale = 0`)
  suppresses the flash instead of holding it lit.
- Thunder is a deterministic synthesized one-shot (`CityThunderSynthesis`:
  crack over brown rumble with a delayed secondary roll) played `0.6-3 s`
  after its flash with distance-scaled volume and low-pass cutoff on the
  `Ambience/Details` group. Two rotating voices sit on child objects because
  an `AudioLowPassFilter` processes every source of its own GameObject.
- Per user request the balcony now hears the weather too:
  `HomeBalconyExteriorAtmosphere` owns its own rain bed, thunder player and
  flash light beside the rain field, gated to the active Balcony shot —
  stepping inside silences the bed and drops the flash while the rain field
  keeps simulating like the fog.

Verification:

- Focused EditMode `GameWeatherRulesTests` passed `9/9` (four-kind coverage,
  storm-only lightning gating, in-storm flash bounds/determinism, ramp
  boundaries now searched by target intensity so a heavy->storm border cannot
  produce a degenerate assertion); zero C# warnings or errors. PlayMode
  suites, player build and smoke intentionally omitted in fast mode.

## 2026-08-13 — Deterministic rain in two intensities

- Added the first exterior weather system as a pure schedule plus
  presentation, with no new session state. `GameWeatherRules` (Core) maps the
  city seed and absolute game minutes into `90`-game-minute slots — Clear
  `55%`, LightRain `30%`, HeavyRain `15%` — and smoothsteps the continuous
  rain intensity between slot targets (`0` / `0.45` / `1.0`) over the first
  `5` game minutes, so City and the Home balcony always sample identical
  weather and scene loads cannot desynchronize it.
- `CityRainField` mirrors the `CityFogField` pattern: a seeded,
  player-following runtime particle system of stretched streak billboards on
  the shared `CityAtmosphereParticle` material (at most `420` particles over
  a `26 x 26 m` box from `12 m` up, world-space, no collision). Intensity
  continuously scales emission, streak width, alpha and velocity stretch, so
  light rain reads sparse and thin while heavy rain reads dense and long.
  While the hero rides the bus the emitter switches to a donut with a `10 m`
  rain-free core, because streak billboards would otherwise spawn inside the
  cabin.
- `CityWeatherController` on `CityGameRoot` samples the rules every frame,
  drives the field, logs `weather_changed` NDJSON events on kind changes and
  feeds `CityRainSoundPlayer` — a deterministic crossfaded xorshift-noise
  loop (`CityRainAmbienceSynthesis`, mono `22050 Hz`, 4 s) whose volume and
  low-pass cutoff track intensity on the `Ambience/Beds` group.
  `HomeBalconyExteriorAtmosphere` builds the same rain field at its fog
  anchor, toggles its renderer with the Balcony shot exactly like the fog
  renderer, and updates intensity per frame; the balcony adds no rain sound.
- Deliberate boundary: rain does not modify `GameTimeDayNightRules`,
  `RuntimeSceneSetup` lighting, fog, grade or far clip — those contracts are
  asserted exactly by existing City/Home PlayMode suites. Daylight dimming,
  wet surfaces and balcony rain audio are recorded as open gaps in
  `ai/systems-map.md` and `ai/architecture-notes.md`.

Verification:

- New focused EditMode `GameWeatherRulesTests` passed `7/7` in Unity
  `6000.5.5f1` (determinism, plateau targets, boundary ramp, all-kinds
  coverage, seed sensitivity, clamping, non-finite rejection); the run
  compiled Runtime and both test assemblies with zero C# warnings or errors.
- Fast mode intentionally omitted PlayMode suites, a player build and smoke.

## 2026-08-13 — Bus albedos and visible pendant cabin lamps

- The bus was the last flat-colour hero object on a textured street, and its
  "cabin light" had no visible source: the `LGT_CabinStrips` boxes were
  centred at `2.765 m`, entirely inside the `2.72-2.78 m` interior ceiling
  panel, so the emissive meshes could never be seen and the two runtime cabin
  Spots floated at `2.83 m` inside the roof.
- Bumped the deterministic bus generator to `1.3.0`. Every mesh now carries
  world-scale box-projected UVs (per-slot metre tiling, so Unity materials
  stay at `(1, 1)`), the ceiling strips protrude below the panel, and two
  pendant lamps hang on the aisle centreline at source `y = ∓1.45` — metal
  stem, trim collar and a `CabinLight` bulb spanning `2.56-2.66 m`. The new
  `cabin_lamp_bulb` role joined the generator's required-role validation.
- Added `tools/build-city-bus-textures.py`: four deterministic tileable
  512 px albedos (paint with panel seams/rivets/grime streaks, brushed metal,
  speckled ribbed linoleum, seat weave), light bases near `0.75-0.8` mean
  luminance so the existing flat `_BaseColor` values keep the hue.
  `CityBusAssetSetup` assigns them per slot (`Body/Accent`, `Metal/Rail`,
  `Interior/Dashboard`, `Seat`) and its prefab validation now fails if a
  mapped material loses its `_BaseMap`.
- `CityBusPresentation` moves the two cabin Spots from the roof interior down
  to the authored bulb centres (`2.61 m`), raises their base intensity
  `5.5 -> 7.5` and warms the night cabin emission so the bulbs read as the
  actual source. Light count, names and directions are unchanged, keeping the
  12+4 city light budget.

Verification:

- Blender 5.0.1 regenerated and self-validated the model: 46 meshes, 4136
  triangles, new signature; `CITY BUS 3D BUILD OK`.
- `python tools/build-city-bus-textures.py` reported mean luminances
  `0.75-0.81` for all four sheets.
- Batch `CityBusAssetSetup.RunBatch` rebuilt and validated the prefab
  (`CITY BUS UNITY ASSET BUILD OK`), including the new albedo binding check.
- Focused EditMode `CityBusAssetImportTests` passed `4/4` and
  `CityBusRuntimeTests` passed `28/28`, including
  `PresentationNightLights_AreSprungScaledAndPoolSafe`. Full suites,
  player build and smoke were intentionally omitted in fast mode.

## 2026-08-13 — District walls for the city buildings

- The street had textured ground under untextured boxes. Every road, sidewalk
  and patch of soil carried a real albedo; the buildings standing on them were
  flat colour, and the four districts differed only by the seeded RGB range in
  `CityLayoutGenerator.CreateBuildingColor` — which `ai/city-zones-art-bible.md`
  §18.4 rules out as sufficient on its own.
- Added `tools/build-city-facade-textures.py`, the first scripted world albedo
  in the project: eight district walls plus a shared roof cap, two per district
  so each carries both of its material axes. Pillow only, deterministic, with
  its own validator covering opacity, wrap, macro contrast, mean luminance,
  channel neutrality and the accent-area ceiling the bible imposes on saturated
  colour.
- Added `CityFacadeGrid` as the one source of the bay and floor pitch. The pane
  arithmetic had been duplicated three times (`BuildWindowBands`,
  `BuildWindowRow`, and `HomeExteriorViewBuilder`'s copy of both); a fourth
  consumer that derives a texture's UV from one copy while geometry comes from
  another would drift silently.
- Added `CityFacadeAppearance`, which tiles the albedo by the building's own
  window grid instead of by metres, so one authored cell covers exactly one
  pane bay and one `2.35 m` storey. Horizontal phase follows the pane-count
  parity, vertical phase is independent of building height, and a stable
  per-lot whole-cell rotation varies presentation without disturbing either.
- **Measured, not assumed.** Facade widths are `11.78–15.5 m` and heights
  `5–13 m`, so `paneCount` is only ever 4 or 5, bay pitch `1.96–2.45 m` and the
  glass fraction of a bay `0.857–0.886` — a ±1.7% spread. That tightness is
  what makes one authored bay land on every real bay within ~3 cm.
- **Two corrections worth recording.** First, the brightest channel any lot can
  reach is `0.616` (a bar), not the `0.36` an earlier sweep suggested; that
  sweep minimised the other channels instead of maximising them. Second, and
  more consequential, reusing `StairwellSurfaceAppearance`'s
  `compensation = 1 / meanLinearLuminance` would have been wrong here: that
  rule assumes the tint and texture multiply in gamma space, while URP converts
  both to linear first. It called for a mean of `0.64`; solving the linear form
  gives `0.35`, and shipping `0.64` would have made every facade in the city
  87% brighter than it is today. The pale chalky result was visible in a shaded
  preview before any of it reached the engine.
- Known limitation, recorded rather than worked around: a repeating sheet
  cannot carry a plinth, because no cell is reliably the ground floor. The
  bible's heavier darker base is not expressible here; the grime runs darken
  the lower part of every floor cell instead.
- Verification: `python tools/build-city-facade-textures.py` — all nine sheets
  pass edge `1.25–3.48` (cap 16), seam `0.25–0.90x` (limit 2.5), contrast
  `99–206` (floor 40), chroma `1.006–1.128` (limit 1.22), mean `0.3496–0.3503`.
  One focused EditMode selection, `CityFacadeAppearanceTests`, 20/20 green.
  Mutation check: dropping the `0.08 m` mass-base term from the vertical phase
  turned the alignment case red with a drift of `0.034` of a cell, which is
  exactly `0.08 / 2.35`; restoring it returned the selection to green.
- The generator's own checks earned their keep twice: the seam ratio caught a
  brick module pitch of `40 px` that does not divide `1024` and so restarted
  mid-brick at the wrap, and then caught Pillow's convolution clamping at the
  border rather than wrapping, which was manufacturing a one-pixel seam on the
  roof gravel. Both are fixed at the source; `wrap_filter` now pads before
  every convolution.

## 2026-08-12 — Bar signs became geometry

- The bar sign was the one part of a facade that did not live in the world: a
  `40 x 48` procedurally drawn pixel sprite on a `BillboardSprite`, turning to
  face the camera while the bracket arm it hung from stayed put. The two came
  apart at any oblique angle, and from the balcony it kept its size and facing
  while every other surface foreshortened.
- It is now a projecting blade sign built from the same collider-free boxes
  and shared material as the rest of the facade, hanging under the existing
  bracket and reading along the street the way a real projecting sign does.
  Eight boxes carry it: two hangers, three panel layers and a three-box
  tankard. Each layer is smaller across the panel than the one behind it but
  slightly thicker across the blade, so the layer behind survives as a border
  without four boxes per frame edge. The palette is the pixel panel's, so the
  bars stay recognisable at the distance they always were.
- `BarBuildingMarker` kept its name, its `BarId` and its place in the
  hierarchy - `Bar Landmark Marker` is still what the balcony reconstruction
  looks for - but it is now a passive identity that records the plates hung
  under it instead of leasing a shared sprite and texture.
- The smoke test's contract moved with it. It used to rotate the camera and
  require every marker to keep facing it; it now captures each plate's world
  pose, swings the camera a quarter turn and requires the signs **not** to have
  moved, and asserts no part of a bar facade billboards at all. The
  shared-asset rule it enforces changed from one shared sprite to one shared
  material, which is the rule this project actually has.
- Verification: `CityScene_BarsHaveUniqueColliderFreeSignGeometry` passes.
  `HomeScene_BuildsWalkableBalconyOnSeededStreet` fails, but **not from this
  work**: it dies earlier at `HomeBalconyPresentationPlayModeTests.cs:215`
  demanding a collider-free exterior reconstruction and finding `Street Lamp
  Chunk` box colliders. Stashing this change and re-running at `16bac4e`
  reproduces it identically, and that commit touches no night, lamp, exterior
  or decoration builder. The failure predates all of it, and it means the
  balcony marker assertion here is compiled but never reached.

## 2026-08-12 — Four presentation defects on the bus, all measured

- **Passengers sat inside the cushion.** The runtime aligns the shared rest
  pelvis to the seat anchor, so the lift has to equal how far a design's own
  seated hips reach below that bone. Nominal `0.015` was guesswork. The
  generator now measures it — `seated_contact_m`, the lowest point of the
  parts bound to `pelvis` and `thigh.L/R` relative to the pelvis — and the
  catalog was sunk by `4.6 cm` (Lampshade), `5.2` (Chair Carrier), `5.4`
  (Long-Arm) and `11.1` (Kettle Hat, whose belly and wide hips reach furthest
  below the bone). Lifts are now the measurement less `0.01 m`, so the cushion
  reads as compressed rather than the passenger as floating, and
  `CityPedestrianRuntimeTests` asserts the declared lift stays inside
  `[contact - 0.03, contact]` so the two cannot drift apart again.
- **The driver sat in his seat the same way**, `2.4 cm` down. Measured at
  runtime instead, because his seated pose is procedural rather than an
  authored clip: his hip geometry reaches `0.0387 m` below the pelvis, so
  `DriverSeatLift` is now `0.029`. The thighs are deliberately excluded from
  that measurement — they slope to the pedals, so their lowest point is a knee
  at `0.355 m`, nothing that rests on a seat.
- **The driver kept staring at the hero through door closing and departure**,
  and since both were then moving relative to each other his head jerked away
  from every stop. `UpdatePlayerFocus` never consulted the doors at all. The
  focus is now gated by `DoorLookWeight`, which already carries exactly the
  right envelope: up through Opening, held while open, down through Closing,
  zero under way. Proximity and permission stay separate — `IsPlayerNearFrontDoor`
  is still the ungated fact about where the hero is, because that is what it
  means.
- **The front wheels steered about the wrong axis.** A probe of the imported
  hierarchy showed the bus up direction reads as `(0, 0, -1)` in a wheel
  pivot's local space, while `ApplySteeringPose` rotated about `Vector3.up` —
  the longitudinal axis, so the wheels leaned instead of turning. Rolling uses
  the local lateral axis, which survives the same mapping, which is why only
  the steering looked wrong. The steering axis is now derived from the model
  once at capture (`ResolveVerticalAxisLocal`) rather than assumed, so a
  re-export cannot silently reintroduce it. The steering wheel already needed
  its own declared `+Z` axis for the same reason — that was the clue.
- Verification: the deterministic art build for the new `seated_contact_m`
  measurement, then one focused EditMode selection over the pedestrian, bus
  runtime, bus asset-import and stop-wait fixtures — 58/58.

## 2026-08-12 — The bus could not cover the last 30 cm into a stop

- Reported as: the bus drove up to stop `02`, stood there with its doors shut
  for about fifteen seconds, and only then opened up and let the waiter on.
  The NDJSON ruled out my first two guesses outright — no `service_hold_expired`,
  so the dwell timer was never frozen, and `board_started` -> `board_completed`
  in `4.5 s` against an `8.36 s` budget, so the transfer itself was healthy.
  Whatever happened, it happened *before* the dwell began.
- A four-lens audit with an adversarial refutation pass found it, and two
  independent lenses reached it separately. `MoveAlongRoute` discarded any
  frame whose travel was under `DistanceTolerance = 0.02 m` rather than
  carrying it forward. It is a latch, not a rounding loss: the discarded
  travel leaves the distance unchanged, so the braking-curve speed cap is
  unchanged, so the next frame is under the threshold too. `BeginDwell` never
  runs, and since the doors are driven only from the dwell timer they never
  open.
- **The regime matters, and my first explanation of it was wrong.** I wrote
  that a `60 fps` cruise approach latches once the stop is within `0.31 m`.
  A faithful float32 replay of `AdvanceMotion` + `MoveAlongRoute` says that
  regime did not occur here: the session ran at a `25 ms` median frame
  (~40 fps), and at `40 fps` a clean cruise approach **arrives every time** —
  the cruise path only starts latching from `44 fps` up. Coming down from
  cruise, `MoveTowards` saturates at `ServiceDeceleration * deltaTime` and
  keeps the bus overspeed against the curve, so it punches through the band.
- What actually bit is the **from-rest regime**. Setting off again from a
  standstill or any low speed, the bus never rises above the band at all: at
  `40 fps` a frame commits motion only while `v > 0.80 m/s`, and the curve
  drops under that `0.14 m` from the stop. The replay latches on 100% of
  from-rest approaches within `12 m`, resting `2-12 cm` short — i.e. visually
  docked at the stop, which is exactly what was reported. Escape needs one
  frame long enough to clear `2 cm` at the pinned speed, `28-61 ms` against a
  `25 ms` median: ordinary jitter, hence an arbitrary duration, an
  instantaneous release, and a textbook dwell afterwards.
- **So the trigger was probably a yield after all**, and my "ruled out"
  verdict on that was too strong. A yield only has to last a fraction of a
  second to zero the speed (`travel = safeTravel; speed = 0f`); the latch then
  supplies all fifteen seconds. That dissolves the objection that nobody saw
  anyone standing in front of the bus. `JunctionSpeed = 3.2` can arm it the
  same way with no obstacle at all.
- The fix carries the residual instead of dropping it: one `pendingTravel`
  field accumulated per frame and drained by the loop. `DistanceTolerance` is
  untouched, which matters because it appears in fourteen places including the
  arrival test itself (`distanceToStop <= tol && speed <= tol`, `:869`) —
  lowering it would have made arrival *stricter*. The same discard also hit
  every other slow-motion case: recovering from a yield, crossing to the next
  link, crawling a junction.
- **This was never an NPC bug.** `MoveAlongRoute` is original route code. The
  ambient passengers only made it visible: nobody used to watch the bus stand
  at a stop, and now somebody is standing there failing to get on.
- Why no test caught it: every bus test stepped at `0.05 s`, where the freeze
  band is `0.034 m` and hides inside the arrival tolerance.
  `ServiceApproach_ReachesTheStopAtRealFrameRates` now runs at `1/30`, `1/40`,
  `1/45`, `1/60`, `1/120` and `1/144`, and
  `ApproachResumingFromAYield_StillReachesTheStop` covers the regime that
  actually bit: hold the bus at a dead stop with an obstacle, release it, and
  require it to reach `Dwelling`. Mutation-checked: restoring the discard
  fails every frame-rate case with the bus stuck in `ApproachingStop` after
  sixty simulated seconds.
- Two follow-ups from the audit are now in. A full yield explicitly clears
  `pendingTravel`, so "a bus stopped for a person does not creep" is a stated
  contract rather than an accident of the loop threshold. And a stall
  watchdog reports `approach_stalled` once after `2 s` motionless short of a
  stop, carrying state, distance, speed, requested travel, `deltaTime`,
  `must_stop` and forward clearance — the one record that separates every
  hypothesis this investigation had to eliminate by simulation, plus a
  matching `approach_released`. It also corrects a comment of mine on
  `MaximumServiceHoldDuration` that claimed a leaked hold strands the bus with
  its doors *shut*; a hold can only be taken while they are fully open, so it
  strands them open. That false comment is what kept regenerating the leaked-
  hold hypothesis.
- **Correction to the previous entry.** The "waiter blocking its own bus"
  diagnosis was wrong, and so were its numbers. I computed the corridor from
  `ObstacleStopPadding = 0.38`, which belongs to `OverlapsDynamicObstacle`
  (`CityBusDirector.cs:554`), a spawn-overlap check. Yielding uses
  `lateralLimit = halfWidth + targetRadius + ObstacleLateralPadding`
  (`CityBusActor.cs:665`) — `1.71 m` for the hero, `1.74 m` for a walker, so
  someone on the sidewalk centreline clears it by `0.26-0.29 m`, not by
  `0.08 m`. The route-bound exemption is harmless and still defensible, but it
  was not what fixed anything and its comment needs rewording.
- Still open, found by the same audit and not yet acted on: the obstacle test
  also samples `player.position + playerVelocity * 0.75 s` with unsmoothed
  velocity, which widens the blocking corridor by up to `3.9 m` at run speed —
  wide enough that walking toward the bus in order to board it stalls it.

## 2026-08-12 — An end-to-end proof for ambient passengers

- Four separate defects broke ambient boarding in turn, each reported from a
  playtest, and every one of them left the planners, the occupancy rules and
  the asset contracts green. Nothing walked a passenger from the pavement into
  a seat and back out, so `Assets/Tests/PlayMode/CityBusNpcPassengerPlayModeTests.cs`
  now does exactly that against the production bus prefab and the real
  pedestrian pool: waiter appears, boards, is seated with `07` still free, and
  alights at a later stop with its dwell hold handed back.
- Three things about the harness were worth learning the hard way:
  - `passengers.enabled = false` silently kills the controller, because
    `OnDisable` calls `Shutdown`. The directors have to stay enabled and drive
    themselves from `LateUpdate`.
  - A nested `yield return SomeEnumerator()` is not driven by the test runner,
    so the phase loops run inline. The first version looked like a stuck bus
    when in fact nothing was advancing it.
  - `Time.deltaTime` in a batch run was observed at `0.006 s` on one attempt
    and pinned to the `6.7 s` ceiling on another, so frame budgets are
    meaningless and, worse, the bus, the walkers and the transfer budget can
    end up on different clocks — a service hold then expires under a passenger
    who is still walking. `Time.captureDeltaTime` pins one fixed step for
    everything, and the whole test runs in about three seconds.
- The coverage was mutation-checked rather than trusted: reintroducing the
  hero-only opposite-driver invariant makes it fail with the passenger riding
  past ten stops without ever getting off, which is precisely the reported
  symptom.

## 2026-08-12 — The waiter was blocking its own bus

- Reported as: the bus pulled up to stop `02`, the driver halted, the doors
  never opened, and a waiting walker stood there. The NDJSON was silent — no
  `board_started`, no `board_blocked` — which located it precisely, because
  the only silent guard in `TryBeginBoarding` is `DoorsFullyOpen`. The doors
  were the problem, not the boarding.
- The cause is geometric and self-inflicted. A `1 m` sidewalk minus a `0.35 m`
  capsule and two `0.15 m` navigation margins admits **exactly one** lateral
  position, `3.50 m` from the road centre; there is no freedom to place a wait
  slot anywhere else. The halted bus flank is at `2.69 m`, so the waiter
  stands `0.81 m` clear while the obstacle corridor reaches
  `AgentRadius + ObstacleStopPadding = 0.73 m`. That `0.08 m` of daylight is
  narrower than the walker's own `0.15 m` shoulder-shift, so a waiter that
  leans road-ward puts the bus into `Yielding` short of the stop. It then
  waits forever for a bus that can never serve it, and the bus never dwells,
  so nobody else boards either. A deadlock built out of two individually
  reasonable numbers.
- The slot cannot move, so the exemption moves instead: the bus obstacle test
  now skips `IsRouteBound` walkers, not merely `IsAttachedToVehicle` ones. A
  walker heading for a stop or standing at it is this bus's passenger, which
  is the same reasoning already accepted for the hero's door dock — his dock
  is deliberately kept outside the corridor so a waiting passenger cannot stop
  the bus reaching its service pose. Ordinary roaming walkers keep their
  yielding untouched.
- Found while investigating: `AdvanceWaiter` dropped a tracked record on two
  paths — walker gone, walker no longer route-bound — **without releasing its
  service hold or its cabin seat**. A leaked hold freezes `dwellElapsed`, the
  door timeline is sampled from that timer, and the next `BeginDwell` resets
  the timer to zero it can never leave: the bus would be stranded at every
  later stop with sealed doors for the rest of the session. Both paths now go
  through one `ReleaseWaiterOwnership` that always hands ownership back and
  warns when it actually reclaimed something. `CityBusActor` also bounds the
  freeze at `DwellDuration + 5 s` and reports `service_hold_expired`, so a
  future leak degrades to a hiccup with a named cause instead of a dead route.
- The existing `PassengerServiceHold_...` case advanced `DwellDuration * 2` in
  one step to prove the freeze, which now trips that bound; it advances
  `DwellDuration + 2` instead, still past the dwell it would otherwise have
  completed.

## 2026-08-12 — Route 01 stops zigzagging

- Reported as "the route and the stop order are extremely illogical", and the
  measurement agreed. `CreateStopTargets` ordered its targets by
  `GetDistrictOrder` — a hardcoded enum, Industrial `0` through Old Town `3`,
  home appended last — which is nominal and contains no geography at all.
- On the default layout that produced: Industrial `(-131, -13)` far west,
  Nightlife `(13, -79)` south centre, Residential `(128, 117)` far north-east,
  Old Town `(-131, 65)` **back to the west edge**, Home `(121, -1)` **out east
  again**. Two full crossings of the city per lap. Straight-line tour between
  stops `1166 m` against a best possible `754 m`; the road loop it forced was
  `2592 m`, `3.4x` the straight tour.
- The order is now a shortest closed tour over the target centres. Five
  targets are solved exactly — fix the first, permute the rest, `(n-1)!` — and
  a layout above `8` falls back to nearest neighbour plus 2-opt. Ties break on
  the ordered target IDs, and the cycle is rotated so `PlayerHome` is served
  first with its direction fixed the same way, so the same layout and seed
  always yield the same loop.
- Result on the default layout: Home, Residential, Old Town, Industrial,
  Nightlife and back — a clean ring with no doubling back. Straight tour
  `754.3 m`, exactly the optimum. **Road loop `2592 m` -> `1798 m`, a `31%`
  cut**, and the loop-to-straight ratio fell from `3.4x` to `2.4x`.
- Only the ordering changed. The accepted-link graph, the right-hand rule, the
  `6 m` left turns, the safe-right macro and every full-body clearance proof
  are untouched, which is why the whole existing planner suite still passes
  unmodified. The remaining `2.4x` is the street grid plus the turn
  restrictions; shortening that means touching the connector search, which was
  deliberately left alone.
- Verification: one focused EditMode selection over `CityBusPlannerTests`,
  `CityBusStopWaitPlannerTests`, `CityBusRuntimeTests` and
  `CityMapBusOverlayTests` — 36/36, including the new
  `ServedOrder_IsAShortestClosedTourStartingAtHome`, which asserts home is
  stop `01`, that the served order is within `5%` of the exact optimum over
  the real stop positions, and that a repeated build gives identical stop IDs.
  Not run: PlayMode, the full EditMode suite, any player build.

## 2026-08-12 — Ambient passengers ride Route 01

- The measurement that decided the design: every walker design is the *same*
  31-bone rig at the same rest pose. `Assets/Pedestrians/Models/*.json` agree
  bone for bone — pelvis head at `0.70 m`, envelope `1.75 m`, identical
  `localBounds.y`. "Different models" is mesh proportion and worn objects, not
  skeleton, so seating is **one** rule for all of them: align the shared rest
  pelvis to the cushion anchor, exactly as `CityBusDriverPresentation` already
  seats the driver. Per-design work then reduces to an authored seated posture
  and a declared clearance, not per-design maths.
- Sole pinning had to be switched off while seated. `GroundFeetToPresentationRoot`
  pins the lowest boot to the actor-root plane every frame; on a seat that
  drags the whole model down until the feet touch the cabin floor.
  `CityPedestrianPresentation` now runs a three-input mixer (Idle/Walk/Sit) and
  swaps the pin for pelvis alignment while seated.
- Four authored `Sit` clips joined the deterministic Blender library
  (`LampshadeSit`, `ChairCarrierSit`, `KettleHatSit`, `LongArmSit`), taking it
  from 10 clips to 14. They are excluded from the footwear bake — a seated clip
  leaves the pavement plane on purpose — and prove a different contract
  instead: measured headroom above the seated pelvis inside a declared band,
  and nothing hanging more than the `0.41 m` cushion height below it. Measured
  `1.030 / 1.055 / 1.050 / 1.050 m` headroom and `0.354-0.374 m` drop; the
  cabin gives `2.05 m` floor-to-ceiling, so the whole catalog clears the roof
  with room to spare.
- The Helmet Lamp Hopper declares no seated ride. It has no seated posture to
  author on `0.46 m` hind feet, and its worn Spot is the one working light the
  pedestrian contract allows — it does not belong in a cabin.
- `CityBusActor` grew from one passenger and one exclusive service hold to a
  three-place cabin with a shared, per-owner hold. The exclusivity had to go:
  with one hold, an ambient passenger stepping through the doorway would have
  silently made the hero's own `E` prompt fail. `CityBusDirector`'s passenger
  cleanup became multicast for the same reason. The release post-condition is
  unchanged — no occupant may remain when the presentation is pooled.
- Recycling now keys on `HasPlayerPassenger`, not `HasPassenger`. Blocking the
  single actor slot because an ambient rider is aboard would strand the bus for
  a whole lap; a rider `92 m` away behind fog is released with it instead.
- `CityBusRidePlan.TryCreate` turned out to be agent-agnostic apart from two
  hard-coded facts. Parameterising seat index, agent radius and grounded-root
  offset was enough to reuse the whole validated dock ladder for a walker, so
  ambient boarding inherits the curb/apron height resolution the hero already
  had rather than re-deriving it.
- Routing to a stop reuses the population director's existing guidance shape
  (`approachTarget` + a node-distance field feeding `SelectClosestCandidate`),
  but seeded at the stop instead of the player. Stops never move, so the
  Dijkstra runs once in `CityBusStopWaitPlanner` rather than being re-searched
  every few metres the way player guidance must be.
- Wait slots sit `0.70 m` road-ward of the blue pole. The pole is deliberately
  `0.2 m` outside the walkable strip and carries a collider, so waiting at
  `ShelterPosition` was never an option. The two slots queue along the lane at
  `+0.30 m` and `+1.40 m`, which also keeps them clear of both door entries
  (`+3.05 m` front, `-1.34 m` rear) — the same `1 m`-pavement geometry that
  already rules out walking abreast.
- NPC boarding does **not** go through `PlayerAnimatedInteractionController`.
  That controller is bound to `PlayerRuntime` and `IPlayerClipPresentation`,
  and `ai/contextual-animation-standard.md` explicitly does not govern NPC
  animation. A short scripted doorway walk with a `2.5 s` abort covers it.
- **Playtest fix — nobody boarded.** A waiter stood at the stop and the bus
  pulled up, but `board_started` never appeared in the NDJSON while
  `waiter_recruited`/`waiter_spawned` did, so boarding was refused before it
  began. The passenger door dock is pushed outward to `3.38 m` from the road
  centreline, the pedestrian lane band is `3.15-3.85 m`, and a `0.35 m` capsule
  there needs `3.03-3.73 m` — the dock overhangs the curb by `0.12 m`. Since
  the dock ladder offsets run *along* the bus and not across it, every
  candidate failed. The hero never hit this because his controller is given
  `World.WalkableArea`, which includes the carriageway. The controller now
  takes that same road-inclusive area, alighting targets the stop's proven
  pavement wait slot instead of the road-side dock, and a `board_blocked`
  warning names the refusing guard once per changed reason so the next failure
  is readable rather than silent.
- **Spawned cabins are not empty.** A bus that has notionally been circling
  its loop should not always pull up with nobody in it, so activation now
  seats a seeded `0-2` ambient passengers. Two things made that awkward and
  both are worth remembering. First, a full ride plan needs a served stop and
  two validated roadside docks, and a spawning bus is cruising — so
  `CityBusRidePlan.TryCreateSeatedPose` resolves the actor-local seat floor
  from the seat anchor plus the cabin-floor door anchor alone. Second, the
  spawn collision probe rejects a capsule overlapping the bus body, which is
  precisely the situation here, so seated spawns opt out of that one probe
  while every other spawn keeps it. The draw is `hash % (max + 1)`, so an
  empty bus stays a real outcome, and it draws against `MaximumNpcOccupants`
  rather than `CabinCapacity` so the hero's place survives.
- **Second playtest fix — the seat side, not the dock.** Ambient passengers
  still neither boarded nor alighted, and `board_blocked` named
  `no_door_dock`. The road-inclusive area had been necessary but not
  sufficient: `CityBusRidePlan.TryCreate` also enforces `driverSide *
  passengerSide < 0`, and the ambient seat order starts at index `2` on the
  driver's side. Seven of its eleven seats are, so nearly every plan was
  rejected — the exit plan included, which is why nobody got off either. The
  preload had worked only because `TryCreateSeatedPose` never ran that check.
  The rule is hero-only: seat `07` must be opposite the driver because his
  authored `BusRideLoop` and the window camera are built around that lateral
  side, and an ambient passenger has neither. It is now an explicit
  `requireOppositeDriverSide` parameter, true for the hero and false for
  everyone else. Lesson: `board_blocked` earned its keep, but one reason
  string covered two independent guards.
- **Teardown throw.** `CityBusDirector.Shutdown` hit "Passenger cleanup must
  release the city bus passenger before its presentation is pooled". Cleanup
  decided who was aboard by reading the *walker's* motion state, and on
  teardown `CityPedestrianDirector.OnDisable` may pool its actors first and
  reset them to `Dormant`, so the loop skipped a real occupant. The bus is the
  authority on its own cabin: cleanup now calls `ReleasePassenger` for every
  tracked record and uses its return value, which is order-independent.
- **Third playtest fix — the transfer could never finish.** The log told the
  whole story: `board_started` followed by `transfer_aborted` exactly `2.525 s`
  later, three times, and never a `board_completed`. `TransferTimeout` was a
  flat `2.5 s` guess. Measuring the real walk against the bus manifest: the
  aisle leg runs `1.16-2.56 m` when the door is chosen sensibly, the pavement
  leg is about `3 m`, and the four riding designs walk at `0.72-1.30 m/s` — so
  a real transfer needs `4.7-7.7 s`. No single constant fits a spread that
  wide, which is why every ambient passenger aborted at the doorway and the
  one preloaded rider bailed out at the same instant.
  The budget is now derived per transfer from the measured path and that
  walker's own pace, clamped to `[3 s, one dwell]`. The door is also chosen by
  the whole journey rather than by which one the walker stands nearer: the two
  doors are `4.39 m` apart on the same kerb, so the old rule could send a
  passenger `6.60 m` down the aisle where `2.56 m` was available. Authored
  pace is kept rather than hurried, because each design has its own cadence
  and speeding the root would read as foot-sliding.
- Verification: `blender --background --python
  tools/build-city-pedestrian-3d-model.py` — the deterministic validator that
  owns the seated clearance bands, the 31-keyed-bone contract, in-place/no
  root motion and the repeat-signature determinism check. Then one focused
  EditMode selection over `CityBusStopWaitPlannerTests`, `CityBusRuntimeTests`
  and `CityPedestrianRuntimeTests`, re-run after the fix with the regression
  cases `PassengerDoorDock_NeedsTheRoadInclusiveArea`,
  `CabinPreload_NeverFillsThePlaceReservedForTheHero` and
  `FilledCabin_StillAdmitsTheHeroToSeat07` — which drives the order a
  preloaded cabin actually produces, ambient passengers first and the hero
  second — and `AmbientSeatOrder_SpansBothSidesOfTheCabin`, which reads the
  real bus model manifest and pins the fact that seven ambient seats sit on
  the driver's side while seat `07` does not. A further
  `TransferBudget_CoversTheRealWalkForEveryRidingDesign` reads the same
  manifest and asserts the budget exceeds the walk each riding design actually
  has to make, so an unreachable timeout cannot return. Final selection:
  47/47. Not run: PlayMode,
  the full EditMode suite and any player build. The board/ride/alight sequence
  itself still has no automated coverage — it needs a scene fixture — so the
  playtest remains its proof.

## 2026-08-12 — Walkers give way along the lane

- Measured the geometry before designing, and it decided the design: sidewalks
  are `1 m`, the lane corridor is `±(AgentRadius + NavigationMargin) = ±0.5 m`,
  and `RoadWalkableArea.Contains` requires the whole `0.35 m` disc inside, so a
  walker has `±0.15 m` of lateral room. Two walkers need `0.70 m` of separation
  to pass. **Walking around each other across the lane is impossible on this
  pavement**, so no amount of steering work would have produced it.
- Avoidance therefore works along the lane, in three parts:
  - A shoulder-shift of up to `0.15 m` away from whatever is ahead,
    implemented as steering toward an offset point rather than the node, so it
    re-centres on its own. Arrival became radius-based (`0.18 m`) because an
    offset walker never lands exactly on a node.
  - Queueing: a walker travelling the same way as the one ahead drops to that
    leader's pace instead of stopping dead and setting off again. The old
    behaviour stuttered.
  - A blocked-time escape: wanting to move and not moving accumulates, and
    after `1.5 s` the walker turns back. From the actor's side a prop and
    another walker are the same problem, so both get the same way out, and it
    is self-clearing because ordinary continuation already refuses to
    backtrack — the node behind hands it a different branch.
- `ShouldYield` became `ResolveAvoidance`, which still returns "must stop" but
  now also sets a speed scale and a lean bias per walker. Stopping is the last
  answer rather than the only one. Head-on ties are still broken by stable slot
  order, so that contract is unchanged.
- This mattered more after the population went from 2 to 8: two walkers meeting
  head-on used to stand nose to nose until the distance rule released one.
- Verification: `CityPedestrianRuntimeTests`, 21/21, including two new focused
  cases — a walker held indefinitely turns back exactly once and only after the
  threshold, and a queued walker keeps moving, leans within the lane and
  re-centres when clear.

## 2026-08-12 — The hopper stops hovering

- Reported symptom: the Helmet Lamp Hopper renders above the pavement.
- The source clips are not at fault. `CityPedestrianLocomotion.json` reports
  `ground_min_m = 0.0` for both `HelmetLampIdle` and `HelmetLampHop`, and Idle
  stays within `0.0097 m` of the ground for its whole cycle, so Blender exports
  him planted.
- Root cause is the gap the airborne exception opened. Every other design has
  its lowest sole pinned to the presentation root every frame, which also
  absorbs whatever the Avatar's motion-node extraction adds between the proven
  clip and the rendered pose. `PreservesAirborneMotion` made
  `CityPedestrianPresentation` skip that correction *entirely*, so for this one
  design the offset had nothing cancelling it.
- Built the missing instrument first:
  `CityPedestrianAirborneGroundingPlayModeTests`. Getting a *valid* reading
  took four attempts, and the first three were silently inert — worth recording
  because each looks like a passing measurement:
  1. Skinned `Renderer.bounds` never recompute without a render pass, so the
     samples reported the bind pose. Two different import settings produced
     bit-identical numbers, which is what exposed it.
  2. Bone transforms fixed that, but the presentation selects
     `CullUpdateTransforms` and a batch-mode run never renders, so the rig was
     never driven at all: the measured arc was exactly `0.0`.
  3. Adding a camera did not help — batch mode still does not render. Only
     forcing `AlwaysAnimate` in the test drove the rig.
  The test now asserts the rig actually moved before trusting any sample.
- With a working reading: the hop is a real `0.272 m` arc, so `lockRootHeightY`
  had to go back to `true` for airborne clips after all. The previous session's
  note that locking "stripped the hop" was never verified — it was written in
  the same session that found its own grounding test inert. Baking is what
  *preserves* the arc here, because the presentation runs `applyRootMotion =
  false` and unbaked height is extracted to root motion and thrown away. The
  FBX was reimported so the setting is live.
- The remaining lift is not a clip defect. Every other walker's per-frame sole
  pin also absorbs the height the shared Generic Avatar adds when retargeting a
  skeleton whose proportions differ from the hero's, and this squat design has
  no such pin. It is now declared as
  `CityPedestrianArchetype.GroundTrim` (`0.05 m` for the hopper) and applied to
  the model root.
- **The exact trim is a visual call and is not machine-settled.** The
  instrument's absolute zero is unreliable: it approximates a sole as a fixed
  drop below its foot bone and so ignores foot rotation, and the idle and hop
  clips answer the same world-space offset by different amounts, so no single
  constant grounds both. The test therefore reports absolute heights and gates
  only on the vertical travel, which it measures soundly. Nudge `GroundTrim` if
  the hopper still reads high or starts to sink.
- Verification: the new PlayMode test passes, and the focused
  `CityPedestrianRuntimeTests` EditMode selection was rerun for regression.

## 2026-08-12 — A populated daytime street

- Replaced the single `MaximumActiveModels = 2` constant with
  `CityPedestrianPopulationProfile`, so each runtime scales on its own anchor
  budget: City `8` day / `3` night over a `13`-model pool, Home balcony `5` / `2`
  over `8`. `CityGameRoot` and `HomeInteriorRoot` now log the resolved caps.
- The pool repeats designs. `CityPedestrianArchetype.MaximumPoolInstances`
  makes that safe: `CreatePoolComposition` deals every design once and then
  round-robins the remainder while respecting each limit, and the Helmet Lamp
  Hopper declares `1` because it wears the only working light. The factory
  validator changed from "one model per design" to "every design present, none
  over its declared limit".
- One spawn event now activates up to two walkers, and the cadence depends on
  whether the street is full: `0.4-2 s` while below target, the original
  `3.5-12.5 s` once only replacements remain. Night keeps one walker per event
  and its long delays throughout.
- Added dispersion: a candidate anchor must keep `12 m` from every active
  walker and no more than two walkers share one sidewalk lane, derived from the
  anchor ID without allocating. The fallback ladder now gives up connectivity
  before dispersion, since a distant walker still reads as city life and two
  stacked on one lane does not.
- Approach guidance is capped at two concurrent walkers; everyone else takes a
  seeded 50/50 initial direction with no player-proximity preference at all.
  Eight walkers all steered at the hero read as pursuit, not as a city.
- Added a forward-travel bias: above `3 m/s` smoothed player speed, selection
  prefers anchors in the forward half-plane. This is what makes the bus ride
  work — at `6 m/s` anything spawned behind is outrun before it can be seen. A
  per-frame jump beyond `12 m` is treated as a teleport and clears the heading.
- Performance work that the larger population made mandatory:
  - `RefreshInitialApproachRoutes` ran an `O(V^2)` Dijkstra over the whole
    graph (169 layout nodes expand to a much larger pedestrian graph) on every
    change of the nearest node. It now uses a binary heap with lazy deletion
    and only recomputes after the player has moved `4 m` *and* the per-component
    target actually changed. Scratch arrays are reused instead of reallocated.
  - Candidate search probed `Physics.CheckCapsule` on every one of the 210 city
    anchors that passed the distance filter. It now collects eligible anchors
    into reusable buffers and probes at most `4` sampled picks, and one
    `Physics.SyncTransforms` covers a whole spawn batch instead of one per
    spawn.
- **A nearer spawn ring was proposed, implemented, and then rejected on
  evidence.** The plan called for a `44-56 m` fog-hidden ring to fill the
  street faster. The existing fog proof in `CityPedestrianRuntimeTests`
  measures transmittance along the view axis *at the frustum corner*, which is
  only `0.574` of the radial distance — a factor omitted when the ring was
  proposed. At `44 m` that leaves `16%` transmittance against an accepted
  `0.2%` bound, and the bound is not met until roughly `72 m` radially, which
  is the existing `76 m` band. The ring was removed; the population increase,
  batch fill and forward bias deliver the goal without it. The test now proves
  the bound for the whole active population rather than the first pair.
- Verification: `CityPedestrianRuntimeTests` EditMode selection, 19/19 passed.
  Not run, and not required by the change: PlayMode, the remaining EditMode
  fixtures, a player build. The Home balcony population is a first estimate —
  `HomeInteriorRoot` now logs how many of its 16 anchors fall in the spawn band
  and in the connected fallback band, so it can be tuned against measurement
  rather than guessed again.

## 2026-08-12 — Helmet Lamp Hopper, a worn light and airborne clips

- Added the fifth city walker, `helmet_lamp_hopper_v1`: a squat miner in ochre
  work wear with a hi-vis band, a battered pale helmet, a lamp housing wired
  down to a belt battery box, and `0.46 m` hind feet. 37 meshes, 1084
  triangles. It never takes a step — `HelmetLampHop` is a two-footed rabbit
  bound through crouch, launch, a tucked airborne apex and landing, and it is
  the fastest walker at `1.32-1.48 m/s`.
- It carries a real always-on shadowless Spot (`7.5 m`, `3.6` intensity,
  `58°/26°`) parented to the animated head bone at the lens. The pool holds one
  hopper, which is what caps such lights in the world at one; the beam is left
  on regardless of the city clock because its owner switched it on.
- Three contracts had to be relaxed, each by explicit declaration rather than
  by a blanket exception, so an accidental violation still fails:
  - `ArchetypeSpec.airborne_lift_m` replaces the every-frame sole rule with
    "never penetrates, lands at least once, reaches this apex band". Airborne
    clips get one constant pelvis lift instead of a per-frame correction,
    because a per-frame correction pins the lowest sole to the road on every
    sample and silently turns a hop into a shuffle.
  - `PedestrianDescriptor.CarriesHeadLamp` turns the prefab validator's blanket
    "no Lights" ban into a declared-count check, and additionally requires the
    lamp to be a bounded shadowless Spot registered on the head bone.
  - `CityPedestrianAssetRegistry.PreservesAirborneMotion` makes
    `CityPedestrianPresentation` skip its per-frame sole pin for that design.
- Found and fixed a silent import defect: the clip importer set
  `lockRootHeightY = true` on every clip, and this Avatar treats the pelvis as
  the motion node, so the hop — authored on the pelvis — was being stripped at
  import. Root-height locking and loop-pose normalisation are now off for
  airborne clips only.
- **Found a pre-existing inert test.** `AssertWalkSolesStayGrounded` drove a
  PlayableGraph and compared sole heights across 12 phases, but a PlayableGraph
  writes no transforms in a batch-mode EditMode run: every phase returned the
  identical rest pose, so the assertion had been comparing a static pose to
  itself for all four earlier archetypes. Diagnostics confirmed the head bone
  sat at exactly `1.4300` in every phase. `AnimationClip.SampleAnimation` does
  drive the rig, but it produced up to `0.58 m` of sole travel for ordinary
  walkers — it bypasses the Avatar path the runtime uses — so rather than fit
  the test to unexplained numbers, the helper was narrowed to what it can
  honestly prove (presentation wiring and sole-renderer presence) and renamed
  `AssertSolePresentationWiring`. Grounding, hand clearance and hop apex are
  now asserted from the generator's shipped locomotion manifest, which is real
  data that Unity imports.

Verification:

- Primary check, the deterministic Blender 5.0.1 validator: `CITY PEDESTRIAN
  ART BUILD OK`. Five models, ten 31-bone loops, every archetype grounded
  against its own footwear, `airborne helmet_lamp_hopper_v1: 0.241 m apex
  lift`, repeated model signatures matched.
- Reviewed the model preview and the five-row contact sheet directly; two
  geometry iterations were needed for value separation, and two animation
  iterations to deepen the crouch and tuck the forepaws.
- Secondary check, one filtered EditMode selection: `CityPedestrianRuntimeTests`
  passed `18/18` in `0.89 s`, including the new fifth prefab case, the worn-lamp
  assertions and the manifest apex contract.
- Complete suites, player build and packaged smoke were intentionally not run.

## 2026-08-12 — Long-Arm Walker and animated hand-clearance validation

- Added the fourth city walker, `long_arm_walker_v1`: narrow and tall in cold
  steel blue, small skull sunk into raised shoulders, eyes almost at the
  hairline, no mouth, and bare pale forearms roughly `3.3x` their bone length
  hanging to the ankles under oversized hands. 35 meshes, 1044 triangles.
- Deliberately the first design whose strangeness is the body itself rather
  than a worn or carried object. Another object-bearer would have collapsed
  into the Chair Carrier's slot; the family trait is "a body treated as
  furniture, worn matter-of-factly", not "wears a thing", so this walker
  extends the motif instead of repeating it.
- `LongArmIdle` (`2.5 s`) holds a dead-still torso under an arm sway that never
  settles; `LongArmWalk` (`1.5 s`, deliberately twice the Kettle Hat's cycle)
  shuffles on barely lifted feet while the arms reach their extremes on the
  passing poses — a quarter cycle behind the legs, so the limbs read as
  pendulums the body drags rather than an ordinary counter-swing.
  Movement `0.72-0.84 m/s` on `0.86-0.94x` clips makes it the slowest walker.
- The visible forearm hangs almost straight down from the elbow rather than
  following the outward A-pose bone axis: extending it along the bone would
  breach the `1.65 m` rest-width guard, and hanging a long segment below its
  own pivot is what produces the pendulum once the shoulder rotates. The hair
  is a close cap that never widens past the skull, because an overhanging brim
  would echo the Lampshade Walker.
- Added `hand_clearance_m` to the archetype contract and an animated
  hand-to-pavement check to `validate_animated_grounding`. Footwear grounding
  could not express this: a design whose hands hang near the ankles pushes them
  through the road while every sole still reports perfect contact. The check
  earned itself immediately — the first authored pose failed at `0.174 m`, above
  the band ceiling, so the hands were lowered until the reach was real.
- Unity side: descriptor, paths, import tracking, a collider-free passive
  `Resources/Pedestrians/LongArmPedestrian3D.prefab` and the catalog entry.
  Runtime again needed no change; the pool is now four against a two-slot cap.

Verification:

- Primary check, the deterministic Blender 5.0.1 validator: `CITY PEDESTRIAN
  ART BUILD OK`. Four models (`1160` / `1032` / `1356` / `1044` triangles),
  eight 31-bone loops, zero loop error and zero root translation, every
  archetype grounded against its own footwear at `0.0` gap and `0.0`
  penetration, Long-Arm hand clearance `0.107 m` in both clips, repeated model
  signatures matched.
- Reviewed the model preview and the four-row contact sheet directly; three
  geometry iterations were needed, mainly to stop the hair reading as a
  Lampshade-like brim.
- Secondary check, one filtered EditMode selection: `CityPedestrianRuntimeTests`
  passed `17/17` in `0.74 s`. Only the fourth `[TestCase]` and two explicit
  arrays needed editing — the pool-size and clip-count assertions were already
  parameterised on the catalog and followed automatically.
- Complete suites, player build and packaged smoke were intentionally not run.

## 2026-08-12 — Kettle Hat Walker and per-archetype clip grounding

- Added the third city walker, `kettle_hat_walker_v1`: a stout short-legged
  figure whose overhanging belly hides the upper legs and whose oversized
  skewed enamel kettle — body, rim band, shoulder, lid, knob, sideways spout
  and handle arc — owns the top of the silhouette while the face stays visible
  under the rim. 42 meshes, 1356 triangles, muted plum coat against the
  Lampshade's green and the Chair Carrier's orange.
- Kept the shared `1.75 m` envelope, the exact 31-bone Generic rig and the
  fixed collider. The short read is authored as proportion: the human mass
  ends near `1.40 m`. Lowering the visible torso further was rejected because
  the arms would then swing around bone pivots they no longer sit near, and a
  genuinely shorter walker would need its own collider parameterisation.
- Replaced the two-way `lampshade / else chair` geometry branch with an
  explicit per-archetype builder map that raises on an unregistered key
  instead of silently falling back to another design.
- **Fixed a real defect:** `build_animation_library` built only the Lampshade
  model and baked and verified every clip against its footwear, so the Chair
  Carrier clips were already grounded against the wrong boots. Grounding is
  now proved per archetype — each design is rebuilt in its own scene and only
  its own clips are baked and validated against its own soles. The baked
  pelvis track is captured as plain per-frame data and re-keyed onto the
  shared library, so the exported clips carry exactly the correction proved.
- Extended the shared locomotion library from four clips to six with
  `KettleHatIdle` (`1.75 s`) and `KettleHatWalk` (`0.75 s`), and made the
  review contact sheet size itself from the catalog, one row per archetype.
- Wired the Unity side: descriptor, paths and expected clips in
  `CityPedestrianAssetSetup`, import tracking in `CityPedestrianModelImporter`,
  a collider-free passive `Resources/Pedestrians/KettleHatPedestrian3D.prefab`,
  and the catalog entry in `CityPedestrianResources` at `0.90-1.02 m/s` on
  `1.08-1.18x` clips.
- No City, Home, graph or bus change was needed: both scenes already load the
  whole catalog through the shared factory, the director already selects among
  free presentations by spawn seed, and the bus already yields to any actor.
  The pool is now larger than the two-slot active cap, so repeat encounters can
  vary the visible pair while two concurrent walkers stay distinct.

Verification:

- Primary check, the deterministic Blender 5.0.1 validator: `CITY PEDESTRIAN
  ART BUILD OK`. Lampshade `38` meshes / `1160` triangles, Chair Carrier `35` /
  `1032`, Kettle Hat `42` / `1356`; six 31-bone loops with zero loop error and
  zero root translation; each archetype reported grounded against its own
  footwear with `0.0` contact gap and `0.0` penetration on every frame;
  repeated model signatures matched.
- Reviewed the generated preview and the three-row contact sheet directly;
  three geometry iterations were needed before the silhouette read as short and
  plump rather than boxy (coat hem slab, hidden face, undersized kettle).
- Secondary check, one filtered EditMode selection: `CityPedestrianRuntimeTests`
  passed `16/16` in `0.71 s`, including the new third parameterized prefab case,
  the six-clip library expectation, the catalog-availability case and the
  pool-equals-catalog-size expectations.
- Complete EditMode/PlayMode suites, a player build and a packaged smoke were
  intentionally not run; this is fast mode.

## 2026-08-12 — Documentation drift repair and retention rule

- Corrected the stale `AGENTS.md` baseline: it still listed four build scenes
  while the project ships seven. It now names all seven in build order, marks
  the five gameplay roots, and adds the Editor/TestSupport assemblies and the
  `tools/` generators.
- Rewrote `README.md`, the only human-facing document, which had not been
  touched since `2026-08-04`. It still described an eight-direction sprite hero
  and a city permanently locked in noir night, and covered none of the Road v2
  streets, street pedestrians, Route 01 bus, passenger rides, inventory, needs,
  pause menu or supermarket. Verified every asserted key binding, dimension and
  constant against the runtime source rather than the planning documents.
- Replaced the `Implemented`-everywhere status column in `ai/systems-map.md`
  with the four-term vocabulary declared in `ai/README.md`, and compressed all
  72 rows from run-on specifications to one or two rendered lines. Five systems
  are honestly `Partial` with their gap named: needs progression (no debuffs),
  the Route 01 bus (City-only), the passenger MVP (no fare or destination),
  scene music (four optional themes absent) and the refrigerator (`Use`
  unavailable). The flow block was reduced to its structural backbone.
- Added a retention rule to `ai/README.md` and `AGENTS.md`, then applied it:
  July 2026 entries moved verbatim into `ai/archive/work-log-2026-07.md` and
  `ai/archive/release-notes-2026-07.md`, with pointers at the head and foot of
  each active file.
- Committed the pending Unity `6000.5.5f1` asset re-serialization separately
  (`chore: normalize Unity asset serialization`). All 23 files were confirmed
  live production assets before committing; the diff was trailing whitespace on
  empty YAML scalars only.

Verification:

- Documentation-only change, so the policy check is diff review plus
  `git diff --check`, which reports clean. No Unity test, build or smoke was
  run, and none is warranted.
- Confirmed no information loss before compressing `systems-map.md`: every
  sampled tuning value (`76-86`, `2.75`, `0.30 m`, `88 m`, `92 m`, `10 s`,
  `1440`, `1080`, `4.5 m`, `24 m`, `0.70 s`, `48 m`, `12 x 12`) already appears
  in both `ai/architecture-notes.md` and `ai/project-overview.md`.
- Table integrity checked mechanically: 72 system rows retained, every row has
  exactly four columns, and no status outside the declared four appears.
- Active documentation context dropped from `731 KB` to `552 KB`, with
  `134 KB` retained verbatim under `ai/archive/`.

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

---

Earlier entries: [`ai/archive/work-log-2026-07.md`](archive/work-log-2026-07.md).
