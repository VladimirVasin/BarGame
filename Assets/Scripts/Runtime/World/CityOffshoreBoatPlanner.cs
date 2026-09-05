using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Finds straight east/west passes on the finite authored sea. Rotated
    /// coast and island footprints are removed before selecting a course;
    /// neither a missing coast nor a narrow gap gets a fallback boat.
    /// </summary>
    public static class CityOffshoreBoatPlanner
    {
        public const float VisualScale = 0.42f;
        public const float CycleSeconds = 440f;
        public const float MinimumCourseLength = 70f;
        public const float MaximumCourseLength = 100f;
        public const float MarginFadeDistance = 12f;
        public const float FullShorePresenceDistance = 8f;
        public const float MaximumShoreDistance = 28f;
        public const float MaximumLocalCourseOffset = 35f;

        // Includes the <=14 m authored hull at .42 scale, its forward
        // searchlight throw and a clearance margin. Lane headings are exactly
        // east/west, so X is longitudinal and Z is the narrow dimension.
        public const float SweptLongitudinalHalfExtent = 8.5f;
        public const float SweptLateralHalfExtent = 2f;
        private const float LaneSeparation = 4.5f;
        private const float BoundaryMargin = 0.5f;

        private readonly struct Course
        {
            public Course(float minX, float maxX, float z, float score)
            {
                MinX = minX;
                MaxX = maxX;
                Z = z;
                Score = score;
            }

            public float MinX { get; }
            public float MaxX { get; }
            public float Z { get; }
            public float Score { get; }
        }

        public static CityOffshoreBoatPlan Create(
            int seed, CitySeacoastPlan coast, CityLighthouseIslandPlan island)
        {
            if (coast == null)
                return null;
            float preferredX = coast.TryGetPart(CitySeacoastPlanner.PierDeckHeadId,
                out CitySeacoastPartDescriptor pier)
                ? pier.Center.x
                : coast.Frame.CenterZone.center.x;
            return Create(seed, coast, island, preferredX);
        }

        /// <summary>
        /// Selects world-fixed courses near the hero's shoreline position.
        /// A remote clear lane must not populate an unrelated coast segment.
        /// </summary>
        public static CityOffshoreBoatPlan Create(
            int seed, CitySeacoastPlan coast, CityLighthouseIslandPlan island,
            float preferredX)
        {
            if (coast == null)
                return null;
            if (float.IsNaN(preferredX) || float.IsInfinity(preferredX))
                throw new ArgumentOutOfRangeException(nameof(preferredX));
            CitySeacoastFrame frame = coast.Frame;
            var sheets = new List<Rect>();
            CitySeacoastSeaLayout.CreateSheetRects(frame, sheets);
            if (sheets.Count == 0)
                return null;

            Rect water = sheets[0];
            for (int index = 1; index < sheets.Count; index++)
            {
                Rect sheet = sheets[index];
                water = Rect.MinMaxRect(
                    Mathf.Min(water.xMin, sheet.xMin),
                    Mathf.Min(water.yMin, sheet.yMin),
                    Mathf.Max(water.xMax, sheet.xMax),
                    Mathf.Max(water.yMax, sheet.yMax));
            }

            var obstacles = new List<Rect>(coast.Parts.Count + (island?.Count ?? 0));
            for (int index = 0; index < coast.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = coast.Parts[index];
                obstacles.Add(ProjectedBounds(part.Center, part.Rotation, part.Size));
            }
            if (island != null)
            {
                for (int index = 0; index < island.Parts.Count; index++)
                {
                    CityLighthouseIslandPartDescriptor part = island.Parts[index];
                    obstacles.Add(ProjectedBounds(part.Center, part.Rotation, part.Size));
                }
            }

            var candidates = new List<Course>();
            // These are candidate depths, never a promise that the whole band
            // is clear. The island occupies it and may split a lane in two.
            for (int lane = 0; lane < 3; lane++)
            {
                float z = frame.WaterlineZ + 30f + lane * LaneSeparation;
                if (z - SweptLateralHalfExtent < water.yMin + BoundaryMargin ||
                    z + SweptLateralHalfExtent > water.yMax - BoundaryMargin)
                    continue;
                AppendCourses(candidates, water, obstacles, preferredX, z, lane);
            }
            candidates.Sort((left, right) => right.Score.CompareTo(left.Score));

            var routes = new List<CityOffshoreBoatRoute>(CityOffshoreBoatPlan.MaximumBoatCount);
            float firstLaneZ = float.NaN;
            float phase = Unit(Hash(seed, 0)) * CycleSeconds;
            for (int index = 0; index < candidates.Count &&
                routes.Count < CityOffshoreBoatPlan.MaximumBoatCount; index++)
            {
                Course course = candidates[index];
                if (routes.Count > 0 && Mathf.Abs(course.Z - firstLaneZ) < LaneSeparation - 0.01f)
                    continue;
                int ordinal = routes.Count;
                float desiredLength = 92f + Unit(Hash(seed, 10 + ordinal)) * 8f;
                float length = Mathf.Min(desiredLength, course.MaxX - course.MinX);
                // Near an obstacle or sea edge, prefer a shorter local pass
                // over shifting a long course into a different shore segment.
                float localLength = 2f * (MaximumLocalCourseOffset +
                    Mathf.Min(preferredX - course.MinX, course.MaxX - preferredX));
                length = Mathf.Min(length, localLength);
                if (length < MinimumCourseLength)
                    continue;
                float center = Mathf.Clamp(preferredX,
                    course.MinX + length * 0.5f, course.MaxX - length * 0.5f);
                if (Mathf.Abs(center - preferredX) > MaximumLocalCourseOffset + 0.001f)
                    continue;
                var start = new Vector3(center - length * 0.5f, frame.SeaTopY, course.Z);
                var end = new Vector3(center + length * 0.5f, frame.SeaTopY, course.Z);
                bool reverse = ((Hash(seed, 20) & 1u) != 0u) ^ (ordinal == 1);
                float speed = 0.42f + Unit(Hash(seed, 30 + ordinal)) * 0.04f;
                routes.Add(new CityOffshoreBoatRoute(
                    "offshore-fishing-boat-" + ordinal,
                    reverse ? end : start, reverse ? start : end,
                    ordinal, length / speed, CycleSeconds,
                    (phase + ordinal * CycleSeconds * 0.5f) % CycleSeconds,
                    VisualScale, MarginFadeDistance));
                if (ordinal == 0)
                    firstLaneZ = course.Z;
            }

            return routes.Count == 0
                ? null
                : new CityOffshoreBoatPlan(routes, frame.SeaTopY, frame.WaterlineZ);
        }

        /// <summary>
        /// Hero proximity to the finite shore, including the actual walkable
        /// pier and mol extensions. Height and camera position do not own it.
        /// </summary>
        public static float ShorePresence(CitySeacoastPlan coast, Vector3 heroPosition)
        {
            if (coast == null || float.IsNaN(heroPosition.x) ||
                float.IsNaN(heroPosition.z) || float.IsInfinity(heroPosition.x) ||
                float.IsInfinity(heroPosition.z))
                return 0f;
            CitySeacoastFrame frame = coast.Frame;
            // Every deck considered below is waterward of this line. Most
            // City frames can reject the hero without walking the part list.
            if (heroPosition.z <= frame.WaterlineZ - MaximumShoreDistance)
                return 0f;
            Rect shoreline = Rect.MinMaxRect(frame.BeachRowBounds.xMin, frame.WaterlineZ,
                frame.BeachRowBounds.xMax, frame.WaterlineZ);
            float distanceSquared = DistanceSquared(shoreline, heroPosition);
            for (int index = 0; index < coast.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = coast.Parts[index];
                if (part.Kind != CitySeacoastPartKind.PierDeck &&
                    part.Kind != CitySeacoastPartKind.MolDeck)
                    continue;
                Rect deck = ProjectedBounds(part.Center, part.Rotation, part.Size);
                if (deck.yMax < frame.WaterlineZ)
                    continue;
                deck.yMin = Mathf.Max(deck.yMin, frame.WaterlineZ);
                distanceSquared = Mathf.Min(distanceSquared, DistanceSquared(deck, heroPosition));
            }
            float fade = Mathf.InverseLerp(FullShorePresenceDistance,
                MaximumShoreDistance, Mathf.Sqrt(distanceSquared));
            return 1f - fade * fade * (3f - 2f * fade);
        }

        private static float DistanceSquared(Rect bounds, Vector3 position)
        {
            float dx = position.x - Mathf.Clamp(position.x, bounds.xMin, bounds.xMax);
            float dz = position.z - Mathf.Clamp(position.z, bounds.yMin, bounds.yMax);
            return dx * dx + dz * dz;
        }

        private static void AppendCourses(
            ICollection<Course> courses, Rect water, IReadOnlyList<Rect> obstacles,
            float preferredX, float z, int lane)
        {
            float left = water.xMin + SweptLongitudinalHalfExtent + BoundaryMargin;
            float right = water.xMax - SweptLongitudinalHalfExtent - BoundaryMargin;
            if (right - left < MinimumCourseLength)
                return;
            var intervals = new List<Vector2> { new Vector2(left, right) };
            for (int index = 0; index < obstacles.Count; index++)
            {
                Rect obstacle = obstacles[index];
                if (obstacle.yMax + SweptLateralHalfExtent < z ||
                    obstacle.yMin - SweptLateralHalfExtent > z)
                    continue;
                float cutLeft = obstacle.xMin - SweptLongitudinalHalfExtent;
                float cutRight = obstacle.xMax + SweptLongitudinalHalfExtent;
                for (int intervalIndex = intervals.Count - 1; intervalIndex >= 0; intervalIndex--)
                {
                    Vector2 interval = intervals[intervalIndex];
                    if (cutRight <= interval.x || cutLeft >= interval.y)
                        continue;
                    intervals.RemoveAt(intervalIndex);
                    if (cutLeft > interval.x)
                        intervals.Insert(intervalIndex, new Vector2(interval.x, cutLeft));
                    if (cutRight < interval.y)
                        intervals.Insert(intervalIndex, new Vector2(cutRight, interval.y));
                }
            }
            for (int index = 0; index < intervals.Count; index++)
            {
                Vector2 interval = intervals[index];
                float length = interval.y - interval.x;
                if (length < MinimumCourseLength)
                    continue;
                float center = Mathf.Clamp(preferredX,
                    interval.x + MinimumCourseLength * 0.5f,
                    interval.y - MinimumCourseLength * 0.5f);
                if (Mathf.Abs(center - preferredX) > MaximumLocalCourseOffset)
                    continue;
                float score = Mathf.Min(length, MaximumCourseLength) -
                    Mathf.Abs(center - preferredX) * 0.5f - lane * 2f;
                courses.Add(new Course(interval.x, interval.y, z, score));
            }
        }

        internal static Rect ProjectedBounds(Vector3 center, Quaternion rotation, Vector3 size)
        {
            Vector3 x = rotation * (Vector3.right * Mathf.Abs(size.x) * 0.5f);
            Vector3 y = rotation * (Vector3.up * Mathf.Abs(size.y) * 0.5f);
            Vector3 z = rotation * (Vector3.forward * Mathf.Abs(size.z) * 0.5f);
            float halfX = Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x);
            float halfZ = Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z);
            return Rect.MinMaxRect(center.x - halfX, center.z - halfZ,
                center.x + halfX, center.z + halfZ);
        }

        private static uint Hash(int seed, int ordinal)
        {
            unchecked
            {
                uint value = (uint)seed ^ 0x4F464253u ^ ((uint)ordinal * 0x9E3779B9u);
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }

        private static float Unit(uint value)
        {
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
