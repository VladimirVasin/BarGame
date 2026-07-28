# Work log

Entries are reverse chronological. Record outcomes and verification, not a transcript.

## 2026-07-28 — Much closer, weightier chase camera

- Reduced the centered exterior camera arm from `3.6 m` to `2.6 m` and the
  interior arm from `2.7 m` to `2.2 m`, preserving the existing `53° / 57°`
  FOV pair and complete full-body composition in both runtime scenes.
- Increased orbit yaw, target focus, outward obstacle recovery, cinematic
  blend and movement-response damping for a heavier, smoother feel. Focus lag
  remains capped at `0.45 m`.
- Preserved immediate inward obstacle avoidance, teleport snapping, stable
  requested yaw/FOV and camera-independent player heading.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused Unity PlayMode player/camera presentation: 14/14 passed.
- D3D11 City/BarInterior framing capture: 1/1 passed; the hero remains fully
  visible in both review frames.
- Windows x64 Player build: succeeded, 0 warnings, 135,663,015 bytes.

## 2026-07-28 — Camera-independent dynamic player shadow

- Added one collider-free `ShadowsOnly` sprite proxy to every runtime player
  in City and BarInterior. It reuses the existing eight-direction full-body
  atlas, selects a silhouette from the player/main-light angle and faces the
  main directional light instead of the orbiting camera.
- Copied whole-puppet bob, weight shift and `Wasted` sway to the shadow proxy
  while leaving the nine visible jointed renderers and their materials
  unchanged.
- Added one cached shared runtime material backed by an alpha-clipped URP
  `ShadowCaster` pass. Practical street/bar lights remain shadowless, and the
  caster adds no collider, texture atlas or per-frame allocation.
- Added resource, behavior, scene-integration and graphics-device render
  checks. The render check compares the same receiver with the caster enabled
  and disabled to verify actual shadow-map darkening.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused Unity EditMode: 1/1 passed.
- Focused Unity PlayMode behavior: 1/1 passed.
- City/BarInterior PlayMode integration: 2/2 passed.
- D3D11 realtime shadow render check: 1/1 passed.
- Windows x64 Player build: succeeded, 0 warnings, 135,663,015 bytes.

## 2026-07-28 — Tinctures in a Row minigame

- Added a seeded `7x7` match-three domain with five normal infusion flavors,
  exactly one starting `XXX`, no initial matches, at least three legal normal
  swaps and 15 accepted moves. Invalid swaps preserve the board, score and
  move count.
- Added unique-cell match resolution, gravity, seeded refill, deterministic
  cascades with a multiplier capped at `x5`, long-run/intersection special
  creation and deterministic dead-board reshuffling while enforcing at most
  one `XXX`.
- Added the modal fourth-bar controller with mouse click/drag,
  keyboard and gamepad input. Normal matches represent customer orders; only
  activating `XXX` immediately commits one `Moonshine`, +24 intoxication and
  one consumed drink. Cancel cannot refund it, and F9 runs stay isolated.
- Registered `tincture-match` in the shared catalog and assigned the fourth
  stable row-major bar through the common resolver. Added the dedicated
  station, tray/shot/`XXX` decor, RU/EN UI, deterministic point-filtered
  `640x360` backdrop and 4x4 atlas, plus generated swap, match and
  moonshine-burst SFX.
- Added eased swap, gravity and refill motion, clipped board entry, cascade
  particles and reshuffle feedback. Match audio now starts with the matching
  clear animation.
- Terminal moves remain completed if the player closes during their cascade;
  a resulting 45-second `Wasted` effect starts only when the modal closes.
  Reopening advances a deterministic per-controller board sequence.

Verification:

- Focused Unity EditMode integration suite: 70/70 passed, with 0 compile
  errors or warnings.
- Runtime, Editor, EditModeTests, PlayModeTests and Assembly-CSharp .NET
  builds: 0 errors, 0 warnings.
- Unity EditMode: 302/302 passed.
- Unity PlayMode in `-nographics`: 54/54 runnable tests passed; the existing
  graphics-device-only RenderGraph test was ignored by design.
- Windows x64 Player build: succeeded, 0 warnings, 135,654,019 bytes.

## 2026-07-28 — Split the G minigame

- Added a pure frame-rate-independent one-sip session with Normal timing,
  irreversible release, foam settling, five accuracy bands, linear 0–100
  scoring, three fresh glasses and a session-best result.
