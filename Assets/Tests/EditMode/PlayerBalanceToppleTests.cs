using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The fight before the fall, in the model alone: the torso flywheel
    /// whips toward an escaping capture point and buys ground, a shove
    /// past reach starts a topple with a lunge instead of a latch, the
    /// root follows the centre of mass while it lasts, a topple can be
    /// recovered and can be lost, the point of no return bounds it, and
    /// what the fall hands the ragdoll is the motion the body had.
    /// </summary>
    public sealed class PlayerBalanceToppleTests
    {
        private const float Frame = 1f / 60f;
        private const int Seed = 4242;

        // ------------------------------------------------------------
        // Sober and at rest.
        // ------------------------------------------------------------

        [Test]
        public void Sober_HasNoTorsoNoPhaseNoBrace()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput quiet = PlayerBalanceInput.Quiet(0f);
            for (int frame = 0; frame < 120; frame++)
            {
                model.Advance(Frame, quiet);
                Assert.That(model.FlywheelAngle.x == 0f && model.FlywheelAngle.y == 0f, Is.True, "flywheel at frame " + frame);
            }

            Assert.That(model.Phase, Is.EqualTo(BalancePhase.Steady));
            Assert.That(model.Output.Phase, Is.EqualTo(BalancePhase.Steady));
            Assert.That(model.BraceWeight, Is.EqualTo(0f));
            Assert.That(model.Output.BraceWeight, Is.EqualTo(0f));
            Assert.That(model.Output.TorsoReactionDegrees.x, Is.EqualTo(0f));
            Assert.That(model.Output.TorsoReactionDegrees.y, Is.EqualTo(0f));
            Assert.That(model.Output.ArmReaction, Is.EqualTo(0f));
            Assert.That(model.Output.CrouchMetres, Is.EqualTo(0f));
            Assert.That(model.LeanDegrees, Is.EqualTo(0f));
            Assert.That(model.LungesTaken, Is.Zero);
            Assert.That(model.Topples, Is.Zero);
            Assert.That(model.FallCause, Is.EqualTo(BalanceFallCause.None));
        }

        [Test]
        public void DrunkAtRest_SpendsNothingOnTheFlywheel()
        {
            // A drunk whose capture point stays inside the boots never
            // commands the torso, and the return spring at zero is zero.
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceSettings still = PlayerBalanceSettings.FromIntoxication(0.3f);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.3f);
            for (int frame = 0; frame < 300; frame++)
            {
                model.Advance(Frame, input, still);
                Assert.That(model.Phase, Is.EqualTo(BalancePhase.Steady), "phase at frame " + frame);
            }

            Assert.That(model.FlywheelAngle.magnitude, Is.LessThan(0.02f));
            Assert.That(model.Topples, Is.Zero);
        }

        // ------------------------------------------------------------
        // The flywheel.
        // ------------------------------------------------------------

        [Test]
        public void Flywheel_WhipsTowardTheExcursionAndUnwinds()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.3f, fallAllowed: false);
            for (int frame = 0; frame < 30; frame++)
            {
                model.Advance(Frame, input);
            }

            model.InjectPerturbation(new Vector2(1f, 0f));
            float peak = 0f;
            for (int frame = 0; frame < 18; frame++)
            {
                model.Advance(Frame, input);
                peak = Mathf.Max(peak, model.FlywheelAngle.x);
                Assert.That(
                    model.FlywheelAngle.magnitude,
                    Is.LessThanOrEqualTo(PlayerBalanceRules.FlywheelMaximumRadians + 0.0001f),
                    "the stop holds at frame " + frame);
            }

            Assert.That(peak, Is.GreaterThan(0.1f), "the torso whips to the right for a rightward shove");
            Assert.That(model.Output.TorsoReactionDegrees.x, Is.GreaterThan(0f));
            Assert.That(model.Output.ArmReaction, Is.GreaterThan(0f));

            // A torso does not stay thrown: within the next four seconds
            // it unwinds to within five degrees of upright at some point,
            // whatever the stagger does after that.
            float closest = float.MaxValue;
            for (int frame = 0; frame < 4 * 60; frame++)
            {
                model.Advance(Frame, input);
                closest = Mathf.Min(closest, model.FlywheelAngle.magnitude);
            }

            Assert.That(
                closest,
                Is.LessThan(5f * Mathf.Deg2Rad),
                "the torso came back within five degrees of upright");
        }

        [Test]
        public void Flywheel_AtTheStopIsSpentAndUnwindsEvenWhileThePointIsOut()
        {
            // Pinned on a stair with the capture point held outside the
            // boots, the old torso sat at its stop for good. Now it is
            // spent at the stop and comes back regardless.
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            model.InjectPerturbation(new Vector2(3f, 0f));
            bool reachedStop = false;
            float lowestAfterStop = float.MaxValue;
            for (int frame = 0; frame < 3 * 60; frame++)
            {
                model.Advance(Frame, input);
                float angle = model.FlywheelAngle.magnitude;
                if (angle >= PlayerBalanceRules.FlywheelMaximumRadians - 0.01f)
                {
                    reachedStop = true;
                }
                else if (reachedStop)
                {
                    lowestAfterStop = Mathf.Min(lowestAfterStop, angle);
                }
            }

            Assert.That(reachedStop, Is.True, "a three-metre-per-second shove throws the torso to its stop");
            Assert.That(
                lowestAfterStop,
                Is.LessThan(PlayerBalanceRules.FlywheelMaximumRadians * 0.5f),
                "and it unwinds from the stop within three seconds");
        }

        [Test]
        public void Flywheel_ReducesTheCaptureExcursion()
        {
            PlayerBalanceSettings withTorso = PlayerBalanceSettings.FromIntoxication(0.5f);
            PlayerBalanceSettings withoutTorso = withTorso.WithFlywheelAcceleration(0f);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.5f, fallAllowed: false);
            float peakWith = PeakExcursionAfterShove(withTorso, input);
            float peakWithout = PeakExcursionAfterShove(withoutTorso, input);

            Assert.That(peakWith, Is.LessThan(peakWithout), $"with {peakWith:F3} m, without {peakWithout:F3} m");
        }

        private static float PeakExcursionAfterShove(
            in PlayerBalanceSettings settings,
            in PlayerBalanceInput input)
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            for (int frame = 0; frame < 30; frame++)
            {
                model.Advance(Frame, input, settings);
            }

            // The rescue window: the whip lasts until the stop, a third
            // of a second; the unwinding after it is the price and is
            // not what is measured here.
            model.InjectPerturbation(new Vector2(0.8f, 0f));
            float peak = 0f;
            for (int frame = 0; frame < 21; frame++)
            {
                model.Advance(Frame, input, settings);
                peak = Mathf.Max(peak, Mathf.Abs(model.Output.CapturePoint.x));
            }

            return peak;
        }

        // ------------------------------------------------------------
        // The topple.
        // ------------------------------------------------------------

        [Test]
        public void ShoveBeyondReach_TopplesWithALungeThenFalls()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            model.InjectPerturbation(new Vector2(3f, 0f));
            model.Advance(Frame, input);

            Assert.That(model.Phase, Is.EqualTo(BalancePhase.Toppling), "a shove past reach is a topple, not a latch");
            Assert.That(model.LostBalance, Is.False);
            Assert.That(model.Output.Phase, Is.EqualTo(BalancePhase.Toppling));
            Assert.That(model.LungesTaken, Is.GreaterThanOrEqualTo(1), "the lunge is thrown at once");
            Assert.That(model.StepActive, Is.True);
            Assert.That(model.StepIsLunge, Is.True);
            Assert.That(model.Topples, Is.EqualTo(1));
            Assert.That(model.Output.Instability, Is.EqualTo(1f));
            Assert.That(model.FallAxis.x, Is.GreaterThan(0.9f), "the topple's axis points where he is going");
            Assert.That(
                model.Output.Step.To.x,
                Is.GreaterThan(PlayerBalanceSettings.FromIntoxication(1f).MaximumStepReach + 0.05f),
                "a lunge reaches further than a stagger's step");

            int fellAt = -1;
            float peakLean = 0f;
            for (int frame = 1; frame < 90; frame++)
            {
                model.Advance(Frame, input);
                peakLean = Mathf.Max(peakLean, model.LeanDegrees);
                if (model.Phase == BalancePhase.Toppling)
                {
                    Assert.That(
                        Vector2.Dot(model.Output.DriftVelocity, model.ComVelocity),
                        Is.GreaterThanOrEqualTo(0f),
                        "in a topple the root goes the way the centre of mass goes");
                }

                if (model.LostBalance)
                {
                    fellAt = frame;
                    break;
                }
            }

            Assert.That(fellAt, Is.GreaterThan(0), "a three-metre-per-second shove floors him inside a second and a half");
            Assert.That(model.Phase, Is.EqualTo(BalancePhase.Fallen));
            Assert.That(model.Output.LostBalance, Is.True);
            Assert.That(model.FallDirection, Is.EqualTo(1f));
            Assert.That(model.Output.FallDirection, Is.EqualTo(1f));
            Assert.That(model.FallCause, Is.Not.EqualTo(BalanceFallCause.None));
            Assert.That(model.FallCause, Is.Not.EqualTo(BalanceFallCause.Forced));
            Assert.That(model.BraceWeight, Is.EqualTo(1f));
            Assert.That(model.Output.BraceWeight, Is.EqualTo(1f));
            Assert.That(model.FallAxis.magnitude, Is.EqualTo(1f).Within(0.001f));
            Assert.That(model.FallAxis.x, Is.GreaterThan(0.9f));
            Assert.That(model.FallVelocity.x, Is.GreaterThan(0.3f), "the fall carries the momentum he had");
            Assert.That(model.FallAngularVelocity, Is.GreaterThan(0f));
            Assert.That(model.FallLeanDegrees, Is.GreaterThan(15f));
            Assert.That(
                model.FallLeanDegrees,
                Is.LessThanOrEqualTo(PlayerBalanceRules.PointOfNoReturnDegrees + 4f),
                "the point of no return bounds the lean the ragdoll starts from");
            Assert.That(model.Output.FallVelocity, Is.EqualTo(model.FallVelocity));
            Assert.That(model.Output.FallLeanDegrees, Is.EqualTo(model.FallLeanDegrees));
            Assert.That(model.Output.CrouchMetres, Is.GreaterThan(0.05f), "the hip has dropped along the pendulum's arc");

            // Latched: further frames keep the fall.
            for (int frame = 0; frame < 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.Phase, Is.EqualTo(BalancePhase.Fallen));
                Assert.That(model.LostBalance, Is.True);
            }
        }

        [Test]
        public void ShoveLeft_FallsLeft()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            model.InjectPerturbation(new Vector2(-3f, 0f));
            for (int frame = 0; frame < 90 && !model.LostBalance; frame++)
            {
                model.Advance(Frame, input);
            }

            Assert.That(model.LostBalance, Is.True);
            Assert.That(model.FallDirection, Is.EqualTo(-1f));
            Assert.That(model.FallAxis.x, Is.LessThan(-0.9f));
            Assert.That(
                Vector2.Dot(model.FallVelocity, model.FallAxis),
                Is.GreaterThanOrEqualTo(0f),
                "what is handed on never points back toward upright");
        }

        [Test]
        public void ShoveForward_FallsAlongTheForwardAxis()
        {
            // Forward he can run a hard shove off — the sagittal reach is
            // longer and two lunges of it take a three-metre-per-second
            // push. A harder one he cannot.
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            model.InjectPerturbation(new Vector2(0f, 4.5f));
            float peakPitch = 0f;
            bool toppled = false;
            for (int frame = 0; frame < 120 && !model.LostBalance; frame++)
            {
                model.Advance(Frame, input);
                toppled |= model.Phase == BalancePhase.Toppling;
                peakPitch = Mathf.Max(peakPitch, model.Output.LeanPitchDegrees);
            }

            Assert.That(toppled, Is.True);
            Assert.That(model.LostBalance, Is.True, "a four-and-a-half-metre-per-second shove floors him");
            Assert.That(model.FallAxis.y, Is.GreaterThan(0.9f), "a forward topple keeps its forward axis");
            Assert.That(Mathf.Abs(model.FallDirection), Is.EqualTo(1f), "the clip side is still a sign");
            Assert.That(peakPitch, Is.GreaterThan(12f), "the free lean of a topple is not clamped to ten degrees");
        }

        [Test]
        public void ShoveForward_ModerateIsRunOffWithLunges()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            model.InjectPerturbation(new Vector2(0f, 3f));
            bool toppled = false;
            bool recovered = false;
            for (int frame = 0; frame < 120 && !model.LostBalance; frame++)
            {
                model.Advance(Frame, input);
                toppled |= model.Phase == BalancePhase.Toppling;
                if (toppled && model.Phase == BalancePhase.Recovering)
                {
                    recovered = true;
                    break;
                }
            }

            Assert.That(toppled, Is.True);
            Assert.That(recovered, Is.True, "two forward lunges run a three-metre-per-second shove off");
            Assert.That(model.LungesTaken, Is.GreaterThanOrEqualTo(1));
            Assert.That(model.LostBalance, Is.False);
        }

        [Test]
        public void FallsDisallowed_NeverTopple()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f, fallAllowed: false);
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            model.InjectPerturbation(new Vector2(3f, 0f));
            for (int frame = 0; frame < 120; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.Phase, Is.EqualTo(BalancePhase.Steady), "phase at frame " + frame);
                Assert.That(model.LostBalance, Is.False);
            }

            Assert.That(model.LungesTaken, Is.Zero);
            Assert.That(model.Topples, Is.Zero);
            Assert.That(model.BraceWeight, Is.EqualTo(0f));
        }

        [Test]
        public void Grace_PreventsTheTopple()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            model.ArmGrace(10f);
            model.InjectPerturbation(new Vector2(3f, 0f));
            for (int frame = 0; frame < 60; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.Phase, Is.Not.EqualTo(BalancePhase.Toppling), "no topple inside the grace at frame " + frame);
                Assert.That(model.LostBalance, Is.False);
            }
        }

        [Test]
        public void Topple_CanBeRecoveredByALunge()
        {
            // A moderate shove at a moderate level: the lunge lands past
            // the capture point and the fight is won. The strength is
            // scanned because which shove is caught is tuning, while
            // THAT some shove past an ordinary step is caught is the
            // feature.
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.7f);
            bool anyRecovered = false;
            float[] shoves = { 0.8f, 1f, 1.2f, 1.4f };
            foreach (float shove in shoves)
            {
                for (int seed = 0; seed < 8 && !anyRecovered; seed++)
                {
                    PlayerBalanceModel model = new PlayerBalanceModel(seed);
                    for (int frame = 0; frame < 60; frame++)
                    {
                        model.Advance(Frame, input);
                    }

                    model.InjectPerturbation(new Vector2(shove, 0f));
                    bool toppled = false;
                    for (int frame = 0; frame < 180; frame++)
                    {
                        model.Advance(Frame, input);
                        toppled |= model.Phase == BalancePhase.Toppling;
                        if (toppled && model.Phase == BalancePhase.Recovering)
                        {
                            Assert.That(model.LungesTaken, Is.GreaterThanOrEqualTo(1), "the save was a lunge");
                            Assert.That(model.LostBalance, Is.False);
                            Assert.That(model.BraceWeight, Is.LessThanOrEqualTo(1f));
                            anyRecovered = true;
                            break;
                        }

                        if (model.LostBalance)
                        {
                            break;
                        }
                    }
                }
            }

            Assert.That(anyRecovered, Is.True, "no lunge ever caught a shove at level 70");
        }

        [Test]
        public void Recovering_ReturnsToSteadyAndTheBraceComesDown()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.7f);
            PlayerBalanceModel model = FirstRecoveredTopple(input, out int lunges);
            Assert.That(model, Is.Not.Null, "no recovered topple to test");
            Assert.That(lunges, Is.GreaterThanOrEqualTo(1));

            float brace = model.BraceWeight;
            int steadyAt = -1;
            for (int frame = 0; frame < 120; frame++)
            {
                model.Advance(Frame, input);
                Assert.That(model.BraceWeight, Is.LessThanOrEqualTo(brace + 0.0001f), "the brace only comes down while recovering");
                brace = model.BraceWeight;
                if (model.Phase == BalancePhase.Steady)
                {
                    steadyAt = frame;
                    break;
                }

                if (model.Phase == BalancePhase.Toppling || model.LostBalance)
                {
                    // Another lurch came before the first was over; that is allowed.
                    return;
                }
            }

            Assert.That(steadyAt, Is.GreaterThanOrEqualTo(0), "recovering ends in steady");
            Assert.That(
                steadyAt * Frame,
                Is.LessThanOrEqualTo(PlayerBalanceRules.RecoveringSeconds + 2f * Frame));
        }

        private static PlayerBalanceModel FirstRecoveredTopple(in PlayerBalanceInput input, out int lunges)
        {
            lunges = 0;
            float[] shoves = { 0.8f, 1f, 1.2f, 1.4f };
            foreach (float shove in shoves)
            {
                for (int seed = 0; seed < 8; seed++)
                {
                    PlayerBalanceModel model = new PlayerBalanceModel(seed);
                    for (int frame = 0; frame < 60; frame++)
                    {
                        model.Advance(Frame, input);
                    }

                    model.InjectPerturbation(new Vector2(shove, 0f));
                    bool toppled = false;
                    for (int frame = 0; frame < 180; frame++)
                    {
                        model.Advance(Frame, input);
                        toppled |= model.Phase == BalancePhase.Toppling;
                        if (toppled && model.Phase == BalancePhase.Recovering)
                        {
                            lunges = model.LungesTaken;
                            return model;
                        }

                        if (model.LostBalance)
                        {
                            break;
                        }
                    }
                }
            }

            return null;
        }

        [Test]
        public void Topple_RecoveryRateAtLevel80_IsBetweenAThirdAndNineTenths()
        {
            // Offline over 150 seeds the model recovered about six topples
            // in ten at level 80 with no steering; the band is generous
            // because the point is that BOTH outcomes happen.
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.8f);
            int topples = 0;
            int recovered = 0;
            int fell = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                PlayerBalanceModel model = new PlayerBalanceModel(seed);
                BalancePhase previous = BalancePhase.Steady;
                for (int frame = 0; frame < 120 * 60; frame++)
                {
                    model.Advance(Frame, input);
                    BalancePhase phase = model.Phase;
                    if (previous == BalancePhase.Toppling && phase != BalancePhase.Toppling)
                    {
                        topples++;
                        if (phase == BalancePhase.Recovering)
                        {
                            recovered++;
                        }
                        else if (phase == BalancePhase.Fallen)
                        {
                            fell++;
                        }
                    }

                    previous = phase;
                    if (model.LostBalance)
                    {
                        break;
                    }
                }
            }

            Assert.That(topples, Is.GreaterThan(20), "level 80 topples often enough to measure");
            float rate = (float)recovered / topples;
            Assert.That(rate, Is.GreaterThan(0.33f).And.LessThan(0.9f), $"recovered {recovered} of {topples}, fell {fell}");
            Assert.That(fell, Is.GreaterThan(3), "and some topples are lost");
        }

        [Test]
        public void Topple_SteeringTowardTheLeanRaisesRecovery()
        {
            int fallsWithout = CountFalls(false);
            int fallsWith = CountFalls(true);
            Assert.That(fallsWith, Is.LessThan(fallsWithout), $"steering {fallsWith} falls, none {fallsWithout}");
        }

        private static int CountFalls(bool steer)
        {
            int falls = 0;
            for (int seed = 0; seed < 30; seed++)
            {
                PlayerBalanceModel model = new PlayerBalanceModel(seed);
                for (int frame = 0; frame < 90 * 60; frame++)
                {
                    float turn = 0f;
                    if (steer)
                    {
                        float lean = model.ComOffset.x - model.SupportCentre.x;
                        turn = lean > 0.01f ? 1f : (lean < -0.01f ? -1f : 0f);
                    }

                    model.Advance(Frame, PlayerBalanceInput.Quiet(1f).WithTurnInput(turn));
                    if (model.LostBalance)
                    {
                        falls++;
                        break;
                    }
                }
            }

            return falls;
        }

        [Test]
        public void MaximumIntoxication_StillFallsWithinThreeMinutes()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            int fellAt = -1;
            for (int frame = 0; frame < 180 * 60; frame++)
            {
                model.Advance(Frame, input);
                if (model.LostBalance)
                {
                    fellAt = frame;
                    break;
                }
            }

            Assert.That(fellAt, Is.GreaterThanOrEqualTo(0));
            Assert.That(model.Topples, Is.GreaterThanOrEqualTo(1));
            Assert.That(model.FallCause, Is.Not.EqualTo(BalanceFallCause.None));
        }

        // ------------------------------------------------------------
        // Seams and invariants.
        // ------------------------------------------------------------

        [Test]
        public void ForceLoseBalance_CarriesInertiaIntoTheFall()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            model.Advance(Frame, PlayerBalanceInput.Quiet(1f, fallAllowed: false));
            model.ForceLoseBalance(-1f);

            Assert.That(model.Phase, Is.EqualTo(BalancePhase.Fallen));
            Assert.That(model.LostBalance, Is.True);
            Assert.That(model.FallCause, Is.EqualTo(BalanceFallCause.Forced));
            Assert.That(model.FallDirection, Is.EqualTo(-1f));
            Assert.That(model.FallAxis, Is.EqualTo(new Vector2(-1f, 0f)));
            Assert.That(model.FallVelocity.x, Is.EqualTo(-PlayerBalanceRules.ForcedFallVelocity).Within(0.0001f));
            Assert.That(model.FallLeanDegrees, Is.EqualTo(PlayerBalanceRules.ForcedFallLeanDegrees));
            Assert.That(
                model.FallAngularVelocity,
                Is.EqualTo(PlayerBalanceRules.ForcedFallVelocity /
                           (0.95f * Mathf.Cos(30f * Mathf.Deg2Rad))).Within(0.001f));
            Assert.That(model.BraceWeight, Is.EqualTo(1f));
            Assert.That(model.Output.Phase, Is.EqualTo(BalancePhase.Fallen));
            Assert.That(model.Output.BraceWeight, Is.EqualTo(1f));
            Assert.That(model.Output.LeanRollDegrees, Is.LessThan(-20f), "the forced fall shows the lean it claims");
            Assert.That(model.Output.CrouchMetres, Is.GreaterThan(0.1f));
        }

        [Test]
        public void Reset_ClearsTheTopple()
        {
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            model.InjectPerturbation(new Vector2(3f, 0f));
            for (int frame = 0; frame < 90 && !model.LostBalance; frame++)
            {
                model.Advance(Frame, input);
            }

            Assert.That(model.Topples, Is.EqualTo(1));
            model.Reset();

            Assert.That(model.Phase, Is.EqualTo(BalancePhase.Steady));
            Assert.That(model.LungesTaken, Is.Zero);
            Assert.That(model.Topples, Is.Zero);
            Assert.That(model.BraceWeight, Is.EqualTo(0f));
            Assert.That(model.FallCause, Is.EqualTo(BalanceFallCause.None));
            Assert.That(model.FlywheelAngle, Is.EqualTo(Vector2.zero));
            Assert.That(model.FallAxis, Is.EqualTo(Vector2.right));
            Assert.That(model.LeanDegrees, Is.EqualTo(0f));
            Assert.That(model.Output.Phase, Is.EqualTo(BalancePhase.Steady));
            Assert.That(model.Output.TorsoReactionDegrees, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Seed_DeterminesThePhasesAndTheLunges()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            PlayerBalanceModel a = new PlayerBalanceModel(7);
            PlayerBalanceModel b = new PlayerBalanceModel(7);
            for (int frame = 0; frame < 45 * 60; frame++)
            {
                a.Advance(Frame, input);
                b.Advance(Frame, input);
                Assert.That(b.Phase, Is.EqualTo(a.Phase), "phase at frame " + frame);
                Assert.That(b.LungesTaken, Is.EqualTo(a.LungesTaken), "lunges at frame " + frame);
                Assert.That(b.FlywheelAngle, Is.EqualTo(a.FlywheelAngle), "flywheel at frame " + frame);
                if (a.LostBalance)
                {
                    break;
                }
            }

            Assert.That(b.FallCause, Is.EqualTo(a.FallCause));
            Assert.That(b.Topples, Is.EqualTo(a.Topples));
        }

        [Test]
        public void Chunking_DoesNotChangeThePhasesOrTheLunges()
        {
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            PlayerBalanceModel thirty = new PlayerBalanceModel(Seed);
            PlayerBalanceModel sixty = new PlayerBalanceModel(Seed);
            for (int tick = 0; tick < 30 * 30; tick++)
            {
                thirty.Advance(1f / 30f, input);
                sixty.Advance(1f / 60f, input);
                sixty.Advance(1f / 60f, input);
                Assert.That(sixty.Phase, Is.EqualTo(thirty.Phase), "phase at tick " + tick);
                Assert.That(sixty.LungesTaken, Is.EqualTo(thirty.LungesTaken), "lunges at tick " + tick);
                Assert.That(sixty.Topples, Is.EqualTo(thirty.Topples), "topples at tick " + tick);
                Assert.That(sixty.FlywheelAngle.x, Is.EqualTo(thirty.FlywheelAngle.x).Within(1e-4f), "flywheel at tick " + tick);
                if (thirty.LostBalance)
                {
                    break;
                }
            }

            Assert.That(sixty.LostBalance, Is.EqualTo(thirty.LostBalance));
            Assert.That(sixty.FallCause, Is.EqualTo(thirty.FallCause));
        }

        [Test]
        public void Lean_IsMeasuredFromTheBootsNotTheRoot()
        {
            // After a step to the right the boots' midpoint has moved
            // right; a centre of mass over the root reads as a lean to
            // the LEFT of the boots, and the output says so.
            PlayerBalanceModel model = new PlayerBalanceModel(Seed);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(0.6f, fallAllowed: false);
            for (int frame = 0; frame < 30; frame++)
            {
                model.Advance(Frame, input);
            }

            model.InjectPerturbation(new Vector2(0.8f, 0f));
            bool stepped = false;
            for (int frame = 0; frame < 120; frame++)
            {
                model.Advance(Frame, input);
                stepped |= model.StepActive;
                if (stepped && !model.StepActive)
                {
                    break;
                }
            }

            Assert.That(stepped, Is.True, "the shove made him step");
            Vector2 centre = (model.LeftFoot + model.RightFoot) * 0.5f;
            Assert.That(model.SupportCentre, Is.EqualTo(centre));
            // The reference slides from the boots' midpoint toward the
            // boot under the pressure as the stance widens; either way
            // it is never the root.
            float split = Mathf.Clamp01(
                (Vector2.Distance(model.LeftFoot, model.RightFoot) -
                 PlayerBalanceRules.NominalStanceMetres) /
                PlayerBalanceRules.SplitStanceMetres);
            Vector2 reference = Vector2.Lerp(centre, model.CentreOfPressure, split);
            Assert.That(model.LeanReference.x, Is.EqualTo(reference.x).Within(0.0001f));
            float expected = PlayerBalanceRules.LeanDegrees(
                model.ComOffset.x - reference.x,
                PlayerBalanceSettings.DefaultComHeight);
            Assert.That(model.Output.LeanRollDegrees, Is.EqualTo(expected).Within(0.01f));
            Assert.That(
                model.Output.LeanRollDegrees,
                Is.Not.EqualTo(PlayerBalanceRules.LeanDegrees(
                    model.ComOffset.x,
                    PlayerBalanceSettings.DefaultComHeight)).Within(0.01f),
                "the root is not the reference once the boots have moved");
        }
    }
}
