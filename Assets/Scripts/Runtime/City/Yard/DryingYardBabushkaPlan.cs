using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One authored NPC stance on a district public place:
    /// a grounded world position and a horizontal facing.</summary>
    public readonly struct CityDryingYardNpcStance
    {
        public CityDryingYardNpcStance(Vector3 position, Vector3 facing)
        {
            Position = position;
            Facing = facing;
        }

        public Vector3 Position { get; }
        public Vector3 Facing { get; }
    }

    public enum DryingYardBabushkaRole
    {
        CarpetBeater,
        Smoker
    }

    /// <summary>One of the three authored drying-yard babushkas: where
    /// she stands or strolls, what she does, which carpet she strikes,
    /// which palette variant she wears and how her loop is
    /// desynchronized from her neighbours'.</summary>
    public readonly struct DryingYardBabushkaStance
    {
        public DryingYardBabushkaStance(
            CityDryingYardNpcStance stance,
            DryingYardBabushkaRole role,
            int paletteVariant,
            float playbackSpeed,
            float phaseOffsetSeconds,
            string carpetId = null,
            Vector3? pathEnd = null,
            float moveSpeed = 0f)
        {
            Position = stance.Position;
            Facing = stance.Facing;
            Role = role;
            PaletteVariant = paletteVariant;
            PlaybackSpeed = playbackSpeed;
            PhaseOffsetSeconds = phaseOffsetSeconds;
            CarpetId = carpetId;
            PathEnd = pathEnd ?? stance.Position;
            MoveSpeed = moveSpeed;
        }

        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public DryingYardBabushkaRole Role { get; }
        public int PaletteVariant { get; }
        public float PlaybackSpeed { get; }
        public float PhaseOffsetSeconds { get; }

        /// <summary>The registry id of the carpet this beater strikes,
        /// or <c>null</c> for the smoker.</summary>
        public string CarpetId { get; }

        /// <summary>The far end of the stroll corridor; equal to
        /// <see cref="Position"/> for a stationary role.</summary>
        public Vector3 PathEnd { get; }
        public float MoveSpeed { get; }
        public bool Strolls => MoveSpeed > 0f;
    }

    /// <summary>
    /// The authored population of the Residential drying yard: two
    /// grandmothers beating the hung carpets from opposite sides of
    /// the rack and one smoking apart at the east edge, watching. The
    /// stances come from the same recipe transform the world builder
    /// applies, so the NPCs land inside the actual drying yard of any
    /// generated city; a custom blueprint without the drying yard
    /// simply reports an absent plan.
    /// </summary>
    public sealed class DryingYardBabushkaPlan
    {
        private static readonly DryingYardBabushkaPlan Absent =
            new DryingYardBabushkaPlan(
                Array.Empty<DryingYardBabushkaStance>());

        private DryingYardBabushkaPlan(
            IList<DryingYardBabushkaStance> stances)
        {
            Stances = new ReadOnlyCollection<DryingYardBabushkaStance>(
                new List<DryingYardBabushkaStance>(stances));
        }

        public IReadOnlyList<DryingYardBabushkaStance> Stances { get; }
        public bool IsPresent => Stances.Count > 0;

        public static DryingYardBabushkaPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor descriptor =
                    layout.DistrictPointsOfInterest[index];
                if (!CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeBabushkaStances(
                            descriptor,
                            out CityDryingYardNpcStance beaterSouth,
                            out CityDryingYardNpcStance beaterNorth,
                            out CityDryingYardNpcStance smoker,
                            out Vector3 smokerPathEnd))
                {
                    continue;
                }

                // The two beaters run the same loop at different
                // speeds and phases so their strikes never fall into
                // lockstep; the smoker strolls her corridor past them
                // at the pace her four-step gesture loop was authored
                // for and owns the third palette.
                return new DryingYardBabushkaPlan(
                    new[]
                    {
                        new DryingYardBabushkaStance(
                            beaterSouth,
                            DryingYardBabushkaRole.CarpetBeater,
                            0,
                            1f,
                            0f,
                            CityDryingYardCarpetRegistry.SouthCarpetId),
                        new DryingYardBabushkaStance(
                            beaterNorth,
                            DryingYardBabushkaRole.CarpetBeater,
                            1,
                            0.91f,
                            0.62f,
                            CityDryingYardCarpetRegistry.NorthCarpetId),
                        new DryingYardBabushkaStance(
                            smoker,
                            DryingYardBabushkaRole.Smoker,
                            2,
                            1f,
                            1.35f,
                            null,
                            smokerPathEnd,
                            0.36f)
                    });
            }

            return Absent;
        }
    }
}
