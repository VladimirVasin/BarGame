# Systems map

An index, not a specification. One row per system: what it guarantees, where its
code lives, and whether it is real.

Detail belongs elsewhere and is not repeated here:

- normative decisions and exact tuning values: `ai/architecture-notes.md`;
- current end-to-end behaviour: `ai/project-overview.md`;
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
(save data, economy, weather) are `Deferred` in `ai/project-overview.md` and do
not make an otherwise complete system `Partial`.

## Systems

| System | Guarantee | Key files | Status |
| --- | --- | --- | --- |
| Unity URP foundation | Rendering profiles, seven build scenes and PC renderer settings. | Unity `6000.5.5f1`, URP `17.5.0`, `PC_Renderer.asset` | Current |
| PS1 world composite | Footprint-average the post-processed frame to `640x360`, apply intoxication warp/ghost/chroma, blend 35% RGB555 and point-upscale. | `Runtime/Rendering`, `IntoxicationRenderState`, `Ps1Composite` material/shader, `Ps1PresentationProfile` | Current |
| City composition | Exact seven-scene bootstrap and runtime roots. | `Runtime/Core`, `SceneIds` | Current |
| Startup waking opening | Fresh run behind the black build-index-0 boundary: frozen `05:59` clock shot, five input-locked seconds, localized Wake Up/Quit, then Wake starts the clock and a continuous six-second wake. | `MainMenuRoot`, `HomeOpening{Controller,Timeline}`, `HomeBedInteraction`, `HomeFixedCameraController`, `GameSessionState` | Current |
| Session clock and day/night rules | Persistent scaled clock from `06:00` at one game minute per real second (`24 h = 1440 s`); night/dawn/day/dusk sampling that survives scene loads and stops at `timeScale = 0`. | `GameTimeState`, `GameTimeRuntime`, `GameTimeDayNightRules`, `GameSessionState` | Current |
| Session time displays | The same session `HH:MM` on the physical Home alarm clock and in the inventory Status panel. | `HomeAlarmClock`, `InventoryView`, `GameSessionState` | Current |
| Gameplay pause menu | Escape/Start takes the shared modal lock in all five gameplay scenes, freezes scaled time, pauses gameplay audio and draws a localized Resume/Start Over/Quit menu with default-No confirmation. | `PauseMenu{Model,Controller}`, `BarMinigameModalLock`, `RetroAudioService`, scene roots | Current |
| Hero inventory | Localized fullscreen item screen under the shared modal lock in all five gameplay scenes: needs bars, cash, 3D portrait, stack grid, live rotating 3D item preview and contextual Eat/Drink/Examine. Stacks and world-source IDs persist across scene loads. | `Inventory{Types,State,MenuModel,ItemModelFactory,ConsumableCatalog,Controller,View,IconLibrary,ItemPreviewRenderer}`, `Resources/Player/Player3DPortrait`, `GameSessionState` | Current |
| Hunger, stress, fatigue and consumption | Clamped `0-100` session needs driven by the same scaled clock (hunger over `1440` game minutes, fatigue over `1080`); food, alcohol and completed bed rest each commit relief exactly once. **Gap:** the values drive no gameplay debuff yet. | `PlayerNeedsProgressionState`, `PlayerNeedsRules`, `InventoryConsumableCatalog`, `DrinkRules`, `GameSessionState` | Partial |
| Inventory-backed target interactions | Reusable Talk/Interact target menu with a single-stack item requirement, default-No confirmation and atomic one-shot consumption that refunds a thrown startup. | `InventoryItemRequirement`, `InventoryTargetInteraction{Definition,Model,Controller}`, `IInventoryTargetInteractionHandler`, `PlayerInteractor` | Current |
| City blueprint and layout model | One immutable stable-ID blueprint per session: validated connected sparse cells, typed urban/open areas, four graph-separated bars, one home, one supermarket and district public lots. | `CityBlueprint`, `CityBlueprintCatalog`, `CityLayoutGenerator`, `CityLayout`, `CitySurfacePlan`, `CityDistrict`, `CityTravelDistance` | Current |
| District public places | One non-building public lot per default urban district at `18 m` minimum sides, with canonical bounds and full-width street approaches, built as four free-standing physical places. | `CityDistrictPointOfInterest{Plan,Planner,WorldBuilder}`, `CityLandUseKind`, `CityLayoutGenerator` | Current |
| City world builder | Build only canonical blueprint surfaces: chunked carriageways, raised sidewalks, markings, zebras, park paths, beach, lake shore, cemetery and non-colliding water, with static mesh colliders on physical surfaces only. | `CityWorldBuilder`, `CityStreetSurface{Plan,Planner}`, `CityOpenArea{DecorationPlan,WorldBuilder}`, `CityExteriorAppearance`, `RuntimePrimitiveFactory` | Current |
| City street surface presentation | Road v2's `8 m` street footprint with a `6 m` carriageway between two raised `1 m` sidewalks; Road v2.1 exposes a flush `8 x 8 m` asphalt apron at selected bus nodes. Shared albedos through MPBs, no material instances. | `CityGenerationSettings`, `CityStreetSurface{Plan,Planner}`, `CityStreetIntersectionSelector`, `CityBusIntersectionSelector`, `CityExteriorAppearance` | Current |
| City and Home street pedestrians | One deterministic radius-safe sidewalk/zebra graph feeding a per-runtime population profile (City `8` day / `3` night over `13` pooled models, Home balcony `5` / `2` over `8`), filled in batches of two on a short cadence and dispersed by lane and peer distance. Each slot takes a seeded pick from an ordered five-archetype catalog; ordinary designs repeat, the airborne, the single lamp-bearing and the four bus-riding designs are declared per archetype, and the lamp design caps at one pooled instance. Fog-band spawn and recycle never depend on camera direction or frustum. | `Runtime/City/NPC`, `CityPedestrianPopulationProfile`, `CityGameRoot`, `HomeInteriorRoot`, `RoadWalkableArea`, `Resources/Pedestrians/{CityPedestrian3D,ChairCarrierPedestrian3D,KettleHatPedestrian3D,LongArmPedestrian3D,HelmetLampPedestrian3D}` | Current |
| City Route 01 bus | One immutable right-hand Street-only loop through every district POI and then Home, served by a single pooled real-scale actor with folding doors, driver, engine, sprung body and night lights. **Gap:** the runtime is City-only — Home reconstructs a static pole, and the map has no live vehicle marker. | `Runtime/Vehicles`, `CityBus{Plan,Planner,Actor,Director,Presentation,Factory,AssetRegistry}`, `CityBusTargetRoutePlanner`, `CityBusWideTurnPlanner`, `CityMapBusOverlay` | Partial |
| Route 01 passengers | A three-place cabin counting the hero: he boards either fully open door into reserved window seat `07` under the authored `BusBoardEnter`/`BusRideLoop`/`BusAlightExit` and a roll-free seated camera, while up to two ambient walkers wait beside a stop pole, board seated and alight at a seeded later stop. **Gap:** fare, destination choice, passenger persistence and live tracking are out of scope. | `CityBusRide{Plan,Controller}`, `CityBusStopWait{Plan,Planner}`, `CityBusNpcPassengerController`, `CityBus{Actor,Director}`, `CityPedestrian{Actor,Presentation,Resources}` | Partial |
| City visual diversity and collision proxies | One stable ordered decoration plan over 24 visual families expanded into six shared-material styles per `48 m` chunk, with a per-kind `None`/`Detail`/`Blocking` proxy catalog for grounded structural recipes only. | `CityDecoration{Descriptor,Plan,Planner,Validator,WorldBuilder}`, `CityDecorationCollisionCatalog`, `CityStaticCollisionBuilder` | Current |
| Road-edge fences | Street-union rails only where the outward side is water, unmapped or outside the active footprint, plus caps at true Street terminals; openings survive as clearance metadata. | `RoadFence{Planner,Plan,WorldBuilder}`, `CitySurfaceDescriptor` | Current |
| Supermarket interior and finite shelf purchases | A runtime-composed `16 x 11 x 3.6 m` shop holding exactly one unit of five products; a modal fixed-camera browser commits an atomic cash/inventory/source purchase and removes the physical unit, and sold IDs stay absent until a new game. | `SupermarketInterior{LayoutPlan,LayoutPlanner,LayoutValidator,WorldBuilder,Root}`, `Supermarket{ShelfView,ProductView,ShelfShopController}`, `SupermarketPurchaseRules` | Current |
| City day/night presentation | Apply the shared time sample to City directional/ambient/reflection lighting and the lamp/bar-light night factor; fog, backdrop, `48 m` far clip and grade stay fixed at every hour, and Bar/Supermarket/Stairwell stay outside the cycle. | `RuntimeSceneSetup`, `CityDayNightController`, `GameTimeDayNightRules`, `CityNight*`, `CityFogField`, `CityLightHalo` | Current |
| Scene music | One scene-owned looping theme per root through a low-pass filter and the shared `Music` group, with unscaled fade envelopes, preload-then-activate handoff and Balcony pause/resume at Home. **Gap:** only `city_theme` and `bar_theme` ship; supermarket, stairwell, home and smoking are optional empty slots. | `SceneMusicPlayer`, `HomeMusicPlayer`, `HomeSmokingMusicPlayer`, `SceneTransitionService`, `Resources/Audio/*` | Partial |
| Common audio mix | One Resources mixer for music, ambience, world/gameplay SFX and UI, with `-6 dB` master headroom, reverb/echo returns and per-scene snapshots. | `GameAudioMixer`, `BarPromenadeAudio.mixer`, `AudioMixerAssetSetup`, scene roots | Current |
| Retro SFX and ambience | Deterministic generated mono `22050 Hz` UI/world/bar effects with bounded pools and cooldowns, plus layered Home and Stairwell soundscapes on layout-derived spatial anchors. | `RetroSfx`, `RetroAudioService`, `RetroAmbience`, `InteriorSoundscape{Synthesis,AnchorPlanner}`, `{Home,Stairwell}Soundscape` | Current |
| Home alarm clock | One validated bed-relative clock and nightstand reusing a 28-segment display: flicker frozen `05:59`, switch to solid `06:00` only on Wake, then follow session time without rebuilding geometry. | `HomeAlarmClock{Plan,Builder,Synthesis}`, `HomeAlarmClock`, `GameSessionState`, `GameAudioMixer` | Current |
| Road, park and ground navigation | Constrain player XZ motion to an indexed union of streets, park lawn, `OpenLand` and `BuildableGround` with radius-safe connectors; physical colliders own local obstruction and climbing. | `RoadWalkableArea`, `CityGroundTraversalPlan{,ner}`, `CitySurfaceDescriptor`, `PlayerMotor` | Current |
| City route planning | Deterministic ordered shortest paths over generated street and park-path edges with a binary min-heap. | `Runtime/Map`, `CityLayout` | Current |
| Player motor | Camera-relative movement to `2.6 m/s` with acceleration and braking, plus a bounded scripted approach that keeps the ordinary gait, settles at an exact authored pose and reports a stall instead of teleporting. | `PlayerMotor`, Input System, `CharacterController`, `IWalkableArea` | Current |
| Third-person chase camera | Close exterior and interior framing with damped yaw and focus, free RMB orbit, deterministic idle motion, intoxication and fall reactions, and obstacle shortening that never rotates the player. | `PlayerCameraFollow`, `IntoxicationStageRules`, `BarMinigameModalLock` | Current |
| Home fixed camera | Three authored MainRoom/Bathroom/Balcony poses hard-cut with hold hysteresis instead of following the player; only quarter-strength status rotation around the fixed base pose. | `HomeCameraShot{,Selector}`, `HomeFixedCameraController`, `PlayerCameraFollow` | Current |
| Home player visibility | Explicitly registered furniture, door and rail renderer groups dither toward an authored alpha floor when they cross a camera ray to the hero, then restore; every collider and GameObject stays intact. | `HomeOcclusion{Registry,Resolver}`, `HomePlayerOcclusionController`, `HomeOccluderDither` material/shader | Current |
| Modular 3D hero presentation | One `Player3D.prefab` in all five gameplay roots: `1.75 m` Generic rig, 73 independent meshes, 31 bones and one shared material with per-mesh palette blocks, driving a damped Idle/Walk blend plus additive status poses and grounded boot pinning. | `Player3D{Resources,AssetRegistry,CharacterPresentation,RagdollController}`, `PlayerFactory`, `Assets/Player3D`, `tools/build-player-3d-model.py` | Current |
| Continuous 3D player interactions | The shared `Positioning -> Enter -> Loop -> Exit` path with independent authored entry/action/exit poses, sample-then-align pelvis anchors, moving-target support and owned idempotent cleanup. Normative contract: `ai/contextual-animation-standard.md`. | `PlayerAnimatedInteraction{Timeline,Controller}`, `IPlayerClipPresentation`, `Player3DCharacterPresentation`, `PlayerMotor` | Current |
| Home bed sleep | One trigger on the door-side bed edge; guided walk, neutral settle, then `BedEnter`/`BedSleepLoop`/`BedExit` through a real bedside sit. Normal completion clears session fatigue; cancellation preserves it. | `HomeBedInteraction{Plan,}`, `PlayerAnimatedInteractionController`, `GameSessionState`, localization catalogs | Current |
| Home balcony smoking | Balcony dock with guided city-facing entry, `SmokeEnter`/`SmokeLoop`/`SmokeExit` on a socket-bound cigarette, periodic mouth plume, a permanent rail ashtray and a bounded city-biased camera push-in with drift and exact restoration. | `HomeBalconySmoking{Plan,Interaction,Timeline,CameraDrift,ExhaleEffect}`, `HomeBalconyWorldBuilder`, `HomeSmokingMusicPlayer` | Current |
| Home refrigerator | A derived cabinet, clear approach, first-person pose and eight storage slots; a modal open → browse → inspect → close flow driven by a prefab-derived right-arm subset under a world-visibility lease, where `Take` commits atomically. **Gap:** `Use` is registered but unavailable. | `HomeRefrigerator{Plan,WorldBuilder,View,Interaction,InteractionTimeline,FirstPersonHand}`, `HomeRefrigeratorItem*`, `Player3DFirstPersonSubset` | Partial |
| Player shadows | Every production mesh casts and receives ordinary URP shadows, while one shared analytic contact quad stays grounded and expands through both authored and physics fall phases. | `Player3DCharacterPresentation`, `Player3DRagdollController`, `PlayerContactShadow{,.shader}`, `PlayerPresentationMetrics` | Current |
| Interaction/UI | Select nearby entrances, exits, bed, smoking dock, refrigerator, stations, counter and bus triggers; localized world prompts are full pointer targets repeating the same availability guards and action path as E/Enter/gamepad South. | `PlayerInteractor`, `InteractionPromptView`, `InventoryTargetInteractionController`, `CityBusRideController`, `Runtime/{Interaction,UI}`, `RetroUiTheme` | Current |
| Bar minigame catalog | One explicit ordered set of activity IDs, localized labels and factories shared by normal interiors and debug launches, with no game-specific debug code. | `BarMinigameCatalog`, `BarMinigameDefinition`, `IBarMinigame` | Current |
| F9 debug window | List every registered game in City and BarInterior, close conflicting modals before taking the lock, step session intoxication in clamped `±20` increments and launch isolated instances; City also toggles map test-teleport. | `MinigameDebugWindow`, `BarMinigameCatalog`, `GameSessionState`, `BarMinigameModalLock`, `CityMapController` | Current |
| Structured session diagnostics | One bounded UTF-8 NDJSON stream with build/session/scene/seed context and correlated transition, minigame and Unity-message events; rotates at 5 MiB keeping three archives, `F8` snapshots. Format: `ai/debug-log.md`. | `Runtime/Diagnostics`, instrumented state boundaries, `MinigameDebugWindow` | Current |
| Bar activity routing | Assign the first four stable row-major bars to cocktails, beer pong, Split the G and Tinctures through one pure resolver, falling back to cocktails for later bars and preserving the activity through transition. | `BarActivityKind`, `BarActivityAssignment`, `BarMinigameCatalog`, `BuildingLot`, `GameSessionState` | Current |
| City map UI | Draw only canonical active surfaces at a readable `22 px/cell` with clipping and independent X/Y pan; player, bars, labeled home, grocery, four public places, the orange itinerary and blue Route 01 with five numbered stops. F9 test-teleport makes every lot selectable. A live bus marker is deliberately omitted. | `CityMap{Controller,View,Viewport}`, `CityMapBusOverlay`, `MinigameDebugWindow`, `RetroUiTheme` | Current |
| Scene transition | One guard across the full transfer: load `DoorTransition` in Single mode, preload the destination with activation blocked, and activate only at the final blackout. | `SceneTransitionService`, `SceneIds`, `GameSessionState` | Current |
| Door transition presentation | A deterministic `3.15 s` unscaled fixed-camera door sequence in a black void, tinted warm on entry and cold on exit, with generated latch and hinge cues. | `DoorTransition{Root,Timeline,Direction}`, `RuntimeSceneSetup`, `RetroSfx` | Current |
| Session state | Reset a complete run on launch, then preserve blueprint ID and seed, active bar and activity, ordered route, visited bars, return kinds, clock and day index, cash, needs, intoxication and the balance sequence across scene loads. | `GameSessionState`, `CityBlueprintCatalog`, `GameTimeState`, `PlayerNeedsProgressionState`, `CityReturnKind`, `HomeArrivalKind` | Current |
| Bar drink retail and physical service | Nine localized drinks at fixed prices through a seated first-person counter: nine physical bottles, an atomic cash/drink commit, prefab-derived arm subsets, a world-space pour and a three-second drink from the matching vessel. | `BarDrink{Catalog,PresentationCatalog,ServicePlan,ServiceTimeline,ShopController,BottleView,VesselView,FirstPersonArms}`, `DrinkPurchaseRules`, `BarCounterStation` | Current |
| Cocktail domain | Four bases with ingredient compatibility, a deterministic seven-item shelf of four compatible additions and three traps, and a scored three-round session. | `Runtime/Cocktails`, persisted `DrinkId` values | Current |
| Cocktail minigame | A same-scene modal base/addition/serve loop that commits drinking state after every serving, retains bad-mix penalties and terminates at `MaxIntoxicationReached`. | `CocktailMinigame{Controller,View}`, `IBarMinigame` | Current |
| Cocktail presentation | A 4x4 pixel-art atlas sliced into IMGUI UV cells, animating ingredient travel, pouring, fill, sparks, bad bubbles, shake and final rank. | `CocktailSpriteLibrary`, `CocktailMinigameView`, `RetroUiTheme`, `Resources/Cocktails` | Current |
| Beer-pong domain | A fixed `120 Hz` aimed-ball simulation with swept table and cup-mouth contacts, six cups, ten throws, clean/bank scoring and an early-clear bonus. | `Runtime/BeerPong/BeerPong{Types,TableLayout,Physics,Session}` | Current |
| Beer-pong minigame | A modal 2D second-bar activity that projects physical x/y/z onto the table backdrop and persists a light-beer penalty after every miss. | `BeerPongMinigame{Controller,View}`, `BeerPongProjection` | Current |
| Beer-pong presentation | A point-filtered `640x360` table and 4x4 atlas with projected shadows, cup reactions, compact aim/power feedback and localized results. | `BeerPongSpriteLibrary`, `Resources/BeerPong`, `RetroUiTheme` | Current |
| Split the G domain | A normalized pint advancing from countdown through one irreversible held sip to a scored result, frame-chunk invariant, with five tolerance bands and up to three glasses. | `SplitTheG{Settings,Session,Scoring}` | Current |
| Split the G minigame | A modal third-bar hold/release activity for Space, LMB and gamepad South that hides the exact level while drinking and persists the real dark-beer fraction immediately. | `SplitTheGMinigameController`, `BarMinigameModalLock`, `GameSessionState` | Current |
| Split the G presentation | A point-filtered `640x360` backdrop and 4x4 atlas with glass tilt, target line, obscured liquid, settling foam, localized result cards and generated gulp SFX. | `SplitTheGMinigameView`, `SplitTheGSpriteLibrary`, `Resources/SplitTheG`, `RetroSfx` | Current |
| Tincture-match domain | A seeded match-free `7x7` board with five flavors and at most one `XXX`, resolving unique clears, gravity, refills, cascades to `x5`, 15 moves and deterministic reshuffles. | `Runtime/TinctureMatch`, `TinctureMatch{Generator,Resolver,Session}` | Current |
| Tincture-match minigame | A modal fourth-bar board for click/drag, keyboard and gamepad where invalid swaps cost no move and only an activated `Moonshine` persists `+24` intoxication. | `TinctureMatchMinigameController`, `BarMinigameModalLock`, `DrinkRules`, `GameSessionState` | Current |
| Tincture-match presentation | A logical `640x360` view with a point-filtered backdrop and 4x4 atlas of five symbol-coded shots plus literal `XXX`, RU/EN feedback and generated swap/match/burst SFX. | `TinctureMatchMinigameView`, `TinctureMatchSpriteLibrary`, `Resources/TinctureMatch`, `RetroSfx` | Current |
| Intoxication stages and presentation | Map `0-100` into Sober plus five named 20-point stages driving movement speed, additive 3D bone poses, camera and world-composite effects, with recovery that accelerates toward sober and pauses under modal ownership. | `IntoxicationStageRules`, `IntoxicationStatusController`, `IntoxicationRenderState`, `IntoxicationHudView`, `Player3DCharacterPresentation` | Current |
| Balance checks and falling | Above `60`, a seeded fixed-step arrow challenge that never disables locomotion until failure; failure plays a directional Fall clip, hands the rig to 13 kinematic-to-dynamic bodies, then blends into a full-body side-specific Rise. The gameplay root and `CharacterController` stay upright and fixed. | `BalanceChallengeModel`, `IntoxicationStatusController`, `BalanceCheckView`, `PlayerFallAnimationTimeline`, `Player3DRagdollController`, `PlayerContactShadow` | Current |
| Bar interior layout and world | One deterministic validated `22 x 16 x 4.8 m` layout with seven zones, four connected clear paths, one reachable activity fixture and one counter station, composed into shell, counter, seating, stage and activity dressing. | `BarInteriorLayout{Plan,Planner,Validator}`, `BarInteriorWorldBuilder`, `BarDrinkServiceWorldBuilder`, `BarInteriorRoot` | Current |
| Player home interior | A validated `10 x 8 x 3.4 m` main-room and bathroom plan with clear entry, main and bathroom paths and furniture outside protected circulation, plus a real window and open door onto a walkable balcony at `4.7 m` street elevation. | `HomeInteriorLayout*`, `HomeBalconyLayout*`, `HomeInteriorWorldBuilder`, `HomeBalconyWorldBuilder`, `HomeBathroomBuilder`, `HomeInteriorRoot` | Current |
| Player-home stairwell | A side-aware `8.6 x 9.6 x 6.25 m` scene connecting street lobby, middle landing and apartment door over 48 visible steps and three seam-free ramps, with the flight above the hero blocked, three height-selected fixed shots and a green desaturated grade. | `StairwellLayout*`, `Stairwell{WorldBuilder,DressingBuilder,InteriorRoot,FixedCameraController,InteriorAtmosphere,Soundscape}` | Current |
| Stairwell surface presentation | Eight cached opaque albedos applied through projection-aware deterministic UV scale and MPB values on one shared `RuntimePrimitiveLit`, with linear-albedo-compensated palette tints and no material instances. | `StairwellSurfaceAppearance`, `Stairwell{WorldBuilder,DressingBuilder}`, `Resources/Stairwell/Textures` | Current |
| Stairwell cat | One rear-view billboard cat on the middle landing rail with a player-tracking head, idle and rare grooming; Talk/Interact opens the shared target menu, and feeding consumes one `OpenStewCan` through synchronized player clips and a cat sprite track. | `StairwellCat{Plan,Actor,SpriteLibrary,LookSelector,IdleModel,Feeding*}`, `StairwellCatInteraction`, `InventoryTargetInteractionController` | Current |
| Home exterior context | Reconstruct a bounded collider-free view of the real home street inside `HomeInterior` from the same blueprint ID and seed, including transformed decoration, a static Route 01 pole and pedestrians enabled only for the Balcony shot. No bus actor is composed, by decision. | `HomeExteriorContext{Plan,Planner}`, `HomeExteriorViewBuilder`, `CityBusStopWorldBuilder`, `CityExteriorAppearance`, `HomeDayNightController` | Current |
| Player home atmosphere | A fog-free post-processed interior on an explicit five-light budget with co-located emitters and halos; the Balcony shot temporarily borrows City's fog, grade and night factor and restores the captured indoor state on every exit. | `HomeInteriorAtmosphere`, `HomeDayNightController`, `HomeBalconyExteriorAtmosphere`, `HomeBathroomLight{Fixture,Flicker}`, `HomeSoundscape` | Current |
| Bar crowd | 12 stable role-based NPCs under a cap of 14 from one shared transparent 3x2 atlas, with centralized `8 Hz` decisions, lightweight per-frame poses and whole-NPC camera-depth sorting against the 3D player. | `Runtime/Bar/NPC`, `Resources/Bar/Npc/BarNpcAtlas.png`, bar layout anchors | Current |
| Bar cinematic atmosphere | Six shadowless practical lights, bar-only post-processing, bounded dust, scene-local spatial cues and a skippable `1.35 s` arrival reveal that restores follow and orbit state before any modal opens. | `BarInteriorAtmosphere`, `BarSoundscape`, `BarArrival{Timeline,Presentation}`, `RuntimeSceneSetup` | Current |
| Automated tests | EditMode and PlayMode coverage over deterministic plans, asset and rig contracts, interaction lifecycles, atomic transactions, audio routing, PS1 presentation and scene round trips, across 154 test files in two assemblies. | `Assets/Tests/{EditMode,PlayMode,Infrastructure}`, `tools/build-player-3d-model.py` validators | Current |
| Automated test audio guard | Silence global listener output for every EditMode and PlayMode run without pausing sources or DSP, then restore the previous volume. | `BarPromenade.TestSupport`, `AutomaticTestAudioMute` | Current |

