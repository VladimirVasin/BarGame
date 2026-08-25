namespace BarPromenade
{
    /// <summary>
    /// How far the one journey out of the city has got.
    ///
    /// One monotone ladder and nothing on it ever goes back down, the
    /// cemetery's own arrangement (<see cref="CemeteryGraveWorkStage"/>). The
    /// whole of what both areas build is a pure function of this single value:
    /// while it is <see cref="NotTaken"/> the car and the man stand on the last
    /// route island, and once it is <see cref="Arrived"/> they stand on the
    /// terrace by the cafe instead. There is never a copy of either in both
    /// places, and never one in neither.
    ///
    /// It survives scene loads because it lives on
    /// <see cref="GameSessionState"/>, which is what makes coming back to the
    /// mountain road later find him still waiting there.
    /// </summary>
    public enum LastRouteFerrymanRideStage
    {
        /// <summary>He is on the bonnet of his car on the island, throwing a
        /// coin, and has not been answered yet.</summary>
        NotTaken = 0,

        /// <summary>
        /// The hero is in the passenger seat and the car is between the two
        /// worlds - either still driving the city's streets, or inside the
        /// tunnel with the load already asked for. Nothing but the arrival
        /// itself reads this, and it exists so that a run interrupted mid-ride
        /// does not put a second car back on the island.
        /// </summary>
        InTransit = 1,

        /// <summary>Parked on the terminal apron with the man back on his
        /// bonnet. He does not drive again.</summary>
        Arrived = 2
    }
}
