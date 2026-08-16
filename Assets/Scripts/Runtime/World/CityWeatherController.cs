using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Samples the deterministic weather schedule every frame and drives the
    /// rain field, rain bed, lightning flash and thunder. Runs beside the
    /// day/night controller and deliberately leaves the exterior lighting
    /// contract untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityWeatherController : MonoBehaviour
    {
        private CityRainField rain;
        private CityRainSoundPlayer sound;
        private CityLightningFlashLight lightning;
        private CityThunderSoundPlayer thunder;
        private Func<bool> isSheltered;
        private bool hasAppliedSample;
        private long lastThunderStrikeId = long.MinValue;

        public bool IsInitialized { get; private set; }
        public WeatherVisualSample CurrentSample { get; private set; }
        public int WeatherApplicationCount { get; private set; }

        public void Initialize(
            CityRainField rainField,
            CityRainSoundPlayer soundPlayer,
            CityLightningFlashLight lightningFlash,
            CityThunderSoundPlayer thunderPlayer,
            Func<bool> shelterProvider)
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
            isSheltered = shelterProvider;
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
            WeatherVisualSample nextSample =
                GameWeatherRules.EvaluateCurrent();
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
            CityRiverResources.SetRainIntensity(
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
                thunder.PlayStrike(sample.DistanceFactor);
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

        private void UpdateShelter()
        {
            rain.SetSheltered(
                isSheltered != null && isSheltered());
        }

        private void Update()
        {
            ApplyCurrentWeather();
        }
    }
}
