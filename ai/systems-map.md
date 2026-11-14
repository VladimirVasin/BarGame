# Systems map

One row per system. Decisions:`ai/architecture-notes.md`;behaviour:`ai/current-world.md`;
history:`ai/release-notes.md`;paths:`git ls-files`/`rg`.

## Status vocabulary

| Status | Meaning |
| --- | --- |
| `Current` | Implemented/verified. |
| `Partial` | Implemented; row names its own gap. |
| `Planned` | Intended, absent. |
| `Deferred` | Outside milestone; `ai/project-overview.md`. |

Scope cuts are `Deferred`; a completed system stays `Current`.

## Systems

| System | Guarantee | Key files | Status |
| --- | --- | --- | --- |
| Village scenery | 18 households/8 sheds/5 yards; local physical maps/unique closeups. | `AlpineVillage{AbandonmentPlan,ExpansionBuilder}`, `VillageSurfaceAppearance` | Current |
| Lodge stove | Soft fire; both doors shut: whole lodge warm, else indoor 2.5 m. | `LodgeStove{Interaction,SessionState}` | Current |
| Lodge shelter | Doors/wind/lamp/seats/photo/hatch; 7 wood identities. Gap: cot/tea stubs. | `LodgeShelter*`, `LodgeInteriorInteractions`, `LodgeWoodAppearance`, `VillageInteriorAcoustics` | Partial |
| Village paths | Ground junctions; brook bridge. | `AlpineVillage{JunctionPlan,FootbridgePlan}` | Current |
| Village life | Six residents/31 homes; help persists; automatic greetings 5 Hz, E immediate; reusable visibility buffer. | `AlpineVillageLifeController`, `VillageWorkroomController`, `VillageHousehold*` | Current |
| Narrative inspection | Village 24+4; hatch choice/reply; Home silent painting close-up. | `NarrativeInteraction*`, `AlpineVillageNarrative*`, `HomeWallPainting` | Current |
| Independent rules assembly | Calendar/day schedule, input priorities and temporary vehicle ownership have no Unity dependencies. | `Scripts/Rules`, `BarPromenade.Rules.asmdef` | Current |
| Shared input actions | Pause/modal priority; Alt cursor blocks mouse combat; focus guard. | `GameInput`, `GameInputPolicy`, `GameCursor` | Current |
| IMGUI | Retained pointer/text; dark Retina buttons; Editor colours/textures preserve authored values. | `RetroUi{Input,TextField,Theme,Texture}` | Current |
| Performance capture | Frame/GPU/GC, inclusive CPU; requested/actual target, mismatch rejection; missing counters explicit. | `RuntimePerformanceCapture`, `PerformanceCaptureSamples` | Current |
| Player build asset gate | Read-only validators block stale/missing runtime assets with explicit repair instructions. | `PlayerBuildAssetValidation` | Current |
| Reproducible asset tooling | Pinned tools, failure propagation, output checks and staged publication preserve existing metas. | `tools/run-blender.py`, `tools/asset_pipeline.py` | Current |
| Offshore fishing boats | Two hero-local coastal passes avoid the working port; leaving shore releases models, sounds and wakes. | `CityOffshoreBoat*`, `build-city-offshore-boats-3d-model.py` | Current |
| Mountain Road Blender misc | 8 kinds/102 placements/19 meshes; plan-owned placement/collision/audio. | `MountainRoadMiscAssetProvider`, `MountainRoadWorldBuilder` | Current |
| Road rhythm | Openings/five debris chapters/clear rails-kerbs retain the climb. | `MountainRoadCompositionRules`, `MountainRoad{Planner,Validator,SceneryMeshFactory,SurfaceMeshFactory}` | Current |
| Road surfaces/cafe | Local 15 families/4K+2K cafe; BC7≤80MiB, unchanged geometry/wind/light. | `MountainRoadSurfaceLibrary`, `MountainRoadCafeSurfaceAppearance` | Current |
| Bar counter seat / shared physical menu | One-hip supported seating; three free stools share the physical booklet lifecycle. | `BarCounterStation`, `BarCounterSeatPlanner` | Current |
| City diner placement | Whole lot/level entry/threshold/shelter/map; existing cast/menu/light. | `CityCafePlan`, `CityWorldBuilder`, `CityGameRoot` | Current |
| City diner cast | Four-role cast: pair dialogue/refills and occasional husband interjection; attendant stays silent. | `MountainRoadCafe*`, `MountainRoadCafeConversation*` | Current |
| City diner menu | Physical menu selection/rest/reopen works. Gap: no order, payment, food/drink or story consequence. | `MountainRoadCafeMenu{Model,Controller,Presentation,HintView}`, `CounterMenu*` | Partial |
| City Blender misc catalog | Shared street/park/river/coastal meshes; plans retain placement/collision. | `CityMiscAssetProvider{,.LateCatalog}`, `CityMiscAssetSetup` | Current |
| Nightlife arch shelter | A traversable fixed shelter: passive residents who talk among themselves, a causal barrel fire, roof-only rain cover. | `CityArchShelter{Plan,Placement,Planner,Validator,WorldBuilder,Presentation,SurfaceAppearance,Resident*,Conversation*}` | Current |
| City ground water network | Flush municipal ironwork on `Roadside`: a gutter grate every `~52 m`, a welded standpipe every `~150 m`. | `CityDecorationPlanner`, `CityDecorationWorldBuilder` | Current |
| City ground | Height-aware joints, sectors and local grave/sand. | `CityGroundSurfaceSystem`, `GroundJointTopology` | Current |
| Construction joints | Metric edges and thresholds. | `RuntimePrimitiveFactory`, `interior_kit.py` | Current |
| City street support | Blender topology joins road, pavement and terrain. | `CityStreetSupportPlan`, `CityTerrainStreetBoundaryPlan`, `CityStreetSurfaceWorldBuilder` | Current |
| City Blender ordinary buildings | Work8;all housing entries:fixed lamps;passages:graffiti/utilities/local faults. | `CityBuilding*`, `CityCoveredPassage*`, `CityResidentialEntrance*` | Current |
| Residential balcony life | Bounded pooled smokers occupy authored Residential docks; Home reconstructs its own balcony-gated selection. | `City/Balcony/CityBalconySmoker*`, `CityPedestrianHandProps` | Current |
| Residential laundry | Residual L-shell; silent worker folds/carries the same cloth/basket; enamel sign/shared light/hum. | `CityLaundry{Plan,AssetProvider,Controller,WorldBuilder}`, `build-city-laundry-3d-model.py` | Current |
| Residential dog park | Residual fenced yard; three articulated dogs, two permanent owners; open entry and local voices. | `CityDogPark{Plan,AssetProvider,Controller,WorldBuilder}`, `CityDogPose`, `CityDogActivity` | Current |
| City Blender low-rise landmarks | Ridge Home03-04/lower bar/shop retain fixed-metre facades. | `CityBarFacadeWorldBuilder`, `CitySupermarketFacadeWorldBuilder` | Current |
| Unity URP foundation | 14 scenes; PC Neutral/Bloom/Vignette. | `6000.6.8f1`, `17.6.0` | Current |
| Scene presentation | 16:9/native/SMAA;4:3 unsupported;Editor gamma;City/warehouse SSR. | `Scene*Feature`, `GraphicsEffectsSettings` | Current |
| Village local lighting | Real emitters; bounded air/snow/bounce, metal and spring reflections. | `AlpineVillageLightingPresentation`, `SceneReflectionCapture` | Current |
| Road lighting | Bounded air/snow/headlights/halos/metal/bounce; station reflections. | `MountainRoad{LightingPresentation,ReflectionController,MetalAppearance}` | Current |
| Depth of field tiers | City Gaussian 8–28 m, interiors radius 0.55. Owned inspection Bokeh. | `RuntimeSceneSetup`, `DepthOfFieldSettingsBinder`, `CinematicDepthOfField` | Current |
| Runtime area composition | 11 roots;four staged builders;doors retain dormant City/village. | `Runtime/Core`, `Runtime/Scenes` | Current |
| Combat | Bar tissue/skull/recoil;3hits→fall/rise;hero grace;HUD HP<100. Gap:pierce. | `CombatActor`,`CombatTestRoot`,`CombatHudLayout` | Partial |
| Crane | Hook6m/up2m/hold5/E6/hoist-haul/bodycam/fall;home10;fatal3→regen10/kill0. | `Combat*.Crane*`,`CombatActor.Restraint` | Current |
| Authored enemies | Bottles:glass fracture,flesh UV,held/snapped poses;all six:horror surfaces. | `CombatActor`,`Combat*Appearance`,`CombatZombieBottleDestruction` | Current |
| Warehouse | Session/seed;fish→Docker/clear;gate/ammo;L3broken/A*;3hawthorn:autoUse/return,+HP/40intox/ghost4s. | `Warehouse*`,`CombatTestRoot` | Current |
| Body destruction | Enemies:sever/crawl;warehouse:hold/decay/tail;hero wounds;passable remains/rig feet. | `CombatBodyDestruction`,`CombatCorpseTraversal`,`WarehouseCorpseDecay` | Current |
| New Game/Debug | Loading bar through staged Home/first frame; stopped12:00 until E. Debug time/starts/tools. | `StartMenu{Root,Model}`, `NewGameStartService`, `SceneTransitionService`, `AreaLoadingRoot` | Current |
| Bar hours and crowd | Daily12-02/3-8; warning/grace/safe exit; accepted service and physical cleanup finish. | `BarBusinessHours`, `BarCrowdController`, `BarClosingController` | Current |
| Russian vocalizer | Twelve M/F styles; menu A/B/free/window; shared phonetic text/face timing, bounded workers/PCM. | `RussianVocalizer{Lab,Synthesis,Voice,Plan}`, `RussianSpeechPhonetics` | Current |
| Free karaoke | LMB/mouse, drunk DSP drift; silent hall gaze, dynamic hero/guest shots, rare WC; cleanup. | `BarKaraoke*`, `KaraokeVoice{,Output}`, `RussianVocalizerSustainOutput` | Current |
| Calm Home opening | Five eased views/noon→E wake; physical foley/breath, seated return/idle→fresh E stand. | `HomeOpening{Controller,CameraSequence}`, `HomeSleepController` | Current |
| Session clock and day/night rules | Persistent 96-minute day at ×1; four real seconds per game minute. Intoxication preserves its rate. | `GameTimeState`, `GameTimeRuntime` | Current |
| World time and pause ownership | Intoxication/debug factors compose; pauses freeze world/calendar. Debug speed preserves the physics step. | `GameTimeScale{State,Runtime}`, `PauseMenuController` | Current |
| Session day/time displays | Inventory/day announcements follow calendar; final7 clock display is an exception. | `HomeAlarmClock`, `InventoryView` | Current |
| Pause / cutscene debug | Pause/replay/timecode;safe hoist→pistol∞reserve/reload→Crane rises/attacks. | `PauseMenu*`,`Cutscene*`,`WarehouseDoorAmbush*` | Current |
| Hero inventory | HP state;equip/fill;LMB1/held sips;close→stow;empty→eye look/2shakes/toss. | `Inventory*`,`GameSessionState`,`PlayerVodkaBottleController` | Current |
| World item pickup and its found screen | Shared lit 3D turn/owned focus;one take;isolated lights;layers restored. | `WorldItem{PickupPlan,Pickup,PickupModel,FoundScreen,FoundView,InspectionPresenter}` | Current |
| Hunger, stress, fatigue and consumption | Needs follow the pause-aware calendar; consumption/rest commit relief once. Gap: needs apply no gameplay debuff. | `PlayerNeedsProgressionState`, `PlayerNeedsRules` | Partial |
| Inventory-backed target interactions | Shared item-backed target menus commit consumption once and refund failed startup. | `InventoryItemRequirement`, `InventoryTargetInteraction{Definition,Model,Controller}` | Current |
| Quest log and journal | Day one opens the mother's house; the full-screen journal lists ticked boxes over one description panel. | `Quest{Types,LogState}`, `GameDaySchedule`, `Journal{MenuModel,View}` | Current |
| New-quest notice | Blinking notebook at the screen's top-right edge; the key shows until first open. | `JournalNoticeView`, `GameSessionState.HasUnreadQuests` | Current |
| Stairwell quest descent gate | The active day-two cat quest gates descent and guides the hero back to the landing before restoring input. | `StairwellQuestDescentBlocker`, `PlayerMotor.MoveTowardsInteractionPose` | Current |
| City blueprint/roads | Two districts+park;144 IDs/−29.2% land; fronts/courts share ground/navigation. | `City{Layout,SpatialPlan,RoadGeometryPlan,CourtyardBlockPlanner}`, `BuildingLot` | Current |
| Coastal utilities | City/Home share facade feeds clear of bays and roofs. | `CityCoastalInfrastructure*`, `CityElectrical*` | Current |
| City elevation/stairs | Shared heights align stairs, ground and collision. | `CityElevation*`, `CityTerrainSurfacePlan` | Current |
| Terraced City | Yards/steps;loft/birds;keeper chores/commute/gate/speech/cry;upper phone. | `CityHillside*`,`CityTerrace*`,`CityPawnshopDescent*`,`CityPigeon*` | Current |
| City mountain boundary | West/south fractured cliffs, graded talus, river cave and open portal. | `CityMountain{Boundary*,Talus*}`, `CityRockface*`, `CityFringeYard*` | Current |
| Eastern mainland view | Closed post; shared road/traffic datum; skyline/glow fade beyond checkpoint. One fixed mast. | `CityEastExit*`, `CityEastRoadProfile`, `CityEastDistance*` | Current |
| City litter | 36 Blender variants off roads; dense east/no near repeats; solids block; finite shared palette per root. | `CityLitterCatalog`, `CityLitter{Plan,Planner,WorldBuilder,Instancer}`, `CityEastLitter*` | Current |
| Eastern checkpoint guards | Distinct guards alternate physical patrols; shared pair/E bubbles, passive shoulder rifles, pause/cleanup. | `CityEastGuard{Duty,Plan,Controller}`, `EastGuardActor` | Current |
| South tunnel travel stub | Walking triggers the authored refusal/return. Gap: no pedestrian transition to Mountain Road. | `CityTunnelTravel{Plan,Planner,Controller,CrossingModel}`, `InteractionPromptView` | Partial |
| Mountain Road area | Tunnel/hairpins/bridge/brink/cableway; forest 48m chunks. | `MountainRoad*`, `MountainRoadBridge*` | Current |
| Mountain crafted slots | Shared imported drive/cabin/support and structural profiles retain bounds/pivots/collision. | `MountainCablewayCraftedGeometry`, `MountainRoadStructureDetails` | Current |
| Crown/slide pilots | One forest RGB chunk/one authored slide normal field; A/B reviewed. | `MountainRoadCrownGradientPilot`, `pistol_normal_pilot.py` | Current |
| Village above the cableway | Snow bowl, abandoned settlement, ski base; mother's house. Gap: dinner/news/Cat and chapel interior. | `AlpineVillage{ExpansionPlan,SnowPlan,SnowTreading,PeripheralStorm*,WorldBuilder}` | Partial |
| Mother's house interior | Wing/bath, two bedrooms/stairs/five shots; unique4K+2K/aux1K; room effects/depth repair/two cubes; passive mirror. | `MothersHouse{Interior,Lighting,Reflection,Surface}*` | Current |
| The cableway carries | Both arrivals approach aboard, dock and exit with a working safety bar; ride leases release on completion/teardown. | `AlpineCableway{RidePlan,CabinSeat,RideController,RideFactory}` | Current |
| City river and embankments | Two clear banks, three bridges/four landings; bounded authored fittings retain physical routes and shared light. | `CityRiver{Plan,DressingPlan,DressingWorldBuilder,WorldBuilder}` | Current |
| Yard wheelchair rider | One staged yard rider follows its reserved circuit, with authored wheelchair motion and existing local lighting. | `YardWheelchair{Motion,Plan,Presentation,Actor,Factory,Provider}` | Current |
| Home-yard composition | Original house gap retains tree, wheelchair circuit and fixed cold wall spotlight after Bar relocation. | `CityOpenAreaDecoration{Plan,Planner}`, `CityOpenAreaWorldBuilder` | Current |
| Cemetery precinct | Deterministic graves, paths, fences and practical lamps dress the separate eastern cemetery. | `CityCemetery{Plan,Planner,WorldBuilder,SurfaceAppearance}`, `CityBenchSitPlan.CreateAll` | Current |
| Residential street fair | Four staffed stalls, three adult visitors and three children; shared seats/actions and clear paths. | `CityFair{Plan,World,Adults,Children,Interaction}` | Current |
| Church precinct and interior | Metric shell/openings match exterior; crafted garden, bounded bounce/air/cube. | `CityChurch*`, `ChurchInterior*`, `ChurchGarden*` | Current |
| Residential courtyard pockets | Plan-owned residential courtyards preserve walkable approaches and bounded authored dressing. | `CityCourtyardPocket{Planner,Geometry}`, `CityCourtyardResident{Plan,Factory,Presentation}` | Current |
| Residential courts | Shared ground/spine/green edges/wall groups; seats/carrier/Home. | `CityResidentialCourtyard*` | Current |
| City yards | Only west stone terraces add a mason's cart; other fringe yards retain their existing service-belt infrastructure. | `CityFringeYard*`, `CityFringeYardLifePlanner` | Current |
| District public places | Four semantic POIs/landmarks survive across two districts; validated public approaches and local practicals. | `CityDistrictPointOfInterest{Plan,Planner,WorldBuilder}`, `CityPointOfInterestSurfaceAppearance` | Current |
| Drying yard babushkas | Staged drying-yard residents share authored domestic actions and local speech/prop ownership. | `DryingYardBabushka{Provider,Plan,Presentation,Factory}`, `CityPedestrianHandProps` | Current |
| Cannery and fish deliveries | FIFO/weighing gate loading; driver lunches on bench; shared speech/clear routes. Gap: empty-tare return. | `CityCannery*`, `CityFishSupply{Cycle,Session}`, `WorldDistancePresentation` | Partial |
| Cannery woman | Seamer/painted face/actions; hair contacts 8→2 afar with continuous time; camera/near keeps full; speech/pause. | `CanneryWoman{AssetProvider,Presentation,Hair,Wardrobe}`, `CanneryWomanAssetSetup` | Current |
| Cannery receiver | Athletic 1.96 m actor/ski hat/3D glasses/painted face; own actions, shared contacts/speech/pause. | `CanneryReceiver{AssetProvider,Presentation}`, `CanneryReceiverAssetSetup` | Current |
| Authored NPC outfits | Fixed outfits and modular items/slots/coverage/presets; editor selection. Gameplay clothing UI/save Deferred. | `NpcWardrobe`, `CanneryWomanWardrobe`, `NpcWardrobeEditor` | Current |
| Global default NPCs | Stable men/women, skinned clothes/hair, distinct visible looks. | `DefaultNpc{Catalog,Population,Factory,Appearance,BodyShape}` | Current |
| Hand poses | Hero/NPC authored cylindrical grip; independent weights, neutral release and surface alignment. | `NpcHandPose`, `CityCanneryController.DriverHands` | Current |
| Cemetery mourner | The grave-side mourner uses her own authored presence, gestures and localized response. | `CemeteryMourner{Provider,Plan,Timeline,Presentation,Factory}`, `CityCemeteryMournerController` | Current |
| Cemetery watchman and gate lodge | The watchman and lodge provide the authored grave-work offer and payment interaction. | `CemeteryWatchman{Provider,Plan,Quips,Interaction,Presentation,Factory}`, `CityCemeteryPlanner.AddLodge` | Current |
| Cemetery gravedigging | Up to three open jobs; grave acts/epitaphs persist per plot, with payment committed through the watchman. | `CemeteryGravedigging{Plan,Controller,Register}`, `CemeteryGraveWork{Stage,Ledger}` | Current |
| Cemetery ravens | A sparse raven pair appears around the first sealed grave, flushes near the hero and returns afterward. | `CemeteryRaven{Provider,RigAnchors,Factory,Plan,PoseRules,IdleModel,HeadModel,FlightModel,DirectorModel,Actor}` | Current |
| The spring and its brook | Village brook and Mountain Road culvert water are implemented. Gap: the dark mountain reaches await acceptance. | `AlpineVillageBrook{Plan,Planner,Builder}`, `AlpineSpringWaterResources` | Partial |
| Sitting on the mother's sofa | The sofa reuses shared bench seating with front-only approach, measured cushion contact and owned shadow cleanup. | `MothersHouseSofaSeatPlanner`, `CityBenchSit{Plan,Interaction,WorldBuilder}` | Current |
| The mother in her chair | She rocks on measured runner contact, greets him at the door and speaks from one shuffled bag that `E` shares. | `MothersHouseMother{Presentation,Factory,Interaction,Quips,SpeechSession,SpeechController}`, `NpcHeroAttentionLook` | Current |
| Outdoor raven roosts | Seeded pairs occupy bounded open-world perches, flush locally and obey area exclusion/vehicle gates. | `RavenRoost{Plan,Controller}`, `{City,MountainRoad,AlpineVillage}RavenRoostPlanner` | Current |
| Seacoast precinct | Port reached by truck road/shoulders; internal dock walks and station/east-shore approaches retained. | `CitySeacoast*`, `CityPort*`, `CitySeaResources` | Current |
| Port social life | Canopy breaks, paired city/weather talk in nonrepeating rounds; driver speaks only to docker. | `CityPortCrew*`, `CityPortConversation*`, `CityPortLighting` | Current |
| Port cold store | Braced canopy/rigging rack, supported cooling and three warm lamps retain cargo lanes; fan follows pause/distance. | `build-city-port-3d-model.py`, `CityPortLighting`, `CityPortSound` | Current |
| Port foreman | Carrot custody retained; voluntary two-speaker E dialogue, no work/pay. | `CityPortForeman*`, `CityPortConversation*` | Current |
| NPC dialogue | Graph, approach/two shots/cancel; foreman. Translucent choices; shared inspection camera. | `DialogueGraph`, `DialogueSessionController`, `ContextualCameraDirector` | Current |
| Dialogue faces | Expressive hero/foreman mouths, brows/blink follow reveal/pause; soil and snack state retained. | `SpeechFaceAnimation`, `SpeechFaceAtlasPresenter` | Current |
| Sea water | Shared water drive owns sea swell, foam and uneven shore swash. | `CitySeaResources`, `CityWaterResources` | Current |
| Beach sand | Deterministic shallow relief and compressible foot trails over a coarser fixed collider. | `CityBeachSandPlan`, `CitySandTreading` | Current |
| Lighthouse island | One distant fog-framed island landmark owns its silhouette, beacon/beam and authored sightline. | `CityLighthouseIsland{Plan,Planner,MeshFactory,WorldBuilder,Resources}` | Current |
| Seacoast fisherman | Close two-hand grip, raised PNG face/beard volume; clip-driven ember/shared exhale. | `SeacoastFisherman*`, `HomeBalconySmokingExhaleEffect` | Current |
| Park chess gazebo | Shared deck, entrance, two seated men, both hero docks, beam pendants and roof rain shelter. | `CityParkGazebo*`, `CityParkFurniture*`, `ParkChessPlayer*` | Current |
| Park chess-set men | Imported chess and draught pieces retain the measured table and opening-layout contracts. | `CityChessBoardGeometry`, `CityChessSetPlan` | Current |
| Park board games | Both boards are playable. | `ChessRules`, `ChessEngine` | Current |
| Park chess-set quarrel | The two men hate each other on the only grounds available: one plays chess and despises draughts, the other the reverse. | `CityParkQuarrelController`, `ParkQuarrelTimeline` | Current |
| City world builder | One validated plan feeds shared staged/synchronous construction; temporary scene, input and audio ownership is explicit. | `CityWorldBuilder`, `CityTerrainSurfaceWorldBuilder` | Current |
| City surfaces | Nine areas/Home: scoped maps/bar UV0+1; ground slots/paint support. | `CitySurfaceLibrary`, `CityGroundSurfaceSystem` | Current |
| Central Park surfaces | The divided park uses shared gravel, paths and bridge geometry from its pure surface plan. | `CityParkSurfaceAppearance`, `CityWorldBuilder` | Current |
| Pedestrian personal space | Above alcohol `60`: guarding palm; above `80`: close shove. | `CityPedestrianPersonalSpace{Rules,Controller}`, `PlayerMotor` | Current |
| Pedestrian street insults | Insult responses need the authored proximity/facing gates; the twenty-line bag deals once a round, surviving reloads. | `CityPedestrianInsult{Rules,Lines,Walk,SessionState,Controller}`, `CityPedestrianPersonalSpaceController` | Current |
| City/Home pedestrians | 192 itineraries, ≤36 street bodies + ≤12 ambient. Street target60/40. Home5/2. | `CityResidentJourneyDirector`, `CityPedestrianDirector`, `CityAmbientPedestrians` | Current |
| NPC Human V2 anatomy, appearance and visibility | Shared rigs; joint fields/volume and shaped hands/clothes retain UVs. | `NpcHumanV2AssetSetup`, `NpcDesignAppearanceCatalog`, `CharacterJointDeformation` | Current |
| City district buses | Two/ring,shared transfers. Gap:no Home service/live map buses. | `City{District,Hillside}BusPlanner`, `CityBus{Director,Actor,Factory}` | Partial |
| Bus stop lighting | City/Home perimeter strip/apron;fixed daylight;bounded soft-shadow fades. | `CityBusStopLighting`, `CityNightAtmosphere` | Current |
| Pedestrian bench rests | Eligible walkers reserve, sit, rest, stand and resume their route. | `CityBenchRest{Plan,Planner}`, `CityBenchNpcRestController` | Current |
| City bus passengers | Two doors/10seats/3standing;remote. Gap:fares/live tracking. | `CityBusRide{Plan,Controller}`, `CityBusStopWait{Plan,Planner}` | Partial |
| City visual variants | Deterministic variants share assets and simple semantic colliders. | `CityDecoration{Descriptor,Plan,Planner,Validator,WorldBuilder}`, `CityDecorationCollisionCatalog` | Current |
| Road-edge fences | Planned fences leave road and waterside openings clear. | `RoadFence{Planner,Plan,WorldBuilder}`, `CitySurfaceDescriptor` | Current |
| Shops | 3 kiosks; Ridge talk; shop7. Shared browser/3D receipt. | `RidgeKiosk*`, `IWorldShelfMenu`, `SupermarketShelfShop*` | Current |
| Ridge kiosk court | Paved path/rails;3 tables,6 men,drinks/bar pairs,clear23. | `RidgeKioskCourt*`, `CityResident*` | Current |
| Pawnshop | Island pavilion/secure clerk. Gap:trade stubs have no transactions. | `CityPawnshop*`, `PawnshopInterior*`, `PawnshopTradeController` | Partial |
| Ridge workshops | Cobbler11–20:30/home;glass08–23/switch/grille. | `CityCobbler*`,`CityGlassReturn*` | Current |

