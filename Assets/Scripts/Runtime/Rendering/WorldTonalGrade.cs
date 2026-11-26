using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>Neutral luminance gains in the gameplay camera's existing URP grading LUT.</summary>
    internal static class WorldTonalGrade
    {
        internal static ShadowsMidtonesHighlights Configure(
            VolumeProfile profile, float shadowGain, float midtoneGain, float highlightGain)
        {
            if (!profile.TryGet(out ShadowsMidtonesHighlights tones))
                tones = profile.Add<ShadowsMidtonesHighlights>();

            // Linear luminance after the room's contrast and colour filter.
            tones.shadowsStart.Override(.012f);
            tones.shadowsEnd.Override(.12f);
            tones.highlightsStart.Override(.45f);
            tones.highlightsEnd.Override(.9f);
            tones.shadows.Override(NeutralGain(shadowGain));
            tones.midtones.Override(NeutralGain(midtoneGain));
            tones.highlights.Override(NeutralGain(highlightGain));
            return tones;
        }

        internal static void SetStreetNight(ShadowsMidtonesHighlights tones, float nightFactor)
        {
            if (tones == null) return;
            // The darkest gaps between lamps keep unit gain through dusk and dawn.
            Vector4 midtones = NeutralGain(Mathf.Lerp(1f, 1.25f, nightFactor));
            if (tones.midtones.value.Equals(midtones)) return;
            tones.midtones.value = midtones;
            tones.highlights.value = NeutralGain(Mathf.Lerp(1f, .93f, nightFactor));
        }

        private static Vector4 NeutralGain(float gain)
        {
            // URP's trackball scales positive offsets by four, and negative offsets by one.
            float offset = gain - 1f;
            return new Vector4(1f, 1f, 1f, offset > 0f ? offset * .25f : offset);
        }
    }
}
