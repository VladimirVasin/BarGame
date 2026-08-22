using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the island is made of. Everything here is presentation
    /// only — the island stands past the walkable sea and past most
    /// of the fog, so its parts never collide, never light, and never
    /// enter the walkable mask.
    /// </summary>
    public enum CityLighthouseIslandPartKind
    {
        Rock = 0,
        ShackWall = 1,
        ShackRoof = 2,
        Timber = 3,
        Wreck = 4,
        TowerBase = 5,
        TowerShaft = 6,
        Gallery = 7,
        LampRoom = 8
    }

    /// <summary>
    /// One box of the island. <see cref="Shade"/> is a brightness
    /// multiplier baked into the vertex colour: ordinary parts carry a
    /// hashed weathering jitter, and the tower shaft uses it for its
    /// day-mark banding — pale courses at 1, dark courses far below —
    /// because banding is the one detail that still reads at the
    /// island's 8–25 % fog contrast.
    /// </summary>
    public readonly struct CityLighthouseIslandPartDescriptor :
        IEquatable<CityLighthouseIslandPartDescriptor>
    {
        public CityLighthouseIslandPartDescriptor(
            string stableId,
            CityLighthouseIslandPartKind kind,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            float shade)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Center = center;
            Rotation = rotation;
            Size = size;
            Shade = shade;
        }

        public string StableId { get; }
        public CityLighthouseIslandPartKind Kind { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }
        public float Shade { get; }

        public bool Equals(CityLighthouseIslandPartDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Center.Equals(other.Center) &&
                   Rotation.Equals(other.Rotation) &&
                   Size.Equals(other.Size) &&
                   Shade.Equals(other.Shade);
        }

        public override bool Equals(object obj)
        {
            return obj is CityLighthouseIslandPartDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ Center.GetHashCode();
                hash = (hash * 397) ^ Rotation.GetHashCode();
                hash = (hash * 397) ^ Size.GetHashCode();
                return (hash * 397) ^ Shade.GetHashCode();
            }
        }
    }

    /// <summary>
    /// The abandoned fishing island offshore, with the lighthouse
    /// that left the mol head. A pure data plan in world coordinates,
    /// the seacoast's own convention: parts sorted by stable id so
    /// two plans from the same inputs compare equal element by
    /// element.
    /// </summary>
    public sealed class CityLighthouseIslandPlan
    {
        /// <summary>
        /// Hard budget. The island is one silhouette read through
        /// fog, not a precinct: the measured default is ~54 parts and
        /// the headroom covers seed variation, nothing more.
        /// </summary>
        public const int MaximumPartCount = 160;

        private readonly
            ReadOnlyCollection<CityLighthouseIslandPartDescriptor>
            parts;

        internal CityLighthouseIslandPlan(
            IList<CityLighthouseIslandPartDescriptor> partSource,
            Vector3 islandCenter,
            Vector3 lanternPosition)
        {
            var partCopy =
                new List<CityLighthouseIslandPartDescriptor>(
                    partSource);
            partCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            parts =
                new ReadOnlyCollection<
                    CityLighthouseIslandPartDescriptor>(partCopy);
            IslandCenter = islandCenter;
            LanternPosition = lanternPosition;
        }

        public IReadOnlyList<CityLighthouseIslandPartDescriptor>
            Parts => parts;

        public int Count => parts.Count;

        /// <summary>The island's anchor at sea level.</summary>
        public Vector3 IslandCenter { get; }

        /// <summary>
        /// Where the rotating lantern assembly stands — the centre of
        /// the lamp room, well above the gallery.
        /// </summary>
        public Vector3 LanternPosition { get; }

        public int GetCount(CityLighthouseIslandPartKind kind)
        {
            int count = 0;
            for (int index = 0; index < parts.Count; index++)
            {
                if (parts[index].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
