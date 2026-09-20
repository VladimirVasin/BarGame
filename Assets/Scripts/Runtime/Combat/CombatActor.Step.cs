using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private AnimationClip[] stepClips;
        private AnimationClip stepClip;
        private Vector3 stepDirection;
        private bool stepBlocked;

        private void LoadStepClips()
        {
            string[] names = CombatAssetProvider.HeroStepClipNames;
            stepClips = new AnimationClip[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                AnimationClip clip = CombatAssetProvider.LoadClip(names[i]);
                stepClips[i] = clip;
                hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(
                    clip.name, "Combat", clip, clip.length, false));
            }
        }

        /// <summary>A committed grounded step. Defence comes from distance, never disabled hit colliders.</summary>
        public bool TryStep(Vector2 input)
        {
            if (hero == null || stepClips == null || roundEnded || !IsAvailable ||
                !GameInput.CanRead(GameInputContext.Gameplay) ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y) || !State.TryStartStep()) return false;
            // Cardinal clips have authored sole contacts. Resolve diagonals to
            // their dominant direction instead of sliding that pose sideways.
            bool side = Mathf.Abs(input.x) > Mathf.Abs(input.y);
            Vector3 localDirection = side ? (input.x < 0f ? Vector3.left : Vector3.right) :
                input.y > .01f ? Vector3.forward : Vector3.back;
            string name = side ? (input.x < 0f ? "CombatStepLeft" : "CombatStepRight") :
                input.y > .01f ? "CombatStepForward" : "CombatStepBackward";
            foreach (AnimationClip clip in stepClips)
                if (clip.name == name) { stepClip = clip; break; }
            stepDirection = transform.TransformDirection(localDirection);
            stepDirection.y = 0f;
            stepDirection.Normalize();
            stepBlocked = false;
            reaction = null; sweepValid = false;
            Present();
            return true;
        }

        private void AdvanceStepMovement(float from, float to)
        {
            if (stepBlocked || motor == null || stepDirection.sqrMagnitude < .5f || to <= from) return;
            float distance = State.Settings.StepDistance *
                (Mathf.SmoothStep(0f, 1f, to) - Mathf.SmoothStep(0f, 1f, from));
            Vector3 moved = motor.ApplyOwnedDisplacement(this, stepDirection * distance);
            // The soles compensate the authored travel. Once a wall refuses it,
            // settle at the actual position instead of completing a stride into
            // the obstacle. The rules still retain the full cost and commitment.
            if (Vector3.Dot(moved, stepDirection) + .001f < distance) stepBlocked = true;
        }
    }
}
