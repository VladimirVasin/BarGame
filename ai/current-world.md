# Current world and gameplay catalogue

[Overview](project-overview.md), [systems](systems-map.md),
[decisions](architecture-notes.md).

Loading (`2026-09-06`,art§15a):static painting/directed City↔MountainRoad↔Village
edge/bar;direct map:last leg;Village warm/inhabited;text/portrait/spoiler/newfact0.

## Implemented MVP

Runtime-composed coastal City/MountainRoad/Village/cableway/upper-house;
modular low-poly 3D hero/streets-climb-lane. Bar/shop/home/church north of
cemetery:own interiors/returns.


### Areas and scenes

- Six named winter neighbours greet hero. `VillageHouseholdCatalog`:
  31 house/families;occupied:`04` Veidra,`08` Drauven,`11` Kersna,mother;
  other windows dark;hero/mother unnamed. Station worker checks cargo lid.
  At `04`:six logs,two baskets to separate supports,tend/rest. `08`/`11`:
  shovel/support,closed basket/rests,hinged gate/fence returns. Shovel:
  pick up/three strokes/return;visitor carries basket to `08` and back through
  gate. Repair/sewing neighbours visit yards/work in `08`'s accessible lower
  room,visible through two windows.
  Chair repair finite;sewing:sit/unpack/work/fold/closed-box stow/stand.
  `VillageNeighbourhoodPlan`:≤4 outside,outings `05:00–18:30`,
  completed before evening return. Moving doors/L-shaped solid vestibules
  at `04/08/11` hide active bodies. Gusts pause movement/waiting for `2.2 s`
  without releasing grips. Neighbours yield/sidestep; pause freezes life.
  `VillageLife` covers bodies/contacts/passages and evening/morning transitions.
  Hero opens/closes `08`, enters, sits and holds the chair rail for the joint.
  Indoors: no cold/snow/fog, muffled outdoor sound, storm through windows.
  Room/hallway fixed views avoid shoulder close-ups. Outdoor help:
  lid/gate/baskets/shovel;hold a moving
  partner's prop, carry full baskets freely between supports, or clear one of
  three patches and return the shovel. Production-rig torso actions retain free
  carrying movement. Residents fetch spring water and
  shovel the chapel/station edge. Finite logs/deliveries/repair/cleared snow/water
  work persist in `GameSessionState.VillageHousehold` across visits, reset on
  New Game; no disk save.

- `MothersHouseInterior`: summit-house door returns to the same safe threshold.
  Imported modest, clean, cared-for old home: central tea table, north rocker/
  fireplace, west sofa; mother sits in the rocker. Real stairs lead to the
  furnished parents' bedroom, disused childhood room and passive bathroom.
  Shared opening table matches all seventeen windows to the exterior; runtime
  collision follows slabs/partitions/openings, one hidden ramp serves visible steps.
  Five fixed height-aware cameras cover ground/stair/corridor/rooms; bathroom
  cutaway affects rendering only. Geometry, furnishings, dimensions, camera/light
  bounds and texture contracts: art bible §10g and `architecture-notes.md`.
  Unique 4K UV0/2K response plus auxiliary 1K atlas use shared house materials,
  BC7+mips ≤30 MiB (28 MiB estimate, unmeasured). Kettle Hat's ten renderers are
  the explicit atlas exception. Hearth/window/practical lighting, finite bounce,
  crackle, muffled wind/ticks/timber and one repaired listener remain scoped.
  Free sofa cushion has no patched throw; rocker/sitter share measured contact.
  Scarf uses shared `WorldItemPickup`/found screen: confirm commits once, source
  stays empty across visits that session; other upstairs furnishings are passive.
  Mother uses shared attention/speech bubble: same door greeting, shuffled
  domestic lines every `35–55 s` within `6 m` on her floor, E from the same bag.
  Ten lines plus scarf request; separate first request raises quest, wearing
  retires request, reload keeps bag. Empty hands/silent hero/no choice;
  Cat/dinner/news remain outside this MVP.
- `MainMenu`:index0/EditorPlay;NewGame/Combat/OldArena/Quit;
  Time first (default07:40), then12starts:9story,City docks-cannery,lodge;village default.
  Mouse/keyboard; existing pad kept. Back retains time; closed Bar shows hours/no commit.
  `NewGameStartService`:day1/selected time;areas AreaLoading/6interiors direct;
  no door-prologue-ride-cabin-arrival-travelart.
- Village:lane foot/2m above stationthreshold→mother;Lodge:3m beforeentry→door.
  Else rootspawn;City home-return/docks-cannery pedestrian-clear.
  City-Bar-lodge arrival1x/laterspawns kept;day1 mother quest-mapmarker;
  door-map-start inside closes/no village signpost;
- CombatTest:3floors/9rooms/4flights:L3gap/R3only/fanwell/9sets-signs;ground:A*/partial/gate;
  Midforms/4K+2Kresp/uniqueUV/metrewearjoins/clones0.
  valve/PusherL;nokeyE2pulls1.2s;keykept→gCOLchainsettle;holdE6s/3turns/leafCOL3.05m/creak;release-pause-Esckeep/maxlatch→heavySFX1@valve/activityexittail/fullstop0.
  Home↔gate/E/back-travel-speech0/stub/gallery2/upper;fridges3/labels-scraps/Take1x/fullkeep/redfade-humclick.
  3src→flickercrackle/mech-music-foes-corpses-spawn0;fan0:6cables snapvalvewell/windcreak.
  toprear/playfill<bulb1.8;pourcans0→flatphys/drag-turn/seams→spasmSFX/die-key1/receipt;keyUIend+stairs↓:heat2s/riseSFX→popglass8phys.
  bulb0/socketcord/restL-lens-amb0/gateRedsoftflicker>0+chest/silent;Escpause/Reset-Newkeep/newgame-inv0.
  D7fan/redshadows/deckblock/guards;M65Spot+weakbodyPoint/bounce/look/black12m/fog-sky0/warmhalo.
  Dust192+32/fan→settle/seed.
  SFX/dryUI/theme.8/echo-wow-LP-vol/mechsync/stop/noreplay/exitfade.
  Fan0:2mono3D/clicks-whisper/.3/.15/.8/LP2800/Worldwet;EOF12–25|25–50/startsame;newfixed3–7m;pause-ownerhold/ResetNewkeep/empty0/clear;§6/art15a.
  StdDocker;Endless1–5/def3:Room,Docker,Crane(last≤2);seedstart-reset-refill/≤5warm/≤2corpses/roleIDs.
  Lab:Arena/6warm/1target/Zombie/reset-prepkept.
  Room×1.2/bighead/longarms-hands/corridor;Lab:Imprint cast/LongExposure emulsion;Room:head0→ret+2s;lens1s/chest-seatface2s/ret18HP/laugh5s→idle5s;twin-chair/IK/rootkept;simlive/input0/others idle→headend;jelly/Lmidbar in/out;glugs/pull;§6.
  HP/structure×.5;head50/torso12.5/hero1;wounds↑/wood1sthit hole-chips.
  Crane:flesh/carapace/6cables/3floors/6m/up2m-chest/LOS/1hit;
  hook5s/COLhoist-haul overrails/struggle/bodycam/fallHP/inertia;
  others+spawns idle→release/cancel;
  escape6freshE/bar/held0;success→actualhome→watch10;
  fatalcry-spasm3→home→regen10/full/sameactor/kill0.
  Frontcrowbar fullblock/back0;sharedblockfront dot≥.35.
  Hero:wounds/HP,no head-limb-bodydetach globally.
  pendulum/voidCOL;pause-modal-focus/reset-disable-unload;art§15a.
  18anchors/shared COL/occupied0decor;
  2.7m/samefloor/LOS|hit→rig.65s/limbs-head breakwood;
  Awakefocus/freeinput;unseenrefill3–7s/≥3m/freepath/0backlog;
  reset:Endless unseen/Std initial. Audio14/2voices/≤2groans/SfxWorld/wood-wet sync;
  Kill1x/heroend;injury-action-ammo-flights kept;
  unused→oldest/excess retire;
  bodydrop0/focus≠hits/Tab-E0.
  Hero:All3/alivehold/deathdrop-pickup;draw grip-ammo resume.
  Docker:30slats40HP→holes/fused head-woodram;8fish/1skin/anyfallspill/finiteasync;
  1.7m/s/plantbursts;ram.9m(.25/.18/2.4)/kneel-rise;ownfall→5sFrenzy/2SFX/Recovery0;
  dmg32/guard28/impulse85-block26/firstsurface1hit/guard-counter-retreat0;
  ownDebris0CC/resize-reset/floor;ground0attack/infection0;freecam/dummy/Tabfight/MMBfocus;
  Rise:tread/pose/retry;guns8gaits/0run/Step1m4way-back/noviceaim/§15a;
  Pistol8+0≤48/slideempty/mag-rack/cases;shotgun2+0≤16/breakload/atomic;winfire/partreload/physspend/empty0;
  Ammo12(6mag×8/6shell×4)/seed9banks/3bays/both/flr;legacy4;3room+6wall/27soloE/fridgecurve/hollowCOL/std/full0/part1/Take>open>close/resetshut;
  Endless12s/pause-modal-focus-off-defeat0/resetrespawnkept/winlive.
  Nearlaunch/falloff/1react/head≤14/firstsurface/0pierce-ricochet-guard.
  Enemies:cloth-tissue-bone loss/sever/torso→skeleton/0chunks;Rhand0weapon/Lhand0twohand-reload;Rbar-loadedpistol.
  Legloss fall/crawl/0rise-step-kick;critical head-torso end/reset. Remains:passable/rigfootlift/floor-walls/poolreset.
  Bar:bat/hook→/shoulder-block/charge/free/8seg/1hit/recoil/6zones;taunt/Eshared/0grant.
  Cloth:rig-anchors/hand-opening/physics0/hair;pause-resetclean.
  HUD:art§15a
- legacy-only Home opening: held bed loop/silent `05:59`/rare whole-clock flicker,
  five-second input lock/localized Wake Up-Quit. Wake sets `06:00`, starts
  clock/alarm, hides menu; alarm stops after three unscaled seconds.
  Wake `6.9 s` (`6 s` exit at `1.15`): `2.25 s` camera glide→gameplay easing;
- Home F9: reversible apartment day `1–7` and City-at-home/map.
  Opening/contextual/door/modal owners block it;
  map button retains the one-shot arrival request consumed after transition;
- one session clock starts at the selected time (default `07:40`)/day `1` when an available starting place is confirmed
  and persists through Single-mode scene loads.
  The retained Home opening still holds it frozen at `05:59` until its Wake or
  the accepted Home debug city-map skip sets it to `06:00`. It advances on
  unpaused real time at
  `0.5` game minute per real second, so a full `24 h` cycle takes exactly
  `2880` real seconds (`48` minutes), crosses midnight with a zero-based day
  index exposed to the player as one-based `DAY N`. True pause stops it;
  intoxication's world slowdown does not. The Home clock shows `HH:MM`;
  the inventory Status panel shows `DAY N · HH:MM`, while one persistent view
  briefly announces day 1 after Wake and every later midnight;
- a separately runtime-composed `MountainRoad` area. The hero arrives `6 m`
  inside a `9 m` physical/walkable tunnel. Its open `72 m` visual tail bends
  after `12 m`, hiding the end without a wall or added navigation; materials,
  light/fog and return arrival stay. The continuous `620 m` uphill road
  ribbon is dimensioned for the `4.83 x 1.80 m` LastRouteCar: `4.8 m` wide on
  ordinary stretches and `6.4 m` through ten `7.5 m`-radius hairpins. It rises
  `26.1 m` at no more than an `8%` grade. After five lower hairpins the
  mandatory route crosses a `50 m`-long high mountain bridge whose `5.8 m`
  deck surrounds the `4.8 m` clear roadway; the terrain opens to a gorge floor
  at world `Y=-16`, at least `25 m` below both bridge ends. Its last `25 m`
  are level - a `20 m` terrace run that carries the road out of the switchback
  field before the `5 m` entry lead - and share the actual road-mesh entry
  vertices with the same irregular roughly `42 x 27 m` terminal plateau, so
  there is no height step, open seam or transverse collider wall. The terrace
  run keeps the raised pad off the climbing road: parked any closer, its rim
  reached over the outer arc of the ninth switchback and buried it in snow. A separate dark asphalt mesh, offset `0.025 m`
  above that shared physical surface and extended `0.45 m` back over the road
  seam, makes the whole entry and R`7.5 m` turning pocket visible without
  adding a second collider. The terrain envelope keeps a `76 m` margin.
  Generic middle/far ridges sit around the outer perimeter of the complete
  route bounds, ground their bases against the minimum sampled terrain beneath
  each oriented footprint and reserve those footprints from both the road
  corridor and tree crowns. The terminal has no cafe, cafe threshold or counter
  seat; the brink bench, LastRoute apron and station remain. The right
  side owns an operating `230 m` cableway with nine supports and eight cabins,
  with every cable height rebased from the raised terminal rather than old
  absolute world heights. Imported crafted profiles replace existing drive,
  cabin and support slots through shared world-owned meshes, keeping their
  envelopes/collision/pivots. Tunnel stone joints and bridge/terminal profiles
  stay outside the established openings and road. At the normal `2.6 m/s`
  walk speed the route takes about `238.5 s`, or `3 min 58 s`; continuous
  `4.2 m/s` run input takes about `148 s`, or `2 min 28 s`. The Ferryman's
  Last Route car reads and drives the same route. On the terminal apron he waits for the
  passenger to get out, then alights and returns to the bonnet. The return leg
  is armed after the passenger's exit; a fresh boarding can then take him back
  to the wheel. Layered forest retains physical/mid/far budgets and three crown
  silhouettes. Shared road/village crowns close below: no sky or cut trunk tip
  shows through at close range; outer sizes, materials and wind stay unchanged.
  It yields locally at three measured hairpins, the bridge and the terminal
  approach; surrounding far trees and both ridge rings keep those openings on
  the climbed road rather than turning them into extra vistas. Natural debris
  gathers into five unequal chapters with deliberate pauses at the same
  structural beats, and a bounded deterministic resolver preserves `0.35 m`
  between conservative footprints and existing roadside furniture. The
  first deliberate misc migration replaces the visible runtime-box geometry
  of `FallenLog`, `Stump`, `DeadTree`, `GuardRail`, `SnowPole`,
  `ConvexMirror`, `UtilityCabinet` and `AbandonedChair`: `102` plan placements
  select `19` deterministic Blender meshes and arrive as `12` combined
  renderers, while transforms, semantic IDs and simple collision proxies stay
  plan-owned. Boulders, culvert, utility cable and tunnel lamp remain on their
  previous builders. Five
  causal positional sound anchors belong to identifiable visible sources,
  including one loose bridge rail; one tunnel practical visibly flickers. Its
  root may generate the pure City layout/mountain plan needed by the City map
  tab, but it never calls a City world builder or creates City GameObjects;
- `AlpineVillage`: plans/validators/builders. Step-free lane: `6.4 m` over
  `82.1 m` (`7.8%`, max `8.3%`) to mother. 12 lane houses, chapel/source/
  arrival axis; no adit/graves. `CoreTerrainBounds` retains core;
  `AlpineVillageExpansionPlan`: forest/base/old-road loop/brook bridge.
  `AlpineVillageAbandonmentPlan`: 18 households (14 standing/3 ruins/foundation),
  8 sheds (2 ruined), 5 closed yards: town hall/square, school, shop-bakery/loading,
  workshop, rescue/apron. 3 worn stone/joinery/dark-window/shutter/debris
  variants; no new residents/lights/interiors. Clear yards, level shelves,
  four-side views. Dry `18 x 12 m` lodge (§10g): wall-headed bunks/cot,
  rug/kettle/table/two three-seat benches/lamp. Beams/cloth clear masonry/mattress;
  wall chest/clear cot; front-left hatch/props, sill/path.
  `VillageSurfaceAppearance`: local maps/seven lodge woods; unique4K/2K/UV1.
  Contacts/snow/ridge deformation stay.
  BC7≤96MiB includes pictures/junctions; shared donors stay.
  Hatch E: centred/oblique/no hero, Yes/No; lift/stop/shut→bottom reply;
  E/Esc ends/repeat, No restores. `LodgeInteriorInteractions`: shared RU/EN
  E seats/photo/skis. Group photo: close-up/text→Next→shared Take/Cancel;
  Take once/Esc exits; frame gone/owned until New Game. Cot/kettle: silent E stubs.
  Doors: both-side E/binary collision, no occupied closure/auto-close/hero animation.
  Warm lamp/no fuel/heat; doors/light persist, New Game open/off.
  `VillageInteriorAcoustics`: wind softer/near silent with one/both shut in .35 s;
  stove/steps/outside clear. Lit stove: shut doors warm room, else indoor 2.5 m;
  walls block heat. Roof culls snow/fixed door targets. Lodge/mother piles:
  E→Yes/No→shared `FirewoodLog`, one/retake; Cancel none/Close retains.
  Stove E: first person/right two-column grid; lit opens door. Silent:
  «Мне понадобится полено»/«Теперь нужно зажечь». Spent log/flip-top opens→third
  click ignites→closes/soft crackle. Exit: camera restored/door shut.
  Empty/LogPlaced/Burning until New Game; no burnout, passive shed/lift.
  Avalanche scar/full-size timber/rock/beams/buried tow/cable/damaged household 15
  (`AvalancheRuinedHouse`). Front ~20 m/fan 38 m; `AlpineVillageAvalanchePlan`:
  shared 14-vertex movement/forest/manifest polygon; ground-triangle-fitted
  deposit/supported debris/snow underneath.
  Lower lift/lodge/loop/ruin accessible; no active slide. Avalanche/road collapse:
  one past catastrophe, unrelated to the older mine.
  Warehouse yard: `RustedTruck` without wheels/doors/glass, `DiscardedChairPile`,
  clear ramp/road. Stock/supports show conserved repair; rail/rubble close gap.
  `AlpineVillageNarrative*`: 24 outdoor IDs (20 props/two notes/truck/chairs).
  Work/storage/debris, no quota/road-view rule; clear approaches,
  assets/text kept. `NarrativeInteraction`/rig/
  `ContextualCameraDirector`: props side/bokeh, notes close-up/no hero.
  `InteractionPromptView`: held bottom pages/cleanup; E next/close, Esc cancel,
  replay/no progress; only minibar photo grants an item. RU/EN resort/road thoughts;
  two notes: three paper pages then thought, paper/UI keys match.
  `AlpineVillageJunctionPlan`: ports/contours/snow; editable asphalt/soil masks
  bake shared 2x2 albedo/linear-response atlases. Other paths raised; no warehouse snow island.
  `AlpineVillageDistanceWorldBuilder` uses six checkpoint meshes with storm
  materials below the gap: city/windows/glow/land/rock/vegetation.
  Cableway is the sole exit. Walkability: core/expansion minus walls/trees/cable
  cut/lodge/cliff/avalanche; ruin collision follows remains, props use MeshColliders.
  The `74°` rise/visible barriers close edges. Paths reserve compacted strips,
  thresholds/chapel-water turn; walking beyond them allowed.
  `AlpineVillageSnowDrift`: colliderless depth, separate terrain; station apron/
  cabin entrance belong to `TerrainCell`. Snow reaches `.45 m` over `1.3/3.2 m`
  loaded/scoured faces. Empty yards/ruins retain snow, paving exposed; rescue
  flagstones support pressing/refill. Paths overlap `1 m` snow by one cell;
  `AlpineVillageSnowTreading` buckets updates, buries zero-depth edges; current
  depth/treading/clearing also drives snow colour and exposed-substrate edges.
  Hero samples remaining snow/current + next step: `.20/.12 m` entry/exit,
  no sprint, `1.05/.65 m/s` forward/back; cleared paths restore ordinary motion.
  Snow foot contacts drive sound/kickup. Approaches fit planes/blend normals;
  soil under `2 m` mouths shares path texture/tint/UV, mesh edges conform.
  Ribbons end at nodes; snow follows mesh ground.
  Authored distance/side/yaw beats form frontage clusters and pauses; exact OBB
  validation, three `7.2-7.5 m` rear-row depth beats and a bounded symmetric
  local correction keep every seeded rotated footprint out of its neighbours
  and the lane without cascading the whole frontage away from the street.
  `AlpineVillageTerrainSampler` shares height between planning, validation,
  collision and map teleport. The workroom plinth cuts lying snow and clips
  final ground triangles into a shallow cavity; render sectors and collision
  use that final mesh. The planning sampler remains the pre-cavity terrain.
  Core shelves ease to the macro slope over `3.6 m`;
  expansion terrain levels buildings/yards and shapes the old-road brink. The ridge
  rises at `3.6` (`74°`) to `60 m`, beyond the hero's `45°` limit.
  `TerrainBounds` charts the expanded ground; `TerrainMeshBounds` also contains
  its physical rise, hidden crest and cableway brink. Ground uses one collision
  mesh and `48 m` render sectors, floor on the ordinary material and rise on
  `AlpineVillageRidgeAppearance`'s `CityMountainPhysical` material (village
  haze colour, breathing density, `0.40` visibility floor and a stable opaque
  colour handoff over `96-108 m`, a dark layered-stone tint, no shadow because
  the shader has no caster pass). Floor and rise share exact edge indices instead of a buried overlap
  ring, and floor/rise/lying-snow world UVs retain the existing baked metre pitch
  once under an identity `_BaseMap_ST`; neither screen-space coverage nor a
  second renderer-size scale can make the distant lower wall crawl. The
  shared shader's zero-default City path retains its existing clip dither.
  Forest uses `48 m` sectors: core cap `420`, expansion `900`, heights `13–30 m`.
  Abandoned-zone height/crown/trunk double, including old-bowl yard infill;
  inhabited-street scale stays. Crowns separate; core routes clear `7 m`, new
  routes `1.2 m + crown radius`, yards/sightlines add clearance. Station,
  landmark aperture, water and house access stay clear. Construction yields
  between terrain/paths/trees/snow/scenery; map/peripheral storm use all paths.
  The accepted `2026-09-06` art pass exposes the existing layered-stone
  surface on the rise and places measured Blender rock/snow ledges beyond
  the walkable toe. The imported pieces share the rise's stable haze and
  vertex snap; the original continuous terrain still owns collision and
  the cableway cut. Snow reaches foundations outside the clear door aprons,
  and its rise run varies along trodden edges without changing movement.
  The cableway valley shares the rise material. Overcast
  daylight uses weaker neutral direct light, grey fill and darker haze/clouds;
  windows/garlands retain the lived-in warmth. Night fill and story dimming
  remain separate. The fog breathes: `RuntimeSceneSetup.EvaluateAlpineVillageFogDensity`
  runs `0.017` between gusts (`9 %` of the mother's door at `91 m` from the
  platform) to `0.045` at a crest (`41 m` left at `3 %` — the far half of
  the lane closes for seconds and the top house vanishes, then returns) on
  a wave keyed to the raw shared gust rhythm with a `0.5 s` attack and
  `1.0 s` release, written every frame by `AlpineVillageRoot.ApplyVisibility`
  under a `110 m` far plane. The §12 dimming grade is held at `0`, but its per-minute
  apply already drives isolated garland loss, seeded window darkness, dirtying
  snow, five practicals and all six causal sounds together. The weather now
  stays severe independently of that grade: a village-only
  stretched `Blizzard` profile keeps snow in `.88–1`, the shaped shared wind
  stays in `.82–1`, and a second terrain-sampled layer carries low spindrift
  with a continuous wind bed on the same deterministic bearing and gust
  rhythm. `AlpineVillagePeripheralStormPlan` measures every point against the
  complete lane/path network and a widening station-to-house aperture that
  encloses the mother's whole footprint; its presentation field lays large
  soft curtains over the exposed side fields and closes rapidly behind the
  house. It never writes global fog and never changes collision, damage,
  speed or the walkable mask. Walking off a route raises the field's overall
  visual pressure and gathers new sheets near the player; free traversal
  remains the contract and the house's crest/trough visibility cycle is
  unchanged. The nine batched garland spans read that shaped wind through bounded
  vertex deformation: both anchors remain fixed while bulbs and the two real
  lights follow each moving midpoint. The enclosing ridge closes sight and traversal rather than sheltering
  the bowl; only the station canopy and moving cabin are locally dry.
  The exterior hero self-hugs while idle/walking/running, rubs shoulders in
  short series and moves hands between them. One-second shivers every `5–9 s`
  keep palms on opposite sleeves; running retains pose/rub clock. Gait/motor/
  speed stay shared. `PlayerColdPresentationModel` synchronizes breath and
  wind-carried `PlayerColdBreathEffect`. Open canopy stays cold; cabin/vehicle/
  transitions/hidden contextual presentation suppress the profile. Mother's
  house/lit lodge suppress cold/breath and thaw frost: whole lodge with both doors
  closed, else indoor 2.5 m. Balance/fall/nausea win; pause freezes
  body/particles, scene/presentation cleanup clears the profile.
  Equipped scarf halves shiver amplitude/new exposure: first/full frost at
  `12/86 s`, normally `6/43 s`. Hug/rub/breath/gait stay; equipment changes
  preserve accumulated ice, thaw keeps its ordinary rate.
  Session frost stays clear for `6 s` outdoors, reaching its cap at `43 s`,
  slightly after the platform-to-mother's-door walk. Thin branching grey-cream
  crystals occupy `8.5–12 %` of ordinary
  edges and up to `18 %` in corners; the central `64 %` rectangle remains clear.
  A fixed fern-crystal bitmap appears in patches with an uneven growth front.
  Blur grows only under frozen patches; clear gaps/centre stay unaffected.
  The layer follows URP post-processing and precedes the shared presentation
  composite, inside its visible image window; HUD, black bars and loading art
  remain clear. Quiet dry crackles and a thin icy ring arrive in irregular
  swells while frost grows. At the cap, cues stop and the active tail fades
  over `0.12 s`; partial thaw and outdoor return restart the normal cold delay.
  Thaw has its own soft ice-release sounds and tiny
  damp clicks, fading with the remaining frost; neither phase uses sharp impacts
  or breaking glass. Warm house/cabin/stove zones
  thaw a full layer over about `8 s`, half over `4 s`, and a light layer over
  roughly `1–2 s`, while body cold and breath are already off. Thaw duration
  follows the remaining exposure. Pausing and scene/loading transitions freeze
  exposure and its clock; the first ready frame discards the loading delta.
  Re-entry resumes the remaining level. A new game or unrelated gameplay scene
  resets it. The real-controller walk reaches the door after `93.9022 m`/
  `36.1163 s` with frost `0.90904`; indoor renders verify full/half/clear layers.
  This is level-`0` weather, independent of the village's dimming grade; see
  the accepted `2026-09-07` exception and its explicit `2026-09-08`
  refinements in `architecture-notes.md`; it adds no health, damage or meter.
  Particle alpha stays capped, and the haze wave is the one thing allowed to
  close the top of the lane — only for the seconds of a gust, with the
  uphill axis and the nearest walls readable throughout and the house back
  at `>= 5 %` from the platform at every running trough. The cableway carries in
  both directions. Pressing `E` on the platform brakes the line to
  a stop with a cabin on the boarding point - a distance-driven profile, so
  it comes to rest ON the point rather than near it - seats the hero on the
  cabin bench facing cabin-forward in first person, lets the line go, and
  fades behind the snow ridge into `AreaArrivalToken.Cableway` at the far
  terminal, which holds the arrival under a black screen until the transition
  flag clears. Both destinations then restore the cabin and passenger under
  the ride's own black fade before revealing the approach: the cabin
  covers the last `18 m` of inbound cable and the station half-turn, docks,
  then automatically plays the visible exit onto the boarding platform.
  The existing safety bar swings inward `90 degrees` from its front post to
  lie along the front window before the doorway is crossed, then closes
  after entry/exit; boarding turns the hero from the side entrance toward
  the cabin's forward axis. World-space attachment retains the player's
  scene parent throughout the ride. A cableway return also parks the
  Ferryman and his car on the mountain apron if the session has not yet
  taken the car, so a direct map visit to the village keeps the ride down.
  The two terminals differ by `MountainCablewayStationKind`: drive below with
  motor, reducer and shaft, tension carriage and weight stack above with no motor at
  all. Boarding is outboard of the outbound track, because the gap between the
  two tracks is filled by the bullwheel pedestal. The village wears a fourth
  deterministic Blender kit - generator contract `v3.3.0` /
  `village_house_archetypes_v3`, `24` assemblies / `57` role meshes. Two
  ordinary closed-shell house archetypes replace the former four cosmetic
  variants: one is a low dark timber block on a heavy stone plinth with sparse
  irregular openings; the other raises a bracketed projecting timber upper
  storey over a high masonry base and uses a more regular opening rhythm. The
  distinct top house is a third type, a broad timber main mass with a
  weathered whitewashed masonry side wing, larger and tidier without heraldry,
  frescoes or tourist-chalet ornament. Roof snow, facade repairs/shutters,
  garland posts, cable gate, rail bridge and a plain stone catch basin remain
  alongside the chapel, mine cart, adit frame, grave markers and firewood.
  Every house and the chapel render as a closed outward-facing Blender shell
  on all four sides; roofs and facade dressing do not survive around an
  invisible wall volume. The redesign preserves the planner's normalized
  bounds, footprints, collision proxies, routes, landmark aperture and story
  meaning. It raises no new
  surface family. Doors retain their plan-owned dimensions and actions;
  a separate Blender door assembly supplies the frame, leaf and hardware.
  Framed windows are placed at real opening dimensions on the wall face
  reported by the house mesh. Coherent timber courses, stone plinth courses,
  shutter joinery, rafters, layered roof edges and irregular roof snow give
  the two masses close-range structure. Five deterministic neutral facade
  sheets carry grain and wear with measured mean compensation and shared
  materials; bright and shaded panes
  share each window's existing dimming cutoff. The Return station alone has
  a fixed-metre Blender canopy with profiled steel, timber supports, fasteners
  and snow; its machinery, obstacles and docking remain plan-owned.
  Garlands are emissive geometry; two cords and three windows own the five real
  village lights. Six bounded spatial voices belong to the visible station,
  one wire, the cable gate, water catch basin, firewood and a house wall;

