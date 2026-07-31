# Work log

Entries are reverse chronological. Record outcomes and verification, not a transcript.

## 2026-07-31 — Physical first-person bar drink service

- Replaced the retail drink shop's full-screen list with a compact overlay on
  a seated first-person presentation. A pure unscaled timeline now owns camera
  approach, persistent bottle browsing, pickup, vessel placement, pouring,
  bottle return, an exact three-second drink, empty-vessel return and an
  explicit-exit camera return.
- Derived one validated service plan from every generated bar layout and
  reserved its lower central back-bar shelf for all nine retail offers. Each
  offer builds as a separate low-poly bottle root with registered renderers,
  stable slot ID, mouth anchor, solid collider, selection trigger and
  kinematic Rigidbody; mouse rays accept both bottle colliders and respect
  solid occluders.
- Added reusable low-poly tumbler, pint, wine glass, shot glass and snifter
  meshes, shared transparent glass/liquid materials, bottom-anchored liquid
  fill and one reusable world-space pour stream. The presentation catalog maps
  water, beers, wines, vodkas and cognacs to their correct vessel, bottle style,
  colors and target fill.
- Added procedural camera-local arms: the right hand grips and tilts the exact
  selected bottle while the left hand presents and lifts the active vessel.
  The ordinary player rig plus dynamic/contact shadows hand off cleanly and
  restore with the camera.
- Preserved the existing atomic transaction at confirmation. Failed purchase
  validation remains in browsing; a successful purchase deducts and records
  exactly once, rejects exit until service completes, returns to the same
  browser after the vessel reaches the counter, and is never refunded by
  disable/destroy/scene cleanup. Repeat orders reuse the same modal ownership;
  only explicit Exit restores camera, input, HUD and player presentation.
- Integrated the presentation into `BarInteriorRoot`, protected committed
  service from the F9 debug launcher, retained immediate pre-commit replacement
  and localized the browse/serve/pour/drink/vessel-return states plus the
  dedicated `EXIT` / `ВЫЙТИ` action in RU/EN.
- Added pure catalog/timeline coverage and PlayMode contracts for nine physical
  bottles, five vessel mappings, shared resources, real fill/stream state,
  first-person lifecycle, exactly-once debit, committed cancellation rules and
  production scene flow. Also made bottle teardown avoid Unity's forbidden
  sibling reorder during ancestor deactivation and made two pre-existing
  queued-input timing checks independent from slow complete-suite wall-clock
  frames.
- Final edge-case auditing tightened the bottle row and widened the seated shot
  so complete renderer bounds retain a tested safe margin at 16:10, and added
  an exact vessel-transform snapshot/reset plus a regression that reuses pint,
  wine-glass, shot-glass and snifter instances after animation scaling.
- Raised the service camera from chest/counter height to a natural seated eye
  height and reduced its upward pitch. The counter's green floor marker and
  emissive sign now snapshot their exact renderer states, remain hidden through
  browsing, repeated serving cycles and camera return, and restore on every
  explicit-exit or close/disable path.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings; localization JSON parses and `git diff --check`
  passes.
- Focused drink-service timeline coverage passed 12/12; complete EditMode
  passed 639/639.
- Complete headless PlayMode passed 122/122 executed tests with 3 GPU-only
  tests skipped and 0 failures. All 8 bar-drink presentation, integration,
  world-builder and production-scene tests passed; the focused repeat-order
  path passed 4/4.
- The Windows x64 Player build succeeded at `148327776` bytes with 0 warnings.

## 2026-07-31 — Refrigerator hum states and PS1 item inspection

- Replaced the single refrigerator response with distinct deterministic
  closed-cabinet and open motor/fan loops. Two co-located spatial sources start
  at the same DSP time and follow the live door amount through cosine/sine
  equal-power gains; initialization, disable and scene cleanup keep both loops
  under the existing scene-local audio ownership contract.
- Added stable catalog metadata and runtime item views for the vodka bottle,
  chicken egg and open stew can. Each item registers its original root,
  renderers and a tight trigger collider without changing the eight-slot
  storage contract or introducing inventory persistence.
- Added pointer ray hover with a reversible `MaterialPropertyBlock` tint and a
  localized cursor label. Keyboard/gamepad cycling remains available as a
  fallback and uses the same registered item set.
- Added a nested unscaled `Browsing -> FlyingIn -> Inspecting -> FlyingOut`
  timeline under the refrigerator's existing modal lock. The chosen model
  eases into a camera-relative centered pivot, rotates slowly over a dark
  backdrop and receives localized name/description plus `Take`, `Use` and
  `Back` actions. `Take` and `Use` currently show a localized unavailable
  placeholder and do not mutate storage or session state.
- Normal return, outer close, cancel, component disable and destruction restore
  the exact parent, sibling index, local transform, collider state and renderer
  colors before releasing the outer interaction. The scaled retro UI now
  converts pointer coordinates into its logical `640 x 360` canvas, and a
  disabled nested controller rejects direct input calls.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings; `git diff --check` passes.
- Focused item/audio/catalog EditMode coverage passed 17/17, and the final
  refrigerator interaction PlayMode fixture passed 6/6 with real Input System
  mouse events, hover tint/shared-material assertions, backdrop alpha, item
  framing, placeholder actions and lifecycle restoration.
- Complete EditMode passed 612/612 and complete D3D12 PlayMode passed 118/118.
- The Windows x64 Player build succeeded at `148258576` bytes with 0 warnings.

## 2026-07-31 — Refrigerator puppet handoff timing

- Kept the ordinary player rig and both shadow presentations visible during
  the refrigerator camera approach, then hid them in the exact frame that
  the low-poly first-person hand becomes visible.
- Restored the rig and shadows as soon as the sealed door enters camera return;
  modal input ownership and HUD restoration still wait for the existing
  interaction completion boundary.
- Updated the focused PlayMode regression to cover the visible approach, hand
  handoff, hidden inspection/close and immediate camera-return restoration.

Verification:

- Runtime and PlayMode test projects compile with 0 errors and 0 warnings.
- Focused refrigerator interaction PlayMode passed 3/3; the neighboring pure
  refrigerator timeline EditMode fixture passed 8/8.

## 2026-07-31 — Interactive first-person Home refrigerator

- Replaced the dark counter-embedded refrigerator placeholder with one
  data-first `HomeRefrigeratorPlan` and a dedicated runtime builder/view. The
  enlarged worn-enamel cabinet now has a real hollow liner, three stained
  shelves, lower drawer, two door bins, frost, rust and grime; the counter is
  physically split around its footprint instead of covering its lower half.
- Defined six cavity slots and two door slots with stable IDs and item
  envelopes. Three initial shelf occupants build as distinct low-poly models:
  a vodka bottle, one chicken egg and an open can of stew. The contract is
  ready for later item placement without claiming a global inventory system.
- Moved the table `0.30 m` deeper in Home-local Z and added validated approach
  bounds/waypoints, trigger, camera, light and sound anchors so the real player
  can reach the refrigerator and it reads clearly from the main fixed shot.
- Added a frame-rate-independent modal open/inspect/close timeline. The camera
  follows a first-person quadratic approach, the normal puppet and shadows are
  hidden, and a procedural low-poly sleeved hand reaches for and turns the
  handle before the door unseals and swings to `102°`. Inspection persists
  until close input; completion and cancellation restore the exact Home shot,
  player renderers, shadows, controls and HUD.
- Added a cold emissive interior strip and depth-tested halo without adding a
  realtime `Light`, localized open/close prompts, generated seal/hinge/thunk
  cues and a door-open volume/low-pass response on the existing spatial
  refrigerator hum.

