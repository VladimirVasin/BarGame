using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBalconyPresentationPlayModeTests
    {
        private const float TimeoutSeconds = 15f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.EnterHome();
            GameSessionState.ClearRoute();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene homeScene =
                SceneManager.GetSceneByName(
                    SceneIds.HomeInterior);
            if (homeScene.IsValid() &&
                homeScene.isLoaded)
            {
                Scene cleanup =
                    SceneManager.CreateScene(
                        "Home Balcony Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                AsyncOperation unload =
                    SceneManager.UnloadSceneAsync(homeScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        yield return null;
                    }
                }
            }

            GameSessionState.ClearRoute();
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            HomeScene_BuildsWalkableBalconyOnSeededStreet()
        {
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(360f);

            AsyncOperation load =
                SceneManager.LoadSceneAsync(
                    SceneIds.HomeInterior,
                    LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            HomeInteriorRoot home = null;
            float deadline =
                Time.realtimeSinceStartup +
                TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home =
                    Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                if (home != null &&
                    home.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(home, Is.Not.Null);
            Assert.That(home.IsInitialized, Is.True);
            Assert.That(home.BalconyLayout, Is.Not.Null);
            Assert.That(home.ExteriorContext, Is.Not.Null);
            Assert.That(
                home.ExteriorContext.NearbyDecorations,
                Is.Not.Empty);
            Assert.That(home.Balcony, Is.Not.Null);
            Assert.That(home.ExteriorView, Is.Not.Null);
            Assert.That(home.DayNight, Is.Not.Null);
            Assert.That(home.DayNight.IsInitialized, Is.True);
            Assert.That(
                home.DayNight.WindowDayFactor,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                home.Atmosphere.WindowLight.color,
                Is.EqualTo(
                    HomeDayNightController.DayWindowLightColor));
            Assert.That(
                home.Atmosphere.WindowLight.intensity,
                Is.EqualTo(
                        HomeDayNightController
                            .DayWindowLightIntensity)
                    .Within(0.001f));
            // A minute of daylight used to be a no-op because the sun
            // was one fixed pose all day. It moves now, so the schedule
            // re-applies; what this test is really about - that the
            // window light's own colour and intensity are flat across
            // the day - is asserted above and is unaffected.
            int stableDayApplicationCount =
                home.DayNight.VisualApplicationCount;
            Color stableWindowColor = home.Atmosphere.WindowLight.color;
            float stableWindowIntensity =
                home.Atmosphere.WindowLight.intensity;
            GameSessionState.AdvanceGameTime(1f);
            yield return null;
            Assert.That(
                home.DayNight.VisualApplicationCount,
                Is.EqualTo(stableDayApplicationCount + 1));
            Assert.That(
                home.Atmosphere.WindowLight.color,
                Is.EqualTo(stableWindowColor));
            Assert.That(
                home.Atmosphere.WindowLight.intensity,
                Is.EqualTo(stableWindowIntensity).Within(0.001f));
            AssertExteriorAtmosphere(home);
            AssertRenderedExteriorBarFacade(home);
            AssertRenderedExteriorDecorations(home.ExteriorView);
            AssertRenderedExteriorDistrictPointsOfInterest(
                home.ExteriorView,
                home.ExteriorContext);
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.HomeInterior));
            Assert.That(
                Object.FindAnyObjectByType<CityGameRoot>(),
                Is.Null);
            Assert.That(
                Object.FindAnyObjectByType<CityMusicPlayer>(),
                Is.Null);
            Assert.That(
                Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));

            Transform ceiling = AssertRequiredObject(
                home.Room,
                "Home Ceiling");
            Renderer ceilingRenderer =
                ceiling.GetComponent<Renderer>();
            Assert.That(ceilingRenderer, Is.Not.Null);
            Assert.That(
                ceilingRenderer.bounds.min.y,
                Is.LessThanOrEqualTo(
                    home.Layout.RoomHeight));
            Assert.That(
                ceilingRenderer.bounds.max.y,
                Is.GreaterThan(
                    home.Layout.RoomHeight));
            Assert.That(
                ceiling.GetComponent<Collider>(),
                Is.Null);

            Transform southReturn =
                AssertRequiredObject(
                    home.Room,
                    "Home South Exterior Return Wall");
            Transform northReturn =
                AssertRequiredObject(
                    home.Room,
                    "Home North Exterior Return Wall");
            AssertExteriorReturnSealsCorner(
                southReturn,
                home.Layout);
            AssertExteriorReturnSealsCorner(
                northReturn,
                home.Layout);

            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony Deck");
            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony Threshold");
            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony Outer Guard");
            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony South Guard");
            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony North Guard");

            Transform glass = AssertRequiredObject(
                home.Balcony,
                "Home Balcony Window Glass");
            Assert.That(
                glass.GetComponent<Collider>(),
                Is.Null);
            Assert.That(
                glass.GetComponent<Renderer>()
                    .sharedMaterial,
                Is.SameAs(
                    HomeBalconyResources
                        .GlassMaterial));
            Transform doorGlass = AssertRequiredObject(
                home.Balcony,
                "Home Balcony Ajar Door Pivot/" +
                "Home Balcony Door Glass");
            Assert.That(
                doorGlass.GetComponent<Collider>(),
                Is.Null);
            Assert.That(
                home.ExteriorView
                    .GetComponentsInChildren<Collider>(
                        true),
                Is.Empty);
            AssertExteriorStaysBeyondFacade(
                home.Room,
                home.ExteriorView);

            Physics.SyncTransforms();
            for (int sample = 0;
                 sample < 3;
                 sample++)
            {
                float x = 4.76f + sample * 0.24f;
                Assert.That(
                    Physics.CheckCapsule(
                        new Vector3(
                            x,
                            0.45f,
                            PlayerHomeBalconyGeometry
                                .DoorCenterZ),
                        new Vector3(
                            x,
                            1.45f,
                            PlayerHomeBalconyGeometry
                                .DoorCenterZ),
                        0.31f,
                        ~0,
                        QueryTriggerInteraction.Ignore),
                    Is.False,
                    "The apartment and balcony must share a capsule-clear doorway.");
            }

            Camera camera = Camera.main;
            Light homeSun = RenderSettings.sun;
            Assert.That(homeSun, Is.Not.Null);
            Quaternion homeSunRotation =
                homeSun.transform.rotation;
            Color homeSunColor = homeSun.color;
            float homeSunIntensity = homeSun.intensity;
            LightShadows homeSunShadows = homeSun.shadows;
            float homeSunShadowStrength =
                homeSun.shadowStrength;
            AmbientMode homeAmbientMode =
                RenderSettings.ambientMode;
            Color homeAmbientLight =
                RenderSettings.ambientLight;
            float homeReflectionIntensity =
                RenderSettings.reflectionIntensity;
            Rect balconyActivation =
                home.BalconyLayout
                    .BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(
                new Vector3(
                    balconyActivation.center.x,
                    home.Layout.PlayerSpawn.y,
                    balconyActivation.center.y));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    HomeCameraShotKind.Balcony));
            Assert.That(home.Music, Is.Not.Null);
            Assert.That(home.Music.IsBalconyActive, Is.True);
            Assert.That(
                home.ExteriorAtmosphere
                    .IsBalconyVisibilityActive,
                Is.True);
            Assert.That(
                home.ExteriorAtmosphere
                    .FogField.FogRenderer.enabled,
                Is.True);
            Assert.That(
                home.ExteriorAtmosphere
                    .CityPostProcessVolume.weight,
                Is.EqualTo(1f));
            AssertExteriorLightsActive(
                home.ExteriorAtmosphere,
                true);
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(
                RenderSettings.fogMode,
                Is.EqualTo(FogMode.ExponentialSquared));
            Assert.That(
                RenderSettings.fogDensity,
                Is.EqualTo(
                        RuntimeSceneSetup.CityFogDensity)
                    .Within(0.0001f));
            Assert.That(
                RenderSettings.fogColor,
                Is.EqualTo(
                    RuntimeSceneSetup.CityFogColor));
            Assert.That(
                camera.backgroundColor,
                Is.EqualTo(
                    RuntimeSceneSetup.CityFogColor));
            Assert.That(
                camera.farClipPlane,
                Is.EqualTo(
                        RuntimeSceneSetup.CityFarClipPlane)
                    .Within(0.001f));
            AssertCityLighting(
                homeSun,
                home.DayNight.CurrentSample);

            bool dayFog = RenderSettings.fog;
            FogMode dayFogMode = RenderSettings.fogMode;
            Color dayFogColor = RenderSettings.fogColor;
            float dayFogDensity = RenderSettings.fogDensity;
            Color dayBackgroundColor = camera.backgroundColor;
            float dayFarClipPlane = camera.farClipPlane;
            GameSessionState.AdvanceGameTime(720f);
            home.DayNight.RefreshImmediate();
            Assert.That(RenderSettings.fog, Is.EqualTo(dayFog));
            Assert.That(RenderSettings.fogMode, Is.EqualTo(dayFogMode));
            Assert.That(RenderSettings.fogColor, Is.EqualTo(dayFogColor));
            Assert.That(
                RenderSettings.fogDensity,
                Is.EqualTo(dayFogDensity));
            Assert.That(
                camera.backgroundColor,
                Is.EqualTo(dayBackgroundColor));
            Assert.That(
                camera.farClipPlane,
                Is.EqualTo(dayFarClipPlane));
            Assert.That(
                home.DayNight.CurrentSample.NightFactor,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                home.ExteriorNight.NightFactor,
                Is.EqualTo(1f).Within(0.0001f));
            AssertCityLighting(
                homeSun,
                home.DayNight.CurrentSample);
            home.ExteriorAtmosphere.enabled = false;
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(
                camera.backgroundColor,
                Is.EqualTo(
                    RuntimeSceneSetup.HomeBackgroundColor));
            Assert.That(
                camera.farClipPlane,
                Is.EqualTo(
                        RuntimeSceneSetup.DefaultFarClipPlane)
                    .Within(0.001f));
            Assert.That(
                home.ExteriorAtmosphere
                    .FogField.FogRenderer.enabled,
                Is.False);
            Assert.That(
                home.ExteriorAtmosphere
                    .CityPostProcessVolume.weight,
                Is.EqualTo(0f));
            AssertExteriorLightsActive(
                home.ExteriorAtmosphere,
                false);
            // One frame, so the home's own day/night can take the lights
            // back: switching the exterior off only stops it overriding
            // them, and who re-applies the home's is the controller, on
            // its next update.
            //
            // Then a nudge of the clock, so the home's controller has a
            // reason to apply. Disabling the exterior hands the lights
            // back through a snapshot it captured when the balcony opened,
            // and that snapshot is minutes stale by now - what proves the
            // handover is not its contents but that the HOME is driving
            // again afterwards. So: let it run, and check the sun is the
            // home's own current sample rather than the city's.
            yield return null;
            GameSessionState.AdvanceGameTime(20f);
            yield return null;
            yield return null;

            AssertHomeLightingRestored(
                homeSun,
                home.DayNight,
                homeSunShadows,
                homeSunShadowStrength,
                homeAmbientMode,
                homeReflectionIntensity);
            home.ExteriorAtmosphere.enabled = true;
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(
                home.ExteriorAtmosphere
                    .IsBalconyVisibilityActive,
                Is.True);
            Assert.That(
                home.ExteriorAtmosphere
                    .FogField.FogRenderer.enabled,
                Is.True);
            Assert.That(
                home.ExteriorAtmosphere
                    .CityPostProcessVolume.weight,
                Is.EqualTo(1f));
            AssertExteriorLightsActive(
                home.ExteriorAtmosphere,
                true);
            AssertCityLighting(
                homeSun,
                home.DayNight.CurrentSample);
            yield return AssertBalconyOcclusionPresentation(home);
            AssertPlayerVisible(
                camera,
                home.Player.GameObject.transform);
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.HomeInterior),
                "Walking onto the balcony must not load another scene.");

            home.Player.Motor.Teleport(
                new Vector3(
                    0.50f,
                    home.Layout.PlayerSpawn.y,
                    -0.50f));
            yield return null;
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    HomeCameraShotKind.MainRoom));
            Assert.That(home.Music.IsBalconyActive, Is.False);
            Assert.That(
                home.ExteriorAtmosphere
                    .IsBalconyVisibilityActive,
                Is.False);
            Assert.That(
                home.ExteriorAtmosphere
                    .FogField.FogRenderer.enabled,
                Is.False);
            Assert.That(
                home.ExteriorAtmosphere
                    .CityPostProcessVolume.weight,
                Is.EqualTo(0f));
            AssertExteriorLightsActive(
                home.ExteriorAtmosphere,
                false);
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(
                camera.backgroundColor,
                Is.EqualTo(
                    RuntimeSceneSetup.HomeBackgroundColor));
            Assert.That(
                camera.farClipPlane,
                Is.EqualTo(
                        RuntimeSceneSetup.DefaultFarClipPlane)
                    .Within(0.001f));
            // One frame, so the home's own day/night can take the lights
            // back: switching the exterior off only stops it overriding
            // them, and who re-applies the home's is the controller, on
            // its next update.
            //
            // With the clock STOPPED for that frame. The session's time
            // keeps running otherwise, and this sequence has already run
            // it into the evening, so the sun the controller applied on
            // one frame and the sample read on the next are minutes apart
            // and visibly different colours. Freezing the scale makes the
            // two the same instant; it is restored immediately after.
            yield return null;
            GameSessionState.AdvanceGameTime(20f);
            yield return null;
            yield return null;

            AssertHomeLightingRestored(
                homeSun,
                home.DayNight,
                homeSunShadows,
                homeSunShadowStrength,
                homeAmbientMode,
                homeReflectionIntensity);
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.HomeInterior));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HomeScene_SpawnsPedestriansOnlyOnBalcony()
        {
            AsyncOperation load =
                SceneManager.LoadSceneAsync(
                    SceneIds.HomeInterior,
                    LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            HomeInteriorRoot home = null;
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(home, Is.Not.Null);
            Assert.That(home.PedestrianPlan, Is.Not.Null);
            Assert.That(home.PedestrianPlan.Count, Is.GreaterThan(0));
            Assert.That(home.Pedestrians, Is.Not.Null);
            Assert.That(home.Pedestrians.IsInitialized, Is.True);
            // Home shares the whole catalog through the same factory, so the
            // balcony can show any registered design, not just the two the
            // City happened to spawn. How MANY it pools is the balcony
            // profile's own business, though - it holds more instances than
            // there are designs so the street below is not one of each, and
            // asserting the design count here read as a broken scene the
            // moment the profile gained its own pool size.
            Assert.That(
                home.Pedestrians.PoolCapacity,
                Is.EqualTo(home.Pedestrians.Profile.PoolSize));
            Assert.That(
                home.Pedestrians.PoolCapacity,
                Is.GreaterThanOrEqualTo(
                    CityPedestrianResources.Archetypes.Count),
                "Every registered design must fit in the pool at least " +
                "once, or the balcony can never show some of them.");
            Assert.That(home.Pedestrians.isActiveAndEnabled, Is.False);
            AssertPedestriansDormant(home.Pedestrians);

            Rect activation =
                home.BalconyLayout.BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(
                new Vector3(
                    activation.center.x,
                    home.Layout.PlayerSpawn.y,
                    activation.center.y));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Balcony));
            Assert.That(
                home.ExteriorAtmosphere.IsBalconyVisibilityActive,
                Is.True);
            Assert.That(home.Pedestrians.isActiveAndEnabled, Is.True);
            home.Pedestrians.Advance(
                home.Pedestrians.TimeUntilNextSpawn + 0.01f);
            Assert.That(
                home.Pedestrians.ActiveCount,
                Is.EqualTo(1),
                "The first randomized event must activate only one slot.");
            float minimumNextDelay = home.Pedestrians.IsNightSpawnMode
                ? CityPedestrianDirector.MinimumNightSpawnCooldown
                : CityPedestrianDirector.MinimumSpawnCooldown;
            float maximumNextDelay = home.Pedestrians.IsNightSpawnMode
                ? CityPedestrianDirector.MaximumNightSpawnCooldown
                : CityPedestrianDirector.MaximumSpawnCooldown;
            Assert.That(
                home.Pedestrians.TimeUntilNextSpawn,
                Is.InRange(
                    minimumNextDelay,
                    maximumNextDelay));

            var activeAnchorIds = new HashSet<string>();
            for (int index = 0;
                 index < home.Pedestrians.Actors.Count;
                 index++)
            {
                CityPedestrianActor actor =
                    home.Pedestrians.Actors[index];
                if (!actor.IsSpawned)
                {
                    continue;
                }

                Assert.That(actor.CollisionEnabled, Is.True);
                Assert.That(
                    activeAnchorIds.Add(actor.SpawnAnchorId),
                    Is.True,
                    "Balcony pedestrians must use unique spawn anchors.");
                float distance = Vector2.Distance(
                    new Vector2(
                        home.Player.GameObject.transform.position.x,
                        home.Player.GameObject.transform.position.z),
                    new Vector2(actor.Position.x, actor.Position.z));
                Assert.That(
                    distance,
                    Is.InRange(
                        CityPedestrianDirector.MinimumSpawnDistance,
                        CityPedestrianDirector.MaximumSpawnDistance),
                    "Balcony pedestrians must spawn in the fog-hidden " +
                    "player-relative band.");
            }

            home.Player.Motor.Teleport(
                new Vector3(
                    0.50f,
                    home.Layout.PlayerSpawn.y,
                    -0.50f));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(home.Pedestrians.isActiveAndEnabled, Is.False);
            AssertPedestriansDormant(home.Pedestrians);
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertPedestriansDormant(
            CityPedestrianDirector pedestrians)
        {
            Assert.That(pedestrians.ActiveCount, Is.Zero);
            for (int index = 0;
                 index < pedestrians.Actors.Count;
                 index++)
            {
                CityPedestrianActor actor = pedestrians.Actors[index];
                Assert.That(actor.IsSpawned, Is.False);
                Assert.That(
                    actor.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Dormant));
                Assert.That(actor.SpawnAnchorId, Is.Empty);
                Assert.That(actor.CollisionEnabled, Is.False);
            }
        }

        private static void AssertExteriorLightsActive(
            HomeBalconyExteriorAtmosphere atmosphere,
            bool expectedActive)
        {
            CityNightAtmosphere lighting =
                atmosphere.ExteriorLighting;
            Assert.That(lighting, Is.Not.Null);
            Assert.That(lighting.enabled, Is.EqualTo(expectedActive));
            AssertLightObjectsActive(
                lighting.BarLights,
                expectedActive);
            AssertLightObjectsActive(
                lighting.StreetLightPool,
                expectedActive);
        }

        private static void AssertLightObjectsActive(
            System.Collections.Generic.IReadOnlyList<Light> lights,
            bool expectedActive)
        {
            for (int index = 0;
                 index < lights.Count;
                 index++)
            {
                Assert.That(lights[index], Is.Not.Null);
                Assert.That(
                    lights[index].gameObject.activeSelf,
                    Is.EqualTo(expectedActive));
            }
        }

        private static void AssertCityLighting(
            Light expectedSun,
            DayNightVisualSample expectedSample)
        {
            Assert.That(RenderSettings.sun, Is.SameAs(expectedSun));
            Assert.That(
                expectedSun.color,
                Is.EqualTo(
                    expectedSample.DirectionalLightColor));
            Assert.That(
                expectedSun.intensity,
                Is.EqualTo(
                        expectedSample.DirectionalLightIntensity)
                    .Within(0.001f));
            Assert.That(
                Quaternion.Angle(
                    expectedSun.transform.rotation,
                    expectedSample.DirectionalLightRotation),
                Is.LessThan(0.001f));
            Assert.That(
                expectedSun.shadows,
                Is.EqualTo(LightShadows.Hard));
            Assert.That(
                expectedSun.shadowStrength,
                Is.EqualTo(
                        expectedSample.ShadowStrength)
                    .Within(0.001f));
            Assert.That(
                RenderSettings.ambientMode,
                Is.EqualTo(AmbientMode.Flat));
            Assert.That(
                RenderSettings.ambientLight,
                Is.EqualTo(expectedSample.AmbientLightColor));
            Assert.That(
                RenderSettings.reflectionIntensity,
                Is.EqualTo(expectedSample.ReflectionIntensity)
                    .Within(0.001f));
        }

        /// <summary>
        /// The home has stopped being lit by the city outside its window.
        ///
        /// Half of this is a snapshot comparison and half of it cannot be.
        /// Shadow settings, the ambient MODE and the reflection intensity
        /// are the home's own dressing and must come back exactly as they
        /// were. The sun's colour, intensity, angle and the ambient tint
        /// are driven by the clock, and the clock keeps moving while the
        /// balcony is open - this sequence deliberately runs it into the
        /// night - so pinning them to a snapshot taken beforehand was
        /// asserting that time had stopped. They are checked against the
        /// home's own CURRENT sample instead, which is what "restored"
        /// has to mean once the sun has moved.
        /// </summary>
        private static void AssertHomeLightingRestored(
            Light expectedSun,
            HomeDayNightController dayNight,
            LightShadows expectedShadows,
            float expectedShadowStrength,
            AmbientMode expectedAmbientMode,
            float expectedReflectionIntensity)
        {
            // Indoors the sun is not the raw day/night sample: the interior
            // mood deliberately tints it between its own night and day
            // colours by how much daylight is coming through the window.
            // Comparing it to the sample was comparing the flat with the
            // street, and comparing it to a snapshot taken before the
            // balcony opened was comparing it with a different hour. The
            // expectation is computed the way the controller computes it.
            float windowDay = dayNight.WindowDayFactor;
            Color expectedSunColor = Color.Lerp(
                HomeDayNightController.NightSunColor,
                HomeDayNightController.DaySunColor,
                windowDay);
            float expectedSunIntensity = Mathf.Lerp(
                HomeDayNightController.NightSunIntensity,
                HomeDayNightController.DaySunIntensity,
                windowDay);
            Color expectedAmbient = Color.Lerp(
                HomeDayNightController.NightAmbientColor,
                HomeDayNightController.DayAmbientColor,
                windowDay);

            Assert.That(RenderSettings.sun, Is.SameAs(expectedSun));
            Assert.That(
                expectedSun.color,
                Is.EqualTo(expectedSunColor));
            Assert.That(
                expectedSun.intensity,
                Is.EqualTo(expectedSunIntensity).Within(0.001f));
            Assert.That(
                expectedSun.shadows,
                Is.EqualTo(expectedShadows));
            Assert.That(
                expectedSun.shadowStrength,
                Is.EqualTo(expectedShadowStrength).Within(0.001f));
            Assert.That(
                RenderSettings.ambientMode,
                Is.EqualTo(expectedAmbientMode));
            Assert.That(
                RenderSettings.ambientLight,
                Is.EqualTo(expectedAmbient));
            Assert.That(
                RenderSettings.reflectionIntensity,
                Is.EqualTo(expectedReflectionIntensity).Within(0.001f));
        }

        private static Transform AssertRequiredObject(
            Transform parent,
            string path)
        {
            Transform result = parent.Find(path);
            Assert.That(
                result,
                Is.Not.Null,
                $"Missing generated object '{path}'.");
            return result;
        }

        private static void AssertExteriorAtmosphere(
            HomeInteriorRoot home)
        {
            HomeBalconyExteriorAtmosphere atmosphere =
                home.ExteriorAtmosphere;
            Assert.That(atmosphere, Is.Not.Null);
            Assert.That(atmosphere.IsInitialized, Is.True);
            Assert.That(
                atmosphere.IsBalconyVisibilityActive,
                Is.False);
            Assert.That(
                atmosphere.ExteriorRoot,
                Is.SameAs(home.ExteriorView));
            Assert.That(atmosphere.FogAnchor, Is.Not.Null);
            Assert.That(
                atmosphere.FogAnchor.localPosition.x,
                Is.EqualTo(
                        HomeExteriorViewBuilder.ExteriorMinimumX +
                        HomeBalconyExteriorAtmosphere.FogAnchorDepth)
                    .Within(0.001f));
            Assert.That(
                atmosphere.FogAnchor.localPosition.x,
                Is.GreaterThan(
                    HomeExteriorViewBuilder.ExteriorMinimumX));

            CityFogField fog = atmosphere.FogField;
            Assert.That(fog, Is.Not.Null);
            Assert.That(fog.IsInitialized, Is.True);
            Assert.That(
                fog.Player,
                Is.SameAs(atmosphere.FogAnchor));
            Assert.That(
                fog.Particles.main.maxParticles,
                Is.EqualTo(CityFogField.MaximumParticles));
            Assert.That(
                fog.FogRenderer.sharedMaterial,
                Is.SameAs(
                    CityNightResources.AtmosphereMaterial));
            Assert.That(fog.FogRenderer.enabled, Is.False);
            Assert.That(
                atmosphere.CityPostProcessVolume,
                Is.Not.Null);
            Assert.That(
                atmosphere.CityPostProcessVolume.isGlobal,
                Is.True);
            Assert.That(
                atmosphere.CityPostProcessVolume.priority,
                Is.GreaterThan(
                    home.Atmosphere.PostProcessVolume.priority));
            Assert.That(
                atmosphere.CityPostProcessVolume.weight,
                Is.EqualTo(0f));
            Assert.That(
                atmosphere.RuntimeCityProfile,
                Is.Not.Null);
            Assert.That(atmosphere.ExteriorLighting, Is.Not.Null);
            Assert.That(
                atmosphere.ExteriorLighting.RealtimeLightCount,
                Is.GreaterThan(0));
            Assert.That(
                atmosphere.ExteriorLighting.RealtimeLightCount,
                Is.LessThanOrEqualTo(
                    CityNightAtmosphere.MaximumRealtimeLights));
            AssertExteriorLightsActive(atmosphere, false);
            Assert.That(
                atmosphere.RuntimeCityProfile.TryGet(
                    out Bloom bloom),
                Is.True);
            Assert.That(bloom.intensity.value, Is.EqualTo(0.62f));
            Assert.That(bloom.threshold.value, Is.EqualTo(0.60f));
            Assert.That(
                atmosphere.RuntimeCityProfile.TryGet(
                    out DepthOfField balconyDepthOfField),
                Is.True,
                "The balcony's city mirror must inherit the far-blur " +
                "band from the shared city noir profile.");
            Assert.That(
                balconyDepthOfField.mode.value,
                Is.EqualTo(DepthOfFieldMode.Gaussian));
            Assert.That(
                atmosphere.RuntimeCityProfile.TryGet(
                    out ColorAdjustments color),
                Is.True);
            Assert.That(
                color.postExposure.value,
                Is.EqualTo(0.62f));
            Assert.That(
                color.colorFilter.value,
                Is.EqualTo(new Color(0.94f, 1f, 0.97f, 1f)));
            Assert.That(
                fog.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                fog.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                fog.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(
                Camera.main.backgroundColor,
                Is.EqualTo(
                    RuntimeSceneSetup.HomeBackgroundColor));
            Assert.That(
                Camera.main.farClipPlane,
                Is.EqualTo(
                        RuntimeSceneSetup.DefaultFarClipPlane)
                    .Within(0.001f));
            AssertUsesCityLitMaterial(
                AssertRequiredObject(
                    home.ExteriorView,
                    "Home Exterior Ground"));
            AssertUsesCityLitMaterial(
                AssertRequiredObject(
                    home.ExteriorView,
                    "Home Exterior Street Surfaces"));
            AssertUsesCityLitMaterial(
                AssertRequiredObject(
                    home.ExteriorView,
                    "Home Exterior Sidewalk Surfaces"));
        }

        private static void AssertRenderedExteriorBarFacade(
            HomeInteriorRoot home)
        {
            BuildingLot barLot = null;
            for (int index = 0;
                 index < home.ExteriorContext.NearbyLots.Count;
                 index++)
            {
                BuildingLot candidate =
                    home.ExteriorContext.NearbyLots[index];
                if (candidate.IsBar)
                {
                    barLot = candidate;
                    break;
                }
            }

            Assert.That(
                barLot,
                Is.Not.Null,
                "The canonical Home street must retain its neighboring bar.");
            Transform bar = home.ExteriorView.Find(
                "Home Exterior Building Silhouettes/" +
                $"Exterior Bar {barLot.BarId}");
            Assert.That(bar, Is.Not.Null);
            Transform foundation = AssertRequiredObject(
                bar,
                "Blender Special Building/" +
                "Exterior Special Building Foundation");
            AssertUsesCityLitMaterial(
                AssertRequiredObject(
                    bar,
                    "Pub Brick Shell"));
            AssertUsesCityLitMaterial(
                AssertRequiredObject(
                    bar,
                    "Pub Rendered Upper Storey"));
            AssertUsesCityLitMaterial(
                AssertRequiredObject(
                    bar,
                    "Pub Slate Roof"));
            Assert.That(
                bar.Find(
                    "Blender Special Building/Exterior Building Mass"),
                Is.Null,
                "The Home view must not retain the old CityMisc pub shell.");
            Assert.That(
                bar.Find("Blender Special Building/Exterior Roof"),
                Is.Null);
            Assert.That(bar.Find("Front Windows"), Is.Null);

            Assert.That(bar.Find("Pub Ground Floor Glass"), Is.Not.Null);
            Assert.That(bar.Find("Pub Upper Sash Frames"), Is.Not.Null);
            Assert.That(
                bar.Find("Bar Entrance Flanking Panels"),
                Is.Not.Null,
                "The pub entrance must close both gaps beside its bays.");
            Assert.That(
                bar.Find("Bar Outer Bay Flanking Panels"),
                Is.Not.Null,
                "The pub frontage must close both outer bay gaps.");
            Assert.That(
                bar.Find("Bar Entrance Reveal Panels"),
                Is.Not.Null,
                "The recessed pub door must keep its side reveals in Home.");
            Assert.That(bar.Find("Bar Door"), Is.Not.Null);
            Assert.That(bar.Find("Bar Door Frame Left"), Is.Not.Null);
            Assert.That(bar.Find("Bar Door Frame Right"), Is.Not.Null);
            Assert.That(bar.Find("Bar Door Header"), Is.Not.Null);
            Assert.That(bar.Find("Bar Entrance Canopy"), Is.Not.Null);
            Assert.That(
                bar.Find("Bar Entrance Canopy Inset"),
                Is.Not.Null);
            Assert.That(bar.Find("Bar Sign Bracket"), Is.Not.Null);
            AssertUsesCityLitMaterial(
                AssertRequiredObject(bar, "Bar Door"));
            AssertUsesCityLitMaterial(
                AssertRequiredObject(
                    bar,
                    "Bar Entrance Canopy"));

            GameObject exteriorPrefab =
                BarModelResources.LoadFacadePrefab();
            Assert.That(exteriorPrefab, Is.Not.Null);
            BarAssetRegistry exteriorRegistry =
                exteriorPrefab.GetComponent<BarAssetRegistry>();
            Assert.That(exteriorRegistry, Is.Not.Null);
            Assert.That(
                exteriorRegistry.DesignId,
                Is.EqualTo("bar_exterior_v2"));
            Assert.That(
                exteriorPrefab.transform.localScale,
                Is.EqualTo(Vector3.one));
            Assert.That(
                exteriorRegistry.Dimensions.Width,
                Is.EqualTo(12.2645f).Within(0.0001f));
            Assert.That(
                exteriorRegistry.Dimensions.Depth,
                Is.EqualTo(13.5237f).Within(0.0001f));
            Assert.That(
                exteriorRegistry.Dimensions.Height,
                Is.EqualTo(9.3435f).Within(0.0001f));

            Vector3 frontage =
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    home.ExteriorContext.PlayerHome,
                    new Vector3(
                        barLot.FrontageDirection.x,
                        0f,
                        barLot.FrontageDirection.y));
            Vector3 doorAnchor =
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    home.ExteriorContext.PlayerHome,
                    barLot.DoorPosition);
            Renderer doorRenderer =
                bar.Find("Bar Door").GetComponent<Renderer>();
            Assert.That(doorRenderer, Is.Not.Null);
            Vector3 expectedDoorCentre = bar.TransformPoint(
                doorAnchor -
                (frontage * 0.16f) +
                (Vector3.up * 1.17f));
            Assert.That(
                Vector3.Distance(
                    doorRenderer.bounds.center,
                    expectedDoorCentre),
                Is.LessThan(0.02f),
                "The Home pub moved away from its authored door anchor.");

            Renderer canopyRenderer = bar
                .Find("Bar Entrance Canopy")
                .GetComponent<Renderer>();
            Vector3 frontageOffset = canopyRenderer.bounds.center -
                bar.TransformPoint(doorAnchor);
            frontageOffset.y = 0f;
            Vector3 worldFrontage = bar.TransformDirection(frontage);
            worldFrontage.y = 0f;
            worldFrontage.Normalize();
            Assert.That(
                Vector3.Dot(frontageOffset.normalized, worldFrontage),
                Is.GreaterThan(0.999f),
                "The Home pub facade must face the street below.");

            Transform markerTransform =
                bar.Find("Bar Landmark Marker");
            Assert.That(markerTransform, Is.Not.Null);
            BarBuildingMarker marker =
                markerTransform.GetComponent<
                    BarBuildingMarker>();
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.BarId, Is.EqualTo(barLot.BarId));
            Assert.That(
                marker.PanelRenderer,
                Is.Not.Null,
                "The reconstructed bar keeps its hanging sign geometry.");
            Assert.That(marker.SignRenderers, Is.Not.Empty);
            Assert.That(
                bar.GetComponentsInChildren<BarEntrance>(true),
                Is.Empty);
            Assert.That(
                bar.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(foundation.GetComponent<Collider>(), Is.Null);
            Assert.That(
                bar.Find(
                    CityBuildingPrototypeWorldBuilder
                        .LogicalCollisionObjectName),
                Is.Null,
                "Home must not import City's logical pub collider.");
            Assert.That(
                bar.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                bar.GetComponentsInChildren<Camera>(true),
                Is.Empty);
        }

        private static IEnumerator AssertBalconyOcclusionPresentation(
            HomeInteriorRoot home)
        {
            Assert.That(home.OcclusionRegistry, Is.Not.Null);
            Assert.That(home.PlayerOcclusion, Is.Not.Null);
            Assert.That(home.PlayerOcclusion.IsInitialized, Is.True);
            string[] railGroupIds =
            {
                "home.balcony.rail.outer",
                "home.balcony.rail.south",
                "home.balcony.rail.north"
            };
            for (int index = 0;
                 index < railGroupIds.Length;
                 index++)
            {
                Assert.That(
                    home.OcclusionRegistry.TryGetGroup(
                        railGroupIds[index],
                        out HomeOccluderGroup group),
                    Is.True);
                Assert.That(
                    group.Kind,
                    Is.EqualTo(HomeOccluderKind.VisualRail));
                Assert.That(group.Renderers, Is.Not.Empty);
                for (int rendererIndex = 0;
                     rendererIndex < group.Renderers.Count;
                     rendererIndex++)
                {
                    Renderer renderer = group.Renderers[rendererIndex];
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(
                            HomeOcclusionResources.DitherMaterial));
                    Assert.That(
                        renderer.GetComponent<Collider>(),
                        Is.Null,
                        "Visible balcony rails must remain separate from safety colliders.");
                }

            }

            Assert.That(
                home.OcclusionRegistry.TryGetGroup(
                    "home.balcony.ajar-door",
                    out HomeOccluderGroup doorGroup),
                Is.True);
            Assert.That(
                doorGroup.Kind,
                Is.EqualTo(HomeOccluderKind.StructuralCutaway));
            Assert.That(doorGroup.Renderers, Is.Not.Empty);
            for (int index = 0;
                 index < doorGroup.Renderers.Count;
                 index++)
            {
                Renderer renderer = doorGroup.Renderers[index];
                Assert.That(
                    renderer.name,
                    Does.Not.Contain("Glass"));
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(HomeOcclusionResources.DitherMaterial));
            }

            Rect activation =
                home.BalconyLayout.BalconyCameraActivationBounds;
            Vector2[] samples =
            {
                new Vector2(
                    activation.xMin + 0.20f,
                    PlayerHomeBalconyGeometry.DoorCenterZ),
                new Vector2(
                    activation.xMin + 0.50f,
                    PlayerHomeBalconyGeometry.DoorCenterZ),
                new Vector2(
                    activation.xMin + 0.80f,
                    PlayerHomeBalconyGeometry.DoorCenterZ),
                new Vector2(
                    activation.xMin + 1.10f,
                    PlayerHomeBalconyGeometry.DoorCenterZ)
            };
            bool fadedDoorFound = false;
            for (int sampleIndex = 0;
                 sampleIndex < samples.Length &&
                 !fadedDoorFound;
                 sampleIndex++)
            {
                home.Player.Motor.Teleport(
                    new Vector3(
                        samples[sampleIndex].x,
                        home.Layout.PlayerSpawn.y,
                        samples[sampleIndex].y));
                yield return null;
                home.PlayerOcclusion.RefreshImmediate();
                fadedDoorFound =
                    home.PlayerOcclusion.GetVisibility(
                        doorGroup) < 0.999f;
            }

            Assert.That(
                fadedDoorFound,
                Is.True,
                "The balcony shot must reveal the player through the open door leaf near the doorway.");

            home.Player.Motor.Teleport(
                new Vector3(
                    activation.center.x,
                    home.Layout.PlayerSpawn.y,
                    activation.center.y));
            yield return null;
        }

        private static void AssertPhysicalSurface(
            Transform parent,
            string path)
        {
            Collider collider =
                AssertRequiredObject(parent, path)
                    .GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.enabled, Is.True);
        }

        private static void AssertExteriorReturnSealsCorner(
            Transform returnWall,
            HomeInteriorLayoutPlan layout)
        {
            Renderer renderer =
                returnWall.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(
                renderer.bounds.min.x,
                Is.LessThanOrEqualTo(
                    PlayerHomeBalconyGeometry
                        .HomeFacadeX +
                    0.001f));
            Assert.That(
                renderer.bounds.max.x,
                Is.GreaterThanOrEqualTo(
                    PlayerHomeBalconyGeometry
                        .HomeFacadeX +
                    PlayerHomeBalconyGeometry
                        .BalconyDepth +
                    0.50f));
            Assert.That(
                renderer.bounds.max.y,
                Is.EqualTo(layout.RoomHeight)
                    .Within(0.001f));
            Assert.That(
                returnWall.GetComponent<Collider>(),
                Is.Null);
        }

        private static void AssertExteriorStaysBeyondFacade(
            Transform room,
            Transform exterior)
        {
            float localMinimum =
                PlayerHomeBalconyGeometry.HomeFacadeX +
                PlayerHomeBalconyGeometry.WallThickness *
                0.5f +
                0.01f;
            float worldMinimum =
                room.TransformPoint(
                    new Vector3(
                        localMinimum,
                        0f,
                        0f)).x;
            Renderer[] renderers =
                exterior.GetComponentsInChildren<Renderer>(
                    true);
            Assert.That(renderers, Is.Not.Empty);
            bool foundGround = false;
            bool foundStreet = false;
            bool foundSidewalk = false;
            bool foundBuilding = false;
            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                Renderer renderer = renderers[index];
                if (!renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Assert.That(
                    renderer.bounds.min.x,
                    Is.GreaterThanOrEqualTo(
                        worldMinimum - 0.02f),
                    $"'{renderer.name}' must stay outside the apartment facade.");
                foundGround |=
                    renderer.name ==
                    "Home Exterior Ground";
                foundStreet |=
                    renderer.name ==
                    "Home Exterior Street Surfaces";
                foundSidewalk |=
                    renderer.name ==
                    "Home Exterior Sidewalk Surfaces";
                foundBuilding |=
                    renderer.name ==
                    "Exterior Building Mass";
            }

            Assert.That(foundGround, Is.True);
            Assert.That(foundStreet, Is.True);
            Assert.That(foundSidewalk, Is.True);
            Assert.That(
                foundBuilding,
                Is.True,
                "Half-space clipping must preserve the street view outside.");
        }

        private static void AssertPlayerVisible(
            Camera camera,
            Transform player)
        {
            Assert.That(camera, Is.Not.Null);
            Vector3 viewport =
                camera.WorldToViewportPoint(
                    player.position +
                    Vector3.up * 0.85f);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(
                viewport.x,
                Is.InRange(0.04f, 0.96f));
            Assert.That(
                viewport.y,
                Is.InRange(0.04f, 0.96f));
        }

        private static void AssertRenderedExteriorDecorations(
            Transform exterior)
        {
            Transform decorationRoot = null;
            for (int index = 0; index < exterior.childCount; index++)
            {
                Transform child = exterior.GetChild(index);
                if (string.Equals(
                        child.name,
                        "Home Exterior City Details",
                        System.StringComparison.Ordinal))
                {
                    decorationRoot = child;
                    break;
                }
            }

            Assert.That(
                decorationRoot,
                Is.Not.Null,
                "The reconstructed exterior must own its decoration child.");
            Assert.That(
                decorationRoot.GetComponentsInChildren<Renderer>(true),
                Is.Not.Empty);
            Assert.That(
                decorationRoot.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                decorationRoot.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                decorationRoot.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                decorationRoot.GetComponentsInChildren<ParticleSystem>(true),
                Is.Empty);
        }

        private static void
            AssertRenderedExteriorDistrictPointsOfInterest(
                Transform exterior,
                HomeExteriorContextPlan context)
        {
            Transform pointOfInterestRoot = null;
            for (int index = 0; index < exterior.childCount; index++)
            {
                Transform child = exterior.GetChild(index);
                if (child.name ==
                    CityDistrictPointOfInterestWorldBuilder
                        .HomeExteriorRootName)
                {
                    pointOfInterestRoot = child;
                    break;
                }
            }

            Assert.That(pointOfInterestRoot, Is.Not.Null);
            int expectedVisibleCount = 0;
            for (int index = 0;
                 index <
                 context.NearbyDistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor descriptor =
                    context.NearbyDistrictPointsOfInterest[index];
                Rect bounds =
                    PlayerHomeBalconyGeometry.ToHomeLocalRect(
                        context.PlayerHome,
                        descriptor.PublicBounds);
                if (bounds.xMin <
                    HomeExteriorViewBuilder.ExteriorMinimumX)
                {
                    continue;
                }

                expectedVisibleCount++;
                Transform site = pointOfInterestRoot.Find(
                    CityDistrictPointOfInterestWorldBuilder
                        .GetSiteName(descriptor.Id));
                Assert.That(site, Is.Not.Null);
                AssertUsesCityLitMaterial(
                    AssertRequiredObject(
                        site,
                        CityDistrictPointOfInterestWorldBuilder
                            .PublicGroundName));
            }

            Assert.That(
                pointOfInterestRoot.childCount,
                Is.EqualTo(expectedVisibleCount));
            Assert.That(
                pointOfInterestRoot
                    .GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                pointOfInterestRoot
                    .GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                pointOfInterestRoot
                    .GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                pointOfInterestRoot
                    .GetComponentsInChildren<ParticleSystem>(true),
                Is.Empty);
        }

        private static void AssertUsesCityLitMaterial(
            Transform transform)
        {
            Renderer renderer = transform.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(
                renderer.sharedMaterial,
                Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial),
                $"'{transform.name}' must use the same lit material as City.");
        }
    }
}
