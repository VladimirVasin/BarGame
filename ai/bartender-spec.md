# The Ordinary Bartender — active model, rig and service choreography

Status: **Current**. The active bar worker is the ordinary two-armed
`bar_bartender_v2`. The former six-armed design remains packaged as an
inactive legacy asset and is never selected by the bar world builder.

## 1. Active character contract

The bartender is an ordinary, silent publican. The four-offer drink menu,
prices, descriptions, paid-order state, deferred consumption effects, closing
state and `BarDrinkServiceTimeline` remain the source of gameplay truth.

- Silhouette: a believable full-body adult at `1.75 m`, with two arms and no
  signature-anatomy overlay.
- Work clothes: dark-green waistcoat, rolled shirt sleeves, dark apron and a
  service towel. The model also carries the restrained flat cap and moustache
  already declared by its measured parts.
- Service roles: bottle drinks keep the right-hand bottle / left-hand vessel
  roles. For beer he walks to the central tap, takes the pint, steadies it with
  the left hand and pulls the middle handle with the right before carrying and
  placing the glass. Outside those windows he returns to the quiet wiping loop.
- World meaning: he is not an oddity, never comments on the hero and gains no
  replacement supernatural trait.

## 2. Model and asset pipeline

- **Tool:** `tools/build-ordinary-bartender-3d-model.py`.
- **Design:** `bar_bartender_v2`, generator `3.0.0`, anatomy standard
  `NpcHumanV2`, A-pose, full height `1.75 m`, rest pelvis `0.835 m`.
- **Rig:** the exact Hero V2 Generic Avatar and 31-bone hierarchy from
  `Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx`.
- **Measured geometry:** `39` meshes / `1,136` triangles, inside the declared
  `900-2,600` triangle budget. The manifest declares zero extra arm pairs,
  colliders, lights and rigidbodies.
- **Outputs:**
  `Assets/Bar/Bartender/Models/BarBartenderOrdinary3D.{fbx,json}` and the
  Blender source/preview under `ArtSource/Bar/Bartender/`.
- **Editor setup:** `BarBartenderV2AssetSetup` builds
  `Assets/Bar/Bartender/Prefabs/BarBartenderOrdinary.prefab`, binds the shared
  `Player3DLit` material and writes its measured data into
  `BarBartenderAssetRegistry`.
- **Provider:** `Assets/Resources/Bar/BarBartenderProvider.asset` points
  `BartenderPrefab` at the ordinary prefab and retains the old prefab only in
  `LegacyBartenderPrefab`. `BarBartenderWorldBuilder` always instantiates the
  active reference.

The model FBX itself remains animation-free. Its registry references the
shared waiter clips in
`Assets/Pedestrians/Animations/MountainRoadCafeCast.fbx` instead of duplicating
them.

## 3. Hands, sockets and authored anchors

The ordinary registry exposes exactly two service hands:

- left hand: `SOCKET_Grip.L`, `SOCKET_Vessel.L` and
  `ANCHOR_BartenderVesselGrip`;
- right hand: `SOCKET_Grip.R`, `SOCKET_Bottle.R` and
  `ANCHOR_BartenderBottleGrip`.

The role sockets remain on the canonical hand bones. The two authored anchors
give the procedural reach layer stable grip transforms without adding bones or
extra limbs to the shared retarget contract.

The active ordinary bartender stands with his root and feet on the authored
bar floor. The `0.42 m` service duckboard and matching root lift belonged to
the superseded six-armed presentation; the active interior contains no such
platform and `BarBartenderWorldBuilder` applies no offset to the actor. Counter
contact is solved in the working pose against the real top at `Y = 1.02 m`,
not by raising the complete body.
The separate menu handoff keeps its existing left-grip motion, but its dock is
collinear with the selected stool: the booklet lands directly before the hero,
matching the Mountain Road cafe placement.

## 4. Reused waiter animation

The registry binds the same four Café attendant clips used by the Mountain
Plateau Café:

| Bar role | Shared clip | Import contract |
| --- | --- | --- |
| quiet default | `CafeAttendantWipe` | `9 s`, looping |
| pickup, placement and return travel | `CafeAttendantWalk` | `1.25 s`, looping |
| pour | `CafeAttendantPour` | `3.5 s`, one-shot |
| acknowledge the service start | `CafeAttendantNotice` | `2.5 s`, one-shot |

