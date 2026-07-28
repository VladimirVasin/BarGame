using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
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

        private const float MovementTimeoutSeconds = 2f;
        private const float MinimumMovingSpeed = 0.25f;
        private const float MaximumMovingSpeed = 2.6f;
        private const float Deceleration = 11f;

        private GameObject playerObject;
        private GameObject cameraObject;
        private PlayerMotor motor;
        private Camera movementCamera;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            inputFixture = new InputTestFixture();
            inputFixture.Setup();

            playerObject = new GameObject("Player Motor Heading Test Player");
            playerObject.transform.position = new Vector3(0f, 100f, 0f);
            playerObject.AddComponent<CharacterController>();
            motor = playerObject.AddComponent<PlayerMotor>();

            cameraObject = new GameObject("Player Motor Heading Test Camera");
            movementCamera = cameraObject.AddComponent<Camera>();
            movementCamera.enabled = false;
            motor.Initialize(movementCamera, null, null);

            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
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
        public IEnumerator ForwardInput_MovesCameraRelativeAndFacesActualVelocity()
        {
            movementCamera.transform.rotation =
                Quaternion.Euler(24f, 73f, 0f);
            playerObject.transform.rotation =
                Quaternion.Euler(0f, 180f, 0f);
            Vector3 expectedDirection = Vector3.ProjectOnPlane(
                movementCamera.transform.forward,
                Vector3.up).normalized;

            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            yield return WaitForMovement();

            Vector3 actualVelocity = motor.PlanarVelocity;
            Assert.That(
                Vector3.Angle(actualVelocity, expectedDirection),
                Is.LessThan(0.1f),
                "W movement must follow the camera's planar forward direction.");
            Assert.That(
                Vector3.Angle(
                    playerObject.transform.forward,
                    actualVelocity.normalized),
                Is.LessThan(0.1f),
                "The player root must face its actual planar velocity.");

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReleasedInput_PreservesHeadingWhenCameraRotates()
        {
            movementCamera.transform.rotation =
                Quaternion.Euler(18f, 41f, 0f);
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

            movementCamera.transform.rotation =
                Quaternion.Euler(18f, 167f, 0f);
            yield return null;
            yield return null;

            Assert.That(
                motor.PlanarVelocity.sqrMagnitude,
                Is.LessThan(0.0001f),
                "Rotating the camera without input must not move the player.");
            AssertHeadingUnchanged(heldHeading, heldYaw);
        }

        [UnityTest]
        public IEnumerator HeldThenReleasedInput_AcceleratesAndCoastsToStop()
        {
            movementCamera.transform.rotation = Quaternion.identity;
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
                Time.realtimeSinceStartup + MovementTimeoutSeconds;
            while (firstBrakingSpeed >= cruisingSpeed - 0.01f &&
                   Time.realtimeSinceStartup < brakingSampleDeadline)
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
        public IEnumerator OppositeInput_BrakesBeforeReversing()
        {
            movementCamera.transform.rotation = Quaternion.identity;
            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            yield return WaitForSpeed(2.5f);

            Vector3 originalDirection =
                motor.PlanarVelocity.normalized;
            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;
            inputFixture.Press(
                keyboard.sKey,
                queueEventOnly: true);

            float reversalStartedAt = Time.time;
            float reversalDeadline =
                Time.realtimeSinceStartup + MovementTimeoutSeconds;
            while (Vector3.Dot(
                       motor.PlanarVelocity,
                       originalDirection) >= 0f &&
                   Time.realtimeSinceStartup < reversalDeadline)
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
                   Time.realtimeSinceStartup < reversalDeadline)
            {
                yield return null;
            }

            Assert.That(
                Vector3.Angle(
                    playerObject.transform.forward,
                    motor.PlanarVelocity.normalized),
                Is.LessThan(0.1f));

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
                keyboard.dKey,
                queueEventOnly: true);
            yield return WaitForSpeed(1f);
            inputFixture.Release(
                keyboard.dKey,
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
        public IEnumerator BlockedBoundary_DoesNotStoreHiddenMomentum()
        {
            var area = new ToggleWalkableArea
            {
                Blocked = true
            };
            motor.Initialize(movementCamera, area, null);
            Vector3 blockedPosition = playerObject.transform.position;

            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            float blockedDeadline = Time.time + 0.5f;
            while (Time.time < blockedDeadline)
            {
                yield return null;
                Assert.That(
                    motor.PlanarVelocity,
                    Is.EqualTo(Vector3.zero));
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

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
        }

        private IEnumerator WaitForMovement()
        {
            yield return WaitForSpeed(MinimumMovingSpeed);
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
    }
}