Verification:

- Added deterministic EditMode coverage for plan geometry, slot occupancy,
  approach clearance, timeline endpoints/chunk invariance and cue contracts,
  plus PlayMode coverage for generated storage, first-person camera/open state,
  item framing/non-intersection, sound response and modal
  restoration/cancellation.
- Runtime and PlayModeTests generated projects compile with 0 errors and
  0 warnings; focused refrigerator PlayMode passed 3/3 and the independently
  rerun pre-existing bed-interaction fixture passed 2/2.
- Complete EditMode passed 601/601 and complete D3D12 PlayMode passed 114/114.
- A 960x540 rendered-frame smoke checked the closed main-room silhouette,
  first-person hand grip and fully open inspection framing. The bottle, egg,
  stew can, shelves, drawer and interior light were all readable in-frame.
- The Windows x64 Player build succeeded at `148229232` bytes with 0 warnings.

## 2026-07-31 — Locked alarm-clock menu beat and extended first wake

- Replaced the automatic clock-to-sleeper reveal with a clock shot that stays
  active until the player chooses Wake Up. The first rendered Home frame shows
  `05:59`; the opening discards its first potentially load-inflated delta and
  then holds five unscaled seconds with no menu rendering or input path.
- Rebuilt the clock face as one reusable 28-segment display. At the timing
  boundary it reveals Wake Up/Quit without changing `05:59`, starting the
  alarm or creating new display geometry. All digits and both colon elements
  keep flickering off for only `0.16 s` at three-second intervals before and
  after the menu appears, by toggling their renderers without material
  instances or hierarchy churn.
- Wake Up alone changes the display to solid `06:00`, hides the menu and
  starts the spatial mechanical ring. The clock shot and persistent sleeping
  loop remain unchanged for exactly three unscaled seconds; the alarm then
  stops, the existing 24-frame exit starts with a `3x` duration multiplier
  (`6 s` instead of the ordinary `2 s`), and the camera glides from the clock
  to the sleeper through a `2.25 s` smootherstep quadratic path before easing
  into the active MainRoom shot. The final pose already matches gameplay when
  control returns, so there is no handoff cut.
- Cancellation during the locked clock beat restores the silent ordinary
  `05:59` display together with normal Home input; only a successful Wake
  request can switch this opening clock to `06:00`.
- Stabilized the ordinary-bed input proof by making its virtual keyboard
  current at each tested press, explicitly processing and holding the movement
  event across frames, and asserting restored movement rather than a transient
  raw key flag.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings.
- Focused opening/animated-interaction EditMode tests passed 23/23.
- Real MainMenu/opening, alarm and ordinary-bed PlayMode tests passed 7/7,
  including the five-second input gate, silent menu, Wake-only time change,
  exact three-second alarm hold, cleanup during the ring, segment counts,
  persistent flicker timing, both camera-move stages, cut-free gameplay
  handoff and the `3x`/`1x` wake-duration split.
- Complete EditMode passed 583/583 and complete D3D12 PlayMode passed 111/111.
- The Windows Player build succeeded at `148173632` bytes with 0 warnings.

## 2026-07-31 — Windows Player runtime-material repair

- Reproduced the all-magenta Windows Player and traced it to runtime-composed
  primitives retaining `GameObject.CreatePrimitive`'s implicit material.
  Editor-only URP defaults made that path look valid in Play Mode, while the
  Player data contained no `Universal Render Pipeline/Lit` shader.
- Added one serialized `RuntimePrimitiveLit` Resources material and made
  `RuntimePrimitiveFactory` assign it to every primitive without an explicit
  specialized material. Per-instance colors remain in
  `MaterialPropertyBlock`, and explicit emissive, atmosphere and glass
  materials remain unchanged.
- Added a regression contract that boxes and cylinders resolve the same
  supported shared URP/Lit material.

Verification:

- Focused `RuntimePrimitiveFactoryTests` passed 4/4; complete EditMode passed
  581/581.
- Complete PlayMode passed 110/110 on D3D12, including the graphics-device
  RenderGraph test and the real Unity Test Framework bootstrap path.
- A full Windows x64 Player build succeeded at `148166976` bytes with
  0 warnings. Its serialized Player data now contains
  `Universal Render Pipeline/Lit` and `RuntimePrimitiveLit`.
- An eight-second D3D12 built-player visual smoke reached the waking Home
  menu with the room, hero, clock and furniture correctly shaded instead of
  magenta; its Player log contained no shader/material error or runtime
  exception.

## 2026-07-31 — PS1 waking opening and Home alarm clock

- Added `MainMenu` as build scene index `0`. Its black launch camera holds the
  initial frame while `MainMenuRoot` resets the complete session, prepares the
  one-shot `HomeArrivalKind.OpeningSleep` value and Single-loads the existing
  `HomeInterior`.
- Added a frame-rate-independent Home opening that starts the existing bed
  interaction directly in its persistent sleep loop, captures modal input,
  reveals clock and sleeper fixed shots, and presents localized PS1-style
  Wake Up/Quit choices.
- Wake Up stops the alarm, requests the existing 24-frame wake sequence and
  restores ordinary Home input, HUD, shadows and active fixed-camera shot
  without reloading the room. Direct and later Home arrivals consume
  `Normal`, so they never replay the opening.
- Added one validated bed-relative nightstand and low-poly 3D alarm clock to
  every Home composition. The opening uses a generated looping mono
  `22050 Hz` mechanical ring on a fully spatial `SFX/World` source plus a
  bounded visual rattle; ordinary visits keep the clock silent.
- Extended the editor scene setup, build-scene contract, RU/EN localization,
  new-session/arrival state, opening timeline, alarm plan/synthesis/lifecycle
  and Home scene-flow coverage.
- Pinned ordinary Unity Editor Play to `MainMenu` from every currently open
  scene. Unity Test Framework's exact temporary `InitTestScene{GUID}` path
  suppresses the override while PlayMode tests start, then restores it on the
  return to Edit Mode.

Verification:

- Opening-focused EditMode passed 52/52; the complete EditMode suite passed
  575/575.
- The opening/alarm/input/transition PlayMode paths passed, including a real
  `E` press and Home-to-Stairwell handoff; the complete PlayMode suite passed
  110/110 after making the older bed movement assertion process its queued
  Input System event explicitly.
- `BarPromenade.Runtime`, EditMode tests and PlayMode tests compile through
  their generated projects with 0 errors and 0 warnings.
- Windows Player build succeeded at `147903696` bytes with 0 warnings.
- A 12-second built-player smoke recorded `MainMenu` at build index `0`, the
  complete new-game reset, `OpeningSleep` prepare/consume and initialized
  `HomeInterior` at build index `5`, with no runtime exception in the Player
  log.

## 2026-07-31 — Player-home frontage regression repair

- Restricted preferred home placement to the selected bar's actual frontage
  instead of accepting any street edge around that bar's lot.
- Applied the `48 m` traversable-route bound before accepting preferred
  candidates as well as fallback candidates. Default layouts retain their
  shared approach and `12 m` fresh-spawn distance.
- Added a direct regression assertion that the default player home shares a
  frontage road with a selected bar.

Verification:

- Runtime, EditModeTests and PlayModeTests assemblies compiled with 0 errors
  and 0 warnings.
- Targeted `CityLayoutGeneratorTests` passed 21/21, targeted
  `PlayerHomeLayoutTests` passed 28/28 and complete EditMode passed 563/563.
