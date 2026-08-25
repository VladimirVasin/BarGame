using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The hero rides the Ferryman's car from inside his own head.
    ///
    /// The numbers this holds are not taste. The seat anchor is a PELVIS
    /// target, the car's roof was cut in
    /// `tools/build-last-route-car-3d-model.py` against this rig's seated
    /// head clearance band of `0.99-1.10 m` above that pelvis, and the eye
    /// has to sit inside that band with headroom left over. A lens that
    /// drifts up through the roof is the one failure this cannot be
    /// eyeballed for - it looks like a bug in the car.
    /// </summary>
    public sealed class LastRouteCarSeatViewTests
    {
        /// <summary>Where the roof underside sits over the seat pelvis
        /// anchor, from the car generator's own contract.</summary>
        private const float RoofOverSeat = 1.04f;

        [Test]
        public void SeatedEye_SitsUnderTheRoofAndAheadOfTheHips()
        {
            LastRouteCarSeatViewPlan.EvaluateCamera(
                Vector3.zero,
                Vector3.forward,
                0f,
                LastRouteCarSeatViewPlan.BasePitchDegrees,
                out Vector3 position,
                out Quaternion rotation);

            Assert.That(
                position.y,
                Is.LessThan(RoofOverSeat - 0.15f),
                "An eye that close to the roof is an eye through it once " +
                "the body rocks on its springs.");
            Assert.That(
                position.y,
                Is.GreaterThan(0.6f),
                "And one much lower is a child in the passenger seat.");
            Assert.That(
                position.z,
                Is.GreaterThan(0f),
                "A passenger's eyes are in front of his hips, not over " +
                "them - and a lens on the hips is a lens in his chest.");

            Vector3 forward = rotation * Vector3.forward;
            Assert.That(
                Vector3.Dot(forward, Vector3.forward),
                Is.GreaterThan(0.99f),
                "He looks out of the windscreen at rest; the board's own " +
                "resting pitch is derived from a board and there is none " +
                "here.");
        }

        /// <summary>
        /// The look is clamped, and the clamp that matters is the yaw: the
        /// Ferryman is sitting at the wheel less than a metre to the hero's
        /// left, and a passenger who cannot turn and look at the driver is
        /// a passenger in a diorama.
        /// </summary>
        [Test]
        public void SeatedLook_TurnsFarEnoughToFaceTheDriverAndNoFurther()
        {
            Assert.That(
                LastRouteCarSeatViewPlan.MaximumYawOffsetDegrees,
                Is.GreaterThan(80f),
                "Anything less cannot put the man at the wheel in frame.");

            LastRouteCarSeatViewPlan.EvaluateCamera(
                Vector3.zero,
                Vector3.forward,
                -400f,
                0f,
                out _,
                out Quaternion rotation);
            Vector3 forward = rotation * Vector3.forward;
            float yaw = Vector3.SignedAngle(
                Vector3.forward,
                Vector3.ProjectOnPlane(forward, Vector3.up),
                Vector3.up);
            Assert.That(
                Mathf.Abs(yaw),
                Is.EqualTo(LastRouteCarSeatViewPlan
                    .MaximumYawOffsetDegrees).Within(0.01f),
                "A spun mouse must stop at the limit, not wrap round to " +
                "the rear bench.");

            LastRouteCarSeatViewPlan.EvaluateCamera(
                Vector3.zero,
                Vector3.forward,
                0f,
                400f,
                out _,
                out Quaternion pitched);
            float pitch = pitched.eulerAngles.x;
            pitch = pitch > 180f ? pitch - 360f : pitch;
            Assert.That(
                pitch,
                Is.EqualTo(LastRouteCarSeatViewPlan.MaximumPitchDegrees)
                    .Within(0.01f));
        }

        /// <summary>
        /// The eye follows the car it is sitting in. The body is on springs
        /// and the seat anchor rides them, so the position is read off that
        /// anchor every frame rather than solved once - but the AXES stay
        /// world level, which is the bus's own hard-won rule: axes taken
        /// off a rocking body couple mouse yaw into pitch and tilt the
        /// horizon.
        /// </summary>
        [Test]
        public void SeatedEye_MovesWithTheSeatAndKeepsTheHorizonLevel()
        {
            var seat = new Vector3(12.5f, 0.62f, -4f);
            Vector3 facing = new Vector3(1f, 0.35f, 0.4f);
            LastRouteCarSeatViewPlan.EvaluateCamera(
                seat,
                facing,
                0f,
                0f,
                out Vector3 position,
                out Quaternion rotation);

            Vector3 planar = Vector3.ProjectOnPlane(
                facing,
                Vector3.up).normalized;
            Vector3 expected = seat +
                (Vector3.up *
                 LastRouteCarSeatViewPlan.EyeHeightAboveSeat) +
                (planar * LastRouteCarSeatViewPlan.EyeForwardMeters);
            Assert.That(position.x, Is.EqualTo(expected.x).Within(1e-4f));
            Assert.That(position.y, Is.EqualTo(expected.y).Within(1e-4f));
            Assert.That(position.z, Is.EqualTo(expected.z).Within(1e-4f));

            Vector3 up = rotation * Vector3.up;
            Assert.That(
                Vector3.Dot(up, Vector3.up),
                Is.GreaterThan(0.999f),
                "A pitched facing must not roll the frame.");
        }
    }
}
