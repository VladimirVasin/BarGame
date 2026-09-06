# Contextual player-animation standard

## Status and scope

This is the mandatory project rule for every world or area interaction that
starts from `E`, a prompt click or confirmation and takes ownership of the main
hero presentation. The production hero is one continuous modular 3D character;
an interaction either drives that world rig or derives a camera-local
first-person subset from the same prefab and materials.

The rule does not govern ordinary locomotion, NPC animation or a presentation
that never takes ownership of the player. A deviation requires an explicit user
decision recorded as an accepted exception in `ai/architecture-notes.md`.

## Mandatory authoring contract

1. The interaction plan owns independent entry root/pelvis/facing, action
   pelvis and exit root/pelvis/facing data. Entry and exit may currently
   coincide, but they remain separate authored values. A trigger determines
   eligibility; it is never a hidden snap destination.
2. Choose the final gameplay camera and presentation mode before locking
   endpoints. Author grounded entry and exit poses inside valid walkable space,
   using the real `CharacterController` root height and validated feet, pelvis,
   head and grip anchors. First-person subsets additionally own a camera-local
   origin and near-plane-safe bounds.
3. Author full-body actions against the production A-pose Generic skeleton.
   Clips are bone-only and in-place, use no gameplay root motion or Animation
   Events, and preserve anatomical `.L/.R`, including the left bandage and
   right shoulder patch.
4. The ordinary neutral pose and first entry sample, plus the terminal action
   sample and restored exit pose, must agree at their shared endpoints. Bed,
   smoking and cat feeding use the existing `Enter`, `Loop` and `Exit` actions
   serialized by `Player3DAssetRegistry`.
5. A first-person arm or hand is a filtered instance of
   `Resources/Player/Player3DV2.prefab`. It reuses the registered arm meshes,
   palette and grip socket; it may not introduce a different hero model. The
   same holds for a hero standing in for the hero somewhere else in the world:
   the bathroom mirror's reflection (`HomeMirrorHeroTwin`) is a second instance
   of that one prefab with its animator off, driven bone for bone from the
   real rig, so there is still exactly one hero model in the project.
6. Do not manufacture opacity fades, alpha crossfades, ordered-dither
   dissolves, camera cuts or hidden teleports to conceal endpoint mismatch.

## Mandatory runtime contract

1. Before action playback, lock manual input but keep the ordinary hero and
   normal mesh/contact shadows visible. Guide the hero to the exact grounded
   entry with the shared constrained `PlayerMotor` and `CharacterController`,
   including normal gait, turn and footsteps. Player input cannot redirect the
   move.
2. Never teleport or invisibly relocate the hero into entry. Reject an
   unreachable vertical level and cancel a blocked or no-progress approach. If
   the hero already occupies the entry pose, the neutral settle-frame rule
   still applies.
3. At exact entry, reset gait, breath, face and additive intoxication offsets,
   then hold the neutral endpoint for at least one rendered frame.
4. Begin the action on the same visible 3D rig. The deterministic interaction
   timeline owns normalized clip time, loop holds and terminal sampling;
   Animator transitions, root motion and Animation Events do not own gameplay
   transactions.
5. An interaction already holding a loop may run a nested
   `enter → loop → exit` action on the same rig. The nested action owns its
   temporary look/input lock and completion callback, then returns to the
   exact parent loop without replacing that interaction's lifecycle or cleanup.
6. Sample the active clip first, then align its registered pelvis anchor to the
   authored world target. Reset that spatial offset on normal completion,
   cancellation, disable, destroy and failed preparation.
7. A camera-local first-person subset acquires an owner-scoped world-visibility
   lease only when the subset becomes visible. The final lease release restores
   the exact world renderer and contact-shadow states.
8. The authored exit carries the visible hero to the independent exit pose.
   Present the terminal pose for at least one rendered frame even when a hitch
   crosses the nominal phase duration. Restore root, facing and neutral
   presentation at that endpoint, then defer input unlock until the final
   presentation `LateUpdate` completes.
9. Failed preparation, stale inventory requirements, scene transition,
   cancellation, disable and destroy use owned idempotent cleanup. Restore
   input, hero presentation, spatial offsets, visibility leases, contact
   shadow, camera, HUD, props and prepared partner animation without consuming
   a resource before every required presentation asset is ready.
10. Reuse `PlayerAnimatedInteractionController.BeginPositioned` and the shared
   positioning/timeline/cleanup path, or extend that path generically. Do not
   add a one-off interaction that bypasses this standard.

## Required coverage, not a per-request run list

Shared contextual coverage owns bounded positioning, held-input resistance,
wrong-height and stall rejection, transition/disable/destroy cleanup, neutral
settle, deterministic clip sampling, pelvis alignment, deferred unlock,
visibility-lease ownership and terminal-pose behavior under normal, single-frame
and hitch steps.

Each new full-body action needs only its unique evidence:

- extend one deterministic plan or asset validator for independent
  entry/action/exit data, grounded dock, facing, required clips, loop flags,
  registered anchors and endpoint match;
- reuse parameterized happy-path integration coverage where practical;
- add one adapter-specific atomicity test only when the action introduces a new
  inventory or committed-resource contract;
- validate any first-person subset against the shared mesh/material registry
  and verify exact world-visibility restoration.

During ordinary iteration, run only the smallest relevant selection under
`ai/prompt-templates.md`. Complete suites, player builds and smoke runs remain
explicit release verification.