- Relevant `SceneFlowSmokeTests` passed 10/10. Two complete PlayMode runs
  reported 104/105 only because the pre-existing bed-input timing assertion
  at `HomeBedInteractionPlayModeTests.cs:292` did not observe a queued key
  within one frame; that test passed 1/1 in isolation.
- Windows x64 Player build succeeded at `147880324` bytes with 0 warnings. A
  15-second headless startup smoke contained no error, exception, assertion,
  failure or missing-asset messages.

## 2026-07-30 — Shared PS1-horror audio mix and interior soundscapes

- Added one canonical `BarPromenadeAudio` mixer with Music, two ambience
  layers, world/gameplay SFX and dry UI groups, plus dedicated environment
  reverb and stereo echo returns under a headroom-preserving master
  compressor.
- Added City, Bar, Stairwell, Home and DoorTransition snapshots. Home uses a
  short damped no-echo space; Stairwell uses the longest, strongest and
  deliberately dark reverb plus restrained echo. Non-door profile changes
  preserve wet tails through a `0.25 s` transition.
- Routed all existing scene music, procedural ambience, pooled SFX, UI and bar
  soundscape sources through their canonical groups. Existing music now
  background-streams instead of fully decompressing on scene load.
- Reworked the Home and Stairwell base loops into steady room beds, then added
  exactly three spatial sources and six generated clips to each interior.
  Home layers refrigerator/night air and sparse domestic details; Stairwell
  layers ventilation/electrical buzz and sparse pipe, metal, water and
  movement cues.
- Kept synthesis deterministic and data-first: pure seeded schedules bound
  delay/pitch/gain, layout planners provide world anchors, quantized
  `22050 Hz` mono generation provides the low-resolution character and
  scene-local cleanup releases every runtime clip/source.
- Added an idempotent Unity editor setup for the mixer. Required DSP
  effects/sends fail fast, and the EditMode contract validates the exact
  topology, send targets, stereo echo parameters and scene snapshot values.

Verification:

- Two consecutive mixer setup runs exited successfully without duplicate
  groups, snapshots, effects or sends; the dedicated mixer contract passed
  9/9.
- Targeted soundscape EditMode passed 10/10 and targeted component PlayMode
  passed 2/2.
- Complete solution build: 0 errors, 0 warnings.
- Complete EditMode reported 559/563; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Complete PlayMode passed 105/105.
- Windows x64 Player build succeeded at `147879812` bytes with 0 warnings.
- The final Player stayed healthy through a 15-second headless startup smoke;
  its log contained no error, exception, assertion, failure or missing-asset
  messages.

## 2026-07-30 — Interactive stairwell cat

- Added one seated rear-view pixel-art cat to the upper bar of the middle
  landing rail. Its exact camera-plane billboard stays composed in all three
  fixed stairwell shots, while directional rows turn the head toward the
  player.
- Built a point-filtered `512x256` atlas from retained image-generation source
  sheets. The first three rows provide breathing, tail and ear idle variants;
  the fourth provides a complete eight-frame paw-lick and face-wash sequence.
- Added a deterministic idle timeline: the first grooming cycle starts after
  24 seconds, lasts 2 seconds at 4 fps and repeats every 36 seconds.
- Added a non-blocking `IInteractable` trigger on a radius-safe middle-landing
  approach. `E` temporarily replaces the localized prompt with the short
  placeholder “Кот молча смотрит.” / “The cat watches.” without locking player
  input.
- Added atlas/import, layout, look hysteresis, idle/grooming, interaction,
  localization and scene-presentation coverage. The fixed-shot assertion now
  checks both viewport composition and an unobstructed physics line of sight.

Verification:

- Final stairwell-cat EditMode selection passed 16/16.
- Final stairwell presentation PlayMode passed 4/4.
- Complete EditMode reported 540/544; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Complete DX12 PlayMode reported 101/102; the sole failure was the unrelated
  input-timing-sensitive Home bed test, which passed its immediate targeted
  DX12 retry 1/1.
- The complete solution built with 0 errors and 0 warnings.
- Windows build succeeded at `147846797` bytes with one package-owned URP
  `Hidden/Core/DebugOccluder` shader warning.

## 2026-07-30 — Stairwell scene-music slot

- Added an optional scene-local `stairwell_theme` resource slot under
  `Resources/Audio/StairwellMusic`.
- Added `StairwellMusicPlayer` to the runtime-composed stairwell root. It uses
  the shared looping non-spatial music setup, remains silent when the track is
  absent and is destroyed on the next Single-mode scene transition.
- Added an in-folder delivery README plus PlayMode coverage for the player,
  source configuration, filter, resource path and scene ownership.
- Updated the current overview, architecture, system map/tree, README and
  release notes with the new music contract.

Verification:

- Stairwell presentation PlayMode passed 3/3.
- Runtime project built with 0 errors and 0 warnings.
- `git diff --check` passed.

## 2026-07-30 — Stairwell traversal, fixed-camera and lighting correction

- Reproduced the blocked first flight through the real `PlayerMotor` path
  instead of the earlier direct-`CharacterController` helper. The physical
  ramp was sound, but independently radius-eroded walkable rectangles left a
  gap at each floor/flight seam.
- Extended the three stair navigation corridors into both adjacent landings by
  more than the controller diameter and added dense radius-aware sampling over
  the complete street-to-apartment route.
- Added a stairwell-specific 3D fixed-camera selector because the lower and
  blocked upper flights share XZ coordinates. Three hard-cut shots cover the
  lower flight, middle/second flight and apartment landing; overlapping
  vertical hold zones prevent rapid cuts at landing thresholds, and the
  player billboard uses exact camera-plane alignment while the controller is
  active.
- Corrected the fluorescent fixtures: opaque housings no longer swallow the
  emissive tubes, two sources were lowered into their camera frames, suspension
  hardware was added, HDR output/halos were strengthened and the three
  co-located flickering point lights received larger readable ranges.
- Added PlayMode coverage using queued keyboard `W` through the actual motor,
  camera-shot changes during the physical climb, per-shot emitter viewport
  visibility, fixture separation/alignment and the unchanged upper blocker.
- Stabilized one unrelated existing Home bed test discovered by the complete
  run: queued `D` input now waits for the next player frame, matching the
  other input assertions in that test instead of forcing a manual input update.
- Updated current architecture, overview, system map/tree, README and release
  notes to record the fixed navigation, cameras and visible practicals.

Verification:

- Stairwell layout/camera EditMode passed 7/7.
- Stairwell presentation, real-motor seam traversal, physical climb/blocker
  and complete home round trip passed 4/4.
- Complete PlayMode passed 101/101.
- Complete EditMode reported 527/531; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Runtime, Editor, EditModeTests and PlayModeTests projects each built with
  0 errors and 0 warnings.
- Windows build succeeded at `145532748` bytes with one package-owned URP
  `Hidden/Core/DebugOccluder` shader warning.
- `git diff --check` passed; Unity left no `InitTestScene` asset behind.

## 2026-07-30 — Industrial home stairwell

- Added `StairwellInterior` as the fifth runtime-composed build scene and
  rerouted home travel through
  `City -> StairwellInterior -> HomeInterior -> StairwellInterior -> City`.
  A consumed side-aware arrival value places the player by the street or
  apartment door without prematurely setting the City home-return state.
- Added a validated three-elevation layout with a ground lobby, middle landing,
  apartment landing and three stair flights. The 48 visible steps use three
  invisible continuous ramp colliders so the `CharacterController` cannot
  catch on individual riser seams.
