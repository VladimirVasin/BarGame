using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused mountain road material surfaces, native lighting, real headlights and lifecycle acceptance.")]
        public IEnumerator MountainRoadNativeLighting()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousCaptureDelta = Time.captureDeltaTime;
            MountainRoadRoot road = null;
            RoadSurfaceAudit surfaces = null;
            GameTimeRuntime clock = null;
            bool clockWasEnabled = false;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("Test mountain road lighting");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                SetRoadNativeEffects(true);
                yield return Capture(SceneIds.MountainRoad,
                    () =>
                    {
                        road = Object.FindAnyObjectByType<MountainRoadRoot>();
                        return road != null && road.IsInitialized ? road : null;
                    },
                    () =>
                    {
                        // Freeze authoritative time without pausing: reflection
                        // submission correctly waits for an unpaused world.
                        // A/B frames render synchronously without advancing snow.
                        clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                        Assert.That(clock, Is.Not.Null);
                        clockWasEnabled = clock.enabled;
                        clock.enabled = false;
                        road.Atmosphere.enabled = false;
                        road.Weather.enabled = false;
                        surfaces = AssertRoadSurfaceContracts(road);
                        var shots = new List<Shot>(RoadNativeLightingShots(road));
                        shots.InsertRange(1, RoadSurfaceShots(road, surfaces));
                        return shots.ToArray();
                    });

                foreach (string effect in new[] { "Reflections", "Lit air", "Halation", "Metal highlights", "Bounce" })
                    AssertRoadLightingDifference(effect + " baseline", effect + " enabled");
                foreach (string material in new[] { "Cafe pigment", "Cafe physical response", "Station physical response", "Road pigment" })
                    AssertRoadLightingDifference(material + " baseline", material + " enabled");
                yield return VerifyRoadHeadlightTransport(road);
                yield return VerifyRoadLightingOwnership(road);
                surfaces.AssertRestoredBindings();
                SceneReflectionCapture[] oldZones = RoadReflectionZones(road);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                Assert.That(road == null, Is.True, "The Single-loaded area must release its presentation owner.");
                foreach (SceneReflectionCapture zone in oldZones)
                    Assert.That(zone == null, Is.True, "Area unload must destroy each owned reflection capture.");
                Assert.That(SceneLightingPresentation.Resolve(Camera.main), Is.Null);
                Assert.That(Shader.GetGlobalInt("_SceneSurfaceReflectionCount"), Is.Zero);
                Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
            }
            finally
            {
                surfaces?.Restore();
                if (road != null)
                {
                    if (road.Atmosphere != null) road.Atmosphere.enabled = true;
                    if (road.Weather != null) road.Weather.enabled = true;
                }
                if (clock != null) clock.enabled = clockWasEnabled;
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test mountain road lighting cleanup");
            }
        }

        private static Shot[] RoadNativeLightingShots(MountainRoadRoot road)
        {
            MountainRoadLightingPresentation lighting = road.Lighting;
            Assert.That(lighting, Is.Not.Null);
            Assert.That(lighting.Reflection, Is.Not.Null);
            Assert.That(lighting.Reflection.ZoneCount, Is.EqualTo(2));
            Assert.That(lighting.HalationSourceCount, Is.InRange(1, MountainRoadLightingPresentation.MaximumHalationSources));
            Assert.That(lighting.BounceRegionCount, Is.InRange(1, MountainRoadLightingPresentation.MaximumBounceRegions));
            AssertRoadExistingPracticals(road);
            AssertRoadNativeShaders();
            var plateau = road.Plan.Plateau;
            var cafe = road.Plan.Terminal.Cafe;
            Vector3 yardEye = plateau.Center - plateau.Forward * 8f + plateau.Right * 14f + Vector3.up * 3.2f;
            Vector3 yardAim = plateau.Center + plateau.Forward * 6f + Vector3.up * 1.25f;
            Vector3 cafeEye = cafe.Center - cafe.Right * 3.5f - cafe.Forward * 2.5f + Vector3.up * EyeHeight;
            Vector3 cafeAim = cafe.Center + cafe.Right * .8f - cafe.Forward * .3f + Vector3.up * 1.5f;
            Assert.That(cafe.ContainsInterior(cafeEye), Is.True, "The material frame must be inside the real cafe.");
            var brink = plateau.Brink;
            Vector3 edgeEye = brink.Corridor.Apex - brink.Corridor.Axis * 7f + plateau.Right * 4f + Vector3.up * EyeHeight;
            Vector3 edgeAim = brink.Corridor.Apex + brink.Corridor.Axis * 32f + Vector3.down * 1.4f;
            Light yard = road.Atmosphere.YardLamp;
            Vector3 haloEye = yard.transform.position + plateau.Right * 6f + plateau.Forward * 3f + Vector3.down * 3f;
            Vector3 bounceAim = RoadBounceReceiver(road);
            Vector3 bounceEye = bounceAim - cafe.Right * 1.5f - cafe.Forward * 1.8f + Vector3.up * .8f;
            MountainRoadBrookPlan brook = MountainRoadBrookPlanner.Create(road.Plan);
            foreach (SceneReflectionCapture zone in RoadReflectionZones(road))
            {
                Assert.That(zone.Bounds.Contains(brook.Bore), Is.False,
                    "The distant culvert must not borrow the terminal or cafe environment.");
                Assert.That(zone.Bounds.Contains(brink.RimCenter), Is.False,
                    "The dark brink must stay outside the two reflection receiver regions.");
            }
            var shots = new List<Shot>();
            double deadline = 0d;
            bool night = false;
            uint dayLightingGeneration = 0;
            shots.Add(Shot.At(RoadLightingFrame("Day terminal"), yardEye, yardAim, 64, readyWhen: () =>
                RoadReflectionsReady(road, ref deadline)));
            shots.Add(Shot.At(RoadLightingFrame("Day cafe"), cafeEye, cafeAim, 64));
            shots.Add(Shot.At(RoadLightingFrame("Day dark edge"), edgeEye, edgeAim, 58));
            shots.Add(Shot.At(RoadLightingFrame("Night terminal"), yardEye, yardAim, 64, readyWhen: () =>
            {
                if (!night)
                {
                    dayLightingGeneration = lighting.Reflection.LightingGeneration;
                    GameSessionState.AdvanceGameTime((float)((21 * 60 - GameSessionState.GameTimeOfDayMinutes) /
                        GameTimeState.GameMinutesPerRealSecond));
                    road.Atmosphere.ApplyCurrentTime(true);
                    night = true;
                    deadline = Time.realtimeSinceStartupAsDouble + 25d;
                }
                lighting.PrepareForCamera(Camera.main);
                if (lighting.Reflection.LightingGeneration == dayLightingGeneration)
                {
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline),
                        "The night must invalidate the daytime static environment.");
                    return false;
                }
                return RoadReflectionsReady(road, ref deadline);
            }));
            shots.Add(Shot.At(RoadLightingFrame("Night cafe"), cafeEye, cafeAim, 64));
            shots.Add(Shot.At(RoadLightingFrame("Night dark edge"), edgeEye, edgeAim, 58));
            AddPair("Reflections", cafeEye, cafeAim, value => GraphicsEffectsSettings.CleanReflectionsEnabled = value);
            AddPair("Lit air", yardEye, yardAim, value => GraphicsEffectsSettings.CleanVolumeEnabled = value);
            AddPair("Halation", haloEye, yard.transform.position, value => GraphicsEffectsSettings.CleanHalationEnabled = value);
            AddPair("Metal highlights", cafeEye, cafeAim, value => GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = value);
            AddPair("Bounce", bounceEye, bounceAim, value => GraphicsEffectsSettings.CleanBounceEnabled = value);
            return shots.ToArray();

            void AddPair(string subject, Vector3 eye, Vector3 aim, Action<bool> apply)
            {
                bool captured = false;
                shots.Add(Shot.At(RoadLightingFrame(subject + " enabled"), eye, aim, 64, readyWhen: () =>
                {
                    if (captured) return true;
                    // Capture evaluates readiness before applying its shot pose,
                    // and invokes this callback again for the final assertion.
                    // Position explicitly, then photograph both states without
                    // a yield so snow, actors and shader time cannot advance.
                    Camera camera = Camera.main;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((aim - eye).normalized, Vector3.up));
                    camera.fieldOfView = 64f;
                    var unchanged = new RoadPracticalSnapshot(road);
                    try
                    {
                        apply(false);
                        CaptureCurrentCamera(camera, SceneIds.MountainRoad, RoadLightingFrame(subject + " baseline"));
                        apply(true);
                        CaptureCurrentCamera(camera, SceneIds.MountainRoad, RoadLightingFrame(subject + " enabled"));
                        unchanged.AssertUnchanged();
                        captured = true;
                    }
                    finally { apply(true); }
                    return true;
                }));
            }
        }

        private static Vector3 RoadBounceReceiver(MountainRoadRoot road)
        {
            // Ground does not reflect onto itself. Frame a real piece of
            // counter/stool metal within a measured floor patch instead.
            for (int index = 0; index < road.Lighting.BounceRegionCount; index++)
            {
                Assert.That(road.Lighting.TryGetBounceRegion(index, out var region), Is.True);
                Assert.That(region.Source, Is.Not.Null);
                Assert.That(region.Normal.y, Is.GreaterThan(.85f));
                Assert.That(region.Visibility, Is.InRange(0f, 1f));
                if (region.Source != road.World.Cafe.Lights[0] && region.Source != road.World.Cafe.Lights[1]) continue;
                foreach (MountainRoadCafePartBinding part in road.World.Cafe.Model.Parts)
                {
                    if (part.Renderer == null || (part.SourceName != "Cafe_StoolMetal" &&
                        part.SourceName != "Cafe_CounterFootRail") || !part.Renderer.bounds.Intersects(region.Bounds)) continue;
                    return part.Renderer.bounds.ClosestPoint(region.Center + Vector3.up * .65f);
                }
            }
            Assert.Fail("A new floor bounce must reach a real cafe counter or stool surface.");
            return default;
        }

        private static bool RoadReflectionsReady(MountainRoadRoot road, ref double deadline)
        {
            if (deadline == 0d) deadline = Time.realtimeSinceStartupAsDouble + 25d;
            road.Lighting.PrepareForCamera(Camera.main);
            SceneReflectionCapture[] zones = RoadReflectionZones(road);
            Assert.That(zones[0].CaptureInProgress && zones[1].CaptureInProgress, Is.False,
                "Only one of the two bounded captures may own a GPU request at once.");
            if (road.Lighting.Reflection.IsReady)
            {
                foreach (SceneReflectionCapture zone in zones)
                    Assert.That(zone.CaptureCompletedCount, Is.GreaterThan(0));
                return true;
            }
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline),
                "Both real road reflection paths must complete, rather than silently accepting fallback lighting.");
            return false;
        }

        private static IEnumerator VerifyRoadHeadlightTransport(MountainRoadRoot road)
        {
            var car = road.LastRouteCar;
            Assert.That(car, Is.Not.Null, "Map and ordinary visits must register the late-created parked car.");
            LastRouteCarDriver driver = car.GetComponentInParent<LastRouteCarDriver>();
            Assert.That(driver, Is.Not.Null);
            LastRouteCarHeadlights headlights = driver.GetComponent<LastRouteCarHeadlights>();
            Assert.That(headlights, Is.Not.Null);
            Transform carRoot = driver.transform;
            Camera camera = Camera.main;
            Vector3 savedPosition = carRoot.position, savedEye = camera.transform.position;
            Quaternion savedRotation = carRoot.rotation, savedLook = camera.transform.rotation;
            float savedFov = camera.fieldOfView, savedPower = headlights.Power;
            bool savedDriver = driver.enabled, savedRide = road.Ride.enabled,
                savedFollow = road.CameraFollow.enabled, savedHeadlights = headlights.enabled;
            SceneReflectionCapture[] zones = RoadReflectionZones(road);
            int initialCaptures = zones[0].CaptureCount + zones[1].CaptureCount;
            try
            {
                road.CameraFollow.enabled = false;
                road.Ride.enabled = false;
                driver.enabled = false;
                headlights.enabled = false;
                headlights.SetPower(LastRouteCarHeadlights.StandingPower);
                Assert.That(headlights.LeftBeam.intensity,
                    Is.EqualTo(LastRouteCarHeadlights.StandingBeamIntensity).Within(.001f));
                LastRouteCarDrivePath descent = LastRouteMountainDrivePlanner.CreateDeparture(road.Plan);
                descent.Sample(descent.Length - 7f, out Vector3 position, out Vector3 forward);
                carRoot.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
                headlights.SetPower(1f);
                Physics.SyncTransforms();
                // The real sprung lamp carrier needs a frame to adopt the moved root.
                yield return null;
                LastRouteCarSeatViewPlan.EvaluateCamera(car.PassengerSeatAnchor.position,
                    carRoot.forward, 0f, LastRouteCarSeatViewPlan.BasePitchDegrees,
                    out Vector3 eye, out Quaternion look);
                camera.transform.SetPositionAndRotation(eye, look);
                camera.fieldOfView = LastRouteCarSeatViewPlan.FieldOfView;
                GraphicsEffectsSettings.CleanVolumeEnabled = false;
                CaptureCurrentCamera(camera, SceneIds.MountainRoad, RoadLightingFrame("Tunnel beam baseline"));
                GraphicsEffectsSettings.CleanVolumeEnabled = true;
                CaptureCurrentCamera(camera, SceneIds.MountainRoad, RoadLightingFrame("Tunnel beam enabled"));
                AssertRoadLightingDifference("Tunnel beam baseline", "Tunnel beam enabled");
                Assert.That(headlights.LeftBeam.intensity, Is.EqualTo(LastRouteCarHeadlights.BeamIntensity).Within(.01f));
                Assert.That(headlights.RightBeam.shadows, Is.Not.EqualTo(LightShadows.None));
                var lighting = road.Lighting;
                Assert.That(lighting.VolumeSourceCount, Is.InRange(1, MountainRoadLightingPresentation.MaximumVolumeSources));
                bool foundBeam = false;
                for (int index = 0; index < lighting.VolumeSourceCount; index++)
                {
                    Assert.That(lighting.TryGetVolumeSource(index, out SceneLocalLightSource source), Is.True);
                    Assert.That(source.Light, Is.Not.Null);
                    Assert.That(lighting.IsInsideVolumeSupport(index, source.Bounds.max + Vector3.one), Is.False);
                    if (source.Light != headlights.LeftBeam && source.Light != headlights.RightBeam) continue;
                    foundBeam = true;
                    Assert.That(source.SampleExistingShadows, Is.True, "Use the actual headlight shadow map.");
                    Assert.That(source.ScatteringGain, Is.InRange(.001f, 1f),
                        "Driving intensity must be normalized before integrating local air.");
                    Vector3 tunnelWall = road.Plan.Tunnel.SpawnPosition + Vector3.up * 2f +
                        Vector3.Cross(Vector3.up, road.Plan.Tunnel.OutwardAxis) * (road.Plan.Tunnel.OpeningWidth * .5f + 1f);
                    Assert.That(lighting.IsInsideVolumeSupport(index, tunnelWall), Is.False,
                        "The passenger beam must not illuminate air beyond the physical tunnel wall.");
                }
                Assert.That(foundBeam, Is.True, "Late-created moving headlights must reach the shared air renderer.");
                SceneCompositeRendererFeature feature = FindVillageLightingFeature();
                Assert.That(feature.DebugCleanVolumeApplied, Is.True);
                Assert.That(feature.DebugCleanVolumeResolution.x, Is.InRange(1, 320));
                Assert.That(feature.DebugCleanVolumeResolution.y, Is.InRange(1, 180));
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(zones[0].CaptureCount + zones[1].CaptureCount, Is.EqualTo(initialCaptures),
                    "Moving headlights must not recapture static terminal and cafe environments.");
            }
            finally
            {
                carRoot.SetPositionAndRotation(savedPosition, savedRotation);
                camera.transform.SetPositionAndRotation(savedEye, savedLook);
                camera.fieldOfView = savedFov;
                headlights.SetPower(savedPower);
                headlights.enabled = savedHeadlights;
                driver.enabled = savedDriver;
                road.Ride.enabled = savedRide;
                road.CameraFollow.enabled = savedFollow;
                GraphicsEffectsSettings.CleanVolumeEnabled = true;
            }
        }

        private static IEnumerator VerifyRoadLightingOwnership(MountainRoadRoot road)
        {
            Camera camera = Camera.main;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(road.Lighting));
            ScenePresentationExclusion exclusion = camera.gameObject.AddComponent<ScenePresentationExclusion>();
            try
            {
                CaptureCurrentCamera(camera, SceneIds.MountainRoad, RoadLightingFrame("Excluded camera"));
                Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
                Assert.That(Shader.GetGlobalVector("_WarehouseCleanEffects"), Is.EqualTo(Vector4.zero));
                Assert.That(Shader.GetGlobalInt("_AlpineLocalLightCount"), Is.Zero);
                Assert.That(Shader.GetGlobalInt("_SceneSurfaceReflectionCount"), Is.Zero);
            }
            finally { Object.DestroyImmediate(exclusion); }
            road.Lighting.enabled = false;
            CaptureCurrentCamera(camera, SceneIds.MountainRoad, RoadLightingFrame("Owner disabled"));
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.Null);
            Assert.That(road.Lighting.Reflection.IsReady, Is.False);
            Assert.That(Shader.GetGlobalInt("_WarehouseBounceCount"), Is.Zero);
            road.Lighting.enabled = true;
            double deadline = 0d;
            while (!RoadReflectionsReady(road, ref deadline)) yield return null;
            Assert.That(SceneLightingPresentation.Resolve(camera), Is.SameAs(road.Lighting));
            foreach (SceneReflectionCapture zone in RoadReflectionZones(road))
                Debug.Log("Mountain road reflection CPU/wall diagnostics: " + zone.name + " submit=" +
                    zone.CaptureSubmitTicks + "; poll=" + zone.CapturePollTicks + "; latency=" +
                    zone.LastCaptureLatencySeconds + ". Cubemap GPU cost remains unmeasured.");
        }

        private static SceneReflectionCapture[] RoadReflectionZones(MountainRoadRoot road)
        {
            var zones = new SceneReflectionCapture[2];
            for (int index = 0; index < zones.Length; index++)
            {
                Assert.That(road.Lighting.Reflection.TryGetZone(index, out zones[index]), Is.True);
                Assert.That(zones[index], Is.Not.Null);
            }
            return zones;
        }

        private static void AssertRoadExistingPracticals(MountainRoadRoot road)
        {
            Assert.That(road.World.Root.GetComponentsInChildren<Light>(true).Length, Is.EqualTo(6));
            Assert.That(road.Atmosphere.GetComponentsInChildren<Light>(true).Length, Is.EqualTo(3));
            Assert.That(road.World.Cafe.Lights.Count, Is.EqualTo(3));
            float[] cafeIntensities = { 60f, 53f, 8.5f };
            for (int index = 0; index < cafeIntensities.Length; index++)
                Assert.That(road.World.Cafe.Lights[index].intensity, Is.EqualTo(cafeIntensities[index]));
        }

        private sealed class RoadPracticalSnapshot
        {
            private readonly Light[] lights;
            private readonly float[] intensities;
            internal RoadPracticalSnapshot(MountainRoadRoot road)
            {
                lights = road.GetComponentsInChildren<Light>(true);
                intensities = new float[lights.Length];
                for (int index = 0; index < lights.Length; index++) intensities[index] = lights[index].intensity;
            }
            internal void AssertUnchanged()
            {
                for (int index = 0; index < lights.Length; index++)
                    Assert.That(lights[index].intensity, Is.EqualTo(intensities[index]),
                        "Optional presentation must preserve the real light power: " + lights[index].name);
            }
        }

        private static void AssertRoadNativeShaders()
        {
            foreach (string name in new[] { "Bar Promenade/Lit", "Hidden/BarPromenade/Alpine Village Local Lit Air" })
            {
                Shader shader = Shader.Find(name);
                Assert.That(shader, Is.Not.Null, name);
                Assert.That(shader.isSupported, Is.True, name);
#if UNITY_EDITOR
                Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader), Is.False, name);
