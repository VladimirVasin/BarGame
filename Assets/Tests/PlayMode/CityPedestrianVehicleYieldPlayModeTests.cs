using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityPedestrianVehicleYieldPlayModeTests
    {
        private const float Step = 1f / 30f;
        private static readonly Vector3 Origin = new Vector3(1300f, 100f, 1300f);
        private static readonly Bounds VehicleBody = new Bounds(
            new Vector3(0f, 1.5f, 0f), new Vector3(2f, 3f, 10f));
        private GameObject root;
        private CityPedestrianActor actor;
        private CityPedestrianPresentation presentation;
        private CityPedestrianPlan plan;
        private CityPedestrianVehicleTraffic traffic;

        private sealed class FlatArea : IWalkableArea
        {
            public float HalfWidth = 20f;

            public bool Contains(Vector3 position, float radius = 0f) =>
                Mathf.Abs(position.x - Origin.x) + radius <= HalfWidth &&
                Mathf.Abs(position.z - Origin.z) + radius <= 20f;

            public Vector3 Constrain(Vector3 currentPosition,
                Vector3 desiredPosition, float radius = 0f)
            {
                desiredPosition.x = Mathf.Clamp(desiredPosition.x,
                    Origin.x - HalfWidth + radius, Origin.x + HalfWidth - radius);
                desiredPosition.z = Mathf.Clamp(desiredPosition.z,
                    Origin.z - 20f + radius, Origin.z + 20f - radius);
                desiredPosition.y = Origin.y;
                return desiredPosition;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (actor != null && actor.IsSpawned)
                actor.ReleasePresentation(root.transform);
            presentation?.Shutdown();
            if (root != null) Object.DestroyImmediate(root);
            presentation = null;
            actor = null;
            root = null;
        }

        [Test]
        public void VehicleCorridor_UsesRotatedBodyAndKeepsDifferentElevationsSeparate()
        {
            var sampled = new CityPedestrianVehicleTraffic();
            sampled.AddFootprint(Origin, Quaternion.Euler(0f, 90f, 0f), VehicleBody);
            Assert.That(sampled.Blocks(Origin + Vector3.right * 4f, 0.35f), Is.True,
                "A turn must rotate the long vehicle body into its actual road.");
            Assert.That(sampled.Blocks(Origin + Vector3.forward * 3f, 0.35f), Is.False,
                "The corridor must not inflate to a circle around the vehicle.");
            Assert.That(sampled.Blocks(Origin + Vector3.up * 8f, 0.35f), Is.False,
                "A vehicle below a terrace must not displace pedestrians above it.");
        }

        [Test]
        public void EscapeSegment_LeavesOwnCorridorWithoutCrossingTheNextLane()
        {
            var sampled = new CityPedestrianVehicleTraffic();
            sampled.AddFootprint(Origin, Quaternion.identity, VehicleBody);
            Assert.That(sampled.BlocksEscapeSegment(Origin, Origin + Vector3.right * 2f, 0.35f),
                Is.False, "The enclosing corridor must allow a walker to leave it.");
            sampled.AddFootprint(Origin + Vector3.right * 3f, Quaternion.identity, VehicleBody);
            Assert.That(sampled.Blocks(Origin + Vector3.right * 6f, 0.35f), Is.False);
            Assert.That(sampled.BlocksEscapeSegment(Origin, Origin + Vector3.right * 6f, 0.35f),
                Is.True, "A clear endpoint does not permit crossing another occupied lane.");
            Assert.That(sampled.BlocksEscapeSegment(Origin, Origin + Vector3.left * 2f, 0.35f),
                Is.False, "The opposite verge remains reachable.");
        }

        [Test]
        public void CarSource_UsesDisabledColliderForBodyAndStoppedDrivingIntent()
        {
            root = new GameObject("Disabled Collider Traffic Source");
            root.transform.position = Origin;
            BoxCollider body = root.AddComponent<BoxCollider>();
            body.center = Vector3.up;
            body.size = new Vector3(2f, 2f, 4f);
            body.enabled = false;
            LastRouteCarDriver driver = root.AddComponent<LastRouteCarDriver>();
            var sampled = new CityPedestrianVehicleTraffic(car: driver);
            sampled.Refresh();
            Assert.That(sampled.Blocks(Origin, 0.35f), Is.True,
                "Disabling the driving collider must retain the vehicle body.");

            root.transform.position = Origin + Vector3.right * 8f;
            sampled.Refresh();
            Assert.That(sampled.Blocks(Origin, 0.35f), Is.False);
            Assert.That(sampled.Blocks(root.transform.position, 0.35f), Is.True);

            // Begin only needs a pure road/model; no imported wheels or
            // steering rig is needed to exercise the service's real source.
            driver.Begin(new LastRouteCarDrivePath(new[] { Origin, Origin + Vector3.forward * 30f }),
                LastRouteCarDriveProfile.City);
            sampled.Refresh();
            Assert.That(driver.Speed, Is.Zero);
            Assert.That(sampled.Blocks(Origin + Vector3.forward * 10f, 0.35f), Is.True,
                "A driver stopped for a walker still reserves the approaching road.");
            driver.enabled = false;
            sampled.Refresh();
            Assert.That(sampled.Blocks(Origin, 0.35f), Is.False,
                "A disabled traffic source must release its retained occupancy.");
        }

        [UnityTest]
        public IEnumerator Walker_ClearsVehicleWaitsForTailAndFollowingVehicleThenReturnsBeforeResuming()
        {
            CreateActor(new FlatArea());
            yield return null;
            Vector3 interrupted = actor.Position;
            int previous = actor.PreviousNodeIndex;
            int target = actor.TargetNodeIndex;
            traffic.AddFootprint(Origin + Vector3.back * 3f, Quaternion.identity, VehicleBody);

            for (int i = 0; i < 180 && traffic.Blocks(actor.Position, actor.AgentRadius); i++)
                actor.Advance(Step, vehicleTraffic: traffic);

            Assert.That(actor.IsYieldingToVehicle, Is.True);
            Assert.That(traffic.Blocks(actor.Position, actor.AgentRadius), Is.False,
                "The real walking capsule must leave the vehicle corridor.");
            AssertPlanarDistance(actor.VehicleReturnPosition, interrupted, 0.001f,
                "Yielding must retain the interrupted position.");
            Assert.That(actor.PreviousNodeIndex, Is.EqualTo(previous));
            Assert.That(actor.TargetNodeIndex, Is.EqualTo(target));
            Assert.That(actor.DetourCount, Is.Zero,
                "Waiting for traffic must not invoke the pedestrian jam escape.");

            // Leaving the safety margin can precede reaching the chosen
            // refuge by a fraction of a stride. Let that stride settle.
            for (int i = 0; i < 30; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Vector3 refuge = actor.Position;
            actor.Advance(0f, vehicleTraffic: traffic);
            AssertPlanarDistance(actor.Position, refuge, 0.001f, "A paused yield must remain still.");

            // The centre has passed, but the back of this long body is still
            // beside the saved point. Releasing on the centre would be early.
            traffic.ClearFootprints();
            traffic.AddFootprint(Origin + Vector3.forward * 4f, Quaternion.identity, VehicleBody);
            for (int i = 0; i < 75; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.IsReturningFromVehicle, Is.False);
            AssertPlanarDistance(actor.Position, refuge, 0.04f,
                "The walker must wait until the entire tail clears.");

            traffic.ClearFootprints();
            traffic.AddFootprint(Origin + Vector3.forward * 9f, Quaternion.identity, VehicleBody);
            traffic.AddFootprint(Origin + Vector3.back * 3f, Quaternion.identity, VehicleBody);
            for (int i = 0; i < 75; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.IsReturningFromVehicle, Is.False);
            AssertPlanarDistance(actor.Position, refuge, 0.04f,
                "A second vehicle must retain the same safe wait.");

            traffic.ClearFootprints();
            bool returned = false;
            for (int i = 0; i < 180; i++)
            {
                actor.Advance(Step, vehicleTraffic: traffic);
                if (actor.IsYieldingToVehicle) continue;
                returned = true;
                break;
            }
            Assert.That(returned, Is.True, "The actor must return after traffic clears.");
            AssertPlanarDistance(actor.Position, interrupted, 0.025f,
                "The return must reach its original point before continuing the graph.");
            Assert.That(actor.PreviousNodeIndex, Is.EqualTo(previous));
            Assert.That(actor.TargetNodeIndex, Is.EqualTo(target));
            for (int i = 0; i < 15; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.Position.z, Is.GreaterThan(interrupted.z + 0.2f),
                "The interrupted forward route must continue after the return.");
        }

        [UnityTest]
        public IEnumerator Walker_WithoutWalkableRefugeDoesNotLeaveTheAllowedArea()
        {
            var area = new FlatArea { HalfWidth = 0.45f };
            CreateActor(area);
            yield return null;
            traffic.AddFootprint(Origin, Quaternion.identity,
                new Bounds(Vector3.up * 1.5f, new Vector3(2f, 3f, 40f)));
            Vector3 interrupted = actor.Position;
            for (int i = 0; i < 90; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(area.Contains(actor.Position, actor.AgentRadius), Is.True);
            AssertPlanarDistance(actor.Position, interrupted, 0.05f,
                "A walker without a safe refuge must retain the vehicle stop.");
            Assert.That(actor.DetourCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Walker_DoesNotStepThroughWallsAndPoolResetClearsTheExcursion()
        {
            CreateActor(new FlatArea());
            CreateWall(-0.8f);
            CreateWall(0.8f);
            Physics.SyncTransforms();
            yield return null;
            traffic.AddFootprint(Origin, Quaternion.identity,
                new Bounds(Vector3.up * 1.5f, new Vector3(2f, 3f, 40f)));
            for (int i = 0; i < 90; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(Mathf.Abs(actor.Position.x - Origin.x), Is.LessThan(0.5f),
                "A refuge behind a solid wall is not reachable.");
            Assert.That(traffic.Blocks(actor.Position, actor.AgentRadius), Is.True);

            CityPedestrianPresentation released = actor.ReleasePresentation(root.transform);
            Assert.That(actor.IsYieldingToVehicle, Is.False);
            Assert.That(actor.IsReturningFromVehicle, Is.False);
            actor.PrepareSpawn(plan, plan.SpawnAnchors[0], 1, 1f, 0.91f, 0f, 0, 1u);
            actor.BindPresentation(released);
            traffic.ClearFootprints();
            actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.IsYieldingToVehicle, Is.False);
            Assert.That(actor.MotionState, Is.EqualTo(CityPedestrianMotionState.Walking));
        }

        [UnityTest]
        public IEnumerator Walker_DoorwayTransferRetainsPassengerOwnership()
        {
            CreateActor(new FlatArea());
            yield return null;
            Assert.That(actor.BeginStopApproach(1, Origin, Vector3.forward,
                new[] { 20f, 0f }), Is.True);
            Assert.That(actor.BeginTransfer(true, Origin + Vector3.forward,
                Origin + Vector3.forward * 2f, Quaternion.identity), Is.True);
            traffic.AddFootprint(Origin, Quaternion.identity, VehicleBody);
            for (int i = 0; i < 15; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.IsYieldingToVehicle, Is.False);
            Assert.That(actor.MotionState, Is.EqualTo(CityPedestrianMotionState.Boarding));
            Assert.That(actor.Position.z, Is.GreaterThan(Origin.z + 0.1f));
        }

        [UnityTest]
        public IEnumerator Walker_StopApproachCannotBoardUntilVehicleExcursionReturns()
        {
            CreateActor(new FlatArea());
            yield return null;
            Assert.That(actor.BeginStopApproach(1, Origin + Vector3.forward * 10f,
                Vector3.right, new[] { 20f, 0f }), Is.True);
            traffic.AddFootprint(Origin, Quaternion.identity, VehicleBody);
            actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.IsYieldingToVehicle, Is.True);
            Assert.That(actor.BeginTransfer(true, Origin + Vector3.forward,
                Origin + Vector3.forward * 2f, Quaternion.identity), Is.False,
                "A boarding request must not take a walker away from its saved return point.");
            Assert.That(actor.MotionState, Is.EqualTo(CityPedestrianMotionState.ApproachingStop));
            Assert.That(actor.CollisionEnabled, Is.True);
        }

        [UnityTest]
        public IEnumerator Walker_WaitsAtKerbThenClearsCrossingBeforeBoarding()
        {
            CreateActor(new FlatArea(), atKerb: true);
            yield return null;
            Vector3 kerb = actor.Position;
            Vector3 far = plan.Nodes[1].Position;
            Assert.That(actor.BeginStopApproach(1, far + Vector3.right * .45f, Vector3.left, new[] { 8f, 0f }), Is.True);
            traffic.AddFootprint(Origin, Quaternion.Euler(0f, 90f, 0f), VehicleBody, predicted: true);
            for (int i = 0; i < 40; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.IsWaitingToCross, Is.True);
            Assert.That(actor.IsYieldingToVehicle, Is.False);
            Assert.That(actor.TravelDirection, Is.EqualTo(Vector3.zero), "Stationary passenger must not brake the passing driver.");
            AssertPlanarDistance(actor.Position, kerb, .03f, "Wait on the pavement.");

            traffic.ClearFootprints();
            for (int i = 0; i < 32; i++) actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.IsCrossingRoad, Is.True);
            // A new future sweep does not send a pedestrian back into traffic.
            traffic.AddFootprint(Origin, Quaternion.Euler(0f, 90f, 0f), VehicleBody, predicted: true);
            for (int i = 0; i < 480 && actor.MotionState != CityPedestrianMotionState.WaitingAtStop; i++)
                actor.Advance(Step, vehicleTraffic: traffic);
            Assert.That(actor.MotionState, Is.EqualTo(CityPedestrianMotionState.WaitingAtStop));
            Assert.That(actor.IsCrossingRoad, Is.False);
            AssertPlanarDistance(actor.Position, far + Vector3.right * .45f, .04f, "Finish at the assigned queue on the far side.");
        }

        private void CreateActor(IWalkableArea area, bool atKerb = false)
        {
            root = new GameObject("Pedestrian Vehicle Yield Test");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = Origin + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(50f, 1f, 50f);
            plan = new CityPedestrianPlan(1, 2, 3u, CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode("before", Origin + Vector3.back * (atKerb ? 4f : 10f), atKerb),
                    new CityPedestrianNode("after", Origin + Vector3.forward * (atKerb ? 4f : 10f), atKerb)
                },
                new[] { new CityPedestrianLink("street", 0, 1, CityPedestrianLinkKind.Crosswalk) },
                new[] { new CityPedestrianSpawnAnchor("spawn", atKerb ? Origin + Vector3.back * 4f : Origin, 0, 1) },
                new[] { Rect.MinMaxRect(Origin.x - 20f, Origin.z - 20f,
                    Origin.x + 20f, Origin.z + 20f) });
            CityPedestrianAssetRegistry registry = CityPedestrianDefaultNpcBody.Create(
                root.transform, DefaultNpcPopulation.PedestrianId(0),
                CityPedestrianResources.LoadStreetClipDonor());
            presentation = registry.gameObject.AddComponent<CityPedestrianPresentation>();
            presentation.Initialize(registry);
            registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            GameObject actorObject = new GameObject("Walking Capsule");
            actorObject.transform.SetParent(root.transform, false);
            actorObject.layer = CityPedestrianCollision.LayerIndex;
            actor = actorObject.AddComponent<CityPedestrianActor>();
            actor.Initialize(area, plan.AgentRadius);
            actor.PrepareSpawn(plan, plan.SpawnAnchors[0], 1, 1f, 0.91f, 0f, 0, 1u);
            actor.BindPresentation(presentation);
            traffic = new CityPedestrianVehicleTraffic();
            Physics.SyncTransforms();
        }

        private void CreateWall(float x)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(root.transform, false);
            wall.transform.position = Origin + new Vector3(x, 1.5f, 0f);
            wall.transform.localScale = new Vector3(0.3f, 3f, 20f);
        }

        private static void AssertPlanarDistance(Vector3 actual, Vector3 expected,
            float limit, string message)
        {
            actual.y = expected.y;
            Assert.That(Vector3.Distance(actual, expected), Is.LessThanOrEqualTo(limit), message);
        }
    }
}