### City generation and layout

- Compact coastal City has two urban districts: working Industrial west bank
  and Residential east bank; the park remains open between them. OldTown/Nightlife
  enum values and catalog entries are compatibility identities, not extra map areas.
  `CitySpatialPlan.IsCompactCoastal` preserves 144 logical lots in `13 x 12`;
  internal pitch `26 m`, west/south service belts `12 m`, eastern four strips
  `17.5 m` and coastal land `28 m`. Declared land excluding sea/river changes
  `193828→137186 m²` (about `−29.2%`), without scaling models or vehicles.
  Short residential fronts/shared rear courts contrast with parallel work streets
  and loading courts. Ordinary fronts stand `1.25–1.55 m` behind the reserved edge.
  Four reserved domestic frontage lots instead keep `2.4 m` for the existing
  courtyard scenes; their footprints stay outside public pavement.
  The expanded oblique/L pilot is absent. Metre bodies retain real fit; `10×6 m` rear wings require full padded clearance and a `2.4 m`
  court gap. `3.6 m` through passages remain walkable under separate overhead
  collision, height `3.2 m` (`4.2 m` Industrial). Court loops join the graph;
  neighbours connect only across continuous safe-grade ground without roads.
  Road/ground complements retain metre poses. Graded home/bar/shop fronts share
  sidewalks, embedded skirts and fixture tops in City/Home.
  Home/bar/fair/arch and service approaches retain owners;
- `CityCoastalInfrastructurePlan` places three passive functional buildings:
  substation `5x4x3.7 m`, municipal workshop `7.4x4.2x4.4 m` and port workshop
  `6x4.4x4.2 m`. Their fixed footprints clear existing fringe works, roads,
  public pavements and traversal; maximum support-height spread `0.24 m`.
  At most `64` poles and `192` cable spans form a causal electrical network:
  poles sit outside the public footway, `5.35 m` from street axes, main wires
  clear roads by at least `7 m`, facade drops have physical ends. The imported
  topology is fitted once at build into one owned cable mesh with shared
  materials; no wire physics, extra Light, NPC or interaction. Home shows only
  a passive `48 m` slice from the same plan;
- one default-blueprint-only mountain boundary plan closes the physical west
  and south edges with deterministic flat-shaded low-poly ridge strips whose
  toes sample the authoritative terrain top. The south skyline remains closed
  over one low, dark `10 m`-wide river-cave water mouth; surrounding rock
  terminates both bank routes physically, and the cave itself is never
  walkable. A
  gate-free `8 x 5.5 m` portal at `yard-south-west-access` has a raised,
  terrain-overlapping floor and closed non-coplanar lining joints. Its `72 m`
  shell stays straight/physical for `12 m`, then bends west to hide its end
  and the backdrop. Navigation reaches `11 m`; crossing `8 m` shows the
  unavailable-travel thought and walks the ordinary rig back to `6.5 m`.
  No tunnel transition handler or cave interaction. North remains sea;
  east has the closed mainland road below. Two camera-relative west/south
  layers at `39.4-43.2 m` keep world azimuth within `48 m` far clip, without
  collision/lights/navigation/world bounds. Shared `CityMountainPhysical` and
  deterministic `CityMountainRockAlbedo` use matching forward/depth/normal
  dither across `43-31 m`, a `0.10` contrast floor only after native Exp2
  reaches it, and natural fog on approach. The fog-exempt shell mixes `0.86`
  toward City fog. The omitted `(-1,-1)` cell holds one textured natural-soil
  slope around `16.2°`, outside navigation behind the exact L-shaped road
  fence, with no steps/terraces/platforms. Outer wings follow sampled terrain
  and diagonal toe; the whole turning ridge profile welds west to south.
  A separate default-only `CornerGuard` pair closes the north-east urban-core
  road cap beside lot `[12,11]` as an ordinary physical `4 m + 4 m` L. It does
  not add ground or extend a boundary along the yards/waterfront, so their
  authored approaches remain open.
  Portal, physical entry and visual continuation pieces retain
  `RuntimePrimitiveLit`;
- immutable default-only `CityFringeYardPlan` derives five Yard compositions
  from canonical bounds/access/terrain. Four west/south strips share graded
  maintenance traces, drainage, retaining work, poles/cables and repair/rockfall
  pockets. Their roughly `22 m` road-to-toe terrain forms one conforming
  compacted-fill batch: `0-4 m` shoulder trace, three transverse traces and
  three or four seeded anchors in `4-14 m` (gaps ≤`40 m`), paired traces or
  flood-drain/retaining work in `14-22 m`. No broad longitudinal track overlays.
  Macro anchors: stepped culvert terrace, industrial repair frame, `6.9 m`
  tunnel forecourt (`>6 m` clear lane, embedded marks, two grounded concrete
  return wings, two-post frame/crown floodlight), caged floodworks/gauge.
  Four measured sheets: compacted forefield, aggregate, board-formed concrete,
  old masonry. Four emissive practicals stay outside the combined geometry;
  the nearest supported anchor within `20 m` leases `CityNightAtmosphere`'s
  last street Spot, keeping its `12`-Light pool. The tunnel moves that lease
  to the faulty second ceiling fixture, keeps a `0.22` daytime floor and gives
  its lens and pooled light the same sparse flicker. Four more emissive-only
  fixtures continue into the bend; the faulty ballast owns a positional
  `5.6 m` buzz and crackles only on visible power dips. The east drain/pole/
  shed/berm profile yields within the mainland-road clearance;
  its old parallel service-road strip is removed. One grounded empty mason
  cart stands at the west terraces; no other fringe vignette. Large masses
  are physical, small traces and cables visual only. Ridges overlap under
  the sampled terrain toe and carry its collider across the join; only the
  tunnel portal and low river mouth interrupt the rock, the bank ending against
  it. Every level-safe ring-road seam into the four mountain Yards is walkable,
  true drops keep rails; three `6 m` routes cut capsule-clear through the
  retaining line to the rock toe — two on broad gravel aprons, the south-east
  flood route on a narrow embedded trace — and the fourth runs through the open
  tunnel forecourt. Fringe dressing adds no destination, Light or world bounds;
  the eastern valley is a separate passive panorama;
- Default `CityEastExitPlanner`: actual compact eastern street→post `12 m`
  setback; the imported profile joins that same datum, with the real road
  ending `32 m` past the post. Flat first `10 m`; `5/4 m` apron/blend;
  booth/barrier/gate/fences close map/physics.
  `CityEastGroundTransition*`: grass-soil `3–5 m` in `8 m`, shared UVs/edge/footsteps.
  `CityEast{Swale,Relief,Tree}Plan`: graded hollow/level crossings, seeded
  metre relief/fixed edges/pads; shared render/physics/map; sparse `3–5 m`
  trees both sides, supported roots, trunk collision/map cuts. Seeded fence
  shrubs/grass/gravel, front `12–16 m`, gaps ≤`20 m`; rear banks/drain/grate/
  repairs, shed stock.
  `CityEastLitterPlan`: the 36-variant `CityLitterCatalog` dense in five bands/
  `.45 m` gaps; solids block/map-cut, clear the street/patrol/church/
  crossings/fence. `CityLitterPlan` (`CityWorldPlans.GetLitter`, primed): same
  catalog sparse city-wide: sidewalk edges, lot bands at walls, tide line, ≤6
  by park benches/paths; off roads/crossings/scenes/yards/cemetery/church/bar
  yard; `1.5 m` gaps, same variant ≥`18/45/90 m`; one instancer.
  Pooled lamp + canopy/shed Spots: day floor/halos/hard shadows.
  `CityEastRoadProfile`: shared road/land/car/lamp datum; `RoadGroundCuts` bends.
  First mast joins pool `12`; later shells/lenses/halos/pools are passive.
  `CityEastDistance*`: city/warm uneven steady glow dome on valley floor.
  Source depth: clip's last `.20 m`; foreground/`48 m`/`.070` fog stay.
  2 cars: exported lanes/session phase/pause/time guard;
  no gameplay/Light/physics/shadow/audio;
- `CityEastGuardDuty`: one home, one three-point out-and-back patrol; swap
  on return. Senior/Junior: own rigs/faces/clothes, separate shoulder rifles
  (`EastGuardActor`), body-aware grounded movement. Speech/modals hold duty.
  Six RU/EN pairs/four E replies share one no-repeat bubble/face clock.
  A whole pair precedes one pending E; no input lock, cancel/distance clears it.
  Personal pairs/glances wait beyond `4 m`, hearing `13 m`; E restores
  reserve. Pause freezes, range never replays;
- one immutable river contract owns a `10 m` channel, two continuous `3 m`
  promenades, `8 m` Works/Mouth road bridges and a `2.8 m` timber ParkPath
  bridge between the park's two `2 x 4` halves. Each road bridge has a stair
  and lower platform on both banks: four reachable landings. Their collidered,
  box-projected Quay lining closes the landward stair, platform and terminal
  faces; the railed waterside stays open. Quay fronts project `0.03 m` beyond
  Paving/Bed faces on main spans, lower frontages and the cave approach,
  retaining the landward rail seat. River parapets preserve stair openings and
  bank-road pads; road-edge fences treat decks as support-only. Route01 uses
  road bridges, never timber. In `default-coastal`, water/bed continue over
  `48 m` behind the southern mountain. Both walks extend from `Z=-182` to
  rock stops at `Z=-208`; cave water/space behind them remain outside nav.
  Low lanterns at `13 m` pitch keep emissive lenses/halos; pooled spill and
  sparse upper lamps always burn, at least two thirds by day (§20).
  `CityRiverDressingPlan` adds graded coping supporting whole rail feet, repair stones, distinct
  bridge joints, open lamp housings and flush tread noses in shared surface
  batches. Cast bollards replace cylinders with matching physical bounds;
  the existing routes, openings, lamp anchors and water owners remain;
- one immutable `CityElevationPlan` produced after 2D topology and before any
  lot, surface or access is materialized. The default coastal blueprint spans
  about `8.1 m` across its generated road nodes, peaks near `10.08 m`, gives
  every urban district at least `1.5 m` of local elevation variation, keeps
  the sea at datum `0` and gives the river a monotonic descent into it.
  The seacoast's raised decks — the mol, the sea pier and the mouth
  footbridge — are contributed to the walkable mask by
  `CitySeacoastPlanner.AppendWalkableFootprints`, since a `Water` cell is
  otherwise never walkable and the player would be clamped at the cell
  boundary; the open sea itself is never contributed.
  `CityTerrainSurfacePlan` is the authoritative sampled top for
  `BuildableGround`, `ParkGround`, `OpenGround` and `Beach`;
  `CityTerrainSurfaceWorldBuilder` turns it into triangulated render meshes and
  matching mesh colliders, eliminating vertical cell-slab seams and giving the
  beach a continuous waterward slope. Declared flat special surfaces and
  legacy/custom blueprints retain their flat fallbacks. Park plazas conform to
  the lawn/path profile, while district public places keep exact flat pads with
  `4 m` blended approaches. Buildings and public slabs extend their foundations
  downward without moving their authored tops. The samplers are authoritative for node,
  cell, road, sidewalk, entrance, return, open-area and debug-teleport height;
- one authored bar-side yard composition in the walkable roadless gap
  immediately left of the bar across the player's home frontage, between the
  bar and its neighbouring supermarket and distinct from the five typed fringe
  `Yard` areas. A worn 24-segment ring surrounds the dead tree and
  carries the pipe-backed wheelchair rider. One fixed cold near-white Spot on
  the supermarket's yard-facing wall runs at intensity `240`, twenty times the `12`-intensity
  street practical. Its bright inner cone contains the complete circuit with
  only `6°` total feather, while range is the greater of `1.5x` the sampled
  throw and sampled throw plus `3 m`. Hard shadows use `0.95` strength and high
  resolution. The HDR lens is boosted `4.8x` and its source halo is larger and
  brighter, but there is no volumetric beam. It stays constant through day and
  night and never tracks the rider, while the old yard lamp remains a dead
  physical trace;
- four signature exterior stair streets, one in Old Town, Residential,
  Industrial and Nightlife. Each owns `6-12` visible collider-free steps at
  `0.15-0.17 m` rise and `0.30-0.34 m` tread, two `1.5 m` landings, physical
  rails/retaining walls and exactly one hidden seam-free ramp collider per
  flight. Road-to-ground and ground-to-ground transitions use sampled endpoint
  heights for the same step-safe decision, and segmented guards follow every
  unsafe physical edge. `CityVerticalTraversalPlan` inventories those seams
  and authored road frontages and proves their authorized component from the
  spawn road. The first version permits only one walkable surface at any XZ
  projection;
- one shared MVP day/night lighting cycle for City, the Home window and the
  Home balcony exterior: night before `06:00`, smooth dawn from `06:00` to
  `07:00`, day until `18:00`, smooth dusk until `19:00`, then night again.
  It blends directional/ambient/reflection lighting and the bounded City/Home
  exterior night fixtures; the fixed neighbour-wall yard Spot is
  outside `NightFactor` and stays on. The atmosphere pool remains capped at
  `12` local realtime lights. One active bus may add `4`, the single pooled
  helmet-lamp pedestrian `1`, the yard `1` and the drying yard's night-only
  pole floodlight `1`. Fixed site lights, including the cemetery and the ten
  church-garden uplights, remain separate from the twelve-light pool;
  the scene Directional and transient lightning Directional are counted
  separately. Bar, Supermarket and Stairwell lighting are unchanged. The
  `0.070` exponential-squared luminous gray-green fog,
  fog-matched terminal camera backdrop, City-only `48 m` visibility cap,
  `CityFogField` and `CityNoirVolumeProfile` stay fixed across the cycle. The
  Windows player retains the runtime-only Exp2 shader variant;

### Weather, sky and presentation

- City and Home share a rain film: fast wetting, slow recession to City's
  permanent drizzle floor; absolute game time carries it across scene handoffs,
  New Game resets it. MPBs preserve authored tints; linear response maps vary
  local smoothness/relief. Up to `42` deterministic top-only puddles share one
  collider-free water mesh `3 mm` above the roads;
- one shared generated exterior cloud ceiling in City, Mountain Road, Alpine
  Village and the active Home balcony shot. A passive `220`-triangle hemisphere
  and one packed linear density texture feed three property-block profiles;
  City/Home reuse the same seed, canonical frame and absolute-time phase, while
  the road and village only change density, scale and colour. The shell follows
  camera translation, so its `47 / 119 / 109 m` radii inside the current
  `48 / 120 / 110 m` far planes are render distance rather than low physical
  altitude. It owns no fog, grade, Light, shadow or collider, and Home disables
  it everywhere except the balcony;
- native/SMAA High/default-on:three passes after native/intoxication, then 4:3/UI;
  previews/probes off. Flags:`SceneLightingPresentation` (4 owners).
  Warehouse:fan/steel/bounce. Village:window/garland air-snow/clip,
  snow bounce/metal/spring cube. House:halos/metal/bounce/air, two cubes;
  atlas/half-shadow/mirror stay. Road:air/snow/headlights/halos/metal/bounce,
  station cube. City:9 areas plus diner, real lamps/windows/traffic;
  finite air/bounce/metal; cafe joins two reused reflection slots.
  Home balcony shares City; indoors off. Dark places retained.
  Filtered256 captures:one pending per owner, no actors/vehicles/precipitation;
  post-submit watchdog gives bounded frame grace for cold stalls.
  moving lights never recapture. No small-object volume shadows.
  Cleanup; GPU unmeasured; Lights/fog/weather/story stay.
  Intoxication:vignette/ghost/chromatic/warp/warmth/exposure;
- DoF: shared setting, exterior Gaussian far blur;
  Bar/Supermarket/Stairwell/Home/Church/Mother's House radius `0.55`
  keeps nearby people/furniture/gestures legible. Priority-10 Bokeh serves
  modal close-ups, then yields to the scene grade;
- prompts/pause/start/inventory/shops/inspectors/journal/HUD/maps
  use Input System pointer/text and crisp `640x360` IMGUI after the world: soot/charcoal/
  dirty-bone, flat rectangular panels, thin nested frames, stable grain and
  grayscale focus. Packaged `Roboto-Regular`: RU/EN, no OS fonts;
  Unity legacy is emergency fallback. `J` opens a full-canvas scrolling
  checklist above the selected quest description. Unread entries blink a top-right
  notebook; its key persists until first opening, then appears for six seconds.
  Menus, inspectors and map omit persistent key guides/footers;commands appear once. Contextual prompts
  are full pointer targets for the same guarded keyboard/gamepad action. Loading
  retains framing/progress and its separately accepted painterly illustration;
- localized pause:Esc/Start/input-time-audio;Resume/Options/default-No restart/quit.
  Warehouse ambush;Lab0. Debug commits cans/fish/key/lamp/fan/gate;
  stages entry;progress stays. Gate→door E. Prep clock0;door3s→attack5s→shin
  hook/jerk+fan720°/s+Spot↑5s/lit sparks. Inverted roof hoist→free camera/180°roll;
  struggle;E/next0. Crane/8guides;front1.18m/red36/pair/FOV.
  Cancel restores preview pose/vitals/grip/fan/red. Clock until end;
  Space/A pause,R/X replay,Esc/B list before capture;
- shared 8-sided cylinders/URP Lit; soft City/balcony directional shadows;
  Home interior keeps its hard directional mode; MSAA off;
- City/Home recipes below feed `CitySurfaceLibrary`: scoped ImageGen pigment/
  physical maps, exact sources, consolidated ground; palette/layout/UVs kept.
  Bar facade: guarded UV0/original UV1. Flat coast/cemetery exceptions and
  glass/emission/text/water/characters stay. New BC7 maps≤224 MiB; donors extra,
  residency unmeasured. Queue: `city-texture-plan.md`. City's soil/asphalt
  repeat at `12 m`, sidewalk at `6 m`, paint at `2 m`, Repeat/Bilinear/mips.
  City, MountainRoad and AlpineVillage ground/retaining surfaces use shared
  `GroundSurfaceLit` via `GroundSurfaceAppearance`, linear response companions
  and MPBs (indexed in City); shared lit-shader parity and interior materials stay separate.
  UV4 road metres/width/seed place wear across/along actual roads; Home projects
  back into the same City frame. Open aprons omit lane bands. Existing snow
  state drives edge/substrate tint, without new collision or accumulation.
  Other City ground reads registry-driven global `_CityGroundWetness` without
  a second renderer list; MountainRoad never inherits that City flag.
  Mountain ridge haze keeps its dedicated shader and gains rock response;
