# System tree

## Current repository

```text
Assets/
  Scenes/
    City.unity
    DoorTransition.unity
    BarInterior.unity
    HomeInterior.unity
  Settings/
    CityNoirVolumeProfile.asset
    PC_Renderer.asset             active PC PS1 renderer feature
  Resources/
    Materials/
      CityNoirEmission.mat
      Ps1Composite.mat
    Rendering/
      Ps1PresentationProfile.asset  default 640x360, lower legacy presets
    Shaders/
      CityAtmosphereParticle.shader
      PlayerSpriteShadowCaster.shader  alpha-clipped ShadowsOnly silhouette
      Ps1Composite.shader         average, RGB555, intoxication distortion, point upscale
    Audio/
      CityMusic/
        city_theme.*  looping City theme
        README.txt
      BarMusic/
        bar_theme.*   looping BarInterior theme
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
    Bar/
      Npc/
        BarNpcAtlas.png                 shared 3x2 transparent crowd atlas
    Localization/
      ru.json
      en.json
  Scripts/
    Runtime/
      Core/          bootstrap, city root, session, transitions
      Diagnostics/   bounded NDJSON session log, rotation and F8 snapshot
      Audio/         filtered scene themes, generated retro SFX and ambience
      Rendering/     PC RenderGraph PS1 composite and settings
        IntoxicationRenderState.cs  world-effect parameters shared with the pass
      Map/           ordered road-route model, heap pathfinding and district map
      World/         city plus validated bar/home layout plans and builders
        CityDistrict.cs          district, land-use, park and gate data
        CityTravelDistance.cs    weighted road/park-path distance between bars
        RoadWalkableArea.cs      XZ union; surface colliders own walkable height
      Bar/NPC/       deterministic crowd plan, actors, shared sprites and director
      Player/        motor, 8-view rig, camera and shadows
        IntoxicationStageRules.cs   five ranges and interpolated profiles
        BalanceChallengeModel.cs    seeded schedule and fixed-step arrow model
        PlayerIntoxicationPose.cs   sway, balance and fall pose evaluator
      Interaction/   contract, shared minigame catalog, bar/home entrances/exits
      Scenes/        bar/home roots, bar atmosphere/reveal and door transition
      Drinks/        stable IDs, retail catalog, atomic purchases and shop UI
      Cocktails/     compatibility, deterministic shelves and 3-round session
      BeerPong/      120 Hz 2.5D physics, rules, projection, controller and view
      SplitTheG/     pure timing/scoring session, controller, view and sprites
      TinctureMatch/ seeded 7x7 board, cascades, controller, view and sprites
      UI/            retro UI, segmented HUD, map and F9 debug
        BalanceCheckView.cs         crisp overhead arc, arrow and risk meter
    Editor/          scene/build helpers and reproducible noir/PS1 asset setup
  Tests/
    EditMode/        layout, roads/fences, intoxication/balance rules, sessions
    PlayMode/        PS1 GPU, player poses, modals, F9 debug and complete flow
ArtSource/
  Player/
    PlayerDirectionalTurntable.png  locked 4x2 source turntable
tools/
  build-player-puppet-atlas.py      deterministic reference/layers/blink build
  build-split-the-g-art.py          deterministic minigame background/atlas build
  build-tincture-match-art.py       deterministic shot background/atlas build
Packages/
ProjectSettings/
```

Cross-system flow:

```text
seed -> CityLayoutGenerator -> 12x12 CityLayout -> CityWorldBuilder
                                           -> four urban districts + central park
                                           -> distant bars via CityTravelDistance
                                           -> player home beside one bar street
                                           -> fresh road-node spawn beside the home
                                           -> indexed RoadWalkableArea -> PlayerMotor
                                          -> CityRoutePathfinder
                                             -> district-aware CityMap
                                          -> RoadFencePlanner
                                             -> chunked fences + park gates
                                          -> CityNightFixturePlanner
                                             -> chunked lamps + signals
player + lamp anchors -> CityNightAtmosphere -> CityLightHalo
player + seed -> CityFogField
player + main directional light -> PlayerDynamicShadow -> world receivers
player -> PlayerInteractor -> BarEntrance/BarExit -> SceneTransitionService
                         or HomeEntrance/HomeExit
                                                  -> DoorTransitionRoot
                                                     -> preloaded destination
       <- active-bar return spawn/context <- GameSessionState
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
City root -> CityMusicPlayer -> city_theme
Bar root -> BarMusicPlayer -> bar_theme
scene root -> matching procedural ambience
input/gameplay events -> RetroAudioService -> pooled generated SFX
URP post-processing -> 640x360 average -> subtle RGB555 blend -> point upscale
world composite -> crisp retro IMGUI overlay
```
