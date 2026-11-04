using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two ordinary flights between adjacent switchback pavements. The
    /// street profiles determine where their height difference fits the kit.</summary>
    public sealed class CityHillsideShortcut
    {
        public CityTerraceTransportBank Bank { get; }
        public Rect Bounds { get; }
        public IReadOnlyList<Vector3> RouteWorld { get; }
        public const float Width = 2.1f;

        private CityHillsideShortcut(CityTerraceTransportBank bank, float x, float north, float south,
            float low, float high)
        {
            Bank = bank;
            Bounds = Rect.MinMaxRect(x - Width * .5f, south, x + Width * .5f, north);
            float middle = (north + south) * .5f;
            float run = CityTerracePilotPlan.FlightRun;
            float halfLanding = CityTerracePilotPlan.LandingSize * .5f;
            float intermediate = low + CityTerracePilotPlan.FlightRise;
            RouteWorld = Array.AsReadOnly(new[] {
                new Vector3(x, low, north), new Vector3(x, low, middle + halfLanding + run),
                new Vector3(x, intermediate, middle + halfLanding),
                new Vector3(x, intermediate, middle - halfLanding),
                new Vector3(x, high, middle - halfLanding - run), new Vector3(x, high, south) });
        }

        public bool TrySampleTop(Vector2 point, out float top)
        {
            top = 0f;
            if (!CityTerracePilotPlan.Inside(Bounds, point.x, point.y)) return false;
            for (int i = 1; i < RouteWorld.Count; i++)
            {
                Vector3 a = RouteWorld[i - 1], b = RouteWorld[i];
                if (point.y < b.z && i < RouteWorld.Count - 1) continue;
                top = Mathf.Lerp(a.y, b.y, Mathf.InverseLerp(a.z, b.z, point.y));
                return true;
            }
            return false;
        }

        internal static CityHillsideShortcut Create(CityHillsideStreetRoute route)
        {
            // Prefer the upper pair so the foot route serves the inhabited
            // summit. Work in the straight portions, outside the flat bends.
            const float rise = CityTerracePilotPlan.FlightRise * 2f;
            const float curb = CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop;
            for (int upper = CityHillsideStreetRoute.LaneCount - 1; upper > 0; upper--)
            {
                float left = route.LaneMinX + Width, right = route.LaneMaxX - Width;
                float a = Difference(left), b = Difference(right);
                if (a * b > 0f) continue;
                for (int step = 0; step < 24; step++)
                {
                    float mid = (left + right) * .5f;
                    if (a * Difference(mid) <= 0f) right = mid;
                    else left = mid;
                }
                float x = (left + right) * .5f;
                float north = route.GetLaneZ(upper - 1) - CityHillsideStreetRoute.OuterHalfWidth;
                float south = route.GetLaneZ(upper) + CityHillsideStreetRoute.OuterHalfWidth;
                float low = route.SampleLaneTop(upper - 1, x) + curb;
                return new CityHillsideShortcut(route.Bank, x, north, south, low, low + rise);

                float Difference(float at) => route.SampleLaneTop(upper, at) - route.SampleLaneTop(upper - 1, at) - rise;
            }
            return null;
        }
    }

    public sealed class CityHillsideAccessPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideAccessPlan> Plans = new();
        public IReadOnlyList<CityHillsideShortcut> Shortcuts { get; }
        private CityHillsideAccessPlan(CityLayout layout)
        {
            var result = new List<CityHillsideShortcut>();
            foreach (CityHillsideStreetRoute route in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                CityHillsideShortcut stair = CityHillsideShortcut.Create(route);
                if (stair == null) throw new InvalidOperationException("No supported stair between " + route.Bank + " hillside streets.");
                result.Add(stair);
            }
            Shortcuts = result.AsReadOnly();
        }
        public static CityHillsideAccessPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityHillsideAccessPlan(value));
    }
}
