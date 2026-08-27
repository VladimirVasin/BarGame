# AI project entry point

Read this file first, then use [`ai/README.md`](ai/README.md) as the documentation index.

## Reality check

The Unity 6 URP vertical slice is implemented. It generates a finite connected,
blueprint-driven city whose inserted north-south river corridor expands the
urban envelope to `13 x 12` while preserving all 144 former land-use lots.
Two road bridges reconnect its outer edges; the 16-cell central park is split
into two `2 x 4` halves joined by a timber footbridge, and four bank stairs
reach lower waterside platforms. The city retains a reachable northern beach
and water edge, four urban districts and one Residential bar directly across
the street from the player home. The
default road footprint now has a mandatory uninterrupted outer Street circuit:
the two road bridges and their continuous bank roads close it across the river,
while only the interior street loops remain seed-optional. The
default footprint also extends east to a reachable `3 x 2` cemetery with
deterministic physical landmarks and street access, a separate `4 x 2`
church precinct immediately north of it, and the residual plain `4 x 4`
north-east yard (the drained former lake block). The church owns a large
Blender-authored exterior and a west-facing street entrance into its separate
runtime-composed interior;
the north edge carries the dressed seacoast precinct — mol, beacon,
the transplanted boat station with its fisherman, footbridge over
the river mouth, animated sea and wild east shore. Both `3 m` river
promenades hand their complete logical width to the shore over granite
thresholds; short transverse rails visibly close only the extra waterside
lips. The default city now reads as a coastal basin: physical flat-shaded
mountain ridges close only its western and southern edges. At the river axis
the southern skyline stays closed above one low, dark `10 m`-wide water mouth
instead of opening into an empty gorge. Water and its bed continue more than
`48 m` behind the mountain, while both `3 m` promenades extend walkably from
world `Z=-156` to the rock stop at `Z=-182`. The player can follow either bank
all the way to that physical stop but cannot enter the cave; it has no prompt,
interaction or transition. All five typed fringe Yards now carry
deterministic authored edge compositions. The four west/south strips form one
old municipal service belt of retaining work, drainage, a narrow maintenance
trace, sparse utility verticals and repair/rockfall pockets. The complete
roughly `22 m` road-to-rock forefield of those four strips is now a conforming
terrain mesh with its own quiet compacted-fill sheet instead of flat
`YardGround` colour. A narrow terrain-conforming road-shoulder trace occupies
the first `4 m`,
three secondary service traces cross the working middle, and three or four
yard-specific meso anchors keep every longitudinal empty interval at or below
`40 m` before the paired-trace-or-flood-drain/retaining toe belt. All four
mountain strips use narrow terrain-following maintenance marks instead of
broad earth-textured longitudinal service-track overlays. Four large anchors
still finish the chapters: stepped masonry and a culvert, an industrial repair
frame, the open-tunnel freight forecourt with grounded stepped return wings,
a two-post service frame and a crown-mounted floodlight, and caged floodworks
with a gauge.
Dedicated forefield, service-track, board-formed concrete and old-masonry
sheets carry the close read. Every height-safe seam along the west/south ring
now opens into that walkable ground instead of acting as an invisible wall.
Three capsule-clear `6 m` routes cut precisely through the retaining line to
the mountain toe. The two western routes use broad gravel aprons, while the
south-east floodworks marks its equally clear terrain route with a narrow,
embedded trace ending at the drain. The fourth route reaches the open portal
over the conforming terrain and is read from a narrow embedded approach mark
plus two wheel ruts, not from a floating `6.9 m` surface box. At every
non-opening toe, the physical
rock mesh now overlaps beneath the ground edge and carries collision through
that bond, so there is no visual or physical void between soil and mountain.
The omitted south-west blueprint cell is closed cityward of the diagonal toe
by one validated, textured corner earthwork mesh whose renderer and collider
share the same sampled topology. It is one continuous natural-soil slope,
about `16.2°` across its centre, with no stairs, platforms, retaining faces or
props. It remains outside the navigation mask behind the two ordinary
map-boundary fence legs, which meet in a physical right angle at the exact road
corner; neither geometry nor navigation crosses the rock toe. The turning ridge interpolates its complete cross-section
from west to south, so both endpoint profiles and the overhead silhouette are
welded rather than touching at one toe point.
The north-east urban-core road cap beside lot `[12,11]` has a separate local
`4 m + 4 m` physical `CornerGuard` pair meeting in the same ordinary right
angle. It guards only that cap; the northern waterfront, eastern yard ground
and their authored approaches remain open.
Each anchor owns one emissive practical; at night the nearest one may lease the
last of the existing eight street Spot slots, so `CityNightAtmosphere` still
owns exactly `12` realtime Lights rather than adding four more. The tunnel
reuses that slot at the second of five ceiling fixtures; it keeps a restrained
`0.22` daytime power floor, flickers on a deterministic sparse contact-fault
pattern and owns a mono ballast buzz audible only within `5.6 m`. Its floor
overlaps the terrain edge, and offset/overlapping lining joints remove the
former ground gap, coplanar wall flicker and ceiling slits.
The eastern Yard uses a separate low,
unlit utility-edge composition and creates no eastern ridge. The gate-free
`8 x 5.5 m` portal now leads into a `72 m` faceted rock shell: the first `12 m`
are straight and physical, the first `11 m` are walkable, and the later
segments bend west so no rear cap or endpoint is visible. Crossing `8 m`
inward while physical tunnel travel remains unavailable shows the localized
thought and guides the hero back to `6.5 m`; this City boundary does not yet
invoke the separately implemented mountain destination. After the mouth, the player-following rain adopts a
dry core, local fog particles are cleared and the camera-relative mountain
shell is hidden; the global Exp2 fog remains unchanged. A camera-relative
west/south ridge shell keeps that enclosing silhouette inside City's fixed
`48 m` far plane without closing the northern sea or the deliberately
untouched eastern horizon. Physical ridge chunks use one shared opaque
`CityMountainPhysical` shader: a horizontal-distance dither hands the backdrop
to real rock from `43 m` through `31 m`, while a restrained `0.10`
fog-visibility floor beyond roughly `22 m` returns to native Exp2 on approach.
The fog-exempt shell is mixed `0.86` toward City fog, so distant mountains read
only as a faint mass and real rock emerges near the toe. Matching forward,
depth and depth-normal passes keep that handoff consistent; tunnel pieces
remain ordinary `RuntimePrimitiveLit` geometry.
The City map consumes that same mountain plan, expands its display only west
and south, and draws the ridge toe/outer hatch, only the visible narrow river
approach into the mountain, and an uncrossed open tunnel arch with only its
first `12 m` represented without inventing a
north or east boundary. The sparse
footprint can be non-rectangular, and the same data-first area contract supports
reordered urban areas.

