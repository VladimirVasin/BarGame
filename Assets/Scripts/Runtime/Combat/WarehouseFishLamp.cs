using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One corded practical, powered by the three committed warehouse finds.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseFishLamp : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private CombatTestRoot owner;
        private Renderer bulb;
        private MaterialPropertyBlock properties;
        private AudioClip crackle;
        private float baseIntensity;
        private float elapsed;
        private bool initialized;
        private bool subscribed;
        private bool audioPaused;

        public bool IsLit { get; private set; }
        public float CurrentFactor { get; private set; }
        public Light Light { get; private set; }
        public AudioSource Source { get; private set; }
        public int CracklePlayCount { get; private set; }

        public void Initialize(GameObject fixture, Light light, CombatTestRoot warehouse)
        {
            if (initialized) throw new InvalidOperationException("Warehouse fish lamp already initialized.");
            if (fixture == null || light == null || warehouse == null) throw new ArgumentNullException();
            owner = warehouse;
            Light = light;
            baseIntensity = light.intensity;
            foreach (Renderer candidate in fixture.GetComponentsInChildren<Renderer>(true))
                if (candidate.name.Contains("LampBulb") && candidate.name.EndsWith("__Lamp", StringComparison.Ordinal))
                {
                    bulb = candidate;
                    break;
                }
            if (bulb == null) throw new InvalidOperationException("Warehouse pendant lacks its authored bulb.");
            properties = new MaterialPropertyBlock();
            Source = light.gameObject.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            CityWorkAudio.Configure(Source, true, HomeSoundscape.BathroomLightCrackleCutoff, 8f, false);
            Source.minDistance = 1.05f;
            Source.volume = HomeSoundscape.BathroomLightCrackleVolume;
            initialized = true;
            Subscribe();
            RefreshCollected();
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
            if (!initialized) return;
            bool complete = AllSourcesCollected(owner.WarehousePlan);
            if (complete != IsLit)
            {
                IsLit = complete;
                elapsed = 0f;
                Source.Stop();
                audioPaused = false;
            }
            if (IsLit && crackle == null)
            {
                float[] samples = HomeSoundscapeSynthesis.GenerateBathroomLightCrackleSamples();
                crackle = AudioClip.Create("Warehouse lamp crackle", samples.Length, 1,
                    HomeSoundscapeSynthesis.SampleRate, false);
                crackle.SetData(samples, 0);
                Source.clip = crackle;
            }
            ApplyFactor(IsLit ? HomeBathroomLightFlicker.EvaluateFactor(elapsed) : 0f, false);
        }

        public static bool AllSourcesCollected(WarehouseArenaPlan plan)
        {
            if (plan == null || plan.Refrigerators.Count != 3) return false;
            // The plan owns stable source identities, independently of rooms and inventory use.
            for (int index = 0; index < plan.Refrigerators.Count; index++)
                if (!GameSessionState.IsWorldItemCollected(plan.Refrigerators[index].SourceId)) return false;
            return true;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        public void Advance(float deltaSeconds)
        {
            if (!initialized || !isActiveAndEnabled) return;
            bool suspended = GameTimeScaleRuntime.IsPaused || owner == null || !owner.isActiveAndEnabled;
            if (suspended)
            {
                if (!audioPaused) { Source.Pause(); audioPaused = true; }
                return;
            }
            if (audioPaused) { Source.UnPause(); audioPaused = false; }
            if (!IsLit) return;
            elapsed = Mathf.Repeat(elapsed + Mathf.Max(0f, deltaSeconds), HomeBathroomLightFlicker.CycleSeconds);
            ApplyFactor(HomeBathroomLightFlicker.EvaluateFactor(elapsed), true);
        }

        private void ApplyFactor(float factor, bool playCrackle)
        {
            bool changed = factor != CurrentFactor;
            bool lightEnabled = IsLit && isActiveAndEnabled && factor > 0f;
            if (!changed && Light.enabled == lightEnabled) return;
            CurrentFactor = factor;
            Light.enabled = lightEnabled;
            Light.intensity = baseIntensity * factor;
            // Stable intervals do not rewrite renderer properties or allocate material instances.
            if (changed || !IsLit)
            {
                bulb.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, Color.Lerp(new Color(.12f, .12f, .12f), Color.white, factor));
                properties.SetColor(EmissionColorId, Color.white * (1.4f * factor));
                bulb.SetPropertyBlock(properties);
            }
            if (changed && playCrackle && crackle != null)
            {
                Source.PlayOneShot(crackle);
                CracklePlayCount++;
            }
        }

        private void OnEnable()
        {
            if (!initialized) return;
            Subscribe();
            RefreshCollected();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (!initialized) return;
            Source.Stop();
            audioPaused = false;
            ApplyFactor(0f, false);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Source != null) { Source.Stop(); Source.clip = null; }
            if (crackle != null) Destroy(crackle);
        }
    }
}
