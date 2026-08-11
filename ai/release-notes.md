# Release notes

## Unreleased

### 2026-08-11 — The night bus now lights its way

- At night, the moving bus now casts two warm headlight pools onto the road and
  carries a soft amber wash through its passenger cabin. Both follow the body
  as it rides on its suspension, fade through dawn and dusk, and switch fully
  off during the day.

### 2026-08-11 — Bus suspension now moves vertically

- Fixed the production bus interpreting its imported FBX-local axis as world
  height, which made the cartoon bounce slide the vehicle forward and backward.
  The body now moves only along the bus vertical, while pitch and roll use its
  actual runtime right/forward axes.

### 2026-08-11 — The bus now rides on cartoon suspension

- The moving bus body now bobs, pitches and leans gently on its springs while
  all four wheels stay planted on the road. Its route and collision remain as
  stable as before.
- Every Route 01 stop now lasts exactly ten seconds in total, including the
  existing door-opening and door-closing transitions.

### 2026-08-11 — Bus doors now fold correctly

- Fixed both bus doorways rotating their complete frame and paired panels as
  one wide central slab. Each doorway now keeps its outer posts fixed and folds
  two upright leaves inward from their real outer hinges, then closes cleanly
  before the bus leaves the stop or returns to its pool.

### 2026-08-11 — Route 01 now winds through the whole city

- Route 01 is no longer a square loop around Central Park. Its deterministic
  closed route now reaches a stop near every district point of interest and one
  near the player's home: five stops in the default city.
- Stops remain off the target lot itself. Nightlife's Last Route Island now has
  a working blue `01` pole nearby and outside its public space, while the worn
  island structures stay an abandoned place rather than becoming the pole.
- The full-size bus uses only body-clear street geometry. Long, smooth
  centerline shifts make selected Road v2.1 right turns safe; ordinary tight
  right turns remain forbidden, and shared signal intersections are accepted
  only when the complete bus clears both physical poles.
- The City map now draws the winding blue loop and five default numbered stop
  markers. Home reconstructs its nearby pole as a static, non-blocking exterior
  detail, but still does not spawn or simulate a bus from the balcony camera.

### 2026-08-11 — Three-way bus junctions no longer break walkers

- Fixed Home initialization failing when a pedestrian route reached a
  three-way Road v2.1 bus junction. Walkers now stay on continuous,
  axis-aligned one-metre sidewalks around the widened corner, while the bus
  keeps its full clear turning apron.

### 2026-08-11 — Route 01 now has a real loop, stops and map identity

- The city bus now repeats one counter-clockwise Route 01 around Central Park,
  always passing Industrial, Nightlife, Residential and Old Town in that order.
  It no longer chooses random street branches or changes its route to chase the
  hero.
- Four named stops now belong to the route itself. Each has a physical blue
  `01` pole in the city and receives one `3-5 s` door-open call per lap; random
  roadside decoration no longer places misleading bus shelters.
- The route deliberately passes the street beside Nightlife's Last Route
  Island but does not stop there, so the abandoned island remains a non-working
  stop rather than becoming new transport infrastructure.
- The city map now shows the blue ink-outlined bus loop beneath the orange bar
  itinerary, four numbered localized stop markers with hover labels, and a
  compact route/stop legend. Live bus tracking and boarding remain deferred.
- Fog-hidden activation now chooses only positions whose forward travel on the
  same loop can bring the bus toward the hero. It prefers `76-86 m`, uses the
  denser-fog `56-86 m` fallback only when necessary, and still keeps at most one
  bus active or potentially visible.

### 2026-08-11 — One real bus now crosses the city

- Added a full-size `8.25 m` ambient city bus with a visible driver area,
  dashboard, handrails and twelve passenger seats behind its windows. Its two
  doors, steering, wheels, engine sound, headlights, brake/tail lights and
  cabin lighting respond to the trip instead of remaining a static prop.
- The bus follows the right side of ordinary streets and calls at compatible
  roadside shelters first. If its retained route reaches none, it receives
  exactly one deterministic service point on the route itself rather than
  pretending that a distant shelter belongs to it. The bus yields to the hero
  and passers-by, stops for a few seconds with both doors open, then continues
  without offering a boarding interaction yet.
- At most one bus can be active or potentially visible. It enters and leaves
  through the distant fog, so quiet intervals with no bus on screen are an
  intentional part of the street rhythm rather than a failed spawn.
- The ambient bus is intentionally limited to City. The real street below the
  Home balcony ends visibly and has no pass-through with two fog-hidden seams
  that can contain the complete vehicle. Pedestrians remain below the balcony,
  but the view does not invent another road or pop a bus in and out when the
  camera shot changes.

### 2026-08-11 — Road v2.1 opens real turning space

- Selected four-way intersections now move their small corner sidewalk pads
  outward and cut their raised approach curbs back by `4.5 m`, leaving a flush
  shared asphalt apron clear for the bus's proven long-body left turn while
  keeping the pedestrian route continuous.
- Walkers follow those displaced corners, and the Home balcony reconstruction
  shows the same junction geometry. Signalized zebra intersections remain
  separate, avoiding poles and crossing paint inside the bus turning apron.

### 2026-08-11 — Road v2 gives the city room for traffic

- Ordinary streets are now `8 m` wide, with a `6 m` asphalt carriageway and
  the existing raised `1 m` sidewalk on each side.
- Intersections expand with the streets, keeping a clear `6 x 6 m` driving
  apron inside each `8 x 8 m` junction core; zebra crossings now span the full
  carriageway.
- The generated grid step grows from `24 m` to `26 m`. Buildings, entrances,
  pedestrians, roadside fixtures, fences, the city map and the street below
  the Home balcony all follow the new canonical dimensions automatically.

### 2026-08-11 — Passers-by now reach a waiting player

- Fixed pedestrians spawning beyond the City camera and then choosing random
  distant turns indefinitely. A fresh walker now follows sidewalk branches
  toward a stationary player until reaching the nearby `24 m` encounter area.
- The original `76-86 m` spawn band remains preferred. Where the generated
  sidewalks split into disconnected components, a linked point from `32 m`
  may be used instead; dense fog still hides the activation.
- The assistance ends after that first approach, so pedestrians resume ordinary
  random roaming rather than following the player. Zebra-crossing choices,
  population limits and sparse night timing are unchanged.

### 2026-08-11 — The stairwell now shows its age

- The apartment stairwell's walls, concrete floors and steps, rusty metalwork,
  battered doors, damp patches, discarded rubbish and blocked upper flight now
  have distinct worn surface textures instead of flat colors.
- Texture variation stays consistent across repeated steps and fixtures while
  preserving the existing cold green palette, warm apartment landing, fixed
  cameras, lighting, cat encounter and fully unchanged walkable route.
- The maps now preserve the stairwell's previous average surface brightness
  instead of multiplying it into near-black, while broader wall, door and
  metal wear remains visible through the low-resolution presentation.

### 2026-08-11 — Daytime passers-by arrive sooner

- Fixed daytime streets feeling much emptier even though `06:00` already uses
  the daytime population rules. Walkers still appear safely inside the distant
  fog, but their hidden approach and departure now advance faster until they
  near the visible street, where they resume their ordinary walking pace.
- Night remains deliberately sparse with one fresh slot, long delays and no
  hidden acceleration.

### 2026-08-10 — Ground between buildings now has a surface

- Exposed land between city buildings now uses a dark, compacted soil texture
  with restrained moss, grit and tiny stones instead of a single flat color.
  Its scale stays continuous across generated lots, and the apartment balcony
  view uses the same ground appearance.

### 2026-08-10 — City map stays inside its frame

- Roads, park paths and landmark strokes on the scrollable full-screen map no
  longer spill into the title or scatter across the surrounding interface.
  Panning now keeps every line cleanly clipped to the map viewport.

### 2026-08-10 — Passers-by now follow the hero, not the camera

- The city no longer creates and simulates a fixed crowd at load. Up to two
  passers-by now appear at different, obstacle-free points `76-86 m` from the
  hero, where the City fog already hides them, and disappear only after moving
  beyond `88 m`. Looking away no longer makes a nearby walker vanish, and the
  camera no longer controls where one can appear.
