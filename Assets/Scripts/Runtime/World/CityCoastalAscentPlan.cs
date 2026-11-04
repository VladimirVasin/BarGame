using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The first built section of the east coast climb follows existing streets.
    /// The seafront supplies its datum and the short cut crosses one flight of rise.</summary>
    public static class CityCoastalAscentPlan
    {
        public const float TargetRise = 2f * CityTerracePilotPlan.FlightRise;
        private const float TieGradePercent = 5.9f;
        private static readonly Vector2Int[] Stations = {
            new Vector2Int(10, 12), new Vector2Int(11, 12), new Vector2Int(11, 11),
            new Vector2Int(11, 10), new Vector2Int(12, 10)
        };
        // Keep the north side of the short-cut block level at its street socket.
        private static readonly Vector2Int NorthFrontageEnd = new Vector2Int(12, 11);
        // Work outward only along the side streets which would otherwise exceed
        // the vehicle grade. Their far ends meet the original network.
        private static readonly Vector2Int[] WestTie = {
            Stations[3], new Vector2Int(11, 9), new Vector2Int(10, 9), new Vector2Int(9, 9)
        };
        private static readonly Vector2Int[] EastTie = { NorthFrontageEnd, new Vector2Int(13, 11) };

        public static IReadOnlyList<Vector2Int> RouteNodes { get; } = System.Array.AsReadOnly(Stations);
        // A vehicle leaving the upper cul-de-sac reaches the terrace junction by
        // returning to (11,10). The interior blocks have no through carriageway.
        public static IReadOnlyList<Vector2Int> TerraceBypassNodes { get; } = System.Array.AsReadOnly(new[] {
            Stations[4], Stations[3], new Vector2Int(11, 9), new Vector2Int(11, 8), new Vector2Int(12, 8)
        });
        public static bool IsEnabled(CityLayout layout) => layout?.ElevationPlan?.HasCoastalAscent == true;

        internal static bool TryApply(CityGenerationSettings settings, IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> kinds, Dictionary<Vector2Int, float> elevations)
        {
            CityRoadGeometryPlan geometry = settings.RoadGeometry;
            if (!CityTerraceInlandJunctionPlan.Supports(settings) || geometry == null ||
                !IsStreetChain(Stations) || !IsStreetChain(WestTie) || !IsStreetChain(EastTie) ||
                !IsStreet(Stations[2], NorthFrontageEnd)) return false;

            float coast = elevations[Stations[0]];
            float lower = coast + CityTerracePilotPlan.FlightRise;
            var targets = new Dictionary<Vector2Int, float> {
                [Stations[2]] = lower,
                [Stations[3]] = lower + CityTerracePilotPlan.FlightRise * .5f,
                [Stations[4]] = coast + TargetRise,
                [NorthFrontageEnd] = lower
            };
            TieToOriginal(WestTie);
            TieToOriginal(EastTie);

            // The blueprint may be reused with different streets or spacing.
            // Check all incident branches before changing any original height.
            foreach (RoadEdge edge in roads)
            {
                if (!targets.ContainsKey(edge.A) && !targets.ContainsKey(edge.B)) continue;
                float maximum = kinds[edge] == CityPathKind.Street
                    ? CityElevationPlan.MaximumBusGradePercent : CityElevationPlan.MaximumPedestrianGradePercent;
                if (Mathf.Abs(Height(edge.A) - Height(edge.B)) > SlopedRun(edge) * maximum * .01f + .00001f)
                    return false;
            }
            for (int i = 1; i < Stations.Length; i++)
                if (Height(Stations[i]) <= Height(Stations[i - 1])) return false;
            foreach (var pair in targets) elevations[pair.Key] = pair.Value;
            return true;

            bool IsStreet(Vector2Int a, Vector2Int b) => elevations.ContainsKey(a) && elevations.ContainsKey(b) &&
                kinds.TryGetValue(new RoadEdge(a, b), out CityPathKind kind) && kind == CityPathKind.Street;

            bool IsStreetChain(Vector2Int[] chain)
            {
                for (int i = 1; i < chain.Length; i++)
                    if (!IsStreet(chain[i - 1], chain[i])) return false;
                return true;
            }

            float Height(Vector2Int node) => targets.TryGetValue(node, out float target) ? target : elevations[node];
            float SlopedRun(RoadEdge edge) => Mathf.Max(0f, geometry.Get(edge).Length -
                geometry.GetEndpointInset(edge, edge.A) - geometry.GetEndpointInset(edge, edge.B));

            void TieToOriginal(Vector2Int[] chain)
            {
                for (int i = 1; i < chain.Length; i++)
                {
                    Vector2Int previous = chain[i - 1], next = chain[i];
                    float previousHeight = Height(previous);
                    float capacity = SlopedRun(new RoadEdge(previous, next)) * TieGradePercent * .01f;
                    targets[next] = Mathf.Clamp(elevations[next], previousHeight - capacity, previousHeight + capacity);
                }
            }
        }
    }
}
