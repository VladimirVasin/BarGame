using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The city's answer to "can the player stand here".
    ///
    /// The mask, not the colliders, is the real boundary of the city, and it
    /// is tested one rectangle at a time - so a point the authored gate says
    /// is inside can still fall in a hole the river cut. Where that happens
    /// the nearest legal point is used, and its height re-sampled, rather
    /// than dropping the player in.
    ///
    /// Solid obstacle footprints are the one thing the mask does NOT exclude:
    /// their underlying ground is walkable and a collider stands on it, which
    /// is right for walking and wrong for arriving. The lattice would
    /// otherwise offer buildings and courtyard fixtures as destinations, so
    /// their footprints are subtracted here.
    /// </summary>
    public sealed class CityMapCityTeleportGround : ICityMapTeleportGround
    {
        private readonly CityLayout layout;
        private readonly List<Rect> obstacleFootprints;
        private RoadWalkableArea walkableArea;

        public CityMapCityTeleportGround(CityLayout layout)
        {
            this.layout = layout ??
                          throw new ArgumentNullException(nameof(layout));
            obstacleFootprints = CollectObstacleFootprints(layout);
        }

        public GameAreaId Area => GameAreaId.City;

        public bool TryResolveStandingPosition(
            Vector2 worldXZ,
            out Vector3 standingPosition)
        {
            standingPosition = default;
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            var probe = new Vector3(worldXZ.x, 0f, worldXZ.y);
            RoadWalkableArea mask = EnsureWalkableArea();
            Vector3 candidate = mask.Contains(probe, radius)
                ? probe
                : mask.ClosestPoint(probe, radius);
            var landing = new Vector2(candidate.x, candidate.z);
            if (!mask.Contains(
                    new Vector3(landing.x, 0f, landing.y),
                    radius) ||
                IsInsideObstacle(landing))
            {
                return false;
            }

            if (!TryResolveSurfaceTop(landing, out float top))
            {
                return false;
            }

            standingPosition = new Vector3(
                landing.x,
                top + PlayerFactory.GroundedRootOffset,
                landing.y);
            return true;
        }

        public bool TryClampArrival(Vector3 arrival, out Vector3 destination)
        {
            RoadWalkableArea mask = EnsureWalkableArea();
            destination = arrival;
            if (mask.Contains(
                    arrival,
                    CityGroundTraversalPlanner.MaximumAgentRadius))
            {
                return true;
            }

            Vector3 nearest = mask.ClosestPoint(
                arrival,
                CityGroundTraversalPlanner.MaximumAgentRadius);
            if (!CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout,
                    new Vector2(nearest.x, nearest.z),
                    out float groundTop,
                    out CitySurfaceDescriptor surface) ||
                surface.IsWater)
            {
                return false;
            }

            destination = new Vector3(
                nearest.x,
                groundTop + PlayerFactory.GroundedRootOffset,
                nearest.z);
            return true;
        }

        /// <summary>
        /// Roads first, because the carriageway runs between the cells and
        /// no cell surface covers it. This is the same order the whole-lot
        /// teleport uses to put a hero on a frontage.
        /// </summary>
        private bool TryResolveSurfaceTop(Vector2 worldXZ, out float top)
        {
            if (layout.ElevationPlan != null &&
                layout.ElevationPlan.TrySampleSurface(
                    worldXZ,
                    CitySurfaceRole.RoadTop,
                    out float roadTop,
                    out _))
            {
                top = roadTop;
                return true;
            }

            if (CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout,
                    worldXZ,
                    out float groundTop,
                    out CitySurfaceDescriptor surface) &&
                !surface.IsWater)
            {
                top = groundTop;
                return true;
            }

            top = 0f;
            return false;
        }

        private bool IsInsideObstacle(Vector2 worldXZ)
        {
            for (int index = 0; index < obstacleFootprints.Count; index++)
            {
                if (obstacleFootprints[index].Contains(worldXZ))
                {
                    return true;
                }
            }

            return false;
        }

        private RoadWalkableArea EnsureWalkableArea()
        {
            // Built on first use, not in the constructor: it is the same
            // mask CityWorldBuilder makes, and building it costs a full
            // layout validation nobody needs unless a teleport happens.
            return walkableArea ??= RoadWalkableArea.FromLayout(layout);
        }

        private static List<Rect> CollectObstacleFootprints(
            CityLayout layout)
        {
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            var footprints = new List<Rect>(
                layout.BuildingLots.Count + 1);
            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.HasBuilding)
                {
                    continue;
                }

                footprints.Add(Expand(
                    Rect.MinMaxRect(
                        lot.Center.x - lot.Size.x * 0.5f,
                        lot.Center.z - lot.Size.y * 0.5f,
                        lot.Center.x + lot.Size.x * 0.5f,
                        lot.Center.z + lot.Size.y * 0.5f),
                    radius));
            }

            CityChurchPlan church = CityChurchPlanner.Create(layout);
            if (church != null)
            {
                footprints.Add(Expand(church.ModelFootprint, radius));
                CityChurchCemeteryPassagePlan passage =
                    CityChurchCemeteryPassagePlanner.Create(
                        layout,
                        church);
                CityChurchCourtyardPlan courtyard =
                    CityChurchCourtyardPlanner.Create(
                        layout,
                        church,
                        passage);
                if (courtyard != null)
                {
                    for (int index = 0;
                         index < courtyard.Fixtures.Count;
                         index++)
                    {
                        footprints.Add(Expand(
                            courtyard.Fixtures[index].BlockerBounds,
                            radius));
                    }
                }
            }

            return footprints;
        }

        private static Rect Expand(Rect bounds, float amount)
        {
            return Rect.MinMaxRect(
                bounds.xMin - amount,
                bounds.yMin - amount,
                bounds.xMax + amount,
                bounds.yMax + amount);
        }
    }

    /// <summary>
    /// The mountain road's answer to the same question, and the reason the
    /// map needs one per area at all.
    ///
    /// Height is exact here rather than sampled: the road IS its centreline
    /// samples, the plateau is one flat slab and the tunnel throat is the
    /// portal's own floor, so an arrival can be placed on the real surface
    /// instead of on a guess about terrain.
    /// </summary>
    public sealed class CityMapMountainRoadTeleportGround
        : ICityMapTeleportGround
    {
        private const float Epsilon = 0.0001f;

        private readonly MountainRoadPlan plan;
        private readonly MountainRoadWalkableArea walkableArea;

        public CityMapMountainRoadTeleportGround(MountainRoadPlan plan)
            : this(new MountainRoadWalkableArea(plan))
        {
        }

        public CityMapMountainRoadTeleportGround(
            MountainRoadWalkableArea area)
        {
            walkableArea = area ??
                           throw new ArgumentNullException(nameof(area));
            plan = area.Plan;
        }

        public GameAreaId Area => GameAreaId.MountainRoad;

        public bool TryResolveStandingPosition(
            Vector2 worldXZ,
            out Vector3 standingPosition)
        {
            standingPosition = default;
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            var probe = new Vector3(worldXZ.x, 0f, worldXZ.y);
            Vector3 candidate = walkableArea.Contains(probe, radius)
                ? probe
                : walkableArea.ClosestPoint(probe, radius);
            var landing = new Vector2(candidate.x, candidate.z);

            // ClosestPoint answers with the plan's own spawn when nothing
            // takes the point at this radius, and its plateau answer steps
            // in from an edge by an approximation that a corner can leave
            // just outside. The answer is re-tested at the FULL radius the
            // motor itself enforces rather than trusted: a square whose only
            // access is a hair outside the mask is not a destination, and
            // its neighbour usually is.
            if (!walkableArea.Contains(
                    new Vector3(landing.x, 0f, landing.y),
                    radius))
            {
                return false;
            }

            standingPosition = new Vector3(
                landing.x,
                ResolveSurfaceTop(landing) +
                PlayerFactory.GroundedRootOffset,
                landing.y);
            return true;
        }

        /// <summary>
        /// Unlike the city, the height is always re-derived. A mountain
        /// landmark is authored at whatever height suited the prop - a cafe
        /// sign, a cableway cable - and its own Y is no promise about the
        /// apron underneath it.
        /// </summary>
        public bool TryClampArrival(Vector3 arrival, out Vector3 destination)
        {
            return TryResolveStandingPosition(
                new Vector2(arrival.x, arrival.z),
                out destination);
        }

        private float ResolveSurfaceTop(Vector2 worldXZ)
        {
            if (plan.Plateau.Contains(worldXZ))
            {
                return plan.Plateau.Center.y;
            }

            MountainRoadTunnelDescriptor tunnel = plan.Tunnel;
            Vector2 portal = new Vector2(
                tunnel.PortalGroundCenter.x,
                tunnel.PortalGroundCenter.z);
            Vector2 axis = new Vector2(
                tunnel.OutwardAxis.x,
                tunnel.OutwardAxis.z).normalized;
            var right = new Vector2(axis.y, -axis.x);
            Vector2 delta = worldXZ - portal;
            float along = Vector2.Dot(delta, axis);
            if (along <= Epsilon &&
                along >= -tunnel.VisualDepth - Epsilon &&
                Mathf.Abs(Vector2.Dot(delta, right)) <=
                tunnel.OpeningWidth * 0.5f + Epsilon)
            {
                return tunnel.PortalGroundCenter.y;
            }

            return ResolveRouteTop(worldXZ);
        }

        private float ResolveRouteTop(Vector2 worldXZ)
        {
            IReadOnlyList<MountainRoadRouteSample> samples =
                plan.Route.Samples;
            float bestDistance = float.PositiveInfinity;
            float bestTop = samples[0].Position.y;
            for (int index = 1; index < samples.Count; index++)
            {
                MountainRoadRouteSample first = samples[index - 1];
                MountainRoadRouteSample second = samples[index];
                var a = new Vector2(first.Position.x, first.Position.z);
                var b = new Vector2(second.Position.x, second.Position.z);
                Vector2 ab = b - a;
                float denominator = ab.sqrMagnitude;
                float t = denominator <= Epsilon
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(worldXZ - a, ab) / denominator);
                float distance =
                    (Vector2.Lerp(a, b, t) - worldXZ).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestTop = Mathf.Lerp(
                    first.Position.y,
                    second.Position.y,
                    t);
            }

            return bestTop;
        }
    }

    /// <summary>
    /// The village's teleport ground.
    ///
    /// Simpler than the mountain's, because the village has one shared height
    /// contract: <c>AlpineVillageTerrainSampler.SampleHeight</c> answers for
    /// the lane, the shelves and the slope between them, and it is the same
    /// function the ground mesh was built from. So a landed square stands on
    /// exactly the surface the player can see, with no per-feature special
    /// case to keep in step.
    /// </summary>
    public sealed class CityMapAlpineVillageTeleportGround
        : ICityMapTeleportGround
    {
        private readonly AlpineVillagePlan plan;
        private readonly AlpineVillageWalkableArea walkableArea;

        public CityMapAlpineVillageTeleportGround(AlpineVillagePlan plan)
            : this(new AlpineVillageWalkableArea(plan))
        {
        }

        public CityMapAlpineVillageTeleportGround(
            AlpineVillageWalkableArea area)
        {
            walkableArea = area ??
                           throw new ArgumentNullException(nameof(area));
            plan = area.Plan;
        }

        public GameAreaId Area => GameAreaId.AlpineVillage;

        public bool TryResolveStandingPosition(
            Vector2 worldXZ,
            out Vector3 standingPosition)
        {
            standingPosition = default;
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            var probe = new Vector3(worldXZ.x, 0f, worldXZ.y);
            Vector3 candidate = walkableArea.Contains(probe, radius)
                ? probe
                : walkableArea.ClosestPoint(probe, radius);
            var landing = new Vector2(candidate.x, candidate.z);

            // Re-tested at the FULL radius the motor enforces rather than
            // trusted: a square whose only access is a hair outside the mask
            // is not a destination, and its neighbour usually is.
            if (!walkableArea.Contains(
                    new Vector3(landing.x, 0f, landing.y),
                    radius))
            {
                return false;
            }

            standingPosition = new Vector3(
                landing.x,
                AlpineVillageTerrainSampler.SampleHeight(plan, landing) +
                PlayerFactory.GroundedRootOffset,
                landing.y);
            return true;
        }

        /// <summary>
        /// The height is always re-derived, never taken from the arrival. A
        /// village point is authored at whatever height suited the thing it
        /// names - a door threshold, a cable centre - and that is no promise
        /// about the ground under it.
        /// </summary>
        public bool TryClampArrival(Vector3 arrival, out Vector3 destination)
        {
            return TryResolveStandingPosition(
                new Vector2(arrival.x, arrival.z),
                out destination);
        }
    }
}
