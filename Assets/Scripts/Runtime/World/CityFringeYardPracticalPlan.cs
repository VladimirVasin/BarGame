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

    internal static class CityTunnelPortalLightGeometry
    {
        internal const float CitywardOffset = 0.85f;
        internal const float HeightAboveOpening = 0.38f;
        internal const float AimDownRatio = 1.15f;
        internal const float MinimumHousingClearance = 4.2f;

        internal static readonly Vector3 HousingSize =
            new Vector3(0.58f, 0.34f, 0.38f);

        internal static Vector3 ResolvePosition(
            CityTunnelForecourtDescriptor forecourt)
        {
            Vector3 axis = Flatten(forecourt.Axis);
            return forecourt.PortalAnchor - axis * CitywardOffset +
                   Vector3.up *
                   (CityMountainBoundaryDefinition.TunnelOpeningHeight +
                    HeightAboveOpening);
        }

        internal static Vector3 ResolveForward(
            CityTunnelForecourtDescriptor forecourt)
        {
            return (Flatten(forecourt.Axis) +
                    Vector3.down * AimDownRatio).normalized;
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.normalized;
        }
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
        /// nearest. The tunnel profile is the overhead portal floodlight and
        /// points down the open entrance corridor.
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
