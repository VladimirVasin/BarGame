using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Plans the north seacoast — the place where the city runs out.
    /// One strip of sand, one strip of water, fog instead of a horizon,
    /// and three moods along it: the dead port west of the river mouth
    /// (a concrete mol walking into the sea, a beacon still burning on
    /// its head, a derrick crane nobody swings), the quiet granite
    /// esplanade east of the mouth with the municipal boat station
    /// that moved here from the drained lake — hut, hire sign, pier,
    /// slipway, hauled hulls and all — and the wild shore beyond it:
    /// rotten breakwater piles marching into the fog, driftwood, dune
    /// grass, and a stranded barge half-dissolved offshore. A timber
    /// footbridge crosses the mouth so the shore reads as one walk.
    /// Pure data: the world builder materialises it.
    ///
    /// Everything is derived from stateless integer hashes of the city
    /// seed and grid indices, so a seed always leaves the same hulls
    /// rotting in the same order.
    ///
    /// The precinct's cells are not touched. The blueprint gives one
    /// full-width row of open water and this planner draws over and
    /// into it exactly as the lake drew its own smaller pond inside
    /// water cells the ground pass skips.
    /// </summary>
    public static class CitySeacoastPlanner
    {
        private const float AccessClearance = 0.45f;

        // Zone cuts. The river corridor splits the shore; the esplanade
        // takes three blocks east of it and the wild shore takes the
        // rest. A coast without room for its port or its esplanade has
        // nothing to dress.
        private const float CenterZoneWidth = 78f;
        private const float MinimumWestZoneWidth = 24f;
        private const float MinimumCenterZoneWidth = 60f;
        private const float MinimumEastZoneWidth = 60f;

        // The esplanade band: a granite walk laid on the sand back
        // from the waterline, far enough that the boat row fits on the
        // strip between it and the sea wall.
        private const float EsplanadeSetback = 6.4f;
        private const float EsplanadeDepth = 3.0f;
        private const float EsplanadeSlabPitch = 3.25f;
        private const float EsplanadeSlabLift = 0.10f;
        private const float EsplanadeSlabThickness = 0.16f;

        // The sea wall at the waterline: the centre zone's authored
        // water edge, knee-high granite where the wild shore has bare
        // sand. Its top clears the safe step over the sand at the
        // waterline so it reads as a barrier, not a kerb.
        private const float SeaWallOffset = 0.55f;
        private const float SeaWallThickness = 0.35f;
        private const float SeaWallHeightAboveEdge = 0.55f;
        private const float SeaWallEmbedment = 0.25f;
        private const float SeaWallBoardLength = 3.0f;

        // The pier, plank for plank the lake's: the deck a little
        // higher over a sea that breathes, the root back on the sand
        // strip so the walk onto the boards is a step, not a climb.
        private const float PierWidth = 2.2f;
        private const float PierReach = 14.0f;
        private const float PierRootDepth = 4.0f;
        private const float PierDeckTopAboveSea = 0.72f;
        private const float PierDeckThickness = 0.12f;
        private const float PierPileSpacing = 2.4f;
        private const float PierPileThickness = 0.26f;
        private const float PierRailHeight = 0.95f;
        private const float PierLateralFromCenter = 18f;

        // The rental hut, unchanged from its lake years, fronting the
        // esplanade's south edge so its door step meets the slabs.
        private const float HutFootprintDepth = 3.6f;
        private const float HutFootprintWidth = 2.8f;
        private const float HutWallHeight = 2.35f;
        private const float HutZOffset = 11.5f;
        private const float HutLateralFromPier = 7.4f;
        private const float HutSignGlyphHeight = 0.34f;

        private const float BoatLength = 4.1f;
        private const float BoatBeam = 1.35f;
        private const float BoatRowPitch = 2.9f;
        private const float BoatRowZOffset = 3.5f;
        private const int BoatTarget = 7;

        private const float SlipwayWidth = 4.0f;

        // The two authored gaps in the sea wall, each exactly as wide
        // as the thing that bridges it plus a hand's clearance — the
        // lake revetment's contract, restated for a straight wall.
        private const float PierGapWidth = PierWidth + 0.30f;
        private const float SlipwayGapWidth = SlipwayWidth + 0.30f;

        // The mol: a concrete breakwater shielding the mouth, deck
        // level well above the swell, parapets on both long edges and
        // across the head, the root end open to its own stair.
        private const float MolOffsetFromChannel = 36f;
        private const float MolWidth = 3.2f;
        private const float MolRootOffset = 6f;
        private const float MolReach = 16f;
        private const float MolDeckAboveSea = 1.47f;
        private const float MolDeckThickness = 0.14f;
        private const float MolParapetHeight = 0.55f;
        private const float MolParapetThickness = 0.24f;

        // The footbridge over the mouth, on the esplanade's own axis:
        // the one stitch that makes the west and east shores one walk.
        private const float FootbridgeZOffset = 7.9f;
        private const float FootbridgeWidth = 2.2f;
        private const float FootbridgeRailHeight = 0.95f;
        private const float FootbridgeOverhang = 1.3f;

        // Steps never rise more than the safe step, less a margin.
        private const float StairMaximumRise = 0.24f;
        private const float StairTread = 0.55f;

        // How far walkable rectangles reach past their neighbours. The
        // walkable mask tests rectangles independently, so ground that
        // only abuts leaves a band two agent radii wide that nobody
        // can occupy; this is the lake's bank seam reach, for the same
        // reason.
        private const float SeamReach = 1.2f;

        // Where a pile's foot must reach before the real shelving bed
        // exists: safely below any bed the sea pass will author.
        private const float AssumedSeaBedDepth = 1.5f;

        // ASCII salts, one per decision, in the project convention.
        private const uint BoatSalt = 0x424F4154u;    // "BOAT"
        private const uint HullSalt = 0x48554C4Cu;    // "HULL"
        private const uint RuinSalt = 0x5255494Eu;    // "RUIN"
        private const uint PileSalt = 0x50494C45u;    // "PILE"
        private const uint DriftSalt = 0x44524654u;   // "DRFT"
        private const uint GrassSalt = 0x47525353u;   // "GRSS"

        internal const string PierDeckRootId = "seacoast-pier-deck-00";
        internal const string PierDeckHeadId = "seacoast-pier-deck-head";
        internal const string PierHeadBoardId = "seacoast-pier-headboard";
        internal const string MolDeckRootId = "seacoast-mol-deck-root";
        internal const string MolDeckHeadId = "seacoast-mol-deck-head";
        internal const string FootbridgeDeckWestId =
            "seacoast-footbridge-deck-west";
        internal const string FootbridgeDeckEastId =
            "seacoast-footbridge-deck-east";
        internal const string HutDoorId = "seacoast-hut-door";
        internal const string BenchIdPrefix = "seacoast-bench-";
        internal const string PromenadeStairWestId =
            "seacoast-promenade-stair-west";
        internal const string PromenadeStairEastId =
            "seacoast-promenade-stair-east";

        // The pedestrian lane's inset from a promenade edge, kept
        // equal to the pedestrian planner's AgentRadius + 0.1 so the
        // quay stairs land exactly under the lane that descends them.
        internal const float PromenadeLaneInset = 0.45f;

        /// <summary>
        /// Returns null when the layout carries no dressable seacoast:
        /// no waterfront sand or sea, no street access, no river to
        /// cut the mouth, or zones too narrow to hold their dressing
        /// (the same silent bail the lake planner uses).
        /// </summary>
        public static CitySeacoastPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!TryCreateSetup(
                    layout,
                    out CitySeacoastFrame frame,
                    out Rect grounds,
                    out CityOpenAreaAccessDescriptor access))
            {
                return null;
            }

            var parts = new List<CitySeacoastPartDescriptor>(420);
            var lamps = new List<CitySeacoastLampDescriptor>(8);
            var reserved = new List<Rect>();
            var wallCuts = new List<Vector2>();

            // Fixtures and buildings claim their spots first, so the
            // hulls and the scatter are planned around them and never
            // through them. This is the cemetery's order, then the
            // lake's, and now the coast's.
            float pierLateral = ResolvePierLateral(frame);
            bool hasHut = AddHut(
                parts, lamps, layout, frame, pierLateral, reserved, access);
            AddPier(parts, lamps, layout, frame, pierLateral, reserved,
                wallCuts);
            AddSlipway(parts, layout, frame, pierLateral, reserved,
                wallCuts);
            AddEsplanade(parts, layout, frame);
            AddSeaWall(parts, frame, wallCuts);
            AddBenches(parts, layout, frame, reserved);
            AddEsplanadeLamps(lamps, layout, frame, reserved);
            AddBoats(parts, layout, frame, pierLateral, layout.Seed,
                hasHut, reserved, access);
            Rect molRect = AddMol(
                parts, lamps, layout, frame, reserved);
            AddMouthSill(parts, frame);
            AddDerrick(parts, layout, frame, molRect, reserved);
            AddPortRuins(parts, layout, frame, layout.Seed, reserved,
                access);
            AddFootbridge(parts, layout, frame);
            AddPromenadeStairs(parts, layout, frame);
            AddWildShore(parts, layout, frame, layout.Seed, reserved);

            var plan = new CitySeacoastPlan(parts, lamps, grounds, frame);
            ValidateOrThrow(layout, plan);
            return plan;
        }

        /// <summary>
        /// The coast's contribution to the walkable mask: the decks it
        /// builds out over water the blueprint marks unwalkable — the
        /// mol, the pier, and the footbridge over the river channel's
        /// cut through the sand row. The open sea itself is never
        /// added: the player is clamped at the waterline everywhere a
        /// deck does not carry them past it.
        ///
        /// Every rectangle overlaps the sand it starts from by more
        /// than <see cref="SeamReach"/>, because adjacent rectangles
        /// are tested independently and ground that merely abuts
        /// leaves a dead band two agent radii wide.
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
                    out CitySeacoastFrame frame,
                    out Rect _,
                    out CityOpenAreaAccessDescriptor _))
            {
                return;
            }

            float waterline = frame.WaterlineZ;
            float pierLateral = ResolvePierLateral(frame);
            destination.Add(Rect.MinMaxRect(
                pierLateral - PierWidth * 0.5f,
                waterline - PierRootDepth,
                pierLateral + PierWidth * 0.5f,
                waterline + PierReach));

            float molX = ResolveMolLateral(frame);
            destination.Add(Rect.MinMaxRect(
                molX - MolWidth * 0.5f,
                waterline - MolRootOffset,
                molX + MolWidth * 0.5f,
                waterline + MolReach));

            destination.Add(Rect.MinMaxRect(
                frame.ChannelXMin - FootbridgeOverhang - SeamReach,
                waterline - FootbridgeZOffset - FootbridgeWidth * 0.5f,
                frame.ChannelXMax + FootbridgeOverhang + SeamReach,
                waterline - FootbridgeZOffset + FootbridgeWidth * 0.5f));

            // The quay junctions. The promenade rectangles and the
            // sand row merely abut at the waterfront boundary, and
            // abutting rectangles leave a dead band two agent radii
            // wide — so each junction gets a bridging strip across
            // the seam, under the stair that walks it.
            for (int index = 0;
                 index < layout.River.Promenades.Count;
                 index++)
            {
                CityRiverPromenadeDescriptor promenade =
                    layout.River.Promenades[index];
                float laneX = promenade.WestBank
                    ? promenade.Bounds.xMin + PromenadeLaneInset
                    : promenade.Bounds.xMax - PromenadeLaneInset;
                destination.Add(Rect.MinMaxRect(
                    laneX - 1.1f,
                    promenade.Bounds.yMax - SeamReach,
                    laneX + 1.1f,
                    promenade.Bounds.yMax + SeamReach));
            }
        }

        /// <summary>
        /// Whether the layout carries a dressable seacoast at all —
        /// the cheap question the river's rail pass asks before it
        /// seals the promenades' north ends: when the coast joins
        /// them, the seal comes off and the quay stairs bridge the
        /// step instead.
        /// </summary>
        internal static bool HasDressableSeacoast(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return TryCreateSetup(
                layout,
                out CitySeacoastFrame _,
                out Rect _,
                out CityOpenAreaAccessDescriptor _);
        }

        /// <summary>
        /// The height a person walks at on the shore: the sand's own
        /// top, lifted by the slab where the esplanade band covers it.
        /// The pedestrian lane samples its node heights here so the
        /// walkers stand on what the builder drew.
        /// </summary>
        internal static float SampleShoreWalkTop(
            CityLayout layout,
            in CitySeacoastFrame frame,
            float x,
            float z)
        {
            float top = SampleSandTop(layout, x, z);
            float bandNorth = frame.WaterlineZ - EsplanadeSetback;
            if (x >= frame.CenterZone.xMin + 0.4f &&
                x <= frame.CenterZone.xMax - 0.4f &&
                z <= bandNorth &&
                z >= bandNorth - EsplanadeDepth)
            {
                top += EsplanadeSlabLift;
            }

            return top;
        }

        /// <summary>
        /// Everything both the dressing pass and the navigation pass
        /// need to agree on. Shared rather than recomputed because the
        /// walkable mask is built before the plan is and must describe
        /// the same decks the world builder will draw.
        /// </summary>
        private static bool TryCreateSetup(
            CityLayout layout,
            out CitySeacoastFrame frame,
            out Rect grounds,
            out CityOpenAreaAccessDescriptor access)
        {
            frame = default;
            grounds = default;

            var sand = new List<CitySurfaceDescriptor>();
            var sea = new List<CitySurfaceDescriptor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Feature !=
                    CityAreaFeatureKind.NorthWaterfront)
                {
                    continue;
                }

                if (surface.Kind == CitySurfaceKind.Beach)
                {
                    sand.Add(surface);
                }
                else if (surface.Kind == CitySurfaceKind.Water)
                {
                    sea.Add(surface);
                }
            }

            if (sand.Count == 0 ||
                sea.Count == 0 ||
                !layout.River.IsEnabled ||
                !TryGetAccess(layout, out access))
            {
                access = default;
                return false;
            }

            Rect beachRow = sand[0].WorldBounds;
            for (int index = 1; index < sand.Count; index++)
            {
                beachRow = Union(beachRow, sand[index].WorldBounds);
            }

            Rect seaRow = sea[0].WorldBounds;
            for (int index = 1; index < sea.Count; index++)
            {
                seaRow = Union(seaRow, sea[index].WorldBounds);
            }

            // The river's last segment is the one that crosses the
            // sand row; its water bounds are the channel's cut.
            if (!TryGetMouthSegment(
                    layout,
                    beachRow,
                    out CityRiverSegmentDescriptor mouth))
            {
                return false;
            }

            float channelXMin = mouth.WaterBounds.xMin;
            float channelXMax = mouth.WaterBounds.xMax;
            float waterline = seaRow.yMin;
            float centerZoneMax = Mathf.Min(
                channelXMax + CenterZoneWidth,
                beachRow.xMax);
            if (channelXMin - beachRow.xMin < MinimumWestZoneWidth ||
                centerZoneMax - channelXMax < MinimumCenterZoneWidth)
            {
                return false;
            }

            float seaTop = sea[0].PhysicalTopY;
            float beachEdgeTop = SampleSandTop(
                layout,
                beachRow.center.x,
                waterline - 0.01f);
            frame = new CitySeacoastFrame(
                beachRow,
                seaRow,
                waterline,
                seaTop,
                beachEdgeTop,
                channelXMin,
                channelXMax,
                Rect.MinMaxRect(
                    beachRow.xMin, beachRow.yMin,
                    channelXMin, seaRow.yMax),
                Rect.MinMaxRect(
                    channelXMax, beachRow.yMin,
                    centerZoneMax, seaRow.yMax),
                Rect.MinMaxRect(
                    centerZoneMax, beachRow.yMin,
                    beachRow.xMax, seaRow.yMax));
            grounds = Union(beachRow, seaRow);
            return true;
        }

        private static float ResolvePierLateral(
            in CitySeacoastFrame frame)
        {
            return Mathf.Min(
                frame.CenterZone.xMin + PierLateralFromCenter,
                frame.CenterZone.xMax - PierWidth - 6f);
        }

        private static float ResolveMolLateral(
            in CitySeacoastFrame frame)
        {
            float candidate = frame.ChannelXMin - MolOffsetFromChannel;
            if (candidate - MolWidth * 0.5f <
                frame.WestZone.xMin + 10f)
            {
                candidate = frame.WestZone.center.x;
            }

            return candidate;
        }

        public static void ValidateOrThrow(
            CityLayout layout,
            CitySeacoastPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Count > CitySeacoastPlan.MaximumPartCount)
            {
                throw new InvalidOperationException(
                    "Seacoast dressing exceeds its bounded part count.");
            }

            CitySeacoastFrame frame = plan.Frame;
            if (frame.WaterlineZ <= frame.BeachRowBounds.yMin ||
                frame.SeaTopY >= frame.BeachEdgeTopY ||
                frame.ChannelXMax <= frame.ChannelXMin)
            {
                throw new InvalidOperationException(
                    "The seacoast frame must put the sand above the " +
                    "sea and the mouth between its banks.");
            }

            Rect containment = Expand(plan.Grounds, 0.65f);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            float bargeMinimumY = float.PositiveInfinity;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = plan.Parts[index];
                if (string.IsNullOrWhiteSpace(part.StableId) ||
                    !ids.Add(part.StableId) ||
                    !IsPositiveFinite(part.Size) ||
                    !IsFinite(part.Center) ||
                    !IsFinite(part.Rotation))
                {
                    throw new InvalidOperationException(
                        "Seacoast parts require unique IDs and finite " +
                        "positive transforms.");
                }

                Rect footprint = ToXZRect(part);
                if (footprint.xMin < containment.xMin ||
                    footprint.xMax > containment.xMax ||
                    footprint.yMin < containment.yMin ||
                    footprint.yMax > containment.yMax)
                {
                    throw new InvalidOperationException(
                        $"Seacoast part '{part.StableId}' leaves the " +
                        "coast precinct.");
                }

                // Nothing stands in the sea that was not authored to.
                if (!MayStandInSea(part.Kind) &&
                    footprint.yMax > frame.WaterlineZ + 0.001f)
                {
                    throw new InvalidOperationException(
                        $"Seacoast part '{part.StableId}' stands in " +
                        "open sea.");
                }

                // Hire boats are hauled up, never afloat, and the
                // driftwood was thrown clear by the water: both stay
                // strictly on the sand.
                if ((part.Kind == CitySeacoastPartKind.Boat ||
                     part.Kind == CitySeacoastPartKind.Driftwood) &&
                    footprint.yMax > frame.WaterlineZ - 0.05f)
                {
                    throw new InvalidOperationException(
                        $"Seacoast part '{part.StableId}' reaches the " +
                        "water; hulls and driftwood lie on the sand.");
                }

                if (part.Kind == CitySeacoastPartKind.PierDeck &&
                    part.Center.y - part.Size.y * 0.5f <
                        frame.SeaTopY + 0.40f)
                {
                    throw new InvalidOperationException(
                        $"Seacoast deck '{part.StableId}' lies too " +
                        "close to the water to walk over it.");
                }

                if ((part.Kind == CitySeacoastPartKind.PierPile ||
                     part.Kind == CitySeacoastPartKind.RottenPile) &&
                    footprint.yMin > frame.WaterlineZ &&
                    GetMinimumWorldY(part) >
                        frame.SeaTopY - AssumedSeaBedDepth + 0.30f)
                {
                    throw new InvalidOperationException(
                        $"Seacoast pile '{part.StableId}' does not " +
                        "reach the bed.");
                }

                if (part.Kind == CitySeacoastPartKind.Barge)
                {
                    bargeMinimumY = Mathf.Min(
                        bargeMinimumY,
                        GetMinimumWorldY(part));
                }

                ValidateZoneContainment(part, footprint, frame);

                if (!part.BlocksMovement ||
                    GetMinimumWorldY(part) >=
                        frame.BeachEdgeTopY + 2.1f)
                {
                    continue;
                }

                for (int accessIndex = 0;
                     accessIndex < layout.OpenAreaAccesses.Count;
                     accessIndex++)
                {
                    CityOpenAreaAccessDescriptor access =
                        layout.OpenAreaAccesses[accessIndex];
                    if (access.Feature !=
                        CityAreaFeatureKind.NorthWaterfront)
                    {
                        continue;
                    }

                    if (OverlapsStrict(
                            footprint,
                            Expand(
                                access.ApproachBounds,
                                AccessClearance)))
                    {
                        throw new InvalidOperationException(
                            $"Seacoast part '{part.StableId}' blocks " +
                            "its canonical street approach.");
                    }
                }
            }

            // The barge sits on the bottom, not at anchor: her hull's
            // lowest plate reaches below the water even though her
            // deck and house ride above it.
            if (plan.GetCount(CitySeacoastPartKind.Barge) > 0 &&
                bargeMinimumY > frame.SeaTopY - 0.20f)
            {
                throw new InvalidOperationException(
                    "The barge floats; she is stranded on the " +
                    "bottom, not riding at anchor.");
            }

            ValidateSeaWallContinuity(plan);
            ValidateMolParapetContinuity(plan);
            ValidateFootbridgeRails(plan);
            ValidateWalkwaysAreClear(plan);
            ValidateLamps(plan);

            // A full row shows the whole vocabulary, the way the
            // cemetery's nearest grave row and the lake's did.
            if (plan.BoatCount >= 4)
            {
                for (int variant = 0; variant < 4; variant++)
                {
                    if (plan.GetBoatVariantCount(
                            (CitySeacoastBoatVariant)variant) == 0)
                    {
                        throw new InvalidOperationException(
                            "A full boat row shows every authored " +
                            "hull.");
                    }
                }
            }
        }

        private static void ValidateZoneContainment(
            in CitySeacoastPartDescriptor part,
            Rect footprint,
            in CitySeacoastFrame frame)
        {
            Vector2 center = footprint.center;
            switch (part.Kind)
            {
                case CitySeacoastPartKind.MolBlock:
                case CitySeacoastPartKind.MolDeck:
                case CitySeacoastPartKind.MolParapet:
                case CitySeacoastPartKind.MolStair:
                case CitySeacoastPartKind.BeaconTower:
                case CitySeacoastPartKind.DerrickCrane:
                case CitySeacoastPartKind.PortRuin:
                    RequireZone(part, center, frame.WestZone, "west");
                    break;
                case CitySeacoastPartKind.EsplanadeSlab:
                case CitySeacoastPartKind.EsplanadeParapet:
                case CitySeacoastPartKind.Bench:
                case CitySeacoastPartKind.PierPile:
                case CitySeacoastPartKind.PierBeam:
                case CitySeacoastPartKind.PierDeck:
                case CitySeacoastPartKind.PierRail:
                case CitySeacoastPartKind.Hut:
                case CitySeacoastPartKind.HutSign:
                case CitySeacoastPartKind.Slipway:
                case CitySeacoastPartKind.Bollard:
                    RequireZone(part, center, frame.CenterZone, "center");
                    break;
                case CitySeacoastPartKind.RottenPile:
                case CitySeacoastPartKind.Barge:
                case CitySeacoastPartKind.BluffStair:
                    RequireZone(part, center, frame.EastZone, "east");
                    break;
                case CitySeacoastPartKind.MouthSill:
                case CitySeacoastPartKind.FootbridgeDeck:
                case CitySeacoastPartKind.FootbridgePile:
                case CitySeacoastPartKind.FootbridgeRail:
                    if (center.x < frame.ChannelXMin - 3f ||
                        center.x > frame.ChannelXMax + 3f)
                    {
                        throw new InvalidOperationException(
                            $"Seacoast part '{part.StableId}' belongs " +
                            "over the river mouth.");
                    }

                    break;
            }
        }

        private static void RequireZone(
            in CitySeacoastPartDescriptor part,
            Vector2 center,
            Rect zone,
            string name)
        {
            if (!zone.Contains(center))
            {
                throw new InvalidOperationException(
                    $"Seacoast part '{part.StableId}' stands outside " +
                    $"its {name} zone.");
            }
        }

        /// <summary>
        /// The centre zone's waterline is either walled or bridged:
        /// every metre is covered by a sea-wall board or by one of the
        /// two authored gaps, each closed by geometry a person can see
        /// — the pier deck over one, the slipway ramp and its chain
        /// over the other. The lake's perimeter contract, restated for
        /// a straight shore.
        /// </summary>
        private static void ValidateSeaWallContinuity(
            CitySeacoastPlan plan)
        {
            if (plan.GetCount(
                    CitySeacoastPartKind.EsplanadeParapet) == 0)
            {
                return;
            }

            CitySeacoastFrame frame = plan.Frame;
            float span = (frame.CenterZone.xMax - 0.5f) -
                         (frame.CenterZone.xMin + 0.5f);
            float walled = 0f;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = plan.Parts[index];
                if (part.Kind ==
                    CitySeacoastPartKind.EsplanadeParapet)
                {
                    walled += Mathf.Max(part.Size.x, part.Size.z);
                }
            }

            float bridged =
                (plan.GetCount(CitySeacoastPartKind.PierDeck) > 0
                    ? PierGapWidth
                    : 0f) +
                (plan.GetCount(CitySeacoastPartKind.Slipway) > 0
                    ? SlipwayGapWidth
                    : 0f);
            if (walled + bridged < span - 0.05f)
            {
                throw new InvalidOperationException(
                    $"The esplanade waterline is {span:F2} m but only " +
                    $"{walled + bridged:F2} m of it is walled or " +
                    "bridged; the rest would be an invisible edge.");
            }
        }

        /// <summary>
        /// The mol's raised deck has no invisible edges either: both
        /// long sides and the head are parapeted, and the one open end
        /// — the root — is bridged by its own visible stair.
        /// </summary>
        private static void ValidateMolParapetContinuity(
            CitySeacoastPlan plan)
        {
            if (plan.GetCount(CitySeacoastPartKind.MolDeck) == 0)
            {
                return;
            }

            float span = MolRootOffset + MolReach;
            float perimeter = span * 2f + MolWidth * 2f;
            float covered = 0f;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = plan.Parts[index];
                if (part.Kind == CitySeacoastPartKind.MolParapet)
                {
                    covered += Mathf.Max(part.Size.x, part.Size.z);
                }
            }

            float bridged =
                plan.GetCount(CitySeacoastPartKind.MolStair) > 0
                    ? MolWidth
                    : 0f;
            if (covered + bridged < perimeter - 0.05f)
            {
                throw new InvalidOperationException(
                    $"The mol deck's edge is {perimeter:F2} m but " +
                    $"only {covered + bridged:F2} m of it is " +
                    "parapeted or bridged by its stair.");
            }
        }

        /// <summary>
        /// The footbridge keeps a full-length rail on both sides. Its
        /// deck hangs over the mouth's open water; a missing rail is
        /// the same invisible edge the sea wall and the parapets
        /// refuse everywhere else.
        /// </summary>
        private static void ValidateFootbridgeRails(
            CitySeacoastPlan plan)
        {
            if (plan.GetCount(CitySeacoastPartKind.FootbridgeDeck) == 0)
            {
                return;
            }

            CitySeacoastFrame frame = plan.Frame;
            float span = frame.ChannelXMax - frame.ChannelXMin +
                         FootbridgeOverhang * 2f;
            int fullRails = 0;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = plan.Parts[index];
                if (part.Kind == CitySeacoastPartKind.FootbridgeRail &&
                    Mathf.Max(part.Size.x, part.Size.z) >= span - 0.7f)
                {
                    fullRails++;
                }
            }

            if (fullRails < 2)
            {
                throw new InvalidOperationException(
                    "The footbridge carries a full rail on each side.");
            }
        }

        /// <summary>
        /// Nothing solid stands across a deck a person walks: the
        /// pier, the mol (short of its beacon pocket at the head) and
        /// the footbridge. A wall the walkable mask cannot see is the
        /// invisible stop this precinct exists to refuse.
        /// </summary>
        private static void ValidateWalkwaysAreClear(
            CitySeacoastPlan plan)
        {
            CitySeacoastFrame frame = plan.Frame;
            var decks = new List<Rect>(3);
            if (plan.GetCount(CitySeacoastPartKind.PierDeck) > 0)
            {
                float pierLateral = ResolvePierLateral(frame);
                decks.Add(Rect.MinMaxRect(
                    pierLateral - PierWidth * 0.5f,
                    frame.WaterlineZ - PierRootDepth,
                    pierLateral + PierWidth * 0.5f,
                    frame.WaterlineZ + PierReach));
            }

            if (plan.GetCount(CitySeacoastPartKind.MolDeck) > 0)
            {
                float molX = ResolveMolLateral(frame);
                decks.Add(Rect.MinMaxRect(
                    molX - MolWidth * 0.5f,
                    frame.WaterlineZ - MolRootOffset,
                    molX + MolWidth * 0.5f,
                    frame.WaterlineZ + MolReach - 2.2f));
            }

            if (plan.GetCount(CitySeacoastPartKind.FootbridgeDeck) > 0)
            {
                decks.Add(Rect.MinMaxRect(
                    frame.ChannelXMin - FootbridgeOverhang,
                    frame.WaterlineZ - FootbridgeZOffset -
                        FootbridgeWidth * 0.5f,
                    frame.ChannelXMax + FootbridgeOverhang,
                    frame.WaterlineZ - FootbridgeZOffset +
                        FootbridgeWidth * 0.5f));
            }

            if (decks.Count == 0)
            {
                return;
            }

            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = plan.Parts[index];
                bool offender =
                    part.Kind == CitySeacoastPartKind.Hut ||
                    part.Kind == CitySeacoastPartKind.HutSign ||
                    part.Kind == CitySeacoastPartKind.Boat ||
                    part.Kind == CitySeacoastPartKind.BoatRest ||
                    part.Kind == CitySeacoastPartKind.Slipway ||
                    part.Kind == CitySeacoastPartKind.Bollard ||
                    part.Kind == CitySeacoastPartKind.Bench ||
                    part.Kind == CitySeacoastPartKind.PortRuin ||
                    part.Kind == CitySeacoastPartKind.DerrickCrane ||
                    part.Kind == CitySeacoastPartKind.Barge ||
                    part.Kind == CitySeacoastPartKind.RottenPile;
                if (!offender || !part.BlocksMovement)
                {
                    continue;
                }

                Rect footprint = ToXZRect(part);
                for (int deckIndex = 0;
                     deckIndex < decks.Count;
                     deckIndex++)
                {
                    if (OverlapsStrict(footprint, decks[deckIndex]))
                    {
                        throw new InvalidOperationException(
                            $"Seacoast part '{part.StableId}' stands " +
                            "across a walkway.");
                    }
                }
            }
        }

        private static void ValidateLamps(CitySeacoastPlan plan)
        {
            var lampIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Lamps.Count; index++)
            {
                CitySeacoastLampDescriptor lamp = plan.Lamps[index];
                if (string.IsNullOrWhiteSpace(lamp.StableId) ||
                    !lampIds.Add(lamp.StableId) ||
                    !IsFinite(lamp.GroundPosition) ||
                    !plan.Grounds.Contains(new Vector2(
                        lamp.GroundPosition.x,
                        lamp.GroundPosition.z)))
                {
                    throw new InvalidOperationException(
                        "Seacoast lamps require unique IDs and must " +
                        "stand inside the precinct.");
                }
            }

            // The hut lights its own doorway; a coast without the hut
            // hangs no bulb. The lake's contract, transplanted with
            // the hut it belongs to.
            bool hasHut = plan.GetCount(CitySeacoastPartKind.Hut) > 0;
            bool hasHutBulb =
                plan.GetLampCount(CitySeacoastLampKind.HutDoor) == 1;
            if (hasHut != hasHutBulb)
            {
                throw new InvalidOperationException(
                    "The rental hut and its door bulb stand or fall " +
                    "together.");
            }

            // The pier still carries the hand lamp somebody stood on
            // its rail years ago; the mol head carries the beacon.
            // One implies the other, both ways.
            bool hasPier =
                plan.GetCount(CitySeacoastPartKind.PierDeck) > 0;
            if (hasPier !=
                (plan.GetLampCount(CitySeacoastLampKind.PierHead) == 1))
            {
                throw new InvalidOperationException(
                    "A pier carries exactly one head lamp.");
            }

            bool hasMol =
                plan.GetCount(CitySeacoastPartKind.MolDeck) > 0;
            if (hasMol !=
                (plan.GetLampCount(CitySeacoastLampKind.Beacon) == 1))
            {
                throw new InvalidOperationException(
                    "The mol and its beacon stand or fall together.");
            }

            int esplanadeLamps =
                plan.GetLampCount(CitySeacoastLampKind.Esplanade);
            if (plan.GetCount(CitySeacoastPartKind.EsplanadeSlab) > 0 &&
                (esplanadeLamps < 3 || esplanadeLamps > 8))
            {
                throw new InvalidOperationException(
                    "The esplanade keeps between three and eight " +
                    "lamps: enough to walk by, too few to call it " +
                    "maintained.");
            }
        }

        // ------------------------------------------------------------------
        // the esplanade
        // ------------------------------------------------------------------

        private static void AddEsplanade(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame)
        {
            float bandNorth = frame.WaterlineZ - EsplanadeSetback;
            float bandCenter = bandNorth - EsplanadeDepth * 0.5f;
            float from = frame.CenterZone.xMin + 0.4f;
            float to = frame.CenterZone.xMax - 0.4f;
            int slabs = Mathf.Max(
                1,
                Mathf.RoundToInt((to - from) / EsplanadeSlabPitch));
            float pitch = (to - from) / slabs;
            for (int index = 0; index < slabs; index++)
            {
                float center = from + (index + 0.5f) * pitch;
                float top = SampleSandTop(layout, center, bandCenter) +
                            EsplanadeSlabLift;
                parts.Add(Part(
                    $"seacoast-esplanade-slab-{index:D2}",
                    CitySeacoastPartKind.EsplanadeSlab,
                    CitySeacoastStyle.Granite,
                    new Vector3(
                        center,
                        top - EsplanadeSlabThickness * 0.5f,
                        bandCenter),
                    Quaternion.identity,
                    new Vector3(
                        pitch - 0.04f,
                        EsplanadeSlabThickness,
                        EsplanadeDepth)));
            }
        }

        /// <summary>
        /// The sea wall: boards the centre zone's waterline except the
        /// two authored gaps, exactly as the lake revetment boarded
        /// its octagon. Each polygonal edge is one straight run here,
        /// which makes the tiling one-dimensional.
        /// </summary>
        private static void AddSeaWall(
            ICollection<CitySeacoastPartDescriptor> parts,
            in CitySeacoastFrame frame,
            IReadOnlyList<Vector2> cuts)
        {
            float from = frame.CenterZone.xMin + 0.5f;
            float to = frame.CenterZone.xMax - 0.5f;
            float wallZ = frame.WaterlineZ - SeaWallOffset;
            float bottom = frame.BeachEdgeTopY - SeaWallEmbedment;
            float top = frame.BeachEdgeTopY + SeaWallHeightAboveEdge;
            float height = top - bottom;

            var blocked = new List<Vector2>(cuts.Count);
            for (int index = 0; index < cuts.Count; index++)
            {
                float start = Mathf.Clamp(
                    cuts[index].x - cuts[index].y, from, to);
                float end = Mathf.Clamp(
                    cuts[index].x + cuts[index].y, from, to);
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
                        Mathf.Max(
                            blocked[index - 1].y,
                            blocked[index].y));
                    blocked.RemoveAt(index);
                }
            }

            int emitted = 0;
            float cursor = from;
            for (int gap = 0; gap <= blocked.Count; gap++)
            {
                float runEnd = gap < blocked.Count
                    ? blocked[gap].x
                    : to;
                float run = runEnd - cursor;
                if (run > 0.05f)
                {
                    int boards = Mathf.Max(
                        1,
                        Mathf.RoundToInt(run / SeaWallBoardLength));
                    float boardLength = run / boards;
                    for (int board = 0; board < boards; board++)
                    {
                        float middle =
                            cursor + (board + 0.5f) * boardLength;
                        parts.Add(Part(
                            $"seacoast-sea-wall-{emitted:D2}",
                            CitySeacoastPartKind.EsplanadeParapet,
                            CitySeacoastStyle.Granite,
                            new Vector3(
                                middle,
                                bottom + height * 0.5f,
                                wallZ),
                            Quaternion.identity,
                            new Vector3(
                                boardLength,
                                height,
                                SeaWallThickness)));
                        emitted++;
                    }
                }

                if (gap < blocked.Count)
                {
                    cursor = blocked[gap].y;
                }
            }
        }

        private static void AddBenches(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame,
            List<Rect> reserved)
        {
            float benchZ = frame.WaterlineZ - EsplanadeSetback - 0.5f;
            int accepted = 0;
            const int candidates = 6;
            for (int index = 0;
                 index < candidates && accepted < 4;
                 index++)
            {
                float t = (index + 0.5f) / candidates;
                float x = Mathf.Lerp(
                    frame.CenterZone.xMin + 5f,
                    frame.CenterZone.xMax - 5f,
                    t);
                Rect footprint = Rect.MinMaxRect(
                    x - 1.0f, benchZ - 0.5f,
                    x + 1.0f, benchZ + 0.5f);
                if (OverlapsAny(footprint, reserved, 0.4f))
                {
                    continue;
                }

                float top = SampleSandTop(layout, x, benchZ) +
                            EsplanadeSlabLift;
                string id = $"{BenchIdPrefix}{accepted:D2}";
                parts.Add(Part(
                    $"{id}-seat",
                    CitySeacoastPartKind.Bench,
                    CitySeacoastStyle.Planking,
                    new Vector3(x, top + 0.42f, benchZ),
                    Quaternion.identity,
                    new Vector3(1.7f, 0.10f, 0.48f)));
                parts.Add(Part(
                    $"{id}-back",
                    CitySeacoastPartKind.Bench,
                    CitySeacoastStyle.Planking,
                    new Vector3(x, top + 0.74f, benchZ - 0.26f),
                    Quaternion.Euler(-8f, 0f, 0f),
                    new Vector3(1.7f, 0.46f, 0.08f)));
                parts.Add(Part(
                    $"{id}-legs",
                    CitySeacoastPartKind.Bench,
                    CitySeacoastStyle.Iron,
                    new Vector3(x, top + 0.20f, benchZ),
                    Quaternion.identity,
                    new Vector3(1.5f, 0.40f, 0.40f)));
                reserved.Add(Expand(footprint, 0.4f));
                accepted++;
            }
        }

        private static void AddEsplanadeLamps(
            ICollection<CitySeacoastLampDescriptor> lamps,
            CityLayout layout,
            in CitySeacoastFrame frame,
            IReadOnlyList<Rect> reserved)
        {
            float lampZ = frame.WaterlineZ - EsplanadeSetback -
                          EsplanadeDepth + 0.5f;
            int accepted = 0;
            const int candidates = 5;
            for (int index = 0;
                 index < candidates && accepted < 4;
                 index++)
            {
                float t = (index + 0.5f) / candidates;
                float x = Mathf.Lerp(
                    frame.CenterZone.xMin + 7f,
                    frame.CenterZone.xMax - 7f,
                    t);
                Rect footprint = Rect.MinMaxRect(
                    x - 0.4f, lampZ - 0.4f,
                    x + 0.4f, lampZ + 0.4f);
                if (OverlapsAny(footprint, reserved, 0.3f))
                {
                    continue;
                }

                float top = SampleSandTop(layout, x, lampZ) +
                            EsplanadeSlabLift;
                lamps.Add(new CitySeacoastLampDescriptor(
                    $"seacoast-lamp-esplanade-{accepted:D2}",
                    CitySeacoastLampKind.Esplanade,
                    new Vector3(x, top, lampZ),
                    0f));
                accepted++;
            }
        }

        // ------------------------------------------------------------------
        // the boat station, plank for plank from the lake
        // ------------------------------------------------------------------

        private static bool AddHut(
            ICollection<CitySeacoastPartDescriptor> parts,
            ICollection<CitySeacoastLampDescriptor> lamps,
            CityLayout layout,
            in CitySeacoastFrame frame,
            float pierLateral,
            ICollection<Rect> reserved,
            CityOpenAreaAccessDescriptor access)
        {
            float hutX = pierLateral - HutLateralFromPier;
            float hutZ = frame.WaterlineZ - HutZOffset;
            if (hutX - HutFootprintWidth * 0.5f <
                frame.CenterZone.xMin + 1.0f)
            {
                hutX = pierLateral + HutLateralFromPier;
            }

            Rect footprint = Rect.MinMaxRect(
                hutX - HutFootprintWidth * 0.5f,
                hutZ - HutFootprintDepth * 0.5f,
                hutX + HutFootprintWidth * 0.5f,
                hutZ + HutFootprintDepth * 0.5f);
            if (!IsClearOfAccess(footprint, access))
            {
                return false;
            }

            float ground = SampleSandTop(layout, hutX, hutZ);
            float floorTop = ground + 0.16f;

            // The hut faces the esplanade, not the water: its counter
            // served people arriving, so the door, the step and the
            // board that says what this place was are the first thing
            // a visitor coming off the slabs sees.
            Quaternion rotation = Quaternion.identity;

            parts.Add(Part(
                "seacoast-hut-floor",
                CitySeacoastPartKind.Hut,
                CitySeacoastStyle.Planking,
                new Vector3(hutX, ground + 0.08f, hutZ),
                rotation,
                new Vector3(
                    HutFootprintWidth + 0.24f,
                    0.16f,
                    HutFootprintDepth + 0.24f)));

            parts.Add(Part(
                "seacoast-hut-back",
                CitySeacoastPartKind.Hut,
                CitySeacoastStyle.Concrete,
                new Vector3(
                    hutX,
                    floorTop + HutWallHeight * 0.5f,
                    hutZ - HutFootprintDepth * 0.5f + 0.09f),
                rotation,
                new Vector3(HutFootprintWidth, HutWallHeight, 0.18f)));
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"seacoast-hut-side-{side}",
                    CitySeacoastPartKind.Hut,
                    CitySeacoastStyle.Concrete,
                    new Vector3(
                        hutX + sign *
                            (HutFootprintWidth * 0.5f - 0.09f),
                        floorTop + HutWallHeight * 0.5f,
                        hutZ),
                    rotation,
                    new Vector3(0.18f, HutWallHeight, HutFootprintDepth)));
            }

            float frontZ = hutZ + HutFootprintDepth * 0.5f - 0.09f;
            const float doorWidth = 0.86f;
            float jamb = (HutFootprintWidth - doorWidth) * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"seacoast-hut-jamb-{side}",
                    CitySeacoastPartKind.Hut,
                    CitySeacoastStyle.Concrete,
                    new Vector3(
                        hutX + sign * (doorWidth + jamb) * 0.5f,
                        floorTop + HutWallHeight * 0.5f,
                        frontZ),
                    rotation,
                    new Vector3(jamb, HutWallHeight, 0.18f)));
            }

            parts.Add(Part(
                "seacoast-hut-lintel",
                CitySeacoastPartKind.Hut,
                CitySeacoastStyle.Concrete,
                new Vector3(
                    hutX,
                    floorTop + HutWallHeight - 0.20f,
                    frontZ),
                rotation,
                new Vector3(doorWidth, 0.40f, 0.18f)));

            // The door itself, left ajar against the jamb. Nobody
            // locked up; there was nothing left to lock up.
            parts.Add(Part(
                HutDoorId,
                CitySeacoastPartKind.Hut,
                CitySeacoastStyle.Planking,
                new Vector3(
                    hutX - doorWidth * 0.28f,
                    floorTop + (HutWallHeight - 0.40f) * 0.5f,
                    frontZ + 0.22f),
                Quaternion.Euler(0f, 34f, 0f),
                new Vector3(doorWidth, HutWallHeight - 0.40f, 0.06f)));

            parts.Add(Part(
                "seacoast-hut-step",
                CitySeacoastPartKind.Hut,
                CitySeacoastStyle.Concrete,
                new Vector3(
                    hutX,
                    ground + 0.08f,
                    hutZ + HutFootprintDepth * 0.5f + 0.22f),
                rotation,
                new Vector3(doorWidth + 0.4f, 0.16f, 0.44f)));

            parts.Add(Part(
                "seacoast-hut-roof",
                CitySeacoastPartKind.Hut,
                CitySeacoastStyle.Planking,
                new Vector3(
                    hutX,
                    floorTop + HutWallHeight + 0.09f,
                    hutZ + 0.24f),
                rotation,
                new Vector3(
                    HutFootprintWidth + 0.44f,
                    0.18f,
                    HutFootprintDepth + 0.92f)));

            parts.Add(Part(
                "seacoast-hut-pipe",
                CitySeacoastPartKind.Hut,
                CitySeacoastStyle.Iron,
                new Vector3(
                    hutX + HutFootprintWidth * 0.28f,
                    floorTop + HutWallHeight + 0.62f,
                    hutZ - 0.9f),
                rotation,
                new Vector3(0.16f, 0.94f, 0.16f)));

            // The counter hatch the boats were hired through, boarded
            // over with two planks nailed across it.
            for (int board = 0; board < 2; board++)
            {
                parts.Add(Part(
                    $"seacoast-hut-boarding-{board}",
                    CitySeacoastPartKind.Hut,
                    CitySeacoastStyle.Planking,
                    new Vector3(
                        hutX + (board == 0 ? -0.02f : 0.04f),
                        floorTop + 1.28f + board * 0.30f,
                        frontZ + 0.10f),
                    Quaternion.Euler(0f, 0f, board == 0 ? 7f : -5f),
                    new Vector3(
                        HutFootprintWidth - 0.2f,
                        0.16f,
                        0.05f)));
            }

            AddHutSign(parts, hutX, hutZ, floorTop);

            lamps.Add(new CitySeacoastLampDescriptor(
                "seacoast-lamp-hut-door",
                CitySeacoastLampKind.HutDoor,
                new Vector3(
                    hutX + doorWidth * 0.72f,
                    ground,
                    hutZ + HutFootprintDepth * 0.5f + 0.30f),
                0f));

            reserved.Add(Expand(footprint, 0.9f));
            return true;
        }

        private static void AddHutSign(
            ICollection<CitySeacoastPartDescriptor> parts,
            float hutX,
            float hutZ,
            float floorTop)
        {
            const string word = "ПРОКАТ ЛОДОК";
            float boardZ = hutZ + HutFootprintDepth * 0.5f + 0.48f;
            float boardTop = floorTop + HutWallHeight + 0.46f;
            float advance = HutSignGlyphHeight * 0.80f;
            float boardWidth = word.Length * advance + 0.34f;

            // The board itself, hanging off the eave, and a little out
            // of true: it has been up there a long time — first over
            // the pond, now over the sea.
            parts.Add(Part(
                "seacoast-hut-signboard",
                CitySeacoastPartKind.HutSign,
                CitySeacoastStyle.Planking,
                new Vector3(hutX, boardTop, boardZ),
                Quaternion.Euler(0f, 0f, -3.5f),
                new Vector3(
                    boardWidth,
                    HutSignGlyphHeight + 0.30f,
                    0.07f)));

            IReadOnlyList<SignSegmentRect> segments =
                CitySignLettering.Layout(
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
                    $"seacoast-hut-sign-{index:D2}",
                    CitySeacoastPartKind.HutSign,
                    CitySeacoastStyle.PaintAccent,
                    new Vector3(
                        hutX - rotatedLateral,
                        boardTop + rotatedHeight,
                        boardZ + 0.05f),
                    Quaternion.Euler(0f, 0f, -3.5f),
                    new Vector3(
                        Mathf.Max(0.02f, segment.Size.x),
                        Mathf.Max(0.02f, segment.Size.y),
                        0.03f)));
            }
        }

        private static void AddPier(
            ICollection<CitySeacoastPartDescriptor> parts,
            ICollection<CitySeacoastLampDescriptor> lamps,
            CityLayout layout,
            in CitySeacoastFrame frame,
            float pierLateral,
            ICollection<Rect> reserved,
            ICollection<Vector2> wallCuts)
        {
            float rootZ = frame.WaterlineZ - PierRootDepth;
            float headZ = frame.WaterlineZ + PierReach;
            float deckTop = frame.SeaTopY + PierDeckTopAboveSea;
            float deckCenterY = deckTop - PierDeckThickness * 0.5f;
            Quaternion rotation = Quaternion.identity;

            // Deck boards laid across the pier, one per metre-ish, so
            // the pier has a rhythm at walking speed. The root and
            // head boards are named: the fisherman's stance is read
            // off them.
            float span = headZ - rootZ;
            int deckCount = Mathf.Max(2, Mathf.RoundToInt(span / 0.9f));
            for (int index = 0; index < deckCount; index++)
            {
                float from = Mathf.Lerp(
                    rootZ, headZ, index / (float)deckCount);
                float to = Mathf.Lerp(
                    rootZ, headZ, (index + 1f) / deckCount);
                string id = index == 0
                    ? PierDeckRootId
                    : index == deckCount - 1
                        ? PierDeckHeadId
                        : $"seacoast-pier-deck-{index:D2}";
                parts.Add(Part(
                    id,
                    CitySeacoastPartKind.PierDeck,
                    CitySeacoastStyle.Planking,
                    new Vector3(
                        pierLateral,
                        deckCenterY,
                        (from + to) * 0.5f),
                    rotation,
                    new Vector3(
                        PierWidth,
                        PierDeckThickness,
                        to - from - 0.04f)));
            }

            // Bearers under the boards, running the length of the pier.
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"seacoast-pier-bearer-{side}",
                    CitySeacoastPartKind.PierBeam,
                    CitySeacoastStyle.TarredTimber,
                    new Vector3(
                        pierLateral + sign * (PierWidth * 0.5f - 0.14f),
                        deckTop - PierDeckThickness - 0.11f,
                        (rootZ + headZ) * 0.5f),
                    rotation,
                    new Vector3(0.20f, 0.22f, span)));
            }

            // Piles. Their feet go below the bed, not to it: a pile
            // that stops exactly at the floor shows a seam every time
            // the water moves.
            float pileBottom = frame.SeaTopY - AssumedSeaBedDepth - 0.2f;
            float pileHeight =
                deckTop - PierDeckThickness - pileBottom;
            int pairCount = Mathf.Max(
                2,
                Mathf.FloorToInt(PierReach / PierPileSpacing));
            for (int pair = 0; pair < pairCount; pair++)
            {
                float z = frame.WaterlineZ + 0.6f +
                          pair * PierPileSpacing;
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1f : 1f;
                    parts.Add(Part(
                        $"seacoast-pier-pile-{pair:D2}-{side}",
                        CitySeacoastPartKind.PierPile,
                        CitySeacoastStyle.TarredTimber,
                        new Vector3(
                            pierLateral + sign *
                                (PierWidth * 0.5f - 0.14f),
                            pileBottom + pileHeight * 0.5f,
                            z),
                        rotation,
                        new Vector3(
                            PierPileThickness,
                            pileHeight,
                            PierPileThickness)));
                }
            }

            // A rail down one side only. Both sides would make it a
            // bridge; one side makes it a working pier somebody tied
            // up against, and leaves the fisherman his open edge.
            float railLateral = pierLateral - PierWidth * 0.5f + 0.08f;
            int postCount = Mathf.Max(
                2,
                Mathf.FloorToInt(span / 2.4f) + 1);
            for (int index = 0; index < postCount; index++)
            {
                float z = Mathf.Lerp(
                    rootZ + 0.4f,
                    headZ - 0.3f,
                    index / (postCount - 1f));
                parts.Add(Part(
                    $"seacoast-pier-post-{index:D2}",
                    CitySeacoastPartKind.PierRail,
                    CitySeacoastStyle.TarredTimber,
                    new Vector3(
                        railLateral,
                        deckTop + PierRailHeight * 0.5f,
                        z),
                    rotation,
                    new Vector3(0.10f, PierRailHeight, 0.10f)));
            }

            parts.Add(Part(
                "seacoast-pier-rail",
                CitySeacoastPartKind.PierRail,
                CitySeacoastStyle.TarredTimber,
                new Vector3(
                    railLateral,
                    deckTop + PierRailHeight - 0.06f,
                    (rootZ + 0.4f + headZ - 0.3f) * 0.5f),
                rotation,
                new Vector3(0.08f, 0.10f, span - 0.7f)));

            // Head boards, closing the end. A pier you can walk off is
            // a hole, and a hole is the invisible edge this shore
            // refuses to build.
            parts.Add(Part(
                PierHeadBoardId,
                CitySeacoastPartKind.PierRail,
                CitySeacoastStyle.TarredTimber,
                new Vector3(
                    pierLateral,
                    deckTop + 0.36f,
                    headZ - 0.1f),
                rotation,
                new Vector3(PierWidth, 0.72f, 0.10f)));

            // The hand lamp somebody stood on the rail cap at the very
            // end of the boards and never came back for. It made the
            // move from the lake with everything else.
            lamps.Add(new CitySeacoastLampDescriptor(
                "seacoast-lamp-pier-head",
                CitySeacoastLampKind.PierHead,
                new Vector3(
                    railLateral,
                    deckTop + PierRailHeight - 0.01f,
                    headZ - 0.55f),
                180f));

            reserved.Add(Expand(
                Rect.MinMaxRect(
                    pierLateral - PierWidth * 0.5f,
                    rootZ,
                    pierLateral + PierWidth * 0.5f,
                    headZ),
                1.1f));
            wallCuts.Add(new Vector2(pierLateral, PierGapWidth * 0.5f));
        }

        private static void AddSlipway(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame,
            float pierLateral,
            ICollection<Rect> reserved,
            ICollection<Vector2> wallCuts)
        {
            float slipX = pierLateral + PierWidth * 0.5f +
                          BoatRowPitch * (BoatTarget + 1f) + 3.2f;
            if (slipX + SlipwayWidth >
                frame.CenterZone.xMax - 2f)
            {
                slipX = frame.CenterZone.xMax - SlipwayWidth - 2f;
            }

            float sandTop = SampleSandTop(
                layout,
                slipX,
                frame.WaterlineZ - 2.2f);

            // Three ramp slabs stepping up out of the water onto the
            // sand, each a little more silted than the last.
            for (int index = 0; index < 3; index++)
            {
                float z = frame.WaterlineZ + 1.2f - index * 1.7f;
                float top = Mathf.Lerp(
                    frame.SeaTopY + 0.10f,
                    sandTop + 0.10f,
                    (index + 1f) / 3f);
                parts.Add(Part(
                    $"seacoast-slipway-{index}",
                    CitySeacoastPartKind.Slipway,
                    CitySeacoastStyle.Concrete,
                    new Vector3(slipX, top - 0.09f, z),
                    Quaternion.identity,
                    new Vector3(SlipwayWidth, 0.18f, 1.7f)));
            }

            // Two bollards and the chain between them: the ramp is
            // closed, and it is closed by something a person can see.
            float chainZ = frame.WaterlineZ - SeaWallOffset;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"seacoast-bollard-{side}",
                    CitySeacoastPartKind.Bollard,
                    CitySeacoastStyle.Iron,
                    new Vector3(
                        slipX + sign * (SlipwayWidth * 0.5f - 0.2f),
                        frame.BeachEdgeTopY + 0.44f,
                        chainZ),
                    Quaternion.identity,
                    new Vector3(0.24f, 1.04f, 0.24f)));
            }

            parts.Add(Part(
                "seacoast-slipway-chain",
                CitySeacoastPartKind.Bollard,
                CitySeacoastStyle.Iron,
                new Vector3(
                    slipX,
                    frame.BeachEdgeTopY + 0.78f,
                    chainZ),
                Quaternion.identity,
                new Vector3(SlipwayWidth - 0.4f, 0.09f, 0.09f)));

            reserved.Add(Expand(
                Rect.MinMaxRect(
                    slipX - SlipwayWidth * 0.5f,
                    frame.WaterlineZ - 3.2f,
                    slipX + SlipwayWidth * 0.5f,
                    frame.WaterlineZ),
                0.9f));
            wallCuts.Add(new Vector2(slipX, SlipwayGapWidth * 0.5f));
        }

        private static void AddBoats(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame,
            float pierLateral,
            int seed,
            bool hasHut,
            IReadOnlyList<Rect> reserved,
            CityOpenAreaAccessDescriptor access)
        {
            var footprints = new List<Rect>(BoatTarget);

            // The row walks east from the pier along the sand strip
            // between the esplanade and the sea wall: dragged out of
            // the water and left where the last person to touch them
            // stopped pulling.
            float startLateral = pierLateral + PierWidth * 0.5f +
                                 BoatRowPitch;
            float rowZ = frame.WaterlineZ - BoatRowZOffset;
            int accepted = 0;
            for (int index = 0; index < BoatTarget * 2 &&
                                accepted < BoatTarget; index++)
            {
                float lateral = startLateral + index * BoatRowPitch;
                if (lateral > frame.CenterZone.xMax - BoatBeam - 1.2f)
                {
                    break;
                }

                uint hash = StableHash(seed, index, 0, BoatSalt);
                float lateralJitter =
                    ((hash & 0xFFFFu) / 65535f - 0.5f) * 0.5f;
                float depthJitter =
                    (((hash >> 16) & 0xFFFFu) / 65535f - 0.5f) * 0.4f;
                float yawJitter =
                    (((hash >> 8) & 0xFFu) / 255f - 0.5f) * 22f;

                float boatX = lateral + lateralJitter;
                float boatZ = rowZ + depthJitter;

                // The footprint is the hull's real extents: bows to
                // the water, so the length lies across Z.
                Rect footprint = Rect.MinMaxRect(
                    boatX - BoatBeam * 0.75f,
                    boatZ - BoatLength * 0.62f,
                    boatX + BoatBeam * 0.75f,
                    boatZ + BoatLength * 0.62f);
                if (OverlapsAny(footprint, reserved, 0f) ||
                    OverlapsAny(footprint, footprints, 0.10f) ||
                    !IsClearOfAccess(footprint, access) ||
                    footprint.yMax > frame.WaterlineZ - 0.85f)
                {
                    continue;
                }

                CitySeacoastBoatVariant variant = accepted < 4
                    ? (CitySeacoastBoatVariant)accepted
                    : (CitySeacoastBoatVariant)(
                        StableHash(seed, index, 1, HullSalt) % 4u);

                EmitBoat(
                    parts,
                    layout,
                    accepted,
                    variant,
                    boatX,
                    boatZ,
                    yawJitter,
                    hasHut && accepted == 0);
                footprints.Add(footprint);
                accepted++;
            }
        }

        private static void EmitBoat(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            int ordinal,
            CitySeacoastBoatVariant variant,
            float x,
            float z,
            float yawJitter,
            bool withOar)
        {
            // Every hull is upside down on two rests, which is how a
            // hire boat is stored and why the row reads as a row of
            // backs rather than a row of tubs. Keel-up: a wide gunwale
            // on the ground, two panels sloping up and in, the bottom
            // as a ridge along the sky.
            float ground = SampleSandTop(layout, x, z);
            float yaw = 90f + yawJitter;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            float length = BoatLength;
            float beam = BoatBeam;
            float depthOfHull = 0.54f;
            CitySeacoastStyle paint = CitySeacoastStyle.HullPaint;
            switch (variant)
            {
                case CitySeacoastBoatVariant.RoundHullDinghy:
                    length = BoatLength * 0.86f;
                    beam = BoatBeam * 1.12f;
                    depthOfHull = 0.60f;
                    break;
                case CitySeacoastBoatVariant.StavedPunt:
                    length = BoatLength * 1.12f;
                    beam = BoatBeam * 0.88f;
                    depthOfHull = 0.44f;
                    break;
                case CitySeacoastBoatVariant.HolledWreck:
                    // The one nobody bothered to turn over properly.
                    paint = CitySeacoastStyle.HullTar;
                    depthOfHull = 0.48f;
                    break;
            }

            Vector3 hullBase = new Vector3(x, ground + 0.18f, z);
            for (int rest = 0; rest < 2; rest++)
            {
                float offset = (rest == 0 ? -1f : 1f) * length * 0.28f;
                parts.Add(BoatPart(
                    $"seacoast-boat-{ordinal:D2}-rest-{rest}",
                    CitySeacoastPartKind.BoatRest,
                    CitySeacoastStyle.TarredTimber,
                    new Vector3(x, ground + 0.09f, z) +
                        rotation * new Vector3(offset, 0f, 0f),
                    rotation,
                    new Vector3(0.22f, 0.18f, beam + 0.3f),
                    ordinal,
                    variant));
            }

            float gunwaleY = 0.04f;
            float ridgeY = depthOfHull;
            float gunwaleZ = beam * 0.5f;
            float ridgeZ = beam * 0.17f;

            Vector3 Place(float height, float across)
            {
                return hullBase +
                       rotation * new Vector3(0f, height, across);
            }

            float riseZ = gunwaleZ - ridgeZ;
            float riseY = ridgeY - gunwaleY;
            float panelSpan = Mathf.Sqrt(
                riseZ * riseZ + riseY * riseY);
            float panelTilt =
                Mathf.Atan2(riseY, riseZ) * Mathf.Rad2Deg;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(BoatPart(
                    $"seacoast-boat-{ordinal:D2}-side-{side}",
                    CitySeacoastPartKind.Boat,
                    paint,
                    Place(
                        (gunwaleY + ridgeY) * 0.5f,
                        sign * (gunwaleZ + ridgeZ) * 0.5f),
                    Quaternion.Euler(sign * panelTilt, yaw, 0f),
                    new Vector3(length * 0.97f, 0.08f, panelSpan),
                    ordinal,
                    variant));

                parts.Add(BoatPart(
                    $"seacoast-boat-{ordinal:D2}-gunwale-{side}",
                    CitySeacoastPartKind.Boat,
                    paint,
                    Place(gunwaleY, sign * gunwaleZ),
                    rotation,
                    new Vector3(length, 0.12f, 0.11f),
                    ordinal,
                    variant));
            }

            parts.Add(BoatPart(
                $"seacoast-boat-{ordinal:D2}-bottom",
                CitySeacoastPartKind.Boat,
                paint,
                Place(ridgeY, 0f),
                rotation,
                new Vector3(length * 0.94f, 0.09f, ridgeZ * 2f),
                ordinal,
                variant));

            parts.Add(BoatPart(
                $"seacoast-boat-{ordinal:D2}-end-0",
                CitySeacoastPartKind.Boat,
                paint,
                hullBase + rotation * new Vector3(
                    -length * 0.47f, (gunwaleY + ridgeY) * 0.5f, 0f),
                rotation,
                new Vector3(0.09f, ridgeY - gunwaleY, beam * 0.82f),
                ordinal,
                variant));
            parts.Add(BoatPart(
                $"seacoast-boat-{ordinal:D2}-end-1",
                CitySeacoastPartKind.Boat,
                paint,
                hullBase + rotation * new Vector3(
                    length * 0.47f, (gunwaleY + ridgeY) * 0.52f, 0f),
                rotation,
                new Vector3(0.09f, ridgeY - gunwaleY, beam * 0.42f),
                ordinal,
                variant));

            parts.Add(BoatPart(
                $"seacoast-boat-{ordinal:D2}-keel",
                CitySeacoastPartKind.Boat,
                CitySeacoastStyle.HullTar,
                Place(ridgeY + 0.07f, 0f),
                rotation,
                new Vector3(length * 0.90f, 0.08f, 0.13f),
                ordinal,
                variant));

            // One oar left leaning on the hull nearest the hut: the
            // detail that says people, not props.
            if (withOar)
            {
                parts.Add(Part(
                    $"seacoast-boat-{ordinal:D2}-oar",
                    CitySeacoastPartKind.Debris,
                    CitySeacoastStyle.Litter,
                    Place(ridgeY * 0.55f, beam * 0.74f) +
                        rotation * new Vector3(length * 0.2f, 0f, 0f),
                    Quaternion.Euler(0f, yaw + 62f, 71f),
                    new Vector3(1.9f, 0.09f, 0.13f)));
            }
        }

        // ------------------------------------------------------------------
        // the dead port
        // ------------------------------------------------------------------

        private static Rect AddMol(
            ICollection<CitySeacoastPartDescriptor> parts,
            ICollection<CitySeacoastLampDescriptor> lamps,
            CityLayout layout,
            in CitySeacoastFrame frame,
            ICollection<Rect> reserved)
        {
            float molX = ResolveMolLateral(frame);
            float rootZ = frame.WaterlineZ - MolRootOffset;
            float headZ = frame.WaterlineZ + MolReach;
            float deckTop = frame.SeaTopY + MolDeckAboveSea;
            Quaternion rotation = Quaternion.identity;

            // Concrete courses from below any bed the sea will get up
            // to the deck's underside, in chunk-splittable segments.
            float blockBottom = frame.SeaTopY - AssumedSeaBedDepth -
                                0.25f;
            float blockTop = deckTop - MolDeckThickness;
            int blockCount = Mathf.CeilToInt(
                (headZ - rootZ) / 5.5f);
            float blockLength = (headZ - rootZ) / blockCount;
            for (int index = 0; index < blockCount; index++)
            {
                float center = rootZ + (index + 0.5f) * blockLength;
                parts.Add(Part(
                    $"seacoast-mol-block-{index:D2}",
                    CitySeacoastPartKind.MolBlock,
                    CitySeacoastStyle.Concrete,
                    new Vector3(
                        molX,
                        (blockBottom + blockTop) * 0.5f,
                        center),
                    rotation,
                    new Vector3(
                        MolWidth,
                        blockTop - blockBottom,
                        blockLength - 0.06f)));
            }

            // Deck boards — concrete plates with a walking rhythm.
            int deckCount = Mathf.Max(
                2,
                Mathf.RoundToInt((headZ - rootZ) / 1.05f));
            for (int index = 0; index < deckCount; index++)
            {
                float from = Mathf.Lerp(
                    rootZ, headZ, index / (float)deckCount);
                float to = Mathf.Lerp(
                    rootZ, headZ, (index + 1f) / deckCount);
                string id = index == 0
                    ? MolDeckRootId
                    : index == deckCount - 1
                        ? MolDeckHeadId
                        : $"seacoast-mol-deck-{index:D2}";
                parts.Add(Part(
                    id,
                    CitySeacoastPartKind.MolDeck,
                    CitySeacoastStyle.Concrete,
                    new Vector3(
                        molX,
                        deckTop - MolDeckThickness * 0.5f,
                        (from + to) * 0.5f),
                    rotation,
                    new Vector3(
                        MolWidth,
                        MolDeckThickness,
                        to - from - 0.03f)));
            }

            // Parapets: both long edges full length, and across the
            // head behind the beacon. The root end stays open to its
            // own stair.
            float parapetCenterY = deckTop + MolParapetHeight * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                float edgeX = molX + sign *
                              (MolWidth * 0.5f - MolParapetThickness * 0.5f);
                int runCount = Mathf.CeilToInt((headZ - rootZ) / 3f);
                float runLength = (headZ - rootZ) / runCount;
                for (int index = 0; index < runCount; index++)
                {
                    float center = rootZ + (index + 0.5f) * runLength;
                    parts.Add(Part(
                        $"seacoast-mol-parapet-{side}-{index:D2}",
                        CitySeacoastPartKind.MolParapet,
                        CitySeacoastStyle.Concrete,
                        new Vector3(edgeX, parapetCenterY, center),
                        rotation,
                        new Vector3(
                            MolParapetThickness,
                            MolParapetHeight,
                            runLength)));
                }
            }

            parts.Add(Part(
                "seacoast-mol-parapet-head",
                CitySeacoastPartKind.MolParapet,
                CitySeacoastStyle.Concrete,
                new Vector3(
                    molX,
                    parapetCenterY,
                    headZ - MolParapetThickness * 0.5f),
                rotation,
                new Vector3(
                    MolWidth,
                    MolParapetHeight,
                    MolParapetThickness)));

            // The stair up from the sand at the root: the mol's one
            // entry, and the visible bridge over its one parapet gap.
            float sand = SampleSandTop(layout, molX, rootZ - 0.6f);
            float rise = deckTop - sand;
            int stepCount = Mathf.Max(
                2,
                Mathf.CeilToInt(rise / StairMaximumRise));
            float stepRise = rise / stepCount;
            for (int index = 0; index < stepCount - 1; index++)
            {
                float top = deckTop - (index + 1) * stepRise;
                parts.Add(Part(
                    $"seacoast-mol-stair-{index:D2}",
                    CitySeacoastPartKind.MolStair,
                    CitySeacoastStyle.Concrete,
                    new Vector3(
                        molX,
                        top - 0.15f,
                        rootZ - 0.28f - index * StairTread),
                    rotation,
                    new Vector3(
                        MolWidth - 0.5f,
                        0.30f,
                        StairTread + 0.06f)));
            }

            AddBeacon(parts, lamps, molX, headZ, deckTop);

            Rect molRect = Rect.MinMaxRect(
                molX - MolWidth * 0.5f,
                rootZ - stepCount * StairTread - 0.6f,
                molX + MolWidth * 0.5f,
                headZ);
            reserved.Add(Expand(molRect, 1.0f));
            return molRect;
        }

        private static void AddBeacon(
            ICollection<CitySeacoastPartDescriptor> parts,
            ICollection<CitySeacoastLampDescriptor> lamps,
            float molX,
            float headZ,
            float deckTop)
        {
            float beaconZ = headZ - 1.0f;
            parts.Add(Part(
                "seacoast-beacon-pedestal",
                CitySeacoastPartKind.BeaconTower,
                CitySeacoastStyle.Concrete,
                new Vector3(molX, deckTop + 0.225f, beaconZ),
                Quaternion.identity,
                new Vector3(1.35f, 0.45f, 1.35f)));
            parts.Add(Part(
                "seacoast-beacon-shaft",
                CitySeacoastPartKind.BeaconTower,
                CitySeacoastStyle.Concrete,
                new Vector3(molX, deckTop + 1.75f, beaconZ),
                Quaternion.identity,
                new Vector3(0.95f, 2.6f, 0.95f)));
            parts.Add(Part(
                "seacoast-beacon-gallery",
                CitySeacoastPartKind.BeaconTower,
                CitySeacoastStyle.Concrete,
                new Vector3(molX, deckTop + 3.225f, beaconZ),
                Quaternion.identity,
                new Vector3(1.30f, 0.35f, 1.30f)));
            float galleryTop = deckTop + 3.40f;
            for (int corner = 0; corner < 4; corner++)
            {
                float signX = (corner & 1) == 0 ? -1f : 1f;
                float signZ = (corner & 2) == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"seacoast-beacon-post-{corner}",
                    CitySeacoastPartKind.BeaconTower,
                    CitySeacoastStyle.Iron,
                    new Vector3(
                        molX + signX * 0.52f,
                        galleryTop + 0.31f,
                        beaconZ + signZ * 0.52f),
                    Quaternion.identity,
                    new Vector3(0.08f, 0.62f, 0.08f)));
            }

            parts.Add(Part(
                "seacoast-beacon-roof",
                CitySeacoastPartKind.BeaconTower,
                CitySeacoastStyle.Iron,
                new Vector3(molX, galleryTop + 0.68f, beaconZ),
                Quaternion.identity,
                new Vector3(0.95f, 0.13f, 0.95f)));

            // The lens itself is the fixture the builder raises; this
            // is the shelf it stands on.
            lamps.Add(new CitySeacoastLampDescriptor(
                "seacoast-lamp-beacon",
                CitySeacoastLampKind.Beacon,
                new Vector3(molX, galleryTop, beaconZ),
                0f));
        }

        private static void AddMouthSill(
            ICollection<CitySeacoastPartDescriptor> parts,
            in CitySeacoastFrame frame)
        {
            // The training wall across the mouth: the river dies
            // against its south face with an honest twelve-centimetre
            // spill, the sea laps its north face, and the two water
            // sheets never touch. Its crest is the beach-edge datum,
            // above both waters' highest swell.
            float crest = frame.BeachEdgeTopY;
            float bottom = frame.SeaTopY - AssumedSeaBedDepth - 0.1f;
            float width = frame.ChannelXMax - frame.ChannelXMin + 0.6f;
            parts.Add(Part(
                "seacoast-mouth-sill",
                CitySeacoastPartKind.MouthSill,
                CitySeacoastStyle.Concrete,
                new Vector3(
                    (frame.ChannelXMin + frame.ChannelXMax) * 0.5f,
                    (crest + bottom) * 0.5f,
                    frame.WaterlineZ),
                Quaternion.identity,
                new Vector3(width, crest - bottom, 1.2f)));
            parts.Add(Part(
                "seacoast-mouth-sill-apron",
                CitySeacoastPartKind.MouthSill,
                CitySeacoastStyle.Concrete,
                new Vector3(
                    (frame.ChannelXMin + frame.ChannelXMax) * 0.5f,
                    (frame.SeaTopY - 0.02f + bottom) * 0.5f,
                    frame.WaterlineZ + 1.0f),
                Quaternion.identity,
                new Vector3(
                    width,
                    frame.SeaTopY - 0.02f - bottom,
                    0.8f)));
        }

        private static void AddDerrick(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame,
            Rect molRect,
            ICollection<Rect> reserved)
        {
            float derrickX = molRect.xMax + 6.5f;
            float derrickZ = frame.WaterlineZ - 3.2f;
            if (derrickX > frame.ChannelXMin - 6f)
            {
                derrickX = molRect.xMin - 6.5f;
            }

            float ground = SampleSandTop(layout, derrickX, derrickZ);
            parts.Add(Part(
                "seacoast-derrick-pedestal",
                CitySeacoastPartKind.DerrickCrane,
                CitySeacoastStyle.Concrete,
                new Vector3(derrickX, ground + 0.40f, derrickZ),
                Quaternion.identity,
                new Vector3(1.7f, 0.80f, 1.7f)));
            float mastBase = ground + 0.80f;
            parts.Add(Part(
                "seacoast-derrick-mast",
                CitySeacoastPartKind.DerrickCrane,
                CitySeacoastStyle.RustIron,
                new Vector3(derrickX, mastBase + 2.7f, derrickZ),
                Quaternion.identity,
                new Vector3(0.36f, 5.4f, 0.36f)));

            // The jib, frozen where its last load left it: out over
            // the water, a little off the mol's axis.
            Quaternion jibRotation = Quaternion.Euler(-40f, 20f, 0f);
            Vector3 mastTop = new Vector3(
                derrickX,
                mastBase + 5.1f,
                derrickZ);
            parts.Add(Part(
                "seacoast-derrick-jib",
                CitySeacoastPartKind.DerrickCrane,
                CitySeacoastStyle.RustIron,
                mastTop + jibRotation * new Vector3(0f, 0f, 3.2f),
                jibRotation,
                new Vector3(0.28f, 0.28f, 7.0f)));
            parts.Add(Part(
                "seacoast-derrick-counterweight",
                CitySeacoastPartKind.DerrickCrane,
                CitySeacoastStyle.Concrete,
                mastTop + jibRotation * new Vector3(0f, 0f, -1.1f),
                jibRotation,
                new Vector3(0.9f, 0.7f, 0.9f)));

            reserved.Add(Rect.MinMaxRect(
                derrickX - 4.2f, derrickZ - 4.2f,
                derrickX + 4.2f, derrickZ + 4.2f));
        }

        private static void AddPortRuins(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame,
            int seed,
            IReadOnlyList<Rect> reserved,
            CityOpenAreaAccessDescriptor access)
        {
            // What the port left on the sand: bollards that moored
            // nothing for years, a fallen gantry beam, crates nobody
            // came back to claim, rope gone stiff as wood.
            const int stations = 12;
            float from = frame.WestZone.xMin + 8f;
            float to = frame.ChannelXMin - 10f;
            int bollard = 0;
            int crate = 0;
            int beam = 0;
            int coil = 0;
            for (int index = 0; index < stations; index++)
            {
                uint hash = StableHash(seed, index, 0, RuinSalt);
                float x = Mathf.Lerp(
                              from,
                              to,
                              (index + 0.5f) / stations) +
                          ((hash & 0xFFu) / 255f - 0.5f) * 4f;
                // The band hugs the old quay line north of the shore
                // lane, so what the port dropped never blocks the walk
                // the pedestrians take along the esplanade axis.
                float z = frame.WaterlineZ -
                          (2.2f + (((hash >> 8) & 0xFFu) / 255f) * 3.0f);
                Rect footprint = Rect.MinMaxRect(
                    x - 1.2f, z - 1.2f, x + 1.2f, z + 1.2f);
                if (OverlapsAny(footprint, reserved, 0f) ||
                    !IsClearOfAccess(footprint, access))
                {
                    continue;
                }

                float ground = SampleSandTop(layout, x, z);
                float yaw = (hash >> 16) % 360u;
                switch ((hash >> 4) % 4u)
                {
                    case 0u:
                        parts.Add(Part(
                            $"seacoast-port-bollard-{bollard:D2}",
                            CitySeacoastPartKind.PortRuin,
                            CitySeacoastStyle.Iron,
                            new Vector3(x, ground + 0.28f, z),
                            Quaternion.Euler(0f, yaw, 0f),
                            new Vector3(0.30f, 0.56f, 0.30f)));
                        bollard++;
                        break;
                    case 1u:
                        parts.Add(Part(
                            $"seacoast-port-crate-{crate:D2}-a",
                            CitySeacoastPartKind.PortRuin,
                            CitySeacoastStyle.Planking,
                            new Vector3(x, ground + 0.34f, z),
                            Quaternion.Euler(0f, yaw, 0f),
                            new Vector3(1.05f, 0.68f, 1.05f)));
                        parts.Add(Part(
                            $"seacoast-port-crate-{crate:D2}-b",
                            CitySeacoastPartKind.PortRuin,
                            CitySeacoastStyle.Planking,
                            new Vector3(
                                x + 0.22f,
                                ground + 0.96f,
                                z - 0.14f),
                            Quaternion.Euler(0f, yaw + 14f, 0f),
                            new Vector3(0.82f, 0.56f, 0.82f)));
                        crate++;
                        break;
                    case 2u:
                        parts.Add(Part(
                            $"seacoast-port-beam-{beam:D2}",
                            CitySeacoastPartKind.PortRuin,
                            CitySeacoastStyle.RustIron,
                            new Vector3(x, ground + 0.16f, z),
                            Quaternion.Euler(0f, yaw, 4f),
                            new Vector3(3.4f, 0.26f, 0.26f)));
                        beam++;
                        break;
                    default:
                        parts.Add(Part(
                            $"seacoast-port-coil-{coil:D2}",
                            CitySeacoastPartKind.PortRuin,
                            CitySeacoastStyle.Litter,
                            new Vector3(x, ground + 0.09f, z),
                            Quaternion.Euler(0f, yaw, 0f),
                            new Vector3(0.9f, 0.18f, 0.9f)));
                        coil++;
                        break;
                }
            }
        }

        // ------------------------------------------------------------------
        // the footbridge over the mouth
        // ------------------------------------------------------------------

        private static void AddFootbridge(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame)
        {
            float deckZ = frame.WaterlineZ - FootbridgeZOffset;
            float west = frame.ChannelXMin - FootbridgeOverhang;
            float east = frame.ChannelXMax + FootbridgeOverhang;
            float bankWest = SampleSandTop(
                layout, frame.ChannelXMin - 1.0f, deckZ);
            float bankEast = SampleSandTop(
                layout, frame.ChannelXMax + 1.0f, deckZ);
            float deckTop = Mathf.Max(bankWest, bankEast) + 0.14f;
            Quaternion rotation = Quaternion.identity;

            // Deck boards laid across the walk, west end and east end
            // named: the shore promenade's lane is strung between
            // them.
            float span = east - west;
            int deckCount = Mathf.Max(2, Mathf.RoundToInt(span / 0.9f));
            for (int index = 0; index < deckCount; index++)
            {
                float from = Mathf.Lerp(
                    west, east, index / (float)deckCount);
                float to = Mathf.Lerp(
                    west, east, (index + 1f) / deckCount);
                string id = index == 0
                    ? FootbridgeDeckWestId
                    : index == deckCount - 1
                        ? FootbridgeDeckEastId
                        : $"seacoast-footbridge-deck-{index:D2}";
                parts.Add(Part(
                    id,
                    CitySeacoastPartKind.FootbridgeDeck,
                    CitySeacoastStyle.Planking,
                    new Vector3(
                        (from + to) * 0.5f,
                        deckTop - 0.06f,
                        deckZ),
                    rotation,
                    new Vector3(
                        to - from - 0.04f,
                        0.12f,
                        FootbridgeWidth)));
            }

            // Landing boards at half height so each end is a step,
            // not a hop.
            for (int side = 0; side < 2; side++)
            {
                bool isWest = side == 0;
                float bank = isWest ? bankWest : bankEast;
                float x = isWest ? west - 0.45f : east + 0.45f;
                parts.Add(Part(
                    $"seacoast-footbridge-landing-{side}",
                    CitySeacoastPartKind.FootbridgeDeck,
                    CitySeacoastStyle.Planking,
                    new Vector3(
                        x,
                        (deckTop + bank) * 0.5f - 0.06f,
                        deckZ),
                    rotation,
                    new Vector3(0.9f, 0.12f, FootbridgeWidth)));
            }

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"seacoast-footbridge-bearer-{side}",
                    CitySeacoastPartKind.FootbridgeDeck,
                    CitySeacoastStyle.TarredTimber,
                    new Vector3(
                        (west + east) * 0.5f,
                        deckTop - 0.23f,
                        deckZ + sign * (FootbridgeWidth * 0.5f - 0.14f)),
                    rotation,
                    new Vector3(span, 0.22f, 0.20f)));
            }

            // Piles into the channel, feet below the river bed.
            float riverWater = ResolveMouthWaterY(layout, frame);
            float pileBottom = riverWater - 1.35f;
            float pileHeight = deckTop - 0.12f - pileBottom;
            for (int pair = 0; pair < 2; pair++)
            {
                float x = Mathf.Lerp(
                    frame.ChannelXMin,
                    frame.ChannelXMax,
                    pair == 0 ? 0.33f : 0.67f);
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1f : 1f;
                    parts.Add(Part(
                        $"seacoast-footbridge-pile-{pair}-{side}",
                        CitySeacoastPartKind.FootbridgePile,
                        CitySeacoastStyle.TarredTimber,
                        new Vector3(
                            x,
                            pileBottom + pileHeight * 0.5f,
                            deckZ + sign *
                                (FootbridgeWidth * 0.5f - 0.14f)),
                        rotation,
                        new Vector3(0.26f, pileHeight, 0.26f)));
                }
            }

            // Rails both sides, full span: a footbridge with an open
            // edge over the mouth would be the coast's one invisible
            // hole.
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                float railZ = deckZ + sign *
                              (FootbridgeWidth * 0.5f - 0.06f);
                int postCount = Mathf.Max(
                    2,
                    Mathf.FloorToInt(span / 2.2f) + 1);
                for (int index = 0; index < postCount; index++)
                {
                    float x = Mathf.Lerp(
                        west + 0.3f,
                        east - 0.3f,
                        index / (postCount - 1f));
                    parts.Add(Part(
                        $"seacoast-footbridge-post-{side}-{index:D2}",
                        CitySeacoastPartKind.FootbridgeRail,
                        CitySeacoastStyle.TarredTimber,
                        new Vector3(
                            x,
                            deckTop + FootbridgeRailHeight * 0.5f,
                            railZ),
                        rotation,
                        new Vector3(
                            0.10f,
                            FootbridgeRailHeight,
                            0.10f)));
                }

                parts.Add(Part(
                    $"seacoast-footbridge-rail-{side}",
                    CitySeacoastPartKind.FootbridgeRail,
                    CitySeacoastStyle.TarredTimber,
                    new Vector3(
                        (west + east) * 0.5f,
                        deckTop + FootbridgeRailHeight - 0.06f,
                        railZ),
                    rotation,
                    new Vector3(span, 0.10f, 0.08f)));
            }
        }

        /// <summary>
        /// The quay stairs: where each river promenade's north end
        /// used to be sealed by a transverse rail, a short granite
        /// stair now walks the small step down onto the sand. The
        /// river's rail pass skips those two seals whenever the coast
        /// exists, so every metre of the hand-off stays visible
        /// geometry.
        /// </summary>
        private static void AddPromenadeStairs(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame)
        {
            for (int index = 0;
                 index < layout.River.Promenades.Count;
                 index++)
            {
                CityRiverPromenadeDescriptor promenade =
                    layout.River.Promenades[index];
                float laneX = promenade.WestBank
                    ? promenade.Bounds.xMin + PromenadeLaneInset
                    : promenade.Bounds.xMax - PromenadeLaneInset;
                float joinZ = promenade.Bounds.yMax;
                float sand = SampleSandTop(
                    layout,
                    laneX,
                    joinZ + 4.3f);
                float drop = promenade.NorthY - sand;
                string baseId = promenade.WestBank
                    ? PromenadeStairWestId
                    : PromenadeStairEastId;
                int stepCount = Mathf.Clamp(
                    Mathf.CeilToInt(Mathf.Abs(drop) / StairMaximumRise),
                    1,
                    6);
                float stepRise = drop / stepCount;
                for (int step = 0; step < stepCount; step++)
                {
                    // Step tops walk from just under the promenade's
                    // datum down to the sand; the last one lands
                    // flush, a threshold slab across the seam itself.
                    float top = promenade.NorthY -
                                (step + 1) * stepRise;
                    parts.Add(Part(
                        step == 0
                            ? baseId
                            : $"{baseId}-{step}",
                        CitySeacoastPartKind.EsplanadeStair,
                        CitySeacoastStyle.Granite,
                        new Vector3(
                            laneX,
                            top - 0.15f,
                            joinZ + 0.28f + step * StairTread),
                        Quaternion.identity,
                        new Vector3(
                            2.0f,
                            0.30f,
                            StairTread + 0.08f)));
                }
            }
        }

        // ------------------------------------------------------------------
        // the wild shore
        // ------------------------------------------------------------------

        private static void AddWildShore(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame,
            int seed,
            IReadOnlyList<Rect> reserved)
        {
            if (frame.EastZone.width < MinimumEastZoneWidth)
            {
                return;
            }

            AddRottenPiles(parts, frame, seed);
            AddBarge(parts, frame, seed);
            EmitBoat(
                parts,
                layout,
                BoatTarget,
                CitySeacoastBoatVariant.HolledWreck,
                frame.EastZone.xMin + 44f,
                frame.WaterlineZ - 4.2f,
                (StableHash(seed, 99, 0, HullSalt) % 33u) - 16f,
                false);
            AddDriftwood(parts, layout, frame, seed, reserved);
            AddShoreGrass(parts, layout, frame, seed, reserved);
            AddBluffStair(parts, layout, frame);
        }

        /// <summary>
        /// The old breakwater's piles, marching into the fog: the row
        /// starts on the sand and walks north until the sea has almost
        /// swallowed it. Each leans its own way; none has held a board
        /// in decades.
        /// </summary>
        private static void AddRottenPiles(
            ICollection<CitySeacoastPartDescriptor> parts,
            in CitySeacoastFrame frame,
            int seed)
        {
            float rowX = frame.EastZone.xMin +
                         frame.EastZone.width * 0.55f;
            const int piles = 9;
            for (int index = 0; index < piles; index++)
            {
                uint hash = StableHash(seed, index, 0, PileSalt);
                float t = index / (piles - 1f);
                float z = frame.WaterlineZ - 3f + index * 2.2f;
                float x = rowX + ((hash & 0xFFu) / 255f - 0.5f) * 1.1f;
                float topY = frame.SeaTopY +
                             Mathf.Lerp(1.05f, 0.10f, t);
                float bottomY = frame.SeaTopY - AssumedSeaBedDepth -
                                0.25f;
                float lean = (((hash >> 8) & 0xFFu) / 255f - 0.5f) * 18f;
                float leanYaw = (hash >> 16) % 360u;
                parts.Add(Part(
                    $"seacoast-rotten-pile-{index:D2}",
                    CitySeacoastPartKind.RottenPile,
                    CitySeacoastStyle.TarredTimber,
                    new Vector3(x, (topY + bottomY) * 0.5f, z),
                    Quaternion.Euler(lean, leanYaw, 0f),
                    new Vector3(0.24f, topY - bottomY, 0.24f)));
            }
        }

        /// <summary>
        /// The stranded barge, listing on the bottom a dozen metres
        /// out: the farthest thing on the shore and the reward for
        /// walking all the way east — a rusted silhouette the fog only
        /// half surrenders.
        /// </summary>
        private static void AddBarge(
            ICollection<CitySeacoastPartDescriptor> parts,
            in CitySeacoastFrame frame,
            int seed)
        {
            float bargeX = frame.EastZone.xMax - 34f;
            float bargeZ = frame.WaterlineZ + 12.5f;
            uint hash = StableHash(seed, 0, 0, PileSalt ^ 0x42u);
            float yaw = 10f + (hash % 12u);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 3.5f);
            float bottom = frame.SeaTopY - 1.25f;
            Vector3 center = new Vector3(bargeX, 0f, bargeZ);

            Vector3 At(float along, float up, float across)
            {
                return center + rotation *
                       new Vector3(along, 0f, across) +
                       Vector3.up * up;
            }

            const float length = 11.6f;
            const float beam = 3.4f;
            float hullTop = frame.SeaTopY + 0.85f;
            float hullHeight = hullTop - bottom;
            float hullCenterY = (hullTop + bottom) * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"seacoast-barge-side-{side}",
                    CitySeacoastPartKind.Barge,
                    CitySeacoastStyle.RustIron,
                    At(0f, hullCenterY, sign * (beam * 0.5f - 0.14f)),
                    rotation,
                    new Vector3(length, hullHeight, 0.28f)));
            }

            for (int end = 0; end < 2; end++)
            {
                float sign = end == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"seacoast-barge-end-{end}",
                    CitySeacoastPartKind.Barge,
                    CitySeacoastStyle.RustIron,
                    At(sign * (length * 0.5f - 0.14f), hullCenterY, 0f),
                    rotation,
                    new Vector3(0.28f, hullHeight, beam)));
            }

            parts.Add(Part(
                "seacoast-barge-deck",
                CitySeacoastPartKind.Barge,
                CitySeacoastStyle.HullTar,
                At(0f, hullTop - 0.08f, 0f),
                rotation,
                new Vector3(length - 0.2f, 0.16f, beam - 0.2f)));
            parts.Add(Part(
                "seacoast-barge-coaming",
                CitySeacoastPartKind.Barge,
                CitySeacoastStyle.RustIron,
                At(-1.2f, hullTop + 0.22f, 0f),
                rotation,
                new Vector3(4.6f, 0.34f, 2.2f)));
            parts.Add(Part(
                "seacoast-barge-house",
                CitySeacoastPartKind.Barge,
                CitySeacoastStyle.RustIron,
                At(length * 0.5f - 1.9f, hullTop + 0.65f, 0f),
                rotation,
                new Vector3(2.4f, 1.30f, 2.6f)));
            parts.Add(Part(
                "seacoast-barge-funnel",
                CitySeacoastPartKind.Barge,
                CitySeacoastStyle.RustIron,
                At(length * 0.5f - 1.4f, hullTop + 1.75f, -0.6f),
                rotation,
                new Vector3(0.5f, 0.9f, 0.5f)));
        }

        private static void AddDriftwood(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame,
            int seed,
            IReadOnlyList<Rect> reserved)
        {
            const int stations = 12;
            int emitted = 0;
            for (int index = 0; index < stations; index++)
            {
                uint hash = StableHash(seed, index, 0, DriftSalt);
                float x = Mathf.Lerp(
                              frame.EastZone.xMin + 4f,
                              frame.EastZone.xMax - 6f,
                              (index + 0.5f) / stations) +
                          ((hash & 0xFFu) / 255f - 0.5f) * 5f;
                float z = frame.WaterlineZ -
                          (2.4f + (((hash >> 8) & 0xFFu) / 255f) * 7.5f);
                Rect footprint = Rect.MinMaxRect(
                    x - 1.5f, z - 1.5f, x + 1.5f, z + 1.5f);
                if (OverlapsAny(footprint, reserved, 0f))
                {
                    continue;
                }

                float ground = SampleSandTop(layout, x, z);
                float length =
                    1.5f + (((hash >> 16) & 0xFFu) / 255f) * 1.3f;
                parts.Add(Part(
                    $"seacoast-driftwood-{emitted:D2}",
                    CitySeacoastPartKind.Driftwood,
                    CitySeacoastStyle.Litter,
                    new Vector3(x, ground + 0.11f, z),
                    Quaternion.Euler(0f, (hash >> 4) % 360u, 0f),
                    new Vector3(length, 0.22f, 0.26f)));
                emitted++;
            }
        }

        private static void AddShoreGrass(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame,
            int seed,
            IReadOnlyList<Rect> reserved)
        {
            // Hard dune grass on the upper sand, in clumps of three
            // stems: the one green thing the coast grows, and it grows
            // it far from the water.
            const int stations = 14;
            int clump = 0;
            for (int index = 0; index < stations; index++)
            {
                uint hash = StableHash(seed, index, 0, GrassSalt);
                float x = Mathf.Lerp(
                              frame.EastZone.xMin + 3f,
                              frame.EastZone.xMax - 3f,
                              (index + 0.5f) / stations) +
                          ((hash & 0xFFu) / 255f - 0.5f) * 6f;
                float z = frame.BeachRowBounds.yMin +
                          3f + (((hash >> 8) & 0xFFu) / 255f) *
                          (frame.BeachRowBounds.height - 12f);
                Rect footprint = Rect.MinMaxRect(
                    x - 0.6f, z - 0.6f, x + 0.6f, z + 0.6f);
                if (OverlapsAny(footprint, reserved, 0f))
                {
                    continue;
                }

                float ground = SampleSandTop(layout, x, z);
                for (int stem = 0; stem < 3; stem++)
                {
                    uint stemHash = StableHash(
                        seed, index, stem + 1, GrassSalt);
                    float height =
                        0.42f + (stemHash & 0xFFu) / 255f * 0.26f;
                    Vector3 offset = new Vector3(
                        (((stemHash >> 8) & 0xFFu) / 255f - 0.5f) *
                        0.5f,
                        0f,
                        (((stemHash >> 16) & 0xFFu) / 255f - 0.5f) *
                        0.5f);
                    parts.Add(Part(
                        $"seacoast-grass-{clump:D2}-{stem}",
                        CitySeacoastPartKind.ShoreGrass,
                        CitySeacoastStyle.Grass,
                        new Vector3(x, ground + height * 0.5f, z) +
                            offset,
                        Quaternion.Euler(
                            0f,
                            stemHash % 360u,
                            ((stemHash >> 12) % 15u) - 7f),
                        new Vector3(0.12f, height, 0.12f)));
                }

                clump++;
            }
        }

        /// <summary>
        /// The worn stair down the east bluff. The street sits metres
        /// above the sand out here; the beach itself is a slope you
        /// can technically walk, but the stair is the honest way down
        /// — and the thing that says people used to come.
        /// </summary>
        private static void AddBluffStair(
            ICollection<CitySeacoastPartDescriptor> parts,
            CityLayout layout,
            in CitySeacoastFrame frame)
        {
            float stairX = frame.EastZone.xMax - 14f;
            float fromZ = frame.BeachRowBounds.yMin + 4.0f;
            float toZ = frame.WaterlineZ - 6.5f;
            float topGround = SampleSandTop(layout, stairX, fromZ);
            float bottomGround = SampleSandTop(layout, stairX, toZ);
            float drop = topGround - bottomGround;
            if (drop < 1.2f)
            {
                return;
            }

            int stepCount = Mathf.Min(
                30,
                Mathf.CeilToInt(drop / StairMaximumRise));
            float stepRise = drop / stepCount;
            float tread = (toZ - fromZ) / stepCount;
            for (int index = 0; index < stepCount; index++)
            {
                float top = topGround - (index + 1) * stepRise;
                float z = fromZ + (index + 0.5f) * tread;
                parts.Add(Part(
                    $"seacoast-bluff-stair-{index:D2}",
                    CitySeacoastPartKind.BluffStair,
                    CitySeacoastStyle.Planking,
                    new Vector3(stairX, top - 0.15f, z),
                    Quaternion.identity,
                    new Vector3(1.6f, 0.30f, tread + 0.08f)));
            }
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static float ResolveMouthWaterY(
            CityLayout layout,
            in CitySeacoastFrame frame)
        {
            if (TryGetMouthSegment(
                    layout,
                    frame.BeachRowBounds,
                    out CityRiverSegmentDescriptor mouth))
            {
                return mouth.NorthWaterY;
            }

            return frame.SeaTopY;
        }

        private static bool TryGetMouthSegment(
            CityLayout layout,
            Rect beachRow,
            out CityRiverSegmentDescriptor mouth)
        {
            mouth = default;
            bool found = false;
            for (int index = 0;
                 index < layout.River.Segments.Count;
                 index++)
            {
                CityRiverSegmentDescriptor segment =
                    layout.River.Segments[index];
                if (!segment.WaterBounds.Overlaps(beachRow))
                {
                    continue;
                }

                if (!found || segment.Cell.y > mouth.Cell.y)
                {
                    mouth = segment;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// The sand's top at a point, from the same height contract
        /// the terrain mesh is built with. The point is clamped into
        /// the sand row, so a part hanging just past an edge still
        /// reads the slope it visually sits on.
        /// </summary>
        private static float SampleSandTop(
            CityLayout layout,
            float x,
            float z)
        {
            CitySurfaceDescriptor best = default;
            bool found = false;
            float bestDistance = float.MaxValue;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Feature !=
                        CityAreaFeatureKind.NorthWaterfront ||
                    surface.Kind != CitySurfaceKind.Beach)
                {
                    continue;
                }

                Rect bounds = surface.WorldBounds;
                float dx = Mathf.Max(
                    0f,
                    Mathf.Max(bounds.xMin - x, x - bounds.xMax));
                float dz = Mathf.Max(
                    0f,
                    Mathf.Max(bounds.yMin - z, z - bounds.yMax));
                float distance = dx + dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = surface;
                    found = true;
                    if (distance <= 0f)
                    {
                        break;
                    }
                }
            }

            if (!found)
            {
                return CitySurfaceDescriptor.WaterTopOffset + 0.32f;
            }

            Rect clampBounds = best.WorldBounds;
            var point = new Vector2(
                Mathf.Clamp(
                    x,
                    clampBounds.xMin + 0.01f,
                    clampBounds.xMax - 0.01f),
                Mathf.Clamp(
                    z,
                    clampBounds.yMin + 0.01f,
                    clampBounds.yMax - 0.01f));
            return CityTerrainSurfacePlan.SampleTop(
                layout,
                best,
                point);
        }

        private static CitySeacoastPartDescriptor Part(
            string stableId,
            CitySeacoastPartKind kind,
            CitySeacoastStyle style,
            Vector3 center,
            Quaternion rotation,
            Vector3 size)
        {
            return new CitySeacoastPartDescriptor(
                stableId,
                kind,
                style,
                center,
                rotation,
                size,
                -1,
                CitySeacoastBoatVariant.PlankSkiff);
        }

        private static CitySeacoastPartDescriptor BoatPart(
            string stableId,
            CitySeacoastPartKind kind,
            CitySeacoastStyle style,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            int boatOrdinal,
            CitySeacoastBoatVariant variant)
        {
            return new CitySeacoastPartDescriptor(
                stableId,
                kind,
                style,
                center,
                rotation,
                size,
                boatOrdinal,
                variant);
        }

        private static bool MayStandInSea(CitySeacoastPartKind kind)
        {
            switch (kind)
            {
                case CitySeacoastPartKind.MolBlock:
                case CitySeacoastPartKind.MolDeck:
                case CitySeacoastPartKind.MolParapet:
                case CitySeacoastPartKind.BeaconTower:
                // The derrick's jib froze where its last load left
                // it: out over the water, metres above it.
                case CitySeacoastPartKind.DerrickCrane:
                case CitySeacoastPartKind.MouthSill:
                case CitySeacoastPartKind.PierPile:
                case CitySeacoastPartKind.PierBeam:
                case CitySeacoastPartKind.PierDeck:
                case CitySeacoastPartKind.PierRail:
                case CitySeacoastPartKind.Slipway:
                case CitySeacoastPartKind.RottenPile:
                case CitySeacoastPartKind.Barge:
                    return true;
                default:
                    return false;
            }
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
                    CityAreaFeatureKind.NorthWaterfront)
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

        private static Rect Union(Rect left, Rect right)
        {
            return Rect.MinMaxRect(
                Mathf.Min(left.xMin, right.xMin),
                Mathf.Min(left.yMin, right.yMin),
                Mathf.Max(left.xMax, right.xMax),
                Mathf.Max(left.yMax, right.yMax));
        }

        /// <summary>
        /// Conservative XZ footprint of an oriented part: the rotated
        /// half-extents projected onto the world axes.
        /// </summary>
        private static Rect ToXZRect(CitySeacoastPartDescriptor part)
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

        private static float GetMinimumWorldY(
            CitySeacoastPartDescriptor part)
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
