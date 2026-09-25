using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private AnimationClip[] stepClips;
        private AnimationClip stepClip;
        private Vector3 stepDirection;
        private Vector2 pendingStepInput;
        private bool stepBlocked;

        private void LoadStepClips(bool forNpc)
        {
            string[] names = CombatAssetProvider.StepClipNames;
            stepClips = new AnimationClip[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                AnimationClip clip = CombatAssetProvider.LoadClip(names[i], forNpc);
                stepClips[i] = clip;
                if (!forNpc)
                    hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(
                        clip.name, "Combat", clip, clip.length, false));
            }
        }

        /// <summary>A committed grounded step, for either fighter. Defence comes from distance,
        /// never disabled hit colliders. A press in the tail of a committed phase waits for its boundary.</summary>
        public bool TryStep(Vector2 input)
        {
            int request = JournalCommand("step");
            if (stepClips == null) return JournalCommandResult(request, "rejected", "step_clips_missing");
            if (roundEnded) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (IsKnockedDown) return JournalCommandResult(request, "rejected", "knocked_down");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            if (float.IsNaN(input.x) || float.IsInfinity(input.x)) return JournalCommandResult(request, "rejected", "input_x_nonfinite", input.x);
            if (float.IsNaN(input.y) || float.IsInfinity(input.y)) return JournalCommandResult(request, "rejected", "input_y_nonfinite", input.y);
            // The rules learn only the lateral sign of a side step, resolved to the
            // same dominant axis as the clip: the step attack swings with the body.
            int lateral = Mathf.Abs(input.x) > Mathf.Abs(input.y) ? (input.x < 0f ? -1 : 1) : 0;
            if (!State.RequestStep(lateral)) return JournalRulesRejected(request, State.Settings.StepCost, true);
            pendingStepInput = input;
            if (State.Phase == MeleePhase.Step) BeginStepPresentation();
            Present();
            return JournalCommandResult(request, State.Phase == MeleePhase.Step ? "started" : "queued", "step");
        }

        private void BeginStepPresentation()
        {
            Vector2 input = pendingStepInput;
            // Cardinal clips have authored sole contacts. Resolve diagonals to
            // their dominant direction instead of sliding that pose sideways.
            bool side = Mathf.Abs(input.x) > Mathf.Abs(input.y);
            Vector3 localDirection = side ? (input.x < 0f ? Vector3.left : Vector3.right) :
                input.y > .01f ? Vector3.forward : Vector3.back;
            string name = side ? (input.x < 0f ? "CombatStepLeft" : "CombatStepRight") :
                input.y > .01f ? "CombatStepForward" : "CombatStepBackward";
            stepClip = null;
            if (stepClips != null)
                foreach (AnimationClip clip in stepClips)
                    if (clip.name == name) { stepClip = clip; break; }
            stepDirection = transform.TransformDirection(localDirection);
            stepDirection.y = 0f;
            stepDirection.Normalize();
            stepBlocked = false;
            reaction = null; sweepValid = false;
            RetroAudio.PlayAt(RetroSfxId.FootstepConcrete, transform.position, .6f);
        }

        private void AdvanceStepMovement(float from, float to)
        {
            if (stepBlocked || stepDirection.sqrMagnitude < .5f || to <= from) return;
            // Match the authored clip's smooth acceleration and braking on the
            // unwarped duel clock, so the loaded sole stays planted throughout.
            float distance = State.Settings.StepDistance *
                (Mathf.SmoothStep(0f, 1f, to) - Mathf.SmoothStep(0f, 1f, from));
            Vector3 moved;
            if (motor != null) moved = motor.ApplyOwnedDisplacement(this, stepDirection * distance);
            else if (npc != null && Body != null && Body.enabled)
            {
                // The opponent's controller mirrors the hero's owned displacement:
                // a wall or the other body refuses travel the same way.
                Vector3 before = transform.position;
                Body.Move(stepDirection * distance);
                moved = transform.position - before;
                moved.y = 0f;
            }
            else return;
            // The soles compensate the authored travel. Once a wall refuses it,
            // settle at the actual position instead of completing a stride into
            // the obstacle. The rules still retain the full cost and commitment.
            if (Vector3.Dot(moved, stepDirection) + .001f < distance)
            {
                stepBlocked = true;
                JournalEvent("step_blocked", action: State.AttackSequence, request: journalActionRequest,
                    f0: GameLog.Field("requested", distance), f1: GameLog.Field("achieved", Vector3.Dot(moved, stepDirection)),
                    f2: GameLog.Field("tolerance", .001f));
            }
            if (!stepBlocked && from < 1f && to >= 1f)
                RetroAudio.PlayAt(RetroSfxId.FootstepConcrete, transform.position, .75f);
        }
    }
}
