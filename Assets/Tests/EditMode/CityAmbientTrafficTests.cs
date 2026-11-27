using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityAmbientTrafficTests
    {
        [Test]
        public void SurveyedLoops_KeepTheirGradesAndHaveContinuousSeams()
        {
            var layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Resolve(
                GameSessionState.DefaultCityBlueprintId), CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            var network = CityDistrictBusPlanner.Create(layout);
            Assert.That(network.Count, Is.EqualTo(3));
            foreach (var source in network)
            {
                var plan = new CityAmbientCarPlan(source, CityStreetSupportPlan.ForLayout(layout));
                Assert.That(plan.Length, Is.InRange(source.LoopLength * .9f, source.LoopLength * 1.01f));
                plan.Path.Sample(plan.Length, out var first, out var f);
                plan.Path.Sample(plan.Length * 2f, out var second, out var g);
                Assert.That(Vector3.Distance(first, second), Is.LessThan(.08f), source.RouteId);
                Assert.That(Vector3.Dot(f, g), Is.GreaterThan(.99f), source.RouteId);
                float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
                for (float d = plan.Length; d < plan.Length * 2f; d += 2f)
                {
                    plan.Path.Sample(d, out var p, out var forward);
                    minimum = Mathf.Min(minimum, p.y); maximum = Mathf.Max(maximum, p.y);
                    if (Mathf.Abs(forward.y) >= .2f)
                    {
                        int nearest = Enumerable.Range(1, plan.Path.PointCount - 2)
                            .OrderBy(i => Mathf.Abs(plan.Path.GetDistance(i) - d)).First();
                        Assert.Fail(source.RouteId + " vertical step at " + p + ": " +
                            string.Join(" -> ", Enumerable.Range(nearest - 4, 9).Select(i => plan.Path.GetPoint(i).ToString("F5"))) +
                            "\n" + string.Join("\n", source.OrderedLinkIndices.Select(i => source.Links[i]).Where(link =>
                                link.Samples.Any(s => Vector3.Distance(s.Position, p) < .2f)).Select(link =>
                                link.Id + ": " + link.Samples[0].Position.ToString("F5") + " to " +
                                link.Samples[link.Samples.Count - 1].Position.ToString("F5"))));
                    }
                }
                if (source.Number == 3) Assert.That(maximum - minimum, Is.GreaterThan(30f));
                var drive = new LastRouteCarDriveModel(plan.Path, LastRouteCarDriveProfile.City);
                drive.Resume(6f, plan.Length * 2f - .2f);
                drive.Advance(.1f);
                Assert.That(drive.HasArrived, Is.False);
                Assert.That(drive.Speed, Is.GreaterThan(2f), "The end of a lap is not a terminal.");
            }
        }

        [Test]
        public void Hulls_KeepOpposingLanesAndBridgeLevelsSeparate()
        {
            var body = new Bounds(Vector3.up * .8f, new Vector3(1.8f, 1.6f, 4.83f));
            var car = new CityTrafficHull(Vector3.zero, Quaternion.identity, body);
            Assert.That(car.Overlaps(new CityTrafficHull(Vector3.right * 3f, Quaternion.Euler(0, 180, 0), body)), Is.False);
            Assert.That(car.Overlaps(new CityTrafficHull(Vector3.up * 7.68f, Quaternion.identity, body)), Is.False);
            Assert.That(car.Overlaps(new CityTrafficHull(Vector3.right, Quaternion.Euler(0, 90, 0), body)), Is.True);
            Assert.That(car.ContainsCapsule(Vector3.forward * 2.6f, .35f), Is.True);
            Assert.That(car.ContainsCapsule(Vector3.right * 1.8f, .35f), Is.False);
        }

        [Test]
        public void CrossingPriority_IsAcyclicButLetsAnEnteredCarClear()
        {
            Assert.That(AmbientTrafficRules.YieldToPrediction(9, 8, false, false, false), Is.True);
            Assert.That(AmbientTrafficRules.YieldToPrediction(8, 9, false, false, false), Is.False);
            Assert.That(AmbientTrafficRules.YieldToPrediction(9, 0, true, false, false), Is.False);
            Assert.That(AmbientTrafficRules.YieldToPrediction(0, 9, false, true, false), Is.True);
            Assert.That(AmbientTrafficRules.YieldToPrediction(9, 0, false, false, true), Is.False);
            Assert.That(AmbientTrafficRules.StopDistance(100f, .2f, 3f), Is.EqualTo(100f));
        }

        [Test]
        public void AmbientDriver_DoesNotCrossAHardHoldEvenWhenItIsTooLateToBrake()
        {
            var root = new GameObject("Traffic braking test");
            try
            {
                var body = root.AddComponent<BoxCollider>();
                var driver = root.AddComponent<LastRouteCarDriver>();
                driver.ExternallyDriven = true;
                driver.KeepBodyCollider = true;
                driver.Begin(new LastRouteCarDrivePath(new[] { Vector3.zero, Vector3.forward * 100f }),
                    LastRouteCarDriveProfile.City, 8f);
                driver.Model.SetHold(.05f);
                driver.Advance(.1f);
                Assert.That(driver.Distance, Is.LessThanOrEqualTo(.0501f));
                Assert.That(driver.Speed, Is.Zero);
                for (int i = 0; i < 240; i++) driver.Advance(.1f);
                Assert.That(driver.Distance, Is.LessThanOrEqualTo(.0501f), "Waiting never grants collision bypass.");
                Assert.That(body.enabled, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AmbientDriver_BuildsBrakeForceAndStopsAtTheHold()
        {
            var root = new GameObject("City service braking");
            try
            {
                root.AddComponent<BoxCollider>();
                var driver = root.AddComponent<LastRouteCarDriver>();
                driver.ExternallyDriven = true;
                driver.KeepBodyCollider = true;
                var profile = new LastRouteCarDriveProfile(6f, 1.45f, 4.8f, 2.65f, 2.2f, 2.35f);
                driver.Begin(new LastRouteCarDrivePath(new[] { Vector3.zero, Vector3.forward * 100f }), profile, 6f);
                driver.Model.SetHold(9.3f);
                driver.Advance(.05f);
                Assert.That(driver.Model.IsObstacleBraking, Is.True);
                Assert.That(driver.Speed, Is.InRange(5.95f, 5.999f), "Brake effort takes time to build.");
                float previous = driver.Speed;
                for (int i = 0; i < 220; i++)
                {
                    driver.Advance(.025f);
                    Assert.That(driver.Speed, Is.LessThanOrEqualTo(previous + .01f));
                    Assert.That(driver.Distance, Is.LessThanOrEqualTo(9.301f));
                    previous = driver.Speed;
                }
                Assert.That(driver.Distance, Is.EqualTo(9.3f).Within(.06f));
                Assert.That(driver.Speed, Is.LessThan(.02f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ObstructedHorn_ContactsOncePerIntervalAndCancelsBeforeContact()
        {
            var horn = new CityHornTimeline();
            int cues = 0;
            for (int i = 0; i < 15; i++)
                if (horn.Advance(.05f, true, 5f)) cues++;
            Assert.That(horn.Reach, Is.GreaterThan(0f));
            for (int i = 0; i < 30; i++)
                if (horn.Advance(.05f, false, 5f)) cues++;
            Assert.That(cues, Is.Zero);
            Assert.That(horn.Reach, Is.Zero);

            for (int i = 0; i < 130; i++)
                if (horn.Advance(.05f, true, 5f))
                {
                    cues++;
                    Assert.That(horn.Reach, Is.EqualTo(1f).Within(.025f));
                }
            Assert.That(cues, Is.EqualTo(2));
        }

        [Test]
        public void Drivers_AppendPermanentOrdinaryIdentitiesAfterExistingCast()
        {
            var roster = DefaultNpcPopulation.Characters;
            int ferryman = System.Array.FindIndex(roster.ToArray(),
                person => person.Id == DefaultNpcPopulation.LastRouteFerryman);
            Assert.That(ferryman, Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < AmbientTrafficRules.CirculatingCapacity; i++)
            {
                var person = roster[ferryman + 1 + i];
                Assert.That(person.Id, Is.EqualTo(DefaultNpcPopulation.AmbientDriverId(i)));
                Assert.That(person.ModelId, Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));
                Assert.That(person.UsesRandomBody, Is.False, "Keep the measured seat contacts.");
            }
            var passingDriver = roster[roster.Count - 1];
            Assert.That(passingDriver.Id, Is.EqualTo(DefaultNpcPopulation.AmbientDriverId(AmbientTrafficRules.CirculatingCapacity)));
            Assert.That(passingDriver.ModelId, Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));
            Assert.That(passingDriver.UsesRandomBody, Is.False);
            Assert.That(roster.Select(p => p.Id).Distinct().Count(), Is.EqualTo(roster.Count));
        }
    }
}
