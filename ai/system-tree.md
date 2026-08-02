# System tree

## Current repository

```text
Assets/
  Scenes/
    MainMenu.unity
    City.unity
    DoorTransition.unity
    BarInterior.unity
    HomeInterior.unity
    StairwellInterior.unity
  Settings/
    CityNoirVolumeProfile.asset
    PC_Renderer.asset             active PC PS1 renderer feature
  Resources/
    Materials/
      CityNoirEmission.mat
      HomeOccluderDither.mat       shared opaque Home foreground cutaway
      Ps1Composite.mat
      RuntimePrimitiveLit.mat      shared packaged URP/Lit runtime geometry
    Rendering/
      Ps1PresentationProfile.asset  default 640x360, lower legacy presets
    Shaders/
      CityAtmosphereParticle.shader
      HomeOccluderDither.shader   Forward+ grouped cutaway with shadow/depth/normals
      HomeWindowGlass.shader      shared transparent Home window/door glass
      PlayerAnimatedInteractionOverlay.shader  depth-independent contextual sprite
      PlayerSpriteShadowCaster.shader  alpha-clipped ShadowsOnly silhouette
      Ps1Composite.shader         average, RGB555, intoxication distortion, point upscale
    Audio/
      Mixers/
        BarPromenadeAudio.mixer  shared groups, DSP returns and five scene snapshots
      CityMusic/
        city_theme.*  looping City theme
        README.txt
      BarMusic/
        bar_theme.*   looping BarInterior theme
        README.txt
      StairwellMusic/
        stairwell_theme.*  optional looping StairwellInterior theme
        README.txt
      SmokingMusic/
        smoking_theme.*  optional Home balcony-vignette loop supplied by user
        README.txt
    Cocktails/
      CocktailSpriteAtlas.png  4x4 glass/ingredient pixel-art atlas
    BeerPong/
      BeerPongBackground.png  empty 640x360 pixel-art table backdrop
      BeerPongAtlas.png       4x4 ball/hand/cup/effect sprite atlas
    SplitTheG/
      SplitTheGBackground.png  640x360 pixel-art bar/counter backdrop
      SplitTheGAtlas.png       4x4 pint/hand/foam/effect sprite atlas
    TinctureMatch/
      TinctureMatchBackground.png  640x360 pixel-art bar backdrop
      TinctureMatchAtlas.png       4x4 shot/effect sprite atlas
    Player/
      PlayerDirectionalAtlas.png       corrected 8x1 visual reference
      PlayerDirectionalPartsAtlas.png  9 layers x 8 views, 64x96 per cell
      PlayerDirectionalBodyExpressionsAtlas.png  five facial body rows
      PlayerBedSleepAtlas.png           8x8 contextual sequence, 128x96 per cell
      PlayerBalconySmokingAtlas.png      8x8 sequence with idle-matched/dithered edges
    Bar/
      Npc/
        BarNpcAtlas.png                 shared 3x2 transparent crowd atlas
    Stairwell/
      Cat/
        StairwellCatAtlas.png           512x256, 8x4 seated/look/grooming atlas
    Localization/
      ru.json
      en.json
  Scripts/
    Runtime/
      Core/          six-scene bootstrap, city root, session, transitions
      Diagnostics/   bounded NDJSON session log, rotation and F8 snapshot
      Audio/         shared mixer routing, filtered themes and generated retro audio
        GameAudioMixer.cs                  canonical groups, snapshots and transitions
        HomeSmokingMusicPlayer.cs          optional interaction-local loop + gain envelope
        HomeAlarmClockSynthesis.cs         generated 22050 Hz mechanical ring
        InteriorSoundscapeSynthesis.cs    quantized Home/Stairwell PCM + two-state fridge hum
        InteriorSoundscapeAnchorPlanner.cs layout-derived spatial emitter anchors
      Rendering/     PC RenderGraph PS1 composite and settings
        IntoxicationRenderState.cs  world-effect parameters shared with the pass
      Map/           ordered road-route model and heap pathfinding
      World/         city plus validated bar/home layout plans and builders
        CityDistrict.cs          district/path/land-use enums and district/park data
        CityTravelDistance.cs    weighted road/park-path distance between bars
        CityDistrictPointOfInterestPlan.cs  kinds, public bounds and street accesses
        CityDistrictPointOfInterestPlanner.cs  primary/public reservations + 18 m guard
        CityDistrictPointOfInterestWorldBuilder.cs  four physical open-place recipes
        CityDecorationDescriptor.cs  24 visual families and anchor contracts
        CityDecorationPlan.cs        immutable ordered seeded decoration data
        CityDecorationPlanner.cs     primary landmarks, lot visuals and clear clusters
        CityDecorationValidator.cs   landmark/core quotas, IDs and clearances
        CityDecorationWorldBuilder.cs  six-style, 48 m chunked visual recipes
        CityExteriorAppearance.cs    shared City/Home ground, facade and window recipe
        CityBarFacadeWorldBuilder.cs shared passive bar-front identity
        RoadFencePlan.cs         typed bar/home/park/public-place openings
        RoadFencePlanner.cs      exposed street boundary minus complete public sides
        CityNightFixturePlanner.cs  lamps/signals clear public ground and approaches
        RoadWalkableArea.cs      street/park/public XZ union; surfaces own height
        HomeInteriorLayout*.cs   main/bath paths, nine footprints and corner blocker
        HomeOcclusionRegistry.cs explicit logical renderer groups and visibility floors
        PlayerHomeBalconyGeometry.cs  shared City/Home facade transform and dimensions
        HomeBalconyLayout*.cs    connected room/threshold/deck walkable plan
        HomeExteriorContextPlan.cs  bounded same-seed street view descriptors
        HomeBalconyWorldBuilder.cs   window, open door, deck and safe open rails
        HomeExteriorViewBuilder.cs   collider-free roads/lots/windows/night fixtures
        HomeBedInteractionPlan.cs  open-side trigger plus stand/action hip anchors
        HomeBalconySmokingPlan.cs  dock/trigger/facing/camera + 24/24/16 timing
        HomeRefrigeratorPlan.cs  body/approach/camera/audio anchors + eight slots
        HomeRefrigeratorWorldBuilder.cs  worn hollow cabinet, shelves, bins and contents
        HomeRefrigeratorView.cs  animated door/handle/emissive interior presentation
        HomeRefrigeratorItemCatalog.cs  localized metadata and preview transforms
        HomeRefrigeratorItemView.cs  stable renderers, selection trigger and original root
        HomeAlarmClockPlan.cs       validated bed-relative nightstand/clock placement
        HomeAlarmClockBuilder.cs    low-poly nightstand and alarm-clock composition
        HomeBathroomBuilder.cs   oriented toilet, shower/sink and pipe damage
        HomeInteriorDressingBuilder.cs  collider-free poverty/neglect details
        StairwellLayout*.cs      three elevations, connected flights and blocker
        StairwellWorldBuilder.cs stairs, landings, rails, doors and physical ramps
        StairwellDressingBuilder.cs pipes, vents, stains, trash and upper debris
      Stairwell/Cat/ deterministic perch, atlas slicing, look and idle presentation
      Bar/NPC/       deterministic crowd plan, actors, shared sprites and director
      Player/        motor, 8-view rig, chase/fixed-pose camera and shadows
        IntoxicationStageRules.cs   five ranges and interpolated profiles
        BalanceChallengeModel.cs    seeded schedule and fixed-step arrow model
        PlayerIntoxicationPose.cs   sway, balance and fall pose evaluator
      Interaction/   contract, minigames and bar/home/stairwell entrances/exits
        PlayerAnimatedInteraction*.cs  enter/loop/exit + per-definition flip/crossfade/plane
        HomeBedInteraction.cs          first-E sleep, persistent loop, second-E wake
        HomeBalconySmoking{Interaction,Timeline}.cs  safe exit + camera push/drift + music envelopes
        HomeRefrigeratorInteraction*.cs  outer modal first-person open/inspect/close timeline
        HomeRefrigeratorItemInspection*.cs  nested hover/fly/rotate/return controller + timeline
        HomeRefrigeratorFirstPersonHand.cs  procedural sleeve, hand and handle reach
        StairwellCatInteraction.cs     localized temporary cat-response placeholder
      Scenes/        startup/bar/home/stairwell roots, atmosphere/reveal and transition
        MainMenuRoot.cs                 black build-index-0 new-run boundary
        HomeOpening*.cs                5 s gate, 3 s post-Wake alarm and 3x wake
        HomeAlarmClock.cs              mutable 28-segment time, spatial ring and rattle
        HomeSoundscape*.cs               paired fridge hum, balcony bed and domestic cues
        StairwellSoundscape*.cs          uneasy spatial beds and industrial cues
        HomeFixedCameraController.cs  three authored shots and sprite-plane alignment
        HomeBalconyExteriorAtmosphere.cs  Balcony-only City fog, grade and lights
        HomeOcclusionResolver.cs      five camera-to-player sample rays
        HomePlayerOcclusionController.cs  grouped dither fade/hold/restore
        HomeInteriorAtmosphere.cs     two practicals + bathroom/window Spots, grade and dust
        HomeBathroomLight*.cs         synchronized tube/halo/point/spill flicker
        StairwellFixedCameraController.cs  three height-selected fixed shots
        StairwellInteriorAtmosphere.cs flickering practicals, grade and dust
      Drinks/        stable IDs, retail catalog, atomic purchases and shop UI
      Cocktails/     compatibility, deterministic shelves and 3-round session
      BeerPong/      120 Hz 2.5D physics, rules, projection, controller and view
      SplitTheG/     pure timing/scoring session, controller, view and sprites
      TinctureMatch/ seeded 7x7 board, cascades, controller, view and sprites
      UI/            retro UI, segmented HUD, district/public-place map and F9 debug
        BalanceCheckView.cs         crisp overhead arc, arrow and risk meter
        CityMapController.cs        canonical layout POIs plus bar-route state
        CityMapView.cs              open public lots, four marker shapes and legend
        InteractionPromptView.cs    localized clickable contextual actions
        HomeRefrigeratorItemInspectionView.cs  hover label and PS1 item panel
    Editor/          scene/build helpers and reproducible noir/PS1/audio asset setup
      AudioMixerAssetSetup.cs  idempotent shared mixer topology and snapshot authoring
  Tests/
    EditMode/        layout plans, mixer DSP contract, sound synthesis and gameplay rules
      ProjectBuildSceneTests.cs             startup scene order/allow-list
      HomeOpeningTimelineTests.cs           persistent 05:59 flicker and Wake-only 06:00
      HomeAlarmClockPlanTests.cs            clock placement and circulation
      HomeRefrigerator{Plan,Timeline}Tests.cs  slots, approach and phase channels
      HomeBalconySmoking{Plan,Timeline}Tests.cs  dock, world-up yaw, timing, drift + safe exit
      PlayerBalconySmokingAssetTests.cs       atlas/idle-handoff/source/build contract
      HomeRefrigeratorItem{Catalog,InspectionTimeline}Tests.cs  metadata and nested phases
      HomeOcclusion{Registry,Resolver}Tests.cs  group and ray contracts
      InteractionPromptViewTests.cs          prompt callback lifecycle
      InteriorSoundscapeSynthesisTests.cs   deterministic distinct loop contracts
      Audio/HomeAlarmClockSynthesisTests.cs generated ring contract
    PlayMode/        audio routing/lifecycle, presentation, traversal and scene flow
      HomeOpeningPlayModeTests.cs           launch, wake, normal Home and cleanup
      HomeAlarmClockPlayModeTests.cs        spatial source/rattle/cleanup
      HomeRefrigerator*PlayModeTests.cs     storage, hover, nested inspection and restoration
      HomeBalconySmokingInteractionPlayModeTests.cs  facing, world-up yaw, drift/fade + restore
      HomeSmokingMusicPlayerPlayModeTests.cs optional clip and mixer-safe lifecycle
      HomePlayerOcclusionControllerPlayModeTests.cs  lifecycle + dither/Forward+ GPU checks
      InteriorSoundscapePlayModeTests.cs    spatial routing, crossfade and lifecycle
ArtSource/
  Player/
    PlayerDirectionalTurntable.png  locked 4x2 source turntable
    BedSleep/                    64 source frames plus keyed/generated sheets
    BalconySmoking/              generated/keyed art + exact-idle dither handoff
tools/
  build-player-puppet-atlas.py      deterministic reference/layers/blink build
  extract-player-bed-sleep-frames.py  deterministic keyed-sheet extraction
  build-player-bed-sleep-atlas.py    validate and pack the 8x8 runtime atlas
  extract-player-balcony-smoking-frames.py  align art + build exact-idle dither bridges
  build-player-balcony-smoking-atlas.py  validate and pack the 8x8 smoking atlas
  build-split-the-g-art.py          deterministic minigame background/atlas build
  build-tincture-match-art.py       deterministic shot background/atlas build
Packages/
ProjectSettings/
```

