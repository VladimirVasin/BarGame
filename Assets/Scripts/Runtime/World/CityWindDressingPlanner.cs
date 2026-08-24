using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Plans the city-wide wind dressing: cloth and rope misc pieces
    /// hung from structures the other plans already draw. Pure and
    /// deterministic — every choice is a hash of the seed and an
    /// anchor position, and every anchor is public descriptor data,
    /// so no zone planner changes for this pass. Zones whose art
    /// vocabulary forbids hanging fabric contribute nothing here by
    /// construction: the bar-side yard, the lighthouse island, the
    /// drained-lake block, the tunnel forecourt, the flood works and
    /// the stone terraces.
    /// </summary>
    public static class CityWindDressingPlanner
    {
        private const uint MarketSalt = 0x57444D41u;
        private const uint ScaffoldSalt = 0x57445343u;
        private const uint LineSalt = 0x57444C4Eu;
        private const uint GantrySalt = 0x57444754u;
        private const uint BannerSalt = 0x57444E4Cu;
        private const uint PierSalt = 0x57445052u;
        private const uint RibbonSalt = 0x57445242u;
        private const uint TailSalt = 0x5744544Cu;

        /// <summary>
        /// New courtyard laundry keeps this far from the drying-yard
        /// POI, so the authored yard stays the district's laundry
        /// statement.
        /// </summary>
        public const float DryingYardClearance = 25f;

        private const float CourtyardLineHeight = 2.05f;
        private const float CourtyardLineSag = 0.18f;
        private const float CourtyardLineHalfSpan = 1.6f;
        private const float CourtyardPoleHeight = 2.2f;
        private const float CourtyardLineObjectClearance = 0.30f;
        private const float CourtyardEntranceClearance = 1.10f;
        private const float RopeThickness = 0.03f;

        /// <summary>
        /// A pinned top edge sinks this far into the member it hangs
        /// from, so the weld never reads as a floating seam.
        /// </summary>
        private const float PinSink = 0.02f;

        private static readonly Color AwningCanvas =
            new Color(0.601f, 0.552f, 0.470f, 1f);
        private static readonly Color ShroudGrey =
            new Color(0.475f, 0.470f, 0.445f, 1f);
        private static readonly Color HempRope =
            new Color(0.420f, 0.370f, 0.280f, 1f);
        private static readonly Color LaundryLinen =
            new Color(0.680f, 0.660f, 0.600f, 1f);
        private static readonly Color LaundryBlue =
            new Color(0.470f, 0.520f, 0.580f, 1f);
        private static readonly Color LaundryOchre =
            new Color(0.620f, 0.540f, 0.420f, 1f);
        private static readonly Color TarpDark =
            new Color(0.208f, 0.225f, 0.235f, 1f);
        private static readonly Color TarredRope =
            new Color(0.160f, 0.140f, 0.120f, 1f);
        private static readonly Color BannerMagenta =
            new Color(0.520f, 0.300f, 0.400f, 1f);
        private static readonly Color PennantPaper =
            new Color(0.550f, 0.500f, 0.420f, 1f);
        private static readonly Color NetGreen =
            new Color(0.360f, 0.400f, 0.340f, 1f);
        private static readonly Color RibbonDark =
            new Color(0.280f, 0.070f, 0.080f, 1f);
        private static readonly Color ServiceCanvas =
            new Color(0.240f, 0.250f, 0.220f, 1f);
        private static readonly Color DeadCable =
            new Color(0.100f, 0.100f, 0.110f, 1f);

        public static CityWindDressingPlan Create(
            CityLayout layout,
            CityDecorationPlan decorationPlan,
            CitySeacoastPlan seacoastPlan,
            CityCemeteryPlan cemeteryPlan,
            CityFringeYardPlan fringeYardPlan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (decorationPlan == null)
            {
                throw new ArgumentNullException(nameof(decorationPlan));
            }

            var cloths = new List<CityWindDressingClothDescriptor>(
                CityWindDressingPlan.MaximumClothCount);
            var supports =
                new List<CityWindDressingSupportDescriptor>(24);

            AddOldTown(cloths, layout, decorationPlan);
            AddResidential(cloths, supports, layout, decorationPlan);
            AddIndustrial(cloths, layout, decorationPlan);
            AddNightlife(cloths, layout, decorationPlan);
            AddPark(cloths, layout, decorationPlan);
            AddSeacoast(cloths, layout, seacoastPlan);
            AddCemetery(cloths, layout, cemeteryPlan);
            AddFringeYards(cloths, layout, fringeYardPlan);

            var plan = new CityWindDressingPlan(cloths, supports);
            CityWindDressingValidator.ValidateOrThrow(layout, plan);
            return plan;
        }

        /// <summary>
        /// Old Town: torn awning rags off the market stalls' valance,
        /// grey shrouds off the scaffoldings' outer guard rails and
        /// one rope end. The bible's canonical old-town sound is a
        /// creaking tent, so the district reads cloth-first: two rags
        /// per stall on up to four stalls, spread by stride over the
        /// whole anchor list rather than clustered at its head.
        /// </summary>
        private static void AddOldTown(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CityDecorationPlan decorationPlan)
        {
            List<CityDecorationDescriptor> markets = CollectDecorations(
                decorationPlan,
                CityDecorationKind.OldTownStreetMarket);
            int marketStride = StrideFor(markets.Count, 4);
            int ragOrdinal = 0;
            for (int index = 0;
                 index < markets.Count && ragOrdinal < 8;
                 index += marketStride)
            {
                CityDecorationDescriptor market = markets[index];
                ResolveLotFrame(
                    layout,
                    market,
                    out Vector3 origin,
                    out Vector3 forward,
                    out Vector3 tangent,
                    out float lotWidth,
                    out _);
                float stallWidth = Mathf.Clamp(
                    lotWidth * 0.44f,
                    3.4f,
                    5.2f);
                uint hash = HashAt(layout.Seed, origin, MarketSalt);

                // The valance's bottom edge runs at 2.09 with the
                // stall's 0.55 street-depth scale already applied to
                // its forward offset. One rag to each side of the
                // counter.
                for (int side = 0; side < 2; side++)
                {
                    float lateral = stallWidth *
                        (0.16f + (0.12f * Unit(hash, side * 8))) *
                        (side == 0 ? -1f : 1f);
                    cloths.Add(new CityWindDressingClothDescriptor(
                        $"wind-old-town-market-rag-{ragOrdinal:00}",
                        CityWindDressingKind.MarketAwningRag,
                        CityWindDressingZone.OldTown,
                        origin +
                        (tangent * lateral) +
                        (forward * 0.352f) +
                        (Vector3.up * (2.09f + PinSink)),
                        YawFromForward(forward),
                        1.05f,
                        0.75f,
                        AwningCanvas,
                        1 + (int)((hash >> (side * 4)) % 3u),
                        5,
                        7,
                        false));
                    ragOrdinal++;
                }
            }

            List<CityDecorationDescriptor> scaffolds =
                CollectDecorations(
                    decorationPlan,
                    CityDecorationKind.OldTownScaffolding);
            int scaffoldStride = StrideFor(scaffolds.Count, 3);
            int shroudOrdinal = 0;
            for (int index = 0;
                 index < scaffolds.Count && shroudOrdinal < 3;
                 index += scaffoldStride)
            {
                CityDecorationDescriptor scaffold = scaffolds[index];
                ResolveLotFrame(
                    layout,
                    scaffold,
                    out Vector3 origin,
                    out Vector3 forward,
                    out Vector3 tangent,
                    out float lotWidth,
                    out float lotHeight);
                float frameWidth = Mathf.Clamp(
                    lotWidth * 0.68f,
                    4.2f,
                    7.2f);
                float frameHeight = Mathf.Clamp(
                    lotHeight - 0.6f,
                    4.8f,
                    7.2f);
                uint hash = HashAt(layout.Seed, origin, ScaffoldSalt);
                float lateral = frameWidth *
                    (0.10f + (0.12f * Unit(hash, 0))) *
                    ((hash & 8u) == 0u ? 1f : -1f);

                // The level-3 guard rail runs on the frame's OUTER
                // face (0.84 out of the facade): a shroud tied there
                // hangs outside the planked frame and reads from the
                // street, unlike the top ledger buried behind the
                // platforms.
                Vector3 topRail = origin +
                    (forward * 0.84f) +
                    (Vector3.up *
                        ((frameHeight * 0.75f) + 0.49f + PinSink));
                cloths.Add(new CityWindDressingClothDescriptor(
                    $"wind-old-town-scaffold-shroud-{shroudOrdinal:00}",
                    CityWindDressingKind.ScaffoldShroud,
                    CityWindDressingZone.OldTown,
                    topRail + (tangent * lateral),
                    YawFromForward(forward),
                    1.5f,
                    1.9f,
                    ShroudGrey,
                    1 + (int)(hash % 2u),
                    5,
                    7,
                    false));

                if (shroudOrdinal == 0)
                {
                    // A rope end off the same frame's level-2 rail.
                    cloths.Add(new CityWindDressingClothDescriptor(
                        "wind-old-town-rope-end-00",
                        CityWindDressingKind.RopeEnd,
                        CityWindDressingZone.OldTown,
                        origin +
                        (forward * 0.84f) +
                        (Vector3.up *
                            ((frameHeight * 0.5f) + 0.49f + PinSink)) +
                        (tangent *
                            (frameWidth * 0.38f *
                             ((hash & 16u) == 0u ? 1f : -1f))),
                        YawFromForward(forward),
                        0.08f,
                        0.9f,
                        HempRope,
                        0,
                        1,
                        6,
                        false));
                }

                shroudOrdinal++;
            }
        }

        /// <summary>
        /// Residential: up to six courtyard drying lines on their own
        /// drawn poles, each with two pieces of wash the hero walks
        /// through. The lines keep 18 m apart and clear of the
        /// drying-yard POI, so the authored yard stays the district's
        /// laundry statement and no street reads as the bible's
        /// forbidden "solid forest of laundry".
        /// </summary>
        private static void AddResidential(
            ICollection<CityWindDressingClothDescriptor> cloths,
            ICollection<CityWindDressingSupportDescriptor> supports,
            CityLayout layout,
            CityDecorationPlan decorationPlan)
        {
            Rect dryingYard = default;
            bool hasDryingYard = false;
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[index];
                if (point.Kind ==
                    CityDistrictPointOfInterestKind.ResidentialDryingYard)
                {
                    dryingYard = point.PublicBounds;
                    hasDryingYard = true;
                    break;
                }
            }

            // One line per discarded-furniture frontage, thinned to
            // six by a spacing rule rather than a fixed head-of-list
            // pick, so the wash spreads over the whole district.
            List<CityDecorationDescriptor> anchors = CollectDecorations(
                decorationPlan,
                CityDecorationKind.ResidentialDiscardedFurniture);
            List<Bounds> blockingDecoration =
                CollectBlockingDecorationBounds(layout, decorationPlan);
            var chosenCenters = new List<Vector3>(6);
            int lineOrdinal = 0;
            for (int index = 0;
                 index < anchors.Count && lineOrdinal < 6;
                 index++)
            {
                CityDecorationDescriptor anchor = anchors[index];
                ResolveLotFrame(
                    layout,
                    anchor,
                    out Vector3 origin,
                    out Vector3 forward,
                    out Vector3 tangent,
                    out _,
                    out _);
                uint hash = HashAt(layout.Seed, origin, LineSalt);
                if (!TryCreateCourtyardLineCenter(
                        layout,
                        anchor,
                        origin,
                        forward,
                        tangent,
                        blockingDecoration,
                        out Vector3 center))
                {
                    continue;
                }

                if (hasDryingYard &&
                    DistanceToRect(
                        new Vector2(center.x, center.z),
                        dryingYard) < DryingYardClearance)
                {
                    continue;
                }

                bool crowded = false;
                for (int chosen = 0;
                     chosen < chosenCenters.Count;
                     chosen++)
                {
                    Vector3 delta = chosenCenters[chosen] - center;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < 18f * 18f)
                    {
                        crowded = true;
                        break;
                    }
                }

                if (crowded)
                {
                    continue;
                }

                AddCourtyardLine(
                    cloths,
                    supports,
                    layout,
                    center,
                    forward,
                    tangent,
                    hash,
                    lineOrdinal);
                chosenCenters.Add(center);
                lineOrdinal++;
            }
        }

        /// <summary>
        /// The furniture anchor occupies one lateral bay of its frontage.
        /// Mirroring that authored bay through the lot centre puts the line
        /// beside it without inventing another arbitrary offset, keeps the
        /// central door clear and preserves the line inside the same block.
        /// The final corridor check rejects a bay taken by any other physical
        /// decoration; a crowded lot simply contributes no laundry.
        /// </summary>
        private static bool TryCreateCourtyardLineCenter(
            CityLayout layout,
            CityDecorationDescriptor anchor,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent,
            IReadOnlyList<Bounds> blockingDecoration,
            out Vector3 center)
        {
            if (!anchor.TryResolveLot(layout, out BuildingLot lot))
            {
                center = default;
                return false;
            }

            float ownerLateral = Vector3.Dot(
                origin - lot.Center,
                tangent);
            if (Mathf.Abs(ownerLateral) < 0.10f)
            {
                center = default;
                return false;
            }

            float freeSide = -Mathf.Sign(ownerLateral);
            float targetLateral = freeSide *
                (Mathf.Abs(ownerLateral) +
                 CourtyardLineObjectClearance);
            float forwardOffset = Vector3.Dot(
                origin - lot.Center,
                forward);
            center = lot.Center +
                (tangent * targetLateral) +
                (forward * forwardOffset);

            float parallelBlockSpan =
                (Mathf.Abs(tangent.x) > 0.5f
                    ? layout.NodeSpacing.x
                    : layout.NodeSpacing.y) -
                layout.RoadWidth;
            if (Mathf.Abs(targetLateral) +
                CourtyardLineHalfSpan +
                CourtyardLineObjectClearance >
                parallelBlockSpan * 0.5f)
            {
                center = default;
                return false;
            }

            float doorLateral = Vector3.Dot(
                lot.DoorPosition - lot.Center,
                tangent);
            float lineMinimum =
                targetLateral - CourtyardLineHalfSpan;
            float lineMaximum =
                targetLateral + CourtyardLineHalfSpan;
            float doorDistance = doorLateral < lineMinimum
                ? lineMinimum - doorLateral
                : doorLateral > lineMaximum
                    ? doorLateral - lineMaximum
                    : 0f;
            if (doorDistance < CourtyardEntranceClearance)
            {
                center = default;
                return false;
            }

            Vector3 footA = center -
                (tangent * CourtyardLineHalfSpan);
            Vector3 footB = center +
                (tangent * CourtyardLineHalfSpan);
            if (!CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout,
                    new Vector2(footA.x, footA.z),
                    out float groundA,
                    out _) ||
                !CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout,
                    new Vector2(footB.x, footB.z),
                    out float groundB,
                    out _))
            {
                center = default;
                return false;
            }

            Rect corridor = Rect.MinMaxRect(
                Mathf.Min(footA.x, footB.x) -
                CourtyardLineObjectClearance,
                Mathf.Min(footA.z, footB.z) -
                CourtyardLineObjectClearance,
                Mathf.Max(footA.x, footB.x) +
                CourtyardLineObjectClearance,
                Mathf.Max(footA.z, footB.z) +
                CourtyardLineObjectClearance);
            for (int index = 0;
                 index < blockingDecoration.Count;
                 index++)
            {
                if (OverlapsStrict(
                        corridor,
                        FootprintOf(blockingDecoration[index])))
                {
                    center = default;
                    return false;
                }
            }

            center.y = (groundA + groundB) * 0.5f;
            return true;
        }

        private static List<Bounds> CollectBlockingDecorationBounds(
            CityLayout layout,
            CityDecorationPlan decorationPlan)
        {
            var result = new List<Bounds>();
            var buffer = new List<Bounds>(
                CityStaticCollisionBuilder.MaximumDecorationProxyCount);
            for (int index = 0;
                 index < decorationPlan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorationPlan.Descriptors[index];
                if (descriptor.CollisionTier ==
                    CityDecorationCollisionTier.None)
                {
                    continue;
                }

                buffer.Clear();
                CityStaticCollisionBuilder.AddDecorationProxyBounds(
                    layout,
                    descriptor,
                    buffer);
                result.AddRange(buffer);
            }

            return result;
        }

        private static Rect FootprintOf(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            const float epsilon = 0.001f;
            return left.xMin < right.xMax - epsilon &&
                   left.xMax > right.xMin + epsilon &&
                   left.yMin < right.yMax - epsilon &&
                   left.yMax > right.yMin + epsilon;
        }

        private static void AddCourtyardLine(
            ICollection<CityWindDressingClothDescriptor> cloths,
            ICollection<CityWindDressingSupportDescriptor> supports,
            CityLayout layout,
            Vector3 center,
            Vector3 forward,
            Vector3 tangent,
            uint hash,
            int ordinal)
        {
            string prefix = $"wind-residential-line-{ordinal:00}";
            Vector3 footA = center - (tangent * CourtyardLineHalfSpan);
            Vector3 footB = center + (tangent * CourtyardLineHalfSpan);
            footA.y = SampleGroundOrFallback(layout, footA);
            footB.y = SampleGroundOrFallback(layout, footB);

            supports.Add(new CityWindDressingSupportDescriptor(
                $"{prefix}-pole-a",
                CityWindDressingSupportKind.LinePole,
                CityWindDressingZone.Residential,
                new RuntimeOrientedBox(
                    footA + (Vector3.up * (CourtyardPoleHeight * 0.5f)),
                    Quaternion.identity,
                    new Vector3(0.09f, CourtyardPoleHeight, 0.09f))));
            supports.Add(new CityWindDressingSupportDescriptor(
                $"{prefix}-pole-b",
                CityWindDressingSupportKind.LinePole,
                CityWindDressingZone.Residential,
                new RuntimeOrientedBox(
                    footB + (Vector3.up * (CourtyardPoleHeight * 0.5f)),
                    Quaternion.identity,
                    new Vector3(0.09f, CourtyardPoleHeight, 0.09f))));

            Vector3 tieA = footA + (Vector3.up * CourtyardLineHeight);
            Vector3 tieB = footB + (Vector3.up * CourtyardLineHeight);
            var chords = new List<RuntimeOrientedBox>(
                CityRopeSpanGeometry.DefaultSegments);
            CityRopeSpanGeometry.AppendChordBoxes(
                chords,
                tieA,
                tieB,
                CourtyardLineSag,
                RopeThickness);
            for (int index = 0; index < chords.Count; index++)
            {
                supports.Add(new CityWindDressingSupportDescriptor(
                    $"{prefix}-chord-{index:00}",
                    CityWindDressingSupportKind.RopeChord,
                    CityWindDressingZone.Residential,
                    chords[index]));
            }

            // Two pieces off-centre so the sag reads between them.
            float firstT = 0.30f + (0.06f * Unit(hash, 8));
            float secondT = 0.62f + (0.06f * Unit(hash, 16));
            cloths.Add(new CityWindDressingClothDescriptor(
                $"{prefix}-piece-00",
                CityWindDressingKind.CourtyardLaundry,
                CityWindDressingZone.Residential,
                CityRopeSpanGeometry.SamplePoint(
                    tieA,
                    tieB,
                    CourtyardLineSag,
                    firstT) + (Vector3.up * PinSink),
                YawFromForward(forward),
                0.7f,
                0.9f,
                LaundryLinen,
                0,
                5,
                7,
                true));
            cloths.Add(new CityWindDressingClothDescriptor(
                $"{prefix}-piece-01",
                CityWindDressingKind.CourtyardLaundry,
                CityWindDressingZone.Residential,
                CityRopeSpanGeometry.SamplePoint(
                    tieA,
                    tieB,
                    CourtyardLineSag,
                    secondT) + (Vector3.up * PinSink),
                YawFromForward(forward),
                0.5f,
                0.6f,
                (hash & 32u) == 0u ? LaundryBlue : LaundryOchre,
                0,
                4,
                6,
                true));
        }

        /// <summary>
        /// Industrial: dark tarpaulin curtains and tarred sling ends
        /// off the pipe racks' street-side top ties — every piece
        /// tied to a process, per the bible. The rooftop gantry is
        /// deliberately skipped: it stands on the district's landmark
        /// tower and its beam runs tens of metres over the street,
        /// where cloth is sub-pixel.
        /// </summary>
        private static void AddIndustrial(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CityDecorationPlan decorationPlan)
        {
            List<CityDecorationDescriptor> racks = CollectDecorations(
                decorationPlan,
                CityDecorationKind.IndustrialPipeRack);
            int rackStride = StrideFor(racks.Count, 4);
            int tarpOrdinal = 0;
            for (int index = 0;
                 index < racks.Count && tarpOrdinal < 4;
                 index += rackStride)
            {
                CityDecorationDescriptor rack = racks[index];
                ResolveLotFrame(
                    layout,
                    rack,
                    out Vector3 origin,
                    out Vector3 forward,
                    out Vector3 tangent,
                    out float lotWidth,
                    out _);
                float rackWidth = Mathf.Clamp(
                    lotWidth * 0.52f,
                    4.5f,
                    7.0f);
                uint hash = HashAt(layout.Seed, origin, GantrySalt);
                float side = (hash & 1u) == 0u ? 1f : -1f;

                // The street-side top tie's underside runs at 2.83
                // over the rack's ground plane: a tarp there hangs to
                // knee height right beside the pavement.
                Vector3 tie = origin +
                    (forward * 0.75f) +
                    (Vector3.up * (2.83f + PinSink));
                cloths.Add(new CityWindDressingClothDescriptor(
                    $"wind-industrial-rack-tarp-{tarpOrdinal:00}",
                    CityWindDressingKind.RackTarp,
                    CityWindDressingZone.Industrial,
                    tie + (tangent * (rackWidth * 0.22f * side)),
                    YawFromForward(forward),
                    1.5f,
                    1.7f,
                    TarpDark,
                    1 + (int)(hash % 2u),
                    5,
                    7,
                    false));
                cloths.Add(new CityWindDressingClothDescriptor(
                    $"wind-industrial-sling-{tarpOrdinal:00}",
                    CityWindDressingKind.SlingRopeEnd,
                    CityWindDressingZone.Industrial,
                    tie + (tangent * (rackWidth * 0.42f * -side)),
                    YawFromForward(forward),
                    0.07f,
                    0.8f,
                    TarredRope,
                    0,
                    1,
                    6,
                    false));
                tarpOrdinal++;
            }
        }

        /// <summary>
        /// Nightlife: faded banner rags off up to eight fire-escape
        /// rails plus two rope ends. The fade is in cloth and paper,
        /// never in neon.
        /// </summary>
        private static void AddNightlife(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CityDecorationPlan decorationPlan)
        {
            List<CityDecorationDescriptor> escapes = CollectDecorations(
                decorationPlan,
                CityDecorationKind.NightlifeFireEscape);
            int escapeStride = StrideFor(escapes.Count, 8);
            int bannerOrdinal = 0;
            int ropeOrdinal = 0;
            for (int index = 0;
                 index < escapes.Count && bannerOrdinal < 8;
                 index += escapeStride)
            {
                CityDecorationDescriptor escape = escapes[index];
                ResolveLotFrame(
                    layout,
                    escape,
                    out Vector3 origin,
                    out Vector3 forward,
                    out Vector3 tangent,
                    out float lotWidth,
                    out float lotHeight);
                float frameWidth = Mathf.Clamp(
                    lotWidth * 0.34f,
                    3.0f,
                    4.4f);
                float frameHeight = Mathf.Clamp(
                    lotHeight - 0.8f,
                    5.2f,
                    7.2f);
                uint hash = HashAt(layout.Seed, origin, BannerSalt);
                int floor = 2 + (int)(hash & 1u);
                float railY = (frameHeight * floor * 0.25f) + 0.53f;
                float lateral = frameWidth *
                    (0.10f + (0.14f * Unit(hash, 8))) *
                    ((hash & 2u) == 0u ? 1f : -1f);

                cloths.Add(new CityWindDressingClothDescriptor(
                    $"wind-nightlife-banner-{bannerOrdinal:00}",
                    CityWindDressingKind.FireEscapeBanner,
                    CityWindDressingZone.Nightlife,
                    origin +
                    (tangent * lateral) +
                    (forward * 1.14f) +
                    (Vector3.up * (railY + PinSink)),
                    YawFromForward(forward),
                    0.8f,
                    1.4f,
                    BannerMagenta,
                    1 + (int)(hash % 3u),
                    5,
                    7,
                    false));
                bannerOrdinal++;

                if (ropeOrdinal < 2)
                {
                    // A rope end off the first floor's rail underside.
                    cloths.Add(new CityWindDressingClothDescriptor(
                        $"wind-nightlife-rope-end-{ropeOrdinal:00}",
                        CityWindDressingKind.RopeEnd,
                        CityWindDressingZone.Nightlife,
                        origin +
                        (tangent * (frameWidth * -0.36f)) +
                        (forward * 1.14f) +
                        (Vector3.up *
                            ((frameHeight * 0.25f) + 0.53f + PinSink)),
                        YawFromForward(forward),
                        0.07f,
                        0.7f,
                        TarredRope,
                        0,
                        1,
                        6,
                        false));
                    ropeOrdinal++;
                }
            }

            // No billboard skirts: on the default city every nightlife
            // billboard rides a tower whose roof is ~50 m up, where a
            // torn hem is sub-pixel. The banners carry the district's
            // fade instead.
        }

        /// <summary>
        /// The park hangs exactly one remnant pennant off the
        /// bandstand's eave: §10's emptiness matters more than the
        /// object count, and the chess corner stays untouched.
        /// </summary>
        private static void AddPark(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CityDecorationPlan decorationPlan)
        {
            List<CityDecorationDescriptor> bandstands =
                CollectDecorations(
                    decorationPlan,
                    CityDecorationKind.ParkBandstand);
            if (bandstands.Count == 0)
            {
                return;
            }

            CityDecorationDescriptor bandstand = bandstands[0];
            Vector3 forward = CardinalForward(bandstand.Forward);
            Vector3 tangent = TangentOf(forward);
            Vector3 origin = bandstand.Position;
            uint hash = HashAt(layout.Seed, origin, BannerSalt);

            // The eave slab's underside runs at 4.00, half-extents
            // 3.675 along the tangent and 3.075 along forward.
            cloths.Add(new CityWindDressingClothDescriptor(
                "wind-park-bandstand-pennant-00",
                CityWindDressingKind.BandstandPennant,
                CityWindDressingZone.Park,
                origin +
                (tangent *
                    (3.30f * ((hash & 1u) == 0u ? 1f : -1f))) +
                (forward * 2.70f) +
                (Vector3.up * (4.00f + PinSink)),
                YawFromForward(forward),
                0.28f,
                0.55f,
                PennantPaper,
                1,
                3,
                6,
                false));
        }

        /// <summary>
        /// Seacoast: net rags over the pier's rail cap and tarred
        /// mooring ends off the slipway chain. The pier head stays
        /// clear — the fisherman's composition there is authored.
        /// </summary>
        private static void AddSeacoast(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CitySeacoastPlan seacoastPlan)
        {
            if (seacoastPlan == null)
            {
                return;
            }

            if (seacoastPlan.TryGetPart(
                    "seacoast-pier-rail",
                    out CitySeacoastPartDescriptor rail))
            {
                // Derive the pier frame back from the rail cap: its
                // centre rides 0.89 over the deck, its length is the
                // pier span less 0.7.
                float span = rail.Size.z + 0.7f;
                float rootZ = rail.Center.z - (span * 0.5f);
                uint hash = HashAt(layout.Seed, rail.Center, PierSalt);
                for (int index = 0; index < 2; index++)
                {
                    // Root-half positions only; the head half belongs
                    // to the fisherman.
                    float along = Mathf.Lerp(
                        rootZ + 1.6f,
                        rail.Center.z + 0.8f,
                        index == 0
                            ? 0.18f + (0.14f * Unit(hash, 0))
                            : 0.62f + (0.14f * Unit(hash, 8)));
                    cloths.Add(new CityWindDressingClothDescriptor(
                        $"wind-seacoast-pier-net-{index:00}",
                        CityWindDressingKind.PierNet,
                        CityWindDressingZone.Seacoast,
                        new Vector3(
                            rail.Center.x - 0.05f,
                            rail.Center.y + 0.05f - PinSink,
                            along),
                        // The rail runs along Z; the net faces the
                        // open water side.
                        -90f,
                        0.8f,
                        0.55f,
                        NetGreen,
                        1 + (int)((hash >> index) % 3u),
                        5,
                        5,
                        false));
                }
            }

            // The slipway mooring chain is the long horizontal
            // Bollard-kind part; the two posts are the short ones.
            CitySeacoastPartDescriptor chain = default;
            bool hasChain = false;
            for (int index = 0;
                 index < seacoastPlan.Parts.Count;
                 index++)
            {
                CitySeacoastPartDescriptor part =
                    seacoastPlan.Parts[index];
                if (part.Kind == CitySeacoastPartKind.Bollard &&
                    part.Size.x > 1f)
                {
                    chain = part;
                    hasChain = true;
                    break;
                }
            }

            if (hasChain)
            {
                uint hash = HashAt(layout.Seed, chain.Center, PierSalt);
                for (int index = 0; index < 2; index++)
                {
                    float lateral =
                        (0.55f + (0.50f * Unit(hash, index * 8))) *
                        (index == 0 ? -1f : 1f);
                    cloths.Add(new CityWindDressingClothDescriptor(
                        $"wind-seacoast-mooring-end-{index:00}",
                        CityWindDressingKind.MooringRopeEnd,
                        CityWindDressingZone.Seacoast,
                        chain.Center +
                        (Vector3.right * lateral) +
                        (Vector3.up * (0.045f - PinSink)),
                        0f,
                        0.07f,
                        0.5f,
                        TarredRope,
                        0,
                        1,
                        5,
                        false));
                }
            }
        }

        /// <summary>
        /// Cemetery: two narrow wreath ribbons tied to enclosure
        /// corner posts, preferring graves that carry an offering.
        /// Ribbon-small on purpose — the small area answers the wind
        /// weakly, which is the zone's quiet.
        /// </summary>
        private static void AddCemetery(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CityCemeteryPlan cemeteryPlan)
        {
            if (cemeteryPlan == null)
            {
                return;
            }

            var offeringOrdinals = new HashSet<int>();
            var posts = new List<CityCemeteryPartDescriptor>();
            for (int index = 0;
                 index < cemeteryPlan.Parts.Count;
                 index++)
            {
                CityCemeteryPartDescriptor part =
                    cemeteryPlan.Parts[index];
                if (part.Kind == CityCemeteryPartKind.GraveOffering)
                {
                    offeringOrdinals.Add(part.GraveOrdinal);
                }
                else if (part.Kind ==
                         CityCemeteryPartKind.GraveEnclosure &&
                         part.StableId.EndsWith(
                             "-rail-post-a",
                             StringComparison.Ordinal))
                {
                    posts.Add(part);
                }
            }

            // Offering graves first: a ribbon belongs where somebody
            // still visits.
            posts.Sort((left, right) =>
            {
                bool leftVisited =
                    offeringOrdinals.Contains(left.GraveOrdinal);
                bool rightVisited =
                    offeringOrdinals.Contains(right.GraveOrdinal);
                if (leftVisited != rightVisited)
                {
                    return leftVisited ? -1 : 1;
                }

                return string.Compare(
                    left.StableId,
                    right.StableId,
                    StringComparison.Ordinal);
            });

            for (int index = 0;
                 index < posts.Count && index < 2;
                 index++)
            {
                CityCemeteryPartDescriptor post = posts[index];
                float yaw = post.Rotation.eulerAngles.y;
                // Post-a is the grave's -X corner; the ribbon hangs
                // just outside it.
                Vector3 outward = post.Rotation * Vector3.left;
                cloths.Add(new CityWindDressingClothDescriptor(
                    $"wind-cemetery-ribbon-{index:00}",
                    CityWindDressingKind.WreathRibbon,
                    CityWindDressingZone.Cemetery,
                    post.Center +
                    (outward * 0.06f) +
                    (Vector3.up * ((post.Size.y * 0.5f) - PinSink)),
                    yaw,
                    0.10f,
                    0.45f,
                    RibbonDark,
                    0,
                    1,
                    5,
                    false));
            }
        }

        /// <summary>
        /// Fringe yards: a service tarp on the industrial belt's
        /// repair gantry and on an east-edge utility shed, plus dead
        /// cable tails off utility crossarms. The tunnel forecourt,
        /// the flood works and the stone terraces hang nothing —
        /// their vocabularies forbid it.
        /// </summary>
        private static void AddFringeYards(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CityFringeYardPlan fringeYardPlan)
        {
            if (fringeYardPlan == null || !fringeYardPlan.IsEnabled)
            {
                return;
            }

            var crossarms = new List<CityFringeYardPartDescriptor>();
            for (int yardIndex = 0;
                 yardIndex < fringeYardPlan.Yards.Count;
                 yardIndex++)
            {
                CityFringeYardDescriptor yard =
                    fringeYardPlan.Yards[yardIndex];
                if (yard.Kind ==
                        CityFringeYardKind.SouthTunnelForecourt ||
                    yard.Kind == CityFringeYardKind.SouthFloodWorks ||
                    yard.Kind == CityFringeYardKind.WestStoneTerraces)
                {
                    continue;
                }

                if (yard.Kind == CityFringeYardKind.WestIndustrialBelt)
                {
                    AddRepairFrameTarp(cloths, layout, yard);
                }

                if (yard.Kind == CityFringeYardKind.EastUtilityEdge)
                {
                    AddShedTarp(cloths, layout, yard);
                }

                for (int partIndex = 0;
                     partIndex < yard.Parts.Count;
                     partIndex++)
                {
                    if (yard.Parts[partIndex].Kind ==
                        CityFringeYardPartKind.UtilityCrossarm)
                    {
                        crossarms.Add(yard.Parts[partIndex]);
                    }
                }
            }

            int stride = Mathf.Max(1, crossarms.Count / 3);
            int tailCount = 0;
            for (int index = 0;
                 index < crossarms.Count && tailCount < 3;
                 index += stride)
            {
                CityFringeYardPartDescriptor arm = crossarms[index];
                uint hash = HashAt(layout.Seed, arm.Center, TailSalt);
                // The 1.45 arm length rides local X; the tail hangs
                // off one end, under the arm.
                Vector3 along = arm.Rotation * Vector3.right;
                Vector3 face = arm.Rotation * Vector3.forward;
                cloths.Add(new CityWindDressingClothDescriptor(
                    $"wind-fringe-cable-tail-{tailCount:00}",
                    CityWindDressingKind.CableTail,
                    CityWindDressingZone.FringeYards,
                    arm.Center +
                    (along * (0.62f * ((hash & 1u) == 0u ? 1f : -1f))) +
                    (Vector3.up * (-0.06f + PinSink)),
                    YawFromForward(face),
                    0.06f,
                    0.8f,
                    DeadCable,
                    0,
                    1,
                    6,
                    false));
                tailCount++;
            }
        }

        private static void AddRepairFrameTarp(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CityFringeYardDescriptor yard)
        {
            // The gantry beam is the high horizontal RepairFrame part;
            // posts are vertical, the pipe cradle is low.
            CityFringeYardPartDescriptor beam = default;
            bool hasBeam = false;
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor part = yard.Parts[index];
                if (part.Kind != CityFringeYardPartKind.RepairFrame ||
                    part.Size.z < 2f)
                {
                    continue;
                }

                if (!hasBeam || part.Center.y > beam.Center.y)
                {
                    beam = part;
                    hasBeam = true;
                }
            }

            if (!hasBeam)
            {
                return;
            }

            uint hash = HashAt(layout.Seed, beam.Center, GantrySalt);
            Vector3 along = beam.Rotation * Vector3.forward;
            Vector3 face = Vector3.Cross(Vector3.up, along);
            cloths.Add(new CityWindDressingClothDescriptor(
                "wind-fringe-tarp-repair-frame",
                CityWindDressingKind.ServiceTarp,
                CityWindDressingZone.FringeYards,
                beam.Center +
                (along *
                    (1.1f * ((hash & 1u) == 0u ? 1f : -1f))) +
                (Vector3.up *
                    ((beam.Size.y * -0.5f) + PinSink)),
                YawFromForward(face),
                1.2f,
                1.5f,
                ServiceCanvas,
                1 + (int)(hash % 2u),
                5,
                7,
                false));
        }

        private static void AddShedTarp(
            ICollection<CityWindDressingClothDescriptor> cloths,
            CityLayout layout,
            CityFringeYardDescriptor yard)
        {
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor shed = yard.Parts[index];
                if (shed.Kind != CityFringeYardPartKind.UtilityShed)
                {
                    continue;
                }

                uint hash = HashAt(layout.Seed, shed.Center, GantrySalt);
                // The door hangs on the shed's -X face; the tarp
                // takes the blind +X wall.
                Vector3 outward = shed.Rotation * Vector3.right;
                Vector3 alongWall = shed.Rotation * Vector3.forward;
                cloths.Add(new CityWindDressingClothDescriptor(
                    "wind-fringe-tarp-shed",
                    CityWindDressingKind.ServiceTarp,
                    CityWindDressingZone.FringeYards,
                    shed.Center +
                    (outward * ((shed.Size.x * 0.5f) + 0.03f)) +
                    (alongWall *
                        (1.6f * ((hash & 1u) == 0u ? 1f : -1f))) +
                    (Vector3.up *
                        ((shed.Size.y * 0.5f) - PinSink)),
                    YawFromForward(outward),
                    1.2f,
                    1.5f,
                    ServiceCanvas,
                    1 + (int)(hash % 2u),
                    5,
                    7,
                    false));
                return;
            }
        }

        private static List<CityDecorationDescriptor> CollectDecorations(
            CityDecorationPlan plan,
            CityDecorationKind kind)
        {
            var matches = new List<CityDecorationDescriptor>();
            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                if (plan.Descriptors[index].Kind == kind)
                {
                    matches.Add(plan.Descriptors[index]);
                }
            }

            matches.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            return matches;
        }

        /// <summary>
        /// The decoration recipes' own frame: forward snapped to a
        /// cardinal axis, width read across the facade, and a facade
        /// anchor's Y re-seated on the lot's ground plane — the same
        /// rules <c>CityDecorationWorldBuilder</c> applies, so hang
        /// points land on the drawn geometry.
        /// </summary>
        private static void ResolveLotFrame(
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            out Vector3 origin,
            out Vector3 forward,
            out Vector3 tangent,
            out float width,
            out float height)
        {
            forward = CardinalForward(descriptor.Forward);
            tangent = TangentOf(forward);
            origin = descriptor.Position;
            if (descriptor.TryResolveLot(layout, out BuildingLot lot))
            {
                width = Mathf.Abs(forward.x) > 0.5f
                    ? lot.Size.y
                    : lot.Size.x;
                height = lot.Height;
                if (descriptor.AnchorKind ==
                    CityDecorationAnchorKind.BuildingFacade)
                {
                    origin.y = lot.Center.y;
                }
            }
            else
            {
                // The decoration recipes' own no-lot fallback frame.
                width = 8f;
                height = 7f;
            }
        }

        /// <summary>
        /// An even-spread pick: taking every n-th of a sorted anchor
        /// list covers the whole district instead of clustering at
        /// the list's head.
        /// </summary>
        private static int StrideFor(int count, int take)
        {
            return Mathf.Max(1, count / Mathf.Max(1, take));
        }

        private static Vector3 CardinalForward(Vector3 forward)
        {
            return Mathf.Abs(forward.x) > Mathf.Abs(forward.z)
                ? new Vector3(Mathf.Sign(forward.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(forward.z));
        }

        private static Vector3 TangentOf(Vector3 forward)
        {
            return new Vector3(-forward.z, 0f, forward.x);
        }

        /// <summary>
        /// The yaw whose panel face (+Z) points along
        /// <paramref name="forward"/>.
        /// </summary>
        private static float YawFromForward(Vector3 forward)
        {
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private static float SampleGroundOrFallback(
            CityLayout layout,
            Vector3 position)
        {
            return CityTerrainSurfacePlan.TrySampleGroundTop(
                layout,
                new Vector2(position.x, position.z),
                out float top,
                out _)
                ? top
                : position.y;
        }

        private static float DistanceToRect(Vector2 point, Rect rect)
        {
            float dx = Mathf.Max(
                Mathf.Max(rect.xMin - point.x, 0f),
                point.x - rect.xMax);
            float dy = Mathf.Max(
                Mathf.Max(rect.yMin - point.y, 0f),
                point.y - rect.yMax);
            return Mathf.Sqrt((dx * dx) + (dy * dy));
        }

        private static uint HashAt(
            int seed,
            Vector3 position,
            uint salt)
        {
            return StableHash(
                seed,
                Mathf.RoundToInt(position.x),
                Mathf.RoundToInt(position.z),
                salt);
        }

        /// <summary>A 0..1 unit float from eight hash bits.</summary>
        private static float Unit(uint hash, int shift)
        {
            return ((hash >> shift) & 0xFFu) / 255f;
        }

        private static uint StableHash(
            int seed,
            int x,
            int z,
            uint salt)
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
