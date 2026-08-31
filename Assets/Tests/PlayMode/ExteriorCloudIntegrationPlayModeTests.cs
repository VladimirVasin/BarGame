using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// One focused, scene-loading proof for the shared cloud owner. It runs
    /// alone because it composes four gameplay worlds in sequence. Pure
    /// motion and rendering contracts stay in EditMode.
    /// </summary>
    public sealed class ExteriorCloudIntegrationPlayModeTests
    {
        private const float TimeoutSeconds = 60f;
        private const float FarPlaneTolerance = 0.01f;
        private const int CaptureWidth = 960;
        private const int CaptureHeight = 540;
        private const string CaptureEnvironmentVariable =
            "BAR_PROMENADE_CAPTURE_CLOUDS";
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            GameSessionState.BeginNewGame();
            GameSessionState.ClearRoute();
        }

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.ClearRoute();
            Time.timeScale = previousTimeScale;
        }

        [UnityTest]
        public IEnumerator
            OutdoorScenesShareOneFieldAndHomeGatesItToTheBalcony()
        {
            CityGameRoot city = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                root => city = root);
            yield return null;

            Assert.That(city.IsInitialized, Is.True);
            AssertOutdoorField(
                city.Clouds,
                ExteriorCloudProfileKind.City,
                RuntimeSceneSetup.CityFarClipPlane);
            CaptureSky("City", city.Clouds);
            ExteriorCloudMotionSample cityPhase = city.Clouds.Phase;
            ExteriorCloudField previousField = city.Clouds;

            MountainRoadRoot road = null;
            yield return LoadSceneAndWaitForRoot<MountainRoadRoot>(
                SceneIds.MountainRoad,
                root => road = root);
            yield return null;

            Assert.That(previousField == null, Is.True,
                "Single-loading MountainRoad left City's cloud field alive.");
            Assert.That(road.IsInitialized, Is.True);
            AssertOutdoorField(
                road.Clouds,
                ExteriorCloudProfileKind.MountainRoad,
                RuntimeSceneSetup.MountainRoadFarClipPlane);
            MountainRoadRoutePlan route = road.Plan.Route;
            MountainRoadBridgeDescriptor bridge = road.Plan.Bridge;
            MountainRoadRouteSample roadEye = route.Sample(
                bridge.StartDistance - 14f);
            MountainRoadRouteSample roadTarget = route.Sample(
                bridge.EndDistance - 5f);
            CaptureSky(
                "MountainRoad",
                road.Clouds,
                roadEye.Position - roadEye.Forward * 2.2f -
                    roadEye.Right * 0.35f + Vector3.up * 1.6f,
                roadTarget.Position + Vector3.up * 6.5f);
            previousField = road.Clouds;

            AlpineVillageRoot village = null;
            yield return LoadSceneAndWaitForRoot<AlpineVillageRoot>(
                SceneIds.AlpineVillage,
                root => village = root);
            yield return null;

            Assert.That(previousField == null, Is.True,
                "Single-loading AlpineVillage left the road cloud alive.");
            Assert.That(village.IsInitialized, Is.True);
            AssertOutdoorField(
                village.Clouds,
                ExteriorCloudProfileKind.AlpineVillage,
                RuntimeSceneSetup.AlpineVillageFarClipPlane);
            CaptureSky("AlpineVillage", village.Clouds);
            previousField = village.Clouds;

            GameSessionState.EnterHome();
            GameSessionState.ClearRoute();
            HomeInteriorRoot home = null;
            yield return LoadSceneAndWaitForRoot<HomeInteriorRoot>(
                SceneIds.HomeInterior,
                root => home = root);
            yield return null;

            Assert.That(previousField == null, Is.True,
                "Single-loading Home left the village cloud field alive.");
            Assert.That(home.IsInitialized, Is.True);
            Assert.That(home.ExteriorAtmosphere, Is.Not.Null);
            ExteriorCloudField homeClouds =
                home.ExteriorAtmosphere.Clouds;
            Assert.That(homeClouds, Is.Not.Null);
            AssertOnlyField(homeClouds);
            Assert.That(
                homeClouds.Profile.Kind,
                Is.EqualTo(ExteriorCloudProfileKind.City));
            Assert.That(homeClouds.IsVisible, Is.False);
            Assert.That(homeClouds.Renderer.enabled, Is.False);
            AssertPhasesEqual(cityPhase, homeClouds.Phase);

            Rect balconyActivation =
                home.BalconyLayout.BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(
                new Vector3(
                    balconyActivation.center.x,
                    home.Layout.PlayerSpawn.y,
                    balconyActivation.center.y));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Balcony));
            Assert.That(
                home.ExteriorAtmosphere.IsBalconyVisibilityActive,
                Is.True);
            AssertOutdoorField(
                homeClouds,
                ExteriorCloudProfileKind.City,
                RuntimeSceneSetup.CityFarClipPlane);
            AssertPhasesEqual(cityPhase, homeClouds.Phase);
            CaptureSky("HomeBalcony", homeClouds);

            home.Player.Motor.Teleport(
                new Vector3(
                    0.5f,
                    home.Layout.PlayerSpawn.y,
                    -0.5f));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(
                home.ExteriorAtmosphere.IsBalconyVisibilityActive,
                Is.False);
            Assert.That(homeClouds.IsVisible, Is.False);
            Assert.That(homeClouds.Renderer.enabled, Is.False);
            AssertOnlyField(homeClouds);
        }

        private static void AssertOutdoorField(
            ExteriorCloudField field,
            ExteriorCloudProfileKind expectedKind,
            float expectedFarPlane)
        {
            Assert.That(field, Is.Not.Null);
            Assert.That(field.IsInitialized, Is.True);
            Assert.That(field.Profile.Kind, Is.EqualTo(expectedKind));
            Assert.That(field.Renderer, Is.Not.Null);
            Assert.That(field.IsVisible, Is.True);
            Assert.That(field.Renderer.enabled, Is.True);
            AssertPhaseInUnitRange(field.Phase);
            AssertOnlyField(field);

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(field.PrimaryCamera, Is.SameAs(camera));
            Assert.That(
                camera.clearFlags,
                Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(
                camera.farClipPlane,
                Is.EqualTo(expectedFarPlane).Within(FarPlaneTolerance));
            Assert.That(
                expectedFarPlane - field.Profile.ShellRadius,
                Is.InRange(0.5f, 2f));
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(
                camera.backgroundColor,
                Is.EqualTo(RenderSettings.fogColor),
                "The cloud horizon and empty pixels must meet the same " +
                "terminal haze colour.");
            Assert.That(
                field.HazeColor,
                Is.EqualTo(RenderSettings.fogColor),
                "The cloud field did not receive the visibility writer's " +
                "current haze colour.");
            Assert.That(
                Vector3.Distance(
                    field.transform.position,
                    camera.transform.position),
                Is.LessThan(0.001f),
                "The finite cloud shell stopped following the active " +
                "camera and would expose its physical radius.");
        }

        private static void AssertOnlyField(ExteriorCloudField expected)
        {
            ExteriorCloudField[] fields =
                Object.FindObjectsByType<ExteriorCloudField>(
                    FindObjectsInactive.Exclude);
            Assert.That(fields, Has.Length.EqualTo(1));
            Assert.That(fields[0], Is.SameAs(expected));
        }

        private static void CaptureSky(
            string areaName,
            ExteriorCloudField field,
            Vector3? capturePosition = null,
            Vector3? captureTarget = null)
        {
            if (!string.Equals(
                    System.Environment.GetEnvironmentVariable(
                        CaptureEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            Camera camera = field.PrimaryCamera;
            Assert.That(camera, Is.Not.Null);

            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(
                CaptureWidth,
                CaptureHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var frame = new Texture2D(
                CaptureWidth,
                CaptureHeight,
                TextureFormat.RGB24,
                false);

            try
            {
                if (capturePosition.HasValue && captureTarget.HasValue)
                {
                    camera.transform.SetPositionAndRotation(
                        capturePosition.Value,
                        Quaternion.LookRotation(
                            captureTarget.Value - capturePosition.Value,
                            Vector3.up));
                }
                else
                {
                    camera.transform.rotation =
                        originalRotation * Quaternion.Euler(-18f, 0f, 0f);
                }
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(
                    new Rect(0f, 0f, CaptureWidth, CaptureHeight),
                    0,
                    0);
                frame.Apply(false, false);

                string directory = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    "Captures",
                    "ExteriorClouds"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(
                    Path.Combine(directory, areaName + ".png"),
                    frame.EncodeToPNG());
            }
            finally
            {
                camera.transform.SetPositionAndRotation(
                    originalPosition,
                    originalRotation);
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(frame);
            }
        }

        private static void AssertPhasesEqual(
            ExteriorCloudMotionSample expected,
            ExteriorCloudMotionSample actual)
        {
            Assert.That(actual.BroadPhase, Is.EqualTo(expected.BroadPhase));
            Assert.That(actual.DetailPhase, Is.EqualTo(expected.DetailPhase));
        }

        private static void AssertPhaseInUnitRange(
            ExteriorCloudMotionSample sample)
        {
            AssertUnitPhase(sample.BroadPhase);
            AssertUnitPhase(sample.DetailPhase);
        }

        private static void AssertUnitPhase(Vector2 phase)
        {
            Assert.That(
                phase.x,
                Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            Assert.That(
                phase.y,
                Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            Action<T> capture)
            where T : Component
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                operation.isDone,
                Is.True,
                $"Scene '{sceneName}' did not load.");
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                T root = Object.FindAnyObjectByType<T>();
                if (root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{sceneName}' did not create {typeof(T).Name}.");
        }
    }
}
