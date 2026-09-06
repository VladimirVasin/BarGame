using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Keeps the hero inside a fixed camera shot without giving up the
    /// authored frame. The shot holds exactly as composed for as long as
    /// he stands comfortably inside the picture; once he reaches its edge
    /// the shot pans by the smallest angle that brings him back into the
    /// safe frame, and never by more than the authored maximum. The camera
    /// does not move, only its aim, so a fixed shot stays a fixed shot.
    ///
    /// The solve is a search along one family of aims: rotating the shot
    /// all the way onto <see cref="BodyAimHeight"/> centres the hero
    /// exactly, so every fraction of that rotation walks him from where he
    /// sits in the frame toward the middle. A bisection over that fraction
    /// finds the first one that puts both his feet and the crown of his
    /// head inside <see cref="SafeFrame"/>, which is the least the camera
    /// can turn and still see all of him. Working on the real frustum
    /// matters: the naive yaw and pitch of a corner of the picture are far
    /// wider than the corner itself, and a dead zone measured that way
    /// lets a hero standing beside the camera slide off the bottom of the
    /// screen.
    /// </summary>
    public readonly struct FixedCameraFocus
    {
        /// <summary>Chest height above the player root: the point the shot
        /// aims at when it turns all the way onto him.</summary>
        public const float BodyAimHeight = 1.05f;

        /// <summary>The two points that must stay in frame: the ground
        /// under his boots and the crown of his head.</summary>
        public const float BodyLowerHeight = 0.05f;
        public const float BodyUpperHeight = 1.78f;

        public const float DefaultSafeFrame = 0.8f;
        public const float DefaultSmoothTime = 0.3f;
        public const float DefaultAspect = 16f / 9f;

        private const int SolverSteps = 7;
        private const float MinimumViewDepth = 0.05f;

        public static readonly FixedCameraFocus None = default;

        public FixedCameraFocus(
            float maximumYawDegrees,
            float maximumPitchDegrees,
            float safeFrame,
            float smoothTime)
        {
            if (!IsFinite(maximumYawDegrees) ||
                maximumYawDegrees < 0f ||
                maximumYawDegrees > 45f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumYawDegrees),
                    "A fixed camera focus may pan at most 45 degrees.");
            }

            if (!IsFinite(maximumPitchDegrees) ||
                maximumPitchDegrees < 0f ||
                maximumPitchDegrees > 45f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPitchDegrees),
                    "A fixed camera focus may tilt at most 45 degrees.");
            }

            if (maximumYawDegrees <= 0f &&
                maximumPitchDegrees <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumYawDegrees),
                    "A fixed camera focus that can neither pan nor tilt " +
                    "is not a focus.");
            }

            if (!IsFinite(safeFrame) ||
                safeFrame <= 0.1f ||
                safeFrame > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(safeFrame),
                    "A fixed camera focus safe frame must be within " +
                    "(0.1, 1].");
            }

            if (!IsFinite(smoothTime) || smoothTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(smoothTime),
                    "A fixed camera focus smooth time must be finite " +
                    "and not negative.");
            }

            Enabled = true;
            MaximumYawDegrees = maximumYawDegrees;
            MaximumPitchDegrees = maximumPitchDegrees;
            SafeFrame = safeFrame;
            SmoothTime = smoothTime;
        }

        public bool Enabled { get; }
        public float MaximumYawDegrees { get; }
        public float MaximumPitchDegrees { get; }

        /// <summary>The fraction of the picture the hero is kept inside,
        /// measured from its centre to its edges.</summary>
        public float SafeFrame { get; }
        public float SmoothTime { get; }

        public static FixedCameraFocus Bounded(
            float maximumYawDegrees,
            float maximumPitchDegrees)
        {
            return new FixedCameraFocus(
                maximumYawDegrees,
                maximumPitchDegrees,
                DefaultSafeFrame,
                DefaultSmoothTime);
        }

        /// <summary>
        /// The pan this focus asks of a shot, in degrees: x is yaw about
        /// the world up, y is pitch. Zero while the hero already stands
        /// inside the safe frame.
        /// </summary>
        public Vector2 Resolve(
            Vector3 cameraPosition,
            Quaternion baseRotation,
            float verticalFieldOfView,
            float aspect,
            Vector3 targetRoot)
        {
            if (!Enabled)
            {
                return Vector2.zero;
            }

            Decompose(
                baseRotation,
                out float baseYaw,
                out float basePitch);
            Vector3 delta =
                targetRoot +
                Vector3.up * BodyAimHeight -
                cameraPosition;
            float horizontal =
                new Vector2(delta.x, delta.z).magnitude;
            if (horizontal <= 0.0001f)
            {
                return Vector2.zero;
            }

            float fullYaw = Mathf.DeltaAngle(
                baseYaw,
                Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
            float fullPitch = Mathf.DeltaAngle(
                basePitch,
                Mathf.Atan2(-delta.y, horizontal) * Mathf.Rad2Deg);

            float tangentVertical = Mathf.Tan(
                Mathf.Clamp(verticalFieldOfView, 1f, 179f) *
                0.5f *
                Mathf.Deg2Rad);
            float tangentHorizontal =
                tangentVertical * Mathf.Max(aspect, 0.1f);
            Vector3 lower =
                targetRoot + Vector3.up * BodyLowerHeight;
            Vector3 upper =
                targetRoot + Vector3.up * BodyUpperHeight;

            if (IsFramed(
                    cameraPosition,
                    baseYaw,
                    basePitch,
                    tangentHorizontal,
                    tangentVertical,
                    lower,
                    upper))
            {
                return Vector2.zero;
            }

            float low = 0f;
            float high = 1f;
            for (int step = 0; step < SolverSteps; step++)
            {
                float middle = (low + high) * 0.5f;
                if (IsFramed(
                        cameraPosition,
                        baseYaw + fullYaw * middle,
                        basePitch + fullPitch * middle,
                        tangentHorizontal,
                        tangentVertical,
                        lower,
                        upper))
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            return new Vector2(
                Mathf.Clamp(
                    fullYaw * high,
                    -MaximumYawDegrees,
                    MaximumYawDegrees),
                Mathf.Clamp(
                    fullPitch * high,
                    -MaximumPitchDegrees,
                    MaximumPitchDegrees));
        }

        /// <summary>
        /// Turns an authored shot rotation by a resolved offset. The
        /// rebuild goes through world yaw and pitch on purpose: turning
        /// about the camera's own up would roll a pitched-down shot and
        /// tip the horizon.
        /// </summary>
        public static Quaternion Compose(
            Quaternion baseRotation,
            Vector2 offset)
        {
            if (offset == Vector2.zero)
            {
                return baseRotation;
            }

            Decompose(
                baseRotation,
                out float yaw,
                out float pitch);
            return Quaternion.Euler(
                pitch + offset.y,
                yaw + offset.x,
                0f);
        }

        private static void Decompose(
            Quaternion rotation,
            out float yaw,
            out float pitch)
        {
            Vector3 forward = rotation * Vector3.forward;
            float horizontal =
                new Vector2(forward.x, forward.z).magnitude;
            yaw = horizontal <= 0.0001f
                ? rotation.eulerAngles.y
                : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            pitch = Mathf.Atan2(-forward.y, horizontal) *
                    Mathf.Rad2Deg;
        }

        private bool IsFramed(
            Vector3 cameraPosition,
            float yaw,
            float pitch,
            float tangentHorizontal,
            float tangentVertical,
            Vector3 lower,
            Vector3 upper)
        {
            Quaternion inverse = Quaternion.Inverse(
                Quaternion.Euler(pitch, yaw, 0f));
            return IsFramed(
                       inverse * (lower - cameraPosition),
                       tangentHorizontal,
                       tangentVertical) &&
                   IsFramed(
                       inverse * (upper - cameraPosition),
                       tangentHorizontal,
                       tangentVertical);
        }

        private bool IsFramed(
            Vector3 view,
            float tangentHorizontal,
            float tangentVertical)
        {
            if (view.z <= MinimumViewDepth)
            {
                return false;
            }

            return Mathf.Abs(view.x) <=
                   view.z * tangentHorizontal * SafeFrame &&
                   Mathf.Abs(view.y) <=
                   view.z * tangentVertical * SafeFrame;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
