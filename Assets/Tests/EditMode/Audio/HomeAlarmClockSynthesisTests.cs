using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeAlarmClockSynthesisTests
    {
        [Test]
        public void MechanicalLoop_IsDeterministicMonoAndSeamSafe()
        {
            float[] first =
                HomeAlarmClockSynthesis
                    .GenerateMechanicalAlarmLoopSamples();
            float[] second =
                HomeAlarmClockSynthesis
                    .GenerateMechanicalAlarmLoopSamples();

            Assert.That(
                HomeAlarmClockSynthesis.SampleRate,
                Is.EqualTo(22050));
            Assert.That(
                HomeAlarmClockSynthesis.Channels,
                Is.EqualTo(1));
            Assert.That(
                first,
                Has.Length.EqualTo(
                    Mathf.RoundToInt(
                        HomeAlarmClockSynthesis.SampleRate *
                        HomeAlarmClockSynthesis.LoopDuration)));
            CollectionAssert.AreEqual(first, second);

            float peak = 0f;
            double sumSquares = 0d;
            int zeroCrossings = 0;
            for (int index = 0; index < first.Length; index++)
            {
                float sample = first[index];
                Assert.That(float.IsNaN(sample), Is.False);
                Assert.That(float.IsInfinity(sample), Is.False);
                peak = Mathf.Max(peak, Mathf.Abs(sample));
                sumSquares += sample * sample;

                if (index > 0 &&
                    (sample >= 0f) != (first[index - 1] >= 0f))
                {
                    zeroCrossings++;
                }
            }

            float rms = Mathf.Sqrt(
                (float)(sumSquares / first.Length));
            Assert.That(peak, Is.InRange(0.12f, 0.86f));
            Assert.That(rms, Is.InRange(0.025f, 0.30f));
            Assert.That(zeroCrossings, Is.GreaterThan(1000));
            Assert.That(
                first[first.Length - 1],
                Is.EqualTo(first[0]).Within(0.000001f));
        }
    }
}