- Spawn timing and placement now vary strongly between runs. The first walker
  waits a random moment, and the second receives a separate wider delay, so
  streets can naturally contain zero, one or two walkers instead of filling
  both slots together.
- At night (`19:00-06:00`), new walkers are much rarer and only one slot may
  spawn. A second walker already on the street at dusk is allowed to leave
  naturally instead of popping out when the clock crosses `19:00`.
- Pedestrians keep walking forward through connected sidewalks and can turn at
  corners instead of pacing back and forth on short segments. Dead-end branches
  are excluded from their navigation graph.
- When a walker passes a zebra crossing, they now independently choose whether
  to continue along the pavement or cross to the other side.
- Looking out from the apartment balcony now uses the same local population on
  the reconstructed street below. Walkers exist only during the Balcony shot
  and are immediately pooled when the player returns indoors.

### 2026-08-10 — Pedestrians stay upright at route ends

- Ambient walkers no longer pitch their complete model sideways when a curb,
  entrance apron or controller contact leaves a small height difference at the
  final waypoint. Route-facing and endpoint completion now use only the XZ
  plane, while physical height correction remains independent.

### 2026-08-10 — Roads now have sidewalks and zebra crossings

- Ordinary streets now read as dark asphalt carriageways bordered by raised
  pedestrian sidewalks on both sides. The previous light road texture has
  been retained for the pavement instead of discarded.
- Center dashes are now textured white traffic paint. Selected ordinary
  signalized intersections also receive four-stripe zebra crossings, with no
  center markings drawn through them; park paths remain visually separate.
- Ambient walkers now travel along sidewalk centers instead of the roadway,
  and returns from bars, Home or the supermarket place the player on the
  frontage sidewalk. The Home balcony view reconstructs the same street
  surfaces and markings without adding collision.

### 2026-08-10 — Streets no longer fence off the city

- The player can now leave the road and explore real open space between
  buildings wherever no structure or large object blocks the way. Water,
  missing terrain and the outside edge of the city remain inaccessible.
- Road rails now appear only at genuine dead ends and unsafe map or water
  boundaries. They physically stop the player, while streets that continue
  into the park and ordinary roadside ground stay open.
- Buildings, bulky street and park decoration, benches, hedges, trees, lamp
  and signal poles now provide appropriate physical obstacles without making
  small clutter needlessly snag the player.
- Nearby passers-by now have a physical presence: the player cannot walk
  through a visible pedestrian, while walkers avoid blocking one another and
  remain confined to their street routes.

### 2026-08-10 — The streets now look like asphalt

- City roads now use a continuous worn gray asphalt surface instead of a flat
  color. Its grain keeps a consistent scale through straight segments and
  intersections rather than stretching across each generated road piece.
- The apartment balcony view reconstructs the same road appearance outside
  Home. Park paths, painted road dashes and the shape or collision of every
  walking surface remain unchanged.

### 2026-08-10 — The city now has passers-by

- City streets now feel inhabited by a small ambient population that walks
  short routes, pauses at route ends and turns back. Routes favor entrances,
  public places and park gates instead of spreading people uniformly.
- The first resident is a slightly bizarre low-poly figure in a long coat with
  a lampshade-like hood, dark recessed face, rigid parcel bag and mismatched
  boots. Four muted palettes vary the same silhouette.
- Nearby walkers reuse the hero's existing Idle and Walk animations. They are
  passive atmosphere with no prompts or gameplay reactions; visible walkers
  now physically block the player while pooled/distant routes stay light.

### 2026-08-10 — Hunger and fatigue now build over time

- After the opening Wake, hunger now rises from `0` to `100` over 24 game hours
  and fatigue over 18. Both pause together with the game clock in the
  inventory and pause menu, then continue across locations and ordinary
  interactions.
- The existing inventory Status bars show the changing values. Eating reduces
  hunger from its current point, while completing the full bed wake resets
  fatigue; cancelled sleep grants no rest.
- Hunger and fatigue are visual status values for now and apply no movement,
  interaction or other gameplay penalties.

### 2026-08-10 — Sleep now clears fatigue

- The inventory Status panel now includes a fourth localized Fatigue bar
  alongside buzz, hunger and stress. Fatigue is a persistent `0-100` need and
  a new run starts at zero.
- Finishing the complete bed wake now restores fatigue to zero. Cancelling the
  sleep interaction or leaving during the wake does not grant the reset.

### 2026-08-10 — Drunken falls now react physically

- Failing a high-intoxication balance check now hands the hero from the
  directional stumble into bounded ragdoll physics, so his impact, limbs and
  brief time on the floor are driven by the surrounding collision geometry.
- The gameplay position remains safely anchored while the body falls. The hero
  then settles smoothly onto his side, braces, rolls onto his hands and knees,
  pauses on all fours, plants one foot, pushes through a low crouch and stands
  before returning to player control. Both fall sides use complete dedicated
  poses, so his limbs no longer flash through a stiff default stance while he
  gets up.

### 2026-08-10 — Drunken walking stays on the floor

- The hero's visible boots now stay planted on the ground throughout the
  walking cycle, including at maximum intoxication. Sway and bent knees no
  longer push the 3D body through floors, while physical movement and
  contextual action clips keep their existing positions.
- Drunken sway now rotates and articulates the body around its authored
  position instead of sliding the entire 3D character left and right across
  the screen.

### 2026-08-09 — A small lamp now marks the apartment exit

- A compact dirty-amber wall lamp now sits above the apartment entrance door.
  Its dark hood and restrained glow replace the old oversized luminous header,
  while a real warm spotlight now illuminates the door, nearby wall and entry
  floor instead of leaving a lone yellow point in the darkness.

### 2026-08-09 — Smoking now reaches the lips

- The balcony animation now shows the complete gesture: the hero settles
  toward the city, retrieves the cigarette, raises it to his lips for a held
  inhale, lowers his hand for the exhale and flicks it away before relaxing.
- The cigarette is now a cigarette-sized prop rather than an oversized cigar.
  It extends correctly from the fingers, appears only after the hand leaves
  the coat and disappears with the exit flick. Its real scene size is no
  longer multiplied by the imported hand-bone scale.
- Every smoking loop now releases a dense, clearly visible gray-green plume
  from the hero's mouth during the outward exhale. It drifts toward the city,
  separates from the moving head and fades before the next breath.
- A worn enamel ashtray now sits permanently on the outer balcony rail,
  directly below the point where the cigarette is flicked. It remains visible
  whether or not the smoking interaction is active.

### 2026-08-09 — Getting out of bed now uses the bedside

- The hero now uses the long side of the bed nearest the apartment door:
  first sitting on its edge, then swinging his legs onto the mattress and
  lowering himself with arm support. His head remains at the pillow instead
  of sleeping head-to-foot in reverse.
- Waking now rolls toward that edge, moves both legs off the mattress, settles
  into a visible seated pose with planted feet, leans forward and only then
  stands. Ordinary entry and wake take three seconds; the opening keeps its
  existing six-second cinematic wake.

### 2026-08-04 — Walking and idle feel more natural

- The hero now bends both elbows, knees and ankles through a complete walking
  cycle instead of passing through a stiff neutral stance between steps.
  Arms counter-swing with persistent elbow flex while each leg visibly loads,
  passes and plants.
- Standing still now has a longer, more readable breathing and weight-shift
  loop with subtle movement through the torso, head, arms and knees.
- Starting and releasing movement now eases between idle and walking; the gait
  also slows with the visible blend instead of changing cadence abruptly.

### 2026-08-04 — The 3D hero now faces his movement

- Corrected the imported model orientation so the hero walks face-first in
  City and every interior instead of appearing to move backwards. Physical
  left/right details and all existing 3D animations remain on their intended
  sides.

### 2026-08-04 — The hero is now fully 3D

- The main character is now one continuous modular low-poly 3D model in the
  City, Bar, Supermarket, Home and Stairwell. His separate head, torso, hands,
  arms, legs and feet remain individually addressable, and the left-arm
  bandage, right-shoulder patch and diagonal strap keep their physical sides.
- Walking, idle expressions, intoxication sway, balance reactions and
  left/right falls now animate the 3D skeleton. The real meshes cast world
  shadows while a small grounded contact patch keeps the feet readable.
