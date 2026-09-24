using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private const int SnowWalkGait = 7;
        private const int SnowWalkBackwardGait = 8;
        // Two measured short steps form one left/right cycle.
        public const float SnowCycleDistance = 1.10f;
        private const float SnowFullPoseSpeed = 0.20f;
        private AnimationClipPlayable snowWalkPlayable, snowWalkBackwardPlayable;
        private Player3DAnimationBinding snowWalkBinding, snowWalkBackwardBinding;
        private bool trackingSnowContacts, snowContactBackward;
        private long lastSnowContactHalfStep;

        /// <summary>A planted snow step after late IK; true identifies the left foot.</summary>
        public event Action<bool> SnowFootContact;

        /// <summary>The visible ordinary snow gait, including its entry/exit blend.</summary>
        public float SnowBlend => locomotionMixer.IsValid()
            ? locomotionMixer.GetInputWeight(SnowWalkGait + 1) +
              locomotionMixer.GetInputWeight(SnowWalkBackwardGait + 1)
            : 0f;

        public float SnowGaitCycle => gaitWeights[SnowWalkBackwardGait] > gaitWeights[SnowWalkGait]
            ? NormalizedGaitCycle(snowWalkBackwardPlayable, snowWalkBackwardBinding)
            : NormalizedGaitCycle(snowWalkPlayable, snowWalkBinding);

        private void BuildSnowGaits()
        {
            snowWalkPlayable = CreateLocomotionPlayable(snowWalkBinding);
            snowWalkBackwardPlayable = CreateLocomotionPlayable(snowWalkBackwardBinding);
            graph.Connect(snowWalkPlayable, 0, locomotionMixer, SnowWalkGait + 1);
            graph.Connect(snowWalkBackwardPlayable, 0, locomotionMixer, SnowWalkBackwardGait + 1);
        }

        private void ApplySnowMotion(in PlayerMotionSample motion, ref Player3DLocomotionState state)
        {
            float snow = Mathf.Clamp01(motion.SnowBlend);
            if (snow <= 0f) return;

            // A sprint request cannot select Run even while the snow pose is
            // entering. Its already visible pose may finish its short crossfade.
            targetGaitWeights[WalkGait] += targetGaitWeights[RunGait];
            targetGaitWeights[RunGait] = 0f;
            if (state == Player3DLocomotionState.Run) state = Player3DLocomotionState.Walk;
            if (planarSpeed < 0.005f) return;

            // Keep the high knee/short step at a slow speed. Fading against
            // Idle by the ordinary 2.6 m/s cap would erase its snow clearance.
            float snowWeight = snow * Mathf.Clamp01(planarSpeed / SnowFullPoseSpeed);
            for (int gait = 0; gait < GaitCount; gait++)
                targetGaitWeights[gait] *= 1f - snow;
            bool backward = motion.SignedForwardSpeed < -0.005f;
            targetGaitWeights[backward ? SnowWalkBackwardGait : SnowWalkGait] = snowWeight;
            if (snowWeight >= MotionThreshold)
                state = backward ? Player3DLocomotionState.SnowWalkBackward : Player3DLocomotionState.SnowWalk;
        }

        private void UpdateBackwardGaitCadence()
        {
            float ordinary = gaitWeights[WalkBackGait];
            float snow = gaitWeights[SnowWalkBackwardGait];
            float cycles = Mathf.Lerp(0.70f, 0.90f, ordinary) /
                Mathf.Max(0.0001f, walkBackBinding.Clip.length);
            cycles = Mathf.Lerp(cycles, planarSpeed / SnowCycleDistance,
                snow / Mathf.Max(0.0001f, ordinary + snow));
            walkBackPlayable.SetSpeed(cycles * walkBackBinding.Clip.length);
            SynchronizeSnowGait(snowWalkBackwardPlayable, snowWalkBackwardBinding,
                walkBackPlayable, walkBackBinding, cycles);
        }

        private static void SynchronizeSnowGait(AnimationClipPlayable snowPlayable,
            Player3DAnimationBinding snowBinding, AnimationClipPlayable ordinaryPlayable,
            Player3DAnimationBinding ordinaryBinding, float cyclesPerSecond)
        {
            // Match contact phase before evaluating the graph. This remains
            // continuous if a modal action has held the ordinary graph still.
            double phase = ordinaryPlayable.GetTime() / ordinaryBinding.Clip.length;
            snowPlayable.SetTime(phase * snowBinding.Clip.length);
            snowPlayable.SetSpeed(cyclesPerSecond * snowBinding.Clip.length);
        }

        private static float NormalizedGaitCycle(AnimationClipPlayable playable,
            Player3DAnimationBinding binding)
        {
            return playable.IsValid() && binding != null && binding.Clip != null
                ? Mathf.Repeat((float)(playable.GetTime() / binding.Clip.length), 1f)
                : 0f;
        }

        private void ApplySnowFootPlant()
        {
            float snow = SnowBlend;
            if (snow <= 0f || locomotionBlend <= 0f) return;
            PlayerFootPlacementRules.SnowFootPlantAmounts(
                NormalizedGaitCycle(snowWalkPlayable, snowWalkBinding),
                out float snowLeft, out float snowRight);
            PlayerFootPlacementRules.SnowFootPlantAmounts(
                NormalizedGaitCycle(snowWalkBackwardPlayable, snowWalkBackwardBinding),
                out float backwardLeft, out float backwardRight);
            float backwardShare = gaitWeights[SnowWalkBackwardGait] /
                Mathf.Max(0.0001f, gaitWeights[SnowWalkGait] + gaitWeights[SnowWalkBackwardGait]);
            snowLeft = Mathf.Lerp(snowLeft, backwardLeft, backwardShare);
            snowRight = Mathf.Lerp(snowRight, backwardRight, backwardShare);
            float share = Mathf.Clamp01(snow / locomotionBlend);
            float left = Mathf.Lerp(footPlantLeft,
                Mathf.Lerp(1f, snowLeft, locomotionBlend), share);
            float right = Mathf.Lerp(footPlantRight,
                Mathf.Lerp(1f, snowRight, locomotionBlend), share);
            bool snowLeads = snow >= locomotionBlend - snow;
            bool forward = snowLeads
                ? gaitWeights[SnowWalkGait] >= gaitWeights[SnowWalkBackwardGait]
                : forwardGaitDominant;
            if (snowLeads) forwardGaitCycle = forward ? SnowGaitCycle : 0f;
            SetFootPlant(PlayerFootPlacementRules.CombinedPlant(left, right), left, right, forward);
        }

        private void EmitSnowFootContacts()
        {
            bool eligible = SnowBlend > 0.5f && planarSpeed > 0.035f &&
                (!IsClipActive || scopedClipLocomotion) && !interactionHandoffLocked &&
                !ragdollPoseActive && !risePose.Active &&
                balancePose.Phase != BalancePhase.Toppling && balancePose.Phase != BalancePhase.Fallen;
            if (!eligible)
            {
                trackingSnowContacts = false;
                return;
            }

            bool backward = gaitWeights[SnowWalkBackwardGait] > gaitWeights[SnowWalkGait];
            AnimationClipPlayable playable = backward ? snowWalkBackwardPlayable : snowWalkPlayable;
            Player3DAnimationBinding binding = backward ? snowWalkBackwardBinding : snowWalkBinding;
            long halfStep = (long)Math.Floor(playable.GetTime() / binding.Clip.length * 2d + 0.000001d);
            if (!trackingSnowContacts || backward != snowContactBackward || halfStep < lastSnowContactHalfStep)
            {
                trackingSnowContacts = true;
                snowContactBackward = backward;
                lastSnowContactHalfStep = halfStep;
                return;
            }

            // A paused graph has no crossings. A hitch still delivers each
            // contact once, through the surface's existing audio/kickup owner.
            for (long contact = lastSnowContactHalfStep + 1; contact <= halfStep; contact++)
            {
                bool left = (contact & 1L) == 0L;
                if ((left ? LeftFootGround : RightFootGround).HasSurface)
                    SnowFootContact?.Invoke(left);
            }
            lastSnowContactHalfStep = halfStep;
        }

        private void ClearSnowGaits()
        {
            snowWalkPlayable = snowWalkBackwardPlayable = default;
            snowWalkBinding = snowWalkBackwardBinding = null;
            trackingSnowContacts = false;
            lastSnowContactHalfStep = 0;
        }
    }
}
