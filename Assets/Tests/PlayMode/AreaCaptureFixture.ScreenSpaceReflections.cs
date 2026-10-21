using System.Collections;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static IEnumerator CaptureScreenSpaceReflectionWorlds()
        {
            SceneScreenSpaceReflectionFeature feature = null;
            GraphicsEffectsSettings.CleanReflectionsEnabled = true;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot city = null;
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (city != null && city.IsInitialized) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True);
                FreezeLightingPolish(city.Player.Motor);
                city.DayNight.enabled = false; city.Weather.enabled = false;
                RuntimeSceneSetup.ApplyCityExteriorLighting(GameTimeDayNightRules.Evaluate(12 * 60), false);
                CityWetSurfaceRegistry.SetImmediate(1f);
                foreach (SceneScreenSpaceReflectionFeature candidate in Resources.FindObjectsOfTypeAll<SceneScreenSpaceReflectionFeature>())
                    if (candidate.isActive) { feature = candidate; break; }
                Assert.That(feature, Is.Not.Null, "The configured PC renderer must include native SSR.");
                Assert.That(city.Lighting.Reflection.RoadReceiverCount, Is.GreaterThan(0));
                AssertSsrRoadCoverage(city);
                BuildingLot bar = null;
                foreach (BuildingLot lot in city.Layout.BuildingLots) if (lot.IsBar) { bar = lot; break; }
                Assert.That(bar, Is.Not.Null);
                Vector3 forward = new Vector3(bar.FrontageDirection.x, 0f, bar.FrontageDirection.y).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Camera camera = city.Camera;
                using (var capture = new SsrCaptureTarget(camera))
                {
                    SetSsrPose(camera, bar.DoorPosition + forward * 7f + right * 2.4f + Vector3.up * .65f,
                        bar.DoorPosition + forward * .6f + Vector3.up * .65f, 60f);
                    yield return WarmSsrCamera(camera, feature, () => city.Lighting.Reflection.AllReady);
                    CaptureSsrPair(capture, feature, SceneIds.City, "Test wet street");

                    camera.transform.position += right * .15f;
                    yield return WarmSsrCamera(camera, feature);
                    capture.Save(SceneIds.City, "Test wet street - Moving view");
                    camera.transform.Rotate(Vector3.up, 50f, Space.World);
                    camera.Render();
                    Assert.That(feature.LastCameraReady, Is.False, "A camera cut must fall back while history refreshes.");
                    yield return WarmSsrCamera(camera, feature);
                    capture.Save(SceneIds.City, "Test wet street - Screen edge fallback");
                    yield return AssertSsrCameraExclusion(camera, feature);

                    CityCoastalGrassWind grass = city.World.Root.GetComponentInChildren<CityCoastalGrassWind>(true);
                    Assert.That(grass, Is.Not.Null);
                    Assert.That(grass.TryGetStem(0, out Vector3 stemRoot, out Vector3 tip), Is.True);
                    Vector3 aim = Vector3.Lerp(stemRoot, tip, .55f);
                    SetSsrPose(camera, aim + new Vector3(1.05f, .38f, -1.05f), aim, 43f);
                    grass.DebugWind = new WindSample(90f, 1f);
                    grass.DebugPhaseSeconds = 1f;
                    grass.DebugInteractionPosition = stemRoot + Vector3.right * 2f;
                    grass.RefreshPresentation();
                    yield return WarmSsrCamera(camera, feature);
                    yield return null;
                    grass.DebugPhaseSeconds = 2f;
                    grass.RefreshPresentation(); camera.Render();
                    Assert.That(grass.PreviousRenderedPhaseSeconds, Is.EqualTo(1f));
                    grass.DebugPhaseSeconds = 3f;
                    grass.RefreshPresentation(); camera.Render();
                    Assert.That(grass.PreviousRenderedPhaseSeconds, Is.EqualTo(1f), "Repeated same-frame renders cannot advance previous wind.");
                    capture.Save(SceneIds.City, "Test coast - Wind motion history");
                    grass.enabled = false; grass.enabled = true; camera.Render();
                    Assert.That(grass.PreviousRenderedPhaseSeconds, Is.EqualTo(grass.AppliedPhaseSeconds));
                    grass.DebugPhaseSeconds = null; grass.DebugWind = null; grass.DebugInteractionPosition = null;
                    grass.RefreshPresentation();
                }
                CityReflectionController oldCityReflection = city.Lighting.Reflection;
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                Assert.That(oldCityReflection == null, Is.True);
                CombatTestRoot warehouse = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(warehouse != null && warehouse.IsInitialized, Is.True);
                warehouse.AutomaticSimulation = false;
                warehouse.SetDuelLogging(false);
                FreezeLightingPolish(warehouse.Player.Motor);
                warehouse.CameraFollow.enabled = false;
                WarehouseCleanPresentation lighting = warehouse.GetComponentInChildren<WarehouseCleanPresentation>();
                Assert.That(lighting, Is.Not.Null);
                WarehouseAtmosphere air = warehouse.GetComponentInChildren<WarehouseAtmosphere>();
                if (air != null) air.enabled = false;
                camera = warehouse.CameraFollow.Camera;
                using (var capture = new SsrCaptureTarget(camera))
                {
                    SetSsrPose(camera, new Vector3(1.8f, .75f, -6.8f), new Vector3(-2.3f, .55f, 2.8f), 65f);
                    yield return WarmSsrCamera(camera, feature, () => lighting.Reflection.IsReady);
                    CaptureSsrPair(capture, feature, SceneIds.CombatTest, "Test wet warehouse floor");
                    AssertWarehouseSurfaceMaterials(warehouse);
                    yield return AssertSsrCameraExclusion(camera, feature);
                    GraphicsEffectsSettings.CleanReflectionsEnabled = false;
                    camera.Render();
                    Assert.That(feature.LastCameraEligible, Is.False);
                    Assert.That(Shader.GetGlobalFloat("_SceneScreenSpaceReflectionReady"), Is.Zero);
                    GraphicsEffectsSettings.CleanReflectionsEnabled = true;
                    yield return WarmSsrCamera(camera, feature);

                    Light source = lighting.FanLight;
                    Assert.That(source, Is.Not.Null);
                    bool oldEnabled = source.enabled;
                    source.enabled = false; camera.Render();
                    Assert.That(feature.LastCameraReady, Is.False, "Source removal invalidates reflected history immediately.");
                    source.enabled = oldEnabled;
                    yield return WarmSsrCamera(camera, feature);

                    Assert.That(GameSessionState.WarehouseFishLamp.TryBegin(true, true), Is.True);
                    WarehouseFishLamp lamp = warehouse.GetComponentInChildren<WarehouseFishLamp>();
                    Assert.That(lamp, Is.Not.Null);
                    lamp.Advance(WarehouseFishLampState.HeatingSeconds);
                    camera.Render();
                    Assert.That(lighting.IsBlackout, Is.True);
                    Assert.That(feature.LastCameraEligible, Is.False);
                    capture.Save(SceneIds.CombatTest, "Test wet warehouse floor - Blackout");
                    lighting.enabled = false;
                    camera.Render();
                    Assert.That(feature.LastCameraEligible, Is.False);
                }
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(lighting == null, Is.True);
                Assert.That(Object.FindObjectsByType<SceneScreenSpaceReflectionScope>().Length, Is.Zero);
            }
            finally
            {
                if (feature != null) { feature.DebugDisableTracing = false; feature.ResetHistory(); }
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static void AssertSsrRoadCoverage(CityGameRoot city)
        {
            int roads = 0;
            var properties = new MaterialPropertyBlock();
            foreach (MeshRenderer renderer in city.transform.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null || material.shader.name != "Bar Promenade/Layered Ground") continue;
                    int index = CitySurfaceLibrary.PropertyIndex(renderer, materials.Length, slot, properties);
                    CitySurfaceLibrary.ReadProperties(renderer, index, properties);
                    if (properties.GetFloat("_GroundRoadCoordinates") > .5f && properties.GetFloat("_GroundKind") < .5f) roads++;
                }
            }
            Assert.That(city.Lighting.Reflection.RoadReceiverCount, Is.EqualTo(roads), "Every registered road slot must retain fallback reflection coverage.");
        }

        private static IEnumerator WarmSsrCamera(Camera camera, SceneScreenSpaceReflectionFeature feature,
            System.Func<bool> fallbackReady = null)
        {
            feature.DebugDisableTracing = false;
            for (int frame = 0; frame < 180; frame++)
            {
                camera.Render();
                if (frame >= 5 && feature.LastCameraReady && (fallbackReady == null || fallbackReady())) yield break;
                yield return null;
            }
            Assert.Fail("Native SSR and its bounded fallback did not become ready.");
        }

        private static void CaptureSsrPair(SsrCaptureTarget capture, SceneScreenSpaceReflectionFeature feature,
            string scene, string name)
        {
            capture.Camera.Render();
            Assert.That(feature.LastCameraReady, Is.True);
            capture.Save(scene, name + " - SSR");
            feature.DebugDisableTracing = true;
            capture.Camera.Render();
            Assert.That(feature.LastCameraEligible, Is.False);
            Assert.That(GraphicsEffectsSettings.CleanReflectionsEnabled, Is.True);
            capture.Save(scene, name + " - Cubemap fallback");
            Assert.That(PolishDifference(scene, name + " - SSR", name + " - Cubemap fallback"), Is.GreaterThan(0),
                "Actual scene pixels must show native SSR contribution at a justified receiver.");
            feature.DebugDisableTracing = false;
        }

        private static IEnumerator AssertSsrCameraExclusion(Camera camera, SceneScreenSpaceReflectionFeature feature)
        {
            var exclusion = camera.gameObject.AddComponent<ScenePresentationExclusion>();
            camera.Render();
            Assert.That(feature.LastCameraEligible, Is.False);
            Assert.That(Shader.GetGlobalFloat("_SceneScreenSpaceReflectionReady"), Is.Zero);
            Object.DestroyImmediate(exclusion);
            yield return WarmSsrCamera(camera, feature);
        }

        private static void SetSsrPose(Camera camera, Vector3 eye, Vector3 aim, float fov)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            camera.fieldOfView = fov;
        }

        private sealed class SsrCaptureTarget : System.IDisposable
        {
            public Camera Camera { get; }
            private readonly RenderTexture target, previous;
            private readonly Texture2D pixels;
            private readonly float oldAspect;
            public SsrCaptureTarget(Camera camera)
            {
                Camera = camera; previous = camera.targetTexture; oldAspect = camera.aspect;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.DefaultHDR);
                target.Create();
                pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                camera.targetTexture = target;
                camera.aspect = (float)Width / Height;
            }
            public void Save(string scene, string name)
            {
                RenderTexture active = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                    Assert.That(IsBlank(pixels), Is.False);
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", scene);
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
                    Debug.Log("Screen space reflection capture: " + scene + "/" + name);
                }
                finally { RenderTexture.active = active; }
            }
            public void Dispose()
            {
                if (Camera != null) { Camera.targetTexture = previous; Camera.aspect = oldAspect; }
                Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
