using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class IndustrialWorkplaceAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CityBuildingAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        [PrebuildSetup(typeof(IndustrialWorkplaceAssetsSetup))]
        public IEnumerator LiveCity_IndustrialShiftsUseEveryBuilding()
        {
            var fractional = new CityResidentWorkday(480, 0, 59.00401203610833d, 60d);
            CityResidentLifeSample before = CityResidentLifeRules.EvaluateWorkday(0, 0, 0, 1, fractional);
            CityResidentLifeSample departure = CityResidentLifeRules.EvaluateWorkday(0, 0, before.EndsAt, 1, fractional);
            Assert.That(departure.Activity, Is.EqualTo(CityResidentActivity.Work));
            Assert.That(departure.StartedAt, Is.EqualTo(before.EndsAt));

            int seed = GameSessionState.DefaultCitySeed;
            CityLayout layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default, CityGenerationSettings.Default, seed);
            var workplaces = CityBuildingEntrancePlan.ForLayout(layout).Workplaces;
            Assert.That(workplaces, Has.Count.EqualTo(63));
            Assert.That(workplaces.Select(site => site.Lot), Is.EquivalentTo(layout.BuildingMasses.Where(lot => lot.HasFunctionalIndustrialPurpose)));
            Assert.That(workplaces.Count(site => site.WorkplaceId.EndsWith(":rear", StringComparison.Ordinal)), Is.EqualTo(1));
            var plan = CityLayoutCache.GetOrCreatePedestrianPlan(layout, seed);
            var renewed = new CityResidentWorld(plan, layout);

            bool capture = Environment.GetEnvironmentVariable("BAR_PROMENADE_WORKPLACE_SURVEY") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
