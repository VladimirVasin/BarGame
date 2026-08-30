using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CemeteryRavenModelTests
    {
        [Test]
        public void IdleModel_IsInvariantToFrameChunking()
        {
            // A frame-rate-exact chunk (1/128 s, a power of two) keeps
            // the chunked sum float-exact over thirty seconds, so any
            // divergence between the two models is a real state bug
            // and never accumulation noise.
            const float chunk = 0.0078125f;
            const int steps = 3840;
            int[] seeds = { 321, 0x5EED, -7 };
            double[] offsets = { 0d, 3.75d, 11.25d };

            for (int pick = 0; pick < seeds.Length; pick++)
            {
                var oneChunk = new CemeteryRavenIdleModel(
                    seeds[pick],
                    offsets[pick]);
                var manyChunks = new CemeteryRavenIdleModel(
                    seeds[pick],
                    offsets[pick]);

                oneChunk.Advance(chunk * steps);
                for (int index = 0; index < steps; index++)
                {
                    manyChunks.Advance(chunk);
                }

                string label =
                    "seed " + seeds[pick] +
                    " offset " + offsets[pick];
                Assert.That(
                    manyChunks.ElapsedSeconds,
                    Is.EqualTo(oneChunk.ElapsedSeconds)
                        .Within(0.000000001d),
                    label);
                Assert.That(
                    manyChunks.CurrentKind,
                    Is.EqualTo(oneChunk.CurrentKind),
                    label);
                Assert.That(
                    manyChunks.EventProgress01,
                    Is.EqualTo(oneChunk.EventProgress01)
                        .Within(0.0001f),
                    label);
                Assert.That(
                    manyChunks.Breathe01,
                    Is.EqualTo(oneChunk.Breathe01).Within(0.0001f),
                    label);
                Assert.That(
                    manyChunks.EventSign,
                    Is.EqualTo(oneChunk.EventSign),
                    label);
            }
        }

        [Test]
        public void IdleModel_RunsExactlyOneSpecialPerCycle()
        {
            var model = new CemeteryRavenIdleModel(321, 0d);
            const float step = 0.01f;
            const int cycles = 2;

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                int specialRuns = 0;
                bool inSpecial = false;
                var runKind = CemeteryRavenIdleKind.Breathe;
                double specialStart = 0d;
                double specialEnd = 0d;
                double cycleStart =
                    cycle * (double)CemeteryRavenIdleModel.CycleSeconds;
                double cycleEnd =
                    cycleStart + CemeteryRavenIdleModel.CycleSeconds;

                while (model.ElapsedSeconds < cycleEnd - step * 0.5d)
                {
                    model.Advance(step);
                    bool special =
                        model.CurrentKind !=
                        CemeteryRavenIdleKind.Breathe;
                    if (special && !inSpecial)
                    {
                        specialRuns++;
                        runKind = model.CurrentKind;
                        specialStart =
                            model.ElapsedSeconds - cycleStart;
                    }
                    else if (special)
                    {
                        Assert.That(
                            model.CurrentKind,
                            Is.EqualTo(runKind),
                            "One special never turns into another " +
                            "mid-run.");
                    }

                    if (special)
                    {
                        specialEnd =
                            model.ElapsedSeconds - cycleStart;
                    }

                    inSpecial = special;
                }

                Assert.That(
                    specialRuns,
                    Is.EqualTo(1),
                    "Cycle " + cycle + " must carry exactly one " +
                    "special: the hash picks WHICH, not whether two " +
                    "overlap.");
                Assert.That(
                    runKind,
                    Is.EqualTo(CemeteryRavenIdleKind.WeightShift)
                        .Or.EqualTo(CemeteryRavenIdleKind.WingRuffle));
                Assert.That(
                    specialStart,
                    Is.GreaterThanOrEqualTo(
                        CemeteryRavenIdleModel
                            .SpecialWindowStartSeconds - 0.02d));
                Assert.That(
                    specialEnd,
                    Is.LessThanOrEqualTo(
                        CemeteryRavenIdleModel
                            .SpecialWindowEndSeconds +
                        CemeteryRavenIdleModel
                            .WingRuffleDurationSeconds + 0.02d));
            }
        }

        [Test]
        public void IdleModel_PreenSuppressesHeadTracking()
        {
            var model = new CemeteryRavenIdleModel(321, 0d);
            model.Advance(
                CemeteryRavenIdleModel.FirstPreenStartSeconds + 1.2f);

            Assert.That(model.IsPreening, Is.True);
            Assert.That(
                model.CurrentKind,
                Is.EqualTo(CemeteryRavenIdleKind.Preen));

            // Mid-preen the ramp is fully in: the pose REPLACES the
            // tracked yaw with the coverts turn, so the hero can walk
            // where he likes without the bird's beak following him.
            CemeteryRavenPose pose = CemeteryRavenPoseRules.IdlePose(
                CemeteryRavenIdleKind.Preen,
                0.5f,
                model.Breathe01,
                model.EventSign,
                model.PreenOnLeftWing,
                50f);
            float side = model.PreenOnLeftWing ? 1f : -1f;
            Assert.That(
                pose.HeadYawDegrees,
                Is.EqualTo(
                    side *
                    CemeteryRavenPoseRules.PreenHeadYawDegrees)
                    .Within(0.001f));
            Assert.That(pose.HeadYawDegrees, Is.Not.EqualTo(50f));
            Assert.That(
                pose.HeadPitchDegrees,
                Is.EqualTo(
                    CemeteryRavenPoseRules.PreenHeadPitchDegrees)
                    .Within(0.001f));

            // And the preened wing lifts while the other stays home.
            if (model.PreenOnLeftWing)
            {
                Assert.That(pose.WingFlapLeftDegrees, Is.Not.Zero);
                Assert.That(pose.WingFlapRightDegrees, Is.Zero);
            }
            else
            {
                Assert.That(pose.WingFlapRightDegrees, Is.Not.Zero);
                Assert.That(pose.WingFlapLeftDegrees, Is.Zero);
            }
        }

        [Test]
        public void HeadModel_UsesAngularHysteresisRateLimitAndClamp()
        {
            var model = new CemeteryRavenHeadModel();

            // Below the enter threshold the head cannot be bothered.
            model.Update(
                true,
                5f,
                CemeteryRavenHeadModel.DefaultEnterErrorDegrees - 1f,
                0.5f);
            Assert.That(model.CurrentYawDegrees, Is.Zero);
            Assert.That(model.IsTurning, Is.False);

            // Above it the turn runs rate-limited, not teleported.
            model.Update(true, 5f, 60f, 0.1f);
            Assert.That(model.IsTurning, Is.True);
            Assert.That(
                model.CurrentYawDegrees,
                Is.EqualTo(
                    CemeteryRavenHeadModel
                        .DefaultTurnDegreesPerSecond * 0.1f)
                    .Within(0.001f));

            // Given time it settles and stops turning.
            model.Update(true, 5f, 60f, 1f);
            Assert.That(model.CurrentYawDegrees, Is.EqualTo(60f));
            Assert.That(model.IsTurning, Is.False);

            // Tracking never exceeds the clamp.
            model.Update(true, 5f, -170f, 5f);
            Assert.That(
                model.CurrentYawDegrees,
                Is.EqualTo(
                    -CemeteryRavenHeadModel
                        .DefaultMaxTrackYawDegrees));

            model.Reset();
            Assert.That(model.CurrentYawDegrees, Is.Zero);
            Assert.That(
                model.Update(true, 5f, float.NaN, 0.1f),
                Is.Zero);
        }

        [Test]
        public void HeadModel_DropsTheTargetBeyondTheDistanceCutoff()
        {
            var model = new CemeteryRavenHeadModel();
            model.Update(true, 5f, 60f, 1f);
            Assert.That(model.CurrentYawDegrees, Is.EqualTo(60f));

            // Past the cutoff the target counts as gone: the head
            // comes home through the same hysteresis, never freezing
            // mid-stare and never leading a man into the fog.
            model.Update(
                true,
                CemeteryRavenHeadModel.MaxTrackDistanceMeters + 2f,
                60f,
                1f);
            Assert.That(model.CurrentYawDegrees, Is.Zero);
            Assert.That(model.IsTurning, Is.False);

            // A missing target behaves identically.
            model.Update(true, 5f, 60f, 1f);
            Assert.That(model.CurrentYawDegrees, Is.EqualTo(60f));
            model.Update(false, 5f, 60f, 1f);
            Assert.That(model.CurrentYawDegrees, Is.Zero);

            // At the cutoff itself the bird still tracks: the gate is
            // "further than", not "this far".
            model.Update(
                true,
                CemeteryRavenHeadModel.MaxTrackDistanceMeters,
                40f,
                1f);
            Assert.That(model.CurrentYawDegrees, Is.EqualTo(40f));
        }

        [Test]
        public void FlightModel_TakeoffClimbsAwayFromTheHeroAndEndsBeyondTheFog()
        {
            var hero = new Vector3(10f, 0f, 0f);
            var start = new Vector3(0f, 0.31f, 0f);
            Vector3 away = (start - hero).normalized;
            Vector3 end = start + away * 46f + Vector3.up * 8f;
            var flight = new CemeteryRavenFlightModel(
                start,
                90f,
                end,
                90f,
                CemeteryRavenFlightKind.Takeoff,
                0x0A11);

            float previousDistance = PlanarDistance(start, hero);
            double doneTime = double.NaN;
            for (double t = 0d; t <= 8.5d; t += 0.05d)
            {
                CemeteryRavenFlightSample sample =
                    flight.Evaluate(t);
                float distance = PlanarDistance(
                    sample.Position,
                    hero);
                Assert.That(
                    distance,
                    Is.GreaterThanOrEqualTo(
                        previousDistance - 0.001f),
                    "The bird recedes from the hero at t=" + t);
                previousDistance = distance;
                Assert.That(
                    sample.WingFold01,
                    Is.EqualTo(Mathf.Clamp01(
                        (float)t /
                        CemeteryRavenFlightModel.WingDeploySeconds))
                        .Within(0.001f));
                if (sample.Done && double.IsNaN(doneTime))
                {
                    doneTime = t;
                    bool pastTheFog =
                        PlanarDistance(sample.Position, start) >=
                        CemeteryRavenFlightModel.DoneDistanceMeters -
                        0.001f;
                    bool timedOut =
                        t >= CemeteryRavenFlightModel
                            .TakeoffTimeoutSeconds - 0.001d;
                    Assert.That(
                        pastTheFog || timedOut,
                        Is.True,
                        "Done means past the fog or timed out.");
                }
            }

            Assert.That(
                double.IsNaN(doneTime),
                Is.False,
                "The takeoff must finish within its own timeout.");
            Assert.That(
                doneTime,
                Is.LessThanOrEqualTo(
                    CemeteryRavenFlightModel.TakeoffTimeoutSeconds +
                    0.05d));

            // Purity: the timeline is a function of absolute time, so
            // the same instant reads the same whatever was evaluated
            // before it — frame chunking cannot exist for it.
            CemeteryRavenFlightSample once = flight.Evaluate(3.0d);
            flight.Evaluate(7.0d);
            flight.Evaluate(0.5d);
            CemeteryRavenFlightSample again = flight.Evaluate(3.0d);
            Assert.That(again.Position, Is.EqualTo(once.Position));
            Assert.That(
                again.YawDegrees,
                Is.EqualTo(once.YawDegrees));
            Assert.That(
                again.FlapPhaseRadians,
                Is.EqualTo(once.FlapPhaseRadians));
            Assert.That(again.Phase, Is.EqualTo(once.Phase));
        }

        [Test]
        public void FlightModel_ReturnLandsExactlyOnThePerchWithWingsFolded()
        {
            var perch = new Vector3(3.2f, 0.31f, -5.7f);
            Vector3 spawn = perch +
                new Vector3(0.6f, 0f, 0.8f) * 46f +
                Vector3.up * 7f;
            var flight = new CemeteryRavenFlightModel(
                spawn,
                0f,
                perch,
                137f,
                CemeteryRavenFlightKind.Return,
                0x0B22);

            bool sawFlare = false;
            bool sawSettle = false;
            CemeteryRavenFlightSample landed = default;
            for (double t = 0d; t <= 30d; t += 0.02d)
            {
                CemeteryRavenFlightSample sample =
                    flight.Evaluate(t);
                if (sample.Phase == CemeteryRavenFlightPhase.Flare)
                {
                    sawFlare = true;
                    Assert.That(sample.WingFold01, Is.EqualTo(1f));
                    Assert.That(
                        sample.BodyPitchDegrees,
                        Is.GreaterThanOrEqualTo(0f));
                    Assert.That(
                        sample.BodyPitchDegrees,
                        Is.LessThanOrEqualTo(
                            CemeteryRavenFlightModel
                                .FlareBodyPitchDegrees + 0.001f));
                }

                if (sample.Phase == CemeteryRavenFlightPhase.Settle)
                {
                    sawSettle = true;
                    Assert.That(
                        Vector3.Distance(sample.Position, perch),
                        Is.LessThan(0.001f),
                        "The settle happens ON the perch.");
                }

                if (sample.Done)
                {
                    landed = sample;
                    break;
                }
            }

            Assert.That(sawFlare, Is.True, "The landing flares.");
            Assert.That(sawSettle, Is.True, "The landing settles.");
            Assert.That(landed.Done, Is.True);
            Assert.That(
                Vector3.Distance(landed.Position, perch),
                Is.LessThan(0.001f),
                "Touch is float-exact on the perch point.");
            Assert.That(landed.WingFold01, Is.Zero);
            Assert.That(landed.BodyPitchDegrees, Is.Zero);
            Assert.That(landed.BodyDipMeters, Is.Zero);
            Assert.That(
                landed.YawDegrees,
                Is.EqualTo(137f).Within(0.001f));
        }

        [Test]
        public void PoseRules_MapTimelineMomentsToPivotDeltas()
        {
            // Weight shift: lean out over one leg with the counter
            // head turn, peaking mid-event.
            CemeteryRavenPose shift = CemeteryRavenPoseRules.IdlePose(
                CemeteryRavenIdleKind.WeightShift,
                0.5f,
                0.5f,
                1f,
                false,
                20f);
            Assert.That(
                shift.BodyLeanDegrees,
                Is.EqualTo(
                    CemeteryRavenPoseRules.WeightShiftLeanDegrees)
                    .Within(0.001f));
            Assert.That(
                shift.HeadYawDegrees,
                Is.EqualTo(
                    20f -
                    CemeteryRavenPoseRules
                        .WeightShiftCounterHeadYawDegrees)
                    .Within(0.001f));

            // Wing ruffle: both wings out together, symmetric.
            CemeteryRavenPose ruffle = CemeteryRavenPoseRules.IdlePose(
                CemeteryRavenIdleKind.WingRuffle,
                0.5f,
                0.5f,
                1f,
                false,
                0f);
            Assert.That(
                ruffle.WingFoldLeftDegrees,
                Is.EqualTo(
                    CemeteryRavenPoseRules.WingRuffleFoldDegrees)
                    .Within(0.001f));
            Assert.That(
                ruffle.WingFoldRightDegrees,
                Is.EqualTo(ruffle.WingFoldLeftDegrees));
            Assert.That(
                ruffle.WingFlapRightDegrees,
                Is.EqualTo(ruffle.WingFlapLeftDegrees));

            // Breathe: only the faint dip and the tracked yaw.
            CemeteryRavenPose breathe = CemeteryRavenPoseRules.IdlePose(
                CemeteryRavenIdleKind.Breathe,
                0f,
                1f,
                1f,
                false,
                12f);
            Assert.That(breathe.HeadYawDegrees, Is.EqualTo(12f));
            Assert.That(
                Mathf.Abs(breathe.BodyDipMeters),
                Is.LessThanOrEqualTo(
                    CemeteryRavenPoseRules
                        .BreatheDipAmplitudeMeters + 0.0001f));
            Assert.That(breathe.WingFoldLeftDegrees, Is.Zero);

            // Flight: fold scales the whole wing and the flap scales
            // with the fold, so deploy, flight and refold read as one
            // motion; the tail trails the body pitch.
            CemeteryRavenPose airborne =
                CemeteryRavenPoseRules.FlightPose(
                    1f,
                    Mathf.PI * 0.5f,
                    18f,
                    0.03f);
            Assert.That(
                airborne.WingFoldLeftDegrees,
                Is.EqualTo(
                    CemeteryRavenPoseRules.WingFoldMaximumDegrees));
            Assert.That(
                airborne.WingFlapLeftDegrees,
                Is.EqualTo(
                    CemeteryRavenPoseRules
                        .FlightFlapAmplitudeDegrees)
                    .Within(0.001f));
            Assert.That(airborne.BodyPitchDegrees, Is.EqualTo(18f));
            Assert.That(airborne.BodyDipMeters, Is.EqualTo(0.03f));
            Assert.That(
                airborne.TailPitchDegrees,
                Is.EqualTo(
                    18f * CemeteryRavenPoseRules.TailFollowFactor)
                    .Within(0.001f));

            CemeteryRavenPose halfDeployed =
                CemeteryRavenPoseRules.FlightPose(
                    0.5f,
                    Mathf.PI * 0.5f,
                    0f,
                    0f);
            Assert.That(
                halfDeployed.WingFoldLeftDegrees,
                Is.EqualTo(
                    CemeteryRavenPoseRules.WingFoldMaximumDegrees *
                    0.5f)
                    .Within(0.001f));
            Assert.That(
                halfDeployed.WingFlapLeftDegrees,
                Is.EqualTo(
                    CemeteryRavenPoseRules
                        .FlightFlapAmplitudeDegrees * 0.5f)
                    .Within(0.001f));

            // The manifest contract: the runtime never swings a wing
            // past the arc the geometry was modelled to sweep.
            Assert.That(
                CemeteryRavenPoseRules.WingFoldMaximumDegrees,
                Is.EqualTo(70f));
        }

        private static float PlanarDistance(
            Vector3 left,
            Vector3 right)
        {
            return new Vector2(
                left.x - right.x,
                left.z - right.z).magnitude;
        }
    }
}
