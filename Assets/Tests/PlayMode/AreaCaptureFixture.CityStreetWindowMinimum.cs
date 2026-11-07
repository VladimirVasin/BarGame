using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private const string StreetWindowArea = "CityStreetWindowMinimum";

        [UnityTest, Timeout(180000)]
        [Explicit("Every ordinary street front retains luminous glass and warms the wall around its opening, including the reported west crest house.")]
        public IEnumerator CityStreetWindowMinimum()
        {
            var preferences = new Dictionary<string, int?>();
            foreach (string key in new[] { "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" })
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            CityGameRoot city = null;
            GameTimeRuntime clock = null;
            PlayerCameraFollow follow = null;
            bool clockEnabled = false, followEnabled = false;
            float captureDelta = Time.captureDeltaTime;
            var hidden = new Dictionary<Renderer, bool>();
            try
            {
                GameSessionState.BeginNewGame("Test street window minimum");
                Assert.That(GameSessionState.TryStartGameTimeAt(21 * 60), Is.True);
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                Time.captureDeltaTime = 1f / 30f;
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (city != null && city.IsInitialized) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True);
                city.DayNight.enabled = false; city.Weather.enabled = false;
                clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                Assert.That(clock, Is.Not.Null); clockEnabled = clock.enabled; clock.enabled = false;
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                followEnabled = follow.enabled; follow.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true)) Hide(renderer);
                foreach (var vehicle in city.BusVehicles)
                    foreach (Renderer renderer in vehicle.GetComponentsInChildren<Renderer>(true)) Hide(renderer);
                city.DayNight.ApplyCurrentTime(true);
                var windows = city.Lighting.WindowLights;
                windows.RefreshImmediate();
                CityBuildingAssetRegistry[] buildings = city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>(true)
                    .Where(item => item.WindowStateBuffer != null).ToArray();
                Assert.That(buildings.Length, Is.GreaterThan(20));
                AssertResidentialWindowInventory(city, buildings);
                var minimums = buildings.Select(item => item.GuaranteedWindowMask).ToArray();
                int sourceCount = windows.Lights.Count;
                Light[] facadeSources = windows.FacadeLights.ToArray();
                Vector3[] facadePositions = facadeSources.Select(light => light.transform.position).ToArray();
                foreach (int hour in new[] { 23, 1, 5 })
                {
                    double minutes = (hour * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                    GameSessionState.AdvanceGameTime((float)(minutes / GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true); windows.RefreshImmediate();
                    AssertWindowSourceModels(buildings, windows);
                    AssertWindowFacadeSources(windows, facadeSources, facadePositions);
                    for (int i = 0; i < buildings.Length; i++)
                    {
                        Assert.That(buildings[i].GuaranteedWindowMask, Is.EqualTo(minimums[i]));
                        Assert.That(StreetMinimumPane(buildings[i]).Side, Is.EqualTo("Front"),
                            "The public facade must not depend on the optional timed pane: " + buildings[i].StableId);
                    }
                    Assert.That(windows.Lights.Count, Is.EqualTo(sourceCount), "Relocating the minimum must not add sources.");
                }
                AssertOptionalWindowFacadeTransitions(city, buildings, facadeSources, facadePositions);
                CityHillsideBuilding lot = CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings
                    .Single(item => item.Lot.Cell == new Vector2Int(-25, -8));
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot.Lot);
                CityBuildingAssetRegistry reported = buildings.Single(item => Vector3.Distance(item.transform.position, pose.Position) < .01f);
                Assert.That(StreetMinimumPane(reported).Uv2SlotId, Is.EqualTo(6));
                Vector3 center = reported.transform.TransformPoint(StreetMinimumPane(reported).LocalCenter);
                city.Player.Motor.Teleport(new Vector3(-85.5f, 39.2f, -348.5f));
                SetWindowLightCamera(city, city.Player.Motor.transform.position + Vector3.up * EyeHeight, center);
                city.Visibility.Refresh(true);
                CaptureCurrentCamera(city.Camera, StreetWindowArea, "01-reported-street-night", 1920, 1080);
                AssertStreetGlassPixels(city, reported, "02-reported-pane");
                AssertStreetWindowWallPixels(city, reported, "04-reported-wall");
                foreach (IGrouping<string, CityBuildingAssetRegistry> group in buildings.Where(item => item.District == CityDistrictKind.Residential)
                    .GroupBy(item => item.StableId))
                {
                    CityBuildingAssetRegistry model = SelectClearStreetWindowReceiver(city, group);
                    AssertStreetGlassPixels(city, model, "variant-" + model.StableId);
                    AssertStreetWindowWallPixels(city, model, "wall-" + model.StableId);
                }
                city.Player.Motor.Teleport(new Vector3(-85.5f, 39.2f, -348.5f));
                SetWindowLightCamera(city, city.Player.Motor.transform.position + Vector3.up * EyeHeight, center);
                city.Visibility.Refresh(true);
                CaptureCurrentCamera(city.Camera, StreetWindowArea, "05-reported-upper-balcony-street", 1920, 1080);
                Vector3 broad = reported.transform.TransformPoint(new Vector3(0f, 4.5f, 18f));
                SetWindowLightCamera(city, broad, reported.transform.TransformPoint(new Vector3(0f, 4.5f, 0f)));
                CaptureCurrentCamera(city.Camera, StreetWindowArea, "03-reported-whole-facade", 1920, 1080);
                Debug.Log("CityStreetWindowMinimum: every actual house retains a fixed front pane throughout the night; reported and residential-variant glass and wall pixels, timed facade-source synchronization and fixed source identities verified.");
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                foreach (Light light in facadeSources)
                    Assert.That(light == null, Is.True, "Window facade sources must be destroyed with their City owner.");

                void Hide(Renderer renderer)
                { hidden[renderer] = renderer.forceRenderingOff; renderer.forceRenderingOff = true; }
            }
            finally
            {
                if (city != null) { city.DayNight.enabled = true; city.Weather.enabled = true; city.Player.Motor.SetInputEnabled(true); }
                if (clock != null) clock.enabled = clockEnabled;
                if (follow != null) follow.enabled = followEnabled;
                foreach (var entry in hidden) if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
                foreach (var preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save(); GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = captureDelta;
                GameSessionState.BeginNewGame("Test street window minimum cleanup");
            }
        }

        private static CityBuildingWindowSlot StreetMinimumPane(CityBuildingAssetRegistry building) =>
            building.WindowSlots.Single(pane => (building.GuaranteedWindowMask & (1UL << pane.Uv2SlotId)) != 0);

        private static CityBuildingAssetRegistry SelectClearStreetWindowReceiver(CityGameRoot city,
            IGrouping<string, CityBuildingAssetRegistry> models)
        {
            Physics.SyncTransforms();
            foreach (CityBuildingAssetRegistry model in models.OrderByDescending(item => item.transform.position.y)
                .ThenBy(item => item.transform.position.x).ThenBy(item => item.transform.position.z))
            {
                CityBuildingWindowSlot pane = StreetMinimumPane(model);
                Vector3 center = model.transform.TransformPoint(pane.LocalCenter);
                Vector3 eye = center + model.transform.forward * 3.5f;
                bool clear = true;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 receiver = StreetWindowWallReceiver(model, pane, side);
                    // A lowland arch or pier can stand between this artificial
                    // close-up and the opening. Require the whole sampled wall
                    // strip to be visible before choosing the variant receiver.
                    foreach (Vector2 offset in new[] { Vector2.zero, new Vector2(-.04f, -.175f), new Vector2(.04f, -.175f),
                        new Vector2(-.04f, .175f), new Vector2(.04f, .175f) })
                    {
                        Vector3 target = receiver + model.transform.right * offset.x + Vector3.up * offset.y;
                        Vector3 ray = target - eye;
                        foreach (RaycastHit hit in Physics.RaycastAll(eye, ray.normalized, ray.magnitude - .05f,
                            ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (hit.collider.transform.IsChildOf(city.Player.GameObject.transform)) continue;
                            Debug.Log("CityStreetWindowMinimum rejecting occluded receiver " + model.StableId + " at=" + model.transform.position +
                                ", pane=" + pane.Uv2SlotId + ", side=" + side + ", blocker=" + hit.collider.name + ", hit=" + hit.point);
                            clear = false;
                            break;
                        }
                        if (!clear) break;
                    }
                    if (!clear) break;
                }
                if (!clear) continue;
                Debug.Log("CityStreetWindowMinimum clear receiver " + model.StableId + " at=" + model.transform.position +
                    ", pane=" + pane.Uv2SlotId + ", center=" + center + ", eye=" + eye);
                return model;
            }
            Assert.Fail("No unobstructed real facade receiver exists for residential variant " + models.Key);
            return null;
        }

        private static Vector3 StreetWindowWallReceiver(CityBuildingAssetRegistry model, CityBuildingWindowSlot pane, int side) =>
            model.transform.TransformPoint(pane.LocalCenter) + model.transform.right * (side * (pane.SizeMeters.x * .5f + .10f));

        private static void AssertStreetGlassPixels(CityGameRoot city, CityBuildingAssetRegistry model, string name)
        {
            CityBuildingWindowSlot pane = StreetMinimumPane(model);
            Vector3 center = model.transform.TransformPoint(pane.LocalCenter);
            Vector3 outward = model.transform.forward;
            Vector3 eye = center + outward * 3.5f;
            city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
            city.DayNight.ApplyCurrentTime(true);
            SetWindowLightCamera(city, eye, center);
            city.Visibility.Refresh(true);
            city.Camera.fieldOfView = 55f;
            model.TryGetRenderer(CityBuildingMeshRole.WindowGlass, out Renderer glass);
            var properties = new MaterialPropertyBlock(); glass.GetPropertyBlock(properties);
            float[] original = properties.GetFloatArray("_CityBuildingWindowStates");
            float[] dark = (float[])original.Clone();
            dark[pane.Uv2SlotId] = (int)dark[pane.Uv2SlotId] % CityWindowAppearance.VariantCount;
            var windows = city.Lighting.WindowLights;
            bool[] enabled = windows.Lights.Select(light => light.enabled).ToArray();
            bool[] facadeEnabled = windows.FacadeLights.Select(light => light.enabled).ToArray();
            try
            {
                foreach (Light light in windows.Lights) light.enabled = false;
                foreach (Light light in windows.FacadeLights) light.enabled = false;
                properties.SetFloatArray("_CityBuildingWindowStates", dark); glass.SetPropertyBlock(properties);
                CaptureCurrentCamera(city.Camera, StreetWindowArea, name + "-off", 1920, 1080);
                properties.SetFloatArray("_CityBuildingWindowStates", original); glass.SetPropertyBlock(properties);
                CaptureCurrentCamera(city.Camera, StreetWindowArea, name + "-on", 1920, 1080);
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", StreetWindowArea);
                var before = new Texture2D(2, 2, TextureFormat.RGB24, false);
                var after = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    before.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-off.png")));
                    after.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-on.png")));
                    Vector3 a = city.Camera.WorldToViewportPoint(center - model.transform.right * pane.SizeMeters.x * .3f - Vector3.up * pane.SizeMeters.y * .3f);
                    Vector3 b = city.Camera.WorldToViewportPoint(center + model.transform.right * pane.SizeMeters.x * .3f + Vector3.up * pane.SizeMeters.y * .3f);
                    int left = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) * before.width), 0, before.width - 1);
                    int bottom = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) * before.height), 0, before.height - 1);
                    int width = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(a.x - b.x) * before.width), 1, before.width - left);
                    int height = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(a.y - b.y) * before.height), 1, before.height - bottom);
                    Color[] off = before.GetPixels(left, bottom, width, height), on = after.GetPixels(left, bottom, width, height);
                    int warm = 0;
                    for (int i = 0; i < off.Length; i++)
                        if (on[i].r > off[i].r + .012f && on[i].r > on[i].b * 1.08f) warm++;
                    Assert.That(warm, Is.GreaterThan(off.Length / 20),
                        "The guaranteed street pane must visibly emit warm light, not merely own a mask: " + model.StableId);
                }
                finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
            }
            finally
            {
                properties.SetFloatArray("_CityBuildingWindowStates", original); glass.SetPropertyBlock(properties);
                for (int i = 0; i < enabled.Length; i++) windows.Lights[i].enabled = enabled[i];
                for (int i = 0; i < facadeEnabled.Length; i++) windows.FacadeLights[i].enabled = facadeEnabled[i];
            }
        }

        private static void AssertWindowFacadeSources(CityWindowLighting windows, Light[] expected, Vector3[] positions)
        {
            Assert.That(windows.FacadeLights.Count, Is.EqualTo(windows.Lights.Count),
                "Each existing pane source must own exactly one bounded facade receiver light.");
            Assert.That(windows.FacadeLights.Count, Is.LessThanOrEqualTo(BarPromenade.CityWindowLighting.MaximumLights));
            for (int i = 0; i < expected.Length; i++)
            {
                Light facade = windows.FacadeLights[i], outward = windows.Lights[i];
                Assert.That(facade, Is.SameAs(expected[i]));
                Assert.That(facade.transform.position, Is.EqualTo(positions[i]));
                Assert.That(facade.type, Is.EqualTo(LightType.Spot));
                Assert.That(facade.range, Is.InRange(.1f, 1.8f));
                Assert.That(facade.shadows, Is.EqualTo(LightShadows.None));
                Assert.That(facade.bounceIntensity, Is.Zero);
                Assert.That(facade.enabled, Is.EqualTo(outward.enabled), "Facade illumination must follow its actual pane on every facade.");
                Assert.That(facade.intensity, Is.GreaterThan(0f));
                Assert.That(Vector3.Dot(facade.transform.forward, outward.transform.forward), Is.LessThan(-.99f),
                    "The compact facade receiver source must point back toward its own opening.");
            }
        }

        private static void AssertOptionalWindowFacadeTransitions(CityGameRoot city, CityBuildingAssetRegistry[] buildings,
            Light[] expected, Vector3[] positions)
        {
            CityBuildingAssetRegistry timed = buildings.First(item => item.OptionalWindowMask != 0);
            double nextNight = Math.Ceiling(CityBuildingWindowSlotAppearance.AbsoluteMinutes / 1440d) * 1440d;
            CityBuildingWindowSlotAppearance.GetOptionalInterval(nextNight, timed.WindowScheduleSeed, out double start, out double end);
            foreach (double minute in new[] { (start + end) * .5d, end + 1d })
            {
                double delta = minute - CityBuildingWindowSlotAppearance.AbsoluteMinutes;
                Assert.That(delta, Is.GreaterThan(0d));
                GameSessionState.AdvanceGameTime((float)(delta / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true); city.Lighting.WindowLights.RefreshImmediate();
                AssertWindowSourceModels(buildings, city.Lighting.WindowLights);
                AssertWindowFacadeSources(city.Lighting.WindowLights, expected, positions);
                Assert.That((timed.LitWindowMask & timed.OptionalWindowMask) != 0, Is.EqualTo(minute < end));
            }
        }

        private static void AssertStreetWindowWallPixels(CityGameRoot city, CityBuildingAssetRegistry model, string name)
        {
            CityBuildingWindowSlot pane = StreetMinimumPane(model);
            Vector3 center = model.transform.TransformPoint(pane.LocalCenter);
            Vector3 right = model.transform.right, outward = model.transform.forward;
            Vector3 eye = center + outward * 3.5f;
            city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
            city.DayNight.ApplyCurrentTime(true);
            SetWindowLightCamera(city, eye, center);
            city.Visibility.Refresh(true);
            city.Camera.fieldOfView = 55f;
            var windows = city.Lighting.WindowLights;
            bool[] enabled = windows.FacadeLights.Select(light => light.enabled).ToArray();
            try
            {
                // Keep luminous glass and the outward aperture light identical in
                // both frames. Only illumination of the wall receivers changes.
                foreach (Light light in windows.FacadeLights) light.enabled = false;
                CaptureCurrentCamera(city.Camera, StreetWindowArea, name + "-off", 1920, 1080);
                for (int i = 0; i < enabled.Length; i++) windows.FacadeLights[i].enabled = enabled[i];
                CaptureCurrentCamera(city.Camera, StreetWindowArea, name + "-on", 1920, 1080);
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", StreetWindowArea);
                var before = new Texture2D(2, 2, TextureFormat.RGB24, false);
                var after = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    Assert.That(before.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-off.png"))), Is.True);
                    Assert.That(after.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-on.png"))), Is.True);
                    foreach (int side in new[] { -1, 1 })
                    {
                        // This measured strip lies outside the authored pane
                        // and frame, on the real facade rather than the atlas.
                        Vector3 receiver = StreetWindowWallReceiver(model, pane, side);
                        Vector3 a = city.Camera.WorldToViewportPoint(receiver - right * .04f - Vector3.up * .175f);
                        Vector3 b = city.Camera.WorldToViewportPoint(receiver + right * .04f + Vector3.up * .175f);
                        Assert.That(a.z > 0f && b.z > 0f && a.x > 0f && b.x < 1f && a.y > 0f && b.y < 1f, Is.True);
                        int left = Mathf.FloorToInt(Mathf.Min(a.x, b.x) * before.width);
                        int bottom = Mathf.FloorToInt(Mathf.Min(a.y, b.y) * before.height);
                        int width = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(a.x - b.x) * before.width));
                        int height = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(a.y - b.y) * before.height));
                        Color[] off = before.GetPixels(left, bottom, width, height), on = after.GetPixels(left, bottom, width, height);
                        int warm = 0;
                        for (int i = 0; i < off.Length; i++)
                        {
                            float red = on[i].r - off[i].r, blue = on[i].b - off[i].b;
                            if (red > 2f / 255f && red > blue * 1.05f) warm++;
                        }
                        Debug.Log("CityStreetWindowMinimum wall " + model.StableId + " at=" + model.transform.position + ", pane=" + pane.Uv2SlotId +
                            ", center=" + center + ", receiver=" + receiver + ", side=" + side + ": warm=" + warm + "/" + off.Length +
                            ", sample=" + left + "," + bottom + "/" + width + "x" + height +
                            ", sources=" + windows.Lights.Count + ", facade=" + windows.FacadeLights.Count);
                        Assert.That(warm, Is.GreaterThan(off.Length / 20),
                            "The real facade beside a glowing window must receive warm light: " + model.StableId + ", side=" + side);
                    }
                }
                finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
            }
            finally
            {
                for (int i = 0; i < enabled.Length; i++) windows.FacadeLights[i].enabled = enabled[i];
            }
        }
    }
}
