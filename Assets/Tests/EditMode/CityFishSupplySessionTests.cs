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
