# Work log

Newest outcomes/checks first. Archive whole dates at budget: [policy](README.md).
Earlier: [September](archive/work-log-2026-09.md), [August](archive/work-log-2026-08.md).

## 2026-09-21 — Brawl v2

- Anatomy: first/six posed bone-local zones replace capsule damage; one HP.
  Head x2 cannot defeat full HP; rear head ends after protection;
  arm/leg/back scale impact data.
  Checks: `CombatRulesTests.Anatomical*`,
  `Range_AnatomicalContactsResolveHeadAndRearHeadForBothRigs`.
- Rules: free swings; breath pays guard/step/charge, regens in stuns after
  own spends. Fresh press parries lights; counter/whiff floor, break keeps breath,
  buffered return/step-attack grace. Regen from ActiveEnd is partition invariant;
  frontal contact consumes press, step/charge drops guard. Check: `CombatRulesTests`.
- Runtime: skipped 1/120 substeps own hit-stop, including lethal freeze;
  knockback/two-body block nudge, shoulder kick before clearance, HP .6/.5
  recovery/guard, Windup→Active whoosh, Active-only wall cancel. Camera/target
  free after fall; `PlaceRound` re-locks. Check: `CombatTestPlayModeTests`.
- Seeded opponent: guard/step/charge intercept/feint/whiff punish/cover,
  winded retreat/corner fighting, probe/press moods; no accidental parry.
- Space: .8 m/.36 s travel/.21 s settle; symmetric smoothstep replaces front-load.
  Shared travel/clip clock; 15 breath/footfalls unchanged. Full Hold freezes breath.
  .02m/.75° camera lag caps keep the raised arm clear.
  Hold check: `Range_AutomaticUpdateConsumesMouseAndKeyboardWithoutGuiEvent`.
  Step checks: `Range_TacticalRecoveryStepsAndFairOpponent`,
  `AreaCaptureFixture.CombatTactics`, `build-combat-test-3d-model.py`.
- `CombatTuning`: aged guard/heavy hits, physics evasion/duel strafe; foreign
  owner releases stance; `CombatDuelSimulator` habits. Check: `CombatBalanceTests`.
- Pre-09-09 docs archived (`check-docs.py`).
- Charge arm: reachable docks/forearm roll stop the left hand folding the
  overhead right arm. Checks: `build-combat-test-3d-model.py`,
  `AreaCaptureFixture.CombatChargeArmAlignment`.
- Aftermath: floor thud/wound-anchored pools. Bleed used to stop before landing;
  check: `Range_LethalContactsHandOffToRagdollPauseResetAndCleanUp` and frames.
- Winner releases combat at Ready→.35 s normal walk/right crowbar; no
  reacquire until R. Check: `Range_VictoryRestoresOrdinaryWalkingAndResetRestoresCombat`.
  Ready-gait probe fails in `Range_CombatWalkingUsesLiveInputAndMovingLegs`.
- `MeleeSwing` commits charge-visible step cue/target bearing/last-fate rhythm;
  both banks have a backhand family. Mirrored bar's support hand is far:
  down-forward elbow pole keeps the wrist in the charge envelope. Checks:
  `CombatRulesTests`, `AreaCaptureFixture.CombatCharge`.
- Body: C1 velocity/atan2, travel/yaw inertia; both .42/.28 m
  travel stances, feet settle in Windup. Hero afraid/unskilled:
  shoulders/chin/awkward effort, stamina breath/tremor/tell flinch/tense face.
  NPC calmer; HP separate. Duel-clock render/contacts freeze on resample/pause.
  Checks: `build-combat-test-3d-model.py`,
  `Range_CombatBodyInertiaKeepsBothRigsContinuousAndClockBound`.

## 2026-09-20 — Combat

- Tactics/HP/blood/charge/HUD; fixed resampling/axes/Z stripe and quick-tap entry.
  Checks: `CombatRulesTests`, `build-combat-{test,blood}-3d-model.py`, rendered
  `CombatTactics`/`CombatTestLockedCamera`; `check-docs.py`.
- Two-hand ready/high guard/opposed regrip, grounded legs/breathing; contacts follow
  blends/injury. Hero keeps stance at rest; round end clears stale guard.
  Checks: `build-combat-test-3d-model.py`, rendered
  `AreaCaptureFixture.CombatTwoHandPose`; guard height/wrists reviewed.

## 2026-09-19 — Wheel, seats, scarf, fair and combat test

- Truck rim/column face the driver; bare/gloved cylindrical grips now bind
  the missing runtime component. Checks: `build-city-cannery-3d-model.py`,
  `build-default-npc-3d-model.py`, rendered `AreaCaptureFixture.DefaultNpcDriverGrip`.
- Bus knees follow feet; lateral hints buried thighs. Trouser regression
  excludes shins; default bodies need `0.076`, donor `0.056` seat lift.
  Check: `SeatedPassengers_StayOnActualCushionsAcrossAnimationAndBusMotion`.
- Scarf follows location after five free third-person seconds, retained over
  travel and deferred for disembark/mouth actions. Check:
  `ScarfLocationPostureWaitsForThirdPersonAndSurvivesTravel` and reviewed frames.
- Three permanent silent fair adults share idle, grounded collision and clear
  children's routes/`2.2 m` paths. Check: rendered `AreaCaptureFixture.CityFair`.
- Crowbar AI/target: mutual hits, walls, falls/drop; safe unload.
  Checks: `CombatTestPlayModeTests`, rendered `AreaCaptureFixture.CombatTest`.

## 2026-09-16 — Nightlife lane crossroads

- The x = 10 lane dead-ended at node `(10,4)`, one block short of the
  `(9..12,5)` street. A required edge would have re-seeded the spanning tree
  and every loop draw after it, so `CityBlueprint.AuthoredStreets` is
  appended after the outer ring. `default-coastal` authors `(10,4)-(10,5)`;
  `(10,5)` is a four-way crossroads and lot `(9,4)` fronts north. Check:
  `CityLayoutGeneratorTests.DefaultCoastalBlueprint_AuthoredStreetTurnsTheNightlifeStubIntoACrossroads`
  with the canonical-home canary and `CityArchShelterTests` in the same run.
- Empties on the shelter terrace: misc kind `NightlifeShelterPlatformLitter`
  (v4.11, bottles and cans in four tint parts) placed by a fifth,
  non-blocking `PlatformLitter` prop. Its envelope wraps the bedding, so the
  plan cannot prove clearance; the sleeper exemption widened and
  `WorldBuilder_PlatformLitterLiesAroundTheBeddingClearOfTheWarmers` checks
  the imported vertices against warmers, barrel and mattress.
  Surface contract: nineteen parts. Checks: generator
  `--validate-only`, `CityMiscAssetSetup.RunBatch`, `CityMiscAssetTests`,
  `CityArchShelterTests`.
- Shelter residents talk on the port principle: pure
  `CityArchShelterConversationSchedule` (Rules) picks authored pairs with the
  port's no-repeat/deferral/discontinuity rules but takes each line's length
  from the shared delivery via the caller instead of a fixed clock;
  `CityArchShelterConversationController` on the shelter root owns one
  manual-clock bubble view over the residents' real heads, raised from
  `CityGameRoot` beside the guards because it needs the camera. 48 RU/EN
  lines (`city.shelter.*`); the story bible's «молчат» was lifted by a §6
  row. Checks: `CityArchShelterConversationTests` (pools, order, deferral,
  earshot, seek, built shelter), `LocalizationCatalogTests`.
- The map's planned bar route (ordered `BarId` list, Dijkstra path,
  distance readout, «Очистить маршрут») is removed as obsolete: session API,
  `Runtime/Map`, map commands/keys and four localization keys retired; bars
  stay as named markers. Three world-gen tests that used the pathfinder as
  a ruler measure the straight line. Checks:
  `CityMapDistrictPresentationTests`, `CityMapAreaPresentationTests`,
  `GameSessionStateTests`, `LocalizationCatalogTests`,
  `CityLayoutGeneratorTests`, `PlayerHomeLayoutTests`,
  `CityTravelDistanceTests`.
