using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The drunk walk in isolation: nothing at all sober, wider and less
    /// even the drunker, landings that hold through the stance and lifts
    /// only in the swing, the same walk from the same seed, and no
    /// dependence on how finely time is chopped.
    /// </summary>
    public sealed class PlayerDrunkGaitModelTests
    {
        private const float Frame = 1f / 60f;
        private const float CyclesPerSecond = 1.4f;

        private static List<PlayerDrunkGaitPose> Walk(
            PlayerDrunkGaitModel model,
            float intoxication,
            float seconds,
            float deltaTime = Frame,
            float runBlend = 0f)
        {
            var poses = new List<PlayerDrunkGaitPose>();
            int frames = Mathf.RoundToInt(seconds / deltaTime);
            for (int frame = 0; frame < frames; frame++)
            {
                float cycle = Mathf.Repeat(frame * deltaTime * CyclesPerSecond, 1f);
                poses.Add(model.Advance(deltaTime, intoxication, cycle, true, runBlend, 1f));
            }

            return poses;
        }

        [Test]
        public void Sober_IsExactlyNoneAndDrawsNothing()
        {
            var sober = new PlayerDrunkGaitModel(21);
            foreach (PlayerDrunkGaitPose pose in Walk(sober, 0f, 6f))
            {
                Assert.That(pose.IsNone, Is.True);
                Assert.That(pose.CadenceMultiplier, Is.EqualTo(1f));
            }

            Assert.That(sober.LandingsDrawn, Is.Zero);

            // Six sober seconds spend none of the seed: the first drunk
            // landing after them is the fresh model's first landing.
            var fresh = new PlayerDrunkGaitModel(21);
            List<PlayerDrunkGaitPose> soberThenDrunk = Walk(sober, 1f, 2f);
            List<PlayerDrunkGaitPose> drunk = Walk(fresh, 1f, 2f);
            for (int index = 0; index < drunk.Count; index++)
            {
                Assert.That(soberThenDrunk[index].LeftFootOffsetLocal, Is.EqualTo(drunk[index].LeftFootOffsetLocal));
                Assert.That(soberThenDrunk[index].RightFootOffsetLocal, Is.EqualTo(drunk[index].RightFootOffsetLocal));
                Assert.That(soberThenDrunk[index].CadenceMultiplier, Is.EqualTo(drunk[index].CadenceMultiplier));
            }
        }

        [Test]
        public void NotWalkingForward_IsNone()
        {
            var model = new PlayerDrunkGaitModel(21);
            for (int frame = 0; frame < 120; frame++)
            {
                float cycle = Mathf.Repeat(frame * Frame * CyclesPerSecond, 1f);
                Assert.That(model.Advance(Frame, 1f, cycle, false, 0f, 1f).IsNone, Is.True, "a turn or a backpedal is not the walk");
                Assert.That(model.Advance(Frame, 1f, cycle, true, 1f, 1f).IsNone, Is.True, "a run is not disordered");
                Assert.That(model.Advance(Frame, 1f, cycle, true, 0f, 0f).IsNone, Is.True, "standing still is not disordered");
            }

            Assert.That(model.LandingsDrawn, Is.Zero);
        }

        [Test]
        public void Width_GrowsWithTheLevel()
        {
            float Width(float intoxication)
            {
                var model = new PlayerDrunkGaitModel(7);
                float sum = 0f;
                int count = 0;
                foreach (PlayerDrunkGaitPose pose in Walk(model, intoxication, 20f))
                {
                    sum += Mathf.Abs(pose.LeftFootOffsetLocal.x) + Mathf.Abs(pose.RightFootOffsetLocal.x);
                    count += 2;
                }

                return sum / count;
            }

            float mild = Width(0.3f);
            float blind = Width(1f);
            Assert.That(mild, Is.GreaterThan(0.02f));
            Assert.That(blind, Is.GreaterThan(mild + 0.04f), "blind drunk the stance is a hand wider");
            Assert.That(blind, Is.LessThanOrEqualTo(PlayerDrunkGaitRules.LateralClampMetres));
        }

        [Test]
        public void Landings_AreOutwardMostOfTheTimeAndSometimesCrossed()
        {
            var model = new PlayerDrunkGaitModel(3);
            int leftOutward = 0;
            int leftCrossed = 0;
            float previousLeft = 0f;
            foreach (PlayerDrunkGaitPose pose in Walk(model, 1f, 60f))
            {
                // Count each new left landing once, as its offset settles.
                if (Mathf.Abs(pose.LeftFootOffsetLocal.x - previousLeft) > 0.0001f)
                {
                    previousLeft = pose.LeftFootOffsetLocal.x;
                    continue;
                }

                if (Mathf.Abs(previousLeft) > 0.001f)
                {
                    if (previousLeft < 0f)
                    {
                        leftOutward++;
                    }
                    else
                    {
                        leftCrossed++;
                    }
                }
            }

            Assert.That(leftOutward, Is.GreaterThan(leftCrossed * 3), "the left boot lands to the left far more often than across");
            Assert.That(leftCrossed, Is.GreaterThan(0), "and now and then it crosses");
        }

        [Test]
        public void Timing_VariesAtFullAndNotSober()
        {
            float Variation(float intoxication)
            {
                var model = new PlayerDrunkGaitModel(11);
                var multipliers = new List<float>();
                float previous = float.NaN;
                foreach (PlayerDrunkGaitPose pose in Walk(model, intoxication, 30f))
                {
                    if (pose.CadenceMultiplier != previous)
                    {
                        previous = pose.CadenceMultiplier;
                        multipliers.Add(previous);
                    }
                }

                if (multipliers.Count < 2)
                {
                    return 0f;
                }

                float mean = 0f;
                foreach (float value in multipliers)
                {
                    mean += value;
                }

                mean /= multipliers.Count;
                float variance = 0f;
                foreach (float value in multipliers)
                {
                    variance += (value - mean) * (value - mean);
                }

                return Mathf.Sqrt(variance / multipliers.Count) / mean;
            }

            Assert.That(Variation(1f), Is.GreaterThan(0.1f), "blind drunk the half-steps come uneven");
            Assert.That(Variation(0f), Is.Zero);
        }

        [Test]
        public void Offset_HoldsThroughTheStanceAndLiftsOnlyInTheSwing()
        {
            var model = new PlayerDrunkGaitModel(5);
            Walk(model, 1f, 3f);
            // Drive the cycle by hand through one full turn from the left
            // swing's start and watch the left boot.
            float previousOffsetX = float.NaN;
            for (int step = 0; step <= 200; step++)
            {
                float cycle = Mathf.Repeat(PlayerDrunkGaitRules.LeftSwingStart + step / 200f, 1f);
                PlayerDrunkGaitPose pose = model.Advance(Frame, 1f, cycle, true, 0f, 1f);
                float progress = PlayerDrunkGaitRules.SwingProgress(cycle, PlayerDrunkGaitRules.LeftSwingStart);
                if (progress >= 1f)
                {
                    // Stance: the landing holds and the boot is down.
                    if (!float.IsNaN(previousOffsetX))
                    {
                        Assert.That(pose.LeftFootOffsetLocal.x, Is.EqualTo(previousOffsetX).Within(0.000001f));
                    }

                    Assert.That(pose.LeftFootLift, Is.Zero);
                    previousOffsetX = pose.LeftFootOffsetLocal.x;
                }
                else if (progress > 0.3f && progress < 0.7f)
                {
                    Assert.That(pose.LeftFootLift, Is.GreaterThanOrEqualTo(0f));
                    previousOffsetX = float.NaN;
                }
            }
        }

        [Test]
        public void ToesTurnOut_LeftAndRightOpposite()
        {
            var model = new PlayerDrunkGaitModel(9);
            float leftMin = 0f;
            float rightMax = 0f;
            foreach (PlayerDrunkGaitPose pose in Walk(model, 1f, 10f))
            {
                leftMin = Mathf.Min(leftMin, pose.LeftFootYawDegrees);
                rightMax = Mathf.Max(rightMax, pose.RightFootYawDegrees);
            }

            Assert.That(leftMin, Is.LessThan(-4f), "the left toes turn out (counter-clockwise from above)");
            Assert.That(rightMax, Is.GreaterThan(4f), "the right toes turn out (clockwise from above)");
        }

        [Test]
        public void Walk_IsDeterministicAndIndependentOfTheChunking()
        {
            var first = new PlayerDrunkGaitModel(13);
            var second = new PlayerDrunkGaitModel(13);
            // Step the two side by side so a diverging draw is named at the
            // frame it happens.
            var fine = new List<PlayerDrunkGaitPose>();
            var coarse = new List<PlayerDrunkGaitPose>();
            for (int frame = 0; frame < 8 * 60; frame++)
            {
                float cycle = Mathf.Repeat(frame * Frame * CyclesPerSecond, 1f);
                fine.Add(first.Advance(Frame, 0.8f, cycle, true, 0f, 1f));
                if (frame % 2 == 0)
                {
                    float coarseCycle = Mathf.Repeat((frame / 2) * (Frame * 2f) * CyclesPerSecond, 1f);
                    coarse.Add(second.Advance(Frame * 2f, 0.8f, coarseCycle, true, 0f, 1f));
                    Assert.That(
                        second.LandingsDrawn,
                        Is.EqualTo(first.LandingsDrawn).Within(1),
                        $"frame {frame}: draws diverged (fine {first.LandingsDrawn}, coarse {second.LandingsDrawn})");
                    if (second.LandingsDrawn == first.LandingsDrawn)
                    {
                        Assert.That(
                            second.DebugLeftTarget,
                            Is.EqualTo(first.DebugLeftTarget),
                            $"frame {frame}: left landing differs after {first.LandingsDrawn} draws");
                        Assert.That(
                            second.DebugRightTarget,
                            Is.EqualTo(first.DebugRightTarget),
                            $"frame {frame}: right landing differs after {first.LandingsDrawn} draws");
                    }
                }
            }

            // The draws — every landing and every half-step's cadence — are
            // the same whatever the frame rate; only the easing between
            // them samples at different moments.
            Assert.That(first.LandingsDrawn, Is.EqualTo(second.LandingsDrawn));
            Assert.That(first.LandingsDrawn, Is.GreaterThan(15));
            Assert.That(
                coarse[coarse.Count - 1].CadenceMultiplier,
                Is.EqualTo(fine[fine.Count - 2].CadenceMultiplier).Within(0.0001f));

            var third = new PlayerDrunkGaitModel(13);
            List<PlayerDrunkGaitPose> again = Walk(third, 0.8f, 8f);
            for (int index = 0; index < fine.Count; index++)
            {
                Assert.That(again[index].LeftFootOffsetLocal, Is.EqualTo(fine[index].LeftFootOffsetLocal));
                Assert.That(again[index].RightFootYawDegrees, Is.EqualTo(fine[index].RightFootYawDegrees));
                Assert.That(again[index].PelvisRollDegrees, Is.EqualTo(fine[index].PelvisRollDegrees));
            }
        }

        [Test]
        public void Rules_CrossingWrapsAndSwingProgressIsOneInStance()
        {
            Assert.That(PlayerDrunkGaitRules.Crossed(0.2f, 0.3f, 0.25f), Is.True);
            Assert.That(PlayerDrunkGaitRules.Crossed(0.3f, 0.2f, 0.25f), Is.False, "the cycle only runs forward");
            Assert.That(PlayerDrunkGaitRules.Crossed(0.95f, 0.05f, 0f), Is.True, "the wrap counts");
            Assert.That(PlayerDrunkGaitRules.Crossed(0.7f, 0.8f, 0.75f), Is.True);
            Assert.That(PlayerDrunkGaitRules.Crossed(0.7f, 0.72f, 0.75f), Is.False);
            Assert.That(PlayerDrunkGaitRules.SwingProgress(0.25f, 0.25f), Is.Zero);
            Assert.That(PlayerDrunkGaitRules.SwingProgress(0.5f, 0.25f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(PlayerDrunkGaitRules.SwingProgress(0.8f, 0.25f), Is.EqualTo(1f));
            Assert.That(PlayerDrunkGaitRules.SwingProgress(0.1f, 0.75f), Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(PlayerDrunkGaitRules.SwingProgress(0.5f, 0.75f), Is.EqualTo(1f));
        }
    }
}
