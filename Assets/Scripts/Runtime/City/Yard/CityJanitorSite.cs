using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Checks a proposed working patch against the same streets and physical floor the hero uses.</summary>
    internal sealed class CityJanitorSite
    {
        internal const float Radius = .75f;
        private readonly CityGameRoot city;
        private readonly CityStreetSupportPlan streets;
        private readonly CityHillsideGroundPlan hillside;
        private readonly Dictionary<Vector2Int, CityCourtyardBlock> courts = new();
        private readonly List<Rect> reservations = new();

        internal CityJanitorSite(CityGameRoot city)
        {
            this.city = city;
            streets = CityStreetSupportPlan.ForLayout(city.Layout);
            hillside = CityHillsideGroundPlan.ForLayout(city.Layout);
            foreach (var court in city.Layout.CourtyardBlocks) courts[court.Cell] = court;
            foreach (var parking in DistrictParkingPlan.ForLayout(city.Layout)) reservations.AddRange(parking.Surfaces);
            foreach (var court in CityResidentialCourtyardPlan.ForLayout(city.Layout).Courts)
            {
                ReservePath(court.ConnectionPath);
                ReservePath(court.CarryRoute);
            }
            foreach (var (line, stop, _, _, _) in CityBusNetworkFurniture.Platforms(city.BusPlans))
            {
                reservations.Add(CityBusPlatformClearance.ShelterReservation(stop.ShelterPosition, stop.Forward, stop.RoadsideForward));
                reservations.Add(CityBusPlatformClearance.PassengerAccess(stop.Position, stop.Forward, stop.ShelterPosition, stop.RoadsideForward));
                reservations.Add(CityBusPlatformClearance.PavementApproach(stop.Position, stop.Forward, stop.RoadsideForward));
                reservations.Add(CityBusStopWaitPlanner.QueueApron(line, stop, city.Layout));
            }
            foreach (var building in CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings)
                reservations.Add(building.ApproachBounds);
            foreach (var entrance in city.Layout.OpenAreaAccesses) reservations.Add(entrance.ApproachBounds);
            foreach (var place in city.Layout.DistrictPointsOfInterest)
            {
                reservations.Add(place.PublicBounds);
                foreach (var access in place.Accesses) reservations.Add(access.ApproachBounds);
            }
        }

        private void ReservePath(IReadOnlyList<Vector3> path)
        {
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                reservations.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x) - .75f, Mathf.Min(a.z, b.z) - .75f,
                    Mathf.Max(a.x, b.x) + .75f, Mathf.Max(a.z, b.z) + .75f));
            }
        }

        internal bool TryGroundHeight(Vector3 candidate, out float top)
        {
            var point = new Vector2(candidate.x, candidate.z);
            return hillside.TrySampleTop(point, out top) ||
                CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, point, out top, out _);
        }

        internal Vector3 SweepFacing(Vector3 feet, Vector3 yardFacing)
        {
            // The brush gathers on the worker's right. At an actual kerb turn the
            // work along it, leaving the worker and the pile inside the tested site.
            for (int ring = 1; ring <= 2; ring++)
            for (int sector = 0; sector < 8; sector++)
            {
                Vector3 outward = Quaternion.AngleAxis(sector * 45f, Vector3.up) * yardFacing;
                Vector3 sample = feet + outward * (ring * 1.5f);
                if (streets.TrySampleTop(new Vector2(sample.x, sample.z), FootstepGroundKind.Concrete, out float road)
                    && Mathf.Abs(road - feet.y) < .40f)
                    return Vector3.Cross(outward, Vector3.up);
            }
            return yardFacing;
        }

        internal bool TryFit(Vector3 proposed, out Vector3 foot, out Vector3 normal)
        {
            foot = proposed;
            normal = Vector3.up;
            if (!city.World.WalkableArea.Contains(proposed, Radius)) return false;
            var point = new Vector2(proposed.x, proposed.z);
            var patch = new Rect(point - Vector2.one * Radius, Vector2.one * (Radius * 2f));
            if (CityBuildingEntrancePlan.IntersectsReservedApproach(city.Layout, patch)) return false;
            foreach (Rect reserve in reservations) if (patch.Overlaps(reserve)) return false;
            foreach (var bench in city.BenchSits)
                if ((bench.Plan.EntryRootPosition - proposed).sqrMagnitude < 2.1f * 2.1f) return false;
            foreach (var path in city.Layout.CourtyardPaths)
                if (path.Bounds.Overlaps(new Rect(point - Vector2.one * 1.6f, Vector2.one * 3.2f), true) &&
                    path.Project(point).DistanceSquared < 1.6f * 1.6f) return false;
            if (!TryPaving(proposed, out var ground)) return false;
            foot = ground.point;
            normal = ground.normal;
            // All four edges must continue the same hard ground. Reject kerbs, steps and raised props.
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = foot + new Vector3(x * Radius, 0f, z * Radius);
                if (!TryPaving(corner, out var edge) || Mathf.Abs(Vector3.Dot(edge.point - foot, normal)) > .035f)
                    return false;
            }
            return !Physics.CheckBox(foot + Vector3.up * 1.03f, new Vector3(Radius, .86f, Radius),
                Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private bool TryPaving(Vector3 p, out RaycastHit hit)
        {
            if (!Physics.Raycast(p + Vector3.up * .46f, Vector3.down, out hit, .92f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < .985f) return false;
            var marker = hit.collider.GetComponentInParent<FootstepGround>();
            var kind = marker != null ? marker.ResolveKind(hit.triangleIndex) : FootstepGroundKind.None;
            if (kind != FootstepGroundKind.Concrete && kind != FootstepGroundKind.Stone && kind != FootstepGroundKind.Tile &&
                !(kind == FootstepGroundKind.Soil && OnCourtyardEarth(hit)))
                return false;
            // Asphalt has the concrete footstep sound. Use its road geometry to keep all five samples off it.
            return !streets.TrySampleTop(new Vector2(p.x, p.z), FootstepGroundKind.Concrete, out float road) ||
                Mathf.Abs(road - hit.point.y) > .40f;
        }

        private bool OnCourtyardEarth(RaycastHit hit)
        {
            Vector3 foot = hit.point;
            var cell = Vector2Int.FloorToInt(city.Layout.GetWorldGridCoordinate(foot));
            if (!courts.TryGetValue(cell, out var court)) return false;
            var point = new Vector2(foot.x, foot.z);
            var surface = hit.collider.GetComponentInParent<CityGroundSurfaceSystem>();
            if (surface == null || surface.CanGrowGrassAt(point)) return false;
            foreach (var polygon in court.GroundPolygons)
                if (CityRoadPolygon.Contains(polygon, point)) return true;
            return false;
        }
    }
}
