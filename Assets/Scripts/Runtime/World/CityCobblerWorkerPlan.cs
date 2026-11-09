using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The cobbler's recessed entrance, inner footway and two front steps form one reversible trip.</summary>
    public sealed class CityCobblerWorkerPlan
    {
        public const string ResidenceId = "ridge-east-house:21:-6";
        public CityHillsideBuilding House { get; }
        public Vector3 InsideHome { get; }
        public Vector3 HomeDoor => House.ThresholdWorld;
        public IReadOnlyList<Vector3> HomeToWork => commute.Points;
        internal Path Commute => commute;
        internal float DoorApproachDistance { get; }
        private readonly Path commute;

        internal CityCobblerWorkerPlan(CityCobblerSitePlan site, Transform shell)
        {
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(site.Layout).Buildings)
                if (house.Bank == CityTerraceTransportBank.East && house.Lot.Cell == new Vector2Int(21, -6) &&
                    !house.Lot.IsPlayerHome) { House = house; break; }
            if (House == null) throw new InvalidOperationException("The cobbler's east Ridge house (21,-6) is missing.");
            InsideHome = HomeDoor - House.Lot.FacadeForward * (CityBuildingAssetProvider.EntranceDepth - .45f);
            CityHillsideStreetRoute street = null;
            foreach (var candidate in CityHillsideStreetPlan.ForLayout(site.Layout).Routes)
                if (candidate.Bank == CityTerraceTransportBank.East) { street = candidate; break; }
            if (street == null) throw new InvalidOperationException("The cobbler's east Ridge footway is missing.");

            var ground = CityHillsideGroundPlan.ForLayout(site.Layout);
            var points = new List<Vector3>(40) { InsideHome, HomeDoor, House.DoorWorld };
            var walk = street.SidewalkPath;
            var start = Project(walk, House.StreetContactWorld);
            var other = Project(street.OppositeSidewalkPath, House.StreetContactWorld);
            if (other.error < start.error) { walk = street.OppositeSidewalkPath; start = other; }
            if (start.error > .20f) throw new InvalidOperationException("The cobbler's doorstep lost its inner sidewalk.");
            GroundLeg(start.point);
            var end = Project(walk, site.Approach);
            if (start.segment < end.segment)
                for (int i = start.segment + 1; i <= end.segment; i++) points.Add(walk[i]);
            else
                for (int i = start.segment; i > end.segment; i--) points.Add(walk[i]);
            points.Add(end.point);
            GroundLeg(site.Approach);
            Vector3 outside = Anchor(shell, "ANCHOR_EntryOutside").position;
            points.Add(outside);
            int firstStep = points.Count;
            foreach (string name in new[] { "ANCHOR_Step1", "ANCHOR_Step2", "ANCHOR_Threshold", "ANCHOR_EntryInside" })
                points.Add(Anchor(shell, name).position);
            points.Add(site.ToWorld(new Vector3(.25f, .48f, 2.62f)));
            points.Add(site.ToWorld(new Vector3(-.47f, .48f, 2.62f)));
            commute = new Path(points);
            DoorApproachDistance = Mathf.Max(0f, commute.Distances[firstStep - 1] - 1.65f);

            void GroundLeg(Vector3 goal)
            {
                Vector3 origin = points[points.Count - 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(origin, goal) / .45f));
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 point = Vector3.Lerp(origin, goal, (float)i / steps);
                    Vector2 xz = XZ(point);
                    if (street.TrySampleTop(xz, out float top, out var kind))
                    {
                        if (kind != CityStreetSupportKind.Sidewalk)
                            throw new InvalidOperationException("The cobbler's commute crossed the carriageway at " + xz);
                    }
                    else if (!ground.TrySampleTop(xz, out top))
                        throw new InvalidOperationException("The cobbler's walking route lacks ground at " + xz);
                    point.y = top;
                    points.Add(point);
                }
            }
        }

        internal static Transform Anchor(Transform shell, string name) =>
            CityPedestrianHandProps.FindSocket(shell, name) ??
            throw new InvalidOperationException("The cobbler's bus is missing " + name);

        private static Vector2 XZ(Vector3 point) => new(point.x, point.z);

        private static (int segment, Vector3 point, float error) Project(IReadOnlyList<Vector3> path, Vector3 target)
        {
            int best = 0;
            Vector3 nearest = path[0];
            float error = float.PositiveInfinity;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector2 d = XZ(path[i + 1] - path[i]);
                float t = Vector2.Dot(XZ(target - path[i]), d) / Mathf.Max(.000001f, d.sqrMagnitude);
                Vector3 p = Vector3.Lerp(path[i], path[i + 1], t);
                float sqr = XZ(p - target).sqrMagnitude;
                if (sqr >= error) continue;
                error = sqr; best = i; nearest = p;
            }
            return (best, nearest, Mathf.Sqrt(error));
        }

        internal sealed class Path
        {
            internal readonly IReadOnlyList<Vector3> Points;
            internal readonly float[] Distances;
            internal float Length => Distances[Distances.Length - 1];

            internal Path(List<Vector3> points)
            {
                Points = points.AsReadOnly();
                Distances = new float[points.Count];
                for (int i = 1; i < points.Count; i++)
                    Distances[i] = Distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            }

            internal Vector3 Sample(float distance, out Vector3 direction)
            {
                int index = Array.BinarySearch(Distances, Mathf.Clamp(distance, 0f, Length));
                int next = Mathf.Clamp(index >= 0 ? index + 1 : ~index, 1, Points.Count - 1);
                while (next < Points.Count - 1 && Distances[next] - Distances[next - 1] < .0001f) next++;
                direction = Vector3.ProjectOnPlane(Points[next] - Points[next - 1], Vector3.up).normalized;
                return Vector3.Lerp(Points[next - 1], Points[next], Mathf.InverseLerp(Distances[next - 1], Distances[next], distance));
            }
        }
    }
}
