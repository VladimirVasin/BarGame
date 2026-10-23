using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The hall, doorway and washroom form a connected union, not a larger
    /// rectangle. Segment coverage prevents diagonal motion across its outside corners.
    /// Physics still owns furniture and the moving door leaf.
    /// </summary>
    public sealed class BarInteriorWalkableArea : IWalkableArea
    {
        private readonly Rect[] regions;

        public BarInteriorWalkableArea(BarInteriorLayoutPlan plan)
        {
            regions = new[] { plan.WalkableBounds, plan.WashroomPassageBounds, plan.WashroomBounds };
        }

        public bool Contains(Vector3 position, float radius = 0f)
        {
            for (int index = 0; index < regions.Length; index++)
                if (BarWashroomLayout.Contains(regions[index], position, radius)) return true;
            return false;
        }

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius)
        {
            Vector3 result = currentPosition;
            float best = float.PositiveInfinity;
            for (int index = 0; index < regions.Length; index++)
            {
                Rect region = regions[index];
                if (region.width < radius * 2f || region.height < radius * 2f) continue;
                Vector3 candidate = new Vector3(
                    Mathf.Clamp(desiredPosition.x, region.xMin + radius, region.xMax - radius),
                    desiredPosition.y,
                    Mathf.Clamp(desiredPosition.z, region.yMin + radius, region.yMax - radius));
                float error = (candidate - desiredPosition).sqrMagnitude;
                if (error >= best || !CoversSegment(currentPosition, candidate, radius)) continue;
                best = error;
                result = candidate;
            }
            return result;
        }

        private bool CoversSegment(Vector3 start, Vector3 end, float radius)
        {
            float covered = 0f;
            // At most three intersecting rectangles, with no per-step allocation.
            for (int pass = 0; pass < regions.Length; pass++)
            {
                float extended = covered;
                for (int index = 0; index < regions.Length; index++)
                {
                    Rect region = regions[index];
                    float near = 0f;
                    float far = 1f;
                    if (Clip(start.x, end.x - start.x, region.xMin + radius,
                            region.xMax - radius, ref near, ref far) &&
                        Clip(start.z, end.z - start.z, region.yMin + radius,
                            region.yMax - radius, ref near, ref far) && near <= covered + .0001f)
                        extended = Mathf.Max(extended, far);
                }
                if (extended >= 1f - .0001f) return true;
                if (extended <= covered) return false;
                covered = extended;
            }
            return false;
        }

        private static bool Clip(float origin, float delta, float minimum, float maximum,
            ref float near, ref float far)
        {
            if (minimum > maximum) return false;
            if (Mathf.Abs(delta) < .00001f) return origin >= minimum && origin <= maximum;
            float first = (minimum - origin) / delta;
            float second = (maximum - origin) / delta;
            near = Mathf.Max(near, Mathf.Min(first, second));
            far = Mathf.Min(far, Mathf.Max(first, second));
            return near <= far;
        }
    }
}
