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
production model. Building masses wear one of eight district wall albedos built
by `tools/build-city-facade-textures.py`, tiled by the building's own bay and
floor grid through `CityFacadeGrid` so the baked window band lands on the real
panes rather than by metres. Road v2 gives ordinary City streets an `8 m` footprint
with a `6 m` carriageway and two raised `1 m` sidewalks. At selected eligible
perpendicular two-way corners and three- or four-way nodes, Road v2.1 moves the
four `1 m` corner pads outward, cuts the raised curbs back by `4.5 m` on every
real Street approach and exposes the shared asphalt apron required by the
production bus; those streets also host deterministic pedestrian and vehicle
graphs. A selected apron may share flat
zebra paint and paired signals, but a bus maneuver is retained only when its
sampled full body clears both physical poles at a conservative `0.30 m` radius.
A `CityPedestrianPopulationProfile` sets the ambient population per runtime:
City runs `8` daytime walkers and `3` at night over a `13`-presentation pool,
the Home balcony `5` and `2` over `8`. One event activates up to two walkers,
and while the street is below its target the next event follows in `0.4-2 s`;
at the target only replacements remain and the long `3.5-12.5 s` cadence
returns. They spawn at randomly
  ranked obstacle-safe anchors in the preferred `76-86 m` band, where the
  fixed City fog has already hidden them. A nearer ring was evaluated and
  rejected because the accepted fog proof measures depth at the frustum corner,
  where it does not hold until roughly `72 m`. If those anchors belong only to graph
  components that cannot reach the player, the director falls back to a linked
  fog-hidden anchor `32-86 m` away. A candidate must also disperse: `12 m` from
  every active walker, at most two per sidewalk lane, and the fallback ladder
  gives up connectivity before it gives up dispersion. When the hero travels
  faster than `3 m/s` — riding the bus, above all — selection prefers anchors
  ahead of the smoothed heading, because anything behind a `6 m/s` vehicle is
  outrun before it can be seen. At most two walkers at a time are steered at
  the hero: until such a walker first reaches the `24 m` encounter
  radius, eligible graph turns favor the continuation closest to the current
  player position; that one-shot guidance then ends and ordinary random roaming
  resumes. Every other walker takes a seeded `50/50` initial direction with no
  player-proximity preference, so the street shows opposing streams instead of
  a crowd converging on the hero. During daytime their still-distant simulation
  smoothly accelerates
  up to `2.75x` and returns to authored pace by `32 m`, so hidden actors approach
  or recycle without occupying slots for long. They keep moving forward
  through graph turns,
  independently choose whether to use each zebra crossing, give way along the
  lane rather than across it — a `0.15 m` shoulder-shift, queueing at the pace
  of whoever is ahead, and turning back after `1.5 s` of being unable to move,
  since a `1 m` pavement cannot fit two walkers abreast — and return to their
  pool only beyond `88 m` from the hero. Camera direction and frustum state do
  not take part in this lifecycle. Strict night (`19:00-06:00`) activates one
  walker per event, uses much longer random delays throughout and retains
  authored simulation pace; walkers already active at dusk are not culled
  early.
  The presentation pool repeats the stable ordered
  catalog: a Lampshade Walker, a Chair Carrier, a Kettle Hat Walker, a
  Long-Arm Walker and a Helmet Lamp Hopper. The first four also declare a
  seated Route 01 ride and own an authored `Sit` loop; the hopper declares
  none and stays on the pavement. Each ordinary design owns three
  City instances and the lamp-bearing hopper exactly one, which is what still
  caps the worn lights in the world at one. The pool exceeds the active
  population, so a repeat encounter shows a different mix. All five
  copy the hero's compatible
  Generic Avatar but use their own looping in-place locomotion: the Lampshade
  stays hunched through idle and walks in short uneven steps, the upright Chair
  Carrier uses a precise high-knee gait beneath an inverted cafe chair, the
  stout short-legged Kettle Hat Walker waddles in fast small steps while its
  belly and its oversized skewed enamel kettle swing against each other, and
  the narrow Long-Arm Walker — the only design whose strangeness is the body
  itself rather than a worn object — shuffles slowly on barely lifted feet
  while bare forearms reaching the pavement swing a quarter cycle behind the
  legs, and the Helmet Lamp Hopper crosses ground in two-footed rabbit bounds
  on `0.46 m` hind feet with a `0.24 m` apex, wearing the one working light
  the pedestrian contract allows: a single always-on shadowless `7.5 m` Spot
  on its miner's helmet. Its archetype declares a maximum of one pooled
  instance, so at most one
  such light exists in the world however large the pool grows. Every walker
  keeps the shared `1.75 m`
  envelope and fixed collider:
  the kettle design is short by proportion, with the human mass ending near
  `1.40 m` and the kettle owning the rest. Each clip is grounded against its
  own archetype's footwear, and designs whose hands travel near the road also
  declare a validated hand-to-pavement clearance band. An airborne design
  instead declares an apex band: its clips are lifted by one constant offset
  rather than pinned per frame, must never penetrate, must land at least once,
  and the runtime replaces its per-frame sole pin with one declared per-design
  `GroundTrim`, so the arc survives while the lift that retargeting adds is
  cancelled. Home maps the same
  graph into the
  bounded street view below the balcony. Its slots are enabled only while
  the Balcony camera shot is active; returning indoors releases them as a scene
  boundary. Because that enabling is itself the composition boundary, the
  balcony profile skips the first-event delay and starts filling immediately
  instead of showing an empty street.

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
in the layout plus the player home, then orders them as a shortest closed tour
rather than by district name: the default city therefore serves Home,
Residential, Old Town, Industrial and Nightlife and comes back, numbering from
the one stop the hero can name. Ordering by the district enum instead had no
geography in it and crossed the whole city twice, walking `1166 m` between
stops where `754 m` was available and forcing a `2592 m` road loop; the tour
order brings the same loop to `1798 m`.
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
hands and head to neutral. While the hero is outside, the bus yields to the
player and active pedestrians. An attached passenger is excluded from the
player-obstacle test, but the bus still yields to pedestrians and cannot be
pooled or released before passenger cleanup. Camera direction and frustum state
never control its lifecycle.

