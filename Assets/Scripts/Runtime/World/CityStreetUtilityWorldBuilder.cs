using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Binds each public interaction volume to its physical door or lid.
    /// </summary>
    public static class CityStreetUtilityWorldBuilder
    {
        public const string RootName = "City Street Utility Interactions";
        public const float TriggerHeight = 1.8f;
        public const float TriggerReach = 1.5f;
        public const float TriggerSpan = 1.7f;

        public static IReadOnlyList<CityStreetUtilityInteraction> Build(
            Transform parent,
            IReadOnlyList<CityStreetUtilityDock> docks, CityGameRoot city = null)
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

            var models = new Dictionary<string, CityStreetUtilityModel>(StringComparer.Ordinal);
            if (city != null)
                foreach (var model in parent.GetComponentsInChildren<CityStreetUtilityModel>(true))
                    models.TryAdd(model.SiteId, model);
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
                models.TryGetValue(dock.DecorationId, out var model);
                if (city && !model) throw new InvalidOperationException("Missing street utility " + dock.DecorationId);
                interaction.Initialize(dock, city, model);
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
