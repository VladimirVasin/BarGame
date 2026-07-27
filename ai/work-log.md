# Work log

Entries are reverse chronological. Record outcomes and verification, not a transcript.

## 2026-07-28 — F9 minigame debug launcher

- Added one localized retro debug window to `City` and `BarInterior`; `F9`
  opens it and launches the registered cocktail or beer-pong activity directly.
- Added one explicit `BarMinigameCatalog` for normal and debug construction.
  A future game appears after registering its unique definition and factory.
- Opening the window closes a conflicting map or minigame before taking the
  state-preserving modal lock, and closing restores the captured player,
  camera and HUD state.
- Debug instances use fresh transient drinking state, do not mark a bar
  visited and do not persist intoxication, drinks or `Wasted`.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 181/181 passed.
- Unity PlayMode: 24/24 passed, including a real Input System F9 press,
  both isolated debug launches and the complete scene flow.
- Windows x64 Player build: succeeded, 0 warnings, 129,113,951 bytes.

## 2026-07-28 — Second-bar beer pong

- Added stable per-bar activities: the second row-major city bar opens beer
  pong while the other two retain the cocktail mixer.
- Replaced the counter-only coupling with one `IBarMinigame` contract, shared
  modal lock and activity station; the generated interior now builds only the
  matching game and furniture.
- Added a deterministic six-cup, ten-throw session and fixed `120 Hz` 2.5D
  simulation with swept table/cup-mouth contacts, table/rim bounces, settlement,
  timeout and out-of-bounds results.
- Added clean/bank/early-clear scoring. Every miss consumes a light beer,
  adds 8 intoxication and commits the drinking state immediately.
- Added a point-filtered 640x360 beer-pong backdrop, 4x4 ball/hand/cup/effect
  atlas, projected flight/shadow/cup feedback, discrete aiming/power UI and
  dedicated generated throw, bounce, rim and sink SFX.
- Completion marks the captured bar ID visited and removes it from the route;
  cancellation restores prior input/HUD state without completing the visit.

Verification:

- Complete .NET solution build: 0 errors, 0 warnings.
- Unity EditMode: 177/177 passed.
- Unity PlayMode: 21/21 passed.
- Windows x64 Player build: succeeded, 0 warnings, 129,102,372 bytes.

## 2026-07-28 — Completed-bar map progress

- Added a current-city visited-bar set to `GameSessionState`, preserved across
  scene transitions and reset together with the route when the seed changes.
- Moved visit completion from bar-interior loading to the cocktail
  minigame's accepted final result. Entering, cancelling or leaving early no
  longer removes the bar from the route.
- Added persistent green numbered markers and a visited counter to the city
  map; amber corner badges now carry route order independently.
- Added regression coverage for idempotent visit persistence, final-result
  completion, cancellation and an unfinished bar round trip.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 141/141 passed.
- Unity PlayMode: 20/20 passed.

## 2026-07-28 — PS1 tonal response correction

- Moved RGB555 quantization from linear-light values to perceptual sRGB space,
  then blended it at 35% instead of replacing the source color.
- Preserved the original HDR delta and restored continuous dark fog, shadow
  detail and lamp falloff while keeping the low-resolution pixel structure.
- Strengthened the GPU test with a one-pixel checker plus exact dark and bright
  tone fields, isolating the test camera from scene fog and post-processing.

Verification:

- Unity EditMode: 141/141 passed.
- Unity PlayMode: 20/20 passed.
- Windows x64 Player build: succeeded, 0 warnings, 124,166,277 bytes.

## 2026-07-28 — PS1 readability and map projection correction

- Raised the default internal world frame from `320x180` to `640x360`, removed
  the visible screen-space Bayer grid and added four-tap footprint averaging
  before RGB555 quantization.
- Kept point upscaling and lower-resolution presets available, but made the
  default output substantially cleaner at 720p and 1080p.
- Replaced the city map's nested `GUIUtility.RotateAroundPivot` calls with one
  logical line transform composed under the retro canvas matrix.
- Restored road and route alignment and replaced the player's short heading
  line with a clear chevron arrow.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 141/141 passed.
- Unity PlayMode: 20/20 passed, including the updated D3D composite test.
- Windows x64 Player build: succeeded, 0 warnings, 124,165,413 bytes.

## 2026-07-28 — PS1-inspired presentation and audio

- Added a PC RenderGraph world composite with a default `320x180` internal
  frame, RGB555 quantization, stable 4x4 Bayer dithering and point upscaling;
  retained `426x240` as an optional readability preset.
