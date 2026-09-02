using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What a pooled body may NOT be carrying.
    ///
    /// A pedestrian design is one asset serving several places, and a hand
    /// prop that belongs to one of them travels to all of them. The babushka
    /// is the case that made this necessary: her prefab ships every renderer
    /// enabled, because the drying yard needs BOTH her carpet beater and her
    /// cigarette on disk and picks one per role at runtime
    /// (<see cref="DryingYardBabushkaPresentation"/>). Nothing on the roaming
    /// path ever wrote `Renderer.enabled`, so a random grandmother walked the
    /// promenade holding a carpet beater and a lit cigarette at once - which
    /// is what the user reported on 2026-09-02: «когда бабушка - случайный
    /// NPC, то у неё в руках не должно быть ни сигареты ни палки для битья
    /// ковров».
    ///
    /// THE TABLE IS PER-DESIGN AND THE NAMES ARE EXACT, and both of those are
    /// load-bearing rather than fussy:
    ///
    /// - A blanket `ACC_` prefix sweep would take her face with it. She also
    ///   carries `ACC_Eye.L`, `ACC_Eye.R`, `ACC_Mouth`, `ACC_Nose` and three
    ///   `ACC_RobeButton` parts.
    /// - A shared "strip every held prop on the street" rule would be wrong
    ///   for the designs beside her. The mourner's street clips fold both
    ///   forearms around her bouquet, so taking it leaves her carrying
    ///   nothing with both arms; the weigher's chalk is a stub inside a
    ///   closed fist and reads as her hand. Only a prop that CONTRADICTS the
    ///   ambient errand comes off, and today that is exactly one design.
    ///
    /// It never touches the prefab and never writes `true`. The yard copy and
    /// the balcony smoker both depend on the asset shipping its props
    /// enabled, and they are separate instances of the same prefab - a shared
    /// asset edit would break both.
    /// </summary>
    internal static class CityPedestrianHeldProps
    {
        /// <summary>
        /// Her two props, by exact renderer name. The beater is four meshes
        /// (handle, neck, paddle rise, paddle tip) and the cigarette two (the
        /// paper and its ember), all skinned to `hand.R`.
        /// </summary>
        private static readonly string[] BabushkaStreetHidden =
        {
            "ACC_BeaterHandle",
            "ACC_BeaterNeck",
            "ACC_BeaterPaddleRise",
            "ACC_BeaterPaddleTip",
            "ACC_Cigarette",
            "ACC_CigaretteEmber"
        };

        /// <summary>
        /// Design id to the renderers an ANONYMOUS copy of it must not show.
        /// One entry today, and it should stay a table rather than a special
        /// case: the next design with a role-specific prop will want a row,
        /// not a second if-statement somewhere else.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string[]>
            RoamingHiddenProps = new Dictionary<string, string[]>
            {
                [CityPedestrianResources.BabushkaDesignId] =
                    BabushkaStreetHidden
            };

        /// <summary>
        /// Called on every pooled body the moment it is minted, before its
        /// presentation is initialized. A design with no row is left exactly
        /// as authored.
        /// </summary>
        public static void ApplyRoamingRules(
            CityPedestrianAssetRegistry registry)
        {
            if (registry == null ||
                !RoamingHiddenProps.TryGetValue(
                    registry.DesignId,
                    out string[] hidden))
            {
                return;
            }

            Hide(registry, hidden);
        }

        /// <summary>
        /// Switches off exactly the named renderers on this instance.
        ///
        /// `Ordinal` on purpose, and exact rather than prefixed: these names
        /// come from the deterministic model generator, and a renamed mesh
        /// should silently stop matching here rather than quietly take a
        /// neighbour's mesh with it.
        /// </summary>
        public static void Hide(
            CityPedestrianAssetRegistry registry,
            IReadOnlyList<string> exactNames)
        {
            if (registry == null || exactNames == null)
            {
                return;
            }

            IReadOnlyList<Renderer> renderers = registry.Renderers;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                for (int name = 0; name < exactNames.Count; name++)
                {
                    if (string.Equals(
                            renderer.name,
                            exactNames[name],
                            System.StringComparison.Ordinal))
                    {
                        renderer.enabled = false;
                        break;
                    }
                }
            }
        }
    }
}
