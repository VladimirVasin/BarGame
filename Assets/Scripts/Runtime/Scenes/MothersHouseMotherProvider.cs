using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the mother's staged prefab
    /// into a build, following the babushka/cashier provider pattern. The
    /// prefab itself lives outside Resources, like every other staged
    /// character, so nothing loads her by walking a folder.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MothersHouseMotherProvider",
        menuName = "Bar Promenade/Mother's House Mother Provider")]
    public sealed class MothersHouseMotherProvider : ScriptableObject
    {
        public const string ResourcePath =
            "MothersHouse/MothersHouseMotherProvider";
        public const string DesignId = "mother_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public bool IsConfigured => stagedPrefab != null;

        public static MothersHouseMotherProvider Load()
        {
            return Resources.Load<MothersHouseMotherProvider>(ResourcePath);
        }

        public void Configure(GameObject configuredStagedPrefab)
        {
            stagedPrefab = configuredStagedPrefab;
        }

        /// <summary>
        /// Checks the one thing a provider can get wrong on its own: pointing
        /// at nothing, or at something that is not the mother.
        /// </summary>
        public void ValidateOrThrow()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "The mother's provider must bind her staged prefab.");
            }

            CityPedestrianAssetRegistry registry =
                stagedPrefab.GetComponent<CityPedestrianAssetRegistry>();
            if (registry == null || registry.DesignId != DesignId)
            {
                throw new InvalidOperationException(
                    $"The mother's provider must bind a '{DesignId}' " +
                    "prefab carrying a root pedestrian registry.");
            }
        }
    }
}
