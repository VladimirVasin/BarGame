# Item interaction and pickup standard

## Scope and decision

Accepted by the user on `2026-09-24`. All new or changed item interactions
reuse this contract. Source-specific rules stay with the source; presentation,
input, inventory credit and cleanup must not become a separate UI per object.
Exceptions require an explicit user decision in `ai/architecture-notes.md`.
The story/art bibles, speech and contextual-animation standards still apply.
This screen moves an item model; it does not authorize a new hero animation.

## Player flow

1. An eligible world target offers a localized action with its activation key:
   currently `E — взять полено` / `E — take a log`. Follow the shared `GameInput`
   bindings; a bare object name is not an actionable prompt. Distance, obstacles,
   transitions and other modal owners must reject unavailable interactions.
2. Show the shared item screen: the actual authored 3D model, readable name and
   description from `InventoryItemCatalog`, dark backdrop and one primary action.
   The model rotates automatically and can be turned with mouse/gamepad.
   Use `WorldItemFoundView`/`RetroItemPanel`; do not build another panel, camera
   pivot, backdrop, rotation or fit-to-frame implementation for a new source.
3. Choose one transaction mode below. Do not ask the player to approve the same
   take twice. An accepted Yes/No transaction leads to a receipt with a success
   line and Close, not another Take button.
4. Prompts, descriptions and receipt text remain silent. Spoken reactions use
   shared speech bubbles. RU/EN entries must satisfy story-bible §21.

## Transaction modes and current owners

| Situation | Reuse | Transaction |
| --- | --- | --- |
| A single collectible in the world | `WorldItemPickup` + `WorldItemPickupPlan` | `WorldItemFoundScreen.TryPresent`; Take calls `GameSessionState.TryCollectWorldItem` once. Source ID survives scene visits. |
| An already confirmed grant, including woodpiles | `InventoryTargetInteractionController` for Yes/No, then `WorldItemFoundScreen.TryPresentReceived` | Source calls `TryAddInventoryItem` once after rechecking capacity. Screen shows success and Close; closing/Escape cannot grant or refund. |
| Inspection inside an existing modal interaction | `HomeRefrigeratorItemInspectionController` pattern | Reuse `WorldItemInspectionPresenter`, `WorldItemInspectionTimeline` and `RetroItemPanel`; retain the outer owner, do not acquire a competing lock. |
| Reclaim CombatTest equipment | `WorldItemFoundScreen`: `CombatCrowbar`/`CombatPistol`/`CombatShotgun` | Take re-equips the same weapon after return, preserving ammo/reload. No grant; Cancel leaves it on the floor. |
| CombatTest ammunition | `WorldItemFoundScreen`: authored magazine/shells | Take credits the carried `IFirearmState` reserve once; full reserve refuses, a partial fill consumes one pack. No inventory grant; Cancel leaves the pack. |

`WorldItemFoundScreen` is installed once on the hero by `PlayerFactory`, resolved
through `WorldItemFoundScreen.For(interactor)`. `InventoryItemModelFactory` and
`InventoryItemPreviewPoses` supply the registered model/materials/pose shared
with inventory. New geometry remains Blender-authored under the project rules.

Before a normal Take, Escape/disable/unload leaves the item uncollected; a failed
commit leaves the screen open with refusal feedback and a way out. After a
successful grant, closing or losing the receipt leaves the item owned.
Normal world pickups show their plan's `TakenFeedbackKey` on successful
completion; an already granted receipt carries its success line on the screen.
Production players must have the initialized shared screen; stripped test rigs
may use the existing direct-grant fallback, which does not prove presentation.

Ordinary inventory capacity/collection belong to `GameSessionState`; CombatTest
gear/ammo belong to the actor/firearm state, never the screen. Replenishable
inventory sources own a temporary model and leave their stock visible.
A duplicate refusal appears only on
a new deliberate interaction. Never emulate a grant by a success label alone.

## Input and lifetime

- All buttons and bindings enter the same guarded Confirm/Dismiss paths.
  The opening press cannot also confirm/close the item screen:
  `WorldItemFoundScreen` defers input until the following frame.
- Modal release restores movement/camera/HUD ownership. `PlayerInteractor`
  suppresses the restored frame's physical interaction press, so Yes, No and
  Close cannot interact with the world again in that frame. Programmatic
  handoff between controllers remains available.
- The shared presenter restores the model's parent/pose before the completion
  callback; the source then removes a collected unique object, re-equips retained
  round equipment or destroys its temporary receipt model. Disable, destroy and
  scene unload must release the screen, model, locks and callbacks. No camera
  child may survive its owner.
- Settled inspection may borrow shared Bokeh at `35 mm/f8` on an eligible
  postprocessed gameplay camera. Never steal, retune or release an outer shot;
  restore/detach/cleanup immediately release only the presenter's token.
  The saved DoF flag applies. Inventory preview keeps local MSAA/fallback.

## Focused acceptance

Extend focused coverage for key/model/text, one credit, capacity refusal,
cancellation, receipt dismissal and cleanup. Exercise real confirmation through
close and the next world Update: direct calls missed woodpile input reuse.
Inspect actual UI captures when presentation changes. Current examples:
`AreaCaptureFixture.AlpineVillageStoveAndWoodpile` (Game View, no `-batchmode`)
and `WorldItemPickupModelTests` (pre-credit pickup). Session inventory/travel
are current; disk saves remain deferred.
