using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The print arrives over fifteen seconds of the player's own time and
    /// leaves over three, and it will not spend either of them behind a
    /// paused menu.
    /// </summary>
    public sealed class BegottenRampModelTests
    {
        private const float Frame = 1f / 60f;

        private static float RunSeconds(
            BegottenRampModel ramp,
            float seconds,
            bool paused = false)
        {
            int frames = Mathf.RoundToInt(seconds / Frame);
            for (int index = 0; index < frames; index++)
            {
                ramp.Advance(Frame, paused);
            }

            return ramp.Strength;
        }

        [Test]
        public void ChangedOutsideTheMenu_SnapsWithNoRamp()
        {
            // This is why every existing print test still means what it
            // meant: they set the flag with nothing paused, so the mode is
            // simply on, at full strength, taking the same code path it
            // always took. Only a player changing their mind behind the
            // menu gets an arrival.
            var ramp = new BegottenRampModel();
            ramp.Observe(false, false);
            ramp.Observe(true, false);

            Assert.That(ramp.Strength, Is.EqualTo(1f));
            Assert.That(ramp.IsArmed, Is.False);
            Assert.That(ramp.IsRamping, Is.False);
        }

        [Test]
        public void FirstSightOfTheSetting_IsNeverAnArrival()
        {
            // A scene loading with the preference already on must not
            // replay fifteen seconds at every door.
            var ramp = new BegottenRampModel();
            ramp.Observe(true, true);

            Assert.That(ramp.Strength, Is.EqualTo(1f));
            Assert.That(ramp.IsArmed, Is.False);
        }

        [Test]
        public void ChangedBehindTheMenu_WaitsAndThenArrives()
        {
            var ramp = new BegottenRampModel();
            ramp.Observe(false, false);
            ramp.Observe(true, true);

            Assert.That(ramp.IsArmed, Is.True);
            Assert.That(RunSeconds(ramp, 5f, paused: true), Is.Zero);

            ramp.NotifyResumed();
            Assert.That(
                RunSeconds(ramp, BegottenRampRules.RampInSeconds + 1f),
                Is.EqualTo(1f));
        }

        [Test]
        public void SwitchedOn_WaitsForTheMenuToClose()
        {
            var ramp = new BegottenRampModel();
            ramp.NotifyEnabled();

            Assert.That(ramp.IsArmed, Is.True);
            Assert.That(
                RunSeconds(ramp, 30f),
                Is.Zero,
                "A mode that arrived behind the menu would be over before " +
                "the player saw a frame of it.");

            ramp.NotifyResumed();
            Assert.That(ramp.IsArmed, Is.False);
            Assert.That(ramp.IsRamping, Is.True);
        }

        [Test]
        public void FromTheMenu_ItTakesFifteenSeconds()
        {
            var ramp = new BegottenRampModel();
            ramp.NotifyEnabled();
            ramp.NotifyResumed();

            Assert.That(
                RunSeconds(ramp, BegottenRampRules.RampInSeconds * 0.5f),
                Is.EqualTo(0.5f).Within(0.02f),
                "Halfway through the ramp is halfway up.");
            Assert.That(
                RunSeconds(ramp, BegottenRampRules.RampInSeconds * 0.5f),
                Is.EqualTo(1f).Within(0.001f),
                "Fifteen seconds is the whole of it.");
            Assert.That(
                RunSeconds(ramp, 1f),
                Is.EqualTo(1f),
                "And it stays there.");
            Assert.That(ramp.IsRamping, Is.False);
        }

        [Test]
        public void SwitchedOff_LeavesFasterThanItArrived()
        {
            var ramp = new BegottenRampModel(true);
            Assert.That(ramp.Strength, Is.EqualTo(1f));

            ramp.NotifyDisabled();
            Assert.That(
                RunSeconds(ramp, BegottenRampRules.RampOutSeconds - 0.2f),
                Is.GreaterThan(0f),
                "Three seconds is a fade, not a cut.");
            Assert.That(RunSeconds(ramp, 0.4f), Is.Zero);
            Assert.That(
                BegottenRampRules.RampOutSeconds,
                Is.LessThan(BegottenRampRules.RampInSeconds));
        }

        [Test]
        public void SwitchedOffMidRamp_TurnsRoundFromWhereItGot()
        {
            var ramp = new BegottenRampModel();
            ramp.NotifyEnabled();
            ramp.NotifyResumed();
            float reached = RunSeconds(ramp, 5f);
            Assert.That(reached, Is.InRange(0.2f, 0.5f));

            ramp.NotifyDisabled();
            float afterHalfASecond = RunSeconds(ramp, 0.5f);
            Assert.That(
                afterHalfASecond,
                Is.LessThan(reached),
                "It goes back down, not up.");
            Assert.That(
                afterHalfASecond,
                Is.GreaterThan(0f),
                "It leaves at the leaving speed rather than snapping: a " +
                "third of the way up costs a third of the three seconds.");
            Assert.That(RunSeconds(ramp, 3f), Is.Zero);
        }

        [Test]
        public void OnAndOffInsideTheMenu_LeavesNothingArmed()
        {
            var ramp = new BegottenRampModel();
            ramp.NotifyEnabled();
            ramp.NotifyDisabled();
            ramp.NotifyResumed();

            Assert.That(ramp.IsArmed, Is.False);
            Assert.That(RunSeconds(ramp, 20f), Is.Zero);
        }

        [Test]
        public void OffAndOnInsideTheMenu_KeepsTheFullPrint()
        {
            var ramp = new BegottenRampModel(true);
            ramp.NotifyDisabled();
            ramp.NotifyEnabled();
            ramp.NotifyResumed();

            Assert.That(
                ramp.Strength,
                Is.EqualTo(1f),
                "Nothing was ever taken away, so nothing has to arrive.");
            Assert.That(RunSeconds(ramp, 20f), Is.EqualTo(1f));
        }

        [Test]
        public void ReopeningTheMenuMidRamp_HoldsAndContinues()
        {
            var ramp = new BegottenRampModel();
            ramp.NotifyEnabled();
            ramp.NotifyResumed();
            float reached = RunSeconds(ramp, 4f);

            Assert.That(
                RunSeconds(ramp, 10f, paused: true),
                Is.EqualTo(reached),
                "A paused game cannot spend the ramp.");
            Assert.That(RunSeconds(ramp, 2f), Is.GreaterThan(reached));
        }

        [Test]
        public void ASceneThatLoadsWithItOn_StartsPrinted()
        {
            var ramp = new BegottenRampModel(true);
            Assert.That(ramp.Strength, Is.EqualTo(1f));
            Assert.That(ramp.IsRamping, Is.False);

            var snapped = new BegottenRampModel();
            snapped.SnapTo(true);
            Assert.That(snapped.Strength, Is.EqualTo(1f));
            Assert.That(snapped.IsArmed, Is.False);
        }

        [Test]
        public void ALongStall_MovesTheRampByOneStepAtMost()
        {
            var ramp = new BegottenRampModel();
            ramp.NotifyEnabled();
            ramp.NotifyResumed();
            ramp.Advance(600f, false);

            Assert.That(
                ramp.Strength,
                Is.EqualTo(
                    BegottenRampRules.MaximumStepSeconds /
                    BegottenRampRules.RampInSeconds).Within(0.0001f),
                "A scene load must not skip the whole arrival.");
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        [TestCase(0f)]
        public void ABrokenDelta_MovesNothing(float delta)
        {
            var ramp = new BegottenRampModel();
            ramp.NotifyEnabled();
            ramp.NotifyResumed();
            ramp.Advance(delta, false);

            Assert.That(ramp.Strength, Is.Zero);
        }

        [Test]
        public void TheEasedStrength_IsExactAtBothEndsAndRisesBetween()
        {
            var ramp = new BegottenRampModel();
            Assert.That(ramp.EasedStrength, Is.Zero);

            ramp.NotifyEnabled();
            ramp.NotifyResumed();
            float previous = 0f;
            for (int step = 0; step < 60; step++)
            {
                RunSeconds(ramp, BegottenRampRules.RampInSeconds / 60f);
                float eased = ramp.EasedStrength;
                Assert.That(
                    eased,
                    Is.GreaterThanOrEqualTo(previous),
                    "The print never backs off while it is arriving.");
                Assert.That(eased, Is.InRange(0f, 1f));
                previous = eased;
            }

            Assert.That(
                ramp.EasedStrength,
                Is.EqualTo(1f).Within(0.0001f),
                "Full strength has to be exactly the picture the mode has " +
                "always made, or every pinned frame in the print tests " +
                "stops meaning anything.");
        }
    }
}
