using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [Test]
        public void GlassSorter_BlockedEveningTripResumesAcrossMidnight()
        {
            var layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            var parcel = new GameObject("Isolated glass return").transform;
            var pavilion = CityGlassReturnWorld.Build(parcel, CityGlassReturnSitePlan.ForLayout(layout), true, out _);
            var worker = pavilion.GetComponent<CityGlassReturnWorker>();
            var plan = CityGlassReturnWorkerPlan.ForLayout(layout);
            worker.ConfigureCommute(plan);
            worker.AutoAdvance = false;
            var observer = new GameObject("Distant observer").transform;
            observer.position = pavilion.position + Vector3.right * 200f;
            worker.PresentationObserver = observer;

            var blocker = new GameObject("Occupied staff doorway").AddComponent<CapsuleCollider>();
            blocker.height = 1.7f;
            blocker.radius = .35f;
            blocker.center = Vector3.up * .85f;
            blocker.transform.position = plan.DoorCrossing[1];
            Physics.SyncTransforms();

            double day = GameTimeState.RealSecondsPerGameDay;
            double opening = CityGlassReturnWorker.ShiftStartMinute / GameTimeState.GameMinutesPerRealSecond;
            double clock = CityGlassReturnWorker.ShiftEndMinute / GameTimeState.GameMinutesPerRealSecond;
            worker.ApplyAt(clock);
            AdvanceTo(day - .5d);
            Assert.That(worker.IsUsingStaffDoor, Is.True);
            Vector3 held = worker.Worker.transform.position;
            using (GameTimeScaleRuntime.AcquirePause()) worker.AdvanceAt(day);
            Assert.That(worker.Worker.transform.position, Is.EqualTo(held));
            AdvanceTo(day + .5d);
            Assert.That(worker.IsCommuting && worker.IsUsingStaffDoor, Is.True,
                "Midnight must retain the unfinished evening door action.");
            Assert.That(worker.Worker.transform.position, Is.EqualTo(held));

            AdvanceTo(2d * day + opening + 1d);
            Assert.That(worker.IsCommuting && worker.IsUsingStaffDoor, Is.True);
            Assert.That(worker.WorkLightOn, Is.False);
            Assert.That(worker.Worker.transform.position, Is.EqualTo(held));
            blocker.enabled = false;
            bool reachedHome = false;
            Vector3 previous = held;
            double deadline = clock + 2d * worker.CommuteSeconds + 2d;
            while (!worker.IsWorking && clock < deadline)
            {
                worker.AdvanceAt(clock += .25d);
                Vector3 current = worker.Worker.transform.position;
                Assert.That(Vector3.Distance(previous, current), Is.LessThan(.30f));
                if (worker.IsAtHome)
                {
                    Assert.That(Vector3.Distance(current, plan.InsideHome), Is.LessThan(.025f));
                    reachedHome = true;
                }
                previous = current;
            }
            Assert.That(reachedHome && worker.IsWorking && worker.WorkLightOn, Is.True,
                "Finish the delayed evening walk, then walk back for the current shift.");

            worker.ApplyAt(day + .5d);
            Assert.That(worker.IsAtHome && !worker.WorkLightOn, Is.True,
                "An explicit calendar seek reconstructs the ordinary night at home.");

            void AdvanceTo(double until)
            {
                while (clock < until)
                {
                    clock = System.Math.Min(clock + .5d, until);
                    worker.AdvanceAt(clock);
                }
            }
        }
    }
}