The build now has ten explicit scenes. `MountainRoad` is appended at index
`7` as the sixth gameplay root, `AreaLoading` at index `8` as a black,
progress-bar-only transfer boundary, and `ChurchInterior` at index `9` as the
seventh gameplay root. The City and mountain worlds are each
runtime-composed after a Single-mode load and are never resident or rendered
together. The ordinary map has switchable City/Mountain Road tabs; confirming
the other area unloads the source into `AreaLoading` and then loads the chosen
destination. `MountainRoad` generates a pure City layout only for the City tab,
never City GameObjects. Its hero starts `6 m` inside a `9 m` exit tunnel and
follows a `600 m` continuous car-scale ascent (about `230.8 s`, or `3 min
51 s`, at the normal `2.6 m/s`). The ribbon rises `26.1 m` at no more than an
`8%` grade: ordinary stretches are `4.8 m` wide and ten `7.5 m`-radius
hairpins widen to `6.4 m`; the final `5 m` are level. Midway, the route must
cross one `50 m`-long high mountain bridge with a `5.8 m` structural deck around
the `4.8 m` clear roadway. Its terrain mask opens a real gorge to world
`Y=-16`, preserving at least a `25 m` visible drop below both bridge ends.
The climb still ends through one level, shared-vertex automotive apron on the
same irregular roughly `42 x 27 m` mountain terminal with a protected `7.5 m`
turning circle. A separate colliderless asphalt overlay makes the complete
entry and turning pocket visible just above the shared road/plateau collision.
The terrain margin is `76 m`; ordinary mid/far ridges ring the outer perimeter
of the route-wide envelope, ground their bases from the minimum terrain under
each footprint and keep those footprints clear of the road and trees. An
enterable five-sided glass cafe with its dedicated silent four-role cast
occupies the left side; a continuously operating `58 m`
cableway with three supports and four cabins climbs from the right side into an
occluding snow ridge. Cafe and cableway heights are now based on the raised
terminal instead of old absolute world heights. Both are built inside the
Mountain Road world, not as additional scenes, and add only sounds owned by
visible appliances, machinery and roller crossings. Their landmarks come from
the same terminal plan used by the map, which now shows all ten hairpins and
the bridge. Layered forest, grounded melancholy roadside objects, mid ridges
and far snowy mountains are distributed over the full route. Eight of the
twelve roadside-misc kinds — `102` of `159` placements — now render from a
deterministic `19`-mesh Blender library combined into `12` runtime batches;
their plan-owned transforms, semantic roots and collision proxies did not
move. Boulders, the culvert, utility cable and tunnel lamp remain the explicit
later migration wave. Nine causal sound
anchors remain, five on the road including the loose bridge rail and four on
the summit, and one tunnel lamp visibly flickers.
The rest of that pad is a dressed transfer yard: ploughed snow and a grit bin
left of the arrival, the last road board and a seized barrier right of it, the
cafe's winter furniture on its threshold, a freight dock with one abandoned
suitcase beside the cable station, and a `0.66 m` retaining wall with two
three-riser flights onto a back terrace. That terrace ends. A `1.02 m` parapet
stands `0.35 m` inside the rim — which is what finally makes the walkable
mask's own clamp invisible — and carries a bench, a survey pillar, a memorial
plate and a windsock mast, with one gap where the run is missing and chained.
Behind it the terrain is cut away `26 m` through a `-27` degree wedge aimed at
the one sector the ridges leave clear inside the `120 m` far plane; no ridge is
moved for it and the two flanking masses become its jambs. In that opening a
fixed matte at `81-105 m`, on the lighthouse island's two existing shaders,
shows the valley bed, the switchback he climbed and a grain of city — all
measured from `y = 0`, the height of the tunnel mouth — with its windows lit
after dark by the same per-minute apply that moves the sun and not one Light in
it. One mercury practical burns over the freight dock; the brink stays dark.
The hero can sit on the bench or on the cafe's one free stool, and the Ferryman
answers up here at last: a repertoire and never an offer, because the road has
ended.
For now the City tunnel still refuses passage; only the map invokes this area
transfer. LastRouteCar still has no Mountain Road driving controller.