- Assigned the third stable row-major bar to Split the G through one shared
  ordinal resolver used by generation and validation. Registered the activity
  in the common catalog, so normal interiors and isolated F9 launches use the
  same factory.
- Added Space, in-canvas LMB and gamepad South hold/release input with
  post-countdown fresh-press protection. The exact level stays obscured while
  drinking and settling; each non-empty sip immediately persists its actual
  dark-beer fraction and Cancel cannot refund it.
- Added the activity-specific interior station and pint display, RU/EN UI,
  deterministic `640x360` pixel-art backdrop, transparent 4x4 gameplay atlas,
  generated gulp SFX and focused domain/presentation/input/scene-flow tests.

Verification:

- Runtime, Editor, EditModeTests, PlayModeTests and Assembly-CSharp .NET
  builds: 0 errors, 0 warnings.
- Unity EditMode: 261/261 passed.
- Unity PlayMode in `-nographics`: 43/43 runnable tests passed; the existing
  graphics-device-only RenderGraph test was ignored by design.
- Windows x64 Player build: succeeded, 0 warnings, 133,861,007 bytes.

## 2026-07-28 — Expressive living idle and facial states

- Increased the readable breathing, weight-transfer and body-rock amplitudes
  at the closer camera distance. The short cuff/strap gesture now alternates
  deterministically between the left and right arms while keeping the
  direction-projected sagittal plane and depth sorting.
- Expanded the body-expression atlas from three to five rows: stronger
  half/closed blinks plus `Watchful` and `Tense`. The two new expressions use
  explicit direction-specific eye, brow and mouth edits, run only after
  sustained idle and reset during locomotion or `Wasted`; blink timing
  continues in either state.
- Kept the nine-renderer hierarchy, authored asymmetry and non-mirrored views.
  All three rear directions remain byte-identical to neutral.
- Extended regression coverage for exact atlas output, pairwise-distinct
  facial states, blink-under-motion, `Wasted` suppression, sagittal depth
  sorting and the left-then-right idle gesture sequence.

Verification:

- Deterministic expression atlas rebuild:
  SHA256 `6FDFB6744B9F74F0EFE67BC30C528B8C654ABEC3444EA815FA5F94DD034A7688`.
- Unity EditMode: 222/222 passed.
- Unity PlayMode in `-nographics`: 35/35 runnable tests passed; the existing
  graphics-device-only RenderGraph test was ignored by design.
- Runtime, EditModeTests, PlayModeTests and Assembly-CSharp .NET builds:
  0 errors, 0 warnings.
- Windows x64 Player build: succeeded, 0 warnings, 132,090,607 bytes.

## 2026-07-28 — Tighter centered chase framing

- Reduced the centered exterior camera arm from `4.6 m` to `3.6 m` and the
  interior arm from `3.3 m` to `2.7 m`.
- Kept the existing FOV, focus heights, orbit, damping, cinematic motion and
  obstacle handling unchanged; no shoulder offset was introduced.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused Unity PlayMode player/camera presentation: 13/13 passed.

## 2026-07-28 — Cinematic camera, living idle and facial blink

- Tightened exterior/interior chase framing to `4.6 m / 53°` and
  `3.3 m / 57°`, added bounded focus damping with teleport snapping, and
  layered deterministic low-frequency idle drift with speed-driven walk bob.
- Preserved immediate inward collision response, smooth outward recovery,
  player-independent orbit yaw and stable FOV. The shared modal lock now fades
  cinematic motion out and restores its captured state.
- Added procedural breathing, weight transfer and a rare left-arm fidget to
  the existing direction-projected joint pose. Idle blends away during
  locomotion and is suppressed under `Wasted` without changing scale, heading
  or the nine-renderer hierarchy.
- Extended the deterministic player builder with a `512x288` body-expression
  atlas. Neutral matches `Body`; half/closed blinks edit only explicit eye
  pixels in five visible-face directions, while three rear views remain
  unchanged. Runtime swaps the existing body sprite on a deterministic timer.

Verification:

- Unity EditMode: 218/218 passed.
- Unity PlayMode in `-nographics`: 35/35 runnable tests passed; the existing
  graphics-device-only RenderGraph test was ignored by design.
- Runtime, EditModeTests, PlayModeTests and Assembly-CSharp .NET builds:
  0 errors, 0 warnings.
- Windows x64 Player build: succeeded, 0 warnings, 131,695,855 bytes.

