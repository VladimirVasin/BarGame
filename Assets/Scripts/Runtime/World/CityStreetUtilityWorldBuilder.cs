using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Installs the placeholder interaction on every street utility
    /// dock: one trigger volume per phone booth door and dumpster lid,
    /// the same way the bench sit pass installs its seats.
    /// </summary>
    public static class CityStreetUtilityWorldBuilder
    {
        public const string RootName = "City Street Utility Interactions";
        public const float TriggerHeight = 1.8f;
        public const float TriggerReach = 1.5f;
        public const float TriggerSpan = 1.7f;

        public static IReadOnlyList<CityStreetUtilityInteraction> Build(
            Transform parent,
            IReadOnlyList<CityStreetUtilityDock> docks)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (docks == null)
            {
                throw new ArgumentNullException(nameof(docks));
            }

            var interactions = new List<CityStreetUtilityInteraction>(
                docks.Count);
            if (docks.Count == 0)
            {
                GameLog.Warning("city", "street_utility_none_installed");
                return interactions;
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            for (int index = 0; index < docks.Count; index++)
            {
                CityStreetUtilityDock dock = docks[index];
                if (!dock.IsPresent)
                {
                    continue;
                }

                var trigger = new GameObject(
                    $"Street Utility {dock.Id}");
                trigger.transform.SetParent(root, false);

                // Local Z reaches from the standing body over the door
                // or lid it faces, so the volume covers both.
                trigger.transform.SetPositionAndRotation(
                    dock.StandPosition +
                    (Vector3.up * (TriggerHeight * 0.5f)) +
                    (dock.Facing * (TriggerReach * 0.25f)),
                    Quaternion.LookRotation(dock.Facing, Vector3.up));
                BoxCollider collider =
                    trigger.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(
                    TriggerSpan,
                    TriggerHeight,
                    TriggerReach);
                var interaction = trigger.AddComponent<
                    CityStreetUtilityInteraction>();
                interaction.Initialize(dock);
                interactions.Add(interaction);
            }

            GameLog.Info(
                "city",
                "street_utility_installed",
                GameLog.Field("count", interactions.Count));
            return interactions;
        }
    }
}
