using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Samples the deterministic weather schedule every frame and drives the
    /// rain field, rain bed, lightning flash and thunder. Runs beside the
    /// day/night controller and deliberately leaves the exterior lighting
    /// contract untouched.
    ///
    /// It may also be handed the area's drifting fog sheets. The fog is not
    /// weather - it is the same at every hour and in every slot - but it is
    /// cleared by the same roof that gives the rain its dry core, and an area
    /// should have exactly one owner of the question "is he under something".
    /// The City keeps its own owner - the tunnel shelter controller, which
    /// also hides the ridge shell - and passes no fog here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityWeatherController : MonoBehaviour
    {
        private CityRainField rain;
        private CityFogField fog;
        private CityRainSoundPlayer sound;
        private CityLightningFlashLight lightning;
        private CityThunderSoundPlayer thunder;
        private Transform listener;
        private Func<bool> isSheltered;
        private ICityWeatherShaper shaper;
        private bool hasAppliedSample;
        private long lastThunderStrikeId = long.MinValue;

        public bool IsInitialized { get; private set; }
        public WeatherVisualSample CurrentSample { get; private set; }

        /// <summary>
        /// The wind actually applied this frame, after any shaping. Read it
        /// rather than re-evaluating the schedule, or a place that shapes its
        /// weather will disagree with itself.
        /// </summary>
        public WindSample CurrentWind { get; private set; }

        public int WeatherApplicationCount { get; private set; }
        public float SurfaceWetness =>
            CityWetSurfaceRegistry.CurrentWetness;

        public void Initialize(
            CityRainField rainField,
            CityRainSoundPlayer soundPlayer,
            CityLightningFlashLight lightningFlash,
            CityThunderSoundPlayer thunderPlayer,
            Transform listenerTransform,
            Func<bool> shelterProvider,
            ICityWeatherShaper weatherShaper = null,
            CityFogField fogField = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The weather controller is already initialized.");
            }

            rain = rainField != null
                ? rainField
                : throw new ArgumentNullException(nameof(rainField));
            sound = soundPlayer;
            lightning = lightningFlash;
            thunder = thunderPlayer;
            listener = listenerTransform != null
                ? listenerTransform
                : throw new ArgumentNullException(
                    nameof(listenerTransform));
            isSheltered = shelterProvider;
            shaper = weatherShaper;
            fog = fogField;
            IsInitialized = true;
            ApplyCurrentWeather(true);
        }

        public void ApplyCurrentWeather(bool force = false)
        {
            if (!IsInitialized)
            {
                return;
            }

            ApplyLightning();
            ApplyWind();
            WeatherVisualSample scheduleSample =
                GameWeatherRules.EvaluateCurrent();
            WeatherVisualSample nextSample = scheduleSample;
            if (shaper != null)
            {
                nextSample = shaper.ShapePrecipitation(nextSample);
            }

            // Area shapers own what falls through that area's air, not the
            // persistent street-film simulation. Otherwise the village's
            // permanent blizzard would report almost full rain to the shared
            // wet-surface registry and carry it back into a clear City slot.
            ApplyWetSurfaces(scheduleSample, force);
            bool kindChanged =
                !hasAppliedSample ||
                CurrentSample.Kind != nextSample.Kind;
            if (!force &&
                hasAppliedSample &&
                CurrentSample.IsVisuallyEquivalentTo(nextSample))
            {
                UpdateShelter();
                return;
            }

            CurrentSample = nextSample;
            hasAppliedSample = true;
            rain.SetIntensity(nextSample.RainIntensity);
            CityWaterResources.SetRainIntensity(
                nextSample.RainIntensity);
            if (sound != null)
            {
                sound.SetIntensity(nextSample.RainIntensity);
            }

            UpdateShelter();
            WeatherApplicationCount++;
            if (kindChanged)
            {
                GameLog.Info(
                    "weather",
                    "weather_changed",
                    GameLog.Field(
                        "kind",
                        nextSample.Kind.ToString()),
                    GameLog.Field(
                        "rain_intensity",
                        nextSample.RainIntensity),
                    GameLog.Field(
                        "day_index",
                        GameSessionState.GameDayIndex),
                    GameLog.Field(
                        "minute_of_day",
                        GameSessionState.GameMinuteOfDay));
            }
        }

        private void ApplyWind()
        {
            // Gusts vary while the weather sample stays constant, so
            // wind runs before the visual-equivalence early-out, like
            // lightning.
            WindSample wind = GameWeatherRules.EvaluateCurrentWind();
            if (shaper != null)
            {
                wind = shaper.ShapeWind(wind);
            }

            CurrentWind = wind;
            CityClothWindRegistry.SetWind(wind);
            Vector3 velocity = wind.Velocity(
                GameWeatherRules.WindSpeedAtFullStrength);
            rain.SetWindDrift(
                new Vector2(velocity.x, velocity.z));
        }

        private void ApplyLightning()
        {
            if (lightning == null)
            {
                return;
            }

            // A frozen clock (pre-wake or pause) would otherwise hold a
            // bright flash on screen indefinitely.
            bool timeFlowing =
                GameSessionState.IsGameTimeRunning &&
                Time.timeScale > 0f;
            LightningSample sample = timeFlowing
                ? GameWeatherRules.EvaluateCurrentLightning()
                : LightningSample.None;
            lightning.Apply(sample);
            if (!sample.IsFlashing ||
                sample.StrikeId == lastThunderStrikeId)
            {
                return;
            }

            lastThunderStrikeId = sample.StrikeId;
            if (thunder != null)
            {
                thunder.PlayStrike(
                    sample.DistanceFactor,
                    sample.AzimuthDegrees,
                    listener.position);
            }

            GameLog.Debug(
                "weather",
                "lightning_strike",
                GameLog.Field("strike_id", sample.StrikeId),
                GameLog.Field(
                    "distance_factor",
                    sample.DistanceFactor),
                GameLog.Field(
                    "azimuth",
                    sample.AzimuthDegrees));
        }

        private static void ApplyWetSurfaces(
            WeatherVisualSample sample,
            bool force)
        {
            double absoluteGameMinutes =
                (GameSessionState.GameDayIndex *
                 GameTimeDayNightRules.MinutesPerDay) +
                GameSessionState.GameTimeOfDayMinutes;
            if (force)
            {
                CityWetSurfaceRegistry.InitializeOrResume(
                    sample.RainIntensity,
                    absoluteGameMinutes);
                return;
            }

            CityWetSurfaceRegistry.Advance(
                sample.RainIntensity,
                GameSessionState.IsGameTimeRunning
                    ? Time.deltaTime
                    : 0f,
                absoluteGameMinutes);
        }

        private void UpdateShelter()
        {
            bool sheltered = isSheltered != null && isSheltered();
            rain.SetSheltered(sheltered);
            if (fog != null)
            {
                fog.SetSheltered(sheltered);
            }
        }

        private void Update()
        {
            ApplyCurrentWeather();
        }
    }
}
