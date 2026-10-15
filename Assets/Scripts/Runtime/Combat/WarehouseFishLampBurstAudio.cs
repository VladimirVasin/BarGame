using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A prewarmed rising filament cue and sharp glass pop, audible from either stair.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseFishLampBurstAudio : MonoBehaviour
    {
        private const int SampleRate = 24000;
        private static AudioClip heating, burst;
        private static int leases;
        private bool leased, paused;
        public AudioSource Source { get; private set; }
        public int HeatingPlayCount { get; private set; }
        public int BurstPlayCount { get; private set; }

        public void Initialize(Transform anchor)
        {
            if (leased) return;
            if (leases == 0)
            {
                heating = MakeClip(false);
                burst = MakeClip(true);
            }
            leases++; leased = true;
            var host = new GameObject("Warehouse Filament And Glass");
            host.transform.SetParent(anchor, false);
            Source = host.AddComponent<AudioSource>(); Source.playOnAwake = false;
            CityWorkAudio.Configure(Source, true, 9200f, 32f, false);
            Source.minDistance = 7f; Source.volume = .9f; Source.priority = 48;
        }

        public void PlayHeating(float elapsed, bool fresh)
        {
            Source.Stop(); Source.clip = heating; Source.volume = .85f;
            Source.time = Mathf.Clamp(elapsed, 0f, heating.length - .002f);
            Source.Play();
            if (fresh) HeatingPlayCount++;
            if (paused || GameTimeScaleRuntime.IsPaused) Source.Pause();
        }

        public void PlayBurst()
        {
            Source.Stop(); Source.clip = burst; Source.volume = .96f; Source.Play(); BurstPlayCount++;
        }

        public void SetPaused(bool value)
        {
            if (paused == value || Source == null) return;
            paused = value;
            if (value) Source.Pause(); else Source.UnPause();
        }

        public void Stop() { if (Source != null) Source.Stop(); paused = false; }

        private static AudioClip MakeClip(bool pop)
        {
            float duration = pop ? .85f : WarehouseFishLampState.HeatingSeconds;
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            uint seed = pop ? 0x474C4153u : 0x48454154u;
            double phase = 0d;
            float low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = (float)i / SampleRate, progress = time / duration;
                seed = unchecked(seed * 1664525u + 1013904223u);
                float noise = (seed >> 8) * (2f / 16777215f) - 1f;
                low += .08f * (noise - low);
                float sample;
                if (!pop)
                {
                    phase += Mathf.Lerp(135f, 1900f, progress * progress) / SampleRate;
                    float hum = (float)(Math.Sin(phase * Math.PI * 2d) + .25d * Math.Sin(phase * Math.PI * 6d));
                    float accent = (noise * .38f + Mathf.Sin(time * 620f) * .48f) * Mathf.Exp(-time * 22f);
                    float envelope = Mathf.SmoothStep(.18f, .78f, progress) * Mathf.Min(1f, time / .008f);
                    sample = accent + envelope * (hum * .38f + (noise - low) * (.07f + progress * .17f));
                }
                else
                {
                    float crack = (noise * .72f + Mathf.Sin(time * 511f) * .4f) * Mathf.Exp(-time * 95f);
                    float ring = Mathf.Sin(time * 2f * Mathf.PI * 3293f) * .21f +
                        Mathf.Sin(time * 2f * Mathf.PI * 5147f) * .13f;
                    sample = crack + (ring + (noise - low) * .16f) * Mathf.Exp(-time * 12f);
                    for (int tick = 0; tick < 5; tick++)
                    {
                        float since = time - (.16f + tick * .092f);
                        if (since >= 0f) sample += (noise * .26f + Mathf.Sin(since * (17000f + tick * 1700f)) * .2f) * Mathf.Exp(-since * 75f);
                    }
                }
                samples[i] = Mathf.Clamp(sample * Mathf.Clamp01((duration - time) / .007f), -.93f, .93f);
            }
            AudioClip clip = AudioClip.Create(pop ? "Warehouse bulb glass burst" : "Warehouse filament rising overload",
                samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0); return clip;
        }

        private void OnDisable() => Stop();
        private void OnDestroy()
        {
            Stop();
            if (Source != null) { Source.clip = null; Destroy(Source.gameObject); }
            if (!leased) return;
            leased = false;
            if (--leases != 0) return;
            if (heating != null) Destroy(heating);
            if (burst != null) Destroy(burst);
            heating = burst = null;
        }
    }
}
