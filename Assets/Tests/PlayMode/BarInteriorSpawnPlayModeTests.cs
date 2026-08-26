using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Watches the first two seconds of the bar through the camera the
    /// player actually gets.
    ///
    /// This exists because the room passed every measurement it had -
    /// anchors, sizes, tints, renderer bounds, a folder of rendered
    /// frames - while the hero fell through its floor forever and the
    /// chase camera sat inside his skull. None of those checks could see
    /// it: `BarModelContractTests` measures geometry and never a
    /// collider, and `AreaCaptureFixture` photographs the room from
    /// invented camera poses with the hero's renderers switched off, so
    /// it can neither see him fall nor see where his camera ended up.
    ///
    /// So this fixture asserts the two things a player notices in his
    /// first second: he stands on the floor, and he can see himself.
    /// </summary>
    public sealed class BarInteriorSpawnPlayModeTests
    {
        private const float TimeoutSeconds = 20f;

        //  Long enough to outlast the arrival shot (1.35 s), which owns
        //  the camera while it plays and hands it back to the follow.
        private const float SettleSeconds = 2f;

        //  The chase camera wants 2.2 m indoors and gives that up to
        //  geometry behind the hero. Under a metre it is inside him.
        private const float MinimumCameraDistance = 1f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameSessionState.EnterBar("bar-spawn-test");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene bar = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (bar.IsValid() && bar.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene(
                    "Bar Spawn Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(bar);
                while (unload != null && !unload.isDone)
                {
                    yield return null;
                }
            }

            GameSessionState.ClearRoute();
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            ArrivingHero_KeepsHisFeetOnTheFloorAndHisCameraBehindHim()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);

            Transform hero = bar.Player.GameObject.transform;
            float spawnHeight = bar.Layout.PlayerSpawn.y;

            float deadline = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.That(
                    hero.position.y,
                    Is.GreaterThan(spawnHeight - 0.5f),
                    $"the hero is falling through the bar's floor: he " +
                    $"spawned at y={spawnHeight:F2} and is now at " +
                    $"{hero.position}. The room's collision is not where " +
                    "its geometry is.");
                yield return null;
            }

            CharacterController controller =
                bar.Player.GameObject.GetComponent<CharacterController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                controller.isGrounded,
                Is.True,
                "the hero never found the floor of the bar");
        }

        [UnityTest]
        public IEnumerator ArrivingHero_IsNotStandingInsideHisOwnCamera()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);

            float deadline = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "the bar has no main camera");
            Assert.That(
                bar.ArrivalPresentation.IsPlaying,
                Is.False,
                "the arrival shot never handed the camera back");

            Transform hero = bar.Player.GameObject.transform;
            Vector3 head = hero.position + Vector3.up * 1.3f;
            float distance = Vector3.Distance(
                camera.transform.position,
                head);
            Assert.That(
                distance,
                Is.GreaterThan(MinimumCameraDistance),
                $"the camera sits {distance:F2} m from the hero's head - " +
                "inside him. Its collision probe is starting inside a " +
                "collider, so the chase distance collapsed to nothing.");
        }

        private static IEnumerator LoadBar(
            System.Action<BarInteriorRoot> onReady)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.BarInterior,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            BarInteriorRoot bar = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                if (bar != null && bar.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                bar,
                Is.Not.Null,
                "the bar interior never built its root");
            Assert.That(bar.IsInitialized, Is.True);
            Assert.That(bar.Player, Is.Not.Null);
            onReady(bar);
        }
    }
}
