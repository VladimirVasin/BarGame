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
    public sealed class TechnicalLifecyclePlayModeTests
    {
        private const float DeadlineSeconds = 60f;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            RuntimePerformanceCapture.StopCapture();
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (SceneTransitionService.IsTransitioning &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            Scene blank = SceneManager.CreateScene("Technical Lifecycle Cleanup");
            SceneManager.SetActiveScene(blank);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != blank && (AreaSceneCatalog.TryGetArea(scene.name, out _) ||
                    scene.name == SceneIds.AreaLoading || scene.name == SceneIds.MainMenu))
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        public IEnumerator NewGame_FromStartMenuLandsAtTheVillageLaneFootWithARunningClock()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu, Is.Not.Null, "The launch scene must install the start card.");
            Assert.That(Object.FindAnyObjectByType<MainMenuRoot>(), Is.Null,
                "The retained Home opening must not be reached without a request.");
            Assert.That(menu.BackdropCamera, Is.Not.Null);
            Assert.That(menu.SelectedOption, Is.EqualTo(StartMenuOption.NewGame));
            // A missing key renders as itself and nothing else notices.
            Assert.That(LocalizationService.Get("opening.new_game"),
                Is.Not.EqualTo("opening.new_game"));
            Assert.That(LocalizationService.Get("opening.quit"), Is.Not.EqualTo("opening.quit"));

            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.False, "A second click must not travel twice.");
            Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
            Assert.That(GameSessionState.GameHour, Is.EqualTo(7));
            Assert.That(GameSessionState.GameMinute, Is.EqualTo(40));
            Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(1));

            bool sawLoadingScreen = false;
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (AreaTravelService.IsTraveling && Time.realtimeSinceStartup < deadline)
            {
                sawLoadingScreen |= Object.FindAnyObjectByType<AreaLoadingRoot>() != null;
                yield return null;
            }

            Assert.That(AreaTravelService.IsTraveling, Is.False, "The village never finished loading.");
            Assert.That(sawLoadingScreen, Is.True, "New Game must travel through the loading screen.");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.AlpineVillage));

            AlpineVillageRoot village = Object.FindAnyObjectByType<AlpineVillageRoot>();
            Assert.That(village, Is.Not.Null);
            Assert.That(village.IsInitialized, Is.True);
            Assert.That(village.HadAreaArrival, Is.True);
            Assert.That(village.ArrivalToken, Is.EqualTo(AreaArrivalToken.Default));

            Transform hero = village.Player.GameObject.transform;
            Vector3 offset = hero.position - village.Plan.SpawnPosition;
            offset.y = 0f;
            Assert.That(offset.magnitude, Is.LessThan(0.5f),
                "He stands at the lane foot, not on the cableway platform.");
            Assert.That(Vector3.Dot(hero.forward, village.Plan.SpawnForward),
                Is.GreaterThan(0.99f), "He faces uphill at the mother's house.");
            Assert.That(GameSessionState.IsRidingAVehicle, Is.False,
                "There is no cabin and no ride: he is simply there.");

            // The loading screen's own seconds are game minutes until the
            // composition pause lands, so the hour is pinned and the minute is not.
            Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
            Assert.That(GameSessionState.GameHour, Is.EqualTo(7));
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.InRange(460d, 480d));
        }

        [UnityTest]
        public IEnumerator AreaTravel_ComposesEachDestinationBeforeCompleting()
        {
            GameSessionState.BeginNewGame();
            yield return SceneManager.LoadSceneAsync(SceneIds.AreaLoading);
            var destinations = new[] { GameAreaId.MountainRoad,
                GameAreaId.AlpineVillage, GameAreaId.City };
            foreach (GameAreaId destination in destinations)
            {
                bool previousAudioPause = AudioListener.pause;
                Assert.That(AreaTravelService.Request(destination), Is.True);
                float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
                int compositionFrames = 0;
                float lastProgress = 0f;
                while (AreaTravelService.IsTraveling && Time.realtimeSinceStartup < deadline)
                {
                    if (AreaTravelService.IsComposing)
                    {
                        compositionFrames++;
                        AreaLoadingRoot overlay = Object.FindAnyObjectByType<AreaLoadingRoot>();
                        Assert.That(overlay, Is.Not.Null, "The bar must cover partially built worlds.");
                        Assert.That(GameTimeScaleRuntime.IsPaused, Is.True);
                        Assert.That(AudioListener.pause, Is.True);
                        Assert.That(AreaTravelService.Progress, Is.GreaterThanOrEqualTo(lastProgress));
                        lastProgress = AreaTravelService.Progress;
                        if (!DestinationReady(destination))
                            Assert.That(AreaTravelService.Progress, Is.LessThan(1f));
                    }
                    yield return null;
                }

                Assert.That(AreaTravelService.IsTraveling, Is.False, "Area construction never finished.");
                Assert.That(compositionFrames, Is.GreaterThan(1), "Construction must yield under the bar.");
                Assert.That(DestinationReady(destination), Is.True);
                Assert.That(SceneManager.GetActiveScene().name,
                    Is.EqualTo(AreaSceneCatalog.GetSceneName(destination)));
                Assert.That(AudioListener.pause, Is.EqualTo(previousAudioPause));
                Assert.That(GameTimeScaleRuntime.IsPaused, Is.False);
                yield return null;
                Assert.That(Object.FindAnyObjectByType<AreaLoadingRoot>(), Is.Null);
            }

            // Opt-in measurements piggyback on this already constructed City;
            // ordinary lifecycle regressions do not incur a performance run.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-bp-audit-capture") >= 0)
            {
                yield return CaptureAtResolution(1920, 1080);
                yield return CaptureAtResolution(3840, 2160);
            }
        }

        [UnityTest]
        public IEnumerator StoppedTransitionOwner_ReleasesGuardAndSceneQueue()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.AreaLoading);
            Assert.That(SceneTransitionService.RequestLoad(SceneIds.AreaLoading), Is.True);
            // Let the coroutine begin its preload, then remove its owner.
            yield return null;
            SceneTransitionService owner = Object.FindAnyObjectByType<SceneTransitionService>();
            Assert.That(owner, Is.Not.Null);
            owner.enabled = false;
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            AsyncOperation following = SceneManager.LoadSceneAsync(SceneIds.AreaLoading);
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!following.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(following.isDone, Is.True, "A held activation stranded the scene queue.");
            Object.Destroy(owner.gameObject);
        }

        private static bool DestinationReady(GameAreaId area)
        {
            switch (area)
            {
                case GameAreaId.City:
                    return Object.FindAnyObjectByType<CityGameRoot>()?.IsInitialized == true;
                case GameAreaId.MountainRoad:
                    return Object.FindAnyObjectByType<MountainRoadRoot>()?.IsInitialized == true;
                case GameAreaId.AlpineVillage:
                    return Object.FindAnyObjectByType<AlpineVillageRoot>()?.IsInitialized == true;
                default: return false;
            }
        }

        private static IEnumerator CaptureAtResolution(int width, int height)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            for (int i = 0; i < 5; i++) yield return null;
            string label = $"city-idle-requested-{width}x{height}";
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath,
                "..", "TestResults", "PerformanceCaptures"));
            Assert.That(RuntimePerformanceCapture.StartCapture(
                new PerformanceCaptureOptions(SceneIds.City, label, 2, 5, 60), directory), Is.True);
            float deadline = Time.realtimeSinceStartup + 20f;
            while (RuntimePerformanceCapture.IsRunning && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(RuntimePerformanceCapture.IsRunning, Is.False);
            Assert.That(RuntimePerformanceCapture.LastReportPath, Is.Not.Empty);
            PerformanceCaptureReport report = JsonUtility.FromJson<PerformanceCaptureReport>(
                File.ReadAllText(RuntimePerformanceCapture.LastReportPath));
            Assert.That(report.reason, Is.EqualTo("completed"));
            Assert.That(report.label, Is.EqualTo(label));
            Assert.That(report.frameIntervalMs.sampleCount, Is.GreaterThan(0));
            Debug.Log($"Performance measurement requested {width}x{height}, actual " +
                $"{report.width}x{report.height}: {RuntimePerformanceCapture.LastReportPath}");
        }
    }
}