- Sleeping, balcony smoking and feeding the stairwell cat now play continuously
  on that same visible model, including their guided approach and exact exit
  restoration. The cigarette follows the character's right-hand socket; the
  cat retains its own pixel-art animation.
- Bar drinking and the refrigerator use first-person arms taken from the same
  model and clothing instead of a separate hand design. The inventory portrait
  has also been replaced by a dedicated transparent render of the 3D hero.
- Retired player atlases and the sprite-shadow proxy are no longer part of the
  runtime. Pixel-art NPCs, the stairwell cat and minigames are unchanged.

### 2026-08-04 — Eastern lake, cemetery and scrollable city map

- The playable city now includes a reachable lake on its north-eastern edge
  and a reachable cemetery on its south-eastern edge. The lake has a walkable
  shore around blocked water; both areas have recognizable physical landmarks
  and dedicated openings from the existing street network.
- The city map keeps its details readable when the expanded territory exceeds
  the frame. It can pan horizontally and vertically with WASD, the right stick,
  mouse wheel gestures or middle/right-button dragging, and shows only the
  scroll indicators that are needed.

### 2026-08-04 — City-map test teleport

- The City F9 debug window can now enable test teleportation. While enabled,
  any object lot on the map can be selected; confirming `Teleport? / Yes`
  moves the hero to its street-front or nearest route point. Normal bar-route
  planning remains unchanged when the debug toggle is off.

### 2026-08-04 — A coastal, rearrangeable city foundation

- The default city now opens onto a full-width beach and waterline along the
  north edge of the map. The beach is reachable from the street and walkable
  to the shore; the water is visible on the world and map but cannot be entered.
- Central Park remains fixed at the city/map center while the generated city
  can use connected irregular outlines instead of filling a perfect rectangle.
  Area names and colors now follow stable area identities rather than their
  current position.
- The city constructor can reuse built-district styles and includes generic
  lake and cemetery area profiles with suitable ground/water presentation and
  street access. They were introduced here as authored-layout foundations and
  are now also used by the playable default city.
- The selected city layout is stored with the session, so the City scene and
  the exterior seen from Home always use the same blueprint and seed.

### 2026-08-04 — Smoother day/night updates

- City and Home no longer repeat unchanged global-lighting, environment and
  lamp-pool work every in-game minute during stable day or night. Movement
  remains responsive while the clock continues advancing normally.
- Dawn, dusk, exterior lights, Home window lighting and Balcony restoration
  retain their existing visual behavior.

### 2026-08-04 — A 24-minute day and working room clock

- A fresh run now holds at `05:59` until Wake Up is chosen. That choice starts
  the room clock from `06:00`; it then shows the real in-game time and keeps
  the same time after moving between scenes. The inventory Status panel also
  shows the current in-game `HH:MM`.
- A complete in-game day lasts exactly 24 real minutes on gameplay time. City,
  apartment-window and balcony lighting move through dawn, day, dusk and night,
  while Bar, Supermarket and Stairwell keep their existing interior look.
- The city's gray-green fog, matching horizon, `48 m` visibility, drifting fog
  field and noir color grade remain unchanged throughout the cycle.

### 2026-08-04 — Hunger, stress and usable provisions

- The inventory Status panel now shows compact intoxication, hunger and stress
  bars alongside the hero portrait and cash, without a redundant textual
  intoxication-stage label. Fresh runs start at zero hunger and zero stress;
  neither value rises on its own yet.
- Existing food can be eaten from the inventory when it has an effect. All
  current cheap food leaves at least `20` hunger, while a no-effect use keeps
  the item.
- Alcohol now relieves stress according to its own value while retaining its
  intoxication. Bar service and every drinking minigame use the same committed
  rule, including proportional Split-the-G sips; the supermarket vodka bottle
  is consumed as four servings.

### 2026-08-04 — A music slot for the supermarket

- The supermarket now supports its own looping `supermarket_theme`, routed
  through the shared music mix with smooth scene-entry and scene-exit fades.
- The supplied supermarket track is included; the shop remains fully playable
  if that optional resource is unavailable.

### 2026-08-04 — Clearer city-map landmarks

- The city map now marks the grocery shop with its own shopping-bag symbol.
- Hovering a bar, home, grocery shop or public place shows its localized name
  in a high-contrast tooltip that stays inside the map, including near edges
  and tightly grouped landmarks.

### 2026-08-04 — A supermarket with stock that stays gone

- The city now contains one deterministic street-front supermarket. Entering
  it loads a separate worn shop interior with three shelf sections, a
  decorative checkout and a cashier behind the register.
- The five cheap shelf goods are a chicken egg, vodka bottle, closed stew can,
  instant noodles and a day-old loaf. Activate a shelf, select the physical
  product with mouse, keyboard or gamepad, and buy it with the same session cash
  shown in the inventory.
- The shelf camera now centers every selected product and follows selection
  across all three shelf sections without leaving the browser. Quiet clickable
  arrows sit immediately beside the product; the same previous/next actions
  work from keyboard and gamepad, skip empty shelves and continue after the
  last item on one shelf is bought.
- A successful purchase adds one item to the hero's inventory and removes that
  exact model from the shelf immediately. It stays gone after leaving and
  returning to the shop, and stock resets only when a new game begins.
- The sealed stew is a separate item from the already-open refrigerator can,
  so it cannot be used to feed the stairwell cat.

### 2026-08-03 — Natural entry and exit for contextual animations

- Pressing `E` for bed sleep or balcony smoking, and confirming cat feeding,
  now enters a visible `Positioning` step: the ordinary hero walks through the
  real grounded movement constraint and turns into the authored direction
  before the special animation begins. An unreachable height, blocked
  no-progress route or scene/lifecycle interruption cancels cleanly instead of
  teleporting the hero into place.
- Contextual animations now own separate entry and exit poses. The ordinary
  hero returns at the exact authored exit point and facing after the final
  frame instead of relying on one shared hidden teleport location.
- At exact entry, the ordinary puppet settles into one neutral rendered
  handoff frame before the atlas appears. Bed sleep and cat feeding match the
  camera-plane `FrontLeft` endpoint; balcony smoking stays upright in world-up
  mode and matches `BackRight`. MainRoom and Bathroom retain camera-plane
  player sprites, while the Balcony shot uses world-up yaw.
- Bed sleep, balcony smoking and cat feeding now change visibility directly at
  their matching endpoints with no sprite alpha fade. The smoking atlas also
  no longer uses dissolve bridge frames.
- The last exit frame is always presented before the interaction becomes idle.
  The restored neutral puppet stays stable through its final render handoff,
  avoiding a one-frame gait, facial-expression or direction pop.
- Camera-plane sequences resolve the upright endpoint hip from the live camera
  up axis and correct the atlas pivot again during `LateUpdate`, keeping feet
  and hips aligned while the camera settles.

### 2026-08-03 — Feed the stairwell cat

- Interacting with the cat now offers `Talk` or `Interact`. Talking keeps the
  cat's familiar silent response.
- If the hero has no open stew, he notices that the cat is hungry but has
  nothing to offer. With stew in the inventory, he can answer a default-No
  `Feed the cat?` confirmation.
- Choosing Yes consumes exactly one can and plays a new fixed-camera sequence
  in which the hero places the food and the cat eats. Both characters return
  cleanly to ordinary stairwell play afterward.
- The same item-aware choice and confirmation flow is now reusable by future
  world targets rather than being hard-coded only for the cat.
- Interaction prompts now expand to their localized text and wrap when needed,
  so the longer Russian hungry-cat message stays fully inside its panel.
- The feeding hero and food can now face the cat correctly in the fixed
  stairwell shot instead of appearing horizontally reversed.

### 2026-08-03 — Quieter apartment music

- The apartment's background theme now sits noticeably lower in the mix,
  without changing music volume in the city, bars, stairwell or the dedicated
  balcony-smoking sequence.

### 2026-08-03 — Inventory in your coat pockets

- Press `I` or gamepad North during free gameplay to open a fullscreen
  PS1-style inventory in the city, bars, apartment or stairwell.
- The screen shows the hero's condition, intoxication level, dollar cash and
  carried items. Slot icons remain crisp and visible, while the selected item
  now appears as a lit, slowly rotating low-poly 3D model in the lower panel
  and the larger Examine view.
