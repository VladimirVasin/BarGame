using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Puts the parked car on its springs.
    ///
    /// The generator hangs all four wheel pivots off `ROOT_Body` alongside
    /// the bodywork, so rocking the body directly drives the tyres into the
    /// ground. The bus solved this years ago and this is the same solution:
    /// slip one empty - the SPRUNG body - between the imported node and its
    /// parent, move the wheels up out of it, and rock the empty. Nothing in
    /// the generator changes and no anchor moves relative to the bodywork it
    /// belongs to.
    ///
    /// It is deliberately NOT the runtime root that rocks. That root carries
    /// the obstacle collider, the two headlight halos and the passenger
    /// seat's trigger, and a car whose collision box breathes is a car the
    /// hero gets nudged by while standing still.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(280)]
    public sealed class LastRouteCarSuspension : MonoBehaviour
    {
        public const string SprungBodyName = "Sprung Body";

        /// <summary>A hitch longer than this is stepped rather than
        /// swallowed, the presentation's own convention.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// A man's weight leaving the bonnet. The nose comes UP - that is
        /// the whole read, and it is why the pitch impulse is positive and
        /// much larger than the heave: he was sitting on the front overhang,
        /// not over the axle.
        /// </summary>
        public const float DismountHeaveImpulse = 0.10f;
        public const float DismountPitchImpulse = 5.2f;

        /// <summary>
        /// A man's weight arriving in a seat. Down, and leaning onto the
        /// side he got in on.
        /// </summary>
        public const float SeatHeaveImpulse = -0.075f;
        public const float SeatPitchImpulse = -1.1f;
        public const float SeatRollImpulse = 3.4f;

        /// <summary>
        /// Degrees of nose lift per metre per second squared of drive. Pulling
        /// away lifts it and braking dips it; at the drive model's `2.6 m/s²`
        /// of braking this is about a degree, which is the whole travel the
        /// springs have.
        /// </summary>
        public const float DriveLoadPitchPerAcceleration = 0.34f;

        /// <summary>
        /// Degrees of lean per metre per second squared across the car. A car
        /// turning right throws its weight left, so the sign is inverted here
        /// rather than at the call site.
        /// </summary>
        public const float DriveLoadRollPerAcceleration = -0.30f;

        /// <summary>How fast the body settles into a new steady load. Slower
        /// than the springs ring, so a corner leans in rather than snaps.
        /// </summary>
        public const float DriveLoadResponsePerSecond = 5.5f;

        private readonly LastRouteCarSuspensionModel model =
            new LastRouteCarSuspensionModel();

        private LastRouteCarAssetRegistry registry;
        private Transform sprungBody;
        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private float pivotHeight;
        private float targetLoadPitch;
        private float targetLoadRoll;
        private float loadPitch;
        private float loadRoll;

        public bool IsInitialized { get; private set; }
        public LastRouteCarSuspensionModel Model => model;
        public Transform SprungBody => sprungBody;

        public void Initialize(LastRouteCarAssetRegistry carRegistry)
        {
            if (carRegistry == null)
            {
                throw new ArgumentNullException(nameof(carRegistry));
            }

            if (!carRegistry.IsBound)
            {
                throw new ArgumentException(
                    "The car cannot be put on springs before its registry " +
                    "is bound.",
                    nameof(carRegistry));
            }

            registry = carRegistry;
            if (!TryCreateSprungBody())
            {
                return;
            }

            // Kept in the ROOT's own space, not the world's. For the years
            // this car was parked those were the same thing; once it drives,
            // a world-space rest pose would leave the bodywork standing on
            // the last route island while the wheels went up the mountain.
            Transform car = registry.transform;
            restLocalPosition = car.InverseTransformPoint(sprungBody.position);
            restLocalRotation =
                Quaternion.Inverse(car.rotation) * sprungBody.rotation;

            // The body rocks about the axle line, not about the ground and
            // not about its own origin: a car pitching about its wheel
            // centres keeps its tyres planted, which is the only reason the
            // wheels were lifted out in the first place.
            pivotHeight = registry.Dimensions.WheelRadius;
            IsInitialized = true;
            Apply();
        }

        /// <summary>He shoved off the bonnet. The front unloads.</summary>
        public void NudgeForDismount()
        {
            model.Nudge(
                DismountHeaveImpulse,
                DismountPitchImpulse,
                0f);
        }

        /// <summary>
        /// Somebody dropped into a seat. <paramref name="towardsCarRight"/>
        /// says which side took the weight, so the driver's arrival and the
        /// hero's lean opposite ways without either being spelled out here.
        /// </summary>
        public void NudgeForSeating(bool towardsCarRight)
        {
            model.Nudge(
                SeatHeaveImpulse,
                SeatPitchImpulse,
                towardsCarRight ? SeatRollImpulse : -SeatRollImpulse);
        }

        /// <summary>
        /// Somebody got back OUT of a seat. Exactly the seating kick inverted:
        /// the body rises and the side he was on comes back up.
        /// </summary>
        public void NudgeForUnseating(bool fromCarRight)
        {
            model.Nudge(
                -SeatHeaveImpulse,
                -SeatPitchImpulse,
                fromCarRight ? -SeatRollImpulse : SeatRollImpulse);
        }

        /// <summary>
        /// A man's weight arriving back on the bonnet - the dismount
        /// inverted, so the nose goes DOWN.
        /// </summary>
        public void NudgeForMount()
        {
            model.Nudge(
                -DismountHeaveImpulse,
                -DismountPitchImpulse,
                0f);
        }

        private bool TryCreateSprungBody()
        {
            Transform body = registry.Body;
            if (body == null || body.parent == null)
            {
                return false;
            }

            Transform bodyParent = body.parent;
            var sprungObject = new GameObject(SprungBodyName);
            sprungObject.layer = registry.gameObject.layer;
            sprungBody = sprungObject.transform;
            sprungBody.SetParent(bodyParent, false);
            sprungBody.localPosition = body.localPosition;
            sprungBody.localRotation = body.localRotation;
            sprungBody.localScale = body.localScale;

            // The unsprung half. Lifted out world-pose-first, so a hub that
            // was drawn touching the arch still is.
            DetachWheel(registry.FrontLeftWheel, bodyParent);
            DetachWheel(registry.FrontRightWheel, bodyParent);
            DetachWheel(registry.RearLeftWheel, bodyParent);
            DetachWheel(registry.RearRightWheel, bodyParent);
            body.SetParent(sprungBody, true);
            return true;
        }

        private static void DetachWheel(Transform wheel, Transform bodyParent)
        {
            if (wheel == null || bodyParent == null)
            {
                return;
            }

            wheel.SetParent(bodyParent, true);
        }

        /// <summary>
        /// What the road is doing to the body right now, as opposed to what
        /// somebody getting in or out did to it a moment ago.
        ///
        /// The model underneath is a struck oscillator with no forcing term,
        /// and it stays that way - a steady lean is not something a kick can
        /// express. So the sustained part of driving lives here, as an offset
        /// eased on top of the ringing, and the two add.
        /// </summary>
        public void SetDriveLoad(
            float longitudinalAcceleration,
            float lateralAcceleration)
        {
            targetLoadPitch = Sanitize(longitudinalAcceleration) *
                              DriveLoadPitchPerAcceleration;
            targetLoadRoll = Sanitize(lateralAcceleration) *
                             DriveLoadRollPerAcceleration;
        }

        public void ClearDriveLoad()
        {
            targetLoadPitch = 0f;
            targetLoadRoll = 0f;
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            float step = Mathf.Min(Time.deltaTime, MaximumStepSeconds);
            model.Advance(step);
            float response = Mathf.Clamp01(
                step * DriveLoadResponsePerSecond);
            loadPitch = Mathf.Lerp(loadPitch, targetLoadPitch, response);
            loadRoll = Mathf.Lerp(loadRoll, targetLoadRoll, response);
            Apply();
        }

        private void Apply()
        {
            if (sprungBody == null)
            {
                return;
            }

            Transform car = registry.transform;
            float pitch = Mathf.Clamp(
                model.PitchDegrees + loadPitch,
                -LastRouteCarSuspensionModel.MaximumPitchDegrees * 2f,
                LastRouteCarSuspensionModel.MaximumPitchDegrees * 2f);
            float roll = Mathf.Clamp(
                model.RollDegrees + loadRoll,
                -LastRouteCarSuspensionModel.MaximumRollDegrees * 2f,
                LastRouteCarSuspensionModel.MaximumRollDegrees * 2f);

            // Positive pitch tips the nose UP and positive roll dips the
            // car's own right, so both angles are applied negated about the
            // root's axes. Resolved against the ROOT, never against the
            // imported body node.
            Quaternion rock =
                Quaternion.AngleAxis(-pitch, car.right) *
                Quaternion.AngleAxis(-roll, car.forward);
            Vector3 rest = car.TransformPoint(restLocalPosition);
            Vector3 pivot = car.position + (car.up * pivotHeight);
            Vector3 rocked = pivot + (rock * (rest - pivot));
            sprungBody.SetPositionAndRotation(
                rocked + (car.up * model.Heave),
                rock * (car.rotation * restLocalRotation));
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
