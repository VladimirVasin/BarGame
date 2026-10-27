# Project overview

## Current baseline

- Windows/PC Unity, Mac Editor supported; runtime-composed city/road/village/interiors.
  [World](current-world.md).
- Unity `6000.6.7f1`, URP `17.6.0`, Input System `1.20.1`, Navigation `2.0.16`:
  `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`.
  Editor Play reloads domain+scene (`m_EnterPlayModeOptions=0`).
- PC:native/SMAA High/saved effects/grade/UI;City/balcony Soft;bar Neutral/bounce.
  City/Home/bar UV0+1;house air. Bar:WC wear/mirror/sign/local light.
  Maps/response/cubes. Pilots:
  coastal stems(City crowns static);one road RGB chunk/
  pistol slide normals;fountain foam/mips. Caps/geometry retained.
- Thirteen scenes are enabled in `ProjectSettings/EditorBuildSettings.asset`:

| Index | Scene | Role |
| --- | --- | --- |
| 0 | `MainMenu` | Start menu / combat / vocalizer |
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

`GameSessionState`: session facade; separate calendar/`VehicleActivityState`
ownership. Vehicle reset/ride leases cannot affect later sessions. Journey
progress is distinct from temporary rides; no disk save/load.

`GameInput`/`GameInputPolicy`: bindings; pause/transition/modal priority.
Balance recovery live; unfocused movement=0; return:neutral WASD/stick.
Mouse orbit: no button; RMB aims; Alt frees cursor/pauses mouse look/combat.
`GameCursor`: leases; UI frees cursor; focus return drops delta.
Impulses kept; local look/debug; no rebind UI.

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

Opt-in performance capture reports frame/GPU/GC, inclusive combat/NPC CPU and
separate City-fountain/warehouse-probe work. Unsupported counters stay unavailable;
Editor captures give local evidence, not a project-wide performance guarantee.

`PlayerBuildAssetValidation` gates packaging with read-only asset checks and
repair commands. Generation/publication: [../tools/README.md](../tools/README.md).

## Implemented capabilities

- CombatTest:warehouse/fish-key-gate;all3fish→power-music-spawn0.
  Endless1–5/def3:Crane/Room/Docker;Crane last≤2.
  Refillrng;≤5active/≤6prepared;Lab:oldArena/6kinds/1target.
  HP2/head2/wounds;Room:head0→ret+2s/retlaugh5→idle5;simlive/otherAI0;3weapons/finiteammo.
  Crane:up2m/5s/E6/overrails/others idle/bodycam/fall→home10;fatal3→regen10/kill0.
  Hero:wounds/no detach/frontguard. Proxy:skin/aim.
  Gap:pierce/balance. [World](current-world.md)
- City: compact working west/residential east districts plus park; 144 stable
  lots on 29.2% less declared land. Residential3–5 storeys, short fronts and
  courts/passages/wings; shared street/pavement contours and Home views. Four
  POIs/landmarks, shore/church and Route01 remain.
  Utilities.
  First unpaused dock entry starts finite port→factory→shop supply once;
  catch-up survives scenes/reentry, excluding pre-entry time. Three units,
  two cranes, crews and a
  `6.481 x 2.4 x 3.2 m` closed truck/full cab share the cycle.
  After its existing central factory-exit turn, it keeps/parks right and yields
  locally to the bus. Six-second traffic access protects actual bodies; graph
  turns avoid street furniture. Street reservations stay `8 m`/asphalt `6 m`;
  `2 m` pavements/stops follow the layout.
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
- Mountain/village/cableway/interiors; local physical maps/unique closeups.
- Six named village neighbours; household help persists across visits.
  27 silent RU/EN inspections: props side/bokeh, paper/photos close-up.
  Lodge minibar/chairs; group photo inspect/pickup, shared receipt/session state.
  31 homes have fixed families, four occupied. Hero/mother remain unnamed.
- One shared animated hero, contextual interactions, intoxication/balance,
  air inertia/steering,clock,needs,inventory,quests and grave work.
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
- Map/inventory/journal/pause/options/localized;warehouse door ambush/debug.
- Shared styles, causal audio and bounded diagnostics.

## Deferred

Long-term saves, broader economy/story progression, release/platform work and
other omissions are itemized in [current-world.md#deferred](current-world.md#deferred).
A `Partial` system states its own gap in the systems map. Code and coverage do not imply a fresh regression or build.

## Verification policy

FAST defaults to one relevant check, plus a focused check only for a shared
framework. Full suites require an explicit release/full-regression request;
a build request authorizes that build, not extra suites or a smoke.
[../AGENTS.md](../AGENTS.md) owns the policy; [prompt-templates.md](prompt-templates.md)
implements it. Record actual execution evidence in the work log, separately
from these current implementation facts.
