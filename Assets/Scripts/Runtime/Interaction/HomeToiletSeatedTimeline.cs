using System;

namespace BarPromenade
{
    public enum HomeToiletSeatedPhase
    {
        Idle, OpeningLid, TurningToSeat, Preparing, Diving, Sitting,
        Seated, Rising, TurningToInspect, Inspecting, Flushing, VortexHold,
        Exiting, Dressing, TurningToLid, ClosingLid, Completed
    }

    /// <summary>
    /// One choreography owns the actor and camera. A phase's terminal sample
    /// must be presented before advancing; moving turns and camera return are
    /// completed by their constrained owners rather than a guessed timer.
    /// </summary>
    public sealed class HomeToiletSeatedTimeline
    {
        public const float SitStartsDuringDive = 1.8f;
        public const float EmissionStartsAt = .5f;
        public const float ReleaseAt = 1.05f;
        private bool externallyCompleted;
        private bool terminalPresented;
        private bool flushPresented;
        public HomeToiletSeatedPhase Phase { get; private set; }
        public float PhaseElapsed { get; private set; }
        public bool WasCancelled { get; private set; }
        public bool IsCompleted => Phase == HomeToiletSeatedPhase.Completed;
        public bool IsExternal => Phase == HomeToiletSeatedPhase.TurningToSeat ||
            Phase == HomeToiletSeatedPhase.TurningToLid || Phase == HomeToiletSeatedPhase.Exiting ||
            Phase == HomeToiletSeatedPhase.TurningToInspect || Phase == HomeToiletSeatedPhase.VortexHold;
        public bool AtEnd => IsExternal ? externallyCompleted : PhaseElapsed >= Duration;
        public bool CanAdvance => AtEnd && terminalPresented;
        public float Progress => Duration > 0f ? Math.Min(1f, PhaseElapsed / Duration) : (AtEnd ? 1f : 0f);
        public float Duration => Phase switch
        {
            HomeToiletSeatedPhase.OpeningLid => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.OpenLid),
            HomeToiletSeatedPhase.Preparing => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Prepare),
            HomeToiletSeatedPhase.Diving => HomeToiletPlungeTimeline.EnterSeconds,
            HomeToiletSeatedPhase.Sitting => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Sit) -
                (HomeToiletPlungeTimeline.EnterSeconds - SitStartsDuringDive),
            HomeToiletSeatedPhase.Seated => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Seated),
            HomeToiletSeatedPhase.Rising => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Rise),
            HomeToiletSeatedPhase.Inspecting => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Inspect),
            HomeToiletSeatedPhase.Flushing => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Flush),
            HomeToiletSeatedPhase.Dressing => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Dress),
            HomeToiletSeatedPhase.ClosingLid => HomeToiletActorPresentation.Duration(HomeToiletActorPhase.CloseLid),
            _ => 0f
        };

        public void Begin() { Reset(); MoveTo(HomeToiletSeatedPhase.OpeningLid); }

        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Phase == HomeToiletSeatedPhase.Idle || IsCompleted) return;
            float limit = Phase == HomeToiletSeatedPhase.Flushing && !flushPresented
                ? HomeToiletActorPresentation.FlushCueSeconds : Duration;
            PhaseElapsed = IsExternal ? PhaseElapsed + Math.Max(0f, seconds) :
                Math.Min(limit, PhaseElapsed + Math.Max(0f, seconds));
        }

        public void CompleteExternal() => externallyCompleted = true;
        public void MarkFlushPresented() => flushPresented = true;
        public void MarkPresented() { if (AtEnd) terminalPresented = true; }

        public bool RequestFinish()
        {
            if (WasCancelled || Phase == HomeToiletSeatedPhase.Idle ||
                Phase >= HomeToiletSeatedPhase.Rising) return false;
            WasCancelled = true;
            return true;
        }

        public void MoveTo(HomeToiletSeatedPhase phase)
        {
            Phase = phase;
            PhaseElapsed = 0f;
            externallyCompleted = terminalPresented = flushPresented = false;
        }

        public void Reset()
        {
            Phase = HomeToiletSeatedPhase.Idle;
            PhaseElapsed = 0f;
            WasCancelled = externallyCompleted = terminalPresented = flushPresented = false;
        }
    }
}
