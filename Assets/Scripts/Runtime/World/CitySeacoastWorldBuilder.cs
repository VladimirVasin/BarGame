using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Materialises a <see cref="CitySeacoastPlan"/>: the dressing
    /// batches into one combined oriented-box mesh per 48 m chunk and
    /// style, and each lamp descriptor becomes a real fixture — the
    /// rental hut's hooded door bulb and the pier's kerosene hand lamp
    /// exactly as they were at the lake, and the esplanade's cast-iron
    /// posts as glow-only fixtures one shade dimmer than the river
    /// embankment's. The navigation light burns offshore now, raised
    /// by the lighthouse island's own builder.
    ///
    /// The sea itself is built here too: chunked animated sheets of
    /// the shared water shader where the flat municipal slab used to
    /// be, over the continuous sand slope — the
    /// depth-threshold foam draws the surf line along that slope on
    /// its own. Each sheet runs a cosmetic apron past the map's north
    /// edge, because past the apron there is only fog anyway.
    /// </summary>
    public static class CitySeacoastWorldBuilder
    {
        public const string RootName = "North Seacoast";
        private const float SpatialChunkSize = 48f;

        // The flat palette. The textured styles will be transcribed
        // into the seacoast texture generator when the skin pass
        // lands, which solves the albedo compensation that keeps the
        // textured product at this exact brightness — edit them
        // together. The timber and hull values are the lake's, moved
        // here with the boats they colour.
        internal static readonly Color Concrete =
            new Color(0.29f, 0.29f, 0.27f);
        internal static readonly Color Granite =
            new Color(0.34f, 0.34f, 0.32f);
        internal static readonly Color Planking =
            new Color(0.31f, 0.28f, 0.24f);
        internal static readonly Color TarredTimber =
            new Color(0.16f, 0.14f, 0.12f);
        internal static readonly Color HullPaint =
            new Color(0.26f, 0.32f, 0.31f);
        internal static readonly Color HullTar =
            new Color(0.13f, 0.12f, 0.11f);
        internal static readonly Color Iron =
            new Color(0.075f, 0.085f, 0.085f);
        internal static readonly Color RustIron =
            new Color(0.28f, 0.17f, 0.12f);
        internal static readonly Color Grass =
            new Color(0.30f, 0.32f, 0.19f);
        // The one legible colour on the whole shore: what is left of
        // the paint on the hire board.
        internal static readonly Color PaintAccent =
            new Color(0.52f, 0.44f, 0.20f);
        internal static readonly Color Litter =
            new Color(0.27f, 0.22f, 0.16f);

        // The hut bulb is the lake's, bolt for bolt: same warm
        // tungsten over the same kind of door, dimmed by day, full at
        // night, never switched off.
        internal static readonly Color HutBulbColor =
            new Color(1.00f, 0.80f, 0.55f);
        internal const float HutBulbNightIntensity = 64f;
        internal const float HutBulbDayIntensity = 14f;
        internal const float HutBulbRange = 7.0f;

        // The esplanade lamps glow without casting: iron posts and an
        // emissive plafond on the glow registry, the embankment lamp
        // recipe gone older and dimmer — these were here first.
        internal static readonly Color EsplanadeLampGlow =
            new Color(1.08f, 0.62f, 0.28f);

        // Each lamp's always-on fog halo: warm HDR multiples of the
        // plafond glow, the same recipe as the river's lanterns, so
        // the row reads down the waterline instead of dissolving.
        private const float EsplanadeHaloInnerSize = 0.85f;
        private const float EsplanadeHaloOuterSize = 2.40f;
        private static readonly Color EsplanadeHaloInner =
            new Color(2.81f, 1.61f, 0.73f, 0.20f);
        private static readonly Color EsplanadeHaloOuter =
            new Color(1.62f, 0.93f, 0.42f, 0.055f);

        private static readonly Color LampIronColor =
            new Color(0.070f, 0.075f, 0.075f);

        public static GameObject Build(
            Transform parent,
            CitySeacoastPlan plan,
            CityLayout layout)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            BuildSea(root, plan.Frame, layout);
            BuildDressing(root, plan);
            BuildLamps(root, plan);

            return root.gameObject;
        }

        /// <summary>
        /// The sea: animated sheets over a shelving bed. The bed only
        /// exists near the shore — deeper out the depth fade bottoms
        /// into the deep colour on its own, which is all the honesty a
        /// sea seen through this fog can use.
        /// </summary>
        private static void BuildSea(
            Transform root,
            in CitySeacoastFrame frame,
            CityLayout layout)
        {
            Transform sea = new GameObject("Sea").transform;
            sea.SetParent(root, false);

            // The swell dies on the sand: the shader's shore fade
            // ramps up from the shallow coastal band. Its anchor stays
            // layout-owned while the sand continues below the water.
            CitySeaResources.ConfigureShoreFade(
                frame.WaterlineZ +
                CitySeacoastSeaLayout.InnerShelfReach);

            var sheets = new List<Rect>();
            CitySeacoastSeaLayout.CreateSheetRects(frame, sheets);
            for (int index = 0; index < sheets.Count; index++)
            {
                CityWaterSurfaceFactory.CreateSlopedSurface(
                    $"Sea Water {index:D2}",
                    sea,
                    sheets[index],
                    frame.SeaTopY,
                    frame.SeaTopY,
                    CitySeaResources.WaterMaterial);
            }

            BuildSwash(sea, frame, layout);

            // The river pours in: its own material continued past the
            // waterline on a shallow downhill sheet, so the mouth
            // reads as water flowing into water. The south edge sits
            // at the river sheet's exact height and the same material
            // carries the same world-driven waves, so the joint is
            // invisible; the far end dives under the sea's datum and
            // the two swells interleave into the churn a river mouth
            // owes the eye.
            CityWaterSurfaceFactory.CreateSlopedSurface(
                "Mouth Spill",
                sea,
                CitySeacoastSeaLayout.CreateMouthSpillRect(frame),
                frame.MouthWaterY,
                frame.SeaTopY - CitySeacoastSeaLayout.SpillDip,
                CityRiverResources.WaterMaterial);

            // Same sand, same UVs and matching edge vertices. Water
            // absorption supplies the darkening as the slope gets deeper.
            GameObject bed = CityTerrainSurfaceWorldBuilder.Build(
                "Sea Bed Slope", sea, layout, CitySurfaceKind.Beach,
                CityExteriorAppearance.BeachSand, false,
                CitySeacoastSurfaceAppearance.GetRecipe(CitySeacoastSurfaceKind.Sand).MetersPerTile,
                seabedOnly: true);
            if (bed != null)
                CitySeacoastSurfaceAppearance.ApplyCombined(bed.GetComponent<Renderer>(),
                    CitySeacoastSurfaceKind.Sand, CityExteriorAppearance.BeachSand);
        }

        // Reuse the water grid with the actual sand height. Only the open
        // beach gets a film: the station seawall and river mouth stay dry
        // on their landward side. No collision or navigation is added.
        private static void BuildSwash(
            Transform sea,
            CitySeacoastFrame frame,
            CityLayout layout)
        {
            int index = 0;
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (surface.Kind != CitySurfaceKind.Beach ||
                    Mathf.Abs(surface.WorldBounds.yMax - frame.WaterlineZ) > 0.01f)
                {
                    continue;
                }

                AddStrip(frame.WestZone);
                AddStrip(frame.EastZone);

                void AddStrip(Rect zone)
                {
                    float west = Mathf.Max(surface.WorldBounds.xMin, zone.xMin);
                    float east = Mathf.Min(surface.WorldBounds.xMax, zone.xMax);
                    if (east - west < 0.01f)
                    {
                        return;
                    }

                    Rect bounds = Rect.MinMaxRect(
                        west, frame.WaterlineZ - CitySeaResources.SwashMeshReach,
                        east, frame.WaterlineZ + CitySeaResources.SwashSeaReach);
                    CityWaterSurfaceFactory.CreateSlopedSurface(
                        $"Sea Swash {index++:D2}", sea, bounds,
                        frame.BeachEdgeTopY, frame.SeaTopY,
                        CitySeaResources.SwashMaterial,
                        sampleTop: point =>
                        {
                            Vector2 sandPoint = new Vector2(
                                point.x, Mathf.Min(point.y, frame.WaterlineZ));
                            float sandTop = CityTerrainSurfacePlan.SampleTop(
                                layout, surface, sandPoint) + 0.018f;
                            float seaward = Mathf.SmoothStep(0f, 1f,
                                (point.y - frame.WaterlineZ) /
                                CitySeaResources.SwashSeaReach);
                            return Mathf.Lerp(sandTop, frame.SeaTopY, seaward);
                        },
                        surfacePitch: 0.30f);
                }
            }
        }

        private static void BuildDressing(
            Transform root,
            CitySeacoastPlan plan)
        {
            var batches =
                new Dictionary<BatchKey, List<RuntimeOrientedBox>>();
            var importedBatches = new Dictionary<
                BatchKey,
                List<RuntimeMeshPlacement>>();
            var collisionBatches =
                new Dictionary<BatchKey, List<RuntimeOrientedBox>>();
            var importedIds = new HashSet<string>(StringComparer.Ordinal);
            CityMiscAssetProvider miscProvider =
                CityMiscAssetProvider.Load();

            TryAppendImportedBoats(
                plan.Parts,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendImportedSlipwayBarrier(
                plan.Parts,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendImportedBarge(
                plan,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendImportedDriftwood(
                plan.Parts,
                miscProvider,
                importedBatches,
                importedIds);

            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = plan.Parts[index];
                var key = new BatchKey(
                    Mathf.FloorToInt(part.Center.x / SpatialChunkSize),
                    Mathf.FloorToInt(part.Center.z / SpatialChunkSize),
                    part.Style);
                if (importedIds.Contains(part.StableId))
                {
                    if (part.BlocksMovement)
                    {
                        AppendBox(collisionBatches, key, part);
                    }

                    continue;
                }

                if (!batches.TryGetValue(
                        key,
                        out List<RuntimeOrientedBox> boxes))
                {
                    boxes = new List<RuntimeOrientedBox>();
                    batches.Add(key, boxes);
                }

                boxes.Add(new RuntimeOrientedBox(
                    part.Center,
                    part.Rotation,
                    part.Size));
            }

            var keys = new List<BatchKey>(batches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                CitySeacoastSurfaceKind? surface =
                    ResolveSurface(key.Style);
                float? uvTileSize = surface.HasValue
                    ? CitySeacoastSurfaceAppearance
                        .GetRecipe(surface.Value).MetersPerTile
                    : (float?)null;
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"Seacoast Chunk {key.X} {key.Z} {key.Style}",
                        root,
                        batches[key],
                        ResolveColor(key.Style),
                        CitySeacoastRules.BlocksMovement(key.Style),
                        uvTileSize);
                if (surface.HasValue)
                {
                    CitySeacoastSurfaceAppearance.ApplyCombined(
                        chunk.GetComponent<Renderer>(),
                        surface.Value,
                        ResolveColor(key.Style));
                }
            }

            keys = new List<BatchKey>(importedBatches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                CitySeacoastSurfaceKind? surface =
                    ResolveSurface(key.Style);
                float? uvTileSize = surface.HasValue
                    ? CitySeacoastSurfaceAppearance
                        .GetRecipe(surface.Value).MetersPerTile
                    : (float?)null;
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedMeshes(
                        $"Imported Seacoast Chunk {key.X} {key.Z} " +
                        $"{key.Style}",
                        root,
                        importedBatches[key],
                        ResolveColor(key.Style),
                        false,
                        uvTileSize);
                if (surface.HasValue)
                {
                    CitySeacoastSurfaceAppearance.ApplyCombined(
                        chunk.GetComponent<Renderer>(),
                        surface.Value,
                        ResolveColor(key.Style));
                }
            }

            keys = new List<BatchKey>(collisionBatches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                GameObject proxy =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"Seacoast Imported Collision {key.X} " +
                        $"{key.Z} {key.Style}",
                        root,
                        collisionBatches[key],
                        ResolveColor(key.Style),
                        true);
                proxy.GetComponent<Renderer>().enabled = false;
            }
        }

        private static void TryAppendImportedBoats(
            IReadOnlyList<CitySeacoastPartDescriptor> parts,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            var groups = new Dictionary<
                int,
                List<CitySeacoastPartDescriptor>>();
            for (int index = 0; index < parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = parts[index];
                if (part.BoatOrdinal < 0 ||
                    (part.Kind != CitySeacoastPartKind.Boat &&
                     part.Kind != CitySeacoastPartKind.BoatRest))
                {
                    continue;
                }

                if (!groups.TryGetValue(
                        part.BoatOrdinal,
                        out List<CitySeacoastPartDescriptor> group))
                {
                    group = new List<CitySeacoastPartDescriptor>();
                    groups.Add(part.BoatOrdinal, group);
                }

                group.Add(part);
            }

            var ordinals = new List<int>(groups.Keys);
            ordinals.Sort();
            for (int index = 0; index < ordinals.Count; index++)
            {
                int ordinal = ordinals[index];
                List<CitySeacoastPartDescriptor> group = groups[ordinal];
                CitySeacoastPartDescriptor restA = default;
                CitySeacoastPartDescriptor restB = default;
                int restCount = 0;
                for (int partIndex = 0;
                     partIndex < group.Count;
                     partIndex++)
                {
                    if (group[partIndex].Kind !=
                        CitySeacoastPartKind.BoatRest)
                    {
                        continue;
                    }

                    if (restCount == 0)
                    {
                        restA = group[partIndex];
                    }
                    else if (restCount == 1)
                    {
                        restB = group[partIndex];
                    }

                    restCount++;
                }

                if (restCount < 2)
                {
                    continue;
                }

                Vector3 origin = new Vector3(
                    (restA.Center.x + restB.Center.x) * 0.5f,
                    Mathf.Min(
                        restA.Center.y - restA.Size.y * 0.5f,
                        restB.Center.y - restB.Size.y * 0.5f),
                    (restA.Center.z + restB.Center.z) * 0.5f);
                TryAppendAssembly(
                    provider,
                    CityMiscKind.SeacoastBoat,
                    (int)restA.Variant,
                    origin,
                    restA.Rotation,
                    CitySeacoastStyle.HullPaint,
                    group,
                    Vector3.one,
                    batches,
                    importedIds);

                string oarId =
                    $"seacoast-boat-{ordinal:D2}-oar";
                for (int partIndex = 0;
                     partIndex < parts.Count;
                     partIndex++)
                {
                    CitySeacoastPartDescriptor part = parts[partIndex];
                    if (!string.Equals(
                            part.StableId,
                            oarId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    TryAppendAssembly(
                        provider,
                        CityMiscKind.SeacoastOar,
                        0,
                        origin,
                        restA.Rotation,
                        CitySeacoastStyle.Litter,
                        new List<CitySeacoastPartDescriptor> { part },
                        Vector3.one,
                        batches,
                        importedIds);
                    break;
                }
            }
        }

        private static void TryAppendImportedSlipwayBarrier(
            IReadOnlyList<CitySeacoastPartDescriptor> parts,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            var barrier = new List<CitySeacoastPartDescriptor>(3);
            CitySeacoastPartDescriptor chain = default;
            bool hasChain = false;
            for (int index = 0; index < parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = parts[index];
                if (part.Kind != CitySeacoastPartKind.Bollard)
                {
                    continue;
                }

                barrier.Add(part);
                if (part.StableId.EndsWith(
                        "-chain",
                        StringComparison.Ordinal))
                {
                    chain = part;
                    hasChain = true;
                }
            }

            if (!hasChain || barrier.Count != 3)
            {
                return;
            }

            TryAppendAssembly(
                provider,
                CityMiscKind.SeacoastSlipwayBarrier,
                0,
                new Vector3(
                    chain.Center.x,
                    chain.Center.y - 0.78f,
                    chain.Center.z),
                chain.Rotation,
                CitySeacoastStyle.Iron,
                barrier,
                Vector3.one,
                batches,
                importedIds);
        }

        private static void TryAppendImportedBarge(
            CitySeacoastPlan plan,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            var barge = new List<CitySeacoastPartDescriptor>();
            CitySeacoastPartDescriptor deck = default;
            bool hasDeck = false;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = plan.Parts[index];
                if (part.Kind != CitySeacoastPartKind.Barge)
                {
                    continue;
                }

                barge.Add(part);
                if (part.StableId.EndsWith(
                        "-deck",
                        StringComparison.Ordinal))
                {
                    deck = part;
                    hasDeck = true;
                }
            }

            if (!hasDeck)
            {
                return;
            }

            Quaternion yaw = Quaternion.Euler(
                0f,
                deck.Rotation.eulerAngles.y,
                0f);
            TryAppendAssembly(
                provider,
                CityMiscKind.SeacoastBarge,
                0,
                new Vector3(
                    deck.Center.x,
                    plan.Frame.SeaTopY,
                    deck.Center.z),
                yaw,
                CitySeacoastStyle.RustIron,
                barge,
                Vector3.one,
                batches,
                importedIds);
        }

        private static void TryAppendImportedDriftwood(
            IReadOnlyList<CitySeacoastPartDescriptor> parts,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            if (provider == null ||
                !CityMiscAssetProvider.Supports(
                    CityMiscKind.SeacoastDriftwood))
            {
                return;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                CitySeacoastPartDescriptor source = parts[index];
                if (source.Kind != CitySeacoastPartKind.Driftwood)
                {
                    continue;
                }

                int variant;
                try
                {
                    variant = provider.SelectVariant(
                        CityMiscKind.SeacoastDriftwood,
                        source.StableId);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!TryGetImportedParts(
                        provider,
                        CityMiscKind.SeacoastDriftwood,
                        variant,
                        out List<CityMiscMeshPart> authored))
                {
                    continue;
                }

                Vector3 authoredSize = authored[0].Mesh.bounds.size;
                Vector3 scale = new Vector3(
                    authoredSize.x > 0.0001f
                        ? source.Size.x / authoredSize.x
                        : 1f,
                    1f,
                    1f);
                TryAppendAssembly(
                    provider,
                    CityMiscKind.SeacoastDriftwood,
                    variant,
                    new Vector3(
                        source.Center.x,
                        source.Center.y - source.Size.y * 0.5f,
                        source.Center.z),
                    source.Rotation,
                    source.Style,
                    new List<CitySeacoastPartDescriptor> { source },
                    scale,
                    batches,
                    importedIds);
            }
        }

        private static bool TryAppendAssembly(
            CityMiscAssetProvider provider,
            CityMiscKind kind,
            int variant,
            Vector3 origin,
            Quaternion rotation,
            CitySeacoastStyle sourceStyle,
            IReadOnlyList<CitySeacoastPartDescriptor> sourceParts,
            Vector3 scale,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            if (sourceParts == null || sourceParts.Count == 0 ||
                !TryGetImportedParts(
                    provider,
                    kind,
                    variant,
                    out List<CityMiscMeshPart> parts))
            {
                return false;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                CityMiscMeshPart part = parts[index];
                CitySeacoastStyle style = ResolveImportedStyle(
                    kind,
                    part.Role,
                    sourceStyle);
                var key = new BatchKey(
                    Mathf.FloorToInt(origin.x / SpatialChunkSize),
                    Mathf.FloorToInt(origin.z / SpatialChunkSize),
                    style);
                if (!batches.TryGetValue(
                        key,
                        out List<RuntimeMeshPlacement> placements))
                {
                    placements = new List<RuntimeMeshPlacement>();
                    batches.Add(key, placements);
                }

                placements.Add(new RuntimeMeshPlacement(
                    part.Mesh,
                    origin,
                    rotation,
                    scale));
            }

            for (int index = 0; index < sourceParts.Count; index++)
            {
                importedIds.Add(sourceParts[index].StableId);
            }

            return true;
        }

        private static bool TryGetImportedParts(
            CityMiscAssetProvider provider,
            CityMiscKind kind,
            int variant,
            out List<CityMiscMeshPart> parts)
        {
            parts = null;
            if (provider == null || !CityMiscAssetProvider.Supports(kind))
            {
                return false;
            }

            try
            {
                int count = CityMiscAssetProvider.GetPartCount(kind);
                var result = new List<CityMiscMeshPart>(count);
                for (int index = 0; index < count; index++)
                {
                    result.Add(provider.GetPartOrThrow(
                        kind,
                        variant,
                        index));
                }

                parts = result;
                return result.Count > 0;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static CitySeacoastStyle ResolveImportedStyle(
            CityMiscKind kind,
            CityMiscMeshRole role,
            CitySeacoastStyle sourceStyle)
        {
            switch (kind)
            {
                case CityMiscKind.SeacoastBoat:
                    if (role == CityMiscMeshRole.Timber)
                    {
                        return CitySeacoastStyle.TarredTimber;
                    }

                    return role == CityMiscMeshRole.Street
                        ? CitySeacoastStyle.HullTar
                        : CitySeacoastStyle.HullPaint;
                case CityMiscKind.SeacoastOar:
                case CityMiscKind.SeacoastDriftwood:
                    return CitySeacoastStyle.Litter;
                case CityMiscKind.SeacoastSlipwayBarrier:
                    return CitySeacoastStyle.Iron;
                case CityMiscKind.SeacoastBarge:
                    return role == CityMiscMeshRole.Industrial
                        ? CitySeacoastStyle.RustIron
                        : CitySeacoastStyle.HullTar;
                default:
                    return sourceStyle;
            }
        }

        private static void AppendBox(
            IDictionary<BatchKey, List<RuntimeOrientedBox>> batches,
            BatchKey key,
            CitySeacoastPartDescriptor part)
        {
            if (!batches.TryGetValue(
                    key,
                    out List<RuntimeOrientedBox> boxes))
            {
                boxes = new List<RuntimeOrientedBox>();
                batches.Add(key, boxes);
            }

            boxes.Add(new RuntimeOrientedBox(
                part.Center,
                part.Rotation,
                part.Size));
        }

        private static CitySeacoastSurfaceKind? ResolveSurface(
            CitySeacoastStyle style)
        {
            switch (style)
            {
                case CitySeacoastStyle.Concrete:
                    return CitySeacoastSurfaceKind.Concrete;
                case CitySeacoastStyle.Granite:
                    return CitySeacoastSurfaceKind.Granite;
                case CitySeacoastStyle.Planking:
                case CitySeacoastStyle.TarredTimber:
                    return CitySeacoastSurfaceKind.Plank;
                case CitySeacoastStyle.HullPaint:
                case CitySeacoastStyle.HullTar:
                    return CitySeacoastSurfaceKind.Hull;
                case CitySeacoastStyle.Sand:
                    return CitySeacoastSurfaceKind.Sand;
                default:
                    // Iron, rust, grass, sign paint and litter stay
                    // flat colour: their members are too thin for a
                    // sheet to read through the PS1 composite.
                    return null;
            }
        }

        private static void BuildLamps(
            Transform root,
            CitySeacoastPlan plan)
        {
            var esplanadePosts = new List<Bounds>();
            var esplanadeBulbs = new List<Bounds>();
            var esplanadeHaloPositions = new List<Vector3>();
            for (int index = 0; index < plan.Lamps.Count; index++)
            {
                CitySeacoastLampDescriptor lamp = plan.Lamps[index];
                switch (lamp.Kind)
                {
                    case CitySeacoastLampKind.HutDoor:
                        BuildHutDoorLamp(root, lamp);
                        break;
                    case CitySeacoastLampKind.PierHead:
                        CityHandLampWorldBuilder.Build(
                            root,
                            "Seacoast Pier Hand Lamp",
                            lamp.GroundPosition,
                            lamp.YawDegrees);
                        break;
                    default:
                        esplanadePosts.Add(new Bounds(
                            lamp.GroundPosition + Vector3.up * 1.25f,
                            new Vector3(0.16f, 2.5f, 0.16f)));
                        esplanadeBulbs.Add(new Bounds(
                            lamp.GroundPosition + Vector3.up * 2.62f,
                            new Vector3(0.42f, 0.24f, 0.42f)));
                        esplanadeHaloPositions.Add(
                            lamp.GroundPosition + Vector3.up * 2.62f);
                        break;
                }
            }

            if (esplanadePosts.Count == 0)
            {
                return;
            }

            Transform lights = new GameObject(
                "Esplanade Lamps").transform;
            lights.SetParent(root, false);
            // The blurred ball each lamp is at a distance in fog: the
            // waterline row stays legible from the pier and the dunes
            // where the bare plafond dissolves by twenty metres.
            for (int index = 0;
                 index < esplanadeHaloPositions.Count;
                 index++)
            {
                CityLightHalo.CreateNightRegistered(
                    lights,
                    esplanadeHaloPositions[index],
                    EsplanadeHaloInnerSize,
                    EsplanadeHaloOuterSize,
                    EsplanadeHaloInner,
                    EsplanadeHaloOuter);
            }
            RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Esplanade Lamp Posts",
                lights,
                esplanadePosts,
                Iron,
                true);
            GameObject glow =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Esplanade Lamp Glow",
                    lights,
                    esplanadeBulbs,
                    EsplanadeLampGlow,
                    CityNightResources.EmissiveMaterial);
            CityNightGlowRegistry.Register(
                glow.GetComponent<Renderer>(),
                EsplanadeLampGlow);
        }

        /// <summary>
        /// The rental hut's door bulb, rebuilt board for board from
        /// its lake recipe: a short stem into the eave, a tin hood, a
        /// bare emissive bulb, a fog halo, and a night-scaled point
        /// light with the day floor of a bulb nobody ever came back
        /// to switch off.
        /// </summary>
        private static void BuildHutDoorLamp(
            Transform parent,
            CitySeacoastLampDescriptor descriptor)
        {
            Transform assembly = new GameObject(
                "Seacoast Hut Door Lamp").transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                descriptor.GroundPosition,
                Quaternion.Euler(0f, descriptor.YawDegrees, 0f));

            RuntimePrimitiveFactory.CreateBox(
                "Hut Lamp Stem",
                assembly,
                new Vector3(0f, 2.32f, 0f),
                new Vector3(0.05f, 0.24f, 0.05f),
                LampIronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Hut Lamp Hood",
                assembly,
                new Vector3(0f, 2.15f, 0f),
                new Vector3(0.24f, 0.07f, 0.24f),
                LampIronColor,
                false);

            Color glow = MultiplyRgb(HutBulbColor, 4.6f, 1f);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Hut Lamp Bulb",
                assembly,
                new Vector3(0f, 2.01f, 0f),
                new Vector3(0.14f, 0.22f, 0.14f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                glow);

            GameObject emitter = new GameObject("Hut Lamp Light");
            emitter.transform.SetParent(assembly, false);
            emitter.transform.localPosition =
                new Vector3(0f, 1.99f, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = HutBulbColor;
            light.intensity = HutBulbNightIntensity;
            light.range = HutBulbRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            GameObject haloObject = new GameObject("Hut Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.40f,
                1.10f,
                MultiplyRgb(HutBulbColor, 4.2f, 0.18f),
                MultiplyRgb(HutBulbColor, 2.1f, 0.05f));
            CityNightSiteLightRegistry.Register(
                light,
                HutBulbNightIntensity,
                HutBulbDayIntensity,
                halo);
        }

        private static Color MultiplyRgb(
            Color color,
            float multiplier,
            float alpha)
        {
            return new Color(
                color.r * multiplier,
                color.g * multiplier,
                color.b * multiplier,
                alpha);
        }

        private static Color ResolveColor(CitySeacoastStyle style)
        {
            switch (style)
            {
                case CitySeacoastStyle.Concrete:
                    return Concrete;
                case CitySeacoastStyle.Granite:
                    return Granite;
                case CitySeacoastStyle.Planking:
                    return Planking;
                case CitySeacoastStyle.TarredTimber:
                    return TarredTimber;
                case CitySeacoastStyle.HullPaint:
                    return HullPaint;
                case CitySeacoastStyle.HullTar:
                    return HullTar;
                case CitySeacoastStyle.Iron:
                    return Iron;
                case CitySeacoastStyle.RustIron:
                    return RustIron;
                case CitySeacoastStyle.Grass:
                    return Grass;
                case CitySeacoastStyle.PaintAccent:
                    return PaintAccent;
                case CitySeacoastStyle.Litter:
                    return Litter;
                // The mouth banks are the terrain skin's own colour,
                // so the cut and the shore it closes read as one sand.
                case CitySeacoastStyle.Sand:
                    return CityExteriorAppearance.BeachSand;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(int x, int z, CitySeacoastStyle style)
            {
                X = x;
                Z = z;
                Style = style;
            }

            public int X { get; }
            public int Z { get; }
            public CitySeacoastStyle Style { get; }

            public bool Equals(BatchKey other)
            {
                return X == other.X &&
                       Z == other.Z &&
                       Style == other.Style;
            }

            public override bool Equals(object obj)
            {
                return obj is BatchKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = (hash * 397) ^ Z;
                    return (hash * 397) ^ (int)Style;
                }
            }

            public static int Compare(BatchKey left, BatchKey right)
            {
                int x = left.X.CompareTo(right.X);
                if (x != 0)
                {
                    return x;
                }

                int z = left.Z.CompareTo(right.Z);
                return z != 0 ? z : left.Style.CompareTo(right.Style);
            }
        }
    }

    /// <summary>
    /// The sea's pure geometry: where the sheets go and how the shore
    /// shelves. Split out of the builder so the arithmetic that keeps
    /// the sheets chunked, the apron cosmetic and the surf line inside
    /// the shader's foam reach is testable without a scene.
    /// </summary>
    internal static class CitySeacoastSeaLayout
    {
        /// <summary>
        /// Sheets never exceed a world chunk, so far ones frustum-cull
        /// under the 48 m far plane; world-position waves make the
        /// seams invisible by construction.
        /// </summary>
        internal const float MaximumSheetWidth = 48f;

        /// <summary>
        /// How far each sheet runs past the sea row into the fog. The
        /// far plane sits at 48 m and the fog eats everything long
        /// before that, so the apron only exists to keep the sheet's
        /// north edge out of any legal camera — and to carry water
        /// under the lighthouse island, whose offshore band ends a
        /// metre inside it. Sized so the island at its far-plane-
        /// limited anchor still has covered sea past its last rock.
        /// </summary>
        internal const float ApronReach = 23f;

        // The low shore wave envelope ends here; it no longer denotes
        // a geometric step in the sand.
        internal const float InnerShelfReach = 2.6f;
        internal const float SeabedReach = 18f;
        private const float DeepSandSlope = 0.20f;
        private const float ShoreSlopeBlendReach = 2.2f;

        /// <summary>
        /// How far the mouth spill carries the river's surface past
        /// the waterline before it slides under the sea sheet, and
        /// how far under the sea's datum its far edge dives so the
        /// sea always covers its end.
        /// </summary>
        internal const float SpillReach = 5.5f;
        internal const float SpillDip = 0.03f;

        internal static Rect CreateMouthSpillRect(
            in CitySeacoastFrame frame)
        {
            return Rect.MinMaxRect(
                frame.ChannelXMin,
                frame.WaterlineZ,
                frame.ChannelXMax,
                frame.WaterlineZ + SpillReach);
        }

        internal static void CreateSheetRects(
            in CitySeacoastFrame frame,
            ICollection<Rect> destination)
        {
            Rect row = frame.SeaRowBounds;
            int count = Mathf.Max(
                1,
                Mathf.CeilToInt(row.width / MaximumSheetWidth));
            float width = row.width / count;
            for (int index = 0; index < count; index++)
            {
                float from = row.xMin + index * width;
                destination.Add(Rect.MinMaxRect(
                    from,
                    frame.WaterlineZ,
                    from + width,
                    row.yMax + ApronReach));
            }
        }

        internal static float SampleSeabedTop(
            CityLayout layout, CitySurfaceDescriptor surface, Vector2 point)
        {
            float distance = point.y - surface.WorldBounds.yMax;
            if (distance <= 0f)
                return CityTerrainSurfacePlan.SampleTop(layout, surface, point);
            var edge = new Vector2(point.x, surface.WorldBounds.yMax);
            float edgeTop = CityTerrainSurfacePlan.SampleTop(layout, surface, edge);
            const float tangentSample = 0.10f;
            float shoreSlope = Mathf.Max(0f,
                (CityTerrainSurfacePlan.SampleTop(layout, surface,
                    edge - Vector2.up * tangentSample) - edgeTop) / tangentSample);
            // Integral of a positive slope that starts at the beach's
            // tangent and eases toward 1:5. No risers or terminal ledge
            // can be exposed inside the water's depth-fade distance.
            float easedDistance = ShoreSlopeBlendReach *
                (1f - Mathf.Exp(-distance / ShoreSlopeBlendReach));
            return edgeTop - DeepSandSlope * distance -
                (shoreSlope - DeepSandSlope) * easedDistance;
        }

        internal static Vector3 SampleSeabedNormal(
            CityLayout layout, CitySurfaceDescriptor surface, Vector2 point)
        {
            const float offset = 0.10f;
            float west = SampleSeabedTop(layout, surface, point - Vector2.right * offset);
            float east = SampleSeabedTop(layout, surface, point + Vector2.right * offset);
            float south = SampleSeabedTop(layout, surface, point - Vector2.up * offset);
            float north = SampleSeabedTop(layout, surface, point + Vector2.up * offset);
            return new Vector3(west - east, offset * 2f, south - north).normalized;
        }
    }
}
