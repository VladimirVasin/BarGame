using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Drives the car along one path and stops it at the end.
    ///
    /// It owns nothing but the runtime root's pose. The model beside it works
    /// out where along the road the car is; this writes that onto the
    /// transform, turns the front wheels toward where it is going, rolls all
    /// four by the ground they have covered, and tells the springs what the
    /// road is doing to the body. Everything else about the car - the doors,
    /// the seat, the halos, the man in it - carries on exactly as it did while
    /// it was parked, because all of it is already hung off this root.
    ///
    /// It ticks on SCALED time on purpose. The pause menu freezes
    /// <c>timeScale</c>, and a car that kept driving through a paused game
    /// would arrive at the cafe while the player was reading the options.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(270)]
    public sealed class LastRouteCarDriver : MonoBehaviour
    {
        /// <summary>
        /// How far ahead the steering reads to find the angle for the front
        /// wheels. Short enough to answer a corner, long enough not to twitch
        /// on the metre-by-metre samples of the mountain road.
        /// </summary>
        public const float SteeringLookAheadMeters = 4.5f;

        /// <summary>The most the front wheels will ever be turned.</summary>
        public const float MaximumSteeringDegrees = 33f;

        public const float SteeringResponsePerSecond = 7f;

        /// <summary>A hitch longer than this is stepped rather than swallowed,
        /// the suspension's own convention.</summary>
        public const float MaximumStepSeconds = 0.1f;

        private LastRouteCarAssetRegistry registry;
        private LastRouteCarSuspension suspension;
        private BoxCollider obstacle;
        private LastRouteCarDriveModel model;
        private float steeringDegrees;
        private float wheelRollDegrees;
        private Transform[] wheels;
        private Quaternion[] wheelRest;
        private bool[] wheelSteers;
        private bool announcedArrival;

        /// <summary>Raised once, on the frame the car comes to rest at the end
        /// of its path.</summary>
        public event Action Arrived;

        /// <summary>
        /// Raised the instant the root has been written, every frame it moves.
        ///
        /// Anything carried BY the car listens to this rather than doing its
        /// own work in a `LateUpdate` and trusting the two to be ordered. They
        /// are not reliably: a component added during a scene build can have
        /// its first `Update` deferred a frame relative to one that already
        /// existed, and the hero riding a car whose `Update` had not run yet
        /// sat one frame's travel behind it - eight centimetres at the speed
        /// this comes out of the tunnel at. Writing the passenger in the same
        /// call as the car has no ordering to get wrong.
        /// </summary>
        public event Action Moved;

        public bool IsDriving { get; private set; }
        public bool HasArrived => model != null && model.HasArrived;
        public LastRouteCarDriveModel Model => model;

        /// <summary>Metres covered so far, for logs and tests.</summary>
        public float Distance => model?.Distance ?? 0f;
        public float Speed => model?.Speed ?? 0f;

        public void Initialize(LastRouteCarAssetRegistry carRegistry)
        {
            registry = carRegistry ??
                throw new ArgumentNullException(nameof(carRegistry));
            suspension = GetComponent<LastRouteCarSuspension>();
            obstacle = GetComponent<BoxCollider>();
            CaptureWheelRest();
        }

        /// <summary>
        /// Sets the car going. The obstacle box comes off for the whole
        /// journey: it is there to stop the hero walking through a parked car,
        /// and a kinematic box shoved along a street at eight metres a second
        /// would push pedestrians - and the hero's own controller - out of the
        /// way of a car they are supposed to be inside.
        /// </summary>
        public void Begin(
            LastRouteCarDrivePath path,
            LastRouteCarDriveProfile profile,
            float initialSpeed = 0f)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            model = new LastRouteCarDriveModel(path, profile);
            model.Resume(initialSpeed);
            announcedArrival = false;
            IsDriving = true;
            if (obstacle != null)
            {
                obstacle.enabled = false;
            }

            ApplyPose();
            GameLog.Info(
                "lastroute",
                "car_drive_begun",
                GameLog.Field("length", path.Length),
                GameLog.Field("initial_speed", initialSpeed));
        }

        /// <summary>
        /// Puts the car at the end of its road, stopped, in one frame.
        ///
        /// The whole of a skip. It moves the DISTANCE and nothing else, so
        /// everything that follows is the ordinary arrival: the next
        /// <see cref="Update"/> writes the pose, raises <see cref="Moved"/> -
        /// which is what carries the passenger - finds the road run out, and
        /// raises <see cref="Arrived"/>. Every world-space thing that would
        /// otherwise go stale (the seat plan, the springs, the man at the
        /// wheel) is already re-solved by that arrival, because a car that
        /// drives there slowly has exactly the same problem.
        /// </summary>
        public bool SkipToEnd()
        {
            if (!IsDriving || model == null || model.HasArrived)
            {
                return false;
            }

            float from = model.Distance;
            model.Resume(0f, model.Path.Length);
            GameLog.Info(
                "lastroute",
                "car_drive_skipped",
                GameLog.Field("from", from),
                GameLog.Field("length", model.Path.Length));
            return true;
        }

        /// <summary>
        /// Puts the car back on its handbrake wherever it happens to be. Safe
        /// to call when it was never driving.
        /// </summary>
        public void Halt()
        {
            IsDriving = false;
            suspension?.ClearDriveLoad();
            if (obstacle != null)
            {
                obstacle.enabled = true;
            }
        }

        private void Update()
        {
            if (!IsDriving || model == null)
            {
                return;
            }

            float step = Mathf.Min(Time.deltaTime, MaximumStepSeconds);
            model.Advance(step);
            ApplyPose();
            Moved?.Invoke();
            ApplyWheels(step);
            suspension?.SetDriveLoad(
                model.LongitudinalAcceleration,
                model.LateralAcceleration);

            if (!model.HasArrived || announcedArrival)
            {
                return;
            }

            announcedArrival = true;
            Halt();
            GameLog.Info(
                "lastroute",
                "car_drive_arrived",
                GameLog.Field("x", transform.position.x),
                GameLog.Field("z", transform.position.z));
            Arrived?.Invoke();
        }

        private void ApplyPose()
        {
            model.Evaluate(out Vector3 position, out Vector3 forward);
            if (forward.sqrMagnitude < 0.000001f)
            {
                transform.position = position;
                return;
            }

            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>
        /// Turns the front pair toward where the road is going and rolls all
        /// four by the ground they have covered.
        ///
        /// Both are written as an offset from each wheel's captured REST
        /// rotation rather than accumulated onto whatever it is currently at:
        /// a rotation composed onto itself every frame drifts, and a tyre that
        /// slowly corkscrews out of its arch is the kind of thing nobody
        /// notices until the drive is six hundred metres long.
        ///
        /// Both axes are resolved against the runtime ROOT and then expressed
        /// in each wheel's parent space. The steering axis in particular is a
        /// documented trap here - the imported model child is turned a half
        /// turn, so an axis read off the wheel's own node is not the car's.
        /// </summary>
        private void ApplyWheels(float step)
        {
            if (wheels == null || registry == null)
            {
                return;
            }

            float radius = registry.Dimensions.WheelRadius;
            if (radius > 0.001f)
            {
                wheelRollDegrees += (model.Speed * step) / radius *
                                    Mathf.Rad2Deg;
                wheelRollDegrees = Mathf.Repeat(wheelRollDegrees, 360f);
            }

            steeringDegrees = Mathf.Lerp(
                steeringDegrees,
                ResolveSteeringTarget(),
                Mathf.Clamp01(step * SteeringResponsePerSecond));

            for (int index = 0; index < wheels.Length; index++)
            {
                Transform wheel = wheels[index];
                if (wheel == null || wheel.parent == null)
                {
                    continue;
                }

                Vector3 rollAxis = wheel.parent.InverseTransformDirection(
                    transform.right);
                Quaternion local =
                    Quaternion.AngleAxis(wheelRollDegrees, rollAxis) *
                    wheelRest[index];
                if (wheelSteers[index])
                {
                    Vector3 steerAxis =
                        wheel.parent.InverseTransformDirection(transform.up);
                    local = Quaternion.AngleAxis(steeringDegrees, steerAxis) *
                            local;
                }

                wheel.localRotation = local;
            }
        }

        /// <summary>
        /// The angle to a point down the road rather than to the pose the car
        /// is already in - the bus's own arrangement
        /// (<c>CityBusActor.ResolveSteeringAngle</c>), and the difference
        /// between wheels that lead a corner and wheels that report it.
        /// </summary>
        private float ResolveSteeringTarget()
        {
            model.Path.Sample(
                model.Distance + SteeringLookAheadMeters,
                out _,
                out Vector3 ahead);
            ahead.y = 0f;
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (ahead.sqrMagnitude < 0.000001f ||
                facing.sqrMagnitude < 0.000001f)
            {
                return 0f;
            }

            return Mathf.Clamp(
                Vector3.SignedAngle(facing, ahead, Vector3.up),
                -MaximumSteeringDegrees,
                MaximumSteeringDegrees);
        }

        private void CaptureWheelRest()
        {
            if (registry == null)
            {
                return;
            }

            wheels = new[]
            {
                registry.FrontLeftWheel,
                registry.FrontRightWheel,
                registry.RearLeftWheel,
                registry.RearRightWheel
            };
            wheelSteers = new[] { true, true, false, false };
            wheelRest = new Quaternion[wheels.Length];
            for (int index = 0; index < wheels.Length; index++)
            {
                wheelRest[index] = wheels[index] != null
                    ? wheels[index].localRotation
                    : Quaternion.identity;
            }
        }
    }
}
