using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Places the permanent population-owned publican at the authored service anchor.</summary>
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
            Transform heroRoot = null,
            Renderer counterSurface = null)
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

            VillageResidentPresentation actor = DefaultNpcFactory.CreateForCharacter(
                parent, DefaultNpcPopulation.BarBartender);
            CityPortCrew.AlignWorkerModelWithPlacement(actor);
            actor.ReleaseAnimation();
            GameObject bartender = actor.gameObject;
            bartender.name = "Bar Bartender";
            // The authored anchor owns the floor contact and service facing.
            bartender.transform.localPosition = anchor.Position;
            bartender.transform.localRotation =
                Quaternion.Euler(0f, anchor.YawDegrees, 0f);

            BarBartenderAssetRegistry registry =
                bartender.AddComponent<BarBartenderAssetRegistry>();
            registry.Configure(actor);
            if (counterSurface != null)
                registry.ConfigureWipeSurface(counterSurface);

            BarBartenderPresentation presentation =
                bartender.AddComponent<BarBartenderPresentation>();
            presentation.Initialize(registry);
            NpcNameplateTarget.Attach(
                bartender, "npc.name.bartender", registry.Head, "bartender");
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

        /// <summary>Shared attention yields while the service sampler owns the body.</summary>
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
                () => presentation.CurrentClipKind ==
                      BarBartenderClipKind.Wipe && !presentation.IsAcknowledgingOrder);
            return look;
        }
    }
}
