using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBalconySmokerSpace
    {
        CityWorld = 0,
        HomeLocal = 1
    }

    /// <summary>
    /// One passive smoker attached to an authored residential balcony dock.
    /// Position and Facing use the plan's declared coordinate space; the
    /// CityWorld aliases retain the original city pose after a Home transform.
    /// </summary>
    public readonly struct CityBalconySmokerDescriptor
    {
        internal CityBalconySmokerDescriptor(
            string stableId,
            Vector2Int lotCell,
            string balconySlotStableId,
            string archetypeDesignId,
            Vector3 position,
            Vector3 facing,
            Vector3 cityWorldPosition,
            Vector3 cityWorldFacing,
            int paletteVariant,
            float animationPhase01)
        {
            if (string.IsNullOrWhiteSpace(stableId) ||
                string.IsNullOrWhiteSpace(balconySlotStableId) ||
                string.IsNullOrWhiteSpace(archetypeDesignId))
            {
                throw new ArgumentException(
                    "A balcony smoker requires stable actor, slot and " +
                    "archetype IDs.");
            }

            Vector3 horizontal = Horizontal(facing);
            Vector3 cityHorizontal = Horizontal(cityWorldFacing);
            if (!IsFinite(position) ||
                !IsFinite(cityWorldPosition) ||
                !IsFinite(horizontal) ||
                !IsFinite(cityHorizontal) ||
                horizontal.sqrMagnitude < 0.0001f ||
                cityHorizontal.sqrMagnitude < 0.0001f ||
                !IsFinite(animationPhase01))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    "Balcony smoker poses and phase must be finite and " +
                    "facings must be horizontal and non-zero.");
            }

            StableId = stableId.Trim();
            LotCell = lotCell;
            BalconySlotStableId = balconySlotStableId.Trim();
            ArchetypeDesignId = archetypeDesignId.Trim();
            Position = position;
            Facing = horizontal.normalized;
            CityWorldPosition = cityWorldPosition;
            CityWorldFacing = cityHorizontal.normalized;
            int normalizedPalette = paletteVariant % 4;
            PaletteVariant = normalizedPalette < 0
                ? normalizedPalette + 4
                : normalizedPalette;
            AnimationPhase01 = Mathf.Repeat(animationPhase01, 1f);
        }

        public string StableId { get; }
        public Vector2Int LotCell { get; }
        public string BalconySlotStableId { get; }
        public string ArchetypeDesignId { get; }
        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public Vector3 CityWorldPosition { get; }
        public Vector3 CityWorldFacing { get; }
        public Vector3 WorldPosition => CityWorldPosition;
        public Vector3 WorldFacing => CityWorldFacing;
        public int PaletteVariant { get; }
        public float AnimationPhase01 { get; }

        internal CityBalconySmokerDescriptor ToHomeLocal(
            BuildingLot playerHome)
        {
            Vector3 localFacing = PlayerHomeBalconyGeometry
                .ToHomeLocalDirection(playerHome, CityWorldFacing);
            return new CityBalconySmokerDescriptor(
                StableId,
                LotCell,
                BalconySlotStableId,
                ArchetypeDesignId,
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    playerHome,
                    CityWorldPosition),
                localFacing,
                CityWorldPosition,
                CityWorldFacing,
                PaletteVariant,
                AnimationPhase01);
        }

        private static Vector3 Horizontal(Vector3 value)
        {
            return new Vector3(value.x, 0f, value.z);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Describes smoke-capable docks on ordinary residential prototypes and
    /// can still select a deliberately sparse fixed cast for bounded exterior
    /// contexts. The player home is excluded by the lot's IsOrdinaryBuilding
    /// contract before any hashing takes place.
    /// </summary>
    public sealed class CityBalconySmokerPlan
    {
        public const int MaximumSmokerCount = 2;
        public const int MaximumSmokersPerBuilding = 1;
        public const float SecondSmokerChance = 0.38f;
        public const float ReadableBalconyRowTolerance = 0.05f;

        private const uint SelectionSalt = 0x534D4B52u;
        private const uint SlotSalt = 0x534C4F54u;
        private const uint PaletteSalt = 0x50414C45u;
        private const uint PhaseSalt = 0x50484153u;
        private const uint CountSalt = 0x434F554Eu;

        private readonly ReadOnlyCollection<
            CityBalconySmokerDescriptor> smokers;

        private CityBalconySmokerPlan(
            int seed,
            CityBalconySmokerSpace space,
            IList<CityBalconySmokerDescriptor> source)
        {
            Seed = seed;
            Space = space;
            var copy = new List<CityBalconySmokerDescriptor>(source);
            copy.Sort(CompareDescriptors);
            smokers = new ReadOnlyCollection<
                CityBalconySmokerDescriptor>(copy);
            ValidateOrThrow();
        }

        public int Seed { get; }
        public CityBalconySmokerSpace Space { get; }
        public IReadOnlyList<CityBalconySmokerDescriptor> Smokers => smokers;
        public IReadOnlyList<CityBalconySmokerDescriptor> Descriptors =>
            smokers;
        public int Count => smokers.Count;
        public bool IsPresent => Count > 0;

        public static CityBalconySmokerPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return Create(layout, ResolveResidentialRegistry());
        }

        /// <summary>
        /// Returns one deterministic, lowest-row smoke-capable dock for every
        /// eligible ordinary Residential building. This is a candidate
        /// catalogue, not a population decision: the city runtime chooses a
        /// small local set around the moving player and releases it again by
        /// distance.
        /// </summary>
        public static IReadOnlyList<CityBalconySmokerDescriptor>
            CreateCandidates(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return CreateCandidates(
                layout,
                ResolveResidentialRegistry());
        }

        internal static CityBalconySmokerPlan Create(
            CityLayout layout,
            CityBuildingAssetRegistry residentialRegistry)
        {
            IReadOnlyList<CityBalconySmokerDescriptor> candidates =
                CreateCandidates(layout, residentialRegistry);
            int targetCount = ResolveTargetCount(
                layout.Seed,
                candidates.Count);
            var selected = new List<CityBalconySmokerDescriptor>(
                targetCount);
            for (int index = 0; index < targetCount; index++)
            {
                selected.Add(candidates[index]);
            }

            return new CityBalconySmokerPlan(
                layout.Seed,
                CityBalconySmokerSpace.CityWorld,
                selected);
        }

        internal static IReadOnlyList<CityBalconySmokerDescriptor>
            CreateCandidates(
                CityLayout layout,
                CityBuildingAssetRegistry residentialRegistry)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (residentialRegistry == null)
            {
                throw new ArgumentNullException(
                    nameof(residentialRegistry));
            }

            if (residentialRegistry.District !=
                CityDistrictKind.Residential)
            {
                throw new ArgumentException(
                    "Balcony smokers require the residential building " +
                    "registry.",
                    nameof(residentialRegistry));
            }

            IReadOnlyList<CityBuildingBalconySlot> slots =
                ResolveReadableBalconySlots(
                    residentialRegistry.BalconySlots);
            IReadOnlyList<string> archetypeDesignIds =
                CityBalconySmokerArchetypeCatalog.EligibleDesignIds;
            if (slots == null ||
                slots.Count == 0 ||
                archetypeDesignIds.Count == 0)
            {
                return Array.Empty<CityBalconySmokerDescriptor>();
            }

            var candidates = new List<Candidate>();
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (lot == null ||
                    !lot.IsOrdinaryBuilding ||
                    lot.District != CityDistrictKind.Residential)
                {
                    continue;
                }

                candidates.Add(CreateCandidate(
                    layout.Seed,
                    lot,
                    residentialRegistry,
                    slots,
                    archetypeDesignIds));
            }

            candidates.Sort(CompareCandidates);
            var descriptors = new List<CityBalconySmokerDescriptor>(
                candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                descriptors.Add(candidates[index].Descriptor);
            }

            return new ReadOnlyCollection<CityBalconySmokerDescriptor>(
                descriptors);
        }

        internal static CityBalconySmokerPlan CreateSelection(
            int seed,
            CityBalconySmokerSpace space,
            IList<CityBalconySmokerDescriptor> source)
        {
            return new CityBalconySmokerPlan(seed, space, source);
        }

        /// <summary>
        /// Keeps the exact city selection, removes actors whose source
        /// prototype is not fully imported into Home, then converts the
        /// surviving poses to Home-local coordinates.
        /// </summary>
        public CityBalconySmokerPlan TransformForHome(
            HomeExteriorContextPlan context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (Space != CityBalconySmokerSpace.CityWorld)
            {
                throw new InvalidOperationException(
                    "Only a city-world balcony smoker plan can be " +
                    "transformed for Home.");
            }

            if (context.Layout.Seed != Seed)
            {
                throw new ArgumentException(
                    "The Home exterior context must come from the same " +
                    "city layout as the smoker plan.",
                    nameof(context));
            }

            var nearbyByCell = new Dictionary<Vector2Int, BuildingLot>();
            for (int index = 0;
                 index < context.NearbyLots.Count;
                 index++)
            {
                BuildingLot lot = context.NearbyLots[index];
                if (lot != null && lot.IsOrdinaryBuilding)
                {
                    nearbyByCell[lot.Cell] = lot;
                }
            }

            var local = new List<CityBalconySmokerDescriptor>(Count);
            for (int index = 0; index < smokers.Count; index++)
            {
                CityBalconySmokerDescriptor descriptor = smokers[index];
                if (!nearbyByCell.TryGetValue(
                        descriptor.LotCell,
                        out BuildingLot lot) ||
                    CityBuildingPrototypeWorldBuilder
                        .ClassifyHomeExterior(context, lot) !=
                    CityBuildingExteriorFit.Full)
                {
                    continue;
                }

                local.Add(descriptor.ToHomeLocal(context.PlayerHome));
            }

            return new CityBalconySmokerPlan(
                Seed,
                CityBalconySmokerSpace.HomeLocal,
                local);
        }

        public static CityBalconySmokerPlan CreateForHome(
            HomeExteriorContextPlan context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return Create(context.Layout).TransformForHome(context);
        }

        private static Candidate CreateCandidate(
            int seed,
            BuildingLot lot,
            CityBuildingAssetRegistry registry,
            IReadOnlyList<CityBuildingBalconySlot> slots,
            IReadOnlyList<string> archetypeDesignIds)
        {
            string lotStableId =
                $"residential-{lot.Cell.x}-{lot.Cell.y}";
            uint baseHash = CityPedestrianStableHash.Combine(
                unchecked((uint)seed),
                CityPedestrianStableHash.String(lotStableId));
            uint slotHash = CityPedestrianStableHash.Combine(
                baseHash,
                SlotSalt);
            CityBuildingBalconySlot slot =
                slots[(int)(slotHash % (uint)slots.Count)];
            string archetypeDesignId = ResolveArchetypeDesignId(
                seed,
                lot.Cell,
                archetypeDesignIds);
            CityBuildingPrototypePose pose =
                CityBuildingPrototypePlacement.ResolveCityPose(
                    lot,
                    registry);
            Vector3 worldPosition = pose.TransformPoint(slot.LocalNpcDock);
            Vector3 worldFacing = pose.Rotation * slot.LocalOutward;
            string stableId =
                $"{lotStableId}/balcony-{slot.StableId}/smoker";
            int palette = (int)(CityPedestrianStableHash.Combine(
                baseHash,
                PaletteSalt) % 4u);
            float phase = CityPedestrianStableHash.ToUnitFloat(
                CityPedestrianStableHash.Combine(baseHash, PhaseSalt));
            var descriptor = new CityBalconySmokerDescriptor(
                stableId,
                lot.Cell,
                slot.StableId,
                archetypeDesignId,
                worldPosition,
                worldFacing,
                worldPosition,
                worldFacing,
                palette,
                phase);
            uint selection = CityPedestrianStableHash.Combine(
                baseHash,
                SelectionSalt);
            return new Candidate(descriptor, selection);
        }

        private static CityBuildingAssetRegistry
            ResolveResidentialRegistry()
        {
            CityBuildingAssetProvider provider =
                CityBuildingAssetProvider.LoadOrThrow();
            GameObject prefab = provider.GetPrefabOrThrow(
                CityDistrictKind.Residential);
            CityBuildingAssetRegistry registry =
                prefab.GetComponent<CityBuildingAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The residential building prototype lost its asset " +
                    "registry.");
            }

            registry.ValidateOrThrow();
            return registry;
        }

        private static IReadOnlyList<CityBuildingBalconySlot>
            ResolveReadableBalconySlots(
                IReadOnlyList<CityBuildingBalconySlot> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<CityBuildingBalconySlot>();
            }

            float lowestDock = float.PositiveInfinity;
            for (int index = 0; index < source.Count; index++)
            {
                lowestDock = Mathf.Min(
                    lowestDock,
                    source[index].LocalNpcDock.y);
            }

            var readable = new List<CityBuildingBalconySlot>(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                CityBuildingBalconySlot slot = source[index];
                if (slot.LocalNpcDock.y <=
                    lowestDock + ReadableBalconyRowTolerance)
                {
                    readable.Add(slot);
                }
            }

            return readable;
        }

        internal static string ResolveArchetypeDesignId(
            int seed,
            Vector2Int lotCell,
            IReadOnlyList<string> eligibleDesignIds)
        {
            if (eligibleDesignIds == null || eligibleDesignIds.Count == 0)
            {
                throw new ArgumentException(
                    "At least one eligible balcony-smoker archetype is " +
                    "required.",
                    nameof(eligibleDesignIds));
            }

            string lotStableId =
                $"residential-{lotCell.x}-{lotCell.y}";
            uint baseHash = CityPedestrianStableHash.Combine(
                unchecked((uint)seed),
                CityPedestrianStableHash.String(lotStableId));
            uint archetypeHash = CityPedestrianStableHash.Combine(
                baseHash,
                0x41524348u);
            return eligibleDesignIds[
                (int)(archetypeHash % (uint)eligibleDesignIds.Count)];
        }

        private static int ResolveTargetCount(int seed, int candidateCount)
        {
            if (candidateCount <= 0)
            {
                return 0;
            }

            if (candidateCount == 1)
            {
                return 1;
            }

            uint countHash = CityPedestrianStableHash.Combine(
                unchecked((uint)seed),
                CountSalt);
            return CityPedestrianStableHash.ToUnitFloat(countHash) <
                   SecondSmokerChance
                ? MaximumSmokerCount
                : 1;
        }

        private void ValidateOrThrow()
        {
            if (smokers.Count > MaximumSmokerCount)
            {
                throw new InvalidOperationException(
                    $"Balcony smokers exceed the {MaximumSmokerCount}-" +
                    "actor cap.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var lots = new HashSet<Vector2Int>();
            for (int index = 0; index < smokers.Count; index++)
            {
                CityBalconySmokerDescriptor descriptor = smokers[index];
                if (!ids.Add(descriptor.StableId) ||
                    !lots.Add(descriptor.LotCell))
                {
                    throw new InvalidOperationException(
                        $"Balcony smoker '{descriptor.StableId}' violates " +
                        "stable identity or the one-actor-per-building cap.");
                }
            }
        }

        private static int CompareDescriptors(
            CityBalconySmokerDescriptor left,
            CityBalconySmokerDescriptor right)
        {
            return string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal);
        }

        private static int CompareCandidates(Candidate left, Candidate right)
        {
            int hashOrder = left.SelectionHash.CompareTo(
                right.SelectionHash);
            return hashOrder != 0
                ? hashOrder
                : CompareDescriptors(left.Descriptor, right.Descriptor);
        }

        private readonly struct Candidate
        {
            public Candidate(
                CityBalconySmokerDescriptor descriptor,
                uint selectionHash)
            {
                Descriptor = descriptor;
                SelectionHash = selectionHash;
            }

            public CityBalconySmokerDescriptor Descriptor { get; }
            public uint SelectionHash { get; }
        }
    }
}
