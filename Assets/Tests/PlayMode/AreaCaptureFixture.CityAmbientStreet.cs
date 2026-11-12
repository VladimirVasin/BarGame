using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Timeout(150000)]
        [Explicit("Observe both street populations on a normal walk from the home door.")]
        public IEnumerator CityAmbientStreet()
        {
            float oldDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = .05f;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.PrepareHomeReturn();
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot city = null;
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds * 2f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);
                AssertRidgeEntrySupports(city);
                CaptureReportedHouse(city);
                AssertAndCaptureGlassPaving(city);
                Assert.That(city.AmbientPedestrians.ActiveCount, Is.InRange(1, 6));
                Assert.That(city.AmbientPedestrians.GetComponentsInChildren<Collider>(true), Is.Empty);
                var workers = new HashSet<int>();
                for (int id = 0; id < CityResidentLifeRules.ResidentCount; id++)
                    if (At(id, CityResidentActivity.Work, city.Pedestrians.Residents.Workplace(id).PavementNode))
                        workers.Add(id);
                Assert.That(workers, Is.Not.Empty);
                CityPedestrianPlan plan = city.PedestrianPlan;
                Transform hero = city.Player.GameObject.transform;
                Assert.That(CityBusStopWaitPlanner.TryFindNearestNode(plan, hero.position, out int start), Is.True);
                CityBusStopWaitPoint platform = null;
                float score = float.PositiveInfinity;
                foreach (CityBusStopWaitPlan line in city.LineWaitPlans)
                foreach (CityBusStopWaitPoint stop in line.WaitPoints)
                {
                    if (!stop.IsReachableFrom(start)) continue;
                    float candidate = Mathf.Abs(stop.NodeDistances[start] - 60f);
                    if (candidate >= score) continue;
                    platform = stop;
                    score = candidate;
                }
                Assert.That(platform, Is.Not.Null);
                var route = CityResidentWorld.BuildRoute(plan, start, platform.PedestrianNodeIndex, platform.NodeDistances);
                Debug.Log($"CityAmbientStreet route: {route.Length:F1} m, {platform.StopId}, {city.AmbientPedestrians.ActiveCount} ambient at the door.");
                var points = new List<Vector3> { plan.Nodes[start].Position };
                for (int i = 0; i < route.Links.Length; i++)
                {
                    CityPedestrianLink link = plan.Links[route.Links[i]];
                    Vector3 from = plan.Nodes[route.Nodes[i]].Position;
                    Vector3 to = plan.Nodes[route.Nodes[i + 1]].Position;
                    if (link.Path != null)
                    {
                        int steps = Mathf.CeilToInt(link.Path.Length / 2f);
                        for (int step = 1; step < steps; step++)
                        {
                            float fraction = (float)step / steps;
                            float along = link.FirstNodeIndex == route.Nodes[i] ? fraction : 1f - fraction;
                            Vector2 point = link.Path.SampleDistance(along * link.Path.Length).Position;
                            points.Add(new Vector3(point.x, Mathf.Lerp(from.y, to.y, fraction), point.y));
                        }
                    }
                    points.Add(to);
                }

                var residents = new HashSet<int>();
                var background = new HashSet<int>();
                var women = new HashSet<int>();
                yield return Walk("day");
                AdvanceResidentLifeClockTo(18 * 60);
                points.Reverse();
                yield return Walk("evening");
                int total = residents.Count + background.Count;
                float share = (float)women.Count / total;
                Debug.Log($"CityAmbientStreet combined: permanent={residents.Count}, ambient={background.Count}, women={women.Count}/{total} ({share:P0})");
                Assert.That(background.Count, Is.GreaterThanOrEqualTo(4));
                Assert.That(women.Count, Is.InRange(1, total - 1), "Both men and women are encountered on the walk.");

                AdvanceResidentLifeClockTo(19 * 60 + 55);
                yield return null;
                yield return null;
                workers.RemoveWhere(id => !At(id, CityResidentActivity.Leisure,
                    city.Pedestrians.Residents.Destination(id, CityResidentActivity.Leisure)));
                Assert.That(workers, Is.Not.Empty, "Workers arrived at their own leisure destinations and stayed there.");
                AdvanceResidentLifeClockTo(23 * 60 + 55);
                yield return null;
                yield return null;
                workers.RemoveWhere(id => !At(id, CityResidentActivity.Home, city.Pedestrians.Residents.Home(id).PavementNode));
                Assert.That(workers, Is.Not.Empty, "The same workers reached their homes through the calendar.");

                city.AmbientPedestrians.enabled = false;
                Assert.That(city.AmbientPedestrians.ActiveCount, Is.Zero);
                Assert.That(city.Pedestrians.LogicalResidentCount, Is.EqualTo(CityResidentLifeRules.ResidentCount));
                yield return PassAtReportedHouse(city);

                IEnumerator Walk(string period)
                {
                    int tick = 0;
                    bool captured = false;
                    Vector3 kerb = plan.Nodes[platform.PedestrianNodeIndex].Position;
                    for (int point = 0; point < points.Count; point++)
                    {
                        bool reached = false;
                        for (int step = 0; step < 180 && !reached; step++)
                        {
                            reached = city.Player.Motor.MoveTowardsApproachWaypoint(points[point], .24f, Time.deltaTime);
                            yield return null;
                            if (++tick % 4 != 0) continue;
                            Assert.That(city.Pedestrians.ActiveCount + city.AmbientPedestrians.ActiveCount,
                                Is.LessThanOrEqualTo(CityAmbientPedestrians.MaximumStreetBodies));
                            Assert.That(city.AmbientPedestrians.ActiveCount, Is.LessThanOrEqualTo(12));
                            int visible = 0;
                            foreach (CityPedestrianActor actor in city.Pedestrians.Actors)
                            {
                                if (!actor.IsSpawned || actor.IsAttachedToVehicle || !InView(actor.Position)) continue;
                                int resident = city.Pedestrians.ResidentIndex(actor);
                                if (resident < 0) continue;
                                residents.Add(resident);
                                if (DefaultNpcPopulation.IsWomanPedestrian(resident)) women.Add(resident);
                                visible++;
                            }
                            for (int slot = 0; slot < CityAmbientPedestrians.MaximumActive; slot++)
                            {
                                if (!city.AmbientPedestrians.TryGetWalker(slot, out Vector3 feet, out bool woman, out int encounter))
                                    continue;
                                Assert.That(Vector3.Distance(feet, kerb), Is.GreaterThan(1.25f));
                                if (!InView(feet)) continue;
                                background.Add(encounter);
                                if (woman) women.Add(-encounter);
                                visible++;
                            }
                            if (!captured && visible >= 2)
                            {
                                CaptureCurrentCamera(city.Camera, "CityAmbientStreet", period, 1280, 720);
                                captured = true;
                            }
                        }
                        Assert.That(reached, Is.True, $"The hero can walk to pavement waypoint {point} at {points[point]} from {hero.position}.");
                    }
                    city.Player.Motor.CancelInteractionPoseMove();
                    if (!captured) CaptureCurrentCamera(city.Camera, "CityAmbientStreet", period, 1280, 720);
                }

                bool At(int id, CityResidentActivity activity, int node)
                {
                    CityResidentJourneyState state = city.ResidentJourneys.GetState(id);
                    return state.Phase == CityResidentJourneyPhase.AtDestination &&
                        state.Activity == activity && state.CurrentNode == node;
                }

                bool InView(Vector3 feet)
                {
                    if ((feet - hero.position).sqrMagnitude > 26f * 26f || city.Visibility.CanChangeAmbientBodyAt(feet))
                        return false;
                    Vector3 chest = feet + Vector3.up * 1.1f;
                    Vector3 view = city.Camera.WorldToViewportPoint(chest);
                    return view.z > 0f && view.x > 0f && view.x < 1f && view.y > 0f && view.y < 1f &&
                        !Physics.Linecast(city.Camera.transform.position, chest, CityPedestrianCollision.NonPedestrianMask,
                            QueryTriggerInteraction.Ignore);
                }
            }
            finally { Time.captureDeltaTime = oldDelta; }
        }

        private static IEnumerator PassAtReportedHouse(CityGameRoot city)
        {
            CityPedestrianPlan plan = city.PedestrianPlan;
            CityPedestrianLink apron = null;
            foreach (CityPedestrianLink link in plan.Links)
                if (link.Id == "hill:East:house:22:-5:apron:walk") { apron = link; break; }
            Assert.That(apron, Is.Not.Null, "Reproduce the entrance from the reported block.");
            Assert.That(apron.Kind, Is.EqualTo(CityPedestrianLinkKind.HomeEntrance));
            var ambient = new CityAmbientWalkPlan(plan, city.Layout, city.BusPlans);
            foreach (int link in plan.GetLinkIndices(apron.FirstNodeIndex))
                if (plan.Links[link].Kind == CityPedestrianLinkKind.HomeEntrance)
                    Assert.That(ambient.Length(link), Is.Zero);

            var host = new GameObject("Ridge Entrance Passing Check");
            city.Pedestrians.AutomaticUpdatesSuspended = true;
            city.Pedestrians.ReleaseResidentPresentationsForTimeJump();
            var director = host.AddComponent<CityPedestrianDirector>();
            director.AutomaticUpdatesSuspended = true;
            try
            {
                var pavement = CityPedestrianPlanner.CreateWalkableArea(plan);
                var actors = new CityPedestrianActor[2];
                var bodies = new CityPedestrianPresentation[actors.Length];
                for (int i = 0; i < actors.Length; i++)
                {
                    var owner = new GameObject("Resident " + i);
                    owner.transform.SetParent(host.transform, false);
                    owner.layer = CityPedestrianCollision.LayerIndex;
                    actors[i] = owner.AddComponent<CityPedestrianActor>();
                    actors[i].Initialize(pavement, plan.AgentRadius);
                    bodies[i] = CityPedestrianFactory.CreateAmbientPresentation(host.transform,
                        DefaultNpcPopulation.PedestrianId(i == 0 ? 187 : 66));
                }
                var observer = new GameObject("Entrance Check Observer");
                observer.transform.SetParent(host.transform, false);
                observer.transform.position = plan.Nodes[apron.FirstNodeIndex].Position + Vector3.right * 6f;
                director.Initialize(plan, actors, bodies, observer.transform, host.transform, () => false);
                for (int i = 0; i < actors.Length; i++)
                {
                    Vector2 xz = apron.Path.SampleDistance(apron.Path.Length * (i == 0 ? .18f : .82f)).Position;
                    var feet = new Vector3(xz.x, apron.PathHeightSampler(xz), xz.y);
                    int target = i == 0 ? apron.SecondNodeIndex : apron.FirstNodeIndex;
                    var anchor = new CityPedestrianSpawnAnchor("entrance:" + i, feet, apron.FirstNodeIndex, apron.SecondNodeIndex);
                    actors[i].PrepareSpawn(plan, anchor, target, CityResidentWorld.WalkingSpeed, 1f, 0f, 0, (uint)(i + 1));
                    actors[i].BindPresentation(bodies[i]);
                    actors[i].SetResidentDestination(target, CityBusStopWaitPlanner.CreateNodeDistances(plan, target));
                }
                Physics.SyncTransforms();
                int steps = Mathf.CeilToInt((apron.Path.Length / CityResidentWorld.WalkingSpeed + 6f) / .05f);
                for (int i = 0; i < steps && (!actors[0].IsAtResidentDestination || !actors[1].IsAtResidentDestination); i++)
                    director.Advance(.05f);
                Assert.That(actors[0].IsAtResidentDestination && actors[1].IsAtResidentDestination, Is.True,
                    $"Both residents must clear the real entrance. {Describe(actors[0])}; {Describe(actors[1])}.");
                yield return null;

                string Describe(CityPedestrianActor actor) =>
                    $"{actor.MotionState} {actor.Position:F2} toward {plan.Nodes[actor.ResidentDestinationNode].Position:F2}" +
                    $", last step {actor.LastDisplacement:F3}, blocked {actor.BlockedTime:F2}, detours {actor.DetourCount}";
            }
            finally
            {
                director.Shutdown();
                Object.Destroy(host);
                city.Pedestrians.AutomaticUpdatesSuspended = false;
            }
        }

        private static void CaptureReportedHouse(CityGameRoot city)
        {
            foreach (CityHillsideBuilding house in CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings)
            {
                if (house.Lot.Cell != new Vector2Int(22, -5)) continue;
                Vector3 eye = house.StreetContactWorld + new Vector3(-6f, 2.1f, -3f);
                Vector3 target = new Vector3(house.CourtBounds.center.x, house.ThresholdWorld.y + .7f, house.CourtBounds.center.y);
                CaptureStreetDetail(city, "ridge-entry", eye, target, 62f);
                return;
            }
            Assert.Fail("The reported Ridge entrance must exist in the default layout.");
        }

        private static void CaptureStreetDetail(CityGameRoot city, string name, Vector3 eye, Vector3 target, float fov)
        {
            Transform camera = city.Camera.transform;
            Pose previous = new Pose(camera.position, camera.rotation);
            float previousFov = city.Camera.fieldOfView;
            try
            {
                camera.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                city.Camera.fieldOfView = fov;
                CaptureCurrentCamera(city.Camera, "CityAmbientStreet", name, 1280, 720);
            }
            finally
            {
                camera.SetPositionAndRotation(previous.position, previous.rotation);
                city.Camera.fieldOfView = previousFov;
            }
        }
    }
}
