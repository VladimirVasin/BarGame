using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class RealtimeLightingCaptureSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.ScenePresentationAssetSetup, BarPromenade.Editor", true)
                .GetMethod("EnsureInstalled", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
#if SURFACE_CACHE
            Type.GetType("BarPromenade.Editor.SurfaceCacheGiPreflight, BarPromenade.Editor", true)
                .GetMethod("EnsurePilotMeshImports", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
#endif
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(600000)]
        [Explicit("Scoped native SSR acceptance and current-version Bar Surface Cache GI compatibility pilot.")]
        [PrebuildSetup(typeof(RealtimeLightingCaptureSetup))]
        public IEnumerator RealtimeLightingPolish()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                foreach (string key in keys) PlayerPrefs.SetInt(key, key == "graphics.aspect_4_3" ? 0 : 1);
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                GameSessionState.BeginNewGame("Test realtime lighting");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                yield return CaptureScreenSpaceReflectionWorlds();
                yield return CaptureBarRealtimeGiPilot();
            }
            finally
            {
                foreach (var preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save(); GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousDelta;
                RuntimePerformanceCapture.StopCapture();
                GameSessionState.BeginNewGame("Test realtime lighting cleanup");
            }
        }

        private static IEnumerator CaptureBarRealtimeGiPilot()
        {
#if SURFACE_CACHE
            GameSessionState.EnterBar("Test bar realtime lighting", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
            yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
            BarInteriorRoot bar = null;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                if (bar != null && bar.IsInitialized) break;
                yield return null;
            }
            Assert.That(bar != null && bar.IsInitialized, Is.True);
            FreezeLightingPolish(bar.Player.Motor);
            Camera camera = Camera.main;
            foreach (Renderer renderer in bar.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(-1.6f, 1.65f, 1.4f),
                Quaternion.LookRotation(new Vector3(1f, -.3f, 4.8f)));
            camera.fieldOfView = 55f;
            var target = new RenderTexture(Width, Height, 24);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = target;
            for (int frame = 0; frame < 15; frame++) yield return null;
            SaveRealtimeFrame(camera, SceneIds.BarInterior, "Test bar - Finite bounce baseline");
            yield return MeasurePolish(SceneIds.BarInterior, "Test bar - Finite bounce baseline");
            IDisposable configuration = null;
            GameObject host = null;
            BarSurfaceCacheGiRendererFeature provider = null;
            BarSurfaceCacheGiPilot pilot = null;
            UniversalRendererData rendererData = null;
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            var errors = new List<string>();
            string failure = null;
            Application.LogCallback receive = (message, stack, type) =>
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 12)
                    errors.Add(message);
            };
            try
            {
                // Only the explicit experimental provider gate may collect errors.
                // Every collected failure rejects GI; SSR runs with ordinary log checks.
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += receive;
                try
                {
                    var preflight = Type.GetType("BarPromenade.Editor.SurfaceCacheGiPreflight, BarPromenade.Editor", true);
                    configuration = (IDisposable)preflight.GetMethod("BeginIsolatedProviderProbe").Invoke(null, null);
                    host = new GameObject("Test bar GI pilot");
                    host.transform.SetParent(bar.transform, false);
                    pilot = host.AddComponent<BarSurfaceCacheGiPilot>();
                    pilot.Initialize(bar);
                    Assert.That(pilot.ContributorCount, Is.InRange(1, BarSurfaceCacheGiPilot.MaximumContributors));
                    Assert.That(pilot.MaterialCount, Is.InRange(1, BarSurfaceCacheGiPilot.MaximumMaterials));
                    foreach (UniversalRendererData candidate in Resources.FindObjectsOfTypeAll<UniversalRendererData>())
                        if (candidate.name == "PC_Renderer") { rendererData = candidate; break; }
                    if (rendererData == null) throw new InvalidOperationException("The active PC renderer was not found.");
                    provider = ScriptableObject.CreateInstance<BarSurfaceCacheGiRendererFeature>();
                    provider.name = "Test bar native GI";
                    rendererData.rendererFeatures.Add(provider);
                    rendererData.SetDirty();
                }
                catch (Exception exception)
                {
                    failure = exception.GetBaseException().GetType().Name + ": " + exception.GetBaseException().Message;
                }
                for (int frame = 0; failure == null && errors.Count == 0 && frame < 90; frame++)
                {
                    yield return null;
                    if (provider != null && provider.FailureReason.Length != 0) failure = provider.FailureReason;
                }
                if (failure == null && errors.Count == 0)
                {
                    Assert.That(provider.ResourcesReady && provider.EnqueuedCameraFrames > 0, Is.True,
                        "A configured preview is not an active provider until native frames execute.");
                    SaveRealtimeFrame(camera, SceneIds.BarInterior, "Test bar - Surface Cache GI");
                    Assert.That(Shader.IsKeywordEnabled("_SCREEN_SPACE_IRRADIANCE"), Is.True);
                    Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
                    Debug.Log($"Bar GI gate: native frame active, contributors {pilot.ContributorCount}, " +
                        $"materials {pilot.MaterialCount}, meshes {pilot.MeshCount}, CPU mesh bytes {pilot.EstimatedMeshCpuBytes}, " +
                        $"hardware RT {SystemInfo.supportsRayTracing}.");
                    yield return MeasurePolish(SceneIds.BarInterior, "Test bar - Surface Cache GI");
                    camera.transform.position += new Vector3(.15f, 0, 0);
                    for (int frame = 0; frame < 12; frame++) yield return null;
                    SaveRealtimeFrame(camera, SceneIds.BarInterior, "Test bar - GI moving camera");
                    GraphicsEffectsSettings.CleanBounceEnabled = false;
                    yield return null;
                    camera.Render();
                    Assert.That(provider.ResourcesReady, Is.False);
                    GraphicsEffectsSettings.CleanBounceEnabled = true;
                    for (int frame = 0; frame < 12 && errors.Count == 0; frame++) yield return null;
                }
                if (errors.Count != 0) failure = string.Join(" | ", errors);
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "Realtime lighting");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "GI compatibility.txt"), failure ?? "Native GI frames executed; rendered images require visual acceptance.");
                Debug.Log(failure == null ? "Bar Surface Cache GI compatibility gate passed; visual review required."
                    : "Bar Surface Cache GI compatibility gate rejected: " + failure);
            }
            finally
            {
                GraphicsEffectsSettings.CleanBounceEnabled = true;
                if (provider != null)
                {
                    if (rendererData != null) rendererData.rendererFeatures.Remove(provider);
                    provider.ReleasePilotResources();
                    Object.DestroyImmediate(provider);
                }
                if (host != null) Object.DestroyImmediate(host);
                if (rendererData != null) rendererData.SetDirty();
                configuration?.Dispose();
                Application.logMessageReceived -= receive;
                LogAssert.ignoreFailingMessages = previousIgnore;
                camera.targetTexture = previous;
                target.Release(); Object.DestroyImmediate(target);
            }
            yield return null;
            Assert.That(BarSurfaceCacheGiPilot.Current, Is.Null);
            // Preserve the established camera-local service teardown order.
            var arms = bar.DrinkShop.FirstPersonArms;
            Object.Destroy(bar.DrinkShop); yield return null;
            if (arms != null) Object.Destroy(arms);
            yield return null;
#else
            Assert.Fail("The isolated native GI compatibility gate must compile with SURFACE_CACHE.");
            yield break;
#endif
        }

        private static void SaveRealtimeFrame(Camera camera, string scene, string name)
        {
            RenderTexture target = camera.targetTexture;
            Assert.That(target, Is.Not.Null);
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", scene);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
        }
    }
}
