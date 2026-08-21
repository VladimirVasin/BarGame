using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Four restrained readings of the old perimeter service-light network.
    /// The east utility Yard deliberately owns none: it remains the open,
    /// low counterpoint to the mountain belt.
    /// </summary>
    public enum CityFringeYardPracticalKind
    {
        CulvertLamp = 0,
        RepairLamp = 1,
        TunnelReturnLamp = 2,
        FloodGaugeLamp = 3
    }

    public readonly struct CityFringeYardPracticalDescriptor :
        IEquatable<CityFringeYardPracticalDescriptor>
    {
        public CityFringeYardPracticalDescriptor(
            string stableId,
            string areaId,
            CityFringeYardKind yardKind,
            CityFringeYardPracticalKind kind,
            Vector3 position,
            Vector3 forward,
            Vector3 lensSize,
            Color litColor)
        {
            StableId = stableId ?? string.Empty;
            AreaId = areaId ?? string.Empty;
            YardKind = yardKind;
            Kind = kind;
            Position = position;
            Forward = forward;
            LensSize = lensSize;
            LitColor = litColor;
        }

        public string StableId { get; }
        public string AreaId { get; }
        public CityFringeYardKind YardKind { get; }
        public CityFringeYardPracticalKind Kind { get; }
        public Vector3 Position { get; }

        /// <summary>
        /// World-space direction of the pooled Spot when this practical is
        /// nearest. The tunnel profile points back toward the city, never
        /// into the sealed throat.
        /// </summary>
        public Vector3 Forward { get; }

        public Vector3 LensSize { get; }
        public Color LitColor { get; }

        public bool Equals(CityFringeYardPracticalDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       AreaId,
                       other.AreaId,
                       StringComparison.Ordinal) &&
                   YardKind == other.YardKind &&
                   Kind == other.Kind &&
                   Position.Equals(other.Position) &&
                   Forward.Equals(other.Forward) &&
                   LensSize.Equals(other.LensSize) &&
                   LitColor.Equals(other.LitColor);
        }

        public override bool Equals(object obj)
        {
            return obj is CityFringeYardPracticalDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    AreaId ?? string.Empty);
                hash = (hash * 397) ^ (int)YardKind;
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ Forward.GetHashCode();
                hash = (hash * 397) ^ LensSize.GetHashCode();
                return (hash * 397) ^ LitColor.GetHashCode();
            }
        }
    }
}
