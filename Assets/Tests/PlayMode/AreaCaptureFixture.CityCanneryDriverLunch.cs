using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The existing delivery driver sits on the yard bench, eats, puts lunch away and returns to loading; F9, seek, pause and rendered poses.")]
        public IEnumerator CityCanneryDriverLunch() => CaptureFocusedPort(CaptureCanneryDriverLunch);

        private static IEnumerator CaptureCanneryDriverLunch(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityCanneryController cannery = city.Cannery;
            var driver = cannery.transform.Find("Fish Delivery Driver").GetComponent<VillageResidentPresentation>();
            Transform pelvis = CityPedestrianHandProps.FindSocket(driver.ModelRoot, "pelvis");
            Transform leftFoot = CityPedestrianHandProps.FindSocket(driver.ModelRoot, "foot.L");
            Transform rightFoot = CityPedestrianHandProps.FindSocket(driver.ModelRoot, "foot.R");
            Transform mouth = CityPedestrianHandProps.FindSocket(driver.ModelRoot, CityPedestrianHandProps.MouthSocketName);
            Transform truck = cannery.Truck, lunch = cannery.DriverLunch;
            var bones = new[] { driver.transform, pelvis, leftFoot, rightFoot, driver.RightGrip, driver.Head, lunch };
            var failures = new List<Exception>();
            Vector3 hero = city.Player.GameObject.transform.position;
            bool manualSpeech = cannery.FactoryConversation.UseManualClock;
            try
            {
                GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                city.Player.Motor.Teleport(cannery.Plan.World(new Vector3(3.2f, .3f, -4f)));
                cannery.FactoryConversation.UseManualClock = true;
                cannery.FactoryConversation.Suspend();
                Physics.SyncTransforms();
                Assert.That(city.DebugWindow.Open(), Is.True);
                Assert.That(city.DebugWindow.TrySpawnLoadedCanneryTruck(), Is.True, city.DebugWindow.LastLaunchErrorKey);
                Assert.That(cannery.Snapshot.Stage, Is.EqualTo(CityFishSupplyStage.PortToFactory));
                Assert.That(cannery.DriverLunchVisible, Is.False);
                Assert.That(cannery.DriverSeatedContactsMatch, Is.True);
                cannery.AutoAdvance = false;
                cannery.ApplyLifeAt(20d);
                long batch = cannery.Snapshot.Batch;
                double wait = cannery.Cycle.StageStart(CityFishSupplyStage.WaitForProduction, batch);
                double load = cannery.Cycle.StageStart(CityFishSupplyStage.LoadFinished, batch);
                DeferCanneryContract(failures, "bench support and continuous sitting/standing", () =>
                {
                    AssertCanneryWaitingBench(city, cannery);
                    Assert.That(cannery.Plan.Local(cannery.DriverBenchSeatContact).y, Is.EqualTo(.60f).Within(.025f));
                    cannery.ApplyAt(wait - 6d);
                    Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.WalkingToBench));
                    cannery.ApplyAt(wait - 3d);
                    Vector3 left = leftFoot.position, right = rightFoot.position;
                    foreach (double at in new[] { wait - 1.5d, wait, wait + 3.5d, load + 1d, load + 3.9d, load + 5.399d })
                    {
                        cannery.ApplyAt(at);
                        Assert.That(Vector3.Distance(leftFoot.position, left), Is.LessThan(.025f), "Left sole stays planted at " + at);
                        Assert.That(Vector3.Distance(rightFoot.position, right), Is.LessThan(.025f), "Right sole stays planted at " + at);
                    }
                    cannery.ApplyAt(wait + 3.5d);
                    Assert.That(Vector3.Distance(pelvis.position, cannery.DriverBenchSeatContact + Vector3.up * .08f),
                        Is.LessThan(.025f), "The skinned seated pelvis meets the actual plank, not the ground or the truck seat.");
                    foreach (double edge in new[] { wait - 12d, wait - 3d, wait, load, load + 2.4d, load + 5.4d, load + 12d })
                    {
                        cannery.ApplyAt(edge - .001d);
                        Vector3 rootBefore = driver.transform.position, hipBefore = pelvis.position, handBefore = driver.RightGrip.position;
                        cannery.ApplyAt(edge + .001d);
                        Assert.That(Vector3.Distance(driver.transform.position, rootBefore), Is.LessThan(.035f), "Driver boundary " + edge);
                        Assert.That(Vector3.Distance(pelvis.position, hipBefore), Is.LessThan(.035f), "Pelvis boundary " + edge);
                        Assert.That(Vector3.Distance(driver.RightGrip.position, handBefore), Is.LessThan(.06f), "Hand boundary " + edge);
                    }
                });
                DeferCanneryContract(failures, "personal lunch contacts and three-carton gate", () =>
                {
                    cannery.ApplyAt(wait + .6d);
                    Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.GettingLunch));
                    Assert.That(cannery.DriverLunchVisible, Is.False, "Bread appears only once his hand reaches his pocket.");
                    cannery.ApplyAt(wait + 3.5d);
                    Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.Eating));
                    Assert.That(cannery.DriverEating && cannery.DriverLunchVisible, Is.True);
                    Assert.That(Vector3.Distance(cannery.DriverLunchMouthContact, mouth.position), Is.LessThan(.015f));
                    Assert.That(Vector3.Distance(cannery.DriverLunchHandContact, driver.RightGrip.position), Is.LessThan(.025f));
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    Vector3 biteHand = driver.RightGrip.position;
                    cannery.ApplyAt(wait + 8d);
                    Assert.That(cannery.DriverLunchVisible, Is.True);
                    Assert.That(cannery.DriverEating, Is.False);
                    Assert.That(Vector3.Distance(driver.RightGrip.position, biteHand), Is.GreaterThan(.12f), "The bite returns to a visible resting hold.");
                    for (int unit = 0; unit < 2; unit++)
                    {
                        cannery.ApplyAt(cannery.Cycle.InspectionPhaseStart(CityCanneryInspectionStage.Clear, unit, batch) + .01d);
                        Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.Eating));
                        Assert.That(cannery.Snapshot.TruckCases, Is.Zero, "An individual approval does not interrupt lunch for loading.");
                    }
                    cannery.ApplyAt(load - .001d);
                    Assert.That(cannery.Snapshot.Inspection.StoredUnits, Is.EqualTo(3));
                    cannery.ApplyAt(load + 1d);
                    Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.StowingLunch));
                    Assert.That(cannery.Snapshot.TruckCases, Is.Zero);
                    cannery.ApplyAt(load + 3.9d);
                    Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.Standing));
                    Assert.That(cannery.DriverLunchVisible, Is.False, "Lunch is put away before he stands or uses the jack.");
                    cannery.ApplyAt(load + 9d);
                    Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.WalkingToTrolley));
                    cannery.ApplyAt(load + 12.85d);
                    Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.None));
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    foreach (var grip in new[] { (driver.LeftGrip, "ANCHOR_TrolleyHandleLeft"), (driver.RightGrip, "ANCHOR_TrolleyHandleRight") })
                        Assert.That(Vector3.Distance(grip.Item1.position,
                            CityCanneryAssetProvider.FindPart(cannery.FactoryTrolley.gameObject, grip.Item2).position), Is.LessThan(.025f));
                    cannery.ApplyAt(cannery.Cycle.StageStart(CityFishSupplyStage.FactoryToShop, batch));
                    Assert.That(cannery.Snapshot.TruckCases, Is.EqualTo(3));
                    Assert.That(cannery.DriverSeatedContactsMatch, Is.True);
                    Assert.That(cannery.DriverLunchVisible, Is.False);
                });

                foreach (var shot in new[] { (wait - 6d, "00-walk-to-bench"), (wait - 1.5d, "01-sitting"),
                    (wait + 3.5d, "02-seated-bite"), (load + 3.9d, "03-standing"), (load + 12.85d, "04-trolley-grip") })
                {
                    cannery.ApplyAt(shot.Item1);
                    Vector3 target = shot.Item2 == "04-trolley-grip" ? driver.transform.position + Vector3.up :
                        cannery.DriverBenchSeatContact + Vector3.up * .45f;
                    Vector3 eye = shot.Item2 == "04-trolley-grip" ? new Vector3(2.8f, 1.6f, -8.2f) :
                        shot.Item2 == "02-seated-bite" ? new Vector3(2.5f, 1.2f, -.7f) : new Vector3(3.4f, 1.8f, .1f);
                    yield return CaptureCannery(camera, city, cannery, shot.Item1, "driver-lunch-" + shot.Item2,
                        cannery.Plan.World(eye), target);
                }
                cannery.ApplyAt(wait + 3.5d);
                var savedPose = new Pose[bones.Length];
                for (int i = 0; i < bones.Length; i++) savedPose[i] = new Pose(bones[i].position, bones[i].rotation);
                cannery.ApplyAt(load + 13d);
                cannery.ApplyAt(0d);
                cannery.ApplyAt(wait + 3.5d);
                DeferCanneryContract(failures, "seek reconstructs the same driver, seat and bite", () =>
                    AssertDriverLunchPose(bones, savedPose));
                Assert.That(CityFishSupplySession.TrySetDebugWorkingSeconds(wait + 3.5d), Is.True);
                cannery.AutoAdvance = true;
                yield return null;
                yield return null;
                Assert.That(cannery.WorkingSeconds, Is.GreaterThan(wait + 3.5d), cannery.LastObstacleName);
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return null;
                    double work = cannery.WorkingSeconds, life = cannery.LifeSeconds;
                    for (int i = 0; i < bones.Length; i++) savedPose[i] = new Pose(bones[i].position, bones[i].rotation);
                    yield return null;
                    yield return null;
                    DeferCanneryContract(failures, "pause holds lunch, body and both clocks", () =>
                    {
                        Assert.That(cannery.WorkingSeconds, Is.EqualTo(work));
                        Assert.That(cannery.LifeSeconds, Is.EqualTo(life));
                        AssertDriverLunchPose(bones, savedPose);
                    });
                    Assert.That(city.DebugWindow.Open(), Is.True);
                    Assert.That(city.DebugWindow.TrySpawnLoadedCanneryTruck(), Is.True, city.DebugWindow.LastLaunchErrorKey);
                    Assert.That(GameTimeScaleRuntime.IsPaused, Is.True);
                    Assert.That(cannery.Snapshot.Batch, Is.GreaterThan(batch));
                    Assert.That(cannery.Truck, Is.SameAs(truck));
                    Assert.That(cannery.transform.Find("Fish Delivery Driver"), Is.SameAs(driver.transform));
                    Assert.That(cannery.DriverLunch, Is.SameAs(lunch));
                    Assert.That(cannery.WorkerCount, Is.EqualTo(5));
                    Assert.That(cannery.DriverLunchVisible, Is.False);
                    Assert.That(cannery.DriverRestPhase, Is.EqualTo(CityCanneryDriverRestPhase.None));
                    Assert.That(cannery.DriverSeatedContactsMatch, Is.True);
                }
            }
            finally
            {
                city.DebugWindow.Close();
                cannery.AutoAdvance = false;
                cannery.FactoryConversation.Suspend();
                cannery.FactoryConversation.UseManualClock = manualSpeech;
                CityFishSupplySession.ResetForNewGame();
                cannery.ApplyAt(0d);
                cannery.AdvanceSounds(false);
                city.Player.Motor.Teleport(hero);
            }
            if (failures.Count > 0) throw new AggregateException("Cannery driver lunch contracts failed.", failures);
        }

        private static void AssertDriverLunchPose(Transform[] bones, Pose[] expected)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                Assert.That(Vector3.Distance(bones[i].position, expected[i].position), Is.LessThan(.0001f), bones[i].name);
                Assert.That(Quaternion.Angle(bones[i].rotation, expected[i].rotation), Is.LessThan(.02f), bones[i].name);
            }
        }
    }
}
