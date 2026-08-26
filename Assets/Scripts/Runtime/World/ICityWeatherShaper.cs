namespace BarPromenade
{
    /// <summary>
    /// A place that reads the one deterministic weather schedule differently
    /// from the city that wrote it.
    ///
    /// There is exactly one implementation and it is the mountain road, where
    /// the same slot falls as snow and blows harder the higher you stand. It
    /// is an optional hook on <see cref="CityWeatherController"/> rather than
    /// a second component beside it, and the reason is ordering: the
    /// controller already writes <c>CityClothWindRegistry</c> and the
    /// precipitation drift every frame, so anything that also wrote them
    /// would be a race decided by execution order. One writer, one hook, two
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
    }
}