- Street pool = default NPC population: the body is adapted, not the
  director. `CityPedestrianDefaultNpcBody` puts a registry ABOVE the
  character root (a bound presentation is reset to unit scale; the body
  keeps its `1.017` height scale), drops the village graph, keeps one
  footstep root, binds no palette; sit/guard/shove read off the weigher
  prefab asset. Six manifests flip `pool_eligible` by hand (outside the
  signature). Bicycle-pocket figure withdrawn, dock kept. Checks:
  `CityPedestrianRuntimeTests`, `CityCourtyardResidentTests`.

## 2026-09-15 — Eastern valley, litter, fair and default NPCs

- Eastern post: broad valley, level apron, descending curves, soft day/night
  glow. `CityEastRoadProfile` owns road/land/car/lamp datum; yard
  cuts retain the garden seam. First mast shares the light pool. Two cars
  retain lane/grade/session/pause. Source depth resolves terrain/road/car
  occlusion; asphalt vertices keep the crown/panorama seam.
  `--near-only` retains panorama Light/audio/collision/travel exclusion.
  Checks: `build-city-east-exit-3d-model.py --validate-only` (`--near-only`/
  `--distance-only`), `AreaCaptureFixture.CityEastExit`, reviewed frames.
- Litter sparsely covers permitted pavement/wall/tide/bench bands, outside
  roads/crossings/authored sites. Memoised `CityLitterPlan` shares geometry/
  instancing with the eastern strip. Spatial hashing prevents nearby repeats;
  real strip/cap/corner boxes fix graded-curve placement. Beach avoids
  loose sand; shelters use the bus plan; priming warms the exit profile.
  Checks: `CityLitterPlanTests`,
  `CityLayoutCacheTests.PrimeCityPlans_JoinsPlansEqualToPlanningOnTheMainThread`,
  `AreaCaptureFixture.CityLitter`, `check-docs.py`.
- Fair: passive displays/silent vendors, organ/bell/shared hero actions,
  seats/garlands. Sampled terrain/tilted supports fix the buried north row.
  Three children share body/outfits/clips; watching, toy car/table and seat
  claims preserve circulation. Trousers now join the waist; triangle totals
  missed the bare pelvis. Residual gaze fixes doubled head tilt. Checks:
  `CityFair`, `CityFairChildHeads`, reviewed activity frames.
- Global `DefaultNpcPopulation`/`CreateForCharacter` assigns stable looks by ID,
  no repeated model/face/hair/clothes tuples until exhaustion.
  Detailed body, outfits, faces and hair/beards retain bind frames/actions;
  explicit coverage replaces local tint/apron loops.
  Checks: `build-default-npc-3d-model.py --validate-only`,
  `DefaultNpcFactoryPlayModeTests`, `AreaCaptureFixture.DefaultNpcWardrobe`,
  `check-docs.py`.
- Hero/default hands: 20% larger reach, slimmer depth/girth, fixed
  wrists/grips. Seated meshes exposed stale passenger lifts after bus
  movement; seat planes now follow it without reticking. Truck caches
  trouser fit. Shared elbow hints end the sleeve growth lateral hints caused;
  authored finger/thumb grip shapes were prepared in the source model.
  Check: `NauseaHand_ReachesTheMouthAndRendersTheSheet`.
- Map: fair, eastern post, docks, arch and church door join the POI list/
  legend/hover/`XYZ` via a map-level kind enum; the layout POI enum stays a
  lot contract; legend rows shrink, overflow line; «Дежурный» on both east
  guards; `fair` audit group; stale cannery/balance key expectations fixed.
  Checks: `CityMapDistrictPresentationTests`, `CityMapAreaPresentationTests`,
  `LocalizationCatalogTests`, `check-docs.py`.

## 2026-09-14 — City geometry, eastern checkpoint, cold City loading

- City audit compares walk mask/physics, registry/triangles, planted controls
  and shifted cameras; coplanarity alone did not prove flicker, frames exposed
  cave/backdrop flicker, fence/shelter collisions, wall seams, bare east yards.
  Checks: `CityAuditProbeTests` (control + A–E + `F_Diagnose`),
  `AreaCaptureFixture.CityAudit`.
- Bus stair kerb bands; cemetery fences use part widths/north edge; boundary
  fragments overlap, port skirt closes its opening, cave lining clears beams,
  gabions bed into stone; joined esplanade slabs follow sand; east ground uses
  forefield texture; grandmothers have distinct outfits. Checks: EditMode
  `CityStreetSurfacePlannerTests`, `CityChurchPlanningTests`,
  `CityCemeteryPlannerTests`, `CityFringeYard*`, `CitySeacoastPlannerTests`,
  `CityMountainBoundaryTests`, `DryingYardBabushkaTests`.
- Checkpoint joins street edge/height; road replaces terrain, dressing fits
  refined supports; panorama stays behind land, welded road collision and a
  radius connector close ray/mask gaps. Patrols skirt shed/sidewalk. Garden/
  yard share grass-soil UVs/footsteps; terrain swale/crossings, shared gentle
  relief and sparse grounded trees replace the parallel service strip.
  Checks: `build-city-east-ground-texture.py --verify`,
  `build-city-east-exit-3d-model.py --dressing-only --validate-only`,
  `AreaCaptureFixture.CityEastExit`, `check-docs.py`.
- Litter: 36 Blender variants reuse City misc across five spaced width bands. Shared
  rigid meshes settle on actual ground; solids clear routes/map arrivals.
  Swale checks distinguish validated props from competing terrain. Checks:
  `build-city-litter-3d-model.py --validate-only`, `AreaCaptureFixture.CityEastExit`.
- A start inside a City interior ended at a door into an unbuilt City, all
  its planning and sampling under the black. `NewGameStartService` primes
  the City's pure chain (layout, night, world plans, grounded bus, street
  surface, pedestrians, cannery route, beach/seabed lists) on one pool `Task`
  that `CityLayoutCache.GetOrGenerate` joins; one owner per layout until then
  (`CityTravelDistance` cache locked, port contract loaded first), a fault
  plans in place. Street/pedestrian/route plans memoised per layout, door pump
  at a 250 ms floor, seabed shore taps cached per column, crackle moments
  hoisted, east-exit support cells 1 m; rows `east_exit`/`east_distance`/
  `dressing` and phase rows for the old gaps. Checks: EditMode
  `CityLayoutCacheTests` (`PrimeCityPlans_*`, `PostYieldPlans_*`),
  `CityTerrainSurfaceWorldBuilderTests` (pool-thread/seabed bit-identity),
  PlayMode `ChurchInteriorPlayModeTests.NewGame_InTheChurch_*`; a temporary
  mesh-hash probe matched every east-exit/seabed/beach/crackle hash.

## 2026-09-13 — Hero, cannery and eastern edge

- Hero: narrow torso/shoulders/upper sleeves in oversized M-65; shaped
  pockets/cuffs, medium curtains, fixed hands/face UV. Body/outfit separated:
  clothed anatomy had blocked undressing/contacts. Wardrobe survives bathroom/
  head leases; bounded hair/hem/cuff inertia/wind/body contacts freeze/reset,
  mirror/arms copy one rig.
  Checks: `build-player-3d-model-v2.py`, `build-home-toilet-seated-3d-model.py`,
  `AreaCaptureFixture.HeroAppearance`.
- Receiver: athletic 1.96 m, glasses/orange hat, painted face/tattoos, own
  clips, hands +18%, longer trouser rise. Both hands fixed from anatomy;
  larger grip delayed scale release. Routes/soles/carton/glasses contacts,
  reserved gestures. `NpcWardrobe`: body/clothes, one outfit, old woman
  bindings retained. Checks:
  `build-cannery-receiver-3d-model.py --validate-only`,
  `AreaCaptureFixture.CityCanneryInspection`.
