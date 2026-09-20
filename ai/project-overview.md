# Project overview

## Current baseline

- **Барный Променад / Bar Promenade** is a playable Windows/PC Unity vertical
  slice with a runtime-composed coastal city, mountain road, alpine village
  and their interiors. Detailed world facts live in [current-world.md](current-world.md).
- Unity `6000.6.0f1`, URP `17.6.0`, Input System `1.20.0`. Actual versions are
  owned by `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json`.
- One active PC quality/pipeline profile applies the PS1 composite after URP
  post-processing. Keyboard, mouse and gamepad retain their existing controls.
- Thirteen scenes are enabled in `ProjectSettings/EditorBuildSettings.asset`:

| Index | Scene | Role |
| --- | --- | --- |
| 0 | `MainMenu` | New Game / Combat Test / Quit |
| 1 | `City` | Gameplay |
| 2 | `DoorTransition` | Door presentation |
| 3 | `BarInterior` | Gameplay |
| 4 | `SupermarketInterior` | Gameplay |
| 5 | `StairwellInterior` | Gameplay |
| 6 | `HomeInterior` | Gameplay |
| 7 | `MountainRoad` | Gameplay |
| 8 | `AreaLoading` | Area-transfer presentation |
| 9 | `ChurchInterior` | Gameplay |
| 10 | `AlpineVillage` | Gameplay |
| 11 | `MothersHouseInterior` | Gameplay |
| 12 | `CombatTest` | Isolated melee test |

All ten gameplay roots use `PlayerFactory`/`Resources/Player/Player3DV2.prefab`:
one rig, `PlayerWardrobe` slots/coverage and bounded hair/jacket physics.
Near-empty scenes compose validated plans at runtime. Authored geometry follows
the Blender and world-canon rules in [../AI.md](../AI.md).

## Technical ownership

| Owner | Responsibility |
| --- | --- |
| `BarPromenade.Rules` | Engine-independent calendar, fish supply, melee timing, vehicle ownership and input priorities |
| `BarPromenade.Runtime` | World composition, gameplay, rendering, audio and shared input bindings |
| `BarPromenade.Editor` | Authoring/import, scene setup, read-only player-build asset gate and diagnostic commands |
| `BarPromenade.TestSupport` | Shared test lifecycle support, including listener muting |
| `BarPromenade.EditModeTests`, `BarPromenade.PlayModeTests` | Pure/asset contracts and focused runtime scenarios |
| `tools/` | Deterministic generators, pinned toolchain, staging/publishing and native-audio build |

`GameSessionState` remains the public session facade. Calendar rules and
`VehicleActivityState` have separate owners; the latter resets itself and
returns ride leases that cannot affect a later session. Persistent journey
progression is distinct from this temporary ownership. Disk save/load is not
implemented.

`GameInput` centralizes shared action aliases and device reads used by movement,
interaction and common menus. `GameInputPolicy` gives pause, transitions and
modal ownership priority while preserving balance-recovery movement. Specific
look/debug controls remain local; a rebinding interface is not implemented.

Area travel unloads the source and loads the destination in Single mode.
City/MountainRoad/AlpineVillage share incremental construction with their
synchronous `Build` compatibility entry points. The loading overlay survives
scene activation until the destination root completes: 20% represents scene
loading and 80% construction. A best-effort 8 ms budget yields between
indivisible stages; it is not a bound on an individual stage. World time,
input and gameplay audio remain paused during construction. Transition owners
release held scene activation and owned state on disable/destroy.

The accepted `2026-09-06` loading presentation uses one static painterly image
per directed edge of City ↔ MountainRoad ↔ AlpineVillage and a bottom progress
bar. Direct map transfers across the middle area use the last leg's image.
This bounded UI exception adds no text, story fact or additional travel time;
see art-bible §15a and the accepted architecture decision.

An optional bounded performance capture reports frame intervals, main/render
threads, available GPU timing, allocations and foot-bake/reflection scopes.
Unsupported metrics stay unavailable. Reports are measurements for the captured
machine and scene, not a project-wide performance guarantee.

`PlayerBuildAssetValidation` gates packaging with read-only asset checks and
repair commands. Generation/publication: [../tools/README.md](../tools/README.md).

