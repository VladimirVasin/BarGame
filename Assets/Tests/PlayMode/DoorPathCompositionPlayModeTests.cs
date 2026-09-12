using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// A door into the City used to pay the whole city build inside one
    /// frozen frame after the door sequence, because only area travel
    /// pumped a registered composition. What has to hold here is the door
    /// path's own staging: the City registers from its Awake while the
    /// black is still up, is built over several frames with the transition
    /// guard held, and the door scene's camera does not outlive the load.
    /// </summary>
    public sealed class DoorPathCompositionPlayModeTests
    {
        private const float DeadlineSeconds = 120f;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while ((SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling, Is.False);
            Scene blank = SceneManager.CreateScene("Door Path Composition Cleanup");
            SceneManager.SetActiveScene(blank);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != blank && (AreaSceneCatalog.TryGetArea(scene.name, out _) ||
                    scene.name == SceneIds.AreaLoading || scene.name == SceneIds.BarInterior ||
                    scene.name == SceneIds.DoorTransition))
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            // Voices that outlive the city (pooled effects, a detached music
            // tail) would play into the next test's listener-less scene and
            // trip its log assertions.
            foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (source != null) source.Stop();
            }
            MusicMix.ClearFadeOuts();
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        public IEnumerator LeavingTheBar_BuildsTheCityOverSeveralFramesBehindTheBlack()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.EnterBar("door-path-composition-test");
            yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
            BarInteriorRoot bar = null;
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (bar == null || !bar.IsInitialized)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Bar initialization timed out.");
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                yield return null;
            }

            bool previousAudioPause = AudioListener.pause;
            // The same entry BarExit takes once its door action completes.
            Assert.That(SceneTransitionService.RequestDoorLoad(
                SceneIds.City, DoorTransitionDirection.ExitBar, out string operationId), Is.True);
            GameSessionState.PrepareCityReturn();
            Assert.That(operationId, Is.Not.Empty);

            int composingFrames = 0;
            deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (SceneTransitionService.IsTransitioning && Time.realtimeSinceStartup < deadline)
            {
                if (CompositionDriver.IsComposing)
                {
                    composingFrames++;
                    Assert.That(AreaTravelService.IsTraveling, Is.False, "The door path composes without area travel.");
                    Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.City));
                    Assert.That(Object.FindAnyObjectByType<TransitionBlackoutOverlay>(), Is.Not.Null,
                        "The black must cover the partially built city.");
                    Assert.That(GameTimeScaleRuntime.IsPaused, Is.True);
                    Assert.That(AudioListener.pause, Is.True);
                }
                yield return null;
            }

            Assert.That(SceneTransitionService.IsTransitioning, Is.False, "The door transition never finished.");
            Assert.That(composingFrames, Is.GreaterThan(1), "The city must be built over several frames, not one.");
            Assert.That(CompositionDriver.IsComposing, Is.False);
            CityGameRoot city = Object.FindAnyObjectByType<CityGameRoot>();
            Assert.That(city, Is.Not.Null);
            Assert.That(city.IsInitialized, Is.True, "The transition ended before the city was ready.");
            Assert.That(GameTimeScaleRuntime.IsPaused, Is.False);
            Assert.That(AudioListener.pause, Is.EqualTo(previousAudioPause));
            yield return null;
            Assert.That(Object.FindAnyObjectByType<TransitionBlackoutOverlay>(), Is.Null,
                "The black must leave with the transition.");
            Assert.That(Object.FindAnyObjectByType<DoorTransitionRoot>(), Is.Null);
            Camera main = Camera.main;
            Assert.That(main, Is.Not.Null);
            Assert.That(main.gameObject.scene.name, Is.EqualTo(SceneIds.City),
                "The door scene's camera must not outlive the Single load.");
        }
    }
}
