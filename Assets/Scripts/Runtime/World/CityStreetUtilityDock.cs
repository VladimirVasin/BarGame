using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityStreetUtilityKind
    {
        PhoneBooth = 0,
        Dumpster = 1
    }

    /// <summary>
    /// The public approach, standing position and facing for one door or lid.
    /// </summary>
    public readonly struct CityStreetUtilityDock
    {
        public CityStreetUtilityDock(
            string id,
            string decorationId,
            CityStreetUtilityKind kind,
            Vector3 standPosition,
            Vector3 facing, Vector3? publicApproach = null)
        {
            facing.y = 0f;
            if (string.IsNullOrEmpty(id) ||
                string.IsNullOrEmpty(decorationId) ||
                facing.sqrMagnitude <= 0.0001f)
            {
                this = default;
                return;
            }

            Id = id;
            DecorationId = decorationId;
            Kind = kind;
            StandPosition = standPosition;
            Facing = facing.normalized;
            Approach = publicApproach ?? standPosition - Facing * 1.0f;
            IsPresent = true;
        }

        internal static CityStreetUtilityDock At(string id, bool phone, Vector3 origin, Vector3 front, Vector3? approach = null)
        {
            front = front.normalized;
            return new CityStreetUtilityDock(id + (phone ? "-door" : "-lid"), id,
                phone ? CityStreetUtilityKind.PhoneBooth : CityStreetUtilityKind.Dumpster,
                origin + front * (phone ? CityStreetUtilityModel.Outside : CityStreetUtilityModel.BinStand), -front, approach);
        }

        public Vector3 Approach { get; }
        public string Id { get; }
        public string DecorationId { get; }
        public CityStreetUtilityKind Kind { get; }

        /// <summary>Ground point the interacting body stands on.</summary>
        public Vector3 StandPosition { get; }

        /// <summary>Horizontal unit vector toward the utility.</summary>
        public Vector3 Facing { get; }

        public bool IsPresent { get; }

        /// <summary>
        /// Collects the lower city, bar yard and Ridge service points.
        /// </summary>
        public static List<CityStreetUtilityDock> CreateAll(
            CityLayout layout,
            CityDecorationPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var docks = new List<CityStreetUtilityDock>(
                plan.GetCount(CityDecorationKind.RoadsidePhoneBooth) +
                plan.GetCount(
                    CityDecorationKind.RoadsideDumpsterAndUtility));
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityDecorationWorldBuilder.AppendUtilityDocks(
                    layout,
                    plan.Descriptors[index],
                    docks);
            }

            foreach (CityStreetUtilityDock dock in CityHillsideFurniturePlan.ForLayout(layout).UtilityDocks) docks.Add(dock);
            return docks;
        }
    }
}
