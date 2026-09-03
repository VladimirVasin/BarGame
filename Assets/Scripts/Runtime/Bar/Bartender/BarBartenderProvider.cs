using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized bridge to the active ordinary bartender and the
    /// preserved six-armed legacy prefab. Both prefabs stay outside
    /// Resources and never enter the pedestrian pool; this provider is the
    /// only runtime address. The legacy reference remains explicit so the
    /// replaced design is retained without being spawned by the bar.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BarBartenderProvider",
        menuName = "Bar Promenade/Bar Bartender Provider")]
    public sealed class BarBartenderProvider : ScriptableObject
    {
        public const string ResourcePath = "Bar/BarBartenderProvider";
        public const string DesignId = "bar_bartender_v2";
        public const string LegacyDesignId = "six_armed_bartender_v1";

        [SerializeField] private GameObject bartenderPrefab;
        [SerializeField] private GameObject legacyBartenderPrefab;

        public GameObject BartenderPrefab => bartenderPrefab;
        public GameObject LegacyBartenderPrefab => legacyBartenderPrefab;

        public static BarBartenderProvider Load()
        {
            return Resources.Load<BarBartenderProvider>(ResourcePath);
        }

        public void ConfigureActive(
            GameObject activePrefab,
            GameObject legacyPrefab)
        {
            bartenderPrefab = activePrefab != null
                ? activePrefab
                : throw new System.ArgumentNullException(
                    nameof(activePrefab));
            legacyBartenderPrefab = legacyPrefab != null
                ? legacyPrefab
                : throw new System.ArgumentNullException(
                    nameof(legacyPrefab));
        }

        public void ConfigureLegacy(GameObject legacyPrefab)
        {
            legacyBartenderPrefab = legacyPrefab != null
                ? legacyPrefab
                : throw new System.ArgumentNullException(
                    nameof(legacyPrefab));
            // A clean checkout may import the old source before the active
            // one. Keep the provider usable during that narrow editor window;
            // the ordinary setup replaces this fallback immediately.
            if (bartenderPrefab == null)
            {
                bartenderPrefab = legacyPrefab;
            }
        }
    }
}
