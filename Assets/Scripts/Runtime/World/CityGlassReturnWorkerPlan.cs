using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One assigned residence and the surveyed same-footway trip from the staff door.</summary>
    public sealed class CityGlassReturnWorkerPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityGlassReturnWorkerPlan> Plans = new();
        private static readonly Vector2Int HomeCell = new(22, -7);
        public CityHillsideBuilding House { get; }
        public string HomeStableId { get; }
        public Vector3 HomeDoor { get; }
        public Vector3 InsideHome { get; }
        public IReadOnlyList<Vector3> WorkToDoor { get; } = Array.Empty<Vector3>();
        public IReadOnlyList<Vector3> WorkToSwitch { get; } = Array.Empty<Vector3>();
        public IReadOnlyList<Vector3> SwitchToDoor { get; } = Array.Empty<Vector3>();
        public Quaternion SwitchFacing { get; }
        public IReadOnlyList<Vector3> DoorCrossing { get; } = Array.Empty<Vector3>();
        public IReadOnlyList<Vector3> DoorToHome { get; } = Array.Empty<Vector3>();

        public static CityGlassReturnWorkerPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityGlassReturnWorkerPlan(value));

        private CityGlassReturnWorkerPlan(CityLayout layout)
        {
            var site = CityGlassReturnSitePlan.ForLayout(layout);
            if (!site.IsEnabled) return;
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings)
                if (house.Bank == CityTerraceTransportBank.East && house.Lot.Cell == HomeCell && !house.Lot.IsPlayerHome)
                { House = house; break; }
            if (House == null) throw new InvalidOperationException("The glass sorter's assigned east Ridge house is missing.");
            HomeStableId = $"ridge-east-house:{HomeCell.x}:{HomeCell.y}";
            HomeDoor = House.ThresholdWorld;
            InsideHome = HomeDoor - House.Lot.FacadeForward * (CityBuildingAssetProvider.EntranceDepth - .45f);
            // Feet remain clear of the rear shelving and the sorting crate.
            Vector3 inside = Local(1.65f, -.48f), outside = Earth(Local(3f, -.48f));
            Vector3 switchStand = Local(1.64f, .27f);
            WorkToSwitch = Array.AsReadOnly(new[] { Local(0f, .89f), Local(.95f, .40f), switchStand });
            SwitchToDoor = Array.AsReadOnly(new[] { switchStand, inside });
            SwitchFacing = Quaternion.LookRotation(site.Rotation * Vector3.right);
            WorkToDoor = Array.AsReadOnly(new[] { Local(0f, .89f), Local(.95f, .40f), switchStand, inside });
            DoorCrossing = Array.AsReadOnly(new[] { inside, Local(2.28f, -.48f), outside });
            CityHillsideStreetRoute road = null;
            var path = new List<Vector3>(48) { outside };
            // Turn around the front corner, rather than diagonally through the side wall.
            GroundLeg(Local(2.75f, 2.15f));
            GroundLeg(site.Approach);
            foreach (var candidate in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if (candidate.Bank == CityTerraceTransportBank.East) { road = candidate; break; }
            if (road == null) throw new InvalidOperationException("The glass sorter's east Ridge footway is missing.");
            var walk = road.SidewalkPath;
            var home = Project(walk, House.StreetContactWorld);
            var other = Project(road.OppositeSidewalkPath, House.StreetContactWorld);
            if (other.distance < home.distance) { walk = road.OppositeSidewalkPath; home = other; }
            if (home.distance > .20f)
                throw new InvalidOperationException("The glass sorter's home no longer fronts its assigned footway.");
            var entry = Project(walk, site.Approach);
            GroundLeg(entry.point);
            int direction = entry.segment < home.segment ? 1 : -1;
            for (int i = entry.segment + (direction > 0 ? 1 : 0);
                direction > 0 ? i <= home.segment : i > home.segment; i += direction) path.Add(walk[i]);
            path.Add(home.point);
            GroundLeg(House.DoorWorld);
            path.Add(HomeDoor);
            path.Add(InsideHome);
            DoorToHome = path.AsReadOnly();

            Vector3 Local(float x, float z) => site.Origin + site.Rotation * new Vector3(x, 0f, z);
            Vector3 Earth(Vector3 point)
            {
                Vector2 xz = XZ(point);
                if (!ground.TrySampleTop(xz, out float y))
                    throw new InvalidOperationException("The glass sorter's footway lost its ground at " + point);
                return new Vector3(point.x, y, point.z);
            }
            void GroundLeg(Vector3 end)
            {
                Vector3 start = path[path.Count - 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / .45f));
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 p = Vector3.Lerp(start, end, i / (float)steps);
                    path.Add(road != null && road.TrySampleTop(XZ(p), out float top, out _) ?
                        new Vector3(p.x, top, p.z) : Earth(p));
                }
            }
        }

        private static (int segment, Vector3 point, float distance) Project(IReadOnlyList<Vector3> path, Vector3 target)
        {
            int segment = 0;
            Vector3 nearest = path[0];
            float best = float.PositiveInfinity;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector2 delta = XZ(path[i + 1]) - XZ(path[i]);
                float fraction = Vector2.Dot(XZ(target) - XZ(path[i]), delta) / Mathf.Max(.000001f, delta.sqrMagnitude);
                Vector3 point = Vector3.Lerp(path[i], path[i + 1], fraction);
                float distance = (XZ(point) - XZ(target)).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; segment = i; nearest = point;
            }
            return (segment, nearest, Mathf.Sqrt(best));
        }
        private static Vector2 XZ(Vector3 point) => new(point.x, point.z);
    }
}
