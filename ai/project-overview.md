# Project overview

## Current baseline

- **Барный Променад / Bar Promenade** is a playable Windows/PC Unity vertical
  slice with a runtime-composed coastal city, mountain road, alpine village
  and their interiors. Detailed world facts live in [current-world.md](current-world.md).
- Unity `6000.6.4f1`, URP `17.6.0`, Input System `1.20.0`;versions:
  `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`.
  Editor Play reloads domain+scene (`m_EnterPlayModeOptions=0`).
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
| 12 | `CombatTest` | Isolated weapon test |

All ten gameplay roots use `PlayerFactory`/`Resources/Player/Player3DV2.prefab`:
one rig/skin/volume/wardrobe; recorded hair/world-jacket,live fallback.
CombatTest jacket/scarf/sling follow rig/anchors without physics; hair kept.
Near-empty scenes compose validated plans. Geometry: Blender/canon in [../AI.md](../AI.md).

## Technical ownership

| Owner | Responsibility |
| --- | --- |
| `BarPromenade.Rules` | Engine-independent calendar, fish supply, melee timing, vehicle ownership and input priorities |
| `BarPromenade.Runtime` | World composition, gameplay, rendering, audio and shared input bindings |
| `BarPromenade.Editor` | Authoring; ordered hero imports; scenes; read-only player-build asset gate; diagnostics |
| `BarPromenade.TestSupport` | Shared test lifecycle support, including listener muting |
| `BarPromenade.EditModeTests`, `BarPromenade.PlayModeTests` | Pure/asset contracts and focused runtime scenarios |
| `tools/` | Deterministic generators, pinned toolchain, staging/publishing and native-audio build |

`GameSessionState` remains the public session facade. Calendar rules and
`VehicleActivityState` have separate owners; the latter resets itself and
returns ride leases that cannot affect a later session. Persistent journey
progression is distinct from this temporary ownership. Disk save/load is not
implemented.

`GameInput`: shared bindings/device reads; `GameInputPolicy`: pause/transition/
modal priority, balance recovery stays live. Unfocused movement input=0;
return waits for neutral WASD+left stick. Impulses remain. Look/debug stay local;
rebinding UI is not implemented.

Area travel uses Single loading; City/Village doors retain one dormant exterior.
Builders share incremental/synchronous entry points. Overlay: 20% loading/80%
construction; 8 ms best-effort yields between indivisible stages. Time/input/
gameplay audio pause until ready; teardown releases ownership. Village terrain
and surfaces share lazy grid heights keyed by plan/brook, invalidated on change;
snow evaluates retained vertices and paths reuse edge work.

Litter palette, forest chunks, hair detail, greetings and port BC7:
accepted `2026-10-13` decision in [architecture-notes.md](architecture-notes.md).

The accepted `2026-09-06` loading UI uses a static painterly image per directed
City ↔ MountainRoad ↔ AlpineVillage edge and a bottom bar; cross-area map travel
uses its last leg. It adds no text, story fact or travel time; art-bible §15a
and the accepted architecture decision define this bounded exception.

An optional bounded performance capture reports frame intervals, main/render
threads, available GPU timing, allocations and foot-bake/reflection scopes.
Unsupported metrics stay unavailable. Reports are measurements for the captured
machine and scene, not a project-wide performance guarantee.

`PlayerBuildAssetValidation` gates packaging with read-only asset checks and
repair commands. Generation/publication: [../tools/README.md](../tools/README.md).

## Implemented capabilities

- CombatTest:warehouse/key-valve-gate/doorstub;
  3fridges/Take→redoff;all3→whitebulb/mechgrind-musicchew0/newspawns0-livekept;
  overhead fish/cansgone→key/receipt/keep;Lchest Spot/torso/fill→black;zombies/lunge;Standard1/Endless1–5/≤2corpses/≤5pool.
  All3/1-2-3/finiteammo+4packs/safe rise/novice aim/corpse step;
  gap:pierce/balance. [World](current-world.md).
- City: 20 metre types; district heights, curved old/service streets,
  courts/passages/wings, unified ground; river/shore, church and Route 01.
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
- Mountain/village areas, cableway/interiors; abandoned village surrounds its lived-in core.
- Six named village neighbours; household help persists across visits.
  27 silent RU/EN inspections: props side/bokeh, paper/photos close-up.
  Lodge minibar/chairs; group photo inspect/pickup, shared receipt/session state.
  31 homes have fixed families, four occupied. Hero/mother remain unnamed.
- One shared animated hero, contextual interactions, intoxication/balance,
  session clock, needs, inventory, purchases, dated quests and grave work.
- A collectible scarf in the mother's upstairs bedroom has reversible inventory
  equipment and reduces alpine shivering and the rate of new frost exposure.
  After five seconds of free third-person play it lowers outside Alpine Village
  and rises there; its resting posture survives travel and yields to mouth actions.
  Outside CombatTest, yellow wrap/45 cm tail follow wind/motion/hero contacts;
  running lifts it. External contacts ignored; detailed contacts diagnostic-only.
  See [current-world.md](current-world.md).
- Home toilet choices share actual hand-operated lid actions; the seated
  branch coordinates the original rig, lowered clothing, inspection/flush
  and an underwater camera that returns while the hero dresses.
- Map, inventory, journal, pause/options and localized interaction interfaces.
- Shared PS1 presentation, causal audio and bounded diagnostics.

See [current-world.md](current-world.md) for gameplay and
[systems-map.md](systems-map.md) for owners. Paths: `git ls-files`/`rg`; the bibles own canon.

## Deferred

Long-term saves, broader economy/story progression, release/platform work and
other omissions are itemized in [current-world.md#deferred](current-world.md#deferred).
A `Partial` system states its own gap in the systems map. Existing code and
coverage do not imply that any broad regression or player build has just run.

## Verification policy

FAST defaults to one relevant check, plus a focused check only for a shared
framework. Full suites require an explicit release/full-regression request;
a build request authorizes that build, not extra suites or a smoke.
[../AGENTS.md](../AGENTS.md) owns the policy; [prompt-templates.md](prompt-templates.md)
implements it. Record actual execution evidence in the work log, separately
from these current implementation facts.
