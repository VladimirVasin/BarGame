using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The four public bays west of home, measured in the sedan's metres.
    /// Depends only on the established house, never on a finished terrain/traffic survey.</summary>
    internal sealed class RidgeParkingPlan
    {
        internal const string LampAnchorPrefix = "Ridge Parking Lamp Anchor ";
        internal const int Capacity = 4, OccupiedBay = 1;
        internal const float Width = 14.5f, Depth = 20.6f, BayWidth = 2.9f, BayLength = 5.8f;
        internal const float TurnRadius = 4.8f, ParkZ = -17.3f, RampFront = 2.9f;
        private static readonly ConditionalWeakTable<CityLayout, RidgeParkingPlan> Cache = new();
        private readonly LastRouteCarDrivePath[] entries = new LastRouteCarDrivePath[Capacity];
        private readonly LastRouteCarDrivePath[] exits = new LastRouteCarDrivePath[Capacity];
        internal Vector3 Origin { get; }
        internal Rect Bounds { get; }
        internal Rect SurfaceBounds { get; }
        internal Rect Entrance { get; }
        internal float RoadTop => Origin.y - CityHillsideBuilding.PavementRise - .012f;
        internal IReadOnlyList<Rect> Curbs { get; }
        internal IReadOnlyList<StreetLampDescriptor> Lamps { get; }

        internal static RidgeParkingPlan ForLayout(CityLayout layout) => layout != null && layout.HasSummitHome
            ? Cache.GetValue(layout, value => new RidgeParkingPlan(value.SummitHome)) : null;

        private RidgeParkingPlan(CityHillsideBuilding home)
        {
            Origin = new Vector3(home.FoundationBounds.xMin - 7.75f, home.ThresholdWorld.y + .012f,
                home.StreetContactWorld.z - 1f);
            Bounds = RectAt(-Width * .5f, -Depth, Width * .5f, 0f);
            SurfaceBounds = RectAt(-Width * .5f, -Depth, Width * .5f, RampFront);
            Entrance = RectAt(-Width * .5f, -.15f, Width * .5f, RampFront + .4f);
            Curbs = Array.AsReadOnly(new[] {
                RectAt(-7.25f, -20.6f, -7.07f, -.15f),
                RectAt(7.07f, -20.6f, 7.25f, -.15f),
                RectAt(-7.25f, -20.6f, 7.25f, -20.42f) });
            var lamps = new StreetLampDescriptor[2];
            Vector3[] feet = { Point(-6.8f, -7.5f), Point(6.7f, -15.3f) };
            for (int i = 0; i < feet.Length; i++)
            {
                Vector3 aim = Point(i == 0 ? -2.5f : 1.8f, i == 0 ? -13f : -16f);
                lamps[i] = new StreetLampDescriptor(default, 0f, StreetLampSide.Right, feet[i],
                    (aim - feet[i]).normalized, aim, StreetLampPurpose.Parking);
            }
            Lamps = Array.AsReadOnly(lamps);
            for (int bay = 0; bay < Capacity; bay++) { entries[bay] = Entry(bay); exits[bay] = Exit(bay); }
        }

        internal Vector3 ParkedPosition(int bay) => Point((bay - 1.5f) * BayWidth, ParkZ);
        internal Rect Bay(int bay) => RectAt((bay - 2f) * BayWidth, ParkZ - BayLength * .5f,
            (bay - 1f) * BayWidth, ParkZ + BayLength * .5f);
        internal LastRouteCarDrivePath EntryPath(int bay) => entries[bay];
        internal LastRouteCarDrivePath ExitPath(int bay) => exits[bay];
        internal bool Clears(CityHillsideSiteDetails.Site site) =>
            (site.Part == CityHillsideSiteDetails.Kind.GardenVerge || site.Part == CityHillsideSiteDetails.Kind.YardTree ||
             site.Part == CityHillsideSiteDetails.Kind.RetainingEnd || site.Part == CityHillsideSiteDetails.Kind.CourtEdge) &&
            SurfaceBounds.Overlaps(site.Bounds);

        internal Vector3 Point(float x, float z) => new(Origin.x + x, SurfaceTop(z), Origin.z + z);
        internal float SurfaceTop(float localZ) => Mathf.Lerp(Origin.y, RoadTop,
            Mathf.InverseLerp(2f, RampFront, localZ));
        internal float Grade(Vector2 p, float before)
        {
            if (p.y > Origin.z) return before; // Public sidewalk/road keep their surveyed support.
            float outside = Mathf.Max(Mathf.Max(Bounds.xMin - p.x, p.x - Bounds.xMax),
                Mathf.Max(Bounds.yMin - p.y, p.y - Bounds.yMax));
            return outside >= .35f ? before : Mathf.Lerp(before, Origin.y - .025f,
                1f - Mathf.SmoothStep(0f, 1f, outside / .35f));
        }
        internal void AddGradeBreaks(Rect terrain, SortedSet<float> xs, SortedSet<float> zs)
        {
            if (!terrain.Overlaps(Bounds)) return;
            foreach (float v in new[] { Bounds.xMin - .35f, Bounds.xMin, Bounds.xMax, Bounds.xMax + .35f })
                if (v > terrain.xMin && v < terrain.xMax) xs.Add(v);
            foreach (float v in new[] { Bounds.yMin - .35f, Bounds.yMin, Bounds.yMax })
                if (v > terrain.yMin && v < terrain.yMax) zs.Add(v);
        }

        private Rect RectAt(float x, float z, float right, float front) =>
            Rect.MinMaxRect(Origin.x + x, Origin.z + z, Origin.x + right, Origin.z + front);

        private LastRouteCarDrivePath Entry(int bay)
        {
            // Shared right turn from the eastbound lane, then an S into the bay.
            var route = new Maneuver(this, -TurnRadius - 6f, 3.5f, Mathf.PI * .5f);
            route.Straight(6f); route.Arc(Mathf.PI * .5f); route.Straight(.5f);
            float offset = (bay - 1.5f) * BayWidth;
            float angle = Mathf.Acos(1f - Mathf.Abs(offset) / (2f * TurnRadius));
            float sign = offset > 0f ? -1f : 1f;
            route.Arc(sign * angle); route.Arc(-sign * angle);
            route.Straight(route.Z - ParkZ);
            return new LastRouteCarDrivePath(route.Points);
        }
        private LastRouteCarDrivePath Exit(int bay)
        {
            // Reverse clear of the adjacent noses, turn into the aisle, stop,
            // then drive forward north and merge east. The cusp preserves body heading.
            var route = new Maneuver(this, (bay - 1.5f) * BayWidth, ParkZ, 0f);
            route.Straight(-11.75f - ParkZ);
            float sign = bay < 2 ? 1f : -1f;
            route.Arc(sign * Mathf.PI * .5f);
            int reverseCount = route.Points.Count;
            route.ReverseDirection();
            route.Arc(sign * Mathf.PI * .5f);
            route.Straight(3.5f - TurnRadius - route.Z);
            route.Arc(Mathf.PI * .5f); route.Straight(7f);
            return new LastRouteCarDrivePath(route.Points, reverseCount);
        }
        private sealed class Maneuver
        {
            private readonly RidgeParkingPlan site;
            private float x, z, heading;
            internal float Z => z;
            internal readonly List<Vector3> Points = new(480);
            internal Maneuver(RidgeParkingPlan site, float x, float z, float heading)
            { this.site = site; this.x = x; this.z = z; this.heading = heading; Points.Add(site.Point(x, z)); }
            internal void ReverseDirection() => heading += Mathf.PI;
            internal void Straight(float distance)
            {
                if (distance < -.01f) throw new InvalidOperationException("Parking aisle is too short.");
                int steps = Mathf.Max(1, Mathf.CeilToInt(distance / .08f));
                for (int i = 1; i <= steps; i++) Points.Add(site.Point(x + Mathf.Sin(heading) * distance * i / steps,
                    z + Mathf.Cos(heading) * distance * i / steps));
                x += Mathf.Sin(heading) * distance; z += Mathf.Cos(heading) * distance;
            }
            internal void Arc(float angle)
            {
                float radius = TurnRadius / Mathf.Sign(angle);
                int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(angle) * TurnRadius / .08f));
                for (int i = 1; i <= steps; i++)
                {
                    float end = heading + angle * i / steps;
                    Points.Add(site.Point(x + radius * (Mathf.Cos(heading) - Mathf.Cos(end)),
                        z + radius * (Mathf.Sin(end) - Mathf.Sin(heading))));
                }
                x += radius * (Mathf.Cos(heading) - Mathf.Cos(heading + angle));
                z += radius * (Mathf.Sin(heading + angle) - Mathf.Sin(heading));
                heading += angle;
            }
        }
    }
}
