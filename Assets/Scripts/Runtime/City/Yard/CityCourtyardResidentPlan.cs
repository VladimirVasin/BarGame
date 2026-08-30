using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityCourtyardResidentActivity
    {
        NardiPlayer = 0,
        BicycleRepair = 1,
        ChairRepair = 2,
        Sweeping = 3,
        Masonry = 4,
        WinchService = 5,
        FloodMaintenance = 6,
        OpenHoodMechanic = 7
    }

    /// <summary>
    /// One passive generic body at a scene-relative dock. Position is the
    /// sampled ground point under its presentation root; a seated role also
    /// carries the exact cushion anchor used by CityPedestrianPresentation.
    /// </summary>
    public readonly struct CityCourtyardResidentDescriptor
    {
        internal CityCourtyardResidentDescriptor(
            string stableId,
            string sourceStableId,
            CityCourtyardResidentActivity activity,
            string designId,
            Vector3 position,
            Vector3 facing,
            int paletteVariant,
            float animationPhase01,
            bool isSeated,
            Vector3 seatAnchorPosition)
        {
            if (string.IsNullOrWhiteSpace(stableId) ||
                string.IsNullOrWhiteSpace(sourceStableId) ||
                string.IsNullOrWhiteSpace(designId))
            {
                throw new ArgumentException(
                    "A courtyard resident requires stable source, actor and " +
                    "design IDs.");
            }

            Vector3 horizontal = new Vector3(facing.x, 0f, facing.z);
            if (!IsFinite(position) ||
                !IsFinite(seatAnchorPosition) ||
                !IsFinite(horizontal) ||
                horizontal.sqrMagnitude < 0.0001f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    "Courtyard resident poses must be finite and have a " +
                    "horizontal facing.");
            }

            StableId = stableId;
            SourceStableId = sourceStableId;
            Activity = activity;
            DesignId = designId;
            Position = position;
            Facing = horizontal.normalized;
            int normalizedPalette = paletteVariant % 4;
            PaletteVariant = normalizedPalette < 0
                ? normalizedPalette + 4
                : normalizedPalette;
            AnimationPhase01 = Mathf.Repeat(animationPhase01, 1f);
            IsSeated = isSeated;
            SeatAnchorPosition = isSeated
                ? seatAnchorPosition
                : position;
        }

        public string StableId { get; }
        public string SourceStableId { get; }
        public CityCourtyardResidentActivity Activity { get; }
        public string DesignId { get; }
        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public int PaletteVariant { get; }
        public float AnimationPhase01 { get; }
        public bool IsSeated { get; }
        public Vector3 SeatAnchorPosition { get; }

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
    /// Selects a deliberately small fixed cast from the static courtyard
    /// scenes. One occurrence of each active residential variant is enough;
    /// the four non-tunnel fringe maintenance scenes are reserved first, then
    /// residential groups are admitted atomically under the eight-body cap.
    /// </summary>
    public sealed class CityCourtyardResidentPlan
    {
        public const int MaximumResidentCount = 8;

        // Public aliases keep callers on the resident vocabulary while the
        // shared geometry contract remains the single owner of variant IDs.
        public const int NardiVariant =
            CityCourtyardPocketGeometry.NardiVariant;
        public const int BicycleRepairVariant =
            CityCourtyardPocketGeometry.BicycleVariant;
        public const int BalconyBasketVariant =
            CityCourtyardPocketGeometry.BalconyBasketVariant;
        public const int ChairRepairVariant =
            CityCourtyardPocketGeometry.ChairRepairVariant;
        public const int SweepingVariant =
            CityCourtyardPocketGeometry.SweepingVariant;
        public const int QuietVariant =
            CityCourtyardPocketGeometry.QuietVariant;

        // Root-local docks are the measured metadata exported beside the
        // corresponding City Misc assemblies. Local X is assembly right;
        // local Z is its authored forward. Keeping these as constants makes
        // the human pose follow every translated/rotated scene descriptor.
        private static readonly Vector3 NardiSeatALocal =
            new Vector3(-0.75f, 0f, 0.42f);
        private static readonly Vector3 NardiSeatBLocal =
            new Vector3(0.75f, 0f, 0.42f);
        private static readonly Vector3 NardiTargetLocal =
            new Vector3(0f, 0f, -0.15f);
        private const float NardiSeatHeight = 0.42f;

        private static readonly Vector3 BicycleDockLocal =
            new Vector3(1.15f, 0f, 0.35f);
        private static readonly Vector3 BicycleTargetLocal =
            new Vector3(-0.20f, 0f, 0f);
        private static readonly Vector3 ChairDockLocal =
            new Vector3(1.00f, 0f, 0.35f);
        private static readonly Vector3 ChairTargetLocal =
            new Vector3(-0.55f, 0f, -0.10f);
        private static readonly Vector3 SweepingDockLocal =
            new Vector3(0f, 0f, 0.35f);
        private static readonly Vector3 SweepingTargetLocal =
            new Vector3(-0.55f, 0f, -0.08f);

        private static readonly Vector3 MasonDockLocal =
            new Vector3(0f, 0f, 1.25f);
        private static readonly Vector3 MasonTargetLocal =
            new Vector3(-0.18f, 0f, 0f);
        private static readonly Vector3 WinchDockLocal =
            new Vector3(1.35f, 0f, 0.82f);
        private static readonly Vector3 WinchTargetLocal =
            Vector3.zero;
        private static readonly Vector3 FloodDockLocal =
            new Vector3(1.35f, 0f, 0.88f);
        private static readonly Vector3 FloodTargetLocal =
            new Vector3(-0.30f, 0f, 0f);
        private static readonly Vector3 MechanicDockLocal =
            new Vector3(2.48f, 0f, -1.48f);
        private static readonly Vector3 MechanicTargetLocal =
            new Vector3(1.30f, 0f, -0.35f);

        private readonly ReadOnlyCollection<
            CityCourtyardResidentDescriptor> residents;

        private CityCourtyardResidentPlan(
            int seed,
            IList<CityCourtyardResidentDescriptor> source)
        {
            Seed = seed;
            var copy = new List<CityCourtyardResidentDescriptor>(source);
            copy.Sort(CompareResidents);
            residents = new ReadOnlyCollection<
                CityCourtyardResidentDescriptor>(copy);
            ValidateOrThrow();
        }

        public int Seed { get; }
        public IReadOnlyList<CityCourtyardResidentDescriptor> Residents =>
            residents;
        public int Count => residents.Count;
        public bool IsPresent => Count > 0;

        public static CityCourtyardResidentPlan Create(
            CityLayout layout,
            CityDecorationPlan decorations,
            CityFringeYardPlan fringeYards)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (decorations == null)
            {
                throw new ArgumentNullException(nameof(decorations));
            }

            if (fringeYards == null)
            {
                throw new ArgumentNullException(nameof(fringeYards));
            }

            var selected = new List<CityCourtyardResidentDescriptor>(
                MaximumResidentCount);
            AppendFringeResidents(layout, fringeYards, selected);
            AppendResidentialResidents(layout, decorations, selected);
            return new CityCourtyardResidentPlan(layout.Seed, selected);
        }

        public static bool IsAllowedDesignId(string designId)
        {
            return string.Equals(
                       designId,
                       CityPedestrianResources.LampshadeDesignId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       designId,
                       CityPedestrianResources.LongArmDesignId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       designId,
                       CityPedestrianResources.ChairCarrierDesignId,
                       StringComparison.Ordinal);
        }

        private static void AppendFringeResidents(
            CityLayout layout,
            CityFringeYardPlan fringeYards,
            ICollection<CityCourtyardResidentDescriptor> selected)
        {
            CityFringeYardPartKind[] activeKinds =
            {
                CityFringeYardPartKind.MasonCart,
                CityFringeYardPartKind.WinchServiceSet,
                CityFringeYardPartKind.FloodMaintenanceSet,
                CityFringeYardPartKind.OpenHoodCar
            };
            for (int kindIndex = 0;
                 kindIndex < activeKinds.Length;
                 kindIndex++)
            {
                if (!TryFindFringePart(
                        fringeYards,
                        activeKinds[kindIndex],
                        out CityFringeYardPartDescriptor part,
                        out string ownerAreaId))
                {
                    continue;
                }

                ResidentGroup group = CreateFringeGroup(
                    layout,
                    part,
                    ownerAreaId);
                TryAppendGroup(selected, group);
            }
        }

        private static void AppendResidentialResidents(
            CityLayout layout,
            CityDecorationPlan decorations,
            ICollection<CityCourtyardResidentDescriptor> selected)
        {
            int[] activeVariants =
            {
                NardiVariant,
                BicycleRepairVariant,
                ChairRepairVariant,
                SweepingVariant
            };
            for (int variantIndex = 0;
                 variantIndex < activeVariants.Length;
                 variantIndex++)
            {
                if (!TryFindResidentialPocket(
                        decorations,
                        activeVariants[variantIndex],
                        out CityDecorationDescriptor pocket))
                {
                    continue;
                }

                ResidentGroup group = CreateResidentialGroup(layout, pocket);
                TryAppendGroup(selected, group);
            }
        }

        private static ResidentGroup CreateResidentialGroup(
            CityLayout layout,
            CityDecorationDescriptor pocket)
        {
            CityCourtyardPocketGeometry.ResolveFrame(
                pocket,
                out _,
                out Vector3 forward);
            Quaternion rotation = Quaternion.LookRotation(
                forward,
                Vector3.up);
            switch (pocket.Variant)
            {
                case NardiVariant:
                {
                    Vector3 first = SampleGroundedDock(
                        layout,
                        pocket.Position,
                        rotation,
                        NardiSeatALocal,
                        null);
                    Vector3 second = SampleGroundedDock(
                        layout,
                        pocket.Position,
                        rotation,
                        NardiSeatBLocal,
                        null);
                    Vector3 target = TransformDock(
                        pocket.Position,
                        rotation,
                        NardiTargetLocal);
                    return new ResidentGroup(
                        pocket.StableId,
                        new[]
                        {
                            CreateResident(
                                layout.Seed,
                                pocket.StableId,
                                "nardi-a",
                                CityCourtyardResidentActivity.NardiPlayer,
                                CityPedestrianResources.LampshadeDesignId,
                                first,
                                target - first,
                                true,
                                first + Vector3.up * NardiSeatHeight,
                                0),
                            CreateResident(
                                layout.Seed,
                                pocket.StableId,
                                "nardi-b",
                                CityCourtyardResidentActivity.NardiPlayer,
                                CityPedestrianResources.LongArmDesignId,
                                second,
                                target - second,
                                true,
                                second + Vector3.up * NardiSeatHeight,
                                2)
                        });
                }
                case BicycleRepairVariant:
                    return CreateStandingGroup(
                        layout,
                        pocket.StableId,
                        pocket.Position,
                        rotation,
                        BicycleDockLocal,
                        BicycleTargetLocal,
                        "bicycle-repair",
                        CityCourtyardResidentActivity.BicycleRepair,
                        CityPedestrianResources.LongArmDesignId,
                        null,
                        1);
                case ChairRepairVariant:
                    return CreateStandingGroup(
                        layout,
                        pocket.StableId,
                        pocket.Position,
                        rotation,
                        ChairDockLocal,
                        ChairTargetLocal,
                        "chair-repair",
                        CityCourtyardResidentActivity.ChairRepair,
                        CityPedestrianResources.ChairCarrierDesignId,
                        null,
                        2);
                case SweepingVariant:
                    return CreateStandingGroup(
                        layout,
                        pocket.StableId,
                        pocket.Position,
                        rotation,
                        SweepingDockLocal,
                        SweepingTargetLocal,
                        "sweeping",
                        CityCourtyardResidentActivity.Sweeping,
                        CityPedestrianResources.LampshadeDesignId,
                        null,
                        3);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(pocket),
                        pocket.Variant,
                        "Only active residential pocket variants own a " +
                        "resident group.");
            }
        }

        private static ResidentGroup CreateFringeGroup(
            CityLayout layout,
            CityFringeYardPartDescriptor part,
            string ownerAreaId)
        {
            Vector3 origin = part.Center -
                             Vector3.up * (part.Size.y * 0.5f);
            switch (part.Kind)
            {
                case CityFringeYardPartKind.MasonCart:
                    return CreateStandingGroup(
                        layout,
                        part.StableId,
                        origin,
                        part.Rotation,
                        MasonDockLocal,
                        MasonTargetLocal,
                        "mason",
                        CityCourtyardResidentActivity.Masonry,
                        CityPedestrianResources.LampshadeDesignId,
                        ownerAreaId,
                        0);
                case CityFringeYardPartKind.WinchServiceSet:
                    return CreateStandingGroup(
                        layout,
                        part.StableId,
                        origin,
                        part.Rotation,
                        WinchDockLocal,
                        WinchTargetLocal,
                        "winch-service",
                        CityCourtyardResidentActivity.WinchService,
                        CityPedestrianResources.LongArmDesignId,
                        ownerAreaId,
                        1);
                case CityFringeYardPartKind.FloodMaintenanceSet:
                    return CreateStandingGroup(
                        layout,
                        part.StableId,
                        origin,
                        part.Rotation,
                        FloodDockLocal,
                        FloodTargetLocal,
                        "flood-maintenance",
                        CityCourtyardResidentActivity.FloodMaintenance,
                        CityPedestrianResources.LampshadeDesignId,
                        ownerAreaId,
                        2);
                case CityFringeYardPartKind.OpenHoodCar:
                    return CreateStandingGroup(
                        layout,
                        part.StableId,
                        origin,
                        part.Rotation,
                        MechanicDockLocal,
                        MechanicTargetLocal,
                        "open-hood-mechanic",
                        CityCourtyardResidentActivity.OpenHoodMechanic,
                        CityPedestrianResources.LongArmDesignId,
                        ownerAreaId,
                        3);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(part),
                        part.Kind,
                        "The tunnel service set deliberately has no actor.");
            }
        }

        private static ResidentGroup CreateStandingGroup(
            CityLayout layout,
            string sourceStableId,
            Vector3 origin,
            Quaternion rotation,
            Vector3 localDock,
            Vector3 localTarget,
            string actorSuffix,
            CityCourtyardResidentActivity activity,
            string designId,
            string ownerAreaId,
            int paletteOffset)
        {
            Vector3 dock = SampleGroundedDock(
                layout,
                origin,
                rotation,
                localDock,
                ownerAreaId);
            Vector3 target = TransformDock(
                origin,
                rotation,
                localTarget);
            return new ResidentGroup(
                sourceStableId,
                new[]
                {
                    CreateResident(
                        layout.Seed,
                        sourceStableId,
                        actorSuffix,
                        activity,
                        designId,
                        dock,
                        target - dock,
                        false,
                        dock,
                        paletteOffset)
                });
        }

        private static CityCourtyardResidentDescriptor CreateResident(
            int seed,
            string sourceStableId,
            string actorSuffix,
            CityCourtyardResidentActivity activity,
            string designId,
            Vector3 position,
            Vector3 facing,
            bool isSeated,
            Vector3 seatAnchor,
            int paletteOffset)
        {
            string stableId = $"{sourceStableId}/resident-{actorSuffix}";
            uint hash = CityPedestrianStableHash.Combine(
                unchecked((uint)seed),
                CityPedestrianStableHash.String(stableId));
            int palette = (int)(hash % 4u) + paletteOffset;
            float phase = CityPedestrianStableHash.ToUnitFloat(
                CityPedestrianStableHash.Combine(hash, 0x50484153u));
            return new CityCourtyardResidentDescriptor(
                stableId,
                sourceStableId,
                activity,
                designId,
                position,
                facing,
                palette,
                phase,
                isSeated,
                seatAnchor);
        }

        private static Vector3 SampleGroundedDock(
            CityLayout layout,
            Vector3 origin,
            Quaternion rotation,
            Vector3 localDock,
            string ownerAreaId)
        {
            Vector3 dock = TransformDock(origin, rotation, localDock);
            if (!CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout,
                    new Vector2(dock.x, dock.z),
                    out float ground,
                    out CitySurfaceDescriptor surface) ||
                (!string.IsNullOrEmpty(ownerAreaId) &&
                 !string.Equals(
                     surface.AreaId,
                     ownerAreaId,
                     StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Courtyard resident dock at ({dock.x:F2}, " +
                    $"{dock.z:F2}) has no sampled owner ground.");
            }

            dock.y = ground;
            return dock;
        }

        private static Vector3 TransformDock(
            Vector3 origin,
            Quaternion rotation,
            Vector3 local)
        {
            Vector3 world = origin + rotation * local;
            world.y = origin.y + local.y;
            return world;
        }

        private static bool TryFindResidentialPocket(
            CityDecorationPlan decorations,
            int variant,
            out CityDecorationDescriptor result)
        {
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor candidate =
                    decorations.Descriptors[index];
                if (candidate.Kind ==
                        CityDecorationKind.ResidentialCourtyardPocket &&
                    candidate.Variant == variant)
                {
                    result = candidate;
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static bool TryFindFringePart(
            CityFringeYardPlan plan,
            CityFringeYardPartKind kind,
            out CityFringeYardPartDescriptor result,
            out string ownerAreaId)
        {
            for (int yardIndex = 0;
                 yardIndex < plan.Yards.Count;
                 yardIndex++)
            {
                CityFringeYardDescriptor yard = plan.Yards[yardIndex];
                for (int partIndex = 0;
                     partIndex < yard.Parts.Count;
                     partIndex++)
                {
                    CityFringeYardPartDescriptor candidate =
                        yard.Parts[partIndex];
                    if (candidate.Kind == kind)
                    {
                        result = candidate;
                        ownerAreaId = yard.AreaId;
                        return true;
                    }
                }
            }

            result = default;
            ownerAreaId = string.Empty;
            return false;
        }

        private static void TryAppendGroup(
            ICollection<CityCourtyardResidentDescriptor> selected,
            ResidentGroup group)
        {
            if (selected.Count + group.Residents.Count >
                MaximumResidentCount)
            {
                return;
            }

            for (int index = 0; index < group.Residents.Count; index++)
            {
                selected.Add(group.Residents[index]);
            }
        }

        private void ValidateOrThrow()
        {
            if (residents.Count > MaximumResidentCount)
            {
                throw new InvalidOperationException(
                    $"Courtyard residents exceed the " +
                    $"{MaximumResidentCount}-actor cap.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < residents.Count; index++)
            {
                CityCourtyardResidentDescriptor resident = residents[index];
                if (!ids.Add(resident.StableId) ||
                    !IsAllowedDesignId(resident.DesignId) ||
                    (resident.Activity ==
                         CityCourtyardResidentActivity.NardiPlayer) !=
                    resident.IsSeated)
                {
                    throw new InvalidOperationException(
                        $"Courtyard resident '{resident.StableId}' violates " +
                        "the passive generic cast contract.");
                }
            }
        }

        private static int CompareResidents(
            CityCourtyardResidentDescriptor left,
            CityCourtyardResidentDescriptor right)
        {
            return string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal);
        }

        private sealed class ResidentGroup
        {
            public ResidentGroup(
                string sourceStableId,
                IReadOnlyList<CityCourtyardResidentDescriptor> residents)
            {
                SourceStableId = sourceStableId ?? string.Empty;
                Residents = residents ??
                    throw new ArgumentNullException(nameof(residents));
            }

            public string SourceStableId { get; }
            public IReadOnlyList<CityCourtyardResidentDescriptor> Residents
            {
                get;
            }
        }
    }
}