#if UNITY_EDITOR
            if (capture)
                Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
            Assert.That(GameSessionState.TryStartGameTimeAt(7 * 60), Is.True);
            yield return LoadProductionCity();
            CityResidentWorld residents = city.Pedestrians.Residents;
            CityPedestrianPlan pavement = city.PedestrianPlan;
            RoadWalkableArea area = CityPedestrianPlanner.CreateWalkableArea(pavement);
            var attendance = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int id = 0; id < CityResidentLifeRules.ResidentCount; id++)
            {
                CityBuildingEntranceDescriptor work = residents.WorkEntrance(id);
                Assert.That(work, Is.Not.Null);
                Assert.That(renewed.WorkEntrance(id).WorkplaceId, Is.EqualTo(work.WorkplaceId));
                Assert.That(renewed.Home(id).Building.Cell, Is.EqualTo(residents.Home(id).Building.Cell));
                CityResidentWorkday day = residents.Workday(id).Value;
                Assert.That(day.Departure + day.ExpectedTripMinutes, Is.LessThan(day.ShiftStart));
                Assert.That(day.Release, Is.GreaterThanOrEqualTo(day.ShiftEnd));
                if (!attendance.TryGetValue(work.WorkplaceId, out List<int> assigned))
                    attendance.Add(work.WorkplaceId, assigned = new List<int>(4));
                assigned.Add(id);
            }
            Assert.That(attendance.Count, Is.EqualTo(63));
            Assert.That(attendance.Values.Count(group => group.Count == 3), Is.EqualTo(60));
            Assert.That(attendance.Values.Count(group => group.Count == 4), Is.EqualTo(3));
            Assert.That(attendance.Values.All(group => group.Select(id => residents.Workday(id).Value.ShiftStart).Distinct().Count() == 1),
                Is.True);
            city.Pedestrians.enabled = false;
            if (city.AmbientPedestrians != null) city.AmbientPedestrians.enabled = false;
            var clock = Object.FindAnyObjectByType<GameTimeRuntime>();
            if (clock != null) clock.enabled = false;
            for (int shift = 7; shift <= 9; shift++)
            {
                yield return AdvanceCalendar(shift * 60);
                int[] group = Enumerable.Range(0, CityResidentLifeRules.ResidentCount)
                    .Where(id => residents.Workday(id).Value.ShiftStart == shift * 60).ToArray();
                int onTime = group.Count(city.ResidentJourneys.IsWorking);
                Debug.Log($"Industrial attendance at {shift}:00: {onTime}/{group.Length}.");
            }
            yield return AdvanceCalendar(12 * 60);
            int atWork = Enumerable.Range(0, CityResidentLifeRules.ResidentCount).Count(city.ResidentJourneys.IsWorking);
            Debug.Log($"Industrial attendance at noon after real bus service replay: {atWork}/{CityResidentLifeRules.ResidentCount}.");
            Assert.That(atWork, Is.EqualTo(CityResidentLifeRules.ResidentCount));

            Collider[] probes = new Collider[32];
            var failures = new List<string>();
            Physics.SyncTransforms();
            foreach (CityBuildingEntranceDescriptor site in workplaces)
            {
                int resident = attendance[site.WorkplaceId][0];
                CityResidentWorld.Address address = residents.Workplace(resident);
                CityResidentWorld.Route walk = residents.BuildRoute(address.FrontageNode, address.PavementNode);
                Assert.That(walk.Nodes[walk.Nodes.Length - 1], Is.EqualTo(address.PavementNode), site.WorkplaceId);
                Assert.That(walk.Links.All(index => pavement.Links[index].Kind == CityPedestrianLinkKind.WorkEntrance), Is.True, site.WorkplaceId);
                Assert.That(Vector3.Distance(pavement.Nodes[address.PavementNode].Position, site.Interior), Is.LessThan(.002f));
                SurveyWorkplace(site, walk, pavement, area, probes, failures);
            }
            if (capture) CaptureWorkplaces(workplaces);

            var rear = workplaces.Single(site => site.WorkplaceId.EndsWith(":rear", StringComparison.Ordinal));
            int worker = attendance[rear.WorkplaceId][0];
            if (failures.Count > 0) Debug.Log(string.Join("\n", failures));
            VerifyWorkerTraversesDoor(residents, pavement, area, worker);
            VerifyWorkerTraversesDoor(residents, pavement, area, attendance["work:1:3:main"][0]);
            VerifyWorkAttendance(renewed, worker);
            Assert.That(failures, Is.Empty, string.Join("\n", failures));

            IEnumerator AdvanceCalendar(double target)
            {
                double advance = target + .01d - GameSessionState.GameTimeOfDayMinutes;
                if (advance > 0d)
                    GameSessionState.AdvanceGameTime((float)(advance / GameTimeState.GameMinutesPerRealSecond));
                yield return null;
                yield return null;
            }
        }

        private void CaptureWorkplaces(IReadOnlyList<CityBuildingEntranceDescriptor> workplaces)
        {
            PlayerCameraFollow follow = city.Camera.GetComponent<PlayerCameraFollow>();
            if (follow != null) follow.enabled = false;
            foreach (CityBuildingEntranceDescriptor site in workplaces)
            {
                bool rear = site.WorkplaceId.EndsWith(":rear", StringComparison.Ordinal);
                Vector3 setback = site.Outward * (rear ? 1.8f : 4.8f);
                Vector3 side = Vector3.Cross(Vector3.up, site.Outward) * .35f;
                Vector3 eye = site.Threshold + setback + side + Vector3.up * 1.65f;
                city.Camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation(site.Threshold + Vector3.up * 1.7f - eye));
                city.Camera.fieldOfView = 76f;
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "IndustrialWorkplaces",
                    site.WorkplaceId.Replace(':', '-'), 768, 432);
            }
        }

        private static void SurveyWorkplace(CityBuildingEntranceDescriptor site, CityResidentWorld.Route walk,
            CityPedestrianPlan pavement, RoadWalkableArea area, Collider[] probes, List<string> failures)
        {
            for (int leg = 0; leg < walk.Links.Length; leg++)
            {
                CityPedestrianLink link = pavement.Links[walk.Links[leg]];
                int stations = Mathf.CeilToInt(link.Path.Length / .25f);
                for (int i = 0; i <= stations; i++)
                {
                    Vector2 point = link.Path.SampleDistance(link.Path.Length * i / stations).Position;
                    Vector3 feet = new Vector3(point.x, link.PathHeightSampler(point), point.y);
                    bool supported = Physics.Raycast(feet + Vector3.up * .3f, Vector3.down, out RaycastHit floor, .6f,
                        CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore);
                    bool traversable = area.Contains(feet, pavement.AgentRadius);
                    if (!supported || Mathf.Abs(floor.point.y - feet.y) > .15f || !traversable)
                    {
                        string hit = supported ? $"{floor.collider.name} ({floor.point.y - feet.y:+0.000;-0.000;0})" : "none";
                        failures.Add($"{site.WorkplaceId}: {link.Id} at {feet:F3}, walkable {traversable}, floor {hit}.");
                        break;
                    }
                    float radius = pavement.AgentRadius;
                    Vector3 lower = floor.point + Vector3.up * (radius + .06f);
                    Vector3 upper = floor.point + Vector3.up * (CityPedestrianActor.CollisionHeight - radius);
                    int overlaps = Physics.OverlapCapsuleNonAlloc(lower, upper, radius, probes,
                        CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore);
                    Assert.That(overlaps, Is.LessThan(probes.Length), $"Probe overflow at {feet:F3}.");
                    for (int hit = 0; hit < overlaps; hit++)
                    {
                        if (probes[hit].gameObject == floor.collider.gameObject) continue;
                        failures.Add($"{site.WorkplaceId}: {probes[hit].name} blocks {link.Id} at {feet:F3}.");
                        return;
                    }
                }
            }
        }

        private static void VerifyWorkerTraversesDoor(CityResidentWorld residents, CityPedestrianPlan pavement,
            RoadWalkableArea area, int worker)
        {
            var root = new GameObject("Industrial doorway worker");
            root.layer = CityPedestrianCollision.LayerIndex;
            try
            {
                var actor = root.AddComponent<CityPedestrianActor>();
                actor.Initialize(area, pavement.AgentRadius);
                var work = residents.Workplace(worker);
                CityResidentWorld.Route approach = residents.BuildRoute(work.FrontageNode, work.PavementNode);
                var start = new CityPedestrianSpawnAnchor("industrial-inbound",
                    pavement.Nodes[approach.Nodes[0]].Position, approach.Nodes[0], approach.Nodes[1]);
                actor.PrepareSpawn(pavement, start, approach.Nodes[1], CityResidentWorld.WalkingSpeed, 1f, 0f, 0, 1u);
                CityPedestrianAssetRegistry registry = CityPedestrianDefaultNpcBody.Create(root.transform,
                    DefaultNpcPopulation.PedestrianId(worker), CityPedestrianResources.LoadStreetClipDonor());
                var body = registry.gameObject.AddComponent<CityPedestrianPresentation>();
                body.Initialize(registry);
                actor.BindPresentation(body);
                Physics.SyncTransforms();
                WalkTo(work.PavementNode);
                Assert.That(Vector3.Distance(actor.Position, residents.WorkEntrance(worker).Interior), Is.LessThan(.14f));
                WalkTo(work.FrontageNode);

                void WalkTo(int target)
                {
                    actor.SetResidentDestination(target, residents.DistancesTo(target));
                    for (int step = 0; step < 1600 && !actor.IsAtResidentDestination; step++)
                        actor.Advance(.05f);
                    Assert.That(actor.IsAtResidentDestination, Is.True,
                        $"NPC capsule stopped at {actor.Position:F3} before {pavement.Nodes[target].Position:F3}.");
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void VerifyWorkAttendance(CityResidentWorld world, int resident)
        {
            var journeys = new CityResidentJourneyDirector(world, Array.Empty<CityBusPlan>(), Array.Empty<CityBusStopWaitPlan>());
            CityResidentWorkday day = world.Workday(resident).Value;
            double midnight = CityResidentLifeRules.MinutesPerDay;
            journeys.Reset(midnight + day.Departure + .001d);
            journeys.Advance(midnight + day.ShiftStart - .01d);
            Assert.That(journeys.GetState(resident).CurrentNode, Is.EqualTo(world.Workplace(resident).PavementNode));
            Assert.That(journeys.IsWorking(resident), Is.False, "An early arrival has entered and is waiting for the shift.");
            journeys.Advance(midnight + day.ShiftStart);
            Assert.That(journeys.IsWorking(resident), Is.True);
            CityResidentJourneyDirector.Snapshot checkpoint = journeys.Capture();
            journeys.Advance(midnight + day.ShiftEnd - .01d);
            Assert.That(journeys.IsWorking(resident), Is.True);
            journeys.Advance(midnight + day.Release);
            Assert.That(journeys.IsLeavingWork(resident), Is.True);
            journeys.Restore(checkpoint);
            Assert.That(journeys.IsWorking(resident), Is.True);

            journeys.Reset(midnight + day.Departure + .001d);
            journeys.SetStreetPresented(resident, true);
            journeys.Advance(midnight + day.ShiftStart + 5d);
            Assert.That(journeys.IsWorking(resident), Is.False, "A delayed visible worker cannot start from the street.");
            Assert.That(journeys.MarkWalkArrived(resident), Is.True);
            Assert.That(journeys.IsWorking(resident), Is.True);
            journeys.Advance(midnight + day.ShiftEnd);
            Assert.That(journeys.IsWorking(resident), Is.False, "Late arrival must not move the end of the shift.");
        }
    }
}
