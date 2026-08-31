using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Pins the optional-parameter generalization of the two pure
    /// raven models. Two things must both be true at once: the
    /// cemetery pair, still constructed with the 2-arg and 6-arg
    /// calls, behaves to the exact shipped numbers (flush 3.5,
    /// return 33.6, done 46, timeout 8 — those constants are pinned
    /// here as values, not just referenced); and a roost handing in
    /// its own scene's gates gets a machine that genuinely polls
    /// them. The return-landing-time assertion is the adversarial
    /// catch: the flight constructor derives its whole return
    /// timeline (cruise, deceleration, touch, done) from the speed
    /// parameters, and an implementation that replaced the const
    /// uses in Evaluate but not in the constructor would integrate
    /// distance at 9 m/s against boundaries computed at 6.5 m/s —
    /// the bird would clamp its distance early and hover flapping at
    /// the perch. Landing at the derived time proves the timeline
    /// was re-derived.
    ///
    /// The playtest-5 additions live here too: the reshaped
    /// altitude PROFILE — a takeoff that tops out its whole climb
    /// inside the first 35% of the planar travel and a return that
    /// holds the spawn altitude until only the last 40% remains —
    /// and the seeded takeoff azimuth fan, pinned pure over an
    /// injected clearance predicate, no physics in EditMode.
    /// </summary>
    public sealed class RavenRoostDirectorParameterTests
    {
        private const int SeedA = 0x0A11;
        private const int SeedB = 0x0B22;

        [Test]
        public void Director_DefaultsAreTheCemeteryNumbersUnchanged()
        {
            // The constants themselves stay pinned: they are the
            // compile-time defaults every shipped 2-arg call keeps.
            Assert.That(
                CemeteryRavenDirectorModel.FlushDistanceMeters,
                Is.EqualTo(3.5f));
            Assert.That(
                CemeteryRavenDirectorModel.ReturnDistanceMeters,
                Is.EqualTo(33.6f).Within(0.0001f));

            var model = new CemeteryRavenDirectorModel(SeedA, SeedB);
            model.Arm(true);

            // One step beyond arm's length the pair sits; at 3.5 m
            // of either bird both flush.
            model.Advance(1f, Input(distanceA: 3.51f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle));
            model.Advance(1f, Input(distanceB: 3.5f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Startled));

            model.Advance(1f, Input(
                flightDoneA: true,
                flightDoneB: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away));

            // The return gate opens at exactly 70% of the city's
            // far plane and not a step sooner.
            model.Advance(1f, Input(crownDistance: 33.59f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away));
            model.Advance(1f, Input(crownDistance: 33.6f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.ReturnFlight));
        }

        [Test]
        public void Director_ParameterizedReturnGateMovesTo84()
        {
            // The road's gate: 0.7 of its 120 m far plane. The
            // city's 33.6 m must NOT open it — a bird returning at
            // 33.6 m in the road's thin fog would pop into plain
            // sight at 23.9% visibility.
            var model = new CemeteryRavenDirectorModel(
                SeedA, SeedB, 3.5f, 84f);
            model.Arm(true);
            model.Advance(1f, Input(distanceA: 2f, distanceB: 2f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Startled));
            model.Advance(1f, Input(
                flightDoneA: true,
                flightDoneB: true));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away));

            model.Advance(1f, Input(crownDistance: 33.6f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away),
                "The cemetery's own gate must not leak into a " +
                "parameterized machine.");
            model.Advance(1f, Input(crownDistance: 83.9f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away));
            model.Advance(1f, Input(crownDistance: 84f));
            Assert.That(
                model.Phase,
                Is.EqualTo(CemeteryRavenPhase.ReturnFlight));
        }

        [Test]
        public void Flight_DefaultTakeoffAndReturnKeepTheirNumbers()
        {
            Assert.That(
                CemeteryRavenFlightModel.DoneDistanceMeters,
                Is.EqualTo(46f));
            Assert.That(
                CemeteryRavenFlightModel.TakeoffTimeoutSeconds,
                Is.EqualTo(8f));
            Assert.That(
                CemeteryRavenFlightModel.ClimbSpeedMetersPerSecond,
                Is.EqualTo(7f));
            Assert.That(
                CemeteryRavenFlightModel.GlideSpeedMetersPerSecond,
                Is.EqualTo(6.5f));

            // Default takeoff: at 7.0 s the climb has covered
            // ~42.7 m (under 46 even with the widest seeded arc);
            // at 7.5 s straight-line distance alone passes 46 m.
            Vector3 start = new Vector3(3f, 1f, -2f);
            var takeoff = new CemeteryRavenFlightModel(
                start,
                0f,
                start + new Vector3(46f, 8f, 0f),
                0f,
                CemeteryRavenFlightKind.Takeoff,
                1234);
            Assert.That(takeoff.Evaluate(7.0d).Done, Is.False);
            CemeteryRavenFlightSample doneSample =
                takeoff.Evaluate(7.5d);
            Assert.That(doneSample.Done, Is.True);
            Assert.That(
                PlanarDistance(doneSample.Position, start),
                Is.GreaterThanOrEqualTo(46f - 0.5f));

            // Default return over the spawn distance 46 lands at
            // the timeline the constructor derives from the
            // cemetery speeds — the number a parameterized flight
            // must MOVE, so it is pinned here first.
            Vector3 perch = new Vector3(10f, 0f, 5f);
            var homeFlight = new CemeteryRavenFlightModel(
                perch + new Vector3(46f, 7f, 0f),
                0f,
                perch,
                45f,
                CemeteryRavenFlightKind.Return,
                7);
            float defaultDone = ExpectedReturnDoneSeconds(
                46f,
                CemeteryRavenFlightModel.GlideSpeedMetersPerSecond);
            Assert.That(
                homeFlight.Evaluate(defaultDone - 0.05d).Done,
                Is.False);
            Assert.That(
                homeFlight.Evaluate(defaultDone + 0.05d).Done,
                Is.True);
        }

        [Test]
        public void Flight_ParameterizedTakeoffReachesDoneByDistance()
        {
            // The road contract: done 96, timeout 14, climb 9. At
            // the cemetery's 8 s cap the bird has flown only ~64 m
            // — a flight still honestly in the air, which proves
            // the 8 s default was really replaced. By 12 s the
            // climb has covered ~100 m: done by DISTANCE, before
            // the 14 s guard ever fires.
            Vector3 start = Vector3.zero;
            var flight = new CemeteryRavenFlightModel(
                start,
                0f,
                start + new Vector3(96f, 9f, 0f),
                0f,
                CemeteryRavenFlightKind.Takeoff,
                77,
                96f,
                14f,
                9f,
                9f);
            Assert.That(
                flight.Evaluate(8.0d).Done,
                Is.False,
                "The default 8 s timeout leaked into a " +
                "parameterized takeoff.");
            CemeteryRavenFlightSample sample =
                flight.Evaluate(12.0d);
            Assert.That(
                sample.Done,
                Is.True,
                "96 m at a 9 m/s climb is covered well before " +
                "the 14 s timeout.");
            Assert.That(
                PlanarDistance(sample.Position, start),
                Is.GreaterThanOrEqualTo(96f - 0.5f));
        }

        [Test]
        public void Flight_ParameterizedReturnLandsOnItsOwnTimeline()
        {
            // The adversarial catch. Spawn 96 m out, glide 9 m/s:
            // the constructor must derive cruise/deceleration/
            // refold from THOSE numbers, landing at
            // (96-4)/9 + 8/10.8 + 0.5 ≈ 11.463 s. A constructor
            // still deriving from the 6.5 m/s const would put the
            // boundaries ~4.6 s late and leave the bird hovering
            // at the perch.
            Vector3 perch = new Vector3(-4f, 2f, 11f);
            Vector3 spawn = perch + new Vector3(96f, 7f, 0f);
            var flight = new CemeteryRavenFlightModel(
                spawn,
                0f,
                perch,
                90f,
                CemeteryRavenFlightKind.Return,
                31,
                96f,
                14f,
                9f,
                9f);

            // Mid-glide cross-check: after 5 s at 9 m/s the bird
            // is 51 m from the perch (a 6.5 m/s integration would
            // read 63.5 m).
            Assert.That(
                PlanarDistance(
                    flight.Evaluate(5.0d).Position,
                    perch),
                Is.EqualTo(96f - 9f * 5f).Within(0.05f),
                "The glide must integrate at the parameterized " +
                "speed.");

            float expectedDone =
                ExpectedReturnDoneSeconds(96f, 9f);
            Assert.That(
                flight.Evaluate(expectedDone - 0.05d).Done,
                Is.False);
            CemeteryRavenFlightSample landed =
                flight.Evaluate(expectedDone + 0.05d);
            Assert.That(
                landed.Done,
                Is.True,
                "The return timeline was not re-derived from the " +
                "parameterized speeds.");
            Assert.That(landed.Position, Is.EqualTo(perch));
        }

        [Test]
        public void Flight_TakeoffClimbTopsOutEarlyAndHoldsLevel()
        {
            // The playtest catch: the climb used to be spread over
            // the WHOLE path, so a bird 20 m out had gained only a
            // fraction of its altitude — under every roofline. The
            // reshaped profile must be at 95% of the climb by 40%
            // of the planar travel and exactly level from halfway.
            var start = new Vector3(2f, 1.5f, -3f);
            const float climbMeters = 16f;
            const float doneMeters = 96f;
            var direction = new Vector3(1f, 0f, 0f);
            var flight = new CemeteryRavenFlightModel(
                start,
                0f,
                start + direction * doneMeters +
                Vector3.up * climbMeters,
                0f,
                CemeteryRavenFlightKind.Takeoff,
                5,
                doneMeters,
                14f,
                9f,
                9f);

            bool sawClimbGate = false;
            bool sawLevelSpan = false;
            for (double t = 0d; t <= 14d; t += 0.02d)
            {
                CemeteryRavenFlightSample sample =
                    flight.Evaluate(t);
                if (sample.Done)
                {
                    break;
                }

                // The seeded arc is pure lateral bow, so the dot
                // with the bearing reads the profile's own
                // along-path distance exactly.
                float progress = Vector3.Dot(
                    sample.Position - start,
                    direction) / doneMeters;
                if (progress >= 0.4f && !sawClimbGate)
                {
                    sawClimbGate = true;
                    Assert.That(
                        sample.Position.y - start.y,
                        Is.GreaterThanOrEqualTo(
                            0.95f * climbMeters),
                        "By 40% of the travel the climb must be " +
                        "as good as done.");
                }

                if (progress >= 0.5f)
                {
                    sawLevelSpan = true;
                    Assert.That(
                        sample.Position.y,
                        Is.EqualTo(start.y + climbMeters)
                            .Within(0.001f),
                        "Past halfway the bird cruises exactly " +
                        "level at its end altitude.");
                }
            }

            Assert.That(
                sawClimbGate,
                Is.True,
                "The scan never reached 40% of the travel.");
            Assert.That(
                sawLevelSpan,
                Is.True,
                "The scan never reached the level cruise span.");
        }

        [Test]
        public void Flight_ReturnHoldsSpawnAltitudeUntilLateDescent()
        {
            // The way back in must clear the same rooftops: the old
            // return sank from its first metre, so the reshaped one
            // holds the spawn altitude across the first 60% of the
            // approach and only then smoothsteps down to the flare.
            var perch = new Vector3(-4f, 2f, 11f);
            const float spawnMeters = 96f;
            Vector3 spawn = perch +
                new Vector3(spawnMeters, 7f, 0f);
            var flight = new CemeteryRavenFlightModel(
                spawn,
                0f,
                perch,
                90f,
                CemeteryRavenFlightKind.Return,
                31,
                96f,
                14f,
                9f,
                9f);

            bool sawHeldSpan = false;
            for (double t = 0d; t <= 30d; t += 0.02d)
            {
                CemeteryRavenFlightSample sample =
                    flight.Evaluate(t);
                float covered = 1f -
                    PlanarDistance(sample.Position, perch) /
                    spawnMeters;
                if (covered > 0.55f || sample.Done)
                {
                    break;
                }

                sawHeldSpan = sawHeldSpan || covered >= 0.5f;
                Assert.That(
                    Mathf.Abs(sample.Position.y - spawn.y),
                    Is.LessThanOrEqualTo(0.5f),
                    "The approach must hold the spawn altitude " +
                    "with " + covered + " of the planar distance " +
                    "covered.");
            }

            Assert.That(
                sawHeldSpan,
                Is.True,
                "The scan must reach deep into the held span " +
                "before the descent begins.");
        }

        [Test]
        public void SelectTakeoffAzimuth_SeedsTheFanAndHonoursClearance()
        {
            Vector3 away = Vector3.forward;

            // All clear: the seeded shuffle must actually vary the
            // first pick, or every flush flies the one line the
            // playtest complained about.
            var firstPicks = new HashSet<int>();
            for (int seed = 1; seed <= 8; seed++)
            {
                Vector3 picked =
                    RavenRoostPlan.SelectTakeoffAzimuth(
                        away,
                        seed,
                        direction => true);
                firstPicks.Add(Mathf.RoundToInt(
                    Vector3.SignedAngle(
                        away,
                        picked,
                        Vector3.up)));

                // Deterministic per seed: the same flush replays
                // the same line, bit for bit.
                Assert.That(
                    RavenRoostPlan.SelectTakeoffAzimuth(
                        away,
                        seed,
                        direction => true),
                    Is.EqualTo(picked),
                    "Seed " + seed + " must redraw its own fan.");
            }

            Assert.That(
                firstPicks.Count,
                Is.GreaterThanOrEqualTo(2),
                "Eight flushes with everything clear must not all " +
                "open on the same bearing.");

            // One open canyon: whatever order the seed deals, the
            // fan must fall through to the only clear line — which
            // also proves the shuffle is a permutation that drops
            // no candidate.
            Vector3 openLine =
                Quaternion.Euler(0f, 50f, 0f) * away;
            for (int seed = 1; seed <= 8; seed++)
            {
                Vector3 picked =
                    RavenRoostPlan.SelectTakeoffAzimuth(
                        away,
                        seed,
                        direction => Vector3.Angle(
                            direction,
                            openLine) < 0.1f);
                Assert.That(
                    Vector3.Angle(picked, openLine),
                    Is.LessThan(0.01f),
                    "Seed " + seed + " must reach the only clear " +
                    "candidate.");
            }
        }

        /// <summary>
        /// The constructor's own return timeline, restated: cruise
        /// over all but the braking length at glide speed, a linear
        /// deceleration to touchdown speed over that length, and the
        /// refold. Written from the model's public constants so a
        /// tuning change moves the expectation with it.
        /// </summary>
        private static float ExpectedReturnDoneSeconds(
            float spawnDistanceMeters,
            float glideSpeedMetersPerSecond)
        {
            float braking = Mathf.Min(
                CemeteryRavenFlightModel.DecelerationDistanceMeters,
                spawnDistanceMeters);
            float cruiseSeconds =
                (spawnDistanceMeters - braking) /
                glideSpeedMetersPerSecond;
            float decelerationSeconds =
                braking * 2f /
                (glideSpeedMetersPerSecond +
                 CemeteryRavenFlightModel
                     .TouchdownSpeedMetersPerSecond);
            return cruiseSeconds +
                   decelerationSeconds +
                   CemeteryRavenFlightModel.RefoldSeconds;
        }

        private static float PlanarDistance(
            Vector3 left,
            Vector3 right)
        {
            return new Vector2(
                left.x - right.x,
                left.z - right.z).magnitude;
        }

        /// <summary>One polled frame as plain values, the director
        /// tests' own idiom: distances default to a hero standing
        /// well clear so a test names only what it is about.</summary>
        private static CemeteryRavenDirectorInput Input(
            float distanceA = 40f,
            float distanceB = 40f,
            float crownDistance = 10f,
            bool sessionActive = false,
            bool flightDoneA = false,
            bool flightDoneB = false)
        {
            return new CemeteryRavenDirectorInput(
                distanceA,
                distanceB,
                crownDistance,
                sessionActive,
                false,
                flightDoneA,
                flightDoneB);
        }
    }
}
