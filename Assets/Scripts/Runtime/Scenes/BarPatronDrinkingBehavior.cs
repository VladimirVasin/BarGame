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
    /// bottle turned toward the lips, and a lazy lower. Every duration comes
    /// from the per-patron seed so the crowd never sips in unison. Pure and
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

        /// <summary>
        /// Absolute phase in the cafe patron's authored Drink clip. The
        /// cadence keeps its long random rests, while each active beat uses
        /// the same raise/sip/lower landmarks as the Mountain Road cafe.
        /// </summary>
        public float AuthoredClipNormalizedTime
        {
            get
            {
                switch (Phase)
                {
                    case BarPatronDrinkPhase.Raise:
                        return Mathf.Lerp(
                            0f,
                            0.48f,
                            Mathf.Clamp01(
                                phaseElapsed / RaiseSeconds));
                    case BarPatronDrinkPhase.Sip:
                        return Mathf.Lerp(
                            0.48f,
                            0.62f,
                            Mathf.Clamp01(
                                phaseElapsed /
                                Mathf.Max(0.0001f, phaseDuration)));
                    case BarPatronDrinkPhase.Lower:
                        return Mathf.Lerp(
                            0.62f,
                            1f,
                            Mathf.Clamp01(
                                phaseElapsed / LowerSeconds));
                    default:
                        return 0f;
                }
            }
        }

        public bool IsActive => Phase != BarPatronDrinkPhase.Rest;

        /// <summary>
        /// Cafe-authored vessel-tip envelope: lift first, tip over
        /// .34-.48, hold through .62, and return by .76.
        /// </summary>
        public float VesselTipWeight
        {
            get
            {
                if (!IsActive)
                {
                    return 0f;
                }

                float normalized = AuthoredClipNormalizedTime;
                float rise = Mathf.InverseLerp(0.34f, 0.48f, normalized);
                float fall = 1f -
                    Mathf.InverseLerp(0.62f, 0.76f, normalized);
                return SmoothStep01(Mathf.Min(rise, fall));
            }
        }

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
    /// Bottle presentation shared by counter and table patrons. Counter
    /// patrons sample the cafe's authored full-body Drink clip through the
    /// existing city graph. Table patrons keep planted feet, lean toward the
    /// real tabletop and use a bounded arm solve. In both cases the coffee
    /// action receives a bottle-specific sip overlay: torso and head lean
    /// back, the bottle turns horizontally toward the mouth, and its authored
    /// neck anchor meets the mouth instead of inheriting arbitrary hand axes.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed class BarPatronDrinkingArmPose : MonoBehaviour
    {
        public const float BottleLipContactTolerance = 0.015f;
        public const float BottleGripContactTolerance = 0.04f;
        public const float TableSupportContactTolerance = 0.04f;
        public const float MinimumSipTorsoLeanBackDegrees = 30f;
        public const float MinimumSipHeadLeanBackDegrees = 17f;
        public const float HeadTiltDegrees = 7f;
        public const float TableSpineLeanDegrees = 9f;
        public const float TableChestLeanDegrees = 6f;
        public const float SipSpineLeanBackDegrees = 18f;
        public const float SipChestLeanBackDegrees = 14f;
        public const float SipHeadLeanBackDegrees = 12f;
        private const int SolveIterations = 6;

        private BarPatronDrinkTimeline timeline;
        private CityPedestrianPresentation presentation;
        private AnimationClip authoredDrinkClip;
        private Transform ownerRoot;
        private Transform rightUpperArm;
        private Transform rightForearm;
        private Transform rightHandSocket;
        private Transform leftClavicle;
        private Transform leftUpperArm;
        private Transform leftForearm;
        private Transform leftHandSocket;
        private Transform spine;
        private Transform chest;
        private Transform head;
        private Transform mouthSocket;
        private Transform bottleRoot;
        private Transform bottleMouth;
        private float bottleGripToLipDistance;
        private Vector3 tableSupportPoint;
        private Quaternion rightUpperBaseLocalRotation;
        private Quaternion rightForearmBaseLocalRotation;
        private Quaternion leftClavicleBaseLocalRotation;
        private Quaternion leftUpperBaseLocalRotation;
        private Quaternion leftForearmBaseLocalRotation;
        private Quaternion spineBaseLocalRotation;
        private Quaternion chestBaseLocalRotation;
        private Quaternion headBaseLocalRotation;
        private float measuredTorsoLeanBackDegrees;
        private float measuredHeadLeanBackDegrees;
        private bool tableLean;
        private bool actionWasActive;
        private bool isInitialized;

        public BarPatronDrinkTimeline Timeline => timeline;
        public Transform BottleRoot => bottleRoot;
        public Transform BottleMouth => bottleMouth;
        public bool IsTableLean => tableLean;
        public Vector3 TableSupportPoint => tableSupportPoint;
        public AnimationClip AuthoredDrinkClip => authoredDrinkClip;
        public float BottleMouthDistance =>
            bottleMouth != null && mouthSocket != null
                ? Vector3.Distance(
                    bottleMouth.position,
                    mouthSocket.position)
                : float.PositiveInfinity;
        public float BottleGripError =>
            bottleRoot != null && rightHandSocket != null
                ? Vector3.Distance(
                    bottleRoot.position,
                    rightHandSocket.position)
                : float.PositiveInfinity;
        public float BottleSipAxisErrorDegrees =>
            bottleRoot != null && ownerRoot != null
                ? Vector3.Angle(
                    bottleRoot.up,
                    ResolveSipBottleUp())
                : float.PositiveInfinity;
        public float BottleHorizontalErrorDegrees =>
            bottleRoot != null && ownerRoot != null
                ? Mathf.Abs(
                    90f - Vector3.Angle(
                        ownerRoot.up,
                        bottleRoot.up))
                : float.PositiveInfinity;
        public float MeasuredTorsoLeanBackDegrees =>
            measuredTorsoLeanBackDegrees;
        public float MeasuredHeadLeanBackDegrees =>
            measuredHeadLeanBackDegrees;
        public float TableSupportError =>
            tableLean && leftHandSocket != null
                ? Vector3.Distance(
                    leftHandSocket.position,
                    tableSupportPoint)
                : 0f;

        public void InitializeCounter(
            BarPatronDrinkTimeline drinkTimeline,
            CityPedestrianPresentation pedestrianPresentation,
            AnimationClip drinkClip,
            Transform patronRoot,
            Transform rightArm,
            Transform rightLowerArm,
            Transform handSocket,
            Transform spineBone,
            Transform chestBone,
            Transform headBone,
            Transform mouthAnchor,
            Transform heldBottleRoot,
            Transform heldBottleMouth,
            float gripToLipDistance)
        {
            InitializeCommon(
                drinkTimeline,
                patronRoot,
                handSocket,
                mouthAnchor,
                heldBottleRoot,
                heldBottleMouth,
                gripToLipDistance);
            rightUpperArm = Require(rightArm, nameof(rightArm));
            rightForearm = Require(
                rightLowerArm,
                nameof(rightLowerArm));
            spine = Require(spineBone, nameof(spineBone));
            chest = Require(chestBone, nameof(chestBone));
            head = Require(headBone, nameof(headBone));
            presentation = pedestrianPresentation != null
                ? pedestrianPresentation
                : throw new ArgumentNullException(
                    nameof(pedestrianPresentation));
            authoredDrinkClip = drinkClip != null
                ? drinkClip
                : throw new ArgumentNullException(nameof(drinkClip));
        }

        public void InitializeTable(
            BarPatronDrinkTimeline drinkTimeline,
            Transform patronRoot,
            Transform rightArm,
            Transform rightLowerArm,
            Transform handSocket,
            Transform leftShoulder,
            Transform leftArm,
            Transform leftLowerArm,
            Transform supportSocket,
            Transform spineBone,
            Transform chestBone,
            Transform headBone,
            Transform mouthAnchor,
            Transform heldBottleRoot,
            Transform heldBottleMouth,
            float gripToLipDistance,
            Vector3 supportPoint)
        {
            InitializeCommon(
                drinkTimeline,
                patronRoot,
                handSocket,
                mouthAnchor,
                heldBottleRoot,
                heldBottleMouth,
                gripToLipDistance);
            rightUpperArm = Require(rightArm, nameof(rightArm));
            rightForearm = Require(
                rightLowerArm,
                nameof(rightLowerArm));
            leftClavicle = Require(
                leftShoulder,
                nameof(leftShoulder));
            leftUpperArm = Require(leftArm, nameof(leftArm));
            leftForearm = Require(
                leftLowerArm,
                nameof(leftLowerArm));
            leftHandSocket = Require(
                supportSocket,
                nameof(supportSocket));
            spine = Require(spineBone, nameof(spineBone));
            chest = Require(chestBone, nameof(chestBone));
            head = Require(headBone, nameof(headBone));
            tableSupportPoint = supportPoint;
            rightUpperBaseLocalRotation = rightUpperArm.localRotation;
            rightForearmBaseLocalRotation = rightForearm.localRotation;
            leftClavicleBaseLocalRotation = leftClavicle.localRotation;
            leftUpperBaseLocalRotation = leftUpperArm.localRotation;
            leftForearmBaseLocalRotation = leftForearm.localRotation;
            spineBaseLocalRotation = spine.localRotation;
            chestBaseLocalRotation = chest.localRotation;
            headBaseLocalRotation = head.localRotation;
            tableLean = true;
        }

        private void Update()
        {
            if (!isInitialized)
            {
                return;
            }

            measuredTorsoLeanBackDegrees = 0f;
            measuredHeadLeanBackDegrees = 0f;
            timeline.Advance(Time.deltaTime);
            if (tableLean)
            {
                ApplyTablePose();
            }
            else
            {
                ApplyCounterPose();
            }

            PlaceBottle();

            if (timeline.ConsumeGulpCue())
            {
                RetroAudioService.EnsureInstalled()?.TryPlay(
                    RetroSfxId.DrinkGulp,
                    mouthSocket.position);
            }
        }

        private void ApplyCounterPose()
        {
            if (!timeline.IsActive)
            {
                if (actionWasActive)
                {
                    presentation.ClearAuthoredAction();
                    actionWasActive = false;
                }

                return;
            }

            float normalized = timeline.AuthoredClipNormalizedTime;
            float weight = ResolveAuthoredActionWeight(
                normalized,
                authoredDrinkClip.length);
            actionWasActive = presentation.ApplyAuthoredAction(
                authoredDrinkClip,
                normalized,
                weight);
            if (!actionWasActive)
            {
                return;
            }

            ApplyBottleSipLean();
            SolveRightArmTowards(
                ResolveSipBottleGrip(),
                timeline.VesselTipWeight);
        }

        private void ApplyTablePose()
        {
            // Some shared idle clips leave upper-body bones unkeyed. Reset the
            // authored table pose explicitly before adding the lean, otherwise
            // these world-space rotations accumulate once per frame and the
            // supporting hand eventually spirals away from the tabletop.
            spine.localRotation = spineBaseLocalRotation;
            chest.localRotation = chestBaseLocalRotation;
            head.localRotation = headBaseLocalRotation;
            leftClavicle.localRotation = leftClavicleBaseLocalRotation;
            leftUpperArm.localRotation = leftUpperBaseLocalRotation;
            leftForearm.localRotation = leftForearmBaseLocalRotation;
            rightUpperArm.localRotation = rightUpperBaseLocalRotation;
            rightForearm.localRotation = rightForearmBaseLocalRotation;

            Vector3 leanAxis = ownerRoot.right;
            spine.rotation = Quaternion.AngleAxis(
                    TableSpineLeanDegrees,
                    leanAxis) *
                spine.rotation;
            chest.rotation = Quaternion.AngleAxis(
                    TableChestLeanDegrees,
                    leanAxis) *
                chest.rotation;

            ApplyBottleSipLean();

            for (int iteration = 0;
                 iteration < SolveIterations;
                 iteration++)
            {
                RotateTowards(
                    leftForearm,
                    leftHandSocket,
                    tableSupportPoint);
                RotateTowards(
                    leftUpperArm,
                    leftHandSocket,
                    tableSupportPoint);
                RotateTowards(
                    leftClavicle,
                    leftHandSocket,
                    tableSupportPoint);
            }

            float weight = timeline.ArmWeight;
            if (weight <= 0.0001f)
            {
                return;
            }

            SolveRightArmTowards(
                ResolveSipBottleGrip(),
                weight);
        }

        private void ApplyBottleSipLean()
        {
            float weight = timeline.VesselTipWeight;
            if (weight <= 0.0001f)
            {
                return;
            }

            Vector3 axis = ownerRoot.right;
            Quaternion spineBefore = spine.rotation;
            Quaternion chestBefore = chest.rotation;
            Quaternion headBefore = head.rotation;
            spine.rotation = Quaternion.AngleAxis(
                    -SipSpineLeanBackDegrees * weight,
                    axis) *
                spine.rotation;
            chest.rotation = Quaternion.AngleAxis(
                    -SipChestLeanBackDegrees * weight,
                    axis) *
                chest.rotation;
            head.rotation = Quaternion.AngleAxis(
                    -(SipHeadLeanBackDegrees + HeadTiltDegrees) * weight,
                    axis) *
                head.rotation;
            measuredTorsoLeanBackDegrees =
                Quaternion.Angle(spineBefore, spine.rotation) +
                Quaternion.Angle(chestBefore, chest.rotation);
            measuredHeadLeanBackDegrees =
                Quaternion.Angle(headBefore, head.rotation);
        }

        private Vector3 ResolveSipBottleUp()
        {
            Vector3 horizontalIntoMouth = -Vector3.ProjectOnPlane(
                ownerRoot.forward,
                ownerRoot.up);
            return horizontalIntoMouth.sqrMagnitude > 0.000001f
                ? horizontalIntoMouth.normalized
                : -ownerRoot.forward;
        }

        private Vector3 ResolveSipBottleGrip()
        {
            return mouthSocket.position -
                   ResolveSipBottleUp() * bottleGripToLipDistance;
        }

        private void SolveRightArmTowards(Vector3 target, float weight)
        {
            if (rightUpperArm == null ||
                rightForearm == null ||
                weight <= 0.0001f)
            {
                return;
            }

            Quaternion upperBase = rightUpperArm.rotation;
            Quaternion forearmBase = rightForearm.rotation;
            for (int iteration = 0;
                 iteration < SolveIterations;
                 iteration++)
            {
                RotateTowards(
                    rightForearm,
                    rightHandSocket,
                    target);
                RotateTowards(
                    rightUpperArm,
                    rightHandSocket,
                    target);
            }

            float clamped = Mathf.Clamp01(weight);
            rightUpperArm.rotation = Quaternion.Slerp(
                upperBase,
                rightUpperArm.rotation,
                clamped);
            rightForearm.rotation = Quaternion.Slerp(
                forearmBase,
                rightForearm.rotation,
                clamped);
        }

        private void PlaceBottle()
        {
            Vector3 hand = rightHandSocket.position;
            Vector3 upright = ownerRoot.up;
            float tipWeight = timeline.VesselTipWeight;
            Quaternion bottleRotation = ResolveBottleRotation(
                ownerRoot.right,
                upright,
                ResolveSipBottleUp(),
                tipWeight);
            Vector3 bottleUp = bottleRotation * Vector3.up;

            // The cafe clip was authored around a short cup. During the sip,
            // solve backward from the lips along the horizontal drinking
            // axis: the bottle body stays outside the face while its real
            // neck anchor remains at the mouth. The correction shares the
            // tip envelope, so it fades cleanly into the ordinary hand pose.
            Vector3 lipSolvedGrip = mouthSocket.position -
                                    bottleUp * bottleGripToLipDistance;
            Vector3 bottlePosition = Vector3.Lerp(
                hand,
                lipSolvedGrip,
                tipWeight);
            bottleRoot.SetPositionAndRotation(
                bottlePosition,
                bottleRotation);
        }

        public static Quaternion ResolveBottleRotation(
            Vector3 ownerRight,
            Vector3 upright,
            Vector3 sipBottleUp,
            float tipWeight)
        {
            Vector3 bottleUp = Vector3.Slerp(
                    upright.normalized,
                    sipBottleUp.normalized,
                    Mathf.Clamp01(tipWeight))
                .normalized;
            Vector3 stableForward = Vector3.ProjectOnPlane(
                ownerRight,
                bottleUp);
            if (stableForward.sqrMagnitude < 0.000001f)
            {
                stableForward = Vector3.ProjectOnPlane(
                    upright,
                    bottleUp);
            }

            return Quaternion.LookRotation(
                stableForward.normalized,
                bottleUp);
        }

        /// <summary>
        /// The cafe's Drink action blends back to its base pose before the
        /// authored clip ends. Keeping the action at full weight through the
        /// last sample and clearing it on the following Rest frame produces a
        /// visible one-frame snap from Drink to Sit.
        /// </summary>
        public static float ResolveAuthoredActionWeight(
            float normalizedTime,
            float clipLengthSeconds)
        {
            if (float.IsNaN(normalizedTime) ||
                float.IsInfinity(normalizedTime) ||
                float.IsNaN(clipLengthSeconds) ||
                float.IsInfinity(clipLengthSeconds) ||
                clipLengthSeconds <= 0f)
            {
                return 0f;
            }

            float elapsed = Mathf.Clamp01(normalizedTime) *
                            clipLengthSeconds;
            float rise = Mathf.Clamp01(
                elapsed /
                MountainRoadCafeCastPresentation.BeatBlendInSeconds);
            float fall = Mathf.Clamp01(
                (clipLengthSeconds - elapsed) /
                MountainRoadCafeCastPresentation.BeatBlendOutSeconds);
            return Mathf.Min(rise, fall);
        }

        private void InitializeCommon(
            BarPatronDrinkTimeline drinkTimeline,
            Transform patronRoot,
            Transform handSocket,
            Transform mouthAnchor,
            Transform heldBottleRoot,
            Transform heldBottleMouth,
            float gripToLipDistance)
        {
            if (isInitialized)
            {
                throw new InvalidOperationException(
                    "The patron drinking pose is already initialized.");
            }

            if (float.IsNaN(gripToLipDistance) ||
                float.IsInfinity(gripToLipDistance) ||
                gripToLipDistance <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gripToLipDistance));
            }

            timeline = drinkTimeline ??
                throw new ArgumentNullException(nameof(drinkTimeline));
            ownerRoot = Require(patronRoot, nameof(patronRoot));
            rightHandSocket = Require(
                handSocket,
                nameof(handSocket));
            mouthSocket = Require(mouthAnchor, nameof(mouthAnchor));
            bottleRoot = Require(
                heldBottleRoot,
                nameof(heldBottleRoot));
            bottleMouth = Require(
                heldBottleMouth,
                nameof(heldBottleMouth));
            bottleGripToLipDistance = gripToLipDistance;
            isInitialized = true;
            PlaceBottle();
        }

        private static Transform Require(
            Transform value,
            string parameterName)
        {
            return value != null
                ? value
                : throw new ArgumentNullException(parameterName);
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

        private void OnDisable()
        {
            ReleaseAuthoredAction();
        }

        private void OnDestroy()
        {
            ReleaseAuthoredAction();
        }

        private void ReleaseAuthoredAction()
        {
            if (!tableLean && presentation != null && actionWasActive)
            {
                presentation.ClearAuthoredAction();
            }

            actionWasActive = false;
        }
    }
}
