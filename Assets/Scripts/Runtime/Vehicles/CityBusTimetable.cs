using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One immutable circulation clock per line. Service and lane changes split
    /// the ring into acceleration, cruise and braking spans. Sampling a day away costs the
    /// same as sampling the next frame.</summary>
    internal sealed class CityBusTimetable
    {
        private static readonly ConditionalWeakTable<CityBusPlan, CityBusTimetable> Tables = new ConditionalWeakTable<CityBusPlan, CityBusTimetable>();
        internal static CityBusTimetable ForPlan(CityBusPlan plan) => Tables.GetValue(plan, Build);
        private static CityBusTimetable Build(CityBusPlan plan) => new CityBusTimetable(plan);

        internal readonly struct Sample
        {
            internal readonly int Link, Stop, LastStop;
            internal readonly float Along, Speed, Dwell;
            internal Sample(int link, float along, float speed, int stop, float dwell, int lastStop)
            { Link = link; Along = along; Speed = speed; Stop = stop; Dwell = dwell; LastStop = lastStop; }
        }

        private sealed class Span
        {
            internal int Link, Stop = -1;
            internal double Along, Length, Cap, Entry, Exit, Peak, Accelerating, Cruising, Travel;
            internal double Departure => Stop >= 0 ? CityBusActor.DwellDuration : 0d;

            internal void SetTiming()
            {
                const double a = CityBusActor.Acceleration, b = CityBusActor.ServiceDeceleration;
                Peak = Math.Min(Cap, Math.Sqrt((2d * a * b * Length + b * Entry * Entry + a * Exit * Exit) / (a + b)));
                Accelerating = Math.Max(0d, (Peak - Entry) / a);
                double braking = Math.Max(0d, (Peak - Exit) / b);
                Cruising = Math.Max(0d, (Length - Accelerating * (Peak + Entry) * .5d -
                    braking * (Peak + Exit) * .5d) / Peak);
                Travel = Accelerating + Cruising + braking;
            }

            internal double TimeAt(double distance)
            {
                double accelLength = Accelerating * (Entry + Peak) * .5d;
                if (distance <= accelLength)
                    return (Math.Sqrt(Entry * Entry + 2d * CityBusActor.Acceleration * distance) - Entry) / CityBusActor.Acceleration;
                if (distance <= accelLength + Cruising * Peak)
                    return Accelerating + (distance - accelLength) / Peak;
                return Travel - (Math.Sqrt(Exit * Exit + 2d * CityBusActor.ServiceDeceleration * Math.Max(0d, Length - distance))
                    - Exit) / CityBusActor.ServiceDeceleration;
            }

            internal void SampleMotion(double elapsed, out float along, out float speed)
            {
                elapsed = Math.Min(Travel, Math.Max(0d, elapsed));
                double position, velocity;
                if (elapsed < Accelerating)
                {
                    velocity = Entry + CityBusActor.Acceleration * elapsed;
                    position = elapsed * (Entry + velocity) * .5d;
                }
                else if (elapsed <= Accelerating + Cruising)
                {
                    velocity = Peak;
                    position = Accelerating * (Entry + Peak) * .5d + (elapsed - Accelerating) * Peak;
                }
                else
                {
                    double remaining = Travel - elapsed;
                    velocity = Exit + CityBusActor.ServiceDeceleration * remaining;
                    position = Length - remaining * (Exit + velocity) * .5d;
                }
                along = (float)(Along + position);
                speed = (float)velocity;
            }
        }

        private readonly CityBusPlan plan;
        private readonly Span[] spans;
        private readonly double[] times, distances, serviceTimes;
        private readonly int[] serviceStops;
        private readonly double[] linkStarts, arrivals;
        internal double Duration { get; }
        private double Length { get; }

        private CityBusTimetable(CityBusPlan plan)
        {
            this.plan = plan;
            linkStarts = new double[plan.Links.Count];
            arrivals = new double[plan.Stops.Count];
            var sections = new List<Span>();
            double length = 0d;
            foreach (int link in plan.OrderedLinkIndices)
            {
                linkStarts[link] = length;
                var lane = plan.Links[link];
                var stops = new List<int>(plan.GetStopIndices(link));
                stops.Sort((a, b) => plan.Stops[a].DistanceAlongLink.CompareTo(plan.Stops[b].DistanceAlongLink));
                double from = 0d;
                foreach (int stop in stops)
                {
                    double at = plan.Stops[stop].DistanceAlongLink;
                    if (at > from) Add(at);
                }
                if (lane.Length > from) Add(lane.Length);
                length += lane.Length;

                void Add(double end)
                {
                    sections.Add(new Span { Link = link, Along = from, Length = end - from,
                        Cap = lane.Kind == CityBusRouteLinkKind.Straight ? CityBusActor.CruiseSpeed : CityBusActor.JunctionSpeed });
                    from = end;
                }
            }
            Length = length;
            spans = sections.ToArray();
            int count = spans.Length;
            distances = new double[count];
            times = new double[count];
            for (int i = 0; i < count; i++) distances[i] = linkStarts[spans[i].Link] + spans[i].Along;
            for (int i = 0; i < plan.Stops.Count; i++)
            {
                var stop = plan.Stops[i];
                int segment = FloorIndex(distances, Wrap(linkStarts[stop.LinkIndex] + stop.DistanceAlongLink, Length));
                if (spans[segment].Stop >= 0) throw new InvalidOperationException("Coincident service positions on one bus line.");
                spans[segment].Stop = i;
            }
            for (int i = 0; i < count; i++)
                spans[i].Entry = spans[i].Stop >= 0 ? 0d : Math.Min(spans[i].Cap, spans[(i + count - 1) % count].Cap);
            // Two turns cover the seam: no lane or stop can require an instantaneous speed change.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < count; i++)
                {
                    Span current = spans[i], next = spans[(i + 1) % count];
                    next.Entry = Math.Min(next.Entry, Math.Sqrt(current.Entry * current.Entry + 2d * CityBusActor.Acceleration * current.Length));
                }
            for (int pass = 0; pass < 2; pass++)
                for (int i = count - 1; i >= 0; i--)
                {
                    Span current = spans[i], next = spans[(i + 1) % count];
                    current.Entry = Math.Min(current.Entry, Math.Sqrt(next.Entry * next.Entry + 2d * CityBusActor.ServiceDeceleration * current.Length));
                }
            var visits = new List<double>(plan.Stops.Count);
            var indices = new List<int>(plan.Stops.Count);
            double time = 0d;
            for (int i = 0; i < count; i++)
            {
                Span span = spans[i];
                times[i] = time;
                if (span.Stop >= 0)
                {
                    arrivals[span.Stop] = time;
                    visits.Add(time); indices.Add(span.Stop);
                }
                span.Exit = spans[(i + 1) % count].Entry;
                span.SetTiming();
                time += span.Departure + span.Travel;
            }
            Duration = time;
            serviceTimes = visits.ToArray();
            serviceStops = indices.ToArray();
        }

        internal static double Wrap(double value, double period)
        {
            double phase = value % period;
            return phase < 0 ? phase + period : phase;
        }

        internal double TimeAt(CityBusActor actor)
        {
            if (actor.CurrentStopIndex >= 0) return arrivals[actor.CurrentStopIndex] + actor.DwellElapsed;
            int served = actor.LastServedStopIndex;
            if (served >= 0)
            {
                var berth = plan.Stops[served];
                // Physical motion can finish braking within tolerance, then wait on
                // that same coordinate until the first departure step. It has already
                // served this berth, even if its nose is a centimetre short of the mark.
                if (berth.LinkIndex == actor.CurrentLinkIndex && actor.DistanceAlongLink <= berth.DistanceAlongLink &&
                    berth.DistanceAlongLink - actor.DistanceAlongLink <= CityBusActor.DistanceTolerance)
                    return arrivals[served] + CityBusActor.DwellDuration;
            }
            return TimeAt(actor.CurrentLinkIndex, actor.DistanceAlongLink);
        }

        internal double TimeAt(int link, float along)
        {
            double distance = Wrap(linkStarts[link] + along, Length);
            int i = FloorIndex(distances, distance);
            Span span = spans[i];
            if (span.Stop >= 0 && distance == distances[i])
                return Wrap(times[i] - 1e-7d, Duration);
            return times[i] + span.Departure + span.TimeAt(distance - distances[i]);
        }

        internal Sample At(double phase)
        {
            phase = Wrap(phase, Duration);
            int i = FloorIndex(times, phase);
            Span span = spans[i];
            double local = phase - times[i];
            if (span.Stop >= 0 && local < span.Departure)
            {
                var stop = plan.Stops[span.Stop];
                return new Sample(stop.LinkIndex, stop.DistanceAlongLink, 0f, span.Stop, (float)local, span.Stop);
            }
            span.SampleMotion(local - span.Departure, out float along, out float speed);
            // During the first centimetres of departure the physical tolerance still
            // considers us on the platform. Restore its serviced flag when rebinding.
            return new Sample(span.Link, along, speed, -1, 0f, span.Stop);
        }

        internal double Arrival(int stop) => arrivals[stop];

        internal double UntilService(double phase, int stop) => Wrap(arrivals[stop] - phase, Duration);

        internal double NextService(double phase, out int stop)
        {
            stop = -1;
            if (serviceTimes.Length == 0) return double.PositiveInfinity;
            phase = Wrap(phase, Duration);
            int i = Array.BinarySearch(serviceTimes, phase);
            i = i >= 0 ? i + 1 : ~i;
            bool wraps = i == serviceTimes.Length;
            if (wraps) i = 0;
            stop = serviceStops[i];
            return serviceTimes[i] - phase + (wraps ? Duration : 0d);
        }

        internal long Visits(double phase, double elapsed)
        {
            if (serviceTimes.Length == 0) return 0;
            double end = phase + elapsed;
            int before = UpperBound(serviceTimes, phase);
            int after = UpperBound(serviceTimes, Wrap(end, Duration));
            return (long)Math.Floor(end / Duration) * serviceTimes.Length + after - before;
        }

        private static int UpperBound(double[] values, double value)
        {
            int i = Array.BinarySearch(values, value);
            return i >= 0 ? i + 1 : ~i;
        }
        private static int FloorIndex(double[] values, double value) => Math.Max(0, UpperBound(values, value) - 1);
    }
}
