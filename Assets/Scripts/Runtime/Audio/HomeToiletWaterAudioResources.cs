using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared, deterministic water texture; no voice, musical bed or bodily effect.</summary>
    internal static class HomeToiletWaterAudioResources
    {
        private const int Rate = 24000;
        private static AudioClip entry, submerged;
        public static AudioClip Entry => entry != null ? entry : entry = CreateEntry();
        public static AudioClip Submerged => submerged != null ? submerged : submerged = CreateSubmerged();

        private static AudioClip CreateEntry()
        {
            var samples = new float[Mathf.RoundToInt(Rate * .72f)];
            uint seed = 0xB041u;
            float low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate;
                float cutoff = Mathf.Lerp(2600f, 280f, Mathf.Clamp01(t / .28f));
                low += (Noise(ref seed) - low) * (1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate));
                float splash = low * .85f * Mathf.Exp(-t * 9f);
                // A short irregular set of damped water resonances, not a repeating gag.
                float bubbles = Bubble(t - .025f, 680f, 290f, .19f) * .15f +
                    Bubble(t - .10f, 470f, 190f, .24f) * .12f +
                    Bubble(t - .21f, 340f, 155f, .29f) * .085f +
                    Bubble(t - .34f, 240f, 130f, .31f) * .045f;
                float edge = Mathf.Clamp01(Mathf.Min(i, samples.Length - 1 - i) / 192f);
                samples[i] = (splash + bubbles) * edge;
            }
            return Clip("Home Bowl Water Entry", samples);
        }

        private static float Bubble(float t, float startHz, float endHz, float duration)
        {
            if (t <= 0f || t >= duration) return 0f;
            float phase = 2f * Mathf.PI * (startHz * t + (endHz - startHz) * t * t / (2f * duration));
            float envelope = Mathf.Clamp01(t / .008f) * Mathf.Exp(-t * 15f) *
                Mathf.Clamp01((duration - t) / .03f);
            return Mathf.Sin(phase) * envelope;
        }

        private static AudioClip CreateSubmerged()
        {
            const int count = Rate * 3;
            const int seam = Rate / 8;
            var raw = new float[count + seam];
            uint seed = 0xD091u;
            float low = 0f, rumble = 0f;
            float lowAlpha = 1f - Mathf.Exp(-2f * Mathf.PI * 540f / Rate);
            float rumbleAlpha = 1f - Mathf.Exp(-2f * Mathf.PI * 65f / Rate);
            // Warm up the noise filters before the saved loop begins.
            for (int i = -Rate / 4; i < raw.Length; i++)
            {
                low += (Noise(ref seed) - low) * lowAlpha;
                rumble += (low - rumble) * rumbleAlpha;
                if (i >= 0) raw[i] = (low - rumble) * .85f;
            }
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                // Tail-to-head overlap preserves both value and slope at the loop boundary.
                float value = i < seam ? Mathf.Lerp(raw[count + i], raw[i],
                    Mathf.SmoothStep(0f, 1f, i / (float)seam)) : raw[i];
                float phase = 2f * Mathf.PI * i / count;
                samples[i] = value * (.83f + .10f * Mathf.Sin(phase) + .07f * Mathf.Sin(phase * 3f + .6f));
            }
            return Clip("Home Bowl Water Interior", samples);
        }

        private static float Noise(ref uint seed)
        {
            seed = unchecked(seed * 1664525u + 1013904223u);
            return ((seed >> 8) / 8388607.5f) - 1f;
        }

        private static AudioClip Clip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Destroy(entry);
            Destroy(submerged);
            entry = submerged = null;
        }

        private static void Destroy(AudioClip clip)
        {
            if (clip == null) return;
            if (Application.isPlaying) Object.Destroy(clip);
            else Object.DestroyImmediate(clip);
        }
    }
}
