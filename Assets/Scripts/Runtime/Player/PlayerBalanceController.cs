using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Runs the balance model every frame ahead of the motor: gathers what
    /// the world did to the hero last frame (achieved velocity, the wall
    /// he touched, the slope under him, which boot the clip has down, the
    /// kerb ahead of the swinging one), advances the model, hands the root
    /// drift to the motor and the body pose to the presentation.
    ///
    /// It never writes a transform itself. The motor moves the capsule,
    /// the presentation moves the bones, and the status controller owns
    /// the fall the model can latch.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [DisallowMultipleComponent]
    public sealed class PlayerBalanceController : MonoBehaviour
    {
        public const float SurfaceProbeStartHeight = 0.35f;
        public const float SurfaceProbeDistance = 1f;
        public const float WallReachDistance = 0.75f;
        public const float WallReachRadius = 0.12f;
        public const float WallReachLowHeight = 0.6f;
        public const float WallReachHighHeight = 1.4f;
        public const int WallBisectionIterations = 8;

        /// <summary>Yaw keeps this much of its rate at full instability.</summary>
        public const float YawScaleAtFullInstability = 0.4f;

        /// <summary>
        /// The brace: where the hands go out for the ground as a topple
        /// passes the point a lunge can save. Each palm aims at the ground
        /// this far ahead of its shoulder along the fall, the shoulders
        /// this far either side of the root, probed from chest height; the
        /// hand on the far side of the fall commits less.
        /// </summary>
        public const float BraceReachAhead = 0.45f;
        public const float BraceShoulderHalfWidth = 0.22f;
        public const float BraceProbeHeight = 1.3f;
        public const float BraceProbeDistance = 1.6f;
        public const float BraceOffHandWeight = 0.7f;
        public const float BracePalmClearance = 0.02f;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private PlayerMotor motor;
        private IPlayerPresentation presentation;
        private IPlayerClipPresentation clipPresentation;
        private IPlayerBalancePresentation balancePresentation;
        private Player3DCharacterPresentation heroPresentation;
        private Player3DRagdollController ragdoll;
        private IWalkableArea walkableArea;
        private Transform root;
        private float capsuleRadius = 0.32f;
        private PlayerBalanceModel model;
        private float intoxication;
        private bool fallsAllowedByLevel;
        private bool frozenByStatus;
        private bool handHolding;
        private bool handWantsHold;
        private float handWeight;
        private float steadySeconds;
        private bool handIsRight = true;
        private PlayerWallReachPose wallReachPose = PlayerWallReachPose.None;
        private PlayerArmReachPose leftBrace = PlayerArmReachPose.None;
        private PlayerArmReachPose rightBrace = PlayerArmReachPose.None;
        private bool initialized;
        private PlayerBalanceOutput lastOutput = PlayerBalanceOutput.Still;

        /// <summary>The wall hand as the presentation is drawing it.</summary>
        public PlayerWallReachPose WallReach => wallReachPose;

        /// <summary>The bracing hands as the presentation is drawing them.</summary>
        public PlayerArmReachPose LeftBrace => leftBrace;
        public PlayerArmReachPose RightBrace => rightBrace;

        /// <summary>The hand is on the wall and taking weight.</summary>
        public bool HandHolding => handHolding;

        public bool IsInitialized => initialized;
        public PlayerBalanceModel Model => model;
        public PlayerBalanceOutput Output => lastOutput;
        public float Instability => lastOutput.Instability;
        public float Intoxication => intoxication;

        /// <summary>The model advanced this frame (not frozen by a clip, a modal, a fall).</summary>
        public bool IsActive { get; private set; }

        /// <summary>Falls are possible right now: level, surface and footing agree.</summary>
        public bool FallAllowedNow { get; private set; }

        /// <summary>Angle of the surface under the root, degrees.</summary>
        public float SurfaceAngleDegrees { get; private set; }

        /// <summary>A wall the hand could reach on the side he is tipping to.</summary>
        public bool WallWithinReach { get; private set; }

        /// <summary>World planar normal of that wall, pointing away from it.</summary>
        public Vector3 WallNormal { get; private set; }

        /// <summary>World point on that wall at reach height.</summary>
        public Vector3 WallPoint { get; private set; }

        public void Initialize(
            PlayerRuntime player,
            IWalkableArea area,
            int seed)
        {
            motor = player.Motor;
            presentation = player.Visual;
            clipPresentation = player.Visual as IPlayerClipPresentation;
            balancePresentation = player.Visual as IPlayerBalancePresentation;
            heroPresentation = player.Visual as Player3DCharacterPresentation;
            ragdoll = player.Ragdoll;
            walkableArea = area;
            root = player.GameObject != null
                ? player.GameObject.transform
                : transform;
            CharacterController controller = root.GetComponent<CharacterController>();
            if (controller != null)
            {
                capsuleRadius = controller.radius;
            }

            model = new PlayerBalanceModel(seed);
            gait = new PlayerDrunkGaitModel(seed ^ GaitSeedSalt);
            lastOutput = PlayerBalanceOutput.Still;
            initialized = true;
        }

        /// <summary>The drunk walk draws its own sequence beside the balance model's.</summary>
        public const int GaitSeedSalt = 0x6A17;

        private PlayerDrunkGaitModel gait;
        private PlayerDrunkGaitPose gaitPose;

        /// <summary>The drunk walk's disorder this frame (exactly none sober or frozen).</summary>
        public PlayerDrunkGaitPose GaitPose => gaitPose;
        public PlayerDrunkGaitModel Gait => gait;

        public void SetIntoxication(float normalized)
        {
            intoxication = Mathf.Clamp01(normalized);
        }

        /// <summary>The level is high enough that balance can be lost.</summary>
        public void SetFallsAllowedByLevel(bool allowed)
        {
            fallsAllowedByLevel = allowed;
        }

        /// <summary>The status controller owns the body (a fall is playing).</summary>
        public void SetFrozen(bool frozen)
        {
            frozenByStatus = frozen;
            if (frozen)
            {
                // Freeze on the spot rather than on the next update: the
                // status controller runs after this one, so the fall it
                // begins must not leave a frame of drift or lean behind.
                IsActive = false;
                FallAllowedNow = false;
                wallReachPose = PlayerWallReachPose.None;
                handHolding = false;
                handWantsHold = false;
                handWeight = 0f;
                leftBrace = PlayerArmReachPose.None;
                rightBrace = PlayerArmReachPose.None;
                if (motor != null)
                {
                    motor.SetBalanceDrift(Vector3.zero);
                    motor.SetBalanceYawScale(1f);
                    motor.SetBalanceHeadingWeave(0f);
                }

                gait?.Reset();
                gaitPose = PlayerDrunkGaitPose.None;
                balancePresentation?.SetBalance(PlayerBalancePose.Neutral);
            }
        }

        /// <summary>Whether the wall hand is taking weight this frame (slice 4).</summary>
        public void SetHandHolding(bool holding)
        {
            handHolding = holding;
        }

        public void ArmGrace(float seconds)
        {
            model?.ArmGrace(seconds);
        }

        public void Reseed(int seed)
        {
            model = new PlayerBalanceModel(seed);
            gait = new PlayerDrunkGaitModel(seed ^ GaitSeedSalt);
            gaitPose = PlayerDrunkGaitPose.None;
            lastOutput = PlayerBalanceOutput.Still;
        }

        public void ResetModel()
        {
            model?.Reset();
            lastOutput = PlayerBalanceOutput.Still;
        }

        /// <summary>Debug and test seam: latch a fall on the next status update.</summary>
        public void DebugForceLoseBalance(float direction)
        {
            model?.ForceLoseBalance(direction);
            if (model != null)
            {
                lastOutput = model.Output;
            }
        }

        /// <summary>A shove in the hero's frame, metres per second.</summary>
        public void InjectPerturbation(Vector2 localVelocity)
        {
            model?.InjectPerturbation(localVelocity);
        }

        /// <summary>
        /// What the ragdoll starts with when the model has latched a
        /// fall: the topple's motion in world space — the centre of
        /// mass's velocity (dipping along the lean), the body's rotation
        /// about the edge of the boots' support, and the axis and pivot
        /// of that rotation.
        /// </summary>
        public PlayerRagdollHandoff BuildRagdollHandoff()
        {
            Vector3 forward = Planar(root.forward, Vector3.forward);
            Vector3 right = Planar(root.right, Vector3.right);
            Vector2 axis = lastOutput.FallAxis;
            Vector3 axisWorld = right * axis.x + forward * axis.y;
            if (axisWorld.sqrMagnitude <= 0.0001f)
            {
                axisWorld = right * lastOutput.FallDirection;
            }

            axisWorld.Normalize();
            Vector2 velocity = lastOutput.FallVelocity;
            Vector3 planar = right * velocity.x + forward * velocity.y;
            float lean = Mathf.Clamp(lastOutput.FallLeanDegrees, 0f, 80f);
            Vector3 linear = planar +
                             Vector3.down *
                             (planar.magnitude * Mathf.Tan(lean * Mathf.Deg2Rad));
            Vector3 angular = Vector3.Cross(Vector3.up, axisWorld) *
                              lastOutput.FallAngularVelocity;
            Vector2 centre = lastOutput.CentreOfPressure;
            Vector3 pivot = root.position + right * centre.x + forward * centre.y;
            return new PlayerRagdollHandoff(
                linear,
                angular,
                axisWorld,
                pivot,
                lastOutput.FallDirection);
        }

        private void Update()
        {
            if (!initialized || motor == null || model == null)
            {
                return;
            }

            bool frozen =
                frozenByStatus ||
                motor.InteractionPoseMoveActive ||
                !motor.InputEnabled ||
                SceneTransitionService.IsTransitioning ||
                BarMinigameModalLock.IsAnyLocked ||
                (presentation != null && presentation.InteractionHandoffLocked) ||
                (clipPresentation != null && clipPresentation.IsClipActive) ||
                (ragdoll != null && ragdoll.IsActive);
            if (frozen)
            {
                IsActive = false;
                FallAllowedNow = false;
                WallWithinReach = false;
                handWantsHold = false;
                handHolding = false;
                handWeight = 0f;
                steadySeconds = 0f;
                wallReachPose = PlayerWallReachPose.None;
                leftBrace = PlayerArmReachPose.None;
                rightBrace = PlayerArmReachPose.None;
                motor.SetBalanceDrift(Vector3.zero);
                motor.SetBalanceYawScale(1f);
                motor.SetBalanceHeadingWeave(0f);
                gait?.Reset();
                gaitPose = PlayerDrunkGaitPose.None;
                balancePresentation?.SetBalance(PlayerBalancePose.Neutral);
                return;
            }

            IsActive = true;
            Vector3 forward = Planar(root.forward, Vector3.forward);
            Vector3 right = Planar(root.right, Vector3.right);

            SurfaceAngleDegrees = SampleSurface(out Vector3 downhill);
            bool grounded = motor.IsGrounded;
            FallAllowedNow = fallsAllowedByLevel &&
                             grounded &&
                             model.GraceSeconds <= 0f &&
                             SurfaceAngleDegrees <=
                             PlayerBalanceRules.MaximumBalanceSurfaceAngle;

            Vector2 velocity = ToLocal(motor.PlanarVelocity, right, forward);
            Vector2 slope = ToLocal(downhill, right, forward) *
                            Mathf.Tan(SurfaceAngleDegrees * Mathf.Deg2Rad);
            PlayerMotorContactSample contact = motor.LastContact;
            Vector2 contactNormal = contact.HasWall
                ? ToLocal(contact.Normal, right, forward)
                : Vector2.zero;

            float plantLeft = 1f;
            float plantRight = 1f;
            float runBlend = 0f;
            float kerbRise = 0f;
            if (heroPresentation != null)
            {
                plantLeft = heroPresentation.LeftFootPlant;
                plantRight = heroPresentation.RightFootPlant;
                runBlend = heroPresentation.RunBlend;
                kerbRise = KerbRiseAhead(plantLeft, plantRight);
            }

            UpdateWallReach(right, forward, contact);
            UpdateWallHand(Time.deltaTime, forward, right, contact);
            Vector2 wallNormal = WallWithinReach
                ? ToLocal(WallNormal, right, forward)
                : Vector2.zero;

            var input = new PlayerBalanceInput(
                intoxication,
                velocity,
                motor.CurrentTurnInput,
                runBlend,
                grounded,
                slope,
                contact.HasSideCollision || contact.HasAreaRefusal,
                contactNormal,
                plantLeft,
                plantRight,
                kerbRise,
                FallAllowedNow,
                WallWithinReach,
                wallNormal,
                handHolding && WallWithinReach);
            model.Advance(Time.deltaTime, input);
            lastOutput = model.Output;
            UpdateBrace(right, forward);

            // The drunk walk runs on the Walk clip's own cycle, which the
            // presentation reports a frame behind; it draws nothing sober.
            gaitPose = gait != null
                ? gait.Advance(
                    Time.deltaTime,
                    intoxication,
                    heroPresentation != null ? heroPresentation.ForwardGaitCycle : 0f,
                    heroPresentation != null && heroPresentation.ForwardGaitDominant,
                    runBlend,
                    heroPresentation != null ? heroPresentation.LocomotionBlend : 0f)
                : PlayerDrunkGaitPose.None;

            Vector2 drift = lastOutput.DriftVelocity;
            motor.SetBalanceDrift(right * drift.x + forward * drift.y);
            motor.SetBalanceYawScale(
                Mathf.Lerp(
                    1f,
                    YawScaleAtFullInstability,
                    lastOutput.Instability));
            // The model's slow weave of the heading bends the line he
            // walks without turning him: the desired velocity, not the yaw.
            motor.SetBalanceHeadingWeave(lastOutput.HeadingWeaveDegrees);

            BalanceStepCommand step = lastOutput.Step;
            var pose = new PlayerBalancePose(
                intoxication > 0f ? 1f : 0f,
                lastOutput.LeanRollDegrees,
                lastOutput.LeanPitchDegrees,
                lastOutput.Instability,
                lastOutput.ArmReaction,
                lastOutput.CrouchMetres,
                new PlayerBalanceStepPose(
                    step.Active,
                    step.Side,
                    step.Progress,
                    step.From,
                    step.To,
                    step.Lift),
                wallReachPose,
                lastOutput.LeftFoot,
                lastOutput.RightFoot,
                lastOutput.TorsoReactionDegrees.x,
                lastOutput.TorsoReactionDegrees.y,
                lastOutput.Phase,
                lastOutput.BraceWeight,
                lastOutput.FallAxis,
                leftBrace,
                rightBrace,
                gaitPose);
            balancePresentation?.SetBalance(pose);
        }

        /// <summary>
        /// The hands go out for the ground as the topple passes the lean
        /// a lunge can still save: each palm aims at the floor ahead of
        /// its own shoulder along the fall, where a ray from chest height
        /// finds it (the root's ground when it finds nothing), the hand
        /// on the side he is going commits fully and the other less.
        /// </summary>
        private void UpdateBrace(Vector3 right, Vector3 forward)
        {
            float weight = lastOutput.BraceWeight;
            if (weight <= 0f)
            {
                leftBrace = PlayerArmReachPose.None;
                rightBrace = PlayerArmReachPose.None;
                return;
            }

            Vector2 axis = lastOutput.FallAxis;
            Vector3 fallWorld = right * axis.x + forward * axis.y;
            fallWorld = fallWorld.sqrMagnitude > 0.0001f
                ? fallWorld.normalized
                : right;
            bool rightSide = axis.x >= 0f;
            leftBrace = ProbeBrace(
                false,
                fallWorld,
                right,
                rightSide ? weight * BraceOffHandWeight : weight);
            rightBrace = ProbeBrace(
                true,
                fallWorld,
                right,
                rightSide ? weight : weight * BraceOffHandWeight);
        }

        private PlayerArmReachPose ProbeBrace(
            bool rightHand,
            Vector3 fallWorld,
            Vector3 right,
            float weight)
        {
            Vector3 shoulder = root.position +
                               Vector3.up * BraceProbeHeight +
                               right * (rightHand ? BraceShoulderHalfWidth : -BraceShoulderHalfWidth);
            Vector3 origin = shoulder + fallWorld * BraceReachAhead;
            Vector3 point = new Vector3(origin.x, root.position.y, origin.z);
            Vector3 normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(
                origin + Vector3.up * 0.3f,
                Vector3.down,
                Hits,
                BraceProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = Hits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(root) ||
                    hit.normal.y <= 0.001f ||
                    hit.distance >= closest)
                {
                    continue;
                }

                closest = hit.distance;
                point = hit.point;
                normal = hit.normal.normalized;
            }

            return new PlayerArmReachPose(
                true,
                rightHand,
                point + normal * BracePalmClearance,
                normal,
                weight,
                0.15f,
                0.05f);
        }

        private void OnDisable()
        {
            IsActive = false;
            if (motor != null)
            {
                motor.SetBalanceDrift(Vector3.zero);
                motor.SetBalanceYawScale(1f);
            }

            balancePresentation?.SetBalance(PlayerBalancePose.Neutral);
        }

        private float SampleSurface(out Vector3 downhill)
        {
            downhill = Vector3.zero;
            Vector3 origin = root.position +
                             Vector3.up * SurfaceProbeStartHeight;
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                Hits,
                SurfaceProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            Vector3 normal = Vector3.up;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = Hits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(root) ||
                    hit.normal.y <= 0.001f ||
                    hit.distance >= closest)
                {
                    continue;
                }

                closest = hit.distance;
                normal = hit.normal.normalized;
            }

            float angle = Vector3.Angle(normal, Vector3.up);
            if (angle > 0.5f)
            {
                downhill = Vector3.ProjectOnPlane(normal, Vector3.up);
                downhill = downhill.sqrMagnitude > 0.000001f
                    ? downhill.normalized
                    : Vector3.zero;
            }

            return angle;
        }

        private float KerbRiseAhead(float plantLeft, float plantRight)
        {
            FootSide swing = plantLeft <= plantRight
                ? FootSide.Left
                : FootSide.Right;
            FootGroundSample swingSample = swing == FootSide.Left
                ? heroPresentation.LeftFootGround
                : heroPresentation.RightFootGround;
            FootGroundSample stanceSample = swing == FootSide.Left
                ? heroPresentation.RightFootGround
                : heroPresentation.LeftFootGround;
            if (!swingSample.HasSurface || !stanceSample.HasSurface)
            {
                return 0f;
            }

            return Mathf.Max(0f, swingSample.ToeY - stanceSample.HeelY);
        }

        private void UpdateWallReach(
            Vector3 right,
            Vector3 forward,
            in PlayerMotorContactSample contact)
        {
            WallWithinReach = false;
            if (contact.HasWall)
            {
                WallWithinReach = true;
                WallNormal = contact.Normal;
                WallPoint = contact.Point.sqrMagnitude > 0f
                    ? contact.Point
                    : root.position - contact.Normal * capsuleRadius +
                      Vector3.up * 1.2f;
                return;
            }

            if (lastOutput.Instability <= PlayerBalanceRules.WallCatchInstability)
            {
                return;
            }

            Vector2 capture = lastOutput.CapturePoint;
            if (capture.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 direction = (right * capture.x + forward * capture.y).normalized;
            float bestDistance = float.PositiveInfinity;
            Vector3 bestNormal = Vector3.zero;
            Vector3 bestPoint = Vector3.zero;

            Vector3 low = root.position + Vector3.up * WallReachLowHeight;
            Vector3 high = root.position + Vector3.up * WallReachHighHeight;
            int count = Physics.CapsuleCastNonAlloc(
                low,
                high,
                WallReachRadius,
                direction,
                Hits,
                WallReachDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = Hits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(root) ||
                    Mathf.Abs(hit.normal.y) >= 0.5f ||
                    hit.distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hit.distance;
                bestNormal = Planar(hit.normal, -direction);
                bestPoint = hit.point;
            }

            if (walkableArea != null)
            {
                Vector3 far = root.position + direction * WallReachDistance;
                if (!walkableArea.Contains(far, capsuleRadius))
                {
                    float inside = 0f;
                    float outside = WallReachDistance;
                    for (int iteration = 0;
                         iteration < WallBisectionIterations;
                         iteration++)
                    {
                        float middle = (inside + outside) * 0.5f;
                        if (walkableArea.Contains(
                                root.position + direction * middle,
                                capsuleRadius))
                        {
                            inside = middle;
                        }
                        else
                        {
                            outside = middle;
                        }
                    }

                    // The area keeps the capsule's centre a radius away
                    // from the wall line: the wall itself is one radius
                    // further out.
                    float wallDistance = outside + capsuleRadius;
                    if (wallDistance < bestDistance)
                    {
                        bestDistance = wallDistance;
                        bestNormal = -direction;
                        bestPoint = root.position +
                                    direction * wallDistance +
                                    Vector3.up * 1.2f;
                    }
                }
            }

            if (float.IsPositiveInfinity(bestDistance))
            {
                return;
            }

            WallWithinReach = true;
            WallNormal = bestNormal;
            WallPoint = bestPoint;
        }

        /// <summary>
        /// The hand on the wall: reaches when he tips toward a wall in
        /// reach (or has just bumped it), holds with hysteresis, and lets
        /// go once he has been steady for a while. The palm target is the
        /// wall point at shoulder height, slid a little ahead.
        /// </summary>
        private void UpdateWallHand(
            float deltaTime,
            Vector3 forward,
            Vector3 right,
            in PlayerMotorContactSample contact)
        {
            float instability = lastOutput.Instability;
            steadySeconds = instability < PlayerWallContactRules.ReleaseInstability
                ? steadySeconds + Mathf.Max(0f, deltaTime)
                : 0f;
            float wallDistance = float.PositiveInfinity;
            float facingDot = 1f;
            if (WallWithinReach)
            {
                Vector3 toWall = WallPoint - root.position;
                toWall.y = 0f;
                wallDistance = toWall.magnitude;
                facingDot = Vector3.Dot(WallNormal, forward);
            }

            handWantsHold = PlayerWallContactRules.ShouldHold(
                handWantsHold,
                WallWithinReach,
                instability,
                wallDistance,
                facingDot,
                contact.HasWall,
                steadySeconds);
            if (handWantsHold &&
                PlayerWallContactRules.TryChooseHand(
                    WallNormal,
                    right,
                    out bool rightHand))
            {
                handIsRight = rightHand;
            }

            handWeight = PlayerWallContactRules.AdvanceWeight(
                handWeight,
                handWantsHold,
                deltaTime);
            handHolding = handWeight > 0.5f;
            if (handWeight <= 0.0001f)
            {
                wallReachPose = PlayerWallReachPose.None;
                return;
            }

            Vector3 palm = PlayerWallContactRules.PalmTarget(
                WallPoint,
                WallNormal,
                forward,
                root.position.y + WallReachHighHeight);
            wallReachPose = new PlayerWallReachPose(
                true,
                handIsRight,
                palm,
                WallNormal,
                handWeight);
        }

        private static Vector2 ToLocal(
            Vector3 world,
            Vector3 right,
            Vector3 forward)
        {
            return new Vector2(
                Vector3.Dot(world, right),
                Vector3.Dot(world, forward));
        }

        private static Vector3 Planar(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.000001f
                ? value.normalized
                : fallback;
        }
    }
}
