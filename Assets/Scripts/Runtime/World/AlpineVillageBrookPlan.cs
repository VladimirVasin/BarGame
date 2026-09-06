using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What one stretch of the spring's water is doing. The reach decides the
    /// water material, the bed's width and whether a cascade stands at its
    /// head; it is not a rendering hint, because the flow direction the
    /// shader needs is a MATERIAL uniform and a material can only serve a
    /// reach, never a segment.
    /// </summary>
    public enum AlpineVillageBrookReachKind
    {
        /// <summary>The catch under the ledge. Still water.</summary>
        Bowl = 0,

        /// <summary>The narrow start below the bowl's overflow lip.</summary>
        Runnel = 1,

        /// <summary>A stone step the water falls over.</summary>
        Cascade = 2,

        /// <summary>Wider, slower water on the gentler stretches.</summary>
        Pool = 3,

        /// <summary>
        /// The band of ground that never dries, running along the contour to
        /// the chapel's basin. It carries no channel and no swale: it is wet
        /// earth, which is the only thing the art bible's §10g allows here.
        /// </summary>
        SeepLine = 4,

        /// <summary>The last stretch, falling away into the cableway cut.
        /// </summary>
        Outfall = 5
    }

    /// <summary>
    /// One cross-section of the brook. The builder sweeps a ribbon through
    /// these exactly as <c>BuildLane</c> sweeps the street skin, and for the
    /// same reason: two vertices cannot follow a curve.
    /// </summary>
    public readonly struct AlpineVillageBrookSample
    {
        internal AlpineVillageBrookSample(
            float distance,
            Vector3 position,
            Vector3 right,
            float width,
            float bedDepth,
            AlpineVillageBrookReachKind reach)
        {
            Distance = distance;
            Position = position;
            Right = right;
            Width = width;
            BedDepth = bedDepth;
            Reach = reach;
        }

        /// <summary>Metres travelled from the bowl's overflow lip.</summary>
        public float Distance { get; }

        /// <summary>Centre of the water surface, on its own ground.</summary>
        public Vector3 Position { get; }

        /// <summary>Horizontal normal to the flow, pointing right.</summary>
        public Vector3 Right { get; }

        public float Width { get; }

        /// <summary>How far the stone bed sits under the water surface.
        /// </summary>
        public float BedDepth { get; }

        public AlpineVillageBrookReachKind Reach { get; }

        public float HalfWidth => Width * 0.5f;
    }

    /// <summary>
    /// One place water reaches daylight. There are several, close together
    /// and at slightly different heights, because a spring that arrives
    /// through one tidy hole reads as a pipe.
    /// </summary>
    public readonly struct AlpineVillageBrookSeep
    {
        internal AlpineVillageBrookSeep(
            string stableId,
            Vector3 mouth,
            Vector3 outward,
            Vector3 landing,
            float fall,
            float width)
        {
            StableId = stableId ?? string.Empty;
            Mouth = mouth;
            Outward = outward;
            Landing = landing;
            Fall = fall;
            Width = width;
        }

        public string StableId { get; }

        /// <summary>Where the water leaves the rock.</summary>
        public Vector3 Mouth { get; }

        /// <summary>Horizontal direction it leaves in.</summary>
        public Vector3 Outward { get; }

        /// <summary>Where the seep meets the water inside the back rim.</summary>
        public Vector3 Landing { get; }

        /// <summary>Height of the falling column under the mouth.</summary>
        public float Fall { get; }

        public float Width { get; }
    }

    /// <summary>A stone lip the water drops over, with the plunge under it.
    /// </summary>
    public readonly struct AlpineVillageBrookCascade
    {
        internal AlpineVillageBrookCascade(
            string stableId,
            float distance,
            Vector3 lip,
            Vector3 forward,
            float drop,
            float width)
        {
            StableId = stableId ?? string.Empty;
            Distance = distance;
            Lip = lip;
            Forward = forward;
            Drop = drop;
            Width = width;
        }

        public string StableId { get; }
        public float Distance { get; }
        public Vector3 Lip { get; }
        public Vector3 Forward { get; }
        public float Drop { get; }
        public float Width { get; }
    }

    /// <summary>
    /// Where the spring's water is, from the rock it leaves to the cut it
    /// disappears down. Pure data: no GameObject, no material, no sound.
    ///
    /// The village had a spring plot, a stone catch and a running-water sound
    /// long before it had any water - <c>BuildSpring</c> drew the surface as
    /// two grey boxes textured as STONE and said so in its own comment. This
    /// plan is what those boxes were standing in for.
    /// </summary>
    public sealed class AlpineVillageBrookPlan
    {
        private readonly ReadOnlyCollection<AlpineVillageBrookSample> samples;
        private readonly ReadOnlyCollection<AlpineVillageBrookSample> seepLine;
        private readonly ReadOnlyCollection<AlpineVillageBrookSeep> seeps;
        private readonly ReadOnlyCollection<AlpineVillageBrookCascade>
            cascades;

        internal AlpineVillageBrookPlan(
            IList<AlpineVillageBrookSample> sourceSamples,
            IList<AlpineVillageBrookSample> sourceSeepLine,
            IList<AlpineVillageBrookSeep> sourceSeeps,
            IList<AlpineVillageBrookCascade> sourceCascades,
            Vector3 ledgeCenter,
            Vector3 ledgeFacing,
            Vector3 bowlFacing,
            Vector3 bowlCenter,
            float bowlWaterTopY,
            Vector2 bowlSize,
            Vector3 overflowLip,
            Vector3 chapelBasinPoint,
            Vector3 outfallPoint)
        {
            samples = new ReadOnlyCollection<AlpineVillageBrookSample>(
                new List<AlpineVillageBrookSample>(sourceSamples));
            seepLine = new ReadOnlyCollection<AlpineVillageBrookSample>(
                new List<AlpineVillageBrookSample>(sourceSeepLine));
            seeps = new ReadOnlyCollection<AlpineVillageBrookSeep>(
                new List<AlpineVillageBrookSeep>(sourceSeeps));
            cascades = new ReadOnlyCollection<AlpineVillageBrookCascade>(
                new List<AlpineVillageBrookCascade>(sourceCascades));
            LedgeCenter = ledgeCenter;
            LedgeFacing = ledgeFacing;
            BowlFacing = bowlFacing;
            BowlCenter = bowlCenter;
            BowlWaterTopY = bowlWaterTopY;
            BowlSize = bowlSize;
            OverflowLip = overflowLip;
            ChapelBasinPoint = chapelBasinPoint;
            OutfallPoint = outfallPoint;
        }

        /// <summary>The brook proper, bowl lip to cableway cut.</summary>
        public IReadOnlyList<AlpineVillageBrookSample> Samples => samples;

        /// <summary>
        /// The wet contour to the chapel's basin. Ground, not channel.
        /// </summary>
        public IReadOnlyList<AlpineVillageBrookSample> SeepLine => seepLine;

        public IReadOnlyList<AlpineVillageBrookSeep> Seeps => seeps;
        public IReadOnlyList<AlpineVillageBrookCascade> Cascades => cascades;

        /// <summary>Centre of the rock the water comes out from under.
        /// </summary>
        public Vector3 LedgeCenter { get; }

        /// <summary>Which way the ledge's face - and its seeps - look.
        /// </summary>
        public Vector3 LedgeFacing { get; }

        /// <summary>Width, height and depth of the outcrop, in metres. The
        /// catch is placed against it, so the size is a plan fact.</summary>
        public Vector3 LedgeSize => AlpineVillageBrookPlanner.LedgeSize;

        public Vector3 BowlCenter { get; }
        /// <summary>The catch keeps the ledge's axes, but can turn halfway
        /// round so its authored +X overflow faces downhill.</summary>
        public Vector3 BowlFacing { get; }
        public Vector3 BowlOutletDirection =>
            Vector3.Cross(Vector3.up, BowlFacing).normalized;
        public float BowlWaterTopY { get; }
        public Vector2 BowlSize { get; }

        public Vector2 CatchOuterSize => BowlSize * 1.12f;
        public Vector2 BowlInnerHalfSize => new Vector2(
            CatchOuterSize.x * 0.33f, CatchOuterSize.y * 0.32f);
        public float OverflowWidth => CatchOuterSize.y * 0.28f - 0.04f;

        /// <summary>Clear standing ground in front of the real stone catch,
        /// rather than the spring plot's unrelated generic door dock.</summary>
        public Vector3 ApproachPosition => BowlCenter + LedgeFacing *
            (CatchOuterSize.y * 0.5f + 0.70f);

        /// <summary>The low point of the bowl's rim, where it spills.
        /// </summary>
        public Vector3 OverflowLip { get; }

        /// <summary>Where the wet contour arrives at the chapel's stonework.
        /// </summary>
        public Vector3 ChapelBasinPoint { get; }

        /// <summary>Where the water leaves the bowl of the village.</summary>
        public Vector3 OutfallPoint { get; }

        public float Length => samples.Count == 0
            ? 0f
            : samples[samples.Count - 1].Distance;

        /// <summary>
        /// How far the point lies outside the channel's centreline, and how
        /// deep the channel is there. The terrain sampler dishes a swale from
        /// this, and the snow field keeps off it.
        ///
        /// Only <see cref="Samples"/> counts. The seep line is wet ground with
        /// no channel at all, so dishing terrain under it would carve a
        /// trench across the lane for water that is soaking, not running.
        /// </summary>
        public float DistanceToChannel(Vector2 point, out float bedDepth)
        {
            bedDepth = 0f;
            if (samples.Count < 2)
            {
                return float.MaxValue;
            }

            float best = float.MaxValue;
            for (int index = 0; index < samples.Count - 1; index++)
            {
                AlpineVillageBrookSample first = samples[index];
                AlpineVillageBrookSample second = samples[index + 1];
                Vector2 start = ToXZ(first.Position);
                Vector2 segment = ToXZ(second.Position) - start;
                float lengthSquared = segment.sqrMagnitude;
                float amount = lengthSquared <= 0.000001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - start, segment) / lengthSquared);
                float distance = Vector2.Distance(
                    point,
                    start + segment * amount);
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                bedDepth = Mathf.Lerp(
                    first.BedDepth,
                    second.BedDepth,
                    amount);
            }

            return best;
        }

        /// <summary>
        /// How far the point lies outside any open water or ground the
        /// spring keeps permanently wet - channel, bowl and seep line
        /// together. This is the one oracle for "snow cannot lie here" and
        /// for the wet ground tint, so the two can never drift apart.
        /// </summary>
        public float DistanceOutsideWetGround(Vector2 point)
        {
            float channel = DistanceToChannel(point, out _) -
                            BrookSwaleHalfWidthForWetness;
            float bowl = DistanceOutsideBowl(point);
            float seep = DistanceOutsideSeepLine(point);
            return Mathf.Min(channel, Mathf.Min(bowl, seep));
        }

        private const float BrookSwaleHalfWidthForWetness = 1.35f;

        private float DistanceOutsideBowl(Vector2 point)
        {
            Vector2 delta = point - ToXZ(BowlCenter);
            Vector2 right = ToXZ(BowlOutletDirection);
            Vector2 forward = ToXZ(BowlFacing).normalized;
            float halfX = Mathf.Max(0.05f, CatchOuterSize.x * 0.5f);
            float halfZ = Mathf.Max(0.05f, CatchOuterSize.y * 0.5f);
            float outsideX = Mathf.Abs(Vector2.Dot(delta, right)) - halfX;
            float outsideZ = Mathf.Abs(Vector2.Dot(delta, forward)) - halfZ;
            if (outsideX <= 0f && outsideZ <= 0f)
            {
                return Mathf.Max(outsideX, outsideZ);
            }

            return new Vector2(
                Mathf.Max(0f, outsideX),
                Mathf.Max(0f, outsideZ)).magnitude;
        }

        private float DistanceOutsideSeepLine(Vector2 point)
        {
            if (seepLine.Count < 2)
            {
                return float.MaxValue;
            }

            float best = float.MaxValue;
            for (int index = 0; index < seepLine.Count - 1; index++)
            {
                AlpineVillageBrookSample first = seepLine[index];
                AlpineVillageBrookSample second = seepLine[index + 1];
                Vector2 start = ToXZ(first.Position);
                Vector2 segment = ToXZ(second.Position) - start;
                float lengthSquared = segment.sqrMagnitude;
                float amount = lengthSquared <= 0.000001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - start, segment) / lengthSquared);
                float half = Mathf.Lerp(
                    first.HalfWidth,
                    second.HalfWidth,
                    amount);
                float distance =
                    Vector2.Distance(point, start + segment * amount) - half;
                best = Mathf.Min(best, distance);
            }

            return best;
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }
}
