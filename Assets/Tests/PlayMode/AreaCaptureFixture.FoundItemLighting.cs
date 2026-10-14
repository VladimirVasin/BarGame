using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private const uint FoundItemInspectionLayer = 1u << 5;

        [UnityTest]
        [Explicit("Shared found-item lighting remains isolated and readable in the dark warehouse.")]
        [PrebuildSetup(typeof(CombatFlashlightCaptureSetup))]
        public IEnumerator CombatWarehouseFoundItemLighting()
        {
            float previousCaptureDelta = Time.captureDeltaTime;
            CombatTestRoot root = null;
            GameObject sources = null;
            WorldItemFoundScreen found = null;
            Time.captureDeltaTime = 1f / 60f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized && root.WarehousePlan != null, Is.True);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                root.SetOpponentFocus(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                Camera camera = root.CameraFollow.Camera;
                root.CameraFollow.enabled = false;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 3.4f, 7.6f),
                    Quaternion.LookRotation(new Vector3(0f, 2.7f, 9f) - new Vector3(0f, 3.4f, 7.6f)));
                root.Flashlight.SpotLight.enabled = false;
                yield return null;
                Vector4 origin = Shader.GetGlobalVector("_WarehouseAmbientOrigin");
                Assert.That(origin.w, Is.EqualTo(1f));
                Assert.That(Vector3.Distance(camera.transform.position,
                    new Vector3(origin.x, origin.y, origin.z)), Is.GreaterThan(WarehouseArenaLighting.FarDistance));
                float originalWorld = ReadFoundItemFrame(camera, null, null).WorldCorner;
                Assert.That(originalWorld, Is.LessThanOrEqualTo(1f / 255f));
                found = WorldItemFoundScreen.For(root.Player.Interactor);
                Assert.That(found != null && found.IsInitialized, Is.True);
                sources = new GameObject("Test found item sources");
                sources.transform.SetParent(root.transform, false);
                Light[] cachedLights = null;
                int commits = 0, completions = 0;
                InventoryItemId[] representatives = {
                    InventoryItemId.CombatPistolAmmo, InventoryItemId.CombatShotgun, InventoryItemId.Scarf };
                string[] descriptions = { "ammunition", "shotgun", "scarf" };
                for (int item = 0; item < representatives.Length; item++)
                {
                    Transform model = InventoryItemModelFactory.BuildPreviewModel(representatives[item], sources.transform);
                    model.name = "Test found " + descriptions[item];
                    model.localPosition = new Vector3(-2f, .5f, 0f);
                    model.localRotation = Quaternion.Euler(0f, 23f, 0f);
                    Vector3 originalPosition = model.localPosition, originalScale = model.localScale;
                    Quaternion originalRotation = model.localRotation;
                    Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                    var masks = new uint[renderers.Length];
                    var materials = new Material[renderers.Length][];
                    var blocks = new bool[renderers.Length];
                    for (int renderer = 0; renderer < renderers.Length; renderer++)
                    {
                        masks[renderer] = renderers[renderer].renderingLayerMask;
                        materials[renderer] = renderers[renderer].sharedMaterials;
                        blocks[renderer] = renderers[renderer].HasPropertyBlock();
                    }
                    Assert.That(found.TryPresent(root.Player.Interactor, representatives[item], model,
                        () => { commits++; return false; }, taken => { Assert.That(taken, Is.False); completions++; }), Is.True);
                    yield return WaitForFoundItemLighting(found, showing: true);
                    Light[] lights = FoundItemPreviewLights(camera);
                    Assert.That(lights.Length, Is.EqualTo(2));
                    if (cachedLights == null) cachedLights = lights;
                    else CollectionAssert.AreEquivalent(cachedLights, lights,
                        "The shared presenter must reuse its two lights on later pickups.");
                    foreach (Light light in lights)
                    {
                        Assert.That(light.isActiveAndEnabled && light.intensity > 0f, Is.True);
                        Assert.That(light.type, Is.EqualTo(LightType.Point));
                        Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
                        Assert.That(light.range, Is.LessThanOrEqualTo(3f));
                    }
                    for (int renderer = 0; renderer < renderers.Length; renderer++)
                    {
                        Assert.That(renderers[renderer].renderingLayerMask, Is.EqualTo(FoundItemInspectionLayer));
                        CollectionAssert.AreEqual(materials[renderer], renderers[renderer].sharedMaterials);
                        Assert.That(renderers[renderer].HasPropertyBlock(), Is.EqualTo(blocks[renderer]));
                    }
                    Renderer backdrop = FindFoundItemBackdrop(camera);
                    FoundItemFrameReading reading = ReadFoundItemFrame(camera, backdrop,
                        "Test found " + descriptions[item] + " in dark warehouse");
                    Assert.That(reading.VisibleSamples, Is.GreaterThan(100),
                        "The actual authored item must have visibly lit pixels beyond the hero's ambient radius.");
                    Assert.That(reading.WorldCorner, Is.LessThanOrEqualTo(originalWorld + 1f / 255f),
                        "Preview lighting must leave the actual warehouse wall dark even without its backdrop.");
                    Quaternion shownRotation = model.rotation;
                    for (int frame = 0; frame < 36; frame++) yield return null;
                    Assert.That(Quaternion.Angle(shownRotation, model.rotation), Is.GreaterThan(1f));
                    reading = ReadFoundItemFrame(camera, backdrop,
                        "Test found " + descriptions[item] + " rotated in preview");
                    Assert.That(reading.VisibleSamples, Is.GreaterThan(100));
                    Assert.That(Shader.GetGlobalVector("_WarehouseAmbientOrigin"), Is.EqualTo(origin),
                        "An item preview must not change the warehouse's hero-centred visibility globals.");
                    if (!Application.isBatchMode) yield return CaptureFoundItemGameView("Test found " + descriptions[item] + " pickup screen");

                    if (item == 1) found.enabled = false;
                    else Assert.That(found.Dismiss(), Is.True);
                    yield return WaitForFoundItemLighting(found, showing: false);
                    Assert.That(model.parent, Is.SameAs(sources.transform));
                    Assert.That(Vector3.Distance(model.localPosition, originalPosition), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(model.localRotation, originalRotation), Is.LessThan(.01f));
                    Assert.That(Vector3.Distance(model.localScale, originalScale), Is.LessThan(.0001f));
                    for (int renderer = 0; renderer < renderers.Length; renderer++)
                    {
                        Assert.That(renderers[renderer].renderingLayerMask, Is.EqualTo(masks[renderer]));
                        CollectionAssert.AreEqual(materials[renderer], renderers[renderer].sharedMaterials);
                        Assert.That(renderers[renderer].HasPropertyBlock(), Is.EqualTo(blocks[renderer]));
                    }
                    foreach (Light light in cachedLights) Assert.That(light.isActiveAndEnabled, Is.False);
                    Assert.That(root.Player.Interactor.InputEnabled && root.Player.Motor.InputEnabled, Is.True);
                    found.enabled = true;
                    Object.Destroy(model.gameObject);
                    yield return null;
                }
                Assert.That(commits, Is.Zero);
                Assert.That(completions, Is.EqualTo(representatives.Length));
                Assert.That(FoundItemPreviewLights(camera).Length, Is.EqualTo(2));
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                foreach (Light light in cachedLights) Assert.That(light == null, Is.True,
                    "The shared inspection lights must be destroyed with their camera/presenter owner.");
            }
            finally
            {
                if (found != null) found.Abandon();
                if (sources != null) Object.Destroy(sources);
                if (root != null) root.AutomaticSimulation = false;
                Time.captureDeltaTime = previousCaptureDelta;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static IEnumerator WaitForFoundItemLighting(WorldItemFoundScreen screen, bool showing)
        {
            for (int frame = 0; frame < 90; frame++)
            {
                if (showing ? screen.IsShowing : !screen.IsPresenting) yield break;
                yield return null;
            }
            Assert.Fail("The shared found-item screen did not complete its bounded presentation transition.");
        }

        private static Light[] FoundItemPreviewLights(Camera camera)
        {
            Light[] all = camera.GetComponentsInChildren<Light>(true);
            return Array.FindAll(all, light => (uint)light.GetUniversalAdditionalLightData().renderingLayers == FoundItemInspectionLayer);
        }

        private static Renderer FindFoundItemBackdrop(Camera camera)
        {
            foreach (Renderer renderer in camera.GetComponentsInChildren<Renderer>(true))
                if (renderer.name == "World Item Found Backdrop") return renderer;
            Assert.Fail("The production found-item presenter must own its camera-local backdrop.");
            return null;
        }

        private readonly struct FoundItemFrameReading
        {
            internal readonly int VisibleSamples;
            internal readonly float WorldCorner;
            internal FoundItemFrameReading(int visible, float worldCorner)
            { VisibleSamples = visible; WorldCorner = worldCorner; }
        }

        private static FoundItemFrameReading ReadFoundItemFrame(Camera camera, Renderer backdrop, string shot)
        {
            bool backdropEnabled = backdrop != null && backdrop.enabled;
            var target = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            try
            {
                // The wall must remain dark because the lights are isolated,
                // rather than merely because the screen veil covers it.
                if (backdrop != null) backdrop.enabled = false;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply();
                int visible = 0, cornerSamples = 0;
                float corner = 0f;
                for (int y = 0; y < Height; y += 4)
                    for (int x = 0; x < Width; x += 4)
                    {
                        Color color = pixels.GetPixel(x, y);
                        float value = color.r * .2126f + color.g * .7152f + color.b * .0722f;
                        if (value > .025f) visible++;
                        if (x < Width / 6 && y > Height * 2 / 3) { corner += value; cornerSamples++; }
                    }
                if (!string.IsNullOrEmpty(shot))
                {
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest);
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, shot + ".png"), pixels.EncodeToPNG());
                }
                return new FoundItemFrameReading(visible, corner / Mathf.Max(1, cornerSamples));
            }
            finally
            {
                if (backdrop != null) backdrop.enabled = backdropEnabled;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(pixels);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static IEnumerator CaptureFoundItemGameView(string shot)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, shot + ".png");
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 5f;
            for (int frame = 0; frame < 180 && Time.realtimeSinceStartup < deadline; frame++)
            {
                yield return null;
                if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous && new FileInfo(path).Length > 8) yield break;
            }
            Assert.Fail("The actual found-item Game view UI screenshot was not written.");
        }
    }
}
