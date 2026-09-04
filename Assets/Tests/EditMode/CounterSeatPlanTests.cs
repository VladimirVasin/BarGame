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
        public void BarMenuFocus_AdaptsTheCafeCloseUpToTheLargerBooklet()
        {
            var menu = new Vector3(-1.15f, 1.0889f, 5.44f);
            var viewer = new Vector3(-1.15f, 1.6175f, 4.65f);
            Vector3 target = menu +
                Vector3.up * CounterMenuCameraPlan.SurfaceLiftMeters;
            Vector3 planarTowardViewer = Vector3.ProjectOnPlane(
                viewer - target,
                Vector3.up).normalized;
            Vector3 barTarget = target +
                planarTowardViewer *
                BarDrinkMenuPresentation
                    .CameraFocusTargetTowardViewerMeters;

            CounterMenuCameraPlan.Evaluate(
                menu,
                Vector3.up,
                Vector3.forward,
                viewer,
                out Vector3 cafePosition,
                out _);
            CounterMenuCameraPlan.EvaluateOverhead(
                menu,
                Vector3.up,
                Vector3.forward,
                viewer,
                BarDrinkMenuPresentation.CameraFocusDistanceMeters,
                BarDrinkMenuPresentation.CameraFocusSurfaceFacing,
                BarDrinkMenuPresentation
                    .CameraFocusTargetTowardViewerMeters,
                out Vector3 barPosition,
                out Quaternion barRotation);
            Assert.That(
                Vector3.Distance(cafePosition, target),
                Is.EqualTo(CounterMenuCameraPlan.FocusDistanceMeters)
                    .Within(0.0001f));
            Assert.That(
                CounterMenuCameraPlan.FocusFieldOfView,
                Is.EqualTo(40f),
                "The mountain-cafe menu keeps its existing shared default.");
            Assert.That(
                Vector3.Distance(barPosition, barTarget),
                Is.EqualTo(
                    BarDrinkMenuPresentation.CameraFocusDistanceMeters)
                    .Within(0.0001f));
            Assert.That(
                Vector3.Dot(
                    (barPosition - barTarget).normalized,
                    Vector3.up),
                Is.EqualTo(
                    BarDrinkMenuPresentation.CameraFocusSurfaceFacing)
                    .Within(0.0001f),
                "The bar camera must hang almost over the page normal.");
            Assert.That(
                Vector3.Dot(
                    barRotation * Vector3.forward,
                    (barTarget - barPosition).normalized),
                Is.GreaterThan(0.9999f));
            Assert.That(
                Vector3.ProjectOnPlane(
                    barPosition - target,
                    Vector3.up).magnitude,
                Is.LessThan(0.14f),
                "The camera projection must land over the menu footprint.");
            Assert.That(
                BarDrinkMenuPresentation.CameraFocusFieldOfView,
                Is.GreaterThan(CounterMenuCameraPlan.FocusFieldOfView),
                "The wider descriptive booklet needs a wider lens.");
            Assert.That(
                BarDrinkMenuPresentation.CameraFocusDistanceMeters,
                Is.LessThan(0.75f),
                "The former distant bar camera position must not return.");
        }

        [Test]
        public void BarMenuText_UsesInsetTwoByTwoGrid()
        {
            CounterMenuPageStyle style = CounterMenuPageStyle.Bar;
            Vector2 leftTop =
                BarDrinkMenuPresentation.ResolveTextBlockPageOffset(0);
            Vector2 leftBottom =
                BarDrinkMenuPresentation.ResolveTextBlockPageOffset(1);
            Vector2 rightTop =
                BarDrinkMenuPresentation.ResolveTextBlockPageOffset(2);
            Vector2 rightBottom =
                BarDrinkMenuPresentation.ResolveTextBlockPageOffset(3);

            Assert.That(leftTop.x, Is.EqualTo(leftBottom.x));
            Assert.That(rightTop.x, Is.EqualTo(rightBottom.x));
            Assert.That(leftTop.x, Is.LessThan(0f));
            Assert.That(rightTop.x, Is.GreaterThan(0f));
            Assert.That(leftTop.y, Is.EqualTo(rightTop.y));
            Assert.That(leftBottom.y, Is.EqualTo(rightBottom.y));

            float verticalGap = leftTop.y - leftBottom.y -
                                style.ItemBoxSize.y;
            Assert.That(
                verticalGap,
                Is.GreaterThanOrEqualTo(0.03f),
                "The two descriptions need a compact but distinct gap.");

            const float pageRuleInnerEdge = 0.2015f;
            float verticalExtent = leftTop.y +
                                   style.ItemBoxSize.y * 0.5f;
            Assert.That(
                verticalExtent,
                Is.LessThanOrEqualTo(pageRuleInnerEdge - 0.02f),
                "Text boxes must stay clear of the page-head/foot rules.");

            const float spineOuterEdge = 0.007f;
            const float outerRailInnerEdge = 0.2485f;
            const float minimumMargin = 0.005f;
            float halfTextWidth = style.ItemBoxSize.x * 0.5f;
            float halfMarkerWidth = style.MarkerBoxSize.x * 0.5f;
            float leftMarkerOuter = leftTop.x - halfTextWidth -
                                    style.MarkerGapMeters - halfMarkerWidth;
            float rightMarkerInner = rightTop.x - halfTextWidth -
                                     style.MarkerGapMeters - halfMarkerWidth;

            Assert.That(
                leftMarkerOuter,
                Is.GreaterThanOrEqualTo(
                    -outerRailInnerEdge + minimumMargin),
                "The left-page marker must not collide with its outer rail.");
            Assert.That(
                leftTop.x + halfTextWidth,
                Is.LessThanOrEqualTo(-spineOuterEdge - minimumMargin),
                "The left text block must stay clear of the spine.");
            Assert.That(
                rightMarkerInner,
                Is.GreaterThanOrEqualTo(spineOuterEdge + minimumMargin),
                "The right-page marker must stay clear of the spine.");
            Assert.That(
                rightTop.x + halfTextWidth,
                Is.LessThanOrEqualTo(
                    outerRailInnerEdge - minimumMargin),
                "The right text block must stay clear of its outer rail.");
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
