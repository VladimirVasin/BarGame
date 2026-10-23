# The Ordinary Bartender — default rig and service choreography

Status: **Current**. Accepted user decision `2026-10-21`: the bartender uses
`DefaultNpcCatalog.OrdinaryWorker` through `DefaultNpcFactory.CreateForCharacter`.
Both former bespoke models, including the six-armed version, their providers,
source assets and regeneration/import paths are removed.

## 1. Active character contract

The bartender remains an ordinary silent worker. The four-offer drink menu,
prices, payment, deferred consumption effects and closing state remain owned
by the shared drink controllers. No replacement strange trait or speech is added.
His permanent identity is `zzz.bar.bartender`: registration sorts after the
existing roster so the migration does not redistribute other characters.
The global population owns face, hair and work clothes; the local bar owns
placement, actions and contacts. No separate bartender body or wardrobe exists.

## 2. Model and asset pipeline

`BarBartenderWorldBuilder` creates the catalog worker at the layout anchor.
`BarBartenderAssetRegistry` adapts its live bones, Animator, grip sockets and
clips to the bar role. It releases the default presentation's animation graph
before the service graph takes ownership. Shared cafe service clips come from
`MountainRoadCafeCast`; locomotion comes from the default actor. Shared hand-prop
geometry supplies the removable service towel.
`build-player-bar-repeat-actions.py` exports the bone-only
`Player/PlayerBarRepeatActions` bank. Its entry/exit match the seated parent
pose and retain the lower body. The live wave fits once to the initial gaze,
reaching forward into first-person view toward the bartender before support;
the tap loop keeps the right wrist steady. The camera does not reframe it.
The bank reads the canonical source without changing it. A focused
`--hand-grip-only` refresh authors `hand_grip.gestures` on the original right
hand meshes: `PointingFist` and finite `IndexTap05` through `IndexTap60` samples.
Only adjacent index samples interpolate; fist and index targets affect
different digits. Basis, rig, materials, weights and existing actions/grips stay.

## 3. Hands, sockets and authored anchors

Exactly two hands serve the real menu, bottle, tap and vessel. The actor remains
on the floor; the counter top is `1.02 m`. The service adapter binds the live
worker rig without repainting population-selected clothes. The towel follows
its shared hand socket and contacts the real top while wiping. Before any other
action, the same left arm lifts it behind the back, then the towel hides there.
Service and walking wait for that contact. Before wiping, the arm reveals it at
the same rear station and draws that same towel
and returns it to the measured top; no prop clone or fade is used.
The actual open or resting booklet is an obstacle with `15 mm` clearance. A cached
whole-stroke envelope selects one stable free patch inside the measured wood
and within the left arm's reach. If no patch fits, the same towel physically
stows behind the back and waits. Idle work also stows, walks with the complete
stride to another reachable patch, turns and draws the same towel before
resuming contact. Service preempts this bounded work cycle.
Menu delivery uses the measured left palm grip, with the flat book ahead of
the torso; post-IK carrier refresh retains its contact through the handoff.

The bar-only free right arm braces and repositions its open palm during Wipe,
and lifts/releases that support during Notice. Free service motion retains the
layer, while bottle/tap/vessel IK, explicit counter support, full Walk and towel
handoff own their occupied chains. The layer releases on disable and cleanup;
shared cafe clips and other actors remain unchanged.

`NpcHandPose` combines authored grasp centres with measured palm, thumb and
finger bind poses for the bartender, visitors and hero. The mesh-derived palm
normal orients the hand; a matching socket alone does not establish a
correct grasp. Fingers close around that measured centre with the palm facing
the vessel. The mug uses its handle; wine and cognac use their bowls,
and the shot glass uses its body. `CylindricalGrip` retains the `22 mm` grasp;
`WineBowlGrip` and `SnifterBowlGrip` open around `37/46 mm` radii, with their
centres `radius + 8 mm` in front of the palm. They retain the original wrist
seam, embedded knuckles and phalange lengths; variants never add together.
The selected shared hand shape follows body sampling;
the contact solve follows the final animation write. Props remain visible
until physical pickup; the closed booklet and returned empty vessel persist.
During pouring the bottle lies to the left of its hand contact in the player's
view. The right hand grasps the neck from above, palm down and fingers wrapped.
The service plan owns the leftward bottle pose; its tilted grip blends toward
projected world up. Existing arm/elbow animation, reach solving and carrying
poses remain. Source anatomy, models, materials and effects are unchanged.

## 4. Reused service and locomotion animation

One service graph samples the shared Wipe, Notice, Pour and handling clips;
full Walk is sampled only while the root translates. The choreography reads
`BarDrinkServiceTimeline` and physical arrival reports rather than committing
purchases itself. Beer uses the central of three taps. Wine, cognac and vodka
use the selected shelf bottle, server-side preparation dock and matching vessel;
the bottle returns to its exact source pose. Preparation uses the opposite side
from the resting menu. Wine/snifter/shot heights are 18.5/12.8/5.8 cm; beer stays
15.515 cm. Measured shell bounds drive supported poses and raised carry paths:
lift, transfer above the counter, lower. Shared two-bone IK closes the measured
hand contacts; the bartender torso has an absolute 38-degree tilt cap. Menu
and drink docks align with the selected stool. Hands blend back out on
completion or cleanup.

`BarBartenderServiceQueue` gives hero/two counter guests deduplicated FIFO slots;
one service runs. `BarCounterPatronRefill` drains cups during sips at distinct
seeded rates. Empty guests wave left and wait without sipping/tapping.
The right arm stays bent: slides the empty mug toward the server for pickup,
then returns to support. After tap refill it grips and pulls the same full mug
back to its original dock before FIFO release. Other cups wait for their bottle.
Attention uses final left-arm IK; fingers ease open after handoff. Other seven
loops stay. Pause freezes service; teardown releases ownership.
Each accepted requester gets a look/one nod, including queued requests, without
interrupting service or adding speech.

