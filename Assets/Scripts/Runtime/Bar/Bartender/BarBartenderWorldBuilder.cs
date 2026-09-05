using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Stands the active ordinary bartender on the layout's authored
    /// Bartender anchor. The six-armed prefab remains provider-addressable
    /// as legacy data but is never selected here.
    /// </summary>
    public static class BarBartenderWorldBuilder
    {
        /// <summary>
        /// <paramref name="heroRoot"/> is the player root the bartender
        /// watches for across the counter; without it he keeps his eyes on
        /// his glasses.
        /// </summary>
        public static BarBartenderPresentation TryBuild(
            Transform parent,
            BarInteriorLayoutPlan layout,
            Transform heroRoot = null)
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

            GameObject bartender = UnityEngine.Object.Instantiate(
                provider.BartenderPrefab,
                parent);
            bartender.name = "Bar Bartender";
            // The authored human service clips were built against a
            // ground-level human and the bar counter has the same 1.02 m
            // working height. Keeping the former six-arm duckboard lift here
            // raises the towel by exactly 0.42 m and makes Wipe clean air.
            bartender.transform.localPosition = anchor.Position;

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
            if (heroRoot != null)
            {
                AttachHeroAttention(presentation, registry, heroRoot);
            }

            GameLog.Info(
                "bar",
                "bartender_built",
                GameLog.Field("anchor", anchor.Id),
                GameLog.Field(
                    "design",
                    registry.DesignId));
            return presentation;
        }

        /// <summary>
        /// The bartender looks up from his glasses: the hero's own head
        /// turn, run behind the presentation's own late pass, held back
        /// while an authored service clip owns the body. The legacy rig
        /// has no clips and glances whenever the hero is in the cone.
        /// </summary>
        public static NpcHeroAttentionLook AttachHeroAttention(
            BarBartenderPresentation presentation,
            BarBartenderAssetRegistry registry,
            Transform heroRoot)
        {
            if (presentation == null)
            {
                throw new ArgumentNullException(nameof(presentation));
            }

            if (registry == null || registry.Head == null)
            {
                return null;
            }

            NpcHeroAttentionLook look = presentation.gameObject
                .AddComponent<NpcHeroAttentionLook>();
            look.Initialize(
                presentation.transform,
                registry.Head,
                registry.Neck,
                heroRoot,
                () => !presentation.UsesOrdinaryRig ||
                      presentation.CurrentClipKind ==
                      BarBartenderClipKind.Wipe);
            return look;
        }
    }
}
