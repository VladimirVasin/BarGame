# Systems map

| System | Responsibility | Depends on | Status |
| --- | --- | --- | --- |
| Unity URP foundation | Rendering profiles, scenes, PC renderer settings | Unity 6000.5.5f1, URP 17.5.0 | Current |
| PS1 world composite | Footprint-average the final post-processed Game-camera image to `640x360` by default (lower `426x240`/`320x180` options), blend 35% perceptual-space RGB555 without screen-space dithering and point-upscale it | `Runtime/Rendering`, `Ps1PresentationProfile`, `Ps1Composite` material/shader, `PC_Renderer.asset` | Implemented |
| City composition | Exact-scene bootstrap and runtime roots | `Runtime/Core`, scene IDs | Implemented |
| City layout model | Seeded roads, lots, buildings and bar descriptors | `Runtime/World/CityLayout*.cs` | Implemented |
| City world builder | Construct night-toned roads, buildings, varied windows, bar approaches and distinct bar facade markers; reuse one flat-shaded 8-sided cylinder mesh for cylindrical props | `CityWorldBuilder.cs`, `BarBuildingMarker.cs`, primitive factory | Implemented |
| Road-edge fences | Derive the exposed perimeter of the road-rectangle union, remove every bar's `3.30 m` entrance interval, then batch collider-free rails and inset posts into two generated meshes | `RoadFencePlanner`, `RoadFencePlan`, `RoadFenceWorldBuilder`, `BarEntranceGeometry`, primitive factory | Implemented |
| Noir city presentation | Apply readable dense City-only fog and post-processing with hard directional shadows and camera MSAA disabled; follow the player with a 36-particle fog field; build depth-tested halos; plan lamps/signals; pool at most 12 shadowless spot/point lights | `RuntimeSceneSetup`, `CityNightFixture*`, `CityNightWorld*`, `CityNightAtmosphere`, `CityFogField`, `CityLightHalo`, shared emissive/atmosphere resources | Implemented |
| Scene music | Loop `city_theme` only in City and `bar_theme` only in BarInterior through a mild low-pass filter; stop each scene-local player on Single-mode transition | `SceneMusicPlayer`, `CityMusicPlayer`, `BarMusicPlayer`, `Resources/Audio/{CityMusic,BarMusic}` | Implemented |
| Retro SFX and ambience | Generate deterministic mono `22050 Hz` UI/world/bar effects, enforce category pools, cooldowns and voice limits, and run a quiet procedural ambience local to each scene | `RetroSfx`, `RetroAudioService`, `RetroAmbience`, scene roots and gameplay callers | Implemented |
| Road navigation | Constrain a circular player to the union of road/apron rectangles | `RoadWalkableArea.cs`, player motor | Implemented |
| City route planning | Build deterministic ordered shortest paths over generated road edges | `Runtime/Map`, `CityLayout` | Implemented |
| Player motor | Camera-relative keyboard/gamepad movement; face the last actual movement direction and preserve it while idle | Input System, CharacterController | Implemented |
| Third-person camera | Keep closer exterior/interior perspective framing; damp focus with bounded lag and teleport snapping; layer subtle deterministic idle/walk motion over free yaw orbit; constrain the final pose against obstacles and fade cinematic motion during modal UI without rotating the player | `PlayerCameraFollow.cs`, `BarMinigameModalLock`, Input System, physics | Implemented |
| Sprite presentation | Select one of eight unique views from the player-camera angle with 5-degree hysteresis; compose body plus eight upper/lower limb layers; project contralateral walking into the active sagittal plane; add readable breathing, weight shift and alternating left/right idle gestures; swap stronger blink plus watchful/tense variants through the existing body renderer while gating the latter two to sustained idle; preserve whole-puppet Wasted sway without mirroring | `Runtime/Player`, `Resources/Player/PlayerDirectional{,Parts,BodyExpressions}Atlas.png` | Implemented |
| Interaction/UI | Select nearby bars, exits and generic activity stations; show localized prompts and use one shared modal input lock through the crisp retro theme | `Runtime/Interaction`, `Runtime/UI`, `RetroUiTheme` | Implemented |
| Bar minigame catalog | Own one explicit ordered set of activity IDs, localized labels/prompts and factories used by both normal interiors and debug launches; expose a registered future definition without changing the debug window | `BarMinigameCatalog`, `BarMinigameDefinition`, `IBarMinigame` | Implemented |
| F9 minigame debug window | List every registered game in `City` and `BarInterior`, close a conflicting map or minigame before taking the modal lock, and launch an isolated instance that does not persist drinking state or complete a bar visit | `MinigameDebugWindow`, `BarMinigameCatalog`, `BarMinigameModalLock`, scene roots | Implemented |
| Bar activity routing | Assign the stable second row-major bar to beer pong and the other generated bars to cocktails, preserve that activity through the scene transition, then resolve its registered factory | `BarActivityKind`, `BarMinigameCatalog`, `BuildingLot`, `BarEntrance`, `GameSessionState` | Implemented |
| City map UI | Display roads/player/bars, green completed visits with a count, and edit a separately badged ordered itinerary on a logical `640x360` retro canvas | `CityMapController.cs`, `CityMapView.cs`, `RetroUiTheme`, Input System | Implemented |
| Scene transition | Guarded async city/interior loads | `SceneTransitionService.cs` | Implemented |
| Session state | Preserve seed, active bar/activity, ordered route, visited-bar set, return contract, intoxication, last alcohol and consumed-drink count for the current run | `GameSessionState.cs` | Implemented |
| Cocktail domain | Define four bases and ingredient compatibility; generate a deterministic seven-item shelf with four compatible additions and three traps; score and advance a three-round session | `Runtime/Cocktails`, persisted `DrinkId` values | Implemented |
| Cocktail minigame | Run same-scene modal base/addition/serve input, commit drinking state after every serving, report completion through the shared minigame contract, and defer a pending `Wasted` effect until finish/close | `CocktailMinigameController.cs`, `CocktailMinigameView.cs`, `IBarMinigame` | Implemented |
| Cocktail presentation | Slice a 4x4 pixel-art atlas into IMGUI UV cells and animate ingredient travel/tilt, pouring, glass fill, success sparks, bad bubbles, shake, stage results and final rank in a responsive retro layout | `CocktailSpriteLibrary.cs`, `CocktailMinigameView.cs`, `RetroUiTheme`, `Resources/Cocktails` | Implemented |
| Beer-pong domain | Simulate an aimed ball at fixed 120 Hz with swept table/cup-mouth contacts, rim and table restitution, six standing cups, ten throws, clean/bank scoring, early-clear bonus and terminal outcomes | `Runtime/BeerPong/BeerPong{Types,TableLayout,Physics,Session}.cs` | Implemented |
| Beer-pong minigame | Run a modal 2D second-bar activity, project physical x/y/z coordinates onto the table backdrop, persist a light-beer penalty after each miss and report terminal completion to the interior root | `BeerPongMinigameController`, `BeerPongMinigameView`, `BeerPongProjection` | Implemented |
| Beer-pong presentation | Draw the point-filtered 640x360 table and 4x4 ball/hand/cup/effect atlas, projected shadows, cup reactions, compact aim/power feedback and localized results | `BeerPongSpriteLibrary`, `Resources/BeerPong`, `RetroUiTheme` | Implemented |
| Intoxication effects | HUD, timed movement slowdown and sprite sway | `IntoxicationStatusController.cs`, `IntoxicationHudView.cs` | Implemented |
| Bar interior | Generated room, player, camera and exit with activity-specific cocktail-counter or long beer-pong-table station | `BarInteriorRoot.cs` | Implemented |
| Automated tests | Determinism, minigame catalog/factories, isolated F9 launch and modal restoration, cocktail/beer-pong behavior, state persistence, retro UI/audio, PS1 GPU presentation and full round trip | `Assets/Tests` | Implemented |

Primary intended flow:

```text
seed -> layout data -> validation -> world builder
                               -> road navigation -> player
                               -> route planner -> city map
                               -> road-union perimeter -> entrance-aware fences
                               -> night fixture plan -> lamps/signals
player + lamp anchors -> bounded light pool -> light halos
player + seed -> player-following local fog field
player -> interaction -> activity-aware scene transition <-> session state
                    -> generic bar station -> minigame catalog
                                           -> cocktail rules/offers
                                           or beer-pong physics/session
                                           -> drinking progress -> session state
                                           -> completed visit -> city map
                                           -> intoxication effects
F9 -> close conflicting modal -> minigame catalog -> isolated debug instance
scene root -> matching scene-local music player -> Single transition stops it
           -> matching procedural ambience player
gameplay/UI events -> RetroAudioService -> bounded category source pools
URP post-processing -> averaged low-resolution image -> subtle RGB555 blend -> point upscale
world composite -> crisp retro IMGUI overlay
```
