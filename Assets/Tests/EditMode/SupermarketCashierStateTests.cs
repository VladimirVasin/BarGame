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

        /// <summary>
        /// The face follows the hero across the shop, but it comes HOME as
        /// he walks up. At the counter the periscope is gone entirely and
        /// he is just a tall man behind a till; from the far aisle he is
        /// craning the whole way.
        ///
        /// This replaces an earlier contract that asserted the pursuit
        /// weight saturated at `1.5 m` and `12 m` alike. That was the
        /// design until 2026-09-02; the retract is deliberate, not a
        /// regression.
        /// </summary>
        [Test]
        public void Extension_ComesHomeAsTheHeroWalksUp()
        {
            var state = new SupermarketCashierSurveillanceState();

            Simulate(state, 12f, -1f, 4f);
            Assert.That(
                state.Extension,
                Is.EqualTo(1f).Within(0.001f),
                "From the far aisle he must be at full stretch.");

            // Now the hero closes to the till and the neck comes in.
            Simulate(
                state,
                SupermarketCashierSurveillanceState
                    .CloseRetractFullMeters - 0.3f,
                -1f,
                4f);
            Assert.That(
                state.Extension,
                Is.EqualTo(0f).Within(0.001f),
                "At the counter the neck must be an ordinary neck.");

            // And pays back out as he leaves.
            Simulate(
                state,
                SupermarketCashierSurveillanceState
                    .CloseRetractReleaseMeters + 1f,
                -1f,
                4f);
            Assert.That(
                state.Extension,
                Is.EqualTo(1f).Within(0.001f),
                "Backing away must pay the neck out again.");
        }

        /// <summary>
        /// The band between the two thresholds is a ramp, not a switch -
        /// otherwise the neck would snap home the instant the hero crossed
        /// a line, which is the thing the whole chain was smoothed to
        /// avoid.
        /// </summary>
        [Test]
        public void Extension_RampsAcrossTheCloseBandRatherThanSnapping()
        {
            float near = SupermarketCashierSurveillanceState
                .CloseRetractFullMeters;
            float far = SupermarketCashierSurveillanceState
                .CloseRetractReleaseMeters;
            Assert.That(
                far,
                Is.GreaterThan(near + 0.5f),
                "The close band is too narrow to ramp across.");

            var state = new SupermarketCashierSurveillanceState();
            Simulate(state, (near + far) * 0.5f, -1f, 6f);
            Assert.That(
                state.Extension,
                Is.InRange(0.2f, 0.8f),
                "Mid-band the neck must be part way out, not at either " +
                "end.");
        }

        /// <summary>
        /// Being caught at the counter must not pay the neck back OUT to
        /// the startle cap. The more retracted of the two wins.
        /// </summary>
        [Test]
        public void Startle_AtTheCounterDoesNotExtendHim()
        {
            var state = new SupermarketCashierSurveillanceState();
            float close = SupermarketCashierSurveillanceState
                .CloseRetractFullMeters - 0.3f;
            Simulate(state, close, -1f, 4f);
            Assert.That(state.Extension, Is.EqualTo(0f).Within(0.001f));

            Simulate(state, close, 1f, 2f);
            Assert.That(state.IsStartled, Is.True);
            Assert.That(
                state.Extension,
                Is.LessThanOrEqualTo(
                    SupermarketCashierSurveillanceState
                        .StartleExtensionCap),
                "A startle at the till must never push the neck out.");
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
            Assert.That(
                state.IsStartled,
                Is.True,
                "The exit hold alone no longer releases him: the " +
                "cooldown has to burn down first.");

            Simulate(
                state,
                12f,
                -1f,
                SupermarketCashierSurveillanceState
                    .StartleCooldownSeconds);
            Assert.That(state.IsStartled, Is.False);
        }

        /// <summary>
        /// Being caught has to cost him a few seconds of staying pulled in.
        /// Before the cooldown the only gate was the exit hold, so half a
        /// second of the hero turning away popped the periscope straight
        /// back out and the beat read as a twitch.
        /// </summary>
        [Test]
        public void Startle_HoldsTheRetractForTheWholeCooldown()
        {
            var state = new SupermarketCashierSurveillanceState();
            Simulate(state, 12f, -1f, 3f);

            // Time is measured from the NOTICE, not from the look-away: the
            // cooldown starts the moment he is caught and burns while the
            // hero is still staring at him.
            float sinceNoticed = 0f;
            while (!state.IsStartled && sinceNoticed < 5f)
            {
                state.Update(12f, 1f, Step);
                sinceNoticed += Step;
            }

            Assert.That(state.IsStartled, Is.True);
            sinceNoticed = 0f;

            while (state.IsStartled && sinceNoticed < 20f)
            {
                state.Update(12f, -1f, Step);
                sinceNoticed += Step;
            }

            Assert.That(
                state.IsStartled,
                Is.False,
                "He never let go at all.");
            Assert.That(
                sinceNoticed,
                Is.GreaterThanOrEqualTo(
                    SupermarketCashierSurveillanceState
                        .StartleCooldownSeconds - Step),
                "The retract must hold for the whole cooldown, even " +
                "though the hero looked away at once.");
            Assert.That(
                sinceNoticed,
                Is.LessThan(
                    SupermarketCashierSurveillanceState
                        .StartleCooldownSeconds + 0.5f),
                "The cooldown must not stack on top of the exit hold.");
        }

        /// <summary>
        /// The neck eases rather than snapping. `MoveTowards` moved at a
        /// constant rate, so the chain jerked into motion and stopped dead
        /// at the cap; a critically damped approach has no such step in its
        /// velocity. Measured as: the per-frame delta must rise from rest
        /// and fall again toward the target, never open at full speed.
        /// </summary>
        [Test]
        public void Extension_EasesInAndOutRatherThanRunningAtAFixedRate()
        {
            var state = new SupermarketCashierSurveillanceState();

            float previous = state.Extension;
            var deltas = new System.Collections.Generic.List<float>();
            for (int frame = 0; frame < 200 && state.Extension < 0.98f; frame++)
            {
                state.Update(6f, -1f, Step);
                deltas.Add(state.Extension - previous);
                previous = state.Extension;
            }

            Assert.That(
                deltas.Count,
                Is.GreaterThan(10),
                "The ramp is too short to judge its shape.");
            Assert.That(
                deltas[0],
                Is.LessThan(deltas[deltas.Count / 3]),
                "The neck must ease IN - the first frame cannot already " +
                "be moving at full rate.");

            float peak = 0f;
            for (int index = 0; index < deltas.Count; index++)
            {
                peak = Mathf.Max(peak, deltas[index]);
            }

            Assert.That(
                deltas[deltas.Count - 1],
                Is.LessThan(peak * 0.6f),
                "The neck must ease OUT - it cannot arrive at full rate.");
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

            // Run to the exact frame he lets go, rather than guessing the
            // time: the resume delay starts THERE, and the cooldown has
            // already been burning since the notice.
            float guard = 0f;
            while (state.IsStartled && guard < 20f)
            {
                state.Update(12f, -1f, Step);
                guard += Step;
            }

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