- The portrait now crops the actual neutral front player sprite, so its face,
  hair and clothing are the same authored pixels seen on the character.
- New runs begin with apartment keys and a lighter. Taking the vodka, egg or
  open stew from the refrigerator now moves it into the inventory, and the item
  stays gone from that refrigerator after leaving and returning home.
- Inventory freezes gameplay and safely owns input until closed; Escape returns
  directly to play without accidentally opening pause.
- Decorative labels, including the yellow opening title, now ignore pointer
  hover and press states instead of revealing a second tinted shadow copy.

### 2026-08-03 — Pause menu

- Escape and gamepad Start now open a localized pause menu throughout City,
  bars, the apartment and the stairwell.
- Pause stops gameplay time and non-UI audio while preserving existing modal
  ownership, so Escape still backs out of maps, activities and refrigerator
  inspection first.
- Resume restores the exact captured control state. Starting over or quitting
  requires an explicit confirmation that unsaved progress will be lost;
  save/load and settings entries are intentionally absent for now.

### 2026-08-03 — Detailed falls from every side

- Failed drunken balance checks now use a long hand-detailed sprite sequence
  for falling, lying on the floor and getting back up.
- Every one of the hero's eight viewing directions has separate left- and
  right-fall artwork, so his bandage, shoulder patch and satchel never flip to
  the wrong physical side.
- The authored silhouette also drives the directional shadow while the
  physical player remains safely upright and stationary.

### 2026-08-03 — Balance checks in motion

- Drunken balance warnings and active checks no longer stop the hero. Movement
  remains live while directional input also steers the balance arrow.
- A failed check still stops the motor for the visible fall, down and recovery
  sequence, then restores the exact captured control state.

### 2026-08-02 — Accelerating sobriety

- Intoxication now falls automatically during free gameplay. Recovery starts
  slowly at roughly one point per `12 s` at level `100` and accelerates toward
  one point per `3 s` near sober.
- Modal interactions pause recovery so a minigame cannot restore an older
  intoxication snapshot when it commits a drink result.

### 2026-08-02 — Fog restored in Windows builds

- The gray-green City and balcony fog now survives shader stripping in a
  Windows player instead of appearing only inside the Unity Editor.

### 2026-08-02 — A louder, living apartment

- The refrigerator motor is now clearly present in the Home mix, with both
  closed- and open-door timbres raised by `4 dB` while their smooth door
  crossfade remains intact.
- Every visible stutter of the bathroom fluorescent tube now produces one
  short spatial electrical crack at the real fixture, so the sound follows
  the light instead of running on an unrelated timer.
- Home now has an optional `home_theme` composition slot. It fades in indoors,
  fades out and pauses on the balcony, then resumes from the same musical
  position when the player returns inside.
- Scene compositions now cross scene boundaries cleanly: the next scene is
  preloaded while the current track fades out, then its own track fades in
  after activation. Empty optional music slots remain silent and safe.

### 2026-08-02 — A cigarette above the sleeping city

- A new interaction point on the apartment balcony lets the hero turn toward
  the city and begin a slow, melancholic cigarette sequence with `E`.
- The close camera eases toward the hero while he draws and lights the
  cigarette, then lingers through a looping rhythm of pauses, drags and
  wind-directed exhales. Pressing `E` again waits for a natural resting beat
  before the cigarette is discarded and the camera returns smoothly.
- The close framing now looks slightly farther toward the city, keeping the
  hero prominent while giving the skyline and street more room beside him.
  Its camera position, field of view and smooth return remain unchanged.
- The close shot no longer freezes once it arrives: a restrained, very slow
  positional and rotational drift gives it a melancholy breathing quality
  without pulsing the field of view. The motion enters with the camera move,
  stays continuous between smoking phases and settles completely during the
  smooth pullback.
- The smoking pose now faces outward toward the city and remains upright on
  the balcony floor while the close camera pitches down. Unlike the reclining
  bed presentation, it turns toward the camera only horizontally instead of
  inheriting the complete camera plane. Entering and leaving it uses a
  `0.35 s` dissolve plus idle-matched transition frames, removing the visible
  pop between the normal hero and the cigarette animation.
- The vignette has its own optional fading music slot. Add
  `smoking_theme.ogg` (or WAV/MP3) under
  `Assets/Resources/Audio/SmokingMusic/`; if no file is present, the scene
  plays normally in silence.

### 2026-08-02 — The real city outside the balcony

- The city visible from the apartment balcony now uses the same gray-green
  distance fog, moonlight, color grade and restricted visibility as the real
  City location instead of a separate dark, clear presentation.
- Roads, building faces, deterministic windows and the neighboring bar now
  share City's materials, colors and facade recipe. Nearby street and bar
  lights illuminate the view only while the balcony camera is active; the
  apartment's original clear warm lighting returns immediately indoors.

### 2026-08-02 — A grounded last-route island

- Removed the repeated cyan and magenta light bars from Nightlife's transport
  island and grounded the old departure board on visible supports.
- Weathered route plates, layered posters, faded timetable rows, a bin,
  discarded bottles, a torn schedule and a lost scarf now tell the story of an
  abandoned late-night stop without turning it into a neon installation.

### 2026-08-02 — Bathroom light reaches the apartment entrance

- Cold fluorescent light now spills through the ajar bathroom door across part
  of the main room and reveals the apartment entrance without changing the
  approved camera composition.
- The bathroom tube, its local glow and the doorway spill stay steady most of
  the time, then briefly stutter together like one failing fluorescent lamp.
  The effect keeps the existing bounded Home light count.

### 2026-08-01 — Readable hero inside the apartment

- Furniture, clutter, bathroom doors and balcony rails now dissolve into a
  coarse PS1-style dither only when they block the hero's head or body from the
  active Home camera. Every part of one object fades together instead of
  leaving detached cushions, shelves or rail posts on screen.
- Low objects may still cover the hero's feet, preserving the sense that the
  character stands inside the room rather than being drawn over it. Cleared
  objects return smoothly instead of popping, while their room lighting,
  shadows and ambient occlusion remain consistent during the effect.
- Collision, safety barriers, windows, lighting and the apartment shell remain
  unchanged. The effect returns objects to full opacity during the waking,
  bed and refrigerator presentations.

### 2026-08-01 — Four open district places

- Replaced the four facade-only POIs with full open city lots: Old Town's
  waterworks court, Residential's drying yard, Industrial's weighbridge and
  Nightlife's last-route island.
- Each place removes the ordinary building from its block, opens every side
  that meets a street and gives the player a distinct space to enter, cross and
  walk around. Public approaches remain clear of guard rails, street lamps and
  traffic signals. Custom blocks smaller than `18 x 18 m` safely omit these
  authored places instead of squeezing them into an unreadable footprint.
- Added free-standing district silhouettes and deliberate physical surfaces or
  obstacles instead of another decoration pasted onto a house. Nearby places
  also reconstruct in the same seeded exterior visible from Home.
- Updated the city map to draw these lots as open ground and mark them with
  four distinct symbols and localized names. They remain informational and do
  not alter bar routes or visited progress.

### 2026-08-01 — A city worth looking at

- Replaced the repeated box-only streetscape with seeded district character.
  Old Town now grows chimneys, dormers, scaffolding, markets and a clock-tower
  landmark; Residential gains balconies, rooftop laundry, discarded furniture
  and a communal greenhouse; Industrial receives stacks, tanks, pipe racks,
  cargo and a gantry; Nightlife adds billboards, fire escapes, vending queues
  and a cinema frontage.
- Added bus shelters, phone booths, dumpsters, utility cabinets, roadworks and
  bicycles along the route, plus a dry fountain/statue, bandstand, chess tables
  and playground inside Central Park. Every ordinary building now carries a
  distinct silhouette or facade treatment, and each district has a guaranteed
  large landmark.
- Made windows, balconies, signs and other facade pieces face the street that
  actually serves their lot. The same seeded details are visible from the Home
  balcony, so entering the apartment no longer swaps the neighborhood for a
  simpler parallel version.
- Kept the new layer lightweight: details are spatially batched, reuse shared
  materials and add no collision, realtime lights, particles or shadow cost.

### 2026-08-01 — Readable apartment exit lighting

