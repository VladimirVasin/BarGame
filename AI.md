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
north-east yard (the drained former lake block). Once the hero has sealed
his first grave, two ordinary procedural ravens hold to the cemetery — one
on that grave's mound, one on open ground nearby — flushing from a close
approach and returning when he withdraws. Sparse pairs of the same
wintering ravens also hold up to fourteen open city spots from the first
day — on the default city ten: the fountain plaza gravel, the bandstand,
a river landing, the mol head, the east-shore barge gunwale, a bridge
kerb and four dumpster kerbs — always already perched,
flushing at arm's length and never at the church, the district POIs, the
cemetery or the boat-station tableau. The
church owns a large
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
unlit utility-edge composition and creates no eastern ridge. The only separate
human-scale vignette in the typed fringe Yards is an unoccupied mason's cart at
the west stone terraces. The former winch-service, tunnel-repair,
flood-maintenance and open-hood-car sets are absent, and no fringe vignette
receives a resident. The north-east `4 x 4` former-lake Yard also remains
deliberately empty. The cart adds no text, interaction, light, audio, story
reaction, water, cats, children, fire, flags, logos or landmarks.
The gate-free
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

The build now has twelve explicit scenes. `MountainRoad` is appended at index
`7` as the sixth gameplay root, `AreaLoading` at index `8` as a black,
progress-bar-only transfer boundary, `ChurchInterior` at index `9` as the
seventh gameplay root, `AlpineVillage` at index `10` as the eighth, and
`MothersHouseInterior` at index `11` as the ninth. The existing door of the
house at the head of the village now enters that separate two-storey interior and returns the
hero to a safe point outside the same threshold. The room-authored environment
is deliberately light, clean and cared for while remaining old and modest:
fading, repairs and soft use carry age, never dirt, damp or abandonment. Its
surfaces use the dedicated `MothersHousePositiveAtlas` and do not reuse Home or
City albedos. The exact Kettle Hat prefab on the tea table is the explicit
exception and keeps its original material and atlas. Inside, the entrance is
centred in the south wall directly opposite the fireplace, and the hero appears
there facing north. Behind the west sofa, a real straight stair rises in the
opposite southward direction from its north-side foot through a split slab to a
west upper corridor and exactly two separate, accessible, currently empty
rooms. Visible Blender steps remain collider-free over one plan-owned hidden
ramp; upper slabs, guards and partitions are runtime-owned collision. Four
height-aware fixed shots cover the ground room, stair/corridor and both rooms,
so overlapping `X/Z` floors cannot retain the wrong camera. A shaded floor lamp replaces the former invisible ceiling
fill, keeping one local pool beneath the hearth key
and restrained ambient floor. Fire, muffled wind, alternating clock ticks and
sparse house settling form the quiet ASMR-like sound bed. City,
Mountain Road and Alpine Village are each
runtime-composed after a Single-mode load and are never resident or rendered
together. The ordinary map has switchable City/Mountain Road/Village tabs; confirming
the other area unloads the source into `AreaLoading` and then loads the chosen
destination. `MountainRoad` generates a pure City layout only for the City tab,
never City GameObjects. Its hero starts `6 m` inside a `9 m` exit tunnel and
follows a `620 m` continuous car-scale ascent (about `238.5 s`, or `3 min
58 s`, at the normal `2.6 m/s` walk; about `148 s`, or `2 min 28 s`, under
continuous `4.2 m/s` run input). The ribbon rises `26.1 m` at no more than an
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
enterable five-sided glass cafe with its dedicated four-role cast occupies the
left side; only its drinking pair exchange a private text conversation. A `230 m`
cableway with nine supports and eight cabins climbs from the right side into the
haze - its far turn stands beyond the scene's `120 m` draw range and is never
seen, and the ride cuts to black mid-span at `73 m` on nothing at all. Cafe and
cableway heights are now based on the raised
terminal instead of old absolute world heights. Both are built inside the
Mountain Road world, not as additional scenes, and add only sounds owned by
visible appliances, machinery and roller crossings. Their landmarks come from
the same terminal plan used by the map, which now shows all ten hairpins and
the bridge. Layered forest uses three deterministic crown silhouettes and
yields at three measured bends, the bridge and the terminal approach, while
the surrounding far stand and both ridge rings keep every road reveal from
becoming a second vista. Natural debris gathers into five unequal roadside
chapters with deliberate gaps around those structural beats and a shared
conservative footprint clearance against all existing roadside furniture.
The cafe's visible shell, interior and furniture now come from one
deterministic fixed-metre Blender set: `61` semantic meshes / `5,794`
triangles, `52` anchors and seven dynamic prop assemblies. Its rear service line
now carries an extended cabinet with a cutting-board dock, a compact stove and
pan, and a refrigerator cavity with two shelves plus a separate hinge-ready
door and grip. That door has no runtime driver, Animator or Rigidbody, and
neither the attendant nor the player uses any of the new kitchen equipment.
The napkin dispenser, sugar shaker and salt shaker have moved clear of the
hero's place, leaving the authored `MenuDock.Hero` free. The seventh prop is
the thin open `Menu.Hero`, initially hidden until the attendant carries it to
that dock after the hero sits. Its pages use a role-specific plain warm-paper
surface instead of the green-banded shared props atlas. The localized TMP
lettering keeps its readable face and screen-right aligned to the focus camera,
so no row is mirrored, hidden by the page or crossed by that atlas band.
The terminal plan still owns its five-sided footprint, open `1.6 m` door,
shelter, map landmark, three causal appliance voices and exactly `17` logical
colliders. Seven stools
follow the long counter and return with their seat tops at `0.8175 m`: the
sleeping lone visitor and the pair occupy three with real seat contact, four
stay empty, and the hero keeps the designated middle main-row seat.
Its approach remains in the aisle while its seated facing now points at the
counter. The entrance-side lone visitor rests his head on two strongly crossed,
stacked forearms and owns no cup or attendant service. Only the pair's two
environment-owned coffee cups visibly drain, each in a separate drink window;
at the refill threshold the attendant leaves the Wipe loop, walks to the cup
and pours it full. The service clock arms only once the
player reaches the cafe's `16 m` entrance radius, which excludes every earlier
hairpin; the first visible sip crosses the refill threshold and begins a Pour
within one minute instead of expiring during the `620 m` approach. Completing
the hero-stool sit switches to a bounded eye-level first-person view of the
counter, hides head geometry, and restores the prior follow camera on exit.
The attendant then silently carries and places the physical open menu.
Once `HeroMenuPlaced` is true, the same `MountainRoadCafeSeatView` blends its
fixed camera over `0.45 s` to a `40`-degree close-up `0.50 m` from the page
along the current seated sight line, keeps world-up with no roll, and blocks
every look source. `W/S` or the D-pad still wraps through three
localized items and `Space`/gamepad West records the selection-only
confirmation. Confirmation or the first `E`/`Enter`/gamepad South action closes
the booklet on the counter and returns the saved seated view; neither starts
retrieval or stands the hero. While it rests there, looking at it changes the
same action to reopen the close-up, while looking away changes it to stand.
Only after the visible stand-up animation completes does the attendant
serialize `WalkToMenu -> TakeMenu` (`2.5 s`) `-> CarryMenuBack`: the closed
booklet stays in the world until the hand physically takes it, follows that
hand back and is hidden only at its dock. A later completed sit starts the
delivery again. The cycle creates no order,
product, payment, inventory item, food, drink, dialogue, reaction, sound or
story state. The ten-clip cast (one sleeping loop plus one lone-patron
interjection, four pair clips and four attendant clips) keeps the attendant
silent, and the hero never enters the pair's cup/refill queue. Only inside the
physical cafe, PairMan and PairWoman follow the fixed
localized text cycle `Man01 -> Woman01 -> ... -> Man10 -> Woman10 -> loop`, with
ten keys per role present in both Russian and English. A pending turn waits
without being consumed or skipped while either patron is in Drink or the
woman's cigarette lift/drag and `SOCKET_Mouth` exhale window; the man's idle
tapping may continue under his line. The active speaker turns head and neck
toward the partner and uses one over-head text bubble. A PairMan -> PairWoman
exchange completes only when both
lines have been fully displayed. After every third completed exchange
(`3/6/9...`, continuously across the ten-pair pool wrap), and only after both
mutual looks have returned to idle, the strongly drunk lone patron
— the woman's husband — raises his head, waves his right hand toward the pair,
calls her home through one line from a four-key RU/EN pool and returns to
sleep. The pair gives him no look, answer or reaction gesture; its pending
order is unchanged, so Woman03 is followed by Man04 and Woman10 by Man01.
Neither conversation has voice or other added audio.
The cigarette's filter is held at the woman's fingers and reaches her mouth;
the ember points away from both hand and face. Its glow remains separate from
the phase-locked world-space mouth plume. The same two visible practicals
and one shadowless technical sulphur wash now share the counter more evenly:
the warm key reaches the sleeping husband, while the existing cold practical
starts inside the visible task fixture over the stove and, together with the
wash, keeps all four figures readable without reaching the terrace, parapet
or dark brink. No additional Light was added. Six colour-neutral semantic
detail sheets split exterior, interior, counter, metal, props and glass without
adding a new hue, `PHILLIES`, `5¢`, logo, price, copied city background or any
readable object text beyond the menu's exactly three localized item names;
authored UV regions and a zero-overlap validator prevent
stretching, repeated samples and coplanar flicker.
Eight of the
twelve roadside-misc kinds — `102` of `159` placements — now render from a
deterministic `19`-mesh Blender library combined into `12` runtime batches;
their semantic roots, placement and collision proxies remain plan-owned.
Boulders, the culvert, utility cable and tunnel lamp remain the explicit
later migration wave. Nine causal sound
anchors remain, five on the road including the loose bridge rail and four on
the summit, and one tunnel lamp visibly flickers. Up to four raven pairs
hold the road the same way the cemetery pair holds its graves — the
gorge-bridge rail, the exit-portal shoulder, the summit parapet clear of
the bench and a culvert roadside — always already perched, never arriving
in frame.
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
The hero can sit on the bench or on the cafe's designated player stool, and the Ferryman
answers up here as well as on the island — his own pool of small talk, and the
same two-choice menu whose second line is the island's mirrored:
`Вернуться в город?`.
For now the City tunnel still refuses passage on foot. The Ferryman's
LastRouteCar crosses it as the story-owned transition and drives the same
planned road to the terminal; the map remains the ordinary area transfer.
**The road runs both ways.** Saying yes on the terrace backs the car round the
apron in a two-point turn — the pocket has no room for a U-turn of any usable
radius, so the reverse leg is part of the planned road rather than a mode on
the car — and drives the whole `620 m` back down into the tunnel; the City then
brings it out of its own south portal still moving and home to the island. The
ride stage is a ring rather than a one-way ladder, and both halves still build
the man and the car from that one value, so he is never in two places and never
in none. Arriving on the mountain any other way — the area tab, or a point
picked on the chart — brings the car up with the hero, because a mountain with
no road down and a cableway that only goes up is otherwise a place the chart
can strand him in.
The car has a voice of its own (`LastRouteCarAudio` over the pure
`LastRouteCarEngineModel`): a petrol four with a three-speed box, so the climb
is heard as the drop into second before each hairpin and the load of the grade
after it; tyres on wet asphalt in the city and packed snow up here, the bridge
deck and its joints, the tunnel's reverb, the starter on the island and the
key-off on the apron, and the wind bed muffled behind the glass for as long as
the hero is in the seat.