The runtime places one visually
distinct player home beside a bar street and one deterministic street-front
supermarket, instantiates the same modular low-poly 3D hero in all seven gameplay
roots, loads separate bar, supermarket, stairwell, home and church interiors, and
restores the same seed and matching exterior return point. The hero keeps
independent body meshes on one Generic rig, uses continuous in-place 3D clips
for locomotion and contextual actions, including a grounded lean/right-hand
press before every ordinary location-door transition, hands failed balance falls from a
directional clip into a bounded runtime ragdoll and back into an authored rise,
and derives first-person arms and the inventory portrait from the same
production model. Ordinary building masses use a separate `36-52 m` height
profile whose roofs fall below one-percent visibility in the fixed City fog;
the bar, the supermarket and the player home retain their original low-rise
heights. Every ordinary lot now instantiates one fixed-metre Blender prototype
for its district and preserves authored pane state through the shared
Off/Cold/Warm slot shader. The single Residential bar separately uses the
complete fixed-metre `bar_exterior_v2`: a two-storey late-Victorian urban pub
with old brick/render, pitched slate roof, unequal chimneys, a lower service
wing, bottle-green/oxblood faceted shopfront, individual sash windows and the
retained door/sign anchors. Four solid flanking panels — two beside the
entrance and one at each outer bay edge — plus full-depth jamb returns close
every oblique sightline into the shell. Its visible terrain foundation uses
the same rough box-projected exterior brick and sits `0.08 m` inside the front
and side faces. It replaces the former
CityMisc bar shell and generic window bands in City and in a fully visible Home
reconstruction; a Home half-space crossing alone keeps the clipped legacy
silhouette.
The broad passive City misc layer now resolves through one deterministic
`city_misc_citywide_v4` Blender library: `64` semantic kinds, `94` assemblies,
`186` role meshes and `33,454` triangles cover ordinary decoration and park
landmarks, night fixtures, Route 01 stops, the eastern yard, cemetery,
seacoast, fringe service belt, the static shells of all four district points of
interest and the live supermarket/player-home shells. Its former bar shell
remains catalogued only for v4 compatibility and is not instantiated. Unity
plans still own placement, terrain, collision, dynamics, interactions, lights,
halos, cloth and NPCs; tilted cemetery monuments deliberately remain on their
legacy geometry. Road v2 gives
ordinary City streets an `8 m` footprint
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

