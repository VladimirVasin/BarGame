using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A refrigerator room's finite find owns its red entrance indicator.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseRoomIndicator : MonoBehaviour
    {
        public const float FadeDuration = 1.25f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color OffColor = new Color(.12f, .018f, .014f).linear;
        private static AudioClip shutdownClip;
        private static int clipOwners;
        private CombatTestRoot owner;
        private Renderer lens;
        private MaterialPropertyBlock properties;
        private Color baseColor, emission;
        private float baseIntensity, elapsed;
        private bool collected, initialized, subscribed;
        private bool shutdownPending, audioPaused;

        public string SourceId { get; private set; }
        public Light Light { get; private set; }
        public AudioSource Source { get; private set; }
        public int ShutdownPlayCount { get; private set; }
        public float FadeProgress => collected ? elapsed / FadeDuration : 0f;
        public float CurrentFactor { get; private set; } = -1f;

        public void Initialize(GameObject fixture, Light light, CombatTestRoot warehouse, string sourceId)
        {
            if (initialized) throw new InvalidOperationException("Warehouse room indicator already initialized.");
            if (fixture == null || light == null || warehouse == null || string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentNullException();
            owner = warehouse;
            Light = light;
            SourceId = sourceId;
            baseIntensity = light.intensity;
            foreach (Renderer candidate in fixture.GetComponentsInChildren<Renderer>(true))
                if (candidate.name.EndsWith("__SignalRed", StringComparison.Ordinal)) { lens = candidate; break; }
            if (lens == null) throw new InvalidOperationException("Warehouse room indicator lacks its authored lens.");
            baseColor = lens.sharedMaterial.GetColor(BaseColorId);
            emission = lens.sharedMaterial.GetColor(EmissionColorId);
            properties = new MaterialPropertyBlock();
            Source = light.gameObject.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            CityWorkAudio.Configure(Source, true, 3800f, 10f, false);
            Source.minDistance = 1f;
            Source.volume = .4f;
            collected = GameSessionState.IsWorldItemCollected(SourceId);
            elapsed = collected ? FadeDuration : 0f;
            initialized = true;
            clipOwners++;
            Subscribe();
            Apply();
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled) return;
            GameSessionState.WorldItemsChanged += RefreshCollected;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            GameSessionState.WorldItemsChanged -= RefreshCollected;
            subscribed = false;
        }

        private void RefreshCollected()
        {
            bool complete = GameSessionState.IsWorldItemCollected(SourceId);
            if (complete == collected) return;
            collected = complete;
            elapsed = 0f;
            Source.Stop();
            shutdownPending = complete;
            SynchronizeAudio();
            Apply();
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        public void Advance(float seconds)
        {
            if (!initialized || !isActiveAndEnabled) return;
            if (!SynchronizeAudio() || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            if (!collected || elapsed >= FadeDuration) return;
            elapsed = Mathf.Min(FadeDuration, elapsed + seconds);
            Apply();
            if (elapsed >= FadeDuration) Source.Stop();
        }

        private bool SynchronizeAudio()
        {
            bool running = owner != null && owner.isActiveAndEnabled && !GameTimeScaleRuntime.IsPaused;
            if (audioPaused == running)
            {
                if (running) Source.UnPause(); else Source.Pause();
                audioPaused = !running;
            }
            if (running && shutdownPending)
            {
                shutdownPending = false;
                if (shutdownClip == null) shutdownClip = CreateShutdownClip();
                Source.clip = shutdownClip;
                Source.Play();
                ShutdownPlayCount++;
            }
            return running;
        }

        private static AudioClip CreateShutdownClip()
        {
            int rate = HomeSoundscapeSynthesis.SampleRate;
            var samples = new float[Mathf.RoundToInt(rate * FadeDuration)];
            uint state = 0x4C414D50u;
            double phase = 0d, filamentPhase = 0d;
            for (int index = 0; index < samples.Length; index++)
            {
                float time = (float)index / rate;
                float progress = time / FadeDuration;
                state = unchecked(state * 1664525u + 1013904223u);
                float noise = (state >> 8) * (2f / 16777215f) - 1f;
                phase += Mathf.Lerp(174f, 38f, progress) / rate;
                filamentPhase += Mathf.Lerp(1190f, 140f, progress) / rate;
                float envelope = Mathf.Min(1f, time / .018f) * Mathf.Pow(1f - progress, .65f);
                float dyingHum = (float)Math.Sin(phase * Math.PI * 2d) * .48f +
                    (float)Math.Sin(filamentPhase * Math.PI * 2d) * .13f + noise * .06f;
                float tickTime = time - (FadeDuration - .055f);
                float tick = tickTime >= 0f ? noise * Mathf.Exp(-tickTime * 160f) * .6f : 0f;
                samples[index] = Mathf.Clamp(dyingHum * envelope + tick, -.85f, .85f);
            }
            AudioClip result = AudioClip.Create("Warehouse lamp dying power", samples.Length, 1, rate, false);
            result.SetData(samples, 0);
            return result;
        }

        private void Apply() => ApplyFactor(collected ? 1f - Mathf.SmoothStep(0f, 1f, FadeProgress) : 1f);

        private void ApplyFactor(float factor)
        {
            if (GameSessionState.WarehouseFishLamp.Phase == WarehouseFishLampPhase.Burst) factor = 0f;
            bool lightEnabled = isActiveAndEnabled && factor > 0f;
            if (CurrentFactor == factor && Light.enabled == lightEnabled) return;
            CurrentFactor = factor;
            Light.enabled = lightEnabled;
            Light.intensity = baseIntensity * factor;
            lens.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, Color.Lerp(OffColor, baseColor, factor));
            properties.SetColor(EmissionColorId, emission * factor);
            lens.SetPropertyBlock(properties);
        }

        private void OnEnable()
        {
            if (!initialized) return;
            bool complete = GameSessionState.IsWorldItemCollected(SourceId);
            if (complete != collected) { collected = complete; elapsed = complete ? FadeDuration : 0f; }
            Subscribe();
            Apply();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (!initialized) return;
            Source.Stop();
            shutdownPending = false;
            audioPaused = false;
            ApplyFactor(0f);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (!initialized) return;
            if (Source != null) { Source.Stop(); Source.clip = null; }
            if (--clipOwners == 0 && shutdownClip != null) { Destroy(shutdownClip); shutdownClip = null; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetClip()
        {
            if (shutdownClip != null) Destroy(shutdownClip);
            shutdownClip = null;
            clipOwners = 0;
        }
    }
}