Cross-system flow:

```text
build index 0 -> MainMenuRoot -> BeginNewGame
                              -> HomeArrival.OpeningSleep
                              -> Single-load HomeInterior
seed -> CityLayoutGenerator -> 12x12 CityLayout -> CityWorldBuilder
                                           -> four urban districts + central park
                                           -> distant bars via CityTravelDistance
                                           -> player home beside one bar street
                                           -> four first-class public lots
                                              -> only at >= 18 m lot width and depth
                                              -> waterworks court
                                              -> drying yard
                                              -> weighbridge
                                              -> grounded non-emissive last-route island
                                           -> shared third-floor balcony facade geometry
                                           -> fresh road-node spawn beside the home
                                           -> RoadWalkableArea
                                              -> streets + park
                                              -> public grounds + approaches
                                              -> PlayerMotor
                                          -> CityRoutePathfinder
                                             -> district-aware CityMap
                                          -> RoadFencePlanner
                                             -> bar/home/park openings
                                             -> full public-place sides remain open
                                          -> CityNightFixturePlanner
                                             -> public reservations stay clear
                                             -> chunked lamps + signals
                                          -> CityDistrictPointOfInterestWorldBuilder
                                             -> physical paving + free-standing recipes
                                             -> intentional surface/obstacle colliders
                                          -> CityDecorationPlanner
                                             -> one ordinary-lot visual each
                                             -> four primary urban landmarks
                                             -> two park landmarks
                                             -> frontage/roadside/park clusters
                                             -> CityDecorationWorldBuilder
                                                -> six shared visual styles
                                                -> shadowless 48 m chunks
                                          -> CityMap
                                             -> canonical public-place descriptors
                                             -> open lots + four marker shapes + legend
                                          -> Home exterior context
                                             -> nearby canonical public places
                                             -> local-space visual reconstruction
player + lamp anchors -> CityNightAtmosphere -> CityLightHalo
player + seed -> CityFogField
player + main directional light -> PlayerDynamicShadow -> world receivers
player -> PlayerInteractor -> InteractionPromptView -> same guarded Interact action
                         -> BarEntrance/BarExit -> SceneTransitionService
                         or HomeEntrance -> StairwellInterior
                            -> StairwellApartmentEntrance -> HomeInterior
                            -> HomeExit -> StairwellInterior
                            -> StairwellStreetExit -> City home return
                                                  -> DoorTransitionRoot
                                                     -> preloaded destination
       <- active-bar return spawn/context <- GameSessionState
       -> StairwellLayoutPlanner -> StairwellLayoutValidator
                                 -> StairwellWorldBuilder
                                    -> 48 visual steps + three physical ramps
                                    -> lower/middle/apartment landings
                                    -> sealed upper-flight debris
       -> StairwellInteriorAtmosphere -> three flickering practicals
                                      -> green grade + sparse dust
       -> StairwellFixedCameraController -> lower/middle/apartment hard cuts
                                         -> fixed pose + camera-plane billboard
       -> StairwellCatPlan -> Middle Landing Back Rail perch + walkable approach
                           -> StairwellCatActor -> rear-view billboard
                                                   + player-tracking head
                                                   + ordinary idle
                                                   + rare 8-frame grooming (~36 s)
                           -> StairwellCatInteraction -> localized text placeholder
       -> StairwellAmbiencePlayer -> steady concrete room bed
       -> StairwellSoundscape -> spatial ventilation + electrical buzz
                              -> seeded pipe/metal/water/movement cues
       -> HomeInteriorLayoutPlanner -> HomeInteriorLayoutValidator
                                    -> HomeInteriorWorldBuilder
                                       -> HomeBathroomBuilder
                                       -> HomeInteriorDressingBuilder
                                       -> HomeRefrigeratorPlan
                                          -> split counter + shifted table approach
                                          -> HomeRefrigeratorWorldBuilder
                                             -> hollow worn cabinet + eight slots
                                             -> vodka / egg / open stew can
                                             -> HomeRefrigeratorItemCatalog + ItemView
                                                -> localized metadata + tight triggers
       -> HomeBalconyLayoutPlanner -> HomeBalconyLayoutValidator
                                   -> window + open door + walkable safe balcony
       -> same seed -> HomeExteriorContextPlanner
                    -> bounded roads/lots/windows/lamps/signals/decorations view
                    -> same CityDecorationWorldBuilder recipes in Home space
                    -> shared City exterior appearance + passive bar facade
                    -> no second City root/player/camera
                    -> Balcony-only City visibility, fog field and light pool
                       -> exact City fog/background/48 m cap/moonlight/grade
                       -> at most 12 street/bar lights; inactive indoors
                       -> captured Home render state restored on exit/disable
       -> HomeInteriorAtmosphere -> two aligned practical Light/emitter/halo pairs
                                 -> synchronized cold shadowed bathroom-spill Spot
                                 -> cold shadowed window cookie Spot
                                 -> at most four owned local realtime lights
                                    + separate scene Directional light
                                 -> shared transparent glass + grade + sparse dust
       -> HomeAmbiencePlayer -> calm steady room bed
       -> HomeSoundscape -> synchronized closed/open refrigerator loops
                          -> equal-power crossfade from current door amount
                          -> spatial balcony night air
                          -> seeded wood/radiator/radio/bathroom cues
       -> HomeFixedCameraController -> main/bath/balcony activation + hold bounds
                                    -> PlayerCameraFollow fixed pose
                                    -> BillboardSprite camera-plane opt-in
                                       -> reset when fixed control ends
       -> HomeOcclusionRegistry -> furniture/dressing/door/rail renderer groups
                                -> HomePlayerOcclusionController
                                   -> five camera-to-sprite samples
                                   -> head/chest/pelvis rays trigger group cutaway
                                   -> shared dither fade / hold / restore
                                   -> full opacity during Home modal presentation
       -> HomeBedInteractionPlan -> reachable open-side trigger
                                 -> HomeBedInteraction -> first/second E
                                    -> PlayerAnimatedInteractionController
                                       -> Idle/Entering/Looping/Exiting timeline
                                       -> PlayerBedSleepAtlas exact camera-plane billboard
                                       -> projected bed axis + preserved handedness
                                       -> lock motor; hide/restore rig + shadows
                                       -> owner cancel -> complete restoration
       -> HomeBalconySmokingPlan -> dock at (6.60, 0.12, -1.45)
                                  -> first E -> face city +X
                                     -> smoking TextureFlipX false
                                     -> world-up yaw billboard (camera-plane mode off)
                                     -> 0.35 s rig-to-atlas crossfade
                                     -> idle-matched/dithered 24-frame enter
                                     -> held 24-frame drag/exhale loop
                                  -> second E -> queued calm-boundary exit
                                     -> 16-frame discard + dithered idle handoff
                                     -> final 0.35 s atlas-to-rig crossfade
                                  -> shadows off until complete, then restored
                                  -> quadratic city-biased push to 38-degree FOV
                                     -> 0.33 m Home-local +X look offset
                                     -> hero near 0.37 viewport X; city visible right
                                     -> local 13-23 s harmonic camera drift
                                     -> no FOV pulse; continuous phase clock
                                  -> drift fades to zero with exact shot restoration
                                  -> optional smoking_theme fade in/out
       -> HomeRefrigeratorInteraction -> modal unscaled timeline
                                      -> clickable close prompt -> RequestClose
                                      -> first-person Bezier camera + low-poly hand
                                      -> seal / handle / 102-degree door animation
                                      -> persistent lit inspection
                                      -> HomeRefrigeratorItemInspectionController
                                         -> hover tint + localized cursor name
                                         -> Browsing/FlyingIn/Inspecting/FlyingOut
                                         -> centered slow rotation + dark backdrop
                                         -> name/description + Take/Use/Back placeholders
                                         -> exact transform/collider/color restoration
                                      -> close + exact fixed-shot/player restoration
                                      -> HomeSoundscape equal-power hum crossfade
       -> HomeAlarmClockPlan -> HomeAlarmClockBuilder
                             -> silent clock/nightstand room dressing
                             -> reusable flickering 05:59 / Wake-only solid 06:00
                             -> HomeAlarmClockSynthesis -> spatial SFX/World ring
       -> consumed HomeArrival.OpeningSleep -> HomeOpeningController
                                             -> direct sleeping loop + modal lock
                                             -> 5 s locked flickering 05:59 shot
                                             -> silent 05:59 + Wake Up/Quit
                                             -> Wake -> solid 06:00 + 3 s ring
                                             -> ring stops -> wake + smooth camera arc
                                             -> 3x exit + continuous gameplay settle
                                             -> existing wake frames
                                             -> normal Home camera/input, no handoff cut
       -> BarInteriorLayoutPlanner -> BarInteriorLayoutValidator
                                   -> BarInteriorWorldBuilder
                                   -> seven zones + four clear paths
                                   -> practical light/audio/NPC anchors
       -> BarInteriorAtmosphere -> six shadowless lights + grade + dust
       -> BarNpcPlanner -> BarNpcDirector -> 12 shared-sprite actors
       -> BarSoundscape -> spatial crowd bed + rare bar cues
       -> BarArrivalPresentation -> skippable Bezier camera reveal
       -> BarActivityStation -> BarMinigameCatalog -> CocktailMinigame
                                                  -> CocktailRules + deterministic 7-item shelf
                                               or -> BeerPongMinigame
                                                  -> 120 Hz ball physics + six-cup session
                                               or -> SplitTheGMinigame
                                                  -> one-sip timer + settling + scoring
                                               or -> TinctureMatchMinigame
                                                  -> 7x7 swaps + cascades + XXX
                             -> drinking progress -> GameSessionState
                             -> completed visit -> CityMap
       -> BarCounterStation -> BarDrinkShop
                            -> retail catalog + atomic cash/drink transaction
                            -> BarDrinkServicePlan -> nine physical bottle slots
                            -> BarDrinkServiceWorldBuilder
                               -> 9 bottle views + 5 vessel views + pour stream
                            -> BarDrinkServiceTimeline
                               -> seated camera + low-poly first-person arms
                               -> pickup -> pour -> 3 s drink -> vessel return
                               -> persistent browser -> explicit camera exit
                            -> GameSessionState wallet + drinking progress
GameSessionState intoxication -> IntoxicationStageRules
                              -> motor + puppet + camera
                              -> IntoxicationRenderState -> PS1 world composite
                              -> above 60 -> balance scheduler/model
                                 -> BalanceCheckView
                                 -> success or visual fall/recovery
F9 -> MinigameDebugWindow -> Left/Right arrows or buttons -> intoxication +/-20
                          -> BarMinigameCatalog -> isolated minigame instance
F8 -> GameDiagnosticsSnapshot -> GameLog -> flushed debug.log state record
state boundaries + scene/minigame correlation -> GameLog -> rotating NDJSON
Unity warning/error/exception ----------------------------^
scene root -> GameAudioMixer -> City/Bar/Stairwell/Home/DoorTransition snapshot
City root -> CityMusicPlayer -> city_theme -----------------------> Music
Bar root -> BarMusicPlayer -> bar_theme --------------------------> Music
Stairwell root -> StairwellMusicPlayer -> optional stairwell_theme -> Music
Home smoking interaction -> optional smoking_theme + gain envelope -> Music
scene root -> matching procedural ambience -----------------------> Ambience/Beds
Home/Stairwell root -> spatial soundscape ------------------------> Ambience/Details
Home opening -> HomeAlarmClock -> spatial mechanical ring --------> SFX/World
input/gameplay events -> RetroAudioService -> pooled SFX/UI groups
Music/details/world sends -> reverb/echo returns -> Master compressor
URP post-processing -> 640x360 average -> subtle RGB555 blend -> point upscale
world composite -> crisp retro IMGUI overlay
```