## Implemented capabilities

- Crowbar test: buffered strikes, simultaneous exchanges, wall recoil and
  reactive guard/rest sparring; rounds have no story consequences.
- A validated connected city with streets, river/shore, neighbourhoods,
  cemetery, church, deterministic weather, residents and Route 01 transport.
  First unpaused dock entry starts finite port→factory→shop supply once;
  catch-up survives scenes/reentry, excluding pre-entry time. Three units,
  two cranes, crews and a
  `6.481 x 2.4 x 3.2 m` closed truck/full cab share the cycle.
  After its existing central factory-exit turn, it keeps/parks right and yields
  locally to the bus. Six-second traffic access protects actual bodies; graph
  turns avoid street furniture. Streets stay `8 m`/`6 m` asphalt; stops stay put.
  Truck spawns on unseen road, parks with first crate; later trips leave factory.
  Two soft arrival horns, reverse beeper/ajar door/look-back steering. Cranes
  continue while the driver yields at the store entrance. Local carts go to
  cargo/return; doors→guided lift unfolds/lowers, closing reverses above ground.
  Store clearance releases driver; jamb bumpers stay outside.
  Paired city/weather complaints shuffle without repeats; shore breaks use the
  existing canopy. Driver talks only to docker; idle is pause-aware.
  Moving grips, vessel horn/echo and warm shadowed searchlight share the scene.
  First crate starts FIFO at twice authored speed; same cans fill three cartons.
  The athletic `1.96 m` receiver wears 3D glasses; he weighs/nods/stores before
  loading. He and the chestnut-haired seamer have painted faces/own actions.
  Four wait/talk outside, queue through doors; side doors reach the public aisle,
  west staff opening the work aisle. Grips win; pause freezes life. Fittings/light
  and fan/contact/pressure sounds follow activity. Distant meshes sleep while
  production/collision/traffic continue; approach restores without replay.
  Hero observes; shop stock stays. Anchored port/factory voices use local reverb via
  `CityWorkAudio`; the shared Music bus is reduced by `6 dB`.
- Separate mountain/village areas, cableway travel and accessible interiors.
- Six village residents perform finite household work; optional hero help and
  the resulting deliveries, repairs and snow clearing survive scene visits.
- One shared animated hero, contextual interactions, intoxication/balance,
  session clock, needs, inventory, purchases, dated quests and grave work.
- A collectible scarf in the mother's upstairs bedroom has reversible inventory
  equipment and reduces alpine shivering and the rate of new frost exposure.
  After five seconds of free third-person play it lowers outside Alpine Village
  and rises there; its resting posture survives travel and yields to mouth actions.
  Its yellow wrap and 45 cm tail follow wind/motion with simple hero contacts;
  running lifts the tail. External objects/NPCs are ignored. Detailed contacts
  remain diagnostic-only; see [current-world.md](current-world.md).
- Home toilet choices share actual hand-operated lid actions; the seated
  branch coordinates the original rig, lowered clothing, inspection/flush
  and an underwater camera that returns while the hero dresses.
- Map, inventory, journal, pause/options and localized interaction interfaces.
- Shared PS1 presentation, causal audio and bounded diagnostics.

See [current-world.md](current-world.md) for the complete gameplay catalogue and
[systems-map.md](systems-map.md) for the concise system index, whose Key files
column names the owner of each system. For paths, use `git ls-files` and `rg`;
the hand-maintained tree was retired because half its entries were wrong or
missing. The story/art bibles define canon; these implementation documents do
not authorize deviations.

## Deferred

Long-term saves, broader economy/story progression, release/platform work and
other omissions are itemized in [current-world.md#deferred](current-world.md#deferred).
A `Partial` system states its own gap in the systems map. Existing code and
coverage do not imply that any broad regression or player build has just run.

## Verification policy

FAST is the default for ordinary work, including shared changes. Use one
primary relevant check, with one additional focused check only where a shared
framework needs it. Complete suites require an explicit release/full-regression
request; a build request authorizes that build, not extra suites or a smoke.
[../AGENTS.md](../AGENTS.md) owns the policy; [prompt-templates.md](prompt-templates.md)
implements it. Record actual execution evidence in the work log, separately
from these current implementation facts.
