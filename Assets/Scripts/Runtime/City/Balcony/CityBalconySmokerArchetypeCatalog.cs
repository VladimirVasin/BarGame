using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Technical eligibility for reusing Hero V2 SmokeLoop. Every accepted
    /// design is a current roaming archetype with the exact shared Avatar,
    /// both foot anchors, and the canonical mouth/right-hand sockets, and
    /// the cigarette hand prop prefab must be buildable.
    /// </summary>
    public static class CityBalconySmokerArchetypeCatalog
    {
        public static IReadOnlyList<string> EligibleDesignIds
        {
            get
            {
                var eligible = new List<string>();
                IReadOnlyList<CityPedestrianArchetype> roaming =
                    CityPedestrianResources.Archetypes;
                for (int index = 0; index < roaming.Count; index++)
                {
                    if (TryGetIneligibilityReason(
                            roaming[index],
                            out _))
                    {
                        continue;
                    }

                    eligible.Add(roaming[index].DesignId);
                }

                return new ReadOnlyCollection<string>(eligible);
            }
        }

        public static bool IsEligible(string designId)
        {
            IReadOnlyList<string> eligible = EligibleDesignIds;
            for (int index = 0; index < eligible.Count; index++)
            {
                if (string.Equals(
                        eligible[index],
                        designId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetIneligibilityReason(
            string designId,
            out string reason)
        {
            if (!CityPedestrianResources.TryGetArchetype(
                    designId,
                    out CityPedestrianArchetype archetype) ||
                !CityPedestrianResources.Roams(designId))
            {
                reason = "not a registered roaming archetype";
                return true;
            }

            return TryGetIneligibilityReason(archetype, out reason);
        }

        internal static bool TryGetIneligibilityReason(
            CityPedestrianArchetype archetype,
            out string reason)
        {
            if (archetype == null)
            {
                reason = "null archetype";
                return true;
            }

            GameObject heroPrefab = Player3DResources.LoadPrefab();
            Player3DAssetRegistry hero = heroPrefab != null
                ? heroPrefab.GetComponent<Player3DAssetRegistry>()
                : null;
            if (hero == null ||
                hero.Animator == null ||
                hero.Animator.avatar == null)
            {
                reason = "Hero V2 Avatar is unavailable";
                return true;
            }

            GameObject prefab = CityPedestrianResources.LoadPrefab(archetype);
            CityPedestrianAssetRegistry registry = prefab != null
                ? prefab.GetComponent<CityPedestrianAssetRegistry>()
                : null;
            if (registry == null ||
                registry.Animator == null ||
                registry.ModelRoot == null)
            {
                reason = "prefab registry, Animator or model root is missing";
                return true;
            }

            if (registry.Animator.avatar != hero.Animator.avatar)
            {
                reason = "Avatar differs from production Hero V2";
                return true;
            }

            if (registry.LeftFootAnchor == null ||
                registry.RightFootAnchor == null)
            {
                reason = "grounding foot anchors are missing";
                return true;
            }

            if (CityPedestrianHandProps.FindSocket(
                    registry.ModelRoot,
                    CityBalconySmokerPresentation.MouthSocketName) == null ||
                CityPedestrianHandProps.FindSocket(
                    registry.ModelRoot,
                    CityBalconySmokerPresentation.RightHandBoneName) == null ||
                CityPedestrianHandProps.FindSocket(
                    registry.ModelRoot,
                    CityBalconySmokerPresentation.CigaretteSocketName) == null)
            {
                reason = "canonical mouth/right-hand sockets are missing";
                return true;
            }

            if (registry.HeadLamp != null ||
                prefab.GetComponentInChildren<Light>(true) != null)
            {
                reason = "the prefab carries a light";
                return true;
            }

            // The cigarette is a hand prop prefab now, the same one for
            // every design, so eligibility no longer depends on a babushka
            // body carrying a borrowable skinned cigarette.
            if (!CityPedestrianHandProps.IsAvailable(
                    CityPedestrianHandPropId.Cigarette))
            {
                reason = "the cigarette hand prop is unavailable";
                return true;
            }

            reason = string.Empty;
            return false;
        }
    }
}
