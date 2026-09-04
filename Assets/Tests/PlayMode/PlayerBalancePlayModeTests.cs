using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The continuous balance model on the production hero, standing on a
    /// flat slab with the motor enabled and no key held: sober he is
    /// bit-exact still; drunk he drifts through the motor inside the
    /// walkable rectangle, steps with the boot ahead of the root, keeps
    /// the stance boot planted, leans and spreads his arms, catches a wall
    /// on his right with the right hand, and a forced fall freezes the
    /// model and reseeds it.
    ///
    /// The clock is pinned at <c>1/60 s</c>, so every frame budget below is
    /// a duration of model time. The intoxication ramp and the fall state
    /// machine run on <c>unscaledDeltaTime</c>, which the pin does not
    /// touch, so budgets that wait on those are generous. Offline, the
    /// model with this seed first steps on its own at <c>12.8–14.4 s</c>
    /// depending on how long the ramp takes; where a test needs a step or
    /// a wall catch sooner than that, it waits for the natural one first
    /// and then provokes it with a lateral shove before giving up.
    /// </summary>
    public sealed class PlayerBalancePlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int FramesPerSecond = 60;
        private const int TestCitySeed = 12345;
        private const float ControllerRadius = 0.32f;
        private const float WideHalfWidth = 5f;
        private const float DriftRectHalfWidth = 0.6f;
        private const float ModelGraceSeconds = 30f;
        private const int GroundedFrameBudget = 180;
        private const int SettleFrames = 12;
        private const int NaturalStepFrameBudget = 15 * FramesPerSecond;
        private const int ProvokedStepFrameBudget = 2 * FramesPerSecond;
        private const int StepFrameBudget = 2 * FramesPerSecond;
        private const int NaturalWallFrameBudget = 8 * FramesPerSecond;
        private const int WallFrameBudget = 20 * FramesPerSecond;
        private const int FallFrameBudget = 3000;
        private const float ShoveMetresPerSecond = 0.9f;
        private const float WallOffset = 0.45f;

        /// <summary>
        /// The interiors' rectangle, verbatim from
        /// <c>BarInteriorRoot.InteriorWalkableArea</c>: an XZ rect that
        /// keeps the capsule's centre one radius inside it.
        /// </summary>
        private sealed class RectWalkableArea : IWalkableArea
        {
            public Rect Bounds;

            public RectWalkableArea(Rect bounds)
            {
                Bounds = bounds;
            }

            public bool Contains(Vector3 position, float radius = 0f)
            {
                return position.x >= Bounds.xMin + radius &&
                       position.x <= Bounds.xMax - radius &&
                       position.z >= Bounds.yMin + radius &&
                       position.z <= Bounds.yMax - radius;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius = 0f)
            {
                float minX = Bounds.xMin + radius;
                float maxX = Bounds.xMax - radius;
                float minZ = Bounds.yMin + radius;
                float maxZ = Bounds.yMax - radius;
                desiredPosition.x = Mathf.Clamp(desiredPosition.x, minX, maxX);
                desiredPosition.z = Mathf.Clamp(desiredPosition.z, minZ, maxZ);
                return desiredPosition;
            }
        }

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject playerObject;
        private GameObject uiObject;
        private GameObject wallObject;
        private RectWalkableArea area;
        private PlayerRuntime runtime;
        private PlayerMotor motor;
        private CharacterController controller;
        private Player3DCharacterPresentation presentation;
        private Player3DAssetRegistry registry;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private IntoxicationStatusController status;
        private Transform root;

        // What the step wait observed, for the messages of the tests that
        // give up.
        private bool waitSawStep;
        private bool waitProvokedStep;
        private float waitMaxInstability;
        private Vector2 previousWaitLeft;
        private Vector2 previousWaitRight;
        private Vector2 currentWaitLeft;
        private Vector2 currentWaitRight;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);

            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "Balance Test Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(12f, 0.2f, 12f);

            cameraObject = new GameObject("Balance Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;

            area = new RectWalkableArea(
                new Rect(
                    -WideHalfWidth,
                    -WideHalfWidth,
                    2f * WideHalfWidth,
                    2f * WideHalfWidth));
            // Spawn resting on the skin width, as the foot-IK tests do: a
            // capsule created exactly on the floor is depenetrated upward
            // by the first move that touches it, which reads as a climb.
            runtime = PlayerFactory.Create(
                null,
                Vector3.up * PlayerFactory.GroundedRootOffset,
                camera,
                area,
                null);
            playerObject = runtime.GameObject;
            root = playerObject.transform;
            motor = runtime.Motor;
            controller = playerObject.GetComponent<CharacterController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                controller.radius,
                Is.EqualTo(ControllerRadius).Within(0.0001f),
                "The containment checks below assume the factory's radius.");
            Assert.That(
                runtime.Visual,
                Is.TypeOf<Player3DCharacterPresentation>());
            presentation = (Player3DCharacterPresentation)runtime.Visual;
            registry = presentation.Registry;
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Anchors.LeftFoot, Is.Not.Null);
            Assert.That(registry.Anchors.RightFoot, Is.Not.Null);
            Assert.That(
                runtime.Balance,
                Is.Not.Null,
                "The production hero carries the balance controller.");

            cameraFollow = cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(camera, root, false);

            uiObject = new GameObject("Balance Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            status = uiObject.AddComponent<IntoxicationStatusController>();

            Physics.SyncTransforms();
            yield return null;
            yield return null;
            yield return WaitForGrounded();
            yield return Frames(SettleFrames);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (wallObject != null)
            {
                Object.Destroy(wallObject);
            }

            if (uiObject != null)
            {
                Object.Destroy(uiObject);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            if (playerObject != null)
            {
                Object.Destroy(playerObject);
            }

            if (groundObject != null)
            {
                Object.Destroy(groundObject);
            }

            ResetSession();
            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SoberHero_NeverDrifts()
        {
            yield return Prepare(0, 0f);
            Vector3 origin = root.position;
            float maxLean = 0f;
            float maxArm = 0f;
            float maxInstability = 0f;
            for (int frame = 0; frame < 4 * FramesPerSecond; frame++)
            {
                yield return null;
                Vector3 position = root.position;
                Assert.That(
                    position.x,
                    Is.EqualTo(origin.x).Within(1e-6f),
                    $"A sober hero must not drift on x (frame {frame}).");
                Assert.That(
                    position.z,
                    Is.EqualTo(origin.z).Within(1e-6f),
                    $"A sober hero must not drift on z (frame {frame}).");
                PlayerBalancePose pose = presentation.BalancePose;
                maxLean = Mathf.Max(maxLean, Mathf.Abs(pose.LeanRollDegrees));
                maxArm = Mathf.Max(maxArm, pose.ArmReaction);
                maxInstability = Mathf.Max(
                    maxInstability,
                    status.Balance.Instability);
            }

            Assert.That(
                status.Balance.IsActive,
                Is.True,
                "The model runs even sober; it is inert, not frozen.");
            Assert.That(status.Balance.Instability, Is.Zero);
            Assert.That(maxInstability, Is.Zero);
            Assert.That(presentation.BalancePose.Weight, Is.Zero);
            Assert.That(maxLean, Is.Zero, "Sober: no lean, ever.");
            Assert.That(maxArm, Is.Zero, "Sober: no arm reaction, ever.");
            PlayerBalanceModel model = status.Balance.Model;
            Assert.That(model.ComOffset.x, Is.Zero);
            Assert.That(model.ComOffset.y, Is.Zero);
            Assert.That(model.ComVelocity.x, Is.Zero);
            Assert.That(model.ComVelocity.y, Is.Zero);
            Assert.That(model.StepsTaken, Is.Zero);
            Assert.That(model.LostBalance, Is.False);
        }

        [UnityTest]
        public IEnumerator DrunkHero_RootDriftsThroughTheMotorAndStaysInsideTheArea()
        {
            yield return Prepare(100, ModelGraceSeconds);
            Vector3 origin = root.position;
            area.Bounds = new Rect(
                origin.x - DriftRectHalfWidth,
                origin.z - DriftRectHalfWidth,
                2f * DriftRectHalfWidth,
                2f * DriftRectHalfWidth);

            float maxDx = 0f;
            float maxDz = 0f;
            for (int frame = 0; frame < 15 * FramesPerSecond; frame++)
            {
                yield return null;
                Vector3 position = root.position;
                maxDx = Mathf.Max(maxDx, Mathf.Abs(position.x - origin.x));
                maxDz = Mathf.Max(maxDz, Mathf.Abs(position.z - origin.z));
                Assert.That(
                    position.y,
                    Is.EqualTo(origin.y).Within(0.005f),
                    $"The drift is planar; the root must not climb or sink " +
                    $"(frame {frame}).");
                // One millimetre of slack under the radius: the controller
                // lands a clamped move within a float ULP of the clamp, and
                // the rect's Contains is an inclusive compare on that edge.
                Assert.That(
                    area.Contains(position, ControllerRadius - 0.001f),
                    Is.True,
                    $"The walkable rect must clamp the balance drift like " +
                    $"any other motion (frame {frame}, x={position.x:F4}, " +
                    $"z={position.z:F4}).");
            }

            Assert.That(
                Mathf.Max(maxDx, maxDz),
                Is.GreaterThan(0.03f),
                $"A blind-drunk hero standing still must creep or step " +
                $"through the motor (max |dx|={maxDx:F4}, max |dz|={maxDz:F4}).");
        }

        [UnityTest]
        public IEnumerator RecoveryStep_MovesTheFootBeforeTheRoot()
        {
            yield return Prepare(100, ModelGraceSeconds);
            yield return WaitForRecoveryStep();
            if (!waitSawStep)
            {
                Assert.Inconclusive(
                    $"No recovery step in {NaturalStepFrameBudget / FramesPerSecond} s " +
                    $"of standing nor within {ProvokedStepFrameBudget / FramesPerSecond} s " +
                    $"of a {ShoveMetresPerSecond} m/s shove; max instability " +
                    $"{waitMaxInstability:F3}.");
            }

            PlayerBalanceStepPose step = presentation.BalancePose.Step;
            FootSide side = step.Side;
            Transform steppingFoot = FootAnchor(side);
            Vector2 footStart = Planar(steppingFoot.position);
            Vector2 rootStart = Planar(root.position);
            float maxFootTravel = 0f;
            Vector2 rootAtLastActive = rootStart;
            int activeFrames = 0;
            for (int frame = 0; frame < StepFrameBudget; frame++)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                PlayerBalanceStepPose now = presentation.BalancePose.Step;
                if (!now.Active || now.Side != side)
                {
                    break;
                }

                activeFrames++;
                maxFootTravel = Mathf.Max(
                    maxFootTravel,
                    Vector2.Distance(Planar(steppingFoot.position), footStart));
                rootAtLastActive = Planar(root.position);
            }

            Assert.That(
                activeFrames,
                Is.GreaterThanOrEqualTo(2),
                "A recovery step lasts a quarter second or more; the " +
                "sampler must see it in flight.");
            float rootTravel = Vector2.Distance(rootAtLastActive, rootStart);
            Assert.That(
                maxFootTravel,
                Is.GreaterThan(0.05f),
                $"The stepping {side} boot must swing out during the step " +
                $"(provoked: {waitProvokedStep}).");
            Assert.That(
                rootTravel,
                Is.LessThan(maxFootTravel),
                $"The root follows the boot at half its travel, never " +
                $"ahead of it (root {rootTravel:F3} m, boot {maxFootTravel:F3} m).");

            PlayerBalancePose landed = presentation.BalancePose;
            Assert.That(
                landed.Step.Active,
                Is.False,
                "The step must land inside the budget.");
            bool leftChanged = Vector2.Distance(
                landed.LeftFootLocal,
                PlayerBalanceModel.DefaultLeftFoot) > 0.02f;
            bool rightChanged = Vector2.Distance(
                landed.RightFootLocal,
                PlayerBalanceModel.DefaultRightFoot) > 0.02f;
            Assert.That(
                leftChanged || rightChanged,
                Is.True,
                $"After a step the model's feet are no longer the default " +
                $"stance (left {landed.LeftFootLocal}, right {landed.RightFootLocal}).");
        }

        [UnityTest]
        public IEnumerator StanceFoot_DoesNotSlideDuringAStep()
        {
            yield return Prepare(100, ModelGraceSeconds);
            yield return WaitForRecoveryStep();
            if (!waitSawStep)
            {
                Assert.Inconclusive(
                    $"No recovery step to measure the stance boot against; " +
                    $"max instability {waitMaxInstability:F3}.");
            }

            FootSide side = presentation.BalancePose.Step.Side;
            FootSide stance = side == FootSide.Left
                ? FootSide.Right
                : FootSide.Left;
            Transform stanceFoot = FootAnchor(stance);
            // The frame before the step and the first frame of it: the
            // stance boot is locked where the clip had it, so even the
            // lock must not move it.
            Vector2 previous = stance == FootSide.Left
                ? previousWaitLeft
                : previousWaitRight;
            Vector2 current = Planar(stanceFoot.position);
            float largestSlide = Vector2.Distance(previous, current);
            int largestSlideFrame = 0;
            previous = current;
            int activeFrames = 0;
            for (int frame = 1; frame <= StepFrameBudget; frame++)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                PlayerBalanceStepPose now = presentation.BalancePose.Step;
                if (!now.Active || now.Side != side)
                {
                    break;
                }

                activeFrames++;
                current = Planar(stanceFoot.position);
                float slide = Vector2.Distance(previous, current);
                if (slide > largestSlide)
                {
                    largestSlide = slide;
                    largestSlideFrame = frame;
                }

                previous = current;
            }

            Assert.That(activeFrames, Is.GreaterThanOrEqualTo(2));
            Assert.That(
                largestSlide,
                Is.LessThan(0.01f),
                $"The {stance} stance boot must stay planted while the " +
                $"{side} boot steps; it moved {largestSlide:F4} m in one " +
                $"frame (step frame {largestSlideFrame}, provoked: " +
                $"{waitProvokedStep}).");
        }

        [UnityTest]
        public IEnumerator RecoveryStep_LandedFootKeepsTheGroundItFound()
        {
            yield return Prepare(100, ModelGraceSeconds);
            yield return WaitForRecoveryStep();
            if (!waitSawStep)
            {
                Assert.Inconclusive(
                    $"No recovery step to land; max instability " +
                    $"{waitMaxInstability:F3}.");
            }

            FootSide side = presentation.BalancePose.Step.Side;
            Transform steppingFoot = FootAnchor(side);
            Vector2 footAtLastActive = Planar(steppingFoot.position);
            Vector3 modelTargetWorld = Vector3.zero;
            float lastProgress = 0f;
            for (int frame = 0; frame < StepFrameBudget; frame++)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                PlayerBalanceStepPose now = presentation.BalancePose.Step;
                if (!now.Active || now.Side != side)
                {
                    break;
                }

                footAtLastActive = Planar(steppingFoot.position);
                lastProgress = now.Progress;
                modelTargetWorld = root.position +
                                   PlanarRight() * now.ToLocal.x +
                                   PlanarForward() * now.ToLocal.y;
            }

            Assert.That(
                presentation.BalancePose.Step.Active,
                Is.False,
                "The step must land inside the budget.");
            Assert.That(
                lastProgress,
                Is.GreaterThan(0.8f),
                "The last in-flight sample must be near the landing.");
            Assert.That(
                Vector2.Distance(footAtLastActive, Planar(modelTargetWorld)),
                Is.LessThan(0.1f),
                $"On its last in-flight frame the drawn {side} boot must be " +
                $"at the model's landing target (provoked: {waitProvokedStep}).");

            // The boot that landed keeps the ground it found: no new lurch
            // starts inside the reaction delay, and a gather step waits a
            // tenth of a second, so these frames belong to the landed boot.
            for (int frame = 1; frame <= 4; frame++)
            {
                if (presentation.BalancePose.Step.Active)
                {
                    break;
                }

                Vector2 landed = Planar(steppingFoot.position);
                Assert.That(
                    Vector2.Distance(landed, footAtLastActive),
                    Is.LessThan(0.06f),
                    $"The landed {side} boot must stay where the step put " +
                    $"it, not snap back under the hip ({frame} frame(s) " +
                    $"after landing it is {Vector2.Distance(landed, footAtLastActive):F3} m " +
                    $"from the landing spot).");
                yield return null;
                presentation.ReapplyLatePresentationPose();
            }
        }

        [UnityTest]
        public IEnumerator DrunkHero_LeansAndSpreadsArms()
        {
            // The sober hands, as drawn: the drunk ones are measured
            // against them, because "the arms rotate" is not the same
            // claim as "the arms go OUT" — the first version of this pose
            // rotated them by the right number of degrees straight back
            // into his ribs.
            // The idle is a four-second breathing loop, so the sober
            // reference is its widest span and its furthest-back reach
            // over one whole loop, not one phase of it.
            CaptureTorsoForward();
            float soberSpan = 0f;
            float soberReach = float.MaxValue;
            for (int frame = 0; frame < 4 * FramesPerSecond; frame++)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                soberSpan = Mathf.Max(soberSpan, HandSpan());
                soberReach = Mathf.Min(soberReach, LowestHandForwardReach());
            }

            yield return Prepare(100, ModelGraceSeconds);
            float maxLean = 0f;
            float maxArm = 0f;
            float maxWeight = 0f;
            float maxSpan = 0f;
            float minSpan = float.MaxValue;
            float minReach = float.MaxValue;
            int drunkFrames = 0;
            for (int frame = 0; frame < 15 * FramesPerSecond; frame++)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                PlayerBalancePose pose = presentation.BalancePose;
                maxLean = Mathf.Max(maxLean, Mathf.Abs(pose.LeanRollDegrees));
                maxArm = Mathf.Max(maxArm, pose.ArmReaction);
                maxWeight = Mathf.Max(maxWeight, pose.Weight);
                // The status recovers on REAL time even under a pinned
                // clock, so the amount may already sit a point under
                // full: the gate only excludes the blend-in.
                if (presentation.IntoxicationAmount >= 0.9f)
                {
                    drunkFrames++;
                    float span = HandSpan();
                    maxSpan = Mathf.Max(maxSpan, span);
                    minSpan = Mathf.Min(minSpan, span);
                    minReach = Mathf.Min(minReach, LowestHandForwardReach());
                }
            }

            Assert.That(
                drunkFrames,
                Is.GreaterThan(10 * FramesPerSecond),
                "The hands were sampled across most of the drunk window.");

            Assert.That(
                maxWeight,
                Is.EqualTo(1f).Within(0.001f),
                "A drunk hero's pose applies at full weight.");
            Assert.That(
                maxLean,
                Is.GreaterThan(2f),
                $"A blind-drunk hero must lean visibly (max {maxLean:F2} deg).");
            Assert.That(
                maxArm,
                Is.GreaterThan(0.05f),
                $"A blind-drunk hero must spread his arms (max {maxArm:F3}).");
            Assert.That(
                minSpan,
                Is.GreaterThan(soberSpan + 0.15f),
                "A blind-drunk hero holds his arms OUT for balance, not " +
                $"against his ribs: hands {minSpan:F3}-{maxSpan:F3} m apart " +
                $"against {soberSpan:F3} m sober.");
            Assert.That(
                minReach,
                Is.GreaterThan(soberReach - 0.05f),
                "Neither arm may swing behind the torso from where it " +
                $"hangs sober (lowest forward reach {minReach:F3} m " +
                $"against {soberReach:F3} m sober). The bug this guards " +
                "sent the hands 0.3 m back.");
        }

        [UnityTest]
        public IEnumerator BalancePose_LeanSignsFollowTheContract()
        {
            // Positive roll is a lean to the RIGHT and positive pitch a
            // lean FORWARD (PlayerBalancePose), whatever the imported
            // bones' local axes happen to point at. The pitch was wrong
            // for a day: the pelvis bone's local right points to the
            // hero's left, so a positive turn about it tipped him back.
            Vector3 forward = root.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Transform head = HandBone(Player3DAnatomicalPart.Head);
            Transform pelvis = HandBone(Player3DAnatomicalPart.Pelvis);
            yield return null;

            presentation.SetBalance(PlayerBalancePose.Neutral);
            presentation.ReapplyLatePresentationPose();
            Vector3 neutral = head.position - pelvis.position;

            presentation.SetBalance(LeanPose(0f, 10f));
            presentation.ReapplyLatePresentationPose();
            Vector3 pitched = head.position - pelvis.position;
            Assert.That(
                Vector3.Dot(pitched - neutral, forward),
                Is.GreaterThan(0.05f),
                "A positive pitch leans the torso FORWARD.");

            presentation.SetBalance(LeanPose(10f, 0f));
            presentation.ReapplyLatePresentationPose();
            Vector3 rolled = head.position - pelvis.position;
            Assert.That(
                Vector3.Dot(rolled - neutral, right),
                Is.GreaterThan(0.05f),
                "A positive roll leans the torso to the RIGHT.");

            presentation.SetBalance(PlayerBalancePose.Neutral);
            presentation.ReapplyLatePresentationPose();
        }

        private static PlayerBalancePose LeanPose(float roll, float pitch)
        {
            return new PlayerBalancePose(
                1f,
                roll,
                pitch,
                0f,
                0f,
                0f,
                PlayerBalanceStepPose.None,
                PlayerWallReachPose.None,
                PlayerBalanceModel.DefaultLeftFoot,
                PlayerBalanceModel.DefaultRightFoot);
        }

        /// <summary>Distance between the two hands, metres.</summary>
        private float HandSpan()
        {
            return Vector3.Distance(
                HandBone(Player3DAnatomicalPart.LeftHand).position,
                HandBone(Player3DAnatomicalPart.RightHand).position);
        }

        private Vector3 torsoForwardLocal;

        /// <summary>
        /// Pins the actor's planar heading in the torso bone's own frame
        /// while he stands sober, so the reach below follows the torso
        /// through the model's pitch and roll instead of reading a
        /// backward lean as arms swung behind the back.
        /// </summary>
        private void CaptureTorsoForward()
        {
            Vector3 forward = root.forward;
            forward.y = 0f;
            forward.Normalize();
            torsoForwardLocal = HandBone(Player3DAnatomicalPart.Torso)
                .InverseTransformDirection(forward);
        }

        /// <summary>
        /// How far ahead of its own shoulder the further-back hand sits
        /// along the torso's pinned heading, metres (negative = behind).
        /// </summary>
        private float LowestHandForwardReach()
        {
            Transform torso = HandBone(Player3DAnatomicalPart.Torso);
            float left = Vector3.Dot(
                torso.InverseTransformDirection(
                    HandBone(Player3DAnatomicalPart.LeftHand).position -
                    HandBone(Player3DAnatomicalPart.LeftUpperArm).position),
                torsoForwardLocal);
            float right = Vector3.Dot(
                torso.InverseTransformDirection(
                    HandBone(Player3DAnatomicalPart.RightHand).position -
                    HandBone(Player3DAnatomicalPart.RightUpperArm).position),
                torsoForwardLocal);
            return Mathf.Min(left, right);
        }

        private Transform HandBone(Player3DAnatomicalPart part)
        {
            Assert.That(
                registry.TryGetPart(part, out var binding) && binding != null,
                Is.True,
                $"The hero rig binds {part}.");
            Assert.That(binding.Bone, Is.Not.Null);
            return binding.Bone;
        }

        [UnityTest]
        public IEnumerator WallOnTheRight_HandReachesItWhenHeTips()
        {
            Vector3 origin = root.position;
            float wallPlaneX = origin.x + WallOffset;
            wallObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallObject.name = "Balance Test Wall";
            wallObject.transform.position = new Vector3(
                wallPlaneX + 0.05f,
                1.5f,
                origin.z);
            wallObject.transform.localScale = new Vector3(0.1f, 3f, 4f);
            Physics.SyncTransforms();

            yield return Prepare(100, ModelGraceSeconds);
            Assert.That(
                registry.TryGetPart(
                    Player3DAnatomicalPart.RightHand,
                    out Player3DAnatomicalPartBinding rightHandBinding) &&
                rightHandBinding != null &&
                rightHandBinding.Bone != null,
                Is.True,
                "The production registry binds the right hand.");
            Transform rightHand = rightHandBinding.Bone;

            bool reachedWithRightHand = false;
            bool wallEverWithinReach = false;
            bool shoved = false;
            int framesSinceReach = 0;
            int heavyHandFrames = 0;
            float maxInstability = 0f;
            float maxWeight = 0f;
            for (int frame = 0; frame < WallFrameBudget; frame++)
            {
                if (!reachedWithRightHand &&
                    !shoved &&
                    frame >= NaturalWallFrameBudget)
                {
                    // He did not tip toward the wall on his own: shove him
                    // at it. Positive x is his right while he faces +z.
                    status.Balance.InjectPerturbation(
                        new Vector2(ShoveMetresPerSecond, 0f));
                    shoved = true;
                }

                yield return null;
                presentation.ReapplyLatePresentationPose();

                Vector3 position = root.position;
                Assert.That(
                    position.x + ControllerRadius,
                    Is.LessThanOrEqualTo(wallPlaneX + 0.01f),
                    $"The capsule must never be driven into the wall " +
                    $"(frame {frame}, root x {position.x:F4}).");

                maxInstability = Mathf.Max(
                    maxInstability,
                    status.Balance.Instability);
                wallEverWithinReach |= status.Balance.WallWithinReach;
                PlayerWallReachPose reach = status.Balance.WallReach;
                maxWeight = Mathf.Max(maxWeight, reach.Weight);
                if (reach.Active && reach.Weight > 0.5f)
                {
                    Assert.That(
                        reach.RightHand,
                        Is.True,
                        $"The only wall is on his right; the right hand " +
                        $"reaches for it (frame {frame}).");
                    reachedWithRightHand = true;
                }

                if (reach.Active && reach.RightHand && reach.Weight > 0.9f)
                {
                    heavyHandFrames++;
                    Assert.That(
                        Mathf.Abs(rightHand.position.x - wallPlaneX),
                        Is.LessThanOrEqualTo(0.06f),
                        $"With the hand at full weight the right hand bone " +
                        $"must sit on the wall plane (frame {frame}, hand x " +
                        $"{rightHand.position.x:F3}, wall x {wallPlaneX:F3}, " +
                        $"palm target x {reach.WorldPosition.x:F3}).");
                }

                if (reachedWithRightHand && ++framesSinceReach > 45)
                {
                    break;
                }
            }

            if (!reachedWithRightHand)
            {
                Assert.Inconclusive(
                    $"The hand never took the wall in " +
                    $"{WallFrameBudget / FramesPerSecond} s (shoved: {shoved}); " +
                    $"max instability {maxInstability:F3}, wall ever within " +
                    $"reach: {wallEverWithinReach}, max hand weight {maxWeight:F2}.");
            }

            Assert.That(
                heavyHandFrames,
                Is.GreaterThan(0),
                $"The hand must reach full weight at least once while " +
                $"holding (max weight {maxWeight:F2}).");
        }

        [UnityTest]
        public IEnumerator FallAllowed_FollowsGraceAndLevel()
        {
            yield return Prepare(100, 0f);
            Assert.That(GameSessionState.BalanceCheckDelayRemaining, Is.Zero);
            Assert.That(
                status.Balance.FallAllowedNow,
                Is.True,
                "Level 100, no grace, flat ground: a fall is possible.");

            GameSessionState.UpdateDrinkingProgress(60, DrinkId.Vodka, 5);
            yield return null;
            yield return null;
            Assert.That(
                status.Balance.FallAllowedNow,
                Is.False,
                "At the threshold the level no longer allows a fall.");
            Assert.That(status.Balance.IsActive, Is.True);
            Assert.That(status.IsFalling, Is.False);
        }

        [UnityTest]
        public IEnumerator ForcedFall_FreezesTheModelAndReseeds()
        {
            yield return Prepare(100, 0f);
            yield return RunForcedFall(-1f);

            Assert.That(
                status.Balance.Model.LostBalance,
                Is.False,
                "The next episode starts on a fresh model.");
            Assert.That(
                GameSessionState.BalanceCheckSequence,
                Is.EqualTo(1),
                "One episode was consumed by the fall.");
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.GreaterThan(
                    IntoxicationStatusController.PostFallGraceDuration - 1f),
                "A fall arms the post-fall grace in the session.");
            int active = 0;
            while (!status.Balance.IsActive && active < 10)
            {
                yield return null;
                active++;
            }

            Assert.That(
                status.Balance.IsActive,
                Is.True,
                "Back on his feet, the model runs again.");
            Assert.That(
                status.Balance.FallAllowedNow,
                Is.False,
                "Balance cannot be lost again inside the grace.");
        }

        [UnityTest]
        public IEnumerator ForcedFall_NextEpisodeGetsAFreshSeed()
        {
            yield return Prepare(100, 0f);
            int firstSeed = status.Balance.Model.Seed;
            Assert.That(
                firstSeed,
                Is.EqualTo(PlayerBalanceRules.EpisodeSeed(TestCitySeed, 0)),
                "The first episode is seeded from the city seed and " +
                "sequence 0.");
            yield return RunForcedFall(1f);

            int secondSeed = status.Balance.Model.Seed;
            Assert.That(
                secondSeed,
                Is.Not.EqualTo(firstSeed),
                "The episode after a fall must not replay the stagger " +
                "that caused it.");
            Assert.That(
                secondSeed,
                Is.EqualTo(
                    PlayerBalanceRules.EpisodeSeed(
                        TestCitySeed,
                        GameSessionState.BalanceCheckSequence)),
                "The next episode is seeded from the sequence the session " +
                "now reports.");
        }

        /// <summary>
        /// Forces a fall in <paramref name="direction"/> and waits for the
        /// ordinary Fall → ragdoll → Rise to finish, asserting the freeze
        /// on the way in. The fall runs on unscaled time, which the pinned
        /// clock leaves alone, so the budget is a frame count large enough
        /// for a few real seconds of batch-mode frames.
        /// </summary>
        private IEnumerator RunForcedFall(float direction)
        {
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(status.IsFalling, Is.False);
            Assert.That(
                status.DebugForceLoseBalance(direction),
                Is.True,
                "A grounded, unblocked hero above the threshold can be " +
                "made to lose his balance.");
            Assert.That(status.Balance.Model.LostBalance, Is.True);
            Assert.That(
                status.Balance.Model.FallDirection,
                Is.EqualTo(direction));

            int frames = 0;
            while (!status.IsFalling && frames < 10)
            {
                yield return null;
                frames++;
            }

            Assert.That(
                status.IsFalling,
                Is.True,
                "The status controller runs the fall the model latched.");
            Assert.That(
                status.Balance.IsActive,
                Is.False,
                "The model is frozen while the fall plays.");
            Assert.That(status.FallDirection, Is.EqualTo(direction));
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(
                presentation.BalancePose.Weight,
                Is.Zero,
                "A frozen model shows a neutral pose.");

            // The fall timeline runs on unscaled time, which the pinned
            // clock leaves alone: batch frames can be a millisecond apart,
            // so a frame budget is not a duration. Wait on the real clock.
            float fallDeadline = Time.realtimeSinceStartup + 10f;
            while (status.IsFalling &&
                   Time.realtimeSinceStartup < fallDeadline)
            {
                yield return null;
            }

            Assert.That(
                status.IsFalling,
                Is.False,
                "The fall must finish within ten real seconds " +
                $"(state {status.BalanceStateName}).");
            Assert.That(motor.InputEnabled, Is.True);
        }

        /// <summary>
        /// Sets the level, initializes the status controller on the hero,
        /// arms the model-only grace the sample asks for, and gives the
        /// controllers two frames to take hold.
        /// </summary>
        private IEnumerator Prepare(int level, float modelGraceSeconds)
        {
            if (level > 0)
            {
                GameSessionState.UpdateDrinkingProgress(
                    level,
                    DrinkId.Vodka,
                    5);
            }

            status.Initialize(runtime, cameraFollow, hud);
            Assert.That(status.Balance, Is.Not.Null);
            Assert.That(
                status.Balance == runtime.Balance,
                Is.True,
                "The status controller drives the hero's own balance " +
                "controller.");
            if (modelGraceSeconds > 0f)
            {
                status.Balance.ArmGrace(modelGraceSeconds);
            }

            yield return null;
            yield return null;
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(
                status.Balance.IsActive,
                Is.True,
                "Nothing freezes the model on a bare slab.");
        }

        /// <summary>
        /// Waits for a recovery step, first the natural one and then one
        /// provoked by a lateral shove, reapplying the late pose every
        /// frame so the boots read as drawn. Leaves the step's first
        /// active frame current.
        /// </summary>
        private IEnumerator WaitForRecoveryStep()
        {
            waitSawStep = false;
            waitProvokedStep = false;
            waitMaxInstability = 0f;
            presentation.ReapplyLatePresentationPose();
            currentWaitLeft = Planar(registry.Anchors.LeftFoot.position);
            currentWaitRight = Planar(registry.Anchors.RightFoot.position);
            previousWaitLeft = currentWaitLeft;
            previousWaitRight = currentWaitRight;
            for (int frame = 0; frame < NaturalStepFrameBudget; frame++)
            {
                yield return null;
                TrackWaitFrame();
                if (presentation.BalancePose.Step.Active)
                {
                    waitSawStep = true;
                    yield break;
                }
            }

            status.Balance.InjectPerturbation(
                new Vector2(-ShoveMetresPerSecond, 0f));
            for (int frame = 0; frame < ProvokedStepFrameBudget; frame++)
            {
                yield return null;
                TrackWaitFrame();
                if (presentation.BalancePose.Step.Active)
                {
                    waitSawStep = true;
                    waitProvokedStep = true;
                    yield break;
                }
            }
        }

        private void TrackWaitFrame()
        {
            presentation.ReapplyLatePresentationPose();
            waitMaxInstability = Mathf.Max(
                waitMaxInstability,
                status.Balance.Instability);
            previousWaitLeft = currentWaitLeft;
            previousWaitRight = currentWaitRight;
            currentWaitLeft = Planar(registry.Anchors.LeftFoot.position);
            currentWaitRight = Planar(registry.Anchors.RightFoot.position);
        }

        private IEnumerator WaitForGrounded()
        {
            int frame = 0;
            while (!motor.IsGrounded && frame < GroundedFrameBudget)
            {
                yield return null;
                frame++;
            }

            Assert.That(
                motor.IsGrounded,
                Is.True,
                "The hero must settle on the slab before a sample starts.");
        }

        private static IEnumerator Frames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                yield return null;
            }
        }

        private Transform FootAnchor(FootSide side)
        {
            Transform anchor = side == FootSide.Left
                ? registry.Anchors.LeftFoot
                : registry.Anchors.RightFoot;
            if (anchor != null)
            {
                return anchor;
            }

            Player3DAnatomicalPart part = side == FootSide.Left
                ? Player3DAnatomicalPart.LeftFoot
                : Player3DAnatomicalPart.RightFoot;
            Assert.That(
                registry.TryGetPart(part, out Player3DAnatomicalPartBinding binding) &&
                binding != null &&
                binding.Bone != null,
                Is.True,
                $"The registry must expose the {side} foot.");
            return binding.Bone;
        }

        private Vector3 PlanarForward()
        {
            Vector3 forward = root.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
        }

        private Vector3 PlanarRight()
        {
            return Vector3.Cross(Vector3.up, PlanarForward());
        }

        private static Vector2 Planar(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static void ResetSession()
        {
            GameSessionState.SetCitySeed(
                GameSessionState.DefaultCitySeed);
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
        }
    }
}
