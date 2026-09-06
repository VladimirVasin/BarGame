using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Traces the spring's water from the rock it leaves to the cut it
    /// disappears down.
    ///
    /// TWO THINGS DECIDE THE WHOLE SHAPE OF THIS, and neither is obvious.
    ///
    /// The first is that STEEPEST DESCENT DOES NOT LEAD TO THE STATION. The
    /// macro ground is `along * grade + lateral * grade * 0.1`, so the true
    /// fall line runs about `(-0.43, -0.90)` - west of the lane, not down it.
    /// From the spring that meets the western edge of the inhabited extent in
    /// some `36 m`, where the `74 deg` wall starts. So the brook does what a
    /// real one does: it runs into the wall, turns, and follows the toe to the
    /// only breach there is - the cableway cut. The water and the cabin leave
    /// the bowl the same way, which is the whole composition: the player has
    /// already been told, by riding it, that down there is where the city is.
    ///
    /// The second is that THE UNDULATION IS AS STEEP AS THE SLOPE. The two
    /// sine terms reach `0.071 m` of fall per metre against the macro plane's
    /// `0.078`, so a naive steepest-descent walk sits down in the first dimple
    /// it finds and never leaves. A stream does not: it fills the dimple and
    /// spills on. That is why the trace carries momentum and a steering bias,
    /// and why the WATER SURFACE is a running minimum computed afterwards
    /// rather than the ground the walk happened to touch.
    /// </summary>
    public static class AlpineVillageBrookPlanner
    {
        /// <summary>
        /// Spacing of the traced cross-sections. Half the terrain cell: fine
        /// enough that a meander reads as a curve, coarse enough that the
        /// whole brook is a couple of hundred triangles of water.
        /// </summary>
        public const float SampleStep = 1f;

        public const int MaximumSamples = 220;

        /// <summary>Where the ledge stands relative to the plot centre, as a
        /// fraction of the plot's depth. Behind the bowl, against the hill.
        /// </summary>
        public const float LedgeSetback = 0.34f;

        /// <summary>
        /// The outcrop's size, owned here rather than by the builder.
        ///
        /// It has to be a plan number because the CATCH IS PLACED AGAINST IT:
        /// when the builder held these privately the two were positioned from
        /// the plot independently and grew into each other - the second
        /// capture of the spring showed the rock sitting through the basin.
        /// </summary>
        public static readonly Vector3 LedgeSize =
            new Vector3(3.4f, 1.45f, 2.1f);

        /// <summary>Rock to water. Small: the catch is tucked under the brow,
        /// which is what puts the seeps over it.</summary>
        public const float LedgeToBowlGap = 0.12f;

        public const float BowlWidth = 2.6f;
        public const float BowlDepth = 1.9f;

        /// <summary>
        /// The existing channel's trace datum. The catch now stands above
        /// the uncut ground; retaining this datum preserves every downstream
        /// sample while only its first section meets the actual overflow.
        /// </summary>
        public const float BowlWaterDrop = 0.14f;

        public const float BowlWaterTopOffset = 0.10f;

        public const float RunnelWidth = 0.72f;
        public const float PoolWidth = 1.9f;

        /// <summary>Width the brook has reached by the time it leaves.
        /// </summary>
        public const float OutfallWidth = 1.45f;

        /// <summary>How far the stone bed sits under the water surface.
        /// </summary>
        public const float BedDepth = 0.11f;

        /// <summary>
        /// The least the water surface may fall between two samples. It is
        /// what makes "the brook only ever descends" true BY CONSTRUCTION
        /// rather than by hoping the ground cooperates, and the validator
        /// then only has to confirm it.
        /// </summary>
        public const float MinimumFallPerSample = 0.004f;

        /// <summary>
        /// A fall bigger than this over one sample is a cascade, and gets a
        /// stone lip and a plunge instead of a slope.
        /// </summary>
        public const float CascadeThreshold = 0.10f;

        /// <summary>
        /// THE HERO MUST ALWAYS BE ABLE TO STEP OUT OF THE CHANNEL.
        ///
        /// `PlayerFactory` gives the controller a `0.28 m` step offset, so a
        /// cascade taller than that turns the brook into a trench a player who
        /// walked into it cannot leave - and the art bible's §10g is explicit
        /// that nothing here stops the player except things he can see. Every
        /// drop is capped under the step offset; a run of small steps reads as
        /// a cascade far better than one the hero can fall into anyway.
        /// </summary>
        public const float MaximumCascadeDrop = 0.25f;

        /// <summary>How much of the channel's depth the terrain gives up.
        /// </summary>
        public const float MaximumChannelCut = 0.55f;

        private const float MomentumWeight = 1.35f;
        private const float DescentWeight = 1f;
        private const float SteerWeightAtEnd = 4f;
        private const float MeanderDegrees = 17f;
        private const float MeanderRate = 0.09f;
        private const float GradientProbe = 0.75f;

        /// <summary>
        /// How far out a plot starts pushing the water away. It has to be
        /// several samples wide, not one: the trace carries momentum, and a
        /// wall it only learns about a metre ahead is a wall it walks into.
        /// At `1.15 m` the brook clipped the corner of `village-house-05`.
        /// </summary>
        private const float PlotClearance = 4.2f;

        private const float PlotPushWeight = 3.4f;

        /// <summary>
        /// How far inside the inhabited extent the channel's centreline has
        /// to stay.
        ///
        /// The brook runs to the western wall and turns down it, which is
        /// correct - but it went on to hug it. At `67 m` a step of `1.5 m`
        /// sideways off the centreline was already `1.49 m` UP the `74 deg`
        /// face, so the far bank was a cliff and the hero could not have
        /// climbed out on that side. The rise begins
        /// <see cref="AlpineVillageTerrainSampler.RidgeStandoff"/> outside
        /// the extent, and the far bank is judged a swale-and-a-metre out, so
        /// the centreline owes the difference plus a margin.
        /// </summary>
        public static readonly float RidgeKeepInside =
            AlpineVillageTerrainSampler.BrookSwaleHalfWidth + 1f -
            AlpineVillageTerrainSampler.RidgeStandoff + 1.2f;

        private const float WallFeelDistance = 9f;
        private const float WallPushWeight = 3f;

        /// <summary>
        /// Where the wet contour ends: short of the chapel's stonework, so
        /// the ground arrives at the basin rather than clipping through it.
        /// </summary>
        public const float ChapelBasinStandoff = 0.55f;

        public const float SeepLineHalfWidth = 1.15f;
        public const int SeepLineSampleCount = 18;
        public const int LedgeSeepCount = 4;

        public const string SpringPlotStableId = "village-spring";
        public const string ChapelPlotStableId = "village-chapel";

        /// <summary>
        /// Builds the brook for a village plan whose ground is still
        /// brook-free. The plan must not have been handed its brook yet:
        /// the trace reads <c>AlpineVillageTerrainSampler.SampleHeight</c>,
        /// and that returns the un-dished ground exactly while
        /// <c>plan.Brook</c> is null. Attaching it is the planner's next
        /// statement, and after that this would be tracing its own swale.
        /// </summary>
        public static AlpineVillageBrookPlan Create(AlpineVillagePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Brook != null)
            {
                throw new InvalidOperationException(
                    "The brook is traced on the ground before it cuts it; " +
                    "this plan already carries one.");
            }

            AlpineVillagePlotDescriptor spring = FindPlot(
                plan,
                SpringPlotStableId);
            AlpineVillagePlotDescriptor chapel = FindPlot(
                plan,
                ChapelPlotStableId);

            // The ledge stands at the back of the plot, against the hill, and
            // its face looks the way the plot does. "Water comes out of the
            // slope behind the houses" is the art bible's own wording.
            Vector3 outward = -spring.Facing;
            Vector3 ledgeCenter = spring.GroundCenter +
                outward * (spring.FootprintSize.y * LedgeSetback);
            Ground(plan, ref ledgeCenter);

            // In front of the ledge's face, not at a fraction of the plot:
            // the two are one composition and have to be measured off each
            // other, or the rock grows through the basin.
            Vector3 bowlCenter = ledgeCenter +
                spring.Facing *
                (LedgeSize.z * 0.5f + BowlDepth * 0.5f + LedgeToBowlGap);
            Ground(plan, ref bowlCenter);
            float bowlWaterTopY = bowlCenter.y + BowlWaterTopOffset;

            List<AlpineVillageBrookSeep> seeps = CreateSeeps(
                ledgeCenter,
                spring.Facing,
                bowlCenter,
                bowlWaterTopY);

            // The rim spills on its downhill side, which is the side the fall
            // line leaves by - a bowl that overflowed uphill would be the one
            // thing nobody could unsee.
            Vector2 fall = FallLine(plan, ToXZ(bowlCenter));
            Vector3 traceLip = bowlCenter +
                new Vector3(fall.x, 0f, fall.y) * (BowlDepth * 0.5f);
            traceLip.y = bowlCenter.y - BowlWaterDrop;

            Vector3 catchRight = Vector3.Cross(Vector3.up, spring.Facing).normalized;
            float outletSide = Vector2.Dot(fall, ToXZ(catchRight)) < 0f ? -1f : 1f;
            Vector3 bowlFacing = spring.Facing * outletSide;
            Vector3 outletDirection = catchRight * outletSide;
            Vector3 overflowLip = bowlCenter + outletDirection *
                (BowlWidth * 1.12f * 0.5f + 0.04f);
            overflowLip.y = bowlWaterTopY;

            Vector3 outfallTarget = ResolveOutfallTarget(plan, bowlCenter);

            List<AlpineVillageBrookSample> samples = Trace(
                plan,
                traceLip,
                outfallTarget,
                out List<AlpineVillageBrookCascade> cascades);

            ConnectOverflow(samples, cascades, overflowLip, -bowlFacing,
                BowlDepth * 1.12f * 0.28f - 0.04f);

            List<AlpineVillageBrookSample> seepLine = CreateSeepLine(
                plan,
                bowlCenter,
                chapel,
                out Vector3 chapelBasinPoint);

            return new AlpineVillageBrookPlan(
                samples,
                seepLine,
                seeps,
                cascades,
                ledgeCenter,
                spring.Facing,
                bowlFacing,
                bowlCenter,
                bowlWaterTopY,
                new Vector2(BowlWidth, BowlDepth),
                overflowLip,
                chapelBasinPoint,
                samples.Count > 0
                    ? samples[samples.Count - 1].Position
                    : overflowLip);
        }

        /// <summary>The existing downstream bed stays exactly where it was.
        /// Only the first cross-section moves from an arbitrary point inside
        /// the closed rim to the authored notch's outer edge.</summary>
        private static void ConnectOverflow(
            List<AlpineVillageBrookSample> samples,
            List<AlpineVillageBrookCascade> cascades,
            Vector3 lip,
            Vector3 right,
            float width)
        {
            if (samples.Count < 2)
            {
                throw new InvalidOperationException("The catch needs a downstream channel.");
            }

            float distanceChange = Vector2.Distance(ToXZ(lip), ToXZ(samples[1].Position)) -
                                   samples[1].Distance;
            samples[0] = new AlpineVillageBrookSample(0f, lip, right,
                width, BedDepth, AlpineVillageBrookReachKind.Runnel);
            for (int index = 1; index < samples.Count; index++)
            {
                AlpineVillageBrookSample sample = samples[index];
                samples[index] = new AlpineVillageBrookSample(
                    sample.Distance + distanceChange, sample.Position, sample.Right,
                    sample.Width, sample.BedDepth, sample.Reach);
            }

            for (int index = 0; index < cascades.Count; index++)
            {
                AlpineVillageBrookCascade cascade = cascades[index];
                cascades[index] = new AlpineVillageBrookCascade(cascade.StableId,
                    cascade.Distance + distanceChange, cascade.Lip, cascade.Forward,
                    cascade.Drop, cascade.Width);
            }
        }

        /// <summary>
        /// The point the brook is steered at: inside the cableway cut, far
        /// enough along that the ground there is genuinely falling away, and
        /// off the rope's own line so the water is not running under the
        /// cabin.
        /// </summary>
        private static Vector3 ResolveOutfallTarget(
            AlpineVillagePlan plan,
            Vector3 bowlCenter)
        {
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector3 forward = cableway.LineForward;
            Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;

            // Matches the cut's own entrance in the terrain sampler; a target
            // short of it would steer the brook at ground that has not
            // started to drop yet.
            float cutStart = cableway.StationArea.Size.y * 0.5f +
                             AlpineVillageTerrainSampler.StationApron -
                             AlpineVillageTerrainSampler.TerrainCell * 0.5f;

            // ON THE SIDE THE WATER IS ALREADY ON.
            //
            // Aiming at the far side of the rope made the brook cross the
            // cableway line and turn back north to reach it - a U-turn in the
            // last twenty metres, which is the one shape moving water never
            // makes. The side is taken from where the spring actually is, not
            // from a sign someone has to get right.
            float side = Mathf.Sign(
                Vector3.Dot(bowlCenter - cableway.StationArea.Center, across));
            if (Mathf.Approximately(side, 0f))
            {
                side = 1f;
            }

            // Inside the cut's core, where the ground is fully lowered,
            // rather than out on the blend where it is not.
            float lateral = side * AlpineVillageTerrainSampler
                .CablewayCutCoreHalfWidth * 0.86f;
            Vector3 target = cableway.StationArea.Center +
                forward * (cutStart + OutfallCutReach) +
                across * lateral;
            Ground(plan, ref target);
            return target;
        }

        private const float OutfallCutReach = 5f;

        private static List<AlpineVillageBrookSample> Trace(
            AlpineVillagePlan plan,
            Vector3 lip,
            Vector3 outfallTarget,
            out List<AlpineVillageBrookCascade> cascades)
        {
            var points = new List<Vector2>();
            var ground = new List<float>();
            Vector2 position = ToXZ(lip);
            Vector2 target = ToXZ(outfallTarget);
            Vector2 direction = FallLine(plan, position);

            points.Add(position);
            ground.Add(SampleGround(plan, position));

            for (int step = 1; step < MaximumSamples; step++)
            {
                float remaining = Vector2.Distance(position, target);
                if (remaining <= SampleStep)
                {
                    break;
                }

                // Steering grows with proximity, so the last stretch commits
                // to the cut instead of wandering along its rim.
                float steer = Mathf.Clamp01(1f - remaining / SteerRange);
                Vector2 toTarget = (target - position).normalized;
                Vector2 descent = FallLine(plan, position);

                float travelled = step * SampleStep;
                float meander = Mathf.Sin(travelled * MeanderRate) *
                                MeanderDegrees * Mathf.Deg2Rad;
                descent = Rotate(descent, meander);

                // Momentum is what keeps the line out of every dimple, and it
                // is also what made the approach orbit the outfall instead of
                // arriving at it. It gives way as the steering comes up.
                Vector2 blended =
                    direction * (MomentumWeight * (1f - 0.7f * steer)) +
                    descent * DescentWeight +
                    toTarget * (SteerWeightAtEnd * steer);
                blended += PlotPush(plan, position) * PlotPushWeight;
                blended += StationPush(plan, position) * PlotPushWeight;
                blended += WallPush(plan, position) * WallPushWeight;
                if (blended.sqrMagnitude <= 0.000001f)
                {
                    blended = toTarget;
                }

                direction = blended.normalized;
                position += direction * SampleStep;
                position = ClampInsideBowl(plan, position);
                points.Add(position);
                ground.Add(SampleGround(plan, position));
            }

            target = ClampInsideBowl(plan, target);
            points.Add(target);
            ground.Add(SampleGround(plan, target));

            return Resolve(points, ground, lip.y, out cascades);
        }

        private const float SteerRange = 42f;

        /// <summary>
        /// Turns a traced ground line into water.
        ///
        /// The surface is a RUNNING MINIMUM: it starts at the bowl's lip and
        /// may never rise, so every dimple the walk crossed becomes a pool
        /// held by its own downstream ground rather than a place the brook
        /// flows uphill out of. Where the fall over one sample is bigger than
        /// a cascade's threshold it is taken as a step - capped, because a
        /// drop taller than the hero's step offset is a trap.
        /// </summary>
        private static List<AlpineVillageBrookSample> Resolve(
            IReadOnlyList<Vector2> points,
            IReadOnlyList<float> ground,
            float lipY,
            out List<AlpineVillageBrookCascade> cascades)
        {
            var samples = new List<AlpineVillageBrookSample>(points.Count);
            cascades = new List<AlpineVillageBrookCascade>();
            float surface = lipY;
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
                float ceiling = surface - MinimumFallPerSample;
                float next = Mathf.Min(ceiling, natural);

                // The terrain gives up only so much: past that the water
                // would be running in a slot nobody dug.
                float floor = ground[index] - MaximumChannelCut;
                next = Mathf.Max(next, Mathf.Min(floor, ceiling));

                float drop = surface - next;
                if (index > 0 && drop > CascadeThreshold)
                {
                    float capped = Mathf.Min(drop, MaximumCascadeDrop);
                    next = surface - capped;
                    Vector2 forward2 = (points[index] -
                                        points[index - 1]).normalized;
                    var lip = new Vector3(
                        points[index - 1].x,
                        surface,
                        points[index - 1].y);
                    cascades.Add(new AlpineVillageBrookCascade(
                        $"village-brook-cascade-{cascades.Count:00}",
                        travelled,
                        lip,
                        new Vector3(forward2.x, 0f, forward2.y),
                        capped,
                        WidthAt(index, points.Count)));
                }

                surface = next;

                Vector2 forward = ResolveForward(points, index);
                var right = new Vector3(forward.y, 0f, -forward.x);
                float width = WidthAt(index, points.Count);
                samples.Add(new AlpineVillageBrookSample(
                    travelled,
                    new Vector3(points[index].x, surface, points[index].y),
                    right,
                    width,
                    BedDepth,
                    ResolveReach(index, points.Count)));
            }

            return samples;
        }

        private static float WidthAt(int index, int count)
        {
            float amount = count <= 1
                ? 0f
                : index / (float)(count - 1);
            // Narrow at the head, wider as side seepage joins it - the one
            // thing the source plan asked for that costs nothing here.
            float irregularity = 0.90f +
                Mathf.Sin(index * 0.67f + 0.4f) * 0.08f +
                Mathf.Sin(index * 1.41f + 2f) * 0.05f;
            return Mathf.Lerp(RunnelWidth, OutfallWidth, amount * amount) *
                irregularity;
        }

        private static AlpineVillageBrookReachKind ResolveReach(
            int index,
            int count)
        {
            if (index == 0)
            {
                return AlpineVillageBrookReachKind.Runnel;
            }

            if (index >= count - 3)
            {
                return AlpineVillageBrookReachKind.Outfall;
            }

            return index % 7 == 0
                ? AlpineVillageBrookReachKind.Pool
                : AlpineVillageBrookReachKind.Runnel;
        }

        private static Vector2 ResolveForward(
            IReadOnlyList<Vector2> points,
            int index)
        {
            if (points.Count < 2)
            {
                return Vector2.up;
            }

            int first = Mathf.Max(0, index - 1);
            int second = Mathf.Min(points.Count - 1, index + 1);
            Vector2 delta = points[second] - points[first];
            return delta.sqrMagnitude <= 0.000001f
                ? Vector2.up
                : delta.normalized;
        }

        /// <summary>
        /// The wet contour to the chapel's basin.
        ///
        /// The two stone catches sit `52 m` apart with `0.31 m` between them:
        /// `0.59 %`, which is not a stream's gradient, it is a CONTOUR. They
        /// are level with each other because a spring line is level - it is
        /// where the water table meets the hillside. So the link between them
        /// is shown as ground that never dries and nothing else. An aqueduct
        /// would read the village's own art bible wrong: "мокрая земля,
        /// каменная приёмная чаша и ручеёк вниз, а не сооружение".
        /// </summary>
        private static List<AlpineVillageBrookSample> CreateSeepLine(
            AlpineVillagePlan plan,
            Vector3 bowlCenter,
            AlpineVillagePlotDescriptor chapel,
            out Vector3 chapelBasinPoint)
        {
            Vector3 basin = AlpineVillagePathPlanner
                .GetChapelSourceBowlPosition(plan, chapel);
            Vector3 toBasin = basin - bowlCenter;
            toBasin.y = 0f;
            Vector3 approach = toBasin.normalized;
            chapelBasinPoint = basin - approach * ChapelBasinStandoff;
            Ground(plan, ref chapelBasinPoint);

            var samples = new List<AlpineVillageBrookSample>(
                SeepLineSampleCount);
            Vector3 start = bowlCenter;
            float travelled = 0f;
            Vector3 previous = start;

            for (int index = 0; index < SeepLineSampleCount; index++)
            {
                float amount = index / (float)(SeepLineSampleCount - 1);
                Vector3 point = Vector3.Lerp(start, chapelBasinPoint, amount);

                // A contour is not a ruled line: it wanders with the ground.
                Vector3 across = Vector3.Cross(Vector3.up, approach)
                    .normalized;
                point += across *
                    (Mathf.Sin(amount * 6.1f) * 1.35f +
                     Mathf.Sin(amount * 13.7f) * 0.5f);
                Ground(plan, ref point);
                travelled += Vector3.Distance(
                    new Vector3(previous.x, 0f, previous.z),
                    new Vector3(point.x, 0f, point.z));
                previous = point;

                var right = new Vector3(approach.z, 0f, -approach.x);
                samples.Add(new AlpineVillageBrookSample(
                    travelled,
                    point,
                    right,
                    SeepLineHalfWidth * 2f *
                        Mathf.Lerp(1.15f, 0.75f, amount),
                    0f,
                    AlpineVillageBrookReachKind.SeepLine));
            }

            return samples;
        }

        private static List<AlpineVillageBrookSeep> CreateSeeps(
            Vector3 ledgeCenter,
            Vector3 facing,
            Vector3 bowlCenter,
            float bowlWaterTopY)
        {
            var seeps = new List<AlpineVillageBrookSeep>(LedgeSeepCount);
            Vector3 across = Vector3.Cross(Vector3.up, facing).normalized;

            // Four mouths at four heights across the face. One tidy hole is
            // a pipe; several at different heights is a hillside.
            float[] offsets = { -0.82f, -0.24f, 0.31f, 0.79f };
            float[] lifts = { 0.46f, 0.72f, 0.29f, 0.58f };
            float[] widths = { 0.13f, 0.19f, 0.10f, 0.15f };

            for (int index = 0; index < offsets.Length; index++)
            {
                // Just PAST the ledge's face, so the column falls clear of
                // the rock and lands in the catch rather than down the
                // outcrop's own front.
                Vector3 mouth = ledgeCenter +
                    across * offsets[index] +
                    facing * (LedgeSize.z * 0.5f + LedgeToBowlGap * 0.5f) +
                    Vector3.up * lifts[index];
                Vector3 landing = bowlCenter + across * offsets[index] -
                    facing * (BowlDepth * 1.12f * 0.32f - 0.20f);
                landing.y = bowlWaterTopY;
                seeps.Add(new AlpineVillageBrookSeep(
                    $"village-spring-seep-{index:00}",
                    mouth,
                    facing,
                    landing,
                    Mathf.Max(0.05f, mouth.y - bowlWaterTopY),
                    widths[index]));
            }

            return seeps;
        }

        /// <summary>Downhill unit vector in XZ, from the real ground.
        /// </summary>
        private static Vector2 FallLine(AlpineVillagePlan plan, Vector2 point)
        {
            float east = SampleGround(
                plan,
                point + new Vector2(GradientProbe, 0f));
            float west = SampleGround(
                plan,
                point - new Vector2(GradientProbe, 0f));
            float north = SampleGround(
                plan,
                point + new Vector2(0f, GradientProbe));
            float south = SampleGround(
                plan,
                point - new Vector2(0f, GradientProbe));
            var gradient = new Vector2(east - west, north - south);
            return gradient.sqrMagnitude <= 0.000001f
                ? Vector2.down
                : -gradient.normalized;
        }

        /// <summary>
        /// Keeps the channel out of levelled plot ground. A brook running
        /// through a doorstep would be a defect nobody could argue with.
        /// </summary>
        private static Vector2 PlotPush(AlpineVillagePlan plan, Vector2 point)
        {
            var push = Vector2.zero;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind == AlpineVillagePlotKind.Spring)
                {
                    continue;
                }

                float outside = AlpineVillageTerrainSampler
                    .DistanceOutsidePlot(plot, point);
                if (outside >= PlotClearance)
                {
                    continue;
                }

                Vector2 away = point - ToXZ(plot.GroundCenter);
                if (away.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                // Squared, so distant plots barely lean on the line and a
                // near one turns it hard.
                float strength = 1f - Mathf.Clamp01(outside / PlotClearance);
                push += away.normalized * (strength * strength);
            }

            return push;
        }

        /// <summary>
        /// Keeps the water off the station pad. The brook has to pass it to
        /// reach the cut, and the pad is the one flat standing place in the
        /// village - the hero arrives on it.
        /// </summary>
        private static Vector2 StationPush(
            AlpineVillagePlan plan,
            Vector2 point)
        {
            float outside = AlpineVillageTerrainSampler
                .DistanceOutsideStation(plan.Station, point);
            if (outside >= StationClearance)
            {
                return Vector2.zero;
            }

            Vector2 away = point - new Vector2(
                plan.Station.PadArea.Center.x,
                plan.Station.PadArea.Center.z);
            if (away.sqrMagnitude <= 0.000001f)
            {
                return Vector2.zero;
            }

            float strength = 1f - Mathf.Clamp01(outside / StationClearance);
            return away.normalized * (strength * strength);
        }

        private const float StationClearance = 6.5f;

        /// <summary>
        /// Leans the line away from the enclosing wall before it reaches it.
        /// A brook at the very foot of a `74 deg` face is half inside the
        /// mountain and has one bank nobody can climb.
        /// </summary>
        private static Vector2 WallPush(AlpineVillagePlan plan, Vector2 point)
        {
            Rect keep = Inset(plan.TerrainBounds, RidgeKeepInside);
            var push = Vector2.zero;

            float west = point.x - keep.xMin;
            float east = keep.xMax - point.x;
            float south = point.y - keep.yMin;
            float north = keep.yMax - point.y;

            push.x += Lean(west) - Lean(east);
            push.y += Lean(south) - Lean(north);
            return push;
        }

        private static float Lean(float clearance)
        {
            if (clearance >= WallFeelDistance)
            {
                return 0f;
            }

            float amount = 1f - Mathf.Clamp01(clearance / WallFeelDistance);
            return amount * amount;
        }

        /// <summary>
        /// The backstop. The push is a lean, not a guarantee, and one sample
        /// that overshoots into the rise is one bank the hero cannot use.
        /// </summary>
        private static Vector2 ClampInsideBowl(
            AlpineVillagePlan plan,
            Vector2 point)
        {
            Rect keep = Inset(plan.TerrainBounds, RidgeKeepInside);
            return new Vector2(
                Mathf.Clamp(point.x, keep.xMin, keep.xMax),
                Mathf.Clamp(point.y, keep.yMin, keep.yMax));
        }

        private static Rect Inset(Rect value, float amount)
        {
            float width = Mathf.Max(1f, value.width - amount * 2f);
            float height = Mathf.Max(1f, value.height - amount * 2f);
            return new Rect(
                value.center.x - width * 0.5f,
                value.center.y - height * 0.5f,
                width,
                height);
        }

        private static float SampleGround(
            AlpineVillagePlan plan,
            Vector2 point)
        {
            return AlpineVillageTerrainSampler.SampleHeight(plan, point);
        }

        private static void Ground(AlpineVillagePlan plan, ref Vector3 point)
        {
            point.y = AlpineVillageTerrainSampler.SampleHeight(
                plan,
                new Vector2(point.x, point.z));
        }

        private static AlpineVillagePlotDescriptor FindPlot(
            AlpineVillagePlan plan,
            string stableId)
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (string.Equals(
                        plan.Plots[index].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    return plan.Plots[index];
                }
            }

            throw new InvalidOperationException(
                $"The village has no '{stableId}' plot to carry water.");
        }

        private static Vector2 Rotate(Vector2 value, float radians)
        {
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }
}
