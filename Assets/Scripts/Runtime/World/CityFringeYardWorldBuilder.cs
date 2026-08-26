using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Materializes the planned fringe as a small set of 48-metre batches.
    /// Tracks, drains and cables stay visual; walls, large stock, sheds and
    /// berms carry the only new collision. Four emissive lenses expose poses
    /// to the existing night pool; no Light component or interaction is built.
    /// </summary>
    internal static class CityFringeYardWorldBuilder
    {
        private const float SpatialChunkSize = 48f;
        internal const float PracticalLensForwardOffset = 0.24f;

        private static readonly Color ServiceGround =
            new Color(0.30f, 0.275f, 0.215f, 1f);
        private static readonly Color Drainage =
            new Color(0.115f, 0.14f, 0.13f, 1f);
        private static readonly Color OldMasonry =
            new Color(0.335f, 0.35f, 0.325f, 1f);
        private static readonly Color Concrete =
            new Color(0.285f, 0.315f, 0.305f, 1f);
        private static readonly Color Gabion =
            new Color(0.30f, 0.325f, 0.295f, 1f);
        private static readonly Color Iron =
            new Color(0.135f, 0.17f, 0.165f, 1f);
        private static readonly Color UtilityPaint =
            new Color(0.22f, 0.305f, 0.31f, 1f);
        private static readonly Color Rock =
            new Color(0.235f, 0.26f, 0.235f, 1f);

        internal static CityFringeYardWorldResult Build(
            Transform parent,
            CityFringeYardPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsEnabled)
            {
                return null;
            }

            var root = new GameObject("Authored Fringe Yards");
            root.transform.SetParent(parent, false);
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
            for (int yardIndex = 0;
                 yardIndex < plan.Yards.Count;
                 yardIndex++)
            {
                CityFringeYardDescriptor yard = plan.Yards[yardIndex];
                TryAppendImportedYardParts(
                    yard,
                    miscProvider,
                    importedBatches,
                    importedIds);
                for (int partIndex = 0;
                     partIndex < yard.Parts.Count;
                     partIndex++)
                {
                    CityFringeYardPartDescriptor part =
                        yard.Parts[partIndex];
                    var key = new BatchKey(
                        Mathf.FloorToInt(part.Center.x / SpatialChunkSize),
                        Mathf.FloorToInt(part.Center.z / SpatialChunkSize),
                        part.Style,
                        part.BlocksMovement);
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
            }

            var keys = new List<BatchKey>(batches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                Color color = ResolveColor(key.Style);
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"Fringe Chunk {key.X} {key.Z} {key.Style} " +
                        $"{(key.BlocksMovement ? "Solid" : "Visual")}",
                        root.transform,
                        batches[key],
                        color,
                        key.BlocksMovement,
                        ResolveTileSize(key.Style),
                        ResolveUvMode(key.Style));
                Renderer renderer = chunk.GetComponent<Renderer>();
                ApplyAppearance(renderer, key.Style, color);
                if (!key.BlocksMovement &&
                    (key.Style == CityFringeYardStyle.ServiceGround ||
                     key.Style == CityFringeYardStyle.ServiceTrack ||
                     key.Style == CityFringeYardStyle.Drainage ||
                     key.Style == CityFringeYardStyle.Iron))
                {
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            keys = new List<BatchKey>(importedBatches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                Color color = ResolveColor(key.Style);
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedMeshes(
                        $"Imported Fringe Chunk {key.X} {key.Z} " +
                        $"{key.Style} " +
                        $"{(key.BlocksMovement ? "Solid" : "Visual")}",
                        root.transform,
                        importedBatches[key],
                        color,
                        false,
                        ResolveTileSize(key.Style),
                        ResolveUvMode(key.Style));
                Renderer renderer = chunk.GetComponent<Renderer>();
                ApplyAppearance(renderer, key.Style, color);
                if (!key.BlocksMovement &&
                    (key.Style == CityFringeYardStyle.ServiceGround ||
                     key.Style == CityFringeYardStyle.ServiceTrack ||
                     key.Style == CityFringeYardStyle.Drainage ||
                     key.Style == CityFringeYardStyle.Iron))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            keys = new List<BatchKey>(collisionBatches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                GameObject proxy =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"Fringe Imported Collision {key.X} " +
                        $"{key.Z} {key.Style}",
                        root.transform,
                        collisionBatches[key],
                        ResolveColor(key.Style),
                        true);
                proxy.GetComponent<Renderer>().enabled = false;
            }

            IList<CityFringePracticalAnchor> practicalAnchors =
                BuildPracticalAnchors(root.transform, plan.Practicals);
            return new CityFringeYardWorldResult(root, practicalAnchors);
        }

        private static void TryAppendImportedYardParts(
            CityFringeYardDescriptor yard,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            TryAppendImportedUtilityPoles(
                yard,
                provider,
                batches,
                importedIds);
            TryAppendImportedSingleParts(
                yard,
                CityFringeYardPartKind.RepairStack,
                CityMiscKind.FringeRepairStock,
                ResolveRepairVariant,
                provider,
                batches,
                importedIds);
            TryAppendImportedSingleParts(
                yard,
                CityFringeYardPartKind.PipeStock,
                CityMiscKind.FringePipeStock,
                ResolvePipeVariant,
                provider,
                batches,
                importedIds);
            TryAppendImportedUtilitySheds(
                yard,
                provider,
                batches,
                importedIds);
            TryAppendImportedFloodGauge(
                yard,
                provider,
                batches,
                importedIds);
        }

        private static void TryAppendImportedUtilityPoles(
            CityFringeYardDescriptor yard,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor pole = yard.Parts[index];
                if (pole.Kind != CityFringeYardPartKind.UtilityPole)
                {
                    continue;
                }

                string armId = pole.StableId.Replace(
                    "-utility-pole-",
                    "-utility-arm-");
                CityFringeYardPartDescriptor arm = default;
                bool found = false;
                for (int armIndex = 0;
                     armIndex < yard.Parts.Count;
                     armIndex++)
                {
                    if (!string.Equals(
                            yard.Parts[armIndex].StableId,
                            armId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    arm = yard.Parts[armIndex];
                    found = true;
                    break;
                }

                if (!found)
                {
                    continue;
                }

                TryAppendAssembly(
                    provider,
                    CityMiscKind.FringeUtilityPole,
                    0,
                    new Vector3(
                        pole.Center.x,
                        pole.Center.y - pole.Size.y * 0.5f,
                        pole.Center.z),
                    arm.Rotation,
                    CityFringeYardStyle.Iron,
                    new[] { pole, arm },
                    Vector3.one,
                    batches,
                    importedIds);
            }
        }

        private static void TryAppendImportedSingleParts(
            CityFringeYardDescriptor yard,
            CityFringeYardPartKind sourceKind,
            CityMiscKind miscKind,
            Func<CityFringeYardPartDescriptor, int> resolveVariant,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor source = yard.Parts[index];
                if (source.Kind != sourceKind)
                {
                    continue;
                }

                int variant = resolveVariant(source);
                if (!TryGetImportedParts(
                        provider,
                        miscKind,
                        variant,
                        out _))
                {
                    continue;
                }

                Vector3 scale = miscKind ==
                    CityMiscKind.FringePipeStock
                        ? ScalePipeStock(variant, source.Size)
                        : Vector3.one;
                TryAppendAssembly(
                    provider,
                    miscKind,
                    variant,
                    new Vector3(
                        source.Center.x,
                        source.Center.y - source.Size.y * 0.5f,
                        source.Center.z),
                    source.Rotation,
                    source.Style,
                    new[] { source },
                    scale,
                    batches,
                    importedIds);
            }
        }

        private static void TryAppendImportedUtilitySheds(
            CityFringeYardDescriptor yard,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor shed = yard.Parts[index];
                if (shed.Kind != CityFringeYardPartKind.UtilityShed)
                {
                    continue;
                }

                string suffix = GetTrailingOrdinal(shed.StableId);
                CityFringeYardPartDescriptor door = default;
                bool found = false;
                for (int doorIndex = 0;
                     doorIndex < yard.Parts.Count;
                     doorIndex++)
                {
                    CityFringeYardPartDescriptor candidate =
                        yard.Parts[doorIndex];
                    if (candidate.Kind ==
                            CityFringeYardPartKind.UtilityDoor &&
                        candidate.StableId.EndsWith(
                            suffix,
                            StringComparison.Ordinal))
                    {
                        door = candidate;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    continue;
                }

                TryAppendAssembly(
                    provider,
                    CityMiscKind.FringeUtilityShedShell,
                    0,
                    new Vector3(
                        shed.Center.x,
                        shed.Center.y - shed.Size.y * 0.5f + 0.16f,
                        shed.Center.z),
                    shed.Rotation,
                    shed.Style,
                    new[] { shed, door },
                    Vector3.one,
                    batches,
                    importedIds);
            }
        }

        private static void TryAppendImportedFloodGauge(
            CityFringeYardDescriptor yard,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            var gaugeParts = new List<CityFringeYardPartDescriptor>(4);
            CityFringeYardPartDescriptor pole = default;
            CityFringeYardPartDescriptor cross = default;
            CityFringeYardPartDescriptor housing = default;
            bool hasPole = false;
            bool hasCross = false;
            bool hasHousing = false;
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor part = yard.Parts[index];
                if (part.Kind ==
                        CityFringeYardPartKind.PracticalHousing &&
                    string.Equals(
                        part.StableId,
                        $"{yard.AreaId}-landmark-practical-housing",
                        StringComparison.Ordinal))
                {
                    housing = part;
                    hasHousing = true;
                    continue;
                }

                if (part.Kind != CityFringeYardPartKind.FloodGauge)
                {
                    continue;
                }

                gaugeParts.Add(part);
                if (part.StableId.EndsWith(
                        "-flood-gauge",
                        StringComparison.Ordinal))
                {
                    pole = part;
                    hasPole = true;
                }
                else if (part.StableId.EndsWith(
                             "-wheel-b",
                             StringComparison.Ordinal))
                {
                    cross = part;
                    hasCross = true;
                }
            }

            if (!hasPole || !hasCross || !hasHousing ||
                gaugeParts.Count != 3)
            {
                return;
            }

            gaugeParts.Add(housing);

            float side = Vector3.Dot(
                cross.Rotation * Vector3.forward,
                pole.Rotation * Vector3.right);
            int variant = side >= 0f ? 0 : 1;
            TryAppendAssembly(
                provider,
                CityMiscKind.FringeFloodGaugeShell,
                variant,
                new Vector3(
                    pole.Center.x,
                    pole.Center.y - pole.Size.y * 0.5f,
                    pole.Center.z),
                pole.Rotation,
                pole.Style,
                gaugeParts,
                Vector3.one,
                batches,
                importedIds);
        }

        private static bool TryAppendAssembly(
            CityMiscAssetProvider provider,
            CityMiscKind kind,
            int variant,
            Vector3 origin,
            Quaternion rotation,
            CityFringeYardStyle sourceStyle,
            IReadOnlyList<CityFringeYardPartDescriptor> sourceParts,
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

            bool blocksMovement = false;
            for (int index = 0; index < sourceParts.Count; index++)
            {
                blocksMovement |= sourceParts[index].BlocksMovement;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                CityMiscMeshPart part = parts[index];
                CityFringeYardStyle style = ResolveImportedStyle(
                    kind,
                    part.Role,
                    sourceStyle);
                var key = new BatchKey(
                    Mathf.FloorToInt(origin.x / SpatialChunkSize),
                    Mathf.FloorToInt(origin.z / SpatialChunkSize),
                    style,
                    blocksMovement);
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

        private static CityFringeYardStyle ResolveImportedStyle(
            CityMiscKind kind,
            CityMiscMeshRole role,
            CityFringeYardStyle sourceStyle)
        {
            switch (kind)
            {
                case CityMiscKind.FringeUtilityPole:
                    return CityFringeYardStyle.Iron;
                case CityMiscKind.FringeRepairStock:
                case CityMiscKind.FringePipeStock:
                    return role == CityMiscMeshRole.Fixture
                        ? CityFringeYardStyle.Iron
                        : CityFringeYardStyle.Concrete;
                case CityMiscKind.FringeUtilityShedShell:
                    return role == CityMiscMeshRole.Fixture
                        ? CityFringeYardStyle.Iron
                        : CityFringeYardStyle.UtilityPaint;
                case CityMiscKind.FringeFloodGaugeShell:
                    return role == CityMiscMeshRole.Fixture
                        ? CityFringeYardStyle.Iron
                        : CityFringeYardStyle.UtilityPaint;
                default:
                    return sourceStyle;
            }
        }

        private static int ResolveRepairVariant(
            CityFringeYardPartDescriptor part)
        {
            string suffix = GetTrailingOrdinal(part.StableId);
            return int.TryParse(suffix, out int value)
                ? Mathf.Clamp(value, 0, 2)
                : 0;
        }

        private static int ResolvePipeVariant(
            CityFringeYardPartDescriptor part)
        {
            return part.Style == CityFringeYardStyle.Iron ? 1 : 0;
        }

        private static string GetTrailingOrdinal(string stableId)
        {
            int separator = stableId.LastIndexOf('-');
            return separator >= 0 && separator + 1 < stableId.Length
                ? stableId.Substring(separator + 1)
                : string.Empty;
        }

        private static Vector3 ScalePipeStock(
            int variant,
            Vector3 target)
        {
            Vector3 source = variant == 0
                ? new Vector3(0.34f, 0.34f, 5.75f)
                : new Vector3(0.58f, 0.38f, 7.40f);
            return new Vector3(
                target.x / source.x,
                target.y / source.y,
                target.z / source.z);
        }

        private static void AppendBox(
            IDictionary<BatchKey, List<RuntimeOrientedBox>> batches,
            BatchKey key,
            CityFringeYardPartDescriptor part)
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

        private static IList<CityFringePracticalAnchor>
            BuildPracticalAnchors(
                Transform parent,
                IReadOnlyList<CityFringeYardPracticalDescriptor> practicals)
        {
            var result = new List<CityFringePracticalAnchor>(
                practicals.Count);
            for (int index = 0; index < practicals.Count; index++)
            {
                CityFringeYardPracticalDescriptor descriptor =
                    practicals[index];
                Transform anchor = new GameObject(
                    $"Fringe Practical {descriptor.Kind}").transform;
                anchor.SetParent(parent, false);
                anchor.localPosition = descriptor.Position;
                anchor.localRotation = Quaternion.LookRotation(
                    descriptor.Forward,
                    Vector3.up);
                GameObject lens = RuntimePrimitiveFactory.CreateBox(
                    "Practical Emissive Lens",
                    anchor,
                    Vector3.forward * PracticalLensForwardOffset,
                    descriptor.LensSize,
                    descriptor.LitColor,
                    CityNightResources.EmissiveMaterial,
                    false);
                CityNightGlowRegistry.Register(
                    lens.GetComponent<Renderer>(),
                    descriptor.LitColor);
                result.Add(new CityFringePracticalAnchor(
                    descriptor.YardKind,
                    anchor));
            }

            return result;
        }

        private static void ApplyAppearance(
            Renderer renderer,
            CityFringeYardStyle style,
            Color color)
        {
            switch (style)
            {
                case CityFringeYardStyle.ServiceGround:
                    CityExteriorAppearance.ApplyGroundSurface(
                        renderer,
                        color);
                    break;
                case CityFringeYardStyle.ServiceTrack:
                    CityFringeYardSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityFringeYardSurfaceKind.ServiceTrack,
                        color);
                    break;
                case CityFringeYardStyle.Drainage:
                    CityRiverSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityRiverSurfaceKind.Bed,
                        color);
                    break;
                case CityFringeYardStyle.OldMasonry:
                    CityFringeYardSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityFringeYardSurfaceKind.Masonry,
                        color);
                    break;
                case CityFringeYardStyle.Concrete:
                    CityFringeYardSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityFringeYardSurfaceKind.Concrete,
                        color);
                    break;
                case CityFringeYardStyle.Gabion:
                    CityMountainSurfaceAppearance.ApplyCombined(
                        renderer,
                        color);
                    break;
                case CityFringeYardStyle.Iron:
                case CityFringeYardStyle.UtilityPaint:
                    CityRiverSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityRiverSurfaceKind.Iron,
                        color);
                    break;
                case CityFringeYardStyle.Rock:
                    CityMountainSurfaceAppearance.ApplyCombined(
                        renderer,
                        color);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }

        private static float ResolveTileSize(CityFringeYardStyle style)
        {
            switch (style)
            {
                case CityFringeYardStyle.ServiceGround:
                    return CityExteriorAppearance.GroundTextureTileSize;
                case CityFringeYardStyle.ServiceTrack:
                    return CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.ServiceTrack).MetersPerTile;
                case CityFringeYardStyle.Drainage:
                    return CityRiverSurfaceAppearance
                        .GetRecipe(CityRiverSurfaceKind.Bed).MetersPerTile;
                case CityFringeYardStyle.OldMasonry:
                    return CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.Masonry).MetersPerTile;
                case CityFringeYardStyle.Concrete:
                    return CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.Concrete).MetersPerTile;
                case CityFringeYardStyle.Gabion:
                    return CityMountainSurfaceAppearance.MetersPerTile;
                case CityFringeYardStyle.Iron:
                case CityFringeYardStyle.UtilityPaint:
                    return CityRiverSurfaceAppearance
                        .GetRecipe(CityRiverSurfaceKind.Iron).MetersPerTile;
                case CityFringeYardStyle.Rock:
                    return CityMountainSurfaceAppearance.MetersPerTile;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }

        private static RuntimeWorldUvMode ResolveUvMode(
            CityFringeYardStyle style)
        {
            return style == CityFringeYardStyle.ServiceGround ||
                   style == CityFringeYardStyle.ServiceTrack ||
                   style == CityFringeYardStyle.Drainage
                ? RuntimeWorldUvMode.XZPlanar
                : RuntimeWorldUvMode.BoxProjected;
        }

        private static Color ResolveColor(CityFringeYardStyle style)
        {
            switch (style)
            {
                case CityFringeYardStyle.ServiceGround:
                case CityFringeYardStyle.ServiceTrack:
                    return ServiceGround;
                case CityFringeYardStyle.Drainage:
                    return Drainage;
                case CityFringeYardStyle.OldMasonry:
                    return OldMasonry;
                case CityFringeYardStyle.Concrete:
                    return Concrete;
                case CityFringeYardStyle.Gabion:
                    return Gabion;
                case CityFringeYardStyle.Iron:
                    return Iron;
                case CityFringeYardStyle.UtilityPaint:
                    return UtilityPaint;
                case CityFringeYardStyle.Rock:
                    return Rock;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(
                int x,
                int z,
                CityFringeYardStyle style,
                bool blocksMovement)
            {
                X = x;
                Z = z;
                Style = style;
                BlocksMovement = blocksMovement;
            }

            public int X { get; }
            public int Z { get; }
            public CityFringeYardStyle Style { get; }
            public bool BlocksMovement { get; }

            public bool Equals(BatchKey other)
            {
                return X == other.X &&
                       Z == other.Z &&
                       Style == other.Style &&
                       BlocksMovement == other.BlocksMovement;
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
                    hash = (hash * 397) ^ (int)Style;
                    return (hash * 397) ^ BlocksMovement.GetHashCode();
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
                if (z != 0)
                {
                    return z;
                }

                int style = left.Style.CompareTo(right.Style);
                return style != 0
                    ? style
                    : left.BlocksMovement.CompareTo(right.BlocksMovement);
            }
        }
    }
}
