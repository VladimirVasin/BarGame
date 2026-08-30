# The Six-Armed Bartender — model, rig and service choreography spec

Status: **model, imported prefab/provider, runtime presence, procedural
idle and ordinary one-bottle service choreography implemented**.
Multi-ingredient cocktail ordering and its simultaneous six-arm
bottle-return chord remain designed but deferred.

## 1. Concept

A heavy, patient figure behind the counter with **three pairs of arms**
— the bar's one openly impossible thing, in the same register as the
Watcher Cashier and the Silent Hill attention idiom. He is not a
monster; he is staff. The wrongness is presented as pure competence:
six hands mean the pour never waits.

- Silhouette: a relatively heavy but believable adult `1.75 m` body
  under the shared `NpcHumanV2` standard, with a short neck and heavy
  head. Three arm pairs are stacked on the torso — shoulder line,
  mid-ribs line and floating-rib line — so the impossible outline still
  reads as a fan.
- The hero never sees him walk, but the production asset is a complete
  body with pelvis, thighs, lower legs and feet rather than the former
  waist-up proposal. The counter and `0.42 m` service duckboard hide
  most of that ordinary anatomy during play.
- Palette: waistcoat black-green over rolled shirt (bar's amber warms
  it), skin in the pale pedestrian range, one accent — a dull brass
  arm-band on the middle-right arm (the pouring arm).
- Idle is the sell: the canonical pair braces on the counter while the
  four extra hands trace quiet unsynchronized arcs that suggest
  polishing, drumming and waiting without literal props. Never
  symmetric, never still, never fast.

## 2. Model pipeline

The implemented pipeline mirrors the Watcher Cashier pass:

- **Tool**: `tools/build-bartender-3d-model.py`, subclassing
  `PedestrianBuilder` via the cashier's `load_character_build_base()`
  idiom from `tools/build-supermarket-cashier-3d-model.py`.
- **Manifest**: generator version `2.0.0`, anatomy standard
  `NpcHumanV2`, full height `1.75 m`, rest pelvis `0.835 m`,
  `50` meshes and `1,436` triangles.
- **Skeleton and Avatar**: the base rig remains the exact 31-bone Hero
  V2 A-pose hierarchy. Unity copies the Avatar from
  `Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx`. The four extra
  arms do not add bones to that retarget contract: their root-bound
  meshes are registered as `Arm2.L/R` and `Arm3.L/R` pivot chains
  with independent grip transforms, then reparented procedurally at
  runtime.
- **Geometry**: the full adult body uses the same low-poly
  box/ellipsoid language as the other NPCs. The two extra arm pairs
  remain the declared `extra_arm_pairs` signature overlay; the
  anatomy pass does not normalize them away or increase polygon
  density.
- **Animation**: the FBX intentionally contains no authored clips.
  Counter rest, unsynchronized idle and reach are owned by the runtime
  presentation.
- **Validation + export**: the measured manifest and FBX live at
  `Assets/Bar/Bartender/Models/BarBartender3D.{json,fbx}`; preview
  output remains under `ArtSource/Bar/Bartender/`.
- **Editor setup**: `BarBartenderAssetSetup` builds
  `Assets/Bar/Bartender/Prefabs/BarBartender.prefab` with a
  `BarBartenderAssetRegistry`. The Resources-loadable
  `Assets/Resources/Bar/BarBartenderProvider.asset` points to that
  production prefab. These outputs were rebuilt and replaced in the
  runtime path; this is no longer only a generator-side design.

## 3. Runtime presentation

- `BarBartenderWorldBuilder` loads `BarBartenderProvider`, instantiates
  the production prefab at the layout's authored
  `BarNpcRole.Bartender` anchor and raises it onto the service
  duckboard.
- `BarBartenderPresentation` folds the canonical upper arms from
  A-pose into counter rest, reparents the four extra pivot chains and
  gives every pair quiet, unsynchronized motion. Its extra chains use
  the established capture → CCD → slerp reach idiom.
- `BarBartenderServiceChoreography` assigns fixed, readable jobs
  rather than the proposed standalone allocator: the lower pair
  touches hovered bottles, the brass-banded middle-right arm follows
  the committed bottle, and the middle-left arm guides the vessel.

## 4. Implemented ordinary service integration

The load-bearing trick: **bottle and vessel motion stay exactly on the
authored `BarDrinkServiceTimeline` channels** (`BottleTravel`,
`BottleTilt`, `VesselVisibility`, `StreamVisibility`, `VesselFill` —
all existing tests and determinism untouched). The bartender's hands
are *readers* of that motion, never drivers: every frame the assigned
arm CCD-follows the moving bottle's grip point, so his hand visibly
carries what the timeline moves.

Per phase of `BarDrinkServicePhase`:

- **Browsing**: pointer hover (`BarDrinkShopController` selection
  highlight) sends the lower arm on the bottle's side toward its shelf
  position at weight `0.65`; the other lower arm returns to idle.
- **BottlePickup → Pouring → BottleReturn**: the brass-banded
  middle-right chain follows the committed bottle at full weight while
  the middle-left chain guides the vessel through
  `VesselPlacement`, `Pouring` and `BottleReturn`. The remaining
  pairs continue their idle business.
- **Drinking**: the bartender's service chains ease out while the
  existing first-person drinking presentation owns the final lift.
- **Cancel/CameraReturn**: all arm weights ease to zero over the
  existing return blends.

## 5. Cocktails — deferred

Everything in this section remains a proposed follow-up. None of the
multi-ingredient order model, UI, mixture state or bottle chord is
reported as current runtime behaviour.

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

## 6. Verification status

- Implemented EditMode coverage validates the provider, Hero V2 Avatar
  source, measured prefab/renderer contract and all four extra pivot
  chains.
- The bar scene smoke contract requires the bartender at the authored
  anchor and verifies every extra chain exposes a grip transform.
- Cocktail order, ingredient-loop, mixture-state and three-ingredient
  end-to-end tests remain part of the deferred cocktail pass; they
  must not be counted as current coverage.

## 7. Delivery state

1. **Model/import pass — implemented**: generator `2.0.0`, measured
   FBX/manifest, prefab, registry and provider.
2. **Presence pass — implemented**: world builder plus procedural
   counter idle for all three arm pairs.
3. **Ordinary service pass — implemented**: hover touch, bottle follow
   and vessel steady during the existing single-drink flow.
4. **Cocktail pass — deferred**: order model, ingredient-loop
   timeline, names, state commit, UI affordance and simultaneous
   bottle-return chord.
