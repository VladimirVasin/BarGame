using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Plans the lake precinct as a municipal boat station nobody has
    /// maintained in a decade: a bank cut down to an inset waterline, a
    /// timber revetment along the whole water's edge, a pier walking out
    /// over the pond, a row of hire hulls hauled up and left, the rental
    /// hut with its faded board, a slipway chained shut, and the shore
    /// lamps whose reflections are the only thing on the water at night.
    /// Pure data: the world builder materialises it.
    ///
    /// Everything is derived from stateless integer hashes of the city
    /// seed and grid indices, so a seed always leaves the same boats
    /// rotting in the same order.
    ///
    /// The precinct's cells are not touched. The blueprint gives two by
    /// two cells of open water - 52 metres of square, which reads as a
    /// reservoir - and this planner draws its own smaller, cornered body
    /// of water inside them, exactly as the river draws its own channel
    /// inside cells the ground pass skips.
    /// </summary>
    public static class CityLakePlanner
    {
        private const float AccessClearance = 0.45f;
        private const float SpatialChunkSize = 48f;

        // The waterline is inset from the water cells by a bank, and its
        // corners are cut, so the shore always shows ground behind the
        // water and the silhouette is never a rectangle. 8 m off each
        // side takes the 52 m square to 36 m across.
        private const float BankFlatWidth = 6.4f;
        private const float BankSlopeWidth = 1.6f;
        private const float BankWidth = BankFlatWidth + BankSlopeWidth;

        // About 14% of the span. Smaller and the cut does not survive
        // the 640x360 composite; larger and the pond reads as a circle
        // somebody drew with a compass.
        private const float WaterlineBevel = 5.2f;

        // The whole basin falls 0.40 m, fixed upstream by
        // CityElevationPlanner.LakeWaterDatumBelowAccess. Taking that
        // fall over 1.6 m instead of over the full 8 m is what turns a
        // 5% ramp nobody notices into a 21% bank you can see.
        private const float BankToeClearance = 0.06f;

        private const float LakeBedDepth = 0.85f;

        // The revetment laps the bed by 5 cm rather than meeting it: the
        // river's rule that what is not allowed is a gap.
        private const float RevetmentEmbedment = 0.90f;
        private const float RevetmentFreeboard = 0.65f;
        private const float RevetmentThickness = 0.18f;
        private const float RevetmentBoardLength = 3.0f;

        // The pier reaches half way across the 36 m pond: far enough to
        // be standing on the water, short enough that the far bank is
        // still the far bank.
        private const float PierLength = 18.0f;
        private const float PierWidth = 2.2f;
        private const float PierRootDepth = -2.4f;
        private const float PierDeckTopAboveWater = 0.62f;
        private const float PierDeckThickness = 0.12f;
        private const float PierPileSpacing = 2.4f;
        private const float PierPileThickness = 0.26f;
        private const float PierPileEmbedment = 0.20f;
        private const float PierRailHeight = 0.95f;

        private const float HutFootprintDepth = 3.6f;
        private const float HutFootprintWidth = 2.8f;
        private const float HutWallHeight = 2.35f;

        // No room on the bank flat, no hut - and by contract no hut
        // bulb either.
        private const float HutRequiredBankRoom = 6.2f;
        private const float HutSignGlyphHeight = 0.34f;

        private const float BoatLength = 4.1f;
        private const float BoatBeam = 1.35f;

        // Hulls are stored bow-to-water, so what stands between two
        // neighbours is a beam and not a length. The pitch is a little
        // over twice the beam: close enough to read as a stack, wide
        // enough to walk between.
        private const float BoatRowPitch = 2.9f;
        private const int BoatTarget = 7;

        private const float SlipwayWidth = 4.0f;

        // The two authored gaps in the revetment. Each is closed by
        // geometry a person can see - the pier deck bridges over one at
        // knee height, the slipway's ramp and its chained beam close the
        // other - and each is exactly as wide as the thing that closes
        // it plus a hand's clearance, which is what lets the perimeter
        // contract check the two against each other.
        private const float PierGapWidth = PierWidth + 0.30f;
        private const float SlipwayGapWidth = SlipwayWidth + 0.30f;

        // How far the bank's walkable strips reach back over the shore
        // ring. The walkable mask tests rectangles independently, so
        // ground that only abuts leaves a band two agent radii wide
        // that nobody can occupy; this is the park fix's connector
        // reach, for the same reason.
        private const float BankSeamReach = 1.2f;

        // ASCII salts, one per decision, in the project convention.
        private const uint BoatSalt = 0x424F4154u;   // "BOAT"
        private const uint HullSalt = 0x48554C4Cu;   // "HULL"
        private const uint ReedSalt = 0x52454544u;   // "REED"
        private const uint RockSalt = 0x524F434Bu;   // "ROCK"
        private const uint DebrisSalt = 0x44454252u; // "DEBR"

        internal const string PierDeckHeadId = "lake-pier-deck-head";
        internal const string PierDeckRootId = "lake-pier-deck-00";

        /// <summary>
        /// Returns null when the layout carries no dressable lake: no
        /// lake water, no lake shore, no street access, or water cells
        /// that do not form one solid rectangle (the same silent bail
        /// the original open-area pass used, now visible to the caller).
        /// </summary>
        public static CityLakePlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!TryCreateSetup(
                    layout,
                    out CityLakeBasin basin,
                    out Frame frame,
                    out Rect grounds,
                    out CityOpenAreaAccessDescriptor access))
            {
                return null;
            }

            var parts = new List<CityLakePartDescriptor>(300);
            var lamps = new List<CityLakeLampDescriptor>(4);
            var reserved = new List<Rect>();
            var revetmentCuts = new List<PerimeterCut>();

            // Fixtures and buildings claim their bank-side spots first,
            // so the hulls and the scatter are planned around them and
            // never through them. This is the cemetery's order, not a
            // new one.
            bool hasHut = AddHut(parts, lamps, frame, basin, reserved, access);
            AddPier(parts, lamps, frame, basin, reserved, revetmentCuts);
            AddSlipway(parts, frame, basin, reserved, revetmentCuts);
            EmitRevetment(parts, basin, revetmentCuts);
            List<Rect> hulls = AddBoats(
                parts, frame, basin, layout.Seed, hasHut, reserved, access);
            AddBankDressing(
                parts, frame, basin, layout.Seed, reserved, hulls, access);

            var plan = new CityLakePlan(parts, lamps, grounds, basin);
            ValidateOrThrow(layout, plan);
            return plan;
        }

        /// <summary>
        /// The lake's contribution to the walkable mask: the bank ring
        /// the world builder draws, and the pier deck over the water.
        ///
        /// The blueprint marks the lake's inner cells `Water`, and a
        /// water cell is not walkable, so without this the player is
        /// clamped at the water-cell boundary — an invisible box around
        /// the whole pond, with the bank, the pier, the hut, the boats
        /// and the fisherman all sealed inside it. The precinct draws
        /// real ground inside those cells, so it has to say so here.
        ///
        /// The pond itself is never added. The bank is emitted as four
        /// strips around the waterline's axis-aligned bounds rather than
        /// around the cut octagon, because this mask is rectangular and
        /// the honest rounding is the one that keeps a person out of the
        /// water: the four corner bevels stay dry land the mask does not
        /// claim, never open water it does.
        ///
        /// Every strip is grown outward past the cell boundary by
        /// <see cref="BankSeamReach"/>. Adjacent rectangles are tested
        /// independently by <see cref="RoadWalkableArea"/>, so ground
        /// that merely abuts leaves a band two agent-radii wide that
        /// nobody can stand in — the seam the park fix had to solve. The
        /// strips also overlap each other along the full cell span so
        /// the ring is continuous around its own corners.
        /// </summary>
        public static void AppendWalkableFootprints(
            CityLayout layout,
            ICollection<Rect> destination)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!TryCreateSetup(
                    layout,
                    out CityLakeBasin basin,
                    out Frame frame,
                    out Rect _,
                    out CityOpenAreaAccessDescriptor _))
            {
                return;
            }

            Rect cells = basin.WaterCellBounds;
            Rect water = basin.WaterlineBounds;
            float reach = BankSeamReach;

            // West and east strips run the full depth of the cells;
            // south and north run their full width. The overlap that
            // buys is what makes the four corners walkable.
            destination.Add(Rect.MinMaxRect(
                cells.xMin - reach, cells.yMin - reach,
                water.xMin, cells.yMax + reach));
            destination.Add(Rect.MinMaxRect(
                water.xMax, cells.yMin - reach,
                cells.xMax + reach, cells.yMax + reach));
            destination.Add(Rect.MinMaxRect(
                cells.xMin - reach, cells.yMin - reach,
                cells.xMax + reach, water.yMin));
            destination.Add(Rect.MinMaxRect(
                cells.xMin - reach, water.yMax,
                cells.xMax + reach, cells.yMax + reach));

            // The four cut corners. The strips above stop at the
            // waterline's axis-aligned bounds, which leaves each bevel
            // triangle — real, collidered bank, up to 3.7 m deep — out
            // of the mask: four invisible corners of exactly the bug
            // this method exists to remove. A staircase of rectangles
            // fills each triangle without ever crossing its diagonal.
            AppendBevelCorner(
                destination, water.xMin, water.yMin, 1f, 1f, basin.BevelMeters);
            AppendBevelCorner(
                destination, water.xMax, water.yMin, -1f, 1f, basin.BevelMeters);
            AppendBevelCorner(
                destination, water.xMin, water.yMax, 1f, -1f, basin.BevelMeters);
            AppendBevelCorner(
                destination, water.xMax, water.yMax, -1f, -1f, basin.BevelMeters);

            // The pier. It is the one authored way out over the water,
            // and it starts back on the bank so it overlaps the ring
            // rather than beginning at the waterline.
            destination.Add(frame.RectFromDepthLateral(
                PierRootDepth,
                PierLength,
                frame.PierLateral - PierWidth * 0.5f,
                frame.PierLateral + PierWidth * 0.5f));
        }

        /// <summary>
        /// Fills one cut corner of the bank with a staircase of
        /// rectangles that stays on the land side of the bevel.
        ///
        /// Every step is anchored at the corner and grown outward by the
        /// seam reach, so the steps all overlap each other and both
        /// neighbouring side strips — abutting rectangles would leave
        /// their own dead bands. Step `i` reaches `(i+1)` slices along
        /// one axis and `bevel - (i+1)` slices across the other, so its
        /// far corner lands exactly on the diagonal `u + v = bevel` and
        /// no step ever claims water.
        ///
        /// A staircase cannot fill a triangle exactly: a band one slice
        /// wide stays uncovered against the diagonal itself. At eight
        /// steps that is `0.65 m` along each axis, about `0.46 m`
        /// perpendicular — the player stops a half step short of the
        /// diagonal boards rather than three and a half metres short of
        /// them, which is what the axis-aligned bounds alone gave.
        /// </summary>
        private static void AppendBevelCorner(
            ICollection<Rect> destination,
            float cornerX,
            float cornerZ,
            float signX,
            float signZ,
            float bevel)
        {
            const int steps = 8;
            float slice = bevel / steps;
            for (int index = 0; index < steps; index++)
            {
                float along = (index + 1) * slice;
                float across = bevel - along;
                if (across <= 0f)
                {
                    continue;
                }

                float x0 = cornerX - signX * BankSeamReach;
                float x1 = cornerX + signX * along;
                float z0 = cornerZ - signZ * BankSeamReach;
                float z1 = cornerZ + signZ * across;
                destination.Add(Rect.MinMaxRect(
                    Mathf.Min(x0, x1),
                    Mathf.Min(z0, z1),
                    Mathf.Max(x0, x1),
                    Mathf.Max(z0, z1)));
            }
        }

        /// <summary>
        /// Everything both the dressing pass and the navigation pass
        /// need to agree on: the basin, the gate-relative frame, the
        /// precinct bounds and the street access.
        ///
        /// Shared rather than recomputed because the walkable mask is
        /// built before the plan is and must describe the same bank the
        /// world builder will draw. If these two ever disagreed the
        /// player would be walled out of ground that visibly exists —
        /// which is exactly the bug this exists to prevent.
        /// </summary>
        private static bool TryCreateSetup(
            CityLayout layout,
            out CityLakeBasin basin,
            out Frame frame,
            out Rect grounds,
            out CityOpenAreaAccessDescriptor access)
        {
            basin = default;
            frame = default;
            grounds = default;

            var water = new List<CitySurfaceDescriptor>();
            var shore = new List<CitySurfaceDescriptor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Feature != CityAreaFeatureKind.Lake)
                {
                    continue;
                }

                if (surface.Kind == CitySurfaceKind.Water)
                {
                    water.Add(surface);
                }
                else if (surface.Kind == CitySurfaceKind.LakeShore)
                {
                    shore.Add(surface);
                }
            }

            if (water.Count == 0 ||
                shore.Count == 0 ||
                !TryGetAccess(layout, out access))
            {
                access = default;
                return false;
            }

            if (!TryGetSolidBounds(water, out Rect waterCellBounds))
            {
                return false;
            }

            grounds = water[0].WorldBounds;
            for (int index = 0; index < water.Count; index++)
            {
                grounds = Union(grounds, water[index].WorldBounds);
            }

            for (int index = 0; index < shore.Count; index++)
            {
                grounds = Union(grounds, shore[index].WorldBounds);
            }

            // A lake smaller than its own bank has nothing to dress.
            if (waterCellBounds.width <=
                    BankWidth * 2f + WaterlineBevel * 2f ||
                waterCellBounds.height <=
                    BankWidth * 2f + WaterlineBevel * 2f)
            {
                return false;
            }

            float bankTopY =
                shore[0].DatumY + CityElevationPlan.GroundTopOffset;
            float waterTopY = water[0].PhysicalTopY;
            basin = new CityLakeBasin(
                waterCellBounds,
                WaterlineBevel,
                BankFlatWidth,
                BankSlopeWidth,
                bankTopY,
                waterTopY,
                waterTopY - LakeBedDepth);
            frame = new Frame(basin, access);
            return true;
        }

        public static void ValidateOrThrow(
            CityLayout layout,
            CityLakePlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Count > CityLakePlan.MaximumPartCount)
            {
                throw new InvalidOperationException(
                    "Lake dressing exceeds its bounded part count.");
            }

            CityLakeBasin basin = plan.Basin;
            if (!(basin.BankFlatWidth > 0f) ||
                !(basin.BankSlopeWidth > 0f) ||
                !(basin.BevelMeters > 0f) ||
                basin.BevelMeters >= Mathf.Min(
                    basin.WaterlineBounds.width,
                    basin.WaterlineBounds.height) * 0.5f ||
                basin.WaterlineBounds.width <= 0f ||
                basin.WaterlineBounds.height <= 0f)
            {
                throw new InvalidOperationException(
                    "The lake basin must inset a positive bank and cut " +
                    "corners smaller than half its waterline span.");
            }

            if (basin.BankTopY <= basin.WaterTopY ||
                basin.BedTopY >= basin.WaterTopY)
            {
                throw new InvalidOperationException(
                    "The lake bank must stand above its water and its " +
                    "bed must lie below it.");
            }

            Rect containment = Expand(plan.Grounds, 0.65f);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            float revetmentTopY = float.NegativeInfinity;
            float bankToeY = basin.WaterTopY + BankToeClearance;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityLakePartDescriptor part = plan.Parts[index];
                if (string.IsNullOrWhiteSpace(part.StableId) ||
                    !ids.Add(part.StableId) ||
                    !IsPositiveFinite(part.Size) ||
                    !IsFinite(part.Center) ||
                    !IsFinite(part.Rotation))
                {
                    throw new InvalidOperationException(
                        "Lake parts require unique IDs and finite " +
                        "positive transforms.");
                }

                Rect footprint = ToXZRect(part);
                if (footprint.xMin < containment.xMin ||
                    footprint.xMax > containment.xMax ||
                    footprint.yMin < containment.yMin ||
                    footprint.yMax > containment.yMax)
                {
                    throw new InvalidOperationException(
                        $"Lake part '{part.StableId}' leaves the lake " +
                        "precinct.");
                }

                // Nothing stands in the water that was not authored to.
                // The cemetery's "no grave stands on an alley", restated
                // for a surface people cannot walk on at all.
                if (!MayStandInWater(part.Kind) &&
                    OverlapsWaterline(footprint, basin))
                {
                    throw new InvalidOperationException(
                        $"Lake part '{part.StableId}' stands in open " +
                        "water.");
                }

                if (part.Kind == CityLakePartKind.Revetment)
                {
                    revetmentTopY = Mathf.Max(
                        revetmentTopY,
                        part.Center.y + part.Size.y * 0.5f);
                }

                // Hire boats are hauled up, never afloat: a boat on the
                // water would say the station still runs.
                if (part.Kind == CityLakePartKind.Boat &&
                    OverlapsWaterline(footprint, basin))
                {
                    throw new InvalidOperationException(
                        $"Lake boat '{part.StableId}' floats; the hire " +
                        "hulls are hauled up on the bank.");
                }

                if (part.Kind == CityLakePartKind.PierDeck &&
                    part.Center.y - part.Size.y * 0.5f <
                        basin.WaterTopY + 0.40f)
                {
                    throw new InvalidOperationException(
                        $"Lake deck '{part.StableId}' lies too close to " +
                        "the water to walk over it.");
                }

                if (part.Kind == CityLakePartKind.PierPile &&
                    part.Center.y - part.Size.y * 0.5f >
                        basin.BedTopY + 0.05f)
                {
                    throw new InvalidOperationException(
                        $"Lake pile '{part.StableId}' does not reach the " +
                        "bed.");
                }

                if (!part.BlocksMovement ||
                    GetMinimumWorldY(part) >= plan.GroundTopY + 2.1f)
                {
                    continue;
                }

                for (int accessIndex = 0;
                     accessIndex < layout.OpenAreaAccesses.Count;
                     accessIndex++)
                {
                    CityOpenAreaAccessDescriptor access =
                        layout.OpenAreaAccesses[accessIndex];
                    if (access.Feature != CityAreaFeatureKind.Lake)
                    {
                        continue;
                    }

                    if (OverlapsStrict(
                            footprint,
                            Expand(access.ApproachBounds, AccessClearance)))
                    {
                        throw new InvalidOperationException(
                            $"Lake part '{part.StableId}' blocks its " +
                            "canonical street approach.");
                    }
                }
            }

            // The revetment is what stops a person walking into the
            // pond, so it has to be tall enough to read as a barrier
            // rather than as a kerb. Anything shorter and the generic
            // guard rail we removed was doing real work.
            if (plan.GetCount(CityLakePartKind.Revetment) > 0 &&
                revetmentTopY - bankToeY <
                    CityRoadGroundBoundaryPlanner.MaximumSafeStep + 0.20f)
            {
                throw new InvalidOperationException(
                    "The lake revetment must stand clear of the safe " +
                    "step above the bank toe.");
            }

            ValidatePerimeterContinuity(plan);
            ValidatePierRootIsClear(plan);

            var lampIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Lamps.Count; index++)
            {
                CityLakeLampDescriptor lamp = plan.Lamps[index];
                if (string.IsNullOrWhiteSpace(lamp.StableId) ||
                    !lampIds.Add(lamp.StableId) ||
                    !IsFinite(lamp.GroundPosition) ||
                    !plan.Grounds.Contains(new Vector2(
                        lamp.GroundPosition.x,
                        lamp.GroundPosition.z)))
                {
                    throw new InvalidOperationException(
                        "Lake lamps require unique IDs and must stand " +
                        "inside the precinct.");
                }
            }

            // A hut always lights its own doorway, and a station without
            // a hut carries no bulb to hang. The cemetery lodge's
            // contract, verbatim.
            bool hasHut = plan.GetCount(CityLakePartKind.Hut) > 0;
            bool hasHutBulb =
                plan.GetLampCount(CityLakeLampKind.HutDoor) == 1;
            if (hasHut != hasHutBulb)
            {
                throw new InvalidOperationException(
                    "The rental hut and its door bulb stand or fall " +
                    "together.");
            }

            if (plan.GetCount(CityLakePartKind.PierDeck) == 0)
            {
                throw new InvalidOperationException(
                    "A boat station without a pier is not a boat " +
                    "station.");
            }

            // The head lamp is the station's only light besides the hut
            // bulb, and the pier is what holds it up: one implies the
            // other, exactly as the hut implies its own bulb.
            if (plan.GetLampCount(CityLakeLampKind.PierHead) != 1)
            {
                throw new InvalidOperationException(
                    "A pier carries exactly one head lamp.");
            }

            if (plan.BoatCount == 0)
            {
                throw new InvalidOperationException(
                    "A boat station keeps at least one hull.");
            }

            // A full row shows the whole vocabulary, the way the
            // cemetery's nearest grave row does.
            if (plan.BoatCount >= 4)
            {
                for (int variant = 0; variant < 4; variant++)
                {
                    if (plan.GetBoatVariantCount(
                            (CityLakeBoatVariant)variant) == 0)
                    {
                        throw new InvalidOperationException(
                            "A full boat row shows every authored hull.");
                    }
                }
            }
        }

        /// <summary>
        /// Nothing solid stands across the pier.
        ///
        /// The hut claims its pocket before the pier is planned and the
        /// pier never consults the reserved list, so the two were free
        /// to interpenetrate — and a 2.35 m wall across the deck root is
        /// a wall the walkable mask cannot see, because the mask is
        /// rectangles and the wall is a collider. That is the same
        /// invisible stop this precinct exists to refuse, arriving by a
        /// different route.
        /// </summary>
        private static void ValidatePierRootIsClear(CityLakePlan plan)
        {
            var deck = new List<Rect>();
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityLakePartDescriptor part = plan.Parts[index];
                if (part.Kind == CityLakePartKind.PierDeck ||
                    part.Kind == CityLakePartKind.PierBeam)
                {
                    deck.Add(ToXZRect(part));
                }
            }

            if (deck.Count == 0)
            {
                return;
            }

            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityLakePartDescriptor part = plan.Parts[index];
                bool blocks =
                    part.Kind == CityLakePartKind.Hut ||
                    part.Kind == CityLakePartKind.HutSign ||
                    part.Kind == CityLakePartKind.Boat ||
                    part.Kind == CityLakePartKind.BoatRest ||
                    part.Kind == CityLakePartKind.Slipway ||
                    part.Kind == CityLakePartKind.Bollard;
                if (!blocks || !part.BlocksMovement)
                {
                    continue;
                }

                Rect footprint = ToXZRect(part);
                for (int deckIndex = 0; deckIndex < deck.Count; deckIndex++)
                {
                    if (OverlapsStrict(footprint, deck[deckIndex]))
                    {
                        throw new InvalidOperationException(
                            $"Lake part '{part.StableId}' stands across " +
                            "the pier.");
                    }
                }
            }
        }

        /// <summary>
        /// The whole waterline is either boarded or bridged. This is the
        /// data-level guarantee that the lake has no invisible
        /// perimeter: every metre of the octagon is covered by a
        /// revetment board or by one of the two authored gaps, each of
        /// which is closed by geometry a person can see.
        /// </summary>
        private static void ValidatePerimeterContinuity(CityLakePlan plan)
        {
            Vector2[] polygon = plan.Basin.CreateWaterlinePolygon();
            float perimeter = 0f;
            for (int index = 0; index < polygon.Length; index++)
            {
                perimeter += Vector2.Distance(
                    polygon[index],
                    polygon[(index + 1) % polygon.Length]);
            }

            if (plan.GetCount(CityLakePartKind.Revetment) == 0)
            {
                return;
            }

            float boarded = 0f;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityLakePartDescriptor part = plan.Parts[index];
                if (part.Kind == CityLakePartKind.Revetment)
                {
                    boarded += Mathf.Max(part.Size.x, part.Size.z);
                }
            }

            // The pier deck and the slipway ramp bridge their own gaps,
            // so their spans count as covered. Both use the same widths
            // the emission cut, so the two halves of this contract
            // cannot drift apart.
            float bridged =
                PierGapWidth +
                (plan.GetCount(CityLakePartKind.Slipway) > 0
                    ? SlipwayGapWidth
                    : 0f);
            if (boarded + bridged < perimeter - 0.05f)
            {
                throw new InvalidOperationException(
                    $"The lake waterline is {perimeter:F2} m but only " +
                    $"{boarded + bridged:F2} m of it is boarded or " +
                    "bridged; the rest would be an invisible edge.");
            }
        }

        // ------------------------------------------------------------------
        // the rental hut
        // ------------------------------------------------------------------

        private static bool AddHut(
            ICollection<CityLakePartDescriptor> parts,
            ICollection<CityLakeLampDescriptor> lamps,
            in Frame frame,
            in CityLakeBasin basin,
            ICollection<Rect> reserved,
            CityOpenAreaAccessDescriptor access)
        {
            if (basin.BankFlatWidth < HutRequiredBankRoom)
            {
                return false;
            }

            // The hut sits on the bank flat beside the pier root, so
            // gate, hut and pier read as one sentence in three-quarter
            // view. It prefers the side away from the boat row, and it
            // will take the other side if that one has no room.
            //
            // It is deliberately NOT clamped into place. Clamping a
            // laterally-cramped hut back inside the bank slides it onto
            // the pier, and a 2.35 m wall across the deck root is an
            // invisible stop the walkable mask cannot even see. A hut
            // with nowhere to stand simply does not stand — the same
            // rule the cemetery's gate lodge follows.
            float separation = PierWidth * 0.5f +
                               HutFootprintWidth * 0.5f + 1.6f;
            float lowerBound =
                frame.LateralMin + HutFootprintWidth * 0.5f + 0.5f;
            float upperBound =
                frame.LateralMax - HutFootprintWidth * 0.5f - 0.5f;
            float lateral = float.NaN;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float side = attempt == 0
                    ? -frame.BoatDirection
                    : frame.BoatDirection;
                float candidate = frame.PierLateral + side * separation;
                if (candidate >= lowerBound && candidate <= upperBound)
                {
                    lateral = candidate;
                    break;
                }
            }

            if (float.IsNaN(lateral))
            {
                return false;
            }

            float depthCenter = -(BankSlopeWidth + HutFootprintDepth * 0.5f + 0.6f);
            Rect footprint = frame.RectFromDepthLateral(
                depthCenter - HutFootprintDepth * 0.5f,
                depthCenter + HutFootprintDepth * 0.5f,
                lateral - HutFootprintWidth * 0.5f,
                lateral + HutFootprintWidth * 0.5f);
            if (!IsClearOfAccess(footprint, access))
            {
                return false;
            }

            float floorTop = basin.BankTopY + 0.16f;

            // The hut faces the gate, not the water. Its counter served
            // people arriving, so the door, the step and the board that
            // says what this place was all have to be the first thing a
            // visitor sees - a shed showing its blank back to the only
            // approach says nothing at all.
            float doorYaw = frame.BaseYawDegrees + 180f;
            Quaternion rotation = Quaternion.Euler(0f, doorYaw, 0f);
            Vector3 hutSize = new Vector3(
                HutFootprintWidth,
                HutWallHeight,
                HutFootprintDepth);

            // Timber floor on blocks, so the hut stands a step proud of
            // the damp the way every shed on a shore does.
            parts.Add(Part(
                "lake-hut-floor",
                CityLakePartKind.Hut,
                CityLakeStyle.Planking,
                frame.Compose(depthCenter, lateral, basin.BankTopY + 0.08f),
                rotation,
                new Vector3(hutSize.x + 0.24f, 0.16f, hutSize.z + 0.24f)));

            // Three walls and a door wall. The window looks at the pier:
            // whoever sat in here watched the boats, not the street.
            parts.Add(Part(
                "lake-hut-back",
                CityLakePartKind.Hut,
                CityLakeStyle.Concrete,
                frame.Compose(
                    depthCenter + HutFootprintDepth * 0.5f - 0.09f,
                    lateral,
                    floorTop + HutWallHeight * 0.5f),
                rotation,
                new Vector3(hutSize.x, HutWallHeight, 0.18f)));
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"lake-hut-side-{side}",
                    CityLakePartKind.Hut,
                    CityLakeStyle.Concrete,
                    frame.Compose(
                        depthCenter,
                        lateral + sign * (HutFootprintWidth * 0.5f - 0.09f),
                        floorTop + HutWallHeight * 0.5f),
                    rotation,
                    new Vector3(0.18f, HutWallHeight, hutSize.z)));
            }

            // The front: a jamb either side of the doorway and a lintel
            // over it, so the opening is a real hole rather than a
            // painted rectangle.
            float frontDepth = depthCenter - HutFootprintDepth * 0.5f + 0.09f;
            float doorWidth = 0.86f;
            float jamb = (HutFootprintWidth - doorWidth) * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"lake-hut-jamb-{side}",
                    CityLakePartKind.Hut,
                    CityLakeStyle.Concrete,
                    frame.Compose(
                        frontDepth,
                        lateral + sign * (doorWidth + jamb) * 0.5f,
                        floorTop + HutWallHeight * 0.5f),
                    rotation,
                    new Vector3(jamb, HutWallHeight, 0.18f)));
            }

            parts.Add(Part(
                "lake-hut-lintel",
                CityLakePartKind.Hut,
                CityLakeStyle.Concrete,
                frame.Compose(
                    frontDepth,
                    lateral,
                    floorTop + HutWallHeight - 0.20f),
                rotation,
                new Vector3(doorWidth, 0.40f, 0.18f)));

            // The door itself, left ajar against the jamb. Nobody locked
            // up; there was nothing left to lock up.
            parts.Add(Part(
                "lake-hut-door",
                CityLakePartKind.Hut,
                CityLakeStyle.Planking,
                frame.Compose(
                    frontDepth - 0.22f,
                    lateral - doorWidth * 0.28f,
                    floorTop + (HutWallHeight - 0.40f) * 0.5f),
                Quaternion.Euler(0f, doorYaw + 34f, 0f),
                new Vector3(doorWidth, HutWallHeight - 0.40f, 0.06f)));

            parts.Add(Part(
                "lake-hut-step",
                CityLakePartKind.Hut,
                CityLakeStyle.Concrete,
                frame.Compose(
                    depthCenter - HutFootprintDepth * 0.5f - 0.22f,
                    lateral,
                    basin.BankTopY + 0.08f),
                rotation,
                new Vector3(doorWidth + 0.4f, 0.16f, 0.44f)));

            // Roof with an eave over the door: the bulb hangs under it,
            // and the eave is what makes the bulb read as a fixture of
            // the building rather than a lamp that wandered up.
            parts.Add(Part(
                "lake-hut-roof",
                CityLakePartKind.Hut,
                CityLakeStyle.Planking,
                frame.Compose(
                    depthCenter - 0.24f,
                    lateral,
                    floorTop + HutWallHeight + 0.09f),
                rotation,
                new Vector3(
                    HutFootprintWidth + 0.44f,
                    0.18f,
                    HutFootprintDepth + 0.92f)));

            parts.Add(Part(
                "lake-hut-pipe",
                CityLakePartKind.Hut,
                CityLakeStyle.Iron,
                frame.Compose(
                    depthCenter + 0.9f,
                    lateral + HutFootprintWidth * 0.28f,
                    floorTop + HutWallHeight + 0.62f),
                rotation,
                new Vector3(0.16f, 0.94f, 0.16f)));

            // The counter hatch the boats were hired through, boarded
            // over with two planks nailed across it.
            for (int board = 0; board < 2; board++)
            {
                parts.Add(Part(
                    $"lake-hut-boarding-{board}",
                    CityLakePartKind.Hut,
                    CityLakeStyle.Planking,
                    frame.Compose(
                        frontDepth - 0.10f,
                        lateral + (board == 0 ? -0.02f : 0.04f),
                        floorTop + 1.28f + board * 0.30f),
                    Quaternion.Euler(
                        0f,
                        doorYaw,
                        board == 0 ? 7f : -5f),
                    new Vector3(HutFootprintWidth - 0.2f, 0.16f, 0.05f)));
            }

            AddHutSign(
                parts, frame, doorYaw, depthCenter, lateral, floorTop);

            lamps.Add(new CityLakeLampDescriptor(
                "lake-lamp-hut-door",
                CityLakeLampKind.HutDoor,
                frame.Compose(
                    depthCenter - HutFootprintDepth * 0.5f - 0.30f,
                    lateral + doorWidth * 0.72f,
                    basin.BankTopY),
                doorYaw));

            reserved.Add(Expand(footprint, 0.9f));
            return true;
        }

        private static void AddHutSign(
            ICollection<CityLakePartDescriptor> parts,
            in Frame frame,
            float doorYaw,
            float depthCenter,
            float lateral,
            float floorTop)
        {
            const string word = "ПРОКАТ ЛОДОК";
            float boardDepth =
                depthCenter - HutFootprintDepth * 0.5f - 0.48f;
            float boardTop = floorTop + HutWallHeight + 0.46f;
            float advance = HutSignGlyphHeight * 0.80f;
            float boardWidth = word.Length * advance + 0.34f;

            // The board itself, hanging off the eave, and a little out
            // of true: it has been up there a long time.
            parts.Add(Part(
                "lake-hut-signboard",
                CityLakePartKind.HutSign,
                CityLakeStyle.Planking,
                frame.Compose(boardDepth, lateral, boardTop),
                Quaternion.Euler(0f, doorYaw, -3.5f),
                new Vector3(boardWidth, HutSignGlyphHeight + 0.30f, 0.07f)));

            IReadOnlyList<SignSegmentRect> segments = CitySignLettering.Layout(
                word,
                HutSignGlyphHeight * 0.62f,
                HutSignGlyphHeight,
                advance);
            for (int index = 0; index < segments.Count; index++)
            {
                SignSegmentRect segment = segments[index];

                // The lettering rides the board's own list, so the
                // strokes stay square to the plank rather than to the
                // world.
                float tilt = -3.5f * Mathf.Deg2Rad;
                float localLateral = segment.Center.x;
                float localHeight = segment.Center.y;
                float rotatedLateral =
                    localLateral * Mathf.Cos(tilt) -
                    localHeight * Mathf.Sin(tilt);
                float rotatedHeight =
                    localLateral * Mathf.Sin(tilt) +
                    localHeight * Mathf.Cos(tilt);
                parts.Add(Part(
                    $"lake-hut-sign-{index:D2}",
                    CityLakePartKind.HutSign,
                    CityLakeStyle.PaintAccent,
                    frame.Compose(
                        boardDepth - 0.05f,
                        lateral - rotatedLateral,
                        boardTop + rotatedHeight),
                    Quaternion.Euler(0f, doorYaw, -3.5f),
                    new Vector3(
                        Mathf.Max(0.02f, segment.Size.x),
                        Mathf.Max(0.02f, segment.Size.y),
                        0.03f)));
            }
        }

        // ------------------------------------------------------------------
        // the pier
        // ------------------------------------------------------------------

        private static void AddPier(
            ICollection<CityLakePartDescriptor> parts,
            ICollection<CityLakeLampDescriptor> lamps,
            in Frame frame,
            in CityLakeBasin basin,
            ICollection<Rect> reserved,
            ICollection<PerimeterCut> cuts)
        {
            float lateral = frame.PierLateral;
            float deckTop = basin.WaterTopY + PierDeckTopAboveWater;
            float deckCenterY = deckTop - PierDeckThickness * 0.5f;
            Quaternion rotation =
                Quaternion.Euler(0f, frame.BaseYawDegrees, 0f);

            // Deck boards laid across the pier, one per metre-ish, so
            // the pier has a rhythm at walking speed. The head board is
            // named, because the fisherman's stance is read off it.
            int deckCount = Mathf.Max(
                2,
                Mathf.RoundToInt((PierLength - PierRootDepth) / 0.9f));
            for (int index = 0; index < deckCount; index++)
            {
                float from = Mathf.Lerp(
                    PierRootDepth, PierLength, index / (float)deckCount);
                float to = Mathf.Lerp(
                    PierRootDepth, PierLength, (index + 1f) / deckCount);
                float center = (from + to) * 0.5f;
                string id = index == 0
                    ? PierDeckRootId
                    : index == deckCount - 1
                        ? PierDeckHeadId
                        : $"lake-pier-deck-{index:D2}";
                parts.Add(Part(
                    id,
                    CityLakePartKind.PierDeck,
                    CityLakeStyle.Planking,
                    frame.Compose(center, lateral, deckCenterY),
                    rotation,
                    new Vector3(PierWidth, PierDeckThickness, to - from - 0.04f)));
            }

            // Bearers under the boards, running the length of the pier.
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"lake-pier-bearer-{side}",
                    CityLakePartKind.PierBeam,
                    CityLakeStyle.TarredTimber,
                    frame.Compose(
                        (PierRootDepth + PierLength) * 0.5f,
                        lateral + sign * (PierWidth * 0.5f - 0.14f),
                        deckTop - PierDeckThickness - 0.11f),
                    rotation,
                    new Vector3(0.20f, 0.22f, PierLength - PierRootDepth)));
            }

            // Piles. Their feet go below the bed, not to it: a pile that
            // stops exactly at the floor shows a seam every time the
            // water refracts.
            float pileBottom = basin.BedTopY - PierPileEmbedment;
            float pileHeight = deckTop - PierDeckThickness - pileBottom;
            int pairCount = Mathf.Max(
                2,
                Mathf.FloorToInt(PierLength / PierPileSpacing));
            for (int pair = 0; pair < pairCount; pair++)
            {
                float depth = 0.6f + pair * PierPileSpacing;
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1f : 1f;
                    parts.Add(Part(
                        $"lake-pier-pile-{pair:D2}-{side}",
                        CityLakePartKind.PierPile,
                        CityLakeStyle.TarredTimber,
                        frame.Compose(
                            depth,
                            lateral + sign * (PierWidth * 0.5f - 0.14f),
                            pileBottom + pileHeight * 0.5f),
                        rotation,
                        new Vector3(
                            PierPileThickness,
                            pileHeight,
                            PierPileThickness)));
                }
            }

            // A rail down one side only. Both sides would make it a
            // bridge; one side makes it a working pier somebody tied up
            // against, and leaves the fisherman somewhere to sit.
            float railLateral = lateral - PierWidth * 0.5f + 0.08f;
            int postCount = Mathf.Max(
                2,
                Mathf.FloorToInt(PierLength / 2.4f) + 1);
            for (int index = 0; index < postCount; index++)
            {
                float depth = Mathf.Lerp(
                    0.4f, PierLength - 0.3f, index / (postCount - 1f));
                parts.Add(Part(
                    $"lake-pier-post-{index:D2}",
                    CityLakePartKind.PierRail,
                    CityLakeStyle.TarredTimber,
                    frame.Compose(
                        depth,
                        railLateral,
                        deckTop + PierRailHeight * 0.5f),
                    rotation,
                    new Vector3(0.10f, PierRailHeight, 0.10f)));
            }

            parts.Add(Part(
                "lake-pier-rail",
                CityLakePartKind.PierRail,
                CityLakeStyle.TarredTimber,
                frame.Compose(
                    (0.4f + PierLength - 0.3f) * 0.5f,
                    railLateral,
                    deckTop + PierRailHeight - 0.06f),
                rotation,
                new Vector3(0.08f, 0.10f, PierLength - 0.7f)));

            // Head boards, closing the end. A pier you can walk off is a
            // hole, and a hole is the invisible edge we are refusing to
            // build anywhere on this lake.
            parts.Add(Part(
                "lake-pier-headboard",
                CityLakePartKind.PierRail,
                CityLakeStyle.TarredTimber,
                frame.Compose(
                    PierLength - 0.1f,
                    lateral,
                    deckTop + 0.36f),
                rotation,
                new Vector3(PierWidth, 0.72f, 0.10f)));

            // The one thing on the station that still works.
            //
            // The lamp stands at the far head of the pier, out over the
            // The one thing on the station that still works, and it is
            // not a fixture of the station at all: a small hand lamp
            // somebody stood on the rail cap at the very end of the
            // boards and never came back for. Warm, close, and sized
            // like an object rather than like infrastructure — a
            // municipal post out here would say the pier is still
            // maintained, which is the opposite of true.
            //
            // It sits on the rail rather than over the deck, so it
            // reads at hand height against the water instead of
            // overhead. The world builder raises the lamp itself; this
            // is the point it stands on.
            lamps.Add(new CityLakeLampDescriptor(
                "lake-lamp-pier-head",
                CityLakeLampKind.PierHead,
                frame.Compose(
                    PierLength - 0.55f,
                    railLateral,
                    deckTop + PierRailHeight - 0.01f),
                frame.BaseYawDegrees));

            reserved.Add(Expand(
                frame.RectFromDepthLateral(
                    PierRootDepth,
                    PierLength,
                    lateral - PierWidth * 0.5f,
                    lateral + PierWidth * 0.5f),
                1.1f));
            cuts.Add(new PerimeterCut(
                frame.Compose(0f, lateral, basin.WaterTopY),
                PierGapWidth * 0.5f));
        }

        // ------------------------------------------------------------------
        // the slipway
        // ------------------------------------------------------------------

        private static void AddSlipway(
            ICollection<CityLakePartDescriptor> parts,
            in Frame frame,
            in CityLakeBasin basin,
            ICollection<Rect> reserved,
            ICollection<PerimeterCut> cuts)
        {
            // Ninety degrees from the pier: the ramp the boats were
            // pulled out on, which is why the hulls are up on the bank
            // at all.
            Rect waterline = basin.WaterlineBounds;
            bool onXEdge = !frame.AlongX;
            float edgeSign = frame.SlipwaySign;
            Vector3 center = onXEdge
                ? new Vector3(
                    edgeSign > 0f ? waterline.xMax : waterline.xMin,
                    basin.WaterTopY,
                    waterline.center.y)
                : new Vector3(
                    waterline.center.x,
                    basin.WaterTopY,
                    edgeSign > 0f ? waterline.yMax : waterline.yMin);

            Vector3 outward = onXEdge
                ? new Vector3(edgeSign, 0f, 0f)
                : new Vector3(0f, 0f, edgeSign);
            Vector3 along = onXEdge
                ? new Vector3(0f, 0f, 1f)
                : new Vector3(1f, 0f, 0f);
            float yaw = onXEdge ? 90f : 0f;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            // Three ramp slabs stepping up from the water to the bank
            // flat, each a little more overgrown than the last.
            for (int index = 0; index < 3; index++)
            {
                float outwardOffset = 0.9f + index * 1.7f;
                float top = Mathf.Lerp(
                    basin.WaterTopY + 0.10f,
                    basin.BankTopY,
                    (index + 1f) / 3f);
                parts.Add(Part(
                    $"lake-slipway-{index}",
                    CityLakePartKind.Slipway,
                    CityLakeStyle.Concrete,
                    center + outward * outwardOffset +
                        Vector3.up * (top - basin.WaterTopY - 0.09f),
                    rotation,
                    new Vector3(SlipwayWidth, 0.18f, 1.7f)));
            }

            // Two bollards and the chain between them: the ramp is
            // closed, and it is closed by something a person can see.
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"lake-bollard-{side}",
                    CityLakePartKind.Bollard,
                    CityLakeStyle.Iron,
                    center + outward * 2.2f +
                        along * sign * (SlipwayWidth * 0.5f - 0.2f) +
                        Vector3.up * 0.52f,
                    rotation,
                    new Vector3(0.24f, 1.04f, 0.24f)));
            }

            parts.Add(Part(
                "lake-slipway-chain",
                CityLakePartKind.Bollard,
                CityLakeStyle.Iron,
                center + outward * 2.2f + Vector3.up * 0.86f,
                rotation,
                new Vector3(SlipwayWidth - 0.4f, 0.09f, 0.09f)));

            Vector2 flat = new Vector2(center.x, center.z);
            reserved.Add(Expand(
                Rect.MinMaxRect(
                    flat.x - SlipwayWidth,
                    flat.y - SlipwayWidth,
                    flat.x + SlipwayWidth,
                    flat.y + SlipwayWidth),
                0.6f));
            cuts.Add(new PerimeterCut(center, SlipwayGapWidth * 0.5f));
        }

        // ------------------------------------------------------------------
        // the revetment
        // ------------------------------------------------------------------

        /// <summary>
        /// Boards the whole waterline except the two authored gaps.
        ///
        /// The boards are cut to the gaps rather than dropped near them.
        /// Dropping whole boards leaves an opening wider than the pier
        /// or ramp that bridges it, which is the invisible edge this
        /// revetment exists to abolish - and which
        /// <see cref="ValidatePerimeterContinuity"/> would catch anyway.
        /// So each polygon edge is split into the runs the gaps leave
        /// behind, and each run is tiled exactly.
        /// </summary>
        private static void EmitRevetment(
            ICollection<CityLakePartDescriptor> parts,
            in CityLakeBasin basin,
            IReadOnlyList<PerimeterCut> cuts)
        {
            Vector2[] polygon = basin.CreateWaterlinePolygon();
            float bottom = basin.WaterTopY - RevetmentEmbedment;
            float top = basin.WaterTopY + RevetmentFreeboard;
            float height = top - bottom;
            var blocked = new List<Vector2>(cuts.Count);
            int emitted = 0;
            for (int index = 0; index < polygon.Length; index++)
            {
                Vector2 from = polygon[index];
                Vector2 to = polygon[(index + 1) % polygon.Length];
                Vector2 span = to - from;
                float length = span.magnitude;
                if (length <= 0.01f)
                {
                    continue;
                }

                Vector2 direction = span / length;
                float yaw = Mathf.Atan2(direction.x, direction.y) *
                            Mathf.Rad2Deg;

                CollectBlockedRuns(from, direction, length, cuts, blocked);

                float cursor = 0f;
                for (int gap = 0; gap <= blocked.Count; gap++)
                {
                    float runEnd = gap < blocked.Count
                        ? blocked[gap].x
                        : length;
                    EmitRevetmentRun(
                        parts,
                        from,
                        direction,
                        cursor,
                        runEnd,
                        yaw,
                        bottom + height * 0.5f,
                        height,
                        ref emitted);
                    if (gap < blocked.Count)
                    {
                        cursor = blocked[gap].y;
                    }
                }
            }
        }

        /// <summary>
        /// The sorted, merged runs of one polygon edge that a gap sits
        /// on, in metres from the edge's start. Clipping to the edge is
        /// what keeps a gap that reaches a corner from silently eating
        /// the neighbouring side.
        /// </summary>
        private static void CollectBlockedRuns(
            Vector2 from,
            Vector2 direction,
            float length,
            IReadOnlyList<PerimeterCut> cuts,
            List<Vector2> blocked)
        {
            blocked.Clear();
            for (int index = 0; index < cuts.Count; index++)
            {
                PerimeterCut cut = cuts[index];
                Vector2 centre = new Vector2(cut.Center.x, cut.Center.z);
                Vector2 offset = centre - from;

                // Only a gap that actually lies on this edge counts; a
                // gap on the far side of the pond projects onto this
                // line too, and its perpendicular distance is what says
                // so.
                float along = Vector2.Dot(offset, direction);
                float across = (offset - direction * along).magnitude;
                if (across > cut.HalfWidth + 0.6f)
                {
                    continue;
                }

                float start = Mathf.Clamp(
                    along - cut.HalfWidth, 0f, length);
                float end = Mathf.Clamp(along + cut.HalfWidth, 0f, length);
                if (end - start > 0.01f)
                {
                    blocked.Add(new Vector2(start, end));
                }
            }

            blocked.Sort((left, right) => left.x.CompareTo(right.x));
            for (int index = blocked.Count - 1; index > 0; index--)
            {
                if (blocked[index].x <= blocked[index - 1].y)
                {
                    blocked[index - 1] = new Vector2(
                        blocked[index - 1].x,
                        Mathf.Max(blocked[index - 1].y, blocked[index].y));
                    blocked.RemoveAt(index);
                }
            }
        }

        private static void EmitRevetmentRun(
            ICollection<CityLakePartDescriptor> parts,
            Vector2 from,
            Vector2 direction,
            float startMeters,
            float endMeters,
            float yaw,
            float centerY,
            float height,
            ref int emitted)
        {
            float run = endMeters - startMeters;
            if (run <= 0.05f)
            {
                return;
            }

            // Whole boards of about the authored length, sized to tile
            // the run exactly so the ring closes with no seam and no
            // overlap.
            int boards = Mathf.Max(
                1,
                Mathf.RoundToInt(run / RevetmentBoardLength));
            float boardLength = run / boards;
            for (int board = 0; board < boards; board++)
            {
                float middle = startMeters + (board + 0.5f) * boardLength;
                Vector2 point = from + direction * middle;
                parts.Add(Part(
                    $"lake-revetment-{emitted:D2}",
                    CityLakePartKind.Revetment,
                    CityLakeStyle.TarredTimber,
                    new Vector3(point.x, centerY, point.y),
                    Quaternion.Euler(0f, yaw, 0f),
                    new Vector3(
                        RevetmentThickness,
                        height,
                        boardLength)));
                emitted++;
            }
        }

        // ------------------------------------------------------------------
        // the hulls
        // ------------------------------------------------------------------

        private static List<Rect> AddBoats(
            ICollection<CityLakePartDescriptor> parts,
            in Frame frame,
            in CityLakeBasin basin,
            int seed,
            bool hasHut,
            IReadOnlyList<Rect> reserved,
            CityOpenAreaAccessDescriptor access)
        {
            var footprints = new List<Rect>(BoatTarget);

            // The row walks away from the hut along the bank flat: they
            // were dragged out of the water and left where the last
            // person to touch them stopped pulling.
            float startLateral = frame.PierLateral +
                                 frame.BoatDirection *
                                 (PierWidth * 0.5f + BoatRowPitch);

            // The row sits on the bank flat with a hull's length across
            // it, clear of the slope in front and the shore behind.
            float depth = -(BankSlopeWidth + BoatLength * 0.5f + 1.0f);
            int accepted = 0;
            for (int index = 0; index < BoatTarget * 2 &&
                                accepted < BoatTarget; index++)
            {
                float lateral = startLateral +
                                frame.BoatDirection * index * BoatRowPitch;
                if (lateral < frame.LateralMin + BoatBeam + 0.6f ||
                    lateral > frame.LateralMax - BoatBeam - 0.6f)
                {
                    break;
                }

                uint hash = StableHash(seed, index, 0, BoatSalt);
                float lateralJitter = ((hash & 0xFFFFu) / 65535f - 0.5f) * 0.5f;
                float depthJitter =
                    (((hash >> 16) & 0xFFFFu) / 65535f - 0.5f) * 0.8f;
                float yawJitter =
                    (((hash >> 8) & 0xFFu) / 255f - 0.5f) * 26f;

                float boatLateral = lateral + lateralJitter;
                float boatDepth = depth + depthJitter;

                // The footprint is the hull's real extents, not a square
                // of its length: a square that wide would have every
                // hull colliding with its own neighbour and the row
                // would thin itself out to nothing.
                Rect footprint = frame.RectFromDepthLateral(
                    boatDepth - BoatLength * 0.5f,
                    boatDepth + BoatLength * 0.5f,
                    boatLateral - BoatBeam * 0.75f,
                    boatLateral + BoatBeam * 0.75f);
                if (OverlapsAny(footprint, reserved, 0f) ||
                    OverlapsAny(footprint, footprints, 0.10f) ||
                    !IsClearOfAccess(footprint, access) ||
                    OverlapsWaterline(footprint, basin))
                {
                    continue;
                }

                // The first four cycle the whole vocabulary, so the row
                // seen from the gate shows every hull the station had.
                CityLakeBoatVariant variant = accepted < 4
                    ? (CityLakeBoatVariant)accepted
                    : (CityLakeBoatVariant)(
                        StableHash(seed, index, 1, HullSalt) % 4u);

                EmitBoat(
                    parts,
                    frame,
                    basin,
                    accepted,
                    variant,
                    boatDepth,
                    boatLateral,
                    yawJitter,
                    hasHut);
                footprints.Add(footprint);
                accepted++;
            }

            return footprints;
        }

        private static void EmitBoat(
            ICollection<CityLakePartDescriptor> parts,
            in Frame frame,
            in CityLakeBasin basin,
            int ordinal,
            CityLakeBoatVariant variant,
            float depth,
            float lateral,
            float yawJitter,
            bool hasHut)
        {
            // Every hull is upside down on two rests, which is how a
            // hire boat is stored and why the row reads as a row of
            // backs rather than a row of tubs.
            //
            // Keel-up means the narrow part is on top: a wide gunwale
            // on the ground, two panels sloping up and in, and the
            // bottom of the boat as a ridge along the sky. Built the
            // other way round - a flat lid on low sides - the row reads
            // as a stack of ramps, which is what the first pass did.
            float ground = basin.BankTopY;
            float yaw = frame.BaseYawDegrees + 90f + yawJitter;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            float length = BoatLength;
            float beam = BoatBeam;
            float depthOfHull = 0.54f;
            CityLakeStyle paint = CityLakeStyle.HullPaint;
            switch (variant)
            {
                case CityLakeBoatVariant.RoundHullDinghy:
                    length = BoatLength * 0.86f;
                    beam = BoatBeam * 1.12f;
                    depthOfHull = 0.60f;
                    break;
                case CityLakeBoatVariant.StavedPunt:
                    length = BoatLength * 1.12f;
                    beam = BoatBeam * 0.88f;
                    depthOfHull = 0.44f;
                    break;
                case CityLakeBoatVariant.HolledWreck:
                    // The one nobody bothered to turn over properly.
                    // It carries no paint left worth the name.
                    paint = CityLakeStyle.HullTar;
                    depthOfHull = 0.48f;
                    break;
            }

            for (int rest = 0; rest < 2; rest++)
            {
                float offset = (rest == 0 ? -1f : 1f) * length * 0.28f;
                parts.Add(BoatPart(
                    $"lake-boat-{ordinal:D2}-rest-{rest}",
                    CityLakePartKind.BoatRest,
                    CityLakeStyle.TarredTimber,
                    frame.Compose(depth, lateral, ground + 0.09f) +
                        rotation * new Vector3(offset, 0f, 0f),
                    rotation,
                    new Vector3(0.22f, 0.18f, beam + 0.3f),
                    ordinal,
                    variant));
            }

            // Everything is laid out in the hull's own frame - X along
            // her length, Z across her beam - and turned into the world
            // by the yaw. Heights are measured off the rests she sits on.
            float restTop = ground + 0.18f;
            float gunwaleY = 0.04f;
            float ridgeY = depthOfHull;
            float gunwaleZ = beam * 0.5f;
            float ridgeZ = beam * 0.17f;

            // Hoisted out of the local function below: a local function
            // may not capture an `in` parameter.
            Vector3 hullBase = frame.Compose(depth, lateral, restTop);

            Vector3 Place(float y, float z)
            {
                return hullBase + rotation * new Vector3(0f, y, z);
            }

            // The two sloping side panels. The tilt is the real angle
            // between gunwale and ridge, so the panels meet the ridge
            // instead of leaving a gap along the top.
            float riseZ = gunwaleZ - ridgeZ;
            float riseY = ridgeY - gunwaleY;
            float panelSpan = Mathf.Sqrt(riseZ * riseZ + riseY * riseY);
            float panelTilt = Mathf.Atan2(riseY, riseZ) * Mathf.Rad2Deg;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(BoatPart(
                    $"lake-boat-{ordinal:D2}-side-{side}",
                    CityLakePartKind.Boat,
                    paint,
                    Place(
                        (gunwaleY + ridgeY) * 0.5f,
                        sign * (gunwaleZ + ridgeZ) * 0.5f),
                    Quaternion.Euler(sign * panelTilt, yaw, 0f),
                    new Vector3(length * 0.97f, 0.08f, panelSpan),
                    ordinal,
                    variant));

                // The gunwale rail, resting on the blocks: the widest
                // line of the boat and the one at eye level from a
                // distance.
                parts.Add(BoatPart(
                    $"lake-boat-{ordinal:D2}-gunwale-{side}",
                    CityLakePartKind.Boat,
                    paint,
                    Place(gunwaleY, sign * gunwaleZ),
                    rotation,
                    new Vector3(length, 0.12f, 0.11f),
                    ordinal,
                    variant));
            }

            // The bottom of the boat, uppermost because she is keel-up.
            parts.Add(BoatPart(
                $"lake-boat-{ordinal:D2}-bottom",
                CityLakePartKind.Boat,
                paint,
                Place(ridgeY, 0f),
                rotation,
                new Vector3(length * 0.94f, 0.09f, ridgeZ * 2f),
                ordinal,
                variant));

            // Transom and bow: the transom square, the bow pinched, so
            // the two ends of the row are not the same shape.
            parts.Add(BoatPart(
                $"lake-boat-{ordinal:D2}-end-0",
                CityLakePartKind.Boat,
                paint,
                hullBase + rotation * new Vector3(
                    -length * 0.47f, (gunwaleY + ridgeY) * 0.5f, 0f),
                rotation,
                new Vector3(0.09f, ridgeY - gunwaleY, beam * 0.82f),
                ordinal,
                variant));
            parts.Add(BoatPart(
                $"lake-boat-{ordinal:D2}-end-1",
                CityLakePartKind.Boat,
                paint,
                hullBase + rotation * new Vector3(
                    length * 0.47f, (gunwaleY + ridgeY) * 0.52f, 0f),
                rotation,
                new Vector3(0.09f, ridgeY - gunwaleY, beam * 0.42f),
                ordinal,
                variant));

            // The keel, the one line that reads from the street.
            parts.Add(BoatPart(
                $"lake-boat-{ordinal:D2}-keel",
                CityLakePartKind.Boat,
                CityLakeStyle.HullTar,
                Place(ridgeY + 0.07f, 0f),
                rotation,
                new Vector3(length * 0.90f, 0.08f, 0.13f),
                ordinal,
                variant));

            // One oar left leaning on the hull nearest the hut: the
            // detail that says people, not props.
            if (ordinal == 0 && hasHut)
            {
                parts.Add(Part(
                    "lake-boat-00-oar",
                    CityLakePartKind.Debris,
                    CityLakeStyle.Litter,
                    Place(ridgeY * 0.55f, beam * 0.74f) +
                        rotation * new Vector3(length * 0.2f, 0f, 0f),
                    Quaternion.Euler(0f, yaw + 62f, 71f),
                    new Vector3(1.9f, 0.09f, 0.13f)));
            }
        }

        // ------------------------------------------------------------------
        // bank dressing
        // ------------------------------------------------------------------

        private static void AddBankDressing(
            ICollection<CityLakePartDescriptor> parts,
            in Frame frame,
            in CityLakeBasin basin,
            int seed,
            IReadOnlyList<Rect> reserved,
            IReadOnlyList<Rect> hulls,
            CityOpenAreaAccessDescriptor access)
        {
            Vector2[] polygon = basin.CreateWaterlinePolygon();
            int clump = 0;
            int rock = 0;
            int debris = 0;

            // Walk the waterline and drop things just behind it, which
            // is the only band of the bank anybody looks at.
            const int stations = 28;
            for (int index = 0; index < stations; index++)
            {
                float t = index / (float)stations;
                Vector2 point = SampleWaterline(polygon, t);
                Vector2 inward =
                    (basin.WaterlineBounds.center - point).normalized;
                uint hash = StableHash(seed, index, 0, ReedSalt);

                float back = 0.9f + ((hash & 0xFFu) / 255f) * 2.6f;
                Vector2 flat = point - inward * back;
                Vector3 ground = new Vector3(
                    flat.x,
                    basin.BankTopY,
                    flat.y);
                Rect footprint = Rect.MinMaxRect(
                    flat.x - 0.9f, flat.y - 0.9f,
                    flat.x + 0.9f, flat.y + 0.9f);
                if (OverlapsAny(footprint, reserved, 0f) ||
                    OverlapsAny(footprint, hulls, 0.2f) ||
                    !IsClearOfAccess(footprint, access))
                {
                    continue;
                }

                int kind = (int)((hash >> 8) % 5u);
                if (kind <= 1)
                {
                    // Reeds stand in the shallows, so these go forward
                    // of the toe rather than back from it.
                    Vector3 wet = new Vector3(
                        point.x - inward.x * 0.35f,
                        basin.WaterTopY,
                        point.y - inward.y * 0.35f);
                    for (int stem = 0; stem < 3; stem++)
                    {
                        float height = 0.62f + stem * 0.14f;
                        uint stemHash = StableHash(
                            seed, index, stem, ReedSalt);
                        Vector3 offset = new Vector3(
                            ((stemHash & 0xFFu) / 255f - 0.5f) * 0.5f,
                            0f,
                            (((stemHash >> 8) & 0xFFu) / 255f - 0.5f) * 0.5f);
                        parts.Add(Part(
                            $"lake-reeds-{clump:D2}-{stem}",
                            CityLakePartKind.Reeds,
                            CityLakeStyle.Reeds,
                            wet + offset + Vector3.up * (height * 0.5f),
                            Quaternion.Euler(
                                0f,
                                (stemHash % 360u),
                                ((stemHash >> 16) % 17u) - 8f),
                            new Vector3(0.12f, height, 0.12f)));
                    }

                    clump++;
                }
                else if (kind == 2 || kind == 3)
                {
                    uint rockHash = StableHash(seed, index, 0, RockSalt);
                    float width = 0.85f + ((rockHash & 0xFFu) / 255f) * 0.9f;
                    float height = 0.34f +
                                   (((rockHash >> 8) & 0xFFu) / 255f) * 0.3f;
                    parts.Add(Part(
                        $"lake-rock-{rock:D2}",
                        CityLakePartKind.Rock,
                        CityLakeStyle.LakeStone,
                        ground + Vector3.up * (height * 0.5f),
                        Quaternion.Euler(0f, rockHash % 360u, 0f),
                        new Vector3(width, height, width * 0.72f)));
                    rock++;
                }
                else
                {
                    // What the station left behind: a coil of rope, a
                    // tyre fender, a plank nobody carried away.
                    uint debrisHash = StableHash(seed, index, 0, DebrisSalt);
                    float length = 0.7f + ((debrisHash & 0xFFu) / 255f) * 1.3f;
                    parts.Add(Part(
                        $"lake-debris-{debris:D2}",
                        CityLakePartKind.Debris,
                        CityLakeStyle.Litter,
                        ground + Vector3.up * 0.09f,
                        Quaternion.Euler(0f, debrisHash % 360u, 0f),
                        new Vector3(length, 0.18f, 0.34f)));
                    debris++;
                }
            }
        }

        private static Vector2 SampleWaterline(
            Vector2[] polygon,
            float t)
        {
            float perimeter = 0f;
            for (int index = 0; index < polygon.Length; index++)
            {
                perimeter += Vector2.Distance(
                    polygon[index],
                    polygon[(index + 1) % polygon.Length]);
            }

            float target = Mathf.Repeat(t, 1f) * perimeter;
            float walked = 0f;
            for (int index = 0; index < polygon.Length; index++)
            {
                Vector2 from = polygon[index];
                Vector2 to = polygon[(index + 1) % polygon.Length];
                float length = Vector2.Distance(from, to);
                if (walked + length >= target)
                {
                    float local = length <= 1e-4f
                        ? 0f
                        : (target - walked) / length;
                    return Vector2.Lerp(from, to, local);
                }

                walked += length;
            }

            return polygon[0];
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private readonly struct PerimeterCut
        {
            public PerimeterCut(Vector3 center, float halfWidth)
            {
                Center = center;
                HalfWidth = halfWidth;
            }

            public Vector3 Center { get; }
            public float HalfWidth { get; }
        }

        /// <summary>
        /// A gate-relative coordinate frame over the basin: depth grows
        /// from the near waterline edge out across the water, lateral
        /// runs along that edge, and negative depth is the bank behind
        /// it. All layout happens in this frame so one algorithm serves
        /// all four access orientations.
        /// </summary>
        private readonly struct Frame
        {
            public Frame(
                CityLakeBasin basin,
                CityOpenAreaAccessDescriptor access)
            {
                Rect waterline = basin.WaterlineBounds;

                // The access normal points from the street into the
                // precinct; the pier grows along it, away from the gate.
                Vector3 inward = access.OutwardNormal.normalized;
                AlongX = Mathf.Abs(inward.x) > 0.5f;
                InwardSign = AlongX
                    ? Mathf.Sign(inward.x)
                    : Mathf.Sign(inward.z);
                if (AlongX)
                {
                    WaterEdge = InwardSign > 0f
                        ? waterline.xMin
                        : waterline.xMax;
                    LateralMin = waterline.yMin + basin.BevelMeters;
                    LateralMax = waterline.yMax - basin.BevelMeters;
                    GateLateral = access.Center.z;
                    BaseYawDegrees = InwardSign > 0f ? 90f : 270f;
                }
                else
                {
                    WaterEdge = InwardSign > 0f
                        ? waterline.yMin
                        : waterline.yMax;
                    LateralMin = waterline.xMin + basin.BevelMeters;
                    LateralMax = waterline.xMax - basin.BevelMeters;
                    GateLateral = access.Center.x;
                    BaseYawDegrees = InwardSign > 0f ? 0f : 180f;
                }

                // The pier takes the straight middle of the near edge,
                // offset off the gate's own axis so the approach stays
                // clear and the walk to the water is a turn rather than
                // a corridor.
                float span = LateralMax - LateralMin;
                float centre = (LateralMin + LateralMax) * 0.5f;
                PierLateral = Mathf.Clamp(
                    GateLateral + (GateLateral <= centre ? 1f : -1f) *
                        span * 0.16f,
                    LateralMin + PierWidth,
                    LateralMax - PierWidth);

                // The hulls walk toward whichever side of the pier has
                // more bank left.
                BoatDirection =
                    LateralMax - PierLateral >= PierLateral - LateralMin
                        ? 1f
                        : -1f;

                // The slipway takes the edge 90 degrees round from the
                // pier, on the side the boats are not stacked.
                SlipwaySign = -BoatDirection;
            }

            public bool AlongX { get; }
            public float InwardSign { get; }
            public float WaterEdge { get; }
            public float LateralMin { get; }
            public float LateralMax { get; }
            public float GateLateral { get; }
            public float BaseYawDegrees { get; }
            public float PierLateral { get; }
            public float BoatDirection { get; }
            public float SlipwaySign { get; }

            public Vector3 Compose(float depth, float lateral, float y)
            {
                float depthCoordinate = WaterEdge + InwardSign * depth;
                return AlongX
                    ? new Vector3(depthCoordinate, y, lateral)
                    : new Vector3(lateral, y, depthCoordinate);
            }

            public Rect RectFromDepthLateral(
                float depthMin,
                float depthMax,
                float lateralMin,
                float lateralMax)
            {
                float first = WaterEdge + InwardSign * depthMin;
                float second = WaterEdge + InwardSign * depthMax;
                float depthLow = Mathf.Min(first, second);
                float depthHigh = Mathf.Max(first, second);
                return AlongX
                    ? Rect.MinMaxRect(
                        depthLow, lateralMin, depthHigh, lateralMax)
                    : Rect.MinMaxRect(
                        lateralMin, depthLow, lateralMax, depthHigh);
            }
        }

        private static CityLakePartDescriptor Part(
            string stableId,
            CityLakePartKind kind,
            CityLakeStyle style,
            Vector3 center,
            Quaternion rotation,
            Vector3 size)
        {
            return new CityLakePartDescriptor(
                stableId,
                kind,
                style,
                center,
                rotation,
                size,
                -1,
                CityLakeBoatVariant.PlankSkiff);
        }

        /// <summary>
        /// A part that belongs to one hauled hull. The ordinal is what
        /// lets the plan count distinct boats and check that a full row
        /// shows every variant.
        /// </summary>
        private static CityLakePartDescriptor BoatPart(
            string stableId,
            CityLakePartKind kind,
            CityLakeStyle style,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            int boatOrdinal,
            CityLakeBoatVariant variant)
        {
            return new CityLakePartDescriptor(
                stableId,
                kind,
                style,
                center,
                rotation,
                size,
                boatOrdinal,
                variant);
        }

        private static bool MayStandInWater(CityLakePartKind kind)
        {
            switch (kind)
            {
                case CityLakePartKind.PierPile:
                case CityLakePartKind.PierBeam:
                case CityLakePartKind.PierDeck:
                case CityLakePartKind.PierRail:
                case CityLakePartKind.Revetment:
                case CityLakePartKind.Slipway:
                case CityLakePartKind.Bollard:
                case CityLakePartKind.Reeds:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a footprint reaches into open water. Sampled at the
        /// footprint's own corners and centre against the octagon, which
        /// is enough for parts this size and never reports a corner
        /// outside a cut bevel as wet.
        /// </summary>
        private static bool OverlapsWaterline(
            Rect footprint,
            in CityLakeBasin basin)
        {
            if (!OverlapsStrict(footprint, basin.WaterlineBounds))
            {
                return false;
            }

            const float inset = 0.05f;
            Vector2[] samples =
            {
                new Vector2(footprint.xMin + inset, footprint.yMin + inset),
                new Vector2(footprint.xMax - inset, footprint.yMin + inset),
                new Vector2(footprint.xMin + inset, footprint.yMax - inset),
                new Vector2(footprint.xMax - inset, footprint.yMax - inset),
                footprint.center
            };
            for (int index = 0; index < samples.Length; index++)
            {
                if (basin.ContainsWaterline(samples[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetSolidBounds(
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            out Rect bounds)
        {
            int minimumX = int.MaxValue;
            int maximumX = int.MinValue;
            int minimumZ = int.MaxValue;
            int maximumZ = int.MinValue;
            bounds = surfaces[0].WorldBounds;
            string areaId = surfaces[0].AreaId;
            for (int index = 0; index < surfaces.Count; index++)
            {
                if (!string.Equals(
                        surfaces[index].AreaId,
                        areaId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                Vector2Int cell = surfaces[index].Cell;
                minimumX = Mathf.Min(minimumX, cell.x);
                maximumX = Mathf.Max(maximumX, cell.x);
                minimumZ = Mathf.Min(minimumZ, cell.y);
                maximumZ = Mathf.Max(maximumZ, cell.y);
                bounds = Union(bounds, surfaces[index].WorldBounds);
            }

            int expected =
                (maximumX - minimumX + 1) * (maximumZ - minimumZ + 1);
            return surfaces.Count == expected;
        }

        private static Rect Union(Rect left, Rect right)
        {
            return Rect.MinMaxRect(
                Mathf.Min(left.xMin, right.xMin),
                Mathf.Min(left.yMin, right.yMin),
                Mathf.Max(left.xMax, right.xMax),
                Mathf.Max(left.yMax, right.yMax));
        }

        private static bool TryGetAccess(
            CityLayout layout,
            out CityOpenAreaAccessDescriptor access)
        {
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                if (layout.OpenAreaAccesses[index].Feature ==
                    CityAreaFeatureKind.Lake)
                {
                    access = layout.OpenAreaAccesses[index];
                    return true;
                }
            }

            access = default;
            return false;
        }

        private static bool IsClearOfAccess(
            Rect footprint,
            CityOpenAreaAccessDescriptor access)
        {
            if (string.IsNullOrEmpty(access.Id))
            {
                return true;
            }

            return !OverlapsStrict(
                footprint,
                Expand(access.ApproachBounds, AccessClearance));
        }

        private static bool OverlapsAny(
            Rect footprint,
            IReadOnlyList<Rect> rects,
            float expansion)
        {
            for (int index = 0; index < rects.Count; index++)
            {
                if (OverlapsStrict(
                        footprint,
                        Expand(rects[index], expansion)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Conservative XZ footprint of an oriented part: the rotated
        /// half-extents projected onto the world axes.
        /// </summary>
        private static Rect ToXZRect(CityLakePartDescriptor part)
        {
            Vector3 right = part.Rotation * Vector3.right;
            Vector3 up = part.Rotation * Vector3.up;
            Vector3 forward = part.Rotation * Vector3.forward;
            float halfX =
                Mathf.Abs(right.x) * part.Size.x * 0.5f +
                Mathf.Abs(up.x) * part.Size.y * 0.5f +
                Mathf.Abs(forward.x) * part.Size.z * 0.5f;
            float halfZ =
                Mathf.Abs(right.z) * part.Size.x * 0.5f +
                Mathf.Abs(up.z) * part.Size.y * 0.5f +
                Mathf.Abs(forward.z) * part.Size.z * 0.5f;
            return Rect.MinMaxRect(
                part.Center.x - halfX,
                part.Center.z - halfZ,
                part.Center.x + halfX,
                part.Center.z + halfZ);
        }

        private static float GetMinimumWorldY(CityLakePartDescriptor part)
        {
            Vector3 right = part.Rotation * Vector3.right;
            Vector3 up = part.Rotation * Vector3.up;
            Vector3 forward = part.Rotation * Vector3.forward;
            float halfY =
                Mathf.Abs(right.y) * part.Size.x * 0.5f +
                Mathf.Abs(up.y) * part.Size.y * 0.5f +
                Mathf.Abs(forward.y) * part.Size.z * 0.5f;
            return part.Center.y - halfY;
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            const float epsilon = 0.001f;
            return left.xMin < right.xMax - epsilon &&
                   left.xMax > right.xMin + epsilon &&
                   left.yMin < right.yMax - epsilon &&
                   left.yMax > right.yMin + epsilon;
        }

        private static bool IsPositiveFinite(Vector3 value)
        {
            return value.x > 0f && value.y > 0f && value.z > 0f &&
                   IsFinite(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static uint StableHash(int seed, int x, int z, uint salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ salt;
                value = (value ^ (uint)x) * 16777619u;
                value = (value ^ (uint)z) * 16777619u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }
    }
}