## 2026-07-28 — View-correct projected player gait

- Replaced the single screen-plane joint axis with a view-projected sagittal
  axis: side views retain their lateral swing, front/back views rotate in
  depth, and diagonal views combine both components.
- Made the gait phases explicit: left/right legs oppose one another and each
  arm opposes the leg on the same side.
- Added phase-aware near/far sorting so depth-projected limbs pass behind or
  in front of the torso without changing sprites or mirroring.
- Moved final pose application after direction selection in `LateUpdate` and
  retained walk bob/rock, idle settling and `Wasted` sway.
- Added PlayMode regression coverage for cardinal/diagonal projection,
  contralateral phasing and depth sorting.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused Unity PlayMode player presentation: 9/9 passed.
- Full Unity PlayMode with D3D11: 31/31 passed, including complete
  `City`/`BarInterior` scene flow.

## 2026-07-28 — Jointed eight-direction player correction

- Kept the locked eight-view character design and restored exactly 259 facial
  pixels that the original chroma-key pass had incorrectly made transparent;
  every pre-existing atlas pixel outside that repair mask remains unchanged.
- Derived a `512x864` puppet atlas from the corrected reference: body plus
  upper/lower layers for both arms and legs in all eight directions. The
  deterministic builder verifies that the nine neutral layers composite
  pixel-for-pixel to the reference frame.
- Replaced the temporary single-renderer presentation with nine visual-only
  `SpriteRenderer` components in four parent/child joint chains. Walking now
  rotates shoulders, elbows, hips and knees, while bob/rock and `Wasted` sway
  affect the complete puppet.
- Preserved camera-independent actor heading, 5-degree view hysteresis,
  non-mirrored asymmetry, PPU 48 and the shared four-pixel foot pivot.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 213/213 passed.
- Unity PlayMode with D3D11: 29/29 passed, including all nine puppet layers,
  joint animation, eight camera sectors and the complete
  `City`/`BarInterior` scene flow.
- Windows x64 Player build: succeeded, 0 warnings, 131,098,367 bytes.

## 2026-07-28 — Eight-direction player prototype

- Replaced the procedural 13-part player rig with one point-filtered
  `SpriteRenderer` backed by eight explicit `64x96` atlas views at PPU 48.
- Locked a grim burgundy/navy character design with persistent left-arm
  bandage, right-shoulder patch and diagonal strap details; every view has the
  same scale and 4-pixel foot pivot and is used without mirroring.
- Added a pure eight-sector selector with 5-degree hysteresis, full wraparound
  and explicit front/side/back ordering.
- Removed both camera-to-player yaw writes. The motor now faces actual planar
  movement, preserves heading while idle and remains camera-relative.
- Preserved prototype walking bob/rock and the existing `Wasted` sway through
  the single-renderer hierarchy in both runtime scenes.
- Updated player/camera tests, import checks, architecture maps, player art
  specification and release notes. Full multi-frame idle/walk animation remains
  the next art pass after approval of the static vertical prototype.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 211/211 passed.
- Unity PlayMode with D3D11: 29/29 passed, including all eight view centers,
  camera-independent heading, input movement, prototype motion, `Wasted`,
  obstacle avoidance and the complete `City`/`BarInterior` flow.
- Manual atlas contrast check: all eight silhouettes and the bandage/patch/strap
  asymmetry remained readable against gray-green fog and warm bar tones.
- Windows x64 Player build: succeeded, 0 warnings, 129,322,495 bytes.

## 2026-07-28 — Entrance-aware road-edge fences

- Added a deterministic pure-data fence plan over the exposed perimeter of
  the complete road-rectangle union. It closes outer edges and dead ends
  without placing barriers inside turns, T-junctions or intersections.
- Added a `3.30 m` opening on the facing road side of every bar, driven from
  the same frontage and shared walkway geometry used by the generated facade.
- Added low ochre two-rail visuals with dark inset posts. They remain
  collider-free so the road/apron mask is authoritative and are combined into
  two generated meshes instead of hundreds of individual renderers.
- Added deterministic, sparse-topology, perimeter-completeness, opening,
  batching, collider and entrance-walkability regression coverage.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 189/189 passed.
- Unity PlayMode with graphics: 25/25 passed, including the generated fence
  hierarchy and every bar's clear walkable approach.
- Windows x64 Player build: succeeded, 0 warnings, 129,125,439 bytes.

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
