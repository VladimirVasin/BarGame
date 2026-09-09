using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two bounded local voices, sounded only by an actual household contact.</summary>
    public sealed class VillageLifeAudio : MonoBehaviour
    {
        private AudioClip wood;
        private AudioClip scrape;
        private AudioClip cloth;
        private AudioClip hinge;
        private AudioSource[] sources;
        private AudioLowPassFilter[] filters;
        private int next;

        public void Initialize()
        {
            const int sampleRate = 22050;
            var samples = new float[4851];
            uint random = 0x73561243u;
            for (int i = 0; i < samples.Length; i++)
            {
                random = random * 1664525u + 1013904223u;
                float noise = ((random >> 8) / 16777215f) * 2f - 1f;
                float t = i / (float)sampleRate;
                float attack = Mathf.Clamp01(t / 0.003f);
                float body = Mathf.Sin(t * 2f * Mathf.PI * 287f) * 0.35f +
                    Mathf.Sin(t * 2f * Mathf.PI * 461f) * 0.18f;
                samples[i] = Mathf.Round((body * Mathf.Exp(-t * 30f) +
                    noise * 0.22f * Mathf.Exp(-t * 65f)) * attack * 127f) / 127f;
            }
            wood = AudioClip.Create("Village basket wood contact", samples.Length, 1, sampleRate, false);
            wood.SetData(samples, 0);
            var scrapeSamples = new float[14332];
            float filtered = 0f;
            for (int i = 0; i < scrapeSamples.Length; i++)
            {
                random = random * 1664525u + 1013904223u;
                float noise = ((random >> 8) / 16777215f) * 2f - 1f;
                filtered = Mathf.Lerp(filtered, noise, .18f);
                float phase = i / (float)scrapeSamples.Length;
                scrapeSamples[i] = filtered * Mathf.Sin(phase * Mathf.PI) * .65f;
            }
            scrape = AudioClip.Create("Village shovel through snow", scrapeSamples.Length, 1, sampleRate, false);
            scrape.SetData(scrapeSamples, 0);
            var clothSamples = new float[6615];
            filtered = 0f;
            for (int i = 0; i < clothSamples.Length; i++)
            {
                random = random * 1664525u + 1013904223u;
                float noise = ((random >> 8) / 16777215f) * 2f - 1f;
                filtered = Mathf.Lerp(filtered, noise, .09f);
                clothSamples[i] = filtered * Mathf.Sin(Mathf.PI * i / clothSamples.Length) * .42f;
            }
            cloth = AudioClip.Create("Village folded cloth", clothSamples.Length, 1, sampleRate, false);
            cloth.SetData(clothSamples, 0);
            var hingeSamples = new float[11025];
            float hingePhase = 0f;
            for (int i = 0; i < hingeSamples.Length; i++)
            {
                float t = i / (float)hingeSamples.Length;
                hingePhase += (190f + 65f * Mathf.Sin(t * Mathf.PI)) * 2f * Mathf.PI / sampleRate;
                float grain = Mathf.Sin(hingePhase) * .14f + Mathf.Sin(hingePhase * 2.03f) * .035f;
                hingeSamples[i] = grain * Mathf.Sin(t * Mathf.PI) * (0.65f + .35f * Mathf.Sin(t * 41f));
            }
            hinge = AudioClip.Create("Village working hinge", hingeSamples.Length, 1, sampleRate, false);
            hinge.SetData(hingeSamples, 0);
            sources = new AudioSource[2];
            filters = new AudioLowPassFilter[2];
            for (int i = 0; i < sources.Length; i++)
            {
                var host = new GameObject("Household contact " + i);
                host.transform.SetParent(transform, false);
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1.4f;
                source.maxDistance = 9f;
                source.volume = 0.3f;
                source.priority = 174;
                GameAudioMixer.Route(source, GameAudioGroup.AmbienceDetails);
                sources[i] = source;
                filters[i] = host.AddComponent<AudioLowPassFilter>();
                filters[i].cutoffFrequency = 18000f;
            }
        }

        public void PlayWood(Vector3 position, float gain = 1f)
        {
            if (wood == null) return;
            AudioSource source = sources[next++ % sources.Length];
            source.transform.position = position;
            source.PlayOneShot(wood, gain);
        }

        public void PlayScrape(Vector3 position)
        {
            if (scrape == null) return;
            AudioSource source = sources[next++ % sources.Length];
            source.transform.position = position;
            source.PlayOneShot(scrape);
        }

        public void PlayCloth(Vector3 position)
        {
            if (cloth == null) return;
            AudioSource source = sources[next++ % sources.Length];
            source.transform.position = position;
            source.PlayOneShot(cloth);
        }

        public void PlayHinge(Vector3 position)
        {
            if (hinge == null) return;
            AudioSource source = sources[next++ % sources.Length];
            source.transform.position = position;
            source.PlayOneShot(hinge);
        }

        public void SetRoomAcoustics(bool listenerInside, float doorOpen)
        {
            if (sources == null) return;
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i].volume = .3f * (listenerInside ? 1f : Mathf.Lerp(.24f, .68f, doorOpen));
                filters[i].cutoffFrequency = listenerInside ? 18000f : Mathf.Lerp(1300f, 4800f, doorOpen);
            }
        }

        private void OnDestroy()
        {
            if (wood != null) Destroy(wood);
            if (scrape != null) Destroy(scrape);
            if (cloth != null) Destroy(cloth);
            if (hinge != null) Destroy(hinge);
        }
    }
}
