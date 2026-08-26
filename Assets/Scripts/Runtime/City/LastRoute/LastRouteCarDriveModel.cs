using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// How one car takes one road: how fast it will go, how hard it gets there
    /// and how much lateral it is willing to carry through a bend.
    ///
    /// The bus's numbers are the band this sits in - it cruises at `6`, drops
    /// to `3.2` at junctions and accelerates at `1.45`. A saloon with one
    /// passenger and nowhere to stop may sit a little above that, but not much:
    /// the point of the drive is to be looked out of.
    /// </summary>
    public readonly struct LastRouteCarDriveProfile
    {
        public LastRouteCarDriveProfile(
            float cruiseSpeed,
            float acceleration,
            float braking,
            float maximumLateralAcceleration,
            float minimumCorneringSpeed)
        {
            CruiseSpeed = Require(cruiseSpeed, nameof(cruiseSpeed));
            Acceleration = Require(acceleration, nameof(acceleration));
            Braking = Require(braking, nameof(braking));
            MaximumLateralAcceleration = Require(
                maximumLateralAcceleration,
                nameof(maximumLateralAcceleration));
            MinimumCorneringSpeed = Require(
                minimumCorneringSpeed,
                nameof(minimumCorneringSpeed));
            if (minimumCorneringSpeed > cruiseSpeed)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumCorneringSpeed),
                    minimumCorneringSpeed,
                    "A car cannot be required to corner faster than it drives.");
            }
        }

        public float CruiseSpeed { get; }
        public float Acceleration { get; }
        public float Braking { get; }

        /// <summary>
        /// Metres per second squared of side load the Ferryman is willing to
        /// put through his tyres. This is the number that decides how the ten
        /// R`7.5 m` hairpins read: at `1.6` the car takes them at about
        /// `3.5 m/s`, which is a man driving a mountain road carefully.
        /// </summary>
        public float MaximumLateralAcceleration { get; }

        /// <summary>
        /// The floor under the cornering limit. Without it a sharp enough
        /// vertex asks for zero and the car stops dead in the bend forever;
        /// with it the worst a corner can do is walk the car round.
        /// </summary>
        public float MinimumCorneringSpeed { get; }

        /// <summary>
        /// City streets: `6 m` of carriageway between kerbs, right-angle
        /// junctions, and a man who has just been told to drive.
        /// </summary>
        public static LastRouteCarDriveProfile City { get; } =
            new LastRouteCarDriveProfile(
                cruiseSpeed: 8.2f,
                acceleration: 1.9f,
                braking: 2.6f,
                maximumLateralAcceleration: 2.2f,
                minimumCorneringSpeed: 2.6f);

        /// <summary>
        /// The climb: ten hairpins, an `8%` grade and a `50 m` bridge over a
        /// gorge. Slower everywhere and far less willing to lean.
        /// </summary>
        public static LastRouteCarDriveProfile Mountain { get; } =
            new LastRouteCarDriveProfile(
                cruiseSpeed: 6.4f,
                acceleration: 1.5f,
                braking: 2.2f,
                maximumLateralAcceleration: 1.6f,
                minimumCorneringSpeed: 2.2f);

        private static float Require(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Drive profile values must be finite and positive.");
            }

            return value;
        }
    }

    /// <summary>
    /// The car driving the path: distance covered, speed carried, and the two
    /// things that take speed away from it - the corner ahead and the end of
    /// the road.
    ///
    /// Pure, like <see cref="LastRouteCarSuspensionModel"/> beside it. No
    /// transform is read, no <c>Time</c> is sampled and nothing is drawn, so
    /// the whole of how this car drives is EditMode-testable and the
    /// MonoBehaviour over it does nothing but hand it a delta and write the
    /// result onto a root.
    ///
    /// The speed limit is a forward sweep of a backward pass: for every vertex
    /// inside the braking horizon, work out how fast the car may be going HERE
    /// and still be down to that vertex's own cornering speed by the time it
    /// arrives, and take the lowest answer. That is what makes it lift off
    /// before a hairpin rather than discover the hairpin from inside it.
    /// </summary>
    public sealed class LastRouteCarDriveModel
    {
        /// <summary>
        /// Never look less than this far ahead, however slowly the car is
        /// moving. Pulling away straight into a corner still wants warning.
        /// </summary>
        public const float MinimumHorizonMeters = 6f;

        /// <summary>
        /// The longest step the integrator takes in one go, the suspension
        /// model's own convention. A dropped frame handed in whole would let
        /// the car walk through the end of the road before it braked.
        /// </summary>
        public const float MaximumSubStepSeconds = 0.05f;

        /// <summary>Slower than this, with the road run out, is stopped.
        /// </summary>
        public const float StoppedSpeed = 0.02f;

        private const float DegreesToRadians = Mathf.PI / 180f;

        private readonly LastRouteCarDrivePath path;
        private readonly LastRouteCarDriveProfile profile;
        private float holdDistance = float.PositiveInfinity;

        public LastRouteCarDriveModel(
            LastRouteCarDrivePath drivePath,
            LastRouteCarDriveProfile driveProfile)
        {
            path = drivePath ??
                throw new ArgumentNullException(nameof(drivePath));
            profile = driveProfile;
        }

        public LastRouteCarDrivePath Path => path;
        public LastRouteCarDriveProfile Profile => profile;

        /// <summary>Metres covered from the start of the path.</summary>
        public float Distance { get; private set; }

        /// <summary>Metres per second, never negative - this car does not
        /// reverse anywhere in the beat.</summary>
        public float Speed { get; private set; }

        /// <summary>
        /// Metres per second squared along the path over the last step.
        /// Positive is pulling away, negative is braking; the suspension reads
        /// it so the body squats and dives instead of gliding.
        /// </summary>
        public float LongitudinalAcceleration { get; private set; }

        /// <summary>
        /// Metres per second squared across the path over the last step, with
        /// the car's own right positive. Read by the suspension for the lean.
        /// </summary>
        public float LateralAcceleration { get; private set; }

        /// <summary>The ceiling the model was driving at over the last step,
        /// published for the tests rather than for the game.</summary>
        public float TargetSpeed { get; private set; }

        public float Remaining => Mathf.Max(0f, path.Length - Distance);

        public bool HasArrived =>
            Remaining <= 0.01f && Speed <= StoppedSpeed;

        /// <summary>
        /// Where along the road the car will not go past, or
        /// <see cref="float.PositiveInfinity"/> when nothing is holding it.
        /// </summary>
        public float HoldDistance => holdDistance;

        /// <summary>
        /// Standing still short of the end of the road because something is
        /// holding it there. The end of the road is
        /// <see cref="HasArrived"/>; this is a car waiting at a give-way
        /// line, and the suspension and the audio both want to know the
        /// difference.
        /// </summary>
        public bool IsWaiting =>
            !float.IsPositiveInfinity(holdDistance) &&
            Speed <= StoppedSpeed &&
            Remaining > 0.01f;

        /// <summary>
        /// Puts a line across the road that the car will not cross.
        ///
        /// It works as a SPEED ceiling and never as a clamp on the distance
        /// covered, which matters: a hold armed late - a bus that appears
        /// round a corner - then costs the hardest stop the car has and a
        /// metre or two over the line, exactly as it would in a real one,
        /// instead of freezing the car mid-street in a single frame.
        /// </summary>
        public void SetHold(float distance)
        {
            holdDistance = float.IsNaN(distance)
                ? float.PositiveInfinity
                : Mathf.Max(0f, distance);
        }

        /// <summary>Lifts the hold. The car pulls away on its own
        /// acceleration, from wherever it happens to be standing.</summary>
        public void ReleaseHold()
        {
            holdDistance = float.PositiveInfinity;
        }

        /// <summary>
        /// Starts the car somewhere other than stopped at the kerb.
        ///
        /// This exists for one moment in the game: the hero comes out of the
        /// mountain's tunnel in a car that never stopped. Narratively it has
        /// been driving for a minute; mechanically it is a new model on a new
        /// path, and without this it would pull away from rest inside the
        /// tunnel and give the whole handover away.
        /// </summary>
        public void Resume(float speed, float distance = 0f)
        {
            Distance = Mathf.Clamp(Sanitize(distance), 0f, path.Length);
            Speed = Mathf.Clamp(
                Sanitize(speed),
                0f,
                Mathf.Min(profile.CruiseSpeed, EvaluateTargetSpeed()));
            LongitudinalAcceleration = 0f;
            LateralAcceleration = 0f;
            TargetSpeed = Speed;
        }

        public void Advance(float deltaTime)
        {
            float remaining = Sanitize(deltaTime);
            if (remaining <= 0f)
            {
                return;
            }

            float startSpeed = Speed;
            float startHeading = SampleHeadingDegrees(Distance);
            float elapsed = 0f;
            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, MaximumSubStepSeconds);
                remaining -= step;
                elapsed += step;
                Step(step);
            }

            LongitudinalAcceleration = elapsed > 0f
                ? (Speed - startSpeed) / elapsed
                : 0f;
            LateralAcceleration = EvaluateLateralAcceleration(
                startHeading,
                elapsed);
        }

        public void Evaluate(out Vector3 position, out Vector3 forward)
        {
            path.Sample(Distance, out position, out forward);
        }

        /// <summary>
        /// The fastest the car may be going at its current distance. Public
        /// because it is the whole of the cornering behaviour and it is much
        /// easier to assert directly than to infer from a trace.
        /// </summary>
        public float EvaluateTargetSpeed()
        {
            // The end of the road is a corner with no exit.
            float limit = Mathf.Sqrt(
                Mathf.Max(0f, 2f * profile.Braking * Remaining));
            limit = Mathf.Min(limit, profile.CruiseSpeed);

            // And so is a give-way line, for as long as it is down. Same
            // arithmetic, so a car braking to a stop line settles onto it
            // the same way it settles onto the terminus.
            if (!float.IsPositiveInfinity(holdDistance))
            {
                limit = Mathf.Min(
                    limit,
                    Mathf.Sqrt(
                        Mathf.Max(
                            0f,
                            2f * profile.Braking *
                            (holdDistance - Distance))));
            }

            float horizon = Mathf.Max(
                MinimumHorizonMeters,
                (Speed * Speed) / (2f * profile.Braking));
            float end = Mathf.Min(path.Length, Distance + horizon);
            for (int index = path.FindFirstIndexAtOrAfter(Distance);
                 index < path.PointCount;
                 index++)
            {
                float vertexDistance = path.GetDistance(index);
                if (vertexDistance > end)
                {
                    break;
                }

                float corner = EvaluateCorneringSpeed(path.GetTurnRate(index));
                if (corner >= profile.CruiseSpeed)
                {
                    continue;
                }

                float lead = Mathf.Max(0f, vertexDistance - Distance);
                float entry = Mathf.Sqrt(
                    (corner * corner) + (2f * profile.Braking * lead));
                limit = Mathf.Min(limit, entry);
            }

            return Mathf.Max(0f, limit);
        }

        /// <summary>
        /// Pure: how fast a bend of this sharpness may be taken. The turn rate
        /// is degrees of heading per metre, so the radius is one radian's worth
        /// of it, and the speed is the usual `v = sqrt(a * r)`.
        /// </summary>
        public float EvaluateCorneringSpeed(float turnRateDegreesPerMeter)
        {
            float rate = Sanitize(turnRateDegreesPerMeter);
            if (rate <= 0.0001f)
            {
                return profile.CruiseSpeed;
            }

            float radius = 1f / (rate * DegreesToRadians);
            float speed = Mathf.Sqrt(
                profile.MaximumLateralAcceleration * radius);
            return Mathf.Clamp(
                speed,
                profile.MinimumCorneringSpeed,
                profile.CruiseSpeed);
        }

        private void Step(float step)
        {
            TargetSpeed = EvaluateTargetSpeed();
            Speed = Speed < TargetSpeed
                ? Mathf.Min(TargetSpeed, Speed + (profile.Acceleration * step))
                : Mathf.Max(TargetSpeed, Speed - (profile.Braking * step));
            Speed = Mathf.Max(0f, Speed);

            Distance = Mathf.Min(path.Length, Distance + (Speed * step));
            if (Remaining <= 0.0001f)
            {
                // The road has run out. Whatever rounding is left in the
                // speed goes with it, so `HasArrived` is a fact rather than
                // something that becomes true a few frames later.
                Distance = path.Length;
                Speed = 0f;
            }
        }

        private float EvaluateLateralAcceleration(
            float startHeadingDegrees,
            float elapsed)
        {
            if (elapsed <= 0f || Speed <= 0.0001f)
            {
                return 0f;
            }

            float turned = Mathf.DeltaAngle(
                startHeadingDegrees,
                SampleHeadingDegrees(Distance));
            float yawRate = (turned * DegreesToRadians) / elapsed;
            return yawRate * Speed;
        }

        private float SampleHeadingDegrees(float distance)
        {
            path.Sample(distance, out _, out Vector3 forward);
            forward.y = 0f;
            return forward.sqrMagnitude > 0.000001f
                ? Mathf.Atan2(forward.x, forward.z) / DegreesToRadians
                : 0f;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
