using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public enum Player3DLocomotionState
    {
        Idle = 0,
        Walk = 1,
        WalkBack = 2,
        TurnLeft = 3,
        TurnRight = 4,
        Run = 5
    }

    /// <summary>
    /// Continuous world presentation for the modular player model. The
    /// CharacterController owns movement and facing; a manual PlayableGraph
    /// owns bone-only, in-place animation and deterministic clip sampling.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Player3DCharacterPresentation :
        MonoBehaviour,
        IPlayerPresentation,
        IPlayerClipPresentation,
        IPlayerBalancePresentation,
        IPlayerRisePresentation
    {
        public const float FullWalkSpeed = 2.6f;
        public const float FullRunSpeed = 4.2f;
        public const float FullWalkBackSpeed = 1.4f;

        // The Silent Hill head: how fast attention takes and releases
        // the head, how the turn is shared down the neck, and the sign
        // conventions of the imported bone axes.
        public const float AttentionBlendInSeconds = 0.22f;
        public const float AttentionBlendOutSeconds = 0.38f;
        public const float AttentionTurnSmoothTime = 0.16f;
        public const float AttentionHeadShare = 0.62f;
        public const float AttentionNeckShare = 0.38f;
        // In-game check: a positive local-X turn on the imported neck
        // and head bones pitches the face UP, so a look-down needs the
        // positive sign — the mirror of the wheel-roll lesson.
        public const float AttentionYawSign = 1f;
        public const float AttentionPitchSign = 1f;

        private const float LocomotionBlendInTime = 0.14f;
        private const float LocomotionBlendOutTime = 0.20f;
        private const float MotionThreshold = 0.05f;
        private const float StatusBlendSpeed = 4.5f;
        private const float BodyRadius = 0.32f;

        // How far maximum intoxication lowers the pelvis onto the
        // IK-held boots: the "heavy knees" of the old symmetric bend.
        private const float IntoxicationCrouchMetres = 0.03f;

        // The drunk holds his arms out for balance, tightrope fashion:
        // abduction (degrees out from the hanging arm) grows with the
        // SQUARE of the status level so a light buzz barely shows and
        // blind drunk is unmistakable, the balance model's reaction
        // raises them further at full instability, part of every degree
        // of spread is a raise to the front so the hands sit ahead of the
        // hips rather than in a stiff T, the arm on the side he is
        // falling AWAY from rises by this much per degree of lean, and
        // the ambient stagger hunts the hands forward and back against
        // each other. Neither arm goes above the shoulder line. The chest
        // counters part of the pelvis roll so the head stays nearer level
        // than the hips.
        private const float IntoxicationArmSpreadDegrees = 40f;
        private const float BalanceArmReactionDegrees = 45f;
        private const float ArmForwardRaiseFraction = 0.3f;
        private const float BalanceArmLeanCoupling = 0.8f;
        private const float IntoxicationArmHuntDegrees = 6f;
        private const float MaximumArmOutwardDegrees = 85f;
        private const float BalanceChestCounterRoll = -0.35f;

        // The arm on the side he leans toward drops, but never closes:
        // it keeps at least this much of the spread, so the tightrope is
        // still a tightrope when the torso whips and the lean is deep.
        private const float MinimumArmSpreadFraction = 0.5f;

        // Inertia of the balance pose. Every channel the model writes is
        // chased through a mass-spring-damper before it reaches a bone:
        // the arms fly out late and overshoot (the windmill of a man who
        // has just been thrown off balance), the lean follows the centre
        // of mass with a little lag rather than snapping to it, and a
        // crouch settles. Each filter is exactly inert at zero, so the
        // sober pose stays bit-for-bit still and a zero-delta re-apply
        // changes nothing.
        private const float ArmFilterOmega = 9f;
        private const float ArmFilterZeta = 0.45f;
        private const float LeanFilterOmega = 14f;
        private const float LeanFilterZeta = 0.8f;
        private const float CrouchFilterOmega = 10f;
        private const float CrouchFilterZeta = 1f;

        // The torso's hip-strategy whip lands on the chest through its
        // own spring, and the arms go with it: both come up as the torso
        // pitches either way (the flail of a man throwing his arms out),
        // and the arm on the side the torso rolls toward rises higher.
        private const float TorsoFilterOmega = 12f;
        private const float TorsoFilterZeta = 0.6f;
        private const float TorsoArmForwardCoupling = 0.6f;
        private const float TorsoArmLeanCoupling = 0.4f;

        // The rise's ground contacts: where the palms and the lead boot
        // meet the floor is probed, from this far above the point and
        // this far down, and a palm rests a hair above what it finds; a
        // hand on the knee sits this far above the shin joint.
        private const float RiseProbeHeight = 0.6f;
        private const float RiseProbeDistance = 1.6f;
        private const float RisePalmClearance = 0.02f;
        private const float RiseKneeHandLift = 0.05f;
        private static readonly RaycastHit[] RiseHits = new RaycastHit[16];

        // Turn-in-place engages only while the feet are effectively
        // stationary and the player is clearly holding a yaw input.
        private const float TurnInPlaceSpeedThreshold = 0.25f;
        private const float TurnInPlaceInputThreshold = 0.2f;

        // The authored Run loop is 18 frames at 24 fps.
        private const float FullRunCycleSeconds = 0.75f;

        // Locomotion mixer layout: input 0 is Idle, the gaits follow.
        private const int GaitCount = 5;
        private const int WalkGait = 0;
        private const int WalkBackGait = 1;
        private const int RunGait = 2;
        private const int TurnLeftGait = 3;
        private const int TurnRightGait = 4;

        private enum ClipOwner
        {
            None = 0,
            External,
            Fall
        }

        private readonly PlayerFacialAnimationState facialState =
            new PlayerFacialAnimationState();
        private readonly Player3DFaceAtlasPresenter faceAtlasPresenter =
            new Player3DFaceAtlasPresenter();

        private Player3DAssetRegistry registry;
        private Transform actorFacingTransform;
        private PlayableGraph graph;
        private AnimationMixerPlayable locomotionMixer;
        private AnimationLayerMixerPlayable layerMixer;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable walkPlayable;
        private AnimationClipPlayable walkBackPlayable;
        private AnimationClipPlayable runPlayable;
        private AnimationClipPlayable turnLeftPlayable;
        private AnimationClipPlayable turnRightPlayable;
        private AnimationClipPlayable activeClipPlayable;
        private Player3DAnimationBinding idleBinding;
        private Player3DAnimationBinding walkBinding;
        private Player3DAnimationBinding walkBackBinding;
        private Player3DAnimationBinding runBinding;
        private Player3DAnimationBinding turnLeftBinding;
        private Player3DAnimationBinding turnRightBinding;
        private Player3DAnimationBinding activeClipBinding;
        private ClipOwner activeClipOwner;
        private float activeClipNormalizedTime;
        private Vector3 clipModelLocalPosition;
        private Quaternion clipModelLocalRotation;
        private Vector3 clipModelLocalScale;
        private bool clipSpatialStateCaptured;
        private readonly float[] targetGaitWeights = new float[GaitCount];
        private readonly float[] gaitWeights = new float[GaitCount];
        private readonly float[] gaitWeightVelocities = new float[GaitCount];
        private float locomotionBlend;
        private float runBlend;
        private float forwardGaitCyclesPerSecond;
        private float planarSpeed;
        private bool hasAuthoredRunClip;
        private float intoxicationTarget;
        private float intoxicationAmount;
        private float balanceLeanTarget;
        private float balanceLean;
        private float fallAmount;
        private float fallDirection = 1f;
        private Vector3 fallAxisWorld = Vector3.right;
        private bool hasFallAxis;
        private float footPlantAmount = 1f;
        private float footPlantLeft = 1f;
        private float footPlantRight = 1f;
        private bool forwardGaitDominant;
        private float forwardGaitCycle;
        private float statusSwayPhase;
        private SecondOrderFilter leftArmOutwardFilter =
            new SecondOrderFilter(ArmFilterOmega, ArmFilterZeta);
        private SecondOrderFilter leftArmForwardFilter =
            new SecondOrderFilter(ArmFilterOmega, ArmFilterZeta);
        private SecondOrderFilter rightArmOutwardFilter =
            new SecondOrderFilter(ArmFilterOmega, ArmFilterZeta);
        private SecondOrderFilter rightArmForwardFilter =
            new SecondOrderFilter(ArmFilterOmega, ArmFilterZeta);
        private SecondOrderFilter leanRollFilter =
            new SecondOrderFilter(LeanFilterOmega, LeanFilterZeta);
        private SecondOrderFilter leanPitchFilter =
            new SecondOrderFilter(LeanFilterOmega, LeanFilterZeta);
        private SecondOrderFilter crouchFilter =
            new SecondOrderFilter(CrouchFilterOmega, CrouchFilterZeta);
        private SecondOrderFilter torsoRollFilter =
            new SecondOrderFilter(TorsoFilterOmega, TorsoFilterZeta);
        private SecondOrderFilter torsoPitchFilter =
            new SecondOrderFilter(TorsoFilterOmega, TorsoFilterZeta);
        private PlayerBalancePose balancePose = PlayerBalancePose.Neutral;
        private PlayerRisePose risePose = PlayerRisePose.None;
        private bool ragdollPoseActive;
        private bool interactionHandoffLocked;
        private bool releaseInteractionHandoffAfterLateUpdate;
        private FacialBoneRest leftEye;
        private FacialBoneRest rightEye;
        private FacialBoneRest leftBrow;
        private FacialBoneRest rightBrow;
        private FacialBoneRest mouth;
        private Transform pelvisBone;
        private Transform chestBone;
        private Transform leftUpperArmBone;
        private Transform rightUpperArmBone;
        private Transform leftThighBone;
        private Transform rightThighBone;
        private Transform leftForearmBone;
        private Transform rightForearmBone;
        private Transform leftHandBone;
        private Transform rightHandBone;
        private Transform leftShinBone;
        private Transform rightShinBone;
        private Transform leftFootBone;
        private Transform rightFootBone;
        private Transform headBone;
        private Transform neckBone;
        private Vector3? attentionFocus;
        private float attentionWeight;
        private float attentionYaw;
        private float attentionPitch;
        private float attentionYawVelocity;
        private float attentionPitchVelocity;
        private Quaternion attentionHeadBase;
        private Quaternion attentionNeckBase;
        private bool attentionBaseCaptured;
        private readonly Player3DProceduralLocomotionLayer layer =
            new Player3DProceduralLocomotionLayer();
        private PlayerFacialExpression visibleFacialExpression;

        public Player3DAssetRegistry Registry => registry;
        public IReadOnlyList<Renderer> Renderers =>
            registry != null
                ? registry.Renderers
                : Array.Empty<Renderer>();
        public Transform VisualRoot =>
            registry != null && registry.ModelRoot != null
                ? registry.ModelRoot
                : transform;
        public PlayerPresentationMetrics Metrics =>
            new PlayerPresentationMetrics(
                registry != null
                    ? registry.Metrics.CanonicalHeight
                    : 1.75f,
                BodyRadius,
                actorFacingTransform != null
                    ? actorFacingTransform
                    : transform,
                GetAnchorPosition(
                    registry != null
                        ? registry.Anchors.LeftFoot
                        : null),
                GetAnchorPosition(
                    registry != null
                        ? registry.Anchors.RightFoot
                        : null),
                footPlantAmount,
                fallAmount,
                fallDirection,
                hasFallAxis ? fallAxisWorld : (Vector3?)null);
        public bool InteractionHandoffLocked =>
            interactionHandoffLocked;
        public Player3DLocomotionState CurrentLocomotionState { get; private set; }
        public float LocomotionBlend => locomotionBlend;
        public float RunBlend => runBlend;
        public float ForwardGaitCyclesPerSecond =>
            forwardGaitCyclesPerSecond;
        public bool HasAuthoredRunClip => hasAuthoredRunClip;
        public float PlanarSpeed => planarSpeed;
        public float IntoxicationAmount => intoxicationAmount;
        public float BalanceLean => balanceLean;
        public float FallAmount => fallAmount;
        public float FallDirection => fallDirection;
        public bool RagdollPoseActive => ragdollPoseActive;

        /// <summary>Per-foot plant weights the late leg layer works from.</summary>
        public float LeftFootPlant => footPlantLeft;
        public float RightFootPlant => footPlantRight;

        /// <summary>The Walk clip's normalized time (<c>0..1</c>) while a forward gait leads; zero otherwise.</summary>
        public float ForwardGaitCycle => forwardGaitCycle;

        /// <summary>Whether Walk or Run leads the locomotion blend this frame.</summary>
        public bool ForwardGaitDominant => forwardGaitDominant;

        /// <summary>What the ground probe under each boot found this frame.</summary>
        public FootGroundSample LeftFootGround =>
            layer.GetSample(FootSide.Left);
        public FootGroundSample RightFootGround =>
            layer.GetSample(FootSide.Right);

        /// <summary>How far the leg solve has faded in (<c>0..1</c>).</summary>
        public float FootIkBlend => layer.IkBlend;

        /// <summary>The pelvis offset the leg layer applied this frame.</summary>
        public float PelvisDrop => layer.LastPelvisDrop;

        /// <summary>Test seam: the late procedural layer itself, for diagnostics that read its per-foot state.</summary>
        internal Player3DProceduralLocomotionLayer Layer => layer;
        public PlayerFacialExpression CurrentFacialExpression =>
            visibleFacialExpression;
        public bool UsesFacialAtlas => faceAtlasPresenter.IsConfigured;
        public string ActiveClipName =>
            activeClipBinding != null
                ? activeClipBinding.ClipName
                : string.Empty;
        public bool IsClipActive => activeClipBinding != null;

        public void Initialize(
            Transform facingTransform,
            Player3DAssetRegistry assetRegistry)
        {
            if (assetRegistry == null)
            {
                throw new ArgumentNullException(nameof(assetRegistry));
            }

            DestroyGraph();
            registry = assetRegistry;
            faceAtlasPresenter.Configure(registry.FaceAtlas);
            actorFacingTransform = facingTransform != null
                ? facingTransform
                : transform;

            Animator animator = registry.Animator;
            if (animator == null)
            {
                throw new InvalidOperationException(
                    "The Player3D registry has no Animator.");
            }

            if (!TryResolveAnimation("Idle", out idleBinding) ||
                !TryResolveAnimation("Walk", out walkBinding) ||
                !TryResolveAnimation("WalkBack", out walkBackBinding) ||
                !TryResolveAnimation("Run", out runBinding) ||
                !TryResolveAnimation("TurnLeft", out turnLeftBinding) ||
                !TryResolveAnimation("TurnRight", out turnRightBinding))
            {
                throw new InvalidOperationException(
                    "The Player3D registry requires the Idle, Walk, " +
                    "WalkBack, Run, TurnLeft and TurnRight clips.");
            }

            hasAuthoredRunClip = true;

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null;
            ConfigureWorldRenderers();
            BuildGraph(animator);
            CaptureStatusBones();
            CaptureFacialBones();
            facialState.Reset();
            SetMotion(PlayerMotionSample.Stationary);
            ApplyLocomotionWeights(immediate: true);
            EvaluateGraph(0f);
            ApplyFacialExpression(PlayerFacialExpression.Neutral);
            layer.Bind(
                registry,
                actorFacingTransform,
                pelvisBone,
                chestBone,
                leftUpperArmBone,
                rightUpperArmBone,
                leftThighBone,
                leftShinBone,
                leftFootBone,
                rightThighBone,
                rightShinBone,
                rightFootBone,
                Player3DFootGroundProbe.CreateForHero(
                    registry,
                    actorFacingTransform));
            layer.BindArms(
                GetPartBone(Player3DAnatomicalPart.LeftForearm),
                GetPartBone(Player3DAnatomicalPart.LeftHand),
                GetPartBone(Player3DAnatomicalPart.RightForearm),
                GetPartBone(Player3DAnatomicalPart.RightHand));
            layer.BindHead(neckBone, headBone);
            layer.Calibrate();
        }

        public void SetMotion(in PlayerMotionSample motion)
        {
            Vector3 planarVelocity = motion.PlanarVelocity;
            planarVelocity.y = 0f;
            planarSpeed = planarVelocity.magnitude;

            for (int index = 0; index < GaitCount; index++)
            {
                targetGaitWeights[index] = 0f;
            }

            // In a topple the root travels with the falling centre of
            // mass at up to a couple of metres a second. That is not a
            // walk: no gait is blended in under the lunge.
            bool toppling = balancePose.Phase == BalancePhase.Toppling ||
                            balancePose.Phase == BalancePhase.Fallen;
            Player3DLocomotionState state = Player3DLocomotionState.Idle;
            if (!interactionHandoffLocked && !toppling)
            {
                bool turningInPlace =
                    planarSpeed < TurnInPlaceSpeedThreshold &&
                    Mathf.Abs(motion.TurnInput) >
                    TurnInPlaceInputThreshold;
                if (turningInPlace)
                {
                    bool turningLeft = motion.TurnInput < 0f;
                    targetGaitWeights[
                        turningLeft ? TurnLeftGait : TurnRightGait] = 1f;
                    state = turningLeft
                        ? Player3DLocomotionState.TurnLeft
                        : Player3DLocomotionState.TurnRight;
                }
                else if (motion.SignedForwardSpeed >= 0f)
                {
                    float forwardBlend = Mathf.Clamp01(
                        motion.SignedForwardSpeed / FullWalkSpeed);
                    float requestedRunBlend = Mathf.Clamp01(
                        motion.RunBlend);
                    float visibleRunTarget =
                        forwardBlend * requestedRunBlend;
                    targetGaitWeights[WalkGait] =
                        forwardBlend - visibleRunTarget;
                    targetGaitWeights[RunGait] = visibleRunTarget;
                    if (visibleRunTarget >= MotionThreshold)
                    {
                        state = Player3DLocomotionState.Run;
                    }
                    else if (forwardBlend >= MotionThreshold)
                    {
                        state = Player3DLocomotionState.Walk;
                    }
                }
                else
                {
                    float blend = Mathf.Clamp01(
                        -motion.SignedForwardSpeed / FullWalkBackSpeed);
                    targetGaitWeights[WalkBackGait] = blend;
                    if (blend >= MotionThreshold)
                    {
                        state = Player3DLocomotionState.WalkBack;
                    }
                }
            }

            CurrentLocomotionState = state;
        }

        public void SetInteractionHandoffLocked(bool locked)
        {
            if (!locked)
            {
                releaseInteractionHandoffAfterLateUpdate =
                    interactionHandoffLocked;
                return;
            }

            interactionHandoffLocked = true;
            releaseInteractionHandoffAfterLateUpdate = false;
            planarSpeed = 0f;
            ResetGaitWeights();
            SetFootPlant(1f, 1f, 1f, false);
            CurrentLocomotionState = Player3DLocomotionState.Idle;
            facialState.Reset();
            ApplyLocomotionWeights(immediate: true);
            if (!IsClipActive && idlePlayable.IsValid())
            {
                idlePlayable.SetTime(0d);
                EvaluateGraph(0f);
            }

            ApplyFacialExpression(PlayerFacialExpression.Neutral);
        }

        public void SetIntoxication(float intensity)
        {
            intoxicationTarget = Mathf.Clamp01(intensity);
        }

        public void SetBalancePose(float signedLean)
        {
            balanceLeanTarget = Mathf.Clamp(signedLean, -1f, 1f);
        }

        /// <summary>
        /// The balance model's pose for this frame: lean, arm reaction,
        /// crouch and any recovery step, applied additively over the clip
        /// in the late layer. Replaced every frame by the balance
        /// controller; a frozen or absent controller leaves it neutral.
        /// </summary>
        public void SetBalance(in PlayerBalancePose pose)
        {
            balancePose = pose;
        }

        public PlayerBalancePose BalancePose => balancePose;

        /// <summary>
        /// The rise model's pose for this frame: while it is active the
        /// late pass draws the rise's limbs on top of the Rise clip
        /// instead of the standing balance pose.
        /// </summary>
        public void SetRise(in PlayerRisePose pose)
        {
            risePose = pose;
        }

        public PlayerRisePose RisePose => risePose;

        public void SetFallPose(float signedDirection, float amount)
        {
            if (!Mathf.Approximately(signedDirection, 0f))
            {
                fallDirection = Mathf.Sign(signedDirection);
            }

            fallAmount = Mathf.Clamp01(amount);
            if (fallAmount <= 0f)
            {
                hasFallAxis = false;
            }
        }

        /// <summary>
        /// The world direction the body is falling in, for the shadow
        /// and anything else that reads <see cref="Metrics"/>; forgotten
        /// when the fall amount returns to zero.
        /// </summary>
        public void SetFallAxis(Vector3 worldPlanar)
        {
            worldPlanar.y = 0f;
            if (worldPlanar.sqrMagnitude <= 0.0001f)
            {
                hasFallAxis = false;
                return;
            }

            fallAxisWorld = worldPlanar.normalized;
            hasFallAxis = true;
        }

        /// <summary>
        /// The ragdoll takes the bones as the late layer left them THIS
        /// frame — the topple's lean, the arms out for the ground — not
        /// the clip under them. The current pose is re-applied with no
        /// time passing, the layer forgets what it wrote so nothing puts
        /// the clip back, and the ragdoll flag goes up without the
        /// restore <see cref="SetRagdollPoseActive"/> would do.
        /// </summary>
        internal void BeginRagdollPoseFromLatePose()
        {
            if (ragdollPoseActive)
            {
                return;
            }

            ReapplyLatePresentationPose();
            layer.ForgetBase();
            ReleaseBalanceStep();
            attentionBaseCaptured = false;
            ragdollPoseActive = true;
            ragdollPoseSince = Time.time;
            risePose = PlayerRisePose.None;
            RestoreFacialBones();
            SetFootPlant(0.25f, 0.25f, 0.25f, false);
            ResetPoseFilters();
        }

        public void SetFallAnimation(
            PlayerFallAnimationPhase phase,
            float normalizedProgress)
        {
            if (ragdollPoseActive)
            {
                return;
            }

            if (phase == PlayerFallAnimationPhase.None)
            {
                if (activeClipOwner == ClipOwner.Fall)
                {
                    EndClip();
                }

                return;
            }

            string side = fallDirection < 0f ? "Left" : "Right";
            string clipName;
            switch (phase)
            {
                case PlayerFallAnimationPhase.Falling:
                    clipName = "Fall" + side;
                    break;
                case PlayerFallAnimationPhase.Down:
                    clipName = "Down" + side;
                    break;
                case PlayerFallAnimationPhase.Rising:
                    clipName = "Rise" + side;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(phase),
                        phase,
                        "Unknown player fall animation phase.");
            }

            if (activeClipBinding == null ||
                activeClipBinding.ClipName != clipName ||
                activeClipOwner != ClipOwner.Fall)
            {
                if (!BeginClip(clipName, ClipOwner.Fall))
                {
                    return;
                }
            }

            SampleActiveClip(normalizedProgress);
        }

        public void SetRagdollPoseActive(bool active)
        {
            if (ragdollPoseActive == active)
            {
                return;
            }

            layer.Restore();
            ragdollPoseActive = active;
            if (active)
            {
                ragdollPoseSince = Time.time;
                RestoreFacialBones();
                SetFootPlant(0.25f, 0.25f, 0.25f, false);
                ResetPoseFilters();
            }
            else
            {
                layer.ResetBlend();
                EvaluateGraph(0f);
            }
        }

        public bool HasClip(string clipName)
        {
            return TryResolveAnimation(clipName, out _);
        }

        public bool TryBeginClip(string clipName)
        {
            CaptureClipSpatialState();
            if (BeginClip(clipName, ClipOwner.External))
            {
                return true;
            }

            ResetClipSpatialOffset();
            return false;
        }

        public void SampleActiveClip(float normalizedTime)
        {
            if (activeClipBinding == null ||
                !activeClipPlayable.IsValid())
            {
                throw new InvalidOperationException(
                    "No Player3D clip is active.");
            }

            float progress = Mathf.Clamp01(normalizedTime);
            activeClipNormalizedTime = progress;
            double sampleTime = activeClipBinding.Clip.length * progress;
            activeClipPlayable.SetTime(sampleTime);
            EvaluateGraph(0f);
            ApplyAuthoredClipFacialPose();
        }

        public void AlignActiveClipAnchor(Vector3 worldPelvisTarget)
        {
            if (!IsFinite(worldPelvisTarget))
            {
                throw new ArgumentException(
                    "The contextual pelvis target must be finite.",
                    nameof(worldPelvisTarget));
            }

            if (activeClipOwner != ClipOwner.External ||
                activeClipBinding == null ||
                registry == null ||
                registry.ModelRoot == null ||
                registry.Anchors.Pelvis == null)
            {
                throw new InvalidOperationException(
                    "An external Player3D clip with a validated pelvis " +
                    "anchor must be active before spatial alignment.");
            }

            CaptureClipSpatialState();
            registry.ModelRoot.position +=
                worldPelvisTarget -
                registry.Anchors.Pelvis.position;
        }

        public void ResetClipSpatialOffset()
        {
            if (!clipSpatialStateCaptured)
            {
                return;
            }

            if (registry != null && registry.ModelRoot != null)
            {
                registry.ModelRoot.localPosition =
                    clipModelLocalPosition;
                registry.ModelRoot.localRotation =
                    clipModelLocalRotation;
                registry.ModelRoot.localScale =
                    clipModelLocalScale;
            }

            clipSpatialStateCaptured = false;
        }

        public void EndClip()
        {
            if (!graph.IsValid() || activeClipBinding == null)
            {
                activeClipBinding = null;
                activeClipOwner = ClipOwner.None;
                activeClipNormalizedTime = 0f;
                ResetClipSpatialOffset();
                facialState.Reset();
                ApplyFacialExpression(PlayerFacialExpression.Neutral);
                return;
            }

            graph.Disconnect(layerMixer, 1);
            if (activeClipPlayable.IsValid())
            {
                graph.DestroyPlayable(activeClipPlayable);
            }

            activeClipPlayable = default;
            activeClipBinding = null;
            activeClipOwner = ClipOwner.None;
            activeClipNormalizedTime = 0f;
            layerMixer.SetInputWeight(0, 1f);
            layerMixer.SetInputWeight(1, 0f);
            ApplyLocomotionWeights(immediate: true);
            EvaluateGraph(0f);
            ResetClipSpatialOffset();
            facialState.Reset();
            ApplyFacialExpression(PlayerFacialExpression.Neutral);
        }

        private void Update()
        {
            if (!graph.IsValid())
            {
                return;
            }

            float deltaTime = Mathf.Max(0f, Time.deltaTime);
            intoxicationAmount = Mathf.MoveTowards(
                intoxicationAmount,
                intoxicationTarget,
                StatusBlendSpeed * deltaTime);
            balanceLean = Mathf.MoveTowards(
                balanceLean,
                balanceLeanTarget,
                StatusBlendSpeed * deltaTime);

            if (ragdollPoseActive)
            {
                SetFootPlant(0.25f, 0.25f, 0.25f, false);
                return;
            }

            if (!IsClipActive)
            {
                ApplyLocomotionWeights(immediate: false);
                UpdateForwardGaitCadence();
                walkBackPlayable.SetSpeed((double)Mathf.Lerp(
                    0.70f,
                    0.90f,
                    gaitWeights[WalkBackGait]));
                EvaluateGraph(deltaTime);
                UpdateFootPlant();
            }
            else
            {
                EvaluateGraph(0f);
                float clipPlant = Mathf.Lerp(1f, 0.35f, fallAmount);
                SetFootPlant(clipPlant, clipPlant, clipPlant, false);
            }
        }

        private void LateUpdate()
        {
            if (!ragdollPoseActive)
            {
                ApplyLatePose(Time.deltaTime);
            }

            // The face is drawn under the ragdoll too: the physics has
            // the bones, but the wince and the closed eyes are the
            // atlas's, and a man on the floor with an idle face is wrong.
            ApplyFacialPose();
            if (!ragdollPoseActive)
            {
                ApplyAttentionPose(Time.deltaTime);
            }

            if (releaseInteractionHandoffAfterLateUpdate)
            {
                interactionHandoffLocked = false;
                releaseInteractionHandoffAfterLateUpdate = false;
            }
        }

        /// <summary>The way one knee bends, in the thigh's current frame: a probe seam.</summary>
        internal Vector3 DebugKneeForward(FootSide side)
        {
            return layer.DebugKneeForward(side);
        }

        /// <summary>The way one elbow points, in the upper arm's current frame: a probe seam.</summary>
        internal Vector3 DebugElbowBack(bool rightArm)
        {
            return layer.DebugElbowBack(rightArm);
        }

        internal void ReapplyLatePresentationPose()
        {
            // A deterministic seam for checks that run in batch mode, where
            // WaitForEndOfFrame is not dispatched. It reapplies the current
            // visible pose after the manual graph has evaluated without
            // advancing any presentation state a second time.
            if (!ragdollPoseActive)
            {
                ApplyLatePose(0f);
                ReapplyFacialPose();
                ApplyAttentionPose(0f);
            }
        }

        /// <summary>The late pass: the rise's limbs while a rise is on, the balance pose otherwise.</summary>
        private void ApplyLatePose(float deltaTime)
        {
            if (risePose.Active)
            {
                ApplyRisePose(deltaTime);
            }
            else
            {
                ApplyProceduralStatusPose(deltaTime);
            }
        }

        /// <summary>
        /// The same seam with the pose's inertia run forward by
        /// <paramref name="seconds"/>: the lean and arm filters settle
        /// onto the pose currently set, without the graph or the frame
        /// advancing. For checks that set a pose and want to read where
        /// it lands, not where the springs are on the way there.
        /// </summary>
        internal void SettleLatePresentationPose(float seconds)
        {
            if (!ragdollPoseActive)
            {
                ApplyLatePose(Mathf.Max(0f, seconds));
                ReapplyFacialPose();
                ApplyAttentionPose(0f);
            }
        }

        private void OnDisable()
        {
            interactionHandoffLocked = false;
            releaseInteractionHandoffAfterLateUpdate = false;
            intoxicationTarget = 0f;
            intoxicationAmount = 0f;
            balanceLeanTarget = 0f;
            balanceLean = 0f;
            fallAmount = 0f;
            fallDirection = 1f;
            SetFootPlant(1f, 1f, 1f, false);
            balancePose = PlayerBalancePose.Neutral;
            risePose = PlayerRisePose.None;
            ResetPoseFilters();
            ragdollPoseActive = false;
            planarSpeed = 0f;
            ResetGaitWeights();
            CurrentLocomotionState = Player3DLocomotionState.Idle;
            facialState.Reset();
            RestoreAttentionPoseBase();
            attentionFocus = null;
            attentionWeight = 0f;
            attentionYaw = 0f;
            attentionPitch = 0f;
            attentionYawVelocity = 0f;
            attentionPitchVelocity = 0f;

            if (activeClipBinding != null)
            {
                EndClip();
            }

            if (graph.IsValid())
            {
                // A disabled presentation rests on the exact neutral
                // frame whichever way it got here: ending a clip used to
                // leave the idle loop at whatever phase it had reached,
                // so the resting pose depended on how long the scene had
                // been running.
                ApplyLocomotionWeights(immediate: true);
                if (idlePlayable.IsValid())
                {
                    idlePlayable.SetTime(0d);
                }

                EvaluateGraph(0f);
            }

            ResetFacialPresentation();
        }

        private void OnDestroy()
        {
            layer.Dispose();
            faceAtlasPresenter.Reset();
            DestroyGraph();
        }

        private bool BeginClip(string clipName, ClipOwner owner)
        {
            if (!TryResolveAnimation(
                    clipName,
                    out Player3DAnimationBinding binding) ||
                !graph.IsValid())
            {
                return false;
            }

            if (activeClipBinding != null)
            {
                graph.Disconnect(layerMixer, 1);
                if (activeClipPlayable.IsValid())
                {
                    graph.DestroyPlayable(activeClipPlayable);
                }
            }

            activeClipPlayable = AnimationClipPlayable.Create(
                graph,
                binding.Clip);
            activeClipPlayable.SetApplyFootIK(false);
            activeClipPlayable.SetApplyPlayableIK(false);
            activeClipPlayable.SetSpeed(0d);
            graph.Connect(activeClipPlayable, 0, layerMixer, 1);
            layerMixer.SetInputWeight(0, 0f);
            layerMixer.SetInputWeight(1, 1f);
            activeClipBinding = binding;
            activeClipOwner = owner;
            activeClipNormalizedTime = 0f;
            facialState.Reset();
            SampleActiveClip(0f);
            return true;
        }

        private bool TryResolveAnimation(
            string clipName,
            out Player3DAnimationBinding binding)
        {
            if (registry != null &&
                registry.TryGetAnimation(clipName, out binding) &&
                binding != null &&
                binding.Clip != null)
            {
                return true;
            }

            binding = null;
            return false;
        }

        private void BuildGraph(Animator animator)
        {
            graph = PlayableGraph.Create("Player3D Presentation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            locomotionMixer = AnimationMixerPlayable.Create(
                graph,
                GaitCount + 1);
            layerMixer = AnimationLayerMixerPlayable.Create(graph, 2);
            idlePlayable = CreateLocomotionPlayable(idleBinding);
            walkPlayable = CreateLocomotionPlayable(walkBinding);
            walkBackPlayable = CreateLocomotionPlayable(walkBackBinding);
            runPlayable = CreateLocomotionPlayable(runBinding);
            turnLeftPlayable = CreateLocomotionPlayable(turnLeftBinding);
            turnRightPlayable = CreateLocomotionPlayable(turnRightBinding);
            graph.Connect(idlePlayable, 0, locomotionMixer, 0);
            graph.Connect(walkPlayable, 0, locomotionMixer, WalkGait + 1);
            graph.Connect(
                walkBackPlayable, 0, locomotionMixer, WalkBackGait + 1);
            graph.Connect(runPlayable, 0, locomotionMixer, RunGait + 1);
            graph.Connect(
                turnLeftPlayable, 0, locomotionMixer, TurnLeftGait + 1);
            graph.Connect(
                turnRightPlayable, 0, locomotionMixer, TurnRightGait + 1);
            graph.Connect(locomotionMixer, 0, layerMixer, 0);
            locomotionMixer.SetInputWeight(0, 1f);
            for (int gait = 0; gait < GaitCount; gait++)
            {
                locomotionMixer.SetInputWeight(gait + 1, 0f);
            }

            layerMixer.SetInputWeight(0, 1f);
            layerMixer.SetInputWeight(1, 0f);

            AnimationPlayableOutput output =
                AnimationPlayableOutput.Create(
                    graph,
                    "Player3D Animator",
                    animator);
            output.SetSourcePlayable(layerMixer);
            graph.Play();
        }

        private AnimationClipPlayable CreateLocomotionPlayable(
            Player3DAnimationBinding binding)
        {
            AnimationClipPlayable playable = AnimationClipPlayable.Create(
                graph,
                binding.Clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            return playable;
        }

        private void ResetGaitWeights()
        {
            for (int gait = 0; gait < GaitCount; gait++)
            {
                targetGaitWeights[gait] = 0f;
                gaitWeights[gait] = 0f;
                gaitWeightVelocities[gait] = 0f;
            }

            locomotionBlend = 0f;
            runBlend = 0f;
            forwardGaitCyclesPerSecond = 0f;
        }

        private void ApplyLocomotionWeights(bool immediate)
        {
            if (!locomotionMixer.IsValid())
            {
                runBlend = 0f;
                return;
            }

            float total = 0f;
            for (int gait = 0; gait < GaitCount; gait++)
            {
                if (immediate)
                {
                    gaitWeights[gait] = targetGaitWeights[gait];
                    gaitWeightVelocities[gait] = 0f;
                }
                else
                {
                    float smoothTime =
                        targetGaitWeights[gait] > gaitWeights[gait]
                            ? LocomotionBlendInTime
                            : LocomotionBlendOutTime;
                    gaitWeights[gait] = Mathf.SmoothDamp(
                        gaitWeights[gait],
                        targetGaitWeights[gait],
                        ref gaitWeightVelocities[gait],
                        smoothTime,
                        Mathf.Infinity,
                        Mathf.Max(0f, Time.deltaTime));
                    if (Mathf.Abs(
                            gaitWeights[gait] -
                            targetGaitWeights[gait]) < 0.0005f)
                    {
                        gaitWeights[gait] = targetGaitWeights[gait];
                        gaitWeightVelocities[gait] = 0f;
                    }
                }

                total += gaitWeights[gait];
            }

            // A crossfade briefly overlaps two gaits; scale the applied
            // weights (never the smoothed state) so the pose cannot
            // overshoot while idle fills whatever remains.
            float scale = total > 1f ? 1f / total : 1f;
            locomotionMixer.SetInputWeight(
                0,
                Mathf.Max(0f, 1f - (total * scale)));
            for (int gait = 0; gait < GaitCount; gait++)
            {
                float appliedWeight = gaitWeights[gait] * scale;
                locomotionMixer.SetInputWeight(
                    gait + 1,
                    appliedWeight);
                if (gait == RunGait)
                {
                    runBlend = appliedWeight;
                }
            }

            locomotionBlend = Mathf.Min(1f, total);
        }

        private void UpdateForwardGaitCadence()
        {
            if (!walkPlayable.IsValid() ||
                !runPlayable.IsValid() ||
                walkBinding == null ||
                walkBinding.Clip == null ||
                runBinding == null ||
                runBinding.Clip == null)
            {
                forwardGaitCyclesPerSecond = 0f;
                return;
            }

            float forwardWeight = Mathf.Clamp01(
                gaitWeights[WalkGait] + gaitWeights[RunGait]);
            float visibleRunRatio = forwardWeight > 0.0001f
                ? Mathf.Clamp01(gaitWeights[RunGait] / forwardWeight)
                : 0f;
            float walkPlaybackSpeed = Mathf.Lerp(
                0.78f,
                1.12f,
                forwardWeight);
            float walkCyclesPerSecond = walkPlaybackSpeed /
                                        Mathf.Max(
                                            0.0001f,
                                            walkBinding.Clip.length);
            // The drunk walk's half-steps run fast and slow; only the
            // walk's share of the cadence, never the run's, and exactly
            // one sober.
            walkCyclesPerSecond *= balancePose.Gait.CadenceMultiplier;
            float runCyclesPerSecond =
                (1f / FullRunCycleSeconds) *
                Mathf.Clamp01(planarSpeed / FullRunSpeed);
            float sharedCyclesPerSecond = Mathf.Lerp(
                walkCyclesPerSecond,
                runCyclesPerSecond,
                visibleRunRatio);
            forwardGaitCyclesPerSecond = sharedCyclesPerSecond;

            // Both playables begin at normalized phase zero and receive the
            // same normalized phase delta every graph evaluation. Their
            // contact landmarks therefore stay aligned through Walk/Run
            // crossfades despite the different clip lengths.
            walkPlayable.SetSpeed((double)(
                sharedCyclesPerSecond * walkBinding.Clip.length));
            runPlayable.SetSpeed((double)(
                sharedCyclesPerSecond * runBinding.Clip.length));
        }

        private void SetFootPlant(
            float combined,
            float left,
            float right,
            bool forwardGait)
        {
            footPlantAmount = combined;
            footPlantLeft = left;
            footPlantRight = right;
            forwardGaitDominant = forwardGait;
        }

        private void UpdateFootPlant()
        {
            float forwardWeight =
                gaitWeights[WalkGait] + gaitWeights[RunGait];
            float strongestOther = Mathf.Max(
                gaitWeights[WalkBackGait],
                Mathf.Max(
                    gaitWeights[TurnLeftGait],
                    gaitWeights[TurnRightGait]));
            if (forwardWeight > 0.0001f &&
                forwardWeight >= strongestOther)
            {
                float visibleRunRatio = Mathf.Clamp01(
                    gaitWeights[RunGait] / forwardWeight);
                forwardGaitCycle =
                    walkPlayable.IsValid() &&
                    walkBinding != null &&
                    walkBinding.Clip != null &&
                    walkBinding.Clip.length > 0.0001f
                        ? Mathf.Repeat(
                            (float)(walkPlayable.GetTime() / walkBinding.Clip.length),
                            1f)
                        : 0f;
                SampleFootPlants(
                    walkPlayable,
                    walkBinding,
                    0.68f,
                    false,
                    out float walkLeft,
                    out float walkRight);
                SampleFootPlants(
                    runPlayable,
                    runBinding,
                    hasAuthoredRunClip ? 0.42f : 0.68f,
                    hasAuthoredRunClip,
                    out float runLeft,
                    out float runRight);
                float left = Mathf.Lerp(
                    1f,
                    Mathf.Lerp(walkLeft, runLeft, visibleRunRatio),
                    locomotionBlend);
                float right = Mathf.Lerp(
                    1f,
                    Mathf.Lerp(walkRight, runRight, visibleRunRatio),
                    locomotionBlend);
                SetFootPlant(
                    PlayerFootPlacementRules.CombinedPlant(left, right),
                    left,
                    right,
                    true);
                return;
            }

            AnimationClipPlayable gaitPlayable = walkPlayable;
            Player3DAnimationBinding gaitBinding = walkBinding;
            float bestWeight = gaitWeights[WalkGait];
            if (gaitWeights[WalkBackGait] > bestWeight)
            {
                bestWeight = gaitWeights[WalkBackGait];
                gaitPlayable = walkBackPlayable;
                gaitBinding = walkBackBinding;
            }

            if (gaitWeights[RunGait] > bestWeight)
            {
                bestWeight = gaitWeights[RunGait];
                gaitPlayable = runPlayable;
                gaitBinding = runBinding;
            }

            if (gaitWeights[TurnLeftGait] > bestWeight)
            {
                bestWeight = gaitWeights[TurnLeftGait];
                gaitPlayable = turnLeftPlayable;
                gaitBinding = turnLeftBinding;
            }

            if (gaitWeights[TurnRightGait] > bestWeight)
            {
                gaitPlayable = turnRightPlayable;
                gaitBinding = turnRightBinding;
            }

            // Backpedal and turn-in-place clips do not share Walk's
            // left-first contact order, so both boots take the scalar
            // plant: the legs still ground themselves, but neither is
            // singled out as the stance foot.
            forwardGaitCycle = 0f;
            SampleFootPlants(
                gaitPlayable,
                gaitBinding,
                0.68f,
                false,
                out float otherLeft,
                out float otherRight);
            float symmetric = Mathf.Lerp(
                1f,
                PlayerFootPlacementRules.CombinedPlant(
                    otherLeft,
                    otherRight),
                locomotionBlend);
            SetFootPlant(symmetric, symmetric, symmetric, false);
        }

        /// <summary>
        /// Where each boot is in its contact cycle. Walk contacts the left
        /// heel at cycle zero and the right at one half; Run keeps the
        /// order with a short flight near .375/.875. The scalar the
        /// contact shadow reads is the larger of the two.
        /// </summary>
        private static void SampleFootPlants(
            AnimationClipPlayable playable,
            Player3DAnimationBinding binding,
            float minimumPlant,
            bool usesRunLandmarks,
            out float left,
            out float right)
        {
            if (!playable.IsValid() ||
                binding == null ||
                binding.Clip == null ||
                binding.Clip.length <= 0.0001f)
            {
                left = 1f;
                right = 1f;
                return;
            }

            float cycle = (float)(playable.GetTime() /
                                  binding.Clip.length);
            PlayerFootPlacementRules.FootPlantAmounts(
                cycle,
                usesRunLandmarks,
                minimumPlant,
                out left,
                out right);
        }

        /// <summary>
        /// Where the hero's head should look, in world space, or null
        /// for nothing. Set every frame by the attention controller;
        /// the presentation owns all smoothing and limits.
        /// </summary>
        public void SetAttentionFocus(Vector3? focus)
        {
            attentionFocus = focus;
        }

        public float AttentionWeight => attentionWeight;

        /// <summary>
        /// The Silent Hill head turn: an additive post-animation yaw
        /// and pitch shared between neck and head, eased in when a
        /// noticeable thing appears and eased back out when it is
        /// gone. Modal clips, interaction handoffs and the ragdoll own
        /// the whole body, so attention stands down for them.
        /// </summary>
        private void ApplyAttentionPose(float deltaTime)
        {
            RestoreAttentionPoseBase();
            bool headFree = registry != null &&
                            headBone != null &&
                            !IsClipActive &&
                            !interactionHandoffLocked &&
                            !ragdollPoseActive &&
                            !risePose.Active;
            bool allowed = headFree && attentionFocus.HasValue;
            // The drunk head: the chin sinks, the head wanders and nods,
            // and it trails the body's lean — summed with the glance
            // under the same limits. Exactly still sober.
            IntoxicationHeadPose drunkHead = headFree
                ? headModel.Advance(
                    deltaTime,
                    intoxicationAmount,
                    lastModelRoll,
                    lastModelPitch)
                : IntoxicationHeadPose.None;
            if (!headFree)
            {
                headModel.Reset();
            }

            if (forcedDrunkHead.HasValue && headFree)
            {
                drunkHead = forcedDrunkHead.Value;
            }

            float weightTarget = allowed ? 1f : 0f;
            if (allowed)
            {
                PlayerAttentionRules.ResolveHeadAngles(
                    headBone.position,
                    actorFacingTransform.eulerAngles.y,
                    attentionFocus.Value,
                    out float targetYaw,
                    out float targetPitch);
                if (attentionWeight <= 0.001f)
                {
                    // A fresh glance starts on target instead of
                    // swinging in from wherever the head last looked.
                    attentionYaw = targetYaw;
                    attentionPitch = targetPitch;
                    attentionYawVelocity = 0f;
                    attentionPitchVelocity = 0f;
                }
                else if (deltaTime > 0f)
                {
                    attentionYaw = Mathf.SmoothDampAngle(
                        attentionYaw,
                        targetYaw,
                        ref attentionYawVelocity,
                        AttentionTurnSmoothTime,
                        float.PositiveInfinity,
                        deltaTime);
                    attentionPitch = Mathf.SmoothDamp(
                        attentionPitch,
                        targetPitch,
                        ref attentionPitchVelocity,
                        AttentionTurnSmoothTime,
                        float.PositiveInfinity,
                        deltaTime);
                }
            }

            if (deltaTime > 0f)
            {
                attentionWeight = Mathf.MoveTowards(
                    attentionWeight,
                    weightTarget,
                    deltaTime / (weightTarget > attentionWeight
                        ? AttentionBlendInSeconds
                        : AttentionBlendOutSeconds));
            }

            if (attentionWeight <= 0.0001f && drunkHead.IsNone)
            {
                return;
            }

            // The glance's angles are in the rules' sense (yaw positive
            // right, pitch positive UP); the drunk head's pitch is
            // written chin-down, so it enters negated. They add before
            // the signs that map them onto the bones, under the glance's
            // own limits.
            float yaw = Mathf.Clamp(
                attentionYaw * attentionWeight + drunkHead.YawDegrees,
                -PlayerAttentionRules.MaxHeadYawDegrees,
                PlayerAttentionRules.MaxHeadYawDegrees);
            float pitch = Mathf.Clamp(
                attentionPitch * attentionWeight - drunkHead.PitchDownDegrees,
                -PlayerAttentionRules.MaxHeadDownPitchDegrees,
                PlayerAttentionRules.MaxHeadUpPitchDegrees);
            float roll = Mathf.Clamp(
                drunkHead.RollDegrees,
                -DrunkHeadMaximumRollDegrees,
                DrunkHeadMaximumRollDegrees);
            yaw *= AttentionYawSign;
            pitch *= AttentionPitchSign;
            roll *= DrunkHeadRollSign;
            CaptureAttentionPoseBase();
            if (neckBone != null)
            {
                neckBone.localRotation *= Quaternion.Euler(
                    pitch * AttentionNeckShare,
                    yaw * AttentionNeckShare,
                    roll * AttentionNeckShare);
            }

            if (headBone != null)
            {
                headBone.localRotation *= Quaternion.Euler(
                    pitch * AttentionHeadShare,
                    yaw * AttentionHeadShare,
                    roll * AttentionHeadShare);
            }
        }

        /// <summary>The head may tilt this far toward a shoulder.</summary>
        public const float DrunkHeadMaximumRollDegrees = 10f;

        /// <summary>
        /// Which way a positive roll about the neck and head bones' local
        /// forward tilts the head; pinned by the drunk-face probe, the
        /// way the pitch and yaw signs were.
        /// </summary>
        public const float DrunkHeadRollSign = 1f;

        private readonly IntoxicationHeadModel headModel =
            new IntoxicationHeadModel(HeadSeedSalt);
        private const int HeadSeedSalt = 0x4E0D;
        private float lastModelRoll;
        private float lastModelPitch;

        /// <summary>The drunk head's pose this frame, for probes.</summary>
        public IntoxicationHeadPose DrunkHeadPose => headModel.Pose;

        private IntoxicationHeadPose? forcedDrunkHead;

        /// <summary>A probe seam: pin the drunk head to a pose (null lets the model drive it again).</summary>
        internal void DebugForceDrunkHead(IntoxicationHeadPose? pose)
        {
            forcedDrunkHead = pose;
        }

        private void CaptureAttentionPoseBase()
        {
            attentionHeadBase = headBone != null
                ? headBone.localRotation
                : Quaternion.identity;
            attentionNeckBase = neckBone != null
                ? neckBone.localRotation
                : Quaternion.identity;
            attentionBaseCaptured = true;
        }

        private void RestoreAttentionPoseBase()
        {
            if (!attentionBaseCaptured)
            {
                return;
            }

            if (headBone != null)
            {
                headBone.localRotation = attentionHeadBase;
            }

            if (neckBone != null)
            {
                neckBone.localRotation = attentionNeckBase;
            }

            attentionBaseCaptured = false;
        }

        private void ApplyProceduralStatusPose(float deltaTime)
        {
            layer.Restore();
            bool enabled = registry != null &&
                           !IsClipActive &&
                           !interactionHandoffLocked;
            if (!enabled)
            {
                ReleaseBalanceStep();
                ResetPoseFilters();
                layer.Apply(
                    Player3DProceduralLayerInput.Disabled,
                    deltaTime);
                return;
            }

            UpdateBalanceStep();

            // The ambient drunk idle: a slow pelvis/chest roll and spread
            // arms scaled by the status level, on the game clock so a
            // pinned test clock and the real game agree. The balance
            // model's lean (slice 3) lands on top of this additively.
            statusSwayPhase += Mathf.Max(0f, deltaTime) * 1.35f;
            float sway = Mathf.Sin(statusSwayPhase) *
                         intoxicationAmount * 5f;
            float stagger = Mathf.Cos(statusSwayPhase * 0.73f) *
                            intoxicationAmount;
            float lean = balanceLean * 13f;

            // The balance model's lean lands on top: the pelvis rolls
            // toward the capture point, the chest counters a little so
            // the head stays nearer level.
            float modelWeight = balancePose.Weight;
            // The lean carries inertia: the pelvis follows the model's
            // centre of mass through a stiff, well-damped spring, so the
            // clamp letting go at the start of a topple reads as a body
            // continuing to tip, not as a jump to a new angle.
            float modelRoll = leanRollFilter.Advance(
                balancePose.LeanRollDegrees * modelWeight,
                deltaTime);
            float modelPitch = leanPitchFilter.Advance(
                balancePose.LeanPitchDegrees * modelWeight,
                deltaTime);
            // The hip strategy: the chest whips in the sense of the fall
            // the model is fighting, on its own spring.
            float torsoRoll = torsoRollFilter.Advance(
                balancePose.TorsoRollDegrees * modelWeight,
                deltaTime);
            float torsoPitch = torsoPitchFilter.Advance(
                balancePose.TorsoPitchDegrees * modelWeight,
                deltaTime);
            lastModelRoll = modelRoll + torsoRoll;
            lastModelPitch = modelPitch + torsoPitch;

            // The arms go OUT, tightrope fashion: the status level spreads
            // them, the model's reaction throws them wider as the stagger
            // gets worse, a share of the spread is a raise to the front,
            // the arm away from the lean rises higher than the one over
            // it, and the ambient stagger hunts them forward and back
            // against each other. A hand reaching for a wall gives its
            // spread back as the reach takes hold, so the IK blends from
            // a hanging arm and not from a flung one. Every term is
            // exactly zero sober.
            float armSpread = intoxicationAmount * intoxicationAmount *
                              IntoxicationArmSpreadDegrees +
                              balancePose.ArmReaction * modelWeight *
                              BalanceArmReactionDegrees;
            // Only the model's roll steers the asymmetry: it is already
            // weight-gated, whereas the legacy scalar lean is not tied to
            // the status at all.
            float armLean = modelRoll * BalanceArmLeanCoupling +
                            torsoRoll * TorsoArmLeanCoupling;
            float armForward = armSpread * ArmForwardRaiseFraction +
                               Mathf.Abs(torsoPitch) * TorsoArmForwardCoupling;
            float armHunt = stagger * IntoxicationArmHuntDegrees;
            float armFloor = armSpread * MinimumArmSpreadFraction;
            float leftArmOutward = Mathf.Clamp(
                armSpread + armLean,
                armFloor,
                MaximumArmOutwardDegrees);
            float rightArmOutward = Mathf.Clamp(
                armSpread - armLean,
                armFloor,
                MaximumArmOutwardDegrees);
            float leftArmForward = armForward + armHunt;
            float rightArmForward = armForward - armHunt;
            PlayerWallReachPose wallReach = balancePose.WallReach;
            if (wallReach.Active && modelWeight > 0f)
            {
                float free = 1f - wallReach.Weight * modelWeight;
                if (wallReach.RightHand)
                {
                    rightArmOutward *= free;
                    rightArmForward *= free;
                }
                else
                {
                    leftArmOutward *= free;
                    leftArmForward *= free;
                }
            }

            // A hand going out for the ground gives its swing back as the
            // brace takes hold, the wall hand's rule, so the IK blends
            // from a hanging arm and not from a flung one.
            float braceWeight = balancePose.BraceWeight * modelWeight;
            if (braceWeight > 0f)
            {
                if (balancePose.LeftBrace.Active)
                {
                    float free = 1f - balancePose.LeftBrace.Weight * modelWeight;
                    leftArmOutward *= free;
                    leftArmForward *= free;
                }

                if (balancePose.RightBrace.Active)
                {
                    float free = 1f - balancePose.RightBrace.Weight * modelWeight;
                    rightArmOutward *= free;
                    rightArmForward *= free;
                }
            }

            // The arms have mass: each angle is chased through an
            // under-damped spring, so a reaction the model throws at them
            // arrives late, flies past and swings back — the windmill —
            // instead of the hands teleporting to the new angle.
            leftArmOutward = leftArmOutwardFilter.Advance(leftArmOutward, deltaTime);
            leftArmForward = leftArmForwardFilter.Advance(leftArmForward, deltaTime);
            rightArmOutward = rightArmOutwardFilter.Advance(rightArmOutward, deltaTime);
            rightArmForward = rightArmForwardFilter.Advance(rightArmForward, deltaTime);

            // Heavy knees come from lowering the pelvis while both boots
            // are held by the leg solve, so they bend anatomically and
            // asymmetrically when the feet stand on different heights.
            float crouch = crouchFilter.Advance(
                intoxicationAmount * IntoxicationCrouchMetres +
                balancePose.CrouchMetres * modelWeight,
                deltaTime);

            // The drunk walk: the boots land wide, long or short and
            // turned out, the swinging one comes up higher, and the
            // pelvis rolls over the wide stance. Gated by the model's
            // weight like the rest of its pose.
            PlayerDrunkGaitPose gait = balancePose.Gait;
            layer.Apply(
                new Player3DProceduralLayerInput(
                    true,
                    lean + sway + modelRoll + gait.PelvisRollDegrees * modelWeight,
                    (lean * -0.42f) + (sway * 0.55f) +
                    (modelRoll * BalanceChestCounterRoll) +
                    torsoRoll,
                    modelPitch,
                    leftArmOutward,
                    leftArmForward,
                    rightArmOutward,
                    rightArmForward,
                    crouch,
                    footPlantLeft,
                    footPlantRight,
                    runBlend,
                    hasAuthoredRunClip,
                    forwardGaitDominant,
                    balancePose.WallReach,
                    torsoPitch,
                    modelWeight > 0f ? balancePose.LeftBrace : PlayerArmReachPose.None,
                    modelWeight > 0f ? balancePose.RightBrace : PlayerArmReachPose.None,
                    gait.LeftFootOffsetLocal * modelWeight,
                    gait.RightFootOffsetLocal * modelWeight,
                    gait.LeftFootYawDegrees * modelWeight,
                    gait.RightFootYawDegrees * modelWeight,
                    gait.LeftFootLift * modelWeight,
                    gait.RightFootLift * modelWeight),
                deltaTime);
        }

        /// <summary>
        /// The rise's late pass: the rise model's hero-frame targets are
        /// turned into world contacts — each palm on the floor the probe
        /// finds ahead of its shoulder (or on the knee), the lead boot on
        /// the floor where it steps — and the layer draws them on top of
        /// the Rise clip.
        /// </summary>
        private void ApplyRisePose(float deltaTime)
        {
            layer.Restore();
            if (registry == null || actorFacingTransform == null)
            {
                layer.ApplyRise(Player3DRiseLayerInput.Disabled, deltaTime);
                return;
            }

            Vector3 forward = actorFacingTransform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            if (risePose.Stage == PlayerRiseStage.Crawling)
            {
                ApplyCrawlPose(deltaTime, right, forward);
                return;
            }

            ResetCrawlContacts();
            PlayerArmReachPose leftHand = RiseHandTarget(
                false,
                risePose.LeftHandWeight,
                risePose.LeftHandOffsetLocal,
                risePose.LeftHandLift,
                right,
                forward);
            PlayerArmReachPose rightHand = RiseHandTarget(
                true,
                risePose.RightHandWeight,
                risePose.RightHandOffsetLocal,
                risePose.RightHandLift,
                right,
                forward);
            if (risePose.HandOnKnee)
            {
                bool kneeRight = risePose.KneeSide == FootSide.Right;
                Transform shin = kneeRight ? rightShinBone : leftShinBone;
                float weight = kneeRight
                    ? risePose.RightHandWeight
                    : risePose.LeftHandWeight;
                if (shin != null && weight > 0.0001f)
                {
                    var knee = new PlayerArmReachPose(
                        true,
                        kneeRight,
                        shin.position + Vector3.up * RiseKneeHandLift,
                        Vector3.up,
                        weight,
                        0.15f,
                        0.05f);
                    if (kneeRight)
                    {
                        rightHand = knee;
                    }
                    else
                    {
                        leftHand = knee;
                    }
                }
            }

            PlayerRiseStepPose step = risePose.Step;
            Vector3 stepWorld = Vector3.zero;
            if (step.Active)
            {
                stepWorld = actorFacingTransform.position +
                            right * step.TargetLocal.x +
                            forward * step.TargetLocal.y;
                stepWorld.y = ProbeRiseFloor(stepWorld, out _);
            }

            // The knees find the floor the way the hands do: on all fours
            // and in the half-kneel the clip leaves them hanging (its
            // contacts were fitted to another rig), so the hips come down
            // until the resting knee sits on the probed floor.
            float kneeDrop = RestingKneeDrop();
            layer.ApplyRise(
                new Player3DRiseLayerInput(
                    true,
                    leftHand,
                    rightHand,
                    step.Active,
                    step.Side,
                    stepWorld,
                    step.Lift,
                    step.Weight,
                    risePose.PelvisOffsetMetres - kneeDrop,
                    risePose.PelvisRollDegrees,
                    risePose.PelvisPitchDegrees,
                    risePose.HeadLiftDegrees,
                    risePose.LegsWeight),
                deltaTime);
        }

        /// <summary>
        /// How far the hips must come down for the knee that rests on the
        /// floor to reach it, in the stages where a knee rests: both while
        /// he pushes up onto all fours, the trailing one through the
        /// half-kneel, fading out as the standing leg solve takes over.
        /// </summary>
        private float RestingKneeDrop()
        {
            float weight;
            bool left;
            bool rightSide;
            switch (risePose.Stage)
            {
                case PlayerRiseStage.PushingUp:
                    weight = Mathf.Clamp01((risePose.StageProgress - 0.3f) / 0.7f);
                    left = true;
                    rightSide = true;
                    break;
                case PlayerRiseStage.Kneeling:
                    weight = 1f;
                    left = risePose.KneeSide != FootSide.Left;
                    rightSide = risePose.KneeSide != FootSide.Right;
                    break;
                case PlayerRiseStage.Standing:
                    weight = 1f - risePose.LegsWeight;
                    left = risePose.KneeSide != FootSide.Left;
                    rightSide = risePose.KneeSide != FootSide.Right;
                    break;
                default:
                    return 0f;
            }

            if (weight <= 0.0001f)
            {
                return 0f;
            }

            float drop = float.PositiveInfinity;
            if (left && leftShinBone != null)
            {
                drop = Mathf.Min(drop, KneeHeightAboveRest(leftShinBone));
            }

            if (rightSide && rightShinBone != null)
            {
                drop = Mathf.Min(drop, KneeHeightAboveRest(rightShinBone));
            }

            return float.IsPositiveInfinity(drop) ? 0f : Mathf.Max(0f, drop) * weight;
        }

        private float KneeHeightAboveRest(Transform knee)
        {
            float floor = ProbeRiseFloor(knee.position, out _);
            return knee.position.y - floor - PlayerRiseRules.CrawlKneeClearanceMetres;
        }

        /// <summary>One contact of the crawl: where it holds the floor, and whether it was swinging last frame.</summary>
        private struct CrawlContact
        {
            public Vector3 World;
            public bool Valid;
            public bool WasSwinging;
        }

        private const int CrawlLeftHand = 0;
        private const int CrawlRightHand = 1;
        private const int CrawlLeftKnee = 2;
        private const int CrawlRightKnee = 3;
        private readonly CrawlContact[] crawlContacts = new CrawlContact[4];
        private float crawlHipDrop;

        private void ResetCrawlContacts()
        {
            for (int index = 0; index < crawlContacts.Length; index++)
            {
                crawlContacts[index] = default;
            }

            crawlHipDrop = 0f;
        }

        /// <summary>
        /// How far the hips must come down for an arm to reach a hand
        /// spot: the shoulder must be no further from it than the arm is
        /// long (a hair under, so the elbow keeps a bend).
        /// </summary>
        private static float HandHipDrop(Transform upper, Transform forearm, Transform hand, Vector3 handPoint)
        {
            if (upper == null || forearm == null || hand == null)
            {
                return 0f;
            }

            float length = LimbTwoBoneIk.ChainLength(upper, forearm, hand) * 0.96f;
            Vector3 planar = handPoint - upper.position;
            planar.y = 0f;
            float reach = Mathf.Min(planar.magnitude, length * 0.98f);
            float allowed = Mathf.Sqrt(Mathf.Max(0f, length * length - reach * reach));
            float needed = upper.position.y - handPoint.y;
            return Mathf.Max(0f, needed - allowed);
        }

        /// <summary>
        /// The crawl: each hand and knee is a contact planted in the WORLD
        /// — it holds its spot while the body crawls over it — and, when
        /// its turn comes, swings in an arc to its next spot a reach ahead
        /// of its shoulder or hip and plants there. The hips come down so
        /// the planted knee rests on the floor; the thighs are aimed at
        /// the knees and the shins trail flat behind.
        /// </summary>
        private void ApplyCrawlPose(float deltaTime, Vector3 right, Vector3 forward)
        {
            Vector3 leftHandPoint = CrawlLimbTarget(
                CrawlLeftHand,
                risePose.LeftHandCrawl,
                leftUpperArmBone,
                registry.Anchors.LeftGrip != null ? registry.Anchors.LeftGrip.position : leftUpperArmBone.position,
                new Vector2(-PlayerRiseRules.CrawlHandSideMetres, PlayerRiseRules.CrawlHandReachMetres),
                PlayerRiseRules.CrawlHandLiftMetres,
                0f,
                right,
                forward,
                out Vector3 leftNormal);
            Vector3 rightHandPoint = CrawlLimbTarget(
                CrawlRightHand,
                risePose.RightHandCrawl,
                rightUpperArmBone,
                registry.Anchors.RightGrip != null ? registry.Anchors.RightGrip.position : rightUpperArmBone.position,
                new Vector2(PlayerRiseRules.CrawlHandSideMetres, PlayerRiseRules.CrawlHandReachMetres),
                PlayerRiseRules.CrawlHandLiftMetres,
                0f,
                right,
                forward,
                out Vector3 rightNormal);
            Vector3 leftKneePoint = CrawlLimbTarget(
                CrawlLeftKnee,
                risePose.LeftKneeCrawl,
                leftThighBone,
                leftShinBone.position,
                new Vector2(-PlayerRiseRules.CrawlKneeSideMetres, PlayerRiseRules.CrawlKneeReachMetres),
                PlayerRiseRules.CrawlKneeLiftMetres,
                PlayerRiseRules.CrawlKneeClearanceMetres,
                right,
                forward,
                out _);
            Vector3 rightKneePoint = CrawlLimbTarget(
                CrawlRightKnee,
                risePose.RightKneeCrawl,
                rightThighBone,
                rightShinBone.position,
                new Vector2(PlayerRiseRules.CrawlKneeSideMetres, PlayerRiseRules.CrawlKneeReachMetres),
                PlayerRiseRules.CrawlKneeLiftMetres,
                PlayerRiseRules.CrawlKneeClearanceMetres,
                right,
                forward,
                out _);

            // The hips come down until the planted knee, aimed at its spot,
            // lands on it (the thigh's length decides the hip height over
            // a knee that far ahead) and until the planted hands can reach
            // theirs (this rig's arms do not reach the floor from the
            // clip's all-fours shoulders), settling there rather than
            // dropping at once.
            float drop = 0f;
            if (!risePose.LeftKneeCrawl.Swinging)
            {
                drop = Mathf.Max(drop, KneeHipDrop(leftThighBone, leftShinBone, leftKneePoint));
            }

            if (!risePose.RightKneeCrawl.Swinging)
            {
                drop = Mathf.Max(drop, KneeHipDrop(rightThighBone, rightShinBone, rightKneePoint));
            }

            if (!risePose.LeftHandCrawl.Swinging)
            {
                drop = Mathf.Max(drop, HandHipDrop(leftUpperArmBone, leftForearmBone, leftHandBone, leftHandPoint));
            }

            if (!risePose.RightHandCrawl.Swinging)
            {
                drop = Mathf.Max(drop, HandHipDrop(rightUpperArmBone, rightForearmBone, rightHandBone, rightHandPoint));
            }

            drop = Mathf.Min(drop, PlayerRiseRules.CrawlMaximumHipDropMetres);
            crawlHipDrop = Mathf.MoveTowards(
                crawlHipDrop,
                drop,
                Mathf.Max(0f, deltaTime) * PlayerRiseRules.CrawlHipDropRateMetresPerSecond);
            drop = crawlHipDrop;

            layer.ApplyRise(
                new Player3DRiseLayerInput(
                    true,
                    new PlayerArmReachPose(true, false, leftHandPoint + leftNormal * RisePalmClearance, leftNormal, 1f),
                    new PlayerArmReachPose(true, true, rightHandPoint + rightNormal * RisePalmClearance, rightNormal, 1f),
                    false,
                    FootSide.Right,
                    Vector3.zero,
                    0f,
                    0f,
                    risePose.PelvisOffsetMetres - drop,
                    risePose.PelvisRollDegrees,
                    risePose.PelvisPitchDegrees,
                    risePose.HeadLiftDegrees,
                    0f,
                    true,
                    leftKneePoint,
                    1f,
                    true,
                    rightKneePoint,
                    1f),
                deltaTime);
        }

        /// <summary>
        /// Where one crawling limb goes this frame. Planted, it is the
        /// world point it holds (taken where the limb is on the crawl's
        /// first frame, else where its last swing landed). Swinging, it
        /// arcs from that point to its next spot — the reference bone's
        /// ground point plus the reach, followed as the body moves — with
        /// the lift over the middle of the swing.
        /// </summary>
        private Vector3 CrawlLimbTarget(
            int index,
            in PlayerCrawlLimb limb,
            Transform reference,
            Vector3 currentWorld,
            Vector2 reachLocal,
            float lift,
            float restClearance,
            Vector3 right,
            Vector3 forward,
            out Vector3 normal)
        {
            ref CrawlContact contact = ref crawlContacts[index];
            Vector3 origin = reference != null ? reference.position : currentWorld;
            Vector3 destination = origin + right * reachLocal.x + forward * reachLocal.y;
            destination.y = ProbeRiseFloor(destination, out normal) + restClearance;
            if (!contact.Valid)
            {
                Vector3 here = currentWorld;
                here.y = ProbeRiseFloor(here, out _) + restClearance;
                contact.World = here;
                contact.Valid = true;
                contact.WasSwinging = false;
            }

            if (limb.Swinging)
            {
                contact.WasSwinging = true;
                float eased = Mathf.SmoothStep(0f, 1f, limb.Progress);
                Vector3 point = Vector3.Lerp(contact.World, destination, eased);
                point.y += lift * Mathf.Sin(limb.Progress * Mathf.PI);
                return point;
            }

            if (contact.WasSwinging)
            {
                // Landed: this is the spot it holds from now on.
                contact.World = destination;
                contact.WasSwinging = false;
            }

            normal = Vector3.up;
            return contact.World;
        }

        /// <summary>
        /// How far the hips must come down for a thigh aimed at
        /// <paramref name="kneePoint"/> to land its knee there: the hip
        /// must be exactly a thigh's length from the point.
        /// </summary>
        private static float KneeHipDrop(Transform thigh, Transform shin, Vector3 kneePoint)
        {
            if (thigh == null || shin == null)
            {
                return 0f;
            }

            float length = Vector3.Distance(thigh.position, shin.position);
            Vector3 planar = kneePoint - thigh.position;
            planar.y = 0f;
            float reach = Mathf.Min(planar.magnitude, length * 0.98f);
            float allowed = Mathf.Sqrt(Mathf.Max(0f, length * length - reach * reach));
            float needed = thigh.position.y - kneePoint.y;
            return Mathf.Max(0f, needed - allowed);
        }

        private PlayerArmReachPose RiseHandTarget(
            bool rightHand,
            float weight,
            Vector2 offsetLocal,
            float lift,
            Vector3 right,
            Vector3 forward)
        {
            if (weight <= 0.0001f)
            {
                return PlayerArmReachPose.None;
            }

            Transform shoulder = rightHand ? rightUpperArmBone : leftUpperArmBone;
            Vector3 origin = shoulder != null
                ? shoulder.position
                : actorFacingTransform.position + Vector3.up;
            Vector3 point = origin + right * offsetLocal.x + forward * offsetLocal.y;
            point.y = ProbeRiseFloor(point, out Vector3 normal);
            return new PlayerArmReachPose(
                true,
                rightHand,
                point + normal * (RisePalmClearance + Mathf.Max(0f, lift)),
                normal,
                weight,
                0.15f,
                0.05f);
        }

        /// <summary>The floor under a point: its height and normal, or the root's ground when nothing is found.</summary>
        private float ProbeRiseFloor(Vector3 point, out Vector3 normal)
        {
            normal = Vector3.up;
            float floor = actorFacingTransform.position.y;
            int count = Physics.RaycastNonAlloc(
                point + Vector3.up * RiseProbeHeight,
                Vector3.down,
                RiseHits,
                RiseProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = RiseHits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(actorFacingTransform) ||
                    hit.normal.y <= 0.001f ||
                    hit.distance >= closest)
                {
                    continue;
                }

                closest = hit.distance;
                floor = hit.point.y;
                normal = hit.normal.normalized;
            }

            return floor;
        }

        /// <summary>
        /// Every pose filter back to rest at zero: the body a clip or the
        /// ragdoll hands back starts from the clip's own pose, not from
        /// wherever the arms were flying when the layer was switched off.
        /// </summary>
        private void ResetPoseFilters()
        {
            leftArmOutwardFilter.Reset();
            leftArmForwardFilter.Reset();
            rightArmOutwardFilter.Reset();
            rightArmForwardFilter.Reset();
            leanRollFilter.Reset();
            leanPitchFilter.Reset();
            crouchFilter.Reset();
            torsoRollFilter.Reset();
            torsoPitchFilter.Reset();
        }

        private bool balanceStepWasActive;
        private FootSide balanceStepSide;
        private Vector3 balanceStepLandingWorld;

        /// <summary>
        /// Turns the balance model's recovery step into world targets for
        /// the leg layer: the stepping boot arcs to where the model put
        /// it, the stance boot holds its ground for the whole step, and a
        /// boot that has landed stays where it landed until the clip
        /// itself lifts it again.
        /// </summary>
        private void UpdateBalanceStep()
        {
            PlayerBalanceStepPose step = balancePose.Step;
            bool active = step.Active &&
                          balancePose.Weight > 0f &&
                          actorFacingTransform != null;
            if (active)
            {
                Vector3 forward = actorFacingTransform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude > 0.0001f
                    ? forward.normalized
                    : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                float eased = Mathf.SmoothStep(0f, 1f, step.Progress);
                Vector2 local = Vector2.Lerp(
                    step.FromLocal,
                    step.ToLocal,
                    eased);
                Vector3 world = actorFacingTransform.position +
                                right * local.x +
                                forward * local.y;
                float lift = Mathf.Sin(step.Progress * Mathf.PI) * step.Lift;
                layer.SetStepTarget(step.Side, world, lift);
                balanceStepLandingWorld = actorFacingTransform.position +
                                          right * step.ToLocal.x +
                                          forward * step.ToLocal.y;

                bool stepStarted = !balanceStepWasActive ||
                                   balanceStepSide != step.Side;
                if (stepStarted)
                {
                    // The stance boot holds the ground it was LAST solved
                    // onto — the raw clip pose under it may be centimetres
                    // away, and a lock taken there would jump the boot.
                    FootSide stance = step.Side == FootSide.Left
                        ? FootSide.Right
                        : FootSide.Left;
                    Transform stanceFoot = stance == FootSide.Left
                        ? leftFootBone
                        : rightFootBone;
                    layer.ReleaseFoot(step.Side);
                    if (layer.TryGetLastAnklePosition(
                            stance,
                            out Vector3 stancePosition))
                    {
                        layer.LockFoot(stance, stancePosition);
                    }
                    else if (stanceFoot != null)
                    {
                        layer.LockFoot(stance, stanceFoot.position);
                    }
                }

                balanceStepWasActive = true;
                balanceStepSide = step.Side;
                return;
            }

            if (balanceStepWasActive)
            {
                // Landed: the boot keeps the spot the step put it on until
                // the clip swings it, and the stance boot is free again.
                layer.ClearStepTarget();
                layer.ReleaseFoot(FootSide.Left);
                layer.ReleaseFoot(FootSide.Right);
                if (balancePose.Weight > 0f)
                {
                    layer.LockFoot(balanceStepSide, balanceStepLandingWorld);
                }

                balanceStepWasActive = false;
            }

            if (layer.IsFootLocked(FootSide.Left) && footPlantLeft < 0.5f)
            {
                layer.ReleaseFoot(FootSide.Left);
            }

            if (layer.IsFootLocked(FootSide.Right) && footPlantRight < 0.5f)
            {
                layer.ReleaseFoot(FootSide.Right);
            }

            if (balancePose.Weight <= 0f)
            {
                layer.ReleaseFoot(FootSide.Left);
                layer.ReleaseFoot(FootSide.Right);
            }
        }

        private void ReleaseBalanceStep()
        {
            layer.ClearStepTarget();
            layer.ReleaseFoot(FootSide.Left);
            layer.ReleaseFoot(FootSide.Right);
            balanceStepWasActive = false;
        }

        private void CaptureStatusBones()
        {
            pelvisBone = registry.Anchors.Pelvis;
            chestBone = registry.Anchors.Chest;
            headBone = registry.Anchors.Head;
            neckBone = GetPartBone(Player3DAnatomicalPart.Neck);
            leftUpperArmBone = GetPartBone(
                Player3DAnatomicalPart.LeftUpperArm);
            rightUpperArmBone = GetPartBone(
                Player3DAnatomicalPart.RightUpperArm);
            leftThighBone = GetPartBone(
                Player3DAnatomicalPart.LeftThigh);
            rightThighBone = GetPartBone(
                Player3DAnatomicalPart.RightThigh);
            leftForearmBone = GetPartBone(
                Player3DAnatomicalPart.LeftForearm);
            rightForearmBone = GetPartBone(
                Player3DAnatomicalPart.RightForearm);
            leftHandBone = GetPartBone(
                Player3DAnatomicalPart.LeftHand);
            rightHandBone = GetPartBone(
                Player3DAnatomicalPart.RightHand);
            leftShinBone = GetPartBone(
                Player3DAnatomicalPart.LeftShin);
            rightShinBone = GetPartBone(
                Player3DAnatomicalPart.RightShin);
            leftFootBone = registry.Anchors.LeftFoot != null
                ? registry.Anchors.LeftFoot
                : GetPartBone(Player3DAnatomicalPart.LeftFoot);
            rightFootBone = registry.Anchors.RightFoot != null
                ? registry.Anchors.RightFoot
                : GetPartBone(Player3DAnatomicalPart.RightFoot);
        }

        private Transform GetPartBone(Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) &&
                   binding != null
                ? binding.Bone
                : null;
        }

        private void CaptureFacialBones()
        {
            leftEye = CaptureBone("face.eye.L");
            rightEye = CaptureBone("face.eye.R");
            leftBrow = CaptureBone("face.brow.L");
            rightBrow = CaptureBone("face.brow.R");
            mouth = CaptureBone("face.mouth");
        }

        private FacialBoneRest CaptureBone(string boneName)
        {
            IReadOnlyList<Player3DMeshBinding> bindings =
                registry.MeshBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding != null &&
                    binding.BoneName == boneName &&
                    binding.Bone != null)
                {
                    return new FacialBoneRest(binding.Bone);
                }
            }

            return default;
        }

        private void ApplyFacialPose()
        {
            // The fall's clips no longer own the face: the moment does —
            // the wince of the floor, the blank of the lie, the strain of
            // the push-up — read from what the presentation already
            // holds, so the drink's faces and the fall's are one thing.
            if (IsClipActive && activeClipOwner != ClipOwner.Fall)
            {
                ApplyAuthoredClipFacialPose();
                return;
            }

            bool allowIdleExpressions =
                !interactionHandoffLocked &&
                locomotionBlend < MotionThreshold &&
                intoxicationAmount < PlayerFacialAnimationState.DrowsyLevel &&
                Mathf.Abs(balanceLean) < 0.001f &&
                fallAmount <= 0.001f &&
                !ragdollPoseActive &&
                !risePose.Active;
            float modelWeight = balancePose.Weight;
            PlayerFacialMood mood = PlayerFacialMoodRules.Resolve(
                new PlayerFacialMoodContext(
                    intoxicationAmount,
                    modelWeight > 0f ? balancePose.Phase : BalancePhase.Steady,
                    balancePose.BraceWeight * modelWeight,
                    balancePose.Instability * modelWeight,
                    ragdollPoseActive,
                    ragdollPoseActive ? Time.time - ragdollPoseSince : 0f,
                    risePose.Active,
                    risePose.Stage,
                    risePose.StageProgress,
                    risePose.SlumpActive));
            PlayerFacialExpression expression = facialState.Advance(
                Time.deltaTime,
                allowIdleExpressions,
                intoxicationAmount,
                mood);

            ApplyFacialExpression(expression);
        }

        /// <summary>The mood the face is being asked for this frame, for probes.</summary>
        public PlayerFacialMood CurrentFacialMood =>
            PlayerFacialMoodRules.Resolve(
                new PlayerFacialMoodContext(
                    intoxicationAmount,
                    balancePose.Weight > 0f ? balancePose.Phase : BalancePhase.Steady,
                    balancePose.BraceWeight * balancePose.Weight,
                    balancePose.Instability * balancePose.Weight,
                    ragdollPoseActive,
                    ragdollPoseActive ? Time.time - ragdollPoseSince : 0f,
                    risePose.Active,
                    risePose.Stage,
                    risePose.StageProgress,
                    risePose.SlumpActive));

        private float ragdollPoseSince;

        private void ReapplyFacialPose()
        {
            if (IsClipActive)
            {
                ApplyAuthoredClipFacialPose();
                return;
            }

            ApplyFacialExpression(facialState.CurrentExpression);
        }

        private void ApplyAuthoredClipFacialPose()
        {
            // Legacy clips already own their keyed face bones. Atlas faces
            // use the same authored priority through optional clip keys.
            if (!UsesFacialAtlas)
            {
                visibleFacialExpression = ReadLegacyFacialExpression();
                return;
            }

            PlayerFacialExpression expression =
                PlayerFacialExpression.Neutral;
            activeClipBinding?.TryGetFacialExpression(
                activeClipNormalizedTime,
                out expression);
            visibleFacialExpression = expression;
            ApplyFacialExpression(expression);
        }

        private PlayerFacialExpression ReadLegacyFacialExpression()
        {
            float eyeScale = Mathf.Min(
                leftEye.ScaleYFactor,
                rightEye.ScaleYFactor);
            if (eyeScale <= 0.16f)
            {
                return PlayerFacialExpression.ClosedBlink;
            }

            bool tense =
                leftBrow.RotationDeltaDegrees >= 7f ||
                rightBrow.RotationDeltaDegrees >= 7f ||
                mouth.ScaleXFactor <= 0.92f;
            if (tense)
            {
                return PlayerFacialExpression.Tense;
            }

            if (eyeScale >= 1.09f)
            {
                return PlayerFacialExpression.Watchful;
            }

            return eyeScale < 0.90f
                ? PlayerFacialExpression.HalfBlink
                : PlayerFacialExpression.Neutral;
        }

        private void ApplyFacialExpression(
            PlayerFacialExpression expression)
        {
            visibleFacialExpression = expression;
            if (faceAtlasPresenter.Apply(expression))
            {
                return;
            }

            RestoreFacialBones();

            switch (expression)
            {
                case PlayerFacialExpression.HalfBlink:
                    leftEye.ScaleY(0.48f);
                    rightEye.ScaleY(0.48f);
                    break;
                case PlayerFacialExpression.ClosedBlink:
                    leftEye.ScaleY(0.08f);
                    rightEye.ScaleY(0.08f);
                    break;
                case PlayerFacialExpression.Watchful:
                    leftEye.ScaleY(1.18f);
                    rightEye.ScaleY(1.18f);
                    leftBrow.RotateZ(-5f);
                    rightBrow.RotateZ(5f);
                    break;
                case PlayerFacialExpression.Tense:
                    leftEye.ScaleY(0.72f);
                    rightEye.ScaleY(0.72f);
                    leftBrow.RotateZ(12f);
                    rightBrow.RotateZ(-12f);
                    mouth.ScaleX(0.82f);
                    break;
                // Bone-faced registries use the same shapes as the atlas.
                case PlayerFacialExpression.Drowsy:
                    leftEye.ScaleY(0.4f);
                    rightEye.ScaleY(0.4f);
                    leftBrow.RotateZ(4f);
                    rightBrow.RotateZ(-4f);
                    break;
                case PlayerFacialExpression.Glazed:
                    leftEye.ScaleY(1.0f);
                    rightEye.ScaleY(0.8f);
                    break;
                case PlayerFacialExpression.Slack:
                    leftEye.ScaleY(0.7f);
                    rightEye.ScaleY(0.7f);
                    leftBrow.RotateZ(-6f);
                    rightBrow.RotateZ(-2f);
                    mouth.ScaleX(0.9f);
                    break;
                case PlayerFacialExpression.Grimace:
                    leftEye.ScaleY(0.45f);
                    rightEye.ScaleY(0.45f);
                    leftBrow.RotateZ(15f);
                    rightBrow.RotateZ(-15f);
                    mouth.ScaleX(0.78f);
                    break;
            }
        }

        private void ResetFacialPresentation()
        {
            visibleFacialExpression = PlayerFacialExpression.Neutral;
            if (!faceAtlasPresenter.Apply(visibleFacialExpression))
            {
                RestoreFacialBones();
            }
        }

        private void RestoreFacialBones()
        {
            leftEye.Restore();
            rightEye.Restore();
            leftBrow.Restore();
            rightBrow.Restore();
            mouth.Restore();
        }

        private void ConfigureWorldRenderers()
        {
            IReadOnlyList<Renderer> renderers = registry.Renderers;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    // Context clips are imported from a separate FBX, so the
                    // model FBX cannot bake their lying/falling poses into
                    // its renderer bounds. Recalculate them while animated
                    // to keep every modular body part visible.
                    skinnedRenderer.updateWhenOffscreen = true;
                }
            }
        }

        private void CaptureClipSpatialState()
        {
            if (clipSpatialStateCaptured ||
                registry == null ||
                registry.ModelRoot == null)
            {
                return;
            }

            Transform modelRoot = registry.ModelRoot;
            clipModelLocalPosition = modelRoot.localPosition;
            clipModelLocalRotation = modelRoot.localRotation;
            clipModelLocalScale = modelRoot.localScale;
            clipSpatialStateCaptured = true;
        }

        private void EvaluateGraph(float deltaTime)
        {
            // Both additive layers come off before the graph writes the
            // frame: restoring them in LateUpdate instead would roll the
            // freshly evaluated head/neck animation back to a stale base
            // and freeze it for as long as the additive stays engaged.
            layer.Restore();
            RestoreAttentionPoseBase();
            if (graph.IsValid())
            {
                graph.Evaluate(Mathf.Max(0f, deltaTime));
            }
        }

        private void DestroyGraph()
        {
            layer.Restore();
            RestoreAttentionPoseBase();
            ResetClipSpatialOffset();
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            activeClipPlayable = default;
            activeClipBinding = null;
            activeClipOwner = ClipOwner.None;
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

        private Vector3 GetAnchorPosition(Transform anchor)
        {
            return anchor != null
                ? anchor.position
                : actorFacingTransform != null
                    ? actorFacingTransform.position
                    : transform.position;
        }

        private readonly struct FacialBoneRest
        {
            private readonly Transform bone;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;
            private readonly Vector3 localScale;

            public FacialBoneRest(Transform transformToCapture)
            {
                bone = transformToCapture;
                localPosition = bone.localPosition;
                localRotation = bone.localRotation;
                localScale = bone.localScale;
            }

            public void Restore()
            {
                if (bone == null)
                {
                    return;
                }

                bone.localPosition = localPosition;
                bone.localRotation = localRotation;
                bone.localScale = localScale;
            }

            public float ScaleXFactor =>
                bone != null && Mathf.Abs(localScale.x) > 0.0001f
                    ? bone.localScale.x / localScale.x
                    : 1f;

            public float ScaleYFactor =>
                bone != null && Mathf.Abs(localScale.y) > 0.0001f
                    ? bone.localScale.y / localScale.y
                    : 1f;

            public float RotationDeltaDegrees =>
                bone != null
                    ? Quaternion.Angle(localRotation, bone.localRotation)
                    : 0f;

            public void ScaleX(float factor)
            {
                if (bone != null)
                {
                    bone.localScale = new Vector3(
                        localScale.x * factor,
                        localScale.y,
                        localScale.z);
                }
            }

            public void ScaleY(float factor)
            {
                if (bone != null)
                {
                    bone.localScale = new Vector3(
                        localScale.x,
                        localScale.y * factor,
                        localScale.z);
                }
            }

            public void RotateZ(float degrees)
            {
                if (bone != null)
                {
                    bone.localRotation = localRotation *
                        Quaternion.AngleAxis(degrees, Vector3.forward);
                }
            }
        }
    }
}
