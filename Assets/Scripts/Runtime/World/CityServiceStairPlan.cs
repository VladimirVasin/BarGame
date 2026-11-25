using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityHillsideShortcut
    {
        internal const string Workshop = "workshop-loading-walk", Culvert = "culvert-service-walk", Gable = "east-gable-court";
        internal const float GableWidth = 2.60f;
        internal readonly struct Apron
        {
            internal readonly Vector3 Start, End;
            internal readonly Rect Bounds;
            internal Apron(Vector3 start, Vector3 end)
            {
                Start = start; End = end;
                Vector3 side = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(end - start, Vector3.up).normalized)
                    * (Width * .5f);
                Vector3 a = Vector3.Min(start - side, start + side), b = Vector3.Max(end - side, end + side);
                Bounds = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
            }
            internal float Top(Vector2 p)
            {
                Vector2 a = new(Start.x, Start.z), axis = new(End.x - Start.x, End.z - Start.z);
                return Mathf.Lerp(Start.y, End.y, Vector2.Dot(p - a, axis) / axis.sqrMagnitude);
            }
        }

        private readonly List<Apron> aprons = new();
        internal IReadOnlyList<Apron> Aprons => aprons;

        private void Pitch(Vector3 direction, float length, float finalHeight)
        {
            Vector3 end = Cursor + direction * length;
            end.y = finalHeight;
            var apron = new Apron(Cursor, end);
            aprons.Add(apron);
            AddBounds(apron.Bounds);
            route.Add(end);
        }

        private void SixSteps(Vector3 direction)
        {
            var flight = new CityExteriorStairFlightDescriptor(Id + " working flight", Cursor, direction,
                CityTerracePilotPlan.FlightWidth, 6, .15f, .32f, CityExteriorStairDropSide.Both);
            flights.Add(flight);
            AddBounds(FlightFootprint(flight));
            route.Add(flight.End);
        }

        internal static CityHillsideShortcut WorkshopWalk(CityLayout layout)
        {
            // The east wall leaves just 1.25 m beside its window. The loading walk fits
            // behind the hall and joins the frontage by the vehicle gates.
            if (CityMetalWorkshop.Select(layout)?.Cell != new Vector2Int(2, 3)) return null;
            const float z = -59.65f, endX = -100.35f;
            float low = StreetTop(layout, -82f, z);
            var walk = new CityHillsideShortcut(default, -1, Workshop, new Vector3(-82f, low, z));
            walk.Flat(Vector3.left, .60f);
            walk.SixSteps(Vector3.left);
            walk.Flat(Vector3.left, walk.Cursor.x - (endX + Width * .5f));
            walk.Turn(Vector3.left, Vector3.back, Width);
            walk.Pitch(Vector3.back, walk.Cursor.z + 74f, StreetTop(layout, endX, -74f));
            return walk;
        }

        internal static CityHillsideShortcut CulvertWalk(CityLayout layout)
        {
            // The ring road and the spur bracket an inspection shelf against the outfall.
            // Its south approach climbs gradually along the retaining line. The flight
            // drops back to the grille at the north end, clear of the pole at Z113.
            const float streetX = -160f, shelfZ = 108.8f, reach = 3.60f;
            var walk = new CityHillsideShortcut(default, -1, Culvert,
                new Vector3(streetX, StreetTop(layout, streetX, shelfZ), shelfZ));
            walk.Pitch(Vector3.left, reach, 8.96f);
            walk.Turn(Vector3.left, Vector3.back, Width);
            walk.SixSteps(Vector3.back);
            walk.Flat(Vector3.back, 1.4f);
            // The shorter gravel run clears the existing feeder pole at Z91.
            walk.Pitch(Vector3.back, 10f, 8.89f);
            walk.Turn(Vector3.back, Vector3.right, Width);
            walk.Pitch(Vector3.right, reach, StreetTop(layout, streetX, walk.Cursor.z));
            return walk;
        }

        internal static CityHillsideShortcut GableWalk(CityHillsideStreetRoute street)
        {
            const int lane = 3;
            var house = CityHillsideNeighborhoodPlan.SurveyGableHouse(street);
            Vector2 gate = new(house.CourtBounds.xMin, house.CourtBounds.center.y);
            float north = street.GetLaneZ(lane) - CityHillsideStreetRoute.OuterHalfWidth;
            float lowerX = gate.x - 2 * CityTerracePilotPlan.FlightRun - LandingLength - GableWidth * .5f - 1.8f;
            var stair = new CityHillsideShortcut(street.Bank, lane, Gable,
                new Vector3(lowerX, street.SampleLaneTop(lane, lowerX) + Curb, north));
            float rise = (house.ThresholdWorld.y - stair.Cursor.y) / 3f;
            stair.Flat(Vector3.back, north - gate.y - GableWidth * .5f - CityTerracePilotPlan.FlightRun, GableWidth);
            stair.FlightRun(Vector3.back, 1, rise, LandingLength, GableWidth);
            stair.Turn(Vector3.back, Vector3.right, GableWidth);
            stair.FlightRun(Vector3.right, 2, rise, LandingLength, GableWidth);
            stair.Flat(Vector3.right, gate.x - stair.Cursor.x, GableWidth);
            return stair;
        }

        private static float StreetTop(CityLayout layout, float x, float z)
        {
            if (CityStreetSupportPlan.ForLayout(layout).TrySampleTop(new Vector2(x, z), out float top, out _)) return top;
            throw new InvalidOperationException($"Missing service stair pavement at {x}, {z}");
        }
    }

    internal static class CityServiceStairPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, List<CityHillsideShortcut>> Plans = new();
        internal static IReadOnlyList<CityHillsideShortcut> Lowland(CityLayout layout) => Plans.GetValue(layout, Create);

        private static List<CityHillsideShortcut> Create(CityLayout layout)
        {
            var routes = new List<CityHillsideShortcut>(2);
            if (layout.SpatialPlan.IsCompactCoastal && layout.Seed == GameSessionState.DefaultCitySeed &&
                layout.BlueprintId == CityBlueprintCatalog.DefaultBlueprintId)
            {
                if (CityHillsideShortcut.WorkshopWalk(layout) is { } hall) routes.Add(hall);
                routes.Add(CityHillsideShortcut.CulvertWalk(layout));
            }
            return routes;
        }
    }
}
