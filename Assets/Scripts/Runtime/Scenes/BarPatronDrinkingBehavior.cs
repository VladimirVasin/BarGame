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
    /// Drink presentation shared by counter and table patrons. Counter
    /// patrons sample the cafe's authored full-body Drink clip through the
    /// existing city graph. Table patrons keep planted feet, lean toward the
    /// real tabletop and use a bounded arm solve. Between sips both types put
    /// the vessel upright on their real surface, support the other hand and
    /// keep a faint non-referential head drift. Low-sofa guests retain their
    /// seated graph beneath the procedural upper-body pose. The vessel's own
    /// grip, drinking edge and sip angle determine its final hand contact.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed partial class BarPatronDrinkingArmPose : MonoBehaviour
    {
        public const float BottleLipContactTolerance = 0.015f;
        public const float BottleGripContactTolerance = 0.015f;
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
        public const float MaximumKaraokeTorsoYawDegrees = 75f;
        private const int SolveIterations = 6;

        private BarPatronDrinkTimeline timeline;
        private CityPedestrianPresentation presentation;
        private Animator contactAnimator;
        private AnimatorCullingMode previousAnimatorCulling;
        private bool ownsAnimatorCulling;
        private AnimationClip authoredDrinkClip;
        private Transform ownerRoot;
        private Transform rightClavicle;
        private Transform rightUpperArm;
        private Transform rightForearm;
        private Transform rightHand;
        private Transform leftClavicle;
        private Transform leftUpperArm;
        private Transform leftForearm;
        private Transform leftHand;
        private Transform leftHandSocket;
        private Transform spine;
        private Transform chest;
        private Transform head;
        private Transform mouthSocket;
        private Transform bottleRoot;
        private Transform bottleMouth;
        private BarPatronDrinkProp drinkProp;
        private Vector3 gripLocalPosition;
        private Vector3 rimLocalPosition;
        private Vector3 baseLocalPosition;
        private Vector3 openingLocalAxis = Vector3.up;
        private Vector3 handleLocalAxis = Vector3.forward;
        private NpcHandGripKind gripKind;
        private float maximumSipTiltDegrees = 90f;
        private float sipTorsoLeanBackDegrees =
            SipSpineLeanBackDegrees + SipChestLeanBackDegrees;
        private float sipHeadLeanBackDegrees =
            SipHeadLeanBackDegrees + HeadTiltDegrees;
        private Vector3 bottleRestPoint;
        private Vector3 tableSupportPoint;
        private Quaternion rightClavicleBaseLocalRotation;
        private Quaternion rightUpperBaseLocalRotation;
        private Quaternion rightForearmBaseLocalRotation;
        private SeatedArmHandAttachment leftHandAttachment;
        private NpcHandPose handPose;
        private AnatomicalHandFrame rightAnatomicalHand;
        private Quaternion leftClavicleBaseLocalRotation;
        private Quaternion leftUpperBaseLocalRotation;
        private Quaternion leftForearmBaseLocalRotation;
        private Quaternion leftHandBaseLocalRotation;
        private Quaternion spineBaseLocalRotation;
        private Quaternion chestBaseLocalRotation;
        private Quaternion headBaseLocalRotation;
        private float measuredTorsoLeanBackDegrees;
        private float measuredHeadLeanBackDegrees;
        private float restHeadMotionDegrees;
        private bool tableLean;
        private bool seatedTable;
        private Renderer surfaceRenderer;
        private Bounds surfaceBounds;
        private bool actionWasActive;
        private bool isInitialized;
        private Transform conversationPartner;
        private readonly NpcAttentionHeadLayer conversationLook = new NpcAttentionHeadLayer();
        private bool conversationLookBound;
        private UnityEngine.Object karaokeFocusOwner;
        private Transform karaokeFocus;
        private float karaokeTorsoYaw, karaokePoseWeight;
        public float KaraokeTorsoYawDegrees => karaokeTorsoYaw;
        public float KaraokePoseWeight => karaokePoseWeight;
        public bool KaraokeHandsReleased => karaokePoseWeight > .95f;
        public bool CanConverse => isInitialized && isActiveAndEnabled && timeline != null &&
            timeline.Phase == BarPatronDrinkPhase.Rest &&
            (CounterRefill == null || !CounterRefill.IsWaitingForRefill && !CounterRefill.IsRefilling);

        public void SetConversationPartner(Transform partner)
        {
            conversationPartner = partner;
            if (partner != null || karaokeFocus != null || !conversationLookBound) return;
            conversationLook.Restore();
            conversationLook.Clear();
        }

        /// <summary>The audience borrows attention without joining a conversation or moving a seat.</summary>
        public bool TrySetKaraokeFocus(UnityEngine.Object owner, Transform focus)
        {
            if (owner == null || focus == null || !isInitialized || !isActiveAndEnabled ||
                karaokeFocusOwner != null && karaokeFocusOwner != owner) return false;
            karaokeFocusOwner = owner;
            karaokeFocus = focus;
            return true;
        }

        public bool IsKaraokeAttending(UnityEngine.Object owner) =>
            ReferenceEquals(karaokeFocusOwner, owner) && karaokeFocus != null && CanConverse &&
            conversationLookBound && conversationLook.Weight > .01f;

        public void ClearKaraokeFocus(UnityEngine.Object owner)
        {
            if (ReferenceEquals(karaokeFocusOwner, null) || !ReferenceEquals(karaokeFocusOwner, owner)) return;
            karaokeFocusOwner = null;
            karaokeFocus = null;
            if (conversationLookBound) { conversationLook.Restore(); conversationLook.Clear(); }
        }
        private bool gulpCuePending;

        public BarPatronDrinkTimeline Timeline => timeline;
        public Transform BottleRoot => bottleRoot;
        public Transform BottleMouth => bottleMouth;
        public BarPatronDrinkProp DrinkProp => drinkProp;
        public bool IsTableLean => tableLean;
        public bool IsSeatedTable => seatedTable;
        public Renderer SurfaceRenderer => surfaceRenderer;
        public Bounds SurfaceBounds => surfaceBounds;
        public NpcHandGripKind GripKind => gripKind;
        public float SipTiltDegrees => maximumSipTiltDegrees;
        public float ExpectedSipTorsoLeanBackDegrees => sipTorsoLeanBackDegrees;
        public float ExpectedSipHeadLeanBackDegrees => sipHeadLeanBackDegrees;
        public Vector3 TableSupportPoint => tableSupportPoint;
        public Vector3 BottleRestPoint => bottleRestPoint;
        public Vector3 BottleBasePosition =>
            bottleRoot != null
                ? bottleRoot.position +
                  bottleRoot.rotation * baseLocalPosition
                : Vector3.positiveInfinity;
        public Vector3 DrinkGripWorldPosition => bottleRoot != null
            ? bottleRoot.position + bottleRoot.rotation * gripLocalPosition
            : Vector3.positiveInfinity;
        public Vector3 DrinkOpeningDirection => bottleRoot != null
            ? bottleRoot.rotation * openingLocalAxis
            : Vector3.up;
        public AnimationClip AuthoredDrinkClip => authoredDrinkClip;
        public float BottleMouthDistance =>
            bottleMouth != null && mouthSocket != null
                ? Vector3.Distance(
                    bottleMouth.position,
                    mouthSocket.position)
                : float.PositiveInfinity;
        public float BottleGripError =>
            bottleRoot != null && handPose != null
                ? Vector3.Distance(
                    DrinkGripWorldPosition,
                    handPose.AnatomicalCylinderCentre(false))
                : float.PositiveInfinity;
        public float BottleHandOrientationErrorDegrees =>
            bottleRoot != null && handPose != null
                ? Vector3.Angle(handPose.AnatomicalPalmNormal(false),
                    -ResolveBottleHandRadial(bottleRoot.rotation))
                : float.PositiveInfinity;
        public NpcHandPose HandPose => handPose;
        public float BottleSipAxisErrorDegrees =>
            bottleRoot != null && ownerRoot != null
                ? Vector3.Angle(
                    DrinkOpeningDirection,
                    ResolveSipBottleUp())
                : float.PositiveInfinity;
        public float BottleHorizontalErrorDegrees =>
            bottleRoot != null && ownerRoot != null
                ? Mathf.Abs(
                    90f - Vector3.Angle(
                        ownerRoot.up,
                        DrinkOpeningDirection))
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
                ? Vector3.Angle(DrinkOpeningDirection, ownerRoot.up)
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
            Vector3 supportPoint,
            Renderer restingSurface = null,
            BarPatronDrinkProp drinkProp = null)
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
                restingBottlePoint,
                drinkProp);
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
            surfaceRenderer = restingSurface;
            surfaceBounds = restingSurface != null ? restingSurface.bounds : default;
            CompleteInitialization(false);
        }

        public void InitializeTable(
            BarPatronDrinkTimeline drinkTimeline,
            CityPedestrianPresentation pedestrianPresentation,
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
            Vector3 supportPoint,
            Renderer restingSurface = null,
            bool isSeatedTable = false,
            BarPatronDrinkProp drinkProp = null)
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
                restingBottlePoint,
                drinkProp);
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
                : throw new ArgumentNullException(nameof(pedestrianPresentation));
            surfaceRenderer = restingSurface;
            surfaceBounds = restingSurface != null ? restingSurface.bounds : default;
            seatedTable = isSeatedTable;
            CompleteInitialization(true);
        }

        private void OnPresentationAdvanced(float deltaTime)
        {
            if (!isInitialized || !isActiveAndEnabled)
            {
                return;
            }

            // A whole A→B exchange holds the existing rest pose, never an
            // interrupted sip or service request. No second animation graph.
            if ((conversationPartner != null || karaokeFocusOwner != null && karaokeFocus != null ||
                karaokePoseWeight > .0001f) && CanConverse) return;

            if (CounterRefill != null)
            {
                if (CounterRefill.isActiveAndEnabled) CounterRefill.Advance(deltaTime);
            }
            else timeline.Advance(deltaTime);
            gulpCuePending |= timeline.ConsumeGulpCue();
        }

        private void LateUpdate()
        {
            if (!isInitialized || !isActiveAndEnabled)
            {
                return;
            }

            // A manually evaluated Animator still owns its native transform
            // write stage after Update. Keep time on Advanced, but publish
            // these final contacts after that stage, as the bartender does.
            bool karaokeReady = karaokeFocusOwner != null && karaokeFocus != null && CanConverse;
            float deltaTime = GameTimeScaleRuntime.IsPaused ? 0f : Time.deltaTime;
            if (CanConverse)
            {
                karaokePoseWeight = Mathf.MoveTowards(karaokePoseWeight, karaokeReady ? 1f : 0f, deltaTime / .55f);
                float desired = 0f;
                if (karaokeReady)
                {
                    Vector3 delta = karaokeFocus.position - ownerRoot.position;
                    float yaw = Mathf.DeltaAngle(ownerRoot.eulerAngles.y, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
                    desired = Mathf.Clamp(yaw - Mathf.Clamp(yaw, -45f, 45f),
                        -MaximumKaraokeTorsoYawDegrees, MaximumKaraokeTorsoYawDegrees) * karaokePoseWeight;
                }
                karaokeTorsoYaw = Mathf.MoveTowards(karaokeTorsoYaw, desired,
                    deltaTime * 145f);
            }
            else { karaokeTorsoYaw = 0f; karaokePoseWeight = 0f; }
            ApplyFinalContacts(true);
            Transform attentionTarget = karaokeReady ? karaokeFocus : conversationPartner;
            if (attentionTarget != null && CanConverse)
            {
                if (!conversationLookBound)
                {
                    conversationLook.Bind(ownerRoot, head, presentation.Registry.Animator);
                    conversationLookBound = true;
                }
                Vector3 focus = attentionTarget.position;
                // The head layer still measures yaw from the immutable seat.
                // Subtract the already-applied torso turn to avoid double aim.
                if (karaokeReady) focus = head.position + Quaternion.AngleAxis(-karaokeTorsoYaw,
                    ownerRoot.up) * (focus - head.position);
                conversationLook.SetFocus(focus);
                conversationLook.Apply(GameTimeScaleRuntime.IsPaused ? 0f : Time.deltaTime);
            }
            else if (conversationLookBound) conversationLook.Clear();

            if (gulpCuePending)
            {
                gulpCuePending = false;
                RetroAudioService.EnsureInstalled()?.TryPlay(
                    RetroSfxId.DrinkGulp,
                    mouthSocket.position);
            }
        }

        /// <summary>
        /// Restores the same final contacts before a crowd owner transfers
        /// the live rig and vessel. Does not advance drinking, finger blends,
        /// speech attention or audio; Update may have just sampled the base
        /// graph since the preceding rendered contact pose.
        /// </summary>
        internal void SampleFinalPose()
        {
            // A service still owning this cup must finish before a transfer;
            // its support-return blend advances only in ordinary LateUpdate.
            if (CounterRefill != null && CounterRefill.IsRefilling) return;
            ApplyFinalContacts(false);
        }

        private void ApplyFinalContacts(bool advanceFingerBlend)
        {
            if (!isInitialized) return;
            measuredTorsoLeanBackDegrees = 0f;
            measuredHeadLeanBackDegrees = 0f;
            restHeadMotionDegrees = 0f;
            if (conversationLookBound) conversationLook.Restore();
            if (tableLean) ApplyTablePose();
            else ApplyCounterPose();
            ApplyKaraokeRestPose();
            if (CounterRefill == null || !CounterRefill.IsRefilling) PlaceBottle();
            ApplyFingerGrip(advanceFingerBlend);
            ApplyCounterAttentionWave();
        }

        private void ApplyKaraokeRestPose()
        {
            if (!CanConverse || karaokePoseWeight <= .0001f) return;
            Pose leftContact = new Pose(leftHand.position, leftHand.rotation);
            Pose rightContact = new Pose(rightHand.position, rightHand.rotation);
            spine.rotation = Quaternion.AngleAxis(karaokeTorsoYaw * .4f, ownerRoot.up) * spine.rotation;
            chest.rotation = Quaternion.AngleAxis(karaokeTorsoYaw * .6f, ownerRoot.up) * chest.rotation;
            // The cup is already upright on its authored table point. Release
            // both hands toward the lap/thighs rather than stretching a planted
            // arm across a turned torso. Pelvis, seat and feet remain untouched.
            bool seated = !tableLean || seatedTable;
            Vector3 lap = spine.parent.position + ownerRoot.up * (seated ? .03f : -.13f) +
                ownerRoot.forward * (seated ? .17f : .025f);
            Vector3 leftTarget = Vector3.Lerp(leftContact.position, lap - ownerRoot.right * .16f, karaokePoseWeight);
            Vector3 rightTarget = Vector3.Lerp(rightContact.position, lap + ownerRoot.right * .16f, karaokePoseWeight);
            SeatedArmIk.SolveTwoBone(leftUpperArm, leftForearm, leftHand, leftTarget,
                leftContact.rotation, ResolveElbowHint(leftUpperArm, false));
            SeatedArmIk.SolveTwoBone(rightUpperArm, rightForearm, rightHand, rightTarget,
                rightContact.rotation, ResolveElbowHint(rightUpperArm, true));
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
                    CounterSpineLeanDegrees * (1f - karaokePoseWeight),
                    CounterChestLeanDegrees * (1f - karaokePoseWeight));
                CompensateHeadLean(CounterHeadCompensationDegrees * (1f - karaokePoseWeight));
                ApplyRestHeadMotion();
                SolveLeftArmTowardsSupport();
                if (CounterRefill != null && CounterRefill.IsRefilling)
                    SolveCounterRightRestSupport();
                else
                {
                    SolveRightHandBottleGrip();
                    CacheCounterRightRestPose();
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
                TableSpineLeanDegrees * (1f - karaokePoseWeight),
                TableChestLeanDegrees * (1f - karaokePoseWeight));
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
            leftHand.localRotation = leftHandBaseLocalRotation;
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

            // Preserve this pose's wrist roll, but close on the wrist rather
            // than treating its offset grip socket as a third limb segment.
            // Near full extension CCD alone left the hand centimetres short.
            Quaternion handRotation = leftHand.rotation;
            Vector3 wrist = tableSupportPoint - handRotation *
                leftHandAttachment.SocketPositionInHand;
            SeatedArmIk.SolveTwoBone(leftUpperArm, leftForearm, leftHand,
                wrist, handRotation, ResolveElbowHint(leftUpperArm, false));
        }

        private void ApplyBottleSipLean()
        {
            // Each vessel has its own body motion as well as its own tilt.
            // A broad bowl meets the lips without the bottle's deep lean.
            float weight = timeline.VesselTipWeight;
            if (weight <= 0.0001f)
            {
                return;
            }

            Vector3 axis = ownerRoot.right;
            Quaternion spineBefore = spine.rotation;
            Quaternion chestBefore = chest.rotation;
            Quaternion headBefore = head.rotation;
            float torsoScale = sipTorsoLeanBackDegrees /
                (SipSpineLeanBackDegrees + SipChestLeanBackDegrees);
            spine.rotation = Quaternion.AngleAxis(
                    -SipSpineLeanBackDegrees * torsoScale * weight,
                    axis) *
                spine.rotation;
            chest.rotation = Quaternion.AngleAxis(
                    -SipChestLeanBackDegrees * torsoScale * weight,
                    axis) *
                chest.rotation;
            head.rotation = Quaternion.AngleAxis(
                    -sipHeadLeanBackDegrees * weight,
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
            Vector3 towardMouth = horizontalIntoMouth.sqrMagnitude > 0.000001f
                ? horizontalIntoMouth.normalized
                : -ownerRoot.forward;
            return Vector3.Slerp(ownerRoot.up, towardMouth,
                maximumSipTiltDegrees / 90f).normalized;
        }

        private Quaternion ResolvePropRotation(float tipWeight)
        {
            Vector3 opening = Vector3.Slerp(ownerRoot.up,
                ResolveSipBottleUp(), Mathf.Clamp01(tipWeight)).normalized;
            Vector3 handle = Vector3.ProjectOnPlane(ownerRoot.right, opening).normalized;
            Vector3 localForward = Vector3.Cross(handleLocalAxis,
                openingLocalAxis).normalized;
            Vector3 worldForward = Vector3.Cross(handle, opening).normalized;
            return Quaternion.LookRotation(worldForward, opening) *
                Quaternion.Inverse(Quaternion.LookRotation(localForward, openingLocalAxis));
        }

        private Vector3 ResolveBottleHandRadial(Quaternion rotation)
        {
            Vector3 radial = Vector3.ProjectOnPlane(
                rotation * handleLocalAxis,
                rotation * openingLocalAxis);
            if (radial.sqrMagnitude < 0.000001f)
            {
                radial = Vector3.ProjectOnPlane(
                    -ownerRoot.forward,
                    rotation * openingLocalAxis);
            }

            return radial.normalized;
        }

        private void ResolveBottlePose(
            out Vector3 bottlePosition,
            out Quaternion bottleRotation)
        {
            float tipWeight = timeline.VesselTipWeight;
            bottleRotation = ResolvePropRotation(tipWeight);
            Vector3 restingRoot = bottleRestPoint -
                ResolvePropRotation(0f) * baseLocalPosition;
            Vector3 lipSolvedRoot = mouthSocket.position -
                bottleRotation * rimLocalPosition;
            bottlePosition = Vector3.Lerp(
                restingRoot,
                lipSolvedRoot,
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
            Vector3 bottleUp = bottleRotation * openingLocalAxis;
            Vector3 gripPosition = bottlePosition + bottleRotation * gripLocalPosition;
            Pose grip = rightAnatomicalHand.ResolveCylinderPose(
                gripPosition, bottleUp, -ResolveBottleHandRadial(bottleRotation));
            Quaternion handRotation = grip.rotation;
            Vector3 handPosition = grip.position;

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

            // A point contact alone cannot distinguish palm from dorsum.
            // The measured thumb/index frame puts the physical curled fingers
            // around the bottle axis and its palm toward the glass.
            SeatedArmIk.SolveTwoBone(rightUpperArm, rightForearm, rightHand,
                handPosition, handRotation, ResolveElbowHint(rightUpperArm, true));
        }

        private Vector3 ResolveElbowHint(Transform upper, bool right)
        {
            return upper.position - ownerRoot.up * 0.30f +
                ownerRoot.right * (right ? 0.10f : -0.10f) +
                ownerRoot.forward * 0.06f;
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
            Vector3 restingBottlePoint,
            BarPatronDrinkProp heldDrink)
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
            Transform handSocketBinding = Require(
                handSocket,
                nameof(handSocket));
            if (handSocketBinding.parent != rightHand)
            {
                throw new ArgumentException(
                    "The bottle socket must be a direct child of hand.R.",
                    nameof(handSocket));
            }

            handPose = rightHand.GetComponentInParent<NpcHandPose>();
            if (handPose == null)
                throw new InvalidOperationException("A default bar patron needs its shared authored hand grip.");
            mouthSocket = Require(mouthAnchor, nameof(mouthAnchor));
            bottleRoot = Require(
                heldBottleRoot,
                nameof(heldBottleRoot));
            bottleMouth = Require(
                heldBottleMouth,
                nameof(heldBottleMouth));
            // Legacy bottle callers use a root at the physical neck grasp.
            // Cup descriptors replace these rotation-only metre offsets.
            gripLocalPosition = Vector3.zero;
            rimLocalPosition = Vector3.up * gripToLipDistance;
            baseLocalPosition = Vector3.down * gripToBaseDistance;
            drinkProp = heldDrink;
            if (drinkProp != null)
            {
                if (drinkProp.Root != bottleRoot || drinkProp.Mouth != bottleMouth)
                    throw new ArgumentException("The drink descriptor must bind the patron's actual prop root and rim.", nameof(heldDrink));
                gripLocalPosition = drinkProp.GripLocalPosition;
                rimLocalPosition = drinkProp.RimLocalPosition;
                baseLocalPosition = drinkProp.BaseLocalPosition;
                openingLocalAxis = drinkProp.OpeningLocalAxis;
                handleLocalAxis = drinkProp.HandleLocalAxis;
                gripKind = drinkProp.GripKind;
                maximumSipTiltDegrees = drinkProp.MaxSipTiltDegrees;
                sipTorsoLeanBackDegrees = drinkProp.SipTorsoLeanBackDegrees;
                sipHeadLeanBackDegrees = drinkProp.SipHeadLeanBackDegrees;
            }
            rightAnatomicalHand = handPose.GetAnatomicalFrame(false, gripKind);
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
            leftHand = Require(leftHandSocket.parent, "supportHand");
            leftHandAttachment = new SeatedArmHandAttachment(leftHand, leftHandSocket);
            tableSupportPoint = supportPoint;
        }

        private void CompleteInitialization(bool usesTableLean)
        {
            AcquireAnimatorCulling();
            // The city graph normally culls transform updates off camera.
            // Sample the actual seated/standing base before caching it; this
            // contact actor must also render its solved limbs on first sight.
            presentation.Advance(0f, false);
            rightClavicleBaseLocalRotation = rightClavicle.localRotation;
            rightUpperBaseLocalRotation = rightUpperArm.localRotation;
            rightForearmBaseLocalRotation = rightForearm.localRotation;
            leftClavicleBaseLocalRotation = leftClavicle.localRotation;
            leftUpperBaseLocalRotation = leftUpperArm.localRotation;
            leftForearmBaseLocalRotation = leftForearm.localRotation;
            leftHandBaseLocalRotation = leftHand.localRotation;
            spineBaseLocalRotation = spine.localRotation;
            chestBaseLocalRotation = chest.localRotation;
            headBaseLocalRotation = head.localRotation;
            tableLean = usesTableLean;
            isInitialized = true;
            // The graph owns the clock; LateUpdate owns final hand contacts.
            presentation.Advanced += OnPresentationAdvanced;
            SampleFinalPose();
        }

        private void ApplyFingerGrip(bool advanceCounterBlend = true)
        {
            // The shared body sampler restores renderer defaults. Reapply
            // the actual authored curled-finger shape after the final solve.
            handPose.SetGrip(false, CounterRightGripWeight(advanceCounterBlend) * (1f - karaokePoseWeight), gripKind);
            handPose.SetGrip(true, 0f);
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
            bool returnAudiencePose = karaokePoseWeight > .0001f || karaokeFocusOwner != null;
            ClearKaraokeFocus(karaokeFocusOwner);
            karaokePoseWeight = karaokeTorsoYaw = 0f;
            if (returnAudiencePose && isInitialized && presentation != null && ownerRoot != null && spine != null && chest != null)
                ApplyFinalContacts(false);
            SetConversationPartner(null);
            handPose?.ResetGrip();
            gulpCuePending = false;
            UnsubscribePresentation();
            ReleaseAuthoredAction();
            RestoreAnimatorCulling();
        }

        private void OnDestroy()
        {
            ClearKaraokeFocus(karaokeFocusOwner);
            UnsubscribePresentation();
            ReleaseAuthoredAction();
            RestoreAnimatorCulling();
        }

        private void OnEnable()
        {
            if (isInitialized && presentation != null)
            {
                AcquireAnimatorCulling();
                presentation.Advanced -= OnPresentationAdvanced;
                presentation.Advanced += OnPresentationAdvanced;
            }
        }

        private void AcquireAnimatorCulling()
        {
            if (ownsAnimatorCulling || presentation == null ||
                presentation.Registry == null || presentation.Registry.Animator == null)
                return;
            contactAnimator = presentation.Registry.Animator;
            previousAnimatorCulling = contactAnimator.cullingMode;
            // These nine bounded tableaux solve hands and props every frame.
            // CullUpdateTransforms can retain an old modular skin pose while
            // the separately rendered vessel already follows its new contact.
            contactAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ownsAnimatorCulling = true;
        }

        private void RestoreAnimatorCulling()
        {
            if (!ownsAnimatorCulling) return;
            if (contactAnimator != null)
                contactAnimator.cullingMode = previousAnimatorCulling;
            ownsAnimatorCulling = false;
            contactAnimator = null;
        }

        private void UnsubscribePresentation()
        {
            if (presentation != null)
            {
                presentation.Advanced -= OnPresentationAdvanced;
            }
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
