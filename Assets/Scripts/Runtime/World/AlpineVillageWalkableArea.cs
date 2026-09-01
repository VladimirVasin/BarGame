using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The whole inhabited bowl, minus the things that actually stand in it.
    ///
    /// IT USED TO BE THE OPPOSITE, and that is the defect this replaces. The
    /// mask was a capsule chain over the lane centreline plus one corridor per
    /// visible path: `2.38 m` of usable half-width on the street and `0.78 m`
    /// on a household branch, inside a bowl `93 x 125 m` across. Six per cent
    /// of the village was walkable and the other ninety-four were an invisible
    /// wall standing on ground the player can see, walk towards and never
    /// reach - the cemetery he could face but not enter, the snow between two
    /// houses, the ground behind the house at the top. Stepping off the path
    /// was impossible everywhere, which is exactly how it read.
    ///
    /// So the rule is inverted. Ground is walkable, and the only things that
    /// refuse it are things the eye already sees refusing it:
    ///
    /// - THE MOUNTAIN. The bowl's flat ends where
    ///   <see cref="AlpineVillageTerrainSampler.RidgeStandoff"/> ends and the
    ///   rise begins, and the rise is `74°` against the hero's `45°` slope
    ///   limit. The mask's outer boundary is drawn on that line and never
    ///   inside the flat, so the perimeter is held by the ground itself and
    ///   the mask is only agreeing with it.
    /// - EVERY BUILDING, at exactly the footprint its own `Physical Shell`
    ///   box collider stands on. The mask agrees with the collider rather than
    ///   leaving the work to it: contact is read back as achieved movement, so
    ///   a graze against a wall reads as a crawl, and sliding in the mask
    ///   keeps the hero's speed. The burial ground is the one plot with no
    ///   shell and is deliberately not here - a graveyard is ground.
    /// - THE CABLEWAY BRINK, the one hole in the bowl. The cut falls at
    ///   `7-28°`, well under the slope limit, so it is the only place a hero
    ///   could actually walk out of the village; the mask closes it at the
    ///   sampler's own entrance line and over the sampler's own width.
    ///
    /// The visible paths stay exactly what they were - the compacted routes
    /// between the places worth going. They are simply no longer the only
    /// ground a person is allowed to stand on.
    /// </summary>
    public sealed class AlpineVillageWalkableArea : IWalkableArea
    {
        /// <summary>
        /// How far the walkable flat continues past the inhabited extent.
        ///
        /// It is the sampler's own ridge standoff, and sharing it is the whole
        /// point: the mask ends on the exact line where the ground starts to
        /// climb, so nothing about the boundary is invisible. Past it the
        /// slope holds the hero without any help from here.
        /// </summary>
        public const float GroundOutset =
            AlpineVillageTerrainSampler.RidgeStandoff;

        /// <summary>
        /// How many times <see cref="ClosestPoint"/> may push a point out of
        /// an obstacle before giving up. Two neighbouring footprints can only
        /// trap a query in a corner; the caller re-tests the result and keeps
        /// the hero where he was rather than trusting it.
        /// </summary>
        private const int ObstacleResolutionPasses = 4;

        private const float BoundaryEpsilon = 0.001f;

        private readonly AlpineVillagePlan plan;
        private readonly Rect ground;
        private readonly List<OrientedRect> obstacles =
            new List<OrientedRect>();

        public AlpineVillageWalkableArea(AlpineVillagePlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            AlpineVillageValidator.ValidateOrThrow(plan);
            ground = BuildGround(plan);
            BuildBuildings();
            BuildCablewayBrink();
        }

        public AlpineVillagePlan Plan => plan;

        /// <summary>The walkable flat, before the obstacles are cut out of it.
        /// </summary>
        public Rect GroundBounds => ground;

        public bool Contains(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            return ContainsXZ(ToXZ(position), radius);
        }

        public Vector3 Constrain(
            Vector3 currentPosition,
            Vector3 desiredPosition,
            float radius = 0f)
        {
            ValidateRadius(radius);
            if (Contains(desiredPosition, radius))
            {
                return desiredPosition;
            }

            if (!IsFinite(desiredPosition))
            {
                return currentPosition;
            }

            Vector3 closest = ClosestPoint(desiredPosition, radius);
            if (Contains(closest, Mathf.Max(0f, radius - 0.001f)))
            {
                return closest;
            }

            // Sliding failed, so keep him where he was rather than teleporting
            // him across the village - the mountain road's fallback.
            return Contains(currentPosition, radius)
                ? new Vector3(
                    currentPosition.x,
                    desiredPosition.y,
                    currentPosition.z)
                : plan.SpawnPosition;
        }

        /// <summary>
        /// The nearest standable point. Clamped into the bowl first, then
        /// pushed out of whatever it landed inside along that obstacle's
        /// shallowest axis - which is what makes a run into a house wall a
        /// slide along it rather than a stop against it.
        /// </summary>
        public Vector3 ClosestPoint(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            if (!IsFinite(position))
            {
                return plan.SpawnPosition;
            }

            Vector2 point = ClampToGround(ToXZ(position), radius);
            for (int pass = 0; pass < ObstacleResolutionPasses; pass++)
            {
                int index = FindOverlapping(point, radius);
                if (index < 0)
                {
                    break;
                }

                point = ClampToGround(
                    obstacles[index].PushOut(point, radius),
                    radius);
            }

            return new Vector3(point.x, position.y, point.y);
        }

        private bool ContainsXZ(Vector2 point, float radius)
        {
            if (point.x < ground.xMin + radius ||
                point.x > ground.xMax - radius ||
                point.y < ground.yMin + radius ||
                point.y > ground.yMax - radius)
            {
                return false;
            }

            return FindOverlapping(point, radius) < 0;
        }

        private int FindOverlapping(Vector2 point, float radius)
        {
            for (int index = 0; index < obstacles.Count; index++)
            {
                if (obstacles[index].Overlaps(point, radius))
                {
                    return index;
                }
            }

            return -1;
        }

        private Vector2 ClampToGround(Vector2 point, float radius)
        {
            return new Vector2(
                ClampAxis(point.x, ground.xMin, ground.xMax, radius),
                ClampAxis(point.y, ground.yMin, ground.yMax, radius));
        }

        private static float ClampAxis(
            float value,
            float min,
            float max,
            float radius)
        {
            float low = min + radius;
            float high = max - radius;
            return low >= high
                ? (min + max) * 0.5f
                : Mathf.Clamp(value, low, high);
        }

        /// <summary>
        /// The bowl itself: the inhabited extent, carried out to the toe of
        /// the enclosing rise. Nothing narrower would be honest - the flat
        /// between the last house and the mountain is ground a person can see
        /// and walk on.
        /// </summary>
        private static Rect BuildGround(AlpineVillagePlan plan)
        {
            Rect bounds = plan.TerrainBounds;
            return Rect.MinMaxRect(
                bounds.xMin - GroundOutset,
                bounds.yMin - GroundOutset,
                bounds.xMax + GroundOutset,
                bounds.yMax + GroundOutset);
        }

        /// <summary>
        /// One obstacle per solid plot, on the plot's own rotated footprint -
        /// the same rectangle <c>AlpineVillageWorldBuilder</c> gives its
        /// `Physical Shell` collider, so the mask and the physics cannot
        /// disagree about where a wall is.
        /// </summary>
        private void BuildBuildings()
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind == AlpineVillagePlotKind.Spring)
                {
                    // The spring is the one plot that is GROUND. It carries
                    // no shell - only its stone catch has a collider - and
                    // walking up to the water is the entire reason it is
                    // there. The burial ground held this seat before the
                    // village lost it.
                    continue;
                }

                Vector2 facing = ToXZ(plot.Facing).normalized;
                Vector2 across = new Vector2(facing.y, -facing.x);
                obstacles.Add(new OrientedRect(
                    ToXZ(plot.GroundCenter),
                    across,
                    facing,
                    plot.FootprintSize * 0.5f));
            }
        }

        /// <summary>
        /// The one hole in the bowl, closed.
        ///
        /// The enclosing ridge is unclimbable and needs nothing from the mask,
        /// but the cableway cut is a gorge that FALLS - `7°` out of the
        /// station apron and never worse than `28°` for two hundred metres
        /// down the mountainside. A hero who may leave the lane may also walk
        /// straight down it and out of the scene, so this is the one boundary
        /// the mask has to hold by itself.
        ///
        /// Both numbers are the sampler's, read rather than re-derived: the
        /// rectangle starts on the cut's own entrance line and is exactly as
        /// wide as the ground the cut takes down. `along` is measured from the
        /// PAD CENTRE and the line's own length from the CABLE, which starts
        /// `1.9 m` further forward - the same two frames the sampler has to
        /// convert between.
        /// </summary>
        private void BuildCablewayBrink()
        {
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            float entrance = cableway.StationArea.Size.y * 0.5f +
                             AlpineVillageTerrainSampler.StationApron -
                             AlpineVillageTerrainSampler.TerrainCell * 0.5f;
            float cableOrigin = Vector3.Dot(
                cableway.LowerCableCenter - cableway.StationArea.Center,
                cableway.LineForward);
            float far = cableOrigin +
                        cableway.LineLength +
                        AlpineVillageTerrainSampler.RidgeCrestDepth;
            Vector3 center = cableway.StationArea.Center +
                             cableway.LineForward *
                             ((entrance + far) * 0.5f);
            obstacles.Add(new OrientedRect(
                ToXZ(center),
                ToXZ(cableway.LineRight),
                ToXZ(cableway.LineForward),
                new Vector2(
                    AlpineVillageTerrainSampler.CablewayCutOuterHalfWidth,
                    (far - entrance) * 0.5f)));
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static void ValidateRadius(float radius)
        {
            if (radius < 0f || float.IsNaN(radius) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private readonly struct OrientedRect
        {
            private readonly Vector2 center;
            private readonly Vector2 axisX;
            private readonly Vector2 axisY;
            private readonly Vector2 halfSize;

            internal OrientedRect(
                Vector2 center,
                Vector2 axisX,
                Vector2 axisY,
                Vector2 halfSize)
            {
                this.center = center;
                this.axisX = axisX.normalized;
                this.axisY = axisY.normalized;
                this.halfSize = halfSize;
            }

            /// <summary>
            /// Whether a body of this radius intersects the rectangle. The
            /// body is treated as a square rather than a circle, which is the
            /// conservative reading at a corner and the one the whole mask
            /// family uses.
            /// </summary>
            internal bool Overlaps(Vector2 point, float radius)
            {
                Vector2 local = ToLocal(point);
                return Mathf.Abs(local.x) < halfSize.x + radius &&
                       Mathf.Abs(local.y) < halfSize.y + radius;
            }

            /// <summary>
            /// Moves a point that is inside the expanded rectangle onto its
            /// nearest face. The shallowest axis is chosen, so a body walking
            /// at a long wall leaves along the wall rather than round the end
            /// of it.
            /// </summary>
            internal Vector2 PushOut(Vector2 point, float radius)
            {
                Vector2 local = ToLocal(point);
                float limitX = halfSize.x + radius + BoundaryEpsilon;
                float limitY = halfSize.y + radius + BoundaryEpsilon;
                if (limitX - Mathf.Abs(local.x) <=
                    limitY - Mathf.Abs(local.y))
                {
                    local.x = local.x < 0f ? -limitX : limitX;
                }
                else
                {
                    local.y = local.y < 0f ? -limitY : limitY;
                }

                return center + axisX * local.x + axisY * local.y;
            }

            private Vector2 ToLocal(Vector2 point)
            {
                Vector2 delta = point - center;
                return new Vector2(
                    Vector2.Dot(delta, axisX),
                    Vector2.Dot(delta, axisY));
            }
        }
    }
}
