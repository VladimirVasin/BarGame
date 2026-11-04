using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityResidentJourneyPhase { AtDestination, Walking, ApproachingStop, Waiting, Riding }

    public readonly struct CityResidentJourneyState
    {
        public readonly CityResidentJourneyPhase Phase;
        public readonly CityResidentActivity Activity;
        public readonly int Line, BoardStop, ExitStop, Vehicle, CurrentNode, DestinationNode, GoalNode, PlaceIndex;
        public readonly double ReadyAt;
        internal CityResidentJourneyState(CityResidentJourneyPhase phase, CityResidentActivity activity, int current, int destination, int goal,
            int line, int board, int exit, int vehicle, int place, double readyAt)
        {
            Phase = phase; Activity = activity; CurrentNode = current; DestinationNode = destination; GoalNode = goal;
            Line = line; BoardStop = board; ExitStop = exit; Vehicle = vehicle; PlaceIndex = place; ReadyAt = readyAt;
        }
    }

    /// <summary>One entry per permanent resident. A stop call is the only way
    /// to enter or leave a particular bus. Scene objects project these entries
    /// and confirm visible transfers; remote service uses the very same seats.</summary>
    public sealed class CityResidentJourneyDirector
    {
        private const int Capacity = CityBusActor.MaximumNpcOccupants;
        private const double WalkingMinutesPerMetre = GameTimeState.GameMinutesPerRealSecond / CityResidentWorld.WalkingSpeed;
        private readonly CityResidentWorld world;
        private readonly CityResidentTransitPlanner planner;
        private readonly Resident[] residents = new Resident[CityResidentLifeRules.ResidentCount];
        private readonly bool[] presented = new bool[CityResidentLifeRules.ResidentCount];
        private readonly CityResidentWorld.Route[] walks = new CityResidentWorld.Route[CityResidentLifeRules.ResidentCount];
        private readonly Dictionary<(int from, int to), CityResidentWorld.Route> paths =
            new Dictionary<(int, int), CityResidentWorld.Route>();
        private readonly int[,] seats;
        private double now = double.NaN;
        private double nextEvent;

        public CityResidentJourneyDirector(CityResidentWorld world, IReadOnlyList<CityBusPlan> lines,
            IReadOnlyList<CityBusStopWaitPlan> waitPlans)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (lines == null || waitPlans == null || waitPlans.Count != lines.Count)
                throw new ArgumentException("Each line needs its own wait plan.");
            planner = new CityResidentTransitPlanner(world, lines, waitPlans);
            seats = new int[lines.Count * 2, Capacity];
            ClearSeats();
            world.Journeys = this;
        }

        public CityResidentJourneyState GetState(int id)
        {
            ref Resident r = ref residents[id];
            bool transit = r.Phase == CityResidentJourneyPhase.ApproachingStop ||
                r.Phase == CityResidentJourneyPhase.Waiting || r.Phase == CityResidentJourneyPhase.Riding;
            return new CityResidentJourneyState(r.Phase, r.Schedule.Activity, r.From, r.WalkTo, r.Goal,
                transit ? r.Leg.Line : -1, transit ? r.Leg.Board : -1, transit ? r.Leg.Exit : -1,
                r.Phase == CityResidentJourneyPhase.Riding ? r.Vehicle : -1,
                r.Phase == CityResidentJourneyPhase.Riding ? CityBusActor.NpcPlaceIndices[r.Slot] : -1, r.ArrivesAt);
        }

        public void SetStreetPresented(int id, bool visible)
        {
            presented[id] = visible;
            if (!visible) nextEvent = Math.Min(nextEvent, Deadline(id));
        }

        public bool IsStreetPresented(int id) => presented[id];
        public int RiderAt(int vehicle, int slot) => seats[vehicle, slot];

        /// <summary>Initial placement or a deliberate time discontinuity. The
        /// caller can resume at an earlier cutover and feed actual historical
        /// stop calls up to the current minute before revealing the city.</summary>
        public void Reset(double absoluteMinute)
        {
            now = absoluteMinute;
            ClearSeats();
            Array.Clear(presented, 0, presented.Length);
            for (int id = 0; id < residents.Length; id++)
            {
                var schedule = Schedule(id, now);
                if (!world.HasPavement)
                {
                    residents[id] = new Resident { Schedule = schedule, Phase = CityResidentJourneyPhase.AtDestination,
                        From = -1, WalkTo = -1, Goal = -1 };
                    continue;
                }
                int from = world.Destination(id, schedule.PreviousActivity);
                residents[id] = default;
                BeginJourney(id, from, schedule, schedule.StartedAt);
            }
            UpdateDueResidents();
        }

        public void Advance(double absoluteMinute)
        {
            if (double.IsNaN(absoluteMinute) || double.IsInfinity(absoluteMinute) || absoluteMinute < 0d)
                throw new ArgumentOutOfRangeException(nameof(absoluteMinute));
            if (double.IsNaN(now) || absoluteMinute < now || absoluteMinute - now > 1440d)
            {
                Reset(absoluteMinute);
                return;
            }
            now = absoluteMinute;
            if (now >= nextEvent) UpdateDueResidents();
        }

        private void UpdateDueResidents()
        {
            nextEvent = double.PositiveInfinity;
            if (!world.HasPavement) return;
            for (int id = 0; id < residents.Length; id++)
            {
                double due;
                while ((due = Deadline(id)) <= now)
                {
                    ref Resident r = ref residents[id];
                    if (r.Phase == CityResidentJourneyPhase.Waiting || r.Phase == CityResidentJourneyPhase.AtDestination)
                        BeginJourney(id, r.From, Schedule(id, due), due);
                    else
                        FinishWalk(id, due);
                }
                nextEvent = Math.Min(nextEvent, due);
            }
        }

        private double Deadline(int id)
        {
            ref Resident r = ref residents[id];
            switch (r.Phase)
            {
                case CityResidentJourneyPhase.Riding:
                    return double.PositiveInfinity;
                case CityResidentJourneyPhase.AtDestination:
                    return r.Schedule.EndsAt;
                case CityResidentJourneyPhase.Waiting:
                    // A physical waiter may be part way through the doorway.
                    // Let the local controller finish its atomic transfer.
                    return presented[id] ? double.PositiveInfinity : r.Schedule.EndsAt;
                default:
                    return presented[id] ? double.PositiveInfinity : r.ArrivesAt;
            }
        }

        public bool MarkAtStop(int id)
        {
            if (residents[id].Phase != CityResidentJourneyPhase.ApproachingStop) return false;
            FinishWalk(id, now);
            return true;
        }

        public bool MarkWalkArrived(int id)
        {
            if (residents[id].Phase != CityResidentJourneyPhase.Walking) return false;
            FinishWalk(id, now);
            return true;
        }

        public int NextWaiting(int vehicle, int stop, bool excludePresented = false)
        {
            int best = -1;
            double since = double.PositiveInfinity;
            for (int i = 0; i < residents.Length; i++)
            {
                ref Resident r = ref residents[i];
                if (r.Phase != CityResidentJourneyPhase.Waiting || r.Leg.Line != vehicle / 2 ||
                    r.Leg.Board != stop || excludePresented && presented[i] || r.ArrivesAt >= since) continue;
                best = i;
                since = r.ArrivesAt;
            }
            return best;
        }

        public bool TryBoard(int id, int vehicle, int stop, int requestedSlot = -1)
        {
            ref Resident r = ref residents[id];
            if (r.Phase != CityResidentJourneyPhase.Waiting || r.Leg.Line != vehicle / 2 ||
                r.Leg.Board != stop || requestedSlot < -1 || requestedSlot >= Capacity) return false;
            // A visible doorway transfer keeps the place chosen when it began.
            // Background service takes the first free seat, then a rail station.
            int first = requestedSlot < 0 ? 0 : requestedSlot;
            int end = requestedSlot < 0 ? Capacity : requestedSlot + 1;
            for (int slot = first; slot < end; slot++)
            {
                if (seats[vehicle, slot] >= 0) continue;
                seats[vehicle, slot] = id;
                r.Slot = slot;
                r.Phase = CityResidentJourneyPhase.Riding;
                r.Vehicle = vehicle;
                r.WalkTo = planner.Node(r.Leg.Line, r.Leg.Exit);
                return true;
            }
            return false;
        }

        public bool TryAlight(int id, int vehicle, int stop)
        {
            ref Resident r = ref residents[id];
            if (r.Phase != CityResidentJourneyPhase.Riding || r.Vehicle != vehicle || r.Leg.Exit != stop)
                return false;
            seats[vehicle, r.Slot] = -1;
            int from = planner.Node(r.Leg.Line, stop);
            if (r.HasTransfer)
            {
                r.Leg = r.Transfer;
                r.HasTransfer = false;
                BeginWalk(id, from, planner.Node(r.Leg.Line, r.Leg.Board), now, true);
            }
            else BeginWalk(id, from, r.Goal, now, false);
            // A zero-length connection can board a different bus at this same minute.
            if (r.From == r.WalkTo) FinishWalk(id, now);
            return true;
        }

        public void ServiceHiddenStop(int vehicle, int stop)
        {
            for (int slot = 0; slot < Capacity; slot++)
            {
                int id = seats[vehicle, slot];
                if (id >= 0) TryAlight(id, vehicle, stop);
            }
            for (int slot = 0; slot < Capacity; slot++)
            {
                if (seats[vehicle, slot] >= 0) continue;
                int next = NextWaiting(vehicle, stop, true);
                if (next < 0) break;
                TryBoard(next, vehicle, stop);
            }
        }

        internal bool TrySample(int id, double minute, out CityResidentWorld.StreetSample sample)
        {
            ref Resident r = ref residents[id];
            if ((r.Phase == CityResidentJourneyPhase.Walking || r.Phase == CityResidentJourneyPhase.ApproachingStop) &&
                walks[id] != null)
                return world.SampleRoute(walks[id], Math.Max(0d, minute - r.StartedAt) / WalkingMinutesPerMetre,
                    r.Prefix, false, r.Schedule, out sample);
            sample = default;
            return false;
        }

        internal void RetainWalk(int id, CityPedestrianActor actor, double minute)
        {
            ref Resident r = ref residents[id];
            if (r.Phase != CityResidentJourneyPhase.Walking && r.Phase != CityResidentJourneyPhase.ApproachingStop) return;
            var route = world.BuildRoute(actor.TargetNodeIndex, r.WalkTo);
            route.StartPrevious = actor.PreviousNodeIndex;
            walks[id] = route;
            r.From = actor.TargetNodeIndex;
            r.Previous = actor.PreviousNodeIndex;
            r.Prefix = actor.Position;
            r.StartedAt = minute;
            r.ArrivesAt = minute + (route.Length + world.PrefixLength(route, actor.Position)) * WalkingMinutesPerMetre;
            nextEvent = Math.Min(nextEvent, r.ArrivesAt);
        }

        private void BeginJourney(int id, int from, CityResidentLifeSample schedule, double start)
        {
            ref Resident r = ref residents[id];
            r.Schedule = schedule;
            r.Goal = world.Destination(id, schedule.Activity);
            CityResidentItinerary trip = planner.Plan(from, r.Goal);
            r.Leg = trip.First;
            r.Transfer = trip.Second;
            r.HasTransfer = trip.LegCount == 2;
            BeginWalk(id, from, trip.LegCount == 0 ? r.Goal : planner.Node(r.Leg.Line, r.Leg.Board),
                start, trip.LegCount > 0);
        }

        private void BeginWalk(int id, int from, int to, double start, bool toBus)
        {
            ref Resident r = ref residents[id];
            r.Phase = toBus ? CityResidentJourneyPhase.ApproachingStop : CityResidentJourneyPhase.Walking;
            r.From = from;
            r.WalkTo = to;
            r.Vehicle = -1;
            r.Prefix = null;
            r.StartedAt = start;
            var walk = CachedPath(from, to);
            walks[id] = walk;
            r.ArrivesAt = start + walk.Length * WalkingMinutesPerMetre;
            nextEvent = Math.Min(nextEvent, r.ArrivesAt);
        }

        private CityResidentWorld.Route CachedPath(int from, int to)
        {
            if (!paths.TryGetValue((from, to), out var walk))
            {
                walk = world.BuildRoute(from, to);
                paths.Add((from, to), walk);
            }
            return walk;
        }

        private void FinishWalk(int id, double at)
        {
            ref Resident r = ref residents[id];
            r.From = r.WalkTo;
            r.Prefix = null;
            walks[id] = null;
            if (at >= r.Schedule.EndsAt)
            {
                BeginJourney(id, r.From, Schedule(id, at), at);
                return;
            }
            if (r.Phase == CityResidentJourneyPhase.ApproachingStop)
            {
                r.Phase = CityResidentJourneyPhase.Waiting;
                r.ArrivesAt = at;
            }
            else
            {
                // Public errands/walks use the existing out-and-back circuit
                // after the first leg, including a journey that ended by bus.
                bool publicVisit = r.Schedule.Activity == CityResidentActivity.Walking ||
                    r.Schedule.Activity == CityResidentActivity.Shopping;
                bool worker = CityResidentLifeRules.HasWorkSchedule(id);
                if (worker && r.Schedule.Activity == CityResidentActivity.Shopping) publicVisit = false;
                int other = worker && publicVisit ? world.LocalWalkNode(r.Goal) :
                    world.Destination(id, CityResidentActivity.Home);
                if (publicVisit && other != r.Goal)
                {
                    BeginWalk(id, r.From, r.From == other ? r.Goal : other, at, false);
                    // No service happens on these local walks. Skip complete
                    // round trips analytically when the calendar jumps forward.
                    double pairMinutes = 2d * (r.ArrivesAt - at);
                    if (!presented[id] && pairMinutes > 0d)
                    {
                        double skip = Math.Floor((Math.Min(now, r.Schedule.EndsAt) - at) / pairMinutes) * pairMinutes;
                        r.StartedAt += skip;
                        r.ArrivesAt += skip;
                    }
                    return;
                }
                r.Phase = CityResidentJourneyPhase.AtDestination;
            }
            nextEvent = Math.Min(nextEvent, Deadline(id));
        }

        private CityResidentLifeSample Schedule(int id, double minute)
        {
            int day = (int)Math.Floor(minute / CityResidentLifeRules.MinutesPerDay);
            return world.Schedule(id, day, minute - day * (double)CityResidentLifeRules.MinutesPerDay);
        }

        private void ClearSeats()
        {
            for (int i = 0; i < seats.GetLength(0); i++)
            for (int s = 0; s < Capacity; s++)
                seats[i, s] = -1;
        }

        internal struct Resident
        {
            internal CityResidentLifeSample Schedule;
            internal CityResidentJourneyPhase Phase;
            internal CityResidentBusLeg Leg, Transfer;
            internal bool HasTransfer;
            internal int From, WalkTo, Goal, Vehicle, Slot, Previous;
            internal Vector3? Prefix;
            internal double StartedAt, ArrivesAt;
        }

        // A checkpoint contains only compact value records and IDs. No transforms,
        // graph/model references or mutable aliases can survive scene teardown.
        public sealed class Snapshot
        {
            internal readonly Resident[] Residents;
            internal readonly int[,] Seats;
            public double Minute { get; }
            internal Snapshot(Resident[] residents, int[,] seats, double minute)
            { Residents = (Resident[])residents.Clone(); Seats = (int[,])seats.Clone(); Minute = minute; }
        }

        public Snapshot Capture() => new Snapshot(residents, seats, now);

        public void Restore(Snapshot snapshot)
        {
            if (snapshot == null || snapshot.Seats.GetLength(0) != seats.GetLength(0) || snapshot.Seats.GetLength(1) > Capacity)
                throw new ArgumentException("Snapshot belongs to another city fleet.", nameof(snapshot));
            Array.Copy(snapshot.Residents, residents, residents.Length);
            // A snapshot with fewer available places keeps its original occupants
            // in those places; added cabin stations begin empty on every vehicle.
            ClearSeats();
            for (int vehicle = 0; vehicle < seats.GetLength(0); vehicle++)
            for (int slot = 0; slot < snapshot.Seats.GetLength(1); slot++)
            {
                int resident = snapshot.Seats[vehicle, slot];
                seats[vehicle, slot] = resident;
                if (resident >= 0) residents[resident].Slot = slot;
            }
            Array.Clear(presented, 0, presented.Length);
            now = snapshot.Minute;
            nextEvent = double.PositiveInfinity;
            for (int id = 0; id < residents.Length; id++)
            {
                ref Resident r = ref residents[id];
                walks[id] = null;
                if (r.Phase == CityResidentJourneyPhase.Walking || r.Phase == CityResidentJourneyPhase.ApproachingStop)
                {
                    walks[id] = r.Prefix.HasValue ? world.BuildRoute(r.From, r.WalkTo) : CachedPath(r.From, r.WalkTo);
                    if (r.Prefix.HasValue) walks[id].StartPrevious = r.Previous;
                }
                nextEvent = Math.Min(nextEvent, Deadline(id));
            }
        }
    }
}
