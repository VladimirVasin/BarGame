using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HawthornHealingPulseTests
    {
        [Test]
        public void SwallowCue_PauseRetriggerAndRecoveryKeepOneBoundedEnvelope()
        {
            var pulse = new HawthornHealingPulseTimeline();
            Assert.That(pulse.Sample(true), Is.EqualTo(Vector4.zero));
            pulse.Trigger();
            pulse.Advance(.18f, false);
            Vector4 peak = pulse.Sample(true);
            Assert.That(peak.x, Is.EqualTo(1f).Within(.0001f));
            pulse.Advance(20f, true);
            Assert.That(pulse.Sample(true), Is.EqualTo(peak));
            pulse.Advance(.82f, false);
            Vector4 recovery = pulse.Sample(true);
            Assert.That(recovery.x, Is.GreaterThan(0f).And.LessThan(peak.x));
            Assert.That(recovery.z, Is.Zero, "The lens settles before the colour.");
            pulse.Trigger();
            Assert.That(pulse.Elapsed, Is.Zero, "A second swallow replaces rather than stacks the cue.");
            Assert.That(pulse.Sample(true).x, Is.EqualTo(recovery.x), "Retrigger never flashes back to clear vision.");
            pulse.Advance(.18f, false);
            Assert.That(pulse.Sample(true), Is.EqualTo(peak));
            Vector4 noLens = pulse.Sample(false);
            Assert.That(noLens.x, Is.EqualTo(peak.x));
            Assert.That(noLens.z, Is.Zero);
            pulse.Advance(HawthornHealingPulseTimeline.DurationSeconds - .18f, false);
            Assert.That(pulse.Active, Is.False);
            Assert.That(pulse.Sample(true), Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void SwallowCue_ForeignCameraAndClearCannotLeavePresentationBehind()
        {
            var first = new GameObject("Swallow cue camera");
            var second = new GameObject("Unrelated camera");
            try
            {
                Camera owner = first.AddComponent<Camera>();
                Camera other = second.AddComponent<Camera>();
                HawthornHealingPulse.Trigger(owner);
                HawthornHealingPulse.Advance(.18f, false);
                Vector4 peak = HawthornHealingPulse.ForCamera(owner);
                Assert.That(peak.x, Is.GreaterThan(.99f));
                Assert.That(HawthornHealingPulse.ForCamera(other), Is.EqualTo(Vector4.zero));
                HawthornHealingPulse.Clear(other);
                Assert.That(HawthornHealingPulse.ForCamera(owner), Is.EqualTo(peak));
                HawthornHealingPulse.Clear(owner);
                Assert.That(HawthornHealingPulse.ForCamera(owner), Is.EqualTo(Vector4.zero));
                first.AddComponent<ScenePresentationExclusion>();
                HawthornHealingPulse.Trigger(owner);
                Assert.That(HawthornHealingPulse.ForCamera(owner), Is.EqualTo(Vector4.zero));
            }
            finally
            {
                HawthornHealingPulse.Clear();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void SwallowCue_ExtendedTailKeepsColourAndHeldEchoThenClearsExactly()
        {
            var pulse = new HawthornHealingPulseTimeline();
            pulse.Trigger();
            pulse.Advance(2.4f, false);
            Assert.That(pulse.Active, Is.True);
            Assert.That(pulse.Sample(true).x, Is.GreaterThan(0f));
            float early = HawthornAfterimageHistory.EvaluateStrength(.8f, .2f);
            float late = HawthornAfterimageHistory.EvaluateStrength(2.4f, .2f);
            Assert.That(late, Is.GreaterThan(early).And.LessThanOrEqualTo(.24f));
            Assert.That(HawthornAfterimageHistory.EvaluateStrength(2.4f, 0f), Is.Zero);
            Assert.That(HawthornAfterimageHistory.EvaluateStrength(2.4f,
                HawthornAfterimageHistory.CaptureIntervalSeconds), Is.Zero);
            Vector4 held = pulse.Sample(true);
            pulse.Advance(10f, true);
            Assert.That(pulse.Sample(true), Is.EqualTo(held));
            pulse.Advance(1.6f, false);
            Assert.That(pulse.Active, Is.False);
            Assert.That(pulse.Sample(true), Is.EqualTo(Vector4.zero));
            Assert.That(HawthornAfterimageHistory.EvaluateStrength(4f, .2f), Is.Zero);
        }
    }
}
