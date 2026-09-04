using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The continuous balance model in isolation: the sober hero is
    /// bit-exact inert, the drunk one integrates on a fixed step under a
    /// seeded disturbance, steps to catch the capture point, and only
    /// latches a fall where the world allows one.
    /// </summary>
    public sealed class PlayerBalanceModelTests
    {
        private const float Frame = 1f / 60f;
        private const int Seed = 4242;

        // ------------------------------------------------------------
        // Sober.
        // ------------------------------------------------------------

        [Test]
        public void Sober_IsBitExactlyInert()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput busy = BusyInput(0f, true);

            for (int frame = 0; frame < 60 * 60; frame++)
            {
                model.Advance(Frame, busy);
                AssertExactlyZero(model.ComOffset, "ComOffset at frame " + frame);
            }

            AssertExactlyZero(model.ComOffset, "ComOffset");
            AssertExactlyZero(model.ComVelocity, "ComVelocity");
            AssertExactlyZero(model.CentreOfPressure, "CentreOfPressure");
            AssertExactlyZero(model.Output.DriftVelocity, "DriftVelocity");
            AssertExactlyZero(model.Output.CapturePoint, "CapturePoint");
            Assert.That(model.Output.LeanRollDegrees, Is.EqualTo(0f));
            Assert.That(model.Output.LeanPitchDegrees, Is.EqualTo(0f));
            Assert.That(model.Output.HeadingWeaveDegrees, Is.EqualTo(0f));
            Assert.That(model.Output.Instability, Is.EqualTo(0f));
            Assert.That(model.Output.CrouchMetres, Is.EqualTo(0f));
            Assert.That(model.Instability, Is.EqualTo(0f));
            Assert.That(model.StepsTaken, Is.Zero);
            Assert.That(model.Stumbles, Is.Zero);
            Assert.That(model.StepActive, Is.False);
            Assert.That(model.Output.Step.Active, Is.False);
            Assert.That(model.Output.WallSupport, Is.False);
            Assert.That(model.LostBalance, Is.False);
            Assert.That(model.Output.LostBalance, Is.False);
            Assert.That(
                model.Output.LeftFoot,
                Is.EqualTo(PlayerBalanceModel.DefaultLeftFoot));
            Assert.That(
                model.Output.RightFoot,
                Is.EqualTo(PlayerBalanceModel.DefaultRightFoot));
            Assert.That(model.LeftFoot, Is.EqualTo(PlayerBalanceModel.DefaultLeftFoot));
            Assert.That(model.RightFoot, Is.EqualTo(PlayerBalanceModel.DefaultRightFoot));
        }

        [Test]
        public void Settings_SoberRowHasNoNoiseNoCouplingNoBias()
        {
            PlayerBalanceSettings sober = PlayerBalanceSettings.FromIntoxication(0f);
            PlayerBalanceSettings blind = PlayerBalanceSettings.FromIntoxication(1f);
            PlayerBalanceSettings past = PlayerBalanceSettings.FromIntoxication(1.5f);

            Assert.That(sober.Intoxication, Is.EqualTo(0f));
            Assert.That(sober.NoiseAmplitude, Is.EqualTo(0f));
            Assert.That(sober.InputCopShift, Is.EqualTo(0f));
            Assert.That(sober.SlopeBias, Is.EqualTo(0f));
            Assert.That(sober.HeadingWeaveDegrees, Is.EqualTo(0f));

            Assert.That(blind.NoiseAmplitude, Is.EqualTo(PlayerBalanceSettings.NoiseAmplitudeAtMaximum).Within(1e-5f));
            Assert.That(blind.InputCopShift, Is.EqualTo(0.05f).Within(1e-5f));
            Assert.That(blind.SlopeBias, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(blind.HeadingWeaveDegrees, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(blind.MaximumStepReach, Is.LessThan(sober.MaximumStepReach));
            Assert.That(blind.ReactionDelay, Is.GreaterThan(sober.ReactionDelay));
            Assert.That(blind.CopDamping, Is.LessThan(sober.CopDamping));

            Assert.That(past.Intoxication, Is.EqualTo(1f));
            Assert.That(past.NoiseAmplitude, Is.EqualTo(blind.NoiseAmplitude));
            Assert.That(
                sober.Omega,
                Is.EqualTo(Mathf.Sqrt(9.81f / 0.95f)).Within(1e-4f));
        }

        // ------------------------------------------------------------
        // Integration: fixed step, seeds.
        // ------------------------------------------------------------

        [Test]
        public void FixedStep_IsIndependentOfFrameChunking()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.8f);
            PlayerBalanceModel thirty = new PlayerBalanceModel(Seed);
            PlayerBalanceModel sixty = new PlayerBalanceModel(Seed);
            PlayerBalanceModel oneTwenty = new PlayerBalanceModel(Seed);

            for (int tick = 0; tick < 300; tick++)
            {
                thirty.Advance(1f / 30f, input);
                sixty.Advance(1f / 60f, input);
                sixty.Advance(1f / 60f, input);
                for (int sub = 0; sub < 4; sub++)
                {
                    oneTwenty.Advance(1f / 120f, input);
                }

                AssertClose(thirty.ComOffset, sixty.ComOffset, 1e-4f, "ComOffset 30 vs 60 at tick " + tick);
                AssertClose(thirty.ComOffset, oneTwenty.ComOffset, 1e-4f, "ComOffset 30 vs 120 at tick " + tick);
                AssertClose(
                    thirty.Output.CapturePoint,
                    sixty.Output.CapturePoint,
                    1e-4f,
                    "CapturePoint 30 vs 60 at tick " + tick);
                Assert.That(sixty.StepsTaken, Is.EqualTo(thirty.StepsTaken), "StepsTaken at tick " + tick);
                Assert.That(oneTwenty.StepsTaken, Is.EqualTo(thirty.StepsTaken), "StepsTaken at tick " + tick);
                Assert.That(sixty.LostBalance, Is.EqualTo(thirty.LostBalance), "LostBalance at tick " + tick);
                Assert.That(oneTwenty.LostBalance, Is.EqualTo(thirty.LostBalance), "LostBalance at tick " + tick);
            }

            AssertClose(thirty.Output.CapturePoint, oneTwenty.Output.CapturePoint, 1e-4f, "CapturePoint at the end");
            AssertClose(thirty.ComVelocity, sixty.ComVelocity, 1e-4f, "ComVelocity at the end");
            Assert.That(sixty.Output.FallDirection, Is.EqualTo(thirty.Output.FallDirection));
        }

        [Test]
        public void Advance_AccumulatesSubStepDeltasAndIgnoresNonPositiveOnes()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            PlayerBalanceModel halves = new PlayerBalanceModel(Seed);
            PlayerBalanceModel whole = new PlayerBalanceModel(Seed);

            halves.Advance(PlayerBalanceModel.FixedStep * 0.5f, input);
            AssertExactlyZero(halves.ComOffset, "half a fixed step integrates nothing");
            halves.Advance(PlayerBalanceModel.FixedStep * 0.5f, input);
            whole.Advance(PlayerBalanceModel.FixedStep, input);

            Assert.That(halves.ComOffset, Is.EqualTo(whole.ComOffset));
            Assert.That(halves.ComVelocity, Is.EqualTo(whole.ComVelocity));

            Vector2 before = whole.ComOffset;
            whole.Advance(0f, input);
            whole.Advance(-1f, input);
            whole.Advance(float.NaN, input);
            Assert.That(whole.ComOffset, Is.EqualTo(before));
        }

        [Test]
        public void Advance_ClampsOneDeltaToAQuarterSecond()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            model.ArmGrace(5f);

            model.Advance(10f, PlayerBalanceInput.Quiet(1f));

            // 0.25 s is 29 or 30 fixed steps depending on float residue.
            Assert.That(model.GraceSeconds, Is.EqualTo(4.75f).Within(0.02f));
            Assert.That(model.GraceSeconds, Is.LessThan(5f));
        }

        [Test]
        public void Seed_IsDeterministic()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            PlayerBalanceModel first = new PlayerBalanceModel(77);
            PlayerBalanceModel second = new PlayerBalanceModel(77);

            for (int frame = 0; frame < 10 * 60; frame++)
            {
                first.Advance(Frame, input);
                second.Advance(Frame, input);
                Assert.That(second.ComOffset, Is.EqualTo(first.ComOffset), "ComOffset at frame " + frame);
                Assert.That(second.ComVelocity, Is.EqualTo(first.ComVelocity), "ComVelocity at frame " + frame);
                Assert.That(second.StepsTaken, Is.EqualTo(first.StepsTaken), "StepsTaken at frame " + frame);
                Assert.That(second.StepActive, Is.EqualTo(first.StepActive), "StepActive at frame " + frame);
            }

            Assert.That(second.Output.CapturePoint, Is.EqualTo(first.Output.CapturePoint));
            Assert.That(second.Output.HeadingWeaveDegrees, Is.EqualTo(first.Output.HeadingWeaveDegrees));
            Assert.That(second.Seed, Is.EqualTo(first.Seed));
        }

        [Test]
        public void Seed_ChangesTheStagger()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            PlayerBalanceModel one = new PlayerBalanceModel(1);
            PlayerBalanceModel two = new PlayerBalanceModel(2);
            bool differed = false;

            for (int frame = 0; frame < 10 * 60; frame++)
            {
                one.Advance(Frame, input);
                two.Advance(Frame, input);
                if (Vector2.Distance(one.ComOffset, two.ComOffset) > 1e-6f)
                {
                    differed = true;
                }
            }

            Assert.That(differed, Is.True, "two seeds produced the same COM trace for ten seconds");
        }

        // ------------------------------------------------------------
        // Pure rules.
        // ------------------------------------------------------------

        [Test]
        public void CapturePoint_Formula()
        {
            float omega = Mathf.Sqrt(9.81f / 0.95f);

            Vector2 capture = PlayerBalanceRules.CapturePoint(
                new Vector2(0.1f, 0f),
                new Vector2(0.3f, 0f),
                omega);

            Assert.That(capture.x, Is.EqualTo(0.1935f).Within(1e-3f));
            Assert.That(capture.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(PlayerBalanceRules.Omega(0.95f), Is.EqualTo(omega).Within(1e-6f));
            Assert.That(
                PlayerBalanceRules.CapturePoint(Vector2.zero, Vector2.zero, omega),
                Is.EqualTo(Vector2.zero));
            Assert.That(
                PlayerBalanceRules.LeanDegrees(0.95f, 0.95f),
                Is.EqualTo(45f).Within(1e-3f));
            Assert.That(PlayerBalanceRules.FallDirection(new Vector2(-0.01f, 0f)), Is.EqualTo(-1f));
            Assert.That(PlayerBalanceRules.FallDirection(Vector2.zero), Is.EqualTo(1f));
            Assert.That(PlayerBalanceRules.FallDirection(new Vector2(0.2f, -1f)), Is.EqualTo(1f));
        }

        [Test]
        public void StepTarget_OvershootsAndNeverCrossesOtherFoot()
        {
            PlayerBalanceSettings settings = PlayerBalanceSettings.FromIntoxication(0.8f);
            Vector2 leftFoot = new Vector2(-0.1f, 0f);
            Vector2 rightFoot = new Vector2(0.1f, 0f);

            Vector2 right = PlayerBalanceRules.StepTarget(
                new Vector2(0.2f, 0f),
                leftFoot,
                FootSide.Right,
                settings);
            Assert.That(right.x, Is.GreaterThanOrEqualTo(0.2f * PlayerBalanceRules.StepOvershoot));
            Assert.That(right.x, Is.LessThanOrEqualTo(settings.MaximumStepReach));
            Assert.That(
                right.x,
                Is.GreaterThanOrEqualTo(leftFoot.x + PlayerBalanceRules.MinimumFootSeparation));
            Assert.That(right.y, Is.EqualTo(0f).Within(1e-6f));

            Vector2 left = PlayerBalanceRules.StepTarget(
                new Vector2(-0.2f, 0f),
                rightFoot,
                FootSide.Left,
                settings);
            Assert.That(left.x, Is.LessThanOrEqualTo(-0.2f * PlayerBalanceRules.StepOvershoot));
            Assert.That(
                left.x,
                Is.LessThanOrEqualTo(rightFoot.x - PlayerBalanceRules.MinimumFootSeparation));
            Assert.That(left.x, Is.GreaterThanOrEqualTo(-settings.MaximumStepReach));

            // A small escape past a foot that already stands far out never
            // lands the stepping foot inside the other one.
            Vector2 farRight = new Vector2(0.3f, 0f);
            Vector2 crossing = PlayerBalanceRules.StepTarget(
                new Vector2(0.02f, 0f),
                farRight,
                FootSide.Right,
                settings);
            Assert.That(
                crossing.x,
                Is.EqualTo(farRight.x + PlayerBalanceRules.MinimumFootSeparation).Within(1e-6f));

            // Reach is the ceiling laterally, a little more sagittally.
            Vector2 huge = PlayerBalanceRules.StepTarget(
                new Vector2(1f, 0.9f),
                leftFoot,
                FootSide.Right,
                settings);
            Assert.That(huge.x, Is.EqualTo(settings.MaximumStepReach).Within(1e-6f));
            Assert.That(
                huge.y,
                Is.EqualTo(settings.MaximumStepReach * PlayerBalanceRules.SagittalReachMultiplier)
                    .Within(1e-6f));
        }

        [Test]
        public void StepSide_PicksTheSideTheCapturePointEscaped()
        {
            PlayerBalanceSettings settings = PlayerBalanceSettings.FromIntoxication(1f);
            BalanceSupportPolygon support = BalanceSupportPolygon.FromFeet(
                PlayerBalanceModel.DefaultLeftFoot,
                PlayerBalanceModel.DefaultRightFoot,
                settings);

            Assert.That(
                PlayerBalanceRules.StepSide(new Vector2(0.3f, 0f), support, FootSide.Left),
                Is.EqualTo(FootSide.Right));
            Assert.That(
                PlayerBalanceRules.StepSide(new Vector2(-0.3f, 0f), support, FootSide.Right),
                Is.EqualTo(FootSide.Left));
            Assert.That(
                PlayerBalanceRules.StepSide(new Vector2(0f, 0.5f), support, FootSide.Left),
                Is.EqualTo(FootSide.Left));
            Assert.That(
                PlayerBalanceRules.StepSide(new Vector2(0f, 0.5f), support, FootSide.Right),
                Is.EqualTo(FootSide.Right));
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(0.17f, 0f), support, settings.CaptureMargin),
                Is.False);
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(0.19f, 0f), support, settings.CaptureMargin),
                Is.True);
        }

        [Test]
        public void SupportPolygon_PadsTheFeetAndGrowsTowardAWall()
        {
            PlayerBalanceSettings settings = PlayerBalanceSettings.FromIntoxication(1f);
            BalanceSupportPolygon support = BalanceSupportPolygon.FromFeet(
                PlayerBalanceModel.DefaultLeftFoot,
                PlayerBalanceModel.DefaultRightFoot,
                settings);

            Assert.That(support.MinX, Is.EqualTo(-0.15f).Within(1e-6f));
            Assert.That(support.MaxX, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(support.MinForward, Is.EqualTo(-0.06f).Within(1e-6f));
            Assert.That(support.MaxForward, Is.EqualTo(0.12f).Within(1e-6f));
            Assert.That(support.HalfWidth, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(support.Contains(new Vector2(0.1f, 0.1f)), Is.True);
            Assert.That(support.Contains(new Vector2(0.16f, 0f)), Is.False);
            Assert.That(support.Excursion(new Vector2(0.25f, 0f)), Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(support.Excursion(new Vector2(0.05f, 0.05f)), Is.EqualTo(0f));
            Vector2 clamped = support.Clamp(new Vector2(1f, -1f));
            Assert.That(clamped.x, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(clamped.y, Is.EqualTo(-0.06f).Within(1e-6f));

            BalanceSupportPolygon single = BalanceSupportPolygon.FromFoot(
                PlayerBalanceModel.DefaultLeftFoot,
                settings);
            Assert.That(single.MinX, Is.EqualTo(-0.15f).Within(1e-6f));
            Assert.That(single.MaxX, Is.EqualTo(-0.05f).Within(1e-6f));

            BalanceSupportPolygon extended = support.ExtendedToward(
                new Vector2(1f, 0f),
                PlayerBalanceRules.WallSupportReach);
            Assert.That(extended.MaxX, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(extended.MinX, Is.EqualTo(support.MinX));
            Assert.That(extended.MinForward, Is.EqualTo(support.MinForward));
            Assert.That(extended.MaxForward, Is.EqualTo(support.MaxForward));

            BalanceSupportPolygon unchanged = support.ExtendedToward(Vector2.zero, 1f);
            Assert.That(unchanged.MaxX, Is.EqualTo(support.MaxX));
            BalanceSupportPolygon noDistance = support.ExtendedToward(Vector2.right, 0f);
            Assert.That(noDistance.MaxX, Is.EqualTo(support.MaxX));
        }

        [Test]
        public void CanRecoverByStep_MatchesTheRecoverablePolygon()
        {
            PlayerBalanceSettings settings = PlayerBalanceSettings.FromIntoxication(1f);
            BalanceSupportPolygon support = BalanceSupportPolygon.FromFeet(
                PlayerBalanceModel.DefaultLeftFoot,
                PlayerBalanceModel.DefaultRightFoot,
                settings);
            BalanceSupportPolygon recoverable =
                PlayerBalanceRules.RecoverablePolygon(support, settings);
            float reach = settings.MaximumStepReach * PlayerBalanceRules.RecoverableReachFraction;

            Assert.That(recoverable.MaxX, Is.EqualTo(support.MaxX + reach).Within(1e-6f));
            Assert.That(
                recoverable.MaxForward,
                Is.EqualTo(support.MaxForward + reach * PlayerBalanceRules.SagittalReachMultiplier)
                    .Within(1e-6f));

            Vector2[] probes =
            {
                new Vector2(0.49f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-0.49f, 0.1f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.52f),
                new Vector2(0f, -0.45f),
                new Vector2(0f, -0.46f),
                Vector2.zero,
            };
            foreach (Vector2 probe in probes)
            {
                Assert.That(
                    PlayerBalanceRules.CanRecoverByStep(probe, support, settings),
                    Is.EqualTo(recoverable.Contains(probe)),
                    "probe " + probe);
            }

            Assert.That(PlayerBalanceRules.CanRecoverByStep(new Vector2(0.49f, 0f), support, settings), Is.True);
            Assert.That(PlayerBalanceRules.CanRecoverByStep(new Vector2(0.5f, 0f), support, settings), Is.False);
        }

        [Test]
        public void TripImpulse_IsZeroBelowTheKerbAndTheDrink()
        {
            Assert.That(PlayerBalanceRules.TripImpulse(0.03f, 1f), Is.EqualTo(0f));
            Assert.That(PlayerBalanceRules.TripImpulse(0.06f, 0.3f), Is.EqualTo(0f));
            Assert.That(PlayerBalanceRules.TripImpulse(0.06f, 1f), Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(PlayerBalanceRules.TripImpulse(0.06f, 0.5f), Is.EqualTo(0.45f).Within(1e-5f));
            Assert.That(PlayerBalanceRules.TripImpulse(0.12f, 1f), Is.EqualTo(1.8f).Within(1e-5f));
            Assert.That(PlayerBalanceRules.TripImpulse(0.04f, 0.35f), Is.GreaterThan(0f));
        }

        [Test]
        public void WallContactRules_ChooseTheNearHandAndHoldWithHysteresis()
        {
            Assert.That(
                PlayerWallContactRules.TryChooseHand(Vector3.left, Vector3.right, out bool rightHand),
                Is.True);
            Assert.That(rightHand, Is.True, "a wall on the right (normal pointing left) takes the right hand");
            Assert.That(
                PlayerWallContactRules.TryChooseHand(Vector3.right, Vector3.right, out bool leftHand),
                Is.True);
            Assert.That(leftHand, Is.False);
            Assert.That(
                PlayerWallContactRules.TryChooseHand(Vector3.forward, Vector3.right, out _),
                Is.False,
                "a wall straight ahead is nobody's hand");

            // Reach: close and unsteady, or already bumped.
            Assert.That(PlayerWallContactRules.ShouldHold(false, true, 0.3f, 0.5f, 0f, false, 0f), Is.True);
            Assert.That(PlayerWallContactRules.ShouldHold(false, true, 0.1f, 0.5f, 0f, false, 0f), Is.False);
            Assert.That(PlayerWallContactRules.ShouldHold(false, true, 0.1f, 0.5f, 0f, true, 0f), Is.True);
            Assert.That(PlayerWallContactRules.ShouldHold(false, true, 0.9f, 0.56f, 0f, false, 0f), Is.False);
            Assert.That(PlayerWallContactRules.ShouldHold(false, false, 0.9f, 0.1f, 0f, true, 0f), Is.False);
            Assert.That(PlayerWallContactRules.ShouldHold(false, true, 0.9f, 0.1f, 0.7f, true, 0f), Is.False);

            // Release: steady for long enough, or the wall gone or behind.
            Assert.That(PlayerWallContactRules.ShouldHold(true, true, 0.05f, 0.5f, 0f, false, 0.2f), Is.True);
            Assert.That(PlayerWallContactRules.ShouldHold(true, true, 0.05f, 0.5f, 0f, false, 0.4f), Is.False);
            Assert.That(PlayerWallContactRules.ShouldHold(true, true, 0.2f, 0.5f, 0f, false, 5f), Is.True);
            Assert.That(PlayerWallContactRules.ShouldHold(true, true, 0.9f, 0.61f, 0f, false, 0f), Is.False);
            Assert.That(PlayerWallContactRules.ShouldHold(true, true, 0.9f, 0.58f, 0f, false, 0f), Is.True);

            Assert.That(PlayerWallContactRules.AdvanceWeight(0f, true, 0.06f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(PlayerWallContactRules.AdvanceWeight(1f, false, 0.175f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(PlayerWallContactRules.AdvanceWeight(0.5f, true, 10f), Is.EqualTo(1f));
            Assert.That(PlayerWallContactRules.AdvanceWeight(0.5f, false, -1f), Is.EqualTo(0.5f));
        }

        // ------------------------------------------------------------
        // Drunk without falls allowed.
        // ------------------------------------------------------------

        [Test]
        public void Drunk_TakesRecoveryStepsWithoutFalling_WhenFallsNotAllowed()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            bool everStepped = false;

            // Two minutes: the seeded sway of one seed in fifty stays
            // inside the ankles' reach for a full minute.
            for (int frame = 0; frame < 120 * 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.LostBalance, Is.False, "fell at frame " + frame);
                Assert.That(model.Output.LostBalance, Is.False, "output fell at frame " + frame);
                Assert.That(model.Instability, Is.LessThanOrEqualTo(0.85f), "instability at frame " + frame);
                Assert.That(model.Output.Instability, Is.LessThanOrEqualTo(0.85f), "output instability at frame " + frame);
                Assert.That(model.Output.DriftVelocity.y, Is.EqualTo(0f), "sagittal drift at frame " + frame);
                Assert.That(model.Output.Step.Active, Is.EqualTo(model.StepActive), "step flag at frame " + frame);
                everStepped |= model.StepActive;
            }

            Assert.That(model.StepsTaken, Is.GreaterThan(0));
            Assert.That(everStepped, Is.True);
            Assert.That(float.IsNaN(model.ComOffset.x) || float.IsNaN(model.ComOffset.y), Is.False);
        }

        [Test]
        public void FallsDisallowed_PinTheCapturePointEvenAgainstAHugeShove()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            PlayerBalanceSettings settings = PlayerBalanceSettings.FromIntoxication(1f);
            float reachLimit =
                settings.MaximumStepReach * PlayerBalanceRules.RecoverableReachFraction;

            model.InjectPerturbation(new Vector2(3f, 0f));
            for (int frame = 0; frame < 2 * 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.LostBalance, Is.False, "fell at frame " + frame);
                Assert.That(model.Instability, Is.LessThanOrEqualTo(0.85f), "instability at frame " + frame);
                Assert.That(
                    Mathf.Abs(model.Output.CapturePoint.x),
                    Is.LessThanOrEqualTo(settings.MaximumStepReach + 0.15f + reachLimit + 0.05f),
                    "capture point escaped the reach polygon at frame " + frame);
            }

            Assert.That(model.StepsTaken, Is.GreaterThan(0));
            // The pin keeps the COM inside a stepped polygon plus reach.
            Assert.That(model.ComOffset.magnitude, Is.LessThan(0.8f));
        }

        [Test]
        public void Slope_AboveTwelveDegrees_NeverLatchesFall()
        {
            // The controller passes fallAllowed = false above
            // MaximumBalanceSurfaceAngle; the model must then stagger down
            // a twenty-degree slope for two minutes without ever falling.
            Assert.That(PlayerBalanceRules.MaximumBalanceSurfaceAngle, Is.EqualTo(12f));
            float downhill = Mathf.Tan(20f * Mathf.Deg2Rad);
            PlayerBalanceInput input = new PlayerBalanceInput(
                1f,
                new Vector2(0f, 0.8f),
                0f,
                0f,
                true,
                new Vector2(0f, downhill),
                false,
                Vector2.zero,
                1f,
                1f,
                0f,
                false,
                false,
                Vector2.zero,
                false);
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);

            for (int frame = 0; frame < 120 * 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.LostBalance, Is.False, "fell at frame " + frame);
                Assert.That(model.Output.LostBalance, Is.False, "output fell at frame " + frame);
                Assert.That(
                    float.IsNaN(model.ComOffset.x) || float.IsNaN(model.ComOffset.y),
                    Is.False,
                    "NaN at frame " + frame);
            }

            Assert.That(model.StepsTaken, Is.GreaterThan(0), "the slope never made him step");
            Assert.That(model.Instability, Is.LessThanOrEqualTo(0.85f));
        }

        [Test]
        public void CounterSteer_TowardLeanReducesCapturePoint()
        {
            PlayerBalanceInput quiet = PlayerBalanceInput.Quiet(0.8f, fallAllowed: false);
            PlayerBalanceModel toward = new PlayerBalanceModel(Seed);
            PlayerBalanceModel away = new PlayerBalanceModel(Seed);
            const int frames = 30 * 60;
            double towardSum = 0.0;
            double awaySum = 0.0;

            for (int frame = 0; frame < frames; frame++)
            {
                toward.Advance(Frame, quiet.WithTurnInput(SignOrZero(toward.ComOffset.x)));
                away.Advance(Frame, quiet.WithTurnInput(-SignOrZero(away.ComOffset.x)));
                towardSum += Mathf.Abs(toward.Output.CapturePoint.x);
                awaySum += Mathf.Abs(away.Output.CapturePoint.x);
            }

            double towardMean = towardSum / frames;
            double awayMean = awaySum / frames;
            Assert.That(towardMean, Is.LessThan(awayMean), "steering into the lean did not settle him");
            Assert.That(
                towardMean,
                Is.LessThan(awayMean * 0.5),
                "steering into the lean should at least halve the capture-point excursion");
            Assert.That(toward.StepsTaken, Is.LessThanOrEqualTo(away.StepsTaken));
            Assert.That(toward.LostBalance, Is.False);
            Assert.That(away.LostBalance, Is.False);
        }

        [Test]
        public void InjectPerturbation_ProducesAStep()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.6f, fallAllowed: false);
            bool stepped = false;
            FootSide side = FootSide.Left;
            BalanceStepCommand command = BalanceStepCommand.None;

            model.InjectPerturbation(new Vector2(1.2f, 0f));
            for (int frame = 0; frame < 60 && !stepped; frame++)
            {
                model.Advance(Frame, input);
                if (model.StepActive)
                {
                    stepped = true;
                    side = model.Output.Step.Side;
                    command = model.Output.Step;
                }
            }

            Assert.That(stepped, Is.True, "a 1.2 m/s shove produced no recovery step within a second");
            Assert.That(side, Is.EqualTo(FootSide.Right));
            Assert.That(model.StepSide, Is.EqualTo(FootSide.Right));
            Assert.That(command.Active, Is.True);
            Assert.That(command.To.x, Is.GreaterThan(command.From.x), "the right foot steps right");
            Assert.That(command.Lift, Is.GreaterThanOrEqualTo(PlayerBalanceRules.StepLiftBase));
            Assert.That(model.StepsTaken, Is.GreaterThanOrEqualTo(1));
            Assert.That(model.LostBalance, Is.False);
        }

        [Test]
        public void WallSupport_ClampsDriftIntoWall()
        {
            // A wall on the right: its normal points left, away from it.
            Vector2 wallNormal = new Vector2(-1f, 0f);
            PlayerBalanceInput input = new PlayerBalanceInput(
                1f,
                Vector2.zero,
                0f,
                0f,
                true,
                Vector2.zero,
                true,
                wallNormal,
                1f,
                1f,
                0f,
                false,
                true,
                wallNormal,
                true);
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            bool everSupported = false;

            for (int frame = 0; frame < 20 * 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(
                    model.Output.DriftVelocity.x,
                    Is.LessThanOrEqualTo(1e-4f),
                    "drift into the wall at frame " + frame);
                Assert.That(model.LostBalance, Is.False, "fell at frame " + frame);
                everSupported |= model.Output.WallSupport;
            }

            Assert.That(everSupported, Is.True, "the wall within reach never became support");
            Assert.That(model.Output.WallSupport, Is.True);
        }

        // ------------------------------------------------------------
        // Falls.
        // ------------------------------------------------------------

        [Test]
        public void MaximumIntoxication_LosesBalanceWithinThreeMinutes()
        {
            // Offline simulation of the same arithmetic with an independent
            // noise stream fell between 2.8 s and 104 s over 300 seeds
            // (median 17 s, p99 85 s); the bound is deliberately generous.
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            const int limit = 180 * 60;
            int fellAtFrame = -1;

            for (int frame = 0; frame < limit; frame++)
            {
                model.Advance(Frame, input);
                if (model.LostBalance)
                {
                    fellAtFrame = frame;
                    break;
                }
            }

            Assert.That(fellAtFrame, Is.GreaterThanOrEqualTo(0), "never lost balance in three minutes");
            Assert.That(Mathf.Abs(model.FallDirection), Is.EqualTo(1f));
            Assert.That(model.Output.LostBalance, Is.True);
            Assert.That(model.Output.FallDirection, Is.EqualTo(model.FallDirection));
            Assert.That(model.Output.Instability, Is.EqualTo(1f));
            Assert.That(model.Instability, Is.EqualTo(1f));

            // Latched: further frames keep the fall and let the COM settle.
            float offsetAtFall = model.ComOffset.magnitude;
            for (int frame = 0; frame < 2 * 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.LostBalance, Is.True, "the fall unlatched at frame " + frame);
            }

            Assert.That(model.ComOffset.magnitude, Is.LessThanOrEqualTo(offsetAtFall + 1e-6f));
        }

        [Test]
        public void ShoveBeyondReach_TopplesAndFallsWithinASecondAndAHalf()
        {
            // A shove past any step no longer latches on the spot: it
            // starts a topple (a lunge, the torso, the root going with
            // him) that is lost inside a second and a half. The topple's
            // own contract is PlayerBalanceToppleTests; here only the
            // outcome and its side.
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);

            PlayerBalanceModel right = new PlayerBalanceModel(Seed);
            right.InjectPerturbation(new Vector2(3f, 0f));
            right.Advance(Frame, input);
            Assert.That(right.LostBalance, Is.False, "the first frame is a topple, not a latch");
            Assert.That(right.Phase, Is.EqualTo(BalancePhase.Toppling));
            for (int frame = 1; frame < 90 && !right.LostBalance; frame++)
            {
                right.Advance(Frame, input);
            }

            Assert.That(right.LostBalance, Is.True);
            Assert.That(right.FallDirection, Is.EqualTo(1f));
            Assert.That(right.Output.LostBalance, Is.True);
            Assert.That(right.Output.FallDirection, Is.EqualTo(1f));

            PlayerBalanceModel left = new PlayerBalanceModel(Seed);
            left.InjectPerturbation(new Vector2(-3f, 0f));
            for (int frame = 0; frame < 90 && !left.LostBalance; frame++)
            {
                left.Advance(Frame, input);
            }

            Assert.That(left.LostBalance, Is.True);
            Assert.That(left.FallDirection, Is.EqualTo(-1f));

            // The same shove with falls disallowed is pinned instead.
            PlayerBalanceModel pinned = new PlayerBalanceModel(Seed);
            pinned.InjectPerturbation(new Vector2(3f, 0f));
            for (int frame = 0; frame < 90; frame++)
            {
                pinned.Advance(Frame, PlayerBalanceInput.Quiet(1f, fallAllowed: false));
            }

            Assert.That(pinned.LostBalance, Is.False);
            Assert.That(pinned.Phase, Is.EqualTo(BalancePhase.Steady));
        }

        [Test]
        public void Grace_PreventsFallUntilElapsed()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);

            model.ArmGrace(20f);
            Assert.That(model.GraceSeconds, Is.EqualTo(20f));
            model.ArmGrace(5f);
            Assert.That(model.GraceSeconds, Is.EqualTo(20f), "a shorter grace never shortens the armed one");

            float previous = model.GraceSeconds;
            for (int frame = 0; frame < 19 * 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.LostBalance, Is.False, "fell inside the grace at frame " + frame);
                Assert.That(model.GraceSeconds, Is.LessThan(previous), "grace did not decrease at frame " + frame);
                previous = model.GraceSeconds;
            }

            Assert.That(model.GraceSeconds, Is.EqualTo(1f).Within(0.05f));

            // Even a shove that would floor him is refused while grace holds.
            model.InjectPerturbation(new Vector2(3f, 0f));
            model.Advance(Frame, input);
            Assert.That(model.LostBalance, Is.False);

            for (int frame = 0; frame < 2 * 60; frame++)
            {
                model.Advance(Frame, input);
            }

            Assert.That(model.GraceSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void ForceLoseBalance_LatchesImmediately()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            for (int frame = 0; frame < 60; frame++)
            {
                model.Advance(Frame, input);
            }

            model.ForceLoseBalance(-0.2f);

            Assert.That(model.LostBalance, Is.True);
            Assert.That(model.FallDirection, Is.EqualTo(-1f));
            Assert.That(model.Instability, Is.EqualTo(1f));
            Assert.That(model.Output.LostBalance, Is.True);
            Assert.That(model.Output.FallDirection, Is.EqualTo(-1f));
            Assert.That(model.Output.Instability, Is.EqualTo(1f));
            AssertExactlyZero(model.Output.DriftVelocity, "drift after a forced fall");

            // Latched through further frames, whatever the input allows.
            float offset = model.ComOffset.magnitude;
            int steps = model.StepsTaken;
            for (int frame = 0; frame < 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.LostBalance, Is.True);
                Assert.That(model.ComOffset.magnitude, Is.LessThanOrEqualTo(offset + 1e-6f));
                offset = model.ComOffset.magnitude;
            }

            Assert.That(model.StepsTaken, Is.EqualTo(steps), "a fallen hero plans no steps");

            PlayerBalanceModel tie = new PlayerBalanceModel(Seed);
            tie.ForceLoseBalance(0f);
            Assert.That(tie.FallDirection, Is.EqualTo(1f));
            PlayerBalanceModel rightward = new PlayerBalanceModel(Seed);
            rightward.ForceLoseBalance(0.5f);
            Assert.That(rightward.FallDirection, Is.EqualTo(1f));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            for (int frame = 0; frame < 5 * 60; frame++)
            {
                model.Advance(Frame, input);
            }

            model.InjectPerturbation(new Vector2(2f, 0f));
            model.Advance(Frame, input);
            model.ForceLoseBalance(-1f);
            model.InjectPerturbation(new Vector2(3f, 0f));

            model.Reset();

            AssertExactlyZero(model.ComOffset, "ComOffset");
            AssertExactlyZero(model.ComVelocity, "ComVelocity");
            AssertExactlyZero(model.CentreOfPressure, "CentreOfPressure");
            Assert.That(model.LostBalance, Is.False);
            Assert.That(model.FallDirection, Is.EqualTo(1f));
            Assert.That(model.StepActive, Is.False);
            Assert.That(model.Instability, Is.EqualTo(0f));
            Assert.That(model.LeftFoot, Is.EqualTo(PlayerBalanceModel.DefaultLeftFoot));
            Assert.That(model.RightFoot, Is.EqualTo(PlayerBalanceModel.DefaultRightFoot));
            AssertExactlyZero(model.Output.DriftVelocity, "Output.DriftVelocity");
            AssertExactlyZero(model.Output.CapturePoint, "Output.CapturePoint");
            Assert.That(model.Output.LeanRollDegrees, Is.EqualTo(0f));
            Assert.That(model.Output.LeanPitchDegrees, Is.EqualTo(0f));
            Assert.That(model.Output.Instability, Is.EqualTo(0f));
            Assert.That(model.Output.Step.Active, Is.False);
            Assert.That(model.Output.WallSupport, Is.False);
            Assert.That(model.Output.LostBalance, Is.False);
            Assert.That(model.Output.FallDirection, Is.EqualTo(1f));
            Assert.That(model.Output.LeftFoot, Is.EqualTo(PlayerBalanceModel.DefaultLeftFoot));
            Assert.That(model.Output.RightFoot, Is.EqualTo(PlayerBalanceModel.DefaultRightFoot));

            // The pending shove died with the reset: with falls allowed it
            // would otherwise floor him on the very next frame.
            model.Advance(Frame, PlayerBalanceInput.Quiet(1f));
            Assert.That(model.LostBalance, Is.False, "a pre-reset shove survived the reset");

            // And a sober frame after a reset is exactly still.
            PlayerBalanceModel sober = new PlayerBalanceModel(Seed);
            sober.Advance(Frame, input);
            sober.Reset();
            sober.Advance(Frame, PlayerBalanceInput.Quiet(0f));
            AssertExactlyZero(sober.ComOffset, "sober ComOffset after reset");
            AssertExactlyZero(sober.Output.DriftVelocity, "sober drift after reset");
        }

        // ------------------------------------------------------------
        // Helpers.
        // ------------------------------------------------------------

        private static PlayerBalanceInput BusyInput(float intoxication, bool fallAllowed)
        {
            // Everything that could couple into the model at once: steering
            // right, half running, a twenty-degree slope, a wall bumped on
            // the right with the hand on it, a swinging boot and a kerb.
            return new PlayerBalanceInput(
                intoxication,
                new Vector2(0.3f, 1.2f),
                1f,
                0.5f,
                true,
                new Vector2(0f, Mathf.Tan(20f * Mathf.Deg2Rad)),
                true,
                new Vector2(-1f, 0f),
                1f,
                0.3f,
                0.06f,
                fallAllowed,
                true,
                new Vector2(-1f, 0f),
                true);
        }

        private static float SignOrZero(float value)
        {
            if (value > 0f)
            {
                return 1f;
            }

            return value < 0f ? -1f : 0f;
        }

        private static void AssertExactlyZero(Vector2 value, string what)
        {
            Assert.That(
                value.x == 0f && value.y == 0f,
                Is.True,
                what + " expected exactly (0, 0) but was " + value.ToString("R"));
        }

        private static void AssertClose(
            Vector2 expected,
            Vector2 actual,
            float tolerance,
            string what)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance), what + " x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance), what + " y");
        }
    }
}
