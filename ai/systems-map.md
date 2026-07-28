# Systems map

| System | Responsibility | Depends on | Status |
| --- | --- | --- | --- |
| Unity URP foundation | Rendering profiles, scenes, PC renderer settings | Unity 6000.5.5f1, URP 17.5.0 | Current |
| PS1 world composite | Footprint-average the final post-processed Game-camera image to `640x360` by default (lower `426x240`/`320x180` options), blend 35% perceptual-space RGB555 without screen-space dithering and point-upscale it | `Runtime/Rendering`, `Ps1PresentationProfile`, `Ps1Composite` material/shader, `PC_Renderer.asset` | Implemented |
| City composition | Exact-scene bootstrap and runtime roots | `Runtime/Core`, scene IDs | Implemented |
| City layout model | Seeded roads, lots, buildings and bar descriptors | `Runtime/World/CityLayout*.cs` | Implemented |
| City world builder | Construct night-toned roads, buildings, varied windows, bar approaches and distinct bar facade markers; reuse one flat-shaded 8-sided cylinder mesh for cylindrical props | `CityWorldBuilder.cs`, `BarBuildingMarker.cs`, primitive factory | Implemented |
| Road-edge fences | Derive the exposed perimeter of the road-rectangle union, remove every bar's `3.30 m` entrance interval, then batch collider-free rails and inset posts into two generated meshes | `RoadFencePlanner`, `RoadFencePlan`, `RoadFenceWorldBuilder`, `BarEntranceGeometry`, primitive factory | Implemented |
| Noir city presentation | Limit City visibility with `0.070` exponential-squared fog, a matching terminal camera backdrop and a `48 m` far clip while keeping BarInterior clear at `220 m`; follow the player with a more visible 36-particle fog field; apply City-only post-processing, hard directional shadows and no camera MSAA; build depth-tested halos; plan lamps/signals; pool at most 12 shadowless spot/point lights | `RuntimeSceneSetup`, `CityNightFixture*`, `CityNightWorld*`, `CityNightAtmosphere`, `CityFogField`, `CityLightHalo`, shared emissive/atmosphere resources | Implemented |
| Scene music | Loop `city_theme` only in City and `bar_theme` only in BarInterior through a mild low-pass filter; stop each scene-local player on Single-mode transition | `SceneMusicPlayer`, `CityMusicPlayer`, `BarMusicPlayer`, `Resources/Audio/{CityMusic,BarMusic}` | Implemented |
| Retro SFX and ambience | Generate deterministic mono `22050 Hz` UI/world/bar effects, including tincture swap/match/moonshine cues; enforce category pools, cooldowns and voice limits; run a quiet procedural ambience local to each scene | `RetroSfx`, `RetroAudioService`, `RetroAmbience`, scene roots and gameplay callers | Implemented |
| Road navigation | Constrain a circular player to the union of road/apron rectangles | `RoadWalkableArea.cs`, player motor | Implemented |
| City route planning | Build deterministic ordered shortest paths over generated road edges | `Runtime/Map`, `CityLayout` | Implemented |
| Player motor | Accelerate camera-relative keyboard/gamepad movement to `5.2 m/s` at `6.5 m/s²`, brake at `11 m/s²`, feed constrained displacement back into velocity, preserve heading while idle and reserve immediate planar stops for modal/transition/teleport boundaries | Input System, CharacterController, `IWalkableArea` | Implemented |
| Third-person camera | Keep very close `2.6 m / 53°` exterior and `2.2 m / 57°` interior framing; use weighty damped yaw/focus with bounded lag and teleport snapping; layer subtle deterministic idle/walk motion over free yaw orbit; shorten immediately against obstacles, recover outward smoothly and fade cinematic motion during modal UI without rotating the player | `PlayerCameraFollow.cs`, `BarMinigameModalLock`, Input System, physics | Implemented |
| Sprite presentation | Select one of eight unique views from the player-camera angle with 5-degree hysteresis; compose body plus eight upper/lower limb layers; drive a slower-settling contralateral gait from actual distance travelled; project it into the active sagittal plane; anchor the lower atlas-derived foot contact, compress the upper body at footfall and keep idle breathing off the feet; preserve depth sorting, expressions and Wasted sway without mirroring | `Runtime/Player`, `Resources/Player/PlayerDirectional{,Parts,BodyExpressions}Atlas.png` | Implemented |
| Player shadows | Cast one camera-independent nine-part alpha-clipped puppet toward the main directional light, remap live gait joints into its light-relative authored view and follow whole-puppet pose motion; also draw one shared analytic contact quad at the grounded actor root, independent of light state and puppet bob, without enabling shadows on practical lights | `PlayerDynamicShadow`, `PlayerContactShadow`, `PlayerShadowResources`, `Player{SpriteShadowCaster,ContactShadow}.shader`, main directional light | Implemented |
| Interaction/UI | Select nearby bars, exits and generic activity stations; show localized prompts and use one shared modal input lock through the crisp retro theme | `Runtime/Interaction`, `Runtime/UI`, `RetroUiTheme` | Implemented |
| Bar minigame catalog | Own one explicit ordered set of activity IDs, localized labels/prompts and factories used by both normal interiors and debug launches; currently register cocktail mixing, beer pong, Split the G and Tinctures in a Row without game-specific debug-window code | `BarMinigameCatalog`, `BarMinigameDefinition`, `IBarMinigame` | Implemented |
| F9 minigame debug window | List every registered game in `City` and `BarInterior`, close a conflicting map or minigame before taking the modal lock, and launch an isolated instance that does not persist drinking state or complete a bar visit | `MinigameDebugWindow`, `BarMinigameCatalog`, `BarMinigameModalLock`, scene roots | Implemented |
| Bar activity routing | Assign the first four stable row-major bars to cocktail mixing, beer pong, Split the G and Tinctures in a Row through one pure resolver, fall back to cocktails for later bars, preserve the activity through transition, then resolve its registered factory | `BarActivityKind`, `BarActivityAssignment`, `BarMinigameCatalog`, `BuildingLot`, `BarEntrance`, `GameSessionState` | Implemented |
| City map UI | Display roads/player/bars, green completed visits with a count, and edit a separately badged ordered itinerary on a logical `640x360` retro canvas | `CityMapController.cs`, `CityMapView.cs`, `RetroUiTheme`, Input System | Implemented |
| Scene transition | Guarded async city/interior loads | `SceneTransitionService.cs` | Implemented |
| Session state | Preserve seed, active bar/activity, ordered route, visited-bar set, return contract, intoxication, last alcohol and consumed-drink count for the current run | `GameSessionState.cs` | Implemented |
| Cocktail domain | Define four bases and ingredient compatibility; generate a deterministic seven-item shelf with four compatible additions and three traps; score and advance a three-round session | `Runtime/Cocktails`, persisted `DrinkId` values | Implemented |
| Cocktail minigame | Run same-scene modal base/addition/serve input, commit drinking state after every serving, report completion through the shared minigame contract, and defer a pending `Wasted` effect until finish/close | `CocktailMinigameController.cs`, `CocktailMinigameView.cs`, `IBarMinigame` | Implemented |
| Cocktail presentation | Slice a 4x4 pixel-art atlas into IMGUI UV cells and animate ingredient travel/tilt, pouring, glass fill, success sparks, bad bubbles, shake, stage results and final rank in a responsive retro layout | `CocktailSpriteLibrary.cs`, `CocktailMinigameView.cs`, `RetroUiTheme`, `Resources/Cocktails` | Implemented |
| Beer-pong domain | Simulate an aimed ball at fixed 120 Hz with swept table/cup-mouth contacts, rim and table restitution, six standing cups, ten throws, clean/bank scoring, early-clear bonus and terminal outcomes | `Runtime/BeerPong/BeerPong{Types,TableLayout,Physics,Session}.cs` | Implemented |
| Beer-pong minigame | Run a modal 2D second-bar activity, project physical x/y/z coordinates onto the table backdrop, persist a light-beer penalty after each miss and report terminal completion to the interior root | `BeerPongMinigameController`, `BeerPongMinigameView`, `BeerPongProjection` | Implemented |
| Beer-pong presentation | Draw the point-filtered 640x360 table and 4x4 ball/hand/cup/effect atlas, projected shadows, cup reactions, compact aim/power feedback and localized results | `BeerPongSpriteLibrary`, `Resources/BeerPong`, `RetroUiTheme` | Implemented |
| Split the G domain | Advance a normalized pint from countdown through one irreversible held sip, settling and scored result; preserve frame-chunk invariance, five tolerance bands, up to three fresh glasses and the best session result | `SplitTheGSettings`, `SplitTheGSession`, `SplitTheGScoring` | Implemented |
| Split the G minigame | Run the modal third-bar hold/release activity for Space, LMB and gamepad South; hide the exact level while drinking/settling, persist the actual dark-beer fraction immediately and complete on Continue, glass three or maximum intoxication | `SplitTheGMinigameController`, `BarMinigameModalLock`, `GameSessionState` | Implemented |
| Split the G presentation | Draw a point-filtered 640x360 bar backdrop and 4x4 pint/hand/foam/effect atlas with glass tilt, target line, obscured liquid, settling foam, localized result cards and generated gulp SFX | `SplitTheGMinigameView`, `SplitTheGSpriteLibrary`, `Resources/SplitTheG`, `RetroUiTheme`, `RetroSfx` | Implemented |
| Tincture-match domain | Generate a seeded match-free `7x7` board with five normal flavors, at least three legal swaps and at most one `XXX`; resolve unique clears, gravity, refills, cascades up to `x5`, 15 accepted moves and deterministic dead-board reshuffles | `Runtime/TinctureMatch`, `TinctureMatchGenerator`, `TinctureMatchResolver`, `TinctureMatchSession` | Implemented |
| Tincture-match minigame | Run the modal fourth-bar board for mouse click/drag, keyboard and gamepad; reject invalid swaps without spending a move, expose `XXX` flavor clears, and immediately persist only an activated `Moonshine` for +24 intoxication | `TinctureMatchMinigameController`, `BarMinigameModalLock`, `DrinkRules`, `GameSessionState` | Implemented |
| Tincture-match presentation | Draw the logical `640x360` view with a point-filtered backdrop and 4x4 atlas of five symbol-coded shots, literal `XXX` moonshine and swap/clear/cascade/reshuffle effects; localize RU/EN feedback and play generated swap, match and burst SFX | `TinctureMatchMinigameView`, `TinctureMatchSpriteLibrary`, `Resources/TinctureMatch`, `RetroUiTheme`, `RetroSfx` | Implemented |
| Intoxication effects | HUD, timed movement slowdown and sprite sway | `IntoxicationStatusController.cs`, `IntoxicationHudView.cs` | Implemented |
| Bar interior | Generate one shared room, player, camera and exit with an activity-specific station and decor, including the tincture tray, five shots and `XXX` display | `BarInteriorRoot.cs` | Implemented |
| Automated tests | Determinism, minigame catalog/factories, isolated F9 launch and modal restoration, cocktail/beer-pong/Split-the-G/tincture behavior, state persistence, retro UI/audio, PS1 GPU presentation and full round trip | `Assets/Tests` | Implemented |

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
                                           or Split the G timer/scoring/session
                                           or tincture board/cascades/XXX
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
