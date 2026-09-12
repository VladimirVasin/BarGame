using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>
    /// The one shape of an <c>initialize_phase</c> row: a composition root
    /// stops the phase timer it owns and reports the elapsed whole
    /// milliseconds under its own category. The caller restarts the timer
    /// for the next phase; this never does, so the rendered frame between
    /// two composition steps is charged to no phase at all.
    /// </summary>
    internal static class GameLogPhases
    {
        internal static void Report(
            string category,
            string phase,
            Stopwatch timer)
        {
            timer.Stop();
            GameLog.Debug(
                category,
                "initialize_phase",
                GameLog.Field("phase", phase),
                GameLog.Field("duration_ms", timer.ElapsedMilliseconds));
        }
    }
}
