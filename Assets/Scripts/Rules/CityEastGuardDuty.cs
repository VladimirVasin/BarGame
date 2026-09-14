using System;

namespace BarPromenade
{
    public enum CityEastGuardDutyPhase { Watch, Walking, Inspect, Handoff }

    /// <summary>Only one guard leaves his station. A return must finish before the other can leave.</summary>
    public sealed class CityEastGuardDuty
    {
        public CityEastGuardDutyPhase Phase { get; private set; } = CityEastGuardDutyPhase.Watch;
        public int Patroller { get; private set; } = 1;
        public int Waypoint { get; private set; }
        public int CompletedPatrols { get; private set; }
        public double WaitRemaining { get; private set; } = 19d;
        public bool IsWalking(int index) => Phase == CityEastGuardDutyPhase.Walking && Patroller == index;
        public bool IsHome(int index) => Patroller != index ||
            Phase == CityEastGuardDutyPhase.Watch || Phase == CityEastGuardDutyPhase.Handoff;

        public void Advance(double seconds, bool held)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (held || seconds == 0d || Phase == CityEastGuardDutyPhase.Walking) return;
            WaitRemaining = Math.Max(0d, WaitRemaining - seconds);
            if (WaitRemaining > 0d) return;
            switch (Phase)
            {
                case CityEastGuardDutyPhase.Watch:
                    Waypoint = 1; Phase = CityEastGuardDutyPhase.Walking; break;
                case CityEastGuardDutyPhase.Inspect:
                    Waypoint = 3; Phase = CityEastGuardDutyPhase.Walking; break;
                case CityEastGuardDutyPhase.Handoff:
                    Patroller = 1 - Patroller;
                    Phase = CityEastGuardDutyPhase.Watch;
                    WaitRemaining = 22d + CompletedPatrols % 3 * 5d;
                    break;
            }
        }

        public void Arrive()
        {
            if (Phase != CityEastGuardDutyPhase.Walking)
                throw new InvalidOperationException("Only a walking guard can arrive at a patrol waypoint.");
            if (Waypoint == 2)
            {
                Phase = CityEastGuardDutyPhase.Inspect;
                WaitRemaining = Patroller == 0 ? 8d : 5d;
            }
            else if (Waypoint == 4)
            {
                CompletedPatrols++;
                Phase = CityEastGuardDutyPhase.Handoff;
                WaitRemaining = 6d;
            }
            else Waypoint++;
        }
    }
}
