using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadVistaPartKind
    {
        /// <summary>The bed of the valley, and the pale thread on it.</summary>
        ValleyFloor = 0,

        /// <summary>Cloud lying in the valley, which the city stands over.</summary>
        MistBand = 1,
        CityBlock = 2,
        HorizonRidge = 3,

        /// <summary>Windows. Additive, and only after dark.</summary>
        LightPatch = 4
    }

    public readonly struct MountainRoadVistaPartDescriptor :
        IEquatable<MountainRoadVistaPartDescriptor>
    {
        internal MountainRoadVistaPartDescriptor(
            string stableId,
            MountainRoadVistaPartKind kind,
            Vector3 center,
            Vector3 size,
            float yawDegrees,
            float shade)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Center = center;
            Size = size;
            YawDegrees = yawDegrees;
            Shade = shade;
        }

        public string StableId { get; }
        public MountainRoadVistaPartKind Kind { get; }
        public Vector3 Center { get; }
        public Vector3 Size { get; }
        public float YawDegrees { get; }

        /// <summary>
        /// How near the front this piece reads, `0` furthest. Depth in a
        /// matte is a value, not a distance: the shader's own haze already
        /// owns distance, and this is what separates the valley bed from
        /// the ridge behind it when both sit in the same wash.
        /// </summary>
        public float Shade { get; }

        public bool Equals(MountainRoadVistaPartDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Center == other.Center &&
                   Size == other.Size &&
                   Mathf.Approximately(YawDegrees, other.YawDegrees) &&
                   Mathf.Approximately(Shade, other.Shade);
        }

        public override bool Equals(object obj)
        {
            return obj is MountainRoadVistaPartDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            return StableId.GetHashCode();
        }

        public override string ToString()
        {
            return $"{StableId} {Kind} @{Center} {Size}";
        }
    }

    /// <summary>
    /// What is on the other side of the brink.
    ///
    /// It is a matte, and it is honest about being one: fixed world
    /// geometry standing in the cut, at `81-105 m` — inside the area's
    /// `120 m` far plane and well outside anything the fog leaves
    /// legible, so it is the only thing out there the eye can read. It
    /// takes no scene fog and grades its own haze by camera distance
    /// instead, which is the lighthouse island's trick at a mountain's
    /// scale.
    ///
    /// Everything is measured from `y = 0`, the level of the tunnel the
    /// hero drove out of. That one anchor is what makes the drop mean
    /// something: the city down there is the city he left, at the height
    /// he left it.
    /// </summary>
    public sealed class MountainRoadVistaPlan
    {
        public const int MaximumPartCount = 220;

        private readonly ReadOnlyCollection<MountainRoadVistaPartDescriptor>
            parts;

        internal MountainRoadVistaPlan(
            IList<MountainRoadVistaPartDescriptor> sourceParts,
            Vector3 eye,
            Vector3 axis)
        {
            if (sourceParts == null)
            {
                throw new ArgumentNullException(nameof(sourceParts));
            }

            parts = new ReadOnlyCollection<MountainRoadVistaPartDescriptor>(
                new List<MountainRoadVistaPartDescriptor>(sourceParts));
            Eye = eye;
            Axis = axis;
        }

        public IReadOnlyList<MountainRoadVistaPartDescriptor> Parts => parts;

        /// <summary>Where the composition was laid out from.</summary>
        public Vector3 Eye { get; }

        public Vector3 Axis { get; }

        public int GetCount(MountainRoadVistaPartKind kind)
        {
            int total = 0;
            for (int index = 0; index < parts.Count; index++)
            {
                if (parts[index].Kind == kind)
                {
                    total++;
                }
            }

            return total;
        }
    }
}
