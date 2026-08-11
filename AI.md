# AI project entry point

Read this file first, then use [`ai/README.md`](ai/README.md) as the documentation index.

## Reality check

The Unity 6 URP vertical slice is implemented. It generates a finite connected,
blueprint-driven city with a `12 x 12` urban core, a fixed traversable central
park, a reachable northern beach and water edge, and four urban districts with
four graph-separated bars. The default footprint now extends east to a
reachable `4 x 4` lake with a walkable shore and blocked water plus a reachable
`3 x 2` cemetery; both have deterministic physical landmarks and street
access. The sparse footprint can be non-rectangular, and the same data-first
area contract supports reordered urban areas. The runtime places one visually
distinct player home beside a bar street and one deterministic street-front
supermarket, instantiates the same modular low-poly 3D hero in all five gameplay
roots, loads separate bar, supermarket, stairwell and home interiors, and
restores the same seed and matching exterior return point. The hero keeps
independent body meshes on one Generic rig, uses continuous in-place 3D clips
for locomotion and contextual actions, hands failed balance falls from a
directional clip into a bounded runtime ragdoll and back into an authored rise,
and derives first-person arms and the inventory portrait from the same
production model. Road v2 gives ordinary City streets an `8 m` footprint
with a `6 m` carriageway and two raised `1 m` sidewalks. At selected eligible
perpendicular two-way corners and three- or four-way nodes, Road v2.1 moves the
four `1 m` corner pads outward, cuts the raised curbs back by `4.5 m` on every
real Street approach and exposes the shared asphalt apron required by the
production bus; those streets also host deterministic pedestrian and vehicle
graphs. A selected apron may share flat
zebra paint and paired signals, but a bus maneuver is retained only when its
sampled full body clears both physical poles at a conservative `0.30 m` radius.
At most two ambient walkers exist near the player: they spawn one at a time
after wide, independently randomized delays at randomly
  ranked obstacle-safe anchors in the preferred `76-86 m` band, where the
  fixed City fog has already hidden them. If those anchors belong only to graph
  components that cannot reach the player, the director falls back to a linked
  fog-hidden anchor `32-86 m` away. Until a fresh walker first reaches the `24 m` encounter
  radius, eligible graph turns favor the continuation closest to the current
  player position; that one-shot guidance then ends and ordinary random roaming
  resumes. During daytime their still-distant simulation smoothly accelerates
  up to `2.75x` and returns to authored pace by `32 m`, so hidden actors approach
  or recycle without occupying both slots for long. They keep moving forward
  through graph turns,
  independently choose whether to use each zebra crossing, and return to their
  pool only beyond `88 m` from the hero. Camera direction and frustum state do
  not take part in this lifecycle. Strict night (`19:00-06:00`) spawning is
  limited to one slot, uses much longer random delays and retains authored
  simulation pace; a second walker already active at dusk is not culled early.
  Their compatible Generic rig
  uses the hero's shared Idle and Walk clips. Home maps the same graph into the
  bounded street view below the balcony. Its two slots are enabled only while
  the Balcony camera shot is active; returning indoors releases them as a scene
  boundary.

One full-size ambient midibus may also be active near the player, although its
fog-hidden spawn cadence deliberately allows periods with no visible bus. The
actual `8.25 x 2.38 x 2.95 m`, `4.5 m`-wheelbase 3D vehicle has a visible
twelve-seat interior, driver area, two animated double-leaf doors that fold
inward around fixed outer posts, steering and rolling wheels, a synthesized
engine loop and time-of-day head, tail and cabin lights. Its separate passive
`CityBusDriver3D` uses the shared `Player3DLit` material and exact 31-bone rig,
with a normal low-poly head and long horizontal eyes. Procedural seated IK keeps
both hands on the rotating wheel grips; the deterministic door timeline moves
only the right hand to a real dashboard button with `12 mm` travel, keeps the
left hand planted, and keeps the head turned toward the front door while it is
open before returning it during closing. The long eyes blink independently;
when the hero comes within `2.75 m` of the outside of the front entrance, the
driver focuses on the hero's actual head and the neck/head segment stretches
up to `0.10 m` with a deliberately uncanny `1.35x` cap. While the bus travels, a
presentation-only sprung body adds speed-scaled cartoon heave up to `0.045 m`,
pitch up to `0.8` degrees and roll up to `1` degree while the four wheel
assemblies remain grounded; the route actor and collider do not move with it.
Route 01 is one deterministic right-hand, Street-only closed winding service
loop. The planner targets every district point of interest that actually exists
in the layout and then the player home; the default city therefore has five
semantic stops in Industrial, Nightlife, Residential, Old Town and Home order.
Each stop sits on a safe straight whose physical street edge is either a target
frontage or one connected edge away. Its blue `01` pole stays on a different
roadside cell and outside the POI public/access bounds or Home footprint. The
loop connects those target straights only through previously accepted,
full-body-clear links: ordinary straights, proven `6 m`-radius left turns and,
only at selected Road v2.1 nodes, a two-edge safe-right macro. That macro uses a
long S-merge over the full incoming Street, a `4.5 m` quarter-turn through the
clear core and a symmetric S-return over the outgoing Street; it owns both
physical edges so routing cannot bypass a stop-bearing edge. Ordinary tight
`3 m` right turns remain rejected. Route selection has no random branching or
player pursuit, and a repeated physical link receives a unique ordered route
occurrence. A deterministic door/driver timeline serves every stop once per lap
with a fixed `10 s` total dwell, including the existing `0.70 s` door-opening
and `0.70 s` door-closing transitions. Random roadside decoration does not
create bus shelters.
Nightlife's Last Route Island now has a working Route 01 pole nearby but outside
the POI itself, so its abandoned island structures remain distinct from the
live stop. Spawning prefers obstacle-safe, fog-hidden route poses `76-86 m` from
the player and may fall back to `56-86 m` only when forward travel on the same
loop can approach them; recycling uses the complete vehicle bounds only after
they are at least `92 m` from the hero, and resets the wheel, button, driver
hands and head to neutral. The bus yields to the player and active
pedestrians, while camera direction and frustum state never control its
lifecycle. The City map draws Route 01 as a blue ink-outlined line beneath the
orange player itinerary, plus five numbered localized stop markers in the
default layout and a compact legend; live bus tracking and boarding are not
implemented. The moving bus runtime is deliberately City-only. Home's balcony
reconstructs the nearby Home stop as a static collider-free `01` pole but has no
bus actor or director: the real exterior has no Street pass-through with both
complete-body seams hidden at `56 m`, and the default home faces a visible road
terminal. The project does not fabricate another road or make bus appearance
depend on the Balcony camera, avoiding a visible activation/pooling pop.

