using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The city's own weather shaper: it never stops raining here.
    ///
    /// The mountain receives the shared schedule as snow and the village as
    /// a capped flurry; the city receives it as rain that never ends. The
    /// slot grid survives untouched and is what keeps the rain ALIVE - a
    /// "Clear" slot is now the city's drizzle, and the schedule goes on
    /// deciding not whether it rains but how hard: drizzle, rain, downpour,
    /// storm. The decree is city-scene only by design; up the mountain a
    /// dry tunnel-mouth arrival in a quiet slot stays dry, and the village
    /// keeps its own weather.
    ///
    /// The kind is carried through untouched, the shapers' shared doctrine:
    /// it still names the slot the whole world is in, and the log saying
    /// `Clear` over a wet street means the same thing it means on the
    /// summit, where `Clear` snows.
    /// </summary>
    public sealed class CityEternalRainShaper : ICityWeatherShaper
    {
        /// <summary>
        /// The lightest rain the city ever sees - what a "Clear" slot falls
        /// as. Well under LightRain's `0.45` so the grid's variety stays
        /// legible, and far enough over the rain field's `0.005` visibility
        /// and the wipers' `0.02` threshold that a drizzle reads as weather
        /// rather than noise.
        /// </summary>
        public const float DrizzleIntensity = 0.18f;

        /// <summary>
        /// The decree as a pure function, for the readers that consume the
        /// schedule directly rather than through a weather controller - the
        /// bus's wipers, the balcony's view of the city, the river's
        /// build-time hook.
        /// </summary>
        public static float FloorIntensity(float rainIntensity)
        {
            return Mathf.Max(DrizzleIntensity, rainIntensity);
        }

        public WeatherVisualSample ShapePrecipitation(
            WeatherVisualSample sample)
        {
            return new WeatherVisualSample(
                sample.Kind,
                FloorIntensity(sample.RainIntensity));
        }

        /// <summary>
        /// The wind is not decreed. A drizzle in near-calm air is a real
        /// state of this sky; the heavier slots bring their own wind with
        /// them, exactly as before.
        /// </summary>
        public WindSample ShapeWind(WindSample wind)
        {
            return wind;
        }
    }
}
