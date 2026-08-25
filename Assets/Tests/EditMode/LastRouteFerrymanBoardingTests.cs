using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The walk from the bonnet to the wheel, as pure data: the clock that
    /// paces it, the springs that answer it, and the three places on the lot
    /// it passes through.
    ///
    /// All three are testable without a scene, and all three are the kind of
    /// thing that fails silently in one. A door that opens after he has
    /// started moving, a spring that never settles, a corner that cuts
    /// through the car - none of them throws, and all of them look like an
    /// animation bug from the outside.
    /// </summary>
    public sealed class LastRouteFerrymanBoardingTests
    {
        /// <summary>His own body, taken as the hero's: the two rigs share a
        /// skeleton and an envelope.</summary>
        private const float BodyReach = 0.36f;

        private static LastRouteFerrymanBoardingTimeline BuildTimeline()
        {
            return new LastRouteFerrymanBoardingTimeline(1f, 4f, 2.5f);
        }

        [Test]
        public void Timeline_RunsDropThenWalkThenBoardAndStops()
        {
            LastRouteFerrymanBoardingTimeline timeline = BuildTimeline();
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.Dismounting));

            timeline.Advance(1.2f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.WalkingToDoor),
                "A step that overruns the drop belongs to the walk.");

            timeline.Advance(4f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.Boarding));

            timeline.Advance(3f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.Driving));
            Assert.That(timeline.IsDone, Is.True);

            timeline.Advance(30f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.Driving),
                "He does not get out again.");
        }

        [Test]
        public void Cues_FireExactlyOnce()
        {
            LastRouteFerrymanBoardingTimeline timeline = BuildTimeline();
            Assert.That(timeline.ConsumeLandingCue(), Is.False);
            timeline.Advance(
                LastRouteFerrymanBoardingTimeline.LandingPhase + 0.01f);
            Assert.That(
                timeline.ConsumeLandingCue(),
                Is.True,
                "The springs are kicked on the landing key.");
            Assert.That(timeline.ConsumeLandingCue(), Is.False);

            Assert.That(timeline.ConsumeSeatCue(), Is.False);
            timeline.Advance(30f);
            Assert.That(timeline.ConsumeSeatCue(), Is.True);
            Assert.That(timeline.ConsumeSeatCue(), Is.False);
        }

        [Test]
        public void DoorAndTravel_NeverPutHimInsideHisOwnDoor()
        {
            // The whole point of the four board constants. He cannot start
            // moving towards the seat until the leaf is out of the way, and
            // the leaf cannot start closing until he is in.
            Assert.That(
                LastRouteFerrymanBoardingTimeline.DoorOpenPhase,
                Is.LessThanOrEqualTo(
                    LastRouteFerrymanBoardingTimeline.TravelStartPhase),
                "He must not set off before the door is open.");
            Assert.That(
                LastRouteFerrymanBoardingTimeline.TravelEndPhase,
                Is.LessThanOrEqualTo(
                    LastRouteFerrymanBoardingTimeline.DoorShutStartPhase),
                "The door must not start closing before he is in.");
            Assert.That(
                LastRouteFerrymanBoardingTimeline.DoorPullPhase,
                Is.LessThan(LastRouteFerrymanBoardingTimeline.DoorOpenPhase));
            Assert.That(
                LastRouteFerrymanBoardingTimeline.DoorShutPhase,
                Is.LessThan(1f),
                "It has to be shut before the driving loop takes over.");

            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness(0f),
                Is.EqualTo(0f));
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness(0.5f),
                Is.EqualTo(1f));
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness(1f),
                Is.EqualTo(0f));
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateSeatTravel(
                    LastRouteFerrymanBoardingTimeline.TravelStartPhase),
                Is.EqualTo(0f));
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateSeatTravel(1f),
                Is.EqualTo(1f));
        }

        [Test]
        public void Drop_HoldsHimOnTheMetalThroughTheShoveThenFalls()
        {
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDropTravel(0.1f),
                Is.EqualTo(0f),
                "The first fifth of the clip is the shove; a root that " +
                "slides during it reads as a conveyor.");
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDropFall(0.1f),
                Is.EqualTo(0f));

            // Squared, not smoothed: a fall accelerates all the way down.
            // Halfway between the release and the landing it has covered a
            // quarter of the drop, not half.
            float middle = 0.5f * (
                LastRouteFerrymanBoardingTimeline.DropReleasePhase +
                LastRouteFerrymanBoardingTimeline.LandingPhase);
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDropFall(middle),
                Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDropFall(
                    LastRouteFerrymanBoardingTimeline.LandingPhase),
                Is.EqualTo(1f),
                "He is on the ground on the landing key, not after it.");
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDropTravel(
                    LastRouteFerrymanBoardingTimeline.LandingPhase),
                Is.EqualTo(1f));
        }

        [Test]
        public void Suspension_RocksAndThenSettles()
        {
            var model = new LastRouteCarSuspensionModel();
            Assert.That(model.IsSettled, Is.True);

            model.Nudge(
                LastRouteCarSuspension.DismountHeaveImpulse,
                LastRouteCarSuspension.DismountPitchImpulse,
                0f);

            // The nose lifting is the kick; the nose dropping back BELOW
            // level afterwards is the spring. Measured as an overshoot
            // rather than as sign changes: a zero crossing is one frame
            // wide and whether it is sampled at all depends on the step,
            // which is how the first version of this assertion managed to
            // fail against a spring that was oscillating perfectly well.
            float peakPitch = 0f;
            float lowestPitch = 0f;
            for (int step = 0; step < 240; step++)
            {
                model.Advance(1f / 60f);
                peakPitch = Mathf.Max(peakPitch, model.PitchDegrees);
                lowestPitch = Mathf.Min(lowestPitch, model.PitchDegrees);
                Assert.That(
                    Mathf.Abs(model.Heave),
                    Is.LessThanOrEqualTo(
                        LastRouteCarSuspensionModel.MaximumHeave + 1e-4f));
                Assert.That(
                    Mathf.Abs(model.PitchDegrees),
                    Is.LessThanOrEqualTo(
                        LastRouteCarSuspensionModel.MaximumPitchDegrees +
                        1e-4f));
                Assert.That(
                    Mathf.Abs(model.RollDegrees),
                    Is.LessThanOrEqualTo(
                        LastRouteCarSuspensionModel.MaximumRollDegrees +
                        1e-4f));
            }

            Assert.That(
                peakPitch,
                Is.GreaterThan(0.2f),
                "A kick nobody can see is not a kick.");
            Assert.That(
                lowestPitch,
                Is.LessThan(-0.05f),
                "Under-damped on purpose: a car that rises once and stops " +
                "reads as a lift, not as springs. The nose has to come " +
                "back down past level before it settles.");
            Assert.That(
                model.IsSettled,
                Is.True,
                "Four seconds is long enough for a car to stop moving.");
        }

        [Test]
        public void Suspension_IgnoresNonsenseAndSubStepsALongHitch()
        {
            var model = new LastRouteCarSuspensionModel();
            model.Nudge(float.NaN, float.PositiveInfinity, 2f);
            model.Advance(float.NaN);
            model.Advance(0.75f);
            Assert.That(float.IsNaN(model.Heave), Is.False);
            Assert.That(
                Mathf.Abs(model.RollDegrees),
                Is.LessThanOrEqualTo(
                    LastRouteCarSuspensionModel.MaximumRollDegrees + 1e-4f),
                "An explicit spring handed a whole dropped frame in one " +
                "step diverges; it has to be walked in several.");
        }

        [Test]
        public void BoardingPlan_LandsAheadOfTheBumperAndRoundsTheWing()
        {
            GameObject prefab = LastRouteCarAssetRegistry.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            var root = new GameObject("Boarding Plan Car");
            try
            {
                GameObject instance =
                    Object.Instantiate(prefab, root.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                var registry = instance
                    .GetComponentInChildren<LastRouteCarAssetRegistry>(true);
                LastRouteFerrymanBoardingPlan plan =
                    LastRouteFerrymanBoardingPlan.Create(registry, 0f);
                Assert.That(plan.IsPresent, Is.True);

                Transform car = registry.transform;
                float halfLength = registry.Dimensions.Length * 0.5f;
                float halfWidth = registry.Dimensions.Width * 0.5f;

                float landingAhead = Vector3.Dot(
                    plan.LandingPosition - car.position, car.forward);
                Assert.That(
                    landingAhead,
                    Is.GreaterThan(halfLength),
                    "He drops off the nose, so he lands in front of it.");

                // The corner exists because the straight line from the
                // landing point to the door cuts the car's own nose. It has
                // to be outside the bodywork on both counts to be worth
                // anything.
                float cornerAcross = Mathf.Abs(Vector3.Dot(
                    plan.ApproachCorner - car.position, car.right));
                Assert.That(
                    cornerAcross,
                    Is.GreaterThan(halfWidth + BodyReach),
                    "The rounding corner must clear the flank.");
                float cornerAhead = Vector3.Dot(
                    plan.ApproachCorner - car.position, car.forward);
                Assert.That(
                    cornerAhead,
                    Is.GreaterThan(halfLength),
                    "and clear the nose.");

                // Both legs of the walk stay out of the footprint.
                AssertLegMissesTheCar(
                    plan.LandingPosition,
                    plan.ApproachCorner,
                    car,
                    halfLength,
                    halfWidth);
                AssertLegMissesTheCar(
                    plan.ApproachCorner,
                    plan.DoorDockPosition,
                    car,
                    halfLength,
                    halfWidth);

                // He ends up on the driver's side, which is the side his
                // own door is on and NOT the one the hero docks at.
                float dockSide = Vector3.Dot(
                    plan.DoorDockPosition - car.position, car.right);
                float driverSide = Vector3.Dot(
                    registry.DriverSeatAnchor.position - car.position,
                    car.right);
                Assert.That(
                    dockSide * driverSide,
                    Is.GreaterThan(0f),
                    "He walks to his own door, not round to the " +
                    "passenger's.");
                Assert.That(
                    Vector3.Dot(plan.DoorDockFacing, car.right) * dockSide,
                    Is.LessThan(0f),
                    "He stands facing the car he is about to get into.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertLegMissesTheCar(
            Vector3 from,
            Vector3 to,
            Transform car,
            float halfLength,
            float halfWidth)
        {
            const int samples = 24;
            for (int index = 0; index <= samples; index++)
            {
                Vector3 point = Vector3.Lerp(from, to, index / (float)samples);
                Vector3 offset = point - car.position;
                float along = Mathf.Abs(Vector3.Dot(offset, car.forward));
                float across = Mathf.Abs(Vector3.Dot(offset, car.right));
                Assert.That(
                    along < halfLength && across < halfWidth,
                    Is.False,
                    $"The walk passes through the bodywork at {point}.");
            }
        }
    }
}
