using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityBusPlanner
    {
        public const float DistrictStopIdeal = 200f;
        public const float DistrictStopMaximum = 230f;
        // On a short arc between two protected interchanges, two useful legs
        // of 135 m are preferable to a single 270 m gap.
        public const float DistrictStopMinimum = 135f;
        private static readonly ConditionalWeakTable<CityLayout, DistrictPlatformSite> DistrictPlatformSites = new();

        internal static bool TryPlaceDistrictStreetStop(CityLayout layout, CityBusRouteLink link,
            ref float distanceAlongLink, out Vector3 shelterPosition)
        {
            shelterPosition = default;
            float min = CityBusDesignVehicle.Default.InflatedLength * .5f + StopJunctionClearance + CandidateEndInset;
            if (link.Kind != CityBusRouteLinkKind.Straight || link.Length < 2f * min) return false;
            var site = DistrictPlatformSites.GetValue(layout, value => new DistrictPlatformSite(value));
            return TryDistrictPlatform(layout, link.Samples, site.Entrances, min, link.Length - min,
                ref distanceAlongLink, out shelterPosition);
        }

        // Slide the entire shelter and both passenger approaches as one tested placement.
        // Preserve the usual setback when the original road strip is already clear.
        private static bool TryDistrictPlatform(CityLayout layout, IReadOnlyList<CityBusPathSample> samples,
            IReadOnlyList<Vector3> entrances, float min, float max, ref float distance, out Vector3 shelter,
            StopTarget target = null)
        {
            float wanted = Mathf.Clamp(distance, min, max);
            distance = wanted;
            if (TryClearBuildingEntrances(layout, entrances, samples, min, max, ref distance, true))
            {
                EvaluateSamples(samples, distance, out Vector3 p, out Vector3 f);
                shelter = GetShelterPosition(layout, p, Vector3.Cross(Vector3.up, f).normalized);
                if (target == null || !IsInsideAny(shelter, target.ExcludedBounds)) return true;
            }

            float best = float.PositiveInfinity;
            shelter = default;
            for (int band = 0; band < 2 && float.IsPositiveInfinity(best); band++)
            for (float d = min; d < max + .001f; d += .5f)
            {
                EvaluateSamples(samples, d, out var road, out var heading);
                var forward = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, forward);
                // Lane centre 1.5 m. At 4.6 m from the road axis the closest roof edge
                // still leaves a continuous metre between the shelter and the kerb.
                float inner = band == 0 ? 3.1f : 5.85f;
                float outer = band == 0 ? 5.65f : 11.5f;
                for (float lateral = inner; lateral <= outer; lateral += .25f)
                {
                    var pole = road + lateral * right + Vector3.up *
                        (CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop);
                    float score = Mathf.Abs(d - wanted) + .15f * Mathf.Abs(lateral - 3.75f);
                    if (target != null)
                    {
                        if (IsInsideAny(pole, target.ExcludedBounds)) continue;
                        score = float.PositiveInfinity;
                        foreach (var destination in target.ReferencePositions)
                            score = Mathf.Min(score, XzDistance(pole, destination));
                    }
                    if (score >= best || !IsClearPoleSite(layout, entrances, road, forward, right, pole, true)) continue;
                    best = score;
                    distance = d;
                    shelter = pole;
                }
            }
            return !float.IsPositiveInfinity(best);
        }

        private static int DistrictStopRank(CityBusStopTargetKind kind)
        {
            if (kind == CityBusStopTargetKind.Transfer) return 0;
            if (IsResidentialFrontage(kind)) return 1;
            if (kind == CityBusStopTargetKind.DistrictPointOfInterest) return 2;
            if (kind == CityBusStopTargetKind.Supermarket) return 3;
            return 4;
        }

        private static float DistrictWalkDistance(CityLayout layout, RoadEdge a, Vector3 from, RoadEdge b, Vector3 to)
        {
            var start = Project(a, from);
            var end = Project(b, to);
            return CityTravelDistance.BetweenAnchors(layout, a, start, b, end) +
                   Vector3.Distance(start, from) + Vector3.Distance(end, to);

            Vector3 Project(RoadEdge edge, Vector3 p)
            {
                CityRoadPath road = layout.RoadGeometry.Get(edge);
                CityRoadProjection nearest = road.Project(new Vector2(p.x, p.z));
                float y = layout.ElevationPlan.SampleRoadDatum(edge, nearest.DistanceAlong / road.Length);
                return new Vector3(nearest.Position.x, y, nearest.Position.y);
            }
        }

        private sealed class DistrictPlatformSite
        {
            public readonly List<Vector3> Entrances;
            private readonly CityLayout layout;
            private readonly CityStreetSupportPlan paving;
            private readonly List<Rect> obstacles = new();

            internal DistrictPlatformSite(CityLayout layout)
            {
                this.layout = layout;
                Entrances = CreateEntranceClearancePoints(layout);
                paving = CityStreetSupportPlan.ForLayout(layout);
                // Actual wall and wing footprints are in CityBusPlatformClearance.
                foreach (var stair in layout.ElevationPlan.SignatureStairs)
                {
                    var placement = CityElevationStairPlacementPlanner.Create(layout, stair);
                    obstacles.Add(placement.Footprint);
                    obstacles.Add(placement.LowerApproachFootprint);
                    obstacles.Add(placement.UpperApproachFootprint);
                }
                foreach (var bridge in layout.River.Bridges) obstacles.Add(bridge.DeckBounds);
                foreach (var landing in layout.River.Landings)
                {
                    obstacles.Add(landing.StairBounds);
                    obstacles.Add(landing.PlatformBounds);
                }
                foreach (var access in layout.OpenAreaAccesses) obstacles.Add(access.ApproachBounds);
                foreach (var poi in layout.DistrictPointsOfInterest)
                    foreach (var access in poi.Accesses) obstacles.Add(access.ApproachBounds);
            }

            internal bool IsOpen(Vector3 position, Vector3 forward, Vector3 pole, Vector3 right)
            {
                Rect roof = CityBusStopWorldBuilder.DescribeFurnitureFootprint(pole, forward, -right);
                // A clear walking strip alongside both doors, connected to the
                // pavement before and after the stop. Keep access to the bench.
                Vector3 walk = position + right * CityBusPlatformClearance.PavementOffsetFromLane;
                Vector3 far = walk + forward * 4.3f;
                Vector3 near = walk - forward * 4.3f;
                Rect envelope = Rect.MinMaxRect(Mathf.Min(roof.xMin, Mathf.Min(far.x, near.x)) - .45f,
                    Mathf.Min(roof.yMin, Mathf.Min(far.z, near.z)) - .45f,
                    Mathf.Max(roof.xMax, Mathf.Max(far.x, near.x)) + .45f,
                    Mathf.Max(roof.yMax, Mathf.Max(far.z, near.z)) + .45f);
                Rect approach = CityBusPlatformClearance.PavementApproach(position, forward, -right);
                foreach (var obstacle in obstacles)
                    if (obstacle.Overlaps(envelope) || obstacle.Overlaps(approach)) return false;
                if (CityResidentialEntrancePlan.IntersectsPrimaryApproach(layout, envelope)) return false;
                var clearance = CityBusPlatformClearance.ForLayout(layout);
                for (int i = -3; i <= 3; i++)
                {
                    Vector3 along = forward * (i * CityBusPlatformClearance.PavementApproachLength / 3f);
                    Vector3 road = position + along;
                    Vector3 kerb = walk + along;
                    if (!paving.TrySampleTop(new Vector2(road.x, road.z), FootstepGroundKind.Concrete, out float roadY) ||
                        !paving.TrySampleTop(new Vector2(kerb.x, kerb.z), out float kerbY, out _) ||
                        Mathf.Abs(roadY - kerbY) > .28f) return false;
                }
                if (!clearance.Allows(position, forward, pole, -right)) return false;
                // Across a broad verge a roof can stand on a level shelf while a ditch or step still
                // cuts off the doors. Sample a passenger-width path from paving to the waiting side.
                float run = Vector3.Dot(pole - walk, right) - .5f;
                if (run < 2.5f) return true;
                for (int side = -1; side <= 1; side++)
                {
                    Vector3 start = walk + side * .4f * forward;
                    if (!clearance.TryHeight(start, out float previous)) return false;
                    int steps = Mathf.CeilToInt(run / .55f);
                    for (int i = 1; i <= steps; i++)
                    {
                        if (!clearance.TryHeight(start + right * (run * i / steps), out float top) ||
                            Mathf.Abs(top - previous) > .25f) return false;
                        previous = top;
                    }
                }
                return true;
            }
        }
    }
}