- Added a local warm spotlight that makes the existing stairwell door readable
  on the right side of the ordinary MainRoom shot. There is no additional
  camera cut: all three Home camera poses, the door geometry and its material
  remain unchanged.

### 2026-07-31 — Cleaner game screens

- Removed persistent keyboard, mouse and gamepad instruction strips from every
  menu, inspector, map and minigame screen. Contextual interaction prompts and
  clickable actions remain, with buttons labeled only by their action.
- Made every contextual action prompt clickable with the pointer, including
  opening and closing the refrigerator, while preserving its existing
  keyboard and gamepad controls.

### 2026-07-31 — Physical first-person bar menu

- Replaced the ordinary full-screen drink list with a seated first-person
  counter scene. The camera glides to a natural seated eye height above the
  counter and shows the hero's procedural low-poly arms while the world remains
  visible behind a compact offer/price overlay. The green order-point floor
  marker and emissive sign hide during the complete menu presentation, then
  restore on return.
- Added nine individually selectable 3D retail bottles to the lower back-bar
  shelf. Every one is a real object with a solid collider, selection trigger,
  kinematic Rigidbody, visible label treatment and mouth anchor; mouse,
  keyboard and gamepad selection all address the same row. The seated shot
  keeps the complete bottle geometry safely framed at both 16:9 and 16:10.
- Confirming a purchase now makes the right hand pick up that exact bottle and
  pour it through a real world-space stream. Water uses a tumbler, beer a pint,
  wine a stemmed wine glass, vodka a shot glass and cognac a snifter; the
  matching 3D liquid volume rises inside the vessel before the left hand holds
  it at the mouth for a full three-second drink and returns it empty to the
  counter.
- Kept the existing atomic wallet/intoxication rules. Money and drinking state
  commit exactly once at confirmation, insufficient purchases stay in the
  browser, and Exit cannot interrupt or refund an already paid service.
- Finishing a drink now returns to the seated bottle browser instead of
  leaving the menu, so several orders can be placed in one visit. Camera return
  and control restoration begin only from the dedicated `EXIT` / `ВЫЙТИ`
  button or its `Esc` / gamepad `B` shortcut.
- Added complete cleanup for camera, controls, HUD, player rig and shadows,
  bottle transforms/colliders, vessel fill and stream state. F9 may replace an
  unpaid browser but cannot interrupt committed service. Reused vessel types
  restore their authored transform before every new order, while scene markers
  remain hidden across repeat orders until explicit exit.

### 2026-07-31 — Interactive Home refrigerator

- Rebuilt the apartment refrigerator as a larger, brighter and much more
  readable worn-enamel fixture. The kitchen counter now ends cleanly on both
  sides of it, and the nearby table moved deeper into the room to leave a
  comfortable approach.
- The refrigerator is now interactive. The camera glides into a first-person
  view, the hero's sleeved hand reaches for and turns the handle, and the door
  unseals and opens with dedicated sounds. It stays open for inspection until
  another interaction or cancel input, then closes and returns smoothly to the
  exact room view with normal control restored.
- Smoothed the first-person handoff: the hero now remains visible during the
  camera glide and disappears only when the low-poly hand enters the frame.
  After the door seals, the hero reappears immediately as the camera pulls
  back.
- Built the interior in detail with a lined cavity, three stained shelves, a
  lower drawer, frost, grime and two door bins. Its eight prepared storage
  slots initially show a vodka bottle, one chicken egg and an open can of stew.
- The three visible contents can now be highlighted directly. Hovering with
  the mouse shows a localized item name beside the cursor; keyboard and gamepad
  selection are supported as well.
- Clicking or confirming an item flies it into the center of the screen in the
  style of a classic PS1 survival-horror examination. The room darkens behind
  it, the model rotates slowly, and localized name and description text appear
  with `Take`, `Use` and `Back` choices. `Take` and `Use` are safe unavailable
  placeholders for now; they do not remove the item or alter the session.
- Returning, cancelling or interrupting the inspection puts the item back in
  its exact original place and restores its normal appearance before the
  refrigerator may close.
- Opening the door brings up a cold interior glow and smoothly crossfades
  between two synchronized procedural refrigerator loops: a muffled
  closed-cabinet hum and a brighter exposed motor/fan loop. Localized Russian
  and English prompts cover browsing, inspection, actions and closing.

### 2026-07-31 — Windows Player material fix

- Fixed the Windows build rendering the runtime-composed room and world in
  solid purple. All ordinary geometry now uses an explicitly packaged shared
  URP material, matching its correct Editor presentation.

### 2026-07-31 — PS1 waking opening and Home alarm clock

- The build now begins behind a black launch boundary and opens on the hero
  already asleep in their existing room instead of dropping directly into the
  city.
- Pressing Play in the Unity Editor now follows the same opening from any
  currently selected scene; manual scene selection is no longer required.
- The first Home shot now stays on the alarm clock at `05:59` for five silent
  seconds with no available buttons. The whole red display flickers off
  briefly at three-second intervals; the localized `ПРОСНУТЬСЯ / WAKE UP` or
  `ВЫЙТИ / QUIT` menu then appears while the shot stays silent and the
  flickering `05:59` remains unchanged.
- Choosing Wake Up switches the clock to solid `06:00`, starts the alarm and
  hides the menu. It rings and rattles for three seconds while the camera stays
  on the clock and the hero remains asleep. The alarm then stops and only then
  does the camera arc to the sleeper and ease into the normal Home shot
  without a final cut. The one-shot wake itself takes six seconds—three times
  the ordinary bed wake—before control returns without a reload.
- Added a low-poly 3D alarm clock and nightstand beside the bed. Its generated
  mechanical ring is spatial, passes through the shared Home audio treatment
  and visibly rattles the clock during the opening.
- The clock remains as silent room dressing on normal Home visits, and the
  usual apartment exit through the stairwell continues into the generated
  city.

### 2026-07-31 — Deterministic home/bar frontage repair

- Fixed generated homes choosing a street beside a bar's lot instead of the
  street containing its actual entrance. Default fresh sessions now begin at
  the intended shared home/bar approach.
- Kept custom layouts bounded to a maximum `48 m` traversable route instead of
  allowing some deterministic seeds to fail city validation during startup.

### 2026-07-30 — PS1 horror audio mix and interior soundscapes

- Added one shared scene-aware audio mix for music, ambience, gameplay sounds
  and UI. Existing City, Bar and Stairwell music now passes through it instead
  of bypassing the environmental treatment.
- Added separate City, Bar, Stairwell, Home and door-transition profiles with
  master headroom, compression and dedicated reverb/echo returns. UI remains
  dry and readable.
- Rebuilt the stairwell soundscape around an uneasy concrete bed, spatial
  ventilation and electrical buzz, plus sparse pipe knocks, metal stress,
  distant water and movement. Its profile has the longest, strongest reverb,
  a dark high-frequency rolloff and restrained echo.
- Rebuilt the Home soundscape as a calmer contrast: a soft room bed, spatial
  refrigerator and balcony night air, plus sparse wood, radiator, radio and
  bathroom details under a short damped reverb without echo.
- Kept the new material procedural, deterministic and deliberately
  low-resolution instead of introducing copied commercial sound assets.

### 2026-07-30 — Decayed stairwell between home and street

- Added a separate playable stairwell between the exterior home entrance and
  the hero's apartment. Going home now means entering the building, climbing
  two flights and using the apartment door; leaving follows the same route in
  reverse and returns to the same exterior entrance.
- Built a ground-floor lobby, an intermediate landing, the apartment-floor
  landing and a further upward flight. The upper flight is visibly buried
  under furniture, wire mesh, planks and sacks and is physically impassable.
- Gave the space a dark industrial-horror treatment with stained concrete,
  rusty railings, exposed pipes, vents, grilles, electrical cabinets,
  radiators, damp damage and trash.
- Added flickering practical lights, a green desaturated image grade, sparse
  dust and a procedural bed of ventilation, mains hum, pipe knocks and distant
  drips.
- Added a scene-local `stairwell_theme` music slot. Dropping a WAV, OGG or MP3
  into `Resources/Audio/StairwellMusic` starts it only in the stairwell and
  stops it automatically on the next Single-mode scene transition.
