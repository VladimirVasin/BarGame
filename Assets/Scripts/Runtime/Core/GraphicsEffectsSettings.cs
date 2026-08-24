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
        private const string AspectRatio43Key = "graphics.aspect_4_3";
        private const string VertexJitterKey = "graphics.vertex_jitter";

        private static bool loaded;
        private static bool depthOfFieldEnabled;
        private static bool intoxicationLensFxEnabled;
        private static bool ditherEnabled;
        private static bool scanlinesEnabled;
        private static bool aspectRatio43Enabled;
        private static bool vertexJitterEnabled;

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
        /// Rounds projected vertices onto the internal-resolution grid,
        /// the way a console with no sub-pixel precision did. Off by
        /// default: it changes how the whole world moves, so it is the
        /// player's to switch on.
        /// </summary>
        public static bool VertexJitterEnabled
        {
            get
            {
                EnsureLoaded();
                return vertexJitterEnabled;
            }
            set => Apply(
                ref vertexJitterEnabled,
                value,
                VertexJitterKey);
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
            // The 4:3 pillarbox is the one opt-in effect: the game is
            // authored widescreen, so the retro frame is a choice.
            aspectRatio43Enabled =
                ReadFlag(AspectRatio43Key, false);
            // The second opt-in. Vertex jitter is the loudest of the
            // retro effects - it moves every silhouette in the game - so
            // it is offered rather than imposed.
            vertexJitterEnabled =
                ReadFlag(VertexJitterKey, false);
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