Both park boards are now playable. Sitting on either free plank starts a
real match against a deliberately mediocre engine: full legal chess on the
chess player's table and full Russian draughts on the neighbour's, with the
hero dark at both because dark is the side the drawn set already had nearest
the free plank. The camera drops to the hero's own seated eyes over the stone
— `1.06 m` above the plank and `0.34 m` forward of it, leaning right over
the stone, `72` degrees, `+-55` degrees of yaw and a pitch band of `-6` to
`75`. The resting pitch is not
authored: it is the bisector of the board's near and far edge angles, so the
field is centred in the frame by construction. Only the head is hidden, by
rig rule rather than by mesh name, because on this model the head is
twenty-two meshes. That table's
two static batches are swapped for one object per man, moves are carried a
step at a time, and the position survives standing up and coming back. The
rules and both engines are pure C# under `Assets/Scripts/Runtime/Games/`,
checked by perft against the five standard positions. There is no board HUD:
whose move it is, check, compulsory capture, promotion, the result and the
offer of another game all arrive as lines the man opposite speaks in his own
bubble, and the quarrel between the two of them is suppressed for as long as
somebody is sitting at a board.

One full-size ambient midibus may also be active near the player, although its
fog-hidden spawn cadence deliberately allows periods with no visible bus. The
actual `8.25 x 2.38 x 2.95 m`, `4.5 m`-wheelbase 3D vehicle has a visible
twelve-seat interior, driver area, two animated double-leaf doors that fold
inward around fixed outer posts, steering and rolling wheels, a synthesized
engine loop and time-of-day head, tail and cabin lights. Two windshield
wipers, each an arm-and-blade mesh on its own authored base pivot, sweep
`±40°` around the windshield normal whenever the deterministic weather
schedule reports rain — slow in drizzle, fast in a downpour — and park back
at the resting diagonal when the rain ends or the vehicle pools. The model carries
world-scale box-projected UVs and four deterministic tileable albedos from
`tools/build-city-bus-textures.py` (exterior paint with panel seams and
rivets, brushed metal, interior linoleum, seat weave), multiplied under the
existing flat material colors. The cabin light is anchored by two visible
pendant lamps on the aisle centreline — stem, collar and a glowing bulb at
`2.56-2.66 m` — whose bulbs the two runtime cabin Spots originate from at
night; the ceiling strips also protrude below the interior ceiling panel
instead of being buried inside its thickness. Its separate passive
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

Exterior weather is a deterministic pure function of the city seed and the
absolute session time (`GameWeatherRules`): `90`-game-minute slots draw clear
(`55%`), light rain (`27%`), heavy rain (`12%`) or a thunderstorm (`6%`) from
a seeded hash, and the continuous rain intensity smoothsteps between the slot
targets (`0`, `0.45`, `1.0`, `1.0`) over the first `5` game minutes of a
slot, so City and the Home balcony always agree and scene loads cannot
desynchronize the sky. Rain renders as a player-following field of stretched
streak particles on the shared atmosphere shader (`CityRainField`, up to
`420` particles over a `26 m` box): light rain is sparser, thinner and
slower-reading, heavy rain denser with longer brighter streaks. A
deterministic synthesized rain bed tracks the same intensity in loudness and
low-pass brightness. A thunderstorm is heavy rain plus lightning from the
same pure schedule: each `12`-game-minute window of a developed storm slot
hashes into at most one strike (`70%`) with its own start, azimuth and
distance, rendered as a transient shadowless directional flash
(`CityLightningFlashLight`, disabled outside the `0.5`-minute flicker so the
pooled light budget is untouched) and answered by a synthesized thunder
one-shot whose delay (`0.6-3 s`), loudness and low-pass follow the strike
distance. The balcony view shows the same rain and the same flashes and
plays the same rain bed and thunder, all gated to the active Balcony shot; a
frozen clock (pre-wake, pause) suppresses the flash instead of holding it.
While the hero rides Route 01 the emitter switches to a ring around the bus
so streaks never spawn inside the cabin. Weather deliberately leaves the
fixed fog, grade and the exactly-asserted day/night lighting contract
untouched: it does not dim daylight, wet surfaces or reach interior windows.