Above the cableway, `AlpineVillage` is one gently crooked `82 m` uphill axis
from the return station to the unique top house. Twelve houses form authored
clusters and pauses rather than an alternating subdivision; their rotated
footprints use exact OBB validation, three authored rear-row depth beats and a
bounded symmetric local correction around those beats.
The chapel, closed adit and ordinary burial ground remain side finds, and every
permitted threshold/spur consumes one visible compacted-path descriptor in both
rendering and traversal. `TerrainMeshBounds` extends the inhabited bowl into a
fully physical `74°` enclosing rise, hidden crest and sampled cableway brink,
and the bowl looms: the toe stands `15 m` outside the top house's envelope
and the `60 m` crest `16.7 m` past it — a mean `34.1°` from mid-lane and
`43°` on the nearest bearings. The rise is the second submesh of the one
ground mesh on its own `CityMountainPhysical` material
(`AlpineVillageRidgeAppearance`: village haze, the breathing density, a `0.40`
visibility floor and a stable opaque colour handoff over `96-108 m` inside the
`110 m` plane). Village floor, rise and lying snow bake the same world-planar
UV scale once and retain identity material transforms; floor and rise share
their toe indices and the same PS1 vertex snap, so neither a buried overlap
ring nor moving screen-space coverage can make the lower wall crawl. The
shared shader's City defaults keep their existing clip-dither handoff.
The deterministic passive village kit now carries `17` assemblies / `43`
role meshes under generator contract `v3.0.0` / `village_house_archetypes_v3`.
Its twelve ordinary houses draw from two structurally different closed-shell
archetypes: a low dark timber block on a heavy stone plinth, and a taller house
whose timber upper storey projects on brackets above a high masonry base. The
unique top house is a third type, a broad timber main mass with one weathered
whitewashed masonry side wing. Their roof solids and snow, facade repairs and
shutters, garland posts, cable gate, rail bridge, catch basin, chapel, mine
cart, adit, grave markers and firewood add no new surface family. The three
house types retain the plan's normalized bounds, footprints, collision and
meaning; no heraldry, frescoes or tourist-chalet decoration was introduced.
Two garland cords and three windows own the five real village
lights. Six bounded synthesized spatial voices stay on visible causes. Two
or three raven pairs sit hunched against the gale at the adit mouth, the
firewood mine cart and a lane fence — never at the chapel over the spring,
the graves, the top house or the station — their voices an accepted
exception outside the warmth grade's six-voice contract. A
village-only blizzard profile keeps snowfall at `.88–1` and wind at `.82–1`,
adds terrain-sampled ground spindrift and drives one continuous synthesized
wind bed from the same deterministic bearing and gust rhythm. A second,
presentation-only peripheral field places strong soft snow curtains outside
the complete trodden-route network and behind the mother's house. Its spatial
plan preserves a widening station-to-house aperture around the whole building,
and it changes no collider, damage, speed or walkable mask: leaving the path is
still physically allowed, but the weather makes it feel exposed. All nine
garland spans read that shaped wind too: their batched render meshes deform
with both anchors fixed, while bulbs and the two real lights follow each free
midpoint. The station
canopy and moving cabin stay locally dry. The haze breathes with the gale:
between gusts it sits at `0.017` (`9 %` of the mother's door at `91 m` from
the platform — the landmark at the limit of sight), at a gust crest it
thickens to `0.045` (`41 m` left at `3 %`: the far half of the lane closes
for seconds and the top house is gone), and it thins back every cycle
because the wave is keyed on the raw shared gust rhythm (`0.66-0.86`, attack
`0.5 s`, release `1.0 s`) rather than the shaped gale that pins at `1` for a
whole thunderstorm slot; one writer applies it every frame, the uphill axis
and the nearest walls read throughout, and the running trough keeps the
house at `>= 5 %`. No lightning, thunder, silhouette or panorama comes with
it. The one warmth grade
is held at `0` until the prologue exists, but already enters the per-minute
apply and jointly removes isolated garlands, darkens seeded windows, dirties
snow, weakens the five practicals and quiets those six local voices without
changing the storm; its dim end rides on the storm base and is clamped at the
storm peak.

