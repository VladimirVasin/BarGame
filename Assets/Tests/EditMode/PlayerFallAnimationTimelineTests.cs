using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerFallAnimationTimelineTests
    {
        [Test]
        public void FrameBudget_MatchesFullBalanceFallAtThirtyFps()
        {
            Assert.That(
                PlayerFallAnimationTimeline.FallingFrameCount,
                Is.EqualTo(14));
            Assert.That(
                PlayerFallAnimationTimeline.DownFrameCount,
                Is.EqualTo(36));
            Assert.That(
                PlayerFallAnimationTimeline.RisingFrameCount,
                Is.EqualTo(30));
            Assert.That(
                PlayerFallAnimationTimeline.FrameCount,
                Is.EqualTo(80));
        }

        [TestCase(PlayerFallAnimationPhase.None, 0f, -1)]
        [TestCase(PlayerFallAnimationPhase.Falling, -1f, 0)]
        [TestCase(PlayerFallAnimationPhase.Falling, 0f, 0)]
        [TestCase(PlayerFallAnimationPhase.Falling, 1f, 13)]
        [TestCase(PlayerFallAnimationPhase.Down, 0f, 14)]
        [TestCase(PlayerFallAnimationPhase.Down, 1f, 49)]
        [TestCase(PlayerFallAnimationPhase.Rising, 0f, 50)]
        [TestCase(PlayerFallAnimationPhase.Rising, 1f, 79)]
        [TestCase(PlayerFallAnimationPhase.Rising, 2f, 79)]
        public void EvaluateFrame_MapsEveryPhaseToItsAuthoredRange(
            PlayerFallAnimationPhase phase,
            float progress,
            int expectedFrame)
        {
            Assert.That(
                PlayerFallAnimationTimeline.EvaluateFrame(
                    phase,
                    progress),
                Is.EqualTo(expectedFrame));
        }

        [Test]
        public void FallAtlasPaths_CoverBothPhysicalVariantsForAllViews()
        {
            Array directions = Enum.GetValues(
                typeof(PlayerViewDirection));
            Assert.That(
                directions.Length,
                Is.EqualTo(PlayerSpriteRig.DirectionCount));

            foreach (PlayerViewDirection direction in directions)
            {
                string leftPath =
                    PlayerSpriteRig.GetFallAtlasResourcePath(
                        direction,
                        -1f);
                string rightPath =
                    PlayerSpriteRig.GetFallAtlasResourcePath(
                        direction,
                        1f);

                Assert.That(leftPath, Does.Contain(direction.ToString()));
                Assert.That(leftPath, Does.EndWith("ScreenLeftAtlas"));
                Assert.That(rightPath, Does.Contain(direction.ToString()));
                Assert.That(rightPath, Does.EndWith("ScreenRightAtlas"));
                Assert.That(rightPath, Is.Not.EqualTo(leftPath));
            }
        }
    }
}
