using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Plans the cemetery precinct: gravel alleys radiating from the
    /// street gate, six authored grave silhouettes scattered with
    /// deterministic jitter, a wrought-iron fence with an arched gate,
    /// hash-seeded birches and firs, and a handful of night-scaled
    /// alley lamps. Pure data: the world builder materialises it.
    ///
    /// Everything is derived from stateless integer hashes of the city
    /// seed and grid indices, so a seed always buries the same people
    /// under the same stones.
    /// </summary>
    public static class CityCemeteryPlanner
    {
        private const float FenceThickness = 0.16f;
        private const float FencePostSpacing = 3.2f;
        private const float AccessClearance = 0.45f;
        private const float SpatialChunkSize = 48f;

        // The dressed interior stays off the fence line so monuments
        // and trees never poke through the railing.
        private const float FenceInset = 1.6f;
        private const float MainAlleyHalfWidth = 1.3f;
        private const float CrossAlleyHalfWidth = 0.9f;
        private const float CrossAlleySpacing = 20f;
        private const float AlleyThickness = 0.07f;

        // Grave grid pitch: wide enough for an enclosure (2.6 x 3.4 m
        // envelope) plus walking room between neighbouring plots.
        private const float GraveColumnPitch = 4.0f;
        private const float GraveRowPitch = 5.0f;
        private const int GraveAcceptPercent = 48;

        // Nearly-open gate leaves: 8 degrees off the alley axis keeps
        // the swung leaf's lateral reach (sin 8 * length + thickness)
        // inside the 0.35 m margin between the pillar centre and the
        // expanded street approach, so the validator never trips.
        private const float GateLeafOpenAngle = 8f;

        // One dim mantle roughly every street-lamp-and-a-half along
        // the main alley: enough to walk it at night, few enough that
        // the cemetery glows instead of shining.
        private const float LampSpacing = 15.4f;

        // ASCII salts, one per decision, in the project convention.
        private const uint GraveAcceptSalt = 0x47414343u;  // "GACC"
        private const uint GraveVariantSalt = 0x47564152u; // "GVAR"
        private const uint GraveDetailSalt = 0x47444554u;  // "GDET"
        private const uint TreeSalt = 0x54524545u;         // "TREE"
        private const uint BushSalt = 0x42555348u;         // "BUSH"

        /// <summary>
        /// Returns null when the layout carries no dressable cemetery:
        /// no cemetery ground, no street access, or cells that do not
        /// form one solid rectangle (the same silent bail the original
        /// open-area pass used, now visible to the caller).
        /// </summary>
        public static CityCemeteryPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var surfaces = new List<CitySurfaceDescriptor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Feature == CityAreaFeatureKind.Cemetery &&
                    surface.Kind == CitySurfaceKind.CemeteryGround)
                {
                    surfaces.Add(surface);
                }
            }

            if (surfaces.Count == 0 ||
                !TryGetAccess(
                    layout,
                    out CityOpenAreaAccessDescriptor access))
            {
                return null;
            }

            string cemeteryAreaId = surfaces[0].AreaId;
            int minimumCellX = int.MaxValue;
            int maximumCellX = int.MinValue;
            int minimumCellZ = int.MaxValue;
            int maximumCellZ = int.MinValue;
            for (int index = 0; index < surfaces.Count; index++)
            {
                if (!string.Equals(
                        surfaces[index].AreaId,
                        cemeteryAreaId,
                        StringComparison.Ordinal))
                {
                    return null;
                }

                Vector2Int cell = surfaces[index].Cell;
                minimumCellX = Mathf.Min(minimumCellX, cell.x);
                maximumCellX = Mathf.Max(maximumCellX, cell.x);
                minimumCellZ = Mathf.Min(minimumCellZ, cell.y);
                maximumCellZ = Mathf.Max(maximumCellZ, cell.y);
            }

            int expectedCellCount =
                (maximumCellX - minimumCellX + 1) *
                (maximumCellZ - minimumCellZ + 1);
            if (surfaces.Count != expectedCellCount)
            {
                return null;
            }

            Rect grounds = surfaces[0].WorldBounds;
            for (int index = 1; index < surfaces.Count; index++)
            {
                grounds = Rect.MinMaxRect(
                    Mathf.Min(grounds.xMin, surfaces[index].WorldBounds.xMin),
                    Mathf.Min(grounds.yMin, surfaces[index].WorldBounds.yMin),
                    Mathf.Max(grounds.xMax, surfaces[index].WorldBounds.xMax),
                    Mathf.Max(grounds.yMax, surfaces[index].WorldBounds.yMax));
            }

            float groundTopY = surfaces[0].DatumY +
                               CityElevationPlan.GroundTopOffset;
            var frame = new Frame(grounds, groundTopY, access);
            var parts = new List<CityCemeteryPartDescriptor>(460);
            var lamps = new List<CityCemeteryLampDescriptor>(3);

            List<Rect> alleys = CreateAlleys(frame);
            EmitAlleys(parts, frame, alleys);
            AddFenceAndGate(parts, frame, access);
            // Lamps and benches claim their alley-side spots first so
            // graves and trees are planned around them, never through
            // them.
            AddLamps(lamps, frame, alleys, access);
            List<Rect> reserved = CreatereservedFootprints(lamps);
            // The watchman's lodge claims its gate-side pocket before
            // benches, graves and trees, so the whole dressing plans
            // around it the way it plans around the lamps. Its porch
            // bulb joins the lamp list afterwards: it hangs under the
            // lodge's own eave, well inside the pocket, so it needs no
            // reserved footprint of its own.
            AddLodge(parts, lamps, frame, reserved);
            AddBenches(parts, frame, alleys, reserved, access);
            List<GraveSite> graves = AddGraves(
                parts,
                frame,
                layout.Seed,
                alleys,
                reserved,
                access);
            AddTrees(
                parts, frame, layout.Seed, alleys, reserved,
                graves, access);
            AddBushes(parts, frame, layout.Seed, alleys, graves, access);

            var plan = new CityCemeteryPlan(
                parts,
                lamps,
                grounds,
                groundTopY);
            ValidateOrThrow(layout, plan);
            return plan;
        }

        public static void ValidateOrThrow(
            CityLayout layout,
            CityCemeteryPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Count > CityCemeteryPlan.MaximumPartCount)
            {
                throw new InvalidOperationException(
                    "Cemetery dressing exceeds its bounded part count.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var alleyRects = new List<Rect>();
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = plan.Parts[index];
                if (part.Kind == CityCemeteryPartKind.Alley)
                {
                    alleyRects.Add(ToXZRect(part));
                }
            }

            // Fence-line parts (pillars, rails) straddle the boundary,
            // so containment is checked against a slightly grown rect.
            Rect containment = Expand(plan.Grounds, 0.65f);
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = plan.Parts[index];
                if (string.IsNullOrWhiteSpace(part.StableId) ||
                    !ids.Add(part.StableId) ||
                    !IsPositiveFinite(part.Size) ||
                    !IsFinite(part.Center) ||
                    !IsFinite(part.Rotation))
                {
                    throw new InvalidOperationException(
                        "Cemetery parts require unique IDs and finite " +
                        "positive transforms.");
                }

                Rect footprint = ToXZRect(part);
                if (footprint.xMin < containment.xMin ||
                    footprint.xMax > containment.xMax ||
                    footprint.yMin < containment.yMin ||
                    footprint.yMax > containment.yMax)
                {
                    throw new InvalidOperationException(
                        $"Cemetery part '{part.StableId}' leaves the " +
                        "cemetery grounds.");
                }

                if (part.GraveOrdinal >= 0)
                {
                    for (int alleyIndex = 0;
                         alleyIndex < alleyRects.Count;
                         alleyIndex++)
                    {
                        if (OverlapsStrict(
                                footprint,
                                alleyRects[alleyIndex]))
                        {
                            throw new InvalidOperationException(
                                $"Cemetery part '{part.StableId}' " +
                                "stands on an alley.");
                        }
                    }
                }

                if (!part.BlocksMovement ||
                    GetMinimumWorldY(part) >= plan.GroundTopY + 2.1f)
                {
                    // The gate arch spans the entrance well above head
                    // height; only ground-level blockers must keep the
                    // canonical street approach walkable.
                    continue;
                }

                for (int accessIndex = 0;
                     accessIndex < layout.OpenAreaAccesses.Count;
                     accessIndex++)
                {
                    CityOpenAreaAccessDescriptor access =
                        layout.OpenAreaAccesses[accessIndex];
                    if (access.Feature != CityAreaFeatureKind.Cemetery)
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
                            $"Cemetery part '{part.StableId}' blocks " +
                            "its canonical street approach.");
                    }
                }
            }

            var lampIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Lamps.Count; index++)
            {
                CityCemeteryLampDescriptor lamp = plan.Lamps[index];
                if (string.IsNullOrWhiteSpace(lamp.StableId) ||
                    !lampIds.Add(lamp.StableId) ||
                    !IsFinite(lamp.GroundPosition) ||
                    !plan.Grounds.Contains(new Vector2(
                        lamp.GroundPosition.x,
                        lamp.GroundPosition.z)))
                {
                    throw new InvalidOperationException(
                        "Cemetery lamps require unique IDs and must " +
                        "stand inside the grounds.");
                }
            }

            // A lodge always lights its own doorway, and a plot
            // without a lodge carries no porch bulb to hang.
            bool hasLodge =
                plan.GetCount(CityCemeteryPartKind.Lodge) > 0;
            int porchLamps =
                plan.GetLampCount(CityCemeteryLampKind.LodgePorch);
            if (hasLodge != (porchLamps == 1))
            {
                throw new InvalidOperationException(
                    "The gate lodge lights its doorstep with exactly " +
                    "one porch lamp, and only a lodge carries one.");
            }

            // A full-size cemetery must show the whole monument
            // vocabulary and light its main alley; a degenerate one-cell
            // plot only has to bury somebody.
            if (plan.GraveCount >= 12)
            {
                for (int variant = 0; variant < 6; variant++)
                {
                    if (plan.GetGraveVariantCount(
                            (CityCemeteryGraveVariant)variant) < 1)
                    {
                        throw new InvalidOperationException(
                            "A full cemetery must contain every grave " +
                            $"variant; missing " +
                            $"{(CityCemeteryGraveVariant)variant}.");
                    }
                }

                int alleyLamps =
                    plan.GetLampCount(CityCemeteryLampKind.Alley);
                if (alleyLamps < 3 || alleyLamps > 9)
                {
                    throw new InvalidOperationException(
                        "A full cemetery lights its main alley with " +
                        "three to nine lamps.");
                }
            }
            else if (plan.GraveCount < 1)
            {
                throw new InvalidOperationException(
                    "A planned cemetery must contain at least one " +
                    "grave.");
            }
        }

        // ------------------------------------------------------------
        // alleys
        // ------------------------------------------------------------

        private static List<Rect> CreateAlleys(Frame frame)
        {
            var alleys = new List<Rect>(4);
            alleys.Add(frame.RectFromDepthLateral(
                0f,
                frame.DepthExtent - FenceInset,
                frame.GateLateral - MainAlleyHalfWidth,
                frame.GateLateral + MainAlleyHalfWidth));
            for (float depth = CrossAlleySpacing;
                 depth < frame.DepthExtent - CrossAlleySpacing * 0.55f;
                 depth += CrossAlleySpacing)
            {
                alleys.Add(frame.RectFromDepthLateral(
                    depth - CrossAlleyHalfWidth,
                    depth + CrossAlleyHalfWidth,
                    frame.LateralMin + FenceInset,
                    frame.LateralMax - FenceInset));
            }

            return alleys;
        }

        private static void EmitAlleys(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            IReadOnlyList<Rect> alleys)
        {
            for (int index = 0; index < alleys.Count; index++)
            {
                Rect alley = alleys[index];
                bool splitAlongX = alley.width >= alley.height;
                float minimum = splitAlongX ? alley.xMin : alley.yMin;
                float maximum = splitAlongX ? alley.xMax : alley.yMax;
                int piece = 0;
                float pieceMinimum = minimum;
                while (pieceMinimum < maximum - 0.1f)
                {
                    float boundary =
                        (Mathf.Floor(pieceMinimum / SpatialChunkSize) +
                         1f) * SpatialChunkSize;
                    float pieceMaximum = Mathf.Min(maximum, boundary);
                    float length = pieceMaximum - pieceMinimum;
                    float center = (pieceMinimum + pieceMaximum) * 0.5f;
                    Vector3 worldCenter = splitAlongX
                        ? new Vector3(
                            center,
                            frame.GroundTopY + AlleyThickness * 0.5f,
                            alley.center.y)
                        : new Vector3(
                            alley.center.x,
                            frame.GroundTopY + AlleyThickness * 0.5f,
                            center);
                    Vector3 size = splitAlongX
                        ? new Vector3(length, AlleyThickness, alley.height)
                        : new Vector3(alley.width, AlleyThickness, length);
                    parts.Add(new CityCemeteryPartDescriptor(
                        $"cemetery-alley-{index:D2}-{piece++}",
                        CityCemeteryPartKind.Alley,
                        CityCemeteryStyle.Gravel,
                        worldCenter,
                        Quaternion.identity,
                        size,
                        -1,
                        CityCemeteryGraveVariant.ClassicStele));
                    pieceMinimum = pieceMaximum;
                }
            }
        }

        // ------------------------------------------------------------
        // fence and gate
        // ------------------------------------------------------------

        private static void AddFenceAndGate(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            CityOpenAreaAccessDescriptor access)
        {
            Rect bounds = frame.Grounds;
            float groundTopY = frame.GroundTopY;
            // Despite its name, the access normal points from the
            // street into the grounds (the original cemetery pass and
            // the lake shore both read it that way).
            Vector3 inward = access.OutwardNormal.normalized;
            bool gateOnWest = inward.x > 0.5f;
            bool gateOnEast = inward.x < -0.5f;
            bool gateOnSouth = inward.z > 0.5f;
            bool gateOnNorth = inward.z < -0.5f;
            float gateCenter = Mathf.Abs(inward.x) > 0.5f
                ? access.Center.z
                : access.Center.x;
            float gateHalfWidth = access.Width * 0.5f +
                                  AccessClearance +
                                  0.35f;
            float gateMinimum = gateCenter - gateHalfWidth;
            float gateMaximum = gateCenter + gateHalfWidth;
            int id = 0;

            AddFenceSide(
                parts, ref id, false, bounds.xMin,
                bounds.yMin, bounds.yMax,
                gateOnWest, gateMinimum, gateMaximum, groundTopY);
            AddFenceSide(
                parts, ref id, false, bounds.xMax,
                bounds.yMin, bounds.yMax,
                gateOnEast, gateMinimum, gateMaximum, groundTopY);
            AddFenceSide(
                parts, ref id, true, bounds.yMin,
                bounds.xMin, bounds.xMax,
                gateOnSouth, gateMinimum, gateMaximum, groundTopY);
            AddFenceSide(
                parts, ref id, true, bounds.yMax,
                bounds.xMin, bounds.xMax,
                gateOnNorth, gateMinimum, gateMaximum, groundTopY);

            AddCornerPillar(parts, "a", bounds.xMin, bounds.yMin, groundTopY);
            AddCornerPillar(parts, "b", bounds.xMax, bounds.yMin, groundTopY);
            AddCornerPillar(parts, "c", bounds.xMin, bounds.yMax, groundTopY);
            AddCornerPillar(parts, "d", bounds.xMax, bounds.yMax, groundTopY);

            bool gateAlongX = gateOnWest || gateOnEast;
            float gateLine = gateAlongX
                ? (gateOnWest ? bounds.xMin : bounds.xMax)
                : (gateOnSouth ? bounds.yMin : bounds.yMax);
            AddGatePillar(
                parts, "cemetery-gate-a", gateAlongX,
                gateLine, gateMinimum, groundTopY);
            AddGatePillar(
                parts, "cemetery-gate-b", gateAlongX,
                gateLine, gateMaximum, groundTopY);

            // The arch is the far-off marker: an iron beam bridging the
            // pillars with a small name table riding its middle.
            float archLength = gateMaximum - gateMinimum + 0.58f;
            Vector3 archCenter = gateAlongX
                ? new Vector3(gateLine, groundTopY + 2.57f, gateCenter)
                : new Vector3(gateCenter, groundTopY + 2.57f, gateLine);
            Vector3 archSize = gateAlongX
                ? new Vector3(0.24f, 0.34f, archLength)
                : new Vector3(archLength, 0.34f, 0.24f);
            parts.Add(new CityCemeteryPartDescriptor(
                "cemetery-gate-arch",
                CityCemeteryPartKind.GateArch,
                CityCemeteryStyle.Iron,
                archCenter,
                Quaternion.identity,
                archSize,
                -1,
                CityCemeteryGraveVariant.ClassicStele));
            Vector3 plaqueSize = gateAlongX
                ? new Vector3(0.10f, 0.44f, 1.15f)
                : new Vector3(1.15f, 0.44f, 0.10f);
            parts.Add(new CityCemeteryPartDescriptor(
                "cemetery-gate-plaque",
                CityCemeteryPartKind.GateArch,
                CityCemeteryStyle.Iron,
                archCenter + Vector3.up * 0.39f,
                Quaternion.identity,
                plaqueSize,
                -1,
                CityCemeteryGraveVariant.ClassicStele));

            AddGateLeaves(
                parts,
                frame,
                access,
                gateAlongX,
                gateLine,
                gateMinimum,
                gateMaximum,
                inward);
        }

        private static void AddGateLeaves(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            CityOpenAreaAccessDescriptor access,
            bool gateAlongX,
            float gateLine,
            float gateMinimum,
            float gateMaximum,
            Vector3 inward)
        {
            float gapHalf = (gateMaximum - gateMinimum) * 0.5f;
            float leafLength = Mathf.Min(1.55f, gapHalf - 0.4f);
            if (leafLength < 0.7f)
            {
                return;
            }

            Vector3 lateralAxis = gateAlongX
                ? Vector3.forward
                : Vector3.right;
            float openTangent =
                Mathf.Tan(GateLeafOpenAngle * Mathf.Deg2Rad);
            for (int side = 0; side < 2; side++)
            {
                float hingeLateral = side == 0
                    ? gateMinimum
                    : gateMaximum;
                Vector3 towardCenter = side == 0
                    ? lateralAxis
                    : -lateralAxis;
                Vector3 leafDirection =
                    (inward + towardCenter * openTangent).normalized;
                Vector3 hinge = (gateAlongX
                    ? new Vector3(gateLine, 0f, hingeLateral)
                    : new Vector3(hingeLateral, 0f, gateLine)) +
                    inward * 0.25f;
                Quaternion rotation = Quaternion.LookRotation(
                    leafDirection,
                    Vector3.up);
                Vector3 frameCenter =
                    hinge + leafDirection * (leafLength * 0.5f);
                string suffix = side == 0 ? "a" : "b";
                parts.Add(new CityCemeteryPartDescriptor(
                    $"cemetery-gate-leaf-{suffix}",
                    CityCemeteryPartKind.GateLeaf,
                    CityCemeteryStyle.Iron,
                    new Vector3(
                        frameCenter.x,
                        frame.GroundTopY + 0.05f + 0.76f,
                        frameCenter.z),
                    rotation,
                    new Vector3(0.06f, 1.52f, leafLength),
                    -1,
                    CityCemeteryGraveVariant.ClassicStele));
                parts.Add(new CityCemeteryPartDescriptor(
                    $"cemetery-gate-leaf-{suffix}-lattice",
                    CityCemeteryPartKind.GateLeaf,
                    CityCemeteryStyle.Iron,
                    new Vector3(
                        frameCenter.x,
                        frame.GroundTopY + 0.24f + 0.55f,
                        frameCenter.z),
                    rotation,
                    new Vector3(0.028f, 1.10f, leafLength * 0.8f),
                    -1,
                    CityCemeteryGraveVariant.ClassicStele));
            }
        }

        private static void AddFenceSide(
            ICollection<CityCemeteryPartDescriptor> parts,
            ref int id,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            bool hasGate,
            float gateMinimum,
            float gateMaximum,
            float groundTopY)
        {
            if (!hasGate)
            {
                AddFenceRun(
                    parts, ref id, horizontal, fixedCoordinate,
                    minimum, maximum, groundTopY);
                return;
            }

            AddFenceRun(
                parts, ref id, horizontal, fixedCoordinate,
                minimum,
                Mathf.Clamp(gateMinimum, minimum, maximum),
                groundTopY);
            AddFenceRun(
                parts, ref id, horizontal, fixedCoordinate,
                Mathf.Clamp(gateMaximum, minimum, maximum),
                maximum, groundTopY);
        }

        private static void AddFenceRun(
            ICollection<CityCemeteryPartDescriptor> parts,
            ref int id,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float groundTopY)
        {
            float length = maximum - minimum;
            if (length <= 0.1f)
            {
                return;
            }

            for (int rail = 0; rail < 2; rail++)
            {
                float height = rail == 0 ? 0.52f : 1.02f;
                AddFenceRailPieces(
                    parts, ref id, horizontal, fixedCoordinate,
                    minimum, maximum, height, groundTopY);
            }

            int postCount = Mathf.Max(
                1,
                Mathf.CeilToInt(length / FencePostSpacing));
            for (int post = 0; post <= postCount; post++)
            {
                float coordinate = Mathf.Lerp(
                    minimum,
                    maximum,
                    post / (float)postCount);
                Vector3 position = horizontal
                    ? new Vector3(
                        coordinate, groundTopY + 0.66f, fixedCoordinate)
                    : new Vector3(
                        fixedCoordinate, groundTopY + 0.66f, coordinate);
                parts.Add(new CityCemeteryPartDescriptor(
                    $"cemetery-fence-post-{id++:D3}",
                    CityCemeteryPartKind.FencePost,
                    CityCemeteryStyle.Iron,
                    position,
                    Quaternion.identity,
                    new Vector3(0.18f, 1.48f, 0.18f),
                    -1,
                    CityCemeteryGraveVariant.ClassicStele));
            }
        }

        private static void AddFenceRailPieces(
            ICollection<CityCemeteryPartDescriptor> parts,
            ref int id,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float height,
            float groundTopY)
        {
            float pieceMinimum = minimum;
            while (pieceMinimum < maximum - 0.1f)
            {
                float boundary =
                    (Mathf.Floor(pieceMinimum / SpatialChunkSize) + 1f) *
                    SpatialChunkSize;
                float pieceMaximum = Mathf.Min(maximum, boundary);
                float length = pieceMaximum - pieceMinimum;
                float center = (pieceMinimum + pieceMaximum) * 0.5f;
                Vector3 size = horizontal
                    ? new Vector3(length, 0.12f, FenceThickness)
                    : new Vector3(FenceThickness, 0.12f, length);
                Vector3 position = horizontal
                    ? new Vector3(
                        center, groundTopY + height, fixedCoordinate)
                    : new Vector3(
                        fixedCoordinate, groundTopY + height, center);
                parts.Add(new CityCemeteryPartDescriptor(
                    $"cemetery-fence-rail-{id++:D3}",
                    CityCemeteryPartKind.FenceRail,
                    CityCemeteryStyle.Iron,
                    position,
                    Quaternion.identity,
                    size,
                    -1,
                    CityCemeteryGraveVariant.ClassicStele));
                pieceMinimum = pieceMaximum;
            }
        }

        private static void AddCornerPillar(
            ICollection<CityCemeteryPartDescriptor> parts,
            string suffix,
            float x,
            float z,
            float groundTopY)
        {
            parts.Add(new CityCemeteryPartDescriptor(
                $"cemetery-corner-{suffix}",
                CityCemeteryPartKind.CornerPillar,
                CityCemeteryStyle.WeatheredConcrete,
                new Vector3(x, groundTopY + 0.81f, z),
                Quaternion.identity,
                new Vector3(0.50f, 1.62f, 0.50f),
                -1,
                CityCemeteryGraveVariant.ClassicStele));
        }

        private static void AddGatePillar(
            ICollection<CityCemeteryPartDescriptor> parts,
            string stableId,
            bool gateAlongX,
            float gateLine,
            float lateral,
            float groundTopY)
        {
            Vector3 position = gateAlongX
                ? new Vector3(gateLine, groundTopY + 1.2f, lateral)
                : new Vector3(lateral, groundTopY + 1.2f, gateLine);
            parts.Add(new CityCemeteryPartDescriptor(
                stableId,
                CityCemeteryPartKind.GatePillar,
                CityCemeteryStyle.WeatheredConcrete,
                position,
                Quaternion.identity,
                new Vector3(0.58f, 2.40f, 0.58f),
                -1,
                CityCemeteryGraveVariant.ClassicStele));
        }

        // ------------------------------------------------------------
        // graves
        // ------------------------------------------------------------

        // ------------------------------------------------------------
        // the watchman's lodge
        // ------------------------------------------------------------

        /// <summary>The whole lodge including its roof overhang must
        /// fit strictly inside the grounds on the chosen side of the
        /// gate; a narrower custom blueprint simply has no lodge and
        /// the watchman plan degrades to absent.</summary>
        private const float LodgeRequiredLateralRoom = 8.30f;

        /// <summary>Lateral offset of the doorway centre from the main
        /// alley, on the lodge side: the doorway, its step, its porch
        /// bulb and the watchman's own post all line up on it.</summary>
        private const float LodgeDoorLateral = 7.42f;

        /// <summary>Depth of the porch bulb: clear of the rear wall
        /// face (3.10 m) and still under the roof's eave (3.35 m), so
        /// the swung door leaf passes in front of it untouched.</summary>
        private const float LodgePorchLampDepth = 3.20f;

        /// <summary>Lateral offset of the porch bulb: beside the
        /// doorway on the alley side, over the solid 1.36 m stretch of
        /// the rear wall (offsets 5.60–6.96) rather than over the
        /// opening itself. The other jamb has 0.12 m to the corner and
        /// nothing to hang a bracket on.</summary>
        private const float LodgePorchLampLateral = 6.60f;

        /// <summary>
        /// The cemetery watchman's booth just inside the gate: a small
        /// timber-floored hut with a window on the alley side (he
        /// watches every arrival), an ajar door at the back, a stove
        /// pipe and a stool. Every blocking part keeps its lateral
        /// edge past the gate opening's raw approach rectangle, so the
        /// canonical street entrance stays untouched. The pocket is
        /// appended to the reserved footprints so benches, graves and
        /// trees plan around the lodge, and a porch bulb is appended
        /// to the lamp list so the doorstep is lit after dark.
        /// </summary>
        private static void AddLodge(
            ICollection<CityCemeteryPartDescriptor> parts,
            ICollection<CityCemeteryLampDescriptor> lamps,
            Frame frame,
            ICollection<Rect> reservedFootprints)
        {
            float roomPositive = frame.LateralMax - frame.GateLateral;
            float roomNegative = frame.GateLateral - frame.LateralMin;
            float sideSign = roomPositive >= roomNegative ? 1f : -1f;
            float room = Mathf.Max(roomPositive, roomNegative);
            if (room < LodgeRequiredLateralRoom)
            {
                return;
            }

            void Emit(
                string suffix,
                CityCemeteryStyle style,
                float depthCenter,
                float depthSize,
                float lateralOffset,
                float lateralSize,
                float yCenter,
                float height)
            {
                Vector3 center = frame.Compose(
                    depthCenter,
                    frame.GateLateral + sideSign * lateralOffset,
                    frame.GroundTopY + yCenter);
                Vector3 size = frame.AlongX
                    ? new Vector3(depthSize, height, lateralSize)
                    : new Vector3(lateralSize, height, depthSize);
                parts.Add(new CityCemeteryPartDescriptor(
                    $"cemetery-lodge-{suffix}",
                    CityCemeteryPartKind.Lodge,
                    style,
                    center,
                    Quaternion.identity,
                    size,
                    -1,
                    CityCemeteryGraveVariant.ClassicStele));
            }

            // Floor slab and the concrete shell: a front wall toward
            // the fence, a solid outer wall, a rear wall with the
            // 0.92 m doorway under a lintel that clears head height,
            // and the alley-side wall carrying the watch window.
            Emit("base", CityCemeteryStyle.Timber,
                2.00f, 2.20f, 6.80f, 2.40f, 0.05f, 0.10f);
            Emit("wall-front", CityCemeteryStyle.WeatheredConcrete,
                0.96f, 0.12f, 6.80f, 2.40f, 1.225f, 2.25f);
            Emit("wall-out", CityCemeteryStyle.WeatheredConcrete,
                2.00f, 2.20f, 7.94f, 0.12f, 1.225f, 2.25f);
            Emit("wall-rear", CityCemeteryStyle.WeatheredConcrete,
                3.04f, 0.12f, 6.28f, 1.36f, 1.225f, 2.25f);
            Emit("door-header", CityCemeteryStyle.WeatheredConcrete,
                3.04f, 0.12f, LodgeDoorLateral, 0.92f, 2.235f, 0.23f);
            Emit("window-pier-a", CityCemeteryStyle.WeatheredConcrete,
                1.20f, 0.60f, 5.66f, 0.12f, 1.225f, 2.25f);
            Emit("window-pier-b", CityCemeteryStyle.WeatheredConcrete,
                2.80f, 0.60f, 5.66f, 0.12f, 1.225f, 2.25f);
            Emit("window-sill", CityCemeteryStyle.WeatheredConcrete,
                2.00f, 1.00f, 5.66f, 0.12f, 0.525f, 0.85f);
            Emit("window-board", CityCemeteryStyle.Timber,
                2.00f, 1.12f, 5.64f, 0.18f, 0.975f, 0.05f);
            Emit("window-head", CityCemeteryStyle.WeatheredConcrete,
                2.00f, 1.00f, 5.66f, 0.12f, 2.10f, 0.50f);
            Emit("roof", CityCemeteryStyle.Timber,
                2.00f, 2.70f, 6.80f, 2.90f, 2.42f, 0.14f);
            Emit("chimney", CityCemeteryStyle.Iron,
                2.85f, 0.14f, 7.70f, 0.14f, 2.695f, 0.55f);
            Emit("step", CityCemeteryStyle.Timber,
                3.275f, 0.35f, LodgeDoorLateral, 0.70f, 0.05f, 0.10f);
            // The stool's id deliberately never matches the
            // "cemetery-bench-*-seat" pattern, so the shared bench-sit
            // pass leaves the watchman's seat alone.
            Emit("stool", CityCemeteryStyle.Timber,
                3.35f, 0.34f, 6.40f, 0.34f, 0.24f, 0.48f);

            // The ajar timber door leaf, hinged at the doorway's outer
            // jamb and swung 35 degrees into the grounds — the gate
            // leaves' composed-direction pattern at hut scale.
            Vector3 hinge = frame.Compose(
                3.10f,
                frame.GateLateral + sideSign * 7.88f,
                frame.GroundTopY + 1.075f);
            Vector3 closeDirection =
                frame.LateralAxis * -sideSign;
            Vector3 deeperDirection = frame.AlongX
                ? new Vector3(frame.InwardSign, 0f, 0f)
                : new Vector3(0f, 0f, frame.InwardSign);
            const float openAngle = 35f * Mathf.Deg2Rad;
            Vector3 leafDirection = (
                closeDirection * Mathf.Cos(openAngle) +
                deeperDirection * Mathf.Sin(openAngle)).normalized;
            parts.Add(new CityCemeteryPartDescriptor(
                "cemetery-lodge-door-leaf",
                CityCemeteryPartKind.Lodge,
                CityCemeteryStyle.Timber,
                hinge + leafDirection * 0.43f,
                Quaternion.LookRotation(leafDirection, Vector3.up),
                new Vector3(0.04f, 1.95f, 0.86f),
                -1,
                CityCemeteryGraveVariant.ClassicStele));

            // One bulb under the eave beside the door, on the alley
            // side. Without it the old man is a silhouette in his own
            // doorway: the alley lamps stand on the far side of the
            // lodge and its roof takes their light away exactly where
            // he keeps his post. Yaw follows the frame's base heading,
            // so the fixture's local +Z points straight out of the
            // door and its light falls across the man beside it.
            lamps.Add(new CityCemeteryLampDescriptor(
                "cemetery-lodge-lamp",
                CityCemeteryLampKind.LodgePorch,
                frame.Compose(
                    LodgePorchLampDepth,
                    frame.GateLateral + sideSign * LodgePorchLampLateral,
                    frame.GroundTopY),
                frame.BaseYawDegrees));

            float pocketNear = frame.GateLateral + sideSign * 5.20f;
            float pocketFar = frame.GateLateral + sideSign * 8.30f;
            reservedFootprints.Add(frame.RectFromDepthLateral(
                0.50f,
                3.70f,
                Mathf.Min(pocketNear, pocketFar),
                Mathf.Max(pocketNear, pocketFar)));
        }

        private readonly struct GraveSite
        {
            public GraveSite(
                int ordinal,
                Vector3 ground,
                Quaternion yaw,
                Rect footprint)
            {
                Ordinal = ordinal;
                Ground = ground;
                Yaw = yaw;
                Footprint = footprint;
            }

            public int Ordinal { get; }
            public Vector3 Ground { get; }
            public Quaternion Yaw { get; }
            public Rect Footprint { get; }
        }

        private static List<Rect> CreatereservedFootprints(
            IReadOnlyList<CityCemeteryLampDescriptor> lamps)
        {
            var footprints = new List<Rect>(lamps.Count);
            for (int index = 0; index < lamps.Count; index++)
            {
                Vector3 position = lamps[index].GroundPosition;
                footprints.Add(Rect.MinMaxRect(
                    position.x - 0.45f,
                    position.z - 0.45f,
                    position.x + 0.45f,
                    position.z + 0.45f));
            }

            return footprints;
        }

        private static List<GraveSite> AddGraves(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            int seed,
            IReadOnlyList<Rect> alleys,
            IReadOnlyList<Rect> reservedFootprints,
            CityOpenAreaAccessDescriptor access)
        {
            var graves = new List<GraveSite>(64);
            int ordinal = 0;
            // Rows and columns start deep enough that the perimeter
            // tree ring (footprint half-width 1.1 m at its own inset)
            // never collides with the outermost grave envelopes.
            for (int row = 0; ; row++)
            {
                float depth = FenceInset + 3.4f + row * GraveRowPitch;
                if (depth > frame.DepthExtent - FenceInset - 2.0f)
                {
                    break;
                }

                for (int column = 0; ; column++)
                {
                    float lateral = frame.LateralMin + FenceInset +
                                    2.5f + column * GraveColumnPitch;
                    if (lateral > frame.LateralMax - FenceInset - 1.6f)
                    {
                        break;
                    }

                    uint acceptHash = StableHash(
                        seed, column, row, GraveAcceptSalt);
                    if (acceptHash % 100u >= GraveAcceptPercent)
                    {
                        continue;
                    }

                    uint detailHash = StableHash(
                        seed, column, row, GraveDetailSalt);
                    float lateralJitter =
                        ((detailHash & 0xFFu) / 255f - 0.5f) * 0.7f;
                    float depthJitter =
                        (((detailHash >> 8) & 0xFFu) / 255f - 0.5f) *
                        0.7f;
                    float yawJitter =
                        (((detailHash >> 16) & 0xFFu) / 255f - 0.5f) *
                        8f;
                    CityCemeteryStyle stoneStyle =
                        PickStoneStyle((detailHash >> 24) % 3u);

                    float graveDepth = depth + depthJitter;
                    float graveLateral = lateral + lateralJitter;
                    Rect footprint = frame.RectFromDepthLateral(
                        graveDepth - 1.85f,
                        graveDepth + 1.85f,
                        graveLateral - 1.6f,
                        graveLateral + 1.6f);
                    if (OverlapsAny(footprint, alleys, 0.5f) ||
                        OverlapsAny(footprint, reservedFootprints, 0.3f) ||
                        !IsClearOfAccess(footprint, access))
                    {
                        continue;
                    }

                    uint variantHash = StableHash(
                        seed, column, row, GraveVariantSalt);
                    // The first six accepted plots cycle through every
                    // silhouette so the row by the gate shows the whole
                    // vocabulary and small seeds still carry variety.
                    CityCemeteryGraveVariant variant = ordinal < 6
                        ? (CityCemeteryGraveVariant)ordinal
                        : PickWeightedVariant(variantHash % 100u);
                    // The rows deepest from the gate are the oldest;
                    // their monuments lean back up to six degrees.
                    float tilt =
                        graveDepth > frame.DepthExtent * 0.55f
                            ? ((variantHash >> 16) & 0xFFu) / 255f * 6f
                            : 0f;
                    bool enclosure =
                        (variantHash >> 8) % 100u < 35u &&
                        variant !=
                            CityCemeteryGraveVariant.FamilyMonument &&
                        variant !=
                            CityCemeteryGraveVariant.OvergrownSlab;
                    bool offering =
                        (variantHash >> 24) % 100u < 25u &&
                        variant !=
                            CityCemeteryGraveVariant.OvergrownSlab;

                    Vector3 ground = frame.Compose(
                        graveDepth,
                        graveLateral,
                        frame.GroundTopY);
                    Quaternion yaw = Quaternion.Euler(
                        0f,
                        frame.BaseYawDegrees + yawJitter,
                        0f);
                    EmitGrave(
                        parts,
                        ordinal,
                        ground,
                        yaw,
                        variant,
                        stoneStyle,
                        tilt,
                        enclosure,
                        offering);
                    graves.Add(new GraveSite(
                        ordinal,
                        ground,
                        yaw,
                        footprint));
                    ordinal++;
                }
            }

            return graves;
        }

        private static CityCemeteryStyle PickStoneStyle(uint pick)
        {
            switch (pick)
            {
                case 0u:
                    return CityCemeteryStyle.GraniteDark;
                case 1u:
                    return CityCemeteryStyle.MarbleLight;
                default:
                    return CityCemeteryStyle.WeatheredConcrete;
            }
        }

        private static CityCemeteryGraveVariant PickWeightedVariant(
            uint roll)
        {
            // The cross carries the largest share: this is an old
            // provincial cemetery, not a monument park.
            if (roll < 20u)
            {
                return CityCemeteryGraveVariant.ClassicStele;
            }

            if (roll < 38u)
            {
                return CityCemeteryGraveVariant.ArchedHeadstone;
            }

            if (roll < 62u)
            {
                return CityCemeteryGraveVariant.OrthodoxCross;
            }

            if (roll < 74u)
            {
                return CityCemeteryGraveVariant.Obelisk;
            }

            return roll < 84u
                ? CityCemeteryGraveVariant.FamilyMonument
                : CityCemeteryGraveVariant.OvergrownSlab;
        }

        private static void EmitGrave(
            ICollection<CityCemeteryPartDescriptor> parts,
            int ordinal,
            Vector3 ground,
            Quaternion yaw,
            CityCemeteryGraveVariant variant,
            CityCemeteryStyle stoneStyle,
            float tiltDegrees,
            bool enclosure,
            bool offering)
        {
            Quaternion tilt = Quaternion.Euler(-tiltDegrees, 0f, 0f);
            switch (variant)
            {
                case CityCemeteryGraveVariant.ClassicStele:
                    AddGravePart(
                        parts, ordinal, variant, "slab",
                        CityCemeteryPartKind.GraveSlab, stoneStyle,
                        ground, yaw, new Vector3(0f, 0f, 0f),
                        Quaternion.identity,
                        new Vector3(1.15f, 0.15f, 2.10f));
                    AddGravePart(
                        parts, ordinal, variant, "plinth",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 0.15f, 0.86f),
                        tilt,
                        new Vector3(0.78f, 0.20f, 0.38f));
                    AddGravePart(
                        parts, ordinal, variant, "stone",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 0.35f, 0.88f),
                        tilt,
                        new Vector3(0.62f, 1.05f, 0.20f));
                    break;
                case CityCemeteryGraveVariant.ArchedHeadstone:
                    AddGravePart(
                        parts, ordinal, variant, "slab",
                        CityCemeteryPartKind.GraveSlab, stoneStyle,
                        ground, yaw, Vector3.zero,
                        Quaternion.identity,
                        new Vector3(1.10f, 0.14f, 2.00f));
                    AddGravePart(
                        parts, ordinal, variant, "stone",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 0.14f, 0.84f),
                        tilt,
                        new Vector3(0.78f, 0.95f, 0.24f));
                    AddGravePart(
                        parts, ordinal, variant, "cap",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 1.09f, 0.84f),
                        tilt,
                        new Vector3(0.52f, 0.26f, 0.22f));
                    break;
                case CityCemeteryGraveVariant.OrthodoxCross:
                    AddGravePart(
                        parts, ordinal, variant, "slab",
                        CityCemeteryPartKind.GraveSlab, stoneStyle,
                        ground, yaw, Vector3.zero,
                        Quaternion.identity,
                        new Vector3(1.00f, 0.12f, 1.95f));
                    AddGravePart(
                        parts, ordinal, variant, "post",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 0.12f, 0.84f),
                        tilt,
                        new Vector3(0.14f, 1.80f, 0.14f));
                    AddGravePart(
                        parts, ordinal, variant, "bar",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 1.42f, 0.84f),
                        tilt,
                        new Vector3(0.80f, 0.13f, 0.12f));
                    // The lower slanted bar of the Orthodox cross,
                    // rolled in the cross plane.
                    AddGravePart(
                        parts, ordinal, variant, "slant",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 0.98f, 0.84f),
                        tilt * Quaternion.Euler(0f, 0f, 32f),
                        new Vector3(0.52f, 0.10f, 0.10f));
                    break;
                case CityCemeteryGraveVariant.Obelisk:
                    AddGravePart(
                        parts, ordinal, variant, "slab",
                        CityCemeteryPartKind.GraveSlab, stoneStyle,
                        ground, yaw, Vector3.zero,
                        Quaternion.identity,
                        new Vector3(1.15f, 0.14f, 2.05f));
                    AddGravePart(
                        parts, ordinal, variant, "base",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 0.14f, 0.84f),
                        tilt,
                        new Vector3(0.68f, 0.35f, 0.68f));
                    AddGravePart(
                        parts, ordinal, variant, "shaft",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 0.49f, 0.84f),
                        tilt,
                        new Vector3(0.40f, 1.15f, 0.40f));
                    AddGravePart(
                        parts, ordinal, variant, "cap",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 1.64f, 0.84f),
                        tilt,
                        new Vector3(0.22f, 0.24f, 0.22f));
                    break;
                case CityCemeteryGraveVariant.FamilyMonument:
                    AddGravePart(
                        parts, ordinal, variant, "slab",
                        CityCemeteryPartKind.GraveSlab, stoneStyle,
                        ground, yaw, Vector3.zero,
                        Quaternion.identity,
                        new Vector3(2.30f, 0.16f, 2.20f));
                    AddGravePart(
                        parts, ordinal, variant, "stone",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 0.16f, 0.88f),
                        tilt,
                        new Vector3(1.46f, 1.00f, 0.28f));
                    AddGravePart(
                        parts, ordinal, variant, "column-a",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(-0.92f, 0.16f, 0.88f),
                        tilt,
                        new Vector3(0.18f, 1.28f, 0.18f));
                    AddGravePart(
                        parts, ordinal, variant, "column-b",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0.92f, 0.16f, 0.88f),
                        tilt,
                        new Vector3(0.18f, 1.28f, 0.18f));
                    AddGravePart(
                        parts, ordinal, variant, "lintel",
                        CityCemeteryPartKind.GraveMonument, stoneStyle,
                        ground, yaw, new Vector3(0f, 1.44f, 0.88f),
                        tilt,
                        new Vector3(2.02f, 0.16f, 0.24f));
                    break;
                default:
                    AddGravePart(
                        parts, ordinal, variant, "slab",
                        CityCemeteryPartKind.GraveSlab, stoneStyle,
                        ground, yaw, Vector3.zero,
                        Quaternion.identity,
                        new Vector3(1.05f, 0.08f, 1.90f));
                    AddGravePart(
                        parts, ordinal, variant, "mound",
                        CityCemeteryPartKind.GraveMonument,
                        CityCemeteryStyle.Soil,
                        ground, yaw, new Vector3(0f, 0.08f, 0.05f),
                        Quaternion.identity,
                        new Vector3(0.80f, 0.24f, 1.45f));
                    AddGravePart(
                        parts, ordinal, variant, "tuft",
                        CityCemeteryPartKind.GraveMonument,
                        CityCemeteryStyle.Flowers,
                        ground, yaw, new Vector3(0f, 0.08f, 0.45f),
                        Quaternion.identity,
                        new Vector3(0.42f, 0.30f, 0.36f));
                    break;
            }

            if (enclosure)
            {
                // The rail band floats at knee height like a real
                // оградка, so four grounded corner posts carry it —
                // without them the band visibly hovers.
                AddGravePart(
                    parts, ordinal, variant, "rail-a",
                    CityCemeteryPartKind.GraveEnclosure,
                    CityCemeteryStyle.Iron,
                    ground, yaw, new Vector3(-1.28f, 0.24f, 0.15f),
                    Quaternion.identity,
                    new Vector3(0.06f, 0.42f, 3.10f));
                AddGravePart(
                    parts, ordinal, variant, "rail-b",
                    CityCemeteryPartKind.GraveEnclosure,
                    CityCemeteryStyle.Iron,
                    ground, yaw, new Vector3(1.28f, 0.24f, 0.15f),
                    Quaternion.identity,
                    new Vector3(0.06f, 0.42f, 3.10f));
                AddGravePart(
                    parts, ordinal, variant, "rail-c",
                    CityCemeteryPartKind.GraveEnclosure,
                    CityCemeteryStyle.Iron,
                    ground, yaw, new Vector3(0f, 0.24f, -1.40f),
                    Quaternion.identity,
                    new Vector3(2.62f, 0.42f, 0.06f));
                AddGravePart(
                    parts, ordinal, variant, "rail-d",
                    CityCemeteryPartKind.GraveEnclosure,
                    CityCemeteryStyle.Iron,
                    ground, yaw, new Vector3(0f, 0.24f, 1.70f),
                    Quaternion.identity,
                    new Vector3(2.62f, 0.42f, 0.06f));
                for (int corner = 0; corner < 4; corner++)
                {
                    float cornerX = (corner & 1) == 0 ? -1.28f : 1.28f;
                    float cornerZ = (corner & 2) == 0 ? -1.40f : 1.70f;
                    AddGravePart(
                        parts, ordinal, variant,
                        $"rail-post-{(char)('a' + corner)}",
                        CityCemeteryPartKind.GraveEnclosure,
                        CityCemeteryStyle.Iron,
                        ground, yaw,
                        new Vector3(cornerX, 0f, cornerZ),
                        Quaternion.identity,
                        new Vector3(0.07f, 0.68f, 0.07f));
                }
            }

            if (offering)
            {
                AddGravePart(
                    parts, ordinal, variant, "flowers",
                    CityCemeteryPartKind.GraveOffering,
                    CityCemeteryStyle.Flowers,
                    ground, yaw, new Vector3(0.22f, 0.16f, -0.45f),
                    Quaternion.identity,
                    new Vector3(0.30f, 0.20f, 0.30f));
            }
        }

        private static void AddGravePart(
            ICollection<CityCemeteryPartDescriptor> parts,
            int ordinal,
            CityCemeteryGraveVariant variant,
            string suffix,
            CityCemeteryPartKind kind,
            CityCemeteryStyle style,
            Vector3 ground,
            Quaternion yaw,
            Vector3 localOffset,
            Quaternion localRotation,
            Vector3 size)
        {
            Vector3 planar = yaw * new Vector3(
                localOffset.x,
                0f,
                localOffset.z);
            var center = new Vector3(
                ground.x + planar.x,
                ground.y + localOffset.y + size.y * 0.5f,
                ground.z + planar.z);
            parts.Add(new CityCemeteryPartDescriptor(
                $"cemetery-grave-{ordinal:D3}-{suffix}",
                kind,
                style,
                center,
                yaw * localRotation,
                size,
                ordinal,
                variant));
        }

        // ------------------------------------------------------------
        // vegetation
        // ------------------------------------------------------------

        private static void AddTrees(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            int seed,
            IReadOnlyList<Rect> alleys,
            IReadOnlyList<Rect> reservedFootprints,
            IReadOnlyList<GraveSite> graves,
            CityOpenAreaAccessDescriptor access)
        {
            int treeIndex = 0;

            // Perimeter ring: candidates every nine metres, hugging
            // the fence, thinned by hash so the wall of trees stays
            // ragged rather than planted.
            for (int side = 0; side < 2; side++)
            {
                float lateral = side == 0
                    ? frame.LateralMin + 1.3f
                    : frame.LateralMax - 1.3f;
                int step = 0;
                for (float depth = 4.5f;
                     depth < frame.DepthExtent - 4.5f;
                     depth += 9f, step++)
                {
                    TryAddTree(
                        parts, frame, seed, alleys, reservedFootprints,
                        graves, access, ref treeIndex, side, step,
                        depth, lateral);
                }
            }

            for (int side = 2; side < 4; side++)
            {
                float depth = side == 2
                    ? 2.0f
                    : frame.DepthExtent - 2.0f;
                int step = 0;
                for (float lateral = frame.LateralMin + 4.5f;
                     lateral < frame.LateralMax - 4.5f;
                     lateral += 9f, step++)
                {
                    // The gate span on the street side stays open.
                    if (side == 2 &&
                        lateral > frame.GateLateral - 4.2f &&
                        lateral < frame.GateLateral + 4.2f)
                    {
                        continue;
                    }

                    TryAddTree(
                        parts, frame, seed, alleys, reservedFootprints,
                        graves, access, ref treeIndex, side, step,
                        depth, lateral);
                }
            }

            // A few interior trees leaning over the cross alleys.
            int crossIndex = 0;
            for (float depth = CrossAlleySpacing;
                 depth < frame.DepthExtent - CrossAlleySpacing * 0.55f;
                 depth += CrossAlleySpacing, crossIndex++)
            {
                for (int side = 0; side < 2; side++)
                {
                    float lateral = side == 0
                        ? frame.LateralMin + 7.5f
                        : frame.LateralMax - 7.5f;
                    TryAddTree(
                        parts, frame, seed, alleys, reservedFootprints,
                        graves, access, ref treeIndex,
                        100 + crossIndex, side, depth + 2.9f, lateral);
                }
            }
        }

        private static void TryAddTree(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            int seed,
            IReadOnlyList<Rect> alleys,
            IReadOnlyList<Rect> reservedFootprints,
            IReadOnlyList<GraveSite> graves,
            CityOpenAreaAccessDescriptor access,
            ref int treeIndex,
            int hashA,
            int hashB,
            float depth,
            float lateral)
        {
            uint hash = StableHash(seed, hashA, hashB, TreeSalt);
            if (hash % 100u >= 62u)
            {
                return;
            }

            Rect footprint = frame.RectFromDepthLateral(
                depth - 1.1f,
                depth + 1.1f,
                lateral - 1.1f,
                lateral + 1.1f);
            if (OverlapsAny(footprint, alleys, 0.6f) ||
                OverlapsAny(footprint, reservedFootprints, 0.3f) ||
                !IsClearOfAccess(footprint, access))
            {
                return;
            }

            for (int index = 0; index < graves.Count; index++)
            {
                if (OverlapsStrict(footprint, graves[index].Footprint))
                {
                    return;
                }
            }

            Vector3 ground = frame.Compose(
                depth,
                lateral,
                frame.GroundTopY);
            bool birch = (hash >> 8) % 100u < 55u;
            if (birch)
            {
                AddTreePart(
                    parts, treeIndex, "trunk",
                    CityCemeteryPartKind.TreeTrunk,
                    CityCemeteryStyle.TrunkBirch,
                    ground, 0f, new Vector3(0.30f, 2.90f, 0.30f));
                AddTreePart(
                    parts, treeIndex, "crown-a",
                    CityCemeteryPartKind.TreeCrown,
                    CityCemeteryStyle.FoliageDark,
                    ground, 2.5f, new Vector3(1.55f, 2.10f, 1.55f));
                AddTreePart(
                    parts, treeIndex, "crown-b",
                    CityCemeteryPartKind.TreeCrown,
                    CityCemeteryStyle.FoliageDark,
                    ground, 4.1f, new Vector3(0.95f, 1.30f, 0.95f));
            }
            else
            {
                AddTreePart(
                    parts, treeIndex, "trunk",
                    CityCemeteryPartKind.TreeTrunk,
                    CityCemeteryStyle.TrunkDark,
                    ground, 0f, new Vector3(0.34f, 1.10f, 0.34f));
                AddTreePart(
                    parts, treeIndex, "crown-a",
                    CityCemeteryPartKind.TreeCrown,
                    CityCemeteryStyle.FoliageDark,
                    ground, 0.95f, new Vector3(2.05f, 1.50f, 2.05f));
                AddTreePart(
                    parts, treeIndex, "crown-b",
                    CityCemeteryPartKind.TreeCrown,
                    CityCemeteryStyle.FoliageDark,
                    ground, 2.15f, new Vector3(1.45f, 1.40f, 1.45f));
                AddTreePart(
                    parts, treeIndex, "crown-c",
                    CityCemeteryPartKind.TreeCrown,
                    CityCemeteryStyle.FoliageDark,
                    ground, 3.25f, new Vector3(0.85f, 1.30f, 0.85f));
            }

            treeIndex++;
        }

        private static void AddTreePart(
            ICollection<CityCemeteryPartDescriptor> parts,
            int treeIndex,
            string suffix,
            CityCemeteryPartKind kind,
            CityCemeteryStyle style,
            Vector3 ground,
            float bottomHeight,
            Vector3 size)
        {
            parts.Add(new CityCemeteryPartDescriptor(
                $"cemetery-tree-{treeIndex:D2}-{suffix}",
                kind,
                style,
                new Vector3(
                    ground.x,
                    ground.y + bottomHeight + size.y * 0.5f,
                    ground.z),
                Quaternion.identity,
                size,
                -1,
                CityCemeteryGraveVariant.ClassicStele));
        }

        private static void AddBushes(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            int seed,
            IReadOnlyList<Rect> alleys,
            IReadOnlyList<GraveSite> graves,
            CityOpenAreaAccessDescriptor access)
        {
            int bushCount = 0;
            for (int index = 0;
                 index < graves.Count && bushCount < 10;
                 index++)
            {
                GraveSite grave = graves[index];
                uint hash = StableHash(
                    seed, grave.Ordinal, 7, BushSalt);
                if (hash % 100u >= 16u)
                {
                    continue;
                }

                float side = (hash & 0x100u) == 0u ? -1f : 1f;
                Vector3 planar = grave.Yaw * new Vector3(
                    side * 1.6f,
                    0f,
                    1.75f);
                var ground = new Vector3(
                    grave.Ground.x + planar.x,
                    grave.Ground.y,
                    grave.Ground.z + planar.z);
                var size = new Vector3(0.85f, 0.62f, 0.85f);
                Rect footprint = Rect.MinMaxRect(
                    ground.x - size.x * 0.5f,
                    ground.z - size.z * 0.5f,
                    ground.x + size.x * 0.5f,
                    ground.z + size.z * 0.5f);
                Rect interior = Expand(frame.Grounds, -1.0f);
                if (OverlapsAny(footprint, alleys, 0.3f) ||
                    !IsClearOfAccess(footprint, access) ||
                    footprint.xMin < interior.xMin ||
                    footprint.xMax > interior.xMax ||
                    footprint.yMin < interior.yMin ||
                    footprint.yMax > interior.yMax)
                {
                    continue;
                }

                parts.Add(new CityCemeteryPartDescriptor(
                    $"cemetery-bush-{bushCount:D2}",
                    CityCemeteryPartKind.Bush,
                    CityCemeteryStyle.FoliageDark,
                    new Vector3(
                        ground.x,
                        ground.y + size.y * 0.5f,
                        ground.z),
                    Quaternion.identity,
                    size,
                    -1,
                    CityCemeteryGraveVariant.ClassicStele));
                bushCount++;
            }
        }

        // ------------------------------------------------------------
        // lamps
        // ------------------------------------------------------------

        private static void AddLamps(
            ICollection<CityCemeteryLampDescriptor> lamps,
            Frame frame,
            IReadOnlyList<Rect> alleys,
            CityOpenAreaAccessDescriptor access)
        {
            // Far enough off the gravel that the lamp footprint
            // (half-width 0.45) clears the alley's own 0.1 m overlap
            // guard with margin to spare.
            float edge = MainAlleyHalfWidth + 0.65f;
            float depthLimit = frame.DepthExtent - FenceInset - 0.6f;
            int index = 0;

            // A symmetric pair frames the gate, then the chain walks
            // the main alley on alternating sides.
            if (TryAddLamp(
                    lamps, frame, alleys, access, index,
                    2.6f, frame.GateLateral - edge))
            {
                index++;
            }

            if (TryAddLamp(
                    lamps, frame, alleys, access, index,
                    2.6f, frame.GateLateral + edge))
            {
                index++;
            }

            float lastDepth = 2.6f;
            for (int step = 1; ; step++)
            {
                float depth = 2.6f + step * LampSpacing;
                if (depth > depthLimit)
                {
                    break;
                }

                float side = step % 2 == 1 ? -1f : 1f;
                if (TryAddLamp(
                        lamps, frame, alleys, access, index,
                        depth, frame.GateLateral + side * edge))
                {
                    index++;
                    lastDepth = depth;
                }
            }

            // If the chain stops well short of the far fence, one more
            // mantle marks the alley's end.
            float farDepth = frame.DepthExtent - FenceInset - 1.2f;
            if (farDepth - lastDepth > LampSpacing * 0.5f)
            {
                TryAddLamp(
                    lamps, frame, alleys, access, index,
                    farDepth, frame.GateLateral - edge);
            }
        }

        private static bool TryAddLamp(
            ICollection<CityCemeteryLampDescriptor> lamps,
            Frame frame,
            IReadOnlyList<Rect> alleys,
            CityOpenAreaAccessDescriptor access,
            int index,
            float depth,
            float lateral)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                float candidateDepth = depth + attempt * 1.0f;
                if (candidateDepth >
                    frame.DepthExtent - FenceInset - 0.6f)
                {
                    return false;
                }

                Rect footprint = frame.RectFromDepthLateral(
                    candidateDepth - 0.45f,
                    candidateDepth + 0.45f,
                    lateral - 0.45f,
                    lateral + 0.45f);
                if (OverlapsAny(footprint, alleys, 0.1f) ||
                    !IsClearOfAccess(footprint, access))
                {
                    continue;
                }

                lamps.Add(new CityCemeteryLampDescriptor(
                    $"cemetery-lamp-{index:D2}",
                    CityCemeteryLampKind.Alley,
                    frame.Compose(
                        candidateDepth,
                        lateral,
                        frame.GroundTopY),
                    frame.BaseYawDegrees));
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------
        // benches
        // ------------------------------------------------------------

        /// <summary>
        /// A timber bench beside the main alley just before each cross
        /// alley, plus one near the far end: places to sit with the
        /// dead a while. Benches prefer alternating sides and flip to
        /// the other side of the alley when a lamp already holds
        /// theirs; their footprints join the reserved list so graves
        /// and trees keep clear.
        /// </summary>
        private static void AddBenches(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            IReadOnlyList<Rect> alleys,
            List<Rect> reserved,
            CityOpenAreaAccessDescriptor access)
        {
            int benchIndex = 0;
            int crossIndex = 0;
            for (float depth = CrossAlleySpacing;
                 depth < frame.DepthExtent - CrossAlleySpacing * 0.55f;
                 depth += CrossAlleySpacing, crossIndex++)
            {
                float preferredSide = crossIndex % 2 == 0 ? 1f : -1f;
                TryAddBench(
                    parts, frame, alleys, reserved, access,
                    ref benchIndex, depth - 2.6f, preferredSide);
            }

            TryAddBench(
                parts, frame, alleys, reserved, access,
                ref benchIndex,
                frame.DepthExtent - FenceInset - 3.2f,
                crossIndex % 2 == 0 ? 1f : -1f);
        }

        private static void TryAddBench(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            IReadOnlyList<Rect> alleys,
            List<Rect> reserved,
            CityOpenAreaAccessDescriptor access,
            ref int benchIndex,
            float depth,
            float preferredSide)
        {
            if (depth < FenceInset + 1.2f)
            {
                return;
            }

            for (int flip = 0; flip < 2; flip++)
            {
                float side = flip == 0 ? preferredSide : -preferredSide;
                float lateral = frame.GateLateral +
                                side * (MainAlleyHalfWidth + 0.75f);
                Rect footprint = frame.RectFromDepthLateral(
                    depth - 0.85f,
                    depth + 0.85f,
                    lateral - 0.5f,
                    lateral + 0.5f);
                if (OverlapsAny(footprint, alleys, 0.1f) ||
                    OverlapsAny(footprint, reserved, 0.2f) ||
                    !IsClearOfAccess(footprint, access))
                {
                    continue;
                }

                EmitBench(
                    parts,
                    frame,
                    benchIndex,
                    depth,
                    lateral,
                    side);
                reserved.Add(footprint);
                benchIndex++;
                return;
            }
        }

        private static void EmitBench(
            ICollection<CityCemeteryPartDescriptor> parts,
            Frame frame,
            int benchIndex,
            float depth,
            float lateral,
            float side)
        {
            Vector3 ground = frame.Compose(
                depth,
                lateral,
                frame.GroundTopY);
            // The bench faces the alley: local +Z looks across it,
            // local +X runs along the seat.
            Quaternion rotation = Quaternion.LookRotation(
                frame.LateralAxis * -side,
                Vector3.up);
            AddBenchPart(
                parts, benchIndex, "seat",
                CityCemeteryStyle.Timber,
                ground, rotation, new Vector3(0f, 0.42f, 0f),
                new Vector3(1.60f, 0.07f, 0.42f));
            AddBenchPart(
                parts, benchIndex, "back",
                CityCemeteryStyle.Timber,
                ground, rotation, new Vector3(0f, 0.50f, -0.225f),
                new Vector3(1.60f, 0.50f, 0.06f));
            AddBenchPart(
                parts, benchIndex, "leg-a",
                CityCemeteryStyle.Iron,
                ground, rotation, new Vector3(-0.64f, 0f, -0.02f),
                new Vector3(0.08f, 0.42f, 0.36f));
            AddBenchPart(
                parts, benchIndex, "leg-b",
                CityCemeteryStyle.Iron,
                ground, rotation, new Vector3(0.64f, 0f, -0.02f),
                new Vector3(0.08f, 0.42f, 0.36f));
        }

        private static void AddBenchPart(
            ICollection<CityCemeteryPartDescriptor> parts,
            int benchIndex,
            string suffix,
            CityCemeteryStyle style,
            Vector3 ground,
            Quaternion rotation,
            Vector3 localOffset,
            Vector3 size)
        {
            Vector3 planar = rotation * new Vector3(
                localOffset.x,
                0f,
                localOffset.z);
            parts.Add(new CityCemeteryPartDescriptor(
                $"cemetery-bench-{benchIndex:D2}-{suffix}",
                CityCemeteryPartKind.Bench,
                style,
                new Vector3(
                    ground.x + planar.x,
                    ground.y + localOffset.y + size.y * 0.5f,
                    ground.z + planar.z),
                rotation,
                size,
                -1,
                CityCemeteryGraveVariant.ClassicStele));
        }

        // ------------------------------------------------------------
        // frame and shared helpers
        // ------------------------------------------------------------

        /// <summary>
        /// A gate-relative coordinate frame over the axis-aligned
        /// grounds: depth grows from the gate edge into the plot,
        /// lateral runs along the gate side. All layout happens in this
        /// frame so one algorithm serves all four gate orientations.
        /// </summary>
        private readonly struct Frame
        {
            public Frame(
                Rect grounds,
                float groundTopY,
                CityOpenAreaAccessDescriptor access)
            {
                Grounds = grounds;
                GroundTopY = groundTopY;
                // The access normal points from the street into the
                // grounds; depth grows along it, away from the gate.
                Vector3 inward = access.OutwardNormal.normalized;
                AlongX = Mathf.Abs(inward.x) > 0.5f;
                InwardSign = AlongX
                    ? Mathf.Sign(inward.x)
                    : Mathf.Sign(inward.z);
                if (AlongX)
                {
                    GateEdge = InwardSign > 0f
                        ? grounds.xMin
                        : grounds.xMax;
                    DepthExtent = grounds.width;
                    LateralMin = grounds.yMin;
                    LateralMax = grounds.yMax;
                    GateLateral = access.Center.z;
                    BaseYawDegrees = InwardSign > 0f ? 90f : 270f;
                }
                else
                {
                    GateEdge = InwardSign > 0f
                        ? grounds.yMin
                        : grounds.yMax;
                    DepthExtent = grounds.height;
                    LateralMin = grounds.xMin;
                    LateralMax = grounds.xMax;
                    GateLateral = access.Center.x;
                    BaseYawDegrees = InwardSign > 0f ? 0f : 180f;
                }

                GateLateral = Mathf.Clamp(
                    GateLateral,
                    LateralMin + FenceInset + MainAlleyHalfWidth,
                    LateralMax - FenceInset - MainAlleyHalfWidth);
            }

            public Rect Grounds { get; }
            public float GroundTopY { get; }
            public bool AlongX { get; }
            public float InwardSign { get; }
            public float GateEdge { get; }
            public float DepthExtent { get; }
            public float LateralMin { get; }
            public float LateralMax { get; }
            public float GateLateral { get; }
            public float BaseYawDegrees { get; }

            /// <summary>World direction of growing lateral values.</summary>
            public Vector3 LateralAxis =>
                AlongX ? Vector3.forward : Vector3.right;

            public Vector3 Compose(
                float depth,
                float lateral,
                float y)
            {
                float depthCoordinate = GateEdge + InwardSign * depth;
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
                float first = GateEdge + InwardSign * depthMin;
                float second = GateEdge + InwardSign * depthMax;
                float depthLow = Mathf.Min(first, second);
                float depthHigh = Mathf.Max(first, second);
                return AlongX
                    ? Rect.MinMaxRect(
                        depthLow, lateralMin, depthHigh, lateralMax)
                    : Rect.MinMaxRect(
                        lateralMin, depthLow, lateralMax, depthHigh);
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
                    CityAreaFeatureKind.Cemetery)
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
        private static Rect ToXZRect(CityCemeteryPartDescriptor part)
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
            CityCemeteryPartDescriptor part)
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
