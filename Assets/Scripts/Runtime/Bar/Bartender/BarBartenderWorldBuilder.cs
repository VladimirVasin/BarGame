using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Stands the Six-Armed Bartender on the layout's authored
    /// Bartender anchor — the dedicated pass the patron builder has
    /// been reserving that spot for. Loads the bespoke prefab through
    /// its addressable provider (the cashier pattern) and starts the
    /// procedural six-arm presentation.
    /// </summary>
    public static class BarBartenderWorldBuilder
    {
        /// <summary>
        /// The service duckboard he works from. The canonical
        /// shoulders sit at 1.29 m and the counter top at ~1.56 m, so
        /// without the platform only his head clears the counter and
        /// reads as one more backbar bottle from the hall. Raised,
        /// his shoulders and the whole six-arm fan work above the
        /// counter line.
        /// </summary>
        public const float DuckboardHeight = 0.42f;

        public static BarBartenderPresentation TryBuild(
            Transform parent,
            BarInteriorLayoutPlan layout)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            bool anchorFound = false;
            BarNpcAnchor anchor = default;
            for (int index = 0;
                 index < layout.NpcAnchors.Count;
                 index++)
            {
                if (layout.NpcAnchors[index].Role ==
                    BarNpcRole.Bartender)
                {
                    anchor = layout.NpcAnchors[index];
                    anchorFound = true;
                    break;
                }
            }

            if (!anchorFound)
            {
                GameLog.Warning("bar", "bartender_anchor_missing");
                return null;
            }

            BarBartenderProvider provider =
                BarBartenderProvider.Load();
            if (provider == null ||
                provider.BartenderPrefab == null)
            {
                GameLog.Warning("bar", "bartender_prefab_missing");
                return null;
            }

            RuntimePrimitiveFactory.CreateBox(
                "Bartender Duckboard",
                parent,
                anchor.Position +
                new Vector3(0f, DuckboardHeight * 0.5f, 0f),
                new Vector3(1.35f, DuckboardHeight, 0.85f),
                new Color(0.16f, 0.11f, 0.07f),
                false);

            GameObject bartender = UnityEngine.Object.Instantiate(
                provider.BartenderPrefab,
                parent);
            bartender.name = "Bar Bartender";
            bartender.transform.localPosition =
                anchor.Position + (Vector3.up * DuckboardHeight);

            // The sprite-era anchor yaw runs along the service alley;
            // the 3D publican must face his guests across the counter,
            // so he looks from the anchor toward the hall's center and
            // only falls back to the authored yaw at the room origin.
            Vector3 towardHall = -anchor.Position;
            towardHall.y = 0f;
            bartender.transform.localRotation =
                towardHall.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(
                        towardHall.normalized,
                        Vector3.up)
                    : Quaternion.Euler(0f, anchor.YawDegrees, 0f);

            BarBartenderAssetRegistry registry =
                bartender.GetComponent<BarBartenderAssetRegistry>();
            if (registry == null)
            {
                GameLog.Warning("bar", "bartender_registry_missing");
                UnityEngine.Object.Destroy(bartender);
                return null;
            }

            BarBartenderPresentation presentation =
                bartender.AddComponent<BarBartenderPresentation>();
            presentation.Initialize(registry);
            GameLog.Info(
                "bar",
                "bartender_built",
                GameLog.Field("anchor", anchor.Id),
                GameLog.Field(
                    "design",
                    registry.DesignId));
            return presentation;
        }
    }
}
