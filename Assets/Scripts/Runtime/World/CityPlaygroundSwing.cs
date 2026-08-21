using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One hanging swing seat. The physics is an honest pendulum — a
    /// hinged rigid body under the top beam — and this component is only
    /// the push: while someone walks into the plank, the seat is
    /// accelerated up to that walker's own pace along the swing plane
    /// and never past it, so a swing leaves the hero's hands at walking
    /// speed instead of being catapulted.
    ///
    /// Anything with a <see cref="CharacterController"/> pushes: the
    /// hero, and an ambient walker that wanders into one.
    /// </summary>
    public sealed class CityPlaygroundSwing : MonoBehaviour
    {
        /// <summary>Below this approach speed a walker is leaning on the
        /// plank, not pushing it.</summary>
        public const float MinimumPushSpeed = 0.25f;

        /// <summary>How hard the seat is dragged towards the walker's
        /// pace, per second of contact.</summary>
        public const float PushResponse = 9f;

        /// <summary>A walker standing exactly under the pivot has no
        /// push side; ignore him until he is clearly behind the
        /// plank.</summary>
        public const float MinimumPushOffset = 0.05f;

        private Rigidbody body;
        private Vector3 seatLocalCenter;
        private Vector3 pushAxis = Vector3.forward;
        private Collider lastNonWalkerCollider;

        public bool IsInitialized { get; private set; }

        /// <summary>How many physics steps have actually pushed this
        /// seat. Zero while nobody has walked into it.</summary>
        public int PushCount { get; private set; }

        /// <summary>How many physics steps have reported a walker
        /// inside the push volume, pushing or not.</summary>
        public int ContactCount { get; private set; }

        public Rigidbody Body => body;

        /// <summary>The horizontal direction the seat travels in, in
        /// world space. It survives the swinging itself: the hinge axis
        /// never turns.</summary>
        public Vector3 PushAxis => pushAxis;

        /// <summary>The plank's centre, in world space.</summary>
        public Vector3 SeatCenter =>
            transform.TransformPoint(seatLocalCenter);

        public void Initialize(Rigidbody seatBody, Vector3 localSeatCenter)
        {
            if (seatBody == null)
            {
                throw new ArgumentNullException(nameof(seatBody));
            }

            body = seatBody;
            seatLocalCenter = localSeatCenter;
            Vector3 axis = Vector3.Cross(transform.right, Vector3.up);
            axis.y = 0f;
            pushAxis = axis.sqrMagnitude > 0.0001f
                ? axis.normalized
                : Vector3.forward;
            IsInitialized = true;
        }

        private void OnTriggerStay(Collider other)
        {
            // Triggers are ignored on purpose: the hero carries a
            // trigger cloth capsule beside his controller, and one
            // walker must not push twice in a step.
            if (!IsInitialized ||
                body == null ||
                other == null ||
                other.isTrigger)
            {
                return;
            }

            // A collider's walker-ness never changes, and a static
            // overlap (a terrain edge, a slab) re-fires every physics
            // step - remember the last miss instead of paying a
            // GetComponentInParent for it each time.
            if (other == lastNonWalkerCollider)
            {
                return;
            }

            CharacterController walker =
                other.GetComponentInParent<CharacterController>();
            if (walker == null)
            {
                lastNonWalkerCollider = other;
                return;
            }

            ContactCount++;
            Push(walker);
        }

        private void Push(CharacterController walker)
        {
            Vector3 seat = SeatCenter;
            float side = Vector3.Dot(
                seat - walker.transform.position,
                pushAxis);
            if (Mathf.Abs(side) < MinimumPushOffset)
            {
                return;
            }

            float pace = Vector3.Dot(walker.velocity, pushAxis);
            if (Mathf.Abs(pace) < MinimumPushSpeed ||
                (pace > 0f) != (side > 0f))
            {
                return;
            }

            // Only ever close the gap to the walker's pace: once the
            // seat swings away faster than he walks, the contact is
            // over and gravity owns the arc again.
            float gain =
                pace -
                Vector3.Dot(body.GetPointVelocity(seat), pushAxis);
            if ((gain > 0f) != (pace > 0f))
            {
                return;
            }

            PushCount++;
            body.WakeUp();
            body.AddForceAtPosition(
                pushAxis * (gain * PushResponse),
                seat,
                ForceMode.Acceleration);
        }
    }
}
