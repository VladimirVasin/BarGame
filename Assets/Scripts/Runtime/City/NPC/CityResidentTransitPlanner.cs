using System.Collections.Generic;

namespace BarPromenade
{
    internal readonly struct CityResidentBusLeg
    {
        internal readonly int Line, Board, Exit;
        internal CityResidentBusLeg(int line, int board, int exit)
        { Line = line; Board = board; Exit = exit; }
    }

    internal readonly struct CityResidentItinerary
    {
        internal readonly CityResidentBusLeg First, Second;
        internal readonly int LegCount;
        internal CityResidentItinerary(CityResidentBusLeg first)
        { First = first; Second = default; LegCount = 1; }
        internal CityResidentItinerary(CityResidentBusLeg first, CityResidentBusLeg second)
        { First = first; Second = second; LegCount = 2; }
    }

    /// <summary>Each pair of platforms has one cheapest one-way ride, optionally
    /// with a walk across a published interchange. Walking alone competes with
    /// the entire trip, including access, expected waits and last mile.</summary>
    internal sealed class CityResidentTransitPlanner
    {
        private readonly CityResidentWorld world;
        private readonly List<Platform> platforms = new List<Platform>();
        private readonly List<Option> options = new List<Option>();
        private readonly Dictionary<(int from, int to), CityResidentItinerary> trips =
            new Dictionary<(int, int), CityResidentItinerary>();
        private readonly IReadOnlyList<CityBusStopWaitPlan> waitPlans;

        internal CityResidentTransitPlanner(CityResidentWorld world, IReadOnlyList<CityBusPlan> lines,
            IReadOnlyList<CityBusStopWaitPlan> waits)
        {
            this.world = world;
            waitPlans = waits;
            var clocks = new CityBusTimetable[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                CityBusPlan line = lines[i];
                if (line.IsEmpty) continue;
                clocks[i] = CityBusTimetable.ForPlan(line);
                foreach (CityBusStopWaitPoint point in waits[i].WaitPoints)
                    platforms.Add(new Platform(i, point, line.Stops[point.StopIndex]));
            }
            int count = platforms.Count;
            var direct = new float[count, count];
            for (int a = 0; a < count; a++)
            for (int b = 0; b < count; b++)
            {
                Platform first = platforms[a], last = platforms[b];
                direct[a, b] = float.PositiveInfinity;
                if (a == b || first.Line != last.Line) continue;
                CityBusTimetable clock = clocks[first.Line];
                // Expected wait is half the two-vehicle headway. The actual
                // transfer is always made on an individual service event.
                double ride = CityBusTimetable.Wrap(clock.Arrival(last.Point.StopIndex) -
                    clock.Arrival(first.Point.StopIndex), clock.Duration);
                direct[a, b] = (float)(clock.Duration * .25d + ride);
                options.Add(new Option(first, last, direct[a, b], new CityResidentItinerary(Leg(first, last))));
            }

            // Opposite platforms at the mouth bridge have a real walking leg;
            // equal transfer IDs alone do not make its crossing instantaneous.
            for (int x = 0; x < count; x++)
            for (int y = 0; y < count; y++)
            {
                Platform off = platforms[x], on = platforms[y];
                if (off.Line == on.Line || off.Stop.TransferNumber != lines[on.Line].Number ||
                    on.Stop.TransferNumber != lines[off.Line].Number || off.Stop.TargetId != on.Stop.TargetId) continue;
                float change = on.Point.NodeDistances[off.Point.PedestrianNodeIndex] / CityResidentWorld.WalkingSpeed;
                if (float.IsInfinity(change)) continue;
                for (int a = 0; a < count; a++)
                for (int b = 0; b < count; b++)
                {
                    float cost = direct[a, x] + change + direct[y, b];
                    if (float.IsInfinity(cost)) continue;
                    Platform first = platforms[a], last = platforms[b];
                    options.Add(new Option(first, last, cost,
                        new CityResidentItinerary(Leg(first, off), Leg(on, last))));
                }
            }
        }

        internal int Node(int line, int stop)
        {
            waitPlans[line].TryGetWaitPoint(stop, out var point);
            return point.PedestrianNodeIndex;
        }

        internal CityResidentItinerary Plan(int from, int destination)
        {
            if (trips.TryGetValue((from, destination), out CityResidentItinerary best)) return best;
            float bestTime = world.DistancesTo(destination)[from] / CityResidentWorld.WalkingSpeed;
            foreach (Option option in options)
            {
                float walking = option.First.Point.NodeDistances[from] +
                    option.Last.Point.NodeDistances[destination];
                float time = option.Seconds + walking / CityResidentWorld.WalkingSpeed;
                if (time >= bestTime) continue;
                bestTime = time;
                best = option.Trip;
            }
            // Both endpoints are permanent places or platform nodes, never arbitrary
            // samples along the street. Cache therefore has no calendar dimension.
            trips.Add((from, destination), best);
            return best;
        }

        private static CityResidentBusLeg Leg(Platform a, Platform b) =>
            new CityResidentBusLeg(a.Line, a.Point.StopIndex, b.Point.StopIndex);

        private readonly struct Platform
        {
            internal readonly int Line;
            internal readonly CityBusStopWaitPoint Point;
            internal readonly CityBusStopDescriptor Stop;
            internal Platform(int line, CityBusStopWaitPoint point, CityBusStopDescriptor stop)
            { Line = line; Point = point; Stop = stop; }
        }

        private readonly struct Option
        {
            internal readonly Platform First, Last;
            internal readonly float Seconds;
            internal readonly CityResidentItinerary Trip;
            internal Option(Platform first, Platform last, float seconds, CityResidentItinerary trip)
            { First = first; Last = last; Seconds = seconds; Trip = trip; }
        }
    }
}
