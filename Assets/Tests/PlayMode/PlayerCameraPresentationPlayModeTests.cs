
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerCameraPresentationPlayModeTests
    {
        private readonly List<GameObject> cleanupObjects =
            new List<GameObject>();


        [UnityTest]
        public IEnumerator ExteriorCamera_RotateYawPreservesPlayerHeadingAndAim()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Third Person Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            yield return null;

            Quaternion originalPlayerHeading = player.transform.rotation;
            Vector3 originalPlayerToCamera = Vector3.ProjectOnPlane(
                camera.transform.position - player.transform.position,
                Vector3.up).normalized;

            Assert.That(camera.orthographic, Is.False);
            Assert.That(
                Vector3.Angle(
                    originalPlayerToCamera,
                    -player.transform.forward),
                Is.LessThan(0.1f),
                "The initial camera pose should follow the actor heading.");
            Assert.That(camera.fieldOfView, Is.EqualTo(53f).Within(0.01f));

            follow.RotateYaw(135f);
            Assert.That(
                Quaternion.Angle(
                    player.transform.rotation,
                    originalPlayerHeading),
                Is.LessThan(0.001f));
            follow.Snap();
            yield return null;

            Vector3 focusPoint = player.transform.position + Vector3.up * 1.4f;
            Vector3 playerToCamera = Vector3.ProjectOnPlane(
                camera.transform.position - player.transform.position,
                Vector3.up).normalized;

            Assert.That(
                Quaternion.Angle(
                    player.transform.rotation,
                    originalPlayerHeading),
                Is.LessThan(0.001f),
                "Orbit yaw must not rotate the player root.");
            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (focusPoint - camera.transform.position).normalized),
                Is.LessThan(0.1f),
                "Orbit yaw must keep the camera aimed at the player.");
            Assert.That(
                Vector3.Angle(originalPlayerToCamera, playerToCamera),
                Is.EqualTo(135f).Within(0.1f),
                "RotateYaw must orbit the camera independently.");
        }



        [UnityTest]
        public IEnumerator ExteriorCamera_VerticalOrbitConsumesMouseInputAndClamps()
        {
            InputTestFixture inputFixture = null;
            Mouse mouse = null;
            try
            {
                inputFixture = new InputTestFixture();
                inputFixture.Setup();
                mouse = InputSystem.AddDevice<Mouse>();

                Camera camera = CreateCamera(Vector3.zero);
                GameObject player = CreateObject(
                    "Vertical Orbit Camera Target");
                player.transform.position = new Vector3(0f, 100f, 0f);
                player.transform.rotation = Quaternion.Euler(0f, 35f, 0f);

                PlayerCameraFollow follow =
                    camera.gameObject.AddComponent<PlayerCameraFollow>();
                follow.Initialize(camera, player.transform, false);
                follow.SetCinematicMotionEnabled(false);
                follow.Snap();
                yield return null;

                Quaternion playerHeading = player.transform.rotation;
                Vector3 initialPlanarForward = Vector3.ProjectOnPlane(
                    camera.transform.forward,
                    Vector3.up).normalized;
                float initialForwardY = camera.transform.forward.y;
                float initialCameraY = camera.transform.position.y;
                Assert.That(
                    follow.CurrentOrbitPitch,
                    Is.EqualTo(14f).Within(0.001f));

                follow.SetOrbitInputEnabled(false);
                inputFixture.Press(
                    mouse.rightButton,
                    queueEventOnly: true);
                inputFixture.Set(
                    mouse.delta,
                    new Vector2(0f, 60f),
                    queueEventOnly: true);
                yield return null;
                yield return null;

                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(14f).Within(0.001f),
                    "A modal orbit lock must suppress vertical input too.");

                follow.SetOrbitInputEnabled(true);
                inputFixture.Set(
                    mouse.delta,
                    new Vector2(0f, 60f),
                    queueEventOnly: true);
                yield return null;
                yield return null;
                follow.Snap();

                Assert.That(
                    follow.CurrentOrbitPitch,
                    Is.LessThan(14f),
                    "Positive mouse Y must raise the ordinary chase view.");
                Assert.That(
                    camera.transform.forward.y,
                    Is.GreaterThan(initialForwardY + 0.05f));
                Assert.That(
                    camera.transform.position.y,
                    Is.LessThan(initialCameraY - 0.1f));
                Assert.That(
                    Vector3.Angle(
                        initialPlanarForward,
                        Vector3.ProjectOnPlane(
                            camera.transform.forward,
                            Vector3.up).normalized),
                    Is.LessThan(0.1f),
                    "Pure vertical input must not change orbit yaw.");
                Assert.That(
                    Quaternion.Angle(
                        player.transform.rotation,
                        playerHeading),
                    Is.LessThan(0.001f));
                AssertCameraAimsAtFocus(camera, player.transform, 1.4f, 2.6f);

                follow.RotatePitch(10000f);
                follow.Snap();
                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(follow.MaximumOrbitPitch));
                Assert.That(
                    follow.CurrentOrbitPitch,
                    Is.EqualTo(55f).Within(0.001f));
                AssertCameraAimsAtFocus(camera, player.transform, 1.4f, 2.6f);

                follow.RotatePitch(-10000f);
                follow.Snap();
                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(follow.MinimumOrbitPitch));
                Assert.That(
                    follow.CurrentOrbitPitch,
                    Is.EqualTo(-20f).Within(0.001f));
                AssertCameraAimsAtFocus(camera, player.transform, 1.4f, 2.6f);
            }
            finally
            {
                if (mouse != null && mouse.added)
                {
                    InputSystem.RemoveDevice(mouse);
                }

                inputFixture?.TearDown();
            }
        }




        [UnityTest]
        public IEnumerator ExteriorCamera_ArrowKeysOrbitAndRespectModalLock()
        {
            InputTestFixture inputFixture = null;
            Keyboard keyboard = null;
            try
            {
                inputFixture = new InputTestFixture();
                inputFixture.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();

                Camera camera = CreateCamera(Vector3.zero);
                GameObject player = CreateObject(
                    "Arrow Orbit Camera Target");
                player.transform.position = new Vector3(0f, 100f, 0f);

                PlayerCameraFollow follow =
                    camera.gameObject.AddComponent<PlayerCameraFollow>();
                follow.Initialize(camera, player.transform, false);
                follow.SetCinematicMotionEnabled(false);
                follow.Snap();
                yield return null;

                float initialPitch = follow.TargetOrbitPitch;
                Vector3 initialPlanarForward = Vector3.ProjectOnPlane(
                    camera.transform.forward,
                    Vector3.up).normalized;

                follow.SetOrbitInputEnabled(false);
                inputFixture.Press(
                    keyboard.upArrowKey,
                    queueEventOnly: true);
                yield return null;
                yield return null;

                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(initialPitch).Within(0.001f),
                    "A modal orbit lock must suppress arrow-key look.");

                follow.SetOrbitInputEnabled(true);
                // The arrow axis is scaled by unscaled delta time, so
                // a fixed frame count means nothing in batch mode —
                // hold the key against a realtime deadline instead.
                float pitchDeadline = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < pitchDeadline &&
                       follow.TargetOrbitPitch > initialPitch - 0.2f)
                {
                    yield return null;
                }

                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.LessThan(initialPitch - 0.19f),
                    "The up arrow must raise the view like stick-up.");

                follow.Snap();
                Assert.That(
                    Vector3.Angle(
                        initialPlanarForward,
                        Vector3.ProjectOnPlane(
                            camera.transform.forward,
                            Vector3.up).normalized),
                    Is.LessThan(0.1f),
                    "A pure vertical arrow must not change orbit yaw.");

                inputFixture.Release(
                    keyboard.upArrowKey,
                    queueEventOnly: true);
                yield return null;

                float pitchAfterVertical = follow.TargetOrbitPitch;
                inputFixture.Press(
                    keyboard.rightArrowKey,
                    queueEventOnly: true);
                float yawDeadline = Time.realtimeSinceStartup + 1.5f;
                float yawTravel = 0f;
                while (Time.realtimeSinceStartup < yawDeadline &&
                       yawTravel < 0.5f)
                {
                    yield return null;
                    follow.Snap();
                    yawTravel = Vector3.Angle(
                        initialPlanarForward,
                        Vector3.ProjectOnPlane(
                            camera.transform.forward,
                            Vector3.up).normalized);
                }

                Assert.That(
                    yawTravel,
                    Is.GreaterThanOrEqualTo(0.5f),
                    "The right arrow must orbit the camera's yaw.");
                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(pitchAfterVertical).Within(0.001f),
                    "A pure horizontal arrow must not change pitch.");

                inputFixture.Release(
                    keyboard.rightArrowKey,
                    queueEventOnly: true);
                yield return null;
            }
            finally
            {
                if (keyboard != null && keyboard.added)
                {
                    InputSystem.RemoveDevice(keyboard);
                }

                inputFixture?.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator ExteriorCamera_OrbitYawHasHeavySmoothConvergence()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Heavy Orbit Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            follow.SetCinematicMotionEnabled(false);
            follow.Snap();

            Quaternion originalPlayerHeading = player.transform.rotation;
            Vector3 initialArm = Vector3.ProjectOnPlane(
                camera.transform.position - player.transform.position,
                Vector3.up).normalized;
            Vector3 targetArm =
                Quaternion.AngleAxis(90f, Vector3.up) * initialArm;
            FieldInfo smoothTimeField =
                typeof(PlayerCameraFollow).GetField(
                    "yawSmoothTime",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(smoothTimeField, Is.Not.Null);
            float configuredSmoothTime =
                (float)smoothTimeField.GetValue(follow);
            Assert.That(configuredSmoothTime, Is.EqualTo(0.2f));

            float sampleStartedAt = Time.unscaledTime;
            follow.RotateYaw(90f);
            yield return null;
            yield return null;

            float earlyProgress = Vector3.Angle(
                initialArm,
                Vector3.ProjectOnPlane(
                    camera.transform.position -
                    player.transform.position,
                    Vector3.up).normalized);
            Assert.That(
                earlyProgress,
                Is.GreaterThan(0f),
                "Orbit yaw should begin responding immediately.");
            float sampleElapsed = Time.unscaledTime - sampleStartedAt;
            if (sampleElapsed <= configuredSmoothTime)
            {
                Assert.That(
                    earlyProgress,
                    Is.LessThan(80f),
                    "A normal frame inside the smoothing window must " +
                    "retain visible rotational weight.");
            }

            float convergenceDeadline =
                Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < convergenceDeadline &&
                   Vector3.Angle(
                       targetArm,
                       Vector3.ProjectOnPlane(
                           camera.transform.position -
                           player.transform.position,
                           Vector3.up).normalized) >= 0.5f)
            {
                yield return null;
            }

            Assert.That(
                Vector3.Angle(
                    targetArm,
                    Vector3.ProjectOnPlane(
                        camera.transform.position -
                        player.transform.position,
                        Vector3.up).normalized),
                Is.LessThan(1f),
                "Heavy yaw smoothing must still settle on the requested " +
                "orbit angle.");
            Assert.That(
                Quaternion.Angle(
                    player.transform.rotation,
                    originalPlayerHeading),
                Is.LessThan(0.001f));
        }



        [UnityTest]
        public IEnumerator ExteriorCamera_ObstacleImmediatelyShortensCameraArm()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Obstructed Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            yield return null;


            Vector3 focusPoint = player.transform.position + Vector3.up * 1.4f;
            float unobstructedDistance = Vector3.Distance(
                focusPoint,
                camera.transform.position);
            Vector3 cameraDirection =
                (camera.transform.position - focusPoint).normalized;
            GameObject wall = CreateObject("Camera Obstacle");
            wall.transform.position = focusPoint + cameraDirection * 1.5f;
            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(4f, 4f, 0.25f);
            Physics.SyncTransforms();
            yield return null;

            float actualDistance = Vector3.Distance(
                focusPoint,
                camera.transform.position);
            Assert.That(
                unobstructedDistance,
                Is.EqualTo(2.6f).Within(0.01f));
            Assert.That(
                actualDistance,
                Is.LessThan(1.5f),
                "An obstacle must pull the camera inward before rendering.");
            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (focusPoint - camera.transform.position).normalized),
                Is.LessThan(0.1f),
                "Collision handling must keep the player centered.");

            wallCollider.enabled = false;
            yield return null;

            float firstRecoveryDistance = Vector3.Distance(
                focusPoint,
                camera.transform.position);
            Assert.That(
                firstRecoveryDistance,
                Is.GreaterThan(actualDistance)
                    .And.LessThan(unobstructedDistance - 0.05f),
                "Leaving an obstacle must recover the closer camera arm " +
                "smoothly instead of popping outward.");

            float recoveryDeadline =
                Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < recoveryDeadline &&
                   Vector3.Distance(
                       focusPoint,
                       camera.transform.position) <
                   unobstructedDistance - 0.01f)
            {
                yield return null;
            }

            Assert.That(
                Vector3.Distance(
                    focusPoint,
                    camera.transform.position),
                Is.EqualTo(unobstructedDistance).Within(0.01f));
        }



        [UnityTest]
        public IEnumerator CameraProfiles_KeepPlayerLowInExteriorAndInteriorFraming()
        {
            Camera exteriorCamera = CreateCamera(Vector3.zero);
            GameObject exteriorPlayer =
                CreateObject("Exterior Framing Target");
            exteriorPlayer.transform.position =
                new Vector3(0f, 100f, 0f);
            PlayerCameraFollow exteriorFollow =
                exteriorCamera.gameObject.AddComponent<PlayerCameraFollow>();

            exteriorFollow.Initialize(
                exteriorCamera,
                exteriorPlayer.transform,
                false);

            Vector3 exteriorFocus =
                exteriorPlayer.transform.position + Vector3.up * 1.4f;
            Assert.That(
                Vector3.Distance(
                    exteriorFocus,
                    exteriorCamera.transform.position),
                Is.EqualTo(2.6f).Within(0.001f));
            Assert.That(
                exteriorCamera.fieldOfView,
                Is.EqualTo(53f).Within(0.01f));
            Assert.That(
                exteriorCamera.WorldToViewportPoint(
                    exteriorPlayer.transform.position + Vector3.up).y,
                Is.InRange(0.3f, 0.42f),
                "The exterior profile must compose the player's center " +
                "below the middle of the frame.");

            Camera interiorCamera = CreateCamera(Vector3.zero);
            GameObject interiorPlayer =
                CreateObject("Interior Framing Target");
            interiorPlayer.transform.position =

                new Vector3(20f, 100f, 0f);
            PlayerCameraFollow interiorFollow =
                interiorCamera.gameObject.AddComponent<PlayerCameraFollow>();

            interiorFollow.Initialize(
                interiorCamera,
                interiorPlayer.transform,
                true);

            Vector3 interiorFocus =
                interiorPlayer.transform.position + Vector3.up * 1.3f;
            Assert.That(
                Vector3.Distance(
                    interiorFocus,
                    interiorCamera.transform.position),
                Is.EqualTo(2.2f).Within(0.001f));
            Assert.That(
                interiorCamera.fieldOfView,
                Is.EqualTo(57f).Within(0.01f));
            Assert.That(
                interiorCamera.WorldToViewportPoint(
                    interiorPlayer.transform.position + Vector3.up).y,
                Is.InRange(0.3f, 0.42f),
                "The interior profile must compose the player's center " +
                "below the middle of the frame.");
            yield return null;
        }



        [UnityTest]
        public IEnumerator CameraFollow_DampsFocusCapsLagAndSnapsTeleport()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Smoothed Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetCinematicMotionEnabled(false);
            follow.Snap();

            Vector3 initialFocus = follow.CurrentFocusPoint;
            player.transform.position += Vector3.right;
            yield return null;

            Vector3 targetFocus =
                player.transform.position + Vector3.up * 1.4f;
            float firstFrameProgress =
                follow.CurrentFocusPoint.x - initialFocus.x;
            Assert.That(
                firstFrameProgress,
                Is.GreaterThan(0f).And.LessThan(0.65f),
                "Normal target motion must be damped.");
            Assert.That(
                Vector3.Distance(
                    follow.CurrentFocusPoint,
                    targetFocus),
                Is.LessThanOrEqualTo(0.451f),
                "Focus damping must never leave the player too far behind.");

            float previousX = follow.CurrentFocusPoint.x;
            float convergenceDeadline =
                Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < convergenceDeadline &&
                   Vector3.Distance(
                       follow.CurrentFocusPoint,
                       targetFocus) >= 0.01f)
            {
                yield return null;
                Assert.That(
                    follow.CurrentFocusPoint.x,
                    Is.GreaterThanOrEqualTo(previousX - 0.0001f),
                    "The damped focus must converge without overshooting.");
                previousX = follow.CurrentFocusPoint.x;
            }

            Assert.That(
                Vector3.Distance(
                    follow.CurrentFocusPoint,
                    targetFocus),
                Is.LessThan(0.01f));

            player.transform.position += Vector3.right * 2f;
            yield return null;

            Vector3 teleportedFocus =
                player.transform.position + Vector3.up * 1.4f;
            Assert.That(
                Vector3.Distance(
                    follow.CurrentFocusPoint,
                    teleportedFocus),
                Is.LessThan(0.0001f),
                "Large target jumps must snap instead of dragging the camera.");
            Assert.That(
                Vector3.Distance(
                    teleportedFocus,
                    camera.transform.position),
                Is.EqualTo(2.6f).Within(0.001f),
                "Teleport snap must apply the exact collision-safe pose.");

        }



        [UnityTest]
        public IEnumerator CinematicMotion_IsSubtleSpeedDrivenAndYawStable()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Cinematic Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);

            Quaternion baseRotation = Quaternion.Euler(14f, 0f, 0f);
            float maximumIdleExcursion = 0f;
            float idleDeadline = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < idleDeadline)
            {
                yield return null;
                maximumIdleExcursion = Mathf.Max(
                    maximumIdleExcursion,
                    CameraExcursionFromBase(
                        camera,
                        follow,
                        baseRotation,
                        2.6f));
                AssertCinematicCameraKeepsStableYawAndFov(camera, 53f);
            }

            float maximumWalkExcursion = 0f;
            float walkDeadline = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < walkDeadline)
            {
                float movementDeltaTime =
                    Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                player.transform.position +=
                    Vector3.right * (4.8f * movementDeltaTime);
                yield return null;
                maximumWalkExcursion = Mathf.Max(
                    maximumWalkExcursion,
                    CameraExcursionFromBase(
                        camera,
                        follow,
                        baseRotation,
                        2.6f));
                AssertCinematicCameraKeepsStableYawAndFov(camera, 53f);
            }

            Assert.That(
                maximumIdleExcursion,
                Is.GreaterThan(0.0001f).And.LessThan(0.02f),
                "Idle camera drift must be present but remain very subtle.");
            Assert.That(
                maximumWalkExcursion,
                Is.GreaterThan(0.008f).And.LessThan(0.04f),
                "Walking must add a bounded motion-driven bob.");

            follow.SetCinematicMotionEnabled(false);
            float fadeDeadline = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < fadeDeadline)
            {
                yield return null;
            }

            Assert.That(
                CameraExcursionFromBase(
                    camera,
                    follow,
                    baseRotation,
                    2.6f),
                Is.LessThan(0.002f),
                "Modal camera suppression must fade the sway to rest.");
        }



        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = cleanupObjects.Count - 1; i >= 0; i--)
            {
                if (cleanupObjects[i] != null)
                {
                    Object.Destroy(cleanupObjects[i]);
                }
            }

            cleanupObjects.Clear();
            yield return null;
            yield return null;
        }

        private static float CameraExcursionFromBase(
            Camera camera,
            PlayerCameraFollow follow,
            Quaternion baseRotation,
            float distance)
        {
            Vector3 basePosition =

                follow.CurrentFocusPoint -
                baseRotation * Vector3.forward * distance;
            return Vector3.Distance(
                camera.transform.position,
                basePosition);
        }

        private static void AssertCinematicCameraKeepsStableYawAndFov(
            Camera camera,
            float expectedFieldOfView)
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;
            Assert.That(
                Vector3.Angle(planarForward, Vector3.forward),
                Is.LessThan(0.05f),
                "Cinematic motion must not introduce yaw sway.");
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(expectedFieldOfView).Within(0.001f),
                "Cinematic motion must not pulse the field of view.");
        }



        private static void AssertCameraAimsAtFocus(
            Camera camera,
            Transform target,
            float focusHeight,
            float expectedDistance)
        {
            Vector3 focus = target.position + Vector3.up * focusHeight;
            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (focus - camera.transform.position).normalized),
                Is.LessThan(0.1f));
            Assert.That(
                Vector3.Distance(focus, camera.transform.position),
                Is.EqualTo(expectedDistance).Within(0.001f));
        }



        private Camera CreateCamera(Vector3 position)
        {
            GameObject cameraObject = CreateObject("Presentation Test Camera");
            cameraObject.transform.position = position;
            return cameraObject.AddComponent<Camera>();
        }


        private GameObject CreateObject(string objectName)
        {
            GameObject gameObject = new GameObject(objectName);
            cleanupObjects.Add(gameObject);
            return gameObject;
        }
    }
}
