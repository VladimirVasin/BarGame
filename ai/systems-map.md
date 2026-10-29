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
| IMGUI | Bounded Input System pointer/text; Editor colours/textures preserve authored values. | `RetroUi{Input,TextField,Theme,Texture}` | Current |
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
| City ground | Typed sectors retain planes/weather/steps; 1.5m asphalt follows buildings; cemetery/sand stay local. | `CityGroundSurfaceSystem`, `CityBuildingPerimeterPlan` | Current |
| City street support | Shared stations/outer seams join asphalt, pavement and terrain; imported topology. | `CityStreetSupportPlan`, `CityTerrainStreetBoundaryPlan`, `CityStreetSurfaceWorldBuilder` | Current |
| City Blender ordinary buildings | Work8 functions; homes: real entries/paved links; passages/wings share footprints. | `CityBuilding*`, `CityResidentialEntrance*`, generators | Current |
| Residential balcony life | Bounded pooled smokers occupy authored Residential docks; Home reconstructs its own balcony-gated selection. | `City/Balcony/CityBalconySmoker*`, `CityPedestrianHandProps` | Current |
| Residential laundry | Residual L-shell; silent worker folds/carries the same cloth/basket; enamel sign/shared light/hum. | `CityLaundry{Plan,AssetProvider,Controller,WorldBuilder}`, `build-city-laundry-3d-model.py` | Current |
| Residential dog park | Residual fenced yard; three articulated dogs, two permanent owners; open entry and local voices. | `CityDogPark{Plan,AssetProvider,Controller,WorldBuilder}`, `CityDogPose`, `CityDogActivity` | Current |
| City Blender low-rise landmarks | Bar, supermarket and `player_home_exterior_v1` are complete passive semantic exteriors with inset foundations. | `CityBarFacadeWorldBuilder`, `CitySupermarketFacadeWorldBuilder` | Current |
| Unity URP foundation | 13 scenes; PC Neutral/Bloom/Vignette. | `6000.6.7f1`, `17.6.0` | Current |
| Scene presentation | Native/SMAA/4:3; gamma-correct Editor UI; City/warehouse SSR. | `Scene*Feature`, `GraphicsEffectsSettings` | Current |
| Village local lighting | Real emitters; bounded air/snow/bounce, metal and spring reflections. | `AlpineVillageLightingPresentation`, `SceneReflectionCapture` | Current |
| Road lighting | Bounded air/snow/headlights/halos/metal/bounce; station reflections. | `MountainRoad{LightingPresentation,ReflectionController,MetalAppearance}` | Current |
| Depth of field tiers | Gaussian far blur/interiors radius0.55; shared owned inspection Bokeh. | `RuntimeSceneSetup`, `DepthOfFieldSettingsBinder`, `CinematicDepthOfField` | Current |
| Runtime area composition | Ten gameplay roots; four build in frames; interior doors retain dormant City/village. | `Runtime/Core`, `Runtime/Scenes` | Current |
| Combat | Warehouse:Crane/Room/Docker≤5;Lab6/1;livehero hold/deathdrop. Gap:pierce/balance. | `CombatActor`,`CombatTestRoot` | Partial |
| Crane | Hook6m/up2m/hold5/E6/hoist-haul/bodycam/fall;home10;fatal3→regen10/kill0. | `Combat*.Crane*`,`CombatActor.Restraint` | Current |
| Authored enemies | Room:idle5s/jelly/bar/wet SFX;Lab:Imprint/LongExposure | `CombatActor`,`CombatTestRoot.Room*` | Current |
| Warehouse | Fish3:regularfoes/spawn0;gate;ammo/bays;L3broken;dust→settle;A*/capsule. | `Warehouse*`,`CombatTestRoot.Navigation` | Current |
| Body destruction | Enemies:sever/crawl;hero:wounds/no detach;passable remains/rig feet. | `CombatBodyDestruction`,`CombatCorpseTraversal`,`CombatBodyAssetSetup` | Current |
| New-game starting place | Time/12 starts; Input System, motion-only hover; closed Bar shows hours. | `StartMenu{Root,Model}`, `NewGame{LocationCatalog,StartService}` | Current |
| Bar hours and crowd | Daily12-02, hourly3-8; real entry/exit, partial pairs, occupied docks; accepted service finishes. | `BarBusinessHours`, `BarCrowdController`, `BarInteriorRoot` | Current |
| Russian vocalizer | Twelve M/F styles; menu A/B/free/window; shared phonetic text/face timing, bounded workers/PCM. | `RussianVocalizer{Lab,Synthesis,Voice,Plan}`, `RussianSpeechPhonetics` | Current |
| Free karaoke | LMB/mouse, drunk DSP drift; silent hall gaze, dynamic hero/guest shots, rare WC; cleanup. | `BarKaraoke*`, `KaraokeVoice{,Output}`, `RussianVocalizerSustainOutput` | Current |
| Retained Home waking opening | Frozen `05:59`, five-second lock, Wake Up/Quit, continuous wake. Gap: no shipped path reaches it. | `MainMenuRoot`, `HomeOpening{Controller,Timeline}` | Partial |
| Session clock and day/night rules | Persistent 48-minute day at ×1; two real seconds per game minute. Intoxication preserves its rate. | `GameTimeState`, `GameTimeRuntime` | Current |
| World time and pause ownership | Intoxication/debug factors compose; pauses freeze world/calendar. Debug speed preserves the physics step. | `GameTimeScale{State,Runtime}`, `PauseMenuController` | Current |
| Session day/time displays | Home clock, inventory and queued day announcements follow one persistent calendar. | `HomeAlarmClock`, `InventoryView` | Current |
| Pause / cutscene debug | Shared pause;warehouse door ambush, local replay/timecode;script→live struggle. | `PauseMenu*`,`Cutscene*`,`WarehouseDoorAmbush*` | Current |
| Hero inventory | Session stacks/Use;one-log cap;pickup/receipt;local preview MSAA/fallback. | `Inventory{Types,State,MenuModel,Controller,View}`, `GameSessionState`, `WorldItemFoundScreen` | Current |
| World item pickup and its found screen | Shared lit 3D turn/owned focus;one take;isolated lights;layers restored. | `WorldItem{PickupPlan,Pickup,PickupModel,FoundScreen,FoundView,InspectionPresenter}` | Current |
| Hunger, stress, fatigue and consumption | Needs follow the pause-aware calendar; consumption/rest commit relief once. Gap: needs apply no gameplay debuff. | `PlayerNeedsProgressionState`, `PlayerNeedsRules` | Partial |
| Inventory-backed target interactions | Shared item-backed target menus commit consumption once and refund failed startup. | `InventoryItemRequirement`, `InventoryTargetInteraction{Definition,Model,Controller}` | Current |
| Quest log and journal | Day one opens the mother's house; the full-screen journal lists ticked boxes over one description panel. | `Quest{Types,LogState}`, `GameDaySchedule`, `Journal{MenuModel,View}` | Current |
| New-quest notice | Blinking notebook at the screen's top-right edge; the key shows until first open. | `JournalNoticeView`, `GameSessionState.HasUnreadQuests` | Current |
| Stairwell quest descent gate | The active day-two cat quest gates descent and guides the hero back to the landing before restoring input. | `StairwellQuestDescentBlocker`, `PlayerMotor.MoveTowardsInteractionPose` | Current |
| City blueprint and road geometry | Two districts+park;144 IDs/−29.2% land; short fronts, courts/passages share ground/navigation. | `City{Layout,SpatialPlan,RoadGeometryPlan,CourtyardBlockPlanner}`, `BuildingLot` | Current |
| Coastal utilities | Three passive utility buildings; bounded poles/wires clear routes; Home shares the plan. | `CityCoastalInfrastructure{Plan,WorldBuilder}` | Current |
| City elevation and exterior stairs | Validated elevation plans keep external stairs and walkable ground consistent with collision. | `CityElevation{Plan,Planner,Validator,Rebaser,StairPlacement}`, `CityTerrainSurfacePlan` | Current |
| City mountain boundary and open tunnel | `default-coastal` closes west/south around the non-traversable river cave and one gate-free `8 x 5.5 m` portal. | `CityMountainBoundary*`, `CityMountainBackdrop*` | Current |
| Eastern mainland view | Closed post; shared descent/lamp/traffic datum, city on valley floor. One pooled mast, passive distance. | `CityEastExit*`, `CityEastRoadProfile`, `CityEastDistance*` | Current |
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
| Bar-side yard composition | The yard keeps its bare-ground wheelchair circuit and reserved utility anchors under the fixed cold spotlight. | `CityOpenAreaDecoration{Plan,Planner}`, `CityOpenAreaWorldBuilder` | Current |
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
| Global default NPCs | Stable IDs/looks, hero-body men160–185cm/slim–chubby; skinned modular clothes, no repeats. | `DefaultNpcCatalog`, `DefaultNpcPopulation`, `DefaultNpcFactory`, `DefaultNpc{Appearance,BodyShape}` | Current |
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
| City/Home pedestrians | City:192 schedules/≤36 actors/cache48; Home5/2, first8. | `CityResidentLifeRules`, `CityResidentWorld`, `CityPedestrianDirector` | Current |
| NPC Human V2 anatomy, appearance and visibility | Shared rigs; joint fields/volume and shaped hands/clothes retain UVs. | `NpcHumanV2AssetSetup`, `NpcDesignAppearanceCatalog`, `CharacterJointDeformation` | Current |
| City Route 01 bus | One validated route and pooled bus run in City. Gap: no Home simulation or live map vehicle marker. | `Runtime/Vehicles`, `CityBus{Plan,Planner,Actor,Audio,Director,Presentation,Factory,AssetRegistry}` | Partial |
| Pedestrian bench rests | Eligible walkers reserve benches, play owned sit/rest/stand actions and return to their route. | `CityBenchRest{Plan,Planner}`, `CityBenchNpcRestController` | Current |
| Route 01 passengers | Hero plus two ambient passenger places. Gap: fares, destination choice, persistence and live tracking. | `CityBusRide{Plan,Controller}`, `CityBusStopWait{Plan,Planner}` | Partial |
| City visual diversity and collision proxies | Deterministic visual variants share simple semantic collision proxies and reusable resources. | `CityDecoration{Descriptor,Plan,Planner,Validator,WorldBuilder}`, `CityDecorationCollisionCatalog` | Current |
| Road-edge fences | Plan-owned fences protect boundaries while preserving every validated road/waterside opening. | `RoadFence{Planner,Plan,WorldBuilder}`, `CitySurfaceDescriptor` | Current |
| Supermarket interior, product art and finite shelf purchases | A passive `16 x 11 x 3.6 m` shell plus one six-model, no-brand product pack shared by shop, inventory and cat flow. | `tools/build-supermarket-{interior,products}-3d-model.py` | Current |
| Supermarket cashier | One staged cashier uses the imported rig, authored service motions and existing shop interaction. | `SupermarketCashier{Provider,AssetRegistry,Factory,Presentation,Actor,SurveillanceState,BlinkState,Interaction}` | Current |
| Supermarket CCTV corner cameras | Four authored corner cameras follow the hero without gameplay surveillance. | `SupermarketInteriorAssetRegistry`, `SupermarketSecurityCamera{,WorldBuilder}` | Current |
| Supermarket fluorescent atmosphere | Visible fixtures own the store's restrained light, flicker and sound within explicit shared-resource budgets. | `SupermarketInteriorAtmosphere`, `SupermarketFluorescentFlicker` | Current |
| City day/night presentation | Shared time/light/windows/glow; City/balcony Soft directional shadows; Home mode restores. | `RuntimeSceneSetup`, `CityDayNightController` | Current |
| Deterministic exterior weather | Shared weather drives area rain/snow/wetness and causal audio. Gap: rain does not dim ambient light or alter grading. | `GameWeatherRules`, `CityWeatherController` | Partial |
| Exterior cloud ceiling | A passive shared-density shell supplies a camera-relative cloud ceiling with bounded horizon coverage. | `ExteriorCloud{AssetMetadata,Profile,MotionRules,Resources,Field,CaptureCamera}`, `ExteriorCloud.shader` | Current |
| Runtime cloth rags | Visible cloth uses shared bounded wind response and explicit attachment constraints. | `ClothPanelFactory`, `CityClothWindRegistry` | Current |
| City wind dressing | Shared wind drives props/cloth; City crowns remain static. | `CityWindDressing{Plan,Planner,Validator,WorldBuilder}`, `CityRopeSpanGeometry` | Current |
| Coastal stems | Root mask/hero radius drive existing stems; A/B reviewed. | `CityCoastalGrassWind` | Current |
| Scene and place music | Lodge gate/radio suppression;warehouse pitch.8/loop. Gap: cemetery/church slots empty. | `MusicMix`, `SceneMusicPlayer`, `CombatWarehouseMusicPlayer` | Partial |
| Common audio mix | Sharedmix;warehouse 3D clicks/whispers:.8/LP2800/World;dryUI;intoxication. | `GameAudioMixer`,`CombatWarehouseAmbiencePlayer` | Current |
| Port and cannery audio | Local machinery/fan/contact; arrival horn ×2 with longer echo/reverb. Pause/distance/seek own playback. | `CityWorkAudio`, `CityPortSound`, `CityCanneryController.{Sound,ProcessSound}` | Current |
| Intoxication sound perception | Separate eased audio persists across scenes; speech/music→0.25× over VHS, caption scatter. Sober restores. | `IntoxicationPerceptionRules`, `NpcSpeechVoice`, `SceneMusicPlayer`, `IntoxicationAudioDriver` | Current |
| Retro SFX and ambience | Generated retro cues and local ambience share routing, distance limits and scene-owned cleanup. | `RetroSfx`, `RetroAudioService` | Current |
| Footstep surfaces | Collider triangles/stamps/overlays select hero/NPC cues; three variants each. | `FootstepGround`, `HeroFootstepGround`, `NpcFootstepDirector` | Current |
| Causal City soundscape | Visible local sources own City sound; bounded schedules and shared routing control the mix. | `CitySound{SourceDescriptor,scapePlan,scapePlanner,SchedulePlanner,Occlusion}` | Current |
| Home alarm clock | Sofa-side `27.6 cm` clock: frozen flickering `05:59`, solid `06:00` on Wake, then session time. | `HomeAlarmClock{Plan,Builder,Synthesis}`, `HomeAlarmClock` | Current |
| Road, park and ground navigation | Masks follow physical bounds; exclusions retain corner overlap, safe eastern street margins stay open. | `RoadWalkableArea`, `CityGroundTraversalPlan{,ner}`, `CityRoadGroundBoundaryPlanner` | Current |
| Player motor | Tank/aim walk;world-space air inertia/limited steering;ground/snow/context limits. | `PlayerMotor{,.TargetMovement,.Snow}`, `PlayerDirectionalInput` | Current |
| Third-person chase camera | Orbit/RMB;.5m right/1.9m back/aim1.4;physical body follows;collision/dolly/fixed-modal. | `PlayerCameraFollow`, `IntoxicationDollyZoomModel` | Current |
| Home camera | Fixed shots/pan with explicit contextual ownership. | `HomeFixedCameraController`, `FixedCameraFocus` | Current |
| Home player visibility | Grouped occluder dither and fixed-shot rules keep the hero visible without changing collision. | `HomeOcclusion{Registry,Resolver}`, `HomePlayerOcclusionController` | Current |
| Modular 3D hero presentation | V2: 31 body/12 hair bones, 55 actions; shared joint boundaries and posed volume correction. | `Player3D*`, `PlayerFactory`, `CharacterJointDeformation` | Current |
| Hero wardrobe | Atomic slot replacement and body coverage; one authored outfit. Gap: extra outfits, UI and persistence. | `PlayerWardrobe`, `Player3DBathingAppearance` | Partial |
| Hero hair | Recorded bones; pose/gear guards; live wind/falls/pause/mirror. | `PlayerHair`, `SecondaryMotionBakeRuntime` | Current |
| Hero jacket cloth | Gameplay:rig/folds/hand-opening,0physics;Editor authoring kept. | `PlayerJacketCloth*`, `PlayerClothPhysicsPolicy` | Current |
| Wearable scarf | Hero/mouth/mirror;5s area height;Combat:rig,0physics. Owned ray buffers/pose-mask leases. | `PlayerScarf*`, `PlayerClothPhysicsPolicy`, `GameSessionState` | Current |
| Alpine Village cold hero | Hug/rubs persist running; scarf halves shiver. Warmth stops cold/breath. | `Player3DCharacterPresentation.Cold`, `PlayerCold{PresentationModel,BreathEffect}` | Current |
| Alpine frost presentation | Session frost; scarf halves growth, full frost stops cues; warm zones thaw. | `AlpineColdExposure{Model,Driver}`, `AlpineColdFrostPass`, `AlpineFrostAudio` | Current |
| Silent Hill attention | Layered gaze reacts to nearby authored targets within rig limits and yields to contextual ownership. | `PlayerAttention{Rules,Controller,Magnet}`, `IntoxicationHeadModel` | Current |
| Continuous 3D player interactions | Shared positioned actions preserve visible entry/exit continuity and clean up presentation/input ownership. | `PlayerAnimatedInteraction{Timeline,Controller}`, `PlayerDoorAction{Plan,Controller,Target}` | Current |
| Sofa sleep and wake | Two hand-supported pelvis steps with seated stops; domed pillow dents and recovers. | `HomeBedInteraction{,Plan}`, `PlayerAnimatedInteractionPelvisPath` | Current |
| City bench and park game-table seats | Shared sit/rest/stand; owning seat retains E/prompt and nested handlers through cleanup. | `CityParkBenchPlanner`, `CityBenchSit{Plan,WorldBuilder}` | Current |
| Ferryman car and passenger seat | Doors, attachment, camera, audio, area arrival; callouts outline radio knobs and glovebox handle. | `LastRouteCar{Plan,Factory,Doors,Suspension}`, `LastRouteCarDashboard`, `LastRouteRadioAffordance` | Current |
| Ferryman radio and road speech | Three stations, playheads and cabin reactions persist; passenger exit switches the radio off. | `LastRouteRadioMusicPlayer`, `LastRouteRideSpeech{Session,View}` | Current |
| Ferryman coin | One authored coin supplies the toss and contained glovebox pile; shared material, no pickups. | `LastRouteCoinAsset`, `LastRouteGloveboxCoins` | Current |
| The last route | Journey stages coordinate Ferryman dialogue, car legs, blackouts and arrivals; the ride is refused when very drunk. | `LastRouteRideController`, `LastRouteFerrymanRideRules`, `LastRouteCar{DrivePath,DriveModel,Driver,GiveWay*}` | Current |
| Home sofa sleep | Face push/3s hold/blinking cut; automatic same-rig wake restores fixed camera. | `HomeBedInteraction`, `HomeSleepController` | Current |
| Home balcony smoking | One modal balcony smoking sequence owns its prop, sound, camera and completion-only stress relief. | `HomeBalconySmoking{Plan,Interaction,Timeline,CameraDrift,ExhaleEffect}`, `HomeBalconyWorldBuilder` | Current |
| Shared refrigerator | Hand-free Home/warehouse;entry/chest aim;atomic Take/Back. Gap: `Use` hidden/unimplemented. | `HomeRefrigerator*`, `RefrigeratorInteractionContext`, `WorldItemInspectionPresenter` | Partial |
| Player shadows | Real mesh shadows and an analytic contact patch follow hero/contextual visibility ownership. | `Player3DCharacterPresentation`, `Player3DRagdollController` | Current |
| Interaction/UI | Prompts/input; nearest family;locker Take > open > close > distance; look/debug local. | `PlayerInteractor`, `IPrioritizedInteractable`, `InteractionPromptView` | Current |
| Spoken text | Translucent shared bubbles/RU vocalizer incl. E/hero; EN clicks, silent bottom; bounded jobs/pause/cleanup. | `SpeechDelivery`, `NpcSpeechBubbleView`, `NpcSpeechVoice` | Current |
| NPC role labels | Eleven roles (east guards share one), excluding village/Mother; 6–4 m, depth occlusion, speech/modal priority. | `NpcNameplateTarget`, `NpcNameplateContext`, `NpcNameplatePolicy` | Current |
| Debug controls | F4: busy/defeat flight, live timelines. F9: day/intoxication/truck; F1–F3: speed. | `DebugFreeCamera`, `MinigameDebugWindow`, `DebugTimeControls`, `HomeDebugCityMapShortcut` | Current |
| Structured session diagnostics | 20MiB/adaptive samples/end reserve/32-action tail;CPU/cloth/ammo/carry/input/F8. | `Runtime/Diagnostics`, `DuelJournal` | Current |
| Bar activity flavour | Legacy activity identity still selects bar flavour; the removed sprite minigames remain absent. | `BarActivityKind`, `BarActivityAssignment` | Current |
| Area map UI | City twelve glyphs/Village groups incl. former resort; shared legend/travel/XYZ. | `CityMap{Controller,View,AreaController,AreaView,MountainRoadOverlay,AlpineVillageOverlay}` | Current |
| Area map art | City charcoal/Village brochure; separate plan/RU SHA+UV guards/RU labels/marks; Road/other seeds schematic. | `{City,AlpineVillage}MapArtResources`, `CityMapArtReferenceExport`, `CityMapIllustratedLabels` | Current |
| Map XYZ inspection | Map points, fair/post/docks/arch/church-door places included, expose world coordinates and validated teleport targets. | `CityMapTeleport{Lattice,Grounds}`, `CityMapPointDescriptor` | Current |
| Map arrival ground | Area-owned ground; City lot/XYZ arrivals re-sample land Y and move occupied markers to a clear street. | `ICityMapTeleportGround`, `CityMap{City,MountainRoad}TeleportGround` | Current |
| Scene transition | Guarded direct/door loads own pending activation and terminal cleanup; failure handling is centralized. | `PlayerDoorAction{Plan,Controller,Target}`, `CityGameRoot` | Current |
| Area loading transition | One directed illustration and bottom bar; 20% load / 80% construction, owned until the destination is ready. | `AreaTravelService`, `AreaLoading{Root,ArtCatalog}` | Current |
| Door transition presentation | A deterministic `1.6 s` unscaled fixed-camera door sequence in a black void, warm on entry and cold on exit. | `DoorTransition{Root,Timeline,Direction}`, `RuntimeSceneSetup` | Current |
| Session state | Session facade delegates temporary vehicle ownership; resets and stale leases cannot leak ride state into a new game. | `GameSessionState`, `CityBlueprintCatalog` | Current |
| Bar drink service | Menu/repeat wave+taps, deferred charge/receipt; guest FIFO/look/nod. | `BarDrink*`, `BarRepeatGesture`, `BarBartenderServiceQueue` | Current |
| Intoxication stages and presentation | Session alcohol drives reversible visual/audio/body presentation and bounded passive recovery. | `IntoxicationStageRules`, `IntoxicationStatusController` | Current |
| Continuous balance and falling | Combat hero:planar/vertical caps/freefall gravity24;settled pose→seated/all-fours rise. | `PlayerBalanceModel`, `PlayerRiseModel`, `Player3D*` | Current |
| Hero drunk muttering | A seeded stage-dependent clock drives short authored mutters, slurring and an owned speech bubble. | `HeroMutterModel`, `HeroMutterLines` | Current |
| Hero nausea bouts | High intoxication drives the walking gauge, cancellation gates, vomiting relief and session mouth-soiling. | `HeroNauseaClock`, `HeroNauseaGaugeModel` | Current |
| Bar interior and exterior | Hall/worn WC; solid shared door, exclusive hero/guest access and masked broken mirror. | `BarInteriorLayout*`, `BarWashroom*`, `InteriorHingedDoorInteraction` | Current |
| Player home interior | 12×8m; sofa sleep/full kitchen/locked room; chair shared sit. | `HomeInteriorModelLibrary`, `HomeDiningSeatPlan` | Current |
| Apartment calendar appearance | Days1–7 neglect; sleep persists view7 separately from calendar; debug reverses. | `HomeApartmentDressing`, `HomeApartmentDay{Rules,Controller}` | Current |
| Player-home stairwell | Blender shell/dressing; original ramps, fixed cameras and cat contacts. | `StairwellLayout*`, `StairwellInteriorModelLibrary` | Current |
| City facade presentation | The legacy 4×4 path remains only for clipped Home crossings. | `CityFacade{Grid,Appearance}`, `CityBuilding{SurfaceAppearance,WindowSlotAppearance}` | Current |
| City window presentation | Pure facade/window slots select bounded lit shares and preserve district palettes and daytime light floors. | `CityDistrict{ArtProfile,PresentationPlan,PresentationPlanner}`, `CityWindowAppearance` | Current |
| Stairwell surface presentation | Cached textures and material property blocks apply measured stairwell surfaces without instance materials. | `StairwellSurfaceAppearance`, `SurfaceAppearanceCore` | Current |
| Exterior ground response | Indexed MPBs share response maps/road masks/snow edges; ridge haze retained. | `GroundSurface{Appearance,Coordinates}`, `GroundSurfaceLit`, `*SurfaceAppearance` | Current |
| Home surfaces | Five local living sheets; bathroom/balcony maps preserved. | `HomeSurfaceAppearance`, `HomeAuthoredVisualFactory` | Current |
| Stairwell cat | The perched cat supports talk/feed and the grin controller; the future story grin trigger remains unwired. | `StairwellCat*`, `StairwellCatInteraction` | Current |
| Home exterior context | Bounded same-seed streets and actual terrain; no collision. Balcony gates residents/atmosphere. | `HomeExteriorContext{Plan,Planner}`, `HomeExteriorViewBuilder`, `CityHomeGroundWorldBuilder` | Current |
| Player home atmosphere | Clock drives six local lights; Balcony borrows/restores City. | `HomeInteriorAtmosphere`, `HomeDayNightController` | Current |
| Mirror brushing | First-person X/Y brushing ends with a three-second close teeth inspection, then spit and faucet closure. | `HomeTeethBrushing*`, `HomeBrushingFirstPersonView`, `HomeSinkFaucet` | Current |
| Toilet choice and seated action | Shared real-hand lid actions; large branch seats, inspects and flushes. Dressing overlaps camera return; no relief. | `HomeToiletChoiceInteraction`, `HomeToilet{PlungeInteraction,SeatedTimeline,ActorPresentation}` | Current |
| Toilet body and bowl contact | Original-rig garment/anatomy module; emitted solid, once-only water contact and isolated local bowl fill. | `HomeToilet{SeatedAppearance,BowelEffect,BowlLighting}` | Current |
| Toilet solid in water | Fixed-step buoyancy, drift and rotation; flush forces drain inside the bowl, with camera clearance and pause/restore. | `HomeToiletFloatingBody`, `HomeToiletBowelEffect` | Current |
| Toilet water presentation | Two-sided water, camera optics, world-bus absorption and entry/underwater sounds; UI bypasses the scoped mix. | `HomeToiletUnderwater{Effect,Pass}`, `HomeToiletWater{Resources,AudioResources}` | Current |
| Toilet flush return | One-second whirlpool and smooth return share two forward camera turns; water/audio restore on exit. | `HomeToiletFlushVortex`, `HomeToilet{BowelEffect,UnderwaterEffect}` | Current |
| First-person toilet | Small option aims ballistic urine for `6 s` plus `2 s` shaking; wet marks persist across Home loads. | `HomeToilet{Interaction,FirstPersonView,AnatomyDynamics}`, `HomeUrine{Effect,Residue}` | Current |
| Home bathroom mirror | A real reflection by the geometric trick: a hole in the wall, a mirrored copy behind it and a second hero instance. | `HomeBathroomMirrorWorld`, `HomeBathroomMirror{OpeningBuilder,Resources}`, `HomeMirror{SubtreeClone,Plane}` | Current |
| Home bathroom scenes | The shower hero exits fully offscreen before redressing; cleanup restores rig and input. | `HomeBathroomSceneInteraction`, `HomeShower{Interaction,CameraPath}`, `HomeTeethBrushingInteraction` | Current |
| Interactive shower washing | E picks up soap; Q returns it and exits. Held LMB washes a selected point; any reachable region fills the gauge. | `HomeShower{WashingInteraction,SoapPose,WashingProgress}` | Current |
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
  -> Combat Test -> kit + Standard/Endless -> start/reset/kit/menu
  -> New Game -> time (default07:40) -> twelve locations/Back -> available confirm -> day1/selectedtime
     -> area/scene loading -> chosen root; village lane-foot is default
Retained Home opening (unreached): 05:59 -> Wake/Quit -> 06:00 -> Home -> City

blueprint + seed -> validated plans -> surfaces/navigation/map/night/decor/fences
  -> pedestrian population; Route 01 -> stops/poles -> pooled bus -> board/ride/alight
clock -> day announcement/needs/day-night; weather -> rain/lightning/thunder
map/travel -> AreaLoading -> destination Single (20%) -> staged composition (80%)
  -> ready -> restore audio/time/input

ten gameplay roots -> PlayerFactory -> Resources/Player/Player3DV2.prefab
  -> locomotion/balance/ragdoll/rise/contextual actions/inventory portrait
interaction -> SceneTransitionService -> DoorTransition -> destination
  -> Bar: seated service; Supermarket: finite stock/atomic purchase
  -> Stairwell: cat; Home: sleep/balcony/fridge/fixed views/exterior slice
results -> GameSessionState -> survive scene loads, not process exit
boundaries -> rotating NDJSON diagnostics; scene -> scoped audio
URP -> native effects/SMAA -> crisp UI
```
