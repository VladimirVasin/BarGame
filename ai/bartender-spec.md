# The Ordinary Bartender — default rig and service choreography

Status: **Current**. Accepted user decision `2026-10-21`: the bartender uses
`DefaultNpcCatalog.OrdinaryWorker` through `DefaultNpcFactory.CreateForCharacter`.

## 1. Active character contract

Ordinary actor; shared controllers own menu/payment/deferred effects/closing.
`2026-10-23`, story §6: [speech contract](bar-dialogue-plan.md) implemented.
One shuffled order comment; own channel over guest talk, shared speech stack.
Hero silent; comments concern the order, never his person/past.
Service/grips precede speech gestures; no strange trait.
`zzz.bar.bartender` sorts after the roster, preserving others' assignments.
Population owns face/hair/work clothes; bar owns placement/actions/contacts.
No bespoke body/wardrobe.

## 2. Model and asset pipeline

`BarBartenderWorldBuilder` creates the catalog worker at the layout anchor.
`BarBartenderAssetRegistry` adapts live bones, Animator, sockets and clips,
releasing the default graph before service takes ownership. Shared cafe clips come from
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
full Walk is sampled only while the root translates. `BarDrinkServiceTimeline`
and physical arrival drive choreography; it cannot commit purchases. Beer uses
the central tap of three; wine/cognac/vodka use the selected shelf bottle,
preparation dock and matching vessel, then restore its exact source pose. Preparation
uses the opposite side from the resting menu. Wine/snifter/shot heights are 18.5/12.8/5.8 cm; beer stays
15.515 cm. Measured shell bounds drive supported poses and raised carry paths:
lift, transfer above the counter, lower. Shared two-bone IK closes the measured
hand contacts; the bartender torso has an absolute 38-degree tilt cap. Menu
and drink docks align with the selected stool. Hands blend back out on
completion or cleanup.

`BarBartenderServiceQueue` gives hero/present counter guests deduplicated FIFO slots;
one service runs. `BarCounterPatronRefill` drains cups during sips at distinct
seeded rates. Empty guests wave left and wait without sipping/tapping.
The right arm stays bent: slides the empty mug toward the server for pickup,
then returns to support. After tap refill it grips and pulls the same full mug
back to its original dock before FIFO release. Other cups wait for their bottle.
Attention uses final left-arm IK; fingers ease open after handoff. Other present
guests keep their loops. Pause freezes service; teardown releases ownership.
Requests keep one look/nod. Shared speech follows accepted/delivered events
independently of physical service.

Daily service runs `12:00–02:00` (`2026-10-25`). `IsAcceptingOrders` gates new,
repeat and NPC refill requests, rechecking after the gesture before payment.
`IsOpen` retains the seated session: accepted orders finish, served drinks remain
drinkable, cleanup and exit stay available. Departure waits for the pair exchange,
accepted service and the current wave's return. Incremental refill binding uses
stable counter-place IDs without resetting existing guests.

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

Guests release the supported vessel and exit empty-handed.
Dirty places stay reserved. One bounded collection queue covers counter/tables/
booths, even after closing. The worker stows the towel, walks around the left
counter end, grips at measured contact and carries the same vessel home.
Occlusion ends collection. Safe handoffs prioritize accepted service;
its clock waits for return. Shared aisle leases yield to returning WC guests.
Pause/disable/exit preserve prop/fill ownership; no player transaction.

## 5. Verification contract

`BarBartenderAssetTests`: catalog worker, shared clips, two-hand adapter.
`BarInteriorSpawnPlayModeTests`: grounded arrival/menu, wipe contact/travel,
free arms, fan, paper clearance/towel stow and beer/bottle service. Initial/repeat
orders measure the forward left wave before two index contacts, horizontal palm,
clenched other fingers, solid clearance, deferred payment/receipt, same vessel,
tap versus bottle refill and explicit second sip. Arrow input verifies limited
drink look and restored seated control. Measure actual palm/finger surfaces,
not sockets alone; final rendered rig retains support until contact.
Different-order collection checks backward facing, vertical lowering,
open-palm support and complete occlusion before replacement.
`AreaCaptureFixture.Bar`/`BarMenu`: placed room/worker. Removed asset paths,
GUIDs and regeneration hooks must have no live references.
`Bartender_WipeTouchesAndTravelsAcrossCounter`: towel work.
`BeerService_BartenderWalksAndPlacesMugWithoutThrowingIt`: depletion/waves,
FIFO/dedupe/cancel, same-cup refill and queued hero payment.
`RedWineService_FetchesPoursCarriesAndWaitsForExplicitDrink`: direct refill,
bottle base left of contact, palm above neck, wrap/clearance/elbow anatomy.
`VodkaService_KeepsShotClearOfCounterAndMenu`: shot/wood/paper clearance.
`BarPatronCleanupPlayModeTests`: retained surface vessel, empty-hand exit,
dirty reservation, room collection/contact/carry/stow and service ownership.
Manual skin captures force fresh render matrices, then restore the flag.
Receipt proof uses visible Game view after IMGUI; Camera.Render omits it.
`player_hand_gestures.validate`/FBX round trip: finite geometry, link lengths,
contacts. `build-player-bar-repeat-actions.validate`: wave, fixed wrist and
shared parent/lower-body seams.
