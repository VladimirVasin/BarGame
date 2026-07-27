# System tree

## Current repository

```text
Assets/
  Scenes/
    City.unity
    BarInterior.unity
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
      Ps1Composite.shader         average, perceptual RGB555 blend, point upscale
    Audio/
      CityMusic/
        city_theme.*  looping City theme
        README.txt
      BarMusic/
        bar_theme.*   looping BarInterior theme
        README.txt
    Cocktails/
      CocktailSpriteAtlas.png  4x4 glass/ingredient pixel-art atlas
    Localization/
      ru.json
      en.json
  Scripts/
    Runtime/
      Core/          bootstrap, city root, session, transitions
      Audio/         filtered scene themes, generated retro SFX and ambience
      Rendering/     PC RenderGraph PS1 world composite and settings
      Map/           ordered road-route model and deterministic pathfinding
      World/         layout, graph generation, world/night, local fog and halos
      Player/        motor, factory, 13-part rig, billboard, camera
      Interaction/   contract, selection, bar entrance and exit
      Scenes/        generated bar interior
      Drinks/        stable drink IDs used by current-run persistence
      Cocktails/     compatibility, deterministic shelves and 3-round session
      UI/            crisp retro theme, prompts, HUD, cocktail view and city map
    Editor/          scene/build helpers and reproducible noir/PS1 asset setup
  Tests/
    EditMode/        layout, roads, night, cocktails, session, retro UI/audio
    PlayMode/        PS1 GPU presentation, cocktail modal and complete flow
Packages/
ProjectSettings/
```

Cross-system flow:

```text
seed -> CityLayoutGenerator -> CityLayout -> CityWorldBuilder
                                          -> RoadWalkableArea -> PlayerMotor
                                          -> CityRoutePathfinder -> CityMap
                                          -> CityNightFixturePlanner
                                             -> CityNightWorldBuilder
player + lamp anchors -> CityNightAtmosphere -> CityLightHalo
player + seed -> CityFogField
player -> PlayerInteractor -> BarEntrance -> SceneTransitionService
       <- restored spawn <- GameSessionState <- BarExit
       -> CounterStation -> CocktailMinigame
                            -> CocktailRules + deterministic 7-item shelf
                            -> served progress -> GameSessionState
                            -> pending Wasted -> intoxication effects
City root -> CityMusicPlayer -> city_theme
Bar root -> BarMusicPlayer -> bar_theme
scene root -> matching procedural ambience
input/gameplay events -> RetroAudioService -> pooled generated SFX
URP post-processing -> 640x360 average -> subtle RGB555 blend -> point upscale
world composite -> crisp retro IMGUI overlay
```
