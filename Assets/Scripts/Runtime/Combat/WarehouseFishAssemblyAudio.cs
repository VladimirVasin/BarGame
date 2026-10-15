using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two bounded world voices; the activity's retained timeline owns every cue.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseFishAssemblyAudio : MonoBehaviour
    {
        private const int SampleRate = 24000;
        private static readonly float[] ContactTimes = { .17f, .51f, .94f, 1.37f, 1.81f, 2.23f, 2.69f, 3.14f, 3.59f };
        private static AudioClip strainClip, impactClip, deathClip, spitClip;
        private static int leases;
        private AudioSource strain, effect;
        private CombatTestRoot owner;
        private bool initialized, leased, paused, finaleActive, spitPlayed;
        private float lastFinaleTime, lastImpactTime = float.NegativeInfinity;

        public AudioSource StrainSource => strain;
        public AudioSource EffectSource => effect;

        public void Initialize(Transform sourcePosition, CombatTestRoot warehouse = null)
        {
            if (initialized) return;
            if (sourcePosition == null) throw new ArgumentNullException(nameof(sourcePosition));
            owner = warehouse;
            try
            {
                AcquireClips(); leased = true;
                strain = CreateVoice(sourcePosition, "Fish Strain", 3400f, .54f);
                effect = CreateVoice(sourcePosition, "Fish Contacts", 4600f, .7f);
                initialized = true;
            }
            catch
            {
                ClearVoices();
                if (leased) { leased = false; ReleaseClips(); }
                throw;
            }
        }

        private static AudioSource CreateVoice(Transform anchor, string name, float cutoff, float volume)
        {
            var host = new GameObject(name);
            host.transform.SetParent(anchor, false);
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            CityWorkAudio.Configure(source, true, cutoff, 12f, false);
            source.minDistance = .7f;
            source.volume = volume;
            source.priority = 88;
            return source;
        }

        public void PlayImpact(float strength = .5f)
        {
            if (!CanPlay || !Finite(strength) || strength <= .02f || Time.unscaledTime - lastImpactTime < .055f) return;
            lastImpactTime = Time.unscaledTime;
            PlayEffect(impactClip, Mathf.Lerp(.2f, .85f, Mathf.Clamp01(strength)),
                Mathf.Lerp(.88f, 1.12f, Mathf.Clamp01(strength)));
        }

        /// <summary>Re-entry seeks to the retained point and never replays earlier contacts.</summary>
        public void BeginConvulsions(float elapsed)
        {
            if (!initialized || !Finite(elapsed)) return;
            elapsed = Mathf.Clamp(elapsed, 0f, WarehouseFishAssemblyState.ConvulsionSeconds + WarehouseFishAssemblyState.SpitSeconds);
            finaleActive = true;
            lastFinaleTime = elapsed;
            spitPlayed = elapsed >= WarehouseFishAssemblyState.ConvulsionSeconds + .35f;
            strain.Stop();
            strain.clip = strainClip;
            if (elapsed >= WarehouseFishAssemblyState.ConvulsionSeconds) return;
            strain.time = Mathf.Min(elapsed, strainClip.length - .001f);
            strain.Play();
            if (paused || GameTimeScaleRuntime.IsPaused) strain.Pause();
        }

        public void SetFinaleTime(float elapsed)
        {
            if (!initialized || !Finite(elapsed)) return;
            elapsed = Mathf.Clamp(elapsed, 0f, WarehouseFishAssemblyState.ConvulsionSeconds + WarehouseFishAssemblyState.SpitSeconds);
            if (!finaleActive || elapsed < lastFinaleTime)
            { BeginConvulsions(elapsed); return; }
            float previous = lastFinaleTime;
            lastFinaleTime = elapsed;
            if (!CanPlay) return;
            if (previous < WarehouseFishAssemblyState.ConvulsionSeconds && elapsed >= WarehouseFishAssemblyState.ConvulsionSeconds)
            {
                strain.Stop();
                PlayEffect(deathClip, .62f, 1f);
            }
            else if (elapsed < WarehouseFishAssemblyState.ConvulsionSeconds)
            {
                // A hitch can cross many contacts. Only the current one may sound.
                for (int index = ContactTimes.Length - 1; index >= 0; index--)
                    if (previous < ContactTimes[index] && elapsed >= ContactTimes[index])
                    {
                        if (elapsed - ContactTimes[index] < .2f)
                            PlayEffect(impactClip, .42f + .06f * (index % 3), .9f + .08f * (index % 2));
                        break;
                    }
            }
        }

        public void PlaySpit()
        {
            if (!CanPlay || spitPlayed) return;
            spitPlayed = true;
            PlayEffect(spitClip, .78f, 1f);
        }

        private bool CanPlay => initialized && isActiveAndEnabled && !paused && !GameTimeScaleRuntime.IsPaused &&
            (owner == null || owner.isActiveAndEnabled);

        private void PlayEffect(AudioClip clip, float volume, float pitch)
        {
            // Replacing the one effect voice bounds native playback even under repeated collision callbacks.
            effect.Stop();
            effect.clip = clip;
            effect.volume = volume;
            effect.pitch = pitch;
            effect.Play();
        }

        public void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            if (!initialized) return;
            if (paused) { strain.Pause(); effect.Pause(); }
            else { strain.UnPause(); effect.UnPause(); }
        }

        public void Stop()
        {
            if (strain != null) strain.Stop();
            if (effect != null) effect.Stop();
            finaleActive = spitPlayed = paused = false;
            lastFinaleTime = 0f;
            lastImpactTime = float.NegativeInfinity;
        }

        private static void AcquireClips()
        {
            if (leases == 0)
            {
                try
                {
                    strainClip = MakeClip("Fish wet strain", WarehouseFishAssemblyState.ConvulsionSeconds, 0);
                    impactClip = MakeClip("Fish flesh contact", .24f, 1);
                    deathClip = MakeClip("Fish dying hiss", .5f, 2);
                    spitClip = MakeClip("Fish cough and key contact", .9f, 3);
                }
                catch { DestroyClips(); throw; }
            }
            leases++;
        }

        private static void ReleaseClips()
        {
            if (leases > 0 && --leases == 0) DestroyClips();
        }

        private static void DestroyClips()
        {
            if (strainClip != null) Destroy(strainClip);
            if (impactClip != null) Destroy(impactClip);
            if (deathClip != null) Destroy(deathClip);
            if (spitClip != null) Destroy(spitClip);
            strainClip = impactClip = deathClip = spitClip = null;
        }

        private static AudioClip MakeClip(string name, float seconds, int kind)
        {
            var samples = new float[Mathf.CeilToInt(seconds * SampleRate)];
            uint seed = 0x6D27A9B3u + (uint)kind * 997u;
            float lowNoise = 0f, dampNoise = 0f;
            double phase = 0d;
            for (int index = 0; index < samples.Length; index++)
            {
                float time = (float)index / SampleRate;
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                float noise = (seed >> 8) * (2f / 16777215f) - 1f;
                lowNoise += .045f * (noise - lowNoise);
                dampNoise += .19f * (noise - dampNoise);
                float sample;
                if (kind == 0)
                {
                    float remaining = Mathf.Clamp01((seconds - time) / .65f);
                    float wetPulse = .1f;
                    for (int beat = 0; beat < ContactTimes.Length; beat++)
                    {
                        float since = time - ContactTimes[beat];
                        if (since >= -.045f && since < .23f)
                            wetPulse += Mathf.Exp(-Mathf.Abs(since) * 18f) * (.7f + .12f * (beat % 3));
                    }
                    float frequency = 71f + lowNoise * 160f + 31f * Mathf.Sin(time * 23.7f) + 17f * Mathf.Sin(time * 11.3f);
                    phase += frequency / SampleRate;
                    float gurgle = (float)(Math.Sin(phase * Math.PI * 2d) + .31d * Math.Sin(phase * Math.PI * 6d));
                    float rattle = (noise - dampNoise) * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(time * 131f + lowNoise)), 5f);
                    float hiss = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - 3.1f) / .7f));
                    sample = (gurgle * .20f * wetPulse + lowNoise * 1.2f * wetPulse + rattle * .13f +
                        dampNoise * .38f * hiss) * remaining * Mathf.Clamp01(time / .025f);
                }
                else if (kind == 1)
                {
                    float attack = Mathf.Clamp01(time / .003f);
                    float wet = lowNoise * 1.6f + dampNoise * .47f;
                    float thump = Mathf.Sin(time * 2f * Mathf.PI * (92f - 75f * time));
                    sample = attack * (wet * Mathf.Exp(-time * 31f) + thump * .39f * Mathf.Exp(-time * 26f));
                }
                else if (kind == 2)
                {
                    float pressure = Mathf.Sin(Mathf.Clamp01(time / seconds) * Mathf.PI);
                    sample = (dampNoise * .72f + lowNoise * .9f +
                        Mathf.Sin(time * Mathf.PI * (148f - time * 160f)) * .1f) * pressure * Mathf.Exp(-time * 4f);
                }
                else
                {
                    float cough = Mathf.Sin(Mathf.Clamp01(time / .23f) * Mathf.PI) * Mathf.Exp(-time * 9f);
                    sample = (lowNoise * 1.6f + dampNoise * .5f + Mathf.Sin(time * 491f) * .12f) * cough;
                    float contact = time - .48f;
                    if (contact >= 0f)
                    {
                        float metal = Mathf.Sin(contact * 2f * Mathf.PI * 1783f) * .27f +
                            Mathf.Sin(contact * 2f * Mathf.PI * 2731f) * .15f +
                            Mathf.Sin(contact * 2f * Mathf.PI * 4127f) * .07f;
                        sample += (metal + noise * .12f * Mathf.Exp(-contact * 150f)) * Mathf.Exp(-contact * 24f);
                    }
                }
                float tail = Mathf.Clamp01((seconds - time) / .012f);
                samples[index] = Mathf.Clamp(sample * tail, -.93f, .93f);
            }
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void OnDisable() => Stop();
        private void OnDestroy()
        {
            Stop();
            ClearVoices();
            if (leased) { leased = false; ReleaseClips(); }
        }

        private void ClearVoices()
        {
            if (strain != null) { strain.clip = null; Destroy(strain.gameObject); }
            if (effect != null) { effect.clip = null; Destroy(effect.gameObject); }
            strain = effect = null;
        }
    }
}
