using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_WalkersClearSummitArrivalAndSharedStairExit()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            yield return LoadProductionCity();
            var graph = city.PedestrianPlan;
            var ambient = city.AmbientPedestrians;
            var motor = city.Player.Motor;
            Vector3 hero = motor.transform.position;
            Assert.That(Vector3.Distance(hero, new Vector3(-25.5f, 40.1f, -350f)), Is.LessThan(.12f));
            motor.SetInputEnabled(false);
            Assert.That(ambient.ActiveCount, Is.InRange(1, CityAmbientPedestrians.WarmLimit));
            for (int frame = 0; frame < 24; frame++)
            {
                int total = ambient.CountNear(hero) + city.Pedestrians.Actors.Count(actor => actor.IsSpawned &&
                    !actor.IsAttachedToVehicle && CityAmbientPedestrians.OnSameStretch(hero, actor.Position));
                Assert.That(total, Is.LessThanOrEqualTo(CityAmbientPedestrians.LocalStreetLimit), "Crowd at the opening bridge.");
                yield return null;
            }
            AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityStreetObstacles", "arrival");
            ambient.enabled = false;
            city.Pedestrians.AutomaticUpdatesSuspended = true;
            city.Pedestrians.ReleaseResidentPresentationsForTimeJump();

            var pavement = CityPedestrianPlanner.CreateWalkableArea(graph);
            var walks = new CityAmbientWalkPlan(graph, city.Layout, city.BusPlans);
            var support = CityStreetSupportPlan.ForLayout(city.Layout);
            var traffic = new CityPedestrianVehicleTraffic();
            var movement = new CityAmbientWalking(walks, pavement, support, traffic);
            int street = LinkIndex("hill:quarter:summit:south:court:b:court:a");
            float heroAlong = walks.ProjectDistance(street, hero);
            float start = Mathf.Min(walks.Length(street) - .02f, heroAlong + 2f);
            Vector3 feet = walks.Sample(street, start, out Vector3 axis);
            Assert.That(Vector3.Dot(feet - hero, axis), Is.GreaterThan(.8f));
            var body = motor.GetComponent<CharacterController>();
            var contact = new[] { new CityAmbientWalking.Contact(-1, hero, body.radius) };
            var route = new CityAmbientWalking.Route(street, -1, start);
            Vector3 position = feet;
            float turn = 0f;
            Physics.SyncTransforms();
            for (int frame = 0; frame < 240 && Vector3.Dot(position - hero, axis) > -1.2f; frame++)
            {
                movement.Advance(ref route, 1, position, CityResidentWorld.WalkingSpeed, FrameSeconds,
                    contact, contact.Length, out position, out Vector3 facing);
                turn = Mathf.Max(turn, Vector3.Angle(-axis, facing));
                Assert.That(pavement.Contains(position, CityAmbientWalking.Radius), Is.True);
                Vector2 separation = new Vector2(position.x - hero.x, position.z - hero.z);
                Assert.That(separation.magnitude, Is.GreaterThanOrEqualTo(CityAmbientWalking.Radius + body.radius - .003f));
            }
            Assert.That(Vector3.Dot(position - hero, axis), Is.LessThanOrEqualTo(-1.2f),
                $"Ambient walker stopped at {position:F3}, blocker {route.Blocker}, backed {route.Backing}.");
            Assert.That(turn, Is.InRange(10f, 31f), "Walker turns slightly toward the sidestep.");

            var host = new GameObject("Street obstacle verification");
            var director = host.AddComponent<CityPedestrianDirector>();
            director.AutomaticUpdatesSuspended = true;
            bool residentCleared;
            string residentTrace;
            try
            {
                var owner = new GameObject("Resident passing the hero");
                owner.transform.SetParent(host.transform, false);
                owner.layer = CityPedestrianCollision.LayerIndex;
                var resident = owner.AddComponent<CityPedestrianActor>();
                resident.Initialize(pavement, graph.AgentRadius);
                var visual = CityPedestrianFactory.CreateAmbientPresentation(host.transform, DefaultNpcPopulation.PedestrianId(66));
                director.Initialize(graph, new[] { resident }, new[] { visual }, motor.transform, host.transform, () => false);
                director.ConfigureVehicleTraffic(null, null, null);
                var link = graph.Links[street];
                int destination = graph.GetLinkIndices(link.FirstNodeIndex).Select(index => graph.Links[index])
                    .Where(next => next.Kind == CityPedestrianLinkKind.Sidewalk)
                    .Select(next => next.Other(link.FirstNodeIndex))
                    .First(node => Vector3.Dot(graph.Nodes[node].Position - hero, axis) < -1.2f);
                var anchor = new CityPedestrianSpawnAnchor("start-obstruction", feet, link.SecondNodeIndex, link.FirstNodeIndex);
                resident.PrepareSpawn(graph, anchor, link.FirstNodeIndex, CityResidentWorld.WalkingSpeed, 1f, 0f, 0, 41u);
                resident.BindPresentation(visual);
                resident.SetResidentDestination(destination, city.Pedestrians.Residents.DistancesTo(destination));
                Physics.SyncTransforms();
                // Include the two lateral legs at the slower passing pace.
                float approach = Vector3.Dot(feet - hero, axis);
                int budget = Mathf.CeilToInt(((approach + 1.2f + 2f * 1.65f) /
                    (resident.MovementSpeed * .72f) + 7f) / FrameSeconds);
                for (int frame = 0; frame < budget && Vector3.Dot(resident.Position - hero, axis) > -1.2f; frame++)
                    director.Advance(FrameSeconds);
                residentCleared = Vector3.Dot(resident.Position - hero, axis) <= -1.2f && resident.DetourCount == 0;
                residentTrace = $"Resident at {resident.Position:F3}, detours {resident.DetourCount}, shoulder {resident.LateralOffset:F3}, " +
                    $"state {resident.MotionState}, waiting {resident.IsYielding}, retreat {resident.IsPassingRetreat}, " +
                    $"last step {resident.LastDisplacement:F3}, blocked {resident.BlockedTime:F2}.";
                Debug.Log(residentTrace);
            }
            finally
            {
                director.Shutdown();
                Object.DestroyImmediate(host);
            }

            var stair = CityHillsideAccessPlan.ForLayout(city.Layout).SharedCourt;
            int lower = LinkIndex("hill:quarter:passage:0:walk");
            int upper = LinkIndex("hill:quarter:passage:upper-join");
            var noPeople = System.Array.Empty<CityAmbientWalking.Contact>();
            WalkStair(lower, upper, 1);
            WalkStair(upper, lower, -1);

            Vector3 centre = walks.Sample(street, heroAlong, out _);
            Vector3 roadEdge = centre + Vector3.forward * 1.18f;
            Assert.That(CityPedestrianPassingGround.TryKerb(support, traffic, centre, roadEdge,
                CityAmbientWalking.Radius, false, out Vector3 grounded), Is.True);
            Assert.That(grounded.y, Is.LessThan(centre.y));
            traffic.AddFootprint(grounded, Quaternion.identity, new Bounds(Vector3.up, Vector3.one * 2f), predicted: true);
            Assert.That(CityPedestrianPassingGround.TryKerb(support, traffic, centre, roadEdge,
                CityAmbientWalking.Radius, false, out _), Is.False, "The forecast keeps the walker off the kerb.");
            traffic.ClearFootprints();
            Assert.That(CityPedestrianPassingGround.TryKerb(support, traffic, centre, centre + Vector3.forward * 1.45f,
                CityAmbientWalking.Radius, false, out _), Is.False, "A detour stays beside the sidewalk.");
            var obstruction = new GameObject("Temporary obstruction on both paved shoulders");
            try
            {
                float barrierAt = heroAlong + 2.75f;
                float approachAt = barrierAt + 2f;
                Assert.That(walks.Length(street), Is.GreaterThan(approachAt));
                Vector3 barrier = walks.Sample(street, barrierAt, out _);
                obstruction.transform.position = barrier + new Vector3(0f, .6f, -.10f);
                obstruction.AddComponent<BoxCollider>().size = new Vector3(.4f, 1.2f, 1.76f);
                Physics.SyncTransforms();
                route = new CityAmbientWalking.Route(street, -1, approachAt);
                position = walks.Sample(street, approachAt, out _);
                bool steppedOffKerb = false;
                float shoulder = 0f;
                for (int frame = 0; frame < 260 && Vector3.Dot(position - barrier, axis) > -1.8f; frame++)
                {
                    movement.Advance(ref route, 1, position, CityResidentWorld.WalkingSpeed, FrameSeconds,
                        noPeople, 0, out position, out _);
                    shoulder = Mathf.Max(shoulder, position.z - barrier.z);
                    steppedOffKerb |= position.z > barrier.z + 1.02f;
                }
                Assert.That(steppedOffKerb && Vector3.Dot(position - barrier, axis) <= -1.8f &&
                    pavement.Contains(position, CityAmbientWalking.Radius), Is.True,
                    $"Walker should step around the obstruction and return to pavement. Ended at {position:F3}, " +
                    $"shoulder {shoulder:F3} m, link {walks.LinkId(route.Link)}.");
            }
            finally { Object.DestroyImmediate(obstruction); }
            Assert.That(residentCleared, Is.True, residentTrace);

            int LinkIndex(string id) => Enumerable.Range(0, graph.Links.Count).Single(index => graph.Links[index].Id == id);

            void WalkStair(int first, int last, int direction)
            {
                float at = direction > 0 ? 0f : walks.Length(first);
                var step = new CityAmbientWalking.Route(first, direction, at);
                Vector3 walker = walks.Sample(first, at, out _);
                bool visitedExit = false;
                bool onFlight = false;
                for (int frame = 0; frame < 1700; frame++)
                {
                    movement.Advance(ref step, 1, walker, CityResidentWorld.WalkingSpeed, FrameSeconds,
                        noPeople, 0, out walker, out _);
                    visitedExit |= step.Link == last;
                    onFlight |= walker.y > stair.RouteWorld[0].y + .3f && walker.y < stair.RouteWorld[stair.RouteWorld.Count - 1].y - .3f;
                    Assert.That(pavement.Contains(walker, CityAmbientWalking.Radius), Is.True, $"Stair support at {walker:F3}.");
                    if (visitedExit && !walks.IsStair(step.Link)) break;
                    Assert.That(step.Direction, Is.EqualTo(direction), $"Unexpected turn on {walks.LinkId(step.Link)} at {walker:F3}.");
                }
                Assert.That(visitedExit && onFlight && !walks.IsStair(step.Link), Is.True,
                    $"Walker failed to leave the public staircase at {walker:F3}, {walks.LinkId(step.Link)}.");
                Debug.Log($"Street walker left the stairs at {walker:F3} onto {walks.LinkId(step.Link)}.");
            }
        }

        [UnityTest]
        public IEnumerator LiveCity_SummitResidentWalksSidewalkAndSharedStairsAtFrameCadence()
        {
            yield return LoadProductionCity();
            var graph = city.PedestrianPlan;
            var world = city.Pedestrians.Residents;
            var stair = CityHillsideAccessPlan.ForLayout(city.Layout).SharedCourt;
            int lower = graph.Links.Single(l => l.Id == "hill:quarter:passage:0:walk").FirstNodeIndex;
            int upper = graph.Links.Single(l => l.Id == "hill:quarter:passage:upper-join").SecondNodeIndex;
            var sidewalk = graph.GetLinkIndices(upper).Select(i => graph.Links[i])
                .Where(l => l.Id.StartsWith("hill:quarter:summit:north") && l.Path.Length > 3f)
                .OrderBy(l => l.Path.Length).First();
            int summit = sidewalk.Other(upper);

            var walker = new GameObject("Summit walking regression").AddComponent<CityPedestrianActor>();
            walker.gameObject.layer = CityPedestrianCollision.LayerIndex;
            walker.Initialize(CityPedestrianPlanner.CreateWalkableArea(graph), graph.AgentRadius);
            var model = CityPedestrianDefaultNpcBody.Create(walker.transform, DefaultNpcPopulation.PedestrianId(0),
                CityPedestrianResources.LoadStreetClipDonor());
            var body = model.gameObject.AddComponent<CityPedestrianPresentation>();
            body.Initialize(model);
            var route = world.BuildRoute(lower, summit);
            walker.PrepareSpawn(graph, new CityPedestrianSpawnAnchor("lower-gate", graph.Nodes[lower].Position,
                lower, route.Nodes[1]), route.Nodes[1], CityResidentWorld.WalkingSpeed, 1f, 0f, 0, 41u);
            walker.BindPresentation(body);
            // Exercise static City collision in isolation, without random crowd encounters.
            foreach (var other in city.Pedestrians.Actors)
                if (other.CollisionEnabled) Physics.IgnoreCollision(walker.CharacterController, other.CharacterController);
            Physics.IgnoreCollision(walker.CharacterController, city.Player.Motor.GetComponent<CharacterController>());
            Physics.SyncTransforms();

            WalkTo(summit);
            WalkTo(lower);

            void WalkTo(int destination)
            {
                const float tick = 1f / 60f;
                walker.SetResidentDestination(destination, world.DistancesTo(destination));
                int budget = Mathf.CeilToInt((route.Length * 1.35f / walker.MovementSpeed + 2f) / tick);
                bool onFlight = false;
                for (int frame = 0; frame < budget && !walker.IsAtResidentDestination; frame++)
                {
                    walker.Advance(tick);
                    var feet = walker.Position;
                    if (stair.TrySampleTop(new Vector2(feet.x, feet.z), out float height) &&
                        height > stair.RouteWorld[0].y + .5f && height < stair.RouteWorld[stair.RouteWorld.Count - 1].y - .5f)
                    {
                        onFlight = true;
                        Assert.That(Mathf.Abs(feet.y - height), Is.LessThan(.22f), "Resident lost stair support");
                    }
                    if (walker.DetourCount != 0)
                        Assert.Fail($"Unobstructed resident turned back at {feet:F3} on the way to {graph.Nodes[destination].Id}");
                }
                Assert.That(walker.IsAtResidentDestination, Is.True,
                    $"Resident missed walking pace to {graph.Nodes[destination].Id} at {walker.Position:F3}, target {walker.TargetNodeIndex}");
                Assert.That(onFlight, Is.True, "Resident must physically cross the shared stair flights");
            }
        }

        [UnityTest]
        public IEnumerator LiveCity_SummitHomeFacesTownBesideTheUpperBridge()
        {
            yield return LoadProductionCity();
            var home = city.Layout.PlayerHome;
            var quarter = CityHillsideQuarterPlan.ForLayout(city.Layout);
            var bridge = quarter.Bridges.Single(b => b.Id == "summit");
            var lot = CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings.Single(b => b.Lot.IsPlayerHome);
            Assert.That(lot.OnSummitStreet, Is.True);
            Assert.That(home, Is.SameAs(lot.Lot));
            Vector3 forward = PlayerHomeBalconyGeometry.GetFrontageDirection(home);
            Vector3 eye = home.DoorPosition + forward * (PlayerHomeBalconyGeometry.BalconyDepth - .5f) +
                PlayerHomeBalconyGeometry.GetTangent(home) * PlayerHomeBalconyGeometry.BalconyCenterZ +
                Vector3.up * (PlayerHomeBalconyGeometry.ApartmentFloorElevation + 1.60f);
            Vector3 towardCity = Vector3.ProjectOnPlane(city.Layout.Supermarket.Center - eye, Vector3.up).normalized;
            Assert.That(Vector3.Dot(forward, towardCity), Is.GreaterThan(.64f), "windows face the lower city");
            Vector3 edge = bridge.WestBearingWorld;
            float eyeToEast = Vector3.Distance(eye, bridge.EastBearingWorld);
            if (eyeToEast < Vector3.Distance(eye, edge)) edge = bridge.EastBearingWorld;
            Assert.That(Vector3.Distance(eye, edge), Is.LessThan(22f), "balcony next to a bridge abutment");
            var mapHome = city.Map.ActiveMapPoints.Single(p => p.StableId == "city:home");
            Assert.That(mapHome.WorldPosition, Is.EqualTo(home.Center));
            Assert.That(CityMapArtResources.TryGet(city.Map, out var sheet, out var bounds), Is.True, "The shipped charcoal map must bind to this summit home and pedestrian network.");
            Assert.That(sheet.isReadable, Is.False);
            Assert.That(bounds.Contains(new Vector2(home.Center.x, home.Center.z)), Is.True);
            if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_SUMMIT_HOME") != "1" ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            double toNoon = (2160d - GameSessionState.GameTimeOfDayMinutes) % 1440d;
            GameSessionState.AdvanceGameTime((float)(toNoon / GameTimeState.GameMinutesPerRealSecond));
            city.Player.Motor.Teleport(lot.StreetContactWorld + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            yield return null;
            var camera = city.Camera;
            var pose = new Pose(camera.transform.position, camera.transform.rotation);
            try
            {
                Shot("city-balcony-straight", eye, forward - Vector3.up * .15f);
                Vector3 bridgeBearing = Vector3.ProjectOnPlane(bridge.DeckCenterWorld - eye, Vector3.up).normalized;
                Shot("city-balcony-bridge", eye, (bridgeBearing + towardCity).normalized - Vector3.up * .27f);
                Debug.Log($"Summit balcony {eye:F2}, facade {forward:F2}, bridge {bridge.DeckCenterWorld:F2}, fog {RenderSettings.fogDensity:F4}.");
            }
            finally { camera.transform.SetPositionAndRotation(pose.position, pose.rotation); }
            void Shot(string name, Vector3 at, Vector3 direction)
            {
                camera.transform.SetPositionAndRotation(at, Quaternion.LookRotation(direction));
                AreaCaptureFixture.CaptureCurrentCamera(camera, "SummitHome", name, 1280, 800);
            }
        }
    }
}
