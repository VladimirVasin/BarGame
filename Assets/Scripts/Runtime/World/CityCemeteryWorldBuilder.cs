using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Materialises a <see cref="CityCemeteryPlan"/>: parts batch into
    /// one combined oriented-box mesh per 48 m chunk and style (stone,
    /// gravel and soil batches carry world-planar UVs and a cemetery
    /// sheet), and each lamp descriptor becomes a cast-iron fixture
    /// with an emissive lens, a fog halo and a night-scaled point
    /// light that dies by day through the shared registries.
    /// </summary>
    public static class CityCemeteryWorldBuilder
    {
        public const string RootName = "Cemetery Landmarks";
        private const float SpatialChunkSize = 48f;

        // The flat palette. The textured styles are transcribed into
        // tools/build-cemetery-textures.py, which solves the albedo
        // compensation that keeps the textured product at this exact
        // brightness — edit them together.
        internal static readonly Color Gravel =
            new Color(0.30f, 0.28f, 0.23f);
        internal static readonly Color Iron =
            new Color(0.07f, 0.09f, 0.09f);
        internal static readonly Color GraniteDark =
            new Color(0.21f, 0.22f, 0.24f);
        internal static readonly Color MarbleLight =
            new Color(0.44f, 0.44f, 0.41f);
        internal static readonly Color WeatheredConcrete =
            new Color(0.30f, 0.31f, 0.28f);
        internal static readonly Color Soil =
            new Color(0.16f, 0.13f, 0.09f);
        internal static readonly Color TrunkDark =
            new Color(0.12f, 0.09f, 0.06f);
        internal static readonly Color TrunkBirch =
            new Color(0.55f, 0.55f, 0.50f);
        internal static readonly Color FoliageDark =
            new Color(0.09f, 0.16f, 0.10f);
        // One muted warm note per plot: wreaths and plastic flowers.
        internal static readonly Color Flowers =
            new Color(0.30f, 0.16f, 0.18f);
        // Bench planks: sun-bleached painted timber, repainted less
        // often than anyone would like.
        internal static readonly Color Timber =
            new Color(0.26f, 0.20f, 0.14f);

        // A cold gas-mantle green, dimmer than street practicals: the
        // cemetery should glow, not shine.
        internal static readonly Color LampLightColor =
            new Color(0.70f, 0.76f, 0.66f);
        internal const float LampNightIntensity = 42f;
        internal const float LampRange = 9.5f;
        private static readonly Color LampIronColor =
            new Color(0.060f, 0.070f, 0.070f);

        // The lodge's porch bulb is the one warm light in the whole
        // plot: a domestic tungsten note against the gas-mantle green,
        // short-ranged so it reads as the watchman's own doorway lamp
        // and not a second alley fixture. It has to actually throw
        // light on the man standing under it, so it sits at the
        // floodlight's end of the scale rather than the alley lamp's —
        // and unlike every other fixture in the city it never goes
        // out, only drops to a filament nobody notices by day.
        internal static readonly Color PorchLightColor =
            new Color(1.00f, 0.80f, 0.55f);
        internal const float PorchNightIntensity = 110f;
        internal const float PorchDayIntensity = 25f;
        internal const float PorchRange = 8.0f;

        public static GameObject Build(
            Transform parent,
            CityCemeteryPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            BuildPartBatches(root, plan.Parts, "Cemetery Chunk");

            for (int index = 0; index < plan.Lamps.Count; index++)
            {
                CityCemeteryLampDescriptor lamp = plan.Lamps[index];
                if (lamp.Kind == CityCemeteryLampKind.LodgePorch)
                {
                    BuildLodgePorchLamp(root, lamp);
                }
                else
                {
                    BuildAlleyLamp(root, lamp, index);
                }
            }

            return root.gameObject;
        }

        /// <summary>
        /// Batches a set of cemetery parts into one combined mesh per
        /// 48 m chunk and style, on the style's own surface sheet and
        /// collision rule. The graves the hero digs himself arrive one
        /// at a time long after the precinct was built, and they go
        /// through this same path so a raised stone is materially the
        /// same object as the rows around it.
        /// </summary>
        internal static void BuildPartBatches(
            Transform root,
            IReadOnlyList<CityCemeteryPartDescriptor> parts,
            string namePrefix)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }

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

            TryAppendImportedGraves(
                parts,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendImportedTrees(
                parts,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendImportedBushes(
                parts,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendImportedBenches(
                parts,
                miscProvider,
                importedBatches,
                importedIds);

            for (int index = 0; index < parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = parts[index];
                var key = new BatchKey(
                    Mathf.FloorToInt(
                        part.Center.x / SpatialChunkSize),
                    Mathf.FloorToInt(
                        part.Center.z / SpatialChunkSize),
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
                CityCemeterySurfaceKind? surface =
                    ResolveSurface(key.Style);
                float? uvTileSize = surface.HasValue
                    ? CityCemeterySurfaceAppearance
                        .GetRecipe(surface.Value).MetersPerTile
                    : (float?)null;
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"{namePrefix} {key.X} {key.Z} {key.Style}",
                        root,
                        batches[key],
                        ResolveColor(key.Style),
                    CityCemeteryRules.BlocksMovement(key.Style),
                    uvTileSize);
                if (surface.HasValue)
                {
                    CityCemeterySurfaceAppearance.ApplyCombined(
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
                CityCemeterySurfaceKind? surface =
                    ResolveSurface(key.Style);
                float? uvTileSize = surface.HasValue
                    ? CityCemeterySurfaceAppearance
                        .GetRecipe(surface.Value).MetersPerTile
                    : (float?)null;
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedMeshes(
                        $"Imported {namePrefix} {key.X} {key.Z} " +
                        $"{key.Style}",
                        root,
                        importedBatches[key],
                        ResolveColor(key.Style),
                        false,
                        uvTileSize,
                        RuntimeWorldUvMode.XZPlanar);
                if (surface.HasValue)
                {
                    CityCemeterySurfaceAppearance.ApplyCombined(
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
                        $"{namePrefix} Imported Collision {key.X} " +
                        $"{key.Z} {key.Style}",
                        root,
                        collisionBatches[key],
                        ResolveColor(key.Style),
                        true);
                proxy.GetComponent<Renderer>().enabled = false;
            }
        }

        private static void TryAppendImportedGraves(
            IReadOnlyList<CityCemeteryPartDescriptor> parts,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            if (provider == null)
            {
                return;
            }

            var groups = new Dictionary<
                int,
                List<CityCemeteryPartDescriptor>>();
            for (int index = 0; index < parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = parts[index];
                if (part.GraveOrdinal < 0 || !IsGravePart(part.Kind))
                {
                    continue;
                }

                if (!groups.TryGetValue(
                        part.GraveOrdinal,
                        out List<CityCemeteryPartDescriptor> group))
                {
                    group = new List<CityCemeteryPartDescriptor>();
                    groups.Add(part.GraveOrdinal, group);
                }

                group.Add(part);
            }

            var ordinals = new List<int>(groups.Keys);
            ordinals.Sort();
            for (int groupIndex = 0;
                 groupIndex < ordinals.Count;
                 groupIndex++)
            {
                List<CityCemeteryPartDescriptor> group =
                    groups[ordinals[groupIndex]];
                CityCemeteryPartDescriptor slab = default;
                bool hasSlab = false;
                for (int index = 0; index < group.Count; index++)
                {
                    if (group[index].Kind !=
                        CityCemeteryPartKind.GraveSlab)
                    {
                        continue;
                    }

                    slab = group[index];
                    hasSlab = true;
                    break;
                }

                if (!hasSlab)
                {
                    continue;
                }

                Vector3 ground = new Vector3(
                    slab.Center.x,
                    slab.Center.y - slab.Size.y * 0.5f,
                    slab.Center.z);
                int variant = (int)slab.Variant;
                var slabParts = SelectParts(
                    group,
                    CityCemeteryPartKind.GraveSlab);
                TryAppendAssembly(
                    provider,
                    CityMiscKind.CemeteryGraveSlab,
                    variant,
                    ground,
                    slab.Rotation,
                    slab.Style,
                    slabParts,
                    batches,
                    importedIds);

                List<CityCemeteryPartDescriptor> monuments =
                    SelectParts(
                        group,
                        CityCemeteryPartKind.GraveMonument);
                if (slab.Variant ==
                    CityCemeteryGraveVariant.OvergrownSlab)
                {
                    TryAppendAssembly(
                        provider,
                        CityMiscKind.CemeteryOvergrownMound,
                        0,
                        ground,
                        slab.Rotation,
                        CityCemeteryStyle.Soil,
                        monuments,
                        batches,
                        importedIds);
                }
                else if (!HasNonRigidMonumentTilt(
                             monuments,
                             slab.Rotation))
                {
                    TryAppendAssembly(
                        provider,
                        CityMiscKind.CemeteryGraveMonument,
                        variant,
                        ground,
                        slab.Rotation,
                        slab.Style,
                        monuments,
                        batches,
                        importedIds);
                }

                TryAppendAssembly(
                    provider,
                    CityMiscKind.CemeteryGraveEnclosure,
                    0,
                    ground,
                    slab.Rotation,
                    CityCemeteryStyle.Iron,
                    SelectParts(
                        group,
                        CityCemeteryPartKind.GraveEnclosure),
                    batches,
                    importedIds);
                TryAppendAssembly(
                    provider,
                    CityMiscKind.CemeteryGraveOffering,
                    0,
                    ground,
                    slab.Rotation,
                    CityCemeteryStyle.Flowers,
                    SelectParts(
                        group,
                        CityCemeteryPartKind.GraveOffering),
                    batches,
                    importedIds);
            }
        }

        private static void TryAppendImportedTrees(
            IReadOnlyList<CityCemeteryPartDescriptor> parts,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            var groups = new Dictionary<
                string,
                List<CityCemeteryPartDescriptor>>(
                    StringComparer.Ordinal);
            for (int index = 0; index < parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = parts[index];
                if (part.Kind != CityCemeteryPartKind.TreeTrunk &&
                    part.Kind != CityCemeteryPartKind.TreeCrown)
                {
                    continue;
                }

                string stem = GetTreeStem(part.StableId);
                if (!groups.TryGetValue(
                        stem,
                        out List<CityCemeteryPartDescriptor> group))
                {
                    group = new List<CityCemeteryPartDescriptor>();
                    groups.Add(stem, group);
                }

                group.Add(part);
            }

            var stems = new List<string>(groups.Keys);
            stems.Sort(StringComparer.Ordinal);
            for (int index = 0; index < stems.Count; index++)
            {
                List<CityCemeteryPartDescriptor> group = groups[stems[index]];
                CityCemeteryPartDescriptor trunk = default;
                bool found = false;
                for (int partIndex = 0;
                     partIndex < group.Count;
                     partIndex++)
                {
                    if (group[partIndex].Kind !=
                        CityCemeteryPartKind.TreeTrunk)
                    {
                        continue;
                    }

                    trunk = group[partIndex];
                    found = true;
                    break;
                }

                if (!found)
                {
                    continue;
                }

                Vector3 ground = new Vector3(
                    trunk.Center.x,
                    trunk.Center.y - trunk.Size.y * 0.5f,
                    trunk.Center.z);
                int variant = trunk.Style ==
                    CityCemeteryStyle.TrunkBirch
                        ? 0
                        : 1;
                TryAppendAssembly(
                    provider,
                    CityMiscKind.CemeteryTree,
                    variant,
                    ground,
                    Quaternion.identity,
                    trunk.Style,
                    group,
                    batches,
                    importedIds);
            }
        }

        private static void TryAppendImportedBushes(
            IReadOnlyList<CityCemeteryPartDescriptor> parts,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            for (int index = 0; index < parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = parts[index];
                if (part.Kind != CityCemeteryPartKind.Bush)
                {
                    continue;
                }

                Vector3 ground = new Vector3(
                    part.Center.x,
                    part.Center.y - part.Size.y * 0.5f,
                    part.Center.z);
                TryAppendAssembly(
                    provider,
                    CityMiscKind.CemeteryBush,
                    0,
                    ground,
                    part.Rotation,
                    part.Style,
                    new List<CityCemeteryPartDescriptor> { part },
                    batches,
                    importedIds);
            }
        }

        private static void TryAppendImportedBenches(
            IReadOnlyList<CityCemeteryPartDescriptor> parts,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            var groups = new Dictionary<
                string,
                List<CityCemeteryPartDescriptor>>(
                    StringComparer.Ordinal);
            for (int index = 0; index < parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = parts[index];
                if (part.Kind != CityCemeteryPartKind.Bench)
                {
                    continue;
                }

                string stem = GetBenchStem(part.StableId);
                if (!groups.TryGetValue(
                        stem,
                        out List<CityCemeteryPartDescriptor> group))
                {
                    group = new List<CityCemeteryPartDescriptor>();
                    groups.Add(stem, group);
                }

                group.Add(part);
            }

            var stems = new List<string>(groups.Keys);
            stems.Sort(StringComparer.Ordinal);
            for (int index = 0; index < stems.Count; index++)
            {
                List<CityCemeteryPartDescriptor> group = groups[stems[index]];
                CityCemeteryPartDescriptor seat = default;
                bool found = false;
                float groundY = float.PositiveInfinity;
                for (int partIndex = 0;
                     partIndex < group.Count;
                     partIndex++)
                {
                    CityCemeteryPartDescriptor part = group[partIndex];
                    groundY = Mathf.Min(
                        groundY,
                        part.Center.y - part.Size.y * 0.5f);
                    if (part.StableId.EndsWith(
                            "-seat",
                            StringComparison.Ordinal))
                    {
                        seat = part;
                        found = true;
                    }
                }

                if (!found)
                {
                    continue;
                }

                TryAppendAssembly(
                    provider,
                    CityMiscKind.CemeteryBench,
                    0,
                    new Vector3(
                        seat.Center.x,
                        groundY,
                        seat.Center.z),
                    seat.Rotation,
                    CityCemeteryStyle.Timber,
                    group,
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
            CityCemeteryStyle sourceStyle,
            IReadOnlyList<CityCemeteryPartDescriptor> sourceParts,
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
                CityCemeteryStyle style = ResolveImportedStyle(
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
                    rotation));
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

        private static CityCemeteryStyle ResolveImportedStyle(
            CityMiscKind kind,
            CityMiscMeshRole role,
            CityCemeteryStyle sourceStyle)
        {
            switch (kind)
            {
                case CityMiscKind.CemeteryOvergrownMound:
                    return role == CityMiscMeshRole.Residential
                        ? CityCemeteryStyle.Flowers
                        : CityCemeteryStyle.Soil;
                case CityMiscKind.CemeteryGraveEnclosure:
                    return CityCemeteryStyle.Iron;
                case CityMiscKind.CemeteryGraveOffering:
                    return CityCemeteryStyle.Flowers;
                case CityMiscKind.CemeteryTree:
                    return role == CityMiscMeshRole.Foliage
                        ? CityCemeteryStyle.FoliageDark
                        : sourceStyle;
                case CityMiscKind.CemeteryBush:
                    return CityCemeteryStyle.FoliageDark;
                case CityMiscKind.CemeteryBench:
                    return role == CityMiscMeshRole.Timber
                        ? CityCemeteryStyle.Timber
                        : CityCemeteryStyle.Iron;
                default:
                    return sourceStyle;
            }
        }

        private static bool IsGravePart(CityCemeteryPartKind kind)
        {
            return kind == CityCemeteryPartKind.GraveSlab ||
                   kind == CityCemeteryPartKind.GraveMonument ||
                   kind == CityCemeteryPartKind.GraveEnclosure ||
                   kind == CityCemeteryPartKind.GraveOffering;
        }

        private static List<CityCemeteryPartDescriptor> SelectParts(
            IReadOnlyList<CityCemeteryPartDescriptor> parts,
            CityCemeteryPartKind kind)
        {
            var selected = new List<CityCemeteryPartDescriptor>();
            for (int index = 0; index < parts.Count; index++)
            {
                if (parts[index].Kind == kind)
                {
                    selected.Add(parts[index]);
                }
            }

            return selected;
        }

        private static bool HasNonRigidMonumentTilt(
            IReadOnlyList<CityCemeteryPartDescriptor> monuments,
            Quaternion graveYaw)
        {
            for (int index = 0; index < monuments.Count; index++)
            {
                CityCemeteryPartDescriptor part = monuments[index];
                if (part.StableId.EndsWith(
                        "-slant",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (Quaternion.Angle(part.Rotation, graveYaw) > 0.05f)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetTreeStem(string stableId)
        {
            int suffix = stableId.IndexOf(
                "-trunk",
                StringComparison.Ordinal);
            if (suffix < 0)
            {
                suffix = stableId.IndexOf(
                    "-crown-",
                    StringComparison.Ordinal);
            }

            return suffix > 0
                ? stableId.Substring(0, suffix)
                : stableId;
        }

        private static string GetBenchStem(string stableId)
        {
            string[] suffixes = { "-seat", "-back", "-leg-" };
            for (int index = 0; index < suffixes.Length; index++)
            {
                int suffix = stableId.IndexOf(
                    suffixes[index],
                    StringComparison.Ordinal);
                if (suffix > 0)
                {
                    return stableId.Substring(0, suffix);
                }
            }

            return stableId;
        }

        private static void AppendBox(
            IDictionary<BatchKey, List<RuntimeOrientedBox>> batches,
            BatchKey key,
            CityCemeteryPartDescriptor part)
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

        /// <summary>
        /// One alley lamp: iron base, pole, a caged head with an
        /// emissive mantle lens, and the night-scaled point light.
        /// The pole carries the only collider; the head is overhead.
        /// </summary>
        private static void BuildAlleyLamp(
            Transform parent,
            CityCemeteryLampDescriptor descriptor,
            int index)
        {
            Transform assembly = new GameObject(
                $"Cemetery Lamp {index}").transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                descriptor.GroundPosition,
                Quaternion.Euler(0f, descriptor.YawDegrees, 0f));

            RuntimePrimitiveFactory.CreateBox(
                "Lamp Base",
                assembly,
                new Vector3(0f, 0.225f, 0f),
                new Vector3(0.26f, 0.45f, 0.26f),
                LampIronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Pole",
                assembly,
                new Vector3(0f, 1.625f, 0f),
                new Vector3(0.11f, 2.35f, 0.11f),
                LampIronColor,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Head",
                assembly,
                new Vector3(0f, 2.99f, 0f),
                new Vector3(0.30f, 0.38f, 0.30f),
                LampIronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Cap",
                assembly,
                new Vector3(0f, 3.22f, 0f),
                new Vector3(0.38f, 0.08f, 0.38f),
                LampIronColor,
                false);

            Color glow = MultiplyRgb(LampLightColor, 4.6f, 1f);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Lamp Mantle",
                assembly,
                new Vector3(0f, 2.97f, 0f),
                new Vector3(0.20f, 0.26f, 0.20f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                glow);

            GameObject emitter = new GameObject("Cemetery Lamp Light");
            emitter.transform.SetParent(assembly, false);
            emitter.transform.localPosition = new Vector3(0f, 2.97f, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LampLightColor;
            light.intensity = LampNightIntensity;
            light.range = LampRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            GameObject haloObject = new GameObject(
                "Cemetery Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.55f,
                1.55f,
                MultiplyRgb(LampLightColor, 4.2f, 0.18f),
                MultiplyRgb(LampLightColor, 2.1f, 0.05f));
            CityNightSiteLightRegistry.Register(
                light,
                LampNightIntensity,
                halo);
        }

        /// <summary>
        /// The gate lodge's porch bulb: a short stem into the eave, a
        /// tin hood and a bare emissive bulb hanging beside the door at
        /// 2 m, with the point light that puts the watchman's face and
        /// hands in the light instead of leaving him a silhouette in
        /// his own doorway. Burns around the clock — dimmed by day,
        /// full at night — so he is lit whenever anyone comes by.
        /// </summary>
        private static void BuildLodgePorchLamp(
            Transform parent,
            CityCemeteryLampDescriptor descriptor)
        {
            Transform assembly = new GameObject(
                "Cemetery Lodge Porch Lamp").transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                descriptor.GroundPosition,
                Quaternion.Euler(0f, descriptor.YawDegrees, 0f));

            RuntimePrimitiveFactory.CreateBox(
                "Porch Lamp Stem",
                assembly,
                new Vector3(0f, 2.32f, 0f),
                new Vector3(0.05f, 0.24f, 0.05f),
                LampIronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Porch Lamp Hood",
                assembly,
                new Vector3(0f, 2.15f, 0f),
                new Vector3(0.24f, 0.07f, 0.24f),
                LampIronColor,
                false);

            Color glow = MultiplyRgb(PorchLightColor, 4.6f, 1f);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Porch Lamp Bulb",
                assembly,
                new Vector3(0f, 2.01f, 0f),
                new Vector3(0.14f, 0.22f, 0.14f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                glow);

            GameObject emitter = new GameObject("Porch Lamp Light");
            emitter.transform.SetParent(assembly, false);
            emitter.transform.localPosition = new Vector3(0f, 1.99f, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = PorchLightColor;
            light.intensity = PorchNightIntensity;
            light.range = PorchRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            GameObject haloObject = new GameObject("Porch Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.40f,
                1.10f,
                MultiplyRgb(PorchLightColor, 4.2f, 0.18f),
                MultiplyRgb(PorchLightColor, 2.1f, 0.05f));
            CityNightSiteLightRegistry.Register(
                light,
                PorchNightIntensity,
                PorchDayIntensity,
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

        private static CityCemeterySurfaceKind? ResolveSurface(
            CityCemeteryStyle style)
        {
            switch (style)
            {
                case CityCemeteryStyle.Gravel:
                    return CityCemeterySurfaceKind.Gravel;
                case CityCemeteryStyle.Soil:
                    return CityCemeterySurfaceKind.Soil;
                case CityCemeteryStyle.GraniteDark:
                case CityCemeteryStyle.MarbleLight:
                    return CityCemeterySurfaceKind.Granite;
                case CityCemeteryStyle.WeatheredConcrete:
                    return CityCemeterySurfaceKind.Stone;
                default:
                    // Iron rails, trunks, crowns, flowers and bench
                    // planks stay flat colour: their members are too
                    // thin for a sheet to read through the PS1
                    // composite.
                    return null;
            }
        }

        private static Color ResolveColor(CityCemeteryStyle style)
        {
            switch (style)
            {
                case CityCemeteryStyle.Gravel:
                    return Gravel;
                case CityCemeteryStyle.Iron:
                    return Iron;
                case CityCemeteryStyle.GraniteDark:
                    return GraniteDark;
                case CityCemeteryStyle.MarbleLight:
                    return MarbleLight;
                case CityCemeteryStyle.WeatheredConcrete:
                    return WeatheredConcrete;
                case CityCemeteryStyle.Soil:
                    return Soil;
                case CityCemeteryStyle.TrunkDark:
                    return TrunkDark;
                case CityCemeteryStyle.TrunkBirch:
                    return TrunkBirch;
                case CityCemeteryStyle.FoliageDark:
                    return FoliageDark;
                case CityCemeteryStyle.Flowers:
                    return Flowers;
                case CityCemeteryStyle.Timber:
                    return Timber;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(int x, int z, CityCemeteryStyle style)
            {
                X = x;
                Z = z;
                Style = style;
            }

            public int X { get; }
            public int Z { get; }
            public CityCemeteryStyle Style { get; }

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
                return z != 0
                    ? z
                    : left.Style.CompareTo(right.Style);
            }
        }
    }
}