The runtime places one visually
distinct player home beside a bar street and one deterministic street-front
supermarket, instantiates the same modular low-poly 3D hero in all nine gameplay
roots, loads separate bar, supermarket, stairwell, home, church and mother's-house
interiors, and
restores the same seed and matching exterior return point. The hero keeps
independent body meshes on one Generic rig, uses continuous in-place 3D clips
for locomotion and contextual actions, including a separate heavy, weary
`0.75 s` Run with a short flight phase and a grounded lean/right-hand
press before every ordinary location-door transition, hands failed balance falls from a
directional clip into a bounded runtime ragdoll and back into an authored rise,
and derives the refrigerator's visible first-person arm and the inventory
portrait from the same production model. Bar drinking instead keeps that
seated world rig and runs a nested full-body pickup/sip/return action on it.
That live model is `Resources/Player/Player3DV2`: the
adult-proportion, atlas-faced Hero V2 in the canonical olive field jacket,
with `41` bone-only Actions. Holding either Shift or gamepad L3 while moving
forward raises the `2.6 m/s` walk to a `4.2 m/s` run; backward movement stays
at `1.4 m/s`, intoxication still scales movement, and scripted approaches stay
at walking pace. The gait blend follows actual constrained speed rather than
the input request. This Hero V2 is the sole packaged player model; the
independent pedestrian bank remains at `37` Actions.
Ordinary building masses use a separate `36-52 m` height
profile whose roofs fall below one-percent visibility in the fixed City fog;
the bar, the supermarket and the player home retain their original low-rise
heights. Every ordinary lot now instantiates one fixed-metre Blender prototype
for its district. Each prototype is split into seven semantic surfaces:
primary/secondary facade, plinth, roof, metal, window frame and window glass.
Generator `2.1.0` exports `28` meshes / `4,218` triangles across the four
wrappers. Six opaque surfaces use one of `24` deterministic district sheets through
side-atlas, full-face or physically scaled UVs; glass preserves pane-local atlas
detail plus deterministic row-balanced warm/dark state through the shared UV2
slot shader. The generator rejects competing exterior coplanar faces and broad
opaque layers closer than `0.03 m`; the terrain foundation sits `0.08 m` inside
the visible footprint. Residential alone owns eight semantic balcony slots:
two at each of four facade levels, each pairing a `2.5 x 1.2 m` deck and its
NPC dock one-to-one with a person-height glazed apartment door and adjacent
window. The legacy decoration descriptor remains stable but emits no second
balcony stack. The single
Residential bar separately uses the
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
The street supermarket likewise uses one complete fixed-metre
`supermarket_exterior_v1` instead of its CityMisc shell, generic apartment
window bands and runtime-box storefront. Its low `15.5 x 15.5 x 6.4 m` body
owns dark brick piers, a recessed double entrance, four framed shop windows,
an integrated weathered cream/ochre/green/burgundy fascia, service elevations,
parapet and low roof plant. Four dedicated exterior sheets bind through
authored per-element UVs: wall and fascia atlases clamp, while brick and metal
repeat at physical scale; the existing roof and supermarket glass families
remain shared. The authored `ПРОДУКТЫ` sign is original and contains no
7-Eleven mark, `7`, price or slogan. City aligns the imported
`exterior_door` anchor to the unchanged lot door, keeps the full logical
collider, entrance apron, trigger and yard spotlight outside the prefab, and
uses an `0.14 m` inset foundation. A fully visible Home reconstruction reuses
the same passive model; only a half-space crossing keeps the clipped legacy
silhouette.
The player home likewise uses one complete fixed-metre
`player_home_exterior_v1` instead of its CityMisc shell, generic window bands
and runtime-box roof/balcony dressing. Georgian Series 209-1 is the restrained
architectural reference: repaired render over a brick plinth, a pitched slate
roof, irregular framed windows, a recessed street entry and a deep supported
upper gallery. The body remains `13 x 12 x 8.8 m`; its canonical balcony
begins at the unchanged front plane and projects `2.3 m` toward the street,
so complete visual bounds reach local `+Z 8.3 m`. Nine semantic sheets use
authored or metre-scaled UVs, and opaque overlays retain `0.03 m` clearance.
Exactly one pane stays lit: the upper street window immediately left of the
balcony; every other pane is dark. Unity aligns the passive asset through the
unchanged door anchor and still owns logical collision, walkway, mailbox,
entrance lamp, number `7`, beacon, trigger and transition. Home reconstructs
the same facade materials, authored window positions and recessed entry around
the real walkable balcony instead of drawing a second generic shell. Its
bounded fixed-camera reconstruction deliberately omits the street model's
narrow front eave fascia: at balcony height that strip crossed the shot as a
foreground beam. The pitched roof edge remains, so the shelter and silhouette
do not change.
Up to four ordinary Residential frontages receive deterministic shallow
courtyard pockets no more than `1.05 m` deep. Their six authored variants are
a Nardi table, bicycle repair, a balcony basket and pulley, chair repair, a
sweeping kit, and a quiet bench with planters. Doorways, public accesses,
district points of interest, collision proxies and existing laundry remain
clear; the pocket proxies participate in the same wind-dressing clearance, so
a wash line is moved or omitted rather than crossing the new furniture.
Selected active pockets may borrow generic colliderless residents, but the
balcony-basket and quiet variants stay unoccupied. The residential courtyard
resident pass is capped at five actors and adds no speech, interaction, light,
sound or story state; fringe Yards receive none.
Every ordinary Residential building also contributes one deterministic
balcony-smoker candidate. A per-session local director rolls appearance
opportunities around the moving player, uses the fog-readable lowest balcony
row, prefers docks `12-22 m` across the street and ahead of travel, keeps at
most two active and at
most one per building, and releases a resident after the player leaves the
area; the player home is never a candidate. It selects from the current roaming
archetypes, reuses the literal
Hero V2 `SmokeLoop` through a hidden native Hero rig and transfers its `31`
canonical local bone channels to the selected pedestrian. It also reuses the
already authored cigarette/ember geometry and mouth-exhale plume. Home keeps a
separate bounded deterministic selection on fully reconstructed nearby
prototypes and gates it with the Balcony shot. These actors add no collision,
interaction, speech, sound, light or story state.
The broad passive City misc layer now resolves through the deterministic
`city_misc_citywide_v4` Blender library at generator version `4.9.0`: `82`
semantic kinds, `122` assemblies, `259` role meshes and `46,542` triangles
cover ordinary decoration and park
landmarks, night fixtures, Route 01 stops, the eastern yard, cemetery,
seacoast, fringe service belt, the static shells of all four district points of
interest, the fixed Nightlife arch shelter with its full-height supported
service terrace joined to the east wall and south facade end. Its former bar,
supermarket and player-home shells remain catalogued only for v4
compatibility and are not instantiated. Unity
plans still own placement, terrain, collision, dynamics, interactions, lights,
halos, cloth and NPCs. The catalog's old standing, seated and sleeping shelter
meshes remain compatibility-only and are no longer instantiated. Their live
replacements are three staged `NpcHumanV2` adults on the Hero V2 Avatar, each
with a `256 px` detail atlas and a dedicated quiet rig loop. Fifteen structure,
barrel, bedding and clutter components reuse measured City surface albedos
through material property blocks. Tilted cemetery monuments deliberately
remain on their legacy geometry. Road v2 gives
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
  The on-disk humanoid-NPC asset set contains `27` rigged designs: the five
  models in the production pedestrian folder, `17` staged residents, the
  active ordinary and retained inactive six-armed bartenders, the active
  normal and retained inactive Watcher cashiers, and the bus driver. The
  active humanoid cast does not grow: `bar_bartender_v2` replaces
  `six_armed_bartender_v1`, and `supermarket_cashier_v1` replaces
  `watcher_cashier_v1`, one for one; the on-disk roster keeps both superseded
  models as legacy assets. Roaming
  is no longer the same distinction:
  since 2026-09-02 the street pool is eight, and seven of those eight are
  staged residents that roam as well. Every one uses `NpcHumanV2`: the exact Hero V2
  31-bone A-pose hierarchy and Avatar copied from
  `Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx`, with a shared
  `0.835 m` rest pelvis. The five production-folder and nine ordinary staged
  model manifests, the four Mountain Road cafe models with their separate
  `10`-clip bank, and the `53`-clip `CityPedestrianLocomotion` bank all use
  `4.5.2` — one shared generator, so one version. The mother and her own bank
  sit one revision back at `4.5.1`; the shelter trio and their isolated
  three-loop bank at `4.3.1`; the active bartender manifest uses `3.1.0`,
  while the retained six-armed bartender and driver manifests use `2.0.0`.
  The cashier generator owns two
  sibling assets over one `256 px` garment-detail atlas: the `1.0.0`
  `SupermarketCashier3D` output is the active `1.75 m`, `40`-mesh / `1,244`-tri
  ordinary-proportioned clerk, while the retained `2.2.0`, `44`-mesh / `1,588`-tri
  `SupermarketWatcherCashier3D` preserves the former undersized head and
  segmented periscope neck without remaining provider-reachable. Their FBXs were
  reimported and the production prefabs/provider assets rebuilt, so runtime uses
  these models rather than retaining the former bodies behind new plans.
  Every modular `SkinnedMeshRenderer` refreshes its bounds from the live pose
  through `NpcSkinnedMeshCullingGuard`. Clip-driven limbs and bounded
  procedural looks therefore cannot be frustum-culled by the small A-pose boxes
  imported from the separate model FBXs.
  The other special active models include the ordinary two-armed,
  `39`-mesh/`1,136`-triangle full-body `1.75 m` bartender and the
  `48`-mesh/`1,496`-triangle driver. The bartender wears a dark-green
  waistcoat, rolled sleeves and an apron, with a service towel in his idle
  hand. `BarBartenderProvider` selects `bar_bartender_v2` at the authored
  counter anchor and retains `six_armed_bartender_v1` only through its
  inactive legacy reference. The active root stands on the bar floor: the
  former `0.42 m` duckboard and matching root lift belonged to the superseded
  six-arm presentation and are absent from the active world. The active
  registry reuses the four `CafeAttendant*` service clips from
  `MountainRoadCafeCast`, plus the complete `Walk` cycle from the compatible
  Hero V2 bank; a manually evaluated
  `PlayableGraph` reads `BarDrinkServiceTimeline`. During counter travel the
  root first faces its path, then advances with that full leg cycle before
  turning back to the authored working orientation. For beer the bartender
  walks to the central tap, takes and fills the slightly enlarged handled beer
  mug, carries it to the selected place and sets it directly before the hero
  with its handle on his right; the right hand pulls the tap handle while the
  left steadies the vessel. The full mug then waits without a timeout: looking at it owns
  both the localized drink prompt and the same thin yellow contour used by the
  closed menu. Accepting the prompt runs a nested pickup, sip and return on the
  visible seated Hero V2 rig. His right hand grips the handle; the seated
  first-person camera begins from the accepted gaze and keeps its captured eye
  offset through the animated head's complete reach-and-sip pose. It eases only
  `0.02 m` back along that gaze as the rim reaches the mouth. The
  rim stays at his mouth while the mug reaches horizontal and the head and
  torso rise with it, and the close-action clip plane keeps the vessel visible;
  then the mug remains empty on the counter.
  Cash is paid at confirmation, while drinking effects commit exactly once
  only after that visible action completes. During the
  quiet shared Wipe, that grounded placement puts the towel against the real
  `Y = 1.02 m` counter top and the clip carries it along the surface instead
  of through the air. The former six-arm cocktail chord was never implemented
  and belongs only to the superseded legacy proposal, not the active delivery
  plan. All four bar
  stools not occupied by patrons are usable, each with its own safe entry,
  exit, camera and service offset; the rightmost is moved inward clear of the
  counter return, and the former single-seat floor/sign marker is not shown.
  The interior arrival stands `1.10 m` farther inside the entrance than the
  former spawn, keeping the restored chase-camera lens in front of the closed
  door after the transition.
  Exactly three beer taps form a compact `0.33 m`-spaced bank at the seat-free
  right end of the counter, beyond that last stool. The menu dock shares the
  selected seat's lateral axis, so the bartender puts the booklet directly
  before the hero just as the Mountain Road cafe attendant does.
  Its physical menu keeps the cafe's resting-booklet gaze reopen, look-away
  stand and post-exit staff retrieval lifecycle, but not the cafe's input
  semantics. The cafe remains selection-only: Space/gamepad West records its
  placeholder choice, while E/Enter/gamepad South rests the booklet. In the
  bar, either E/Enter/gamepad South or Space/gamepad West orders the selected
  drink, while Escape rests the booklet without ordering. A
  thin yellow contour surrounds the closed booklet only while that same gaze
  predicate changes the contextual prompt to "open menu"; looking away or
  beginning the unfold clears it, so the open spread is never highlighted. A
  successful bar purchase runs service while that booklet remains closed, and
  a later completed sit receives a fresh delivery. The four visible offers
  occupy one readable inset `2 x 2` grid across the spread, with safe inner
  margins that keep every name, price and description inside its cell. Long
  copy wraps by words instead of shrinking the whole block, so all four use
  the same visible type scale. The
  spread focuses at `0.45 m` / FOV `72` from a near-overhead pose whose
  projection stays close to the booklet centre. Its bar-only page style uses
  larger bold, near-black type. Each block shows a dry low-grade product name,
  price and two-line description without truncation.
  The visible order is flat house beer, cheap fortified wine, unaged distillate and
  bottom-shelf vodka; four matching bottles are centred on the back bar. It
  restores the exact seated pose when it closes. Bar-menu cinematic DOF does
  not begin during seating or delivery: only the fully open menu starts the
  page-focused `35 mm`, `f/8` profile. Resting the menu, confirming service or
  beginning exit clears it immediately. Its left leaf folds over the upper arc
  into a separated opaque stack. Independently, true-interior post-processing
  uses restrained Gaussian depth of field with a maximum radius of `0.55`, so
  nearby characters remain readable instead of acquiring close-range bokeh.
  The interior entrance is the facade-matched standard `1.45 x 2.34 m`
  panelled door, not the former room-height curtained portal. The common adult
  substrate changes large anatomical proportions, not identity: the Long-Arm
  remains mouthless with ground-reaching forearms and heavy hands, the kettle
  and hopper silhouettes remain, the inactive legacy bartender alone keeps
  six arms, and the driver keeps the long horizontal eyes. The active
  cashier keeps the former uniform, face, blink and attentive bounded gaze but
  has an ordinary head and a non-scaling human neck; the old `18 m` Watcher
  treatment survives only in its inactive asset. `NpcDesignAppearanceCatalog`
  consequently covers `29` on-disk character designs: `8` bizarre and `21`
  normal. The inactive Watcher and six-armed bartender remain bizarre; their
  active cashier and ordinary bartender replacements are classified normal.
  The presentation pool repeats a stable ordered catalog of eight ordinary
  people: the Chair Carrier, the Yard Babushka, the Weigh Attendant, the
  Cemetery Watchman, the two Park players, the Cemetery Mourner and the Lake
  Fisherman. Seven of those keep a placed role as well, so each carries TWO
  clip pairs on one prefab — the placed pair its own presentation reads
  through `IdleClip`/`WalkClip`, which is why the babushka can still beat her
  carpet on the spot in the drying yard, and an ordinary street pair the pool
  reads through `RoamingIdleClip`/`RoamingWalkClip`. That street gait is the
  hero's own eight-key walk cycle re-authored into the pedestrian bank rather
  than referenced: the pedestrian importer forces
  `lockRootHeightY`/`lockRootPositionXZ` and bakes the vertical pelvis arc
  into the pose where his importer does not, and the pedestrian `Animator`
  runs with `applyRootMotion = false`, so a reference to his clip asset would
  have produced a walk with a dead pelvis. It is merged onto each design's own
  base pose, so seven designs share one gait and each keeps its coat and
  posture.
  Three of the eight declare a seated Route 01 ride and own an authored `Sit`
  loop — the Chair Carrier, the Weigh Attendant and the Cemetery Watchman. The
  other five are refused by measurement rather than by preference: seating
  aligns the shared rest pelvis to the cushion, and the mourner's coat hem
  hangs `0.4256 m` below that bone and the babushka's housecoat `0.3347 m`
  against `0.05-0.13 m` on every design that does ride, while the fisherman's
  shouldered rod rises `1.9047 m` above it and the park players' shout
  `1.19 m`, both through the cabin ceiling. No design on the street wears a
  real-time light at all, which replaces the former cap of exactly one. The
  pool exceeds the active population, so a repeat encounter shows a different
  mix.
  Four walkers came off the street in the same change and were deliberately
  kept in the project: the courtyard vignettes cast the Lampshade, the Long-Arm
  and the Chair Carrier by name, and `MothersHouseKettleProp` instantiates the
  Kettle Hat walker whole to borrow ten of his renderers for the mother's
  teapot. They resolve through `CityPedestrianResources.TryGetArchetype`, which
  searches the roaming catalog and the non-roaming table alike, while the
  narrower `Roams` answers whether a design is actually on the street; their
  prefabs stay in `Resources` and only their catalog membership changed. Their
  own looping in-place locomotion is intact: the Lampshade
  stays hunched through idle and walks in short uneven steps, the upright Chair
  Carrier — the one design of the old five that stayed, because he was always
  an ordinary man with a strange load rather than a strange body — uses a
  precise high-knee gait beneath an inverted cafe chair, the
  stout short-legged Kettle Hat Walker waddles in fast small steps while its
  belly and its oversized skewed enamel kettle swing against each other — and
  that kettle is permanently on the boil, in every state including the bus
  ride: a pure `KettleBoilModel` fed the presentation's own (distance-
  accelerated) delta through `CityPedestrianPresentation.Advanced` rocks an
  editor-built `ANCHOR_KettleLid` pivot under the head bone that the lid and
  knob are re-skinned to (no bone added to the 31-bone rig), and vents a
  code-built grey steam plume from `ANCHOR_KettleSpout`, attached by the
  factory and never authored into the prefab, with no Light and no sound. He
  is also the one pooled walker with a `256 px` detail atlas — light greys
  multiplied by his palette tint through the shared `Player3DLit` material via
  the property block, so all four palettes survive — at `2,004` triangles /
  `52` meshes with full-length sleeves, cuffs, lapels, a thumb and finger
  block per hand and boots with toe caps and heels; and
  the narrow Long-Arm Walker — the only design whose strangeness is the body
  itself rather than a worn object — shuffles slowly on barely lifted feet
  while bare forearms reaching the pavement swing a quarter cycle behind the
  legs, and the Helmet Lamp Hopper crosses ground in two-footed rabbit bounds
  on `0.46 m` hind feet with a `0.24 m` apex, wearing the one working light
  the pedestrian contract allows: a single always-on shadowless `7.5 m` Spot
  on its miner's helmet. He is now off the street, so no such light exists in
  the ambient crowd at all - a stronger bound than the maximum of one pooled
  instance his archetype used to declare, and one an EditMode test asserts
  directly over every roaming prefab rather than over his instance count. Every walker
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
`CityBusDriver3D` uses the shared `Player3DLit` material and the
`NpcHumanV2` rig/Avatar copied from Hero V2, with a normal low-poly head and
the canonical long horizontal eyes. Procedural seated IK keeps
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
The shared seating rule applies to the four rider-capable pooled designs. Like
every `NpcHumanV2` rig, they use Hero V2's exact 31-bone Avatar at a
`0.835 m` rest pelvis, so the runtime aligns that bone to the cushion rather
than pinning the lowest sole, which on a seat would drag the model down until
its boots touched the cabin floor. What varies is declared per archetype -- an
authored seated posture, a pelvis lift and back offset, and a headroom band the
deterministic generator proves against the real deformed meshes. The `2.05 m`
cabin and `0.41 m` cushion leave `1.64 m`; the four rebuilt riders measure
approximately `0.907-0.918 m` above the seated pelvis and hang
`0.375-0.388 m` below it.
Fare/payment, destination selection, passenger persistence and
live bus tracking are deferred. The City map still draws Route 01 as a pale
neutral line beneath the darker bone-toned player itinerary, plus five numbered
localized stop markers in the default layout and a compact legend; it has no
live bus marker. The moving bus runtime is deliberately City-only. Home's
balcony
reconstructs the nearby Home stop as a static collider-free `01` pole but has no
bus actor or director: the real exterior has no Street pass-through with both
complete-body seams hidden at `56 m`, and the default home faces a visible road
terminal. The project does not fabricate another road or make bus appearance
depend on the Balcony camera, avoiding a visible activation/pooling pop.

