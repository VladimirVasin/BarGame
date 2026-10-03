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
            CityRoadJunction oblique = layout.RoadGeometry.ObliqueJunction;
            Assert.That(oblique, Is.Not.Null);
            foreach (CityRoadPath pavement in oblique.SidewalkPaths)
                for (float distance = .1f; distance < pavement.Length; distance += .5f)
                {
                    Vector2 point = pavement.SampleDistance(distance).Position;
                    float top = layout.ElevationPlan.GetNodeElevation(oblique.Node) + CityStreetSurfacePlanner.SidewalkTop;
                    Vector3 probe = new Vector3(point.x, top + .5f, point.y);
                    if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore) ||
                        Mathf.Abs(hit.point.y - top) > .025f)
                        issues.Add($"Oblique junction pavement at {point:F4}, expected={top:F4}");
                    Vector3 standing = new Vector3(point.x, top, point.y);
                    if (!city.World.WalkableArea.Contains(standing, .35f)) issues.Add($"Oblique hero navigation at {point:F4}");
                    if (!pedestrianArea.Contains(standing, .35f)) issues.Add($"Oblique pedestrian navigation at {point:F4}");
                }
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
                shots.Add(Shot.At($"replanning-courtyard-street-{index + 1:00}-curve", eye,
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
            shots.Add(Shot.At("replanning-courtyard-street-04-t-junction", junctionEye,
                junction + Vector3.left * 6f + Vector3.up * (EyeHeight - .6f), 102f));
            CityRoadSample branchEye = branchPath.SampleDistance(11f);
            Vector3 obliqueEye = ReplanningStreetEye(layout,
                new Vector3(branchEye.Position.x, 0f, branchEye.Position.y));
            shots.Add(Shot.At("replanning-courtyard-street-05-approach", obliqueEye,
                junction + Vector3.forward * 4f + Vector3.up * (EyeHeight - .6f), 90f));
            VerifyReplanningCourtyards(city, streetPlan, shots, issues);
            Debug.Log($"OldTown pilot: {layout.RoadGeometry.CurvedEdges.Count} shared road paths; physical probe issues={issues.Count}.");
            return shots.ToArray();
        }

        private static void VerifyReplanningCourtyards(CityGameRoot city,
            CityStreetSurfacePlan streetPlan, List<Shot> shots, List<string> issues)
        {
            CityLayout layout = city.Layout;
            Assert.That(layout.CourtyardBlocks.Count, Is.EqualTo(4));
            Assert.That(layout.BuildingMasses.Count, Is.EqualTo(146));
            var mapGround = new CityMapCityTeleportGround(layout);
            CharacterController hero = city.Player.GameObject.GetComponent<CharacterController>();
            Physics.SyncTransforms();
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
            {
                for (float distance = 0f; distance < block.Route.Length + .5f; distance += .5f)
                {
                    Vector2 point = block.Route.SampleDistance(distance).Position;
                    if (!TryReplanningSurfaceTop(layout, streetPlan, point, out float top))
                    {
                        issues.Add($"Courtyard {block.Cell} has no authored walking surface at {point:F4}.");
                        continue;
                    }
                    Vector3 floor = new Vector3(point.x, top, point.y);
                    if (!Physics.Raycast(floor + Vector3.up * 1.5f, Vector3.down,
                        out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                        Mathf.Abs(hit.point.y - top) > .025f || hit.normal.y < .7f)
                        issues.Add($"Courtyard {block.Cell} physical floor at {point:F4}, expected={top:F4}, " +
                            $"actual={hit.point.y:F4}, collider={hit.collider?.name}.");
                    if (!city.World.WalkableArea.Contains(floor, .35f))
                        issues.Add($"Courtyard {block.Cell} hero capsule leaves navigation at {point:F4}.");
                    // Keep the full .35 m radius. The production step offset
                    // permits the existing curb while testing body clearance.
                    Collider[] obstacles = Physics.OverlapCapsule(
                        floor + Vector3.up * (hero.stepOffset + .35f + PlayerFactory.GroundedRootOffset),
                        floor + Vector3.up * (hero.height - .35f + PlayerFactory.GroundedRootOffset), .35f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    foreach (Collider obstacle in obstacles)
                    {
                        if (obstacle.transform.IsChildOf(city.Player.GameObject.transform) ||
                            obstacle.GetComponentInParent<DefaultNpcAppearance>() != null ||
                            obstacle.GetComponentInParent<CityPedestrianActor>() != null) continue;
                        issues.Add($"Courtyard {block.Cell} route capsule blocked at {point:F4} by {obstacle.name}.");
                    }
                }
                foreach (Vector3 arrival in new[] { block.CourtCenter, block.PassageCenter })
                {
                    Vector2 point = new Vector2(arrival.x, arrival.z);
                    if (!mapGround.TryResolveStandingPosition(point, out Vector3 standing) ||
                        (new Vector2(standing.x, standing.z) - point).sqrMagnitude > .0001f ||
                        !TryReplanningSurfaceTop(layout, streetPlan, point, out float top) ||
                        Mathf.Abs(standing.y - top - PlayerFactory.GroundedRootOffset) > .025f)
                        issues.Add($"Courtyard {block.Cell} map arrival moved or missed the actual ground at {point:F4}.");
                }
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(block.Primary);
                Vector3 courtEye = ReplanningCourtyardEye(layout, streetPlan,
                    new Vector2(block.CourtCenter.x, block.CourtCenter.z));
                Vector3 courtTarget = pose.TransformPoint(new Vector3(9f, 1.15f, -2.5f));
                if (block.RearBuilding != null)
                {
                    Vector3 flank = pose.TransformPoint(new Vector3(8.1f, 0f, -1.5f));
                    courtEye = ReplanningCourtyardEye(layout, streetPlan, new Vector2(flank.x, flank.z));
                    courtTarget = block.CourtCenter + Vector3.up * 1.15f;
                }
                shots.Add(Shot.At($"replanning-courtyard-{block.Cell.x}-{block.Cell.y}-court", courtEye,
                    courtTarget, 88f));
                if (block.RearBuilding != null)
                {
                    float passageDistance = block.Route.Project(new Vector2(block.PassageCenter.x,
                        block.PassageCenter.z)).DistanceAlong;
                    CityRoadSample eye = block.Route.SampleDistance(Mathf.Max(0f, passageDistance - 2f));
                    CityRoadSample target = block.Route.SampleDistance(Mathf.Min(block.Route.Length, passageDistance + 4f));
                    Vector3 passageEye = ReplanningCourtyardEye(layout, streetPlan, eye.Position);
                    shots.Add(Shot.At($"replanning-courtyard-{block.Cell.x}-{block.Cell.y}-passage", passageEye,
                        new Vector3(target.Position.x, passageEye.y - .65f, target.Position.y), 78f));
                }
                if (block.Cell == new Vector2Int(0, 8))
                {
                    Vector3 entranceEye = ReplanningCourtyardEye(layout, streetPlan, block.Route.Vertices[0]);
                    CityRoadSample target = block.Route.SampleDistance(6f);
                    shots.Add(Shot.At("replanning-courtyard-0-8-entrance", entranceEye,
                        new Vector3(target.Position.x, entranceEye.y - .55f, target.Position.y), 82f));
                }
            }
        }

        private static bool TryReplanningSurfaceTop(CityLayout layout, CityStreetSurfacePlan streetPlan,
            Vector2 point, out float top)
        {
            if (streetPlan.CurvedSidewalkPolygons.Any(polygon => CityRoadPolygon.Contains(polygon, point)) ||
                (layout.RoadGeometry.ObliqueJunction != null && layout.RoadGeometry.ObliqueJunction.SidewalkPolygons
                    .Any(polygon => CityRoadPolygon.Contains(polygon, point))))
                return layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.SidewalkTop, out top, out _);
            var position = new Vector3(point.x, 0f, point.y);
            foreach (RuntimeOrientedBox sidewalk in streetPlan.SidewalkGeometry)
                if (sidewalk.TrySampleTop(position, out top)) return true;
            if (CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out top, out _)) return true;
            return layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.RoadTop, out top, out _);
        }

        private static Vector3 ReplanningCourtyardEye(CityLayout layout, CityStreetSurfacePlan streetPlan, Vector2 point)
        {
            Assert.That(TryReplanningSurfaceTop(layout, streetPlan, point, out float top), Is.True);
            return new Vector3(point.x, top + EyeHeight, point.y);
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
