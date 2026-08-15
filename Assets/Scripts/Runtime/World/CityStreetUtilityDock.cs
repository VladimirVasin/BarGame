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
    /// The service point one street utility offers a future
    /// interaction: where a standing body docks, which way it faces
    /// and which decoration owns it. Derived from the decoration
    /// recipes the same way the bench seats are, so the prompt and the
    /// drawn door or lid can never disagree.
    /// </summary>
    public readonly struct CityStreetUtilityDock
    {
        public CityStreetUtilityDock(
            string id,
            string decorationId,
            CityStreetUtilityKind kind,
            Vector3 standPosition,
            Vector3 facing)
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
            IsPresent = true;
        }

        public string Id { get; }
        public string DecorationId { get; }
        public CityStreetUtilityKind Kind { get; }

        /// <summary>Ground point the interacting body stands on.</summary>
        public Vector3 StandPosition { get; }

        /// <summary>Horizontal unit vector toward the utility.</summary>
        public Vector3 Facing { get; }

        public bool IsPresent { get; }

        /// <summary>
        /// Collects the dock every phone booth and dumpster in the city
        /// carries, including the pair leaning on the bar-side yard wall,
        /// so a future interaction pass can install triggers the same
        /// way the bench sit pass does.
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

            return docks;
        }
    }
}
