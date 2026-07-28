using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests
{
    public sealed class PlayerPresentationPlayModeTests
    {
        private readonly List<GameObject> cleanupObjects = new List<GameObject>();

        private static readonly PlayerPuppetPart[] JointParts =
        {
            PlayerPuppetPart.LeftUpperArm,
            PlayerPuppetPart.LeftLowerArm,
            PlayerPuppetPart.RightUpperArm,
            PlayerPuppetPart.RightLowerArm,
            PlayerPuppetPart.LeftUpperLeg,
            PlayerPuppetPart.LeftLowerLeg,
            PlayerPuppetPart.RightUpperLeg,
            PlayerPuppetPart.RightLowerLeg
        };

        [UnityTest]
        public IEnumerator InitializedRig_CreatesNinePartVisualOnlyPuppet()
        {
            Camera camera = CreateCamera(new Vector3(0f, 5f, 8f));
            GameObject rigObject = CreateObject("Presentation Test Rig");
            PlayerSpriteRig rig = rigObject.AddComponent<PlayerSpriteRig>();

            rig.Initialize(camera);
            yield return null;

            SpriteRenderer[] renderers =
                rigObject.GetComponentsInChildren<SpriteRenderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(9));
            Assert.That(rig.Renderers, Has.Count.EqualTo(9));
            Assert.That(
                rigObject.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "Generated visual descendants must not contain 3D colliders.");
            Assert.That(
                rigObject.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty,
                "Generated visual descendants must not contain 3D rigidbodies.");
            Assert.That(
                rigObject.GetComponentsInChildren<Collider2D>(true),
                Is.Empty,
                "Generated visual descendants must not contain 2D colliders.");
            Assert.That(
                rigObject.GetComponentsInChildren<Rigidbody2D>(true),
                Is.Empty,
                "Generated visual descendants must not contain 2D rigidbodies.");

            PlayerPuppetPart[] expectedParts =
            {
                PlayerPuppetPart.Body,
                PlayerPuppetPart.LeftUpperArm,
                PlayerPuppetPart.LeftLowerArm,
                PlayerPuppetPart.RightUpperArm,
                PlayerPuppetPart.RightLowerArm,
                PlayerPuppetPart.LeftUpperLeg,
                PlayerPuppetPart.LeftLowerLeg,
                PlayerPuppetPart.RightUpperLeg,
                PlayerPuppetPart.RightLowerLeg
            };
            PlayerViewDirection[] expectedDirections =
            {
                PlayerViewDirection.Front,
                PlayerViewDirection.FrontRight,
                PlayerViewDirection.Right,
                PlayerViewDirection.BackRight,
                PlayerViewDirection.Back,
                PlayerViewDirection.BackLeft,
                PlayerViewDirection.Left,
                PlayerViewDirection.FrontLeft
            };
            Assert.That(
                System.Enum.GetValues(typeof(PlayerPuppetPart)),
                Is.EqualTo(expectedParts));
            Assert.That(
                System.Enum.GetValues(typeof(PlayerViewDirection)),
                Is.EqualTo(expectedDirections));
            Assert.That(rig.DirectionSprites, Has.Count.EqualTo(8));

            Assert.That(rig.VisualRoot, Is.Not.Null);
            Assert.That(rigObject.transform.childCount, Is.EqualTo(1));
            Assert.That(rig.VisualRoot.parent, Is.SameAs(rigObject.transform));
            Assert.That(
                rig.VisualRoot.name,
                Is.EqualTo("GeneratedDirectionalPuppet"));
            Assert.That(
                rig.VisualRoot.GetComponent<BillboardSprite>(),
                Is.Not.Null);
            Assert.That(rig.VisualRoot.childCount, Is.EqualTo(1));
            Assert.That(rig.PoseRoot, Is.Not.Null);
            Assert.That(rig.PoseRoot.parent, Is.SameAs(rig.VisualRoot));
            Assert.That(rig.PoseRoot.name, Is.EqualTo("PoseRoot"));
            Assert.That(rig.PoseRoot.childCount, Is.EqualTo(5));
            Transform body =
                rig.GetPartTransform(PlayerPuppetPart.Body);
            Assert.That(
                body.parent,
                Is.SameAs(rig.PoseRoot));
            Assert.That(body.name, Is.EqualTo(PlayerPuppetPart.Body.ToString()));
            Assert.That(body.childCount, Is.EqualTo(0));
            AssertJointPair(
                rig,
                PlayerPuppetPart.LeftUpperArm,
                PlayerPuppetPart.LeftLowerArm);
            AssertJointPair(
                rig,
                PlayerPuppetPart.RightUpperArm,
                PlayerPuppetPart.RightLowerArm);
            AssertJointPair(
                rig,
                PlayerPuppetPart.LeftUpperLeg,
                PlayerPuppetPart.LeftLowerLeg);
            AssertJointPair(
                rig,
                PlayerPuppetPart.RightUpperLeg,
                PlayerPuppetPart.RightLowerLeg);
            Assert.That(rig.PoseRoot.GetChild(0), Is.SameAs(body));
            Assert.That(
                rig.PoseRoot.GetChild(1),
                Is.SameAs(rig.GetPartTransform(
                    PlayerPuppetPart.LeftUpperArm)));
            Assert.That(
                rig.PoseRoot.GetChild(2),
                Is.SameAs(rig.GetPartTransform(
                    PlayerPuppetPart.RightUpperArm)));
            Assert.That(
                rig.PoseRoot.GetChild(3),
                Is.SameAs(rig.GetPartTransform(
                    PlayerPuppetPart.LeftUpperLeg)));
            Assert.That(
                rig.PoseRoot.GetChild(4),
                Is.SameAs(rig.GetPartTransform(
                    PlayerPuppetPart.RightUpperLeg)));

            var uniqueSprites = new HashSet<Sprite>();
            for (int partIndex = 0;
                 partIndex < expectedParts.Length;
                 partIndex++)
            {
                PlayerPuppetPart part = expectedParts[partIndex];
                SpriteRenderer partRenderer = rig.GetPartRenderer(part);
                Transform partTransform = rig.GetPartTransform(part);

                Assert.That(partRenderer, Is.Not.Null);
                Assert.That(
                    rig.Renderers[partIndex],
                    Is.SameAs(partRenderer));
                Assert.That(partRenderer.transform, Is.SameAs(partTransform));
                Assert.That(
                    partTransform.GetComponent<SpriteRenderer>(),
                    Is.SameAs(partRenderer));

                for (int directionIndex = 0;
                     directionIndex < expectedDirections.Length;
                     directionIndex++)
                {
                    PlayerViewDirection direction =
                        expectedDirections[directionIndex];
                    Sprite sprite = rig.GetPartSprite(part, direction);

                    Assert.That(sprite, Is.Not.Null);
                    Assert.That(
                        uniqueSprites.Add(sprite),
                        Is.True,
                        $"{part} {direction} must have its own sprite.");
                    Assert.That(
                        sprite.rect.x,
                        Is.EqualTo(directionIndex * 64f).Within(0.001f));
                    Assert.That(
                        sprite.rect.y,
                        Is.EqualTo(partIndex * 96f).Within(0.001f));
                    Assert.That(
                        sprite.rect.width,
                        Is.EqualTo(64f).Within(0.001f));
                    Assert.That(
                        sprite.rect.height,
                        Is.EqualTo(96f).Within(0.001f));
                    Assert.That(
                        sprite.pixelsPerUnit,
                        Is.EqualTo(48f).Within(0.001f));
                }

                Assert.That(
                    partRenderer.sprite,
                    Is.SameAs(rig.GetPartSprite(
                        part,
                        PlayerViewDirection.Front)));
            }

            Assert.That(uniqueSprites, Has.Count.EqualTo(72));
            for (int directionIndex = 0;
                 directionIndex < expectedDirections.Length;
                 directionIndex++)
            {
                Assert.That(
                    rig.DirectionSprites[directionIndex],
                    Is.SameAs(rig.GetPartSprite(
                        PlayerPuppetPart.Body,
                        expectedDirections[directionIndex])));
                Assert.That(
                    rig.GetDirectionSprite(expectedDirections[directionIndex]),
                    Is.SameAs(rig.DirectionSprites[directionIndex]));
            }

            Assert.That(rig.BodyRenderer, Is.SameAs(rig.Renderer));
            Assert.That(rig.Renderer.sprite, Is.SameAs(
                rig.GetDirectionSprite(PlayerViewDirection.Front)));

            Vector3 bodyFrameOrigin = GetFrameOriginInPoseSpace(
                rig,
                PlayerPuppetPart.Body);
            for (int partIndex = 0;
                 partIndex < expectedParts.Length;
                 partIndex++)
            {
                PlayerPuppetPart part = expectedParts[partIndex];
                Assert.That(
                    Vector3.Distance(
                        GetFrameOriginInPoseSpace(rig, part),
                        bodyFrameOrigin),
                    Is.LessThan(0.001f),
                    $"{part} must align to the body frame at rest.");
            }

            AssertNoMirroring(rig);
        }

        [UnityTest]
        public IEnumerator Billboard_FacesCameraAndKeepsWorldUp_AfterFrame()
        {
            Camera camera = CreateCamera(new Vector3(6f, 5f, -8f));
            GameObject billboardObject = CreateObject("Billboard Test");
            billboardObject.transform.position = new Vector3(-2f, 0.5f, 1f);
            BillboardSprite billboard = billboardObject.AddComponent<BillboardSprite>();

            billboard.Initialize(camera);
            yield return null;

            Vector3 expectedForward = Vector3.ProjectOnPlane(
                camera.transform.position - billboardObject.transform.position,
                Vector3.up).normalized;

            Assert.That(
                Vector3.Angle(billboardObject.transform.forward, expectedForward),
                Is.LessThan(0.1f));
            Assert.That(
                Vector3.Angle(billboardObject.transform.up, Vector3.up),
                Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator CameraOrbit_SwitchesAllNinePartsAcrossEverySector()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject actor = CreateObject("Directional Actor");
            actor.transform.position = new Vector3(3f, 10f, -2f);
            actor.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            Quaternion actorHeading = actor.transform.rotation;
            PlaceCameraAtRelativeYaw(camera, actor.transform, 0f);

            GameObject rigObject = CreateObject("Directional Presentation Rig");
            rigObject.transform.SetParent(actor.transform, false);
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera, actor.transform);
            yield return null;

            var observedSprites = new HashSet<Sprite>();
            for (int index = 0; index < 8; index++)
            {
                PlaceCameraAtRelativeYaw(
                    camera,
                    actor.transform,
                    index * 45f);
                yield return null;

                PlayerViewDirection expectedDirection =
                    (PlayerViewDirection)index;
                Assert.That(
                    rig.CurrentDirection,
                    Is.EqualTo(expectedDirection),
                    $"Relative camera yaw {index * 45f} degrees.");

                for (int partIndex = 0;
                     partIndex < PlayerSpriteRig.PartCount;
                     partIndex++)
                {
                    PlayerPuppetPart part =
                        (PlayerPuppetPart)partIndex;
                    SpriteRenderer renderer =
                        rig.GetPartRenderer(part);
                    Sprite expectedSprite =
                        rig.GetPartSprite(part, expectedDirection);

                    Assert.That(
                        renderer.sprite,
                        Is.SameAs(expectedSprite),
                        $"{part} did not switch to {expectedDirection}.");
                    Assert.That(
                        observedSprites.Add(renderer.sprite),
                        Is.True,
                        $"{part} reused a sprite in {expectedDirection}.");
                }

                Assert.That(
                    Quaternion.Angle(actor.transform.rotation, actorHeading),
                    Is.LessThan(0.001f));
                AssertNoMirroring(rig);
            }

            Assert.That(observedSprites, Has.Count.EqualTo(72));

            PlaceCameraAtRelativeYaw(camera, actor.transform, 360f);
            yield return null;
            Assert.That(
                rig.CurrentDirection,
                Is.EqualTo(PlayerViewDirection.Front));
            AssertDirectionSprites(
                rig,
                PlayerViewDirection.Front);
            Assert.That(
                Quaternion.Angle(actor.transform.rotation, actorHeading),
                Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator SetMotion_AnimatesEveryJointWithBobAndRockThenSettles()
        {
            Camera camera = CreateCamera(new Vector3(0f, 7f, -10f));
            GameObject rigObject =
                CreateObject("Animated Presentation Test Rig");
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera);
            yield return null;

            Transform poseRoot = rig.PoseRoot;
            Vector3 idlePosePosition = poseRoot.localPosition;
            Quaternion idlePoseRotation = poseRoot.localRotation;
            Quaternion[] idleJointRotations =
                new Quaternion[JointParts.Length];
            float[] maximumJointAngles =
                new float[JointParts.Length];
            Sprite[] idleSprites = CaptureDisplayedSprites(rig);

            for (int index = 0; index < JointParts.Length; index++)
            {
                idleJointRotations[index] =
                    rig.GetPartTransform(JointParts[index]).localRotation;
            }

            float maximumBob = 0f;
            float maximumRock = 0f;

            Assert.DoesNotThrow(() => rig.SetMotion(Vector3.left * 4f));
            float motionDeadline = Time.realtimeSinceStartup + 2f;
            while ((!AllValuesExceed(maximumJointAngles, 0.5f) ||
                    maximumBob <= 0.003f ||
                    maximumRock <= 0.1f) &&
                   Time.realtimeSinceStartup < motionDeadline)
            {
                yield return null;

                for (int index = 0;
                     index < JointParts.Length;
                     index++)
                {
                    maximumJointAngles[index] = Mathf.Max(
                        maximumJointAngles[index],
                        Quaternion.Angle(
                            idleJointRotations[index],
                            rig.GetPartTransform(
                                JointParts[index]).localRotation));
                }

                maximumBob = Mathf.Max(
                    maximumBob,
                    Mathf.Abs(
                        poseRoot.localPosition.y -
                        idlePosePosition.y));
                maximumRock = Mathf.Max(
                    maximumRock,
                    Quaternion.Angle(
                        idlePoseRotation,
                        poseRoot.localRotation));
            }

            for (int index = 0; index < JointParts.Length; index++)
            {
                Assert.That(
                    maximumJointAngles[index],
                    Is.GreaterThan(0.5f),
                    $"{JointParts[index]} must visibly rotate while walking.");
            }
            Assert.That(
                maximumBob,
                Is.GreaterThan(0.003f),
                "Walking must bob the whole puppet.");
            Assert.That(
                maximumRock,
                Is.GreaterThan(0.1f),
                "Walking must rock the whole puppet.");
            AssertDisplayedSprites(rig, idleSprites);
            AssertNoMirroring(rig);

            rig.SetMotion(Vector3.zero);
            float minimumSettleTime = Time.realtimeSinceStartup + 0.4f;
            float settleDeadline = Time.realtimeSinceStartup + 2f;
            bool returnedToIdle = false;
            while (Time.realtimeSinceStartup < settleDeadline)
            {
                yield return null;
                returnedToIdle =
                    Vector3.Distance(
                        poseRoot.localPosition,
                        idlePosePosition) < 0.003f &&
                    Quaternion.Angle(
                        poseRoot.localRotation,
                        idlePoseRotation) < 0.2f &&
                    AllJointsWithin(
                        rig,
                        idleJointRotations,
                        0.3f);
                if (returnedToIdle &&
                    Time.realtimeSinceStartup >= minimumSettleTime)
                {
                    break;
                }
            }

            Assert.That(
                returnedToIdle,
                Is.True,
                "Stopping must settle every joint and the puppet root.");
            AssertDisplayedSprites(rig, idleSprites);
            AssertNoMirroring(rig);
        }

        [UnityTest]
        public IEnumerator SetMotion_CardinalViewsUseSagittalPlaneAndContralateralPhase()
        {
            yield return AssertProjectedGait(
                PlayerViewDirection.Front);
            yield return AssertProjectedGait(
                PlayerViewDirection.Right);
            yield return AssertProjectedGait(
                PlayerViewDirection.Back);
            yield return AssertProjectedGait(
                PlayerViewDirection.Left);
        }

        [UnityTest]
        public IEnumerator SetMotion_DiagonalViewCombinesScreenAndDepthSwing()
        {
            yield return AssertProjectedGait(
                PlayerViewDirection.FrontRight);
        }

        [UnityTest]
        public IEnumerator SetWasted_SwaysVisualWithoutChangingHeadingAndReturnsToIdle()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject actor = CreateObject("Wasted Presentation Actor");
            actor.transform.position = new Vector3(-2f, 10f, 4f);
            actor.transform.rotation = Quaternion.Euler(0f, 123f, 0f);
            PlaceCameraAtRelativeYaw(camera, actor.transform, 0f);

            GameObject rigObject = CreateObject("Wasted Presentation Rig");
            rigObject.transform.SetParent(actor.transform, false);
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera, actor.transform);
            yield return null;

            Transform poseRoot = rig.PoseRoot;
            Vector3 idlePosition = poseRoot.localPosition;
            Quaternion idleRotation = poseRoot.localRotation;
            Quaternion playerHeading = actor.transform.rotation;
            PlayerViewDirection idleDirection = rig.CurrentDirection;
            Sprite[] idleSprites = CaptureDisplayedSprites(rig);
            float maximumSway = 0f;
            float maximumRock = 0f;

            rig.SetMotion(Vector3.zero);
            rig.SetWasted(true);
            float swayDeadline = Time.realtimeSinceStartup + 1.5f;
            while ((maximumSway <= 0.004f || maximumRock <= 0.2f) &&
                   Time.realtimeSinceStartup < swayDeadline)
            {
                yield return null;
                maximumSway = Mathf.Max(
                    maximumSway,
                    Mathf.Abs(poseRoot.localPosition.x - idlePosition.x));
                maximumRock = Mathf.Max(
                    maximumRock,
                    Quaternion.Angle(
                        idleRotation,
                        poseRoot.localRotation));
                Assert.That(
                    Quaternion.Angle(
                        actor.transform.rotation,
                        playerHeading),
                    Is.LessThan(0.001f));
                Assert.That(
                    rig.CurrentDirection,
                    Is.EqualTo(idleDirection));
            }

            Assert.That(maximumSway, Is.GreaterThan(0.004f));
            Assert.That(maximumRock, Is.GreaterThan(0.2f));
            Assert.That(rig.CurrentDirection, Is.EqualTo(idleDirection));
            AssertDisplayedSprites(rig, idleSprites);
            AssertNoMirroring(rig);

            rig.SetWasted(false);
            float minimumSettleTime = Time.realtimeSinceStartup + 0.5f;
            float settleDeadline = Time.realtimeSinceStartup + 2f;
            bool returnedToIdle = false;
            while (Time.realtimeSinceStartup < settleDeadline)
            {
                yield return null;
                Assert.That(
                    Quaternion.Angle(
                        actor.transform.rotation,
                        playerHeading),
                    Is.LessThan(0.001f));
                Assert.That(
                    rig.CurrentDirection,
                    Is.EqualTo(idleDirection));

                returnedToIdle =
                    Vector3.Distance(
                        poseRoot.localPosition,
                        idlePosition) < 0.003f &&
                    Quaternion.Angle(
                        poseRoot.localRotation,
                        idleRotation) < 0.2f;
                if (returnedToIdle &&
                    Time.realtimeSinceStartup >= minimumSettleTime)
                {
                    break;
                }
            }

            Assert.That(
                returnedToIdle,
                Is.True,
                "Disabling Wasted must settle the visual back to idle.");
            Assert.That(
                Quaternion.Angle(actor.transform.rotation, playerHeading),
                Is.LessThan(0.001f));
            Assert.That(rig.CurrentDirection, Is.EqualTo(idleDirection));
            AssertDisplayedSprites(rig, idleSprites);
            AssertNoMirroring(rig);
        }

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

            follow.RotateYaw(135f);
            Assert.That(
                Quaternion.Angle(
                    player.transform.rotation,
                    originalPlayerHeading),
                Is.LessThan(0.001f));
            follow.Snap();
            yield return null;

            Vector3 focusPoint = player.transform.position + Vector3.up * 1.1f;
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
        public IEnumerator ExteriorCamera_ObstacleImmediatelyShortensCameraArm()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Obstructed Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            yield return null;

            Vector3 focusPoint = player.transform.position + Vector3.up * 1.1f;
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
            Assert.That(unobstructedDistance, Is.GreaterThan(5f));
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

        private IEnumerator AssertProjectedGait(
            PlayerViewDirection expectedDirection)
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject actor = CreateObject(
                $"{expectedDirection} Gait Actor");
            actor.transform.position = new Vector3(
                0f,
                20f + (int)expectedDirection * 4f,
                0f);
            PlaceCameraAtRelativeYaw(
                camera,
                actor.transform,
                (int)expectedDirection * 45f);

            GameObject rigObject = CreateObject(
                $"{expectedDirection} Gait Rig");
            rigObject.transform.SetParent(actor.transform, false);
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera, actor.transform);
            yield return null;

            Assert.That(
                rig.CurrentDirection,
                Is.EqualTo(expectedDirection));

            float viewAngle =
                (int)expectedDirection * 45f * Mathf.Deg2Rad;
            Vector3 expectedRotationAxis = new Vector3(
                Mathf.Cos(viewAngle),
                0f,
                Mathf.Sin(viewAngle)).normalized;
            Vector3 expectedSwingDirection = Vector3.Cross(
                expectedRotationAxis,
                Vector3.down).normalized;

            rig.SetMotion(Vector3.forward * 4f);
            float deadline = Time.realtimeSinceStartup + 2f;
            float leftLegSwing = 0f;
            while (Mathf.Abs(leftLegSwing) < 0.08f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                leftLegSwing = GetProjectedSwing(
                    rig,
                    PlayerPuppetPart.LeftUpperLeg,
                    expectedSwingDirection);
            }

            Assert.That(
                Mathf.Abs(leftLegSwing),
                Is.GreaterThanOrEqualTo(0.08f),
                $"{expectedDirection} must produce a readable projected stride.");

            float rightLegSwing = GetProjectedSwing(
                rig,
                PlayerPuppetPart.RightUpperLeg,
                expectedSwingDirection);
            float leftArmSwing = GetProjectedSwing(
                rig,
                PlayerPuppetPart.LeftUpperArm,
                expectedSwingDirection);
            float rightArmSwing = GetProjectedSwing(
                rig,
                PlayerPuppetPart.RightUpperArm,
                expectedSwingDirection);

            Assert.That(
                leftLegSwing * rightLegSwing,
                Is.LessThan(-0.002f),
                $"{expectedDirection} legs must move in opposite phases.");
            Assert.That(
                leftLegSwing * leftArmSwing,
                Is.LessThan(-0.002f),
                $"{expectedDirection} same-side arm and leg must oppose.");
            Assert.That(
                leftLegSwing * rightArmSwing,
                Is.GreaterThan(0.002f),
                $"{expectedDirection} contralateral arm and leg must agree.");

            Vector3 leftLegDirection =
                GetJointDownDirection(
                    rig,
                    PlayerPuppetPart.LeftUpperLeg);
            Vector3 planarSwing = new Vector3(
                leftLegDirection.x,
                0f,
                leftLegDirection.z);
            Assert.That(
                Mathf.Abs(Vector3.Dot(
                    planarSwing.normalized,
                    expectedSwingDirection)),
                Is.GreaterThan(0.98f),
                $"{expectedDirection} stride left its sagittal plane.");

            float expectedScreenWeight =
                Mathf.Abs(expectedSwingDirection.x);
            float expectedDepthWeight =
                Mathf.Abs(expectedSwingDirection.z);
            if (expectedScreenWeight < 0.01f)
            {
                Assert.That(
                    Mathf.Abs(leftLegDirection.x),
                    Is.LessThan(0.01f),
                    $"{expectedDirection} must not fan the legs sideways.");
            }
            else
            {
                Assert.That(
                    Mathf.Abs(leftLegDirection.x),
                    Is.GreaterThan(0.04f));
            }

            if (expectedDepthWeight < 0.01f)
            {
                Assert.That(
                    Mathf.Abs(leftLegDirection.z),
                    Is.LessThan(0.01f),
                    $"{expectedDirection} side view must remain screen-plane.");
            }
            else
            {
                Assert.That(
                    Mathf.Abs(leftLegDirection.z),
                    Is.GreaterThan(0.04f));
                AssertDepthSortingMatchesSwing(
                    rig,
                    PlayerPuppetPart.LeftUpperLeg,
                    PlayerPuppetPart.LeftLowerLeg);
                AssertDepthSortingMatchesSwing(
                    rig,
                    PlayerPuppetPart.RightUpperLeg,
                    PlayerPuppetPart.RightLowerLeg);
                AssertDepthSortingMatchesSwing(
                    rig,
                    PlayerPuppetPart.LeftUpperArm,
                    PlayerPuppetPart.LeftLowerArm);
                AssertDepthSortingMatchesSwing(
                    rig,
                    PlayerPuppetPart.RightUpperArm,
                    PlayerPuppetPart.RightLowerArm);
            }

            rig.SetMotion(Vector3.zero);
            AssertNoMirroring(rig);
        }

        private Camera CreateCamera(Vector3 position)
        {
            GameObject cameraObject = CreateObject("Presentation Test Camera");
            cameraObject.transform.position = position;
            return cameraObject.AddComponent<Camera>();
        }

        private static void PlaceCameraAtRelativeYaw(
            Camera camera,
            Transform actor,
            float relativeYaw)
        {
            Vector3 actorForward = Vector3.ProjectOnPlane(
                actor.forward,
                Vector3.up).normalized;
            Vector3 flatOffset =
                Quaternion.AngleAxis(relativeYaw, Vector3.up) *
                actorForward *
                7f;
            Vector3 focusPoint = actor.position + Vector3.up;
            camera.transform.position =
                actor.position + flatOffset + Vector3.up * 4f;
            camera.transform.LookAt(focusPoint);
        }

        private static void AssertJointPair(
            PlayerSpriteRig rig,
            PlayerPuppetPart upperPart,
            PlayerPuppetPart lowerPart)
        {
            Transform upper = rig.GetPartTransform(upperPart);
            Transform lower = rig.GetPartTransform(lowerPart);

            Assert.That(upper.parent, Is.SameAs(rig.PoseRoot));
            Assert.That(upper.name, Is.EqualTo(upperPart.ToString()));
            Assert.That(upper.childCount, Is.EqualTo(1));
            Assert.That(upper.GetChild(0), Is.SameAs(lower));
            Assert.That(lower.parent, Is.SameAs(upper));
            Assert.That(lower.name, Is.EqualTo(lowerPart.ToString()));
            Assert.That(lower.childCount, Is.EqualTo(0));
        }

        private static float GetProjectedSwing(
            PlayerSpriteRig rig,
            PlayerPuppetPart part,
            Vector3 expectedSwingDirection)
        {
            return Vector3.Dot(
                GetJointDownDirection(rig, part),
                expectedSwingDirection);
        }

        private static Vector3 GetJointDownDirection(
            PlayerSpriteRig rig,
            PlayerPuppetPart part)
        {
            return rig.GetPartTransform(part).localRotation *
                   Vector3.down;
        }

        private static void AssertDepthSortingMatchesSwing(
            PlayerSpriteRig rig,
            PlayerPuppetPart upperPart,
            PlayerPuppetPart lowerPart)
        {
            float depth =
                GetJointDownDirection(rig, upperPart).z;
            Assert.That(
                Mathf.Abs(depth),
                Is.GreaterThan(0.005f),
                $"{upperPart} needs a stable near/far depth.");

            int upperOrder =
                rig.GetPartRenderer(upperPart).sortingOrder;
            int lowerOrder =
                rig.GetPartRenderer(lowerPart).sortingOrder;
            Assert.That(
                upperOrder,
                Is.EqualTo(lowerOrder),
                $"{upperPart} and {lowerPart} must share one depth layer.");
            Assert.That(
                upperOrder,
                depth > 0f
                    ? Is.GreaterThan(0)
                    : Is.LessThan(0),
                $"{upperPart} sorting must follow its projected depth.");
        }

        private static Vector3 GetFrameOriginInPoseSpace(
            PlayerSpriteRig rig,
            PlayerPuppetPart part)
        {
            SpriteRenderer renderer = rig.GetPartRenderer(part);
            Sprite sprite = renderer.sprite;
            Vector3 originFromPivot = new Vector3(
                -sprite.pivot.x / sprite.pixelsPerUnit,
                -sprite.pivot.y / sprite.pixelsPerUnit,
                0f);
            Vector3 worldOrigin =
                renderer.transform.TransformPoint(originFromPivot);
            return rig.PoseRoot.InverseTransformPoint(worldOrigin);
        }

        private static void AssertDirectionSprites(
            PlayerSpriteRig rig,
            PlayerViewDirection direction)
        {
            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                Assert.That(
                    rig.GetPartRenderer(part).sprite,
                    Is.SameAs(rig.GetPartSprite(part, direction)));
            }
        }

        private static Sprite[] CaptureDisplayedSprites(
            PlayerSpriteRig rig)
        {
            var sprites = new Sprite[PlayerSpriteRig.PartCount];
            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                sprites[partIndex] =
                    rig.GetPartRenderer(part).sprite;
            }

            return sprites;
        }

        private static void AssertDisplayedSprites(
            PlayerSpriteRig rig,
            Sprite[] expectedSprites)
        {
            Assert.That(
                expectedSprites,
                Has.Length.EqualTo(PlayerSpriteRig.PartCount));
            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                Assert.That(
                    rig.GetPartRenderer(part).sprite,
                    Is.SameAs(expectedSprites[partIndex]));
            }
        }

        private static bool AllValuesExceed(
            float[] values,
            float threshold)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] <= threshold)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllJointsWithin(
            PlayerSpriteRig rig,
            Quaternion[] expectedRotations,
            float toleranceDegrees)
        {
            for (int index = 0; index < JointParts.Length; index++)
            {
                if (Quaternion.Angle(
                        rig.GetPartTransform(
                            JointParts[index]).localRotation,
                        expectedRotations[index]) >= toleranceDegrees)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AssertNoMirroring(PlayerSpriteRig rig)
        {
            AssertPositiveScale(rig.VisualRoot);
            AssertPositiveScale(rig.PoseRoot);

            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                SpriteRenderer renderer =
                    rig.GetPartRenderer(part);

                Assert.That(renderer.flipX, Is.False, $"{part} flipX.");
                Assert.That(renderer.flipY, Is.False, $"{part} flipY.");
                AssertPositiveScale(renderer.transform);
            }
        }

        private static void AssertPositiveScale(Transform transform)
        {
            Assert.That(
                transform.localScale.x,
                Is.GreaterThan(0f),
                $"{transform.name} local scale X.");
            Assert.That(
                transform.localScale.y,
                Is.GreaterThan(0f),
                $"{transform.name} local scale Y.");
            Assert.That(
                transform.localScale.z,
                Is.GreaterThan(0f),
                $"{transform.name} local scale Z.");
        }

        private GameObject CreateObject(string objectName)
        {
            GameObject gameObject = new GameObject(objectName);
            cleanupObjects.Add(gameObject);
            return gameObject;
        }
    }
}