#endif
            }
        }

        private static string RoadLightingFrame(string description) => "Test mountain road - " + description;

        private static void SetRoadNativeEffects(bool enabled)
        {
            GraphicsEffectsSettings.CleanReflectionsEnabled = enabled;
            GraphicsEffectsSettings.CleanVolumeEnabled = enabled;
            GraphicsEffectsSettings.CleanHalationEnabled = enabled;
            GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = enabled;
            GraphicsEffectsSettings.CleanBounceEnabled = enabled;
        }

        private static void AssertRoadLightingDifference(string before, string after)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.MountainRoad);
            var baseline = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var actual = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(baseline.LoadImage(File.ReadAllBytes(Path.Combine(folder, RoadLightingFrame(before) + ".png"))), Is.True);
                Assert.That(actual.LoadImage(File.ReadAllBytes(Path.Combine(folder, RoadLightingFrame(after) + ".png"))), Is.True);
                Color32[] a = baseline.GetPixels32(), b = actual.GetPixels32();
                int changed = 0;
                for (int index = 0; index < a.Length; index++)
                    if (Math.Abs(a[index].r - b[index].r) + Math.Abs(a[index].g - b[index].g) +
                        Math.Abs(a[index].b - b[index].b) >= 2) changed++;
                Debug.Log("Mountain road native lighting pair '" + after + "': " + changed + " changed pixels.");
                Assert.That(changed, Is.GreaterThan(24), after + " must visibly affect its actual receiver.");
            }
            finally { Object.DestroyImmediate(baseline); Object.DestroyImmediate(actual); }
        }
    }
}
