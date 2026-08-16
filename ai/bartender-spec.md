# The Six-Armed Bartender — model, rig and service choreography spec

Status: **designed, not yet built**. This is the "dedicated 3D bartender
pass" that `BarPatronWorldBuilder` has been reserving the Bartender
anchor for.

## 1. Concept

A heavy, patient figure behind the counter with **three pairs of arms**
— the bar's one openly impossible thing, in the same register as the
Watcher Cashier and the Silent Hill attention idiom. He is not a
monster; he is staff. The wrongness is presented as pure competence:
six hands mean the pour never waits.

- Silhouette: cashier-class torso (broader than a pedestrian), short
  neck, heavy head. Three arm pairs stacked on the torso — shoulder
  line, mid-ribs line, floating-rib line — each pair slightly shorter
  and lower than the one above, so the outline reads as a fan.
- The hero never sees him walk. He exists from the waist up behind the
  counter; no leg rig beyond the canonical stubs needed by the shared
  skeleton contract.
- Palette: waistcoat black-green over rolled shirt (bar's amber warms
  it), skin in the pale pedestrian range, one accent — a dull brass
  arm-band on the middle-right arm (the pouring arm).
- Idle is the sell: each pair does quiet unsynchronized business —
  lower pair braced on the counter, middle-left polishing a glass in
  small circles, upper-right drumming fingers. Never symmetric, never
  still, never fast.

## 2. Model pipeline

Mirror of the Watcher Cashier pass:

- **Tool**: `tools/build-bartender-3d-model.py`, subclassing
  `PedestrianBuilder` via the cashier's `load_character_build_base()`
  idiom from `tools/build-supermarket-cashier-3d-model.py`.
- **Skeleton**: the canonical pedestrian skeleton (shared retarget
  contract) plus two extra arm chains per side:
  `upper_arm2.L/R → forearm2.L/R → hand2.L/R` (mid pair) and
  `upper_arm3.L/R → forearm3.L/R → hand3.L/R` (lower pair), parented
  to `chest`. Every one of the six hands carries the canonical
  non-deform sockets: `SOCKET_Grip`, `SOCKET_Bottle`, `SOCKET_Vessel`
  suffixed per chain (`SOCKET_Bottle.R`, `SOCKET_Bottle2.R`, …), same
  head/tail conventions as the pedestrian sockets (bottle/vessel
  socket +Y runs palm→ground at rest — the patron-bottle rig already
  depends on that).
- **Geometry**: box/ellipsoid parts per bone exactly like the base
  builder; triangle budget ≤ 1.8x the cashier's (six arms are the
  point; everything else stays cheap). Arms 2/3 reuse the arm part
  proportions scaled 0.94 / 0.88.
- **Clips**: `BartenderIdle` (the three-pair business above, ~6 s
  loop, authored via the `BonePose`/`ActionSpec` tables), and
  `BartenderAttend` (a 1.2 s settle: all six arms drop to ready — the
  clip that plays when service opens). No walk, no sit.
- **Validation + export**: `validate_cashier_result`-style checks
  (socket presence per hand, weight ownership, budget), manifest JSON
  + preview render into `ArtSource/Bar/Bartender/`, FBX into
  `Assets/Bar/Bartender/Models/BartenderSixArm3D.fbx`.
