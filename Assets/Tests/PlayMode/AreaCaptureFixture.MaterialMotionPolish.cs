using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(600000)]
        [Explicit("Focused production material, rooted grass, crown pigment, slide normal and water A/B acceptance.")]
        public IEnumerator MaterialMotionPolish()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousDelta = Time.captureDeltaTime;
            CityCoastalGrassWind grass = null;
            Material basin = null;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test material motion");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.CleanReflectionsEnabled = false;
                GraphicsEffectsSettings.CleanVolumeEnabled = true;
                GraphicsEffectsSettings.CleanHalationEnabled = true;
                GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = true;
                GraphicsEffectsSettings.CleanBounceEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;

                BarInteriorRoot bar = null;
                GameSessionState.EnterBar("Test bar materials", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return Capture(SceneIds.BarInterior, () =>
                {
                    bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                    return bar != null && bar.IsInitialized ? bar : null;
                }, () =>
                {
                    FreezeLightingPolish(bar.Player.Motor);
                    bar.Atmosphere.Dust.Pause(true);
                    Assert.That(BarCounterMaterialResponse.SharedMaterialCount, Is.EqualTo(1));
                    foreach (string path in new[] { BarCounterMaterialResponse.WoodResourcePath, BarCounterMaterialResponse.BrassResourcePath })
                    {
                        Texture2D map = Resources.Load<Texture2D>(path);
                        Assert.That(map, Is.Not.Null);
                        Assert.That(map.isDataSRGB, Is.False);
                        Assert.That(map.isReadable, Is.False);
                        Assert.That(map.width, Is.EqualTo(512));
                        Assert.That(map.mipmapCount, Is.GreaterThan(1));
                    }
                    var shots = new List<Shot>();
                    AddBarPair("Wood", new Vector3(-.55f, 2.45f, 4.65f), new Vector3(-.55f, 1.02f, 5.85f), 40f);
                    AddBarPair("Brass taps", new Vector3(4.2f, 2f, 4.7f), new Vector3(5.05f, 1.25f, 5.75f), 34f);
                    AddBarPair("Brass rail", new Vector3(-3.4f, .70f, 3.8f), new Vector3(-3.4f, .26f, 5.12f), 32f);
                    return shots.ToArray();
                    void AddBarPair(string label, Vector3 eye, Vector3 aim, float fov)
                    {
                        foreach (bool enabled in new[] { false, true })
                        {
                            bool response = enabled;
                            shots.Add(Shot.At("Test bar - " + label + (enabled ? " response" : " uniform"), eye, aim, fov,
                                readyWhen: () =>
                                {
                                    Assert.That(BarCounterMaterialResponse.SetResponseEnabled(bar.Room, response), Is.EqualTo(10));
                                    return true;
                                }));
                        }
                    }
                });
                foreach (string label in new[] { "Wood", "Brass taps", "Brass rail" })
                    Assert.That(PolishDifference(SceneIds.BarInterior, "Test bar - " + label + " uniform",
                        "Test bar - " + label + " response"), Is.GreaterThan(24));
                var arms = bar.DrinkShop.FirstPersonArms;
                Object.Destroy(bar.DrinkShop); yield return null;
                if (arms != null) Object.Destroy(arms);
                yield return null;

                CityGameRoot city = null;
                CityFountainReflectionController mirror = null;
                int captures = 0;
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () =>
                {
                    FreezeLightingPolish(city.Player.Motor);
                    city.DayNight.enabled = false; city.Weather.enabled = false;
                    grass = city.World.Root.GetComponentInChildren<CityCoastalGrassWind>(true);
                    Assert.That(grass, Is.Not.Null);
                    Assert.That(grass.StemCount, Is.InRange(1, CityCoastalGrassWind.MaximumStems));
                    Assert.That(grass.TryGetStem(0, out Vector3 root, out Vector3 tip), Is.True);
                    grass.DebugPhaseSeconds = 1f;
                    grass.DebugWind = new WindSample(90f, 1f);
                    grass.DebugInteractionPosition = root + Vector3.right * 2f;
                    Vector3 aim = Vector3.Lerp(root, tip, .55f);
                    Vector3 eye = aim + new Vector3(1.05f, .38f, -1.05f);
                    var shots = new List<Shot>
                    {
                        GrassShot("Still", true, false), GrassShot("Wind", false, false), GrassShot("Contact", false, true)
                    };
                    basin = CityFountainWaterResources.BasinMaterial;
                    Assert.That(basin.GetFloat("_EdgeFoamStrength"), Is.Zero);
                    Assert.That(basin.GetFloat("_CrestFoamStrength"), Is.Zero);
                    Assert.That(basin.GetFloat("_ReflectionBlur"), Is.EqualTo(.65f).Within(.001f));
                    Transform fountain = AuditFindByName(city.World.Root.transform, "Park Fountain Water");
                    Assert.That(fountain, Is.Not.Null);
                    Renderer water = null;
                    foreach (Renderer candidate in fountain.GetComponentsInChildren<Renderer>())
                        if (candidate.sharedMaterial == basin) { water = candidate; break; }
                    Assert.That(water, Is.Not.Null);
                    foreach (var candidate in Object.FindObjectsByType<CityFountainReflectionController>())
                        if (candidate.TargetMaterial == basin) { mirror = candidate; break; }
                    Assert.That(mirror, Is.Not.Null);
                    Vector3 center = water.bounds.center;
                    foreach (string label in new[] { "Legacy edge", "Clear edge", "Filtered reflection" })
                    {
                        string stage = label;
                        shots.Add(Shot.At("Test fountain - " + label, center + new Vector3(3.2f, 1.9f, -3.2f), center, 53f,
                            readyWhen: () =>
                            {
                                if (mirror.CaptureCount == 0) return false;
                                basin.SetFloat("_EdgeFoamStrength", stage == "Legacy edge" ? 1f : 0f);
                                basin.SetFloat("_ReflectionBlur", stage == "Filtered reflection" ? .65f : 0f);
                                captures = mirror.CaptureCount;
                                return true;
                            }));
                    }
                    CityRiverPromenadeDescriptor quay = city.Layout.River.Promenades[0];
                    CityRiverSegmentDescriptor segment = city.Layout.River.Segments[0];
                    Rect channel = segment.WaterBounds;
                    Vector3 riverAim = new Vector3(channel.center.x,
                        segment.AverageWaterY + CitySurfaceDescriptor.WaterTopOffset, channel.center.y);
                    shots.Add(Shot.At("Test river - Existing palette and reflection",
                        new Vector3(quay.Bounds.center.x, (quay.SouthY + quay.NorthY) * .5f + EyeHeight,
                            quay.Bounds.center.y - 3f), riverAim, 60f));
                    return shots.ToArray();
                    Shot GrassShot(string label, bool still, bool contact) =>
                        Shot.At("Test coast - " + label, eye, aim, 43f, readyWhen: () =>
                        {
                            grass.DebugDisableMotion = still;
                            grass.DebugInteractionPosition = root + Vector3.right * (contact ? .15f : 2f);
                            grass.RefreshPresentation(); return true;
                        });
                });
                Assert.That(PolishDifference(SceneIds.City, "Test coast - Still", "Test coast - Wind"), Is.GreaterThan(24));
                Assert.That(PolishDifference(SceneIds.City, "Test coast - Wind", "Test coast - Contact"), Is.GreaterThan(24));
                Assert.That(PolishDifference(SceneIds.City, "Test fountain - Legacy edge", "Test fountain - Clear edge"), Is.GreaterThan(24));
                Assert.That(PolishDifference(SceneIds.City, "Test fountain - Clear edge", "Test fountain - Filtered reflection"), Is.GreaterThan(24));
                Assert.That(mirror.ReflectionTexture.useMipMap, Is.True);
                Assert.That(mirror.ReflectionTexture.mipmapCount, Is.GreaterThan(1));
                Assert.That(mirror.ReflectionTexture.width, Is.EqualTo(128));
                Assert.That(mirror.ReflectionTexture.filterMode, Is.EqualTo(FilterMode.Trilinear));
                for (int i = 0; i < 8; i++) yield return null;
                Assert.That(mirror.CaptureCount, Is.EqualTo(captures), "Stable lighting must reuse the snapshot.");
                grass.DebugPhaseSeconds = null; grass.DebugWind = null; grass.DebugInteractionPosition = null;
                grass.DebugDisableMotion = false; grass.RefreshPresentation();

                yield return CaptureCrownAndSlidePolish();
            }
            finally
            {
                if (grass != null)
                {
                    grass.DebugPhaseSeconds = null; grass.DebugWind = null; grass.DebugInteractionPosition = null;
                    grass.DebugDisableMotion = false; grass.DebugDisableInteraction = false; grass.RefreshPresentation();
                }
                if (basin != null) { basin.SetFloat("_EdgeFoamStrength", 0f); basin.SetFloat("_ReflectionBlur", .65f); }
                foreach (var preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save(); GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame("Test material motion cleanup");
            }
        }

        [UnityTest, Timeout(180000)]
        [Explicit("Only the crown and slide views remaining after production material/water acceptance.")]
        public IEnumerator MaterialMotionPolishGeometry()
        {
            float previousDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test crown and metal materials");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                yield return CaptureCrownAndSlidePolish();
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame("Test crown and metal cleanup");
            }
        }

        private static IEnumerator CaptureCrownAndSlidePolish()
        {
            MountainRoadRoot road = null;
            yield return Capture(SceneIds.MountainRoad, () =>
            {
                road = Object.FindAnyObjectByType<MountainRoadRoot>();
                return road != null && road.IsInitialized ? road : null;
            }, () =>
            {
                FreezeLightingPolish(road.Player.Motor);
                road.Wind.enabled = false; road.Weather.enabled = false;
                var pilot = Object.FindAnyObjectByType<MountainRoadCrownGradientPilot>();
                Assert.That(pilot, Is.Not.Null);
                Mesh mesh = pilot.Target.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.colors32.Length, Is.EqualTo(mesh.vertexCount));
                MountainRoadForestDescriptor tree = road.Plan.Forest[0];
                bool found = false;
                foreach (MountainRoadForestDescriptor candidate in road.Plan.Forest)
                    if ((candidate.Position - pilot.Anchor).sqrMagnitude < .0001f)
                    { tree = candidate; found = true; break; }
                Assert.That(found, Is.True);
                var shots = new List<Shot>();
                // Use the measured crown radius/height, as the production
                // surface fixture does. A guessed wide view was occluded.
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 aim = tree.Position + Vector3.up * tree.Height * .42f;
                    Vector3 eye = tree.Position + road.Plan.Plateau.Right * side * (tree.CrownRadius + 2.3f) +
                        Vector3.up * tree.Height * .38f;
                    string label = side < 0f ? "Left" : "Right";
                    foreach (bool enabled in new[] { false, true })
                    {
                        bool gradient = enabled;
                        shots.Add(Shot.At("Test crowns - " + label + (enabled ? " vertical" : " flat"), eye, aim, 52f,
                            readyWhen: () => { pilot.SetGradientEnabled(gradient); return true; }));
                    }
                }
                return shots.ToArray();
            });
            int changed = PolishDifference(SceneIds.MountainRoad, "Test crowns - Left flat", "Test crowns - Left vertical") +
                PolishDifference(SceneIds.MountainRoad, "Test crowns - Right flat", "Test crowns - Right vertical");
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return null;
            CaptureSlideNormalPolish();
            Assert.That(changed, Is.GreaterThan(24), "The measured crown views must expose the selected pigment field.");
        }

        private static void CaptureSlideNormalPolish()
        {
            var stage = new GameObject("Test metal inspection");
            const int layer = 30;
            AmbientMode previousAmbient = RenderSettings.ambientMode;
            Color previousColor = RenderSettings.ambientLight;
            Mesh authored = null, clone = null;
            try
            {
                var pistol = CombatPistolAssetProvider.CreatePistol(stage.transform);
                foreach (Transform part in pistol.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
                MeshFilter slide = null;
                foreach (MeshFilter part in pistol.GetComponentsInChildren<MeshFilter>())
                    if (part.name == PistolSlideNormalPilot.TargetName) { slide = part; break; }
                Assert.That(slide, Is.Not.Null);
                authored = slide.sharedMesh;
                clone = Object.Instantiate(authored);
                var light = new GameObject("Test metal grazing light").AddComponent<Light>();
                light.transform.SetParent(stage.transform, false);
                light.type = LightType.Directional; light.intensity = 1.3f;
                light.cullingMask = 1 << layer;
                light.transform.rotation = Quaternion.Euler(28f, -38f, 0);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.13f, .15f, .18f);
                var camera = new GameObject("Test metal camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.gameObject.AddComponent<BarPromenade.Rendering.ScenePresentationExclusion>();
                camera.enabled = false; camera.cullingMask = 1 << layer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.065f, .075f, .09f);
                camera.nearClipPlane = .01f; camera.farClipPlane = 2f; camera.fieldOfView = 36f;
                Vector3 center = slide.GetComponent<Renderer>().bounds.center;
                foreach (float side in new[] { -1f, 1f })
                {
                    camera.transform.SetPositionAndRotation(center + new Vector3(side * .25f, .12f, -.18f),
                        Quaternion.LookRotation(new Vector3(-side * .25f, -.12f, .18f)));
                    string label = side < 0f ? "Left" : "Right";
                    clone.normals = PistolSlideNormalPilot.CreateFlatBaseline(authored);
                    slide.sharedMesh = clone;
                    CaptureCurrentCamera(camera, "Materials", "Test steel - " + label + " flat");
                    slide.sharedMesh = authored;
                    CaptureCurrentCamera(camera, "Materials", "Test steel - " + label + " weighted");
                    Assert.That(PolishDifference("Materials", "Test steel - " + label + " flat",
                        "Test steel - " + label + " weighted"), Is.GreaterThan(24));
                }
            }
            finally
            {
                Object.DestroyImmediate(stage);
                if (clone != null) Object.DestroyImmediate(clone);
                RenderSettings.ambientMode = previousAmbient; RenderSettings.ambientLight = previousColor;
            }
        }
    }
}