- Park: `CityParkSurfaceAppearance`/`tools/build-city-park-textures.py` recipes
  turf/path/plaza (`4x4` joints)/bark/foliage/timber/stone/painted steel.
  Ground/stone/timber: `GroundSurfaceLit`; foliage/metal: City response.
  World UV pitches: ground `3.0/2.2/2.8 m`; face-normal projected
  trunks/crowns/hedges/timber `1.2/1.6/1.4 m`. Palette/brightness compensated;
  Home shares path. `CityDecorationWorldBuilder` batches landmarks by surface/
  colour, with continuous chunk UVs; other decor flat. Fountain disc meets
  inner ring; pours clear pedestal. Gazebo at the old chess anchor
  shares deck/steps/collision through `CityParkGazeboGeometry`; games/men,
  entry/docks free. Roof shelters rain; one-live/one-dead beam pendants only.
  ParkPath lamps omitted. Bins, blank notice board and repaired benches;
- eight legacy district wall albedos plus one shared roof cap, generated by
  `tools/build-city-facade-textures.py`, remain the box/sheet contract for the
  supermarket's crossing-only fallback and a prototype crossing the clipped
  Home half-space. They are no longer the visible surface
  path for whole ordinary buildings. Two per buildable district carry that district's two
  material axes: Old Town brick and blown render, Residential cool and warm
  painted panel, Industrial sheet and utilitarian brick, Nightlife shopfront
  and service side. Each sheet is authored at `1024` as four bays by four floors
  so Unity's import to `512` is an exact 2:1 downsample and the cell grid stays
  pixel-exact. `CityFacadeAppearance` tiles those remaining consumers by their
  own window grid rather than by metres, so one authored cell covers exactly
  one pane bay and one `2.35 m` storey and the baked window band, sill and grime
  run land on the fallback geometry; a stable per-lot whole-cell bay and floor
  rotation varies which cell lands where without disturbing that. The sheets
  hold a mean linear luminance of `0.35` and the night facade tint is brightened
  by `1 / 0.62`, which preserves the pre-texture wall brightness through URP's
  linear multiply and never clamps the brightest lot, a bar. Whole ordinary
  Blender prototypes instead use the later 24-sheet semantic surface contract
  described below.
  `CityStreetSurfacePlan`: Road v2 `8 m` traffic reserve, `6 m`
  asphalt at `+0.08 m`, pavement at `+0.14 m`; the `1 m` inset stays while
  compact public frontage widens outward to `2 m`. `8 x 8 m` node cores and
  ordinary `6 x 6 m` aprons remain. Road v2.1 moves supported corner pads
  outward, cuts raised kerbs back `4.5 m` at selected 2/3/4-way
  nodes and closes missing three-way sides with `1 x 8 m` paving. Compact
  narrow/wide approaches continue flush over bus aprons. Dashes clear nodes/
  zebras; up to six crossings share signals; bus/pole gap stays `0.30 m`.
  `CityStreetSupportPlan` typed stations drive imported Blender top/edge
  topology. Cached outer boundaries constrain
  soil/lawn/path triangles through a `4 m` blend;
  internal/buried edges do not pull earth down. `CityGroundSurfaceSystem` and
  `GroundSurfacePartitioner` retain planes/normals/metre UVs while removing
  lower-priority overlaps. Typed submeshes preserve priority/weather/triangle
  footsteps in `48 m` render/collision sectors; paint clips/projects to support.
  Disabled hosts retain semantics; structures stay. Local cemetery cuts/
  coatings and deformable sand/fixed collision remain. Home
  copies bounded actual street/terrain triangles without collision. Ordinary facade panes consume the first channel
  of the pure `CityDistrictPresentationPlanner`: each district keeps its own
  lit ratio, while every authored facade row quantizes that ratio to a stable
  warm/dark selection. At least one warm street-lamp-coloured pane reaches
  every floor and side, and a multi-pane row always retains darkness. Bar,
  Home and Supermarket panes retain their authored material families;
- one player-following `CityFogField`, capped at 36 more visible slowly
  drifting particles, plus depth-tested soft halos around lamps, bar lights
  and active signals. The same field, unchanged, now also runs on the mountain
  road and in the alpine village, where the area's own weather owner clears
  and refills it under a roof instead of a second controller; only the Exp2
  haze behind it stays per-area, and only the village's breathes with the
  gale;

### Streets, transport and sound

- All speech/`E` uses shared head bubbles; bottom descriptions/choices/pages stay
  silent. Russian spoken lines use whole-line procedural vocalization in their
  existing speaker profiles and captured windows: tempo fits without pitch shifts,
  text/faces follow voiced duration, and the remaining tail adapts. Preparation
  holds reveal; overdue/unready results cancel without replay. English retains
  `24` chars/s, `.13 s` writing clicks and the default `2.5 s` tail;
- eleven RU/EN role labels: Foreman, Watchman, Fisherman, Ferryman, Chess player,
  Checkers player, Bartender, Cashier, Cat, Cafe attendant, Guard (both east-post
  guards); village/Mother excluded.
  Moving actor/head: `6→4 m`, `.25–.5 m` gap, shared `10 px`/charcoal. Depth/actor-ID
  mask rejects colliderless occlusion; no edge clamp. Speech/pause/modal/transition
  suppress; overlap priority is active interaction→nearest;
- one radius-safe sidewalk/crosswalk/Courtyard graph; courtyard knots use
  exact free ground holes/heights and .35 m radius. Street spawn anchors stay.
  At most two low-poly walkers are
  active near the player: one randomized runtime event activates one slot at a
  randomly ranked obstacle-safe anchor in the preferred fog-hidden `76-86 m`
  band. If that band contains no anchor in a graph component capable of reaching
  the player, a linked dense-fog fallback may use `32-86 m`. The
  first event waits `1.25-7.5 s`; every later slot or replacement waits its own
  `3.5-12.5 s`, so the two walkers are staggered. Each walks
  forward through available turns, has an independent 50% choice at a zebra,
  and is recycled only after moving beyond `88 m` from the hero. Before a fresh
  walker first reaches `24 m`, eligible non-backtracking turns follow the
  shortest physical Path.Length graph distance to the closest node in that
  component; after that first encounter the guidance stays
  off and ordinary random roaming resumes. By day, its hidden distant
  simulation smoothly accelerates from authored pace at `32 m`
  to at most `2.75x` from `76 m`, bringing an approaching walker inward sooner
  and sending an outward walker back to the pool sooner. Camera
  direction, frustum membership and far-clip settings do not participate in
  spawn or recycling. Before `06:00` and from `19:00`, fresh population is
  capped at one slot and uses much longer `15-35 s` initial and `30-70 s`
  replacement delays without distant acceleration; the clock never removes an
  already active dusk walker.
  Every pooled body is the permanent default NPC `city.pedestrian.i` (`13`
  City, first `8` Home): model least-used over the catalog, street wear,
  own village gait; the weigher lends sit and guard/shove. The six library
  residents left the street on `2026-09-16`.
  A slot's `CharacterController` is enabled only after a unique, obstacle-safe
  spawn and disabled before pooling. The dedicated layer collides with the player,
  ignores other pedestrians and is excluded from camera/interaction queries.
  A roaming walker alone may briefly defend personal space: raw alcohol above
  `60` through `80` selects the Guard hand at `1 m`, and above `80` selects a
  Shove at `0.75 m`. One reaction at a time stops and turns the walker, samples
  a dedicated one-second action, then resumes its graph route. The isolated
  `CityPedestrianPersonalSpace` bank contains Guard/Shove for the six ordinary
  residents: the free left palm acts while the right hand keeps its grip pose
  (a roaming walker holds no prop in it; see the hand-prop library below).
  Contact occurs at one third of the action. A strong contact
  requests a collision-constrained `0.4 m` push over `0.3 s` and a modest balance
  perturbation (`0.65 m/s`). Another reaction requires leaving `1.5 m` and a
  `3 s` cooldown;
  staged roles, bus/bench activity and a player already under action or fall
  ownership do not participate. The reaction adds no dialogue or audio.
  The authored humanoid-NPC asset set comprises `25` rigged designs: five
  production-folder pedestrian models, `17` staged residents, the active normal
  and retained inactive Watcher cashiers, and the bus driver. The bartender
  uses the existing global default-worker model; both bespoke versions are removed. Every
  rigged design uses
  `NpcHumanV2`, the Hero V2 31-bone body core and compatible A-pose Avatar from
  `Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx`, with a common
  `0.835 m` rest pelvis. The `53`-clip `CityPedestrianLocomotion` bank uses `4.5.2`.
  The four relocated cafe models and their separate `10`-clip bank use
  `4.5.2`; the shelter trio and dedicated three-loop bank use `4.2.0`.
  Nothing an NPC holds is part of a body model. The nine hand props
  (carpet beater, cigarette, funeral bouquet, chalk, fishing rod, smoking
  pipe, cafe cigarette, service towel, coffee pot) form the separate
  `CityPedestrianHandProps` library (`4.5.2`, `33` meshes / `840`
  triangles, no armature) built by the same generator and imported to nine
  passive `Resources/Pedestrians/HandProps` prefabs whose `Mount` is
  measured against the reference body's socket in its bind pose. They are
  attached at runtime to a named socket (`SOCKET_Grip.R/L`,
  `SOCKET_Cigarette.R`, `SOCKET_Mouth`) only by the staged roles that need
  them; pooled walkers and bar patrons attach nothing, and the laid grave
  bouquet is the same prefab placed on the slab. The six prop-bearing
  bodies exclude their skinned prop parts. The accepted `2026-09-29` ordinary
  refinement shapes anatomy, separate fingers, clothes and shoes on four models:
  fisherman `45` meshes / `7,760` triangles; mother `49` / `7,872`;
  cashier `40` / `6,828`; driver `48` / `6,876`. Fisherman/mother retain
  generator `4.5.2`; cashier `2.0.0`, driver `3.0.0`. Three share the palette-neutral `256 px`
  `OrdinaryCharacterDetailAtlas`; the cashier retains his own garment atlas.
  The mother's original expression grid remains, with its surface ahead of
  the skull. Shared rig/sockets remain; both fishing clips bring the left grip closer.
  Fisherman grips the rod with curled fingers; sleeves stay separate, the small reel sits below.
  PNG features cover the shaped
  head; fitted 3D moustache/beard/small hollow pipe and exhale meet the raised mouth. The closed
  hood clears the skull. Ember/light show
  only on inhale; shared mouth exhale follows clip phase. PNG faces are the Normal/default standard;
  remaining solid Normal features await migration.
  The inactive Watcher cashier (`2.2.0`,
  `2.05 m`, `44` meshes / `1,588` triangles) remain unchanged.
  Every modular `SkinnedMeshRenderer` is governed by
  `NpcSkinnedMeshCullingGuard`: humanoid authoring pipelines serialize
  dynamic bounds, and their registry families reassert that contract once
  when an instance wakes. Clip-driven limbs and procedural bones therefore
  cannot be frustum-culled by the small A-pose boxes imported from the separate
  model FBXs. The ordinary bartender uses the catalog worker anatomy.
  This is a shared adult anatomical substrate,
  not a flattening of character identity: the mouthless Long-Arm, kettle and
  hopper silhouettes remain, and the driver keeps his horizontal eyes. The active cashier keeps the same uniform, face, blink
  and attentive gaze, but uses an ordinary head and non-scaling human neck;
  the former `18 m` treatment survives only in the inactive
  `watcher_cashier_v1` asset. The appearance catalog covers `27` authored designs — `7` bizarre and `20` normal.
  The active cashier is normal; his retained Watcher predecessor is bizarre
  and never creates a duplicate active role. The default-worker bartender is ordinary.
  The pool holds one presentation per registered design — Lampshade Walker,
  Chair Carrier, Kettle Hat Walker, Long-Arm Walker and Helmet Lamp Hopper —
  each with four material-property-block palettes. All five use dedicated
  in-place `Idle`/`Walk` loops: the Lampshade keeps a persistent
  hunch and uneven short step, the chair-burdened walker stays upright with a
  quicker high-knee gait, the stout Kettle Hat Walker moves at `0.90-1.02 m/s`
  on `1.08-1.18x` clips with a constant waddle and counter-phased belly and
  kettle — a kettle that is always on the boil: an editor-built lid pivot
  under the head bone (the lid and knob re-skinned to it, no bone added)
  trembles and jumps on a seeded `2.2-3.1 s` vent cycle driven by the
  presentation's own delta, and a factory-attached grey steam plume leaves the
  spout anchor, in every state including the Route 01 ride, with no light and
  no sound; he is the first pooled walker with a `256 px` detail atlas
  (greys multiplied by the palette tint through the shared material, `2,004`
  triangles / `52` meshes with real sleeves, cuffs, lapels, thumbs and booted
  toes) — and the narrow Long-Arm Walker is the slowest at `0.72-0.84 m/s` on
  `0.86-0.94x` clips, shuffling on barely lifted feet while its ground-reaching
  bare forearms swing a quarter cycle behind the legs and never fully settle in
  idle, and the Helmet Lamp Hopper is the fastest at `1.32-1.48 m/s` on
  `0.94-1.06x` clips, never taking a step at all: it coils on `0.46 m` hind
  feet and covers ground in two-footed bounds with a proven `0.24 m` apex. It
  is also the only walker allowed a working light — one always-on shadowless
  `7.5 m` Spot bolted to its miner's helmet and parented to the animated head
  bone, capped at one instance because the pool holds a single hopper. Every
  design keeps the shared `1.75 m` envelope and the fixed `1.7 m`
  collider: the kettle walker's overhanging belly, tiny legs and oversized
  skewed kettle carry its short read, because a genuinely shorter walker would
  need its own collider parameterisation. A seeded choice among free
  presentations fills one slot per event, so the pool is twice the two-actor
  cap and concurrent walkers keep distinct silhouettes while repeat encounters
  vary the pair. Home transforms that same graph into its local exterior,
  retains a bounded `100 m` fog-hidden approach context beyond the facade while
  rendering its existing `48 m` street slice, and runs the slots only while the
  Balcony shot is active;
