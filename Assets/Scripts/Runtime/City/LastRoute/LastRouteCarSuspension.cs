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

        private readonly LastRouteCarSuspensionModel model =
            new LastRouteCarSuspensionModel();

        private LastRouteCarAssetRegistry registry;
        private Transform sprungBody;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private float pivotHeight;

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

            restPosition = sprungBody.position;
            restRotation = sprungBody.rotation;

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

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            model.Advance(Mathf.Min(Time.deltaTime, MaximumStepSeconds));
            Apply();
        }

        private void Apply()
        {
            if (sprungBody == null)
            {
                return;
            }

            Transform car = registry.transform;
            // Positive pitch tips the nose UP and positive roll dips the
            // car's own right, so both angles are applied negated about the
            // root's axes. Resolved against the ROOT, never against the
            // imported body node.
            Quaternion rock =
                Quaternion.AngleAxis(-model.PitchDegrees, car.right) *
                Quaternion.AngleAxis(-model.RollDegrees, car.forward);
            Vector3 pivot = car.position + (car.up * pivotHeight);
            Vector3 rocked = pivot + (rock * (restPosition - pivot));
            sprungBody.SetPositionAndRotation(
                rocked + (car.up * model.Heave),
                rock * restRotation);
        }
    }
}
