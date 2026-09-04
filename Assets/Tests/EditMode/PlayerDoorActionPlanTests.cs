using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerDoorActionPlanTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void CreateStationary_DerivesUprightDoorFacingPoses()
        {
            Vector3 interactionPosition =
                new Vector3(3f, 0.9f, -5f);
            Vector3 dockRoot =
                new Vector3(
                    1f,
                    PlayerFactory.GroundedRootOffset,
                    -2f);
            Vector3 rawFacing = new Vector3(3f, 4f, -4f);
            Vector3 expectedFacing =
                new Vector3(3f, 0f, -4f).normalized;
            Vector3 expectedHip =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    dockRoot);

            PlayerDoorActionPlan plan =
                PlayerDoorActionPlan.CreateStationary(
                    interactionPosition,
                    dockRoot,
                    rawFacing);

            AssertVector(plan.InteractionPosition, interactionPosition);
            AssertVector(plan.EntryRootPosition, dockRoot);
            AssertVector(plan.ExitRootPosition, dockRoot);
            AssertVector(plan.EntryFacingDirection, expectedFacing);
            AssertVector(plan.ExitFacingDirection, expectedFacing);
            AssertVector(plan.EntryHipPosition, expectedHip);
            AssertVector(plan.ActionHipPosition, expectedHip);
            AssertVector(plan.ExitHipPosition, expectedHip);
            AssertPose(
                plan.EntryPose,
                dockRoot,
                expectedHip,
                expectedFacing);
            AssertPose(
                plan.ExitPose,
                dockRoot,
                expectedHip,
                expectedFacing);
        }

        [Test]
        public void Constructor_PreservesIndependentEntryActionAndExitData()
        {
            Vector3 entryRoot = new Vector3(1f, 0.04f, 2f);
            Vector3 entryHip = new Vector3(1f, 0.74f, 2f);
            Vector3 actionHip = new Vector3(1.1f, 0.71f, 2.2f);
            Vector3 exitRoot = new Vector3(1.4f, 0.04f, 2.5f);
            Vector3 exitHip = new Vector3(1.4f, 0.74f, 2.5f);
            Vector3 entryFacing = new Vector3(2f, 0f, 1f);
            Vector3 exitFacing = new Vector3(-1f, 0f, 3f);

            var plan = new PlayerDoorActionPlan(
                new Vector3(2f, 0.8f, 3f),
                entryRoot,
                entryFacing,
                entryHip,
                actionHip,
                exitRoot,
                exitFacing,
                exitHip);

            AssertVector(plan.EntryRootPosition, entryRoot);
            AssertVector(plan.EntryHipPosition, entryHip);
            AssertVector(plan.ActionHipPosition, actionHip);
            AssertVector(plan.ExitRootPosition, exitRoot);
            AssertVector(plan.ExitHipPosition, exitHip);
            AssertVector(
                plan.EntryFacingDirection,
                entryFacing.normalized);
            AssertVector(
                plan.ExitFacingDirection,
                exitFacing.normalized);
            Assert.That(
                Vector3.Distance(
                    plan.EntryPose.RootPosition,
                    plan.ExitPose.RootPosition),
                Is.GreaterThan(0.1f));
        }

        [Test]
        public void CreateStationary_RejectsMissingPlanarFacing()
        {
            Assert.That(
                () => PlayerDoorActionPlan.CreateStationary(
                    Vector3.zero,
                    Vector3.up * PlayerFactory.GroundedRootOffset,
                    Vector3.up),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void DoorArrivalPose_FacesAwayAndSeedsCameraBehindPlayer()
        {
            var doorObject = new GameObject("Destination Door");
            var playerObject = new GameObject("Returned Player");
            var cameraObject = new GameObject("Returned Camera");
            try
            {
                Vector3 doorward =
                    new Vector3(3f, 2f, -4f).normalized;
                Vector3 expectedForward =
                    new Vector3(-3f, 0f, 4f).normalized;
                Vector3 returnPosition =
                    new Vector3(8f, 0.04f, -6f);
                PlayerDoorActionTarget destinationDoor =
                    doorObject.AddComponent<PlayerDoorActionTarget>();
                destinationDoor.Configure(
                    PlayerDoorActionPlan.CreateStationary(
                        doorObject.transform.position,
                        Vector3.up * PlayerFactory.GroundedRootOffset,
                        doorward));

                PlayerDoorArrivalPose pose =
                    PlayerDoorArrivalPose.FromDestinationDoor(
                        returnPosition,
                        destinationDoor);
                pose.ApplyTo(playerObject.transform);

                AssertVector(playerObject.transform.position, returnPosition);
                AssertVector(playerObject.transform.forward, expectedForward);

                Camera camera = cameraObject.AddComponent<Camera>();
                PlayerCameraFollow follow =
                    cameraObject.AddComponent<PlayerCameraFollow>();
                follow.Initialize(
                    camera,
                    playerObject.transform,
                    false);

                Vector3 playerToCamera =
                    camera.transform.position - playerObject.transform.position;
                playerToCamera.y = 0f;
                Vector3 cameraForward = camera.transform.forward;
                cameraForward.y = 0f;
                Assert.That(
                    Vector3.Dot(
                        playerToCamera.normalized,
                        -expectedForward),
                    Is.GreaterThan(0.995f),
                    "The chase camera must start behind the returned hero.");
                Assert.That(
                    Vector3.Dot(
                        cameraForward.normalized,
                        expectedForward),
                    Is.GreaterThan(0.995f),
                    "The camera must look over the hero's outward shoulder.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(playerObject);
                UnityEngine.Object.DestroyImmediate(doorObject);
            }
        }

        private static void AssertPose(
            PlayerAnimatedInteractionPose pose,
            Vector3 expectedRoot,
            Vector3 expectedHip,
            Vector3 expectedFacing)
        {
            AssertVector(pose.RootPosition, expectedRoot);
            AssertVector(pose.HipPosition, expectedHip);
            Assert.That(
                Vector3.Angle(
                    pose.RootRotation * Vector3.forward,
                    expectedFacing),
                Is.LessThan(0.001f));
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThan(Tolerance));
        }
    }
}
