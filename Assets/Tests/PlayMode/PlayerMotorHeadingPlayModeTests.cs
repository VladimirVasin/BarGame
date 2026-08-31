using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerMotorHeadingPlayModeTests
    {
        private sealed class ToggleWalkableArea : IWalkableArea
        {
            public bool Blocked { get; set; }

            public bool Contains(Vector3 position, float radius = 0f)
            {
                return true;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius = 0f)
            {
                return Blocked
                    ? currentPosition
                    : desiredPosition;
            }
        }

        private sealed class RecordingMotionPresentation :
            IPlayerMotionPresentation
        {
            public PlayerMotionSample LastMotion { get; private set; }

            public void SetMotion(in PlayerMotionSample motion)
            {
                LastMotion = motion;
            }
        }

        private const float MovementTimeoutSeconds = 2f;
        private const float MinimumMovingSpeed = 0.25f;
        private const float MaximumMovingSpeed = 2.6f;
        private const float MaximumRunningSpeed = 4.2f;
        private const float BackwardMaximumSpeed = 1.4f;
        private const float Deceleration = 11f;

        private GameObject playerObject;
        private PlayerMotor motor;
        private RecordingMotionPresentation presentation;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private Gamepad gamepad;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            inputFixture = new InputTestFixture();
            inputFixture.Setup();

            playerObject = new GameObject("Player Motor Heading Test Player");
            playerObject.transform.position = new Vector3(0f, 100f, 0f);
            playerObject.AddComponent<CharacterController>();
            motor = playerObject.AddComponent<PlayerMotor>();
            presentation = new RecordingMotionPresentation();
            motor.Initialize(null, presentation);

            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            if (gamepad != null && gamepad.added)
            {
                InputSystem.RemoveDevice(gamepad);
            }

            if (playerObject != null)
            {
                Object.Destroy(playerObject);
            }

            inputFixture?.TearDown();
            inputFixture = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArrowKeys_NoLongerMoveTheHero()
        {
            inputFixture.Press(
                keyboard.upArrowKey,
                queueEventOnly: true);
            inputFixture.Press(
                keyboard.rightArrowKey,
                queueEventOnly: true);

            float deadline = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Assert.That(
                    motor.PlanarVelocity.magnitude,
                    Is.LessThan(0.001f),
                    "The arrow keys belong to the camera orbit and " +
                    "must not walk the hero.");
            }

            inputFixture.Release(
                keyboard.upArrowKey,
                queueEventOnly: true);
            inputFixture.Release(
                keyboard.rightArrowKey,
                queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ForwardInput_MovesAlongOwnForwardWithoutTurning()
        {
            playerObject.transform.rotation =
                Quaternion.Euler(0f, 180f, 0f);
            Vector3 expectedDirection = playerObject.transform.forward;
            float expectedYaw = playerObject.transform.eulerAngles.y;

            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            yield return WaitForMovement();

            Assert.That(
                Vector3.Angle(motor.PlanarVelocity, expectedDirection),
                Is.LessThan(0.1f),
                "W must walk along the hero's own forward axis.");
            AssertHeadingUnchanged(expectedDirection, expectedYaw);

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LeftShift_RunsForwardAndReleaseReturnsToWalk()
        {
            QueueKeyboardState(Key.W, Key.LeftShift);
            yield return WaitForSpeed(MaximumRunningSpeed - 0.1f);

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.InRange(
                    MaximumRunningSpeed - 0.1f,
                    MaximumRunningSpeed + 0.05f));
            Assert.That(
                presentation.LastMotion.RunBlend,
                Is.GreaterThan(0.9f),
                "The measured gait must reach its run clip at the run cap.");

            float runningSpeed = motor.PlanarVelocity.magnitude;
            float runningBlend = presentation.LastMotion.RunBlend;
            QueueKeyboardState(Key.W);
            yield return null;

            float expectedFirstReleaseSpeed = Mathf.Max(
                MaximumMovingSpeed,
                runningSpeed - Deceleration * Time.deltaTime);
            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.EqualTo(expectedFirstReleaseSpeed).Within(0.12f),
                "Releasing Shift while W stays held must use ordinary " +
                "deceleration instead of forcing a gait-speed snap.");
            float expectedFirstReleaseBlend = Mathf.InverseLerp(
                MaximumMovingSpeed,
                MaximumRunningSpeed,
                motor.PlanarVelocity.magnitude);
            Assert.That(
                presentation.LastMotion.RunBlend,
                Is.EqualTo(expectedFirstReleaseBlend).Within(0.03f)
                    .And.LessThan(runningBlend),
                "The run gait must follow measured speed after Shift is " +
                "released.");

            float previousBlend = presentation.LastMotion.RunBlend;
            float deadline =
                Time.realtimeSinceStartup + MovementTimeoutSeconds;
            while (motor.PlanarVelocity.magnitude >
                       MaximumMovingSpeed + 0.02f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Assert.That(
                    presentation.LastMotion.RunBlend,
                    Is.LessThanOrEqualTo(previousBlend + 0.02f),
                    "Run blend must decay monotonically with the actual " +
                    "forward speed.");
                previousBlend = presentation.LastMotion.RunBlend;
            }

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.InRange(
                    MaximumMovingSpeed - 0.05f,
                    MaximumMovingSpeed + 0.05f));
            Assert.That(
                presentation.LastMotion.RunBlend,
                Is.LessThanOrEqualTo(0.02f));

            QueueKeyboardState();
        }

        [UnityTest]
        public IEnumerator RightShift_RunsForwardToSameCap()
        {
            QueueKeyboardState(Key.W, Key.RightShift);
            yield return WaitForSpeed(MaximumRunningSpeed - 0.1f);

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.InRange(
                    MaximumRunningSpeed - 0.1f,
                    MaximumRunningSpeed + 0.05f));
            Assert.That(
                presentation.LastMotion.RunBlend,
                Is.GreaterThan(0.9f));

            QueueKeyboardState();
        }

        [UnityTest]
        public IEnumerator SprintAndTurn_ReachesRunCapWithoutSkidding()
        {
            float startYaw = playerObject.transform.eulerAngles.y;
            QueueKeyboardState(Key.W, Key.D, Key.LeftShift);
            yield return WaitForSpeed(MaximumRunningSpeed - 0.1f);

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.InRange(
                    MaximumRunningSpeed - 0.1f,
                    MaximumRunningSpeed + 0.05f),
                "A running arc must retain the canonical run cap.");
            Assert.That(
                Vector3.Angle(
                    motor.PlanarVelocity,
                    playerObject.transform.forward),
                Is.LessThan(0.5f),
                "Tank steering must rotate earned momentum with the hero " +
                "instead of producing a lateral skid.");
            Assert.That(
                presentation.LastMotion.RunBlend,
                Is.GreaterThan(0.9f));
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(
                    startYaw,
                    playerObject.transform.eulerAngles.y)),
                Is.GreaterThan(20f));

            QueueKeyboardState();
        }

        [UnityTest]
        public IEnumerator GamepadLeftStickPress_RunsForwardToSameCap()
        {
            inputFixture.Set(
                gamepad.leftStick,
                Vector2.up,
                queueEventOnly: true);
            inputFixture.Press(
                gamepad.leftStickButton,
                queueEventOnly: true);
            yield return WaitForSpeed(MaximumRunningSpeed - 0.1f);

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.InRange(
                    MaximumRunningSpeed - 0.1f,
                    MaximumRunningSpeed + 0.05f));
            Assert.That(
                presentation.LastMotion.RunBlend,
                Is.GreaterThan(0.9f));

            inputFixture.Release(
                gamepad.leftStickButton,
                queueEventOnly: true);
            inputFixture.Set(
                gamepad.leftStick,
                Vector2.zero,
                queueEventOnly: true);
        }

        [UnityTest]
        public IEnumerator Sprint_DoesNotAccelerateBackwardOrYaw()
        {
            QueueKeyboardState(Key.S, Key.LeftShift);
            yield return WaitForSpeed(BackwardMaximumSpeed - 0.1f);

            float backwardDeadline = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < backwardDeadline)
            {
                yield return null;
                Assert.That(
                    motor.PlanarVelocity.magnitude,
                    Is.LessThanOrEqualTo(BackwardMaximumSpeed + 0.05f));
                Assert.That(
                    presentation.LastMotion.RunBlend,
                    Is.Zero.Within(0.001f));
            }

            QueueKeyboardState(Key.LeftShift);
            yield return WaitForStop();

            float startYaw = playerObject.transform.eulerAngles.y;
            float elapsed = 0f;
            QueueKeyboardState(Key.D, Key.LeftShift);
            while (elapsed < 0.25f)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            float yawDelta = Mathf.DeltaAngle(
                startYaw,
                playerObject.transform.eulerAngles.y);
            Assert.That(
                yawDelta,
                Is.EqualTo(150f * elapsed).Within(1f),
                "Shift must not multiply the ordinary yaw rate.");
            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.LessThan(0.001f));

            QueueKeyboardState();
        }

        [UnityTest]
        public IEnumerator Intoxication_ScalesRunCapButKeepsFullRunBlend()
        {
            const float intoxicationSpeedMultiplier = 0.7f;
            float expectedRunSpeed =
                MaximumRunningSpeed * intoxicationSpeedMultiplier;
            motor.SetSpeedMultiplier(intoxicationSpeedMultiplier);
            QueueKeyboardState(Key.W, Key.LeftShift);
            yield return WaitForSpeed(expectedRunSpeed - 0.08f);

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.InRange(
                    expectedRunSpeed - 0.08f,
                    expectedRunSpeed + 0.05f));
            Assert.That(
                presentation.LastMotion.RunBlend,
                Is.GreaterThan(0.9f),
                "The run blend must normalize against the intoxicated " +
                "walk and run caps, not the sober constants.");

            QueueKeyboardState();
        }

        [UnityTest]
        public IEnumerator BackwardInput_WalksBackwardWithoutTurning()
        {
            playerObject.transform.rotation =
                Quaternion.Euler(0f, 90f, 0f);
            Vector3 heading = playerObject.transform.forward;
            float expectedYaw = playerObject.transform.eulerAngles.y;

            inputFixture.Press(
                keyboard.sKey,
                queueEventOnly: true);
            yield return WaitForMovement();

            Assert.That(
                Vector3.Angle(motor.PlanarVelocity, -heading),
                Is.LessThan(0.1f),
                "S must back the hero up along his own forward axis " +
                "instead of turning him around.");
            AssertHeadingUnchanged(heading, expectedYaw);

            float sampleDeadline = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < sampleDeadline)
            {
                yield return null;
                Assert.That(
                    motor.PlanarVelocity.magnitude,
                    Is.LessThanOrEqualTo(BackwardMaximumSpeed + 0.05f),
                    "Backing up must stay slower than walking forward.");
            }

            AssertHeadingUnchanged(heading, expectedYaw);
            inputFixture.Release(
                keyboard.sKey,
                queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TurnInput_RotatesInPlaceWithoutTranslation()
        {
            Vector3 startPosition = playerObject.transform.position;
            float startYaw = playerObject.transform.eulerAngles.y;

            inputFixture.Press(
                keyboard.aKey,
                queueEventOnly: true);
            float deadline = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Assert.That(
                    motor.PlanarVelocity.magnitude,
                    Is.LessThan(0.001f),
                    "A alone must not translate the hero.");
            }

            inputFixture.Release(
                keyboard.aKey,
                queueEventOnly: true);
            AssertPlanarPositionUnchanged(startPosition);
            float leftDelta = Mathf.DeltaAngle(
                startYaw,
                playerObject.transform.eulerAngles.y);
            Assert.That(
                leftDelta,
                Is.LessThan(-5f),
                "A must yaw the hero to his left on the spot.");

            yield return null;
            startYaw = playerObject.transform.eulerAngles.y;
            inputFixture.Press(
                keyboard.dKey,
                queueEventOnly: true);
            deadline = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            inputFixture.Release(
                keyboard.dKey,
                queueEventOnly: true);
            AssertPlanarPositionUnchanged(startPosition);
            float rightDelta = Mathf.DeltaAngle(
                startYaw,
                playerObject.transform.eulerAngles.y);
            Assert.That(
                rightDelta,
                Is.GreaterThan(5f),
                "D must yaw the hero to his right on the spot.");
        }

        [UnityTest]
        public IEnumerator ForwardPlusTurn_MovesAlongAnArc()
        {
            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            inputFixture.Press(
                keyboard.dKey,
                queueEventOnly: true);
            yield return WaitForSpeed(2f);

            Vector3 startPosition = playerObject.transform.position;
            float startYaw = playerObject.transform.eulerAngles.y;
            float deadline = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            float yawDelta = Mathf.DeltaAngle(
                startYaw,
                playerObject.transform.eulerAngles.y);
            Assert.That(
                yawDelta,
                Is.GreaterThan(10f),
                "Holding W+D must keep turning while walking.");
            Assert.That(
                PlanarDistance(
                    startPosition,
                    playerObject.transform.position),
                Is.GreaterThan(0.3f),
                "Holding W+D must keep the hero travelling.");

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            inputFixture.Release(
                keyboard.dKey,
                queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReleasedInput_PreservesHeadingWhileCoasting()
        {
            playerObject.transform.rotation =
                Quaternion.Euler(0f, 205f, 0f);

            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            yield return WaitForMovement();

            Vector3 heldHeading = playerObject.transform.forward;
            float heldYaw = playerObject.transform.eulerAngles.y;

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return WaitForStop();

            AssertHeadingUnchanged(heldHeading, heldYaw);
        }

        [UnityTest]
        public IEnumerator HeldThenReleasedInput_AcceleratesAndCoastsToStop()
        {
            Vector3 startingPosition = playerObject.transform.position;

            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);

            float previousSpeed = 0f;
            float earlySampleDeadline = Time.time + 0.22f;
            while (Time.time < earlySampleDeadline)
            {
                yield return null;
                float speed = motor.PlanarVelocity.magnitude;
                Assert.That(
                    speed,
                    Is.GreaterThanOrEqualTo(previousSpeed - 0.08f),
                    "Acceleration must build speed monotonically.");
                previousSpeed = speed;
            }

            float earlySpeed = motor.PlanarVelocity.magnitude;
            Assert.That(
                earlySpeed,
                Is.GreaterThan(0.4f).And.LessThan(2.35f),
                "The player must move promptly without reaching maximum " +
                "speed during the first fraction of a second.");

            float accelerationDeadline =
                Time.realtimeSinceStartup + MovementTimeoutSeconds;
            while (motor.PlanarVelocity.magnitude < 2.5f &&
                   Time.realtimeSinceStartup < accelerationDeadline)
            {
                yield return null;
                float speed = motor.PlanarVelocity.magnitude;
                Assert.That(
                    speed,
                    Is.GreaterThanOrEqualTo(previousSpeed - 0.08f));
                previousSpeed = speed;
            }

            float cruisingSpeed = motor.PlanarVelocity.magnitude;
            Assert.That(
                cruisingSpeed,
                Is.InRange(2.5f, MaximumMovingSpeed + 0.05f));
            Assert.That(
                playerObject.transform.position.z -
                startingPosition.z,
                Is.GreaterThan(0.3f));

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            Vector3 releasePosition = playerObject.transform.position;
            float firstBrakingSpeed = cruisingSpeed;
            float firstBrakingDeltaTime = 0f;
            float brakingSampleDeadline =
                Time.time + MovementTimeoutSeconds;
            while (firstBrakingSpeed >= cruisingSpeed - 0.01f &&
                   Time.time < brakingSampleDeadline)
            {
                releasePosition = playerObject.transform.position;
                yield return null;
                firstBrakingDeltaTime = Time.deltaTime;
                firstBrakingSpeed = motor.PlanarVelocity.magnitude;
            }

            float expectedFirstBrakingSpeed = Mathf.Max(
                0f,
                cruisingSpeed - Deceleration * firstBrakingDeltaTime);
            Assert.That(
                firstBrakingSpeed,
                Is.EqualTo(expectedFirstBrakingSpeed).Within(0.12f),
                "The first processed release frame must apply the configured " +
                "deceleration, including when that frame is unusually long.");
            Assert.That(
                firstBrakingSpeed,
                Is.LessThan(cruisingSpeed - 0.01f),
                "Releasing input must begin braking.");

            previousSpeed = firstBrakingSpeed;
            float brakingDeadline =
                Time.realtimeSinceStartup + MovementTimeoutSeconds;
            while (motor.PlanarVelocity.magnitude > 0.02f &&
                   Time.realtimeSinceStartup < brakingDeadline)
            {
                yield return null;
                float speed = motor.PlanarVelocity.magnitude;
                Assert.That(
                    speed,
                    Is.LessThanOrEqualTo(previousSpeed + 0.08f),
                    "Braking speed must decay monotonically.");
                previousSpeed = speed;
            }

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.LessThanOrEqualTo(0.02f));
            float coastingDistance = Vector3.Distance(
                Vector3.ProjectOnPlane(releasePosition, Vector3.up),
                Vector3.ProjectOnPlane(
                    playerObject.transform.position,
                    Vector3.up));
            Assert.That(
                coastingDistance,
                Is.InRange(0.18f, 0.55f),
                "The heavy stop needs a visible but controllable coasting " +
                "distance.");
        }

        [UnityTest]
        public IEnumerator OppositeInput_BrakesThenBacksUpWithoutTurning()
        {
            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            yield return WaitForSpeed(2.5f);

            Vector3 originalDirection =
                motor.PlanarVelocity.normalized;
            Vector3 heldHeading = playerObject.transform.forward;
            float heldYaw = playerObject.transform.eulerAngles.y;
            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;
            inputFixture.Press(
                keyboard.sKey,
                queueEventOnly: true);

            float reversalStartedAt = Time.time;
            float reversalDeadline = Time.time + MovementTimeoutSeconds;
            while (Vector3.Dot(
                       motor.PlanarVelocity,
                       originalDirection) >= 0f &&
                   Time.time < reversalDeadline)
            {
                yield return null;
            }

            Assert.That(
                Time.time - reversalStartedAt,
                Is.GreaterThan(0.12f),
                "Opposite input must brake the existing momentum before " +
                "reversing it.");
            Assert.That(
                Vector3.Dot(
                    motor.PlanarVelocity,
                    originalDirection),
                Is.LessThan(0f));

            while (Vector3.Dot(
                       motor.PlanarVelocity,
                       originalDirection) > -MinimumMovingSpeed &&
                   Time.time < reversalDeadline)
            {
                yield return null;
            }

            Assert.That(
                Vector3.Angle(
                    motor.PlanarVelocity,
                    -heldHeading),
                Is.LessThan(0.1f),
                "The reversal must back the hero up along his facing, " +
                "never spin him around.");
            AssertHeadingUnchanged(heldHeading, heldYaw);

            inputFixture.Release(
                keyboard.sKey,
                queueEventOnly: true);
        }

        [UnityTest]
        public IEnumerator InputDisableAndTeleport_StopPlanarMotionImmediately()
        {
            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            yield return WaitForSpeed(1f);

            motor.SetInputEnabled(false);
            Assert.That(motor.PlanarVelocity, Is.EqualTo(Vector3.zero));
            Vector3 disabledPosition = playerObject.transform.position;
            yield return null;
            yield return null;
            AssertPlanarPositionUnchanged(disabledPosition);

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;
            motor.SetInputEnabled(true);
            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            yield return WaitForSpeed(1f);
            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);

            Vector3 teleportedPosition =
                new Vector3(4f, 100f, 2f);
            motor.Teleport(teleportedPosition);
            Assert.That(motor.PlanarVelocity, Is.EqualTo(Vector3.zero));
            yield return null;
            yield return null;
            AssertPlanarPositionUnchanged(teleportedPosition);
        }

        [UnityTest]
        public IEnumerator ComponentDisable_StopsActiveRunImmediately()
        {
            QueueKeyboardState(Key.W, Key.LeftShift);
            yield return WaitForSpeed(MaximumRunningSpeed - 0.1f);

            motor.enabled = false;

            Assert.That(motor.PlanarVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(
                presentation.LastMotion.RunBlend,
                Is.Zero.Within(0.001f));

            QueueKeyboardState();
        }

        [UnityTest]
        public IEnumerator BlockedBoundary_DoesNotStoreHiddenMomentum()
        {
            var area = new ToggleWalkableArea
            {
                Blocked = true
            };
            motor.Initialize(area, presentation);
            Vector3 blockedPosition = playerObject.transform.position;

            QueueKeyboardState(Key.W, Key.LeftShift);
            float blockedDeadline = Time.time + 0.5f;
            while (Time.time < blockedDeadline)
            {
                yield return null;
                Assert.That(
                    motor.PlanarVelocity,
                    Is.EqualTo(Vector3.zero));
                Assert.That(
                    presentation.LastMotion.RunBlend,
                    Is.Zero.Within(0.001f),
                    "Blocked sprint must be stationary in presentation too.");
                AssertPlanarPositionUnchanged(blockedPosition);
            }

            area.Blocked = false;
            yield return null;

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.GreaterThan(0f)
                    .And.LessThanOrEqualTo(
                        6.5f * Time.deltaTime + 0.1f),
                "Leaving a boundary must start from rest instead of " +
                "releasing stored momentum.");

            QueueKeyboardState();
        }

        [Test]
        public void InteractionPoseMove_ApproachesWithoutOvershootAndAligns()
        {
            motor.SetInputEnabled(false);
            Vector3 start = playerObject.transform.position;
            Vector3 target = start + new Vector3(0.52f, 0f, 0.18f);
            Quaternion targetRotation =
                Quaternion.LookRotation(Vector3.left, Vector3.up);
            float previousDistance = PlanarDistance(start, target);
            bool completed = false;

            for (int step = 0; step < 20 && !completed; step++)
            {
                Vector3 before = playerObject.transform.position;
                completed = motor.MoveTowardsInteractionPose(
                    target,
                    targetRotation,
                    0.05f);
                float travelled = PlanarDistance(
                    before,
                    playerObject.transform.position);
                float distance = PlanarDistance(
                    playerObject.transform.position,
                    target);

                Assert.That(
                    travelled,
                    Is.LessThanOrEqualTo(2.6f * 0.05f + 0.0001f),
                    "A guided interaction step must not teleport or " +
                    "overshoot its authored entry point.");
                Assert.That(
                    distance,
                    Is.LessThanOrEqualTo(previousDistance + 0.0001f));
                previousDistance = distance;
            }

            Assert.That(completed, Is.True);
            Assert.That(
                PlanarDistance(playerObject.transform.position, target),
                Is.LessThanOrEqualTo(
                    PlayerMotor.InteractionPositionTolerance));
            Assert.That(
                Quaternion.Angle(
                    playerObject.transform.rotation,
                    targetRotation),
                Is.LessThanOrEqualTo(
                    PlayerMotor.InteractionRotationToleranceDegrees));
            Assert.That(motor.PlanarVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(motor.InputEnabled, Is.False);
        }

        [Test]
        public void InteractionPoseMove_ZeroDeltaDoesNotTeleportOrRotate()
        {
            motor.SetInputEnabled(false);
            Vector3 start = playerObject.transform.position;
            Quaternion startRotation = playerObject.transform.rotation;
            Vector3 target = start + new Vector3(0.52f, 0f, 0.18f);
            Quaternion targetRotation =
                Quaternion.LookRotation(Vector3.left, Vector3.up);

            bool completed = motor.MoveTowardsInteractionPose(
                target,
                targetRotation,
                0f);

            Assert.That(completed, Is.False);
            Assert.That(playerObject.transform.position, Is.EqualTo(start));
            Assert.That(
                playerObject.transform.rotation,
                Is.EqualTo(startRotation));
            Assert.That(motor.PlanarVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(motor.InteractionPoseMoveActive, Is.True);
            Assert.That(motor.InputEnabled, Is.False);
        }

        [Test]
        public void InteractionPoseMove_VerticalMismatchDoesNotComplete()
        {
            motor.SetInputEnabled(false);
            Vector3 start = playerObject.transform.position;
            Vector3 target = start +
                Vector3.up *
                (PlayerMotor.InteractionVerticalTolerance + 0.1f);

            bool completed = motor.MoveTowardsInteractionPose(
                target,
                playerObject.transform.rotation,
                0.05f);

            Assert.That(
                completed,
                Is.False,
                "Matching the planar pose must not hide an authored " +
                "vertical mismatch.");
            Assert.That(playerObject.transform.position, Is.EqualTo(start));
            Assert.That(motor.PlanarVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(motor.InteractionPoseMoveActive, Is.True);
            Assert.That(motor.InteractionPoseMoveStalled, Is.False);
        }

        [Test]
        public void InteractionPoseMove_BlockedPathStallsWithoutMoving()
        {
            var area = new ToggleWalkableArea
            {
                Blocked = true
            };
            motor.Initialize(area, null);
            motor.SetInputEnabled(false);
            Vector3 start = playerObject.transform.position;
            Vector3 target = start + Vector3.right * 0.5f;
            bool completed = false;

            for (int step = 0;
                 step < 10 && !motor.InteractionPoseMoveStalled;
                 step++)
            {
                completed = motor.MoveTowardsInteractionPose(
                    target,
                    Quaternion.LookRotation(Vector3.right, Vector3.up),
                    0.25f);
                Assert.That(completed, Is.False);
                Assert.That(
                    playerObject.transform.position,
                    Is.EqualTo(start),
                    "A blocked guided approach must not tunnel or " +
                    "teleport through its walkable constraint.");
                Assert.That(motor.PlanarVelocity, Is.EqualTo(Vector3.zero));
            }

            Assert.That(
                motor.InteractionPoseMoveStalled,
                Is.True,
                "A blocked authored approach must eventually terminate " +
                "through the shared no-progress guard.");
            Assert.That(motor.InteractionPoseMoveActive, Is.False);
            Assert.That(playerObject.transform.position, Is.EqualTo(start));
        }

        private IEnumerator WaitForMovement()
        {
            yield return WaitForSpeed(MinimumMovingSpeed);
        }

        private void QueueKeyboardState(params Key[] pressedKeys)
        {
            InputSystem.QueueStateEvent(
                keyboard,
                new KeyboardState(pressedKeys));
        }

        private IEnumerator WaitForSpeed(float minimumSpeed)
        {
            float deadline =
                Time.realtimeSinceStartup + MovementTimeoutSeconds;
            while (motor.PlanarVelocity.sqrMagnitude <
                       minimumSpeed * minimumSpeed &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.GreaterThanOrEqualTo(minimumSpeed),
                "The motor did not reach the requested speed in time.");
        }

        private IEnumerator WaitForStop()
        {
            float deadline =
                Time.realtimeSinceStartup + MovementTimeoutSeconds;
            while (motor.PlanarVelocity.sqrMagnitude > 0.0001f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                motor.PlanarVelocity.sqrMagnitude,
                Is.LessThanOrEqualTo(0.0001f),
                "The motor did not stop after releasing movement input.");
        }

        private void AssertPlanarPositionUnchanged(
            Vector3 expectedPosition)
        {
            Assert.That(
                Vector3.Distance(
                    Vector3.ProjectOnPlane(
                        playerObject.transform.position,
                        Vector3.up),
                    Vector3.ProjectOnPlane(
                        expectedPosition,
                        Vector3.up)),
                Is.LessThan(0.001f),
                "A hard stop must not leave planar drift.");
        }

        private void AssertHeadingUnchanged(
            Vector3 expectedForward,
            float expectedYaw)
        {
            Assert.That(
                Vector3.Angle(
                    playerObject.transform.forward,
                    expectedForward),
                Is.LessThan(0.1f),
                "Stopping must preserve the player's forward direction.");
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(
                    playerObject.transform.eulerAngles.y,
                    expectedYaw)),
                Is.LessThan(0.1f),
                "Stopping must preserve the player's yaw.");
        }

        private static float PlanarDistance(
            Vector3 first,
            Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }
    }
}
