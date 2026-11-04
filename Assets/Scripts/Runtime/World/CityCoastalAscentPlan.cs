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
        // The upper street continues east to the coast road. The established
        // western bypass also leads from its terrace mouth to the inland junction.
        public static IReadOnlyList<Vector2Int> TerraceBypassNodes { get; } = System.Array.AsReadOnly(new[] {
            Stations[4], Stations[3], new Vector2Int(11, 9), new Vector2Int(11, 8), new Vector2Int(12, 8)
        });
        // Three continuous through-streets from the coastal bend to the inland junction.
        public static IReadOnlyList<Vector2Int> EasternThroughNodes { get; } = System.Array.AsReadOnly(new[] {
            new Vector2Int(13, 11), new Vector2Int(13, 10), new Vector2Int(13, 9), new Vector2Int(13, 8)
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
            var westEnd = WestTie[WestTie.Length - 1];
            var westContinuation = westEnd + Vector2Int.left;
            if (IsStreet(westEnd, westContinuation)) TieNext(westEnd, westContinuation);
            TieToOriginal(EastTie);
            AddEasternThroughGrade();

            // The blueprint may be reused with different streets or spacing.
            // Check all incident branches before changing any original height.
            foreach (RoadEdge edge in roads)
            {
                if (!targets.ContainsKey(edge.A) && !targets.ContainsKey(edge.B)) continue;
                if (Mathf.Abs(Height(edge.A) - Height(edge.B)) > MaximumRise(edge)) return false;
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

            float MaximumRise(RoadEdge edge) => SlopedRun(edge) * .01f * (kinds[edge] == CityPathKind.Street
                ? CityElevationPlan.MaximumBusGradePercent : CityElevationPlan.MaximumPedestrianGradePercent) + .00001f;

            void AddEasternThroughGrade()
            {
                var chain = EasternThroughNodes;
                var spur = new Vector2Int(12, 8);
                if (!IsStreet(chain[0], NorthFrontageEnd) || !IsStreet(chain[3], spur)) return;
                float available = 0f;
                for (int i = 1; i < chain.Count; i++)
                {
                    if (!IsStreet(chain[i - 1], chain[i])) return;
                    available += SlopedRun(new RoadEdge(chain[i - 1], chain[i]));
                }
                // Through Route 01's offset inland junction, the outer wheels
                // use the physical approach apron. Preserve both ends of its
                // last span; a steeper approach loses this southbound movement.
                int lastRegraded = chain.Count - 2;
                available -= SlopedRun(new RoadEdge(chain[lastRegraded], chain[lastRegraded + 1]));
                float end = Height(chain[lastRegraded]);
                if (available <= 0f || end <= lower || end - lower > available * TieGradePercent * .01f) return;
                float travelled = 0f;
                var through = new Dictionary<Vector2Int, float> { [chain[0]] = lower };
                for (int i = 1; i <= lastRegraded; i++)
                {
                    travelled += SlopedRun(new RoadEdge(chain[i - 1], chain[i]));
                    through[chain[i]] = Mathf.Lerp(lower, end, travelled / available);
                }
                // Where the upper street meets the eastern road, keep both
                // carriageway grades within their usable run between flat mouths.
                var upperConnection = new RoadEdge(Stations[4], chain[1]);
                if (IsStreet(Stations[4], chain[1]))
                {
                    float capacity = SlopedRun(upperConnection) * TieGradePercent * .01f;
                    through[chain[1]] = Mathf.Clamp(through[chain[1]],
                        Height(Stations[4]) - capacity, Height(Stations[4]) + capacity);
                }
                // A different seed can have an additional side street. Decline
                // just this extension if its connection is too short.
                foreach (RoadEdge edge in roads)
                {
                    bool changedA = through.TryGetValue(edge.A, out float a);
                    bool changedB = through.TryGetValue(edge.B, out float b);
                    if (!changedA && !changedB) continue;
                    if (!changedA) a = Height(edge.A);
                    if (!changedB) b = Height(edge.B);
                    if (Mathf.Abs(a - b) > MaximumRise(edge)) return;
                }
                foreach (var station in through) targets[station.Key] = station.Value;
            }

            void TieToOriginal(Vector2Int[] chain)
            {
                for (int i = 1; i < chain.Length; i++)
                    TieNext(chain[i - 1], chain[i]);
            }

            void TieNext(Vector2Int previous, Vector2Int next)
            {
                float previousHeight = Height(previous);
                float capacity = SlopedRun(new RoadEdge(previous, next)) * TieGradePercent * .01f;
                targets[next] = Mathf.Clamp(elevations[next], previousHeight - capacity, previousHeight + capacity);
            }
        }
    }
}
