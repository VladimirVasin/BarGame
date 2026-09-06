# Systems map

An index, not a specification. One row per system: what it guarantees, where its
code lives, and whether it is real.

Detail belongs elsewhere and is not repeated here:

- normative decisions and exact tuning values: `ai/architecture-notes.md`;
- current end-to-end behaviour: `ai/current-world.md`;
- file-by-file layout: `ai/system-tree.md`;
- player-visible history: `ai/release-notes.md`.

## Status vocabulary

| Status | Meaning |
| --- | --- |
| `Current` | Implemented and verified in the repository. |
| `Partial` | Implemented, but a named part of *this system's own* intent is missing. The row must state the gap. |
| `Planned` | Intended; no implementation in the repository. |
| `Deferred` | Explicitly outside the present milestone; see `ai/project-overview.md`. |

A row never carries a status outside this table. Product-level scope cuts
(save data, economy, weather extras beyond rain) are `Deferred` in
`ai/project-overview.md` and do not make an otherwise complete system
`Partial`.

## Systems

| System | Guarantee | Key files | Status |
| --- | --- | --- | --- |
| Village art pass | Detailed house joinery and neutral facade sheets; rock ledges and Return canopy preserve the bowl and routes. | `VillageAssetProvider`, `VillageFacadeAppearance`, `AlpineVillageRockBuilder` | Current |
| Independent rules assembly | Calendar/day schedule, input priorities and temporary vehicle ownership have no Unity dependencies. | `Scripts/Rules`, `BarPromenade.Rules.asmdef` | Current |
| Shared input actions | One binding API serves common actions and preserves existing controls; pause and transitions take priority. | `GameInput`, `GameInputPolicy` | Current |
| Performance capture | Opt-in CPU/GPU/frame/GC and hot-scope distributions; unavailable counters are not reported as zero. | `RuntimePerformanceCapture`, `PerformanceCaptureSamples` | Current |
| Player build asset gate | Read-only validators block stale/missing runtime assets with explicit repair instructions. | `PlayerBuildAssetValidation` | Current |
| Reproducible asset tooling | Pinned tools, failure propagation, output checks and staged publication preserve existing metas. | `tools/run-blender.py`, `tools/asset_pipeline.py` | Current |
| Offshore fishing boats | Up to two hero-local coastal passes with warm beams, soft motors and sparse horns; leaving the shore releases them, independently of the camera. | `CityOffshoreBoat*`, `build-city-offshore-boats-3d-model.py` | Current |
| Mountain Road Blender misc wave 1 | Eight kinds / `102` placements use `19` passive Blender meshes in `12` batches; the plan retains placement, semantic roots, collision and sounds. | `MountainRoadMiscAssetProvider`, `MountainRoadWorldBuilder` | Current |
| Mountain Road composition rhythm | Plan-validated forest openings, five debris chapters and cleared rails/kerbs preserve the climb rhythm. | `MountainRoadCompositionRules`, `MountainRoad{Planner,Validator,SceneryMeshFactory,SurfaceMeshFactory}` | Current |
| Mountain terminal cafe environment | Passive Blender cafe and seven prop assemblies retain plan-owned collision, lighting and appliance audio. | `MountainRoadCafe{AssetRegistry,WorldBuilder,CollisionWorldBuilder}`, `MountainRoadCafeAssetSetup` | Current |
| Bar counter seat / shared physical menu | Four free stools share the physical booklet lifecycle. | `BarCounterStation`, `BarCounterSeatPlanner` | Current |
| Mountain terminal cafe cast | Four-role cast: pair dialogue/refills and occasional husband interjection; attendant stays silent. | `MountainRoadCafe*`, `MountainRoadCafeConversation*` | Current |
| Mountain terminal cafe menu | Physical menu selection/rest/reopen works. Gap: no order, payment, food/drink or story consequence. | `MountainRoadCafeMenu{Model,Controller,Presentation,HintView}`, `CounterMenu*` | Partial |
| City Blender misc catalog | CityMisc `4.9.0`: `82` kinds / `122` assemblies / `259` passive role meshes / `46,542` triangles; plans retain placement, collision and dynamics. | `CityMiscAssetProvider{,.LateCatalog}`, `CityMiscAssetSetup` | Current |
| Nightlife arch shelter | A traversable fixed shelter retains passive residents, causal barrel fire and roof-only rain shelter. | `CityArchShelter{Plan,Placement,Planner,Validator,WorldBuilder,Presentation,SurfaceAppearance,Resident*}` | Current |
| City ground water network | Flush municipal ironwork on the `Roadside` anchor: a gutter grate with a valve lid every `~52 m`, a welded-shut standpipe over a dry trough every `~150 m`. | `CityDecorationPlanner`, `CityDecorationWorldBuilder` | Current |
| District ground | District ground meshes share cell-edge seams and use each district's authored wear palette. | `CityWorldBuilder.BuildDistrictGround`, `CityExteriorAppearance.ResolveDistrictGroundTint` | Current |
| City Blender ordinary buildings | Fixed-metre v2.1 district wrappers total `28` meshes / `4,218` triangles / `194` UV2 opening slots. | `CityBuilding*`, building generators | Current |
| Residential balcony life | Bounded pooled smokers occupy authored Residential docks; Home reconstructs its own balcony-gated selection. | `City/Balcony/CityBalconySmoker*`, `CityPedestrianHandProps` | Current |
| City Blender low-rise landmarks | Bar, supermarket and 209-1-inspired `player_home_exterior_v1` are complete passive semantic exteriors with inset foundations. | `CityBarFacadeWorldBuilder`, `CitySupermarketFacadeWorldBuilder` | Current |
| Unity URP foundation | Twelve build scenes and PC renderer settings with an explicit project-owned Neutral/Bloom/Vignette baseline volume instead of a sample-scene profile. | `6000.6.0f1`, `17.6.0` | Current |
| PS1 presentation | Shared low-resolution composite; optional 4:3, vertex jitter and Begotten mode preserve gameplay controls. | `Runtime/Rendering`, `IntoxicationRenderState` | Current |
| Depth of field tiers | Exterior grades retain broad Gaussian far blur; Bar, Supermarket, Stairwell, Home, Church and Mother's House cap it at radius `0.55`. | `RuntimeSceneSetup`, `DepthOfFieldSettingsBinder` | Current |
| Runtime area composition | Twelve scenes/nine gameplay roots; three area roots construct incrementally during area travel. | `Runtime/Core`, `Runtime/Scenes` | Current |
| Startup waking opening | Frozen `05:59`, five-second input lock, Wake Up/Quit, then continuous wake. | `MainMenuRoot`, `HomeOpening{Controller,Timeline}` | Current |
| Session clock and day/night rules | Persistent calendar at one game minute per unpaused real second (`24 h = 1440 s`); intoxication slowdown preserves calendar and needs rates. | `GameTimeState`, `GameTimeRuntime` | Current |
| World time and pause ownership | One world factor (`1` to `0.88`) and matching physics step; pause leases freeze world/calendar and restore the current factor. | `GameTimeScale{State,Runtime}`, `PauseMenuController` | Current |
| Session day/time displays | Home clock, inventory and queued day announcements follow one persistent calendar. | `HomeAlarmClock`, `InventoryView` | Current |
| Gameplay pause menu | Escape/Start owns input/time/audio; confirmed restart/quit and persistent graphics options remain shared. | `PauseMenu{Model,Controller}`, `GraphicsEffectsSettings` | Current |
| Hero inventory | Shared modal inventory displays needs/cash/items and contextual actions; stacks persist within the session. | `Inventory{Types,State,MenuModel,ItemModelFactory,ConsumableCatalog,Controller,View,IconLibrary,ItemPreviewRenderer}` | Current |
| Hunger, stress, fatigue and consumption | Needs follow the pause-aware calendar; consumption/rest commit relief once. Gap: needs apply no gameplay debuff. | `PlayerNeedsProgressionState`, `PlayerNeedsRules` | Partial |
| Inventory-backed target interactions | Shared item-backed target menus commit consumption once and refund failed startup. | `InventoryItemRequirement`, `InventoryTargetInteraction{Definition,Model,Controller}` | Current |
| Quest log and journal | The calendar fires each dated event once; quest state and journal follow session-owned activation/completion. | `Quest{Types,LogState}`, `GameDaySchedule` | Current |
| Stairwell quest descent gate | The active day-two cat quest gates descent and guides the hero back to the landing before restoring input. | `StairwellQuestDescentBlocker`, `PlayerMotor.MoveTowardsInteractionPose` | Current |
| City blueprint and layout model | One immutable stable-ID blueprint per session: connected sparse cells, typed areas, one Residential bar across from home, supermarket and public lots. | `CityBlueprint`, `CityBlueprintCatalog` | Current |
| City elevation and exterior stairs | Validated elevation plans keep external stairs and walkable ground consistent with collision. | `CityElevation{Plan,Planner,Validator,Rebaser,StairPlacement}`, `CityTerrainSurfacePlan` | Current |
| City mountain boundary and open tunnel | `default-coastal` closes west/south around the non-traversable river cave and one gate-free `8 x 5.5 m` portal. | `CityMountainBoundary*`, `CityMountainBackdrop*` | Current |
| South tunnel travel stub | Walking triggers the authored refusal/return. Gap: no pedestrian transition to Mountain Road. | `CityTunnelTravel{Plan,Planner,Controller,CrossingModel}`, `InteractionPromptView` | Partial |
| Mountain Road area | Separate ascent with ten hairpins, gorge bridge, forest, summit cafe and cableway; plans own route surfaces. | `MountainRoad*`, `MountainRoadBridge*` | Current |
| Village above the cableway | Walkable snow bowl and accessible mother's house. Gap: dinner/news/Cat content and chapel interior remain unwritten. | `AlpineVillage{SnowPlan,SnowTreading,SnowKickup,PeripheralStorm*,RidgeAppearance,WorldBuilder}` | Partial |
| Mother's house interior | Separate two-storey house with furnished bedrooms, runtime stair collision and height-aware fixed cameras. | `MothersHouseInterior*`, `HomeFixedCameraController` | Current |
| The cableway carries | Cabin travel spans areas; a session-safe lease releases on completion/teardown, and pause blocks skipping. | `AlpineCableway{RidePlan,CabinSeat,RideController,RideFactory}` | Current |
| City river and embankments | A `10 m` north-south channel preserves all 144 lots. | `CityRiver{Definition,Plan,Planner,Resources,SurfaceAppearance,WorldBuilder}`, `CityWaterSurfaceFactory` | Current |
| Yard wheelchair rider | One staged yard rider follows its reserved circuit, with authored wheelchair motion and existing local lighting. | `YardWheelchair{Motion,Plan,Presentation,Actor,Factory,Provider}` | Current |
| Bar-side yard composition | The yard keeps its bare-ground wheelchair circuit and reserved utility anchors under the fixed cold spotlight. | `CityOpenAreaDecoration{Plan,Planner}`, `CityOpenAreaWorldBuilder` | Current |
| Cemetery precinct | Deterministic graves, paths, fences and practical lamps dress the separate eastern cemetery. | `CityCemetery{Plan,Planner,WorldBuilder,SurfaceAppearance}`, `CityBenchSitPlan.CreateAll` | Current |
| Church precinct and interior | Garden loop, two seats, low hedge, ground lights, fountain/statue/pot; west entry, one cemetery link and separate interior. | `CityChurch*`, `ChurchGarden*` | Current |
| Residential courtyard pockets | Plan-owned residential courtyards preserve walkable approaches and bounded authored dressing. | `CityCourtyardPocket{Planner,Geometry}`, `CityCourtyardResident{Plan,Factory,Presentation}` | Current |
| City yards | Only west stone terraces add a mason's cart; other fringe yards retain their existing service-belt infrastructure. | `CityFringeYard*`, `CityFringeYardLifePlanner` | Current |
| District public places | Four public lots retain validated street approaches, causal props and bounded local practicals. | `CityDistrictPointOfInterest{Plan,Planner,WorldBuilder}`, `CityPointOfInterestSurfaceAppearance` | Current |
| Drying yard babushkas | Staged drying-yard residents share authored domestic actions and local speech/prop ownership. | `DryingYardBabushka{Provider,Plan,Presentation,Factory}`, `CityPedestrianHandProps` | Current |
| Weighbridge attendants and needle | Two staged attendants and the physical weighbridge needle share authored weight, animation and interaction rules. | `WeighbridgeAttendant{Provider,Plan,Presentation,Factory}`, `CityPedestrianHandProps` | Current |
| Cemetery mourner | The grave-side mourner uses her own authored presence, gestures and localized response. | `CemeteryMourner{Provider,Plan,Timeline,Presentation,Factory}`, `CityCemeteryMournerController` | Current |
| Cemetery watchman and gate lodge | The watchman and lodge provide the authored grave-work offer and payment interaction. | `CemeteryWatchman{Provider,Plan,Quips,Interaction,Presentation,Factory}`, `CityCemeteryPlanner.AddLodge` | Current |
| Cemetery gravedigging | Up to three open jobs; grave acts/epitaphs persist per plot, with payment committed through the watchman. | `CemeteryGravedigging{Plan,Controller,Register}`, `CemeteryGraveWork{Stage,Ledger}` | Current |
| Cemetery ravens | A sparse raven pair appears around the first sealed grave, flushes near the hero and returns afterward. | `CemeteryRaven{Provider,RigAnchors,Factory,Plan,PoseRules,IdleModel,HeadModel,FlightModel,DirectorModel,Actor}` | Current |
| The spring and its brook | Village brook and Mountain Road culvert water are implemented. Gap: the dark mountain reaches still await visual acceptance. | `AlpineVillageBrook{Plan,Planner,Builder}`, `AlpineSpringWaterResources` | Partial |
| Sitting on the mother's sofa | The sofa reuses shared bench seating with front-only approach, measured cushion contact and owned shadow cleanup. | `MothersHouseSofaSeatPlanner`, `CityBenchSit{Plan,Interaction,WorldBuilder}` | Current |
| The mother in her chair | A silent mother sits in the rocking chair on her own rig; voice, interaction and expression driving remain unwired. | `MothersHouseMother{Plan,Presentation,Factory,Provider,AssetSetup}`, `MothersHouseRockingChairMotion` | Current |
| Outdoor raven roosts | Seeded pairs occupy bounded open-world perches, flush locally and obey area exclusion/vehicle gates. | `RavenRoost{Plan,Controller}`, `{City,MountainRoad,AlpineVillage}RavenRoostPlanner` | Current |
| Seacoast precinct | A plan-owned mol, beacon, boat station and east shore share real approaches, footprints and causal dressing. | `CitySeacoast{Plan,Planner,WorldBuilder,SurfaceAppearance}`, `CitySeaResources` | Current |
| Sea water | Shared water drive owns sea swell, foam and uneven shore swash. | `CitySeaResources`, `CityWaterResources` | Current |
| Beach sand | Deterministic shallow relief and compressible foot trails over fixed collision. | `CityBeachSandPlan`, `CitySandTreading` | Current |
| Lighthouse island | One distant fog-framed island landmark owns its silhouette, beacon/beam and authored sightline. | `CityLighthouseIsland{Plan,Planner,MeshFactory,WorldBuilder,Resources}` | Current |
| Seacoast fisherman | The boat-station fisherman uses his staged rod/line action and localized response at the real dock. | `SeacoastFisherman{Provider,Plan,Quips,Interaction,Presentation,Factory,PipeEffect,Line}` | Current |
| Park chess set inhabitants and wire lamp | The Central Park chess set gets its two permanent inhabitants and its one light. | `ParkChessPlayer{Provider,Plan,Presentation,Factory}` | Current |
| Park chess-set men | Imported chess and draught pieces retain the measured table and opening-layout contracts. | `CityChessBoardGeometry`, `CityChessSetPlan` | Current |
| Park board games | Both boards are playable. | `ChessRules`, `ChessEngine` | Current |
| Park chess-set quarrel | The two men hate each other on the only grounds available: one plays chess and despises draughts, the other the reverse. | `CityParkQuarrelController`, `ParkQuarrelTimeline` | Current |
| City world builder | One validated plan feeds shared staged/synchronous construction; temporary scene, input and audio ownership is explicit. | `CityWorldBuilder`, `CityTerrainSurfaceWorldBuilder` | Current |
| City street surface presentation | Road/sidewalk/apron meshes share the deterministic street plan and measured surface recipes. | `CityGenerationSettings`, `CityStreetSurface{Plan,Planner}` | Current |
| Central Park surfaces | The divided park uses shared gravel, paths and bridge geometry from its pure surface plan. | `CityParkSurfaceAppearance`, `CityWorldBuilder` | Current |
| Pedestrian personal space | Above alcohol `60`: guarding palm; above `80`: close shove. | `CityPedestrianPersonalSpace{Rules,Controller}`, `PlayerMotor` | Current |
| Pedestrian street insults | Local insult responses require the authored proximity/facing gates and reuse pooled speakers. | `CityPedestrianInsult{Rules,Lines,Controller}`, `CityPedestrianPersonalSpaceController.IsHeroAvailable` | Current |
| City and Home street pedestrians | City streams a bounded walker population; Home reconstructs only its bounded exterior context. | `Runtime/City/NPC`, `CityPedestrianHandProps` | Current |
| NPC Human V2 anatomy, appearance and visibility | `26` rigged humanoid designs exist on disk; the cashier swap is one-for-one and does not grow the active cast. | `NpcHumanV2AssetSetup`, `NpcDesignAppearanceCatalog` | Current |
| City Route 01 bus | One validated route and pooled bus run in City. Gap: no Home simulation or live map vehicle marker. | `Runtime/Vehicles`, `CityBus{Plan,Planner,Actor,Audio,Director,Presentation,Factory,AssetRegistry}` | Partial |
| Pedestrian bench rests | Eligible walkers reserve benches, play owned sit/rest/stand actions and return to their route. | `CityBenchRest{Plan,Planner}`, `CityBenchNpcRestController` | Current |
| Route 01 passengers | Hero plus two ambient passenger places. Gap: fares, destination choice, persistence and live tracking. | `CityBusRide{Plan,Controller}`, `CityBusStopWait{Plan,Planner}` | Partial |
| City visual diversity and collision proxies | Deterministic visual variants share simple semantic collision proxies and reusable resources. | `CityDecoration{Descriptor,Plan,Planner,Validator,WorldBuilder}`, `CityDecorationCollisionCatalog` | Current |
| Road-edge fences | Plan-owned fences protect boundaries while preserving every validated road/waterside opening. | `RoadFence{Planner,Plan,WorldBuilder}`, `CitySurfaceDescriptor` | Current |
| Supermarket interior, product art and finite shelf purchases | A passive Blender-authored `16 x 11 x 3.6 m` shell plus one six-model, no-brand/no-text product pack shared by shop, inventory, refrigerator and cat flow. | `tools/build-supermarket-{interior,products}-3d-model.py` | Current |
| Supermarket cashier | One staged cashier uses the imported rig, authored service motions and existing shop interaction. | `SupermarketCashier{Provider,AssetRegistry,Factory,Presentation,Actor,SurveillanceState,BlinkState,Interaction}` | Current |
| Supermarket CCTV corner cameras | Two corner cameras use authored mounts and bounded mechanical scanning without gameplay surveillance. | `SupermarketInteriorAssetRegistry`, `SupermarketSecurityCamera{,WorldBuilder}` | Current |
| Supermarket fluorescent atmosphere | Visible fixtures own the store's restrained light, flicker and sound within explicit shared-resource budgets. | `SupermarketInteriorAtmosphere`, `SupermarketFluorescentFlicker` | Current |
| City day/night presentation | Apply one time sample to directional/ambient/reflection light, windows and registered electric glow. | `RuntimeSceneSetup`, `CityDayNightController` | Current |
| Deterministic exterior weather | Shared weather drives area rain/snow/wetness and causal audio. Gap: rain does not dim ambient light or alter grading. | `GameWeatherRules`, `CityWeatherController` | Partial |
| Exterior cloud ceiling | A passive shared-density shell supplies a camera-relative cloud ceiling with bounded horizon coverage. | `ExteriorCloud{AssetMetadata,Profile,MotionRules,Resources,Field,CaptureCamera}`, `ExteriorCloud.shader` | Current |
| Runtime cloth rags | Visible cloth uses shared bounded wind response and explicit attachment constraints. | `ClothPanelFactory`, `CityClothWindRegistry` | Current |
| City wind dressing | Shared wind drives authored trees/props/cloth while preserving causal movement limits. | `CityWindDressing{Plan,Planner,Validator,WorldBuilder}`, `CityRopeSpanGeometry` | Current |
| Scene and place music | Shared handoff rules mix shipped scene/place themes. Gap: cemetery and church music slots are empty. | `MusicMix`, `SceneMusicPlayer` | Partial |
| Common audio mix | One shared mixer routes scene themes, causal ambience, effects and reversible intoxication processing. | `GameAudioMixer`, `BarPromenadeAudio.mixer` | Current |
| Intoxication sound perception | Shared bounded VHS processing follows the smoothed alcohol level and returns to exact bypass when sober. | `IntoxicationPerceptionRules`, `IntoxicationAudioDriver` | Current |
| Retro SFX and ambience | Generated retro cues and local ambience share routing, distance limits and scene-owned cleanup. | `RetroSfx`, `RetroAudioService` | Current |
| Causal City soundscape | Visible local sources own City sound; bounded schedules and shared routing control the mix. | `CitySound{SourceDescriptor,scapePlan,scapePlanner,SchedulePlanner,Occlusion}` | Current |
| Home alarm clock | Bed-relative 27.6 cm clock; readable opening close-up, frozen flickering `05:59`, solid `06:00` on Wake and then session time. | `HomeAlarmClock{Plan,Builder,Synthesis}`, `HomeAlarmClock` | Current |
| Road, park and ground navigation | Pure walkable masks constrain roads, parks and authored ground consistently with physical boundaries. | `RoadWalkableArea`, `CityGroundTraversalPlan{,ner}` | Current |
| City route planning | Deterministic ordered shortest paths over generated street and park-path edges with a binary min-heap. | `Runtime/Map`, `CityLayout` | Current |
| Player motor | Shared tank movement/run uses constrained velocity and common input; contextual approaches retain their own owner. | `PlayerMotor`, `PlayerDirectionalInput` | Current |
| Third-person chase camera | Shared collision-aware chase/orbit blends cinematic motion and yields to owned fixed/modal shots. | `PlayerCameraFollow`, `IntoxicationDollyZoomModel` | Current |
| Home fixed camera | Home uses authored fixed shots with smooth transitions and explicit contextual camera ownership; the main-room shot pans up to 18/9 degrees, and only as far as it must, to keep the hero framed. | `HomeCameraShot{,Selector}`, `HomeFixedCameraController`, `FixedCameraFocus` | Current |
| Home player visibility | Grouped occluder dither and fixed-shot rules keep the hero visible without changing collision. | `HomeOcclusion{Registry,Resolver}`, `HomePlayerOcclusionController` | Current |
| Modular 3D hero presentation | One Hero V2 in nine gameplay roots: 34 parts / 2,384 triangles, 31 bones and 41 actions. | `Player3D*`, `PlayerFactory` | Current |
| Silent Hill attention | Layered gaze reacts to nearby authored targets within rig limits and yields to contextual ownership. | `PlayerAttention{Rules,Controller,Magnet}`, `IntoxicationHeadModel` | Current |
| Continuous 3D player interactions | Shared positioned actions preserve visible entry/exit continuity and clean up presentation/input ownership. | `PlayerAnimatedInteraction{Timeline,Controller}`, `PlayerDoorAction{Plan,Controller,Target}` | Current |
| Bed sleep and wake | Two hand-supported pelvis steps with seated stops in both directions; a domed pillow dents and recovers. | `HomeBedInteraction{,Plan}`, `PlayerAnimatedInteractionPelvisPath` | Current |
| City bench and park game-table seats | Plan-owned seats reuse shared contextual sit/rest/stand with measured contacts and camera cleanup. | `CityParkBenchPlanner`, `CityBenchSit{Plan,WorldBuilder}` | Current |
| The Ferryman's car and its passenger seat | An imported car, driver and passenger seat own doors, attachment, camera, audio and cross-area arrival. | `LastRouteCar{Plan,Factory,Doors,Suspension}`, `LastRouteCarDashboard{,State,Target,Gaze}` | Current |
| The last route | Session journey stages coordinate Ferryman dialogue, car legs, blackouts and arrivals; pause blocks skipping. | `LastRouteRideController`, `LastRouteCar{DrivePath,DriveModel,Driver,GiveWay,GiveWayModel}` | Current |
| Home bed sleep | One trigger on the door-side bed edge; guided walk, neutral settle, then `BedEnter`/`BedSleepLoop`/`BedExit` through a real bedside sit. | `HomeBedInteraction{Plan,}`, `PlayerAnimatedInteractionController` | Current |
| Home balcony smoking | One modal balcony smoking sequence owns its prop, sound, camera and completion-only stress relief. | `HomeBalconySmoking{Plan,Interaction,Timeline,CameraDrift,ExhaleEffect}`, `HomeBalconyWorldBuilder` | Current |
| Home refrigerator | Physical shelf browsing, first-person inspection and atomic collection work. Gap: `Use` is registered but unavailable. | `HomeRefrigerator{Plan,WorldBuilder,View,Interaction,InteractionTimeline,FirstPersonHand}` | Partial |
| Player shadows | Real mesh shadows and an analytic contact patch follow hero/contextual visibility ownership. | `Player3DCharacterPresentation`, `Player3DRagdollController` | Current |
| Interaction/UI | Common action bindings and explicit input priorities serve shared prompts and menus; specific look/debug input remains local. | `PlayerInteractor`, `InteractionPromptView` | Current |
| F9 debug controls | City/Bar/Road/Home share intoxication and day `1–7` controls. | `MinigameDebugWindow`, `HomeDebugCityMapShortcut` | Current |
| Structured session diagnostics | Bounded NDJSON records correlated operations; optional performance reports are separate from the support log. | `Runtime/Diagnostics`, `MinigameDebugWindow` | Current |
| Bar activity flavour | Legacy activity identity still selects bar flavour; the removed sprite minigames remain absent. | `BarActivityKind`, `BarActivityAssignment` | Current |
| Area map UI | City/MountainRoad/Village tabs consume pure plans; area travel and teleport share destination validation. | `CityMap{Controller,View,AreaController,AreaView,MountainRoadOverlay,AlpineVillageOverlay}` | Current |
| Map XYZ inspection | Map points expose precise world coordinates and validated teleport destinations in the existing debug flow. | `CityMapTeleport{Lattice,Grounds}`, `CityMapPointDescriptor` | Current |
| Map arrival ground | The clamp belongs to the area the player stands in, because both scenes share one coordinate system and the mountain route starts on top of the city. | `ICityMapTeleportGround`, `CityMap{City,MountainRoad}TeleportGround` | Current |
| Scene transition | Guarded direct/door loads own pending activation and terminal cleanup; failure handling is centralized. | `PlayerDoorAction{Plan,Controller,Target}`, `CityGameRoot` | Current |
| Area loading transition | One directed illustration and bottom bar; 20% load / 80% construction, owned until the destination is ready. | `AreaTravelService`, `AreaLoading{Root,ArtCatalog}` | Current |
| Door transition presentation | A deterministic `3.15 s` unscaled fixed-camera door sequence in a black void, tinted warm on entry and cold on exit, with generated latch and hinge cues. | `DoorTransition{Root,Timeline,Direction}`, `RuntimeSceneSetup` | Current |
| Session state | Session facade delegates temporary vehicle ownership; resets and stale leases cannot leak ride state into a new game. | `GameSessionState`, `CityBlueprintCatalog` | Current |
| Bar drink retail and physical service | The inset 2x2 menu offers exactly four low-grade drinks; either order-key family pays once. | `BarDrink{Catalog,MenuPresentation,ServicePlan,ServiceTimeline,ShopController,VesselView}` | Current |
| Intoxication stages and presentation | Session alcohol drives reversible visual/audio/body presentation and bounded passive recovery. | `IntoxicationStageRules`, `IntoxicationStatusController` | Current |
| Continuous balance and falling | One balance model coordinates drift, wall support, fall/ragdoll/crawl/rise and directional recovery. | `PlayerBalanceModel`, `PlayerBalanceRules` | Current |
| Hero drunk muttering | A seeded stage-dependent clock drives short authored mutters, slurring and an owned speech bubble. | `HeroMutterModel`, `HeroMutterLines` | Current |
| Hero nausea bouts | High intoxication drives the walking gauge, cancellation gates, vomiting relief and session mouth-soiling. | `HeroNauseaClock`, `HeroNauseaGaugeModel` | Current |
| Bar interior and exterior | Imported bar shell/service assets keep plan-owned collision, entrance geometry and shared resources. | `BarInteriorLayout{Plan,Planner,Validator}`, `Bar{AssetRegistry,ServicePropFactory}` | Current |
| Player home interior | Blender `home_interior_v1` parts fill the validated apartment and balcony; Unity keeps collision, contacts and cameras. | `HomeInteriorModelLibrary`, `HomeAuthoredVisualFactory` | Current |
| Apartment calendar appearance | Exact days `1–7` accumulate domestic neglect; later days keep state seven. | `HomeApartmentDressing`, `HomeApartmentDay{Rules,Controller}` | Current |
| Player-home stairwell | A shared ramp/step plan joins street and home with height-selected cameras and an authored atmosphere. | `StairwellLayout*` | Current |
| City facade presentation | The legacy 4×4 path remains only for clipped Home crossings. | `CityFacade{Grid,Appearance}`, `CityBuilding{SurfaceAppearance,WindowSlotAppearance}` | Current |
| City window presentation | Pure facade/window slots select bounded lit shares and preserve district palettes and daytime light floors. | `CityDistrict{ArtProfile,PresentationPlan,PresentationPlanner}`, `CityWindowAppearance` | Current |
| Stairwell surface presentation | Cached textures and material property blocks apply measured stairwell surfaces without instance materials. | `StairwellSurfaceAppearance`, `SurfaceAppearanceCore` | Current |
| Mountain Road surface presentation | Measured printed/borrowed recipes share materials and deterministic UVs; shader exclusions remain explicit. | `MountainRoadSurfaceAppearance`, `MountainRoad{Surface,Terrain,Scenery}MeshFactory` | Current |
| Home surface presentation | Twelve shared apartment sheets bind authored semantic parts with their UVs; permitted hardware keeps metre fitting. | `HomeSurfaceAppearance`, `HomeAuthoredVisualFactory` | Current |
| Stairwell cat | The perched cat supports talk/feed and the grin controller; the future story grin trigger remains unwired. | `StairwellCat*`, `StairwellCatInteraction` | Current |
| Home exterior context | Home reconstructs a bounded same-seed street view only; Balcony gates its residents and atmosphere. | `HomeExteriorContext{Plan,Planner}`, `HomeExteriorViewBuilder` | Current |
| Player home atmosphere | The shared clock controls visible home lights, while Balcony borrows and restores the City's atmosphere. | `HomeInteriorAtmosphere`, `HomeDayNightController` | Current |
| Mirror brushing | Manual X/Y brush contact fills the gauge; full progress shows teeth, then spits into the real basin. | `HomeTeethBrushing{Interaction,Progress,Timeline,ArmPose}` | Current |
| First-person toilet | Actual hero arm aims ballistic urine for `6 s` plus `4 s` shaking; wet marks persist across Home loads within the session. | `HomeToilet{Interaction,FirstPersonView,AnatomyDynamics}`, `HomeUrine{Effect,Residue}` | Current |
| Home bathroom mirror | A real reflection by the old geometric trick: a hole in the north wall, a mirrored copy of the bathroom behind it and a second instance of the hero prefab as his reflection; no RenderTexture, camera or stencil. | `HomeBathroomMirrorWorld`, `HomeBathroomMirrorOpeningBuilder`, `HomeMirrorSubtreeClone`, `HomeMirrorPlane`, `HomeBathroomMirrorResources` | Current |
| Home bathroom scenes | Shared approach/exit restores rig, camera, input and resources; shower plumbing faces the hero between his braced hands. | `HomeBathroomSceneInteraction`, `Home{Shower,TeethBrushing}Interaction` | Current |
| Bar patrons and bartender | Eleven patrons and the ordinary two-armed bartender use grounded shared props; six-arm art remains legacy-only. | `BarPatron{WorldBuilder,DrinkingBehavior}` | Current |
| Bar cinematic atmosphere | A bounded arrival reveal, six practicals and source-owned room/jukebox audio retain independent cleanup. | `BarInteriorAtmosphere`, `BarMusicPlayer` | Current |
| Automated tests | Focused EditMode/PlayMode contracts share infrastructure; complete suites require an explicit release/full-regression request. | `Assets/Tests/{EditMode,PlayMode,Infrastructure}` | Current |
| Automated test audio guard | Silence global listener output for every EditMode and PlayMode run without pausing sources or DSP, then restore the previous volume. | `BarPromenade.TestSupport`, `AutomaticTestAudioMute` | Current |

