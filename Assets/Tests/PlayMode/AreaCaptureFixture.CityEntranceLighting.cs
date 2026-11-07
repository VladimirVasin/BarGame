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

                // The road-access plan deliberately excludes hillside houses.
                // Use the independent inventory of all building lots so this
                // acceptance cannot repeat the runtime builder's old omission.
                List<CityResidentialEntranceDescriptor> entrances = ExpectedEntranceLampDoors(city.Layout);
                Assert.That(entrances, Is.Not.Empty);
                var sources = new List<Transform>();
                var lights = new List<Light>();
                var claimed = new HashSet<Transform>();
                CityResidentialEntranceDescriptor capture = null, highest = null;
                int fresh = 0, reused = 0;
                foreach (CityResidentialEntranceDescriptor entrance in entrances)
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
                Assert.That(fresh + reused, Is.EqualTo(entrances.Count));
                Assert.That(capture, Is.Not.Null);
                AssertEntranceRegistryInventory(city.World.Root.transform, entrances);
                AssertPlayerHomeEntranceLamp(city, claimed);
                Debug.Log("CityEntranceLighting coverage: entries=" + entrances.Count + ", new=" + fresh + ", existing canopies=" + reused);

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
                CaptureReportedRidgeEntrance(city, entrances, lights);

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
                double toNight = (21 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(toNight / GameTimeState.GameMinutesPerRealSecond));
                GameSessionState.EnterHome();
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
                yield return AssertHomeEntranceLampCoverage();
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

        private static List<CityResidentialEntranceDescriptor> ExpectedEntranceLampDoors(CityLayout layout)
        {
            var result = new List<CityResidentialEntranceDescriptor>();
            var included = new HashSet<BuildingLot>();
            foreach (BuildingLot lot in layout.BuildingMasses) Add(lot);
            foreach (CityHillsideBuilding house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings) Add(house.Lot);
            return result;

            void Add(BuildingLot lot)
            {
                if (!lot.IsOrdinaryBuilding || lot.District != CityDistrictKind.Residential || !included.Add(lot)) return;
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                result.Add(new CityResidentialEntranceDescriptor(lot,
                    pose.TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant)),
                    pose.Rotation * CityBuildingAssetProvider.GetExpectedEntranceOutward(lot.BuildingVariant),
                    Array.Empty<Vector3>(), Array.Empty<CityEntranceForecourtPlan>()));
            }
        }

        private static void AssertEntranceRegistryInventory(Transform root,
            IReadOnlyList<CityResidentialEntranceDescriptor> expected)
        {
            int actual = 0;
            foreach (CityBuildingAssetRegistry building in root.GetComponentsInChildren<CityBuildingAssetRegistry>(true))
            {
                if (building.District != CityDistrictKind.Residential) continue;
                Assert.That(building.HasEntrance, Is.True);
                Vector3 threshold = building.transform.TransformPoint(building.LocalEntranceThreshold);
                int matches = 0;
                foreach (CityResidentialEntranceDescriptor entry in expected)
                    if (Vector3.Distance(entry.Threshold, threshold) < .05f) matches++;
                Assert.That(matches, Is.EqualTo(1), "Every actual model entrance must match the independent lot inventory: " + threshold);
                actual++;
            }
            Assert.That(actual, Is.EqualTo(expected.Count), "The inventory must include lowland, infill, slope and shared-court homes.");
        }

        private static void AssertPlayerHomeEntranceLamp(CityGameRoot city, HashSet<Transform> claimed)
        {
            int found = 0;
            for (int i = 0; i < city.Night.Atmosphere.LampAnchors.Count; i++)
            {
                Transform anchor = city.Night.Atmosphere.LampAnchors[i];
                if (anchor == null || anchor.name != "IndustrialTaskLight_HomeEntrance") continue;
                found++;
                Assert.That(claimed.Add(anchor), Is.True, "The hero's bespoke doorway must retain its own source.");
                Light light = city.Night.Atmosphere.GetLightForAnchor(i);
                Assert.That(light != null && light.isActiveAndEnabled, Is.True);
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(light.intensity, Is.EqualTo(4.2f).Within(.001f));
                Assert.That(light.range, Is.EqualTo(4.5f).Within(.001f));
                Renderer lens = null;
                foreach (Renderer renderer in anchor.parent.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name == "Home Entrance Lamp") { lens = renderer; break; }
                Assert.That(lens, Is.Not.Null, "The existing player-home light must still have its visible lamp.");
                Assert.That(Vector3.Distance(lens.bounds.ClosestPoint(anchor.position), anchor.position), Is.LessThan(.03f));
            }
            Assert.That(found, Is.EqualTo(1), "The bespoke hero-home lamp is reused exactly once.");
        }

        private static void CaptureReportedRidgeEntrance(CityGameRoot city,
            IReadOnlyList<CityResidentialEntranceDescriptor> entrances, IReadOnlyList<Light> lights)
        {
            Vector2 reported = new Vector2(-70.8f, -350.2f);
            CityHillsideBuilding nearest = null;
            float distance = float.PositiveInfinity;
            foreach (CityHillsideBuilding house in CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings)
            {
                if (house.Lot.IsPlayerHome) continue;
                float candidate = (new Vector2(house.ThresholdWorld.x, house.ThresholdWorld.z) - reported).sqrMagnitude;
                if (candidate >= distance) continue;
                nearest = house; distance = candidate;
            }
            Assert.That(nearest, Is.Not.Null);
            Assert.That(nearest.Lot.Cell, Is.EqualTo(new Vector2Int(-23, -8)), "Keep the reported west-slope house in the reproduction.");
            Assert.That(distance, Is.LessThan(25f * 25f), "The reported coordinate must identify a nearby hillside entrance.");
            Assert.That(nearest.HasGlazedBay, Is.True, "The reported blue bay must reproduce the omitted west-slope house.");
            CityResidentialEntranceDescriptor entry = null;
            foreach (CityResidentialEntranceDescriptor candidate in entrances)
                if (ReferenceEquals(candidate.Lot, nearest.Lot)) { entry = candidate; break; }
            Assert.That(entry, Is.Not.Null);
            Transform source = FindEntranceLampSource(city.Night.Atmosphere, entry, out _);
            Transform fixture = FindEntranceLampVisual(source, out _, out Bounds bounds);
            CityBuildingAssetRegistry registry = FindEntranceLampBuilding(city.World.Root.transform, entry.Threshold);
            Transform mount = registry.transform.Find(CityResidentialEntranceLighting.CeilingMountName);
            Assert.That(mount, Is.Not.Null, "A glazed stair bay needs the measured landing mount.");
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(mount.position.y + .005f), "No part of the lamp may be buried in its stair landing.");
            Assert.That(source.position.y, Is.LessThan(mount.position.y - .05f));
            Assert.That(fixture.up.y, Is.GreaterThan(.99f));
            Vector3 tangent = Vector3.Cross(Vector3.up, entry.Outward);
            Vector3 eye = entry.Threshold + entry.Outward * 4.5f + tangent * 1.1f + Vector3.up * EyeHeight;
            city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
            SetEntranceLampCamera(city, eye, entry.Threshold + Vector3.up * 2f);
            Vector3 lensViewport = city.Camera.WorldToViewportPoint(source.position);
            Assert.That(lensViewport.z, Is.GreaterThan(0));
            Assert.That(lensViewport.x, Is.InRange(.1f, .9f));
            Assert.That(lensViewport.y, Is.InRange(.1f, .9f));
            CaptureEntranceLampPair(city.Camera, lights, "05-reported-ridge-night",
                entry.Threshold - entry.Outward * .65f + Vector3.up * 1.05f,
                entry.Threshold + entry.Outward * .75f + Vector3.up * .035f);
            Debug.Log("CityEntranceLighting reported ridge: lot=" + nearest.Lot.Cell + ", threshold=" + entry.Threshold + ", source=" + source.position);
        }

        private static IEnumerator AssertHomeEntranceLampCoverage()
        {
            HomeInteriorRoot home = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized) break;
                yield return null;
            }
            Assert.That(home != null && home.IsInitialized, Is.True);
            Rect activation = home.BalconyLayout.BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(new Vector3(activation.center.x, home.Layout.PlayerSpawn.y, activation.center.y));
            for (int frame = 0; frame < 5; frame++) yield return null;
            home.ExteriorAtmosphere.RefreshImmediate();
            Assert.That(home.ExteriorAtmosphere.IsBalconyVisibilityActive, Is.True);
            CityNightAtmosphere atmosphere = home.ExteriorAtmosphere.ExteriorLighting;
            Assert.That(atmosphere, Is.Not.Null);
            atmosphere.RefreshImmediate();
            var expected = new List<CityResidentialEntranceDescriptor>();
            HomeExteriorContextPlan context = home.ExteriorContext;
            float radius = HomeExteriorContextPlanner.ViewRadius;
            Vector3 origin = context.PlayerHome.DoorPosition;
            var window = new Rect(origin.x - radius, origin.z - radius, radius * 2f, radius * 2f);
            var hillside = new HashSet<BuildingLot>();
            foreach (CityHillsideBuilding house in CityHillsideNeighborhoodPlan.ForLayout(context.Layout).AllBuildings)
                if (!house.Lot.IsPlayerHome && (window.Overlaps(house.FoundationBounds) || window.Overlaps(house.ApproachBounds)))
                    hillside.Add(house.Lot);
            foreach (CityResidentialEntranceDescriptor entry in ExpectedEntranceLampDoors(context.Layout))
            {
                bool retained = hillside.Contains(entry.Lot);
                if (!retained)
                    foreach (BuildingLot lot in context.NearbyLots)
                        if (ReferenceEquals(lot, entry.Lot) && CityBuildingPrototypeWorldBuilder.ClassifyHomeExterior(context, lot) == CityBuildingExteriorFit.Full)
                        { retained = true; break; }
                if (!retained) continue;
                Vector3 threshold = home.ExteriorView.TransformPoint(PlayerHomeBalconyGeometry.ToHomeLocal(context.PlayerHome, entry.Threshold));
                Vector3 outward = home.ExteriorView.TransformDirection(PlayerHomeBalconyGeometry.ToHomeLocalDirection(context.PlayerHome, entry.Outward));
                expected.Add(new CityResidentialEntranceDescriptor(entry.Lot, threshold, outward,
                    Array.Empty<Vector3>(), Array.Empty<CityEntranceForecourtPlan>()));
            }
            Assert.That(hillside.Count, Is.GreaterThan(0), "The retained Home frame must exercise separately built hillside homes.");
            AssertEntranceRegistryInventory(home.ExteriorView, expected);
            home.ExteriorLighting.WindowLights.RefreshImmediate();
            AssertWindowSourceModels(home.ExteriorView.GetComponentsInChildren<CityBuildingAssetRegistry>(),
                home.ExteriorLighting.WindowLights);
            var claimed = new HashSet<Transform>();
            foreach (CityResidentialEntranceDescriptor entry in expected)
            {
                Transform source = FindEntranceLampSource(atmosphere, entry, out Light light);
                Assert.That(claimed.Add(source), Is.True, "No Home-frame entrance may borrow another doorway's fixture.");
                Assert.That(light.isActiveAndEnabled && light.intensity > 0f, Is.True);
                Assert.That(light.type, Is.EqualTo(LightType.Point));
                Assert.That(light.range, Is.EqualTo(5.5f).Within(.001f));
                FindEntranceLampVisual(source, out Renderer lens, out Bounds measured);
                Assert.That(Vector3.Distance(lens.bounds.ClosestPoint(source.position), source.position), Is.LessThan(.15f));
                CityBuildingAssetRegistry building = FindEntranceLampBuilding(home.ExteriorView, entry.Threshold);
                Transform mount = building.transform.Find(CityResidentialEntranceLighting.CeilingMountName);
                if (mount != null) Assert.That(measured.max.y, Is.LessThanOrEqualTo(mount.position.y + .005f));
            }
            foreach (Collider collider in home.ExteriorView.GetComponentsInChildren<Collider>(true))
                Assert.That(collider.enabled, Is.False, "The shared fixture fix must preserve the passive exterior.");
            CaptureCurrentCamera(home.CameraFollow.GetComponent<Camera>(), EntranceLampCaptureArea,
                "06-home-balcony-night", EntranceLampWidth, EntranceLampHeight);
            Debug.Log("CityEntranceLighting Home coverage: expected=" + expected.Count + ", hillside=" + hillside.Count);
        }

        private static CityBuildingAssetRegistry FindEntranceLampBuilding(Transform root, Vector3 threshold)
        {
            foreach (CityBuildingAssetRegistry building in root.GetComponentsInChildren<CityBuildingAssetRegistry>(true))
                if (building.HasEntrance && Vector3.Distance(building.transform.TransformPoint(building.LocalEntranceThreshold), threshold) < .05f)
                    return building;
            throw new AssertionException("The measured entrance must belong to an actual Blender building: " + threshold);
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
            Vector3 door, Vector3 paving, params Vector3[] surrounding)
        {
            var powers = new float[lights.Count];
            for (int i = 0; i < lights.Count; i++) { powers[i] = lights[i].intensity; lights[i].intensity = 0f; }
            try { CaptureCurrentCamera(camera, EntranceLampCaptureArea, name + "-off", EntranceLampWidth, EntranceLampHeight); }
            finally { for (int i = 0; i < lights.Count; i++) lights[i].intensity = powers[i]; }
            CaptureCurrentCamera(camera, EntranceLampCaptureArea, name + "-on", EntranceLampWidth, EntranceLampHeight);
            AssertEntranceLampReceiver(camera, name, "door", door);
            AssertEntranceLampReceiver(camera, name, "paving", paving);
            string[] names = { "left facade", "right facade", "balcony underside" };
            for (int i = 0; i < surrounding.Length; i++) AssertEntranceLampReceiver(camera, name, names[i], surrounding[i]);
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
