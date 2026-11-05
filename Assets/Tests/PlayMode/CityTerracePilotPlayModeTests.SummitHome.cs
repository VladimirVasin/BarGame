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
