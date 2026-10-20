using UnityEngine;

namespace BarPromenade
{
    public static class GraphicsEffectsSettings
    {
        private const string CleanAntialiasingKey = "graphics.clean_aa";
        private const string CleanReflectionsKey = "graphics.clean_reflections";
        private const string CleanVolumeKey = "graphics.clean_volume";
        private const string CleanHalationKey = "graphics.clean_halation";
        private const string CleanDirectionalHighlightsKey = "graphics.clean_directional_highlights";
        private const string CleanBounceKey = "graphics.clean_bounce";
        private const string DepthOfFieldKey = "graphics.dof";
        private const string IntoxicationLensFxKey =
            "graphics.intoxication_fx";
        private const string AspectRatio43Key = "graphics.aspect_4_3";

        private static bool loaded;
        private static bool cleanAntialiasingEnabled;
        private static bool cleanReflectionsEnabled;
        private static bool cleanVolumeEnabled;
        private static bool cleanHalationEnabled;
        private static bool cleanDirectionalHighlightsEnabled;
        private static bool cleanBounceEnabled;
        private static bool depthOfFieldEnabled;
        private static bool intoxicationLensFxEnabled;
        private static bool aspectRatio43Enabled;

        public static int Version { get; private set; }

        public static bool CleanAntialiasingEnabled
        {
            get
            {
                EnsureLoaded();
                return cleanAntialiasingEnabled;
            }
            set => Apply(ref cleanAntialiasingEnabled, value, CleanAntialiasingKey);
        }

        public static bool CleanReflectionsEnabled
        {
            get { EnsureLoaded(); return cleanReflectionsEnabled; }
            set => Apply(ref cleanReflectionsEnabled, value, CleanReflectionsKey);
        }

        public static bool CleanVolumeEnabled
        {
            get { EnsureLoaded(); return cleanVolumeEnabled; }
            set => Apply(ref cleanVolumeEnabled, value, CleanVolumeKey);
        }

        public static bool CleanHalationEnabled
        {
            get { EnsureLoaded(); return cleanHalationEnabled; }
            set => Apply(ref cleanHalationEnabled, value, CleanHalationKey);
        }

        public static bool CleanDirectionalHighlightsEnabled
        {
            get { EnsureLoaded(); return cleanDirectionalHighlightsEnabled; }
            set => Apply(ref cleanDirectionalHighlightsEnabled, value, CleanDirectionalHighlightsKey);
        }

        public static bool CleanBounceEnabled
        {
            get { EnsureLoaded(); return cleanBounceEnabled; }
            set => Apply(ref cleanBounceEnabled, value, CleanBounceKey);
        }

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

            cleanAntialiasingEnabled = ReadFlag(CleanAntialiasingKey);
            cleanReflectionsEnabled = ReadFlag(CleanReflectionsKey);
            cleanVolumeEnabled = ReadFlag(CleanVolumeKey);
            cleanHalationEnabled = ReadFlag(CleanHalationKey);
            cleanDirectionalHighlightsEnabled = ReadFlag(CleanDirectionalHighlightsKey);
            cleanBounceEnabled = ReadFlag(CleanBounceKey);
            depthOfFieldEnabled = ReadFlag(DepthOfFieldKey);
            intoxicationLensFxEnabled =
                ReadFlag(IntoxicationLensFxKey);
            // The 4:3 pillarbox is the one opt-in effect: the game is
            // authored widescreen, so the aspect ratio is a choice.
            aspectRatio43Enabled =
                ReadFlag(AspectRatio43Key, false);
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