- Sealed the flight above the apartment floor with a full-width,
  full-standing-height safety collider backed by a visible pile of furniture,
  wire mesh, planks and sacks.
- Built the decayed industrial-horror dressing from shared runtime primitives:
  stained concrete, rusty rails, exposed pipes, ventilation, grilles,
  electrical cabinets, radiators, damp damage and trash.
- Added three bounded flickering practical lights, a green desaturated
  Bloom/color/vignette/grain profile, at most 14 dust motes, and a dedicated
  music-free procedural ventilation/mains/pipe/knock/drip ambience.
- Added RU/EN building/apartment prompts, four distinct door-transition
  directions, deterministic layout/state/audio coverage, full round-trip
  scene-flow coverage and a physical PlayMode climb/blocker test.
- Updated the scene helper, Build Settings, Windows scene list, README,
  project overview, system tree/map, architecture notes and release notes.

Verification:

- Stairwell-focused EditMode passed 62/62.
- Stairwell presentation, physical climb/blocker and complete home round trip
  passed 3/3; complete PlayMode passed 100/100.
- Complete EditMode reported 525/529; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Runtime, Editor, EditModeTests and PlayModeTests projects each built with
  0 errors and 0 warnings.
- Windows build succeeded at `145526908` bytes with one package-owned URP
  `Hidden/Core/DebugOccluder` shader warning.
- `git diff --check` passed.

## 2026-07-30 — PC-only quality/render configuration

- Removed the unused `Mobile_RPAsset` and `Mobile_Renderer` assets after
  confirming that the project is currently Windows/PC-targeted.
- Reduced `QualitySettings` to one PC quality level at index `0`, made it the
  current and default level for every serialized platform key, and left its
  platform exclusion list empty.
- Kept mobile quality and renderer parity explicitly deferred.

Verification:

- Complete EditMode reported 517/521; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Complete PlayMode passed 98/98.
- Windows build succeeded at `145489763` bytes with one package-owned URP
  `Hidden/Core/DebugOccluder` shader warning.
- No project references to either removed Mobile asset GUID remain, and
  `git diff --check` passed.

## 2026-07-30 — Home balcony wall-leak correction

- Clipped the Home exterior presentation to
  `x >= HomeFacadeX + WallThickness / 2 + 0.01`: ground, roads, haze,
  buildings and windows now stay outside the facade, while lamps and signals
  use an additional wall-clearance filter.
- Added an opaque collider-free ceiling plus south/north return walls to close
  the remaining shell gaps without changing the window, open door or walkable
  balcony.
- Removed the obsolete emissive `Home Exit Header` that projected into the
  upper-right camera edge and sealed the `0.50 m` side gaps plus `0.34 m`
  transom gap around the front city-exit door with opaque wall sections.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds completed with 0 errors
  and 0 warnings.
- Focused `HomeBalconyLayoutTests` EditMode coverage passed 8/8.
- Focused `HomeBalconyPresentationPlayModeTests` and
  `HomeInteriorPresentationPlayModeTests` passed 2/2; the follow-up front-entry
  correction and Home-to-City transition coverage passed 3/3.
- Inspected a `1516x824` D3D control render matching the reported frame: the
  orange corner marker and unintended street sightline were absent.

## 2026-07-30 — Same-scene third-floor Home balcony

- Replaced the black Home right wall with a real window and open glazed door
  leading directly to a walkable balcony at `4.7 m` street elevation without
  loading another scene.
- Extended the validated Home walkable mask through the doorway threshold and
  across the balcony deck. Open-looking rails retain invisible solid safety
  colliders so the player can occupy the exterior space without falling.
- Reconstructed a bounded view of the actual player-home street from the
  preserved city seed: nearby roads, lots, stable windows, lamps and signals
  are transformed into Home-local coordinates without spawning a second City
  root, player, camera or realtime street-light pool.
- Added the matching balcony/window/door treatment to the generated City home
  facade and raised its default mass to `8.8 m`, with shared transform and
  dimension helpers keeping both representations aligned.
- Added one cold shadowed cookie Spot aimed through the window while preserving
  exactly the two existing shadowless practical lights. Window and door panes
  reuse one shared transparent URP glass shader/material.
- Added a third fixed Home camera shot for the balcony and expanded data-first,
  scene-presentation, lighting, glass, collider and camera regressions.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests compilation completed with
  0 errors and 0 warnings.
- Focused EditMode coverage passed 35/35; focused PlayMode coverage passed 9/9.
- Inspected the D3D visual capture of the finished window, directed night
  light, exterior street and balcony presentation.
- Complete EditMode passed 516/520; the only failures were the same four
  pre-existing `CityLayoutGeneratorTests`.
- Complete PlayMode reported 94 passed, 1 failed and 3 skipped; the sole
  failure was the pre-existing batch-input second-`E` assertion in
  `HomeBedInteractionPlayModeTests`.
- Windows build succeeded at `145488227` bytes with 0 warnings.
- `git diff --check` passed.

## 2026-07-30 — Animated bed sleep interaction

- Added a reusable frame-rate-independent animated-interaction timeline with
  `Idle -> Entering -> Looping -> Exiting` phases and a player controller that
  presents atlas frames on the camera plane between authored stand/action hip
  anchors.
- Split presentation into an outer camera-facing root and an inner sprite
  visual. Contextual interactions can now perspective-project an authored
  world axis through `WorldToScreenPoint`, preserve texture handedness and ease
  the inner visual into and out of the required camera-plane roll.
- The controller locks the motor for the complete interaction, hides the
  ordinary nine-layer rig plus dynamic/contact shadows, enables ordinary
  interaction input only during the persistent loop for the second `E`, and
  restores every captured state on completion, owner cancellation, disable or
  scene cleanup.
- Added one data-first trigger on the reachable `xMax` side of the Home bed.
  The first localized `E` plays 24 lie-down frames at `12 fps`, 16 sleeping
  frames loop indefinitely at `4 fps`, with an extra `0.25 s` delay on
  full-inhale frame `034` and an extra `0.75 s` post-exhale rest on frame
  `027` for one `5 s` breath cycle; the second localized `E` plays 24
  dedicated wake-up frames at `12 fps`. The physical player root stays safely
  beside the bed, and the crumpled shirt is hidden only while the sequence is
  active.
- Aligned the sleep pose with the bed's world `+X` head-to-foot axis after
  perspective projection, kept the authored head-left side at the `xMin`
  pillow, and moved the action hip `0.135 m` footward so the loop has balanced
  approximately `7.7 cm` head and foot margins.
- Added a shared contextual-sprite overlay shader with `ZWrite Off` and
  `ZTest Always`, eliminating bed depth-buffer clipping across the complete
  lie/sleep/wake sequence. Restored the natural `0.045 m` action-hip surface
  clearance instead of compensating with an artificial vertical lift.
- Made the bed own only the sequence it started. A disabled or destroyed bed
  now cancels persistent sleep and restores movement, interaction input, the
  rig, both shadows and the per-interaction captured clutter state; a disabled
  or uninitialized animation controller no longer advertises the prompt.
- Added the point-filtered `1024 x 768` player sleep atlas as 64 row-major
  `128 x 96` cells with pivot `(64, 40)`, all 64 source frames, keyed/generated
  working sheets and deterministic extraction, validation and packing tools.
- Added pure timeline, bed-plan and asset-contract EditMode coverage plus a
  real Home-scene `InputTestFixture` PlayMode regression for both `E` presses,
  motor locking, persistent sleep, owner-disable cancellation and complete
  movement/visual restoration.

Verification:

