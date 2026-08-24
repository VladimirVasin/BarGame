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
                if (rect.Contains(start) || rect.Contains(end))
                {
                    continue;
                }

                if (!SegmentIntersectsRect(start, end, rect))
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

            return blockers == 1
                ? new CitySoundOcclusionSample(
                    1,
                    OneBlockerVolume,
                    OneBlockerCutoff)
                : new CitySoundOcclusionSample(0, 1f, float.MaxValue);
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
