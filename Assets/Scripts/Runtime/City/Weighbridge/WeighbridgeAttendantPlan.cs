using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The weighbridge's walkable deck as a world-space
    /// oriented rectangle at deck-top height: the surface whose
    /// occupancy the indicator needle answers.</summary>
    public readonly struct CityWeighbridgeDeckRect
    {
        public CityWeighbridgeDeckRect(
            Vector3 center,
            Quaternion yaw,
            float halfWidth,
            float halfLength)
        {
            Center = center;
            Yaw = yaw;
            HalfWidth = halfWidth;
            HalfLength = halfLength;
        }

        public Vector3 Center { get; }
        public Quaternion Yaw { get; }
        public float HalfWidth { get; }
        public float HalfLength { get; }
    }

    public enum WeighbridgeAttendantRole
    {
        Weigher,
        WeighedWorker
    }

    /// <summary>One of the two authored weighbridge attendants: where
    /// they stand or pace, which palette variant they wear and how
    /// their loop is desynchronized from their neighbour's.</summary>
    public readonly struct WeighbridgeAttendantStance
    {
        public WeighbridgeAttendantStance(
            CityDryingYardNpcStance stance,
            WeighbridgeAttendantRole role,
            int paletteVariant,
            float playbackSpeed,
            float phaseOffsetSeconds,
            Vector3? pathEnd = null)
        {
            Position = stance.Position;
            Facing = stance.Facing;
            Role = role;
            PaletteVariant = paletteVariant;
            PlaybackSpeed = playbackSpeed;
            PhaseOffsetSeconds = phaseOffsetSeconds;
            PathEnd = pathEnd ?? stance.Position;
        }

        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public WeighbridgeAttendantRole Role { get; }
        public int PaletteVariant { get; }
        public float PlaybackSpeed { get; }
        public float PhaseOffsetSeconds { get; }

        /// <summary>The far end of the deck corridor; equal to
        /// <see cref="Position"/> for the stationary weigher. The
        /// worker's travel is slaved to his loop's normalized time,
        /// so there is no separate move speed.</summary>
        public Vector3 PathEnd { get; }
        public bool Strolls =>
            Role == WeighbridgeAttendantRole.WeighedWorker;
    }

    /// <summary>
    /// The authored population of the Industrial cold weighbridge:
    /// the weigher beside the mechanism reading the indicator and
    /// chalking the deck edge, and the worker pacing the deck's long
    /// axis, standing still at its centre as if being weighed. The
    /// stances come from the same recipe transform the world builder
    /// applies, so the pair lands on the actual weighbridge of any
    /// generated city; a custom blueprint without the weighbridge
    /// simply reports an absent plan.
    /// </summary>
    public sealed class WeighbridgeAttendantPlan
    {
        private static readonly WeighbridgeAttendantPlan Absent =
            new WeighbridgeAttendantPlan(
                Array.Empty<WeighbridgeAttendantStance>());

        private WeighbridgeAttendantPlan(
            IList<WeighbridgeAttendantStance> stances)
        {
            Stances = new ReadOnlyCollection<WeighbridgeAttendantStance>(
                new List<WeighbridgeAttendantStance>(stances));
        }

        public IReadOnlyList<WeighbridgeAttendantStance> Stances { get; }
        public bool IsPresent => Stances.Count > 0;

        public static WeighbridgeAttendantPlan Create(CityLayout layout)
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
                        .TryDescribeWeighbridgeStances(
                            descriptor,
                            out CityDryingYardNpcStance weigher,
                            out CityDryingYardNpcStance weighedWorker,
                            out Vector3 weighedPathEnd))
                {
                    continue;
                }

                // Distinct palettes and a desynchronized loop keep
                // the pair from ever moving in lockstep: the weigher
                // runs her check at authored pace while the worker
                // paces slightly slow and phase-shifted.
                return new WeighbridgeAttendantPlan(
                    new[]
                    {
                        new WeighbridgeAttendantStance(
                            weigher,
                            WeighbridgeAttendantRole.Weigher,
                            0,
                            1f,
                            0f),
                        new WeighbridgeAttendantStance(
                            weighedWorker,
                            WeighbridgeAttendantRole.WeighedWorker,
                            2,
                            0.97f,
                            0.85f,
                            weighedPathEnd)
                    });
            }

            return Absent;
        }
    }
}
