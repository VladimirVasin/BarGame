using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Measured furniture outside the workshop, with the public footway and glass kept open.</summary>
    public sealed class CityCobblerYardPlan
    {
        public const string SeatId = "ridge-cobbler-fitting-seat";
        public readonly struct Placement
        {
            public readonly string Part;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            internal Placement(string part, Vector3 position, float yaw)
            {
                Part = part;
                Position = position;
                Rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        public IReadOnlyList<Placement> Placements { get; }
        public IReadOnlyList<Vector2[]> SolidPolygons { get; }
        public Vector3 PathStart { get; }
        public Vector3 PathEnd { get; }
        public Vector3 BenchPathEnd { get; }
        public CityBenchSeat Seat { get; }
        public Vector3 CableStart { get; }
        public Vector3 CableEnd { get; }

        internal CityCobblerYardPlan(CityCobblerSitePlan site)
        {
            var ground = CityHillsideGroundPlan.ForLayout(site.Layout);
            var placements = new List<Placement>(7);
            var solids = new List<Vector2[]>();
            Place("SeatBench", new Vector2(69.4f, -302.9f), 235f);
            var bench = placements[0];
            Vector3 forward = bench.Rotation * Vector3.forward;
            Vector3 right = bench.Rotation * Vector3.right;
            Vector3 footrest = bench.Position + right * .95f + forward * .43f;
            Place("Footrest", XZ(footrest), 235f);
            Vector3 scraper = site.ToWorld(new Vector3(2.36f, 0f, 3.36f));
            Place("BootScraper", XZ(scraper), 250f);
            Place("KerbLong", new Vector2(67.3f, -311.99f), 69f);
            Place("KerbShort", new Vector2(68.75f, -312.42f), 80f);
            Place("Lilac", new Vector2(71f, -312f), 37f);
            Place("OldBusWheels", new Vector2(69.5f, -310.65f), 180f);
            Placements = placements.AsReadOnly();
            SolidPolygons = solids.AsReadOnly();

            // Match the worker's nearest point on the inner sidewalk, rather than
            // inventing a shortcut across the carriageway or the middle of the bend.
            float nearest = float.PositiveInfinity;
            foreach (var road in CityHillsideStreetPlan.ForLayout(site.Layout).Routes)
            {
                if (road.Bank != CityTerraceTransportBank.East) continue;
                foreach (var path in new[] { road.SidewalkPath, road.OppositeSidewalkPath })
                for (int i = 1; i < path.Count; i++)
                {
                    Vector3 delta = Vector3.ProjectOnPlane(path[i] - path[i - 1], Vector3.up);
                    float t = Mathf.Clamp01(Vector3.Dot(site.Approach - path[i - 1], delta) / delta.sqrMagnitude);
                    Vector3 point = Vector3.Lerp(path[i - 1], path[i], t);
                    float distance = (XZ(point) - XZ(site.Approach)).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    PathStart = point;
                }
            }
            if (float.IsPositiveInfinity(nearest)) throw new InvalidOperationException("Missing cobbler yard footway.");
            PathStart = Ground(Vector3.MoveTowards(PathStart, site.Approach, 1.1f));
            PathEnd = site.Approach;
            BenchPathEnd = Ground(bench.Position + forward * .84f);
            Vector3 dock = bench.Position + forward * (.46f * .5f + CityBenchSitPlan.EntryEdgeDistance);
            Seat = new CityBenchSeat(SeatId, bench.Position + Vector3.up * .48f,
                1.22f, .46f, Ground(dock).y, forward);

            CableEnd = site.ToWorld(new Vector3(0f, 2.66f, -4.08f));
            CityHillsideBuilding house = null;
            foreach (var candidate in CityHillsideNeighborhoodPlan.ForLayout(site.Layout).Buildings)
                if (candidate.Bank == CityTerraceTransportBank.East && candidate.Lot.Cell == new Vector2Int(21, -6))
                    house = candidate;
            if (house == null) throw new InvalidOperationException("Missing cobbler yard supply wall.");
            CableStart = new Vector3(house.FoundationBounds.xMin - .06f, CableEnd.y, CableEnd.z);

            Vector3 Ground(Vector3 p)
            {
                if (!ground.TrySampleTop(XZ(p), out float top))
                    throw new InvalidOperationException("Missing cobbler yard ground at " + p);
                return new Vector3(p.x, top, p.z);
            }
            void Place(string part, Vector2 point, float yaw)
            {
                var placement = new Placement(part, Ground(new Vector3(point.x, 0f, point.y)), yaw);
                placements.Add(placement);
                foreach (var box in CityCobblerYardWorld.Geometry.Part(part).boxes)
                {
                    Bounds bounds = box.MetricBounds;
                    var min = bounds.min; var max = bounds.max;
                    solids.Add(CityRoadPolygon.CounterClockwise(new[] { Corner(min.x, min.z), Corner(max.x, min.z),
                        Corner(max.x, max.z), Corner(min.x, max.z) }));
                }
                Vector2 Corner(float x, float z) => XZ(placement.Position + placement.Rotation * new Vector3(x, 0f, z));
            }
        }

        private static Vector2 XZ(Vector3 p) => new(p.x, p.z);
    }
}
