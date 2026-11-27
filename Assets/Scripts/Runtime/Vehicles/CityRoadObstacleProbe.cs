using System;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityRoadObstacleProbe
    {
        internal delegate void SamplePath(float metres, out Vector3 position, out Vector3 forward);

        private const float NearStep = .6f;
        private const float ProbeSpacing = 1.25f;
        private const float MaximumReach = 17f;
        private const float RefreshSeconds = .13f;
        private Collider[] scratch = new Collider[16];
        private Collider cached;
        private Bounds cachedBounds;
        private float cachedDistance = float.PositiveInfinity;
        private float refreshAt;
        private float covered;

        internal void Reset()
        {
            cached = null;
            cachedDistance = float.PositiveInfinity;
            refreshAt = 0f;
            covered = 0f;
        }

        internal void Travel(float metres) => covered += Mathf.Max(0f, metres);

        internal Collider Find(SamplePath sample, Transform owner, Bounds bounds, float reach,
            out float distance, bool checkAllNear = false)
        {
            reach = Mathf.Clamp(reach, NearStep, MaximumReach);
            sample(NearStep, out var near, out var nearForward);
            var immediate = Overlap(owner, bounds, near, Quaternion.LookRotation(nearForward),
                ignoreRoadUsers: !checkAllNear);
            if (immediate != null)
            {
                distance = 0f;
                return immediate;
            }

            if (Time.time >= refreshAt || covered >= ProbeSpacing * .5f ||
                cached != null && !cached.bounds.Equals(cachedBounds))
            {
                Reset();
                refreshAt = Time.time + RefreshSeconds;
                float length = NearStep;
                while (length < reach)
                {
                    float next = Mathf.Min(reach, length + ProbeSpacing);
                    sample(next, out var point, out var forward);
                    var hit = Overlap(owner, bounds, point, Quaternion.LookRotation(forward), ignoreRoadUsers: true);
                    if (hit != null)
                    {
                        cached = hit;
                        cachedBounds = hit.bounds;
                        // The previous body sample was clear. Stop before the
                        // unsampled interval, with the caller's separate road gap.
                        cachedDistance = length;
                        break;
                    }
                    length = next;
                }
            }

            distance = cached != null && cached.enabled && cached.gameObject.activeInHierarchy
                ? Mathf.Max(0f, cachedDistance - covered)
                : float.PositiveInfinity;
            return float.IsPositiveInfinity(distance) ? null : cached;
        }

        internal Collider Overlap(Transform owner, Bounds bounds, Vector3 position, Quaternion rotation,
            float lengthPadding = 0f, bool ignoreRoadUsers = false)
        {
            // Keep the box above wheel contact to exclude the surveyed road.
            float bottom = Mathf.Max(.4f, bounds.min.y);
            float halfHeight = Mathf.Max(.1f, (bounds.max.y - bottom) * .5f);
            Vector3 centre = position + rotation * new Vector3(bounds.center.x, bottom + halfHeight, bounds.center.z);
            var extents = new Vector3(bounds.extents.x + .04f, halfHeight, bounds.extents.z + .04f + lengthPadding);
            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(centre, extents, scratch, rotation, ~0, QueryTriggerInteraction.Ignore);
                if (count < scratch.Length || scratch.Length >= 128) break;
                Array.Resize(ref scratch, scratch.Length * 2);
            }
            var hits = count == scratch.Length
                ? Physics.OverlapBox(centre, extents, rotation, ~0, QueryTriggerInteraction.Ignore)
                : scratch;
            int total = hits == scratch ? count : hits.Length;
            Collider blocker = null;
            for (int i = 0; i < total; i++)
            {
                var hit = hits[i];
                if (hit == null || hit.transform.IsChildOf(owner) || hit.bounds.max.y <= position.y + .4f)
                    continue;
                if (ignoreRoadUsers && IsTrackedRoadUser(hit)) continue;
                blocker = hit;
                break;
            }
            Array.Clear(scratch, 0, count);
            return blocker;
        }

        internal static bool IsTrackedRoadUser(Collider hit)
        {
            var root = hit.transform;
            return root.GetComponentInParent<CityBusActor>() != null ||
                root.GetComponentInParent<LastRouteCarDriver>() != null ||
                root.GetComponentInParent<CityPedestrianActor>() != null ||
                hit is CharacterController;
        }
    }
}