- Seamer: 1.63 m/8000 triangles, painted blink/speech/colleague smile,
  own clips/contacts, RU/EN shared bubbles; range re-enable restores outfit/
  palette. Hair/body/strand contacts, covered nape, bounded bends, pause/seek
  reset; cached contacts/lazy diagnostics/idle-once reduce CPU/GC. Checks:
  `build-cannery-woman-3d-model.py --validate-only`,
  `AreaCaptureFixture.CityCanneryWomanPerformance`,
  `AreaCaptureFixture.CityCanneryWomanContacts`.
  Same woman: story §6/§11/§16.10, art §8; crime retained, chronology/romance open.
- Fixed brook approach/bank slope. Stale fixtures follow real house routes,
  port perches/audio/beach, array `Has.Count` and menu-rest/shop cancellation. Checks:
  EditMode `AlpineVillage*|VillageAsset|CityChurchPlanning|CityTerrainSurfaceWorldBuilder|RavenRoostPlan|CityFishSupplyCycle|CityMiscAsset|CityStreetSurfacePlanner|CityWetSurface|BarSurfaceAppearance`;
  PlayMode `SceneFlowSmoke|HomeOpening|MothersHouseInterior|BarDrinkPhysicalShop|StairwellInteriorPresentation`.
- Surface footsteps: appended cues/tags/overlays, `HeroFootstepGround` between
  claimant/plain step; hashed variants avoid alternation. NPC roots exclude
  riders; cafe linoleum. Checks:
  `RetroSfxLibraryTests|HeroFootstepGroundTests|NpcFootstepsTests|MountainRoadCafeCollision*`.
- East: closed civilian post, long road/distant skyline lights; canopy/bench/
  cabinet, repaired fence/road, dry drain/shoulders/service traces, low shrub/
  grass groups. Imported FBX axes needed hierarchy export + placed bounds:
  ground samples missed them. Terrain-fitted kit keeps routes/map/roof clear.
  Two distinct guards: shoulder rifles, alternating duty, shared pair/E
  speech/pause/cleanup; hidden mutual attraction canon. Import/patrol/motor/
  closed map/barrier/day-night verified. Checks:
  `build-city-east-exit-3d-model.py --validate-only` (also `--dressing-only`),
  `build-city-east-guards-3d-model.py --validate-only`, `AreaCaptureFixture.CityEastExit`.
- Documentation: `python tools/check-docs.py`, `git diff --check`.

## 2026-09-12 — Glovebox prompt and cannery driver's lunch

- Reused radio contours/connected labels on the authored glovebox catch;
  it follows the lid, shares gaze/input/pause guards and yields to the driver.
  Removed the duplicate bottom prompt. Geometry/open-close/UI lifecycle:
  `LastRouteCarRidePlayModeTests.Ride_AnswersTheRadioFromTheSeatWhileTheCarIsMoving`.
  Documentation: `python tools/check-docs.py`, `git diff --check`.
- Driver waits for the inspected cartons on the yard bench and eats personal
  bread; shared seat clips/food and measured contacts keep the same actor.
  Existing delivery time owns approach, lunch and return without moving the
  loading gate. Verification pending: `AreaCaptureFixture.CityCanneryDriverLunch`.
- Loading: per-phase timers in every root (shared `GameLogPhases`), per-block
  and per-mesh rows in the three world builders, composition-frame accounting
  in the travel pump. On those numbers: themes stream and load in the
  background (a suppressed player defers its theme), the loading-screen floor
  is gone, the city yields once per sixteen lots, the map's foreign tabs chart
  on first open or an idle warm with their planners on a task from the moment a
  root is ready, pure plans are memoised per session in `CityLayoutCache`, the
  three terrain samplers and the port and truck-route lookups narrow candidates
  through spatial indexes, the bus plan is derived from the
  routing the world build already paid for, the beach collider is a coarser
  lattice of the same plan, the door sequence is `1.6 s`, the menu warms the hero and pedestrian
  prefabs, prototype validation runs once per source and primitives no longer
  cook a throwaway collider. Not obvious because the two largest phases were
  opaque blocks and the door path builds the city in one frame. Every drawn
  mesh and every truck pose hashed identical before and after (temporary
  EditMode probes). Proof: `AreaTravelContractTests`, `SceneMusicImportTests`,
  `CityLayoutCacheTests`, `CityTerrainSurfaceWorldBuilderTests`,
  `MountainRoadTests`, `DoorTransitionTimelineTests`, `AreaAssetWarmupTests`,
  `CityMapLazyAreaTabsPlayModeTests`, `LastRouteRadioMusicPlayerPlayModeTests`,
  `TechnicalLifecyclePlayModeTests`.
