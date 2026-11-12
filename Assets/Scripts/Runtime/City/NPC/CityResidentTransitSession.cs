using System;

namespace BarPromenade
{
    /// <summary>Coordinates the resident calendar with the six physical services. A sleeping
    /// or unloaded City needs only its last state; on return we merge actual stop visits in
    /// chronological order, including transfers between lines.</summary>
    internal sealed class CityResidentTransitSession
    {
        internal sealed class Snapshot
        {
            internal int Seed;
            internal string Blueprint;
            internal double Minute;
            internal CityResidentJourneyDirector.Snapshot Residents;
            internal CityBusDirector.PhaseSnapshot[] Vehicles;
        }

        private static CityResidentTransitSession active;
        private static Snapshot saved;
        private readonly CityGameRoot city;
        private readonly double[] cursors;
        private readonly double[] arrivals;
        private double minute;
        private bool suspended;

        private static double Now => GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes;

        internal CityResidentTransitSession(CityGameRoot city)
        {
            this.city = city;
            cursors = new double[city.BusVehicles.Count];
            arrivals = new double[cursors.Length];
        }

        internal void Attach()
        {
            if (active == this) return;
            active = this;
            double now = Now;
            if (saved != null && saved.Seed == city.Layout.Seed &&
                saved.Blueprint == city.Layout.BlueprintId && saved.Vehicles.Length == cursors.Length)
            {
                minute = saved.Minute;
                city.ResidentJourneys.Restore(saved.Residents);
                for (int i = 0; i < cursors.Length; i++)
                    city.BusVehicles[i].RestorePhase(saved.Vehicles[i]);
                if (!suspended) CatchUp(now);
            }
            else
            {
                // On first entering an already inhabited city, reconstruct today's travel.
                // A new city opened before the clock starts keeps its exact paired start.
                minute = GameSessionState.IsGameTimeRunning ? Math.Floor(now / 1440d) * 1440d : now;
                city.ResidentJourneys.Reset(minute);
                CatchUp(now);
                city.ResidentJourneys.SeedKioskAtCityStart();
            }
            saved = null;
        }

        internal void Detach()
        {
            if (active != this) return;
            saved = Capture();
            active = null;
        }

        internal void Suspend()
        {
            if (active != this || suspended) return;
            Tick();
            // A boarding event can be newer than the last 5 Hz population tick.
            minute = Now;
            city.ResidentJourneys.Advance(minute);
            suspended = true;
        }

        internal void Resume()
        {
            if (active != this || !suspended) return;
            CatchUp(Now);
            suspended = false;
        }

        internal void Tick()
        {
            if (active != this || suspended) return;
            city.ResidentJourneys.RequireKioskWalkIn(city.Pedestrians.IsNearKioskCourt);
            double now = Now;
            double elapsed = now - minute;
            // A normal calendar sample is tiny even at the debug speed multiplier.
            // Explicit calendar jumps must release physical transfers before historical service.
            if (elapsed > 2d || elapsed < 0d)
            {
                if (city.VehiclePassengers != null)
                    foreach (var controller in city.VehiclePassengers) controller.ReleaseForTimeJump();
                city.Pedestrians.ReleaseResidentPresentationsForTimeJump();
                for (int i = 0; i < cursors.Length; i++)
                    if (!CarriesHero(i)) city.BusVehicles[i].PrepareForDetachedTravel();
                CatchUp(now);
            }
            else if (elapsed >= .05d)
            {
                city.ResidentJourneys.Advance(now);
                minute = now;
            }
        }

        private void CatchUp(double now)
        {
            city.ResidentJourneys.RequireKioskWalkIn(false);
            try { AdvanceDetachedJourneys(now); }
            finally { city.ResidentJourneys.RequireKioskWalkIn(city.Pedestrians.IsNearKioskCourt); }
        }

        private void AdvanceDetachedJourneys(double now)
        {
            if (now < minute)
            {
                city.ResidentJourneys.Reset(now);
                minute = now;
                return;
            }

            // All schedules repeat daily. Skip full old days analytically instead of
            // replaying an unbounded number of residents, stops or frames after a time jump.
            double from = Math.Max(minute, now - 1440d);
            if (from > minute)
            {
                double skippedSeconds = (from - minute) / GameTimeState.GameMinutesPerRealSecond;
                for (int i = 0; i < cursors.Length; i++)
                    if (!CarriesHero(i)) city.BusVehicles[i].AdvanceDetached(skippedSeconds, false);
                city.ResidentJourneys.Reset(from);
            }
            double duration = (now - from) / GameTimeState.GameMinutesPerRealSecond;
            for (int i = 0; i < cursors.Length; i++)
            {
                cursors[i] = 0d;
                var vehicle = city.BusVehicles[i];
                arrivals[i] = CarriesHero(i) ? double.PositiveInfinity : vehicle.SecondsUntilNextService;
            }

            while (true)
            {
                int first = -1;
                for (int i = 0; i < arrivals.Length; i++)
                    if (arrivals[i] <= duration && (first < 0 || arrivals[i] < arrivals[first])) first = i;
                if (first < 0) break;
                var vehicle = city.BusVehicles[first];
                double arrival = arrivals[first];
                int stop = vehicle.AdvanceToNextService();
                cursors[first] = arrival;
                city.ResidentJourneys.Advance(from + arrival * GameTimeState.GameMinutesPerRealSecond);
                city.ResidentJourneys.ServiceHiddenStop(first, stop);
                double next = vehicle.SecondsUntilNextService;
                if (!(next > 0d)) throw new InvalidOperationException("Detached bus service clock did not advance.");
                arrivals[first] = arrival + next;
            }

            for (int i = 0; i < cursors.Length; i++)
                if (!CarriesHero(i) && duration > cursors[i])
                    city.BusVehicles[i].AdvanceDetached(duration - cursors[i], false);
            city.ResidentJourneys.Advance(now);
            minute = now;
        }

        private bool CarriesHero(int vehicle) =>
            city.BusVehicles[vehicle].Actor?.HasPlayerPassenger == true ||
            city.BusRides?[vehicle] != null && city.BusRides[vehicle].State != CityBusRideState.Outside;

        private Snapshot Capture()
        {
            if (!suspended)
            {
                Tick();
                minute = Now;
                city.ResidentJourneys.Advance(minute);
                city.Pedestrians.RetainPresentedJourneys();
            }
            var vehicles = new CityBusDirector.PhaseSnapshot[cursors.Length];
            for (int i = 0; i < vehicles.Length; i++)
                vehicles[i] = city.BusVehicles[i].CapturePhase();
            return new Snapshot { Seed = city.Layout.Seed, Blueprint = city.Layout.BlueprintId,
                Minute = minute, Residents = city.ResidentJourneys.Capture(), Vehicles = vehicles };
        }

        internal static Snapshot CaptureCheckpoint() => active != null ? active.Capture() : saved;
        internal static void RestoreCheckpoint(Snapshot snapshot)
        {
            active = null;
            saved = snapshot;
        }
    }
}