- After the depth-independent overlay correction, the focused
  `PlayerBedSleepAssetTests` passed 3/3, including shader support, render queue,
  pass lookup and explicit depth-state checks.
- Atlas validation passed with file SHA256
  `11EE6886B4BDB439CEB183EEE42F699B275A1362B527E61AEF06F0CC2AA4B56B`.
- Sequential .NET builds of `BarPromenade.Runtime`, `BarPromenade.Editor`,
  `BarPromenade.EditModeTests` and `BarPromenade.PlayModeTests` completed with
  0 errors and 0 warnings.
- After the breathing-timing follow-up, the Runtime, EditModeTests and
  PlayModeTests .NET builds repeated with 0 errors and 0 warnings; the focused
  animated-interaction timeline passed 16/16 and the real DX12 bed-sleep
  scenario passed 1/1.
- Two complete DX12 PlayMode runs each passed 96/97. Their only failure was the
  existing batch-only synthetic `Keyboard.dKey` assertion in the bed test;
  the same complete bed scenario passes 1/1 in isolation.
- Complete EditMode repeated at 502/506; exactly the same four pre-existing
  `CityLayoutGeneratorTests` remain red.
- The earlier feature Windows build succeeded at `145453839` bytes with
  0 warnings.
- Reviewed the complete atlas visually for the lie-down, breathing loop and
  dedicated wake-up sequence.
- Final `git diff --check` passed.

## 2026-07-30 — Approved Home framing and visible practicals

- Finalized the runtime-composed interior as an impoverished, cluttered old
  alcoholic's flat with six main-room furniture groups and three bathroom
  fixtures. Dedicated blocking camera-corner junk now makes the authored
  bed-side camera pocket physically unreachable.
- Locked the user-approved main-room shot to
  `(-4.48, 3.00, -3.25)`, Euler `(28°, 55°, 0°)`, `64°` FOV and the
  bathroom shot to `(1.82, 2.20, 0.86)`, Euler `(30°, 38°, 0°)`, `92°` FOV.
- Made both practical sources explicit in the image: the warm hanging bulb and
  cold bathroom tube use visible HDR emitters and depth-tested halos physically
  aligned with their two shadowless `Light` components.
- Reoriented the toilet with its cistern against the right wall and its bowl
  facing into the bathroom.
- Added an opt-in fixed-camera plane mode to `BillboardSprite`. Home enables
  exact `-camera.forward`/`camera.up` alignment so the `64 x 96` hero does not
  compress in either steep shot, and resets the mode when fixed control ends.
- Extended the focused regressions to cover exact camera poses, the blocking
  corner footprint, practical emitter/light/halo alignment and visibility,
  toilet orientation and projected sprite aspect. The corner junk was widened
  after the final nearest-reachable-point review so the complete player frame
  stays on screen without changing the approved camera pose.

Verification:

- `BarPromenade.Runtime`, `BarPromenade.Editor` and
  `BarPromenade.EditModeTests` .NET builds completed with 0 errors and
  0 warnings; Unity compiled the PlayMode assembly during the test runs.
- Focused `PlayerHomeLayoutTests` passed 21/21 and focused Home PlayMode
  coverage passed 8/8, including the camera-plane aspect, full-frame visibility
  at the nearest reachable main-room point and corrected toilet orientation.
- Complete PlayMode passed 96/96.
- Complete EditMode repeated at 481/485; the same four pre-existing
  `CityLayoutGeneratorTests` remain red, while all 21 Home layout tests pass.
- Reviewed the final `960 x 540` main-room and bathroom renders; both visible
  practical sources read in-frame and the bathroom character is neither
  compressed nor cropped.
- Windows build succeeded at `142280031` bytes with one package-owned URP
  `DebugOccluder.shader` vector-truncation warning and no project-code warning.
- `git diff --check` passed.

## 2026-07-29 — Neglected home, bathroom and fixed cameras

- Expanded the deterministic `10 x 8 x 3.4 m` home plan with explicit
  main-room/bathroom zones, protected entry/main/bathroom-access paths and
  validated toilet, shower and sink footprints.
- Rebuilt the runtime interior as a dim impoverished bachelor flat with worn
  furniture, stained and peeling surfaces, a boarded dead window, bottles,
  cans, ashtray, dirty dishes, clothes, papers, an old radio and restrained
  personal remnants. Small narrative clutter is collider-free so it does not
  invalidate the authored circulation.
- Added a complete tiled bathroom with an ajar opening, toilet, shower tray
  and curtain, pedestal sink, cracked mirror, exposed rusty pipes, leak damage
  and a floor drain.
- Added a bounded Home-only atmosphere: a weaker hard-shadow
  directional/ambient base, two shadowless dirty-yellow/cold practical lights,
  a cleaned-up runtime Bloom/color/exposure/vignette/grain profile and at most
  12 shared-material dust motes.
- Split Home ambience from the bar loop with deterministic refrigerator,
  mains, pipe and drip layers.
- Added two authored fixed camera poses for the main room and bathroom. The
  controller applies immediate hard cuts, keeps each shot through a wider
  hold area for doorway hysteresis, ignores orbit/follow movement and retains
  reduced intoxication, balance and fall rotation around the fixed position.
- Refreshes the player's billboard plane in the same hard cut, preventing the
  nine-layer sprite from briefly rendering edge-on to a newly active shot.
- Blocks the otherwise reachable pocket beneath the main camera with the sofa,
  falls back to the main shot for all legitimate non-bathroom floor, keeps the
  ajar bathroom door solid with capsule-width clearance and aligns narrative
  clutter to the actual furniture surfaces.
- Added focused EditMode/PlayMode coverage for bathroom layout and scene
  construction, Home ambience/atmosphere bounds and cleanup, fixed-pose
  behavior, shot hysteresis and the existing home round trip.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds: 0 errors,
  0 warnings.
- Focused home-layout and retro-SFX EditMode checks: 20/20 and 28/28 passed.
- Focused Home fixed-camera, atmosphere, presentation and round-trip PlayMode
  checks: 9/9 passed; the shared billboard check passed 1/1.
- Reviewed final `960 x 540` renders from both authored shots and the nearest
  reachable point to the main anchor. This caught and drove corrections to
  the initial underexposure, bathroom angle/door occlusion, stale
  player-sprite plane and the original camera blind pocket.
- Windows build succeeded at `142277983` bytes with 0 warnings.
- Complete EditMode repeated at 480/484: four unchanged
  `CityLayoutGeneratorTests` fail on the pre-existing bar-distance contract
  (the isolated class repeats 17/21).
- Complete PlayMode passed 96/96 after the Home presentation fixture was made
  to unload its Single-mode scene during teardown; the previously affected
  legacy intoxication/debug-window/motor classes also pass 16/16 in isolation.
- `git diff --check` passed.

## 2026-07-29 — Player home MVP

- Added one deterministic non-bar player home beside a generated bar street,
  validated within `48 m` of a bar by traversable route distance, and moved the
  fresh-session spawn to its frontage node.
- Gave the exterior a distinct teal/cool-lit facade, porch, mailbox, chimney,
  walkable approach, fence opening, localized interaction and labeled house
  marker on the full-screen map.
- Added the separate runtime-composed `HomeInterior` scene with a validated
  `10 x 8 x 3.4 m` layout, five furniture groups, warm practical lighting,
  quiet ambience, shared player/intoxication systems and one exit.
- Extended the shared door transition and session contract with explicit
  bar/home return kinds, preserving the city seed, route, visits, cash and
  drinking progress while returning to the matching exterior entrance.
