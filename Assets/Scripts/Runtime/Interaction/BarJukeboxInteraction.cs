using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bar's coin jukebox — an interactive stub for now. Its panel
    /// and two glow tubes breathe independently while the theme plays;
    /// the interaction flash is composed over that same presentation so
    /// two components never fight over one renderer's property block.
    /// Actual track selection over <c>BarMusicPlayer</c> is a later pass.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarJukeboxInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string PromptKeyName = "interaction.jukebox";
        public const float FlashSeconds = 0.6f;
        public const int LightChannelCount = 3;
        public const float MinimumLightIntensity = 0.56f;
        public const float MaximumLightIntensity = 1.32f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        private Renderer[] lightRenderers = Array.Empty<Renderer>();
        private Color[] lightColors = Array.Empty<Color>();
        private float[] lightIntensities = Array.Empty<float>();
        private MaterialPropertyBlock properties;
        private BarMusicPlayer musicPlayer;
        private float presentationTime;
        private float flashRemaining;

        public int UseCount { get; private set; }
        public string PromptKey => PromptKeyName;
        public Vector3 InteractionPosition => transform.position;
        public IReadOnlyList<Renderer> LightRenderers => lightRenderers;
        public IReadOnlyList<float> LightIntensities => lightIntensities;
        public BarMusicPlayer MusicPlayer => musicPlayer;

        public void Initialize(
            Renderer configuredPanelRenderer,
            Color configuredPanelColor)
        {
            ConfigureLights(
                new[] { configuredPanelRenderer },
                new[] { configuredPanelColor });
        }

        public void Initialize(
            Renderer configuredPanelRenderer,
            Color configuredPanelColor,
            Renderer configuredLeftTubeRenderer,
            Renderer configuredRightTubeRenderer,
            Color configuredTubeColor)
        {
            ConfigureLights(
                new[]
                {
                    configuredPanelRenderer,
                    configuredLeftTubeRenderer,
                    configuredRightTubeRenderer
                },
                new[]
                {
                    configuredPanelColor,
                    configuredTubeColor,
                    configuredTubeColor
                });
        }

        public void BindMusic(BarMusicPlayer configuredMusicPlayer)
        {
            musicPlayer = configuredMusicPlayer;
        }

        public static float EvaluateLightIntensity(
            int channelIndex,
            float timeSeconds,
            float normalizedMusicGain)
        {
            if (channelIndex < 0 || channelIndex >= LightChannelCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channelIndex));
            }

            float speed;
            float phase;
            float accentSpeed;
            switch (channelIndex)
            {
                case 0:
                    speed = 1.65f;
                    phase = 0.25f;
                    accentSpeed = 3.7f;
                    break;
                case 1:
                    speed = 2.15f;
                    phase = 2.05f;
                    accentSpeed = 4.35f;
                    break;
                default:
                    speed = 2.55f;
                    phase = 4.3f;
                    accentSpeed = 4.9f;
                    break;
            }

            float wave = 0.5f +
                Mathf.Sin(timeSeconds * speed + phase) * 0.5f;
            float accentWave = 0.5f +
                Mathf.Sin(
                    timeSeconds * accentSpeed + phase * 0.61f) *
                0.5f;
            float accent = Mathf.Pow(accentWave, 5f);
            float shape = wave * 0.78f + accent * 0.22f;
            float pulse = Mathf.Lerp(
                0.72f,
                MaximumLightIntensity,
                shape);
            return Mathf.Lerp(
                MinimumLightIntensity,
                pulse,
                Mathf.Clamp01(normalizedMusicGain));
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return interactor != null &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            UseCount++;
            flashRemaining = FlashSeconds;
            RetroAudioService.EnsureInstalled()?.TryPlay(
                RetroSfxId.UiConfirm,
                transform.position);
            GameLog.Info(
                "bar",
                "jukebox_stub_used",
                GameLog.Field("use_count", UseCount));
        }

        private void Update()
        {
            AdvancePresentation(Time.unscaledDeltaTime);
        }

        public void AdvancePresentation(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (properties == null || lightRenderers.Length == 0)
            {
                return;
            }

            presentationTime += deltaTime;
            flashRemaining = Mathf.Max(
                0f,
                flashRemaining - deltaTime);
            float musicGain = musicPlayer != null
                ? musicPlayer.NormalizedGain
                : 1f;
            float interactionFlash =
                flashRemaining / FlashSeconds;

            for (int index = 0;
                 index < lightRenderers.Length;
                 index++)
            {
                Renderer renderer = lightRenderers[index];
                if (renderer == null)
                {
                    continue;
                }

                float intensity = EvaluateLightIntensity(
                    index,
                    presentationTime,
                    musicGain);
                if (index == 0)
                {
                    intensity += interactionFlash * 0.9f;
                }

                lightIntensities[index] = intensity;
                Color displayed = lightColors[index] * intensity;
                displayed.a = lightColors[index].a;
                properties.Clear();
                renderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, displayed);
                properties.SetColor(LegacyColorId, displayed);
                renderer.SetPropertyBlock(properties);
            }
        }

        private void ConfigureLights(
            Renderer[] configuredRenderers,
            Color[] configuredColors)
        {
            if (configuredRenderers == null ||
                configuredColors == null ||
                configuredRenderers.Length == 0 ||
                configuredRenderers.Length != configuredColors.Length ||
                configuredRenderers.Length > LightChannelCount)
            {
                throw new ArgumentException(
                    "The jukebox needs one to three paired light renderers.");
            }

            for (int index = 0;
                 index < configuredRenderers.Length;
                 index++)
            {
                if (configuredRenderers[index] == null)
                {
                    throw new ArgumentNullException(
                        nameof(configuredRenderers));
                }
            }

            lightRenderers = (Renderer[])configuredRenderers.Clone();
            lightColors = (Color[])configuredColors.Clone();
            lightIntensities = new float[lightRenderers.Length];
            properties = new MaterialPropertyBlock();
            for (int index = 0;
                 index < lightRenderers.Length;
                 index++)
            {
                properties.Clear();
                lightRenderers[index].GetPropertyBlock(properties);
                Color authored = properties.GetColor(BaseColorId);
                if (authored.a > 0f ||
                    authored.maxColorComponent > 0.0001f)
                {
                    lightColors[index] = authored;
                }
            }

            presentationTime = 0f;
            flashRemaining = 0f;
            AdvancePresentation(0f);
        }

        private void OnDisable()
        {
            flashRemaining = 0f;
            if (properties == null)
            {
                return;
            }

            for (int index = 0;
                 index < lightRenderers.Length;
                 index++)
            {
                Renderer renderer = lightRenderers[index];
                if (renderer == null)
                {
                    continue;
                }

                properties.Clear();
                renderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, lightColors[index]);
                properties.SetColor(LegacyColorId, lightColors[index]);
                renderer.SetPropertyBlock(properties);
                lightIntensities[index] = 1f;
            }
        }
    }
}
