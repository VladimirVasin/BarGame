using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Keeps the hero the same SIZE in a fixed shot. A shot that cannot
    /// move sees him at whatever distance the room puts him — nine metres
    /// across the far corner, two and a half beside the camera — and one
    /// authored lens makes him a doll at one end and a wall of coat at the
    /// other. This picks the lens that gives him the share of the picture
    /// the shot asks for, inside limits the composition can live with, so
    /// the room breathes in and out around him instead of him shrinking
    /// inside it.
    ///
    /// It is not a dolly zoom: nothing moves, the perspective is whatever
    /// the authored anchor sees, and the lens only ever travels between
    /// <see cref="MinimumFieldOfView"/> and
    /// <see cref="MaximumFieldOfView"/>. At the wide limit the hero simply
    /// grows — he is standing next to the lens and that is what standing
    /// next to a lens looks like.
    /// </summary>
    public readonly struct FixedCameraZoom
    {
        /// <summary>The hero's height as the lens measures it: the same
        /// sole-to-crown span <see cref="FixedCameraFocus"/> keeps in
        /// frame, so the two agree about what "the hero" is.</summary>
        public const float BodyHeight =
            FixedCameraFocus.BodyUpperHeight -
            FixedCameraFocus.BodyLowerHeight;

        public const float DefaultSmoothTime = 0.45f;
        private const float MinimumViewDepth = 0.35f;

        public static readonly FixedCameraZoom None = default;

        public FixedCameraZoom(
            float targetHeroFraction,
            float minimumFieldOfView,
            float maximumFieldOfView,
            float smoothTime)
        {
            if (!IsFinite(targetHeroFraction) ||
                targetHeroFraction <= 0.05f ||
                targetHeroFraction > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetHeroFraction),
                    "A fixed camera zoom must aim at a share of the " +
                    "picture within (0.05, 1].");
            }

            if (!IsFinite(minimumFieldOfView) ||
                !IsFinite(maximumFieldOfView) ||
                minimumFieldOfView < 20f ||
                maximumFieldOfView > 100f ||
                minimumFieldOfView >= maximumFieldOfView)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumFieldOfView),
                    "A fixed camera zoom needs an ordered lens range " +
                    "inside 20..100 degrees.");
            }

            if (!IsFinite(smoothTime) || smoothTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(smoothTime),
                    "A fixed camera zoom smooth time must be finite and " +
                    "not negative.");
            }

            Enabled = true;
            TargetHeroFraction = targetHeroFraction;
            MinimumFieldOfView = minimumFieldOfView;
            MaximumFieldOfView = maximumFieldOfView;
            SmoothTime = smoothTime;
        }

        public bool Enabled { get; }

        /// <summary>The share of the frame's height the hero should
        /// occupy, measured sole to crown.</summary>
        public float TargetHeroFraction { get; }
        public float MinimumFieldOfView { get; }
        public float MaximumFieldOfView { get; }
        public float SmoothTime { get; }

        public static FixedCameraZoom Bounded(
            float targetHeroFraction,
            float minimumFieldOfView,
            float maximumFieldOfView)
        {
            return new FixedCameraZoom(
                targetHeroFraction,
                minimumFieldOfView,
                maximumFieldOfView,
                DefaultSmoothTime);
        }

        /// <summary>
        /// The lens this zoom asks for, in degrees, from where the shot
        /// stands and where the hero is. Measured along the shot's own
        /// forward axis, not the straight line to him: the frame's height
        /// is what the hero is being compared against, and that is what
        /// the depth in front of the camera measures.
        /// </summary>
        public float Resolve(
            Vector3 cameraPosition,
            Quaternion rotation,
            float authoredFieldOfView,
            Vector3 targetRoot)
        {
            if (!Enabled)
            {
                return authoredFieldOfView;
            }

            Vector3 body =
                targetRoot +
                Vector3.up * FixedCameraFocus.BodyAimHeight;
            float depth = Vector3.Dot(
                body - cameraPosition,
                rotation * Vector3.forward);
            if (!IsFinite(depth) || depth < MinimumViewDepth)
            {
                return MaximumFieldOfView;
            }

            float bodyAngle =
                2f *
                Mathf.Atan2(BodyHeight * 0.5f, depth) *
                Mathf.Rad2Deg;
            return Mathf.Clamp(
                bodyAngle / TargetHeroFraction,
                MinimumFieldOfView,
                MaximumFieldOfView);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
