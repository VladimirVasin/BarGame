using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>Bounds simulation CPU work against the currently imported arena assets; not rendered FPS.</summary>
    public sealed class CombatPerformancePlayModeTests
    {
        private const float TickSeconds = 1f / 60f;
        private const int MeasuredTicks = 20;
        private CombatTestRoot root;
        private float previousCaptureDelta;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = TickSeconds;
            RetroAudioService.Instance?.StopAll();
            EnsureListener();
            GameSessionState.BeginNewGame();
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
            root = Object.FindAnyObjectByType<CombatTestRoot>();
            Assert.That(root, Is.Not.Null);
            root.AutomaticSimulation = false;
            for (int frame = 0; frame < 60 && (!root.IsInitialized ||
                !GameInput.CanRead(GameInputContext.Gameplay)); frame++) yield return null;
            Assert.That(root.IsInitialized && GameInput.CanRead(GameInputContext.Gameplay), Is.True,
                "The imported combat scene must initialize and release its input gate.");
            root.SetSparring(false);
            Assert.That(root.Sparring, Is.False);
        }

        [UnityTest]
        public IEnumerator Range_SimulationTicksStayWithinFrameBudget()
        {
            var labels = new string[4];
            var averages = new double[4];
            var watch = new Stopwatch();
            for (int scenario = 0; scenario < labels.Length; scenario++)
            {
                float distance = scenario < 2 ? 1.4f : 4f;
                bool attacking = (scenario & 1) != 0;
                labels[scenario] = (attacking ? "both attacking" : "ready") + " distance=" + distance;
                PlacePair(distance);
                for (int frame = 0; frame < 4; frame++)
                {
                    root.Tick(TickSeconds);
                    yield return null;
                }
                if (attacking)
                {
                    Assert.That(root.Hero.TryAttack(), Is.True, labels[scenario] + ": hero starts");
                    Assert.That(root.Opponent.TryAttack(), Is.True, labels[scenario] + ": opponent starts");
                    // Start timing in the windup and continue through its active contact window.
                    for (int frame = 0; frame < 12; frame++)
                    {
                        root.Tick(TickSeconds);
                        yield return null;
                    }
                    Assert.That(root.Hero.State.IsAttacking && root.Opponent.State.IsAttacking, Is.True,
                        labels[scenario] + ": both swings must still be running when timing starts");
                }

                watch.Reset();
                for (int frame = 0; frame < MeasuredTicks; frame++)
                {
                    watch.Start();
                    root.Tick(TickSeconds);
                    watch.Stop();
                    yield return null;
                }
                averages[scenario] = watch.Elapsed.TotalMilliseconds / MeasuredTicks;
                TestContext.Out.WriteLine($"Combat simulation CPU: {labels[scenario]}, " +
                    $"{averages[scenario]:F3} ms/tick; excludes LateUpdate and rendering.");
                Assert.That(root.Hero != null && root.Opponent != null && root.Hero.Weapon != null &&
                    root.Opponent.Weapon != null, Is.True, labels[scenario] + ": both actors and weapons survive");
                LogAssert.NoUnexpectedReceived();
            }
            for (int scenario = 0; scenario < averages.Length; scenario++)
                Assert.That(averages[scenario], Is.LessThan(16.7d),
                    labels[scenario] + ": simulation alone exhausted the 60 Hz frame budget");
        }

        private void PlacePair(float distance)
        {
            root.ResetRound();
            Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(ground, Vector3.forward);
            root.Opponent.ResetActor(ground + Vector3.forward * distance, Vector3.back);
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) root.AutomaticSimulation = false;
            RetroAudioService.Instance?.StopAll();
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.name == SceneIds.CombatTest)
            {
                Scene cleanup = SceneManager.CreateScene("Combat Performance Cleanup");
                SceneManager.SetActiveScene(cleanup);
                foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
                    if (listener.gameObject.scene == active) listener.enabled = false;
                EnsureListener();
                AsyncOperation unload = SceneManager.UnloadSceneAsync(active);
                if (unload != null) yield return unload;
            }
            GameSessionState.BeginNewGame();
            Time.captureDeltaTime = previousCaptureDelta;
            root = null;
            yield return null;
        }

        private static void EnsureListener()
        {
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
                if (listener.isActiveAndEnabled) return;
            new GameObject("Combat Performance Audio Listener").AddComponent<AudioListener>();
        }
    }
}
