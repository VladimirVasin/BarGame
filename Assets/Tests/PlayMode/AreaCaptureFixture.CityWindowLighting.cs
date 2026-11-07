using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using WindowLighting = BarPromenade.CityWindowLighting;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private const string WindowLightCaptureArea = "CityWindowLighting";
        private const int WindowLightCaptureWidth = 1920, WindowLightCaptureHeight = 1080;

        [UnityTest]
        [Timeout(180000)]
        [Explicit("Persistent City fixtures, every home retains a lit window, seeded occupancy intervals, Ridge spill and cleanup.")]
        public IEnumerator CityLightingContinuity()
        {
            string[] keys = { "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            CityGameRoot city = null;
            GameTimeRuntime clock = null;
            PlayerCameraFollow follow = null;
            bool clockWasEnabled = false, followWasEnabled = false;
            var hiddenPlayer = new List<Renderer>();
            float previousCaptureDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test city window light");
                Assert.That(GameSessionState.TryStartGameTimeAt(21 * 60), Is.True);
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (city != null && city.IsInitialized) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True, "City must finish its actual world construction.");
                city.DayNight.enabled = false;
                city.Weather.enabled = false;
                city.DayNight.ApplyCurrentTime(true);
                clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                Assert.That(clock, Is.Not.Null);
                clockWasEnabled = clock.enabled;
                clock.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                followWasEnabled = follow.enabled;
                follow.enabled = false;
                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { renderer.enabled = false; hiddenPlayer.Add(renderer); }

                WindowLighting windows = city.Lighting.WindowLights;
                Assert.That(windows, Is.Not.Null);
                Assert.That(windows.CandidateCount, Is.GreaterThan(4));
                Assert.That(windows.Lights.Count, Is.EqualTo(windows.CandidateCount));
                var owned = new List<Light>(windows.Lights);
                var street = new List<Light>(city.Night.Atmosphere.StreetLightPool);
                int ordinaryLightCount = city.Night.Atmosphere.RealtimeLightCount;
                // Source construction precedes Root.IsInitialized. This
                // coroutine can resume before the first eligible LateUpdate;
                // synchronize the public owner before inspecting its lights.
                windows.RefreshImmediate();
                AssertPermanentWindowSources(city, windows);
                AssertPermanentStreetSources(city.Night.Atmosphere);
                FindWindowLightReceiver(city, out CityBuildingAssetRegistry building,
                    out CityBuildingWindowSlot pane, out Vector3 window, out Vector3 outward);
                Vector3 tangent = Vector3.Cross(Vector3.up, outward);
                Vector3 wall = window - Vector3.up * (pane.SizeMeters.y * .5f - .025f) + outward * .025f;
                Vector3 ground = new Vector3(window.x, building.transform.position.y + .04f, window.z) + outward;
                Vector3 position = window + outward * 6f + tangent * 1.6f + Vector3.up * .75f;
                Vector3 target = Vector3.Lerp(window, ground, .35f);
                city.Player.Motor.Teleport(position - Vector3.up * EyeHeight);
                SetWindowLightCamera(city, position, target);
                windows.RefreshImmediate();
                AssertWindowLightSource(windows, window);
                for (int frame = 0; frame < 3; frame++) yield return null;
                SetWindowLightCamera(city, position, target);
                windows.RefreshImmediate();
                AssertWindowLightBudget(windows);

                // Both renders happen in one frame: rain, NPCs, shader time,
                // fixture flicker and the emissive panes are identical.
                CaptureWindowLightPair(city.Camera, windows, "01-night", wall, ground);
                AssertHeroIndependentCityLighting(city, windows, owned, street, position, target, wall, ground);
                CapturePersistentCityViews(city, windows, position, target);
                AssertNightWindowOccupancy(city, windows);
                float[] nightPowers = new float[owned.Count];
                for (int index = 0; index < nightPowers.Length; index++) nightPowers[index] = owned[index].intensity;
                double untilNoon = (12 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(untilNoon / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                SetWindowLightCamera(city, position, target);
                windows.RefreshImmediate();
                for (int index = 0; index < owned.Count; index++)
                    if (nightPowers[index] > .001f)
                        Assert.That(owned[index].intensity, Is.EqualTo(nightPowers[index] * GameTimeDayNightRules.DayFixtureFloor)
                            .Within(.001f), "The same window retains the established daytime fixture floor.");
                CaptureWindowLightPair(city.Camera, windows, "02-noon", wall, ground);

                Vector3 secondPosition = window + outward * 5f - tangent * 6f + Vector3.up;
                SetWindowLightCamera(city, secondPosition, window - Vector3.up * .4f);
                windows.RefreshImmediate();
                Assert.That(windows.Lights, Is.EqualTo(owned), "Every window keeps its own permanent Light.");
                AssertWindowLightBudget(windows);
                CaptureCurrentCamera(city.Camera, WindowLightCaptureArea, "03-oblique",
                    WindowLightCaptureWidth, WindowLightCaptureHeight);
                Assert.That(city.Night.Atmosphere.StreetLightPool, Is.EqualTo(street));
                Assert.That(city.Night.Atmosphere.RealtimeLightCount, Is.EqualTo(ordinaryLightCount),
                    "Windows and street fixtures keep separate permanent ownership.");

                var exclusion = city.Camera.gameObject.AddComponent<ScenePresentationExclusion>();
                windows.RefreshImmediate();
                AssertWindowLightsOff(windows);
                Object.DestroyImmediate(exclusion);
                windows.RefreshImmediate();
                AssertPermanentWindowSources(city, windows);
                Assert.That(city.EnterDormant(), Is.True);
                AssertWindowLightsOff(windows);
                city.ResumeFromDormant();
                city.DayNight.enabled = false;
                city.Weather.enabled = false;
                city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
                SetWindowLightCamera(city, position, target);
                windows.RefreshImmediate();
                AssertPermanentWindowSources(city, windows);
                Assert.That(windows.Lights, Is.EqualTo(owned));
                Debug.Log("CityLightingContinuity: permanent source identity and real spill verified. GPU/frame-cost comparison unavailable in this render acceptance.");
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                Assert.That(windows == null, Is.True, "The exterior owns and unloads its permanent window lights.");
                foreach (Light light in owned) Assert.That(light == null, Is.True);
                foreach (Light light in street) Assert.That(light == null, Is.True);
            }
            finally
            {
                if (city != null)
                {
                    if (city.IsDormant) city.ResumeFromDormant();
                    city.DayNight.enabled = true;
                    city.Weather.enabled = true;
                    city.Player.Motor.SetInputEnabled(true);
                }
                if (clock != null) clock.enabled = clockWasEnabled;
                if (follow != null) follow.enabled = followWasEnabled;
                foreach (Renderer renderer in hiddenPlayer) if (renderer != null) renderer.enabled = true;
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test city window light cleanup");
            }
        }

        private static void FindWindowLightReceiver(CityGameRoot city, out CityBuildingAssetRegistry building,
            out CityBuildingWindowSlot pane, out Vector3 center, out Vector3 outward)
        {
            for (int pass = 0; pass < 2; pass++)
            foreach (CityBuildingAssetRegistry candidate in city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>())
            {
                if (candidate.District != CityDistrictKind.Residential) continue;
                foreach (CityBuildingWindowSlot slot in candidate.WindowSlots)
                {
                    if (slot.Floor != 0 || pass == 0 && slot.Side != "Front" || slot.OpeningKind != CityBuildingOpeningKind.Window ||
                        (candidate.GuaranteedWindowMask & (1UL << slot.Uv2SlotId)) == 0) continue;
                    building = candidate; pane = slot;
                    center = candidate.transform.TransformPoint(slot.LocalCenter);
                    outward = candidate.transform.TransformDirection(WindowLightOutward(slot.Side)).normalized;
                    Debug.Log("CityWindowLighting receiver: " + candidate.StableId + " at " + center + ", slot=" + slot.SlotId);
                    return;
                }
            }
            throw new AssertionException("The actual Residential geometry must expose a lit ground-floor window.");
        }

        private static void SetWindowLightCamera(CityGameRoot city, Vector3 position, Vector3 target)
        {
            city.Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            city.Camera.fieldOfView = 63f;
            city.Camera.aspect = (float)WindowLightCaptureWidth / WindowLightCaptureHeight;
            city.Night.Atmosphere.RefreshImmediate();
            Physics.SyncTransforms();
        }

        private static void AssertWindowLightSource(WindowLighting windows, Vector3 expected)
        {
            for (int index = 0; index < windows.Lights.Count; index++)
                if (windows.TryGetSelectedWindow(index, out Vector3 actual, out _) && (actual - expected).sqrMagnitude < .01f) return;
            Assert.Fail("Every lit receiver window must be represented by its permanent real Light.");
        }

        private static void AssertWindowLightBudget(WindowLighting windows)
        {
            Assert.That(windows.ActiveCount, Is.InRange(1, windows.Lights.Count));
            Assert.That(windows.Lights.Count, Is.InRange(1, WindowLighting.MaximumLights));
            foreach (Light light in windows.Lights)
            {
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(light.range, Is.LessThanOrEqualTo(WindowLighting.MaximumRange));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard), "The real aperture must occlude the outgoing light.");
                Assert.That(light.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier,
                    Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow));
                Assert.That(light.color, Is.EqualTo(CityNightAtmosphere.StreetLampColor));
            }
        }

        private static void AssertWindowLightsOff(WindowLighting windows)
        {
            Assert.That(windows.ActiveCount, Is.Zero);
            foreach (Light light in windows.Lights) Assert.That(light.enabled, Is.False);
        }

        private static Vector3 WindowLightOutward(string side)
        {
            switch (side)
            {
                case "Front": return Vector3.forward;
                case "Rear": return Vector3.back;
                case "Left": return Vector3.left;
                case "Right": return Vector3.right;
                default: throw new AssertionException("Unknown authored window side: " + side);
            }
        }

        private static void AssertPermanentWindowSources(CityGameRoot city, WindowLighting windows)
        {
            CityBuildingAssetRegistry[] buildings = city.GetComponentsInChildren<CityBuildingAssetRegistry>();
            AssertWindowSourceModels(buildings, windows);
            AssertResidentialWindowInventory(city, buildings);
        }

        private static void AssertWindowSourceModels(CityBuildingAssetRegistry[] buildings, WindowLighting windows)
        {
            int expected = 0, expectedActive = 0;
            var properties = new MaterialPropertyBlock();
            foreach (CityBuildingAssetRegistry building in buildings)
            {
                ulong mask = building.LitWindowMask;
                Assert.That(mask, Is.EqualTo(CityBuildingWindowSlotAppearance.ResolveTimedMask(building,
                    CityBuildingWindowSlotAppearance.AbsoluteMinutes)), "Glass must follow the shared absolute game clock.");
                ulong guaranteed = building.GuaranteedWindowMask;
                ulong candidates = guaranteed | building.OptionalWindowMask;
                Assert.That(guaranteed != 0 && (guaranteed & (guaranteed - 1)) == 0, Is.True,
                    building.StableId + " must retain one guaranteed glowing ordinary window.");
                Assert.That((mask & guaranteed) == guaranteed && (mask & ~candidates) == 0, Is.True,
                    "Timed occupancy must never darken the last pane or light an unauthored source.");
                Assert.That(building.OptionalWindowMask == 0 || building.District == CityDistrictKind.Residential, Is.True);
                Assert.That(building.TryGetRenderer(CityBuildingMeshRole.WindowGlass, out Renderer glass), Is.True);
                properties.Clear();
                glass.GetPropertyBlock(properties);
                float[] states = properties.GetFloatArray("_CityBuildingWindowStates");
                Assert.That(states, Is.Not.Null.And.Not.Empty);
                foreach (CityBuildingWindowSlot slot in building.WindowSlots)
                {
                    bool lit = (mask & (1UL << slot.Uv2SlotId)) != 0;
                    bool visibleEmission = (int)states[slot.Uv2SlotId] / CityWindowAppearance.VariantCount != (int)CityWindowFamily.Off;
                    Assert.That(visibleEmission, Is.EqualTo(lit), "Visible emission and real source must agree: " + building.StableId);
                    if ((candidates & (1UL << slot.Uv2SlotId)) == 0) continue;
                    Assert.That(slot.OpeningKind, Is.EqualTo(CityBuildingOpeningKind.Window));
                    Vector3 center = building.transform.TransformPoint(slot.LocalCenter);
                    AssertWindowLightSource(windows, center);
                    int matches = 0;
                    for (int i = 0; i < windows.Lights.Count; i++)
                        if (windows.TryGetSelectedWindow(i, out Vector3 source, out _) && (source - center).sqrMagnitude < .0001f)
                        {
                            matches++;
                            Assert.That(windows.Lights[i].enabled, Is.EqualTo(lit && building.gameObject.activeInHierarchy),
                                "Glass emission and its permanent real light must change together: " + building.transform.parent.name +
                                ", pane=" + slot.Uv2SlotId + ", mask=" + mask + ", active=" + windows.ActiveCount +
                                ", owner=" + windows.isActiveAndEnabled);
                        }
                    Assert.That(matches, Is.EqualTo(1));
                    if (lit && building.gameObject.activeInHierarchy) expectedActive++;
                    expected++;
                }
            }
            Assert.That(windows.Lights.Count, Is.EqualTo(expected), "Guaranteed and timed panes each own a fixed Light.");
            Assert.That(windows.ActiveCount, Is.EqualTo(expectedActive));
        }

        private static void AssertResidentialWindowInventory(CityGameRoot city, CityBuildingAssetRegistry[] actual)
        {
            var expected = new HashSet<BuildingLot>();
            foreach (BuildingLot lot in city.Layout.BuildingMasses)
                if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential) expected.Add(lot);
            foreach (CityHillsideBuilding house in CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings)
                if (house.Lot.IsOrdinaryBuilding) expected.Add(house.Lot);
            foreach (BuildingLot lot in expected)
            {
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                Vector3 entrance = pose.TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant));
                int matches = 0;
                foreach (CityBuildingAssetRegistry building in actual)
                    if (building.HasEntrance && Vector3.Distance(building.transform.TransformPoint(building.LocalEntranceThreshold), entrance) < .05f)
                    { matches++; Assert.That(building.LitWindowMask, Is.Not.Zero, "A residential home cannot be dark: " + lot.Cell); }
                Assert.That(matches, Is.EqualTo(1), "The window check must include lowland, infill, slopes and Ridge: " + lot.Cell);
            }
            PlayerHomeExteriorAssetRegistry hero = city.World.Root.GetComponentInChildren<PlayerHomeExteriorAssetRegistry>();
            Assert.That(hero, Is.Not.Null);
            int litHomePanes = 0;
            foreach (PlayerHomeExteriorPartBinding part in hero.Parts)
                if (part.Emissive && part.Sheet == "WindowGlass" && part.Renderer != null && part.Renderer.enabled)
                {
                    Material material = part.Renderer.sharedMaterial;
                    Assert.That(material != null && material.IsKeywordEnabled("_EMISSION") &&
                        material.GetColor("_EmissionColor").maxColorComponent > .001f, Is.True,
                        "The bespoke hero home must retain its visible warm lit glass.");
                    litHomePanes++;
                }
            Assert.That(litHomePanes, Is.GreaterThanOrEqualTo(1));
        }

        private static void AssertNightWindowOccupancy(CityGameRoot city, WindowLighting windows)
        {
            var buildings = city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>();
            CityBuildingAssetRegistry timed = null;
            foreach (CityBuildingAssetRegistry building in buildings)
                if (building.OptionalWindowMask != 0) { timed = building; break; }
            Assert.That(timed, Is.Not.Null, "Residential homes need a bounded timed occupancy candidate.");
            double now = CityBuildingWindowSlotAppearance.AbsoluteMinutes;
            double block = Math.Ceiling(now / 1440d) * 1440d;
            CityBuildingWindowSlotAppearance.GetOptionalInterval(block, timed.WindowScheduleSeed, out double start, out double end);
            Assert.That(end - start, Is.InRange(20d, 50d));
            Assert.That(CityBuildingWindowSlotAppearance.OptionalPaneIsOn((start + end) * .5d, timed.WindowScheduleSeed), Is.True);
            Assert.That(CityBuildingWindowSlotAppearance.OptionalPaneIsOn(end + 1d, timed.WindowScheduleSeed), Is.False);
            Assert.That(CityBuildingWindowSlotAppearance.OptionalPaneIsOn(double.NaN, timed.WindowScheduleSeed), Is.False);
            var identities = windows.Lights.ToArray();
            var positions = identities.Select(light => light.transform.position).ToArray();
            uint initialGeneration = windows.StateGeneration;
            foreach (double minute in new[] { block, (start + end) * .5d, end + 1d, block + 230d })
            {
                double delta = minute - CityBuildingWindowSlotAppearance.AbsoluteMinutes;
                if (delta > 0d) GameSessionState.AdvanceGameTime((float)(delta / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                windows.RefreshImmediate();
                Assert.That(CityWindowAppearance.NightFactor, Is.GreaterThan(.99f));
                AssertPermanentWindowSources(city, windows);
                ulong mask = timed.LitWindowMask;
                uint generation = windows.StateGeneration;
                windows.RefreshImmediate();
                Assert.That(timed.LitWindowMask, Is.EqualTo(mask), "The same absolute time must reconstruct the same occupancy.");
                Assert.That(windows.StateGeneration, Is.EqualTo(generation), "An unchanged clock must not invalidate reflection history.");
                Assert.That(windows.Lights, Is.EqualTo(identities));
                for (int i = 0; i < identities.Length; i++) Assert.That(identities[i].transform.position, Is.EqualTo(positions[i]));
                bool shouldBeOn = CityBuildingWindowSlotAppearance.OptionalPaneIsOn(
                    CityBuildingWindowSlotAppearance.AbsoluteMinutes, timed.WindowScheduleSeed);
                Assert.That((timed.LitWindowMask & timed.OptionalWindowMask) != 0, Is.EqualTo(shouldBeOn));
                if (minute == (start + end) * .5d || minute == end + 1d)
                {
                    CityBuildingWindowSlot slot = timed.WindowSlots.First(pane => (timed.OptionalWindowMask & (1UL << pane.Uv2SlotId)) != 0);
                    Vector3 center = timed.transform.TransformPoint(slot.LocalCenter);
                    Vector3 outward = timed.transform.TransformDirection(WindowLightOutward(slot.Side));
                    SetWindowLightCamera(city, center + outward * 7f + Vector3.up, center);
                    CaptureCurrentCamera(city.Camera, WindowLightCaptureArea, shouldBeOn ? "11-timed-window-on" : "12-timed-window-off",
                        WindowLightCaptureWidth, WindowLightCaptureHeight);
                }
            }
            Assert.That(windows.StateGeneration, Is.GreaterThan(initialGeneration),
                "Real glass state transitions must invalidate the existing bounded reflection cache.");
            Debug.Log("CityLightingContinuity: every actual residential home retains a warm pane; seeded timed glass/light transitions and fixed source identities verified.");
        }

        private static void AssertPermanentStreetSources(CityNightAtmosphere atmosphere)
        {
            Assert.That(atmosphere.StreetLightPool.Count, Is.GreaterThan(12),
                "Street lights must cover their actual fixtures instead of twelve proximity leases.");
            Assert.That(atmosphere.StreetLightPool.Count, Is.GreaterThanOrEqualTo(atmosphere.LampAnchors.Count));
            for (int index = 0; index < atmosphere.LampAnchors.Count; index++)
            {
                Transform fixture = atmosphere.LampAnchors[index];
                if (fixture == null) continue;
                Light light = atmosphere.GetLightForAnchor(index);
                Assert.That(light, Is.Not.Null, "Every authored fixture needs a permanent Light.");
                Assert.That(light, Is.SameAs(atmosphere.StreetLightPool[index]));
                Assert.That(Vector3.Distance(light.transform.position, fixture.position), Is.LessThan(.001f));
                if (fixture.gameObject.activeInHierarchy)
                    Assert.That(light.isActiveAndEnabled && light.intensity > 0f, Is.True,
                        "A distant fixture stays lit: " + fixture.name);
            }
        }

        private static void AssertHeroIndependentCityLighting(CityGameRoot city, WindowLighting windows,
            IReadOnlyList<Light> windowSources, IReadOnlyList<Light> streetSources, Vector3 cameraPosition,
            Vector3 cameraTarget, Vector3 wall, Vector3 ground)
        {
            var lights = new List<Light>(windowSources.Count + streetSources.Count);
            lights.AddRange(windowSources); lights.AddRange(streetSources);
            var positions = new Vector3[lights.Count];
            var rotations = new Quaternion[lights.Count];
            var powers = new float[lights.Count];
            var enabled = new bool[lights.Count];
            for (int index = 0; index < lights.Count; index++)
            {
                positions[index] = lights[index].transform.position;
                rotations[index] = lights[index].transform.rotation;
                powers[index] = lights[index].intensity;
                enabled[index] = lights[index].enabled;
            }
            CaptureCurrentCamera(city.Camera, WindowLightCaptureArea, "04-fixed-camera-hero-near",
                WindowLightCaptureWidth, WindowLightCaptureHeight);
            city.Player.Motor.Teleport(cameraPosition + Vector3.right * 100f);
            Assert.That(Vector3.Distance(city.Player.Motor.transform.position, cameraPosition), Is.GreaterThan(80f));
            SetWindowLightCamera(city, cameraPosition, cameraTarget);
            windows.RefreshImmediate();
            for (int index = 0; index < lights.Count; index++)
            {
                Assert.That(lights[index].transform.position, Is.EqualTo(positions[index]),
                    "Hero movement must not relocate " + lights[index].name);
                Assert.That(lights[index].transform.rotation, Is.EqualTo(rotations[index]));
                Assert.That(lights[index].intensity, Is.EqualTo(powers[index]).Within(.0001f),
                    "Hero proximity must not change fixture power: " + lights[index].name);
                Assert.That(lights[index].enabled, Is.EqualTo(enabled[index]),
                    "Hero distance must not change a permanent source's availability.");
            }
            Assert.That(windows.Lights, Is.EqualTo(windowSources));
            Assert.That(city.Night.Atmosphere.StreetLightPool, Is.EqualTo(streetSources));
            CaptureCurrentCamera(city.Camera, WindowLightCaptureArea, "05-fixed-camera-hero-far",
                WindowLightCaptureWidth, WindowLightCaptureHeight);
            AssertWindowReceiverUnchanged(city.Camera, wall, "window sill/reveal");
            AssertWindowReceiverUnchanged(city.Camera, ground, "pavement");
            city.Player.Motor.Teleport(cameraPosition - Vector3.up * EyeHeight);
            city.Night.Atmosphere.RefreshImmediate();
            windows.RefreshImmediate();
        }

        private static void CapturePersistentCityViews(CityGameRoot city, WindowLighting windows,
            Vector3 closePosition, Vector3 closeTarget)
        {
            // Stand on the actual open summit bridge. The legacy lot balcony
            // coordinates belong to the old lower house, not this Ridge view.
            CityHillsideQuarterPlan quarter = CityHillsideQuarterPlan.ForLayout(city.Layout);
            var summit = quarter.Links.Single(link => link.Id == "summit");
            var bridge = quarter.Bridges.Single(item => item.Id == "summit");
            Vector3 centre = bridge.DeckCenterWorld;
            Vector3 ridgeFloor = new Vector3(centre.x, summit.PavementTopAt(centre.x), centre.z + 3.85f);
            Vector3 ridgePosition = ridgeFloor + Vector3.up * EyeHeight;
            Vector3 cityTarget = centre + new Vector3(4f, -10f, 70f);
            city.Player.Motor.Teleport(ridgeFloor);
            SetWindowLightCamera(city, ridgePosition, cityTarget);
            city.Visibility.Refresh(true);
            Assert.That(city.Camera.farClipPlane, Is.EqualTo(CityAltitudeVisibility.SummitFarPlane).Within(.01f));
            Assert.That(city.Visibility.Density, Is.EqualTo(CityAltitudeVisibility.SummitDensity).Within(.0001f));
            windows.RefreshImmediate();
            LogCityVisibleLightBudget(city.Camera, "Ridge");
            CaptureCurrentCamera(city.Camera, WindowLightCaptureArea, "06-ridge-city-night",
                WindowLightCaptureWidth, WindowLightCaptureHeight);
            Vector3 approach = ridgePosition + Vector3.right * 12f;
            city.Player.Motor.Teleport(approach - Vector3.up * EyeHeight);
            SetWindowLightCamera(city, approach, cityTarget);
            city.Visibility.Refresh(true);
            windows.RefreshImmediate();
            LogCityVisibleLightBudget(city.Camera, "Ridge approach");
            CaptureCurrentCamera(city.Camera, WindowLightCaptureArea, "07-ridge-approach-night",
                WindowLightCaptureWidth, WindowLightCaptureHeight);
            city.Player.Motor.Teleport(closePosition - Vector3.up * EyeHeight);
            SetWindowLightCamera(city, closePosition, closeTarget);
            city.Visibility.Refresh(true);
            windows.RefreshImmediate();
            LogCityVisibleLightBudget(city.Camera, "Window facade");
        }

        private static void LogCityVisibleLightBudget(Camera camera, string view)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            Light[] all = Object.FindObjectsByType<Light>();
            int permanentActive = 0, conservativeVisible = 0;
            foreach (Light light in all)
            {
                if (!light.isActiveAndEnabled || light.intensity <= .001f ||
                    light.type != LightType.Point && light.type != LightType.Spot) continue;
                permanentActive++;
                if (GeometryUtility.TestPlanesAABB(planes, new Bounds(light.transform.position, Vector3.one * light.range * 2f)))
                    conservativeVisible++;
            }
            Debug.Log("CityLightingContinuity " + view + ": total active local=" + permanentActive +
                ", conservative visible volumes=" + conservativeVisible + ", camera far=" + camera.farClipPlane +
                ", URP desktop visible-light limit=256; GPU/CPU frame delta unavailable.");
            Assert.That(conservativeVisible, Is.LessThanOrEqualTo(254),
                "The actual view must leave headroom below URP's visible-light limit without proximity leases.");
        }

        private static void AssertWindowReceiverUnchanged(Camera camera, Vector3 receiver, string description)
        {
            Vector3 viewport = camera.WorldToViewportPoint(receiver);
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", WindowLightCaptureArea);
            var near = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var far = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(near.LoadImage(File.ReadAllBytes(Path.Combine(folder, "04-fixed-camera-hero-near.png"))), Is.True);
                Assert.That(far.LoadImage(File.ReadAllBytes(Path.Combine(folder, "05-fixed-camera-hero-far.png"))), Is.True);
                int x = Mathf.RoundToInt(viewport.x * near.width), y = Mathf.RoundToInt(viewport.y * near.height);
                const int radius = 24;
                Color[] a = near.GetPixels(x - radius, y - radius, radius * 2, radius * 2);
                Color[] b = far.GetPixels(x - radius, y - radius, radius * 2, radius * 2);
                int changed = 0;
                for (int index = 0; index < a.Length; index++)
                    if (Mathf.Abs(a[index].r - b[index].r) + Mathf.Abs(a[index].g - b[index].g) +
                        Mathf.Abs(a[index].b - b[index].b) > 1f / 255f) changed++;
                Debug.Log("CityLightingContinuity fixed-camera " + description + ": " + changed + " pixels changed by hero distance.");
                Assert.That(changed, Is.Zero, "Hero distance must not change the same visible " + description + " pixels.");
            }
            finally { Object.DestroyImmediate(near); Object.DestroyImmediate(far); }
        }

        private static void CaptureWindowLightPair(Camera camera, WindowLighting windows, string name,
            Vector3 wall, Vector3 ground)
        {
            var powers = new float[windows.Lights.Count];
            for (int index = 0; index < powers.Length; index++)
            {
                powers[index] = windows.Lights[index].intensity;
                windows.Lights[index].intensity = 0f;
            }
            try
            {
                CaptureCurrentCamera(camera, WindowLightCaptureArea, name + "-off",
                    WindowLightCaptureWidth, WindowLightCaptureHeight);
            }
            finally
            {
                for (int index = 0; index < powers.Length; index++) windows.Lights[index].intensity = powers[index];
            }
            CaptureCurrentCamera(camera, WindowLightCaptureArea, name + "-on",
                WindowLightCaptureWidth, WindowLightCaptureHeight);
            AssertWindowReceiverDifference(camera, name, "window sill/reveal", wall);
            AssertWindowReceiverDifference(camera, name, "pavement", ground);
        }

        private static void AssertWindowReceiverDifference(Camera camera, string name, string receiver, Vector3 position)
        {
            Vector3 viewport = camera.WorldToViewportPoint(position);
            Assert.That(viewport.z, Is.GreaterThan(0));
            Assert.That(viewport.x, Is.InRange(.05f, .95f));
            Assert.That(viewport.y, Is.InRange(.05f, .95f));
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", WindowLightCaptureArea);
            var before = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var after = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(before.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-off.png"))), Is.True);
                Assert.That(after.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-on.png"))), Is.True);
                int x = Mathf.RoundToInt(viewport.x * before.width), y = Mathf.RoundToInt(viewport.y * before.height);
                const int radius = 24;
                Color[] a = before.GetPixels(x - radius, y - radius, radius * 2, radius * 2);
                Color[] b = after.GetPixels(x - radius, y - radius, radius * 2, radius * 2);
                int brighter = 0;
                for (int pixel = 0; pixel < a.Length; pixel++)
                    if ((b[pixel].r + b[pixel].g + b[pixel].b) - (a[pixel].r + a[pixel].g + a[pixel].b) >= 3f / 255f)
                        brighter++;
                Debug.Log("CityWindowLighting " + name + " " + receiver + ": " + brighter + " brighter pixels.");
                Assert.That(brighter, Is.GreaterThan(8),
                    "Real window light must brighten the " + receiver + "; an unchanged wall beside an emissive pane is insufficient.");
            }
            finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
        }
    }
}
