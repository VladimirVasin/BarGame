using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CounterSeatPlanTests
    {
        private GameObject serviceSpaceObject;

        [TearDown]
        public void TearDown()
        {
            if (serviceSpaceObject != null)
            {
                UnityEngine.Object.DestroyImmediate(serviceSpaceObject);
            }
        }

        [Test]
        public void FromService_ProvidesSharedCafeStoolFallback()
        {
            Transform serviceSpace = CreateServiceSpace();
            BarDrinkServicePlan service = CreateServicePlan();

            CounterSeatPlan plan = CounterSeatPlan.FromService(
                serviceSpace,
                service);

            AssertVector(
                plan.InteractionPosition,
                new Vector3(-1.15f, 0.8175f, 4.77f));
            AssertVector(
                plan.EntryPose.RootPosition,
                new Vector3(
                    -1.15f,
                    PlayerFactory.GroundedRootOffset,
                    4.01f));
            AssertVector(
                plan.ActionHipPosition,
                new Vector3(-1.15f, 0.8475f, 4.77f));
            AssertVector(
                plan.ExitPose.RootPosition,
                plan.EntryPose.RootPosition);
            Assert.That(plan.ApproachWaypointCount, Is.Zero);

            Vector3 livePelvis = plan.ActionHipPosition +
                new Vector3(0.02f, 0.01f, -0.03f);
            plan.EvaluateCamera(
                livePelvis,
                0f,
                0f,
                out Vector3 cameraPosition,
                out Quaternion cameraRotation);
            AssertVector(
                cameraPosition,
                service.CameraPosition +
                new Vector3(0.02f, 0.01f, -0.03f));
            Assert.That(
                Quaternion.Angle(cameraRotation, service.CameraRotation),
                Is.LessThan(0.001f));
        }

        [Test]
        public void FromServiceAnchors_UsesAuthoredSeatApproachAndDock()
        {
            Transform serviceSpace = CreateServiceSpace();
            BarDrinkServicePlan service = CreateServicePlan();
            Transform seat = CreateAnchor(
                "HeroSeat",
                new Vector3(-1.15f, 0.82f, 4.77f));
            Transform approach = CreateAnchor(
                "HeroApproach",
                new Vector3(-1.15f, 0f, 3.35f));
            Transform stand = CreateAnchor(
                "HeroStand",
                new Vector3(-1.15f, 0f, 4.02f));

            CounterSeatPlan plan =
                CounterSeatPlan.FromServiceAnchors(
                    serviceSpace,
                    service,
                    seat,
                    approach,
                    stand,
                    stand);

            AssertVector(plan.InteractionPosition, seat.position);
            AssertVector(
                plan.ActionHipPosition,
                new Vector3(-1.15f, 0.85f, 4.77f));
            AssertVector(
                plan.EntryPose.RootPosition,
                new Vector3(
                    -1.15f,
                    PlayerFactory.GroundedRootOffset,
                    4.02f));
            AssertVector(
                plan.ExitPose.RootPosition,
                new Vector3(
                    -1.15f,
                    PlayerFactory.GroundedRootOffset,
                    4.02f));
            Assert.That(plan.ApproachWaypointCount, Is.EqualTo(1));
            AssertVector(
                plan.ApproachWaypoints[0],
                new Vector3(
                    -1.15f,
                    PlayerFactory.GroundedRootOffset,
                    3.35f));
            Assert.That(
                Vector3.Dot(
                    plan.EntryPose.RootRotation * Vector3.forward,
                    Vector3.forward),
                Is.GreaterThan(0.999f));

            var buffer = new Vector3[
                CounterSeatPlan.MaximumApproachWaypoints];
            Assert.That(
                plan.BuildApproachWaypoints(
                    new Vector3(
                        -1.15f,
                        PlayerFactory.GroundedRootOffset,
                        4.30f),
                    buffer),
                Is.Zero,
                "A player already beside the dock must not backtrack.");
            Assert.That(
                plan.BuildApproachWaypoints(
                    new Vector3(
                        -1.15f,
                        PlayerFactory.GroundedRootOffset,
                        3.0f),
                    buffer),
                Is.EqualTo(1));
            AssertVector(
                buffer[0],
                new Vector3(
                    -1.15f,
                    PlayerFactory.GroundedRootOffset,
                    3.35f));
        }

        [Test]
        public void
            FromServiceAnchors_IgnoresVerticalEmptyRotationsAndDerivesYaw()
        {
            Transform serviceSpace = CreateServiceSpace();
            BarDrinkServicePlan service = CreateServicePlan();
            Quaternion verticalEmptyRotation = Quaternion.LookRotation(
                Vector3.up,
                Vector3.forward);
            Transform seat = CreateAnchor(
                "Vertical HeroSeat",
                new Vector3(2f, 0.82f, 5f),
                verticalEmptyRotation);
            Transform entry = CreateAnchor(
                "Vertical HeroStand",
                new Vector3(1f, 0f, 3f),
                verticalEmptyRotation);
            Transform exit = CreateAnchor(
                "Vertical HeroExit",
                new Vector3(0.5f, 0f, 2.5f),
                Quaternion.LookRotation(Vector3.down, Vector3.back));

            CounterSeatPlan plan =
                CounterSeatPlan.FromServiceAnchors(
                    serviceSpace,
                    service,
                    seat,
                    approachGroundAnchor: null,
                    entryGroundAnchor: entry,
                    exitGroundAnchor: exit);

            Vector3 expectedFacing = new Vector3(1f, 0f, 2f).normalized;
            Vector3 entryFacing =
                plan.EntryPose.RootRotation * Vector3.forward;
            Vector3 exitFacing =
                plan.ExitPose.RootRotation * Vector3.forward;
            Assert.That(Mathf.Abs(entryFacing.y), Is.LessThan(0.0001f));
            Assert.That(Mathf.Abs(exitFacing.y), Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Dot(entryFacing, expectedFacing),
                Is.GreaterThan(0.9999f));
            Assert.That(
                Vector3.Dot(exitFacing, expectedFacing),
                Is.GreaterThan(0.9999f));
            Assert.That(
                Vector3.Dot(
                    plan.EntryPose.RootRotation * Vector3.up,
                    Vector3.up),
                Is.GreaterThan(0.9999f));
            Assert.That(
                Vector3.Dot(
                    plan.ExitPose.RootRotation * Vector3.up,
                    Vector3.up),
                Is.GreaterThan(0.9999f));
        }

        [Test]
        public void FromServiceAnchors_UsesAuthoredCameraWithoutHiddenClamping()
        {
            Transform serviceSpace = CreateServiceSpace();
            BarDrinkServicePlan service = CreateServicePlan();
            Transform seat = CreateAnchor(
                "HeroSeat",
                new Vector3(-1.15f, 0.8175f, 4.53f));
            Transform stand = CreateAnchor(
                "HeroStand",
                new Vector3(-1.15f, 0f, 4.02f));
            Transform camera = CreateAnchor(
                "HeroCamera",
                new Vector3(-1.15f, 1.6175f, 4.65f));
            Transform cameraLook = CreateAnchor(
                "HeroCameraLook",
                new Vector3(-1.15f, 1.7175f, 7.37f));

            CounterSeatPlan plan = CounterSeatPlan.FromServiceAnchors(
                serviceSpace,
                service,
                seat,
                approachGroundAnchor: null,
                entryGroundAnchor: stand,
                exitGroundAnchor: stand,
                cameraAnchor: camera,
                cameraLookAtAnchor: cameraLook);
            plan.EvaluateCamera(
                plan.ActionHipPosition,
                0f,
                0f,
                out Vector3 position,
                out Quaternion rotation);

            AssertVector(position, new Vector3(-1.15f, 1.6175f, 4.65f));
            Assert.That(
                Vector3.Dot(
                    rotation * Vector3.forward,
                    (cameraLook.position - position).normalized),
                Is.GreaterThan(0.9999f));
        }

        [Test]
        public void BarMenuFocus_PreservesCafeDefaultsAndUsesReadableFraming()
        {
            var menu = new Vector3(-0.38f, 1.0889f, 5.44f);
            var viewer = new Vector3(-1.15f, 1.6175f, 4.65f);
            Vector3 target = menu +
                Vector3.up * CounterMenuCameraPlan.SurfaceLiftMeters;

            CounterMenuCameraPlan.Evaluate(
                menu,
                Vector3.up,
                Vector3.forward,
                viewer,
                out Vector3 cafePosition,
                out _);
            CounterMenuCameraPlan.Evaluate(
                menu,
                Vector3.up,
                Vector3.forward,
                viewer,
                BarDrinkMenuPresentation.CameraFocusDistanceMeters,
                out Vector3 barPosition,
                out _);

            Assert.That(
                Vector3.Distance(cafePosition, target),
                Is.EqualTo(CounterMenuCameraPlan.FocusDistanceMeters)
                    .Within(0.0001f));
            Assert.That(
                CounterMenuCameraPlan.FocusFieldOfView,
                Is.EqualTo(40f),
                "The mountain-cafe menu keeps its existing shared default.");
            Assert.That(
                Vector3.Distance(barPosition, target),
                Is.EqualTo(
                    BarDrinkMenuPresentation.CameraFocusDistanceMeters)
                    .Within(0.0001f));
            Assert.That(
                Vector3.Distance(barPosition, viewer),
                Is.LessThan(0.20f));
            Assert.That(
                barPosition.y,
                Is.GreaterThanOrEqualTo(1.20f),
                "The focused lens must remain visibly above the " +
                "1.02 m counter top.");
            Assert.That(
                BarDrinkMenuPresentation.CameraFocusFieldOfView,
                Is.EqualTo(60f));
        }

        private Transform CreateServiceSpace()
        {
            serviceSpaceObject = new GameObject("Counter Seat Test Space");
            return serviceSpaceObject.transform;
        }

        private Transform CreateAnchor(
            string anchorName,
            Vector3 position,
            Quaternion? rotation = null)
        {
            var anchor = new GameObject(anchorName);
            anchor.transform.SetParent(serviceSpaceObject.transform, false);
            anchor.transform.position = position;
            anchor.transform.rotation = rotation ?? Quaternion.identity;
            return anchor.transform;
        }

        private static BarDrinkServicePlan CreateServicePlan()
        {
            var neutral = new BarDrinkServicePose(
                Vector3.zero,
                Quaternion.identity);
            return new BarDrinkServicePlan(
                "counter-seat-test",
                17u,
                new BarDrinkServicePose(
                    new Vector3(-1.15f, 0f, 4.77f),
                    Quaternion.identity),
                new Vector3(-1.15f, 1.76f, 4.81f),
                new Vector3(-1.15f, 2.12f, 5.45f),
                72f,
                new Vector3(-1.15f, 0f, 4.80f),
                neutral,
                neutral,
                neutral,
                neutral,
                neutral,
                Array.Empty<BarDrinkBottleSlotPlan>());
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThan(0.0001f),
                $"Expected {expected}, but was {actual}.");
        }
    }
}