Every operated runtime IMGUI surface uses `RetroUiTheme` as one crisp
post-composite `640x360` interface: soot/charcoal/dirty-bone values, flat
rectangles, thin nested frames, stable grain and grayscale-readable focus.
The interface reference is UI-only; it does not alter world rendering, camera,
aspect, audio, gameplay or localized copy. The full contract lives in
`ai/city-zones-art-bible.md` §15a.

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
desynchronize the sky. One shared `ExteriorCloudField` now makes that sky
visible in City, MountainRoad, AlpineVillage and the active Home balcony shot.
Its generated 220-triangle hemisphere follows camera translation while
retaining a canonical compass frame, so the `47 / 119 / 109 m` render radii
inside the areas' `48 / 120 / 110 m` far planes never read as physical cloud
altitude or produce low-cloud parallax. City and Home share the exact City
profile, seed and absolute-time phase; the road and village only reshape
coverage, scale and colour. All profiles advect from the existing deterministic
wind schedule, blend their horizon into the area's current haze through one
property block and add no light, shadow, fog or grade owner. The field is
absent from true interiors and disabled in Home outside the Balcony shot.
Rain renders as a player-following field of stretched streak particles on the
shared atmosphere shader (`CityRainField`, up to
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
distance. The balcony view shows the same rain - on its own anchor half a field
extent past the facade (`RainAnchorDepth`, not the fog's `25.5 m`, which
left every streak twelve metres out and fogged), so streaks fall past the
lens and onto the deck, with the hero's building registered as a kill volume
so none crosses the glazed bedroom - and the same flashes, and plays the
same rain bed and thunder, all gated to the active Balcony shot; a
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
`CityFringeYardLandmarkPlanner.cs` and `CityFringeYardLifePlanner.cs`, receive
measured surfaces through
`CityFringeYardSurfaceAppearance.cs`, split their conforming terrain through
`CityFringeYardGroundWorldBuilder.cs`, and materialize detail through
`CityFringeYardWorldBuilder.cs`; its four runtime practical anchors are leased
by `CityNightAtmosphere.cs` without expanding that pool. Residential frontage
pockets start at `CityCourtyardPocketPlanner.cs` and
`CityCourtyardPocketGeometry.cs`; their bounded ambient cast is planned and
instantiated by `CityCourtyardResident{Plan,Factory,Presentation}.cs`.
Supermarket truth starts
at `Assets/Scripts/Runtime/Scenes/SupermarketInteriorRoot.cs` and
`Assets/Scripts/Runtime/World/SupermarketInteriorLayoutPlanner.cs`; authored
hall and product geometry is exposed by `SupermarketInteriorAssetRegistry` and
`SupermarketProduct{AssetRegistry,ModelResources}`, then shared with inventory
and refrigerator presentation through `InventoryItemModelFactory`. Session-time
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
  cashier, supermarket interior and product pack, cat, chess set, Last Route
  car, church, Mountain Road misc and City misc — each pairing its script with
  a measured JSON manifest and a
  determinism check. Blender lives at
  `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe`.
  This is a rule for what is built from now on. The structural runtime-primitive
  geometry still in the tree — terrain, roads, logical building collision and
  foundation masses, infrastructure, dynamic precinct pieces, the mountain
  road and its terminal — predates it
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
  authoring) and is fully migrated, interior and facade. The supermarket is
  now the next complete fixed-metre user: its exterior comes from
  `tools/build-supermarket-exterior-3d-model.py`, while the passive interior
  shell, fixtures and CCTV pivots come from
  `tools/build-supermarket-interior-3d-model.py`. Its six reusable generic,
  unbranded and text-free product models come from the separate passive
  `tools/build-supermarket-products-3d-model.py` pack: five are finite shop
  stock, while the open stew can enters the world only through the Home
  refrigerator/cat flow. The apartment/stairwell remain future migrations.
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
