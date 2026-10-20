using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused Alpine Village native lighting and lifecycle acceptance.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageNativeLighting()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousCaptureDelta = Time.captureDeltaTime;
            AlpineVillageRoot village = null;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test village lighting");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                SetVillageNativeEffects(true);
                yield return Capture(SceneIds.AlpineVillage,
                    () =>
                    {
                        village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                        return village != null && village.IsInitialized ? village : null;
                    },
                    () => VillageNativeLightingShots(village));

                // Each A/B pair above renders synchronously without yielding:
                // clock, moving snow, camera and world geometry are identical.
                AssertVillageLightingDifference("Test village - Station baseline", "Test village - Station directional light");
                AssertVillageLightingDifference("Test village - Spring baseline", "Test village - Spring reflection");
                AssertVillageLightingDifference("Test village - Night baseline", "Test village - Night native lighting");
                AssertVillageLightingDifference("Test village - Window air baseline", "Test village - Window air and snow");
                yield return VerifyVillageLightingRouting(village);
                SceneReflectionCapture reflection = village.Lighting.Reflection;
                double tickMilliseconds = 1000d / System.Diagnostics.Stopwatch.Frequency;
                Debug.Log($"Village reflection diagnostics: {reflection.CaptureCount} submissions, " +
                    $"{reflection.CaptureCompletedCount} completions, {reflection.CaptureFailureCount} failures; " +
                    $"submit CPU {reflection.CaptureSubmitTicks * tickMilliseconds:F3} ms, " +
                    $"poll CPU {reflection.CapturePollTicks * tickMilliseconds:F3} ms, " +
                    $"last asynchronous wall latency {reflection.LastCaptureLatencySeconds:F3} s. " +
                    "These counters do not measure cubemap GPU cost or a frame-time gain.");
            }
            finally
            {
                if (village != null && village.IsDormant) village.ResumeFromDormant();
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test village lighting cleanup");
            }
        }

        private static Shot[] VillageNativeLightingShots(AlpineVillageRoot root)
        {
            AlpineVillageLightingPresentation lighting = root.Lighting;
            Assert.That(lighting, Is.Not.Null);
            Assert.That(lighting.Reflection, Is.Not.Null);
            Assert.That(lighting.HalationSourceCount, Is.InRange(1, AlpineVillageLightingPresentation.MaximumHalationSources));
            Assert.That(lighting.BounceRegionCount, Is.InRange(1, AlpineVillageLightingPresentation.MaximumBounceRegions));
            AssertVillageNativeShaders();

            AlpineVillageLaneSample lane = root.Plan.Lane.Sample(root.Plan.Lane.Length * .43f);
            Vector3 streetEye = lane.Position + lane.Right * .65f + Vector3.up * EyeHeight;
            Vector3 streetAim = root.Plan.MothersHouse.GroundCenter + Vector3.up * 2.8f;
            Transform station = root.World.StationRoot.transform;
            Renderer rail = Array.Find(station.GetComponentsInChildren<Renderer>(true), item => item.name == "Tension Rail");
            Assert.That(rail, Is.Not.Null, "Frame the actual upper station steel, not an invented position.");
            Vector3 steelAim = rail.bounds.center + Vector3.up * .2f;
            Vector3 steelEye = steelAim + station.forward * 2.8f + station.right * 1.8f + Vector3.up * 1.1f;
            AlpineVillageBrookPlan brook = root.Plan.Brook;
            Vector3 springEye = brook.ApproachPosition + brook.LedgeFacing * 2.3f +
                brook.BowlOutletDirection * 1.8f + Vector3.up * 1.9f;
            Vector3 springAim = brook.BowlCenter + Vector3.up * .1f;

            lighting.PrepareForCamera(Camera.main);
            Assert.That(lighting.TryGetVolumeSource(0, out SceneLocalLightSource window), Is.True,
                "The village needs an actual working practical for the air/snow acceptance frame.");
            Vector3 outward = Vector3.ProjectOnPlane(window.Light.transform.forward, Vector3.up).normalized;
            if (outward.sqrMagnitude < .1f) outward = root.Plan.MothersHouse.Facing;
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            Vector3 windowAim = window.Bounds.center + Vector3.up * .2f;
            Vector3 windowEye = windowAim + outward * 3f + side * 1.7f + Vector3.up * .6f;
            bool nightApplied = false, volumeObserved = false;
            double probeDeadline = 0d;
            int warmupFailures = 0;
            return new[]
            {
                Shot.At("Test village - Day uphill", streetEye, streetAim, 62, readyWhen: () =>
                {
                    if (probeDeadline == 0d)
                    {
                        // Capture warms the real camera before this callback.
                        // Cold shader compilation is not asynchronous probe
                        // latency; the production watchdog can legitimately
                        // exhaust its attempts during those first GPU frames.
                        warmupFailures = lighting.Reflection.CaptureFailureCount;
                        GraphicsEffectsSettings.CleanReflectionsEnabled = false;
                        lighting.PrepareForCamera(Camera.main);
                        GraphicsEffectsSettings.CleanReflectionsEnabled = true;
                        probeDeadline = Time.realtimeSinceStartupAsDouble + 10d;
                    }
                    lighting.PrepareForCamera(Camera.main);
                    if (lighting.Reflection.IsReady)
                    {
                        Assert.That(lighting.Reflection.CaptureCompletedCount, Is.GreaterThan(0));
                        Assert.That(lighting.Reflection.CaptureFailureCount, Is.EqualTo(warmupFailures));
                        return true;
                    }
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(probeDeadline),
                        "The genuine asynchronous spring reflection did not become ready.");
                    return false;
                }),
                Shot.At("Test village - Station baseline", steelEye, steelAim, 61, readyWhen: () =>
                { GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = false; return true; }),
                Shot.At("Test village - Station directional light", steelEye, steelAim, 61, readyWhen: () =>
                { GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = true; return true; }),
                Shot.At("Test village - Spring baseline", springEye, springAim, 62, readyWhen: () =>
                { GraphicsEffectsSettings.CleanReflectionsEnabled = false; return true; }),
                Shot.At("Test village - Spring reflection", springEye, springAim, 62, readyWhen: () =>
                { GraphicsEffectsSettings.CleanReflectionsEnabled = true; return true; }),
                Shot.At("Test village - Night baseline", streetEye, streetAim, 62, readyWhen: () =>
                {
                    if (!nightApplied)
                    {
                        double minutes = 21 * 60 - GameSessionState.GameTimeOfDayMinutes;
                        GameSessionState.AdvanceGameTime((float)(minutes / GameTimeState.GameMinutesPerRealSecond));
                        nightApplied = true;
                        SetVillageNativeEffects(false);
                        return false;
                    }
                    return root.StormWave <= GustTroughWave;
                }),
                Shot.At("Test village - Night native lighting", streetEye, streetAim, 62, readyWhen: () =>
                { SetVillageNativeEffects(true); return true; }),
                Shot.At("Test village - Window air baseline", windowEye, windowAim, 65, readyWhen: () =>
                { GraphicsEffectsSettings.CleanVolumeEnabled = false; return true; }),
                Shot.At("Test village - Window air and snow", windowEye, windowAim, 65, readyWhen: () =>
                { GraphicsEffectsSettings.CleanVolumeEnabled = true; return true; }),
                Shot.At("Test village - Blizzard native lighting", streetEye, streetAim, 62, readyWhen: () =>
                {
                    if (!volumeObserved)
                    {
                        SceneCompositeRendererFeature feature = FindVillageLightingFeature();
                        Assert.That(feature.DebugCleanVolumeApplied, Is.True);
                        Assert.That(feature.DebugCleanVolumeResolution.x, Is.InRange(1, 320));
                        Assert.That(feature.DebugCleanVolumeResolution.y, Is.InRange(1, 180));
                        Assert.That(feature.DebugAlpineVolumeSourceCount, Is.InRange(1, 4));
                        Assert.That(lighting.VolumeSourceCount, Is.InRange(1, 4));
                        VerifyVillageVolumeSources(lighting);
                        volumeObserved = true;
                    }
                    Assert.That(root.Snow.Kind, Is.EqualTo(CityPrecipitationKind.Blizzard));
                    Assert.That(root.Weather.CurrentSample.RainIntensity,
                        Is.GreaterThanOrEqualTo(AlpineVillageWeatherRules.SnowFloor));
                    // The lighting scenario uses a human-readable synthetic
                    // seed. It must not wait for a gust amplitude belonging to
                    // the separate, default-seed weather acceptance fixture.
                    return true;
                })
            };
        }

        private static void VerifyVillageVolumeSources(AlpineVillageLightingPresentation lighting)
        {
            for (int i = 0; i < lighting.VolumeSourceCount; i++)
            {
                Assert.That(lighting.TryGetVolumeSource(i, out SceneLocalLightSource source), Is.True);
                Assert.That(source.Light, Is.Not.Null);
                Assert.That(source.Light.isActiveAndEnabled, Is.True);
                Assert.That(source.Light.intensity, Is.GreaterThan(0));
                Assert.That(source.Bounds.size.x, Is.InRange(.1f, source.Light.range * 2));
                Assert.That(source.Bounds.size.y, Is.InRange(.1f, source.Light.range * 2));
                Assert.That(source.Bounds.size.z, Is.InRange(.1f, source.Light.range * 2));
                if (source.Light.type == LightType.Spot)
                {
                    Vector3 outward = Vector3.ProjectOnPlane(source.Light.transform.forward, Vector3.up).normalized;
                    Vector3 planeNormal = new Vector3(source.ExteriorPlane.x, source.ExteriorPlane.y, source.ExteriorPlane.z);
                    Assert.That(Vector3.Dot(planeNormal, outward), Is.GreaterThan(.9f),
                        "Window light must illuminate the exterior side of its real facade.");
                }
            }
        }

        private static IEnumerator VerifyVillageLightingRouting(AlpineVillageRoot root)
        {
            Camera camera = Camera.main;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(root.Lighting));
            ScenePresentationExclusion exclusion = camera.gameObject.AddComponent<ScenePresentationExclusion>();
            try
            {
                Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
                CaptureCurrentCamera(camera, SceneIds.AlpineVillage, "Test village - Excluded camera");
                Assert.That(Shader.GetGlobalVector("_WarehouseCleanEffects"), Is.EqualTo(Vector4.zero));
                Assert.That(Shader.GetGlobalInt("_AlpineLocalLightCount"), Is.Zero);
                Assert.That(FindVillageLightingFeature().DebugCleanVolumeApplied, Is.False);
            }
            finally { Object.DestroyImmediate(exclusion); }

            Assert.That(root.EnterDormant(), Is.True);
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
            Assert.That(root.Lighting.Reflection.IsReady, Is.False);
            Assert.That(Shader.GetGlobalVector("_VillageReflectionRegion").w, Is.Zero);
            root.ResumeFromDormant();
            yield return null;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(root.Lighting));
            double deadline = Time.realtimeSinceStartupAsDouble + 10d;
            while (!root.Lighting.Reflection.IsReady && Time.realtimeSinceStartupAsDouble < deadline)
            {
                root.Lighting.PrepareForCamera(camera);
                yield return null;
            }
            Assert.That(root.Lighting.Reflection.IsReady, Is.True,
                "A resumed village must recapture its spring and restore camera ownership.");
        }

        private static void AssertVillageNativeShaders()
        {
            foreach (string name in new[] { "Bar Promenade/Lit", "Hidden/BarPromenade/Alpine Village Local Lit Air" })
            {
                Shader shader = Shader.Find(name);
                Assert.That(shader, Is.Not.Null, name);
                Assert.That(shader.isSupported, Is.True, name);
#if UNITY_EDITOR
                var errors = new System.Text.StringBuilder(name);
                foreach (var message in UnityEditor.ShaderUtil.GetShaderMessages(shader))
                    errors.AppendLine().Append(message.message);
                Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader), Is.False, errors.ToString());