- **Editor setup**: `BartenderAssetSetup` (mirror of the cashier's)
  bakes the prefab with a `BarBartenderAssetRegistry`: animator, bone
  and socket lookups for all six hands, renderer palette bindings,
  clip references. Prefab into `Resources/Bar/BartenderSixArm3D`.

## 3. Runtime presentation

- `BarBartenderPresentation`: manually-advanced PlayableGraph
  (pedestrian idiom — idle/attend mixer, no controller), spawned at
  the layout's authored `BarNpcRole.Bartender` anchor, facing the
  service side of the counter.
- **Arm layers**: six independent `BartenderArmPose` procedural
  additive layers, one per chain — the exact capture → CCD → slerp
  idiom of `HomeTeethBrushingArmPose` / `BarPatronDrinkingArmPose`
  (the recorded standard exception in `ai/architecture-notes.md`).
  Each layer owns `(upperArm, forearm, hand)` of one chain, an
  effector (that hand's grip socket), a target provider and a weight.
- **Arm allocator**: a pure `BartenderArmAllocator` — given a world
  target and the set of busy arms, returns the free hand with the
  cheapest reach (distance from that pair's shoulder line, upper pair
  preferring high shelf rows, lower pair the counter). Deterministic,
  EditMode-testable.

## 4. Service integration — the bartender pours, not the hero

The load-bearing trick: **bottle and vessel motion stay exactly on the
authored `BarDrinkServiceTimeline` channels** (`BottleTravel`,
`BottleTilt`, `VesselVisibility`, `StreamVisibility`, `VesselFill` —
all existing tests and determinism untouched). The bartender's hands
are *readers* of that motion, never drivers: every frame the assigned
arm CCD-follows the moving bottle's grip point, so his hand visibly
carries what the timeline moves.

Per phase of `BarDrinkServicePhase`:

- **Browsing**: pointer hover (`BarDrinkShopController` selection
  highlight) allocates the nearest free arm at weight ~0.6 to *touch*
  the hovered bottle on its shelf slot — fingertips against the
  label, retracting when the hover moves. Six arms mean rapid hover
  changes read as restless competence, not popping.
- **BottlePickup → Pouring → BottleReturn**: the touching arm becomes
  the carrying arm (weight 1, wrist aligned to the timeline's
  `BottleTilt`); a second arm tracks the vessel during
  `VesselPlacement` and steadies it through `Pouring`. The remaining
  pairs continue idle business — that contrast is where six arms pay
  off.
- **Drinking**: the bartender's arms release (weights ease out); the
  hero's `BarDrinkFirstPersonArms` keep exactly one job — the
  existing left-hand vessel lift. Its right-arm bottle-grip role is
  retired; `ArmsVisibility` now gates only the drinking lift.
- **Cancel/CameraReturn**: all arm weights ease to zero over the
  existing return blends; allocator resets.

## 5. Cocktails

- **Order model**: in Browsing a new "mix" affordance (localized
  prompt keys `interaction.mix_drink` / `interaction.serve_mix`) lets
  the hero queue **2–3 alcoholic ingredients** (water excluded) before
  confirming. A pure `BarCocktailOrder` holds the queued `DrinkId`s
  and derives price (sum of offers + a flat mixing fee), intoxication
  payload (sum), and the blended liquid color (mean of the
  ingredients' `BarDrinkPresentation` liquid colors).
- **Choreography**: the confirmed sequence loops
  `BottlePickup → Pouring(short, 0.9 s) → hold` once per ingredient —
  and **each ingredient gets its own arm**, which keeps holding its
  bottle mid-air after pouring. The finale: all engaged arms return
  their bottles to the shelf simultaneously — the six-armed chord the
  whole feature exists for. Timeline change is additive: an
  ingredient-loop wrapper around the existing phases with
  per-ingredient durations; `ConfirmedPresentationDurationSeconds`
  becomes a function of ingredient count.
- **Names**: a small fixed table for the recognizable pairs
  (`Ёрш` = beer + vodka first among them), falling back to
  "Смесь №<stable hash>" for uncatalogued blends. Names surface in
  the service UI and the intoxication HUD toast.
- **State**: no new `DrinkId`s. `GameSessionState` gains a served-mix
  commit (ingredient list) so persistence and intoxication rules stay
  on existing enums; purchase validation walks the ingredient offers.

## 6. Tests

- EditMode: rig contract (six hand chains + sockets on the imported
  prefab, cashier-test idiom); `BartenderArmAllocator` determinism
  and busy-arm exclusion; `BarCocktailOrder` pricing/color/naming;
  extended timeline ingredient-loop durations and channel bounds.
- PlayMode: bartender present at the anchor with patrons unchanged;
  during `Pouring` the carrying hand's grip socket sits within
  `0.10 m` of the bottle grip point; a 3-ingredient cocktail runs
  end-to-end and commits the summed transaction; cancellation mid-mix
  restores every bottle exactly (the `BarDrinkBottleView` snapshot
  contract already guarantees the bottle side).
- A temporary D3D11 capture (removed after use, per convention) to
  eyeball idle, the shelf touch, the carry and the six-armed chord.

## 7. Build order

1. **Model pass**: tool + FBX + prefab + registry + rig contract
   tests. Bartender stands behind the counter playing `BartenderIdle`.
2. **Presence pass**: presentation + arm layers idling in the bar;
   patrons/service untouched.
3. **Service pass**: allocator + hover-touch + carry/steady during the
   existing single-drink flow; retire the first-person right arm.
4. **Cocktail pass**: order model, ingredient-loop timeline, names,
   state commit, UI affordance.

Each pass lands independently green; the bar is never broken between
them.
