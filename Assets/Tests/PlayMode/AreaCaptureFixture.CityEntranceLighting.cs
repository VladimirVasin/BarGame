using System;
using System.Collections;
using System.Collections.Generic;
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
        private const string EntranceLampCaptureArea = "CityEntranceLighting";
        private const int EntranceLampWidth = 1920, EntranceLampHeight = 1080;

        [UnityTest]
        [Timeout(180000)]
        [Explicit("All residential doorways: visible measured fixtures, permanent real spill, day floor and exterior cleanup.")]
        public IEnumerator CityEntranceLighting()
        {
            string[] keys = { "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3", "graphics.clean_volume" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            CityGameRoot city = null;
            GameTimeRuntime clock = null;
            PlayerCameraFollow follow = null;
            bool clockEnabled = false, followEnabled = false;
            float previousCaptureDelta = Time.captureDeltaTime;
            var hidden = new List<Renderer>();
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test entrance lamps");
                Assert.That(GameSessionState.TryStartGameTimeAt(21 * 60), Is.True);
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                GraphicsEffectsSettings.CleanVolumeEnabled = true;
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (city != null && city.IsInitialized) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True);
                city.DayNight.enabled = false;
                city.Weather.enabled = false;
                city.DayNight.ApplyCurrentTime(true);
                clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                Assert.That(clock, Is.Not.Null);
                clockEnabled = clock.enabled; clock.enabled = false;
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                followEnabled = follow.enabled; follow.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { renderer.enabled = false; hidden.Add(renderer); }

                CityResidentialEntrancePlan plan = CityResidentialEntrancePlan.ForLayout(city.Layout);
                Assert.That(plan.Entrances, Is.Not.Empty);
                var sources = new List<Transform>();
                var lights = new List<Light>();
                var claimed = new HashSet<Transform>();
                CityResidentialEntranceDescriptor capture = null, highest = null;
                int fresh = 0, reused = 0;
                foreach (CityResidentialEntranceDescriptor entrance in plan.Entrances)
                {
                    Transform source = FindEntranceLampSource(city.Night.Atmosphere, entrance, out Light light);
                    Assert.That(claimed.Add(source), Is.True, "Each doorway owns its own fixture: " + entrance.Lot.Cell);
                    Assert.That(light.type, Is.EqualTo(LightType.Point));
                    Assert.That(light.isActiveAndEnabled && light.intensity > 0f, Is.True);
                    Assert.That(light.range, Is.LessThanOrEqualTo(5.5f));
                    Assert.That(light.intensity, Is.EqualTo(7.5f).Within(.001f),
                        "New fixtures and reused canopy lamps must share the accepted doorway profile.");
                    Assert.That(light.shadows, Is.EqualTo(LightShadows.Soft));
                    Assert.That(light.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier,
                        Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow));
                    Assert.That(light.color, Is.EqualTo(CityNightAtmosphere.GetIndustrialTaskLightColor(CityIndustrialPurpose.CompressorHouse)));
                    Transform fixture = FindEntranceLampVisual(source, out Renderer lens, out Bounds measured);
                    Assert.That(lens.sharedMaterial, Is.SameAs(CityNightResources.EmissiveMaterial));
                    Assert.That(lens.enabled, Is.True);
                    Assert.That(Vector3.Distance(lens.bounds.ClosestPoint(source.position), source.position), Is.LessThan(.15f),
                        "The actual light must leave the visible diffuser, in world metres.");
                    Assert.That(source.position.y - entrance.Threshold.y, Is.InRange(2.4f, 3.1f));
                    if (source.name == CityResidentialEntranceLighting.LightAnchorName)
                    {
                        fresh++;
                        Assert.That(Mathf.Max(measured.size.x, measured.size.z), Is.InRange(.1f, 1.05f));
                        Assert.That(measured.size.y, Is.InRange(.05f, .6f));
                        Assert.That(Mathf.Min(measured.size.x, measured.size.z), Is.GreaterThanOrEqualTo(.05f));
                        Assert.That(fixture.GetComponentsInChildren<Collider>(true), Is.Empty,
                            "The lamp must add no obstruction to the doorway or its paving.");
                        if (capture == null) capture = entrance;
                    }
                    else reused++;
                    if (highest == null || entrance.Threshold.y > highest.Threshold.y) highest = entrance;
                    sources.Add(source); lights.Add(light);
                }
                Assert.That(fresh, Is.GreaterThan(0));
                Assert.That(fresh + reused, Is.EqualTo(plan.Entrances.Count));
                Assert.That(capture, Is.Not.Null);
                Debug.Log("CityEntranceLighting coverage: entries=" + plan.Entrances.Count + ", new=" + fresh + ", existing canopies=" + reused);

                Vector3 tangent = Vector3.Cross(Vector3.up, capture.Outward);
                Vector3 eye = capture.Threshold + capture.Outward * 5.4f + tangent * 1.1f + Vector3.up * 1.7f;
                Vector3 target = capture.Threshold + Vector3.up * 1.45f;
                Vector3 door = capture.Threshold - capture.Outward * .65f + Vector3.up * 1.05f;
                Vector3 paving = capture.Threshold + capture.Outward * .75f + Vector3.up * .035f;
                Vector3 facadeLeft = capture.Threshold + tangent * 1.1f + Vector3.up * 1.65f;
                Vector3 facadeRight = capture.Threshold - tangent * 1.1f + Vector3.up * 1.65f;
                Vector3 balcony = FindEntranceBalconyUnderside(city, capture);
                city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
                SetEntranceLampCamera(city, eye, target);
                for (int frame = 0; frame < 3; frame++) yield return null;
                SetEntranceLampCamera(city, eye, target);
                city.Lighting.PrepareForCamera(city.Camera);
                Assert.That(city.Lighting.VolumeSourceCount, Is.LessThanOrEqualTo(CityLightingPresentation.MaximumVolumeSources));
                int doorwayAirSources = 0;
                for (int i = 0; i < city.Lighting.VolumeSourceCount; i++)
                    if (city.Lighting.TryGetVolumeSource(i, out var air) && lights.Contains(air.Light)) doorwayAirSources++;
                Assert.That(doorwayAirSources, Is.InRange(1, CityLightingPresentation.MaximumVolumeSources / 2),
                    "The nearby doorway must illuminate bounded air through the shared provider.");
                CaptureEntranceLampPair(city.Camera, lights, "01-night", door, paving, facadeLeft, facadeRight, balcony);
                var positions = new Vector3[lights.Count];
                var powers = new float[lights.Count];
                for (int i = 0; i < lights.Count; i++)
                { positions[i] = lights[i].transform.position; powers[i] = lights[i].intensity; }
                city.Player.Motor.Teleport(eye + Vector3.right * 100f);
                city.Night.Atmosphere.RefreshImmediate();
                for (int i = 0; i < lights.Count; i++)
                {
                    Assert.That(lights[i].transform.position, Is.EqualTo(positions[i]));
                    Assert.That(lights[i].intensity, Is.EqualTo(powers[i]).Within(.0001f));
                    Assert.That(lights[i].enabled, Is.True, "A distant entrance must retain its light.");
                    Assert.That(Vector3.Distance(sources[i].position, lights[i].transform.position), Is.LessThan(.001f));
                }
                CaptureCurrentCamera(city.Camera, EntranceLampCaptureArea, "02-night-hero-far", EntranceLampWidth, EntranceLampHeight);
                city.Player.Motor.Teleport(highest.Threshold + highest.Outward * 4.2f);
                SetEntranceLampCamera(city, highest.Threshold + highest.Outward * 4.2f + Vector3.up * EyeHeight,
                    highest.Threshold + Vector3.up * 1.55f);
                CaptureCurrentCamera(city.Camera, EntranceLampCaptureArea, "03-highest-entrance-night", EntranceLampWidth, EntranceLampHeight);

                double toNoon = (12 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(toNoon / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();
                for (int i = 0; i < lights.Count; i++)
                    Assert.That(lights[i].intensity, Is.EqualTo(powers[i] * GameTimeDayNightRules.DayFixtureFloor).Within(.001f));
                city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
                SetEntranceLampCamera(city, eye, target);
                CaptureEntranceLampPair(city.Camera, lights, "04-noon", door, paving, facadeLeft, facadeRight, balcony);

                Assert.That(city.EnterDormant(), Is.True);
                foreach (Light light in lights) Assert.That(light.enabled, Is.False);
                city.ResumeFromDormant();
                city.DayNight.enabled = false; city.Weather.enabled = false; follow.enabled = false;
                city.Night.Atmosphere.RefreshImmediate();
                foreach (Light light in lights) Assert.That(light.enabled, Is.True);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                foreach (Light light in lights) Assert.That(light == null, Is.True, "Entrance light lifetime belongs to its exterior.");
                Debug.Log("CityEntranceLighting: visible door fixtures, native receiver spill and fixed-source cleanup verified; frame cost unmeasured.");
            }
            finally
            {
                if (city != null)
                {
                    if (city.IsDormant) city.ResumeFromDormant();
                    city.DayNight.enabled = true; city.Weather.enabled = true;
                    city.Player.Motor.SetInputEnabled(true);
                }
                if (clock != null) clock.enabled = clockEnabled;
                if (follow != null) follow.enabled = followEnabled;
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test entrance lamps cleanup");
            }
        }

        private static Transform FindEntranceLampSource(CityNightAtmosphere atmosphere,
            CityResidentialEntranceDescriptor entrance, out Light light)
        {
            Transform found = null; light = null;
            Vector3 expected = entrance.Threshold + Vector3.up * CityResidentialEntranceLighting.MountHeight;
            for (int i = 0; i < atmosphere.LampAnchors.Count; i++)
            {
                Transform source = atmosphere.LampAnchors[i];
                if (source == null || source.name != CityResidentialEntranceLighting.LightAnchorName &&
                    source.name != CityResidentialCourtyardAssetProvider.EntranceLightAnchorName) continue;
                Vector3 delta = source.position - expected;
                if (Mathf.Abs(delta.y) > .8f) continue;
                delta.y = 0f;
                if (delta.sqrMagnitude >= .8f * .8f) continue;
                Assert.That(found, Is.Null, "Do not duplicate an existing canopy lamp at " + entrance.Lot.Cell);
                found = source; light = atmosphere.GetLightForAnchor(i);
            }
            Assert.That(found, Is.Not.Null, "A visible actual fixture must cover entrance " + entrance.Lot.Cell);
            Assert.That(light, Is.Not.Null);
            return found;
        }

        private static Transform FindEntranceLampVisual(Transform source, out Renderer lens, out Bounds bounds)
        {
            for (Transform parent = source.parent; parent != null; parent = parent.parent)
            {
                Renderer[] renderers = parent.GetComponentsInChildren<Renderer>(true);
                lens = null;
                bool housing = false;
                bounds = default;
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (i == 0) bounds = renderer.bounds; else bounds.Encapsulate(renderer.bounds);
                    if (renderer.sharedMaterial == CityNightResources.EmissiveMaterial) lens = renderer;
                    else housing = true;
                }
                if (lens != null && housing) return parent;
            }
            throw new AssertionException("A doorway source must have both an authored lens and housing.");
        }

        private static void SetEntranceLampCamera(CityGameRoot city, Vector3 position, Vector3 target)
        {
            city.Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            city.Camera.fieldOfView = 63f;
            city.Camera.aspect = (float)EntranceLampWidth / EntranceLampHeight;
            city.Night.Atmosphere.RefreshImmediate();
            city.Visibility.Refresh(true);
            Physics.SyncTransforms();
        }

        private static Vector3 FindEntranceBalconyUnderside(CityGameRoot city,
            CityResidentialEntranceDescriptor entrance)
        {
            foreach (CityBuildingAssetRegistry building in city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>())
            {
                if (!building.HasEntrance || Vector3.Distance(building.transform.TransformPoint(building.LocalEntranceThreshold),
                    entrance.Threshold) > .05f) continue;
                float nearest = float.PositiveInfinity;
                Vector3 receiver = default;
                foreach (CityBuildingBalconySlot slot in building.BalconySlots)
                {
                    if (slot.Side != "Front" || slot.Floor != 1) continue;
                    Bounds deck = slot.LocalDeckBounds;
                    Vector3 point = building.transform.TransformPoint(new Vector3(deck.center.x, deck.min.y, deck.center.z));
                    float distance = (point - entrance.Threshold).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance; receiver = point;
                }
                Assert.That(nearest, Is.LessThan(float.PositiveInfinity), "The actual entrance building needs a measured balcony receiver.");
                return receiver;
            }
            throw new AssertionException("The entrance must match an actual Blender building.");
        }

        private static void CaptureEntranceLampPair(Camera camera, IReadOnlyList<Light> lights, string name,
            Vector3 door, Vector3 paving, Vector3 facadeLeft, Vector3 facadeRight, Vector3 balcony)
        {
            var powers = new float[lights.Count];
            for (int i = 0; i < lights.Count; i++) { powers[i] = lights[i].intensity; lights[i].intensity = 0f; }
            try { CaptureCurrentCamera(camera, EntranceLampCaptureArea, name + "-off", EntranceLampWidth, EntranceLampHeight); }
            finally { for (int i = 0; i < lights.Count; i++) lights[i].intensity = powers[i]; }
            CaptureCurrentCamera(camera, EntranceLampCaptureArea, name + "-on", EntranceLampWidth, EntranceLampHeight);
            AssertEntranceLampReceiver(camera, name, "door", door);
            AssertEntranceLampReceiver(camera, name, "paving", paving);
            AssertEntranceLampReceiver(camera, name, "left facade", facadeLeft);
            AssertEntranceLampReceiver(camera, name, "right facade", facadeRight);
            AssertEntranceLampReceiver(camera, name, "balcony underside", balcony);
        }

        private static void AssertEntranceLampReceiver(Camera camera, string name, string receiver, Vector3 position)
        {
            Vector3 viewport = camera.WorldToViewportPoint(position);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(viewport.x, Is.InRange(.05f, .95f));
            Assert.That(viewport.y, Is.InRange(.05f, .95f));
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", EntranceLampCaptureArea);
            var a = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var b = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(a.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-off.png"))), Is.True);
                Assert.That(b.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-on.png"))), Is.True);
                const int radius = 12;
                int x = Mathf.RoundToInt(viewport.x * a.width), y = Mathf.RoundToInt(viewport.y * a.height);
                Color[] before = a.GetPixels(x - radius, y - radius, radius * 2, radius * 2);
                Color[] after = b.GetPixels(x - radius, y - radius, radius * 2, radius * 2);
                int brighter = 0;
                for (int i = 0; i < before.Length; i++)
                    if (after[i].r + after[i].g + after[i].b - before[i].r - before[i].g - before[i].b >= 3f / 255f) brighter++;
                Debug.Log("CityEntranceLighting " + name + " " + receiver + ": " + brighter + " brighter pixels.");
                Assert.That(brighter, Is.GreaterThan(before.Length / 10),
                    "The real doorway lamp must visibly illuminate a surface patch of its " + receiver + ".");
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
