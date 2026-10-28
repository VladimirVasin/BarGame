using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CitySoundOcclusionSample
    {
        public CitySoundOcclusionSample(
            int blockerCount,
            float volumeMultiplier,
            float maximumCutoffFrequency)
        {
            BlockerCount = blockerCount;
            VolumeMultiplier = volumeMultiplier;
            MaximumCutoffFrequency = maximumCutoffFrequency;
        }

        public int BlockerCount { get; }
        public float VolumeMultiplier { get; }
        public float MaximumCutoffFrequency { get; }
        public bool IsOccluded => BlockerCount > 0;
    }

    /// <summary>
    /// Deterministic coarse outdoor occlusion over authored building masses.
    /// Curbs, props and the source's own fixture cannot randomly muffle a cue.
    /// </summary>
    public static class CitySoundOcclusion
    {
        public const float OneBlockerVolume = 0.40f;
        public const float MultipleBlockerVolume = 0.10f;
        public const float OneBlockerCutoff = 2200f;
        public const float MultipleBlockerCutoff = 900f;

        /// <summary>
        /// One owner's immutable 2D building geometry. Storage is bounded by its
        /// finite lot list and authored collision polygons; it retains no world,
        /// listener or static cache. Moving endpoints require no geometry allocation.
        /// </summary>
        public sealed class Geometry
        {
            private readonly struct Footprint
            {
                internal readonly Rect Bounds;
                internal readonly Vector2[][] Polygons;
                internal Footprint(Rect bounds, Vector2[][] polygons) { Bounds = bounds; Polygons = polygons; }
            }

            private readonly Footprint[] footprints;
            public int BuildingCount { get; }
            public int PolygonCount { get; }
            public int VertexCount { get; }

            internal Geometry(IReadOnlyList<BuildingLot> lots)
            {
                if (lots == null) throw new ArgumentNullException(nameof(lots));
                footprints = new Footprint[lots.Count];
                int buildings = 0, polygons = 0, vertices = 0;
                for (int index = 0; index < lots.Count; index++)
                {
                    BuildingLot lot = lots[index];
                    if (lot == null || !lot.HasBuilding) continue;
                    Vector2[][] parts = null;
                    if (lot.HasFacadeRotation || lot.BuildingVariant > 0)
                    {
                        // These newly authored arrays belong to this snapshot;
                        // neither BuildingLot nor callers retain or expose them.
                        IReadOnlyList<Vector2[]> authored = lot.CreateCollisionPolygons();
                        parts = new Vector2[authored.Count][];
                        for (int part = 0; part < parts.Length; part++)
                        {
                            parts[part] = authored[part];
                            vertices += parts[part].Length;
                        }
                        polygons += parts.Length;
                    }
                    Rect bounds;
                    if (lot.HasFacadeRotation)
                    {
                        // Match WorldBounds exactly, reusing the already captured
                        // polygons instead of authoring the same geometry twice.
                        bounds = CityRoadPolygon.Bounds(parts[0]);
                        for (int part = 1; part < parts.Length; part++)
                        {
                            Rect next = CityRoadPolygon.Bounds(parts[part]);
                            bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, next.xMin), Mathf.Min(bounds.yMin, next.yMin),
                                Mathf.Max(bounds.xMax, next.xMax), Mathf.Max(bounds.yMax, next.yMax));
                        }
                    }
                    else
                    {
                        Bounds world = lot.WorldBounds;
                        bounds = Rect.MinMaxRect(world.min.x, world.min.z, world.max.x, world.max.z);
                    }
                    footprints[buildings++] = new Footprint(bounds, parts);
                }
                BuildingCount = buildings; PolygonCount = polygons; VertexCount = vertices;
            }

            public CitySoundOcclusionSample Evaluate(Vector3 source, Vector3 listener)
            {
                Vector2 start = new Vector2(source.x, source.z), end = new Vector2(listener.x, listener.z);
                int blockers = 0;
                for (int index = 0; index < BuildingCount; index++)
                {
                    Footprint footprint = footprints[index];
                    if (Blocks(start, end, footprint.Bounds, footprint.Polygons) && ++blockers >= 2) break;
                }
                return Result(blockers);
            }
        }

        public static Geometry Capture(IReadOnlyList<BuildingLot> lots) => new Geometry(lots);

        public static CitySoundOcclusionSample Evaluate(
            Vector3 source,
            Vector3 listener,
            IReadOnlyList<BuildingLot> lots)
        {
            if (lots == null)
            {
                throw new ArgumentNullException(nameof(lots));
            }

            Vector2 start = new Vector2(source.x, source.z);
            Vector2 end = new Vector2(listener.x, listener.z);
            int blockers = 0;
            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                if (lot == null || !lot.HasBuilding)
                {
                    continue;
                }

                Bounds bounds = lot.WorldBounds;
                var rect = Rect.MinMaxRect(
                    bounds.min.x,
                    bounds.min.z,
                    bounds.max.x,
                    bounds.max.z);
                IReadOnlyList<Vector2[]> polygons = lot.HasFacadeRotation || lot.BuildingVariant > 0
                    ? lot.CreateCollisionPolygons() : null;
                if (!Blocks(start, end, rect, polygons))
                {
                    continue;
                }

                blockers++;
                if (blockers >= 2)
                {
                    return new CitySoundOcclusionSample(
                        blockers,
                        MultipleBlockerVolume,
                        MultipleBlockerCutoff);
                }
            }

            return Result(blockers);
        }

        private static CitySoundOcclusionSample Result(int blockers) => blockers >= 2
                ? new CitySoundOcclusionSample(blockers, MultipleBlockerVolume, MultipleBlockerCutoff)
                : blockers == 1
                ? new CitySoundOcclusionSample(
                    1,
                    OneBlockerVolume,
                    OneBlockerCutoff)
                : new CitySoundOcclusionSample(0, 1f, float.MaxValue);

        private static bool Blocks(Vector2 start, Vector2 end, Rect rect, IReadOnlyList<Vector2[]> polygons)
        {
            if (polygons != null)
            {
                bool inside = false, intersects = false;
                for (int index = 0; index < polygons.Count; index++)
                {
                    Vector2[] polygon = polygons[index];
                    inside |= CityRoadPolygon.Contains(polygon, start) || CityRoadPolygon.Contains(polygon, end);
                    intersects |= SegmentIntersectsPolygon(start, end, polygon);
                }
                if (inside || !intersects) return false;
            }
            else if (rect.Contains(start) || rect.Contains(end)) return false;
            return SegmentIntersectsRect(start, end, rect);
        }

        public static bool SegmentIntersectsRect(
            Vector2 start,
            Vector2 end,
            Rect rect)
        {
            float enter = 0f;
            float exit = 1f;
            Vector2 delta = end - start;
            return Clip(-delta.x, start.x - rect.xMin, ref enter, ref exit) &&
                   Clip(delta.x, rect.xMax - start.x, ref enter, ref exit) &&
                   Clip(-delta.y, start.y - rect.yMin, ref enter, ref exit) &&
                   Clip(delta.y, rect.yMax - start.y, ref enter, ref exit) &&
                   exit >= enter;
        }

        private static bool SegmentIntersectsPolygon(Vector2 start, Vector2 end,
            IReadOnlyList<Vector2> polygon)
        {
            float enter = 0f, exit = 1f;
            Vector2 delta = end - start;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 edge = polygon[(index + 1) % polygon.Count] - polygon[index];
                float signed = CityRoadPolygon.Cross(edge, start - polygon[index]);
                float direction = CityRoadPolygon.Cross(edge, delta);
                if (Mathf.Abs(direction) < .00001f)
                {
                    if (signed < 0f) return false;
                    continue;
                }
                float boundary = -signed / direction;
                if (direction > 0f) enter = Mathf.Max(enter, boundary);
                else exit = Mathf.Min(exit, boundary);
                if (exit < enter) return false;
            }
            return true;
        }

        private static bool Clip(
            float direction,
            float distance,
            ref float enter,
            ref float exit)
        {
            if (Mathf.Abs(direction) < 0.00001f)
            {
                return distance >= 0f;
            }

            float ratio = distance / direction;
            if (direction < 0f)
            {
                if (ratio > exit)
                {
                    return false;
                }

                enter = Mathf.Max(enter, ratio);
            }
            else
            {
                if (ratio < enter)
                {
                    return false;
                }

                exit = Mathf.Min(exit, ratio);
            }

            return true;
        }
    }
}