- Fixed the player-radius navigation seams at the lobby and landings, so normal
  movement can enter and traverse the staircase instead of stopping at the
  first riser.
- Added three fixed cinematic camera angles for the lower flight, middle
  flight and apartment floor, with stable hysteresis when crossing a landing.
- Rebuilt the fluorescent fixtures so their suspended glowing tubes are no
  longer hidden inside their housings, remain visible in their matching camera
  angles and cast stronger readable pools without removing the darkness.
- Added a seated pixel-art cat to the upper rail of the intermediate landing.
  It remains composed with its back to every fixed camera while turning its
  head toward the hero, cycles through quiet idle movement and performs a rare
  eight-frame grooming animation roughly every 36 seconds.
- The cat can be addressed through the normal interaction control. For now it
  returns a short localized text placeholder without stopping the player.

### 2026-07-30 — PC-only rendering configuration

- Removed the unused mobile URP asset, renderer and quality preset. The current
  Windows version now retains a single PC quality/rendering configuration;
  this does not change its presentation, while mobile support remains deferred.

### 2026-07-30 — Window and walkable third-floor balcony

- Replaced the black right side of the Home interior with a real window and an
  open glazed door leading onto a balcony in the same scene.
- The balcony is fully walkable at third-floor height and overlooks the same
  seeded street as the exterior home. Its open rails keep their light
  silhouette while an invisible safety boundary prevents accidental falls.
- Nearby roads, buildings, lit windows, lamps and signals now continue beyond
  the room instead of ending in darkness; the City version of the home has the
  matching balcony facade.
- A cold, shadowed shaft of night light enters through the window, while the
  existing warm room lamp and cold bathroom tube keep their previous lighting.
  The window and door use shared transparent glass.
- Added a dedicated fixed camera shot that takes over when the hero steps
  through the door and onto the balcony.
- Sealed unintended ceiling, side-wall and front-entry gaps, removed the stray
  orange exit marker from the camera edge, and kept exterior scenery strictly
  outside the facade without changing the window, open door or walkable
  balcony.

### 2026-07-30 — Animated sleep at home

- The bed in the Home interior is now interactive from its open side. Press
  `E` once to lie down and fall asleep; the hero remains in a breathing sleep
  loop for as long as desired, and a second `E` wakes them up.
- Added a bespoke 64-frame sequence with a full lie-down, persistent sleeping
  loop and separate wake-up animation. The normal walking puppet and both of
  its shadows return only after the wake-up finishes.
- Slowed the sleeping loop to one five-second breath: the chest rises at
  `4 fps`, pauses briefly at full inhale, then settles into a longer rest
  after exhale.
- The sleeping hero now follows the bed's perspective, keeps their head on the
  pillow side and sits evenly within the mattress instead of appearing
  mirrored or screen-horizontal. The full sleeping silhouette now clears the
  bedding instead of visually sinking into the mattress and blanket.
- Movement remains locked for the complete sleep interaction, while the wake
  prompt becomes available during sleep in both Russian and English.

### 2026-07-30 — Approved Home framing and practical lights

- Moved the main-room fixed camera into the approved bed-side corner at
  `(-4.48, 3.00, -3.25)`, Euler `(28°, 55°, 0°)`, with a `64°` FOV. The
  bathroom now uses `(1.82, 2.20, 0.86)`, Euler `(30°, 38°, 0°)`, with a
  `92°` FOV.
- The warm hanging bulb and cold bathroom tube are now visible HDR emitters
  with halos physically aligned to the light they cast, so illumination has a
  readable source in both shots.
- Blocking junk closes the camera corner without obstructing the authored
  walking routes, and the bathroom toilet now faces naturally into the room
  with its cistern at the right wall.
- The hero now aligns to the complete fixed-camera plane instead of only its
  horizontal direction. This preserves the original `64 x 96` sprite
  proportions in steep views and automatically returns to normal billboard
  behavior after leaving the fixed-camera controller.

### Player home

- Every generated city now contains one recognizable player home beside a bar
  street. Its teal facade, cool windows, porch light and mailbox distinguish
  it in the world, while the city map gives it a separate labeled house icon.
- The interior is now a dim, neglected old alcoholic's bachelor flat: stained
  walls, a boarded dead window, six main-room furniture groups, worn bedding,
  dirty dishes, bottles, cans, an ashtray, old papers, a radio and sparse
  personal remnants sell long-term poverty and drinking without blocking the
  walking routes.
- Added a complete separate bathroom with tiled surfaces, an ajar doorway,
  toilet, shower and curtain, sink, cracked mirror, rusty exposed pipes, leak
  damage and a floor drain.
- A visible dirty-yellow hanging lamp and cold bathroom tube sit over a
  subdued home-only color grade, sparse dust and a dedicated refrigerator,
  mains, pipe and drip ambience.
- The single Main Camera now hard-cuts between fixed main-room and bathroom
  corner shots. Wider hold areas add hysteresis at the doorway, so hovering at
  the threshold cannot flicker the view; orbit input does not move either
  fixed pose. Home temporarily aligns the player's billboard to the complete
  camera plane, preventing both edge-on and vertically compressed sprites.
- Entering and leaving still use the same door transition as bars and return
  the hero to the matching exterior approach without losing route, visit,
  wallet or intoxication progress.

### Bar-adjacent city start

- A fresh run now places the hero on a safe street node beside their home and
  its neighboring generated bar instead of at the distant city center.
- Returning from a bar remains unchanged and still restores that specific
  bar's entrance position.

### Bar drinks and session wallet

- Every bar now has a separate counter point where the player can buy one of
  nine ordinary drinks without starting or completing the bar's minigame.
- A fresh session starts with `$999`. The shop shows each price, the current
  balance and the resulting intoxication before confirmation; successful
  purchases deduct cash and consume the drink immediately.
- Water remains available at maximum intoxication, costs `$2` and does not
  sober the player. Unavailable or unaffordable purchases leave both cash and
  drinking progress unchanged.

### Opaque player hands

- Restored the missing skin and bandage pixels in both lower arms across all
  eight player directions, so the character's hands no longer show the world
  through transparent gaps.
- Rebuilt the jointed puppet atlas without changing the character design,
  facial artwork, directional silhouettes or animation hierarchy.

### Lower third-person framing

- Raised the exterior and interior camera aim points so the hero now occupies
  the lower part of the screen and leaves more view ahead while walking.
- Kept the existing camera distance, field of view, orbit and obstacle
  behavior unchanged.

### Physically raised city surfaces

- Streets and park paths now use their rendered height as a real walkable
  surface. The player steps onto them instead of sinking through to the city
  ground beneath.
- The park lawn and central plaza also have matching surface colliders. Small
  height changes use the existing character step behavior, leaving room for
  authored stairs in later city geometry.

### Support diagnostics

- Added a bounded structured `debug.log` for reproducible support reports. It
  records build/scene/seed context, generated city, bar and home summaries,
  route and visit changes, correlated transitions, minigame results,
  drinking/balance outcomes and Unity warnings or exceptions without
  per-frame telemetry.
- Press `F8` in the city or bar to capture the current player/session/world
  state immediately. `Shift+F8` opens the directory containing the active log.
- Logs rotate automatically at 5 MiB and retain three archives; release builds
  use a quieter profile while development builds include phase timings.

### District-scale city and central park

- Expanded the default city from `4 x 4` to `12 x 12` blocks, roughly
  `288 x 288 m`, with cross-city arterials and a deterministic connected road
  graph.
- Added Old Town, Residential, Industrial and Nightlife districts with
  different building proportions, heights, palettes and street details.
- Added a central `4 x 4`-block park with a walkable lawn, crossing paths,
  plaza, trees, benches, hedges and four open gates connected to surrounding
  streets.
- Moved the four bars into different urban districts and enforced at least
  `120 m` of traversable graph distance between every pair.
- Updated the full-screen map with district colors and localized labels plus
  distinct park land and paths.
- Spatially indexed walkability, changed route finding to a binary min-heap
  and batched roads, fences and lamp geometry into `48 m` chunks so the larger
  city remains practical at runtime.

### Cinematic expanded bar interior

- Expanded the bar into a denser `22 x 16 m` venue with a long counter and
  mirrored backbar, bottle shelves, three booths, four social tables, a
  curtained performance stage and dedicated activity space.