- Restyled the interaction prompt, intoxication HUD, city map and cocktail
  minigame with one burgundy/amber retro theme while keeping the UI crisp
  above the pixelated world.
- Replaced smooth runtime cylinder visuals with one shared flat-shaded
  8-sided mesh, enabled hard directional shadows and disabled camera MSAA.
- Added deterministic generated `22050 Hz` retro SFX, bounded source pools,
  cooldowns, scene-local city/bar ambience and mild filtering for the existing
  correctly routed music themes.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 137/137 passed.
- Unity PlayMode: 20/20 passed, including the D3D GPU composite smoke test.
- Windows x64 Player build: succeeded, 0 warnings, 124,164,821 bytes.

## 2026-07-28 — Cocktail glass liquid polish

- Realigned the procedural liquid to the transparent inner cavity of the
  pixel-art glass instead of drawing it across the glass frame.
- Replaced the flat glowing rectangle with darker pixel rows, a stepped
  meniscus, tapered lower rows and restrained animated highlights.
- Capped the visible fill below the rim and kept the glass sprite as the top
  layer so its outline and reflections mask the liquid naturally.

Verification:

- Runtime .NET build: 0 errors, 0 warnings.
- The open Unity Editor completed its domain reload without compiler errors.

## 2026-07-27 — Three-cocktail mixing minigame

- Replaced the five-pick drink-chain game with a same-scene modal cocktail
  mixer launched at the counter edge.
- Added three rounds with a beer/wine/vodka/cognac base, 2–4 unique additions,
  and a deterministic seven-item shelf containing four compatible choices and
  three traps.
- Added per-round scoring up to 100, a 300-point session maximum and a
  15-point penalty for every incompatible addition.
- Committed intoxication, last alcohol and the served-cocktail count through
  `GameSessionState` after each serving; bad served mixtures defer the
  45-second `Wasted` effect until finish/close, and 100 intoxication ends early.
- Added a 4x4 pixel-art glass/ingredient atlas plus animated glass fill,
  ingredient travel/tilt, pouring, sparks, bad bubbles, shaking, stage progress
  and final rank.
- Added mouse, keyboard and gamepad controls under the existing modal input
  lock, and replaced the old minigame text with RU/EN cocktail keys.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 106/106 passed.
- Unity PlayMode: 15/15 passed.
- Windows x64 Player build: succeeded, 0 warnings, 124,131,406 bytes.

## 2026-07-27 — Correct scene music routing

- Assigned `city_theme` exclusively to `Resources/Audio/CityMusic` and
  `bar_theme` exclusively to `Resources/Audio/BarMusic`.
- Kept both looping players under their matching scene roots, so Single-mode
  transitions stop the previous scene's theme automatically.

Verification:

- The open Unity Editor imported both resource slots and compiled Runtime,
  Editor, EditModeTests and PlayModeTests successfully with no C# errors.
- Resource layout contains only `city_theme` in `CityMusic` and only
  `bar_theme` in `BarMusic`.

## 2026-07-27 — Fog-forward readability pass

- Lifted the exterior palette, ambient/moon contribution and shadow floor while
  changing the city fog to a denser luminous gray-green.
- Added one seeded, player-following `CityFogField` capped at 36 slow
  world-space particles.
- Added a shared depth-tested atmosphere shader and two-layer `CityLightHalo`
  effects for pooled street lights, bar lights and active amber signals.
- Changed the street-light pool to directed spot lights while keeping bar
  entrances on point lights and the complete realtime-light budget bounded.
- Retuned the City-only Bloom, ColorAdjustments, Vignette and FilmGrain profile;
  the bar interior remains free of exterior fog and halo objects.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests assemblies:
  0 errors, 0 warnings.
- Unity EditMode: 82/82 passed.
- Unity PlayMode: 14/14 passed.
- Manual 1280x720 chase-camera renders checked forward and side street
  readability, distance fog and depth-tested light diffusion.
- Windows x64 Player build: succeeded, 0 warnings, 117,808,526 bytes.

## 2026-07-27 — Scene-local city music slot

- Added `Resources/Audio/CityMusic` as the documented drop folder for a WAV,
  OGG or MP3 exterior theme.
- Added a deterministic non-spatial looping player under `CityGameRoot`.
- Kept the player scene-local, so loading `BarInterior` destroys it and stops
  exterior music without persistent audio state.

## 2026-07-27 — Noir city night

- Reworked the generated exterior into a fixed blue-black night with
  exponential fog, cold moonlight, varied dark/cool windows and a dedicated
  ACES/Bloom/ColorAdjustments/Vignette profile.
