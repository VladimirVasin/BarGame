using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The mountain water where the road meets it: a reach coming down the
    /// cut slope, the culvert it goes under, and the reach leaving on the
    /// other side toward the city.
    ///
    /// IT IS NOT THE WHOLE `620 m` OF ROUTE, and that is deliberate. What the
    /// player has to be told here is one sentence - "the water from up there
    /// crosses under this road and carries on down" - and a ribbon paralleling
    /// the entire climb would say it six times and cost six times as much.
    /// The culvert is the event; this is the water on either side of it.
    ///
    /// The culvert itself is not new. It has stood at a tenth of the route
    /// since the road was built, with a stone headwall, a dark cylinder for a
    /// bore, and a `CulvertWater` sound anchor beside it - A SOUND WITH
    /// NOTHING MAKING IT. This plan is what that sound has been describing.
    /// </summary>
    public sealed class MountainRoadBrookPlan
    {
        private readonly ReadOnlyCollection<MountainRoadBrookSample> inlet;
        private readonly ReadOnlyCollection<MountainRoadBrookSample> outlet;

        internal MountainRoadBrookPlan(
            IList<MountainRoadBrookSample> sourceInlet,
            IList<MountainRoadBrookSample> sourceOutlet,
            Vector3 inletMouth,
            Vector3 outletMouth,
            Vector3 bore,
            float boreRadius,
            string culvertStableId)
        {
            inlet = new ReadOnlyCollection<MountainRoadBrookSample>(
                new List<MountainRoadBrookSample>(sourceInlet));
            outlet = new ReadOnlyCollection<MountainRoadBrookSample>(
                new List<MountainRoadBrookSample>(sourceOutlet));
            InletMouth = inletMouth;
            OutletMouth = outletMouth;
            Bore = bore;
            BoreRadius = boreRadius;
            CulvertStableId = culvertStableId ?? string.Empty;
        }

        /// <summary>Water arriving at the road, uphill side.</summary>
        public IReadOnlyList<MountainRoadBrookSample> Inlet => inlet;

        /// <summary>Water leaving the bore and going on down.</summary>
        public IReadOnlyList<MountainRoadBrookSample> Outlet => outlet;

        public Vector3 InletMouth { get; }
        public Vector3 OutletMouth { get; }

        /// <summary>Where the visible bore pours from.</summary>
        public Vector3 Bore { get; }

        public float BoreRadius { get; }

        public string CulvertStableId { get; }
    }

    /// <summary>One cross-section of the road's water.</summary>
    public readonly struct MountainRoadBrookSample
    {
        internal MountainRoadBrookSample(
            float distance,
            Vector3 position,
            Vector3 right,
            float width,
            float bedDepth)
        {
            Distance = distance;
            Position = position;
            Right = right;
            Width = width;
            BedDepth = bedDepth;
        }

        public float Distance { get; }
        public Vector3 Position { get; }
        public Vector3 Right { get; }
        public float Width { get; }
        public float BedDepth { get; }
        public float HalfWidth => Width * 0.5f;
    }

    /// <summary>
    /// Traces the two short reaches either side of the road's culvert.
    ///
    /// The same shape as the village's tracer and for the same reasons -
    /// steepest descent carries momentum so it spills out of dimples rather
    /// than sitting in the first one, and the water surface is a running
    /// minimum afterwards so "it only ever descends" is true by construction.
    /// It is a separate function rather than a shared one because the two
    /// grounds are different contracts: this one reads the ROAD's sampler,
    /// which knows about the carriageway's bed and its plateau.
    /// </summary>
    public static class MountainRoadBrookPlanner
    {
        public const string CulvertStableId = "misc-culvert";

        public const float SampleStep = 1.2f;

        /// <summary>How far above the road the water is picked up.</summary>
        public const float InletReach = 34f;

        /// <summary>And how far below it is followed before the fog has it.
        /// </summary>
        public const float OutletReach = 30f;

        public const float InletWidth = 1.15f;
        public const float OutletWidth = 1.45f;
        public const float BedDepth = 0.10f;
        public const float MinimumFallPerSample = 0.005f;

        /// <summary>
        /// How far the channel's ends stand off the headwall, so the ribbon
        /// never runs through the stonework it arrives at.
        /// </summary>
        public const float HeadwallStandoff = 0.55f;

        private const float MomentumWeight = 1.2f;
        private const float DescentWeight = 1f;
        private const float GradientProbe = 1.1f;
        /// <summary>
        /// How far the channel is held off the carriageway.
        ///
        /// It has to be generous, because THE ROAD IS A TRENCH: the terrain
        /// sampler sinks the soil `RoadBedClearance` under the asphalt, so
        /// the fall line beside a road points AT it and a brook tracing
        /// steepest descent runs happily into the carriageway. The first
        /// trace did exactly that, nine samples below the culvert.
        /// </summary>
        private const float RoadClearance = 5.5f;

        private const float RoadPushWeight = 9f;
        private const float MaximumChannelCut = 0.45f;

        public static MountainRoadBrookPlan Create(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadMiscDescriptor culvert = FindCulvert(plan);

            // WHICH SIDE IS UPHILL IS MEASURED, NOT ASSUMED. The culvert is
            // authored on one side of the carriageway and the road's own
            // cross-fall differs left from right; taking a sign on faith here
            // would put the inlet below the outlet and run the water up
            // through the bore.
            MountainRoadRouteSample road = plan.Route.Sample(
                plan.Route.Length * CulvertRouteFraction);
            Vector3 across = road.Right;
            float leftGround = Ground(
                plan,
                ToXZ(road.Position - across * SideProbeDistance));
            float rightGround = Ground(
                plan,
                ToXZ(road.Position + across * SideProbeDistance));
            float uphillSide = leftGround >= rightGround ? -1f : 1f;

            Vector3 inletMouth = culvert.Position +
                across * (uphillSide * HeadwallStandoff);
            inletMouth.y = Ground(plan, ToXZ(inletMouth));

            // The other end of the culvert, which is the headwall mirrored
            // through the road's own centreline - not an invented offset.
            // A bore's two mouths are the same distance out on either side,
            // and that distance is what makes the crossing read as one
            // structure rather than two unrelated ends.
            float culvertLateral = Vector3.Dot(
                culvert.Position - road.Position,
                across);
            Vector3 outletMouth = road.Position - across * culvertLateral;
            outletMouth.y = Ground(plan, ToXZ(outletMouth));

            // The bore pours from the downhill face, a little over the ground
            // it lands on.
            Vector3 bore = outletMouth +
                across * (-uphillSide * 0.1f) +
                Vector3.up * BoreLift;

            List<MountainRoadBrookSample> inlet = Trace(
                plan,
                inletMouth,
                -across * uphillSide,
                InletReach,
                InletWidth,
                true);
            inlet.Reverse();
            Renumber(inlet);

            List<MountainRoadBrookSample> outlet = Trace(
                plan,
                outletMouth,
                -across * uphillSide,
                OutletReach,
                OutletWidth,
                false);

            return new MountainRoadBrookPlan(
                inlet,
                outlet,
                inletMouth,
                outletMouth,
                bore,
                BoreRadius,
                culvert.StableId);
        }

        /// <summary>Where the culvert stands, as the planner authored it.
        /// </summary>
        public const float CulvertRouteFraction = 0.10f;

        private const float SideProbeDistance = 7f;
        private const float BoreLift = 0.32f;
        private const float BoreRadius = 0.31f;

        /// <summary>
        /// Walks downhill from a start point. For the inlet the walk runs
        /// AWAY from the road and is reversed afterwards, because the reach
        /// has to arrive at a mouth whose position is already fixed by the
        /// stonework - tracing towards a target would wander off it.
        /// </summary>
        private static List<MountainRoadBrookSample> Trace(
            MountainRoadPlan plan,
            Vector3 start,
            Vector3 awayFromRoad,
            float reach,
            float width,
            bool uphillWalk)
        {
            var points = new List<Vector2>();
            var ground = new List<float>();
            Vector2 position = ToXZ(start);
            Vector2 direction = new Vector2(
                awayFromRoad.x,
                awayFromRoad.z).normalized;
            if (uphillWalk)
            {
                direction = -direction;
            }

            points.Add(position);
            ground.Add(Ground(plan, position));

            int steps = Mathf.Max(2, Mathf.RoundToInt(reach / SampleStep));
            for (int step = 1; step <= steps; step++)
            {
                Vector2 slope = FallLine(plan, position);
                if (uphillWalk)
                {
                    slope = -slope;
                }

                Vector2 blended = direction * MomentumWeight +
                                  slope * DescentWeight +
                                  RoadPush(plan, position) * RoadPushWeight;
                if (blended.sqrMagnitude <= 0.000001f)
                {
                    blended = direction;
                }

                direction = blended.normalized;
                position += direction * SampleStep;
                points.Add(position);
                ground.Add(Ground(plan, position));
            }

            return Resolve(points, ground, width, uphillWalk);
        }

        private static List<MountainRoadBrookSample> Resolve(
            IReadOnlyList<Vector2> points,
            IReadOnlyList<float> ground,
            float width,
            bool uphillWalk)
        {
            var samples = new List<MountainRoadBrookSample>(points.Count);

            // Walking uphill means the surface has to be a running MAXIMUM on
            // the way out and becomes a descent when the list is reversed.
            float surface = ground[0] - BedDepth * 0.5f;
            float travelled = 0f;
            for (int index = 0; index < points.Count; index++)
            {
                if (index > 0)
                {
                    travelled += Vector2.Distance(
                        points[index - 1],
                        points[index]);
                }

                float natural = ground[index] - BedDepth * 0.5f;
                if (index > 0)
                {
                    surface = uphillWalk
                        ? Mathf.Max(
                            surface + MinimumFallPerSample,
                            Mathf.Min(
                                natural,
                                surface + MaximumChannelCut))
                        : Mathf.Min(surface - MinimumFallPerSample, natural);
                    if (!uphillWalk)
                    {
                        surface = Mathf.Max(
                            surface,
                            Mathf.Min(
                                ground[index] - MaximumChannelCut,
                                surface));
                    }
                }
                else
                {
                    surface = natural;
                }

                Vector2 forward = ResolveForward(points, index);
                samples.Add(new MountainRoadBrookSample(
                    travelled,
                    new Vector3(points[index].x, surface, points[index].y),
                    new Vector3(forward.y, 0f, -forward.x),
                    width,
                    BedDepth));
            }

            return samples;
        }

        private static void Renumber(List<MountainRoadBrookSample> samples)
        {
            float travelled = 0f;
            for (int index = 0; index < samples.Count; index++)
            {
                if (index > 0)
                {
                    travelled += Vector3.Distance(
                        samples[index - 1].Position,
                        samples[index].Position);
                }

                MountainRoadBrookSample sample = samples[index];
                samples[index] = new MountainRoadBrookSample(
                    travelled,
                    sample.Position,
                    sample.Right,
                    sample.Width,
                    sample.BedDepth);
            }
        }

        private static Vector2 ResolveForward(
            IReadOnlyList<Vector2> points,
            int index)
        {
            int first = Mathf.Max(0, index - 1);
            int second = Mathf.Min(points.Count - 1, index + 1);
            Vector2 delta = points[second] - points[first];
            return delta.sqrMagnitude <= 0.000001f
                ? Vector2.up
                : delta.normalized;
        }

        private static Vector2 FallLine(MountainRoadPlan plan, Vector2 point)
        {
            float east = Ground(plan, point + new Vector2(GradientProbe, 0f));
            float west = Ground(plan, point - new Vector2(GradientProbe, 0f));
            float north = Ground(plan, point + new Vector2(0f, GradientProbe));
            float south = Ground(plan, point - new Vector2(0f, GradientProbe));
            var gradient = new Vector2(east - west, north - south);
            return gradient.sqrMagnitude <= 0.000001f
                ? Vector2.down
                : -gradient.normalized;
        }

        /// <summary>
        /// Keeps the channel off the carriageway. A brook across the road is
        /// a ford, and this road has none.
        /// </summary>
        private static Vector2 RoadPush(MountainRoadPlan plan, Vector2 point)
        {
            MountainRoadRouteSample nearest = NearestRoad(plan, point);
            Vector2 delta = point - ToXZ(nearest.Position);
            float distance = delta.magnitude;
            float keep = nearest.Width * 0.5f + RoadClearance;
            if (distance >= keep || distance <= 0.0001f)
            {
                return Vector2.zero;
            }

            return delta.normalized * (1f - distance / keep);
        }

        private static MountainRoadRouteSample NearestRoad(
            MountainRoadPlan plan,
            Vector2 point)
        {
            MountainRoadRouteSample best = plan.Route.Samples[0];
            float bestSquared = float.MaxValue;
            for (int index = 0; index < plan.Route.Samples.Count; index++)
            {
                MountainRoadRouteSample sample = plan.Route.Samples[index];
                float squared = (ToXZ(sample.Position) - point).sqrMagnitude;
                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    best = sample;
                }
            }

            return best;
        }

        private static float Ground(MountainRoadPlan plan, Vector2 point)
        {
            return MountainRoadTerrainSampler.SampleHeight(
                plan.Route,
                plan.Plateau,
                point);
        }

        private static MountainRoadMiscDescriptor FindCulvert(
            MountainRoadPlan plan)
        {
            for (int index = 0; index < plan.Misc.Count; index++)
            {
                if (string.Equals(
                        plan.Misc[index].StableId,
                        CulvertStableId,
                        StringComparison.Ordinal))
                {
                    return plan.Misc[index];
                }
            }

            throw new InvalidOperationException(
                "The mountain road has no culvert for its water to cross by.");
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }
}
