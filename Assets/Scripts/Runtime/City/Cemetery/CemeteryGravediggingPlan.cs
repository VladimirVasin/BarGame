using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one job the cemetery watchman has to give: which vacant
    /// plot he sends the hero to, and the exact hole that comes out of
    /// it. Pure data derived from the cemetery plan, so a seed always
    /// offers the same grave whether or not anybody ever accepts it.
    ///
    /// The hole is axis-aligned rather than turned with the plot's own
    /// few degrees of jitter: the ground it is cut out of is a terrain
    /// skin subtracted by axis-aligned rectangles, and a grave that
    /// agrees with its own hole matters more than four degrees nobody
    /// can see.
    /// </summary>
    public sealed class CemeteryGravediggingPlan
    {
        /// <summary>Dug to the shoulder: deep enough to read as a
        /// grave, shallow enough to climb back out of.</summary>
        public const float PitDepthMeters = 1.60f;

        /// <summary>Mouth of the hole, along the plot's own heading
        /// and across it. A coffin's envelope with working room.
        /// </summary>
        public const float PitLengthMeters = 2.30f;
        public const float PitWidthMeters = 1.05f;

        /// <summary>How much earth the collar walls lining the hole
        /// keep between the pit face and the cut terrain edge.
        /// </summary>
        public const float PitWallThicknessMeters = 0.30f;

        /// <summary>The spoil heap: everything that came out of the
        /// hole, piled along one side within the plot's own envelope.
        /// </summary>
        public const float SpoilLengthMeters = 1.90f;
        public const float SpoilWidthMeters = 0.85f;

        private static readonly CemeteryGravediggingPlan AbsentPlan =
            new CemeteryGravediggingPlan();

        private CemeteryGravediggingPlan()
        {
            IsPresent = false;
        }

        private CemeteryGravediggingPlan(
            CityCemeteryPlotDescriptor plot,
            bool runsAlongX,
            float groundTopY)
        {
            IsPresent = true;
            Plot = plot;
            RunsAlongX = runsAlongX;
            GroundTopY = groundTopY;
            Ground = new Vector3(
                plot.Ground.x,
                groundTopY,
                plot.Ground.z);
            PitMouth = CreateCenteredRect(
                Ground,
                runsAlongX ? PitLengthMeters : PitWidthMeters,
                runsAlongX ? PitWidthMeters : PitLengthMeters);
            // The heap sits on the side of the hole away from the
            // headstone end, clear of the collar and still inside the
            // plot the watchman signed over.
            float spoilOffset =
                PitWidthMeters * 0.5f + SpoilWidthMeters * 0.5f + 0.12f;
            Vector3 spoilCenter = runsAlongX
                ? new Vector3(Ground.x, groundTopY, Ground.z + spoilOffset)
                : new Vector3(Ground.x + spoilOffset, groundTopY, Ground.z);
            SpoilCenter = spoilCenter;
            SpoilFootprint = CreateCenteredRect(
                spoilCenter,
                runsAlongX ? SpoilLengthMeters : SpoilWidthMeters,
                runsAlongX ? SpoilWidthMeters : SpoilLengthMeters);
        }

        /// <summary>False when the city has no cemetery, no lodge, or
        /// no vacant plot left to bury anybody in.</summary>
        public bool IsPresent { get; }

        /// <summary>The vacant plot the job was written against.
        /// </summary>
        public CityCemeteryPlotDescriptor Plot { get; }

        /// <summary>Ground point at the middle of the hole.</summary>
        public Vector3 Ground { get; }

        /// <summary>True when the hole's long axis runs along world X.
        /// </summary>
        public bool RunsAlongX { get; }

        public float GroundTopY { get; }

        /// <summary>The rectangle cut out of the cemetery's terrain
        /// skin — the hole's mouth, exactly.</summary>
        public Rect PitMouth { get; }

        public Vector3 SpoilCenter { get; }
        public Rect SpoilFootprint { get; }

        /// <summary>Floor level of the finished hole.</summary>
        public float PitFloorY => GroundTopY - PitDepthMeters;

        /// <summary>
        /// The whole worksite — hole, collar and spoil heap. It never
        /// leaves the plot, so digging can never disturb a neighbour.
        /// </summary>
        public Rect WorkFootprint
        {
            get
            {
                Rect collar = Expand(PitMouth, PitWallThicknessMeters);
                return Rect.MinMaxRect(
                    Mathf.Min(collar.xMin, SpoilFootprint.xMin),
                    Mathf.Min(collar.yMin, SpoilFootprint.yMin),
                    Mathf.Max(collar.xMax, SpoilFootprint.xMax),
                    Mathf.Max(collar.yMax, SpoilFootprint.yMax));
            }
        }

        /// <summary>
        /// The job nearest the watchman's own post: he is not going to
        /// walk a new man to the far fence. Ties break on the plot's
        /// stable id, so the choice never depends on list order.
        /// </summary>
        public static CemeteryGravediggingPlan Create(
            CityCemeteryPlan cemeteryPlan,
            CemeteryWatchmanPlan watchmanPlan)
        {
            if (cemeteryPlan == null ||
                watchmanPlan == null ||
                !watchmanPlan.IsPresent)
            {
                return AbsentPlan;
            }

            Vector3 post = watchmanPlan.Stance.Position;
            bool found = false;
            CityCemeteryPlotDescriptor best = default;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0;
                 index < cemeteryPlan.Plots.Count;
                 index++)
            {
                CityCemeteryPlotDescriptor plot =
                    cemeteryPlan.Plots[index];
                if (!plot.IsVacant)
                {
                    continue;
                }

                float distance = new Vector2(
                    plot.Ground.x - post.x,
                    plot.Ground.z - post.z).sqrMagnitude;
                const float tieEpsilon = 0.0001f;
                bool nearer = !found ||
                              distance < bestDistance - tieEpsilon;
                bool tiedAndEarlier =
                    found &&
                    !nearer &&
                    distance <= bestDistance + tieEpsilon &&
                    string.CompareOrdinal(
                        plot.StableId,
                        best.StableId) < 0;
                if (nearer || tiedAndEarlier)
                {
                    found = true;
                    best = plot;
                    bestDistance = distance;
                }
            }

            if (!found)
            {
                return AbsentPlan;
            }

            // The plot's heading turned into the world axis it is
            // closest to: the cemetery frame only ever faces the four
            // compass directions, and the jitter is four degrees.
            Vector3 forward = best.Yaw * Vector3.forward;
            bool runsAlongX =
                Mathf.Abs(forward.x) > Mathf.Abs(forward.z);
            return new CemeteryGravediggingPlan(
                best,
                runsAlongX,
                cemeteryPlan.GroundTopY);
        }

        /// <summary>
        /// The contract the digging rests on: a finite hole that fits
        /// inside its own plot with its collar and heap, so cutting it
        /// out of the terrain can never reach a neighbouring grave.
        /// </summary>
        public static void ValidateOrThrow(CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return;
            }

            if (plan.Plot.State != CityCemeteryPlotState.Vacant)
            {
                throw new InvalidOperationException(
                    "A gravedigging job must be written against a " +
                    "vacant plot.");
            }

            if (plan.PitMouth.width <= 0f ||
                plan.PitMouth.height <= 0f ||
                float.IsNaN(plan.GroundTopY) ||
                float.IsInfinity(plan.GroundTopY))
            {
                throw new InvalidOperationException(
                    "A gravedigging job requires a finite positive " +
                    "hole.");
            }

            Rect footprint = plan.WorkFootprint;
            Rect plot = plan.Plot.Footprint;
            if (footprint.xMin < plot.xMin ||
                footprint.xMax > plot.xMax ||
                footprint.yMin < plot.yMin ||
                footprint.yMax > plot.yMax)
            {
                throw new InvalidOperationException(
                    $"The gravedigging worksite leaves plot " +
                    $"'{plan.Plot.StableId}'.");
            }
        }

        private static Rect CreateCenteredRect(
            Vector3 center,
            float sizeX,
            float sizeZ)
        {
            return Rect.MinMaxRect(
                center.x - sizeX * 0.5f,
                center.z - sizeZ * 0.5f,
                center.x + sizeX * 0.5f,
                center.z + sizeZ * 0.5f);
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }
    }
}
