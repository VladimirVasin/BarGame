namespace BarPromenade
{
    /// <summary>
    /// Everything the passenger can leave changed on the dash, as a value.
    ///
    /// It lives on <see cref="GameSessionState"/> rather than on the car
    /// because there are two cars: the city leg ends in a tunnel, the screen
    /// goes under, and the Mountain Road raises a NEW car from the ride
    /// stage. A radio switched on at the island has to still be on when the
    /// lights come back, and a lid left open has to still be open.
    /// </summary>
    public readonly struct LastRouteCarDashboardState
    {
        public LastRouteCarDashboardState(
            bool radioOn,
            int tuningDetent,
            bool gloveboxOpen)
        {
            RadioOn = radioOn;
            TuningDetent = LastRouteCarRadioModel.WrapDetent(tuningDetent);
            GloveboxOpen = gloveboxOpen;
        }

        public bool RadioOn { get; }
        public int TuningDetent { get; }
        public bool GloveboxOpen { get; }

        /// <summary>A new game: radio off, needle where the last owner left
        /// it, lid shut.</summary>
        public static LastRouteCarDashboardState Default =>
            new LastRouteCarDashboardState(
                false,
                LastRouteCarRadioModel.DefaultDetent,
                false);

        public LastRouteCarDashboardState WithRadioOn(bool radioOn)
        {
            return new LastRouteCarDashboardState(
                radioOn, TuningDetent, GloveboxOpen);
        }

        public LastRouteCarDashboardState WithTuningDetent(int tuningDetent)
        {
            return new LastRouteCarDashboardState(
                RadioOn, tuningDetent, GloveboxOpen);
        }

        public LastRouteCarDashboardState WithGloveboxOpen(bool gloveboxOpen)
        {
            return new LastRouteCarDashboardState(
                RadioOn, TuningDetent, gloveboxOpen);
        }
    }
}