- one route-driven ambient midibus with a strict single-slot cap. The production
  model uses its real `8.25 x 2.38 x 2.95 m` body and `4.5 m` wheelbase rather
  than a hidden gameplay scale, and exposes a modeled driver area, twelve
  passenger seats, rails, dashboard, two animated double-leaf doors, rolling
  wheels and front steering. Each door keeps its outer posts fixed while its
  two independently hinged leaves fold inward around the bus vertical. The
  separate passive `CityBusDriver3D` uses the shared `Player3DLit` material
  and the `NpcHumanV2` rig/Avatar copied from Hero V2, with a normal low-poly
  head and its canonical long horizontal eyes.
  Passengers follow the cushion plane after bus movement; lifts match seated mesh depth.
  Procedural seated IK keeps both hands on the rotating wheel grips. For each
  door command, a deterministic timeline moves only the right hand to the
  dashboard button with `12 mm` travel while the left stays planted, and turns
  the driver toward the front door for the open-door hold before returning
  during closing. The long eyes blink on a deterministic cycle. A nearby hero
  on the outside of the front entrance takes focus priority: the driver tracks
  the hero's real head while the neck/head segment extends by up to `0.10 m`
  with a `1.35x` cap, then restores its exact base scale when the hero leaves. A
  runtime-only sprung presentation pivot gives the moving body speed-scaled
  cartoon heave up to `0.045 m`, acceleration/road pitch up to `0.8` degrees
  and steering/road roll up to `1` degree. All four wheel assemblies stay
  outside that pivot and grounded; actor, collider and route pose do not
  move. Its bounded audio presentation is equally physical: a
  fully spatial rear-engine loop uses a linear `24-48 m` tail tied to
  `RuntimeSceneSetup.CityFarClipPlane`: it is silent through the `76-86 m`
  fog-hidden activation band and becomes readable only after entering the
  rendered street slice. A second rear-mounted structure-borne loop fades in
  only for the hero aboard. The front and rear
  doorways each own a dedicated spatial pneumatic voice; opening and closing
  clips are fired once from the actual door-phase edges and pool cleanly with
  the actor. Canonical Route 01 is an immutable right-hand, Street-only
  closed grand city loop. Its target planner serves every district point of
  interest and `PlayerHome`, and on a dressed layout adds coverage targets:
  every open-area access gate (cemetery, all yards, the waterfront), two
  eastern waterfront spread anchors so the whole `400 m` beach front is
  driven, the outermost park gate on each river bank and the supermarket.
  Targets are ordered by their station along the road-grid perimeter,
  counter-clockwise from home, so the right-hand doors face the outer
  precincts and the served sequence crosses the river exactly twice — once
  per road bridge. Coverage targets prefer the kerb that faces their
  precinct over raw edge adjacency, anchor beside the gate rather than in
  its `8.8 m` approach throat, may drift up to four road edges and `120 m`
  when their own frontage ends in a river-flank or map-corner stub, and are
  dropped (never fatal) if no cycle-capable candidate survives. District
  POIs keep their bounded same-district river fallback and Home stays on
  its frontage or one edge away. Connectors are length-weighted shortest
  paths that may not drive any selected stop's own direction — the bus
  never cruises past its own pole — while the opposite direction of a stop
  street stays open, as on any real carriageway. Stop furniture keeps
  `7 m` of clearance (pole and shelter wall centre) from every bar, home
  and supermarket door sidewalk point: a blocked placement slides along
  its link to the nearest clear spot and a link with no clear spot is
  rejected, so no shelter ever crowds an entrance. After the ring closes,
  a coalescing pass drops the less essential stop of any pair closer than
  `80 m` along the loop (home and the district POIs are never dropped; the
  surviving pole serves both destinations), then a spacing pass walks the
  loop and inserts plain kerbside stops wherever the along-loop gap exceeds
  `200 m`, aiming at `150 m` and never landing two inserted stops closer
  than `80 m` or on a directed street the loop drives twice. A second,
  planar coalesce enforces a `35 m` floor between any two poles anywhere
  on the map — the loop folds back on itself, and along-loop distance
  cannot see two shelters sharing a corner. The veto is empirical, not
  predictive: a drop is tried, the spacing pass refills the torn
  stretch, and only if some gap still exceeds three ideal intervals is
  the drop rolled back (then the pair's other member, then protection). Full-body-clear ordinary straights and proven `6 m`-radius left
  turns enter the loop. At selected Road v2.1 nodes only — whose corner
  pads may now stand on open yard ground, which is what lets the loop turn
  along the city fringe — a clearance-proven two-edge right-turn macro uses
  a long S-merge across the full incoming Street, a `4.5 m` quarter-turn in
  the clear core and a symmetric S-return across the outgoing Street;
  ordinary tight `3 m` right turns remain rejected. A physical street link
  may recur in a connector, but every ordered occurrence receives a unique
  route link/node ID. Route selection has no random branch or player
  pursuit. The compact production loop recalculates semantic, gate and
  numbered street stops from coverage and spacing, with no fixed stop count.
  Each has a physical blue `01` pole, served once per lap by that
  deterministic door/driver timeline with a fixed `10 s` total dwell,
  including `0.70 s` opening and `0.70 s` closing transitions for both
  doors. Random roadside decoration does not emit bus shelters.
  The residential last-route island has a working pole
  nearby but outside its public ground and approaches, leaving the abandoned
  island structures distinct from the live stop. A pooled actor prefers
  obstacle-safe fog-hidden route poses `76-86 m` from the player and falls back
  to `56-86 m` only when forward travel on the same loop can approach the
  player. The cap means at most one bus can be active or potentially visible
  rather than guaranteeing that one is always on screen. While the hero is
  outside, it yields to the player and pedestrians and recycles only when its
  full body is at least `92 m` away. While the hero is attached as a passenger,
  the director omits that hero from bus-obstacle prediction and prevents
  recycling or slot release, while pedestrian yielding remains active.
  Suspension, wheel/steering articulation, button travel, driver hands/look, the
  door timeline, a synthesized engine loop and night-scaled head, tail and cabin
  emission reset with the pool. Two
  shadowless runtime headlight Spots illuminate the road ahead and two soft
  downward cabin Spots light the interior; the shared night factor scales all
  four and pooling switches them fully off. Camera direction and frustum
  membership never participate in the lifecycle. At either fully open
  passenger door, the standard localized E/Enter/gamepad/pointer prompt begins
  the passenger MVP. The controller chooses the closest valid front- or
  rear-door exterior dock and records that door-specific transfer; the bus
  holds its dwell while the visible three-second `BusBoardEnter` action carries
  the hero through the selected live doorway and aligns the ordinary rig's
  pelvis to window seat `07`, the first fixed anchor on the side opposite the
  driver. The controller then keeps
  the gameplay root in its original hierarchy, disables ordinary locomotion,
  collision and the contact patch. After each bus update it late-synchronizes
  the root's world pose to that actor-local seat, keeps the two-second
  `BusRideLoop` aligned to the live seat and owns a seat-relative seated camera.
  Its sprung position keeps a level horizon/independent yaw-pitch, without body
  roll. The aisle-side default looks through the nearest window; shared mouse
  orbit (no button), right stick and arrows rotate within cabin bounds.
  The exit prompt is withheld until
  `ServiceOrdinal` exceeds the boarding value, so it is available at the next
  or any later stop. A second service hold keeps the doors open while the
  three-second `BusAlightExit` uses the same selected live doorway waypoint and
  an independent validated grounded roadside exit pose, then blends back to the
  ordinary chase camera. Entry and exit root heights are resolved from the same
  deterministic street-surface plan that builds the physical raised sidewalks
  and flat bus aprons.
  Normal completion and cancellation, scene teardown or forced actor cleanup
  restore the player motor, `CharacterController`, contact shadow and camera
  exactly once without mutating its hierarchy, with a safe exterior fallback
  when needed. This passenger runtime remains City-only and uses the first
  fixed opposite-driver seat; it has no fare, destination picker, NPC
  passengers, passenger
  persistence or live map marker.
  Home's bounded exterior regenerates
  the same route plan and reconstructs the nearby Home stop as a static
  collider-free pole, but it has no bus actor or director: no real Street
  pass-through offers both complete-body seams at or beyond the fog-hidden
  `56 m` boundary, and the default facade faces a visible road terminal. A
  fabricated continuation or
  Balcony-camera-owned activation would create a visible pop, so neither is
  introduced;
- deterministic street lamps with geometry batched into `48 m` spatial
  chunks, focused lower-pole collision proxies, shadowless spot-light pools
  and slow out-of-phase amber traffic signals generated from the road graph;
- scene loops under `Resources/Audio/`: `{City,Bar,Supermarket,Stairwell,Church,Home}Music/`
  with matching snake-case `_theme` names; optional slots fail silent.
  `AlpineVillageMusicPlayer` adds `AlpineVillageMusic/alpine_village_theme`,
  user's optional MP3: streaming/background/stereo.
  Lodge/both doors shut: 1 s resume, no restart; open/outside: 4 s fade/pause.
  Suppressed before playback; normal scene exits.
  User gain `1.00`; LUFS uncalibrated.
  `MusicMix`: unscaled `4 s` out→silence→`1 s` in; no overlap, waits for clip data.
  Six measured masters span ~`8 LUFS` raw;
  source trims/Music `-11.5 dB` target `-36.5 LUFS` after Master `-6 dB`.
  Ordinary themes: `12 kHz` low-pass; action transients kept.
  The bar is diegetic: `bar_theme` sits on the visible jukebox grille with
  full linear positioning across the room, a `120 Hz–5.6 kHz` cabinet band,
  light distortion and a short-range generated motor/record texture. The
  cabinet's amber panel and two pink tubes use three phase-shifted emissive
  pulses whose strength follows the theme gain; they add no realtime `Light`
  and never strobe. Departing themes detach into the persistent mix:
  Single-load keeps the tail over the door presentation while the destination
  streams without waiting; its theme enters after that tail.
  Home pauses `home_theme` after the same `4 s` fade-out
  whenever the Balcony shot owns the doorway-hysteresis zone, then resumes the
  same sample through the `1 s` fade-in only after returning indoors and only
  once the mix is clear. Home also owns an optional interaction-local
  `Resources/Audio/SmokingMusic/smoking_theme` loop: it holds silent until the
  apartment theme has finished leaving, eases in over the shared `1 s`, and
  leaves through the shared `4 s` fade-out when the vignette exits.
  Music is also bound to places, not only to scenes: `City` hands the mix to
  an optional place theme whenever the hero stands on grounds that have one,
  and takes it back on the way out. The one slot today is
  `Resources/Audio/CemeteryMusic/cemetery_theme` over
  `CityCemeteryPlan.Grounds`; the place keeps the mix until the hero is `4 m`
  clear of those grounds, and both tracks resume from their own sample.
  Missing optional tracks are silent-safe — an empty place slot simply leaves
  the city theme playing unless the Ferryman's radio is switched on.
  The radio has exactly three station folders under
  `Resources/Audio/LastRouteRadio/Station1`, `Station2` and `Station3`.
  All three contain user-supplied MP3s; each accepts one WAV, OGG or MP3 with
  any name. A new session selects station 1. Tracks loop from the dashboard:
  mono, a `180–3500 Hz` old-speaker band, mild saturation, quiet hiss and subtle speed
  drift. Power, station selection and a separate playhead for each station
  survive area changes. An empty station stays selected without music or a
  fallback track. Radio power
  suppresses `city_theme` before its first start after a
  City load and while the location director selects it. Power-off immediately
  stops radio music/hiss and any detached tail, preserving the station and
  playhead. Starting the passenger's exit invokes this same power switch only
  if it is on; the city resumes through its usual `1 s` fade-in.
  Power-on/scene exit retain the shared music rule. Gaze callouts outline
  radio knobs and the moving glovebox handle: `E` powers/opens/closes;
  powered `Q` cycles `1 → 2 → 3 → 1`, moving the needle with tuning hiss/crackle.
  Each trip randomly chooses one station for the
  driver's single permitted radio reaction. After `10 s` of its actual
  playback, he starts `lastroute.ride.radio` and reaches for the tuning knob
  with the distal palm gripping its side. A low, outboard elbow lets the fingers
  follow the forearm. The station changes only at the reached turn endpoint,
  with the ordinary tuning cue; his hand returns
  to the steering wheel. Player power/tuning input cancels the gesture and
  returns the hand from its current pose without overwriting the player's selection.
  The chosen station and used flag survive both scenes; only a new trip
  chooses again and clears the flag. Missing music does not advance the clock;
  pause/loading freeze it. Power-off and station changes reset only that clock,
  preserving the choice and once-per-trip limit. Themes: shared `Music`, scene/interaction ownership;
- one shared `BarPromenadeAudio` mixer with `Music`, `Ambience/Beds`,
  `Ambience/Details`, `SFX/World`, `SFX/Gameplay` and dry `UI` groups;
  all snapshots keep Music `-11.5 dB`, Beds `-4 dB`, Details `+0.5 dB`, World
  `+2 dB`, Gameplay `+2.5 dB`, UI `+1.5 dB` under Master `-6 dB`. City, Bar,
  Stairwell, Home and DoorTransition retain reverb/echo returns. Wet tails
  blend over `.25 s`, except DoorTransition's immediate blackout. Detail/world
  sends compensate dry boosts, preserving room energy;
- deterministic generated mono retro SFX at `22050 Hz`, including a footstep
  per ground with three variants for the hero and NPC rigs, a door latch and
  hinge creak, with bounded
  category pools, per-effect cooldowns and voice limits, all routed through
  canonical mixer groups;
- separate scene-local procedural City, Bar, Home and Stairwell ambience beds,
  plus a nine-voice causal City runtime, an eight-source Home spatial soundscape
  and a three-source Stairwell soundscape. City's former non-spatial electrical
  content is gone: its bed is only a quiet diffuse air floor. Immutable
  descriptors bind sound to visible waterworks, drying rack, carpet,
  last-route speaker and park-fountain bounds. The director owns five loop,
  three scheduled and one action voice, creates only deterministic quantized
  mono `22050 Hz` clips, activates them inside their finite radii and applies
  coarse building-mass occlusion. Carpet impacts fire on the authored contact
  frame. `CityWorkAudio` routes anchored mono port/cannery sources to
  `SfxWorld`, with full 3D, no pan/spread/Doppler and `0 dB` direct level.
  Source reverb is `.28 s` outside, `.95 s` inside; linear ranges `3–32/2–24 m`.
  Zone mix is zero; City's mixer return stays. Crane audio follows the hoist
  drive; retort stays in its body. Factory fan/contact cues share this route;
  an empty scale and unbound swing are silent.
  Surf is one fully spatial voice following the nearest point of the finite
  waterline and reuses the same building-mass attenuation; thunder is placed
  at the deterministic lightning azimuth, and
  rain alone stays diffuse because it is a field around the listener. Home
  combines a calm room bed, synchronized co-located closed
  and open refrigerator loops, balcony night air and sparse soft wood,
  radiator, radio and bathroom details, plus a dedicated fluorescent crackle
  source co-located with the bathroom tube. The crackle is triggered by the
  same applied-factor changes that drive the visible tube, halo, point and
  spill lights. Both refrigerator timbres are `4 dB` louder than the original
  mix and retain their equal-power door crossfade. Stairwell combines a
  concrete room bed, ventilation and electrical buzz with rarer pipe knocks,
  metal stress,
  distant water and movement. Both use deterministic schedules, `22050 Hz`
  mono clips, quantized retro waveforms and layout-derived anchors;

### City structure, districts and dressing

- an immutable `CityBlueprint`/builder/catalog boundary with stable blueprint
  and area IDs. Area definitions separate `UrbanBuilt` districts from
  `NonUrbanOpen` areas, retain a reusable visual archetype, declare movable,
  center-anchor or north-edge placement and assign buildable, park, open-land
  or water topology per cell. The legacy rectangular blueprint remains a
  compatibility path;
- a spanning-tree road graph over only the sparse road footprint, with
  deterministic optional interior loops, filtered cross-city arterials,
  required open-area access edges and three declared river crossings. After
  the seeded passes `default-coastal` requires every exterior road-grid
  frontage as Street, so bridges and bank roads close one drivable outer
  circuit, then appends its authored street `(10,4)-(10,5)`:
  the x = 10 Nightlife lane meets `[9..12;5]` at a crossroads instead of
  dead-ending between `[9;4]` and `[10;4]`;
- four readable built areas—Old Town, Residential, Industrial and
  Nightlife—plus a fixed 16-cell central park split into two `2 x 4` regions
  with lawn, plazas, trees, benches and hedges. A dedicated timber footbridge
  joins their ParkPath graph across the river. `CityParkBenchPlanner` derives
  four ordinary benches per region from real non-bridge path runs; one
  descriptor keeps the oriented timber, collider and path-facing sit dock in
  agreement;
- one fixed Nightlife survival pocket between `[10;5]`/`[11;5]`: a closed,
  non-walkable `11.602 m`-deep arch above a `1.562 m` terrain step. Ten north
  steps meet a supported east `7.30614 x 8.851 m` terrace, including its
  `1.50 m` landing; slab/masonry join the facade and south wall end.
  Barrel, two warmers, bedding/sleeper occupy the terrace; fifteen passive
  empties (bottles standing and lying, cans) ring the bedding clear of every
  anchor; sparse clutter stays below. Three detailed Hero-Avatar rigs have
  `256 px` atlases and separate warming/sleeping loops, no colliders or
  player response. The three talk among themselves over the shared bubble
  channel like the port crew: 20 warmer pairs and 4 sleeper answers, no
  repeat until a pool is spent, within `13 m` of the hero, never about him,
  frozen by pause, dropped out of earshot.
  The sole full-depth route is west/lower, `2.2 m` wide; `1.09 m` guards
  close `0.41–1.562 m` drops and only the stairs enter.
  Nineteen named renderers reuse measured City surface families through MPBs.
  Five flame/ember parts, spill/sparks, one warm Point Light and bounded
  synthesized crackle belong to the barrel. Rain dies under the
  roof; no local fog;
- fair `[11;3]`/`[11;4]`: four staffed passive bread/fruit/pottery/toy stalls,
  shared hero organ/bell actions, two seats, clutter, three always-lit garlands
  in pool `12`. Three silent adults idle at the first three stalls' inner ends:
  global `fair.visitor.0..2` identities/wardrobe, no aprons, grounded collision.
  Three silent children share one detailed body, three constructed outfits
  with waist-high trousers and child clips: watching toys, rolling a personal
  car at a low table, sitting via `CityBenchSeatClaims`. Routes and hand/foot
  contacts preserve `2.2 m` paths and the west door approach. Child gaze follows
  sampled face orientation, fades with activity; offsets restore before sampling.
  `CityFairPlanner` grounds supports; decoration/litter/arrivals stay clear.
  No buying/speech/reward, cat response or story state;
- one mandatory north-edge waterfront in the default blueprint, dressed as
  the seacoast precinct (`CitySeacoastPlanner`): its connected beach has a
  deterministic street approach and remains walkable to the water line,
  while the northern water row carries an animated sea (a third material of
  the shared water shader over chunked sheets and a continuous sand slope
  whose depth hides its far edge) and stays excluded from player navigation and
  night-fixture placement. Open west/east sand receives uneven swash tongues
  reaching up to `2.8 m`, with a faster advance, slower retreat, broken foam
  and a brief wet-sand tail. The shared transparent swash strips conform to
  the beach, add no collision and exclude the sea wall and port quay.
  Beach sand carries bounded `0.15 m` deterministic relief on a `0.4 m`
  grid and a shallow loose visual skin; its collider is a coarser `0.8 m`
  lattice (`0.4 m` in the port's graded box). `CitySandTreading` presses only
  the skin into soft trails; confirmed beach contacts use `FootstepSand` and
  small sand grains through the village kickup effect. The loose skin fades across the
  last `4–6 m` toward the surf. The same sand texture, tint and world UVs
  continue from exact shared shore heights and normals down an `18 m`
  seabed, easing toward a `1:5` slope. River-mouth cuts remain open.
  The shore is zoned around the river mouth — a
  working fishing port with a quay, two cranes and a breakwater
  to the west; a granite esplanade with sparse glow lamps, benches and the
  abandoned municipal boat station (hut, «ПРОКАТ ЛОДОК» board, sea pier
  with the fisherman, chained slipway, hauled hulls) at the centre; rotten
  breakwater piles, driftwood, dune grass and a stranded barge on the wild
  east — with a timber footbridge over the mouth, full-width quay thresholds
  adopting both river promenades' north ends while short transverse rails
  visibly close only their non-walkable waterside lips, a coast pedestrian lane in the walker
  graph, seacoast texture sheets, map landmarks and a synthesized surf
  ambience bed. Offshore, at the edge of visibility in the fog, stands
  the abandoned lighthouse island (`CityLighthouseIslandPlanner`): a
  fixed presentation-only rock mound with ruined fishermen's shacks, a
  wrecked hull and a ~15 m banded lighthouse whose lantern genuinely
  rotates (two opposed additive beams, pure seed+minute rules,
  night-gated), rendered on its own no-fog shaders with a
  camera-distance self-fade inside the 48 m far plane — visible from
  the esplanade, sand and pier head, gone from every street;
- Port (`CityPort{Plan,Cycle,Controller}`): `20 m` trawler, west-mouth berth;
  2 cranes/trolley/cold store, 3 units. First running/unpaused entry starts
  ship/supply once; pre-entry time excluded. Autonomous scenes/reentry;
  new-game reset. Before: Approach `0`. `432 s`+waits: moor, `3*64 s`
  deliveries, secure/depart/idle; truck gates next ship. Silent custody seek.
  Cargo clears baffle; roller leaf/rails move together, outer-jamb bumpers.
  Sway <`9 cm` settles before lowering; empty trolley/cranes return.
  Five default workers: captain/deckhand/operators/docker; shore trio rests under
  canopy, returns via south lanes. Paired speech keeps absent/no-repeat rounds;
  rest ends before greetings, staggered farewells fit earshot, no replay.
  Pause-aware breath/weight/gaze; turns/Walk→Run retain grips, levers follow hands.
  Driver↔docker only; tare/cart avoided, physical custody-owned people/cargo.
  `CityPortForeman`: store wall/cart-facing/quay light, left carrot: 3 bites→pail→pocket.
  E/pair gate, `1.65 m` approach/two side chest shots; spoken «Хочу»/«Не сейчас».
  Shared mouths/brows/blink/soil twins; punctuation/tail close, face fills mouth,
  chin/jowls lag/decay. Pause freezes; cancel restores faces/input/HUD/rig.
  Held carrot clock/bite; left down/right gestures, paired pose/camera.
  No job/pay/quest. Solid caissons: `20 m` seaward/`-3.4 m` below sea.
  `CityPortAccessPlan`/`PortAccessLayout.json`: `6 m` road/regrade/L-yard,
  `6.481 x 2.4 m` truck; road/shoulders form city approach; ramp/yard masks join.
  No bypass/spur/crossing in geometry/terrain/walk/map/fence openings.
  Shore NPCs use sand east of road opening. Rear/side walks stay; boats avoid
  `3.4 m` basin. City asphalt/`12 m` phase/wetness.
  Panels/guards/joists/wired lamps keep baffle/east stock; solid west rack/bench/
  lids/inserts/tare/tools/gloves/squeegee, flush drains/wheel rubs clear lanes.
  Braced canopy/east rope-shackle-chock rack/cooler supports: three passive
  groups/≤5 renderers/shared surfaces; anchors/rest docks/bounds stay.
  Roof flashing/fender/ladder/bollard/crane bolts refine old meshes.
  Rack/cooler solids join canopy/wall collision, clear of loading opening.
  Two-fan evaporator→outside condensers/drain.
  Three warm shadowed spots join quay/store/canopy lights: day≥2/3,distance gate.
  Searchlight shadows deck/water; wake `CityPortWater`; `CityPortSound`:
  Motor `.6/.24`, `48 m`; horns `20/4 s` pre-Moor/Depart. Arrival ×2,
  echo `780 ms`/reverb `6.2 s`. Pause/teardown: people/audio/wake.
  Cramped blueprints retain nonoperational mol;
- Cannery: `8 x 14 m` hall/`18 x 18 m` site/bay/public bypass.
  Side/public + west/staff openings; machines/glass stay.
  `CityCanneryPlan`: ten Blender parts/five maps/port surfaces, fifteen cans/
  three cartons/four workers/driver. `CanneryReceiverActor`: athletic `1.96 m`,
  Hat/3D glasses/painted face/arm ink/5 clips; free-hand adjustment.
  Unload→two-hand carry→outdoor scales→release/settle/nod→support; empty=0.
  Three approvals/put-aways/clearance gate loading.
  Receiver/seamer: fixed `NpcWardrobe`; body/ink/face/hair/glasses independent.
  `ordinary-worker-v1`:fair/port/factory/driver/station/bartender +13walkers.
  Men:hero anatomy/NPC rig,160–185cm/slim–chubby+belly.
  4faces/3haircolors/21skinned items/8slots/3presets,shared assets/coverage;
  vest/jackets/cardigan/coat/overshirt/trousers/shoes;hero/bespoke/combat kept.
  BodyNpc torso metadata retains triangle-corner multiplicity after FBX welding;
  imported frames keep the strict affine check.
  Hero/NPC hands:+20%,slim,fixed wrists;`NpcHandPose`:bare/gloved grip.
  `DefaultNpcPopulation`/`CreateForCharacter`:stable IDs/looks by role,no repeats.
  Four wait at bench. Snapshot-owned `DriverRest`/`DriverLunch`:
  cart→bench/bread→stow/stand→load; shared seat/food/pause/seek/range/debug/custody.
  `LifeSeconds` pauses; work time owns walks.
  Prep wipes/returns cloth; seamer cuff, retort wrist; inspection owns receiver hands.
  `CanneryWomanActor`: one outfit/5 clips/shared contacts; pelvis hem,
  nape/bend/inertia/wind, skin/sleeve/arm/strand contacts; body/pose caches, idle once.
  Pause freezes, seek/range reset, palette kept. Hero-face atlas/16 RU/EN lines;
  smile `wait.07.b`/`wait.10.b`.
  `CityCanneryConversationDeck`: `8/10/4` pairs/shared bubbles/free hands/no-repeat;
  no hero/job/romance, driver↔docker. Fittings/routes clear;
  local sounds follow action/pause/distance/seek.
  `CityFishSupplyCycle`: FIFO `2x`/inspection/`276 s` transfers, pause/obstacle holds;
  fixed stock, tare Deferred. Truck `6.481 x 2.4 x 3.2 m`, full cab/guided lift
  above ground; central exit→right lane/parking, clear turns, streets `8/6 m`.
  Six-second access yields to entering/unbrakeable bus until release.
  Dock-latched unseen truck/driver `3 m/s`, parking `156 s` with first crate,
  then factory trips; two soft horns, reverse beeper/ajar door/look-back steering.
  Cranes never wait/reserve access. First arrival owns store doorway; driver
  takes accepted stock or waits for docker ("Жду тебя, дружище"); later docker
  holds north threshold. Only work shifts; idle/speech/waves live,
  event time restores custody. Cart/canopy/north approach clears tare/rear grips;
  east aisle free. Doors→unfold→lower;
  cart return reverses. `80/96 m` mesh/light gates retain custody/collision/traffic,
  restore on approach even paused;
- up to two decorative old fishing vessels spawn only near the actual hero
  at the coast (`CityOffshoreBoat{Planner,Controller}`): full presence within
  `8 m` of the finite shore or pier/mol decks, zero at `28 m`. Their cleared
  courses avoid the working port, are chosen around the hero's easting and remain fixed during each
  pass. Moving over `32 m` alongshore or leaving the coastal band fades and
  releases vessels, audio and water slots before another local pass; camera
  movement does not control spawning. Two
  dedicated Blender variants use `0.42` presentation scale, slow staggered
  motion, the sea's wave pose and hull self-haze inside the unchanged `48 m`
  far plane. Warm working searchlights retain a two-thirds daytime floor;
  bounded moving water glints/wakes preserve the lighthouse's separate lamp.
  Two soft positional motor loops and one sparse shared horn belong to the
  vessels. They add no realtime Light, docking, interaction, navigation, map
  marker or story state; the station's boats remain ashore. Art-bible §10d,
  story-bible §6 and the accepted architecture exception define that scope;
- a reusable Cemetery non-urban profile on the default city's eastern edge
  (the church occupies the next `4 x 2` rows and the former lake block above
  that remains an open `4 x 4` north-east yard with sparse scrub),
  where the `3 x 2` cemetery is walkable ground;
  it requires one street-linked open-area approach and exposes the same data
  to world, fence, navigation, map and deterministic landmark consumers. The
  cemetery is dressed by its own dedicated plan: a textured gravel alley
  network from an arched, leaf-open iron gate, hash-varied graves in six
  monument silhouettes (stele, arched stone, Orthodox cross, obelisk, family
  monument, overgrown mound) over three stone tints with оградка enclosures
  on grounded corner posts and offerings, perimeter birches and firs,
  sittable timber benches beside the main alley at the cross alleys and its
  far end (they join the shared bench-sit pass through the world result's
  cemetery plan), and a chain of night-scaled gas lamps walking the whole
  main alley on alternating sides (a pair at the gate, one roughly every
  `15 m`, one at the far end); ground, alleys, mounds and stone carry
  dedicated `CityCemetery*Albedo` sheets. The watchman at the gate carries
  the game's one paid job, and it is four worked acts on a vacant plot —
  dig, lower, fill, set the stone — each a modal session that takes the
  camera down onto the grave, hides the hero and animates a procedural spade
  alone. He gives that job over and over: `CemeteryGravediggingRegister`
  hands out the next free plot outward from his post, up to three unfinished
  holes at once, and settles up for every closed grave in one sum. Nothing is
  committed until an act finishes, so every worksite in the yard stays a pure
  function of its own rung in the book of work (`CemeteryGraveWorkLedger`,
  one stage and one epitaph per plot). From the session's first sealed
  grave onward, two ordinary procedural ravens hold to the yard: one on
  the crown of that first mound, the other a few steps off on vacant
  ground, facing the grave. They shift their weight, preen, rarely give a
  dry synthesized caw, follow a passer-by with their heads, flush into the
  fog when the hero closes within arm's reach and settle back on the same
  two points once he is nearly out of sight; while a grave-work session
  runs they neither startle nor call. Sparse pairs of the same wintering
  ravens also hold open bird-logic spots across all three outdoor areas
  from the first day — always already perched, never arriving in frame and
  never anywhere the story touches;
- one deterministic Church precinct on the `4 x 2` area north of the cemetery.
  West street entrance/east altar, `10 m` setback and nominal exterior main body
  `23 x 44 x 32 m` at `.55` retain the neo-Gothic tower, spire, buttresses,
  lancets/rose, pitched roofs and Latin crosses. No facade realtime Light.
  `CityChurchCourtyardPlan` retains the full-width stone forecourt, `2.4 m`
  gravel loop, two bench pockets, two small trees, six shrubs and two beds.
  `ChurchGarden3D` supplies eleven passive donors: slatted/bolted benches,
  dressed fountain/ledge, Mary statue, hollow pots, hedge and shielded uplight.
  The fixed kit uses `14,632` triangles; seat datum `.49 m`, ledge `.65 m`.
  Stone/clay/timber shared response maps read City's rain clock without local
  drivers; identical albedo pixels retain their original PNG bytes.
  The `1.6 m` fountain retains shared basin/stream water and its quiet `4.5 m`
  voice, no light. The portable empty pot keeps its geometry/hand contacts and
  five Hero V2 actions; two shelf docks persist only within the session.
  `ChurchGardenBorderPlan` retains `28` `.8 m` hedge segments, rounded wings,
  `3.9 m` north opening and the existing east tree gap. Nine ground lights wash
  planting and one the statue: warm shadowless `35°` Spots, range `3.2 m`,
  real lenses/halos and day floor `2/3`. `CityNightSiteLightRegistry` owns them
  outside the twelve-Light pool; their finite bounce/volume exclusion remains.
  `CityChurchGroundPlan` keeps continuous sampled terrain, a flat inhabited
  garden and graded northern reserve. Paving replaces grass skin, with no
  coplanar overlaps; open iron closes genuine outer edges and west frontage
  outside the `8.8 m` entrance. `CityChurchCemeteryPassagePlan` retains one
  maintained `3 m` link from the cemetery middle cross alley to the south
  church path, with shared safe heights. West cemetery gates remain its sole
  street entrance; the internal link is no new access descriptor.
  `ChurchInterior` uses a separately loaded kit-authored metric shell:
  nominal `12.1 x 24.2 x 7.55 m` (measured top `7.529 m`, room collision
  `7.6 m`), shaped narthex/aisles/nave/apse/vault, four piers, six paired pew
  rows, two confessionals, font/votives,
  organ loft, high altar/tabernacle/crucifix behind an impassable rail.
  `church_metric_geometry.py` and the validated layout share actual openings,
  `.3 m` walls, `1.8 x 2.6 m` west door and exterior-metre correspondence.
  The west rose shares a real circular opening; its raised central ceiling
  pocket stays inside the exterior roof.
  hero/furniture are authored at ordinary size. `ChurchInteriorAtmosphere`
  reads model anchors for `16` warm practicals/`10` window glows and shafts.
  Shared effects bound bounce to four regions, air to two sources, halation to
  sixteen emitters and the floor/iron/brass environment to one filtered 256 cube.
  Physical response maps, Neutral/soft shadows, clear air and Gaussian DOF
  `5–13 m` retain quiet warm/cold layers. No church SSR/SSGI/SCGI; City's SSR
  stays road-only. No service/priest/bell/ritual/text/story reaction was added.
  Shared door actions use the physical opening/exact outside return. Model-owned
  threshold/lamp include hidden sloped support; projected sill/buried foundation
  extend the nominal main body. City/ChurchInterior load only their own payload;
- deterministic decoration gives each ordinary lot a distinct silhouette/facade;
  four urban/two park landmarks and optional frontage/roadside/park clusters.
  Its 24 visual families include chimneys/scaffolding/balconies/laundry/tanks/
  pipe racks/billboards/fire escapes/markets/discarded furniture/cargo/vending
  queues/legacy shelter/phone booths/roadworks/fountain-statue/bandstand/chess/
  playground. Only the two swings remain unbatched: hinged rigid seats hang
  from the top beam and move on hero contact. Random roadside excludes bus
  shelters; Route 01 owns target-derived physical stop poles. Ground frontage/
  roadside descriptors sample final XZ rendered terrain; geometry/collision/
  interaction docks share pavement heights, not lot datums;
- Residential pockets:≤four facade sites/depth≤`1.05 m`; six imports:Nardi/
  bicycle repair/basket-pulley/chair repair/sweeping/bench-planters. Full proxies
  yield to doors/access/POIs/collision/lines; entering static collision before
  wind dressing moves/omits intersecting wash. ≤five generic colliderless residents;
  bicycle(since `2026-09-16`)/basket/quiet pockets empty. No fringe Yard residents/
  text/interaction/light/audio/story reaction;
- Residential:one passive smoker candidate per ordinary house, lowest fog-readable
  balcony row. Session-local appearances around the moving hero prefer `12-22 m`
  across/ahead, ≤two active/one per house, distance release; hero home excluded.
  Roaming archetypes sample Hero V2 `SmokeLoop`/holds on a hidden native driver;
  all `31` canonical local bone channels/Blender cigarette-ember/shared-rig mouth
  exhale transfer. Home's bounded deterministic selection fits complete prototypes
  in its reconstructed half-space, enabled only in the Balcony shot.
  No collision/interaction/speech/sound/light/story state;
- one residual residential laundry (`CityLaundry{Plan,AssetProvider,Controller}`):
  fixed-metre `8.6x6.6m` L-kit/windows/front closed/side open;two drums/table/shelves/canopy/trolley.
  Permanent global worker:hinged cloth/basket `48s`,authored contacts/collision/free floor.
  Nonemissive enamel `2.4x.76m`:generated «ПРАЧЕЧНАЯ»/washer-cloth;
  UV/Clamp/sRGB/mips/BC7/1024/nonreadable;ArtSource LaundrySign prompt/SHA.
  Port tiles:plaster/concrete2m,metal1.5m,timber/fabric1m,Roof2m/Rubber;
  City albedo/slopes/roughness/cavity. Covered GroundAuthoredConcrete:CityClimate=false/dry.
  4 cached lens/halo centres↓165°/20°:warm work/neutral machines/cool canopy/front4m;
  no air beams.Pool12/Soft256/day floor/bounce4/air4/probes2/hum/shared profiles.
  Bounded time/pause/cleanup;static Home. `0-5`;no other text/service/economy/quest/reward/hint;
- `8x10m` residential dog park (`CityDogPark*`,`CityDogPose`,`CityDogActivity`):
  residual ground after laundry,park-first fitting;homes/POIs/routes retained.
  Fence/open1.6m entry/earth/bench/barrier;three distinct articulated dogs walk,
  sniff,play/sit;two permanent silent global owners,one seated. Local bounded
  collision/avoidance,calendar phases/30Hz poses/distance sleep;pause/disable/unload cleanup.
  Dog bark reuses village synthesis;shared soft paws.No new light/text/quest/story;
- one deterministic `city_misc_citywide_v4` mesh library at generator version
  `4.14.0` supplies the passive visuals for the broad City misc pass: `104`
  semantic kinds resolve to `145` assemblies, `305` role meshes and
  `57,218` triangles. It covers the 24-family
  decoration layer and park landmarks, street lamps and signal housings,
  Route 01 shelters/poles, the eastern yard, cemetery graves and vegetation,
    the church-yard surfaces and planting plus the modified cemetery
    north-fence posts and rails,
  seacoast hut/pier/esplanade joints, cemetery hut/gates, boats/barge/driftwood,
  fringe utility dressing, the static shells
  of all four district points of interest and the Nightlife arch-shelter kit,
  including its full-height masonry platform support and worn slab. Its former
  bar, supermarket and player-home shells remain in the catalog only for v4
  compatibility and are not instantiated. Role meshes, not world prefabs;
  plans own placement/terrain/collision/dynamics/interactions/realtime lights/
  halos/cloth/NPCs. Standing/seated/sleeping Nightlife shelter meshes are also
  compatibility-only, never instantiated. The live trio are staged `NpcHumanV2` adults on the
  Hero V2 Avatar, with three separate `256 px` detail atlases and an isolated
  three-loop idle bank. Fifteen shell, terrace, barrel, bedding and clutter
  renderers reuse measured City surface albedos through property blocks.
  Tilted cemetery monuments intentionally retain their legacy visual builder;
- four full-block semantic places: working bank waterworks court/cannery;
  residential drying yard/last-route island. Canonical descriptors reserve
  public ground/street access, excluding ordinary buildings/bars/home/primary
  landmarks; cannery hall keeps production out of its public passage/bypass.
  Each has a distinct silhouette/movement grammar; Home reconstructs nearby
  descriptors locally. The last-route island keeps its broken
  canopy ring but uses no emissive strips: the old departure board is visibly
  grounded on two supports and weathered route plates, layered posters, a
  waste bin, bottles, a discarded timetable and a lost scarf replace the
  repeated neon. Four scripted opaque POI albedos from
  `tools/build-city-poi-textures.py` (yard paving slabs, painted metal,
  laundry cloth, worn timber) ride the shared `RuntimePrimitiveLit`
  through `CityPointOfInterestSurfaceAppearance` property blocks with the
  same linear albedo compensation as the facade/home/supermarket sheets:
  all four public grounds are paved, and the drying yard's frames, lines,
  hanging simulated wash (which keeps its shared two-sided cloth material
  and matte specular), bench and fixtures are fully textured. The drying
  yard also carries the one authored POI realtime light: a communal
  floodlight on its own `4.3 m` pole at the street-side corner opposite
  the shared bench, washing all three drying frames with a cold
  near-white shadowless `72°` Spot (range `16`, night intensity `150` — the
  7-12 m throw needs floodlight wattage for the far row to reach
  street-lamp brightness through the night grade and fog) plus a
  fog halo and a dead-by-day emissive lens; `CityNightSiteLightRegistry`
  scales and disables it with the shared night factor, its lower pole
  owns a focused collider outside every access approach, and the Home
  vista rebuilds only the pole/head/lens geometry without a light. On the
  yard's west strip, upwind of the wash, stands the Soviet carpet-beating
  rack — painted-metal posts and crossbar with two hung carpets textured
  by the shared Home rug albedo. In the city each carpet is real
  simulated cloth pinned over the bar (heavy: stiff and damped,
  outside the laundry's weather-wind registry; the vista
  keeps static boxes), and the yard's three authored staged NPCs work
  around it: two babushka grandmothers beat those carpets from opposite
  sides with the forward-biased bright plastic beater (one shared
  `yard_babushka_v1` model, desynchronized `BabushkaBeat` loops at
  different speeds and phases), each strike driving a short
  deterministic acceleration pulse through exactly the carpet she
  faces via `CityDryingYardCarpetRegistry`; the third strolls a
  cloth-free corridor between the rack and the drying frames back and
  forth past the beaters at `0.36 m/s`, gesturing emphatically on the
  four-step `4 s` `BabushkaSmoke` loop with one cigarette drag per
  lap, turning smoothly at the corridor ends. They follow the rider's
  staged contract: authored with the shared pedestrian art library,
  outside Resources and the pool, colliderless with attention magnets,
  spawned from the same POI descriptor transform as the drawn carpets
  so they land inside the drying yard of any generated city. These
  authored recipes require both
  lot dimensions to meet
  `CityLayoutGenerator.MinimumDistrictPointLotDimension` (`18 m`); smaller
  custom blocks omit the district POIs safely;
- city-wide wind dressing: one cross-zone plan
  (`CityWindDressing{Plan,Planner,Validator,WorldBuilder}`) hangs up to `64`
  simulated cloth and rope pieces off structures the other dressers already
  draw — torn awning rags on up to four of Old Town's market valances (two
  per stall), grey shrouds on up to three scaffoldings' outer guard rails,
  up to six residential courtyard drying lines on their own drawn poles and
  parabola-sagged rope (`CityRopeSpanGeometry`, walk-through wash in the
  cloth body registry, lines 18 m apart), dark tarpaulin curtains and sling
  ends on up to four industrial pipe racks' street-side ties (the rooftop
  gantry is skipped — its beam runs tens of metres up the landmark tower,
  where cloth is sub-pixel), eight faded fire-escape banners and two rope
  ends in Nightlife (no billboard skirts — every nightlife billboard rides
  a ~50 m tower), exactly one
  remnant pennant on the park bandstand, net rags on the pier rail and
  tarred mooring ends on the slipway chain (the pier head's fisherman
  composition stays clear), two dark wreath ribbons on cemetery enclosure
  posts (offering graves first), service tarps on the fringe repair gantry
  and utility shed plus dead cable tails off the crossarms. Anchors are
  picked by stride across each district's full anchor list, and a
  street-level per-district floor is test-pinned so the dressing meets an
  ordinary walk instead of clustering once per city. Every piece is
  a `ClothPanelFactory` panel on the weather-wind registry; rope-width
  strips (`<= 0.12 m`) keep the factory's flat colour while wider pieces
  ride the shared POI cloth sheet; only line poles collide (batched
  collider). Residential lines use the free frontage bay opposite their
  furniture anchor and are omitted when that complete corridor would meet a
  blocking decoration, leave the block or crowd the entrance. The bar-side
  yard, lighthouse island, drained-lake block,
  tunnel forecourt, flood works and stone terraces hang nothing by
  authored rule;
