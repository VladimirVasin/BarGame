using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerMotorHeadingPlayModeTests
    {
        private const float MovementTimeoutSeconds = 2f;
        private const float MinimumMovingSpeed = 0.25f;

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

        private IEnumerator WaitForMovement()
        {
            float deadline =
                Time.realtimeSinceStartup + MovementTimeoutSeconds;
            while (motor.PlanarVelocity.sqrMagnitude <
                       MinimumMovingSpeed * MinimumMovingSpeed &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                motor.PlanarVelocity.magnitude,
                Is.GreaterThanOrEqualTo(MinimumMovingSpeed),
                "The motor did not reach a non-zero planar velocity in time.");
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
