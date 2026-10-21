using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Timeout(240000)]
        [Explicit("Focused bar bounce/tonemapping, City soft shadows and house air edge acceptance.")]
        public IEnumerator LightingPolish()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousDelta = Time.captureDeltaTime;
            ScriptableRendererFeature ao = null;
            bool previousAo = true;
            SceneCompositeRendererFeature composite = null;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test lighting polish");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                GraphicsEffectsSettings.CleanReflectionsEnabled = false;
                GraphicsEffectsSettings.CleanVolumeEnabled = true;
                GraphicsEffectsSettings.CleanHalationEnabled = true;
                GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = true;
                GraphicsEffectsSettings.CleanBounceEnabled = true;

                BarInteriorRoot bar = null;
                GameSessionState.EnterBar("Test bar lighting", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return Capture(SceneIds.BarInterior, () =>
                {
                    bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                    return bar != null && bar.IsInitialized ? bar : null;
                }, () =>
                {
                    FreezeLightingPolish(bar.Player.Motor);
                    Assert.That(bar.Lighting.BounceRegionCount, Is.InRange(1, 2));
                    Assert.That(bar.Atmosphere.PracticalLights.Count, Is.LessThanOrEqualTo(6));
                    ao = FindPolishAmbientOcclusion(); previousAo = ao.isActive;
                    VolumeProfile profile = bar.Atmosphere.PostProcessVolume.profile;
                    Assert.That(profile.TryGet(out Tonemapping tone), Is.True);
                    Assert.That(tone.mode.value, Is.EqualTo(TonemappingMode.Neutral));
                    if (profile.TryGet(out FilmGrain grain)) grain.active = false;
                    bar.Atmosphere.Dust.Pause(true);
                    Vector3 eye = new Vector3(-.55f, 1.55f, 4.45f), aim = new Vector3(-.55f, 1.5f, 6.5f);
                    return new[]
                    {
                        BarShot("Contact baseline", false, false, TonemappingMode.Neutral),
                        BarShot("Contact shading", true, false, TonemappingMode.Neutral),
                        BarShot("Reflected warmth", true, true, TonemappingMode.Neutral),
                        BarShot("ACES", true, true, TonemappingMode.ACES),
                        BarShot("Neutral", true, true, TonemappingMode.Neutral),
                        Shot.At("Test bar - Dark seating", new Vector3(-2.5f, 1.8f, -2.2f),
                            new Vector3(-9.5f, 1.2f, 2f), 62f)
                    };
                    Shot BarShot(string label, bool contact, bool bounce, TonemappingMode mode) =>
                        Shot.At("Test bar - " + label, eye, aim, 50f, readyWhen: () =>
                        {
                            ao.SetActive(contact); GraphicsEffectsSettings.CleanBounceEnabled = bounce;
                            tone.mode.Override(mode); return true;
                        });
                });
                Assert.That(PolishDifference(SceneIds.BarInterior, "Test bar - Contact baseline", "Test bar - Contact shading"), Is.GreaterThan(24));
                Assert.That(PolishDifference(SceneIds.BarInterior, "Test bar - Contact shading", "Test bar - Reflected warmth"), Is.GreaterThan(24));
                Assert.That(PolishDifference(SceneIds.BarInterior, "Test bar - ACES", "Test bar - Neutral"), Is.GreaterThan(24));
                Camera camera = Camera.main;
                Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(bar.Lighting));
                bar.Lighting.enabled = false;
                CaptureCurrentCamera(camera, SceneIds.BarInterior, "Test bar - Disabled owner");
                Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
                Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
                bar.Lighting.enabled = true;
                var exclusion = camera.gameObject.AddComponent<ScenePresentationExclusion>();
                CaptureCurrentCamera(camera, SceneIds.BarInterior, "Test bar - Excluded camera");
                Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
                Object.DestroyImmediate(exclusion);
                camera.transform.SetPositionAndRotation(new Vector3(-.55f, 1.55f, 4.45f),
                    Quaternion.LookRotation(new Vector3(0f, -.05f, 2.05f)));
                foreach (bool bounce in new[] { false, true })
                {
                    GraphicsEffectsSettings.CleanBounceEnabled = bounce;
                    yield return MeasurePolish(SceneIds.BarInterior, bounce ? "Test bar - Bounce enabled" : "Test bar - Bounce disabled");
                }
                // This lighting fixture owns no drink action. Release its service
                // before Single loading destroys the camera-local arm renderers.
                var arms = bar.DrinkShop.FirstPersonArms;
                Object.Destroy(bar.DrinkShop);
                yield return null;
                if (arms != null) Object.Destroy(arms);
                yield return null;

                CityGameRoot city = null;
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () =>
                {
                    FreezeLightingPolish(city.Player.Motor);
                    city.DayNight.enabled = false; city.Weather.enabled = false;
                    Assert.That(bar == null, Is.True, "The bar bounce owner must unload with its room.");
                    Assert.That(RenderSettings.sun.shadows, Is.EqualTo(LightShadows.Soft));
                    BuildingLot lot = null;
                    foreach (BuildingLot candidate in city.Layout.BuildingLots) if (candidate.IsBar) { lot = candidate; break; }
                    Assert.That(lot, Is.Not.Null);
                    Shot pose = FrameSpecialEntrance("Test city - Soft shadows", lot, 4.8f);
                    return new[]
                    {
                        Shot.At("Test city - Hard reference", pose.Position, pose.Target, pose.FieldOfView,
                            readyWhen: () => { RenderSettings.sun.shadows = LightShadows.Hard; return true; }),
                        Shot.At("Test city - Soft shadows", pose.Position, pose.Target, pose.FieldOfView,
                            readyWhen: () => { RuntimeSceneSetup.ApplyCityExteriorLighting(GameTimeDayNightRules.Evaluate(12 * 60), false); return true; }),
                        Shot.At("Test city - Moving camera", pose.Position + Vector3.right * .15f, pose.Target, pose.FieldOfView),
                        Shot.At("Test city - Night soft shadows", pose.Position, pose.Target, pose.FieldOfView,
                            readyWhen: () => { RuntimeSceneSetup.ApplyCityExteriorLighting(GameTimeDayNightRules.Evaluate(0), false); return true; })
                    };
                });
                Assert.That(PolishDifference(SceneIds.City, "Test city - Hard reference", "Test city - Soft shadows"), Is.GreaterThan(24));
                Assert.That(RenderSettings.sun.shadows, Is.EqualTo(LightShadows.Soft));
                RuntimeSceneSetup.ApplyCityExteriorLighting(GameTimeDayNightRules.Evaluate(12 * 60), false);
                foreach (LightShadows mode in new[] { LightShadows.Hard, LightShadows.Soft })
                {
                    RenderSettings.sun.shadows = mode;
                    yield return MeasurePolish(SceneIds.City, "Test city - " + mode + " shadows");
                }
                RuntimeSceneSetup.ApplyCityExteriorLighting(GameTimeDayNightRules.Evaluate(12 * 60), false);

                MothersHouseInteriorRoot house = null;
                yield return Capture(SceneIds.MothersHouseInterior, () =>
                {
                    house = Object.FindAnyObjectByType<MothersHouseInteriorRoot>();
                    return house != null && house.IsInitialized ? house : null;
                }, () =>
                {
                    FreezeLightingPolish(house.Player.Motor);
                    house.FixedCamera.enabled = false;
                    if (house.Atmosphere.FireFlicker != null) house.Atmosphere.FireFlicker.enabled = false;
                    if (house.ChairMotion != null) house.ChairMotion.enabled = false;
                    composite = FindVillageLightingFeature();
                    var shots = new List<Shot>();
                    AddHousePair("Furniture edge", new Vector3(1.6f, 1.8f, -2.2f), new Vector3(-1.65f, .65f, -.1f), 55f);
                    AddHousePair("Chair edge", new Vector3(1.7f, .85f, -.3f), new Vector3(0f, .52f, 1.55f), 50f);
                    shots.Add(Shot.At("Test house - Room boundary", new Vector3(4.6f, 2.6f, -3.4f), new Vector3(0, 1.05f, .6f), 36f));
                    return shots.ToArray();
                    void AddHousePair(string label, Vector3 eye, Vector3 aim, float fov)
                    {
                        foreach (bool repair in new[] { false, true })
                        {
                            bool enabled = repair;
                            shots.Add(Shot.At("Test house - " + label + (repair ? " repaired" : " reference"), eye, aim, fov,
                                readyWhen: () => { composite.DebugMothersHouseDepthRepairDisabled = !enabled; return true; }));
                        }
                    }
                });
                int repaired = PolishDifference(SceneIds.MothersHouseInterior, "Test house - Furniture edge reference", "Test house - Furniture edge repaired") +
                    PolishDifference(SceneIds.MothersHouseInterior, "Test house - Chair edge reference", "Test house - Chair edge repaired");
                Assert.That(repaired, Is.GreaterThan(0), "A real foreground edge must exercise the exact depth repair.");
                Assert.That(composite.DebugMothersHouseVolumeSourceCount, Is.InRange(1, 2));
                Assert.That(composite.DebugMothersHouseVolumeResolution.x, Is.LessThanOrEqualTo(320));
                Assert.That(composite.DebugMothersHouseVolumeResolution.y, Is.LessThanOrEqualTo(180));
                Assert.That(house.Lighting.InteriorVolumeSourceCount, Is.InRange(1, 2));
                for (int i = 0; i < house.Lighting.InteriorVolumeSourceCount; i++)
                {
                    Assert.That(house.Lighting.TryGetInteriorVolumeSource(i, out SceneInteriorLightSource source), Is.True);
                    Assert.That(source.RoomBounds.Contains(source.Light.transform.position), Is.True,
                        "Native repair must use a real emitter inside its physical room.");
                }
                camera = Camera.main;
                camera.transform.SetPositionAndRotation(new Vector3(1.6f, 1.8f, -2.2f),
                    Quaternion.LookRotation(new Vector3(-3.25f, -1.15f, 2.1f)));
                foreach (bool repair in new[] { false, true })
                {
                    composite.DebugMothersHouseDepthRepairDisabled = !repair;
                    yield return MeasurePolish(SceneIds.MothersHouseInterior,
                        repair ? "Test house - Depth repair enabled" : "Test house - Depth repair disabled");
                }
                composite.DebugMothersHouseDepthRepairDisabled = false;
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                Assert.That(SceneLightingPresentation.Resolve(Camera.main), Is.Null);
                Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
                Assert.That(Shader.GetGlobalInt("_MothersHouseAirSourceCount"), Is.Zero);
            }
            finally
            {
                RuntimePerformanceCapture.StopCapture();
                if (ao != null) ao.SetActive(previousAo);
                if (composite != null) composite.DebugMothersHouseDepthRepairDisabled = false;
                foreach (var preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save(); GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame("Test lighting polish cleanup");
            }
        }

        private static void FreezeLightingPolish(PlayerMotor motor)
        {
            motor.SetInputEnabled(false);
            var clock = Object.FindAnyObjectByType<GameTimeRuntime>();
            if (clock != null) clock.enabled = false;
            var follow = Camera.main.GetComponent<PlayerCameraFollow>();
            if (follow != null) follow.enabled = false;
            foreach (Volume volume in Object.FindObjectsByType<Volume>())
                if (volume.profile.TryGet(out FilmGrain grain)) grain.active = false;
        }

        private static ScriptableRendererFeature FindPolishAmbientOcclusion()
        {
            foreach (var feature in Resources.FindObjectsOfTypeAll<ScriptableRendererFeature>())
                if (feature != null && feature.GetType().Name == "ScreenSpaceAmbientOcclusion") return feature;
            Assert.Fail("The PC SSAO renderer feature is missing."); return null;
        }

        private static int PolishDifference(string scene, string before, string after)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", scene);
            var a = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var b = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(a.LoadImage(File.ReadAllBytes(Path.Combine(directory, before + ".png"))), Is.True);
                Assert.That(b.LoadImage(File.ReadAllBytes(Path.Combine(directory, after + ".png"))), Is.True);
                Color32[] first = a.GetPixels32(), second = b.GetPixels32(); int different = 0;
                for (int i = 0; i < first.Length; i++)
                    if (Math.Abs(first[i].r - second[i].r) + Math.Abs(first[i].g - second[i].g) + Math.Abs(first[i].b - second[i].b) >= 2) different++;
                Debug.Log($"Lighting polish '{after}': {different} changed pixels.");
                return different;
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        private static IEnumerator MeasurePolish(string scene, string label)
        {
            Assert.That(RuntimePerformanceCapture.StartCapture(new PerformanceCaptureOptions(scene, label, .25, 1),
                Path.Combine(Directory.GetCurrentDirectory(), "Captures", "Lighting polish", label), Camera.main), Is.True);
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while (RuntimePerformanceCapture.IsRunning && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(RuntimePerformanceCapture.IsRunning, Is.False, "The local comparison must finish within its bounded window.");
            Assert.That(File.Exists(RuntimePerformanceCapture.LastReportPath), Is.True);
        }
    }
}
