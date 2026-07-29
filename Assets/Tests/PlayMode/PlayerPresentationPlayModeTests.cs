using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
            bool returnedToLivingIdle = false;
            while (Time.realtimeSinceStartup < settleDeadline)
            {
                yield return null;
                returnedToLivingIdle =
                    IsLivingIdleWithinBounds(rig);
                if (returnedToLivingIdle &&
                    Time.realtimeSinceStartup >= minimumSettleTime)
                {
                    break;
                }
            }

            Assert.That(
                returnedToLivingIdle,
                Is.True,
                "Stopping must settle into the bounded living idle.");
            AssertDisplayedSprites(rig, idleSprites);
            AssertNoMirroring(rig);
        }

        [UnityTest]
        public IEnumerator WalkCadence_ScalesWithActualTravelSpeed()
        {
            Camera camera = CreateCamera(new Vector3(0f, 7f, -10f));
            GameObject rigObject =
                CreateObject("Distance Driven Gait Test Rig");
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera);
            yield return null;

            FieldInfo phaseField = typeof(PlayerSpriteRig).GetField(
                "animationPhase",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(phaseField, Is.Not.Null);
            MethodInfo animateMethod =
                typeof(PlayerSpriteRig).GetMethod(
                    "AnimatePuppet",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(animateMethod, Is.Not.Null);
            AssertPrivateFloat(rig, "fullAnimationSpeed", 5.2f);
            AssertPrivateFloat(rig, "walkCycleDistance", 2.7f);
            AssertPrivateFloat(rig, "settleSpeed", 8f);
            AssertPrivateFloat(rig, "walkRockDegrees", 1.8f);
            AssertPrivateFloat(
                rig,
                "walkBodyCompressionHeight",
                0.012f);
            AssertPrivateFloat(
                rig,
                "footPlantCompressionHeight",
                0.005f);
            rig.enabled = false;

            const float slowSpeed = 1.3f;
            const float sampleDuration = 0.35f;
            rig.SetMotion(Vector3.forward * slowSpeed);
            float slowPhaseStart = (float)phaseField.GetValue(rig);
            animateMethod.Invoke(
                rig,
                new object[] { sampleDuration });

            float slowPhaseDelta =
                (float)phaseField.GetValue(rig) - slowPhaseStart;
            float slowPhaseRate =
                slowPhaseDelta / sampleDuration;

            const float fastSpeed = 5.2f;
            rig.SetMotion(Vector3.forward * fastSpeed);
            float fastPhaseStart = (float)phaseField.GetValue(rig);
            animateMethod.Invoke(
                rig,
                new object[] { sampleDuration });

            float fastPhaseDelta =
                (float)phaseField.GetValue(rig) - fastPhaseStart;
            float fastPhaseRate =
                fastPhaseDelta / sampleDuration;

            Assert.That(
                slowPhaseRate,
                Is.EqualTo(
                    slowSpeed / 2.7f * Mathf.PI * 2f)
                    .Within(0.001f));
            Assert.That(
                fastPhaseRate / slowPhaseRate,
                Is.InRange(3.7f, 4.3f),
                "Walking cadence must follow distance travelled instead " +
                "of playing at one fixed rate.");
        }

        [UnityTest]
        public IEnumerator Idle_BreathesShiftsWeightAndFidgetsInSagittalPlane()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject actor = CreateObject("Living Idle Actor");
            actor.transform.position = new Vector3(0f, 30f, 0f);
            actor.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            PlaceCameraAtRelativeYaw(camera, actor.transform, 0f);

            GameObject rigObject = CreateObject("Living Idle Rig");
            rigObject.transform.SetParent(actor.transform, false);
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera, actor.transform);
            rig.SetMotion(Vector3.zero);
            yield return null;

            Quaternion actorHeading = actor.transform.rotation;
            PlayerViewDirection idleDirection = rig.CurrentDirection;
            Sprite[] idleSprites = CaptureDisplayedSprites(rig);
            float minimumY = float.PositiveInfinity;
            float maximumY = float.NegativeInfinity;
            float minimumX = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumLeftArmAngle = 0f;
            float maximumRightArmAngle = 0f;
            Vector3 leftArmDirectionAtMaximum = Vector3.down;
            Vector3 rightArmDirectionAtMaximum = Vector3.down;
            float firstLeftGestureTime = float.PositiveInfinity;
            float firstRightGestureTime = float.PositiveInfinity;
            bool sawFarLeftArmLayer = false;
            float observationStart = Time.realtimeSinceStartup;
            float deadline = observationStart + 8.45f;

            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;

                Vector3 posePosition = rig.PoseRoot.localPosition;
                minimumX = Mathf.Min(minimumX, posePosition.x);
                maximumX = Mathf.Max(maximumX, posePosition.x);
                minimumY = Mathf.Min(
                    minimumY,
                    rig.UpperBodyOffset.y);
                maximumY = Mathf.Max(
                    maximumY,
                    rig.UpperBodyOffset.y);

                Transform leftArm = rig.GetPartTransform(
                    PlayerPuppetPart.LeftUpperArm);
                float leftArmAngle = Quaternion.Angle(
                    Quaternion.identity,
                    leftArm.localRotation);
                if (leftArmAngle > maximumLeftArmAngle)
                {
                    maximumLeftArmAngle = leftArmAngle;
                    leftArmDirectionAtMaximum =
                        leftArm.localRotation * Vector3.down;
                }
                if (leftArmAngle > 1.2f &&
                    float.IsPositiveInfinity(firstLeftGestureTime))
                {
                    firstLeftGestureTime =
                        Time.realtimeSinceStartup - observationStart;
                }

                Transform rightArm = rig.GetPartTransform(
                    PlayerPuppetPart.RightUpperArm);
                float rightArmAngle = Quaternion.Angle(
                    Quaternion.identity,
                    rightArm.localRotation);
                if (rightArmAngle > maximumRightArmAngle)
                {
                    maximumRightArmAngle = rightArmAngle;
                    rightArmDirectionAtMaximum =
                        rightArm.localRotation * Vector3.down;
                }
                if (rightArmAngle > 1.2f &&
                    float.IsPositiveInfinity(firstRightGestureTime))
                {
                    firstRightGestureTime =
                        Time.realtimeSinceStartup - observationStart;
                }

                Assert.That(IsLivingIdleWithinBounds(rig), Is.True);
                float leftArmDepth =
                    (leftArm.localRotation * Vector3.down).z;
                int expectedLeftArmOrder =
                    leftArmDepth < -0.01f ? -2 : 3;
                int leftArmOrder = rig.GetPartRenderer(
                    PlayerPuppetPart.LeftUpperArm).sortingOrder;
                Assert.That(
                    leftArmOrder,
                    Is.EqualTo(expectedLeftArmOrder),
                    "The readable idle gesture must preserve limb depth.");
                sawFarLeftArmLayer |= leftArmOrder == -2;
                float rightArmDepth =
                    (rightArm.localRotation * Vector3.down).z;
                int expectedRightArmOrder =
                    rightArmDepth < -0.01f ? -1 : 4;
                Assert.That(
                    rig.GetPartRenderer(
                        PlayerPuppetPart.RightUpperArm).sortingOrder,
                    Is.EqualTo(expectedRightArmOrder),
                    "The alternating idle gesture must preserve depth.");
                Assert.That(
                    Quaternion.Angle(
                        actor.transform.rotation,
                        actorHeading),
                    Is.LessThan(0.001f));
                Assert.That(rig.CurrentDirection, Is.EqualTo(idleDirection));
            }

            Assert.That(
                maximumY - minimumY,
                Is.GreaterThan(0.0035f),
                "Living idle must include readable breathing.");
            Assert.That(
                maximumX - minimumX,
                Is.GreaterThan(0.0025f),
                "Living idle must include a slow weight shift.");
            Assert.That(
                maximumLeftArmAngle,
                Is.GreaterThan(1.2f),
                "Living idle must include the first left-arm gesture.");
            Assert.That(
                maximumRightArmAngle,
                Is.GreaterThan(1.2f),
                "Living idle must include the following right-arm gesture.");
            Assert.That(
                firstLeftGestureTime,
                Is.LessThan(firstRightGestureTime),
                "The readable idle gesture must alternate left then right.");
            Assert.That(
                sawFarLeftArmLayer,
                Is.True,
                "The expressive cuff gesture must enter its far layer.");
            Assert.That(
                Mathf.Abs(leftArmDirectionAtMaximum.x),
                Is.LessThan(0.002f),
                "Front-view idle motion must remain in the sagittal plane.");
            Assert.That(
                Mathf.Abs(rightArmDirectionAtMaximum.x),
                Is.LessThan(0.002f),
                "The second arm gesture must remain sagittal too.");
            AssertDisplayedSprites(rig, idleSprites);
            AssertNoMirroring(rig);
            AssertUnitScales(rig);
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
        public IEnumerator SetIntoxication_MaximumSwaysVisualWithoutChangingHeadingAndReturnsToIdle()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject actor = CreateObject("Intoxicated Presentation Actor");
            actor.transform.position = new Vector3(-2f, 10f, 4f);
            actor.transform.rotation = Quaternion.Euler(0f, 123f, 0f);
            PlaceCameraAtRelativeYaw(camera, actor.transform, 0f);

            GameObject rigObject = CreateObject("Intoxicated Presentation Rig");
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
            float maximumLeftArmAngle = 0f;
            float maximumRightArmAngle = 0f;

            rig.SetMotion(Vector3.zero);
            rig.SetIntoxication(1f);
            float settledIntoxicationSampleTime =
                Time.realtimeSinceStartup + 0.35f;
            float swayDeadline = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < swayDeadline)
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
                if (Time.realtimeSinceStartup >=
                    settledIntoxicationSampleTime)
                {
                    maximumLeftArmAngle = Mathf.Max(
                        maximumLeftArmAngle,
                        Quaternion.Angle(
                            Quaternion.identity,
                            rig.GetPartTransform(
                                PlayerPuppetPart.LeftUpperArm)
                                .localRotation));
                    maximumRightArmAngle = Mathf.Max(
                        maximumRightArmAngle,
                        Quaternion.Angle(
                            Quaternion.identity,
                            rig.GetPartTransform(
                                PlayerPuppetPart.RightUpperArm)
                                .localRotation));
                }
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
            Assert.That(
                maximumLeftArmAngle,
                Is.GreaterThan(1f),
                "Maximum intoxication must visibly spread the left arm.");
            Assert.That(
                maximumRightArmAngle,
                Is.GreaterThan(1f),
                "Maximum intoxication must visibly spread the right arm.");
            Assert.That(rig.IntoxicationAmount, Is.GreaterThan(0.95f));
            Assert.That(rig.CurrentDirection, Is.EqualTo(idleDirection));
            AssertDisplayedSprites(rig, idleSprites);
            AssertNoMirroring(rig);

            rig.SetIntoxication(0f);
            float minimumSettleTime = Time.realtimeSinceStartup + 0.5f;
            float settleDeadline = Time.realtimeSinceStartup + 2f;
            bool returnedToLivingIdle = false;
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

                returnedToLivingIdle =
                    IsLivingIdleWithinBounds(rig);
                if (returnedToLivingIdle &&
                    Time.realtimeSinceStartup >= minimumSettleTime)
                {
                    break;
                }
            }

            Assert.That(
                returnedToLivingIdle,
                Is.True,
                "Clearing intoxication must settle back into living idle.");

            float minimumLivingIdleY = rig.UpperBodyOffset.y;
            float maximumLivingIdleY = rig.UpperBodyOffset.y;
            float livingIdleDeadline = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < livingIdleDeadline)
            {
                yield return null;
                minimumLivingIdleY = Mathf.Min(
                    minimumLivingIdleY,
                    rig.UpperBodyOffset.y);
                maximumLivingIdleY = Mathf.Max(
                    maximumLivingIdleY,
                    rig.UpperBodyOffset.y);
                Assert.That(IsLivingIdleWithinBounds(rig), Is.True);
            }

            Assert.That(
                maximumLivingIdleY - minimumLivingIdleY,
                Is.GreaterThan(0.0004f),
                "Living idle must resume after the intoxication sway.");
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

        private static void AssertPrivateFloat(
            object target,
            string fieldName,
            float expected)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            Assert.That(
                (float)field.GetValue(target),
                Is.EqualTo(expected).Within(0.0001f));
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
                Sprite displayed =
                    rig.GetPartRenderer(part).sprite;
                if (part == PlayerPuppetPart.Body)
                {
                    Assert.That(
                        expectedSprites[partIndex],
                        Is.SameAs(rig.GetPartSprite(
                            part,
                            rig.CurrentDirection)));
                    bool faceVisible =
                        rig.CurrentDirection ==
                            PlayerViewDirection.Front ||
                        rig.CurrentDirection ==
                            PlayerViewDirection.FrontRight ||
                        rig.CurrentDirection ==
                            PlayerViewDirection.Right ||
                        rig.CurrentDirection ==
                            PlayerViewDirection.Left ||
                        rig.CurrentDirection ==
                            PlayerViewDirection.FrontLeft;
                    Sprite expectedBody =
                        rig.CurrentFacialExpression ==
                            PlayerFacialExpression.Neutral ||
                        !faceVisible
                            ? expectedSprites[partIndex]
                            : rig.GetFacialExpressionSprite(
                                rig.CurrentFacialExpression,
                                rig.CurrentDirection);
                    Assert.That(
                        displayed,
                        Is.SameAs(expectedBody));
                    continue;
                }

                Assert.That(
                    displayed,
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

        private static bool IsLivingIdleWithinBounds(
            PlayerSpriteRig rig)
        {
            Vector3 posePosition = rig.PoseRoot.localPosition;
            if (Mathf.Abs(posePosition.x) > 0.01f ||
                posePosition.y < -0.006f ||
                posePosition.y > 0.012f ||
                Mathf.Abs(posePosition.z) > 0.001f ||
                Quaternion.Angle(
                    Quaternion.identity,
                    rig.PoseRoot.localRotation) > 0.95f)
            {
                return false;
            }

            Vector3 upperBodyOffset = rig.UpperBodyOffset;
            if (Mathf.Abs(upperBodyOffset.x) > 0.001f ||
                upperBodyOffset.y < -0.001f ||
                upperBodyOffset.y > 0.012f ||
                Mathf.Abs(upperBodyOffset.z) > 0.001f)
            {
                return false;
            }

            for (int index = 0; index < JointParts.Length; index++)
            {
                if (Quaternion.Angle(
                        Quaternion.identity,
                        rig.GetPartTransform(
                            JointParts[index]).localRotation) > 2.2f)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AssertUnitScales(PlayerSpriteRig rig)
        {
            Assert.That(rig.VisualRoot.localScale, Is.EqualTo(Vector3.one));
            Assert.That(rig.PoseRoot.localScale, Is.EqualTo(Vector3.one));

            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                Transform part = rig.GetPartTransform(
                    (PlayerPuppetPart)partIndex);
                Assert.That(
                    part.localScale,
                    Is.EqualTo(Vector3.one),
                    $"{part.name} must not use scale animation.");
            }
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
