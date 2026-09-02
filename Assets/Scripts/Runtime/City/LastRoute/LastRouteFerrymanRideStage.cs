namespace BarPromenade
{
    /// <summary>
    /// Where the Ferryman and his car are, and which way they are going.
    ///
    /// It used to be a monotone ladder, the cemetery's own arrangement
    /// (<see cref="CemeteryGraveWorkStage"/>), because the journey out of the
    /// city was made once and never unmade. It is a RING now: he takes the
    /// hero up, waits on the terrace by the cafe for as long as he is left
    /// there, and takes him back down again if he is asked. What has not
    /// changed is the invariant the whole thing exists for - the whole of what
    /// both areas build is a pure function of this single value, so there is
    /// never a copy of either man or car in both places, and never one in
    /// neither.
    ///
    /// The two moving states are the ones nothing but an arrival reads. They
    /// exist so that a run interrupted mid-ride does not put a second car back
    /// at the end it left.
    ///
    /// It survives scene loads because it lives on
    /// <see cref="GameSessionState"/>, which is what makes coming back to the
    /// mountain road later find him still waiting there.
    /// </summary>
    public enum LastRouteFerrymanRideStage
    {
        /// <summary>He is on the bonnet of his car on the island, throwing a
        /// coin, waiting to be asked.</summary>
        NotTaken = 0,

        /// <summary>
        /// The hero is in the passenger seat and the car is climbing - either
        /// still driving the city's streets, or inside the tunnel with the
        /// load already asked for.
        /// </summary>
        InTransit = 1,

        /// <summary>Parked on the terminal apron with the man back on his
        /// bonnet, at the top of the road.</summary>
        Arrived = 2,

        /// <summary>
        /// The same again downhill: he has been asked a second time, the hero
        /// is back in the seat, and the car is between the terrace and the
        /// island.
        /// </summary>
        Returning = 3
    }
}