| Supermarket cashier | Wall checkout/decor snacks;anchored palms/light;ordinary cashier,bulging 3D face/shared rig. | `SupermarketCashier{Provider,AssetRegistry,Factory,Presentation,Actor,SurveillanceState,BlinkState,Interaction}` | Current |
| Supermarket CCTV corner cameras | Four authored corner cameras follow the hero without gameplay surveillance. | `SupermarketInteriorAssetRegistry`, `SupermarketSecurityCamera{,WorldBuilder}` | Current |
| Supermarket fluorescent atmosphere | Visible fixtures own the store's restrained light, flicker and sound within explicit shared-resource budgets. | `SupermarketInteriorAtmosphere`, `SupermarketFluorescentFlicker` | Current |
| City day/night presentation | Uniform fog/48m;night dims sky/fill. Booths/pawnshop benches retain fixed light/day floor. | `CityNightAtmosphere`, `CityAltitudeVisibility`, `CityDayNightController`, `HomeBalconyExteriorAtmosphere` | Current |
| Exterior weather | Rain contacts, wet film, snow and audio follow the clock. Gap: weather-driven grade. | `GameWeatherRules`, `CityWeatherController`, `CityRainContactField` | Partial |
| Exterior cloud ceiling | A passive shared-density shell supplies a camera-relative cloud ceiling with bounded horizon coverage. | `ExteriorCloud{AssetMetadata,Profile,MotionRules,Resources,Field,CaptureCamera}`, `ExteriorCloud.shader` | Current |
| Runtime cloth rags | Visible cloth uses shared bounded wind response and explicit attachment constraints. | `ClothPanelFactory`, `CityClothWindRegistry` | Current |
| City wind | Weather/cloth, height-driven Ridge crowns/grass and fixed-end power spans. | `CityWindField`, `CityWindDressing*`, `CityCoastalInfrastructureWorldBuilder` | Current |
| City vegetation | City/Home grass off paths, Ridge trees and shrubs. | `CityGrass{Plan,Surface,Field}`, `CityRidgeVegetationPlan` | Current |
| Scene and place music | Lodge gate/radio suppression;warehouse pitch.8/loop. Gap: cemetery/church slots empty. | `MusicMix`, `SceneMusicPlayer`, `CombatWarehouseMusicPlayer` | Partial |
| Common audio mix | Sharedmix;warehouse 3D clicks/whispers:.8/LP2800/World;dryUI;intoxication. | `GameAudioMixer`,`CombatWarehouseAmbiencePlayer` | Current |
| Port and cannery audio | Local machinery/fan/contact; arrival horn ×2 with longer echo/reverb. Pause/distance/seek own playback. | `CityWorkAudio`, `CityPortSound`, `CityCanneryController.{Sound,ProcessSound}` | Current |
| Intoxication sound perception | Separate eased audio persists across scenes; speech/music→0.25× over VHS, caption scatter. Sober restores. | `IntoxicationPerceptionRules`, `NpcSpeechVoice`, `SceneMusicPlayer`, `IntoxicationAudioDriver` | Current |
| Retro SFX and ambience | Cues/ambience share routing/cleanup; config invalidates native banks for bounded lazy recovery. | `RetroSfx`, `RetroAudioService` | Current |
| Footstep surfaces | Collider triangles/stamps/overlays select hero/NPC cues; three variants each. | `FootstepGround`, `HeroFootstepGround`, `NpcFootstepDirector` | Current |
| Causal City soundscape | Visible local sources own City sound; bounded schedules and shared routing control the mix. | `CitySound{SourceDescriptor,scapePlan,scapePlanner,SchedulePlanner,Occlusion}` | Current |
| Home alarm clock | Prepared12:00/legacy05:59→06:00; final7 impossible readouts; actual time stays. | `HomeAlarmClock{Plan,Builder,Synthesis}`, `HomeAlarmClock` | Current |
| Road, park and ground navigation | Masks follow physical bounds; exclusions retain corner overlap, safe eastern street margins stay open. | `RoadWalkableArea`, `CityGroundTraversalPlan{,ner}`, `CityRoadGroundBoundaryPlanner` | Current |
| Player motor | Tank/aim walk;world-space air inertia/limited steering;ground/snow/context limits. | `PlayerMotor{,.TargetMovement,.Snow}`, `PlayerDirectionalInput` | Current |
| Third-person chase camera | Orbit/RMB;.5m right/1.9m back/aim1.4;physical body follows;collision/dolly/fixed-modal. | `PlayerCameraFollow`, `IntoxicationDollyZoomModel` | Current |
| Home camera | Entry/sofa/kitchen zones + bathroom/balcony; pan/hold and contextual ownership. | `HomeFixedCameraController`, `FixedCameraFocus` | Current |
| Home player visibility | Grouped occluder dither and fixed-shot rules keep the hero visible without changing collision. | `HomeOcclusion{Registry,Resolver}`, `HomePlayerOcclusionController` | Current |
| Modular 3D hero presentation | V2/joint volume; seven drink-dose body/hair/wear forms; mirror/FP/portrait copies. | `Player3D*`, `PlayerCorruptionAppearance`, `HeroCorruptionRules` | Current |
| Hero wardrobe | Atomic slot replacement and body coverage; one authored outfit. Gap: extra outfits, UI and persistence. | `PlayerWardrobe`, `Player3DBathingAppearance` | Partial |
| Hero hair | Recorded bones; pose/gear guards; live wind/falls/pause/mirror. | `PlayerHair`, `SecondaryMotionBakeRuntime` | Current |
| Hero jacket cloth | Gameplay:rig/folds/hand-opening,0physics;Editor authoring kept. | `PlayerJacketCloth*`, `PlayerClothPhysicsPolicy` | Current |
| Wearable scarf | Hero/mouth/mirror;5s area height;Combat:rig,0physics. Owned ray buffers/pose-mask leases. | `PlayerScarf*`, `PlayerClothPhysicsPolicy`, `GameSessionState` | Current |
| Alpine Village cold hero | Hug/rubs persist running; scarf halves shiver. Warmth stops cold/breath. | `Player3DCharacterPresentation.Cold`, `PlayerCold{PresentationModel,BreathEffect}` | Current |
| Alpine frost presentation | Session frost; scarf halves growth, full frost stops cues; warm zones thaw. | `AlpineColdExposure{Model,Driver}`, `AlpineColdFrostPass`, `AlpineFrostAudio` | Current |
| Silent Hill attention | Layered gaze reacts to nearby authored targets within rig limits and yields to contextual ownership. | `PlayerAttention{Rules,Controller,Magnet}`, `IntoxicationHeadModel` | Current |
| Continuous 3D player interactions | Positioned entry/exit/contact continuity; optional exit hold/tail timing; owned presentation/input cleanup. | `PlayerAnimatedInteraction{Timeline,Controller}`, `PlayerDoorAction{Plan,Controller,Target}` | Current |
| Sofa sleep and wake | Two hand-supported pelvis steps with seated stops; domed pillow dents and recovers. | `HomeBedInteraction{,Plan}`, `PlayerAnimatedInteractionPelvisPath` | Current |
| City bench and park game-table seats | Shared sit/rest/stand; owning seat retains E/prompt and nested handlers through cleanup. | `CityParkBenchPlanner`, `CityBenchSit{Plan,WorldBuilder}` | Current |
| Ferryman car and passenger seat | Doors, attachment, camera, audio, area arrival; callouts outline radio knobs and glovebox handle. | `LastRouteCar{Plan,Factory,Doors,Suspension}`, `LastRouteCarDashboard`, `LastRouteRadioAffordance` | Current |
| Ferryman radio and road speech | Three stations, playheads and cabin reactions persist; passenger exit switches the radio off. | `LastRouteRadioMusicPlayer`, `LastRouteRideSpeech{Session,View}` | Current |
| Ferryman coin | One authored coin supplies the toss and contained glovebox pile; shared material, no pickups. | `LastRouteCoinAsset`, `LastRouteGloveboxCoins` | Current |
| The last route | Journey stages coordinate Ferryman dialogue, car legs, blackouts and arrivals; the ride is refused when very drunk. | `LastRouteRideController`, `LastRouteFerrymanRideRules`, `LastRouteCar{DrivePath,DriveModel,Driver,GiveWay*}` | Current |
| Home sofa sleep | Supported hands/body→face; stable wake/foley/breath; forms→7, seated return→E stand. | `HomeBedInteraction`, `HomeSleep{Controller,AudioCue}` | Current |
| Home balcony | Mouth-contact smoke/right orbit; E folding door; final7 seal waits if outside. | `HomeBalconySmoking*`, `HomeBalconyWorldBuilder`, `HomeBalcony{FoldingDoorInteraction,CorruptionBarrier}` | Current |
| Shared refrigerator | Hand-free Home/warehouse;entry/chest aim;atomic Take/Back. Gap: `Use` hidden/unimplemented. | `HomeRefrigerator*`, `RefrigeratorInteractionContext`, `WorldItemInspectionPresenter` | Partial |
| Player shadows | Real mesh shadows and an analytic contact patch follow hero/contextual visibility ownership. | `Player3DCharacterPresentation`, `Player3DRagdollController` | Current |
| Interaction/UI | Prompts/input; nearest family;locker Take > open > close > distance; look/debug local. | `PlayerInteractor`, `IPrioritizedInteractable`, `InteractionPromptView` | Current |
| Spoken text | Translucent shared bubbles/RU vocalizer incl. E/hero; EN clicks, silent bottom; bounded jobs/pause/cleanup. | `SpeechDelivery`, `NpcSpeechBubbleView`, `NpcSpeechVoice` | Current |
| NPC role labels | Eleven roles (east guards share one), excluding village/Mother; 6–4 m, depth occlusion, speech/modal priority. | `NpcNameplateTarget`, `NpcNameplateContext`, `NpcNameplatePolicy` | Current |
| Debug controls | F4 flight; F9 weather/tools; F1–F3 speed. F9/F8 scene/XYZ flag. | `DebugFreeCamera`, `MinigameDebugWindow`, `DebugTimeControls`, `PlayerCoordinatesHud` | Current |
| Structured session diagnostics | 20MiB/adaptive samples/end reserve/32-action tail;CPU/cloth/ammo/carry/input/F8. | `Runtime/Diagnostics`, `DuelJournal` | Current |
| Bar activity flavour | Legacy activity identity still selects bar flavour; the removed sprite minigames remain absent. | `BarActivityKind`, `BarActivityAssignment` | Current |
| Area map UI | Fullscreen;scrollable place list→one plaque/line/square;large hero arrow;travel/XYZ. | `CityMap{Controller,View,AreaController,AreaView,MountainRoadOverlay,AlpineVillageOverlay}` | Current |
| Area map art | City charcoal:Ridge sites/stairs. Village brochure;Road/other seeds schematic. | `{City,AlpineVillage}MapArtResources`, `CityMapArtReferenceExport`, `CityMapIllustratedLabels` | Current |
| Map XYZ inspection | Map points, fair/post/docks/arch/church-door places included, expose world coordinates and validated teleport targets. | `CityMapTeleport{Lattice,Grounds}`, `CityMapPointDescriptor` | Current |
| Map arrival ground | Area-owned ground; City lot/XYZ arrivals re-sample land Y and move occupied markers to a clear street. | `ICityMapTeleportGround`, `CityMap{City,MountainRoad}TeleportGround` | Current |
| Scene transition | Guarded loads/cleanup; closed entrance leaves stay solid and door-action docks clear. | `PlayerDoorAction{Plan,Controller,Target}`, `ApartmentEntranceDoor`, `CityGameRoot` | Current |
| Area loading transition | One directed illustration and bottom bar; 20% load / 80% construction, owned until the destination is ready. | `AreaTravelService`, `AreaLoading{Root,ArtCatalog}` | Current |
| Door transition presentation | Deterministic1.6s/unscaled door shot:black void,warm entry/cold exit. | `DoorTransition{Root,Timeline,Direction}`, `RuntimeSceneSetup` | Current |
| Session state / checkpoint | Latest scene-entry RAM snapshot; pause restores progress/world. New Game/exit clears; no disk save. | `GameSessionState`, `SessionCheckpointService`, `CheckpointWorldState` | Current |
| Bar drink service | Menu/repeat/receipt;beer/wine three portions,full wine;guest FIFO/look/nod. | `BarDrink*`, `BarRepeatGesture`, `BarBartenderServiceQueue` | Current |
| Intoxication stages and presentation | Vodka+24;reversible FX/body;recovery4.5–18s/point,higher→slower. | `IntoxicationStageRules`,`IntoxicationStatusController` | Current |
| Continuous balance and falling | Stair falls;unsupported pose releases;combat caps;seated/all-fours rise. | `PlayerBalance{Model,Controller}`, `StairWalkableSurface`, `PlayerRiseModel` | Current |
| Hero drunk muttering | A seeded stage-dependent clock drives short authored mutters, slurring and an owned speech bubble. | `HeroMutterModel`, `HeroMutterLines` | Current |
| Hero nausea bouts | High intoxication drives the walking gauge, cancellation gates, vomiting relief and session mouth-soiling. | `HeroNauseaClock`, `HeroNauseaGaugeModel` | Current |
| Bar interior and exterior | Hall/worn WC; solid shared door, exclusive hero/guest access and masked broken mirror. | `BarInteriorLayout*`, `BarWashroom*`, `InteriorHingedDoorInteraction` | Current |
| Player home interior | 12×8×2.9m; stove meets wall; sofa/kitchen/locked room/shared sit. | `HomeInteriorModelLibrary`, `HomeDiningSeatPlan` | Current |
| Apartment calendar appearance | Days1–7 neglect; heavier7/exact debug restore; sleep view7+hero7. | `HomeApartmentDressing`, `HomeApartmentDay{Rules,Controller}` | Current |
| Player-home stairwell | Three puzzles/pickup shots/equip;crowbar wakes crowd after return.Safe room3/retry. | `StairwellLabyrinth*`, `StairwellPickup*`, `StairwellEncounter*` | Current |
| City facade presentation | The legacy 4×4 path remains only for clipped Home crossings. | `CityFacade{Grid,Appearance}`, `CityBuilding{SurfaceAppearance,WindowSlotAppearance}` | Current |
| City windows | Street-front primary/timed extra;paired local wall wash. | `CityWindow{Appearance,Lighting}`, `CityBuildingWindowSlotAppearance` | Current |
| Stairwell surface presentation | Cached textures and material property blocks apply measured stairwell surfaces without instance materials. | `StairwellSurfaceAppearance`, `SurfaceAppearanceCore` | Current |
| Exterior ground response | Indexed MPBs share response maps/road masks/snow edges; ridge haze retained. | `GroundSurface{Appearance,Coordinates}`, `GroundSurfaceLit`, `*SurfaceAppearance` | Current |
| Home/Stairwell surfaces | Original micro sheets/unique macro charts/shared variants; exact earlier-state restore. | `HomeSurfaceAppearance`, `HomeAuthoredVisualFactory`, `SurfaceDamageLibrary` | Current |
| Stairwell cat | Ordinary talk/feed; statue: silent camera cut/hero hidden only close-up; quest stays. | `StairwellCat*`, `StairwellCatInteraction` | Current |
| Home exterior context | Same-seed64m city around Home, rear included; no collision. Balcony gates residents/atmosphere. | `HomeExteriorContext{Plan,Planner}`, `HomeExteriorViewBuilder`, `CityHomeGroundWorldBuilder` | Current |
| Player home atmosphere | Clock drives six local lights; Balcony borrows/restores City. | `HomeInteriorAtmosphere`, `HomeDayNightController` | Current |
| Mirror brushing | First-person X/Y brushing ends with a three-second close teeth inspection, then spit and faucet closure. | `HomeTeethBrushing*`, `HomeBrushingFirstPersonView`, `HomeSinkFaucet` | Current |
| Toilet choice and seated action | Shared real-hand lid actions; large branch seats, inspects and flushes. Dressing overlaps camera return; no relief. | `HomeToiletChoiceInteraction`, `HomeToilet{PlungeInteraction,SeatedTimeline,ActorPresentation}` | Current |
| Toilet body and bowl contact | Original-rig garment/anatomy module; emitted solid, once-only water contact and isolated local bowl fill. | `HomeToilet{SeatedAppearance,BowelEffect,BowlLighting}` | Current |
| Toilet solid in water | Fixed-step buoyancy, drift and rotation; flush forces drain inside the bowl, with camera clearance and pause/restore. | `HomeToiletFloatingBody`, `HomeToiletBowelEffect` | Current |
| Toilet water presentation | Two-sided water, camera optics, world-bus absorption and entry/underwater sounds; UI bypasses the scoped mix. | `HomeToiletUnderwater{Effect,Pass}`, `HomeToiletWater{Resources,AudioResources}` | Current |
| Toilet flush return | One-second whirlpool and smooth return share two forward camera turns; water/audio restore on exit. | `HomeToiletFlushVortex`, `HomeToilet{BowelEffect,UnderwaterEffect}` | Current |
| First-person toilet | Small option aims ballistic urine for `6 s` plus `2 s` shaking; wet marks persist across Home loads. | `HomeToilet{Interaction,FirstPersonView,AnatomyDynamics}`, `HomeUrine{Effect,Residue}` | Current |
| Home bathroom mirror | Geometric room/hero behind opening; packaged shared glass retains player transparency. | `HomeBathroomMirrorWorld`, `HomeBathroomMirror{OpeningBuilder,Resources}`, `HomeMirror{SubtreeClone,Plane}` | Current |
| Home bathroom scenes | The shower hero exits fully offscreen before redressing; cleanup restores rig and input. | `HomeBathroomSceneInteraction`, `HomeShower{Interaction,CameraPath}`, `HomeTeethBrushingInteraction` | Current |
| Interactive shower washing | Mouse look/centre aim; held LMB follows skin aim without release. E soap/Q exit; any region fills gauge. | `HomeShower{WashingInteraction,SoapPose,WashingProgress}` | Current |
| Shower water presentation | Visible jets follow the tilted nozzle; residual drops and ripples share a tray landing point, with local sound. | `HomeShower{Interaction,TrayWater}`, `HomeSoundscape` | Current |
| Bar patrons and bartender | WC retains guest/place/drink; shared-aisle collection has bounded contact retries and forward knees. | `BarCrowd*`, `BarWashroomSchedule`, `BarPatron*`, `BarAisleTraffic`, `BarBartender*` | Current |
| Bar speech | Counter/two booth pairs + bartender; ≤4 lines; solitary tables silent. A→B/no repeats. | `BarConversation{Catalog,Schedule,Controller}`, `BarSpeechVariantDeck` | Current |
| Bar cinematic atmosphere | 8 lights/3 shadows; 4 bounded bounces/2×256 cubes; Neutral. | `Bar{InteriorAtmosphere,LightingPresentation,MusicPlayer}` | Current |
| Bar realtime GI pilot | Native gate passed; fixture-only. Gap: gameplay activation/player budget. | `BarSurfaceCacheGi{Pilot,RendererFeature}`, `SurfaceCacheGiPreflight` | Partial |
| Material/water pilots | Counter/brass roughness; fountain foam-free basin/mips; A/B reviewed. | `BarCounterMaterialResponse`, `CityFountainWaterResources` | Current |
| Automated tests | Focused EditMode/PlayMode contracts share infrastructure; complete suites need an explicit regression request. | `Assets/Tests/{EditMode,PlayMode,Infrastructure}` | Current |
| Automated test audio guard | Silence the global listener for every EditMode and PlayMode run without pausing sources, then restore volume. | `BarPromenade.TestSupport`, `AutomaticTestAudioMute` | Current |

