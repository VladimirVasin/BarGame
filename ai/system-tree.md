# System tree

## Current repository

```text
Assets/
  Scenes/
    City.unity
    BarInterior.unity
  Settings/
    CityNoirVolumeProfile.asset
  Resources/
    Materials/
      CityNoirEmission.mat
    Shaders/
      CityAtmosphereParticle.shader
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
      Audio/         scene-local looping City and BarInterior themes
      Map/           ordered road-route model and deterministic pathfinding
      World/         layout, graph generation, world/night, local fog and halos
      Player/        motor, factory, 13-part rig, billboard, camera
      Interaction/   contract, selection, bar entrance and exit
      Scenes/        generated bar interior
      Drinks/        stable drink IDs used by current-run persistence
      Cocktails/     compatibility, deterministic shelves and 3-round session
      UI/            prompts, HUD, cocktail presentation and modal city map
    Editor/          scene/build helpers and reproducible noir asset setup
  Tests/
    EditMode/        layout, roads, night, cocktails, session, localization
    PlayMode/        presentation, cocktail modal and complete scene flow
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
```
