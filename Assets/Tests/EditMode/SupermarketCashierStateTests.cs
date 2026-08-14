using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketCashierStateTests
    {
        private const float Step = 1f / 60f;

        private static void Simulate(
            SupermarketCashierSurveillanceState state,
            float distance,
            float lookDot,
            float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                state.Update(distance, lookDot, Step);
            }
        }

        [Test]
        public void Extension_PursuesAtEveryDistance()
        {
            var state = new SupermarketCashierSurveillanceState();

            // The face follows the hero anywhere in the shop: the
            // pursuit weight saturates near the counter and in the
            // farthest aisle alike.
            Simulate(state, 1.5f, -1f, 3f);
            Assert.That(state.Extension, Is.EqualTo(1f).Within(0.001f));

            state.Reset();
            Simulate(state, 12f, -1f, 3f);
            Assert.That(state.Extension, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Extension_GuiltyRetractIsFasterThanTheCreep()
        {
            var state = new SupermarketCashierSurveillanceState();

            float extendElapsed = 0f;
            while (state.Extension < 0.99f && extendElapsed < 10f)
            {
                state.Update(6f, -1f, Step);
                extendElapsed += Step;
            }

            float retractElapsed = 0f;
            while (state.Extension >
                   SupermarketCashierSurveillanceState
                       .StartleExtensionCap + 0.01f &&
                   retractElapsed < 10f)
            {
                state.Update(6f, 1f, Step);
                retractElapsed += Step;
            }

            Assert.That(extendElapsed, Is.GreaterThan(1f));
            Assert.That(
                retractElapsed,
                Is.LessThan(extendElapsed * 0.5f),
                "The guilty retract must be much faster than the " +
                "curious creep toward the hero.");
        }

        [Test]
        public void Startle_RequiresHeldGazeAndHeldEscape()
        {
            var state = new SupermarketCashierSurveillanceState();
            Simulate(state, 12f, -1f, 3f);

            state.Update(12f, 1f, 0.1f);
            Assert.That(
                state.IsStartled,
                Is.False,
                "A glance shorter than the enter hold must not " +
                "startle the cashier.");

            state.Update(12f, -1f, Step);
            Simulate(state, 12f, 1f, 0.3f);
            Assert.That(state.IsStartled, Is.True);
            Assert.That(state.ScanFrozen, Is.True);
            Assert.That(state.BlinkSuppressed, Is.True);

            state.Update(12f, -1f, 0.3f);
            Assert.That(
                state.IsStartled,
                Is.True,
                "Looking away shorter than the exit hold must not " +
                "release the startle.");

            Simulate(state, 12f, -1f, 1.0f);
            Assert.That(state.IsStartled, Is.False);
        }

        [Test]
        public void Startle_CapsExtensionAndReleasesSlowly()
        {
            var state = new SupermarketCashierSurveillanceState();
            Simulate(state, 12f, -1f, 3f);
            Assert.That(state.Extension, Is.EqualTo(1f).Within(0.001f));

            Simulate(state, 12f, 1f, 2f);
            Assert.That(
                state.Extension,
                Is.EqualTo(
                    SupermarketCashierSurveillanceState
                        .StartleExtensionCap).Within(0.001f));
            Assert.That(
                state.WideEyeScale,
                Is.EqualTo(
                    SupermarketCashierSurveillanceState
                        .WideEyeStartleScale).Within(0.01f));

            Simulate(state, 12f, -1f, 4f);
            Assert.That(state.IsStartled, Is.False);
            Assert.That(
                state.Extension,
                Is.GreaterThan(
                    SupermarketCashierSurveillanceState
                        .StartleExtensionCap),
                "After the hero looks away the periscope must creep " +
                "back up.");
            Assert.That(
                state.WideEyeScale,
                Is.EqualTo(
                    SupermarketCashierSurveillanceState
                        .WideEyeBaseScale).Within(0.01f));
        }

        [Test]
        public void BlinkSuppression_OutlastsStartleByResumeDelay()
        {
            var state = new SupermarketCashierSurveillanceState();
            Simulate(state, 12f, 1f, 0.5f);
            Assert.That(state.IsStartled, Is.True);

            Simulate(state, 12f, -1f, 1.0f);
            Assert.That(state.IsStartled, Is.False);
            Assert.That(
                state.BlinkSuppressed,
                Is.True,
                "Blinking must stay suppressed briefly after the " +
                "gaze breaks.");

            Simulate(
                state,
                12f,
                -1f,
                SupermarketCashierSurveillanceState
                    .BlinkResumeDelaySeconds + 0.1f);
            Assert.That(state.BlinkSuppressed, Is.False);
        }

        [Test]
        public void Update_IgnoresInvalidInputs()
        {
            var state = new SupermarketCashierSurveillanceState();
            Simulate(state, 12f, -1f, 2f);
            float extensionBefore = state.Extension;

            state.Update(float.NaN, 0f, Step);
            state.Update(5f, float.PositiveInfinity, Step);
            state.Update(5f, 0f, float.NaN);

            Assert.That(
                state.Extension,
                Is.EqualTo(extensionBefore));
        }

        [Test]
        public void Blink_WaitsOutTheLongCycleThenClosesAndOpens()
        {
            var blink = new SupermarketCashierBlinkState();

            AdvanceBlink(
                blink,
                SupermarketCashierBlinkState.BlinkStartSeconds - 0.1f,
                false);
            Assert.That(blink.Closure, Is.Zero);
            Assert.That(blink.EyesClosed, Is.False);

            AdvanceBlink(
                blink,
                0.1f + SupermarketCashierBlinkState.CloseDurationSeconds,
                false);
            Assert.That(blink.Closure, Is.EqualTo(1f).Within(0.01f));
            Assert.That(blink.EyesClosed, Is.True);

            AdvanceBlink(
                blink,
                SupermarketCashierBlinkState.HoldDurationSeconds * 0.5f,
                false);
            Assert.That(blink.EyesClosed, Is.True);

            AdvanceBlink(
                blink,
                (SupermarketCashierBlinkState.HoldDurationSeconds *
                 0.5f) +
                SupermarketCashierBlinkState.OpenDurationSeconds +
                0.02f,
                false);
            Assert.That(blink.Closure, Is.Zero.Within(0.001f));
        }

        [Test]
        public void Blink_CycleIsMuchRarerThanTheBusDrivers()
        {
            Assert.That(
                SupermarketCashierBlinkState.CycleDurationSeconds,
                Is.GreaterThan(6f));
            Assert.That(
                SupermarketCashierBlinkState.BlinkStartSeconds,
                Is.EqualTo(6.11f).Within(0.001f));
        }

        [Test]
        public void Blink_SuppressionRestartsTheStare()
        {
            var blink = new SupermarketCashierBlinkState();
            AdvanceBlink(
                blink,
                SupermarketCashierBlinkState.BlinkStartSeconds +
                SupermarketCashierBlinkState.CloseDurationSeconds,
                false);
            Assert.That(blink.EyesClosed, Is.True);

            blink.Advance(Step, true);
            Assert.That(blink.Closure, Is.Zero);
            Assert.That(blink.EyesClosed, Is.False);

            AdvanceBlink(
                blink,
                SupermarketCashierBlinkState.BlinkStartSeconds - 0.1f,
                false);
            Assert.That(
                blink.Closure,
                Is.Zero,
                "After suppression the full unbroken stare must run " +
                "again before the next blink.");
        }

        private static void AdvanceBlink(
            SupermarketCashierBlinkState blink,
            float seconds,
            bool suppressed)
        {
            int steps = Mathf.CeilToInt(seconds / Step);
            float remainder = seconds - ((steps - 1) * Step);
            for (int index = 0; index < steps - 1; index++)
            {
                blink.Advance(Step, suppressed);
            }

            blink.Advance(Mathf.Max(0f, remainder), suppressed);
        }
    }
}