- Added entrance dressing, posters, beams, wainscot, a ceiling fan, practical
  lamps, service details and atmospheric dust so the room reads as a lived-in
  venue from every camera angle.
- Added 12 animated patrons: a working bartender, performer, seated booth
  groups, standing guests and a roaming visitor. Their silhouettes layer
  correctly around the player and furniture.
- Warm cinematic grading, bloom, vignette and film grain now combine with six
  shadowless practical lights. A skippable opening camera move establishes the
  bar before returning cleanly to normal follow control.
- Added a subtle spatial crowd bed and occasional glass/chair sounds while
  retaining the bar theme and ambience.
- Beer Pong, Split G and Tincture remain available in their respective bar
  variants, with clear paths between the entrance, counter, activity and exit.

### Five-stage intoxication and balance

- Replaced the former temporary intoxication status with one persistent
  percentage-driven system. The HUD stays hidden at `0`; positive values fill
  five 20-point segments named Light Buzz / «Лёгкий хмель», Tipsy /
  «Навеселе», Drunk / «Подшофе», Unsteady / «Шатает» and Very Drunk /
  «В стельку».
- Higher values continuously strengthen puppet sway, arm spread, bent knees,
  camera roll, movement slowdown and world-image vignette, ghost/chromatic
  doubling, warp, warmth and exposure pulse. The strongest level lowers
  movement speed to `0.70x`; all presentation eases into a changed value.
- Above `60`, periodic balance checks draw a crisp semicircular gauge over the
  hero. Hold arrows or A/D, D-pad or left stick to keep its moving arrow in
  the shrinking green center before the red risk meter fills.
- Checks become longer and more frequent as intoxication rises, with stronger
  disturbances and less player authority. Failing drops the visual puppet to
  the arrow side, holds it down briefly and raises it again while the
  physical player root remains safely stationary.
- Balance checks pause around maps, minigames, F9 and scene transitions. They
  resume only after a safety delay; reaching `60` or below cancels them.

### Classic fixed-camera door transitions

- Entering or leaving a bar now passes through a dedicated black-void scene
  instead of cutting directly between locations.
- A close fixed camera watches the handle turn and the low-poly door open,
  eases toward the threshold, then fades fully to black over `3.15 s`.
- The door swings outward toward the player, while the revealed doorway stays
  completely black instead of exposing a flat destination-colored panel.
  Warm/cold door lighting, a short latch and two hinge-creak beats reinforce
  the movement.
- The destination preloads behind the animation and cannot activate until
  both loading and the final blackout are complete.

### Restricted fog visibility

- Thickened the city's luminous gray-green distance fog and capped its camera
  range at `48 m`, so the next blocks dissolve into haze instead of remaining
  clearly readable across the map.
- Replaced the separate dark camera backdrop with the terminal fog color, so
  gaps between distant buildings no longer expose a black edge of the world.
- Made the existing local drifting fog more visible without increasing its
  36-particle budget.
- Bar interiors remain fog-free and retain their `220 m` camera range.

### Opaque diagonal head silhouettes

- Restored 51 turntable-authored head, cheek, ear, hair and neck pixels that
  the original chroma-key pass had left transparent across `FrontRight`,
  `BackRight`, `BackLeft` and `FrontLeft`.
- Regenerated the reference, jointed-parts and all five body-expression atlas
  rows. Rear diagonal expressions remain neutral; only their missing alpha
  coverage changed.

### Grounded player foot contact

- Lowered the visual foot baseline from `4 cm` to `5 mm`; the previous
  always-positive walk bob could place both soles as much as `7.5 cm` above
  the road.
- Added atlas-derived left/right foot contacts. The lower stance foot now
  remains pinned through the gait cycle while the opposite foot swings, and a
  short `12 mm` upper-body compression plus `5 mm` sole compression marks
  each footfall.
- Breathing and impact motion now affect the body and arms without lifting
  both legs during idle or walking.
- Added a small procedural contact shadow fixed to the grounded actor root.
  It stays beneath the feet independently of puppet bob, camera orbit,
  directional-light state and the existing realtime silhouette shadow.

### Heavy inertial locomotion

- The hero's maximum movement speed is now `2.6 m/s`, half of its previous
  value. Existing acceleration and braking remain intact, so movement still
  ramps and settles instead of changing speed in one frame.
- Reversing direction first bleeds the old momentum. Road boundaries and
  physical collisions discard blocked velocity, so they never release a
  stored push later.
- Modal interfaces, scene transitions, input disable and teleport retain an
  immediate safe stop.
- Walking cadence now follows actual distance travelled rather than playing
  at one fixed rate. Joint settling is softer and body rock is slightly
  stronger, keeping the gait alive through braking before it returns to idle.

### Dynamic player shadow

- The hero now casts a realtime alpha-clipped silhouette in the city, bar
  interior and home interior.
- The hidden shadow puppet faces the main directional light and chooses one of
  the existing eight authored views from the player/light angle, so orbiting
  the camera no longer rotates or flattens the shadow.
- All nine shadow-only body and limb parts now mirror the live joint angles.
  The projected silhouette visibly walks, compresses at footfall and sways
  instead of sliding as one frozen full-body card.
- Street and bar practical lights remain shadowless to preserve the existing
  realtime-light budget.

### Tinctures in a Row minigame

- Added a fourth stable city bar with a `7x7` match-three board, five
  symbol-coded infusion flavors, exactly one starting `XXX` moonshine shot and
  15 accepted moves.
- Invalid swaps return without spending a move. Accepted swaps resolve unique
  matches, gravity, seeded refills and deterministic cascades with a multiplier
  capped at `x5`; boards with no normal move reshuffle automatically.
- Runs of four or more and intersecting matches can create `XXX`, but the board
  never contains more than one. Swapping it with a flavor clears every shot of
  that flavor.
- Normal matches are customer orders and do not increase intoxication. Only
  activating `XXX` immediately saves one `Moonshine`, one consumed drink and
  +24 intoxication; cancelling cannot refund it, while F9 runs remain isolated.
- Added mouse click/drag, keyboard and gamepad controls, RU/EN UI, an
  activity-specific tray/shot/`XXX` interior display, a point-filtered
  `640x360` backdrop, transparent 4x4 sprite atlas and generated swap, match
  and moonshine-burst sounds. Swaps, gravity and refills animate between
  immutable board snapshots with synchronized cascade effects.
- Closing during the terminal cascade still completes the visit. Reaching
  100 intoxication finishes after the cascade and leaves the player at the
  permanent highest percentage-driven stage.

### Split the G minigame

- The third stable city bar hosts Split the G; together with Tinctures in a
  Row, the default four bars now have one distinct activity each.
- Hold Space, LMB or gamepad South for one irreversible virtual sip. The exact
  liquid boundary disappears behind the tilted pint, hand and foam until the
  `1.4 s` settling phase reveals the result.
- Remaining level is derived from total unscaled hold time, so the same sip
  scores identically at different frame rates. Perfect/Excellent/Good/Close/
  Miss use 1/3/6/10-percent error bands.
- A session allows up to three fresh dark-beer glasses and keeps its best
  result. Continue can finish early; the third result finishes automatically.
- Every non-empty sip immediately saves its actual consumed fraction as dark
  beer progress, while F9 debug launches remain fully isolated.
- Added a dedicated `640x360` pixel-art bar backdrop, transparent 4x4
  pint/hand/foam/effect atlas, localized RU/EN interface and generated gulp
  sound.

### Cinematic player presentation

- Moved the centered chase camera much closer with separate `2.6 m / 53°`
  exterior and `2.2 m / 57°` interior profiles while retaining a complete
  full-body composition.
- Increased orbit, focus, obstacle-recovery and cinematic blend inertia for a
  heavier, smoother response. Focus lag remains bounded, teleport snapping
  and immediate inward obstacle avoidance are preserved, and the arm now
  eases back out instead of popping.
- Camera motion now fades out and restores with the shared modal lock used by
  the map, minigames and F9 launcher.
- Strengthened the procedural living idle with readable breathing, weight
  transfer and a short gesture that alternates between the left and right
  arms; all motion still blends with walking and yields progressively to
  strong intoxication, balance and fall poses.