- Added deterministic collider-free street lamps and seed-phased amber
  traffic signals derived from the road graph.
- Added one shared HDR emissive material and capped the complete city at 12
  shadowless realtime point lights, prioritizing nearby lamps and bar entries.
- Kept bar interiors fog-free and preserved map, navigation, interaction and
  third-person camera behavior.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests assemblies:
  0 errors, 0 warnings.
- Unity EditMode: 82/82 passed.
- Unity PlayMode: 14/14 passed.
- Manual rendered checks: spawn street, bar facade and lit traffic signal.
- Windows x64 Player build: succeeded, 0 warnings, 105,759,742 bytes.

## 2026-07-27 — City map and bar itinerary

- Added a full-screen schematic city map with roads, blocks, player direction
  and stable numbered bar markers.
- Added ordered add/remove/reorder controls for mouse, keyboard and gamepad,
  with modal player/camera locking and a persistent map-open hint.
- Added deterministic weighted road-graph routing from the player through all
  selected bars, including session persistence and visited-stop removal.
- Added RU/EN strings plus EditMode and PlayMode regression coverage.

Verification:

- All solution assemblies: 0 errors, 0 warnings.
- Unity EditMode: 75/75 passed.
- Unity PlayMode: 12/12 passed.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Visible bar landmarks

- Added one shared procedural pixel mug sign to every generated bar and kept
  it upright and camera-facing through the existing billboard behavior.
- Added warm amber bar windows plus collider-free gold door frames and
  entrance canopies.
- Kept city layout, road navigation, entrance triggers and scene flow unchanged.
- Added PlayMode coverage for bar/marker identity, sprite sharing contract,
  ordinary-building exclusion, decorative colliders and camera yaw.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests assemblies:
  0 errors, 0 warnings.
- Unity EditMode: 63/63 passed.
- Unity PlayMode: 11/11 passed.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Counter drink minigame

- Added a counter-edge interaction station and an in-scene modal drink game
  with four cycling offers and five selections.
- Added a pure drink catalog, compatibility rules, deterministic offers,
  one-use water reset and completed/bad-mix outcomes.
- Persisted intoxication, last alcohol and total drink count across city/bar
  scene loads for the current application run.
- Added the 45-second `Wasted` debuff with `0.75` movement speed, sprite sway,
  localized HUD and result feedback.
- Added RU/EN catalog entries plus EditMode and PlayMode regression coverage.

Verification:

- Runtime, EditModeTests and PlayModeTests assemblies: 0 errors, 0 warnings.
- Unity EditMode: 63/63 passed.
- Unity PlayMode: 10/10 passed, including minigame outcomes and scene persistence.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Third-person chase camera

- Replaced the fixed orthographic/isometric view with a perspective camera
  directly behind the player's camera-relative heading.
- Added right-mouse/right-stick yaw and sphere-cast obstacle avoidance for
  both city and bar interior distances.
- Added a PlayMode regression check for perspective projection and behind-player
  alignment.

Verification:

- Runtime and PlayMode test assemblies: 0 errors, 0 warnings.
- Unity PlayMode: 8/8 passed, including yaw/collision camera regressions and
  the complete `City -> BarInterior -> City` cycle.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Playable MVP vertical slice

- Added deterministic data-first city generation with a connected road graph,
  building lots, stable bars and validated frontage return points.
- Added runtime world construction, road/apron navigation, a modular 13-part
  sprite player, camera-relative movement and billboard presentation.
- Added nearby interaction, RU/EN catalogs, guarded scene transitions,
  persistent seed/bar context and a generated shared bar interior.
- Replaced the build scene with `City`, added `BarInterior`, runtime/editor/test
  assemblies, and a Windows build helper.
- Added deterministic EditMode tests and PlayMode presentation/round-trip smoke
  tests.

Verification:

- `dotnet build` for Runtime, Editor, EditModeTests and PlayModeTests:
  0 errors, 0 warnings.
- Unity EditMode: 15/15 passed.
- Unity PlayMode: 6/6 passed, including `City -> BarInterior -> City`.
- Windows x64 Player build: succeeded, 0 warnings.

## 2026-07-27 — Repository foundation

- Initialized an empty Git repository.
- Added Unity-oriented ignore and attribute rules.
- Added repository instructions and the initial AI memory set.
- Inspected Unity version, packages, build scenes, and stock assets.
- Confirmed that the Bar Promenade MVP remains planned and has not yet been implemented.

Verification: documentation paths and baseline facts were checked against `ProjectVersion.txt`, `Packages/manifest.json`, `EditorBuildSettings.asset`, and the current `Assets` tree.
