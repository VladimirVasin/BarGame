using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerViewDirectionSelectorTests
    {
        [TestCase(0f, PlayerViewDirection.Front)]
        [TestCase(45f, PlayerViewDirection.FrontRight)]
        [TestCase(90f, PlayerViewDirection.Right)]
        [TestCase(135f, PlayerViewDirection.BackRight)]
        [TestCase(180f, PlayerViewDirection.Back)]
        [TestCase(225f, PlayerViewDirection.BackLeft)]
        [TestCase(270f, PlayerViewDirection.Left)]
        [TestCase(315f, PlayerViewDirection.FrontLeft)]
        public void NearestDirection_MapsAllSectorCenters(
            float angleDegrees,
            PlayerViewDirection expected)
        {
            Assert.That(
                PlayerViewDirectionSelector.GetNearestDirection(
                    angleDegrees),
                Is.EqualTo(expected));
        }

        [TestCase(-360f, PlayerViewDirection.Front)]
        [TestCase(-45f, PlayerViewDirection.FrontLeft)]
        [TestCase(-1f, PlayerViewDirection.Front)]
        [TestCase(359f, PlayerViewDirection.Front)]
        [TestCase(360f, PlayerViewDirection.Front)]
        [TestCase(405f, PlayerViewDirection.FrontRight)]
        [TestCase(810f, PlayerViewDirection.Right)]
        public void NearestDirection_WrapsAngles(
            float angleDegrees,
            PlayerViewDirection expected)
        {
            Assert.That(
                PlayerViewDirectionSelector.GetNearestDirection(
                    angleDegrees),
                Is.EqualTo(expected));
        }

        [Test]
        public void Select_DirectJumpChoosesNearestSector()
        {
            var selector = new PlayerViewDirectionSelector();

            Assert.That(
                selector.Select(0f),
                Is.EqualTo(PlayerViewDirection.Front));
            Assert.That(
                selector.Select(180f),
                Is.EqualTo(PlayerViewDirection.Back));
        }

        [Test]
        public void Select_HoldsAtInclusiveHysteresisBoundaries()
        {
            var selector = new PlayerViewDirectionSelector();

            selector.Select(0f);

            Assert.That(
                selector.Select(27.5f),
                Is.EqualTo(PlayerViewDirection.Front));
            Assert.That(
                selector.Select(-27.5f),
                Is.EqualTo(PlayerViewDirection.Front));
        }

        [Test]
        public void Select_LeavingHysteresisBandChoosesNearestSector()
        {
            var selector = new PlayerViewDirectionSelector();

            selector.Select(0f);

            Assert.That(
                selector.Select(27.501f),
                Is.EqualTo(PlayerViewDirection.FrontRight));
        }

        [Test]
        public void Select_UsesReverseThresholdAfterDirectionChanges()
        {
            var selector = new PlayerViewDirectionSelector();

            selector.Select(0f);
            selector.Select(27.501f);

            Assert.That(
                selector.Select(17.5f),
                Is.EqualTo(PlayerViewDirection.FrontRight));
            Assert.That(
                selector.Select(17.499f),
                Is.EqualTo(PlayerViewDirection.Front));
        }

        [Test]
        public void Reset_UsesSpecifiedDirectionForRetention()
        {
            var selector = new PlayerViewDirectionSelector(
                initialDirection: PlayerViewDirection.FrontLeft);

            Assert.That(
                selector.CurrentDirection,
                Is.EqualTo(PlayerViewDirection.FrontLeft));

            selector.Reset(PlayerViewDirection.Front);

            Assert.That(
                selector.CurrentDirection,
                Is.EqualTo(PlayerViewDirection.Front));
            Assert.That(
                selector.Select(27.5f),
                Is.EqualTo(PlayerViewDirection.Front));
        }
    }
}