## Primary intended flow

Behavior: [current world](current-world.md); decisions: [architecture](architecture-notes.md).
Earlier rows: [superseded snapshot](archive/systems-map-2026-09-06.md).

```text
MainMenu (index 0)
  -> New Game -> Home1/stopped12:00 -> quiet shots -> E wake/seated return -> E stand
  -> Debug -> time07:40/twelve places OR combat/lab/vocalizer -> start/back
Legacy API only: 05:59 -> Wake/Quit -> 06:00 -> Home -> City

blueprint + seed -> validated plans -> surfaces/navigation/map/night/decor/fences
  -> pedestrian population; district rings -> platforms -> line actors -> board/ride/alight/transfer
clock -> day announcement/needs/day-night; weather -> rain/lightning/thunder
map/travel -> AreaLoading -> destination Single (20%) -> staged composition (80%)
  -> ready -> restore audio/time/input

11 gameplay roots -> PlayerFactory -> Resources/Player/Player3DV2.prefab
  -> locomotion/balance/ragdoll/rise/contextual actions/inventory portrait
interaction -> SceneTransitionService -> DoorTransition -> destination
  -> Bar: seated service; Supermarket: repeatable stock/shared receipt
  -> Stairwell: cat; Home: sleep/balcony/fridge/fixed views/exterior slice
results -> GameSessionState -> survive scene loads, not process exit
boundaries -> rotating NDJSON diagnostics; scene -> scoped audio
URP -> native effects/SMAA -> crisp UI
```