- frontage-aware windows and facade details now face each lot's actual road.
  Decoration geometry is shadowless, reuses the two packaged shared materials
  and combines at most six style batches per `48 m` chunk. A deterministic
  `None`/`Detail`/`Blocking` catalog gives grounded structural and bulky
  recipes one to four simple box proxies, while rooftop, hanging and small
  narrative details stay non-physical. Park benches/hedges, the home mailbox
  and lower lamp/signal poles also own focused proxies. The bounded Home
  balcony exterior rebuilds the same descriptors in Home-local space but
  remains collision-free;
- rendered streets, park paths, lawn and plaza own matching static colliders,
  so the existing `0.28 m` controller step climbs their real height changes
  instead of letting the character mesh intersect raised surfaces;
- ordinary Blender models: five Residential forms, `3–5` storeys,
  heights `13.4/15.8/12.8/15.8/10 m`. Every body/wing has an open `1.4×2.4 m`
  entrance/`1.8 m` vestibule: measured threshold, split collision/skirt, real floor.
  Cached `CityResidentialEntrancePlan` owns protected graded paving→public
  sidewalk; City/Home/map share it, without new interactions or apartments.
  Industrial eight functions, heights `8.6/7.8/8.4/7.8/6/8.4/6.6/7.8 m`:
  dry/cold storage, repair/metal/packaging, boiler/compressor, repair garage.
  Equipment owns roofs/gates/recesses; timber/concrete/steel retain response.
  `IndustrialTaskLight_*` uses the twelve-Light pool/halos/day floor.
  Twenty original IDs plus three Industrial IDs; fronts≤15.5m,L15×14m,wing10×6m;
  work passage3.6×4.2m. Measured roles/Blender metres/+Z front align to
  `DoorPosition+0.08m`. Home/shop/bar retain heights. Unique facade/plinth sides,
  metric equipment/roof/metal/frame,pane-local glass; warm day emission kept.
  Trim/skirts avoid coplanarity; Home shares pose, crossings alone clip silhouettes;
- the bar now uses the complete fixed-metre `bar_exterior_v2` from the shared
  bar Blender pipeline instead of the City misc `BarBuildingShell` and generic
  window bands. Its `12.2645 x 13.5237 x 9.3435 m` measured envelope is unchanged;
  the rear corner of the existing service wing is rebuilt/lowered for the WC.
  It is a two-storey late-Victorian neighbourhood pub:
  brick/faded render, pitched slate/unequal chimneys/low wing, green/oxblood
  faceted front, sash windows, gutters/downpipes and tankard. Timber cheeks,
  recessed bay flank panels/full-depth jambs hide the empty shell at oblique angles.
  Unit-scale placement keeps door/sign anchors. Unity owns the `0.08 m` front/side-inset, box-projected
  `ExteriorBrick` foundation skirt, renderer-free full-size logical collider,
  entrance apron, trigger,
  transition and existing bar light. A fully visible Home reconstruction reuses
  the same complete collider-free model; only a pub crossing the apartment
  half-space keeps the clipped legacy silhouette;
- the fixed-metre `supermarket_exterior_v1` replaces City misc shell, apartment
  window bands and runtime-box storefront. The passive `15.5 x 15.5 x 6.4 m`
  neighbourhood-store body owns dark brick piers, a recessed double entrance,
  four framed glazing bays, a `9.2 m` canopy, integrated original
  cream/ochre/green/burgundy fascia, the authored `ПРОДУКТЫ` sign, service
  elevations, parapet and low roof plant. Four dedicated sheets split unique
  wall/fascia atlases from physically repeated brick/metal; roof and warm
  supermarket glazing reuse shared families. Unity aligns `exterior_door` to
  the lot door. City mass surrounds a rear receiving cavity with solid
  jambs/header/floor/ceiling; the local trolley parks inside the service door.
  Apron `4.8 m`, trigger, transition, side-wall yard spotlight and `0.14 m` inset
  skirt remain. Full Home uses the collider-free model; half-space crossings
  alone use the clipped fallback. Old City misc shell: compatibility;
  shared collision builders/`SupermarketInterior` unchanged;
- the player home uses complete passive `player_home_exterior_v1`, a
  fixed-metre interpretation of the restrained Georgian Series 209-1 type.
  Its `13 x 12 x 8.8 m` body owns repaired cold stucco, a brick plinth, pitched
  slate roof, irregular framed openings, recessed entry and supported upper
  gallery. The canonical deck projects `2.3 m` past the door plane, so visual
  bounds extend from local `-Z 6 m` to `+Z 8.3 m` while the lot, inset
  `0.08 m` foundation and renderer-free logical collider remain body-sized.
  Forty-seven semantic meshes bind nine dedicated sheets through authored or
  metre-scaled UVs; competing opaque layers keep at least `0.03 m` clearance.
  Exactly the upper street window immediately left of the balcony is
  emissive, and every other pane is dark. City aligns `exterior_door` to the
  unchanged lot door and retains the walkway, mailbox, lamp, number `7`,
  beacon, trigger and transition. Home reconstructs the same materials, exact
  visible window positions, recessed entry and outward physical balcony, but
  omits the narrow street-only front eave fascia that crossed its fixed camera
  as a foreground beam; the pitched roof edge remains;
  the old City misc shell remains compatibility-only;
- deterministic ochre guard rails, batched into `48 m` spatial chunks, only
  where a street faces water, unmapped space or the active map boundary, plus
  full-width caps at true degree-one street dead ends. Street-to-park
  continuations count as connected and are not capped. Rails are physical;
  their narrow posts remain visual-only, and all former entrance/gate/public
  opening descriptors remain available as decoration-clearance metadata;
- player navigation includes complete logical `BuildableGround` regions
  plus existing streets, park and `OpenLand`. Radius-safe seams connect
  road-to-ground and adjacent ground cells for the maximum `0.35 m` agent;
  water, unmapped cells and outside space stay excluded, while real building,
  prop, vegetation, pole, fence and pedestrian colliders decide local
  obstruction;
- 144 land-use lots in the default urban core, including 16 park cells and
  4 open semantic places, plus 34 northern beach/water surface
  cells, 16 north-east yard cells and 6 cemetery cells. The core contains
  exactly one reachable Residential bar at `(12,6)`, one non-bar player home
  across its shared street frontage at `(12,5)`, and exactly one ordinary
  street-front supermarket. The retained bar keeps stable ID
  `bar-01352777-12-06` and `SplitTheG` activity assignment. Four semantic
  rooftop/facade landmarks survive independently of the two districts. Custom
  generated layouts may still request multiple graph-separated bars;
- a default `8.8 m` player-home body with a recognizable supported upper
  gallery, open balcony door and one lit window to its street-left; City and
  Home share the same `4.7 m` floor, door/window positions and `2.3 m` outward
  deck geometry;
- when the opening route first reaches the City, the hero starts on the road
  node beside the deterministic player home and its neighboring bar, `13 m`
  from their shared street approach under default spacing; custom-layout
  fallback placement remains bounded to `48 m` by traversable street distance,
  and returning from a bar, home or supermarket interior restores that
  entrance's own sidewalk arrival point rather than the road centerline;
- diegetic bar identification through warm windows, framed entrances and
  shared camera-facing pixel mug signs;

### The hero

- sole `Resources/Player/Player3DV2` in ten roots;arms/portrait.
  Lean `1.75 m`:31 body bones/55 actions/tapered torso/male hips-two buttocks/
  slim arms/refined hands. Loose M-65/right patch; separate trousers/knee folds,
  burgundy1460 boots/ankle-ball flex.
  `PlayerWardrobe`:shirt-jacket-trousers-boots-belt/coverage/atomic slots/leases.
  `hero_field_workwear` only; outfits/UI/save Deferred. Curtains12 bones;
  Jacket:gameplay rig-only;elbow folds/hand-opening;0live/baked cloth.
  Editor authoring/scarf-sling policy stay;garment folds/joint volume.
  Hair:bounded motion/contacts/pause-reset/mirrors;arms copy jacket;hair→scarf.
  Lean `40/60`; `ai/player-art-spec.md`;
- one manual PlayableGraph presentation that damp-blends the in-place
  `Idle` (4 s), `Walk` (1 s), `Run` (.75 s/18 frames) and snow clips from
  actual constrained speed. Idle breathes/shifts weight; Walk articulates
  contact/down/passing/up, elbows/knees/ankles. Heavy Run loads forward, swings
  arms, lifts knees and briefly flies. `SnowWalk`/`SnowWalkBackward` (2 s)
  extract bent legs high with long support, short steps and weight transfer;
  phase persists, cadence follows `1.10 m` per cycle, cold torso blends to 25%.
  Start/stop use `.14/.20 s` envelopes; cadence follows weight. No root motion;
  the face atlas drives neutral, half/closed blink, watchful and tense states.
  A failed balance check may temporarily suspend
  this graph while the
  same registered bones are owned by the bounded ragdoll. Intoxication sway,
  arm spread, knee bend and balance lean are
  additive rotational/limb poses, preserve the authored pelvis position in
  the actor ground plane and reset through the same lifecycle cleanup. After
  the ordinary and additive pose is sampled, reused CPU bake buffers measure
  the actual deformed vertices of the registered foot meshes, then offset only
  the pelvis vertically so the lower visible sole stays at its neutral grounded
  height; the physical player root, model root and contextual clips remain
  untouched. Run weight progressively releases downward grounding, and at full
  Run it only lifts sole penetration instead of pulling both airborne boots
  down to the floor;
- one shared source-scene action for all ten ordinary bar, supermarket,
  home, stairwell and mother's-house location doors. After the existing interact command, the
  constrained motor guides the visible hero to an explicit grounded dock and
  facing, holds a neutral frame, then plays the planted
  `DoorUseEnter/Loop/Exit` lean and short physical-right-hand press. Only the
  terminal neutral completion may request the separate scene transition. The
  City supermarket entrance alone opts into a calculated `0.242 m` initial
  vertical tolerance covering the complete road/curb/graded prompt reach;
  every other door retains the common `0.02 m` default;
- ordinary URP mesh shadows cast from the real character geometry in every
  gameplay root, plus one small light-independent analytic contact patch fixed
  to the grounded player root. The patch follows foot plant and expands,
  rotates and offsets for left/right falls without moving the physical root;
- road-constrained tank motion: `W`→hero-forward `2.6 m/s`;
  Shift/L3+forward→`4.2 m/s`; `S`: backpedal `1.4 m/s`;
  `A`/`D`: step-turn clips/arcs, yaw in place `150°/s`.
  Ground accel/brake `6.5/11 m/s²`;coasts;modal/transition/teleport stop;
  Blocked motion0. Air:world-space inertia/no release brake/limited
  steering/gravity24. Camera-plane only in CombatTest aim.
  Intoxication scales walk/run; fatigue no debuff; scripted approaches walk;
- City/Bar/Supermarket:third-person chase `1.9 m` ext/int;
  FOV `53°/57°`,focus `1.6/1.5 m`,hero low.
  Camera-right `.5 m` matches free aim;aim `1.4 m` back/FOV kept.
  Free/stable gun origin:shoulder+`.4 m`;melee lock+`.2 m`.
  Stable gun lock:same distance/handoff;side/back collision/dolly align.
  Direct root travel; focus overrides damp/cap `.45 m`.
  Damped yaw/pitch `-20°..55°`;teleport snap/idle-walk motion.
  Collision:immediate contraction/smooth return. Mouse (no button), right
  stick/arrows orbit, including seats; arrows:per-second;
  WASD walks. RMB:firearm aim; park chess owns arrows.
  Cursor:locked/hidden; Alt releases/shows, stops mouse look/combat; return
  needs fresh presses. Menus free cursor; focus return drops delta.
  `GameCursor` leases; authored cameras keep control. Fullscreen modals
  fade cinematics; balance keeps intoxication/falls;
- Home keeps its three authored fixed shots and now protects the player from
  foreground occlusion through one explicit registry of logical furniture,
  dressing, door and balcony-rail groups. Five 3D presentation samples cover
  the head, both sides of the chest, pelvis and feet; the first four drive
  reveal decisions, while low objects may still hide the feet naturally.
  Blocking groups fade to their authored `0.15-0.23` visibility floors through
  one shared opaque alpha-clip dither material, with a `0.15 s` fade,
  `0.12 s` clear hold and `0.30 s` restore. The material retains Forward+
  practical lights, cookies, shadows and SSAO. The system never changes colliders,
  glass, lights or the room shell, and restores full opacity while the opening,
  refrigerator or animated Home interactions own presentation;
- one percentage-driven intoxication profile shared by City, BarInterior,
  SupermarketInterior, HomeInterior and StairwellInterior:
  `1–20` Light Buzz / «Лёгкий хмель», `21–40` Tipsy / «Навеселе»,
  `41–60` Drunk / «Подшофе», `61–80` Unsteady / «Шатает» and `81–100`
  Very Drunk / «В стельку»; `0` is Sober and hides the HUD. Free gameplay
  continuously lowers the session level on unscaled time, from about `12 s`
  per point at `100` to `3 s` per point near sober; modal interactions pause
  this recovery so their committed drinking snapshots remain authoritative;
- continuously escalating movement slowdown, 3D bone sway, arm spread, knee
  bend, camera roll and world-image distortion within those ranges, with the
  HUD rendered as five independently filling 20-point segments; presentation
  eases toward a changed level over about `0.7 s`;
- during vomiting, the head and neck add `24°` of downward pitch to the
  unchanged folded-torso pose. The source stays at the actual mouth; its launch turns
  up to `25°` toward the hero's forward with full-burst speed `2.8–3.4 m/s`
  and weak-burst speed `1.8–2.3 m/s`, giving the jet an initial forward
  impulse before gravity brings it down;
- exponential VHS-like processing of existing music, ambience, SFX and their
  reverb/echo returns, with dry UI and exact sober bypass. The shared stereo
  transport wanders, flutters, drops out briefly and chews fragments most
  strongly at `81-100` (`0.4-1 s` episodes every `2-4 s` at maximum), adding no
  voice, noise bed or world source. Native audio smoothing eases strength
  changes, episode boundaries and repetition seams; sobering fades back to
  exact dry sound without changing world tempo or visuals.
  Shared gameplay speech stays audible: smoothed alcohol level drives at most
  `-6 dB` gain and a `7600→1800 Hz` low-pass. Speech/music playback reaches
  `0.25×` at maximum over existing VHS breakup; native DSP stays unchanged.
  Speech text/mouth follow slowed PCM within the original real window, leaving
  unheard fragments unrevealed. All spoken captions reuse the hero's peak glyph
  scatter with cached wrapped rows. Sobriety restores; menu auditions stay dry.
  `IntoxicationPerceptionRules` maps the
  alcohol percentage through `A = (exp(4.5 L/100)-1)/(exp(4.5)-1)`; world
  motion follows `1-0.12 A`, bounded at `0.88`. The persistent
  `GameTimeScaleRuntime` owns the status/world ramp and a separate persistent
  audio level: exponential easing with `0.7 s` unpaused real-time constant,
  retained across player/scene rebinds. VHS, music and speech use its cached
  profile; explicit synchronization/session reset restore exact values.
  It composes pause leases and a matching
  positive physics step. Physical bar service and fall/rise/crawl share world time with the
  bodies; intoxication leaves calendar, needs and recovery at their intended
  real-time rates. Explicit debug speed multiplies world/calendar progression;
  UI/input keep real-time response, true pause freezes progression;
- above `60` the hero is balanced by a continuous seeded model rather than a
  check: the drink pushes his centre of mass, the ankles chase it late, the
  torso and arms whip to buy ground, the boots step to catch it, and A/D
  pressed toward the lean is the recovery. When no ordinary step can reach
  where the capture point is going he topples — the root goes with him, one or
  two lunges are thrown at where the point will be, the hands go out for the
  ground — and either a lunge catches him or he goes down;
