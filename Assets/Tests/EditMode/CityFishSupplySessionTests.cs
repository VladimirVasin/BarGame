using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityFishSupplySessionTests
    {
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            GameSessionState.BeginNewGame();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
            Time.timeScale = previousTimeScale;
        }

        [TestCase(96)]
        [TestCase(320)]
        public void TruckObstacleQuery_SaturationKeepsFilteringAndFindsBlockingBodies(int ownBodyCount)
        {
            var owner = new GameObject("Cannery obstacle query owner");
            var external = new GameObject("Cannery obstacle query external bodies");
            Vector3 origin = new Vector3(10000f, 10000f, 10000f);
            owner.transform.position = external.transform.position = origin;
            var cannery = owner.AddComponent<CityCanneryController>();
            try
            {
                // Fill both the retained-growth and complete-fallback paths
                // with hits which the final body filter must ignore.
                for (int index = 0; index < ownBodyCount; index++)
                {
                    var ownBody = new GameObject("Own truck body " + index);
                    ownBody.transform.SetParent(owner.transform, false);
                    ownBody.transform.localPosition = Vector3.up;
                    ownBody.AddComponent<BoxCollider>().size = Vector3.one * .2f;
                }
                var lowBody = new GameObject("Low ground body");
                lowBody.transform.SetParent(external.transform, false);
                lowBody.transform.localPosition = Vector3.down;
                lowBody.AddComponent<BoxCollider>().size = Vector3.one * .2f;
                var blockingBody = new GameObject("Blocking pedestrian body");
                blockingBody.transform.SetParent(external.transform, false);
                blockingBody.transform.localPosition = Vector3.up;
                BoxCollider blocker = blockingBody.AddComponent<BoxCollider>();
                blocker.size = Vector3.one * .3f;
                blocker.enabled = false;

                Physics.SyncTransforms();
                Assert.That(cannery.FindTruckObstacle(origin, Vector3.one * 2f,
                    Quaternion.identity, origin.y), Is.Null,
                    "A saturated set of ignored bodies must not stop delivery forever.");
                blocker.enabled = true;
                Physics.SyncTransforms();
                Assert.That(cannery.FindTruckObstacle(origin, Vector3.one * 2f,
                    Quaternion.identity, origin.y), Is.SameAs(blocker),
                    "A real body cannot disappear behind a full scratch buffer.");
                blocker.enabled = false;
                Physics.SyncTransforms();
                Assert.That(cannery.FindTruckObstacle(origin, Vector3.one * 2f,
                    Quaternion.identity, origin.y), Is.Null,
                    "Reusing query storage cannot retain a disabled blocker.");
            }
            finally
            {
                Object.DestroyImmediate(external);
                Object.DestroyImmediate(owner);
                Physics.SyncTransforms();
            }
        }

        [Test]
        public void FirstActualDockArrivalStartsOnceAndSurvivesLeavingAndSceneIntervalsUntilNewGame()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            CityPortPlan port = CitySeacoastPlanner.Create(layout).Port;
            Assert.That(port, Is.Not.Null);
            Rect docks = port.LandBounds;
            Vector3 near = new Vector3(docks.center.x, port.QuayTopY, docks.center.y);
            Vector3 far = near + Vector3.right * 80f;
            foreach (var probe in new[]
            {
                (point: near, inside: true, name: "dock centre"),
                (point: new Vector3(docks.xMin + .01f, near.y, docks.yMin + .01f), inside: true, name: "inner southwest corner"),
                (point: new Vector3(docks.xMax - .01f, near.y, docks.yMax - .01f), inside: true, name: "inner northeast corner"),
                (point: new Vector3(docks.xMin - .01f, near.y, near.z), inside: false, name: "west of docks"),
                (point: new Vector3(docks.xMax + .01f, near.y, near.z), inside: false, name: "east of docks"),
                (point: new Vector3(near.x, near.y, docks.yMin - .01f), inside: false, name: "south of docks"),
                (point: new Vector3(near.x, near.y, docks.yMax + .01f), inside: false, name: "north of docks"),
                (point: near + Vector3.up * 2.99f, inside: true, name: "within upper height allowance"),
                (point: near - Vector3.up * 2.99f, inside: true, name: "within lower height allowance"),
                (point: near + Vector3.up * 3.01f, inside: false, name: "above docks"),
                (point: near - Vector3.up * 3.01f, inside: false, name: "below docks"),
                (point: far, inside: false, name: "distant presentation is not arrival")
            })
                Assert.That(port.IsAtDocks(probe.point), Is.EqualTo(probe.inside), probe.name);

            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(near)), Is.False, "Pre-wake cannot dispatch the port.");
            Assert.That(CityFishSupplySession.HasStarted, Is.False);
            Assert.That(CityFishSupplySession.Advance(false), Is.Zero);
            Assert.That(GameSessionState.TryStartGameTimeAt(23 * 60 + 50), Is.True);
            GameSessionState.AdvanceGameTime(40f);
            Assert.That(GameSessionState.GameDayIndex, Is.EqualTo(1), "The first visit need not happen on day one.");
            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(far)), Is.False);
            Assert.That(CityFishSupplySession.Advance(false), Is.Zero);
            Time.timeScale = 0f;
            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(near)), Is.False, "Pause cannot consume the first-arrival event.");
            Assert.That(CityFishSupplySession.HasStarted, Is.False);
            Time.timeScale = 1f;
            GameSessionState.AdvanceGameTime(3600f);
            Assert.That(CityFishSupplySession.Advance(1f), Is.Zero, "Unobserved calendar time must not advance the first vessel approach.");

            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(near)), Is.True);
            Assert.That(CityFishSupplySession.HasStarted, Is.True);
            Assert.That(CityFishSupplySession.WorkingSeconds, Is.Zero);
            Assert.That(CityFishSupplySession.Advance(false), Is.Zero, "Arrival sets the baseline to now, without catching up pre-visit time.");
            var cycle = new CityFishSupplyCycle(45d, 18d, 35d, 40d, 2d, 2d, 40d);
            CityFishSupplySnapshot first = cycle.Sample(CityFishSupplySession.WorkingSeconds);
            Assert.That(first.Stage, Is.EqualTo(CityFishSupplyStage.PortVisit));
            Assert.That(first.PortSeconds, Is.Zero);
            Assert.That(CityPortCycle.Sample(first.PortSeconds).Stage, Is.EqualTo(CityPortCycleStage.Approach));

            GameSessionState.AdvanceGameTime(10f);
            Assert.That(CityFishSupplySession.Advance(false), Is.EqualTo(10d));
            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(far)), Is.False);
            GameSessionState.AdvanceGameTime(25f);
            Assert.That(CityFishSupplySession.Advance(1f), Is.EqualTo(35d), "Leaving the docks does not freeze an established supply cycle.");
            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(near)), Is.False);
            Assert.That(CityFishSupplySession.Advance(false), Is.EqualTo(35d), "A repeated arrival cannot rewind the first approach.");
            GameSessionState.AdvanceGameTime(9f);
            Assert.That(CityFishSupplySession.Advance(true), Is.EqualTo(35d), "Physical handling/traffic blocks still own their elapsed interval.");
            GameSessionState.AdvanceGameTime(4f);
            Assert.That(CityFishSupplySession.Advance(.5f), Is.EqualTo(37d));

            // Exercise the session transition contract without composing a
            // second City: no port/controller samples exist in this interval.
            GameSessionState.EnterSupermarket();
            GameSessionState.AdvanceGameTime(1500f);
            GameSessionState.PrepareSupermarketReturn();
            CityPortPlan reconstructedPort = CitySeacoastPlanner.Create(layout).Port;
            Assert.That(CityFishSupplySession.TryStart(reconstructedPort.IsAtDocks(near)), Is.False);
            Assert.That(CityFishSupplySession.HasStarted, Is.True);
            Assert.That(CityFishSupplySession.Advance(false), Is.EqualTo(1537d), "Reconstruction includes all elapsed time outside City.");

            GameSessionState.BeginNewGame();
            Assert.That(CityFishSupplySession.HasStarted, Is.False);
            Assert.That(CityFishSupplySession.WorkingSeconds, Is.Zero);
            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(near)), Is.False);
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime(200f);
            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(far)), Is.False);
            Assert.That(CityFishSupplySession.Advance(false), Is.Zero);
            Assert.That(CityFishSupplySession.TryStart(port.IsAtDocks(near)), Is.True);
            Assert.That(CityFishSupplySession.Advance(false), Is.Zero);
            GameSessionState.AdvanceGameTime(3f);
            Assert.That(CityFishSupplySession.Advance(false), Is.EqualTo(3d), "The new game owns a fresh first-arrival clock.");
        }
    }
}