- Updated editor scene setup, build settings, diagnostics, localization,
  EditMode coverage and the full `City -> HomeInterior -> City` PlayMode smoke
  path.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds: 0 errors, 0 warnings.
- RU/EN localization catalogs parse successfully.
- Unity EditMode and PlayMode suites: pending while the project is open in an
  interactive Unity Editor instance.
- `git diff --check` passed.

## 2026-07-29 — Bar-adjacent fresh city spawn

- Replaced the fresh session's central-city spawn with a deterministic node on
  one generated bar's frontage road, placing the player `12 m` from that bar
  under default spacing.
- Preserved the existing `SpawnNode`/`SpawnWorldPosition` contract, full
  player-radius walkability and the central-node fallback for custom layouts
  with no bars.
- Kept the separate return path unchanged, so leaving an interior still
  restores the active bar's exact return position.
- Added seed-varied pure generation coverage and a real City bootstrap
  assertion for spawn position, road travel distance and controller clearance.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused city-layout EditMode checks: 21/21 passed.
- Focused generated-City PlayMode bootstrap: 1/1 passed.
- Complete Unity EditMode suite: 460/460 passed.
- Complete headless Unity PlayMode suite: all 84 runnable checks passed; the
  three graphics-only checks were skipped as expected.
- `git diff --check` passed.

## 2026-07-29 — Bar drink purchases and session wallet

- Added a session-only integer cash wallet starting at `$999`, a fixed
  nine-item retail catalog and pure purchase results that reject unsupported,
  unaffordable or maximum-intoxication alcohol without mutating state.
- Added one atomic `GameSessionState` purchase boundary: successful orders
  deduct cash and immediately commit drinking progress; water costs `$2`,
  counts as consumed, does not sober the player and preserves the last
  alcoholic drink.
- Added a localized retro shop modal and a separate counter interaction point
  in all four bar variants. The data-first layout reserves its trigger, removes
  one nearby stool for access and validates it against furniture, the activity
  station and exit.
- Integrated shop ownership with the shared modal lock and F9 window without
  adding the shop to the minigame catalog or bar-visit completion flow.
- Added cash and purchase outcomes to structured diagnostics and updated the
  current architecture, system maps and player-facing notes.

Verification:

- Complete generated .NET solution build: 0 errors, 0 warnings.
- Focused economy/layout/localization EditMode checks: 50/50 passed.
- Focused shop/F9/four-bar scene PlayMode checks: 9/9 passed.
- Complete Unity EditMode suite: 456/456 passed.
- Complete headless PlayMode suite ran twice: all changed-path checks and all
  non-motor checks passed, with three expected graphics-only skips. The first
  run passed 83/84 runnable checks and the second 82/84; the unchanged
  `PlayerMotorHeadingPlayModeTests` release-timing assertion remained flaky.
  Its isolated failing method passed 1/1; the complete motor class passed 5/6.
- `git diff --check` passed.

## 2026-07-29 — Opaque player hands

- Compared all eight runtime directions against the locked player turntable
  and restored 439 lower-arm skin/bandage pixels that the original chroma-key
  pass had made transparent.
- Extended the deterministic atlas builder with a lower-arm capsule repair
  constrained to skin-colored source pixels, then rebuilt the reference and
  nine-part/body-expression atlases without changing facial artwork.
- Added EditMode regression samples covering the repaired lower arms in every
  direction and verifying that the runtime lower-arm layers retain the exact
  reference colors.

Verification:

- Deterministic atlas builder migration: 439 lower-arm repairs; immediate
  rerun: 0 repairs with stable output.
- Runtime/EditModeTests .NET build: 0 errors, 0 warnings.
- Focused player-atlas EditMode checks: 9/9 passed.
- Focused player-rig PlayMode checks: 15/15 passed.
- Visual comparison of all eight corrected frames against the locked source:
  hands and bandage are opaque without magenta-background spill.

## 2026-07-29 — Bar dust particle error cleanup

- Configured all three BarInterior dust velocity axes as
  `TwoConstants` before enabling Velocity over Lifetime. The zero Z range now
  matches the randomized X/Y modes instead of leaving Unity's default
  `Constant` curve and emitting a native validation error continuously.
- Added a focused PlayMode regression that runs the real dust system for a
  frame, checks the three curve modes and rejects unexpected Unity logs.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused dust-velocity PlayMode check: 1/1 passed.
- BarInterior scene bootstrap PlayMode check: 1/1 passed.
- Both Unity logs contained zero instances of
  `Particle Velocity curves must all be in the same mode`.

## 2026-07-29 — Lower third-person camera framing

- Raised the exterior/interior chase-camera focus heights from `1.1 m / 1.05 m`
  to `1.4 m / 1.3 m`, placing the player below frame center without changing
  distance, FOV, orbit damping or collision handling.
- Extended both camera-profile PlayMode regressions with explicit viewport
  composition bounds and updated nearby focus, collision and teleport checks.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused camera-framing PlayMode check: 1/1 passed.
- Full player/camera presentation PlayMode checks: 15/15 passed.
- `git diff --check` passed.

## 2026-07-29 — Physically raised city walking surfaces

- Kept the authored height differences and added static mesh colliders to the
  same chunked street and park-path geometry used for rendering.
- Made the park lawn a box-collider surface and the central octagonal plaza a
  shared-mesh collider surface. The existing `0.28 m` controller step now
  climbs the `0.04 m` lawn-to-path rise instead of passing through the path.
- Reverted the provisional whole-puppet pixel lift; the correction now belongs
  to world collision, preserving the grounded atlas and shadow contracts.
- Added factory coverage for optional collidable combined meshes and a City
  PlayMode regression that settles on the lawn, crosses the path edge and
  verifies the controller rises to its physical top.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Focused primitive-factory EditMode checks: 3/3 passed.
- City presentation and walkable-surface PlayMode checks: 3/3 passed.
- Nearby city park/marker presentation check: 1/1 passed.
- `git diff --check` passed.

## 2026-07-29 — Structured session diagnostics

- Added a fail-safe NDJSON session logger with stable envelopes, typed fields,
  build/session/scene/seed context and basic/verbose/off runtime profiles.
- Instrumented session mutations, city and bar initialization, scene
  transitions, interactions, map lifecycle, all four minigames, intoxication
  stages and balance challenges at state-changing boundaries.
- Added correlation IDs for transitions, minigame runs, balance sequences and
  manual snapshots. `F8` captures and flushes a support snapshot; `Shift+F8`
  opens the active log directory.
- Added 5 MiB copy-and-truncate rotation with three retained archives, bounded
  field strings, exact-repeat suppression and separate bounded Unity warning
  and error budgets with dropped-count summaries.
- Documented locations, profiles, schema, event categories, retention, support
  workflow and privacy considerations in `ai/debug-log.md`.

Verification:

- Runtime, EditModeTests, PlayModeTests and complete Assembly-CSharp .NET
  builds: 0 errors, 0 warnings.
- Complete Unity EditMode suite: 423/423 passed.
- Complete headless Unity PlayMode suite: all 77 runnable checks passed;
  three graphics-only checks were skipped by the null graphics device.
- Verbose City → BarInterior → City integration: 1/1 passed. All 79 physical
  log lines parsed as schema-v1 JSON, used one session, had contiguous
  sequence numbers and correctly correlated both completed transitions.
- `git diff --check` passed.

## 2026-07-29 — District-scale city and traversable central park

- Expanded the deterministic default layout from `4 x 4` to `12 x 12` blocks
  and added mandatory cross-city arterials.