- a lost topple hands the bracing pose and its motion about the supporting boot
  to a 14-body runtime ragdoll. The pelvis is tethered within `0.68 m` of the
  root;lower-spine/chest joints share `18 kg` as `8/10 kg`.
  Every ragdoll collider ignores its jointed neighbor and the upright
  `CharacterController`, while other non-overlapping body pairs collide, and the expanded
  analytic contact shadow follows the fall. Combat hero:separate planar/vertical
  caps;freefall gravity24/lateral inertia;NPC kept. He lies until the
  ragdoll is still and, drunk, a while longer; then the capsule is brought
  under him and turned to match how he lies. Calibrated chest/pelvis fronts
  against the probed floor distinguish stomach from back; a side landing
  compares the required rotation and reachable supports. The selected route
  stays fixed during the attempt, independently of the lead side. Stomach
  goes through `RiseLeft/Right` onto all fours; back goes through
  `RiseSeatedLeft/Right` into a supported seated pause. Both can slump, bring
  a boot under the body and rise with a hand on the knee. Held movement keeps
  the existing crawl; from sitting it first completes the visible
  `RiseSeatedToCrawlLeft/Right` transfer. A seeded model scrubs the authored
  actions, eases internal support changes and retains the terminal wobble.
  Final pose transitions run after the clip and limb solve, carrying the last
  visible pose and velocity through a saved topple and back into the stagger.
  The frozen ragdoll also blends against that complete solved pose, so the
  hands cannot reach their new pose before the chest. Completion and owned
  cleanup restore the graph, kinematic bodies, disabled ragdoll colliders and
  ordinary contact shadow. Dense source validation and the focused ten-sequence
  runtime capture verify the `2026-09-06` recovery change; see the work log;

### Map, transitions and shared interaction

- City map:full-screen blueprint-centred;cell labels/area colours. Park/beach/sea/river/promenade/cemetery surfaces
  and paths; cemetery–church passage cuts both outlines without street gate/
  teleport anchor. Coast:mol/pier/hut/piles/barge,lighthouse dot on the north
  border at true easting; Works/Mouth/timber bridges have distinct treatments.
  Player/home-bar/home/grocery markers;twelve POI glyphs (four lots,
  fair/post/docks/arch/church/cafe/laundry/dog yard),bounded localized legend/
  hover names. Public lots/fair use open ground,not buildings;locations come
  from validated memoised plans/port.
  Default ImageGen `UI/Maps`:City charcoal/Silent Hill;Village worn tourist
  brochure/red-crossed resort.
  LodgeApproach arrival;no3D change. Plan/RU SHA+bounds/size guard
  stale art;other seeds/MountainRoad→schematic. City SHA excludes Village
  keys;Village mapUvRect separates map/nonXYZ ad. Cached clamped nonreadable
  bitmap≤2K/area. Bounded RU cache:contrast plates/leaders/collision/clip;
  no anonymous yards/plate coords;named POIs/click→XYZ inspector;
  player/POIs/bus/selection/XYZ/arrivals live. Authored edits/new objects→redraw;
  live motion0.
  It consumes `CityWorldResult.MountainBoundaryPlan`,
  expands display bounds only west and south, including the visible cave
  approach and the first `12 m` of tunnel, and draws each ridge as a
  toe-to-outer-foot hatch carrying only the visible narrow river approach
  into its mountain mouth. The hidden cave continuation is not drawn as open
  map space. The open portal uses a fixed `19 x 17` uncrossed arch marker
  with a localized hover label; outside the scrolling viewport the marker
  clamps to the visible edge as a direction indicator. North and east keep
  the layout's original map maxima. It also draws the canonical Route 01 loop
  as a pale neutral line, adds
  five numbered stop markers in the default layout with localized hover
  labels and keeps both symbols in a compact legend. The map has no live bus
  marker. City and Mountain Road are separate schematics under the area tabs;
  the player marker is drawn only on the current area's tab. An observational
  `XYZ` mode opens on both tabs through the panel button, keyboard `C` or
  gamepad north/`Y`. A click selects the point under the pointer; Left/Right
  or D-pad cycle the complete active-area catalog, persistently highlight the
  selection and centre keyboard-picked points. The side panel gives the
  localized point name, its area and invariant world `X/Y/Z` to one decimal
  place.
  City's catalog owns every `BuildingLot`, every open-area arrival, every bus
  stop, the current player, the city mountain-tunnel portal, the boat station
  hut and the fair, eastern post, docks, arch and church door. Special lots
  replace their generic lot: the bar exposes `ReturnPosition`, home and
  supermarket `Center`, each district POI its authored position. Mountain
  Road owns the current
  player, tunnel, every authored hairpin apex, bridge centre, plateau endpoint,
  cableway and brink. City also owns its cafe POI. Road polylines, intermediate route samples
  and mountain hatches remain decorative. Point inspection is mutually
  exclusive with debug teleport and suppresses area travel and
  teleport confirmation until the player exits `XYZ` mode. Confirming the
  other area requests a map arrival, Single-loads `AreaLoading`, then the
  destination. Its overlay holds until construction ends: 20% of the bar for
  the load, 80% for the build. The source unloads before the destination
  composes: two exteriors are never resident together. Interior doors keep
  the City or village dormant and wake it; the City map's house door is
  Single. Mountain Road is drawn as its exit tunnel, complete winding route,
  all ten authored hairpins, distinct mountain bridge, enlarged endpoint
  terminal and surrounding mountain hatch. Hairpins and bridge come from the
  same pure route plan used by the world. The terminal plan also supplies
  distinct localized cableway and brink landmarks; the map does not
  infer them from runtime GameObjects. The physical terminal
  keeps a clear `7.5 m` vehicle circle on its irregular roughly `42 x 27 m`
  plateau. City owns the relocated five-sided Nighthawks-inspired cafe on one
  reserved lot: candidate `[10;0]`, fallback `[9..10;0..2]`. Its level support
  joins the existing sidewalk, retaining road widths/routes and open foreground
  at the glass. It is enterable without scene loading; its linoleum floor
  collider remains `4 mm` over support. Seven threshold props and the hero
  counter seat use the cafe's transformed frame; shelter/map/XYZ/debug arrival
  share the plan. There is no MountainRoad copy. Its lone patron,
  neighbouring couple and attendant are four dedicated staged models, not
  pedestrian-pool substitutes. Its fixed-metre Blender set has `61` meshes /
  `5,794` triangles, `52` semantic anchors and seven dynamic prop assemblies.
  The rear service wall reads as one kitchen run: a long cabinet and worktop
  with `CuttingBoardDock`, a compact stove and pan at `StovePanDock`, and a
  refrigerator cavity with two shelves.
  `FridgeDoor` is the sixth dynamic prop, rooted at the authored
  `FridgeDoorPivot` with child `Grip.FridgeDoor`; it ships closed and has no
  runtime driver, Animator, Rigidbody or attendant/player interaction. The
  napkin dispenser, sugar and salt shakers sit clear of the hero's counter
  place and the menu handoff area. The seventh prop is a thin open
  `Menu.Hero`, with its own hand grip, counter dock and three item/one
  selection anchors; it begins hidden until the cafe menu runtime presents
  it. At runtime its `menu_pages` role uses plain warm paper rather than the
  green-banded shared props sheet, and the TMP face and glyph direction are
  resolved against the focus camera.
  Seven stools follow the main counter and return with their seat tops at
  `0.8175 m`: three are occupied with real butt contact and four remain empty.
  The hero may take the middle main-row gap; its dock remains in the aisle while
  its independent facing now looks at the counter. The lone patron nearest the
  entrance sleeps with his head on strongly crossed forearms, visibly stacked
  one above the other without intersecting, on the counter. He owns no
  cup, never drinks and never enters the attendant's service queue. Only the
  pair's two cups visibly drain during their Drink clips. Both handles face the
  side opposite the earlier build; each refitted Grip stays exactly on the
  animated hand while the open rim tips toward the owner's live mouth socket,
  independent of imported bone axes, then returns exactly to the centre of its
  own saucer. The attendant's Wipe reaches the real
  counter surface, while the complete right hand and pot stay clear through
  carry, lift and action blends. During Pour, separate runtime geometry is
  rebuilt every frame from the animated pot spout to the active cup's
  `PourTarget`; the stream is not baked into any clip. A pure clock arms on the
  player's first entry into the cafe's `16 m` entrance radius. Role-local windows and distinct fill/consumption
  profiles and non-overlapping windows keep the pair out of lockstep while the
  first visible sip still crosses the refill threshold within one minute. The
  attendant wipes between episodes, then walks and pours when either of those
  two cups crosses that threshold. Completing
  the hero-stool sit switches to a bounded eye-level first-person view of the
  counter and restores the exact prior follow-camera state on exit. It also
  requests one silent attendant handoff: `MountainRoadCafeMenuController`
  advances its pure model from delivery to open only after the physical menu
  reaches `MenuDock.Hero`, while `MountainRoadCafeMenuPresentation` keeps the
  booklet and its localized TMP text in world space. When
  `HeroMenuPlaced` becomes true, the existing `MountainRoadCafeSeatView`
  blends the fixed camera over `0.45 s` along the current seated sight line
  to a `40`-degree view `0.50 m` from the page. The close-up preserves
  world-up without roll and suppresses every look source. `W/S` (or the D-pad)
  still wraps through the three items and `Space`/gamepad West records the
  selection-only confirmation. Confirmation or the first
  `E`/`Enter`/gamepad South action drives a `0.40 s` opaque hinge fold from
  the spread into a thin booklet resting on the counter and releases focus
  back to the saved seated view;
  neither stands the hero or requests retrieval. Looking at that booklet makes
  the same interaction physically unfold it over the same interval, while
  looking away makes it request the authored stand-up. Only after that visible
  exit completes does the shared
  attendant timeline serialize the return as `WalkToMenu -> TakeMenu`
  (`2.5 s`) `-> CarryMenuBack`; the closed booklet remains at the counter until
  the physical take, follows the hand and is hidden at its service dock. A
  later completed sit resets the round trip and receives the menu again. The
  cycle creates
  no order, product, payment, inventory item, food, drink, dialogue, reaction,
  sound or story state. The
  ten-clip cast (`2` lone-patron clips, `2 + 2` pair clips, `4` attendant
  clips) has no NPC voice bed; the hero never enters its pair-only cup/refill
  queue. Only while the player is
  inside the plan's physical cafe volume, the pair follows the fixed localized
  text order `Man01 -> Woman01 -> ... -> Man10 -> Woman10 -> loop`; each role
  owns ten stable keys in both Russian and English. A queued turn survives
  either patron's Drink and the woman's cigarette lift/smoke window without
  consuming or skipping its key, then resumes after the authored action. One over-head bubble
  belongs to the active speaker, whose additive neck/head look turns toward the
  partner before the line appears. A PairMan -> PairWoman exchange completes
  only when both lines have been fully displayed. After every third completed
  exchange (`3/6/9...`, continuously across the ten-pair pool wrap), and once
  both mutual looks have returned to idle, the lone patron — the
  woman's strongly drunk husband — raises his head, waves his right hand toward
  the pair, shows one line from a separate four-key RU/EN pool and returns to
  the exact sleeping seam. The pair completely ignores him: it receives no
  look, answer or reaction gesture, and its pending ManNN key is unchanged
  (`Man04` after `Woman03`, `Man01` after `Woman10`).
  The attendant has no lines. The pair's default seated idles remain legible around the conversation:
  the man makes
  three uneven left-hand taps with no impact sound and may continue tapping
  beneath his text, while the woman takes one cigarette drag and settles into a
  restrained exhale. The authored ember glow follows the drag; a separate
  world-space plume follows `SOCKET_Mouth` through the exhale window. Both read
  the same live normalized idle phase and own no separate timer, Light or
  AudioSource. The bubbles likewise add no voice or
  other AudioSource. Her fingers hold the filter rather than the ember; the
  filter still reaches the mouth and the burning tip points clear of hand and
  face. The plan owns entry, shelter, anchors, lighting and exactly `17`
  logical colliders. The two visible practicals and one shadowless technical
  wash are redistributed, not multiplied: the warm key reaches the sleeping
  husband, while
  `Light.ColdService` now starts inside the visible task fixture over the stove
  with a cold emissive lens. Its widened bisector cone contains the stove and
  pan task surface as well as all four figures, where it works with the wash to
  keep the tableau readable. These remain the cafe's three runtime Lights and
  stay local to the cafe and near foreground. `MountainRoadSurfaceLibrary`
  owns 15 pigment/physical families; palette/metre UVs stay. Cafe
  unique UV0 uses 4K colour/2K response, UV1 intact. Signed
  slopes/roughness/cavity differ from pigment; ground keeps format,
  UV4/wind. BC7≤80MiB includes auxiliary maps. Glass/emission/menu/water/
  cast/car stay; no new text/brand/price/background.
  The four cast members also use role-specific `256 px` detail atlases and
  curved/multi-segment geometry at the current Hero V2 fidelity level; these
  atlases cover face, clothing, hair/headwear and shoes through the shared
  player lit material rather than adding per-instance materials.
  On the right, a `230 m` continuously looping cableway moves eight
  colliderless cabins over nine supports while its upper return remains beyond
  the Mountain Road draw range. The lower station retains its weather shelter;
  the cafe's shelter is installed in City. The remaining mountain pad is a
  bounded dressed transfer yard on existing sheets/tints: a ploughed snow bank
  and grit bin, the last road board and a seized barrier, a freight dock with one
  abandoned suitcase, and a `0.66 m` retaining wall whose two three-riser
  flights climb to a back terrace. A `1.02 m` parapet closes that terrace
  `0.35 m` inside the rim, carrying a sittable bench, a survey pillar, a
  memorial plate and a windsock mast, with one chained gap. Past it the
  terrain is cut `26 m` down through a `-27` degree wedge — the one sector
  the ridges leave clear inside the `120 m` far plane, so none is moved and
  both flanking masses become its jambs — and a fixed matte at `81-105 m` on
  the lighthouse island's shaders shows the valley bed, the switchback he
  climbed and a grain of city, all measured from the tunnel mouth's own
  height and lit after dark with no Light at all. One mercury practical burns
  over the freight dock and the brink stays dark. The Ferryman answers on the
  summit from a second repertoire that offers nothing. While driving, ten
  ordinary road lines use shared bubbles/clicks, turning his head to the passenger.
  The shuffle persists across scenes, with no round/seam repeats. First line:
  `12–18 s` of travel; later: `30–45 s` after typing plus the `2.5 s` reading hold,
  at most five per trip. Stop/pause freeze delivery; fades/skip discard the line,
  with no backlog. Passenger look/dashboard input remain available. The glovebox
  combines `107` idle-toss octagonal brass coins (`54 x 9 mm`) in one shared-material
  mesh, no physics/pickup; generation checks the closed compartment/bulb clearance.
  Opening the lid gives a short ownership remark; after `2 s` the driver
  leans and reaches with the nearer hand. Its palm meets the lid's underside;
  a low elbow and fingers following the forearm preserve the whole arm through
  the closing push. Contact starts closure,
  then the hand returns to the wheel. The bulb stays in the niche and goes out with
  the lid.
  At either end of that road he refuses to drive a hero on the last
  two drunkenness stages: the menu opens, the small talk is untouched, and
  the second option answers «Не в таком виде. Я подожду.» and closes. With the
  test teleport enabled through the City F9 toggle or the Home debug-map arrival,
  lots require side-panel confirmation. Lot and XYZ arrivals
  share obstacle/ground checks: land Y is re-sampled, occupied markers use the
  nearest clear street. Building/church-yard footprints include capsule radius;
  authored decks retain their height where the terrain sampler has no surface.
  Keep at least `22` logical pixels per map cell; clip overflowing content and
  pan it independently on X/Y with WASD, the right stick, mouse-wheel gestures
  or middle/right-button dragging while drawing scroll indicators only for
  overflowing axes;
- localized RU/EN interaction prompts whose pointer, keyboard and gamepad
  activation share one action path;
- guarded asynchronous transitions and persistent blueprint ID,
  seed/bar context for the current city, with an explicit
  bar/home/supermarket/church return
  kind, a separate stairwell-arrival side and a consumed
  `Normal`/`OpeningSleep` Home arrival value and a resettable one-shot
  `DebugCityMapOnArrivalRequested` flag. Every completed exterior building
  exit resolves a `PlayerDoorArrivalPose` from the destination door's own
  doorward action axis: the hero faces along its opposite before the chase
  camera initializes, so the leaf is behind him and the first outdoor frame
  is already an over-shoulder view. This covers bar, home, supermarket,
  church and the mother's house without encoding geometry in transition enums;
- a dedicated `1.6 s` `DoorTransition` scene after that source-scene gesture:
  an unscaled fixed-camera handle/door sequence opens the leaf outward toward
  the camera against a solid black doorway while the destination preloads,
  then activates only after the final blackout;
- one deterministic runtime-composed `8.6 x 9.6 x 6.25 m`
  `StairwellInterior` between the exterior home entrance and the apartment:
  the route climbs from a ground-floor lobby through a middle landing to the
  hero's third-floor door, while a fully sealed debris pile makes the next
  upward flight impassable; 48 visible steps use three seam-free walkable ramp
  colliders, radius-safe overlapping navigation corridors keep every
  floor/flight seam traversable by the real `PlayerMotor`, and side-aware
  arrival restores the player beside the correct door;
- the fixed-metre `interior_kit` shell/dressing has real openings, panelled leaves,
  steel necks/saddles meeting wooden rails and handles on doors/cabinet. Door
  docks align the right-hand clip with the lever, plates with the latch stile.
  Concrete, rust, pipes/grilles, cabinets/radiators, damp
  damage/litter and upper junk remain. Eight RGB ImageGen albedos under
  `Resources/Stairwell/Textures` cover wall/concrete/stairs/metal/door/damage/
  wood/debris; wall/door/metal retain stronger macro contrast.
  `StairwellSurfaceAppearance` caches recipes and uses deterministic native-UV
  `_BaseMap_ST`, smoothness/metallic and compensated tint through property blocks
  on the shared `RuntimePrimitiveLit`. All ordinary shell/dressing renderers,
  including non-emissive fixture parts, receive the appropriate recipe. Hidden walkable ramps and the upper
  safety blocker, emissive tubes and halos, the hero, cat and dust/VFX remain
  outside that surface layer. Three bounded fixed camera shots cut between the
  lower flight, middle flight and apartment landing with height hysteresis;
  each shot keeps its exposed suspended HDR fluorescent tube and halo visible,
  while three stronger flickering practical-light pools, a green desaturated
  Bloom/vignette/grain profile, at most 14 dust particles, a concrete ambience
  bed, spatial ventilation and electrical layers, sparse positional industrial
  cues, a long dark reverb/moderate echo snapshot and the separate optional
  `stairwell_theme` music slot establish the atmosphere;
- one clickable 3D near-black cat — the game's last sprite conversion — sits
  with its back to the camera on the `Middle Landing Back Rail`. It has no
  armature: the actor articulates authored pivot empties, so the untouched
  deterministic idle timeline still breathes, flicks the tail, twitches an
  ear and grooms roughly every 36 seconds, while a continuous hysteresis
  head-yaw model (65° clamp) tracks the player. A default-hidden Cheshire
  grin — a tooth crescent wider than the head on an arc-length reveal
  shader — can draw itself in from the center outward while the head turns
  over the shoulder to the live camera; only a future script triggers it
  through `StairwellInteriorRoot.CatGrin`. The shared `IInteractable` menu opens
  only for active day-two `FeedTheCat`: default-Talk, the existing silent cat
  response, and default-No feeding confirmation or missing-item feedback.
  `OpenStewCan` stays reserved until completion, including day one, so the
  unique Home tin cannot be eaten before the quest;
- one reusable inventory-backed target-interaction model and controller own the
  pure item requirement, `Choice -> Confirmation -> Executing -> Closed`
  states, pointer/keyboard/gamepad choices, shared modal lock, temporary prompt
  feedback and lifecycle cleanup. A confirmed handler prepares every visual
  resource before `GameSessionState` atomically removes the required stack;
  failed preparation or a stale requirement consumes nothing. The stairwell
  cat is the first adapter: accepting the feed consumes exactly one
  `OpenStewCan`, visibly walks the ordinary 3D hero to an authored middle-shot
  entry pose and samples `CatFeedEnter`, `CatFeedLoop` and `CatFeedExit` on the
  continuous world rig. The cat keeps its independent 16-step `6 fps` feeding
  timeline, now posed as a head-down eating dip with chew alternation. The
  track starts with the player's loop, pauses ordinary cat idle/look and
  restores the hero, cat, contact shadow, input, HUD, camera and modal
  ownership after normal completion or abnormal cleanup;

### Interiors

- the deterministic `11.4 x 7.8 x 3 m` bar hall fits the ground-floor facade including
  walls/ceiling and recessed `1.45 x 2.34 m` entrance; a measured frame keeps it inside.
  Blender/interior-kit owns passive geometry; Unity owns collision/layout/light/actions.
  Counter body spans
  X[-2.1,3.85], top X[-2.325,4.075]: left shortened .9m, backbar/right end stay.
  The right return, brass rail and three taps at its seat-free end remain;
  Two opposed booths/two round tables furnish the hall. Booths at X-4.38,
  Z-2.15/.65 have `2.05 x 2.8 m` footprints and `1.05 x .85 m` tables;
  door-side/opposite benches at centreZ-.95/+.95 face +Z/-Z, one visitor each.
  WC3x3x3 replaces table/guest:toilet/basin/cracked geometric mirror.
  Worn tile/plaster;bin/litter/brush/paper/dispenser;text-free rude art.
  Door1.1x2.2m/WC:0↔-90 both sides;wing/3%outward roof match.
  Toilet hint≤.65m/facing approach;counter≤1.2m/dock.
  Jukebox replaces the right-side activity console/three vessels:
  pivot `(5,0,-.65)`, yaw180, grille toward `-X` into the hall. Its audio, collision
  and emissive pulses follow. Cocktail/SplitTheG/TinctureMatch build no console
  or vessels in any layout; activity IDs/assignments remain. Coat rack/hooks/
  collision are gone. Karaoke faces exit `-X`: podium centre `(4.1,0,-2.7)`,
  Rect `(3.05,-3.525,2.1,1.65)`. Solid `.12 m` floor is below the hero's `.28 m`
  step, without a logical movement block. Mic `(3.75,1.6,-2.7)`, stand `(4.2,.16,-2.7)`;
  approach `(2.6,.04,-2.15)` via side entry `(4.2,.16,-2.15)`.
  Speakers/curtains/valance stay. The existing stage practical moves in front of
  the singer and uses the WC's warm colour/120-degree pool; light/shadow/bounce
  counts stay. The microphone admits free wordless karaoke:
  Side entry/Enter/Loop/Exit: same rig/mic.
  LMB sings, mouse Y controls 110–440Hz; direct DSP/6ms glide avoids prefetch.
  Smooth drunk drift: sober 0, maximum ±1.5 semitones.
  DSP/96px marker/trace share source pitch before perception/VHS.
  First note claims hall silence/gaze, .9s grace; contacts/grips/WC stay.
  Speech/bubbles/jobs/crowd voices stop, no replay. Six hero angles/guest/bartender
  close-ups every 5–8s. WC pee/poop×face/STATIC bowl: same guest/hero rear,
  low live hands only seated WC; dressed in hall; scoped bowl fill/underwater
  optics/audio420Hz/−5dB, restored on cut. Attendance/service/WC keep owners.
  Shared FOV/roll/DOF; HUD every frame incl.WC, music waits. E exits/Esc pauses.
  Pause/focus freeze drift, end performance/hold view; resume base/fresh click.
  Closing/exit/cancel/disable/unload restore owners. No score/lyrics/judgement.
  BeerPong table at
  `(3.85,-1.75,.76,1.83)`. Effect providers/bounds follow.
  Five stools remain: two occupied and
  three independent hero stations, with safe approach/exit and selected-stool
  menu/drink docks. Counter top `1.02 m`, stool tops `0.8175 m`; the rightmost
  stool is at `x = 1.95, z = 0.91`. Unique UV0 albedo/response preserves metre UV1/
  source wear; shared opaque/glass/emission/service paths stay. Residential
  identity, `SplitTheG`, menu and gameplay stay. Direct bar loads also select
  Residential; explicit legacy district layouts retain their own profiles;
- 8lights:6hall+WCspot+soft WCsign;3shadowspots.
  Hall2counter+1doorbounce/256HDRcube;WC bounded bounce/256cube ceramic/metal.
  Door excluded;hall air scoped;Neutral/dust/DOF keep warm dry pub.
  Matte Ø≈1.38m fan:Y2.55,20°/s;brass stem/beam gap6.5cm.
  Skippable `1.35 s` reveal→chase;no ordinary bar SSR/SCGI;
