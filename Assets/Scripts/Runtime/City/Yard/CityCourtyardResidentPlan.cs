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
        Sweeping = 3
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
    /// Selects a deliberately small fixed cast from the static residential
    /// courtyard scenes. One occurrence of each active variant is enough;
    /// groups are admitted atomically under the five-body cap.
    /// </summary>
    public sealed class CityCourtyardResidentPlan
    {
        public const int MaximumResidentCount = 5;

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
            CityDecorationPlan decorations)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (decorations == null)
            {
                throw new ArgumentNullException(nameof(decorations));
            }

            var selected = new List<CityCourtyardResidentDescriptor>(
                MaximumResidentCount);
            AppendResidentialResidents(layout, decorations, selected);
            return new CityCourtyardResidentPlan(layout.Seed, selected);
        }

        /// <summary>
        /// Who may stand in a courtyard, recast whole on 2026-09-02.
        ///
        /// It used to be the Lampshade, the Long-Arm and the Chair Carrier,
        /// and that was never a casting decision - it was the roaming pool of
        /// the day, frozen into a literal. When the strange walkers came off
        /// the street the courtyards kept them, so the one place a player
        /// meets a faceless figure or a man with forearms to his ankles
        /// became a residential yard, a metre from the pavement, at every
        /// seed. The user found all three by walking the city.
        ///
        /// The three now named are the only ordinary designs that own a
        /// WORKING LOOP of their own, which is what makes them worth placing:
        /// `WatchmanWatch`, `WeigherCheck` and `BabushkaSmoke` are six, six
        /// and four seconds of authored business against the one-and-a-half
        /// second pavement breath these pockets used to play.
        ///
        /// Still a hard-coded literal list, deliberately, and NOT a read of
        /// `NpcDesignAppearanceCatalog`: that table records verdicts and the
        /// architecture notes state plainly that the runtime does not consult
        /// it - model selection stays explicit at each site. What the catalog
        /// buys is a TEST that no site contradicts it.
        /// </summary>
        public static bool IsAllowedDesignId(string designId)
        {
            return string.Equals(
                       designId,
                       CityPedestrianResources.WatchmanDesignId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       designId,
                       CityPedestrianResources.WeighAttendantDesignId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       designId,
                       CityPedestrianResources.BabushkaDesignId,
                       StringComparison.Ordinal);
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
                                // Forced rather than chosen: a seated role
                                // needs both a declared seated ride and a
                                // wired sit clip, and among ordinary designs
                                // only the watchman and the weigher have
                                // both. The park's two board players have
                                // neither - `sitClip` is null on both
                                // prefabs - which is why the obvious cast
                                // for a board game is impossible.
                                CityPedestrianResources.WatchmanDesignId,
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
                                CityPedestrianResources.WeighAttendantDesignId,
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
                        // A grandmother standing over an upturned bicycle,
                        // smoking and talking with her free arm. Her own
                        // `BabushkaSmoke` is four seconds of exactly that -
                        // "emphatic left-arm talk, one drag per lap" - and
                        // read over a bicycle it becomes commentary on
                        // somebody else's repair. Her carpet beater is hidden
                        // for this role and the cigarette stays; see the
                        // factory.
                        CityPedestrianResources.BabushkaDesignId,
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
                        // The watchman, and the point is that HIS HANDS ARE
                        // EMPTY. The Chair Carrier stood here until
                        // 2026-09-02 and put three chairs in one three-metre
                        // frame - the one on his shoulders, the one being
                        // mended and the bench - which is the fault the user
                        // photographed. `WatchmanWatch` is hands clasped
                        // behind the back, a disapproving head shake and one
                        // smug chin jut: a man who has put the plane down and
                        // does not like what he made.
                        CityPedestrianResources.WatchmanDesignId,
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
                        // `WeigherCheck` is "look up at the dial, lean in to
                        // the linkage, crouch to chalk the deck edge" - a
                        // woman bending to the ground and straightening, which
                        // over a broom reads as sweeping. NOTE this pocket
                        // exists only on seeds whose optional variant
                        // resolves to it, and the shipping seed resolves to
                        // chair repair instead; leaving one active variant
                        // uncast is exactly how the old cast survived so
                        // long, so it is cast anyway and covered by a
                        // synthetic pocket in EditMode.
                        CityPedestrianResources.WeighAttendantDesignId,
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
