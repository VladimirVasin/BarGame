using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One imported pendant suspended from the gazebo's cross-beam.</summary>
    public readonly struct CityParkChessLampPendant
    {
        public CityParkChessLampPendant(
            Vector3 mountPoint,
            Vector3 shadeRimCenter,
            bool isLit)
        {
            MountPoint = mountPoint;
            ShadeRimCenter = shadeRimCenter;
            IsLit = isLit;
        }

        public Vector3 MountPoint { get; }
        public Vector3 ShadeRimCenter { get; }
        public Vector3 ShadeCenter => ShadeRimCenter +
            Vector3.up * (CityParkChessLampPlan.ShadeHeightMeters * 0.5f);
        public Vector3 BulbCenter => ShadeRimCenter +
            Vector3.up * CityParkChessLampPlan.BulbCenterHeightMeters;
        public bool IsLit { get; }
    }

    /// <summary>
    /// Two beam-mounted shades over the two tables, one still burning.
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
                default,
                Vector3.zero,
                Vector3.forward,
                Vector3.right,
                false);

        private CityParkChessLampPlan(
            CityParkChessLampPendant lit,
            CityParkChessLampPendant dead,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent,
            bool isPresent)
        {
            LitPendant = lit;
            DeadPendant = dead;
            Origin = origin;
            Forward = forward;
            Tangent = tangent;
            IsPresent = isPresent;
        }

        public CityParkChessLampPendant LitPendant { get; }
        public CityParkChessLampPendant DeadPendant { get; }
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

            CityParkChessLampPendant lit = DescribePendant(
                origin,
                tangent,
                -CityChessBoardGeometry.TableOffsetMeters,
                true);
            CityParkChessLampPendant dead = DescribePendant(
                origin,
                tangent,
                CityChessBoardGeometry.TableOffsetMeters,
                false);
            return new CityParkChessLampPlan(
                lit,
                dead,
                origin,
                forward,
                tangent,
                true);
        }

        private static CityParkChessLampPendant DescribePendant(
            Vector3 origin,
            Vector3 tangent,
            float tableOffset,
            bool isLit)
        {
            float beamAboveDeck =
                CityParkGazeboGeometry.BeamUndersideHeightMeters -
                CityParkGazeboGeometry.DeckHeightMeters;
            Vector3 mount = origin + tangent * tableOffset +
                Vector3.up * beamAboveDeck;
            return new CityParkChessLampPendant(
                mount,
                mount - Vector3.up * MountToRimMeters,
                isLit);
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
