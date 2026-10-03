using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Replanned district streets through the production camera, light and fog.")]
        public IEnumerator CityReplanning()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime((float)(360d / GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            var issues = new List<string>();
            yield return Capture(SceneIds.City,
                () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.Layout != null && city.World != null &&
                        city.BusPlan != null ? city : null;
                }, () => CityReplanningShots(city, issues));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        private static Shot[] CityReplanningShots(CityGameRoot city, List<string> issues)
        {
            CityLayout layout = city.Layout;
            Assert.That(layout.SpatialPlan.IsUniform, Is.False);
            LogReplanningRouteLengths(city);
            var shots = new List<Shot>();
            CityStreetSurfacePlan streetPlan = CityStreetSurfacePlanner.Create(layout);
            RoadWalkableArea pedestrianArea = CityPedestrianPlanner.CreateWalkableArea(city.PedestrianPlan);
            for (int index = 0; index < layout.RoadGeometry.CurvedEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadGeometry.CurvedEdges[index];
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                for (float s = 6.5f; s < path.Length - 6f; s += 1f)
                {
                    CityRoadSample sample = path.SampleDistance(s);
                    float datum = layout.ElevationPlan.SampleRoadDatum(edge, s / path.Length);
                    foreach (float offset in new[] { 0f, -3.5f, 3.5f })
                    {
                        Vector2 point = sample.Position + sample.Right * offset;
                        if (offset != 0 && streetPlan.CurvedSidewalkRibbons.Any(ribbon =>
                            ribbon.Edge.Equals(edge) && NearPavementEnd(ribbon, point))) continue;
                        Vector3 probe = new Vector3(point.x, datum + 2f, point.y);
                        Assert.That(Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 4f,
                            ~0, QueryTriggerInteraction.Ignore), Is.True, "Curved paving must have a physical surface.");
                        bool pavement = offset != 0 && streetPlan.CurvedSidewalkPolygons.Any(polygon =>
                            CityRoadPolygon.Contains(polygon, point));
                        float top = datum + (pavement ? CityStreetSurfacePlanner.SidewalkTop : CityStreetSurfacePlanner.RoadTop);
                        if (Mathf.Abs(hit.point.y - top) > .025f)
                            issues.Add($"Paving {edge.A}->{edge.B} s={s:F2} offset={offset:F1} point={point:F4} pavement={pavement} expected={top:F4} actual={hit.point.y:F4} collider={hit.collider.name}");
                        if (!city.World.WalkableArea.Contains(hit.point, .35f))
                            issues.Add($"Hero navigation at {point:F4}");
                        if (pavement && !pedestrianArea.Contains(hit.point, .35f))
                            issues.Add($"Pedestrian navigation at {point:F4}");
                    }
                }
                CityRoadSample eyeSample = path.SampleDistance(path.Length * .3f);
                Vector3 eye = ReplanningStreetEye(layout, new Vector3(eyeSample.Position.x, 0, eyeSample.Position.y));
                CityRoadSample targetSample = path.SampleDistance(Mathf.Min(path.Length - 2f, path.Length * .3f + 16f));
                shots.Add(Shot.At($"replanning-pilot-{index + 1:00}-curve", eye,
                    new Vector3(targetSample.Position.x, eye.y - .7f, targetSample.Position.y), 78f));
            }
            foreach (CityPedestrianLink link in city.PedestrianPlan.Links.Where(link => link.Path != null))
                for (float distance = .5f; distance < link.Path.Length; distance += 1f)
                {
                    Vector2 point = link.Path.SampleDistance(distance).Position;
                    float top = link.PathHeightSampler(point);
                    Vector3 probe = new Vector3(point.x, top + .5f, point.y);
                    RaycastHit[] hits = Physics.RaycastAll(probe, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore);
                    if (!hits.Any(hit => hit.collider is MeshCollider && Mathf.Abs(hit.point.y - top) < .025f))
                        issues.Add($"Pedestrian height {link.Id} at {point:F4}, expected={top:F4}, hits={string.Join(",", hits.Select(hit => hit.collider.name + ":" + hit.point.y.ToString("F4")))}");
                }
            RoadEdge branch = new RoadEdge(new Vector2Int(1, 8), new Vector2Int(2, 8));
            CityRoadPath branchPath = layout.RoadGeometry.Get(branch);
            CityRoadSample middle = branchPath.SampleDistance(branchPath.Length * .5f);
            Vector2 released = middle.Position - middle.Right * 5.2f;
            float branchDatum = layout.ElevationPlan.SampleRoadDatum(branch, .5f);
            Vector3 releasedProbe = new Vector3(released.x, branchDatum + 2f, released.y);
            Assert.That(Physics.Raycast(releasedProbe, Vector3.down, out RaycastHit releasedGround, 4f,
                ~0, QueryTriggerInteraction.Ignore), Is.True, "Released straight-road strip must be filled with ground.");
            Assert.That(releasedGround.point.y, Is.LessThan(branchDatum + .04f), "The obsolete straight road must not survive below the new bend.");
            Assert.That(city.World.WalkableArea.Contains(releasedGround.point, .35f), Is.True);
            Vector3 junction = layout.GetNodeWorldPosition(new Vector2Int(1, 8));
            Vector3 junctionEye = ReplanningStreetEye(layout, junction + Vector3.right * 5f);
            shots.Add(Shot.At("replanning-pilot-04-t-junction", junctionEye,
                junction + Vector3.left * 6f + Vector3.up * (EyeHeight - .6f), 102f));
            Debug.Log($"OldTown pilot: {layout.RoadGeometry.CurvedEdges.Count} shared road paths; physical probe issues={issues.Count}.");
            return shots.ToArray();
        }

        private static bool NearPavementEnd(CityStreetRibbonDescriptor ribbon, Vector2 point)
        {
            foreach (Vector2[] polygon in ribbon.Polygons)
                foreach (int side in new[] { 0, 2 })
                {
                    // Cross-sections are legitimate material/height boundaries;
                    // raycasts on their millimetre tolerance band are ambiguous.
                    Vector2 a = polygon[side], delta = polygon[(side + 1) % polygon.Length] - a;
                    Vector2 nearest = a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
                    if ((point - nearest).sqrMagnitude < .000004f) return true;
                }
            return false;
        }

        private static void LogReplanningRouteLengths(CityGameRoot city)
        {
            Assert.That(city.World.FringeYardPlan.HasTunnelForecourt, Is.True);
            Assert.That(city.World.MountainBoundaryPlan.HasTunnel, Is.True);
            LastRouteCarPlan car = LastRouteCarPlan.Create(city.Layout);
            Assert.That(car.IsPresent, Is.True);
            CityTunnelTravelPlan tunnel = CityTunnelTravelPlanner.Create(
                city.World.MountainBoundaryPlan.Tunnel);
            CityTunnelForecourtDescriptor forecourt = city.World.FringeYardPlan.TunnelForecourt;
            LastRouteCarDrivePath departure = LastRouteCityDrivePlanner.CreateDeparture(
                car, city.Layout, forecourt, tunnel.FloorSurfaceY);
            LastRouteCarDrivePath arrival = LastRouteCityDrivePlanner.CreateReturn(
                car, city.Layout, forecourt, tunnel.FloorSurfaceY);
            float blackout = LastRouteCityDrivePlanner.TunnelBlackoutDepth;
            Debug.Log($"City replanning routes: departureTotal={departure.Length:F3} m; " +
                $"cityToPortal={departure.Length - blackout:F3} m; tunnel={blackout:F3} m; " +
                $"returnTotal={arrival.Length:F3} m; busLoop={city.BusPlan.LoopLength:F3} m; " +
                $"busStops={city.BusPlan.Stops.Count}.");
        }

        private static Vector3 ReplanningStreetEye(CityLayout layout, Vector3 position)
        {
            if (layout.ElevationPlan.TrySampleSurface(new Vector2(position.x, position.z),
                CitySurfaceRole.RoadTop, out float top, out _)) position.y = top;
            position.y += EyeHeight;
            return position;
        }
    }
}