#endif
            }
        }

        private static SceneCompositeRendererFeature FindVillageLightingFeature()
        {
            foreach (SceneCompositeRendererFeature feature in Resources.FindObjectsOfTypeAll<SceneCompositeRendererFeature>())
                if (feature != null && feature.isActive) return feature;
            Assert.Fail("The active scene presentation renderer feature is missing.");
            return null;
        }

        private static void SetVillageNativeEffects(bool enabled)
        {
            GraphicsEffectsSettings.CleanReflectionsEnabled = enabled;
            GraphicsEffectsSettings.CleanVolumeEnabled = enabled;
            GraphicsEffectsSettings.CleanHalationEnabled = enabled;
            GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = enabled;
            GraphicsEffectsSettings.CleanBounceEnabled = enabled;
        }

        private static void AssertVillageLightingDifference(string before, string after)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.AlpineVillage);
            var a = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var b = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(a.LoadImage(File.ReadAllBytes(Path.Combine(folder, before + ".png"))), Is.True);
                Assert.That(b.LoadImage(File.ReadAllBytes(Path.Combine(folder, after + ".png"))), Is.True);
                Color32[] original = a.GetPixels32(), changed = b.GetPixels32();
                int different = 0;
                for (int i = 0; i < original.Length; i++)
                    if (Math.Abs(original[i].r - changed[i].r) + Math.Abs(original[i].g - changed[i].g) +
                        Math.Abs(original[i].b - changed[i].b) >= 2) different++;
                Debug.Log($"Village native lighting pair '{after}': {different} changed pixels.");
                Assert.That(different, Is.GreaterThan(24), after + " must visibly affect its actual receiver.");
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
