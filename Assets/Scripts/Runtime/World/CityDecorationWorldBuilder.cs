using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class CityDecorationWorldBuilder
    {
        private const float SpatialChunkSize = 48f;
        private const int BatchStyleCount = 7;

        private static readonly Color MasonryColor =
            new Color(0.27f, 0.22f, 0.17f);
        private static readonly Color ResidentialColor =
            new Color(0.20f, 0.29f, 0.30f);
        private static readonly Color IndustrialColor =
            new Color(0.20f, 0.22f, 0.21f);
        private static readonly Color StreetColor =
            new Color(0.10f, 0.12f, 0.13f);
        private static readonly Color MagentaGlow =
            new Color(1.25f, 0.18f, 0.72f);
        private static readonly Color CyanGlow =
            new Color(0.16f, 0.88f, 1.30f);
        // Pale fluorescent lightbox white-green: municipal utility
        // signage, deliberately quieter than the nightlife neon.
        private static readonly Color BacklitSignGlow =
            new Color(1.22f, 1.36f, 1.18f);

        /// <summary>The batch brown the playground's own timber is
        /// drawn in, for the swing seats hanging outside the batch.
        /// </summary>
        internal static Color MasonryBatchColor => MasonryColor;

        /// <summary>The batch near-black the park's small metalwork is
        /// drawn in, for the swing ropes.</summary>
        internal static Color StreetBatchColor => StreetColor;

        public static GameObject Build(
            Transform parent,
            CityLayout layout,
            CityDecorationPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = new GameObject("City Details").transform;
            root.SetParent(parent, false);
            BuildGeometry(
                root,
                layout,
                plan.Descriptors,
                null);
            return root.gameObject;
        }

        public static GameObject BuildHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context,
            IReadOnlyList<CityDecorationDescriptor> descriptors)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            Transform root = new GameObject(
                "Home Exterior City Details").transform;
            root.SetParent(parent, false);
            BuildGeometry(
                root,
                context.Layout,
                descriptors,
                context);
            return root.gameObject;
        }

        private static void BuildGeometry(
            Transform root,
            CityLayout layout,
            IReadOnlyList<CityDecorationDescriptor> descriptors,
            HomeExteriorContextPlan homeContext)
        {
            var chunks = new Dictionary<ChunkCoordinate, ChunkGeometry>();
            var parts = new List<DecorationPart>(15);
            var collisionBounds = new List<Bounds>(
                CityStaticCollisionBuilder.MaximumDecorationProxyCount);
            for (int index = 0; index < descriptors.Count; index++)
            {
                parts.Clear();
                Expand(
                    layout,
                    descriptors[index],
                    parts);
                for (int partIndex = 0;
                     partIndex < parts.Count;
                     partIndex++)
                {
                    DecorationPart part = parts[partIndex];
                    Bounds bounds = part.Bounds;
                    if (homeContext != null)
                    {
                        bounds = new Bounds(
                            PlayerHomeBalconyGeometry.ToHomeLocal(
                                homeContext.PlayerHome,
                                bounds.center),
                            PlayerHomeBalconyGeometry.ToHomeLocalSize(
                                homeContext.PlayerHome,
                                bounds.size));
                        if (!HomeExteriorViewBuilder
                                .TryClipToExteriorHalfSpace(
                                    bounds,
                                    out bounds))
                        {
                            continue;
                        }
                    }

                    AddToChunk(
                        chunks,
                        bounds,
                        part.Style,
                        part.Surface);
                }

                if (homeContext == null)
                {
                    collisionBounds.Clear();
                    CityStaticCollisionBuilder.AddDecorationProxyBounds(
                        layout,
                        descriptors[index],
                        collisionBounds);
                    for (int collisionIndex = 0;
                         collisionIndex < collisionBounds.Count;
                         collisionIndex++)
                    {
                        AddCollisionToChunk(
                            chunks,
                            collisionBounds[collisionIndex]);
                    }
                }
            }

            BuildChunks(root, chunks);
        }

        private static void Expand(
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            ICollection<DecorationPart> parts)
        {
            descriptor.TryResolveLot(
                layout,
                out BuildingLot lot);
            var context = new RecipeContext(layout, descriptor, lot);
            switch (descriptor.Kind)
            {
                case CityDecorationKind.OldTownChimneysAndDormers:
                    BuildChimneys(context, parts);
                    break;
                case CityDecorationKind.OldTownScaffolding:
                    BuildScaffolding(context, parts);
                    break;
                case CityDecorationKind.OldTownStreetMarket:
                    BuildMarket(context, parts);
                    break;
                case CityDecorationKind.OldTownClockTower:
                    BuildClockTower(context, parts);
                    break;
                case CityDecorationKind.ResidentialBalconies:
                    BuildBalconies(context, parts);
                    break;
                case CityDecorationKind.ResidentialLaundryAndAntenna:
                    BuildLaundry(context, parts);
                    break;
                case CityDecorationKind.ResidentialDiscardedFurniture:
                    BuildFurniture(context, parts);
                    break;
                case CityDecorationKind.ResidentialRooftopGreenhouse:
                    BuildGreenhouse(context, parts);
                    break;
                case CityDecorationKind.IndustrialStacksAndTanks:
                    BuildStacks(context, parts);
                    break;
                case CityDecorationKind.IndustrialPipeRack:
                    BuildPipeRack(context, parts);
                    break;
                case CityDecorationKind.IndustrialCargo:
                    BuildCargo(context, parts);
                    break;
                case CityDecorationKind.IndustrialGantry:
                    BuildGantry(context, parts);
                    break;
                case CityDecorationKind.NightlifeBillboard:
                    BuildBillboard(context, parts);
                    break;
                case CityDecorationKind.NightlifeFireEscape:
                    BuildFireEscape(context, parts);
                    break;
                case CityDecorationKind.NightlifeVendingAndQueue:
                    BuildVendingQueue(context, parts);
                    break;
                case CityDecorationKind.NightlifeCinema:
                    BuildCinema(context, parts);
                    break;
                case CityDecorationKind.RoadsideDumpsterAndUtility:
                    BuildDumpster(context, parts);
                    break;
                case CityDecorationKind.RoadsidePhoneBooth:
                    BuildPhoneBooth(context, parts);
                    break;
                case CityDecorationKind.RoadsideBusShelter:
                    BuildBusShelter(context, parts);
                    break;
                case CityDecorationKind.RoadsideRoadworkAndBicycle:
                    BuildRoadwork(context, parts);
                    break;
                case CityDecorationKind.ParkFountainAndStatue:
                    BuildFountain(context, parts);
                    break;
                case CityDecorationKind.ParkBandstand:
                    BuildBandstand(context, parts);
                    break;
                case CityDecorationKind.ParkChessTables:
                    BuildChessTables(context, parts);
                    break;
                case CityDecorationKind.ParkPlayground:
                    BuildPlayground(context, parts);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        descriptor.Kind,
                        "Unsupported city decoration kind.");
            }
        }

        private static void BuildChimneys(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float spread = Mathf.Clamp(c.Width * 0.22f, 1.1f, 2.4f);
            for (int side = -1; side <= 1; side += 2)
            {
                Add(parts, c, c.Primary, side * spread, 0.85f,
                    side * 0.22f, 0.68f, 1.70f, 0.68f);
                Add(parts, c, BatchStyle.Masonry, side * spread, 1.74f,
                    side * 0.22f, 0.88f, 0.18f, 0.88f);
            }

            float dormerX = c.Mirror * Mathf.Min(c.Width * 0.18f, 1.8f);
            Add(parts, c, c.Primary, dormerX, 0.62f, 0f,
                1.65f, 1.24f, 1.05f);
            Add(parts, c, BatchStyle.Masonry, dormerX, 1.31f, 0f,
                1.90f, 0.18f, 1.30f);
            Add(parts, c, BatchStyle.Street, dormerX, 0.67f, 0.56f,
                0.62f, 0.58f, 0.08f);
        }

        private static void BuildScaffolding(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.68f, 4.2f, 7.2f);
            float height = Mathf.Clamp(c.Height - 0.6f, 4.8f, 7.2f);
            for (int side = -1; side <= 1; side += 2)
            {
                Add(parts, c, BatchStyle.Industrial, side * width * 0.5f,
                    height * 0.5f, 0.18f, 0.12f, height, 0.12f);
                Add(parts, c, BatchStyle.Industrial, side * width * 0.5f,
                    height * 0.5f, 0.82f, 0.12f, height, 0.12f);
            }

            for (int level = 1; level <= 3; level++)
            {
                float y = height * level * 0.25f;
                Add(parts, c, BatchStyle.Masonry, 0f, y, 0.50f,
                    width + 0.35f, 0.14f, 0.92f);
                Add(parts, c, BatchStyle.Industrial, 0f, y + 0.54f, 0.84f,
                    width, 0.10f, 0.10f);
            }

            Add(parts, c, BatchStyle.Industrial, 0f, height - 0.25f, 0.20f,
                width, 0.12f, 0.12f);
        }

        private static void BuildMarket(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.44f, 3.4f, 5.2f);
            for (int side = -1; side <= 1; side += 2)
            {
                Add(parts, c, BatchStyle.Street, side * width * 0.44f,
                    1.20f, -0.38f, 0.14f, 2.40f, 0.14f);
                Add(parts, c, BatchStyle.Street, side * width * 0.44f,
                    1.20f, 0.38f, 0.14f, 2.40f, 0.14f);
            }

            Add(parts, c, c.Primary, 0f, 2.48f, 0f,
                width, 0.18f, 1.25f);
            Add(parts, c, BatchStyle.Residential, 0f, 2.26f, 0.64f,
                width, 0.34f, 0.10f);
            Add(parts, c, BatchStyle.Masonry, 0f, 0.88f, 0.10f,
                width * 0.70f, 0.18f, 0.64f);
            Add(parts, c, BatchStyle.Masonry, -width * 0.30f, 0.32f, 0.70f,
                0.72f, 0.64f, 0.72f);
            Add(parts, c, BatchStyle.Masonry, width * 0.27f, 0.24f, 0.74f,
                0.58f, 0.48f, 0.58f);
            Add(parts, c, BatchStyle.Street, 0f, 1.45f, -0.48f,
                width * 0.62f, 0.12f, 0.28f);
        }

        private static void BuildClockTower(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.28f, 2.8f, 4.0f);
            Add(parts, c, c.Primary, 0f, 1.80f, 0f,
                width, 3.60f, width);
            Add(parts, c, BatchStyle.Masonry, 0f, 4.35f, 0f,
                width * 0.82f, 1.50f, width * 0.82f);
            Add(parts, c, BatchStyle.Masonry, 0f, 5.16f, 0f,
                width * 1.02f, 0.22f, width * 1.02f);
            Add(parts, c, BatchStyle.Street, 0f, 5.55f, 0f,
                width * 0.76f, 0.58f, width * 0.76f);
            Add(parts, c, BatchStyle.Street, 0f, 6.42f, 0f,
                0.30f, 1.20f, 0.30f);
            AddClockFace(parts, c, width, 1f, 0f);
            AddClockFace(parts, c, width, -1f, 0f);
            AddClockFace(parts, c, width, 0f, 1f);
            AddClockFace(parts, c, width, 0f, -1f);
            Add(parts, c, BatchStyle.Residential, 0f, 4.36f,
                width * 0.425f, 0.68f, 0.10f, 0.08f);
            Add(parts, c, BatchStyle.Residential, 0f, 4.36f,
                width * 0.43f, 0.10f, 0.62f, 0.08f);
        }

        private static void AddClockFace(
            ICollection<DecorationPart> parts,
            RecipeContext c,
            float width,
            float xSide,
            float zSide)
        {
            float offset = width * 0.42f;
            Vector3 size = xSide != 0f
                ? new Vector3(0.08f, 1.02f, 1.02f)
                : new Vector3(1.02f, 1.02f, 0.08f);
            Add(parts, c, BatchStyle.Residential,
                xSide * offset, 4.36f, zSide * offset,
                size.x, size.y, size.z);
        }

        private static void BuildBalconies(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.50f, 2.8f, 4.8f);
            int count = Mathf.Clamp(
                Mathf.FloorToInt((c.Height - 0.8f) / 2.25f),
                2,
                3);
            for (int floor = 0; floor < count; floor++)
            {
                float y = 1.75f + floor * 2.15f;
                Add(parts, c, c.Primary, 0f, y, 0.54f,
                    width, 0.16f, 1.08f);
                Add(parts, c, BatchStyle.Street, 0f, y + 0.58f, 1.02f,
                    width, 0.12f, 0.10f);
                Add(parts, c, BatchStyle.Street, -width * 0.48f,
                    y + 0.58f, 0.55f, 0.10f, 1.05f, 0.92f);
                Add(parts, c, BatchStyle.Street, width * 0.48f,
                    y + 0.58f, 0.55f, 0.10f, 1.05f, 0.92f);
            }
        }

        private static void BuildLaundry(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            Add(parts, c, BatchStyle.Street, 0f, 1.45f, -0.65f,
                0.12f, 2.90f, 0.12f);
            Add(parts, c, BatchStyle.Street, 0f, 2.25f, -0.65f,
                2.70f, 0.10f, 0.10f);
            Add(parts, c, BatchStyle.Street, 0f, 2.62f, -0.65f,
                1.65f, 0.08f, 0.08f);
            Add(parts, c, BatchStyle.Street, -1.22f, 2.24f, -0.65f,
                0.10f, 0.82f, 0.10f);
            Add(parts, c, BatchStyle.Street, 1.22f, 2.24f, -0.65f,
                0.10f, 0.82f, 0.10f);
            Add(parts, c, BatchStyle.Street, -2.00f, 1.20f, 0.42f,
                0.12f, 2.40f, 0.12f);
            Add(parts, c, BatchStyle.Street, 2.00f, 1.20f, 0.42f,
                0.12f, 2.40f, 0.12f);
            Add(parts, c, BatchStyle.Street, 0f, 2.17f, 0.42f,
                4.05f, 0.06f, 0.06f);
            Add(parts, c, c.Primary, -1.18f, 1.68f, 0.43f,
                0.86f, 0.92f, 0.05f);
            Add(parts, c, BatchStyle.Masonry, 0f, 1.72f, 0.43f,
                0.72f, 0.84f, 0.05f);
            Add(parts, c, BatchStyle.Residential, 1.12f, 1.64f, 0.43f,
                0.92f, 1.00f, 0.05f);
        }

        // The sittable seats hidden in the decoration recipes, shared
        // with <see cref="AppendBenchSeats"/> so the seat the hero
        // docks against is always the seat that was drawn.
        private const float CouchX = 0.55f;
        private const float CouchSeatCenterY = 0.48f;
        private const float CouchSeatThickness = 0.42f;
        private const float CouchWidth = 2.20f;
        private const float CouchDepth = 0.92f;
        // The five numbers a man standing on this board also has to know
        // are owned by CityChessBoardGeometry and aliased here, so the
        // drawn lattice and the set on it cannot drift apart.
        private const float ChessTableOffset =
            CityChessBoardGeometry.TableOffsetMeters;
        // How far a chess foot is sunk past the grass it stands on, so
        // the join reads as a footing bedded into the lawn rather than
        // a box resting on a surface it only touches at one point.
        private const float ChessGroundEmbed = 0.12f;
        private const float ChessFootingSize = 0.66f;
        private const float ChessFootingTopY = 0.12f;
        private const float ChessPedestalSize = 0.34f;
        private const float ChessPedestalTopY = 0.80f;
        private const float ChessSlabSize = 1.44f;
        private const float ChessSlabThickness = 0.14f;
        private const float ChessTableTopY =
            CityChessBoardGeometry.TableTopY;
        private const int ChessSquaresPerSide =
            CityChessBoardGeometry.SquaresPerSide;
        private const float ChessSquareSize =
            CityChessBoardGeometry.SquareSizeMeters;
        private const float ChessFieldSize =
            CityChessBoardGeometry.FieldSizeMeters;
        private const float ChessLightTopY =
            CityChessBoardGeometry.LightTopY;
        private const float ChessSquareSink = 0.012f;
        private const float ChessSquareProud =
            CityChessBoardGeometry.DarkSquareProudMeters;
        private const float ChessRimWidth = 0.07f;
        private const float ChessRimTopY = ChessLightTopY + 0.018f;
        private const float ChessBenchSeatCenterY = 0.46f;
        private const float ChessBenchSeatThickness = 0.16f;
        private const float ChessBenchZ = 1.10f;
        private const float ChessBenchWidth = 1.12f;
        private const float ChessBenchDepth = 0.42f;
        private const float ChessBenchLegInset = 0.18f;
        private const float ChessBenchLegWidth = 0.14f;
        private const float ChessBenchLegDepth = 0.34f;
        private const float ChessBenchPadWidth = 0.30f;
        private const float ChessBenchPadDepth = 0.44f;
        private const float ChessBenchPadTopY = 0.04f;
        private const float PlaygroundBenchZ =
            CityPlaygroundGeometry.BenchZ;
        private const float PlaygroundBenchSeatCenterY = 0.62f;
        private const float PlaygroundBenchSeatThickness = 0.18f;
        private const float PlaygroundBenchWidth = 3.75f;
        private const float PlaygroundBenchDepth = 0.42f;

        /// <summary>
        /// Describes the sittable seats one street decoration carries,
        /// in world space, mirroring the recipe transform used by the
        /// geometry expansion. The chess benches face their table, the
        /// discarded couch faces the street, the playground bench
        /// faces the swings; every other kind reports none.
        /// </summary>
        public static void AppendBenchSeats(
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            ICollection<CityBenchSeat> target)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            descriptor.TryResolveLot(
                layout,
                out BuildingLot lot);
            var context = new RecipeContext(layout, descriptor, lot);
            switch (descriptor.Kind)
            {
                case CityDecorationKind.ResidentialDiscardedFurniture:
                    target.Add(DescribeSeat(
                        context,
                        $"{descriptor.StableId}-couch",
                        -CouchX * context.Mirror,
                        CouchSeatCenterY + CouchSeatThickness * 0.5f,
                        0f,
                        CouchWidth,
                        CouchDepth,
                        1f));
                    return;
                case CityDecorationKind.ParkChessTables:
                    for (int table = -1; table <= 1; table += 2)
                    {
                        for (int side = -1; side <= 1; side += 2)
                        {
                            target.Add(DescribeSeat(
                                context,
                                $"{descriptor.StableId}-seat" +
                                $"-{(table < 0 ? 'a' : 'b')}" +
                                $"{(side < 0 ? '1' : '2')}",
                                table * ChessTableOffset,
                                ChessBenchSeatCenterY +
                                ChessBenchSeatThickness * 0.5f,
                                side * ChessBenchZ,
                                ChessBenchWidth,
                                ChessBenchDepth,
                                -side));
                        }
                    }

                    return;
                case CityDecorationKind.ParkPlayground:
                    target.Add(DescribeSeat(
                        context,
                        $"{descriptor.StableId}-bench",
                        0f,
                        PlaygroundBenchSeatCenterY +
                        PlaygroundBenchSeatThickness * 0.5f,
                        PlaygroundBenchZ,
                        PlaygroundBenchWidth,
                        PlaygroundBenchDepth,
                        -1f));
                    return;
                default:
                    return;
            }
        }

        /// <summary>
        /// The transform one decoration recipe is drawn in: where its
        /// origin is and which way it faces after the recipe's own axis
        /// snap. Anything that has to stand in the same frame as the
        /// baked parts — the playground's hanging swings, above all —
        /// reads the basis from here rather than repeating the snap.
        /// </summary>
        internal static bool TryDescribeRecipeBasis(
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            out Vector3 origin,
            out Vector3 forward)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            descriptor.TryResolveLot(layout, out BuildingLot lot);
            var context = new RecipeContext(layout, descriptor, lot);
            origin = context.Origin;
            forward = context.Forward;
            return forward.sqrMagnitude > 0.0001f;
        }

        /// <summary>
        /// The boxes one decoration expands to, in world space. The
        /// builder batches them away immediately, so this is the only
        /// way to state what a recipe actually draws.
        /// </summary>
        internal static void AppendPartBounds(
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            ICollection<Bounds> target)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var parts = new List<DecorationPart>(15);
            Expand(layout, descriptor, parts);
            for (int index = 0; index < parts.Count; index++)
            {
                target.Add(parts[index].Bounds);
            }
        }

        private static CityBenchSeat DescribeSeat(
            RecipeContext context,
            string id,
            float x,
            float seatTopHeight,
            float z,
            float width,
            float depth,
            float faceSign)
        {
            float depthScale = context.StreetDepthScale;
            Vector3 seatTopCenter = context.Origin +
                (context.Tangent * x) +
                (Vector3.up * seatTopHeight) +
                (context.Forward * (z * depthScale));
            return new CityBenchSeat(
                id,
                seatTopCenter,
                width,
                Mathf.Max(0.05f, depth * depthScale),
                context.Origin.y,
                context.Forward * faceSign);
        }

        // The reachable faces of the street utilities, shared with
        // <see cref="AppendUtilityDocks"/> so the dock a future
        // interaction uses always fronts the drawn door and lid.
        private const float PhoneBoothDoorLocalZ = 0.59f;
        private const float PhoneBoothDoorHalfDepth = 0.04f;
        private const float DumpsterBodyLocalX = -0.65f;
        private const float DumpsterBodyHalfDepth = 0.675f;
        public const float UtilityDockClearance = 0.62f;

        /// <summary>
        /// Describes the interaction dock one street decoration
        /// carries, in world space, mirroring the recipe transform
        /// used by the geometry expansion. The phone booth docks a
        /// body in front of its door, the dumpster in front of its
        /// bin lid; every other kind reports none.
        /// </summary>
        public static void AppendUtilityDocks(
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            ICollection<CityStreetUtilityDock> target)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            descriptor.TryResolveLot(
                layout,
                out BuildingLot lot);
            var context = new RecipeContext(layout, descriptor, lot);
            float depthScale = context.StreetDepthScale;
            switch (descriptor.Kind)
            {
                case CityDecorationKind.RoadsidePhoneBooth:
                    float doorReach =
                        (PhoneBoothDoorLocalZ +
                         PhoneBoothDoorHalfDepth) * depthScale +
                        UtilityDockClearance;
                    target.Add(new CityStreetUtilityDock(
                        $"{descriptor.StableId}-door",
                        descriptor.StableId,
                        CityStreetUtilityKind.PhoneBooth,
                        context.Origin + context.Forward * doorReach,
                        -context.Forward));
                    return;
                case CityDecorationKind.RoadsideDumpsterAndUtility:
                    float lidReach =
                        DumpsterBodyHalfDepth * depthScale +
                        UtilityDockClearance;
                    target.Add(new CityStreetUtilityDock(
                        $"{descriptor.StableId}-lid",
                        descriptor.StableId,
                        CityStreetUtilityKind.Dumpster,
                        context.Origin +
                        context.Tangent * DumpsterBodyLocalX +
                        context.Forward * lidReach,
                        -context.Forward));
                    return;
                default:
                    return;
            }
        }

        private static void BuildFurniture(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float mirror = c.Mirror;
            Add(parts, c, c.Primary, -CouchX * mirror, CouchSeatCenterY, 0f,
                CouchWidth, CouchSeatThickness, CouchDepth);
            Add(parts, c, c.Primary, -0.55f * mirror, 1.02f, -0.37f,
                2.20f, 0.78f, 0.18f);
            Add(parts, c, c.Primary, -1.58f * mirror, 0.78f, 0f,
                0.20f, 0.82f, 0.92f);
            Add(parts, c, c.Primary, 0.48f * mirror, 0.78f, 0f,
                0.20f, 0.82f, 0.92f);
            Add(parts, c, BatchStyle.Street, -1.22f * mirror, 0.14f, 0f,
                0.16f, 0.28f, 0.16f);
            Add(parts, c, BatchStyle.Street, 0.12f * mirror, 0.14f, 0f,
                0.16f, 0.28f, 0.16f);
            Add(parts, c, BatchStyle.Masonry, 1.25f * mirror, 0.18f, 0.35f,
                1.30f, 0.22f, 2.10f);
            Add(parts, c, BatchStyle.Street, 1.25f * mirror, 0.31f, 0.35f,
                1.12f, 0.05f, 1.92f);
        }

        private static void BuildGreenhouse(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.46f, 3.4f, 5.4f);
            float depth = Mathf.Clamp(c.Depth * 0.34f, 2.6f, 4.0f);
            Add(parts, c, BatchStyle.Masonry, 0f, 0.12f, 0f,
                width, 0.24f, depth);
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                for (int zSide = -1; zSide <= 1; zSide += 2)
                {
                    Add(parts, c, BatchStyle.Residential,
                        xSide * width * 0.47f, 1.15f,
                        zSide * depth * 0.47f, 0.12f, 2.06f, 0.12f);
                }
            }

            Add(parts, c, BatchStyle.Residential, 0f, 2.16f,
                -depth * 0.47f, width, 0.12f, 0.12f);
            Add(parts, c, BatchStyle.Residential, 0f, 2.16f,
                depth * 0.47f, width, 0.12f, 0.12f);
            Add(parts, c, BatchStyle.Residential, -width * 0.47f, 2.16f,
                0f, 0.12f, 0.12f, depth);
            Add(parts, c, BatchStyle.Residential, width * 0.47f, 2.16f,
                0f, 0.12f, 0.12f, depth);
            Add(parts, c, c.Primary, -width * 0.24f, 2.42f, 0f,
                width * 0.48f, 0.18f, depth + 0.18f);
            Add(parts, c, c.Primary, width * 0.24f, 2.42f, 0f,
                width * 0.48f, 0.18f, depth + 0.18f);
            Add(parts, c, BatchStyle.Street, 0f, 2.65f, 0f,
                0.12f, 0.12f, depth + 0.30f);
            Add(parts, c, BatchStyle.Street, width * 0.32f, 3.20f, 0f,
                0.10f, 1.45f, 0.10f);
            Add(parts, c, BatchStyle.Street, width * 0.32f, 3.64f, 0f,
                1.10f, 0.08f, 0.08f);
        }

        private static void BuildStacks(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Add(parts, c, c.Primary, side * 2.15f, 2.65f, -0.72f,
                    0.82f, 5.30f, 0.82f);
                Add(parts, c, BatchStyle.Industrial, side * 2.15f, 5.34f,
                    -0.72f, 1.08f, 0.18f, 1.08f);
                Add(parts, c, BatchStyle.Industrial, side * 1.05f, 0.18f,
                    1.05f, 1.55f, 0.36f, 1.55f);
                Add(parts, c, c.Primary, side * 1.05f, 1.43f, 1.05f,
                    1.34f, 2.18f, 1.34f);
                Add(parts, c, BatchStyle.Street, side * 1.05f, 2.58f,
                    1.05f, 1.48f, 0.18f, 1.48f);
            }

            Add(parts, c, BatchStyle.Industrial, 0f, 0.72f, 0.96f,
                2.25f, 0.24f, 0.24f);
            Add(parts, c, BatchStyle.Industrial, 0f, 1.34f, -0.68f,
                3.55f, 0.22f, 0.22f);
        }

        private static void BuildPipeRack(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.52f, 4.5f, 7.0f);
            for (int side = -1; side <= 1; side += 2)
            {
                Add(parts, c, BatchStyle.Street, side * width * 0.48f,
                    1.55f, -0.75f, 0.18f, 3.10f, 0.18f);
                Add(parts, c, BatchStyle.Street, side * width * 0.48f,
                    1.55f, 0.75f, 0.18f, 3.10f, 0.18f);
                Add(parts, c, BatchStyle.Industrial, 0f, 2.92f,
                    side * 0.75f, width, 0.18f, 0.18f);
            }

            for (int pipe = -1; pipe <= 1; pipe++)
            {
                Add(parts, c, c.Primary, 0f, 1.85f + pipe * 0.46f,
                    pipe * 0.40f, width + 0.45f, 0.20f, 0.20f);
            }

            Add(parts, c, BatchStyle.Street, -width * 0.48f, 2.05f, 0f,
                0.22f, 0.16f, 1.70f);
            Add(parts, c, BatchStyle.Street, width * 0.48f, 2.05f, 0f,
                0.22f, 0.16f, 1.70f);
        }

        private static void BuildCargo(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float mirror = c.Mirror;
            Add(parts, c, c.Primary, -1.65f * mirror, 1.15f, 0f,
                3.10f, 2.30f, 1.75f);
            Add(parts, c, BatchStyle.Industrial, 1.35f * mirror, 0.82f, 0.35f,
                2.40f, 1.64f, 1.55f);
            for (int side = -1; side <= 1; side += 2)
            {
                Add(parts, c, BatchStyle.Street,
                    (-1.65f + side * 1.18f) * mirror, 1.15f, 0.90f,
                    0.12f, 2.05f, 0.10f);
                Add(parts, c, BatchStyle.Street,
                    (1.35f + side * 0.88f) * mirror, 0.82f, 1.15f,
                    0.10f, 1.42f, 0.10f);
            }

            Add(parts, c, BatchStyle.Masonry, -1.65f * mirror, 0.12f, 0f,
                3.30f, 0.24f, 1.95f);
            Add(parts, c, BatchStyle.Masonry, 1.35f * mirror, 0.12f, 0.35f,
                2.60f, 0.24f, 1.72f);
            Add(parts, c, BatchStyle.Masonry, -0.10f * mirror, 0.38f, 1.65f,
                0.72f, 0.76f, 0.72f);
            Add(parts, c, BatchStyle.Masonry, 0.72f * mirror, 0.28f, 1.70f,
                0.54f, 0.56f, 0.54f);
            Add(parts, c, BatchStyle.Masonry, -0.72f * mirror, 0.22f, 1.74f,
                0.44f, 0.44f, 0.44f);
        }

        private static void BuildGantry(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.60f, 6.0f, 9.0f);
            float depth = Mathf.Clamp(c.Depth * 0.42f, 3.2f, 5.0f);
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                for (int zSide = -1; zSide <= 1; zSide += 2)
                {
                    Add(parts, c, BatchStyle.Industrial,
                        xSide * width * 0.48f, 2.70f,
                        zSide * depth * 0.45f, 0.30f, 5.40f, 0.30f);
                }

                Add(parts, c, BatchStyle.Industrial,
                    xSide * width * 0.48f, 5.25f, 0f,
                    0.34f, 0.34f, depth);
            }

            Add(parts, c, c.Primary, 0f, 5.48f, 0f,
                width + 0.55f, 0.46f, 0.46f);
            Add(parts, c, BatchStyle.Street, 0f, 5.82f, -0.72f,
                width, 0.16f, 0.16f);
            Add(parts, c, BatchStyle.Street, 0f, 5.82f, 0.72f,
                width, 0.16f, 0.16f);
            Add(parts, c, BatchStyle.Industrial, c.Mirror * width * 0.18f,
                5.12f, 0f, 0.78f, 0.62f, 0.92f);
            Add(parts, c, BatchStyle.Street, c.Mirror * width * 0.18f,
                3.80f, 0f, 0.10f, 2.05f, 0.10f);
            Add(parts, c, BatchStyle.Industrial, c.Mirror * width * 0.18f,
                2.72f, 0f, 0.42f, 0.22f, 0.42f);
        }

        private static void BuildBillboard(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.55f, 4.2f, 7.0f);
            Add(parts, c, BatchStyle.Street, -width * 0.32f, 1.55f, 0f,
                0.22f, 3.10f, 0.22f);
            Add(parts, c, BatchStyle.Street, width * 0.32f, 1.55f, 0f,
                0.22f, 3.10f, 0.22f);
            Add(parts, c, BatchStyle.Street, 0f, 3.48f, 0f,
                width + 0.32f, 2.45f, 0.24f);
            Add(parts, c, BatchStyle.Street, 0f, 4.73f, 0.02f,
                width + 0.58f, 0.16f, 0.32f);
            Add(parts, c, BatchStyle.Street, 0f, 2.23f, 0.02f,
                width + 0.58f, 0.16f, 0.32f);
            Add(parts, c, BatchStyle.Street, -width * 0.51f, 3.48f, 0.02f,
                0.16f, 2.66f, 0.32f);
            Add(parts, c, BatchStyle.Street, width * 0.51f, 3.48f, 0.02f,
                0.16f, 2.66f, 0.32f);
            // Two backlit posters, not bare glowing quads: dark glyph
            // strokes across each pane read as a headline, body text
            // and a photo block — the phone-booth lightbox idiom.
            for (int side = -1; side <= 1; side += 2)
            {
                float paneX = side * width * 0.24f;
                Add(parts, c, c.Neon, paneX, 3.48f, 0.15f,
                    width * 0.46f, 1.82f, 0.06f);
                Add(parts, c, BatchStyle.Street,
                    paneX - side * width * 0.075f, 4.02f, 0.185f,
                    width * 0.21f, 0.30f, 0.02f);
                Add(parts, c, BatchStyle.Street, paneX, 3.56f, 0.185f,
                    width * 0.36f, 0.10f, 0.02f);
                Add(parts, c, BatchStyle.Street,
                    paneX + side * width * 0.02f, 3.34f, 0.185f,
                    width * 0.30f, 0.09f, 0.02f);
                Add(parts, c, BatchStyle.Street,
                    paneX + side * width * 0.12f, 2.95f, 0.185f,
                    width * 0.17f, 0.46f, 0.02f);
                Add(parts, c, BatchStyle.Street,
                    paneX - side * width * 0.14f, 2.86f, 0.185f,
                    width * 0.15f, 0.09f, 0.02f);
            }
        }

        private static void BuildFireEscape(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float height = Mathf.Clamp(c.Height - 0.8f, 5.2f, 7.2f);
            float width = Mathf.Clamp(c.Width * 0.34f, 3.0f, 4.4f);
            for (int floor = 1; floor <= 3; floor++)
            {
                float y = height * floor * 0.25f;
                Add(parts, c, BatchStyle.Industrial, 0f, y, 0.62f,
                    width, 0.16f, 1.18f);
                Add(parts, c, BatchStyle.Street, 0f, y + 0.58f, 1.14f,
                    width, 0.10f, 0.10f);
            }

            Add(parts, c, BatchStyle.Street, -width * 0.48f, height * 0.5f,
                1.12f, 0.10f, height, 0.10f);
            Add(parts, c, BatchStyle.Street, width * 0.48f, height * 0.5f,
                1.12f, 0.10f, height, 0.10f);
            Add(parts, c, BatchStyle.Industrial, width * 0.22f, height * 0.38f,
                1.20f, 0.12f, height * 0.62f, 0.12f);
            Add(parts, c, BatchStyle.Industrial, width * 0.43f, height * 0.38f,
                1.20f, 0.12f, height * 0.62f, 0.12f);
            for (int rung = 0; rung < 4; rung++)
            {
                Add(parts, c, BatchStyle.Industrial, width * 0.325f,
                    0.72f + rung * 0.72f, 1.20f,
                    width * 0.25f, 0.08f, 0.08f);
            }
        }

        private static void BuildVendingQueue(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Add(parts, c, BatchStyle.Street, side * 0.72f, 1.02f, -0.35f,
                    1.15f, 2.04f, 0.72f);
                // The lit machine front is a product window, so dark
                // grid mullions carve it into a vitrine instead of one
                // bare glowing rectangle.
                Add(parts, c, c.Neon, side * 0.72f, 1.32f, 0.03f,
                    0.80f, 0.72f, 0.05f);
                Add(parts, c, BatchStyle.Street, side * 0.72f, 1.32f,
                    0.062f, 0.82f, 0.045f, 0.02f);
                Add(parts, c, BatchStyle.Street, side * 0.72f - 0.135f,
                    1.32f, 0.062f, 0.045f, 0.74f, 0.02f);
                Add(parts, c, BatchStyle.Street, side * 0.72f + 0.135f,
                    1.32f, 0.062f, 0.045f, 0.74f, 0.02f);
                Add(parts, c, BatchStyle.Industrial, side * 0.72f, 0.62f,
                    0.04f, 0.74f, 0.16f, 0.05f);
            }

            for (int post = 0; post < 4; post++)
            {
                float x = -2.35f + post * 1.55f;
                Add(parts, c, BatchStyle.Street, x, 0.50f, 1.20f,
                    0.16f, 1.00f, 0.16f);
            }

            // Queue rails are painted steel, not neon tubes: glowing
            // handrails read as untextured noise at street level.
            Add(parts, c, BatchStyle.Street, -1.58f, 0.78f, 1.20f,
                1.40f, 0.07f, 0.07f);
            Add(parts, c, BatchStyle.Street, 1.52f, 0.78f, 1.20f,
                1.40f, 0.07f, 0.07f);
        }

        private static void BuildCinema(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = Mathf.Clamp(c.Width * 0.72f, 6.0f, 9.5f);
            Add(parts, c, BatchStyle.Street, 0f, 3.20f, 0.65f,
                width, 0.65f, 1.45f);
            Add(parts, c, c.Neon, 0f, 3.14f, 1.39f,
                width * 0.88f, 0.18f, 0.06f);
            Add(parts, c, BatchStyle.Street, c.Mirror * width * 0.36f,
                6.25f, 0.10f, 1.10f, 5.60f, 0.32f);
            for (int line = 0; line < 3; line++)
            {
                Add(parts, c, c.Neon, c.Mirror * width * 0.36f,
                    4.55f + line * 1.45f, 0.28f,
                    0.72f, 0.18f, 0.05f);
            }

            Add(parts, c, BatchStyle.Masonry, -width * 0.36f, 1.42f, 0.08f,
                0.48f, 2.84f, 0.48f);
            Add(parts, c, BatchStyle.Masonry, width * 0.36f, 1.42f, 0.08f,
                0.48f, 2.84f, 0.48f);
            Add(parts, c, BatchStyle.Street, -width * 0.18f, 1.35f, 0.22f,
                1.65f, 2.20f, 0.18f);
            Add(parts, c, BatchStyle.Street, width * 0.18f, 1.35f, 0.22f,
                1.65f, 2.20f, 0.18f);
            // Backlit movie posters carry a dark figure block and
            // title strokes over the glow, so the lightboxes read as
            // printed one-sheets instead of bare glowing panes.
            for (int side = -1; side <= 1; side += 2)
            {
                float posterX = side * width * 0.18f;
                Add(parts, c, c.Neon, posterX, 1.35f, 0.32f,
                    1.28f, 1.75f, 0.05f);
                Add(parts, c, BatchStyle.Street,
                    posterX - side * 0.19f, 1.58f, 0.35f,
                    0.46f, 0.88f, 0.02f);
                Add(parts, c, BatchStyle.Street,
                    posterX + side * 0.28f, 1.30f, 0.35f,
                    0.34f, 0.40f, 0.02f);
                Add(parts, c, BatchStyle.Street, posterX, 0.86f, 0.35f,
                    0.96f, 0.10f, 0.02f);
                Add(parts, c, BatchStyle.Street,
                    posterX + side * 0.06f, 0.68f, 0.35f,
                    0.74f, 0.08f, 0.02f);
            }
            Add(parts, c, BatchStyle.Masonry, 0f, 8.72f, 0f,
                width * 0.92f, 0.28f, 0.52f);
        }

        private static void BuildDumpster(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            Add(parts, c, BatchStyle.Industrial, DumpsterBodyLocalX,
                0.62f, 0f, 2.45f, 1.24f, DumpsterBodyHalfDepth * 2f);
            Add(parts, c, BatchStyle.Street, -1.25f, 1.34f, -0.08f,
                1.16f, 0.16f, 1.42f);
            Add(parts, c, BatchStyle.Street, -0.05f, 1.34f, -0.08f,
                1.16f, 0.16f, 1.42f);
            Add(parts, c, BatchStyle.Street, -1.55f, 0.64f, 0.71f,
                0.12f, 0.92f, 0.10f);
            Add(parts, c, BatchStyle.Street, 0.25f, 0.64f, 0.71f,
                0.12f, 0.92f, 0.10f);
            for (int wheel = 0; wheel < 4; wheel++)
            {
                float x = wheel < 2 ? -1.42f : 0.12f;
                float z = (wheel & 1) == 0 ? -0.55f : 0.55f;
                Add(parts, c, BatchStyle.Street, x, 0.14f, z,
                    0.24f, 0.28f, 0.24f);
            }

            Add(parts, c, BatchStyle.Street, 1.35f, 0.92f, -0.22f,
                1.15f, 1.84f, 0.72f);
            Add(parts, c, BatchStyle.Industrial, 1.35f, 0.98f, 0.16f,
                0.88f, 1.42f, 0.05f);
            Add(parts, c, BatchStyle.Street, 1.35f, 1.28f, 0.20f,
                0.62f, 0.08f, 0.05f);
            Add(parts, c, BatchStyle.Street, 1.35f, 1.05f, 0.20f,
                0.62f, 0.08f, 0.05f);
        }

        private static void BuildPhoneBooth(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            Add(parts, c, BatchStyle.Street, 0f, 0.10f, 0f,
                1.38f, 0.20f, 1.28f);
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                for (int zSide = -1; zSide <= 1; zSide += 2)
                {
                    Add(parts, c, c.Primary, xSide * 0.62f, 1.35f,
                        zSide * 0.56f, 0.14f, 2.50f, 0.14f);
                }
            }

            Add(parts, c, c.Primary, 0f, 2.65f, 0f,
                1.48f, 0.22f, 1.38f);
            Add(parts, c, BatchStyle.Residential, 0f, 1.55f, -0.57f,
                1.08f, 1.65f, 0.06f);
            Add(parts, c, BatchStyle.Residential, -0.63f, 1.55f, 0f,
                0.06f, 1.65f, 0.92f);
            Add(parts, c, BatchStyle.Residential, 0.63f, 1.55f, 0f,
                0.06f, 1.65f, 0.92f);
            Add(parts, c, c.Primary, 0f, 1.42f, PhoneBoothDoorLocalZ,
                1.05f, 1.90f, PhoneBoothDoorHalfDepth * 2f);
            Add(parts, c, BatchStyle.Street, -0.30f, 1.46f, 0.65f,
                0.28f, 0.62f, 0.10f);

            // The municipal lightbox over the door: a dark housing on
            // the roof fascia, one pale fluorescent panel recessed
            // behind its frame, and dark glyph strokes across the glow
            // that read as the word without spelling it.
            Add(parts, c, BatchStyle.Street, 0f, 2.65f, 0.66f,
                1.50f, 0.30f, 0.12f);
            Add(parts, c, BatchStyle.BacklitSign, 0f, 2.65f, 0.725f,
                1.32f, 0.20f, 0.03f);
            for (int stroke = -3; stroke <= 3; stroke++)
            {
                Add(parts, c, BatchStyle.Street,
                    stroke * 0.165f, 2.65f, 0.745f,
                    0.055f, (stroke & 1) == 0 ? 0.13f : 0.10f, 0.02f);
            }
        }

        private static void BuildBusShelter(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            Add(parts, c, BatchStyle.Residential, 0f, 1.32f, -0.48f,
                4.25f, 2.45f, 0.10f);
            Add(parts, c, BatchStyle.Street, 0f, 2.62f, 0f,
                4.65f, 0.18f, 1.18f);
            Add(parts, c, BatchStyle.Street, -2.05f, 1.30f, 0f,
                0.16f, 2.60f, 0.16f);
            Add(parts, c, BatchStyle.Street, 0f, 1.30f, -0.48f,
                0.16f, 2.60f, 0.16f);
            Add(parts, c, BatchStyle.Street, 2.05f, 1.30f, 0f,
                0.16f, 2.60f, 0.16f);
            Add(parts, c, BatchStyle.Masonry, 0f, 0.72f, 0.05f,
                2.65f, 0.16f, 0.62f);
            Add(parts, c, BatchStyle.Street, 0f, 1.10f, -0.20f,
                2.65f, 0.62f, 0.14f);
            Add(parts, c, BatchStyle.Street, -0.90f, 0.34f, 0.05f,
                0.14f, 0.68f, 0.14f);
            Add(parts, c, BatchStyle.Street, 0.90f, 0.34f, 0.05f,
                0.14f, 0.68f, 0.14f);
            Add(parts, c, BatchStyle.Street, 2.60f, 1.25f, -0.30f,
                0.14f, 2.50f, 0.14f);
            Add(parts, c, c.Primary, 2.60f, 2.18f, -0.30f,
                0.68f, 0.54f, 0.10f);
        }

        private static void BuildRoadwork(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            Add(parts, c, BatchStyle.Street, -1.45f, 0.52f, 0f,
                0.18f, 1.04f, 0.72f);
            Add(parts, c, BatchStyle.Street, 1.45f, 0.52f, 0f,
                0.18f, 1.04f, 0.72f);
            Add(parts, c, BatchStyle.Masonry, 0f, 0.82f, 0f,
                3.20f, 0.38f, 0.16f);
            Add(parts, c, BatchStyle.Masonry, -1.65f, 0.26f, 1.12f,
                0.46f, 0.52f, 0.46f);
            Add(parts, c, BatchStyle.Masonry, 1.65f, 0.26f, 1.12f,
                0.46f, 0.52f, 0.46f);
            float bikeX = c.Mirror * 0.55f;
            Add(parts, c, BatchStyle.Street, bikeX - 0.72f, 0.58f, -1.15f,
                0.08f, 1.05f, 0.12f);
            Add(parts, c, BatchStyle.Street, bikeX - 0.72f, 0.58f, -1.15f,
                0.92f, 0.08f, 0.12f);
            Add(parts, c, BatchStyle.Street, bikeX + 0.72f, 0.58f, -1.15f,
                0.08f, 1.05f, 0.12f);
            Add(parts, c, BatchStyle.Street, bikeX + 0.72f, 0.58f, -1.15f,
                0.92f, 0.08f, 0.12f);
            Add(parts, c, BatchStyle.Industrial, bikeX, 0.62f, -1.15f,
                1.15f, 0.10f, 0.10f);
            Add(parts, c, BatchStyle.Industrial, bikeX, 0.88f, -1.15f,
                0.10f, 0.72f, 0.10f);
            Add(parts, c, BatchStyle.Street, bikeX + 0.48f, 1.12f, -1.15f,
                0.72f, 0.08f, 0.08f);
            Add(parts, c, BatchStyle.Street, bikeX - 0.12f, 1.00f, -1.15f,
                0.44f, 0.10f, 0.28f);
        }

        private static void BuildFountain(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            const float radius = 3.20f;
            const CityParkSurfaceKind stone = CityParkSurfaceKind.Stone;
            Add(parts, c, BatchStyle.Masonry, stone, 0f, 0.14f, 0f,
                radius * 2f, 0.28f, radius * 2f);
            Add(parts, c, BatchStyle.Masonry, stone, -radius + 0.20f,
                0.48f, 0f, 0.40f, 0.68f, radius * 2f);
            Add(parts, c, BatchStyle.Masonry, stone, radius - 0.20f,
                0.48f, 0f, 0.40f, 0.68f, radius * 2f);
            Add(parts, c, BatchStyle.Masonry, stone, 0f, 0.48f,
                -radius + 0.20f, radius * 2f, 0.68f, 0.40f);
            Add(parts, c, BatchStyle.Masonry, stone, 0f, 0.48f,
                radius - 0.20f, radius * 2f, 0.68f, 0.40f);
            // The standing water stays a flat plane, like the river,
            // the lake and the sea: a texture here would read as silt
            // on glass rather than a weak fountain still running.
            Add(parts, c, BatchStyle.Residential, 0f, 0.32f, 0f,
                radius * 1.62f, 0.08f, radius * 1.62f);
            Add(parts, c, BatchStyle.Masonry, stone, 0f, 0.88f, 0f,
                1.35f, 1.12f, 1.35f);
            Add(parts, c, BatchStyle.Masonry, stone, 0f, 1.74f, 0f,
                0.72f, 0.62f, 0.72f);
            // The figure keeps its near-black street tone - a darkened
            // monument, not a pale one - over the same stone sheet.
            Add(parts, c, BatchStyle.Street, stone, 0f, 2.85f, 0f,
                0.62f, 1.62f, 0.48f);
            Add(parts, c, BatchStyle.Street, stone, 0f, 3.88f, 0f,
                0.58f, 0.58f, 0.58f);
            Add(parts, c, BatchStyle.Street, stone, -0.62f, 3.05f, 0f,
                0.78f, 0.18f, 0.26f);
            Add(parts, c, BatchStyle.Street, stone, 0.62f, 3.05f, 0f,
                0.78f, 0.18f, 0.26f);
        }

        private static void BuildBandstand(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            float width = 6.80f;
            const CityParkSurfaceKind timber = CityParkSurfaceKind.Timber;
            const CityParkSurfaceKind metal =
                CityParkSurfaceKind.PaintedMetal;
            Add(parts, c, BatchStyle.Masonry, CityParkSurfaceKind.Stone,
                0f, 0.18f, 0f, width, 0.36f, 5.60f);
            Add(parts, c, BatchStyle.Residential, timber, 0f, 0.45f, 0f,
                width * 0.92f, 0.18f, 5.10f);
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                for (int zSide = -1; zSide <= 1; zSide += 2)
                {
                    Add(parts, c, BatchStyle.Masonry, timber,
                        xSide * width * 0.43f, 2.25f, zSide * 2.25f,
                        0.28f, 3.60f, 0.28f);
                }
            }

            Add(parts, c, BatchStyle.Residential, timber, 0f, 4.15f, 0f,
                width + 0.55f, 0.30f, 6.15f);
            Add(parts, c, BatchStyle.Street, metal, 0f, 4.55f, 0f,
                width * 0.78f, 0.50f, 4.90f);
            Add(parts, c, BatchStyle.Street, metal, 0f, 5.15f, 0f,
                0.34f, 0.90f, 4.25f);
            Add(parts, c, BatchStyle.Street, metal, -2.15f, 1.14f, -2.34f,
                1.85f, 0.12f, 0.12f);
            Add(parts, c, BatchStyle.Street, metal, 0f, 1.14f, -2.34f,
                1.85f, 0.12f, 0.12f);
            Add(parts, c, BatchStyle.Street, metal, 2.15f, 1.14f, -2.34f,
                1.85f, 0.12f, 0.12f);
        }

        /// <summary>
        /// Two park chess tables, each a stone pedestal on a bedded
        /// footing carrying an inlaid board, flanked by a bench on
        /// either side.
        ///
        /// Nothing here is drawn down to the recipe's origin plane. The
        /// set is anchored at one sampled point but spans nearly four
        /// metres of lawn that keeps sloping under it - a tenth of a
        /// metre between the two tables in the default city - so every
        /// foot asks the terrain what height it stands on and is drawn
        /// down past it. That is what stops the downhill table and its
        /// benches hanging over the grass.
        /// </summary>
        private static void BuildChessTables(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            const CityParkSurfaceKind stone = CityParkSurfaceKind.Stone;
            for (int table = -1; table <= 1; table += 2)
            {
                float x = table * ChessTableOffset;
                float ground = c.GroundDrop(x, 0f) - ChessGroundEmbed;
                AddColumn(parts, c, BatchStyle.Masonry, stone,
                    x, 0f, ground, ChessFootingTopY,
                    ChessFootingSize, ChessFootingSize);
                AddColumn(parts, c, BatchStyle.Masonry, stone,
                    x, 0f, ground, ChessPedestalTopY,
                    ChessPedestalSize, ChessPedestalSize);
                AddColumn(parts, c, BatchStyle.Masonry, stone,
                    x, 0f,
                    ChessTableTopY - ChessSlabThickness,
                    ChessTableTopY,
                    ChessSlabSize, ChessSlabSize);
                BuildChessBoard(c, parts, x);
                for (int side = -1; side <= 1; side += 2)
                {
                    BuildChessBench(c, parts, x, side);
                }
            }
        }

        /// <summary>
        /// One playing board, drawn square by square.
        ///
        /// It cannot be a sheet: the park textures ride world UVs, which
        /// tile by world position, so an eight-by-eight pattern would
        /// land wherever the table happened to stand and cut its own
        /// squares off at the board's edge. Sixty-four drawn squares are
        /// the only way the pattern registers with the board it is on -
        /// a real board's colouring, a1 dark and h1 light, inside a rim
        /// that stops the outer rank bleeding into the stone.
        ///
        /// Which parity is dark is not free, and it is decided by the
        /// planks rather than by taste. Both seats face along the
        /// recipe's forward axis, `Tangent` runs to the left of anyone
        /// facing `+Forward`, and a board is correct only when each
        /// player has a light square in his near-right corner. That
        /// corner is `(file 0, rank 0)` for the man on the near plank
        /// and `(7, 7)` for the man opposite — the same parity, because
        /// an even board maps corner to corner under a half turn. So the
        /// dark squares are the odd ones, which also puts `a1` (the far
        /// file on his left) dark and the white queen on `d1` light.
        /// Drawing the even parity instead is a board that fails at a
        /// glance to anybody who plays, and it was invisible only while
        /// the boards were empty.
        ///
        /// The light half is one plate; the dark squares are set into it
        /// and stand a few millimetres proud, which keeps the two planes
        /// out of a depth fight and gives the inlay the shallow lip real
        /// boards have.
        /// </summary>
        private static void BuildChessBoard(
            RecipeContext c,
            ICollection<DecorationPart> parts,
            float x)
        {
            const CityParkSurfaceKind timber = CityParkSurfaceKind.Timber;
            AddColumn(parts, c, BatchStyle.Masonry, timber,
                x, 0f, ChessTableTopY, ChessLightTopY,
                ChessFieldSize, ChessFieldSize);
            const float squareOffset = (ChessSquaresPerSide - 1) * 0.5f;
            for (int file = 0; file < ChessSquaresPerSide; file++)
            {
                for (int rank = 0; rank < ChessSquaresPerSide; rank++)
                {
                    if (!CityChessBoardGeometry.IsDarkSquare(file, rank))
                    {
                        continue;
                    }

                    AddColumn(parts, c, BatchStyle.Street, timber,
                        x + (file - squareOffset) * ChessSquareSize,
                        (rank - squareOffset) * ChessSquareSize,
                        ChessLightTopY - ChessSquareSink,
                        ChessLightTopY + ChessSquareProud,
                        ChessSquareSize, ChessSquareSize);
                }
            }

            const float rimCenter =
                (ChessFieldSize + ChessRimWidth) * 0.5f;
            const float rimOuter =
                ChessFieldSize + ChessRimWidth * 2f;
            for (int side = -1; side <= 1; side += 2)
            {
                AddColumn(parts, c, BatchStyle.Street, timber,
                    x, side * rimCenter,
                    ChessTableTopY, ChessRimTopY,
                    rimOuter, ChessRimWidth);
                AddColumn(parts, c, BatchStyle.Street, timber,
                    x + side * rimCenter, 0f,
                    ChessTableTopY, ChessRimTopY,
                    ChessRimWidth, ChessFieldSize);
            }
        }

        /// <summary>
        /// One chess bench: the seat it always had, now standing on two
        /// stone legs that run from it down to the lawn under that end,
        /// each ending in a pad bedded into the grass. A seat with
        /// nothing beneath it floats by its own seat height however well
        /// the anchor was placed.
        /// </summary>
        private static void BuildChessBench(
            RecipeContext c,
            ICollection<DecorationPart> parts,
            float x,
            int side)
        {
            const CityParkSurfaceKind stone = CityParkSurfaceKind.Stone;
            float z = side * ChessBenchZ;
            Add(parts, c, BatchStyle.Street, CityParkSurfaceKind.Timber,
                x, ChessBenchSeatCenterY, z,
                ChessBenchWidth, ChessBenchSeatThickness,
                ChessBenchDepth);
            float legTop =
                ChessBenchSeatCenterY - ChessBenchSeatThickness * 0.25f;
            for (int end = -1; end <= 1; end += 2)
            {
                float legX = x + end *
                    (ChessBenchWidth * 0.5f - ChessBenchLegInset);
                float ground = c.GroundDrop(legX, z) - ChessGroundEmbed;
                AddColumn(parts, c, BatchStyle.Masonry, stone,
                    legX, z, ground,
                    ground + ChessGroundEmbed + ChessBenchPadTopY,
                    ChessBenchPadWidth, ChessBenchPadDepth);
                AddColumn(parts, c, BatchStyle.Masonry, stone,
                    legX, z, ground, legTop,
                    ChessBenchLegWidth, ChessBenchLegDepth);
            }
        }

        /// <summary>
        /// The swing frame and its bench only. Two A-frames, each capped
        /// by its own cross beam, carry the long beam that spans them;
        /// the seats and their ropes are deliberately absent here,
        /// because <see cref="CityPlaygroundSwingBuilder"/> hangs them as
        /// moving pendulums instead of baking them into the batch.
        /// </summary>
        private static void BuildPlayground(
            RecipeContext c,
            ICollection<DecorationPart> parts)
        {
            const CityParkSurfaceKind metal =
                CityParkSurfaceKind.PaintedMetal;
            const CityParkSurfaceKind timber = CityParkSurfaceKind.Timber;
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                for (int zSide = -1; zSide <= 1; zSide += 2)
                {
                    Add(parts, c, BatchStyle.Residential, metal,
                        xSide * CityPlaygroundGeometry.PostOffsetX,
                        CityPlaygroundGeometry.PostHeight * 0.5f,
                        zSide * CityPlaygroundGeometry.PostOffsetZ,
                        CityPlaygroundGeometry.PostThickness,
                        CityPlaygroundGeometry.PostHeight,
                        CityPlaygroundGeometry.PostThickness);
                }

                Add(parts, c, BatchStyle.Residential, metal,
                    xSide * CityPlaygroundGeometry.PostOffsetX,
                    CityPlaygroundGeometry.CrossBeamY,
                    0f,
                    CityPlaygroundGeometry.CrossBeamThickness,
                    CityPlaygroundGeometry.CrossBeamThickness,
                    CityPlaygroundGeometry.CrossBeamDepth);
            }

            Add(parts, c, BatchStyle.Residential, metal, 0f,
                CityPlaygroundGeometry.TopBeamY, 0f,
                CityPlaygroundGeometry.TopBeamWidth,
                CityPlaygroundGeometry.TopBeamThickness,
                CityPlaygroundGeometry.TopBeamThickness);

            Add(parts, c, BatchStyle.Masonry, timber, 0f,
                PlaygroundBenchSeatCenterY, PlaygroundBenchZ,
                PlaygroundBenchWidth, PlaygroundBenchSeatThickness,
                PlaygroundBenchDepth);
            Add(parts, c, BatchStyle.Street, metal, 0f, 0.34f,
                PlaygroundBenchZ,
                0.42f, 0.68f, 0.42f);
        }

        /// <summary>
        /// A park-landmark part stated by the two heights it runs
        /// between rather than by a centre and a height. Anything
        /// standing on sampled ground is naturally described that way -
        /// a footing runs from the grass under it up to a fixed height -
        /// and solving for the centre at every call site is where the
        /// half-thicknesses go wrong.
        /// </summary>
        private static void AddColumn(
            ICollection<DecorationPart> parts,
            RecipeContext context,
            BatchStyle style,
            CityParkSurfaceKind surface,
            float x,
            float z,
            float bottomY,
            float topY,
            float width,
            float depth)
        {
            Add(
                parts,
                context,
                style,
                surface,
                x,
                (bottomY + topY) * 0.5f,
                z,
                width,
                Mathf.Max(0.01f, topY - bottomY),
                depth);
        }

        /// <summary>
        /// A park-landmark part: same batch colour as before, but
        /// drawn from the park's own packaged sheet instead of a flat
        /// fill.
        /// </summary>
        private static void Add(
            ICollection<DecorationPart> parts,
            RecipeContext context,
            BatchStyle style,
            CityParkSurfaceKind surface,
            float x,
            float y,
            float z,
            float width,
            float height,
            float depth)
        {
            Add(
                parts,
                context,
                style,
                surface,
                x,
                y,
                z,
                width,
                height,
                depth,
                true);
        }

        private static void Add(
            ICollection<DecorationPart> parts,
            RecipeContext context,
            BatchStyle style,
            float x,
            float y,
            float z,
            float width,
            float height,
            float depth)
        {
            Add(
                parts,
                context,
                style,
                default,
                x,
                y,
                z,
                width,
                height,
                depth,
                false);
        }

        private static void Add(
            ICollection<DecorationPart> parts,
            RecipeContext context,
            BatchStyle style,
            CityParkSurfaceKind surface,
            float x,
            float y,
            float z,
            float width,
            float height,
            float depth,
            bool textured)
        {
            float streetDepthScale = context.StreetDepthScale;
            z *= streetDepthScale;
            depth = Mathf.Max(0.05f, depth * streetDepthScale);
            Vector3 center =
                context.Origin +
                context.Tangent * x +
                Vector3.up * y +
                context.Forward * z;
            Vector3 size = Mathf.Abs(context.Forward.x) > 0.5f
                ? new Vector3(depth, height, width)
                : new Vector3(width, height, depth);
            parts.Add(new DecorationPart(
                new Bounds(center, size),
                style,
                textured
                    ? surface
                    : (CityParkSurfaceKind?)null));
        }

        private static void AddToChunk(
            IDictionary<ChunkCoordinate, ChunkGeometry> chunks,
            Bounds bounds,
            BatchStyle style,
            CityParkSurfaceKind? surface)
        {
            var coordinate = new ChunkCoordinate(
                Mathf.FloorToInt(bounds.center.x / SpatialChunkSize),
                Mathf.FloorToInt(bounds.center.z / SpatialChunkSize));
            if (!chunks.TryGetValue(
                    coordinate,
                    out ChunkGeometry geometry))
            {
                geometry = new ChunkGeometry();
                chunks.Add(coordinate, geometry);
            }

            Vector3 localCenter = bounds.center - coordinate.Origin;
            geometry.Add(
                style,
                surface,
                new Bounds(localCenter, bounds.size));
        }

        private static void AddCollisionToChunk(
            IDictionary<ChunkCoordinate, ChunkGeometry> chunks,
            Bounds bounds)
        {
            var coordinate = new ChunkCoordinate(
                Mathf.FloorToInt(bounds.center.x / SpatialChunkSize),
                Mathf.FloorToInt(bounds.center.z / SpatialChunkSize));
            if (!chunks.TryGetValue(coordinate, out ChunkGeometry geometry))
            {
                geometry = new ChunkGeometry();
                chunks.Add(coordinate, geometry);
            }

            geometry.CollisionBoxes.Add(new Bounds(
                bounds.center - coordinate.Origin,
                bounds.size));
        }

        private static void BuildChunks(
            Transform parent,
            IDictionary<ChunkCoordinate, ChunkGeometry> chunks)
        {
            var coordinates = new List<ChunkCoordinate>(chunks.Keys);
            coordinates.Sort(ChunkCoordinate.Compare);
            Material defaultMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            Material emissiveMaterial = CityNightResources.EmissiveMaterial;
            for (int index = 0; index < coordinates.Count; index++)
            {
                ChunkCoordinate coordinate = coordinates[index];
                ChunkGeometry geometry = chunks[coordinate];
                Transform chunk = new GameObject(
                    $"City Detail Chunk {coordinate.X} {coordinate.Z}")
                    .transform;
                chunk.SetParent(parent, false);
                chunk.localPosition = coordinate.Origin;
                CityStaticCollisionBuilder.AddBoxColliders(
                    chunk,
                    geometry.CollisionBoxes);
                for (int styleIndex = 0;
                     styleIndex < BatchStyleCount;
                     styleIndex++)
                {
                    var style = (BatchStyle)styleIndex;
                    BatchGeometry batch = geometry.Get(style);
                    if (batch == null || batch.Boxes.Count == 0)
                    {
                        continue;
                    }

                    bool emissive =
                        style == BatchStyle.NeonMagenta ||
                        style == BatchStyle.NeonCyan ||
                        style == BatchStyle.BacklitSign;
                    GameObject result =
                        RuntimePrimitiveFactory.CreateCombinedBoxes(
                            GetBatchName(style),
                            chunk,
                            batch.Boxes,
                            GetBatchColor(style),
                            emissive
                                ? emissiveMaterial
                                : defaultMaterial,
                            false);
                    Renderer renderer = result.GetComponent<Renderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    if (emissive)
                    {
                        // Neon and sign lightboxes are electric: they
                        // die to a tinted tube under the day sky.
                        CityNightGlowRegistry.Register(
                            renderer,
                            GetBatchColor(style));
                    }
                }

                BuildParkBatches(chunk, coordinate, geometry);
            }
        }

        /// <summary>
        /// The park landmarks' batches. They keep the batch colour they
        /// always had and add the park's packaged sheet over it, box
        /// projected at the sheet's metre pitch. The chunk transform is
        /// offset by the chunk origin, so the origin goes back into the
        /// UVs: a fountain whose kerb and basin land in two different
        /// chunks still tiles as one continuous stone.
        /// </summary>
        private static void BuildParkBatches(
            Transform chunk,
            ChunkCoordinate coordinate,
            ChunkGeometry geometry)
        {
            List<ParkBatchKey> keys = geometry.GetSortedParkKeys();
            for (int index = 0; index < keys.Count; index++)
            {
                ParkBatchKey key = keys[index];
                BatchGeometry batch = geometry.GetPark(key);
                if (batch.Boxes.Count == 0)
                {
                    continue;
                }

                Color color = GetBatchColor(key.Style);
                GameObject result =
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        $"Park {key.Surface} {key.Style} Details",
                        chunk,
                        batch.Boxes,
                        color,
                        false,
                        CityParkSurfaceAppearance
                            .GetRecipe(key.Surface)
                            .MetersPerTile,
                        CityParkSurfaceAppearance.GetUvMode(key.Surface),
                        coordinate.Origin);
                Renderer renderer = result.GetComponent<Renderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                CityParkSurfaceAppearance.ApplyCombined(
                    renderer,
                    key.Surface,
                    color);
            }
        }

        private static string GetBatchName(BatchStyle style)
        {
            switch (style)
            {
                case BatchStyle.Masonry:
                    return "Masonry Details";
                case BatchStyle.Residential:
                    return "Residential and Park Details";
                case BatchStyle.Industrial:
                    return "Industrial Details";
                case BatchStyle.Street:
                    return "Street Utility Details";
                case BatchStyle.NeonMagenta:
                    return "Magenta Emissive Details";
                case BatchStyle.NeonCyan:
                    return "Cyan Emissive Details";
                case BatchStyle.BacklitSign:
                    return "Backlit Sign Details";
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private static Color GetBatchColor(BatchStyle style)
        {
            switch (style)
            {
                case BatchStyle.Masonry:
                    return MasonryColor;
                case BatchStyle.Residential:
                    return ResidentialColor;
                case BatchStyle.Industrial:
                    return IndustrialColor;
                case BatchStyle.Street:
                    return StreetColor;
                case BatchStyle.NeonMagenta:
                    return MagentaGlow;
                case BatchStyle.NeonCyan:
                    return CyanGlow;
                case BatchStyle.BacklitSign:
                    return BacklitSignGlow;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private static BatchStyle ResolvePrimaryStyle(
            CityDecorationPalette palette)
        {
            switch (palette)
            {
                case CityDecorationPalette.OldTownBrick:
                case CityDecorationPalette.OldTownStone:
                case CityDecorationPalette.ParkStone:
                    return BatchStyle.Masonry;
                case CityDecorationPalette.ResidentialCool:
                case CityDecorationPalette.ResidentialWarm:
                case CityDecorationPalette.ParkPaint:
                    return BatchStyle.Residential;
                case CityDecorationPalette.IndustrialSteel:
                case CityDecorationPalette.IndustrialRust:
                    return BatchStyle.Industrial;
                case CityDecorationPalette.NightlifeMagenta:
                    return BatchStyle.NeonMagenta;
                case CityDecorationPalette.NightlifeCyan:
                    return BatchStyle.NeonCyan;
                case CityDecorationPalette.StreetUtility:
                    return BatchStyle.Street;
                default:
                    return BatchStyle.Street;
            }
        }

        private enum BatchStyle
        {
            Masonry = 0,
            Residential = 1,
            Industrial = 2,
            Street = 3,
            NeonMagenta = 4,
            NeonCyan = 5,
            BacklitSign = 6
        }

        private readonly struct RecipeContext
        {
            public RecipeContext(
                CityLayout layout,
                CityDecorationDescriptor descriptor,
                BuildingLot lot)
            {
                Layout = layout;
                Descriptor = descriptor;
                Lot = lot;
                Origin = descriptor.Position;
                if (descriptor.AnchorKind ==
                        CityDecorationAnchorKind.BuildingFacade ||
                    descriptor.Kind ==
                        CityDecorationKind.NightlifeCinema)
                {
                    Origin = new Vector3(
                        descriptor.Position.x,
                        lot != null
                            ? lot.Center.y
                            : descriptor.Position.y,
                        descriptor.Position.z);
                }

                Forward = ResolveForward(descriptor.Forward, lot);
                Tangent = new Vector3(-Forward.z, 0f, Forward.x);
                Primary = ResolvePrimaryStyle(descriptor.Palette);
                Neon = descriptor.Palette ==
                        CityDecorationPalette.NightlifeCyan
                    ? BatchStyle.NeonCyan
                    : BatchStyle.NeonMagenta;
            }

            public CityLayout Layout { get; }
            public CityDecorationDescriptor Descriptor { get; }
            public BuildingLot Lot { get; }
            public Vector3 Origin { get; }
            public Vector3 Forward { get; }
            public Vector3 Tangent { get; }
            public BatchStyle Primary { get; }
            public BatchStyle Neon { get; }
            public float Mirror => (Descriptor.Variant & 1) == 0 ? 1f : -1f;
            public float StreetDepthScale
            {
                get
                {
                    switch (Descriptor.Kind)
                    {
                        case CityDecorationKind.OldTownStreetMarket:
                            return 0.55f;
                        case CityDecorationKind
                            .ResidentialDiscardedFurniture:
                            return 0.42f;
                        case CityDecorationKind.IndustrialCargo:
                            return 0.28f;
                        case CityDecorationKind
                            .NightlifeVendingAndQueue:
                            return 0.45f;
                        case CityDecorationKind
                            .RoadsideDumpsterAndUtility:
                            return 0.72f;
                        case CityDecorationKind.RoadsidePhoneBooth:
                            return 0.82f;
                        case CityDecorationKind.RoadsideRoadworkAndBicycle:
                            return 0.44f;
                        default:
                            return 1f;
                    }
                }
            }
            public float Width => Lot == null
                ? 8f
                : Mathf.Abs(Forward.x) > 0.5f
                    ? Lot.Size.y
                    : Lot.Size.x;
            public float Depth => Lot == null
                ? 7f
                : Mathf.Abs(Forward.x) > 0.5f
                    ? Lot.Size.x
                    : Lot.Size.y;
            public float Height => Lot == null ? 7f : Lot.Height;

            /// <summary>
            /// How far the terrain under a recipe-local footprint sits
            /// from the recipe's own origin plane: negative downhill,
            /// positive uphill. A ground decoration is anchored at one
            /// sampled point, but the park lawn keeps sloping under the
            /// rest of it, so a foot drawn down to y = 0 hangs in the
            /// air on the downhill side of its own anchor. A foot drawn
            /// down to this drop meets the grass instead.
            ///
            /// Zero when the sample falls outside the terrain contract,
            /// which leaves the recipe on its origin plane exactly as
            /// before.
            /// </summary>
            public float GroundDrop(float x, float z)
            {
                if (Layout == null)
                {
                    return 0f;
                }

                Vector3 foot =
                    Origin +
                    Tangent * x +
                    Forward * (z * StreetDepthScale);
                return CityTerrainSurfacePlan.TrySampleGroundTop(
                    Layout,
                    new Vector2(foot.x, foot.z),
                    out float top,
                    out _)
                    ? top - Origin.y
                    : 0f;
            }

            private static Vector3 ResolveForward(
                Vector3 candidate,
                BuildingLot lot)
            {
                candidate.y = 0f;
                if (!IsFinite(candidate.x) ||
                    !IsFinite(candidate.z) ||
                    candidate.sqrMagnitude < 0.25f)
                {
                    candidate = lot != null && lot.HasRoadFrontage
                        ? new Vector3(
                            lot.FrontageDirection.x,
                            0f,
                            lot.FrontageDirection.y)
                        : Vector3.back;
                }

                return Mathf.Abs(candidate.x) > Mathf.Abs(candidate.z)
                    ? new Vector3(Mathf.Sign(candidate.x), 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(candidate.z));
            }

            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }

        private readonly struct DecorationPart
        {
            public DecorationPart(
                Bounds bounds,
                BatchStyle style,
                CityParkSurfaceKind? surface)
            {
                Bounds = bounds;
                Style = style;
                Surface = surface;
            }

            public Bounds Bounds { get; }
            public BatchStyle Style { get; }

            /// <summary>
            /// The packaged park sheet this part is made of, when it
            /// belongs to one of the four park landmarks. Null for
            /// every other decoration, which stays on its flat batch
            /// colour.
            /// </summary>
            public CityParkSurfaceKind? Surface { get; }
        }

        private sealed class BatchGeometry
        {
            public readonly List<Bounds> Boxes = new List<Bounds>();
        }

        private sealed class ChunkGeometry
        {
            private readonly BatchGeometry[] batches =
                new BatchGeometry[BatchStyleCount];

            // Park landmark parts batch on a second axis: the same
            // seven colours, split again by the packaged sheet they
            // are drawn from. Sorted on read so a chunk always emits
            // its renderers in the same order.
            private readonly Dictionary<ParkBatchKey, BatchGeometry>
                parkBatches =
                    new Dictionary<ParkBatchKey, BatchGeometry>();

            public List<Bounds> CollisionBoxes { get; } =
                new List<Bounds>();

            public void Add(
                BatchStyle style,
                CityParkSurfaceKind? surface,
                Bounds bounds)
            {
                if (surface.HasValue)
                {
                    var key = new ParkBatchKey(style, surface.Value);
                    if (!parkBatches.TryGetValue(
                            key,
                            out BatchGeometry parkBatch))
                    {
                        parkBatch = new BatchGeometry();
                        parkBatches.Add(key, parkBatch);
                    }

                    parkBatch.Boxes.Add(bounds);
                    return;
                }

                int index = (int)style;
                BatchGeometry batch = batches[index];
                if (batch == null)
                {
                    batch = new BatchGeometry();
                    batches[index] = batch;
                }

                batch.Boxes.Add(bounds);
            }

            public BatchGeometry Get(BatchStyle style)
            {
                return batches[(int)style];
            }

            public List<ParkBatchKey> GetSortedParkKeys()
            {
                var keys = new List<ParkBatchKey>(parkBatches.Keys);
                keys.Sort(ParkBatchKey.Compare);
                return keys;
            }

            public BatchGeometry GetPark(ParkBatchKey key)
            {
                return parkBatches[key];
            }
        }

        private readonly struct ParkBatchKey : IEquatable<ParkBatchKey>
        {
            public ParkBatchKey(
                BatchStyle style,
                CityParkSurfaceKind surface)
            {
                Style = style;
                Surface = surface;
            }

            public BatchStyle Style { get; }
            public CityParkSurfaceKind Surface { get; }

            public static int Compare(
                ParkBatchKey left,
                ParkBatchKey right)
            {
                int surfaceComparison =
                    ((int)left.Surface).CompareTo((int)right.Surface);
                return surfaceComparison != 0
                    ? surfaceComparison
                    : ((int)left.Style).CompareTo((int)right.Style);
            }

            public bool Equals(ParkBatchKey other)
            {
                return Style == other.Style &&
                       Surface == other.Surface;
            }

            public override bool Equals(object obj)
            {
                return obj is ParkBatchKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return ((int)Style * 397) ^ (int)Surface;
            }
        }

        private readonly struct ChunkCoordinate :
            IEquatable<ChunkCoordinate>
        {
            public ChunkCoordinate(int x, int z)
            {
                X = x;
                Z = z;
            }

            public int X { get; }
            public int Z { get; }
            public Vector3 Origin => new Vector3(
                X * SpatialChunkSize,
                0f,
                Z * SpatialChunkSize);

            public bool Equals(ChunkCoordinate other)
            {
                return X == other.X && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is ChunkCoordinate other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 397) ^ Z;
                }
            }

            public static int Compare(
                ChunkCoordinate first,
                ChunkCoordinate second)
            {
                int x = first.X.CompareTo(second.X);
                return x != 0 ? x : first.Z.CompareTo(second.Z);
            }
        }
    }
}