- one separate `16 x 11 x 3.6 m` `SupermarketInterior` whose fixed shell,
  profiled shelf bodies, recessed cold cabinet, checkout, stockroom facade,
  fluorescent housings and articulated CCTV pivots are one deterministic
  passive Blender asset. The validated layout plan still owns protected
  aisles and the placement data for collision, product slots and interactions;
  runtime composition owns the actual colliders, finite-product lifetimes,
  practical lights and interaction objects. Five trimmed counter-clockwise
  skirting sections bury their rear and bottom faces `3 mm` into the wall/floor
  and meet without coplanar corner overlap. Its
  decorative checkout is staffed by `supermarket_cashier_v1` — a passive
  ordinary-proportioned `1.75 m`, `40`-mesh / `6,828`-triangle 3D clerk on the
  shared `NpcHumanV2` 31-bone Hero V2 Avatar at a `0.835 m` rest pelvis. He keeps the former uniform, detail atlas,
  attentive face, planted hands and hunch, but his ordinary neck never scales:
  only the eyes and head follow the hero through a bounded `28°` turn while
  the complete body remains behind the register. He retains the rare blink and
  a separate `E — talk` placeholder. The former `watcher_cashier_v1` with its
  five-segment `18 m` pursuit neck remains a named model/prefab asset but is not
  provider-reachable or instantiated in ordinary gameplay. Four chunky
  ceiling-corner CCTV units servo their lenses after the hero from the
  first frame (fake-emissive recording LEDs, no colliders, no real
  lights), and the hall runs on a fluorescent light budget: six
  shadowless practicals under the directional fill — one per tube row, a
  warm checkout accent, a cool cold-shelf spill — with one row
  flickering deterministically. The checkout
  is not a purchase station: activating a shelf opens its authored fixed product view centered on
  the first available physical product. The modal browser keeps one continuous
  lock while previous/next input cycles through every stocked shelf, skips
  empty shelves and moves the camera to the selected shelf while aiming at the
  selected model's visual bounds. Renderer-only owner leases hide the hero and
  active cashier during this view without disabling either gameplay root, then
  restore every renderer's captured state on all exit paths. Muted clickable
  arrows sit directly beside the product instead of adding another footer hint;
  keyboard and gamepad use the same navigation path;
- one deterministic passive `supermarket_product_pack_v1` Blender asset owns
  the six generic, unbranded and text-free product models shared by the shop,
  inventory previews, Home refrigerator and cat-feeding flow: instant noodles,
  day-old loaf, vodka bottle, closed stew can, open stew can and chicken egg.
  Its `33` meshes / `2,276` triangles feed an asset setup responsible for one
  Resources prefab per item; runtime supplies selection collision and lifetime.
  The three shop shelves contain exactly one finite physical unit of each of
  only five offers: noodles and loaf, vodka and closed stew, plus the egg on
  the cold shelf. Their
  bottom-centre pivots sit on exact authored tier anchors instead of floating
  or overhanging. The `0.46 m` vodka source uses a `0.37 m` shop fit envelope on
  the unobstructed third/top tier and remains below the shelving-unit top during
  selection; closed stew occupies the first tier. The open can's world source
  remains the Home/cat flow, and it does not add a sixth store offer. Confirming a purchase
  atomically deducts its integer price, adds one matching inventory item and
  commits the product's stable world-source ID. The bought model and collider
  disappear immediately, and the source filter keeps that shelf position empty
  after leaving and re-entering until `BeginNewGame`. Failures for insufficient
  cash, a full stack or an already-bought source mutate nothing. The product's
  child price tag disappears with it instead of remaining on an empty shelf.
  Closed stew is
  a separate `ClosedStewCan` item from the refrigerator's `OpenStewCan` and
  therefore cannot satisfy the stairwell cat's feeding requirement;
- Home `12 x 8 x 3.4 m`, `X[-7,5]`: clear routes, bathroom/balcony intact.
  Blender `home_interior_v1`/`interior_kit`: geometry; Unity: collision/actions/
  lights/cameras/live sleep deformation.
  `ApartmentEntranceDoor` shares aged timber/four recessed panels, deep frame,
  architraves/threshold/hardware with CombatTest; opening `1.32 m`.
  The same locked room is `13.04 m²`, `X[-2.80,1.46], Z[0.82,3.88]`;
  its unchanged door reports the missing key without opening or consuming keys.
  Sleep is on the unfolded `2.55 x 1.15 m` west sofa, back west/head south.
  No corner junk/late decor; bookcase/radio face east from the south-west corner.
  A `3.37 m` full kitchen includes four burners/oven, hollow sink, storage,
  worktop and utensils; cooking is visual only. Table top `0.83 m` is flush to
  the locked-room front, wholly inside its width; table/chair share `X=-1.925`.
  Chair seat `0.48 m`; `HomeDiningSeatPlan` shares side-entry/rear approach;
  an owned filter retains seat E/prompt through cleanup.
  Five living sheets preserve bathroom/balcony maps. `HomeWallPainting` adds
  anonymous wall art with silent standing close-up/E-Esc exit, no text/reward;
- seven calendar apartment appearances through `HomeApartmentDayRules` and
  `HomeApartmentDayController`: day one is relatively kept, days two through
  seven accumulate domestic clutter and grime, and later days retain day seven.
  The exact clamped day can be inspected forward or backward in Home debug.
  This presentation is separate from alcohol level, acts and future decay;
  sleep, sobering and scene round trips do not clean it or disable actions;
- one visually prominent data-first refrigerator fitted into a split kitchen
  counter, with an independently authored player-width approach; its enamel cabinet contains a
  hollow liner, three stained shelves, a lower drawer, frost, grime and two
  door bins. Six cavity slots and two door slots form the storage contract;
  the initial occupied slots hold a vodka bottle, one chicken egg and an open
  can of stew, instantiated from the same passive six-item product pack used by
  shop and inventory presentation. Each occupant owns stable catalog metadata,
  registered renderers and a tight non-blocking selection trigger. A successful `Take`
  now transfers the item into the run inventory, removes the physical model
  and persists the stable collected slot across scene round trips;
- one shared modal refrigerator presentation: Home's unscaled first-person
  Bezier approach keeps the hero visible until the handle turns and the sealed
  door opens to `102°`, without a camera-local hand. Warehouse first guides
  the real hero to authored entry/facing through the constrained motor;
  wrong-level or stalled entry cancels, and a rendered neutral settle precedes
  the door/camera timeline. Its chest Lens lights the cavity from opening;
  cursor aim starts only while browsing and freezes during pause/nested views.
  Clickable Close, interaction or cancel share the guarded close/seal path.
  World meshes/contact shadows restore as camera return begins; input/HUD at
  completion, Home at its fixed shot. Home's cold strip/halo add no realtime
  `Light`; seal/hinge/thunk cues crossfade synchronized closed/open spatial loops;
- while the outer refrigerator interaction holds the lit open state, one
  nested item browser highlights the vodka, egg or open stew can and
  shows its localized name beside the pointer; keyboard/gamepad cycling and
  confirm provide the same selection path. Click or confirm flies the chosen
  model into the center of the camera, fades in a dark camera-facing backdrop,
  rotates it slowly and presents a localized title, short description and
  two centered `Take`/`Back` actions. `Take` atomically commits the stable world-item
  source and inventory stack before removing the model. Target-owned uses such as
  feeding the stairwell cat begin at that world target. `Back`
  returns an untaken item before the refrigerator can close. Normal return, cancel,
  disable and destroy restore the exact parent, sibling index, local transform,
  selection collider and original renderer colors without acquiring a second
  modal lock;
- one localized fullscreen hero inventory in City, BarInterior,
  SupermarketInterior, HomeInterior and StairwellInterior. `I` or gamepad North
  captures the shared modal lock, freezes scaled time, hides the gameplay HUD
  and preserves the ambient audio bed; Escape, the same toggle or gamepad East
  restores the exact prior input, camera, HUD and time-scale state without
  opening pause on the same frame. The logical `640x360` screen combines a
  dedicated transparent portrait rendered from the production 3D hero, four
  compact intoxication/hunger/stress/fatigue bars, dollar cash, a five-column point-filtered
  icon grid, selected item description and contextual Eat/Drink/Equip/Remove, Examine and
  Close commands. Hunger, stress and fatigue are session-owned `0-100` values
  that start at zero and survive ordinary scene loads. Once the startup Wake
  starts the shared session clock, hunger rises from `0` to `100` over
  `1440` game minutes and fatigue over `1080`; both use hidden double-precision
  fractions while the existing menu bars expose only clamped integers. The
  same pause-aware real-time path freezes progression before Wake and during
  true pause, but otherwise continues through scenes, transitions and ordinary
  interactions without following intoxication's world slowdown. Food clears
  the hunger fraction when it applies its relief, while only a normally
  completed bed sleep clears fatigue and its
  fraction. Neither need has a gameplay debuff yet. The selected item is a
   live low-resolution 3D model in the lower panel and Examine view. Examine
   centres a compact preview without an inner frame, with compact text/buttons;
   fish cans use a smaller inspection scale.
   The hidden unscaled preview stage reuses Home/shop bottle/egg/stew/noodles/loaf,
   warehouse fish cans and inventory key/lighter geometry. A
  pure catalog and ordered stack state begin every new run with apartment keys
  and a lighter, persist across scene loads and reset with the session. Current
  clothing has a separate equipped flag, with `Используется` / `Не используется`
  and `Надеть` / `Снять` for the selected scarf; its icon is yellow like the
  garment. Equipment does not consume the
  item, requires ownership and clears when its last unit is removed; a new
  game resets both collection and equipment. `PlayerFactory` installs the
  optional `PlayerScarfController` on the shared hero. Its separate imported
  yellow wrap covers the neck and nape, and the lower face when raised. After
  `5 s` of free third-person play it lowers everywhere except Alpine Village,
  where it rises. Travel keeps the prior resting posture; first-person views,
  rides and action ownership restart the wait, and pause freezes it. The same
  left-hand gesture changes both ways, beginning only after disembark completes.
  The `45 cm` pinned tail responds to exterior wind and motion with bounded
  bending. Running lifts it behind the hero, with flutter and smooth settling.
  Wrap, knot and tail keep simple contact with
  the hero's body parts through bone-following proxy volumes derived from
  the authored body meshes. External objects, buildings and NPCs are ignored.
  `PlayerScarfBodyContacts` measures the body once and follows its bones;
  `PlayerScarfContactSurface` corrects the wrap/knot. Ordinary gameplay avoids
  world scans and body baking; detailed contacts remain diagnostic-only.
  Rest geometry is immutable; actors own dynamic surfaces. Pause freezes them;
  teleports reset motion. Closed
  cabins, the village workroom and interiors remove exterior wind; the home
  balcony uses it. One shared matte material samples the mother's-house
  `BookCloth` tile. Visible mouth actions lease access and wait for a short
  left-hand reach/pull/release (`0.24/0.55/0.24 s`), which lowers the authored
  wrap shape before drinking, smoking or brushing; vomiting also owns access.
  Cached hand/thumb surfaces place the actual hand exterior `4 mm` in front
  of the garment.
  Mouth access overrides the location posture and restores it on release;
  it never changes equipment or cold protection. Unequipping and session/inventory
  reset clear the resting posture. Shower
  clothing ownership temporarily removes its visible form. Head/body hiding
  applies to the accessory, and the bathroom mirror copies its live appearance
  and all corrected surfaces without another deformation/contact update. Instant inventory
  consumption retains its existing non-animated behavior. Current
  food has explicit relief but cannot reduce hunger below `20`; food with no
  effect remains in its stack. Alcohol has separate stress-relief values, and
  the inventory vodka bottle commits four servings atomically while maximum
  intoxication leaves it untouched. Use appears only for consumables/equipment;
  temporarily blocked actions stay disabled;
- a real window and open glazed door in the Home right wall leading, without
  another scene load, onto a walkable third-floor balcony at `4.7 m` street
  elevation; open-looking rails retain invisible safety colliders, while the
  view rebuilds only a bounded same-blueprint-and-seed slice of the actual
  street's asphalt, sidewalks, road markings, lots, windows, lamps and
  signals. City and Home share the ground compositor, street plan, facade,
  window and complete passive pub-exterior appearance recipe. It also
  reconstructs the target-derived Home stop as the same static
  blue `01` pole in Home-local space, without colliders. The balcony
  shot temporarily applies City's exact exponential-squared fog, matching
  background, `48 m` visibility cap, current time-of-day lighting, grading,
  local fog field and bounded `12`-light street/bar pool, then restores the
  captured Home visibility and lighting for MainRoom, Bathroom, disable and
  destroy. During that shot only, the same two-slot pedestrian runtime supplies
  distance-managed passers-by in the fog-hidden band on the reconstructed
  street below; leaving the shot immediately pools them. The static stop is not
  a vehicle activation boundary: the balcony does not compose a bus actor or
  director because its real street context has no two-ended, complete-body
  fog-hidden pass-through. Fog and the City grade remain
  identical at every
  hour. It never creates a second City root, player or camera;
- one modal balcony-smoking vignette at the Home-local dock around
  `(6.60, 0.04, -1.45)`: the first `E` locks manual input while the ordinary 3D
  rig walks to entry and turns toward the city along `+X`; exact alignment
  holds one neutral rendered frame, then the same visible rig samples
  `SmokeEnter`, `SmokeLoop` and `SmokeExit`. The authored motion settles the
  hero toward the outer rail, draws the right hand from his coat, brings the
  cigarette to his lips for a held inhale, lowers it for an outward exhale and
  returns through a deliberate discard. Once per `9.5 s` loop, local frame
  `16` emits a bounded gray-green world-space plume from the registered mouth
  socket; it travels toward the city, grows and fades before the next loop,
  while queued exit stops new emission and lets the detached smoke dissipate.
  The approximately `74 mm` cigarette
  follows the registered right-hand socket along its outward local axis; it is
  revealed only after leaving the coat and hidden on the exit flick. A
  permanent worn enamel ashtray rests on the outer rail at Home-local
  `(7.25, 1.12, -1.67)`, directly below that authored flick, and remains
  visible independently of the smoking lifecycle. The
  deterministic timeline retains its melancholic `9.5 s` loop; a second `E`
  queues until a calm low-hand boundary, preventing a raised-hand or inhale
  cut. No renderer swap, sprite fade or shadow handoff is needed;
  the camera holds briefly and eases along a quadratic push-in to `38°` FOV.
  Its close look target is biased `0.33 m` toward the city along Home-local
  `+X`, turning the target yaw to about `13.12°`: the hero stays prominent at
  about `0.37` viewport X while a point `1 m` cityward remains visible to his
  right. Camera position and FOV are unchanged. A smoking-local deterministic
  drift overlays that path with restrained
  X/Y/Z position amplitudes of `0.016 / 0.007 / 0.005 m` and pitch/yaw/roll
  amplitudes of `0.12° / 0.20° / 0.08°`. Paired `13-23 s` harmonics share one
  clock across every interaction phase, while the camera blend fades their
  envelope in and back to exactly zero during the smooth `2 s` Balcony-shot
  restoration. FOV has no extra pulse and generic `PlayerCameraFollow` remains
  unchanged. The
  separate optional `smoking_theme` music starts from silence once the
  mixing rule clears the apartment theme, and leaves through the same shared
  `4 s` fade-out rather than the camera ramp; without a supplied clip, the
  complete interaction remains playable and silent;
- one reusable animated-interaction timeline and player controller with a
  visible `Positioning` pre-phase followed by
  `Entering -> Looping -> Exiting`; each interaction supplies separate entry
  root/pelvis/facing, action pelvis and exit root/pelvis/facing poses, plus an
  optional pelvis waypoint with independent arrival/hold timing in each
  transition. The
  ordinary rig uses the constrained motor, gait, turn and footsteps to reach
  entry. Exact grounded alignment clears locomotion, facial and additive status
  offsets and holds the neutral 3D endpoint for one rendered frame. The
  controller then samples the registered enter/loop/exit Generic clips on that
  same rig and aligns its pelvis anchor to the authored world target after each
  sample. Gameplay owns clip time and terminal holds; root motion, Animator
  transitions and Animation Events own no transaction. The terminal exit pose
  is presented before the physical root moves to the independent exit, and the
  neutral rig stays locked through its final `LateUpdate`. Extra loop holds and
  the opening-only exit-duration multiplier remain deterministic. Unreachable
  height, no-progress approach, failed preparation, transition, disable and
  destroy all reset clip spatial offsets and restore control, camera, HUD,
  props, partner animation and contact shadow through owned cleanup;
- one reachable sleeping-sofa interaction on its east long side:
  the first `E` walks and turns the 3D hero to the middle of
  that edge, opposite the sleeping pelvis, holds neutral for one frame and plays the `5 s` `BedEnter`
  on the continuous rig — the one contextual action authored on eased curves
  with separately staggered pelvis, lower-spine, chest and head keys.
  The hands support the lowering trunk and the head settles last. An authored
  pelvis path replaces continuous travel across the mattress: two `7 cm`
  lifts, transfers under alternating planted hands and full seated stops.
  The hands change support during the pause, and reclining starts only after
  both transfers. The
  deterministic `BedSleepLoop` repeats with the existing breathing holds until
  a second `E` plays the `6.0 s` `BedExit`; the opening can begin directly in
  that loop and apply its one-shot wake-duration multiplier. Waking is a
  four-beat sit-up rather than a roll: he curls onto his elbows into a
  half-crouch on the mattress with both boots drawn under him, uses the same
  two supported pelvis steps to reach the side, then drops the right
  leg over the near edge, then the left, and only then stands. Per-sample pelvis alignment
  keeps the same longitudinal coordinate through entry, sleep and exit. The
  standing approach stays clear of the south-west bookcase.
  The sleeper remains at the authored sofa action anchor, with the head at the
  south pillow. Both hip anchors are the mattress top plus a measured body
  offset rather than a clearance guess: the generator reports how far the
  supine back, the lifted head and the seated weight hang below the pelvis
  bone, `PlayerCharacterDimensions` mirrors those numbers, and the pillow,
  blanket and crumpled shirt are placed around the pose instead of through it.
  The mattress and the closed, domed pillow use deformable vertex grids:
  they dent under actual contact with their local rest surface, his hip
  target descends by the same sink depth so he lies in the dent rather than
  hovering over it, and the dent visibly refills over about a second and a
  half once he is up. The pillow retains thickness and connected shoulders
  while its head hollow refills; its rest shape is authored in Blender. Sitting
  on the edge dents nothing, because that pose is pinned by both boots on
  the floor.
  Only a normally completed `BedExit` resets session fatigue;
  cancellation, transition, disable and destroy preserve it. Localized prompts
  and all normal/abnormal cleanup remain;
- one sofa-relative nightstand and a `27.6 cm`-wide 3D alarm clock that remain visible
  as ordinary Home dressing. Its reusable 28-segment display begins the
  retained waking opening at `05:59` and flickers all digits and punctuation briefly
  at long intervals. After a silent five-second input lock it reveals the menu
  without changing the time or starting the alarm. Choosing Wake Up changes
  the display to solid `06:00`, starts the shared session time, generates a
  looping mono `22050 Hz` mechanical ring, rattles visibly and routes its fully
  spatial source through `SFX/World`. From that handoff onward the display
  follows the persistent session hour/minute, including after leaving and
  returning Home. The clock shot and sleeping loop remain fixed for three
  unscaled seconds; the ring then stops and only then does the camera glide to
  the sleeper and smoothly settle into the active Home shot while the existing
  72-frame wake sequence plays over `6.9 s` instead of the ordinary six,
  restoring normal control without a camera cut or another scene load;
- one fully built bathroom with tiled surfaces, an ajar doorway, toilet,
  shower tray and curtain, pedestal sink, a cloudy mirror that really reflects
  (a hole in the wall with a mirrored copy of the bathroom and of the hero behind
  it, active under the pinned bathroom shot, including owned first-person
  brushing, without the former raised black crack strip), exposed rusty pipes,
  leak stains and floor drain; the toilet cistern sits against the right wall
  and its bowl faces into the room;
- one Home-only atmosphere with a weaker hard-shadow directional/ambient base,
  three bounded shadowless practical pools aligned with the living pendant,
  kitchen fixture and cold bathroom tube. The bathroom point pool, tube and halo share
  one deterministic unscaled flicker: they stay steady for most of each
  `6.4 s` cycle, then briefly stutter through a `0.52 s` fluorescent-failure
  burst while a co-located spatial electrical crackle follows every actually
  rendered factor change. One separate cold shadowed ForcePixel Spot starts just inside the
  bathroom threshold, shares the same flicker and projects through the ajar
  door onto the apartment exit area; another cold shadowed cookie Spot casts
  time-of-day light through the window, blending from the existing cold night
  shaft to a warm daylight shaft. A shadowless warm Spot is physically
  co-located with the compact amber lamp above the entrance and casts a local
  pool over the door, wall and floor. These remain capped at six
  atmosphere-owned local realtime lights; the scene Directional light is
  separate. The atmosphere also owns a shared transparent glass
  shader/material, a restrained Bloom/color/exposure/vignette/film-grain
  runtime volume, at most 12 sparse dust motes, a calm room bed, spatial
  refrigerator/balcony layers, sparse domestic details and a short damped
  reverb snapshot without echo;
- one Main Camera defaults to three authored poses:
  MainRoom at `(-6.48, 3.05, -3.25)`, Euler `(28°, 55°, 0°)`, `68°` FOV;
  Bathroom at
  `(1.82, 2.20, 0.86)`, Euler `(30°, 38°, 0°)` and `92°` FOV, and the Balcony
  pose; separate activation and wider hold bounds provide threshold
  hysteresis. Fixed positions ignore orbit and retain quarter-strength
  intoxication/balance/fall rotation;
