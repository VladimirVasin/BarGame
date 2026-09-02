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
    /// Procedural checkout pose shared by two separately authored assets.
    /// The active ordinary cashier keeps his fixed human neck and only turns
    /// his head within a conservative limit. The retained Watcher asset can
    /// still opt into the original five-segment pursuit curve.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketCashierPresentation :
        MonoBehaviour,
        IRendererPresentation
    {
        public const float NeckSegmentHeight = 0.11f;
        public const float RestNeckLength = 0.55f;
        // Long enough to reach every corner of the 16 x 11 hall from
        // the register: the face simply always arrives.
        public const float MaximumNeckLengthMeters = 18f;
        public const float HoverStandoffMeters = 0.85f;
        public const float HoverLiftMeters = 0.25f;
        public const float ObstacleClearanceMeters = 0.50f;
        public const float ObstacleMarginMeters = 0.22f;

        /// <summary>
        /// How much further out the arch LOOKS AHEAD than safety needs.
        ///
        /// This is what makes the transition smooth rather than merely
        /// damped. The arch target is solved against boxes grown by this
        /// much, so the neck starts lifting while the straight line is
        /// still clear of the real shelf; by the time the hard margin
        /// would bite, the eased shape is already up there and nothing
        /// has to jump. Damping alone could not do it - a damped step
        /// function still starts from the step.
        /// </summary>
        public const float ArchAnticipationMeters = 0.75f;

        /// <summary>
        /// The neck's own half-thickness, plus the head's.
        ///
        /// The clip probe was a ZERO-RADIUS point test, so the chain had
        /// no thickness at all: a shelf edge could pass between the curve
        /// and the drawn tube. The widest thing on the chain is the head
        /// (`0.0952` in X after the `1.12` scale) and the segment rings
        /// run to `0.077`, so this covers both.
        /// </summary>
        public const float NeckProbeRadiusMeters = 0.10f;

        /// <summary>Seconds for the arch to rise, and to fall again. The
        /// rise is quicker: he is dodging a shelf, not posing.</summary>
        public const float ArchRaiseSmoothTime = 0.22f;

        public const float ArchLowerSmoothTime = 0.38f;
        public const float WatcherHeadTiltDegrees = 6f;
        public const float ScanYawDegrees = 4f;
        public const float ScanYawHertz = 0.18f;
        public const float ScanPitchDegrees = 1.5f;
        public const float ScanPitchHertz = 0.11f;
        public const float PupilDartMeters = 0.012f;
        public const float StartledPupilScale = 0.62f;
        public const float FixedHeadLookLimitDegrees = 28f;
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
        private Vector3 leftPupilOffsetInEye;
        private Vector3 rightPupilOffsetInEye;
        private Vector3 headAnchorLocal;
        private Quaternion headLookOffset = Quaternion.identity;
        private Vector3 leftHandTarget;
        private Vector3 rightHandTarget;
        private Bounds headLimits;
        private IReadOnlyList<Bounds> obstacles = Array.Empty<Bounds>();
        private bool hasPoseContext;
        private float archHeight;
        private float archWeight;
        private float archHeightVelocity;
        private float archWeightVelocity;
        private bool hasArchState;
        private float scanElapsed;
        private bool usesExtensibleNeck;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public IReadOnlyList<Renderer> Renderers =>
            registry != null
                ? registry.Renderers
                : Array.Empty<Renderer>();
        public float CurrentExtension { get; private set; }
        public float NeckStretchRatio { get; private set; }
        public bool UsesExtensibleNeck => usesExtensibleNeck;
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

            usesExtensibleNeck = registry.UsesExtensibleNeck;
            if (usesExtensibleNeck)
            {
                BindNeckChain();
            }

            CaptureHeadLookOffset();
            CaptureBaseBonePoses();
            CaptureBlinkRenderers();
            CapturePupilOffsets();
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
            if (usesExtensibleNeck)
            {
                ApplyExtensibleNeck(safeDeltaTime, command, up);
            }
            else
            {
                ApplyFixedHead(command, up);
                CurrentExtension = 0f;
                NeckStretchRatio = 1f;
            }

            ApplyEyes(command, up);

            blink.Advance(
                safeDeltaTime,
                usesExtensibleNeck && command.BlinkSuppressed);
            for (int index = 0; index < blinkRenderers.Length; index++)
            {
                Renderer target = blinkRenderers[index];
                if (target != null)
                {
                    target.forceRenderingOff = blink.EyesClosed;
                }
            }
        }

        private void ApplyExtensibleNeck(
            float deltaTime,
            in SupermarketCashierPoseCommand command,
            Vector3 up)
        {
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
            ResolveCurveControls(
                chainBase,
                tip,
                deltaTime,
                out Vector3 controlA,
                out Vector3 controlB);

            ApplyNeckCurve(chainBase, controlA, controlB, tip);
            ApplyHead(command, tip, up);
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
                // The head is a volume, not a point: test it against the
                // same padded box the curve uses, or the face sinks into a
                // shelf it is hovering just inside of.
                Bounds guarded = Expand(obstacles[index], 0f);
                if (guarded.Contains(pursuit))
                {
                    pursuit.y = guarded.max.y +
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
        /// The staple: a cubic curve whose two controls lift to a
        /// shared clearance height over every obstacle the run passes,
        /// so the chain climbs out of the counter fast, travels above
        /// the aisles and only descends at the hero. The resulting
        /// curve is re-sampled against the (margin-expanded) obstacle
        /// boxes and the clearance is raised until nothing clips.
        /// </summary>
        private void ResolveCurveControls(
            Vector3 from,
            Vector3 to,
            float deltaTime,
            out Vector3 controlA,
            out Vector3 controlB)
        {
            StraightControls(from, to, out controlA, out controlB);
            if (!hasPoseContext)
            {
                archWeight = 0f;
                archWeightVelocity = 0f;
                return;
            }

            // The TARGET is solved against an anticipation envelope that is
            // deliberately larger than the one safety uses, so the arch
            // starts rising while the straight line is still clear of the
            // real shelf. By the time the hard margin would bite, the eased
            // shape is already there and nothing has to jump.
            float target = SolveArchHeight(
                from,
                controlA,
                controlB,
                to,
                ArchAnticipationMeters);
            bool wantsArch = !float.IsNegativeInfinity(target);
            if (!wantsArch)
            {
                target = archHeight;
            }

            if (!hasArchState)
            {
                hasArchState = true;
                archHeight = wantsArch ? target : from.y;
                archWeight = wantsArch ? 1f : 0f;
                archHeightVelocity = 0f;
                archWeightVelocity = 0f;
            }

            archWeight = Damp(
                archWeight,
                wantsArch ? 1f : 0f,
                ref archWeightVelocity,
                wantsArch ? ArchRaiseSmoothTime : ArchLowerSmoothTime,
                deltaTime);
            if (wantsArch)
            {
                archHeight = Damp(
                    archHeight,
                    target,
                    ref archHeightVelocity,
                    ArchRaiseSmoothTime,
                    deltaTime);
            }

            BlendControls(from, to, out controlA, out controlB);

            // And the floor. Everything above is about how it LOOKS; this is
            // the part that must be true on every single frame, including
            // every frame of the blend. If the shape we are actually about to
            // render still touches a shelf, the height is taken to whatever
            // clears it and the eased value is snapped to match, so the ease
            // resumes from the truth rather than fighting it.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (!CurveTouchesAnyObstacle(
                        from, controlA, controlB, to))
                {
                    return;
                }

                float safe = SolveArchHeight(
                    from,
                    controlA,
                    controlB,
                    to,
                    0f);
                if (float.IsNegativeInfinity(safe))
                {
                    return;
                }

                archHeight = Mathf.Max(
                    archHeight,
                    safe + (attempt * 0.35f));
                archWeight = 1f;
                archWeightVelocity = 0f;
                archHeightVelocity = 0f;
                BlendControls(from, to, out controlA, out controlB);
            }
        }

        /// <summary>
        /// The unarched shape. Controls at the chord's thirds are the degree
        /// elevation of a straight line, so this is exactly a straight rod -
        /// which is the point.
        /// </summary>
        private static void StraightControls(
            Vector3 from,
            Vector3 to,
            out Vector3 controlA,
            out Vector3 controlB)
        {
            controlA = Vector3.Lerp(from, to, 1f / 3f);
            controlB = Vector3.Lerp(from, to, 2f / 3f);
        }

        /// <summary>
        /// The two shapes, mixed by <see cref="archWeight"/>.
        ///
        /// BOTH discontinuities have to be blended, not just the obvious one.
        /// The arched solution moves the controls ALONG the run as well as
        /// up: `1/3, 2/3` becomes `0.20, 0.80`. Blending only the height
        /// would still slide the control points sideways in one frame.
        /// </summary>
        private void BlendControls(
            Vector3 from,
            Vector3 to,
            out Vector3 controlA,
            out Vector3 controlB)
        {
            float weight = Mathf.Clamp01(archWeight);
            controlA = Vector3.Lerp(
                from,
                to,
                Mathf.Lerp(1f / 3f, 0.20f, weight));
            controlB = Vector3.Lerp(
                from,
                to,
                Mathf.Lerp(2f / 3f, 0.80f, weight));
            float height = Mathf.Min(archHeight, headLimits.max.y);
            controlA.y = Mathf.Lerp(controlA.y, height, weight);
            controlB.y = Mathf.Lerp(controlB.y, height, weight);
        }

        /// <summary>
        /// The plateau height the given shape would need to clear every
        /// obstacle it touches, or negative infinity when it touches none.
        /// <paramref name="extraMargin"/> grows the boxes beyond the safety
        /// margin, which is how the arch is made to anticipate.
        /// </summary>
        private float SolveArchHeight(
            Vector3 from,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 to,
            float extraMargin)
        {
            float clearance = float.NegativeInfinity;
            for (int index = 0; index < obstacles.Count; index++)
            {
                Bounds expanded = Expand(obstacles[index], extraMargin);
                if (!CurveTouchesBounds(
                        from, controlA, controlB, to, expanded))
                {
                    continue;
                }

                clearance = Mathf.Max(
                    clearance,
                    expanded.max.y + ObstacleClearanceMeters);
            }

            return clearance;
        }

        private static float Damp(
            float current,
            float target,
            ref float velocity,
            float smoothTime,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return current;
            }

            float next = Mathf.SmoothDamp(
                current,
                target,
                ref velocity,
                smoothTime,
                Mathf.Infinity,
                deltaTime);
            if (Mathf.Abs(target - next) <= 0.0005f)
            {
                velocity = 0f;
                return target;
            }

            return next;
        }

        private bool CurveTouchesAnyObstacle(
            Vector3 from,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 to)
        {
            for (int index = 0; index < obstacles.Count; index++)
            {
                if (CurveTouchesBounds(
                        from,
                        controlA,
                        controlB,
                        to,
                        Expand(obstacles[index], 0f)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CurveTouchesBounds(
            Vector3 from,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 to,
            Bounds bounds)
        {
            for (int sample = 1;
                 sample < ObstacleSampleCount;
                 sample++)
            {
                float t = sample / (float)ObstacleSampleCount;
                if (bounds.Contains(EvaluateCurve(
                        from, controlA, controlB, to, t)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The obstacle box as the neck must see it: its own margin, the
        /// chain's thickness, and whatever anticipation the caller wants.
        ///
        /// THE Y TERM USED TO BE HALF WHAT IT READ AS.
        /// <c>Bounds.Expand(Vector3)</c> adds HALF its argument to the
        /// extents, which is why every other caller in this project passes
        /// `margin * 2f`. The vertical term here did not, so a `2.05 m`
        /// shelf was guarded to `2.16` where the two horizontal terms were
        /// guarded to the full `0.22` per side. Vertical is the one axis
        /// this fixture actually has to clear.
        /// </summary>
        private static Bounds Expand(Bounds bounds, float extraMargin)
        {
            float margin =
                ObstacleMarginMeters +
                NeckProbeRadiusMeters +
                Mathf.Max(0f, extraMargin);
            Bounds expanded = bounds;
            expanded.Expand(new Vector3(
                margin * 2f,
                margin * 2f,
                margin * 2f));
            return expanded;
        }

        /// <summary>
        /// Lays the five segments along the quadratic from the neck
        /// base to the tip: each pivot sits on its curve point, turns
        /// its rest up-axis onto the local curve direction and scales
        /// its rigid segment mesh to span the gap.
        /// </summary>
        private void ApplyNeckCurve(
            Vector3 chainBase,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 tip)
        {
            IReadOnlyList<Transform> pivots = registry.NeckPivots;
            for (int index = 0; index < ChainSegmentCount; index++)
            {
                Vector3 from = EvaluateCurve(
                    chainBase,
                    controlA,
                    controlB,
                    tip,
                    index / (float)ChainSegmentCount);
                Vector3 to = EvaluateCurve(
                    chainBase,
                    controlA,
                    controlB,
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
            Vector3 controlA,
            Vector3 controlB,
            Vector3 to,
            float t)
        {
            float inverse = 1f - t;
            return (inverse * inverse * inverse * from) +
                   (3f * inverse * inverse * t * controlA) +
                   (3f * inverse * t * t * controlB) +
                   (t * t * t * to);
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

        /// <summary>
        /// The ordinary cashier can acknowledge movement in the hall without
        /// acquiring any non-human anatomy. Rotation is measured from the
        /// restored pose every frame and capped, while the authored head
        /// position and neck scale remain untouched.
        /// </summary>
        private void ApplyFixedHead(
            in SupermarketCashierPoseCommand command,
            Vector3 up)
        {
            Transform head = registry.Head;
            Quaternion restRotation = head.rotation;
            Vector3 faceDirection;
            if (command.HasFocus)
            {
                Vector3 toFocus = command.FocusPoint - head.position;
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

            Quaternion desired = Quaternion.LookRotation(
                    faceDirection,
                    up) *
                headLookOffset;
            head.rotation = Quaternion.RotateTowards(
                restRotation,
                desired,
                FixedHeadLookLimitDegrees);
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
                usesExtensibleNeck ? command.StartleWeight : 0f);

            ApplyEye(
                registry.FaceEyeLeft,
                dart,
                pupilScale,
                leftPupilOffsetInEye);
            ApplyEye(
                registry.FaceEyeRight,
                dart,
                pupilScale,
                rightPupilOffsetInEye);
        }

        /// <summary>
        /// Pinches one pupil WITHOUT dragging it out of its socket.
        ///
        /// THE PINCH USED TO TELEPORT THE PUPILS OUT OF THE HEAD, and it
        /// read exactly as the user reported it: "the pupils look so far
        /// down it feels like they are not there at all". They were not
        /// looking anywhere. `face.eye.L` rests at `z 1.606` while the
        /// pupil it drives is drawn at `1.963` - the generator says so
        /// outright, "the bones rest far below the authored face" - so the
        /// pupil hangs `0.357 m` above its own bone's origin. Scaling that
        /// bone scales about the ORIGIN, so a startled `0.62` walked the
        /// pupil `0.357 * 0.38 = 0.135 m` straight down: four and a half
        /// times the eye white's own `0.029 m` radius, out through the
        /// chin. The startle is exactly when the hero is close and looking,
        /// which is why it only ever showed up close.
        ///
        /// So the scale is compensated by the translation it induces. The
        /// offset is captured once at bind time in the bone's own space, so
        /// it costs one cached vector and no per-frame search.
        /// </summary>
        private static void ApplyEye(
            Transform eye,
            Vector3 dart,
            float pupilScale,
            Vector3 pupilOffsetInEye)
        {
            eye.localScale *= pupilScale;
            eye.position += dart + eye.TransformVector(
                pupilOffsetInEye * (1f - pupilScale));
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

        private void CaptureHeadLookOffset()
        {
            Vector3 up = registry.transform.up;
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

        /// <summary>
        /// Where each pupil is drawn, in its own eye bone's space.
        ///
        /// Captured from the renderer bounds rather than assumed, so the
        /// compensation in <see cref="ApplyEye"/> stays correct if the
        /// generator ever moves the eyes or the head scale changes again.
        /// A missing pupil leaves the offset zero, which makes the
        /// compensation a no-op rather than an exception - the blink
        /// capture already refuses a model with the wrong eye count.
        /// </summary>
        private void CapturePupilOffsets()
        {
            leftPupilOffsetInEye = Vector3.zero;
            rightPupilOffsetInEye = Vector3.zero;
            IReadOnlyList<SupermarketCashierRendererBinding> bindings =
                registry.RendererBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                SupermarketCashierRendererBinding binding =
                    bindings[index];
                if (binding == null ||
                    binding.Renderer == null ||
                    !string.Equals(
                        binding.Role,
                        PupilRole,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 centre = binding.Renderer.bounds.center;
                if (binding.RendererName.EndsWith(
                        ".R",
                        StringComparison.Ordinal))
                {
                    rightPupilOffsetInEye = registry.FaceEyeRight
                        .InverseTransformVector(
                            centre - registry.FaceEyeRight.position);
                }
                else
                {
                    leftPupilOffsetInEye = registry.FaceEyeLeft
                        .InverseTransformVector(
                            centre - registry.FaceEyeLeft.position);
                }
            }
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
            int expectedPivotCount = registry.UsesExtensibleNeck
                ? SupermarketCashierAssetRegistry.WatcherNeckSegmentCount
                : 0;
            if (pivots.Count != expectedPivotCount)
            {
                throw new InvalidOperationException(
                    registry.UsesExtensibleNeck
                        ? "The Watcher cashier requires exactly five " +
                          "neck pivots."
                        : "The ordinary cashier must not bind extensible " +
                          "neck pivots.");
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