- Added four distinct urban districts plus a `4 x 4` central park with a lawn,
  plaza, trees, benches, hedges, crossing paths and four connected gates.
- Replaced shuffled bar placement with deterministic max-min selection over
  weighted graph travel. The four default bars occupy distinct districts and
  every pair is at least `120 m` apart along traversable paths.
- Updated the city map with localized district labels, district land colors
  and separate park-path presentation.
- Kept the larger world bounded by batching roads, fence rails/posts and lamp
  fixtures/bulbs into `48 m` spatial chunks. Added a BVH-style index for
  walkable rectangles and a binary min-heap for route finding.
- Extended pure and scene-level coverage for districts, park topology,
  continuous gate traversal, bar travel distance, map presentation, fence
  openings/chunks, walkability indexing and lamp batching.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Complete Unity EditMode suite: 388/388 passed.
- Complete headless Unity PlayMode suite: all 77 runnable checks passed;
  three graphics-only render tests were skipped by the null graphics device.
- Focused expanded-city scene checks: 3/3 passed, including all four park
  gates and spatially chunked fence geometry.
- `git diff --check` passed.

## 2026-07-28 — Cinematic expanded bar interior

- Rebuilt the runtime-composed bar around a deterministic `22 x 16 x 4.8 m`
  layout with seven named zones, four protected circulation paths and validated
  fixture clearances.
- Added a longer counter and backbar, bottle shelves and mirror, three booths,
  four social tables, a curtained stage, activity bay, entrance dressing,
  posters, ceiling beams, fan, practical fixtures and a dedicated service door.
- Populated the room with 12 lightweight NPCs: bartender, performer, booth
  groups, standing patrons and a walker. Six shared PS1/noir sprites, centralized
  low-frequency decisions and depth-aware billboard sorting keep the crowd
  readable without adding colliders or per-NPC materials.
- Added a six-light shadowless practical-light budget, warm URP grading, bloom,
  vignette, film grain, local dust and a skippable `1.35 s` arrival reveal.
- Added a two-source spatial soundscape for the crowd bed and rare bar-service
  cues while preserving the existing music and ambience source budget.
- Kept Beer Pong, Split G and Tincture variants functional, with exactly one
  activity station and one exit. Fixed modal ownership so interacting during
  the arrival reveal cannot restore stale input state.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Complete Unity EditMode suite: 365/365 passed.
- Complete Unity PlayMode suite: 80/80 passed, 0 skipped.
- D3D captures inspected the arrival, expanded room, crowd, stage, backbar and
  activity dressing; a 32-sample physics check covers the full camera reveal.
- Windows Player build: succeeded, `142098662` bytes, 0 warnings.

## 2026-07-28 — Five-stage intoxication and balance

- Replaced the independent timed intoxication status with a single persistent
  `0–100` value evaluated through five named 20-point ranges. Added continuous
  speed, jointed-puppet, chase-camera and PS1 world-composite parameters plus a
  crisp five-segment localized HUD.
- Added deterministic balance scheduling above `60`, a fixed-step inertial
  arrow model, the overhead arc/safe-sector/risk presentation and a
  balance-specific modal lock for arrows, A/D, D-pad and left stick.
- Added visual fall/down/rise recovery, camera response and a fall-aware contact
  shadow while keeping the physical player root upright and stationary.
- Preserved the maximum-intoxication terminal behavior of all four minigames
  without applying a separate expiring status after completion or cancellation.
- Extended the shared F9 window with clickable and Left/Right-arrow `-20/+20`
  controls that clamp session intoxication while preserving drink context.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Complete Unity EditMode suite: 332/332 passed.
- Complete headless Unity PlayMode suite: all 71 runnable checks passed;
  three RenderGraph/realtime-shadow pixel checks were ignored because the
  null graphics device cannot execute them.
- Focused intoxication status cycle: 3/3 passed, including threshold cancel
  and failure/fall/recovery/cooldown.
- Source inspection found no remaining timed-status identifier or localized
  key under `Assets`; `git diff --check` passed.

## 2026-07-28 — Contextual intoxication HUD

- Kept the intoxication HUD completely hidden at `0` while preserving its
  existing modal visibility state.
- Made the panel appear immediately for any positive intoxication value and
  disappear again if drinking progress resets to zero.
- Added a focused state-transition regression check.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Focused intoxication-HUD EditMode check: 1/1 passed.

## 2026-07-28 — Reduced player maximum speed

- Halved the player motor's maximum planar speed from `5.2 m/s` to
  `2.6 m/s`.
- Kept the existing `6.5 m/s²` acceleration, `11 m/s²` braking, camera
  response and procedural gait unchanged. No sprint state or run animation
  was added.
- Updated the inertial movement checks for the new cruising speed, stopping
  distance and direction-reversal timing.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Focused player-motor PlayMode suite: 6/6 passed.

## 2026-07-28 — Classic fixed-camera door transition

- Added `DoorTransition` as a dedicated third build scene and taught the
  runtime bootstrap and editor scene setup to install its matching root.
- Routed both bar entry and exit through one guarded
  `source -> DoorTransition -> destination` chain. The destination preloads
  while activation remains blocked until the complete `3.15 s` sequence
  returns to black.
- Added a deterministic unscaled timeline for reveal, handle turn, low-poly
  door opening, camera push and final fade. The doorway remains a solid black
  sprite while the leaf swings outward toward the player instead of receding
  into the opening. Entering uses warm door lighting; exiting uses a colder
  gray-green treatment.
- Split the existing door sound into the short latch cue plus a generated
  sustained hinge creak, played from the animated door.
- Extended the scene-flow contract to observe both transition directions and
  reject a second request while the first chain owns the transition guard.

Verification:

- Runtime, Editor, EditModeTests, PlayModeTests and Assembly-CSharp .NET
  builds: 0 errors, 0 warnings.
- Complete Unity EditMode suite: 310/310 passed.
- Focused door-scene and full round-trip PlayMode checks: 2/2 passed; the
  deterministic door timeline passed 4/4 EditMode checks.
- Complete headless Unity PlayMode suite: 65 passed, 0 failed and 3
  graphics-only tests skipped. All three skipped GPU tests passed in the D3D
  run; its only failure was the pre-existing real-time-sensitive opposite
  input motor check, which passed on a focused retry.
- D3D visual capture inspected the open outward pose and confirmed that the
  black doorway sprite fully covers the aperture beneath the frame.
- Windows Player build: succeeded, `135687344` bytes, 0 warnings.

## 2026-07-28 — Restricted city fog visibility

- Increased the City-only exponential-squared fog density from `0.048` to
  `0.070` and limited the exterior camera far plane from `220 m` to `48 m`.
  The fog reaches near-total opacity before the clip plane.
- Matched the solid City camera background exactly to the terminal fog color.
  Empty pixels beyond finite or clipped geometry now continue the gray-green
  haze instead of exposing the former dark sky as a world boundary.
- Raised the existing local fog gradient from `0.060 / 0.082` to
  `0.080 / 0.120` alpha without increasing its 36-particle budget, emission
  rate or footprint.
- Kept `BarInterior` fog-free and made its return to the default `220 m`
  camera range an explicit regression contract.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Focused City/BarInterior atmosphere PlayMode: 2/2 passed.
- The initial D3D11 density capture kept the player and current junction
  readable, while a follow-up live `1920x1080` Game-view check exposed the
  darker clear color between distant buildings and drove the terminal
  backdrop correction.
- Complete Unity EditMode suite: 304/304 passed.
- Complete headless Unity PlayMode retry: 64 passed, 0 failed and 3
  graphics-device checks skipped as expected; one pre-existing inertial motor
  test was transient on the first run, then passed both isolated and in the
  complete retry.

