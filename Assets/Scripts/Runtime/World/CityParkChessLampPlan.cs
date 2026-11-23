using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One imported pendant suspended from the gazebo's cross-beam.</summary>
    public readonly struct CityParkChessLampPendant
    {
        public CityParkChessLampPendant(
            Vector3 mountPoint,
            Vector3 shadeRimCenter)
        {
            MountPoint = mountPoint;
            ShadeRimCenter = shadeRimCenter;
        }

        public Vector3 MountPoint { get; }
        public Vector3 ShadeRimCenter { get; }
        public Vector3 ShadeCenter => ShadeRimCenter +
            Vector3.up * (CityParkChessLampPlan.ShadeHeightMeters * 0.5f);
        public Vector3 BulbCenter => ShadeRimCenter +
            Vector3.up * CityParkChessLampPlan.BulbCenterHeightMeters;
    }

    /// <summary>
    /// One broad pendant centered between the two tables.
    /// The chess descriptor's origin is the walkable gazebo deck, so lamp,
    /// furniture and occupants share that datum even on the sloping lawn.
    /// No tree search, freestanding pole or cable above the roof is needed.
    /// </summary>
    public sealed class CityParkChessLampPlan
    {
        /// <summary>Measured imported pendant bounds, from rim to beam.</summary>
        public const float MountToRimMeters = 0.70f;
        public const float ShadeHeightMeters = 0.13f;
        public const float BulbCenterHeightMeters = 0.055f;

        private static readonly CityParkChessLampPlan AbsentPlan =
            new CityParkChessLampPlan(
                default,
                Vector3.zero,
                Vector3.forward,
                Vector3.right,
                false);

        private CityParkChessLampPlan(
            CityParkChessLampPendant pendant,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent,
            bool isPresent)
        {
            Pendant = pendant;
            Origin = origin;
            Forward = forward;
            Tangent = tangent;
            IsPresent = isPresent;
        }

        public CityParkChessLampPendant Pendant { get; }
        public Vector3 Origin { get; }
        public Vector3 Forward { get; }
        public Vector3 Tangent { get; }
        public bool IsPresent { get; }

        public static CityParkChessLampPlan Create(
            CityLayout layout,
            CityDecorationPlan decorations)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (decorations == null ||
                !TryFindChessBasis(
                    layout,
                    decorations,
                    out Vector3 origin,
                    out Vector3 forward,
                    out Vector3 tangent))
            {
                return AbsentPlan;
            }

            float beamAboveDeck = CityParkGazeboGeometry.BeamUndersideHeightMeters -
                CityParkGazeboGeometry.DeckHeightMeters;
            Vector3 mount = origin + Vector3.up * beamAboveDeck;
            var pendant = new CityParkChessLampPendant(mount, mount - Vector3.up * MountToRimMeters);
            return new CityParkChessLampPlan(pendant, origin, forward, tangent, true);
        }

        private static bool TryFindChessBasis(
            CityLayout layout,
            CityDecorationPlan decorations,
            out Vector3 origin,
            out Vector3 forward,
            out Vector3 tangent)
        {
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind != CityDecorationKind.ParkChessTables ||
                    !CityDecorationWorldBuilder.TryDescribeRecipeBasis(
                        layout,
                        descriptor,
                        out origin,
                        out forward))
                {
                    continue;
                }

                tangent = new Vector3(-forward.z, 0f, forward.x);
                return true;
            }

            origin = Vector3.zero;
            forward = Vector3.forward;
            tangent = Vector3.right;
            return false;
        }
    }
}
