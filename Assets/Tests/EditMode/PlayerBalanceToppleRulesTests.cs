using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The pure formulas of the fight before the fall: the torso
    /// flywheel's command, return and stop, the capture point's runaway
    /// prediction a lunge aims at, the lunge target, the pendulum's hip
    /// drop, the brace ramp and the ragdoll's angular velocity.
    /// Hero frame throughout: x right, y forward, metres.
    /// </summary>
    public sealed class PlayerBalanceToppleRulesTests
    {
        private const float Tolerance = 0.00001f;

        private static PlayerBalanceSettings BlindDrunk =>
            PlayerBalanceSettings.FromIntoxication(1f);

        private static BalanceSupportPolygon StancePolygon()
        {
            return BalanceSupportPolygon.FromFeet(
                new Vector2(-0.1f, 0f),
                new Vector2(0.1f, 0f),
                BlindDrunk);
        }

        [Test]
        public void Settings_FlywheelRowShrinksWithTheDrink()
        {
            PlayerBalanceSettings sober = PlayerBalanceSettings.FromIntoxication(0f);
            PlayerBalanceSettings drunk = BlindDrunk;

            Assert.That(sober.FlywheelAcceleration, Is.EqualTo(22f).Within(Tolerance));
            Assert.That(drunk.FlywheelAcceleration, Is.EqualTo(9f).Within(Tolerance));
            Assert.That(sober.FlywheelReactionDelay, Is.EqualTo(0.05f).Within(Tolerance));
            Assert.That(drunk.FlywheelReactionDelay, Is.EqualTo(0.18f).Within(Tolerance));

            PlayerBalanceSettings quiet = drunk.WithFlywheelAcceleration(0f);
            Assert.That(quiet.FlywheelAcceleration, Is.EqualTo(0f));
            Assert.That(quiet.MaximumStepReach, Is.EqualTo(drunk.MaximumStepReach));
            Assert.That(quiet.NoiseAmplitude, Is.EqualTo(drunk.NoiseAmplitude));

            PlayerBalanceSettings lunge = drunk.WithStepReach(PlayerBalanceRules.LungeReachMultiplier);
            Assert.That(
                lunge.MaximumStepReach,
                Is.EqualTo(drunk.MaximumStepReach * PlayerBalanceRules.LungeReachMultiplier).Within(Tolerance));
            Assert.That(lunge.FlywheelAcceleration, Is.EqualTo(drunk.FlywheelAcceleration));
            Assert.That(lunge.StepDuration, Is.EqualTo(drunk.StepDuration));
        }

        [Test]
        public void FlywheelCommand_PointsAlongTheExcursion()
        {
            BalanceSupportPolygon support = StancePolygon();

            // Inside the polygon, and inside the margin: exactly nothing.
            Vector2 inside = PlayerBalanceRules.FlywheelCommand(
                new Vector2(0.05f, 0.02f), support, 0.03f, 22f);
            Assert.That(inside.x, Is.EqualTo(0f));
            Assert.That(inside.y, Is.EqualTo(0f));
            Vector2 margin = PlayerBalanceRules.FlywheelCommand(
                new Vector2(0.17f, 0f), support, 0.03f, 22f);
            Assert.That(margin.x, Is.EqualTo(0f));
            Assert.That(margin.y, Is.EqualTo(0f));

            // Outside to the right: the full budget, to the right.
            Vector2 right = PlayerBalanceRules.FlywheelCommand(
                new Vector2(0.4f, 0f), support, 0.03f, 22f);
            Assert.That(right.x, Is.EqualTo(22f).Within(Tolerance));
            Assert.That(right.y, Is.EqualTo(0f).Within(Tolerance));

            // Outside diagonally: along the escape, at the budget's magnitude.
            Vector2 diagonal = PlayerBalanceRules.FlywheelCommand(
                new Vector2(0.45f, 0.42f), support, 0.03f, 9f);
            Assert.That(diagonal.magnitude, Is.EqualTo(9f).Within(Tolerance));
            Assert.That(diagonal.x, Is.EqualTo(diagonal.y).Within(Tolerance));

            // No budget, no command.
            Vector2 none = PlayerBalanceRules.FlywheelCommand(
                new Vector2(0.4f, 0f), support, 0.03f, 0f);
            Assert.That(none, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void FlywheelReturn_IsCappedAndZeroAtRest()
        {
            Vector2 rest = PlayerBalanceRules.FlywheelReturn(Vector2.zero, Vector2.zero);
            Assert.That(rest.x == 0f && rest.y == 0f, Is.True, "at rest the return is exactly zero");

            Vector2 small = PlayerBalanceRules.FlywheelReturn(new Vector2(0.05f, 0f), Vector2.zero);
            Assert.That(small.x, Is.LessThan(0f));
            Assert.That(
                small.x,
                Is.EqualTo(-PlayerBalanceRules.FlywheelReturnFrequency *
                           PlayerBalanceRules.FlywheelReturnFrequency * 0.05f).Within(Tolerance));

            Vector2 large = PlayerBalanceRules.FlywheelReturn(
                new Vector2(PlayerBalanceRules.FlywheelMaximumRadians, 0f), Vector2.zero);
            Assert.That(
                large.magnitude,
                Is.EqualTo(PlayerBalanceRules.FlywheelReturnAccelerationLimit).Within(Tolerance));

            // Damping opposes the velocity.
            Vector2 damped = PlayerBalanceRules.FlywheelReturn(Vector2.zero, new Vector2(0f, 0.1f));
            Assert.That(damped.y, Is.LessThan(0f));
        }

        [Test]
        public void ClampFlywheel_StopsAtTheAngleAndKillsTheOutwardParts()
        {
            Vector2 angle = new Vector2(1f, 0f);
            Vector2 velocity = new Vector2(3f, 0.5f);
            Vector2 applied = new Vector2(20f, -2f);
            bool clamped = PlayerBalanceRules.ClampFlywheel(ref angle, ref velocity, ref applied);

            Assert.That(clamped, Is.True);
            Assert.That(angle.x, Is.EqualTo(PlayerBalanceRules.FlywheelMaximumRadians).Within(Tolerance));
            Assert.That(angle.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(velocity.x, Is.EqualTo(0f).Within(Tolerance), "outward velocity dies at the stop");
            Assert.That(velocity.y, Is.EqualTo(0.5f).Within(Tolerance), "sideways velocity survives");
            Assert.That(applied.x, Is.EqualTo(0f).Within(Tolerance), "the outward push is taken back");
            Assert.That(applied.y, Is.EqualTo(-2f).Within(Tolerance));

            // Inside the stop nothing changes; an inward velocity survives at the stop.
            Vector2 insideAngle = new Vector2(0.3f, 0.2f);
            Vector2 insideVelocity = new Vector2(1f, 1f);
            Vector2 insideApplied = new Vector2(5f, 5f);
            Assert.That(
                PlayerBalanceRules.ClampFlywheel(ref insideAngle, ref insideVelocity, ref insideApplied),
                Is.False);
            Assert.That(insideAngle, Is.EqualTo(new Vector2(0.3f, 0.2f)));
            Assert.That(insideVelocity, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(insideApplied, Is.EqualTo(new Vector2(5f, 5f)));

            Vector2 stopAngle = new Vector2(1f, 0f);
            Vector2 inward = new Vector2(-2f, 0f);
            Vector2 inwardApplied = new Vector2(-4f, 0f);
            PlayerBalanceRules.ClampFlywheel(ref stopAngle, ref inward, ref inwardApplied);
            Assert.That(inward.x, Is.EqualTo(-2f).Within(Tolerance));
            Assert.That(inwardApplied.x, Is.EqualTo(-4f).Within(Tolerance));
        }

        [Test]
        public void PredictedCapturePoint_GrowsExponentiallyBeyondTheEdge()
        {
            BalanceSupportPolygon support = StancePolygon();
            float omega = BlindDrunk.Omega;

            Vector2 inside = PlayerBalanceRules.PredictedCapturePoint(
                new Vector2(0.05f, 0.03f), support, omega, 0.3f);
            Assert.That(inside, Is.EqualTo(new Vector2(0.05f, 0.03f)));

            Vector2 now = PlayerBalanceRules.PredictedCapturePoint(
                new Vector2(0.4f, 0f), support, omega, 0f);
            Assert.That(now.x, Is.EqualTo(0.4f).Within(Tolerance));

            Vector2 later = PlayerBalanceRules.PredictedCapturePoint(
                new Vector2(0.4f, 0f), support, omega, 0.3f);
            float expected = support.MaxX + (0.4f - support.MaxX) * Mathf.Exp(omega * 0.3f);
            Assert.That(later.x, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(later.x, Is.GreaterThan(0.4f + 0.3f), "a third of a second more than doubles the excursion");
            Assert.That(later.y, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void LungeTarget_BiasesWithInputAndError()
        {
            PlayerBalanceSettings lunge = BlindDrunk.WithStepReach(PlayerBalanceRules.LungeReachMultiplier);
            // Close enough that neither the plain nor the steered target
            // hits the lunge row's reach clamp.
            Vector2 capture = new Vector2(0.2f, 0f);
            Vector2 other = new Vector2(-0.1f, 0f);

            Vector2 plain = PlayerBalanceRules.LungeTarget(capture, other, FootSide.Right, 0f, Vector2.zero, lunge);
            Vector2 steered = PlayerBalanceRules.LungeTarget(capture, other, FootSide.Right, 1f, Vector2.zero, lunge);
            Vector2 errant = PlayerBalanceRules.LungeTarget(capture, other, FootSide.Right, 0f, new Vector2(0.05f, 0.1f), lunge);

            Assert.That(plain, Is.EqualTo(PlayerBalanceRules.StepTarget(capture, other, FootSide.Right, lunge)));
            Assert.That(steered.x, Is.GreaterThan(plain.x), "steering right pulls the lunge right");
            Assert.That(
                steered.x - plain.x,
                Is.EqualTo(lunge.InputCopShift * PlayerBalanceRules.LungeInputGain * PlayerBalanceRules.StepOvershoot).Within(0.001f));
            Assert.That(errant.y, Is.GreaterThan(plain.y), "the aim error lands where it points");
            Assert.That(
                Mathf.Abs(plain.x),
                Is.LessThanOrEqualTo(lunge.MaximumStepReach + Tolerance),
                "a lunge never reaches past the lunge row");
            Assert.That(
                lunge.MaximumStepReach,
                Is.GreaterThan(BlindDrunk.MaximumStepReach),
                "the lunge row reaches further than the ordinary one");
        }

        [Test]
        public void PendulumDrop_At38DegreesIs20cm()
        {
            Assert.That(PlayerBalanceRules.PendulumDrop(0f, 0.95f), Is.EqualTo(0f));
            Assert.That(
                PlayerBalanceRules.PendulumDrop(PlayerBalanceRules.PointOfNoReturnDegrees, 0.95f),
                Is.EqualTo(0.95f * (1f - Mathf.Cos(38f * Mathf.Deg2Rad))).Within(Tolerance));
            Assert.That(PlayerBalanceRules.PendulumDrop(38f, 0.95f), Is.EqualTo(0.2015f).Within(0.001f));
            Assert.That(
                PlayerBalanceRules.PendulumDrop(-16f, 0.95f),
                Is.EqualTo(PlayerBalanceRules.PendulumDrop(16f, 0.95f)),
                "the drop is the same either way");
        }

        [Test]
        public void BraceWeight_RampsFromTheBraceLeanToThePointOfNoReturn()
        {
            Assert.That(PlayerBalanceRules.BraceWeight(0f), Is.EqualTo(0f));
            Assert.That(PlayerBalanceRules.BraceWeight(PlayerBalanceRules.BraceStartDegrees), Is.EqualTo(0f));
            Assert.That(PlayerBalanceRules.BraceWeight(32f), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(PlayerBalanceRules.BraceWeight(PlayerBalanceRules.PointOfNoReturnDegrees), Is.EqualTo(1f));
            Assert.That(PlayerBalanceRules.BraceWeight(60f), Is.EqualTo(1f));
        }

        [Test]
        public void FallAngularVelocity_IsSpeedOverTheLever()
        {
            Assert.That(PlayerBalanceRules.FallAngularVelocity(0f, 30f, 0.95f), Is.EqualTo(0f));
            float expected = 1.2f / (0.95f * Mathf.Cos(30f * Mathf.Deg2Rad));
            Assert.That(PlayerBalanceRules.FallAngularVelocity(1.2f, 30f, 0.95f), Is.EqualTo(expected).Within(Tolerance));
            Assert.That(
                PlayerBalanceRules.FallAngularVelocity(1f, 89f, 0.95f),
                Is.LessThan(1f / (0.95f * 0.2f) + Tolerance),
                "the lever never collapses to nothing");
        }

        [Test]
        public void Constants_KeepTheirRelationships()
        {
            Assert.That(PlayerBalanceRules.PointOfNoReturnDegrees, Is.GreaterThan(BlindDrunk.FallLeanDegrees));
            Assert.That(PlayerBalanceRules.BraceStartDegrees, Is.LessThan(PlayerBalanceRules.PointOfNoReturnDegrees));
            Assert.That(PlayerBalanceRules.RecoverLeanDegrees, Is.LessThan(BlindDrunk.FallLeanDegrees));
            Assert.That(PlayerBalanceRules.MinimumToppleSeconds, Is.LessThan(PlayerBalanceRules.MaximumToppleSeconds));
            Assert.That(PlayerBalanceRules.LungeReachMultiplier, Is.GreaterThan(1f));
            Assert.That(PlayerBalanceRules.LungeDurationMultiplier, Is.LessThan(1f), "a lunge is faster than a stagger's step");
            Assert.That(PlayerBalanceRules.FlywheelMaximumRadians, Is.EqualTo(40f * Mathf.Deg2Rad).Within(Tolerance));
        }
    }
}