## 2026-07-28 — Opaque diagonal player heads

- Compared the four diagonal atlas frames with the locked turntable and
  restored exactly 51 genuine subject pixels left transparent by the original
  chroma-key pass: 12 `FrontRight`, 13 `BackRight`, 14 `BackLeft` and 12
  `FrontLeft`.
- Rebuilt the reference, nine-part and five-expression atlases
  deterministically. All 51 reference changes are binary `0 -> 255` alpha;
  the body layer receives the same pixels and every expression row preserves
  them.
- Split heuristic face-scan and explicit edge-repair validation in the atlas
  builder, retaining both migration from the previous atlas and idempotent
  zero-change rebuilds.

Verification:

- EditModeTests .NET build: 0 errors, 0 warnings.
- Deterministic builder rerun: 0 repairs; output SHA-256 remained stable.
- Legacy-atlas migration reproduced the committed expression-atlas SHA-256.
- Visual check of all four diagonal frames against a contrasting background:
  no internal head/neck transparency remains.
- Turned-head alpha regression: 1/1 passed; complete player asset suite: 8/8
  passed.
- Complete Unity EditMode suite: 304/304 passed.
- Facial runtime and full player presentation PlayMode: 1/1 and 15/15 passed.
- Complete headless Unity PlayMode suite: 64 passed, 0 failed and 3
  graphics-device checks skipped as expected.
- D3D graphics shadow render checks: 2/2 passed.
- Windows x64 Player build: succeeded, 0 warnings, 135,673,543 bytes.

## 2026-07-28 — Articulated directional player shadow

- Replaced the single static full-body shadow card with nine collider-free
  `ShadowsOnly` body and limb renderers.
- Kept the shadow view authored relative to the main light while remapping the
  live smoothed joint angles into that view. Walking, footfall compression,
  idle gestures and the then-current intoxication motion now reshape the
  projected silhouette.
- Added component lifecycle handling plus regressions for actor translation,
  opposite gait phases and the actual D3D receiver pixels.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Complete Unity EditMode suite: 303/303 passed.
- Complete headless Unity PlayMode suite: 64 passed, 0 failed and 3
  graphics-device checks skipped as expected.
- Player shadow behavior PlayMode: 3/3 passed.
- Full player presentation PlayMode: 15/15 passed.
- D3D graphics shadow render checks: 2/2 passed, including a pixel-level
  animated-silhouette comparison.
- Windows x64 Player build: succeeded, 0 warnings, 135,673,543 bytes.

## 2026-07-28 — Grounded player foot contact

- Lowered the runtime visual baseline from `0.04 m` to `0.005 m`, matching the
  atlas foot pivot instead of leaving a permanent visible gap above the
  controller ground origin.
- Added explicit left/right sole contacts derived from the eight directional
  atlas poses. Each frame grounds the lower support contact; the other foot
  remains free to swing and the support alternates across half-cycles.
- Replaced the always-positive `0.035 m` whole-puppet bob with a `0.012 m`
  upper-body impact compression and `0.005 m` sole compression. Idle
  breathing now offsets only the body and arm roots.
- Added one collider-free shared four-vertex contact quad with a dedicated
  transparent URP shader. It follows the player root, not `PoseRoot`, and
  remains available independently of the realtime directional shadow.
- Hardened the generated shadow lifecycle: disabling the contact component
  hides its renderer, while subsystem resets dispose and lazily rebuild shared
  generated materials and mesh resources.

Verification:

- Runtime and PlayModeTests .NET builds: 0 errors, 0 warnings.
- Complete Unity EditMode suite: 303/303 passed.
- Complete headless Unity PlayMode suite: 63 passed, 0 failed and 3
  graphics-device checks skipped as expected.
- Deterministic grounded gait PlayMode: 1/1 passed.
- Player shadow behavior PlayMode: 2/2 passed.
- Full player presentation PlayMode: 15/15 passed.
- D3D graphics shadow render checks: 2/2 passed.
- City footfall/opposite-support visual capture: 1/1 passed.
- Windows x64 Player build: succeeded, 0 warnings, 135,672,519 bytes.

## 2026-07-28 — Heavy inertial locomotion

- Replaced one-frame planar speed changes with `6.5 m/s²` acceleration toward
  the existing `5.2 m/s` maximum and `11 m/s²` braking. A normal release now
  produces about `1.23 m` of controllable coasting from full speed.
- Fed actual `CharacterController` displacement back into the velocity state
  and removed its minimum movement threshold, preserving frame-independent
  low-speed acceleration without storing pressure at road edges or
  collisions.
- Kept input disable, modal ownership, scene transitions and teleport as hard
  planar stops. Direction reversal brakes old momentum before accelerating in
  the opposite direction.
- Replaced the fixed `2.2 cycles/s` gait with one cycle per `2.7 m` travelled,
  matched full animation amplitude to `5.2 m/s`, softened joint settling from
  `12` to `8` and increased body rock from `1.4°` to `1.8°`.

Verification:

- Runtime, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Focused inertial motor PlayMode: 6/6 passed.
- Full player presentation PlayMode: 15/15 passed.
- Facial-state locomotion regression: 1/1 passed.
- Complete City/BarInterior scene flow: 9/9 passed.
- D3D11 acceleration/braking visual sequence: 1/1 passed; sampled speeds were
  `0.00 → 2.17 → 4.12 → 5.20 → 3.21 → 0.00 m/s`.
- Windows x64 Player build: succeeded, 0 warnings, 135,663,527 bytes.

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
- Copied whole-puppet bob, weight shift and the then-current intoxication sway
  to the shadow proxy while leaving the nine visible jointed renderers and
  their materials unchanged.
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
  the then-current 45-second intoxication effect started only when the modal
  closed. Reopening advances a deterministic per-controller board sequence.

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
  sustained idle and reset during locomotion or the then-current strong
  intoxication state; blink timing continues in either state.
- Kept the nine-renderer hierarchy, authored asymmetry and non-mirrored views.
  All three rear directions remain byte-identical to neutral.
- Extended regression coverage for exact atlas output, pairwise-distinct
  facial states, blink-under-motion, strong-intoxication suppression,
  sagittal depth sorting and the left-then-right idle gesture sequence.

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
  locomotion and was suppressed under the then-current strong intoxication
  state without changing scale, heading or the nine-renderer hierarchy.
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
  retained walk bob/rock, idle settling and the then-current intoxication sway.
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
  rotates shoulders, elbows, hips and knees, while bob/rock and the
  then-current intoxication sway affect the complete puppet.
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
- Preserved prototype walking bob/rock and the then-current intoxication sway
  through the single-renderer hierarchy in both runtime scenes.
- Updated player/camera tests, import checks, architecture maps, player art
  specification and release notes. Full multi-frame idle/walk animation remains
  the next art pass after approval of the static vertical prototype.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests .NET builds:
  0 errors, 0 warnings.
- Unity EditMode: 211/211 passed.
- Unity PlayMode with D3D11: 29/29 passed, including all eight view centers,
  camera-independent heading, input movement, prototype intoxication motion,
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
  visited and do not persist intoxication, drinks or the then-current
  temporary status.

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
  `GameSessionState` after each serving; bad served mixtures then deferred a
  45-second intoxication effect until finish/close, and 100 intoxication ended
  early.
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
- Added the then-current 45-second intoxication debuff with `0.75` movement
  speed, sprite sway, localized HUD and result feedback.
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