Route 01 now has a City-only passenger MVP. At either fully open passenger
door, the ordinary localized E/Enter/gamepad/pointer prompt lets the hero
board into fixed window seat `07` on the side opposite the driver. The
controller selects the nearest
valid front- or rear-door dock and records that door-specific transfer; the bus
holds its stop timer while the visible `BusBoardEnter` action passes through
the selected live doorway, then carries the ordinary 3D rig in `BusRideLoop`.
The shared contextual animation controller aligns the pelvis to live sprung
door/seat anchors rather than hiding a teleport. A seat-relative seated
camera position follows that sprung seat, while its yaw/pitch axes and horizon
remain world-level instead of inheriting body roll. It starts on the aisle side
of the hero and looks through the nearest side window; RMB mouse look and the
gamepad right stick rotate independent yaw and bounded pitch in place without
moving the camera outside the cabin. During travel the
gameplay root stays in
its original hierarchy and is late-synchronized to the actor-local seat pose,
so bus-slot or scene deactivation cannot disable the hero through reparenting.
The exit prompt becomes available only
after the service ordinal advances, so the hero can leave at the next or any
later stop through the same selected passenger door and `BusAlightExit` onto a
validated grounded roadside dock. Its root height comes from the same street
surface plan as the physical road and sidewalks, so a door on a flat bus apron
does not target the raised curb. Boarding/alighting holds the doors, and
cancellation, scene teardown or bus lifecycle cleanup restores the motor,
collider, contact shadow and camera at a safe exterior pose while leaving the
player hierarchy unchanged.
Up to two ambient walkers ride alongside him, so the cabin never carries more
than three counting the hero: seat `07` stays reserved, and ambient passengers
take a stable order of the other eleven seats biased to the driver-side row and
rear bench. The bus does not arrive empty by default: the moment it activates
it seats a seeded `0-2` ambient passengers, who were riding before the hero
could see the vehicle and leave through the ordinary alighting path at a seeded
later stop. Waiters appear two ways. A roaming walker already within `55 m` of a
stop along the pavement graph is recruited and walks there, so the hero can
watch the whole approach; where the stop is already beyond the `76 m` fog band a
waiter is activated straight onto its slot. Either way it stands on the sidewalk
centreline `0.70 m` road-ward of the blue `01` pole, facing the carriageway,
because the pole itself sits `0.2 m` outside the walkable strip and carries a
collider. Two slots per stop queue along the lane at `+0.30 m` and `+1.40 m`
from the halt pose, clear of both door entries, since a `1 m` pavement cannot
fit two walkers abreast. Boarding takes the same shared service hold as the
hero -- the hold is per owner, so a boarding walker never disables his own
prompt -- reuses the same validated door dock and grounded-root resolution with
its own seat index and capsule radius, and runs a short scripted doorway walk
whose time budget is derived from the measured `pavement -> door -> seat` path
and that walker's own `0.72-1.30 m/s` pace, clamped to `3 s` up to one whole
dwell; the door is chosen by that whole journey rather than by which one the
walker stands nearer, because the two doors sit `4.39 m` apart on the same
kerb. An overrun aborts back to the pavement rather than stalling the fixed
`10 s` dwell. Passengers alight at a seeded strictly later stop through the same
door onto a validated roadside dock and rejoin ordinary roaming at the stop's
own graph node. A rider is exempt from the `88 m` pedestrian recycle rule, from
distant simulation acceleration and from the bus's own pedestrian yielding;
recycling keys on the hero alone, so a bus `92 m` away behind fog pools with its
ambient passengers instead of stranding the single actor slot for a lap.
Seating is one rule for every design: all five copy the hero's exact 31-bone rig
at a `0.70 m` rest pelvis, so the runtime aligns that bone to the cushion rather
than pinning the lowest sole, which on a seat would drag the model down until
its boots touched the cabin floor. What varies is declared per archetype -- an
authored seated posture, a pelvis lift and back offset, and a headroom band the
deterministic generator proves against the real deformed meshes. The `2.05 m`
cabin and `0.41 m` cushion leave `1.64 m`; the four riders measure `1.03-1.06 m`
above the seated pelvis and hang `0.35-0.38 m` below it.
Fare/payment, destination selection, passenger persistence and
live bus tracking are deferred. The City map still draws Route 01 as a blue
ink-outlined line beneath the orange player itinerary, plus five numbered
localized stop markers in the default layout and a compact legend; it has no
live bus marker. The moving bus runtime is deliberately City-only. Home's
balcony
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