Startup truth begins at `Assets/Scripts/Runtime/Scenes/MainMenuRoot.cs` and
`Assets/Scripts/Runtime/Scenes/HomeOpeningController.cs`; generated-city truth
continues from `Assets/Scripts/Runtime/Core/CityGameRoot.cs` and
`Assets/Scripts/Runtime/World/CityLayoutGenerator.cs`; the default-city
mountain rim starts at `CityMountainBoundaryPlanner.cs` and materializes
through `CityMountainBoundaryWorldBuilder.cs` plus the presentation-only
`CityMountainBackdropWorldBuilder.cs`. Its river-cave descriptor also drives
the water, bed, walkable banks and quay work in `CityRiverWorldBuilder.cs` and
`RoadWalkableArea.cs`; the authored ground before that rim and
the separate eastern utility edge start at `CityFringeYardPlanner.cs` and
`CityFringeYardForefieldPlanner.cs` plus
`CityFringeYardLandmarkPlanner.cs`, receive measured surfaces through
`CityFringeYardSurfaceAppearance.cs`, split their conforming terrain through
`CityFringeYardGroundWorldBuilder.cs`, and materialize detail through
`CityFringeYardWorldBuilder.cs`; its four runtime practical anchors are leased
by `CityNightAtmosphere.cs` without expanding that pool. Supermarket truth starts
at `Assets/Scripts/Runtime/Scenes/SupermarketInteriorRoot.cs` and
`Assets/Scripts/Runtime/World/SupermarketInteriorLayoutPlanner.cs`. Session-time
truth lives in `Assets/Scripts/Runtime/Core/GameTimeState.cs`,
`GameTimeRuntime.cs` and `GameTimeDayNightRules.cs`.

The park chess set carries real men. Seven authored meshes from
`tools/build-city-chess-set-3d-model.py` — six turned chess pieces and a
draught, all sized from the drawn `0.15 m` square — are combined into four
meshes for `56` men across the two boards: a full chess opening on one table
and `24` draughts on the other. The placement is a checked contract rather
than dressing (`a1` dark, a light square in each player's near-right corner,
queen on her own colour, `RNBQKBNR`, knights facing the opponent, draughts on
dark squares only), and correcting it also corrected the drawn board, which
had put a dark square on both players' right for as long as it was empty.
Nothing ever moves a piece: both games are set up and unstarted, which is an
accepted exception to the art bible's ban on pieces, recorded in
`ai/architecture-notes.md`.

Runtime support diagnostics are written as bounded NDJSON through
`Assets/Scripts/Runtime/Diagnostics/`; see `ai/debug-log.md` for profiles,
paths and event boundaries.

## Source-of-truth order

1. Files currently present in the repository.
2. Unity project and package settings.
3. `ai/architecture-notes.md` for accepted decisions.
4. `ai/city-zones-art-bible.md` and `ai/city-story-bible.md` for what the world
   is allowed to be.
5. Planning documents for intended work.

Never report a planned system as implemented without inspecting relevant
repository evidence. This does not require running every test layer.

## Working agreement

- Use the canonical workflow matching the task in `ai/prompt-templates.md`.
- Fast targeted verification is the default. Complete suites require an
  explicit release/full-regression request. Create a player build only when it
  is the requested deliverable or release gate; add a smoke only when requested
  or when packaged startup behavior is the changed contract.
- Start from `ai/project-overview.md` and `ai/systems-map.md`.
- **Anything the player sees, hears, reads or does in the world is governed by
  two mandatory documents:** `ai/city-zones-art-bible.md` for form and
  `ai/city-story-bible.md` for meaning. Before adding a detail, find the
  `Нельзя` it would violate — none means allowed, one dated in the story
  bible's §6 registry means allowed from that level, and one that is not in the
  registry means the detail is not added. New in-fiction text must satisfy the
  story bible's §21 register, its §16 laws are hard, and every scale level must
  still pass all nine art bible §16 acceptance checks. See AGENTS.md, World
  canon.
- All future contextual player animations must follow the mandatory
  `ai/contextual-animation-standard.md`; do not add one-off teleport, root-motion
  gameplay transactions or visibility fades that conceal mismatched endpoints.
