using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The 3D stairwell cat: the last sprite conversion. The visual
    /// is the passive authored prefab; this actor adopts its meshes
    /// under the exported pivot empties (the wheelchair mechanism
    /// pattern) and articulates them from the untouched pure
    /// timelines - StairwellCatIdleModel keeps every second of the
    /// old sprite's breathing, tail flicks, ear twitches and
    /// grooming, and StairwellCatFeedingTimeline keeps the feeding
    /// contract the player clips are paired to.
    ///
    /// All pose writes are deltas over rest poses cached at
    /// initialization, about the model's own world axes - never
    /// absolute local eulers, so the FBX axis conversion stays out of
    /// the pose math.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StairwellCatActor : MonoBehaviour
    {
        private struct RestPose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;

            public static RestPose Capture(Transform target)
            {
                return new RestPose
                {
                    Position = target.localPosition,
                    Rotation = target.localRotation,
                    Scale = target.localScale
                };
            }
        }

        private Camera targetCamera;
        private Transform player;
        private StairwellCatRigAnchors anchors;
        private StairwellCatIdleModel idleModel;
        private StairwellCatHeadYawModel headYawModel;
        private StairwellCatFeedingTimeline feedingTimeline;
        private bool feedingPrepared;
        private RestPose chestRest;
        private RestPose headRest;
        private RestPose earLeftRest;
        private RestPose earRightRest;
        private readonly RestPose[] tailRest =
            new RestPose[StairwellCatRigAnchors.TailPivotCount];

        public bool IsInitialized { get; private set; }

        /// <summary>The haunches renderer - the cat's visibility
        /// proxy, as the sprite renderer used to be.</summary>
        public Renderer Renderer { get; private set; }

        public StairwellCatRigAnchors Anchors => anchors;
        public StairwellCatGrinController Grin { get; private set; }
        public float HeadYawDegrees =>
            headYawModel != null
                ? headYawModel.CurrentYawDegrees
                : 0f;
        public int CurrentFrame =>
            idleModel != null
                ? idleModel.CurrentFrame
                : 0;
        public StairwellCatIdleKind CurrentIdleKind =>
            idleModel != null
                ? idleModel.CurrentKind
                : StairwellCatIdleKind.Breathe;
        public bool IsFeeding =>
            feedingTimeline != null &&
            feedingTimeline.IsActive;
        public bool IsFeedingPrepared => feedingPrepared;
        public int CurrentFeedingFrame =>
            IsFeeding
                ? feedingTimeline.FrameIndex
                : -1;

        public void Initialize(
            Camera camera,
            Transform playerTransform,
            StairwellCatRigAnchors rigAnchors,
            StairwellCatGrinController grinController)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The stairwell cat is already initialized.");
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (playerTransform == null)
            {
                throw new ArgumentNullException(
                    nameof(playerTransform));
            }

            if (rigAnchors == null)
            {
                throw new ArgumentNullException(nameof(rigAnchors));
            }

            if (!rigAnchors.IsBound)
            {
                throw new ArgumentException(
                    "The stairwell cat rig anchors are not fully " +
                    "bound.",
                    nameof(rigAnchors));
            }

            if (grinController == null)
            {
                throw new ArgumentNullException(
                    nameof(grinController));
            }

            targetCamera = camera;
            player = playerTransform;
            anchors = rigAnchors;
            Grin = grinController;
            Renderer = anchors.BodyRenderer;

            BindArticulation();
            CaptureRestPoses();

            idleModel = new StairwellCatIdleModel();
            headYawModel = new StairwellCatHeadYawModel();
            feedingTimeline = new StairwellCatFeedingTimeline();
            IsInitialized = true;
            AdvancePresentation(0f);
        }

        public bool BeginFeeding()
        {
            if (!TryPrepareFeeding())
            {
                return false;
            }

            if (BeginPreparedFeeding())
            {
                return true;
            }

            CancelFeedingPreparation();
            return false;
        }

        public bool TryPrepareFeeding()
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                IsFeeding)
            {
                return false;
            }

            // The sprite cat loaded its feeding atlas here; the 3D
            // cat has nothing to fetch, but the three-phase
            // prepare/begin/cancel contract survives for the
            // interaction's phase machine.
            feedingPrepared = true;
            return true;
        }

        public bool BeginPreparedFeeding()
        {
            if (!feedingPrepared ||
                !IsInitialized ||
                !isActiveAndEnabled ||
                IsFeeding ||
                !feedingTimeline.Begin())
            {
                return false;
            }

            feedingPrepared = false;
            ApplyCurrentPose();
            return true;
        }

        public bool CancelFeedingPreparation()
        {
            if (!feedingPrepared)
            {
                return false;
            }

            feedingPrepared = false;
            return true;
        }

        public bool CompleteFeeding()
        {
            if (feedingTimeline == null ||
                !feedingTimeline.Complete())
            {
                return false;
            }

            ApplyCurrentPose();
            return true;
        }

        public bool CancelFeeding()
        {
            if (feedingTimeline == null ||
                !feedingTimeline.Cancel())
            {
                return false;
            }

            ApplyCurrentPose();
            return true;
        }

        public void AdvancePresentation(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            Grin.Advance(deltaTime);
            if (IsFeeding)
            {
                feedingTimeline.Advance(deltaTime);
            }
            else
            {
                idleModel.Advance(deltaTime);
                bool tracking =
                    idleModel.CurrentKind !=
                    StairwellCatIdleKind.Groom;
                if (tracking && player != null)
                {
                    headYawModel.Update(
                        ComputeYawToward(player.position),
                        deltaTime);
                }
            }

            ApplyCurrentPose();
        }

        /// <summary>
        /// Adopts the flat-exported meshes and child empties under
        /// their pivots. The FBX deliberately ships everything beside
        /// the empties with each mesh's origin on its pivot, so this
        /// runtime reparent is exact.
        /// </summary>
        private void BindArticulation()
        {
            Transform head = anchors.HeadPivot;
            anchors.EarLeftPivot.SetParent(head, true);
            anchors.EarRightPivot.SetParent(head, true);
            anchors.MuzzleAnchor.SetParent(head, true);
            anchors.TailPivots[1].SetParent(anchors.TailPivots[0], true);
            anchors.TailPivots[2].SetParent(anchors.TailPivots[1], true);

            for (int index = 0;
                 index < anchors.RendererBindings.Count;
                 index++)
            {
                StairwellCatRendererBinding binding =
                    anchors.RendererBindings[index];
                if (binding == null ||
                    binding.Renderer == null ||
                    string.IsNullOrEmpty(binding.PivotName))
                {
                    continue;
                }

                Transform pivot = ResolvePivot(binding.PivotName);
                if (pivot == null)
                {
                    throw new InvalidOperationException(
                        $"The cat part '{binding.RendererName}' " +
                        $"names unknown pivot '{binding.PivotName}'.");
                }

                binding.Renderer.transform.SetParent(pivot, true);
            }
        }

        private Transform ResolvePivot(string pivotName)
        {
            switch (pivotName)
            {
                case StairwellCatRigAnchors.ChestPivotName:
                    return anchors.ChestPivot;
                case StairwellCatRigAnchors.HeadPivotName:
                    return anchors.HeadPivot;
                case StairwellCatRigAnchors.EarLeftPivotName:
                    return anchors.EarLeftPivot;
                case StairwellCatRigAnchors.EarRightPivotName:
                    return anchors.EarRightPivot;
                default:
                    for (int index = 0;
                         index <
                         StairwellCatRigAnchors.TailPivotNames.Length;
                         index++)
                    {
                        if (string.Equals(
                                pivotName,
                                StairwellCatRigAnchors
                                    .TailPivotNames[index],
                                StringComparison.Ordinal))
                        {
                            return anchors.TailPivots[index];
                        }
                    }

                    return null;
            }
        }

        private void CaptureRestPoses()
        {
            chestRest = RestPose.Capture(anchors.ChestPivot);
            headRest = RestPose.Capture(anchors.HeadPivot);
            earLeftRest = RestPose.Capture(anchors.EarLeftPivot);
            earRightRest = RestPose.Capture(anchors.EarRightPivot);
            for (int index = 0; index < tailRest.Length; index++)
            {
                tailRest[index] =
                    RestPose.Capture(anchors.TailPivots[index]);
            }
        }

        private void ApplyCurrentPose()
        {
            StairwellCatPose pose = IsFeeding
                ? StairwellCatPoseRules.FeedingPose(
                    feedingTimeline.FrameIndex)
                : StairwellCatPoseRules.IdlePose(
                    idleModel.CurrentKind,
                    idleModel.CurrentFrame,
                    headYawModel.CurrentYawDegrees);

            float grinWeight = Grin != null ? Grin.HeadTurnWeight : 0f;
            if (grinWeight > 0f && targetCamera != null)
            {
                pose = StairwellCatPoseRules.ComposeGrin(
                    pose,
                    grinWeight,
                    ComputeYawToward(
                        targetCamera.transform.position));
            }

            ApplyPose(pose);
        }

        /// <summary>
        /// The direction the cat's muzzle faces at rest. The FBX
        /// geometry faces model-local -Z (Blender -Y through the
        /// axis bake) and the prefab's inner half turn aims the
        /// PREFAB at +Z, so the live geometry always faces the
        /// negation of the model root's axes.
        /// </summary>
        private Vector3 CatForward => -anchors.ModelRoot.forward;

        private Vector3 CatRight => -anchors.ModelRoot.right;

        private float ComputeYawToward(Vector3 worldTarget)
        {
            Vector3 forward = CatForward;
            forward.y = 0f;
            Vector3 toTarget =
                worldTarget - anchors.HeadPivot.position;
            toTarget.y = 0f;
            if (forward.sqrMagnitude <= 0.000001f ||
                toTarget.sqrMagnitude <= 0.000001f)
            {
                return 0f;
            }

            return Vector3.SignedAngle(
                forward,
                toTarget,
                Vector3.up);
        }

        private void ApplyPose(in StairwellCatPose pose)
        {
            Vector3 catForward = CatForward;
            Vector3 catRight = CatRight;

            Transform chest = anchors.ChestPivot;
            chest.localScale = chestRest.Scale * pose.ChestScale;

            Transform head = anchors.HeadPivot;
            Quaternion headDelta =
                ParentSpaceRotation(
                    head,
                    pose.HeadYawDegrees,
                    Vector3.up) *
                ParentSpaceRotation(
                    head,
                    pose.HeadPitchDegrees,
                    catRight) *
                ParentSpaceRotation(
                    head,
                    pose.HeadRollDegrees,
                    catForward);
            head.localRotation = headDelta * headRest.Rotation;
            head.localPosition =
                headRest.Position +
                ParentSpaceVector(
                    head,
                    Vector3.up * pose.HeadLiftMeters);

            anchors.EarLeftPivot.localRotation =
                ParentSpaceRotation(
                    anchors.EarLeftPivot,
                    pose.EarLeftTiltDegrees,
                    catRight) *
                earLeftRest.Rotation;
            anchors.EarRightPivot.localRotation =
                ParentSpaceRotation(
                    anchors.EarRightPivot,
                    pose.EarRightTiltDegrees,
                    catRight) *
                earRightRest.Rotation;

            ApplyTailSwing(0, pose.TailSwing01Degrees, catForward);
            ApplyTailSwing(1, pose.TailSwing02Degrees, catForward);
            ApplyTailSwing(2, pose.TailSwing03Degrees, catForward);
        }

        private void ApplyTailSwing(
            int index,
            float swingDegrees,
            Vector3 catForward)
        {
            Transform pivot = anchors.TailPivots[index];
            pivot.localRotation =
                ParentSpaceRotation(pivot, swingDegrees, catForward) *
                tailRest[index].Rotation;
        }

        private static Quaternion ParentSpaceRotation(
            Transform target,
            float degrees,
            Vector3 worldAxis)
        {
            if (degrees == 0f)
            {
                return Quaternion.identity;
            }

            Vector3 axis = target.parent != null
                ? target.parent.InverseTransformDirection(worldAxis)
                : worldAxis;
            return Quaternion.AngleAxis(degrees, axis);
        }

        private static Vector3 ParentSpaceVector(
            Transform target,
            Vector3 worldVector)
        {
            return target.parent != null
                ? target.parent.InverseTransformVector(worldVector)
                : worldVector;
        }

        private void Update()
        {
            AdvancePresentation(Time.deltaTime);
        }

        private void OnDisable()
        {
            CancelFeedingPreparation();
            CancelFeeding();
        }

        private void OnDestroy()
        {
            feedingPrepared = false;
            feedingTimeline?.Cancel();
            IsInitialized = false;
        }
    }
}
