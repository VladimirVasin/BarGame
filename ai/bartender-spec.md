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
geometry supplies the removable service towel. The role retains no legacy model.

## 3. Hands, sockets and authored anchors

Exactly two hands serve the real menu, bottle, tap and vessel. The actor remains
on the floor; the counter top is `1.02 m`. The service adapter binds the live
worker rig without repainting population-selected clothes. The towel follows
its shared hand socket and contacts the real top while wiping. Before any other
action, the same left arm lifts it behind the back, then the towel hides there.
Service and walking wait for that contact. Before wiping, the arm reveals it at
the same rear station and draws that same towel
and returns it to the measured top; no prop clone or fade is used.

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

Hero pickup, sip and return use the continuous production world rig and the
existing nested interaction. `BarPlayerDrinkReach` applies the final torso and
right-arm layer: up to 40 degrees shared by hip, spine and chest near the
counter, preserving seated pelvis and foot contacts and easing upright during
the lift. The vessel stays supported until the physical grasp centre is within
`3 mm` and the palm faces it, then follows the
solved hand. Return keeps hand and vessel together until the base reaches its
exact supported pose within `3 mm`; only then does the hand withdraw. The
action clock holds its pickup/return endpoint until these contacts complete.
The existing seated camera refreshes from the moved head. Effects commit once
after consumption; no separate hero, prop teleport or visibility transition
bridges the reach.

After drinking, the empty vessel stays on the counter while the menu reopens.
Confirming the next order first sends the bartender to collect it: walk to the
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

## 5. Verification contract

`BarBartenderAssetTests` covers the catalog worker, shared clips and two-hand
adapter. `BarInteriorSpawnPlayModeTests` covers grounded arrival, readable menu,
wipe contact/travel and physical beer/bottle service. Grasp checks must measure
palm/finger geometry independently of sockets; hero pickup/return checks sample
the rendered final world-rig pose and retain counter support until contact.
Repeated-order coverage retains the old empty vessel through collection and
checks backward facing, vertical lowering, open-palm support and occlusion
before replacement. `AreaCaptureFixture.Bar` and `BarMenu`
show the placed room and actual default actor. Removed asset paths, GUIDs and
regeneration hooks must have no remaining live references.
