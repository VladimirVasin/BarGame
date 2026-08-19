using TMPro;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The typeface the plaque is cut in.
    ///
    /// It is built at runtime rather than committed as a generated
    /// atlas, and both halves of that matter. The source is a real
    /// `.ttf` with full Cyrillic, so anything a player can type has a
    /// letter — which a hand-drawn font could only promise for the
    /// characters somebody remembered to draw. And the atlas is
    /// dynamic, so glyphs are rasterized the moment they are asked
    /// for, which keeps the project's habit of generating its art
    /// instead of storing it.
    ///
    /// Roboto rather than a system font: it ships inside a Unity
    /// package this project already pulls, it is Apache-2.0 and so
    /// safe to redistribute, and it carries the whole Russian
    /// alphabet. Picking up `arial.ttf` off the machine would have
    /// been none of those things.
    /// </summary>
    public static class CemeteryPlaqueFont
    {
        public const string ResourcePath = "Fonts/Roboto-Regular";

        /// <summary>
        /// Big enough that the plate reads at the distance the camera
        /// stops at, small enough that one page of atlas holds a line
        /// of Russian without spilling.
        /// </summary>
        public const int SamplingPointSize = 48;
        public const int AtlasPadding = 5;
        public const int AtlasSize = 512;

        private static TMP_FontAsset cached;

        /// <summary>
        /// The font asset, made once and kept. Null when the source
        /// face is missing, and every caller treats that as "no board
        /// lettering" rather than throwing — a missing font is a
        /// blank plaque, not a broken grave.
        /// </summary>
        public static TMP_FontAsset Get()
        {
            if (cached != null)
            {
                return cached;
            }

            var face = Resources.Load<Font>(ResourcePath);
            if (face == null)
            {
                GameLog.Warning(
                    "city",
                    "grave_plaque_font_missing",
                    GameLog.Field("resource", ResourcePath));
                return null;
            }

            cached = TMP_FontAsset.CreateFontAsset(
                face,
                SamplingPointSize,
                AtlasPadding,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic);
            if (cached != null)
            {
                cached.name = "Grave Plaque Font";
            }

            return cached;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            cached = null;
        }
    }
}
