using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Over what distance the ground returns to the macro slope past a
        /// shelf.
        ///
        /// FOR A LONG TIME IT DID NOT MEAN THIS. All three blends read
        /// `Mathf.SmoothStep(0f, ShelfBlendDistance, distance)`, and Unity's
        /// third argument is a `0-1` FRACTION, not a distance: the call
        /// returns metres, saturates at one metre of input, and the weight
        /// falling out of `1 - that` went NEGATIVE past `0.347 m`. It
        /// survived only because `Mathf.Lerp` clamps, so every shelf blended
        /// out over `0.347 m` instead of `3.6` - a factor of `10.4` - while
        /// the guards a few lines away (`outside >= ShelfBlendDistance`)
        /// went on believing the constant. `TerrainMargin` was sized against
        /// the intended `3.6` all along, so the fix moves the ground TOWARDS
        /// what the rest of the plan already assumed. Use
        /// <see cref="SmoothRange"/> here, never the bare `Mathf.SmoothStep`.
        /// </summary>
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
        /// the inhabited extent.
        ///
        /// It is also the walkable mask's own outer boundary - see
        /// <see cref="AlpineVillageWalkableArea.GroundOutset"/>. That is a
        /// contract and not a coincidence: the mask ends on the exact line
        /// where the ground starts refusing him, so the perimeter is held by
        /// the slope rather than by an invisible wall standing on flat snow.
        /// </summary>
        internal const float RidgeStandoff = 3f;

        /// <summary>
        /// Steeper than the hero's `45°` slope limit, and that is the floor
        /// of the number rather than a look: at `0.62` (`32°`) the ridge was
        /// climbable, so "you can only get here by cabin" was a claim held up
        /// by the walkable mask alone. `1.15` (`49°`) closed that and `1.6`
        /// (`58°`) read as a smear in the haze; `3.6` is `74°`, which reaches
        /// the full rise `16.7 m` past the toe, keeps the lateral crests well
        /// inside the `110 m` draw range and the ridge material's `96 m`
        /// handoff, and lifts the mean silhouette from mid-lane to `34.1°`
        /// (`43°` on the nearest bearings).
        ///
        /// The mask no longer depends on this, which is the point: it opens
        /// the whole bowl and stops at
        /// <see cref="RidgeStandoff"/>, so the wall is what actually holds
        /// the perimeter and there is nothing invisible about it.
        /// </summary>
        internal const float RidgeRisePerMeter = 3.6f;

        /// <summary>
        /// Full height of the wall over the bowl floor. `34` with the old
        /// `30 m` margin subtended `16-20°` and dissolved into the haze;
        /// `60` over the `12 m` margin is what makes the bowl press in over
        /// the roofs. The plan's world-bounds ceiling follows it.
        /// </summary>
        internal const float RidgeMaximumRise = 60f;

        /// <summary>
        /// Ground carried past the full rise. The visible mesh must not end on
        /// the crest itself: from inside the bowl that exposes both the back
        /// of a single-sided mesh and the empty world beyond it.
        /// </summary>
        internal const float RidgeCrestDepth = 8f;

        /// <summary>
        /// Minimum distance the physical mesh continues past the inhabited
        /// bounds: standoff, complete steep rise, then a hidden crest.
        /// </summary>
        internal const float RidgeMeshOutset =
            RidgeStandoff +
            RidgeMaximumRise / RidgeRisePerMeter +
            RidgeCrestDepth;

        /// <summary>
        /// The shared height of a visible cable tower. The cableway builder
        /// refuses to draw an A-frame shorter than this; using the same value
        /// for its planned ground keeps the rollers on the rope.
        /// </summary>
        internal const float CablewaySupportClearance = 4.8f;

        /// <summary>
        /// Flat half-width of the descending cut. It includes both A-frame
        /// feet and one terrain cell, so the triangles bracketing a foot are
        /// on the same ground rather than pulling it up the side wall.
        /// </summary>
        /// <summary>
        /// The cut under the rope is a valley now, not a slot: the line runs
        /// on for a far plane and more, and a `16 m` trench that deep reads
        /// as a canyon from the seat. The core carries both tracks and their
        /// cabins with air to spare; the blend brings the walls in at a slope
        /// a mountainside actually has.
        /// </summary>
        internal const float CablewayCutCoreHalfWidth = 7f;

        internal const float CablewayCutBlendWidth = 12f;

        internal const float CablewayCutOuterHalfWidth =
            CablewayCutCoreHalfWidth + CablewayCutBlendWidth;

        /// <summary>
        /// How quickly the ground gets out from under the rope once the
        /// apron ends. It was `6 m` and the cabin outran it: the rope starts
        /// falling at the pad's own edge while the cut was still a tenth of
        /// the way in, so the underside was in the hillside for the first few
        /// metres of every descent. The ramp has to be shorter than the cabin
        /// takes to fall its own hang.
        /// </summary>
        private const float CablewayCutRampLength = 3.5f;
        // One cell, and it must stay one cell: `closeStart` is measured from
        // the last support plus this, so widening it eats the ramp the
        // mountain closes over the rope on.
        private const float CablewaySupportShelfHalfLength = TerrainCell;
        private const float CablewaySupportShelfBlend = TerrainCell * 0.5f;

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
            float laneWeight = 1f - SmoothRange(
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
                float stationWeight = 1f - SmoothRange(
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

                float weight = 1f - SmoothRange(
                    0f,
                    ShelfBlendDistance,
                    outside);
                height = Mathf.Lerp(height, plot.GroundCenter.y, weight);
            }

            float enclosedHeight = height + SampleRidgeRise(plan, point);
            return SampleCablewayBrink(plan, point, enclosedHeight);
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
        /// Cuts the one honest opening through the lower edge of the bowl.
        /// The station apron remains level; immediately after it, the ground
        /// falls under the descending rope, carries every visible support and
        /// closes again before the hidden turn enters the mountain.
        /// </summary>
        internal static float SampleCablewayBrink(
            AlpineVillagePlan plan,
            Vector2 point,
            float enclosedHeight)
        {
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            ProjectOntoCablewayLine(
                cableway,
                point,
                out float along,
                out float across);

            // TWO FRAMES MEET HERE AND THEY ARE `1.9 m` APART.
            //
            // `along` is measured from the STATION PAD'S CENTRE, because that
            // is what the cut's own entrance is measured from. Every distance
            // it gets compared against - node distances, the last support,
            // the line's own length - is measured along the CABLE,
            // and the cable starts `1.9 m` forward of the pad centre. Left
            // unconverted the whole descent profile was read `1.9 m` early:
            // each pylon's shelf sat short of its own legs (its sampled
            // ground came out `0.58 m` under the height the planner authored)
            // and, far worse, the ground closed back over the rope `1.9 m`
            // before the ride's blackout is complete - so the last stretch of
            // the visible descent happened inside the mountain, which is the
            // very thing the mountain road's own cut was just repaired for.
            float cableOrigin = Vector3.Dot(
                cableway.LowerCableCenter - cableway.StationArea.Center,
                cableway.LineForward);
            float alongCable = along - cableOrigin;

            float cutStart = cableway.StationArea.Size.y * 0.5f +
                             StationApron - TerrainCell * 0.5f;
            // The hill never closes over the rope any more. The line runs
            // on down the mountainside past the scene's draw range and is
            // clipped before it turns, so the cut simply follows the rope
            // to the end of the mesh: the bed every pylon's shelf keeps,
            // carried on until there is nothing left to draw.
            float cutEnd = cableway.LineLength + RidgeCrestDepth;
            if (along > cutStart && alongCable < cutEnd &&
                across < CablewayCutOuterHalfWidth)
            {
                float entranceWeight = SmoothRange(
                    cutStart,
                    cutStart + CablewayCutRampLength,
                    along);
                float lateralWeight = 1f - SmoothRange(
                    CablewayCutCoreHalfWidth,
                    CablewayCutOuterHalfWidth,
                    across);
                float cutGround = SampleCablewayGround(
                    cableway,
                    alongCable);
                float cutWeight = entranceWeight * lateralWeight;
                enclosedHeight = Mathf.Lerp(
                    enclosedHeight,
                    Mathf.Min(enclosedHeight, cutGround),
                    cutWeight);
            }

            // Each pylon owns a two-metre shelf in the SAME sampled contract.
            // That is not decorative flattening: a two-metre terrain grid can
            // otherwise interpolate across a bend in the descent profile and
            // leave the authored GroundPosition floating between vertices.
            for (int index = 0; index < cableway.Nodes.Count; index++)
            {
                MountainCablewayNodeDescriptor node = cableway.Nodes[index];
                if (node.Kind != MountainCablewayNodeKind.Support)
                {
                    continue;
                }

                float pastShelf = Mathf.Max(
                    0f,
                    Mathf.Abs(alongCable - node.Distance) -
                    CablewaySupportShelfHalfLength);
                if (pastShelf >= CablewaySupportShelfBlend ||
                    across >= CablewayCutOuterHalfWidth)
                {
                    continue;
                }

                float lengthWeight = 1f - SmoothRange(
                    0f,
                    CablewaySupportShelfBlend,
                    pastShelf);
                float lateralWeight = 1f - SmoothRange(
                    CablewayCutCoreHalfWidth,
                    CablewayCutOuterHalfWidth,
                    across);
                enclosedHeight = Mathf.Lerp(
                    enclosedHeight,
                    Mathf.Min(enclosedHeight, node.GroundPosition.y),
                    lengthWeight * lateralWeight);
            }

            return enclosedHeight;
        }

        private static void ProjectOntoCablewayLine(
            MountainRoadCablewayPlan cableway,
            Vector2 point,
            out float along,
            out float across)
        {
            Vector2 origin = new Vector2(
                cableway.StationArea.Center.x,
                cableway.StationArea.Center.z);
            Vector2 forward = new Vector2(
                cableway.LineForward.x,
                cableway.LineForward.z).normalized;
            Vector2 right = new Vector2(
                cableway.LineRight.x,
                cableway.LineRight.z).normalized;
            Vector2 delta = point - origin;
            along = Vector2.Dot(delta, forward);
            across = Mathf.Abs(Vector2.Dot(delta, right));
        }

        private static float SampleCablewayGround(
            MountainRoadCablewayPlan cableway,
            float distance)
        {
            IReadOnlyList<MountainCablewayNodeDescriptor> nodes =
                cableway.Nodes;
            if (distance <= nodes[0].Distance)
            {
                return NodeCutGround(nodes[0]);
            }

            for (int index = 0; index < nodes.Count - 1; index++)
            {
                MountainCablewayNodeDescriptor first = nodes[index];
                MountainCablewayNodeDescriptor second = nodes[index + 1];
                if (distance > second.Distance)
                {
                    continue;
                }

                float span = Mathf.Max(
                    0.0001f,
                    second.Distance - first.Distance);
                return Mathf.Lerp(
                    NodeCutGround(first),
                    NodeCutGround(second),
                    (distance - first.Distance) / span);
            }

            return NodeCutGround(nodes[nodes.Count - 1]);
        }

        /// <summary>
        /// The bed the cut descends on, at one node.
        ///
        /// It is the node's own planned ground EXCEPT at the station, where
        /// that ground is the level pad the hero stands on - `0.8 m` above the
        /// clearance every other node keeps under the rope. Interpolating the
        /// cut out of the pad's height drags the whole first span up with it
        /// and squeezed the cabin to `0.86 m` of air in the middle of it. The
        /// pad itself is not at risk: the apron and the entrance ramp hold the
        /// real ground level over it, and this is only the profile they ramp
        /// TOWARDS.
        /// </summary>
        private static float NodeCutGround(
            MountainCablewayNodeDescriptor node)
        {
            return Mathf.Min(
                node.GroundPosition.y,
                node.CableCenter.y - CablewaySupportClearance);
        }

        private static float SmoothRange(float start, float end, float value)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(start, end, value));
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