Hero pickup, sip and return use the continuous production world rig and the
existing nested interaction. `BarPlayerDrinkReach` applies the final torso and
right-arm layer: up to 40 degrees shared by hip, spine and chest near the
counter, preserving seated pelvis and foot contacts and easing upright during
the lift. The vessel stays supported until the physical grasp centre is within
`3 mm` and the palm faces it, then follows the
solved hand. Return keeps hand and vessel together until the base reaches its
exact supported pose within `3 mm`; only then does the hand withdraw. The
action clock holds its pickup/return endpoint until these contacts complete.
The eye camera follows the animated head. Pickup, sip and return permit
`±12°` manual yaw and `±10°` pitch. Menu confirmation and repeat requests use
ordinary seated look: yaw `±70°`, pitch `−25°…55°` in the seated reference.
Completion retains the chosen gaze; tracking and lens remain unchanged.
Effects commit once
after consumption; no separate hero, prop teleport or visibility transition
bridges the reach.

Menu confirmation closes the booklet; an empty vessel's gaze outline repeats
the last drink. Both use the same seated hero's forward left wave, then two
soft right-index contacts.
The left palm then supports him. The right palm stays horizontal above the wood;
the thumb and three other fingers form a fist. Only the straight index flexes
at its knuckle, with a fixed wrist through both contacts and their intervening
lift. Actual palm/finger surfaces stay outside the counter; other surfaces
retain at least `3 mm` clearance at index contact. After both taps, an empty
beer mug gets a typed right-hand grip, supported slide toward the server,
release and hand return before acceptance. Travel is up to `12 cm`, fitted to
arm reach with the whole mug `1 cm` from menu/counter edges. Other vessels and
no-cup orders have no offer. Interrupted gestures do not buy. Completion revalidates
and charges the selected or last served drink once, then joins the FIFO.
Payment/receipt occur on acceptance, not dispatch. The bartender carries the
same mug to the tap and stays planted while retracting the full mug into
the walking grasp; only the completed retraction releases guest travel.
Wine/cognac/vodka keep the same vessel
on its guest dock while the matching bottle comes, pours directly and returns
to its shelf. Another explicit sip commits the new order's effects once.
Accepted user exception `2026-10-22` replaces disposal only for this repeat.

Choosing a different drink through the menu first sends the bartender to collect it: walk to the
guest dock, close the hand at physical contact and lift the same vessel. Its
pickup facing is retained through a straight `0.25 m` backward step. Low forward
carry keeps the elbow below the shoulder with an open bend. Lowering then
holds vessel X/Z fixed and moves vertically. The free right hand opens
and braces a fixed counter point with `2 mm` surface clearance, then blends
out through the existing reach layer. The vessel stays in the left grasp.
Only after its full measured bounds are below the counter and all eight
guest-camera corner rays are occluded does it deactivate; the next service
then begins. Existing rig, materials, IK, camera and economy stay. Pause,
disable, destruction and scene unload release all owners.
An accepted first or repeat transaction shows its actual deducted amount and
remaining balance in a transient left HUD receipt and plays the shared pooled
payment cue. Pending/failed/interrupted gestures and repeated input create no
receipt or charge. The receipt remains visible during the bar modal, expires
on its unscaled clock, freezes with pause and clears with scene teardown.

## 5. Verification contract

`BarBartenderAssetTests` covers the catalog worker, shared clips and two-hand
adapter. `BarInteriorSpawnPlayModeTests` covers grounded arrival, readable menu,
wipe contact/travel, both free arms, fan placement/speed, resting-menu clearance
or physical towel stow, patch-to-patch Walk, and physical beer/bottle service.
Initial-menu and same-drink coverage measure the forward-visible left wave before either tap, horizontal palm,
clenched fingers, index-only motion, actual surfaces outside the wood,
actual index-pad contacts, deferred payment,
receipt/sound, object identity, tap refill versus stationary guest-dock refill
and a second explicit sip. Real arrow input checks limited camera rotation in
drink phases, the repeat gesture and restored seated control. Grasp checks must measure
palm/finger geometry independently of sockets; hero pickup/return checks sample
the rendered final world-rig pose and retain counter support until contact.
Different-menu-order coverage retains the old empty vessel through collection and
checks backward facing, vertical lowering, open-palm support and occlusion
before replacement. `AreaCaptureFixture.Bar` and `BarMenu`
show the placed room and actual default actor. Removed asset paths, GUIDs and
regeneration hooks must have no remaining live references. The existing
`Bartender_WipeTouchesAndTravelsAcrossCounter` covers towel work.
`BeerService_BartenderWalksAndPlacesMugWithoutThrowingIt` also checks counter
depletion/waves, FIFO/dedupe/cancel, same-cup refill and queued hero confirmation/payment.
`RedWineService_FetchesPoursCarriesAndWaitsForExplicitDrink` covers direct refill,
the bottle base left of the contact during initial/repeat pours, visible palm
above the neck, finger wrap, solid clearance and preserved elbow geometry.
`VodkaService_KeepsShotClearOfCounterAndMenu`
retains its shot/counter/menu clearance checks.
Manual skin captures force fresh render matrices and restore that flag afterward.
Receipt frames require a visible Game view without batch mode and capture it
after IMGUI; Camera.Render cannot verify this overlay.
`player_hand_gestures.validate` and its FBX round trip check finite hand
geometry, original link lengths and contact clearance;
`build-player-bar-repeat-actions.validate` checks the wave, fixed wrist and
shared parent/lower-body seams.
