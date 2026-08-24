using UnityEngine;

namespace BarPromenade
{
    public static class GraphicsEffectsSettings
    {
        private const string DepthOfFieldKey = "graphics.dof";
        private const string IntoxicationLensFxKey =
            "graphics.intoxication_fx";
        private const string DitherKey = "graphics.dither";
        private const string ScanlinesKey = "graphics.scanlines";
        private const string RainOnLensKey = "graphics.rain_lens";
        private const string AspectRatio43Key = "graphics.aspect_4_3";
        private const string HighFrameRateKey = "graphics.frame_rate_60";

        private static bool loaded;
        private static bool depthOfFieldEnabled;
        private static bool intoxicationLensFxEnabled;
        private static bool ditherEnabled;
        private static bool scanlinesEnabled;
        private static bool rainOnLensEnabled;
        private static bool aspectRatio43Enabled;
        private static bool highFrameRateEnabled;

        public static int Version { get; private set; }

        public static bool DepthOfFieldEnabled
        {
            get
            {
                EnsureLoaded();
                return depthOfFieldEnabled;
            }
            set => Apply(
                ref depthOfFieldEnabled,
                value,
                DepthOfFieldKey);
        }

        public static bool IntoxicationLensFxEnabled
        {
            get
            {
                EnsureLoaded();
                return intoxicationLensFxEnabled;
            }
            set => Apply(
                ref intoxicationLensFxEnabled,
                value,
                IntoxicationLensFxKey);
        }

        public static bool DitherEnabled
        {
            get
            {
                EnsureLoaded();
                return ditherEnabled;
            }
            set => Apply(ref ditherEnabled, value, DitherKey);
        }

        public static bool ScanlinesEnabled
        {
            get
            {
                EnsureLoaded();
                return scanlinesEnabled;
            }
            set => Apply(ref scanlinesEnabled, value, ScanlinesKey);
        }

        public static bool RainOnLensEnabled
        {
            get
            {
                EnsureLoaded();
                return rainOnLensEnabled;
            }
            set => Apply(ref rainOnLensEnabled, value, RainOnLensKey);
        }

        public static bool AspectRatio43Enabled
        {
            get
            {
                EnsureLoaded();
                return aspectRatio43Enabled;
            }
            set => Apply(
                ref aspectRatio43Enabled,
                value,
                AspectRatio43Key);
        }

        /// <summary>
        /// Off is the period rate the game is dressed for and the
        /// default; on doubles it for players who would rather have the
        /// smoother motion. Both live in
        /// <see cref="BarPromenadeRuntimeBootstrap"/>, which owns the
        /// actual cap.
        /// </summary>
        public static bool HighFrameRateEnabled
        {
            get
            {
                EnsureLoaded();
                return highFrameRateEnabled;
            }
            set => Apply(
                ref highFrameRateEnabled,
                value,
                HighFrameRateKey);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            loaded = false;
            Version = 0;
        }

        internal static void ResetLoadedStateForTests()
        {
            Reset();
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            depthOfFieldEnabled = ReadFlag(DepthOfFieldKey);
            intoxicationLensFxEnabled =
                ReadFlag(IntoxicationLensFxKey);
            ditherEnabled = ReadFlag(DitherKey);
            scanlinesEnabled = ReadFlag(ScanlinesKey);
            rainOnLensEnabled = ReadFlag(RainOnLensKey);
            // The 4:3 pillarbox is the one opt-in effect: the game is
            // authored widescreen, so the retro frame is a choice.
            aspectRatio43Enabled =
                ReadFlag(AspectRatio43Key, false);
            // Likewise opt-in: the period rate is the default, and
            // doubling it is the player's call.
            highFrameRateEnabled =
                ReadFlag(HighFrameRateKey, false);
            loaded = true;
        }

        private static bool ReadFlag(
            string key,
            bool defaultEnabled = true)
        {
            return PlayerPrefs.GetInt(
                key,
                defaultEnabled ? 1 : 0) != 0;
        }

        private static void Apply(
            ref bool field,
            bool value,
            string key)
        {
            EnsureLoaded();
            if (field == value)
            {
                return;
            }

            field = value;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Version++;
            GameLog.Info(
                "graphics",
                "effect_toggled",
                GameLog.Field("effect", key),
                GameLog.Field("enabled", value));
        }
    }
}
