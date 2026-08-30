namespace BarPromenade
{
    /// <summary>
    /// What on the dash the passenger is looking at. Two things answer him
    /// - the radio, split down the middle into its two knobs, and the
    /// glovebox lid - and everything else drawn on the dash is furniture.
    /// </summary>
    public enum LastRouteCarDashboardTarget
    {
        None = 0,
        RadioPower = 1,
        RadioTuning = 2,
        Glovebox = 3
    }
}