- Expanded facial animation to five deterministic states: stronger
  half/closed blinks plus watchful and tense idle expressions in all five
  visible-face directions. Rear views remain neutral, locomotion cancels the
  idle-only expressions, the visible puppet still uses exactly nine renderers
  and no sprite is mirrored.

### Eight-direction player prototype

- Added eight unique front/side/back views without replacing the character's
  modular animation principle: the current rig uses one body layer and
  separate upper/lower segments for both arms and legs.
- Camera orbit no longer turns the hero. Movement stays camera-relative, while
  the hero keeps the last actual movement heading when stopping.
- Added 5-degree directional hysteresis, a shared foot pivot and explicit
  non-mirrored views to prevent boundary flicker, size jumps and asymmetric
  detail errors.
- Restored 259 face pixels accidentally removed by chroma-key processing; all
  visible facial pixels are opaque while the existing silhouette, clothing and
  palette remain unchanged.
- Walking now rotates shoulders, elbows, hips and knees in every direction,
  alongside lightweight bob/rock and the existing whole-puppet intoxication
  sway. Full multi-frame idle/walk animation remains a future art pass.
- Corrected front/back walking so limbs swing in depth instead of fanning
  sideways. Left/right limbs now alternate explicitly, arms oppose the
  same-side legs, diagonals blend screen/depth motion and far limbs pass behind
  the torso.

### Visible road-edge fences

- Added low ochre two-rail barriers along every exposed road edge and across
  dead ends, making the road-only movement boundary visible in the city.
- Intersections and connected road mouths stay open because fences follow the
  exact perimeter of the combined road surface rather than individual edges.
- Every generated bar automatically receives a `3.30 m` fence opening around
  its entrance walkway; future bar lots use the same data-driven rule.
- The barriers are visual-only, so the existing road/apron movement mask
  remains authoritative and the chase camera is unaffected by the new posts.
  All rails and posts are combined into two render meshes.

### F9 minigame debug window

- Press `F9` in the city or bar interior to open a direct launcher for every
  registered minigame; cocktail mixing, beer pong, Split the G and Tinctures
  in a Row are available now.
- Normal interiors and the debug list use the same explicit catalog, so a
  future game appears after its definition and factory are registered.
- Opening the window closes a conflicting map or minigame and preserves the
  modal input/HUD state when the window or launched game closes.
- The Left/Right arrow keys or clickable `-20/+20` controls change the real
  session intoxication in clamped 20-point steps for rapid stage and balance
  testing without changing its last-drink or consumed-drink context.
- Debug minigame runs remain isolated: they do not mark a bar visited or save
  their own intoxication and consumed-drink changes.

### Beer-pong minigame

- The second bar on the stable city map opens beer pong; the first keeps the
  cocktail mixer, the third hosts Split the G and the fourth hosts Tinctures
  in a Row.
- Aim with mouse, keyboard or gamepad, charge a throw and watch the ball use
  deterministic 2.5D physics with real table and cup-rim bounces.
- Clear six cups in ten throws. Clean sinks score 100, bank shots add 50, and
  unused throws add an early-clear bonus.
- Every miss consumes a light beer, adds 8 intoxication and immediately saves
  that drinking state. The activity ends on a clear, the throw limit or
  maximum intoxication.
- Added a point-filtered 640x360 pixel-art bar/table background, a 4x4
  ball/hand/cup/effect atlas, compact aiming feedback and distinct retro throw,
  bounce, rim and sink sounds.
- Completing the activity marks that bar visited and removes it from the
  itinerary; cancelling leaves both the visit and route untouched.

### PS1-inspired presentation and audio

- Added a PC renderer feature that composites the post-processed world at
  `640x360` by default, applies four-tap footprint averaging and RGB555
  quantization as a 35% perceptual-space blend without a visible screen-space
  dither grid, then point-upscales at exact 2x/3x scale on 720p/1080p outputs;
  lower `426x240` and `320x180` modes remain available.
- Restyled prompts, intoxication HUD, city map and cocktail interface with a
  compact burgundy/amber PS1-era UI theme. General overlays use a logical
  `640x360` canvas, while the cocktail screen remains responsive.
- Replaced smooth cylinder visuals with one shared flat-shaded 8-sided mesh,
  switched the main directional light to hard shadows and disabled camera
  MSAA for sharper low-poly silhouettes.
- Added deterministic `22050 Hz` retro UI, movement, door and cocktail SFX with
  pooled playback, cooldowns and voice limits.
- Added separate procedural ambience for the city, bar and home. The Home loop
  adds refrigerator, mains, pipe and drip layers while preserving the correct
  `city_theme`/`bar_theme` split and mild low-pass treatment on both music
  players.
- Kept runtime IMGUI intentionally crisp after the pixelated world composite.
  The current renderer integration targets PC; mobile parity is deferred.
- Fixed city-map road/route/player-heading lines being displaced by nested
  GUI transforms; the player now uses a clear chevron heading indicator.

### Cocktail mixing minigame

- Replaced the five-pick drink selection with a hands-on three-cocktail game
  at the edge of the bar counter.
- Choose beer, wine, vodka or cognac, then mix in 2–4 unique ingredients from
  a seven-item shelf containing four good matches and three traps.
- Compatible recipes score up to 100 points per cocktail and 300 total; every
  bad addition costs 15 points.
- New pixel-art bottles, fruit, ice and glass animations show each pour,
  rising liquid, good sparks, bad bubbles and the final shake.
- The glass fill now follows the actual inner cavity with a tapered pixel
  surface instead of appearing as a glowing rectangular progress bar.
- Added three-stage progress, a final rank and complete mouse, keyboard and
  gamepad controls.
- Accepting the final minigame result now marks that bar as visited; entering
  the interior or leaving an unfinished game does not.
- Intoxication and served cocktails persist after every stage. A bad served
  mixture keeps its score and intoxication penalties but creates no separate
  timed status; reaching 100 ends the session with the explicit
  maximum-intoxication result.

### Fog-forward city atmosphere

- Raised the night scene's baseline visibility and replaced the dark blue haze
  with a denser luminous gray-green fog.
- Added a slow local fog layer that follows the player without changing
  navigation or collision.
- Street lamps now cast directed pools of light, while lamps, bar entrances and
  flashing amber signals bloom into soft depth-aware halos.
- Retuned City-only bloom, grading, vignette and film grain; the warm bar
  interior remains clear of exterior atmosphere effects.

### Scene music slots

- Added separate resource folders for the looping `city_theme` and `bar_theme`.
- Each theme now plays only in its matching scene and stops automatically on a
  Single-mode transition.

### Noir city night

- Converted the generated city to a fixed nocturnal presentation with
  atmospheric fog, cold moonlight and City-only color grading.
- Added glowing street lamps with warm pools of light and a strict realtime
  light budget.
- Added slow seed-phased blinking amber signals to major intersections.
- Ordinary windows now form a deterministic mix of dark, cool and rare warm
  panes, while bars remain constant bright landmarks.
- Bar interiors remain warm and fog-free.

### City map and bar itinerary

- Added a full-screen map showing the road network, player and every bar.
- Bars can be added, removed and reordered into a numbered visit itinerary.
- Each leg follows a deterministic shortest route over the generated roads.
- The itinerary survives bar scene transitions and removes a stop only after
  that bar's assigned minigame is completed.
- Completed bars persist as green numbered map markers with a visited counter;
  amber corner badges keep route order readable independently.
- Added mouse, keyboard and gamepad controls plus RU/EN map text.

### Project foundation

- Unity 6 URP project created from the stock template.
- Versioned project guidance and AI memory initialized.

### Playable MVP

- The initial vertical slice used a deterministic connected city with roads,
  16 building lots and 3 bars; the current default has since expanded to 4.
- Road-constrained eight-direction sprite character and free-orbit perspective
  third-person chase camera with obstacle avoidance.
- Localized interaction prompts and a separate generated bar interior.
- Guarded scene transitions and return to the same bar/city layout.
- EditMode, PlayMode and Windows Player verification.

### Visible bar landmarks

- Bar buildings now use warm window bands and gold-framed entrance canopies.
- Added shared procedural pixel mug signs that remain readable from changing
  third-person camera angles.
- Decorative facade pieces are collider-free and do not change city layout,
  navigation or entrance interaction.