- a lived-in bathroom: a rebuilt shower stall (hand-operated entrance curtain on an
  L-rail, mixer, rusty pipe, tilted head, soap shelf); the green hose segments
  alongside the pipe are removed. Its red/blue cross valves share the sink's
  authored wheel and stand on top of the mixer; the right hand follows the
  hot wheel's turning grip. The bathroom lamp sits above the curtain rail,
  with its practical light and crackle source raised alongside it. The plumbing
  stands on the back wall directly ahead of the washing hero. The existing
  imported head is tilted `20°` instead of `35°` and moved `0.09 m` toward
  the wall, with its connecting arm shortened. Narrow water streaks emit
  in a `30°` fan along the actual nozzle axis; their local depth fade is `0.02 m`, preserving
  visibility near skin. Residual drops still land inside the tray at the
  fixed landing point, which also anchors the water ripples. Three modal scenes run on
  one shared skeleton — a first-person toilet action, a first-person shower
  in which the hero visibly opens the curtain opposite the tap wall, steps
  into the stall and closes it behind him. The lens starts a gentler flight on `E`,
  crosses the opening curtain and reaches the future eye position before
  the hero. Dressed approach/opening precede offscreen undress before StepIn;
  he enters bare and joins the eye lens without drift. The feet stand farther from
  the wall and the torso leans toward the water without automatic sway.
  Both palms rest on the tile. Before taking soap, the right hand opens
  the hot wheel, then the left opens the cold wheel. Each returns to its brace;
  each opening contributes half the final flow.
  Valve turns sound at the grip, running water at the actual stream impact,
  and final drops sound only when they land in the tray.
  He washes with the current wardrobe removed: actual bare body/skin atlas,
  no repaint or shoulder overlays, the toilet's passive anatomy. Restoration
  returns the selected outfit and preserves the first-person head lease.
  Before soap, mouse/right stick look normally. `E` takes it regardless of cursor
  position or viewing direction. A localized prompt, yellow outline and
  connecting line appear beside the soap when it is visible; they do not
  gate pickup. Pickup takes priority over exit. Pressing on
  the body selects a surface point; holding left mouse repeats right-hand
  strokes there with a stationary cursor. Release stops washing and safely
  withdraws the hand; a new press selects another point. RMB/LT look pauses
  rubbing. Arrow keys turn the view at `90°/s` alongside the
  pointer, and held right mouse also enables look. Absolute pitch is bounded
  to `−75°…115°`, allowing a view of the shower head and room to wash the
  chest in view even with the bent head ahead of the torso.
  The gamepad's interaction button picks up soap, right stick moves the
  pointer, right trigger holds washing, and left
  trigger changes the stick to look. Measured contact travel fills one shared
  gauge over at least `10 s` of active washing; any single selectable body
  region can fill it completely, with no regional quotas. The soap stays in
  the right hand throughout pickup, washing and return; every part of that
  arm is excluded from body picking and washing. The soap rests against the
  measured production palm and follows its hand frame; travel uses the same
  body-mesh guard. Actual soap contact drives a stronger bounded passive
  anatomy response that settles after release. The two scrotum lobes rest
  closer to the bare body, rotated down around their fixed attachments.
  A shallow water layer grows to `1 cm` in the tray, with ripples and flow
  toward a real perforated `15 cm` drain in the far-right corner when facing
  the tap wall. The layer drains after shutoff. The user-requested removal
  of camera-lens drops leaves the scene image unobstructed by a water overlay.
  `E` is consumed as soap pickup throughout washing. Exit `Q` works without
  aiming or a visible prompt. When the cold wheel is visible, a label sits
  to its left with the same yellow outline and leader used for soap.
  Full progress or exit `Q` returns the soap before the left hand closes
  the cold wheel and the right closes the hot wheel. The shower loop gain is doubled to `0.29`;
  other bathroom sounds retain their levels. The hero remains undressed
  through straightening, drips, opening the curtain and stepping out.
  The camera detaches before straightening, returning the last wash look to its
  fixed entry endpoint during `0.6 s` straightening and `3 s` of drips.
  It stays there while he opens the curtain and steps out first, stopping
  at the existing outside dock facing the room. A `0.25 s` rendered settle
  lets the walking pose fade before the offscreen check. Clothing
  restores only after a rendered frame shows every active body part outside
  the camera frustum, including his feet. The lens then retraces its entry path over `4 s`, preserving the entry rotation/FOV
  interpolation rather than turning to follow him. Only the rendered default
  frame allows an in-place turn toward the curtain at the authored outside
  dock and the closing gesture; input returns after its terminal
  frame, with no extra actor turn. The static left curtain overlaps all
  the way to the tile. Only full progress grants shower relief and mouth
  cleaning; waiting without starting a stroke or leaving early does not.
  The third scene is first-person teeth brushing through the
  real mirror. The left hand opens an authored faucet valve before brushing;
  its stream and local water sound follow the valve. Opening and closing
  each play one local mechanical cue, including a reversed turn on cancellation.
  Mouse or right-stick X/Y
  drives the actual right arm relative to the reflection; its gauge credits
  only commanded brush travel confirmed at the
  teeth, requiring at least `5 s` of active movement. At full progress the
  hero inspects his teeth over `3 s`: `0–0.6 s` lowers the brush and leans
  closer, `0.6–0.9 s` opens his lips, `0.9–1.65 s` turns left,
  `1.65–2.4 s` turns right, and `2.4–3 s` returns to centre and the standing
  pose. The feet and gameplay root stay at the dock. `TeethInspectHalf` and
  `TeethInspect` reveal two distinct muted tooth rows on the existing curved
  face, preserving the weary eyes and straight mouth corners. The eye camera
  holds the reflection through the head turns, easing FOV from `48°` to
  `34°` with the lean and back before spit; faucet water keeps flowing.
  Only the reflected head/neck receives a scoped base-colour multiplier
  `1–2.5` following the brushing camera blend. Source hero, shared materials,
  alpha, light/shadow response, room lights and glass are preserved; cleanup
  restores the ordinary dark reflection. The inspection's tooth rows use
  muted cream `(210, 204, 181)`.
  He then bends and spits for `1.5 s`; mouth-origin foam follows ballistic
  flight into the real hollow Blender basin. The camera stays in the hero's eyes throughout;
  the left hand turns the valve shut after the spit and before camera return.
  The reflected rig, brush, mouth foam, spit and faucet water stay synchronized.
  Stop `E` cancels before full progress and closes any opened valve first;
  after camera return, completion and cancellation both take one ordinary
  backward step from the sink while facing the mirror, then restore input.
  Scene interruption immediately stops the owned water and audio. Completion
  cleans the soiled mouth on every replay and grants stress `-5` once per
  game day; cancellation does neither. Toilet and shower retain their own
  completion-only relief;
- the toilet's single world trigger opens `HomeToiletChoiceInteraction`
  with `По-маленькому` / `По-большому`. Menu/mouse selection dispatches to
  one bathroom owner; cancel starts nothing. Both branches use the hero's
  hand and shared `2 s` lid actions after the grounded approach. The small
  option blends to eye level over `1.5 s`. A hollow pedestal and open annular
  seat keep the water reachable; a paper roll stands on the cistern. The
  view/stream/residue core also serves the polygon taunt. The actual production
  right arm holds a matching Blender anatomy mesh through IK; only head
  geometry hides near the camera. Mouse/right stick aim, with unrestricted
  yaw passed into a body turn on the spot; RMB/gamepad LB provide independent
  look. A local right-edge gauge drains over exactly `6 s`, followed by `2 s`
  of shaking and `1.3 s` of camera return, with guided travel separate.
  Over the final `20%` of the main emission, pressure, diameter and packet
  frequency ease to zero; emitted liquid retains its existing trajectory.
  Two Blender-authored scrotum lobes hang from fixed body attachments.
  Camera movement excites a damped shaft spring and two distinct gravity
  pendulums; the real hand and urine outlet follow the resulting pose.
  Ballistic liquid and residual drops collide with the authored surfaces;
  the bowl receives water hits while missed shots leave merged, surface-bound
  wet marks and wall drips. Their bounded session store survives Home reloads
  independently of calendar dirt and resets with a new game. Natural completion
  flushes and commits stress `-6` once; stop input cancels without relief and
  retains existing marks. The shared lifecycle restores pose, head visibility,
  camera, cursor, occlusion, HUD, lid and modal input on completion or interruption;
- the large toilet option uses `HomeToiletSeatedTimeline` to coordinate the
  same hero, lowered trousers and a camera inside the actual hollow Blender
  `ToiletBowl`, below local water top `Y = 0.4373 m`. After opening the lid
  and a grounded turn, `Prepare` takes `2 s`; the `2.5 s` dive starts sitting
  at `1.8 s`, followed by its remaining `1.3 s`, a `3 s` seated action,
  `2 s` rise, grounded turn and `2 s` inspection while the lens stays below
  the water. The `2.5 s` flush action presents the real button press at
  `1.5 s`; a long frame cannot skip that contact marker. The camera
  accelerates in the whirlpool for `1 s` after pressing, then returns over
  `2.5 s`. Dressing starts immediately with that return and lasts `2 s`;
  lid closure waits until the camera is back.
  The lens passes the empty opening before seating and after the body rises.
  `HomeToiletActorPresentation` samples nine independent Hero V2 clips;
  `HomeToiletSeatedAppearance` owns ten skinned lower-body/garment renderers
  and five `Lowered` shapes, using the production bones and garment proxies
  that follow those bones. Bare pieces derive from anatomy, fabric pieces from
  the separate trousers; the selected trousers supply their shared appearance.
  Original garment endpoints, outfit and renderer flags restore.
  `HomeToiletBowelEffect` starts visible emission at seated `0.5 s`, releases
  at `1.05 s`, falls ballistically through air and slows under water. One
  contact produces a ripple, splash, bubbles and sound. The solid approaches
  within `26 mm` of the lens. A scoped invisible
  `HomeToiletBowlLighting` fill affects only its rendering layer, without
  changing scene exposure. During inspection the same fill rises `0.32 m`,
  its range blends `0.78 → 1.2 m` and intensity `0.17 → 0.25` to reach the
  bent face; it returns to the bowl setting by the end of the flush action.
  Water tint and optical ripple follow the surface.
  A scoped `Master/Perception` low-pass after VHS reaches `420 Hz` and
  `-5 dB`, with smooth depth response; UI bypasses it. One `0.72 s` water-entry
  cue plays at the actual downward crossing, and a quiet shared `3 s` water
  loop fades with submersion. Source filters and VHS parameters remain
  untouched. Completion or interruption stops the owned voices and returns
  mixer control to scene snapshots. The branch reuses the
  bathroom positioning, modal and cleanup lifecycle. Ordinary cancellation
  clears the body from the camera's exit route before returning and restores
  clothes and the lid without flushing; teardown immediately releases owned
  state. Natural completion uses `HomeToiletFlushVortex`: two forward roll
  turns (`720 degrees`) with continuous angular speed, a `9 mm` orbit radius
  and a smooth stop at the ordinary shot. Three curling surface arms and
  six reused orbiting bubble meshes follow its strength. After lens contact
  and a `0.28 s`/`70 mm` clearance move, `HomeToiletFloatingBody` integrates
  buoyancy, drag, current and angular motion at `1/120 s`, with relative
  density `0.76`. The authored bowl profile and actual camera constrain it;
  the solid player-footprint collider remains outside this scoped simulation.
  Flush forces capture it and hide it only inside the lower bowl zone,
  after a minimum guard; disappearance is not prescribed at one second.
  Zero elapsed time freezes the state, and `End` resets it. An owned source
  reuses the existing `2.6 s`
  `ToiletFlush` sound through the underwater mix. Surface/underwater parameters
  and audio restore on exit. The `1.1.0` seated module has `1,602` triangles;
  its more defined buttocks form one closed `958`-triangle pelvis component.
  The branch adds no stress relief or session transaction. The accepted
  practical inspection, brief lower face view, flush and local-light boundaries
  are recorded in architecture notes and story-bible §6; current Unity
  verification is recorded in the work log;

### People, diagnostics and money

- `BarBusinessHours`: daily12:00 inclusive–02:00 exclusive; new entrance/order/repeat/refill
  reject after closing. Accepted FIFO service/served drinks finish; exit stays free.
  No plot closure/event advances. New-game location validation shares these hours.
  Hourly deterministic day/bar target:12–16=3–4,16–19=4–6,19–23=6–8,
  23–01=4–6,01–02=3–4; closed0, excluding bartender/hero.
  `BarCrowdController` reserves eight visit appearances/slots, instantiates only present
  guests, moves one through the real hinged door/aisle at a time, blends shared rig
  seat endpoints. Active speech/service finish before departure; the guest
  releases the same supported vessel and leaves empty-handed. Dirty docks stay
  reserved until the bartender collects and stows their actual prop behind the
  counter. One bounded background queue covers counter, tables and booths;
  the worker goes around the left counter end, grips at contact and walks back.
  Accepted service waits for his real return; cleanup also runs after closing.
  Rare WC visits preserve the same resident/place/drink while speech/refill wait.
  Physical door/access admits one visitor, respects the hero and shows silent
  occupied status outside; inside egress stays free. Behind the closed door only
  a timed wait runs; guests return with aisle priority over the next collection.
  Occupied/recovering hero docks stay reserved. No in-room spawn/fade.
- up to eight transient default NPC visitors: each entry uses the shared catalog/
  body/face/hair/wardrobe allocator anew, without permanent IDs or changes to
  permanent characters. Appearances do not repeat within the visit.
  Four sit on opposed booth `0.48 m` seats, two at counter X-1.65/1.05 on
  cafe `0.8175 m` stools, and two stand at pub-table edges. Placement and
  actions stay local; the default body adapter owns shared rig compatibility.
  Present guests drink. Each entry independently shuffles a balanced
  Bottle/Pint/WineGlass/ShotGlass/Snifter bag from shared service assets.
  `BarPatronDrinkProp` caches physical grip/rim/base/bounds and typed sip/torso
  profiles. Counter `CafeManDrink` and booth seated graphs stay. Seeded
  Rest/Raise/Sip/Lower brings each rim to the mouth with its matching
  handle/cupped/cylinder grip. After `Lower`, each vessel returns upright to
  the actual counter/booth/pub tabletop with the free hand supported; its full
  footprint, including handles, stays inside rectangular/circular surfaces.
  Slight deterministic, non-referential head drift lasts until the next `Raise`.
  Counter:Bottle→ShotGlass; distinct seeded sip drain. Empty:left wave/wait,
  Same-cup FIFO with hero;R bent at support, offers empty mug/pulls full to
  original dock before release. Six loops stay.
  Accepted request:look/one nod; service continues.
  Base pose is sampled before caching contacts. The eight drinkers lease
  `AlwaysAnimate` so offscreen body/hands/props stay aligned; disable/destroy
  restores prior culling, enable reacquires it;
- one global default-worker bartender occupies the authored floor-level anchor.
  `DefaultNpcFactory.CreateForCharacter` resolves permanent `zzz.bar.bartender`
  after the existing roster, preserving its earlier assignments. The role
  adapter releases the default animation graph, reuses cafe service clips,
  default locomotion and one towel, and leaves clothing/appearance
  to the global population. Both bespoke bartender model/source/provider/import
  families are removed;
  `BarBartenderServiceChoreography` turns/walks the root, restores working
  facing and binds anatomical IK; total torso tilt≤38°. Scaled shell bounds
  keep lift/transfer/lower above wood/outside paper. Wine/snifter/shot:
  18.5/12.8/5.8cm; beer15.515cm. First service routes appear below.
  Root/feet stay on the floor; no duckboard/lift. Shared Wipe touches `Y1.02m`:
  a reachable whole stroke keeps15mm from open/closed menu or physically stows.
  Idle stow/Walk/turn/draw moves to another bounded patch; service preempts.
  The flat menu travels on the left palm ahead of the torso, refreshed after IK.
  The same arm hides/reveals it behind the back; rear contact gates service.
  Free right palm braces/repositions/notices; occupied IK/Walk/towel win.
  Central-tap beer/three bottle routes live; mixing/multi-bottle remain deferred;
- `BarConversationController`: present counter/two booth pairs and bartender; two shared views.
  Absent partner suspends that pair only; refresh releases its lines/declarations.
  Closing finishes active exchanges without new ones; accepted-order comments remain.
  Separate-table patrons silent; ≤4 lines; A→B/no repeats. Short waits/bounded tokens.
  Rest/contact gates; pause freezes, WC/range/exit cancel. Foam awaits state;
- the soundscape keeps one diffuse occupied-room bed, then places two
  independent `8 s` mono murmur pockets at the real booth and social-table
  groups, their gains easing with actual attendance; empty pockets receive no crowd cue.
  Closing fades/pauses the jukebox through its existing mix; work lighting/ventilation stay.
  City/Home bar ground glazing dims outside hours; upstairs/landmark stay readable.
  One fully spatial World voice moves between occupied groups and the
  counter for glass, chair, bottle and short wordless crowd events every
  `4.5–8 s`. Together with the grille-owned theme and its close cabinet
  texture, these six bed sources remain wordless. Speech adds no mixer
  bus or global reverb. The same jukebox owns three
  slow, gain-linked emissive channels for its panel and side tubes; the
  interaction flash is composed by that single presentation writer;
- one exit and five permanent counter adapters expose three to five actually free stools; the
  activity fixture (beer-pong table, stage) survives purely as layout
  dressing. There is no bar-visited mechanic and no planned bar route on
  the map: bars are numbered markers with names, nothing more;
- global `F4` free flight: mouse/WASD, Q down/E up, Shift ×4.
  Busy actions/modal ownership and defeat permit entry; timelines stay live.
  Flight reserves physical commands, never snapshots input flags. F4/Esc returns
  the current scene camera; actual pause/scene/disable clears the overlay;
- `F9` City/Bar/Road/Home: closes map, modal lock; clicks/Left/Right adjust
  intoxication `±20` within `0–100`, keep drink context. Days `1–7` keep
  `HH:MM`/running/needs; days uncapped. City owns teleport flag;
  Home days reversible; `HomeDebugCityMapShortcut`: City map beside home.
  Opening/actions/doors/modals block Home debug/delay midnight;
  Committed drink protected. Default-on speed flag: `F1/F2/F3`
  `×3/×5/×10` across nine scenes/actions; repeat/flag-off→`×1`. Pause/loading
  block keys; transitions retain speed, new game resets `×1`/flag. Readout:
  speed/brief `×1`; world/calendar accelerate, UI/audio realtime, motion caps stay.
  City «Loaded fish truck» closes F9 on success: same truck/seated driver/three
  crates, clear final approach about `20 m` from factory→normal unload/production.
  Works pre-dock/paused; seeks only delivery time/batch (repeat→next batch),
  calendar/needs/hero stay;
- `debug.log`:NDJSON scene/world/drink/balance/Unity results;F8snapshot/Shift+F8dir;
  cached movement_summary before end-close. Duel:20MiB/70%samples/32-action summary tail;
- a session-only cash wallet starting at `$999`, shared by finite supermarket
  stock and the bar's four-item visible drink menu. The bar now uses the same
  physical counter-seat and `CounterMenu` behavior as the relocated City cafe.
  `BarCounterSeatPlanner` exposes all three stools not occupied by patrons; each
  owns a collision-safe entry/exit, and the rightmost sits at local `x = 1.95`
  clear of the counter return. Three closely spaced taps occupy the seat-free
  counter end beyond it. Bar-only side clips retain a grounded foot/counter
  palm until one buttock meets the rim, then roll weight inward and lift the
  free leg. Exit restores floor support before releasing the seat.
  `CounterSeatLoop` holds pelvis; upper body breathes. Bar drinks keep its legs;
  cafe/bus stay.
  Body/garments must clear actual furniture. The bartender then carries a
  `bar_service_props_v1` `1.4.1` booklet to the selected stool's axis before him.
  The shared model/input/page/focus/hint/prop-motion layer focuses
  the larger bar spread over `0.45 s` at `0.45 m` and FOV `72`, using a
  near-overhead solve centred over the booklet. Its bar-only cinematic DOF is
  a restrained `35 mm`, `f/8` override focused on the page and exists only
  while the spread is open; resting the booklet, accepting an order or starting
  an exit ends it immediately. The bar style uses larger bold near-black type
  on an inset `2 x 2` grid with safe space from the spine, outer rails and
  head/foot rules, plus a compact gap between the two blocks on each page.
  Word wrapping keeps long localized copy at the same visible type scale;
  the cafe keeps its smaller `0.50 m`, FOV-40 framing. In both venues the page view uses an
  opaque two-leaf hinge rig for the `0.40 s` close/open motion. The moving leaf
  follows the upper arc and both page/cover blocks are separated in the closed
  stack, so neither material alpha nor z-fighting represents the closed state.
  `W/S` or D-pad wraps selection. In the bar, `Space`/West and the contextual
  `E`/`Enter`/South all order the selected offer; `Escape` alone folds and rests
  the booklet without ordering, and a rejected purchase remains open. The cafe
  keeps its selection-only contract: `Space`/West confirms the choice, while
  its first `E`/`Enter`/South still closes and rests the menu. Looking at that
  closed booklet changes the contextual action to unfold it and, through that
  exact same gaze/prompt predicate, enables its thin yellow contour. Looking
  away or beginning the unfold clears the contour, so the open spread is never
  highlighted; looking away changes the action to stand. The bar adapter fills
  two rows on each page with exactly four localized low-grade offers — flat
  house beer, cheap fortified wine, unaged distillate and bottom-shelf vodka. Every block
  carries a name, fixed price and two-line description; four matching shelf
  bottles are centred behind the counter. The cafe adapter remains three
  selection-only item names. A rejected bar purchase leaves the booklet open
  with failure feedback. Confirmation closes the booklet for the request gesture;
  completion revalidates, charges once and queues behind earlier guests.
  Intoxication/last-drink/count/stress commit only after drinking.
  Beer: full Walk to the central tap, `155 mm` mug, real handle/world stream;
  upright carry to the selected place, handle right. Guest-side reach/spine
  bend keep the palm on the handle until supported, without throw/roll. Wine,
  cognac and vodka use the selected shelf bottle. The bartender reaches its
  live position with the full Walk, takes it only when the right hand makes
  contact, then walks to a stable upright preparation vessel at the server edge
  opposite the resting menu. Its scaled shell stays above the counter through
  lift, transfer and lowering; the tilted bottle also retains wood clearance. He pours into the matching `WineGlass`, `Snifter` or
  `ShotGlass`, carries the full vessel to the selected place and sets it down,
  then returns the bottle to its exact shelf pose. While every full vessel
  waits, the same gaze predicate
  drives its thin yellow contour and the localized `E` drink prompt. The seated
  world body owns base `2 s` pickup, `3 s` sip and `2 s` return clips.
  For beer its right hand grips the handle directly. The seated
  first-person camera starts from the gaze that accepted the mug and keeps its
  captured eye-space offset through the animated head's complete pose. It eases
  `0.02 m` back along that gaze at the mouth and temporarily uses a `0.03 m`
  near plane. During the sip the drinking rim stays at the
  mouth, the mug reaches horizontal, and the head and
  torso rise with it in the same restrained motion as the corrected bar
  patrons. All drinks use continuous right-hand Hero V2. Lean precedes contact;
  wrists lift for approach, carry until counter support, then retract.
  Clavicle reach uses upper arm, not nested wrist.
  Completion restores seated camera/look.
  Cached palm/thumb/finger anatomy orients grip; fingers close post-animation.
  Wine/snifter use cupped bowl grips, shot its body, beer its handle.
  View is head-tracked. Pickup/sip/return: manual yaw±12°/pitch±10°.
  Menu/repeat request: ordinary seated yaw±70°/pitch−25°…55°; gaze retained.
  Menu confirm/empty gaze/E:forward L wave in FPS, then two R-index MCP taps.
  Palm stays horizontal/wrist fixed; thumb/other fingers clenched above wood.
  `hand_grip.gestures`:PointingFist/adjacent IndexTap05–60; no counter penetration;
  Empty-beer repeat: typed R grip/short supported offer/release before charge;
  reach-fitted up to12cm, whole mug1cm from book/edge. Other vessels/no cup: no offer.
  Same mug→tap/planted server retract/return; other cups wait for bottle.
  A second explicit sip applies effects once.
  Accepted first/repeat payments briefly show actual debit/balance at left HUD
  with the shared payment cue; gesture/failure/spam never charges or notifies.
  Different menu order: contact pickup, .25m straight back
  with facing held, vertical lower at fixed X/Z. Open right palm braces a fixed
  top point. Hide only fully below/guest-occluded, then fetch next.
  Camera-local arms stay off; seated camera hides only head.
  Completing an order leaves the closed booklet available to reopen or to
  leave behind. Closing restores the exact seated camera; ordinary room DOF,
  not the menu override, is already active before service or third-person view.
  Exiting restores the exact pre-seat camera and world rig; only
  after the visible stand-up completes does the bartender take the booklet and
  carry it home, and a later sit starts a fresh delivery.
  On scene arrival the hero starts `1.10 m` farther into the entrance, leaving
  the restored chase-camera lens inside the closed door plane.
  Legacy drink IDs and purchase definitions remain readable for saved/session
  compatibility, but none of their five removed entries is exposed by the bar
  menu. Lifecycle teardown restores every transform,
  collider, camera, world-presentation, shadow, input and HUD state without refunding an
  already committed purchase. The whole bottle row remains inside the seated
  shot at 16:9 and 16:10, and repeated orders restore reusable vessels to their
  authored scale. The service camera sits above the counter at natural seated
  eye height. There is no green floor marker or visible emissive single-seat
  sign.

## Deferred

- Infinite streaming world and floating origin.
- Weather beyond the implemented deterministic
  clear/light-rain/heavy-rain/thunderstorm schedule with its rain field, rain
  bed, lightning flashes, thunder, wet street film and puddles:
  weather-driven ambient lighting or grading changes, wind-driven debris and
  volumetric light shafts.
- Player-drivable vehicles, a broader traffic simulation, or skating physics;
  the implemented City-only Route 01 passenger MVP remains route-driven. Fares
  and payment, destination selection, passenger persistence and live map
  tracking are deferred. Ambient NPC passengers are **not** deferred: two
  places are occupied by `CityBusNpcPassengerController`, and the systems map
  states the remaining gap.
- Multiple bespoke bar interiors.
- Mobile quality/render-profile parity; the current Windows/PC-targeted project
  retains only its PC quality level, render-pipeline asset and renderer.
- Additional bespoke 3D animation polish beyond the current in-place
  locomotion, face, hybrid fall, bed, smoking, cat-feeding and bus-passenger
  action set.
- Minimap, in-world GPS trail, route autopilot, and manual map zoom/pan.
- Sobering mechanics, long-term save data/slots, income/jobs, a broader economy,
  combat outside the isolated test, online features and other NPC/quest dialogue adapters.
  Dated quests, the journal, local exchanges and the foreman's player-chosen
  dialogue branches are implemented.
- Final bespoke art and audio masters, accessibility and platform release work.
  RU/EN localization itself is implemented and carries every in-fiction line;
  what remains is coverage of strings added after a feature lands.
- Bar minigames: the original four sprite minigames are cut entirely; any
  future bar activities start from a new design. The cemetery gravedigging
  acts are the first minigames built on that footing — city-side, and on the
  surviving `BarMinigameModalLock` rather than the removed catalog.
- Multi-ingredient cocktail ordering, mixture state/UI and multi-bottle
  choreography. The active ordinary bartender, physical menu, central-tap beer
  service and all three single-bottle routes are already present.

South City Rollers/Skaters: procedural-world/sprite reference only; no imported code/assets.