`BarBartenderPresentation` evaluates those clips through a manually driven
`PlayableGraph`. It reads the current `BarDrinkServiceTimeline` frame: Notice
during `CameraApproach`; Walk during bottle travel,
`BeerWalkToTap`, `BeerGlassPickup`, `BeerCarryToGuest` and
`BeerGlassPlacement`; Pour during `Pouring` and `BeerPouring`; and Wipe in the
remaining phases. The
service towel is hidden while the left hand is working with the vessel and is
restored on return to idle. In that idle phase the shared
`CafeAttendantWipe` is visibly sampled; the grounded actor placement puts its
towel against the `Y = 1.02 m` bar top and the clip moves it along the surface
instead of hovering above it.

`BarBartenderServiceChoreography` is still a reader, never a transaction
driver. The timeline and position reports own progress. For beer the actor
must physically reach `BeerTapServerDock` before pickup/pour can advance, then
reach the selected guest dock before placement. The world presentation moves
the pint between `BeerTapVesselDock`, the left grip and the service point,
pulls `BeerTapHandlePivot` through the right grip, and emits the stream from
`BeerTapSpout`. A bounded reach overlay brings each hand to its current target,
then blends both hands back out when service ends or the shop closes.

## 5. Verification contract

- `BarBartenderAssetTests` verifies that the provider selects the distinct
  ordinary prefab while retaining legacy, that the active prefab uses the Hero
  V2 Avatar, has no extra-arm chains, and exposes four waiter clips plus all
  required sockets and anchors.
- The same fixture drives a real `BarDrinkServiceTimeline` through Notice,
  Walk, Pour and Wipe and checks the two-hand reach contract, including beer
  walk/pour/carry phases.
- `BarDrinkServiceTimelineTests.BeerService_WaitsForPhysicalArrivalAndExplicitDrink`
  checks both position gates, the indefinite `AwaitingDrink` hold and the
  explicit `2/3/2 s` pickup/sip/return branch.
- `BarDrinkPhysicalShopPlayModeTests.BeerTapService_WaitsForGazeThenHeroReturnsEmptyPint`
  checks the central handle and stream, delayed gameplay effects, gaze-bound
  prompt/outline, nested Hero V2 action, hand-to-pint contact and persistent
  empty vessel.
- `GameSessionStateTests.PaidDrinkOrder_DefersEffectsAndConsumesExactlyOnce`
  checks that cash commits at order confirmation while intoxication,
  last-drink, count and stress commit exactly once on consumption.
- `BarInteriorSpawnPlayModeTests` requires the active root to remain grounded
  and samples changing points of Wipe to prove that the towel both contacts and
  travels across the real counter top.
- `SceneFlowSmokeTests` requires the spawned bar worker to use the ordinary
  rig at the authored bartender anchor.
- The Blender validator owns the measured `39`-mesh / `1,136`-triangle,
  `1.75 m`, two-arm manifest contract.

## Appendix A. Inactive six-armed legacy design

The original `six_armed_bartender_v1` is retained for history and asset
compatibility. Its generator, measured output and prefab remain at
`tools/build-bartender-3d-model.py`,
`Assets/Bar/Bartender/Models/BarBartender3D.{fbx,json}` and
`Assets/Bar/Bartender/Prefabs/BarBartender.prefab`.

That legacy asset is a `1.75 m` NpcHumanV2 figure with `50` meshes / `1,436`
triangles and four procedural pivot chains (`Arm2.L/R`, `Arm3.L/R`) layered on
top of the canonical arm pair. `BarBartenderAssetRegistry` and the legacy
branch of `BarBartenderPresentation` still preserve those bindings so the
asset remains inspectable. `BarBartenderProvider.LegacyBartenderPrefab`
retains the reference, but `BarBartenderWorldBuilder` never chooses it.

Its former multi-ingredient cocktail proposal — mix affordance, mixture state,
one bottle per extra hand and a simultaneous bottle-return chord — was never
implemented and is now superseded together with the active six-arm concept.
It is not current behaviour or an active delivery plan; the ordinary bartender
continues to serve the existing single selected drink through the unchanged
timeline.
