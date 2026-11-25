using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The roof is the upper walking plane over the enclosed garage floor.</summary>
    internal sealed class RidgeGaragePlan
    {
        internal const string ResourcePath = "City/RidgeGarages/RidgeGarages";
        internal const string StableId = "ridge-garages";
        internal const float RoofHeight = 3f;
        private const float CenterX = -112f, FrontZ = -278.8f;
        private const int LowerLane = 3;
        private const float StairX = 8.05f, TopLandingZ = -4.15f;
        private const float Run = CityTerracePilotPlan.FlightRun, Landing = 1.2f;
        private static readonly ConditionalWeakTable<CityLayout, RidgeGaragePlan> Plans = new();
        private readonly CityHillsideStreetRoute street;
        internal Vector3 Origin { get; }
        internal Rect Shell { get; }
        internal Rect Stair { get; }
        internal Rect Apron { get; }
        internal Rect UpperPath { get; }
        internal Rect Bounds { get; }
        internal CityExteriorStairFlightDescriptor[] Flights { get; }
        internal Vector3 LowerArrival => Origin + new Vector3(4.28f, 0f, -.85f);
        internal Vector3 RoofArrival => Origin + new Vector3(-5.5f, RoofHeight, -4.7f);
        internal IReadOnlyList<Vector3> Path { get; }
        private float LowerEdge => street.GetLaneZ(LowerLane) - CityHillsideStreetRoute.OuterHalfWidth;
        private float UpperEdge => street.GetLaneZ(LowerLane + 1) + CityHillsideStreetRoute.OuterHalfWidth;

        internal static RidgeGaragePlan ForLayout(CityLayout layout)
        {
            if (!layout.SpatialPlan.IsCompactCoastal || layout.Seed != GameSessionState.DefaultCitySeed) return null;
            return Plans.GetValue(layout, value => {
                foreach (var road in CityHillsideStreetPlan.ForLayout(value).Routes)
                    if (road.Bank == CityTerraceTransportBank.West) return new RidgeGaragePlan(road);
                return null;
            });
        }

        private RidgeGaragePlan(CityHillsideStreetRoute road)
        {
            street = road;
            Origin = new Vector3(CenterX, StreetTop(LowerLane, CenterX), FrontZ);
            Shell = Rect.MinMaxRect(CenterX - 7f, FrontZ - 6f, CenterX + 7f, FrontZ);
            float stairStart = TopLandingZ + Run * 2f + Landing;
            Stair = Rect.MinMaxRect(Shell.xMax, Shell.yMin, CenterX + 9.1f, FrontZ + stairStart);
            Apron = Rect.MinMaxRect(Shell.xMin, FrontZ, Stair.xMax, LowerEdge);
            UpperPath = Rect.MinMaxRect(CenterX - 6.7f, UpperEdge, CenterX - 4.3f, Shell.yMin);
            Bounds = Rect.MinMaxRect(Shell.xMin, UpperEdge, Stair.xMax, LowerEdge);
            Flights = new[] { Flight(1, 0f, stairStart), Flight(2, 1.5f, TopLandingZ + Run) };
            var points = new List<Vector3> {
                new(CenterX + StairX, StreetTop(LowerLane, CenterX + StairX), LowerEdge + 1f)
            };
            foreach (var flight in Flights) { points.Add(Origin + flight.Start); points.Add(Origin + flight.End); }
            points.Add(Origin + new Vector3(StairX, RoofHeight, -5.1f));
            points.Add(Origin + new Vector3(-5.5f, RoofHeight, -5.1f));
            points.Add(Origin + new Vector3(-5.5f, RoofHeight, -6f));
            points.Add(new Vector3(CenterX - 5.5f, StreetTop(LowerLane + 1, CenterX - 5.5f), UpperEdge - 1f));
            Path = points;
        }

        private static CityExteriorStairFlightDescriptor Flight(int number, float y, float z) =>
            new("Garage stair " + number, new Vector3(StairX, y, z), Vector3.back,
                CityTerracePilotPlan.FlightWidth, CityTerracePilotPlan.StepsPerFlight,
                RoofHeight / (2 * CityTerracePilotPlan.StepsPerFlight), CityTerracePilotPlan.TreadDepth,
                CityExteriorStairDropSide.Both);

        private float StreetTop(int lane, float x) =>
            street.SampleLaneTop(lane, x) + CityHillsideBuilding.PavementRise;

        internal bool IsPaved(Vector2 p) => Apron.Contains(p) || UpperPath.Contains(p);

        internal float SurfaceTop(Vector2 p)
        {
            if (p.y < Shell.yMin)
                return Mathf.Lerp(StreetTop(LowerLane + 1, p.x), Origin.y + RoofHeight,
                    Mathf.InverseLerp(UpperEdge, Shell.yMin, p.y));
            if (p.x >= Stair.xMin && p.y <= Stair.yMax)
            {
                foreach (var flight in Flights)
                {
                    float distance = FrontZ + flight.Start.z - p.y;
                    if (distance <= flight.RunLength)
                        return Origin.y + flight.Start.y + Mathf.Clamp01(distance / flight.RunLength) * flight.TotalRise;
                }
                return Origin.y + RoofHeight;
            }
            if (p.y <= FrontZ) return Origin.y + RoofHeight;
            return ApronTop(p);
        }

        private float ApronTop(Vector2 p)
        {
            // The stair foot meets the forecourt at the same datum as the bays.
            float flatEnd = p.x >= Stair.xMin ? Stair.yMax : FrontZ + .55f;
            return Mathf.Lerp(Origin.y, StreetTop(LowerLane, p.x), Mathf.InverseLerp(flatEnd, LowerEdge, p.y));
        }

        internal float Grade(Vector2 p, float previous)
        {
            if (p.y <= UpperEdge || p.y >= LowerEdge) return previous;
            float dx = Mathf.Max(0f, Mathf.Max(Bounds.xMin - p.x, p.x - Bounds.xMax));
            if (dx >= 1.25f) return previous;
            // At the shared front edge terrain belongs to the lower forecourt.
            // An unhinted map point at the same XZ instead lands on the roof.
            float level;
            if (p.x >= Stair.xMin && p.y <= Stair.yMax) level = SurfaceTop(p);
            else if (p.y >= FrontZ) level = ApronTop(p);
            else if (p.y <= Shell.yMin) level = SurfaceTop(p);
            else level = Origin.y + RoofHeight * Mathf.InverseLerp(FrontZ, Shell.yMin, p.y);
            return Mathf.Lerp(level - CityHillsideGroundSeamPlan.SurfaceRecess, previous, Mathf.SmoothStep(0f, 1f, dx / 1.25f));
        }

        internal bool SafeArrival(Vector2 p, float? hintedY, float radius, out float top)
        {
            top = SurfaceTop(p);
            if (p.y >= Shell.yMin && p.y <= FrontZ && p.x >= Shell.xMin && p.x <= Shell.xMax)
            {
                // Explicit lower-level destinations are restricted to the clear entrance aisle.
                if (hintedY.HasValue && hintedY.Value < Origin.y + RoofHeight * .5f)
                {
                    top = Origin.y;
                    return p.x >= CenterX + 3.7f + radius && p.x <= CenterX + 6.8f - radius &&
                        p.y >= FrontZ - 1.25f && p.y <= FrontZ;
                }
                return p.x >= Shell.xMin + radius + .12f && p.x <= Shell.xMax - radius - .12f &&
                    p.y >= Shell.yMin + radius + .12f && p.y <= Shell.yMax - radius - .12f;
            }
            if (Stair.Contains(p)) return Mathf.Abs(p.x - (CenterX + StairX)) <= CityTerracePilotPlan.FlightWidth * .5f - radius;
            return true;
        }
    }
}
