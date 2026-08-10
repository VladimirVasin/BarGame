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
        Walk
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
        IPlayerClipPresentation
    {
        public const float FullWalkSpeed = 2.6f;

        private const float LocomotionBlendInTime = 0.14f;
        private const float LocomotionBlendOutTime = 0.20f;
        private const float MotionThreshold = 0.05f;
        private const float StatusBlendSpeed = 4.5f;
        private const float BodyRadius = 0.32f;

        private enum ClipOwner
        {
            None = 0,
            External,
            Fall
        }

        private readonly PlayerFacialAnimationState facialState =
            new PlayerFacialAnimationState();

        private Player3DAssetRegistry registry;
        private Transform actorFacingTransform;
        private PlayableGraph graph;
        private AnimationMixerPlayable locomotionMixer;
        private AnimationLayerMixerPlayable layerMixer;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable walkPlayable;
        private AnimationClipPlayable activeClipPlayable;
        private Player3DAnimationBinding idleBinding;
        private Player3DAnimationBinding walkBinding;
        private Player3DAnimationBinding activeClipBinding;
        private ClipOwner activeClipOwner;
        private Vector3 clipModelLocalPosition;
        private Quaternion clipModelLocalRotation;
        private Vector3 clipModelLocalScale;
        private bool clipSpatialStateCaptured;
        private float targetLocomotionBlend;
        private float locomotionBlend;
        private float locomotionBlendVelocity;
        private float planarSpeed;
        private float intoxicationTarget;
        private float intoxicationAmount;
        private float balanceLeanTarget;
        private float balanceLean;
        private float fallAmount;
        private float fallDirection = 1f;
        private float footPlantAmount = 1f;
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
        private ProceduralStatusPoseBase proceduralStatusPoseBase;
        private bool proceduralStatusPoseBaseCaptured;
        private FootGroundingProbe footGroundingProbe;
        private float groundedFootHeightOffset;
        private bool groundedFootHeightOffsetCaptured;

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
        public float PlanarSpeed => planarSpeed;
        public float IntoxicationAmount => intoxicationAmount;
        public float BalanceLean => balanceLean;
        public float FallAmount => fallAmount;
        public float FallDirection => fallDirection;
        public bool RagdollPoseActive => ragdollPoseActive;
        public PlayerFacialExpression CurrentFacialExpression =>
            facialState.CurrentExpression;
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
                !TryResolveAnimation("Walk", out walkBinding))
            {
                throw new InvalidOperationException(
                    "The Player3D registry requires Idle and Walk clips.");
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null;
            ConfigureWorldRenderers();
            BuildGraph(animator);
            CaptureStatusBones();
            CaptureFacialBones();
            facialState.Reset();
            SetMotion(Vector3.zero);
            ApplyLocomotionWeights(immediate: true);
            EvaluateGraph(0f);
            footGroundingProbe = FootGroundingProbe.Create(registry);
            CaptureGroundedFootHeightOffset();
        }

        public void SetMotion(Vector3 planarVelocity)
        {
            planarVelocity.y = 0f;
            planarSpeed = planarVelocity.magnitude;
            targetLocomotionBlend = interactionHandoffLocked
                ? 0f
                : Mathf.Clamp01(planarSpeed / FullWalkSpeed);
            CurrentLocomotionState =
                targetLocomotionBlend >= MotionThreshold
                    ? Player3DLocomotionState.Walk
                    : Player3DLocomotionState.Idle;
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
            targetLocomotionBlend = 0f;
            locomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            footPlantAmount = 1f;
            CurrentLocomotionState = Player3DLocomotionState.Idle;
            facialState.Reset();
            ApplyLocomotionWeights(immediate: true);
            if (!IsClipActive && idlePlayable.IsValid())
            {
                idlePlayable.SetTime(0d);
                EvaluateGraph(0f);
            }
        }

        public void SetIntoxication(float intensity)
        {
            intoxicationTarget = Mathf.Clamp01(intensity);
        }

        public void SetBalancePose(float signedLean)
        {
            balanceLeanTarget = Mathf.Clamp(signedLean, -1f, 1f);
        }

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

            RestoreProceduralStatusPoseBase();
            ragdollPoseActive = active;
            if (active)
            {
                RestoreFacialBones();
                footPlantAmount = 0.25f;
            }
            else
            {
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
            double sampleTime = activeClipBinding.Clip.length * progress;
            activeClipPlayable.SetTime(sampleTime);
            EvaluateGraph(0f);
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
                ResetClipSpatialOffset();
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
            layerMixer.SetInputWeight(0, 1f);
            layerMixer.SetInputWeight(1, 0f);
            ApplyLocomotionWeights(immediate: true);
            EvaluateGraph(0f);
            ResetClipSpatialOffset();
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
                footPlantAmount = 0.25f;
                return;
            }

            if (!IsClipActive)
            {
                ApplyLocomotionWeights(immediate: false);
                double speed = Mathf.Lerp(
                    0.78f,
                    1.12f,
                    locomotionBlend);
                walkPlayable.SetSpeed(speed);
                EvaluateGraph(deltaTime);
                UpdateFootPlant();
            }
            else
            {
                EvaluateGraph(0f);
                footPlantAmount = Mathf.Lerp(1f, 0.35f, fallAmount);
            }
        }

        private void LateUpdate()
        {
            if (!ragdollPoseActive)
            {
                ApplyProceduralStatusPose();
                ApplyFacialPose();
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
                ApplyProceduralStatusPose();
                ApplyFacialExpression(facialState.CurrentExpression);
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
            footPlantAmount = 1f;
            ragdollPoseActive = false;
            planarSpeed = 0f;
            targetLocomotionBlend = 0f;
            locomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            CurrentLocomotionState = Player3DLocomotionState.Idle;
            facialState.Reset();

            if (activeClipBinding != null)
            {
                EndClip();
            }
            else if (graph.IsValid())
            {
                ApplyLocomotionWeights(immediate: true);
                if (idlePlayable.IsValid())
                {
                    idlePlayable.SetTime(0d);
                }

                EvaluateGraph(0f);
            }

            RestoreFacialBones();
        }

        private void OnDestroy()
        {
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
            locomotionMixer = AnimationMixerPlayable.Create(graph, 2);
            layerMixer = AnimationLayerMixerPlayable.Create(graph, 2);
            idlePlayable = AnimationClipPlayable.Create(
                graph,
                idleBinding.Clip);
            walkPlayable = AnimationClipPlayable.Create(
                graph,
                walkBinding.Clip);
            idlePlayable.SetApplyFootIK(false);
            idlePlayable.SetApplyPlayableIK(false);
            walkPlayable.SetApplyFootIK(false);
            walkPlayable.SetApplyPlayableIK(false);
            graph.Connect(idlePlayable, 0, locomotionMixer, 0);
            graph.Connect(walkPlayable, 0, locomotionMixer, 1);
            graph.Connect(locomotionMixer, 0, layerMixer, 0);
            locomotionMixer.SetInputWeight(0, 1f);
            locomotionMixer.SetInputWeight(1, 0f);
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

        private void ApplyLocomotionWeights(bool immediate)
        {
            if (!locomotionMixer.IsValid())
            {
                return;
            }

            if (immediate)
            {
                locomotionBlend = targetLocomotionBlend;
                locomotionBlendVelocity = 0f;
            }
            else
            {
                float smoothTime = targetLocomotionBlend > locomotionBlend
                    ? LocomotionBlendInTime
                    : LocomotionBlendOutTime;
                locomotionBlend = Mathf.SmoothDamp(
                    locomotionBlend,
                    targetLocomotionBlend,
                    ref locomotionBlendVelocity,
                    smoothTime,
                    Mathf.Infinity,
                    Mathf.Max(0f, Time.deltaTime));
                if (Mathf.Abs(
                        locomotionBlend - targetLocomotionBlend) < 0.0005f)
                {
                    locomotionBlend = targetLocomotionBlend;
                    locomotionBlendVelocity = 0f;
                }
            }

            locomotionMixer.SetInputWeight(0, 1f - locomotionBlend);
            locomotionMixer.SetInputWeight(1, locomotionBlend);
        }

        private void UpdateFootPlant()
        {
            if (!walkPlayable.IsValid() ||
                walkBinding == null ||
                walkBinding.Clip.length <= 0.0001f)
            {
                footPlantAmount = 1f;
                return;
            }

            float cycle = (float)(walkPlayable.GetTime() /
                                  walkBinding.Clip.length);
            float planted = Mathf.Abs(Mathf.Cos(cycle * Mathf.PI * 2f));
            footPlantAmount = Mathf.Lerp(
                1f,
                Mathf.Lerp(0.68f, 1f, planted),
                locomotionBlend);
        }

        private void ApplyProceduralStatusPose()
        {
            RestoreProceduralStatusPoseBase();
            if (registry == null || IsClipActive ||
                interactionHandoffLocked)
            {
                return;
            }

            CaptureProceduralStatusPoseBase();
            float phase = Time.unscaledTime * 1.35f;
            float sway = Mathf.Sin(phase) *
                         intoxicationAmount * 5f;
            float stagger = Mathf.Cos(phase * 0.73f) *
                            intoxicationAmount;
            float lean = balanceLean * 13f;
            if (pelvisBone != null)
            {
                // Keep the authored horizontal pelvis anchor. Procedural
                // translation here moves the whole scale-100 imported rig.
                pelvisBone.localRotation *= Quaternion.AngleAxis(
                    lean + sway,
                    Vector3.forward);
            }

            if (chestBone != null)
            {
                chestBone.localRotation *= Quaternion.AngleAxis(
                    (lean * -0.42f) + (sway * 0.55f),
                    Vector3.forward);
            }

            float armSpread = intoxicationAmount * 9f;
            RotateBone(
                leftUpperArmBone,
                Vector3.forward,
                armSpread + (stagger * 2f));
            RotateBone(
                rightUpperArmBone,
                Vector3.forward,
                -armSpread + (stagger * 2f));

            float kneeBend = intoxicationAmount * 7f;
            RotateBone(leftThighBone, Vector3.right, -kneeBend);
            RotateBone(rightThighBone, Vector3.right, -kneeBend);
            RotateBone(leftShinBone, Vector3.right, kneeBend * 1.35f);
            RotateBone(rightShinBone, Vector3.right, kneeBend * 1.35f);

            GroundOrdinaryPose();
        }

        private void CaptureStatusBones()
        {
            pelvisBone = registry.Anchors.Pelvis;
            chestBone = registry.Anchors.Chest;
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
            proceduralStatusPoseBaseCaptured = false;
            footGroundingProbe = null;
            groundedFootHeightOffsetCaptured = false;
        }

        private void CaptureProceduralStatusPoseBase()
        {
            proceduralStatusPoseBase = new ProceduralStatusPoseBase(
                pelvisBone,
                chestBone,
                leftUpperArmBone,
                rightUpperArmBone,
                leftThighBone,
                rightThighBone,
                leftShinBone,
                rightShinBone);
            proceduralStatusPoseBaseCaptured = true;
        }

        private void RestoreProceduralStatusPoseBase()
        {
            if (!proceduralStatusPoseBaseCaptured)
            {
                return;
            }

            proceduralStatusPoseBase.Restore();
            proceduralStatusPoseBaseCaptured = false;
        }

        private bool TryGetLowestFootHeight(out float height)
        {
            height = float.PositiveInfinity;
            if (registry == null)
            {
                return false;
            }

            IncludeFootHeight(registry.Anchors.LeftFoot, ref height);
            IncludeFootHeight(registry.Anchors.RightFoot, ref height);
            return !float.IsPositiveInfinity(height);
        }

        private void CaptureGroundedFootHeightOffset()
        {
            groundedFootHeightOffsetCaptured = false;
            if (actorFacingTransform == null ||
                !TryGetGroundingHeight(out float footHeight))
            {
                return;
            }

            groundedFootHeightOffset =
                footHeight - actorFacingTransform.position.y;
            groundedFootHeightOffsetCaptured = true;
        }

        private void GroundOrdinaryPose()
        {
            if (pelvisBone == null ||
                actorFacingTransform == null ||
                !groundedFootHeightOffsetCaptured ||
                !TryGetGroundingHeight(out float footHeight))
            {
                return;
            }

            // The CharacterController owns the grounded actor root, while
            // Walk, Idle and intoxication are bone-only presentation. Pin the
            // lower registered foot to its neutral height so gait, sway and
            // knee bend cannot push the visible rig through the floor.
            float targetHeight = actorFacingTransform.position.y +
                                 groundedFootHeightOffset;
            pelvisBone.position += Vector3.up *
                (targetHeight - footHeight);
        }

        private bool TryGetGroundingHeight(out float height)
        {
            if (footGroundingProbe != null &&
                footGroundingProbe.TryGetLowestHeight(out height))
            {
                return true;
            }

            return TryGetLowestFootHeight(out height);
        }

        private static void IncludeFootHeight(
            Transform foot,
            ref float height)
        {
            if (foot != null && IsFinite(foot.position))
            {
                height = Mathf.Min(height, foot.position.y);
            }
        }

        private Transform GetPartBone(Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) &&
                   binding != null
                ? binding.Bone
                : null;
        }

        private static void RotateBone(
            Transform bone,
            Vector3 localAxis,
            float degrees)
        {
            if (bone != null)
            {
                bone.localRotation *= Quaternion.AngleAxis(
                    degrees,
                    localAxis);
            }
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
            bool allowIdleExpressions =
                !IsClipActive &&
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

        private void ApplyFacialExpression(
            PlayerFacialExpression expression)
        {

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
            RestoreProceduralStatusPoseBase();
            if (graph.IsValid())
            {
                graph.Evaluate(Mathf.Max(0f, deltaTime));
            }
        }

        private void DestroyGraph()
        {
            RestoreProceduralStatusPoseBase();
            ResetClipSpatialOffset();
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            activeClipPlayable = default;
            activeClipBinding = null;
            activeClipOwner = ClipOwner.None;
        }

        private readonly struct ProceduralStatusPoseBase
        {
            private readonly BoneLocalPose pelvis;
            private readonly BoneLocalPose chest;
            private readonly BoneLocalPose leftUpperArm;
            private readonly BoneLocalPose rightUpperArm;
            private readonly BoneLocalPose leftThigh;
            private readonly BoneLocalPose rightThigh;
            private readonly BoneLocalPose leftShin;
            private readonly BoneLocalPose rightShin;

            public ProceduralStatusPoseBase(
                Transform pelvisBone,
                Transform chestBone,
                Transform leftUpperArmBone,
                Transform rightUpperArmBone,
                Transform leftThighBone,
                Transform rightThighBone,
                Transform leftShinBone,
                Transform rightShinBone)
            {
                pelvis = new BoneLocalPose(pelvisBone);
                chest = new BoneLocalPose(chestBone);
                leftUpperArm = new BoneLocalPose(leftUpperArmBone);
                rightUpperArm = new BoneLocalPose(rightUpperArmBone);
                leftThigh = new BoneLocalPose(leftThighBone);
                rightThigh = new BoneLocalPose(rightThighBone);
                leftShin = new BoneLocalPose(leftShinBone);
                rightShin = new BoneLocalPose(rightShinBone);
            }

            public void Restore()
            {
                pelvis.Restore();
                chest.Restore();
                leftUpperArm.Restore();
                rightUpperArm.Restore();
                leftThigh.Restore();
                rightThigh.Restore();
                leftShin.Restore();
                rightShin.Restore();
            }
        }

        private sealed class FootGroundingProbe
        {
            private readonly FootContactPoint[] contacts;

            private FootGroundingProbe(FootContactPoint[] contactPoints)
            {
                contacts = contactPoints;
            }

            public static FootGroundingProbe Create(
                Player3DAssetRegistry assetRegistry)
            {
                if (assetRegistry == null)
                {
                    return null;
                }

                var points = new List<FootContactPoint>();
                IReadOnlyList<Player3DMeshBinding> bindings =
                    assetRegistry.MeshBindings;
                for (int index = 0; index < bindings.Count; index++)
                {
                    Player3DMeshBinding binding = bindings[index];
                    if (binding == null ||
                        binding.Renderer == null ||
                        binding.Bone == null ||
                        (binding.BoneName != "foot.L" &&
                         binding.BoneName != "foot.R") ||
                        binding.MeshName.IndexOf(
                            "BootSole",
                            StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    AddSoleBoundsContacts(
                        points,
                        binding.Bone,
                        binding.Renderer.bounds);
                }

                return points.Count > 0
                    ? new FootGroundingProbe(points.ToArray())
                    : null;
            }

            public bool TryGetLowestHeight(out float height)
            {
                height = float.PositiveInfinity;
                for (int index = 0; index < contacts.Length; index++)
                {
                    FootContactPoint contact = contacts[index];
                    if (!contact.TryGetWorldPosition(out Vector3 position))
                    {
                        continue;
                    }

                    height = Mathf.Min(height, position.y);
                }

                return !float.IsPositiveInfinity(height);
            }

            private static void AddSoleBoundsContacts(
                ICollection<FootContactPoint> points,
                Transform bone,
                Bounds bounds)
            {
                AddBoundsFaceContacts(
                    points,
                    bone,
                    bounds,
                    bounds.min.y);
                AddBoundsFaceContacts(
                    points,
                    bone,
                    bounds,
                    bounds.max.y);
            }

            private static void AddBoundsFaceContacts(
                ICollection<FootContactPoint> points,
                Transform bone,
                Bounds bounds,
                float y)
            {
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.min.x, y, bounds.min.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.min.x, y, bounds.max.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.max.x, y, bounds.min.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.max.x, y, bounds.max.z)));
            }
        }

        private readonly struct FootContactPoint
        {
            private readonly Transform bone;
            private readonly Vector3 boneLocalPosition;

            public FootContactPoint(
                Transform footBone,
                Vector3 worldPosition)
            {
                bone = footBone;
                boneLocalPosition = bone != null
                    ? bone.InverseTransformPoint(worldPosition)
                    : Vector3.zero;
            }

            public bool TryGetWorldPosition(out Vector3 position)
            {
                if (bone == null)
                {
                    position = default;
                    return false;
                }

                position = bone.TransformPoint(boneLocalPosition);
                return IsFinite(position);
            }
        }

        private readonly struct BoneLocalPose
        {
            private readonly Transform bone;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;

            public BoneLocalPose(Transform boneToCapture)
            {
                bone = boneToCapture;
                localPosition = bone != null
                    ? bone.localPosition
                    : Vector3.zero;
                localRotation = bone != null
                    ? bone.localRotation
                    : Quaternion.identity;
            }

            public void Restore()
            {
                if (bone == null)
                {
                    return;
                }

                bone.localPosition = localPosition;
                bone.localRotation = localRotation;
            }
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
