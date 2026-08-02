using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure geometric helpers used by the Home player-visibility presentation.
    /// Bounds are deliberately used instead of colliders because much of the
    /// apartment dressing is visual-only.
    /// </summary>
    public static class HomeOcclusionResolver
    {
        public const int PlayerSampleCount = 5;
        public const int ProtectedPlayerSampleCount = 4;
        public const float DefaultTargetPadding = 0.03f;

        public static void BuildPlayerSamples(
            Bounds playerBounds,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < PlayerSampleCount)
            {
                throw new ArgumentException(
                    $"At least {PlayerSampleCount} player samples are required.",
                    nameof(destination));
            }

            Vector3 right = NormalizeAxis(
                cameraRight,
                Vector3.right);
            Vector3 up = NormalizeAxis(
                cameraUp,
                Vector3.up);
            Vector3 center = playerBounds.center;
            Vector3 extents = playerBounds.extents;
            float rightExtent = Mathf.Max(
                0.05f,
                ProjectedExtent(extents, right));
            float upExtent = Mathf.Max(
                0.1f,
                ProjectedExtent(extents, up));

            // Head, left/right chest and pelvis protect the readable body.
            // The final feet sample is diagnostic only: feet alone may remain
            // naturally hidden by low furniture.
            destination[0] = center + up * (upExtent * 0.82f);
            destination[1] =
                center +
                up * (upExtent * 0.28f) -
                right * (rightExtent * 0.38f);
            destination[2] =
                center +
                up * (upExtent * 0.28f) +
                right * (rightExtent * 0.38f);
            destination[3] = center - up * (upExtent * 0.20f);
            destination[4] = center - up * (upExtent * 0.82f);
        }

        public static bool ShouldFade(
            Bounds occluderBounds,
            Vector3 cameraPosition,
            IReadOnlyList<Vector3> protectedSamples,
            float targetPadding = DefaultTargetPadding)
        {
            if (protectedSamples == null)
            {
                throw new ArgumentNullException(nameof(protectedSamples));
            }

            for (int index = 0;
                 index < protectedSamples.Count;
                 index++)
            {
                if (SegmentIntersectsBounds(
                        cameraPosition,
                        protectedSamples[index],
                        occluderBounds,
                        targetPadding))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool SegmentIntersectsBounds(
            Vector3 segmentStart,
            Vector3 segmentEnd,
            Bounds bounds,
            float targetPadding = DefaultTargetPadding)
        {
            Vector3 segment = segmentEnd - segmentStart;
            float length = segment.magnitude;
            if (!IsFinite(segmentStart) ||
                !IsFinite(segmentEnd) ||
                !IsFinite(bounds.center) ||
                !IsFinite(bounds.size) ||
                length <= 0.0001f)
            {
                return false;
            }

            float maximumDistance =
                length - Mathf.Max(0f, targetPadding);
            if (maximumDistance <= 0f)
            {
                return false;
            }

            var ray = new Ray(
                segmentStart,
                segment / length);
            return bounds.IntersectRay(
                       ray,
                       out float distance) &&
                   distance >= 0f &&
                   distance < maximumDistance;
        }

        private static float ProjectedExtent(
            Vector3 extents,
            Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extents.x +
                   Mathf.Abs(axis.y) * extents.y +
                   Mathf.Abs(axis.z) * extents.z;
        }

        private static Vector3 NormalizeAxis(
            Vector3 axis,
            Vector3 fallback)
        {
            return IsFinite(axis) &&
                   axis.sqrMagnitude > 0.0001f
                ? axis.normalized
                : fallback;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
