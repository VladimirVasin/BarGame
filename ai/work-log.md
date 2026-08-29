# Work log

Entries are reverse chronological. Record outcomes and verification, not a transcript.

Entries from months before the previous full month live in `ai/archive/`;
see [`ai/README.md`](README.md) for the retention rule.
Earlier entries: [`work-log-2026-07.md`](archive/work-log-2026-07.md).

## 2026-08-29 — Windows read as windows, with warm light on every floor

The reported facade failure had three layers. Ordinary Blender buildings fed
their window shader the sky's raw `NightFactor`, so every selected pane went
black from 07:00 to 18:00. The old Nightlife schedule then concentrated its
strongest share on the front ground floor and deliberately darkened upper and
rear rows. Finally, `CityBuildingWindowSlots` never sampled
`CityWindowAlbedo`; its solid emissive quads could only read as cards rather
than framed glass.

- The district ratio is now quantized independently for every authored
  floor/side row. Each row gets at least one stable lit pane and, when it has
  multiple panes, at least one dark pane. A block/floor/side phase changes the
  chosen bays without changing row density, so upper floors and rear facades
  cannot go wholly dark and the building does not become an all-lit grid.
- Every selected pane uses exactly
  `CityNightAtmosphere.StreetLampColor` (`1, 0.72, 0.42`) and the §20 fixture
  factor, retaining two thirds of its night strength at noon. Districts still
  differ in lit share and aperture geometry; cold Industrial rooms and the
  lower-floor Nightlife bias are superseded by the accepted canon exception.
- Generator `1.1.0` gives every `WindowGlass` face a complete projected
  `0..1` UV0 while preserving the UV2 slot ID. The shader selects one complete
  frame/curtain/blind atlas quadrant with a half-texel inset and multiplies it
  into both albedo and emission, so a bright pane keeps the form of a window.
- The authored bar uses the same warm emissive material for its storefront and
  two upper groups while its middle upper group stays dark. Its metre-scale
  planar UVs deliberately receive white albedo/emission maps because separate
  sash geometry already supplies the window form. No realtime `Light` was
  added and the City budget remains `12`.

Fast verification: the focused `CityWindowAppearanceTests`,
`CityDistrictPresentationPlannerTests`, `CityBuildingAssetTests` and
`CityBuildingPrototypeRuntimeTests` selection passed `18/18` in `2.135 s`.
The explicit GPU-backed `AreaCaptureFixture.CityWindowLighting` selection
passed `1/1` in `6.670 s` at noon with raw night factor `0` and fixture factor
`2/3`. Its five close street-level frames were inspected after correcting the
capture camera to use the prototype's authored door/front anchor instead of a
fog-hidden lot centre: the bar retains its dark middle sash, and Old Town,
Residential, Industrial and Nightlife all show warm atlas-detailed panes on
lower and upper rows with dark gaps between them. No broad Unity suite, player
build or startup smoke was run.

## 2026-08-29 — The Ferryman's car gets its voice, and the climb is heard

The user asked for engine sound on the Ferryman's car and, more broadly,
for the sounds of the climb up the mountain road to be worked through. The
2026-08-25 entry had already said it plainly: the bus has `CityBusAudio`,
the car had nothing, and a `620 m` climb played out with no engine under it.

- **The engine is a model before it is a sound.** `LastRouteCarEngineModel`
  is pure, like the drive and suspension models beside it: speed,
  longitudinal acceleration and grade in, revs and load out, through a
  three-speed box with hysteresis (`0.84` up / `0.36` down) and a `0.32 s`
  clutch dip per change. That box is most of what the climb sounds like -
  the drop into second before each hairpin and the grade held in it after -
  and the `8%` grade arrives as LOAD rather than revs (`0.14` rolling on the
  flat, about `0.5` on the climb, `0` on overrun), which is what opens the
  exterior low-pass. Ignition is a phase ladder: `Starting` (`1.05 s` of
  cranking under idle, a flare to `0.58`, a `0.65 s` settle) -> `Running` ->
  `Stopping` (`0.8 s`) -> `Off`; `Start(alreadyRunning)` is the mountain
  leg, where the car comes out of the tunnel with an engine that never
  stopped and there is no starter to hear.
- **Five voices, source-first, the bus's rule.** `LastRouteCarAudio` hangs
  off the runtime root beside the driver, anchored along the root's own
  axes from the registry's dimensions (never an imported node - the
  headlights' trap): a petrol-four loop in the engine bay under the bonnet
  he sits on (`42 Hz` fundamental, every fourth firing weak, a valve tick
  over it), a 2D cabin loop that fades up over `0.35 s` while the hero is
  in the seat, a tyre loop at the rear axle whose surface is `WetAsphalt`
  in the city and `PackedSnow` on the mountain (`0.55` gain, `1500 Hz`
  against `4200`), a bridge-deck thrum under the same axle with an
  expansion-joint thump at either abutment (the abutments resolved onto
  the car's OWN path by `FindNearestDistance` the first frame it has one,
  and silent across a skip), and one cue voice for starter, key-off and
  the door latches (a leaf armed past `0.15`, fired under `0.02`). Every
  loop is authored at frequencies that divide the four seconds exactly, so
  the tonal part is phase-continuous at the seam and only the noise is
  crossfaded. The engine is a petrol four on purpose: the bus is a diesel,
  and the two should never be confused across a street.
- **The tunnel closes round it.** An `AudioReverbFilter` on the engine bay
  and on the axle, faded by room level over `0.45 s` rather than switched,
  driven by `MountainRoadRoot.IsInsideTunnel` - factored out of
  `IsSheltered` so it is a function of a point and not of the player - on
  the mountain, and by `CityTunnelShelterController.IsSheltered` in the
  city.
- **The wind drops behind the glass.**
  `MountainRoadWindSoundPlayer.SetEnclosure` is a second, independent
  factor on the bed (`0.42` volume, `0.45` cutoff), so the car can muffle
  it without becoming a second writer of the wind: the wind driver keeps
  writing strength every frame exactly as before.
- **Ignition follows the journey by polling, the headlights' rule.** The
  engine is wanted while the man is at the wheel and the car has not
  arrived, or while the road is running. On the island it turns over the
  moment he takes the wheel and idles while the hero walks round to his
  own door; on the apron it is switched off once, and the block has died
  before the Ferryman gets out. `LastRouteCarFactory.InstallMechanisms`
  raises it with the springs, the doors and the driver; both roots bind
  it (`MountainRoadRoot.BuildLastRoute` with the deck and the wind bed,
  `CityGameRoot` with the city tunnel), and `BindWindBed` closes the loop
  whichever of the car and the bed is raised second.

Verification: headless on `6000.5.10f1`. EditMode `LastRouteCarAudioTests`
`10 / 10` - the ignition ladder (crank under idle, flare over it, settle),
the mountain leg starting already running, the box climbing first-second-
third on a straight and dropping to second for a `3 m/s` hairpin with a
measured rev dip on every change, load `0.14` flat / `~0.5` on `8%` / `0`
on overrun at identical revs, key-off dying in `0.8 s`, the gain and
surface laws, loop determinism/bounds/seam and cue tails, and the real
factory-built car carrying exactly five sources on the runtime root with
the engine bay forward of the perch and the tyres on the rear axle.
PlayMode `LastRouteCarRidePlayModeTests` `7 / 7`, the six existing rides
untouched plus `Ride_IsHeardFromTheEngineBayAndFallsSilentOnTheApron`:
running from the tunnel with no starter, revs and a gear change on the way,
tyres up, cabin blend at `1` with the hero seated, one key-off at the apron
and every loop stopped after it. Proved red first: with `IsEngineWanted`
forced false the new test fails at its first assert, "It came out of the
tunnel running". Runtime, EditMode and PlayMode assemblies compile clean
(a neighbouring session's window-light edit broke the runtime for a few
minutes mid-way and was waited out, not touched). No player build and no
audition through speakers: whether the petrol four READS against the bus's
diesel, and whether `0.56` full-throttle is the right level under the wind
bed, is the user's ear to judge.

## 2026-08-29 — The balcony rain was twelve metres out and fogged

The user reported, after the eternal-rain decree, that from the balcony the
rain was STILL not visible. It was not an intensity problem and not a
toggle: the renderer was enabled, the field was simulating, and the decree's
`0.18` floor was applied. It was geometry. `HomeBalconyExteriorAtmosphere`
built its rain field on the FOG anchor - `FogAnchorDepth 25.5 m` past the
facade, at street level, which is right for fog sheets that must not fill
the apartment - so the `26 m` rain box began `12 m` from the balcony lens
and ended `38 m` out. Under the city's Exp2 haze (`0.070`) that is `49%`
visibility at the near edge and `1%` at the far one, on streaks two
centimetres wide at a tenth alpha; the street scene never had this problem
because there the field FOLLOWS the hero and streaks pass the lens at arm's
length.

- The rain now stands on its own `RainAnchor`, `RainAnchorDepth =
  FieldExtent / 2` past the facade (`13 m`), still at street level and the
  balcony's own Z. The field is therefore born exactly ON the facade
  plane: the balcony camera stands inside its footprint with the spawn
  plane `4 m` overhead, and streaks fall past the lens and onto the deck
  the way they fall around the hero in the street. Street level is kept
  deliberately - born twelve metres over the STREET, a streak reaches the
  pavement below rather than dying mid-air in the frame.
- Wind can carry a live streak through the facade, and the bedroom behind
  it is glazed (ajar door, window), so the hero's own building is now a
  `CityRainField` local shelter - the same trigger-kill volume the
  Nightlife arch uses - covering the building's whole column from street
  to roof lip and `0.5 m` into the walls, hung off the atmosphere (the
  exterior view is collider-free by test contract) as a trigger on the
  Ignore Raycast layer. The sky above the roof stays rain.
- The drift was city-axis wind applied unrotated in a scene whose street is
  the city turned to face `+X`; it now goes through
  `PlayerHomeBalconyGeometry.ToHomeLocalDirection` like every other
  city-to-home vector.
- `HomeBalconyPresentationPlayModeTests.HomeScene_RainFallsPastTheBalconyLens`
  pins the contract as geometry rather than as a toggle: the spawn box's
  near edge is the facade plane, the lens is inside the footprint under the
  spawn plane, the drift is the turned wind, the shelter's bounds are the
  building's, and over two pinned-clock seconds a streak passes within
  `8 m` of the lens while none is found inside the building. The shared
  `AssertExteriorAtmosphere` now asserts the rain anchor beside the fog
  anchor.

Verification: headless PlayMode `HomeBalconyPresentationPlayModeTests` on
`6000.5.10f1`: `3 / 3` passed, the new test named in the results XML.
Proved red first: with `RainAnchorDepth` temporarily set to the fog's
`25.5 m`, the new test fails at its first geometric assert - "Streaks are
born ON the facade plane", expected `5.13`, was `17.63` - which is exactly
the twelve metres the user could not see through. Runtime and PlayMode test
assemblies compile with no new warnings. No player build and no hand pass:
whether the streaks READ against the PS1 composite from the fixed balcony
shot is the open question, and the user's eye is the instrument.

## 2026-08-29 — The city decree: it never stops raining there

The user decreed permanent rain - varying intensity, CITY SCENE ONLY. The
first instinct (raise `ClearIntensity` in `GameWeatherRules`) was mapped and
rejected: the schedule is shared, and a global floor leaks uphill through the
shapers - the mountain's one genuinely dry moment (a tunnel-mouth arrival in
a Clear slot, snow exactly `0` at climb `0`) would silently vanish, and two
schedule tests plus a mountain weather test pin exactly that dryness.

So the decree lives where the house already puts area weather: an
`ICityWeatherShaper`. The mountain snows through one, the village flurries
through one, and the city now rains through `CityEternalRainShaper`:
precipitation floored at `DrizzleIntensity 0.18` (visible rain, clearly under
LightRain's `0.45`, over the wipers' `0.02`), wind passed through untouched -
drizzle in near-calm air is a real state of the sky - and the Kind carried
through by the shapers' shared doctrine: it names the slot the whole world is
in, and `Clear` over a wet street means what it means on the summit, where
`Clear` snows. Wired at the controller plus the four direct schedule readers
(city rain-field init, river build-time water hook, bus wiper intensity, and
all three reads of the home balcony - the balcony looks at the CITY, and the
two views of one sky must agree).

Consequences accepted and written into canon: the shared wet film never reads
drier than the drizzle in city scenes, puddles never dissolve (the drying
machinery stays for the slope between heavy and drizzle), the rain bed is a
permanent part of the city's sound, and the bus wipes forever. Lightning
stays storm-slot-gated - events, not fixtures. Flagged, not changed: three
tableaux now play under eternal drizzle (the drying-yard babushkas with their
carpets, the courtyard laundry lines, the park chess players) - no NPC reads
weather anywhere, this was already true in every rain slot, and re-staging
them is a canon decision for another day.

Verified: `65/65` EditMode across `CityEternalRain` (new: the floor, the
untouched wind, and end-to-end through the controller - the street film can
never read drier than the drizzle), `GameWeatherRules`,
`CityWeatherControllerFogShelter`, `MountainRoadRideWeather`,
`CityWetSurface` and `CityBusRuntime` - zero existing weather tests
re-anchored, because the schedule itself is untouched. Not run: PlayMode.

## 2026-08-29 — Alpine Village now lives inside a permanent hard gale

The player explicitly asked for very strong snow and very strong wind in the
Alpine Village. That contradicted the shipped canon rather than merely an old
tune: story level `0` said «Ясно и тепло», both village bibles banned a
snowstorm, and the implementation deliberately limited snow to `.34–.62` while
damping wind to `.08–.40`. The explicit request is now recorded in the §6
registry and architecture notes. What changed is the weather, not the place's
meaning: warm light, the single uphill axis, the top house and the independent
warmth grade remain; lightning, thunder, danger framing and whiteout remain
absent.

`AlpineVillageWeatherShaper` now holds snowfall at `.88–1` and wind at `.82–1`
while preserving the shared schedule's bearing and short gust rhythm. A
village-only `Blizzard` precipitation profile supplies dense, restrained-alpha
stretched flakes without changing Mountain Road snow. The new deterministic
`AlpineVillageStormField` adds fast low spindrift at the terrain sampler's real
height, compensates vertical transport for the slope and drives the shared
synthesized wind bed from that same gale. The station canopy rejects and culls
ground strips in its dry core, the main layer switches to its shelter shape,
and both layers stay out of the moving cabin. Ground-field prewarm now carries
fractional emission instead of rounding sheltered startup upward. Finally, the
main precipitation field also culls its already-live dry cylinder every
sheltered frame, so wind cannot carry old flakes back through a roof.
`CityWeatherController` feeds persistent street wetness from the unshaped city
slot, so the village's permanent `.88+` visual snow cannot return to a clear
City as almost-full rain wetness.

The final environment audit found that snow and sound carried the gale but the
nine baked garland spans did not. `AlpineVillageGarlandWind` now deforms each
unique cord and bulb mesh with zero travel at both attachments and a bounded
`0.33 m` maximum in the free middle. It reads
`CityWeatherController.CurrentWind` after village shaping rather than creating
a second wind writer; the semantic midpoint and each of the two real lights
follow the same offset. Garland colliders and Blender geometry are untouched.

Fast verification: the focused `AlpineVillageStorm` EditMode category passed
`5/5` in `0.241 s` (`Logs/AlpineVillageStormFinal8.xml`), covering both weather
bands, coherent spindrift and fixed-anchor garland motion, readable generated-
mesh ownership, the live-particle dry core and the shared wetness regression.
The explicit Alpine Village PlayMode capture passed
`1/1` in `4.85 s`
(`Logs/AlpineVillageStormCapture.xml`). Its two delayed lower-axis frames plus
the ordinary-house and top-house views were inspected: dense diagonal flakes
and terrain-low crosswind strips change visibly between A/B, while the road,
closed house masses and warm destination remain readable. No broad Unity suite,
player build or startup smoke was run.

## 2026-08-29 — The §20 law reached the fixtures: everything burns at noon

The player asked whether every lamp now burns around the clock. It did not:
commit 0e389a8 wrote the law into the story bible and said plainly that the
fixture-side implementation was deferred - "it crosses four lighting systems
and eight art-bible entries". A full audit confirmed the gap fixture by
fixture: at `NightFactor = 0` (exactly, for the whole 07:00-18:00 plateau)
the street-lamp bulbs rendered pure black, the pooled realtime lights and bar
lights switched off, every glow-registry emissive fell to the 10% "dead tube",
lit windows fell to unlit glazing, the cemetery alley lamps and both POI
floodlights disabled outright, the vista's distant city windows disabled
their renderer - and even the fixtures that did burn floored at 22-35% of
night strength against a mandated two thirds.

**One number, one function.** `GameTimeDayNightRules.DayFixtureFloor = 2/3`
and `FixtureFactor(nightFactor) = Lerp(floor, 1, nf)`. The raw `NightFactor`
stays the SKY's - the sun, ambient and reflections still go all the way down
at noon; only what a FIXTURE multiplies by changed. Consumers converted:
bulbs, windows, glow registry (the dead-tube constant now IS the law's
floor), site registry (the floor overrides authored day intensities from
below - the ferryman lamp's 33%, the porch's 23%, the cave's 22% all rise to
two thirds; nothing registered is ever disabled; halos never hide), the
pooled atmosphere including its lease scan (the "zero-factor pool does not
scan lamp anchors" optimisation was fair while lamps died at dawn, and died
with them), the vista's city windows (the very city the law is about read
DEAD from the brink at any factor under 0.18), the bus cabin plafond (the
plafond that moves; headlights stay events), and the summit yard lamp
(boost `0.55 → 0.5`, day/night exactly `2/3`).

**Two real bugs surfaced by the change's own tests.** The bus presentation's
`SetNightFactor` equality gate survived the pool: a husk parks with lights
hard-off and factor zero, a respawn at constant noon hands it the same zero,
and the refresh never ran - the plafond stayed dark until the next dusk edge.
`hasAppliedNightFactor` (the city registry's own idiom) clears in
`ResetForPool`. And the law test caught a raw `CityLightHalo` lying about
visibility when never initialized - test-side, initialize it properly.

Canon: the eight art-bible entries that still ordered lamps dark by day are
rewritten (drying-yard and island floodlights, island prose, upper promenade
lamps, cemetery lamps + its Нельзя + its Проверка, the §2.3 cycle line), and
the lodge bulb's "the one lamp that never goes out" uniqueness claim became
"the first of all of them". Architecture notes carry the accepted entry.

Verified: `73/73` EditMode across `AlwaysLitLaw`, `CityNightAtmosphere`,
`CityNightGlowRegistry`, `CityWindowAppearance`, `CityCemeteryPlanner`,
`CityPointOfInterestSurfaceAppearance`, `CityBusRuntime`, `MountainRoadVista`
and `MountainRoadSummitLighting` - five of those suites re-anchored from
pinning darkness to pinning the floor, each with a comment naming the law.
The PlayMode noon assertions (bulb black, halos zero) were re-anchored in
`CityNightPresentationPlayModeTests` but not run - a full city PlayMode boot
is minutes for two inverted asserts; flagged for the next PlayMode pass. Out
of scope by canon: interiors (their own controllers), the village (its own
stronger rule, already compliant - `NightFactor` does not occur in its
builder), and the excluded events.

## 2026-08-29 — The Alpine Village houses got their walls back

The player reported that every village building still had no walls and asked
for the houses to be assembled in Blender. The missing mass was not a plot or
provider omission: all four ordinary-house variants, the top house and the
chapel already carried a `Walls` role, and `AlpineVillageWorldBuilder` created
its renderer. The shared `prism_y` authoring primitive wound every cap and side
inward. Blender's two-sided preview hid it; Unity's ordinary back-face culling
correctly removed the complete shell. The same primitive also inverted the
prismatic portions of roofs, grave markers, shutters and repairs.

`build-village-3d-model.py` v2.1.1 now emits outward caps and sides. Every
closed `PartSpec` must have finite positive signed volume, and preview materials
enable back-face culling, so neither the deterministic validator nor the
contact sheet can conceal this class of defect again. The compatible
`village_wave2_v2` catalog was rebuilt without changing its semantic shape:
`19` assemblies, `53` meshes, `3,624` triangles, signature
`24ca0fc6fb9c310d56183a03b147a3da2c3898c47a3fde5b92930eb3a865bb57`.
All `53` regenerated FBX meshes return positive signed volume after a clean
Blender re-import.

Unity adds the other half of the guard at its real import boundary:
`VillageAssetSetup` compares every imported mesh's signed-volume direction to
the known-good box-authored house plinth before binding, and a focused
`VillageAssetTests` regression pins the House/Chapel/TopHouse wall and roof
roles. The provider was rebound to the new signature. Four plan-derived
PlayMode frames now cover the lower uphill axis, an ordinary front/three-quarter
view, its uninterrupted side wall and the top house. They show closed masses,
grounded plinths, complete roofs and doors/windows resting on visible walls.
Doors and panes remain real-metre runtime attachments and collision remains
plan-owned; no material family, story content or interaction changed.

Fast verification: Blender direct validation passed `19 / 53 / 3,624` with
repeated signatures; Unity provider bind printed
`VILLAGE UNITY ASSET BUILD OK`; explicit PlayMode
`AreaCaptureFixture.AlpineVillage` passed `1/1` in `3.864 s`, and all four
frames were inspected. No broad Unity suite, player build or startup smoke was
run.

## 2026-08-29 — The car stops driving through the bus, and the street cannot deadlock

The player watched the Ferryman's car drive STRAIGHT THROUGH a city bus, and
asked for the car to ease off for a bus or a walker ahead - with the explicit
worry that the car and the bus must never end up braked for each other at a
junction, soft-locked.

**The mechanism, verified.** The car's only yield was the one authored
give-way at the forecourt turn; a road may declare at most one give-way point
and the component goes inert after one commit. Outside that turn nothing ever
calls `SetHold`, and the obstacle collider is off for the whole drive - the
only thing that can stop a driving car is the hold. Meanwhile Route 01's one
bus dwells `10 s` IN the car's lane - both planners lay their lane `1.5 m`
right of the same crowns, so shared streets have `0.00 m` of lateral
separation - and the car closes on it at `8.2 − 6.0 = 2.2 m/s`. The bus's own
obstacle scan checks the hero and walkers only; the car does not exist for it.

**The fix extends the machinery the car already had, with one writer.**
`LastRouteCarTrafficYieldModel` is the give-way model's sibling - pure,
hand-stepped, the same three rules inherited whole: a driver who cannot stop
does not try (a conflict nearer than braking distance is driven through,
because stopping INSIDE the junction parks the car in the one lane the bus
will never yield in); he wants to see it clear for `0.4 s`, not a frame (the
bus is sensed off its instantaneous heading, which flickers through turns);
and he never waits forever - `18 s` at a standstill (past the longest lawful
`10+5 s` dwell), then he goes, logs `car_traffic_waited_out`, and may not
re-arm on the same still-standing conflict, or he stutters into it forever.
The sensing is two pure statics on `LastRouteCarGiveWay`
(`FindBusPathConflict`, `FindWalkerPathConflict`): the road walked as `2 m`
chords out to a stopping-distance horizon, the bus as tail-to-predicted-nose
segment against the `2.7 m` corridor (opposite lanes run at `3.0 m` - lawful
oncoming traffic never triggers; deliberately NO same-way exclusion, because
on his own road the same-way bus ahead IS the collision partner), walkers at
`1.2 m` plus their radius (tighter than the crossing's `1.35`: a bus-stop
walker stands `1.74 m` off the lane line, and looser reads the whole pavement
as jaywalkers). The give-way component is the single hold writer: crossing
decision and traffic rule are both advanced each frame and the model's one
slot takes the min - two writers calling `SetHold` are last-writer-wins.

**Deadlock is impossible by construction, and the arithmetic is pinned.** The
bus was deliberately NOT taught to see the car - that symmetric rule is
exactly what makes two vehicles stand braked for each other. The wait graph is
acyclic: car yields to bus and walkers; bus yields to walkers and the HERO.
The hero rides the car, so the one cycle candidate is a held car parking its
hero inside the bus's own `1.71 m` yield corridor -
`TrafficYield_HoldGeometryKeepsTheWaitGraphAcyclic` pins
`2.7 + 6 − 2 (worst overshoot) > 1.71 + 2.415 (hero behind nose)`. And if the
unforeseen happens anyway, the `18 s` cap is the last unlock: the one ride out
of the city cannot soft-lock, the give-way's own decree.

**The test found a real bug before the player did.** The first probe walked
its grid FROM THE CAR, so the grid - and the conflict, and the hold - crept
forward with the car, and the car chased its own quantisation toward the bus
at half a metre a second instead of stopping.
`TrafficYield_StandsBehindADwellingBusAndFollowsItOut` watched it crawl the
whole follow gap. The grid is now anchored to the road
(`floor(distance / step) * step`).

Verified: `61/61` EditMode (`LastRouteCarDrive` + `LastRouteRide` +
`LastRouteCarPlacement`) - seven new tests: stands behind a dwelling bus and
follows it out, ignores the lawful oncoming lane, holds short of a junction
sweep, never parks inside the junction, gives up past the longest lawful
dwell without re-arming, holds for the jaywalker and ignores the pavement,
and the acyclicity arithmetic. Two aborted Unity runs on the way were the
parallel session's half-written test file and a transient project lock - both
waited out, not touched. Not run: PlayMode (the wiring reuses the give-way's
attach path, which was already live); the mountain leg costs nothing - no
directors are attached there.

## 2026-08-29 — The Ferryman turns his wheel, on the bus driver's own mechanism

The user asked for the Ferryman to steer while driving, by analogy with the bus
driver. Everything the analogy needs already existed and was idle: the car's
driver computes the exact signal the bus uses (heading error to a tangent
`4.5 m` ahead, clamped `±33°`, smoothed at `7/s`) and already yaws the front
wheels with it; the car model ships a rigged rim (`INT_SteeringWheel` under
`PIVOT_SteeringWheel`) with grip anchors ON the rim as children of the pivot —
the asset setup even validates that parentage "or a later turn of the wheel
strands the driver's hands in mid-air" — and the ferryman's drive clip is an
authored two-hands-on-the-rim pose. Nothing rotated the pivot and nothing read
the grips.

Four moves:

- **The rim rolls in `LastRouteCarDriver.ApplyWheels`** — same component, same
  frame, same smoothed `steeringDegrees` as the front wheels, so the two can
  never disagree mid-corner. Ratio `3.0` (NOT the bus's `3.55`: `33 × 3.55 =
  117°` against a `100°` cap would pin the rim at the cap through every corner;
  `33 × 3.0 = 99°` restores the bus's own rail-to-rail-at-full-lock pairing).
  `Halt()` straightens the wheel — `Update` stops on arrival, and the alighting
  clip starts from hands drawn on an unturned rim. The axis is MEASURED, not
  bound: the normal of the plane the two grips lie on, pointed at the driver's
  seat — and the bus's negation is deliberately NOT copied, because the bus's
  column points at the windshield while this raked column points back at the
  driver; the same negation here would have reproduced the bus's old
  rolled-left-on-a-right-turn bug in mirror.
- **The arm bones crossed the prefab build.** The shared pedestrian registry
  serializes no arm bones and nothing is found by name at runtime, so
  `LastRouteFerrymanRigAnchors` (the fifth-clip precedent — "the component
  that is already his alone") now carries `upper_arm/forearm/hand/SOCKET_Grip
  .L/.R`, bound by name in `CityPedestrianAssetSetup`'s ferryman branch and
  validated socket-rides-its-own-hand. Prefab rebuilt headless.
- **The solver became shared.** The bus's two-bone CCD (`SolveTwoBone`, bend
  hint, hard wrist write) moved verbatim from private statics in
  `CityBusDriverPresentation` to `SeatedArmIk`, with the attachment/target
  structs; both cabs now read one mechanism. One real tuning: the post-hint
  recovery was two iterations, and at the car's full lock — grips carried
  `99°` from where the drive pose drew the hands — that left a palm hovering
  `2.2 cm` off a `2 cm` contract. Four converges it; on the bus's smaller
  angles the extra passes land on the same pose (re-verified, 29/29).
- **The hands close per frame in the ferryman's `LateUpdate`,** strictly AFTER
  `graph.Evaluate` (the graph rewrites every bone each evaluate), targets
  lerped from the clip's own socket pose to the live grips by a `0.35 s`
  eased weight that engages only in the Driving phase — so the boarding and
  alighting seams, both authored against the drive base pose, stay untouched,
  and the parked-seat `0.02 m` assertions never see a moved pelvis. Both drive
  legs steer for free: city and mountain run the same driver + presentation.

**Nothing existing pinned any of this** — the LastRoute suite had zero steering
assertions, so a wheel wired backwards would have shipped green. Two mirrors of
the bus's own precedent tests now do: EditMode
`Steering_RollsTheRimWithTheFrontWheelsForTheDriver` (ratio, rest-restore on
`Halt`, and the SIGN — the axis of the measured rim delta must point at the
driver's seat, clockwise under his hands on a right steer) and PlayMode
`Driving_HisHandsRideTheTurningRim` (a bent road saturates the clamp; on 30+
frames with the rim past `30°` both palms stay within the bus's `0.02 m` grip
error, under `AlwaysAnimate` — batch mode otherwise reads back a bind pose and
proves nothing). My own first draft of the EditMode test fell into the
documented imported-node-basis trap — read zero yaw off a correctly steered
wheel by measuring the node's `forward` — and was rewritten as a parent-space
delta, which is now noted in the test itself.

Verified: EditMode 93/93 (LastRouteCarDrive/Placement/Ferryman*/Ride +
CityBusAssetImport on the shared solver), PlayMode 10/10
(LastRouteFerrymanPlayModeTests), bus re-verified after the solver tuning
29/29. Not run: broader suites; the give-way pre-turn (wheels turning while
held at a stop line, look-ahead reaching into the corner) is existing front
wheel behavior the rim now mirrors — deliberate, visible from the passenger
seat, and worth a look in-game.

## 2026-08-29 — The Nightlife gap became a full-depth inhabited arch

The City art/story bibles were read before implementation. The fixed walkable
gap between presentation cells `[10;5]` and `[11;5]` remains an ordinary
pre-epidemic survival pocket: no quest, dialogue, reaction, collectible,
spectacle of poverty or story-state change was added. The user's explicit
request for strong causal illumination is recorded as one bounded architecture
exception to the draft's temporary emissive-only rule.

`CityArchShelterPlanner` now derives the structure from the two actual building
bounds. The closed, non-walkable service bridge and its rain volume cover the
whole `11.602 m` common side-facade depth while the inset passage keeps one
continuous `2.2 m` lower-ground route. The local southern tableau stays
separate from the
northern ten-step flight. The first capture exposed that the stair landing,
barrel, people and bedding were merely placed at the numeric `1.562 m` upper
datum: the landing was too short and the rest had no rendered or physical
support. The first correction derived one `4.00 x 6.10 m` platform from the
last tread seam, but the next gameplay screenshot showed that it still read as
an arbitrary central plinth: it stopped `3.30614 m` short of the east wall
and `2.751 m` short of the south facade end. The final
`7.30614 x 8.851 m` service terrace begins at the same last-tread seam, keys
into the east support's exact inner face and reaches the raw south wall end.
Its worn surface contains the clear `1.50 m` landing, barrel, both warmers,
bedding and sleeper, while one massive masonry support and one collider reach
the lower datum. A terrain audit rejected the tempting claim that the east
side remained a second route: sampled ground at the terrace ends is
`0.41-0.51 m` lower than the slab, above the player's `0.28 m` step offset.
Full-width `1.09 m` physical north/south guards plus a west guard over the
`7.251 m` segment south of the stair therefore make this a stair-only service
pocket; the exact `1.60 m` stair band is its only opening and the west `2.2 m`
route alone remains continuous on lower ground. The east facade collider was
expanded to the visual wall's inner face, closing a final `0.206 m` physical
seam. Loose clutter stays below. Plan validation re-derives the wall seams,
three guards and these footprints from the layout, proves all five staged
subjects are supported, and makes the renderer, colliders, overhead obstacle
and rain trigger agree with the authored mass.

The first strict materialization check then exposed a second coordinate bug:
the facade-centred structure root is `0.48614 m` east of the terrain's shared
boundary, so locally correct platform meshes still materialized east of their
plan and collider. The generator now names that offset explicitly and applies
it once to the complete ten-step/landing/platform/rail group. The authored
flight was also brought from eight visual treads to the plan's ten `0.322 m`
treads. A final renderer-bounds audit found the imported mattress and sleeper
overhanging the east edge; their shared root moved `0.25 m` inward and the
seated warmer `0.05 m` inward. The world regression now proves the actual
imported bedding and all three rendered residents, not only their abstract
anchors, remain over the slab. Synthetic reversed-datum layouts are rejected:
the one production default layout and one authored shell are east-rising.

CityMisc v4.6.0 rebuilt the arch shell, supported platform and fire as imported passive
geometry. The citywide catalog remains `80` semantic kinds / `115` assemblies
but grows to `238` role meshes and `42,878` triangles, signature
`87680a5d05066a52504900a19b0e4ec19955fbe180fc6cc8d60f6e5995e412ad`.
The barrel has four independently deforming flame shapes, an ember bed, an
irregular transparent spill and deterministic sparse sparks. One always-on
warm shadowed Point Light (`95` base intensity, `7.0 m` range) follows the
same bounded multi-frequency flicker, so the walls, ground, bedding and three
figures receive a moving causal pool. The flame parts cast no shadows; no
particle light, smoke, local weather or exposure override was introduced.
Capture feedback reduced the first over-bright pass without touching global
exposure: the ordinary flicker now stays near `0.82-1.16`, the halo/spill are
subordinate, and the unlit ends of the shelter remain dark.
The existing deterministic crackle, two warmer shifts and sleeper breathing
remain local presentation details.

Fast verification: Blender validate-only passed the final `115 / 238 / 42,878`
catalog and Unity rebound all provider entries. Focused EditMode
`CityArchShelter` passed `8/8` in `0.825 s`. Explicit PlayMode capture
`AreaCaptureFixture.CityArchShelter` passed `1/1` in `6.560 s`; all five final
frames were inspected for the full exterior mass, continuous ten-step entry,
wall attachment, visible full-height support under every staged resident and
prop, the open lower-ground route, and the balanced warm light. Neither final
log contains the obsolete
URP-incompatible `Light.shadowResolution` warning; shadow tiering now uses only
`UniversalAdditionalLightData`. No broad Unity suite, player build or startup
smoke was run.

## 2026-08-29 — Mountain Road became a staged ascent instead of an even corridor

The art and story bibles were re-read before implementation. The pass stays
inside their existing Mountain Road contract: the terminal is still a working
transfer yard, the brink remains its single measured view opening, and no
tourism, second route, accident, stop, new light, sound or in-fiction line was
added.

`MountainRoadCompositionRules` now owns the climb's negative space. All forest
budgets remain intact, but three selected hairpin centres yield locally across
the physical, middle and far crown layers; near/middle trees also stand back
from the bridge and final approach while the surrounding far stand and ridges
keep the horizon closed. The renderer now consumes the three palette indices
the planner already supplied as three low-poly crown silhouettes instead of
drawing all `420` trees alike. Boulders, logs, stumps and dead trees retain
their counts and stable IDs but gather into five unequal roadside chapters
with deliberate pauses at the structural beats. Their bounded deterministic
resolver rejects every candidate whose oriented footprint comes within
`0.35 m` of accepted natural debris or authored roadside furniture. Dead-tree
footprints are kind-aware: their imported mesh and branch colliders scale by
height, so the shared planner/validator envelope is `0.19 x height`, and those
largest silhouettes pack first. Later random candidates can no longer form
interpenetrating piles.

The same pass fixed three concrete composition defects. Hairpin guardrails now
follow each bend's outside rather than always choosing the same world side;
the abandoned chair moved out of the bridge/gorge fold onto a late upper shelf;
and both road kerb strips were rewound toward the verge so the opaque material
no longer culls their visible faces. Planner validation holds the forest
openings, all three crown variants, rail side/clearance and chair grounding.
The capture fixture now follows ten plan-derived beats from tunnel threshold to
the one terminal brink instead of placing generic cameras behind the tunnel
cap and inside terrain.

Fast verification: explicit PlayMode capture
`AreaCaptureFixture.MountainRoad` passed `1/1` in `9.86 s`; all ten resulting
frames were inspected, including both bridge views, the lower and snow
hairpins, terminal approach/yard and the single brink opening. The focused
EditMode contract `DefaultPlan_BuildsAbsurdHighTenHairpinBridgeWorld` then
passed `1/1` in `13.87 s`, including crown envelopes, misc clearances, rails
and kerb normals. No broad Unity suite or player build was run.

## 2026-08-29 — Alpine Village became one authored climb instead of a dressed strip

The art/story bibles were read before implementation and the work stayed inside
their existing village registry: no mother, dinner, chapel interior, cult,
tourist image, panorama, storm or new in-fiction line was added. The target was
the missing form underneath the already-approved place.

`TerrainBounds` now means the inhabited inner bowl and `TerrainMeshBounds` the
full physical ground. The latter samples the enclosing `49°` rise, hidden crest
and a cableway brink/cut with grounded support shelves, so the planned ridge is
actually rendered and colliding instead of beginning just outside the old mesh.
The walkable mask and a new visible path plan consume the same station exit,
household thresholds, landmark dog-legs and chapel-water approach. House frontage
uses authored distance/side/yaw beats rather than alternating equal cells. Rotated
footprints have conservative world bounds, exact SAT overlap validation and a
deterministic depth solve around three authored `7.2 / 7.2 / 7.5 m` rear-row
beats; the symmetric local correction prevents the old greedy cascade from
pushing each later house beyond an already deep neighbour. Lane clearance
measures the rotated physical extent instead of the door midpoint and keeps a
separate `0.55 m` solver floor above the hard traversal guard.
The path plan now carries its plot owner and validates the larger of its visible
and walkable widths as an exact segment capsule against every rotated footprint.
That integration check exposed a direct adit dog-leg through house 08, a rare
cemetery contact and a chapel-source turn through its own wall. The adit now
uses an authored outer hook beyond the rear row and selects its shortest clear
turn from house 08's seeded expanded OBB; the cemetery takes the clear direct
worn line and the water turn keeps its whole envelope behind the chapel.

Village Blender wave 2.1 expanded the passive kit from `11 / 27` to `19 / 53`
assemblies/role meshes (`3,624` triangles): roof snow, a distinct top house,
three facade-detail variants, garland post, cable gate, rail bridge and plain
stone catch basin. The regenerated Blend/contact sheet/FBX/manifest share
signature `1521bc7417c4e5cca639170798cf24f0f423b3e1378c6ac14cfcde670afa06d3`;
Unity bound all 53 meshes into the Resources provider. The builder now groups
houses, connects irregular garland beats to eaves or visible brackets, uses only
two cord lights plus three snow-pool spots, scatters ordinary grave markers in
loose bands, and gives the gate/basin/bridge plan-owned collision matching their
visible heights. Mine cable, rail and firewood remain everyday reuse, not an
industrial tableau.
Runtime construction now loads that provider once and fails before building if
any of the exact 53 kind/variant/role tuples is absent; individual assemblies no
longer fall back to primitives. The editor binder also rejects a manifest whose
generator version differs from the runtime catalog.

Six bounded synthesized spatial voices were added with no imported recording or
text: return-station metal, one authored wire, dog behind the visible cable gate,
water at the catch basin, firewood in the mine cart and a very quiet wordless hum
behind a house wall. Shared dressing anchors keep form independent of audio while
audio reads exact visible owners: the station voice sits on the return bullwheel
and the wire voice on its span midpoint rather than on a batching pivot.
`WarmthGrade` remains `0`, but its existing
per-minute apply now drives isolated garland loss, deterministic window darkness,
roof/ground snow dirt, all five village practicals and the six sound gains/cutoffs
together, satisfying the one-parameter §10g contract before any prologue drives it.

Fast verification: Blender validate/full generation and contact-sheet inspection
passed in the art task. The first Unity bind exposed one missing `using System`
after licensing recovered from its stale-client `505`; the concrete rerun compiled
scripts, imported FBX/manifest, rebound the provider and logged
`VILLAGE UNITY ASSET BUILD OK`, return code `0`. A final focused
`dotnet build BarPromenade.Runtime.csproj` passed with zero errors and 37 existing
Unity-serialized-field warnings. After the integration fixes, a direct focused
Roslyn compile of the complete runtime response plus `AlpineVillageTests`,
`VillageAssetTests`, `AlpineVillagePathTests` and
`AlpineVillageSoundscapeTests` passed, as did the complete editor response that
contains `VillageAssetSetup`. An independent mirror sweep of the exact
hash/lane/SAT formulas covered all `200,001` seeds from `-100000` through
`100000`: no solver exhaustion, plot/spur overlap or lane-clearance failure;
the largest local correction was `1.2 m`. A second exact sweep over the same
`200,001` seeds resolved the full path plan with zero route or final-envelope
failures. Every adit route used the intended single OBB corner; the tightest
foreign-plot margin was still `0.080416 m` (seed `3677`, adit versus house 08).
`git diff --check` passed. Full
EditMode/PlayMode suites and a player build were intentionally not run in fast
mode.

## 2026-08-29 — The way back down, and two frames that were 1.9 metres apart

The ascent was repaired yesterday; this is the descent. A parallel session had
already cut the brink the village needed - a corridor that falls under the rope,
carries every pylon and closes again before the hidden turn - so what was left
was to make it actually hold, and it did not.

**The test that was missing.** The mountain road has
`CablewayCabinBody_ClearsSampledTerrainOnBothTracks`; the village had nothing of
the kind, which is how a line authored as a mirror of the mountain's climb shipped
diving into its own hillside. `CablewayDescent_FliesWhileVisibleAndEndsInsideTheMountain`
is the village's version and it is deliberately two-sided: the cabin must be in the
air everywhere the player can still see it, AND the mountain must have closed over
the rope by the metre the cut lands on. Clearance alone would pass a line that ends
in open air; closure alone would pass a line buried the whole way. It failed on
first run by `28 m`, and then earned its keep three more times.

Four causes, each of which breaks the ride on its own:

- **Two frames, `1.9 m` apart.** `SampleCablewayBrink` measures `along` from the
  station PAD's centre, but every distance it compares against - node distances,
  the last support, `UpperOccluderNearFaceDistance` - is measured along the CABLE,
  which starts `1.9 m` further forward. So the whole descent profile was read
  early: each pylon's shelf sat short of its own legs, and the ground closed back
  over the rope `1.9 m` before the blackout completes - the same "riding inside the
  mountain" the road had just been fixed for. One conversion, `alongCable`, fixes
  both; it is also the exact cause of the sibling session's own red test, which
  wanted `82.330` at a support and got `81.750` - the `0.58 m` the profile had
  drifted.
- **`39` degrees out of a station.** The drops were `{0, -13, -18.5, -22, -24}`:
  steepest in the first span and flattening downhill, which is backwards, and at
  that grade the cabin's underside - it hangs `3.13 m` under the rope - was below
  the boarding platform one metre off the pad. No terrain cut can rescue that
  without cutting the pad the hero stands on. Now `{0, -2, -11, -18.5, -24}`: the
  same `24 m` of fall, spread the way a mountain line actually falls, gentle out of
  the terrace and steepening down the face.
- **The cut descended out of the pad's height.** The station node's planned ground
  is the level pad, `0.8 m` above the clearance every other node keeps under the
  rope, and interpolating the cut bed out of it dragged the whole first span up:
  `0.86 m` of air under the cabin in the middle of it. `NodeCutGround` clamps that
  one node to the same rule as the others. The pad is not at risk - the apron and
  the entrance ramp hold the real ground level over it; this is only the profile
  they ramp towards.
- **The entrance ramp was slower than the fall.** `6 m` of ramp while the rope
  starts dropping at the pad's own edge left the underside in the hillside for the
  first few metres. `3.5 m` now.

**And the way back is armed.** `CreateForArrival` set neither `departureLineLength`
nor `departureFadeLeadMeters`, so `FadeTriggerDistance` fell to its `1 m` floor and
the descent cut to black almost as soon as it moved. Yesterday that was deliberately
left alone with a comment saying so, because it was the only thing hiding all of the
above. The ground is honest now, so both are set in `AwaitArrivalStart`, where this
station's own plan first exists. The descent flies the same derived rule the ascent
does: black at `LastVisibleDistance`, with the mountain already closed in front.

Verified: `53` of `54` across `AlpineVillage`, `MountainCablewayRide`,
`MountainCableway`, `MountainRoadTerminal`, `MountainRoadTerminalSite`,
`MountainRoad`, `MountainRoadBrink` and `CityWeatherControllerFogShelter`. The one
red is the sibling session's `TerrainMesh_BuildsTheRidgeAndTheCablewayBrink`,
which now misses by `0.18 m` of mesh-versus-sampler interpolation at a pylon
rather than the `0.58 m` of real profile drift it started at; widening the pylon
shelf to close it eats the ramp the mountain closes on, so it is theirs to finish
and was left alone. Not run: PlayMode. The walkable mask, the spawn and the
station's own shelf are untouched by any of this - the cut begins a terrain cell
beyond the apron, exactly as it did before.

## 2026-08-28 — The cabin drove into the mountain, and the fade test watched it happen

The player reported the cabin passing THROUGH rock at the end of the cableway
ride, right before the load, and guessed the line needed raising. It did not.
The rock is `far-snow-cableway-occluder`, and it is planted ON the line on
purpose: `1.8 m` short of the cable end, `10 m` thick along it, so its near
face crosses the track at `d = 51.2` and the last `6.8 m` of visible line — the
whole upper turn — is inside solid geometry by design. The far-turn root is
literally called "Upper Return Hidden Behind Snow Ridge". Raising the cable
would have flown the cabin over a crest into open sky, which is the one cut the
ride's own comment forbids and the postcard §10f bans.

What was wrong was WHEN the screen goes out. `FadeLeadMeters = 5.5` was chosen
by eye, apart from the rock's numbers. Measured in the engine: the cabin's nose
enters the rock at `d = 50.5` and the passenger's eye at `51.3`, while the
blackout did not START until `52.5` and was not complete until `55.165`. So
`3.9 m` — nearly two seconds — of first-person ride happened inside the
mountain, and because a ridge is single-sided it has no back face, so from in
there he was looking at the world straight through the rock.

Three things had to change together:

- **The number stopped being a number.** `UpperOccluderSetback` and
  `UpperOccluderDepth` moved onto `MountainRoadCablewayPlan`, the planner
  builds the ridge from them, and `EvaluateFadeLeadMeters` derives the lead
  from `LastVisibleDistance` — the near face, less the cabin's own leading
  edge, less a metre of air. It comes out at `11.302`, black at `d = 49.363`
  with exactly `1 m` still clear. The leading edge is the ROOF LIP, not the
  front wall: the slab is built at `CabinSize.z * 1.08` and oversails the body
  by `6 cm` a side, so measuring the body spends `6 cm` of a clearance that
  claims to be a metre. `CabinRoofOverhang` is now the plan's and the world
  builder consumes it.
- **The last tower moved `50 → 44`.** At `50` it stood `1.2 m` off a cliff -
  nobody erects a pylon there - and it left no interval at all in which both
  authored rules could hold: the cut lands after the last tower, AND before the
  rock. That contradiction is exactly why the geometry lost the argument in
  silence. At `44` the final span is `14 m`, the same order as the others.
- **One shape, three readers.** `MountainRoadRidgeGeometry` now owns the
  polygonal crest that the scenery factory draws, and the planner and the
  terminal validator both measure the cable against it. The old check read the
  top of the ridge's bounding BOX, which stands `4 m` above the rock that is
  actually built - a ridge whose crest missed the cable entirely would have
  passed. Fixing only the validator would have turned that hole into a crash:
  the crest carries a seeded variation in eight steps, and on the residue
  `(seed + 4099) & 7 == 1` the drawn snow stands `0.34 m` UNDER the cable. Both
  shipped seeds miss it. The planner now sizes from `CrestFactor` - `0.41 m`
  taller on that one seed, unchanged on the other seven - and a seed sweep
  walks all eight.

This last one is not mine. I had shipped the validator arm alone, and a
verification pass swept the eight residues, reproduced the probe's own crest
dump to three decimals, and found the seed that throws. The same pass caught
the roof lip. Both were real and both are in.

**The suite had a fade test and it was green the whole time.**
`RideFade_HappensInTheFinalSpanBehindTheRidge` asserts only along-line
distances and the lead: no Y term, no reference to `plan.Ridges`. It would stay
green under any raise of the cable and it stayed green while the cabin drove
into a wall in front of it. `RideFade_IsCompleteBeforeTheCabinReachesTheRock`
now measures the thing the player sees - the near face, the nose of his own
cabin, and how much dissolve is left when they meet - on both tracks.

Verified by two instruments. A throwaway EditMode probe walked both lines and
reported the crossing against a replica of the built crest (roof lip at
`50.363`, eye at `51.3`; black at `55.165` before the fix and `49.363` after).
Then `52` tests across `MountainCablewayRide`, `MountainCableway`,
`MountainRoadTerminal`, `MountainRoadTerminalSite`, `MountainRoad`,
`MountainRoadBrink`, `MountainRoadSummitLighting` and `AlpineVillage` passed,
with all three fade/crest tests present by name in the results XML. The probe
was deleted; its coverage lives in the tests. No PlayMode run: nothing here
touches navigation, the walkable mask or a scene asset.

**Superseded the same day — the descent was fixed too; see the entry above.**
What follows is what was true when this entry was written.

**Found and NOT fixed - the descent is broken the other way.** The village end
of the same line is authored as a mirror of this one, with hard-coded drops
`{0, -13, -18.5, -22, -24}` that were never checked against the village's own
ground. The village terrain patch reaches `33.6 m` along the line
(`TerrainBounds` minZ `167.95`, measured), and over that stretch the ground
falls about `3 m` while the cable falls `20`: the cabin is inside the village
hillside from roughly `d = 1` to `d = 33.6`, up to `16 m` deep, and then leaves
the patch and descends over nothing. Its named occluder,
`village-cableway-ridge-occluder`, is a string that no builder ever reads, so
that ride ends in open air; the village's own three ridge descriptors are read
by no builder either. The honest fix is a brink under the station - the ground
has to fall away, the way `MountainRoadTerrainSampler.ApplyBrinkFall` already
cuts one at the summit - and that reshapes authored village terrain, which is
bible-governed. Raised with the user rather than done unasked.

**And nobody has seen it, because of a second bug that is currently load-bearing.**
`CreateForArrival` sets neither `departureLineLength` nor the new
`departureFadeLeadMeters`, so the return leg's `FadeTriggerDistance` falls to
its `1 m` floor and the descent cuts to black almost as soon as it moves. That
reads exactly like an oversight to fix, and fixing it alone would turn a
one-metre cut into half a minute of riding through the inside of a mountain. It
now carries a comment saying so. The two must be repaired in that order: the
village's ground and its ridge first, the arming second.

## 2026-08-28 — The city's fog goes up the mountain, and the weather owner clears it

Both areas above the city had the Exp2 haze and nothing in the air. Distance
fog alone fades a slope out; it never puts anything BETWEEN the hero and the
slope, which is why the mountain read as a fade and the village as a warm
gradient. The city has had the answer since the first night pass, and it is one
component: `CityFogField`, 36 world-space sheets following the player.

It is now built in `MountainRoadRoot.BuildAtmosphere` and
`AlpineVillageRoot.BuildAtmosphere` **verbatim** - same component, same shared
atmosphere material, same cap, same gradient, seeded off each area's plan seed.
No per-area tint was added and none is wanted: the art bible bans a zone's own
fog, and the particle shader already mixes the area's own Exp2 haze into every
sheet through `MixFog`, so the same white sheet comes out cold on the road and
warm in the village for free. The three hazes behind them are untouched:
`0.070` grey-green city, `0.026` mountain, `0.0145` warm village.

The shelter is the part with a decision in it. In the City the fog's shelter
belongs to `CityTunnelShelterController`, because entering the portal must also
hide the ridge shell - one event, two effects. Neither mountain area has that
controller or needs it, and both already own exactly one predicate for "is he
under something" (tunnel plus terminal; the station canopy) which
`CityWeatherController` is already polling every frame for the snow. So the
weather owner takes an optional `CityFogField` as its last argument and clears
and refills it in `UpdateShelter` alongside the rain. The City passes nothing
there and is byte-for-byte unchanged in behaviour.

Ordering matters and is easy to get wrong silently: the fog is built BEFORE
`Weather.Initialize`, because `Initialize` forces a weather apply and
`CityFogField.SetSheltered` returns early on a field that is not initialized
yet - a fog built after the controller would simply never clear.

Verified with one focused EditMode run,
`CityWeatherControllerFogShelterTests` (`2/2` passed, both names present in the
results XML): a sheltered predicate clears rain and fog together and stepping
back outside refills and replays the field, and a null fog leaves the rain's
shelter working. The Unity run compiled the whole tree, so both roots build. No
PlayMode suite, no player build and no capture: nothing here changes geometry,
navigation or a scene asset, and the appearance question is one the player
answers by standing on the mountain.

## 2026-08-28 — Four walls between the cabin and the village, and my test could not see any of them

The player reported he could not leave the upper station. I fixed the walkable
mask, shipped it, and he reported it again — **because the test I trusted walked
the MASK and he was walking into COLLIDERS.** The mask is a polygon and knows
nothing about furniture; the mountain's own site validator says that about
itself in a comment I had read. The instrument that settled it in one run is the
one the summit already had and the village did not:
`AlpineVillageStationExitPlayModeTests` builds the real world and walks a real
`CharacterController` at the foot of the lane, and its failure message names the
metre it stops at.

Four causes, each of which stops him on its own:

- **The fence.** The previous entry closed the drive terminal's centre gate and
  put the way through beside the strip - right for a terminal you ARRIVE at
  across a yard. The village is one you LEAVE, so the same fence stood across
  the whole exit with its gap at the far end: walking at the village he met the
  rails broadside, slid their length and wedged on the end post `5.94 m` short.
  Moving the gap inboard only walled off the steps, which are the only way down.
  A barrier is drive-terminal furniture - the village has no yard, no freight
  and no machinery, and the cabins carry no colliders - so the return terminal
  now has none, and the "boarding closed" sign goes with it, being the summit's
  sign about the summit's line.
- **The pad was a table in the air.** `CreateStation` sets it `7 m` downhill of
  the lane foot and forces its height to the foot's, and nothing flattened
  anything underneath: `0.19 m` to `1.32 m` of air, edge lips of `0.34 m` to
  `1.50 m` against a `0.28 m` step offset, ONE-WAY. The sampler now cuts the
  station its own shelf, like the lane's and the plots'.
- **The mask was square to the HILL, the station to the LINE** - `19.9°` apart.
  It refused `3.71 m²` of real concrete at the corners and granted `7.59 m²` of
  thin air off the sides. At the summit the two frames are the same pair of
  vectors, which is exactly why no test ever saw it.
- **He was turned the wrong way.** The arrival faced him up the lane, which
  bears `19.9°` off the flight's axis - so two metres of walking straight took
  him off the side of his own staircase and `0.48 m` down onto the pad, which
  he could not then climb. Off the cabin he now faces down the steps.

One more that was a bound rather than a sighting, and worth keeping: the shelf
was `1.4 m` wide against a `2 m` terrain grid, and a shelf narrower than a cell
is not reproduced at its own rim - the outward vertex bracketing the edge sits
on the raw slope and drags the rim down by up to `0.16 m`, which on the slab is
a `0.32 m` lip. `StationApron` is now the cell size, and the cell size lives in
the sampler because the sampler is the contract.

**And the canon rule.** The user set one: the city is overcast and foggy day and
night, so every lighting fixture burns always. It is written into story bible
§20 with a number - the day takes a third off a fixture, no more - and the
village is explicitly out of scope, being canonically the one warm bright place.
Writing it forced two corrections. My own first draft claimed the day differs
from the night in visibility; it does not - `CityFogDensity` and
`CityFarClipPlane` are written once and never touched by hour. And §19 held
"light that does not go out" as a piece of EVIDENCE, which the law destroys: the
evidence is now the emptiness under the lamp, not the lamp, and §8 was re-hinged
onto that. The fixture-side implementation is NOT done - it crosses four
lighting systems and eight art-bible entries that say lamps go dark by day, and
it is not something to start blind at the end of a long session.

Verification: EditMode **1780 passed, 3 failed** - the same `CityMiscAssetTests`
catalog reds. PlayMode **4 passed**, including the new village exit. The two
EditMode village tests were proved red first: on the hill frame the pad axes
come back `-0.94` against `1.0`, and without the shelf the pad "stands 1.23 m
clear of the ground". The exit test was red before the fence came out, naming
the wedge. No player build; the hand pass is what settles whether it reads.

## 2026-08-28 — The station was invisible to the validator, and the hut was not the cut

`MountainCablewayObstaclePlan` is the new one-list-two-readers piece: every
solid box a station puts on the ground, as a pure function of its plan. The
world builder places them and gives them their colliders; the site validator
floods with them. Before it, the station was a **hole in every check the
terminal has**. `MountainRoadTerminalSiteValidator` already walks the summit
with the player's own `0.32 m` capsule and `0.28 m` step offset, and its own
comment says neither the walkable mask nor any other validator would notice
furniture cutting the yard in two — but the fill only ever walked `site.Parts`,
and `MountainRoadTerminalSitePlanner` contains no mention of the cableway. The
pad, the four columns, the drive hut, the fence and the whole boarding strip
were invisible to it. `MountainRoadTests` measured the station only as "between
8 and 20 colliders", which says nothing about WHICH.

The boarding side is rebuilt from the plan rather than from literals in the
builder. `MountainRoadCablewayPlan` now derives the column offsets the frame is
built from, the strip's inner and outer edges, the gate jamb, the step run and
the apron; the builder reads them. Three things it fixes, all authored wrong
and none of them noticed:

- **the strip was built through a column.** It ran to `4.075` and the corner
  columns stand at `3.81`–`4.09`. It now stops at `3.75`, derived from
  `StationColumnInnerFace` rather than from `dock + 1 m`.
- **the steps straddled the fence.** Treads at `0.88`–`1.90` with the fence at
  `1.56`: the way up and the thing barring it were the same half metre. They
  now start at `1.75`, past it.
- **the strip stood on open ground.** The bullwheel is `4.5 m` forward of the
  station centre on a `6.2 m` pad — "outside the canopy footprint entirely", by
  the builder's own comment — so most of the strip is off the concrete. A
  `Physical Boarding Apron` now carries it. Kept to the STRIP's width and not
  the pad's: at the pad's width its outer-forward corner lands `0.068 m` from
  the plateau polygon, which was checked numerically before it was authored.

**The correction that matters more than the fix.** The premise was that the
drive service hut physically blocked the approach — it stood at `+3.25`, its
body running `2.20`–`4.30` across the lane the steps rise out of, with `0.20 m`
to the pad's edge. The first half is true and the hut has moved to the machine
side where the drive is. The second half **does not survive measurement**: the
hut is `2.1 m` of a `9 m` pad, and it is not a cut. Restoring it to `+3.25`
leaves `Site_LetsTheHeroWalkFromTheRoadToTheBoardingDock` green, and — the
decisive one — leaves the new PlayMode approach test green too, with a real
`CharacterController` sliding around it and reaching the dock. So the hut was
never what stopped anybody boarding. On the evidence the remaining candidate is
the nineteen seconds: the line was built RUNNING, `CanInteract` did not require
a docked cabin, so pressing `E` called one and set a `waitingForCabin` flag and
then nothing happened, visibly or audibly, for about nineteen seconds. That is
now gone by construction rather than by fixing it — see below — but it is an
inference, and the hand pass is what settles it.

Two things had to change before the new tests could bite at all, and both are
worth keeping:

- **the fill was a POINT.** With station boxes rasterized cell-centre-in-
  footprint, it walked the `0.20 m` slot between the hut and the pad's edge.
  Obstructions are now widened by the capsule that has to pass them; SURFACES
  are not, because widening the strip would swallow the treads that climb it
  and wall off its own steps. `MountainCablewayObstacle.IsWalkableSurface` is
  that distinction and it is not cosmetic.
- **`CheckReached` was too loose to mean anything here.** It accepts any open
  reached cell within `±4` cells — a metre — and the existing station check
  aims `5.4 m` SHORT of the station centre, at the yard. On a `1.37 m` strip
  that lets ground beside the platform vouch for the platform. The dock check
  searches `±1` cell and demands the cell be at the DOCK'S OWN HEIGHT.

**The line is built standing**, with a cabin already on the boarding point, and
turns only once somebody is in it. `Initialize` searches for the cabin whose
phase puts it on the point rather than assuming index zero, and starts running
in the old way if none is. That one change deletes the whole waiting knot —
`waitingForCabin`, the poll in `Update`, and the unanswered question of what
confirms a call to the player — and `CanInteract` now refuses while the line
runs, so the prompt never shows over an empty bay. `RequestDockAt` stays: the
arrival still uses it, and it is the way back if the line is somehow moving.
`ApplyMotorVoice` goes to SILENCE at rest rather than to a `0.05` idle hum,
because the line now spends most of its life parked.

The faded sign moved `1.2 m` inboard onto the fence's last bay. It stays — that
is still the truest thing about the place — but the gate is now where it used
to cantilever out to, and a board at chest height across the only way in is one
the hero walks through. The gate itself has no outboard jamb: the station's own
column is already at `3.81` and a second post cannot stand there, so the fence
ends at the jamb and the bay beside it is the way through. Worth knowing and
not fixed here: **that fence does not seal the boarding side**. It spans
`-2.2`–`2.295` on a `9 m` pad, so closing the gate entirely still leaves the
dock reachable around its end — which is why the reachability test is really a
guard on the STEPS, and is proved so by removing the treads.

**The cabin windows were the lamp lens.** `CreateCabinWindow` passed
`CityNightResources.EmissiveMaterial` — `Assets/Resources/Materials/CityNoirEmission.mat`,
`RenderType: Opaque`, `_Blend 0`, `URP/Unlit` — so the three panes on each of
four cabins were glowing plates and the alpha authored on the tint was
discarded outright. The whole ride is first-person specifically so the hero can
watch the slope fall away, and he was looking at a wall. They now wear
`HomeBalconyResources.GlassMaterial`, which is the glazing the cafe two hundred
metres down the same road already carries: a `HideFlags.HideAndDontSave` runtime
singleton on `Bar Promenade/Home Window Glass`, whose `Queue`/`RenderType`
Transparent, `Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off` and `Cull Back` all
live in ShaderLab rather than in a `.mat` — so there is nothing for a URP
ShaderGUI to rewrite behind us, which is the `_SrcBlend 1↔5` trap that has bitten
`CityBusGlass.mat` repeatedly. Tint alpha `0.24`, just under the cafe's `0.28` on
the same shader, because this is the only pane in the game the hero rides BEHIND;
the fragment adds edge highlight and grime on top of it.

Three things that are decisions and not details. The material is **shared and
read-only** — the cabin's tint rides the per-renderer property block `CreateBox`
already writes, because writing it on the material would repaint the cafe's
three window walls, its boiler sight glass and the hero's own balcony; a test
pins that. The panes stay **closed boxes and not quads**: `Cull Back` plus a box
gives the passenger the inner face and the platform the outer one, while a
flattened plane would look right from the platform and be invisible from the
bench — the church vault's lesson in a smaller room, and the test pins
`vertexCount == 24` for it. And the two **practical lenses stay opaque
emissive**, because each has a real spot light parented under it.

Considered and rejected: a new `MountainCabinGlass.mat` plus its own loader
(same shader, so identical pixels, in exchange for an asset, a guid and a new
runtime invariant), and a `Glass` member on `MountainRoadSurfaceKind` — that one
is structurally impossible, since `MountainRoadSurfaceAppearance` assigns one
shared opaque material and writes six properties into an MPB, and blend, ZWrite,
cull and queue are per-material state an MPB cannot carry. The sweep found no
other call site worth changing in the same pass: the church's stained glass is
deliberately opaque unlit HDR driven per-minute, and the city's facade windows
are actively locked opaque by `CityWindowAppearanceTests`. Left as backlog, not
touched: the pub and supermarket storefront glass, which need a third branch in
an editor manifest→material mapping and a prefab rebuild.

**The dock had no light on it, and chasing that found a worse bug.** The
station's two fixtures both hang under the canopy on the yard side and both
throw BACKWARDS: measured against the dock they are `92.7°` and `52.5°` off
axis, against half-angles of `50` and `39`. The one square metre of this
station a passenger has to find was the darkest ground on it. Re-aiming either
is arithmetically dead — from `8.4 m` and `7.0 m`, delivering even the pad's own
wash needs `28` and `19`, and this mountain's band tops out at `16` with the
tests refusing anything over `18`. So: a boom off the outboard-forward column,
housing and emissive lens at the flood's own `4.21`, one spot at `7.0` / range
`9` / `72°`, aimed at a standing chest rather than at the concrete. It delivers
`0.61` at the strip against the station practical's `0.42` on the pad — half
again as bright as the ground beside it, which is what makes a marker.

**And the reason its coordinates are derived is a defect that had already
shipped.** The head is placed at `BoardingDockRightOffset + 0.52` /
`BoardingDockForwardOffset`, because **the two terminals do not hang their cable
in the same place**: the summit puts it `4.50 m` in front of the pad centre,
`AlpineVillagePlanner` puts it at `1.90`. Anything authored at `4.50` stands
`2.6 m` behind the village dock — which is exactly where the arriving hero opens
his eyes.

Chasing that number down exposed the entry above as half-wrong. The boarding
side was ordered off a fence line authored at a fixed `1.56`, with the strip
running from the top of the steps to twice the dock minus that. At the summit
the chain came out fine. **At the village it solved to `2.77` → `1.03`: a strip
`1.74 m` long IN THE WRONG DIRECTION**, with its own steps past its far end. No
test saw it, because the only test that measured the strip built the summit —
the same shape of blindness as the synthetic PlayMode scene, one level down. The
whole side is now ordered from the DOCK outwards (strip, then the flight to its
near end, then the barrier behind that), which reproduces the summit's authored
numbers to the millimetre and gives the village a boarding side that exists.
`BoardingFenceForward` stops being a constant, and that is the point.

Verification: full EditMode — **1777 passed, 3 failed**. The three are the
`CityMiscAssetTests` catalog reds carried in from the church-courtyard commit
(`97`/`192` asserted against `106`/`205` built) and are untouched by this work.
PlayMode `AlpineCablewayRidePlayModeTests` — **3 passed**, now built on
`MountainRoadWorldBuilder` over the shipped plan with the real
`MountainRoadWalkableArea` instead of a bare cube and an always-walkable area;
that synthetic scene is exactly why the suite was green through a release in
which the cabin could not be entered. Each new test was proved red before being
trusted: `StationObstacles_KeepTheBoardingLaneClear` fails naming the hut and
the lane when it is put back on the boarding side, and removing the treads
makes the validator itself throw `the site cut the cableway boarding platform
off from the arrival` out of `MountainRoadPlanner.Create`. The glass tests were
proved red the same way: reverted to the old material they fail naming
`Universal Render Pipeline/Unlit` where the glazing shader belongs. So was the
inside-out strip: put back on the fence-ordered chain,
`BoardingSide_IsOrderedFromTheDockAtBothTerminals(False)` fails with
`-1.74001217`, which is the arithmetic exactly. Two light-count equalities had
to move with the new fixture — `MountainCablewayTests` 2→3 under the cableway
root, `MountainRoadSummitLightingTests` 5→6 within `45 m` of the apron; both
were equalities rather than floors, which is why a third lamp could not be
added quietly. No player build; no hand pass in the editor, and the hand pass is
the open question above.

Not settled from the files, for whoever runs the hand pass: whether `0.24` is
right from the bench rather than on paper — the near pane sits about `0.4 m`
from the eye and the far one about `1.15 m`, and the hero sees both plus the
open doorway at once. And these panes used to be emissive, so the cabin has lost
what faint self-glow it had on a climb that is deliberately dark; if it now
reads as a black hole against the valley, the answer is a small practical inside
the cabin, not a thicker glass.

## 2026-08-28 — The village is built, and a door that scales is not a door

`tools/build-village-3d-model.py` is the fourth deterministic Blender kit, cloned
from the mountain misc generator and keeping all of its machinery — the pure
authoring pass, the double run and `sha256` comparison before the filesystem is
touched, `validate_assemblies`, the atomic manifest write, the fixed FBX axes.
Eleven assemblies, twenty-seven meshes, `1844` triangles: four crooked houses,
the chapel over the source, the mine cart, the adit frame, three grave markers
and a firewood stack. `VillageAssetProvider` carries them on a flat entry table
(the City kit's pattern, so a later wave is additive) and
`Village/VillageAssetSetup` derives what it expects to import from that runtime
catalog rather than from a second list, so the two halves of the pipeline cannot
drift apart in silence.

**Two decisions did more work than the geometry.**

The kit ships **no doors and no window panes**. Both scale with the descriptor,
and these plots run from a four-metre cottage to the seven-metre house at the
head of the lane — one modelled door would be a hatch on one and a barn opening
on the other. They are drawn by the world builder at real metres instead, which
is the church's own doctrine: the imported model owns mass and material, the
plan owns every opening a person uses and every collider gameplay touches. The
first pass then hung them on the plot FOOTPRINT and they floated half a metre
off the wall with their own shadows behind them — the authored shell stops at
`0.415` of its cube so the roof can overhang it. Fixed by asking the walls mesh
for its own bounds rather than mirroring the generator's constant in C#.

And it raises **no new surface sheet**, per the art bible §10g entry written
earlier the same day: every part wears one of the mountain area's fifteen. That
is a real saving — no second texture generator, no second manifest — and it is
also the correct reading of the zone, because what makes the village warm is its
light and not its substance.

The garlands are the zone. Bulbs are emissive geometry in one combined mesh per
span with only every other span carrying a real lamp: five lights for the whole
lane, against the eighty-odd that one-light-per-bulb would have asked URP for.

The first contact sheet came back with four houses that read as the same house —
the eaves and apex offsets were a couple of centimetres of the normalized cube,
about fifteen real centimetres. Pushed to an apex a metre off centre and eaves
half a metre apart, which is what makes two slopes of one roof visibly
different. Three editor captures (from the lane foot, mid-lane and overhead)
were rendered to check it and the probe was deleted afterwards; the first of
them was thrown away, because the first `Camera.Render` of a session draws with
no shadow maps. The probe's own first camera position was wrong in a useful way:
it stood at the station pad centre, which put the lens inside a cabin — the near
list it printed is what said so.

`ai/system-tree.md` and `ai/systems-map.md` are now updated, which closes the
documentation debt named in the two entries below.

Verification: full Unity EditMode suite — **1766 passed, 3 failed, 0 skipped**.
The three are the same `CityMiscAssetTests` reds as the entries below and are
still not from this work: they assert the City misc catalog at HEAD's `97`/`192`
while the uncommitted church-courtyard work in the tree has grown it to
`106`/`205`, and `git diff` shows that test file untouched. PlayMode
`AlpineCablewayRidePlayModeTests` re-run after the kit landed — **2 passed**.
The Blender generator validates and builds with matching repeated signatures
(`e6181b4c…`), and `VillageAssetSetup.RunBatch` imported and bound the provider
headlessly. No player build; no hand pass in the editor.

## 2026-08-28 — The cableway carries, and the dock was standing inside a pillar

Boarding, the ride and the return are built. `MountainCablewayController` no
longer derives position from `elapsedSeconds * CabinSpeed` but accumulates
`travelledDistance`, which is what makes `RequestDockAt` / `Resume` possible at
all; `MountainCablewayDriveRules` holds the two profiles as pure functions.
Braking is `v = cruise * sqrt(d / brake)` against the distance REMAINING and
the final step is clamped to it, so a cabin comes to rest ON the point rather
than approaching it asymptotically — the EditMode test pins that to a
centimetre at a coarse `1/30 s` step. A cabin already sitting on the point is
sent round the whole loop instead: the offer is "the next one".

**The boarding dock was inside the bullwheel pedestal, and only PlayMode found
it.** Every EditMode number was correct — step height, tread risers, seat
anchor, clearance — and the hero still could not board, because the dock had
been placed between the two tracks. That gap is `1.15 m` wide and the
pedestal's own foot fills it: physics shoved him half a metre clear at spawn
and he then spent the whole test walking at a point inside solid steel, with
nothing louder than one `entry was blocked` line to say so. Boarding is now
OUTBOARD of the outbound track, which is also simply what a station does, and
the cabin's doorway moved with it to local `+X`. Found by instrumenting the
declining function's own branches rather than re-deriving its conditions from
outside; the probe was removed once it had answered.

Three smaller defects the tests caught before that:

- **the launch ramp was a fixed point at zero.** Speed was a function of
  distance run since resume, so at zero distance the speed was zero, the
  distance never grew and the line sat there for ever looking like a stuck
  dock. `MinimumLaunchFraction` is the floor that lets it start.
- **`CabinAttachmentToBottom` is the cabin's UNDERSIDE**, and the standable
  floor is `0.40 m` above it on the lower skirt. Measuring boarding against
  the underside put the platform `0.40 m` too low and turned the `0.42 m`
  step straight back into the `0.82 m` climb the platform exists to remove.
  `CabinSkirtHeight` is now one constant the plan and the builder share.
- **the open gate had a post standing in it.** The rails were cut for a
  `1.6 m` opening and the middle fence post left on the centre line, so the
  fence read as open and was not.

The platform is `0.85 m` proud of the pad, well over the hero's `0.28 m` step
offset, so it carries three treads at its yard end; `PlayerFactory.StepOffset`
is named for the same reason `SlopeLimitDegrees` was.
`MountainCablewayStationKind` splits the two terminals — the drive keeps motor,
reducer and shaft, the return gets a tension carriage and weight stack and no
motor voice at all, which meant relaxing `Initialize`'s reducer null-check
rather than handing it a gearbox that is not there. The village station is now
built by the cableway builder rather than hand-copied beside it.

The passenger rides from `MountainCablewayController.Moved`, raised in the same
call that posed the cabins, and is never reparented. `AreaArrivalToken.Cableway`
arrives with him already in a contextual interaction, so the arrival only ARMS
in `Awake` and its coroutine holds under an already-black screen until
`IsTransitioning` clears — plus one further frame, so the force-complete pass on
the falling edge has been and gone. The seat plan is re-solved against the far
station before anything reads it, and `BeginPositionedLoop` is used rather than
`BeginLooping`. `GameSessionState.IsRidingTheCableway` gates the three things
that move him, and the map's checks now read `IsRidingAVehicle`; the window onto
the map stays open, as it does in the car.

Verification: full Unity EditMode suite — **1760 passed, 3 failed, 0 skipped**.
The three reds are `CityMiscAssetTests` and are NOT from this work: they assert
the City misc catalog at HEAD's `97` assemblies / `192` meshes while the
uncommitted church-courtyard work in the tree has grown it to `106` / `205`, and
`git diff` shows that test file untouched by this session. PlayMode
`AlpineCablewayRidePlayModeTests` — **2 passed, 0 failed**: the line stops for
him, he reaches the bench, the cabin carries him within `5 cm` of his captured
offset over three seconds of pinned frames, getting out mid-air is refused, and
the leg only leaves the area once the fade is fully black. No player build, and
no hand pass in the editor yet.

## 2026-08-28 — The village above the cableway exists, and its ridge had to out-climb the hero

`GameAreaId.AlpineVillage` is the eleventh scene and the eighth gameplay root:
`AlpineVillagePlanner` → `AlpineVillageValidator` → `AlpineVillageWorldBuilder`
→ `AlpineVillageWorldResult`, composed by `AlpineVillageRoot` on the
`MountainRoadRoot` shape. One crooked lane climbs `82.1 m` and `6.4 m` (an
average `7.8%`, under the `8.3%` pedestrian ceiling) from the cableway station
on the lowest terrace to the house at its head; twelve houses stand either
side, and the chapel, the adit and the burial ground sit on side spurs so the
head of the lane belongs to the house alone. `AlpineVillageTerrainSampler` is
the single height contract for planning, validation, the ground mesh and the
map's teleport ground, split into a bare-slope pass the planner lays the lane
along and a finished pass that flattens the shelves.

**The enclosing ridge was climbable, and the comment claiming otherwise was
mine.** `RidgeRisePerMeter` was authored at `0.62` — `32°` — against a
`CharacterController.slopeLimit` of `45°`, so "reachable only by cableway" was
a promise kept by the walkable mask and by nothing in the world. The test that
caught it was measuring the wrong thing too (it asked for `8 m` of rise on all
four sides and got `6.95` on the downhill one, where the ground legitimately
falls away as the line leaves). Fixed at the source: `1.15` is `49°`, which the
controller refuses on its own, and `PlayerFactory.SlopeLimitDegrees` and
`StepOffset` are now named constants so terrain meant to be a wall can be
authored against the same numbers the hero is built with rather than against a
literal repeated in two files.

The map grew a third tab. `CityMapMountainRoadOverlay` is reused rather than
duplicated — both tabs are one polyline and one rectangle, and here the
polyline is the lane — while the places up there reach the chart as map POINTS,
which is the mechanism the inspector and the teleport already read. Ordinary
houses are deliberately not on it. The village label for the house at the top
is «Дом на вершине», not «Дом матери»: the §6 registry row lifts the PLACE and
not the events, and a map label naming her would be the one line about her the
lift explicitly does not grant.

`AlpineVillageWeatherShaper` re-reads the city's schedule with a ceiling as
well as a floor, because §12 bans the storm outright, and damps the wind where
the exposed road amplified it — `WindShelter 0.45` against the road's
`WindExposureAtFoot 1.7`. The altitude multiplier lands after the clamp into
`0..1`, the ordering the mountain road already paid for. `WarmthGrade` is
plumbed as a PARAMETER of `RuntimeSceneSetup.ApplyAlpineVillageLighting` and
`ApplyAlpineVillageVisibility` rather than something written over them, because
the per-minute re-apply wipes anything written from outside inside a second;
it stays at `0` until the prologue exists. Fog density `0.0145` was chosen
against one shot rather than by feel: the house is `82 m` up the lane and about
a quarter of it survives the haze, which is the warm shape the composition asks
for, and the ridge still hides the edge of the world.

Canon: the story bible's §6 registry gains a row lifting §15, §18, §25 and art
bible §10f together, on the user's explicit decision, recorded as an accepted
architecture exception. §12 gains a «Форма» section, §2 records that the
cableway carries, §23 item 11 is restated and split, and the art bible gains
`§10g` plus an amended §10f that no longer calls the plateau the only zone
outside the city.

Not done yet, and named so it is not mistaken for finished: the cableway does
not carry anyone — boarding, the ride, the fade at the ridge and the return are
Phase B, and the village is currently reachable only by the map tab, exactly as
the mountain road already is. The houses are massing shells from runtime
primitives; the village Blender kit, the garlands, the chapel interior, the
mine cart and the graves are Phase C. `ai/system-tree.md` and
`ai/systems-map.md` are not yet updated.

Verification: focused Unity EditMode selection
(`AlpineVillageTests`, `AreaTravelContractTests`, `LocalizationCatalogTests`,
`MountainCablewayTests`, `MountainRoadTests`, `CityMapAreaPresentationTests`,
`CityMapDistrictPresentationTests`, `MountainRoadTerminalTests`) — **81 passed,
0 failed, 0 skipped**. Both localization catalogs parse and hold `447` keys
each. `BarPromenade.Runtime` and `BarPromenade.EditModeTests` compile clean
headlessly. An earlier run of the same selection was `44/45`: the one red was
`CityMapAreaPresentationTests.CrossAreaTravel_UsesCallbackAndMapTeleportArrival`
asserting exactly two area tabs, correctly updated to three. No PlayMode run,
no player build, and no broad EditMode suite.

## 2026-08-27 — The church moved toward the street and gained a maintained yard connection

The user explicitly accepted one narrow exception to the previous Church and
Cemetery site canon. The implementation keeps the City exterior at `0.55`
scale, moves it from a `16 m` to a `10 m` west-street setback, and gives the
precinct a stone forecourt plus a restrained north lawn/garden: two sittable
benches, two small trees, six clipped shrubs and two modest beds, with no new
realtime light, sound or lore. `CityChurchCourtyardPlan` owns every surface,
fixture and reserved route. The visible yard, linked gravel extension and
modified north-fence posts/rails consume passive Blender-authored City misc
meshes while Unity keeps only placement, batching and collision. The added kit brings that
catalog to `72` kinds, `106` assemblies, `205` role meshes and `36,050`
triangles.

One maintained `3 m` opening continues the cemetery's middle cross alley
through its north fence into a south church path. The west cemetery gate stays
the only street gate and remains the route for the mourner, watchman and grave
work. `CityChurchCemeteryPassagePlan` is shared by cemetery and church
composition: it selects an existing cross-alley axis, cuts exactly one
post-ended fence interval, extends the gravel to the boundary and proves the
shared threshold is capsule-clear and within the safe step contract. The map
cuts that same interval out of both precinct outlines without making it a
street gate or teleport anchor, and City map arrivals reject every courtyard
fixture footprint.

Verification: the focused Unity EditMode category
`CityChurchCemeteryPassage` passed `2/2` in `0.744 s`; the final
`CityChurchCourtyard` category passed `3/3` in `1.621 s`, with `0` failed or
skipped. Unity exited cleanly after compiling Runtime and EditMode. The City
misc Blender generate/validator finished at `106` assemblies / `205` meshes, and
`CityMiscAssetSetup.RunBatch` imported and bound the expanded provider
successfully. No broad Unity suites or player build were run.

## 2026-08-27 — Calendar days and the F9 day selector

The existing continuous session clock now exposes its zero-based day index as
player-facing `DAY N`. Wake and every later midnight queue a brief persistent
announcement outside transitions and modal UI, while inventory keeps
`DAY N · HH:MM` visible in its Status panel.

The F9 window in City, Bar and Mountain Road now has direct day `1–7` buttons.
A debug change preserves time of day, running/frozen state and needs; ordinary
midnight progression remains unbounded. Moving backward also rebases City's
scheduled and triggered one-shot audio timestamps so a `7 -> 3` test jump
cannot silence the soundscape until the old date catches up.

Verification: focused EditMode selection (`GameTimeStateTests`,
`LocalizationCatalogTests`, `CitySoundscapeIntegrationTests`) — **18 passed,
0 failed**; focused PlayMode day-selector and inventory day/time scenarios —
**2 passed, 0 failed**. The first EditMode attempt met two transient compiler
errors in concurrent Church work; after that work supplied the missing methods,
the unchanged focused selection passed.

## 2026-08-27 — The sun came in through the roof, and the roof was not there

*(The church's own light ended up BAKED at one pose — see the note partway
down. The solar arc below is real and global, and the City and Mountain Road
use it; the church simply stands at one hour of it and turns its shafts on and
off with the clock.)*

Asked how "light from the window" worked in the church by day, and whether
it was not simply the City's own lighting arriving as though the sun passed
through the roof. It was, and more literally than the question supposed.

**Four separate reasons no light had ever entered this building through a
window.**

1. **There were no windows.** `INT_PlasteredShell` was five unbroken `0.32 m`
   boxes and the generator contains no boolean anywhere. The "stained glass"
   was an `8 cm` slab glued to the inside face of a solid wall.
2. **There was no roof** over either side aisle, over the narthex — where the
   hero opens his eyes, at `z −18.8` — or over the sanctuary. The vault spans
   only `x ∈ [−8, 8]`, `z ∈ [−15, 18]`, and the aisle walls stop at `y 10`.
3. **The vault cast no shadow either.** `INT_RibbedVault` was six vertices and
   two quads — `4` triangles, no thickness — and both faces point DOWN into
   the room. URP's ShadowCaster pass culls back faces, so from the sun's side
   there was nothing there at all. The comment in
   `ChurchInteriorDayNightController` claiming "the vault seals the nave from
   the directional" was false and has been replaced with what is actually true.
   There was also an open rectangle over the west door, `x ±1.4` by `z 4.2..14`.
4. **`shadowStrength` was `0.48`**, so even where something did occlude, 52% of
   the sun came through it. No amount of tuning anything else could have made
   a window matter while that stood.

What the player read as daylight was twenty hand-aimed lights — ten spots with
their targets baked at build time, ten point glows — fired at **identical
intensity into both aisles at every hour**, plus real sunlight pouring in over
the tops of the walls. There is not one `Dot(sun, wallNormal)` in the old code.

**The sun now moves.** `GameTimeDayNightRules` held one constant pose,
`Euler(52, 28, 0)`, from 07:00 to 18:00 — and a test asserted that morning,
noon and evening were byte-identical, which is exactly why nothing in the
world could tell the time from the light. It is now a real solar arc: rises
due east at 06:00, culminates due south at 52° at noon, sets due west at 18:00.

**The model is an equinox, and that is not a simplification but the only
consistent answer.** A twelve-hour day forces zero declination. Asking for a
thirteen-hour day instead forces a POSITIVE declination and a sunrise north of
east — and the church's north aisle would then catch an hour of grazing light
that a basilica in this hemisphere must never see. Twelve hours keeps the
azimuth inside `90°..270°` all day, so **the north aisle takes no direct sun at
any minute**, which is what makes the room read.

The peak elevation is the one authored number, and it is deliberately the `52°`
the retired pose already carried: **the old fixed pose survives as the arc's
13:07 pose**, `3.26°` off. The City's afternoon is the frame it always was;
morning and evening are new around it.

**The interior does not share the City's compass** and used the world sun raw.
The model stands at identity in its own scene while the building stands in the
City with its door west and altar east, so the frames differ by a quarter turn
(`ChurchInteriorSunRules.InteriorFromWorld`). The interior's `+Z` is the altar
— confirmed by three independent coordinate matches before anything was built
on it — which puts local `+X` on the south. The constant is pinned by a test
derived from the same `Vector3.right` the city planner enforces, because a
quarter turn looks like nothing in the source.

**What the shell is now.** Each aisle wall is two `0.16 m` leaves whose
openings differ by `0.20 m` on all four sides, so the step between them IS the
reveal and the embrasure splays into the room. Solid vault running the full
length, aisle lean-tos out to the wall head, a ridge cap over the joint — the
two pitches touch along one line and their upper faces splay apart above it,
which from overhead is a `29 cm` slot straight down the middle of the nave.
`23` parts, `9520` triangles, both inside budget.

**Two new build-time validators, and both were proved red before being
trusted.** `validate_lancet_apertures` probes the geometry from both sides:
empty where the aperture is, solid immediately around it. `validate_interior_is_sealed_above`
walks a grid of floor columns and requires **two** covering polygons above
each — which is what catches a single-sided shell, since a solid has an
underside and a top and a surface has one face. Reverting the walls named all
ten lancets; reverting the vault named `1364` open columns, and the `558` that
still passed were exactly the aisle columns under the lean-tos, which is the
arithmetic that proves the check discriminates a shell from a solid.

**The daylight layer now asks one question per window** — does the sun actually
reach you — and the light, the beam and the glazing all read the same answer.
The panes were opaque URP Unlit at a constant cyan at three in the morning and
at noon alike; they now gain `0.10 / 0.55 / 2.60` for night, shaded and sunlit,
and they no longer cast, or an opaque pane would plug the only hole the sun has.

**The ten daylight SPOTS are gone entirely, and so is the reason they existed.**
They stood in for a sun that could not get into the building. The sun gets in
now, and it is a PARALLEL source: it delivers the same light at three metres
and at thirteen. A spot cannot — sized by `illuminance × distance²` for the pool
it aims at, it necessarily blasts everything nearer, and at a low morning sun
"everything nearer" is the whole aisle wall. The last thing they were wanted
for was the COLOUR the light picked up passing through coloured glass, and that
is now the directional's own tint, `(0.734, 0.889, 0.917)` — which is not a
cheat standing in for a light cookie but the physically true colour of the only
light in the room, because every ray of it came through a lancet. Thirty-six
runtime lights became twenty-six.

**The SHADED aisle is not dark, and that is deliberate.** A north window takes
no sun in this hemisphere but it passes sky all day, and the first pass had it
at a gain of `0.55` — darker than the plaster around it, so the north aisle
read as lit masonry with black slots cut in it. The panes now sit at `1.30`,
above the grade's own `0.62` bloom threshold, so they read as lit glass; the
sunlit wall keeps twice that again. Each shaded lancet's glow went `0.24` to
`0.45` of the full value, which makes it a small diffuse source lifting its own
reveal rather than a token. Photographed with the candles out and ambient at
`0.05` to prove the wall is held by its own windows and not by the sconces.

**And the light is BAKED at one pose, on the user's call.** The church does not
track the sun. It stands at solar noon, due south, and the only thing the clock
does to it is strength and colour: shafts while it is light, none after dark,
with the dawn and dusk hours as the ramp. The pose was picked by measurement,
not taste — any lean ALONG the nave lets each window's light slip past the piers
and run the length of the building, taking the room from a mean brightness of
`71` to `104` with the five columns dissolving into one wash. Square-on, each
lancet keeps its own bay.

**The method failure that cost four wrong diagnoses, and it is one line.**
The first `Camera.Render()` of an edit-mode session draws before the shadow
maps exist, so it comes back with the whole room lit and no shadow anywhere.
Every "the 08:00 hour is washed out" frame was that first render. Three fixes
were aimed at the phantom — an intensity ceiling on the cones, a cone narrowed
to subtend its own pool, a beam brightness tied to the sun's descent — and each
one **moved the measured mean by nothing**, which was the evidence and was read
as a mystery three times. A fourth answer came from a bisect that disabled one
light layer per arm without re-applying the others, so one arm silently
inherited the previous loop iteration's glazing and "proved" the cones were the
cause; deleting them outright changed the number by a tenth of a point. One
discarded warm-up frame put the identical scene at `71`. **Throw the first
frame away, and a bisect is only a bisect if every arm is built the same way.**
The cones stayed deleted on the argument above rather than on that measurement,
which is retracted.

The measurement that did hold is the one that mattered: with the sun alone and
ambient at `0.02`, the plan views are black but for five south patches. The
shell is sealed.

**The beams are an oblique prism, re-solved rather than rotated.** The near
ring is the aperture and must stay welded to the wall; only the far ring
travels. A right prism on a rotating transform would swing the window along
with the beam. `ChurchLightShaft.shader` is `CityLighthouseBeam` plus a depth
fade from `CityAtmosphereParticle`, a near fade, in-shader motes in the prism's
own object space, and one thing found by looking: a pure inverse-Fresnel
cross-section darkens the beam exactly when you look ALONG it — which is late
afternoon, when the shafts rake down the nave and the path through the volume
is longest. It left two bright rails with nothing between them. `_CoreGlow` is
the floor that keeps a column a column from every angle.

**Still open, and deliberately.** The interior has no west rose, though the
exterior carries one — the door faces west and the sun sets due west, so a rose
would throw a coloured disc straight down the nave into the altar at dusk. It
costs the 24th and last part in the interior budget and a rebuilt west wall.
And no light cookie: the coloured mosaic a real lancet projects is approximated
by the directional's tint rather than drawn, which the RGB555 composite would
have crushed most of anyway.

- Verification: Blender validator green (`23` meshes, `9520/22000` triangles),
  both new validators proved red on the defects they exist for and restored,
  exterior FBX and preview returned to their committed bytes since that asset
  did not change, prefabs rebuilt. Full EditMode `1736/1736`, full PlayMode
  `193 passed / 9 skipped / 0 failed`, both re-run after the last change (all
  nine skips pre-existing: the eight capture fixtures and the IMGUI batchmode
  limit). The new north/south
  assertion was proved red before being trusted — making `WallFacing` ignore
  which wall it is asked about fails it with "the north aisle cannot take
  direct sun", naming `19.72` where `0` is required. Frames plan and eye level
  at 08:00 / 12:00 / 16:00 / 17:00 / 18:30 / 02:00 from a throwaway probe —
  plan view first and always, because the last time this project judged a
  light pool from eye level the pew backs stood between the camera and the
  floor and produced a confidently wrong diagnosis.
  **Intensities are NOT set from those frames** and must not be: an edit-mode
  camera gets no ACES, no bloom and no exposure. The day frame measures a mean
  brightness of `71` against night's `41`, with five discrete columns and a
  dark nave; how that sits under the grade is an in-game question.

## 2026-08-27 — The chandeliers were a heap of parts standing near each other

Reported as "the chandeliers are unconnected elements", which is exactly
what they were, for two independent reasons that both look like nothing in
the source.

**The hoop was laid radially.** `Quaternion.Euler(0, -angle, 0)` turns a
box's local +X onto the RADIUS, so ten bars pointed outward like the spokes
of a starburst instead of lying along the circle. A tangent needs the extra
quarter turn: `-angle - 90`. Each bar now also spans the CHORD between its
neighbours - `2 r sin(pi/n)` plus a little - rather than an arbitrary
fraction of the radius, so the ring actually closes instead of leaving ten
gaps whose size depended on how big the corona was.

**And the chain came down to nothing.** It ran to the hoop's centre, where
there was no hub and no arms, so it ended in mid air with the ring floating
around it. There is now a boss at the centre for the chain to land on and
five arms out to the hoop.

**A quarter turn is invisible in a diff, so it is measured instead.** The
PlayMode test takes each hoop bar's own outward radius and its long axis and
asserts the dot product is near zero - one is what a spoke reads - and that
a hub and its arms exist. It reports `0.0000` across all ten bars.

- Verification: church PlayMode `3/3`, full EditMode `1726/1726`. Frames in
  `Captures/ChurchCorona/`.

## 2026-08-27 — The votive stand was hanging in the air with its flames painted on

The one fixture the previous pass did not reach, reported precisely: every
candle now behaves except the candelabrum, which still floats and whose
flame is still baked. Both true, and both had the same root - it is the one
lit fixture the IMPORTED MODEL owns end to end.

**It floated by thirty centimetres, and the arithmetic was in plain sight.**
The base plate was `cylinder((x, y, .36), .375, .12)`, which spans z `0.30`
to `0.42` against a floor whose top is `0`. The stand is now a foot on the
ground - plate `0..0.12` - and the STEM makes up the difference, lengthened
from `.62` to `.90`, so the ring at `1.02` and every candle standing on it
are exactly where they were. Nothing above the foot moved.

The envelope check could never have caught this: it asserts geometry stays
INSIDE the declared `0..1.35`, and a floating stand is inside. There is now
a check for the other end - the lowest vertex of each stand must touch the
floor - and reverting the plate to `.36` reproduces it for both stands.

**Its flames were baked because they were the one thing that could not
move.** `INT_VotiveFlames` was a single merged mesh of thirty-two diamonds,
sixteen a stand. A merged mesh has no per-flame transform, so the previous
pass gave those two fixtures a flickering LIGHT over frozen geometry, which
is exactly what was reported.

**The fix was to give the fixture one owner instead of two.** The generator
no longer renders that part at all; the runtime builds sixteen flames per
stand and animates them with the same `ChurchCandleFlame` driver as the
sconces and coronas. The model keeps the wax and the ring - those do not
move - and keeps the flame geometry in its LAYOUT group, which is what
declares the fixture's `0..1.35` envelope, so no contract shifted.

The ring rule is now the mirrored pair this codebase already uses for
`PEW_ROW_YS`: `votive_candle_xy` in the generator and
`ChurchInteriorAtmosphere.VotiveFlamePosition` in C#, each naming the other.
The runtime flames have to stand on wicks the model authored, so if the two
ever drift the flames float off their candles - visible immediately, which
is the honest guard for a rule this small.

Interior parts went 23 to 22 and triangles `9,476` to `9,092`.

- Verification: the votive light now drives sixteen flame objects, and one
  measured over 150 frames swung its Y scale `0.0976..0.1635` around a rest
  of `0.13`. Blender validator green, prefabs rebuilt, full EditMode
  `1726/1726`, church PlayMode `3/3`. Frames in `Captures/ChurchVotive/`.

## 2026-08-27 — The piers fought the depth buffer, and the candles start burning

Three reported things in the church interior, two of them plain defects and
one a feature.

**The columns flickered because they were fighting themselves.** Each pier
was a `.70` shaft running the full `9.6 m` with a base and a capital of the
SAME `.70` radius buried inside it - `0.94 m` of curved wall exactly
coincident with curved wall, which is a depth-buffer fight and reads as
shimmer the moment the camera moves. They are now three solids meeting end
to end: base `0..0.44`, shaft `0.44..9.10`, capital `9.10..9.60`, with the
base and capital flared to `.78` so they are a profile rather than a hidden
duplicate.

**And their texture mirrored at every facet.** `assign_world_uv` picks a
projection axis per polygon from the dominant normal. On a box that is
right; on a cylinder the side normals sweep the whole circle, so every facet
past 45 degrees picks a different axis than its neighbour and the sheet
mirrors at each seam - the blotching down the piers. `wrap_upright` now maps
upright curved faces by ANGLE, per connected component, so a merged part
holding four piers wraps around each one rather than around their shared
centre.

`.78` and not more: the aisle routes begin at `x 6.3` and a pier centred at
`5.5` may not reach them. The route validator refused `.82` outright, which
is the second time this week it has caught a change nobody thought was a
gameplay change.

**The coronas hung from nothing.** Their chains all stopped at a single
hardcoded `y 9.4`, but the ceiling over them is not one plane: the vault
ridge stands at `14` over the centre line and the narthex corona hangs under
the choir loft slab at `4.4`. Chain top is now per fixture.

**The candles burn.** `ChurchCandleFlame` drives each warm fixture: layered
sine waves whose periods share no common multiple, so the pattern does not
visibly repeat, plus a rare deeper "guttering" dip. The flame geometry
stretches, narrows, leans and rises; the light it casts breathes at a
FRACTION of that - `0.14` against the flame's `0.28` - because a candle's tip
dances while the pool it throws only wavers, and matching them makes a room
strobe. Every fixture takes its phase from a hash of its own index, or the
whole church gutters in unison. Colour dips toward ember as the flame does.

Measured, not asserted by eye: over 150 frames one sconce's light moved
`1.508..1.919` around a base of `1.75`, and its flame's Y scale
`0.0796..0.1312` around a rest of `0.11`.

**Two owners of one field is the trap here.** The day and night schedule was
writing `light.intensity` and now writes `ChurchCandleFlame.BaseIntensity`
instead; the flame owns the light from frame to frame and flickers around
that. The PlayMode test reads `BaseIntensity` too - asserting on
`light.intensity` would have been flaky by construction, since the flicker
band around the day value overlaps the night value.

The votive stands, altar and font burn in geometry the imported model owns
and the scene cannot move, so those fixtures flicker in light alone.

- Verification: Blender validator green (`9,476` interior triangles,
  exterior byte-identical and restored), prefabs rebuilt, full EditMode
  `1726/1726`, church PlayMode `3/3`. Frames in `Captures/ChurchFlames/`.

## 2026-08-27 — The nave faced the door, and the icons hung on the windows

Two reported defects in the authored interior, both real and both now
build-time failures.

**The Stations of the Cross were hung across the lancets.** Fourteen
stations were spaced evenly at `y −11, −7.5, −4, −0.5, 3, 6.5, 10` down a
wall whose five windows sit at `−11, −6, 0, 6, 11`. Even spacing against
uneven spacing: four of the seven a side overlapped a window, and the one at
`−11` sat dead centre on it. They now hang in the wall between the lancets —
`−13.8, −8.5, −4.2, −1.8, 1.8, 4.2, 8.5`, the two wide bays taking a pair —
and are narrowed from `1.45` to `1.2 m` to fit the gaps with clearance.

**The pews faced the door.** `pew_geometry` put the backrest at
`center_y + 0.27`, and `+y` is the sanctuary, so every worshipper sat with
his back to the altar. The backrest is now the negative offset and the
kneeler swapped with it. The offset is a named constant because its SIGN is
the whole contract.

**And they stopped a nave short of the altar.** Six rows ended at `z −4.25`
with the communion rail at `12.4` — `16.65 m` of bare floor between the last
pew and the rail. Ten rows now run `−8.5` to `5.45`, ending just before the
transept crossing.

**Three separate hardcoded censuses had to be found by running into them one
at a time** — the generator's `pew_halves` contract count `12`, the C#
`ChurchInteriorLayoutValidator.RequiredFixtureCount = 31`, and the EditMode
layout test's list of twelve centres. Each is a number that could have been
derived from the row list and was not. The first two are now derived; the
third is a test and is meant to be explicit, but it reads its count from the
validator's constant instead of repeating `12`.

**Both defects are now build-time failures, and the checks were proved red
before being trusted.** `validate_church_furniture` in the generator fails if
any station's span overlaps a lancet's, if `PEW_BACKREST_OFFSET` is
non-negative, or — read back from the authored geometry rather than the
constant — if any backrest vertex reaches past the seat centre toward the
sanctuary. Reverting the two old values reproduces exactly three failures,
naming the station at `−11` and both halves of the pew check. On the C# side
the layout test now asserts the front row is within `7.5 m` of the rail and
behind the transept crossing.

The transept crossing check earned itself immediately: the first ten-row
layout put the front row at `5.95`, whose `0.72 m` depth clipped the
protected path at `6.3` by a centimetre, and the existing route validator
refused the build. The rows were shifted back `0.5 m`.

- Verification: Blender validator green (`6,412` exterior and `9,476`
  interior triangles, exterior byte-identical and restored from HEAD),
  `ChurchAssetSetup.RunBatch` rebuilt both prefabs, full EditMode
  `1726/1726`, church PlayMode `3/3`. Frames in `Captures/ChurchFurniture/`.
  An intermediate rebuild briefly rewrote a one-ULP `_Color` drift on
  `ChurchRoof` and `ChurchGlassWarm`; the final run put both back to their
  committed bytes, so no material is in the change.

## 2026-08-27 — The church gets light, and a wrong diagnosis worth recording

Reported: the church interior is too dark; it needs more light without losing
the intimate mood, and by day the light should fall in through the stained
glass.

**What was there.** Six lights for a hall of `23 x 44 x 14 m`: two at the
votive stands, two at the high altar, and two "Cool Stained Glass" spots
authored at intensity `1.15`. The nave, the crossing and the narthex had
none — and the narthex, under the choir loft at `z −18.8`, is exactly where
the hero opens his eyes. The scene also had no clock, so it looked the same
at noon as at midnight, and the default `05:59` is night.

**What it has now.** Three layers. A **warm layer** of sixteen —
votive stands, high altar, altar candles, font, six wall sconces (four down
the aisles between the Stations of the Cross, two in the narthex) and four
hanging coronas over the centre line. Every one is real geometry: the sconces
are backplate, arm, cup, candle and flame; the coronas are a chain, a hoop of
iron segments and candles standing on it. A pool of light with nothing making
it reads as a mistake.

**The coronas were the second round and the lesson in them is about
placement, not brightness.** With only wall sconces the aisles and the
windows read beautifully and the whole CENTRE of the room - the main aisle,
the pews, the door the hero arrives by, and the hero himself - stayed one
black hole. The sconces sit on walls eleven metres from the middle of a 23 m
nave, so at the centre line they deliver `1.75 / 11²`, about `0.014`. No
increase in their intensity fixes that without blowing the walls out; the
centre needed its own fixtures, which is exactly what a nave of this size is
lit by in life. The general ambient floor went up with them, `0.150` to
`0.205` at night, because the user asked for general light and not only more
pools.
A **daylight cone** per authored lancet, ten of them, at the exact XZ of
`INT_StainedGlass`, aimed down across the aisle so the pool lands on the
floor a person walks on. And a **glass glow** per lancet, a point at the
window lighting its own reveal — a real window both throws a pool and lights
the wall around itself, and with only the cone the aisle wall stayed dead
and the daylight seemed to come from nowhere.
`ChurchInteriorDayNightController` trades them on `GameTimeDayNightRules`,
the same sun the Home window uses: at noon the glass carries the hall and
the candles drop to `0.62`; after dark the daylight dies and the wax is all
there is; dawn and dusk pass through amber glass on the way.

**The real cause of "the church is dark", and it is a rule not a fix.**
URP falls off with the SQUARE of the distance, and the church's numbers were
authored as if it did not. The two original stained-glass spots sat at
`1.15` and had nine metres to cross: `1.15 / 9²` is about `0.014` arriving at
the floor. The candles at `0.95` had a metre to cross and delivered `0.95`.
Same-looking numbers, seventy times apart in effect. **Size a light by its
throw**: the cones are at `110` for a `6.6 m` crossing, the glows at `9` for
`1.7 m`, the sconces at `1.75` for `1.5 m`. That is what "the lighting works
strangely" in this project has been.

**A wrong diagnosis, recorded because the method failed and not just the
conclusion.** Mid-task this log claimed `LightType.Spot` renders nothing in
this pipeline. **It is false and the claim has been removed.** Spot lights
work — proved twice since, in a bare scene and inside ChurchInterior, on
both `Ps1Lit` and stock URP Lit, and the exact "dead" shaft configuration
throws a plain pool when photographed from above.

What went wrong was the measurement, not the reasoning on top of it. Every
capture that "proved" spots dead was framed from eye level behind the pew
backs, which are `1.5 m` tall and stand exactly between a nave camera and
the floor a `36°` cone lands on. The control that seemed decisive — a Point
and a Spot at one position, only the Point lighting anything — had the same
flaw: the Point radiates in every direction and lit the pew backs facing the
camera, while the Spot aimed straight down at a floor the camera could not
see. **A control is only a control if both arms are visible to the
instrument.** The habit that would have caught it on the first run is the
one that caught it in the end: an orthographic camera straight down over the
subject, which cannot be occluded by what stands beside it.

Also: the interior model's aisles have no roof above `y 9.6` (the vault
shell covers only the nave), so the directional pours down them. It is kept
low on purpose — `0.30` at night, `0.62` by day — so the daylight the player
reads comes through the glass rather than over the wall.

- Verification: church PlayMode `3/3`, full EditMode green. Frames at night,
  noon and dusk from five positions in `Captures/ChurchInteriorLight/`,
  including a plan view of the north aisle added after the mistake above.
  Every intensity in this entry was set from those frames.

## 2026-08-27 — The church you could not walk into, and four centimetres of paving

Reported: the church is far too big from outside, and its door does not work.
Both were true and they were separate defects.

**The door.** The prompt appeared. Pressing it did nothing, every time,
forever. `PlayerAnimatedInteractionController.BeginPositionedInternal` refuses
any entry pose further than `PlayerMotor.InteractionVerticalTolerance` —
`0.02 m` — from the hero's own root, and the church handed it a dock at
`GroundTopY + ApproachSurfaceHeight + GroundedRootOffset`. That middle term is
the forecourt paving, which `CityChurchWorldBuilder` builds with
`RuntimePrimitiveFactory.CreateBox(..., collider: false)`. So the dock stood
`0.04 m` above the only height the hero could ever reach, twice the tolerance,
and the door action declined silently — no exception, no log, `TryBegin`
simply returning `false` up through `PlayerDoorActionTarget` and
`ChurchEntrance`. **A decorative surface was baked into a gameplay height it
does not physically provide.** The dock and the City return are now measured
against the church ground itself, the paving is laid `12 mm` proud — under the
controller's own skin width, so nobody wades through it — and
`CityChurchPlanner.ValidateOrThrow` refuses any plan whose dock or return is
not within the interaction tolerance of the grounded root over `GroundTopY`.

The City return moved too, and that was the second thing the new test caught:
`access.Center` sits on the street's *outer edge*, where the pavement is still
`0.22 m` above the church ground, so leaving the church put the hero inside the
kerb. It is now a stride in, standing on the forecourt.

**Diagnosis order matters here.** Four probes said the door was fine — the
walkable mask contains the dock, the trigger overlaps the interactor sphere,
`CanInteract` returns `true`, the hero walks the approach without stalling.
Every one of those was true and none of them was the bug. What settled it was
calling `Interact()` in the real City scene and printing
`PlayerDoorActionController.IsPlaying` and the animated phase on a timer:
`playing=False phase=Idle` for twelve seconds. Same lesson as the bus-stop
spawn refusal in August — **when an action silently declines, instrument the
declining call's own state rather than re-deriving its preconditions from
outside.**

**The scale.** The authored basilica is `44 x 23 x 32 m` and stood `8 m` off
its own frontage, in a town of `18 m` blocks. From the pavement it was not a
building, it was a wall filling the frame edge to edge, with the rose window
above the top of the screen. The placer now sets `localScale` to
`ExteriorModelScale = 0.55` (`24.2 x 12.65 x 17.6 m`) and the setback to
`16 m`, and the nave is laid on the frontage's own axis instead of hugging the
cemetery, so the walk in is straight and the whole west front — rose window,
bell tower, pinnacles — is visible from the street. One prefab serves both this
landmark and the `ChurchInterior` scene, so nothing was re-authored in Blender
and the interior keeps its size and its twelve layout contracts. The anchor
contract is unchanged: `ExteriorEntranceAnchorLocalPosition` stays the prefab's
own unscaled `(0, 0, 22.05)` that `ChurchAssetSetup` validates, and
`ExteriorEntranceModelOffset` is the placed one.

**The door is now the City's ordinary door.** The authored `EXT_WestDoors` is a
`2.8 x 4.2 m` slab standing *behind* the facade's own stone plinth — its bottom
`1.5 m` walled off, no frame, no handle, nothing marking it after dark. The
placer hides that renderer (and throws if the prefab stops publishing it, so a
re-authored model cannot leave two doors stacked) and draws a `1.8 x 2.6 m`
leaf with a mullion, stone jambs, a lintel, two handles, a flush threshold and
a bracket lamp in the model's own Wood/Stone/Iron colours. Dock, trigger and
prompt now sit at one point `0.82 m` out at `0.82 m` up with a `1.05 m` radius
— the bar's, the grocery's and the player's own front door, to the centimetre.

- Verification: `CityChurchEntrance_OpensForAHeroWalkingInOffTheStreet` is new
  and walks the whole thing — teleport to the return, settle, walk the
  forecourt, assert the prompt, press it, assert `IsPlaying`, ride the
  transition into `ChurchInterior`. It was red against the old code at exactly
  the `IsPlaying` assert, which is the only evidence it is wired to the bug.
  `3/3` church PlayMode; full EditMode suite green; captures written to
  `Captures/ChurchExterior/`.

## 2026-08-27 — The drying yard's carpet rack is asked for as geometry

`DryingYardBabushkaTests.Build_DryingYardCarriesTheCarpetRack` expected three
runtime boxes (`Carpet Rack Post South`/`North`, `Carpet Rack Bar`) that
`87322d4` put behind `if (!shellImported)`. It went red at that commit — the
test has not been touched since `ae2bca1` — and it was the last red in the
suite.

The rack was never missing. `tools/build-city-misc-3d-model.py` authors it
explicitly: two eight-sided posts at `x −6.05`, `z −1.35` and `+1.55`, rising
`0 → 1.62`, and a tube of radius `0.05` from `(−6.05, 1.62, −1.42)` to
`(−6.05, 1.62, 1.62)`. Those are the four runtime constants exactly, and the
tube's length is the deleted bar's `ZNorth − ZSouth + 0.14`. The measured
manifest agrees and did so already at `87322d4`, byte-identical. The gating was
deliberate: the assembly's `unity_owned_parts` lists what Unity keeps — cloth,
lens, light, halo, NPCs, collision proxies — and pointedly omits the rack. In
the same commit the author updated the *sibling* appearance test to assert the
imported role meshes; this one was simply missed.

Confirmed in the built world before touching anything, by a throwaway probe
that dumped the hierarchy: the two rack-post colliders sit at
`(−6.05, 0.81, ∓)` spanning `1.62`, both carpets' cloth hangs at `y 1.660` on
`x −6.05`, and the painted-metal batch's local bounds run `x −6.12 … 4.65` —
`−6.12` being the rack post's own west face, `−6.05 − 0.07`. Nothing else in
that mesh comes west of `−4.65`.

**Two fixes were considered and rejected, both because they would have left the
test's own name a lie.** Copying the sibling and asserting the imported mesh is
present duplicates `CityPointOfInterestSurfaceAppearanceTests` and stops
checking the rack entirely — the same failure the chess rename was fixed to
avoid. Giving the rack three named part meshes turns `metalCount ==` `1` red in
that same sibling.

**What the test guards now is the collider, not the crossbar, and that was a
deliberate narrowing.** A first version proved the whole rack geometrically —
metal at bar height between the carpets, metal on the paving outside them — and
it worked, but `195` lines for one piece of yard scenery is not proportionate.
The stakes are not equal either: a carpet hanging on a vanished bar is a
cosmetic oddity, while the two obstacle colliders still standing at the posts
become an **invisible wall** the player walks into. So the test asks the
narrower question — each post collider must enclose some of the shell's
triangles — and the crossbar is knowingly left unguarded, which the doc comment
says outright. Nothing collides with the bar.

Verification: the assertion was proved able to fail before it was trusted. A
temporary control shifted each post collider `5 m` along Z and required the
same probe to come back empty; it did, while the unshifted colliders both found
metal. The control was deleted. Full EditMode suite green.

Two incidental facts worth keeping. **`Mesh.isReadable` lies about runtime-
combined meshes**: the imported chunks report `false` while `mesh.triangles`
returns all `628` of them, so a triangle walker must never gate on that flag —
an earlier probe that did silently measured only the runtime primitive boxes
and produced a completely wrong answer. And a whole-city sweep of the point-of-
interest builder found `26` of `30` colliders already backed by drawn geometry,
the other four being an artifact of the sweep's own grouping — so this contract
would pass today across the board.

**Two silent holes named, not closed.** `BuildDryingYardFloodlight` and
`BuildIslandMastFloodlight` carry the same gate over `Drying Yard Floodlight
Pole`, `Floodlight Housing`, `Island Floodlight Bracket` and `Island Floodlight
Housing`, and **no test names any of them** — so that authored geometry is
unpinned without anything going red. And the root cause is general:
`unity_owned_parts` is declared per assembly but only null-checked in
`CityMiscAssetSetup`, never cross-checked against what the runtime actually
builds. That is why this red shipped at all.

## 2026-08-27 — The four EditMode reds, and one of them was a real defect

Three were stale expectations and one was production. Taken in turn:

`CityChessTableGeometryTests.Board_DrawsItsTwoColoursAsSeparateTimberBatches`
expected the boxes-era batch names. The imported batcher prefixes its
renderers, so the board now arrives as `Imported Park Timber Masonry Details`
and `Imported Park Timber Street Details`. The two-colour contract itself never
moved: the manifest ships the light plate on the masonry role and the dark
squares plus rim on the street role, two `ParkBatchKey` buckets, two batch
colours. The plan handed to the builder is now narrowed to the chess descriptor
alone, because the bandstand and the playground also draw park timber on the
masonry colour — a city-wide assertion would have stayed green with the board
gone entirely.

`CityMapAreaPresentationTests.CityTeleportLattice_TurnsTheStreetsThemselvesIntoPlaces`
built its layout from the two-argument `CityLayoutGenerator.Generate`. That is
the legacy overload, it lays no church ground, `CityChurchPlanner.Create`
returns null on a city without any, and the church-footprint exclusion below it
had therefore never been exercised. Switched to the blueprint overload. **This
is the second time this session that the legacy overload cost a run** — see the
puddle-planner entry below.

`CityMiscAssetTests.DefaultCity_MigratesWaveOneWithoutMovingRuntimeContracts`
counted `81` wave-one descriptors. Not the Blender migration: `fd691b8` cut the
city from four bars to one, which handed three lots back to ordinary frontage
dressing and two more wave-one props with them. Now `83`, with a comment saying
the number is a census that is expected to move when the city's composition
moves, so the next reader re-counts instead of hunting a regression.

`CityWindDressingPlannerTests.DefaultCity_GroundsStreetMiscAndKeepsCourtyardLinesClear`
was the real one, and it was not the migration either. `HomeYardUtilityPlanner`
grounded the yard's phone booth and dumpster at `site.GroundY` — a single point
sampled at the centre of a rectangle that spans two cells — while both objects
stand several metres away along the bar wall. They sat `8.5 cm` under the
terrain, and that plane is also the floor of their collision proxy and the
stand height of the booth's interaction dock. Born ungrounded on 2026-08-14;
continuous sloped ground arrived 2026-08-15; the assertion that catches it was
written already-red on 2026-08-24. A latent defect a new assertion exposed, not
behaviour that changed.

**The fix went where the anchor is authored, not where the descriptor is
written, and that distinction was the whole problem.** Re-grounding inside
`AddHomeYardUtilityDescriptor` is the obvious patch and it is wrong:
`CityDecorationPlannerTests.cs:524` asserts `booth.Position` equals the anchor
*exactly*, so the obvious patch trades one red for another. `TryCreatePhoneBooth`,
`TryCreateDumpster` and `TryPlaceAgainstAnchorWall` now take the `CityLayout`
and sample `TrySampleGroundTop` at each object's own xz, with the yard datum
kept as a fallback so a failed sample leaves the object standing rather than
dropping it and breaking `CityStreetUtilityPlanTests`' `yardDockCount == 2`.
Anchor and descriptor stay one value, so nothing downstream notices. The plan's
composition is provably untouched: both `IsSeparated` and
`IsProtectedGroundAnchor` compare `PlanarSquaredDistance`, so a change in Y
cannot move a spacing decision. Eight call sites, four runtime and four test,
all of which already had the layout in scope.

Verification: full EditMode suite `1725` tests, `1724` passed. All four names
confirmed present in the results XML — a filter that matches nothing also
reports green. The two tests that the anchor change could have broken
(`BarSideYard_LeansPhoneBoothAndDumpsterOnTheBarWall`,
`BarSideYard_KeepsItsDressingOffTheLeaningUtilities`) are green.

## 2026-08-27 — The canonical bar becomes a complete old neighbourhood pub

The interim three-role City misc bar shell and its generic Unity window bands
were replaced by `bar_exterior_v2`, authored in the existing deterministic bar
Blender pipeline. The fixed-metre `12.2645 x 13.5237 x 9.3435 m` exterior is a
two-storey late-Victorian urban pub: old brick and faded render, a real pitched
slate roof, two unequal chimneys, a lower service wing, bottle-green/oxblood
faceted shopfront, individual upper sash windows, gutters/downpipes and the
retained pictorial tankard. Generator `2.1.0` exports `38` passive meshes and
`4,308` triangles with no imported colliders, lights, cameras or animation.
Full-depth jamb returns close the recessed door itself, while two solid timber
cheeks close the former `0.35–0.41 m` gaps between its central pilasters and
the faceted bay frames. The new `Bar Outer Bay Flanking Panels` mesh closes the
remaining `0.34 m` sightline at both outer bay edges, between the last frame and
the end pilaster, so the empty shell cannot be seen from either side.
The blade-sign anchor and its wall bracket sit on the solid pier beside the
upper sashes rather than sharing a window axis.

`CityBarFacadeWorldBuilder` now places that whole asset at unit scale from its
unchanged `exterior_door` anchor, preserves the `sign_pivot` and
`Bar Landmark Marker` hierarchy, binds dedicated brick/plaster/roof sheets and
hands authored window panes to the existing bar day/night family. City retains
a collider-free, box-projected `ExteriorBrick` foundation skirt, now lowered to
the intended `0.04 m` overlap and inset `0.08 m` from the street and both side
edges so it cannot z-fight with the authored shell. The renderer-free logical
collision stays full-size; the entrance apron, trigger, transition and single
established light/halo are unchanged.

The full Home exterior reconstruction uses the same collider-free pub. A pub
crossing the apartment half-space still receives the clipped legacy silhouette;
hidden pubs are omitted. `CityMiscKind.BarBuildingShell` stays in the v4 catalog
for compatibility but neither City nor the full Home path instantiates it.

Verification: the Blender exterior validator passed with `38` parts and `4,308`
triangles; the explicit `AreaCaptureFixture.CitySpecialBuildings` selection
passed `1/1`, and its general bar frame, two opposing `1280 x 720` entrance
close-ups, both outer bay edges and a low foundation view were reviewed. The
focused
`CityBuildingPrototypeRuntimeTests.DefaultCity_PlacesDistrictPrototypesAndClipsHomeExterior`
selection passed `1/1`; the final
`SceneFlowSmokeTests.CityScene_BarsHaveUniqueColliderFreeSignGeometry`
selection also passed `1/1` after the sign-pier, full frontage closure and
inset brick-foundation regressions were added. Full suites and a player build
were intentionally not run in fast mode.

## 2026-08-27 — The three low-rise City buildings move to Blender

The bar, supermarket and player home now use dedicated Blender-authored
shells instead of visible primitive masses and roofs. Each assembly exposes
three passive roles (`Shell_Masonry`, `Roof_Street`, `Trim_Industrial`) and is
bounded-scaled from a canonical source envelope to the existing procedural
special lot. Their established low-rise height rules remain authoritative.

`CitySpecialBuildingWorldBuilder` owns the shared City/Home placement path.
It aligns source `+Z` to the true frontage, adds only a shallow terrain skirt
and leaves the former building mass renderer-free as the logical collider.
Existing window bands, signs, storefronts, doors, balcony, mailbox, triggers
and transitions remain plan-owned Unity composition. Home projects the same
model into apartment-local space: Full models are reused without colliders,
Hidden models are omitted and Crossing models retain the legacy clipped
fallback.

The deterministic City misc source advanced to `city_misc_citywide_v4`:
`64` kinds, `94` assemblies, `186` role meshes and `33,454` triangles under
signature
`10335b02af0035e0e9ec9f5da2726ade86f5d1d23fc43503e2022f8deb304397`.
The wave-one and v2 compatibility signatures stayed unchanged.

Verification: the full deterministic Blender build/validator passed and Unity
rebound all `186` provider entries. The focused
`CityBuildingPrototypeRuntimeTests` selection passed `1/1`, covering all three
City shells, logical collision and the Home Full/Crossing/Hidden policy. The
explicit `AreaCaptureFixture.CitySpecialBuildings` selection passed `1/1`;
its three `1280 x 720` facade frames were reviewed for scale, terrain joins,
legacy roof duplication and preserved frontage composition. Full suites and a
player build were intentionally not run in fast mode.

## 2026-08-27 — Fixed-metre Blender buildings entered the live City

The four staged `city_buildings_prototypes_v1` wrappers now replace the visible
primitive mass, roof and generic pane rows on every ordinary lot. Placement is
front-anchor driven (`+Z` to frontage, anchor to door plus `0.08 m`) and never
scales or detaches imported meshes. Unity retains only a shallow visible
terrain skirt and the former renderer-free logical BoxCollider, preserving
navigation, sound and special-building behavior.

The combined unreadable glass role now consumes its authored UV2 slot IDs
through one shared URP/PS1-snapped shader. A per-building 64-entry MPB table
keeps deterministic Off/Cold/Warm choices and brightness variants under the
existing global night factor without cloning meshes or creating pane objects.
Because v1 UV0 spans the complete glass role, the shader intentionally uses
flat slot colours instead of pretending to address the old 2x2 curtain sheet.

Roof and facade decoration anchors moved from randomized logical heights and
depths to kind-specific fixed prototype mounts derived from the authored roof
planes. The two v1 roofs without a large flat pad deliberately bed the clock
tower/greenhouse into their host geometry; nothing is placed at an empty
attachment-bounds centre. Home maps the same City pose into its
local frame: hidden models are omitted, whole exterior models remain intact,
and only a model crossing the apartment half-space uses the legacy clipped
silhouette. Bar, supermarket and player-home composition remains unchanged.

Verification: the focused
`BarPromenade.Tests.EditMode.CityBuildingPrototypeRuntimeTests` selection
passed `1/1`. The explicit `AreaCaptureFixture.City` selection also passed
`1/1`; all three `1280 x 720` frames were reviewed for prototype scale,
frontage placement, missing terrain joins, duplicate primitive masses and
slot-window lighting. Full suites and a player build were intentionally not
run in fast mode.

## 2026-08-27 — Landscape pass finished: the tide mark, the terraces, the strip and the districts' own soil

The last four items of the nine-item landscape analysis. Two of the three
estimates I had published turned out to be wrong in the project's favour, and
saying so is most of what this entry is for.

**The wrack line.** `442 m` of sand carried no tide mark but the shader's foam.
`AddWrackLine` now runs the whole row rather than one zone, because a wrack line
does not care which mood of shore it crosses: the dead port, the esplanade and
the wild east all get the same weed and the same litter at the same distance
from the water. It wanders for the reason the foam does — the surf never reaches
the same run of sand twice — and the mats lie ALONG the water within a few
degrees, never across it. The river's cut through the sand is skipped; the mouth
banks own that run. No new part kind: `Debris` was already in the enum.

**The terraces.** The cemetery and church grounds are solid slabs, so wherever
they meet lower ground their side is a real wall the player sees — and it was
drawn with planar XZ UVs, which smear the soil sheet down that wall in vertical
streaks. One argument each: `RuntimeWorldUvMode.BoxProjected`. `ProjectBoxUv`
picks the plane from the face normal's dominant axis, so tops stay XZ and are
byte-identical while the sides finally get their own.

**The `1.25 m` strip, and the anchor it needed.** This is the one item that
genuinely wanted a new `CityDecorationAnchorKind`, and the reason is worth
keeping: the validator maps each decoration kind to exactly one anchor, so a
ground anchor is meaningless without a kind that can only live there. That kind
is `LotGroundDownpipeOutfall` — where a facade's downpipe finally reaches the
ground, which the art bible has promised since its Old Town section and which
has ended in nothing ever since. A cast shoe out of the wall, a splash block,
and the runnel the water has cut across the bare soil. Catalogue `96 -> 97`
assemblies, `190 -> 192` meshes, `GENERATOR_VERSION` `4.2.0`.

Two things made it small. `ValidateAnchor` does not switch on anchor kind for
lot-anchored descriptors, so `LotGround` needed no validation work — only an
entry in `HasLotAnchor`, which is exactly the trap that would have sent it down
the non-lot branch. And `TryCreateFrontageAnchor` already places on this strip
and already samples the terrain through `CityTerrainSurfacePlan.TrySampleGroundTop`;
it only needed an optional depth override, because most street furniture stands
off the wall and an outfall is bolted to it.

**The districts' own soil, and the seam that was not there.** I had reported
this as blocked: either a hard colour seam down the middle of a street, or
shader work the project forbids. Both horns were wrong. Each district is a
single area id, and buildable surfaces are cut on `26 m` cell edges — which are
street centrelines. So splitting the ground by district puts every boundary
under four metres of asphalt on each side. `BuildDistrictGround` builds one mesh
per district plus a neutral catch-all, costing three extra draws and no seam.

The tint comes from `CityDistrictArtProfile.Wear`, which was authored long ago
and read by nothing: the family says what the dirt here is made of and the
amount says how much. Old Town gets brick dust and washed-down soot, Residential
something swept and colder, Industrial the darkest soil in the city, Nightlife
violet-cool and never quite dry. The cast is deliberately small — enough that
the four quadrants separate in grayscale, which the art bible tests for, and not
enough to make the ground a coloured floor. `Build` applies the neutral sheet
itself, so the cast must be written after it rather than through the colour
argument it overwrites.

Verification: 569 EditMode City tests, 565 passed. The four reds are the same
four, by name, as the sweep taken before any of this session's landscape work —
all belong to the parallel buildings migration. The existing
`DefaultCity_CoversEveryOrdinaryLotAndRequiredLandmarks` already demands at
least one of every `CityDecorationKind` in the shipped city, so it is what
proves the outfall actually places rather than silently failing to.

## 2026-08-27 — The city that dies of its water finally shows some

The landscape analysis named this the strongest single addition available:
a city built as a watershed, whose whole plot turns on what was poured into its
supply, had **no visible water infrastructure on the ground anywhere** — no
grate, no gutter drain, no valve cover, no standpipe outside the one Old Town
waterworks court. The facades already promise it (art bible §6: «кабели,
водостоки и трубы образуют многолетнюю сеть») and the downpipes terminate in
nothing.

Two Blender assemblies now exist. **`RoadsideDrainAndCover`** is a gutter grate
— frame, five bars — bedded in concrete beside a round valve lid set apart the
way a valve chamber always is; nothing rises above `56 mm`. **`RoadsideCappedStandpipe`**
is a street column **welded shut**: base flange, `0.96 m` shaft, a weld bead and
a cap plate run over the top, the spout cut off and blanked, the chain that held
the cup still on its eye, and the trough it fed left dry at its foot. It is the
same municipal grammar as the working waterworks court at the other end of the
pipe, which is the whole point — one is used, this one was given up on, years
before the hero and for its own reasons. It is also, quietly, his cover.

The coupling I had assumed turned out to be weaker than reported: the analysis
said a ground fixture needs a new `CityDecorationAnchorKind`, but **`Roadside`
is already the non-lot anchor** and a colonka historically stands at the kerb
anyway. So this shipped with no anchor work at all. And because both are flush,
their collision tier is `None` — no proxy recipe, no `BoxCollider`, and not one
line of walkable-mask work.

The chain, end to end: `tools/build-city-misc-3d-model.py` gains two
`build_*` functions in the modern `local_*` / `root_local_direct` profile,
appended to `make_assemblies` (never inserted — both compatibility signatures
cover prefixes, and both came back unchanged). Catalogue `94 → 96` assemblies,
`186 → 190` meshes, `33,454 → 34,062` triangles against a `240,000` budget;
`GENERATOR_VERSION` `4.0.0 → 4.1.0`. `CityMiscAssetProvider` gains the two
`CityMiscKind` values, the bumped constants and a `StreetMasonryParts` layout;
`CityMiscAssetSetup.RunBatch` rebound the asset headlessly and its
`buildSignature` matches the manifest's `43c01110…`. Then two
`CityDecorationKind` values, the `Roadside` district/anchor contract, protection
radii, `TryResolveImportedKind`, the fixed-metre branch of
`TryResolveImportedTransform`, box fallbacks authored at the generator's own
metres, and a planner pass reusing the existing `TryAddUtilityCoverage` helper
— drains at `34/52 m` spacing, capped standpipes at `110/150 m`, rare on
purpose because one on every corner reads as a style instead of a failure.

Verification: `CityDecorationPlannerTests` 11/11, including a new
`ShippedCity_PutsTheWaterNetworkOnTheGround` that proves the shipped city
actually plans both kinds, on the right anchor, at tier `None`, with the
standpipes rarer than the drains — a silent non-placement is exactly the failure
this kind of pass invites. `CityMiscAssetTests` 5/6, the sixth being the
neighbour's. Broad `BarPromenade.Tests.EditMode.City` sweep: **569 tests, 565
passed**, and the four reds are byte-identical in name to the four from the
sweep taken *before* any of this session's work — all four belong to the
parallel Blender buildings migration.

## 2026-08-27 — Landscape pass: standing water off the road, a bed with grain, a denser dead port

Three items out of a nine-item landscape analysis. Each is small, each closes a
gap the analysis named, and each is green.

**Puddles now pool off the carriageway.** `CityPuddlePlanner.Create` only ever
walked `streetPlan.StreetGeometry`, so a game whose subject is water had no
standing water in the yards, on the cemetery terrace or on the church ground.
The blocker is real and is why this was never trivial: a puddle is a flat
`6 mm` slab and the buildable ground carries the valley's cross-fall — up to
`5.44 %`, which is `17 cm` across a three-metre pool, one end buried and the
other in the air. So the new `CreateOpenGround` pass takes only the ground that
is provably level: `AlignOpenAreasToAccesses` pins **every** cell of an area
declaring a street access to that one access datum, so the fringe yards, the
cemetery and the church ground are dead flat by construction. Each candidate is
inset `4 m` from its cell edge, because only the interior is flat — the terrain
skin ramps toward whatever the neighbour sits at. Park and beach are excluded
by kind: the park keeps its emptiness and the sand slopes to the waterline.
`MaximumOpenGroundPuddleCount = 16` on top of the road cap of `42`, and both
lists draw as the one existing sheet, so this costs no draw call. The street
pass and its signature are untouched, which keeps
`PuddlePlanner_IsDeterministicAndKeepsPatchesBounded` and its "left its source
road surface" assertion meaningful.

**The sea bed has grain.** "Sea Bed Shelf" was combined boxes in flat
`Silt (0.10,0.10,0.085)` with no texture, read through the water's `1.4 m`
depth fade along all `442 m` of shore — the first thing the eye meets at the
waterline. It now carries the seacoast sand sheet on world-planar XZ UVs at the
shore's own pitch, keeping the silt tint: the linear compensation rule
preserves the brightness, so this adds grain without lifting the bed out of the
dark.

**The dead port is denser and reaches inland.** The city blames this place for
killing it — it is the hero's alibi — and it was twelve stations in a
three-metre strip along the old quay line, one item every `12 m`, with twenty
metres of bare sand behind them. Now eighteen stations over a band reaching
`10.6 m` back instead of `5.2 m`. Still one item per seventy-odd square metres:
dense enough to read as a yard, far too sparse to be the scrapyard the art
bible bans. The band still starts north of the shore lane, so nothing blocks
the esplanade walk.

Verification: 74 passed, 0 failed across `CitySound*`, `CityWetSurface*`,
`CityPuddleWater*`, `CitySeacoast*`, `SeacoastFisherman*` and
`CityPedestrianSeacoast*`. A new `PuddlePlanner_PoolsOnlyOnTheLevelOpenPrecincts`
proves every pool sits inside one level precinct cell at exactly that cell's
own `PhysicalTopY + SurfaceOffset`. It cost one failed run first, for the
reason worth repeating: **the two-argument `CityLayoutGenerator.Generate` is
the legacy overload and has no yards, cemetery or church at all** — the
blueprint overload is the only one that builds the city this planner pools on.

A broader `BarPromenade.Tests.EditMode.City` sweep (568 tests) showed four reds,
none of them from this work: `CityChessTableGeometryTests` (batch renamed to
`Imported ...`), `CityMiscAssetTests` (81 → 83 meshes),
`CityMapAreaPresentationTests`, and the `homeyard-booth` ground sample in
`CityWindDressingPlannerTests` — the last already recorded as a known
pre-existing red. All four belong to systems a **parallel session** is actively
migrating to Blender (`CityMiscAssetProvider`, `CityMiscAssetSetup`,
`CityDecorationPlanner`, `CityMisc3D.json` are all dirty in its name). The
usual stash-and-rerun proof was attempted and had to be abandoned: with another
session editing the same tree, stashing this session's files left the project
uncompilable and blocked the pop, because that session had meanwhile changed
`CityWorldBuilder.cs` too. Recovery was per-file `git checkout stash@{0} -- …`
for the nine uncontested files plus a manual re-apply on top of their version
of the tenth. **Do not run a stash baseline probe while a neighbour is editing
the tree.**

## 2026-08-27 — The park swing creaks, and the causal soundscape is complete

`ParkSwingCreak` was the most obviously reserved empty slot in the repo: the cue
existed, was synthesised, routed, given a district and a `ParkPlayground` owner,
marked `RequiresPhysicalTrigger` and allowed by the park's profile — and nothing
ever emitted it. Two tests actively pinned it absent, with the reason written
into the assertion: *"A swing cue needs a real motion binding first."* The seats
have been honest hinged rigid bodies pushed by any `CharacterController` since
they were built. Only the binding was missing.

`CityPlaygroundSwing` now watches the seat's signed pace along its own push axis
in `FixedUpdate`, keeps the peak of the current half-swing, and raises
`CreakOccurred` when that pace crosses zero — **the top of the arc, not the
bottom**, because that is where the load reverses and a rope actually complains;
firing at the fastest point would put the sound under the pivot, where a rope is
quietest. A `MinimumCreakSpeed` of `0.55 m/s` gates it, which is also what makes
a swing go quiet on its own: the arc decays, the peak falls under the gate, and
the creaking stops before the motion does.

`CitySoundscapeAnchorPlanner.AddPlaygrounds` anchors the descriptor to the beam
at `RopeAnchorY`, inside an envelope squared off on the wider of the top beam and
twice `SeatReach` so it holds every point the plank can reach — the integration
test requires the emitter to sit inside the fixture that emits it, and the
runtime plays from the moving seat. `CitySoundscapeDirector` subscribes to the
swings exactly as it subscribes to the carpet-beating babushkas, plays through
the existing `TryPlayPhysicalAction` with the seat as the position override, and
unsubscribes in `OnDestroy`. `CityGameRoot` finds the swings under `World.Root`
rather than being handed them: they are built inside the decoration pass and are
the only moving physical owner the root does not create itself.

The city plan now carries **11 sources, 3 of them triggered**, and every cue in
the catalogue has a physical owner. `ai/systems-map.md`'s causal-soundscape row
loses its stated gap and moves from `Partial` to `Current`.

Verification: `CitySoundscapeIntegrationTests` — 3 passed, and the whole
`CitySound*` EditMode set — **23 passed, 0 failed**. `BarPromenade.EditModeTests`
also builds clean through the Unity-bundled SDK
(`Editor/Data/DotNetSdk/dotnet.exe`, which is the one carrying an SDK; the
`NetCoreRuntime` copy is runtime-only).

Three environment traps cost most of this session and are worth writing down.
Unity batchmode first died on `[Licensing::Module] Error: Failed to handshake to
channel`, which was **not** a licence problem: the licence is present and valid
at `%LOCALAPPDATA%\Unity\licenses\UnityEntitlementLicense.xml`, and the client
log names the real fault — `Unsupported protocol version '1.18.3'`. A stale
`Unity.Licensing.Client` from `Program Files\Unity Hub\UnityLicensingClient_V1`
(**version 1.17.4**, up for two days) was holding the
`Unity-LicenseClient-tushk-6000.5.10` pipe and bouncing the editor's own 1.18.3
client. Killing it clears the handshake; Hub respawns it, so this will recur
until Hub itself is updated. Second, `Unity.exe` is a GUI-subsystem binary, so
`&` does not block and `$LASTEXITCODE` comes back empty — it needs
`Start-Process -Wait -PassThru`. Third, **`-quit` must not be combined with
`-runTests`**: the editor exits before the Test Framework runs and reports
success with no results file.

## 2026-08-27 — The story bible: the game is about alcoholism

`ai/city-story-bible.md` (1,749 lines, 26 sections, 38 `Нельзя` and 31
`Проверка`) is the peer to `ai/city-zones-art-bible.md`: the art bible owns
form, this one owns meaning. It keeps the art bible's register and conventions —
hard-wrapped Russian, `Короткая формула:`, append-only numbering, and a
`Нельзя` / `Проверка` pair closing every section about a place, a character or
a mechanism.

**The subject is the hero's alcoholism; the poisoning is the plot.** He killed
the woman he lived with, carried what a body becomes up the mountain to the
alpine village he is from, and poured it into the spring that feeds the city.
Two weeks pass. He drinks nothing but alcohol because he is the one person who
knows the water kills — and thirst makes stopping impossible, so the thing
destroying him is the only thing keeping him alive.

The machine closes on itself without a line of dialogue. The city's one bar
shuts for good because there is nobody left to drink there; there is nobody left
because he buried them; thirst sends him to his own tap; he drinks what he
poured into the spring, and **the player presses the key**, an ordinary
interaction with no cutscene and no warning, exactly as they did for every drink
before it. Then the debris on the stairwell's sealed upper flight is gone, the
Cat walks ahead, the bar door waits at the top presented as gates, there is
nothing behind them, and he does not get up. The staircase is not delirium: it
is what everyone he buried saw before they went down. Black screen; nothing is
explained.

Two more symmetries fall out of the same decisions and cost nothing to build.
The first glass of water in the game is poured by his mother in the prologue and
he drinks it without noticing; the last he pours himself, and does not notice
either. And the one pedestrian the city already ships in a single copy, with his
own headlamp burning outside the city's schedule "because a person switched it
on", is a miner — from the mine up the mountain where the hero's father died.
The game never says so.

That single decision collapses the world model into one rule: **everything
strange in the game is his.** The six-armed bartender, the cashier's
eighteen-metre neck, the mouthless Длиннорукий, the kettle head, the hopping
miner — none of it is the poison and none of it is metaphor; it is what a man
four months into this sees. The citizens are ordinary, nobody ever reacts, and
that absence of reaction is how the player reads the world. Three things are
real: the water is poisoned, people die of it, and he buries them for `150`
apiece. A grave is the only object in the game that cannot be doubted, because
he dug it with his hands.

Almost none of it needs building, which is the point of §2. The intoxication
ladder, the six stages, the balance arc above `60`, the real fall on 13 bodies,
the nine-drink menu where **water is the cheapest item and the only lethal
one**, the flat the README already calls an old alcoholic's — all shipped. So
is the standpipe court (a fenceless working waterworks, the lower end of the
pipe), the Cat's grin switched off behind `SetGrinProgress`, the gravedigger
who is paid per hole by a watchman who *always has another one* — now explained
by the epidemic — and the stairwell's **sealed upper flight from `3.2` to
`4.8 m`**, which is the staircase into the sky the finale walks up.

Structure: a village prologue plus five city acts over about two game weeks.
The prologue opens with him already at the table — dinner with his fading
mother, who asks after that girl, speaks of his father in the present tense
because she cannot hold when it was, and does not register the one time he says
«Я сделал нечто плохое». Then the chapel, where **the player sees the pour in
full**: the only thing the game ever shows directly, ten minutes in, before the
player knows what it was. The murder is never shown.

The gravedigging is the moral engine. The watchman *always has another hole* —
already true in the code, now explained by an epidemic — and the six-to-eight
people going into them are the city's own built cast. Each has a real errand
(medicine, find someone, get them to the one feldsher in town); some live after
it, some were already lost, and it is never knowable which. He understands
exactly what killed each one and says nothing, and when the woman's turn comes
he carries her wrapped through the city at the height of the epidemic — nobody
looks, because everyone is carrying bodies — and drops her in without a coffin,
a stone, a plaque or the eight words he writes for strangers at `150` apiece.
When someone dies, **everything of theirs stays except them**: the lamp on the
pier rail still burns, the rod is still against the end board, the stool by the
lodge door is still there. That cashes the art bible's six families of
round-the-clock practicals, written long before this story, as literal fact.

New geometry for the entire story is two things: the village and the sealed
room in his flat, whose free pocket at `X ∈ [-0.10, 1.46]`, `Z ∈ [0.82, 3.65]`
reuses the bathroom's west partition and must be built outside the validated
catalogue (the `HomeAlarmClockPlan` precedent) so the count assertions in
`HomeInteriorLayoutValidator` and `PlayerHomeLayoutTests` stay intact.

The one conflict is recorded in `ai/architecture-notes.md` rather than resolved
quietly: the art bible bans mysticism in five place sections and §Статус left
the supernatural undefined. Those bans now describe **level `0-1`** of an
act-driven, monotone, never-displayed `0-5` scale, and above it a ban lifts
only by name and by level through a seven-entry registry in §6. The permanent
list never lifts, and every level must still pass all nine of the art bible's
§16 checks. The art bible's §Статус points at the story bible and states the
rule; nothing else in it was touched.

Verification: every quoted line checked verbatim against
`Assets/Resources/Localization/ru.json`; every claimed world fact checked
against the file that builds it; no line over 80 characters outside tables; 28
locked decisions in §24 and a §25 that keeps the remaining blanks blank —
narrative order stays deliberately open, because a fractured chronology would
force a redesign of the scale, the decay track and the act ladder at once, all
three of which assume time runs one way.

§23 lists honestly what does not exist: act structure and saving, the scale, the
irreversible decay track, thirst as a fourth need, a dialogue system, the roster
with its errands and deaths, removing a dead NPC without touching anything
around them, the Cat outside the stairwell, the sealed room, the body's journey
through the city, the village, the opened flight, the final fall with no rise,
and removing the starting `999`. No code was changed and no test was touched.

**Both bibles are now binding, not merely discoverable.** Registering the story
bible in `ai/README.md` made it findable; nothing obliged anyone to follow it,
and the art bible had the same gap — neither was named in `AGENTS.md`, and
`AI.md` mentioned the art bible once, in prose, about the chess exception. So
`AGENTS.md` gains a `## World canon` section stating which document owns form
and which owns meaning, and the procedure that makes them enforceable: before
adding a detail, find the `Нельзя` it would violate — none means allowed, one
dated in the story bible's §6 registry means allowed from that level, and one
absent from the registry means **the detail is not added** and the discussion
becomes whether to add a registry row. New in-fiction text must satisfy §21,
§16's laws are hard, every scale level must still pass all nine art-bible §16
checks, and a deviation needs an explicit user decision recorded in
`ai/architecture-notes.md` — the same escalation `ai/contextual-animation-
standard.md` already uses. `AI.md` carries the matching Working-agreement
bullet, and the source-of-truth order gains both bibles as a new rank 4, above
planning documents and below accepted decisions.

## 2026-08-27 — The first four City building prototypes are staged in Blender

The ordinary-building migration now has a deliberately non-runtime foundation:
one deterministic `city_buildings_prototypes_v1` source and four fixed-metre
district grammars. Old Town's `FragmentedPerimeter` is
`14 x 13.5 x 42 m` / `768` triangles, Residential's `SetbackCourtyard` is
`11.5 x 11.5 x 40 m` / `1,238`, Industrial's `LowWideProcess` is
`14 x 13.5 x 36 m` / `798`, and Nightlife's `TallDense` is
`12.5 x 12 x 48 m` / `858`. All four fit the production default's minimum
district footprints and live height bands without scale.

Each prototype exports exactly six passive role meshes (`Shell`, `Trim`,
`Roof`, `Metal`, `WindowFrame`, `WindowGlass`), for `24` meshes and `3,662`
triangles total. The manifest also carries a ground-centred front anchor, one
roof and four facade attachment bounds per building, and `194` stable window
slots with UV2 IDs. Source `+Y` becomes Unity `+Z`; no materials, colliders,
lights, cameras or animation enter the FBX. Generator `1.0.0` produced build
signature
`a38ab8521b0470e080ea074204b2a948dc9bdbcc14bfd06cecc95a2d7506c1ac`.

Unity's strict importer wraps the four roots as passive Resources prefabs and
binds them through `CityBuildingAssetProvider`; the registry preserves the
manifest identity, envelope, role renderers and attachment/window metadata.
This slice intentionally adds no consumer to `CityWorldBuilder` or
`HomeExteriorViewBuilder`: the live City still draws its ordinary primitive
masses, roofs and pane rows. Runtime selection, frontage/foundation placement,
facade and per-window appearance, logical collision/decoration authority and
Home half-space handling remain the next integration slice.

Blender `5.0.1` completed the full BLEND/preview/FBX/JSON build and the
independent `--validate-only` pass. Both repeated the same pure-build
signature; the four prototype triangle counts and `45 / 54 / 41 / 54` window
slot counts matched the manifest. A clean FBX re-import found exactly five
Empty roots plus `24` meshes, two UV layers on every mesh and zero materials,
Actions, lights, cameras or other payload types. The final contact sheet keeps
all four buildings, both label rows and the bright `1.75 m` scale figures in
frame.

Unity's batch setup completed with `CITY BUILDING UNITY ASSET BUILD OK`, and
the focused `BarPromenade.Tests.EditMode.CityBuildingAssetTests` selection
passed `3/3` (`Logs/city-buildings-editmode-results.xml`). The import proof
also fixed the nested-hierarchy axis handoff explicitly: Blender exports with
`bake_space_transform = false`, Unity imports with `bakeAxisConversion = true`,
and the four model-relative bounds now match the manifest after that one
conversion.

## 2026-08-27 — The production city keeps one bar beside the player home

The default coastal blueprint now requires exactly one Residential bar. It
keeps the authored home-frontage cell `(12,6)`, stable ID
`bar-01352777-12-06`, shared road anchor with the home at `(12,5)` and its
former `SplitTheG` dressing. The Industrial `(2,0)`, Nightlife `(8,0)` and Old
Town `(0,11)` sites return to the ordinary-building pool, leaving `121`
ordinary core buildings. Explicit test/authored layouts can still request
multiple graph-separated bars and keep the ordinal activity resolver.

One-bar selection now prefers a candidate with a real buildable lot directly
across its street frontage. This preserves the home/bar pairing for the legacy
generator across all four covered seeds rather than falling back to a merely
nearby home. The map names the destination from its Residential identity
(`The Ogonyok` / `«Огонёк»`), uses singular route copy and hides reorder arrows
when only one stop exists. Night lighting remains inside the exact 12-Light
budget as `1 bar + 11 street`, or `1 + 10 + 1` while a fringe practical leases
a street slot.

Focused EditMode verification passed `7/7`
(`Logs/single-bar-editmode-results.xml`): canonical layout, legacy default,
four home-pair seeds and the authored map label. The focused City bootstrap
PlayMode proof passed in the two-test run, and the corrected exact night-budget
reproduction passed `1/1` (`Logs/single-bar-night-rerun-results.xml`). Full
suites and a player build were intentionally not run.

## 2026-08-26 — The City misc layer moves to Blender end to end

The phased City pass is now one deterministic `city_misc_citywide_v3` source:
`61` semantic kinds, `91` assemblies, `177` role meshes and `32,642`
triangles. It replaces passive visible geometry across all 24 ordinary
decoration families and park landmarks, street lamps and traffic housings,
Route 01 shelters and poles, the eastern yard, cemetery, seacoast, fringe
service belt and the static shells of the four district points of interest.

`CityMiscAssetProvider` binds kind/variant/role to imported mesh sub-assets;
the existing builders retain their authored transforms and batching. World
plans still own placement and terrain, and Unity keeps collision proxies,
dynamic and interactive pieces, realtime lights and halos, cloth and NPCs.
Tilted cemetery monuments intentionally remain on the legacy visual path
because the current assembly contract is rigid.

The final manifest signature is
`3fff5efec42b67e97fe921c44bf22ec076523ae5dd6f0ddd87f6fd2a631c973a`;
its wave-one and v2 compatibility subsets remain frozen. The full Blender
export and the independent `--validate-only` pass both completed cleanly, and
`CityMiscAssetSetup` imported/rebound all `177` provider entries against the
same manifest without a contract error. The focused EditMode selection passed
`65/65` (`Logs/city-misc-v3-rerun-results.xml`); its fringe proof records the
expanded `144`-renderer budget against the built world's measured `129`.
The exact night-City PlayMode proof passed `1/1`
(`Logs/city-misc-v3-playmode-rerun-results.xml`). Its first headless run also
exposed an older native URP failure in the fountain cubemap path; the reflection
controller now skips `RenderToCubemap` only on Unity's Null graphics device,
while ordinary rendered play keeps the mirror unchanged.

## 2026-08-26 — The first Mountain Road misc wave moves to Blender

The first deliberately bounded misc migration replaces the visible runtime
primitives for fallen logs, stumps, dead trees, guard rails, snow poles, the
convex mirror, utility cabinets and the abandoned chair. Together those are
`102` of the default plan's `159` misc placements. One deterministic Blender
source exports `15` assemblies as `19` mesh sub-assets (`2,516` triangles):
three log variants, four stump variants, three dead-tree variants and the
single or multipart meshes required by the other five kinds.

`MountainRoadMiscAssetProvider` owns the import contract and deterministic
stable-ID variant choice. `MountainRoadWorldBuilder` combines the selected
meshes into exactly `12` renderers while preserving plan-authored transforms,
stable semantic roots, loose-object sound targets and renderless box collision
proxies. Dead-tree variants use uniform height scaling so their authored branch
proportions survive. Boulder, culvert, utility cable and tunnel lamp remain on
their legacy builders for later waves.

Verification covered the Blender direct validator and deterministic rebuild,
the Unity asset setup/import contract, and a focused EditMode run: `5/5`
tests passed, including the full opaque-surface sweep and a default-world proof
of all `102` migrated instances and `12` batches.

## 2026-08-26 — Four things the bar migration proved were missing

Not features. Each of these was paid for in the same session, in defects or
in wasted runs.

**Frames.** Three defects passed `1710` green tests and were caught only by
looking at a rendered picture. `Assets/Tests/PlayMode/AreaCaptureFixture.cs`
now photographs any world scene — city, bar, mountain road, home, stairwell,
supermarket, church — through the scene's OWN main camera, so the frame
carries the real lighting and the real post-processing rather than an
edit-mode approximation of them. The captures are `[Explicit]`, because they
are not tests and because running heavy scene-loading fixtures together
already tripped `ExitPlayModeTask` here; run one area per invocation. A frame
that comes out a single flat colour fails, since a folder of black rectangles
otherwise looks exactly like success. `BarCaptureTool` was absorbed and
deleted; one capture mechanism, not two.

**Domain reload off.** `m_EnterPlayModeOptions` was `0` — the fast-enter
feature was enabled but disabling nothing, so every Play paid a full assembly
reload. An audit of all `677` runtime files found `8` holding mutable static
state without a `SubsystemRegistration` reset, and no `static event` anywhere,
which is what made the change safe. Six classes got the hook
(`RuntimePrimitiveFactory`, `BarDrinkServiceResources`,
`HomeOcclusionResources`, `CityBusAudio`, `LocalizationService`,
`CityBlueprintCatalog`); `GameLog` was already routed through
`GameLogRuntime.ResetStatics` and only needed its session id and sequence
cleared; and two static fields added to `BarInteriorWorldBuilder` earlier that
day were removed outright, being a channel between two methods that should
have been a parameter.

**One working copy per session.** Recorded in `AI.md`. Sharing one checkout
between two agents cost three broken compilations, several aborted Unity runs
and two foreign red tests in every report — including once during this very
task. `Library/`, `Temp/` and `Logs/` are already gitignored, so
`git worktree` is sufficient.

**A full suite before "done".** The FAST budget in `ai/prompt-templates.md`
said not to run complete suites. That is what let a summit rebuild be reported
`81/81` green on a `MountainRoad*` filter while it had broken
`CityMapAreaPresentationTests` outside it. The focused check stays for
iteration; one complete EditMode run is now the condition for calling a task
done. Recorded alongside it: `-testFilter` is a regular expression, and `"Bar"`
matches the whole project through the `BarPromenade` namespace — always read
`total` back.

Deliberately not done: a golden snapshot of the city (valuable, but prevention
rather than a cost already being paid), and baking the city (the seed is
already a constant — `SetCitySeed` is never called outside tests — so there is
only one city already; `77 000` lines across `160` `City*` files is a separate
migration).

The capture fixture caught two defects in ITSELF, both only by looking, which
is the argument for it in miniature. The scene's main camera is the HERO's
camera, so his head filled the middle of every frame; his renderers are now
hidden for the duration and restored in a `finally`. And the first City frames
were photographs of the inside of a wall, because the camera positions had
been invented rather than measured. That second one was fixed by mechanism
rather than by tuning numbers: a shot may now be declared as an offset in the
hero's own frame, which is correct in any scene without measuring it, and that
is what every area except the bar uses.

Verified: `1705/1716` EditMode. Eleven failures, none of them from this work —
nine share the verbatim cause `The City misc provider requires exactly 177
mesh entries` from the concurrent city-decor migration (whose own test also
reports `city_misc_citywide_v3` expected against `city_misc_all_decor_v2`
present), and the remaining two are the same two church failures, with the
same numbers, that were already red before this task began.

PlayMode `11/15` across six fixtures, the four failures being the same misc
provider again — every one of them a City-scene test. Those eleven passes are
the real check on the domain-reload change: each test enters play mode again
inside one editor session, so a static that failed to reset would surface on
the second entry rather than the first. One honest gap: because the City scene
cannot currently build, City-specific statics were not exercised, and that
part of the change is unverified until the concurrent migration lands.

## 2026-08-26 — The bar moves to Blender, inside and out

The bar was `89` `RuntimePrimitiveFactory` calls across `1969` lines. It is
now two authored models — an interior of `156` parts and `8484` triangles,
a facade of `15` parts and `420` — and `864` lines of placer. Not one
primitive remains in either builder.

Nothing about the room's layout changed. Every dimension is the one
`BarInteriorLayoutPlanner` publishes or the one the primitive it replaces
used; the manifest records them and `BarModelContractTests` asserts the two
still agree. What changed is what the geometry is made of. Edges are
relieved, so a corner catches light instead of drawing one black line. The
doorway is framed as piers and a lintel and has reveals. Cups, shades,
glasses and tap handles taper. Bottles have necks. The counter has a
recessed plinth and an overhanging top. The curtains hang in folds, the
industrial dressing is pipes rather than flat cards, the jukebox crown is an
arch, and the room meets its floor at a skirting.

`tools/interior_kit.py` is the reusable half and the reason this was worth
doing: wall runs framed around their openings, swept mouldings, chamfers,
panelled leaves, turned legs. It holds no bar-specific value and is meant for
the apartment, stairwell and supermarket next. `tools/bar_parts.py` holds the
Unity-space authoring helpers, so the generator writes the same numbers the
plan publishes instead of re-deriving each one by hand.

Three defects, none of which review would have caught.

The axis mapping was wrong: `bakeAxisConversion` swaps Y and Z without
negating, so the door landed in the opposite wall and the counter station
`9.5 m` away. Caught by asserting the model's anchor against
`plan.CounterStationPosition`.

Every cylinder in the room, and the skirting, had reversed winding: the same
`(cos, sin)` traversal runs the opposite way in XZ from XY. Inverted normals
are invisible in a wireframe, in a triangle count and in every dimension
check; they show up only under a light. `signed_volume` now checks each solid
at generation time.

Worst: an imported FBX splits its unit conversion across the hierarchy — the
authoring root arrives scaled `100` and every part scaled `0.01`. The placer
flattens parts up into the room so `room.Find("Small Stage")` keeps working,
and doing that without preserving world transforms dropped the root's factor.
The entire room became a hundredth of its size while keeping correct anchors,
correct collision and a correct manifest, because none of those come from the
meshes. It surfaced only through a test asserting that a district's wall
dressing is big enough to read at `640x360`. `BarAssetSetup` now measures the
imported model against the manifest's bounds.

Pipeline copied from the church: `-- --validate-only`, a SHA-256
`build_signature` covering geometry AND the UV pitch table, `BarModelImporter`
(`materialImportMode = None`, `addCollider = false`), and `BarAssetSetup`
building both prefabs and refusing colliders, lights, cameras, rigidbodies and
animators in either. Materials are not assets: two shared materials, lit and
emissive, carry all `171` parts, and the sheet, district tint, smoothness and
metallic arrive in a property block exactly as `BarSurfaceAppearance.Apply`
delivers them to a primitive. That is what keeps district tinting working, and
the model declares WHERE each tint comes from rather than the runtime carrying
a sixty-case table.

Collision stays authored: the manifest declares a box per collider, and they
are the boxes the primitives had, so traversal is unchanged while the visible
geometry is free to be re-cut. The facade is authored once facing `+X` and
turned to face its lot, replacing two hand-written size triples per part that
were the same box rotated ninety degrees.

Also fixed here: `CityMapAreaPresentationTests` still expected two mountain
terminal landmarks after the summit rebuild added the brink as a third.

Verified: generator validates and runs twice to identical manifests; `57` bar
and facade EditMode tests green. Two failures remain in the tree from the
concurrent church work and were not touched. Nothing here has been seen
rendered.

## 2026-08-26 — A Catholic church north of the cemetery

The eastern open land now has a dedicated `4 x 2` Church precinct directly
north of the cemetery, while the remaining north-east Yard stays a rectangular
`4 x 4`. The church selects one west Street frontage after the ordinary road
graph is complete, so it does not perturb the seeded road network. Its central
west door, approach, interaction dock and City return are all derived from the
same typed exterior anchor; the model keeps at least `5 m` clear of the
cemetery and has no gate through the cemetery fence.

The building is an explicitly Roman Catholic provincial neo-Gothic church,
not an Orthodox variant: a `44 x 23 x 32 m` basilica with a tall bell spire,
Latin cross, buttresses, lancet windows, a rose window and pitched roofs. The
separate interior contains an open narthex, nave and side aisles, four piers,
ribbed vault, twelve pew halves, confessionals, a font, votive stands, a
supported choir loft and organ, plus a sealed sanctuary with communion rail,
altar, tabernacle and crucifix. The protected player routes keep a measured
`2 m` clearance and the sanctuary remains physically inaccessible. City-map
arrival also subtracts the church footprint, so it cannot place the player
inside the exterior collider.

One deterministic Blender source exports independent exterior and interior
FBX payloads and accepted previews. Unity builds passive typed Resources
prefabs from their shared manifest; gameplay plans, not the FBX files, own
colliders, navigation, lighting, entry and return. The normal door-action flow
now opens the appended `ChurchInterior` scene through `DoorTransition`, and
exiting returns the player to the same exterior frontage.

- Verification: Blender direct church validator passed (`6,412` exterior and
  `8,804` interior triangles); Unity `ChurchAssetSetup.RunBatch` imported and
  validated both prefabs; the focused rendered PlayMode door/scene round trip
  passed `1/1`. `git diff --check` was clean apart from existing line-ending
  warnings. Complete suites and a player build were not run.

## 2026-08-26 — The summit stops being a turning circle with two things on it

The terminal plateau was built in one pass in August and the work log
called it an MVP at the time: a `42 x 27 m` polygon carrying a turning
circle, a cafe on the left, a cable station on the right and nothing
else at all. It is now a transfer yard — the place where the road ends
and the cable starts — with a raised terrace, a cut edge, and a view.

**The composition came from a measurement, not from a preference.** A
throwaway probe swept every ridge and all `146` trees from a candidate
eye on each rim. The ground already rises east (`+5.6 m` inside `45 m`)
and falls west; and between `-44` and `-10` degrees off the back rim
there is nothing at all inside the area's `120 m` far plane, while the
ridges stand shoulder to shoulder on either side of it. So the cut face
went on the east rim behind the cableway, the terrace closed the back
rim, and the opening was aimed at the gap the ridges already leave. No
ridge had to be moved, which matters: the validator requires at least
six mid and ten far-snow, and there are eight and twelve.

**The brink is a terrain mask, not authored rock.** The first plan said
not to touch `MountainRoadTerrainSampler`; that was wrong. The terrain
is one continuous `1.6 m` grid over the whole area, so its macro plane
would have been drawn straight through any cliff hung off the rim. The
cut follows `ApplyBridgeGorge`: a wedge from the rim, `-27` degrees,
`9` degrees plus `3` of taper, taking the ground down `26 m` — roughly
to the height of the tunnel he drove out of. It is applied to the FINAL
returned height, after the plateau's own exterior blend, because
applied earlier that blend lifts the cliff back to pad height over
exactly the twelve metres the cliff is made of. The interior early
return still answers first, so the pad, the road seam and the surface
the car drives on are untouched by construction.

**`MountainRoadViewCorridor.DepthInside` had a real bug, found by its
own validator.** A point beyond the far arc reported its radial
shortfall as if it were a lateral clearance, so a route sample `142 m`
away was reported as standing `9.75 m` from the edge of the cut. It now
answers the true distance to the sector.

**The site is `85` parts in ten batches and adds no sheet.** Every style
resolves to one of the fifteen surfaces the mountain already prints or
borrows AND to a tint that surface's manifest already carries — a
borrowed sheet's albedo compensation is fitted to the tints that
multiply it, so a new colour would have meant re-solving that fit.

**The connectivity pass is the test that earns its keep.** The
retaining wall is the first thing in this world able to cut the
terminal in two, and neither the walkable mask — a polygon that knows
nothing about furniture — nor any existing validator would have
noticed. It floods heights rather than a blocking flag, so a `0.66 m`
wall stops the fill and the three `0.22 m` risers through it do not,
which is the distinction the player's own `0.28 m` step offset makes.

**Two things the tests deleted rather than fixed.** The painted
shoulders framing the view were occluded by the real ground at their
own lateral offset — so they went, because the walls of the cut are
real snow under a real sun and a matte of them behind them was both
hidden by them and worse than them. And the worn paths across the yard
went too: the plateau slab is already asphalt, so there was no snow for
a path to be worn through.

**He speaks up here now, and only speaks.** A repertoire, not the
island's menu: that menu's second option is "leave the city?" and its
whole execution path drives boarding and the ride stage, none of which
means anything six hundred metres above the city. The stub is
`SeacoastFishermanInteraction`'s contract in the same trigger box and
the same dock the factory already built.

Also: a bench on the terrace and the cafe's middle empty stool, both on
the shared city sit offer with no new clips, kind or prompt; the stool
row's geometry published as constants so the offer and the timber
cannot disagree; the attendant noticing somebody sit down, through the
tableau's own scheduler rather than around it; one mercury practical
over the freight dock, owned by the atmosphere beside the tunnel lamp;
four more causal sounds; and a third map landmark.

- **Not done, deliberately:** the cafe sign is a blank enamel board.
  Lettering needs two new glyphs in the shared `CitySignLettering` and
  is worth its own change.
- **Found and not fixed:** the counter stools sit at `0.4675 m` under a
  `1.02 m` counter. That is `0.3 m` low for a bar, but the three silent
  patrons are already posed on them, so raising the row moves the cast
  and belongs to its own pass. The hero sits at their height.
- Verification: the focused EditMode `MountainRoad` selection, `81/81`.
  Complete suites, a player build and a rendered smoke were not run.

### The drive was hanging in the air, and the yard had nowhere to go

**Nothing held the cableway machinery up.** The bullwheel is a `3.1 m`
disc standing `4 m` over the pad and `4.5 m` FORWARD of the station
centre — outside the canopy footprint altogether — and the reducer is a
`1.35 x 0.92 m` box at `3.55 m`. A drive shaft ran between them, which
tied the two to each other and neither of them to the ground. It now
has a bearing pedestal from the pad to the hub with a housing under the
disc and a concrete foot, four struts tying that outrigger back to the
frame it stands proud of, and a machine deck slung between the two rear
columns for the gearbox to stand on. Boarding happens under that deck,
which is what a lower station looks like.

**And the yard got a privy.** Single seat, plank, in the north-east
pocket between the cable station and the cut: downwind of the working
side, out of sight of the arrival, nowhere near the cafe door. Skids, a
board floor, three walls, a lintel with jambs, a door ajar at `26` in
two leaves with a slot between them — the cut-out every one of these
doors has, made of the gap because nothing here can cut a hole. The
roof STEPS rather than slopes: a site part carries a yaw and no pitch,
and four boards falling six centimetres each read as a mono-pitch at
this resolution while staying inside the batch. Inside it is the
apartment bathroom's own porcelain pan, set INTO the bench rather than
standing on it, built out of batch beside the cloth and the chains
because the batch does only boxes. Somebody carried a real pan up six
hundred metres of switchback and bolted it through a board, which was
easier than getting a new one.

**Note against the new Blender rule:** the privy predates it by about an
hour and is exactly what it now covers — a new building made of runtime
primitives. It is the obvious first candidate for the generator
pipeline, and small enough to be a good one.

### Corrections after the first look at it

Three reported, and the third turned out to be the serious one.

**The cafe threshold was crowded.** A bin `0.9 m` from one jamb, an ash
post `0.5 m` from the other, and the fourth body of the plough bank
reaching `right -15.6` — inside a doorway spanning `-17.0` to `-15.4`.
The bank got there because the yaw of its last two bodies was MIRRORED
off the rim they were meant to lie along: that edge runs at `42` degrees
in this frame and they stood at `52`, across it. The fourth body is
gone, the furniture is off the threshold, and a new
`CheckApproachesStayClear` holds a box off the cafe door and both seat
docks. It is a different question from the flood fill, which walks a
`0.25 m` grid with no capsule inflation and therefore reads a two-cell
slot between a bin and a snow bank as passable. Written naively the new
rule also reported the bench's own planks as blocking the way to the
bench, so an approach now starts PAST whatever forms it, and a seat's
runs `1 m` rather than `3` — the parapet a metre in front of the brink
bench is not in its way, it is the point of it.

**The lighting was set on the wrong scale, and that was mine.** This
area's fixtures run `1.65` to `16`; the documented CITY practicals run
`31` to `240`. Reading the yard lamp off the city list put it at `38`,
three and a half times the brightest thing on the mountain. It is
`9.5` now. The cafe also threw no light at all outside itself — both
its lamps stand indoors with `8.2 m` and `5.8 m` of range from `3.8 m`
up, so the cone never left the building and from the yard the place
read as a glowing box rather than something to steer towards. It has a
third fixture now, OUTSIDE the fascia at the glazed chamfer, washing
the doorstep, the parked car and its own walls; light on a wall is what
says "building" at forty metres. And the cable station ran at `1.65`
beside a cafe counter at `10.5`, which made the pair a lit window and a
night-light: it is `7.2` now with a second flood on the outer canopy
edge reaching the freight kerb, because one lamp under a canopy lights
only what it hangs over.

**Everything placed off the pad was twenty-six metres in the air.**
`MountainRoadTerminalPlanner.LocalToWorld` takes an OFFSET for its
`up`, and the cloth, the chains, the yard lamp and the brink bench were
all handed `yardTop + something` — a height added to a height. Nothing
saw it: not the flood fill, not the connectivity pass, not the seat
test, because `CityBenchSitPlan` takes a plank dock's height from
`GroundY` rather than from the seat, so the dock was correct, the
prompt appeared, and sitting down would have thrown the hero
twenty-six metres up. What caught it was the new lamp test asking
whether the light and its own shade were still in the same place:
`25.98 m` apart. Every placement now goes through a `Point` helper that
sets the height ABSOLUTELY, the offset form is called from nowhere, and
two tests pin the class — the seat is checked itself rather than
through its dock, and every cloth anchor, chain end, practical and seat
must lie within `[yard - 1, yard + 12]`.

## 2026-08-26 — The hero waits for his own door, the way the Ferryman does

Reported as an asymmetry, and that is exactly what it was: "перевозчик
сначала открывает дверь, а потом садится; герой не дожидается и проходит
сквозь дверь".

Both men's CLIPS are authored the same. `CarBoardEnter` is `relaxed 0.0,
reach 0.10, pull 0.22, door_clear 0.34, seat_step 0.52, seat_settle 0.66,
seat_down 0.78, door_shut 0.90, seated 1.0` — a man standing still at the
handle for the first third of it. What differed was the ROOT.
`LastRouteFerrymanBoardingTimeline` holds the Ferryman's at
`TravelStartPhase 0.36`, after his leaf stands open. The hero's was driven by
`PlayerAnimatedInteractionPelvisTransition`, which had only two markers —
arrive at the waypoint, leave it — and therefore **started travelling on the
clip's first frame and arrived only on its last**. Measured: `0.905 m` off
the dock (ninety per cent of the way to the doorway) at `0.34`, the moment
the leaf finished swinging; and still `0.301 m` short of the seat at `0.84`
when the clip has him seated and pulling the door shut.

The transition now carries a HOLD and a SETTLE either side of its waypoint,
both defaulted to the old shape (`hold 0`, `settle 1`) so the bench, the bed
and the bus seat — none of which has a door to wait for — say nothing and
behave identically. The car seat names all eight as constants off its own
clip's keys, and the contract is tested against the LEAF's phases rather than
against the numbers, which is where it actually lives.

Outward is the same defect and got the same fix (hold to `0.24`, the leaf
having been shoved open from inside at `0.22`; settle at `0.94`), with one
deliberate asymmetry kept: the leaf is still closing while he walks away from
it, because that is what a person does and what the arm in `CarAlightExit` is
authored to do.

Verification: all three new door tests were run against the old constants and
go red with the numbers above. They measure a DISTANCE and report it — NUnit
compares a `Vector3` bitwise, and the first draft of these failed with
"Expected (0,0,0) But was (0,0,0)", which is the colour/MPB trap in a new
costume.

## 2026-08-26 — The car turns off at the tunnel instead of past it, and looks first

Reported as one fault and it was two, stacked, both of them invisible to
every assertion the departure already had. A probe run that dumped every
vertex of the finished road on the default seed named both in one pass; the
numbers below are from that run.

- **It drove `13 m` past its own turning and swung back through `135°`.**
  The route ended at `TryFindNearestNode(streetAnchor)`, and the forecourt's
  street anchor is `access.Center`, which `CitySurfacePlan` places at the
  MIDDLE of its frontage edge. Both ends of that block were therefore
  `13.60 m` away — equal to the centimetre — so "nearest junction" was a coin
  toss decided by `Dictionary` key order, and it came down on the far one.
  The lane ran west to `x=-25.4`, then a `12 m` diagonal came back east to
  the opening at `x=-13`. The planner now finds the nearest drivable road
  EDGE instead, drops a perpendicular foot onto it, costs both of its ends
  (route length plus the run back along the block, or the far end wins on a
  technicality) and stops the lane at the foot. Which end wins also decides
  which lane the car is in when it arrives, which is what makes this a
  give-way at all.
- **The one corner that mattered was the one corner not rounded.** `cut =
  Min(CornerRadiusMeters, |incoming|/2, |outgoing|/2)`, and the legs either
  side of the forecourt turn arrived pre-subdivided at `1.5 m` because
  `AppendStraight` cut as it built — so that corner got `0.75 m` against the
  `4.5 m` every street junction got, and the class comment already claimed
  the opposite ("Rounded first, then cut fine"). `AppendStraight` is gone;
  straights are single `Append`s, a new `Straighten` pass drops the collinear
  vertices the forecourt run carries (the street anchor and the tunnel floor
  step, which alone held the turn to a `2.75 m` cut), `RoundCorners` measures
  its angle on the ground plane the way `BuildTurnRates` already does, and
  `Subdivide` puts the `1.5 m` sampling back at the end.
- Result on the default seed: `289.1 m` in `52.6 s` → `266.4 m` in `48.0 s`;
  worst curvature `1535.6` → `23.3` deg/m, and the `23.3` is the pull-away
  off the parking lot, not a road turn — the two street turns now peak at
  `17.77` and `17.78`, which is the same corner twice. Worst lateral on the
  road `7.79` → `2.84 m/s²` against a profile willing to carry `2.2`.
- **And it now looks before it crosses.** The turn is a left across the
  oncoming carriageway and the pavement in front of the opening, so the
  planner publishes a `LastRouteCarGiveWayPoint` on the road it lays: a stop
  line `6.5 m` back up the LANE from the turning — not back along the finished
  road, because a point on the arc is already committed to it — which the arc
  shortens to `5.6 m` along the road, at `219.11 m`, where the car is still
  square in its lane. `LastRouteCarDriveModel` gained `SetHold`, which works
  as a speed ceiling and never as a clamp on distance covered, so a hold
  armed late costs the hardest stop the car has and a metre over the line
  rather than a frame in which the car is not where it was.
  `LastRouteCarGiveWayModel` is the pure wait-or-go; `LastRouteCarGiveWay`
  asks the live city, reusing Route 01's own rules — the same walker
  exclusions and the same predict-them-a-second-ahead as
  `CityBusDirector.ResolveObstacleState`, plus the bus itself swept forward
  along its heading, because at this junction the bus IS the traffic.
- **Two things an adversarial pass caught, both of them mine.** The turn's
  crossing STARTS at the car's own lane centre, and `CityBusPlanner` lays
  Route 01's links at the same `1.5 m` off the same crown — so a bus simply
  following the car swept through the crossing, read as traffic, and would
  have held him at the line for the whole `15 s` cap with nothing crossing.
  The sweep is now gated on direction, which is what was asked for in the
  first place ("на встречке"), and starts at the bus's TAIL rather than its
  reported middle, so a dwelling bus with eight metres of body across the
  mouth is not invisible. And dropping the pre-subdivision turned the tunnel
  mouth's `3 cm` throat lift from a harmless kink into a visible one: the
  forecourt ground and the tunnel floor were two vertices at the same X and
  Z, `BuildVertexForwards` averages a vertical segment into a forward pitched
  `45°`, and the car reared over three metres of road in first person right
  at the last beat of the city. It was survivable while the rounder left arc
  ends a centimetre and a half apart; at `1.5 m` it is a wheelie. The portal
  is now one vertex carrying the floor's own height, and the lift rides the
  approach as a `0.2%` grade. `CityDeparture_NeverPointsTheCarUpOrDownASlope`
  guards it — nothing else in this system can, because curvature is measured
  on the ground plane everywhere by design.

Verification: the three new planner tests were run against the OLD planner
(`git stash push` on that file alone, everything else kept) and all three go
red with the right messages — `1536 degrees per metre at 238,5 m`, `gets
8,2 m further from its own turning`, and no give-way declared. The give-way
model's own tests caught a real defect while being written: the free-run
branch fired on `!hasHeldBack` alone, so a crossing blocked the whole way in
never stopped the car at all.

## 2026-08-26 — The hero rode the mountain in the tunnel, and the climb can be skipped

Reported as "he gets out of the car with no animation". The clip was running
fine. **His drawn body was seventy metres away.**

`ResumeSeated` starts the mountain leg with `BeginLooping`, which is the
overload for a body that resumes a loop it never left and stands up where it
sat down — a bench. It sets `placeAtExitOnCompletion = false`, and that one
flag gates three things:

- `BindActionPelvisTarget(car.PassengerSeatAnchor)` returns `false`
  (`IsPositionedEntryOrLoopActive` → `if (!placeAtExitOnCompletion …) return
  false`), and `ResumeSeated` **ignored the return value**. So `actionHip`
  stayed at the pelvis point the tunnel solved, and
  `Player3DCharacterPresentation` pins `ModelRoot` to it absolutely, every
  frame. The capsule rode the car; the model stayed in the tunnel. Nobody saw
  it because the camera is his own eyes and his head is hidden by rig rule.
- `exitHip = standHipPosition` — the tunnel dock — and `RequestExit()` never
  rewrites it. So `CarAlightExit` played, correctly, six hundred metres away.
- The moving-platform `RequestExit(authoredExitPose, …)` that exists for
  exactly this refuses outright on the same flag.

New `BeginPositionedLoop(definition, actionHip, authoredExitPose, transition)`
starts a loop that OWNS the root; `ResumeSeated` uses it and now logs if the
anchor bind is ever refused again; `Interact` re-aims the exit at the dock as
the plan reads now (`RebuildPlanFromCar` has already re-solved it by then),
falling back to the plain overload. One `BuildDockPose()` builds that pose in
all three places, because every point in the plan is world-space and the car
moves.

**And the climb can now be cut short.** `F10` — one of the few genuinely
unbound keys; `E`, `Space`, `Enter`, `Escape` are spoken for many times over
and `F8`/`F9` belong to the debug window and the Home shortcut. The hint names
the key, in a corner label of its own: not the interaction prompt, because
`PlayerInteractor` rewrites that every frame and clears its timed channel the
moment input is taken away — which is the first thing a ride does.

The skip goes THROUGH THE BLACK. Six hundred metres in one frame is a glitch
in any framing — the mountain visibly changes shape around a car that did not
turn — so `TrySkipRide` moves nothing: it takes the screen down at `0.6 s`
(the tunnel's own `1.4` is a car being swallowed and is meant to be watched;
a player who has pressed a key is waiting), and `UpdateSkip` applies the jump
from inside `IsFullyBlack`, then brings the screen back at `0.8 s`.
`LastRouteRideFadeView` gained `FadeOut(float)`/`FadeIn(float)` for that.

The jump itself moves the DISTANCE and nothing else:
`LastRouteCarDriver.SkipToEnd()` → `model.Resume(0f, path.Length)`. What
follows is the ordinary arrival rather than a second one written for the skip
— the driver writes the pose, raises `Moved` (which is what carries the hero),
runs out of road and raises `Arrived`, so the seat re-solve, the springs and
the man at the wheel are all handled by code that already existed for a car
that drives the whole way.

Verification, and the first draft of it was worthless. `Alighting_ClimbsOut…`
was written as `while (harness.Seat.IsSeated)`, but the loop ends on the frame
the exit is requested — so it never ran an iteration, every maximum stayed at
zero, and **it passed against the very bug it was written for**. Rewritten to
walk a fixed 150-frame window (the exit is 24 frames at 12 fps = 2.0 s, clock
pinned at 1/60) with a liveness assert that his body left the seat at all.
Re-run against the old `BeginLooping`, all three now go red: `His body went
70,0 m from the dock`, `His drawn body drifts 70,00 m from the car it is
sitting in`, `The hero's drawn body was left behind the skip`.

The skip test samples the black at the TOP of each turn, while the skip is
still pending: the jump lands inside the controller's `Update` (order 320) and
the fade view runs after it at `400`, so by the time the coroutine resumes on
that frame the screen has already started coming back and a check made
afterwards finds neither the black nor the un-jumped car. Verified red against
an instant jump.

One test of mine was also wrong in KIND: the skip's Ferryman assertion pinned
his offset from the car to a centimetre and failed at `0.107 m`. That is not
him being left behind — unlike the two passengers he is re-solved every frame
from his own sampled driving pose, so his root moves against the bodywork
while he sits there holding the wheel. Measured by displacement instead: he
has to travel the jump his car took.

EditMode `1671/1672` (the one red is the pre-existing `homeyard-booth`),
PlayMode `187` passed / `1` skipped.

## 2026-08-26 — The headlights were emitting from inside the cabin

Second look at the pulled blackout, and the diagnosis in the entry below is
half wrong. The white blobs were never the road, and they were never really
about intensity: **the emitters sat `1.8 m` BEHIND the lens, which on this car
is the windscreen.** Both beams were shining out from inside the cabin, so
their `52°` cones opened across the bonnet, the A-pillars and the door card on
the way out. What the frame showed was a car lighting itself.

The setback had a reason and the arithmetic behind it was right — inverse
square makes the four metres ahead of the bumper about eleven times the pool
at fourteen, and pulling the source back flattens that to roughly four. It was
the wrong fix for it. The emitters now sit `LensStandoffMeters = 0.12 m` proud
of the lamp's own front face, measured with the world AABB's SUPPORT function
along the car's forward (`|d·e|` summed per axis) rather than `extents.z`,
which is only the answer when the car happens to face down Z. The `up * 0.10`
nudge went with it — it was lifting the source toward the bonnet line, which
is one of the surfaces that was being washed. The near-field hot spot the
setback was hiding does come back, and from the passenger seat it falls behind
the bonnet, which is where a real car puts it.

`BurningHeadlights_PointDownTheRoadAndRideTheSprings` now also asserts every
emitter is outside the car's drawn shell (measured off the renderers, halos
excluded), ahead of its centre, and within `0.25 m` of the lit face — "at the
lamps", not merely somewhere in front. Verified red against the old placement:
"'Headlight Beam Left' emits from inside the car's own bodywork."

A PlayMode capture across `0 / 2600 / 6000 / 11000` at two hours confirms the
self-lighting is gone: **blown pixels `0.00%` at every intensity**, where the
pulled build blew out at `2600`, and the road band rises monotonically
`0.008 → 0.042 → 0.068 → 0.093`. The intensity was NOT re-tuned from those
numbers and stays at `6000`: a manual `camera.Render()` into a RenderTexture
skips part of the post stack, so the capture came back far darker than the
editor's own view of the same scene — the project's standing rule that light
intensities are never tuned from a capture that lacks post-processing applies
in PlayMode too, not only in edit mode. What the capture is good for is the
geometric fact and the relative ladder.

Both suites green afterwards: EditMode `1670/1671` (the one red is the
pre-existing `homeyard-booth`), PlayMode `185` passed / `1` skipped.

## 2026-08-26 — The blackout is pulled; the headlights stay and burn harder

The entry below shipped a ride in which the mountain's sun, ambient,
reflection and fog were all taken out so the car's beams were the only light
in the world. In the editor it came up as a black frame with two blown-white
pools in it and the user pulled it on sight: "верни обычное нормальное
освещение которое было до изменений, просто сильно усиль свет от фар".

Reverted in full, and by `git checkout` rather than by hand so the two
lighting files are byte-identical to what they were before the experiment:
`RuntimeSceneSetup.cs` (the second `ApplyMountainRoadLighting` overload, the
`grade.DirectionalScale` factors, the fog writes and the `directional.enabled`
override), `MountainRoadAtmosphere.cs` (`BindRide`, `UpdateRideBlackout`,
`ApplyRideBlackout`, `ApplyRideGradeToVolume`, the camera clear colour and the
`EnvironmentRefreshStep` throttle), and `MountainRoadRideGrade.cs` is deleted
outright along with the two EditMode tests that pinned its window. Nothing of
the snow, the wind, the swaying crowns or the foliage shadows is touched —
they were the same session's work but not the same feature.

**The headlights stay, and they now own their own switch.** They were powered
by `headlights.SetPower(rideBlackout)`, so deleting the grade would have left
them permanently dark. `LastRouteCarHeadlights.Follow(ride)` polls the
controller's flags in its own `Update` and ramps `1.2 s` up / `2.5 s` down on
UNSCALED time; `MountainRoadRoot` wires it straight to `Ride` and the
atmosphere no longer knows the journey exists. That coupling only ever existed
because the thing putting the sun out was also the thing that would put it
back every game minute; with the sun staying up, a headlight is a switch on a
car again.

`BeamIntensity` `2600 → 6000` and `SpillIntensity` `130 → 300`. **Not
measured, and the comment says so.** The `2600` was measured, but against the
blacked-out world that no longer exists, and the editor held the project lock
for the whole of this change so no capture could be run — the doc comment
carries the method (main camera to a RenderTexture, read back sRGB, camera not
`0.6 m` behind the car) for whoever tunes it next. Note that reverting the
grade also restores the area's own bloom threshold `0.55 → 0.72` and vignette
`0.24 → 0.13`, so the pools bloom less hard at any intensity than the pulled
screenshot showed.

**Unverified by test run.** `dotnet build` is clean on all three assemblies;
no EditMode or PlayMode suite could be run, because `Temp/UnityLockfile` was
held by the user's own editor throughout.

## 2026-08-26 — The climb happens in the dark, in the wind, in the snow

**Superseded — the blackout half of this entry was pulled the same day; see
the entry above. The wind, snow and foliage-shadow work stands.**

The ride up the serpentine read as a daytime drive. Three separate things
were missing, and each one had a mechanical reason it could not simply be
switched on.

- **The headlights were never lights.** `LastRouteCarFactory` hung two halo
  billboards where the lamps are, on the stated ground that the night light
  budget belongs to the street masts. That holds in the city. On a mountain
  with no masts, no windows and no sun it left a bloom around a lamp that was
  lighting nothing. `LastRouteCarHeadlights` now adds two shadow-casting
  `46°` spots and one wide unshadowed spill on top — the halos are untouched,
  because the glow was never what was missing. The virtual sources sit
  `1.8 m` BEHIND the lens they shine out of: inverse-square otherwise makes
  the four metres in front of the bumper eleven times brighter than the pool
  at fourteen, and setting the source back flattens that to about four. They
  hang off the sprung body, so the beam dips under braking for free; the
  halos stay on the runtime root, which deliberately does not rock because it
  carries the obstacle collider.
- **The blackout had to live inside the per-minute lighting apply, not beside
  it.** `MountainRoadAtmosphere.Update` re-applies the exterior grade every
  time the game minute ticks, so anything written over it from outside would
  be wiped within a second. `MountainRoadRideGrade` is therefore a PARAMETER
  of `RuntimeSceneSetup.ApplyMountainRoadLighting`, and the atmosphere carries
  its weight. The directional is switched off outright rather than dimmed —
  URP then drops the main-light shadow map, which is what pays for the two new
  shadow-casting beams, and it can never promote a headlight in its place
  because only directional lights are eligible.
- **Killing the sun was half of it. The fog was the other half.** The area's
  fog is a pale grey-green `(0.265, 0.315, 0.300)` that is two thirds of the
  frame by forty metres — on its own brighter than anything two headlights
  will light. Kill the sun and leave it and the result is not a dark road but
  a grey soup with a bright hole in it. Fog colour, camera clear colour,
  ambient and reflection all travel on the one weight; density goes to
  `0.042`, which doubles as what hides the far ridges nothing is lighting any
  more. `DynamicGI.UpdateEnvironment` fires on the endpoints and a few times
  across the ramp, never per frame.
- **The trees needed a second URP Lit clone, and the reason is one vertex
  channel.** All 420 crowns are two cones each merged into one mesh per
  layer, so there is no transform to rotate. The four passes that must agree
  on a displacement — forward, shadow, depth, depth-normals — share exactly
  `POSITION` and `TEXCOORD0` between them: `ShadowCasterPass` and
  `DepthOnlyPass` declare no `texcoord1` and no `COLOR`, and `DepthOnlyPass`
  even names its position field `position` rather than `positionOS`. So the
  crown's V now measures height above THAT TREE'S OWN FOOT instead of above
  the world origin, which yields the bend lever, the tree's own altitude
  (`positionWS.y - aboveBase`) and, with the vertex's world XZ, a per-tree
  phase — all of it out of UV0. Altitude moved into the U phase, which is
  where the vertical decorrelation between neighbouring trees used to come
  from.
- **`Ps1LitFoliage.shader` wraps FOUR passes where `Ps1Lit` wraps three, and
  the extra one is ShadowCaster.** Snap and wind are not the same kind of
  thing. The snap is a projection-space artefact of the camera's own grid, so
  no snap in a shadow map can ever agree with it. The wind is an object-space
  displacement identical under every projection, so the shadow must carry it
  — a crown whose shadow stands still while the crown sways is the bug, and
  on this road it is the most visible one there is, because the headlights
  throw those shadows straight across the asphalt. `Ps1LitShaderParityTests`
  is now parameterized over both clones, with one extra fixture asserting
  three snaps and four bends in the foliage file.
- **Snow is the same schedule, not a new kind of weather.** `WeatherKind`
  gains nothing; `CityWeatherController` gains one optional
  `ICityWeatherShaper`, and `MountainRoadWeatherShaper` re-reads the city's
  own sample for a place that is higher. One hook rather than a second
  component, because the controller already writes the cloth registry and the
  precipitation drift every frame and anything else writing them would be a
  race decided by execution order. `CityRainField` is parameterized by a
  `CityPrecipitationProfile` rather than duplicated: a flake settles at about
  a metre a second, which forces ten times the lifetime, which forces the
  particle count up and the emission rate down, which is most of the table.
- **Altitude, never ride progress.** `MountainRoadWeatherRules` keys
  everything off world Y between the route's foot and its summit, so one
  number serves the car, the hero on foot afterwards, and every individual
  tree on the slope. The decision worth arguing about is the snow floor:
  `55%` of weather slots are Clear, so a snowfall that were nothing but the
  schedule would leave more than half of all rides dry — and the ride is
  taken once. The summit therefore snows at `0.55` even in a Clear slot. The
  sway amplitude the trees are driven with is deliberately UNCLAMPED up to
  `1.6`, because `WindSample.Strength01` clamps by construction and pushing
  the altitude gain through it would flatten exactly the case that should be
  worst.
- **The rain bed is gone from the mountain and a wind bed replaced it.** Snow
  is silent; what the climb sounds like is the wind driving it sideways.
  `MountainRoadWindSound` sits beside `CityRainSound` rather than inside
  `MountainRoadSoundSynthesis`, whose whole contract is that every clip
  belongs to a visible object — its snow-pole whine is a pole resonating, not
  air.
- **Shadows.** The far conifer layer now casts and receives (it stands
  `17-28 m` out, inside both the `50 m` shadow distance and the beam, and is
  the only thing between the light and the void), and the terminal apron
  receives (the car parks on it under its own beams). The far snowy ring stays
  off at `62 m`, and the haul cable stays off because `55 mm` of cable is
  sub-texel at `640x360`. Crown mesh bounds are expanded by `2.5 m` so the
  wind cannot bend a stand out of its own culling volume.

### Corrections after the first build came up black

Two defects, both in the lighting half, both found by looking rather than
reasoning.

- **The beams pointed at the sky.** `LastRouteCarSuspension` sets
  `sprungBody.localRotation = body.localRotation`, so the sprung body inherits
  the IMPORTED node's axes — and this car's imported forward is very nearly
  vertical. The lamps were parented there and aimed with a local Euler, so they
  burned at full power into nothing. The blackout had already put the sun out;
  nothing threw and nothing logged, and the scene was pure black. This is the
  seventh instance of the imported-basis trap in this project. Every axis now
  comes from the runtime root and the WORLD pose is written after parenting, so
  the spring still carries it.
  `LastRouteCarPlacementTests.BurningHeadlights_PointDownTheRoadAndRideTheSprings`
  is the guard that was missing.
- **The fog, not the ambient, is what makes a night legible here — and it had
  been set to near-black.** Distant geometry blends TO the fog colour, so the
  forest beside the road is not lit at all: it IS the fog, and in a captured
  frame it measures `0.088` against the road's `0.066` — brighter than the
  road. Taking the fog to `(0.020, 0.024, 0.028)` therefore deleted every tree,
  ridge and rail outside the beam. Sweeping `RenderSettings.ambientLight` from
  `0.13` to `0.28` moved that same forest from `0.0000` to `0.0003`, which is
  to say ambient is not a lever in this scene at all. Shipping: fog
  `(0.115, 0.135, 0.142)` at density `0.028`, ambient `(0.045, 0.052, 0.058)`
  as a floor only. The grade test now pins a WINDOW in both directions —
  too pale is grey soup, too dark deletes the mountain.
- **The beam intensity was twenty times short, and only a capture said so.**
  Arithmetic against the street masts (`31` over `16.5 m`) gave `110`; a mast
  lights a pavement eight metres below it and a headlight throws twenty. A
  throwaway PlayMode probe — real world, forced grade, main camera rendered to
  a RenderTexture, mean luminance reported — put the answer at `2600` over
  `58 m` at `52°`, which lands the lit road at `0.083` against `0.066` for the
  same view under ordinary lighting. Two traps inside the probe itself, both of
  which produced convincing lies: a Linear readback encoded as PNG reads about
  ten times too dark (use sRGB), and a camera `0.6 m` behind the car is inside
  the cabin. The probe was deleted afterwards.
- `additionalLightsShadowResolutionTier` throws outside play mode and takes the
  whole car build down with it; it is guarded by `Application.isPlaying`.

Verification: `MountainRoadRideWeatherTests` (new, pure — climb monotonicity,
the snow floor and its two-axis monotonicity, summit-versus-tunnel wind in
every slot, and the grade's identity at rest and blackout at full),
`MountainRoadSurfaceAppearanceTests` and `Ps1LitShaderParityTests` in the same
selection. The PlayMode ride suite and a player build were deliberately not
run.

## 2026-08-26 — The mountain road stopped being flat colour

The area was built entirely out of untextured tints. Six sheets are now
printed for it, nine are borrowed, and the unwraps that would have made a
sheet look worse than a flat colour were corrected first.

- **Six new measured albedos** — cold non-directional mountain asphalt, damp
  forest floor, wind-packed snow, coarse bedded stone, dark conifer needles
  and ridge-and-furrow bark — are generated, validated and hashed by
  `tools/build-mountain-road-textures.py` into
  `ArtSource/MountainRoad/mountain-road-textures.json`, and imported at `512`
  with Repeat and mips. The asphalt carries no wheel bands and no travel
  direction, because the road turns through ten hairpins and any directional
  wear would run across the carriageway half the time.
- **Nine kinds are borrowed rather than reprinted.** Concrete, iron, painted
  metal, masonry, linoleum, timber and wall paint already ship, so the
  bridge, cableway and cafe read those sheets — but a borrowed kind does NOT
  inherit its source family's compensation, which is fitted to the tints that
  multiply a sheet and not to the PNG. The tool measures each borrowed sheet,
  re-solves the constant against the mountain's own tints and refuses the
  build if a channel would clamp or brightness would move by more than `8%`.
  The city masonry under the cafe's brick tint needed `1.4465`, not the
  fringe's `1.3895`. Borrowed entries record the source SHA256, so a
  regeneration upstream is caught here.
- **`MountainRoadSurfaceAppearance`** owns all fifteen recipes and applies
  them through `MaterialPropertyBlock` on the one shared `RuntimePrimitiveLit`.
  Hand-built meshes bake metre-scale UVs at their recipe's pitch and take
  `ApplyCombined`; single primitives take `Apply` with an explicit projection
  wherever their proportions would pick the wrong face. No call to
  `renderer.material` and no new material instance anywhere.
- **Six unwraps were wrong and are fixed.** The road kerb now continues the
  carriageway's unwrap over its edge instead of squeezing three metres of
  asphalt into two centimetres of border, with no vertex duplication, so the
  road and plateau still share one entry vertex. The plateau and the terminal
  apron are unwrapped in the road's frame from the entry sample, so the
  texture crosses their shared seam unbroken. Soil and snow are cut from one
  vertex grid and now share one set of normals averaged over both triangle
  sets — separate `RecalculateNormals` calls were lighting the snow line as a
  seam. Ridges and boulders had no UVs at all and take the faceted box
  projection off their existing normals, so the lighting is unchanged. Crowns
  unroll by arc length against height, phased per tree. The bridge deck moved
  onto the single-box batch its girders and piers already use. The cafe's
  prism splits its cap and side UVs, which also gives the roof slab a crisp
  arris instead of a bevel.
- **Excluded on purpose, and the test enforces the list:** the tunnel dark,
  the culvert bore, the sagging cable and the haul cable, the flickering lamp
  lens, the sign's painted stroke, five sheets of glazing and six emissive
  parts. Anything else that ships without a sheet fails the sweep — which is
  how `Entrance Header` was caught before it shipped flat.
- **An adversarial review of the diff caught one defect of my own:**
  `CreateStoneMesh` baked every ridge and boulder at the layered-stone pitch,
  but `CreateRidges` also builds the far snowy ring, which wears the wind-snow
  sheet — so that ring tiled a fifth too coarsely for its own recipe. The
  factory now takes the kind it is being baked for, and each caller names that
  kind once and hands the same value to both the bake and the recipe, so the
  two cannot drift apart again.
- **Found by the review, NOT fixed here, because it is geometry:** both road
  kerb quads in `MountainRoadSurfaceMeshFactory.AppendRibbon` are wound
  facing the road centreline. With `AddQuad(a,b,c,d)` emitting `(a,b,c)` and
  the repository's `normal = Cross(b-a, c-a)` convention — which the road's
  own top quad and the plateau's skirt both confirm — the left kerb comes out
  `+Right` and the right kerb `-Right`, i.e. both inward, so `Ps1Lit`'s back
  culling drops them. The winding predates this pass, and the corrected kerb
  UVs are right either way (`halfWidth + SurfaceThickness/tile` puts exactly
  `0.18 m` of sheet across the `0.18 m` drop), but they land on faces that are
  never rasterised except where the plateau's correctly wound skirt reuses the
  same two vertices. The fix is one line — swap the two kerb `AddQuad`
  argument pairs — but it changes what the road's silhouette draws, which this
  pass reserved.
- **Also known and left:** the plateau's first and last skirt faces reuse the
  ribbon's straight-U kerb offset instead of the radial one, so two of its
  sixteen `0.18 m` skirt faces sample a `0.019`-`0.143 m` band rather than the
  full `0.18 m`; separating them would break the road/plateau shared-entry-
  vertex contract that `MountainRoadTests` pins. The terrain keeps its
  XZ-planar unwrap, so the gorge walls stretch vertically the same way the
  City's own terrain does; a slope-aware parameterization would change tiling
  density across the whole `76 m` envelope.
- **Verification:** `python tools/build-mountain-road-textures.py --verify`
  passed all six printed sheets (worst brightness error `5.8%`, worst seam
  ratio `2.10x`, contrast `44`-`83`) and all nine borrowed contracts (worst
  error `7.5%`). One focused Unity EditMode invocation over
  `MountainRoadSurfaceAppearanceTests|MountainRoadTests|MountainRoadTerminalTests|MountainCablewayTests|MountainRoadCafeCastTests`
  passed `36/36`; the first run of that same filter was `35/36`, red on the
  untextured `Entrance Header`, which is the evidence the sweep is wired to
  the defect it exists for. A wider invocation adding `LastRouteRideTests`,
  `RuntimePrimitiveFactoryTests` and the four borrowed families' own
  appearance fixtures passed `126/126`, which is what proves the borrowed
  sheets were not disturbed. After the ridge-pitch correction the final run
  over the six mountain and Last Route fixtures passed `52/52`. Broader Unity
  suites were not re-run: the deterministic contract is covered by the tool's
  own validator and these fixtures.

## 2026-08-26 — The arrival threw the passenger out of his own car

Two reports, one cause, and the second one hid the first.

- **`MountainRoadRoot.Awake` runs INSIDE the area transition, not after it.**
  `AreaTravelService` sets `allowSceneActivation = true`, the destination
  scene wakes, and the coroutine then keeps yielding on
  `while (!destinationOperation.isDone)` before `Complete` finally clears the
  flag. Through that whole window `AreaTravelService.IsTraveling` — and
  therefore `SceneTransitionService.IsTransitioning` — is still **true**, and
  `PlayerAnimatedInteractionController.Update` force-completes any running
  interaction while it is. So the mountain arrival seated the hero and the
  very next `Update` tore it down: he was dumped on the tunnel floor at the
  exit pose and his car drove the six hundred metres to the cafe without him.
  `LastRouteRideController.CreateForMountain` now only ARMS the leg;
  `AwaitMountainStart` holds it — under a screen already painted fully black —
  until the service has genuinely finished, which is a frame or two.
  **The general rule: a destination root's `Awake` is not "after the load".**
  Anything that starts an interaction, or anything else that
  `IsTransitioning` tears down, has to wait for it to clear.
- **And that is why the map was dead.** The chart was gated on the ride stage
  being `InTransit`, which was meant to be transient — but with the hero
  ejected, nothing ever advanced it, and a gate on *opening the map at all*
  turned one bug into "the player has no map". The gate was too blunt anyway:
  reading the chart while the car drives is worth having, and watching your
  own marker climb the mountain is a small gift. `CityMapController.Open` no
  longer consults the ride; what refuses instead is the three things that
  would actually MOVE him — `ConfirmDebugTeleport`,
  `CanTeleportToSelectedMapPoint` and the private `RequestAreaTravel` — all
  through one named predicate, `GameSessionState.IsRidingTheFerryman`.
  **Gate the action, not the window onto it.**
- **Verification.** `Ride_WaitsForTheAreaLoadBeforeSeatingHim` reproduces the
  window rather than approximating it: it drives
  `AreaTravelService.IsTraveling` through its private setter by reflection,
  holds it true for twenty frames while asserting the hero is neither seated
  nor ejected, releases it, and then checks he stays seated for thirty more.
  Both it and the deferral assertion in
  `Ride_CarriesTheHeroAndOnlyLetsHimOutWhenItStops` were confirmed RED against
  the old one-line `CreateForMountain` before the fix went back in, and both
  named the defect. Full suite below.

## 2026-08-25 — The Ferryman drives, and the tunnel finally goes somewhere

- **What existed already did most of it.** `MountainRoadRoutePlan.Sample` is a
  centreline parameterised by arc length with the drivable surface height in
  it, and `TunnelToCafe_IsOneUnbrokenDrivableSurface` has been asserting a
  `1.05 m` half-width corridor over all `620 m` since the terrace moved. So the
  mountain leg is that route read out at a metre into one polyline, plus a
  lead-in from inside the tunnel and `5.5 m` onto the apron - and it inherits
  the proof. `AreaArrivalToken.Tunnel` had sat unused in the enum since area
  travel shipped; `Ferryman` is its first real caller.
- **The city leg routes on the LAYOUT's edges, and the obvious choice was
  wrong.** `CityBusPlan.Nodes`/`.Links` look like the city's street graph -
  baked turn arcs, lane offset applied, clearance-swept for a body `8.25 m`
  long - and they are not. They are Route 01 itself: one closed, directed,
  right-hand circuit. Routing the departure on it meant only ever going the way
  the bus goes, and the first measurement came back **`4842 m` and over ten
  minutes** - two hundred and ninety-one of the loop's three hundred and
  forty-six links, eighty-four per cent of the way round `5.6 km`, to reach a
  portal `170 m` away. Every geometric assertion in the suite passed while it
  did; what caught it was a test that simply drove the path and printed the
  clock. `CityLayout.RoadEdges` is the real grid, undirected and with no
  timetable: `289 m` in `52.6 s`, and the giveaway in hindsight was
  `Links.Count == Nodes.Count == 346` against a reported
  `ClearanceAcceptedLinkCount` of `1218`. **Measure the thing the player
  experiences, not only the geometry of it.**
- **The lane and the corners are this file's own.** Junction centres are
  pushed `1.5 m` into the right-hand lane along the bisector of their two
  segments (mitred, so the two halves of a corner meet), each square junction
  is cut into a `4.5 m` arc, and only THEN is the whole thing subdivided at
  `1.5 m` - rounding after subdividing would cap every cut at half a short
  segment and leave the arcs barely bent. The two ends are the pieces no
  street graph knows: the pull-away off the lot (a quadratic through a control
  point out along the car's own nose, so it leaves the way it is pointing) and
  the forecourt corridor, which `CityFringeYardPlanner` already keeps clear as
  `DriveClearBounds`.
- **Speed is a forward sweep of a backward pass.** For every vertex inside the
  braking horizon, work out how fast the car may be going here and still be
  down to that vertex's cornering speed on arrival, and take the lowest answer.
  That is what makes it lift off BEFORE a hairpin instead of discovering it
  from inside one. The `R7.5 m` bends pull it to about `3.5 m/s` on their own,
  with no authored slow-down anywhere.
- **Three things would have shipped broken and none of them throws.**
  `LastRouteCarSuspension` cached its rest pose as a WORLD point, which was
  free for as long as the car never moved and would have left the bodywork
  standing on the island while the wheels went up the mountain; it is in the
  root's own space now. `LastRouteFerrymanPresentation` solved the driving seat
  once at `Initialize` and then stopped writing his root at all once the
  boarding timeline finished, so he would have stayed at the world position
  that solved him; the seat is re-derived every frame from the same two drawn
  anchors. And `LastRouteCarSeatPlan` is entirely world-space and was worked
  out against a parked car - six hundred metres and twenty-six metres of
  altitude later `CanInteract`'s own vertical-tolerance check against
  `plan.EntryRootPosition.y` would have refused to let the hero out at all, so
  the seat re-solves its whole plan when the car stops.
- **The way back out needed no new animation.** Every one-shot in the library
  is authored to END on the base pose of the clip the runtime crosses into, so
  played backwards it BEGINS there - which is exactly what a reverse beat wants
  at its seam. `FerrymanBoard` and `FerrymanDismount` run in reverse by written
  `SetTime` on clips parked at speed zero (no negative playable speeds), and
  the door curve is symmetric enough to reuse verbatim at `1 - progress`. Only
  the trudge is different: the PATH is reversed and the walk cycle still plays
  forwards, because a walk run backwards is a man moonwalking round a car.
- **The car parks nose-in and deliberately does not use the turning pocket.**
  The apron is a turning circle and the temptation is obvious, but the cafe's
  nearest corner stands `8.24 m` from the apron centre against a validated
  clearance of `TurningRadius + 0.55 = 8.05` - nineteen centimetres - and a
  U-turn of any usable radius sweeps through either the cafe or the cableway
  station. `MountainPath_NeverDrivesThroughTheCafe` probes the body's own
  half-width to either side of the last forty metres and holds that.
- **No new gate was needed on the tunnel refusal.**
  `CityTunnelTravelController.CanEngage` already requires
  `Motor.InputEnabled`, and a seated passenger has none - so the walk-back
  boundary simply never arms for a hero in a car. The map did need one:
  `CityMapController.Open` now refuses while the ride stage is `InTransit`,
  beside the two clauses it already had for scene transitions and area travel.
- **Two more that only a running frame loop could have found.** The hero was
  written from the seat's own `LateUpdate` and sat exactly one frame's travel -
  `8.7 cm` at tunnel-exit speed - behind the car on the frame the engine
  started, because a component added during a scene build can have its first
  `Update` deferred against one that already existed. He is now written from
  `LastRouteCarDriver.Moved`, in the same call as the car, where there is no
  ordering to get wrong. And the arrival re-solved the seat plan *after*
  handing the hero his `CharacterController` back - so the plan's ground probe,
  which raycasts down at the dock, hit **him** and put the entry root `1.61 m`
  up; `CanInteract`'s vertical tolerance then refused to open the door at all.
  The whole ride worked and ended with the passenger sealed in. The re-solve
  now happens before the controller comes back.
- **Verification.** `LastRouteCarDriveTests` + `LastRouteRideTests`: 24 passed,
  including the real default-seed city path (starts at the parked car, ends
  `15 m` inside the portal, no seam over `2.5 m`, no corner the car cannot
  take, and the drive clock pinned to `35-75 s`) and the mountain path walked
  against `MountainRoadWalkableArea` at car half-width and probed to either
  side of the last forty metres for the cafe. Regression over the seven
  existing `LastRoute*` EditMode classes plus `GameSessionStateTests`,
  `AreaTravelContractTests` and the two map-area fixtures: 150 passed.
  `LastRouteFerrymanPlayModeTests`: 9 passed - the whole boarding beat still
  works under the presentation changes. `LastRouteCarRidePlayModeTests` is new,
  runs the mountain arrival end to end, and both of its failures above were
  real defects it named precisely.
- **Still missing, and worth saying plainly:** the car is silent. The bus has
  `CityBusAudio`; this has nothing, so a `620 m` climb plays out with no engine
  under it. That is the obvious next piece and it is a soundscape job rather
  than a driving one.

## 2026-08-25 — The cafe terrace was parked on top of the last switchback

- **The report was one coordinate, and it was exact.** `X 127.5 Z -4.5` is the
  apex of hairpin `8`, the last switchback before the road turns back down to
  hairpin `9`. The road ribbon is drawn there perfectly; what stops the car is
  the terrain. `MountainRoadTerrainSampler` snaps every point inside the
  terminal plateau to the pad height, and the pad's rim ran from `X 129` down
  through `(130, -9.6)` and `(135, -14.1)` — straight across the outer arc of
  that hairpin. From `500 m` to `512 m` of route distance the ground sat up to
  `1.54 m` ABOVE the asphalt, with a `MeshCollider` on it. The road was still
  there, under the snow.
- **Nothing was checking for it, which is why it shipped.** Trees, roadside
  props and backdrop ridges all have validated road clearance; the plateau had
  none. The route grew from `82.7 m` to `600 m` in `458d4de` (carried in as
  unverified neighbouring-session work), the switchback field grew with it to
  `X 138`, and the `42 x 27 m` pad centred on `X 150` reaches back to `X 129`.
  The existing corridor test only asked the WALKABLE MASK, which is built from
  the ribbon and answered yes the whole way up.
- **The pad moved, along the road, and the climb did not change by a
  millimetre.** `TerminalTerraceRun = 20 m` is appended as a level
  `UpperApproach` run before the `5 m` entry lead, and `ClimbLength` subtracts
  both, so `EvaluateElevation` still divides by `595` — every sample up to
  `595 m` keeps its exact former position, width and grade, and the hairpins,
  the bridge and the gorge are untouched. Only `route.End` slides `+20 m` in Z,
  and the pad, cafe, cableway and apron ride along with it because they are all
  authored in pad-local coordinates. `OutdoorRouteLength` is `620 m`; the walk
  is `238.5 s`. Pad rim to road edge went from `-3.10 m` (three metres INSIDE
  the ribbon) to `+8.69 m`, and the bank beside the hairpin is now a `1.5 m`
  rise over `8 m` of ground instead of a wall at the kerb.
- **The invariant is now enforced, not just satisfied.**
  `ValidateTerminalPadClearsTheClimb` holds every plateau rim edge `4 m` clear
  of every route segment that is not part of the terminal approach, using the
  validator's existing segment-distance helper — `16` edges against `620`
  segments, cheap enough to run on every plan.
  `TunnelToCafe_IsOneUnbrokenDrivableSurface` is the behavioural half: it walks
  all `620 m`, probes nine lanes across the full ribbon width at every sample
  and asserts the ground stays at least `0.1 m` below the driving surface, then
  drives the car corridor from the tunnel through the apron to the cafe door.
- **Verification.** Proved the new test is wired to the bug: reverted the
  planner, disabled the new validator hook, re-ran, and it failed with
  `The road is buried at mountain-route-504 (500.2 m, Hairpin) near X 130.3
  Z -1.7: the ground sits at 25.86 and the surface at 24.32` — the user's spot.
  Restored, then `MountainRoadTests`, `MountainRoadTerminalTests`,
  `MountainCablewayTests`, `MountainRoadCafeCastTests` and
  `CityMapMountainPresentationTests`: `23/23` green. Full EditMode suite run
  afterwards because a route-length constant reaches every mountain consumer.

## 2026-08-25 — The hero gets into the car the way the Ferryman does, and rides it from inside his own head

- **The light was in the wrong place, and it was the second wrong place.** The
  Ferryman's lamp began as a bare Point with no fixture, parented beside him
  and rewritten every frame; that was replaced by a second head on the
  island's route mast, which fixed the fiction and broke the light. The mast
  stands beside the paving circle, the bay is fitted per seed up to `7 m` away
  and the man on the bonnet faces OUT at the way in — so the throw arrived
  from behind him across ten metres of inverse square. It now stands on a
  `3.30 m` post of its own, fitted in front of the car and turned back down
  the bonnet: night `45` / day `15` over `9 m` at `44` degrees, roughly `3.7 m`
  of slant range instead of ten, calibrated against the drying yard's
  floodlight rather than guessed.
- **The post is fitted, not authored, because the bay is.** `TryDescribeFerrymanLampStance`
  walks a ladder of places ahead of the car and REJECTS rather than nudges —
  inside the lot, off the paving, out of every approach strip, clear of the
  bodywork, and clear of the two points of ground the Ferryman walks through
  on his way round the nose. The paving clearance is `0.85 m` and that number
  is measured off the CANOPY, not the paving: the five broken segments carry
  roofs whose outer corners reach about `6.05 m` from the middle of the island
  and hang at `3.49`, which a `3.30 m` post would thread its head through.
  The old mast bracket survives as the fallback for a bay that leaves nowhere
  to stand one; `FerrymanLamp_FitsOnAlmostEverySeedThatParksTheCar` holds that
  to the exception it is meant to be.
- **The hero was still boarding a bus.** The Ferryman has had a real door beat
  since he was given somewhere to go, and the hero played `BusBoardEnter` at
  the passenger door with his hands by his sides while a leaf swung itself
  open on a `MoveTowards` timer that started when he was told to walk over.
  `CarBoardEnter` and `CarAlightExit` are that beat authored on the hero's rig
  on the Ferryman's own key grid, hands mirrored: the hero docks at the
  PASSENGER door already facing the way the car points, so the car is on his
  left the whole way in and the leaf only becomes the thing on his right once
  he is through it and sitting down.
- **The leaf is now a pure function of the clip that is pulling it** —
  `LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness` on the way in,
  reused verbatim, and this side's own curve on the way out because he never
  gets out. The free-running door timer is gone.
- **Sitting in the car switches to first person**, the park boards'
  arrangement rather than the bus's: the bus puts its lens behind and inboard
  of a passenger, which reads in a room and would be the back of his own head
  in a `1.4 m` cabin. `LastRouteCarSeatViewPlan` puts the eye `0.78 m` over the
  seat pelvis anchor and `0.12` in front of it, `62` degrees, `±105` of yaw so
  he can turn and look at the man at the wheel; `Player3DHeadVisibility` takes
  the head off while the lens is inside it and his hands and knees stay in
  frame. The camera is taken at the moment his hips leave the doorway and
  given back a third of the way through standing up, so the walk in and the
  climb out are both seen from outside.
- **`target_direction` is not usable in a pose that bends the spine, and that
  cost two probe rounds.** It is relative to the parent's own delta, so an arm
  authored against a chest bowed forty degrees swings forward with it: the
  first pass had the hero reaching for a door handle at his own hip and
  bracing on a door frame behind his knees. Every arm in the two car Actions
  is now an absolute `armature_direction`, probed against printed wrist
  positions and rendered from two angles before export — the front view is
  the one that reads a lateral reach, and the first probe camera was aimed
  straight down the reach and showed nothing at all.
- **Player Action library: `35 -> 37`.** Both car clips open and close on
  `bus_seated`, so `BusRideLoop` is still the seated middle and the car's roof
  height in `tools/build-last-route-car-3d-model.py` still governs the head
  clearance; `validate_interaction_pose` holds the whole
  `Relaxed -> CarBoardEnter -> BusRideLoop -> CarAlightExit -> Relaxed` chain.
  The model FBX, the preview and the portrait came back geometrically
  identical and were restored, so only the animation FBX, the `.blend` and the
  manifest carry the change.
- Verification: `tools/build-player-3d-model.py` through Blender (`37` Actions,
  all validators green), `Player3DAssetSetup.Run` headless, and an EditMode
  selection over `LastRouteCarPlacementTests`, `LastRouteCarDoorTests`,
  `LastRouteCarSeatViewTests`, `Player3DAssetImportTests`,
  `CityPointOfInterestSurfaceAppearanceTests` and
  `LastRouteFerrymanBoardingTests`. Not run, and deliberately: the PlayMode
  suites and any player build.

## 2026-08-25 — The whole map is squares now, and the mountain road can be walked into

- **The chart was only a destination where something happened to stand.** The
  point inspector could pick a bar, a stop, a precinct or a landmark, and the
  gaps between them — every street, every shoulder, every switchback shelf —
  were not addressable at all. `CityMapTeleportLattice` rules an even square
  lattice over the whole tab and keeps the squares its area's ground answers
  for; each kept square joins that area's point catalog, so selection, the
  coordinate readout, key cycling and the existing teleport button all work on
  it unchanged. Coverage measured on the default seed: the city keeps **all
  196** of its squares, the mountain road **151 of 350** — the entire
  serpentine including the bridge, and the whole plateau.
- **The step is the size the viewport already keeps readable**, so a square is
  never smaller on screen than a comfortable click: the city takes its own cell
  spacing (`26 m`) anchored on `WorldOrigin`, so one square is one city cell and
  the lattice cannot cut a block in half; the mountain road takes `8 m` from
  the tunnel portal.
- **A square is probed from its edges, not only its middle, and that is the
  whole trick.** Anchoring on the city grid puts the carriageway on the SEAM
  between two squares rather than through the middle of one, so a centre-only
  probe would have left every street off the chart — and would have answered
  each block square with a point inside its own building, because the walkable
  mask counts the ground under a block as walkable and treats the building as
  a collider standing on it. Nine probe points per square, footprints
  subtracted, nearest-to-centre wins.
- **The mountain teleport was measuring against the city.** `TryClampToWalkableGround`
  built `RoadWalkableArea.FromLayout(Layout)` unconditionally — right in the
  City, nonsense on the mountain road, and not a refusal but a wrong answer:
  the two scenes share one coordinate system and the mountain route starts at
  the world origin, directly on top of the city, so a hairpin or the cafe was
  clamped onto a street that is not in that scene. The clamp is now
  `ICityMapTeleportGround`, one per area; `MountainRoadRoot` hands the map its
  own `World.WalkableArea`, the City needs no wiring and behaves exactly as
  before. Mountain heights are derived rather than sampled — the road is its
  own centreline samples, the plateau one flat slab, the tunnel the portal
  floor — which also fixes landmarks whose authored Y belonged to a prop.
- **Squares register no hover target, on purpose.** The map resolves hover and
  clicks by distance first and priority only as a tie-break, so a whole square
  would have outbid the small markers lying inside it. The pointer finds a
  square arithmetically instead, and only after every named target has missed —
  which also keeps the pass at ~14 rects and ~40 lines per repaint rather than
  a few hundred hit boxes.
- **A pointer pick no longer recentres the chart.** Key cycling still does, and
  must — the next point is usually off screen. A click must not: the square is
  already under the cursor, and pulling the map out from under the hand every
  time is the difference between choosing a square and chasing one. It did not
  matter while every point was a landmark; with a lattice, clicking is the
  whole interaction.
- The lattice is charted the first time the inspector is opened, not at
  `Initialize`: it costs a few thousand mask probes and a player who never
  presses `XYZ` should never pay for it.
- **And none of it could have been reached.** The teleport button was hidden
  outright unless `DebugTeleportEnabled` was armed — and the only switch for
  that, `MinigameDebugWindow`, existed in `City` and `BarInterior` only, while
  the flag lives on the map controller, which is rebuilt per scene. So
  travelling to the mountain road turned the teleport off and left nothing to
  turn it back on. Two changes: `MountainRoadRoot` now builds the same F9
  window (it was already area-neutral), and **the inspector carries its own
  teleport** — turning `XYZ` on IS the decision. The old gate was a second
  switch in another window in another scene guarding a mode nobody enters by
  accident, and its whole visible effect was to make the mode look broken:
  pick a point, read its coordinates, nothing to press. The area check stays;
  that one is real. Debug mode and the inspector are simply independent now,
  so toggling F9 no longer closes a mode it does not own.
- **And then the other tab said "point is in another area" and stopped.** That
  is a statement of fact standing in for an answer. Both halves of it were
  true — the other tab charts a scene that is not loaded, and reaching it is a
  transition rather than a `Motor.Teleport` — and neither half implies the map
  cannot start that transition. `AreaTravelRequest` now optionally carries an
  arrival coordinate under a new `AreaArrivalToken.MapPoint`; the service arms
  it with the token before destination activation, exactly where the old
  comment said exact-position persistence belonged. The button reads "travel
  to this point", the ordinary `AreaLoading` runs, and the destination root
  spawns on the coordinate: `MountainRoadRoot` through its own walkable mask,
  `CityGameRoot` by resolving the height from the ground under the point
  first (a chart point carries whatever Y suited the thing it names — a bar's
  road anchor sits at zero) and by the plain clamp for decks the surface
  sampler will not answer for. Either failing falls back to that area's
  ordinary front door rather than dropping the hero into scenery. The travel
  callback became `Func<AreaTravelRequest, bool>` on the way, since a
  `(area, token)` pair can no longer say what is being asked for.
- **A leaked static came out of hiding on the way.**
  `CrossAreaTravel_RejectionKeepsSelectionAndReportsFalse` ends with the map
  deliberately still open and then destroys it, and `BarMinigameModalLock` is
  a plain object in a static field — it does not go null when its owner dies.
  Every earlier fixture happened to never open anything afterwards, so it
  stayed invisible until a new fixture called `Open()` and was refused with no
  error anywhere (green alone, red in the suite — the signature). Closed at
  the source, and the new fixture reads `IsAnyLocked` first, which is the
  property's own retire-a-dead-lock path.
- Verification: focused EditMode over `CityMapAreaPresentationTests`,
  `CityMapDistrictPresentationTests`, `LocalizationCatalogTests` and
  `AreaTravelContractTests` — `61/61`, including five new contracts
  (`MountainTeleportLattice_CoversTheWholeRoadAndPlateau`,
  `MountainMap_ClampsAgainstMountainGroundNotTheCity`,
  `CityTeleportLattice_TurnsTheStreetsThemselvesIntoPlaces`,
  `PointInspection_TeleportsWithoutArmingDebugModeFirst`,
  `MapPointOnTheOtherTab_TravelsCarryingTheCoordinate`). Coverage itself
  was read off a throwaway probe that dumped both lattices as ASCII and was
  then deleted — IMGUI cannot be captured headlessly, so the drawn grid and
  scrim are unverified by machine and want an eye in the editor.

## 2026-08-25 — The Ferryman's own lamp was blowing him out

- Halved it: `70/22` night/day to `38/12`, range unchanged at `5.2 m`. That
  makes it the dimmest registered site light in the city by some way, which is
  correct — it is the only one that is not a lamp.
- The number was wrong for a reason worth keeping. At `70` it sat between the
  pier hand lamp (`46` at `11 m`) and the porch bulb (`110` at `8 m`), which
  reads as reasonable until you notice the RANGE: `5.2 m` concentrates all of
  it on one man, and at his face it delivered about what standing directly
  under a street practical does. He is also the only lit thing on an unlit
  lot, so there is nothing beside him for the eye to normalise against, and
  through ACES and bloom at `640x360` that is a cut-out rather than a man in
  the dark. **Intensity cannot be judged against another fixture's intensity
  without its range and what else is lit near it.**
- It is safe to take this much out because the same pass that fitted the lamp
  also lifted his coat palette forty percent (`0.055` to `0.078`). The lamp no
  longer has to carry him on its own, so it can go back to being what the
  design says it is: not a fixture, but his own headlights coming off the mist.
- The range is untouched on purpose. The tight falloff is what keeps the
  warmth on HIM rather than washing the car and the paving, which is the
  difference between a lit man and a lit lot.
- Verification: `dotnet build` on Runtime, `0` errors. Nothing pins these
  constants — no test and no manifest reads them — so there was nothing to
  re-baseline; the judgement is the neighbour table above, per the standing
  rule that light intensities are never tuned from edit-mode captures.

## 2026-08-25 — The map inspector teleports, and stopped eating the debug teleport

- Follow-up to the XYZ inspector below, correcting two things about it.
- **The inspector and the debug teleport cancelled each other, and that
  removed the teleport.** `SetMapPointInspectionEnabled(true)` cleared
  `DebugTeleportEnabled` and the reverse cleared the inspection. The reasoning
  was half right — one map click cannot mean both "select this lot" and
  "select this point" — but the exclusion was applied to the MODES rather than
  to the click, so opening the coordinate readout silently switched debug mode
  off and there was no way to have both. Only the LOT SELECTION is dropped
  now; the click stays unambiguous because while the inspector is on the
  markers pick points and the whole-lot buttons go quiet. Leaving debug mode
  still closes the inspector, because it is a debug tool.
- **A point is now a teleport destination.** `ConfirmMapPointTeleport` sends
  the player to the selected point rather than to the middle of the region
  containing it — which is the whole complaint: a precinct like the cemetery
  or the yards is ONE entry in `MapObjects` covering a whole area, so
  confirming it could only ever mean "somewhere in there". Every open-area
  target already had a point of its own, so nothing new had to be catalogued.
  The arrival is clamped to walkable ground exactly as the area teleport
  clamps its own, and a point that cannot be clamped is refused and logged
  rather than dropping the hero into scenery. Points on the other tab are
  refused outright: that is a different scene, and the area-travel button
  owns that trip.
- **The readout lost its height.** `X/Y/Z` became `X/Z`. A plan view has two
  coordinates; the third is the one number the projection cannot show, nobody
  navigates by it, and on a city graded everywhere it is noise beside the two
  that locate the point. `map.point.coordinates` now carries two placeholders
  and the catalog test asserts there is no third.
- Verification: focused EditMode over `CityMapAreaPresentationTests` and
  `LocalizationCatalogTests` — `16/16`, including two new contracts
  (`PointInspection_KeepsDebugTeleportAndOffersThePointItself`,
  `PointCoordinates_ReadOutXAndZOnly`).

## 2026-08-25 — Both map tabs gain an observational XYZ inspector

- Added a shared stable-ID `CityMapPointDescriptor` catalog and an ordinary
  coordinate-inspection mode to the full-screen City/Mountain Road map. The
  `XYZ` panel button, keyboard `C` or gamepad north/`Y` toggles it; click picks
  the foreground-first target under the pointer, while Left/Right or D-pad
  Left/Right cycles deterministically and recentres the viewport. The selected
  point keeps a visible outline, and the side panel shows its localized name,
  area and invariant world `X/Y/Z` to one decimal place.
- City's catalog contains every canonical `BuildingLot`, every open-area
  arrival, all bus stops, the current player, the city mountain-tunnel portal
  and the boat-station hut. Special lots replace their anonymous entry instead
  of appearing twice: bars use `ReturnPosition`, home and supermarket use
  `Center`, and POIs keep their authored point.
- Mountain Road catalogs the current player, exit tunnel, all ten authored
  hairpin apexes, bridge centre, plateau endpoint, cafe and cableway. Road and
  itinerary polylines, intermediate route samples and mountain hatches remain
  decorative and cannot be selected.
- Inspection is observational and mutually exclusive with debug teleport. It
  consumes the map's action input without editing the bar route, requesting
  area travel or confirming a teleport; the player must close `XYZ` before
  using those actions.
- Verification: focused EditMode
  `CityMapAreaPresentationTests.MapPointInspection_CoversBothTabsWithoutRequestingTravel`
  passed `1/1` in `1.11 s`; both localization JSON catalogs parse with matching
  point keys. The remaining EditMode suite, PlayMode, a player build and a
  manual visual smoke were intentionally not run in fast mode.

## 2026-08-25 — Mountain terminal asphalt and ridges stop disappearing

- Materialized the terminal's visible asphalt entry and full R`7.5 m` turning
  pocket as a dedicated mesh `0.025 m` above the authoritative road/plateau
  surface, overlapping the road seam by `0.45 m`. The overlay is deliberately
  colliderless: the existing continuous road and plateau remain the single
  physical driving surface, so visibility no longer costs a second physics
  skin or creates a coplanar seam.
- Expanded the terrain margin to `76 m`. Generic mid and far-snow ridges now
  sample the outer perimeter of the global route/plateau envelope. Every
  oriented base is placed from the minimum terrain sampled beneath its complete
  footprint and buried by `1.5 m`; ridge footprints are validated clear of the
  road corridor, plateau and every tree crown.
- Verification: the focused EditMode regression
  `MountainRoadTests.DefaultPlan_BuildsAbsurdHighTenHairpinBridgeWorld` was
  `Passed`, `1/1`, in `14.662368 s`. Complete suites, a player build and manual
  smoke remain outside fast mode.

## 2026-08-25 — Mountain Road becomes an absurd high serpentine

- Replaced the former `82.7 m` / `8.7 m` two-turn climb with a deterministic
  `600 m` route that gains `26.1 m`: ten `7.5 m`-radius hairpins widen the
  ordinary `4.8 m` road to `6.4 m`, sampled grade stays at or below `8%`, and
  the final `5 m` remain level. At the ordinary `2.6 m/s` player speed the
  complete ascent now takes about `230.8 s`, or `3 min 51 s`. The pure route
  model owns ordered sections and hairpin descriptors rather than two fixed
  turn fields; LastRouteCar driving remains outside this feature.
- Inserted one mandatory `50 m` high-gorge bridge between the fifth and sixth
  hairpins. Its descriptor holds the `4.8 m` clear road, `5.8 m` deck,
  `0.72 m` slab, `1.1 m` rails and world-`Y=-16` gorge floor, with at least
  `25 m` below both deck ends. A focused bridge builder adds the sloped
  structural deck beneath the authoritative asphalt, batched girders and
  crossbeams, two abutments, two floor-grounded bridge piers and open two-beam
  rails backed by continuous physical collision. It is bounded to seven
  renderers and six enabled colliders and creates no light or audio source.
  The existing loose-guardrail sound now belongs to a visible bridge rail.
- Terrain lookup skips suspended road samples and carves the dedicated gorge;
  forest, road dressing, snow poles and ridge layers now sample the whole route.
  The existing `42 x 27 m` terminal, silent four-role cafe and four-cabin
  cableway remain intact, while cable heights are rebased from the raised
  plateau. The map copies all ten hairpin apexes and the bridge centre from the
  same plan and draws a separately localized bridge marker.
- Verification: focused EditMode
  `MountainRoadTests.DefaultPlan_BuildsAbsurdHighTenHairpinBridgeWorld` passed
  `1/1` in `8.87 s`; focused EditMode
  `CityMapAreaPresentationTests.MountainRoadOverlay_FromPlanOwnsHairpinsBridgeAndLandmarks`
  passed `1/1` in `0.59 s`; `git diff --check` covers the final handoff.
  Complete suites, a player build and a manual scene smoke were intentionally
  not run in fast mode.

## 2026-08-25 — The Ferryman gets out of his car before he gets into it

- "Уехать из города? — Да" used to be `0.75 s` of `Vector3.Lerp` from the
  bonnet to the driver's seat, straight through the bodywork. It is now a
  four-phase beat: he shoves off the metal and drops onto the lot, the car
  comes up on its springs, he walks round the nose, stops at his own door,
  pulls the handle, gets in and shuts it behind him. About seven and a half
  seconds, of which the player controls himself for six.
- **The menu closes when his boots land, not when the door shuts.** The
  answer is over the moment he moves; holding a dialogue open across a walk
  round a car would make the payoff feel like a cutscene the player is
  locked out of.
- **One new clip, not four.** `FerrymanDismount` (`1.0 s`) opens on the exact
  base pose of `FerrymanWait` and closes on the exact base pose of
  `FerrymanTrudge`; `FerrymanBoard` was re-authored from `0.75 s` to `2.5 s`
  and now opens on the trudge's base and closes on the drive's, carrying the
  handle, the step back, the way in and the door shut inside one clip. The
  walk is `FerrymanTrudge`, authored months ago and never once played until
  today. He is the library's second one-shot as well as its first, so
  `IsOneShotClip` had to stop inferring the shape from `ActionClipName` and
  start naming both.
- **His arms were solved, not posed.** Three hand targets swept in Blender
  against the rig - `0.43 m` in front of him at the car's waist for the
  handle, drawn back and across for the pull, `0.63 m` out to his left for
  the door edge from the seat. The left hand, because the car is left-hand
  drive and the rig faces its own `-Y`, which puts the driver's door on his
  left. Every first guess about this shoulder has been inverted for two
  sessions running; the sweep took one Blender run and settled all three.
- **The doors open, and that broke the handle.** Both leaves were authored on
  hinge pivots in `build-last-route-car-3d-model.py` specifically so that "he
  opens the door" would one day be a rotation - but the front handle was
  drawn into the flank's trim mesh, which is invisible for exactly as long as
  nothing moves and a chrome bar hanging in an empty doorway the moment
  something does. It now rides its own leaf. The rear handles stay on the
  body; those doors never open.
- **The docks had to move, and nothing would have told us.** A `1.51 m` leaf
  on a hinge at the A-pillar sweeps every bearing between shut and open, and
  the hero's dock stood `0.99 m` from that hinge - the door would have swung
  clean through him. There is no angle that helps: the only safe place is
  outside the blade's radius. Both docks are now `1.85 m` out and a metre
  back along the flank, which is also where a person actually stands to open
  a car door. `LastRouteCarDoors.MeasureSwingClearance` states the rule and
  two EditMode tests hold both docks to it - plus one that proves neither
  dock leaves the island on the production seed, because the bay placement
  only ever guaranteed `0.40 m` of clearance.
- **Springs, as a kick rather than a road wave.** The bus samples its
  suspension from distance travelled; this car never moves again, so what it
  needs is an impulse and two seconds of settling. A pure three-channel
  damped oscillator (`ζ = 0.35`, ~`1.1 Hz`) drives a sprung body slipped
  between the imported node and its parent, with the four wheel pivots lifted
  out of it - the bus's own trick, because the generator hangs the wheels off
  `ROOT_Body` beside the panels and rocking that drives the tyres into the
  ground. The nose lifts about `4 cm` when his weight leaves the bonnet.
- **The passenger seat is his to offer.** The hero could already sit in the
  car - prompt, clips and all - at any time, including while the man who owns
  it was still sitting on the bonnet. It is now gated on
  `presentation.IsDriving`, attached after the fact the way the watchman's
  gravedigging is, because the car is built before the Ferryman is. The
  passenger door swings for him too, timed against the shared bus transfer
  rather than a bespoke hero clip, and his seated pelvis is bound to the seat
  anchor so he rides the rocking body instead of floating over it.
- **His lamp goes with him.** It was a fixed point beside the bonnet, which
  was right for as long as he never left it. A man in the darkest coat in the
  game walking out of the only light on an unlit island simply disappears for
  four seconds. The offset is captured rather than re-authored, so the perch
  is pixel-identical and only the walk gains anything.
- Verification: `blender --background --factory-startup --python
  tools/build-last-route-car-3d-model.py` (`36` meshes, `1992` triangles) and
  the same for the pedestrian generator at `--archetype all` (`37` clips,
  `perched FerrymanWait: seat 0.5097-0.5106 m` unchanged, all five Ferryman
  clips grounded); `LastRouteCarAssetSetup.BuildOrThrow` and
  `CityPedestrianAssetSetup.RunLastRouteFerryman` headless; EditMode
  `LastRouteCarDoorTests`, `LastRouteFerrymanBoardingTests`,
  `LastRouteCarPlacementTests`, `LastRouteFerrymanTests` and
  `CityPedestrianRuntimeTests` — `58/58`, plus `LastRouteFerrymanAssetTests`,
  both park players and the canopy rag — `21/21`.
- **Note for whoever picks this up:** the city map UI is being rewritten in
  the same working tree at the same time, and for a while its EditMode
  fixture would not compile, which blocks every Unity run in the project
  rather than just its own. If a batch run aborts with "Scripts have compiler
  errors" in `CityMapAreaPresentationTests.cs`, it is not this pass.

## 2026-08-25 — The mountain cafe has its own silent cast

- Replaced the four generic static counter figures with four isolated staged
  roles: a lone patron, a neighbouring man/woman couple and an attendant. Each
  owns a distinct low-poly model and two in-place Generic clips in the dedicated
  `MountainRoadCafeCast` animation library; the prefabs and serialized provider
  stay outside the ordinary pedestrian pool.
- The immutable cast plan preserves semantic role IDs, leaves two stools empty
  and aligns only the three occupied places with cups. A seeded controller keeps
  long `18-32 s` global rests, `35-55 s` per-role cooldowns, one active beat at
  most and one synchronized couple beat. It adds no voices, physics, lights or
  ambient emitters.
- Blender's cafe-only validator rebuilt the four models and eight-clip library,
  proved all three seated contacts against the `0.46 m` stool and the attendant
  against the floor, and exported a reviewed four-role contact sheet. Unity's
  asset setup imported the passive prefabs/provider successfully. A live batch
  import also exposed a rebuild ping-pong: the cafe postprocessor had treated
  shared Player assets as cafe-owned source triggers, so neighbouring setup
  pipelines could repeatedly force-import one another. Only the dedicated cafe
  models/manifests/library now trigger an automatic rebuild; a clean setup run
  exits once with code `0`. Focused `MountainRoadCafeCastTests` passed `13/13`
  after the final import; the full suites were intentionally not run in fast
  mode.

## 2026-08-25 — Five things wrong with the Ferryman, from a screenshot

- **You could see through his hips.** He is the only design whose drawn
  hem is a placeholder: `LastRouteFerrymanCoat` hides `CLO_CoatHem` the
  moment the cloth skirt that replaces it exists. Everything above that
  box hangs off the spine and everything below it off the thighs, so
  hiding it left `0.146 m` of nothing where his pelvis is — visible clean
  through the coat, and invisible to the whole suite because the model on
  disk was complete. The body under the placeholder is now its own part,
  `CLO_CoatSeat`: deliberately narrower than the stub (`0.336` against
  `0.392`, so the cloth flaps at `0.180 m` keep `12 mm` of air) and
  stopping at the hip line, so the lowest drawn point of the pelvis group
  stays the mooring coil and the perch measurement does not move.
- **The coin was under one pixel.** Drawn at a realistic `32 mm`, which
  at the `640x360` composite is nothing from any distance a player looks
  at him from. Now `54 mm`, brighter brass, and thrown `0.50 m` instead
  of `0.42` so the arc carries past his own face.
- **He swings his legs now**, and that took a contract rather than a
  keyframe. The perch validator measures his seat against the lowest
  drawn point of the model in EVERY frame, and on this design that point
  is a boot sole — so two legs up at once would move the seat by the full
  amplitude. The two boots were levelled to `1 mm` of each other (the
  left had been hanging `73 mm` high), and the loop kicks one leg at a
  time on keys whose neighbours leave that leg at rest: right at `0.25`,
  left at `0.625`, a half-hearted right at `0.875`. Measured across all
  `97` baked frames the seat travels `0.5097-0.5106 m` — a millimetre —
  while the swinging ankle rises `76 mm`.
- **He was staring at the sky.** Twelve degrees of backward lean with the
  chin up to match. Four degrees came out of the pelvis and the spine and
  four out of the neck and the head. Both things that lean carries had to
  be re-swept: the thighs (`-56/-60` now) to keep the boots on the bumper,
  and the bracing arm (`upper_arm.R` Z `-70` to `-62`) to keep the palm on
  the bonnet — it now sits `3 mm` into the metal against `26 mm` before.
- **And he is lit.** One warm shadowless point light on the runtime root
  beside the art, `38` at night dropping to `12` by day through
  `CityNightSiteLightRegistry` — the cemetery porch bulb's contract, for
  the same reason it exists there. No fixture and no fog halo: the warmth
  is his own headlights coming back off the mist, and a halo is the blur
  of a lamp that would not be there. It hangs above his cap so the light
  rakes DOWN over the brim, because the design draws no eyes and leans on
  the brim's own shadow slab. His coat palette came up about forty percent
  with it (`0.055` to `0.078`); at the old value no lamp made any
  difference. The near-black under the brim is deliberately not lifted.
- **A test can look green and prove nothing.** Every staged pedestrian
  ships `CullUpdateTransforms`, so in batch mode — which draws nothing —
  the Animator declines to write a single bone and the whole rig reads
  back in its BIND pose. The first leg-swing assertion failed with the two
  ankles `8.9e-08 m` apart, which is not a swing bug: the bind ankles sit
  at exactly equal height. Any PlayMode assertion on a staged NPC's POSE
  has to set `AlwaysAnimate` on the instance first, or it is measuring the
  bind pose. The two existing pose assertions beside it pass either way,
  which is how this went unnoticed.
- **Note for whoever picks this up:** the Mountain Road cafe cast was
  being built in the same working tree at the same time, and a full
  `--archetype all` run currently dies inside `cafe_lone_patron`
  (`892` triangles against a `900-1900` budget). The shared locomotion
  library was therefore rebuilt at its shipped `36`-clip set with the four
  cafe designs held back — they were never in it; they have their own
  `MountainRoadCafeCast` library. One clean full run once that design
  passes puts everything back in step.
- Verification: `blender --background --factory-startup --python
  tools/build-city-pedestrian-3d-model.py -- --archetype
  last_route_ferryman` (`33` meshes, `992` triangles) plus a
  library-only run reporting `perched FerrymanWait: seat 0.5097-0.5106 m
  over the soles, ground contact GEO_BootSole.L, GEO_BootSole.R`;
  `Unity.exe -executeMethod CityPedestrianAssetSetup.RunLastRouteFerryman`;
  EditMode selection over `LastRouteFerrymanAssetTests`,
  `LastRouteFerrymanTests`, `LastRouteCarPlacementTests`,
  `CityPedestrianRuntimeTests`, `ParkChessPlayerTests` and
  `ParkCheckersPlayerTests` — `65/65`, including the two new contracts;
  `LastRouteFerrymanPlayModeTests` — `6/6`.

## 2026-08-25 — The Ferryman was sitting on nothing

- He was hanging in the air over the bonnet with his cloth coat spread
  under him, which is what made it look like he was perched on some extra
  prop. The cause: the art contract puts the model origin on the sole
  plane of the BIND pose - standing straight - and the perch has both
  knees up on a car, so the feet leave that plane entirely. Placing the
  root on the car's soles anchor therefore left him a leg's-worth too
  high.
- The runtime cannot measure the correction itself. Unity does not
  recompute skinned bounds for a manually driven PlayableGraph, and the
  ankle bone is no substitute - the perch deliberately draws one boot back
  onto its toe, so an ankle-based solve landed `19.5 cm` short. Blender
  already measures the right number for the validator it prints
  (`seated_drop_m`, the pelvis above the lowest drawn point of the posed
  model: `0.485715 m`), so that number now rides the animation manifest
  into the prefab and the presentation places the PELVIS that far above
  the bumper - the same shape as the driver's seat solve beside it.
- The new test proves it against the car's own `PerchSeatAnchor` rather
  than against the placement arithmetic: the drawn pose keeps the
  underside of his hips `0.5077 m` over his soles and the car draws its
  bonnet `0.505 m` over its bumper, so boots on the bumper means backside
  on the metal. Measured after the fix: pelvis `1.0607` against a bonnet
  at `1.08`, which is the authored `-0.022 m` pelvis lift to the
  millimetre.
- Verification: `LastRouteFerrymanPlayModeTests` `5/5`; EditMode
  pedestrian, fisherman, both park players and the Ferryman asset
  contracts `65/65`; full PlayMode `177 passed, 0 failed, 1 skipped`.
  Renders from four angles confirm he is on the metal.

## 2026-08-25 — The twenty reds, and the four real bugs behind them

- Took the full PlayMode suite from `20` failures to `0`
  (`177 passed / 1 skipped`) and EditMode from `16` to `1`
  (`1552 passed`). The one left is `homeyard-booth` sitting `8.5 cm` under
  its sampled ground, which belongs to the six `CityFringeYard*` files
  another session has open right now.
- **Four were production bugs, not stale expectations.**
  - The playground swing could never be pushed. Its trigger volume's lift
    subtracted the seat's PIVOT-space y (about `-2.3`) where the seat's
    height above the lawn (`0.62`) was meant, so the box floated three
    metres up by the crossbar. Now `57` contacts and an `0.80 m` push.
  - `BarMinigameModalLock` is a plain object in a static field, so unlike
    a MonoBehaviour reference it never went null when its owner was
    destroyed. A lock taken by an interaction whose scene then unloaded
    stayed held for the whole session, and every interaction in the game
    asks `IsAnyLocked` first - so everything would silently stop opening.
    A lock whose subject is gone now releases itself. This was what made
    thirteen EditMode tests fail in the suite and pass in isolation.
  - The Home balcony's exterior view carried street-lamp colliders and a
    bus stop whose shelter stood inside the bedroom. The night builder's
    collision is now opt-out and the view passes `false`; the stop is
    clipped to the exterior half-space the same way the ground boxes
    already were, rather than dropped, so the balcony keeps its stop.
  - `HomeBedInteraction` has always known how to hide a crumpled shirt
    while the hero sleeps and put it back after - and nothing ever built
    the shirt, so the lookup returned null and the beat was dead. It now
    exists.
- The rest were expectations that had stopped describing the game:
  literals where the blueprint, the population profile or the owner's own
  constant should have been asked; `Has.Count` reflecting on an array;
  intoxication and a wall clock raced against real elapsed time; a
  camera-corner framing check that demanded a whole standing man two
  metres from the lens; a first-person hand measured on the frame it
  began to appear. Each is now written against the thing it is really
  about, with the reason in a comment.
- Verification: full PlayMode `178` -> `177 passed, 0 failed, 1 skipped`;
  full EditMode `1553` -> `1552 passed, 1 failed`. The remaining skip is
  the IMGUI one: batch mode has no game view, so OnGUI never runs.

## 2026-08-25 — Two guarded bus tests were the test's own ground

- Lifted the `Assert.Ignore` guards on
  `CityBusRidePlayModeTests.Passenger_BoardsRidesAndExitsAtLaterStop` and
  `CityBusNpcPassengerPlayModeTests.AmbientPassenger_BoardsRidesAndAlightsAtALaterStop`
  and fixed them. Both had been recorded as "fails on any code"; both were
  the harness, not the bus.
- Each builds its route on `CityStreetSurfacePlanner.RoadTop` (`0.08`) while
  its own `CreateGround` slab topped out at `SidewalkTop` (`0.14`). That
  buried the bus and every door dock derived from it six centimetres deep,
  and `CityPedestrianDirector.IsCollisionActivationSafe` then correctly
  refused to materialise a waiter inside terrain — its clearance capsule's
  lowest point sat `12 mm` under the slab. The hero test failed the same way
  one step earlier: he was spawned with his feet under the ground he was
  meant to walk on. Both slabs now top out at the height their own scene
  uses, and the hero spawns standing on it.
- Getting there is worth recording, because four indirect probes were all
  clean and all misleading: the pedestrian pool had a free actor, twelve
  riding presentations, no peer overlap, and an external `OverlapCapsule` at
  the slot reported nothing. What settled it in one run was temporarily
  logging at every `return null` inside the production spawn path and at
  each branch of the clearance check — that named the collider and printed
  the capsule extents. The instrumentation was reverted with
  `git checkout` after `git diff` confirmed it held nothing but probes.
- Also fixed, and found only because the City's build log was read rather
  than the test results: `LastRouteCarSeatPlan.Create` still took its axes
  from the imported `Body` node — the imported-basis trap for the sixth
  time, in a file whose sibling had already been corrected for it. That
  node's forward is nearly vertical, so the flattened vector was zero,
  `Quaternion.LookRotation` warned into the log and handed back identity,
  and the hero rode a car built entirely around its transparent glass while
  facing world `+Z`. Axes now come from the prefab root, and the seat's
  facing is derived from the drawn cabin (driver's seat → steering wheel),
  which has no basis to be wrong about. A new PlayMode test asserts the
  seated facing against that vector rather than merely against identity.
- Verification: `LastRouteFerrymanPlayModeTests`, both bus suites and the
  stairwell fixture — `15 passed, 1 skipped, 0 failed`, and the "Look
  rotation viewing vector is zero" warning is gone from the run.
  The remaining stairwell skip is a batch-mode capability limit: no game
  view, so OnGUI never runs and the IMGUI panel cannot be measured; its
  logic assertions run before the guard.
- **Not fixed, and not mine:** the full PlayMode suite is `178` tests with
  `20` failures — audio source counts `10 → 13`, night lights `5 → 6`, city
  grid `(12,12) → (17,14)`, physical boundaries `5 → 13`, street lamps and
  the home scenes. Every one is in a subsystem this pass did not touch, and
  they reproduce identically when run with none of this pass's classes in
  the filter, which rules out test-order coupling. They belong to the other
  sessions' in-flight work in the same tree.

## 2026-08-24 — The Ferryman, his coin and his coat

- Finished the Last Route Ferryman end to end: staged archetype, four clips,
  editor prefab and provider, and six runtime files under
  `Assets/Scripts/Runtime/City/LastRoute/`. He perches on the bonnet of the
  parked car with his boots on its bumper, facing out over the nose at
  whoever walks up, and throws a coin while he waits.
- He is the clip library's first design with TWO seats — a bonnet and a
  driver's seat — so `ActionSpec` gained a `perched` flag and an archetype
  may now declare both bands. Nothing is loosened: every seated clip is
  still proved against exactly one band, and a clip that cannot name its
  band is still an error.
- That immediately caught a real defect. `FerrymanDrive` had been marked
  `leaves_seat` and was therefore never measured at all; measured, it hung
  `0.4107 m` of leg under a seat with `0.22 m` of floor beneath it — a bus
  posture in a car. The cabin floor drop is now per-design rather than the
  bus's hard-coded `0.41`, and the driving pose was converged against the
  car's own numbers: `0.2197 m` of leg against `0.22 m` of floor, and
  `1.0288 m` of head against the `1.04 m` the roof allows.
- A transition is no longer declared a loop. `FerrymanBoard` is `one_shot`
  in the manifest, imported with `loopTime` and `loopPose` off, and asserted
  both ways round — loop-pose normalisation would drag its last frame back
  towards the bonnet, and it is authored to end exactly on the driving pose
  the runtime crosses into.
- The coin has no state: it never reparents, stays a child of the runtime
  root at scale one, and its world pose is a pure function of the wait
  loop's normalized time. Three flips per toss, odd on purpose so it lands
  the other face, and `1080°` so the catch has no seam.
- The coat skirt is real `Cloth`, hung as two narrow flaps beside his hips
  rather than one sheet in front. The single front panel was tried first and
  a render showed it as a signboard propped against his shins; the outer
  side of each thigh is open air, which is where cloth without colliders can
  safely hang.
- Interaction is the cat's, not the fisherman's: "Поговорить" or
  "Взаимодействовать", and the second asks «Уехать из города?» before it
  acts. Yes gets him off the bonnet and behind his own wheel and is not
  reversible. His twelve lines never offer a ride — the offer lives on the
  menu and only there, which a test enforces by grepping both catalogs.
- Verification: both Blender generators green (`36` actions,
  `perched FerrymanWait 0.5077 m`, `seated FerrymanDrive 1.0288/0.2197 m`);
  `dotnet build` on Runtime, Editor and both test assemblies (0 errors);
  `Unity.exe -executeMethod CityPedestrianAssetSetup.BuildOrThrow`; EditMode
  selection `96/96` across the Ferryman, car placement, pedestrian runtime,
  fisherman, both park players and localization; PlayMode
  `LastRouteFerrymanPlayModeTests` `4/4`, including the coin holding its arc
  to `1 mm` over `300` frames and his pelvis landing within `2 cm` of the
  drawn driver's seat. Throwaway renders from four angles confirmed the
  perch, the burning headlights and the coat before the capture was deleted.
- Also fixed, for everyone: the loop check compared raw quaternion
  components, so `ChessJeer`/`CheckersJeer` failed at `1.5055302` on a pair
  of clips that loop perfectly — `dot = -1.000000` exactly, the same pose in
  the antipodal representation. Rotations are now compared as rotations, and
  a clip that ends on `-q` says so in the build output instead of passing
  silently.

## 2026-08-24 — Mountain terminal cafe and cableway MVP

- Expanded the joined road endpoint to an irregular approximately `42 x 27 m`
  terminal while retaining the shared road vertices and a protected `7.5 m`
  vehicle circle. Terrain now blends smoothly around the exterior apron, the
  cable corridor is excluded from forest placement, and the upper machinery is
  hidden by one authored snow-ridge occluder rather than duplicate geometry.
- Built a same-scene five-sided Nighthawks-inspired cafe on the left with a
  genuinely open `1.6 m` entrance, physical glass/shell/furniture, a long
  counter, four silent staged figures and exactly two always-on practical
  Spots. Three short-range mono voices belong to its visible refrigerator,
  ceiling fixture and coffee boiler; the interior participates in rain shelter.
- Built the right-side `58 m` cableway as one continuous up/upper-turn/down/
  lower-turn loop. Four colliderless cabins move at `2.05 m/s` over three
  grounded colliderless remote supports; only the lower station is physical.
  Its visible reducer owns the motor loop and each real roller crossing emits
  the corresponding positional clack.
- Added cafe/cableway landmarks and localized hover names to the Mountain Road
  map from the terminal plan, integrated both builders into the mountain-only
  runtime root, and documented the new player-visible endpoint.
- Verification: the focused Unity EditMode `MountainRoad` category passed
  `9/9`, covering terminal geometry and seam, smooth terrain, cabin clearance
  and loop continuity, physical/colliderless ownership, open cafe entrance,
  world budgets, map landmarks and both localization catalogs. Full suites, a
  player build and a rendered Game View smoke were intentionally not run.

## 2026-08-24 — Mountain Road adopts LastRouteCar scale

- Resized the mountain route against the authored `4.83 x 1.80 m`
  LastRouteCar instead of treating it as a pedestrian ribbon. Ordinary road
  width is now `4.8 m`; both hairpins widen to `6.4 m`, use `7.5 m`
  centreline radii and carry denser arc sampling. The `8 x 5.5 m` tunnel
  remains unchanged and flares smoothly into the narrower carriageway.
- Enlarged physical, middle and far forest envelopes plus boulders, logs,
  stumps, dead trees, guardrails, culvert, mirror, utility fixtures and snow
  poles. Roadside placement now derives a minimum centre offset from the live
  ribbon width, the rotated object's cross-road extent and an `0.8 m`
  shoulder clearance, so the scaled props do not consume the driving lane.
- Rebuilt the terminal as an irregular approximately `22 x 18 m` turning
  plateau. The final `5 m` of route is level with it; the road and platform
  share their two top and lower entry vertices, the terrain bed is continuous,
  and the former transverse platform sidewall is omitted. This removes the
  measured `0.60 m` step, `0.42 m` open gap and collider lip at the old join.
- Verification: focused EditMode
  `DefaultPlan_BuildsLongGroundedTwoHairpinWorld` passed `1/1`, including the
  LastRouteCar-width centre corridor, shared mesh vertices, continuous terrain
  seam and a `6.5 m` clear turning radius on the plateau. Complete suites, a
  player build and a rendered driving smoke were intentionally not run.

## 2026-08-24 — Mountain Road becomes a separate loaded area

- Appended `MountainRoad` and `AreaLoading` at build indices `7` and `8`,
  preserving every previous scene index and expanding the shared-player set to
  six gameplay roots. Mountain Road composes only its own world; City and the
  mountain are separated by Single-mode loads and never coexist in a frame.
- Built a continuous `82.7 m` narrow ascent from a `9 m` exit tunnel, with the
  hero starting `6 m` inside it. The `3.4 m` road widens to `4.6 m` through two
  `5.5 m` hairpins, rises `8.7 m`, and ends after roughly `31.8 s` of ordinary
  walking on an irregular approximately `12 x 10 m` mountain plateau.
- Layered grounded forest/misc, middle ridges and far snowy mountains close the
  scene without exposing an endpoint. Five positioned sound anchors now belong
  to readable physical sources, and one tunnel lamp visibly flickers instead
  of using unattached ambience.
- Added normal City/Mountain Road map tabs. Selecting the other area routes
  through a black loading screen with a progress bar; only the active-area tab
  draws the player. The mountain root regenerates pure City map data without
  instantiating City GameObjects.
- Kept the physical City tunnel deliberately unavailable: it still delivers
  the refusal/return behaviour and is not yet connected to Mountain Road.
- Verification: Unity compiled the Runtime, Editor, EditMode and PlayMode
  assemblies during explicit scene setup; the focused area-loading/build-scene
  selection passed `9/9`, and the Mountain Road plan/world regression passed
  `1/1` with the real session seed. `git diff --check` is clean. The dedicated
  map presentation selection, full suites, a player build and Game View visual
  smoke were not run.

## 2026-08-24 — The south tunnel becomes a visible future route

- The former sealed gate is now an open `8 x 5.5 m` portal into a `72 m`
  faceted tunnel. Its first `12 m` are physical, later sections bend west,
  and the uncapped end remains outside both the entrance sightline and the
  City's far-plane contract. The schematic map shows only the open arch and
  the first `12 m`, not the hidden visual continuation.
- An inward crossing at `8 m` shows one localized thought and walks the normal
  player rig back to `6.5 m`; no prompt, teleport, destination scene or fake
  transition was added. Portal sheltering gives the player-following rain a
  dry core, clears local fog particles and hides the camera-relative ridge
  shell until the player leaves with mouth hysteresis; global Exp2 fog remains.
- Five path-following ceiling fixtures provide the depth read. The second
  reuses the existing pooled tunnel Spot with a daytime floor, deterministic
  sparse two-dip flicker, short-range mono ballast buzz and synchronized
  positional crackle, preserving the City's twelve-Light cap.
- Follow-up runtime fix: the lens `MaterialPropertyBlock`, which owns native
  Unity state, now initializes lazily on its first real lens application rather
  than in the MonoBehaviour field initializer. This works even under an inactive
  parent, removes the Unity 6 `CreateImpl` constructor exception and prevents
  its secondary null dereference during the first flicker application.
- Follow-up approach z-fighting fix: generic forefield marks are cut out of
  `DriveClearBounds`; the approach and wheel-rut segments meet exactly instead
  of overlapping by `0.06 m`, and consecutive concrete-return sections likewise
  meet at their end planes instead of overlapping by `0.18 m`.
- Verification: one focused Unity EditMode selection across mountain/fringe
  construction, travel crossing, shelter, tunnel lighting, night-light budget,
  map presentation and localization passed `11/11`. Full suites and a player
  build were not run. The reported lighting lifecycle regression then passed
  its full inactive-parent factory test `1/1`. The approach regression then
  passed its focused fringe-yard test `1/1`; no Game View visual smoke was run.

## 2026-08-24 — The quay keeps one visible face under its rail

- The texture flicker seen from the river was exact depth fighting, not a
  texture or shader fault. Upper Paving slabs, lowered platforms and the cave
  approach all ended on the water boundary; their side faces were coplanar
  with the Quay wall there. The submerged Bed side deliberately lapped the
  same wall by `0.04 m`, adding another coincident face below the waterline.
- All three wall paths now reveal the Quay skin `0.03 m` toward the water.
  The wall grows asymmetrically rather than shifting: its landward face stays
  fixed beneath the iron-rail seat, while its river face covers the Paving
  and Bed faces instead of competing with them.
- Verification: focused Unity EditMode
  `CityRiverPlannerTests.WorldBuilder_QuayFacesCoverCoplanarPavingAndBedSides`
  passed `1/1`. Full suites and a player build were not run.

## 2026-08-24 — The vertices learn to jump, and the options page learns to scroll

- PS1 vertex jitter, the artefact of a console with no sub-pixel precision:
  its GPU took whole-number screen coordinates, so a vertex did not slide
  as the camera moved, it jumped from pixel to pixel and the triangle
  between two of them boiled. Reproduced by rounding the projected XY onto
  the grid the frame is presented on. Off by default; the strength is a
  dial on the presentation profile.
- Two facts shaped the whole implementation. The world is drawn entirely by
  **stock URP Lit** through the serialized `RuntimePrimitiveLit` material —
  423 call sites across 74 files — so there was nowhere to put a vertex
  stage until the world moved onto a project-owned shader. And the scene
  rasterizes at full resolution: the PS1 image is post-only, so a snap
  tuned to the framebuffer would land three times finer than a visible
  pixel and read as nothing at all.
- `Ps1Lit.shader` is therefore a **verbatim copy** of the package
  `Lit.shader` — produced by copying the file, not by transcribing it —
  with exactly four differences: the shader name, a shared
  `Ps1VertexJitter.hlsl` include in three passes, their `#pragma vertex`
  lines, and a header comment. Each wrapper calls URP's own vertex
  function and rounds the clip position it returns, so at strength zero the
  output is bit-identical to stock Lit and the game looks exactly as it
  did. No `.hlsl` is forked, so a URP upgrade stays a re-copy of one
  ShaderLab file rather than a four-way merge.
- Which passes snap is a correctness question, not a taste one. ForwardLit,
  DepthOnly and DepthNormals snap identically from one shared include —
  SSAO reads depth-normals, so a disagreement of one bit haloes every
  silhouette. ShadowCaster deliberately does not: the shadow map is a
  different projection at a different resolution, so no snap can agree with
  the camera's, and snapping there would make the shadow wobble
  independently of the silhouette. `Meta` must never snap (its
  `positionCS` is lightmap-UV space) and `MotionVectors` cannot be wrapped
  at all — its include declares its own `#pragma vertex`. All nine passes
  are still cloned: URP strips by pass name, so they cost nothing in a
  build and buy keyword-space equality with the stock shader.
- The snap parameters travel as a **global** written by a render-graph pass
  rather than as a material property: `UnityPerMaterial` has to stay
  byte-identical to stock Lit or the SRP Batcher stops batching every
  renderer in the game. A pass rather than `Shader.SetGlobalVector`
  because a process-wide write belongs to whichever camera renders next,
  and two cameras here must not jitter — the inventory preview, whose
  orthographic lens is a few centimetres wide, and the reflection probe,
  whose six cube faces would each round onto their own grid and seam. Both
  carry a `Ps1VertexJitterExclusion` marker. An unset global reads as
  zero, so material thumbnails and any camera without the feature keep the
  stock image for free.
- Migration: 16 materials (world, hero, thirteen bus) repointed, and the
  two editor asset-setup scripts that would have silently regenerated 14 of
  them back onto the package shader. `Ps1LitShaderParityTests` reads the
  live package file and asserts per-pass pragma equality, keyword-space
  equality, the property names the world drives through, and that no
  migrated material has fallen back — the URP bump that would invalidate
  the clone now fails a test instead of dimming the lighting.
- Three removals the user asked for in the same pass. The frame rate is
  fixed at 60 and is no longer a setting: it changes how the hero handles
  near tight geometry, so it is not the player's to move. Rain-on-lens is
  gone entirely — droplet shader code, the render state, the weather
  drive, the setting and the row. And the options list, which had quietly
  grown past the bottom of its panel, now scrolls: the window follows the
  selection, the wheel nudges it, and the rows live in one table instead of
  being hand-numbered at each call site, which is what let it overflow
  unnoticed.
- Verification: EditMode across the settings, menu, localization,
  presentation, primitive-factory and asset-import fixtures; shader compile
  and parity gates; the composite's own render tests. Visual confirmation
  of the jitter in motion is still outstanding.

## 2026-08-24 — Street dressing reaches the ground and laundry keeps clear

- Ground-level frontage and roadside decorations inherited the owning lot's
  terrace datum even after their XZ anchor moved toward the street. The
  rendered terrain is sampled separately and includes both its continuous
  slope and top offset, so furniture and utility clusters could visibly hang
  above it. Their final anchors now sample that real surface; geometry,
  collision proxies and interaction docks remain on the same descriptor.
- Residential courtyard lines no longer use the fixed lateral `2.9 m` shift
  that put their nearest pole inside the discarded-furniture cluster. A line
  now takes the free bay opposite its furniture anchor and is retained only
  when its full corridor stays on ground, clears the entrance and intersects
  no blocking decoration. The low masonry piece in that furniture recipe was
  also seated flush with the corrected origin.
- Verification: `dotnet build BarPromenade.EditModeTests.csproj` completed
  with zero errors (only the existing serialized-field warnings), and the
  scoped diff passed `git diff --check`. The focused Unity regression was not
  started because another filtered EditMode batch process already held the
  project; no full suite or player build was run.

## 2026-08-24 — Both quays hand their full walking width to the shore

- Production-layout probing found a descriptor mismatch rather than a bad
  terrain collider. Each physical north end was visibly open, but the
  seacoast contributed only a `2.2 m` connector centred on the offset
  pedestrian lane. After shrinking that rectangle by the player's `0.32 m`
  capsule, most of each logical `3 m` quay became an invisible clamp.
- `CitySeacoastPlanner` now derives one shared full-width junction rectangle
  from `promenade.Bounds` for both navigation and every granite threshold
  tread. The extra roughly `1 m` structural lip between the logical route and
  the waterside rail remains non-walkable and now has a short transverse rail
  at each bank, so visible geometry and movement permissions agree.
- Verification: focused EditMode
  `WorldBuilder_LetsTheProductionControllerCrossBothQuayShoreJunctions`
  passed for both banks, both directions and the outer/centre/waterside lanes
  with the production collider stack. The same regression samples the whole
  seam at the conservative `0.35 m` audit radius. Full suites, a player build
  and manual visual smoke were intentionally not run in fast mode.

## 2026-08-24 — Split-park benches follow the paths they serve

- The old river-park pass placed eight raw `Vector3` points at fixed offsets
  from each region's centre and plaza without consulting `ParkPath`. In the
  default seed two west benches crossed the path centreline, two clipped its
  edge, and most east benches sat loose in the lawn. Geometry, collision and
  sitting then independently assumed every plank ran along world X.
- Added the pure `CityParkBenchPlanner`: each non-bridge linear ParkPath yields
  path-side candidates, four distributed candidates are retained per park
  region, and the timber stays `0.30 m` beyond the path edge while its entry
  dock reaches the paving. One immutable descriptor now carries region,
  position and facing through elevation rebasing, oriented mesh batching,
  collision and the sit plan. Trees are placed after benches and reject their
  clearance circle; the sit dock resamples the raised path top.
- Verification: focused EditMode
  `ParkBenches_FollowRealPathsInBothHalves` passed `1/1`; it proves four
  benches in each half, path alignment/facing, a clear path footprint, a full
  entry line over ParkPath geometry and tree clearance. Unity compiled the
  changed Runtime and EditMode assemblies without errors. No neighbouring
  fixtures, full suites, player build or subjective visual smoke was run.

## 2026-08-24 — River landing cuts now have real retaining faces

- The initial platform-UV diagnosis was incomplete. The supplied gameplay
  screenshot showed that the broad pale areas were fog visible through the
  missing vertical faces of the promenade cut, not the
  `Lower Waterside Platform` renderer. The platform keeps its useful
  metre-scale `BoxProjected` paving update, but it was not the pictured gap.
- Every landing now owns one combined, collidered
  `Granite Landing Cut Retaining Walls` batch. Stepped panels rise from each
  tread to the upper promenade along the landward edge, continue beside the
  lower platform and close its terminal edge; the railed waterside remains
  open to the river. The batch uses the Quay sheet with metre-scale
  `BoxProjected` UVs so every inward-facing wall reads as granite masonry.
- Extended the existing whole-river appearance regression to require exactly
  one lining for each of the four planned landings and verify its coverage,
  collider, Quay albedo and every face UV. The platform faces remain covered
  by the same regression.
- Verification: focused EditMode
  `BuildRiver_TexturesEveryBankAndBridgeMember` passed `1/1`; Unity compiled
  the changed Runtime and EditMode assemblies without errors. No neighbouring
  fixtures, full suites, player build or subjective visual smoke was run.

## 2026-08-24 — Quay wall lamps burn around the clock

- The river builder used to merge sparse upper-promenade plafonds and the low
  wall-mounted waterside lenses into one `Embankment Lamp Glow` renderer, then
  hand that whole batch and every halo to `CityNightGlowRegistry`. Split the
  geometry into night-gated `Promenade Lamp Glow` and always-lit
  `Quay Wall Lamp Glow`; wall halos are now directly initialized and stay out
  of the night registry. The fixtures therefore remain visibly energized by
  day without adding any `Light` or changing the 12-light runtime pool.
- Added a focused river regression that drives the shared night factor to zero
  and proves upper lenses/halos go dark while every wall lens and halo keeps
  its full authored value. The river appearance resolver explicitly exempts
  both emissive batches and the new wall-halo name from ordinary surface
  texturing.
- Verification: focused EditMode
  `QuayWallLamps_KeepTheirBulbsAndHalosLitDuringDay` passed `1/1`; Unity
  compiled the changed Runtime and EditMode assemblies without errors. No
  neighbouring fixtures, full suites, player build or subjective visual smoke
  was run.

## 2026-08-24 — The known-red list stops being folklore

- `CityWetSurfaceTests.CustomGroundTint_SurvivesWetAndDryWeather` is fixed at
  the root, which turned out not to be where anyone was looking. A probe
  printing full-precision channels at each stage showed the authored
  `0.31f` already returning as `0.309999973` **straight out of
  `ApplyGroundSurface`**, before any weather runs: the
  `MaterialPropertyBlock` round-trip is not bit-exact, it drifts by one ULP.
  The wetness path is faithful (`afterDry == afterApply`) and the CPU
  arithmetic is exact, so the planned "write the dry tint exactly" change
  would have fixed nothing — the probe cancelled it. Both colour asserts in
  that file now compare per channel `Within(1e-5f)`. The neighbouring
  `Is.EqualTo(Color.white)` assert was left exact for months only because
  `1.0` is the one value that survives the drift, so it moved to the same
  helper rather than staying as a trap to copy.
- The namespace half of the test-filter trap is dead at the root: **17**
  files (not the 3 first suspected) declared `BarPromenade.Tests` while
  living in the EditMode or PlayMode folder, so any
  `BarPromenade.Tests.EditMode.(...)` filter silently dropped them and still
  reported green. All now declare the namespace matching their assembly.
  Grepped first for full-name references in code, docs, scripts and CI —
  none outside historical work-log prose. The `(.EditMode)?` escape hatch in
  filters is no longer needed; the *other* half of the trap (a filter is
  also silent about class names that do not exist) is unaffected and stays.
- Four permanent batchmode reds became honest skips instead of noise every
  full run: both synthetic bus boarding scenarios and the stairwell IMGUI
  tail now `Assert.Ignore` under `Application.isBatchMode` with a reason
  naming the cause, the stash-verified baseline date and this log. The
  stairwell cat test keeps asserting the refusal, the missing-stew prompt
  key and the restored input headless — only the panel-rect measurements
  are skipped.
- `StatusFaceFallsAndContactShadowDrive3DBonesAndCleanUp` was flaky by
  construction, not by tolerance: its post-release poll overwrote the three
  joint angles every frame and only broke out when all three dipped under
  `0.5°` in the *same* frame. The joints reach their minima a frame or two
  apart, so under batchmode pacing the break never fired and the assert
  read whichever phase the loop ended on. Each joint is now scored by its
  own closest approach across the loop. A real residue still fails: a
  constant offset lifts the whole sweep, minimum included.
- The stairwell descent red was not environment noise at all: it was a
  scene bug the harness had been hiding. Three traces got there.
  A position/grounded/heading log showed the hero walking `0.97 m` and then
  standing still for seven seconds, grounded, input enabled, W held, facing
  correctly. Motor diagnostics then showed the controller delivering
  **100 %** of every requested move — nothing was blocked — while his speed
  kept collapsing to zero and ramping up again. Collider names named the
  culprit: `562` hits on **Upper Stair Debris Safety Blocker**, contact at
  its bottom face.
- The debris that seals the upper flight sits with its underside on the
  apartment floor plane at `y = 3.20`, which
  `StairwellLayoutValidator.ValidateUpperBlocker` requires. Directly
  beneath it the lower flight climbs to the middle landing, and with a
  `1.6 m` storey and a `1.7 m` hero the two clear each other by about
  **minus one centimetre**: descending the top treads, his crown plus the
  controller skin grazed the blocker. Planar velocity is read back from
  achieved movement, so every graze cost him all his speed and he
  re-accelerated from a standstill — a crawl, not a wall, which is exactly
  why it read as flakiness. The blocker is now set back along the flight
  (`z` span `[-1.10, -0.40]`, was `[-0.90, -0.02]`); its `x` and `y` seal
  is untouched, so the validator's contract and the debris-blocks-the-climb
  test hold, and the descent has about `0.23 m` of headroom.
  `UpperBlocker_LeavesHeadroomOverTheLowerFlight` pins that clearance
  against the flight geometry rather than against a magic number.
- The test keeps `Time.captureDeltaTime` pinned to `1/60` for the walk, but
  for the opposite reason to the one first supposed: unpinned, batch mode
  runs frames far faster than real time, so the hero covered the flight
  even while crawling — which is how a physical obstruction hid behind a
  green run for weeks. Held to a real stride, the test measures the descent
  a player would actually make. The fixture now runs `6 passed / 1 skipped`
  headless, against the `5/7` with two reds the notes had called permanent.
- Frame rate was ruled out as the *cause* on the way: a sweep at
  `60/90/120/144/240/500` fps showed the hero failing to reach the bottom
  in eight seconds at *every* rate, getting a step or two further at `60`.
  The high-rate cases only made the same graze unrecoverable.
- The game is capped anyway, on its own merits rather than as a fix. It
  shipped uncapped (`QualitySettings`, one `PC` level, `vSyncCount: 0`, no
  `Application.targetFrameRate`), so a fast machine rendered this
  `640x360` composite at several hundred frames a second — the regime where
  a stride per frame shrinks past the controller's `40 mm` skin and any
  contact eats whole frames of movement. `BarPromenadeRuntimeBootstrap` now
  applies `PeriodFrameRate = 30` before the first scene, the rate the
  fixed-camera survival horror this game is shaped like actually ran at
  (Silent Hill 2 on the PS2, the first Silent Hill on the PS1). Batch mode
  is exempt so the test runner is not idled between frames.
- The rate is the player's to change: `options.frame_rate_60` joins the
  pause-menu graphics rows as the second opt-in after the 4:3 pillarbox,
  persisted through `GraphicsEffectsSettings` like the rest, and the
  options row re-applies the cap immediately rather than waiting for a
  restart. Only `30` and `60` are offered, and
  `Cap_KeepsEveryOfferedRateStridingPastTheSkin` is what forbids a third:
  it asserts that a single frame of walking still clears the controller
  skin twice over at every rate on the menu. Geometry was ruled out on the way: the flight
  runs `-Z` from the middle landing, so the hardcoded `180°` heading is
  right; the cat sits on the far side at `x = 0.72` behind a trigger, not a
  blocker; and `UpperBlockerBounds` sits at `y = 3.2-5.2`, nowhere near.
- **Latent, owned by the bus session:** root-cause the two synthetic bus
  PlayMode boarding scenarios under batch mode —
  `Passenger_BoardsRidesAndExitsAtLaterStop` waits for `Riding` and gets
  `Outside` (frame pace of the approach to the dock), and
  `AmbientPassenger_BoardsRidesAndAlightsAtALaterStop` never seats a waiter
  at the stop beyond the fog band. The `Assert.Ignore` guards come out the
  day that lands.

## 2026-08-24 — The puddles join the water

- The gutter puddles stopped being tinted Lit quads and became the city's
  fourth `CityRiverWater` material (`CityPuddleWaterResources`): the fountain
  basin's still-water recipe (flow zero, facets zero, refraction zero) with
  `_AdditionalSpecular 1.6` for lamp glints, `_ReflectionStrength 0.8` into a
  second `CityFountainReflectionController` cubemap hung 1.6 m over the road
  network's centre, and `_FoamDistance 0.002` pinned below the planner's 3 mm
  standing depth so edge foam cannot whitewash a patch.
- Drying reuses the shader's own background composite instead of alpha: a new
  `_SurfaceWetness` uniform lerps the fragment toward the sampled road, so a
  dry puddle is pixel-equal to bare asphalt under `Blend Off`. New
  `_EdgeNoiseParams` value noise (world XZ) erodes the rim first — the rim
  mask rides UV0 as a pyramid over each 3×3 patch grid in the rebuilt
  combined sheet — so puddles shrink to their middles rather than fading as
  rectangles. Planner untouched; still one mesh, one material, no collider.
- Wetness reaches water as a whole-material drive, not MPB (the water rule):
  `CityWaterResources` gained `SetSurfaceWetness` plus a `driesWithStreets`
  registration flag, fed from `CityWetSurfaceRegistry`'s throttled beats;
  river, sea and basin keep the shader default 1 and never dry.
- Verification: 4 new `CityPuddleWaterTests` (contracts, wetness routing
  reaching only the drying material, registry-driven film, builder
  mesh/mirror/no-MPB shape) passed and were confirmed by name in the results
  XML; headless captures at wetness 1 / 0.4 / 0 showed lamp glints and mirror
  on the film, centre-shrunk remnants, and a dry frame indistinguishable from
  road. Note: `CityWetSurfaceTests.CustomGroundTint_SurvivesWetAndDryWeather`
  fails at HEAD `0226bce` with a sub-print-precision tint drift **with these
  changes stashed** — pre-existing, classified via a clean-HEAD baseline run,
  not owned by this change.

## 2026-08-24 — Route 01 audio respects the visible City slice

- Replaced the actor-root, bass-only `0.08-0.24` engine voice whose
  logarithmic `48 m` limit sat well inside the `76-86 m` hidden spawn band.
  `CityBusAudio` now owns a bounded four-voice presentation: a rear-mounted
  mid-readable exterior diesel (`0.32-0.52`, linear `24-48 m`) tied directly
  to `RuntimeSceneSetup.CityFarClipPlane`, a distinct rear cabin/body loop
  faded over `0.35 s` only for the attached hero, and one dedicated pneumatic
  source above each real passenger doorway. The diesel is silent throughout
  the `76-86 m` hidden spawn band and rises only after entering the rendered
  City slice. Every voice stays fully spatial and routes to `SFX/World`.
- Door clips are deterministic low-rate mono valve/hiss/mechanism/latch
  gestures. They fire once on the real Closed-to-Opening and Open-to-Closing
  phase edges (including a coarse-step closing fallback), so neither generic
  door cooldowns nor per-frame ambience can suppress or multiply them. The
  two doorway pitches differ slightly, while their shared gain budget rises
  from `0.66` outside to `0.82` for the hero aboard.
- The first focused door run exposed a useful `2.1 mm` mismatch after the
  sprung body settled. The audio anchors now follow the live front/rear entry
  transforms after every presentation motion update instead of remaining at
  their bind poses. Pooling stops both loops and both one-shots, clears clips
  and resets phase counters.
- Verification: the full bus audio lifecycle scenario
  `SamePlanAndAdvanceSequence_RepeatsBusLifecycle` passed; after the anchor
  correction the exact regression
  `StopDwell_HoldsForTenSecondsBeforeResuming` passed `1/1`. Unity recompiled
  Runtime, Editor, EditModeTests and PlayModeTests without errors;
  `git diff --check` is clean apart from the pre-existing mixer line-ending
  warning. No full suites, player build or subjective speaker/headphone
  audition was run.
- Follow-up visibility cap: `dotnet build BarPromenade.EditModeTests.csproj
  -nologo` compiled Runtime and the updated distance regression with `0`
  errors (`154` existing serialized-field warnings). The live Unity editor was
  left undisturbed, so the updated EditMode selection was not re-run.

## 2026-08-24 — Whole-game mix puts actions ahead of music

- Audited every runtime `AudioSource` and the committed mixer. All sources
  already route through the canonical topology and no serialized scene or
  prefab sources bypass it; the actual gap was that every leaf fader remained
  at Unity's `0 dB` default in all five snapshots.
- Measured the six present MP3 masters with EBU R128. Their raw integrated
  loudness ranged from `-18.3` to `-10.2 LUFS`, which made Bar roughly `8 dB`
  louder than City before room response. Added non-destructive per-track
  source trims that converge within `0.15 dB` of a `-30.5 LUFS` in-game
  background target, plus a shared `12 kHz` music low-pass. The absent
  cemetery theme retains a neutral placeholder and must be measured when a
  master is supplied.
- Authored one snapshot-invariant gain hierarchy: Master `-6 dB`, Music
  `-5.5`, Beds `-4`, Details `+0.5`, World `+2`, Gameplay `+2.5`, UI `+1.5`.
  Detail and world reverb/echo sends were reduced by their dry-bus boosts, so
  the existing City/Bar/Stairwell/Home room identities retain their wet energy
  without masking attacks. The master compressor and transition envelopes are
  unchanged; no global sidechain was added to avoid audible pumping.
- Reclassified spatial thunder from ambient detail to World and the Home wake
  alarm from World to Gameplay. Updated dependent PlayMode expectations and
  made the mixer generator author every content leaf deterministically.
- Verification: Unity regenerated `BarPromenadeAudio.mixer` successfully and
  focused `GameAudioMixerAssetTests` passed `16/16`, including serialized
  snapshot faders, DSP routing, send compensation and music calibration.
  `git diff --check` is clean. No full suites, player build or subjective
  speaker/headphone audition was run.

## 2026-08-24 — City sound now belongs to visible physical sources

- Replaced the city filler layer with an immutable causal sound plan built
  from the runtime layout and point-of-interest geometry. The default City has
  ten descriptors owned by five visible constructions: waterworks, drying
  yard, weighbridge, last-route island and park fountain. Five are stable
  loops, three are autonomous but explained details and two can fire only from
  a real presentation event.
- Added a fixed nine-voice runtime director with fully spatial linear rolloff,
  per-source radii, deterministic scheduling and variants, mixer routing,
  building-mass low-pass/volume occlusion and a provenance ring buffer. The
  clips are generated lazily as mono, 22.05 kHz, quantized PS1-like signals;
  the former City bed now carries only a very quiet diffuse air layer.
- Bound carpet impacts to the exact authored contact frame and the renderer
  centre of the carpet that was actually struck. Weighbridge stress follows a
  real load-threshold crossing. The playground swing deliberately stays
  silent until it exposes a first-class motion event instead of receiving a
  decorative timer.
- Moved surf to the nearest point of the finite visible shoreline and gave it
  the same building occlusion model. Thunder now comes from the lightning
  azimuth after distance delay and pending balcony thunder is cancelled when
  the exterior is no longer visible. Rain remains diffuse around the listener
  by design.
- Added pure planner/scheduler/synthesis coverage plus a focused integration
  fixture for source ownership, trigger classes, occlusion tiers and the hard
  voice budget. Verification: `CitySoundscapeIntegrationTests` passed `3/3`
  after the final positioning patch; `CitySourceSoundSynthesisTests` passed
  `5/5`; `git diff --check` is clean. No full Unity suites or player build were
  run.

## 2026-08-24 — First atmospheric visual slice: wet streets, district windows and four bar rooms

- Audited the current runtime-composed world before changing it and kept the
  slice deliberately presentation-only: no scene layout, circulation,
  colliders, fog distance, day/night schedule or realtime-light budget moved.
- Replaced the PC pipeline's borrowed sample-scene volume reference with the
  project-owned `PCPresentationBaselineVolumeProfile`. It preserves the
  effective Neutral tonemapping, Bloom (`0.25`, threshold `1`, scatter `0.5`,
  HQ) and Vignette (`0.2`) baseline. The Supermarket's runtime-only volume now
  states the same baseline explicitly before its local depth of field.
- Added the pure `CityDistrictArtProfile` / presentation plan and planner for
  frontage, mass, window, light, wear and one-block authored transitions. The
  first live consumer is the ordinary facade window resolver: Old Town uses
  irregular low occupancy, Residential warm apartment clusters, Industrial
  sparse task lights and Nightlife a bright road-facing base under dark upper
  and rear facades. Bar/Home/Supermarket families still short-circuit. A
  focused variation-key API avoids a per-pane plan allocation, while seeded
  2/3-pane grouping and phase avoid the former global domino cadence.
- Added one cross-scene wet-film registry driven by the existing deterministic
  weather. Ground, roads, sidewalks and markings wet at `0.58/s`, dry at
  `0.028/s`, preserve authored dry tint through multiplicative MPBs and retain
  state across City/Home handoffs by absolute game time; a new run resets it.
  `CityPuddlePlanner` stable-ranks at most `42` grounded patches and the world
  builder emits only their upper faces in one collider-free, shadowless mesh
  `3 mm` over the road. Application is quantized to `0.01` wetness steps to
  bound MPB churn without losing continuous state accumulation.
- Expanded `BarDistrictIdentity` from a narrow accent into a coherent surface,
  sign, glass and practical-light family over the unchanged validated room.
  One safe wall-scale motif distinguishes each room: Old Town ledger/missing
  portraits, Residential worn surfaces/curtains, Industrial safety band/pipes
  and Nightlife cyan/magenta neon. The Residential large-surface tints remain
  identical to the packaged texture-generator contract.
- Added focused EditMode coverage for profile transitions/window schedules and
  temperature, film timing/tint restoration/re-registration, puddle grounding,
  bar identities/surface compensation and the explicit PC/Supermarket volume
  baseline. Three independent static reviews found and closed the authored-tint,
  fixed window-pair cadence, rear Nightlife glow, per-pane allocation and
  puddle-selection/seam risks.
- Verification: Unity batch import completed a clean Tundra script compilation
  (`949` items evaluated, no C# errors), and `git diff --check` is clean apart
  from pre-existing player-work line-ending notices. The requested focused
  test runner did not produce XML: the sandbox attempt lost Package Manager
  IPC, the first external attempt used a locally incompatible `-quit` flag,
  and the corrected no-quit launch collided with a second Unity project
  process and returned before discovery. No full suites or player build were
  run.

## 2026-08-24 — The wipers arc like wipers, and they actually wipe

- Three user requests on the glass rain. (1) The static bead layer is
  gone — only the running drops remain. (2) The wiper motion was the
  Body-axis trap again: `ResolveForwardAxisLocal` against the
  imported Body node handed the blades the vehicle VERTICAL, so they
  swung door-style around their base — which read as a broken
  fulcrum. The reference is the vehicle root now and the blades arc
  across the windshield around its normal (the authored pivots at the
  arm bases were always correct). (3) The blades now truly wipe: the
  droplet shader carries a per-frame wipe mask — pivot, blade angle
  and sweep direction per wiper, pushed in each pane's own
  coordinates from `AdvanceWipers`. The blade angle is MEASURED from
  the visible blade tip every frame (tip captured in pivot-local
  space at initialization from renderer bounds), so the mask can
  never disagree with the drawn arm; drops ahead of the blade wait
  for it, behind it the glass starts clean and regrows toward the
  return stroke. The mask gates on panes facing the bus forward, so
  side windows keep their drops.
- Test-infrastructure lesson, now in memory: the asset-import test
  classes live in `BarPromenade.Tests` WITHOUT `.EditMode`
  (`CityBusAssetImportTests`, `CityBusDriverAssetContractTests`, like
  `Player3DAssetImportTests`), so a
  `BarPromenade.Tests.EditMode.(...)` filter silently dropped every
  new bus contract test while the rest kept the run green — the
  steering and glass-rain guards "passed" without ever running. The
  correct filter is `BarPromenade.Tests(.EditMode)?.(...)`, and a new
  test's NAME must be seen in the results XML at least once.
- Verified with the fixed filter: 41/41 including
  `Wipers_ArcAcrossTheWindshieldAndDriveTheWipeMask` (arc around the
  windshield normal, no vertical swing, mask radius from the measured
  blade, mask parks with the rain),
  `Steering_YawsFrontWheelsAndRollsTheColumnWithTheTurn` and
  `GlassRain_OverlaysCoverEveryPaneAndFollowIntensity` — all three
  now genuinely executed. Visual QA: exterior windshield captures at
  two sweep phases show the blades arcing and the wiped sector
  trailing them (TestResults/wiper-upswing.png, wiper-downswing.png).

## 2026-08-24 — Rain reaches the bus windows, and drops run down the glass

- The user flagged that a rainy ride reads dry from inside the bus.
  Two causes, two fixes. (1) The rain field's sheltered donut kept a
  10 m rain-free core around the follow target — every streak stood
  past the fog's teeth. `CityRainField.ShelterHoleRadius` is now
  6.5 m: the 8.25 x 2.38 m body has a 4.3 m half-diagonal, the rest
  is wind-drift margin, and the donut band becomes 6.5-19.5 m — rain
  stands right outside the glass. Pinned by
  `CityRainFieldWindTests.ShelterHole_HugsTheBusButKeepsRainAtTheGlass`.
- (2) New droplet overlays: `CityBusPresentation` clones every
  Glass-slot pane (4 door leaves + the combined `GLS_Windows`) into a
  child renderer carrying `Bar Promenade/City Bus Glass Rain` — a
  procedural runner-and-bead shader driven per frame by the same
  `RainIntensity` the wipers already receive (`AdvanceWipers` ends by
  pushing it into per-overlay MaterialPropertyBlocks; dry glass
  disables the overlays entirely). The shader computes its pane
  coordinates in WORLD metres re-anchored to the pane object's
  origin: the imported glass nodes carry neither the vehicle basis
  (object Y runs along the bus) nor metre units (the transforms bear
  a 100x scale), so object-space patterns collapsed into sub-pixel
  noise — the same import-basis trap as the wheel pivots and the
  shelter heights, now documented in the shader.
- Guard: `CityBusAssetImportTests.
  GlassRain_OverlaysCoverEveryPaneAndFollowIntensity` — one overlay
  per Glass binding reusing the pane's own mesh, disabled while dry,
  intensity mirrored into the property block, drying back off.
  Verified visually via edit-mode captures (door leaf and the full
  glazing band show chunky trails and beads at intensity 0.9).

- The user flagged two steering bugs the grand loop finally made
  visible (30-45 manoeuvres per lap where the old tour had a handful;
  no bus re-export happened — these were latent since the model
  landed). Probe-measured in root space: at steer +20 (a right turn)
  the front-left steering pivot rotated 20° around (0, 0, -1) — pure
  camber, the wheels LEANED into corners — and the hand wheel rolled
  +71° around (0, 0, +1), which the driver, watching from the axis
  tail, sees as counterclockwise: the rim spun LEFT on right turns.
- Two one-line causes in `CityBusPresentation`. (1)
  `ResolveVerticalAxisLocal` took `registry.Body` as its vertical
  reference, but the imported Body node's own up reads (0, 0, -1) in
  root space — the very import rotation the resolution exists to
  absorb — so the "vertical" it derived WAS the longitudinal axis;
  the reference is now the vehicle root transform. (2) The steering
  column axis binding points at the windshield, and a positive Unity
  rotation reads counterclockwise to the viewer the axis points away
  from; the applied axis is now negated so a positive (right) steer
  rolls the rim clockwise under the driver's hands. The grips are
  children of the rim, so the driver's hands follow for free.
- New contract guard
  `CityBusAssetImportTests.Steering_YawsFrontWheelsAndRollsTheColumnWithTheTurn`:
  both steering pivots must yaw +20 around the vehicle vertical with
  under 1° of longitudinal roll, and the column's signed roll toward
  the driver must equal `SteeringWheelAngle` — so the next re-export
  cannot silently reintroduce either bug. Wipers still resolve their
  sweep axis against Body (`ResolveForwardAxisLocal`) — same latent
  trap, left untouched pending a visual check.

- The user exercised the documented escalation immediately: stops 1
  and 2 (home + supermarket, 33.2 m planar, 338 m apart along the
  loop) read as "the bus detours and arrives practically where it
  left". `MinimumPlanarStopSpacing` rose 30 → 35 as planned, letting
  the retention ranks resolve both spared named pairs.
- The forecast ceiling veto turned out to be the wrong shape: it
  blocked dropping the supermarket because home@5 → loop-south@503
  would leave a 498 m hole, but it could not know a refill insertion
  would stand a fresh pole mid-corridor. `CoalescePlanarCloseStops`
  now tries empirically: drop one pair member, rerun
  `InsertSpacingStops` (which refills any gap past 200 m with
  planar-clear poles), and only if the refilled loop still holds a
  gap past 450 m roll back — first onto the pair's other member, then
  into a permanent protection of the pair. Termination is monotonic:
  refill poles respect the planar floor by construction, so the
  violation set only shrinks. `InsertSpacingStops` now seeds its
  loop-* suffix counters from the poles already standing — the refill
  probe minted a duplicate "loop-east" id (and with it a duplicate
  localization key) when the counters restarted.
- Production roster 29 → 28: the supermarket pole merges into the
  home stop (the shop door stays a 33 m walk from it), and the
  CEMETERY loses its named pole — its gate remains served by the
  nightlife stop 31.4 m away plus the refilled tail pole
  (loop-east-2@5488, intervals 260/135). Agreed escalation if the
  user wants the cemetery pole back by name: exempt OpenAreaAccess
  stops from planar drops. The home corridor gets a refilled
  loop-east@352 (intervals 347/151); no planar pair under 35 m
  remains (tightest 35.4 m), the 450 m ceiling holds everywhere.

## 2026-08-24 — The loop's folded poles coalesce across the map

- The user flagged stops standing practically together (map numbers
  3+6 and 22+23). `CoalesceCloseStops` only measures ALONG the loop
  and only consecutive stations, so it never saw the loop folding
  back on itself: the production roster carried nine planar pairs of
  9.8-24.8 m — 24+27 at 9.8 m, four pairs at 13.8 m (including both
  the user's), plus 18.4/19.5/21.9/24.8 m folds. On a single one-way
  loop such neighbours are redundant by construction: the same bus
  calls at both.
- New `CoalescePlanarCloseStops` pass in `CityBusPlanner.Create`
  (after `InsertSpacingStops`, before `GroundShelterPositions`):
  deterministic pair scan in list order, one drop per pass with
  rescan, reusing `SelectStopToDrop`/`GetStopRetentionRank` (home and
  district points of interest never drop). A drop that would tear an
  along-loop hole past `MaximumCoalescedStopGap` (450 m, the spacing
  test's ceiling) falls back to the pair's other member — needed
  exactly once: pair 10+31 (21.9 m), where dropping 31 tears the
  loop's tail to 538 m but dropping 10 leaves 301 m. Insertion
  candidates in `InsertStopsIntoGap` now also check planar clearance
  against every already-planned pole, so a future layout cannot
  reinsert a fold twin.
- `MinimumPlanarStopSpacing = 30f`. The insertion-time clearance did
  more than expected: instead of inserting fold twins and dropping
  them again, the spacing pass now places its poles planar-clear from
  the start, so the production roster settles at 35 → 29 stops with
  every change confined to ANONYMOUS spacing poles — no named stop,
  gate, localization key or NPC waiter anchor is touched (the
  position-derived loop-* suffixes regenerate, which renames some
  surviving spacing poles). Zero pairs under 30 m remain; the
  tightest pairs stand at 31.4 m, mean along-loop interval 193.7 m,
  loop 5618 m. Known decision: the named pairs just above the floor —
  home+supermarket at 33.2 m and nightlife+cemetery at 31.4 m — are
  deliberately spared; the escalation path if the user objects is to
  raise the floor to 35 m and let the retention ranks resolve them
  (supermarket merges into home, the cemetery gate into the nightlife
  point of interest).
- `StopSpacing_StaysRegularAlongTheLoop` gained an all-pairs planar
  assertion (both-mandatory pairs exempt) and swapped its
  two-thirds-within-200 m quota for a mean-interval bound
  (`loopLength / stops <= TargetStopSpacing * 1.5`): the quota
  measured an artefact — the folds' regularity was held up by the
  very duplicate poles this change removes; `withinTarget` stays as a
  diagnostic print.

## 2026-08-24 — Bus shelters stand on the pavement they were drawn over

- The user could not sit on the home stop's shelter bench:
  `PlayerAnimatedInteractionController` aborted with "Animated
  interaction entry was blocked; current=(152.68, 8.14, -6.22),
  target=(152.68, 8.22, -6.22)" — a pure 8 cm vertical mismatch
  against the motor's 2 cm interaction tolerance, hit after the 1.5 s
  stall guard.
- Root cause: `CityBusTargetRoutePlanner.GetShelterPosition` set the
  shelter's Y analytically (road centre-line height plus the constant
  kerb step). On the graded boundary street the boxed sidewalk's real
  top at the shelter was 8 cm lower, so the whole shelter, its bench
  plank and the authored sit entry floated. `ResolveSeatDockGround`
  in `CityBenchSitPlan` could not save it: it only RAISES from
  `seat.GroundY` (`Mathf.Max` against sampled walkway tops), so an
  inflated baseline passes straight through — and the existing
  `CreateAll_DocksResolvedSeatsOnTheWalkableSurface` test replicates
  the same Max-raise from the same baseline, which is why it stayed
  green. Same bug class as the ride-dock height fix that already
  taught `CityBusRidePlan.ResolveGroundedRootY` to sample
  sidewalk+street boxes max-wins — the shelter placement had simply
  never received it.
- Two-part fix. (1) A `GroundShelterPositions` post-pass in
  `CityBusPlanner.Create`, after target/coalesced/inserted stops are
  final, re-grounds each descriptor's `ShelterPosition.y` through
  `TryResolveShelterGroundTop`: the pole plants on the district strip
  just OUTSIDE the pavement edge (`halfRoad + 0.2`), so the continuous
  terrain (`CityTerrainSurfacePlan.TrySampleGroundTop`) samples first
  and the sidewalk/street boxes (new public
  `CityBusRidePlan.TryResolvePhysicalSurfaceTop`) may only raise it —
  every consumer (stop visual, ride plan's `localSidewalkTop`, wait
  points, shelter bench GroundY) now agrees. (2)
  `ResolveSeatDockGround` semantics: the authored `seat.GroundY` is
  only a fallback for docks NOTHING samples; a sampled terrain or
  walkway surface wins in both directions, because it is what the
  sitter's CharacterController grounds on. Cemetery/plinth seats keep
  their authored fallback (their docks sample no walkway boxes).
  `GetShelterPosition` keeps its analytic height for the planner's own
  distance/exclusion checks and now says so in a comment.
- New EditMode guard `CityBusCoverageTests.
  EveryShelter_StandsOnThePhysicalPavement`: every production stop's
  shelter Y must equal the sampled ground within 1 mm (fails on the
  pre-fix code). `CityBenchRestTests.
  CreateAll_DocksResolvedSeatsOnTheWalkableSurface` was updated to
  mirror the new trust-the-sample semantics — its old expected-ground
  arithmetic replicated the same Max-raise from the same inflated
  baseline as the code, which is exactly why it never caught this.

- The user asked for classic tank controls: S must back the hero up
  instead of spinning him around, A/D must turn him in place instead of
  strafing, and translation happens only on W (steering an arc when
  combined with A/D). Implemented in `PlayerMotor`: A/D yaw the root at
  `150°/s` (scaled by the intoxication speed multiplier), W targets
  `2.6 m/s` along `transform.forward`, S targets `1.4 m/s` backwards
  (`BackwardMoveSpeed`), the accel/brake inertia model is unchanged, and
  `FaceMovementDirection` no longer runs during input locomotion (it
  stays for scripted `WalkPlanarStep` approaches). The whole
  camera-relative steering path — `CameraRelativeDirection`, the
  camera-cut latch, the motor's `Camera` dependency — is deleted;
  `ReadMovement` clamps the axes independently so W+A keeps full
  forward speed.
- `IPlayerMotionPresentation.SetMotion` now takes a `PlayerMotionSample`
  (planar velocity, signed forward speed, turn input). The 3D
  presentation grew a five-input locomotion mixer (Idle, Walk,
  WalkBack, TurnLeft, TurnRight) with per-gait SmoothDamp weights,
  renormalised only at the applied-weight level; `locomotionBlend` is
  now the total gait weight, and `UpdateFootPlant` reads the dominant
  gait playable. Turn-in-place engages below `0.25 m/s` with
  `|turn| > 0.2`.
- Three new Blender actions (32 → 35): `WalkBack` is the eight walk
  landmarks in reverse time (keeps the opposite-arm-to-leg relation for
  free); `TurnLeft`/`TurnRight` are one-second four-phase step-turns
  (wind, inner-foot lift, plant, outer-foot drag), the right one
  hand-mirrored per the walk convention. Signs were probe-verified
  before authoring: +Y on pelvis/chest yaws the character to his own
  LEFT (facing +18.9° at +20°), −X on a thigh lifts that leg. Importer
  `LoopingClips` gained the three names; the manifest `action_count`
  self-updates; the runtime prefab was rebuilt headlessly.
- Tests: `PlayerMotorHeadingPlayModeTests` rewritten for tank controls
  (back up without turning, turn in place without translation, W+D
  arcs, opposite input brakes then backs up without spinning); the
  camera-cut latch test is deleted with the latch itself (it was also
  the known batchmode-flaky one). The stairwell descent test now aims
  the hero down the flight and holds W; the bed wake test presses W
  (D no longer translates). `Player3DAssetImportTests` pins 35.
  New presentation coverage: backward/turn samples select the
  dedicated states, and the authored-joint-range gate samples the three
  new clips. `StatusFaceFallsAndContactShadowDrive3DBonesAndCleanUp`
  was tipping over its 0.5° neutral-pose asserts because the idle loop
  itself swings the pelvis ±2° — the comparison now polls across one
  full idle loop instead of trusting phase luck.
- Verification: EditMode `Player3DAssetImportTests` 4/4, visibility and
  stairwell-cat suites green; PlayMode motor+presentation suite 18/19
  with the one failure fixed as above. The visual QA contact sheet
  gained `WalkBack` and `TurnLeft` tiles (2×3 grid).

## 2026-08-24 — Stops step away from doorways (and off the steepest ramps)

- The user flagged stops standing practically on top of bar entrances.
  Nothing in the planner knew about doors: a stop excluded only its own
  target's bounds, so a pole and its four-metre shelter could land
  across any bar, home or supermarket doorway.
- New entrance clearance in the planner
  (`CityBusCoverageStopPlanner`): every bar, home and supermarket door
  (its `SidewalkArrivalPosition`) projects a `7 m`
  `BuildingEntranceClearance` zone checked at the pole AND the shelter
  wall centre, leaving ~`4.9 m` of daylight from the nearest shelter
  piece to the door. A blocked placement slides along its link in
  `0.5 m` steps (forward first) to the nearest clear spot — both for
  target candidates (`TryCreateStopCandidate`, which then re-measures
  its reference distance so sorting judges the pole where it really
  stands) and for inserted street stops. A link with no clear span is
  rejected. New `CityBusCoverageTests.Stops_KeepClearOfBuildingEntrances`
  pins the contract for every stop × every door.
- The reshuffle exposed two grade bugs. First, stop candidates now
  carry `IsGraded` and sort level-first (coverage: penalty, grade, hop;
  mandatory: hop, penalty, grade) — grade stays a last resort, never a
  ban: an outright ban emptied the route (a hillside POI lost every
  candidate) and starved the eastern plateau climb of inserted stops
  (an 880 m stopless hole).
- Second, the real dock-height fix: `CityBusRidePlan.ResolveGroundedRootY`
  sampled sidewalk boxes first and street boxes only as a fallback, so
  where a graded sidewalk's end dips under the flat junction pad the
  plan reported the buried slope — the supermarket stop's docks missed
  the physical surface by up to `6.5 cm`. Sidewalks and street/apron
  boxes now sample together and the highest surface wins, matching the
  physics raycast by construction.
- Production roster: 35 stops on a `5618 m` loop (the supermarket
  regained its own pole once home slid clear of the shared bar door),
  minimum gap `82.9 m`, worst named-destination walk still `55.5 m`.
- Verification: bundled-dotnet compile clean; EditMode bus + coverage +
  wait + runtime + map + localization + layout + bench + stop-builder
  suites 68/68 green; PlayMode
  `ProductionCityRoute_AllStopsExposeBothDoorPrompts` and
  `ProductionCityDoorDocks_MatchPhysicalSurfaceHeight` green. The two
  synthetic bus PlayMode boarding tests remain the stash-proven
  batchmode baseline reds, untouched.

## 2026-08-24 — Close stop pairs coalesce into one pole

- The user flagged the flip side of the grand loop: in places the
  stops now stood absurdly close together. Target stops anchor to
  destinations independently, so a gate, a park gate and the
  supermarket could pile onto neighbouring kerbs — the minimum
  spacing only ever bound the inserted street stops.
- New `CoalesceCloseStops` pass between target-stop creation and the
  spacing pass: any pair closer than `MinimumStopSpacing` (`80 m`)
  along the loop loses its less essential member (retention order:
  home/POI never dropped, then gate > park gate > supermarket >
  street stop; equals keep the earlier). The route geometry is
  untouched — the bus still drives past the dropped gate, whose
  destination the surviving neighbour provably serves.
- Production roster: `36 -> 33` stops on the same `4980 m` loop;
  dropped `yard-east`, `yard-west-south`, the `north-waterfront-wild`
  spread anchor and the supermarket pole, each absorbed by a
  neighbour on the same street. Minimum along-loop gap rose to
  `87.1 m`; worst named-destination walk stayed `55.5 m`, worst gap
  stayed `343.3 m` (the doubled-back west approach corridor).
- Tests now hold the minimum for EVERY adjacent pair except
  home/POI pairs (`StopSpacing_StaysRegularAlongTheLoop`, which also
  prints the full roster), `OpenAreaStops_ServeTheirGates` asserts
  every gate keeps some pole within the `150 m` walk budget instead
  of demanding its own, and `SemanticStops` bounds coverage-stop
  counts instead of pinning them.
- Verification: bundled-dotnet compile clean; EditMode
  `CityBusPlannerTests|CityBusCoverageTests|CityBusStopWaitPlannerTests|`
  `CityBusRuntimeTests|CityMapBusOverlayTests|LocalizationCatalogTests|`
  `CityLayoutGeneratorTests` 63/63 green, plus stop consumers
  `CityBenchRestTests|CityBenchSitTests|CityBusStopWorldBuilderTests|`
  `CityPedestrianRuntimeTests` 26/26 green.

## 2026-08-24 — Route 01 becomes the grand city loop

- The user called the five-stop bus route "absolutely broken and
  illogical": it toured the four district POIs and Home over ~1.9 km
  and ignored the beach, the cemetery, the park and every yard. The
  brief: pass through as much of the city as possible, stop at a
  steady sensible interval, and put any point of the city within a
  walk of a stop — road-network changes allowed if realistic.
- The one road-network change needed was not an edge but a rule:
  `CityBusIntersectionSelector` refused to pour corner pads onto
  `OpenGround`, so the entire boundary ring (x=0, z=0, x=13) was
  turn-incapable and no route could ever reach the fringe. Yard
  ground is supporting now; ~30 boundary nodes gained Road v2.1
  aprons and nothing else moved (the widening is monotone).
- Targets grew from 5 to ~21: every `OpenAreaAccess` gate, two
  synthetic eastern waterfront anchors (the beach is 440 m long and
  its only gate is on the west bank), the outermost park gate per
  bank, the supermarket. Ordered by perimeter station,
  counter-clockwise so the doors face the precincts and the river is
  crossed exactly twice by construction; coverage targets are
  droppable, bank-filtered, capped after the cycle prune, prefer the
  precinct-side kerb over hop count, and anchor `8 m` beside their
  gate because a shelter projected into the `8.8 m` approach throat
  is always rejected.
- Three planner mechanics had to change for the strict phase to
  close at 21 targets: connectors are Dijkstra by metres (the old
  link-count BFS scored a two-edge wide-right as one hop and wove
  spaghetti), the stop prohibition is per driving direction rather
  than per edge (the opposite kerb is a different pole), and a
  spacing pass inserts numbered street stops wherever an along-loop
  gap exceeds `200 m`, only on directed streets the loop drives
  exactly once.
- Production layout: 36 stops on a 4980 m loop, built in ~0.4 s,
  every named destination within 55 m of a stop by pavement graph,
  worst pavement metre 229 m (a sunken riverside walk), worst
  along-loop gap 343 m, 31/36 gaps at or under 200 m. New
  `CityBusCoverageTests` pins coverage, spacing, wait-point
  completeness and gate service; `ru`/`en` gained 45 stop names.
- Verification: EditMode
  `CityBusPlannerTests|CityBusCoverageTests|CityBusStopWaitPlannerTests|`
  `CityBusRuntimeTests|CityMapBusOverlayTests|LocalizationCatalogTests|`
  `CityLayoutGeneratorTests` 63/63 green; full EditMode 1411/1411
  green; PlayMode `ProductionCityRoute_AllStopsExposeBothDoorPrompts`
  and `ProductionCityDoorDocks_MatchPhysicalSurfaceHeight` green after
  teaching the prompt diagnostic that a graded dock's own Y is the
  road height. The two synthetic bus PlayMode tests fail headless on
  the clean baseline too (stash-verified) — pre-existing, untouched.

## 2026-08-23 — Every fixed lamp gets the blurred ball it is in fog

- Standing on a bridge, the new quay lanterns and their glints were
  invisible: an emissive lens is a couple of pixels, and the
  exponential-squared fog eats a warm point by ~25-30 m. The user
  asked for lamps visible at that distance "but blurred" — and for
  the principle to hold for every light source in the city, the
  bridge being only the example.
- The mechanism the city already had for this is `CityLightHalo` —
  the soft two-particle billboard the pooled lights and the one-off
  site lights carry. New static factory
  `CityLightHalo.CreateNightRegistered` builds a halo with no Light
  of its own and hands it to `CityNightGlowRegistry`, which grew a
  halo list next to its renderers: dead by day, full at night, the
  same night-factor path as every electric glow. Rolled out to every
  fixed lamp that lacked one: the river's waterside lanterns AND its
  upper embankment posts, every street mast (halo stands apart from
  the anchor — the night presentation test pins anchors bare), and
  the seacoast esplanade posts. The hut door lamp, hand lamp,
  cemetery, park, bars and practicals already carried theirs.
- With every fixture wearing its own halo, the pooled spots' 
  travelling halos would double the blob on arrival — so the pool now
  carries light alone: `pooledHaloVisible[]` keeps a slot's halo
  hidden except for the leased fringe practical (which has no static
  duplicate). `CityNightPresentationPlayModeTests` updated to match:
  every realtime light still owns a halo on the atmosphere material,
  but only the bar lights must show theirs.
- The river's glints also came up to the fixtures now standing over
  the water: `_AdditionalSpecular` 1.2 → 2.0 (the sea's value) and
  `_SpecularPower` explicitly 24 (down from the inherited tight 48,
  toward the sea's 20) so a lantern lays a glitter road the fog can
  be seen eating rather than a pin-prick it swallows whole.
- Verified: all three csproj compile clean; filtered EditMode green
  40/40 (river planner incl. the halo-count contract for wall +
  upper lamps, surface appearance with the "Fog Light Halo"
  exemption, night atmosphere incl. the practical-only pooled-halo
  visibility, river water, seacoast planner). Not run: the PlayMode
  night presentation fixture (edited mechanically; scene-based run
  is expensive) and full suites. Play-mode eyeball still owed.

## 2026-08-23 — The quay wall hangs its lanterns over the river

- Asked for lamps down on the concrete rise so the parapet view reads
  a row. The wall had nothing: the upper embankment lamps stand 52 m
  apart against a fog that kills a warm point by ~30 m, so the player
  never saw a rhythm. New waterside lanterns hang on both quay wall
  faces — back plate, arm, hood (iron batch "Waterside Lantern
  Brackets", collider-free, the name deliberately avoiding the
  appearance resolver's "Quay Wall" substring trap) and a lens riding
  in the same "Embankment Lamp Glow" batch and registry entry as the
  upper plafonds. `CreateQuayWallLampPositions`: 13 m pitch, lens at
  the water datum + 1.02 (the datum falls 2.4 → 0 toward the sea, so
  the row follows it), skipping the three bridges (6 m), the landing
  frontages (1 m) and the south cave approach (first lamp z = −143,
  the art bible keeps the plug dark) — 18 per bank.
- The nearest fixtures burn with real light at no budget cost: each
  lantern also plants a "Quay Lamp Anchor" aimed down-and-across the
  channel, and the anchors join `CityNightAtmosphere`'s nearest-first
  pool (12 lights total, 8 pooled — unchanged). The pool distinguishes
  them by index alone: past `quayAnchorStartIndex` a slot takes the
  low wide profile (6 / 10 m / 130° / 70° against the street 31 /
  16.5 / 105° / 55° — the lens hangs ~1 m over what it lights, not
  4.7) and the anchor's own authored aim, the practicals' convention.
  Plumbing is the `FringePracticalAnchors` shape: river builder `out`s
  the anchors → `CityWorldResult.RiverQuayLampAnchors` →
  `CityGameRoot` → `InitializeLighting` → `Initialize`, all via
  optional parameters so every existing call site compiles untouched.
- The river now glints: `_AdditionalSpecular` 0 → 1.2 (the fountain's
  value; the sea's 2.0 is too loud for a narrow channel seen from a
  quay right above it). The old rationale — lamps too far up the bank
  to glint — died the moment the lanterns came down to the water;
  both stale comments (material and shader Properties) rewritten.
- Verified: runtime + test csproj compile clean; filtered EditMode
  green 18/18 (planner incl. the new pitch/skips/datum-height test,
  atmosphere incl. the new pool-profile/re-stamp test, river water)
  plus 10/10 appearance (the walk accepts both new batches). Not run:
  full suites, play-mode eyeballing of the row and the glints.

## 2026-08-23 — The water learns to catch the lamps and the lighthouse

- Asked for light-source glints on the water, the lighthouse included.
  Found the lamp glints already written and already dead: the renderer
  runs Forward+ (`PC_Renderer.asset`, `m_RenderingMode: 2`), where URP
  forces `_ADDITIONAL_LIGHTS` off and hands lights out through the
  cluster list — `CityRiverWater.shader` never declared
  `_CLUSTER_LIGHT_LOOP`, so its additional-light loop compiled into a
  variant that never ran. Added the pragma and restructured the loop
  through `LIGHT_LOOP_BEGIN/END` over a local literally named
  `inputData` (the cluster macro is textual — `HomeOccluderDither` was
  the in-repo precedent), keeping the classic pair as the plain-Forward
  fallback. The pier hand lamp and the boat-hut bulb now lay their
  glitter on the sea, as `_AdditionalSpecular 2.0` always intended.
- The lighthouse got a virtual lamp instead of a real one — the island's
  "never a real Light" stands. Four new tail properties in the water
  CBUFFER (`_LanternPosition` xyz + 1/range², `_LanternColor`,
  `_LanternGlint` defaulting 0 so river and basin stay dark,
  `_LanternBeamDir` as sin/cos azimuth + cos of the flash half-width):
  the same banded Blinn-Phong as the fixtures, windowed by a soft
  distance falloff (range 60 — fog and the 48 m far clip close first),
  swept by `abs(dot(bearing, beamDir))` folding the two opposed beams
  into one line so the streak crosses the sea in step with the cones,
  over a 0.15 constant shimmer, dimmed to the controller's 0.6 day
  floor by `_NightFactor`. `CitySeaResources.ConfigureLighthouse` is
  the `ConfigureShoreFade` shape (island builder pushes position once,
  re-applied through `Configure` against build order);
  `SetLanternBeamAzimuth` is fed per frame from the lantern
  controller's `Apply` with the very azimuth the pivot was turned to,
  and writes only the cached material.
- Verified: runtime + test csproj compile clean; filtered EditMode run
  green, 35/35 — the shader-compile gate, the wave-model contract
  (`WaveField`/`ShoreEnvelope` untouched), the fountain/seacoast
  material invariants, and two new tests: the island build lays the
  glint on the sea alone (river and basin keep the shader's zero) and
  the azimuth push mirrors the rules' `Atan2(x, z)` convention and
  14° half-width cosine. Not run: full suites, play-mode eyeballing of
  the sweep.

## 2026-08-23 — The water learns what the mattress knew

- Asked for water "on the mattress's principle" — real waves, not a flat
  sheet. Measured why the existing water read flat despite already being
  displaced geometry: amplitudes of 5–9 cm across 44 m sheets (under a
  640×360 pixel of silhouette), smooth interpolated normals drowned by
  the ripple map, the wave normal reaching the screen only through
  fresnel and a banded glint (sea/basin refraction is 0), and the
  river's second train at 1.80 m wavelength on a 1 m grid — below
  Nyquist, aliasing into vertex noise.
- The mattress mechanism (CPU `SetVertices`) would cost ~22.5k sea
  vertices per frame and break the world-XZ seam invariant, so its
  *lessons* moved into the shader instead: `_SlopeGain` (the bed's
  `ExaggerateDentShading` — lateral normal steepened past the honest
  tilt), `_FacetStrength` (the bed's per-cell-quad faceting, taken from
  screen-space derivatives of the displaced surface so the welded grid
  its tests pin survives), `_CrestShading` (crests toward the shallow
  tone, troughs toward the deep — value change is what survives the
  composite), and relative-crest whitecaps banded with the edge foam.
- The sea got a real swell: `0.09 → 0.20` wave height, flow
  `(0, -1)` shoreward at `_FlowSpeed 0.38` so the rollers travel
  (~0.85 m/s) instead of standing and breathing, and a shore fade —
  amplitude enveloped by world Z from a 0.35 floor at the inner shelf's
  seaward edge to full 12 m out, with the envelope's derivative folded
  into the analytic slope by the product rule so the normal never
  detaches from the surface and sheet seams stay invisible. Clearances
  that size the swell: crest 0.346 m vs pier deck underside +0.60
  (validator floor +0.40), faded trough 0.121 m vs the inner shelf's
  0.15 m; the barge sits on the bed so a trough shows wet hull, not
  air. River: defaults now `0.08 / 4.8 m` (Nyquist fixed), basin stays
  pond-calm with facets off — a faceted normal would shatter the
  Morrowind mirror. `CrestAllowance 0.25 → 0.45`.
- `CityWaterWaveModel` + `CityWaterWaveProfile`: a pure C# mirror of
  the shader's displacement (trains, breathing, shore envelope), the
  bed-model pattern of physics living where they can be asserted. The
  fisherman's float now rides it in `LateUpdate` — XZ still pinned,
  Y on the drawn swell. `CityWaterWaveModelTests` holds the mirror to
  the shader source literal by literal, the analytic slope to the
  finite difference across the shore ramp, the crest to its ceiling
  and the faded trough to the shelf; the planner test that pinned
  "stands still" now pins "rolls shoreward".


## 2026-08-23 — Wind dressing scaled to be met on a walk

- A gameplay test reported the wind dressing invisible: walking the city
  showed practically no cloth. A diagnostic dump of the default seed
  explained it — 29 pieces over a ~390×335 m city, because every pass
  took only the first 1-2 anchors of its kind while the city actually
  plans 12 markets, 20 scaffoldings, 18 furniture frontages, 18 pipe
  racks and 19 fire escapes; worse, the industrial pieces hung from the
  landmark tower's rooftop gantry at 45 m and both billboard skirts at
  ~50 m — invisible in principle (all ten nightlife billboards ride
  towers).
- Fixes in `CityWindDressingPlanner`: anchors are now picked by stride
  across each kind's whole sorted list (markets ×4 with two rags each,
  scaffolds ×3 with the shroud moved from the inner top ledger to the
  outer level-3 guard rail where it reads from the street, fire escapes
  ×8, pipe racks ×4 with a street-side tarp + sling pair at 2.85 m);
  residential courtyard lines went from a head-of-list 2 to up to 6,
  thinned by an 18 m spacing rule instead of a fixed pick. The rooftop
  gantry pieces were removed (`GantryTarp` became `RackTarp`) and the
  billboard skirts deleted outright (`BillboardSkirt` is now a
  documented enum hole). Budgets: city cap `32 → 64`, supports `48 →
  96`, per-zone Old Town 12 / Residential 14 / Industrial 8 /
  Nightlife 10. Default seed now plans 56 pieces, all urban ones at
  1.9-6 m above ground.
- New EditMode contract for exactly this regression:
  `DefaultCity_HangsStreetLevelClothInEveryUrbanDistrict` pins a
  street-level (≤ 8 m above ground) floor per urban district
  (8/10/6/6); the determinism floor rose to ≥ 40 pieces. Verified with
  the wind-dressing EditMode fixtures plus a temporary diagnostic dump
  test (deleted after use).

## 2026-08-23 — Arrow keys move to the camera orbit

- The arrow keys no longer walk the hero; they orbit the chase camera like
  the gamepad right stick. `PlayerMotor.ReadMovement` drops its four
  `*ArrowKey` terms (WASD + left stick remain), and
  `PlayerCameraFollow.SampleOrbitInputDegrees` gains a keyboard branch on
  the stick's per-second scaling (`keyboardYawSpeed = 150`,
  `keyboardPitchSpeed = 120`, serialized next to the gamepad speeds; up
  looks up, matching stick-up). The sample stays additive across devices
  and keeps the existing `SmoothDamp` targets, so keyboard look needs no
  extra smoothing.
- The one real conflict was the seated park board game: its cursor owns
  the arrows and it consumes the same shared orbit sample. The sample
  gained an `includeKeyboard` parameter (default true) and
  `CityBoardGameController.ReadLookInput` opts out. Every other arrow
  consumer (map, pause menu, inventory, shops, balance check, debug
  window, grave work, refrigerator inspection) sits behind
  `BarMinigameModalLock`, which already disables orbit input
  unconditionally — verified by call-site sweep. The bus ride keeps
  keyboard look through the default.
- Tests: `PlayerMotorHeadingPlayModeTests.ArrowKeys_NoLongerMoveTheHero`
  (held arrows leave `PlanarVelocity` at zero) and
  `PlayerCameraPresentationPlayModeTests
  .ExteriorCamera_ArrowKeysOrbitAndRespectModalLock` (modal lock
  suppresses arrows; up arrow raises the view without touching yaw; right
  arrow orbits yaw without touching pitch). The arrow axis scales by
  unscaled delta time, so the test holds keys against realtime deadlines —
  a fixed frame count means nothing in batch mode's sub-millisecond
  frames. The run also surfaced
  `CameraCutDuringHeldInput_KeepsPreCutFrameUntilRelease` failing at
  ~1.6-2.0° against its 0.5° re-aim threshold — stash-verified to fail
  identically on the untouched baseline in batchmode, so it is recorded
  here and left alone. Docs: README controls, project-overview,
  architecture-notes, systems-map, system-tree, tutorial-scenario.

## 2026-08-23 — Wind dressing: cloth and rope misc across every zone

- Added the city-wide wind dressing: one cross-zone
  `CityWindDressing{Plan,Planner,Validator,WorldBuilder}` quartet plus the
  shared `CityRopeSpanGeometry` parabola (the chess-lamp sag extracted as a
  reusable curve + chord-chain helper; the lamp itself deliberately keeps its
  own copy for now). The planner is pure and seeded and hangs up to `32`
  simulated `ClothPanelFactory` panels off structures other plans already
  draw: market awning rags and a scaffolding shroud/rope end (Old Town), two
  courtyard drying lines on their own drawn poles and sagging rope with
  body-registered walk-through wash held `>= 25 m` off the drying-yard POI
  (Residential), gantry tarps and sling ends (Industrial), fire-escape
  banners and a billboard skirt (Nightlife), one bandstand pennant (Park —
  §10's emptiness), pier-rail net rags and slipway-chain mooring ends with
  the fisherman's pier head kept clear (Seacoast), wreath ribbons on
  enclosure posts preferring offering graves (Cemetery), and service tarps
  plus dead cable tails on the service fringe yards. The bar-side yard,
  lighthouse island, drained-lake block, tunnel forecourt, flood works and
  stone terraces hang nothing by authored rule (user-confirmed zeroes).
- Anchor geometry was rederived from the recipes, not guessed: the
  decoration builder's cardinal-snapped forward and lot-width clamps
  reproduce the valance/ledger/beam/rail positions the builders actually
  draw, and precinct parts (pier rail, slipway chain, enclosure posts,
  crossarms, sheds) are found by kind/size in their plans' public part
  lists. Rope-width strips (`columns=1`) needed no factory change — the
  panel factory already accepts a `1x2` grid and pins exactly the top row.
- Wired after `CityFountainWaterBuilder` in `CityWorldBuilder.Build` (cloth
  can't batch; the swing/fountain precedent), returned on `CityWorldResult`
  as `WindDressingPlan`/`WindDressingRoot`. The home-exterior vista is
  untouched — all pieces are street-scale details below its resolution.
- Verification: bundled-dotnet compile plus one filtered EditMode run —
  `CityRopeSpanGeometryTests` (curve + chord chain),
  `CityWindDressingPlannerTests` (determinism, zone budgets, body-registry
  restraint, drying-yard clearance, bar-side-yard emptiness, per-zone
  containment against the cemetery/seacoast plan grounds — the district
  descriptor list does not carry those precincts' dressed ground),
  `CityWindDressingWorldBuilderTests` (cloth count == plan, wind-registry
  delta == cloth count, body delta == planned wash, no colliders on cloth,
  pole batch carries the collider). `LastRouteCanopyRagTests` still scopes
  to the POI builder root and stays green; only its stale comment was
  refreshed. Full suites deliberately not run (fast mode).

## 2026-08-23 — The feeding scene finally reads: the tin meets the muzzle

- The composed cat-feeding scene had been silently broken since the
  player's 3D migration, in three independent ways no test could see:
  1. **The hero fed the cat backwards.** `feed_pose` in
     `tools/build-player-3d-model.py` used negative-X pelvis/spine/chest
     pitches; on this rig +X bows FORWARD (probe-measured: +25° spine
     moves the head 0.37 m forward, the shipped pose threw it 0.53 m
     back), so through the whole loop the hero arched ~40° away from
     the cat like a limbo dancer.
  2. **The tin was twelve metres long.** `RebuildFeedingCanProp`
     parented the factory can under `SOCKET_Grip.L` without cancelling
     the FBX bone hierarchy's 100x scale (grip `lossyScale` measured
     exactly (100,100,100)); the 0.12 m tin rendered ~12-14 m of pale
     slabs across the shot. The cigarette knew this all along
     (`InverseScale` in `HomeBalconySmokingInteraction`) — the tin now
     does the same at its prop root.
  3. **The cat chewed bare air.** Nothing ever staged food near the
     cat. Geometry rules out a rail-standing tin: `PIVOT_Head` sits
     0.38 m over the perch with `ANCHOR_Muzzle` only 0.112 m away, so
     a pure head pitch bottoms out ~0.27 m above the rail (verified:
     rest muzzle (0.44, 0.13 fwd) rotated 36° lands exactly at the
     measured (0.373, 0.147)). The fix is staging, not cat surgery.
- **New choreography** (`CatFeedEnter/Loop/Exit` re-authored; frame
  counts, fps, durations and clip names untouched): the hero carries
  the tin two-handed at the belly, raises it to the dipped muzzle
  (grip probe-solved via `armature_direction` on both arm bones to
  0.35 m forward / 1.32 m up at the dock — Unity landed within 2 cm
  of the Blender solve), HOLDS the offer for the cat's whole 16-step
  timeline with a breathing settle, and lowers it back through the
  carry. Both loop endpoints key the offer pose, so enter→loop→exit
  seams are exact and the untouched cat contract chews mid-loop with
  its muzzle ~0.14 m over the held rim, wobbling into it.
- **Probe-driven, not guessed**: eight Blender rounds measured the
  standing sign conventions and hand positions (naive two-link solves
  land ~0.1 m off on this rig); the BAKED actions were re-sampled and
  re-rendered separately from `_apply_pose`; a throwaway editor
  capture probe rendered the composed stairwell from the real
  MiddleFlight shot with numeric muzzle/grip/tin logs. Probe lesson
  recorded: after `clip.SampleAnimation` in edit mode the BONES and
  bone-parented props move but `Camera.Render()` still draws
  SkinnedMeshRenderers in bind pose — skinned poses verify only in
  PlayMode (the visual-capture contact sheet shows the offer).
- **New regression pins** in the paired PlayMode feeding test: held-tin
  renderer bounds < 0.30 m (kills any 100x return) and tin center
  within 0.40 m of `ANCHOR_Muzzle` during Looping (kills any future
  drift of the composition).
- **Verification**: Blender generator validators green (full rebuild);
  EditMode `StairwellCatRuntime` 16/16, `StairwellCatInteraction` 8/8,
  `InventoryTargetInteractionModel` 7/7, `Player3D` import contracts
  green; PlayMode `StairwellInteriorPresentation` 5/7 with the two
  stash-proven batchmode-environment reds unchanged
  (`…WithoutStewShowsMissingMessage`, `…DescendsLowerFlight…`) and
  `Scene_FeedingConsumesOneStewAndCompletesPairedAnimation` green
  including the new pins; `Player3DVisualCapture` contact sheet green.

## 2026-08-23 — The last sprite: the stairwell cat goes 3D, Cheshire inside

- **The conversion**: `StairwellCatActor` no longer builds a
  `SpriteRenderer` + `BillboardSprite` — it adopts and articulates a
  passive authored prefab. New one-off Blender generator
  `tools/build-stairwell-cat-3d-model.py` (Blender 5 headless,
  deterministic, ~908 tris, validators for perch footprint, grin
  width > head width and the grin UV contract) → FBX + manifest under
  `Assets/Stairwell/Cat/Models/`, editor `StairwellCatAssetSetup` +
  `StairwellCatModelImporter` build
  `Assets/Stairwell/Cat/Prefabs/StairwellCat.prefab` and bind
  `Resources/Stairwell/StairwellCatProvider.asset` (cashier pipeline
  shape, no avatar work). First character with **no armature**: pivot
  empties only (`PIVOT_Chest/Head/Ear.L/R/Tail.01..03`), exported flat
  with mesh origins on their pivots; the actor reparents at Initialize
  (wheelchair adopt) and writes pose deltas about the model's world
  axes over cached rest poses.
- **Kept without a byte of change**: `StairwellCatIdleModel` (its
  timings now drive chest-scale breathing, tail-pivot flicks, ear
  twitches and a head-down groom), `StairwellCatFeedingTimeline`'s
  16-step 6 fps contract (now a head-dip eating pose), and the entire
  `StairwellCatInteraction` — the actor's feeding API survived
  verbatim, so the quest, the descent blocker and the paired player
  clips never noticed.
- **The Cheshire grin**: `ACC_Grin`, a tooth crescent wider than the
  head on its own `StairwellCatGrin.shader` — arc-length u baked into
  UVs, fragment clip on `abs(u-0.5) > 0.5*_GrinProgress` with a
  feathered glowing frontier and shader-side tooth seams. Pure
  `StairwellCatGrinTimeline` (appear 0.4 s / vanish 1.2 s, vanish
  scales with start progress); `StairwellCatGrinController` is the
  deliberately schedule-free public API
  (`BeginGrin/EndGrin/SetGrinProgress`, `StairwellInteriorRoot.CatGrin`)
  for a future trickster script. The committed grin swings the head
  over the shoulder toward the live camera (cap 150°; ordinary
  tracking clamps at 65°). Hidden by default: renderer disabled at
  progress 0 and every fragment discarded on top.
- **Axis lesson (probe-caught)**: the live geometry faces the
  NEGATION of the inner model root's axes (FBX -Z under the prefab's
  inner half turn under the factory's instance half turn) — the first
  build tracked and grinned 180° backwards. Caught by a throwaway
  editor capture probe rendering the imported prefab from the
  MiddleFlight-relative pose with an `ANCHOR_Muzzle` direction log,
  not by tests (the fake-rig tests only asserted magnitudes); the fake
  test rig now mirrors the real double-flip chain exactly.
- **Deleted**: both cat atlases and their Resources folder, both
  atlas build tools, the sprite sources,
  `StairwellCat{,Feeding}SpriteLibrary`, `StairwellCatLook{,Selector}`
  and `BillboardSprite` itself (the cat was its last consumer) — the
  runtime draws no world-space gameplay sprites any more, closing the
  standing architecture-notes exception.
- **Verification**: generator validators green; prefab
  Build/Validate menu methods green headless; EditMode
  `-testFilter StairwellCat` 29/29 (grin timeline phases/asymmetry,
  yaw hysteresis/rate/clamp, pose rules per kind, adopt hierarchy,
  feeding API, default-hidden grin, prefab/manifest/provider contract,
  `ShaderHasError` pin); PlayMode `StairwellInteriorPresentation` 5/7
  — the two failures (`…WithoutStewShowsMissingMessage` at the IMGUI
  `HasRenderedLayout` assert, `…DescendsLowerFlight…` stall) were
  **stash-proven pre-existing**: identical failures on the pre-change
  baseline in batchmode. Visual captures of the imported prefab:
  idle perch, feeding dip, half-grin mid-turn and the full
  green-eyed over-shoulder grin all read correctly.

- **The mol beacon is gone entirely** — `AddBeacon`, the `BeaconTower`
  part kind and `Beacon` lamp kind (both left as deliberate enum
  holes), `BuildBeaconLens`, the whole
  `CitySeacoastBeaconController.cs` file, the map's beacon dot and the
  mol↔beacon validator clause. The mol keeps its deck, parapets and
  stair; the sea keeps `_AdditionalSpecular` for the pier hand lamp.
- **A new `CityLighthouseIsland*` family** (plan/planner/mesh
  factory/world builder/resources + `CityLighthouseLanternController`)
  plans an abandoned fishing island `31 m` past the waterline, north
  of the pier head's easting: a two-tier rock mound, two ruined shacks
  (one roofless), leaning poles, a fallen jetty, a heeled wreck, and a
  `~15 m` banded lighthouse — exactly one lamp room, lantern height
  ≥ 12 m over the sea, all parts held to the offshore band clear of
  every walkable deck (validator + tests). Null seacoast → null island.
- **Fog physics dictated the rendering**: at 35-45 m the city's Exp2
  0.070 leaves nothing, so the island is one baked vertex-coloured
  mesh (24 verts/box, faces pre-lit top/south) on
  `CityLighthouseIsland.shader` — no engine fog, a fixed `0.62` haze
  mix toward `CityFogColor`, and a `43-47 m` per-fragment self-fade
  that dissolves it before the 48 m far plane can clip (the mountain
  backdrop's trick with distance made explicit). Queue Transparent-90
  ZWrite On composes over the 2900 water. The lantern is additive
  geometry on `CityLighthouseBeam.shader` (no real Light — 16 m range
  cannot reach shore, halos are fog-eaten): a lens core (`_Uniform 1`)
  and two opposed beam cones, HDR lens ×4 over the 0.60 bloom
  threshold.
- **The rotation is pure data**: `CityLighthouseLanternRules` —
  azimuth = seed-phased `9°`/game-minute (40 s revolution, flash every
  20 s through two opposed beams, `±14°` smoothstep flash window) —
  applied absolutely each frame; the controller hand-honours the site
  registry contract (× night factor, off below 0.02) and surges the
  lens when a beam sweeps the camera bearing.
- **Tuned farther out on user review**: offshore anchor `31 → 35 m`
  past the waterline (the sheets' 18 m apron caps it — the validator
  margin is now under a metre), and the flat `0.62` haze became a
  distance-graded mix (`0.75` at ≤20 m → `0.92` at ≥38 m), so from
  the ordinary shore only the outlines survive while the pier head
  genuinely brings the island a step out of the fog.
- Verification: focused `CitySeacoastPlannerTests|CityLighthouseIslandTests`
  18/18 green, then the full EditMode suite; both new shaders pinned
  clean via `ShaderUtil.ShaderHasError` in a throwaway editor capture
  that also rendered the island from esplanade/waterline/pier-head/
  inland/close poses — silhouette scale, banding, fade band and sea
  compositing all read correctly (beam brightness and bloom are
  play-mode-only and expect one live tuning pass).

## 2026-08-22 — Atmosphere pass: DOF, drunk lens, PS1 film layers, Options

- **Depth of field in two tiers.** Every scene grade (city noir asset +
  runtime mirror, Bar/Home/Stairwell, a new minimal Supermarket volume)
  gained a subtle Gaussian far blur via the shared
  `RuntimeSceneSetup.AddGaussianDepthOfField` helper (city `8-28 m`,
  radius `1.5` — the band that still reads under the exp² fog and the
  `640x360` crush). Modal close-ups (bar counter, fridge + item
  inspection, grave work, park boards, bus seat, bar arrival, wake-up
  clock) share one `CinematicDepthOfField` Bokeh volume at priority 10
  with per-frame focus tracking and weight blending.
- **Intoxication now bends the lens.** `IntoxicationProfile` grew
  `ChromaticAberration` (`0→0.45`) and `LensDistortion` (`0→-0.14`)
  stage curves; `IntoxicationLensVolumeDriver` (priority-8 volume owned
  by the status controller's UI object) applies them each presentation
  update.
- **The PS1 composite grew three film layers**: Bayer 4x4 dithering
  before RGB555 quantization (half-step amplitude, internal-pixel
  locked), a step scanline mask on the point upscale (a cosine cancels
  at exactly 2x — the 720p case), and procedural rain-on-lens droplets
  + streaks as a UV offset before the 4-tap average, fed by the new
  `RainLensRenderState` from `CityWeatherController` (2.5 s ramp,
  bus shelter dries the lens).
- **The pause menu gained an Options page** (Resume/Options/Restart/
  Quit): six graphics toggles (DOF, drunk lens, dither, scanlines,
  rain, 4:3 aspect) drawn as retro checkboxes on the row's right edge
  (painted after the row button so the button background cannot cover
  them), ru/en localization, instant effect and persistence through
  the new `GraphicsEffectsSettings` static service over `PlayerPrefs`
  — the project's first settings persistence.
- **Opt-in 4:3 mode (default off)**: the composite reads the centered
  4:3 window of the widescreen frame (internal `480x360`, the exact
  view of a 4:3 camera at the same vertical FOV) and pillarboxes the
  upscale with pure black bars; the crop/pillarbox pair is an identity
  mapping over the visible region, verified by a pillarbox PlayMode
  test that keeps the exact flat-tone RGB555 contract in place.
- Verification: focused EditMode selection over the five touched
  classes (`GraphicsEffectsSettings`, `PauseMenuModel`,
  `Ps1Presentation`, `IntoxicationRules`, `LocalizationCatalog`) —
  49/49 green; focused PlayMode `Ps1CompositeRenderGraphPlayModeTests`
  (toggles forced off keep the exact-tone contract, a new test proves
  dither splits a flat tone and scanlines darken alternate rows);
  `NightPresentationAssetSetup.Run` regenerated
  `CityNoirVolumeProfile.asset` headlessly. Not run: the remaining
  PlayMode fixtures touched only additively (bar shop, home
  atmosphere, balcony, intoxication status, pause menu, city night).

## 2026-08-22 — The mouth opened (playtest fixes)

- First playtest of the coast surfaced two honest holes at the river
  mouth, both fixed. **The river now pours into the sea** instead of
  dying against the training sill: the sill is gone (the `MouthSill`
  part kind became `MouthBank`), replaced by a mouth spill — a sloped
  sheet of the river's own material carried from the exact height of
  its last sheet's north edge (`CitySeacoastFrame.MouthWaterY`, read
  from the mouth segment) down under the sea's datum, so the same
  world-driven waves make the joint invisible and the two swells
  interleave into churn where the sheets cross. **The channel's cut
  through the sand row is closed**: the granite quay walls end at the
  promenade and the terrain skin has no underside, so the player saw
  through the world along both sides of the mouth — stepped sand-faced
  banks (`Sand` style, the terrain's own `BeachSand` colour and sheet)
  now follow the shore height contract from the last quay wall to the
  waterline, feet buried below the river bed, faces lapping into the
  water so the river's foam draws against them.
- Verification: `DefaultCity_KeepsRiverAndSeaSheetsApartAtTheMouth`
  replaced by `DefaultCity_PoursTheRiverIntoTheSea` (spill continuity
  with the river sheet, dive under the sea, gap-free bank coverage on
  both sides); full EditMode suite green.

## 2026-08-22 — The north seacoast, and the lake that moved to it

- **The north shore stopped being a stub.** A new `CitySeacoast`
  precinct (planner/plan/world builder/appearance, the lake recipe
  transplanted 1:1) zones the waterfront around the river mouth: dead
  port with a concrete mol, occulting beacon and frozen derrick to the
  west; granite esplanade with glow lamps, benches and the boat
  station at the centre; rotten pile row, driftwood, dune grass, a
  lone wreck and a stranded barge on the wild east. A timber
  footbridge crosses the mouth as precinct geometry (the river's
  2-road+1-foot bridge contract is untouched), and a concrete
  training sill under the waterline keeps the river and sea sheets
  apart — its crest is test-pinned above both waters' highest crests.
- **The sea is real water now.** `CitySeaResources` is the third
  material of the shared water shader (zero flow, long low swell,
  additional-specular for the beacon's glitter road) on chunked
  sheets over a jittered silt shelf whose depth sits inside the foam
  distance, so the surf line draws itself; the flat municipal slab is
  suppressed when the coast plan exists. The beacon pulses a pure
  deterministic occulting characteristic driven off game minutes.
- **People reach the shore.** A coast lane joins the pedestrian
  2-core graph (street spur through the rail opening, quay junctions
  that un-prune both `river:{bank}:north` stubs down new quay stairs,
  the footbridge link, an east terminal ring); the quay seal rails are
  skipped when the coast exists; esplanade benches join the shared
  bench-sit pass; a synthesized nine-second surf bed follows the
  hero's distance to the waterline and the deterministic wind.
- **The fisherman moved with his station** — nine runtime files
  renamed `Lake*` → `Seacoast*` with meta GUIDs preserved, stance
  reading the coast pier's named boards, line ending at the sea's own
  top, quips re-keyed `seacoast.fisherman.*` (the crucian became
  flounder). The 3D asset chain keeps its lake-era names on purpose.
- **The lake itself is gone**: blueprint block replaced by a plain
  `yard-north-east`, six world files, its tests, five texture sheets
  and the generator deleted, three enum values left as holes so
  nothing renumbers, the elevated-basin elevation contracts removed,
  and every switch, map draw, colour and localization entry swept.
- **Verification:** staged over seven commits, each compiled headless
  and ran green; the full EditMode suite finishes 1368/1368 (the 17
  lake tests retired, 28 seacoast/pedestrian/appearance/fisherman
  tests added). Textures regenerated via
  `tools/build-city-seacoast-textures.py` with solved compensations
  transcribed and pinned.

## 2026-08-22 — The cave gap and the embankment's invisible wall

- **The user walked the new south embankment and found both bugs in
  the cave zone.** A fact probe (throwaway EditMode test printing the
  notch descriptor, south ridge stations and every walkable rect near
  the cave) confirmed both mechanisms before any fix.
- **The invisible wall:** the 4 m sloped forefield shoulders flanking
  the banks (x∈[0,4] and x∈[22,26] over z∈[-182,-156]) are real
  collidered ground, and the yards' land beyond them (x&lt;0, x&gt;26 down
  to z=-182) is already in the walkable mask — but the shoulders
  themselves were not, so the whole promenade extension was sealed
  along its outer edge. Probe sample x=3: False at every z south of
  -156. The mask now carries both shoulders plus seam strips on all
  four of their edges (promenade side, yard side, and the -156 line),
  same abutting-rect idiom as the existing cave seams.
- **The gap in the mountain:** two layers. Geometrically, the mouth
  facade used `min(WestPeakY, EastPeakY)` for the crown while each
  side facade rose to its own peak — with peaks 27.0 vs 22.7 that left
  a 4.3 m rectangle of open sky over the crown, and the notch splits
  the ridge line so there is NO rock behind it. The whole facade now
  tops out at the higher peak. Materially, the facade wore the
  ordinary opaque rock material while the physical ridges beside it
  wear the fog-safe dither — at night the ridge dissolves into the fog
  and the opaque facade does not, splitting the silhouette at the seam
  into a bright hole. The facade now wears `ApplyPhysicalRidge` like
  the ridges; the portal arch deliberately keeps the ordinary material
  (documented as a close-range piece).
- **Verification:** before/after captures from four promenade poses
  (probe deleted after use), the fact probe re-run showing the shoulder
  samples flip to True, and the full EditMode suite.
- **Round two, after the user played it:** the facade material change
  was WRONG and is reverted — the fog-handoff dither shows whatever is
  behind, and behind the facade is the cave void and portal, not the
  backdrop ring, so at night the plate dissolved into a glowing hole
  with a floating arch. The real remaining mechanism was the handoff
  band itself: far clip is 48 m, the near backdrop ring rides at
  39.4 m, and the physical rock dissolved across 31-43 m — but at fog
  density 0.07 the rock's 10%-dot silhouette is still faintly readable
  at 31 m, so a player standing AT the rim (only possible on the cave
  promenade) watched the crest dissolve mid-air. The band now hugs the
  clip (39-47.5 m, still straddling the ring as the swap contract and
  its tests require); by 39 m the dots are haze-coloured and the same
  handoff is invisible. The reported texture flicker: the crown facade
  overlaps the flanks by 0.12 m with coplanar front faces - z-fighting
  strips right beside the arch - so the crown is recessed 3 cm.
- **Round four - the sealed tunnel had the identical hole, and the
  mouth got its lamp.** The head-on probe render showed the road
  tunnel's ridge gap wide open: wedges of sky between the two tapering
  ridge ends above the arch, plus the same spandrel corners. The
  facade+spandrel construction is now a shared `AddPortalBackstop`
  (flanks, crown, arc-hugging stepped spandrels with radius-generic
  inset math and an inner-arc clamp; depth staggers 0/0.03/0.06
  against coplanar z-fights) used by both portals - the tunnel's
  backstop tops at the taller adjoining south station and sits 0.45 m
  behind the mouth-plane furniture. The river mouth also gained its
  requested lamp: an iron-hooded, kerosene-warm lens on the arch crown
  (120 night / 26 day floor / 15 m range, between the door bulbs and
  the yard floodlight), wired like the lake hut bulb - glow registry,
  point light, halo, site-light day dimming. Verified by after-fix
  renders of both mouths; probes deleted.
- **Round three - the user named the actual geometry.** The mouth is a
  RECTANGULAR facade opening with a SEMICIRCULAR portal ring inside
  it, and the two upper corners of the rectangle - the spandrels
  outside the ring's outer arc - were simply open: a sightline through
  them runs down the 56 m throat past the 48 m far clip and renders as
  fog. Two stepped blocks per side now hug the outer arc (stepped, no
  diagonals, per the project idiom), each inner-bottom corner kept
  outside the ring's inner radius so the arch opening stays whole, and
  recessed 6 cm so their overlap strips with the flanks (0) and crown
  (3 cm) never share a front plane. Verified by head-on and oblique
  captures (probe deleted): both corners closed, the arch intact.

## 2026-08-22 — The audit's three deferred design calls, decided

- **The yard bin got its own scheme instead of a corner slot** (the
  user's call: вариант «б»). Four corner slots host four objects; the
  fifth — the bin — now presses flush against an end wall between them
  via `TryResolveYardWallSlot`: two ends tried in hash order, three
  lateral seats per end, and every candidate must clear the circuit,
  the access mouth, booth/dumpster reservations AND the actual
  footprints of the already-placed corner objects (collected as
  world-axis rects mirroring the descriptor literals). A yard with no
  clear wall seat simply gets no bin — better absent than standing
  inside the sandpit.
- **Dead-end sidewalks extend past the cap again.** The degree-1
  branch of `ResolveEndpointInset` returns `-halfRoad` by design, but
  both callers clamped the ratio to [0,1] and `Vector3.Lerp` clamps
  too, so the extension never happened. Negative insets now extrapolate
  (`LerpUnclamped`; positive insets keep the old clamped semantics for
  short edges) while the elevation sample stays clamped, so the wrap
  runs flat at the cap's own datum.
- **Board marks stack by draw layer.** Every plate sat at exactly
  `PlateHoverMeters + PlateThickness/2`, so a hover over a destination
  square (or a check square that is also the last move) was two
  coplanar boxes z-fighting. Each Redraw layer (last-move, check,
  destinations, selection, hover) now adds `PlateLayerStep = 0.6 mm` —
  the read order settles, and 2.4 mm of total spread is invisible at
  table distance.
- **Verification:** full EditMode suite re-run after the three changes
  (the sidewalk extension touches walkable/pedestrian-graph ground, so
  the whole suite, not just the street classes).

## 2026-08-22 — Full-project audit: nine reviewers, one soft-lock, a leak chorus

- **A parallel review swept the whole runtime, tests and editor tooling**
  (nine areas, findings verified against the code before any fix). The one
  high-severity gameplay bug: restoring a city at the `Coffined` grave
  stage never raised the lying monument, so after fill the stone act
  borrowed a null stone and soft-locked until the next city rebuild —
  `Restore()` now delivers the stone at `Coffined` like every other stage.
  A cousin guard (`actConcluded`) keeps a committed act from being
  "restored as abandoned" when a scene transition or disable lands during
  the leave blend.
- **Input and state bugs:** the pause menu's resting-mouse hover overrode
  keyboard navigation every IMGUI pass (now gated on `MouseMove`, like the
  inventory; same fix in the opening menu); the park board game kept
  playing — clicks, restart, unscaled-time speech — behind an open pause
  menu (now holds its breath on `IsAnyPaused`); a draughts click that
  could mean two capture chains ending on one square silently played the
  first-enumerated one (`BoardGameAction` now carries `CaptureCount` and
  the longer chain wins, as the comment always promised); an aborted home
  opening left the whole session frozen at day 0 05:59 with no recovery
  (`RestoreSessionClockDisplay` was a self-guarded no-op; it now starts
  game time idempotently and re-follows it).
- **Leaks:** `HomeSmokingMusicPlayer`'s detached scene-exit carrier
  outlived its fade forever (now destroys itself like `SceneMusicPlayer`);
  `HomeSoundscape.OnDestroy` skipped the shower clip/source — the PlayMode
  suite had asserted 5 sources against a constant of 6 and could never
  have passed; `CityNightResources`/`CityWindowAppearance` nulled created
  materials without destroying them in their domain-reload-disabled reset.
- **Per-frame costs:** `PlayerInteractor`/`PlayerAttention` allocated a
  `MonoBehaviour[]` per overlapped collider per frame (list overload now);
  the bed deformer's shading pass allocated two full vertex arrays per
  surface per settling frame (reuses the write buffer + a `GetNormals`
  scratch list) and the depression model now early-outs when settled at
  rest; `RuntimePrimitiveFactory.SetColor` made a `MaterialPropertyBlock`
  per call under the cemetery mark pulse; the bus obstacle probe was
  O(walkers x probe steps x link samples) from a linear sample scan (now
  bisected) and rebuilt two ride plans twice per frame during dwell (now
  pose-keyed); city-map/bar/bus-stop labels, tooltip `GUIContent`, prompt
  and speech-bubble measurement, the intoxication HUD line and the shelf
  shop's per-event delegates are all cached now; `CityTravelDistance`
  rebuilt the weighted road graph per query inside validation's bar-pair
  loops (now cached by list identity).
- **Motion and geometry:** the yard wheelchair's lap wrap snapped every
  non-lap-periodic channel once per circuit (distance now unbounded, wheel
  scrub integrated in closed form; the lap test asserts seam continuity);
  captured board-game men always swept toward the hero regardless of who
  took them; the hanging bar sign mirrored its tankard into the wall on
  -X/-Z frontages; the deformable bed's AABB only grew downward while the
  rim welt rises above the rest top (grows both ways now, test updated);
  the swing's push trigger dipped 3 cm into the lawn and fired
  `OnTriggerStay` against the terrain every physics step forever (lifted,
  plus a last-miss collider cache); a timed-out bus alighting resumed the
  walker mid-carriageway (now stood on the wait slot first, like aborts);
  bench rest claims released while the sitter still stood on the
  seat-front slot (a `Standing` phase holds the claim through the return)
  and rest attempts ticked on wall-clock time through pauses.
- **Editor pipeline:** the full pedestrian rebuild bound six providers but
  never the lake fisherman's; the chess-set auto-build ran unwrapped in
  `delayCall` (now queued+caught like every sibling) and collapsed
  duplicate mesh names silently (now throws); PS1 presentation auto-setup
  ran in batch mode (guarded now) and stamped the feature map before the
  new sub-asset was saved (localId 0; save now precedes the stamp); the
  bus prefab save gained its missing `out bool` check; the
  cashier/bartender "Validate Imported Contract" logs no longer claim a
  model diff that does not run.
- **Deliberately not fixed** (need a design decision): the home-yard bin
  resolves the same corner slot as the sandpit (five edge objects, four
  corner slots — someone must lose an object or gain a wall scheme); the
  dead-end sidewalk extension is neutralised by a `Clamp01` (restoring it
  changes world geometry); coplanar board-marker plates can z-fight;
  `CityElevationPlanner`'s stair rise can leave the validator band on a
  dead code path. All recorded here rather than guessed at.
- **Verification:** all four csproj compile clean (0 errors) after every
  batch; full headless EditMode suite run on 6000.5.9f1 (first full run
  since the upgrade) — results in the entry's own session notes; the
  updated tests are `HomeBedDeformableSurfaceTests` (bounds allowance up),
  `YardWheelchairMotionTests` (lap continuity), and
  `InteriorSoundscapePlayModeTests` (six sources, nine clips, shower clip
  in the destroy sweep).

## 2026-08-22 — The dent existed and showed nothing

- **The user looked at the opening and saw a flat bed.** He was right, and
  the reason was mine twice over. First, every PlayMode assertion read the
  MODEL through `GetSurfaceHeight` — the circular-test trap the earlier
  adversarial review had warned about in general terms; the tests now read
  the actual mesh vertices as well. Second, once a headless capture probe
  rendered before/after PNGs from the real camera poses, the pixel diff
  proved the dent WAS in the frame (1% of pixels, exactly under the torso)
  and simultaneously invisible: a 4.5 cm bowl tilts smooth-shaded facets
  under ten degrees, which is a few percent of brightness on albedos whose
  own noise is far louder.
- **Three presentation fixes, each verified by re-capture.** The top faces
  became independent per-cell quads (faceted shading — the grazing-angle
  capture shows the surface line dipping beautifully, so the geometry was
  never in doubt); dented facet normals get their lateral component
  steepened ~3x — the PS1-legitimate cheat that keeps geometry honest and
  makes light answer it; and the exit-phase body weight no longer ramps to
  zero by the bedside seat — it stays at one, sources vanish naturally as
  parts rise, and the slow spring refills the hollow visibly behind the
  rising body, which is the only moment the dent is not hidden under the
  body that made it. The mattress skirt settled at 0.18 m after 0.12
  (self-occluded) and 0.28 (too soft) both failed on screen.
- **A third real bug fell out of the dense probe run**: `BeginLooping`
  raises `PhaseChanged` before the bed sets its ownership flag, so the
  equilibrium snap's ownership check always saw false — and in batch mode
  frames are 1–3 ms, too short for the spring to take even one 1/120 step,
  which is what exposed it. The snap now ignores ownership at event time;
  LateUpdate re-resolves the weight, and snapping a foreign loop lands on
  rest.
- **Verification:** capture probe (since deleted, per the throwaway-script
  rule) rendered rest/dented pairs at the sleeper close-up, the room shot,
  a grazing angle and a raking light — the final pair reads under the
  worst light; EditMode bed selection 15/15; PlayMode
  `HomeBedInteractionPlayModeTests` 7/7 including the new mesh-vertex
  assertions; the known unrelated reds unchanged.
- **Round two, after the user still saw nothing:** the edit-mode probe
  had bypassed the real pipeline (occluder-dither material, bulb light,
  real camera), so a play-mode screen probe captured what the player
  actually sees — and the dent was there but whisper-quiet at gameplay
  distance. Three louder levers, each re-captured through the real
  pipeline: a rim welt (displaced bedding bulges UP around the body,
  `RimBulgeRatio`; the lit welt against the shaded hollow is what carries
  the read), the sink deepened 0.045 → 0.065 so the sunken body reads
  behind the untouched mattress rim, and cells coarsened 0.10 → 0.14 m so
  the relief lands as hard facet light-steps instead of gradients. The
  final real-pipeline capture shows the hero clearly seated IN the bed
  with a lit welt and a deep boot pocket. Both probes deleted after use.
  A further "нужно больше" pass cranked the numbers to their final
  values — sink 0.10 (half the mattress), welt ratio 0.65, pillow sink
  0.045 — and the real-pipeline re-capture reads unmistakably even from
  the wide gameplay shot.

## 2026-08-22 — The mattress finally gives under him

- **The user's ask:** the bed should behave "like cloth, but thick" — it
  should dent. Of the three techniques discussed, procedural grid
  deformation won: the dent follows the real pose rather than one authored
  blendshape state, stays deterministic, and is assertable to the vertex.
  User picked ~4.5 cm of sink and a ~1.5 s slow refill after he rises.
- **The load-bearing coupling, decided before any mesh existed.** Runtime
  pins the sleep clip by the pelvis to `SleepingHipHeight` and grounds
  nothing while a clip owns the rig — dent the mattress without lowering
  that target and the hero hovers over his own dent. So `SleepingHipHeight`
  descends by `BedSleeperSinkDepth`, the pillow's rest top is derived one
  pillow-dent above the sunken head plane, and the bedside seat is
  deliberately untouched: it is pinned by both boots on the floor, so a
  dent there could only open a gap, not close one.
- **Depth comes from penetration, not weights.** An adversarial design
  review caught that weighted dent depths contradict a rigidly lowered
  pose (0.35× under the feet would leave heels 2.9 cm inside the
  mattress). Each source's target depth is its part's actual penetration
  under the rest plane, read from live renderer bounds and clamped to the
  sink — the surface meets each part's underside exactly: full 4.5 cm
  under the torso (whose jacket defined the support offset), ~2 cm under
  the boots, nothing under parts that float.
- **The build.** `HomeBedDeformableSurfaceFactory` replaces the two
  primitive boxes (which shared Unity's built-in cube mesh — writing to it
  would have corrupted every primitive in the game) with closed boxes
  whose tops are ~0.1 m grids; the border ring is pinned so the
  single-quad sides stay welded and the rest silhouette, names, BedLinen
  texturing (0..1 UVs under the ST transform), occlusion membership and
  `bounds.max.y` are exactly what the boxes had; bounds grow downward only.
  `HomeBedSurfaceDepressionModel` is a pure fixed-step spring in the
  cemetery-model idiom (τ 0.10 s down, 0.50 s up, smoothstep skirt, edge
  band, bit-deterministic). `HomeBedSurfaceDeformer` (order 400, after
  every pose writer) resolves body weight from public `Phase` +
  `FrameIndex` — zero across both seat windows, ramping over the lie-down
  and the rise — so **no shared interaction code changed**; the one bed
  API addition is `HomeBedInteraction.OwnsActiveInteraction`, without
  which balcony smoking would dent the bed through the shared controller.
- **Two real bugs found by the runs, not by review.** In batch mode frames
  are 1–3 ms, so the two frames between `BeginSleeping` and the first
  assert never accumulate one 1/120 integrator step — which exposed that
  the equilibrium snap never fired: `PhaseChanged(Looping)` is raised
  inside `BeginLooping`, before the bed sets its ownership flag, so the
  snap's ownership check always saw false. The snap now ignores ownership
  at event time (LateUpdate re-resolves weight; snapping a foreign loop
  lands on rest, a no-op). And bilinear sampling leaked uncapped depth
  across the pillow-shadow border from a neighbouring vertex, so the cap
  rect pads itself by one cell.
- **Verification:** `dotnet build` ×3 clean; EditMode **1348 total /
  1347 passed** — 8 new `HomeBedDeformableSurfaceTests` green, the one red
  is `CityMountainBoundaryTests.LegacyAndCustomBlueprints_StayOptOut`
  from the mountain-fringe work (file sets fully disjoint from this
  change); PlayMode bed suite **7/7** including the two new tests (dent
  equals sink under the torso from the first frame of a programmatic
  sleep; full refill within 2.5 s after wake AND after a cancelled
  sleep); home presentation, opening and smoking suites show only the
  three known pre-existing reds (lamp framing, AudioSource count,
  smoking calm-frame — all baseline-proven earlier).
- **Not run, and not runnable here:** how the dent reads on screen. That
  wants the room camera during an ordinary sleep and the opening's
  sleeper close-up.

## 2026-08-21 — The southern river now disappears inside the mountain

- Replaced the default coastal river-axis void with one validated cave
  contract while retaining the historical `CityMountainRiverNotchDescriptor`
  type name. Its cave aliases now expose the visible approach, `10 m`-wide water
  mouth, two bank and promenade approaches, physical rock stop and a `56 m`
  hidden water throat that extends beyond the unchanged `48 m` far plane.
- Extended the river water and silt bed continuously from the city into that
  throat. Both collidered `3 m` promenades, their quay walls and longitudinal
  rails now continue from world `Z=-156` to `Z=-182`; the former transverse
  south-end rails are gone, and `RoadWalkableArea` includes only those two bank
  routes before collidered rock. The water and everything behind the stop stay
  non-traversable, with no prompt, interaction, destination or transition.
- Closed the physical and distant southern mountain silhouettes above a low,
  dark mouth, then added the rock shoulders, dark lining and enough hidden
  water/bed depth that no river end is visible. The City map consumes the same
  descriptor but bounds and draws only the visible bank/water approach, closed
  ridge and dark mouth; the hidden throat does not expand the map.
- **Verification:** the focused Unity EditMode selection
  `CityMountainBoundaryTests.SouthRiverCave_ExtendsBeyondVisibilityAndKeepsBothBanksWalkableToRock`
  was requested, but Unity terminated during startup after `30 s` when Package
  Manager could not connect to IPC stream `Upm-29284`; no test-results phase
  ran, so there is no pass/fail result. The fallback
  `dotnet build BarPromenade.EditModeTests.csproj --no-restore` then compiled
  the runtime and focused regression with `0` errors; the reported `136`
  warnings are unrelated serialized-field warnings in existing providers and
  manifest DTOs. `git diff --check` passed.
  Full suites, a player build and manual visual smoke were intentionally
  omitted in fast mode.

## 2026-08-21 — The tunnel meets the ground and lights its throat

- Traced the visible entrance gap to the throat floor starting `0.45 m` behind
  the portal while the Yard terrain ends exactly at the portal plane. The floor
  is now an independent lining piece that reaches `0.25 m` cityward beneath the
  terrain, ends at the original sealed depth and rises `0.03 m` through the
  ground joint, so the entrance has continuous visible ground coverage.
- Removed the exact coplanar portal/throat overlap that caused z-fighting. The
  throat walls now start outside the portal's inner planes, overlap the floor
  vertically and meet an overlapping, outward-overhanging ceiling instead of
  leaving the former `0.275 m` upper slits or another coplanar outer seam.
- Reused the existing `SouthTunnelForecourt` pooled Spot rather than adding a
  thirteenth Light. Its housing now sits above the portal crown, aims down and
  inward at the gate/floor, and uses the stronger tunnel profile
  `150` intensity / `16 m` range / `72°` outer / `40°` inner cone. The night
  pool remains `4` bar + `7` street + `1` tunnel practical = `12` Lights; the
  shallower housing leaves its emissive lens visibly proud of the front face.
- **Verification:** four focused Unity EditMode regressions passed (`4/4`):
  `WorldBuilders_CreatePhysicalClosureAndPresentationOnlyRim`,
  `DefaultCoastal_PlansFiveDeterministicFringesAndSealedForecourt`,
  `DefaultCoastal_CellFiveMinusOneUsesNarrowTunnelTraces`, and
  `FringePractical_LeasesOneStreetSlotWithinTwelveLightBudget`. They cover the
  closed lining joints, portal-light pose and direction, strengthened light
  profile, and unchanged 12-Light budget. A final geometry review then exposed
  a buried lens and one remaining outer wall/ceiling coplanar face; after those
  corrections the two affected regressions passed again (`2/2`).
  `git diff --check` also passed. Full suites, a player build and manual visual
  smoke were intentionally omitted in fast mode.

## 2026-08-21 — The ridge turn and both authored road corners are continuous

- Reproduced the report at player position `(-154.85,-159.40)`. The canonical
  blueprint intentionally omits cell `(-1,-1)`, leaving a `322 m²` concave
  pentagon between the west Yard, south Yard, road-node box and diagonal
  mountain toe with neither render triangles nor collision.
- Added a default-only mountain corner-closure descriptor. Its west and south
  chains reuse the continuous-terrain axis samples and heights, its inner point
  meets the actual south-west road-node corner, and its outer edge follows all
  three diagonal toe stations. One world-UV forefield mesh supplies both the
  renderer and the MeshCollider, without adding a full square surface behind
  the mountain.
- Follow-up traversal exposed that the first closure was absent from
  `RoadWalkableArea`. A corrective stair-and-terrace pass was visually and
  spatially wrong for an ordinary soil corner and was removed completely. The
  final closure is one 20-vertex, 18-triangle natural-ground mesh: its direct
  centre is a `16.2°` slope with no stairs, benches, retaining faces or props.
  It contributes no footprint to `RoadWalkableArea`; the soil only fills the
  visual and physical void behind the road edge.
- Kept the ordinary map-boundary fence ownership. Its horizontal and vertical
  legs both reach the exact road corner and meet there as one physical L-shaped
  barrier, so the player cannot leave the street into the filled pocket.
- The first visual check exposed a height-sampling defect hidden by that XZ
  contract: the square road-node corner lies outside the elevation sampler's
  round edge corridor, so both shared endpoints fell to world `Y=0` and the
  rails plunged underground at `68.4°`. Fence planning now falls back to the
  owning Street node datum for points inside its square cap. Both `4 m` legs
  consequently remain horizontal at the actual road height and visibly meet.
- Added a separate default-coastal `CornerGuard` pair at the north-east
  urban-core road cap beside lot `[12,11]`. Its two level, perpendicular
  `4 m` physical legs meet at the real node datum as an ordinary L. No ground, ridge,
  navigation footprint or extended waterfront fence was added, so the lake
  and northern-shore approaches remain open.
- Traced the upper slit to the diagonal ridge using one fixed south-west normal:
  only its toe anchor matched the neighbouring west/south ridges while shoulder,
  crest and back diverged by metres. The join now interpolates the complete
  outward profile and validation requires both endpoint cross-sections to weld.
- **Verification:** the focused Unity EditMode regression
  `SouthWestCornerClosure_WeldsSampledGroundAndCollision` passed (`1/1`)
  after the natural-ground replacement. It builds the closure, verifies the
  shared render/collision topology, confirms the former route remains outside
  the radius-`0.35 m` walkable mask, checks both collidered and rendered fence
  legs meet at the same corner on the road-node datum, and confirms navigation
  stops cityward of the rock toe. The focused Unity EditMode regression
  `DefaultCoastal_CellTwelveElevenBuildsVisibleCornerGuard` also passed (`1/1`),
  proving both north-east legs, their exact height and their visible physical
  rail meshes.
  `git diff --check` passed with only
  the existing texture-file line-ending notices. Full suites, a player build
  and a manual City visual smoke were intentionally omitted in fast mode.

## 2026-08-21 — South-west slabs, loose portal blocks and the rock void are gone

- Reproduced the report in cell `[5,-1]`, inside
  `yard-south-west`. Its `6.9 m` tunnel-approach ribbon, `4.6 m` longitudinal
  service-track boxes and inherited repair pad were collider-free surfaces
  sampled only on their centre lines; the cross-slope left visible corners up
  to `0.16 m` in the air.
- Kept the logical `6.9 m` forecourt and `>6 m` capsule-clear terrain route,
  but replaced its broad visual box with a `0.36 m` embedded trace and retained
  paired `0.36 m` wheel ruts. Western service tracks remain paired `0.76 m`
  terrain-following traces; south-west tracks and spurs use the `0.36 m` cap,
  and generic repair stock now belongs only to the industrial profile without
  a ground pad. Pure-plan validation checks every affected lower corner with
  no more than `0.02 m` positive gap. The paired `8 m` traces and compact portal
  assembly keep a strict re-derived `650`-descriptor ceiling.
- Traced the remaining portal-side float to two concrete `PipeStock` bars, one
  deliberately offset `0.36 m` above ground, and to six isolated cheek boxes.
  Replaced the whole loose read with two continuous three-stage board-formed
  concrete returns, supported iron wear caps and a two-post side frame for the
  cityward lamp. Each structural base is placed below the lowest of its four
  terrain samples, while the freight anchor and drain covers are narrow,
  terrain-seated marks. The exact drive-clear bounds remain untouched.
- Traced the universal rock gap to the mountain cross-section starting at its
  old foot `0.35 m` beyond the terrain toe. Added an exact toe anchor and a
  rock seam buried `0.04 m` and overlapped `0.08 m` cityward; both the visible
  ridge and its near-toe collider cover the new bands. Generalized ridge end
  caps while preserving the declared tunnel and river openings.
- **Verification:** the exact Unity EditMode regressions
  `DefaultCoastal_CellFiveMinusOneUsesNarrowTunnelTraces` and
  `PhysicalRidges_BuryRockSeamAcrossEveryOwnedTerrainToe` each passed `1/1`;
  the first was rerun after the portal rebuild with its new terrain-seat,
  support, depth-span and drive-clear assertions and again passed `1/1`.
  `git diff --check` passed with only the existing texture-file line-ending
  notices. Full suites, a player build and a manual City visual smoke were
  intentionally not run in fast mode.

## 2026-08-21 — Forefield overlays no longer float or pass through

- Reproduced the report in cell `[7,-1]`, the first access cell of
  `yard-south-east`. Its new floodworks composition combined a broad
  center-grounded, collider-free anchor slab with wide shoulder sections; the
  low gabion was physical but sampled only once across the terrain slope.
- Replaced every broad forefield anchor with a narrow graded stroke directed
  down the road-to-rock slope. Reduced the shoulder to a `0.76 m` trace, split
  it exactly at source terrain-cell boundaries, lowered all new surface marks
  into the ground skin, and divided each floodworks return into three separately
  grounded solid gabions. Raised terrace shelves and all rockfall masses now
  use the existing collidered world batches.
- Extended pure-plan validation over maximum trace widths, slope direction,
  terrain contact, low-return depth and physical-mass collision. Added one
  regression that inspects the bottom corners of the exact `[7,-1]` parts and
  the collision policy across all fringe Yards.
- A follow-up screenshot exposed an older, separate culprit at the toe: the
  south-east `service-track-*` run was a `4.6 m` wide earth-textured visual box
  strip. Removed that whole longitudinal run from `SouthFloodWorks`; the
  conforming forefield terrain remains underneath, and the toe drain/gabions
  retain the floodworks read. Validation and the exact regression now require
  that the south-east Yard cannot reacquire a `ServiceTrack` descriptor.
- Narrowed the remaining south-east access overlay from a rigid `5.4 m` slab
  to a `0.76 m` embedded trace without narrowing its capsule-clear terrain
  route. Flood service traces now terminate at the actual toe drain; `2 m`
  terrain-following segments and `0.10 m` downward thickness keep their bottom
  edges seated below the sloped ground skin.
- A second follow-up position `(77.50, -165.37)` exposed two older broad
  collider-free boxes: the `5 x 6.8 m` dark silt fan and the generic
  `5.4 x 4.2 m` repair pad. Removed both from `SouthFloodWorks`, narrowed its
  service/anchor/overflow marks to at most `0.8 m`, and made the validator plus
  focused regression reject either platform kind or any newly broad trace.
- **Verification:** the focused Unity EditMode
  `DefaultCoastal_CellSevenMinusOneSeatsForefieldAndBlocksMasses` regression
  passed `1/1`; `git diff --check` passed with only the existing line-ending
  notices for the texture manifest/generator. Full suites, a player build and
  a manual City visual smoke were intentionally not run in fast mode.

## 2026-08-21 — The road-to-mountain forefield is no longer empty

- Traced the flat read to the authoritative terrain build: every `OpenGround`
  Yard was emitted with `applyGroundAppearance=false`, while all measured
  fringe sheets and most geometry lived only in the last metres at the rock
  toe. Split the four mountain-facing area IDs into their own conforming
  `Mountain Forefield Ground` mesh and applied a new quiet compacted-fill
  albedo with baked `8 m` world UVs. The eastern/custom batch remains generic,
  and every source area still owns exactly one terrain mesh collider.
- Authored the complete roughly `22 m` depth as three readable chapters: a
  continuous collider-free road shoulder in `0-4 m`, a `4-14 m` working band
  crossed by three secondary service traces, and the established track,
  drainage and retaining belt in `14-22 m`. Each mountain strip now carries
  three or four deterministic meso compositions with no empty longitudinal
  gap over `40 m`: drainage shelves, repair pipes/cradles, freight staging, or
  silt-wash/gabion returns. Raised stock remains physical; surface marks do not.
- Extended the validator over depth bands, anchor and pole spacing, all
  step-safe road seams, the four capsule routes, river notch, tunnel lane and a
  bounded `640`-descriptor ceiling. Runtime detail remains in shared
  material/style batches and adds no `Light`.
- **Verification:** `python tools/build-city-fringe-textures.py --verify`
  passed all four measured sheets. The focused Unity EditMode `CityFringeYard`
  category passed `5/5`, covering deterministic coverage/budget, every safe
  seam and rock route, exact terrain ownership/texture/UV/collider split, and
  the retained 12-Light lease. Full suites, a player build and a manual City
  visual smoke were intentionally not run in fast mode.

## 2026-08-21 — The outer ring now opens into the mountain fringe

- Traced the blocked edge to two independent contracts. All Yard ground was
  walkable, but the road/ground union exposed only one `8.8 m` connector per
  long strip, leaving visually open, height-safe seams as invisible motor
  clamps. Three non-tunnel traversal reservations also stopped at the service
  track, so their collidered retaining runs cut off the final `5-6 m` to rock.
- Scoped the navigation exception to the four mountain-facing Yards of
  `default-coastal`: every already step-safe Street frontage now becomes a
  connector, while real drops still produce terrain rails. The eastern Yard,
  beach, lake, cemetery and custom/legacy blueprints keep authored gates.
- Extended three visible gravel spurs to one player radius from the ridge toe
  and split only the intersected retaining module around each `6 m` corridor.
  The south-west drive-clear route and sealed portal remain unchanged.
- **Verification:** the exact Unity EditMode regression
  `DefaultCoastal_OpensRingFrontagesAndKeepsRockRoutesClear` passed `1/1`. It
  samples a `0.32 m` player capsule across newly open road seams and along all
  four rock/portal routes against both the walkable mask and blocking fringe
  footprints. Full suites, a player build and a manual City smoke were
  intentionally not run in fast mode.

## 2026-08-21 — The western and southern fringe gained authored anchors

- Expanded the existing five-Yard service-belt plan without changing access,
  terrain or navigation ownership. The four mountain-facing strips now own one
  deterministic macro anchor apiece: stepped masonry and a culvert, an
  industrial repair frame with winch and pipe stock, the sealed-tunnel return
  light/forecourt, and caged floodworks with a gauge and silt fan. The eastern
  utility edge stays low and deliberately dark.
- Added three measured deterministic surface families for service aggregate,
  board-formed concrete and old masonry. Their generator emits a manifest and
  contact sheet, validates compensation/wrap/brightness, and imports the
  runtime sheets at `512` with Repeat/mips. Rock, silt and iron continue to use
  the existing City families.
- Added four separate emissive practical anchors while keeping the combined
  fringe root free of `Light` components. Within `20 m`, the nearest supported
  anchor leases the eighth existing street Spot: the atmosphere changes from
  `4` bar + `8` street to `4+7+1`, never above its existing `12`-Light cap.
  The tunnel light points back toward the city; the sealed throat and east edge
  stay dark.
- Rebalanced the mountain handoff for the same fog hierarchy. Physical rock now
  floors at `0.10` visibility instead of `0.55`, while the camera-relative
  shell mixes `0.86` toward City fog, leaving a faint distant mass and allowing
  real rock and fringe detail to emerge only on approach.
- **Verification:** `tools/build-city-fringe-textures.py --verify` passed all
  three measured sheets. One focused Unity EditMode invocation passed `13/13`:
  the outer Street circuit, fringe plan/build, nine texture/import cases,
  12-Light lease contract and fog handoff. Final visual code review found that
  the emissive lens planes sat inside their opaque housings; moving them
  `0.24 m` onto the aimed face and correcting the industrial lamp to point
  across its repair stock were followed by one exact fringe regression, which
  passed `1/1`. Full suites, a player build and a manual day/night City capture
  were intentionally not run in fast mode.

## 2026-08-21 — The default city's outer road is one complete circuit

- Traced the visible edge gaps to road topology rather than rendering. The
  Kruskal graph guaranteed global connectivity, but each unused outer edge was
  still only admitted by the `0.28` optional-loop roll; the production seed
  consequently omitted nine south, north and east boundary segments.
- Added a default-blueprint-only post-pass after the seeded graph, frontage and
  Yard-access repairs. It appends every road-grid-to-exterior frontage as
  Street without rerolling or replacing any interior edge. The existing
  continuous river-bank streets and two road bridges join the two sides, so
  the complete city edge can be followed as a closed circuit.
- Added a focused regression that inventories every exterior frontage, asserts
  that it is Street, and confirms both road bridges still join the circuit.
- **Verification:** the focused Unity EditMode regressions
  `DefaultCoastalBlueprint_CreatesContinuousOuterStreetRing` and
  `DefaultCoastal_BuildsClosedWindingTargetRoute` each passed `1/1`, covering
  both the new perimeter contract and the retained closed Route 01 plan. Full
  EditMode/PlayMode suites, a player build and a manual driving smoke were
  intentionally not run in fast mode.

## 2026-08-21 — Ordinary city roofs disappear into the fog

- Split the building-height contract into a retained `5–13 m` range for bars
  and special buildings and a new `36–52 m` range for ordinary buildings. The
  canonical ordinary predicate excludes bars, the supermarket, the player's
  home, parks and district points of interest; the special authored heights
  remain unchanged.
- At the minimum ordinary height, the roof is `32 m` above the conservative
  `4 m` camera reference and retains only `0.66%` colour through City's
  `0.070` Exp2 fog. The repeating facade treatment continues through the upper
  mass, while roof motifs and rooftop landmarks intentionally disappear with
  the roof.
- Expanded `CityWorldResult.Bounds` to cover the taller generated masses and
  extended the facade-height sweep to both new endpoints.
- **Verification:** the focused EditMode regression
  `DefaultSettings_HideOnlyOrdinaryBuildingRoofsInFog` passed `1/1` in Unity.
  `BarPromenade.EditModeTests.csproj` also compiled with `0` errors (the
  existing `123` warnings remain). Full suites, a player build and a visual
  shadow review were intentionally not run in fast mode.

## 2026-08-21 — Door transitions now begin with the hero's own gesture

- Authored and exported the bone-only `DoorUseEnter`, `DoorUseLoop` and
  `DoorUseExit` actions on the production 3D rig. The planted sequence adds a
  subtle forward chest lean and short physical-right-hand press, with exact
  `Relaxed` seams and no root motion or Animation Events.
- Added one shared positioned door-action adapter to `PlayerFactory`. All eight
  ordinary bar, supermarket, home and stairwell doors now guide the visible
  hero to an explicit grounded dock and facing, hold the neutral handoff frame,
  play the action, restore the terminal neutral pose and only then request the
  existing scene transition. Owned cancellation restores input and releases
  the door for reuse, including controller-side approach aborts.
- A live Home playthrough exposed that the first interior docks used authored
  spawn height instead of the CharacterController's settled root height: Home
  was `0.08 m` outside the action system's `0.02 m` vertical tolerance, so a
  real `E` press was silently rejected. Home, bar, supermarket and both
  stairwell levels now derive their dock Y from the physical floor plus
  `GroundedRootOffset`. The opening-flow regression now acquires the real
  `PlayerInteractor` target and presses `E` from the settled floor height
  instead of calling the exit component directly.
- Rebuilt the production Blender, FBX, manifest and runtime-prefab assets. The
  generator reports `32` Actions, `1,534/4,500` triangles and valid full-rig,
  root, foot, right-grip, forward-reach and chest-incline contracts.
- **Verification:** the canonical Blender generator completed with
  `BP3D BUILD OK`; `Player3DAssetSetup` rebuilt the runtime prefab with all
  three registered clips. Focused `PlayerDoorActionPlayModeTests` passed
  `2/2`, covering terminal-only completion and both target/controller-side
  cancellation cleanup. After the live Home fix,
  `BarPromenade.PlayModeTests.csproj` compiled with `0` errors (the existing
  `17` serialized-provider warnings remain); the strengthened real-`E` Unity
  regression was not launched in a second Editor while the project was open.
  The new EditMode coverage compiled during asset refresh, but its filtered
  batch invocation did not execute because Unity exited after that refresh;
  full suites and a player build were intentionally not run in fast mode.

## 2026-08-21 — The five fringe yards became an authored service belt

- Added one deterministic `CityFringeYardPlan` for all five typed perimeter
  Yards. Four west/south strips now share retaining, drainage, maintenance-track
  and sparse utility language; the eastern Yard uses a separate low utility
  edge and does not create an eastern mountain.
- Moved the worn tunnel approach out of the mountain renderer and expanded it
  into a protected freight sequence: street apron, cross-drain, wheel ruts,
  paired tunnel cheeks and sealed portal. The mountain system still owns the
  frame, dark throat and collidered gate, with no prompt or transition.
- Added pure planning/validation plus 48-metre style/collision batches. Large
  walls, stock and utility masses are physical; tracks, drains, cables and
  markings stay colliderless, and every declared street access remains clear.
- **Verification:** `BarPromenade.Runtime.csproj` built with `0` errors (the
  existing `17` provider-field warnings remain). Two focused Unity invocations
  exercised `CityFringeYardTests`: after correcting an NUnit iterator assertion,
  planning, validation and materialization completed; the second run reached
  only the authored renderer-budget check (`106` actual versus `96` expected).
  The cap was corrected to `128`, matching the existing 48-metre batching
  contract; fast-mode's two-invocation ceiling prevented another Unity rerun.

## 2026-08-20 — The closed tunnel is readable on the scrolling map

- Replaced the map-scale gate speck with a fixed `19 x 17` high-contrast
  closed-portal marker and a localized hover label.
- When the portal lies outside the current scroll viewport, the same marker
  clamps to the visible edge and points toward it. The tunnel throat now
  explicitly participates in the west/south-only display-bound expansion.
- **Verification:** focused `CityMapMountainPresentationTests` passed `3/3`,
  including fixed marker size, off-screen edge clamping and explicit tunnel
  throat coverage without changing the north/east maxima.

## 2026-08-20 — The mountain handoff stays visible and reaches the City map

- Diagnosed the close-approach disappearance as a depth handoff, not camera
  clipping: the opaque physical crest began hiding the camera-relative shell
  while City's `0.070` Exp2 fog still left the replacement rock visually equal
  to the fog colour.
- Moved physical ridge chunks onto one shared opaque
  `CityMountainPhysical` material. Its screen-space dither is driven by
  horizontal camera distance and raises physical coverage over `43-31 m`;
  distant rock keeps a `0.55` visibility floor beyond `12 m` and blends back to
  native Exp2 by `9 m`. `DepthOnly` and `DepthNormalsOnly` repeat the same clip,
  so no prepass can erase backdrop pixels that the forward pass did not draw.
  The portal, throat, approach and sealed gate retain `RuntimePrimitiveLit`.
- Passed `World.MountainBoundaryPlan` into the City map. Its presentation
  envelope may expand only at the west and south minima, never the north/east
  maxima. The view now hatches every ridge from toe to outer foot, continues
  river blue through the south notch, and ends the tunnel throat at a crossed
  sealed-gate mark.
- **Verification:** focused `CityMapMountainPresentationTests` passed `2/2`.
  Focused `CityMountainBoundaryTests` reported `5/6`: both new physical-handoff
  and world-builder material/pass tests passed. The remaining
  `LegacyAndCustomBlueprints_StayOptOut` failed during pre-existing
  custom-layout setup in `CityElevationValidator` because the test river datum
  is inconsistent; it did not reach mountain planning. Unity imported and
  compiled `CityMountainPhysical.shader` without shader errors.

## 2026-08-20 — The coastal city now sits inside a west/south mountain basin

- Added an opt-in `CityMountainBoundaryPlan` for `default-coastal`. Its pure
  planner and validator derive only West and South ridge strips from the
  stable perimeter Yard IDs and authoritative terrain samples; legacy and
  custom blueprints keep an empty plan. West tapers before the northern beach,
  the south-west corner receives a diagonal join, North and East receive no
  mountain descriptors, and the south rim owns a separate river notch.
- Materialized the physical boundary as chunked flat-shaded low-poly rock.
  The near toe owns collision; the tall rear mass is presentation-only and
  casts no large distant shadows. One dedicated deterministic weathered-rock
  sheet, `CityMountainRockAlbedo`, is generated and validated by
  `tools/build-city-mountain-textures.py`, then applied through MPBs on the
  shared primitive material.
- Derived one south-west portal from `yard-south-west-access`: an approximately
  `8 x 5.5 m` rock opening, worn approach, short dark throat and a visible
  collidered metal gate. It is intentionally sealed. No interaction, prompt,
  scene ID, transition target or walkable-mask contribution was added.
- Added a separate two-layer camera-relative ridge shell at `39.4-43.2 m` for
  only the world-west and world-south sectors. It follows camera translation
  without rotation, contributes no collision, light, shadow, map, navigation
  or City result bounds, keeps the river-axis gorge open, and uses a dedicated
  shader that mixes the authored silhouette with City's haze instead of
  applying distance fog again. The
  existing `0.070` Exp2 fog and `48 m` far clip remain unchanged.
- Integrated the physical boundary and backdrop into `CityWorldBuilder` while
  retaining the original gameplay bounds and walkable area.
- **Verification:** `python tools/build-city-mountain-textures.py --verify`
  passed the deterministic sheet contract (`2.2%` brightness error,
  `0.84x` seam ratio). Unity `6000.5.9f1` imported and compiled the runtime,
  EditMode fixture and backdrop shader without errors after one NUnit syntax
  correction. The intended focused fixture did not execute: the second and
  final fast-mode invocation accepted `-quit` immediately after refresh, so
  it produced no results XML. A rendered day/night boundary review was not
  run in batch mode and remains the manual visual check.

## 2026-08-20 — Waking is four beats, and none of them is a roll

- **The user watched it and said it was still wrong, then said exactly what he
  wanted:** sit up into a half-crouch on the bed, drop the right leg, then the
  left, and only then stand. So `BedExit` was re-cut to that and the roll onto
  the side deleted outright.
- **It is also the anatomically obvious motion.** The hero sleeps head toward
  `-X`, face up, and the only way off the bed is the door-side `-Z` edge —
  which puts his *right* side toward that edge. The near leg going first is
  not a stylistic choice; it is the one that can go first.
- **Removing the roll bought the checkability back.** The previous pass had to
  leave the mid-roll unasserted, because a rolling body's support genuinely
  moves and `PlayerAnimatedInteractionPelvisTransition` carries one waypoint,
  not a profile. A sit-up keeps his weight on the mattress from the first frame
  to the moment the first boot leaves it, so all ten samples through `0.50`
  are now asserted against the eased pelvis path instead of excused.
- **The beat itself is a measurement now.** `validate_bed_support_contract`
  reports, and refuses to drift on: both boots up on the bedding for the
  half-crouch (`L+0.547 R+0.540` over the floor, mattress at `0.560`), the
  right boot down while the left is still up (`L+0.523 R-0.015` — half a metre
  of separation), and both planted before he stands (`L-0.009 R-0.015`).
  Nothing else in the pipeline could notice if that order were lost.
- **The thighs have to fold as fast as the pelvis rises.** Authored blind, they
  swung straight down through the mattress — `0.40 m` below the pelvis at the
  elbow-prop landmark against `0.098 m` of clearance. In this rig the pelvis
  rotation carries the legs, so a lying-pose thigh angle points the legs at the
  floor the moment the torso comes up. Four measured passes fixed it; the body
  now tracks the runtime plane within the stated bedding give at every sample.
- **`BedExit` grew 3.75 s → 6.0 s** (72 frames at 12 fps) because four beats do
  not fit in four seconds. That forced `BedTransitionFrameCount` to split into
  `BedEnterFrameCount`/`BedExitFrameCount`, moved the seat window to
  `0.50`–`0.88`, and dropped `OpeningWakeDurationMultiplier` to `1.15`.
- **One silent-pass hazard found and closed.** Every bed and opening assertion
  recomputes from `Definition.ExitFrameCount`, so the C# frame count and the
  authored clip could have drifted apart with the whole suite green and the
  wake playing at the wrong speed. `Player3DAssetImportTests`
  `BedTimingConstants_ReproduceTheAuthoredClipDurations` now compares the
  runtime constants against the manifest for all three bed clips.
- **Verification:** generator clean; full export; `Player3DAssetSetup.Run`
  (the `BedExit` clip kept its `internalID`, so the prefab's binding survived);
  full EditMode **1299/1299**; PlayMode `HomeBedInteractionPlayModeTests`
  **5/5**. `HomeOpeningPlayModeTests` still fails on its AudioSource count —
  the same pre-existing failure proven against a stashed baseline earlier.
- **Not run, and not runnable here:** whether it now looks like a man getting
  out of bed. Batch mode has no game view.

## 2026-08-20 — The bed was built beside the sleeper, not under him

- **Two complaints, two measurable causes.** "Like a wooden doll" was
  `_create_action`'s `interpolation="LINEAR"` default: only `Idle` and `Walk`
  ever passed `BEZIER`, so eight landmarks in three seconds meant constant
  speed inside each segment and an infinite-acceleration corner at every one.
  "Sinks into the bed" was `BedDressingSurfaceHeight = 0.67`, a number that
  matched no surface the builder actually made — mattress `0.56`, crooked
  blanket `0.66`, pillow `0.73`.
- **Nothing catches him.** Runtime pins a contextual clip by its pelvis bone
  (`AlignActiveClipAnchor`) and `ApplyProceduralStatusPose` returns early for
  as long as a clip is active, so `GroundOrdinaryPose` never corrects the
  lying body. One guessed clearance decided everything, and it was wrong in
  three directions at once: hips three centimetres inside the blanket, head
  twelve inside the pillow, and — measured for the first time here — the
  bedside sit eight centimetres above the bedding with both boots off the
  floor. He was not sitting on the bed; he was hovering over it.
- **Measure, then build.** Authoring blind does not work on this rig, so
  `validate_bed_support_contract` reports rather than guesses: it samples the
  real posed meshes and prints how far the supine back (`0.1377`, the jacket),
  the back of the lifted head (`0.0656`, the hair cap) and the seated weight
  (`0.0239`, the lift that plants both boots) hang below the pelvis bone.
  `PlayerCharacterDimensions` mirrors those three; `HomeBedInteractionPlan`
  adds them to `BedMattressSurfaceHeight`; the pillow's top is then *derived*
  from the head offset instead of being placed and hoped for. Four probe runs
  fixed the axis signs — both of my first guesses were inverted, and shin
  rotation turned out to barely move the boot at all while the thigh moved it
  `0.012 m` per degree.
- **Overlap needed new machinery.** `_create_action` keyed all 25 bones on
  every landmark, which makes lag physically impossible. A key may now name a
  subset; `BED_LEADING_BONES` takes a landmark and `BED_TRAILING_BONES` takes
  the same one a few frames later. Both endpoints must still key the whole
  rig, and the function refuses a partial one. That plus Bezier is what
  removed the doll read; `BedEnter`/`BedExit` also grew from `3.0 s` to
  `3.75 s` with anticipation, a bedside settle, a hand planted on the mattress
  through the lowering, and a held beat with his hands on his knees before he
  stands.
- **The blanket moved, not the sleeper.** Per the user's decision he lies on
  the mattress; the crooked blanket is shoved to the wall side clear of his
  corridor and the crumpled shirt now lies on the mattress it was always meant
  to be dropped on. `HomeInteriorPresentationPlayModeTests` asserted the shirt
  rests on the blanket, and now asserts the mattress.
- **What is deliberately not asserted.** The roll between edge and back. A
  rolling body's support genuinely moves — on your side your hips ride higher
  than on your back — and `PlayerAnimatedInteractionPelvisTransition` carries
  one waypoint, not a profile, so asserting through it would be asserting
  against the runtime rather than against the pose. Modelling it properly
  means reshaping code shared by five interactions, and the user scoped this
  to the bed. The held poses either side of the roll are exact; the bedside
  seat and the waking stir carry a stated soft-goods allowance because bedding
  compresses and runtime starts easing the pelvis edge-ward from the wake's
  first frame.
- **Verification:** `blender --background --factory-startup --python
  tools/build-player-3d-model.py` clean, then the full export; `dotnet build`
  on Runtime, EditModeTests and PlayModeTests (0 errors); `Unity.exe
  -batchmode -executeMethod Player3DAssetSetup.Run`; the **whole** EditMode
  suite — `1298/1298`, including the five new `HomeBedDressingGeometryTests`;
  PlayMode `HomeBedInteractionPlayModeTests` `5/5` with the new
  `Bed_SleepAndWakeNeverPushTheHeroThroughTheMattress` sweeping every
  anatomical renderer through the loop and the wake.
- **Pre-existing red, confirmed by baseline rather than assumed.** Six
  PlayMode failures survive on this branch and are none of mine — I stashed my
  work and re-ran to prove it: two bus-passenger tests, the city route's
  boarding dock, `Smoking_ClickableExitQueuesAtCalmFrameAndRestores`, the home
  entry lamp's viewport framing (identical to five decimals with my changes
  absent), and `HomeOpeningPlayModeTests`' AudioSource count (`13` vs `10`),
  which counts sources long before the wake and belongs to the place-music
  work committed just before this. `StatusFaceFallsAndContactShadowDrive3DBonesAndCleanUp` is
  marginally red either way and floats — `0.526 / 0.565 / 1.004` against a
  `0.5` threshold across runs.
- **Not run, and not runnable here:** the look of it. Batch mode has no game
  view, and no test can tell whether a man now lies down like a man. That
  wants a play-mode look from the Home room shot and from the opening's
  sleeper camera.

## 2026-08-20 — A theme can belong to a place, not only to a scene

- **The place is already in the data.** `CityDistrictKind` has carried
  `Cemetery` all along, and `CityCemeteryPlan.Grounds` is the fenced footprint
  in world XZ. Nothing new had to be authored to know where the cemetery is;
  the music just had to read it.
- **`CityLocationMusicDirector` holds the table.** One
  `CityLocationMusicSlot` per place — id, grounds, player — with `city_theme`
  as the default underneath. It resolves the hero's place each frame and hands
  the mix over on a change. Adding another place is one more slot, not more
  logic. The only slot today is the cemetery, and a seed without one
  contributes nothing.
- **The boundary is pure and tested.** `CityLocationMusicZones.Resolve` takes
  the grounds, the active index and the hero's XZ; the active place keeps the
  mix until he is `ExitMarginMeters` (`4 m`) clear of it. Without that hold a
  walk along the fence would flap the mix, and each flap costs a whole
  fade-out and fade-in.
- **The handover is the existing rule at its existing length.** A shorter
  `2 s` in-world fade was built and then dropped on the user's call: one rule
  means one number, so walking between places in the city costs the same `4 s`
  as a scene change. Nothing in the director spells the length out — it reads
  `MusicMix.FadeOutSeconds` like everything else, and the order comes for free
  because `FadeOutAndPause` registers with the mix and `ResumeWithFadeIn`
  defers on it.
- **Two things a place theme must not do.** It must not play before the hero
  ever walked in, so every slot is parked with `FadeOutAndPause(0f)` at
  initialization; and an empty optional slot must not be able to silence the
  city, so a slot whose clip is absent is dropped rather than accepted. Both
  themes resume from their own sample, so leaving and returning continues each
  track where it stopped.
- **Verification.** EditMode `CityLocationMusicZonesTests` 7/7 (hysteresis,
  neighbouring grounds, degenerate rects, rejected margin). PlayMode
  `CityLocationMusicDirectorPlayModeTests` 3/3 (handover through the rule,
  the hold margin, an empty slot leaving the city playing, and opening
  already inside the grounds). PlayMode
  `SceneFlowSmokeTests.EnterAndExitBar_ReturnsToSameBarInSameCity` green with
  the director wired into `CityGameRoot`. Runtime, EditMode and PlayMode
  assemblies compile.
- **Pre-existing failure, left alone.**
  `SceneFlowSmokeTests.CityScene_BootstrapsGeneratedWorldPlayerAndFourBars`
  fails on a stale city-size assertion — `Layout.BlockCount` expects
  `(12, 12)`, the default city is `(17, 14)`. Unrelated to audio and out of
  scope; the location-music coverage was moved into the bar round-trip test,
  which actually runs.

## 2026-08-20 — One theme leaves before the next one starts

- **The rule lives in one place.** `MusicMix` owns `FadeOutSeconds = 4`,
  `FadeInSeconds = 1` and the registry of themes that are still leaving.
  `SceneMusicPlayer.DefaultFadeDurationSeconds` and
  `HomeMusicPlayer.BalconyFadeDurationSeconds` are gone; every fade in the
  game now reads its length from the rule.
- **Nothing starts over an unfinished fade.** A theme can only begin through
  `BeginFadeInThroughRule`, which holds it in the new `WaitingForMix` state
  while `MusicMix.IsFadeOutActive` and starts it — `Play()` included, so the
  track begins at its head — the frame the mix clears. Themes hand over
  instead of crossfading.
- **The tail outlives its scene instead of blocking it.** Four seconds of
  fade-out and a `3.15 s` door presentation would have stacked into a
  seven-second pause at every door. `MusicMix.BeginDetachedFadeOut` reparents
  the music object into `DontDestroyOnLoad` and keeps the same `AudioSource`
  running, so the streaming clip is never re-seeked and never clicks; the
  player destroys its own carrier at zero. The old activation gate
  (`IsOutgoingMusicFadeGateComplete`, `MusicFadeSafetyTimeoutSeconds`,
  `AreMusicFadesComplete`) is deleted — `SceneTransitionService` just asks
  every `IMusicMixSource` in the outgoing scene to leave.
- **The in-scene changes obey the same rule.** Home fades `home_theme` out
  over `4 s` on the Balcony shot and back in over `1 s` indoors, and
  `HomeSmokingMusicPlayer` waits for that fade before its first note, eases in
  over `FadeInSeconds` when it had to wait, and leaves through
  `BeginRuleFadeOut` at the `Exiting` phase rather than the shorter
  camera-restore ramp.
- **Verification.** Focused PlayMode run of
  `SceneMusicPlayerPlayModeTests`, `HomeMusicPlayerPlayModeTests` and
  `HomeSmokingMusicPlayerPlayModeTests`: 15/15 green, including two new
  cases proving a theme stays silent until the outgoing one reaches zero and
  that the vignette theme leaves through the shared fade. Because the change
  is shared, one more focused run covered the real round trip:
  `SceneFlowSmokeTests.EnterAndExitBar_ReturnsToSameBarInSameCity` is green,
  with its old "City music must stop when the bar replaces City" assertion
  rewritten — a surviving city player is now allowed only as a detached
  fade-out, and the test waits for it to go. The rest of the scene-flow suite
  was out of scope.

## 2026-08-20 — The watchman has a yard of work, not one hole

- **The job is repeatable and there can be several of them at once.**
  `CemeteryGravediggingRegister` owns one `CemeteryGravediggingController`
  per grave the hero was ever sent to plus the one on offer, and finds the
  next offer with `CemeteryGravediggingPlan.Create(plan, watchman, taken)` —
  the nearest vacant plot he has not signed over yet, so the work walks
  outward from the lodge. `MaximumOpenJobs` is `3`: three unfinished holes is
  as much as he will let one man hold, and until one is closed the window
  gives quips instead of plots. The offer, the acceptance and the refusal are
  the same three keys they always were.
- **The stage is per plot now.** `GameSessionState.GraveWorkStage` and
  `GraveEpitaph` are gone; `CemeteryGraveWorkLedger` holds one record per
  plot id — stage and epitaph — and `TryAdvanceGraveWork(plotId, stage)` /
  `TrySetGraveEpitaph(plotId, text)` are keyed by it. Each worksite is still
  a pure function of one stored value, so a city rebuild stands the whole
  yard back up: half-dug holes open, finished stones standing, each board
  carrying its own line (`CemeteryPlaqueSurface.Initialize`). A record whose
  plot the current seed does not have is logged and skipped.
- **One log entry, however many holes.** `QuestDefinition.IsRepeatable`
  marks `DigTheGrave`, `QuestLogState.TryActivate` revives a completed
  repeatable quest in place rather than adding a second line, and the quest
  is driven from `TryAdvanceGraveWork` instead of from the controller: up on
  any `Marked`, down only when nothing is unfinished.
- **Money before the next hole.** `ICemeteryWorkGiver` is what the watchman
  now speaks for — one controller answers it, and so does the register, which
  is what keeps `CemeteryWatchmanTests` and the single-job tests honest. He
  pays for every closed grave in one sum (`CollectWages`) and only offers
  after there is nothing owed, because a man who has just filled one in wants
  paying before he is asked to open another.
- **One session, many worksites.** `ICemeteryGraveWorkSession.TryBegin` takes
  the grave as an argument and `CemeteryGraveWorkController` binds to
  whichever hole raised the request, and to whichever finished board the hero
  stops to read. There is one camera and one hero, so there is still exactly
  one session.
- **Verified:** EditMode `CemeteryGravedigging`, `CemeteryGraveWork` and
  `CemeteryWatchman` fixtures — 37/37 green, including the two new contracts
  (`TheWatchmanGivesGraveAfterGraveUpToWhatAManHolds`,
  `EveryGraveHeGaveComesBackOnTheNextCityBuild`). Not run: the rest of the
  EditMode suite and any PlayMode work.

## 2026-08-20 — The spade acts stop being a swing, and the soil goes with it

- **Digging and filling are a choice of square and a press.** The timing bar
  is out of both, at the user's direction, and they were right: `18` timed
  swings is one shot demanded eighteen times, and it was the same shot every
  time. What was left once it went is the thing that was actually carrying
  the act — the lattice rule that no segment may go deeper than its
  shallowest neighbour, which is a decision about *where* to work. `A`/`D`
  move the spade, `E` takes a course, and `CemeteryGraveLatticeModel.TryStrike`
  no longer consults anything but that rule.
- **The chosen square is outlined in the hole, not on the panel.**
  `CityCemeterySegmentFrameWorldBuilder` builds one thin chalk-white frame and
  moves it, since every segment of a lattice is the same size, and
  `Place` drops it on the working face of the chosen segment — down as the
  digging goes, up as the filling comes back. This is what made the panel's
  `3 x 2` map redundant: with the timing bar gone, choosing the square was the
  whole act, and six identical patches of earth with the map beside them was a
  puzzle to be solved against the world rather than a picture of it. The panel
  is gone for both spade acts; what is left is one hint line naming two keys.
  The two acts no longer share it — `Coffined` returns `FillHintKey` now —
  because a line reading "dig" at a hero shovelling a hole shut is the wrong
  instruction however cheap it is to share.
- **The soil system is deleted outright.** `CemeteryGraveSoil` (turf, loam,
  clay, stone, root, spoil and their table) is gone, with the lattice's
  per-course ground, the controller's `NextGroundSeed`, the view's soil
  colours and label, the six `cemetery.soil.*` keys in both catalogs and four
  tests. A kind of ground that changes nothing and is shown nowhere is not
  detail, it is weight. `CemeterySoilProfile` survives in substance as
  `CemeterySwingProfile`, declared in `CemeteryStrokeModel` and stated by
  `CemeteryStoneSettleSettings.TampProfile`: the three blows that set the
  stone are the only timed swing left in the job, so its shape belongs where
  it is used. `TheOnlyTimedSwingLeftIsWideEnoughToHit` still measures that
  window off the model in seconds, which is the mistake that unit invited
  once already.
- **The plaque's face is found by triangles, not vertices.** The band search
  shipped reading the monument's *vertices*, and `Attach` returned null on
  every stone. A throwaway EditMode probe said why in one line: a monument is
  a combined box mesh with 48 vertices, all of them at box corners, so a
  horizontal band anywhere between two corner rows samples nothing at all.
  `TryFindSolidFace` now walks the triangles whose `[minY, maxY]` span
  contains the seat height and takes their corners, which is a surface rather
  than a point cloud and cannot have holes between rows. It searches top-down
  from `0.74` to `0.44` of the stone's height with a `0.10 m` threshold and
  cuts the board to whatever the stone offers there, so the plate lands on the
  head of a stele and between the arms of a cross instead of on the plinth.
  The probe is now `TheBoardSitsOnTheStoneAndCarriesReadableLines`.
- **Verified:** EditMode `CemeteryGraveWork`, `CemeteryGravedigging`,
  `CemeteryWatchman`, `LocalizationCatalog`, `RetroSfxLibrary` and
  `CityCemeteryPlanner`, 84/84. The world frame's own geometry is not covered
  — it is built from `GetSegmentRect` and placed by `GetSegmentFace`, both of
  which are, but that the outline reads as an outline on screen is an eyeball
  matter like the rest of the IMGUI surface.

## 2026-08-19 — The gravedigging is worked, not pressed

- **Four acts, four games.** The ladder gains a rung —
  `Unclaimed → Marked → Dug → Coffined → Filled → Sealed → Paid` — because
  closing a hole cannot be undone, so filling and setting the stone had to be
  separate commits. `CemeteryGraveWorkController` runs each act as a modal
  session over `BarMinigameModalLock`, blends the camera down onto the grave
  the way `CityBoardGameController` does, and calls
  `CemeteryGravediggingController.TryAdvance` only at the end.
  `TryAdvance` itself is untouched: the site prompt now goes through
  `RequestAdvance`, which hands the act to an `ICemeteryGraveWorkSession` if
  one is attached and otherwise commits outright — which is what every
  EditMode test and any headless build still sees.
- **Nothing is committed until it is finished.** A session owns everything it
  puts in the world and takes it all back out on `Esc`, so
  `GameSessionState` gained no field: the worksite stays a pure function of
  one stored stage, and that stage never has to describe half a hole.
- **The hole is a lattice.** `CemeteryGraveLatticeModel` divides the mouth
  `3 × 2` and each segment into three courses, `18` in all. One rule holds
  it together — a segment may only be worked while it is no deeper than its
  shallowest neighbour — which forbids pillars, cannot deadlock (the globally
  shallowest segment always satisfies it), and is the whole reason the pit is
  divided. `CemeterySoilTable` gives turf, loam, clay, stone and root their
  own bite width, swing speed and strike count; a jarred blade only costs
  progress on a root. Filling is the same lattice read upward and meets
  nothing but spoil, so only digging has ground worth varying.
- **The ground is re-rolled on every attempt.** The lattice takes an `int`
  seed and is a pure function of it — one number is one hole, which is what
  makes it testable — but `CemeteryGraveWorkController.NextGroundSeed` draws a
  fresh one each time an act opens. This is deliberate and it is the one
  number in the job not derived from the city seed: `CitySeed` is never
  actually changed at runtime (`SetCitySeed` has no gameplay caller), so
  seeding the soil from the plot id meant every playthrough met the same stone
  in the same corner. Recorded as an accepted exception in
  `ai/architecture-notes.md`; the price is that a bad roll can be re-rolled by
  abandoning, which costs everything already dug.
- **The spade is the only animated thing.** `CityGravediggerShovelWorldBuilder`
  builds it from oriented boxes with its origin at the blade's point, and
  `CemeteryShovelAnimator` drives it through drive/lever/lift/dump. The hero
  is leased out of sight through `PlayerPresentationVisibility` and the camera
  takes his own eye line, so there is no rig to disagree with — an accepted
  deviation, recorded in `ai/architecture-notes.md`, and the reason
  `ai/contextual-animation-standard.md` does not apply here.
- **Geometry.** `CityCemeteryProgressivePitWorldBuilder` states the half-dug
  hole as one earth block per segment from the floor up to its working face,
  which is the same expression for both acts. The slab is cut once at the
  first spadeful and not touched again, so a stroke costs one small combined
  mesh rather than a rebuild of the cemetery ground.
  `CityCemeteryGroundExcavation.Excavate` is now idempotent for an identical
  rectangle, mirroring `Fill`, so the commit survives finding its own hole
  already open; an overlapping *different* rectangle is still refused.
  `CityCemeteryPitWorldBuilder.AppendSpoil` grew a fullness so the heap grows
  and shrinks smoothly instead of a course at a time.
- **The swing was unplayable and is retuned.** The first numbers were chosen
  as widths and shipped as widths, which is the wrong unit: the marker is a
  sine, so it runs *fastest* exactly where the biting window is. Measured as
  time in hand, stone gave `20 ms` — one frame at 60 — and loam `52 ms`. Bite
  bands roughly doubled and sweep rates roughly halved, putting the range at
  `116 ms` (stone) to `245 ms` (turf).
  `CemeteryGraveWorkTests.EveryGroundLeavesTimeEnoughToActuallyHitIt` now
  measures every row off `CemeteryStrokeModel` itself and holds it between
  `0.10 s` and `0.32 s`, so the unit can never quietly become width again.
- **The spade stood inside the waiting coffin.** The kit's two offsets were
  chosen independently and the spade's landed within the box's outline, so the
  handle came up through the lid. It now stands past the end of the box, and
  `CemeteryGravediggingPlan.ValidateOrThrow` projects the gap onto the
  coffin's own long axis and refuses a plan where it is smaller than
  `CoffinHalfSpanMeters`, so the two numbers can no longer drift into each
  other unnoticed.
- **Hard ground no longer wants two strikes.** `StrikesPerCourse` and
  `ResetsOnJar` are gone from `CemeterySoilProfile` along with the lattice's
  per-course strike counter: one good strike is one course, whatever it is
  made of. Asking for a second hit on the same square is the same shot
  demanded twice, and it only lengthened the act. What still separates stone
  from loam is the width of the window, so stone and root were tightened to
  `105` and `122` ms against loam's `192`.
- **Act two was "hold Q and E", and now it cannot be.** The old model paid
  both ropes out at matched rates, so holding both lowered the box level with
  nothing to control. The fix is the user's own: `Q` and `E` still pay their
  own end out and nothing held still means nothing moves — a coffin on two
  unattended ropes does not descend, and pretending otherwise was an
  overcomplication I went down a long way before being pulled back — but the
  *balance point* crawls. Fresh ground gives under one bearer and then the
  other, so the setting of the ropes that hangs the box level moves, and moves
  further than the tolerance is wide. Standing still now loses it because
  level walks out from under a box that never moved; holding both loses it
  because the head pays out faster than the foot and the point keeps going.
  Rope only ever goes out, so correcting and descending are the same action
  and every correction costs depth. The gauge draws the moving band and a
  needle for the balance the hero controls — a band pinned to the centre would
  say "hold still", which is the one thing that does not work.
  The tuning trap is recorded in the settings: the point must crawl slower
  *on average* than the slower rope can move the balance. An early pass had a
  wander that outran the ropes, which is not difficulty, it is impossibility.
  `NoWayOfNotPlayingLowersTheCoffin` holds all four ways of not playing
  against four balance-point phases, and `FollowingTheBalancePointLandsIt`
  proves the intended play lands across three deadzones, three reaction delays
  and four phases — a window one reaction speed wide is not a mechanic.
- **The board was blank and hanging, and guessing had run out.** Two more
  passes of reasoning had produced two more wrong fixes, so this one was
  measured: a throwaway EditMode probe printing the real bounds of the stone,
  the board and every line on it.
  It said the type was `4 mm` tall on a plate `200 mm` deep. TextMeshPro
  measures its size in the same units as its rect, and on this board one unit
  of size comes out at roughly `0.095 m` of line — so the first numbers, read
  as points, overflowed the rect and `Truncate` threw whole lines away, and
  the second, read as metres, drew letters too small to see. Sizes are now
  taken off that measurement, and every line auto-shrinks rather than being
  dropped, because a truncated line renders as nothing and reads as a bug.
  It also said the board was seated on the *bounding box* of the monument.
  For a stele that is the front face; for the Orthodox cross — one of the four
  silhouettes the plot hash can pick — it is the air between the arms.
  `TryFindSolidFace` now walks the stone's own vertices down in bands, takes
  the first band wide enough to carry a plate, and puts the board on the real
  front of the solid there.
  The probe became `TheBoardSitsOnTheStoneAndCarriesReadableLines`: the bezel
  must intersect the stone, the plate must sit within its height, and each of
  the three lines must draw more than `12 mm` of letter and fit inside the
  brass. None of that was visible to any test before, which is why it shipped
  twice.
- **Five faults in the stone act, and two of them were mine at the root.**
  The committed monument floated because it was placed by its authored parts
  while the one in the hero's hands was placed by its own measured bounds —
  two different rules for where a stone sits. `BuildStandingStone` now routes
  the final one through the same `ApplyLyingPose` that seats it during the
  act, so what he lets go of is what stays. The board showed its words back to
  front because I turned the text a further half-circle on top of a board that
  was already facing the reader: TextMeshPro lays its quads out with normals
  of `(0, 0, -1)`, so a line is readable from its own local -Z and needs no
  flip at all. The board hung in the air because `Attach` measures the stone
  to find its face and I was calling it at act start — against a stone still
  flat on the grass, whose bounds are a different shape entirely; it is now
  fitted the moment the stone comes upright. `ApplyLyingPose` also stopped
  letting the plaque vote on where the stone's foot is.
- **No field to type into, and reading is a camera move.** The inscription is
  taken straight off `Keyboard.onTextInput` and drawn on the brass as it
  arrives, with a cut mark at the end; the only thing left on screen is one
  bare hint line with the words remaining. `CemeteryPlaqueView` is deleted
  outright — with real letters on the stone there is nothing to put on screen
  that is not already on the grave — and `TryBeginReading` takes the modal
  lock and brings the camera to the board instead.
- **The plaque letters are TextMeshPro, not a hand-made font.** The user asked
  the obvious question — Minecraft signs just draw text onto a texture, and
  Russian works there — and they were right that nothing fundamental was in the
  way. Two simpler paths existed and I had not said so: Unity's built-in font
  renders Cyrillic (the whole UI proves it), and `Unity.TextMeshPro.dll` was
  already compiled in `Library/ScriptAssemblies`, shipped inside
  `com.unity.ugui` 2.5.0, merely unreferenced by our asmdef. The hand-authored
  `5 x 7` font was a defensible choice — headlessly pixel-testable, no imported
  binaries, exactly the PS1 look — but it was a choice, and its glyph shapes
  were drawn blind.
  Now: asmdef references `Unity.TextMeshPro`, TMP Essential Resources are
  imported (`Assets/TextMesh Pro`, 79 files — the project's first imported
  binary assets, and the price of this route), `Roboto-Regular.ttf` sits in
  `Assets/Resources/Fonts` (Apache-2.0, taken from a Unity package this
  project already pulls, and its cmap verified to carry all 64 of А-я plus Ё),
  and `CemeteryPlaqueFont` builds a dynamic-atlas `TMP_FontAsset` from it at
  runtime so no generated SDF asset is committed. The board's three lines are
  world-space `TextMeshPro` objects laid on the plate, the epitaph auto-sizing
  so any line the word limit allows fits. `CemeteryPlaqueTexture` and its
  pixel tests are gone; what replaces them checks the face itself carries the
  whole Russian alphabet, which is the thing that could quietly be swapped for
  a Latin-only one.
  Note what was lost: the old test proved ink actually reached the plate. TMP
  renders through a graphics device, so that guarantee is now an eyeball one.
- **One stone, a board really on it, and words really on the board.** Three
  faults in the first pass at act four, all of them visible at a glance.
  The session was building a second monument beside the one already lying by
  the head, so it now borrows `CemeteryGravediggingController.LyingStone` the
  way the coffin and the spade are borrowed — one stone, heaved upright and
  driven home, and `RestLyingStone` puts it back on its side if the act is
  abandoned. The plaque hung at a fixed offset from the plot centre rather
  than on the stone; `CityCemeteryPlaqueWorldBuilder.Attach` now measures the
  monument's renderers, seats the board against its real front face at a share
  of its real height and parents it to the stone, so it rides the thing while
  it is being stood up and fits a narrow cross as well as a wide stele.
- **The plaque's letters are a font now, not a panel.** `CitySignLettering` is
  eleven glyphs covering exactly what the city's signs spell, and a board has
  to carry whatever is typed at it, so `CemeteryPlaqueTexture` is a real
  `5 x 7` bitmap font — full Cyrillic and Latin, figures and punctuation, with
  the dozen Cyrillic capitals that are Latin shapes aliased rather than drawn
  twice — rasterized into a point-filtered `168 x 112` texture and handed to
  the shared material through a property block. The plate is its own quad with
  authored UVs, because a cube's faces do not all run the same way up and half
  of them would mirror the text. `CemeteryPlaqueSurface` re-stamps it the
  moment the line is cut. Three tests hold it: every character the player can
  type has a glyph, every epitaph the word limit allows survives wrapping with
  all its words, and a stamped plate actually carries ink.
- **The stone act is two efforts and a plaque.** The monument now lies flat by
  the head of the plot from the moment the job is taken — part of the kit, like
  the coffin and the spade — and the lamp and spade survive to `Sealed` rather
  than `Filled`, because the last act is worked by the same light and driven
  home with the back of the same spade. `CemeteryStoneSettleModel` was rewritten
  from the plumb-bubble into `Raising` (press `E`, dead weight, no window, sags
  when let go) and `Setting` (three timed blows on the shared swing, a miss
  costing only the swing). `CemeteryShovelAnimator.PlayTamp` brings the spade
  down flat from over the head. Nothing in the act can be failed: it is the
  last thing between the hero and his wage.
- **The plaque, and the only text a player writes.** Three lines — a name
  nobody gave, a span nobody knows, and one the hero cuts himself.
  `CemeteryEpitaph` is pure: `CountWords`, `IsWithinLimits` and `Sanitize`
  hold it to eight words and sixty-four characters, and the field refuses a
  ninth word rather than truncating one silently. It is stored through
  `GameSessionState.TrySetGraveEpitaph`, which writes once and refuses a
  second attempt. Note the limit of "permanently": the project has no save
  files at all, so it survives scene loads and dies with the session, exactly
  like every other piece of session state.
  The board carries no letters as geometry. `CitySignLettering` knows only the
  glyphs the city's own signs spell and a plaque has to hold whatever is typed,
  so the board is a board (`CityCemeteryPlaqueWorldBuilder`) and the words live
  on the panel — with `CemeteryPlaqueReadInteraction` left on the finished
  grave, or the line would be visible once and never again.
- **Two shots the act needed.** `EvaluateCamera` gained a signed lateral shift
  so standing the stone up leans toward the side it is lying on, and
  `EvaluatePlaqueCamera` walks round to the front of the monument for the
  inscription — the digger works from the flank, so his own eye line sees the
  board edge-on and could never read it.
- **The bearers are gone and the rope keys follow the shot.** Two timbers laid
  across the mouth were the one thing standing where the coffin has to pass, so
  `CemeteryGraveTrestle` became `CemeteryGraveSlings`: four ropes running down
  from the ground line, no timber. The blocks the box waited on keep their own
  colour and stay at the foot of the plot. And `Q`/`E` were bound to the head
  and foot of the grave, which puts them the right way round on half the yard
  and backwards on the other half depending on the plot's heading against the
  side the digger stands on; they are now bound to the left and right of the
  shot, with `CoffinGaugeSign` mirroring the needle and the sliding band to
  match. `TheRopeKeysFollowTheShotAndNotTheGrave` holds both headings.
- **The panel was sized to the picture and clipped the words.** IMGUI
  truncates a label that does not fit; it does not shrink one. The hint names
  three controls in a sentence and lost both ends of itself at `292` logical
  px. Panel widened to `356`, the three rows given explicit heights, and the
  hint wrapped over two lines so a longer locale drops a line instead of
  losing its ends. The layout came out of `OnGUI` into
  `CemeteryGraveWorkView.CreateLayout` / `CreateLatticeRect` /
  `CreateSideRect` so `ThePanelHoldsEverythingItDraws` can hold it to
  containment and ordering — the only part of an IMGUI surface testable
  without a game view.
- **The gear stands on the plot, and there is only ever one of each.**
  Taking the job now also sets down the coffin on two timber blocks and drives
  the spade into the ground beside it, past the foot of the grave — the only
  clear ground on the plot, since the heap owns one flank, the digger and his
  camera own the other and the lamp owns the head. `CemeteryGravediggingPlan`
  carries `CoffinRestGround` / `SpadeRestGround` with a per-plot skew, and
  `ValidateOrThrow` refuses a plan that would leave either of them standing on
  the worksite.
- **The acts borrow those props rather than raising their own.** This was the
  point: a session that built its own spade put two spades on one plot. The
  spade and the waiting coffin now belong to `CemeteryGravediggingController`
  and are exposed as `Spade` / `WaitingCoffin`;
  `CemeteryGraveWorkController` enables the animator already on the spade and
  plays `PlayTakeUp`, so the object the player has been looking at is the
  object that comes to hand, and `PlayLayDown` stands it back in the ground
  under the camera blend. Act two moves the waiting coffin onto the bearers
  instead of spawning one, so `LowerCoffin` consumes the same box.
  `ThereIsOnlyEverOneSpadeOnTheWorksite` and
  `TheWaitingCoffinIsTheOneThatGoesInTheHole` count them through the whole
  ladder. Lifetimes stay a pure function of the stage: spade and blocks
  through `Coffined`, waiting coffin through `Dug`, all tidied at `Filled`.
- **The lamp belongs to the job, not to the hole.** It is raised in
  `TryAccept` and in the `Marked` branch of `Restore`, not in `Dig`. Digging
  is now a timed act, and timing a spade against ground lit by one distant
  alley lamp is not a difficulty setting.
- **Verified:** EditMode `CemeteryGraveWork`, `CemeteryGravedigging`,
  `CemeteryWatchman`, `LocalizationCatalog` and `RetroSfxLibrary` suites,
  67/67 after the retune (80/80 with `CityCemeteryPlanner` and
  `InteractionPromptView` before it). The panel is IMGUI and cannot be
  captured headlessly, so its layout is not covered; the framing rule is
  (`CemeteryGraveWorkStance` is pure and asserts the shot looks down into the
  hole from the dry side of it).

## 2026-08-19 — A grave takes three acts, and the third one pays

- **The job is three interactions, not one.** `CemeteryGraveWorkStage` is a
  monotone ladder (`Unclaimed` → `Marked` → `Dug` → `Coffined` → `Sealed` →
  `Paid`) carried in `GameSessionState`, and the whole worksite is a pure
  function of it, so `CemeteryGravediggingController.Restore` rebuilds any
  stage exactly on every city build. The quest log keeps only the two states
  it has ever had: `Active` on accept, `Completed` on the third act. Digging
  the hole no longer finishes anything.
- **Act one leaves a light beside the hole.** The hole is unchanged; what is
  new is the lamp standing on the collar at the head-end right corner, `24°`
  off the grave's axis, on `CemeteryGravediggingPlan.LampGround` — half a
  collar thickness out on both axes, with `ValidateOrThrow` proving it lands
  on the worksite rather than over the void, and a test proving the whole
  `0.14 m` fixture clears the mouth even turned. It is not a lamp of its own:
  `CityLakeWorldBuilder.BuildPierHeadLamp` was extracted wholesale into
  `CityHandLampWorldBuilder` and both places now call it, so the fisherman's
  pier lamp and the gravedigger's are one fixture and cannot drift. That
  brought its registry decisions along unchanged — the site registry's
  day-floor overload (`46`/`16`) so it burns around the clock, and the glass
  deliberately OUT of the glow registry, which would otherwise dim the lamp to
  a tenth by day while it went on throwing light. The extraction is
  mechanical: same object names, same order, same values, and the lake suites
  stayed green.
- **Act two is a real model.** `CityCemeteryCoffinWorldBuilder` builds the
  six-sided домовина: four flank boards turned to their own segment of the
  outline (`0.40 m` at the feet, `0.62 m` at the shoulders, `0.46 m` at the
  head), end boards, an overhanging lid of a centre plank and four wings on
  the same segments, and an Orthodox cross laid on it. `1.95 x 0.75 x 0.44 m`
  inside a `2.30 x 1.05 x 1.60 m` hole, colliderless under the mouth cap.
- **Act three closes it.** `CityCemeteryGroundExcavation.Fill` removes the cut
  and rebuilds the slab whole — idempotent, because the work is restored from
  a stage and not from a list of holes. The pit dressing and the coffin go
  with it and `CityCemeterySealedGraveWorldBuilder` puts up a fresh mound and
  a stone. The stone is not a new silhouette: `CityCemeteryPlanner` grew a
  public `CreateGraveParts`, and the batching loop of `CityCemeteryWorldBuilder`
  became `BuildPartBatches`, so a grave the hero digs goes through the same
  path as the standing rows. The `GraveSlab` part is dropped — a slab is what
  a family lays years later — and the plot's own FNV-1a hash fixes which of
  the four single-grave silhouettes it wears and whether it is dark granite or
  light marble.
- **The mound was a step pyramid first.** Three centred courses read exactly
  as the pit builder's own comment warns; the fix was per-course yaw
  (`0/-8/+11°`) and offsets an order of magnitude larger, with scales stated
  along and across separately so the heap stays a ridge over the body. Caught
  by a headless render, not by a test.
- **The lamp is picked up with the last spadeful.** `SealGrave` destroys it
  along with the pit dressing and the coffin, and `Restore` stands it only for
  `Dug` and `Coffined`. A closed grave needs nothing lit over it, and leaving
  a lamp burning on finished work made the plot read as still open.
- **The wage.** `GameSessionState.TryEarnCash` is the only way cash goes up
  outside a new game, logged like every other economy move. A finished grave
  pays `150` on the next ordinary talk interaction at the watchman's window,
  once.
- **Telling the player about the money at all.** Two gaps, both real: nothing
  pointed him back to the gate, and the wage was announced by a line with no
  number in it — cash lives only on the inventory screen and the two shop
  panels, all of them modal. The sealing line now ends with "теперь — к
  сторожу за расчётом", and `InteractionPromptView` grew a formatted-feedback
  path: `ShowFormattedFeedback`/`ShowFormattedFeedbackAt` hold arguments
  beside the key and `GetDisplayedTextAt` composes them at the view's single
  text-resolution point. The key stays a key, so everything reading
  `PromptKey` still gets one; the catalog keeps the wording around the number
  (`«Держи. Заработал.»   +${0}`), which is the project's twelve-site
  `string.Format(Get(key), value)` convention rather than a new one. A catalog
  invariant now pins both the placeholder and the `$`.

**Verification.** Final run `CemeteryGravediggingTests`,
`InteractionPromptViewTests`, `LocalizationCatalogTests`, `CityLakePlannerTests`,
`LakeFishermanTests`, `CemeteryWatchmanTests` — 41/41. Four headless renders
confirmed the lamp, the coffin, the closed grave and that the lamp is gone from
it, and drove the mound rework. IMGUI cannot be captured headlessly, so the
wage line is proved by `FormattedFeedback_KeepsTheKeyAndComposesTheValue`
instead of by eye. Not run: the full EditMode suite, PlayMode and a player
build.

## 2026-08-19 — The yard counts its empty places and the first grave is dug

- **The cemetery is divided into burial plots.** `CityCemeteryPlan.Plots`
  partitions the whole dressed interior at the grave pitch into `Occupied` /
  `Vacant` / `Obstructed`. Default city: `168` plots on a `14 x 12` lattice
  over `74 x 52 m` — `46` occupied, `60` vacant, `62` not burial ground
  (`58` of those are the alleys and their margin, three the vegetation, one
  the lodge). Plot geometry comes from `GraveDetailSalt` alone, so an empty
  plot already knows where its future monument stands; the accept hash only
  picks which clear cells are occupied today. Because that hash is now read
  after the geometry test rather than before it, the standing graves are
  unchanged: the SHA-256 of all `479` parts and `7` lamps matched the
  pre-change build exactly.
- **A vacant plot is a promise, and `MarkObstructedPlots` keeps it.** Trees
  and bushes are planned around standing monuments only, so the pass runs
  last and demotes any vacant plot the vegetation landed on; `ValidatePlots`
  then throws if anything at grave height still overlaps one.
- **The watchman has work to give.** `QuestId.DigTheGrave`. Talking to him
  puts the offer up in place of the prompt — `E` takes it, `Q` refuses, and a
  refusal costs nothing because the hole still needs digging. Taking it marks
  out the nearest vacant plot to his own post with a pulsing plate and four
  pegs; interacting with that digs the grave and completes the quest. State
  lives in the quest log, so `CemeteryGravediggingController.Restore` puts the
  marker or the finished hole back on every city build.
- **The hole is a real hole.** The cemetery ground is not the continuous
  terrain skin — `UsesContinuousTop` is false for `CemeteryGround`, so it is a
  solid slab of boxes from the terrain floor to the soil top. Digging
  subtracts the rectangle from `CreateSurfacePatches` and rebuilds the slab
  (`CityCemeteryGroundExcavation`), which leaves a genuine rectangular void;
  `CityCemeteryPitWorldBuilder` over-cuts by the collar thickness and refills
  the ring with box-projected soil, so the walls carry true UVs instead of the
  slab's planar ones smeared down a vertical face. An invisible cap over the
  mouth keeps the hero out of a `1.6 m` pit he has no jump or climb to leave;
  it is named and commented for removal the day he can climb.
- **The twelve standing test failures were the tests.** Five ran against the
  legacy blueprint for features only the shipped one has; three were stale
  invariants (the driver's gaze eases back over the closing beat, a toilet
  flush is `2.6 s`, the river surfaces cells no area declares); two compared
  geometry more tightly than it can be computed (a 3D tangent on a graded
  street reads a `1.5 m` lane offset as `1.4989`; `Mathf.Approximately` is a
  `1e-6` relative test and a sidewalk lands two microns off a metre); two ate
  the tin the stairwell cat has first claim on. One was the product:
  `DayNightVisualSample.IsVisuallyEquivalentTo` compared quaternions bitwise,
  so the first minute of dusk — identical in every colour, intensity and
  factor — read as a change because `Quaternion.Slerp` renormalises. It now
  compares within a hundredth of a degree. `CityBlueprintBuilder.From` gained
  an id overload so a clone keeps its river and its area requirements.
- **The pedestrian graph was starved, not over-pruned.** `FindTwoCore` is
  deliberate and asserted, but it peels iteratively, so a single unjoined
  pavement end unravels the street behind it. Two junction gaps left `70`
  loose ends and cost `59%` of the graph — `880` of `2137` nodes, `138` of
  `210` streets, and every signature stair. The `21` cul-de-sacs now close
  across the head of the street (`TryConnectDeadEndCap`), and a junction
  builds its mouth for any leg with no pavement rather than only for legs with
  no road at all, which covers the `14` park-path legs. Loose ends fell to
  `4`; every street keeps its pavement and all four stairs are walkable on all
  three seeds checked. The cul-de-sac lane also stopped overshooting the head
  by `3.65 m` into ground whose height cannot be sampled. This matters beyond
  the NPCs: the hero's own walkable area is built from this graph.
- **The cat's tin is borrowed, not taken.** The requirement left the inventory
  before the feeding animation and was only committed at its loop phase, and
  an abort in between — walking out of the stairwell, the cat failing to
  start — spent it with the quest still active. Since the shop sells only
  closed tins and nothing opens them, that stranded the reservation for good.
  `CloseInternal` now refunds anything not yet committed, and
  `CommitRequirement` spends it at the instant the cat has its head in the
  tin, which is the same instant the quest closes.
- **Verification.** Full EditMode `1257/1257` under `6000.5.5f1`, including
  four new cemetery-plot and gravedigging contracts, four for the tin, and one
  that walks the reservation across the cat quest's whole life. The dug grave
  was also eyeballed through a headless `Camera.Render` capture. After the
  `6000.5.9f1` upgrade only the two assemblies were recompiled; the suite has
  not been re-run on the new editor.
- **Unity `6000.5.9f1`.** The upgrade re-serialized nothing: only
  `ProjectVersion.txt`, `ProjectAuditorSettings.asset` and the package lock
  (`collab-proxy` `2.13.6`, `burst` `1.8.30`) moved. URP stays `17.5.0`.

## 2026-08-19 — And the boards start playing back

- Sitting on either free park plank now starts a real game on that table.
  Full legal chess on the chess player's board, full Russian draughts on the
  neighbour's, both against an engine that is deliberately not very good, and
  both from a seated first-person pose over the hero's own eyes.
- **The rules are pure and they are checked by counting.**
  `Assets/Scripts/Runtime/Games/` holds `ChessRules`/`ChessEngine`/`ChessMatch`
  and `DraughtsRules`/`DraughtsEngine`/`DraughtsMatch` with no Unity in them
  beyond the lattice constant they borrow. Chess is pinned by perft against
  the five standard positions — start to depth `4` (`197281`), Kiwipete to `3`
  (`97862`), the endgame position to `4` (`43238`), the promotion position and
  the tactical position to `3` — which covers castling both sides both
  colours, castling through check, en passant including the pin that forbids
  it, and promotion with and without capture. Draughts is pinned rule by rule:
  compulsory capture, backward capture by a man, maximal chains, flying kings
  and their choice of landing, the Turkish strike, and crowning inside a chain
  continuing as a king.
- **The hero is dark at both boards, and that was not a choice.** The free
  plank at each table is the one whose near ranks the drawn set already filled
  with dark men, so the man opposite is White at his own board and opens. The
  live board's opening position is asserted equal to `CityChessSetPlan`'s
  drawn placement, piece for piece, so the game starts as the set stood.
- **One mirror, in one place.** The board was drawn with its `a` file at
  lattice file `7`. The chess engine underneath is written in ordinary chess
  coordinates and `ChessMatch.MirrorFile` is the only conversion; draughts
  needs none, because a draughts board is symmetric across the files.
  Under-promotion is not offered — a pawn that gets there is a queen.
- **The seated camera has almost no slack, and every number in it was found
  by rendering rather than by reasoning.** The eye ends at `1.06 m` over the
  plank and `0.34 m` forward along its facing, `72` degrees, with a look band
  of `-6..75` and `+-55` of yaw — a man leaning right over the stone. Three
  passes of renders walked it out from the head bone, and the far end is
  bounded too: at `1.12 m` and `0.38 m` the pieces flatten and the man
  opposite thins to a sliver, and the shot stops being a park. Four things
  were wrong before a capture said so, and each is now a constant with the
  render behind it in its own comment: the eye sat exactly on the head bone
  (lens inside the skull); the look band reached `-25` up, which showed the
  tops of the park trees, because the man opposite sits `2.2 m` away with his
  head *below* the hero's eye line; at `0.06 m` forward the hero's own chest
  was a flat wall across the bottom third of the frame; and at `68` degrees
  the near corners of the board were `2%` inside a `16:9` frame, which is a
  coincidence rather than a margin.
- **The pitch is derived, not authored, and it had to be.** The near edge of a
  board this close subtends far more angle than the far one, so the angle that
  actually centres the field is nowhere near the line to the board's middle —
  at the final eye it is `51.3` degrees against the `30` first authored. It is now
  the bisector of the near and far edge angles, computed from the eye
  constants, so moving the eye can never leave the field hanging off one end
  of the frame. `CityBoardGameTests` asserts the near edge sits as far below
  the axis as the far edge sits above it, and separately that the outer
  corners of the corner squares are in frame at `16:9`.
- **The head is not a mesh, and that is why the first attempt failed.** The
  seated view hid the two anatomical parts called Head and Neck and left the
  player looking at the inside of his own hair, ears, nose, stubble, brows,
  eyes, pupils and mouth: twenty-two meshes in all, most of them on `face.*`
  bones. `Player3DHeadVisibility` now states the rule against the rig —
  anything weighted to `head`, `neck` or `face.*` — and
  `Player3DHeadVisibilityTests` instantiates the production prefab and asserts
  every part lands on the right side of the collar, that the torso, arms and
  jacket survive, that everything comes back on restore, and that a renderer
  somebody else had already switched off is left alone. Nothing below the
  collar is ever hidden: at the resting angle the body falls under the bottom
  of the frame, and looking down brings his own arms back over the stone.
- **The board has no HUD, and that is the design rather than an omission.**
  The panel that used to sit over the board naming the game, the turn, check
  and the ending is gone; all of it is said by the man opposite instead, as
  ten cues of two lines each — greet, your move, check, crown, take, refused
  pick, win, lose, draw and the offer of another game. Ordinary lines are
  dropped while one is still on screen, the result and the offer are forced
  through, and the offer follows the result by four seconds so the two read as
  two sentences. `CityParkQuarrelController.SetSuppressed` stops the quarrel
  for the sitting, because there is one bubble anchor over that set and their
  argument would have wiped out every word about the position.
  `CityBoardGameTests` walks the cue enum per game and asserts each line
  exists and fits the bubble's two rows of `48` characters, so a cue can never
  be added without a line.
- Live men replace that table's two static batches only — one object per man
  on the same seven meshes, the same timber sheet and the same lattice — and
  every move is carried a step at a time so a draughts chain reads as a chain.
  The board is re-synced from the rules at the end of every move, so a
  presentation bug costs one frame rather than a corrupt game.
- Picking is a plane read rather than sixty-four colliders, which also picks
  the square under a man as readily as an empty one. `E` still stands the hero
  up, so confirm is `Space` / west button and cancel is `Backspace`; `R`
  restarts a finished game. Two new synthesized effects (`BoardPiecePlace`,
  `BoardPieceTake`) and twenty localized lines the old man aims at the hero
  rather than at his neighbour.
- Verification: `ChessRulesTests`, `DraughtsRulesTests`, `BoardGameEngineTests`,
  `CityBoardGameTests`, `LocalizationCatalogTests` and `RetroSfxLibraryTests` —
  `88/89`. The one failure is pre-existing and unrelated:
  `RetroSfxLibraryTests` asserts every effect duration is inside `0.04..0.5 s`
  and `ToiletFlush` has been `2.6 s` since it shipped. Two throwaway edit-mode
  renders were taken to check the seated framing and deleted afterwards. Not
  run: PlayMode, a player build, and the rest of the EditMode suite.

## 2026-08-18 — And the hero can sit down at either board

- The prompt on the two free park game planks now names the game rather than
  offering a sit (`interaction.play_chess` / `interaction.play_checkers`), and
  taking one seats the hero across the board from its old man, facing him.
- **The old failure was a dock inside a table.** `CityBenchSitPlan` put every
  entry dock in front of the seat, on the side the sitter faces, because that
  is where you back onto an ordinary plank from. A game plank faces its own
  table, so the dock landed under the slab and the approach stalled against
  it: `Animated interaction entry was blocked; current=(-37.74, 4.77, -35.20),
  target=(-36.63, 4.71, -35.20)`. Seats now carry a `CityBenchSeatKind`, and a
  game seat docks off the plank end on the sitter's right instead, keeping the
  seated facing.
- **There is exactly one lane onto one of these planks, and it is narrower
  than it looks.** The collision proxy is one box per table covering slab,
  pedestal and both benches (`1.50 x 2.70`), so a body cannot stand behind a
  plank or between plank and table at all. The dock therefore stands
  `0.56 + 0.66 m` off the seat centre, clearing the block by `0.47 m` against
  a `0.36 m` capsule, and `BuildApproachWaypoints` joins that end lane behind
  the plank or across the board depending on which side of the set the walk
  starts. `CityChessBoardGeometry` grew the bench and block extents so the
  proxy, the drawn timber and the route now read the same numbers.
- New `ChessSeatEnter`/`ChessSeatPlayLoop`/`ChessSeatExit` (`chess_seat`,
  `3 s` / `4 s` / `3 s`), authored on top of the bus seat and validated by the
  same full-rig seam checker, which was generalised from `validate_bus_ride_
  pose` to take a clip family. The pelvis path perches on the plank end at
  `0.52` and departs at `0.72`, so the clip sits on the corner and then slides
  along the timber.
- **Authoring these poses blind produced a forward stride, not a side entry.**
  A single-axis Blender probe fixed it in one run: on this rig a negative
  bone-local `z` on either thigh abducts that leg towards `+X`, and a negative
  `y` on the pelvis turns the body the same way — both opposite to what the
  existing `L +z / R -z` splay pairs suggest. Every lateral key had the wrong
  sign. Beats were then replayed against a probe plank and slab under the real
  runtime pelvis path rather than eyeballed on a floor.
- **Moving the dock exposed a second copy of where the dock is.**
  `ResolveSeatDockGround` resampled the walkable surface under its own
  front-offset guess, so a side-docked seat would have been grounded at a
  point the walk never reaches — the same class of bug as the original, one
  step downstream. Both now call `CityBenchSitPlan.GetDockOffset`, and
  `CityBenchRestTests` was pointed at it rather than repeating the formula a
  third time; that test caught the drift on the first run.
- The game trigger leans its depth back onto the lawn rather than sitting
  centred on the plank, so the volume covers the lane the walk comes down
  without reaching across the table into the plank opposite and offering the
  wrong game.
- Verification: `tools/build-player-3d-model.py` (29 Actions, seams and
  fixed-root checks pass) and one EditMode selection —
  `CityChessSeatSitTests`, `Player3DAssetImportTests`,
  `LocalizationCatalogTests`, `ParkChessPlayerTests`,
  `ParkCheckersPlayerTests`, `CityBenchRestTests`, `CityCemeteryPlannerTests`,
  `CityPlaygroundSwingTests` — 40/40. No PlayMode run and no player build.

## 2026-08-18 — Both boards get their men

- Authored `tools/build-city-chess-set-3d-model.py`: six turned chess pieces
  and a draught, `1910` triangles for the lot, every dimension a fraction of
  the drawn `0.15 m` square. The runtime places `56` of them across the two
  park boards as four combined meshes.
- **The board's colouring was wrong and it took men on it to notice.** Both
  planks face along the recipe's `+Forward`, and `Tangent = (-Forward.z, 0,
  Forward.x)` runs to the left of anyone facing that way, so each man's
  near-right corner is `(0,0)` or `(7,7)`. Both must be light and both were
  dark. The dark parity is now the odd one, which also puts `a1` dark and the
  white queen on a light `d1`. `CityChessBoardGeometry` owns that rule and the
  five numbers the men and the lattice both need; the recipe aliases them, so
  a set can never end up half a square off its own squares.
- **This overrides a documented art rule and the rule was right to exist.**
  `ai/city-zones-art-bible.md` §10 banned pieces on the boards, on the sound
  reasoning that a position mid-game implies somebody opposite is moving.
  Both sets are therefore laid out in the *starting* position and nothing in
  the runtime ever moves one. That reads as a sharper version of the same
  idea rather than a softer one: the empty plank opposite is no longer a
  table nobody plays at, it is a game nobody started. The ban is replaced in
  the bible by what it was actually protecting — no other position, no
  captured men beside the board, no clock, no scoresheet, and no draughts
  king, since a king means a game was played.
- The knight is the whole reason this is an FBX rather than boxes. The first
  attempt stacked five rotated boxes and rendered as a flag on a pole; the
  second draws the head and neck as one closed profile — chest, throat, jaw,
  nose, the stop under the brow, forehead, poll, crest, mane — and extrudes
  it across four slices with the outer two scaled `0.88` so it is a solid
  rather than a card. Two ears set the height. A horse is a line, and a line
  has to be drawn rather than stacked.
- Three exporter defaults had to be beaten, and all three hide behind the
  imported hierarchy that every other model in this project instantiates:
  `apply_scale_options="FBX_SCALE_ALL"` keeps the metre-to-FBX factor out of
  a `scale = 100` root, `bake_space_transform=True` bakes Z-up-to-Y-up into
  the vertices instead of onto that root's rotation, and `isReadable = true`
  on the importer, because `Mesh.CombineMeshes` reads vertices at runtime and
  an unreadable source combines into nothing — silently, in a player build,
  on a board that simply comes up empty. Without the first two the meshes
  arrive a hundredth of their size and lying on their backs while the model
  preview still looks perfect.
- `RuntimePrimitiveFactory` grew `CreateCombinedMeshes`: the same world-UV
  and batching contract the box helpers have, for authored meshes. It is the
  first geometry in this city that is not a box.
- The men take the board's own timber sheet, pushed apart from the two square
  tints — brighter than the light inlay, darker than the dark one — and
  unlike the flat board batch they cast and receive shadows, which is what
  separates a light man standing on a light square.
- Verification: `blender --background --factory-startup --python
  tools/build-city-chess-set-3d-model.py` (the validator reports every
  contract breach at once, since tuning a turning is a loop); `dotnet build`
  on the Editor project; `Unity.exe -batchmode -executeMethod
  CityChessSetAssetSetup.Run`; EditMode selection
  `CityChessSetTests|CityChessTableGeometryTests|ParkChessPlayerTests|ParkCheckersPlayerTests|CityParkSurfaceAppearanceTests`
  — 57/57. Then a throwaway edit-mode capture of the real generated city from
  the empty plank opposite each man, which is the one place the player is
  invited to sit: the back rank reads rook-knight-bishop-king-queen-bishop-
  knight-rook left to right from that side, the queens are on their own
  colours, every draught is on a dark square, and neither old man's forearms
  pass through his own back rank.

## 2026-08-18 — The two of them cannot stand each other

- Gave the park chess set its argument. `CityParkQuarrelController` polls the
  hero against the middle of the set (`22 m` in, `25 m` out — the gap is
  hysteresis, or a hero standing on the line resets the turn every other
  frame), ticks `ParkQuarrelTimeline` and, on each cue, calls `BeginTaunt` on
  whichever presentation holds the turn. Modelled on
  `CityCemeteryMournerController`: the two staged prefabs stay passive.
- **The turn is five seconds and strictly alternating.** The opener is drawn
  from the city seed rather than fixed. A hitch long enough to owe several
  shouts forfeits them instead of firing a backlog — two men screaming over
  each other on the frame the game unfreezes is not the scene — while an
  ordinary frame's overshoot is carried so the cadence does not drift a frame
  later every turn.
- **Two new authored clips rather than a procedural bone overlay.** The
  library already keeps every stationary NPC beat as a baked clip validated
  on posture and loop closure, and `CityPedestrianAssetRegistry` exposes only
  `Head`, `Pelvis` and the two feet — an overlay would have had to find
  `neck` and `upper_arm.L` by name and would have left the shout out of the
  manifest, out of the perch validator and out of review renders. The four
  existing procedural exceptions are all *continuous* additions to a pose,
  not a beat with its own silhouette.
- **The two men share one unmirrored pose, and that is a fact about the
  set.** The chess seat is at local `(-1.85, -1.10)` facing `+Forward`, the
  draughts seat at `(+1.85, +1.10)` facing `-Forward`, and
  `Tangent = (-Forward.z, 0, Forward.x)` points to the left of anybody facing
  `+Forward`. Project the separation onto each man's own frame and both get
  the same answer: the neighbour is `2.2 m` ahead and `3.7 m` to his **left**.
  So both turn left, both throw the left arm, and `checkers_player_base_pose`
  already returns the chess player's body. Two clip names remain necessary
  only because clips are keyed by name and handed to a design by `design_id`.
- The head turn sums to `64°` across chest, neck and head, short of the `73°`
  that would put his eyes on the neighbour: an old neck does not go there and
  the read is the throw, not the eyeline. `+Y` is that left turn — the same
  axis the watchman's head shake keys.
- The arm was authored against measured axis semantics rather than by eye. A
  throwaway Blender probe rendered single-axis rotations off the rest pose
  and settled it: on `upper_arm.L`, `+X` raises the arm from the rest T and
  `-Z` swings it forward. The first attempt, written by perturbing the fitted
  fold, left the arm across the chest; the review render caught it.
- **The bubble opens on the clip's own phase**, not on a second timer — the
  fisherman's rule. The presentation exposes `TauntPhase`, the controller
  runs at `[DefaultExecutionOrder(320)]` so it reads this frame's value, and
  the line appears the frame the phase crosses `0.22`, which is the authored
  full-extension key. Re-timing the clip re-times both.
- `NpcSpeechBubbleView` is IMGUI on the shared `640x360` canvas at
  `GUI.depth = -75` — above the intoxication HUD, below the interaction
  prompt, the map and the pause menu. Not uGUI/TMP: that would have been the
  project's first Canvas, needed an asmdef reference and a committed Cyrillic
  font atlas. Not world-space either: the PS1 composite pass crushes the
  frame to `640x360` and RGB555 *before* UI is drawn, so a panel in the world
  would be unreadable while this one stays sharp. The panel is measured once
  from the whole line and only the drawn substring grows, or `CalcHeight`
  would jump the box a row taller mid-word.
- `CityPedestrianAssetRegistry` gained an optional `ActionClip` slot and
  `CityPedestrianAssetSetup` an optional `actionClipName`/`actionDuration`,
  copying the existing `sitClipName` shape. Every other descriptor compiles
  and validates unchanged; a design that declares no beat must not carry a
  clip in the slot.
- Repaired a pre-existing failure while here:
  `CityPedestrianRuntimeTests.ProductionPrefabs_UseCustomLocomotionAndGroundedWalk`
  still expected `26` locomotion clips and named none of the park pair's, so
  it had been red since the two men were added. Its list and
  `StagedLocomotionClipCount` now cover all six park clips (`32` total).
- Verification: `blender --background --python
  tools/build-city-pedestrian-3d-model.py` — `32` actions, both jeers perch
  at `0.5354-0.5392 m` inside the declared `(0.53, 0.55)` band with
  `loop_max_error 1e-06`; `dotnet build` on `BarPromenade.EditModeTests` and
  `BarPromenade.Editor` (0 errors); `Unity.exe -batchmode -executeMethod
  CityPedestrianAssetSetup.Run` to rebind both staged prefabs; EditMode
  selection `ParkQuarrelTests|CityPedestrianRuntimeTests|ParkChessPlayerTests|ParkCheckersPlayerTests`
  — 46/46. **Not run, and not runnable here:** the bubble itself. Batch mode
  has no game view, so IMGUI cannot be captured; the panel, its backdrop and
  the typing need a play-mode look.

## 2026-08-18 — And somebody at the table next to him

- Authored `park_checkers_player_v1`, the second man at the park chess set,
  and seated him on `-seat-b2`. Of the four planks the recipe draws that is
  the only one that is both at the other table and on the far side of it, so
  the two men are turned toward one another with the whole set between them.
  `-seat-b1` is the other table but the same side and the same direction,
  which would seat them shoulder to shoulder; `-seat-a2` is the seat across
  the old man's own board, which §10 forbids by name.
- **The pair intensifies §10 rather than spending it.** Their two facings are
  antiparallel and offset by the `3.70 m` between the tables, so each sits
  about 59 degrees off the other's axis - in front of him, plainly, but never
  looked at, and both are folded over their own boards anyway. The two
  remaining planks stay unclaimed and sittable. Two old men turned to face
  each other across four metres of lawn, each with an empty seat opposite,
  not even playing the same game.
- **Both channels had to be re-derived from the piece rather than the field.**
  Chess and draughts share one board, so a check on the second man would say
  "board" twice and separate nothing. The silhouette wears one thick draught
  worn flat over a dark band; the cloth answers squares with circles run on
  the diagonal, because that is the only line a draught travels. The circle is
  smaller than the neighbour's square at the same pitch, and that is the one
  place the two patterns cannot be built alike - squares on a lattice tile and
  may touch, circles of the same size fuse into a chain of blobs, which the
  first review render showed exactly.
- **The rake of the draught is a measurement.** `CANONICAL_HEIGHT` is enforced
  to ten microns, so a cap that merely sits lower than the neighbour's crown is
  not available: a flat piece resting on a skull that stops at `1.640` cannot
  reach `1.750`. It is therefore worn raked, at the one angle whose raised
  edge lands on the ceiling. Where the king's cross takes the envelope standing
  straight up, the draught takes it lying down, and the silhouette inverts on
  axis and width instead of on height.
- **What sets the radius is the face, not the read.** A bench sitter's head is
  below the player's eye, so the player always looks down onto this piece, and
  at `0.39 m` across it curtained the whole face from the only angle the game
  offers. The radius governs that far more than the rake does - the rake lifts
  the near edge, the radius decides how far it reaches out over the brow in the
  first place. At `0.30 m` and 40 degrees the leading edge sits 29 mm forward of
  the brow and 147 mm above it, and the face is open from every standing
  approach. It is still 2.6 times the width of the crown's band next door.
- **Everything below the neck is the chess player's geometry to the
  millimetre**, which is a requirement rather than a saving: his six arm angles
  are a coordinate-descent solve against an elbow on a board at `0.90` and a
  palm under a cheek, and both tables draw the same board over the same plank,
  so the solve transfers only while the coat, hips, legs and skull stay
  identical. The build proved it rather than assuming it - the perch validator
  reports the same `0.5388-0.5396 m` band, the same `0.0651 m` pelvis lift and
  the same `GEO_BootSole.L` contact as its neighbour, on all 288 frames. Had
  any of those moved, some part had been copied nearly rather than exactly.
- **Own clips, not shared ones, and that is forced.** Actions are handed to a
  design by `design_id` and `ACTION_BY_NAME` is keyed on the clip name alone,
  so reusing `ChessBrood` would either leave the archetype with nothing baked
  or overwrite the neighbour's entry. `CheckersMull` therefore earns its keep:
  a shallower breath and the settle at a different point in the lap, so two men
  under one lamp never rise and fall together, and the perch band is proved
  against his own meshes rather than somebody else's.
- The presentation and factory are deliberate near-copies rather than a shared
  base. Every staged character owns its own passive quartet, `CityGameRoot`
  types its property on the concrete presentation, and the two constants worth
  sharing must not be: `PerchPelvisLiftMeters` and `FocusHeightMeters` are
  measurements off one design's meshes and one design's pose, and a shared one
  that drifted would fail no test - it would merely sink a man into his bench.
  A third bench sitter is where the extraction earns itself.
- Two generator faults the second design exposed and fixed: `render_preview`
  hard-coded `chess_player_base_pose()` for any archetype declaring
  `perch_seat_height_m`, so a second perched design would silently inherit the
  wrong stance - it now dispatches per archetype; and the review camera was
  first mirrored to the model's other side, which put it square in front of the
  key light, flattened every value and made a design meant to be judged beside
  the chess player impossible to compare with him. It now uses his camera.
- No lamp work. `Lamp_BurnsOverTheMiddleOfTheSetAndNowhereElse` already pinned
  both boards inside the lit circle, and a second burning lamp on that wire is
  forbidden by §10; a new test pins that the second man is lit by the existing
  one so no later move of the wire can light one and not the other.

Verification:

- The full deterministic art build across thirteen archetypes: repeated
  signatures match, `park_checkers_player_v1` at 48 meshes and 1260 triangles
  inside `(900, 2200)`, the `1.750` envelope exact, `perched CheckersMull:
  seat 0.5388-0.5396 m over the soles, pelvis lift 0.0651 m, ground contact
  GEO_BootSole.L`, and both new clips grounded against their own footwear.
  The chess player's `build_signature` is byte-identical to HEAD and no other
  manifest changed, so the shipped man did not regress.
- `dotnet build` clean on `BarPromenade.Runtime` and
  `BarPromenade.EditModeTests`; the staged prefab and provider rebuilt and
  bound through the new menu item.
- EditMode `19/19`: the four new `ParkCheckersPlayerTests` plus
  `ParkChessPlayerTests`, `CityChessTableGeometryTests` and
  `CityBenchRestTests` as regression. The load-bearing one is
  `SeatClaims_TwoMenTakeTwoPlanksAndLeaveTwoFree` - both claims succeed,
  neither can take the other's, and `-seat-a2` and `-seat-b1` are still drawn,
  still unclaimed and still claimable by the hero.
- Review renders: the archetype preview from his neighbour's camera, and a
  temporary paired rig (scratch only, not committed) that seats both designs on
  the real seat offsets with the drawn tables, boards and benches. From the
  park approach and from the side the pair reads as a narrow vertical tower
  against a wide flat circle, both folded over their own boards, both seats
  opposite them visibly empty.
- Not run, deliberately: PlayMode, the full EditMode suite and a player build.
  The paired rig is a Blender approximation of the set, not the game's own
  lighting; the in-engine day/night look of the pair is still worth a pass.

## 2026-08-18 — Somebody is still sitting at the chess tables

- The park chess set has been drawn and sittable for a while with nobody on
  it. Authored the `park_chess_player_v1` archetype and lit the corner. He is
  an old man on the plank of one of the two tables, elbows on the board, head
  in both hands, nobody across from him.
- **The chess reference is carried twice, because §3.2 forbids letting colour
  carry a read on its own.** The silhouette wears a king's tulle where a hat
  would be - the direct continuation of Lampshade/Kettle Hat/Helmet Lamp - and
  the cloth carries a check on the scarf tails and both lapels. Neither is
  white: the light square is the park's cold bone at `0.615`, deliberately
  near the fisherman's corrected `0.455` rather than near 1, because he sits
  directly under the one burning lamp and that is exactly where the slicker
  clipped.
- **He is the library's first bench sitter, and neither existing grounding
  rule fits.** Sole-pinning would drag him down until he stood on the lawn;
  the bus cabin's `seated_clearance_m` measures headroom against a roof a park
  bench does not have. So the generator grew `perch_seat_height_m`: the
  distance from the underside of his hips - the part that rests on timber - to
  his soles, which has to equal the height of the drawn plank. This one is
  used, unlike the `perch_clearance_m` the fisherman grew and lost. The drawn
  seat is `0.540 m`; the authored loop holds `0.5388-0.5396` across all 288
  frames.
- The legs are asymmetric on purpose and it is a fix, not a flourish. The
  shared rig is asymmetric (`toe.L -0.230`, `toe.R -0.188`), so identical
  angles land the soles `32 mm` apart. One foot plants flat and the other is
  drawn back onto its toe: the validator now reports which part actually
  touches, and it is `GEO_BootSole.L` on every frame, with the right toe
  `3.7 mm` clear.
- **The arms are solved, not posed**, the way the fisherman's were. Elbow on
  the board and palm under the cheek, by coordinate descent, to `0.1 mm` and
  `0.2 mm`; the right side is the left mirrored, because the solver will
  happily find an equally exact but visibly different wrist roll. Where the
  elbow lands is *derived*: a seated shoulder is already at its ceiling in the
  A-pose rest and every degree of lean lowers it, so there is exactly one
  forward distance at which an elbow reaches the board without shortening a
  `0.2869 m` upper arm, and it puts them on the squares rather than on the
  slab edge.
- The breath rule is the fisherman's inverted. His hands held a rod, so his
  breath could move the neck and head; this design's hands hold his head, so
  the breath moves the spine and chest **only** and carries skull and both
  palms as one piece. Keying the neck would slide his face out of his hands
  once a lap. The settle is small for a second reason he never had: his elbows
  rest on a fixed board, and every degree at the chest slides them about five
  millimetres across it.
- **The wire runs across the set, not along it, and that is a measurement.**
  The park plants trees on an 8x8 grid and the decoration planner then keeps
  the set `4.8 m` clear of every trunk, which drops it into a gap between tree
  rows: along the line of the two tables the nearest trunks stand about five
  metres off-axis, so a wire between them would pass beside the set and hang
  its lamp over grass. Across the set the same field offers a pair almost
  exactly on the line (`0.81 m` and `0.37 m` off). So the wire crosses between
  the two boards and its one working lamp covers both, which is the wider
  circle the fixture was chosen for. The knot takes the trunk face nearest the
  set, which pulls the lamp a further `0.26 m` toward the middle.
- Two pendants, one bulb. The dead one is a bare socket in an identical shade
  further down the wire. The lens uses the boat station's `1.5x` multiplier
  rather than the usual `4.6x` - blue passes 1 at `4.6x` and warm glass turns
  into a white chip - and stays out of `CityNightGlowRegistry`, which would
  read dead at noon on a fixture that never switches off.
- Three renders caught what no test could: he was folded so far forward that
  the crown lay on its side and the face pointed at the grass (the neck now
  counters the lean, leaving the crown `22 deg` off vertical); the cross on
  the crown was `24 mm` and disappeared into one downsampled pixel; and the
  palms sat in front of the mouth rather than at the cheeks, which reads as
  covering a face rather than propping a head.
- **Worth remembering about edit-mode capture:** the review path builds no
  Global Volume and no post-processing, so there is no bloom, no ACES and no
  `+0.62` exposure. Raising this lamp from `40` to `58` changed the captured
  image not at all. Practicals cannot be tuned against that image; the value
  landed at `52 / 14` over `10 m` by sitting it between the documented pier
  hand lamp (`46 / 16` over `11 m`) and the door bulb (`64-110` over `7-8 m`).
- He claims his seat through `CityBenchSeatClaims` for the life of the City,
  so the hero's prompt leaves that plank and `CityBenchNpcRestController`
  never lowers a walker onto him. The factory is raised after
  `CityBenchSitPlan.CreateAll` and before the rest controller for that reason.
  The talk stub is deliberately not built: the constants and the dock offset
  are in place for a later pass.
- Verification: the full deterministic art build (12 archetypes, repeated
  signatures matching, every clip grounded against its own footwear, the new
  perch band proved on all 288 frames), `ParkChessPlayerTests`,
  `CityChessTableGeometryTests` and `CityBenchRestTests` - 14/14 - plus
  edit-mode renders of the set from the park approach, from the side, close on
  his hands and wide, at day and night. No PlayMode suite and no player build.
- Noted but not fixed, as out of scope: `CityPedestrianAssetSetup.BuildOrThrow`
  binds the babushka, attendant, mourner, watchman and now the chess player,
  but not the lake fisherman. His provider is bound only by his own menu item.

## 2026-08-18 — The open precincts join the teleport map

- Follow-up to the precinct drawing pass: the new cells were drawn and named
  but still could not be clicked. The cause is structural, not cosmetic - every
  clickable thing on the map is a `BuildingLot`, and `CityBlueprintCell`
  creates no lot for `OpenLand` or `Water`, so the lake, the cemetery, the five
  yards and the north waterfront had no hit box at all. (The central park was
  never affected: its `ParkLand` cells do create lots.)
- **One selection index space, appended not interleaved.** `MapObjects` stays
  literally `Layout.BuildingLots`, and indices at or past its count address the
  new `MapAreaTargets`. `DrawBuildings` uses its raw loop index as the
  selection index and `FindMapObjectIndex` is a ReferenceEquals scan, so any
  shift would have mis-selected lots silently, with no compile error and no
  failing test. Each target carries its own `SelectionIndex`, so the view never
  reverse-maps a region back to an index.
- **The arrival point is the precinct's own gate, stepped a stride in.** The
  access centre itself sits on the seam between the street rect and the
  precinct ground, and `RoadWalkableArea.Contains` tests one rectangle at a
  time, so the seam fails at the `0.35 m` agent radius; `1.5 m` along
  `OutwardNormal` lands deep inside the access cell's single mask rect and
  still inside `ApproachBounds`. `OutwardNormal` is used unnegated - it already
  points from the street into the area.
- **Height comes from the drawn terrain, not the road datum.** The lot path
  samples `CitySurfaceRole.RoadTop`, which can never resolve `5.5 m` from a
  centreline and silently falls back to a flat road offset - metres wrong on
  the terraced yards and the bilinear beach. The precinct path uses
  `CityTerrainSurfacePlan.TrySampleGroundTop`, the sampler the terrain itself
  is built from.
- **The mask still gets the last word.** `AddRiverClippedGround` subtracts every
  river segment from a cell rect, so a future access cell could be bisected.
  `ConfirmDebugTeleport` clamps the arrival with `RoadWalkableArea.ClosestPoint`
  and re-samples the height, and refuses the teleport with a logged
  `debug_teleport_unreachable` rather than dropping the player in the water.
  The mask is built lazily on the first teleport - the same one
  `CityWorldBuilder` makes - so nothing pays for it unless a teleport happens.
- The click pass is issued dead last in `DrawMap`, after the bus legend: IMGUI
  gives a press to the first control that claims it, so full-cell buttons
  cannot swallow a lot, bar, stop or landmark click. It is gated on
  `DebugTeleportEnabled`, so the ordinary route-planning map is unchanged.
- All five yards share `map.district.yard`, so the teleport panel would have
  shown five identical lines. Labels now carry the gate cell through one new
  `map.area_at` key, the way an anonymous lot carries its own.
- Focused Unity EditMode `CityMapDistrictPresentationTests` passed `33/33`,
  including new coverage that every open precinct has exactly one target whose
  arrival is inside the walkable mask at the planner's `0.35 m` radius, stands
  on sampled non-water ground at `top + GroundedRootOffset`, faces the
  unnegated access normal, and yields a distinct label. The IMGUI click itself
  cannot be exercised headlessly - batch mode has no game view - so the button
  pass was reasoned about from the first-control-wins rule, not measured. No
  PlayMode suite or build was run.

## 2026-08-18 — The map draws its precincts and names nothing

- The map had been keeping up with the city by adding a label per area. With
  the river, the lake, the cemetery and five yards in, that was thirteen
  104-px name plates over a ~290-px-wide map, five of them reading «Двор»,
  and underneath them the new precincts were flat tinted rectangles.
- **Areas are now drawn, not captioned.** Added `CityMapAreaOverlay`: a pure
  per-layout model of every blueprint area — the drawn surface rects, the
  outline of that ground taken from the surfaces themselves (so it stops at
  the kerb where the ground does), and the canonical street approaches from
  `CityLayout.OpenAreaAccesses`. The view strokes each non-urban precinct,
  marks its gates, and gives it a motif: crosses, beach sand, yard lines,
  lake reeds.
- **The lake reads as a basin.** Its water cells are painted as bank and the
  water is only the authored waterline, drawn with its corners cut, plus the
  pier and the hire hut. All three come from the `CityLakePlan` the world was
  actually built from, passed to the map by `CityGameRoot`, so the map cannot
  disagree with the ground about where the boards are.
- **Every name is now a tooltip.** The district plates, the pinned «Вы здесь»
  and the pinned «Дом» are gone. Each area cell registers a background hover
  target, so a pointer anywhere over the city names the ground it rests on,
  while markers keep answering first: `ResolveHoveredLabel` resolves the
  foreground tier and only falls back to the precinct layer when nothing
  named was hit. The nearest-marker-then-priority rule inside the foreground
  tier is unchanged.
- The bus legend no longer deletes the hover targets it overlaps — a cell is
  bigger than a marker, and deleting it blanked names well outside the
  legend. The legend now blocks the tooltip only while the pointer is on it.
- The hero is a filled arrowhead pointing where they face, laid down as
  widening rows because IMGUI fills rectangles, outlined in ink so it holds
  over pale ground.
- Focused Unity EditMode `CityMapDistrictPresentationTests` passed `32/32`,
  including new coverage that the overlay names and outlines every canonical
  precinct with gates on the open ones, and that a marker outbids the area
  beneath it. Unity compiled runtime and test assemblies for that run; no
  PlayMode suite or build was run. First attempt caught that the bare
  `CityLayoutGenerator.Generate(settings, seed)` overload builds the legacy
  city, which has none of these precincts.

## 2026-08-18 — The man on the end of the boards

- The fisherman's runtime layer had been in and green since the boat station
  landed, with one gap: no art. Authored the `lake_fisherman_v1` archetype and
  closed it. He is a hooded man in a municipal-yellow oilskin standing at the
  head of the pier, tipped out over its end board, with a rod in both hands and
  a lit pipe in his teeth.
- **Both hands are really on the rod, and that is a fitted result rather than a
  posed one.** The rod is one rigid part on one vertex group, so it rides the
  right fist and the left hand has to be brought onto the same axis. Eyeballing
  Euler angles for a cross-body reach does not converge: the arm angles were
  solved by coordinate descent against the model's own `ACC_RodGrip` and
  `ACC_RodTip`, to `0.5 mm` on the right and `4.4 mm` on the left.
- That grip then constrains what the loop may key. His breath is authored on
  the spine chain **only**: both clavicles hang off the chest, so breathing on
  the chest swings both arms and the rod together and the grip survives
  untouched, while the same breath authored on the clavicles would open his
  hands off the stick once per lap. The one rod correction in the eight seconds
  is authored the same way, on the spine and neck, for the same reason.
- **The pipe is driven by the clip, not by a timer.** `FishermanLean` is keyed
  on an exact quarter-loop breath grid — rest at every quarter, full inhale at
  every eighth between — so `frac(normalized * 4)` is the breath phase, and
  `LakeFishermanPresentation` publishes it. The ember colour, its point light
  and the plume's emission rate all read that one number; the plume lags it by
  `0.18` of a breath because the draw pulls air through the bowl before the
  smoke leaves it. Emitting in phase with the ribs was the mistake worth a
  test: smoke that swells while the chest is still filling reads as a particle
  system parented to a man.
- **Two bind-pose anchors, measured by the prefab build.** Every pedestrian
  part is a rigidly skinned mesh, so the pipe bowl and the rod point have no
  Transform to hang an ember or a line from. Reconstructing either at runtime
  would mean re-deriving the FBX axis conversion and the prefab's own 180°
  model flip in gameplay code, twice. `CityPedestrianAssetSetup` measures both
  off the imported meshes once and parents an empty to the bone that carries
  them; `LakeFishermanRigAnchors` is passive metadata in the wheelchair
  registry's pattern.
- He was authored **sitting** first, and that cost a whole grounding contract
  which is now gone again. A seated man is not sole-grounded — his backside is
  lower than his boots — and he is not cabin-seated either, so the generator
  grew `perch_clearance_m` / `ActionSpec.perched`, a bake that settled the
  whole silhouette onto `z=0` on whatever carried it, and a proof that the seat
  of the coat and both soles all reached the boards. It worked (soles at
  `0.0000` and `0.0016`, hem at `0.0026`) and it took four measured passes,
  the last of which turned up something worth keeping in mind: the shared
  Player rig is deliberately asymmetric (`toe.L` at `-0.230`, `toe.R` at
  `-0.188`), so identical left/right leg angles land the two soles `32 mm`
  apart, and no per-frame pelvis bake can level two feet at once. When the user
  asked for him standing instead, all of it came out — an unused declared
  contract is worse than no contract — and he now grounds on his own boots like
  every other walker.
- Three renders caught what no test could:
  - The wader shaft was authored from `0.560` down but rides `shin`, whose
    head is at `0.354`. Every centimetre above its own bone head swung out
    through the thigh as soon as the knee bent — a brown wedge stabbing out of
    his hip. Shafts now stop exactly at the knee.
  - The soles pointed at the sky. A positive `foot` rotation lowers the toe on
    this rig; I had assumed the opposite sign from `SEATED_LEGS`, whose shins
    happen to sit within a few degrees of vertical, where the sign does not
    show.
  - The slicker clipped to pure white next to the hand lamp. Brought down from
    `0.560` to `0.455` red — still the loudest hue any City design wears, and
    still the only saturated colour on the precinct that is worn rather than
    built.
- Also fixed a maintenance trap I fell into on the way: the model importer
  kept its own hand-written list of pedestrian FBX paths. A design added to
  `Descriptors` and forgotten there imports on default settings, builds its
  own Avatar, and fails much later with `Bone 'root' rest transform differs`.
  It now asks `CityPedestrianAssetSetup.IsDeclaredModelPath`.
- Verification: `LakeFishermanTests` and `CityPedestrianRuntimeTests`, the full
  deterministic art build (11 archetypes, repeated signatures match, every clip
  grounded against its own footwear), and edit-mode renders of the pier from
  the approach, the side, behind and the water.

## 2026-08-18 — A lamp somebody left on the rail

- Follow-up on the user's own screenshot: the head lamp had come out as a full
  municipal street post with a big white head, which is not what an abandoned
  pier should carry. Replaced with a small kerosene-warm hand lamp standing on
  the rail cap at the very end of the boards - tin foot, amber glass, a wire
  bail over the top, roughly a forearm tall.
- The change is as much semantic as visual. A post says the pier is
  maintained; a lamp left on the rail says somebody walked out here, put it
  down and did not come back, which is the same sentence the rest of the
  station is telling. §10d's Свет, Мостки, Нельзя and Проверка were rewritten
  again to match, and Нельзя now names the street post and cold white light as
  the things to avoid.
- Scaled to an object: `165 / 70` at `26 m` becomes `46 / 16` at `11 m`, so it
  lights the last stretch of deck and the water beside it and nothing further.
  It stays on the day-floor overload, so it still never switches off.
- Two things the render caught that no test could:
  - The glass clipped to pure white. The emissive multiplier is bounded by the
    *blue* channel, not by taste: at `2.4x` this colour's blue passes 1, all
    three channels saturate together, and an amber glass becomes a white chip
    while the cast light stays warm. `1.5x` puts blue at `0.63`, so red and
    green saturate into a hot core and the glass keeps its hue. The halo
    multipliers were clipping the same way and came down with it.
  - The bail read as a dark spike rather than a handle at this size; lowered
    and thinned.
- Verification: `CityLakePlannerTests`, `LakeFishermanTests`,
  `RoadWalkableAreaTests`, `CityNightGlowRegistryTests` — 14/14 — plus close
  and mid-range renders at both day and night.

## 2026-08-18 — One lamp still burning

- User decision, and it overturns something I had written into the art bible
  the same day. §10d said the pier-head lantern was dead and listed "горящий
  фонарь на конце мостков" under Нельзя, on the reasoning that an abandoned
  station keeps no working light. The three shore lamps are removed and that
  lantern becomes the station's one working, always-on fixture instead.
- The concept survives the inversion and is arguably tighter for it: the bank
  going completely dark is a stronger statement of abandonment than three
  municipal posts, and a single lamp still fed at the far end of the pier says
  "left, not dismantled". §10d's Свет, Мостки, Нельзя and Проверка were all
  rewritten to match rather than left contradicting the build.
- It also lands better technically. The water's reflection is an
  additional-light highlight, not a mirror, so it only exists where a fixture's
  own range covers the surface — the shore lamps could never reach past the
  bank. A lamp standing over the middle of the pond has the whole width of the
  water under it.
- `CityLakeLampKind.Shore` becomes `PierHead`; `CityLakePartKind.PierLantern`
  is gone, because the fixture now carries its own post and lens the way every
  other lamp does. The validator's "the shore carries two to five lamps" band
  becomes "a pier carries exactly one head lamp", and the test additionally
  pins that the lamp stands over open water — on the bank it would be a
  different fixture with a different job.
- Registered through the day-floor overload at `165 / 70` with a `26 m` range:
  "always working" has to read at noon too, not just survive the night factor.
- The lens is deliberately kept OUT of `CityNightGlowRegistry`. The day render
  caught it: the registry lerps every registered emissive down to a tenth of
  itself under a day sky, so the lamp was throwing light while its own glass
  looked dead. That is right for a fixture that switches off and wrong for one
  that does not — the always-on yard spotlight sits outside the registry for
  the same reason. The cemetery porch bulb still has the older behaviour; left
  alone as out of scope.
- Verified by render, not only by test: from the water looking back at the
  lamp it reads as a lit head with a fog halo over a dark post, a warm pool on
  the deck and the water around the pier lit; the bank behind is entirely dark.
  By day the glass reads lit. The reflection is a lit highlight rather than a
  mirror, so it lives within the fixture's own range and does not stretch the
  full width of the pond - the art bible says so rather than promising more.
- Verification: `CityLakePlannerTests`, `LakeFishermanTests`,
  `RoadWalkableAreaTests`, `CityRiverWaterTests` and
  `CityNightGlowRegistryTests` — 21/21 — plus the day and night renders above.

## 2026-08-18 — Getting into the boat station

- The precinct shipped sealed. Every contract passed, the geometry rendered,
  and the player could not reach any of it: `RoadWalkableArea` drops any
  surface that is neither `BuildableGround` nor `IsWalkable`, a `Water` cell is
  never walkable (`CitySurfacePlan.cs`: `cell.Topology == OpenLand`), and the
  bank, revetment, pier, hut, boats and fisherman all sit inside the lake's
  `2 x 2` water cells. `PlayerMotor` clamps against that mask every frame, so
  the boundary of those cells was a `52 x 52 m` invisible box.
- I had written the opposite into `ai/architecture-notes.md` the day before, as
  an "accepted asymmetry" — that the bank was walkable but off the nav graph so
  pedestrians would not stray onto it. That reasoning only ever considered
  pedestrians. It never checked the player, and the player is clamped by the
  same mask. Both that note and the work-log claim are corrected.
- Confirmed twice before touching anything: by the deciding lines, and by an
  EditMode probe that walked the mask at the agent radius and reported the bank
  `False` from the cell line inward and the pier head `False`.
- Fixed the way the river already solves it — the river registers its
  promenades, bridge decks and landing platforms as explicit rects, and clips
  its own channel out of the ground. The lake now does the same in reverse:
  `CityLakePlanner.AppendWalkableFootprints` contributes the bank ring and the
  pier deck, and never the pond. A shared `TryCreateSetup` derives the basin
  and frame for both the dressing pass and the mask, so the ground the builder
  draws and the ground the mask admits cannot diverge. Deliberately not routed
  through `CityLakePlan`: that would put the whole ~292-part dressing planner
  and its validator behind every `FromLayout` call in the game.
- Strips are grown `1.2 m` outward past the cell line. Rectangles are tested
  independently, so ground that merely abuts leaves a band two agent-radii wide
  that nobody can stand in — the seam the park fix had to solve.
- A multi-agent sweep over the other candidate mechanisms found two more real
  defects that the passing tests could not see, both since fixed:
  - **The hut stood across the pier root.** `AddHut` clamped a laterally
    cramped hut back inside the bank and slid it onto the deck; `AddPier` never
    consults the reserved list and nothing tested part against part. A `2.35 m`
    collidered wall spanned the deck at its root — an invisible stop the mask
    cannot see, because the mask is rectangles and the wall is a collider. The
    hut now takes the side that has room or does not stand at all, the cemetery
    lodge's rule, and `ValidatePierRootIsClear` enforces it. Reproduced as a
    failing contract on seven fixtures before the fix.
  - **The four cut corners were dead.** The strips followed the waterline's
    axis-aligned bounds, so each `5.2 m` bevel triangle of real collidered bank
    — up to `3.7 m` deep — was outside the mask: four fresh invisible corners
    of the same class. Each is now filled by an eight-step staircase whose far
    corner lands exactly on the diagonal, leaving a bounded `~0.46 m` standoff
    from the boards instead.
- Checked and left alone: pedestrians take the mask only as a clamp
  (`CityPedestrianActor` uses it at one call site, `Constrain`), not as a route
  source, so admitting the bank does not send anyone walking onto it.
- Also confirmed by probe and not a bug: a `0.7 m` band along the rest of the
  precinct perimeter. `LakeShore`, `CemeteryGround`, `Beach` and `OpenGround`
  all return true from `RequiresAuthoredAccess`, so these precincts are entered
  through their one authored street approach by design. The cemetery measures
  identically. The lake's own approach probes walkable end to end.
- Verification: `CityLakePlannerTests` (now including a walk from the street
  approach across the bank and out to the pier head at the full agent radius,
  the four cut corners, and a `24 x 24` sweep proving open water stays shut),
  plus `RoadWalkableAreaTests` — the park's own regressions — and
  `LakeFishermanTests`. 13/13.

## 2026-08-18 — The boat station

- The lake had never had a pass. It was a flat `52 x 52 m` tinted box on the
  shared primitive material, a shore with no texture sheet, no light source
  anywhere on the precinct, and four decorations borrowed from the shared
  open-area pass. It was the only place in the city with no art-bible section.
  Brought it to the cemetery's standard as an abandoned municipal boat
  station: art-bible §10d, the Plan/Planner/WorldBuilder/SurfaceAppearance
  quartet, a texture generator with a measured contract, real fixtures on the
  night registries, and an inhabitant.
- Three things found in the code that changed the design, all confirmed before
  acting on them:
  - `FlowAxis()` normalized a zero `_FlowDirection` to `(0,1)`, so "still
    water" was a river flowing north with the label taken off.
    `CityRiverResources`' doc comment claimed the opposite; both fixed.
  - The lake shore was already ringed by an undocumented `1.05 m`
    `Terrain Guard Rails` box — the `0.40 m` drop exceeds `MaximumSafeStep`
    and only `RiverWater` was skipped. Once the bank made that boundary
    continuous walkable ground the rail became the invisible perimeter the
    park fix removed, so the skip was widened to authored water edges and the
    precinct now owes a visible barrier instead, under a validated
    continuity-and-height contract.
  - `CitySignLettering` carried only `П Р О Д У К Т Ы 7`; «ПРОКАТ ЛОДОК»
    needed `А` and `Л`.
- The grid was deliberately not touched. The waterline is inset inside the
  water cells by an authored bank and its corners cut, so the elevation plan,
  `ValidateLakeBasin` and the map are all untouched — the same hand-off the
  river already gets from `BuildGround`. **This entry originally also claimed
  the nav mask was untouched. That was the bug: see 2026-08-18 — Getting into
  the boat station.**
- Rendered the result rather than trusting the tests, and it was worth it: the
  first build passed every contract and still looked wrong. Cut the
  screen-space mirror outright (it smeared the screen edge across the pond in
  coloured rectangles); reshaped the hulls, which read as ramps until the
  narrow face went on top; turned the hut to face the gate, since a shed
  showing its blank back to the only approach says nothing; filled the bank
  ring's four corners, which the radial projection had left as holes.
- Chased a faint parallel banding on the water through the wave trains, the
  ripple sheet, refraction, absorption, foam and per-vertex fog. Ruled every
  one of them out by measurement (flat normals and saturated absorption still
  band). Moved fog to the fragment stage anyway — interpolating a non-linear
  curve across a metre grid is wrong regardless — and tripled the lake's
  posterisation steps, which took the residual to about `3%` of range. Left it
  named as a gap rather than claimed as fixed; it sits below the PS1
  composite's dither in the shipped renderer, which this capture path bypasses.
- The isotropy of the lake's ripple sheet is the one property that separates it
  from the river's, so it is measured (`slopeAnisotropy`) and bounded in the
  generator rather than left as a comment.
- The fisherman's whole runtime layer, localization and tests are in and green;
  his Blender archetype and staged prefab are not authored, so the factory logs
  `lake_fisherman_provider_missing` and returns null. Recorded as a gap.
- Verification: one focused EditMode selection over `CityLakePlannerTests`,
  `CityRiverWaterTests`, `CityOpenAreaDecorationPlannerTests`,
  `CitySignLetteringTests` and `CityElevationPlannerTests` (20/20), plus
  `LakeFishermanTests` and `LocalizationCatalogTests` (12/12), and the texture
  generators' own `--verify` for both the lake and the river after refactoring
  their shared wrap validator. Not run: full EditMode/PlayMode suites, a player
  build, `CityCemeteryPlannerTests`.

## 2026-08-18 — The river runs

- The banks got granite, quay courses and iron; the water between them was
  still three sine bands on an eight-vertex box, quantized to four steps and
  unlit. It read as the placeholder it was, and more so the better the banks
  got.
- Checked what the engine offers first: HDRP ships a full water system —
  Pool/River/Ocean, current maps, foam, buoyancy — and URP ships none. There
  is no official URP water package in 17 or in 6.1, and Unity's own URP
  samples author water as an ordinary Shader Graph. `boat-attack-water` is a
  Unity repo but explicitly unsupported. So: our own shader, which is what
  the existing one already was.
- Geometry. `CityWaterSurfaceFactory` emits a single-sided grid at a 1 m
  pitch instead of a stretched cube, so a vertex wave has somewhere to go.
  Only the top face: the sides were always behind the quay walls and nothing
  looks up through a river. No UVs — the shader derives them from world
  position, which is what makes the segment joins invisible and what will let
  the sea and the lake reuse the same factory.
- The channel needed a floor. `CityWorldBuilder` skips `RiverWater` and
  `CityTerrainSurfaceWorldBuilder` subtracts `WaterBounds`, so the channel
  was a hole; transparent water would have shown the skybox through it. A
  silt floor sits `RiverBedDepth = 1.10 m` down with two submerged sides
  starting at `0.08 m`, which laps the full quay wall's underside at `0.12 m`
  by 4 cm. Extending the wall skirt instead would have re-pinned geometry
  corrected twice this week, so the bed closes itself.
- Bridge piers ended at the plan datum — `0.12 m` *above* the water top, a
  gap the opaque lid was hiding. They now bottom out on the floor.
- Shader. Transparent queue at `-100`, `ZWrite On`, `Blend Off`: it composites
  against `_CameraOpaqueTexture` and `_CameraDepthTexture` itself, both
  already required by `PC_RPAsset`. That keeps it a depth occluder for the
  light halos at 3000 and makes absorption a function of measured water
  thickness rather than a constant alpha. Three summed wave trains displace
  vertices with an analytic normal; one ripple sheet is sampled twice at
  different pitch, rate and a small rotation — a large rotation would cancel
  the downstream smear that makes it read as a current. Depth drives colour,
  the soft edge against the granite and the foam, so foam lands at the walls,
  the piers and the stair landings without being placed. Refraction rejects
  a sample nearer than the surface, or objects in front of the river bleed
  into it. Banding moved from the whole colour onto the specular, foam and
  rain terms, so the sheets survive the 640x360 composite.
- Textures. `tools/build-city-river-textures.py` gained a fourth albedo
  (`CityRiverBedAlbedo`, 2.0 m, compensation 1.4425) and a second family:
  `CityRiverWaterNormal` and `CityRiverWaterFoam`. Those two are not albedos
  — a derivative map and a mask — so they skip the mean-luminance rule, the
  compensation solve and the channel-ratio bound, and record into a separate
  `waterSheets` block. Both are still wrap-validated. The normal map is the
  project's first linear import (`sRGBTexture: 0`); imported through the sRGB
  curve its neutral 128 stops being neutral and the river lights as if from
  one corner. The three existing albedos regenerated byte-identically, so
  their hashes still hold.
- Regression: `CityRiverWaterTests` pins the grid, the fall, the floor depth,
  the wall overlap, the piers, the absent collider and property block, the
  sheets on the material, and that the shader compiles. That last one was
  verified by deliberately breaking the shader and confirming the test
  reported the file and line — `Shader.Find` resolves a broken shader
  happily, and nothing else would have caught it before the magenta.
  `CityRiverSurfaceAppearanceTests` gained the bed kind and a water-sheet
  contract case.
- `CityRiverWaterTests` 7/7, `CityRiverSurfaceAppearanceTests` 9/9,
  `CityRiverPlannerTests` 8/8.
- Colour. The plan was to set the deep tone to the sea's own
  `(0.10, 0.29, 0.38)` so the mouth would step tonally rather than sharply.
  Rendered, that was wrong and obviously so: the sea's value is an *albedo*
  on a lit material and reaches the screen at a fraction of itself, while
  this shader composites its own colour and emitted it whole — a tropical
  lagoon dropped into a grey city, brighter and far more saturated than
  anything around it. The deep tone is now `(0.070, 0.175, 0.200)`, the same
  hue family brought down to roughly what the old flat river rendered at,
  which had been art-directed to sit in this palette. Fresnel came down from
  `0.5` to `0.30` and specular from `1.15` to `0.85` for the same reason;
  nearly every pixel of a river seen from its own bank is at a grazing angle,
  so the Fresnel term was not a rim, it was the surface.
- Verified by eye, not only by test: the city was built in an edit-mode
  scene and rendered from the promenade, the footbridge, the waterline and
  down the channel. That is what caught the colour. The ripple reads as a
  current, the foam sits in a broken line at both walls, and nothing shows
  through the channel from any angle.
- Not done, deliberately: the sea and the lake are still flat boxes on the
  shared primitive material, so the mouth shows a tonal step. The shader was
  written to take them; that is the next commit. When they adopt it, the
  sea's own tone will need the same albedo-to-rendered correction.

## 2026-08-18 — A bridge crossing ends where its span does

- Reported symptoms at the park footbridge: one cell past the deck is park
  path where the embankment should be, and the neighbouring cell on the road
  flickers between two textures.
- Both are one fault. `CreateBaseSurfaces` insets a travel surface by half
  its own width at each end, so a crossing edge runs bank node to bank node
  minus the deck half-width. At 26 m spacing over a 10 m channel that is
  8 m of overshoot per bank: 4 m of it is the road corridor, the other 4 m
  is the granite promenade. The footbridge deck is 2.8 m wide, not 8 m, so
  its inset is 1.4 m rather than `halfRoad` - the strip reaches 2.6 m into
  the intersection pad, and pad and path top out at exactly
  `nodeY + RoadTop`, which is the flicker. Landward of the span the same
  strip lies 8 cm proud of the promenade, which is the false path cell.
- Shortening the bridges in the previous commit exposed both: the old deck
  ran the full `DeckBounds` and covered the overshoot from above.
- A crossing edge now takes its inset from `SpanBounds`, so the carriageway
  or path is exactly the deck - channel plus the two `QuayEdgeOffset` quay
  seats. The embankment paving carries the approach, as it already did for
  every metre of bank that no bridge touches.
- The road bridge underside loses `SurfaceClearance` on each end as well as
  each side, so its end faces no longer sit flush with the shortened
  carriageway's.
- Regression: `BridgeCrossings_EndOnTheirSpanAndLeaveTheBanksPaved` pins
  every bridge's surface to its span and asserts no travel geometry reaches
  the promenade between the road corridor and the span.
- Follow-up from the same report: with the deck no longer hidden under an
  oversized structure, the bridges read as flat colour, because they were
  the one thing the river builder deliberately left untextured. They now
  take the sheet their own material names - `Iron` for the works
  crossing, `Quay` for the mouth crossing, and the park's `Timber` for the
  footbridge deck, beams and handrails, which belongs to the park's family
  rather than the embankment's three. Primitives take the per-transform
  box projection; the combined rail, plank and structure batches bake
  world UVs at their sheet's pitch, as the stair flights already did.
  No authored colour changed.
- `BuildRiver_TexturesTheBanksAndLeavesTheBridgesFlat` pinned the old
  contract, so it becomes `BuildRiver_TexturesEveryBankAndBridgeMember`:
  every renderer carries the sheet its material names, only the water and
  the lamp glow stay flat, and the tint-to-manifest check stays on the
  bank renderers, whose palette it was solved from.

Verification:

- Focused `CityRiverPlannerTests` passed 8/8; the new test fails on a
  stashed clean tree, so it pins the reported fault.
- Focused `CityRiverSurfaceAppearanceTests` passed 7/7 for the texturing
  follow-up.
- The full EditMode suite was also run against both trees for the surface
  fix: 13 failures, identical before and after, all pre-existing and out
  of scope.

## 2026-08-18 — Stair approach rails stop at the corner

- Reported symptom: a signature stair's rail runs out into the roadway, seen
  around the street between nodes `7,7` and `7,8`.
- Cause: `CityElevationStairPlacementPlanner` anchored both approaches at the
  node centre, while ordinary sidewalks stop at the intersection square
  (`ResolveEndpointInset` in `CityStreetSurfacePlanner`). With the default
  8 m road that pushed the approach paving and its inner guard rail 4 m into
  the junction, 3 m of it inside the crossing carriageway - a rail with posts
  standing across the traffic lanes and the pedestrian crossing.
- The approach now takes the same endpoint inset as the sidewalk it
  continues: `halfRoad` at an intersection core, `-halfRoad` at a stub end,
  clamped so it can never bite past the landing it serves. The lower approach
  loses ~4 m of its ~9.6-10.5 m, and its rail with it.
- Nothing else needed moving: the road datum is flat within `halfRoad` of a
  node, so the approach and the carriageway are level where the rail now
  ends - it was guarding a drop that does not exist there.
- `CityExteriorStairWorldBuilder.BuildRails` skips a degenerate rail rather
  than emitting a zero-length beam, in case a clamp ever collapses one.
- Regression: `AssertApproachClearsCrossingCarriageway` walks the perpendicular
  street edges at both stair nodes and asserts neither the approach footprint
  nor the rail footprint intersects their carriageway.

Verification:

- `BarPromenade.Runtime.csproj` and `BarPromenade.EditModeTests.csproj`
  compile with 0 errors.
- Focused `CityElevationPlannerTests` passed 9/10, including the new
  assertions on all four signature stairs.
- `LegacyAndCustomBlueprints_KeepFlatFallback` fails with "Area 'central-park'
  must be four-neighbour connected"; confirmed identical on a stashed clean
  tree, so it is pre-existing and out of scope here.
- No broader EditMode/PlayMode suite, player build or smoke was run.

## 2026-08-18 — The embankment gets its stone and iron

- The banks were three flat colours: `Granite` underfoot, `GraniteEdge` in
  the retaining wall, `Iron` in every rail. So three sheets, one per thing
  the embankment is actually made of, rather than a generic stone set.
- `tools/build-city-river-textures.py` adds three grammars over the shared
  home machinery. Paving: 0.8 m flags in a running bond, tight recessed
  joints, chamfer arrises, oval worn centres, granite grain and hairline
  cracks. Quay: 0.55 m courses of rusticated blocks, deep mortar, lit top
  arris against a shadowed bottom, tooled pitting, runoff and efflorescence.
  Iron: brushed paint over castings, chipped to bright lips around darker
  pits, rust freckling out of the chips.
- Deliberately no waterline band on the quay sheet. The runtime picks each
  span's UV offset from a transform hash, so a band would sit at a
  different height on every wall; the damp reads as vertical runoff, which
  is offset-agnostic.
- Pitches are metre-true: paving `3.2 m` (four flags), quay `2.2 m` (four
  courses, about one wall span tall), iron `1.2 m` along a rail. Measured
  contract in `ArtSource/City/river-textures.json`: compensations
  `1.3875 / 1.404 / 1.5145`, brightness error `0.24 / 0.59 / 0.53 %`, edge
  `3.5 / 2.9 / 6.3`, contrast `68 / 72 / 76`.
- First run failed its own seam check: joints were laid to the right of each
  grid line, which makes column 0 mortar and column 1023 flag. Centring both
  joint and mortar on the line fixed it - `edge=17.97` to `3.53`.
- `CityRiverSurfaceAppearance` owns the recipes and both application paths,
  because the embankment is built both ways: slabs, walls, rails and posts
  are separate primitives taking `_BaseMap_ST` metre tiling, while the stair
  flights and lamp posts are combined batches taking world UVs baked at the
  recipe pitch.
- The projection is resolved from the mesh, not passed in:
  `SurfaceAppearanceCore.ResolveBoxProjection` drops the thinnest local axis.
  The embankment is three kinds in four orientations across some thirty call
  sites, and naming a plane at each is how a five-metre rail ends up
  sampling one stretched patch of its sheet. The bollards pass
  `CylinderSide` explicitly, being the one non-box surface.
- The river builder's private `CreateBox`/`CreateBeamBetween`/
  `CreateSlopedSurface` take an optional surface kind. The bridges pass
  none and stay flat, which is also what the new test pins.
- Verified: EditMode `CityRiverSurfaceAppearanceTests` 7/7 and
  `CityRiverPlannerTests` 7/7, both after the sheets were regenerated once
  for quality (the flags' polish was a scored rectangle, the iron had no
  brush direction). Runtime and EditModeTests compile with 0 errors. The
  appearance test pins the PNG to the manifest by sha256, the manifest's
  measured numbers to the recipes, and the compensation rule to the tints
  the built river really applies - so regenerating without updating the
  recipes, or editing the palette without regenerating, fails there.
- Not run: PlayMode, and the rest of the EditMode suite.

## 2026-08-18 — Bridges end at the water

- Reported at the park footbridge and suspected on the other two; both
  correct, and one cause. Every bridge was built on `DeckBounds`, which
  runs bank node to bank node — `26 m` at default spacing for a `10 m`
  channel. The `8 m` of overshoot per bank is exactly the embankment.
- So the timber planks, the undersides, the girders and the parapets all
  lay across the granite promenade slab, `3 cm` into its top face, and the
  guard rails — inset only by half the road width — started at the
  promenade's landward edge and ran its full `4 m` to the water.
- `CityRiverBridgeDescriptor` now carries a second footprint. `DeckBounds`
  keeps its meaning, the crossing that pedestrian links, the map line and
  the furniture exclusions read; the new `SpanBounds` is the structure: the
  channel plus one `CityRiverPlanner.QuayEdgeOffset` (`0.48 m`) seat on each
  quay wall, which is the same plane the quay guard rails stand on. Planks,
  underside, girders, piers and both parapets are built on the span.
- The span, not the road width, now sets the guard range; the old
  `RailThickness * 0.5` inset stays, because `AddRailPostsAlongX` centres its
  end posts on the range ends and they would otherwise overhang the seat.
- The road surface across the crossing is unchanged — it is the street plan's,
  not the bridge's — so the approaches still carry road over the embankment
  and nothing lost its walking surface.
- Landing stairs sit landward of the quay, so their parapet openings now clip
  to `0.6 m` — a hole at the water's edge rather than a way through.
  `CreateBridgeLandingGaps` drops any opening under `MinimumParapetOpening`
  (`1.2 m`); the landing parapet runs unbroken over the water and the stairs
  are reached by walking round its end, which the promenade already allows.
- Piers moved from `0.18` to `0.30` of the footprint half-width, which keeps
  them near the banks as before now that the footprint is the channel.
- Shortening left the reported flicker, because it was a second fault with the
  same shape: the bridges shared planes with the travel surface the street plan
  draws across the crossing. Both banks of a crossing resolve to the same
  height — `ResolveDefaultNodeElevation` gives `distanceToBank = 0` on each and
  `waterY` depends only on `z` — so `WestY == EastY`, the crossing is flat, and
  the park path's top plane is exactly `AverageY + RoadTop`, which is exactly
  where the timber planks were topped. The planks were also exactly `2.8 m`
  wide, the path's own width, so the side faces coincided as well; on the road
  bridges the underside is `8 m` wide against an `8 m` road, coincident over
  the `8 cm` band where they overlap.
- `SurfaceClearance` (`3 cm`) now holds structure off those planes. The timber
  deck is topped at `deckY + SurfaceClearance` and widened by `2x` it, so it
  stands proud of the path and overhangs its sides; the planks still sink
  `8 cm` into the path, so nothing floats and no seam opens. The road underside
  goes the other way and is *narrowed* by `2x` it: widening it past the
  carriageway would have exposed the parapet posts' base plane, which sits at
  `AverageY` — the same height as the underside top — and was until now hidden
  inside the asphalt slab. Recessed, the slab overhangs its girders, which is
  the right silhouette anyway.
- Verified: EditMode `CityRiverPlannerTests` 7/7 for the shortening, including
  a new plan-level check that every `SpanBounds` covers the water and overlaps
  neither promenade. `BarPromenade.Runtime.csproj` and
  `BarPromenade.EditModeTests.csproj` compile with 0 errors, the clearance
  change included. `CityPedestrianPlannerTests` passed 7/8; the failure
  (`Create_ElevatedCity_UsesLocalSurfacesAndSignatureStairs`,
  `city-stair-oldtown` empty) reproduces on the unmodified branch and is not
  from this change. `RoadFencePlannerTests` was not run — it reads only
  `DeckBounds` and `IsRiverBridgeEdge`, both unchanged.
- **Not re-run after the clearance change:** the editor was reopened, so
  `CityRiverPlannerTests` still needs one batchmode pass to confirm the two
  updated deck assertions and the new underside-overhang check.

## 2026-08-17 — Swings that swing

- Three complaints about the park playground, all correct: the swings
  sat almost on top of the bench, the frame was four legs under one
  bar with nothing bracing it, and the seats were baked boxes.
- `CityPlaygroundGeometry` is now the single set of numbers the frame,
  the proxies, the bench seat offer and the seats all read, so those
  three cannot drift apart again. The bench moves from `z = 2.25` to
  `3.60`; the protection radius follows, `3.3 -> 3.9`, because what
  needs the room is the bench, not the frame.
- Each A-frame gets its own cross beam (`CrossBeamY = 2.85`) along the
  frame depth, and the long beam moves on top of both (`3.06`), which
  is also where the ropes are now tied (`RopeAnchorY = 2.95`).
- The two seats leave the batched decoration layer. `CityWorldBuilder`
  builds them through `CityPlaygroundSwingBuilder` as a sibling root:
  one hinged rigid body per seat, hinge axis along the beam, limits
  `±50°`, centre of mass at the plank, and two taut rope boxes drawn
  in the pivot's own space so the timber sheet rides the arc instead
  of swimming over it. PhysX cloth was considered for the ropes and
  rejected — cloth is driven and drives nothing back, so it cannot
  carry a seat, let alone be pushed.
- `CityPlaygroundSwing` is the push: while anything with a
  `CharacterController` walks into the plank, the seat is accelerated
  towards that walker's own pace along the swing plane and never past
  it, so it leaves his hands at walking speed. The body never sleeps,
  because a sleeping body reports no trigger stay and a resting swing
  is exactly the one he walks up to.
- The blocking proxy was one box across the whole frame, so the bay
  was unreachable; it is now one proxy per A-frame plus the bench,
  three in all, and the bay between them is open ground.
- Verified: EditMode `CityPlaygroundSwingTests` +
  `CityDecorationPlannerTests` + `CityParkSurfaceAppearanceTests` +
  `CityBenchRestTests`, 44/45. The new tests read the recipe's own
  parts back (both cross beams present, the bay empty), the built
  rig (two hinged non-kinematic pendulums under the beam, one solid
  plank collider and one push volume each), the bench clearance
  (bench front and the hero's sit dock both outside the swept arc)
  and the proxies (frames solid, bay open). The single failure,
  `BarSideYard_LeansPhoneBoothAndDumpsterOnTheBarWall`, fails
  identically on the clean branch and is about the home-yard site
  planner.
- The push itself is physics, so it is proved in PlayMode:
  `CityPlaygroundSwingPlayModeTests` walks a bare hero-sized controller
  into a seat for `60` fixed steps and reads `57` contact steps, `21`
  push steps, `0.80 m` of travel along the push axis and a rising
  plank; the walker himself only advances `1.27 m` in that time,
  because the swing is what gives way. It then teleports him clear and
  waits for the seat to come back past its rest. First run failed at
  zero movement — the test's own walker, not the swing: a live
  `CharacterController` owns its pose and dragged the transform back to
  where the object was created, which is the same trap
  `PlayerMotor.Teleport` works around.

## 2026-08-17 — The park's invisible wall

- Bug: the player could not walk off the street into Central Park
  anywhere — an invisible wall the whole way round, over ground that
  looks open and a lawn mesh that runs on unbroken.
- Two independent causes, found in that order.
- First, a `0.4 m` seam at the gates. `CityRoadGroundBoundaryPlanner`
  classifies every gate span as a safe connection and
  `CityGroundTraversalPlanner` turns it into a connector, but that
  connector reaches a fixed `ConnectorReach` (`0.8 m`) either side of
  the park cell edge, while a region's `WalkableBounds` is inset
  `RoadWidth * 0.5 + 1.2 m` from the cell node — `1.2 m` past the same
  edge. Road, connector and lawn left an unwalkable band `0.4 m` wide,
  and an agent of radius `0.35 m` cannot step over it.
  `AddParkLawnReach` in `CityGroundTraversalPlanner` now adds, for a
  safe `ParkGround` span whose lawn edge lies beyond the standard
  connector, a second strip from the seam onto the lawn with the same
  overlap at both ends. The existing connector is untouched, the strip
  is clipped to the span the boundary planner produced, and interior
  park-path seams get nothing because they already sit inside the lawn.
- Second — the actual complaint, reported against cell `(8, 5)` — the
  gates were never the whole boundary. `RequiresAuthoredAccess` funnels
  a park's street frontage through gates because the park is fenced,
  but `CityWorldBuilder` skips `BuildParkHedges` on a terraced city,
  where a straight hedge would float over the terrace. The production
  city is terraced, so it has no hedge and no gate posts and yet kept
  the gates-only navigation: `93 m` of open-looking frontage per side,
  passable at four `8.8 m` openings. Navigation and geometry now share
  one predicate, `CityLayout.HasParkBoundaryHedges`, used by the world
  builder to raise the hedge and by the boundary planner to demand a
  gate. A hedged (flat) park keeps gate-only entry; a terraced one is
  open along every level-safe metre and closed only where the boundary
  plan finds a real drop.
- Verified: EditMode `RoadWalkableAreaTests` +
  `CityVerticalTraversalAuditTests` + `CityElevationPlannerTests`. The
  new `FromLayout_WalksEveryLevelSafeParkGateOntoTheLawn` and
  `FromLayout_OpensEveryLevelSafeParkFrontageWithoutHedges` walk the
  gates and every safe frontage span inward in `0.25 m` steps at the
  full agent radius; both fail before the change.
  `FromLayout_AddsParkLawnButNotPerimeterGap` still passes, so a hedged
  park still admits only through its gates. A perimeter dump confirmed
  `(8, 5)` open end to end and only the guarded terrace drop at
  `(7, 7)` still closed. Fourteen failures elsewhere in the suite (bus,
  session, sfx, elevation, beach/lake) are pre-existing on this branch
  — the failure set is identical before and after.

## 2026-08-17 — Central Park surfaces

- Scope: the park was the last big zone still shipping flat colours —
  lawn, paths, plaza discs, trunks, canopies, benches, hedges. Six new
  sheets from `tools/build-city-park-textures.py`
  (`CityPark{Lawn,Path,Plaza,Bark,Foliage,Timber}Albedo`), the usual
  import-from-`build-home-textures.py` pattern the cemetery generator
  established, plus `ArtSource/City/park-textures.json` and its contact
  sheet.
- Grammars transcribe the bible's park materials: turf with bald
  trodden patches, grit/ruts/pebbles, a `4x4` slab joint grid, vertical
  bark ridges with knots, leaf clumps over dark gaps, grain under
  flaked paint. Two local wrap helpers the shared module lacks —
  `wrap_polygon`/`wrap_leaf` and `wrap_flake` — because the first pass
  drew leaves and paint flakes as ellipses and both read as bubbles at
  1:1.
- Compensation: timber's `(0.38, 0.22, 0.10)` could not hold 8% across
  a single constant at `mean_target 0.50` (9.0%). Raised the mean to
  `0.58` (timber) and `0.55` (foliage) rather than touch the authored
  palette — a higher-mean sheet is already precedent
  (`HomeBedLinen 0.58`, `HomeEnamel 0.62`) and the compensation makes
  the final brightness identical either way. Errors now 7.3% / 6.6%,
  the rest 1.2-4.2%.
- `RuntimeWorldUvMode.BoxProjected` in `RuntimePrimitiveFactory`: the
  XZ planar bake collapses a vertical face to one line of the sheet
  (V is that face's constant world Z). Fine for the cemetery's small
  monuments, ruinous for a `18 m` hedge run. Box projection picks the
  plane from the face normal, so every face tiles at true metre scale
  and neighbours still share world coordinates. The old
  `xzPlanarUvTileSize` parameter became `worldUvTileSize` + an optional
  mode; every existing call site is positional and unchanged.
- `CityTerrainSurfaceWorldBuilder.Build`/`BuildConformingDisc` took an
  optional `worldUvTileSize` (default: the city ground pitch) so the
  lawn and the plaza discs bake at their own sheets' pitch.
- Seams for testing: `BuildParkLawn` extracted from `BuildGround` and
  `BuildPark` made internal, so both are reachable without building
  144 buildings. `HomeExteriorViewBuilder` grew `BuildParkPathBoxesIfAny`
  and lost its now-unused generic combined-box helper.
- Verified: `python tools/build-city-park-textures.py --verify` (six
  sheets pass mean/edge/seam/contrast/chroma/compensation), then the
  EditMode selection `CityParkSurfaceAppearanceTests` +
  `RuntimePrimitiveFactoryTests` — 30/30. Not run: full EditMode/
  PlayMode suites, player build.

### Follow-up: the four park landmarks

- Audited the whole decoration layer first: all 24 families are flat,
  batched by `(chunk 48 m, BatchStyle)` into 7 shared colours, with
  `SetColor` and no `_BaseMap` anywhere. So the park's fountain,
  bandstand, chess tables and playground shared meshes with every
  neighbouring district's decor and could not be textured in place.
- Rejected the enum-explosion fix. A `BatchStyle` carries one colour,
  and the park parts need 7 (material, colour) combinations —
  Stone/Masonry, Stone/Street, Timber/{Masonry,Residential,Street},
  PaintedMetal/{Residential,Street}. Added a second batch axis instead:
  `DecorationPart` carries an optional `CityParkSurfaceKind`, and
  `ChunkGeometry` keeps a `ParkBatchKey`-keyed dictionary beside the
  seven flat arrays, sorted on read so chunk output stays deterministic.
  The other 20 families are untouched and still flat.
- Two new sheets, `CityParkStoneAlbedo` (1.5 m, jointless — the plaza
  sheet's 4x4 grid would read as a crack across a carved figure) and
  `CityParkPaintedMetalAlbedo` (1.2 m, metallic 0.15). Timber is
  reused: its spec gained the three decoration batch colours, which
  left the solved compensation at 1.3345 exactly, so all six original
  PNGs stayed byte-identical (checked by sha256 against the previous
  manifest).
- Colours are unchanged everywhere. Each part keeps the batch tone it
  had and only gains a sheet, so the darkened statue stays dark and the
  teal playground frame stays teal. The fountain's standing water keeps
  its flat plane, matching the river, lake and sea.
- `RuntimePrimitiveFactory` gained an optional `worldUvOrigin`: the
  decoration chunk transform is offset by the chunk origin, so without
  it a landmark whose parts straddle a 48 m boundary would restart its
  tiling mid-object. Default is zero, so every other caller is
  unaffected.
- Verified: generator `--verify` (8/8 sheets), then EditMode
  `CityParkSurfaceAppearanceTests` + `RuntimePrimitiveFactoryTests` —
  37/37, including a new test asserting that park batches carry a park
  sheet and that nothing else in the decoration layer carries any.
- Fixed one bug of my own on the way: the test's builder-tint table
  read the three decoration colours before their static initialisers
  ran, so both new sheets were validated against black.
- Pre-existing failure, unrelated and left alone:
  `CityDecorationPlannerTests.BarSideYard_LeansPhoneBoothAndDumpsterOnTheBarWall`
  fails on `HomeYardSitePlanner.TryCreate` — confirmed by stashing this
  work and rerunning it on the clean tree.

## 2026-08-17 — The watchman moves to his doorstep, under a bulb

- Stance: `CemeteryWatchmanPlan` now reads `cemetery-lodge-step` and
  `cemetery-lodge-wall-rear` instead of the window board. The rear
  wall is a slab, so the axis it is thin along is the doorway's
  normal (signed away from the booth by the base→step vector); he
  stands `DoorStandOffMeters` = 0.75 m past the step's centre and
  `AlleyStepMeters` = 0.30 m aside along the wall toward the alley —
  depth 4.025 / lateral 7.12 in the lodge frame. Outside the roof
  overhang (3.35), in front of the 35°-ajar leaf rather than behind
  it, 2.05 m from the base centre so the "right beside his booth"
  guard holds.
- Facing: no longer the gate arch. From behind the lodge that line
  clipped his own rear corner at lateral 5.65, so he was staring into
  his wall. The heading is now the alley: the arch vector with the
  door normal projected out of it, i.e. straight along the rear wall
  toward the main alley every visitor walks up. Arch-less plans fall
  back to the door normal.
- Porch bulb: `CityCemeteryLampDescriptor` gained a
  `CityCemeteryLampKind` (Alley | LodgePorch) and `AddLodge` appends
  `cemetery-lodge-lamp` at depth 3.20 / lateral 6.60 — under the eave
  (3.35), clear of the wall face (3.10) so the swung leaf passes in
  front of it, and beside the opening over the solid 1.36 m stretch
  of rear wall (5.60–6.96); the far jamb has 0.12 m to the corner and
  nothing to carry a bracket. World builder branches on kind: stem
  into the eave, tin hood, emissive bulb at 2.01 m, warm point light
  (1.00/0.80/0.55) with its own halo. Lodge part count stays 15 — the
  fixture is built, not batched, exactly like the alley mantles.
- Made it actually light him: intensity 30 → 110 at night with range
  8 (the alley mantle is 42, the drying-yard floodlight 150 — at 30
  the bulb glowed and lit nothing), `ForcePixel` kept so it wins the
  URP asset's `AdditionalLightsPerObjectLimit: 4` beside him. Pedestrian
  material is URP/Lit, so the point light does reach him.
- Around-the-clock: `CityNightSiteLightRegistry.Register` gained a
  `dayIntensity` overload — intensity lerps day→night instead of
  scaling to zero, and the light stays enabled when that floor is
  non-zero. The porch bulb sits at 25 by day, the only fixture in the
  city that never goes out; its fog halo still fades with the night
  factor. Every existing caller keeps the old behaviour through the
  three-argument overload.
- Validation: the alley 3–9 lamp rule now counts alley lamps only,
  plus a new invariant — a plan has a porch lamp iff it has a lodge.
- Verification: bundled-dotnet compiles green (runtime + EditMode).
  EditMode run is PENDING — the Unity editor was reopened mid-task
  and batchmode cannot share the project; the previous placement's
  run was 13/13 with the full suite at 1045/1059 (the 14 failures are
  pre-existing, confirmed against a stashed clean tree).

## 2026-08-17 — The gate lodge and the snide cemetery watchman

- Booth: new `CityCemeteryPartKind.Lodge` + `AddLodge` in the
  cemetery planner — 15 `cemetery-lodge-*` parts (timber floor/roof,
  concrete shell with a 0.92 m doorway under a 2.12 m lintel and a
  1.0 × 0.9 watch window facing the alley, iron stovepipe, 35°-ajar
  door leaf via the gate-leaf composed-direction pattern, step and
  stool). Placed in the gate-side pocket on the roomier lateral side
  (fit guard 8.3 m → narrower blueprints get no lodge), emitted
  before benches/graves/trees with the pocket appended to the
  reserved footprints. Every blocking part clears the RAW approach
  rectangle (the stricter test-side rule), so no exemption needed.
  World builder untouched — batches are per-style.
- Watchman: fifth staged archetype `cemetery_watchman_v1` (seed
  963201, 43 meshes / 988 triangles): aerodrome cap owning the
  1.75 m envelope, quilted telogreika, kirza boot shafts, grey
  moustache/stubble/brows with one raised — the smirk is geometry.
  Two clips: `WatchmanWatch` 6 s (weight shifts, disapproving head
  shake, one chin jut + shrug) and `WatchmanShuffle` 1.5 s
  (hands-behind-back shuffle, authored now for a later patrol pass —
  the runtime is stationary idle only). Quartet + stance read back
  from the plan's own lodge parts (`cemetery-lodge-base` →
  `window-board` direction, facing the gate arch), wired in
  `CityGameRoot` after the mourner.
- Snideness: the project's first rotating talk stub. Cashier-contract
  `CemeteryWatchmanInteraction` on its own trigger box in front of
  the window + pure `CemeteryWatchmanQuips` (seeded xorshift over 15
  localized keys, a repeat draw slides to its neighbour — never the
  same line twice running). 16 new keys in ru/en catalogs +
  `RequiredKeys`; lines kept ≤ ~70 chars for the prompt panel.
- Verification: Blender rebuild green first run (24 actions,
  determinism ok); bundled-dotnet compiles green; batchmode
  `RunCemeteryWatchman` build+bind; focused EditMode run of
  `CemeteryWatchmanTests` (lodge pocket/approach sweep, Absent
  degradation, window-post stance and gate-arch facing, quip
  determinism/coverage/no-repeat, catalog presence) plus
  `CityCemeteryPlannerTests`, `CemeteryMournerTests`,
  `CityPedestrianRuntimeTests` (staged clip count 8 → 10) and
  `LocalizationCatalogTests` regressions.

## 2026-08-17 — The cemetery mourner: a scripted graveside visit

- New scripted transient staged NPC, the fourth staged archetype
  (`cemetery_mourner_v1`, seed 918477, 38 meshes / 988 triangles):
  babushka-derived geometry with a long near-black coat, wrist-length
  sleeves, a heavy veil with shoulder drapes, pale skin and five
  `ACC_Bouquet*` meshes on `hand.R`. Two clips instead of four —
  the shared library's validators require exactly idle+walk looping
  clips per design — so `MournerWalk` (1.5 s cradle-armed gait) plus
  one `MournerMourn` rite (36.5 s = lay 3.5 + sob 30 + wipe 3, keys
  built in a loop, first == last for the loop contract, played once
  per visit). Grounding bake handled the 876-frame clip fine.
- Runtime quartet in `Runtime/City/Cemetery/` plus
  `CityCemeteryMournerController` (weighbridge-needle polling mould,
  created in `CityGameRoot` after the needle): trigger = hero within
  28 m of `CemeteryPlan.Grounds` + 180 s cooldown, one mourner at a
  time. Spawn honours the director's spirit by hand: street-axis
  candidates 26-90 m from the gate, accepted at >= 76 m from the hero
  or outside the camera's ~80° half-cone, most-behind-camera far
  fallback. Route = spawn → gate threshold (terrain-sampled street
  heights) → main-alley spine (planner's own lateral clamp mirrored,
  2.9 m) → sideways to the stand point 1.75 m before the slab
  (clears enclosure rails; enclosed ordinals are excluded outright
  via `GraveEnclosure` parts). Grave choice is xorshift over
  `Hash(citySeed, visitIndex)`. Lay cue at 1.9 s hides the hand
  bouquet and stands a four-box shared-material bouquet at the
  authored offering offset mirrored to (-0.22, 0.17, -0.45); it
  despawns with her. She finishes the rite even if the hero leaves
  (early despawn only past 88 m and unseen).
- Pipeline gotcha worth remembering: `CityPedestrianModelImporter`
  whitelists model paths — a new staged FBX imports with default
  settings (no axis bake, no copied Avatar) until added to
  `IsPedestrianModel`, failing prefab validation with "Bone 'root'
  rest transform differs".
- Pre-existing breakage repaired in passing: the weighbridge pass
  never taught `CityPedestrianRuntimeTests` its two clips, so its
  hardcoded locomotion list and `StagedLocomotionClipCount = 4` had
  been failing since that commit; the list now carries the weigh and
  mourner pairs and the constant is `8`.
- Verification: Blender rebuild green first run (22 actions,
  determinism check passed); bundled-dotnet compiles green; one
  batchmode `RunCemeteryMourner` build+bind; focused EditMode run of
  `CemeteryMournerTests` (8 new tests: candidates/enclosures, the
  OutwardNormal sign guard, deterministic grave choice, gate route
  containment, spawn rule, 30 s cry timeline with one-shot lay cue,
  hitch remainder carry, trigger band) plus the babushka, attendant,
  cemetery-planner and pedestrian-runtime regressions.

## 2026-08-17 — Cemetery: grounded оградки and truly sittable benches

- User-reported: the grave enclosures visually hovered — the rail band
  sits at knee height (0.24-0.66 m) with nothing carrying it. Each
  enclosure now stands on four grounded corner posts
  (`0.07 x 0.68 m`, ids `rail-post-{a..d}`), and the planner test
  asserts exactly four posts per enclosure with bottoms on
  `GroundTopY`. Part budget re-derived `480 -> 560`.
- The alley benches joined the shared bench-sit system instead of
  being scenery: `CityWorldResult` now carries the nullable
  `CemeteryPlan`, `CityBenchSitPlan.CreateAll` takes it as a required
  parameter and reads one `CityBenchSeat` per `cemetery-bench-*-seat`
  part (top centre, plank size, `GroundTopY`, facing from the part's
  own rotation) — the same read-back-from-the-plan contract as the
  bar-side yard bench, docked through `ResolveSeatDockGround` onto the
  alley edge. `CityGameRoot` passes `World.CemeteryPlan`; the NPC
  bench-rest planner sees the seats too but graph reach naturally
  keeps roamers out of the cemetery.
- Tests: `CityBenchRestTests` call sites updated to the new signature;
  `CityCemeteryPlannerTests` gained the enclosure-post assertions and
  `DefaultCity_CemeteryBenchesJoinTheSittableSeats` (one sit offer per
  drawn seat plank, inside the grounds, pelvis on the plank at
  `0.49 m + SeatClearance`).
- Verification: bundled-dotnet compiles green; one focused batchmode
  run of `CityCemeteryPlannerTests` + `CityBenchRestTests`.

## 2026-08-17 — Cemetery follow-up: the lamp chain and alley benches

- The three fixed lamps became a chain along the whole main alley: the
  symmetric gate pair stays, then one lamp per `LampSpacing = 15.4 m`
  on alternating sides, plus a far-end lamp when the chain stops more
  than half a spacing short of the fence (default city: 6 lamps on the
  long orientation, 4-5 on the short one). `TryAddLamp` now also
  rejects spots on the gravel itself (alley-overlap check) and lamp
  IDs are `D2`-padded so ordinal order stays lexical.
- New `Bench` part kind + `Timber` style: a painted-plank bench with
  iron legs (4 parts) beside the main alley just before each cross
  alley and one near the far fence, facing the gravel. Benches prefer
  alternating sides and flip across the alley when a lamp already
  holds theirs; their footprints join the lamp footprints in one
  `reserved` list that graves and trees now avoid (renamed from
  `lampFootprints`).
- Validator: a full cemetery now expects `3-9` lamps.
  `CityCemeteryPlannerTests` asserts `4-9` lamps, a `> 12 m` lamp
  spread along the alley (not clustered at the gate) and `>= 8` bench
  parts. Art bible §10c, README, overview, systems map and release
  notes updated to the chain-plus-benches picture.
- Verification: bundled-dotnet compiles of Runtime + EditModeTests
  green, then the focused `CityCemeteryPlannerTests` batchmode run
  (deferred behind a lockfile watch until the interactive editor
  closed). The first run caught a real defect: the new lamp-vs-alley
  overlap guard rejected every lamp, because at the old
  `half + 0.45 m` edge offset the lamp footprint grazed the expanded
  alley band exactly at the seam. Lamps moved to `half + 0.65 m` off
  the gravel; second run all green (7/7).

## 2026-08-17 — The cemetery gets its own module, variety and light

- Extracted the cemetery out of `CityOpenAreaDecorationPlan` (which
  kept the lake and bar-side yard; its budget dropped `420 -> 260`)
  into a dedicated conventional triad: `CityCemeteryPlan` (oriented
  part descriptors with rotation — the open-area AABB descriptor could
  not carry tilted crosses or swung gate leaves), `CityCemeteryPlanner`
  (pure, `StableHash`-seeded, `ValidateOrThrow`), and
  `CityCemeteryWorldBuilder` (48 m chunk × style batches via
  `CreateCombinedOrientedBoxes`). Budget `480` parts; the default city
  plans ~`420`.
- The planner works in a gate-relative depth/lateral frame so one
  algorithm serves all four gate orientations: main gravel alley from
  the gate plus cross alleys every `20 m` (chunk-split slabs); a
  jittered grave grid (`4.0 x 5.0 m` pitch, `48%` hash acceptance)
  with six monument variants — the first six accepted plots cycle all
  variants so the gate row is a showcase and the contract is testable —
  three stone tints, back-tilt up to `6°` in the rows deepest from the
  gate, `35%` оградка enclosures and `25%` offerings; fence ported
  intact plus four corner pillars; the gate gained a `2.4 m` pillar
  pair, an iron arch with plaque (overhead parts are exempt from the
  approach-clearance rule by a `2.1 m` bottom-height test) and two
  nearly-open lattice leaves whose `8°` opening angle keeps their
  lateral reach inside the `0.35 m` margin to the expanded approach;
  hash-thinned perimeter/interior birches and firs and grave-side
  bushes; lamps are planned first so graves and trees avoid them.
- Trap fixed along the way: `CityOpenAreaAccessDescriptor.
  OutwardNormal` points from the street *into* the grounds despite its
  name (the lake and the original cemetery pass both read it that
  way); the first draft inverted it, which put the gate on the far
  side and walled off the real approach — caught by `ValidateOrThrow`
  in the first test run.
- Three alley lamps follow the island-floodlight recipe (emissive
  mantle + `CityNightGlowRegistry`, `CityLightHalo`, point light
  `LightShadows.None` + `CityNightSiteLightRegistry`), so they die by
  day; the pole is the only collider.
- New deterministic texture pass `tools/build-cemetery-textures.py`
  (engine-imported from `build-home-textures.py`): four validated
  1024² sheets — low-contrast speckled granite (planar-XZ UVs smear
  vertically on monument faces; the quiet sheet reads as weathering),
  cracked/lichened stone, pebble gravel, leaf-litter soil — with
  manifest `ArtSource/City/cemetery-textures.json`.
  `CityCemeterySurfaceAppearance` transcribes the solved recipes
  (compensations `1.398/1.397/1.4055/1.4755`) and applies them to
  combined meshes over `RuntimePrimitiveFactory`'s world-planar UVs
  (no per-renderer UV offset needed — world position decorrelates).
  "Cemetery Ground" in `CityWorldBuilder` now carries the soil sheet
  the way roads carry asphalt. Hand-authored the four texture `.meta`
  files from the POI template (512 import, Repeat).
- Art bible gained §10c «Кладбище — город, который никуда не спешит»;
  release notes, project overview, systems map and system tree
  updated.
- Verification: bundled-dotnet compiles of Runtime + EditModeTests,
  then one focused batchmode EditMode run —
  `CityCemeteryPlannerTests` (determinism, `>= 30` graves, all six
  variants and three tints, gate dressing counts, slab non-overlap,
  approach clearance, textured build, day-dead lamps) plus the
  trimmed `CityOpenAreaDecorationPlannerTests` — all 10 green.

## 2026-08-17 — Last route island: the inner route ring joins the paving

- Reversed the one texturing exception on the island: the `7.2 m` inner
  route ring was left a flat painted marking band, but between the
  textured platform and the textured centre disc it read in-game as a
  missing texture rather than as paint. It now carries the paving sheet
  (`CylinderCapXZ`) under its existing dark `NightlifeFrame` tint —
  paint over the same paving — so the island is textured with no
  exceptions. `Build_TexturesTheLastRouteIsland` paving count `2 -> 3`.
- Verification: headless runtime and EditMode-test compiles green; the
  EditMode run itself was blocked by an open editor session.

## 2026-08-17 — The weighbridge is attended, and the scale answers weight

- The Industrial cold weighbridge received its authored pair — the last
  of the four canonical POIs to get residents — built strictly on the
  babushka mould (`Plan`/`Presentation`/`Factory`/`Provider` in a new
  `City/Weighbridge/`, staged model outside `Resources`, passivity
  guard, attention magnets at `1.60 m`). The weigher (palette 0) stands
  beside the mechanism at recipe-local `(3.05, 1.60)` — beside the
  axis, never across it, per the art bible's not-a-checkpoint rule,
  now also a guardrail test — looping a `6 s` check: crane up at the
  dial, lean to the linkage, crouch and chalk the deck edge (the chalk
  marks get their author; `ACC_Chalk` is role-enabled like the
  babushka props). The weighed worker (palette 2, `0.97x`, `+0.85 s`)
  paces the deck's long axis on the deck top with his corridor
  position slaved to his clip's normalized time
  (`EvaluateCorridorProgress`, pure), so pose and travel cannot drift:
  one `12 s` loop = one half round trip, direction flips on iteration
  parity, and normalized `0.36-0.64` holds him square and still at the
  deck centre — cross-commented on both sides of the python/runtime
  boundary like `StrikeNormalizedTime`.
- The scale answers weight. `BuildWeighbridge` registers the `Scale
  Needle` transform in `CityWeighbridgeIndicatorRegistry` (carpet
  registry pattern; City build only — the Home vista never claims the
  slot), and `CityWeighbridgeNeedleController` on the City root eases
  the needle off its captured authored `28°` rest by up to `34°` local
  roll while the worker's pause or the hero stands on the walkable
  deck (`TryDescribeWeighbridgeDeck` rect + foot band), exponential
  `0.45 s` attack / `0.90 s` release, and settles it back when the
  deck empties. Needle logic lives outside the NPC prefab: the
  attendants stay passive and are only polled (`IsWeighingNow`).
- Art: new staged `weigh_attendant` archetype (`842519`, `960` tris,
  exact `1.75 m` cap crown) — quilted grey-green jacket with seams,
  buttons and hip pockets, deliberately no authority markers — plus
  `WeigherCheck`/`WeighedPace` in the shared locomotion library
  (`18 -> 20` clips). `CityPedestrianModelImporter` taught the new
  model path (the miss surfaced as a real import failure: without
  `bakeAxisConversion` + avatar copy the root rest transform diverged
  and `ValidateDescriptor` threw — the explicit-list contract worked).
  `Rebuild Staged Weigh Attendant` menu builds prefab + provider.
- Verification: headless runtime/editor compiles green;
  `WeighbridgeAttendantTests` (stances in bounds, axis guardrail,
  needle registration, `0.30 m` obstacle sweep, deck-rect point tests,
  monotone attack/release easing, pause-window hold, provider binding)
  green; `DryingYardBabushkaTests` re-run green after the shared
  library regeneration; Blender build `CITY PEDESTRIAN ART BUILD OK`,
  determinism check passing, existing model manifests byte-identical.

## 2026-08-17 — Last route island: textured end to end, mast floodlight

- The island joins the drying yard as a fully textured public place. A
  fifth scripted POI albedo, `CityPoiPaperAlbedo` (new `poi_paper`
  grammar: paper fibre, faded print rows, creases, glue/bleach
  staining), covers the island's paper layer — totem map backing, torn
  posters, weathered route plates, schedule rows and the discarded
  timetable. Painted metal covers all fifteen canopy members, the mast
  group, the departure board frame, the bench base, the waste bin and
  the new floodlight metalwork; the empty bench seat is timber; the
  lost scarf and all six simulated canopy rags go through the cloth
  path (`ApplyClothPanel`, previously laundry-only); the island
  platform and empty centre disc are paved (`CylinderCapXZ`), while
  the inner route ring stays a flat painted marking band.
- The generator specs now transcribe the island tints (island paving,
  nightlife frame/waste/seat, rag and poster colours), so every sheet
  compensation re-solved: paving `1.422 -> 1.4205`, painted metal
  `1.4465 -> 1.479`, cloth `1.396 -> 1.4105`, timber `1.433 -> 1.445`,
  paper `1.4215`, all within the `8%` generator limit (worst `7.6%`,
  cloth). Existing sheet images are byte-identical — only tints and
  compensations moved in `ArtSource/City/poi-textures.json`.
- The island gained the second authored POI realtime light: an old
  service floodlight bracketed off the route mast under the broken
  totem (head at recipe-local `-2.40, 4.42, -1.05`), aimed across the
  empty centre at the empty bench. Cold violet-grey white
  (`0.80, 0.74, 0.92`) — the district's magenta/cyan bleached to a
  service tone, deliberately distinct from the drying yard's blue-white
  — same shadowless `72°` Spot family, range `16`, night intensity
  `150`, fog halo and boosted HDR lens, night-scaled through
  `CityNightSiteLightRegistry` so nothing electric burns by day. It
  adds no collider: the mast base already owns the island obstacle,
  so approach clearance is untouched. The Home vista rebuilds
  bracket/housing/lens geometry only. Documented worst-case realtime
  light budget moves `19 -> 20` (night only). Art bible: the island's
  "no glow of its own" is now scoped to neon — the mast floodlight is
  the one working electric fixture, serving the emptiness, not a
  stage.
- Verification: `python tools/build-city-poi-textures.py --verify`
  passes all five sheets. Focused Unity EditMode selection green
  `18/18` (`CityPointOfInterestSurfaceAppearanceTests` incl. new
  island coverage counts and island floodlight contract +
  `LastRouteCanopyRagTests`). PlayMode POI assertions updated — the
  public places carry the two named floodlights with their two halo
  particle systems, and the island's zero-emissive rule is now "the
  mast floodlight lens is the island's only emissive surface" —
  `CityNightPresentationPlayModeTests` re-run green `4/4`.

## 2026-08-16 — The swing is probed, not guessed: backswing over the shoulder

- Two in-game reviews in a row caught the beat swinging wrong — first
  into her own skirt, then "chopping" — because the arm keys were
  authored by analogy with other designs' poses, and the analogies
  lied: the pipeback "rim reach" actually puts hands down-back on the
  wheel rims, not forward. The fix was to stop guessing: a scratch
  Blender probe now loads the real generator, applies candidate
  `BonePose` keys to the built babushka rig and prints the world
  position of `hand.R` plus the rigid beater's world direction and
  tip. Six probe rounds established the rig's actual upper-arm axes
  (local X raises the sideways A-pose arm, local Z swings it forward,
  local Y twists the paddle) and produced verified keys, now recorded
  as coordinates in the pose comments.
- The final cycle: backswing `(70, -70, 12)` with a `-30°` wrist twist
  — hand beside the ear at `(-0.55, -0.36, 1.32)`, paddle swept back
  over the shoulder (direction `+0.90` on +Y, tip behind the back at
  height `1.29`) — then the forward whack `(18, 35, 60)` landing the
  hand at `(-0.59, -0.41, 1.31)` with the paddle pointing almost
  straight forward (`-0.96` on -Y), tip `0.94 m` in front at carpet
  height `1.16`; recoil bounces off the cloth and the lift arcs back
  up through the front. Art, prefab and tests re-verified
  (EditMode `26/26`).

## 2026-08-16 — The beat lands on the carpet, the carpet answers

- In-game review caught the first babushka cut whipping herself: the
  beater was authored hanging straight down (the A-pose width gate),
  so the strike folded it back into her own skirt. The carry
  direction is now forward-biased `(0, -0.6, -0.8)` — still inside
  the `1.65 m` envelope because it leans into -Y, not X — and the
  beat keys gained a real wrist snap (`hand.R` X `-42° -> +34°`), so
  the extended paddle lands out in front, on the carpet. Verified on
  the regenerated contact sheet.
- The carpets now answer the blows: in the city each is a simulated
  cloth panel pinned over the rack bar (6x6, stiff, damping `0.82`,
  textured with the Home rug albedo over its plain panel UVs, plus a
  static fold cap on the bar), registered in the new named-slot
  `CityDryingYardCarpetRegistry` and deliberately outside the
  weather-wind registry — heavy pile does not flap like laundry. Each
  beater's presentation fires a `0.16 s` decaying
  `externalAcceleration` pulse away from her whenever her loop
  crosses the authored strike moment (`0.28`), so the exact carpet
  she faces shudders under every whack, in her own rhythm. The
  balcony vista keeps cheap static carpet boxes.
- The third babushka no longer stands far off at the east edge: she
  strolls a cloth-free corridor between the rack and the west
  drying-frame posts, back and forth past both beaters at `0.36 m/s`
  with a smooth `220°/s` turn at each end. `BabushkaSmoke` was
  re-authored from a stationary `8.5 s` watch into a `4 s` four-step
  shuffle under emphatic left-arm talk — palm-open sweep, inward
  chop, open again — with the cigarette held ready at chest height
  and one drag per lap. The stance API now emits her corridor
  (`TryDescribeBabushkaStances` gained the path end), the plan
  carries per-stance path/speed/carpet wiring, and the stance tests
  sample the whole corridor against every yard obstacle.

## 2026-08-16 — Three babushkas populate the drying yard

- The drying yard gained its authored population: two grandmothers
  beat hung carpets with the classic Soviet plastic beater and a
  third stands apart at the east edge, smoking and watching. They
  are staged NPCs in the rider's mould — outside the pedestrian
  pool, colliderless with `PlayerAttentionMagnet`s, always present
  while the City lives.
- Art: one new `yard_babushka` archetype in
  `tools/build-city-pedestrian-3d-model.py` (seed `715233`, staged,
  38 meshes / 928 triangles, budget `900-2000`): housecoat, apron,
  skirt, rust headscarf whose folded crown owns the exact `1.75 m`
  envelope, felt boots — and both hand props on `hand.R`: the bright
  plastic beater (authored hanging straight down, because the A-pose
  envelope allows barely 5 cm past the fingertips on X) and a
  cigarette along the canonical `SOCKET_Cigarette.R` axis. The
  runtime enables exactly one prop per role.
- Two new authored loops join the shared locomotion FBX (16 -> 18
  Actions): `BabushkaBeat` (`1.5 s` — ear-height wind-up, forward
  rim-reach strike into the carpet, rocking recovery) and
  `BabushkaSmoke` (`8.5 s` — left arm folded under the right elbow,
  raise, held drag, chin-up exhale, weight shift). Both keep the
  feet planted and ride the ordinary walker sole bake. The first
  strike cut swung sideways out of the A-pose; the corrected keys
  reuse the pipeback rim-reach fold that provably lands hands
  forward. Verified on the regenerated locomotion contact sheet.
- The recipe grew the Soviet carpet-beating rack on the west strip,
  upwind of the wash: two painted-metal posts, a crossbar and two
  hung carpets textured with the shared Home rug albedo
  (`HomeSurfaceKind.Rug` — a hung carpet is the same object indoors
  and out), all with obstacle colliders proven outside every access
  approach. New `TryDescribeBabushkaStances` mirrors the bench-seat
  contract so the NPC plan and the drawn carpets can never drift.
- Runtime: `DryingYardBabushka{Provider,Plan,Presentation,Factory}`
  under `City/Yard` — pure stance plan off the POI descriptor
  (safe-absent for custom blueprints), one-clip manual PlayableGraph
  per instance with per-instance palette variant, playback speed
  (`1.0/0.91`) and phase offsets so the two beaters never strike in
  lockstep; spawn from `CityGameRoot` after the rider. Editor:
  staged descriptor + importer registration + `Rebuild Staged Yard
  Babushka` menu whose build also creates/rewires
  `Resources/City/DryingYardBabushkaProvider.asset`, closing the
  rider pipeline's manual provider-binding gap. The staged manifest
  validation was split from the wheelchair-specific checks it
  wrongly bundled.
- Verification pending Unity access (the editor was open through
  this session): the Blender build validates and is deterministic;
  runtime/editor/test assemblies compile; `DryingYardBabushkaTests`
  (stances inside the yard, opposed desynchronized beaters, watching
  smoker, rack presence + rug texture, approach- and stance-clear
  colliders, provider contract) plus the updated
  `CityPedestrianRuntimeTests` clip census (`16 -> 18`) need one
  EditMode run after the editor closes, and the editor must build
  the staged prefab (auto-queued or via the menu) before the
  provider test passes.

## 2026-08-16 — POI surface textures and the drying yard floodlight

- Four scripted opaque POI albedos join the facade/home/supermarket
  texture family: `tools/build-city-poi-textures.py` (importing the
  shared home pipeline) emits yard paving slabs (new `poi_paving`
  grammar), painted metal, laundry cloth (`linen`) and worn timber
  (`planks`) into `Assets/Resources/Textures/CityPoi*Albedo.png`,
  with the measured contract in `ArtSource/City/poi-textures.json`
  (compensations `1.422/1.4465/1.396/1.433`).
- `CityPointOfInterestSurfaceAppearance` (hash salt `5000`) applies
  them through property blocks on the shared primitive material. All
  four public grounds are paved with their district tints; the drying
  yard is textured end to end — frames, lines, posts, bench legs and
  floodlight metalwork on painted metal, the bench seat on timber,
  and the simulated laundry through a new `ApplyClothPanel` path that
  keeps the shared two-sided cloth material, matte specular and
  metre-tiles the panel's authored width/height (a new
  dimension-explicit `SurfaceAppearanceCore.CreateBaseMapTransform`
  overload, since skinned panels have no `MeshFilter`).
- The drying yard gained the one authored POI realtime light: a
  communal floodlight on its own `4.3 m` pole at the street-side
  corner opposite the shared bench (recipe-local `4.10, 4.55`),
  aimed across all three drying frames — a cold near-white
  shadowless `72°` Spot, range `16`, night intensity `150`, with fog
  halo and a boosted HDR lens that dies by day. The first cut ran at
  street-practical intensity `34` and read as unlit in game: spread
  over a `72°` cone with a `7-12 m` throw that is under half a street
  lamp's pavement level, invisible through the night grade, fog and
  PS1 composite (the always-on bar-side yard spot needs `240` for the
  same reason). Floodlight wattage is the honest unit for a beam this
  long. New
  `CityNightSiteLightRegistry` (glow-registry pattern, wired into
  `CityNightWorldResult.SetNightFactor`) scales the light and halo
  with the shared night factor and disables them below `0.02`, so
  nothing electric burns by day; the always-on bar-side yard
  spotlight deliberately stays outside it. The lower pole owns a
  focused obstacle collider proven outside every access approach;
  the Home vista rebuilds pole/head/lens geometry only. Documented
  worst-case realtime light budget moves `18 -> 19` (night only).
- `LastRouteCanopyRagTests` was stale from the laundry-cloth commit
  (it required every POI cloth to be a canopy rag, but the drying
  yard has hung simulated laundry since then and the test was not
  re-run); it now admits exactly the two cloth families.
- Verification: EditMode `14/14`
  (`CityPointOfInterestSurfaceAppearanceTests` — recipe/import/PNG
  contract incl. compensation-vs-builder-tints, apply path, salt
  separation from supermarket, per-site paving + drying-yard
  coverage counts, floodlight contract with night-factor
  scaling/disable and approach-clear pole collider, light- and
  collider-free vista + `LastRouteCanopyRagTests`).
  `CityNightPresentationPlayModeTests` POI assertions updated (the
  public places now carry exactly one Light — the floodlight — and
  only its halo particles) and re-run green `4/4`. The full EditMode
  suite's 14 unrelated failures reproduce identically on a clean
  HEAD stash and predate this change.

## 2026-08-16 — Cloth and wind: torn rags on the broken canopy

- Unity's built-in cloth entered the project the honest way: the
  `cloth`/`wind`/`physics` modules were already in the manifest, and
  since PhysX cloth ignores `WindZone` entirely, the wind is our own
  deterministic schedule instead. `GameWeatherRules` grew a pure
  `WindSample` path (slot-hashed bearing with the same smoothstep
  transitions as rain, strength from the slot's weather kind
  `0.15/0.40/0.65/0.95`, continuous seeded gusts at `7.3/1.9` game
  minutes and a `±9°` sway at `3.1`), sampled by
  `CityWeatherController` every frame before the visual-equivalence
  early-out, exactly like lightning.
- `ClothPanelFactory` builds skinned cloth panels at runtime — the
  project's first runtime `SkinnedMeshRenderer`: terrain-idiom mesh,
  one root bone, top row pinned through `ClothSkinningCoefficient`
  (`maxDistance` capped at `0.35 x height` as the explosion clamp)
  and torn hems as a pure hash of the variant.
  `CityClothWindRegistry` (glow-registry pattern) turns the wind
  sample into `externalAcceleration` (`7.5 m/s²` at full strength)
  plus gust/lift `randomAcceleration`.
- Double-siding was rebuilt after an in-game report of sparkling
  rags: the first cut duplicated reversed triangles over the same
  vertices, but cloth recomputes particle normals from EVERY
  triangle each frame, so the opposing windings cancelled the
  normals into glinting garbage. The simulated topology is now
  strictly single-sided; the back face renders through one shared
  cull-off clone of the primitive material (per-panel colour still
  on the MPB, smoothness/metallic zeroed so live cloth normals never
  catch specular).
- Six authored rags now hang from the Last Route island's broken
  canopy (city build only — the home-exterior vista stays
  cloth-free), and `CityRainField.SetWindDrift` replaces the
  hardcoded `x = 0.4..1.0` drift so rain in City and on the balcony
  leans the same way the rags blow.
- Two batch-mode traps burned and documented: cloth pauses while its
  renderer is culled, and in `-batchmode` a camera only truly renders
  into a `RenderTexture` — the simulation PlayMode test needs both;
  and `cloth.vertices` reports authored particle rest poses, so live
  deformation must be read via `SkinnedMeshRenderer.BakeMesh`.
- Verification: EditMode `19/19` (wind rules determinism/range/
  storm-vs-clear/boundary smoothness, factory mesh/pinning/torn
  variants, island rag presence + vista exclusion + registry count,
  rain drift alignment); PlayMode `2/2` — free hem moves, pinned row
  holds, all vertices finite. Storm-strength captures (removed after
  use) show five canopy segments with crumpled, wind-thrown rags and
  no geometry explosions.

## 2026-08-16 — Booth seating, booth scale and the jukebox move

- Three placement bugs fixed together. The seated-pair anchors sat
  at x −8.25/−8.75 — inside the booth tables (−8.77..−7.59) or the
  gap beside the bench (−10.64..−8.92) — and their z values missed
  the authored booth centers by up to 1.3 m; the pairs now sit ON
  the bench (x −9.7, z = booth center ± 0.55) facing the table.
- The booths themselves were furniture for giants: seat depth
  1.72 m and a 1.55 m back, with the cushion topping out at 0.77
  against the guests' 0.46 seat height. Now: one-seat-deep bench
  (0.78), banquette back (0.95), cushion top at ~0.47 so seated
  pelvises actually rest on it.
- The jukebox had been planted at (−9.72, 3.15) — the center of
  booth-3's footprint, unreachable inside the bench. It moved to
  the front wall east of the entrance (6.4, −6.78), rotated to face
  the hall, with its approach asserted inside walkable bounds.
- Verification: bar layout planner + surface suites `22/22`, bar
  smoke `1/1`; captures (removed after use) show pairs seated on
  the cushions at the tables and the glowing jukebox free-standing
  and approachable by the entrance.

## 2026-08-16 — Bar hall relight: the readability pass

- The first relight was too timid; the hall floor still sank to
  black. Three compounding causes fixed decisively: the bar's own
  post grade ran the same trap the Home grade once did (negative
  exposure −0.05 under contrast +9 — now +0.30 over contrast 5,
  vignette eased), the scene ambient/fill was shy (ambient to
  `(0.28, 0.20, 0.17)`, directional `0.72 → 0.95` at shadow
  strength `0.42`), and the floor albedo was a 5% mirror of nothing
  (`0.095 → 0.14` red-brown, worn-plank sheet regenerated with the
  new tint, compensation `1.485 → 1.4575`).
- Verification: bar surface + identity EditMode `9/9`, bar smoke
  and drink-service integration green, and the Home atmosphere
  fixture re-proved `2/2` standalone (its two batch failures were
  audio-listener log leakage between scene loads, not lighting).
  Before/after captures (removed after use): the plank floor now
  carries the pendant pools, wallpaper walls and every guest read
  across the hall, and the noir palette survives.

## 2026-08-16 — «Огонёк»: the Residential bar authored

- The bar by the hero's home has a name: «Огонёк» ("The Ogonyok"),
  replacing the literal placeholder. It is the first fully authored
  district identity — a bar for people without money.
- Texturing: `tools/build-bar-textures.py` emits four validated
  sheets entirely on existing home grammars (trodden planks, old
  wallpaper, tired dark veneer, upholstery rubbed to the weave);
  `BarSurfaceAppearance` (salt 4000) applies them and
  `BarInteriorWorldBuilder` dresses the floor, all five walls, the
  counter and its panels, the backbar, booth bases/cushions/backs
  and the stage — but only when the identity asks for the Worn
  surface set. Other bars keep flat tints untouched (asserted).
- Lighting: the bar scene gets the Home readability rule tuned
  darker (ambient floor ~×1.8, shadow strength 0.72 → 0.52) and the
  counter pendants now burn through the district identity — the
  «Огонёк» runs its bulbs a step warmer and 10% dimmer; the other
  identities keep the exact authored amber.
- New furniture: the coin jukebox by the stage in every bar — arched
  corpus, glowing amber panel, two glow tubes, speaker grille and a
  key row, with `BarJukeboxInteraction` as the interactive stub
  (prompt, use counter, panel flash, confirm cue; track selection
  over `BarMusicPlayer` is a later pass).
- Verification: bar surface contract, district identity and
  localization suites `16/16`; bar smoke + drink service
  integration `2/2`; temporary captures (removed after use) show
  the wallpaper walls, warm dim pendants, seated booth guests and
  the glowing jukebox with a patron standing at it.

## 2026-08-16 — Bar district split: the technical base

- The plumbing for per-district bar identities, values deliberately
  unchanged until the art passes author real differences.
  `BarDistrictIdentityCatalog` serves a `BarDistrictIdentity` per
  bar district (mood per the zone art bible — Memory / Household /
  AfterShift / Escape — display-name localization key, palette and
  light hooks, crowd density scale); every other district
  normalizes to the Nightlife fallback the direct-loaded bar has
  always effectively been.
- The district flows the whole way: `BuildingLot.District` →
  `BarEntrance.Configure` → `GameSessionState.EnterBar` (new
  `ActiveBarDistrict`, reset with the other bar state on
  home/supermarket entry and new game) →
  `BarInteriorLayoutPlanner.Generate(..., district)` →
  `BarInteriorLayoutPlan.District` / `.DistrictIdentity`, with the
  layout validator refusing non-bar districts. Four
  `bar.district.*` name keys landed in both localization catalogs.
- Verification: `BarDistrictIdentityTests` (catalog coverage and
  distinctness, normalization, plan threading incl. the legacy
  entry point, session lifecycle) plus localization and bar layout
  suites passed `28/28`; the bar smoke passed `1/1` on the
  fallback path.

## 2026-08-16 — The bartender pours: service pass landed

- Pass 3 of the bartender spec. The bottle never flies to the hero's
  hand anymore: `BarDrinkShopController` retires the first-person
  right-arm grip (the hero keeps only the left-hand drink lift) and
  carries the committed bottle from its shelf to the authored
  `BottlePourPose` with a small lift arc — the same timeline
  channels, a different destination. The vessel no longer scale-pops
  at the counter: it slides in flat along the brass from past the
  left edge of the seated frame (`VesselSlideEntryOffset`,
  `VesselVisibility` is the slide) before the pour fills it.
- `BarBartenderServiceChoreography` puts his hands on all of it:
  the brass-banded mid-right chain CCD-rides the carried bottle,
  the mid-left chain rides the sliding vessel and steadies it
  through the pour, and while the hero merely browses, the lower
  pair reaches back and fingers whichever bottle is hovered —
  arms as readers of the authored motion, never drivers. Idle
  amplitudes roughly doubled after the first pass proved invisible
  at hall distance.
- Verification: drink-service integration and the bar smoke passed
  `2/2`; the three `BarDrinkPhysicalShopPlayModeTests` failures were
  proven pre-existing by rerunning them on a clean stashed HEAD
  (NUnit `Has.Count` against the array-backed
  `player.Visual.Renderers` — unrelated to this pass). Temporary
  captures (removed after use) show the vessel mid-slide down the
  counter and the filled wine glass with the bartender's arm on it
  at mid-pour.

## 2026-08-16 — Bartender reads from the hall

- The two-metre rebuild alone was not enough: behind the ~1.56 m
  brass counter top only his pale head cleared the line and, point
  for point, read as one more backbar bottle (an isolation render
  proved the prefab itself was fine — the camouflage was the bug).
  Three coordinated fixes make him legible from anywhere in the
  hall: the model grew to a cashier-class 2.0 m with the long neck
  stub, he now works from a 0.42 m service duckboard so the
  shoulders and the whole extra-arm fan clear the counter, and the
  center counter pendant re-hung directly over his board so the
  head and moustache catch warm light against the dark backbar. The
  waistcoat palette brightened a step, the anchor moved beside the
  hero's counter station, and the canonical hands now rest ON the
  counter top (root-local rest points ride the duckboard height).
- Verification: bartender asset contract `1/1`, bar layout planner
  `17/17` with the moved anchor and pendant, bar smoke `1/1`;
  iterative temporary captures (removed after use) confirmed the
  hall sightline finally shows a lit face, cap and moustache above
  the counter instead of an anonymous bottle.

## 2026-08-15 — Six-Armed Bartender: model pass and bar presence

- Passes 1–2 of [`ai/bartender-spec.md`](bartender-spec.md) landed.
  `tools/build-bartender-3d-model.py` (Blender, subclassing the
  shared `PedestrianBuilder` like the cashier tool) builds the
  publican on the exact canonical 31-bone skeleton: broad torso,
  waistcoat/apron/flat cap/moustache, and two extra arm pairs as
  twelve rigid segments on sixteen `PIVOT_Arm{2,3}.{L,R}.*` empties
  (the cashier-neck/wheelchair mechanism) plus the brass band on the
  mid-right pouring arm. 50 meshes, 1436 triangles of the 3400
  budget; FBX + manifest + preview under `Assets/Bar/Bartender` and
  `ArtSource/Bar/Bartender`.
- The C# pipeline mirrors the cashier end to end:
  `BarBartenderModelImporter`, `BarBartenderAssetSetup` (manifest
  contract validation, prefab build, provider binding),
  `BarBartenderAssetRegistry` with the four serialized arm chains,
  and the addressable `BarBartenderProvider`.
- `BarBartenderPresentation` re-parents the chains under their
  pivots beneath the chest, folds the canonical pair to a counter
  rest via world-space two-bone solving (imported FBX bone axes are
  not trustworthy for local Euler folds — the first capture proved
  it), runs desynchronized per-chain idle business and head sway,
  and already exposes `SetChainTarget` CCD reaching for the service
  pass. `BarBartenderWorldBuilder` stands him on the authored
  Bartender anchor facing the hall (the sprite-era anchor yaw runs
  along the service alley); `BarInteriorRoot.Bartender` exposes him.

Verification:

- The Blender build validates the full contract (canonical skeleton,
  16 pivots, part markers, budget, grounding) and the Unity batch
  `BuildOrThrow` build passed including its own post-build
  validation. `BarBartenderAssetTests` EditMode passed `1/1`;
  the bar smoke test with new bartender assertions passed `1/1`.
- Temporary D3D11 captures (removed after use) were visually
  inspected across compass views: he stands the service alley at
  the counter, faces the guests, the canonical hands meet over the
  counter and the extra-arm fan reads at PS1 resolution.
- Remaining per the spec: pass 3 (service choreography — hover
  touch, carry, steady) and pass 4 (cocktails).

## 2026-08-15 — Six-armed bartender spec authored

- Wrote [`ai/bartender-spec.md`](bartender-spec.md): the design for
  the dedicated 3D bartender pass — a three-pair-armed figure on the
  cashier model pipeline, six independent CCD arm layers over a
  manually-advanced idle, service choreography where the authored
  `BarDrinkServiceTimeline` channels keep driving the bottles while
  the bartender's hands visibly touch, carry and steady them, and a
  2–3 ingredient cocktail order model with per-ingredient arms and a
  simultaneous bottle-return finale. Four independently-green build
  passes; nothing implemented yet.

## 2026-08-15 — Grocery lettering and the hero home anchor

- The supermarket signs now spell. `CitySignLettering` is a pure
  blocky segment font (П Р О Д У К Т Ы plus the house digit) laid out
  on a facade plane; the storefront band replaces its five anonymous
  glowing blocks with the word `ПРОДУКТЫ`, and a new vertical blade
  sign hangs off the storefront corner — one glyph per row, lettered
  on both street faces with per-face mirroring so the asymmetric
  glyphs always read forward. Both signs ride the shared glow
  registry and the home-exterior clipping path.
- The hero's building is now findable: a warm entrance lamp under a
  small canopy, the lit deep-blue house-number plaque (`7`) beside
  the door, and a rooftop antenna mast with a `0.3 m` red beacon
  (`2.3` HDR red, `~3.5 m` above the roof) that survives the city fog
  from blocks away. Everything registers with the night glow
  registry, so it dims by day with the rest of the city.

Verification:

- `CitySignLetteringTests` EditMode passed `3/3` (word layout bounds
  and centering, per-cell scaling determinism, glyph coverage
  including the house digit, unknown-glyph rejection).
- A temporary City-scene capture (removed after use) was visually
  inspected across four street viewpoints: the storefront word reads
  head-on, the blade sign reads top-down through fog from down the
  block, the entrance lamp and plaque mark the door, and the red
  beacon shows above the roofline from `26 m` away.
- Sign/anchor object assertions were added to the City smoke test;
  the suite currently fails earlier on the pre-existing `12x12`
  envelope expectation, which the in-flight `17x14` city expansion
  on this branch has not yet updated — unrelated to the signage.

## 2026-08-15 — Supermarket surface textures and fluorescent lighting

- The supermarket hall now carries real packaged albedos instead of flat
  tints. `tools/build-supermarket-textures.py` imports the entire home
  texture contract (linear luminance rule, wrap-by-construction drawing,
  compensation solving, validation) from `build-home-textures.py` and
  adds three market grammars — worn 4x4 linoleum squares with traffic
  scuffs, suspended ceiling panels over whitewash, corrugated cardboard
  with a tape band — reusing the home stucco / painted-metal / laminate
  grammars for walls, shelving and the counter. Six validated 1024
  sheets live in `Assets/Resources/Supermarket/Textures`, the measured
  contract in `ArtSource/Supermarket/supermarket-textures.json`.
- `SupermarketSurfaceAppearance` mirrors the home appearance class
  (metre-scale projected tiling, compensated display tint, hash salt
  3000) and the world builder resurfaces every big surface: floor and
  patches, all five wall segments plus the entrance header, ceiling,
  gondola frames/backings/tiers, the cold case, the checkout base and
  trim, and the stockroom cartons. Decals, stripes and small props stay
  deliberately flat.
- Lighting got the Home readability treatment: scene ambient rose from
  `(0.078, 0.098, 0.083)` to `(0.21, 0.25, 0.225)`, the directional
  fill `0.36 -> 0.72` with shadow strength `0.58 -> 0.45` so the
  ceiling-shadowed key survives indoors, fluorescent rows
  `1.05 -> 1.45` (range `8.4`), the checkout warm accent
  `0.78 -> 1.05` and the cold-shelf spill `0.55 -> 0.75`. The tired
  ballast flicker row is untouched.

Verification:

- The generator validates all six sheets (seam, mean luminance,
  compensation cap, contrast, chroma; worst brightness error `4.9%`).
- Focused EditMode passed `17/17`: the new
  `SupermarketSurfaceAppearanceTests` contract (recipes vs the
  generated constants, importer settings, tint compensation against
  the builder palette measured on the real PNGs, and a world-builder
  audit that all six sheets land on the hall), plus atmosphere and
  layout suites. `SupermarketPurchasePersistencePlayModeTests` passed
  `3/3` end-to-end on the textured scene.
- A temporary PlayMode capture (removed after use) rendered the live
  scene from the gameplay camera and an eye-height aisle view: the
  linoleum grid, wall mottle, ceiling panels, shelf metal, cartons and
  counter all read, the aisles and the hero stay legible between the
  fluorescent pools, and the green noir palette survives.

## 2026-08-15 — Returned the wheelchair yard to the bar

- Re-anchored the authored wheelchair yard to the bar directly across the
  player home's shared street frontage, then selected only its roadless left
  side. The resulting walkable gap lies between that bar and the neighbouring
  supermarket; the five typed fringe yards remain unrelated and undecorated.
- Split the narrative `PlayerHome` owner from the physical bar anchor in the
  shared site contract. The circuit dressing and leaning phone booth/dumpster
  now follow the bar, while the existing sampled spotlight mounts flush to the
  supermarket's yard-facing wall and covers the complete rider circuit.

Verification:

- Focused Unity EditMode regression
  `DefaultCity_DressesTheRoadlessGapDirectlyLeftOfABar` passed `1/1`.
  Broad suites and a player build were intentionally not run in fast mode.

## 2026-08-15 — Continuous city terrain and traversal audit

- Replaced the default city's isolated Buildable/Park/Open/Beach cell slabs
  with one sampled continuous-top contract and triangulated mesh colliders.
  Beach cells now share a canonical waterward profile; the lake is a local
  elevated basin instead of a deep pit to the global water datum.
- Road/ground and ground/ground connectors and guards now classify the same
  sampled physical edge. Unsafe guards follow the slope in segments and own
  retaining collision; park plazas conform to the terrain, district public
  places receive flat pads with `4 m` blends, and building foundations extend
  down without moving their authored tops.
- Added the deterministic `CityVerticalTraversalPlan` seam/frontage audit and
  fixed all eight river-park gates: their former centers sat over internal
  `ParkPath` corridors and left only `0.4 m` lawn slivers, so they now occupy
  capsule-wide, step-safe cell frontages.

Verification:

- Focused Unity EditMode category `CityTraversal` passed `7/7` on production
  seed `20260727`. Unity also compiled the shared runtime, EditMode and
  PlayMode assemblies in that invocation; broad suites and a player build were
  intentionally not run in fast mode.

## 2026-08-15 — Bar patrons drink from the bar's own bottles

- Bar guests now visibly drink. `BarPatronDrinkTimeline` (pure,
  seeded) loops Rest → Raise → Sip → Lower with per-patron randomized
  rests (`3.5–9.5 s`), sips (`1.1–2.2 s`) and an initial stagger so
  the crowd never moves in unison. `BarPatronDrinkingArmPose` is the
  procedural additive layer atop the authored Idle/Sit loops: each
  LateUpdate it captures the animated right arm, CCD-steers the held
  bottle's mouth onto the pedestrian `SOCKET_Mouth` anchor, tips the
  bottle up to `38°` with a `7°` head-back counter-tilt and slerps by
  the timeline weight — the teeth-brushing/bus-driver idiom.
- The bottles are the bar's own: `BarDrinkServiceWorldBuilder` exposes
  `BuildBottleVisual`, rebuilding the exact shelf silhouettes
  (beer longnecks, vodka, pepper vodka, cognac — picked by seed) as
  hand-scale props (`0.42×`) riding the canonical `SOCKET_Bottle.R`,
  gripped at `45 %` of bottle height, neck up. Every third guest stays
  deliberately empty-handed; a seeded ~30 % of sips play the existing
  `DrinkGulp` retro SFX at the lips so the room murmurs, not gurgles.
- `BarPatron` exposes the optional `Drinking` layer; designs missing
  the canonical sockets log and simply hold nothing.

Verification:

- Focused `BarPatronDrinkTimelineTests` EditMode passed `3/3`
  (cadence bounds, per-seed determinism plus cross-seed stagger, gulp
  one-shot discipline). `SceneFlowSmokeTests.BarInteriorScene_…`
  passed `1/1` with new assertions: some guests drink, some don't,
  and every held bottle is a visible prop riding the guest's hand.
- A temporary D3D11 RenderTexture capture (removed after use, per
  convention) was visually inspected: mid-sip the bottle mouth sits
  at the lips (asserted `< 0.12 m`), the bottle tips toward the face,
  the rest pose leaves the authored idle untouched. Batch-mode note:
  the capture rig must force `AnimatorCullingMode.AlwaysAnimate` —
  with no live cameras the culling-driven pedestrian animator holds
  the bind pose.

## 2026-08-15 — Brush-tip contact and interruptible bathroom scenes

- The brushing CCD now steers a `Brush Tip` effector anchored at the
  toothbrush bristles instead of the RightGrip socket, so the brush head —
  not the gripping fist — works the mouth in the mirror close-up. The
  mouth forward offset dropped from `6 cm` to `1.5 cm` to suit the new
  effector; `HomeTeethBrushingArmPose` falls back to the grip when no
  effector is assigned.
- All three bathroom scenes are now interruptible from any pre-wind-down
  phase via the shared stop input. Timelines keep visual continuity on
  abort: the teeth/toilet cameras walk home scaled from their actual
  blend, the shower curtain reverses from its current scale and water
  fades from its current amount. A stop during brushing still passes the
  rinse beat; a toilet abort during the camera retreat suppresses the
  flush. Foam no longer pops in during a rinse that never brushed past
  the foam threshold.
- The minimum times (`4 s` brush, `6 s` hold, `2.5 s` privacy) now gate
  only the stress reward — an early interrupt ends gracefully, commits
  nothing and leaves the once-per-day teeth gate unconsumed. The base
  `OnRequestStop` returns acceptance, fixing a latch where a refused stop
  press set `StopQueued` forever and swallowed all later presses. The
  toilet gained a visible stop prompt (`interaction.stop_toilet`, en/ru).

Verification:

- Unity `6000.5.5f1` batch compile passed with no compiler errors.
- Focused EditMode (bathroom timelines + localization catalog) passed
  `18/18`, including new coverage for aborts before the minimums, camera
  blend continuity on interrupt and the suppressed abort flush. Focused
  `HomeBathroomInteractionsPlayModeTests` passed `3/3`.

## 2026-08-15 — Readable home interior lighting floor

- Raised the Home interior readability floor that left most of the flat and
  the moving player nearly black. `HomeDayNightController` ambient rose about
  threefold (day `0.26/0.235/0.205`, night `0.145/0.14/0.17`), the interior
  directional fill went from `0.44/0.22` to `0.85/0.42` (day/night), and
  `RuntimeSceneSetup.EnsureHomeInterior` now uses shadow strength `0.45`
  instead of `0.62` so the ceiling-shadowed directional survives indoors as
  usable fill; its bootstrap ambient matches the new floor.
- Extended the main practical lamp range from `6 m` to `9 m` so its
  inverse-square falloff reaches the far walls of the roughly `9 x 7 m` flat,
  and the entry-door light from `4 m` to `5.5 m`. The day lamp intensity rose
  `2.30 -> 2.90` and night `4.10 -> 4.40`; the window key light is unchanged
  so the day/night window hierarchy stays intact.
- Lifted the interior grade out of compounding darkness: post exposure
  `-0.08 -> +0.25`, contrast `7 -> 5`, a lighter color filter and vignette
  `0.24 -> 0.18`. Bloom, grain and saturation are untouched.
- Fixed a pre-existing `CS0177` in `CityTerrainSurfacePlan` corner-elevation
  short-circuit that blocked batch compilation.

Verification:

- Unity `6000.5.5f1` batch compile passed with no compiler errors.
- Focused Home PlayMode: `HomeInteriorAtmospherePlayModeTests` passed `2/2`
  with updated expectations (lamp range `9`, entry range `5.5`, positive
  exposure). Three scene tests failed on this branch's unrelated
  work-in-progress: balcony street-lamp colliders, pedestrian count `8 != 5`,
  and a bathroom-lamp viewport framing check at `-0.08` — none are affected
  by light intensity, ambient or grade values.

## 2026-08-14 — River fence ownership and stair access correction

- Corrected the post-river collision conflict between `RoadFencePlanner` and
  `CityRiverWorldBuilder`. Declared river bridges are now support-only inputs
  to the generic road-boundary planner, while both promenade bounds support
  their adjoining bank roads. Generic colliders no longer duplicate the
  authored bridge parapets or close their four stair gaps.
- Trimmed the Works, Mouth and timber bridge guards to the inner edges of the
  two `8 m` bank-road pads, including the half-width of their end posts. Decks
  and structural members still meet the road nodes; only the obstructing guard
  geometry is shortened.
- Added river-layout regression coverage for fence ownership and physical
  bridge-guard bounds. The focused `CityRiver` EditMode category passed
  `12/12`; broad EditMode/PlayMode suites and a player build were intentionally
  not run in fast mode.

## 2026-08-14 — North-south river and three bridge hierarchy

- Expanded the default urban envelope from `12 x 12` to `13 x 12` while
  preserving all 144 land-use cells: the new central column is a declared
  north-south river corridor and the eastern city shifts one cell outward.
  `CityRiverDefinition`/`CityRiverPlan` own the `10 m` channel, two `3 m`
  promenades, three typed crossings and elevation-aware geometry descriptors.
- Added two distinct Road v2 bridges at the Works and Mouth edges plus one
  `2.8 m` timber ParkPath footbridge. Central Park remains 16 cells as two
  `2 x 4` regions connected by that footbridge. Route 01 uses both road
  bridges exactly once and never the timber bridge; bus furniture and ambient
  pedestrian spawns stay clear of the reserved crossings. Home keeps its
  frontage-adjacent stop; river-layout POIs use a same-district cyclic Street
  bounded to five grid edges and `120 m` from their public access.
- Built animated night/rain-responsive water, physical upper promenades,
  retaining edges, bridge decks and parapets. Each road bridge has one
  physical stair flight and lower platform on each bank, for four waterside
  landings total. River-proximity audio was not added in this pass.
- Extended the player and pedestrian walkable plans through the embankments
  and declared bridge graph. Updated the City map to draw the river and both
  promenades below roads, then overlay distinct Works, Mouth and planked timber
  bridge styles from the same layout metadata.
- Focused EditMode verification passed all `11/11` tests in the `CityRiver`
  category, covering topology, grades, walkability, physical river geometry,
  pedestrians, Route 01 and the map. Broad EditMode/PlayMode suites and a
  player build were intentionally not run in fast mode.

## 2026-08-15 — Bathroom: rebuilt shower and three modal scenes

- `HomeBathroomBuilder.BuildShower` rebuilt (~25 new parts, tray
  collider and pinned names kept): L-rail over both open sides, an
  animatable four-fold curtain group (pivot at the left front corner,
  gathered `scale.x 0.55` <-> drawn `1.0`) plus a static side run, wall
  mixer with red/blue cross handles and a spout, a four-segment sagging
  hose, riser/arm/neck and a tilted bell head with a dark nozzle plate,
  tray rims, drain, soap shelf. `HomeSurfaceAppearanceTests` palettes
  (`BedLinen += CurtainLight`) and exempt list extended.
- One shared scene skeleton (`HomeBathroomSceneInteraction`): modal
  capture, guided walk-in via `MoveTowardsInteractionPose` (stall →
  cancel), settle frame, Bézier camera from the pinned bathroom shot
  with the smoking drift, debounced stop with release re-arm,
  idempotent restore + `ReapplyActiveShot`, commit only on completed
  walk-out. Three recorded exceptions to the animation standard (no
  new clips — the set is closed): curtained Idle, off-frame Idle,
  procedural CCD arm.
- Toilet: privacy cut to the ajar-door frame (FOV 60), cistern hiss on
  the new shower-water loop at 0.35, one-shot `ToiletFlush` beat at
  3.6 s with the flush handle dipping, stress −6.
- Shower: hero walks into the tray, curtain draws shut, water/steam
  particles (code-built, shared atmosphere material) + crossfaded
  seamless hiss loop (`SetShowerWaterAmount`, 6th owned source,
  counts 5→6/8→9), corner frame FOV 54, min 6 s / auto 10 s,
  stress −12. The bathroom light flicker keeps running through it.
- Teeth brushing: camera from the mirror plane into the hero's face
  (FOV 36), `HomeTeethBrushingArmPose` (order 300, capture-solve-slerp
  CCD of the right arm to the Mouth anchor, 5.5 Hz oscillation, head
  counter-yaw), RightGrip toothbrush + Mouth foam props with the
  cigarette inverse-scale correction, scrub cues every 0.55 s, rinse
  with two Pour beats and a camera dip to the basin, relief gated once
  per game day (`TryCommitTeethBrushingRelief`, reset on new game).
- New SFX `ToiletFlush` (rush → gurgle → refill hiss) and
  `TeethBrushScrub` (two-stroke band noise); +5 localization keys
  (197 each, symmetric).

- **In-game bug and fix:** the first live test showed `E` doing
  nothing at all three spots. Root cause: the scene docks are authored
  at floor level `y = 0`, but the grounded controller root rides at
  `y ≈ 0.12`, and `PlayerMotor.MoveTowardsInteractionPose` demands a
  `2 cm` vertical match to complete — the walk-in arrived planar,
  could never finish, hit the stall timeout and silently cancelled the
  scene. The shared skeleton now grounds every walk target to the
  hero's current height (gravity owns the vertical, tray step
  included) and logs `bathroom_scene_started/rejected/stalled/
  completed` so the next silent failure reads straight out of
  `debug.log`. A PlayMode fixture
  (`HomeBathroomInteractionsPlayModeTests`) replays the exact `E` path
  for all three scenes.

- **Second live-path bug:** the brushing walk-in stalled 1 cm short of
  its dock — the capsule (radius `0.32` + skin width) met the sink
  basin collider at `z 3.25` while the guided walk demands an exact
  planar arrival. The stall diagnostics (player/target coordinates in
  `bathroom_scene_stalled`) pinpointed it; the dock moved to
  `z 2.78`.

Verification:

- Runtime, EditModeTests and PlayModeTests compile with 0 errors.
- Focused EditMode batch (scene timelines + surfaces + home layout +
  localization): 71/72 on the first pass — the one failure was a test
  authoring bug (phase-overshoot in the brushing fixture), fixed;
  the timeline fixture then passed 7/7.
- Focused PlayMode `HomeBathroomInteractionsPlayModeTests`: 3/3 —
  toilet privacy cut commits once, shower draws the curtain, runs
  water and restores, brushing replays with the day-gated relief.

## 2026-08-15 — Apartment lighting follows the session clock

- `HomeDayNightController` grew from window-only to a full indoor mood
  pass, all within the existing five-light budget (no new lights):
  window color gains a dusk amber phase (`1.0/0.56/0.30`, blend `0.65`)
  peaking mid-transition and exactly zero at the test-pinned day/night
  poles; the main lamp swings `2.30 -> 4.10` day to night and deepens
  its orange; the entry spot lifts `8.0 -> 9.4` (the presentation test
  floor of `>= 8` holds at all hours); `RenderSettings.ambientLight`
  and the directional fill lerp warm-bright day to cold-dark night
  (`0.44 -> 0.22`, blue-grey `0.60/0.66/0.82`).
- Balcony discipline: the ambient/sun mood is skipped while
  `HomeBalconyExteriorAtmosphere` has the balcony visibility active
  (the shot borrows City lighting) and reasserts itself on the
  visibility flip back indoors.

Verification:

- Runtime and PlayModeTests compile with 0 errors. Focused PlayMode
  batch: `HomeInteriorAtmospherePlayModeTests` fully green, and the
  balcony test's day-pole lighting assertions (WindowDayFactor `1`,
  exact day window color/intensity) pass before its failure point.
- Two `HomeBalconyPresentationPlayModeTests` failures are pre-existing
  and unrelated to lighting: the collider-free exterior view now
  contains `Street Lamp Chunk` BoxColliders (lamp chunks from the
  committed checkpoint `79572db` are not stripped by the exterior view
  builder), and the balcony pedestrian count is `8` where the test
  pins `5` (population changes from the committed city batches).
  Recorded, not fixed in this lighting pass.

## 2026-08-15 — Cashier neck: whole-hall reach, honest counter avoidance

- The neck's `4.5 m` cap still read as a limit in-game — the head
  stalled short of a hero deep in the aisles. The cap is now `18 m`
  (`MaximumNeckLengthMeters`), enough for every corner of the
  `16 x 11` hall: the face simply always arrives. Tool manifest ratio
  refreshed to `32.7` — geometry untouched, identical signature.
- Neck segments and the head were still visible through counters: the
  single-control quadratic sagged near its endpoints and clipped
  shelf edges the midpoint lift never covered. The solver is now a
  cubic staple — both controls rise to a shared clearance height at
  `t = 0.2/0.8`, so the chain climbs out of the register fast, rides
  above the aisles and descends only at the hero — and the resulting
  curve is re-sampled against every margin-expanded (`0.22 m`)
  shelf/fixture AABB, raising the clearance (up to four attempts,
  ceiling-clamped) until nothing clips.

Verification:

- Runtime compiles with 0 errors; Blender manifest regeneration
  reproduced the same build signature. Focused supermarket EditMode
  batch (cashier state/asset, cameras, atmosphere): 19/19 passed.

## 2026-08-15 — Bigger CCTV and a fluorescent light budget

- The corner cameras nearly doubled: thick `0.13 m` stems, a
  `0.27 x 0.27 x 0.62 m` body with hood, lens and iris, a `0.05 m`
  recording LED, corner inset widened to `0.62 m` and the head dropped
  to `0.50 m` below the ceiling — readable from the shop floor.
- `SupermarketInteriorAtmosphere`: the hall leaves the single flat
  directional behind. Six shadowless practicals — a cold point under
  each fluorescent row (`1.05/7.6 m`), one warm accent over the
  checkout (the only warmth in the hall, pooled on the Watcher
  Cashier), one cool cold-shelf spill — while the directional key steps
  down `0.48 -> 0.36` and remains the only shadow caster. Row two
  flickers on a deterministic `0.11 s` stepped pattern (dips to
  `0.30`), dimming both its light and its fake-emissive tube tint via
  MaterialPropertyBlock. Installed by the interior root right after the
  world build.
- `SupermarketInteriorAtmosphereTests`: the installed budget is exactly
  six lights, none directional, all shadowless, flicker present; the
  flicker pattern visibly dips below `0.9` and returns to `1.0` within
  bounds.

Verification:

- Runtime, EditModeTests and PlayModeTests compile with 0 errors.
- Focused EditMode batch (cashier pursuit state + cashier asset
  contract + CCTV + atmosphere + supermarket layout + open-area
  decorations): 22/23 passed — every supermarket fixture green,
  including the reworked pursuit-state tests.
- The 1 failure is pre-existing and unrelated:
  `CityOpenAreaDecorationPlannerTests
  .DefaultCity_DressesOnlyTheHomeYardWithACircuitAndTraces` finds
  `YardSpotlight.HasValue == false` on the default seed. This batch
  never touches the yard planner; the planner's last change is the
  river/envelope commit `8b84db7` (12x12 -> 13x12), which evidently
  moved the default home yard out of the spotlight condition.
  Recorded, not fixed here — river area.

## 2026-08-15 — Nightlife neon panes dressed with dark glyphs

- The last bare glowing quads among the misc decorations were the
  nightlife neon family: the billboard's two poster panes, the cinema's
  two lightbox one-sheets and the vending machines' front windows, plus
  the vending queue's glowing handrails. Following the phone-booth
  lightbox idiom, every pane now carries dark half-embedded strokes over
  the glow — headline/body/photo blocks on the billboard posters, a
  figure block and title strokes on the movie posters, vitrine grid
  mullions on the machine fronts — so they read as printed backlit
  signage instead of untextured rectangles. The queue handrails became
  painted steel: a glowing handrail at street level was noise, not
  signage. Thin marquee and letter strips stay neon — those are tubes by
  design. Day-night gating is untouched (the glyphs are Street-style
  batches, only the panes remain registered electric glows).

Verification:

- Runtime and EditModeTests compile with 0 errors; deterministic
  recipes changed geometry only, no plan or validator contract moved.
  Focused decoration fixtures pending the editor lock release.

## 2026-08-15 — Corner CCTV cameras track the hero

- `SupermarketSecurityCameraWorldBuilder` hangs four camera units in the
  hall corners, positions resolved purely from the layout plan (half
  room size minus wall minus `0.55 m` inset, `0.42 m` below the
  ceiling). Each unit: ceiling stem, boxy head with hood and dark lens,
  and a fake-emissive red recording LED with shadows off — primitive
  boxes on the shared runtime material, no Collider, no Light, so the
  one-directional-light budget is untouched.
- `SupermarketSecurityCamera` snaps its head onto the hero at
  initialization (never caught pointing at a wall) and then servos at
  `240°/s` via `Quaternion.RotateTowards` in `LateUpdate`; `Track` is
  public so EditMode drives it without a play loop. Built by the
  interior root right after the cashier, tracking the same body
  transform.
- `SupermarketSecurityCameraTests`: the four resolved corners are
  symmetric, distinct and under the ceiling; `ResolveAim` points the
  lens forward vector at the focus and survives a degenerate target;
  a real build under a temp root aims all four heads at a fake hero,
  follows him after `Track`, and stays collider- and light-free.

Verification:

- Runtime and EditModeTests assemblies compile with 0 errors. The
  focused EditMode batch (cameras + cashier state/asset + supermarket
  layout) is still pending: the open Unity editor holds the project
  lock; run it with the editor closed.

## 2026-08-15 — Cashier neck: head reattached, pursuit over the shelves

- In-game check showed the head tearing off the chain: head rotation ran
  around the canonical head bone, which rests `~0.5 m` below the authored
  face, so every pitch swung the skull off the neck. The head is now
  pinned to the curve tip by its authored neck-attachment point captured
  at bind (`InverseTransformPoint`), and rotates around that joint.
- The neck no longer just elongates — it pursues. The five pivots are
  laid along a quadratic curve from the neck base to a hover point beside
  the hero's face (`0.85 m` standoff, `0.25 m` lift), capped at `4.5 m`
  of neck; when the straight line crosses a shelf or fixture AABB, the
  curve's control point lifts above the tallest obstruction `+0.45 m`,
  so the chain arcs over the aisles instead of clipping through them.
  Obstacles and the hall roam box come from the layout plan through the
  root and factory into the presentation.
- `SupermarketCashierSurveillanceState` reworked to a pursuit weight:
  saturates to `1` whenever the hero is present (no more distance
  periscope or `ProximityCrane`), creeps at `0.9/s`, reels back at
  `2.4/s` under the caught-looking startle (cap `0.30`), blink
  suppression unchanged. Tool manifest ratio updated to `8.2`
  (`4.5 m / 0.55 m`) — geometry untouched, signature identical, prefab
  stays valid.

Verification:

- Runtime, EditModeTests and PlayModeTests compile with 0 errors; the
  Blender manifest regeneration reproduced the same build signature.
- The focused EditMode batch (cashier state/asset + supermarket layout)
  could not run this pass: the Unity editor held the project lock.
  Compile-clean recorded; run the fixtures with the editor closed.

## 2026-08-14 — The Watcher Cashier staffs the supermarket checkout

- Authored `tools/build-supermarket-cashier-3d-model.py` on the
  bus-driver pattern: subclasses the pedestrian `PedestrianBuilder`,
  keeps the exact 31-bone Player A-pose skeleton and exports an
  animation-free FBX + manifest (`watcher_cashier_v1`, 44 meshes,
  1588/2200 triangles, resting height `2.05 m`, signature-stamped).
  The design: hunched clerk, tiny head, five `0.11 m` neck segments
  with vertebra rings on `PIVOT_Neck.01..05` empties, a strangling
  collar narrower than the neck, one saturated name tag, enormous
  bulging eye whites (the right 8% larger) with pinprick pupils on
  the `face.eye.*` bones. A standalone validator owns the cashier's
  numbers (the shared one now demands an `ArchetypeSpec`).
- Runtime set under `Runtime/Supermarket/Cashier/`: provider asset in
  Resources referencing the off-Resources prefab (wheelchair pattern),
  registry with bones + pivots + manifest colors, factory with
  passivity guard, `PlayerAttentionMagnet` (2.0 m) and spawn logging,
  and a fully procedural presentation — restore rest pose each frame,
  hunch, CCD palms onto the counter, re-parent segments under pivots,
  fold pivots into a chain off the neck bone, stretch to `2.4x` on
  per-segment shares, serpentine yaw/pitch distribution, head hard on
  the chain tip delta inside a clamp box, pupil darts by bone
  translation (the eye bones sit `0.39 m` below the authored face, so
  rotations would sling pupils off the face), startle pupil pinch and
  `forceRenderingOff` blink.
- Pure logic split for tests: `SupermarketCashierSurveillanceState`
  (periscope `smoothstep 2..9 m`, extend `0.9/s` vs retract `2.4/s`,
  caught-looking hysteresis `cos 22°/0.15 s` in — `cos 30°/0.8 s`
  out, extension cap `0.30`, blink resume delay `1.2 s`) and
  `SupermarketCashierBlinkState` (`6.5 s` cycle, close/hold/open
  `0.09/0.16/0.14`, suppression restarts the cycle).
- Editor pass: `SupermarketCashierModelImporter` (shared Player
  Avatar via CopyFromOther) + `SupermarketCashierAssetSetup` (manifest
  contract incl. `neck_segment_count == 5` and pivot names, prefab
  build with forced shared material and bindings in manifest order,
  provider binding, menu items). Prefab built headless via
  `-executeMethod` and passed its own `ValidateOrThrow`.
- `SupermarketInteriorRoot.BuildCashier()` spawns the clerk on the
  authored `cashier-main` plan anchor after the player exists and
  plants the `E — заговорить` talk stub (booth/dumpster contract) in
  front of the register; +2 localization keys in ru/en (192 each).

Verification:

- Blender 5.0.1 headless build OK (44 meshes, 1588 triangles, 5
  pivots, deterministic signature); preview render checked visually.
- Runtime, Editor and EditModeTests assemblies compile with 0 errors.
- Unity batch `SupermarketCashierAssetSetup.Run` built and validated
  the prefab + provider. Focused EditMode batch:
  `SupermarketCashierStateTests` + `SupermarketCashierAssetTests` +
  `SupermarketInteriorLayoutTests` + `LocalizationCatalogTests` —
  23/23 passed.

## 2026-08-14 — Bar-visited mechanic removed entirely

- Cut the visit tracking from `GameSessionState`: the `visitedBars` set,
  `VisitedBarCount`, `MarkBarVisited`, `IsBarVisited` and
  `ClearVisitedBars` are gone, together with the `bar_visited` /
  `visited_bars_cleared` log events and every `visited_count` field in
  seed/blueprint-change, City-init and F8-snapshot logging. Entering a
  bar no longer touches the planned route — `RemoveRouteStop` stays as a
  manual map edit only.
- The city map lost the green visited marker colour, the visited legend
  swatch and the «`N`/4 посещено» counter; `CityMapController` lost
  `VisitedBarCount`/`IsBarVisited`. `map.visited_count` removed from
  both localization catalogs (190 keys each, still symmetric).
- Tests reworked: `GameSessionStateTests` dropped the MarkBarVisited
  fixture and visit asserts (seed/blueprint tests now cover the route
  only), `SceneFlowSmokeTests` dropped all visit asserts and setup,
  five Home PlayMode fixtures dropped their `ClearVisitedBars`
  hygiene calls, `LocalizationCatalogTests` required list updated.

Verification:

- Runtime, EditModeTests and PlayModeTests compile with 0 errors via
  the bundled dotnet SDK. Focused EditMode batch run:
  `GameSessionStateTests` + `LocalizationCatalogTests` 51/53 passed.
  The 2 failures are pre-existing and unrelated:
  `FoodUse_ClearsFractionalHungerProgress` and
  `CheapFoodUse_StopsAtFloorAndKeepsUnusedItem` expect a free stew can,
  but the committed `FeedTheCat` starter quest (cfd4993) reserves
  `OpenStewCan`, so `TryConsumeInventoryItem` returns
  `ReservedForQuest`. Recorded, not fixed here — quest-journal area.

## 2026-08-14 — Sprite NPCs and bar minigames cut; 3D guests seated

- Removed the sprite NPC engine (`Bar/NPC`: actor, director, factory,
  planner, sprite library, types) and both of its populations: the bar
  crowd with its bartender and the supermarket cashier. The layout data
  survives — `BarNpcAnchor`/`BarNpcRole` live in the interior layout
  plan, and `SupermarketCashierPlan` keeps its authored spot for the
  future dedicated 3D cashier/bartender pass.
- Added `BarPatronWorldBuilder`: the production 3D pedestrians take the
  same authored anchors — `SeatedPatron` anchors get a bench-style seat
  anchor and the archetype's `SeatedRide` pose, everyone else stands on
  idle; `BarPatronAnimator` advances the loops; bartender anchors stay
  empty by design. `SceneFlowSmokeTests` now asserts 3D guests and no
  seated bartender.
- Cut all four minigames wholesale: `BeerPong`, `Cocktails`,
  `SplitTheG`, `TinctureMatch` runtime folders, the UI controllers and
  sprite libraries, `BarMinigameCatalog`/`IBarMinigame`/
  `BarActivityStation`, their Resources atlases, art generators and
  every dedicated fixture (about 60 files). `BarActivityKind` survives
  purely as the interior layout flavour (stage, beer pong table stay as
  dressing), normalized locally instead of via the catalog.
  `MarkBarVisited` moved from minigame completion to bar entry.
- `MinigameDebugWindow` kept its real duties — F9 modal with
  intoxication adjustment, City-map test-teleport toggle, F8
  diagnostics, drink-shop modal exclusivity — and lost the launcher
  list; `BarMinigameModalLock` stays as the generic modal capture.
- Localization: 114 minigame keys removed from both catalogs and the
  required list (kept symmetric at 191 keys); stale format assertions
  dropped.

Verification:

- Runtime, EditModeTests and PlayModeTests all compile with 0 errors
  via the bundled dotnet SDK; focused bar/supermarket/localization
  fixtures run recorded below.

## 2026-08-14 — Full-height street lamps with matched luminous power

- Scaled the street lamp assembly `1.6x` in `CityNightWorldBuilder`:
  a `5.30 m` mast (was `3.30`), thicker pole, longer arm, larger head
  and lantern. Every part offset is measured from the same planned
  base position, so no lamp moved; the light anchor rose with the
  lantern to `4.70 m`.
- Scaled the luminous power to the new height in
  `CityNightAtmosphere`: the source sits `1.61x` higher, so the
  inverse-square law sets intensity `12 -> 31` for the same pavement
  illuminance, range `10.5 -> 16.5 m`, and the fog halos grew with the
  lantern (`1.15/3.10`). Spot angles and the bar entrance lights are
  unchanged; the lower-pole collider already outsizes the thicker
  mast.
- Retired the stale "twenty street practicals" phrasing around the
  yard spotlight ratio; its authored `240` intensity is untouched.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `CityNightFixturePlannerTests` +
  `CityOpenAreaDecorationPlannerTests` passed `8/8` — lamp placement
  contracts and the yard-spotlight contracts both hold with the grown
  masts.

## 2026-08-14 — Pedestrians rest on benches

- Extended `CityPedestrianActor` with a bench lifecycle mirroring the
  Route 01 machinery. The pavement network ends at the kerb and
  `Constrain` never lets a walker off it, so the lifecycle is
  graph-then-crossing: `ApproachingBench` walks the Dijkstra guidance
  to the bench's own node, `WalkingToBenchSeat` is a short scripted
  off-network crossing (capsule released, like a bus doorway) onto the
  slot, `WaitingAtBench` hands to `BeginBenchSit` (presentation seated
  on an anchor exactly like a bus seat) and `StandUpFromBench` walks
  the same crossing back before `ResumeRoaming`. Cancellation from any
  crossing phase re-plants the walker on the bench node first. Bus
  logic is untouched: its guards key on the stop states.
- Added `CityBenchRestPlanner`: from the same `CityBenchSitPlan` seats
  the hero uses, it keeps only benches whose slot is within a `6 m`
  crossing of a graph node (reusing the bus wait planner's
  now-internal Dijkstra and nearest-node helpers) — which naturally
  excludes the hero's yard bench and anything the network cannot
  honestly reach. Each point carries the stand slot, seat top, sit
  facing and distance field.
- Added `CityBenchNpcRestController` in `CityGameRoot`: every `3.5 s`
  it may (p = `0.4`, xorshift seeded by the city seed) send the nearest
  eligible walker (walking, seatable archetype, within `30 m` of graph
  walk) to a free bench; on arrival it seats him for `15-30 s`, then
  stands him back onto the slot. Approaches time out at `45 s`;
  recycled walkers release their seats; at most `2` rest at once.
- Added `CityBenchSeatClaims`, a shared claim registry: the rest
  controller claims per rest, and `CityBenchSitInteraction` now claims
  on begin/releases on idle-or-cancel and hides its prompt for a seat
  claimed by another — the hero and the walkers can never share a
  plank.
- Added `CityBenchRestTests` (reachable seats, yard exclusion, claim
  exclusivity) and re-ran the pedestrian runtime fixture as the state
  machine regression.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `CityBenchRestTests` passed `2/2` and the full
  `CityPedestrianRuntimeTests` fixture passed `22/22` as the actor
  state-machine regression. The first run caught two honest design
  holes: the pedestrian walkable network never contained the bench
  slots (which produced the crossing design) and the yard bench
  qualified through a nearby node (now excluded by id, by decision).

## 2026-08-14 — Silent Hill attention: the hero's head finds targets

- Added the attention system in `PlayerAttention.cs`. Pure
  `PlayerAttentionRules` define the notice cone (`3.6 m`, `±75°`), the
  wider release cone (`4.2 m`, `±100°`) so a held target never
  flickers at the edge, the people-first ordering (`0.8x` effective
  distance) and the neck limits (`±68°` yaw, `±32°` pitch).
- `PlayerAttentionController` (installed by `PlayerFactory`) scans at
  `0.18 s` intervals: one physics overlap finds every `CanInteract`
  interactable and the pedestrians by their collision layer, and a
  static `PlayerAttentionMagnet` registry covers colliderless
  characters — the yard rider gets a magnet at seated head height in
  his factory. Between scans the held target is tracked live, so a
  walking passer-by keeps the head on him.
- `Player3DCharacterPresentation` applies the glance post-animation in
  `LateUpdate` with the established capture/restore base pattern:
  yaw/pitch shared `62/38` between head and neck bones,
  `SmoothDampAngle` turns, `0.22 s` ease-in / `0.38 s` ease-out, a
  fresh glance starting on target, and full stand-down whenever a
  modal clip, interaction handoff or ragdoll owns the body. Axis signs
  are named constants after the wheel-roll lesson.
- Added `PlayerAttentionTests`: cone and hysteresis contracts, neck
  clamps, and a controller pass proving people outrank closer objects,
  the fallback to interactables, and the release behind the back.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `PlayerAttentionTests` passed `3/3` (after
  marking the magnet `[ExecuteAlways]` so edit-mode registration works)
  and `YardWheelchairMotionTests` passed `10/10` in the same batch —
  clearing the previously blocked roll-sign and ground-profile
  contracts as well.
- In-game check: only the magnet-driven rider drew the head. The scan's
  self-filter compared `transform.root`, and every gameplay scene
  parents the player and the whole world under one composition root —
  so all colliderful targets read as "self". Replaced it with
  `IsChildOf(player transform)` and re-rooted the controller test so
  the player and its targets share one root like a real scene; the
  batch rerun was blocked by the open editor at the time of writing.
- Second in-game check: the hero craned his neck upward in the
  apartment. Points of interest never hang overhead by design, so the
  rules now reject any focus more than `2.1 m` above the hero's feet
  and the pitch clamp became asymmetric — the chin still drops `32°`
  for floor items but rises at most `10°`. Both are covered by the
  rules fixture.
- Third in-game check exposed the real culprit behind both cranes: the
  pitch axis sign. NPC faces sit at eye height (pitch about zero) and
  looked right, while interactables sit low — the intended `32°` chin
  drop applied inverted as a `32°` crane. Positive local X on the
  imported neck/head bones pitches the face up, so
  `AttentionPitchSign` flipped to `+1` (the wheel-roll lesson again);
  the overhead-focus and `+10°` up-clamp guards stay as safety.

## 2026-08-14 — The rider's lap follows the real ground

- In-game check found the chair hovering where the yard straddles two
  terraces: the plan's single flat `GroundY` came from the home cell's
  datum while the neighbour half of the circle can sit on another
  terrace. `YardWheelchairPlan.Create` now optionally takes the
  `CityElevationPlan` and samples `64` ground heights around the ring
  (`GroundDatum` + `GroundTopOffset`, falling back to the site ground
  where sampling misses); `Sample` reads the interpolated profile, so a
  terrace lip reads as a short ramp instead of a hover. `CityGameRoot`
  passes the layout's elevation plan.
- Extended the motion fixture: a synthetic stepped profile must carry
  the pose off the flat plane exactly along the interpolation, and the
  default-city elevated plan must match the elevation samples at every
  probed angle.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK. The focused `YardWheelchairMotionTests` batch run was
  blocked by the open editor holding the project lock; the two new
  deterministic contracts run with the next unlocked suite.

## 2026-08-14 — The drawn yard ring is removed; the rider keeps its circuit

- Removed the 24-chord `YardRingTrack` geometry, its `YardWornTrack`
  style, the packed-earth albedo, its generator tool and its focused
  test: the rider now circles the dead tree on bare ground with nothing
  drawn for the lap (reversing the same-day albedo work by user
  decision).
- Rewired `YardWheelchairPlan.Create` from ring read-back onto the yard
  site contract: `HomeYardSite.RingCenter/RingRadius/GroundY` are the
  circuit, with the dead tree still required at the centre. Slot
  clearances, utility anchors and the spotlight already used the same
  contract, so every keep-off-the-lap rule survives unchanged.
- Updated the open-area and wheelchair fixtures: no `home-yard-ring-`
  descriptors may exist, and the plan must equal the site ring exactly.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `YardWheelchairMotionTests` +
  `CityOpenAreaDecorationPlannerTests` + `CityDecorationPlannerTests`
  passed `22/22` under Unity `6000.5.5f1`, which also exercised the
  earlier roll-sign regression assertion for the first time in batch.

## 2026-08-14 — Turning wheels and a ragged push rhythm for the rider

- Made the pivot articulation real: `YardWheelchairPresentation` now
  adopts the static chair meshes (`ACC_WheelTyre/PushRim/WheelSpokes`,
  `ACC_CasterTyre/CasterHub`) under their authored `PIVOT_*` empties at
  initialize. The exporter deliberately ships them beside the pivots
  (parenting a skinned FBX mesh through an Empty double-converts units)
  with each mesh origin on its pivot, so the runtime reparent is exact —
  the existing distance-locked pivot rotations finally turn visible
  geometry, differential and caster swivel included. Bellows and organ
  pipes stay bone-skinned and keep riding the body animation.
- Added the hand-push cycle to `YardWheelchairMotion`: a `1.35 m`
  ground-locked cycle (`PushDistance`), smooth surge to `1.42x` through
  the `24%` stroke and a long bleed to `0.62x` through the coast,
  multiplied over the existing lap sway. Defined on distance, not time,
  so wheels, pace, the arm loop (speed clamp widened to `0.30-1.60`)
  and the bellows pump (now driven by `PushPhase`) can never drift
  apart. Minimum sampled speed stays above the `0.5 m/s` contract.
- Extended the motion fixture: a push cycle must surge at least `1.8x`
  over its trough and repeat exactly one push-distance later, and the
  presentation must adopt the wheel meshes under their pivots and turn
  them with covered distance.
- In-game check found the tyres rolling backwards: the baked FBX axis
  conversion leaves the axle on local X with positive spin reversed.
  Added `YardWheelchairPresentation.RollSign = -1` applied to both
  drive wheels and the caster roll, with an exact-rotation regression
  assertion so the sign cannot silently flip back.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `YardWheelchairMotionTests` passed `9/9` under
  Unity `6000.5.5f1` — the two new contracts plus every pre-existing
  motion invariant (circuit hold, drift flip, lap time, wheel
  differential, minimum speed). The subsequent roll-sign fix compiled
  clean; its batch rerun was blocked by the open editor holding the
  project, and the sign assertion mirrors the presentation formula
  exactly.

## 2026-08-14 — Packed-earth albedo for the yard wheelchair circuit

- Added `tools/build-city-yard-track-texture.py`: a deterministic 512
  seamless sheet of compacted bare earth (pressed hollows, wheel-polished
  dust, pressed-in stones, fine grain), isotropic on purpose — a circle
  has no single rut direction under world-planar mapping. Authored at
  mean RGB `120/104/80`, about twice as bright and warmer than
  `CityGroundSoilAlbedo` (`53/52/40`), so the trace contrasts against
  the yard soil while reading as trodden dirt.
- The `YardWornTrack` batch in `CityOpenAreaWorldBuilder` now builds
  through the planar-UV combine path (`1.8 m` tile) and receives the
  sheet via the shared `CityExteriorAppearance.ApplyYardTrackSurface`
  recipe (white tint, `0.05` smoothness, shared `RuntimePrimitiveLit`);
  every other open-area style stays a flat colour. Ring geometry, the
  rider's derived circuit and collision are untouched.
- Extended the open-area fixture: worn-track chunks must carry the
  packed-earth albedo with authored UVs on the shared material, and
  non-track chunks must stay textureless.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `CityOpenAreaDecorationPlannerTests` passed `4/4`
  under Unity `6000.5.5f1`, including the new worn-track albedo
  contract.

## 2026-08-14 — Placeholder interactions on every booth and dumpster

- Added `CityStreetUtilityInteraction`: an `IInteractable` stub standing
  on the recipe-derived dock of every phone booth door and dumpster lid.
  It offers the real prompts (`interaction.use_phone_booth`,
  `interaction.search_dumpster`) and answers through
  `PlayerInteractor.ShowFeedback` with `city.phone_booth.placeholder` /
  `city.dumpster.placeholder` for `2.5 s` — the same stub contract the
  stairwell cat used before feeding shipped. A future pass swaps only
  `Interact`; the trigger and dock stay.
- Added `CityStreetUtilityWorldBuilder` mirroring the bench sit pass:
  one oriented trigger volume per dock under
  `City Street Utility Interactions`, wired in `CityGameRoot` right
  after the bench sits from `CityStreetUtilityDock.CreateAll`.
- Added the four localization keys to both catalogs and the required-key
  list; extended `CityStreetUtilityPlanTests` with a builder contract
  (one placeholder per dock, kind-matched prompt, trigger volume).

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK; focused `CityStreetUtilityPlanTests` +
  `LocalizationCatalogTests` EditMode passed `10/10` under Unity
  `6000.5.5f1`.

## 2026-08-14 — Audit: every exterior electric glow joins the night clock

- Audited all `CityNoirEmission` users. Interiors (bar, home, stairwell,
  supermarket, fridge, alarm clock) legitimately own their light; the
  exterior stragglers were the nightlife neon batches, the booth backlit
  signs, the supermarket sign/letters and its two flat glowing storefront
  slabs, the home porch light, the hero's lit balcony window, the
  balcony-view lower facade panes and the POI lamps (waterworks
  `Working Lamp`, weighbridge `Cold Service Lamp`).
- Added `CityNightGlowRegistry`: builders register each electric renderer
  with its lit colour; `CityNightWorldResult.SetNightFactor` lerps them
  between a `0.10x` dead-fixture tint and full glow and prunes destroyed
  renderers, covering City and the bounded Home exterior. Deliberate
  exceptions stay always-on: traffic signals, the weighbridge
  `Scale Indicator Face` (`alwaysLit`) and the authored yard spotlight;
  the bus already dims through `CityBusPresentation.SetNightFactor`, and
  the Home-view terminal haze is a backdrop, not a fixture.
- Rebuilt the supermarket storefront glass as real glazing: the panels now
  use the shared Supermarket window-family material with the plain-glass
  quadrant of the window sheet (`CityWindowAppearance.ApplyPlainPane`), so
  they are framed, textured and follow the clock for free.
- Added `CityNightGlowRegistryTests` covering the lit/dead lerp contract
  and dead-renderer pruning.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK.
- Focused EditMode under Unity `6000.5.5f1` passed `15/15`:
  `CityNightGlowRegistryTests`, `CityWindowAppearanceTests` and
  `CityDecorationPlannerTests` together, re-proving the decoration
  build path with the registered neon batches.

## 2026-08-14 — Textured facade windows on the night-factor clock

- Added `tools/build-city-window-textures.py`: a deterministic 512 sheet of
  four pane variants (plain, curtains, blinds, lamp) authored light-glass /
  dark-frame in the facade sheets' doctrine and tone family, pre-corrected
  for the pane's 3.5:1 stretch, shipped as
  `Resources/Textures/CityWindowAlbedo.png`.
- Added `CityWindowAppearance`: one shared runtime material per lit window
  family (Cold/Warm/Bar/Home/Supermarket) cloned from the packaged unlit
  emission material with the sheet as `_BaseMap`; per-pane variety is an
  MPB `_BaseMap_ST` quadrant only, so the material keeps colour authority.
  `SetNightFactor` lerps each family colour between unlit `DayGlass` and its
  lit hue — the whole city's windows dim through five materials.
  `CityNightWorldResult.SetNightFactor` calls it, which covers both the City
  and the bounded Home exterior clocks. Dark panes keep the default lit
  material and get the same sheet via MPB, so they read as glazing all day.
- Replaced `CityExteriorAppearance.ResolveWindowColor` with
  `ResolveWindowFamily` (same hash, same 65/25/10 mix — seeds light the same
  rooms) and refactored both window builders onto it. Added
  `RuntimePrimitiveFactory.CreateMaterialBox`, a box that writes no colour
  property block so material-wide changes reach it.
- Added `CityWindowAppearanceTests` covering family determinism and mix,
  shared-material identity and night-factor lerp, variant quadrant bounds
  and the shipped sheet.

Verification:

- `BarPromenade.EditModeTests` compiles with 0 errors via the bundled
  dotnet SDK.
- Focused EditMode under Unity `6000.5.5f1`: `CityDecorationPlannerTests`
  passed `10/10` alongside the new fixture (also re-proving the booth
  lightbox build path); `CityWindowAppearanceTests` passed `4/4` after
  switching an exact `Color` equality to per-channel tolerance
  (`Mathf.Lerp(a, b, 1f)` is not bit-exact `b`).

## 2026-08-14 — Leaning yard utilities and citywide booth/dumpster coverage

- Added `HomeYardUtilityPlanner` to the shared yard site contract: it leans a
  phone booth against the hero's own wall (door into the yard) and the shared
  dumpster at the far end of the same wall. Both anchors keep their whole
  footprint `ring radius + 1.4 m` off the wheelchair circuit and never
  overlap each other; the yard slot objects now treat those footprints as
  reserved ground.
- The city decoration planner consumes the same anchors as ordinary
  `RoadsidePhoneBooth`/`RoadsideDumpsterAndUtility` descriptors
  (`…-homeyard-booth`/`…-homeyard-dumpster`), so recipes, night neon,
  chunked collision proxies and the home balcony exterior view all come from
  the existing street catalogue.
- Made booths and dumpsters repeat like infrastructure. Random roadside
  clusters demote a crowding utility to roadwork (booths never closer than
  `55 m`, dumpsters `40 m`), and a new row-major coverage pass fills the gaps
  (a booth within about `90 m` of every ordinary lot; dumpsters within `65 m`
  in Residential/Industrial and `100 m` elsewhere).
- Prepared interactivity: new `CityStreetUtilityDock.CreateAll` mirrors the
  bench-seat read-back and derives one dock per booth door and dumpster lid
  from shared recipe constants, so a future interaction pass can install
  triggers exactly like the bench sit pass does.
- Replaced the booth's bare floating neon slab with a municipal lightbox on
  the roof fascia: a dark `Street` housing, one recessed panel in a new
  seventh `BacklitSign` batch style (pale fluorescent `1.22/1.36/1.18` HDR
  glow on the shared emissive material, quieter than nightlife neon) and
  seven dark glyph strokes that read as the sign's word abstractly, matching
  the supermarket block-letter idiom. Batching stays per-chunk shared-material
  only.
- Extended pure coverage: spacing/coverage invariants across seeds, the
  hero-wall lean and circuit clearance contracts, yard dressing staying off
  the reserved utility ground, and dock determinism/ownership/reach.

Verification:

- Focused `BarPromenade.Tests.EditMode` run over `CityDecorationPlannerTests`,
  `CityOpenAreaDecorationPlannerTests` and `CityStreetUtilityPlanTests`
  passed `15/15` under Unity `6000.5.5f1`, including the unchanged strict
  yard dressing counts.
- `PlansAcrossSeeds_ExerciseCompleteRecipeCatalog` was stale since the
  roadside pool became shelter-free and now expects the route-owned shelter
  to stay absent from ambient decoration.
- Known unrelated failure, not addressed here: `CityBusPlannerTests
  .OrderedRoute_IsStreetOnlyRightHandAndOneInOneOut` misses its `1.5 m`
  departure-lane assertion by `0.0011` on this branch; the bus planner reads
  only the decoration plan's seed, so it is independent of this change.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Turned the permanent yard light into a noir key

- Strengthened the same single static neighbour-wall Spot from the ordinary
  street-practical level to intensity `240` (`20x` the street value of `12`) and
  retained its cold near-white color and day/night independence.
- Set range to the greater of `1.5x` the sampled throw and sampled throw plus
  `3 m`, then tightened the cone so the complete wheelchair circuit stays
  inside the bright inner region with only `6°` of total feather. The source
  now casts hard shadows at `0.95` strength and high resolution; its HDR lens
  multiplier is `4.8x` and its halo is larger and brighter.
- Kept the presentation architectural: there is no volumetric beam, no rider
  tracking and no second `Light`. The old yard lamp remains dead, so the
  bounded worst case stays `18` local realtime lights.

Verification:

- Focused `BarPromenade.Tests.EditMode`
  `CityOpenAreaDecorationPlannerTests`
  `.DefaultCity_DressesOnlyTheHomeYardWithACircuitAndTraces`: passed `1/1`
  under Unity `6000.5.5f1`.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Enabled vertical orbit on the ordinary chase camera

- `PlayerCameraFollow` now consumes both components of its existing RMB mouse
  and gamepad right-stick sample. The new pitch target is smoothed over
  `0.18 s`, clamped to `-20°..55°` and retained across fixed-camera ownership,
  just like the independent chase yaw.
- City, Bar and ordinary Supermarket follow gain vertical orbit. Home,
  Stairwell, contextual fixed shots and the bus's separately bounded seated
  view keep their existing ownership and limits. Modal `OrbitInputEnabled`
  suppression still gates both axes.

Verification:

- Focused `PlayerCameraPresentationPlayModeTests`
  `.ExteriorCamera_VerticalOrbitConsumesMouseInputAndClamps`: passed `1/1`
  under Unity `6000.5.5f1` in an isolated HEAD-based project copy carrying
  only the camera runtime and regression-test changes.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Put a permanent neighbour spotlight over the home yard

- Corrected the authored home-yard contract to the world that is actually
  built: the wheelchair circuit occupies the walkable roadless gap between the
  hero's building and its neighbour, not the large eastern fringe `Yard`.
- Added one stable wall-mounted spotlight to the same data-first composition.
  Its fixed shadowless cone covers the complete worn circuit at constant
  intensity through day and night, stays outside `NightFactor` and never tracks
  the rider. The old two-part yard lamp remains dead geometry with no emitter.
- Accounted for the permanent source without shrinking the atmosphere pool:
  `12` atmosphere lights + `4` bus Spots + `1` pooled helmet Spot + `1` yard
  Spot gives a bounded worst case of `18` local realtime lights. The scene
  Directional and transient lightning Directional remain separate.

Verification:

- `BarPromenade.Tests.EditMode.CityOpenAreaDecorationPlannerTests`
  `.DefaultCity_DressesOnlyTheHomeYardWithACircuitAndTraces`: passed `1/1`
  under Unity `6000.5.5f1`.
- `BarPromenade.Tests.PlayMode.CityNightPresentationPlayModeTests`
  `.CityDayNight_ChangesLightingWithoutChangingFog`: passed `1/1` under Unity
  `6000.5.5f1`.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Restored the Pipeback Roller's wheelchair at full size

- Fixed the staged Unity prefab build that multiplied all `17` root-bound
  wheelchair `MeshRenderer` transforms by an extra `0.01`. The FBX importer
  already honours the model's metre units, so the duplicate conversion left
  the wheels, rims, spokes, casters, frame, seat, backrest, armrests,
  footrests and push levers at one percent of their authored size while the
  skinned rider remained full-size.
- Removed that additional scale conversion and rebuilt
  `Assets/Pedestrians/Staged/Prefabs/PipebackRoller3D.prefab`; the complete
  chair is visible again without changing the isolated provider or ambient
  pedestrian pool contract.
- This fix restores the static mechanism geometry only. The passive
  `PIVOT_*` anchors are still not transform parents of their meshes, so
  procedural wheel/caster/mechanism articulation remains a separate
  limitation.

Verification:

- Focused EditMode
  `StagedPipebackRoller_ImportsPassiveWheelchairAndRemainsOutsidePool` passed
  `1/1` after the staged prefab rebuild.
- No broader suite or player build was run in fast mode.

## 2026-08-14 — Added a one-key Home-to-City debug map entry

- `HomeInteriorRoot` now always installs `HomeDebugCityMapShortcut`. F9 works
  from any Home phase, including the opening's locked `ClockHold`, and uses the
  guarded direct scene-transition path instead of playing the apartment and
  stairwell presentations.
- If the session clock is still frozen, an accepted shortcut starts it from
  `06:00`, prepares the normal player-home City return and sets the resettable
  `GameSessionState.DebugCityMapOnArrivalRequested` handoff. Seed, cash, needs
  and starter inventory are otherwise untouched.
- A real runtime skip from the opening's `AwaitingWake` menu exposed a
  lifecycle race: City enabled test teleport after the scene transition, but
  its single map-open attempt ran while the previous Home opening still owned
  `BarMinigameModalLock`, so the map remained closed.
- `CityGameRoot` now enables test teleport after the transition and uses a
  success-driven retry window bounded to `2 s` of realtime. It opens only
  after both the transition and previous modal lock release, consumes the
  one-shot on success, and also clears it with diagnostic state on timeout so
  a failed request cannot leak into a later City load. Ordinary Wake/Quit and
  Home -> Stairwell -> City behavior is unchanged.

Verification:

- The focused PlayMode
  `BarPromenade.Tests.PlayMode.HomeOpeningPlayModeTests.MainMenu_F9SkipsHomeAndOpensCityDebugTeleportMap`
  now waits for `AwaitingWake` and asserts the active modal-lock precondition
  before F9. It passed `1/1` in an isolated Unity project copy while the main
  editor remained open.
- No broader suite or player build was run in fast mode.

## 2026-08-13 — Staged the Pipeback Roller wheelchair NPC

- Added `pipeback_roller_v1` / Pipeback Roller («Трубный седок») as a complete
  staged presentation rather than a sixth production pedestrian. The ordinary
  seated rider wears dark burgundy; the bizarre silhouette belongs to the
  wheelchair's two large drive wheels, nervous front casters, under-seat
  bellows and asymmetrical fan of tarnished organ pipes.
- Extended the deterministic pedestrian generator with
  `Assets/Pedestrians/Staged/Models/PipebackRoller3D.{fbx,json}` and the
  adjacent editable source/preview. The rider preserves the exact production
  31-bone Generic hierarchy and shared `Player3DLit` material. Six passive
  `PIVOT_Wheel.L/R`, `PIVOT_Caster.L/R`, `PIVOT_Bellows` and `PIVOT_PipeBank`
  anchors expose the future procedural mechanism contract without adding
  deform bones or auxiliary curves to the Avatar.
- Added two in-place Actions to the shared animation-only locomotion library.
  `PipebackIdle` keeps the head level over a slow breath under the pipe load;
  `PipebackRoll` stages a two-handed raised-lever push, forward body lean,
  release and recovery. Bellows and pipes follow the authored pelvis/chest
  motion; wheel/caster rotation remains intentionally procedural and deferred.
  The staged design deliberately owns no `Sit` clip.
- Added the passive staged prefab at
  `Assets/Pedestrians/Staged/Prefabs/PipebackRoller3D.prefab`. It is outside
  `Resources` and carries only the shared `CityPedestrianAssetRegistry` plus a
  passive `CityWheelchairNpcAssetRegistry` for those six mechanism pivots. It
  has no runtime actor, collider, Rigidbody, light, audio or interaction, and
  `CityPedestrianResources.OrderedArchetypes` remains the five-design
  production catalog. City and Home therefore keep their existing `13`- and
  `8`-presentation pools, and the staged NPC cannot roam, wait for or ride
  Route 01.
- Production registration is deferred until the graph can exclude stairs and
  prove curb/turn clearance, the actor has a wheelchair footprint rather than
  the ordinary `0.35 m` capsule, runtime derives wheel/caster motion from
  travelled distance, and Route 01 owns an accessible boarding and securement
  design instead of the ordinary pelvis-to-seat transfer.

Verification:

- Blender 5.0.1 completed the full generator/validator, rendered the six-row
  contact sheet and matched repeated model signatures. Pipeback measures `52`
  meshes / `2388` triangles at exactly `1.75 m`; both clips remain in-place and
  loop-closed with wheel contact `0.000 m`, footrest clearance `0.268 m`, seat
  gap at most `0.023 m` and hand-to-lever distance below `0.10 m`.
- Unity rebuilt the passive staged prefab and the one focused EditMode contract
  `StagedPipebackRoller_ImportsPassiveWheelchairAndRemainsOutsidePool` passed
  `1/1`, including avatar/material/passivity, the two clips, six pivots, absence
  from `Resources`/catalog, and unchanged City `13` / Home `8` pool isolation.
  Complete suites and a player build were intentionally omitted in fast mode.

## 2026-08-14 — Moved the yard composition to where the hero actually stands

- Reported from play: the yard by the home read as empty ground. The
  dressing was anchored to the centroid of the yard area, but `yard-east`
  is the whole eastern pocket (`4 x 6` cells, over `100 x 150 m`), so the
  centroid landed ~`65 m` east of the home — past the `48 m` far clip and
  the fog. The ground was there; every object was invisible.
- The composition is now anchored to the yard's street entrance, which the
  layout puts at cell `(12,5)`, `17 m` from the door. The ring is offset
  past the approach (`approach reach + clearance + radius + margin`) so
  the worn circuit stays unbroken instead of losing segments to the
  entrance, and the dressed rect is built along the inward normal from the
  entrance rather than over the whole pocket.
- Measured after the change (default seed): dead tree `27.6 m` from the
  home, bin `20 m`, everything else `23-35 m`; yard datum `7.00` against
  the home's `7.44`, so the `0.44 m` step is a kerb, not the cliff a
  far-side access would have produced.
- Guards added so this cannot regress silently: every yard part must be
  within `46 m` of the home, the dead tree within `34 m`, and the ring must
  keep all `24` segments.

## 2026-08-14 — Put the Pipeback Roller on the yard circuit, drifting

- New `Assets/Scripts/Runtime/City/Yard/`: `YardWheelchairMotion` (pure
  pose math), `YardWheelchairPlan` (reads the circuit back out of the
  authored dressing — trunk gives the centre and ground height, the worn
  ring segments give the radius, so rider and track can never drift
  apart), `YardWheelchairPresentation`, `YardWheelchairActor`,
  `YardWheelchairFactory`, `YardWheelchairProvider`.
- The drift is the whole point: the chassis is yawed `19° ± 7.5°` into the
  circle *against* the direction of travel, the slip breathes over `0.37`
  laps, the pace sags and recovers in the same phase (`1.05 m/s ± 8%`), the
  ridden line wanders `±0.14 m` off the worn ring over `0.83` laps, and the
  body holds a `4.5°` outward lean. Wheels turn from real distance with an
  inner/outer differential plus a scrub factor from the slip angle;
  casters trail round to point where the chair is actually going rather
  than where it faces; bellows pump and the pipe bank rocks.
- Isolation respected exactly as specified: the prefab stays at
  `Assets/Pedestrians/Staged/Prefabs/`, out of `Resources`, out of
  `CityPedestrianResources`/`OrderedArchetypes`, and is never passed to
  `CityPedestrianFactory`. The only reference is a serialized
  `YardWheelchairProvider` asset at `Resources/City/`, which is what the
  factory loads — the prefab itself is never `Resources.Load`ed.
- `CityPedestrianPresentation` was deliberately NOT reused: it grounds a
  walker by its shoe soles, and this NPC has to sit on its wheels. The new
  presentation builds its own two-clip manual `PlayableGraph`
  (`PipebackIdle`/`PipebackRoll`) with an `AnimationPlayableOutput`, since
  the staged prefab ships no `AnimatorController`. Prefab passivity is
  re-validated at instantiation.
- Wired in `CityGameRoot` beside the bus, with `yard_wheelchair_present`
  and `yard_wheelchair_radius` in the init log.
- Tests: new `YardWheelchairMotionTests` — the plan matches the authored
  ring segment-by-segment, the pose holds the circuit and always carries a
  slip angle, the drift mirrors with direction, a lap returns to its start,
  non-positive steps are ignored, and the wheel differential favours the
  outer wheel and grows with distance.

## 2026-08-13 — Dressed the home yard around a circuit nobody else uses

- First authored yard composition, in `CityOpenAreaDecorationPlanner`:
  a third `BuildYards` call beside `BuildLake`/`BuildCemetery`, dressing
  only `yard-east` (the other four wait for their own descriptions).
  Nine new `CityOpenAreaDecorationKind` values and four styles
  (`YardWornTrack`, `YardTimber`, `YardPipe`, `YardPaint`) with colours in
  `CityOpenAreaWorldBuilder.ResolveColor`; the flat ring and the dropped
  toy are declared non-blocking.
- Composition follows the art bible's rule that this city is made bleak by
  subtraction, not by piling on rubbish: a bare dead trunk with two broken
  limbs at the centre, a 24-chord worn ring at radius `6 m` around it, and
  seven edge traces — repaired bench (one leg swapped for painted pipe),
  carpet-beating frame, empty sandpit, one child's toy as the only
  saturated colour, dead lamp post, bin beside the entrance, one bottle.
  The yard emits no light at all, by design.
- Placement rules: everything derives from the union of the yard's surface
  bounds and the declared access; edge objects rotate their authored angle
  in quarter turns until the footprint clears the street approach, and the
  bin is offset sideways from the entrance rather than standing in it (the
  first version put it straight in the approach and was rejected by the
  planner's own clearance check). Ring chords are short, so nothing spans
  a `48 m` batching chunk. Randomness is one salted `StableHash`
  (`0x59415244`) that only spins the edge ring.
- The middle of the ring is deliberately left empty for the wheelchair
  rider; the model is still being authored, so no actor, presentation or
  placeholder was added in this pass. The character contract is recorded
  in the plan: chair as an unrigged `GEO_*`/`PIVOT_Wheel*` prop (the shared
  31-bone rig has no wheel bones), rider on the shared rig, registered
  outside the pedestrian catalog.
- Tests: `CityOpenAreaDecorationPlannerTests` gained a yard fixture
  (determinism, per-kind counts, containment in the yard ground, ring stays
  non-blocking, circuit centre free of props) and its clearance loop was
  fixed — `.Single(access.Feature == descriptor.Feature)` threw once five
  yards shared one feature. `CityLayoutGeneratorTests`' "yards carry no
  decoration" assertion is inverted to "only `yard-east` is dressed".
- Art bible gained §10a for the yard in the same shape as the four public
  places (essence, movement grammar «замкнутый круг», light, Нельзя,
  Проверка).

## 2026-08-13 — Typed the boundary voids as Yards

- The three unmapped regions behind the boundary streets are now five typed
  `Yard` areas: `yard-east` (`RectInt(12,2,4,6)`, the pocket beside the
  player's home between cemetery and lake) plus `yard-south-{west,east}`
  and `yard-west-{south,north}` — one-cell perimeter strips halved so each
  aligns to its own access datum on the terraced perimeter. `(-1,-1)` and
  the `x15/z0-1` notch stay void. Blueprint cells `198 -> 246`; yard open-area
  accesses `5`.
- New `CityAreaFeatureKind.Yard` + `CityDistrictKind.Yard` wired through
  every gate that defaults to throw: combination/topology/structural
  validation, `IsSpecialArchetype`, the `CreateUrbanArea` guard, the
  required-access lists in `CitySurfacePlan` and `CityLayout`, elevation
  datum + preferred stair connections, `RequiresAuthoredAccess`, the world
  ground bucket (`OpenGround` -> new `YardGround` colour) and the map
  (`YardLand` fill + `map.district.yard` = «Двор»/"Yard" in ru/en).
  `CitySurfaceKind.OpenGround` — previously unreachable — is now the yard's
  surface kind.
- Stage 2 relaxed one declared invariant deliberately: only the lot and
  road-grid footprint must normalize to `(0,0)` (every per-cell random
  stream hashes raw coordinates, so shifting the grid would regenerate a
  different city); the `OpenLand`/`Water` fringe may reach `-1`, bounded by
  a named constant. `ValidateNorthWaterfront` and its test now scan the
  normalized `x >= 0` range.
- Three determinism hazards were identified up front and neutralized:
  yards are excluded from `TryResolveSignatureStairOwner` (else the four
  district stairs re-rank), from bus-corner support in
  `CityBusIntersectionSelector` (else the home stop drifts to the new
  boundary corners), and the new `EnsureYardAccessEdges` runs *after* every
  RNG consumer so it is a no-op on the canonical city instead of re-seeding
  the road graph.
- Zero decoration by construction: `CityOpenAreaDecorationPlanner` still
  only builds lake and cemetery, and a test asserts no yard descriptor
  exists. The yards are placeholders awaiting authored content.
- Verification: EditMode city suites green (`75/75` across
  `CityLayoutGeneratorTests`, `CityElevationPlannerTests`,
  `RoadFencePlannerTests`, `CityOpenAreaDecorationPlannerTests`,
  `CityMapDistrictPresentationTests`, `LocalizationCatalogTests`),
  including new `DefaultCoastalBlueprint_CreatesReachableEastYard`,
  `...CreatesReachablePerimeterYards` and a
  `DefaultSeed_KeepsCanonicalHomePlacement` canary (home still `(11,5)` at
  `(143,-13)`, partner bar `(11,6)`, four public places). Nine unrelated
  failures in the full suite (bus/pedestrian/GameSessionState/day-night)
  reproduce on clean `HEAD` without these changes and are not caused here.
  PlayMode not run.

## 2026-08-13 — Gave the default city terrain and exterior stairs

- Added a pure immutable `CityElevationPlan` between blueprint topology and
  spatial materialization. The default coastal blueprint now spans `12 m`,
  every urban district has at least `1.5 m` of local terrace variation, water
  keeps declared sea/lake datums, and legacy/custom blueprints stay exactly
  flat. One sampler now grounds nodes, cells, lots, entrances, returns, public
  places, open-area access, stops, waiting slots and debug teleports.
- Rebuilt City ground as deep terrace slabs and streets as oriented graded
  road/sidewalk/paint meshes with level junction pads. One shared boundary
  plan emits radius-safe connectors where road and ground differ by at most
  the `0.28 m` controller step and physical guards everywhere else; decorations, facade proxies, fences, night
  fixtures, park dressing, lake/cemetery dressing and Home's same-seed exterior
  transform inherit their local datum.
- Added one validated signature stair street in Old Town, Residential,
  Industrial and Nightlife. Each has `6-12` visible collider-free steps,
  `0.15-0.17 m` rise, `0.30-0.34 m` tread, two `1.5 m` landings, physical
  rails/retaining walls and exactly one hidden continuous ramp collider. The
  pedestrian graph includes both stair directions while a parallel grade-safe
  Street edge preserves Route 01.
- Made Route 01 elevation-aware end to end: grade-filtered links and 3D
  samples, level turns, local stops/waiters/boarding docks and actor pitch with
  roll locked. Fresh/return spawns and map test-teleport resolve the same live
  surface. The player contact patch now follows the collider normal; a balance
  check refuses to start on slopes above `12°`.

Verification:

- `dotnet build BarPromenade.Runtime.csproj -nologo --verbosity quiet` passed
  with `0` warnings and `0` errors.
- Focused EditMode `CityElevationPlannerTests` passed `8/8`; focused EditMode
  `CityExteriorStairModuleTests` passed `4/4`. Focused PlayMode
  `CityBusRidePlayModeTests.ProductionCityDoorDocks_MatchPhysicalSurfaceHeight`
  passed `1/1` after proving every Route 01 transfer dock against the built
  collider surface. Full suites and a player build were intentionally omitted
  in fast mode.

## 2026-08-13 — Textured the player's apartment

- Twelve deterministic seamless apartment albedos (wallpaper, ceiling
  plaster, painted planks, dark wood, worn laminate, upholstery, bed linen,
  bathroom tile, white enamel, painted metal, concrete, entry rug) from a
  new Pillow generator `tools/build-home-textures.py` — facade-pipeline
  structure (1024 source / 512 import, periodic-by-construction noise,
  `--verify`, SHA256 manifest in `ArtSource/Home/home-textures.json`,
  contact sheet). Compensation constants are solved per sheet with the
  city-facade **linear** rule against the exact builder tints (channels
  below `0.09` clamp-checked only — sRGB toe); the stairwell gamma rule
  would have over-brightened the dark home palette up to 2x.
- Extracted `SurfaceAppearanceCore` (projection enum, metre tiling,
  stable-hash UV offsets, display tint) out of
  `StairwellSurfaceAppearance`, which now delegates with bit-identical
  hash order; new `HomeSurfaceAppearance` (12 recipes, lazy cache,
  `[RuntimeInitializeOnLoadMethod]` reset, hash salt `1000 + kind`) plus
  `HomeSurfacePrimitives.CreateBox/CreateCylinder` wrappers.
- Threaded through the builders keeping every existing `Color` as the
  tint: `HomeInteriorWorldBuilder` (shell, facade piers, furniture),
  `HomeBathroomBuilder` (walls, three tile planes, porcelain fixtures,
  pipes), `HomeBalconyWorldBuilder` (facades, deck, rails, frames, door
  leaf), `HomeInteriorDressingBuilder` (window boards, radio, radiator
  pipes), `HomeRefrigeratorWorldBuilder` (cabinet, front frame, cavity
  liners, shelves, door). Decal overlays (damp/peel/stains), sub-`0.45 m`
  props, the alarm clock and fridge food stay flat-tinted by an explicit
  exemption list; `HomeExteriorViewBuilder` untouched (already textured by
  the city systems).
- `HomeOccluderDither.shader` now declares and samples `[MainTexture]
  _BaseMap` (default white) plus `_Smoothness`/`_Metallic` in ForwardLit,
  so textured furniture keeps its albedo through visibility fades instead
  of flashing to the compensated flat tint — the MPB survives the
  controller's `sharedMaterial` swap untouched.
- New EditMode `HomeSurfaceAppearanceTests`: 12 import contracts + raw-PNG
  opacity/seam/contrast checks, a C# re-derivation of the linear
  compensation rule from the shared tint table, MPB apply/stability/
  projection tests, a dither-shader `_BaseMap` regression guard, and a
  full `HomeInteriorWorldBuilder.Build` walk asserting every ordinary
  renderer is textured or on the exemption list with all 12 sheets seen.
  Unity-side test runs still pending (editor import of the new PNGs
  required); generator `--verify` passes twice with identical hashes.

## 2026-08-13 — Quest system, journal menu and the feed-the-cat gate

- Added a minimal data-first quest core: `QuestId`/`QuestStatus`/
  `QuestDefinition`/`QuestCatalog` plus a pure `QuestLogState`
  (activate-once, complete-once), owned by `GameSessionState` next to the
  inventory. `ResetToDefaults` seeds `FeedTheCat` as active on every new
  game; activation and completion are logged under the `quest` channel.
- `StairwellCatInteraction` completes `FeedTheCat` at the moment the
  prepared feeding actually begins (the can is already consumed and the cat
  eats), not at the exit clip, so an aborted exit cannot lose the
  completion.
- New `StairwellQuestDescentBlocker` in the stairwell root: while the quest
  is active, crossing `0.35 m` below the middle-landing elevation on the way
  down shows the localized `quest.feed_cat.block.descend` line through the
  existing `InteractionPromptView` feedback panel and drives the hero back
  to a landing return pose via `PlayerMotor.MoveTowardsInteractionPose`
  with input locked; stall detection ends the walk gracefully. Crossing
  detection (previous sample above, current below) means a spawn below the
  threshold can never trap the player.
- While the quest is active `GameSessionState.EvaluateInventoryItemUse`
  returns the new `InventoryItemUseStatus.ReservedForQuest` for
  `OpenStewCan`, so the inventory eat action refuses with
  `inventory.use.failure.reserved_for_quest` and keeps the can; the closed
  can stays edible. The cat feeding itself removes the item through the
  target-interaction path, which is deliberately unaffected.
- New `JournalController` (J / gamepad RB — Select was already the map)
  in all five gameplay roots: shared `BarMinigameModalLock`, frozen time
  scale like the inventory, localized quest list with per-status
  description and IN PROGRESS/DONE tags. Eleven new ru/en localization
  entries.
- Focused new EditMode `QuestLogTests` passed `5/5` (new-game activation,
  one-shot completion, reserved/released/closed-can consumption rules);
  the run also compiled the runtime assembly. Full suites and a player
  build were intentionally not run. Batchmode serializer churn in
  `Assets/Vehicles/Materials` was reverted, not committed.

## 2026-08-13 — Working bus windshield wipers

- The bus carried two static wiper cylinders welded into one `GEO_Wipers`
  mesh, so nothing could move when the new weather rained on the windshield.
  Bumped the deterministic generator to `1.4.0`: each wiper is now its own
  arm-and-blade mesh under an authored base pivot (`PIVOT_WiperL/R`, roles
  `left_wiper`/`right_wiper`) on the body at the exact old base points, with
  rest geometry matching the old diagonal pose. Generator validation gained
  wiper control-pivot and single-owned-mesh checks; 47 meshes, 4176
  triangles, new signature.
- `CityBusAssetRegistry` binds both wiper pivots (validated by
  `CityBusAssetSetup` alongside the other articulation bindings and reset by
  `ResetArticulation`). `CityBusPresentation.AdvanceWipers(rain, dt)` sweeps
  them `±40°` in mirrored directions around a model-derived windshield-normal
  axis (`ResolveForwardAxisLocal`, same lesson as the wheel vertical axis):
  sweep rate lerps `0.35-1.15 Hz` with rain intensity, a dry frame parks the
  blades at `110°/s` instead of freezing them, and a rain restart re-enters
  the sine sweep at the parked angle's own phase so blades never teleport.
- `CityBusActor.Advance` gained an optional `rainIntensity` argument fed by
  `CityBusDirector` from a new provider that defaults to the pure
  `GameWeatherRules` schedule, mirroring the night-factor provider; existing
  three-argument call sites keep compiling with parked wipers.

Verification:

- Blender 5.0.1 regenerated and self-validated the model
  (`CITY BUS 3D BUILD OK`); batch `CityBusAssetSetup.RunBatch` rebuilt and
  validated the prefab (`CITY BUS UNITY ASSET BUILD OK`).
- Focused EditMode `CityBusAssetImportTests` + `CityBusRuntimeTests` passed
  `29/29` with zero C# warnings, including the new
  `PresentationWipers_SweepWithRainAndParkWhenDry` (sweep bounds, mirrored
  blades, smooth parking, pool reset) and the extended import checks for the
  wiper pivots, bindings and `ResetArticulation`. The first run exposed that
  the synthetic `RuntimeFixture` registry predated the new optional wiper
  bindings; the fixture now authors both pivots. PlayMode, full suites and a
  player build were intentionally omitted in fast mode.

## 2026-08-13 — Thunderstorms and balcony weather audio

- Extended the same-session weather schedule with a fourth slot kind:
  `Thunderstorm` (`6%`, carved from heavy rain, which drops to `12%` and
  light rain to `27%`). A storm carries full heavy-rain intensity plus
  lightning from the same pure schedule: each `12`-game-minute window of a
  fully developed storm slot hashes into at most one strike (`70%`) with a
  deterministic start offset, azimuth and distance band
  (`GameWeatherRules.EvaluateLightning`), so City and the Home balcony flash
  the identical storm without any new session state.
- The flash is one transient shadowless directional light
  (`CityLightningFlashLight`) with a flickering `0.5`-game-minute decay
  envelope, peak intensity `1.9` scaled down to `45%` at the far distance
  band. It stays disabled outside a flash and lives outside `Night.Root`, so
  the pooled 12+4 light budget and the existing light-count assertions are
  untouched. A frozen clock (pre-wake `05:59`, pause `timeScale = 0`)
  suppresses the flash instead of holding it lit.
- Thunder is a deterministic synthesized one-shot (`CityThunderSynthesis`:
  crack over brown rumble with a delayed secondary roll) played `0.6-3 s`
  after its flash with distance-scaled volume and low-pass cutoff on the
  `Ambience/Details` group. Two rotating voices sit on child objects because
  an `AudioLowPassFilter` processes every source of its own GameObject.
- Per user request the balcony now hears the weather too:
  `HomeBalconyExteriorAtmosphere` owns its own rain bed, thunder player and
  flash light beside the rain field, gated to the active Balcony shot —
  stepping inside silences the bed and drops the flash while the rain field
  keeps simulating like the fog.

Verification:

- Focused EditMode `GameWeatherRulesTests` passed `9/9` (four-kind coverage,
  storm-only lightning gating, in-storm flash bounds/determinism, ramp
  boundaries now searched by target intensity so a heavy->storm border cannot
  produce a degenerate assertion); zero C# warnings or errors. PlayMode
  suites, player build and smoke intentionally omitted in fast mode.

## 2026-08-13 — Deterministic rain in two intensities

- Added the first exterior weather system as a pure schedule plus
  presentation, with no new session state. `GameWeatherRules` (Core) maps the
  city seed and absolute game minutes into `90`-game-minute slots — Clear
  `55%`, LightRain `30%`, HeavyRain `15%` — and smoothsteps the continuous
  rain intensity between slot targets (`0` / `0.45` / `1.0`) over the first
  `5` game minutes, so City and the Home balcony always sample identical
  weather and scene loads cannot desynchronize it.
- `CityRainField` mirrors the `CityFogField` pattern: a seeded,
  player-following runtime particle system of stretched streak billboards on
  the shared `CityAtmosphereParticle` material (at most `420` particles over
  a `26 x 26 m` box from `12 m` up, world-space, no collision). Intensity
  continuously scales emission, streak width, alpha and velocity stretch, so
  light rain reads sparse and thin while heavy rain reads dense and long.
  While the hero rides the bus the emitter switches to a donut with a `10 m`
  rain-free core, because streak billboards would otherwise spawn inside the
  cabin.
- `CityWeatherController` on `CityGameRoot` samples the rules every frame,
  drives the field, logs `weather_changed` NDJSON events on kind changes and
  feeds `CityRainSoundPlayer` — a deterministic crossfaded xorshift-noise
  loop (`CityRainAmbienceSynthesis`, mono `22050 Hz`, 4 s) whose volume and
  low-pass cutoff track intensity on the `Ambience/Beds` group.
  `HomeBalconyExteriorAtmosphere` builds the same rain field at its fog
  anchor, toggles its renderer with the Balcony shot exactly like the fog
  renderer, and updates intensity per frame; the balcony adds no rain sound.
- Deliberate boundary: rain does not modify `GameTimeDayNightRules`,
  `RuntimeSceneSetup` lighting, fog, grade or far clip — those contracts are
  asserted exactly by existing City/Home PlayMode suites. Daylight dimming,
  wet surfaces and balcony rain audio are recorded as open gaps in
  `ai/systems-map.md` and `ai/architecture-notes.md`.

Verification:

- New focused EditMode `GameWeatherRulesTests` passed `7/7` in Unity
  `6000.5.5f1` (determinism, plateau targets, boundary ramp, all-kinds
  coverage, seed sensitivity, clamping, non-finite rejection); the run
  compiled Runtime and both test assemblies with zero C# warnings or errors.
- Fast mode intentionally omitted PlayMode suites, a player build and smoke.

## 2026-08-13 — Bus albedos and visible pendant cabin lamps

- The bus was the last flat-colour hero object on a textured street, and its
  "cabin light" had no visible source: the `LGT_CabinStrips` boxes were
  centred at `2.765 m`, entirely inside the `2.72-2.78 m` interior ceiling
  panel, so the emissive meshes could never be seen and the two runtime cabin
  Spots floated at `2.83 m` inside the roof.
- Bumped the deterministic bus generator to `1.3.0`. Every mesh now carries
  world-scale box-projected UVs (per-slot metre tiling, so Unity materials
  stay at `(1, 1)`), the ceiling strips protrude below the panel, and two
  pendant lamps hang on the aisle centreline at source `y = ∓1.45` — metal
  stem, trim collar and a `CabinLight` bulb spanning `2.56-2.66 m`. The new
  `cabin_lamp_bulb` role joined the generator's required-role validation.
- Added `tools/build-city-bus-textures.py`: four deterministic tileable
  512 px albedos (paint with panel seams/rivets/grime streaks, brushed metal,
  speckled ribbed linoleum, seat weave), light bases near `0.75-0.8` mean
  luminance so the existing flat `_BaseColor` values keep the hue.
  `CityBusAssetSetup` assigns them per slot (`Body/Accent`, `Metal/Rail`,
  `Interior/Dashboard`, `Seat`) and its prefab validation now fails if a
  mapped material loses its `_BaseMap`.
- `CityBusPresentation` moves the two cabin Spots from the roof interior down
  to the authored bulb centres (`2.61 m`), raises their base intensity
  `5.5 -> 7.5` and warms the night cabin emission so the bulbs read as the
  actual source. Light count, names and directions are unchanged, keeping the
  12+4 city light budget.

Verification:

- Blender 5.0.1 regenerated and self-validated the model: 46 meshes, 4136
  triangles, new signature; `CITY BUS 3D BUILD OK`.
- `python tools/build-city-bus-textures.py` reported mean luminances
  `0.75-0.81` for all four sheets.
- Batch `CityBusAssetSetup.RunBatch` rebuilt and validated the prefab
  (`CITY BUS UNITY ASSET BUILD OK`), including the new albedo binding check.
- Focused EditMode `CityBusAssetImportTests` passed `4/4` and
  `CityBusRuntimeTests` passed `28/28`, including
  `PresentationNightLights_AreSprungScaledAndPoolSafe`. Full suites,
  player build and smoke were intentionally omitted in fast mode.

## 2026-08-13 — District walls for the city buildings

- The street had textured ground under untextured boxes. Every road, sidewalk
  and patch of soil carried a real albedo; the buildings standing on them were
  flat colour, and the four districts differed only by the seeded RGB range in
  `CityLayoutGenerator.CreateBuildingColor` — which `ai/city-zones-art-bible.md`
  §18.4 rules out as sufficient on its own.
- Added `tools/build-city-facade-textures.py`, the first scripted world albedo
  in the project: eight district walls plus a shared roof cap, two per district
  so each carries both of its material axes. Pillow only, deterministic, with
  its own validator covering opacity, wrap, macro contrast, mean luminance,
  channel neutrality and the accent-area ceiling the bible imposes on saturated
  colour.
- Added `CityFacadeGrid` as the one source of the bay and floor pitch. The pane
  arithmetic had been duplicated three times (`BuildWindowBands`,
  `BuildWindowRow`, and `HomeExteriorViewBuilder`'s copy of both); a fourth
  consumer that derives a texture's UV from one copy while geometry comes from
  another would drift silently.
- Added `CityFacadeAppearance`, which tiles the albedo by the building's own
  window grid instead of by metres, so one authored cell covers exactly one
  pane bay and one `2.35 m` storey. Horizontal phase follows the pane-count
  parity, vertical phase is independent of building height, and a stable
  per-lot whole-cell rotation varies presentation without disturbing either.
- **Measured, not assumed.** Facade widths are `11.78–15.5 m` and heights
  `5–13 m`, so `paneCount` is only ever 4 or 5, bay pitch `1.96–2.45 m` and the
  glass fraction of a bay `0.857–0.886` — a ±1.7% spread. That tightness is
  what makes one authored bay land on every real bay within ~3 cm.
- **Two corrections worth recording.** First, the brightest channel any lot can
  reach is `0.616` (a bar), not the `0.36` an earlier sweep suggested; that
  sweep minimised the other channels instead of maximising them. Second, and
  more consequential, reusing `StairwellSurfaceAppearance`'s
  `compensation = 1 / meanLinearLuminance` would have been wrong here: that
  rule assumes the tint and texture multiply in gamma space, while URP converts
  both to linear first. It called for a mean of `0.64`; solving the linear form
  gives `0.35`, and shipping `0.64` would have made every facade in the city
  87% brighter than it is today. The pale chalky result was visible in a shaded
  preview before any of it reached the engine.
- Known limitation, recorded rather than worked around: a repeating sheet
  cannot carry a plinth, because no cell is reliably the ground floor. The
  bible's heavier darker base is not expressible here; the grime runs darken
  the lower part of every floor cell instead.
- Verification: `python tools/build-city-facade-textures.py` — all nine sheets
  pass edge `1.25–3.48` (cap 16), seam `0.25–0.90x` (limit 2.5), contrast
  `99–206` (floor 40), chroma `1.006–1.128` (limit 1.22), mean `0.3496–0.3503`.
  One focused EditMode selection, `CityFacadeAppearanceTests`, 20/20 green.
  Mutation check: dropping the `0.08 m` mass-base term from the vertical phase
  turned the alignment case red with a drift of `0.034` of a cell, which is
  exactly `0.08 / 2.35`; restoring it returned the selection to green.
- The generator's own checks earned their keep twice: the seam ratio caught a
  brick module pitch of `40 px` that does not divide `1024` and so restarted
  mid-brick at the wrap, and then caught Pillow's convolution clamping at the
  border rather than wrapping, which was manufacturing a one-pixel seam on the
  roof gravel. Both are fixed at the source; `wrap_filter` now pads before
  every convolution.

## 2026-08-12 — Bar signs became geometry

- The bar sign was the one part of a facade that did not live in the world: a
  `40 x 48` procedurally drawn pixel sprite on a `BillboardSprite`, turning to
  face the camera while the bracket arm it hung from stayed put. The two came
  apart at any oblique angle, and from the balcony it kept its size and facing
  while every other surface foreshortened.
- It is now a projecting blade sign built from the same collider-free boxes
  and shared material as the rest of the facade, hanging under the existing
  bracket and reading along the street the way a real projecting sign does.
  Eight boxes carry it: two hangers, three panel layers and a three-box
  tankard. Each layer is smaller across the panel than the one behind it but
  slightly thicker across the blade, so the layer behind survives as a border
  without four boxes per frame edge. The palette is the pixel panel's, so the
  bars stay recognisable at the distance they always were.
- `BarBuildingMarker` kept its name, its `BarId` and its place in the
  hierarchy - `Bar Landmark Marker` is still what the balcony reconstruction
  looks for - but it is now a passive identity that records the plates hung
  under it instead of leasing a shared sprite and texture.
- The smoke test's contract moved with it. It used to rotate the camera and
  require every marker to keep facing it; it now captures each plate's world
  pose, swings the camera a quarter turn and requires the signs **not** to have
  moved, and asserts no part of a bar facade billboards at all. The
  shared-asset rule it enforces changed from one shared sprite to one shared
  material, which is the rule this project actually has.
- Verification: `CityScene_BarsHaveUniqueColliderFreeSignGeometry` passes.
  `HomeScene_BuildsWalkableBalconyOnSeededStreet` fails, but **not from this
  work**: it dies earlier at `HomeBalconyPresentationPlayModeTests.cs:215`
  demanding a collider-free exterior reconstruction and finding `Street Lamp
  Chunk` box colliders. Stashing this change and re-running at `16bac4e`
  reproduces it identically, and that commit touches no night, lamp, exterior
  or decoration builder. The failure predates all of it, and it means the
  balcony marker assertion here is compiled but never reached.

## 2026-08-12 — Four presentation defects on the bus, all measured

- **Passengers sat inside the cushion.** The runtime aligns the shared rest
  pelvis to the seat anchor, so the lift has to equal how far a design's own
  seated hips reach below that bone. Nominal `0.015` was guesswork. The
  generator now measures it — `seated_contact_m`, the lowest point of the
  parts bound to `pelvis` and `thigh.L/R` relative to the pelvis — and the
  catalog was sunk by `4.6 cm` (Lampshade), `5.2` (Chair Carrier), `5.4`
  (Long-Arm) and `11.1` (Kettle Hat, whose belly and wide hips reach furthest
  below the bone). Lifts are now the measurement less `0.01 m`, so the cushion
  reads as compressed rather than the passenger as floating, and
  `CityPedestrianRuntimeTests` asserts the declared lift stays inside
  `[contact - 0.03, contact]` so the two cannot drift apart again.
- **The driver sat in his seat the same way**, `2.4 cm` down. Measured at
  runtime instead, because his seated pose is procedural rather than an
  authored clip: his hip geometry reaches `0.0387 m` below the pelvis, so
  `DriverSeatLift` is now `0.029`. The thighs are deliberately excluded from
  that measurement — they slope to the pedals, so their lowest point is a knee
  at `0.355 m`, nothing that rests on a seat.
- **The driver kept staring at the hero through door closing and departure**,
  and since both were then moving relative to each other his head jerked away
  from every stop. `UpdatePlayerFocus` never consulted the doors at all. The
  focus is now gated by `DoorLookWeight`, which already carries exactly the
  right envelope: up through Opening, held while open, down through Closing,
  zero under way. Proximity and permission stay separate — `IsPlayerNearFrontDoor`
  is still the ungated fact about where the hero is, because that is what it
  means.
- **The front wheels steered about the wrong axis.** A probe of the imported
  hierarchy showed the bus up direction reads as `(0, 0, -1)` in a wheel
  pivot's local space, while `ApplySteeringPose` rotated about `Vector3.up` —
  the longitudinal axis, so the wheels leaned instead of turning. Rolling uses
  the local lateral axis, which survives the same mapping, which is why only
  the steering looked wrong. The steering axis is now derived from the model
  once at capture (`ResolveVerticalAxisLocal`) rather than assumed, so a
  re-export cannot silently reintroduce it. The steering wheel already needed
  its own declared `+Z` axis for the same reason — that was the clue.
- Verification: the deterministic art build for the new `seated_contact_m`
  measurement, then one focused EditMode selection over the pedestrian, bus
  runtime, bus asset-import and stop-wait fixtures — 58/58.

## 2026-08-12 — The bus could not cover the last 30 cm into a stop

- Reported as: the bus drove up to stop `02`, stood there with its doors shut
  for about fifteen seconds, and only then opened up and let the waiter on.
  The NDJSON ruled out my first two guesses outright — no `service_hold_expired`,
  so the dwell timer was never frozen, and `board_started` -> `board_completed`
  in `4.5 s` against an `8.36 s` budget, so the transfer itself was healthy.
  Whatever happened, it happened *before* the dwell began.
- A four-lens audit with an adversarial refutation pass found it, and two
  independent lenses reached it separately. `MoveAlongRoute` discarded any
  frame whose travel was under `DistanceTolerance = 0.02 m` rather than
  carrying it forward. It is a latch, not a rounding loss: the discarded
  travel leaves the distance unchanged, so the braking-curve speed cap is
  unchanged, so the next frame is under the threshold too. `BeginDwell` never
  runs, and since the doors are driven only from the dwell timer they never
  open.
- **The regime matters, and my first explanation of it was wrong.** I wrote
  that a `60 fps` cruise approach latches once the stop is within `0.31 m`.
  A faithful float32 replay of `AdvanceMotion` + `MoveAlongRoute` says that
  regime did not occur here: the session ran at a `25 ms` median frame
  (~40 fps), and at `40 fps` a clean cruise approach **arrives every time** —
  the cruise path only starts latching from `44 fps` up. Coming down from
  cruise, `MoveTowards` saturates at `ServiceDeceleration * deltaTime` and
  keeps the bus overspeed against the curve, so it punches through the band.
- What actually bit is the **from-rest regime**. Setting off again from a
  standstill or any low speed, the bus never rises above the band at all: at
  `40 fps` a frame commits motion only while `v > 0.80 m/s`, and the curve
  drops under that `0.14 m` from the stop. The replay latches on 100% of
  from-rest approaches within `12 m`, resting `2-12 cm` short — i.e. visually
  docked at the stop, which is exactly what was reported. Escape needs one
  frame long enough to clear `2 cm` at the pinned speed, `28-61 ms` against a
  `25 ms` median: ordinary jitter, hence an arbitrary duration, an
  instantaneous release, and a textbook dwell afterwards.
- **So the trigger was probably a yield after all**, and my "ruled out"
  verdict on that was too strong. A yield only has to last a fraction of a
  second to zero the speed (`travel = safeTravel; speed = 0f`); the latch then
  supplies all fifteen seconds. That dissolves the objection that nobody saw
  anyone standing in front of the bus. `JunctionSpeed = 3.2` can arm it the
  same way with no obstacle at all.
- The fix carries the residual instead of dropping it: one `pendingTravel`
  field accumulated per frame and drained by the loop. `DistanceTolerance` is
  untouched, which matters because it appears in fourteen places including the
  arrival test itself (`distanceToStop <= tol && speed <= tol`, `:869`) —
  lowering it would have made arrival *stricter*. The same discard also hit
  every other slow-motion case: recovering from a yield, crossing to the next
  link, crawling a junction.
- **This was never an NPC bug.** `MoveAlongRoute` is original route code. The
  ambient passengers only made it visible: nobody used to watch the bus stand
  at a stop, and now somebody is standing there failing to get on.
- Why no test caught it: every bus test stepped at `0.05 s`, where the freeze
  band is `0.034 m` and hides inside the arrival tolerance.
  `ServiceApproach_ReachesTheStopAtRealFrameRates` now runs at `1/30`, `1/40`,
  `1/45`, `1/60`, `1/120` and `1/144`, and
  `ApproachResumingFromAYield_StillReachesTheStop` covers the regime that
  actually bit: hold the bus at a dead stop with an obstacle, release it, and
  require it to reach `Dwelling`. Mutation-checked: restoring the discard
  fails every frame-rate case with the bus stuck in `ApproachingStop` after
  sixty simulated seconds.
- Two follow-ups from the audit are now in. A full yield explicitly clears
  `pendingTravel`, so "a bus stopped for a person does not creep" is a stated
  contract rather than an accident of the loop threshold. And a stall
  watchdog reports `approach_stalled` once after `2 s` motionless short of a
  stop, carrying state, distance, speed, requested travel, `deltaTime`,
  `must_stop` and forward clearance — the one record that separates every
  hypothesis this investigation had to eliminate by simulation, plus a
  matching `approach_released`. It also corrects a comment of mine on
  `MaximumServiceHoldDuration` that claimed a leaked hold strands the bus with
  its doors *shut*; a hold can only be taken while they are fully open, so it
  strands them open. That false comment is what kept regenerating the leaked-
  hold hypothesis.
- **Correction to the previous entry.** The "waiter blocking its own bus"
  diagnosis was wrong, and so were its numbers. I computed the corridor from
  `ObstacleStopPadding = 0.38`, which belongs to `OverlapsDynamicObstacle`
  (`CityBusDirector.cs:554`), a spawn-overlap check. Yielding uses
  `lateralLimit = halfWidth + targetRadius + ObstacleLateralPadding`
  (`CityBusActor.cs:665`) — `1.71 m` for the hero, `1.74 m` for a walker, so
  someone on the sidewalk centreline clears it by `0.26-0.29 m`, not by
  `0.08 m`. The route-bound exemption is harmless and still defensible, but it
  was not what fixed anything and its comment needs rewording.
- Still open, found by the same audit and not yet acted on: the obstacle test
  also samples `player.position + playerVelocity * 0.75 s` with unsmoothed
  velocity, which widens the blocking corridor by up to `3.9 m` at run speed —
  wide enough that walking toward the bus in order to board it stalls it.

## 2026-08-12 — An end-to-end proof for ambient passengers

- Four separate defects broke ambient boarding in turn, each reported from a
  playtest, and every one of them left the planners, the occupancy rules and
  the asset contracts green. Nothing walked a passenger from the pavement into
  a seat and back out, so `Assets/Tests/PlayMode/CityBusNpcPassengerPlayModeTests.cs`
  now does exactly that against the production bus prefab and the real
  pedestrian pool: waiter appears, boards, is seated with `07` still free, and
  alights at a later stop with its dwell hold handed back.
- Three things about the harness were worth learning the hard way:
  - `passengers.enabled = false` silently kills the controller, because
    `OnDisable` calls `Shutdown`. The directors have to stay enabled and drive
    themselves from `LateUpdate`.
  - A nested `yield return SomeEnumerator()` is not driven by the test runner,
    so the phase loops run inline. The first version looked like a stuck bus
    when in fact nothing was advancing it.
  - `Time.deltaTime` in a batch run was observed at `0.006 s` on one attempt
    and pinned to the `6.7 s` ceiling on another, so frame budgets are
    meaningless and, worse, the bus, the walkers and the transfer budget can
    end up on different clocks — a service hold then expires under a passenger
    who is still walking. `Time.captureDeltaTime` pins one fixed step for
    everything, and the whole test runs in about three seconds.
- The coverage was mutation-checked rather than trusted: reintroducing the
  hero-only opposite-driver invariant makes it fail with the passenger riding
  past ten stops without ever getting off, which is precisely the reported
  symptom.

## 2026-08-12 — The waiter was blocking its own bus

- Reported as: the bus pulled up to stop `02`, the driver halted, the doors
  never opened, and a waiting walker stood there. The NDJSON was silent — no
  `board_started`, no `board_blocked` — which located it precisely, because
  the only silent guard in `TryBeginBoarding` is `DoorsFullyOpen`. The doors
  were the problem, not the boarding.
- The cause is geometric and self-inflicted. A `1 m` sidewalk minus a `0.35 m`
  capsule and two `0.15 m` navigation margins admits **exactly one** lateral
  position, `3.50 m` from the road centre; there is no freedom to place a wait
  slot anywhere else. The halted bus flank is at `2.69 m`, so the waiter
  stands `0.81 m` clear while the obstacle corridor reaches
  `AgentRadius + ObstacleStopPadding = 0.73 m`. That `0.08 m` of daylight is
  narrower than the walker's own `0.15 m` shoulder-shift, so a waiter that
  leans road-ward puts the bus into `Yielding` short of the stop. It then
  waits forever for a bus that can never serve it, and the bus never dwells,
  so nobody else boards either. A deadlock built out of two individually
  reasonable numbers.
- The slot cannot move, so the exemption moves instead: the bus obstacle test
  now skips `IsRouteBound` walkers, not merely `IsAttachedToVehicle` ones. A
  walker heading for a stop or standing at it is this bus's passenger, which
  is the same reasoning already accepted for the hero's door dock — his dock
  is deliberately kept outside the corridor so a waiting passenger cannot stop
  the bus reaching its service pose. Ordinary roaming walkers keep their
  yielding untouched.
- Found while investigating: `AdvanceWaiter` dropped a tracked record on two
  paths — walker gone, walker no longer route-bound — **without releasing its
  service hold or its cabin seat**. A leaked hold freezes `dwellElapsed`, the
  door timeline is sampled from that timer, and the next `BeginDwell` resets
  the timer to zero it can never leave: the bus would be stranded at every
  later stop with sealed doors for the rest of the session. Both paths now go
  through one `ReleaseWaiterOwnership` that always hands ownership back and
  warns when it actually reclaimed something. `CityBusActor` also bounds the
  freeze at `DwellDuration + 5 s` and reports `service_hold_expired`, so a
  future leak degrades to a hiccup with a named cause instead of a dead route.
- The existing `PassengerServiceHold_...` case advanced `DwellDuration * 2` in
  one step to prove the freeze, which now trips that bound; it advances
  `DwellDuration + 2` instead, still past the dwell it would otherwise have
  completed.

## 2026-08-12 — Route 01 stops zigzagging

- Reported as "the route and the stop order are extremely illogical", and the
  measurement agreed. `CreateStopTargets` ordered its targets by
  `GetDistrictOrder` — a hardcoded enum, Industrial `0` through Old Town `3`,
  home appended last — which is nominal and contains no geography at all.
- On the default layout that produced: Industrial `(-131, -13)` far west,
  Nightlife `(13, -79)` south centre, Residential `(128, 117)` far north-east,
  Old Town `(-131, 65)` **back to the west edge**, Home `(121, -1)` **out east
  again**. Two full crossings of the city per lap. Straight-line tour between
  stops `1166 m` against a best possible `754 m`; the road loop it forced was
  `2592 m`, `3.4x` the straight tour.
- The order is now a shortest closed tour over the target centres. Five
  targets are solved exactly — fix the first, permute the rest, `(n-1)!` — and
  a layout above `8` falls back to nearest neighbour plus 2-opt. Ties break on
  the ordered target IDs, and the cycle is rotated so `PlayerHome` is served
  first with its direction fixed the same way, so the same layout and seed
  always yield the same loop.
- Result on the default layout: Home, Residential, Old Town, Industrial,
  Nightlife and back — a clean ring with no doubling back. Straight tour
  `754.3 m`, exactly the optimum. **Road loop `2592 m` -> `1798 m`, a `31%`
  cut**, and the loop-to-straight ratio fell from `3.4x` to `2.4x`.
- Only the ordering changed. The accepted-link graph, the right-hand rule, the
  `6 m` left turns, the safe-right macro and every full-body clearance proof
  are untouched, which is why the whole existing planner suite still passes
  unmodified. The remaining `2.4x` is the street grid plus the turn
  restrictions; shortening that means touching the connector search, which was
  deliberately left alone.
- Verification: one focused EditMode selection over `CityBusPlannerTests`,
  `CityBusStopWaitPlannerTests`, `CityBusRuntimeTests` and
  `CityMapBusOverlayTests` — 36/36, including the new
  `ServedOrder_IsAShortestClosedTourStartingAtHome`, which asserts home is
  stop `01`, that the served order is within `5%` of the exact optimum over
  the real stop positions, and that a repeated build gives identical stop IDs.
  Not run: PlayMode, the full EditMode suite, any player build.

## 2026-08-12 — Ambient passengers ride Route 01

- The measurement that decided the design: every walker design is the *same*
  31-bone rig at the same rest pose. `Assets/Pedestrians/Models/*.json` agree
  bone for bone — pelvis head at `0.70 m`, envelope `1.75 m`, identical
  `localBounds.y`. "Different models" is mesh proportion and worn objects, not
  skeleton, so seating is **one** rule for all of them: align the shared rest
  pelvis to the cushion anchor, exactly as `CityBusDriverPresentation` already
  seats the driver. Per-design work then reduces to an authored seated posture
  and a declared clearance, not per-design maths.
- Sole pinning had to be switched off while seated. `GroundFeetToPresentationRoot`
  pins the lowest boot to the actor-root plane every frame; on a seat that
  drags the whole model down until the feet touch the cabin floor.
  `CityPedestrianPresentation` now runs a three-input mixer (Idle/Walk/Sit) and
  swaps the pin for pelvis alignment while seated.
- Four authored `Sit` clips joined the deterministic Blender library
  (`LampshadeSit`, `ChairCarrierSit`, `KettleHatSit`, `LongArmSit`), taking it
  from 10 clips to 14. They are excluded from the footwear bake — a seated clip
  leaves the pavement plane on purpose — and prove a different contract
  instead: measured headroom above the seated pelvis inside a declared band,
  and nothing hanging more than the `0.41 m` cushion height below it. Measured
  `1.030 / 1.055 / 1.050 / 1.050 m` headroom and `0.354-0.374 m` drop; the
  cabin gives `2.05 m` floor-to-ceiling, so the whole catalog clears the roof
  with room to spare.
- The Helmet Lamp Hopper declares no seated ride. It has no seated posture to
  author on `0.46 m` hind feet, and its worn Spot is the one working light the
  pedestrian contract allows — it does not belong in a cabin.
- `CityBusActor` grew from one passenger and one exclusive service hold to a
  three-place cabin with a shared, per-owner hold. The exclusivity had to go:
  with one hold, an ambient passenger stepping through the doorway would have
  silently made the hero's own `E` prompt fail. `CityBusDirector`'s passenger
  cleanup became multicast for the same reason. The release post-condition is
  unchanged — no occupant may remain when the presentation is pooled.
- Recycling now keys on `HasPlayerPassenger`, not `HasPassenger`. Blocking the
  single actor slot because an ambient rider is aboard would strand the bus for
  a whole lap; a rider `92 m` away behind fog is released with it instead.
- `CityBusRidePlan.TryCreate` turned out to be agent-agnostic apart from two
  hard-coded facts. Parameterising seat index, agent radius and grounded-root
  offset was enough to reuse the whole validated dock ladder for a walker, so
  ambient boarding inherits the curb/apron height resolution the hero already
  had rather than re-deriving it.
- Routing to a stop reuses the population director's existing guidance shape
  (`approachTarget` + a node-distance field feeding `SelectClosestCandidate`),
  but seeded at the stop instead of the player. Stops never move, so the
  Dijkstra runs once in `CityBusStopWaitPlanner` rather than being re-searched
  every few metres the way player guidance must be.
- Wait slots sit `0.70 m` road-ward of the blue pole. The pole is deliberately
  `0.2 m` outside the walkable strip and carries a collider, so waiting at
  `ShelterPosition` was never an option. The two slots queue along the lane at
  `+0.30 m` and `+1.40 m`, which also keeps them clear of both door entries
  (`+3.05 m` front, `-1.34 m` rear) — the same `1 m`-pavement geometry that
  already rules out walking abreast.
- NPC boarding does **not** go through `PlayerAnimatedInteractionController`.
  That controller is bound to `PlayerRuntime` and `IPlayerClipPresentation`,
  and `ai/contextual-animation-standard.md` explicitly does not govern NPC
  animation. A short scripted doorway walk with a `2.5 s` abort covers it.
- **Playtest fix — nobody boarded.** A waiter stood at the stop and the bus
  pulled up, but `board_started` never appeared in the NDJSON while
  `waiter_recruited`/`waiter_spawned` did, so boarding was refused before it
  began. The passenger door dock is pushed outward to `3.38 m` from the road
  centreline, the pedestrian lane band is `3.15-3.85 m`, and a `0.35 m` capsule
  there needs `3.03-3.73 m` — the dock overhangs the curb by `0.12 m`. Since
  the dock ladder offsets run *along* the bus and not across it, every
  candidate failed. The hero never hit this because his controller is given
  `World.WalkableArea`, which includes the carriageway. The controller now
  takes that same road-inclusive area, alighting targets the stop's proven
  pavement wait slot instead of the road-side dock, and a `board_blocked`
  warning names the refusing guard once per changed reason so the next failure
  is readable rather than silent.
- **Spawned cabins are not empty.** A bus that has notionally been circling
  its loop should not always pull up with nobody in it, so activation now
  seats a seeded `0-2` ambient passengers. Two things made that awkward and
  both are worth remembering. First, a full ride plan needs a served stop and
  two validated roadside docks, and a spawning bus is cruising — so
  `CityBusRidePlan.TryCreateSeatedPose` resolves the actor-local seat floor
  from the seat anchor plus the cabin-floor door anchor alone. Second, the
  spawn collision probe rejects a capsule overlapping the bus body, which is
  precisely the situation here, so seated spawns opt out of that one probe
  while every other spawn keeps it. The draw is `hash % (max + 1)`, so an
  empty bus stays a real outcome, and it draws against `MaximumNpcOccupants`
  rather than `CabinCapacity` so the hero's place survives.
- **Second playtest fix — the seat side, not the dock.** Ambient passengers
  still neither boarded nor alighted, and `board_blocked` named
  `no_door_dock`. The road-inclusive area had been necessary but not
  sufficient: `CityBusRidePlan.TryCreate` also enforces `driverSide *
  passengerSide < 0`, and the ambient seat order starts at index `2` on the
  driver's side. Seven of its eleven seats are, so nearly every plan was
  rejected — the exit plan included, which is why nobody got off either. The
  preload had worked only because `TryCreateSeatedPose` never ran that check.
  The rule is hero-only: seat `07` must be opposite the driver because his
  authored `BusRideLoop` and the window camera are built around that lateral
  side, and an ambient passenger has neither. It is now an explicit
  `requireOppositeDriverSide` parameter, true for the hero and false for
  everyone else. Lesson: `board_blocked` earned its keep, but one reason
  string covered two independent guards.
- **Teardown throw.** `CityBusDirector.Shutdown` hit "Passenger cleanup must
  release the city bus passenger before its presentation is pooled". Cleanup
  decided who was aboard by reading the *walker's* motion state, and on
  teardown `CityPedestrianDirector.OnDisable` may pool its actors first and
  reset them to `Dormant`, so the loop skipped a real occupant. The bus is the
  authority on its own cabin: cleanup now calls `ReleasePassenger` for every
  tracked record and uses its return value, which is order-independent.
- **Third playtest fix — the transfer could never finish.** The log told the
  whole story: `board_started` followed by `transfer_aborted` exactly `2.525 s`
  later, three times, and never a `board_completed`. `TransferTimeout` was a
  flat `2.5 s` guess. Measuring the real walk against the bus manifest: the
  aisle leg runs `1.16-2.56 m` when the door is chosen sensibly, the pavement
  leg is about `3 m`, and the four riding designs walk at `0.72-1.30 m/s` — so
  a real transfer needs `4.7-7.7 s`. No single constant fits a spread that
  wide, which is why every ambient passenger aborted at the doorway and the
  one preloaded rider bailed out at the same instant.
  The budget is now derived per transfer from the measured path and that
  walker's own pace, clamped to `[3 s, one dwell]`. The door is also chosen by
  the whole journey rather than by which one the walker stands nearer: the two
  doors are `4.39 m` apart on the same kerb, so the old rule could send a
  passenger `6.60 m` down the aisle where `2.56 m` was available. Authored
  pace is kept rather than hurried, because each design has its own cadence
  and speeding the root would read as foot-sliding.
- Verification: `blender --background --python
  tools/build-city-pedestrian-3d-model.py` — the deterministic validator that
  owns the seated clearance bands, the 31-keyed-bone contract, in-place/no
  root motion and the repeat-signature determinism check. Then one focused
  EditMode selection over `CityBusStopWaitPlannerTests`, `CityBusRuntimeTests`
  and `CityPedestrianRuntimeTests`, re-run after the fix with the regression
  cases `PassengerDoorDock_NeedsTheRoadInclusiveArea`,
  `CabinPreload_NeverFillsThePlaceReservedForTheHero` and
  `FilledCabin_StillAdmitsTheHeroToSeat07` — which drives the order a
  preloaded cabin actually produces, ambient passengers first and the hero
  second — and `AmbientSeatOrder_SpansBothSidesOfTheCabin`, which reads the
  real bus model manifest and pins the fact that seven ambient seats sit on
  the driver's side while seat `07` does not. A further
  `TransferBudget_CoversTheRealWalkForEveryRidingDesign` reads the same
  manifest and asserts the budget exceeds the walk each riding design actually
  has to make, so an unreachable timeout cannot return. Final selection:
  47/47. Not run: PlayMode,
  the full EditMode suite and any player build. The board/ride/alight sequence
  itself still has no automated coverage — it needs a scene fixture — so the
  playtest remains its proof.

## 2026-08-12 — Walkers give way along the lane

- Measured the geometry before designing, and it decided the design: sidewalks
  are `1 m`, the lane corridor is `±(AgentRadius + NavigationMargin) = ±0.5 m`,
  and `RoadWalkableArea.Contains` requires the whole `0.35 m` disc inside, so a
  walker has `±0.15 m` of lateral room. Two walkers need `0.70 m` of separation
  to pass. **Walking around each other across the lane is impossible on this
  pavement**, so no amount of steering work would have produced it.
- Avoidance therefore works along the lane, in three parts:
  - A shoulder-shift of up to `0.15 m` away from whatever is ahead,
    implemented as steering toward an offset point rather than the node, so it
    re-centres on its own. Arrival became radius-based (`0.18 m`) because an
    offset walker never lands exactly on a node.
  - Queueing: a walker travelling the same way as the one ahead drops to that
    leader's pace instead of stopping dead and setting off again. The old
    behaviour stuttered.
  - A blocked-time escape: wanting to move and not moving accumulates, and
    after `1.5 s` the walker turns back. From the actor's side a prop and
    another walker are the same problem, so both get the same way out, and it
    is self-clearing because ordinary continuation already refuses to
    backtrack — the node behind hands it a different branch.
- `ShouldYield` became `ResolveAvoidance`, which still returns "must stop" but
  now also sets a speed scale and a lean bias per walker. Stopping is the last
  answer rather than the only one. Head-on ties are still broken by stable slot
  order, so that contract is unchanged.
- This mattered more after the population went from 2 to 8: two walkers meeting
  head-on used to stand nose to nose until the distance rule released one.
- Verification: `CityPedestrianRuntimeTests`, 21/21, including two new focused
  cases — a walker held indefinitely turns back exactly once and only after the
  threshold, and a queued walker keeps moving, leans within the lane and
  re-centres when clear.

## 2026-08-12 — The hopper stops hovering

- Reported symptom: the Helmet Lamp Hopper renders above the pavement.
- The source clips are not at fault. `CityPedestrianLocomotion.json` reports
  `ground_min_m = 0.0` for both `HelmetLampIdle` and `HelmetLampHop`, and Idle
  stays within `0.0097 m` of the ground for its whole cycle, so Blender exports
  him planted.
- Root cause is the gap the airborne exception opened. Every other design has
  its lowest sole pinned to the presentation root every frame, which also
  absorbs whatever the Avatar's motion-node extraction adds between the proven
  clip and the rendered pose. `PreservesAirborneMotion` made
  `CityPedestrianPresentation` skip that correction *entirely*, so for this one
  design the offset had nothing cancelling it.
- Built the missing instrument first:
  `CityPedestrianAirborneGroundingPlayModeTests`. Getting a *valid* reading
  took four attempts, and the first three were silently inert — worth recording
  because each looks like a passing measurement:
  1. Skinned `Renderer.bounds` never recompute without a render pass, so the
     samples reported the bind pose. Two different import settings produced
     bit-identical numbers, which is what exposed it.
  2. Bone transforms fixed that, but the presentation selects
     `CullUpdateTransforms` and a batch-mode run never renders, so the rig was
     never driven at all: the measured arc was exactly `0.0`.
  3. Adding a camera did not help — batch mode still does not render. Only
     forcing `AlwaysAnimate` in the test drove the rig.
  The test now asserts the rig actually moved before trusting any sample.
- With a working reading: the hop is a real `0.272 m` arc, so `lockRootHeightY`
  had to go back to `true` for airborne clips after all. The previous session's
  note that locking "stripped the hop" was never verified — it was written in
  the same session that found its own grounding test inert. Baking is what
  *preserves* the arc here, because the presentation runs `applyRootMotion =
  false` and unbaked height is extracted to root motion and thrown away. The
  FBX was reimported so the setting is live.
- The remaining lift is not a clip defect. Every other walker's per-frame sole
  pin also absorbs the height the shared Generic Avatar adds when retargeting a
  skeleton whose proportions differ from the hero's, and this squat design has
  no such pin. It is now declared as
  `CityPedestrianArchetype.GroundTrim` (`0.05 m` for the hopper) and applied to
  the model root.
- **The exact trim is a visual call and is not machine-settled.** The
  instrument's absolute zero is unreliable: it approximates a sole as a fixed
  drop below its foot bone and so ignores foot rotation, and the idle and hop
  clips answer the same world-space offset by different amounts, so no single
  constant grounds both. The test therefore reports absolute heights and gates
  only on the vertical travel, which it measures soundly. Nudge `GroundTrim` if
  the hopper still reads high or starts to sink.
- Verification: the new PlayMode test passes, and the focused
  `CityPedestrianRuntimeTests` EditMode selection was rerun for regression.

## 2026-08-12 — A populated daytime street

- Replaced the single `MaximumActiveModels = 2` constant with
  `CityPedestrianPopulationProfile`, so each runtime scales on its own anchor
  budget: City `8` day / `3` night over a `13`-model pool, Home balcony `5` / `2`
  over `8`. `CityGameRoot` and `HomeInteriorRoot` now log the resolved caps.
- The pool repeats designs. `CityPedestrianArchetype.MaximumPoolInstances`
  makes that safe: `CreatePoolComposition` deals every design once and then
  round-robins the remainder while respecting each limit, and the Helmet Lamp
  Hopper declares `1` because it wears the only working light. The factory
  validator changed from "one model per design" to "every design present, none
  over its declared limit".
- One spawn event now activates up to two walkers, and the cadence depends on
  whether the street is full: `0.4-2 s` while below target, the original
  `3.5-12.5 s` once only replacements remain. Night keeps one walker per event
  and its long delays throughout.
- Added dispersion: a candidate anchor must keep `12 m` from every active
  walker and no more than two walkers share one sidewalk lane, derived from the
  anchor ID without allocating. The fallback ladder now gives up connectivity
  before dispersion, since a distant walker still reads as city life and two
  stacked on one lane does not.
- Approach guidance is capped at two concurrent walkers; everyone else takes a
  seeded 50/50 initial direction with no player-proximity preference at all.
  Eight walkers all steered at the hero read as pursuit, not as a city.
- Added a forward-travel bias: above `3 m/s` smoothed player speed, selection
  prefers anchors in the forward half-plane. This is what makes the bus ride
  work — at `6 m/s` anything spawned behind is outrun before it can be seen. A
  per-frame jump beyond `12 m` is treated as a teleport and clears the heading.
- Performance work that the larger population made mandatory:
  - `RefreshInitialApproachRoutes` ran an `O(V^2)` Dijkstra over the whole
    graph (169 layout nodes expand to a much larger pedestrian graph) on every
    change of the nearest node. It now uses a binary heap with lazy deletion
    and only recomputes after the player has moved `4 m` *and* the per-component
    target actually changed. Scratch arrays are reused instead of reallocated.
  - Candidate search probed `Physics.CheckCapsule` on every one of the 210 city
    anchors that passed the distance filter. It now collects eligible anchors
    into reusable buffers and probes at most `4` sampled picks, and one
    `Physics.SyncTransforms` covers a whole spawn batch instead of one per
    spawn.
- **A nearer spawn ring was proposed, implemented, and then rejected on
  evidence.** The plan called for a `44-56 m` fog-hidden ring to fill the
  street faster. The existing fog proof in `CityPedestrianRuntimeTests`
  measures transmittance along the view axis *at the frustum corner*, which is
  only `0.574` of the radial distance — a factor omitted when the ring was
  proposed. At `44 m` that leaves `16%` transmittance against an accepted
  `0.2%` bound, and the bound is not met until roughly `72 m` radially, which
  is the existing `76 m` band. The ring was removed; the population increase,
  batch fill and forward bias deliver the goal without it. The test now proves
  the bound for the whole active population rather than the first pair.
- Verification: `CityPedestrianRuntimeTests` EditMode selection, 19/19 passed.
  Not run, and not required by the change: PlayMode, the remaining EditMode
  fixtures, a player build. The Home balcony population is a first estimate —
  `HomeInteriorRoot` now logs how many of its 16 anchors fall in the spawn band
  and in the connected fallback band, so it can be tuned against measurement
  rather than guessed again.

## 2026-08-12 — Helmet Lamp Hopper, a worn light and airborne clips

- Added the fifth city walker, `helmet_lamp_hopper_v1`: a squat miner in ochre
  work wear with a hi-vis band, a battered pale helmet, a lamp housing wired
  down to a belt battery box, and `0.46 m` hind feet. 37 meshes, 1084
  triangles. It never takes a step — `HelmetLampHop` is a two-footed rabbit
  bound through crouch, launch, a tucked airborne apex and landing, and it is
  the fastest walker at `1.32-1.48 m/s`.
- It carries a real always-on shadowless Spot (`7.5 m`, `3.6` intensity,
  `58°/26°`) parented to the animated head bone at the lens. The pool holds one
  hopper, which is what caps such lights in the world at one; the beam is left
  on regardless of the city clock because its owner switched it on.
- Three contracts had to be relaxed, each by explicit declaration rather than
  by a blanket exception, so an accidental violation still fails:
  - `ArchetypeSpec.airborne_lift_m` replaces the every-frame sole rule with
    "never penetrates, lands at least once, reaches this apex band". Airborne
    clips get one constant pelvis lift instead of a per-frame correction,
    because a per-frame correction pins the lowest sole to the road on every
    sample and silently turns a hop into a shuffle.
  - `PedestrianDescriptor.CarriesHeadLamp` turns the prefab validator's blanket
    "no Lights" ban into a declared-count check, and additionally requires the
    lamp to be a bounded shadowless Spot registered on the head bone.
  - `CityPedestrianAssetRegistry.PreservesAirborneMotion` makes
    `CityPedestrianPresentation` skip its per-frame sole pin for that design.
- Found and fixed a silent import defect: the clip importer set
  `lockRootHeightY = true` on every clip, and this Avatar treats the pelvis as
  the motion node, so the hop — authored on the pelvis — was being stripped at
  import. Root-height locking and loop-pose normalisation are now off for
  airborne clips only.
- **Found a pre-existing inert test.** `AssertWalkSolesStayGrounded` drove a
  PlayableGraph and compared sole heights across 12 phases, but a PlayableGraph
  writes no transforms in a batch-mode EditMode run: every phase returned the
  identical rest pose, so the assertion had been comparing a static pose to
  itself for all four earlier archetypes. Diagnostics confirmed the head bone
  sat at exactly `1.4300` in every phase. `AnimationClip.SampleAnimation` does
  drive the rig, but it produced up to `0.58 m` of sole travel for ordinary
  walkers — it bypasses the Avatar path the runtime uses — so rather than fit
  the test to unexplained numbers, the helper was narrowed to what it can
  honestly prove (presentation wiring and sole-renderer presence) and renamed
  `AssertSolePresentationWiring`. Grounding, hand clearance and hop apex are
  now asserted from the generator's shipped locomotion manifest, which is real
  data that Unity imports.

Verification:

- Primary check, the deterministic Blender 5.0.1 validator: `CITY PEDESTRIAN
  ART BUILD OK`. Five models, ten 31-bone loops, every archetype grounded
  against its own footwear, `airborne helmet_lamp_hopper_v1: 0.241 m apex
  lift`, repeated model signatures matched.
- Reviewed the model preview and the five-row contact sheet directly; two
  geometry iterations were needed for value separation, and two animation
  iterations to deepen the crouch and tuck the forepaws.
- Secondary check, one filtered EditMode selection: `CityPedestrianRuntimeTests`
  passed `18/18` in `0.89 s`, including the new fifth prefab case, the worn-lamp
  assertions and the manifest apex contract.
- Complete suites, player build and packaged smoke were intentionally not run.

## 2026-08-12 — Long-Arm Walker and animated hand-clearance validation

- Added the fourth city walker, `long_arm_walker_v1`: narrow and tall in cold
  steel blue, small skull sunk into raised shoulders, eyes almost at the
  hairline, no mouth, and bare pale forearms roughly `3.3x` their bone length
  hanging to the ankles under oversized hands. 35 meshes, 1044 triangles.
- Deliberately the first design whose strangeness is the body itself rather
  than a worn or carried object. Another object-bearer would have collapsed
  into the Chair Carrier's slot; the family trait is "a body treated as
  furniture, worn matter-of-factly", not "wears a thing", so this walker
  extends the motif instead of repeating it.
- `LongArmIdle` (`2.5 s`) holds a dead-still torso under an arm sway that never
  settles; `LongArmWalk` (`1.5 s`, deliberately twice the Kettle Hat's cycle)
  shuffles on barely lifted feet while the arms reach their extremes on the
  passing poses — a quarter cycle behind the legs, so the limbs read as
  pendulums the body drags rather than an ordinary counter-swing.
  Movement `0.72-0.84 m/s` on `0.86-0.94x` clips makes it the slowest walker.
- The visible forearm hangs almost straight down from the elbow rather than
  following the outward A-pose bone axis: extending it along the bone would
  breach the `1.65 m` rest-width guard, and hanging a long segment below its
  own pivot is what produces the pendulum once the shoulder rotates. The hair
  is a close cap that never widens past the skull, because an overhanging brim
  would echo the Lampshade Walker.
- Added `hand_clearance_m` to the archetype contract and an animated
  hand-to-pavement check to `validate_animated_grounding`. Footwear grounding
  could not express this: a design whose hands hang near the ankles pushes them
  through the road while every sole still reports perfect contact. The check
  earned itself immediately — the first authored pose failed at `0.174 m`, above
  the band ceiling, so the hands were lowered until the reach was real.
- Unity side: descriptor, paths, import tracking, a collider-free passive
  `Resources/Pedestrians/LongArmPedestrian3D.prefab` and the catalog entry.
  Runtime again needed no change; the pool is now four against a two-slot cap.

Verification:

- Primary check, the deterministic Blender 5.0.1 validator: `CITY PEDESTRIAN
  ART BUILD OK`. Four models (`1160` / `1032` / `1356` / `1044` triangles),
  eight 31-bone loops, zero loop error and zero root translation, every
  archetype grounded against its own footwear at `0.0` gap and `0.0`
  penetration, Long-Arm hand clearance `0.107 m` in both clips, repeated model
  signatures matched.
- Reviewed the model preview and the four-row contact sheet directly; three
  geometry iterations were needed, mainly to stop the hair reading as a
  Lampshade-like brim.
- Secondary check, one filtered EditMode selection: `CityPedestrianRuntimeTests`
  passed `17/17` in `0.74 s`. Only the fourth `[TestCase]` and two explicit
  arrays needed editing — the pool-size and clip-count assertions were already
  parameterised on the catalog and followed automatically.
- Complete suites, player build and packaged smoke were intentionally not run.

## 2026-08-12 — Kettle Hat Walker and per-archetype clip grounding

- Added the third city walker, `kettle_hat_walker_v1`: a stout short-legged
  figure whose overhanging belly hides the upper legs and whose oversized
  skewed enamel kettle — body, rim band, shoulder, lid, knob, sideways spout
  and handle arc — owns the top of the silhouette while the face stays visible
  under the rim. 42 meshes, 1356 triangles, muted plum coat against the
  Lampshade's green and the Chair Carrier's orange.
- Kept the shared `1.75 m` envelope, the exact 31-bone Generic rig and the
  fixed collider. The short read is authored as proportion: the human mass
  ends near `1.40 m`. Lowering the visible torso further was rejected because
  the arms would then swing around bone pivots they no longer sit near, and a
  genuinely shorter walker would need its own collider parameterisation.
- Replaced the two-way `lampshade / else chair` geometry branch with an
  explicit per-archetype builder map that raises on an unregistered key
  instead of silently falling back to another design.
- **Fixed a real defect:** `build_animation_library` built only the Lampshade
  model and baked and verified every clip against its footwear, so the Chair
  Carrier clips were already grounded against the wrong boots. Grounding is
  now proved per archetype — each design is rebuilt in its own scene and only
  its own clips are baked and validated against its own soles. The baked
  pelvis track is captured as plain per-frame data and re-keyed onto the
  shared library, so the exported clips carry exactly the correction proved.
- Extended the shared locomotion library from four clips to six with
  `KettleHatIdle` (`1.75 s`) and `KettleHatWalk` (`0.75 s`), and made the
  review contact sheet size itself from the catalog, one row per archetype.
- Wired the Unity side: descriptor, paths and expected clips in
  `CityPedestrianAssetSetup`, import tracking in `CityPedestrianModelImporter`,
  a collider-free passive `Resources/Pedestrians/KettleHatPedestrian3D.prefab`,
  and the catalog entry in `CityPedestrianResources` at `0.90-1.02 m/s` on
  `1.08-1.18x` clips.
- No City, Home, graph or bus change was needed: both scenes already load the
  whole catalog through the shared factory, the director already selects among
  free presentations by spawn seed, and the bus already yields to any actor.
  The pool is now larger than the two-slot active cap, so repeat encounters can
  vary the visible pair while two concurrent walkers stay distinct.

Verification:

- Primary check, the deterministic Blender 5.0.1 validator: `CITY PEDESTRIAN
  ART BUILD OK`. Lampshade `38` meshes / `1160` triangles, Chair Carrier `35` /
  `1032`, Kettle Hat `42` / `1356`; six 31-bone loops with zero loop error and
  zero root translation; each archetype reported grounded against its own
  footwear with `0.0` contact gap and `0.0` penetration on every frame;
  repeated model signatures matched.
- Reviewed the generated preview and the three-row contact sheet directly;
  three geometry iterations were needed before the silhouette read as short and
  plump rather than boxy (coat hem slab, hidden face, undersized kettle).
- Secondary check, one filtered EditMode selection: `CityPedestrianRuntimeTests`
  passed `16/16` in `0.71 s`, including the new third parameterized prefab case,
  the six-clip library expectation, the catalog-availability case and the
  pool-equals-catalog-size expectations.
- Complete EditMode/PlayMode suites, a player build and a packaged smoke were
  intentionally not run; this is fast mode.

## 2026-08-12 — Documentation drift repair and retention rule

- Corrected the stale `AGENTS.md` baseline: it still listed four build scenes
  while the project ships seven. It now names all seven in build order, marks
  the five gameplay roots, and adds the Editor/TestSupport assemblies and the
  `tools/` generators.
- Rewrote `README.md`, the only human-facing document, which had not been
  touched since `2026-08-04`. It still described an eight-direction sprite hero
  and a city permanently locked in noir night, and covered none of the Road v2
  streets, street pedestrians, Route 01 bus, passenger rides, inventory, needs,
  pause menu or supermarket. Verified every asserted key binding, dimension and
  constant against the runtime source rather than the planning documents.
- Replaced the `Implemented`-everywhere status column in `ai/systems-map.md`
  with the four-term vocabulary declared in `ai/README.md`, and compressed all
  72 rows from run-on specifications to one or two rendered lines. Five systems
  are honestly `Partial` with their gap named: needs progression (no debuffs),
  the Route 01 bus (City-only), the passenger MVP (no fare or destination),
  scene music (four optional themes absent) and the refrigerator (`Use`
  unavailable). The flow block was reduced to its structural backbone.
- Added a retention rule to `ai/README.md` and `AGENTS.md`, then applied it:
  July 2026 entries moved verbatim into `ai/archive/work-log-2026-07.md` and
  `ai/archive/release-notes-2026-07.md`, with pointers at the head and foot of
  each active file.
- Committed the pending Unity `6000.5.5f1` asset re-serialization separately
  (`chore: normalize Unity asset serialization`). All 23 files were confirmed
  live production assets before committing; the diff was trailing whitespace on
  empty YAML scalars only.

Verification:

- Documentation-only change, so the policy check is diff review plus
  `git diff --check`, which reports clean. No Unity test, build or smoke was
  run, and none is warranted.
- Confirmed no information loss before compressing `systems-map.md`: every
  sampled tuning value (`76-86`, `2.75`, `0.30 m`, `88 m`, `92 m`, `10 s`,
  `1440`, `1080`, `4.5 m`, `24 m`, `0.70 s`, `48 m`, `12 x 12`) already appears
  in both `ai/architecture-notes.md` and `ai/project-overview.md`.
- Table integrity checked mechanically: 72 system rows retained, every row has
  exactly four columns, and no status outside the declared four appears.
- Active documentation context dropped from `731 KB` to `552 KB`, with
  `134 KB` retained verbatim under `ai/archive/`.

## 2026-08-12 — Two pedestrian archetypes and bespoke locomotion

- Added the city-wide Chair Carrier (`chair_carrier_v1`): an upright low-poly
  passer-by carrying an inverted cafe chair whose legs cage the head. The
  existing Lampshade Walker now keeps a pronounced C-curve, bent knees and
  withdrawn neck in both idle and its short asymmetric walk, so stopping no
  longer snaps it back to the hero's upright pose.
- Added one animation-only Generic locomotion library with dedicated
  `LampshadeIdle`, `LampshadeWalk`, `ChairCarrierIdle` and `ChairCarrierWalk`
  loops. Both model FBXs remain animation-free, copy the production Player
  Avatar and use the shared `Player3DLit` material; their four palette variants
  remain property-block driven.
- Replaced the single hard-coded runtime presentation with an explicit ordered
  archetype catalog and one pooled instance per design. The spawn seed chooses
  among free presentations, applies archetype-specific movement and cadence,
  and preserves the existing two-walker daytime / one-walker night caps and the
  shared City/Home Balcony lifecycle. Added a `0.15 s` idle/walk blend and
  geometry-based grounding for both boot naming conventions.

Verification:

- The deterministic Blender 5 generator completed with `CITY PEDESTRIAN ART
  BUILD OK`: Lampshade `38` meshes / `1160` triangles, Chair Carrier `35` /
  `1032`, four 31-bone loops, zero root translation, zero loop error and zero
  per-frame sole gap or penetration.
- Unity rebuilt and validated both production prefabs successfully. Focused
  EditMode coverage passed `3/3` in `0.41 s`: two parameterized asset/rig/clip/
  grounding cases plus the catalog spawn, distinct-design pool and speed-range
  case. Complete suites, player build and packaged smoke were intentionally
  omitted in fast mode.

## 2026-08-12 — Route 01 passenger MVP

- Added standard localized E/Enter/gamepad/pointer boarding through both fully
  open passenger doors and exit from fixed window seat `07` on the side
  opposite the driver. The bus
  now holds its service dwell during each visible transfer, admits one
  owner-scoped passenger and refuses recycling or actor release until passenger
  cleanup has completed.
- Added deterministic front/rear exterior entry/exit docks, nearest-door
  selection, a retained door-specific live waypoint and seat `07` binding.
  The exterior clearance now keeps the waiting player capsule outside the bus
  obstacle corridor, preventing a self-created yield before the service dwell;
  each entry/exit root now derives its height from the deterministic physical
  street-surface plan, choosing the raised sidewalk or flat road apron at that
  exact door position. A curb-height difference within the real
  `CharacterController.stepOffset`
  remains a visible, reachable positioned approach instead of hiding the
  prompt.
  Boarding uses `BusBoardEnter`, travel holds `BusRideLoop`, and the exit prompt
  becomes available only after the service ordinal advances to the next or any
  later stop before `BusAlightExit` returns the hero through the selected door
  to a validated grounded roadside pose.
- Extended the shared positioned-interaction controller with a moving pelvis
  target plus an independently requested exit pose. The production 3D hero
  remains visible across the transfer and follows the sprung seat instead of
  using a hidden teleport or renderer fade.
- Added a seat-following seated ride camera whose safe aisle-side default looks
  through the nearest window instead of inward/down. Its horizon stays level in
  world space while suspension pitches/rolls, and direction-vector blending
  avoids transient roll during boarding/alighting. RMB mouse look and the
  gamepad right stick now rotate independent bounded yaw/pitch in place, reuse the
  ordinary modal orbit-input gate and preserve a continuous blend back to the
  chase pose. The gameplay root remains in its original
  hierarchy and late-synchronizes to the actor-local seat after bus movement,
  avoiding forbidden parent/sibling mutations when the bus slot or scene is
  deactivated. Normal exit, cancellation, scene teardown and forced bus cleanup
  restore the player motor, collider, contact shadow and camera while releasing
  both service and passenger ownership.
- Kept the MVP City-only and deliberately limited to one fixed seat. Fare and
  payment, destination selection, NPC passengers, passenger persistence and a
  live map marker remain deferred.

Verification:

- The deterministic Blender player generator/export validator completed with
  `26` Actions, including the new three-second board, looping two-second ride
  and three-second alight clips on the production Generic rig.
- Unity imported the regenerated animation FBX, rebuilt the production Player3D
  prefab with all three bus clips and compiled Runtime, Editor, EditMode and
  PlayMode assemblies without errors.
- Focused PlayMode regression
  `Passenger_BoardsRidesAndExitsAtLaterStop` was extended to exercise ordinary
  `PlayerInteractor` discovery at both exterior doors, nearest rear-door
  selection, same-stop rejection, attached movement without self-yield or
  recycling, later-stop exit through the retained door and exact player/camera
  restoration. The updated focused selection passed `1/1` in `0.56 s`, including
  the real localized clickable `InteractionPromptView` at both doors. Focused
  actor-ownership and moving-pelvis regressions remain in place; complete
  suites, a player build and a packaged smoke check remain intentionally
  omitted in fast mode.
- Production-city regression
  `ProductionCityRoute_AllStopsExposeBothDoorPrompts` passed `1/1` in `1.05 s`.
  It covers the default seed's five stops and both doors, the real localized
  clickable prompt from road height, and a passenger waiting before arrival
  while the real `CityBusDirector` resolves obstacles and still reaches its
  open-door dwell.
- Focused physical-ground regression
  `ProductionCityDoorDocks_MatchPhysicalSurfaceHeight` passed `1/1` in `1.50 s`.
  It compared all five stops, both doors and both entry/exit poses against the
  real generated colliders: nine door points use sidewalk top `0.14`, while
  Home/front correctly uses apron top `0.08` and grounded root `Y=0.12`.
  The strengthened production prompt regression then passed `1/1` in `1.08 s`,
  including a real click at that Home/front dock and the next-frame transition
  from `Positioning` to `Entering`.
- The focused ride regression was extended with stable actor-local following
  while Player retains its original parent, followed by bus-slot deactivation
  during `Riding`. It passed `1/1` in `0.78 s`, restoring passenger/service
  ownership, motor, collider, shadow and camera without either Unity hierarchy
  error.
- The same regression now also requires a level default view through the
  nearest window and feeds real queued RMB mouse delta plus gamepad right-stick
  input through the passenger camera. Runtime and PlayMode test projects
  compile with `0` warnings and `0` errors.
- Corrected the fixed seat from same-side `Seat_01` to opposite-driver
  `Seat_07`, and strengthened the same regression to prove the side contract,
  a level horizon on every boarding-blend frame and under forced suspension
  pitch/roll, plus unmixed direction-correct X/Y input for both mouse and right
  stick. The focused selection passed `1/1` in `0.97 s`.

## 2026-08-11 — Route 01 production driver

- Added the separate passive `CityBusDriver3D`: a normal low-poly head with
  long horizontal eyes, the shared `Player3DLit` material and the exact 31-bone
  rig used as a procedural presentation target.
- Added seated IK that keeps both hands on the rotating steering-wheel grips.
  The deterministic door timeline moves the right hand to the dashboard button
  for each open/close command, drives its real `12 mm` travel while the left
  hand stays planted, and now holds the real head turn for the complete open
  phase before returning during closing.
- Added deterministic blinking and proximity focus on the main player's real
  head at the outside of the front entrance. The connected neck/head segment
  stretches up to `0.10 m` with a `1.35x` limit and restores its exact local
  scale when focus ends or the bus returns to its pool.
- Preserved the fixed `10 s` stop dwell and `0.70 s` opening/closing transitions.
  Wheel, button, hands, head/look and timeline state now reset with the bus pool.

Verification:

- The deterministic Blender driver generator/export validator completed, and
  focused `CityBusDriverAssetContractTests` verification passed `1/1`.
- The rebuilt production bus prefab passed `CityBusAssetSetup.RunBatch`; focused
  `DriverPresentation_TracksWheelPressesButtonAndLooksAtDoor` verification then
  passed `1/1`, covering wheel/grip contact, both button presses, the actual
  face-bone direction throughout the open hold, player focus/stretch, blinking
  and pool reset. Complete Unity suites, a player build and a packaged smoke
  check were intentionally omitted in fast mode.

## 2026-08-11 — Bus headlights and soft cabin light

- Added two warm, shadowless runtime headlight Spots that follow the sprung bus
  body and illuminate the road ahead, plus two short wide downward Spots for a
  soft readable cabin wash. The production art prefab remains `Light`-free.
- Scaled all four sources with the existing shared `NightFactor`, preserving
  the current dawn/dusk blend, and disabled them completely during daytime,
  presentation disable and pool reset. Existing head/tail/cabin emission and
  brake-light behavior remain unchanged.
- Kept the city-atmosphere pool capped at 12 shadowless lights; the sole active
  bus may add only its four owned Spots, bounding the exterior total at 16.

Verification:

- Focused Unity EditMode
  `PresentationNightLights_AreSprungScaledAndPoolSafe` passed `1/1`, covering
  light count, sprung hierarchy, direction, `0 / 0.5 / 1` night scaling and
  exact pooled shutdown. Full EditMode/PlayMode suites, player build and smoke
  were intentionally omitted in fast mode.
- Scoped `git diff --check` passed. The full dirty-worktree check still reports
  the pre-existing Unity serializer whitespace churn in prefab, material and
  FBX meta files, which this change preserves.

## 2026-08-11 — Bus suspension corrected to runtime axes

- Fixed the production FBX basis turning the intended vertical suspension
  heave into a visible forward/backward slide. The sprung pivot now captures
  its neutral pose relative to the bus presentation and applies heave along the
  runtime bus vertical, with pitch and roll composed around the runtime right
  and forward axes before the imported neutral rotation.
- Added a production-prefab regression that requires non-zero heave to project
  only onto bus height, rejects longitudinal or lateral drift, verifies the
  pitch/roll rotation basis and checks exact pooled reset. The actor transform,
  collider, wheel contacts and ten-second dwell are unchanged.

Verification:

- `dotnet build BarPromenade.EditModeTests.csproj -nologo` succeeded with zero
  errors and the existing `32` JSON-manifest `CS0649` warnings.
- The exact Unity EditMode production-prefab regression
  `SuspensionPresentation_UsesBusVerticalAndBodyAxes` passed `1/1`; scoped
  `git diff --check` passed for the bug-fix files.

## 2026-08-11 — City bus rides on cartoon suspension

- Added a presentation-only `Suspension Visual` pivot around the bus body.
  The four wheel assemblies remain grounded outside that pivot while the body,
  doors, cabin and lights receive a bounded distance-driven heave, acceleration
  and braking pitch, and steering roll. The route transform, kinematic body,
  collider, planner bounds and recycling distances remain unchanged. Door
  hinges preserve their production neutral axis and follow the sprung body
  vertical while it is pitched or rolled.
- Capped the authored ride at `0.045 m` heave, `0.8°` pitch and `1°` roll,
  eased it back to neutral at rest and restored the exact neutral hierarchy,
  articulation and procedural phase whenever the model returns to its pool.
- Replaced the seeded `3-5 s` stop range with one fixed `10 s` total dwell.
  The existing `0.70 s` door opening and closing transitions remain inside
  those ten seconds.
- Added focused regressions for a moving body over grounded wheel contacts,
  unchanged actor/collider state, exact pooled reset and the ten-second dwell
  boundary.

Verification:

- Focused Unity EditMode `CityBusRuntimeTests` passed `15/15`. Fast mode
  passed `15/15`; the one production-prefab door regression passed `1/1`
  after the runtime hierarchy change. Fast mode intentionally omitted the full
  EditMode/PlayMode suites, a player build and a packaged smoke check.
- Scoped `git diff --check` passed for the implementation, tests and
  documentation. The full dirty-worktree check still reports the pre-existing
  Unity serializer whitespace churn in modified prefab, material and FBX meta
  files, which this change deliberately preserves.

## 2026-08-11 — Bus doors fold inward from real hinges

- Rebuilt both production bus doorways as independent double-leaf assemblies.
  Each leaf now owns its panel, glass and moving trim on an outer hinge, while
  the doorway's outer posts remain fixed to the body instead of rotating as one
  wide central slab.
- Updated the runtime registry, prefab builder and presentation to bind all four
  leaves. Opposed world-space rotations use the bus vertical, fold into the
  cabin and restore the exact authored pose before pooling. The deterministic
  Blender source/FBX/manifest and Resources prefab now share generator version
  `1.1.0`, `41` meshes and `3804` triangles.
- Added a production-prefab regression that checks both doorways, vertical
  rotation, equal opposed angles, inward movement, fixed posts and exact reset.

Verification:

- The Blender generator validator completed and a dedicated fully-open review
  render showed two clear, upright doorways with both leaf pairs folded inward.
- Focused Unity EditMode
  `CityBusAssetImportTests.DoorPresentation_UsesOpposedInwardHingedLeaves`
  passed `1/1`. Fast mode intentionally omitted the full EditMode/PlayMode
  suites, a player build and a packaged smoke check.

## 2026-08-11 — Winding Route 01 reaches district places and Home

- Replaced the Central Park ring selection with a deterministic target-derived
  Route 01. The planner orders every actual district point of interest and then
  `PlayerHome`; the default city now owns five semantic stops in Industrial,
  Nightlife, Residential, Old Town and Home order. Each stop chooses a safe
  straight on the target frontage or one connected edge away, keeps its pole on
  another roadside cell and outside the target public/access bounds or Home
  footprint, and carries explicit target kind, ID and cell metadata.
- Connected those target straights through one accepted closed graph. Retained
  links include ordinary straights, proven `6 m` left turns and a selected-apron
  two-edge safe-right macro: a long S-merge over the full incoming Street, a
  `4.5 m` quarter-turn through the clear core and a symmetric S-return over the
  outgoing Street. The macro marks both physical edges occupied so it cannot
  bypass a stop edge. Ordinary unselected `3 m` rights remain rejected.
- Expanded Road v2.1 eligibility to safe perpendicular two-way corners as well
  as three- and four-way nodes. Signal intersections remain eligible because
  excluding them disconnects the production target graph; every retained
  maneuver now proves its inflated body against both actual signal poles at a
  conservative `0.30 m` radius, rejecting collisions as
  `StaticFixtureOverlap`. The physical apron remains `4.5 m` long.
- Gave every ordered route occurrence unique link/node IDs even when a physical
  section repeats. Nightlife's Last Route Island now has a working Route 01 pole
  nearby but outside the POI, while its abandoned island composition remains
  distinct. The City map consumes the five default stop descriptors without a
  live bus marker.
- Reused the stop visual builder in Home: the bounded exterior selects the
  `PlayerHome` target and reconstructs its blue `01` pole in local space without
  colliders. Home still creates no bus actor or director. Added the Home stop
  localization and focused planner/Home composition coverage.

Verification:

- The focused Unity EditMode `CityBusPlannerTests` fixture passed `6/6`, covering
  deterministic non-empty generation, the closed winding loop, accepted
  straight/left/wide-right clearance including real signal fixtures, semantic
  POI/Home stops and stop-edge ownership.
- The focused Home exterior integration regression passed `1/1`, proving the
  nearby `PlayerHome` pole is reconstructed in local space without colliders,
  a bus actor or a bus director.
- Scoped documentation review and the full-worktree `git diff --check` passed
  after serializer-only import churn was removed. Fast mode intentionally
  omitted the full EditMode/PlayMode suites, a player build and a rendered
  walkthrough.

## 2026-08-11 — Repository artifact cleanup

- Removed the unused stock URP tutorial scaffold (`Assets/Readme.asset` and
  `Assets/TutorialInfo`). Its GUIDs and editor types had no references outside
  the scaffold, and it was unrelated to the runtime-composed project.
- Removed three superseded Stairwell albedos and their metas from `Resources`:
  wall paint, corroded metal and door paint. Runtime, tests and documentation
  use only their active `V2` replacements, so the old versions unnecessarily
  increased repository and packaged Resource size by about `6.8 MB`.
- Cleared ignored, reproducible local output: two old player builds, 829 test
  result files, Python bytecode and five stale diagnostic logs. Active Unity
  caches, IDE project files and user settings remain untouched. Total local
  space reclaimed was about `550 MB`.

Verification:

- Asset GUID/path audit found no external references to the deleted tracked
  files; all remaining Unity assets retain matching metas and unique GUIDs.
- `BarPromenade.Runtime.csproj` built with `0` warnings and `0` errors, and the
  scoped staged diff check passed. A focused Unity runner exited before test
  discovery and produced no result XML, so no Unity test result is claimed.

## 2026-08-11 — Road v2.1 three-way pedestrian junction fix

- Fixed the Home-loading exception introduced when Road v2.1 began accepting
  safe three-way bus aprons. The pedestrian graph and its physical closed-side
  sidewalk now share the displaced `4.5 m` corner coordinate, so every link
  remains axis-aligned instead of connecting a new corner to the old `3.5 m`
  mouth.
- The closed side is a continuous `1 x 8 m` raised strip outside the clear
  `8 x 8 m` bus core. It meets both corner pads, retains the exact `1 m`
  pedestrian corridor and does not occupy any real bus approach.

Verification:

- Focused Unity EditMode regressions for the production Home pedestrian graph
  and the physical three-way sidewalk mouth passed `2/2`; the shared Road v2
  apron and raised-sidewalk contracts also passed `2/2`. Fast mode intentionally
  omitted the full EditMode/PlayMode suites and a player build.

## 2026-08-11 — Canonical Route 01, physical stops and map overlay

- Replaced the retained branching bus graph with the immutable
  `bus-route:default-coastal:ring-01:ccw`: one right-hand counter-clockwise
  Street ring around Central Park. Every link now has one ordered successor and
  every lap repeats Industrial, Nightlife, Residential and Old Town without
  route RNG or player pursuit. Sampled full-body clearance still admits the
  proven straight and `6 m` left-turn geometry and rejects unsafe tight turns.
- Added four semantic route-owned stops on safe straights in that district
  order, including stable IDs, localization keys, lap distances and roadside
  poses. `CityBusStopWorldBuilder` gives each one a physical blue Route `01`
  pole; the random roadside decoration selector no longer emits bus shelters.
  The actor serves every stop once per lap with its existing seeded `3-5 s`
  two-door dwell, then resets service state at the loop seam.
- The canonical ring deliberately traverses the frontage street beside
  Nightlife's Last Route Island, superseding the earlier edge exclusion, while
  stop placement still excludes that frontage. The island therefore remains a
  non-working abandoned stop rather than becoming Route 01 infrastructure.
- Reworked one-slot activation around the fixed loop. Dynamic obstacle-safe
  poses prefer the fog-hidden `76-86 m` band and fall back to `56-86 m` only
  when forward loop distance reaches a player-side encounter sample; a loop
  with no forward encounter sample is rejected. Recycling still waits for `92 m`
  complete-body clearance, and camera/frustum state remains irrelevant.
- Added an immutable simplified bus-map overlay. The City map draws the blue
  ink-outlined loop below the orange player itinerary, four numbered localized
  hover stops and a compact route/stop legend; a live bus marker and boarding
  remain deferred.
- Expanded Road v2.1 apron selection from safe four-way nodes to safe three- or
  four-way nodes, while retaining full-core, real-approach and pedestrian
  clearance checks. Added focused planner, runtime, map-overlay, localization
  and scene-composition coverage for the new contracts.

Verification:

- The focused Unity EditMode selection covered the planner, fixed-loop
  runtime, map overlay, random-decoration exclusion and RU/EN catalogs:
  `25/26` passed initially. Its only failure exposed an over-permissive
  synthetic road-edge fixture, not production behavior; after narrowing that
  fixture to its intended spawn segment, the exact failed regression passed
  `1/1` and the complete focused `CityBusRuntimeTests` fixture passed `13/13`.
- Scoped source/documentation diff review and `git diff --check` passed. Fast
  mode intentionally omitted full EditMode/PlayMode suites, a player build and
  a rendered walkthrough.

## 2026-08-11 — Ambient city bus and Road v2.1 junctions

- Added the accepted production design vehicle at its real
  `8.25 x 2.38 x 2.95 m` dimensions and `4.5 m` wheelbase. The generated FBX,
  manifest and Resources prefab contain the exterior shell plus a visible
  driver area, dashboard, twelve passenger seats, rails, two articulated doors,
  four wheels, steering pivots and registered head/tail/cabin light renderers;
  runtime never shrinks the model to make the road fit.
- Extended the shared street surface to Road v2.1. A stable selector reserves
  eligible Street-only four-way nodes outside the zebra/signal set, moves their
  four `1 x 1 m` corner sidewalk pads onto clear adjacent ground, exposes a
  full `8 x 8 m` asphalt core and cuts each raised approach curb back by
  `4.5 m`. The resulting flush shared apron preserves the pedestrian line while
  clearing the bus's rear-body sweep. Home retains the same geometry in its
  bounded reconstruction.
- Added a deterministic right-hand, Street-only bus graph with sampled
  long-body clearance. It admits straight links and analytic `6 m`-radius left
  turns through Road v2.1 aprons, rejects the tighter `3 m` right-turn
  candidates and retains a cyclic strongly connected route. Compatible
  roadside bus shelters map to stops first; when the strict retained route has
  none, it receives exactly one deterministic route-native stop on a safe
  retained straight. That fallback owns `CityBusStopOrigin.RouteNative` and an
  empty `SourceDecorationId`, never a fabricated shelter identity. The
  Nightlife last-route-island frontage is intentionally outside the drivable
  graph. Route, anchor and mapped-shelter stop counts stay derived data rather
  than content constants.
- Added one pooled ambient-bus slot in City. Obstacle-safe spawning prefers the
  fog-hidden `76-86 m` band; initial routing approaches the player and then
  releases into ordinary roam.
  The bus yields to the player and active pedestrians, serves stops with a
  randomized `3-5 s` dwell and two-door animation, and recycles only after the
  closest point of its complete body reaches `92 m`. Camera direction, frustum
  state and far clip never drive this lifecycle. The one-slot cap deliberately
  permits intervals with no active or visible bus.
- Kept the runtime deliberately out of Home. No real Street pass-through in
  the balcony exterior has both complete-body seams at or beyond the hidden
  `56 m` boundary, and the default facade faces a visible road terminal.
  Fabricating another road would contradict the generated city; owning spawn
  or pooling from the Balcony camera would create a visible pop. The existing
  pedestrian exterior runtime remains unchanged.
- Added a kinematic physical body on the dedicated `CityBus` layer, rolling and
  steering wheels, brake/night-sensitive emission and a generated `22050 Hz`
  engine loop. Presentation and audio reset before pooling; the passive prefab
  itself remains collider-free and non-interactive.

Verification:

- Focused Unity EditMode selection passed `13/13`: `CityBusPlannerTests`,
  `CityBusRuntimeTests`, `CityBusAssetImportTests`, the Road v2.1 surface
  regression and the pedestrian-apron regression.
- Focused Unity PlayMode City scene smoke passed `1/1`.
- Fast mode intentionally omits complete EditMode/PlayMode suites, a player
  build and a broad rendered walkthrough.

## 2026-08-11 — Road v2 street cross-section

- Raised the canonical default street footprint from `6 m` to `8 m`. With the
  existing two `1 m` sidewalks, ordinary streets now expose a `6 m`
  carriageway, an `8 x 8 m` junction core and a clear `6 x 6 m` carriageway
  apron. The unchanged `18 m` blocks now produce a `26 m` grid step and a
  `312 m` default 12-block core span.
- Kept the migration data-first: entrances and sidewalk arrivals, pedestrian
  lanes, fences, night fixtures, decoration clearance, map projection and the
  bounded Home reconstruction continue to derive from `RoadWidth` and
  `NodeSpacing`, so no duplicated scene geometry was introduced.
- Replaced the pedestrian production regression's obsolete fixed home
  coordinate with the generated sidewalk arrival, and added focused Road v2
  coverage for the default width, pitch, carriageway, junction apron and
  widened zebra.
- Recorded the scope boundary explicitly: the cross-section is ready for a
  vehicle route plan, but a long bus still requires a swept-turn proof using
  its final body, axle and steering dimensions before bus runtime is added.

Verification:

- Focused Unity EditMode selection passed `4/4`: the Road v2 surface contract,
  default city dimensions, stationary-player pedestrian approach and Home
  exterior pedestrian transform.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City walkthrough.

## 2026-08-11 — Stationary-player pedestrian encounters

- Confirmed from the reported session log that City initialized 210 pedestrian
  anchors without errors and then ran for roughly 104 seconds without a visible
  encounter. The presentation prefab and all 38 renderer bindings remained
  valid; the failure was in the distance lifecycle.
- Kept obstacle-safe `76-86 m` as the preferred hidden spawn band. The reported
  home-return position exposed that both anchors in that ring belonged to
  sidewalk components whose closest point was still `38.5 m` away, while the
  player-linked component had anchors only at roughly `34-49 m`. Added a
  dense-fog `32-86 m` connected fallback for that topology.
- Added a one-shot approach phase: until a walker first reaches `24 m`, eligible
  non-backtracking turns follow shortest physical graph distance to the nearest
  player-side node in their own connected component. Once reached,
  that slot permanently returns to seeded random roaming for the rest of the
  spawn, while its independent zebra decision remains intact.
- Extended hidden daytime acceleration down to `32 m`, still inside dense fog,
  so the guaranteed approach does not spend most of its time beyond the `48 m`
  camera. Night keeps its authored movement speed and sparse timing.
- Added focused coverage for a branch whose seeded ordinary choice points away,
  guided zebra decisions, the bounded stationary-player approach, the exact
  default seed/home-return graph and the no-reacquisition contract.

Verification:

- `dotnet build BarPromenade.EditModeTests.csproj -nologo` passed with zero
  errors and 15 existing `CS0649` manifest-field warnings.
- Focused Unity EditMode `CityPedestrianRuntimeTests` passed `13/13`, including
  the exact `20260727` home-return stationary-player regression.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City walkthrough.

## 2026-08-11 — Restored readable stairwell textures

- Corrected the first stairwell texture pass after live inspection showed that
  URP/Lit multiplied the new maps by palette colors authored for a white map,
  removing another `56-74%` of surface light and crushing texture variation.
- Added per-recipe linear-albedo compensation (`2.17x-3.98x`) to map the
  original semantic color to a display tint whose textured mean matches the
  former flat-color brightness. Lighting, post exposure, hero/cat presentation
  and emissive fixtures remain unchanged.
- Added higher-macro-contrast ImageGen V2 wall, door and corroded-metal maps;
  the original lower-contrast sources remain beside them. The active eight-map
  set now enforces opaque RGB storage, Repeat-safe edges, at least `24/255`
  sampled `p95-p05` contrast and a compensated linear mean within `0.08` of
  the original brightness.

Verification:

- `dotnet build BarPromenade.EditModeTests.csproj -nologo` passed with zero
  errors and 15 existing `CS0649` manifest-field warnings.
- A direct validator passed all eight active sources for opacity, contrast,
  Repeat-edge delta and compensated mean brightness.
- Focused Unity EditMode `StairwellSurfaceAppearanceTests` passed `20/20`,
  including active-map imports, compensated brightness, projection-aware
  tiling and enabled-renderer coverage.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered Stairwell walkthrough.

## 2026-08-11 — Textured stairwell surfaces

- Added eight opaque RGB ImageGen albedos under
  `Resources/Stairwell/Textures`: wall paint, ordinary concrete, worn stair
  concrete, corroded metal, door paint, damp/damage, dirty wood and mixed
  debris. Unity imports each at runtime as `512x512` sRGB with Repeat, Bilinear
  filtering, mipmaps, anisotropy `4`, no compression and no readable CPU copy.
- Added `StairwellSurfaceAppearance` as the single recipe/cache boundary. It
  retains native primitive UVs, maps visible box planes and cylinder
  circumference/length explicitly, derives deterministic physical scale and
  stable hierarchy-based offsets, and writes `_BaseMap`, `_BaseMap_ST`,
  smoothness and metallic through material property blocks while preserving
  the existing `_BaseColor`/`_Color` tint and shared `RuntimePrimitiveLit`
  material.
- Routed every enabled ordinary renderer from `StairwellWorldBuilder` and
  `StairwellDressingBuilder` through the new surface wrappers: walls and dirty
  bands; ground, ceiling and columns; steps and landings; rails, grilles, doors
  and frames; pipes, vents, cabinets and radiator; damage, litter and upper
  debris; and all non-emissive fluorescent hardware.
- Left hidden walkable ramps and the upper safety blocker untextured, and kept
  emissive tubes, halos, the production hero, cat and dust/VFX on their existing
  specialized presentation paths. Geometry, colliders, cameras, lighting and
  stairwell traversal did not change.

Verification:

- Focused Unity EditMode `StairwellSurfaceAppearanceTests` passed `20/20`,
  including tall-cylinder and first/last-step texel-density regressions.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a manual rendered Stairwell walkthrough.

## 2026-08-11 — Daytime pedestrian encounter cadence

- Confirmed that the fresh `06:00` start already selects daytime pedestrian
  rules; the strict night boundary remains `<06:00` / `>=19:00`.
- Fixed two distant actors monopolizing the complete daytime pool outside the
  `48 m` City view. Actor simulation now accelerates smoothly from `1x` at
  `56 m` to at most `2.75x` from `76 m`, so an inward route reaches the player
  sooner and an outward route crosses the existing `88 m` recycle boundary
  sooner. Spawn anchors, two-slot cap, randomized delays and camera-independent
  lifecycle remain unchanged.
- Kept night actors at authored pace in addition to their existing one-slot cap
  and longer delays.
- Added a focused straight-approach regression that bounds the hidden daytime
  transit and verifies ordinary near-range and night movement speeds.

Verification:

- Focused Unity EditMode
  `CityPedestrianRuntimeTests.Factory_DaytimeFastForwardsOnlyFogDistantWalkers`
  passed `1/1`; Unity compiled the affected Runtime and EditMode assemblies.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City/Home walkthrough.

## 2026-08-10 — Textured ground between city buildings

- Added one opaque generated compacted-soil albedo at
  `Resources/Textures/CityGroundSoilAlbedo`. Unity imports it at runtime as
  `512x512` sRGB with Repeat, Bilinear filtering, mipmaps, anisotropy `4`, no
  compression and no readable CPU copy.
- Applied the soil through `12 m` world-aligned XZ UVs and a material property
  block on the shared `RuntimePrimitiveLit`. The City keeps the existing
  collider-backed `Active Land` combined mesh, while the clipped Home exterior
  reconstruction uses the same visual recipe without adding a collider.
- Left beach, lake-shore, cemetery, water, park lawn and street treatments
  unchanged, and expanded the parameterized exterior-surface contract to cover
  the new resource, import, seam, UV, shared-material and MPB settings.

Verification:

- Focused Unity EditMode `RuntimePrimitiveFactoryTests` passed `9/9`; Unity
  compiled the affected Runtime and EditMode assemblies without errors.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City/Home walkthrough.

## 2026-08-10 — Scrollable city-map line clipping

- Fixed the scrollable full-screen City map leaking and scattering roads, park
  paths and short landmark strokes across the title and surrounding panel.
- Composed the rotated line transform around the active map-group origin under
  the retro canvas matrix, then clipped each visible segment to the local
  viewport while accounting for its direction and thickness. Route-panel
  legend lines remain outside that map-only clipping context.
- Extended the existing line-rendering coverage with the nested scaled-group
  transform and horizontal, vertical, diagonal, fully external and already
  visible clipping cases.

Verification:

- Focused Unity EditMode map-line selection passed `2/2`; Unity compiled the
  affected Runtime and EditMode assemblies without errors.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and a rendered City walkthrough.

## 2026-08-10 — Player-relative pedestrian lifecycle

- Removed the Main Camera from `CityPedestrianDirector` and factory inputs.
  Spawn selection, active lifetime and pooling no longer read camera direction,
  frustum membership or far-clip settings.
- Moved unique obstacle-safe spawns into the `76-86 m` player-relative band.
  At its inner edge the fixed `0.070` Exp2 City fog retains less than `0.2%`
  scene transmittance even at the widest production `70-degree` 16:9 frustum
  corner after a conservative combined `6 m` camera and full visual-envelope
  depth offset; actors remain active through camera turns and return to the
  pool only after moving beyond `88 m` from the hero.
- Replaced the immediate deterministic fill with a director-local runtime
  random stream for candidate rank, motion/palette variation and timing. The
  first one-slot event waits `1.25-7.5 s`, each later slot or replacement gets
  a separate `3.5-12.5 s` delay, and failed searches retry after `0.8-2.4 s`.
- Added a strict `<06:00` / `>=19:00` spawn mode with one fresh-population slot,
  `15-35 s` initial delays, `30-70 s` replacement delays and `4-10 s` retries.
  Entering night does not cull either of two walkers already active at dusk.
- Kept Home's Balcony-only enable/disable as a scene-composition boundary while
  applying the same distance lifecycle whenever its local street runtime is
  active. Its transformed graph now retains a bounded `100 m` approach-anchor
  context beyond the facade while the rendered street slice remains `48 m`.
  Replaced the old seen/left-view assertions with player-distance,
  camera-independence and staggered-scheduling coverage.

Verification:

- Focused Unity EditMode selection covering staggered/random and night spawn
  schedules, strict time boundaries, camera-independent distance recycling,
  stable head-on yielding and the expanded Home anchor context passed `11/11`;
  Unity also compiled the affected Runtime, EditMode and PlayMode assemblies.
- Fast mode intentionally omitted complete EditMode/PlayMode suites, a player
  build and scene smoke.

## 2026-08-10 — Balcony street pedestrians

- Added a Home-local projection of the seeded City pedestrian graph. Nodes and
  navigation rectangles use the existing City-to-Home facade transform, while
  spawn anchors are retained only on the bounded nearby-road set and only when
  the complete pedestrian radius lies beyond the apartment facade.
- Composed the existing two-slot pedestrian factory under `HomeInteriorRoot`
  with the real Main Camera and the player as its locality focus. The balcony
  atmosphere now enables the director only for the Balcony shot and disables
  it before restoring indoor visibility, immediately releasing presentations
  and `CharacterController`s on exit, disable or destruction.
- Moved pedestrian visibility sampling after all Home contextual camera owners
  and made player collision/yield checks require vertical capsule overlap, so
  the player four storeys above does not block street-level spawn or movement.
- Added a pure graph-transform/filter regression and a focused Home PlayMode
  lifecycle covering dormant MainRoom slots, unique off-frustum Balcony spawn
  and complete recycling after returning indoors.

Verification:

- Focused Unity EditMode test
  `ExteriorPedestrians_TransformCityGraphAndFilterSpawnAnchors` passed `1/1`.
- Focused Unity PlayMode test
  `HomeScene_SpawnsPedestriansOnlyOnBalcony` passed `1/1`.
- The older broad Home balcony presentation test was also attempted but stops
  before the new pedestrian assertions at its pre-existing collider-free
  exterior assertion: current street-lamp chunks contain `BoxCollider`s. That
  unrelated contract was left unchanged. Fast mode omitted complete suites, a
  player build and scene smoke.

## 2026-08-10 — Local camera-aware street pedestrians

- Replaced the 12 always-simulated two-point routes and six-model distance
  pool with one deterministic sidewalk graph and exactly two reusable runtime
  slots. The graph joins street lanes through radius-safe corner turns, prunes
  all reachable dead ends to its 2-core and consumes explicit zebra descriptors
  as three-link curb/carriageway connectors.
- Walkers now spawn only at unique, obstacle-clear anchors inside the player's
  local far-clip window and fully outside a conservative camera-frustum bound.
  An offscreen approach remains alive until first seen; after that, leaving the
  frame releases its controller and presentation after a short grace. An
  unseen timeout reclaims paths that never enter the shot.
- Reworked actors as resettable slots that continue forward through graph
  turns without endpoint reversals. At each zebra entry they make one seeded
  50% cross/don't-cross choice and automatically complete a chosen crossing.
  Ordinary despawn disables the `CharacterController` before returning the
  still-live PlayableGraph presentation to its pool.
- Added focused planner/runtime coverage for deterministic topology,
  radius-safe links, dead-end removal, narrow-road zebra rejection, turns,
  both zebra decisions, unique max-two offscreen spawning, static obstruction,
  slot yielding and the seen-to-exit lifecycle. Updated the scene smoke
  assertion for the valid initial population range of zero through two.

Verification:

- Focused Unity EditMode selection
  `CityPedestrianPlannerTests;CityPedestrianRuntimeTests` passed `15/15`.
  Unity compiled the affected runtime and EditMode test code in that run.
- Fast mode intentionally omitted the complete EditMode/PlayMode suites, a
  player build and scene smoke.

## 2026-08-10 — Upright pedestrian endpoint steering

- Fixed a latent 3D-facing error exposed by the raised sidewalks. Near a route
  endpoint, a small `CharacterController` height correction could dominate the
  remaining horizontal distance; feeding that vector to `LookRotation` pitched
  the complete actor and pooled visual close to horizontal throughout the
  endpoint pause.
- Pedestrian route distance, facing, final placement and endpoint completion
  now operate strictly in XZ and preserve the controller's current Y. The
  existing turn phase already used planar travel direction and remains
  unchanged.
- Added a focused regression that injects a vertical mismatch beside the final
  waypoint and verifies an upright root through endpoint pause, turning and
  reversed walking.

Verification:

- `dotnet build BarPromenade.EditModeTests.csproj -nologo` passed with zero
  errors; the 15 `CS0649` manifest DTO warnings are pre-existing.
- Focused Unity EditMode regression
  `Actor_VerticalContactCorrectionAtEndpointKeepsRootUpright` passed `1/1`
  after the user closed the Editor. Fast mode omitted broader suites, a player
  build and scene smoke.

## 2026-08-10 — Asphalt carriageways, sidewalks and zebra crossings

- Added a pure `CityStreetSurfacePlan` that keeps the canonical road footprint
  but partitions ordinary `6 m` streets into a dark `4 m` carriageway and two
  raised `1 m` sidewalks. It also plans intersection pavement, white center
  dashes, four-stripe zebra approaches, sidewalk/crosswalk walkable rectangles
  and explicit ParkPath surfaces before GameObjects exist. Generation now
  rejects widths that cannot leave a positive carriageway.
- Extracted the deterministic, public-space-safe degree-3+ intersection
  selector from the night fixture planner. Traffic signals and zebra crossings
  now share the same ordered set of at most six nodes, and center dashes are
  omitted from all intersection and crosswalk bounds.
- Reassigned the previous light road albedo to
  `Resources/Textures/CitySidewalkAlbedo` while preserving its Unity GUID, and
  added generated dark-asphalt and worn-white-paint albedos. All three use
  Repeat XZ UV recipes and material property blocks on the shared
  `RuntimePrimitiveLit`; no material instances were added.
- Updated the chunked City builder with physical sidewalk meshes and
  collider-free markings. Entrance aprons now terminate at the near sidewalk,
  match its `0.08-0.14 m` curb bounds and return the player to its center rather
  than the road axis. The bounded Home exterior consumes the same surface plan
  in local space without collision.
- Shifted the 12 deterministic ambient routes to sidewalk centers and replaced
  their separate street mask with the plan's sidewalk-only rectangles. Current
  walkers still use single-edge routes and stop before intersections; the
  crosswalk rectangles are available for a later multi-edge connector phase.

Verification:

- Focused Unity EditMode selection passed `28/28`: street-surface geometry,
  shared signal/crosswalk selection, texture import/seams/MPBs, sidewalk NPC
  containment, and deterministic frontage sidewalk arrivals.
- Unity compiled Runtime, Editor, EditMode and PlayMode assemblies during that
  invocation. Fast mode intentionally omitted complete suites, a player build
  and scene smoke.
- Scoped changed-source/document whitespace review is clean. Repository-wide
  `git diff --check` remains noisy only in the unrelated pre-existing
  `CityPedestrian3D` prefab/FBX-meta edits.

## 2026-08-10 — Physical city obstacles and open ground traversal

- Expanded the player's indexed macro walkable area from streets and explicit
  approaches to complete logical `BuildableGround` plus existing `OpenLand`.
  Overlapping road-to-ground and adjacent-ground connectors preserve
  continuity for the maximum `0.35 m` agent radius; water, unmapped cells and
  outside space remain excluded. Buildings and props now rely on their actual
  colliders instead of invisible road-only limits.
- Reclassified the 24 city-decoration families through deterministic
  `None`/`Detail`/`Blocking` tiers. Grounded structural and bulky recipes build
  one to four simple chunk-owned box proxies; rooftop, hanging and small
  narrative details stay non-physical. Added focused collision for park
  benches and hedges, the home mailbox, and lower lamp/signal poles while the
  Home exterior reconstruction remains presentation-only.
- Replaced continuous road-edge fencing with physical rails only at water,
  unmapped and active-map boundaries plus full-width true Street dead ends.
  Terminal degree includes ParkPath edges, so streets entering the park remain
  open. Existing entrance/gate/public/open-area descriptors remain available
  as decoration-clearance metadata; narrow posts remain visual-only.
- Added a dedicated `CityPedestrian` layer and presentation-gated
  `CharacterController` to pooled walkers. The controller activates only after
  an overlap-safe bind and disables before pooling; pedestrians collide with
  the player, ignore one another, are excluded from camera/interaction queries
  and retain a separate street-only navigation mask with stable head-on yield.

Verification:

- Added focused EditMode contracts for collision tiers/proxies, pedestrian
  layer and pooling lifecycle, boundary/dead-end fence classification,
  physical rails and radius-safe ground continuity.
- Passed the focused PlayMode test
  `SceneFlowSmokeTests.CityScene_GroundTraversalUsesPhysicalBoundaries`
  (`1/1`): the real player capsule crossed from a street into a clear yard,
  stopped against building mass, and the scene exposed the intended fence,
  park, mailbox, fixture, decoration and visible-pedestrian colliders.
- The initial targeted EditMode command completed script compilation but quit
  during its first asset refresh before emitting test results. Per fast-mode
  scope, no full suite, player build or additional smoke was run.

## 2026-08-10 — Textured city asphalt

- Added one opaque generated asphalt albedo at
  `Resources/Textures/CityRoadAsphaltAlbedo`. Unity imports it at runtime
  `512x512` with sRGB, Repeat, Bilinear filtering, mipmaps, anisotropy `4`,
  no compression and no readable CPU copy.
- Extended `RuntimePrimitiveFactory` with opt-in XZ planar UVs and applied a
  stable `12 m` tile size only to the City street batches and their
  collider-free Home exterior reconstruction.
- Kept the one shared `RuntimePrimitiveLit` material. Road renderers receive
  the albedo, white tint, `0.10` smoothness and zero metallic through their
  existing material property blocks, without per-surface material instances.
  Park paths, road dashes and City collider mesh ownership remain unchanged.
- Expanded focused `RuntimePrimitiveFactoryTests` coverage for the packaged
  asset/importer, opaque PNG and Repeat-edge seam threshold, road MPB and
  shared material, XZ UV density and unchanged shared collider mesh.

Verification:

- Focused Unity EditMode
  `BarPromenade.Tests.EditMode.RuntimePrimitiveFactoryTests` passed `6/6`
  tests in Unity `6000.5.5f1`.
- Documentation diff review and `git diff --check` passed.
- Fast mode intentionally omitted complete Unity suites, a player build and
  startup smoke.

## 2026-08-10 — Ambient city street pedestrians

- Added 12 deterministic short pedestrian routes to `CityGameRoot`, biased
  toward bar/home/supermarket frontages, district public places, open-area
  accesses and park gates. Endpoints remain outside intersections using the
  actual road width plus actor radius; every virtual actor continuously walks,
  pauses and turns while staying inside the street mask.
- Added a bounded pool of six visible presentations with outer-fog activation,
  camera-relative hysteresis and lightweight yielding near the player or
  another presented walker. The actors own no colliders, rigidbodies,
  interactions, prompts or persistent gameplay state, and scaled zero delta
  freezes route and animation progress.
- Authored the first resident, the `1.75 m` Lampshade Walker: a long dark-green
  coat, recessed face with one amber mark, rigid parcel bag, mismatched boots
  and a trapezoid hood. Its deterministic Blender source produces 38 rigidly
  skinned parts at 1,160 triangles on the exact 31-bone Player hierarchy, with
  no Actions, colliders, lights or emissive parts.
- Imported that model through the production Player Generic Avatar, one shared
  instanced `Player3DLit` material and four muted MPB palettes. Each pooled
  presentation directly references the Player animation FBX's looping `Idle`
  and `Walk`, keeps root motion off and grounds the animated boot-sole geometry
  while route motion remains code-owned. Explicit teardown now destroys every
  manual PlayableGraph in scene, test and failed-factory lifecycles; mutual
  builder guards prevent the pedestrian and Player importers from requeuing
  each other indefinitely.

Verification:

- Blender 5.0.1 deterministic build/validator passed: 31 matching bones,
  38 meshes, 1,160/1,200 triangles, grounded `1.75 m` bounds and zero Actions;
  generated signature
  `0e29c300259a698cba443f2d2ae9f37f9ac30c18478edf966f68d19b20a90b5d`.
- Unity importer/prefab validator passed in batch mode with the external Player
  Avatar, shared material and direct `Idle`/`Walk` references.
- Focused EditMode pedestrian selection passed 9/9 in 0.89 seconds, including
  plan stability/safety/function bias, active-pool cap/hysteresis, pause/turn,
  passive prefab contracts and 12 sampled Walk sole-contact phases. The final
  run exited without leaked PlayableGraphs.
- Fast mode intentionally omitted complete Unity suites, a player build,
  startup smoke and a rendered City walkthrough.

## 2026-08-10 — Clock-driven hunger and fatigue

- Connected hunger and fatigue to the one persistent scaled session clock.
  After the startup Wake, hunger fills from `0` to `100` over `1440` game
  minutes and fatigue over `1080`; progression freezes with the clock before
  Wake and at `timeScale = 0`, but otherwise survives and continues through
  ordinary interactions, transitions and scene loads.
- Added a pure double-precision fractional progression state, keeping large and
  small time steps deterministic and discarding overflow at the `100` cap.
  Public session values and the existing four-bar inventory Status card remain
  clamped integers; no hunger or fatigue debuff is applied yet.
- Made value-setting transactions clear their corresponding hidden fraction:
  committed food clears the hunger remainder, a normally completed bed wake
  clears fatigue and its remainder, and a new game clears both. Cancelled sleep
  preserves the accumulated fatigue instead of treating the rest as completed.
- Kept diagnostics boundary-based by recording passive need changes only when
  a visible integer level changes instead of logging each frame.

Verification:

- Focused EditMode progression and session-state selection: 12/12 passed in
  0.29 seconds.
- Focused PlayMode
  `InventoryPlayModeTests.Open_ShowsCurrentGameTimeAndFreezesIt`: 1/1 passed in
  1.12 seconds.
- Fast mode intentionally omitted complete Unity suites, a player build and
  startup smoke.

## 2026-08-10 — Session fatigue and completed bed rest

- Added session-owned fatigue as a clamped integer `0-100` value where higher
  is worse. New games start at zero, ordinary scene loads preserve it, manual
  diagnostics record it and a dedicated mutation boundary is ready for a
  future accumulation system; no runtime source raises it yet.
- Expanded the inventory Status card to four compact bars and added localized
  `УСТАЛОСТЬ` / `FATIGUE` captions without moving cash or session time outside
  the existing `150 x 172` panel.
- Added an explicit normal-completion event to the shared animated-interaction
  controller. `HomeBedInteraction` resets fatigue only after the terminal
  `BedExit`; an accepted wake that is then cancelled by transition, disable or
  lifecycle cleanup preserves the prior value.
- Extended session, localization, diagnostic and real Home-bed regression
  coverage for defaults, clamping, successful rest and cancellation atomicity.

Verification:

- Focused PlayMode
  `HomeBedInteractionPlayModeTests.Bed_FatigueResetsOnlyAfterCompletedWake`:
  1/1 passed in 2.01 seconds.
- Focused EditMode selection for fatigue state, diagnostics and localization:
  8/8 passed in 0.42 seconds.
- Fast mode intentionally omitted complete Unity suites, a player build and
  startup smoke.

## 2026-08-10 — Bounded hybrid ragdoll for drunken falls

- Added a runtime-composed 13-body ragdoll over the production Generic rig.
  `PlayerFactory` builds kinematic rigidbodies, owned colliders and constrained
  joints from serialized anatomical bindings, so rebuilding `Player3D.prefab`
  cannot erase the setup and no alternate hero is introduced.
- A failed balance check now plays `0.16 s` of the directional Fall action,
  suspends manual PlayableGraph/late-pose writes and transfers the current bones
  to physics for the rest of Falling plus Down. Owned colliders ignore each
  other and the upright `CharacterController`; a `0.68 m` pelvis tether keeps
  the physical pose near the fixed gameplay root.
- Recovery freezes physics, disables its colliders and blends the complete bone
  hierarchy for `0.16 s` into the matching Rise start before returning control
  to animation. Re-authored both physical sides as distinct full-body,
  `50`-source-frame (`1.67 s`) Rise actions: exact side-down start, brace and
  prone roll, a held hands-and-knees pose, lead-foot plant, low crouch and an
  exact `Relaxed` endpoint. Every landmark supplies the full body pose, avoiding
  the former bind/A/T-like limbs; the all-fours hold remains inside the existing
  `Rising` state rather than adding a gameplay phase.
- Completion, intoxication cancellation, transition and lifecycle cleanup
  restore the neutral graph-owned rig, kinematic bodies, input and fall-aware
  contact shadow. The fixed gameplay root remains authoritative throughout.
- Extended deterministic Blender validation and the focused failed-balance
  PlayMode contracts around full-body Down/Rise seams, all-fours support,
  every imported Rise frame's visible floor boundary, physical chest motion,
  bounded pelvis, owned collision policy, exact recovery and input cleanup.

Verification:

- Blender 5.0.1 production generation and the embedded recovery validator:
  passed (`BP3D BUILD OK`, 23 actions, 1,534/4,500 triangles). Both Rise sides
  preserve their full-rig seams and place both hands and knees in the supported
  all-fours band before the lead-foot plant.
- Focused Unity PlayMode
  `FailedBalanceCheck_FallsRecoversAndSchedulesCooldown`: 1/1 passed in
  7.15 seconds after importing the final animation FBX and runtime prefab.
  The separate imported-pose contract
  `RiseClips_PassThroughGroundedAllFoursBeforeNeutral` also passed 1/1 in
  0.28 seconds for both physical sides, including a dense 41-frame floor sweep.
- Fast mode intentionally omitted complete Unity suites, a player build and
  startup smoke.

## 2026-08-10 — Grounded and laterally anchored intoxicated 3D walking

- Restored the grounded-pose contract lost in the sprite-to-3D transition.
  The ordinary presentation now caches the neutral deformed boot-sole contour
  and offsets only the pelvis after Walk plus additive status bones, keeping
  the lower visible sole at its grounded height without moving the
  CharacterController root or `ModelRoot`.
- Made the procedural intoxication/balance layer idempotent by restoring its
  clean locomotion pose before every graph evaluation, clip sample, repeated
  late-pose application and lifecycle teardown. Removed the old unconditional
  intoxication pelvis drop; contextual Fall/Down/Rise and interaction clips
  remain outside ordinary grounding.
- Removed procedural pelvis X translation from intoxication and balance. Its
  intended `0.018` local stagger was multiplied by the imported rig's `100x`
  hierarchy scale and slid the complete visible skeleton by up to `1.8 m`;
  pelvis/chest rotation, arm stagger and knee articulation retain the sway
  without moving the authored horizontal rig anchor.
- Added a focused PlayMode regression that bakes the production foot meshes,
  covers a complete Walk cycle and the full `6.38 s` maximum-intoxication
  horizontal stagger period, rejects floor penetration, hovering and any
  lateral pelvis envelope beyond the authored Walk, and locks root/model-root
  stability plus repeated-pose idempotence.

Verification:

- Focused PlayMode verification passed `1/1`:
  `Player3DOrdinaryPresentationPlayModeTests.MaximumIntoxicationWalk_KeepsVisibleRigAnchored`
  in `8.37 s`.
- Fast mode intentionally omitted complete Unity suites, a player build,
  startup smoke and manual rendered review.

## 2026-08-09 — Placed a permanent ashtray under the balcony flick

- Sampled the shipped `SmokeExit` discard pose and placed a `0.26 m`
  low-poly worn enamel ashtray at Home-local `(7.25, 1.12, -1.67)`. Its base
  rests on the outer rail cap and its dish covers the animated ember point
  around `(7.14, 1.30, -1.67)`.
- Composed the visual-only body, dark basin and ash remnant under the permanent
  `Home Balcony` hierarchy. The prop owns no collider, light, particles or
  interaction lifecycle and is deliberately excluded from the rail dither
  group, so it remains active before, during and after smoking.
- Extended the existing smoking PlayMode regression to lock shared-material
  reuse, rail contact, exact plan placement, exit-flick coverage and continued
  visibility after the interaction restores.

Verification:

- `dotnet build BarPromenade.PlayModeTests.csproj -nologo` compiled runtime and
  PlayMode test assemblies with `0` warnings and `0` errors.
- The focused Unity test invocation could not acquire the project because the
  user's Unity editor was already open, so it exited before compilation and
  produced no test-result XML; the running editor was left untouched.
- `git diff --check` passed. Fast mode intentionally
  omitted complete Unity suites, a player build, startup smoke and manual
  rendered review.

## 2026-08-09 — Restored periodic balcony-smoking exhale smoke

- Added a deterministic runtime mouth plume to the existing 3D smoking loop.
  One `16`-particle burst starts at loop-local frame `16`, repeats with the
  held `9.5 s` cadence and reuses the shared procedural atmosphere material.
  The emitter follows the registered mouth socket without inheriting its FBX
  scale, while world-space particles travel cityward, expand and fade before
  the next loop under a `32`-particle cap. Larger particles, stronger opacity,
  broader procedural coverage and longer lifetimes keep the plume readable
  through the low-resolution PS1 composite.
- Integrated the effect with smoking ownership: positioning and entry remain
  clear, Looping starts the scheduled emitter, Exiting stops new emission but
  lets the detached plume dissipate, and completion, cancellation, disable,
  destroy or reinitialization clear every remaining particle.
- Extended the existing smoking PlayMode regression to prove two separated
  bursts one complete loop apart, outward mouth alignment and velocity,
  queued exit at an unsafe frame, lingering world-space smoke during exit and
  exact cleanup afterward.

Verification:

- Focused PlayMode verification passed `1/1`:
  `HomeBalconySmokingInteractionPlayModeTests.Smoking_ClickableExitQueuesAtCalmFrameAndRestores`.
  The run completed in `9.95 s` with no compilation or test errors.
- `git diff --check` passed. Fast mode intentionally omitted complete Unity
  suites, a player build, startup smoke and manual rendered review.

## 2026-08-09 — Added a compact lamp above the apartment entrance

- Added a deterministic `Home Entry Door Lamp` assembly to the generated Home
  interior: a narrow dark housing and hood, a shared HDR emissive amber lens
  and a shared depth-tested halo. It is centered in the existing transom above
  the door, remains under `0.35 m` wide and has no collider.
- Added a co-located shadowless warm ForcePixel Spot aimed down and into the
  room. Its full-strength cone reaches both the entrance door and the floor in
  front of it, so the fixture produces a real local pool instead of only an
  emissive dot. The explicit Home atmosphere budget is now five local lights.
- Extended the existing Home presentation regression to lock the hierarchy,
  shared materials, bloom threshold, transom placement, Full-HD main-camera
  framing, lack of collision, co-located light, illuminated door/floor targets
  and the five-light realtime budget. The old `Home Exit Header` absence
  contract remains in place.

Verification:

- Focused atmosphere PlayMode verification passed `1/1` and confirmed the real
  Spot's position, direction, intensity, range, cone, warm color and five-light
  ownership budget.
- The focused Home presentation test reached and passed every entry-lamp
  integration assertion, including full-strength coverage of the door and
  floor, then failed later in the pre-existing player-framing assertion with
  `minX = -0.0799` while the worktree contains the separate in-progress bed and
  player-animation changes. No lamp assertion failed.
- `git diff --check` passed. Fast mode intentionally omitted complete Unity
  suites, a player build and a startup smoke.

## 2026-08-09 — Re-authored balcony smoking around a real inhale

- Replaced the two-pose smoking motion with authored Blender sequences for a
  settled cityward stance, jacket reach, cigarette draw, mouth contact, cupped
  first light, held inhale, lowered-hand exhale and a rail-side exit flick.
  The existing four-second enter/loop, two-second exit, `9.5 s` held loop and
  calm exit boundaries remain unchanged.
- Corrected the socket prop from a backward `120 x 10 mm` cylinder with an
  embedded ember to a roughly `74 mm` cigarette aligned along socket-local
  `+Y`: `70 x 6.5 mm` paper plus a contiguous `4 x 7 mm` ember. It now appears
  only after the hand leaves the coat and disappears on the exit flick. The
  prop root cancels Unity's inherited FBX bone scale so those dimensions also
  remain exact in world space instead of expanding by `100x` in play mode.
- Bumped the deterministic Blender generator to `2.4.0` and added smoking
  validation for every Action's fixed root, source-facing socket contract,
  low-hand rest clearance, mouth contact/alignment and exact loop seam. Unity
  coverage now measures the animated head-to-nose direction against the real
  Home-local `+X` city vector instead of trusting only the gameplay root.

Verification:

- Blender `5.0.1` regenerated and self-validated `73` separate meshes, `31`
  bones, six sockets, `23` in-place Actions and `1,534` triangles. Inhale
  socket-to-mouth distance is `5.275 mm`, socket-axis alignment is `0.9385`,
  and both root and loop-seam error are zero. Eight key poses plus side views
  were inspected without hand/face intersections.
- Unity rebuilt `Resources/Player/Player3D.prefab` at generator `2.4.0`.
  Focused PlayMode verification passed `1/1`:
  `HomeBalconySmokingInteractionPlayModeTests.Smoking_ClickableExitQueuesAtCalmFrameAndRestores`.
  Its geometry check measures the live paper and ember in world space through
  the imported animated socket hierarchy.
- `git diff --check` passed. Fast mode intentionally omitted complete Unity
  suites, a player build and a startup smoke.

## 2026-08-09 — Rebuilt 3D bed entry and wake around a real bedside sit

- Replaced the old foot-end dock with a clear segment of the long bed edge
  nearest the apartment door. The hero now approaches facing into the room
  with his back to the mattress, and both normal interaction and opening wake
  restore to that same grounded side dock.
- Added an optional held pelvis waypoint to the shared animated-interaction
  controller. Bed entry reaches a low seated hip, holds while both feet remain
  planted, then moves inward; wake reaches the same point from the bed centre,
  holds through the supported sit and only then proceeds to standing. This
  keeps runtime pelvis alignment synchronized with the authored Blender keys
  instead of sliding the seated pose through a direct centre-to-dock lerp.
- Re-authored three-second `BedEnter` and `BedExit` Actions. Entry sits first,
  braces on the mattress, swings the legs up and lowers through the side;
  exit wakes, rolls toward the door side, pushes the chest up, drops both legs,
  settles upright, releases the hands, leans weight over the feet and rises.
  `BedSleepLoop` keeps the head at the pillow, face upward and eyes closed.
- Bumped the deterministic Blender generator to `2.3.0`. Its validation now
  checks the new source `-X -> +X` sleep orientation. Blender regenerated the
  editable source, model and animation FBXs, manifest, preview and portrait.
  The ordinary transition is now three seconds; the opening multiplier is
  two, preserving its established six-second wake.
- Replaced the legacy sprite-extent assertions with production 3D checks. The
  focused bed regression now samples the real rig both in the sleep loop and
  at the door-side seated waypoint.

Verification:

- Blender `5.0.1` regenerated and self-validated `73` separate meshes, `31`
  bones, six sockets, `23` in-place Actions and `1,534` triangles. Entry and
  exit key poses were rendered against a diagnostic mattress; seated feet,
  hand support, forward weight transfer and final stand were inspected.
- Focused Unity PlayMode verification passed `1/1`:
  `HomeBedInteractionPlayModeTests.Bed_ProgrammaticSleepStartsInLoopAndWakeRestoresPlayer`.
  It sampled the production head/feet orientation and the real pelvis at the
  held door-side seated waypoint before confirming final control restoration.
- Fast mode intentionally omitted complete Unity suites, a player build and a
  startup smoke.

## 2026-08-04 — Articulated 3D walk and stronger idle

- Re-authored the production Blender locomotion Actions. Walk now uses eight
  contact/down/passing/up phases with opposite arm swing and independent
  forearm, hand, thigh, shin and foot rotation; both elbows remain flexed and
  each swing knee reaches a readable passing pose. Idle is now a four-second
  two-sided breathing and weight-shift loop that moves the pelvis, torso,
  head, arms and softly loaded knees while retaining the exact Relaxed seam
  required by contextual handoffs.
- Limited auto-clamped Bezier interpolation to Idle and Walk, leaving Relaxed
  plus all contextual, fall and facial Actions on their linear timing. Blender
  regenerated the editable source, both production FBXs, manifest, preview
  and portrait under generator `2.1.0`; Unity rebuilt the stamped runtime
  prefab with the new four-second Idle binding.
- Replaced the linear locomotion weight step with damped `0.14 s` start and
  `0.20 s` stop envelopes. Walk playback speed now follows the visible blend,
  so a hard release does not change cadence while the gait is still fading.
  The focused ordinary-presentation regression now checks monotonic
  intermediate weights and imported elbow, knee and ankle excursions.

Verification:

- Blender `5.0.1` regenerated and self-validated `73` separate meshes, `31`
  bones, six sockets, `23` in-place Actions and `1,534` triangles. Eight Walk
  phases were inspected from front/three-quarter and side views, together with
  the strengthened Idle phases; no joint flip or blocking mesh separation was
  found.
- Focused Unity PlayMode verification passed `1/1`:
  `Player3DOrdinaryPresentationPlayModeTests.FactoryCreatesModular3DPlayerAndDrivesLocomotion`.
  The dedicated asset setup completed successfully and rebuilt the production
  prefab at generator `2.1.0` with `Idle = 4.0 s`.
- `git diff --check` passed. In fast mode, no complete Unity suite, player
  build or startup smoke was run.

## 2026-08-04 — Correct 3D hero facing

- Rotated the imported FBX model by `180°` at the generated runtime-prefab
  boundary, so the visible anatomical front now follows the authoritative
  player root and its actual planar movement. `PlayerMotor`, camera-relative
  controls, in-place clips and root motion remain unchanged.
- Made the player asset regression compare the head-to-nose direction against
  the prefab's declared forward vector and validate the bandage/shoulder patch
  on physical left/right relative to that direction. The visual-capture helper
  now frames the prefab-space forward direction instead of applying the model
  adapter twice.

Verification:

- Unity rebuilt `Resources/Player/Player3D.prefab` successfully and compiled
  Runtime, Editor, EditMode and PlayMode assemblies without errors.
- Focused EditMode verification passed `1/1`:
  `Player3DAssetImportTests.ProductionModel_HasDeterministicRuntimePrefabContract`.
- `git diff --check` passed. In fast mode, no complete Unity suite, player
  build or startup smoke was run.

## 2026-08-04 — Complete modular 3D hero migration

- Promoted the Blender hero experiment into the production player asset path.
  The deterministic source now emits a `1.75 m` A-pose model, separate model
  and animation FBXs, a manifest, 23 in-place Generic Actions and a transparent
  portrait. The Unity prefab keeps 73 independent mesh objects, 16 required
  anatomical bindings, a 31-bone armature with six non-deforming sockets and
  one shared URP/Lit material with a property-block palette.
- Replaced the active hero presentation in City, Bar, Supermarket, Home and
  Stairwell with one `Resources/Player/Player3D.prefab` instantiated by
  `PlayerFactory`. A presentation-neutral seam now feeds locomotion and status
  state into the 3D PlayableGraph, preserves physical left/right details,
  drives face/intoxication/balance bones and samples left/right fall/down/rise
  clips while the gameplay root remains authoritative.
- Migrated bed sleep, balcony smoking and cat feeding to deterministic
  enter/loop/exit clips on the same continuous world rig. The shared contextual
  controller retains grounded positioning, neutral settle, sample-then-pelvis
  alignment, terminal holds, deferred unlock, atomic preparation and owned
  cleanup. The smoking prop uses the registered right-hand cigarette socket;
  the cat keeps its independent NPC sprite track.
- Rebuilt bar-drinking arms and the refrigerator reach as filtered camera-local
  subsets of the same production prefab. Owner-scoped visibility leases restore
  the exact world meshes and contact shadow. Inventory now uses the dedicated
  transparent 3D portrait rather than cropping the retired directional atlas.
- Removed the 22 legacy runtime player atlas PNGs together with the obsolete
  sprite-rig/dynamic-shadow code and shaders. Real hero meshes cast URP shadows
  and the analytic ground-contact patch remains planted and expands/offsets
  during falls. Historical player source art and tools remain only as retired
  lineage; NPC, cat and minigame sprites are unchanged.

Verification:

- Blender `5.0.1` regenerated and self-validated the production source, model
  and animation FBXs: `73` separate meshes, `31` bones, six sockets, `23`
  in-place Actions, `1,534` triangles and an exact `1.750 m` height. Unity then
  imported the assets, compiled the affected assemblies and rebuilt the
  runtime prefab successfully from its dependency signature.
- The focused GPU-backed PlayMode selection passed `15/16` on its combined
  run. Its sole failure was the new contact-sheet foreground threshold; after
  correcting the isolated capture lighting and background, that exact visual
  regression passed `1/1`. Thus all `16` selected gameplay-scene, ordinary
  presentation, contextual-animation, first-person, shadow and visual
  contracts passed in the final code state, and the resulting four-pose
  contact sheet was inspected manually.
- `git diff --check` passed. In accordance with fast mode, no complete Unity
  suite, packaged player build or startup smoke was run.

## 2026-08-04 — Experimental modular Blender hero

- Added a Blender-native low-poly generator for the locked player design. It
  derives the `1.75 m` proportions and primary joint heights from the current
  puppet, keeps the weary head-heavy silhouette and preserves the burgundy
  overshirt, charcoal shirt, navy trousers, heavy boots, left-forearm bandage,
  right-shoulder patch and diagonal strap without mirroring.
- Kept 16 core anatomical meshes plus hair, clothes, facial pieces and
  signature details independently editable. All 3D objects retain unique mesh
  datablocks, rigid armature weights and an explicit mapping to the existing
  nine `PlayerPuppetPart` groups; preview objects cannot enter FBX/GLB export.
- Documented background generation, relaxed/A-pose, height/seed controls,
  optional preview/manifest/FBX/GLB outputs and the anatomical side convention.
  The experiment remains outside `Assets` and is not integrated into runtime.

Verification:

- Blender `5.0.1` generated, self-validated, rendered and saved the relaxed
  model: `73` separate mesh objects, `1,534` triangles, ground contact at
  `Z=0`, exact `1.750 m` hair-tip height, outward face winding and correct
  bandage/patch sides. Temporary selection-only GLB and FBX exports also
  completed successfully; a `1.60 m` A-pose/alternate-seed run reached its
  requested height exactly under the same validator.
- The generated validation `.blend`, PNG and JSON stayed under ignored
  `TestResults`; Unity tests and a player build were not run for this isolated
  authoring-tool change.

## 2026-08-04 — Playable lake, cemetery and scrollable city map

- Extended the playable `default-coastal` blueprint east without changing its
  `12 x 12` road/lot core: a `4 x 4` Lake now surrounds `2 x 2` blocked water
  with walkable shore, and a `3 x 2` walkable Cemetery occupies the
  south-eastern edge. Both receive deterministic street approaches; the
  northern beach/water pair now spans the complete `16`-cell city width.
- Added one bounded data-first open-area decoration plan. Lake builds a stone
  water edge, reeds, rocks and a weathered boat; Cemetery builds a clear entry
  path, gated iron perimeter, ordered graves and sparse dark trees. Blocking
  geometry is batched by eight shared styles in `48 m` chunks, stays out of
  water and preserves each canonical access corridor.
- Made the city-map viewport retain a readable `22 px/cell` logical scale,
  clip overflow and pan independently on both axes. It focuses on the player
  when opened and supports WASD, right stick, wheel/Shift+wheel and
  middle/right-button dragging with per-axis scroll indicators.

Verification:

- `BarPromenade.EditModeTests.csproj` compiled the affected Runtime and
  EditMode test assemblies successfully with `0` warnings and `0` errors,
  including the new viewport and open-area planner sources.
- Focused Unity EditMode verification passed `4/4`: the expanded coastal
  blueprint, deterministic Lake/Cemetery decoration plan and both map viewport
  overflow/clamping contracts.
- `git diff --check` passed. Full suites and a player build were not run.

## 2026-08-04 — F9 city-map test teleport

- Added a City-only test-teleport toggle to the existing F9 debug window while
  retaining its BarInterior minigame and intoxication tools unchanged.
- Made every canonical map lot selectable in debug teleport mode, including
  ordinary lots, public places, home, supermarket and bars. The map replaces
  its route sidebar with an explicit `Teleport? / Yes` confirmation.
- Confirming closes the map, restores modal input ownership, teleports the hero
  to the selected lot's street-front return point (or nearest generated route
  fallback), faces the lot and rebuilds any planned route from the new
  position. Normal bar-route selection remains unchanged while the mode is
  disabled.

Verification:

- Focused PlayMode verification passed `1/1` for the F9-owned toggle, selection
  of a non-bar lot and the resulting physical player relocation/input
  restoration; the run also compiled the affected Runtime and test assemblies.
- Both localization catalogs parse with all six new keys, no duplicates, and
  `git diff --check` passes.
- Full EditMode/PlayMode suites and a player build were not run.

## 2026-08-04 — Blueprint-driven coastal city MVP

- Added an immutable `CityBlueprint` model and fluent builder with stable
  blueprint/area IDs, `UrbanBuilt` versus `NonUrbanOpen` classification,
  reusable archetypes, placement policies and per-cell buildable, park,
  open-land or water topology. The catalog now owns the playable
  `default-coastal` blueprint and an explicit legacy rectangular path.
- Made the road graph, lots, validation, world/map bounds and ground surfaces
  consume the connected sparse footprint rather than assuming every bounding
  cell exists. The existing `4 x 4` park stays fixed on the blueprint center
  anchor while built-area placements can be rearranged independently.
- Extended the default city north with one connected walkable beach row and a
  continuous water row. A deterministic street approach opens its road fence,
  the player can reach the water line, water remains outside navigation and
  night fixtures reject water positions.
- Added generic Lake and Cemetery profiles for authored blueprints. Lake shore,
  water and cemetery ground receive typed surfaces, map presentation and one
  canonical street-linked approach, without claiming bespoke landmark or prop
  art in this MVP.
- Propagated area IDs through generated lots, district descriptors, bars and
  public-place descriptors, and persisted the selected blueprint ID in the
  session. City and the Home balcony exterior now regenerate from the same
  blueprint ID and seed.

Verification:

- Focused Unity EditMode verification passed `28/28` for
  `CityLayoutGeneratorTests`, including the coastal default, urban-area swap
  and irregular Lake/Cemetery blueprint contracts; Unity also compiled the
  Runtime, EditModeTests and PlayModeTests assemblies for the run.
- Localization catalogs parsed successfully and `git diff --check` passed.
- Full EditMode/PlayMode suites and a player build were not run.

## 2026-08-04 — Day/night runtime optimization

- Made City and Home day/night presentation change-driven: advancing through
  a stable day or night sample now updates the observed minute without
  reapplying identical lighting, bulb, halo or realtime-light state.
- Reused the active `RenderSettings.sun`, removed recurring
  `DynamicGI.UpdateEnvironment` calls from ordinary phase updates and retained
  environment refreshes for forced setup and Balcony lifecycle boundaries.
- Made night-factor writes idempotent, reused one bulb
  `MaterialPropertyBlock`, kept forced refresh semantics and stopped the
  disabled daytime street-light pool from rescanning the City's `438` lamp
  anchors. A `0 -> visible` transition refreshes the pool once; inactive Home
  exterior lighting waits for its existing Balcony activation refresh.

Verification:

- Focused EditMode day/night sample coverage passed `9/9`.
- Focused City day/night and Home Balcony PlayMode coverage passed `2/2`;
  after tightening the near-zero visibility guard, the final City regression
  rerun passed `1/1`.
- `git diff --check` passed. Full suites and a player build were not run.

## 2026-08-04 — Wake-started session clock and MVP day/night

- Added session-owned game time that resets frozen at `05:59`; a successful
  startup Wake sets `06:00` and starts the only persistent scaled-time driver.
  It advances at `1.0` game minute per real second, so one in-game day is
  exactly `1440` real seconds (`24` minutes), including midnight/day-index
  rollover and continuity across scene loads.
- Made the Home alarm clock follow current session hours and minutes after the
  opening handoff and on later Home visits; the inventory Status panel now
  exposes the same current `HH:MM`.
- Added shared night/dawn/day/dusk lighting samples for City, the Home window
  and the reconstructed Balcony exterior. City/Home exterior lamps, bar lights
  and halos fade with the night factor; Bar, Supermarket and Stairwell visuals
  remain unchanged.
- Kept City fog settings, matching background, `48 m` far clip,
  `CityFogField` and `CityNoirVolumeProfile` outside the time-of-day system.

Verification:

- Focused EditMode game-time/day-night rules: `13/13` passed.
- Focused PlayMode wake/clock, Home balcony and City fog-invariant paths:
  `4/4` passed; the repaired post-Wake cancellation path also passed its
  focused rerun `1/1`.
- After adding the inventory `HH:MM`, the PlayMode test project build passed
  with `0` warnings/errors and both localization catalogs parsed as valid JSON.
- `git diff --check` passed. Full suites and a player build were not run.

## 2026-08-04 — Hunger, stress and usable provisions

- Added session-owned hunger and stress scales with explicit `0/100` defaults
  for every new run. This MVP adds no passive or event-driven growth yet.
- Added data-first consumable values and one atomic inventory-use boundary.
  Cheap supermarket food relieves hunger only down to `20/100`; a vodka
  bottle represents four servings and applies its intoxication, drink count
  and stress relief together without consuming the item on a failed use.
- Routed actual alcoholic servings from direct purchases, cocktails, Beer
  Pong, Split the G and Tincture Match through the shared stress-relief commit,
  including fractional Split the G consumption and duplicate-snapshot guards.
- Kept the existing compact status card and added hunger/stress bars beside
  the portrait, while removing the redundant textual intoxication-stage label.
  Inventory now exposes localized contextual Eat/Drink actions, `U`/gamepad-
  West input, disabled no-effect food and inline result feedback.
- Added bounded hunger/stress diagnostics, focused domain/session/UI coverage
  and updated the current architecture, system and release documentation.

Verification:

- Focused EditMode coverage for needs rules, consumable/drink catalogs, session
  transactions and localization passed `102/102`.
- Review-driven stale/duplicate snapshot and saturated-counter regressions in
  `GameSessionStateTests` passed `39/39` after the final guard was tightened.
- Focused PlayMode
  `InventoryPlayModeTests.UKey_DrinksAtZeroStressAndKeepsMenuOpen` passed
  `1/1` with the graphics device required by the existing inventory preview.
- `git diff --check` passed. Full suites, player build, startup smoke and manual
  rendered review were intentionally not run under the fast-mode policy.

## 2026-08-04 — Optional supermarket music slot

- Added the optional `Resources/Audio/SupermarketMusic/supermarket_theme`
  composition slot and installed its scene-owned player under the runtime
  supermarket root.
- Reused the shared music mixer route, mild low-pass treatment, background
  loading, one-second unscaled fade envelope and scene-transition fade gate;
  the shop remains silent-safe if its track is unavailable.
- Added the supplied `supermarket_theme.mp3` with streaming, background-load
  and no-preload import settings.
- Added the resource-folder handoff instructions and focused scene-bootstrap
  coverage that works both before and after the clip is supplied.

Verification:

- Focused PlayMode
  `SupermarketPurchasePersistencePlayModeTests.Scene_BootstrapsOptionalMusicThroughSharedMixer`
  passed `1/1`.
- Full suites, player build, startup smoke and manual audio review were
  intentionally not run under the fast-mode verification policy.

## 2026-08-04 — Grocery-shop marker and map hover names

- Added the canonical `CityLayout.Supermarket` to the city map as a distinct
  high-contrast shopping-bag marker without making it a bar route stop.
- Registered bars, the player home, supermarket and district public places as
  localized hover targets. Overlapping hitboxes resolve by nearest marker and
  deterministic priority, while one wrapped retro tooltip flips and clamps to
  remain inside the map.
- Added RU/EN grocery-shop map text and focused coverage for canonical layout
  integration, hover arbitration, edge-safe tooltip placement and localization.

Verification:

- Focused EditMode `CityMapDistrictPresentationTests` and
  `LocalizationCatalogTests` passed `28/28` in the primary Unity invocation.
- The review-driven nearest-marker edge-case regression passed `1/1` in one
  narrow follow-up invocation.
- Full suites, player build, startup smoke and manual rendered review were
  intentionally not run under the fast-mode verification policy.

## 2026-08-04 — Product-centered cross-shelf supermarket browsing

- Kept the shelf browser under one modal ownership while extending previous/
  next selection across the deterministic dry, pantry/spirits and cold shelf
  order. Empty shelves are skipped in both directions, and buying a shelf's
  final product continues at the next stocked shelf instead of closing early.
- Reused every shelf's authored fixed camera position and field of view, but now
  aim it at the combined world renderer bounds of the highlighted product on
  open, selection, shelf transfer and post-purchase fallback.
- Added low-contrast `<`/`>` controls immediately beside the selected model's
  projected screen bounds. They brighten only on hover, share the existing
  keyboard/gamepad navigation action and block click-through into world stock;
  no footer control hint was added.

Verification:

- Focused PlayMode `SupermarketPurchasePersistencePlayModeTests` passed `2/2`,
  covering product centering, arrow placement/hit blocking, bidirectional shelf
  transfer, empty-shelf skipping, continued browsing after purchase, exact
  modal/camera/input restoration and the existing reload persistence contract.
- Full suites, player build and startup smoke were intentionally not run under
  the fast-mode verification policy.

## 2026-08-04 — Finite-stock supermarket

- Added `SupermarketInterior` as a seventh build scene and registered its
  runtime root. The default city now reserves one deterministic eligible
  street-front supermarket, preferring Residential and the shortest traversable
  route from the home; its dedicated facade, apron, fence opening, interaction
  trigger and return point use the canonical lot/frontage data.
- Added a validated `16 x 11 x 3.6 m` shop plan and runtime world with protected
  circulation, three shelf sections, a stockroom facade, a decorative checkout
  and one decorative cashier. The cashier/register remain scenery; purchases
  begin at a shelf and use its authored fixed product view.
- Added five localized finite product offers and shared inventory models/icons:
  chicken egg, vodka bottle, closed stew can, instant noodles and day-old loaf.
  The sealed `ClosedStewCan` remains a distinct inventory ID from the cat-ready
  refrigerator `OpenStewCan`.
- Added one atomic world-item purchase boundary for source validity, catalog
  membership, affordability and stack capacity. Success records the stable
  source, adds one inventory item, deducts cash and immediately removes the
  physical shelf product; rebuilding the scene filters purchased sources until
  `BeginNewGame`. Every failure leaves cash, inventory and shelf persistence
  unchanged.
- Added shelf pointer/keyboard/gamepad selection, localized price/balance/error
  UI, exact modal/camera/player restoration, supermarket inventory/pause/status
  installation and the separate City round-trip context.

Verification:

- Targeted EditMode passed `21/21` across
  `SupermarketPurchaseRulesTests`, `SupermarketInteriorLayoutTests`,
  `SupermarketCityPlanningTests` and `ProjectBuildSceneTests`.
- Focused PlayMode `SupermarketPurchasePersistencePlayModeTests` passed `1/1`.
  Full suites, player build and startup smoke were intentionally not run under
  the fast-mode verification policy.

## 2026-08-03 — Minimal verification by default

- Audited the canonical workflows and repository instructions after ordinary
  feature work expanded into `777` EditMode tests, `164` PlayMode tests, three
  redundant project builds, a Windows player build and a startup smoke. The
  delay came from automatic release-style verification, not from retaining the
  tests themselves.
- Made FAST verification the default even for shared and cross-system changes.
  A normal request now gets one primary check; only a shared-framework change
  may add one focused check. Documentation uses diff-check only; deterministic
  art/data uses its validator; C# uses one narrow EditMode/PlayMode selection,
  or the highest affected project build when no suitable test exists.
- Full suites now require an explicit full-regression/release request. A player
  build runs only when requested as the deliverable or gate; smoke is reserved
  for an explicit request or changed packaged-startup behavior. Existing tests
  remain available for targeted and release use instead of being deleted.
- Clarified that the contextual-animation standard defines coverage that must
  exist, not a list that must be executed on every animation change. Generic
  stall/cancel/hitch/handoff cases remain owned by the shared pipeline; each new
  animation now extends its unique validator and adds at most one happy-path
  PlayMode interaction when existing parameterized coverage cannot represent
  its scene wiring, plus atomicity coverage only for a new resource contract.

Verification:

- Documentation-only policy change: reviewed the instruction diff and ran
  `git diff --check`; no Unity test, project build or player smoke was run.

## 2026-08-03 — Mandatory future contextual-animation standard

- Added `ai/contextual-animation-standard.md` as the normative contract for
  every future `E`/area/prompt interaction that replaces the ordinary player
  rig with a bespoke sprite atlas. It requires independent authored entry,
  action and exit data; visible constrained positioning; exact neutral endpoint
  frames; a direct zero-fade handoff; terminal-frame presentation; camera-plane
  or world-up pivot correctness; owned lifecycle cleanup; and deterministic
  asset, timeline, EditMode and PlayMode coverage.
- Linked the standard from the project entry point, AI memory index, repository
  Unity rules, accepted architecture decision and player art specification so a
  future implementation cannot treat the current bed/smoking/cat behavior as a
  one-off. Deviations now require an explicit user decision recorded as an
  accepted architecture exception.

Verification:

- Documentation links and scope were reviewed against the implemented shared
  interaction pipeline; `git diff --check` passed. No runtime files or Unity
  assets changed in this documentation-only follow-up.

## 2026-08-03 — Authored entry and exit for contextual animations

- Added a shared visible `Positioning` phase for the bed, balcony-smoking and
  cat-feeding interactions. Pressing `E` now captures modal ownership while the
  ordinary articulated hero walks and turns through `PlayerMotor` to a grounded
  authored entry root; manual movement cannot redirect the approach. Separate
  entry/action/exit root, hip and facing data replace the former implicit stand
  anchor, and unreachable height, stalled motion, scene transition, disable or
  destroy paths cancel through the same state-restoring cleanup.
- Added a deterministic ordinary-rig handoff lock. Exact entry alignment selects
  the nearest eight-way direction without hysteresis, clears gait/breath/face
  offsets, holds one neutral rendered frame, then switches directly to the atlas.
  Bed and cat use exact preflipped `FrontLeft` endpoints; smoking uses the actual
  Balcony-view `BackRight` endpoint. All three installed definitions now use zero
  sprite alpha crossfade. Exit holds the atlas's terminal frame, restores the
  separately authored exit pose and defers rig unlock through its final
  `LateUpdate` render frame.
- Kept camera-plane and world-up handoffs physically aligned. Bed and cat resolve
  their upright hip references against live camera up and refresh after camera
  `LateUpdate`; Balcony ordinary and smoking sprites stay world-up. The grounded
  player-root offset is explicit in all three plans, and Cat interaction
  availability rejects a player on another stairwell level.
- Rebuilt and locked the three 64-frame player atlases, source contracts and
  hashes. Smoking frames `000/063` now match ordinary `BackRight` cell `3` exactly
  without the retired endpoint dissolve; bed and cat endpoints match ordinary
  `FrontLeft` cell `7`. Updated plans, runtime lifecycle tests and AI system docs.

Verification:

- All smoking extractor/packer, bed-atlas and cat-atlas validators passed. Runtime,
  EditModeTests and PlayModeTests projects compiled with zero errors; the final
  sequential EditModeTests and PlayModeTests builds had zero warnings.
- Complete Unity EditMode coverage passed `777/777`. Complete PlayMode coverage
  passed `162/164`; every changed bed/smoking/cat positioning, hard-handoff,
  cancellation and paired-feeding scenario passed. The two unrelated suite
  failures were existing timing-sensitive checks: the bar arrival was already
  not playing at its full-suite assertion and passed on the immediate isolated
  retry;
  the hungry-cat prompt's gameplay state passed but its batchmode-only
  `HasRenderedLayout` assertion still did not receive an `OnGUI` event.
- The Windows player built successfully at `226,017,372` bytes with zero warnings.
  A hidden 15-second D3D11 startup smoke stayed alive and logged no error,
  exception, assertion or crash before its exact launched PID was stopped.

## 2026-08-03 — Inventory-backed cat feeding

- Added a reusable single-stack inventory-target definition, pure
  `Choice -> Confirmation -> Executing` model and scene-local modal controller.
  Talk/Interact, default-No confirmation, pointer/keyboard/gamepad input,
  temporary prompt feedback, stale-requirement rejection and lifecycle cleanup
  now share one contract that other world targets can reuse.
- Added read-only inventory count/requirement queries and retained the existing
  atomic `TryRemoveInventoryItem` commit. A handler prepares every required
  presentation resource before removal, so failed setup, No, missing stew or an
  item disappearing during confirmation cannot start a free interaction or
  consume a partial requirement. The shared player animation now exposes a
  non-starting resource/anchor preflight; a thrown start refunds the committed
  stack, and target cleanup cancels only resources that adapter acquired.
- Replaced the cat's direct placeholder response with the shared choice menu.
  Talk preserves the old response; Interact without stew shows the localized
  hunger thought; Interact with stew asks `Feed the cat?` and consumes exactly
  one `OpenStewCan` only after Yes.
- Added a validated middle-shot feeding dock and paired presentation. The
  point-filtered `1024x768` player atlas plays 24 present, 16 action and 24
  return frames; the cat begins its independent top-first `512x128`, 16-frame
  `6 fps` track at the player loop while ordinary idle/look is paused. Normal
  completion and abnormal modal/target lifecycle paths restore the player rig,
  shadows, cat, camera, HUD, input and lock ownership.
- Added raw and keyed source sheets plus explicit contracts under
  `ArtSource/Player/CatFeeding` and `ArtSource/Stairwell/Cat/Feeding`. New
  deterministic validators/packers are
  `tools/build-player-cat-feeding-atlas.py` and
  `tools/build-stairwell-cat-feeding-atlas.py`; their runtime outputs are
  `Resources/Player/PlayerCatFeedingAtlas` and
  `Resources/Stairwell/Cat/StairwellCatFeedingAtlas`.
- Replaced the prompt's fixed `180x24` layout with a centered responsive panel:
  it expands up to `520` logical pixels, enables wrapping and grows vertically
  when required. Added an exact long-Russian-feedback regression that checks
  expansion, wrapping height and containment inside the `640x360` UI canvas.
- Corrected the player feeding presentation to use the shared authored
  horizontal mirror. The source sheet faces image-right while the MiddleFlight
  cat is camera-left; `TextureFlipX = true` now turns the hero and can toward
  the cat. EditMode and runtime PlayMode contracts cover both the applied flip
  and the camera-space cat/player ordering.

Verification:

- Focused inventory-target, session, localization, animated-player, interaction,
  cat runtime and feeding-asset EditMode coverage passed `97/97`.
- Focused GPU Stairwell PlayMode coverage passed `6/6`, including Talk,
  missing-stew feedback, default-No confirmation, atomic one-can consumption,
  paired animation visibility and exact completion cleanup.
- Complete EditMode coverage passed `769/769`. Both complete GPU D3D12
  PlayMode runs passed `157/158`; the only failure was the pre-existing bar
  arrival smoke assertion after its presentation had already received skip
  input from shared suite state. That exact unrelated test passed `1/1` in a
  fresh isolated GPU run, while all six Stairwell/cat tests passed in every run.
- Runtime/EditModeTests and PlayModeTests projects built with zero warnings or
  errors. A Windows x64 player built successfully at
  `Build/Windows/BarPromenade.exe` (`226,003,548` bytes); its single warning is
  Unity URP's `Hidden/Core/DebugOccluder` D3D11 truncation warning. The player
  remained healthy through a 15-second D3D12 startup smoke with no gameplay
  exceptions or assertions.
- The follow-up responsive-prompt change compiled through Runtime,
  EditModeTests and PlayModeTests with zero warnings or errors. The focused
  non-batch graphical PlayMode test passed `1/1` in the working project,
  exercising the actual `OnGUI` path and confirming that the localized hungry-
  cat text expands beyond the old width, fits and stays inside the canvas.

## 2026-08-03 — Quieter apartment music

- Reduced only the looping Home theme's source-volume ceiling from the shared
  `0.65` scene-music level to `0.35`, leaving City, Bar, Stairwell and the
  separate balcony-smoking vignette mix unchanged.
- Added focused coverage for the final Home source volume after fade-in.

Verification:

- Unity runtime, EditMode and PlayMode assemblies compiled successfully.
- Focused `HomeMusicPlayerPlayModeTests` passed `3/3`; `git diff --check`
  passed.

## 2026-08-03 — Inventory presentation fidelity

- Moved the clickable slot hit target behind inventory contents so all five
  generated point-filtered item icons remain visible above interaction state.
- Replaced the separately painted inventory portrait with a direct upper-body
  crop from the canonical neutral front player atlas cell and standardized the
  Russian cash label on the session's dollar currency.
- Added one hidden lifecycle-owned `160x128` orthographic RenderTexture stage
  with warm/cool local lighting and unscaled rotation. The lower selected-item
  panel and Examine screen now show the live 3D model while gameplay is paused.
- Extracted the refrigerator's vodka, egg and open-stew geometry into a shared
  collider-free item factory and added matching low-poly apartment keys and
  lighter models. The refrigerator retains its exact roots, dimensions,
  selection colliders and shared-material contract.
- Added presentation coverage for visible icon pixels, canonical portrait
  provenance, all five finite collider-free models, dollar localization,
  selection/model synchronization, paused-time rotation, GPU-visible preview
  pixels and preview cleanup.

Verification:

- Unity runtime, EditMode and PlayMode assemblies compiled successfully.
- Focused inventory presentation/localization EditMode passed `19/19`.
- Focused inventory/refrigerator PlayMode passed `7/7`, including a direct GPU
  RenderTexture readback of the selected model.
- Full EditMode passed `741/741`; full PlayMode passed `156/156` with no
  failed, skipped or inconclusive tests.
- A Windows x64 release player built successfully at
  `Build/Windows/BarPromenade.exe` (`222,570,079` bytes). The only build warning
  was the package-owned URP `Hidden/Core/DebugOccluder` D3D11 truncation
  warning. A hidden D3D12 smoke reached a ready `MainMenu` at `1280x720` and
  emitted no runtime warning, assertion, error or exception; the direct
  PlayMode GPU check covers the actual open inventory preview path.

## 2026-08-03 — Static decorative UI labels

- Corrected the shared retro label style so decorative `GUI.Label` text keeps
  its authored color through normal, hover, active and focused pointer states.
  The yellow Home-opening title remains yellow, while its black offset shadow
  can no longer turn yellow on hover and appear as a duplicate title.
- Interactive button styles retain their existing hover and pressed colors.

Verification:

- Runtime and EditMode test assemblies built with zero warnings and errors.
- Focused `RetroUiThemeTests` passed `12/12`; `git diff --check` passed.

## 2026-08-03 — PS1 hero inventory and refrigerator pickup

- Added one localized fullscreen `640x360` inventory to City, BarInterior,
  HomeInterior and StairwellInterior. `I` or gamepad North captures the shared
  modal lock, freezes scaled time, hides movement/interaction/camera/HUD input
  and restores the exact captured state on toggle, cancel, transition, disable
  or destroy. Pause executes first, so Escape closes inventory without opening
  pause in the same frame.
- Added a pure item catalog, ordered bounded stack state and menu model. Fresh
  sessions begin with apartment keys and a lighter; status shows the current
  intoxication stage/level and cash. The IMGUI presentation uses generated
  point-filtered portrait/item textures and exposes only working Examine and
  Close commands.
- Replaced the refrigerator `Take` placeholder with an atomic stable-source
  transfer for vodka, egg and open stew. A taken item is removed from the live
  refrigerator registry/model, added to the session inventory and omitted when
  Home is reconstructed after a scene round trip. `Use` remains unavailable
  until item-use rules exist.

Verification:

- Unity 6000.5.5f1 compiled Runtime, EditModeTests and PlayModeTests; direct
  `dotnet build Assembly-CSharp.csproj` completed with zero warnings/errors.
- Focused inventory/session/localization EditMode passed `43/43`; focused
  inventory/refrigerator PlayMode passed `12/12`, followed by the updated
  inventory controller lifecycle set at `4/4`.
- Full EditMode passed `728/728`; full PlayMode passed `155/155` with no failed,
  skipped or inconclusive tests.
- A Windows x64 player built successfully at
  `Builds/InventorySmoke/BarPromenade.exe`. A hidden D3D12 release-player smoke
  reached `MainMenu -> HomeInterior`, initialized Home in about `1.2 s` and
  emitted no runtime exception. Null-GPU `-nographics` remains unsupported by
  the project's packaged URP material contract and was not used for the valid
  smoke result.

## 2026-08-03 — Shared-lock gameplay pause menu

- Added one localized PS1-style Pause/Resume/Start Over/Quit interface to the
  runtime UI roots in City, BarInterior, HomeInterior and StairwellInterior.
  Restart and quit use a separate default-No confirmation page; save/load and
  settings remain absent.
- Pause captures the existing fullscreen modal lock, exact input/camera/HUD
  state, time scale and listener-pause flag. It freezes scaled gameplay and
  non-UI audio while the UI SFX pool remains audible, restores safely after a
  one-frame resume guard and restores immediately on lifecycle/destructive
  paths.
- Existing child modals keep first ownership of Escape, the Home opening keeps
  its exclusive lock and the Bar-specific gate prevents pause from skipping
  the arrival reveal.

Verification:

- Unity 6000.5.5f1 imported and compiled Runtime, EditModeTests and
  PlayModeTests; direct .NET builds completed with zero warnings and errors.
- Focused pause tests passed `5/5` EditMode and `5/5` PlayMode.
- Full EditMode passed `721/721`.
- Full PlayMode passed `144` active tests with the five existing ignored tests;
  one unrelated existing motor-inertia test failed because its queued key
  release was not processed before the first braking sample. The same failure
  reproduced in an isolated rerun; every pause and four-scene installation
  check passed.
- A Windows x64 player build completed successfully at
  `Temp/PauseMenuBuild/BarPromenade.exe`.

## 2026-08-03 — Silent automated test runs

- Added one shared Unity Test Framework run callback used by both EditMode and
  PlayMode assemblies. It captures the current global listener volume, keeps
  output at zero throughout the run and restores the captured value afterward.
- Muting uses `AudioListener.volume` rather than pausing audio, so source play
  state, samples, fades, scheduling and DSP-dependent assertions keep their
  ordinary semantics. The callback is preserved for standalone player tests.

Verification:

- Unity script compilation completed with `Tundra build success`.
- Focused EditMode mute registration passed `1/1`.
- Focused PlayMode mute plus existing scene/Home music lifecycle coverage
  passed `13/13`.
- TestSupport, EditModeTests and PlayModeTests projects built with zero
  warnings and zero errors.

## 2026-08-03 — Eight-direction detailed fall animations

- Added 16 transparent detailed fall atlases: all eight existing player views
  with separately authored screen-left and screen-right variants. Each atlas
  exposes 80 `128x96` cells, for 1280 runtime sprites without mirroring the
  physical left-arm bandage or right-shoulder patch.
- Added an explicit unscaled `14`-frame fall, `36`-frame down and `30`-frame
  rise mapping. The rig lazily slices only requested atlases, reuses its body
  renderer, hides the other eight layers and restores the ordinary puppet.
  Dynamic shadows use the matching full-body frame without adding renderers.
- Added a deterministic importer for Point/Clamp, no mipmaps and uncompressed
  Standalone texture data.

Verification:

- Validated all 16 RGBA files at `1280x768`: all 1280 cells contain visible
  pixels, transparent corners are clean and no green fringe remains.
- Runtime, EditMode and PlayMode C# projects built with zero warnings/errors.
- Focused fall tests passed `14/14` EditMode and `2/2` PlayMode.
- Full suites passed `715/715` EditMode and `139/139` active PlayMode tests;
  the existing five ignored PlayMode cases remained ignored. No player build
  was produced.

## 2026-08-03 — Moving balance checks

- Added a motor-input policy to the shared modal lock. Fullscreen presentations
  still stop locomotion, while the balance-specific option preserves it during
  warning and active challenge phases.
- A failed balance check now disables the motor only when the fall begins and
  restores the captured input state after rising or cancellation.
- Updated the focused PlayMode contract to require movement during the check
  and movement blocking during the actual fall.

Verification:

- Runtime and PlayMode-test C# projects compiled with zero warnings or errors;
  Unity script compilation completed with `Tundra build success`.
- The focused intoxication PlayMode class passed `3/3`, covering movement in
  Warning/Active, motor stop on failure and exact restoration after recovery.
- Full PlayMode and player-build checks were intentionally deferred in fast
  mode.

## 2026-08-02 — Accelerating intoxication recovery

- Added session-owned fractional recovery that lowers the integer intoxication
  level during free gameplay on unscaled time and persists across gameplay
  scene changes.
- Recovery takes about `12 s` per point at level `100` and accelerates
  continuously to `3 s` per point near sober. It clamps at zero, preserves the
  last-drink and consumed-drink context, and clears balance scheduling at the
  existing threshold.
- Paused recovery while a modal lock owns gameplay because the current bar
  minigames commit absolute intoxication snapshots.

Verification:

- Runtime and EditMode-test C# projects compiled with zero warnings or errors.
- Focused intoxication rules/session EditMode tests passed `51/51`.
- Full PlayMode and player-build checks were intentionally deferred in fast
  mode.

## 2026-08-02 — Runtime fog shader variant retained

- Traced the Editor/player mismatch to built-in shader stripping: every build
  scene serializes fog off, while `RuntimeSceneSetup` enables Exp2 only after
  loading. The previous build reduced `City Atmosphere Particle` from eight
  variants to one.
- Switched Graphics fog stripping from Automatic to Custom and retained only
  the used Exponential Squared mode. Added an EditMode build-contract test for
  those serialized settings.

Verification:

- The EditMode test assembly compiled with zero warnings or errors. During the
  requested rebuild, the shader compiled four internal D3D11 programs instead
  of the previous two, confirming that the fogged variant was retained.
- The Windows rebuild was stopped at the user's request so they can perform
  the final player build manually; no completed build is claimed here.

## 2026-08-02 — Apartment ambience and guarded music fades

- Raised both synchronized Home refrigerator layers by exactly `4 dB` while
  preserving their co-located equal-power door crossfade.
- Added a fifth spatial Home detail source at the bathroom tube. Every one of
  the seven applied visual flicker edges now triggers one deterministic
  `55 ms` electrical crackle; unchanged factors do not retrigger it.
- Added the optional `Resources/Audio/HomeMusic/home_theme` slot and
  `HomeMusicPlayer`. The track fades in indoors, fades out to a real pause in
  the fixed-camera Balcony zone, and resumes from the same sample on return.
- Reworked shared scene music around an unscaled smooth one-second envelope.
  Streaming clips wait for loaded audio data before playback; Single scene
  loads hold destination activation until outgoing music reaches silence.
  Missing, failed or disabled players complete safely, and a bounded fallback
  prevents the activation gate from deadlocking.
- Added deterministic envelope, preserved-sample, never-started-source,
  camera-boundary, flicker-edge, root-binding and real scene-transition gate
  coverage. Updated audio placement notes, architecture facts and player-facing
  release notes.

Verification:

- Runtime, EditMode-test and PlayMode-test C# projects compiled with zero
  warnings or errors.
- Focused synthesis EditMode tests passed `4/4`; focused audio/Home PlayMode
  tests passed `12/12`; the final never-started-source plus City transition
  regression run passed `4/4`.
- The final complete EditMode suite passed `698/698`. The final complete
  PlayMode suite passed all `137` runnable tests with `0` failures; five
  graphics-output tests remained intentionally ignored under `-nographics`.
- A fresh `StandaloneWindows64` build completed successfully at
  `156,379,088` bytes with zero build warnings. `git diff --check` passed.

## 2026-08-02 — City-biased balcony-smoking close framing

- Increased `CameraCityLookOffset` from `0.18 m` to `0.33 m`, adding
  `0.15 m` along Home-local `+X` so the close shot looks farther toward the
  reconstructed city instead of centering primarily on the hero. The target
  yaw changes from about `8.03°` to `13.12°`, an increase of about `5.09°`.
- Kept the authored close-camera position, `38°` FOV, slow harmonic drift and
  exact two-second Balcony-shot restoration unchanged.
- Tightened the framing regression: the hero resolves near `0.37` viewport X
  at `16:9` and must remain inside `0.28-0.43` across supported desktop aspect
  ratios, while a probe `1 m` farther along the city-facing direction must
  stay in frame and project to his screen-right. A semantic direction check
  also requires the close-camera forward dot with city-local `+X` to exceed
  `0.19`.

Verification:

- A fresh isolated Unity `6000.5.5f1` copy passed the focused smoking-plan
  EditMode tests (`2/2`) and the complete smoking-interaction PlayMode test
  (`1/1`), including city-biased viewport composition, drift and exact exit
  restoration.
- Runtime, EditMode-test and PlayMode-test C# assemblies compiled with zero
  warnings or errors, and `git diff --check` passed. A new
  `StandaloneWindows64` build was not repeated for this data-only framing
  adjustment; the immediately preceding smoking-camera batch built cleanly.

## 2026-08-02 — Slow balcony-smoking camera drift

- Layered a smoking-local deterministic camera drift over the existing
  quadratic Balcony-to-close-shot path instead of changing generic
  `PlayerCameraFollow`. Local X/Y/Z position amplitudes are
  `0.016 / 0.007 / 0.005 m`; pitch/yaw/roll amplitudes are
  `0.12° / 0.20° / 0.08°`.
- Each position and rotation channel combines paired low-frequency harmonics
  with periods between `13 s` and `23 s`. One presentation clock continues
  across Entering, Looping and Exiting, preventing a motion restart at phase
  boundaries.
- Reused `CameraBlend` as the drift envelope. The offset arrives with the
  existing camera push, fades back to exactly zero through the two-second exit
  and leaves the captured Balcony pose and existing FOV interpolation intact;
  there is no FOV pulse.

Verification:

- A fresh isolated Unity `6000.5.5f1` copy passed the complete EditMode suite
  (`697/697`). The first complete PlayMode run passed `132/133`; its only
  failure was the new test using `Quaternion.Angle`, which rounded the
  sub-centidegree drift to `0°`. After replacing that assertion with a stable
  small-angle calculation, the focused smoking test passed (`1/1`) and the
  complete PlayMode rerun passed (`133/133`).
- A fresh `StandaloneWindows64` player build completed successfully at
  `156,367,888` bytes with zero build warnings. The C# runtime and affected
  test assemblies also compiled without warnings, and `git diff --check`
  passed.

## 2026-08-02 — Balcony-smoking plane, facing and idle-handoff correction

- Corrected the final Balcony-shot orientation without changing the physical
  `+X` city-facing player root. The smoking definition now opts out of the
  shared/default texture mirror with `TextureFlipX = false`, matching the
  projected handedness of the actual Balcony view; the bed/default contract
  remains mirrored and keeps its existing presentation.
- Split billboard plane alignment from texture handedness in the shared
  animated-interaction definition. Smoking now sets
  `AlignBillboardToCameraPlane = false`, preserving world up and rotating only
  around yaw, so the standing silhouette and feet no longer lean with the
  pitched close camera. The default remains exact camera-plane alignment for
  the bed, where the reclining silhouette must avoid fixed-shot foreshortening.
- Rebuilt the atlas handoff around the ordinary directional rig. Frames `000`
  and `063` now match the `PlayerDirectionalAtlas` right-direction idle
  pixel-for-pixel at the same hip/foot pivot. Frames `001-007` use a
  deterministic `8 x 8` Bayer/RGB bridge into the generated smoking art,
  frame `008` is fully authored smoking art, and frames `058-062` reverse the
  bridge before the exact final idle. The authored smoking silhouette was
  also normalized to the ordinary side-view proportions.
- Added an edge-only `0.35 s` visual crossfade to the reusable animated
  interaction definition. On entry the ordinary nine-part rig fades out as
  the smoking atlas fades in; the final `0.35 s` of exit reverses the same
  handoff. Dynamic and contact shadows remain disabled for the complete
  active interaction because neither supports the alpha blend, then restore
  from their captured states only when completion returns control.

Verification:

- The in-memory extractor validation passed all 64 frames, exact ordinary-idle
  endpoints, orientation, pivot and bounded handoff-step checks. The corrected
  extracted-frame pixel SHA-256 is
  `AECBD7E0486EE89042A58C6BF7D0A561E4311C5AF23F5FD340FCD5BCF64E1C65`.
- The in-memory atlas validation passed all 64 RGBA `128 x 96` sources and the
  `8 x 8` layout. The corrected atlas-pixel SHA-256 is
  `90AA87008702C81A41259B4D60E3D9912BD4E42E23DE247A9EA2CDA16CC131A5`.
- A fresh isolated Unity `6000.5.5f1` copy after the world-up correction
  passed the complete EditMode suite (`695/695`) and complete PlayMode suite
  (`133/133`). The smoking PlayMode contract now verifies a materially pitched
  close camera, world-vertical presentation and the animated feet remaining
  within `0.01 m` of the authored Balcony dock contact.
- A fresh `StandaloneWindows64` player build completed successfully at
  `156,365,840` bytes with zero build warnings. Final extractor/atlas
  validation and `git diff --check` also passed.

## 2026-08-02 — Melancholic balcony-smoking vignette

- Added one reachable interaction point around Home-local
  `(6.60, 0.12, -1.45)`. The first `E` docks and locks the hero facing the
  city along `+X`; the view handedness and upright presentation are resolved
  by the corrective follow-up above.
- Added a dedicated 64-frame, point-filtered sequence: 24 slow cigarette-draw,
  lighter and first-drag enter frames, a 24-frame rest/drag/breath-hold/side-
  exhale loop with deliberate pauses for a `9.5 s` cycle, and 16 discard and
  idle-handoff exit frames. The retained generated/keyed sources and strict
  atlas builder provide a reproducible `8 x 8`, `1024 x 768` runtime atlas.
- The second `E` is accepted immediately but waits for a calm loop boundary
  before starting the exit, avoiding a cut during the raised-hand drag or
  active exhale. Modal input, rig and shadows restore through the existing
  animated-interaction cleanup contract.
- Added a brief hold and smooth quadratic camera push to a close `38°` FOV,
  followed by a two-second eased restoration to the captured Balcony shot.
- Added the separate optional
  `Assets/Resources/Audio/SmokingMusic/smoking_theme` slot. It restarts at
  zero gain, fades in over `3.2 s`, loops through the shared `Music` group and
  fades out with the exit; the vignette remains silent-safe until the user
  places an OGG, WAV or MP3 file in that folder.
- Added deterministic plan/timeline/asset coverage and PlayMode coverage for
  modal entry, queued safe-frame exit, camera/music envelopes and complete
  restoration.

Initial implementation verification before the corrective pass:

- Strict atlas validation passed for all 64 RGBA `128 x 96` sources, the
  shared `(64, 40)` Unity hip pivot and the `8 x 8` lower-row-first layout;
  the validated atlas-pixel SHA-256 is
  `B29D7C5963AC1DEBC89BF933DE119EF6FFE472BC8502393DF22C0FDE325B18EE`.
- The generated loop was normalized before final packing: logical frames
  `047 -> 024` are pixel-identical at the held rest bridge, while the
  `031 -> 032` mouth-pose join has only `0.03085` alpha XOR. The retained
  profile family used the then-current generated proportions; the corrective
  pass above replaced its edge handoff and smoking-specific flip contract.
- An isolated Unity `6000.5.5f1` verification copy passed the complete
  EditMode suite (`693/693`) and the complete PlayMode suite (`133/133`),
  including the then-current smoking lifecycle and city-facing projection,
  optional audio source and restoration checks. The final foot-pivot assertion
  also passed in a focused EditMode rerun (`2/2`).
- A clean `StandaloneWindows64` player build completed successfully at
  `151,678,544` bytes with zero build warnings. Final localization JSON
  parsing, Unity GUID uniqueness and `git diff --check` also passed.

## 2026-08-02 — City-parity view from the Home balcony

- Removed the balcony view's separate dark exterior recipe. City and Home now
  share one deterministic Lit palette for ground, roads, building masses,
  roofs and window states, plus one passive bar-facade builder that preserves
  the neighboring bar's door, frame, canopy, bracket and landmark without
  adding an entrance trigger or collision to Home.
- Added a balcony-only exterior atmosphere controller. The Balcony shot uses
  City's exact exponential-squared fog, fog-colored background, `48 m` camera
  cap, moonlight, reflection level and post-process values, plus one seeded
  36-particle fog field and the retained bounded street/bar light pool.
  MainRoom, Bathroom, component disable and destruction restore the captured
  Home fog, camera and lighting state and deactivate every exterior light and
  halo.
- Kept the reconstructed exterior visual-only: it still creates no second
  City root, player, camera, listener, gameplay entrance or collider. Nearby
  district public places now use the same ordinary Lit material as City while
  retaining collider-free Home presentation.
- Extended the Home balcony regression to cover the exact City fog, grade,
  moonlight and reflection contract, exterior-light activation and cleanup,
  shared Lit materials, passive neighboring-bar identity and indoor-state
  restoration.

Verification:

- The focused Home balcony PlayMode regression passed `1/1` after the final
  lighting lifecycle changes.
- A temporary GPU-backed `1280 x 720` sRGB capture test passed `1/1`; manual
  review confirmed the expected gray-green distance haze, illuminated facade
  masses and neighboring bar light. The temporary test was removed afterward.
- The complete EditMode suite passed `685/685`.
- The complete GPU-backed PlayMode suite passed `128/128`.
- A Windows build of all six configured scenes succeeded at `148,517,792`
  bytes with zero warnings.
- Runtime, Editor, EditModeTests and PlayModeTests `.csproj` builds each passed
  with zero warnings and zero errors.
- `git diff --check` passed.

## 2026-08-02 — Grounded last-route island dressing

- Removed all eight emissive magenta/cyan recipe pieces from Nightlife's
  last-route island: five repeated canopy strips, both totem halves and the
  single departure-board line. The broken canopy ring and open traversal
  grammar remain unchanged.
- Grounded the floating departure board with two visible posts and feet that
  meet both the island paving and the board shell.
- Replaced the neon repetition with two weathered canopy route plates, layered
  paper posters on the totem, three faded schedule rows, a waste bin, two
  bottles, a discarded timetable and one lost scarf. Only the bin adds a new
  intentional obstacle collider; public approaches stay open.
- Extended the City presentation regression to reject emissive island
  materials and removed part names, prove both board supports meet their
  surfaces and require the new grounded details.

Verification:

- `dotnet build BarPromenade.Runtime.csproj -nologo` passed with zero warnings
  and zero errors.
- `dotnet build BarPromenade.PlayModeTests.csproj -nologo` passed with zero
  warnings and zero errors.
- `CityNightPresentationPlayModeTests` passed `3/3`, including the new
  no-emission, grounded-support and open-approach regression coverage.
- `HomeBalconyPresentationPlayModeTests` passed `1/1`, confirming that the
  shared last-route recipe still composes correctly in the apartment exterior
  view.
- GPU visual review of `Logs/CityLastRouteIsland.png` confirmed that the old
  board is visibly supported, all cyan/magenta bars are absent and the dull
  replacement dressing reads in the live City fog and lighting.
- `git diff --check` passed.

## 2026-08-02 — Flickering bathroom spill in the Home main shot

- Replaced the isolated warm apartment-exit accent with a cold hard-shadow
  ForcePixel Spot staged just inside the bathroom threshold and aimed through
  the solid ajar door toward the existing exit area. The Home atmosphere still
  owns at most four local realtime lights and all three fixed camera poses,
  room geometry and door materials remain unchanged.
- Added one deterministic unscaled `6.4 s` fluorescent-failure cycle. The
  bathroom point pool and doorway spill stay steady for most of the cycle,
  then share one brief irregular series of deep dips.
- Connected the visible HDR tube and depth-tested halo to the same factor
  through a dedicated fixture component. The emitter uses one reused material
  property block and keeps the shared emissive material; the halo only hides
  during the deepest dip.
- Updated the focused atmosphere and complete Home-presentation regressions to
  cover source placement inside the bathroom, cold hard-shadow direction,
  bounded light count, deterministic timing and fixture wiring.

Verification:

- Runtime, Editor, EditModeTests and PlayModeTests assemblies compiled in
  Unity with no compiler errors or warnings.
- The complete filtered Home PlayMode set passed `28/28` on the final code.
- A temporary GPU-backed `1280 x 720` capture test passed `1/1`; manual review
  of the real MainRoom camera confirmed a bounded cold pool across the entry
  floor, a matching cold bathroom threshold and no whole-room overexposure.
  The temporary capture test was removed after verification.
- `git diff --check` passed.

## 2026-08-01 — Home player visibility through foreground objects

- Added one explicit Home occlusion registry populated by the runtime world,
  dressing, bathroom and balcony builders. Logical furniture, decoration,
  door and visible rail groups own stable IDs, kinds, renderer membership and
  authored minimum visibility, while the room shell, glass, lights and safety
  colliders remain outside the presentation system. The tall box on the sofa
  joins the sofa group, and the alarm-clock nightstand plus opaque clock shell
  form their own group while the emissive digits remain untouched.
- Corrected multi-object registration to accumulate renderers from every
  supplied source instead of retaining only the last source, with a dedicated
  regression proving that all parts of a composite object stay together.
- Added a pure bounds resolver with five camera-plane player samples. Rays to
  the head, left/right chest and pelvis protect the readable body; the feet
  sample remains diagnostic so low foreground objects may preserve natural
  depth.
- Added a Home-owned controller that fades an entire blocking group through
  one shared opaque alpha-clip dither material. It uses a `0.15 s` fade-out,
  `0.12 s` clear hold and `0.30 s` restoration, preserves existing property
  block colors and never changes colliders or GameObject state.
- Kept the replacement material compatible with the active PC Forward+
  renderer: clustered additional lights, cookies, light layers and reflection
  probes remain available, and clipped shadow, depth and depth-normal passes
  keep the fade coherent with shadows and SSAO.
- Opening, refrigerator and animated Home interactions suspend the cutaway and
  restore full opacity. Controller cleanup restores the original shared
  materials.
- Added registry/resolver contracts, a synthetic grouped controller lifecycle
  regression, a GPU coverage check for the dither shader and a balcony-scene
  presentation regression.

Verification:

- Focused Home occlusion EditMode checks passed, including the `12/12`
  registry contract suite; the complete EditMode suite passed `685/685`.
- Focused controller/GPU checks passed `3/3`, including real dither coverage
  and clustered Forward+ additional-light rendering; all Home PlayMode checks
  passed `27/27` and the complete PlayMode suite passed `128/128`.
- Runtime, Editor, EditModeTests and PlayModeTests assemblies built with zero
  compiler warnings and errors.
- Windows x64 player build succeeded at `148,501,936` bytes. Its one warning is
  the package-owned `Hidden/Core/DebugOccluder` vector-truncation warning, not
  a project shader warning.

## 2026-08-01 — First-class open district points of interest

- Replaced the temporary four facade POIs with a canonical layout-owned public
  land use. After bars, the player home and primary landmark cells are fixed,
  the generator selects at most one separate street-connected lot per urban
  district by access count, primary-landmark separation and a stable seeded
  rank. The default city provides all four. Authored sites require both lot
  dimensions to meet `MinimumDistrictPointLotDimension` (`18 m`); smaller
  custom blocks omit all four safely, while eligible compact layouts omit only
  a district with no safe candidate.
- Added stable public-place and access descriptors for Old Town's waterworks
  court, Residential's drying yard, Industrial's weighbridge and Nightlife's
  last-route island. A public lot contains no building, bar, home or primary
  landmark. Its full ground and street approaches enter the walkable mask,
  every adjacent street side becomes a complete fence opening, and lamp/signal
  planning keeps both the ground and approaches clear.
- Added a dedicated physical world builder. The four places use distinct
  free-standing forms and movement grammars—asymmetric basin and standpipe,
  parallel drying frames, axial weighbridge and broken-ring transit island—with
  deliberate surface/obstacle colliders instead of collider-free facade props.
  The bounded Home exterior reconstructs nearby sites from the same canonical
  descriptors without gameplay colliders.
- Returned the ordinary decoration catalog to its original 24 families and
  four primary urban landmarks. Decoration planning now excludes public lots
  naturally because they have no building.
- Rewired the city map to consume `CityLayout.DistrictPointsOfInterest`
  directly, render each public lot as open ground and show a distinct marker
  shape plus localized RU/EN name for each kind. POIs remain informational and
  do not enter route selection, pathfinding or visited-bar progress.
- Added deterministic EditMode and PlayMode coverage for reservations,
  validation, walkable approaches, complete fence openings, fixture clearance,
  world/Home construction and canonical map integration.

Verification:

- A fresh isolated Unity import and compilation completed successfully.
- Full Unity EditMode passed `668/668`.
- Full Unity PlayMode passed `125/125`.
- Windows x64 Player build succeeded at `141.5 MB`.
- `git diff --check` passed.
- A graphical or manual camera review was not run in this verification pass.

## 2026-08-01 — City zone art-direction bible

- Added a current-versus-target art bible for Old Town, Residential,
  Industrial, Nightlife and Central Park.
- Locked each zone's emotional role, spatial and facade grammar, material
  aging, light, sound, human traces, bar threshold and explicit anti-goals.
- Defined one-block visual transition bands, shared city constants,
  determinism rules, implementation slices and objective recognition checks.
- Kept the current topology, localization names, bar activity assignment,
  global noir presentation and runtime contracts unchanged.

Verification:

- Documentation-only change; reviewed against the current district generator,
  decoration plan, world builder, map localization and project memory.

## 2026-08-01 — Seeded city silhouettes, landmarks and street details

- Added a pure, version-independent city-decoration plan with stable IDs,
  independent hash salts, explicit anchor/palette/visibility contracts and a
  hard `420`-descriptor cap. Every ordinary building receives one district
  visual; the four urban districts receive one landmark each and Central Park
  receives a fountain/statue plus bandstand.
- Implemented 24 low-poly recipe families spanning rooftop silhouettes,
  facade depth, frontage stories, common roadside furniture and park features.
  Windows and facade details now use the lot's real road frontage instead of a
  fixed world direction, while ordinary facade tint keeps district color at
  night.
- Expanded static details through one dedicated builder into at most six
  shared-material batches per `48 m` chunk. The layer adds no colliders,
  realtime lights, audio sources, particles or shadows; per-kind footprints
  protect entrances, gates and existing night fixtures, and narrow frontage
  recipes stay inside the real street/building pocket.
- Reused the same seeded descriptors and recipes in the bounded Home balcony
  exterior after Home-local conversion and half-space clipping. Removed the
  superseded ordinary-lot district detail call so legacy planters, vents and
  signs cannot overlap the new compositions.
- Added pure coverage for determinism, seed variation, all 24 kinds, ordinary
  lot and landmark quotas, stable finite data and protected clearances. Added
  City/Home scene contracts for batching, shared materials and the visual-only
  component budget.

Verification:

- Runtime, EditModeTests and PlayModeTests generated projects compile with
  0 errors and 0 warnings; `git diff --check` passes.
- Focused decoration EditMode passed 6/6; focused City presentation passed 3/3
  and Home balcony presentation passed 1/1.
- Complete EditMode passed 649/649 and complete PlayMode passed 125/125.
- Windows x64 Player build finished successfully with no build-warning markers.
- A temporary D3D11 RenderTexture smoke captured and visually inspected all
  four urban landmarks plus a street market, bus shelter and park fountain;
  the temporary capture test was removed after verification.

## 2026-08-01 — Readable apartment exit lighting

- Added one separate warm, shadowless ForcePixel Spot named
  `Home Exit Door Light`, aimed at the existing stairwell door so it reads on
  the right side of the ordinary MainRoom shot. The two practical lights and
  cold shadowed window-cookie Spot remain; `HomeInteriorAtmosphere` now owns at
  most four local realtime lights, while the scene Directional light remains
  separate.
- Kept the three existing MainRoom, Bathroom and Balcony camera poses intact.
  The door geometry and material are also unchanged.
- Added PlayMode coverage for the door light's type, placement, direction,
  warm color, range, shadowless ForcePixel setup and atmosphere-owned light
  budget, plus presentation checks that it reaches and points at the door.

Verification:

- `BarPromenade.PlayModeTests.csproj` compiles with 0 errors and 0 warnings.
- Focused Home atmosphere and presentation PlayMode checks passed 2/2 and 1/1.
- The full EditMode suite passed 643/643 and the full PlayMode suite passed
  125/125 under D3D11.
- A focused D3D11 visual-capture PlayMode check passed 1/1 and confirmed that
  the added light makes the unchanged door readable in the unchanged MainRoom
  composition.

---

Earlier entries: [`ai/archive/work-log-2026-07.md`](archive/work-log-2026-07.md).
