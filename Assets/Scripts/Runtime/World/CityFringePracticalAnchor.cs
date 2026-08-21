using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Connects one authored fringe practical to the bounded night-light
    /// pool. The anchor position is the light source and its forward axis is
    /// the authored aim direction.
    /// </summary>
    public readonly struct CityFringePracticalAnchor
    {
        public CityFringePracticalAnchor(
            CityFringeYardKind kind,
            Transform anchor)
        {
            Kind = kind;
            Anchor = anchor != null
                ? anchor
                : throw new ArgumentNullException(nameof(anchor));
        }

        public CityFringeYardKind Kind { get; }
        public Transform Anchor { get; }
    }
}
