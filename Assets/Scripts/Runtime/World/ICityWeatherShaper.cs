namespace BarPromenade
{
    /// <summary>
    /// A place that reads the one deterministic weather schedule differently
    /// from the city that wrote it.
    ///
    /// Mountain Road and Alpine Village both implement it: the road turns the
    /// slot into climbing snow, while the village keeps a permanent gale and
    /// very heavy snowfall without changing the shared bearing or timing. It
    /// is an optional hook on <see cref="CityWeatherController"/> rather than
    /// a second component beside it, and the reason is ordering: the
    /// controller already writes <c>CityClothWindRegistry</c> and the
    /// precipitation drift every frame, so anything that also wrote them
    /// would be a race decided by execution order. One writer, one hook, three
    /// named axes.
    ///
    /// The bearing is deliberately not shapeable. Cloth, snow and the swaying
    /// crowns have to agree on which way the wind is going, and that
    /// agreement is exactly what a second source of direction would break.
    /// </summary>
    public interface ICityWeatherShaper
    {
        WeatherVisualSample ShapePrecipitation(WeatherVisualSample sample);

        WindSample ShapeWind(WindSample wind);

        /// <summary>
        /// The target the persistent street film is driven toward, read
        /// from the raw schedule sample. A third axis, and apart from
        /// precipitation on purpose: what falls through an area's air is
        /// that area's alone, while <c>CityWetSurfaceRegistry</c> is one
        /// shared simulation the hero carries between scenes. The snow
        /// areas hand the schedule through untouched - a blizzard on the
        /// terrace must not soak the city's asphalt when he comes back
        /// down - and the city floors it at its drizzle, because the
        /// asphalt of this city never dries.
        /// </summary>
        float ShapeSurfaceWetness(WeatherVisualSample sample);
    }
}
