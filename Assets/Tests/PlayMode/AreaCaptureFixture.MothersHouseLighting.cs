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
        [Explicit("Focused mother's house surfaces, native lighting, room boundaries and cleanup acceptance.")]
        [PrebuildSetup(typeof(MothersHouseLivingRoomAssetsSetup))]
        public IEnumerator MothersHouseNativeLighting()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            MothersHouseInteriorRoot house = null;
            float previousCaptureDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test mother's house lighting");
                GameSessionState.TryStartGameTimeAt(12 * 60);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                SetHouseNativeEffects(true);
                yield return Capture(SceneIds.MothersHouseInterior,
                    () =>
                    {
                        house = Object.FindAnyObjectByType<MothersHouseInteriorRoot>();
                        return house != null && house.IsInitialized ? house : null;
                    },
                    () => HouseNativeLightingShots(house));

                AssertHouseLightingDifference("Living original", "Living native lighting");
                AssertHouseLightingDifference("Air baseline", "Local lit air");
                AssertHouseLightingDifference("Surface baseline", "Surface response");
                AssertHouseLightingDifference("Bathroom original", "Bathroom native lighting");
                SceneCompositeRendererFeature feature = FindVillageLightingFeature();
                Assert.That(feature.DebugMothersHouseVolumeSourceCount, Is.InRange(1, 2));
                Assert.That(feature.DebugMothersHouseVolumeResolution.x, Is.InRange(1, 320));
                Assert.That(feature.DebugMothersHouseVolumeResolution.y, Is.InRange(1, 180));
                Assert.That(house.Lighting.ActiveRoomId, Is.EqualTo(MothersHouseLightingRoom.Bathroom));
                yield return VerifyHouseLightingOwnership(house);
            }
            finally
            {
                if (house != null && house.World != null) SetHouseSurfaceResponse(house, true);
                if (house != null && house.Atmosphere != null && house.Atmosphere.FireFlicker != null)
                    house.Atmosphere.FireFlicker.enabled = true;
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test mother's house lighting cleanup");
            }
        }

        private static Shot[] HouseNativeLightingShots(MothersHouseInteriorRoot root)
        {
            Assert.That(root.Lighting, Is.Not.Null);
            Assert.That(root.Lighting.Reflection, Is.Not.Null);
            Assert.That(root.Lighting.Reflection.ZoneCount, Is.EqualTo(2));
            Assert.That(root.Lighting.HalationSourceCount, Is.InRange(1, 24));
            Assert.That(root.Lighting.BounceRegionCount, Is.InRange(1, 4));
            Assert.That(root.Lighting.RoomCount, Is.EqualTo(7));
            for (int patch = 0; patch < root.Lighting.BounceRegionCount; patch++)
            {
                Assert.That(root.Lighting.TryGetBounceRegion(patch, out var region), Is.True);
                if (region.Room == MothersHouseLightingRoom.GroundMain)
                {
                    Bounds protectedChair = root.Lighting.ProtectedChairBounds;
                    protectedChair.Expand(-.002f);
                    Assert.That(region.Bounds.Intersects(protectedChair), Is.False,
                        "New bounce must preserve the existing pool around the mother and chair.");
                }
            }
            Assert.That(root.Atmosphere.GetComponentsInChildren<Light>(true).Length,
                Is.EqualTo(MothersHouseInteriorAtmosphere.PracticalLightCount +
                    MothersHouseInteriorAtmosphere.HearthBounceLightCount),
                "The new presentation must reuse the home's actual lamps.");
            AssertHouseLightingShaders();
            AssertHouseSurfaceTextures(root);
            Assert.That(root.Layout.TryGetCameraShot(HomeCameraShotKind.MainRoom, out HomeCameraShot main), Is.True);
            Assert.That(root.Layout.TryGetCameraShot(HomeCameraShotKind.UpperBathroom, out HomeCameraShot bathroom), Is.True);
            Vector3 mainAim = main.Position + main.Rotation * Vector3.forward * 10f;
            Vector3 bathAim = bathroom.Position + bathroom.Rotation * Vector3.forward * 10f;
            Vector3 scarfPosition = MothersHouseScarfPickupPlan.Create(root.Layout).Position + Vector3.up * .04f;
            double warmDeadline = 0, deadline = 0;
            bool capturePathWarmed = false;
            int previousFailures = 0;
            return new[]
            {
                Shot.At(HouseLightingFrame("Living original"), main.Position, mainAim, main.FieldOfView, readyWhen: () =>
                {
                    if (warmDeadline == 0)
                    {
                        // Cubemap faces can compile variants absent from the game camera.
                        // Warm both real capture paths before testing the normal watchdog.
                        warmDeadline = Time.realtimeSinceStartupAsDouble + 90d;
                    }
                    root.Lighting.PrepareForCamera(Camera.main);
                    if (!capturePathWarmed)
                    {
                        var reflection = root.Lighting.Reflection;
                        if (reflection.LivingRoom.CaptureCount == 0 || reflection.Bathroom.CaptureCount == 0 ||
                            reflection.CaptureInProgress)
                        {
                            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(warmDeadline),
                                "Both real reflection paths must finish shader warmup.");
                            return false;
                        }
                        previousFailures = HouseReflectionFailures(root);
                        reflection.Rearm();
                        capturePathWarmed = true;
                        deadline = Time.realtimeSinceStartupAsDouble + 25d;
                        root.Lighting.PrepareForCamera(Camera.main);
                    }
                    if (!root.Lighting.Reflection.IsReady)
                    {
                        Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline),
                            "Both bounded room reflections must complete after camera warmup.");
                        return false;
                    }
                    Assert.That(HouseReflectionFailures(root), Is.EqualTo(previousFailures));
                    SetHouseNativeEffects(false);
                    return true;
                }),
                Shot.At(HouseLightingFrame("Living native lighting"), main.Position, mainAim, main.FieldOfView,
                    readyWhen: () => { SetHouseNativeEffects(true); return true; }),
                Shot.At(HouseLightingFrame("Air baseline"), main.Position, mainAim, main.FieldOfView,
                    readyWhen: () => { GraphicsEffectsSettings.CleanVolumeEnabled = false; return true; }),
                Shot.At(HouseLightingFrame("Local lit air"), main.Position, mainAim, main.FieldOfView,
                    readyWhen: () => { GraphicsEffectsSettings.CleanVolumeEnabled = true; return true; }),
                Shot.At(HouseLightingFrame("Surface baseline"), main.Position, mainAim, main.FieldOfView,
                    readyWhen: () => { SetHouseSurfaceResponse(root, false); return true; }),
                Shot.At(HouseLightingFrame("Surface response"), main.Position, mainAim, main.FieldOfView,
                    readyWhen: () => { SetHouseSurfaceResponse(root, true); return true; }),
                Shot.At(HouseLightingFrame("Wood and fabric"), new Vector3(1.6f, 1.8f, -2.2f),
                    new Vector3(-1.65f, .65f, -.1f), 55f),
                Shot.At(HouseLightingFrame("Tea surfaces"), new Vector3(1.3f, 1.45f, -1.25f),
                    new Vector3(0f, .5f, 0f), 48f),
                MothersHouseShot(root, HomeCameraShotKind.StairAndUpperCorridor, HouseLightingFrame("Corridor")),
                MothersHouseShot(root, HomeCameraShotKind.UpperNorthRoom, HouseLightingFrame("Parents bedroom")),
                MothersHouseShot(root, HomeCameraShotKind.UpperSouthRoom, HouseLightingFrame("Childhood room")),
                Shot.At(HouseLightingFrame("Scarf cloth"), scarfPosition + new Vector3(.45f, .6f, -.45f),
                    scarfPosition, 40f),
                Shot.At(HouseLightingFrame("Bathroom original"), bathroom.Position, bathAim, bathroom.FieldOfView,
                    readyWhen: () => { SetHouseNativeEffects(false); return true; }),
                Shot.At(HouseLightingFrame("Bathroom native lighting"), bathroom.Position, bathAim, bathroom.FieldOfView,
                    readyWhen: () => { SetHouseNativeEffects(true); return true; })
            };
        }

        private static IEnumerator VerifyHouseLightingOwnership(MothersHouseInteriorRoot house)
        {
            Camera camera = Camera.main;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(house.Lighting));
            ScenePresentationExclusion exclusion = camera.gameObject.AddComponent<ScenePresentationExclusion>();
            try
            {
                CaptureCurrentCamera(camera, SceneIds.MothersHouseInterior, HouseLightingFrame("Excluded camera"));
                Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
                Assert.That(Shader.GetGlobalVector("_WarehouseCleanEffects"), Is.EqualTo(Vector4.zero));
                Assert.That(Shader.GetGlobalInt("_SceneSurfaceReflectionCount"), Is.Zero);
            }
            finally { Object.DestroyImmediate(exclusion); }

            var reflection = house.Lighting.Reflection;
            int captures = reflection.LivingRoom.CaptureCount + reflection.Bathroom.CaptureCount;
            float fireTime = house.Atmosphere.FireFlicker.ElapsedSeconds;
            for (int frame = 0; frame < 45; frame++) yield return null;
            Assert.That(house.Atmosphere.FireFlicker.ElapsedSeconds, Is.GreaterThan(fireTime));
            Assert.That(reflection.LivingRoom.CaptureCount + reflection.Bathroom.CaptureCount, Is.EqualTo(captures),
                "Live flame variation must not continuously recapture static room environments.");
            Assert.That(reflection.LivingRoom.CaptureInProgress && reflection.Bathroom.CaptureInProgress, Is.False);
            house.Lighting.enabled = false;
            CaptureCurrentCamera(camera, SceneIds.MothersHouseInterior, HouseLightingFrame("Owner disabled"));
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
            Assert.That(Shader.GetGlobalInt("_SceneSurfaceReflectionCount"), Is.Zero);
            house.Lighting.enabled = true;
            double deadline = Time.realtimeSinceStartupAsDouble + 25d;
            while (!reflection.IsReady && Time.realtimeSinceStartupAsDouble < deadline)
            {
                house.Lighting.PrepareForCamera(camera);
                Assert.That(reflection.LivingRoom.CaptureInProgress && reflection.Bathroom.CaptureInProgress, Is.False);
                yield return null;
            }
            Assert.That(reflection.IsReady, Is.True, "Re-enabling the home must restore its room captures.");
            Debug.Log("Mother's house reflection CPU/wall diagnostics: living=" +
                reflection.LivingRoom.CaptureSubmitTicks + "/" + reflection.LivingRoom.CapturePollTicks + "/" +
                reflection.LivingRoom.LastCaptureLatencySeconds + "; bathroom=" +
                reflection.Bathroom.CaptureSubmitTicks + "/" + reflection.Bathroom.CapturePollTicks + "/" +
                reflection.Bathroom.LastCaptureLatencySeconds + ". GPU cost remains unmeasured.");
        }

        private static int HouseReflectionFailures(MothersHouseInteriorRoot root) =>
            root.Lighting.Reflection.LivingRoom.CaptureFailureCount + root.Lighting.Reflection.Bathroom.CaptureFailureCount;

        private static string HouseLightingFrame(string description) => "Test mother's house - " + description;

        private static void SetHouseNativeEffects(bool enabled)
        {
            GraphicsEffectsSettings.CleanReflectionsEnabled = enabled;
            GraphicsEffectsSettings.CleanVolumeEnabled = enabled;
            GraphicsEffectsSettings.CleanHalationEnabled = enabled;
            GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = enabled;
            GraphicsEffectsSettings.CleanBounceEnabled = enabled;
        }

        private static void AssertHouseLightingShaders()
        {
            foreach (string resource in new[] { "Shaders/Lit", "Shaders/MothersHouseLightVolume", "Shaders/MothersHouseSurface" })
            {
                Shader shader = Resources.Load<Shader>(resource);
                Assert.That(shader, Is.Not.Null, resource);
                Assert.That(shader.isSupported, Is.True, resource);
#if UNITY_EDITOR
                Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader), Is.False, resource);