The build starts in `MainMenu`, resets a fresh session and opens the existing
Home interior in a one-shot sleeping presentation. Its first Home frame holds
on a silent `05:59` alarm clock whose complete display flickers briefly at
long intervals. For five seconds there is no menu input; then the localized
PS1-style `WAKE UP`/`QUIT` menu appears while the clock stays silent and keeps
showing and flickering `05:59`. Only Wake Up switches it to solid `06:00` and
starts both the alarm and the session clock. The clock shot and sleeping loop
hold for three more unscaled seconds; when the alarm stops, the continuous
six-second camera and wake animation begin and settle into the normal Home
shot. Ordinary later bed wakes retain their two-second timing.

Fresh-session time is frozen at `05:59` until that successful startup Wake,
then advances from `06:00` on scaled time at `1.0` game minute per real second.
The clock persists across scene loads and drives the Home display, inventory
time readout and shared City/Home window and balcony lighting; one complete
in-game day is exactly
`1440` real seconds (`24` minutes). Night is before
`06:00`, dawn is `06:00-07:00`, day is `07:00-18:00`, dusk is
`18:00-19:00`, and night resumes at `19:00`. City fog, its matching
background, `48 m` far clip, `CityFogField` and `CityNoirVolumeProfile` do not
change with time; Bar, Supermarket and Stairwell visuals remain unchanged.

Startup truth begins at `Assets/Scripts/Runtime/Scenes/MainMenuRoot.cs` and
`Assets/Scripts/Runtime/Scenes/HomeOpeningController.cs`; generated-city truth
continues from `Assets/Scripts/Runtime/Core/CityGameRoot.cs` and
`Assets/Scripts/Runtime/World/CityLayoutGenerator.cs`; supermarket truth starts
at `Assets/Scripts/Runtime/Scenes/SupermarketInteriorRoot.cs` and
`Assets/Scripts/Runtime/World/SupermarketInteriorLayoutPlanner.cs`. Session-time
truth lives in `Assets/Scripts/Runtime/Core/GameTimeState.cs`,
`GameTimeRuntime.cs` and `GameTimeDayNightRules.cs`.

Runtime support diagnostics are written as bounded NDJSON through
`Assets/Scripts/Runtime/Diagnostics/`; see `ai/debug-log.md` for profiles,
paths and event boundaries.

## Source-of-truth order

1. Files currently present in the repository.
2. Unity project and package settings.
3. `ai/architecture-notes.md` for accepted decisions.
4. Planning documents for intended work.

Never report a planned system as implemented without inspecting relevant
repository evidence. This does not require running every test layer.

## Working agreement

- Use the canonical workflow matching the task in `ai/prompt-templates.md`.
- Fast targeted verification is the default. Complete suites require an
  explicit release/full-regression request. Create a player build only when it
  is the requested deliverable or release gate; add a smoke only when requested
  or when packaged startup behavior is the changed contract.
- Start from `ai/project-overview.md` and `ai/systems-map.md`.
- All future contextual player animations must follow the mandatory
  `ai/contextual-animation-standard.md`; do not add one-off teleport, root-motion
  gameplay transactions or visibility fades that conceal mismatched endpoints.
- Update the maps and work log when implementation changes project reality.
- Keep documentation concise and mark uncertainty directly.
