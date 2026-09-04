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
    /// upright on the table, a raise to the lips, a held sip with the bottle
    /// turned toward the lips, and a lazy lower back to the tabletop. Every
    /// duration comes from the per-patron seed so the crowd never sips in
    /// unison. Pure and EditMode-testable.
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
        public const float RestHeadBlendSeconds = 0.40f;
        public const float MaximumRestHeadYawDegrees = 2.8f;
        public const float MaximumRestHeadPitchDegrees = 1.4f;

        private readonly System.Random random;
        private readonly float restHeadPhase;
        private float phaseElapsed;
        private float phaseDuration;
        private bool sipHasGulp;
        private bool gulpConsumed;

        public BarPatronDrinkTimeline(int seed)
        {
            random = new System.Random(seed);
            restHeadPhase = ResolveStablePhase(seed);
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
        public int CompletedDrinks { get; private set; }

        /// <summary>
        /// A small, non-referential head drift during the wait between sips.
        /// It fades to neutral at both phase edges so Raise and Lower retain
        /// their authored endpoint. X is pitch and Y is yaw, in degrees.
        /// </summary>
        public Vector2 RestHeadEulerDegrees
        {
            get
            {
                if (Phase != BarPatronDrinkPhase.Rest ||
                    phaseDuration <= 0f)
                {
                    return Vector2.zero;
                }

                float blendIn = SmoothStep01(
                    phaseElapsed / RestHeadBlendSeconds);
                float blendOut = SmoothStep01(
                    (phaseDuration - phaseElapsed) /
                    RestHeadBlendSeconds);
                float weight = Mathf.Min(blendIn, blendOut);
                float primary = phaseElapsed + restHeadPhase;
                float yaw =
                    (Mathf.Sin(primary * 1.07f) * 2.15f +
                     Mathf.Sin(primary * 0.43f + 1.2f) * 0.65f) *
                    weight;
                float pitch =
                    (Mathf.Sin(primary * 0.71f + 0.5f) * 1.05f +
                     Mathf.Sin(primary * 0.31f + 2.1f) * 0.35f) *
                    weight;
                return new Vector2(
                    Mathf.Clamp(
                        pitch,
                        -MaximumRestHeadPitchDegrees,
                        MaximumRestHeadPitchDegrees),
                    Mathf.Clamp(
                        yaw,
                        -MaximumRestHeadYawDegrees,
                        MaximumRestHeadYawDegrees));
            }
        }

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
                    CompletedDrinks++;
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

        private static float ResolveStablePhase(int seed)
        {
            unchecked
            {
                uint mixed = (uint)seed;
                mixed ^= mixed >> 16;
                mixed *= 0x7feb352du;
                mixed ^= mixed >> 15;
                mixed *= 0x846ca68bu;
                mixed ^= mixed >> 16;
                return (mixed & 0xffffu) /
                       65535f *
                       Mathf.PI *
                       2f;
            }
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
    /// real tabletop and use a bounded arm solve. Between sips both types put
    /// the bottle upright on their real surface, support the other hand and
    /// keep a faint non-referential head drift. In both cases the coffee action
    /// receives a bottle-specific sip overlay: torso and head lean back, the
    /// bottle turns horizontally toward the mouth, and its authored neck
    /// anchor meets the mouth instead of inheriting arbitrary hand axes.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed class BarPatronDrinkingArmPose : MonoBehaviour
    {
        public const float BottleLipContactTolerance = 0.015f;
        public const float BottleGripContactTolerance = 0.015f;
        public const float BottleHandSurfaceOffset = 0.06f;
        public const float MinimumBottleHandRadialClearance = 0.055f;
        public const float MinimumRightHandSideAlignment = 0.95f;
        public const float BottleHandOrientationToleranceDegrees = 1f;
        public const float TableSupportContactTolerance = 0.04f;
        public const float BottleSurfaceContactTolerance = 0.025f;
        public const float BottleRestUprightToleranceDegrees = 2f;
        public const float MinimumSipTorsoLeanBackDegrees = 30f;
        public const float MinimumSipHeadLeanBackDegrees = 17f;
        public const float HeadTiltDegrees = 7f;
        public const float TableSpineLeanDegrees = 9f;
        public const float TableChestLeanDegrees = 6f;
        public const float CounterSpineLeanDegrees = 16f;
        public const float CounterChestLeanDegrees = 12f;
        public const float CounterHeadCompensationDegrees = 18f;
        public const float SipSpineLeanBackDegrees = 18f;
        public const float SipChestLeanBackDegrees = 14f;
        public const float SipHeadLeanBackDegrees = 12f;
        private const int SolveIterations = 6;

        private BarPatronDrinkTimeline timeline;
        private CityPedestrianPresentation presentation;
        private AnimationClip authoredDrinkClip;
        private Transform ownerRoot;
        private Transform rightClavicle;
        private Transform rightUpperArm;
        private Transform rightForearm;
        private Transform rightHand;
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
        private float bottleGripToBaseDistance;
        private Vector3 bottleRestPoint;
        private Vector3 tableSupportPoint;
        private Quaternion rightClavicleBaseLocalRotation;
        private Quaternion rightUpperBaseLocalRotation;
        private Quaternion rightForearmBaseLocalRotation;
        private SeatedArmHandAttachment rightHandAttachment;
        private Quaternion leftClavicleBaseLocalRotation;
        private Quaternion leftUpperBaseLocalRotation;
        private Quaternion leftForearmBaseLocalRotation;
        private Quaternion spineBaseLocalRotation;
        private Quaternion chestBaseLocalRotation;
        private Quaternion headBaseLocalRotation;
        private float measuredTorsoLeanBackDegrees;
        private float measuredHeadLeanBackDegrees;
        private float restHeadMotionDegrees;
        private bool tableLean;
        private bool actionWasActive;
        private bool isInitialized;

        public BarPatronDrinkTimeline Timeline => timeline;
        public Transform BottleRoot => bottleRoot;
        public Transform BottleMouth => bottleMouth;
        public bool IsTableLean => tableLean;
        public Vector3 TableSupportPoint => tableSupportPoint;
        public Vector3 BottleRestPoint => bottleRestPoint;
        public Vector3 BottleBasePosition =>
            bottleRoot != null
                ? bottleRoot.position -
                  bottleRoot.up * bottleGripToBaseDistance
                : Vector3.positiveInfinity;
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
                    ResolveBottleHandContact(
                        bottleRoot.position,
                        bottleRoot.up),
                    rightHandSocket.position)
                : float.PositiveInfinity;
        public float BottleHandRadialClearance =>
            bottleRoot != null && rightHandSocket != null
                ? Vector3.ProjectOnPlane(
                    rightHandSocket.position - bottleRoot.position,
                    bottleRoot.up).magnitude
                : 0f;
        public float BottleHandRightSideAlignment
        {
            get
            {
                if (bottleRoot == null ||
                    rightHandSocket == null ||
                    ownerRoot == null)
                {
                    return -1f;
                }

                Vector3 radial = Vector3.ProjectOnPlane(
                    rightHandSocket.position - bottleRoot.position,
                    bottleRoot.up);
                Vector3 rightSide = Vector3.ProjectOnPlane(
                    ownerRoot.right,
                    bottleRoot.up);
                if (radial.sqrMagnitude < 0.000001f ||
                    rightSide.sqrMagnitude < 0.000001f)
                {
                    return -1f;
                }

                return Vector3.Dot(
                    radial.normalized,
                    rightSide.normalized);
            }
        }
        public float BottleHandOrientationErrorDegrees =>
            bottleRoot != null && rightHandSocket != null
                ? Quaternion.Angle(
                    rightHandSocket.rotation,
                    ResolveRightBottleSocketRotation(
                        ownerRoot.right,
                        bottleRoot.up))
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
        public float RestHeadMotionDegrees => restHeadMotionDegrees;
        public float TableSupportError =>
            leftHandSocket != null
                ? Vector3.Distance(
                    leftHandSocket.position,
                    tableSupportPoint)
                : float.PositiveInfinity;
        public float BottleSurfaceContactError =>
            bottleRoot != null
                ? Vector3.Distance(
                    BottleBasePosition,
                    bottleRestPoint)
                : float.PositiveInfinity;
        public float BottleRestUprightErrorDegrees =>
            bottleRoot != null && ownerRoot != null
                ? Vector3.Angle(bottleRoot.up, ownerRoot.up)
                : float.PositiveInfinity;

        public void InitializeCounter(
            BarPatronDrinkTimeline drinkTimeline,
            CityPedestrianPresentation pedestrianPresentation,
            AnimationClip drinkClip,
            Transform patronRoot,
            Transform rightShoulder,
            Transform rightArm,
            Transform rightLowerArm,
            Transform rightHandBone,
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
            float gripToBaseDistance,
            Vector3 restingBottlePoint,
            Vector3 supportPoint)
        {
            InitializeCommon(
                drinkTimeline,
                patronRoot,
                rightHandBone,
                handSocket,
                mouthAnchor,
                heldBottleRoot,
                heldBottleMouth,
                gripToLipDistance,
                gripToBaseDistance,
                restingBottlePoint);
            rightClavicle = Require(
                rightShoulder,
                nameof(rightShoulder));
            rightUpperArm = Require(rightArm, nameof(rightArm));
            rightForearm = Require(
                rightLowerArm,
                nameof(rightLowerArm));
            ConfigureSurfaceSupport(
                leftShoulder,
                leftArm,
                leftLowerArm,
                supportSocket,
                supportPoint);
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
            CompleteInitialization(false);
        }

        public void InitializeTable(
            BarPatronDrinkTimeline drinkTimeline,
            Transform patronRoot,
            Transform rightShoulder,
            Transform rightArm,
            Transform rightLowerArm,
            Transform rightHandBone,
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
            float gripToBaseDistance,
            Vector3 restingBottlePoint,
            Vector3 supportPoint)
        {
            InitializeCommon(
                drinkTimeline,
                patronRoot,
                rightHandBone,
                handSocket,
                mouthAnchor,
                heldBottleRoot,
                heldBottleMouth,
                gripToLipDistance,
                gripToBaseDistance,
                restingBottlePoint);
            rightClavicle = Require(
                rightShoulder,
                nameof(rightShoulder));
            rightUpperArm = Require(rightArm, nameof(rightArm));
            rightForearm = Require(
                rightLowerArm,
                nameof(rightLowerArm));
            ConfigureSurfaceSupport(
                leftShoulder,
                leftArm,
                leftLowerArm,
                supportSocket,
                supportPoint);
            spine = Require(spineBone, nameof(spineBone));
            chest = Require(chestBone, nameof(chestBone));
            head = Require(headBone, nameof(headBone));
            CompleteInitialization(true);
        }

        private void Update()
        {
            if (!isInitialized)
            {
                return;
            }

            measuredTorsoLeanBackDegrees = 0f;
            measuredHeadLeanBackDegrees = 0f;
            restHeadMotionDegrees = 0f;
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

                ResetUpperBodyToBase();
                ApplySurfaceLean(
                    CounterSpineLeanDegrees,
                    CounterChestLeanDegrees);
                CompensateHeadLean(CounterHeadCompensationDegrees);
                ApplyRestHeadMotion();
                SolveLeftArmTowardsSupport();
                SolveRightHandBottleGrip();
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
            float restPoseWeight;
            if (!actionWasActive)
            {
                ResetUpperBodyToBase();
                restPoseWeight = 1f - timeline.ArmWeight;
            }
            else
            {
                // The action itself fades to zero at both clip boundaries.
                // Keep the tabletop rest lean under that fade so Rest ->
                // Raise and Lower -> Rest share the exact same endpoint.
                restPoseWeight = 1f - weight;
            }

            ApplySurfaceLean(
                CounterSpineLeanDegrees * restPoseWeight,
                CounterChestLeanDegrees * restPoseWeight);
            CompensateHeadLean(
                CounterHeadCompensationDegrees * restPoseWeight);
            ApplyBottleSipLean();
            SolveLeftArmTowardsSupport();
            SolveRightHandBottleGrip();
        }

        private void ApplyTablePose()
        {
            // Some shared idle clips leave upper-body bones unkeyed. Reset the
            // authored table pose explicitly before adding the lean, otherwise
            // these world-space rotations accumulate once per frame and the
            // supporting hand eventually spirals away from the tabletop.
            ResetUpperBodyToBase();
            ApplySurfaceLean(
                TableSpineLeanDegrees,
                TableChestLeanDegrees);
            ApplyRestHeadMotion();

            ApplyBottleSipLean();
            SolveLeftArmTowardsSupport();
            SolveRightHandBottleGrip();
        }

        private void ResetUpperBodyToBase()
        {
            spine.localRotation = spineBaseLocalRotation;
            chest.localRotation = chestBaseLocalRotation;
            head.localRotation = headBaseLocalRotation;
            leftClavicle.localRotation = leftClavicleBaseLocalRotation;
            leftUpperArm.localRotation = leftUpperBaseLocalRotation;
            leftForearm.localRotation = leftForearmBaseLocalRotation;
            rightClavicle.localRotation =
                rightClavicleBaseLocalRotation;
            rightUpperArm.localRotation = rightUpperBaseLocalRotation;
            rightForearm.localRotation = rightForearmBaseLocalRotation;
        }

        private void ApplySurfaceLean(
            float spineDegrees,
            float chestDegrees)
        {
            Vector3 leanAxis = ownerRoot.right;
            spine.rotation = Quaternion.AngleAxis(
                    spineDegrees,
                    leanAxis) *
                spine.rotation;
            chest.rotation = Quaternion.AngleAxis(
                    chestDegrees,
                    leanAxis) *
                chest.rotation;
        }

        private void ApplyRestHeadMotion()
        {
            Vector2 euler = timeline.RestHeadEulerDegrees;
            restHeadMotionDegrees = euler.magnitude;
            if (restHeadMotionDegrees <= 0.0001f)
            {
                return;
            }

            head.rotation = Quaternion.AngleAxis(
                    euler.y,
                    ownerRoot.up) *
                Quaternion.AngleAxis(
                    euler.x,
                    ownerRoot.right) *
                head.rotation;
        }

        private void CompensateHeadLean(float degrees)
        {
            head.rotation = Quaternion.AngleAxis(
                    -degrees,
                    ownerRoot.right) *
                head.rotation;
        }

        private void SolveLeftArmTowardsSupport()
        {
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

        private Vector3 ResolveRestingBottleGrip()
        {
            return bottleRestPoint +
                   ownerRoot.up * bottleGripToBaseDistance;
        }

        private Vector3 ResolveBottleHandContact(
            Vector3 bottleGrip,
            Vector3 bottleUp)
        {
            Vector3 radial = Vector3.ProjectOnPlane(
                ownerRoot.right,
                bottleUp);
            if (radial.sqrMagnitude < 0.000001f)
            {
                radial = Vector3.ProjectOnPlane(
                    -ownerRoot.forward,
                    bottleUp);
            }

            return bottleGrip +
                   radial.normalized * BottleHandSurfaceOffset;
        }

        private void ResolveBottlePose(
            out Vector3 bottlePosition,
            out Quaternion bottleRotation)
        {
            float tipWeight = timeline.VesselTipWeight;
            bottleRotation = ResolveBottleRotation(
                ownerRoot.right,
                ownerRoot.up,
                ResolveSipBottleUp(),
                tipWeight);
            Vector3 bottleUp = bottleRotation * Vector3.up;
            Vector3 lipSolvedGrip = mouthSocket.position -
                                    bottleUp * bottleGripToLipDistance;
            bottlePosition = Vector3.Lerp(
                ResolveRestingBottleGrip(),
                lipSolvedGrip,
                timeline.ArmWeight);
        }

        private void SolveRightHandBottleGrip()
        {
            if (rightClavicle == null ||
                rightUpperArm == null ||
                rightForearm == null ||
                rightHand == null)
            {
                return;
            }

            ResolveBottlePose(
                out Vector3 bottlePosition,
                out Quaternion bottleRotation);
            Vector3 bottleUp = bottleRotation * Vector3.up;
            Vector3 socketPosition = ResolveBottleHandContact(
                bottlePosition,
                bottleUp);
            Quaternion socketRotation =
                ResolveRightBottleSocketRotation(
                    ownerRoot.right,
                    bottleUp);
            Quaternion handRotation = socketRotation *
                Quaternion.Inverse(
                    rightHandAttachment.SocketRotationInHand);
            Vector3 handPosition = socketPosition -
                handRotation *
                rightHandAttachment.SocketPositionInHand;

            for (int iteration = 0;
                 iteration < SolveIterations;
                 iteration++)
            {
                RotateTowards(
                    rightForearm,
                    rightHand,
                    handPosition);
                RotateTowards(
                    rightUpperArm,
                    rightHand,
                    handPosition);
                RotateTowards(
                    rightClavicle,
                    rightHand,
                    handPosition);
            }

            // The point solve above cannot choose wrist roll. Write the full
            // right-hand frame after it: socket Y follows the bottle down,
            // socket X points inward, and the model's bind-space hand/socket
            // relation keeps the thumb on the anatomical right-hand side.
            rightHand.rotation = handRotation;
        }

        private void PlaceBottle()
        {
            ResolveBottlePose(
                out Vector3 bottlePosition,
                out Quaternion bottleRotation);
            bottleRoot.SetPositionAndRotation(
                bottlePosition,
                bottleRotation);
        }

        public static Quaternion ResolveRightBottleSocketRotation(
            Vector3 ownerRight,
            Vector3 bottleUp)
        {
            Vector3 normalizedUp = bottleUp.normalized;
            Vector3 radial = Vector3.ProjectOnPlane(
                ownerRight,
                normalizedUp);
            if (radial.sqrMagnitude < 0.000001f)
            {
                radial = Vector3.Cross(normalizedUp, Vector3.forward);
            }

            if (radial.sqrMagnitude < 0.000001f)
            {
                radial = Vector3.Cross(normalizedUp, Vector3.right);
            }

            radial.Normalize();
            Vector3 socketForward = Vector3.Cross(
                    radial,
                    normalizedUp)
                .normalized;
            return Quaternion.LookRotation(
                socketForward,
                -normalizedUp);
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
            Transform handBone,
            Transform handSocket,
            Transform mouthAnchor,
            Transform heldBottleRoot,
            Transform heldBottleMouth,
            float gripToLipDistance,
            float gripToBaseDistance,
            Vector3 restingBottlePoint)
        {
            if (isInitialized || timeline != null)
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

            if (float.IsNaN(gripToBaseDistance) ||
                float.IsInfinity(gripToBaseDistance) ||
                gripToBaseDistance <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gripToBaseDistance));
            }

            if (!IsFinite(restingBottlePoint))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(restingBottlePoint));
            }

            timeline = drinkTimeline ??
                throw new ArgumentNullException(nameof(drinkTimeline));
            ownerRoot = Require(patronRoot, nameof(patronRoot));
            rightHand = Require(handBone, nameof(handBone));
            rightHandSocket = Require(
                handSocket,
                nameof(handSocket));
            if (rightHandSocket.parent != rightHand)
            {
                throw new ArgumentException(
                    "The bottle socket must be a direct child of hand.R.",
                    nameof(handSocket));
            }

            rightHandAttachment = new SeatedArmHandAttachment(
                rightHand,
                rightHandSocket);
            mouthSocket = Require(mouthAnchor, nameof(mouthAnchor));
            bottleRoot = Require(
                heldBottleRoot,
                nameof(heldBottleRoot));
            bottleMouth = Require(
                heldBottleMouth,
                nameof(heldBottleMouth));
            bottleGripToLipDistance = gripToLipDistance;
            bottleGripToBaseDistance = gripToBaseDistance;
            bottleRestPoint = restingBottlePoint;
        }

        private void ConfigureSurfaceSupport(
            Transform leftShoulder,
            Transform leftArm,
            Transform leftLowerArm,
            Transform supportSocket,
            Vector3 supportPoint)
        {
            if (!IsFinite(supportPoint))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(supportPoint));
            }

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
            tableSupportPoint = supportPoint;
        }

        private void CompleteInitialization(bool usesTableLean)
        {
            rightClavicleBaseLocalRotation = rightClavicle.localRotation;
            rightUpperBaseLocalRotation = rightUpperArm.localRotation;
            rightForearmBaseLocalRotation = rightForearm.localRotation;
            leftClavicleBaseLocalRotation = leftClavicle.localRotation;
            leftUpperBaseLocalRotation = leftUpperArm.localRotation;
            leftForearmBaseLocalRotation = leftForearm.localRotation;
            spineBaseLocalRotation = spine.localRotation;
            chestBaseLocalRotation = chest.localRotation;
            headBaseLocalRotation = head.localRotation;
            tableLean = usesTableLean;
            isInitialized = true;
            if (tableLean)
            {
                ApplyTablePose();
            }
            else
            {
                ApplyCounterPose();
            }

            PlaceBottle();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
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
