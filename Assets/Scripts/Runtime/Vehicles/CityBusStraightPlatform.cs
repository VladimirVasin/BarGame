using UnityEngine;

namespace BarPromenade
{
    public static partial class CityBusPlanner
    {
        // Some long turns also own the full road on either side. Only the
        // straight portion in the ordinary kerb lane can ever serve passengers.
        internal static bool TryMacroPlatform(CityLayout layout, CityBusRouteLink link,
            CityBusRouteNode from, CityBusRouteNode to, float idealDistance, out float stopDistance,
            out Vector3 pole, out RoadEdge actualEdge)
        {
            stopDistance = 0f;
            pole = default;
            actualEdge = default;
            if (link.Kind == CityBusRouteLinkKind.Straight) return false;
            float best = float.PositiveInfinity;
            float bodyEnd = CityBusDesignVehicle.Default.InflatedLength * .5f + StopJunctionClearance;
            var site = DistrictPlatformSites.GetValue(layout, value => new DistrictPlatformSite(value));
            for (float distance = bodyEnd; distance < link.Length - bodyEnd; distance += .75f)
            {
                float cost = Mathf.Abs(distance - idealDistance);
                if (cost >= best) continue;
                CityBusActor.EvaluateLink(link, distance, out var position, out var heading);
                Vector2 center = new Vector2(position.x, position.z);
                Vector2 forward = new Vector2(heading.x, heading.z).normalized;
                RoadEdge edge = from.RoadEdge;
                if (!ParallelToKerb(edge))
                {
                    edge = to.RoadEdge;
                    if (edge == from.RoadEdge || !ParallelToKerb(edge)) continue;
                }
                if (!BodyOnStraight(-bodyEnd) || !BodyOnStraight(bodyEnd)) continue;
                float selected = distance;
                if (!TryDistrictPlatform(layout, link.Samples, site.Entrances, distance, distance,
                    ref selected, out var shelter)) continue;
                pole = shelter;
                stopDistance = selected;
                actualEdge = edge;
                best = cost;

                bool ParallelToKerb(RoadEdge candidate)
                {
                    if (candidate.A == candidate.B || !layout.HasRoad(candidate) ||
                        layout.IsBusFurnitureExcluded(candidate) || layout.GetPathKind(candidate) != CityPathKind.Street)
                        return false;
                    var road = layout.RoadGeometry.Get(candidate);
                    var projection = road.Project(center);
                    float inset = layout.RoadWidth * .5f + bodyEnd;
                    if (projection.DistanceAlong < inset || projection.DistanceAlong > road.Length - inset) return false;
                    float alignment = Vector2.Dot(road.SampleDistance(projection.DistanceAlong).Tangent, forward);
                    float lane = (layout.RoadWidth - 2f * CityStreetSurfacePlanner.SidewalkWidth) * .25f;
                    return Mathf.Abs(alignment) > .997f &&
                        Mathf.Abs(projection.SignedLateral * Mathf.Sign(alignment) - lane) < .32f;
                }

                bool BodyOnStraight(float offset)
                {
                    CityBusActor.EvaluateLink(link, distance + offset, out var end, out var direction);
                    var ray = new Vector2(end.x, end.z) - center;
                    return Vector2.Dot(forward, new Vector2(direction.x, direction.z).normalized) > .994f &&
                        Mathf.Abs(forward.x * ray.y - forward.y * ray.x) < .24f;
                }
            }
            return !float.IsInfinity(best);
        }
    }
}
