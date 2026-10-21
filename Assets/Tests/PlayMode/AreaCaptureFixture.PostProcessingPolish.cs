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
        [UnityTest, Timeout(600000)]
        [Explicit("Focused grain/vignette A/B, shared item focus ownership, preview MSAA and real native gradient review.")]
        public IEnumerator PostProcessingPolish()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousDelta = Time.captureDeltaTime;
            WorldItemFoundScreen found = null;
            GameObject sources = null;
            WorldItemInspectionPresenter presenter = null;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                foreach (string key in keys) PlayerPrefs.SetInt(key, key == "graphics.aspect_4_3" ? 0 : 1);
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                GameSessionState.BeginNewGame("Test post processing");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                yield return CapturePostProcessingRooms();

                CityGameRoot city = null;
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () =>
                {
                    FreezeLightingPolish(city.Player.Motor);
                    city.DayNight.enabled = false; city.Weather.enabled = false;
                    CitySeacoastFrame coast = city.World.SeacoastPlan.Frame;
                    float x = coast.EastZone.xMin + Mathf.Min(16f, coast.EastZone.width * .18f);
                    float z = coast.WaterlineZ - 5.6f;
                    Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout,
                        new Vector2(x, z), out float ground, out _), Is.True);
                    Vector3 eye = new Vector3(x, ground + EyeHeight, z);
                    BuildingLot bar = null;
                    foreach (BuildingLot lot in city.Layout.BuildingLots) if (lot.IsBar) { bar = lot; break; }
                    Assert.That(bar, Is.Not.Null);
                    Shot street = FrameSpecialEntrance("Test native - Street fog", bar, 9f);
                    var shots = new List<Shot>();
                    foreach (int minute in new[] { 12 * 60, 0 })
                    {
                        int time = minute;
                        // Contrast reference only: keep the real overcast sky/weather,
                        // rather than presenting this as a complete night transition.
                        string light = minute == 0 ? "Night directional reference" : "Day";
                        shots.Add(Shot.At("Test native - " + light + " sky and sea", eye,
                            eye + new Vector3(0f, 5f, 100f), 60f, readyWhen: () =>
                            {
                                RuntimeSceneSetup.ApplyCityExteriorLighting(GameTimeDayNightRules.Evaluate(time), false);
                                return true;
                            }));
                        shots.Add(Shot.At("Test native - " + light + " street fog", street.Position,
                            street.Target, street.FieldOfView));
                    }
                    return shots.ToArray();
                });
                RuntimeSceneSetup.ApplyCityExteriorLighting(GameTimeDayNightRules.Evaluate(12 * 60), false);
                Camera camera = Camera.main;
                Assert.That(camera.GetUniversalAdditionalCameraData().dithering, Is.False,
                    "The conditional gradient review must start from the actual native output without added dither.");
                Debug.Log($"Native gradient review: {QualitySettings.activeColorSpace}, HDR {camera.allowHDR}, " +
                    "1280x720 RGB24 PNG readback; sky/fog and intentional water bands must be judged separately.");
                city.Player.Motor.SetInputEnabled(true);
                city.Player.Interactor.SetInputEnabled(true);
                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
                sources = new GameObject("Test inspection sources");
                sources.transform.SetParent(city.transform, false);
                Transform model = InventoryItemModelFactory.BuildPreviewModel(InventoryItemId.Scarf, sources.transform);
                model.name = "Test inspection scarf";
                model.position = camera.transform.position + camera.transform.forward * 2f;
                Vector3 originalPosition = model.localPosition;
                found = WorldItemFoundScreen.For(city.Player.Interactor);
                int grants = 0;
                Assert.That(found.TryPresent(city.Player.Interactor, InventoryItemId.Scarf, model,
                    () => { grants++; return true; }, taken => Assert.That(taken, Is.False)), Is.True);
                Assert.That(found.OwnsDepthOfField, Is.False, "The lift must retain its existing world lens.");
                yield return WaitForFoundItemLighting(found, showing: true);
                Assert.That(found.OwnsDepthOfField, Is.True);
                Assert.That(found.InspectionFocusDistance, Is.EqualTo(WorldItemInspectionPresenter.PreviewDistance).Within(.001f));
                Volume focus = FindPostProcessingFocus();
                Assert.That(focus.profile.TryGet(out DepthOfField depth), Is.True);
                Assert.That(depth.mode.value, Is.EqualTo(DepthOfFieldMode.Bokeh));
                Assert.That(depth.aperture.value, Is.EqualTo(8f));
                Assert.That(depth.focalLength.value, Is.EqualTo(35f));
                for (int frame = 0; frame < 30; frame++) yield return null;
                focus.weight = 0f;
                CaptureCurrentCamera(camera, SceneIds.City, "Test inspection - World lens reference");
                focus.weight = 1f;
                CaptureCurrentCamera(camera, SceneIds.City, "Test inspection - Owned item focus");
                Assert.That(PolishDifference(SceneIds.City, "Test inspection - World lens reference",
                    "Test inspection - Owned item focus"), Is.GreaterThan(24));
                if (!Application.isBatchMode) yield return CapturePostProcessingGameView("Test inspection - Actual found screen");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    Quaternion rotation = model.rotation;
                    float weight = focus.weight;
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(Quaternion.Angle(rotation, model.rotation), Is.LessThan(.01f));
                    Assert.That(focus.weight, Is.EqualTo(weight));
                    GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                    yield return null;
                    Assert.That(focus.weight, Is.Zero, "Disabling DoF while paused must remove the override immediately.");
                }
                found.enabled = false;
                Assert.That(found.OwnsDepthOfField, Is.False);
                Assert.That(CinematicDepthOfField.IsActive, Is.False);
                Assert.That(model.parent, Is.SameAs(sources.transform));
                Assert.That(Vector3.Distance(model.localPosition, originalPosition), Is.LessThan(.0001f));
                Assert.That(grants, Is.Zero);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                found.enabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = true;

                presenter = new WorldItemInspectionPresenter(camera, "Test inspection ownership");
                var timeline = new WorldItemInspectionTimeline();
                Assert.That(timeline.BeginInspection(), Is.True);
                timeline.Advance(1f);
                CinematicDepthOfField.Begin(7f, 11f, 45f);
                Assert.That(presenter.Begin(model, WorldItemInspectionPresenter.CalculateWorldBounds(model),
                    Quaternion.identity, 1f), Is.True);
                presenter.Apply(timeline.CurrentFrame);
                Assert.That(presenter.OwnsDepthOfField, Is.False, "An inspection must not borrow an existing cinematic shot.");
                Assert.That(depth.focusDistance.value, Is.EqualTo(7f));
                presenter.Restore();
                Assert.That(CinematicDepthOfField.IsActive, Is.True);
                CinematicDepthOfField.EndImmediately();
                Assert.That(presenter.Begin(model, WorldItemInspectionPresenter.CalculateWorldBounds(model),
                    Quaternion.identity, 1f), Is.True);
                presenter.Apply(timeline.CurrentFrame);
                Assert.That(presenter.OwnsDepthOfField, Is.True);
                CinematicDepthOfField.Begin(9f, 11f, 45f);
                presenter.Restore();
                Assert.That(CinematicDepthOfField.IsActive, Is.True, "Stale inspection cleanup must retain the newer cinematic shot.");
                Assert.That(depth.focusDistance.value, Is.EqualTo(9f));
                CinematicDepthOfField.EndImmediately();
                presenter.Dispose(); presenter = null;

                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.Scarf), Is.True);
                Assert.That(city.Inventory.Open(), Is.True);
                for (int i = 0; i < GameSessionState.InventoryItems.Count; i++)
                    if (GameSessionState.InventoryItems[i].ItemId == InventoryItemId.Scarf && city.Inventory.SelectedItemIndex != i)
                        Assert.That(city.Inventory.SelectItem(i), Is.True);
                InventoryItemPreviewRenderer preview = city.Inventory.View.PreviewRenderer;
                preview.RefreshNow();
                preview.enabled = false; // Keep exactly the same model angle for local AA comparison.
                Camera previewCamera = preview.PreviewCamera;
                Assert.That(previewCamera.GetComponent<ScenePresentationExclusion>(), Is.Not.Null);
                Assert.That(previewCamera.GetUniversalAdditionalCameraData().renderPostProcessing, Is.False);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
                preview.RefreshNow();
                Assert.That(preview.MsaaSamples, Is.EqualTo(1));
                SavePostProcessingPreview(preview, "Test inventory - Unsmoothed model");
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                preview.RefreshNow();
                Assert.That(preview.MsaaSamples, Is.InRange(1, InventoryItemPreviewRenderer.RequestedMsaaSamples));
                Assert.That(previewCamera.allowMSAA, Is.EqualTo(preview.MsaaSamples > 1));
                SavePostProcessingPreview(preview, "Test inventory - Local model MSAA");
                if (preview.MsaaSamples > 1)
                    Assert.That(PolishDifference(SceneIds.City, "Test inventory - Unsmoothed model",
                        "Test inventory - Local model MSAA"), Is.GreaterThan(24));
                Texture retainedTexture = preview.Texture;
                preview.RefreshNow();
                Assert.That(preview.Texture, Is.SameAs(retainedTexture), "A stable AA setting must reuse the small target.");
                Assert.That(previewCamera, Is.SameAs(preview.PreviewCamera));
                if (!Application.isBatchMode) yield return CapturePostProcessingGameView("Test inventory - Actual item panel");
                Assert.That(city.Inventory.Close(), Is.True);
                Assert.That(preview.IsRendering, Is.False);
                preview.enabled = true;
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                Assert.That(CinematicDepthOfField.IsActive, Is.False);
                Assert.That(previewCamera == null, Is.True);
            }
            finally
            {
                if (found != null) found.Abandon();
                presenter?.Dispose();
                CinematicDepthOfField.EndImmediately();
                if (sources != null) Object.Destroy(sources);
                foreach (var preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save(); GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame("Test post processing cleanup");
            }
        }

        private static Volume FindPostProcessingFocus()
        {
            foreach (Volume volume in Object.FindObjectsByType<Volume>())
                if (volume.profile.name == "Runtime Cinematic Depth Of Field") return volume;
            Assert.Fail("The shared cinematic owner must provide the inspection's actual volume.");
            return null;
        }

        private static void SavePostProcessingPreview(InventoryItemPreviewRenderer preview, string name)
        {
            var source = (RenderTexture)preview.Texture;
            var target = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                preview.PreviewCamera.Render();
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                pixels.Apply();
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
                Debug.Log($"Local preview AA: {preview.MsaaSamples} samples, {source.width}x{source.height}, {source.graphicsFormat}.");
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }

        private static IEnumerator CapturePostProcessingGameView(string name)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (File.Exists(path) && new FileInfo(path).Length > 8) yield break;
            }
            Assert.Fail("The actual post-processing item UI screenshot must finish in its bounded window.");
        }
    }
}