- **Every 3D object is assembled in Blender.** New geometry is authored by a
  deterministic generator under `tools/build-*-3d-model.py`, exported and
  imported as a model asset; it is not composed at runtime out of
  `RuntimePrimitiveFactory` boxes and cylinders. The existing generators are
  the pattern to copy — player, pedestrians, bus, bus driver, bartender,
  cashier, cat, chess set, Last Route car, church, Mountain Road misc and City
  misc — each pairing its script with a measured JSON manifest and a
  determinism check. Blender lives at
  `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe`.
  This is a rule for what is built from now on. The structural runtime-primitive
  geometry still in the tree — terrain, roads, building masses, infrastructure,
  dynamic precinct pieces, the mountain road and its terminal — predates it
  and is not retroactively invalid. The City and Mountain Road misc libraries
  are explicit bounded migrations; moving anything else remains its own
  decision, taken piece by piece and never as a side effect of another task.
- **Interiors share one authoring library: `tools/interior_kit.py`.** It is
  imported by interior generators and holds what a box cannot express — wall
  runs with real openings and reveals, swept mouldings, chamfered edges,
  panelled leaves, turned legs. Its rule is that it contains no value belonging
  to any one room; if a number is specific to the bar it lives in the bar's
  generator. The bar is the first thing built on it
  (`tools/build-bar-3d-model.py`, with `tools/bar_parts.py` for Unity-space
  authoring) and is fully migrated, interior and facade; the apartment,
  stairwell and supermarket are the intended next users.
- **Blender's axes reach Unity by SWAPPING the last two, not by negating one.**
  Unity `(x, y, z)` is Blender `(x, z, y)` under the project's export settings
  (`axis_forward="-Z", axis_up="Y"`) plus `bakeAxisConversion`; the
  right-to-left handedness change is what removes the sign one would expect.
  Never settle this by reasoning about it — assert an authored anchor against
  the plan position it is supposed to occupy, as `BarModelContractTests` does.
  Getting it wrong silently mirrors the model: the bar's doorway landed in the
  opposite wall and its counter 9.5 m away.
- **An imported FBX keeps its unit factor on the authoring root.** That root
  arrives scaled `100` and its meshes store vertices at a hundredth of the
  metres they were authored in; an anchor's `localPosition` is likewise a
  hundredth. Anything that separates a part from that root — a reparent with
  `worldPositionStays: false`, an `Instantiate` followed by
  `localScale = Vector3.one`, reading `anchor.localPosition` instead of
  `anchor.position` — silently makes it a hundredth of its size or puts it a
  hundredth of the way to where it belongs, while anchors, collision, counts
  and the manifest all stay right, because none of those come from the meshes.
  Reparent with `worldPositionStays: true`, take a clone's scale from the
  template's `lossyScale`, read anchors through world space, have the asset
  setup MEASURE the imported renderers against the manifest bounds, and have a
  test MEASURE the placed room — a correct prefab can still be placed wrongly.
  This cost three separate defects in the bar, and only a rendered frame found
  the last two.
- **A swapped axis pair is a reflection, so it reverses face winding.** Any
  generator that authors in Unity space and converts must re-wind every face,
  and any ring swept through XZ winds the opposite way from the same ring
  through XY. Inverted normals survive wireframes, triangle counts and every
  dimension assertion; check the signed volume of each solid at generation
  time, as `tools/bar_parts.py` does.
- **One working copy per concurrent session.** Two agents in the same checkout
  fight over one Unity project: only one instance may open it, so the other's
  runs abort with "another Unity instance is running", and a half-written file
  from one breaks the other's compilation and wastes a whole ten-minute test
  run. In one session that cost three broken compilations, several aborted
  runs and two foreign red tests in every report. Give each session its own
  checkout — `Library/`, `Temp/` and `Logs/` are already gitignored, so a
  worktree is self-sufficient:

  ```
  git worktree add ../БП-<branch> <branch>
  ```

  The first Unity launch there is slow while `Library/` is rebuilt; that is the
  whole price, and it is paid once. Branches still merge the ordinary way — the
  gain is that until they merge the sessions cannot break each other.
- **Look at what you changed.** `Assets/Tests/PlayMode/AreaCaptureFixture.cs`
  renders any world scene to `Captures/<area>/`. Run it for any scene whose
  appearance changed and open the frames. Numbers cannot see an object in the
  wrong place or a mesh at a hundredth of its size.
- Update the maps and work log when implementation changes project reality.
- Keep documentation concise and mark uncertainty directly.
