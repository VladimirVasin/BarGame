using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The shared court under the south rock and the cart approach outside the last east hairpin.</summary>
    internal sealed class RidgeCellarPlan
    {
        internal const string Id = "east-ridge-cellars";
        internal const int Steps = 3;
        internal const float Tread = .48f, Rise = .13f;
        private static readonly ConditionalWeakTable<CityLayout, RidgeCellarPlan> Plans = new();
        private readonly Lazy<IReadOnlyList<Vector3>> walkRoute;
        private readonly Rect levelCourt, benchPad, northJoinBounds;
        private readonly Vector2[] northJoin;
        private readonly float northJoinTop;
        private readonly CityHillsideStreetRoute street;
        private readonly List<Vector2[]> gravel = new();
        private readonly List<Vector2[]> paving = new();
        private readonly List<Vector2[]> solids = new();
        private readonly List<Vector2[]> walking = new();
        private readonly List<Vector2[]> footprints = new();
        private readonly List<Vector2[]> plantingKeepouts = new();
        internal IReadOnlyList<Vector2[]> Gravel => gravel;
        internal IReadOnlyList<Vector2[]> Paving => paving;
        internal IReadOnlyList<Vector2[]> Solids => solids;
        internal IReadOnlyList<Vector2[]> WalkSurfaces => walking;
        internal IReadOnlyList<Vector2[]> MapFootprints => footprints;
        internal Vector2[] ShellOutline { get; }
        internal IReadOnlyList<Vector2> Drain { get; }
        internal IReadOnlyList<Vector2[]> DrainSurface { get; }
        internal IReadOnlyList<Vector3> WalkRoute => walkRoute.Value;
        internal Rect Bounds { get; }
        internal Vector3 Origin { get; }
        internal Vector3 Arrival => Origin + new Vector3(0f, 0f, 2.7f);
        internal Vector2 StairFoot { get; }
        internal float UpperY => Origin.y + Steps * Rise;
        internal Vector2 LaneLampFoot { get; }
        internal Vector2 LaneLampForward { get; }
        internal Vector2[] LaneLampBase { get; }

        internal static RidgeCellarPlan ForLayout(CityLayout layout)
        {
            if (!layout.SpatialPlan.IsCompactCoastal || layout.Seed != GameSessionState.DefaultCitySeed) return null;
            return Plans.GetValue(layout, city => {
                foreach (var road in CityHillsideStreetPlan.ForLayout(city).Routes)
                    if (road.Bank == CityTerraceTransportBank.East) return new RidgeCellarPlan(city, road);
                return null;
            });
        }

        private RidgeCellarPlan(CityLayout layout, CityHillsideStreetRoute road)
        {
            street = road;
            int summit = CityHillsideStreetRoute.LaneCount - 1;
            float toeX = road.LaneMaxX;
            float south = road.GetLaneZ(summit), north = road.GetLaneZ(summit - 1);
            float ground = road.SampleLaneTop(summit, toeX) - .20f;
            Origin = new Vector3(toeX + 8f, ground - Steps * Rise, south - 22f);
            Bounds = Rect.MinMaxRect(Origin.x - 12f, Origin.z - 5.8f, toeX + 26f, north + 8f);
            levelCourt = Rect.MinMaxRect(Origin.x - 9.75f, Origin.z - 5.65f, Origin.x + 10f, Origin.z + 7.85f);
            ShellOutline = Shape(new(-9.75f, -5.65f), new(9.75f, -5.65f), new(9.75f, 0f),
                new(-8.8f, .58f), new(-9.75f, .38f));
            solids.Add(ShellOutline);
            solids.Add(Part(7.10f, 4.50f, 9.35f, 5.42f));
            solids.Add(Part(8.18f, 5.95f, 9.36f, 7.75f));

            var bench = CityHillsideFurniturePlan.NearestRawBench(layout, new Vector2(toeX + 6f, south - 12f));
            benchPad = bench.AccessBounds;
            float stairX = Mathf.Clamp(benchPad.xMax + 1.65f, toeX - .2f, toeX + 8f);
            if (benchPad.xMin - 1.65f >= toeX + .5f) stairX = benchPad.xMin - 1.65f;
            StairFoot = new Vector2(stairX, Mathf.Clamp(benchPad.yMin - Steps * Tread - .5f, Origin.z + 6.6f, Origin.z + 8.6f));

            // Join the north pavement, wrap the entire outside shoulder, then ease into
            // the sorting yard. The two wheel tracks stay on the same walkable triangles.
            float radius = (north - south) * .5f;
            var centre = new Vector2(toeX, south + radius);
            var cart = new List<Vector2> { new(toeX - 2.7f, north + 4.45f), new(toeX, north + 6.65f) };
            for (int i = 1; i <= 13; i++)
            {
                float a = Mathf.PI * i / 16f;
                float offset = Mathf.Lerp(6.65f, 8.05f, Mathf.Clamp01(i / 3f));
                cart.Add(centre + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (radius + offset));
            }
            cart.Add(new Vector2(Origin.x + 5f, Origin.z + 13.7f));
            cart.Add(new Vector2(Origin.x + 5.2f, Origin.z + 8.8f));
            cart.Add(new Vector2(Origin.x + 4.4f, Origin.z + 4f));
            // At the north gate the native dirt drops away between the road seam
            // and the next bank. Cut and fill the cart width through that strip.
            northJoin = cart.GetRange(0, 6).ToArray();
            var join = CityRoadPolygon.Bounds(northJoin);
            northJoinBounds = Rect.MinMaxRect(join.xMin - 3f, join.yMin - 3f, join.xMax + 3f, join.yMax + 3f);
            northJoinTop = road.SampleLaneTop(summit - 1, toeX) +
                CityHillsideBuilding.PavementRise - CityHillsideGroundSeamPlan.SurfaceRecess;
            var lane = new CityRoadPath(cart);
            walking.AddRange(lane.Ribbon(2.25f));
            plantingKeepouts.AddRange(walking);
            gravel.AddRange(lane.Ribbon(.55f, -.57f));
            gravel.AddRange(lane.Ribbon(.55f, .57f));

            // At the lower end of the bend the lamp shines into the ruts. Its footing
            // stands beyond the drain, with the full width of the cart lane free.
            const int lampVertex = 13;
            LaneLampForward = (cart[lampVertex + 1] - cart[lampVertex - 1]).normalized;
            var laneRight = new Vector2(LaneLampForward.y, -LaneLampForward.x);
            LaneLampFoot = cart[lampVertex] - laneRight * 2.38f;
            Vector2 side = laneRight * .15f, front = LaneLampForward * .15f;
            LaneLampBase = CityRoadPolygon.CounterClockwise(new[] {
                LaneLampFoot - side - front, LaneLampFoot + side - front,
                LaneLampFoot + side + front, LaneLampFoot - side + front
            });
            plantingKeepouts.Add(LaneLampBase);

            // The kerb follows the outside edge. Each cast section shares a joint
            // with the next and the channel faces the cart lane.
            var lip = lane.Offset(-1.68f);
            var drain = new List<Vector2>();
            for (int i = lip.Vertices.Count - 3; i >= 1; i--) drain.Add(lip.Vertices[i]);
            Drain = drain;
            var drainPath = new CityRoadPath(drain);
            solids.AddRange(drainPath.Ribbon(.26f));
            DrainSurface = drainPath.Ribbon(.62f, -.1775f);

            var apron = Shape(new(-9.65f, -.1f), new(9.45f, -.1f), new(9.45f, 2.1f),
                new(-9.65f, 3f));
            paving.Add(apron);
            var yard = Shape(new(-10.4f, .8f), new(9.6f, .1f), new(13.5f, 5.2f),
                new(13f, 12.8f), new(4.8f, 13.8f), new(-9.6f, 8.2f));
            gravel.Add(yard);
            walking.Add(yard);
            var shortPath = new List<Vector2> {
                new(StairFoot.x - .6f, Origin.z + 3.6f), StairFoot,
                StairFoot + Vector2.up * (Steps * Tread + .65f),
                new(stairX, south - 7.5f), new(toeX + .6f, south - 4.55f) };
            var doorstep = new CityRoadPath(shortPath);
            var shortPaving = doorstep.Ribbon(2.2f);
            paving.AddRange(shortPaving);
            walking.AddRange(shortPaving);
            walking.Add(apron);
            plantingKeepouts.AddRange(shortPaving);
            plantingKeepouts.Add(apron);
            plantingKeepouts.AddRange(solids);
            footprints.Add(ShellOutline);
            footprints.AddRange(walking);

            var course = new List<Vector2>(cart) { new(Arrival.x, Arrival.z) };
            course.AddRange(shortPath);
            // The terrain builds from this plan. Resolve walking heights after that grid
            // exists, so the map and route follow the same triangles as a player's boots.
            walkRoute = new Lazy<IReadOnlyList<Vector3>>(() => {
                var surface = CityHillsideGroundPlan.ForLayout(layout);
                var route = new List<Vector3>(course.Count);
                foreach (var point in course)
                {
                    if (!road.TrySampleTop(point, out float top, out _) && !surface.TrySampleTop(point, out top))
                        throw new InvalidOperationException("Unsupported cellar approach at " + point);
                    route.Add(new Vector3(point.x, top, point.y));
                }
                return route.AsReadOnly();
            });
        }

        private Vector2[] Part(float x1, float z1, float x2, float z2) =>
            CityRoadPolygon.Rectangle(Rect.MinMaxRect(Origin.x + x1, Origin.z + z1, Origin.x + x2, Origin.z + z2));

        private Vector2[] Shape(params Vector2[] points)
        {
            var origin = new Vector2(Origin.x, Origin.z);
            for (int i = 0; i < points.Length; i++) points[i] += origin;
            return CityRoadPolygon.CounterClockwise(points);
        }

        // Existing trees can stand on the open court. Only trunks on a walking strip
        // or within a solid must clear it; the canopy may still shade the bench.
        internal bool BlocksPlant(Vector2 p, float radius)
        {
            if (p.x < Bounds.xMin - radius || p.x > Bounds.xMax + radius ||
                p.y < Bounds.yMin - radius || p.y > Bounds.yMax + radius) return false;
            foreach (var piece in plantingKeepouts)
            {
                if (CityRoadPolygon.Contains(piece, p)) return true;
                for (int i = 0; i < piece.Length; i++)
                {
                    Vector2 a = piece[i], edge = piece[(i + 1) % piece.Length] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude);
                    if ((p - a - edge * t).sqrMagnitude < radius * radius) return true;
                }
            }
            return false;
        }

        internal void AddGradeBreaks(SortedSet<float> xs, SortedSet<float> zs)
        {
            for (int i = 0; i < northJoin.Length; i++)
            {
                var p = northJoin[i];
                xs.Add(p.x);
                zs.Add(p.y - 1.25f);
                zs.Add(p.y + 1.25f);
            }
            xs.Add(levelCourt.xMin - 1f); xs.Add(levelCourt.xMin); xs.Add(levelCourt.xMax); xs.Add(levelCourt.xMax + 2f);
            xs.Add(StairFoot.x - 1.1f); xs.Add(StairFoot.x + 1.1f);
            zs.Add(levelCourt.yMin); zs.Add(levelCourt.yMax);
            zs.Add(StairFoot.y); zs.Add(StairFoot.y + Steps * Tread); zs.Add(levelCourt.yMax + 6f);
        }

        internal float Grade(Vector2 p, float previous)
        {
            if (northJoinBounds.Contains(p)) return GradeNorthGate(p, previous);
            if (p.x < levelCourt.xMin - 1f || p.x > levelCourt.xMax + 2f ||
                p.y < levelCourt.yMin - .35f || p.y > levelCourt.yMax + 6f) return previous;
            float level = Mathf.Lerp(Origin.y, UpperY, Mathf.InverseLerp(levelCourt.yMax, levelCourt.yMax + 6f, p.y));
            float ramp = Mathf.Lerp(Origin.y, UpperY, Mathf.InverseLerp(StairFoot.y, StairFoot.y + Steps * Tread, p.y));
            level = Mathf.Lerp(ramp, level, Mathf.InverseLerp(1.10f, 2.4f, Mathf.Abs(p.x - StairFoot.x)));
            float left = Mathf.InverseLerp(levelCourt.xMin - 1f, levelCourt.xMin, p.x);
            float right = Mathf.InverseLerp(levelCourt.xMax + 2f, levelCourt.xMax, p.x);
            float back = Mathf.InverseLerp(levelCourt.yMin - .35f, levelCourt.yMin, p.y);
            float front = Mathf.InverseLerp(levelCourt.yMax + 6f, levelCourt.yMax + 4.5f, p.y);
            float benchDistance = Mathf.Max(0f, Mathf.Max(Mathf.Max(benchPad.xMin - p.x, p.x - benchPad.xMax),
                Mathf.Max(benchPad.yMin - p.y, p.y - benchPad.yMax)));
            float weight = Mathf.Min(Mathf.Min(left, right), Mathf.Min(back, front)) * Mathf.Clamp01(benchDistance / .6f);
            return Mathf.Lerp(previous, level, weight);
        }

        private float GradeNorthGate(Vector2 p, float previous)
        {
            float nearest = 9f;
            for (int i = 1; i < northJoin.Length; i++)
            {
                Vector2 from = northJoin[i - 1], span = northJoin[i] - from;
                float t = Mathf.Clamp01(Vector2.Dot(p - from, span) / span.sqrMagnitude);
                nearest = Mathf.Min(nearest, (p - from - t * span).sqrMagnitude);
            }
            if (nearest >= 9f) return previous;
            if (street.TrySampleTop(p, out float roadY, out _))
                return Mathf.Min(previous, roadY - CityHillsideGroundSeamPlan.SurfaceRecess);
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 1.45f, Mathf.Sqrt(nearest)));
            return Mathf.Lerp(previous, northJoinTop, blend);
        }
    }
}
