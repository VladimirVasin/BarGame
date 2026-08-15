using System;
using UnityEngine;

namespace BarPromenade
{
    public enum BarPatronDrinkPhase
    {
        Rest = 0,
        Raise = 1,
        Sip = 2,
        Lower = 3
    }

    /// <summary>
    /// One guest's drinking cadence: long seeded rests with the bottle
    /// dangling from the hand, a raise to the lips, a held sip with the
    /// bottle tipped up, and a lazy lower. Every duration comes from the
    /// per-patron seed so the crowd never sips in unison. Pure and
    /// EditMode-testable.
    /// </summary>
    public sealed class BarPatronDrinkTimeline
    {
        public const float RaiseSeconds = 0.85f;
        public const float LowerSeconds = 0.95f;
        public const float SipTiltRiseSeconds = 0.30f;
        public const float SipTiltFallSeconds = 0.35f;
        public const float MinimumRestSeconds = 3.5f;
        public const float MaximumRestSeconds = 9.5f;
        public const float MinimumSipSeconds = 1.10f;
        public const float MaximumSipSeconds = 2.20f;
        public const float MinimumInitialStaggerSeconds = 0.4f;
        public const float MaximumInitialStaggerSeconds = 6.0f;
        public const float GulpDelaySeconds = 0.45f;
        public const float GulpChance = 0.30f;

        private readonly System.Random random;
        private float phaseElapsed;
        private float phaseDuration;
        private bool sipHasGulp;
        private bool gulpConsumed;

        public BarPatronDrinkTimeline(int seed)
        {
            random = new System.Random(seed);
            // The first rest is the crowd stagger: nobody starts their
            // evening on the same beat.
            phaseDuration = Mathf.Lerp(
                MinimumInitialStaggerSeconds,
                MaximumInitialStaggerSeconds,
                NextUnit());
        }

        public BarPatronDrinkPhase Phase { get; private set; } =
            BarPatronDrinkPhase.Rest;
        public float PhaseElapsed => phaseElapsed;
        public float PhaseDuration => phaseDuration;

        /// <summary>0..1 blend of the procedural drinking arm.</summary>
        public float ArmWeight
        {
            get
            {
                switch (Phase)
                {
                    case BarPatronDrinkPhase.Raise:
                        return SmoothStep01(
                            phaseElapsed / RaiseSeconds);
                    case BarPatronDrinkPhase.Sip:
                        return 1f;
                    case BarPatronDrinkPhase.Lower:
                        return 1f - SmoothStep01(
                            phaseElapsed / LowerSeconds);
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>
        /// 0..1 bottle-up tip inside the sip: rises at the lips, holds,
        /// eases off before the arm lowers. Zero in every other phase.
        /// </summary>
        public float SipTilt
        {
            get
            {
                if (Phase != BarPatronDrinkPhase.Sip)
                {
                    return 0f;
                }

                float rise = Mathf.Clamp01(
                    phaseElapsed / SipTiltRiseSeconds);
                float fall = Mathf.Clamp01(
                    (phaseDuration - phaseElapsed) /
                    SipTiltFallSeconds);
                return SmoothStep01(Mathf.Min(rise, fall));
            }
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime));
            }

            phaseElapsed += Mathf.Max(0f, deltaTime);
            if (phaseElapsed < phaseDuration)
            {
                return;
            }

            switch (Phase)
            {
                case BarPatronDrinkPhase.Rest:
                    SetPhase(
                        BarPatronDrinkPhase.Raise,
                        RaiseSeconds);
                    break;
                case BarPatronDrinkPhase.Raise:
                    SetPhase(
                        BarPatronDrinkPhase.Sip,
                        Mathf.Lerp(
                            MinimumSipSeconds,
                            MaximumSipSeconds,
                            NextUnit()));
                    sipHasGulp = NextUnit() < GulpChance;
                    gulpConsumed = false;
                    break;
                case BarPatronDrinkPhase.Sip:
                    SetPhase(
                        BarPatronDrinkPhase.Lower,
                        LowerSeconds);
                    break;
                case BarPatronDrinkPhase.Lower:
                    SetPhase(
                        BarPatronDrinkPhase.Rest,
                        Mathf.Lerp(
                            MinimumRestSeconds,
                            MaximumRestSeconds,
                            NextUnit()));
                    break;
            }
        }

        /// <summary>
        /// One-shot: true at most once per sip, only for the seeded
        /// minority of sips that carry an audible gulp — a bar of six
        /// guests must murmur, not gurgle in chorus.
        /// </summary>
        public bool ConsumeGulpCue()
        {
            if (Phase != BarPatronDrinkPhase.Sip ||
                !sipHasGulp ||
                gulpConsumed ||
                phaseElapsed < GulpDelaySeconds)
            {
                return false;
            }

            gulpConsumed = true;
            return true;
        }

