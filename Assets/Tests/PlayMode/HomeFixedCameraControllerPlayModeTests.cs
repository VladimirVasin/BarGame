using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeFixedCameraControllerPlayModeTests
    {
        private readonly List<GameObject> cleanupObjects =
            new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = cleanupObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                if (cleanupObjects[index] != null)
                {
                    UnityEngine.Object.Destroy(
                        cleanupObjects[index]);
                }
            }

            cleanupObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            FixedPose_StaysAnchoredIgnoresOrbitAndClearRestoresChase()
        {
            PlayerCameraFollow follow = CreateFollow(
                false,
                out Camera camera,
                out Transform target);
            follow.SetCinematicMotionEnabled(false);
            follow.RotateYaw(35f);
            follow.Snap();
            Vector3 chaseArmBeforeFixed =
                GetPlanarCameraArm(camera, target);

            Vector3 fixedPosition =
                new Vector3(-4.2f, 103.1f, -3.4f);
            Quaternion fixedRotation =
                Quaternion.Euler(19f, 41f, 0f);
            const float fixedFieldOfView = 48f;
            follow.SetFixedPose(
                fixedPosition,
                fixedRotation,
                fixedFieldOfView);

            Assert.That(follow.FixedPoseActive, Is.True);
            AssertVector(
                follow.FixedBasePosition,
                fixedPosition);
            AssertRotation(
                follow.FixedBaseRotation,
                fixedRotation);
            AssertVector(
                follow.FixedBasePose.position,
                fixedPosition);
            AssertRotation(
                follow.FixedBasePose.rotation,
                fixedRotation);
            Assert.That(
                follow.FixedBaseFieldOfView,
                Is.EqualTo(fixedFieldOfView));
            AssertFixedPose(
                camera,
                fixedPosition,
                fixedRotation,
                fixedFieldOfView);

            target.position +=
                new Vector3(3.5f, 0f, 2.25f);
            follow.RotateYaw(140f);
            yield return null;
            yield return null;

            AssertFixedPose(
                camera,
                fixedPosition,
                fixedRotation,
                fixedFieldOfView);

            follow.ClearFixedPose();

            Assert.That(follow.FixedPoseActive, Is.False);
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(53f).Within(0.01f));
            Assert.That(
                Vector3.Distance(
                    target.position + Vector3.up * 1.4f,
                    camera.transform.position),
                Is.EqualTo(2.6f).Within(0.001f));
            Assert.That(
                Vector3.Angle(
                    GetPlanarCameraArm(camera, target),
                    chaseArmBeforeFixed),
                Is.LessThan(0.1f),
                "Orbit requests made in fixed mode must not alter the restored chase yaw.");
        }

        [UnityTest]
        public IEnumerator
            FixedPose_IntoxicationOnlyRotatesAndCinematicDisableSettlesToBase()
        {
            PlayerCameraFollow follow = CreateFollow(
                true,
                out Camera camera,
                out Transform target);
            Vector3 fixedPosition =
                new Vector3(3.8f, 102.9f, -3.2f);
            Quaternion fixedRotation =
                Quaternion.Euler(21f, -38f, 0f);
            const float fixedFieldOfView = 46f;
            follow.SetFixedPose(
                fixedPosition,
                fixedRotation,
                fixedFieldOfView);
            follow.SetIntoxication(1f);
            follow.SetBalanceReaction(0.8f, -1f, 1f);

            float maximumExcursion = 0f;
            float reactionDeadline =
                Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup <
                   reactionDeadline)
            {
                target.position += Vector3.right * 0.02f;
                yield return null;
                AssertVector(
                    camera.transform.position,
                    fixedPosition);
                Assert.That(
                    camera.fieldOfView,
                    Is.EqualTo(fixedFieldOfView)
                        .Within(0.001f));
                maximumExcursion = Mathf.Max(
                    maximumExcursion,
                    Quaternion.Angle(
                        camera.transform.rotation,
                        fixedRotation));
            }

            Assert.That(
                maximumExcursion,
                Is.GreaterThan(0.05f)
                    .And.LessThan(2f),
                "Fixed-camera intoxication and balance reactions must remain visible but bounded.");

            follow.SetCinematicMotionEnabled(false);
            float settleDeadline =
                Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup <
                       settleDeadline &&
                   Quaternion.Angle(
                       camera.transform.rotation,
                       fixedRotation) >= 0.01f)
            {
                yield return null;
                AssertVector(
                    camera.transform.position,
                    fixedPosition);
            }

            AssertRotation(
                camera.transform.rotation,
                fixedRotation,
                0.02f);
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(fixedFieldOfView)
                    .Within(0.001f));
        }

        [Test]
        public void
            Selector_CurrentShotWinsHoldMarginAndSupportsDirectTeleport()
        {
            IReadOnlyList<HomeCameraShot> shots =
                CreateShots();
            var selector =
                new HomeCameraShotSelector(shots);

            Assert.That(
                selector.Select(
                    new Vector3(0f, 0f, 0f)).Kind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(
                selector.Select(
                    new Vector3(0f, 0f, 3.30f)).Kind,
                Is.EqualTo(HomeCameraShotKind.MainRoom),
                "The active main shot must win inside its expanded hold margin.");
            Assert.That(
                selector.Select(
                    new Vector3(0f, 0f, 3.70f)).Kind,
                Is.EqualTo(HomeCameraShotKind.Bathroom));
            Assert.That(
                selector.Select(
                    new Vector3(0f, 0f, 3.00f)).Kind,
                Is.EqualTo(HomeCameraShotKind.Bathroom),
                "The bathroom shot must retain ownership while returning through its hold margin.");
            Assert.That(
                selector.Select(
                    new Vector3(0f, 0f, 2.70f)).Kind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));

            var teleportedSelector =
                new HomeCameraShotSelector(shots);
            Assert.That(
                teleportedSelector.Select(
                    new Vector3(0f, 0f, 4.40f)).Kind,
                Is.EqualTo(HomeCameraShotKind.Bathroom),
                "A direct teleport must choose the activation area containing the destination.");
            Assert.That(
                teleportedSelector.Select(
                    new Vector3(3f, 0f, 4.40f)).Kind,
                Is.EqualTo(HomeCameraShotKind.MainRoom),
                "Floor outside the bathroom shot must fall back to the " +
                "main-room camera even when the previous shot was the bathroom.");
            Assert.That(
                teleportedSelector.Select(
                    new Vector3(6.20f, 0f, -1.40f)).Kind,
                Is.EqualTo(HomeCameraShotKind.Balcony),
                "A direct teleport onto the balcony must choose its exterior shot.");
            Assert.That(
                teleportedSelector.Select(
                    new Vector3(5.05f, 0f, -1.40f)).Kind,
                Is.EqualTo(HomeCameraShotKind.Balcony),
                "The balcony shot must retain ownership through the doorway margin.");
            Assert.That(
                teleportedSelector.Select(
                    new Vector3(4.60f, 0f, -1.40f)).Kind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
        }

        [Test]
        public void Selector_RejectsInvalidShotCollections()
        {
            Assert.That(
                () =>
                {
                    _ = new HomeCameraShotSelector(null);
                },
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () =>
                {
                    _ = new HomeCameraShotSelector(
                        Array.Empty<HomeCameraShot>());
                },
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () =>
                {
                    _ = new HomeCameraShot(
                        HomeCameraShotKind.MainRoom,
                        new Rect(-1f, -1f, 2f, 2f),
                        new Rect(-0.5f, -0.5f, 1f, 1f),
                        Vector3.zero,
                        Quaternion.identity,
                        50f);
                },
                Throws.TypeOf<ArgumentException>(),
                "Activation bounds cannot extend beyond hold bounds.");
            Assert.That(
                () =>
                {
                    _ = new HomeCameraShot(
                        HomeCameraShotKind.MainRoom,
                        new Rect(-1f, -1f, 2f, 2f),
                        new Rect(-2f, -2f, 4f, 4f),
                        Vector3.zero,
                        Quaternion.identity,
                        10f);
                },
                Throws.TypeOf<ArgumentOutOfRangeException>());

            HomeCameraShot main = CreateShots()[0];
            var duplicates = new[]
            {
                main,
                new HomeCameraShot(
                    HomeCameraShotKind.MainRoom,
                    new Rect(-1f, -1f, 2f, 2f),
                    new Rect(-1.5f, -1.5f, 3f, 3f),
                    new Vector3(2f, 3f, -2f),
                    Quaternion.Euler(20f, -20f, 0f),
                    52f)
            };
            Assert.That(
                () =>
                {
                    _ = new HomeCameraShotSelector(
                        duplicates);
                },
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () =>
                {
                    _ = new HomeCameraShotSelector(
                        new[]
                        {
                            default(HomeCameraShot)
                        });
                },
                Throws.InstanceOf<ArgumentException>());
        }

        [UnityTest]
        public IEnumerator
            Controller_StartsMainAndHardCutsAcrossBidirectionalHoldMargins()
        {
            PlayerCameraFollow follow = CreateFollow(
                true,
                out Camera camera,
                out Transform target);
            IReadOnlyList<HomeCameraShot> shots =
                CreateShots();
            GameObject controllerObject =
                CreateObject("Home Fixed Camera Controller");
            HomeFixedCameraController controller =
                controllerObject.AddComponent<
                    HomeFixedCameraController>();

            controller.Initialize(
                follow,
                target,
                shots);

            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            AssertShotApplied(
                camera,
                follow,
                shots[0]);

            target.position =
                new Vector3(0f, target.position.y, 3.30f);
            yield return null;
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));

            target.position =
                new Vector3(0f, target.position.y, 3.70f);
            yield return null;
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Bathroom));
            AssertShotApplied(
                camera,
                follow,
                shots[1]);

            target.position =
                new Vector3(0f, target.position.y, 3.00f);
            yield return null;
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Bathroom));

            target.position =
                new Vector3(0f, target.position.y, 2.70f);
            yield return null;
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            AssertShotApplied(
                camera,
                follow,
                shots[0]);

            target.position =
                new Vector3(0f, target.position.y, 4.40f);
            yield return null;
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Bathroom));
            AssertShotApplied(
                camera,
                follow,
                shots[1]);

            controller.enabled = false;
            Assert.That(follow.FixedPoseActive, Is.False);
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(57f).Within(0.01f));

            controller.enabled = true;
            Assert.That(follow.FixedPoseActive, Is.True);
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Bathroom));
            AssertShotApplied(
                camera,
                follow,
                shots[1]);

            target.position =
                new Vector3(6.20f, target.position.y, -1.40f);
            yield return null;
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Balcony));
            AssertShotApplied(
                camera,
                follow,
                shots[2]);

            target.position =
                new Vector3(5.05f, target.position.y, -1.40f);
            yield return null;
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Balcony));

            target.position =
                new Vector3(4.60f, target.position.y, -1.40f);
            yield return null;
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            AssertShotApplied(
                camera,
                follow,
                shots[0]);
        }

        /// <summary>
        /// The apartment is wider than one aim from the corner the main
        /// room shot stands in: walking to the west wall used to carry the
        /// hero off the left of the picture. The shot now follows him, but
        /// only once he reaches the edge of the frame and only by the
        /// least that brings him back, and the camera itself never leaves
        /// the spot it was hung on.
        /// </summary>
        [UnityTest]
        public IEnumerator
            Focus_HoldsTheAuthoredFrameThenPansTheLeastThatFramesHim()
        {
            PlayerCameraFollow follow = CreateFollow(
                true,
                out Camera camera,
                out Transform target);
            follow.SetCinematicMotionEnabled(false);
            camera.aspect = 16f / 9f;
            IReadOnlyList<HomeCameraShot> shots =
                CreateApartmentShots();
            GameObject controllerObject =
                CreateObject("Home Focus Camera Controller");
            HomeFixedCameraController controller =
                controllerObject.AddComponent<
                    HomeFixedCameraController>();

            target.position = new Vector3(0f, 100f, -1.2f);
            controller.Initialize(
                follow,
                target,
                shots);
            yield return null;

            Assert.That(controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(follow.FixedFocusActive, Is.True);
            Assert.That(
                follow.FixedFocusOffset.magnitude,
                Is.LessThan(0.001f),
                "A hero in the middle of the room must not move the shot.");
            AssertRotation(
                camera.transform.rotation,
                shots[0].Rotation);
            Assert.That(IsFramed(camera, target), Is.True);

            target.position = new Vector3(-4.65f, 100f, 0.70f);
            Assert.That(
                IsFramed(camera, target),
                Is.False,
                "The west wall must be outside the authored frame, or " +
                "this test proves nothing.");

            follow.Snap();
            Vector2 offset = follow.FixedFocusOffset;
            Assert.That(
                offset.x,
                Is.LessThan(-1f),
                "The shot must turn west after him.");
            Assert.That(
                Mathf.Abs(offset.x),
                Is.LessThanOrEqualTo(
                    MainRoomFocus.MaximumYawDegrees + 0.001f));
            Assert.That(
                Mathf.Abs(offset.y),
                Is.LessThanOrEqualTo(
                    MainRoomFocus.MaximumPitchDegrees + 0.001f));
            Assert.That(IsFramed(camera, target), Is.True);
            AssertVector(
                camera.transform.position,
                shots[0].Position);
            Assert.That(
                camera.transform.rotation.eulerAngles.z,
                Is.EqualTo(0f).Within(0.01f)
                    .Or.EqualTo(360f).Within(0.01f),
                "A pan must not tip the horizon.");

            float panned = follow.FixedFocusOffset.magnitude;
            target.position = new Vector3(0f, 100f, -1.2f);
            yield return null;
            yield return null;

            Assert.That(
                follow.FixedFocusOffset.magnitude,
                Is.LessThan(panned),
                "The pan must ease back once he is framed again.");
            follow.Snap();
            Assert.That(
                follow.FixedFocusOffset.magnitude,
                Is.LessThan(0.001f),
                "The shot must settle back onto its authored frame.");
            AssertRotation(
                camera.transform.rotation,
                shots[0].Rotation);

            target.position = new Vector3(-4.65f, 100f, 0.70f);
            follow.Snap();
            Assert.That(follow.FixedFocusOffset.magnitude,
                Is.GreaterThan(1f));
            follow.SetFixedPose(
                shots[0].Position,
                shots[0].Rotation,
                shots[0].FieldOfView);

            Assert.That(
                follow.FixedFocusActive,
                Is.False,
                "A new owner of the camera must get the pose it asked " +
                "for, not the pan the room was holding.");
            AssertRotation(
                camera.transform.rotation,
                shots[0].Rotation);
        }

        private static readonly FixedCameraFocus MainRoomFocus =
            FixedCameraFocus.Bounded(18f, 9f);

        /// <summary>The home shots as the apartment authors them, lifted
        /// a hundred metres so the test rig cannot meet the world.</summary>
        private static IReadOnlyList<HomeCameraShot>
            CreateApartmentShots()
        {
            return new[]
            {
                new HomeCameraShot(
                    HomeCameraShotKind.MainRoom,
                    new Rect(-4.65f, -3.65f, 9.30f, 4.40f),
                    new Rect(-4.65f, -3.65f, 9.30f, 4.48f),
                    new Vector3(-4.48f, 103.00f, -3.25f),
                    new Vector3(28f, 55f, 0f),
                    64f)
                    .WithFocus(MainRoomFocus),
                new HomeCameraShot(
                    HomeCameraShotKind.Bathroom,
                    new Rect(1.69f, 0.89f, 2.82f, 2.64f),
                    new Rect(1.61f, 0.73f, 2.98f, 2.86f),
                    new Vector3(1.82f, 102.20f, 0.86f),
                    new Vector3(30f, 38f, 0f),
                    92f)
            };
        }

        /// <summary>Whether the hero stands whole inside the picture,
        /// from the ground under his boots to the crown of his head.</summary>
        private static bool IsFramed(
            Camera camera,
            Transform target)
        {
            return IsFramed(
                       camera,
                       target.position +
                       Vector3.up * FixedCameraFocus.BodyLowerHeight) &&
                   IsFramed(
                       camera,
                       target.position +
                       Vector3.up * FixedCameraFocus.BodyUpperHeight);
        }

        private static bool IsFramed(
            Camera camera,
            Vector3 worldPoint)
        {
            Vector3 viewport =
                camera.WorldToViewportPoint(worldPoint);
            return viewport.z > 0f &&
                   viewport.x >= 0f &&
                   viewport.x <= 1f &&
                   viewport.y >= 0f &&
                   viewport.y <= 1f;
        }

        private PlayerCameraFollow CreateFollow(
            bool interior,
            out Camera camera,
            out Transform target)
        {
            GameObject targetObject =
                CreateObject("Home Camera Target");
            targetObject.transform.position =
                new Vector3(0f, 100f, 0f);
            target = targetObject.transform;

            GameObject cameraObject =
                CreateObject("Home Fixed Test Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            PlayerCameraFollow follow =
                cameraObject.AddComponent<
                    PlayerCameraFollow>();
            follow.Initialize(
                camera,
                target,
                interior);
            return follow;
        }

        private static IReadOnlyList<HomeCameraShot>
            CreateShots()
        {
            return new[]
            {
                new HomeCameraShot(
                    HomeCameraShotKind.MainRoom,
                    new Rect(-5f, -4f, 10f, 7f),
                    new Rect(-5f, -4f, 10f, 7.5f),
                    new Vector3(-4.2f, 103.1f, -3.4f),
                    Quaternion.Euler(19f, 41f, 0f),
                    48f),
                new HomeCameraShot(
                    HomeCameraShotKind.Bathroom,
                    new Rect(-2f, 3.2f, 4f, 2f),
                    new Rect(-2.2f, 2.8f, 4.4f, 2.6f),
                    new Vector3(1.7f, 102.8f, 4.8f),
                    Quaternion.Euler(18f, -142f, 0f),
                    50f),
                new HomeCameraShot(
                    HomeCameraShotKind.Balcony,
                    new Rect(5.2f, -3.2f, 2.3f, 3.4f),
                    new Rect(4.9f, -3.5f, 2.9f, 4f),
                    new Vector3(3.6f, 103.1f, -3.55f),
                    Quaternion.Euler(24f, 55f, 0f),
                    70f)
            };
        }

        private static void AssertShotApplied(
            Camera camera,
            PlayerCameraFollow follow,
            HomeCameraShot shot)
        {
            Assert.That(follow.FixedPoseActive, Is.True);
            Assert.That(
                follow.FixedBaseFieldOfView,
                Is.EqualTo(shot.FieldOfView));
            AssertFixedPose(
                camera,
                shot.Position,
                shot.Rotation,
                shot.FieldOfView);
        }

        private static void AssertFixedPose(
            Camera camera,
            Vector3 expectedPosition,
            Quaternion expectedRotation,
            float expectedFieldOfView)
        {
            AssertVector(
                camera.transform.position,
                expectedPosition);
            AssertRotation(
                camera.transform.rotation,
                expectedRotation);
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(expectedFieldOfView)
                    .Within(0.001f));
        }

        private static Vector3 GetPlanarCameraArm(
            Camera camera,
            Transform target)
        {
            return Vector3.ProjectOnPlane(
                    camera.transform.position -
                    target.position,
                    Vector3.up)
                .normalized;
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThan(0.001f));
        }

        private static void AssertRotation(
            Quaternion actual,
            Quaternion expected,
            float tolerance = 0.01f)
        {
            Assert.That(
                Quaternion.Angle(actual, expected),
                Is.LessThan(tolerance));
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            cleanupObjects.Add(result);
            return result;
        }
    }
}
