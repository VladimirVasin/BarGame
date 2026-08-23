using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Plans the lighthouse island: a rock mound offshore of the
    /// centre zone, two ruined fishermen's shacks, leaning poles, a
    /// wrecked hull, and the lighthouse itself — a banded tower four
    /// times the old mol beacon's height, carrying the rotating
    /// lantern.
    ///
    /// The island stands at the edge of visibility on purpose. From
    /// the esplanade and the waterline sand it is a breath of
    /// silhouette in the fog; from the pier head it firms up; from
    /// anywhere deeper in the city it is past the 48 m far plane and
    /// simply not there. Its geometry is fixed in world space — no
    /// camera following — so walking the shore moves it the way a
    /// real island moves: not at all.
    ///
    /// Everything here is presentation. No colliders, no walkable
    /// contribution, no real light: the fog and the far plane keep
    /// the player away more firmly than any wall.
    /// </summary>
    public static class CityLighthouseIslandPlanner
    {
        /// <summary>
        /// How far past the waterline the island anchors. The sea
        /// apron follows the island (it is one constant), so the real
        /// ceiling is the 48 m far plane: at 39 m the waterline sand
        /// still sees the whole island (~40 m to the lantern, full
        /// alpha), the pier head (~26 m) keeps its firmed-up view,
        /// and from the esplanade (~46 m) the tower's top dissolves
        /// into the self-fade band while the mound holds — the island
        /// at the very edge of what the far plane permits. Pushing
        /// past ~40 starts erasing it from the waterline itself.
        /// </summary>
        internal const float OffshoreDistance = 39f;

        /// <summary>
        /// Forced perspective on top of the physical distance: the
        /// anchor already stands as far out as the 48 m far plane
        /// permits, so the rest of "farther" is drawn — the whole
        /// silhouette at this fraction of its planned size around the
        /// sea-level anchor. The lantern's LIGHT deliberately does
        /// not follow (only the lens does, to fit the lamp room):
        /// beams at full throw and a later fade band are what keep
        /// the flash visible from the shore. The floor is the
        /// lantern invariant: at 0.78 the lamp still clears its
        /// 12 m over the sea.
        /// </summary>
        internal const float VisualScale = 0.78f;

        /// <summary>
        /// The pier points at the island: the anchor sits a step east
        /// of the pier head's own easting, clamped into the sea row.
        /// </summary>
        internal const float LateralFromPierHead = 6f;
        internal const float LateralClearance = 12f;

        // The mound: two tiers of yawed rock slabs and a few outcrop
        // blocks, feet well under the swell so no seam ever surfaces.
        internal const float MoundDeckAboveSea = 1.4f;
        private const float TierOneRadius = 3.4f;
        private const float TierTwoRadius = 1.9f;
        private const float OutcropRadius = 4.8f;

        // The tower. Height is the point: the old mol beacon stood
        // 4.15 m over its deck; this shaft alone is three times that.
        private const float TowerBaseWidth = 3.4f;
        private const float TowerBaseHeight = 1.1f;
        private const int TowerShaftSegments = 5;
        private const float TowerShaftSegmentHeight = 2.5f;
        private const float TowerShaftBottomWidth = 2.55f;
        private const float TowerShaftTopWidth = 1.55f;
        private const float GalleryWidth = 2.7f;
        private const float GalleryThickness = 0.35f;
        private const float GalleryPostHeight = 0.95f;
        private const float LampRoomFloorWidth = 1.15f;
        private const float LanternAboveGallery = 0.62f;

        // The tower shaft's day-mark banding, carried in Shade: pale
        // courses read against the fog, dark courses cut them.
        private const float PaleBandShade = 1.0f;
        private const float DarkBandShade = 0.30f;

        // ASCII salts, one per decision, in the project convention.
        private const uint IslandSalt = 0x49534C45u;  // "ISLE"
        private const uint ShackSalt = 0x5348434Bu;   // "SHCK"
        private const uint RockSalt = 0x524F434Bu;    // "ROCK"
        private const uint WreckSalt = 0x5752434Bu;   // "WRCK"
        private const uint TimberSalt = 0x544D4252u;  // "TMBR"

        /// <summary>
        /// Returns null when there is no seacoast to stand off — the
        /// legacy layouts without a dressed shore keep their empty
        /// horizon.
        /// </summary>
        public static CityLighthouseIslandPlan Create(
            int citySeed,
            CitySeacoastPlan seacoastPlan)
        {
            if (seacoastPlan == null)
            {
                return null;
            }

            CitySeacoastFrame frame = seacoastPlan.Frame;
            Vector2 anchor = ResolveAnchor(seacoastPlan);
            float seaTop = frame.SeaTopY;
            float deckTop = seaTop + MoundDeckAboveSea;

            var parts =
                new List<CityLighthouseIslandPartDescriptor>(64);
            AddMound(parts, citySeed, anchor, seaTop);
            Vector3 lantern = AddTower(parts, anchor, deckTop);
            AddShacks(parts, citySeed, anchor, seaTop);
            AddTimber(parts, citySeed, anchor, seaTop);
            AddWreck(parts, citySeed, anchor, seaTop);

            // The forced-perspective pass: everything shrinks toward
            // the sea-level anchor, so rocks stay level with the
            // swell and the whole island reads farther offshore than
            // the sea sheets let it stand.
            var pivot = new Vector3(anchor.x, seaTop, anchor.y);
            for (int index = 0; index < parts.Count; index++)
            {
                CityLighthouseIslandPartDescriptor part = parts[index];
                parts[index] = new CityLighthouseIslandPartDescriptor(
                    part.StableId,
                    part.Kind,
                    pivot + (part.Center - pivot) * VisualScale,
                    part.Rotation,
                    part.Size * VisualScale,
                    part.Shade);
            }

            lantern = pivot + (lantern - pivot) * VisualScale;

            var plan = new CityLighthouseIslandPlan(
                parts,
                new Vector3(anchor.x, seaTop, anchor.y),
                lantern);
            ValidateOrThrow(plan, frame);
            return plan;
        }

        /// <summary>
        /// Where the lantern stands, derived from the coast alone —
        /// no seed, so the map can mark the island without building
        /// it. False when the coast plan is missing.
        /// </summary>
        public static bool TryResolveLanternPosition(
            CitySeacoastPlan seacoastPlan,
            out Vector3 lanternPosition)
        {
            if (seacoastPlan == null)
            {
                lanternPosition = default;
                return false;
            }

            Vector2 anchor = ResolveAnchor(seacoastPlan);
            float seaTop = seacoastPlan.Frame.SeaTopY;
            float deckTop = seaTop + MoundDeckAboveSea;
            // Identical float operations to Create's scale pass, so
            // the derived spot equals the built plan's bit for bit.
            var pivot = new Vector3(anchor.x, seaTop, anchor.y);
            var lantern = new Vector3(
                anchor.x,
                ResolveGalleryTop(deckTop) + LanternAboveGallery,
                anchor.y);
            lanternPosition = pivot + (lantern - pivot) * VisualScale;
            return true;
        }

        private static Vector2 ResolveAnchor(
            CitySeacoastPlan seacoastPlan)
        {
            CitySeacoastFrame frame = seacoastPlan.Frame;
            float pierX = seacoastPlan.TryGetPart(
                CitySeacoastPlanner.PierDeckHeadId,
                out CitySeacoastPartDescriptor pierHead)
                ? pierHead.Center.x
                : frame.CenterZone.center.x;
            float islandX = Mathf.Clamp(
                pierX + LateralFromPierHead,
                frame.SeaRowBounds.xMin + LateralClearance,
                frame.SeaRowBounds.xMax - LateralClearance);
            return new Vector2(
                islandX,
                frame.WaterlineZ + OffshoreDistance);
        }

        private static float ResolveShaftTop(float deckTop)
        {
            return deckTop + TowerBaseHeight +
                   TowerShaftSegments * TowerShaftSegmentHeight;
        }

        private static float ResolveGalleryTop(float deckTop)
        {
            return ResolveShaftTop(deckTop) + GalleryThickness;
        }

        // ------------------------------------------------------------------
        // the mound
        // ------------------------------------------------------------------

        private static void AddMound(
            ICollection<CityLighthouseIslandPartDescriptor> parts,
            int seed,
            Vector2 anchor,
            float seaTop)
        {
            // Tier one: six yawed slabs in a ring, tops barely over
            // the swell, feet a metre under it.
            for (int index = 0; index < 6; index++)
            {
                uint hash = Hash(seed, RockSalt, index);
                float angle = index * 60f +
                              (Sample01(hash) - 0.5f) * 28f;
                float radius = TierOneRadius +
                               Sample01(hash >> 8) * 1.2f;
                float radians = angle * Mathf.Deg2Rad;
                parts.Add(Part(
                    $"island-rock-low-{index}",
                    CityLighthouseIslandPartKind.Rock,
                    new Vector3(
                        anchor.x + Mathf.Cos(radians) * radius,
                        seaTop - 0.35f,
                        anchor.y + Mathf.Sin(radians) * radius *
                            0.82f),
                    Quaternion.Euler(
                        0f,
                        Sample01(hash >> 16) * 180f,
                        0f),
                    new Vector3(
                        5.2f + Sample01(hash >> 4) * 1.8f,
                        1.7f,
                        4.2f + Sample01(hash >> 12) * 1.6f),
                    0.92f + Sample01(hash >> 20) * 0.14f));
            }

            // Tier two: the deck the tower and the shacks stand on —
            // a centre cap and four shoulders.
            parts.Add(Part(
                "island-rock-deck-center",
                CityLighthouseIslandPartKind.Rock,
                new Vector3(anchor.x, seaTop + 0.55f, anchor.y),
                Quaternion.Euler(0f, 14f, 0f),
                new Vector3(4.4f, 1.7f, 3.9f),
                1.02f));
            for (int index = 0; index < 4; index++)
            {
                uint hash = Hash(seed, RockSalt, 16 + index);
                float angle = 45f + index * 90f +
                              (Sample01(hash) - 0.5f) * 22f;
                float radius = TierTwoRadius +
                               Sample01(hash >> 8) * 0.8f;
                float radians = angle * Mathf.Deg2Rad;
                parts.Add(Part(
                    $"island-rock-deck-{index}",
                    CityLighthouseIslandPartKind.Rock,
                    new Vector3(
                        anchor.x + Mathf.Cos(radians) * radius,
                        seaTop + 0.45f,
                        anchor.y + Mathf.Sin(radians) * radius),
                    Quaternion.Euler(
                        0f,
                        Sample01(hash >> 16) * 180f,
                        0f),
                    new Vector3(
                        3.4f + Sample01(hash >> 4) * 1.2f,
                        1.6f,
                        2.9f + Sample01(hash >> 12) * 1.0f),
                    0.9f + Sample01(hash >> 20) * 0.16f));
            }

            // Outcrops: broken teeth on the rim.
            for (int index = 0; index < 4; index++)
            {
                uint hash = Hash(seed, RockSalt, 32 + index);
                float angle = 20f + index * 90f +
                              (Sample01(hash) - 0.5f) * 40f;
                float radius = OutcropRadius +
                               Sample01(hash >> 8) * 1.6f;
                float radians = angle * Mathf.Deg2Rad;
                float width = 1.2f + Sample01(hash >> 4) * 1.0f;
                parts.Add(Part(
                    $"island-rock-outcrop-{index}",
                    CityLighthouseIslandPartKind.Rock,
                    new Vector3(
                        anchor.x + Mathf.Cos(radians) * radius,
                        seaTop + 0.15f + Sample01(hash >> 12) * 0.5f,
                        anchor.y + Mathf.Sin(radians) * radius *
                            0.85f),
                    Quaternion.Euler(
                        (Sample01(hash >> 16) - 0.5f) * 16f,
                        Sample01(hash >> 20) * 180f,
                        (Sample01(hash >> 24) - 0.5f) * 16f),
                    new Vector3(width, 1.3f, width * 0.85f),
                    0.88f + Sample01(hash >> 6) * 0.18f));
            }
        }

        // ------------------------------------------------------------------
        // the lighthouse
        // ------------------------------------------------------------------

        private static Vector3 AddTower(
            ICollection<CityLighthouseIslandPartDescriptor> parts,
            Vector2 anchor,
            float deckTop)
        {
            parts.Add(Part(
                "island-tower-base",
                CityLighthouseIslandPartKind.TowerBase,
                new Vector3(
                    anchor.x,
                    deckTop + TowerBaseHeight * 0.5f,
                    anchor.y),
                Quaternion.identity,
                new Vector3(
                    TowerBaseWidth,
                    TowerBaseHeight,
                    TowerBaseWidth),
                1f));

            float shaftBottom = deckTop + TowerBaseHeight;
            for (int index = 0; index < TowerShaftSegments; index++)
            {
                float width = Mathf.Lerp(
                    TowerShaftBottomWidth,
                    TowerShaftTopWidth,
                    index / (float)(TowerShaftSegments - 1));
                parts.Add(Part(
                    $"island-tower-shaft-{index}",
                    CityLighthouseIslandPartKind.TowerShaft,
                    new Vector3(
                        anchor.x,
                        shaftBottom +
                            (index + 0.5f) * TowerShaftSegmentHeight,
                        anchor.y),
                    Quaternion.identity,
                    new Vector3(
                        width,
                        TowerShaftSegmentHeight,
                        width),
                    (index & 1) == 0 ? PaleBandShade : DarkBandShade));
            }

            float shaftTop = ResolveShaftTop(deckTop);
            parts.Add(Part(
                "island-tower-gallery",
                CityLighthouseIslandPartKind.Gallery,
                new Vector3(
                    anchor.x,
                    shaftTop + GalleryThickness * 0.5f,
                    anchor.y),
                Quaternion.identity,
                new Vector3(
                    GalleryWidth,
                    GalleryThickness,
                    GalleryWidth),
                1f));

            float galleryTop = ResolveGalleryTop(deckTop);
            for (int corner = 0; corner < 4; corner++)
            {
                float signX = (corner & 1) == 0 ? -1f : 1f;
                float signZ = (corner & 2) == 0 ? -1f : 1f;
                parts.Add(Part(
                    $"island-tower-post-{corner}",
                    CityLighthouseIslandPartKind.Gallery,
                    new Vector3(
                        anchor.x + signX * 0.98f,
                        galleryTop + GalleryPostHeight * 0.5f,
                        anchor.y + signZ * 0.98f),
                    Quaternion.identity,
                    new Vector3(0.12f, GalleryPostHeight, 0.12f),
                    1f));
            }

            parts.Add(Part(
                "island-tower-lamp-floor",
                CityLighthouseIslandPartKind.LampRoom,
                new Vector3(anchor.x, galleryTop + 0.11f, anchor.y),
                Quaternion.identity,
                new Vector3(
                    LampRoomFloorWidth,
                    0.22f,
                    LampRoomFloorWidth),
                1f));

            float roofBottom = galleryTop + GalleryPostHeight;
            parts.Add(Part(
                "island-tower-roof",
                CityLighthouseIslandPartKind.Gallery,
                new Vector3(
                    anchor.x,
                    roofBottom + 0.13f,
                    anchor.y),
                Quaternion.identity,
                new Vector3(1.6f, 0.26f, 1.6f),
                1f));
            parts.Add(Part(
                "island-tower-finial",
                CityLighthouseIslandPartKind.Gallery,
                new Vector3(
                    anchor.x,
                    roofBottom + 0.26f + 0.275f,
                    anchor.y),
                Quaternion.identity,
                new Vector3(0.14f, 0.55f, 0.14f),
                1f));

            return new Vector3(
                anchor.x,
                galleryTop + LanternAboveGallery,
                anchor.y);
        }

        // ------------------------------------------------------------------
        // the shacks
        // ------------------------------------------------------------------

        private static void AddShacks(
            ICollection<CityLighthouseIslandPartDescriptor> parts,
            int seed,
            Vector2 anchor,
            float seaTop)
        {
            // Both shacks stand on tier-one rock, clear of the tower
            // base. The first kept most of itself; the second lost
            // its roof to a storm and keeps it beside the door.
            float ground = seaTop + 0.5f;
            AddRuinedShack(
                parts,
                seed,
                0,
                new Vector2(anchor.x - 4.6f, anchor.y - 2.2f),
                18f,
                ground,
                true);
            AddRuinedShack(
                parts,
                seed,
                1,
                new Vector2(anchor.x + 4.2f, anchor.y + 1.6f),
                -27f,
                ground,
                false);
        }

        private static void AddRuinedShack(
            ICollection<CityLighthouseIslandPartDescriptor> parts,
            int seed,
            int ordinal,
            Vector2 origin,
            float yawDegrees,
            float ground,
            bool roofStillUp)
        {
            uint hash = Hash(seed, ShackSalt, ordinal);
            Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
            string prefix = $"island-shack-{ordinal}";

            Vector3 At(float localX, float y, float localZ)
            {
                Vector3 offset = yaw * new Vector3(localX, 0f, localZ);
                return new Vector3(
                    origin.x + offset.x,
                    y,
                    origin.y + offset.z);
            }

            parts.Add(Part(
                $"{prefix}-floor",
                CityLighthouseIslandPartKind.ShackWall,
                At(0f, ground + 0.08f, 0f),
                yaw,
                new Vector3(3.4f, 0.16f, 2.6f),
                0.9f + Sample01(hash) * 0.12f));
            parts.Add(Part(
                $"{prefix}-wall-back",
                CityLighthouseIslandPartKind.ShackWall,
                At(0f, ground + 0.16f + 0.95f, -1.23f),
                yaw,
                new Vector3(3.4f, 1.9f, 0.14f),
                0.86f + Sample01(hash >> 4) * 0.14f));
            parts.Add(Part(
                $"{prefix}-wall-left",
                CityLighthouseIslandPartKind.ShackWall,
                At(-1.63f, ground + 0.16f + 0.95f, 0f),
                yaw,
                new Vector3(0.14f, 1.9f, 2.6f),
                0.88f + Sample01(hash >> 8) * 0.12f));
            // The right wall leans off plumb — the ruin in one move.
            parts.Add(Part(
                $"{prefix}-wall-right",
                CityLighthouseIslandPartKind.ShackWall,
                At(1.55f, ground + 0.16f + 0.9f, 0f),
                yaw * Quaternion.Euler(
                    0f,
                    0f,
                    9f + Sample01(hash >> 12) * 6f),
                new Vector3(0.14f, 1.9f, 2.6f),
                0.84f + Sample01(hash >> 16) * 0.12f));
            // The front wall came down whole and lies where it fell.
            parts.Add(Part(
                $"{prefix}-wall-fallen",
                CityLighthouseIslandPartKind.ShackWall,
                At(0.3f, ground + 0.12f, 1.95f),
                yaw * Quaternion.Euler(
                    4f,
                    (Sample01(hash >> 20) - 0.5f) * 18f,
                    0f),
                new Vector3(3.1f, 0.14f, 1.8f),
                0.8f + Sample01(hash >> 24) * 0.12f));

            if (roofStillUp)
            {
                // Half-slid, still pitched over the walls.
                parts.Add(Part(
                    $"{prefix}-roof",
                    CityLighthouseIslandPartKind.ShackRoof,
                    At(0.5f, ground + 0.16f + 1.9f + 0.1f, 0.35f),
                    yaw * Quaternion.Euler(9f, 8f, 0f),
                    new Vector3(3.9f, 0.11f, 3.1f),
                    0.9f));
                parts.Add(Part(
                    $"{prefix}-stove-pipe",
                    CityLighthouseIslandPartKind.Timber,
                    At(-1.1f, ground + 0.16f + 1.9f + 0.55f, -0.9f),
                    yaw * Quaternion.Euler(0f, 0f, 4f),
                    new Vector3(0.16f, 0.9f, 0.16f),
                    0.85f));
            }
            else
            {
                // Down beside the doorway, one corner in the sand.
                parts.Add(Part(
                    $"{prefix}-roof",
                    CityLighthouseIslandPartKind.ShackRoof,
                    At(2.6f, ground + 0.2f, 0.9f),
                    yaw * Quaternion.Euler(3f, 24f, 6f),
                    new Vector3(3.9f, 0.11f, 3.1f),
                    0.82f));
            }
        }

        // ------------------------------------------------------------------
        // the scatter
        // ------------------------------------------------------------------

        private static void AddTimber(
            ICollection<CityLighthouseIslandPartDescriptor> parts,
            int seed,
            Vector2 anchor,
            float seaTop)
        {
            // Leaning poles on the rim: what is left of drying racks
            // and a flag mast nobody lowered.
            for (int index = 0; index < 5; index++)
            {
                uint hash = Hash(seed, TimberSalt, index);
                float angle = 30f + index * 72f +
                              (Sample01(hash) - 0.5f) * 34f;
                float radius = 4.2f + Sample01(hash >> 8) * 1.8f;
                float radians = angle * Mathf.Deg2Rad;
                parts.Add(Part(
                    $"island-pole-{index}",
                    CityLighthouseIslandPartKind.Timber,
                    new Vector3(
                        anchor.x + Mathf.Cos(radians) * radius,
                        seaTop + 1.3f,
                        anchor.y + Mathf.Sin(radians) * radius *
                            0.85f),
                    Quaternion.Euler(
                        (Sample01(hash >> 12) - 0.5f) * 22f,
                        Sample01(hash >> 16) * 180f,
                        (Sample01(hash >> 20) - 0.5f) * 22f),
                    new Vector3(0.14f, 2.3f, 0.14f),
                    0.85f + Sample01(hash >> 4) * 0.15f));
            }

            // The landing the island was used from: two planks of a
            // fallen jetty walking into the water on the shore side.
            for (int index = 0; index < 2; index++)
            {
                uint hash = Hash(seed, TimberSalt, 16 + index);
                parts.Add(Part(
                    $"island-jetty-plank-{index}",
                    CityLighthouseIslandPartKind.Timber,
                    new Vector3(
                        anchor.x - 1.2f + index * 0.9f +
                            (Sample01(hash) - 0.5f) * 0.5f,
                        seaTop + 0.05f - index * 0.15f,
                        anchor.y - 4.8f - index * 1.9f),
                    Quaternion.Euler(
                        3f + Sample01(hash >> 8) * 4f,
                        (Sample01(hash >> 12) - 0.5f) * 24f,
                        0f),
                    new Vector3(0.5f, 0.09f, 2.7f),
                    0.8f + Sample01(hash >> 16) * 0.12f));
            }
        }

        private static void AddWreck(
            ICollection<CityLighthouseIslandPartDescriptor> parts,
            int seed,
            Vector2 anchor,
            float seaTop)
        {
            // A hull heeled onto the west rocks, half in the water:
            // keel, two strakes, bow and transom, all on one list so
            // the boat ruins as a single object.
            uint hash = Hash(seed, WreckSalt, 0);
            Vector2 origin = new Vector2(
                anchor.x - 6.8f,
                anchor.y + 3.4f);
            float heel = 20f + Sample01(hash) * 8f;
            float yawDegrees = 118f + Sample01(hash >> 8) * 24f;
            Quaternion pose = Quaternion.Euler(0f, yawDegrees, heel);

            Vector3 At(Vector3 local)
            {
                Vector3 offset = pose * local;
                return new Vector3(
                    origin.x + offset.x,
                    seaTop + 0.1f + offset.y,
                    origin.y + offset.z);
            }

            parts.Add(Part(
                "island-wreck-keel",
                CityLighthouseIslandPartKind.Wreck,
                At(Vector3.zero),
                pose,
                new Vector3(1.35f, 0.55f, 4.4f),
                0.9f));
            parts.Add(Part(
                "island-wreck-strake-port",
                CityLighthouseIslandPartKind.Wreck,
                At(new Vector3(-0.68f, 0.5f, 0f)),
                pose * Quaternion.Euler(0f, 0f, -14f),
                new Vector3(0.12f, 0.75f, 4.2f),
                0.86f));
            parts.Add(Part(
                "island-wreck-strake-starboard",
                CityLighthouseIslandPartKind.Wreck,
                At(new Vector3(0.68f, 0.42f, 0f)),
                pose * Quaternion.Euler(0f, 0f, 14f),
                new Vector3(0.12f, 0.75f, 4.2f),
                0.82f));
            parts.Add(Part(
                "island-wreck-bow",
                CityLighthouseIslandPartKind.Wreck,
                At(new Vector3(0f, 0.35f, 2.35f)),
                pose,
                new Vector3(0.9f, 0.7f, 0.8f),
                0.88f));
            parts.Add(Part(
                "island-wreck-transom",
                CityLighthouseIslandPartKind.Wreck,
                At(new Vector3(0f, 0.4f, -2.2f)),
                pose,
                new Vector3(1.15f, 0.65f, 0.12f),
                0.84f));
        }

        // ------------------------------------------------------------------
        // validation
        // ------------------------------------------------------------------

        /// <summary>
        /// The island and its lighthouse stand or fall together — the
        /// moral successor of the mol-and-beacon invariant — and the
        /// whole island keeps to the water the walkable decks never
        /// reach and the sea sheets still cover.
        /// </summary>
        internal static void ValidateOrThrow(
            CityLighthouseIslandPlan plan,
            in CitySeacoastFrame frame)
        {
            if (plan.Count > CityLighthouseIslandPlan.MaximumPartCount)
            {
                throw new InvalidOperationException(
                    "The lighthouse island exceeds its part budget.");
            }

            if (plan.GetCount(
                    CityLighthouseIslandPartKind.LampRoom) != 1)
            {
                throw new InvalidOperationException(
                    "The island and its lighthouse stand or fall " +
                    "together: exactly one lamp room.");
            }

            if (plan.LanternPosition.y - frame.SeaTopY < 12f)
            {
                throw new InvalidOperationException(
                    "The lighthouse lost its height: the lantern " +
                    "must stand at least 12 m over the sea.");
            }

            // Clear of every walkable deck (the mol reaches 16 m past
            // the waterline), and inside the sheeted sea so there is
            // always water under the rocks.
            float minimumZ = frame.WaterlineZ + 16f + 6f;
            float maximumZ = frame.SeaRowBounds.yMax +
                             CitySeacoastSeaLayout.ApronReach - 1f;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityLighthouseIslandPartDescriptor part =
                    plan.Parts[index];
                float reach = Mathf.Max(
                    part.Size.x,
                    part.Size.z) * 0.5f;
                if (part.Center.z - reach < minimumZ ||
                    part.Center.z + reach > maximumZ)
                {
                    throw new InvalidOperationException(
                        $"Island part '{part.StableId}' leaves the " +
                        "offshore band.");
                }

                if (part.Center.x - reach <
                    frame.SeaRowBounds.xMin ||
                    part.Center.x + reach >
                    frame.SeaRowBounds.xMax)
                {
                    throw new InvalidOperationException(
                        $"Island part '{part.StableId}' leaves the " +
                        "sea row.");
                }
            }
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static CityLighthouseIslandPartDescriptor Part(
            string stableId,
            CityLighthouseIslandPartKind kind,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            float shade)
        {
            return new CityLighthouseIslandPartDescriptor(
                stableId,
                kind,
                center,
                rotation,
                size,
                shade);
        }

        private static uint Hash(int seed, uint salt, int index)
        {
            unchecked
            {
                uint value = (uint)seed ^ salt ^
                             ((uint)index * 0x9E3779B9u) ^ IslandSalt;
                value *= 16777619u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }

        private static float Sample01(uint hash)
        {
            return (hash & 0xFFu) / 255f;
        }
    }
}
