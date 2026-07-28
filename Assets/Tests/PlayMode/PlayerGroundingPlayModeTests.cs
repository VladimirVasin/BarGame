using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerGroundingPlayModeTests
    {
        private const float FullSpeed = 5.2f;
        private const float WalkCycleDistance = 2.7f;
        private const float SampleDeltaTime = 1f;

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
                    Object.Destroy(cleanupObjects[index]);
                }
            }

            cleanupObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundedGait_AlternatesAPlantedFootAndCompresses()
        {
            GameObject cameraObject = CreateObject(
                "Grounded Gait Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(8f, 4f, 0f);
            cameraObject.transform.LookAt(Vector3.up);

            GameObject rigObject = CreateObject(
                "Grounded Gait Test Rig");
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera);
            yield return null;

            FieldInfo phaseField = typeof(PlayerSpriteRig).GetField(
                "animationPhase",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo animateMethod =
                typeof(PlayerSpriteRig).GetMethod(
                    "AnimatePuppet",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(phaseField, Is.Not.Null);
            Assert.That(animateMethod, Is.Not.Null);

            rig.enabled = false;
            rig.SetMotion(Vector3.forward * FullSpeed);

            GroundedPoseSample firstStrike = SamplePose(
                rig,
                phaseField,
                animateMethod,
                0f);
            GroundedPoseSample firstTransfer = SamplePose(
                rig,
                phaseField,
                animateMethod,
                Mathf.PI * 0.5f);
            GroundedPoseSample secondStrike = SamplePose(
                rig,
                phaseField,
                animateMethod,
                Mathf.PI);
            GroundedPoseSample secondTransfer = SamplePose(
                rig,
                phaseField,
                animateMethod,
                Mathf.PI * 1.5f);

            AssertFootfall(firstStrike);
            AssertFootfall(secondStrike);
            AssertTransfer(firstTransfer);
            AssertTransfer(secondTransfer);
            Assert.That(
                firstTransfer.FootHeightDelta *
                secondTransfer.FootHeightDelta,
                Is.LessThan(0f),
                "The planted foot must alternate across the two half-cycles.");
            Assert.That(
                firstStrike.UpperBodyY,
                Is.LessThan(firstTransfer.UpperBodyY - 0.009f),
                "Foot strike must visibly compress the upper body.");
            Assert.That(
                secondStrike.UpperBodyY,
                Is.LessThan(secondTransfer.UpperBodyY - 0.009f));

            float minimumFootHeight = float.PositiveInfinity;
            float maximumFootHeight = float.NegativeInfinity;
            for (int sampleIndex = 0; sampleIndex < 16; sampleIndex++)
            {
                GroundedPoseSample sample = SamplePose(
                    rig,
                    phaseField,
                    animateMethod,
                    sampleIndex * Mathf.PI * 2f / 16f);
                minimumFootHeight = Mathf.Min(
                    minimumFootHeight,
                    sample.LowestFootY);
                maximumFootHeight = Mathf.Max(
                    maximumFootHeight,
                    sample.LowestFootY);
            }

            Assert.That(
                minimumFootHeight,
                Is.GreaterThanOrEqualTo(-0.0065f));
            Assert.That(
                maximumFootHeight,
                Is.LessThanOrEqualTo(0.0015f),
                "At least one foot must remain at the ground anchor " +
                "throughout the gait cycle.");
        }

        private static GroundedPoseSample SamplePose(
            PlayerSpriteRig rig,
            FieldInfo phaseField,
            MethodInfo animateMethod,
            float targetPhase)
        {
            float phaseAdvance =
                FullSpeed /
                WalkCycleDistance *
                Mathf.PI *
                2f *
                SampleDeltaTime;
            phaseField.SetValue(rig, targetPhase - phaseAdvance);
            animateMethod.Invoke(
                rig,
                new object[] { SampleDeltaTime });

            float leftFootY =
                rig.LeftFootContactWorldPosition.y;
            float rightFootY =
                rig.RightFootContactWorldPosition.y;
            return new GroundedPoseSample(
                leftFootY,
                rightFootY,
                rig.UpperBodyOffset.y,
                rig.FootPlantAmount);
        }

        private static void AssertFootfall(GroundedPoseSample sample)
        {
            Assert.That(
                sample.LowestFootY,
                Is.InRange(-0.0065f, -0.0035f));
            Assert.That(
                sample.FootPlantAmount,
                Is.GreaterThan(0.99f));
        }

        private static void AssertTransfer(GroundedPoseSample sample)
        {
            Assert.That(
                sample.LowestFootY,
                Is.InRange(-0.0015f, 0.0015f));
            Assert.That(
                Mathf.Abs(sample.FootHeightDelta),
                Is.GreaterThan(0.01f),
                "One foot must swing while the other remains planted.");
            Assert.That(
                sample.FootPlantAmount,
                Is.LessThan(0.01f));
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            cleanupObjects.Add(result);
            return result;
        }

        private readonly struct GroundedPoseSample
        {
            public GroundedPoseSample(
                float leftFootY,
                float rightFootY,
                float upperBodyY,
                float footPlantAmount)
            {
                LeftFootY = leftFootY;
                RightFootY = rightFootY;
                UpperBodyY = upperBodyY;
                FootPlantAmount = footPlantAmount;
            }

            public float LeftFootY { get; }
            public float RightFootY { get; }
            public float UpperBodyY { get; }
            public float FootPlantAmount { get; }
            public float LowestFootY =>
                Mathf.Min(LeftFootY, RightFootY);
            public float FootHeightDelta =>
                LeftFootY - RightFootY;
        }
    }
}
