using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CemeteryMournerPhase
    {
        Approach = 0,
        LayFlowers = 1,
        Cry = 2,
        WipeTears = 3,
        Depart = 4,
        Done = 5
    }

    /// <summary>
    /// One mourner visit's clock: the walk in, laying the flowers,
    /// exactly thirty seconds of crying, wiping the tears and the walk
    /// out. The three graveside phases together equal one playback of
    /// the authored MournerMourn clip, so pose and script can never
    /// drift apart. Pure and EditMode-testable — the bar patron
    /// drinking timeline idiom.
    /// </summary>
    public sealed class CemeteryMournerTimeline
    {
        /// <summary>Graveside phase lengths. They must match the
        /// authored section boundaries of the MournerMourn clip in
        /// tools/build-city-pedestrian-3d-model.py.</summary>
        public const float LaySeconds = 3.5f;
        public const float CrySeconds = 30f;
        public const float WipeSeconds = 3f;

        /// <summary>The moment inside LayFlowers when the bouquet
        /// leaves her hands: the deepest point of the authored bow.</summary>
        public const float LayCueSeconds = 1.9f;

        private float phaseElapsed;
        private float phaseDuration;
        private bool layCueConsumed;

        public CemeteryMournerTimeline(
            float approachSeconds,
            float departSeconds)
        {
            if (float.IsNaN(approachSeconds) ||
                float.IsInfinity(approachSeconds) ||
                approachSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(approachSeconds));
            }

            if (float.IsNaN(departSeconds) ||
                float.IsInfinity(departSeconds) ||
                departSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(departSeconds));
            }

            ApproachSeconds = approachSeconds;
            DepartSeconds = departSeconds;
            phaseDuration = approachSeconds;
        }

        public float ApproachSeconds { get; }
        public float DepartSeconds { get; }
        public CemeteryMournerPhase Phase { get; private set; } =
            CemeteryMournerPhase.Approach;
        public float PhaseElapsed => phaseElapsed;
        public float PhaseDuration => phaseDuration;
        public bool IsDone => Phase == CemeteryMournerPhase.Done;

        public bool IsWalkingPhase =>
            Phase == CemeteryMournerPhase.Approach ||
            Phase == CemeteryMournerPhase.Depart;

        public bool IsGravesidePhase =>
            Phase == CemeteryMournerPhase.LayFlowers ||
            Phase == CemeteryMournerPhase.Cry ||
            Phase == CemeteryMournerPhase.WipeTears;

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            phaseElapsed += Mathf.Max(0f, deltaTime);
            // The remainder of a step that crosses a boundary belongs
            // to the next phase, so a hitchy frame cannot silently
            // stretch the ritual.
            while (Phase != CemeteryMournerPhase.Done &&
                   phaseElapsed >= phaseDuration)
            {
                float carried = phaseElapsed - phaseDuration;
                switch (Phase)
                {
                    case CemeteryMournerPhase.Approach:
                        SetPhase(
                            CemeteryMournerPhase.LayFlowers,
                            LaySeconds);
                        break;
                    case CemeteryMournerPhase.LayFlowers:
                        SetPhase(CemeteryMournerPhase.Cry, CrySeconds);
                        break;
                    case CemeteryMournerPhase.Cry:
                        SetPhase(
                            CemeteryMournerPhase.WipeTears,
                            WipeSeconds);
                        break;
                    case CemeteryMournerPhase.WipeTears:
                        SetPhase(
                            CemeteryMournerPhase.Depart,
                            DepartSeconds);
                        break;
                    case CemeteryMournerPhase.Depart:
                        SetPhase(CemeteryMournerPhase.Done, 0f);
                        break;
                }

                phaseElapsed = carried;
                if (Phase == CemeteryMournerPhase.Done)
                {
                    phaseElapsed = 0f;
                    break;
                }
            }
        }

        /// <summary>
        /// One-shot: true exactly once per visit, at the authored
        /// moment of the lay bow when the bouquet touches the slab.
        /// </summary>
        public bool ConsumeLayCue()
        {
            if (Phase != CemeteryMournerPhase.LayFlowers ||
                layCueConsumed ||
                phaseElapsed < LayCueSeconds)
            {
                return false;
            }

            layCueConsumed = true;
            return true;
        }

        private void SetPhase(CemeteryMournerPhase phase, float duration)
        {
            Phase = phase;
            phaseDuration = duration;
            phaseElapsed = 0f;
        }
    }
}
