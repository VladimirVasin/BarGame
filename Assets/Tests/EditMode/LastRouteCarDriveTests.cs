using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// How the Ferryman's car takes a road, as pure data: the centreline it
    /// reads, and the speed it is willing to carry along it.
    ///
    /// Every failure this file guards against is silent from the outside. A
    /// car that overshoots the end of the path parks in the cafe wall; one
    /// that never quite stops leaves the hero unable to get out, because the
    /// seat's exit is gated on the car being still; one that takes an R`7.5 m`
    /// hairpin at cruise reads as a car on rails rather than a man driving.
    /// None of them throws.
    /// </summary>
    public sealed class LastRouteCarDriveTests
    {
        /// <summary>The mountain's own hairpin radius, which is what the
        /// cornering limit has to be judged against.</summary>
        private const float HairpinRadius = 7.5f;

        private static LastRouteCarDrivePath BuildStraight(
            float length,
            float step = 1f)
        {
            var points = new List<Vector3>();
            for (float distance = 0f; distance < length; distance += step)
            {
                points.Add(new Vector3(0f, 0f, distance));
            }

            points.Add(new Vector3(0f, 0f, length));
            return new LastRouteCarDrivePath(points);
        }

        /// <summary>
        /// A straight, a 180-degree arc of the mountain's own radius, and a
        /// straight out of it - the shape of every one of the ten hairpins.
        /// </summary>
        private static LastRouteCarDrivePath BuildHairpin(
            float leadIn = 60f,
            float leadOut = 60f)
        {
            var points = new List<Vector3>();
            for (float distance = 0f; distance < leadIn; distance += 1f)
            {
                points.Add(new Vector3(0f, 0f, distance - leadIn));
            }

            var center = new Vector3(HairpinRadius, 0f, 0f);
            const int divisions = 24;
            for (int index = 0; index <= divisions; index++)
            {
                float angle = Mathf.PI * index / divisions;
                points.Add(center + new Vector3(
                    -Mathf.Cos(angle) * HairpinRadius,
                    0f,
                    Mathf.Sin(angle) * HairpinRadius));
            }

            for (float distance = 1f; distance <= leadOut; distance += 1f)
            {
                points.Add(new Vector3(
                    HairpinRadius * 2f,
                    0f,
                    -distance));
            }

            return new LastRouteCarDrivePath(points);
        }

        [Test]
        public void Path_MeasuresItsOwnLengthAndWeldsRepeatedPoints()
        {
            var points = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(3f, 0f, 0f),
                new Vector3(3f, 0f, 4f)
            };
            var path = new LastRouteCarDrivePath(points);

            Assert.That(
                path.PointCount,
                Is.EqualTo(3),
                "The duplicated start is one point, not two. The city leg is " +
                "three sources concatenated and every seam repeats a point.");
            Assert.That(
                path.Length,
                Is.EqualTo(7f).Within(0.0001f),
                "Three metres along X and four along Z, and the welded " +
                "duplicate contributes nothing.");
        }

        [Test]
        public void Path_SamplesInsideAndClampsOutside()
        {
            LastRouteCarDrivePath path = BuildStraight(20f);

            path.Sample(7.5f, out Vector3 position, out Vector3 forward);
            Assert.That(position.z, Is.EqualTo(7.5f).Within(0.001f));
            Assert.That(
                Vector3.Angle(forward, Vector3.forward),
                Is.LessThan(0.01f));

            path.Sample(-40f, out Vector3 before, out _);
            path.Sample(400f, out Vector3 after, out _);
            Assert.That(before.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                after.z,
                Is.EqualTo(20f).Within(0.001f),
                "Past the end is the end, never an extrapolation off the road.");
        }

        [Test]
        public void Path_ReadsAHairpinAsTheRadiusItWasDrawnAt()
        {
            LastRouteCarDrivePath path = BuildHairpin();

            // Degrees of heading per metre round a circle is 180/(pi*r).
            float expected = 180f / (Mathf.PI * HairpinRadius);
            float measured = path.MaximumTurnRate(0f, path.Length);
            Assert.That(
                measured,
                Is.EqualTo(expected).Within(expected * 0.08f),
                "The turn rate is the arc's own curvature, so the cornering " +
                "limit is a radius the mountain planner can be checked against.");

            Assert.That(
                path.MaximumTurnRate(0f, 40f),
                Is.EqualTo(0f).Within(0.0001f),
                "The straight lead-in does not borrow the corner's sharpness.");
        }

        [Test]
        public void Model_PullsAwayCruisesAndStopsExactlyAtTheEnd()
        {
            LastRouteCarDrivePath path = BuildStraight(200f);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.City);

            Assert.That(model.Speed, Is.EqualTo(0f));
            Assert.That(model.HasArrived, Is.False);

            float previousDistance = -1f;
            for (int frame = 0; frame < 4000 && !model.HasArrived; frame++)
            {
                model.Advance(1f / 60f);
                Assert.That(
                    model.Speed,
                    Is.GreaterThanOrEqualTo(0f),
                    "This car never reverses.");
                Assert.That(
                    model.Distance,
                    Is.GreaterThanOrEqualTo(previousDistance),
                    "Distance covered is monotone.");
                Assert.That(
                    model.Distance,
                    Is.LessThanOrEqualTo(path.Length + 0.0001f),
                    "It must never drive off the end of its own path.");
                previousDistance = model.Distance;
            }

            Assert.That(
                model.HasArrived,
                Is.True,
                "A two hundred metre street leg finishes inside a minute.");
            Assert.That(
                model.Distance,
                Is.EqualTo(path.Length).Within(0.01f));
            Assert.That(model.Speed, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Model_ReachesCruiseOnALongStraight()
        {
            LastRouteCarDrivePath path = BuildStraight(400f);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.City);

            float fastest = 0f;
            for (int frame = 0; frame < 4000 && !model.HasArrived; frame++)
            {
                model.Advance(1f / 60f);
                fastest = Mathf.Max(fastest, model.Speed);
            }

            Assert.That(
                fastest,
                Is.EqualTo(LastRouteCarDriveProfile.City.CruiseSpeed)
                    .Within(0.05f),
                "Given room, it drives at its cruise and not below it.");
        }

        [Test]
        public void Model_SlowsBeforeAHairpinRatherThanInsideIt()
        {
            LastRouteCarDrivePath path = BuildHairpin();
            LastRouteCarDriveProfile profile =
                LastRouteCarDriveProfile.Mountain;
            var model = new LastRouteCarDriveModel(path, profile);

            const float cornerEntryDistance = 60f;
            float arcLength = Mathf.PI * HairpinRadius;
            // The turn rate at the join is half a corner's worth - one straight
            // segment meeting one arc segment - so the geometry is only fully
            // curved a little way in. Judge the steady state over the middle of
            // the arc, and judge the approach at the entry.
            float settledFrom = cornerEntryDistance + (arcLength * 0.25f);
            float settledTo = cornerEntryDistance + (arcLength * 0.75f);

            float speedAtEntry = float.MaxValue;
            float fastestSettled = 0f;
            float worstLateral = 0f;
            for (int frame = 0; frame < 6000 && !model.HasArrived; frame++)
            {
                model.Advance(1f / 60f);
                if (model.Distance >= cornerEntryDistance &&
                    speedAtEntry > profile.CruiseSpeed)
                {
                    speedAtEntry = model.Speed;
                }

                if (model.Distance >= settledFrom &&
                    model.Distance <= settledTo)
                {
                    fastestSettled = Mathf.Max(fastestSettled, model.Speed);
                }

                if (model.Distance > cornerEntryDistance &&
                    model.Distance < cornerEntryDistance + arcLength)
                {
                    worstLateral = Mathf.Max(
                        worstLateral,
                        Mathf.Abs(model.LateralAcceleration));
                }
            }

            Assert.That(
                speedAtEntry,
                Is.LessThan(profile.CruiseSpeed - 1.5f),
                "It must already be well off cruise at the mouth of the bend. " +
                "A car that arrives at cruise is a car that never saw the " +
                "corner coming, which is what the look-ahead horizon exists " +
                "to prevent.");

            // Judged against the curvature this path actually carries rather
            // than against the ideal radius it was drawn from. A 24-chord arc
            // measures a few per cent shy of a true circle - which is what the
            // radius test above pins - and that belongs in one assertion, not
            // silently in the tolerance of this one.
            float allowed = model.EvaluateCorneringSpeed(
                path.MaximumTurnRate(settledFrom, settledTo));

            // What the model promises is that it ARRIVES at each vertex at
            // that vertex's own cornering speed. Between two vertices it is
            // still on the brakes, so it may legitimately be one sample's
            // worth of braking above the steady figure - and both real paths
            // here are sampled at a metre.
            float permitted = Mathf.Sqrt(
                (allowed * allowed) + (2f * profile.Braking * 1.1f));
            Assert.That(
                fastestSettled,
                Is.LessThanOrEqualTo(permitted),
                "Through the body of the bend it sits at the cornering speed " +
                "its own curvature allows, give or take the metre it is still " +
                "braking over.");
            Assert.That(
                allowed,
                Is.LessThan(profile.CruiseSpeed),
                "The bend has to be imposing a limit at all, or the assertion " +
                "above proves nothing.");
            Assert.That(
                worstLateral,
                Is.LessThanOrEqualTo(
                    profile.MaximumLateralAcceleration * 1.35f),
                "Side load through the bend stays near the profile's ceiling.");
        }

        [Test]
        public void Model_CorneringSpeedFallsWithTheRadiusAndHasAFloor()
        {
            var model = new LastRouteCarDriveModel(
                BuildStraight(10f),
                LastRouteCarDriveProfile.Mountain);

            float gentle = model.EvaluateCorneringSpeed(1f);
            float sharp = model.EvaluateCorneringSpeed(12f);
            Assert.That(
                sharp,
                Is.LessThan(gentle),
                "A sharper bend is a slower one.");
            Assert.That(
                model.EvaluateCorneringSpeed(0f),
                Is.EqualTo(LastRouteCarDriveProfile.Mountain.CruiseSpeed),
                "A straight imposes no limit at all.");
            Assert.That(
                model.EvaluateCorneringSpeed(1000f),
                Is.EqualTo(
                    LastRouteCarDriveProfile.Mountain.MinimumCorneringSpeed),
                "However sharp the vertex, the car keeps walking through it - " +
                "a cornering limit of zero would strand the whole beat.");
        }

        [Test]
        public void Model_SurvivesAHitchWithoutDrivingThroughTheEnd()
        {
            LastRouteCarDrivePath path = BuildStraight(40f);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.City);

            // One second handed in whole, repeatedly: the dropped-frame case
            // the sub-stepping exists for.
            for (int frame = 0; frame < 60 && !model.HasArrived; frame++)
            {
                model.Advance(1f);
                Assert.That(
                    model.Distance,
                    Is.LessThanOrEqualTo(path.Length + 0.0001f));
            }

            Assert.That(model.HasArrived, Is.True);
            Assert.That(
                model.Distance,
                Is.EqualTo(path.Length).Within(0.01f));
        }

        [Test]
        public void Model_IgnoresNonFiniteAndNegativeSteps()
        {
            var model = new LastRouteCarDriveModel(
                BuildStraight(50f),
                LastRouteCarDriveProfile.City);

            model.Advance(float.NaN);
            model.Advance(float.PositiveInfinity);
            model.Advance(-4f);

            Assert.That(model.Distance, Is.EqualTo(0f));
            Assert.That(model.Speed, Is.EqualTo(0f));
        }

        [Test]
        public void Model_StopsAtAHoldAndGoesAgainWhenItIsLifted()
        {
            LastRouteCarDrivePath path = BuildStraight(120f);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.City);
            model.SetHold(60f);

            Advance(model, 30f);

            Assert.That(
                model.Distance,
                Is.EqualTo(60f).Within(0.35f),
                "A car held at a line settles onto it, the same way it " +
                "settles onto the end of the road.");
            Assert.That(model.Speed, Is.LessThan(0.05f));
            Assert.That(
                model.HasArrived,
                Is.False,
                "Waiting is not arriving. The seat's exit is gated on the " +
                "car being finished, and a passenger let out at a give-way " +
                "line steps into moving traffic.");
            Assert.That(model.IsWaiting, Is.True);

            model.ReleaseHold();
            Advance(model, 30f);

            Assert.That(model.HasArrived, Is.True);
            Assert.That(model.IsWaiting, Is.False);
        }

        [Test]
        public void Model_BrakesForAHoldRatherThanBeingStoppedAtIt()
        {
            LastRouteCarDrivePath path = BuildStraight(200f);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.City);
            Advance(model, 20f);
            float cruising = model.Speed;
            Assert.That(
                cruising,
                Is.GreaterThan(6f),
                "It has to be up to speed for this to prove anything.");

            // Armed with barely enough road to stop in. The point is that it
            // costs the hardest stop the car has and a little overshoot -
            // never a frame in which the car simply is not where it was.
            model.SetHold(model.Distance + 8f);
            float worstStep = 0f;
            float previous = model.Distance;
            for (int frame = 0; frame < 600; frame++)
            {
                model.Advance(1f / 60f);
                worstStep = Mathf.Max(worstStep, model.Distance - previous);
                previous = model.Distance;
            }

            Assert.That(
                worstStep,
                Is.LessThan(cruising / 60f + 0.001f),
                "The car jumped. A hold is a speed ceiling, never a clamp " +
                "on where the car is allowed to be.");
            Assert.That(
                model.Speed,
                Is.LessThan(0.05f),
                "And it does come to rest.");
        }

        [Test]
        public void GiveWay_HoldsWhileTheWayIsBlockedAndGoesWhenItClears()
        {
            var decision = new LastRouteCarGiveWayModel(40f);

            // Walked in from thirty metres out with something coming.
            float hold = float.PositiveInfinity;
            for (float distance = 10f; distance <= 39f; distance += 0.5f)
            {
                hold = decision.Advance(0.1f, distance, 3f, 2.6f, false);
            }

            Assert.That(hold, Is.EqualTo(40f));
            Assert.That(decision.IsGivingWay, Is.True);
            Assert.That(decision.IsCommitted, Is.False);

            // Standing at the line. It clears, he waits a beat, he goes.
            hold = decision.Advance(0.1f, 40f, 0f, 2.6f, true);
            Assert.That(
                hold,
                Is.EqualTo(40f),
                "One clear frame is a walker between footfalls, not a gap.");

            for (int step = 0; step < 8; step++)
            {
                hold = decision.Advance(0.1f, 40f, 0f, 2.6f, true);
            }

            Assert.That(decision.IsCommitted, Is.True);
            Assert.That(decision.CommitReason, Is.EqualTo("clear"));
            Assert.That(hold, Is.EqualTo(float.PositiveInfinity));

            // And it stays gone: the car is in the turn now.
            Assert.That(
                decision.Advance(0.1f, 41f, 2f, 2.6f, false),
                Is.EqualTo(float.PositiveInfinity),
                "Nothing seen from inside the turn can stop the car in it.");
        }

        [Test]
        public void GiveWay_DoesNotStandOnTheBrakesThroughItsOwnLine()
        {
            var decision = new LastRouteCarGiveWayModel(40f);

            // Blocked with eight metres to go at cruise, which needs almost
            // thirteen to stop in. A driver past the point of no return takes
            // the turn; braking through a line he is already over is worse,
            // and holding him there is the game stuttering.
            float hold = decision.Advance(1f / 60f, 32f, 8.2f, 2.6f, false);

            Assert.That(hold, Is.EqualTo(float.PositiveInfinity));
            Assert.That(decision.IsCommitted, Is.True);
            Assert.That(decision.CommitReason, Is.EqualTo("too_late"));
        }

        [Test]
        public void GiveWay_IgnoresWhatItSeesFromUpTheRoad()
        {
            var decision = new LastRouteCarGiveWayModel(200f);

            // A bus crossing the mouth while the car is still a block away
            // is not a reason to wait, and - the part that matters - not a
            // reason to spend the clock that stops him waiting for ever.
            for (int step = 0; step < 600; step++)
            {
                decision.Advance(0.1f, 20f, 8.2f, 2.6f, false);
            }

            Assert.That(decision.IsCommitted, Is.False);
            Assert.That(decision.IsGivingWay, Is.False);
            Assert.That(
                decision.WaitedSeconds,
                Is.EqualTo(0f),
                "A whole minute of blocked crossing seen from a hundred and " +
                "eighty metres away has burnt the wait budget before the " +
                "car ever arrives.");
        }

        [Test]
        public void GiveWay_NeverWaitsForEver()
        {
            var decision = new LastRouteCarGiveWayModel(40f);

            // Somebody has stalled on the kerb. This is the one ride out of
            // the city with the hero in the passenger seat: a pedestrian who
            // stops walking must not be able to end the game.
            float hold = float.PositiveInfinity;
            float waited = 0f;
            while (!decision.IsCommitted && waited < 60f)
            {
                hold = decision.Advance(0.1f, 40f, 0f, 2.6f, false);
                waited += 0.1f;
            }

            Assert.That(decision.IsCommitted, Is.True);
            Assert.That(decision.CommitReason, Is.EqualTo("waited_out"));
            Assert.That(hold, Is.EqualTo(float.PositiveInfinity));
            Assert.That(
                waited,
                Is.EqualTo(LastRouteCarGiveWayModel.MaximumWaitSeconds)
                    .Within(0.2f));
        }

        [Test]
        public void GiveWay_LetsAClearRoadRunAtTheLineWithoutSlowing()
        {
            var decision = new LastRouteCarGiveWayModel(40f);

            for (float distance = 0f; distance <= 20f; distance += 0.5f)
            {
                Assert.That(
                    decision.Advance(0.1f, distance, 8.2f, 2.6f, true),
                    Is.EqualTo(float.PositiveInfinity),
                    $"The car is being braked at {distance:0.0} m for a " +
                    "crossing that is clear.");
            }

            Assert.That(
                decision.IsCommitted,
                Is.False,
                "Clear from up the road is not a decision. Something can " +
                "still pull out in front of him before he gets there.");
        }

        /// <summary>
        /// A left turn off a street running west, across the oncoming lane
        /// and into a mouth on the far side - the shape of the only crossing
        /// either leg has. The car waits at `(0, 0)` pointing west.
        /// </summary>
        private static LastRouteCarGiveWayPoint BuildCrossing()
        {
            return new LastRouteCarGiveWayPoint(
                40f,
                new Vector3(-6f, 0f, 0f),
                new Vector3(-6f, 0f, -5.5f));
        }

        private static readonly Vector3 Westward = new Vector3(-1f, 0f, 0f);

        [Test]
        public void GiveWay_WaitsForABusComingTheOtherWay()
        {
            LastRouteCarGiveWayPoint crossing = BuildCrossing();

            // In the oncoming lane, `1.5 m` the far side of the crown,
            // twenty metres away and coming this way.
            Assert.That(
                LastRouteCarGiveWay.IsCrossedByVehicle(
                    crossing,
                    Westward,
                    new Vector3(-26f, 0f, -3f),
                    Vector3.right,
                    6f,
                    4.1f),
                Is.True,
                "A bus about to drive through the turn is the whole reason " +
                "there is a line to stop at.");
        }

        [Test]
        public void GiveWay_DoesNotWaitForABusFollowingHimDownHisOwnLane()
        {
            LastRouteCarGiveWayPoint crossing = BuildCrossing();

            // The crossing STARTS in the car's own lane, and Route 01 lays
            // its links at the same offset off the same crown - so a bus
            // simply following the car sweeps straight through the crossing.
            // Direction-agnostically that reads as traffic, and the car sits
            // at the line until the wait runs out with nothing crossing it.
            Assert.That(
                LastRouteCarGiveWay.IsCrossedByVehicle(
                    crossing,
                    Westward,
                    new Vector3(14f, 0f, 0f),
                    Westward,
                    6f,
                    4.1f),
                Is.False,
                "A bus behind him in his own lane is traffic he is in, not " +
                "traffic he crosses.");
        }

        [Test]
        public void GiveWay_SeesABusStoppedWithItsTailAcrossTheMouth()
        {
            LastRouteCarGiveWayPoint crossing = BuildCrossing();

            // Dwelling, so no speed to sweep with, and reported at the middle
            // of eight metres of body. Its nose is well past the mouth and
            // its tail is over it.
            Assert.That(
                LastRouteCarGiveWay.IsCrossedByVehicle(
                    crossing,
                    Westward,
                    new Vector3(-2f, 0f, -3f),
                    Vector3.right,
                    0f,
                    4.1f),
                Is.True,
                "A bus is eight metres long and it is reported at its " +
                "middle. Sweeping only forwards leaves its tail invisible.");
        }

        [Test]
        public void GiveWay_WaitsForSomeoneAboutToStepOntoTheCrossing()
        {
            LastRouteCarGiveWayPoint crossing = BuildCrossing();

            // Standing clear, but walking straight at it - a metre and a
            // half's warning at a stride.
            Assert.That(
                LastRouteCarGiveWay.IsWalkedInto(
                    crossing,
                    new Vector3(-8.5f, 0f, -3f),
                    Vector3.right,
                    1.3f,
                    0.35f),
                Is.True,
                "Where he will be beats where he is: a walker read only at " +
                "his own feet is one the car has already passed.");

            Assert.That(
                LastRouteCarGiveWay.IsWalkedInto(
                    crossing,
                    new Vector3(-8.5f, 0f, -3f),
                    Vector3.left,
                    1.3f,
                    0.35f),
                Is.False,
                "And the same man walking away from it is not in the way.");
        }

        [Test]
        public void GiveWay_MeasuresTheGapBetweenTwoStretchesOfRoad()
        {
            // Crossing segments touch at zero, whatever their ends do.
            Assert.That(
                LastRouteCarGiveWay.SegmentDistance(
                    new Vector3(-10f, 0f, -3f),
                    new Vector3(10f, 0f, -3f),
                    new Vector3(0f, 0f, 0f),
                    new Vector3(0f, 0f, -6f)),
                Is.EqualTo(0f).Within(0.001f));

            // Parallel and apart is the gap between them, and height is not
            // part of it - the forecourt sits below the street it opens off.
            Assert.That(
                LastRouteCarGiveWay.SegmentDistance(
                    new Vector3(-10f, 9f, 4f),
                    new Vector3(10f, 9f, 4f),
                    new Vector3(-10f, 0f, 0f),
                    new Vector3(10f, 0f, 0f)),
                Is.EqualTo(4f).Within(0.001f));

            // A degenerate segment is a point, not a divide by zero.
            Assert.That(
                LastRouteCarGiveWay.SegmentDistance(
                    new Vector3(3f, 0f, 0f),
                    new Vector3(3f, 0f, 0f),
                    new Vector3(0f, 0f, 0f),
                    new Vector3(0f, 0f, -6f)),
                Is.EqualTo(3f).Within(0.001f));
        }

        private static void Advance(
            LastRouteCarDriveModel model,
            float seconds)
        {
            for (float elapsed = 0f; elapsed < seconds; elapsed += 1f / 60f)
            {
                model.Advance(1f / 60f);
            }
        }

        /// <summary>
        /// The everywhere-else rule, end to end on a synthetic street: the
        /// car eases off and STANDS behind a bus dwelling on its own lane
        /// line, and pulls away when it leaves. Before this rule the car had
        /// no code at all that knew the bus existed outside the one forecourt
        /// give-way, and the player watched it drive straight through eight
        /// metres of dwelling bus.
        /// </summary>
        [Test]
        public void TrafficYield_StandsBehindADwellingBusAndFollowsItOut()
        {
            LastRouteCarDrivePath path = BuildStraight(120f);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.City);
            var traffic = new LastRouteCarTrafficYieldModel();
            Vector3 busCenter = new Vector3(0f, 0f, 60f);
            const float BusHalfLength = 4.125f;

            float Step(Vector3 busPosition, float busSpeed)
            {
                float conflict = LastRouteCarGiveWay.FindBusPathConflict(
                    path,
                    model.Distance,
                    40f,
                    busPosition,
                    Vector3.forward,
                    busSpeed,
                    BusHalfLength);
                float hold = traffic.Advance(
                    1f / 60f,
                    model.Distance,
                    model.Speed,
                    model.Profile.Braking,
                    float.IsPositiveInfinity(conflict)
                        ? float.PositiveInfinity
                        : Mathf.Max(
                            0f,
                            conflict -
                            LastRouteCarGiveWay.TrafficFollowGapMeters));
                model.SetHold(hold);
                model.Advance(1f / 60f);
                return hold;
            }

            // Fourteen seconds - the far side of a lawful 10 s dwell, and
            // still inside his own patience - at a bus that has not moved:
            // he must be standing short of its tail, not through it. (Twenty
            // seconds here and the wait cap fires and he drives through,
            // which is its own test below.)
            for (int frame = 0; frame < 840; frame++)
            {
                Step(busCenter, 0f);
            }

            Assert.That(
                model.IsWaiting,
                Is.True,
                "The car must be standing behind the dwelling bus.");
            Assert.That(
                model.Distance + 2.415f,
                Is.LessThan(busCenter.z - BusHalfLength),
                "The car's nose is inside the bus.");
            Assert.That(
                traffic.IsYielding,
                Is.True);

            // The bus pulls away and leaves. The hold recedes with it and
            // the car follows out and drives the street to its end.
            float busZ = busCenter.z;
            for (int frame = 0; frame < 3000 && !model.HasArrived; frame++)
            {
                busZ += 6f / 60f;
                Step(
                    busZ > 200f
                        ? new Vector3(0f, 0f, 500f)
                        : new Vector3(0f, 0f, busZ),
                    6f);
            }

            Assert.That(
                model.HasArrived,
                Is.True,
                "The road must be his again once the bus is gone.");
        }

        /// <summary>
        /// A bus lawfully passing the other way runs its own lane, three
        /// metres off his - the corridor threshold must never read it as
        /// traffic to brake for, or every correct oncoming meeting stops
        /// the ride.
        /// </summary>
        [Test]
        public void TrafficYield_IgnoresABusPassingTheOtherWay()
        {
            LastRouteCarDrivePath path = BuildStraight(120f);
            Assert.That(
                LastRouteCarGiveWay.FindBusPathConflict(
                    path,
                    10f,
                    60f,
                    new Vector3(3f, 0f, 40f),
                    Vector3.back,
                    6f,
                    4.125f),
                Is.EqualTo(float.PositiveInfinity),
                "The oncoming lane is not his problem.");

            // And the same bus drifted onto his own line IS.
            Assert.That(
                LastRouteCarGiveWay.FindBusPathConflict(
                    path,
                    10f,
                    60f,
                    new Vector3(0f, 0f, 40f),
                    Vector3.back,
                    6f,
                    4.125f),
                Is.LessThan(40f));
        }

        /// <summary>
        /// A bus sweeping a junction ahead holds the car SHORT of the
        /// corridor; the same bus still short of the junction does not.
        /// </summary>
        [Test]
        public void TrafficYield_HoldsShortOfAJunctionSweep()
        {
            LastRouteCarDrivePath path = BuildStraight(120f);

            // Crossing the lane at z=50, nose plus a breath of prediction
            // reaching over the line.
            float conflict = LastRouteCarGiveWay.FindBusPathConflict(
                path,
                10f,
                60f,
                new Vector3(-10f, 0f, 50f),
                Vector3.right,
                6f,
                4.125f);
            Assert.That(conflict, Is.LessThan(50f));
            Assert.That(conflict, Is.GreaterThan(40f));

            // Far side of its own street, sweep well short of the lane.
            Assert.That(
                LastRouteCarGiveWay.FindBusPathConflict(
                    path,
                    10f,
                    60f,
                    new Vector3(-25f, 0f, 50f),
                    Vector3.right,
                    6f,
                    4.125f),
                Is.EqualTo(float.PositiveInfinity));
        }

        /// <summary>
        /// The give-way's own rule, inherited whole: a conflict discovered
        /// nearer than the car can stop is driven through, because braking
        /// to a standstill INSIDE the junction parks the car in the one lane
        /// the bus will never yield in - which is how two vehicles that each
        /// behave reasonably lock a street.
        /// </summary>
        [Test]
        public void TrafficYield_NeverParksInsideTheJunction()
        {
            var traffic = new LastRouteCarTrafficYieldModel();

            // Two metres to the hold at city cruise, which needs almost
            // thirteen to stop in.
            Assert.That(
                traffic.Advance(1f / 60f, 40f, 8.2f, 2.6f, 42f),
                Is.EqualTo(float.PositiveInfinity));
            Assert.That(traffic.IsYielding, Is.False);
        }

        /// <summary>
        /// He never waits forever - this is the one ride out of the city -
        /// and after giving up he does not brake straight back into the
        /// thing he just decided to pass.
        /// </summary>
        [Test]
        public void TrafficYield_GivesUpAfterTheLongestLawfulDwell()
        {
            var traffic = new LastRouteCarTrafficYieldModel();

            // Stopped at the hold with the conflict never moving. The
            // longest lawful bus stands 15 s; at 18 he goes.
            float hold = 0f;
            for (float waited = 0f; waited < 17.9f; waited += 0.1f)
            {
                hold = traffic.Advance(0.1f, 34f, 0f, 2.6f, 34f);
            }

            Assert.That(hold, Is.EqualTo(34f));
            Assert.That(traffic.IsWaitedOut, Is.False);

            hold = traffic.Advance(0.2f, 34f, 0f, 2.6f, 34f);
            Assert.That(hold, Is.EqualTo(float.PositiveInfinity));
            Assert.That(traffic.IsWaitedOut, Is.True);

            // The same still-standing conflict must not re-arm, or the car
            // stutters into the bus forever.
            Assert.That(
                traffic.Advance(0.1f, 36f, 4f, 2.6f, 40f),
                Is.EqualTo(float.PositiveInfinity));

            // A genuinely clear road restores his patience.
            for (int step = 0; step < 5; step++)
            {
                traffic.Advance(
                    0.1f,
                    50f,
                    8f,
                    2.6f,
                    float.PositiveInfinity);
            }

            Assert.That(traffic.IsWaitedOut, Is.False);
            Assert.That(
                traffic.Advance(0.1f, 50f, 8f, 2.6f, 80f),
                Is.EqualTo(80f),
                "A new conflict after a clear road is held for again.");
        }

        /// <summary>
        /// Walkers: the jaywalker ahead in the lane holds the car; the
        /// walker waiting at a stop on the pavement - `1.74 m` off the lane
        /// line - never does, or the whole pavement reads as jaywalkers.
        /// </summary>
        [Test]
        public void TrafficYield_HoldsForTheJaywalkerAndIgnoresThePavement()
        {
            LastRouteCarDrivePath path = BuildStraight(120f);

            Assert.That(
                LastRouteCarGiveWay.FindWalkerPathConflict(
                    path,
                    10f,
                    60f,
                    new Vector3(0.3f, 0f, 40f),
                    Vector3.right,
                    1.1f,
                    0.35f),
                Is.LessThan(40f),
                "A walker in the road must hold the car.");

            Assert.That(
                LastRouteCarGiveWay.FindWalkerPathConflict(
                    path,
                    10f,
                    60f,
                    new Vector3(1.74f, 0f, 40f),
                    Vector3.zero,
                    0f,
                    0.35f),
                Is.EqualTo(float.PositiveInfinity),
                "The pavement is not the road.");
        }

        /// <summary>
        /// The wait graph between this car and Route 01 must stay acyclic,
        /// and this is the arithmetic that keeps it so. The bus never brakes
        /// for the car - only for walkers and for the HERO, whom it looks
        /// for within `1.71 m` of its own lane line (half bus `1.19` + hero
        /// radius `0.32` + padding `0.20`). The hero rides this car, so a
        /// held car must park him OUTSIDE that corridor: nose at clearance
        /// plus follow gap less the worst late-hold overshoot, hero at most
        /// half a car behind the nose. If this inequality ever breaks, the
        /// two vehicles can stand braked for each other at a junction with
        /// neither crossing anything.
        /// </summary>
        [Test]
        public void TrafficYield_HoldGeometryKeepsTheWaitGraphAcyclic()
        {
            const float BusHeroCorridor = 1.19f + 0.32f + 0.20f;
            const float WorstLateHoldOvershoot = 2f;
            const float HeroBehindNose = 4.83f * 0.5f;
            Assert.That(
                LastRouteCarGiveWay.BusClearanceMeters +
                LastRouteCarGiveWay.TrafficFollowGapMeters -
                WorstLateHoldOvershoot,
                Is.GreaterThan(BusHeroCorridor + HeroBehindNose),
                "A held car would park its hero inside the bus's own " +
                "yield corridor, and the two would wait for each other.");
        }

        /// <summary>
        /// The rim rolls with the front wheels, at the ratio, and - the
        /// whole point of the assertion - the right way round UNDER THE
        /// DRIVER'S HANDS. The bus's rim once rolled left on every right
        /// turn because its column axis points at the windshield and a
        /// positive rotation reads counterclockwise from the axis tail;
        /// this car's raked column points back at the driver, so the same
        /// negation copied over would have reproduced that bug in mirror.
        /// The axis is measured off the drawn grips instead, and this pins
        /// the result.
        /// </summary>
        [Test]
        public void Steering_RollsTheRimWithTheFrontWheelsForTheDriver()
        {
            GameObject prefab = LastRouteCarAssetRegistry.LoadPrefab();
            Assert.That(prefab, Is.Not.Null, "The car prefab is missing.");
            var host = new GameObject("Car Steering Test");
            try
            {
                GameObject car = Object.Instantiate(prefab, host.transform);
                car.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                var registry =
                    car.GetComponentInChildren<LastRouteCarAssetRegistry>(
                        true);
                Assert.That(registry.IsBound, Is.True);

                var driver = car.AddComponent<LastRouteCarDriver>();
                driver.Initialize(registry);
                Transform pivot = registry.SteeringWheelPivot;
                Quaternion rest = pivot.localRotation;
                Transform frontWheel = registry.FrontLeftWheel;
                Quaternion frontRest = frontWheel.localRotation;

                driver.ApplySteeringPose(20f);
                Assert.That(
                    driver.SteeringWheelDegrees,
                    Is.EqualTo(20f * LastRouteCarDriver.SteeringWheelRatio)
                        .Within(0.001f));

                // The front pair answers the same signal one-to-one: `20`
                // degrees about the car's up. Measured as a DELTA in parent
                // space rather than off the node's own `forward` - the
                // imported wheel child is turned a half turn, its axes are
                // not the car's, and this test's first draft proved the trap
                // by reading zero yaw off a correctly steered wheel.
                (frontWheel.localRotation * Quaternion.Inverse(frontRest))
                    .ToAngleAxis(
                        out float frontAngle,
                        out Vector3 frontAxis);
                Vector3 frontAxisWorld = frontWheel.parent != null
                    ? frontWheel.parent.TransformDirection(frontAxis)
                    : frontAxis;
                if (frontAngle > 180f)
                {
                    frontAngle = 360f - frontAngle;
                    frontAxisWorld = -frontAxisWorld;
                }

                Assert.That(
                    frontAngle,
                    Is.EqualTo(20f).Within(0.5f),
                    "The front wheels must answer the steer one-to-one.");
                Assert.That(
                    Vector3.Dot(frontAxisWorld.normalized, car.transform.up),
                    Is.GreaterThan(0.9f),
                    "A positive steer must yaw the front wheels toward the " +
                    "car's right, about its up.");

                // The rim's own turn, read back as angle-and-axis. The
                // delta quaternion lives in the pivot's PARENT space, so
                // its axis does too.
                (pivot.localRotation * Quaternion.Inverse(rest))
                    .ToAngleAxis(out float rimAngle, out Vector3 rimAxis);
                Vector3 rimAxisWorld = pivot.parent != null
                    ? pivot.parent.TransformDirection(rimAxis)
                    : rimAxis;
                if (rimAngle > 180f)
                {
                    rimAngle = 360f - rimAngle;
                    rimAxisWorld = -rimAxisWorld;
                }

                Assert.That(
                    rimAngle,
                    Is.EqualTo(60f).Within(0.5f),
                    "The rim must roll at the ratio.");

                // Clockwise for the man holding it: the positive-turn axis
                // points AT the driver's seat, and a positive Unity
                // rotation reads clockwise to the viewer at the axis tip.
                Assert.That(
                    Vector3.Dot(
                        rimAxisWorld,
                        registry.DriverSeatAnchor.position -
                        pivot.position),
                    Is.GreaterThan(0f),
                    "A right steer must roll the rim clockwise under the " +
                    "driver's hands - the bus shipped this backwards once.");

                // The handbrake straightens the wheel: Halt is what runs on
                // arrival, and the alighting clip that follows starts from
                // hands drawn on an UNTURNED rim.
                driver.Halt();
                Assert.That(driver.SteeringWheelDegrees, Is.EqualTo(0f));
                Assert.That(
                    Quaternion.Angle(pivot.localRotation, rest),
                    Is.LessThan(0.01f),
                    "A parked car does not hold its last corner.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