- Doors: one `CompositionDriver` pumps the area and door paths, so a door
  into the City or Home composes behind the black instead of freezing one
  frame (an abort drains the remainder); the City stays resident across the
  every interior door (supermarket, church, stairwell and home, the village's
  mother's house) - dormant behind it, woken at its return dock - and every
  other load discards it first; a composition renders every fourth frame under its
  overlay. Six enable/disable asymmetries fixed on the way. Proof:
  `DoorPathCompositionPlayModeTests`, `ResidentCityRoundTripPlayModeTests`
  (two cycles), `SceneFlowSmokeTests.EnterAndExitBar_ReturnsToSameBarInSameCity`,
  `HomeBalconyLayoutTests`, `AreaTravelContractTests`.

## 2026-09-11 — Docks, speech, cannery and start menu

Checks below passed except where stated; named capture frames reviewed.

- Warehouse fittings clear stock/carts; light/fan/lifecycle:
  `build-city-port-3d-model.py`, `AreaCaptureFixture.CityPortWarehouseInterior`.
- Foreman at store/cart/quay light; left carrot→pail→pocket, palmar grip/right
  gesture. Food/grip/paths/alignment/speech/cancel:
  `build-city-port-foreman-3d-model.py`, `CityPortForeman`.
- Dock spur/footpath/crossing removed from terrain/walk/map/openings; truck road/
  shoulders/internal walks stay. Blender validation, `CityPortTraversalAudit`.
- All speech/E: slower shared bubbles, silent bottom, standard linked.
  Timing/RU/EN/admission/cleanup: `ParkQuarrelTests`, `MountainRoadCafeConversationTests`,
  `NpcSpeechVoiceTests`, `InteractionPromptViewTests`, `CitySpokenResponses`.
- Arrival-owned warehouse access: later docker waits outside or driver waits
  for exit; no crane reservation/empty-stock remark. Custody/clearance:
  `CityFishSupplyCycleTests`, `CityPortWarehouseAccess`.
- Labels exclude village/Mother; cleared masks/hat-hand clearance:
  `NpcNameplates` depth/distance/speech/lifecycle. Capture restores factory
  presentation around existing teardown error.
- Foreman dialogue: flat-quay approach/side shots, shared spoken replies;
  §6 face/reply exceptions. Cancel preserves §16.16/custody/snack;
  nested Talk recovers. Reveal drives mouths/brows/blink, chin/jowls damp.
  Branches/prompts/RU/EN/tail/mesh/pause/cleanup:
  `CityPortForemanDialogue`, `dialogue_face_atlas.py --validate-only`, foreman generator.
- Port motor/horn timing: `dotnet build BarPromenade.Runtime.csproj`;
  arrival ×2/longer DSP tail: `CityPortArrivalHorn`.
- F9 on; F1/F2/F3 ×3/×5/×10, repeat resets; world/calendar, fixed physics:
  `PauseMenuPlayModeTests.DebugSpeedHotkeys_ToggleGateAndSurviveSceneChanges`,
  `NestedPause_RestoresLatestTempoAndIgnoresOldSessionLeases`.
- 48-minute day/needs, minute-aware test setup: `GameTimeStateTests`/needs checks.
- Cannery: doors/waits/grips/pairs/light/audio: generator,
  `CityCanneryLivingShift`. Same cartons carried/weighed/approved outside;
  loading awaits storage/clearance. Ramp/FBX support: `CityCanneryInspection`.
  Yard speech gate/idle restored; reused bench clears crew/cart lanes:
  `CityCanneryOutsideWait` (placement/physics, before/after shift, gestures/pause).
  `CityCannery` retains stale repeat-duration/dock-latch failures.
- City F9 stages the same loaded truck near factory, pre-dock/paused; only
  supply time rebases, repeats start a fresh batch. `CityCanneryDebugSpawn`:
  menu/pause/custody/resume/repeat.
- New Game opens eleven RU/EN places; Back keeps time stopped, confirmation
  resets day one/07:40. Village/Cannery/Home arrivals:
  `TechnicalLifecyclePlayModeTests.NewGame_*`; navigation/catalog:
  `StartMenuModelTests`. Main/picker GameView frames reviewed.
- Budgets/frozen references: `python tools/check-docs.py`.

## 2026-09-10 — Found-item screen, port and cannery

- Finds: refrigerator inspection/inventory poses. `WorldItemPickup`
  replaces scarf pickup; opening guard/full-pocket cancel/no hints (art §15a).
  `WorldItemInspectionTimelineTests`,
  `WorldItemPickupModelTests`, `HomeRefrigeratorItemCatalogTests` and
  `InventoryPresentationTests` passed; `HomeRefrigeratorInteractionPlayModeTests`
  blocked by Home seacoast graph in port integration.
- Port maps/metre UVs/access; fixed rails/beach links.
  `build-city-port-3d-model.py --validate-only` / `AreaCaptureFixture.CityPort` passed.
- PlayMode warnings fixed: unordered root/runtime-only wind/direct GameView
  assembly lookup. `dotnet build BarPromenade.PlayModeTests.csproj` passed.
- Weighbridge→cannery: finite custody/fixed stock/deferred tare, three-crate
  FIFO/2x during unload. Maps/workwear/poses/steam; import/reach/returns/receiver/
  packer fixed. `AreaCaptureFixture.CityCannery`/`CityPort`
  passed custody/contacts/traffic/pause/restore.
  Truck front/lenses: `AreaCaptureFixture.CityCanneryTruckAppearance` passed.
- Eight mono `SfxWorld` voices/local reverb, Music −6 dB; causal anchors.
  `GameAudioMixerAssetTests.MixerAsset_HasCanonicalDspRoutingAndSceneValues`
  / `AreaCaptureFixture.CityProductionAudio` passed mixer/Unity DSP.
- Village conifers by §6 row: forest around a cleared village, sizes 1-2x, so
  spacing is crown-to-crown, and trunks block via the mask, not physics.
  `Conifers_AreTheRoadsOwnTreesOnTheirOwnBand` passed.
- Port life/contacts: `AreaCaptureFixture.CityPortSocialLife` passed.
  Reach limits stop jitter; earlier greetings/Run/pushing/signed pullbacks retain
  grips. Shore rest under a warm canopy lamp; clear routes return to duty.
  City/weather pairs preserve rounds across absent roles/seek.
  Cart by tare/canopy: clear handle approach; rest replies finish before greetings.
  `CityPortHandlingArrival` passed; frames reviewed.
- Port masks join west ramp/yards; bypass radius once. Stops had no collider.
  `AreaCaptureFixture.CityPortTraversalAudit` passed seams, edge lanes and turns.
- `python tools/check-docs.py` passed.
- Dock latch persists until new game: `CityFishSupplySessionTests` passed.
  Cab/bus yields/reverse beep/docker-only talk; lift:
  `build-city-cannery-3d-model.py --only-part Truck` passed.
  Truck starts afar, parks with first crate; repeats from factory, horn pair.
  Doors before lift/reverse closure; crates continue, local carts return.
  Driver wait ends on actual doorway exit, not return to crane. Vessel horn has
  local echo/reverb; actual shadow/DSP/canopy proof:
  `CityCanneryDriverDelivery` passed; `CityPortShelter` /
  `CityPortSearchlightAppearance` frames reviewed.
- Mountain return tunnel: flat cap→curved open tail; physical limits stay.
  `MountainRoadCityTunnel` passed; frames reviewed.
- Map lot/XYZ: fresh land Y, solids avoided via streets.
  `DebugTeleport_NorthRowArrivesOutsideBuildingsOnActualSurface` passed.

## 2026-09-09 — Village opening, journal and working port

- `StartMenuRoot` resets the session on entry and offers New Game/Quit.
  New Game uses `GameTimeState.TryStartAt` for day one's `07:40`, then
  `AreaLoading` and the village's `lane_foot` spawn. Setting the clock avoids
  aging needs through simulated waking. No journey means no loading
  illustration. The complete Home wake remains behind
  `MainMenuRoot.RequestLegacyOpening()`; its startup status and canon were corrected.
- `ReachMothersHouse` comes from `GameDaySchedule` during `BeginNewGame`,
  including the retained opening. Completion in `EnterMothersHouse()`
  covers both door and map entry. The temporary house mark belongs only to
  the map schematic; story §6 records the startup and guidance exceptions.
- `JournalMenuModel` owns selection and `JournalView` draws a scrolling
  full-page list with the selected description and geometric ticks; dynamic
  font rebuilding no longer loses a tick or hides overflow. The corner
  notice lives under the shared journal controller. Reset clears notice
  flags before `SyncDayEvents()`, preserving day one's unseen quest.
- Startup/journal checks: `QuestLogTests`, `JournalMenuModelTests`,
  `JournalViewLayoutTests`, `GameDayScheduleTests`, `LocalizationCatalogTests`,
  `NewGame_FromStartMenuLandsAtTheVillageLaneFootWithARunningClock`.
  The earlier `MainMenu_WakeStartsAlarmThenRestoresGameplay` failure remains
  unrelated: its renderer assertion includes intentionally hidden scarf accessories.
- The mother speaks, looks back and asks for the scarf. The talk stub is the
  fisherman's shape on its own trigger beside her, because the factory still
  refuses her a collider; the head-look is `NpcHeroAttentionLook` at order
  `350`, behind the chair (`300`) and her graph (`310`). Asking outright
  strains §13's «разговор не удержался», so she asks outright AND does not
  hold it: the same request sits in her ordinary pool, drawn again as if new,
  and leaves the pool once the scarf is worn. `FindTheScarf` closes on
  WEARING it in `TrySetInventoryItemEquipped`, not on the pickup — she asked
  for a scarf, not for an errand. A scarf worn before she asks opens nothing.
- She then got the Ferryman's road speech in a room: an overhead bubble on a
  manual clock behind a shuffled bag, held across visits by
  `MothersHouseMotherSpeechSession` because the house reloads through the door
  and a per-visit bag spends the same two lines forever. Both channels draw
  that one bag, so `E` cannot echo what she just said alone. Two departures
  from the ride: no per-visit quota, because a room does not end and a mother
  who fell silent would read as having nothing left; and silence is spent only
  within `6 m` on her floor, so the bag is not emptied at an empty room while
  he is upstairs. Ten ordinary lines plus the re-ask, which is the only entry
  that retires. The greeting is outside the bag, once per entry, and is the
  same line every time on purpose.
  Checks: `MothersHouseMotherQuipsTests` beside the startup/journal set above.
- The western port now receives a trawler at one berth. Two cranes, five
  ordinary workers and a trolley transfer six cages to cold storage before
  departure; the hero observes. The Blender kit has real holds and measured
  contacts. Absolute clock sampling preserves cargo custody across scene
  recreation and seeks; physical sound crossings do not replay. The quay
  extends seaward on solid caissons because its original shore placement
  buried the store in rising sand. Side ramps connect the public rear path
  and breakwater; the existing beach walker graph stays clear and unchanged.
  Story §6 and both bibles record the bounded working-port exception.
  `AreaCaptureFixture.CityPort` passed; its settled day/night frames were
  reviewed. Captures now wait for frame-based GPU transform uploads after
  timeline seeks. Documentation passed `python tools/check-docs.py`.

## 2026-09-09 — Cableway grounding and the Ferryman's cabin

- Both cableway destination roots synchronize constructed colliders before
  player creation: paused construction had calibrated boots against lower
  terrain. `Ride_OnlyLeavesTheAreaOnceTheScreenIsBlack` and its standing frames
  verify support after alighting and walking off both platforms.
- The Ferryman's ten ordinary road lines use a session shuffle bag, travel
  intervals and reading holds. Overhead bubbles turn his head without taking
  input. Pause/skip cleanup and scene continuity
  are covered by `RoadSpeech_PreservesSilenceAndBagAcrossLegsWithoutTakingThePrompt`.
- The spatial radio has three station folders, starts at station 1 and loops
  each station's own track through the old-speaker chain. All slots now hold
  user MP3s; any filename works, with `radio_theme` preferred. Separate playheads
  use the carrier's station index, so equal clip names remain independent.
  Power still gates the city theme even on an empty station. Power-off captures
  the playhead and immediately cuts music/hiss, including any departing tail.
  Outlined `E`/`Q` callouts control power and cyclic station selection; a short
  tuning hiss/crackle answers `Q`. Station/playhead and power checks:
  `Radio_ThreeStationsKeepTheirOwnTracksAndPlayheads` and
  `Radio_HoldsCityAcrossReplacementAndLocationChanges`.
- The existing coin is now one authored octagonal mesh shared by the toss and
  the passive glovebox pile. `build-last-route-coin-3d-model.py --validate-only`
  checks deterministic geometry and compartment/bulb clearance. Runtime places
  it in prefab axes; the compartment checks placed vertices independently of the lid.
- Each trip randomly chooses one station for a single radio reaction after
  actual playback. Only hand contact at the turn endpoint commits the switch.
  Player E/Q cancels the gesture without replacing their choice. Choice/used
  state span both scenes; Story §6 bounds the rhetorical exception.
- Opening the glovebox triggers an ownership remark and delayed physical
  closure from below. Both gestures keep low elbows and fingers along the forearm.
  `CabinReactions_FollowTheRadioAndCloseTheLidByHand` passed;
  final contact/turn and closing frames confirm natural hands and contact.
  The practical remains `0.15 m`: `Capture_TheCabinFromThePassengerSeat`
  verifies a readable drawer without the windshield hotspot.
- Passenger exit uses the manual radio-off path, retaining station/playhead.
  Mountain arrival now waits for that exit before arming departure; the earlier
  leg change suppressed the driver's exit. Reboarding clears the old alighting
  timeline. Focused regressions passed:
  `Alighting_ClimbsOutBesideTheCarWhereItActuallyStopped` and
  `Alighting_WalksHimBackRoundAndOntoHisOwnBonnet`.
- Documentation checked with `python tools/check-docs.py` and `git diff --check`.

## 2026-09-09 — Cableway boarding, continuous arrival and waiting Ferryman

- Fixed the existing cabin safety bar to swing inward `90 degrees` along the
  front window before the hero crosses the side doorway and close after
  entry/exit. Boarding turns the hero onto the bench facing cabin-forward.
- Both destination arrivals begin aboard, cover `18 m` of inbound cable and
  the station half-turn, stop at the normal dock and automatically play the
  visible exit. World-space attachment preserves the player's scene parent
  through travel and alighting. The arrival owner first waits for the area
  transition to clear, then restores the cabin, passenger and camera under
  its own black ride fade before revealing the approach.
- `MountainRoadRoot.BuildLastRoute` now also brings an untaken car to the apron
  on cableway return. The Ferryman waits there after direct map visits to the
  village as well as after the ordinary car/cableway round trip.
- Updated current behavior and the physical passages of both bibles; no new
  fiction, canon exception or transport route.
- Focused PlayMode round trip passed: `AlpineCablewayRidePlayModeTests.
  Ride_OnlyLeavesTheAreaOnceTheScreenIsBlack`, `1/1`, `76.19 s`. It checks
  both real area loads, bar timing, cabin-facing pose, attachment, docking,
  automatic exits, reboarding and the waiting Ferryman from `NotTaken`.
  Results and six frames: `Captures/CablewayRegression/`. Reviewed the
  first-person approaches and mountain boarding/alighting frames; the village
  side camera is obscured by a canopy post, so its exit evidence is the
  runtime assertions. `git diff --check` passed. No broad suites or player build.

## 2026-09-09 — Scarf tail rises and trails during running

- Following the user's report that the simplified tail stayed too vertical,
  bounded deformation now bends it into a visibly raised ribbon trailing
  behind during running. Smoothed motion drives its rise and return after
  stopping; a small travelling wave keeps the cloth moving along its length.
  The authored topology and simple hero-body contacts remain; no external
  object queries or detailed cloth solver are added to gameplay.
- Updated the current descriptions and the existing same-day canon decision.
  This is a refinement of the accepted procedural garment form, with no new
  story fact or exception. The first rendered check caught an inward bend:
  normalization includes the hero's `180°` facing rotation. The deformation
  and the backward-displacement assertion now derive the back direction from
  the normalized authored mesh; no extra collision passes were needed.
- The same focused `ScarfPerformance` check then passed (`1/1`, `51.19 s`;
  `TestResults/scarf-running-lift-final.xml` and `.log`). Across `840` measured
  frames it retains the `2 ms` p95 budget and adds rendered tip lift, backward
  displacement, continued motion, calm settling and paused-mesh checks.
  Straight/turning run mean tip lift was `22.45/22.08 cm`, backward offset
  `31.51/31.75 cm`, and sampled tip motion spanned `5.48/6.37 cm`. Following
  `1.1 s` of clamped cloth settling time, residual tip lift was `0.3 mm`;
  paused vertex drift was zero. Hero-body exclusion and teleport reset passed.
- Whole-scarf p95 was `0.60 ms` indoors, `0.62 ms` outdoors standing and
  `0.66/0.68 ms` running/turning; maximum recorded scarf time was `1.08 ms`.
  All five idle/run/turn/settled/reset images were opened and reviewed, then
  published with the report to `Captures/ScarfPerformance`. The scarf visibly
  trails out behind the body while running and hangs down again after stopping.
  `git diff --check` passed. No full suites or player build were run.

## 2026-09-09 — Quiet bus audio teardown with empty door sources

- `CityBusAudio.StopSource` now resets playback time only when the source has
  an `AudioClip`. Door sources start empty and are cleared on release; stopping
  them before the first cue or again after pooling no longer writes an invalid
  playback cursor. Stop/mute, retained engine clips and idle pitch, and door
  clip clearing keep their existing contracts.
- Extended the existing `CityBusRuntimeTests.StopDwell_HoldsForTenSecondsBeforeResuming`
  to capture warnings, stop twice before the first door cue and after clearing
  it, release repeatedly, and retain the engine/door lifecycle assertions.
  The focused EditMode check passed (`1/1`, `0.47 s`;
  `TestResults/bus-audio-stop-final.xml` and `.log`) with no captured warnings.
  An initial attempt exposed a test-only assumption that EditMode dispatches
  `OnDisable`; the test now calls `Shutdown`, which uses the same actor release
  path, and only that selection was rerun. `git diff --check` passed. No full
  suites, PlayMode run or player build was performed for this fix.

## 2026-09-09 — Lightweight scarf with hero-body contacts

- Following continued outdoor frame drops, especially while running, the user
  permitted simpler mechanics and then explicitly retained only contact with
  the hero's body. External objects, buildings and NPCs no longer affect the
  scarf. This superseding exception is recorded in architecture notes and both
  world bibles; collection, yellow `45 cm` form, equipment, cold protection,
  mouth/shower ownership and mirror presentation keep their existing scope.
- Ordinary gameplay uses bounded procedural wind/movement bending and
  `PlayerScarfBodyContacts`: once-measured, bone-following ellipsoids resolve
  wrap, knot and tail contacts against the simplified body. There is no per-frame body
  mesh bake, world-triangle discovery or detailed cloth/contact job. The old
  precise solver remains explicitly opt-in for diagnostic captures only.
- Focused `AreaCaptureFixture.ScarfPerformance` passed (`1/1`, `47.85 s`;
  `TestResults/scarf-body-only.xml` and `.log`). Its `780` measured rendered
  frames include inventory, stationary indoor/outdoor poses, running and
  weaving turns both equipped and unequipped. It checks production collision
  worlds remain absent, body proxies remain present, free tail vertices stay
  outside those proxies after running/turns/teleport, and paused work is zero.
- Whole-scarf p95 is `0.59 ms` indoors, `0.63 ms` outdoors, `0.62 ms` running
  and `0.64 ms` running with turns (budget `2 ms`). Synchronous equip measured
  `7.73/0.62 ms` indoors/outdoors. A single turning sample reached `9.23 ms`;
  the p95 is not a worst-frame guarantee. Whole-frame means in the village
  remain `54–63 ms` in this Editor capture, including scene/render costs.
  All three running/turn/reset images in `Captures/ScarfPerformance` were
  opened and reviewed; the yellow wrap, knot and hanging tail remain readable.
- The earlier `360`/`540`-frame captures and timings below are historical
  evidence for the previous full-contact implementation; its old performance
  report is preserved as `Captures/ScarfPerformance/detailed-contact-performance-report.json`.
  `git diff --check` passed. No full suites, player build or additional Unity
  invocation were run.

## 2026-09-09 — Car audio initialization and workroom shadow warnings

- `LastRouteCarAudio` now configures its five sources, loop clips and filters
  on three inactive anchors before activating them. This avoids Unity's
  clipless filtered-source autoplay warning while preserving ride playback
  and the existing mix.
- `VillageWorkroomEnvironment` explicitly requests Medium (`512 px`) shadow
  faces for `AmbientLight`. Its six point-light faces fit the unchanged PC
  `2048 px` atlas at the resolution URP previously reduced High to; lamp
  intensity, soft shadows and effective quality are unchanged.
- The existing focused PlayMode checks passed together (`2/2`, `47.75 s`):
  `LastRouteCarRidePlayModeTests.Ride_IsHeardFromTheEngineBayAndFallsSilentOnTheApron`
  checks clean source initialization and ride audio; `AreaCaptureFixture.VillageWorkroomWalls`
  also checks the shadow budget. Results: `TestResults/audio-shadow-warnings.xml`
  and `.log`; neither warning appears in the full log. The four refreshed
  `Captures/VillageWorkroomWalls` images retain consistent warm light and
  contact shadows. No broader suite or player build was run.

## 2026-09-09 — Scarf equip slowdown and native simulation

- Reproduced the reported slowdown in the production mother's-house bedroom.
  The initial `ScarfPerformance` probe passed in `4.09 s`
  (`TestResults/scarf-performance-probe.xml`): average frame interval rose
  from `18.63` to `34.22 ms` after equipping. Whole-scarf work averaged
  `17.35 ms`, including `8.19 ms` for wrap/knot contacts. Inventory intervals
  were `20.07 ms` with keys, `19.17 ms` with the folded scarf and `19.61 ms`
  with it equipped; paused geometry work was zero and the preview stayed
  passive and stable. Equipping had a separate one-time `116 ms` cost.
- `PlayerScarfContactJob` executes the existing sequential PBD contacts through
  Burst with strict arithmetic and reusable native buffers. Each world update
  exports its managed geometry/BVHs through four bulk snapshot copies;
  all-surface and closed-component BVHs use median partitions. Exact two-plane
  rejection skips separated pairs before expensive finite-feature checks.
  `PlayerScarfIntegrationJob` retains 120 Hz integration and the same stretch,
  pin and contact schedule. Jobs warm up during scene installation. The shared
  hero also prepares nearby immutable topology/static BVHs during world loading,
  even when unequipped, then resets motion history while retaining those caches.
  Paused frames skip geometry writes and report zero work.
- The focused performance probe now covers the bedroom/inventory and outdoor
  village, records whole-scarf and wrap/knot cost plus discovery, collection
  and spatial-index scopes. Nine phases now measure `60` real frame intervals
  each: a representative p95 must stay below `16 ms`, while both synchronous
  equip actions must stay below `100 ms`.
  Both performance and dense-contact checks assert native execution.
- Final `ScarfPerformance` passed (`1/1`, `33.97 s`;
  `TestResults/scarf-performance-final.xml`), covering `540` measured frames.
  Whole-scarf mean/p95 was `7.45/10.28 ms` indoors and `11.75/14.62 ms`
  outdoors. The initial indoor mean of `17.35 ms` used 12 frames; the final
  phase uses 60. Synchronous equip fell from `116.06` to `25.90 ms` indoors
  and from `1,834.99` to `36.87 ms` outdoors after load-time cache preparation.
  All active samples used the native solver; paused geometry work stayed zero.
- Final inventory frame means were `17.81 ms` for keys, `19.22 ms` for the
  unequipped scarf, `19.78 ms` for equipped scarf and `19.85 ms` after reopening.
  Preview model/camera/texture identity, passive composition and unscaled
  rotation passed. Whole-frame means without/with scarf were `17.50/25.65 ms`
  indoors and `62.38/72.84 ms` in the village; these are Editor measurements
  at the capture settings, separate from the scarf-only budget.
- The native contact/performance selection passed (`2/2`, `105.56 s`;
  `TestResults/scarf-performance-contacts.xml`). Its independent contact proof
  covered `360` rendered frames with zero penetrations, `8,786` body pairs,
  `787` obstacle pairs and `170` NPC pairs. All unprotected witnesses, three
  GPU-only meshes, pause, mirror and native execution checks passed. Seam
  error was `0.31 mm`, minimum NPC distance `3.83 mm`; peak tail/whole/surface
  work was `5.10/39.10/18.02 ms`, with world collection including reset peaking
  at `36.54 ms`. Dense-contact/reset peaks remain above ordinary steady work.
  The later loading-cache change preserved contact math and was covered by
  the final performance rerun. Inspected final captures and reports are in
  `Captures/ScarfCollision` and `Captures/ScarfPerformance`. No full suites or
  player build ran.

## 2026-09-09 — Managed scarf contact performance and independent surface verification

- Cached compact BVH bounds and static median partitions, deferred distant
  mesh lookups, and compacted topology components. The solver reuses exact
  unchanged point results only within one `Resolve`; first-pass stability
  additionally requires zero contacts because that pass includes swept motion.
  It limits swept point work to its first pass and passes triangles by reference.
  Tail contacts retain
  two intermediate four-pass solves and a final 32-pass limit; frame matrices
  and damping are cached. Physical model surfaces and contact thickness remain.
- The rendered interior oracle now sums every triangle's solid angle in double
  precision, with compensated summation and a `1 mm` boundary tolerance.
  Independent edge-crossing checks remain. The obsolete ray-hit limitation
  and its superseded witness no longer describe the current test.
- `ScarfCollisionContacts` passed (`1/1`, `118.05 s`;
  `TestResults/scarf-collision-optimized-verified.xml`). That managed-solver
  capture recorded `360`
  rendered frames, zero detected penetrations, `8,786` body pairs, `794`
  obstacle pairs and `178` NPC pairs. Wall/GPU/NPC unprotected witnesses,
  three GPU-only meshes, pause and mirror checks passed. Seam error peaked
  at `0.31 mm`; minimum NPC distance was `3.88 mm`.
- In the Editor capture, maximum tail-step time fell from `210.72` to
  `84.03 ms`, and maximum world collection time including reset from `114.81`
  to `41.24 ms`. New per-frame means (tail/world) were `59.88/10.16 ms` at
  the wall, `62.15/17.07 ms` in the corner and `9.43/32.71 ms` by the NPC.
  The earlier log has only sparse checkpoints, so it supplies no comparable
  phase means. Tail timing excludes wrap/knot correction and the rest of the
  frame; dense contacts remain expensive in the Editor. No full suites or
  player build ran.

## 2026-09-09 — Collectible scarf, equipment status and cold protection

- Added one folded scarf on the existing linen chest in the parents' bedroom
  upstairs in the mother's house. Its support is derived from the actual room
  plan. Atomic collection preserves its empty source on later scene visits;
  new-game reset restores it. The inventory keeps equipment separate from
  quantity and exposes localized Equip/Remove and In use/Not in use states.
- Added a separate deterministic Blender scarf pack: folded prop, skinned
  neck/nape wrap with a lowered-mouth shape, back knot and `45 cm` cloth tail.
  The user's same-day clarification lengthens the hanging tail to mid-back;
  it changes the garment's form without adding lore.
  Subsequent same-day requests make it yellow and extend physical contacts
  to the hero, NPCs, buildings and other model surfaces. The native cloth
  prototype is replaced by a CPU simulation on the original tail topology,
  shared real-triangle contact queries, and corrected dynamic wrap/knot meshes.
  Hidden source skins preserve the authored mouth shape. Actor and mirror
  meshes are independent, while the mirror copies final corrected geometry.
  Shared `PlayerFactory` equipment uses existing scene wind, shelter, visibility
  and bathing ownership. Visible mouth actions acquire an owner-scoped access
  lease and wait for the original rig's short left-hand pull; cleanup returns
  the wrap without unequipping it. Cached hand/thumb surface offsets place
  the gesture's closest rotated hand surface `4 mm` in front of the garment.
  The mirror copies the same garment and tail,
  with no second cloth simulation. Existing instant inventory consumption
  gains no bodily animation. Core Hero V2 meshes/action counts are unchanged.
- Indexed nearby contacts with a triangle BVH and cached local static-mesh
  BVHs. A mixed hash for packed edge keys removes the terrain-grid topology
  startup bottleneck. Compensated `BakeMesh(..., true)` snapshots apply imported
  renderer scale once. Scarf surfaces update at `410`, after NPC attention
  and outdoor-help contacts; the mirror copies the final surfaces at `420`.
  Finite point/face and edge/edge contacts distribute local positional
  corrections by barycentric weights; swept contacts follow the closest
  feature's motion. Wrap/knot contacts start from the current authored skin
  pose each active frame. Temporary runtime trace instrumentation has been
  removed. The inventory icon now matches the yellow garment.
- Equipped scarf protection halves only the upper-body shiver blend and new
  outdoor frost exposure. Fresh exposure now takes `12/86 s` to first/full
  frost with the scarf, versus `6/43 s` without. Existing ice, full/half warm
  thaw (`8/4 s`), hug/rubs, breath, locomotion and protective-action priority
  remain intact. Changing clothing does not reset either presentation clock.
- Recorded the user-approved bounded exception to the previous upstairs
  interaction ban in story §6 and architecture notes. The scarf remains
  ordinary clothing without a previous owner or family clue; it reuses the
  house atlas cloth tile. Updated both bibles, current-world, technical maps,
  player specification and README. Active logs retain September/August only.
- Initial native-cloth implementation verification: the single
  `AreaCaptureFixture.ScarfJourney` PlayMode selection
  passed (`1/1`, `41.88 s`), including pickup/UI, mouth access, scene persistence,
  wind, pause, cold protection, mirror/visibility, unequip and new-game reset.
  The deterministic Blender validator and `git diff --check` passed. Inspected
  the production-scene captures in `Captures/Scarf` (close garment shots use a
  capture-only fill light). Live cloth pin drift was `0.19 mm`, its seam stayed
  `17.72 mm` from the authored knot centre, free travel was `240.11 mm`, and
  pause drift was zero; the JSON report accompanies the frames.
  Fixed FBX unit import and Cloth's duplicate skin transform during this focused
  reproduction. Frame measurements now wait for completed camera rendering,
  after the hero's two animation passes. No full suites or player build ran.
- Pre-optimization real-model-contact verification: `AreaCaptureFixture.ScarfCollisionContacts`
  passed (`1/1`, `132.48 s`; `TestResults/scarf-collision-grip.xml`). The report
  from that run recorded `360` rendered frames, zero detected
  penetrations, `8,786` body pairs, `792` obstacle pairs and `175` NPC pairs.
  Unprotected poses demonstrably intersected the wall, GPU-only model and NPC;
  the protected run covered three GPU-only meshes, pause and mirror copying.
  Maximum seam error was `0.166 mm`, tail motion `31.663 mm`, and minimum NPC
  surface distance `3.899 mm`. Measured peaks were `114.8 ms` for world contact
  collection including reset and `210.7 ms` for a tail step in a dense corner.
  These measurements form the baseline for the optimization recorded above.
  No full suites or player build ran.

## 2026-09-09 — Village outdoor help and session household progress

- Completed part 4's outdoor help controller: the hero can take a loaded firewood
  basket, walk freely while carrying it, put it on a free support, borrow and
  return the shovel, clear a small snow patch, hold the station crate lid while
  its worker secures the strap, or hold an open gate for a passing neighbour.
  Entry and exit use the shared positioned interaction path and the same visible
  production hero. Help remains voluntary, with brief thanks and no reward task.
- Added a separate Hero V2 bank of 13 bone-only clips and matching sampled
  metre-space prop tracks. The owned carry layer preserves ordinary pelvis/leg
  locomotion, takes precedence over cold arms, and yields to full-body actions.
  It releases on cleanup without changing the hero prefab or prior action banks.
  Graph insertion disconnects its old consumer before adding the carry/cold layer;
  final support contacts apply after graph changes at release.
- Firewood work now moves six existing loose logs into the two existing baskets
  before delivery. The same residents also have finite errands: fetch water in
  one physical pail from the spring and return it to its support, or take the
  shovel to the chapel threshold and station edge. Separate eight-second
  bucket-fill and six-second station-strap actions preserve the six bodies and
  earlier resident banks. The station pose has measured hand anchors, planted
  feet and at least `19.44 cm` of arm reach margin in the source validator.
- `VillageHouseholdProgress` records log ownership, occupied delivery supports,
  the repaired chair, three stages for each snow patch and completed water
  visits. Scene recreation restores these completed changes within the current
  game session; transient carrying retains the original support as its fallback.
  New-game reset clears this progress. Snow work deforms the existing snow field;
  its jobs sit beyond the station's bare apron and the chapel's wet source path.
  Bucket pickup and return blend the actual support offset into the authored
  reach. Wood, shovel, cloth and hinge sounds follow physical local actions.
  Restored six damaged Russian localization strings and added the help prompts.
- Source verification: the outdoor hero validator passed all 949 samples, with
  maximum grip error below `0.001 mm`, unchanged lower-body matrices and matched
  shared endpoints. The gate pose also retains at least `5.95 cm` of anatomical
  reach margin across handle heights `0.60–0.85 m`, including its actual sloping
  yard placement. Inspected source previews with the actual basket, shovel,
  lid and gate meshes. The focused `AreaCaptureFixture.VillageOutdoorLife`
  passed end to end in `394.37 s`: nine completed hero actions, six real logs,
  two delivered baskets, nine clearing stages, completed water return, real
  scene unload/recreation and new-game reset. All three jobs began on measured
  snow and remained visibly cleared after reload. The actual gate route clears
  its fence returns and house 09; resident yielding retains obstacle guards.
- Runtime measurements: `4,581` hero grip samples (maximum `0.138 mm`), `179`
  station partner samples (`0.064 mm`), `479` bucket-fill samples (`5.10 mm`),
  `4,428` carried-log contacts and `715` visible-mesh hand checks. `9,631` hero
  body checks found no penetration. Root/prop steps remained below `0.131 m`.
  Results and reviewed gameplay frames: `TestResults/village-outdoor-life.xml`
  and `Captures/VillageOutdoorLife/verification.json` with PNGs alongside.
  The seeded `VillageOutdoorPartners` remains a diagnostic selection; final
  acceptance uses the complete journey above. Restored 18 importer-only material
  rewrites against the pre-check snapshot; the production hero bytes are unchanged.
  Full suites and a player build were not run.

## 2026-09-09 — Workroom wall flicker

- Reproduced the competing surfaces in house `08`: the shell cavity ended at
  the room-facing finish planes, leaving the lining and shell cut faces
  coplanar. Moved the cavity behind the full wall, partition, ceiling and floor
  thickness, with a minimum `20 mm` backing clearance. Short aperture returns
  close the resulting gaps around the existing windows and door. Regenerated
  the workroom model; playable dimensions, furniture and materials are unchanged.
- Verification: the deterministic Blender validator passed `218` depth and
  aperture samples. The focused `AreaCaptureFixture.VillageWorkroomWalls`
  PlayMode check passed in `15.76 s`: all `24` lining probes hit, with `22`
  shell-backed probes and a minimum finish-to-shell setback of `60 mm`.
  Two pre-existing gaps in the shell require no depth separation; their lining
  remains solid. The initial check incorrectly required backing at those gaps;
  its assertion was corrected against the actual source mesh before rerunning.
- Inspected the room captures with small camera offsets and the exterior
  window view: the large competing wall triangles are gone and the openings
  remain closed around their edges. Results are in
  `TestResults/village-workroom-walls.xml` and
  `Captures/VillageWorkroomWalls/`. The user's current production hero prefab
  remains byte-for-byte unchanged. Full suites and a player build were not run.

## 2026-09-09 — Village household workroom

- Implemented part 3 of the accepted village plan inside the existing house
  `08`: a physical lower room, two cut windows, solid ceiling and floor, real
  furniture, lamps and a screened route into the private living space. Soil
  is lowered under the boards, with a buried foundation and a grid-cell inset
  that preserves the exterior ground beside the facade.
- The same repair neighbour and sewing woman walk to their work, manipulate
  the original tools and materials, finish finite sequences and return home.
  Nine separate Generic clips include tool pickup/return, seating, unpacking,
  sewing, folding and stowing. Cloth and box have real moving hinges; thread
  and contact anchors follow the sampled metre-space tracks. Existing bodies,
  the corrected neck and the earlier resident clips are preserved.
- The production hero uses the shared positioned interaction controller to
  work the household door, hold the chair rail during repair, and sit/stand
  at the bench. A separate action bank leaves the user-edited hero prefab
  intact. Cancellation before actual repair does not commit the result;
  the third assisted hammer contact fixes the same rail. Residents leave the
  door open for an indoor guest while still completing their evening routes.
- The enclosed room suppresses the shared cold layer, removes weather particles
  within its actual volume, muffles outside sound and supplies wood footfalls.
  Outdoor help and persistence between village visits remain part 4.
- The ordinary chase camera reproduced a shoulder intersection against the
  room wall. Local interior shots now use the existing fixed-pose, bounded
  focus and lens API; the room owner yields to other camera owners and releases
  the view on leaving. It preserves all bodies and the physical facade.
- Verification: the Blender geometry and both animation-bank validators passed.
  The focused `AreaCaptureFixture.VillageWorkroom` journey passed: all nine new
  actions, 9,476 NPC grip samples, 355 hero hold samples, 30 door grip samples
  (each maximum below `0.1 mm`), 4,207 indoor walking samples without a furniture
  intersection, cancellation, finished repair, bench entry/exit, real stow,
  weather exclusion and both workers' indoor evening return. The guest stayed
  in the room while both walked home in about `37 s`. This run did not exercise
  the outside-worker evening branch. The final selected run passed in `76.64 s`;
  its normal room-camera frame was inspected after resolving the wall/shoulder
  and foreground-cabinet obstruction. Results: `TestResults/village-workroom.xml`
  and `Captures/VillageWorkroom/verification.json`, with gameplay PNGs alongside.
  The user-edited production hero prefab remains byte-for-byte unchanged;
  unrelated automatic material colour/blend rewrites were restored to their
  captured pre-run contents. Full suites and a player build were not run.

## 2026-09-09 — The print now arrives THROUGHOUT the ramp instead of at its end

- The user judged the retune above by ear: «в конечном итоге неплохо, но я ощущаю
  изменения только при 100 % применении фильтра — а оно должно изменяться в звуке
  прям плавно, прям так же как визуал». The measurement agreed and had been
  sitting in the previous session's own report: distortion across the ramp went
  `0 → 0.79 → 1.4 → 16.5 → 37 %`, so five sixths of the tearing happened in the
  last fifth of the fifteen seconds.
- **Three causes, all the same mistake in different clothes: a quantity heard on
  a logarithmic scale interpolated linearly against the weight, so its audible
  part is spent at the end.** The DRIVE is a ratio against the mask, and scaling
  it by the weight left the dub twelve decibels under the reference at half
  weight, where nothing can tear however wide the mask stands. The BANDS sweep
  four and a half octaves at the bottom and under two at the top, so at half
  weight the slit stood at `11.8 kHz` and the highpass at `55 Hz`, neither of
  which can be heard leaving. And the AMPLIFIER's voice was on `w²` — my own
  change from the session before, made to hold down a mid-ramp swell.
- Fixes, in the same order. A published `ArrivalHeadroomDb = 20` closing on the
  track's own reference as `w^0.20`: **fixed decibels and not a fraction of the
  mixer's ride**, because the ride depends on how loud the room happens to be
  and the shape of an arrival must not — scaled by the ride, a quiet scene
  reached the tearing far later in the ramp than a loud one, for no reason a
  player could see. The projector's fader takes back exactly what the early
  drive adds, so **the published gain across the ramp is arithmetically
  unchanged** and only the ratio the emulsion sees moves forward. A
  `BandArrivalExponent = 0.50` for both ends of the band. And
  `AmplifierArrivalExponent` back to `1.0`, having measured what it was bought
  for: at `2.0` the mid-ramp loudness moved `0.1 LU` while the print's whole
  bite moved into the last fifth.
- The mixer also acquires his level on the first picture rather than slewing up
  to it: without that, six seconds of every threaded reel went by with the hand
  still climbing and nothing able to tear. He still RIDES slowly afterwards,
  which is what keeps a loud passage tearing harder than a quiet one.
- **Verification: the native validator, green, DLL published** (`84A5C843…`).
  Distortion across the ramp is now `0 → 3.08 → 11.41 → 18.10 → 23.15 → 27.36 →
  31.32 → 37.32 %` at weights `0, .1, .2, .35, .5, .65, .8, 1`, and the shape is
  now a CONTRACT rather than a taste: the validator fails under `4 %` a fifth of
  the way in, `8 %` a third of the way and `15 %` at half. Everything else held
  unchanged — the same response shape at full weight, the same rail, the same
  24 Hz grid, the same exact bypass.
- C# mirror rechecked outside Unity (68 constants, none missing or disagreeing,
  every published constant a literal) and the EditMode assembly compiled headless
  with `0` errors. The EditMode test itself still could not be run: the project
  is open in another Unity instance.
- Listening render of `city_theme` through the mode's own ramp sent to the user.

Earlier entries: [`work-log-2026-09.md`](archive/work-log-2026-09.md).
