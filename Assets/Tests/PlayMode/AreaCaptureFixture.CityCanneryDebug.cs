using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("F9 loaded-truck dispatch, pause, repeat and continuing delivery; Game view captures.")]
        public IEnumerator CityCanneryDebugSpawn()
        {
            Assert.That(Application.isBatchMode, Is.False);
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show(); view.Focus();
#endif
            yield return CaptureFocusedPort(ValidateCanneryDebugSpawn);
        }

        private static IEnumerator ValidateCanneryDebugSpawn(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityCanneryController cannery = city.Cannery;
            MinigameDebugWindow debug = city.DebugWindow;
            Transform truck = cannery.Truck;
            Vector3 savedHero = city.Player.GameObject.transform.position;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool wasFollowing = follow != null && follow.enabled;
            try
            {
                cannery.ForcePresentation = false;
                if (follow != null) follow.enabled = false;
                Vector3 observer = cannery.Plan.World(new Vector3(3.2f, .3f, -4f));
                city.Player.Motor.Teleport(observer);
                Physics.SyncTransforms();
                Assert.That(CityFishSupplySession.HasStarted, Is.False);
                Assert.That(debug.CanSpawnLoadedCanneryTruck, Is.True);
                Assert.That(debug.TrySpawnLoadedCanneryTruck(), Is.False, "Only an open debug window can dispatch.");

                using (GameTimeScaleRuntime.AcquirePause())
                {
                    Assert.That(debug.Open(), Is.True);
                    yield return null;
                    yield return CaptureDialogueScreenshot("cannery-debug-00-menu");
                    var unchanged = (GameSessionState.GameDayIndex, GameSessionState.GameTimeOfDayMinutes,
                        GameSessionState.HungerLevel, GameSessionState.FatigueLevel, GameSessionState.CashBalance);
                    Vector3 playerBefore = city.Player.GameObject.transform.position;
                    double life = cannery.LifeSeconds;
                    Assert.That(debug.TrySpawnLoadedCanneryTruck(), Is.True, debug.LastLaunchErrorKey);
                    Assert.That(debug.IsOpen, Is.False);
                    Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                    Assert.That(GameTimeScaleRuntime.IsPaused, Is.True, "Debug must not release someone else's pause.");
                    Assert.That(CityFishSupplySession.HasStarted, Is.True);
                    Assert.That(cannery.AutoAdvance, Is.True);
                    Assert.That((GameSessionState.GameDayIndex, GameSessionState.GameTimeOfDayMinutes,
                        GameSessionState.HungerLevel, GameSessionState.FatigueLevel, GameSessionState.CashBalance), Is.EqualTo(unchanged));
                    Assert.That(city.Player.GameObject.transform.position, Is.EqualTo(playerBefore));
                    Assert.That(cannery.LifeSeconds, Is.EqualTo(life));
                    AssertLoaded();
                    double pausedWork = cannery.WorkingSeconds;
                    Vector3 pausedTruck = truck.position;
                    yield return null;
                    yield return null;
                    Assert.That(cannery.WorkingSeconds, Is.EqualTo(pausedWork));
                    Assert.That(truck.position, Is.EqualTo(pausedTruck));
                    camera.transform.SetPositionAndRotation(observer + Vector3.up * 1.7f,
                        Quaternion.LookRotation(truck.position + Vector3.up * 1.2f - (observer + Vector3.up * 1.7f)));
                    camera.fieldOfView = 60f;
                    CaptureCurrentCamera(camera, "CityCannery", "debug-01-loaded-approach");
                }

                double start = cannery.WorkingSeconds;
                Vector3 before = truck.position;
                yield return null;
                yield return null;
                Assert.That(cannery.WorkingSeconds, Is.GreaterThan(start), cannery.LastObstacleName);
                Assert.That(Vector3.Distance(truck.position, before), Is.GreaterThan(.001f));

                // Let the normal departure easing reach road speed before
                // advancing the calendar to a later handling checkpoint.
                float movingUntil = Time.time + .8f;
                while (Time.time < movingUntil) yield return null;

                // Continue the real session past the first delivered crate,
                // without manually assigning the controller's sampled state.
                double receiving = cannery.Cycle.TransferUnitStart(CityFishSupplyStage.UnloadFish, 1,
                    cannery.Snapshot.Batch) + 1d;
                GameSessionState.AdvanceGameTime((float)(receiving - cannery.WorkingSeconds));
                yield return null;
                yield return null;
                Assert.That(cannery.Snapshot.Stage, Is.EqualTo(CityFishSupplyStage.UnloadFish));
                Assert.That(cannery.Snapshot.Handled, Is.GreaterThanOrEqualTo(1));
                Assert.That(cannery.Production.IsActive, Is.True);
                Assert.That(cannery.Snapshot.AccountedUnits, Is.EqualTo(CityFishSupplyCycle.HandlingUnits));

                using (GameTimeScaleRuntime.AcquirePause())
                {
                    long batch = cannery.Snapshot.Batch;
                    Assert.That(debug.Open(), Is.True);
                    Assert.That(debug.TrySpawnLoadedCanneryTruck(), Is.True);
                    Assert.That(cannery.Snapshot.Batch, Is.GreaterThan(batch));
                    AssertLoaded();
                    Assert.That(cannery.Snapshot.FactoryFish + cannery.Snapshot.FactoryCases + cannery.Snapshot.InProcess, Is.Zero);
                    Assert.That(cannery.WorkerCount, Is.EqualTo(5));
                }

                void AssertLoaded()
                {
                    Assert.That(cannery.Truck, Is.SameAs(truck), "Repeated dispatch reuses the same truck.");
                    Assert.That(cannery.Snapshot.Stage, Is.EqualTo(CityFishSupplyStage.PortToFactory));
                    Assert.That(cannery.Snapshot.TruckFish, Is.EqualTo(CityFishSupplyCycle.HandlingUnits));
                    Assert.That(cannery.Snapshot.AccountedUnits, Is.EqualTo(CityFishSupplyCycle.HandlingUnits));
                    Assert.That(cannery.HasSpawnedTruck && cannery.TruckPresentationActive, Is.True);
                    Assert.That(cannery.DriverSeatedContactsMatch, Is.True);
                    Assert.That(Vector3.Distance(truck.position, cannery.Plan.Origin), Is.LessThan(45f));
                    Assert.That(cannery.DetectObstacle(), Is.False, cannery.LastObstacleName);
                    Assert.That(CityFishSupplySession.WorkingSeconds, Is.EqualTo(cannery.WorkingSeconds));
                    for (int i = 0; i < CityFishSupplyCycle.HandlingUnits; i++)
                    {
                        Transform unit = cannery.transform.Find("Fish handling unit " + i);
                        Assert.That(unit.gameObject.activeInHierarchy, Is.True);
                        Assert.That(Vector3.Distance(unit.position, truck.position), Is.LessThan(6f));
                    }
                }
            }
            finally
            {
                debug.Close();
                cannery.AutoAdvance = false;
                cannery.AdvanceSounds(false);
                city.Player.Motor.Teleport(savedHero);
                if (follow != null) follow.enabled = wasFollowing;
                CityFishSupplySession.ResetForNewGame();
            }
        }
    }
}
