using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One frame of surveillance for the presentation to render.
    /// </summary>
    public readonly struct SupermarketCashierPoseCommand
    {
        public SupermarketCashierPoseCommand(
            float extension,
            float startleWeight,
            bool scanFrozen,
            bool blinkSuppressed,
            Vector3 focusPoint,
            bool hasFocus)
        {
            Extension = Mathf.Clamp01(extension);
            StartleWeight = Mathf.Clamp01(startleWeight);
            ScanFrozen = scanFrozen;
            BlinkSuppressed = blinkSuppressed;
            FocusPoint = focusPoint;
            HasFocus = hasFocus;
        }

        public float Extension { get; }
        public float StartleWeight { get; }
        public bool ScanFrozen { get; }
        public bool BlinkSuppressed { get; }
        public Vector3 FocusPoint { get; }
        public bool HasFocus { get; }
    }

    /// <summary>
    /// Fully procedural pose for the Watcher Cashier: no clips, no
    /// Animator controller. Every frame restores the imported rest
    /// pose, plants the hunched body with both palms dead-still on the
    /// checkout, then lays the five re-parented neck segments along a
    /// pursuit curve: the head literally travels to hover beside the
    /// hero anywhere in the shop, the chain stretching to reach and
    /// arcing up over any shelf standing in the straight line. The
    /// undersized head is pinned to the curve tip by its authored
    /// neck-attachment point, so it can never tear off the chain.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketCashierPresentation : MonoBehaviour
    {
        public const float NeckSegmentHeight = 0.11f;
        public const float RestNeckLength = 0.55f;
        public const float MaximumNeckLengthMeters = 4.5f;
        public const float HoverStandoffMeters = 0.85f;
        public const float HoverLiftMeters = 0.25f;
        public const float ObstacleClearanceMeters = 0.45f;
        public const float WatcherHeadTiltDegrees = 6f;
        public const float ScanYawDegrees = 4f;
        public const float ScanYawHertz = 0.18f;
        public const float ScanPitchDegrees = 1.5f;
        public const float ScanPitchHertz = 0.11f;
        public const float PupilDartMeters = 0.012f;
        public const float StartledPupilScale = 0.62f;
        public const string EyeWhiteRole = "wide_watcher_eye";
        public const string PupilRole = "visible_eye_pupil";

        private const float SpineHunchDegrees = 8f;
        private const float ChestHunchDegrees = 4f;
        private const int ArmSolveIterations = 6;
        private const int ObstacleSampleCount = 12;
        private const int ChainSegmentCount =
            SupermarketCashierAssetRegistry.NeckSegmentCount;

        private readonly List<BonePose> baseBonePoses =
            new List<BonePose>();
        private readonly List<PivotPose> basePivotPoses =
            new List<PivotPose>();

        private SupermarketCashierAssetRegistry registry;
        private SupermarketCashierBlinkState blink;
        private Renderer[] blinkRenderers = Array.Empty<Renderer>();
        private Transform[] neckSegments = Array.Empty<Transform>();
        private int[] segmentStretchAxes = Array.Empty<int>();
        private Vector3[] segmentRestScales = Array.Empty<Vector3>();
        private Vector3[] pivotRestDirLocals = Array.Empty<Vector3>();
        private Vector3 tipRestInNeck;
        private Vector3 headAnchorLocal;
        private Quaternion headLookOffset = Quaternion.identity;
        private Vector3 leftHandTarget;
        private Vector3 rightHandTarget;
        private Bounds headLimits;
        private IReadOnlyList<Bounds> obstacles = Array.Empty<Bounds>();
        private bool hasPoseContext;
        private float scanElapsed;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public float CurrentExtension { get; private set; }
        public float NeckStretchRatio { get; private set; }
        public bool EyesClosed => blink != null && blink.EyesClosed;
        public Vector3 HeadWorldPosition =>
            registry != null && registry.Head != null
                ? registry.Head.position
                : transform.position;

        public void Initialize(
            SupermarketCashierAssetRegistry cashierRegistry)
        {
            registry = cashierRegistry != null
                ? cashierRegistry
                : throw new ArgumentNullException(
                    nameof(cashierRegistry));
            ValidateBindings();

            Animator animator = registry.Animator;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            BindNeckChain();
            CaptureBaseBonePoses();
            CaptureBlinkRenderers();
            blink = new SupermarketCashierBlinkState();
            NeckStretchRatio = 1f;
            isInitialized = true;
        }

        public void ConfigurePoseContext(
            Vector3 configuredLeftHandTarget,
            Vector3 configuredRightHandTarget,
            Bounds configuredHeadLimits,
            IReadOnlyList<Bounds> configuredObstacles)
        {
            leftHandTarget = configuredLeftHandTarget;
            rightHandTarget = configuredRightHandTarget;
            headLimits = configuredHeadLimits;
            obstacles = configuredObstacles ?? Array.Empty<Bounds>();
            hasPoseContext = true;
        }

        public void Apply(
            float deltaTime,
            in SupermarketCashierPoseCommand command)
        {
            if (!isInitialized)
            {
                return;
            }

            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            if (!command.ScanFrozen)
            {
                scanElapsed += safeDeltaTime;
            }

            RestoreBasePoses();
            ApplyBodyPose();

            Vector3 up = registry.transform.up;
            Vector3 restTip = registry.Neck.TransformPoint(tipRestInNeck);
            Vector3 chainBase = registry.NeckPivots[0].position;
            Vector3 pursuit = ResolvePursuitPoint(
                command,
                chainBase,
                restTip,
                up);
            Vector3 tip = Vector3.Lerp(
                restTip,
                pursuit,
                command.Extension);
            Vector3 control = ResolveCurveControl(chainBase, tip);

            ApplyNeckCurve(chainBase, control, tip);
            ApplyHead(command, tip, up);
            ApplyEyes(command, up);

            blink.Advance(safeDeltaTime, command.BlinkSuppressed);
            for (int index = 0; index < blinkRenderers.Length; index++)
            {
                Renderer target = blinkRenderers[index];
                if (target != null)
                {
                    target.forceRenderingOff = blink.EyesClosed;
                }
            }

            CurrentExtension = command.Extension;
            NeckStretchRatio = Mathf.Max(
                1f,
                (tip - chainBase).magnitude / RestNeckLength);
        }

        /// <summary>
        /// Where the head wants to hover: just in front of the hero's
        /// face, slightly above, on the cashier's side — clamped to the
        /// room limits, pushed out of solid fixtures and capped by the
        /// fully stretched neck length.
        /// </summary>
        private Vector3 ResolvePursuitPoint(
            in SupermarketCashierPoseCommand command,
            Vector3 chainBase,
            Vector3 restTip,
            Vector3 up)
        {
            if (!command.HasFocus || !hasPoseContext)
            {
                return restTip;
            }

            Vector3 toFocus = Vector3.ProjectOnPlane(
                command.FocusPoint - chainBase,
                up);
            Vector3 standoff = toFocus.sqrMagnitude > 0.0001f
                ? toFocus.normalized * HoverStandoffMeters
                : Vector3.zero;
            Vector3 pursuit = command.FocusPoint +
                up * HoverLiftMeters -
                standoff;
            pursuit = ClampToBounds(pursuit, headLimits);

            for (int index = 0; index < obstacles.Count; index++)
            {
                if (obstacles[index].Contains(pursuit))
                {
                    pursuit.y = obstacles[index].max.y +
                        ObstacleClearanceMeters * 0.7f;
                }
            }

            Vector3 reach = pursuit - chainBase;
            if (reach.magnitude > MaximumNeckLengthMeters)
            {
                pursuit = chainBase +
                    reach.normalized * MaximumNeckLengthMeters;
            }

            return pursuit;
        }

        /// <summary>
        /// The quadratic-curve control point. A straight run gets a
        /// straight neck; any shelf crossing the line lifts the middle
        /// of the curve above the tallest obstruction, so the chain
        /// arcs over the aisles instead of clipping through them.
        /// </summary>
        private Vector3 ResolveCurveControl(Vector3 from, Vector3 to)
        {
            Vector3 control = (from + to) * 0.5f;
            if (!hasPoseContext)
            {
                return control;
            }

            float requiredHeight = float.NegativeInfinity;
            for (int index = 0; index < obstacles.Count; index++)
            {
                Bounds obstacle = obstacles[index];
                if (!SegmentCrossesBounds(from, to, obstacle))
                {
                    continue;
                }

                requiredHeight = Mathf.Max(
                    requiredHeight,
                    obstacle.max.y + ObstacleClearanceMeters);
            }

            if (requiredHeight > control.y)
            {
                // The quadratic only reaches half way to its control
                // point at t=0.5, so the control climbs twice the
                // missing height.
                control.y += (requiredHeight - control.y) * 2f;
            }

            if (control.y > headLimits.max.y)
            {
                control.y = headLimits.max.y;
            }

            return control;
        }

        private static bool SegmentCrossesBounds(
            Vector3 from,
            Vector3 to,
            Bounds bounds)
        {
            for (int sample = 1;
                 sample < ObstacleSampleCount;
                 sample++)
            {
                float t = sample / (float)ObstacleSampleCount;
                if (bounds.Contains(Vector3.Lerp(from, to, t)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Lays the five segments along the quadratic from the neck
        /// base to the tip: each pivot sits on its curve point, turns
        /// its rest up-axis onto the local curve direction and scales
        /// its rigid segment mesh to span the gap.
        /// </summary>
        private void ApplyNeckCurve(
            Vector3 chainBase,
            Vector3 control,
            Vector3 tip)
        {
            IReadOnlyList<Transform> pivots = registry.NeckPivots;
            for (int index = 0; index < ChainSegmentCount; index++)
            {
                Vector3 from = EvaluateCurve(
                    chainBase,
                    control,
                    tip,
                    index / (float)ChainSegmentCount);
                Vector3 to = EvaluateCurve(
                    chainBase,
                    control,
                    tip,
                    (index + 1) / (float)ChainSegmentCount);
                Vector3 direction = to - from;
                float length = direction.magnitude;
                Transform pivot = pivots[index];
                pivot.position = from;
                if (length > 0.0001f)
                {
                    Vector3 currentRestDir =
                        pivot.rotation * pivotRestDirLocals[index];
                    pivot.rotation = Quaternion.FromToRotation(
                            currentRestDir,
                            direction / length) *
                        pivot.rotation;
                }

                Transform segment = neckSegments[index];
                Vector3 scale = segmentRestScales[index];
                scale[segmentStretchAxes[index]] *=
                    Mathf.Max(0.05f, length / NeckSegmentHeight);
                segment.localScale = scale;
            }
        }

        private static Vector3 EvaluateCurve(
            Vector3 from,
            Vector3 control,
            Vector3 to,
            float t)
        {
            float inverse = 1f - t;
            return (inverse * inverse * from) +
                   (2f * inverse * t * control) +
                   (t * t * to);
        }

        /// <summary>
        /// The head looks at the hero and is pinned to the curve tip
        /// by its authored neck-attachment point — rotation happens
        /// around that joint, never around the distant canonical bone,
        /// so the head cannot separate from the chain.
        /// </summary>
        private void ApplyHead(
            in SupermarketCashierPoseCommand command,
            Vector3 tip,
            Vector3 up)
        {
            Transform head = registry.Head;
            Vector3 faceDirection;
            if (command.HasFocus)
            {
                Vector3 toFocus = command.FocusPoint - tip;
                faceDirection = toFocus.sqrMagnitude > 0.0001f
                    ? toFocus.normalized
                    : registry.transform.forward;
            }
            else
            {
                float scanYaw = command.ScanFrozen
                    ? 0f
                    : ScanYawDegrees * Mathf.Sin(
                        scanElapsed * ScanYawHertz * 2f * Mathf.PI);
                float scanPitch = command.ScanFrozen
                    ? 0f
                    : ScanPitchDegrees * Mathf.Sin(
                        scanElapsed * ScanPitchHertz * 2f * Mathf.PI);
                faceDirection =
                    Quaternion.AngleAxis(scanYaw, up) *
                    Quaternion.AngleAxis(
                        -scanPitch,
                        registry.transform.right) *
                    registry.transform.forward;
            }

            Vector3 lookRight = Vector3.Cross(up, faceDirection);
            if (lookRight.sqrMagnitude < 0.0001f)
            {
                lookRight = registry.transform.right;
            }

            Quaternion tilt = Quaternion.AngleAxis(
                WatcherHeadTiltDegrees,
                lookRight.normalized);
            head.rotation = tilt *
                Quaternion.LookRotation(faceDirection, up) *
                headLookOffset;
            head.position += tip - head.TransformPoint(headAnchorLocal);
        }

        private void ApplyEyes(
            in SupermarketCashierPoseCommand command,
            Vector3 up)
        {
            float scanYaw = command.ScanFrozen || command.HasFocus
                ? 0f
                : Mathf.Sin(scanElapsed * ScanYawHertz * 2f * Mathf.PI);
            Vector3 dart =
                registry.Head.right * (scanYaw * PupilDartMeters);
            float pupilScale = Mathf.Lerp(
                1f,
                StartledPupilScale,
                command.StartleWeight);

            ApplyEye(registry.FaceEyeLeft, dart, pupilScale);
            ApplyEye(registry.FaceEyeRight, dart, pupilScale);
        }

        private static void ApplyEye(
            Transform eye,
            Vector3 dart,
            float pupilScale)
        {
            eye.position += dart;
            eye.localScale *= pupilScale;
        }

        /// <summary>
        /// Adopts the five static neck segments under their authored
        /// pivots (the wheelchair mechanism pattern), then folds the
        /// pivots into one transform chain hanging off the neck bone so
        /// posing the chest carries the whole periscope, and captures
        /// the rest geometry the curve solver needs.
        /// </summary>
        private void BindNeckChain()
        {
            IReadOnlyList<Transform> pivots = registry.NeckPivots;
            Transform modelRoot = registry.ModelRoot != null
                ? registry.ModelRoot
                : registry.transform;
            Transform[] children =
                modelRoot.GetComponentsInChildren<Transform>(true);
            neckSegments = new Transform[ChainSegmentCount];
            for (int index = 0;
                 index < neckSegments.Length;
                 index++)
            {
                string segmentName =
                    $"NECK_Segment.{index + 1:00}";
                for (int childIndex = 0;
                     childIndex < children.Length;
                     childIndex++)
                {
                    if (string.Equals(
                            children[childIndex].name,
                            segmentName,
                            StringComparison.Ordinal))
                    {
                        neckSegments[index] = children[childIndex];
                        break;
                    }
                }

                if (neckSegments[index] == null)
                {
                    throw new InvalidOperationException(
                        $"The cashier prefab is missing {segmentName}.");
                }

                neckSegments[index].SetParent(pivots[index], true);
            }

            pivots[0].SetParent(registry.Neck, true);
            for (int index = 1; index < pivots.Count; index++)
            {
                pivots[index].SetParent(pivots[index - 1], true);
            }

            Vector3 up = registry.transform.up;
            Transform lastPivot = pivots[pivots.Count - 1];
            Vector3 restTipWorld =
                lastPivot.position + up * NeckSegmentHeight;
            tipRestInNeck =
                registry.Neck.InverseTransformPoint(restTipWorld);
            headAnchorLocal =
                registry.Head.InverseTransformPoint(restTipWorld);

            Vector3 eyeCenter =
                (registry.FaceEyeLeft.position +
                 registry.FaceEyeRight.position) * 0.5f;
            Vector3 restFace = Vector3.ProjectOnPlane(
                eyeCenter - registry.Head.position,
                up);
            if (restFace.sqrMagnitude < 0.0001f)
            {
                restFace = registry.transform.forward;
            }

            headLookOffset =
                Quaternion.Inverse(
                    Quaternion.LookRotation(restFace.normalized, up)) *
                registry.Head.rotation;

            segmentStretchAxes = new int[neckSegments.Length];
            segmentRestScales = new Vector3[neckSegments.Length];
            pivotRestDirLocals = new Vector3[neckSegments.Length];
            for (int index = 0; index < neckSegments.Length; index++)
            {
                Transform segment = neckSegments[index];
                segmentRestScales[index] = segment.localScale;
                segmentStretchAxes[index] = DominantAxis(
                    segment.InverseTransformDirection(up));
                pivotRestDirLocals[index] = pivots[index]
                    .InverseTransformDirection(up);
            }
        }

        private void CaptureBaseBonePoses()
        {
            baseBonePoses.Clear();
            AddBasePose(registry.Pelvis);
            AddBasePose(registry.Spine);
            AddBasePose(registry.Chest);
            AddBasePose(registry.Neck);
            AddBasePose(registry.Head);
            AddBasePose(registry.FaceEyeLeft);
            AddBasePose(registry.FaceEyeRight);
            AddBasePose(registry.LeftUpperArm);
            AddBasePose(registry.LeftForearm);
            AddBasePose(registry.LeftHand);
            AddBasePose(registry.RightUpperArm);
            AddBasePose(registry.RightForearm);
            AddBasePose(registry.RightHand);

            basePivotPoses.Clear();
            IReadOnlyList<Transform> pivots = registry.NeckPivots;
            for (int index = 0; index < pivots.Count; index++)
            {
                basePivotPoses.Add(new PivotPose(pivots[index]));
            }
        }

        private void AddBasePose(Transform target)
        {
            baseBonePoses.Add(new BonePose(target));
        }

        private void RestoreBasePoses()
        {
            for (int index = 0; index < baseBonePoses.Count; index++)
            {
                baseBonePoses[index].Restore();
            }

            for (int index = 0; index < basePivotPoses.Count; index++)
            {
                basePivotPoses[index].Restore();
            }

            for (int index = 0; index < neckSegments.Length; index++)
            {
                neckSegments[index].localScale =
                    segmentRestScales[index];
            }
        }

        private void ApplyBodyPose()
        {
            Transform root = registry.transform;
            Vector3 right = root.right;
            registry.Spine.rotation = Quaternion.AngleAxis(
                    SpineHunchDegrees,
                    right) *
                registry.Spine.rotation;
            registry.Chest.rotation = Quaternion.AngleAxis(
                    ChestHunchDegrees,
                    right) *
                registry.Chest.rotation;

            if (!hasPoseContext)
            {
                return;
            }

            SolveArm(
                registry.LeftUpperArm,
                registry.LeftForearm,
                registry.LeftHand,
                leftHandTarget);
            SolveArm(
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand,
                rightHandTarget);
        }

        private static void SolveArm(
            Transform upper,
            Transform lower,
            Transform end,
            Vector3 target)
        {
            for (int iteration = 0;
                 iteration < ArmSolveIterations;
                 iteration++)
            {
                RotateTowards(lower, end, target);
                RotateTowards(upper, end, target);
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

        private static Vector3 ClampToBounds(
            Vector3 point,
            Bounds bounds)
        {
            return new Vector3(
                Mathf.Clamp(point.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(point.y, bounds.min.y, bounds.max.y),
                Mathf.Clamp(point.z, bounds.min.z, bounds.max.z));
        }

        private static int DominantAxis(Vector3 direction)
        {
            float absX = Mathf.Abs(direction.x);
            float absY = Mathf.Abs(direction.y);
            float absZ = Mathf.Abs(direction.z);
            if (absX >= absY && absX >= absZ)
            {
                return 0;
            }

            return absY >= absZ ? 1 : 2;
        }

        private void CaptureBlinkRenderers()
        {
            var targets = new List<Renderer>(4);
            IReadOnlyList<SupermarketCashierRendererBinding> bindings =
                registry.RendererBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                SupermarketCashierRendererBinding binding =
                    bindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                if (string.Equals(
                        binding.Role,
                        EyeWhiteRole,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        binding.Role,
                        PupilRole,
                        StringComparison.Ordinal))
                {
                    targets.Add(binding.Renderer);
                }
            }

            if (targets.Count != 4)
            {
                throw new InvalidOperationException(
                    "The cashier blink requires exactly two watcher " +
                    "eye whites and two pupils.");
            }

            blinkRenderers = targets.ToArray();
        }

        private void ValidateBindings()
        {
            Transform[] requiredBindings =
            {
                registry.Pelvis,
                registry.Spine,
                registry.Chest,
                registry.Neck,
                registry.Head,
                registry.FaceEyeLeft,
                registry.FaceEyeRight,
                registry.LeftUpperArm,
                registry.LeftForearm,
                registry.LeftHand,
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand
            };
            for (int index = 0; index < requiredBindings.Length; index++)
            {
                if (requiredBindings[index] == null)
                {
                    throw new InvalidOperationException(
                        "The cashier rig is missing a required bone " +
                        "binding.");
                }
            }

            if (registry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The cashier prefab requires an Animator with the " +
                    "shared player Avatar.");
            }

            IReadOnlyList<Transform> pivots = registry.NeckPivots;
            if (pivots.Count !=
                SupermarketCashierAssetRegistry.NeckSegmentCount)
            {
                throw new InvalidOperationException(
                    "The cashier prefab requires exactly five neck " +
                    "pivots.");
            }

            for (int index = 0; index < pivots.Count; index++)
            {
                if (pivots[index] == null)
                {
                    throw new InvalidOperationException(
                        "A cashier neck pivot binding is missing.");
                }
            }
        }

        private readonly struct BonePose
        {
            private readonly Transform target;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;
            private readonly Vector3 localScale;

            public BonePose(Transform configuredTarget)
            {
                target = configuredTarget;
                localPosition = configuredTarget.localPosition;
                localRotation = configuredTarget.localRotation;
                localScale = configuredTarget.localScale;
            }

            public void Restore()
            {
                if (target == null)
                {
                    return;
                }

                target.localPosition = localPosition;
                target.localRotation = localRotation;
                target.localScale = localScale;
            }
        }

        private readonly struct PivotPose
        {
            private readonly Transform target;
            private readonly Quaternion localRotation;

            public PivotPose(Transform configuredTarget)
            {
                target = configuredTarget;
                LocalPosition = configuredTarget.localPosition;
                localRotation = configuredTarget.localRotation;
            }

            public Vector3 LocalPosition { get; }

            public void Restore()
            {
                if (target == null)
                {
                    return;
                }

                target.localPosition = LocalPosition;
                target.localRotation = localRotation;
            }
        }
    }
}
