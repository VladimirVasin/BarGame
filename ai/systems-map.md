# Systems map

| System | Responsibility | Depends on | Status |
| --- | --- | --- | --- |
| Unity URP foundation | Rendering profiles, scenes, PC renderer settings | Unity 6000.5.5f1, URP 17.5.0 | Current |
| PS1 world composite | Footprint-average the final post-processed Game-camera image to `640x360` by default (lower `426x240`/`320x180` options), blend 35% perceptual-space RGB555 without screen-space dithering and point-upscale it | `Runtime/Rendering`, `Ps1PresentationProfile`, `Ps1Composite` material/shader, `PC_Renderer.asset` | Implemented |
| City composition | Exact-scene bootstrap and runtime roots | `Runtime/Core`, scene IDs | Implemented |
| City layout model | Seeded roads, lots, buildings and bar descriptors | `Runtime/World/CityLayout*.cs` | Implemented |
| City world builder | Construct night-toned roads, buildings, varied windows, bar approaches and distinct bar facade markers; reuse one flat-shaded 8-sided cylinder mesh for cylindrical props | `CityWorldBuilder.cs`, `BarBuildingMarker.cs`, primitive factory | Implemented |
| Noir city presentation | Apply readable dense City-only fog and post-processing with hard directional shadows and camera MSAA disabled; follow the player with a 36-particle fog field; build depth-tested halos; plan lamps/signals; pool at most 12 shadowless spot/point lights | `RuntimeSceneSetup`, `CityNightFixture*`, `CityNightWorld*`, `CityNightAtmosphere`, `CityFogField`, `CityLightHalo`, shared emissive/atmosphere resources | Implemented |
| Scene music | Loop `city_theme` only in City and `bar_theme` only in BarInterior through a mild low-pass filter; stop each scene-local player on Single-mode transition | `SceneMusicPlayer`, `CityMusicPlayer`, `BarMusicPlayer`, `Resources/Audio/{CityMusic,BarMusic}` | Implemented |
| Retro SFX and ambience | Generate deterministic mono `22050 Hz` UI/world/bar effects, enforce category pools, cooldowns and voice limits, and run a quiet procedural ambience local to each scene | `RetroSfx`, `RetroAudioService`, `RetroAmbience`, scene roots and gameplay callers | Implemented |
| Road navigation | Constrain a circular player to the union of road/apron rectangles | `RoadWalkableArea.cs`, player motor | Implemented |
| City route planning | Build deterministic ordered shortest paths over generated road edges | `Runtime/Map`, `CityLayout` | Implemented |
| Player motor | Camera-relative keyboard/gamepad movement | Input System, CharacterController | Implemented |
| Third-person camera | Perspective chase framing, yaw input and obstacle avoidance | `PlayerCameraFollow.cs`, Input System, physics | Implemented |
| Sprite presentation | Billboard and procedural 13-part walk pose | `Runtime/Player` | Implemented |
| Interaction/UI | Select nearby bars, exits and counter stations; show localized prompts through the crisp retro theme | `Runtime/Interaction`, `Runtime/UI`, `RetroUiTheme` | Implemented |
| City map UI | Display roads/player/bars, green completed visits with a count, and edit a separately badged ordered itinerary on a logical `640x360` retro canvas | `CityMapController.cs`, `CityMapView.cs`, `RetroUiTheme`, Input System | Implemented |
| Scene transition | Guarded async city/interior loads | `SceneTransitionService.cs` | Implemented |
| Session state | Preserve seed, active bar, ordered route, visited-bar set, return contract, intoxication, last alcohol and served-cocktail count for the current run | `GameSessionState.cs` | Implemented |
| Cocktail domain | Define four bases and ingredient compatibility; generate a deterministic seven-item shelf with four compatible additions and three traps; score and advance a three-round session | `Runtime/Cocktails`, persisted `DrinkId` values | Implemented |
| Cocktail minigame | Run same-scene modal base/addition/serve input, commit drinking state after every serving, mark the bar visited only when the final result is accepted, and defer a pending `Wasted` effect until finish/close | `CocktailMinigameController.cs`, `CocktailMinigameView.cs` | Implemented |
| Cocktail presentation | Slice a 4x4 pixel-art atlas into IMGUI UV cells and animate ingredient travel/tilt, pouring, glass fill, success sparks, bad bubbles, shake, stage results and final rank in a responsive retro layout | `CocktailSpriteLibrary.cs`, `CocktailMinigameView.cs`, `RetroUiTheme`, `Resources/Cocktails` | Implemented |
| Intoxication effects | HUD, timed movement slowdown and sprite sway | `IntoxicationStatusController.cs`, `IntoxicationHudView.cs` | Implemented |
| Bar interior | Generated room, player, camera, counter station and exit | `BarInteriorRoot.cs` | Implemented |
| Automated tests | Determinism, cocktail rules/offers/session, state persistence, retro UI/audio, PS1 GPU presentation and full round trip | `Assets/Tests` | Implemented |

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
                                           -> completed visit -> city map
                                           -> deferred Wasted -> intoxication effects
scene root -> matching scene-local music player -> Single transition stops it
           -> matching procedural ambience player
gameplay/UI events -> RetroAudioService -> bounded category source pools
URP post-processing -> averaged low-resolution image -> subtle RGB555 blend -> point upscale
world composite -> crisp retro IMGUI overlay
```
