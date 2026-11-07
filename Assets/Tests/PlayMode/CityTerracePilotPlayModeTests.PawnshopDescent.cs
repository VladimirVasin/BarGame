using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PawnshopDescentCaptureSetup : IPrebuildSetup
    {
        public const string CaptureEnvironment = "BAR_PROMENADE_CAPTURE_PAWNSHOP_DESCENT";

        public void Setup()
        {
#if UNITY_EDITOR
            Type exporter = Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true);
            if (Environment.GetEnvironmentVariable(CaptureEnvironment) == "1")
                exporter.GetMethod("FocusGameViewForCapture", Type.EmptyTypes).Invoke(null, null);
            exporter.GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, PrebuildSetup(typeof(PawnshopDescentCaptureSetup)), Timeout(240000)]
        public IEnumerator LiveCity_PawnshopDescentWalksBothWaysWithSupportedStepsRailsAndMapArrivals()
        {
            bool capture = Environment.GetEnvironmentVariable(PawnshopDescentCaptureSetup.CaptureEnvironment) == "1";
            int previousInterval = OnDemandRendering.renderFrameInterval;
            PlayerCameraFollow follow = null;
            PlayerMotor motor = null;
            try
            {
                if (capture) Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                    "Descent review needs the rendered production Game view.");
                yield return LoadProductionCity();
                var plan = CityPawnshopDescentPlan.ForLayout(city.Layout);
                Assert.That(plan.IsEnabled, Is.True);
                Assert.That(CityPawnshopDescentPlan.BenchId, Is.EqualTo(CityHillsideFurniturePlan.HomeBenchId));
                Assert.That(plan.Flights.Count, Is.EqualTo(2));
                Assert.That(plan.Landings.Count, Is.EqualTo(3));
                Assert.That(plan.Landings[1].Length, Is.GreaterThanOrEqualTo(2.4f - .001f));
                Assert.That(plan.UpperDock.y - plan.LowerDock.y, Is.InRange(3.7f, 4.3f));
                Transform world = city.World.Root.GetComponentsInChildren<Transform>()
                    .SingleOrDefault(part => part.name == "Pawnshop hillside descent");
                Assert.That(world, Is.Not.Null);
                var bench = city.GetComponentsInChildren<CityBenchSitInteraction>()
                    .Single(seat => seat.Plan.Id == CityPawnshopDescentPlan.BenchId);
                Assert.That(Vector3.Distance(plan.BenchDock + Vector3.up * PlayerFactory.GroundedRootOffset,
                    bench.Plan.EntryRootPosition), Is.LessThan(.01f), "The original permanent bench retains its exit dock.");
                Assert.That(CityPawnshopPlan.TryCreate(city.Layout, out var pawnshop), Is.True);
                var crossing = CityPawnshopCrosswalkPlan.ForLayout(city.Layout).Crossings.Single(item => item.Lane == 5);
                foreach (Vector3 crossingPoint in crossing.WalkPath)
                    Assert.That(plan.PawnshopRoute.Any(point => Vector3.Distance(point, crossingPoint) < .02f), Is.True,
                        "The descent must reach the shop by the existing zebra, rather than cutting the road bend.");
                Assert.That(Vector3.Distance(plan.PawnshopRoute[0], plan.BenchDock), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(plan.PawnshopRoute[plan.PawnshopRoute.Count - 1] +
                    Vector3.up * PlayerFactory.GroundedRootOffset, pawnshop.ReturnWorld), Is.LessThan(.03f));
                Assert.That(plan.RouteWorld.Any(point => Vector3.Distance(point, plan.LowerDock) < .02f), Is.True,
                    "The lower apron must join the existing outer public footway.");

                motor = city.Player.Motor;
                var capsule = motor.GetComponent<CharacterController>();
                var visual = (Player3DCharacterPresentation)city.Player.Visual;
                isolatedHeroCapsule = capsule;
                motor.SetInputEnabled(false);
                city.Player.Interactor.SetInputEnabled(false);
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                follow.SetOrbitInputEnabled(false);
                OnDemandRendering.renderFrameInterval = 24;
                Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                Assert.That(GameSessionState.TryPrepareGameTimeAt(720), Is.True);
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();
                Assert.That(city.Camera.GetUniversalAdditionalCameraData().renderPostProcessing, Is.True);
                Assert.That(RenderSettings.fog, Is.True, "The physical route retains the City's atmospheric depth.");
                Physics.SyncTransforms();

                var mapGround = new CityMapCityTeleportGround(city.Layout);
                AssertDescentGeometry(plan, world, motor.transform);
                foreach (var landing in plan.Landings)
                    AssertDescentArrival(landing.Center);
                AssertDescentArrival(plan.UpperDock);
                AssertDescentArrival(plan.LowerDock);
                var mapPoint = city.Map.PointsOfInterest.Single(point => point.StableId == "pawnshop-descent");
                Assert.That(mapPoint.Kind, Is.EqualTo(CityMapPointOfInterestKind.PawnshopDescent));
                Assert.That(mapPoint.HasFootprint, Is.False);
                Assert.That(Vector3.Distance(mapPoint.WorldPosition,
                    plan.UpperDock + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.01f));
                int pointIndex = city.Map.PointsOfInterest.ToList().FindIndex(point => point.StableId == "pawnshop-descent");
                Assert.That(city.Map.GetPointOfInterestLabel(pointIndex), Is.EqualTo("Спуск к ломбарду"));
                string geography = CityMapArtResources.CreateFingerprint(city.Map);
                Assert.That(geography, Does.Match("^[a-fA-F0-9]{64}$"));
                TextAsset metadataAsset = Resources.Load<TextAsset>(CityMapArtResources.ResourcePath);
                Assert.That(metadataAsset, Is.Not.Null);
                var metadata = JsonUtility.FromJson<CityMapArtResources.Metadata>(metadataAsset.text);
                bool hasArt = CityMapArtResources.TryGet(city.Map, out _, out _);
                if (hasArt) Assert.That(metadata.fingerprint, Is.EqualTo(geography), "Art must describe the current stairs.");
                if (metadata.fingerprint != geography) Assert.That(hasArt, Is.False, "Stale art must fall back to the live scheme.");
                foreach (Vector2[] footprint in plan.Footprints)
                    foreach (Vector2 point in footprint)
                        Assert.That(city.Map.DisplayWorldXZBounds.Contains(point), Is.True);
                Debug.Log($"PAWNSHOP DESCENT bench={plan.BenchDock:F3} upper={plan.UpperDock:F3} " +
                    $"lower={plan.LowerDock:F3} flights={plan.Flights.Count} route={plan.PawnshopRoute.Count} " +
                    $"mapFingerprint={geography} artCurrent={hasArt}");

                // One initial placement selects the existing bench. The entire
                // descent, public footway, zebra, door and return are walked.
                motor.Teleport(plan.BenchDock + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 5; frame++) yield return null;
                if (capture)
                {
                    Vector3 eye = new Vector3(-33.6f, 35.7f, -334.9f) + Vector3.up * 1.55f;
                    Vector3 target = plan.Landings[1].Center + Vector3.up * .3f;
                    follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 60f);
                    yield return DescentPresentationAt(720, "01-upper-bench-day");
                    yield return DescentPresentationAt(0, "02-upper-bench-night");
                    Vector3 center = (plan.UpperDock + plan.LowerDock) * .5f;
                    Vector3 side = Vector3.Cross(Vector3.up, plan.UpperDock - plan.LowerDock).normalized;
                    eye = center + side * 9f + Vector3.up * 3.2f;
                    follow.SetFixedPose(eye, Quaternion.LookRotation(center + Vector3.up * .4f - eye), 60f);
                    yield return DescentPresentationAt(720, "03-stair-side-profile-day");
                    yield return DescentPresentationAt(0, "04-stair-side-profile-night");
                }
                yield return WalkDescent(plan.PawnshopRoute, false, "bench-to-pawnshop");
                Assert.That(Vector3.Distance(motor.transform.position, pawnshop.ReturnWorld), Is.LessThan(.12f));
                yield return WalkDescent(plan.PawnshopRoute, true, "pawnshop-to-bench");
                Assert.That(Vector3.Distance(motor.transform.position,
                    bench.Plan.EntryRootPosition), Is.LessThan(.12f));

                void AssertDescentArrival(Vector3 floor)
                {
                    Vector3 requested = floor + Vector3.up * PlayerFactory.GroundedRootOffset;
                    Assert.That(mapGround.TryClampArrival(requested, out Vector3 arrival), Is.True);
                    Assert.That(Vector3.Distance(arrival, requested), Is.LessThan(.035f),
                        "The map must select the new physical surface, not the old earth below it: " + floor);
                    Assert.That(city.World.WalkableArea.Contains(arrival, capsule.radius), Is.True);
                }

                IEnumerator WalkDescent(IReadOnlyList<Vector3> route, bool reverse, string stage)
                {
                    for (int corner = 0; corner < route.Count; corner++)
                    {
                        int index = reverse ? route.Count - corner - 1 : corner;
                        Vector3 target = route[index] + Vector3.up * PlayerFactory.GroundedRootOffset;
                        bool arrived = false;
                        int limit = Mathf.Max(100, Mathf.CeilToInt(Vector3.Distance(motor.transform.position, target) /
                            (2.6f * Time.captureDeltaTime)) + 100);
                        for (int frame = 0; frame < limit && !arrived; frame++)
                        {
                            IsolateAmbientCollisionPairs();
                            Vector3 before = motor.transform.position;
                            arrived = motor.MoveTowardsApproachWaypoint(target, .04f, Time.captureDeltaTime);
                            yield return null;
                            Vector3 current = motor.transform.position;
                            Assert.That(Vector3.Distance(before, current), Is.LessThan(.35f), stage + " teleported.");
                            Assert.That(city.World.WalkableArea.Contains(current, capsule.radius), Is.True,
                                stage + " left the clear route at " + current);
                            if (plan.ContainsFootprint(new Vector2(current.x, current.z)))
                            {
                                Assert.That(plan.TrySampleTop(new Vector2(current.x, current.z), out float top), Is.True);
                                Assert.That(current.y - top, Is.InRange(-.06f, .28f), stage + " lost capsule support.");
                            }
                            Assert.That(visual.LeftFootGround.HasSurface || visual.RightFootGround.HasSurface, Is.True,
                                stage + " lost both production boot probes.");
                            AssertFootNearActor(visual.LeftFootGround, current.y, stage + " left foot");
                            AssertFootNearActor(visual.RightFootGround, current.y, stage + " right foot");
                        }
                        if (!arrived) Debug.Log($"PAWNSHOP DESCENT BLOCKED stage={stage} station={index} " +
                            $"at={motor.transform.position:F3} target={target:F3} flags={capsule.collisionFlags}");
                        Assert.That(arrived, Is.True, $"{stage} blocked at {motor.transform.position:F3}, target {target:F3}.");
                        Assert.That(motor.transform.position.y, Is.EqualTo(target.y).Within(.20f), stage + " wrong storey.");
                    }
                    motor.CancelInteractionPoseMove();
                }

                IEnumerator DescentPresentationAt(int minute, string shot)
                {
                    int savedMinute = GameSessionState.GameMinuteOfDay;
                    int interval = OnDemandRendering.renderFrameInterval;
                    Assert.That(GameSessionState.TryPrepareGameTimeAt(minute), Is.True);
                    OnDemandRendering.renderFrameInterval = 1;
                    try
                    {
                        city.DayNight.ApplyCurrentTime(true);
                        for (int frame = 0; frame < 3; frame++) yield return null;
                        city.Night.Atmosphere.RefreshImmediate();
                        string path = Path.GetFullPath(Path.Combine("Captures", "PawnshopDescent", shot + ".png"));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                        ScreenCapture.CaptureScreenshot(path);
                        float deadline = Time.realtimeSinceStartup + 8f;
                        while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previous || new FileInfo(path).Length < 1000) &&
                            Time.realtimeSinceStartup < deadline) yield return null;
                        Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous && new FileInfo(path).Length >= 1000,
                            Is.True, "The actual Game view did not finish the descent frame: " + shot);
                    }
                    finally
                    {
                        Assert.That(GameSessionState.TryPrepareGameTimeAt(savedMinute), Is.True);
                        city.DayNight.ApplyCurrentTime(true);
                        city.Night.Atmosphere.RefreshImmediate();
                        OnDemandRendering.renderFrameInterval = interval;
                    }
                }
            }
            finally
            {
                if (motor != null) motor.CancelInteractionPoseMove();
                if (follow != null) follow.ClearFixedPose();
                OnDemandRendering.renderFrameInterval = previousInterval;
            }
        }

        private static void AssertDescentGeometry(CityPawnshopDescentPlan plan, Transform world, Transform hero)
        {
            foreach (CityExteriorStairFlightDescriptor flight in plan.Flights)
            {
                Assert.That(flight.StepCount, Is.EqualTo(12));
                Assert.That(flight.StepRise, Is.InRange(.15f, .18f));
                Assert.That(flight.TreadDepth, Is.GreaterThanOrEqualTo(.28f));
                Assert.That(flight.Width, Is.GreaterThanOrEqualTo(1.8f));
                Transform model = world.Find(flight.Id);
                Assert.That(model, Is.Not.Null, "An imported fixed-metre flight must provide visible treads and rails.");
                Transform lower = model.GetComponentsInChildren<Transform>().Single(part => part.name == "ANCHOR_Lower");
                Transform upper = model.GetComponentsInChildren<Transform>().Single(part => part.name == "ANCHOR_Upper");
                Assert.That(Vector3.Distance(lower.position, flight.Start), Is.LessThan(.015f));
                Assert.That(Vector3.Distance(upper.position, flight.End), Is.LessThan(.015f));
                Assert.That(model.GetComponentsInChildren<MeshRenderer>().Any(renderer => renderer.enabled &&
                    renderer.GetComponent<MeshFilter>()?.sharedMesh != null), Is.True);
                for (int step = 0; step < flight.StepCount; step++)
                {
                    float run = (step + .5f) * flight.TreadDepth;
                    Vector3 center = flight.Start + flight.Direction * run;
                    float rampY = flight.Start.y + (step + .5f) * flight.StepRise;
                    float treadY = flight.Start.y + (step + 1f) * flight.StepRise;
                    foreach (float across in new[] { -.55f, 0f, .55f })
                    {
                        Vector3 ray = center + flight.Right * across + Vector3.up * (treadY - center.y + .5f);
                        var hits = Physics.RaycastAll(ray, Vector3.down, 1.5f, Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Collide).Where(hit => !hit.transform.IsChildOf(hero)).ToArray();
                        Assert.That(hits.Any(hit => FootProbeSurface.IsProbeSurface(hit.collider) &&
                            hit.transform.IsChildOf(world) && Mathf.Abs(hit.point.y - treadY) < .015f), Is.True,
                            "Both feet need the authored visible tread at " + ray);
                        var solids = hits.Where(hit => !hit.collider.isTrigger).ToArray();
                        Assert.That(solids, Is.Not.Empty);
                        float physicalTop = solids.Max(hit => hit.point.y);
                        Assert.That(physicalTop, Is.EqualTo(rampY).Within(.045f),
                            "Native earth or another physical skin blocks the new ramp at " + ray);
                    }
                }
                Vector3 railOrigin = (flight.Start + flight.End) * .5f + Vector3.up * .70f;
                foreach (int side in new[] { -1, 1 })
                    Assert.That(Physics.RaycastAll(railOrigin, flight.Right * side, 1.35f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(hit =>
                            hit.transform.IsChildOf(world) && hit.distance > .65f && hit.normal.y < .5f), Is.True,
                        "A physical outer guard must protect each flight side.");
            }
        }
    }
}
