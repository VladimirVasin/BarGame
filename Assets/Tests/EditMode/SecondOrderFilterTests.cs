using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The limb-inertia filter in isolation: an under-damped channel
    /// overshoots and settles, a critically damped one never overshoots,
    /// zero stays bit-exactly zero, a residual snaps to zero, and the
    /// integration does not depend on how the time is chunked.
    /// </summary>
    public sealed class SecondOrderFilterTests
    {
        [Test]
        public void UnderDamped_OvershootsThenSettles()
        {
            // The arm filter: zeta 0.45 rings once past the mark by about
            // a fifth (exp(-pi*zeta/sqrt(1-zeta^2)) = 0.205) and is done
            // inside two seconds.
            var filter = new SecondOrderFilter(9f, 0.45f);
            float peak = 0f;
            float peakTime = 0f;
            const float step = 1f / 120f;
            for (int tick = 1; tick <= 240; tick++)
            {
                float value = filter.Advance(1f, step);
                if (value > peak)
                {
                    peak = value;
                    peakTime = tick * step;
                }
            }

            Assert.That(peak, Is.GreaterThan(1.15f).And.LessThan(1.25f));
            Assert.That(peakTime, Is.LessThan(0.5f));
            Assert.That(filter.Value, Is.EqualTo(1f).Within(0.01f));
            Assert.That(filter.Velocity, Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void CriticallyDamped_NeverOvershoots()
        {
            var filter = new SecondOrderFilter(10f, 1f);
            for (int tick = 0; tick < 240; tick++)
            {
                float value = filter.Advance(1f, 1f / 60f);
                Assert.That(value, Is.LessThanOrEqualTo(1f + 1e-4f), "tick " + tick);
                Assert.That(value, Is.GreaterThanOrEqualTo(0f), "tick " + tick);
            }

            Assert.That(filter.Value, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void ZeroFromZero_IsExactlyZero()
        {
            var filter = new SecondOrderFilter(14f, 0.8f);
            for (int tick = 0; tick < 1000; tick++)
            {
                float value = filter.Advance(0f, 1f / 60f);
                Assert.That(value, Is.EqualTo(0f), "tick " + tick);
            }

            Assert.That(filter.Value, Is.EqualTo(0f));
            Assert.That(filter.Velocity, Is.EqualTo(0f));
        }

        [Test]
        public void TinyResidual_SnapsToExactZero()
        {
            var filter = new SecondOrderFilter(14f, 0.8f);
            filter.Reset(SecondOrderFilter.SnapEpsilon * 0.5f);
            Assert.That(filter.Value, Is.Not.EqualTo(0f));

            float value = filter.Advance(0f, 1f / 60f);

            Assert.That(value, Is.EqualTo(0f));
            Assert.That(filter.Value, Is.EqualTo(0f));
            Assert.That(filter.Velocity, Is.EqualTo(0f));
        }

        [Test]
        public void DecayToZero_EndsExactlyZero()
        {
            // A channel that was busy and is now asked for zero decays,
            // and once inside the epsilon it reads exactly zero rather
            // than an ever-smaller residual: the drunk who sobered up is
            // bit-for-bit still again.
            var filter = new SecondOrderFilter(9f, 0.45f);
            for (int tick = 0; tick < 60; tick++)
            {
                filter.Advance(30f, 1f / 60f);
            }

            Assert.That(filter.Value, Is.GreaterThan(20f));
            for (int tick = 0; tick < 600; tick++)
            {
                filter.Advance(0f, 1f / 60f);
            }

            Assert.That(filter.Value, Is.EqualTo(0f));
            Assert.That(filter.Velocity, Is.EqualTo(0f));
        }

        [Test]
        public void NonPositiveDelta_LeavesStateUntouched()
        {
            var filter = new SecondOrderFilter(9f, 0.45f);
            filter.Advance(1f, 0.1f);
            float before = filter.Value;
            float velocityBefore = filter.Velocity;
            Assert.That(before, Is.GreaterThan(0f));

            Assert.That(filter.Advance(5f, 0f), Is.EqualTo(before));
            Assert.That(filter.Advance(5f, -1f), Is.EqualTo(before));
            Assert.That(filter.Advance(5f, float.NaN), Is.EqualTo(before));
            Assert.That(filter.Advance(float.NaN, 0.1f), Is.EqualTo(before));
            Assert.That(filter.Value, Is.EqualTo(before));
            Assert.That(filter.Velocity, Is.EqualTo(velocityBefore));
        }

        [Test]
        public void LongDelta_IsSubSteppedIdenticallyToSmallOnes()
        {
            // A quarter second in one call walks the same 0.01 s sub-steps
            // as twenty-five calls of 0.01 s, so a dropped frame and a
            // steady one agree.
            var whole = new SecondOrderFilter(14f, 0.8f);
            var pieces = new SecondOrderFilter(14f, 0.8f);

            whole.Advance(1f, 0.25f);
            for (int piece = 0; piece < 25; piece++)
            {
                pieces.Advance(1f, 0.01f);
            }

            Assert.That(whole.Value, Is.EqualTo(pieces.Value).Within(1e-5f));
            Assert.That(whole.Value, Is.GreaterThan(0.5f).And.LessThan(1.1f));
        }

        [Test]
        public void Chunking_30Vs120Hz_AgreeOnAStep()
        {
            // The same held target walked at two frame rates: the
            // sub-stepping makes the integration the same, only the
            // last sub-step's length differs.
            var thirty = new SecondOrderFilter(9f, 0.45f);
            var oneTwenty = new SecondOrderFilter(9f, 0.45f);
            float worst = 0f;
            for (int tick = 0; tick < 60; tick++)
            {
                thirty.Advance(1f, 1f / 30f);
                for (int sub = 0; sub < 4; sub++)
                {
                    oneTwenty.Advance(1f, 1f / 120f);
                }

                worst = Mathf.Max(worst, Mathf.Abs(thirty.Value - oneTwenty.Value));
            }

            Assert.That(worst, Is.LessThan(0.02f));
            Assert.That(thirty.Value, Is.EqualTo(1f).Within(0.02f));
        }

        [Test]
        public void Reset_PutsTheChannelAtRestAtTheValue()
        {
            var filter = new SecondOrderFilter(9f, 0.45f);
            filter.Advance(1f, 0.2f);
            filter.Reset(3f);

            Assert.That(filter.Value, Is.EqualTo(3f));
            Assert.That(filter.Velocity, Is.EqualTo(0f));
            Assert.That(filter.Advance(3f, 1f / 60f), Is.EqualTo(3f).Within(1e-6f));
        }

        [Test]
        public void Constructor_ClampsDegenerateParameters()
        {
            var filter = new SecondOrderFilter(0f, -1f);
            Assert.That(filter.AngularFrequency, Is.GreaterThan(0f));
            Assert.That(filter.DampingRatio, Is.EqualTo(0f));
        }
    }
}
