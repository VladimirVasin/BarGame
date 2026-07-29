using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarArrivalPresentationPlayModeTests
    {
        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject cameraObject;
        private GameObject targetObject;
        private GameObject presentationObject;
        private Camera camera;
        private PlayerCameraFollow follow;
        private BarArrivalPresentation presentation;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();

            targetObject =
                new GameObject("Bar Arrival Target");
            targetObject.transform.position =
                new Vector3(0f, 100f, 0f);

            cameraObject =
                new GameObject("Bar Arrival Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            follow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(
                camera,
                targetObject.transform,
                true);
            follow.SetCinematicMotionEnabled(false);
            follow.Snap();

            presentationObject =
                new GameObject("Bar Arrival Presentation");
            presentation =
                presentationObject.AddComponent<
                    BarArrivalPresentation>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            if (mouse != null && mouse.added)
            {
                InputSystem.RemoveDevice(mouse);
            }

            Destroy(presentationObject);
            Destroy(cameraObject);
            Destroy(targetObject);
            inputFixture?.TearDown();
            inputFixture = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Timeline_UsesSameCameraAndRestoresFollowPose()
        {
            Vector3 expectedPosition = camera.transform.position;
            Quaternion expectedRotation = camera.transform.rotation;
            float expectedFieldOfView = camera.fieldOfView;
            Vector3 shotPosition =
                targetObject.transform.position +
                new Vector3(-3f, 2.7f, -5f);
            Vector3 lookAt =
                targetObject.transform.position +
                Vector3.up * 1.1f;

            presentation.Initialize(
                camera,
                follow,
                shotPosition,
                lookAt,
                64f,
                BarArrivalTimeline.MinimumDuration);

            Assert.That(presentation.IsPlaying, Is.True);
            Assert.That(
                presentation.ControlledCamera,
                Is.SameAs(camera));
            Assert.That(follow.enabled, Is.False);
            Assert.That(follow.OrbitInputEnabled, Is.False);
            AssertVector(camera.transform.position, shotPosition);
            Assert.That(camera.fieldOfView, Is.EqualTo(64f));

            presentation.AdvancePresentation(
                BarArrivalTimeline.MinimumDuration * 0.5f);
            Assert.That(
                Vector3.Distance(
                    camera.transform.position,
                    shotPosition),
                Is.GreaterThan(0.05f));
            Assert.That(
                Vector3.Distance(
                    camera.transform.position,
                    expectedPosition),
                Is.GreaterThan(0.05f));

            presentation.AdvancePresentation(
                BarArrivalTimeline.MinimumDuration * 0.5f);
            Assert.That(presentation.IsPlaying, Is.False);
            Assert.That(presentation.WasSkipped, Is.False);
            Assert.That(follow.enabled, Is.True);
            Assert.That(follow.OrbitInputEnabled, Is.True);
            AssertVector(camera.transform.position, expectedPosition);
            Assert.That(
                Quaternion.Angle(
                    camera.transform.rotation,
                    expectedRotation),
                Is.LessThan(0.01f));
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(expectedFieldOfView).Within(0.01f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MovementInput_SkipsAndRestoresPriorOrbitState()
        {
            follow.SetOrbitInputEnabled(false);
            Vector3 expectedPosition = camera.transform.position;
            Quaternion expectedRotation = camera.transform.rotation;
            float expectedFieldOfView = camera.fieldOfView;

            presentation.Initialize(
                camera,
                follow,
                targetObject.transform.position +
                new Vector3(4f, 3f, -5f),
                targetObject.transform.position +
                Vector3.up,
                62f);

            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;

            Assert.That(presentation.IsPlaying, Is.False);
            Assert.That(presentation.WasSkipped, Is.True);
            Assert.That(follow.enabled, Is.True);
            Assert.That(
                follow.OrbitInputEnabled,
                Is.False,
                "Arrival must restore a pre-existing orbit lock.");
            AssertVector(camera.transform.position, expectedPosition);
            Assert.That(
                Quaternion.Angle(
                    camera.transform.rotation,
                    expectedRotation),
                Is.LessThan(0.01f));
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(expectedFieldOfView).Within(0.01f));

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisableMidShot_RestoresDisabledFollowAndExactPose()
        {
            follow.enabled = false;
            follow.SetOrbitInputEnabled(true);
            Vector3 frozenPosition =
                new Vector3(7f, 104f, -9f);
            Quaternion frozenRotation =
                Quaternion.Euler(11f, 27f, 3f);
            camera.transform.SetPositionAndRotation(
                frozenPosition,
                frozenRotation);
            camera.fieldOfView = 49f;

            presentation.Initialize(
                camera,
                follow,
                new Vector3(-4f, 103f, -7f),
                targetObject.transform.position,
                63f);
            Assert.That(presentation.IsPlaying, Is.True);

            presentation.enabled = false;

            Assert.That(presentation.IsPlaying, Is.False);
            Assert.That(follow.enabled, Is.False);
            Assert.That(follow.OrbitInputEnabled, Is.True);
            AssertVector(camera.transform.position, frozenPosition);
            Assert.That(
                Quaternion.Angle(
                    camera.transform.rotation,
                    frozenRotation),
                Is.LessThan(0.01f));
            Assert.That(camera.fieldOfView, Is.EqualTo(49f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConfirmAndOrbitInputs_SkipIndependently()
        {
            Vector3 shotPosition =
                targetObject.transform.position +
                new Vector3(-3f, 2.5f, -5f);
            Vector3 lookAt =
                targetObject.transform.position +
                Vector3.up;

            presentation.Initialize(
                camera,
                follow,
                shotPosition,
                lookAt);
            inputFixture.Press(
                keyboard.enterKey,
                queueEventOnly: true);
            yield return null;

            Assert.That(presentation.IsPlaying, Is.False);
            Assert.That(presentation.WasSkipped, Is.True);

            inputFixture.Release(
                keyboard.enterKey,
                queueEventOnly: true);
            yield return null;
            presentation.Initialize(
                camera,
                follow,
                shotPosition,
                lookAt);
            inputFixture.Press(
                mouse.rightButton,
                queueEventOnly: true);
            yield return null;

            Assert.That(presentation.IsPlaying, Is.False);
            Assert.That(presentation.WasSkipped, Is.True);

            inputFixture.Release(
                mouse.rightButton,
                queueEventOnly: true);
            yield return null;
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThan(0.001f));
        }

        private static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
            {
                Object.Destroy(gameObject);
            }
        }
    }
}
