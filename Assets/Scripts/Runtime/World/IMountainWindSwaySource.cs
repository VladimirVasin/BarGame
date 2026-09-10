namespace BarPromenade
{
    /// <summary>
    /// The one thing <see cref="MountainRoadWindDriver"/> needs from a weather
    /// model: how hard the crowns should be bending right now.
    ///
    /// It is deliberately NOT a member of <c>ICityWeatherShaper</c>. Only the
    /// two mountain areas grow anything that sways, and the city's own rain
    /// shaper has no meaning to give the number - it would have to invent one
    /// to satisfy an interface it never uses.
    /// </summary>
    public interface IMountainWindSwaySource
    {
        /// <summary>Current sway amplitude, in the units the wind shader reads.</summary>
        float SwayAmplitude { get; }
    }
}
