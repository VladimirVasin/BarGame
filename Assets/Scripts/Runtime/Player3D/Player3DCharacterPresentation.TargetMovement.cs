using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private const int StrafeLeftGait = 5, StrafeRightGait = 6;
        private const float SideStepCycleDistance = .60f;
        private AnimationClipPlayable strafeLeftPlayable, strafeRightPlayable;
        private Player3DAnimationBinding strafeLeftBinding, strafeRightBinding;
        private float sideStepSpeed;

        private bool EnsureSideStepGaits()
        {
            if (strafeLeftPlayable.IsValid() && strafeRightPlayable.IsValid()) return true;
            if (!graph.IsValid() || !TryResolveAnimation("CombatStrafeLeft", out strafeLeftBinding) ||
                !TryResolveAnimation("CombatStrafeRight", out strafeRightBinding)) return false;
            strafeLeftPlayable = CreateLocomotionPlayable(strafeLeftBinding);
            strafeRightPlayable = CreateLocomotionPlayable(strafeRightBinding);
            graph.Connect(strafeLeftPlayable, 0, locomotionMixer, StrafeLeftGait + 1);
            graph.Connect(strafeRightPlayable, 0, locomotionMixer, StrafeRightGait + 1);
            return true;
        }

        private bool TrySetSideStepMotion(in PlayerMotionSample motion, out Player3DLocomotionState state)
        {
            state = Player3DLocomotionState.Idle;
            if (sideStepSpeed < .035f || !EnsureSideStepGaits()) return false;
            float forwardSpeed = Mathf.Abs(motion.SignedForwardSpeed);
            float sideShare = sideStepSpeed / (sideStepSpeed + forwardSpeed);
            bool left = motion.SignedSideSpeed < 0f;
            // Speed changes cadence, not the width of the authored planted shuffle.
            // Blend its pose only at starts/stops and against diagonal forward travel.
            float sideWeight = Mathf.Clamp01(sideStepSpeed / .15f) * sideShare;
            targetGaitWeights[left ? StrafeLeftGait : StrafeRightGait] = sideWeight;
            bool backward = motion.SignedForwardSpeed < 0f;
            float forwardWeight = Mathf.Clamp01(forwardSpeed / (backward ? FullWalkBackSpeed : FullWalkSpeed)) *
                (1f - sideShare);
            if (backward) targetGaitWeights[WalkBackGait] = forwardWeight;
            else
            {
                targetGaitWeights[RunGait] = forwardWeight * motion.RunBlend;
                targetGaitWeights[WalkGait] = forwardWeight * (1f - motion.RunBlend);
            }
            state = sideShare >= .5f ? (left ? Player3DLocomotionState.StrafeLeft : Player3DLocomotionState.StrafeRight) :
                backward ? Player3DLocomotionState.WalkBack : motion.RunBlend > .5f ?
                    Player3DLocomotionState.Run : Player3DLocomotionState.Walk;
            return true;
        }

        private void UpdateSideStepCadence()
        {
            if (!strafeLeftPlayable.IsValid() || !strafeRightPlayable.IsValid()) return;
            float visibleWeight = gaitWeights[StrafeLeftGait] + gaitWeights[StrafeRightGait];
            // During a diagonal blend only this share of the side excursion survives.
            // Bound the brief start crossfade so its first tiny weight cannot race time.
            float cycles = sideStepSpeed / (SideStepCycleDistance * Mathf.Max(.5f, visibleWeight));
            strafeLeftPlayable.SetSpeed(cycles * strafeLeftBinding.Clip.length);
            strafeRightPlayable.SetSpeed(cycles * strafeRightBinding.Clip.length);
        }

        private bool TryApplySideStepFootPlant()
        {
            float leftWeight = gaitWeights[StrafeLeftGait], rightWeight = gaitWeights[StrafeRightGait];
            float sideWeight = leftWeight + rightWeight;
            if (sideWeight <= .0001f || sideWeight < gaitWeights[WalkGait] + gaitWeights[RunGait] ||
                sideWeight < gaitWeights[WalkBackGait]) return false;
            bool movingLeft = leftWeight >= rightWeight;
            AnimationClipPlayable playable = movingLeft ? strafeLeftPlayable : strafeRightPlayable;
            Player3DAnimationBinding binding = movingLeft ? strafeLeftBinding : strafeRightBinding;
            if (!playable.IsValid() || binding == null) return false;
            float cycle = Mathf.Repeat((float)playable.GetTime() / binding.Clip.length, 1f);
            float half = Mathf.Repeat(cycle * 2f, 1f);
            float lift = Mathf.Sin(half * Mathf.PI);
            float swingPlant = 1f - .8f * lift * lift;
            bool leftSwing = movingLeft == (cycle < .5f);
            float left = Mathf.Lerp(1f, leftSwing ? swingPlant : 1f, sideWeight);
            float right = Mathf.Lerp(1f, leftSwing ? 1f : swingPlant, sideWeight);
            forwardGaitCycle = 0f;
            SetFootPlant(PlayerFootPlacementRules.CombinedPlant(left, right), left, right, false);
            return true;
        }

        private void ClearSideStepGaits()
        {
            strafeLeftPlayable = strafeRightPlayable = default;
            strafeLeftBinding = strafeRightBinding = null;
            sideStepSpeed = 0f;
        }
    }
}