## Primary intended flow

Structural backbone only. Exact ordering, tuning and edge cases live in
`ai/project-overview.md` and `ai/architecture-notes.md`.

```text
build index 0 -> MainMenu -> BeginNewGame + HomeArrival.OpeningSleep
  -> HomeInterior sleeping opening
     -> 05:59 clock shot -> 5 s input lock -> Wake Up / Quit
     -> Wake -> 06:00 + session clock starts -> alarm hold -> 6 s wake
     -> ordinary Home control -> StairwellInterior -> City

session clock -> day/night sample -> City + Home lighting, Home clock display
              -> elapsed minutes -> hunger (1440) + fatigue (1080) -> inventory bars

blueprint ID + seed -> immutable blueprint -> validated sparse layout
  -> typed area cells -> surfaces, navigation, city map
  -> four bars / home / supermarket / district public places
  -> street surface plan -> Road v2 carriageway + sidewalks + Road v2.1 aprons
     -> pedestrian graph -> profile-sized player-relative walker population
     -> Route 01 loop -> stops + poles -> one pooled bus actor
        -> board prompt -> seat 07 -> ride -> alight at a later stop
  -> night fixture plan -> lamps, signals, halos
  -> decoration plan -> visuals + collision proxies
  -> fence plan -> rails with clearance openings

five gameplay roots -> PlayerFactory -> Resources/Player/Player3D.prefab
  -> Idle / Walk blend + face + additive status bones
  -> contextual actions: bed, smoking, cat feeding, bus board/ride/exit
  -> failed balance -> Fall clip -> bounded ragdoll -> Rise -> Relaxed
  -> first-person subsets: bar bottles, refrigerator
  -> inventory portrait

player -> interaction -> SceneTransitionService -> DoorTransition
  -> preload destination -> activate at blackout
  -> BarInterior   -> minigame catalog -> cocktails / beer pong / Split the G / tinctures
                   -> counter station -> physical drink service
  -> SupermarketInterior -> finite shelves -> atomic purchase
  -> StairwellInterior   -> cat -> Talk / feed
  -> HomeInterior        -> bed, balcony smoking, refrigerator, three fixed shots
                         -> bounded same-seed exterior reconstruction

all gameplay results -> GameSessionState -> preserved across Single loads
state boundaries     -> correlated rotating debug.log (NDJSON)
scene root           -> audio mixer snapshot + scene music + ambience
URP post-processing  -> PS1 composite -> crisp retro IMGUI overlay
```
