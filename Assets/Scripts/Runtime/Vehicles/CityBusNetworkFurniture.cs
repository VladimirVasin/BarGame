using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityBusNetworkFurniture
    {
        // A shared platform has one physical pole and bench. Opposite banks of a
        // pedestrian interchange keep separate platforms even with the same name.
        public static IEnumerable<CityBusStopDescriptor> Stops(IReadOnlyList<CityBusPlan> lines)
        {
            foreach (var platform in Platforms(lines)) yield return platform.stop;
        }

        internal static bool SamePlatform(Vector3 first, Vector3 second) => (first - second).sqrMagnitude < .25f;

        internal static IEnumerable<(CityBusPlan line, CityBusStopDescriptor stop, bool shared, int ordinal, int transferOrdinal)>
            Platforms(IReadOnlyList<CityBusPlan> lines)
        {
            var transfers = new Dictionary<(string id, int route), (CityBusStopDescriptor stop, int lineIndex, int ordinal)>();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                for (int j = 0; j < line.Stops.Count; j++)
                {
                    var stop = line.Stops[j];
                    if (stop.TransferNumber > 0)
                        transfers[(stop.TargetId, line.Number)] = (stop, i, Ordinal(stop, j));
                }
            }
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                for (int j = 0; j < line.Stops.Count; j++)
                {
                    var stop = line.Stops[j];
                    int partnerOrdinal = 0;
                    bool shared = false;
                    if (stop.TransferNumber > 0 && transfers.TryGetValue((stop.TargetId, stop.TransferNumber), out var partner))
                    {
                        shared = SamePlatform(stop.ShelterPosition, partner.stop.ShelterPosition);
                        if (shared && partner.lineIndex < i) continue;
                        partnerOrdinal = partner.ordinal;
                    }
                    yield return (line, stop, shared, Ordinal(stop, j), partnerOrdinal);
                }
            }
        }

        private static int Ordinal(CityBusStopDescriptor stop, int index) =>
            (stop.SequenceIndex >= 0 ? stop.SequenceIndex : index) + 1;

        // Upstream traffic and later decoration reserve both the shelter itself
        // and the pavement used to reach its bench and the two vehicle doors.
        internal static IEnumerable<Rect> Reservations(IReadOnlyList<CityBusPlan> lines, CityLayout layout)
        {
            foreach (var platform in Platforms(lines))
            {
                var stop = platform.stop;
                yield return CityBusStopWaitPlanner.QueueApron(platform.line, stop, layout);
                yield return CityBusPlatformClearance.ShelterReservation(stop.ShelterPosition, stop.Forward,
                    stop.RoadsideForward);
                yield return CityBusPlatformClearance.PassengerAccess(stop.Position, stop.Forward,
                    stop.ShelterPosition, stop.RoadsideForward);
                yield return CityBusPlatformClearance.PavementApproach(stop.Position, stop.Forward, stop.RoadsideForward);
            }
        }

        public static HashSet<RoadEdge> ServiceRoads(IReadOnlyList<CityBusPlan> lines)
        {
            var roads = new HashSet<RoadEdge>();
            foreach (var line in lines)
            foreach (var node in line.Nodes)
                if (node.RoadEdge.A != node.RoadEdge.B) roads.Add(node.RoadEdge);
            return roads;
        }
    }
}
