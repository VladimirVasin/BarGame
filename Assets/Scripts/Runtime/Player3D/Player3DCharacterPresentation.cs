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
        IPlayerBalancePresentation
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

        // The balance model's arm reaction at full instability, and how
        // much of its pelvis roll the chest counters so the head stays
        // nearer level than the hips.
        private const float BalanceArmReactionDegrees = 35f;
        private const float BalanceChestCounterRoll = -0.35f;

        // Turn-in-place engages only while the feet are effectively
        // stationary and the player is clearly holding a yaw input.
        private const float TurnInPlaceSpeedThreshold = 0.25f;
        private const float TurnInPlaceInputThreshold = 0.2f;

        // The authored Run loop is 18 frames at 24 fps. Keeping this
        // production cadence when V1 falls back to Walk makes the old rig
        // visibly hurry instead of silently dropping the run request.
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
        private float footPlantAmount = 1f;
        private float footPlantLeft = 1f;
        private float footPlantRight = 1f;
        private bool forwardGaitDominant;
        private float statusSwayPhase;
        private PlayerBalancePose balancePose = PlayerBalancePose.Neutral;
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
                fallDirection);
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

        /// <summary>What the ground probe under each boot found this frame.</summary>
        public FootGroundSample LeftFootGround =>
            layer.GetSample(FootSide.Left);
        public FootGroundSample RightFootGround =>
            layer.GetSample(FootSide.Right);

        /// <summary>How far the leg solve has faded in (<c>0..1</c>).</summary>
        public float FootIkBlend => layer.IkBlend;

        /// <summary>The pelvis offset the leg layer applied this frame.</summary>
        public float PelvisDrop => layer.LastPelvisDrop;
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
                !TryResolveAnimation("TurnLeft", out turnLeftBinding) ||
                !TryResolveAnimation("TurnRight", out turnRightBinding))
            {
                throw new InvalidOperationException(
                    "The Player3D registry requires the Idle, Walk, " +
                    "WalkBack, TurnLeft and TurnRight clips.");
            }

            hasAuthoredRunClip = TryResolveAnimation(
                "Run",
                out runBinding);
            if (!hasAuthoredRunClip)
            {
                // Hero V1 is a byte-frozen fallback and deliberately keeps
                // its original 37-action bank. Give it a separately timed
                // Walk playable so the ordinary run state remains safe.
                runBinding = walkBinding;
            }

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

            Player3DLocomotionState state = Player3DLocomotionState.Idle;
            if (!interactionHandoffLocked)
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

        public void SetFallPose(float signedDirection, float amount)
        {
            if (!Mathf.Approximately(signedDirection, 0f))
            {
                fallDirection = Mathf.Sign(signedDirection);
            }

            fallAmount = Mathf.Clamp01(amount);
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
                RestoreFacialBones();
                SetFootPlant(0.25f, 0.25f, 0.25f, false);
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
                ApplyProceduralStatusPose(Time.deltaTime);
                ApplyFacialPose();
                ApplyAttentionPose(Time.deltaTime);
            }

            if (releaseInteractionHandoffAfterLateUpdate)
            {
                interactionHandoffLocked = false;
                releaseInteractionHandoffAfterLateUpdate = false;
            }
        }

        internal void ReapplyLatePresentationPose()
        {
            // A deterministic seam for checks that run in batch mode, where
            // WaitForEndOfFrame is not dispatched. It reapplies the current
            // visible pose after the manual graph has evaluated without
            // advancing any presentation state a second time.
            if (!ragdollPoseActive)
            {
                ApplyProceduralStatusPose(0f);
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
            bool allowed = registry != null &&
                           headBone != null &&
                           !IsClipActive &&
                           !interactionHandoffLocked &&
                           !ragdollPoseActive &&
                           attentionFocus.HasValue;
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

            if (attentionWeight <= 0.0001f)
            {
                return;
            }

            float yaw = AttentionYawSign *
                        attentionYaw *
                        attentionWeight;
            float pitch = AttentionPitchSign *
                          attentionPitch *
                          attentionWeight;
            CaptureAttentionPoseBase();
            if (neckBone != null)
            {
                neckBone.localRotation *= Quaternion.Euler(
                    pitch * AttentionNeckShare,
                    yaw * AttentionNeckShare,
                    0f);
            }

            if (headBone != null)
            {
                headBone.localRotation *= Quaternion.Euler(
                    pitch * AttentionHeadShare,
                    yaw * AttentionHeadShare,
                    0f);
            }
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
            float armSpread = intoxicationAmount * 9f;

            // The balance model's lean lands on top: the pelvis rolls
            // toward the capture point, the chest counters a little so
            // the head stays nearer level, and the arms go out as the
            // stagger gets worse.
            float modelWeight = balancePose.Weight;
            float modelRoll = balancePose.LeanRollDegrees * modelWeight;
            float modelPitch = balancePose.LeanPitchDegrees * modelWeight;
            float modelArms = balancePose.ArmReaction * modelWeight *
                              BalanceArmReactionDegrees;

            // Heavy knees come from lowering the pelvis while both boots
            // are held by the leg solve, so they bend anatomically and
            // asymmetrically when the feet stand on different heights.
            float crouch = intoxicationAmount * IntoxicationCrouchMetres +
                           balancePose.CrouchMetres * modelWeight;

            layer.Apply(
                new Player3DProceduralLayerInput(
                    true,
                    lean + sway + modelRoll,
                    (lean * -0.42f) + (sway * 0.55f) +
                    (modelRoll * BalanceChestCounterRoll),
                    modelPitch,
                    armSpread + (stagger * 2f) + modelArms,
                    -armSpread + (stagger * 2f) - modelArms,
                    crouch,
                    footPlantLeft,
                    footPlantRight,
                    runBlend,
                    hasAuthoredRunClip,
                    forwardGaitDominant,
                    balancePose.WallReach),
                deltaTime);
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
            if (IsClipActive)
            {
                ApplyAuthoredClipFacialPose();
                return;
            }

            bool allowIdleExpressions =
                !interactionHandoffLocked &&
                locomotionBlend < MotionThreshold &&
                intoxicationAmount < 0.35f &&
                Mathf.Abs(balanceLean) < 0.001f &&
                fallAmount <= 0.001f;
            PlayerFacialExpression expression = facialState.Advance(
                Time.deltaTime,
                allowIdleExpressions);

            ApplyFacialExpression(expression);
        }

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
