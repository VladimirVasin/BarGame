# Systems map

| System | Responsibility | Depends on | Status |
| --- | --- | --- | --- |
| Unity URP foundation | Rendering profiles, scenes, project settings | Unity 6000.5.5f1, URP 17.5.0 | Current |
| City composition | Exact-scene bootstrap and runtime roots | `Runtime/Core`, scene IDs | Implemented |
| City layout model | Seeded roads, lots, buildings and bar descriptors | `Runtime/World/CityLayout*.cs` | Implemented |
| City world builder | Construct night-toned roads, buildings, varied windows, bar approaches and distinct bar facade markers | `CityWorldBuilder.cs`, `BarBuildingMarker.cs`, primitive factory | Implemented |
| Noir city presentation | Apply readable dense City-only fog and post-processing; follow the player with a 36-particle fog field; build depth-tested halos; plan lamps/signals; pool at most 12 shadowless spot/point lights | `RuntimeSceneSetup`, `CityNightFixture*`, `CityNightWorld*`, `CityNightAtmosphere`, `CityFogField`, `CityLightHalo`, shared emissive/atmosphere resources | Implemented |
| Scene music | Loop `city_theme` only in City and `bar_theme` only in BarInterior; stop each scene-local player on Single-mode transition | `CityMusicPlayer`, `BarMusicPlayer`, `Resources/Audio/{CityMusic,BarMusic}` | Implemented |
| Road navigation | Constrain a circular player to the union of road/apron rectangles | `RoadWalkableArea.cs`, player motor | Implemented |
| City route planning | Build deterministic ordered shortest paths over generated road edges | `Runtime/Map`, `CityLayout` | Implemented |
| Player motor | Camera-relative keyboard/gamepad movement | Input System, CharacterController | Implemented |
| Third-person camera | Perspective chase framing, yaw input and obstacle avoidance | `PlayerCameraFollow.cs`, Input System, physics | Implemented |
| Sprite presentation | Billboard and procedural 13-part walk pose | `Runtime/Player` | Implemented |
| Interaction/UI | Select nearby bars, exits and counter stations; show localized prompts | `Runtime/Interaction`, `Runtime/UI` | Implemented |
| City map UI | Display roads/player/bars and edit a modal ordered itinerary | `CityMapController.cs`, `CityMapView.cs`, Input System | Implemented |
| Scene transition | Guarded async city/interior loads | `SceneTransitionService.cs` | Implemented |
| Session state | Preserve seed, active bar, ordered route, return contract, intoxication, last alcohol and served-cocktail count for the current run | `GameSessionState.cs` | Implemented |
| Cocktail domain | Define four bases and ingredient compatibility; generate a deterministic seven-item shelf with four compatible additions and three traps; score and advance a three-round session | `Runtime/Cocktails`, persisted `DrinkId` values | Implemented |
| Cocktail minigame | Run same-scene modal base/addition/serve input, commit state after every served cocktail and defer a pending `Wasted` effect until finish/close | `CocktailMinigameController.cs`, `CocktailMinigameView.cs` | Implemented |
| Cocktail presentation | Slice a 4x4 pixel-art atlas into IMGUI UV cells and animate ingredient travel/tilt, pouring, glass fill, success sparks, bad bubbles, shake, stage results and final rank | `CocktailSpriteLibrary.cs`, `Resources/Cocktails` | Implemented |
| Intoxication effects | HUD, timed movement slowdown and sprite sway | `IntoxicationStatusController.cs`, `IntoxicationHudView.cs` | Implemented |
| Bar interior | Generated room, player, camera, counter station and exit | `BarInteriorRoot.cs` | Implemented |
| Automated tests | Determinism, cocktail rules/offers/session, state persistence, presentation and full round trip | `Assets/Tests` | Implemented |

Primary intended flow:

```text
seed -> layout data -> validation -> world builder
                               -> road navigation -> player
                               -> route planner -> city map
                               -> night fixture plan -> lamps/signals
player + lamp anchors -> bounded light pool -> light halos
player + seed -> player-following local fog field
player -> interaction -> scene transition <-> session state
                    -> cocktail minigame -> cocktail rules/offers
                                           -> served progress -> session state
                                           -> deferred Wasted -> intoxication effects
scene root -> matching scene-local music player -> Single transition stops it
```
