#if UNITY_EDITOR_OSX
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class MetalGameViewRingBufferTests
    {
        private const string WarningPrefix = "Ran out of Graphics Ring Buffer space.";
        private const int SecondsPerStop = 30;
        // Start at Home and visit the balconies that spawned smokers in the reported session.
        private static readonly Vector2Int[] Stops =
        {
            new(-41, -21), new(42, -21), new(40, -21), new(41, -21), new(21, -6),
            new(22, -6), new(23, -6), new(22, -4), new(22, -2), new(23, -3)
        };
        private GameSessionState.CheckpointSnapshot savedSession;
        private CheckpointWorldState savedWorld;

        [UnityTest, Explicit("Five minutes of actual macOS Editor Game View rendering. Run alone without -batchmode or -nographics.")]
        [Timeout(600000)]
        public IEnumerator CityGameView_ThreadedMetalRingBuffer()
        {
            Assert.That(Application.isBatchMode, Is.False, "The interactive Game View must repaint.");
            Assert.That(SystemInfo.graphicsDeviceType, Is.EqualTo(GraphicsDeviceType.Metal));
            Assert.That(SystemInfo.graphicsMultiThreaded, Is.True, "This probe exercises the threaded graphics client.");
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-gfx-ring-buffer-size");
            Assert.That(index >= 0 && index + 1 < args.Length, Is.True, "Pass the buffer size being compared on the Editor command line.");
            Assert.That(uint.TryParse(args[index + 1], out uint bytes) && bytes > 0, Is.True);

            savedSession = GameSessionState.CaptureCheckpoint();
            savedWorld = CheckpointWorldState.Capture();
            int warnings = 0, frames = 0, lastFrame = -1;
            Camera main = null;
            bool sawGameView = false;
            void ObserveLog(string text, string stack, LogType type)
            {
                if (text != null && text.StartsWith(WarningPrefix, StringComparison.Ordinal))
                    Interlocked.Increment(ref warnings);
            }
            void Rendered(ScriptableRenderContext context, Camera camera)
            {
                if (main != null && camera == main && camera.cameraType == CameraType.Game && lastFrame != Time.frameCount)
                {
                    // A real Game View repaint includes GameView.OnGUI above the native
                    // render loop. macOS keyboard focus can belong to another application.
                    if (!sawGameView && frames % 120 == 0)
                    {
                        var trace = new System.Diagnostics.StackTrace(false);
                        for (int i = 0; i < trace.FrameCount && !sawGameView; i++)
                            sawGameView = trace.GetFrame(i)?.GetMethod()?.DeclaringType?.FullName == "UnityEditor.GameView";
                    }
                    lastFrame = Time.frameCount;
                    frames++;
                }
            }

            Application.logMessageReceivedThreaded += ObserveLog;
            RenderPipelineManager.endCameraRendering += Rendered;
            try
            {
                var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
                var view = EditorWindow.GetWindow(viewType);
                view.Show();
                view.Focus();
                yield return null;
                view.Repaint();
                Debug.Log($"Metal Game View probe: starting Home → Stairwell → City, ring {bytes} bytes, {SystemInfo.renderingThreadingMode}.");

                GameSessionState.BeginNewGame("metal-game-view-probe");
                Assert.That(GameSessionState.TryStartGameTimeAt(6 * 60), Is.True);
                GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
                GameSessionState.EnterHome();
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
                yield return WaitFor(() => UnityEngine.Object.FindAnyObjectByType<HomeInteriorRoot>() is { IsInitialized: true }, "Home composition");
                Assert.That(SceneTransitionService.RequestDoorLoad(SceneIds.StairwellInterior, DoorTransitionDirection.ExitApartment), Is.True);
                GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.ApartmentDoor);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.StairwellInterior &&
                    !SceneTransitionService.IsTransitioning, "apartment door");
                Assert.That(SceneTransitionService.RequestDoorLoad(SceneIds.City, DoorTransitionDirection.ExitBuilding), Is.True);
                GameSessionState.PrepareHomeReturn();
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.City &&
                    !SceneTransitionService.IsTransitioning, "street door");
                var city = UnityEngine.Object.FindAnyObjectByType<CityGameRoot>();
                Assert.That(city != null && city.IsInitialized, Is.True);
                main = city.Camera;
                Assert.That(main, Is.SameAs(Camera.main));
                var follow = main.GetComponent<PlayerCameraFollow>();
                var houses = CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings;
                for (int stop = 0; stop < Stops.Length; stop++)
                {
                    var house = houses.Single(h => h.Lot.Cell == Stops[stop]);
                    var motor = city.Player.Motor;
                    motor.Teleport(house.StreetContactWorld + Vector3.up * PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                    int startFrames = frames;
                    double start = Time.realtimeSinceStartupAsDouble, previous = start;
                    while (Time.realtimeSinceStartupAsDouble - start < SecondsPerStop)
                    {
                        double now = Time.realtimeSinceStartupAsDouble;
                        view.Repaint();
                        follow.RotateYaw(16f * (float)Math.Min(now - previous, .2));
                        previous = now;
                        if (stop >= Stops.Length / 2 && GameSessionState.IntoxicationLevel < 85)
                            GameSessionState.UpdateDrinkingProgress(95, DrinkId.Vodka, GameSessionState.DrinksConsumed + 1);
                        yield return null;
                    }
                    Assert.That(frames - startFrames, Is.GreaterThanOrEqualTo(30), $"No sustained production Game View rendering at {Stops[stop]}.");
                    Assert.That(sawGameView, Is.True, "The production camera must be drawn by the actual Editor Game View.");
                    Debug.Log($"Metal Game View probe: {stop + 1}/{Stops.Length}, cell {Stops[stop]}, head-camera frames {frames - startFrames}, " +
                        $"alcohol {GameSessionState.IntoxicationLevel}, warnings {Volatile.Read(ref warnings)}.");
                }
                Assert.That(Volatile.Read(ref warnings), Is.Zero, $"The graphics ring overflowed while rendering the Home-to-City route with {bytes} bytes.");
            }
            finally
            {
                Application.logMessageReceivedThreaded -= ObserveLog;
                RenderPipelineManager.endCameraRendering -= Rendered;
            }
        }

        private static IEnumerator WaitFor(Func<bool> ready, string operation)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 80;
            while (!ready() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(ready(), Is.True, $"Timed out waiting for {operation}.");
        }

        [UnityTearDown]
        public IEnumerator RestoreProbeSession()
        {
            if (savedSession == null) yield break;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            }
            finally
            {
                savedWorld.Restore();
                GameSessionState.RestoreCheckpoint(savedSession);
                savedSession = null;
            }
        }
    }
}
#endif
