# Contextual player-animation standard

## Status and scope

This is a mandatory project rule for every future world/area contextual
interaction that starts from `E` or an equivalent prompt, click or confirmation
and temporarily replaces the ordinary `PlayerSpriteRig` with a bespoke player
sprite/atlas animation.

It does not automatically govern ordinary locomotion, procedural idle, NPC
animation or a presentation that never replaces the player rig. A deviation
for an interaction inside this scope requires an explicit user decision recorded
as an accepted exception in `ai/architecture-notes.md`. Art mismatch, schedule
pressure or implementation convenience are not exceptions.

## Mandatory authoring contract

1. The interaction plan owns independent authored entry root/hip/facing, action
   anchor and exit root/hip/facing data. Entry and exit may currently coincide,
   but they must remain separate data. The trigger area determines eligibility;
   it is never a hidden snap destination.
2. Choose the final gameplay camera shot and billboard mode before locking the
   endpoint art. Author the grounded entry and exit poses inside valid walkable
   space, with the correct `CharacterController` root height, clearance, foot
   baseline, PPU and hip pivot.
3. Determine endpoint direction from the real ordinary rig at the authored pose
   under the final camera. Do not guess the `PlayerViewDirection`. Camera-plane
   and world-up presentation, handedness and configured texture flip are part of
   the endpoint contract.
4. The first atlas frame must visibly match the neutral ordinary rig at entry
   after every configured flip and projection. The terminal atlas frame must
   visibly match the neutral ordinary rig at exit. Pose, direction, silhouette,
   scale, physical asymmetry, feet and hip pivot must agree.
5. Author the actual enter/action/exit motion between those endpoints. Do not
   manufacture a fade, alpha crossfade, ordered-dither dissolve, blended idle
   pixels or another concealment bridge to compensate for mismatched art.

## Mandatory runtime contract

1. Before atlas playback, lock manual input but keep the ordinary rig and its
   normal shadows fully visible. Guide the hero to the exact grounded entry with
   the shared constrained `PlayerMotor`/`CharacterController`, including normal
   gait, turn and footsteps. Player input must not redirect this movement.
2. Never teleport or invisibly relocate the hero into entry. Reject an
   unreachable vertical level and cancel a blocked or no-progress approach. If
   the hero already occupies the exact entry pose, the neutral settle-frame rule
   still applies.
3. At exact entry, select the actual nearest ordinary-rig direction without
   hysteresis, reset gait/breath/face/intoxication presentation offsets and hold
   the neutral ordinary endpoint for at least one rendered frame.
4. Switch visibility directly from ordinary rig to atlas. Installed definitions
   in this class must use `visualCrossfadeDurationSeconds == 0`; opacity is a
   hard `1/0` or `0/1` handoff. Camera, lighting or audio fades that do not hide
   a player-sprite mismatch remain independent and are allowed.
5. The authored exit animation carries the visible hero to the independent exit
   pose. Present the terminal atlas frame for at least one rendered frame even
   when a timing hitch crosses the nominal phase duration. Restore the physical
   root, facing and neutral ordinary rig at that exact endpoint, keep the
   handoff lock through the final `LateUpdate`, then return control.
6. Camera-plane endpoints resolve their upright hip/foot reference against the
   live `camera.up` and refresh after camera motion. World-up endpoints remain
   upright. Neither mode may introduce a one-frame pivot or foot slip.
7. Failed preparation, stale inventory requirements, unreachable or stalled
   positioning, scene transition, cancellation, disable and destroy must use
   owned cleanup. Restore input, rig, shadows, camera, HUD and any prepared
   partner animation without leaking state or consuming a resource before all
   required presentation resources have prepared successfully.
8. Reuse `PlayerAnimatedInteractionController.BeginPositioned` and the shared
   pose/timeline/handoff path, or extend that path generically. Do not add a
   one-off interaction that bypasses this standard.

## Required coverage, not a per-request run list

The following contracts must exist for this animation class, but they are split
between shared-pipeline coverage and per-animation coverage. During ordinary
iteration, run only the smallest relevant selection under
`ai/prompt-templates.md`. Do not run full suites, a player build or smoke merely
because this checklist exists.

Shared `PlayerAnimatedInteraction` coverage owns bounded positioning, held-input
resistance, wrong-height/stall rejection, transition/disable/destroy cleanup,
neutral settle and deferred unlock, direct opacity, camera-plane/world-up pivot
resolution and terminal-frame behavior under normal, single-frame and hitch
steps. Do not duplicate all of those cases for every new animation. Run these
shared tests only when the shared pipeline changes.

Each new animation needs only its unique evidence:

- extend one deterministic/parameterized plan or asset validator to cover its
  independent entry/action/exit data, grounded dock, facing, configured
  billboard mode, pivots, import/frame order, endpoint match and zero fade;
- reuse parameterized happy-path integration coverage where possible; add at
  most one focused PlayMode interaction only when unique scene wiring cannot be
  represented by the existing coverage;
- one adapter-specific atomicity test only when the animation introduces a new
  inventory or other committed-resource contract.

Do not clone shared failure, timing or lifecycle cases for every animation.
Ordinary implementation runs the validator and, only when scene/runtime wiring
changed, the single happy-path integration at most. Broader shared tests are
release gates or evidence for a change to the shared pipeline itself.
