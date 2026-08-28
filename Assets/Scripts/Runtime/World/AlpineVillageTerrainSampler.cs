using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One pure terrain-height contract for the village, shared by planning,
    /// validation and mesh construction - the mountain road's discipline.
    ///
    /// It is deliberately split in two. <see cref="SampleMacroHeight"/> is the
    /// bare slope and knows nothing about what stands on it, so the planner can
    /// lay the lane along it before any plot exists.
    /// <see cref="SampleHeight"/> is the finished ground and flattens the
    /// shelves the lane and the plots sit on.
    /// </summary>
    internal static class AlpineVillageTerrainSampler
    {
        /// <summary>
        /// How far the soil is sunk under the lane skin. The lane never relies
        /// on a coplanar cutout, exactly as the mountain road's asphalt does
        /// not.
        /// </summary>
        internal const float LaneBedClearance = 0.18f;

        /// <summary>Half-width of the flattened shoulder either side of the
        /// lane, measured from its own edge.</summary>
        internal const float LaneShoulder = 1.4f;

        /// <summary>Over what distance the ground returns to the macro slope
        /// past a shelf.</summary>
        internal const float ShelfBlendDistance = 3.6f;

        /// <summary>Flat apron kept around a plot's footprint.</summary>
        internal const float PlotApron = 1.1f;

        /// <summary>
        /// Flat ground kept around the station pad, measured from its own
        /// edge.
        ///
        /// It has to be at least ONE TERRAIN CELL, and that is a correctness
        /// bound rather than a look. The ground the hero actually stands on is
        /// a mesh sampled on a `2 m` grid and linearly interpolated between
        /// samples, so a shelf narrower than a cell is not reproduced at its
        /// own rim: the outward vertex bracketing the edge sits on the raw
        /// slope and drags the rim down with it, by up to `0.16 m` on the
        /// downhill flank - which on top of the pad's own `0.16 m` slab is a
        /// `0.32 m` lip against a `0.28 m` step offset. One cell guarantees
        /// every vertex bracketing the rim is itself on the flat.
        /// </summary>
        internal const float StationApron = TerrainCell;

        /// <summary>
        /// The pitch the village ground mesh is sampled at. It lives here
        /// rather than in the world builder because the SAMPLER is the
        /// contract and the mesh is one of its readers - and because the
        /// apron above has to be measured against it.
        /// </summary>
        internal const float TerrainCell = 2f;

        /// <summary>
        /// Where the enclosing ridge starts to climb, as a distance outside
        /// the walkable extent.
        /// </summary>
        internal const float RidgeStandoff = 6f;

        /// <summary>
        /// Steeper than the hero's `45°` slope limit, and that is the whole
        /// reason for the number rather than a look: at `0.62` (`32°`) the
        /// ridge was climbable, so "you can only get here by cabin" was a
        /// claim held up by the walkable mask alone. `1.15` is `49°`, which
        /// the `CharacterController` refuses on its own.
        /// </summary>
        internal const float RidgeRisePerMeter = 1.15f;

        internal const float RidgeMaximumRise = 34f;

        internal static float SampleMacroHeight(
            Vector3 slopeOrigin,
            Vector3 uphill,
            float grade,
            Vector2 point)
        {
            Vector2 origin = new Vector2(slopeOrigin.x, slopeOrigin.z);
            Vector2 axis = new Vector2(uphill.x, uphill.z);
            if (axis.sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentException(
                    "The village slope needs a horizontal uphill direction.",
                    nameof(uphill));
            }

            axis.Normalize();
            Vector2 across = new Vector2(axis.y, -axis.x);
            Vector2 delta = point - origin;
            float along = Vector2.Dot(delta, axis);
            float lateral = Vector2.Dot(delta, across);

            // A very gentle plane, plus enough undulation that the eye reads
            // ground rather than a ramp. The cross-fall is a tenth of the
            // climb: the slope leans, it does not tilt.
            float macro = slopeOrigin.y +
                          along * grade +
                          lateral * grade * 0.1f;
            float undulation =
                Mathf.Sin(point.x * 0.27f + point.y * 0.15f) * 0.17f +
                Mathf.Sin(point.x * -0.09f + point.y * 0.23f) * 0.11f;
            return macro + undulation;
        }

        internal static float SampleHeight(
            AlpineVillagePlan plan,
            Vector2 point)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            float macro = SampleMacroHeight(
                plan.SlopeOrigin,
                plan.Uphill,
                plan.Grade,
                point);
            float height = macro;

            // The lane's own shelf. Inside the carriageway the ground is the
            // centreline height; past the shoulder it eases back to the slope.
            float laneDistance = plan.Lane.FindNearest(
                point,
                out float lateralDistance);
            AlpineVillageLaneSample sample = plan.Lane.Sample(laneDistance);
            float laneHalfWidth = sample.Width * 0.5f;
            float laneBed = sample.Position.y - LaneBedClearance;
            float pastEdge = Mathf.Max(
                0f,
                lateralDistance - laneHalfWidth - LaneShoulder);
            float laneWeight = 1f - Mathf.SmoothStep(
                0f,
                ShelfBlendDistance,
                pastEdge);
            height = Mathf.Lerp(height, laneBed, laneWeight);

            // THE STATION STANDS ON GROUND, and until this it did not.
            //
            // The planner sets the pad `7 m` DOWNHILL of the lane foot and
            // then forces its height to the foot's - and nothing flattened
            // anything underneath. The slab hung between `0.19 m` and
            // `1.32 m` in the air, every edge of it was a lip of `0.34 m` to
            // `1.50 m` against a `0.28 m` step offset, and the drop was
            // ONE-WAY: a hero who got off the station could never get back on
            // it. That is what "there are no steps and I cannot leave the
            // station" was.
            //
            // The shelf is cut to the pad's own base, so the `0.16 m` slab
            // stands on it as a single step, exactly as the summit's pad
            // stands on its plateau.
            float outsideStation = DistanceOutsideStation(plan.Station, point);
            if (outsideStation < ShelfBlendDistance)
            {
                float stationWeight = 1f - Mathf.SmoothStep(
                    0f,
                    ShelfBlendDistance,
                    outsideStation);
                height = Mathf.Lerp(
                    height,
                    plan.Station.PadArea.Center.y,
                    stationWeight);
            }

            // Every plot stands on level ground. A door threshold on a slope
            // is a step the hero cannot use, and the dock tolerance is two
            // centimetres.
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                float outside = DistanceOutsidePlot(plot, point);
                if (outside >= ShelfBlendDistance)
                {
                    continue;
                }

                float weight = 1f - Mathf.SmoothStep(
                    0f,
                    ShelfBlendDistance,
                    outside);
                height = Mathf.Lerp(height, plot.GroundCenter.y, weight);
            }

            return height + SampleRidgeRise(plan, point);
        }

        /// <summary>
        /// The bowl. Beyond the walkable extent the ground climbs steeply and
        /// keeps climbing, which is what makes the cabin the only way in.
        /// </summary>
        internal static float SampleRidgeRise(
            AlpineVillagePlan plan,
            Vector2 point)
        {
            float outside = DistanceOutsideRect(
                plan.TerrainBounds,
                point) - RidgeStandoff;
            if (outside <= 0f)
            {
                return 0f;
            }

            return Mathf.Min(RidgeMaximumRise, outside * RidgeRisePerMeter);
        }

        /// <summary>
        /// How far the point lies outside the plot's apron, on the ground
        /// plane. Zero anywhere on the level shelf itself.
        /// </summary>
        internal static float DistanceOutsidePlot(
            AlpineVillagePlotDescriptor plot,
            Vector2 point)
        {
            Vector2 center = new Vector2(
                plot.GroundCenter.x,
                plot.GroundCenter.z);
            Vector2 forward = new Vector2(plot.Facing.x, plot.Facing.z);
            if (forward.sqrMagnitude <= 0.000001f)
            {
                forward = Vector2.up;
            }

            forward.Normalize();
            Vector2 right = new Vector2(forward.y, -forward.x);
            Vector2 delta = point - center;
            float alongDepth = Mathf.Abs(Vector2.Dot(delta, forward)) -
                               (plot.FootprintSize.y * 0.5f + PlotApron);
            float alongWidth = Mathf.Abs(Vector2.Dot(delta, right)) -
                               (plot.FootprintSize.x * 0.5f + PlotApron);
            float outsideDepth = Mathf.Max(0f, alongDepth);
            float outsideWidth = Mathf.Max(0f, alongWidth);
            return Mathf.Sqrt(
                outsideDepth * outsideDepth + outsideWidth * outsideWidth);
        }

        /// <summary>
        /// How far the point lies outside the station's flat apron. Zero
        /// anywhere on the shelf the pad and its boarding strip stand on.
        /// </summary>
        internal static float DistanceOutsideStation(
            AlpineVillageStationPlan station,
            Vector2 point)
        {
            if (station == null)
            {
                throw new ArgumentNullException(nameof(station));
            }

            MountainRoadTerminalRect pad = station.PadArea;
            Vector2 center = new Vector2(pad.Center.x, pad.Center.z);
            Vector2 right = new Vector2(pad.Right.x, pad.Right.z).normalized;
            Vector2 forward =
                new Vector2(pad.Forward.x, pad.Forward.z).normalized;
            Vector2 delta = point - center;
            float acrossRight = Mathf.Abs(Vector2.Dot(delta, right)) -
                                (pad.Size.x * 0.5f + StationApron);
            float acrossForward = Mathf.Abs(Vector2.Dot(delta, forward)) -
                                  (pad.Size.y * 0.5f + StationApron);
            float outsideRight = Mathf.Max(0f, acrossRight);
            float outsideForward = Mathf.Max(0f, acrossForward);
            return Mathf.Sqrt(
                outsideRight * outsideRight +
                outsideForward * outsideForward);
        }

        private static float DistanceOutsideRect(Rect rect, Vector2 point)
        {
            float x = Mathf.Max(
                0f,
                Mathf.Max(rect.xMin - point.x, point.x - rect.xMax));
            float y = Mathf.Max(
                0f,
                Mathf.Max(rect.yMin - point.y, point.y - rect.yMax));
            return Mathf.Sqrt(x * x + y * y);
        }
    }
}
