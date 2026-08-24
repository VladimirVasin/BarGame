using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityWetSurfaceKind
    {
        Ground = 0,
        Road = 1,
        Sidewalk = 2,
        RoadMarking = 3,
        Puddle = 4
    }

    internal readonly struct CityWetSurfaceSample
    {
        public CityWetSurfaceSample(Color tint, float smoothness)
        {
            Tint = tint;
            Smoothness = smoothness;
        }

        public Color Tint { get; }
        public float Smoothness { get; }
    }

    /// <summary>
    /// Pure response curve shared by the City and the bounded exterior seen
    /// from Home. Rain arrives quickly, while the accumulated film dries much
    /// more slowly after a shower has passed.
    /// </summary>
    internal static class CityWetSurfaceRules
    {
        public const float WettingRatePerSecond = 0.58f;
        public const float DryingRatePerSecond = 0.028f;

        public static float Advance(
            float currentWetness,
            float rainIntensity,
            float deltaTime)
        {
            float current = Mathf.Clamp01(currentWetness);
            float target = Mathf.Clamp01(rainIntensity);
            float rate = target > current
                ? WettingRatePerSecond
                : DryingRatePerSecond;
            return Mathf.MoveTowards(
                current,
                target,
                Mathf.Max(0f, deltaTime) * rate);
        }

        public static CityWetSurfaceSample Evaluate(
            CityWetSurfaceKind kind,
            float wetness)
        {
            float amount = Mathf.Clamp01(wetness);
            ResolveRecipe(
                kind,
                out float drySmoothness,
                out float wetSmoothness,
                out Color wetTint);
            return new CityWetSurfaceSample(
                Color.Lerp(Color.white, wetTint, amount),
                Mathf.Lerp(drySmoothness, wetSmoothness, amount));
        }

        private static void ResolveRecipe(
            CityWetSurfaceKind kind,
            out float drySmoothness,
            out float wetSmoothness,
            out Color wetTint)
        {
            switch (kind)
            {
                case CityWetSurfaceKind.Ground:
                    drySmoothness =
                        CityExteriorAppearance.GroundSmoothness;
                    wetSmoothness = 0.34f;
                    wetTint = new Color(0.72f, 0.77f, 0.73f, 1f);
                    return;
                case CityWetSurfaceKind.Road:
                    drySmoothness = CityExteriorAppearance.RoadSmoothness;
                    wetSmoothness = 0.68f;
                    wetTint = new Color(0.56f, 0.63f, 0.64f, 1f);
                    return;
                case CityWetSurfaceKind.Sidewalk:
                    drySmoothness =
                        CityExteriorAppearance.SidewalkSmoothness;
                    wetSmoothness = 0.48f;
                    wetTint = new Color(0.69f, 0.75f, 0.74f, 1f);
                    return;
                case CityWetSurfaceKind.RoadMarking:
                    drySmoothness =
                        CityExteriorAppearance.RoadMarkingSmoothness;
                    wetSmoothness = 0.56f;
                    wetTint = new Color(0.75f, 0.80f, 0.78f, 1f);
                    return;
                case CityWetSurfaceKind.Puddle:
                    drySmoothness = CityExteriorAppearance.RoadSmoothness;
                    wetSmoothness = 0.92f;
                    wetTint = new Color(0.46f, 0.55f, 0.57f, 1f);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unknown City wet-surface kind.");
            }
        }
    }

    /// <summary>
    /// Applies the shared weather film through property blocks. The generated
    /// world keeps one shared material; registering a surface never creates a
    /// per-renderer material instance.
    /// </summary>
    internal static class CityWetSurfaceRegistry
    {
        private const float MinimumAppliedStep = 0.01f;

        private sealed class Entry
        {
            public Entry(
                Renderer renderer,
                CityWetSurfaceKind kind,
                Color dryTint)
            {
                Renderer = renderer;
                Kind = kind;
                DryTint = dryTint;
            }

            public Renderer Renderer { get; }
            public CityWetSurfaceKind Kind { get; set; }
            public Color DryTint { get; set; }
        }

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly MaterialPropertyBlock Properties =
            new MaterialPropertyBlock();

        private static float currentWetness;
        private static float lastAppliedWetness = -1f;
        private static bool hasWeatherState;
        private static double lastAbsoluteGameMinutes;

        public static float CurrentWetness => currentWetness;
        internal static int RegisteredSurfaceCount => Entries.Count;

        public static void Register(
            Renderer renderer,
            CityWetSurfaceKind kind)
        {
            RegisterCore(renderer, kind, null);
        }

        public static void Register(
            Renderer renderer,
            CityWetSurfaceKind kind,
            Color dryTint)
        {
            RegisterCore(renderer, kind, dryTint);
        }

        private static void RegisterCore(
            Renderer renderer,
            CityWetSurfaceKind kind,
            Color? authoredDryTint)
        {
            if (renderer == null)
            {
                return;
            }

            for (int index = Entries.Count - 1; index >= 0; index--)
            {
                Entry entry = Entries[index];
                if (entry.Renderer == null)
                {
                    Entries.RemoveAt(index);
                    continue;
                }

                if (entry.Renderer == renderer)
                {
                    entry.Kind = kind;
                    if (authoredDryTint.HasValue)
                    {
                        entry.DryTint = authoredDryTint.Value;
                    }

                    Apply(entry);
                    return;
                }
            }

            var added = new Entry(
                renderer,
                kind,
                authoredDryTint ?? ResolveDryTint(renderer));
            Entries.Add(added);
            Apply(added);
        }

        public static void SetImmediate(float wetness)
        {
            hasWeatherState = true;
            SetWetness(Mathf.Clamp01(wetness), true);
        }

        public static void InitializeOrResume(
            float rainIntensity,
            double absoluteGameMinutes)
        {
            ValidateGameMinutes(absoluteGameMinutes);
            if (!hasWeatherState ||
                absoluteGameMinutes < lastAbsoluteGameMinutes)
            {
                hasWeatherState = true;
                lastAbsoluteGameMinutes = absoluteGameMinutes;
                SetWetness(rainIntensity, true);
                return;
            }

            float elapsedSeconds = (float)(
                (absoluteGameMinutes - lastAbsoluteGameMinutes) /
                GameTimeState.GameMinutesPerRealSecond);
            lastAbsoluteGameMinutes = absoluteGameMinutes;
            SetWetness(
                CityWetSurfaceRules.Advance(
                    currentWetness,
                    rainIntensity,
                    elapsedSeconds));
        }

        public static void Advance(
            float rainIntensity,
            float deltaTime,
            double absoluteGameMinutes)
        {
            ValidateGameMinutes(absoluteGameMinutes);
            if (!hasWeatherState)
            {
                InitializeOrResume(rainIntensity, absoluteGameMinutes);
                return;
            }

            lastAbsoluteGameMinutes = Math.Max(
                lastAbsoluteGameMinutes,
                absoluteGameMinutes);
            SetWetness(
                CityWetSurfaceRules.Advance(
                    currentWetness,
                    rainIntensity,
                    deltaTime));
        }

        internal static void ResetForTests()
        {
            Entries.Clear();
            currentWetness = 0f;
            lastAppliedWetness = -1f;
            hasWeatherState = false;
            lastAbsoluteGameMinutes = 0d;
            Properties.Clear();
        }

        internal static void ResetForNewSession()
        {
            ResetForTests();
        }

        private static void SetWetness(
            float wetness,
            bool force = false)
        {
            float clamped = Mathf.Clamp01(wetness);
            currentWetness = clamped;
            bool reachedEndpoint = clamped <= 0f || clamped >= 1f;
            bool endpointNeedsApply =
                reachedEndpoint &&
                !Mathf.Approximately(clamped, lastAppliedWetness);
            if (!force &&
                !endpointNeedsApply &&
                Mathf.Abs(clamped - lastAppliedWetness) <
                MinimumAppliedStep)
            {
                return;
            }

            lastAppliedWetness = clamped;
            for (int index = Entries.Count - 1; index >= 0; index--)
            {
                Entry entry = Entries[index];
                if (entry.Renderer == null)
                {
                    Entries.RemoveAt(index);
                    continue;
                }

                Apply(entry);
            }
        }

        private static void Apply(Entry entry)
        {
            CityWetSurfaceSample sample =
                CityWetSurfaceRules.Evaluate(entry.Kind, currentWetness);
            Color displayedTint = Multiply(entry.DryTint, sample.Tint);
            Properties.Clear();
            entry.Renderer.GetPropertyBlock(Properties);
            Properties.SetColor(BaseColorId, displayedTint);
            Properties.SetColor(ColorId, displayedTint);
            Properties.SetFloat(SmoothnessId, sample.Smoothness);
            entry.Renderer.SetPropertyBlock(Properties);
        }

        private static Color ResolveDryTint(Renderer renderer)
        {
            Properties.Clear();
            renderer.GetPropertyBlock(Properties);
            if (Properties.HasProperty(BaseColorId))
            {
                return Properties.GetColor(BaseColorId);
            }

            Material material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(BaseColorId))
            {
                return material.GetColor(BaseColorId);
            }

            if (material != null && material.HasProperty(ColorId))
            {
                return material.GetColor(ColorId);
            }

            return Color.white;
        }

        private static Color Multiply(Color first, Color second)
        {
            return new Color(
                first.r * second.r,
                first.g * second.g,
                first.b * second.b,
                first.a * second.a);
        }

        private static void ValidateGameMinutes(double absoluteGameMinutes)
        {
            if (double.IsNaN(absoluteGameMinutes) ||
                double.IsInfinity(absoluteGameMinutes) ||
                absoluteGameMinutes < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(absoluteGameMinutes));
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            ResetForTests();
        }
    }
}
