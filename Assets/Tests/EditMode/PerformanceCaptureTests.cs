using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PerformanceCaptureTests
    {
        [Test]
        public void CommandLineRequiresExplicitSceneAndPreservesScenarioBudget()
        {
            Assert.That(PerformanceCaptureOptions.FromArguments(new[] { "-runTests" }), Is.Null);
            PerformanceCaptureOptions options = PerformanceCaptureOptions.FromArguments(new[]
            {
                "-bp-perf-scene=City", "-bp-perf-label", "4k-walk",
                "-bp-perf-seconds=45", "-bp-perf-warmup", "8", "-bp-perf-target-fps=60"
            });
            Assert.That(options.Scene, Is.EqualTo("City"));
            Assert.That(options.Label, Is.EqualTo("4k-walk"));
            Assert.That(options.DurationSeconds, Is.EqualTo(45));
            Assert.That(options.WarmupSeconds, Is.EqualTo(8));
            Assert.That(options.TargetFramesPerSecond, Is.EqualTo(60));
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("0")]
        [TestCase("121")]
        public void CaptureRejectsNonFiniteOrUnboundedDuration(string duration)
        {
            Assert.That(() => PerformanceCaptureOptions.FromArguments(new[]
            {
                "-bp-perf-scene=City", "-bp-perf-seconds=" + duration
            }), Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void BoundedSamplesPreserveHitchesAndDoNotInventUnavailableGpuTime()
        {
            var samples = new PerformanceCaptureSamples(100);
            for (int frame = 1; frame <= 100; frame++)
            {
                samples.Add(PerformanceCaptureSamples.FrameInterval, frame <= 95 ? 10 : 100);
                samples.Add(PerformanceCaptureSamples.FootBake, 0);
                samples.Add(PerformanceCaptureSamples.Gpu, -1);
            }
            samples.Add(PerformanceCaptureSamples.FrameInterval, 9000);
            samples.Add(PerformanceCaptureSamples.Gpu, double.NaN);
            PerformanceDistribution frames = samples.Summarize(PerformanceCaptureSamples.FrameInterval);
            Assert.That(samples.IsFull, Is.True);
            Assert.That(frames.sampleCount, Is.EqualTo(100));
            Assert.That(frames.mean, Is.EqualTo(14.5));
            Assert.That(frames.p50, Is.EqualTo(10));
            Assert.That(frames.p95, Is.EqualTo(10));
            Assert.That(frames.p99, Is.EqualTo(100));
            Assert.That(frames.maximum, Is.EqualTo(100));
            Assert.That(samples.Summarize(PerformanceCaptureSamples.Gpu).sampleCount, Is.Zero);
            PerformanceDistribution feet = samples.Summarize(PerformanceCaptureSamples.FootBake);
            Assert.That(feet.sampleCount, Is.EqualTo(100), "Measured zero is distinct from unavailable.");
            Assert.That(feet.maximum, Is.Zero);
        }
    }
}