        private void SetPhase(
            BarPatronDrinkPhase phase,
            float duration)
        {
            Phase = phase;
            phaseDuration = duration;
            phaseElapsed = 0f;
        }

        private float NextUnit()
        {
            return (float)random.NextDouble();
        }

        private static float SmoothStep01(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * (3f - 2f * clamped);
        }
    }

    /// <summary>
    /// Procedural additive drinking arm atop the patron's authored
    /// Idle/Sit loop: every LateUpdate it captures the animated
    /// right-arm pose, CCD-steers the held bottle's mouth to the
    /// SOCKET_Mouth anchor, tips the bottle up during the sip with a
    /// small head-back counter-tilt, then slerps back by the timeline
    /// weight — the teeth-brushing/bus-driver idiom; the recorded
    /// standard exception lives in ai/architecture-notes.md.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed class BarPatronDrinkingArmPose : MonoBehaviour
    {
        public const float BottleTiltDegrees = 38f;
        public const float HeadTiltDegrees = 7f;
        private const int SolveIterations = 3;

        private BarPatronDrinkTimeline timeline;
        private Transform upperArm;
        private Transform forearm;
        private Transform head;
        private Transform mouthSocket;
        private Transform bottleRoot;
        private Transform bottleMouth;
        private Quaternion bottleBaseLocalRotation;
        private bool isInitialized;

        public BarPatronDrinkTimeline Timeline => timeline;
        public Transform BottleRoot => bottleRoot;
        public Transform BottleMouth => bottleMouth;

        public void Initialize(
            BarPatronDrinkTimeline drinkTimeline,
            Transform rightUpperArm,
            Transform rightForearm,
            Transform headBone,
            Transform mouthAnchor,
            Transform heldBottleRoot,
            Transform heldBottleMouth)
        {
            if (isInitialized)
            {
                throw new InvalidOperationException(
                    "The patron drinking arm is already initialized.");
            }

            timeline = drinkTimeline ??
                throw new ArgumentNullException(
                    nameof(drinkTimeline));
            upperArm = rightUpperArm != null
                ? rightUpperArm
                : throw new ArgumentNullException(
                    nameof(rightUpperArm));
            forearm = rightForearm != null
                ? rightForearm
                : throw new ArgumentNullException(
                    nameof(rightForearm));
            head = headBone != null
                ? headBone
                : throw new ArgumentNullException(nameof(headBone));
            mouthSocket = mouthAnchor != null
                ? mouthAnchor
                : throw new ArgumentNullException(
                    nameof(mouthAnchor));
            bottleRoot = heldBottleRoot != null
                ? heldBottleRoot
                : throw new ArgumentNullException(
                    nameof(heldBottleRoot));
            bottleMouth = heldBottleMouth != null
                ? heldBottleMouth
                : throw new ArgumentNullException(
                    nameof(heldBottleMouth));
            bottleBaseLocalRotation = bottleRoot.localRotation;
            isInitialized = true;
        }

        private void LateUpdate()
        {
            if (!isInitialized)
            {
                return;
            }

            timeline.Advance(Time.deltaTime);
            float weight = timeline.ArmWeight;
            float tilt = timeline.SipTilt;
            bottleRoot.localRotation = bottleBaseLocalRotation *
                Quaternion.Euler(-BottleTiltDegrees * tilt, 0f, 0f);
            if (weight <= 0.0001f)
            {
                return;
            }

            Vector3 target = mouthSocket.position;
            Quaternion upperBase = upperArm.rotation;
            Quaternion forearmBase = forearm.rotation;
            Quaternion headBase = head.rotation;
            for (int iteration = 0;
                 iteration < SolveIterations;
                 iteration++)
            {
                RotateTowards(forearm, bottleMouth, target);
                RotateTowards(upperArm, bottleMouth, target);
            }

            float clamped = Mathf.Clamp01(weight);
            upperArm.rotation = Quaternion.Slerp(
                upperBase,
                upperArm.rotation,
                clamped);
            forearm.rotation = Quaternion.Slerp(
                forearmBase,
                forearm.rotation,
                clamped);
            head.rotation = Quaternion.Slerp(
                headBase,
                Quaternion.AngleAxis(
                    -HeadTiltDegrees * tilt,
                    transform.right) *
                headBase,
                clamped);

            if (timeline.ConsumeGulpCue())
            {
                RetroAudioService.EnsureInstalled()?.TryPlay(
                    RetroSfxId.DrinkGulp,
                    mouthSocket.position);
            }
        }

        private static void RotateTowards(
            Transform joint,
            Transform end,
            Vector3 target)
        {
            Vector3 toEnd = end.position - joint.position;
            Vector3 toTarget = target - joint.position;
            if (toEnd.sqrMagnitude < 0.000001f ||
                toTarget.sqrMagnitude < 0.000001f)
            {
                return;
            }

            joint.rotation = Quaternion.FromToRotation(
                    toEnd,
                    toTarget) *
                joint.rotation;
        }
    }
}
