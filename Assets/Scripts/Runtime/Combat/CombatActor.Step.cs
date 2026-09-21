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
            if (stepClips == null || roundEnded || !IsAvailable ||
                !GameInput.CanRead(GameInputContext.Gameplay) ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y)) return false;
            // The rules learn only the lateral sign of a side step, resolved to the
            // same dominant axis as the clip: the step attack swings with the body.
            int lateral = Mathf.Abs(input.x) > Mathf.Abs(input.y) ? (input.x < 0f ? -1 : 1) : 0;
            if (!State.RequestStep(lateral)) return false;
            pendingStepInput = input;
            if (State.Phase == MeleePhase.Step) BeginStepPresentation();
            Present();
            return true;
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
            RetroAudio.PlayAt(RetroSfxId.FootstepConcrete, transform.position, .5f);
        }

        private void AdvanceStepMovement(float from, float to)
        {
            if (stepBlocked || stepDirection.sqrMagnitude < .5f || to <= from) return;
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
            if (Vector3.Dot(moved, stepDirection) + .001f < distance) stepBlocked = true;
        }
    }
}
