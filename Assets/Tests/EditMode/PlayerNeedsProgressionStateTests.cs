using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerNeedsProgressionStateTests
    {
        [TestCase(360d, 25, 33)]
        [TestCase(720d, 50, 66)]
        [TestCase(1080d, 75, 100)]
        [TestCase(1440d, 100, 100)]
        public void Advance_ReachesPlannedCheckpoints(
            double elapsedGameMinutes,
            int expectedHunger,
            int expectedFatigue)
        {
            PlayerNeedsProgressionState state =
                new PlayerNeedsProgressionState();

            state.Advance(elapsedGameMinutes);

            Assert.That(state.HungerLevel, Is.EqualTo(expectedHunger));
            Assert.That(state.FatigueLevel, Is.EqualTo(expectedFatigue));
        }

        [Test]
        public void Advance_IsIndependentOfChunkSize()
        {
            const double totalGameMinutes = 713.75d;
            const double chunkGameMinutes = 0.25d;
            PlayerNeedsProgressionState singleStep =
                new PlayerNeedsProgressionState();
            PlayerNeedsProgressionState chunked =
                new PlayerNeedsProgressionState();

            singleStep.Advance(totalGameMinutes);
            for (int index = 0;
                 index < totalGameMinutes / chunkGameMinutes;
                 index++)
            {
                chunked.Advance(chunkGameMinutes);
            }

            Assert.That(
                chunked.HungerLevel,
                Is.EqualTo(singleStep.HungerLevel));
            Assert.That(
                chunked.FatigueLevel,
                Is.EqualTo(singleStep.FatigueLevel));
        }

        [Test]
        public void Advance_IgnoresInvalidElapsedTime()
        {
            PlayerNeedsProgressionState state =
                new PlayerNeedsProgressionState();
            state.SetHunger(12);
            state.SetFatigue(34);

            state.Advance(0d);
            state.Advance(-1d);
            state.Advance(double.NaN);
            state.Advance(double.PositiveInfinity);
            state.Advance(double.NegativeInfinity);

            Assert.That(state.HungerLevel, Is.EqualTo(12));
            Assert.That(state.FatigueLevel, Is.EqualTo(34));
        }

        [Test]
        public void Advance_SaturatesWithoutRetainingBacklog()
        {
            PlayerNeedsProgressionState state =
                new PlayerNeedsProgressionState();
            state.SetHunger(99);
            state.SetFatigue(99);

            state.Advance(1000000d);

            Assert.That(state.HungerLevel, Is.EqualTo(100));
            Assert.That(state.FatigueLevel, Is.EqualTo(100));

            state.SetHunger(50);
            state.SetFatigue(50);
            state.Advance(5d);

            Assert.That(state.HungerLevel, Is.EqualTo(50));
            Assert.That(state.FatigueLevel, Is.EqualTo(50));
        }

        [Test]
        public void Reset_ClearsVisibleAndFractionalProgress()
        {
            PlayerNeedsProgressionState state =
                new PlayerNeedsProgressionState();
            state.Advance(7.3d);

            state.Reset();
            state.Advance(7.3d);

            Assert.That(state.HungerLevel, Is.Zero);
            Assert.That(state.FatigueLevel, Is.Zero);

            state.Advance(7.2d);

            Assert.That(state.HungerLevel, Is.EqualTo(1));
            Assert.That(state.FatigueLevel, Is.EqualTo(1));
        }
    }
}
