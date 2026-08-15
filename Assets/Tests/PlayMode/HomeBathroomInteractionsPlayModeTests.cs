using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBathroomInteractionsPlayModeTests
    {
        private const float TimeoutSeconds = 30f;
        private const float FastTimeScale = 6f;

        private HomeInteriorRoot home;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            GameSessionState.BeginNewGame();
            GameSessionState.EnterHome();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Scene cleanupScene = SceneManager.CreateScene(
                "BathroomInteractionCleanup" +
                UnityEngine.Random.Range(0, 100000));
            SceneManager.SetActiveScene(cleanupScene);
            AsyncOperation unload =
                SceneManager.UnloadSceneAsync(SceneIds.HomeInterior);
            while (unload != null && !unload.isDone)
            {
                yield return null;
            }

            home = null;
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Toilet_EPlaysPrivacyCutAndCommitsOnce()
        {
            yield return LoadHome();
            GameSessionState.UpdateNeeds(0, 40);
            yield return WalkToAndActivate(
                home.ToiletScene,
                new Vector3(3.10f, 0.12f, 1.20f));

            Time.timeScale = FastTimeScale;
            yield return WaitUntil(
                () => home.ToiletScene.Timeline.Phase ==
                      HomeToiletScenePhase.Privacy,
                "The toilet scene never reached its privacy cut.");
            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.False,
                "The scene must own the player while it runs.");

            yield return WaitUntil(
                () => home.Player.Motor.InputEnabled,
                "The toilet scene never restored the player.");
            Time.timeScale = 1f;
            Assert.That(
                GameSessionState.StressLevel,
                Is.EqualTo(40 - HomeToiletInteraction.StressRelief));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(92f).Within(0.01f),
                "The pinned bathroom shot must return after the " +
                "scene.");
        }

        [UnityTest]
        public IEnumerator Shower_DrawsCurtainRunsWaterAndRestores()
        {
            yield return LoadHome();
            GameSessionState.UpdateNeeds(0, 50);
            Transform curtain =
                home.Room.Find("Home Bathroom Shower Curtain");
            Assert.That(curtain, Is.Not.Null);

            yield return WalkToAndActivate(
                home.ShowerScene,
                new Vector3(3.30f, 0.12f, 2.35f));

            Time.timeScale = FastTimeScale;
            yield return WaitUntil(
                () => home.ShowerScene.Timeline.Phase ==
                      HomeShowerScenePhase.Hold,
                "The shower never reached its running hold.");
            Assert.That(
                curtain.localScale.x,
                Is.EqualTo(1f).Within(0.01f),
                "The curtain must be fully drawn while the water " +
                "runs.");
            Assert.That(
                home.Soundscape.ShowerWaterAmount,
                Is.EqualTo(1f).Within(0.01f));
            Assert.That(home.ShowerScene.WaterEffect.IsEmitting,
                Is.True);

            yield return WaitUntil(
                () => home.Player.Motor.InputEnabled,
                "The shower scene never restored the player.");
            Time.timeScale = 1f;
            Assert.That(
                curtain.localScale.x,
                Is.EqualTo(
                    HomeShowerSceneTimeline.GatheredCurtainScale)
                    .Within(0.01f),
                "The curtain must gather back after the scene.");
            Assert.That(
                home.Soundscape.ShowerWaterAmount,
                Is.Zero.Within(0.001f));
            Assert.That(
                GameSessionState.StressLevel,
                Is.EqualTo(50 - HomeShowerInteraction.StressRelief));
        }

        [UnityTest]
        public IEnumerator Brushing_MirrorSceneGatesReliefPerDay()
        {
            yield return LoadHome();
            GameSessionState.UpdateNeeds(0, 30);
            yield return WalkToAndActivate(
                home.TeethBrushing,
                new Vector3(2.075f, 0.12f, 2.55f));

            Time.timeScale = FastTimeScale;
            yield return WaitUntil(
                () => home.TeethBrushing.Timeline.Phase ==
                      HomeTeethBrushingPhase.Brushing,
                "The brushing scene never reached its loop.");
            Assert.That(
                home.TeethBrushing.Toothbrush,
                Is.Not.Null);
            Assert.That(
                home.TeethBrushing.Toothbrush.activeSelf,
                Is.True,
                "The toothbrush must be in hand during the loop.");
            Assert.That(
                home.TeethBrushing.ArmPose.Weight,
                Is.EqualTo(1f).Within(0.01f));

            yield return WaitUntil(
                () => home.Player.Motor.InputEnabled,
                "The brushing scene never restored the player.");
            Time.timeScale = 1f;
            Assert.That(
                home.TeethBrushing.Toothbrush.activeSelf,
                Is.False);
            int stressAfterFirst = GameSessionState.StressLevel;
            Assert.That(
                stressAfterFirst,
                Is.EqualTo(
                    30 - HomeTeethBrushingInteraction.StressRelief));

            // The same game day: the scene replays, the relief does
            // not.
            yield return WalkToAndActivate(
                home.TeethBrushing,
                new Vector3(2.075f, 0.12f, 2.55f));
            Time.timeScale = FastTimeScale;
            yield return WaitUntil(
                () => home.Player.Motor.InputEnabled,
                "The second brushing never restored the player.");
            Time.timeScale = 1f;
            Assert.That(
                GameSessionState.StressLevel,
                Is.EqualTo(stressAfterFirst),
                "A second brushing the same day must commit " +
                "nothing.");
        }

        private IEnumerator LoadHome()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.HomeInterior,
                LoadSceneMode.Single);
            while (load != null && !load.isDone)
            {
                yield return null;
            }

            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("HomeInterior never finished initializing.");
        }

        private IEnumerator WalkToAndActivate(
            HomeBathroomSceneInteraction scene,
            Vector3 approachPosition)
        {
            home.Player.Motor.Teleport(approachPosition);
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (ReferenceEquals(
                        home.Player.Interactor.ActiveInteractable,
                        scene))
                {
                    scene.Interact(home.Player.Interactor);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"{scene.GetType().Name} was never discovered by " +
                "the interactor.");
        }

        private static IEnumerator WaitUntil(
            System.Func<bool> condition,
            string failureMessage)
        {
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(failureMessage);
        }
    }
}