## Primary intended flow

Structural backbone only. Exact world behavior lives in `ai/current-world.md`;
implementation decisions live in `ai/architecture-notes.md`. Earlier verbose
rows are preserved in `ai/archive/systems-map-2026-09-06.md` as a superseded snapshot.

```text
build index 0 -> MainMenu -> BeginNewGame + HomeArrival.OpeningSleep
  -> HomeInterior sleeping opening
     -> 05:59 clock shot -> 5 s input lock -> Wake Up / Quit
     -> Wake -> 06:00 + session clock starts -> alarm hold -> 6 s wake
     -> ordinary Home control -> StairwellInterior -> City
     -> debug F9 -> 06:00 if needed -> direct City at home
                 -> teleport enabled + map opens after transition

session clock -> one-based day -> DAY N announcement + inventory day/time
              -> day/night sample -> City + Home lighting, Home clock display
              -> elapsed minutes -> hunger (1440) + fatigue (1080) -> inventory bars
seed + session clock -> weather slots -> rain intensity -> rain field + rain bed
                     -> storm windows -> lightning flash + delayed thunder

map -> City / Mountain Road / Village tabs -> confirm other area
  -> AreaLoading Single -> directed still + bottom bar -> destination Single (20%)
  -> staged RuntimeComposition (80%) -> root ready -> restore audio/time/input
  -> MountainRoad -> tunnel -> ten-hairpin climb around high bridge -> plateau

blueprint ID + seed -> immutable blueprint -> validated sparse layout
  -> typed area cells -> surfaces, navigation, city map
  -> one Residential bar across from home / supermarket / district public places
  -> street surface plan -> Road v2 carriageway + sidewalks + Road v2.1 aprons
     -> pedestrian graph -> profile-sized player-relative walker population
     -> Route 01 loop -> stops + poles -> one pooled bus actor
        -> board prompt -> seat 07 -> ride -> alight at a later stop
  -> night fixture plan -> lamps, signals, halos
  -> decoration plan -> visuals + collision proxies
  -> fence plan -> rails with clearance openings

nine gameplay roots -> PlayerFactory -> Resources/Player/Player3DV2.prefab
  -> 41 Actions: Idle / Walk / heavy Run + seated drink 2/3/2 + face atlas/status bones
  -> Shift or L3 + forward -> 4.2 m/s run; backpedal and scripted approaches walk
  -> actual constrained speed owns Run weight; intoxication scales it, fatigue does not
  -> contextual actions: bed, smoking, cat feeding, bus board/ride/exit
  -> continuous balance model: drift, recovery steps, wall hand; lost capture point -> Fall clip -> bounded ragdoll -> Rise -> Relaxed
  -> first-person refrigerator arm; bar drink stays on nested full-body seated rig
  -> inventory portrait

player -> interaction -> SceneTransitionService -> DoorTransition
  -> preload destination -> activate at blackout
  -> BarInterior   -> activity-flavoured dressing + seated/standing 3D patrons
                   -> counter station -> physical drink service
  -> SupermarketInterior -> finite shelves -> atomic purchase
  -> StairwellInterior   -> cat -> Talk / feed
  -> HomeInterior        -> bed, balcony smoking, refrigerator, three fixed shots
                         -> bounded same-seed exterior reconstruction

all gameplay results -> GameSessionState -> preserved across Single loads
state boundaries     -> correlated rotating debug.log (NDJSON)
scene root           -> audio mixer snapshot + scene music + ambience
URP post-processing  -> PS1 composite -> crisp soot/bone IMGUI overlay
```