#endif
            }
        }

        private static void AssertHouseSurfaceTextures(MothersHouseInteriorRoot house)
        {
            var registry = house.World.Registry;
            Assert.That(registry.SurfaceContract, Is.Not.Null);
            Assert.That(registry.SurfaceContract.GpuBytes, Is.LessThanOrEqualTo(30L * 1024 * 1024));
            Texture2D[] textures = { registry.SurfaceAtlas, registry.SurfaceResponse, registry.PositiveAtlas.Texture };
            int[] sizes = { 4096, 2048, 1024 };
            for (int index = 0; index < textures.Length; index++)
            {
                Texture2D texture = textures[index];
                Assert.That(texture, Is.Not.Null);
                Assert.That(texture.width, Is.EqualTo(sizes[index]));
                Assert.That(texture.height, Is.EqualTo(sizes[index]));
                Assert.That(texture.format, Is.EqualTo(TextureFormat.BC7));
                Assert.That(texture.mipmapCount, Is.GreaterThan(1));
                Assert.That(texture.isReadable, Is.False);
            }
            var properties = new MaterialPropertyBlock();
            int unique = 0;
            foreach (var part in registry.Parts)
            {
                part.Renderer.GetPropertyBlock(properties);
                if (part.SurfaceUniqueUv)
                {
                    unique++;
                    Assert.That(part.Renderer.sharedMaterial.shader.name, Is.EqualTo("Bar Promenade/Mothers House Surface"));
                    Assert.That(properties.GetTexture("_BaseMap"), Is.SameAs(registry.SurfaceAtlas));
                    Assert.That(properties.GetTexture("_SceneMaterialResponse"), Is.SameAs(registry.SurfaceResponse));
                    Assert.That(part.BaseMapTransform, Is.EqualTo(new Vector4(1, 1, 0, 0)));
                }
                else Assert.That(properties.GetTexture("_BaseMap"), Is.SameAs(registry.PositiveAtlas.Texture));
                properties.Clear();
            }
            Assert.That(unique, Is.EqualTo(registry.SurfaceContract.UniquePartCount).And.GreaterThan(0));
            Assert.That(PlayerScarfResources.SharedMaterial.GetTexture("_BaseMap"), Is.SameAs(registry.PositiveAtlas.Texture),
                "Folded and worn scarf must retain the compact house cloth instead of loading the surface atlas.");
        }

        private static void SetHouseSurfaceResponse(MothersHouseInteriorRoot house, bool enabled)
        {
            var properties = new MaterialPropertyBlock();
            foreach (var part in house.World.Registry.Parts)
            {
                if (!part.SurfaceUniqueUv || part.Renderer == null) continue;
                properties.Clear(); part.Renderer.GetPropertyBlock(properties);
                properties.SetFloat("_SceneResponseStrength", enabled ? 1f : 0f);
                properties.SetFloat("_SceneNormalStrength", enabled ? .65f : 0f);
                part.Renderer.SetPropertyBlock(properties);
            }
        }

        private static void AssertHouseLightingDifference(string before, string after)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.MothersHouseInterior);
            var baseline = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var actual = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(baseline.LoadImage(File.ReadAllBytes(Path.Combine(folder, HouseLightingFrame(before) + ".png"))), Is.True);
                Assert.That(actual.LoadImage(File.ReadAllBytes(Path.Combine(folder, HouseLightingFrame(after) + ".png"))), Is.True);
                Color32[] a = baseline.GetPixels32(), b = actual.GetPixels32();
                int changed = 0;
                for (int i = 0; i < a.Length; i++)
                    if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) >= 2) changed++;
                Debug.Log("Mother's house native lighting pair '" + after + "': " + changed + " changed pixels.");
                Assert.That(changed, Is.GreaterThan(24), after + " must visibly affect the real room.");
            }
            finally { Object.DestroyImmediate(baseline); Object.DestroyImmediate(actual); }
        }
    }
}
