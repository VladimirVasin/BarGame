using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged Cemetery
    /// Watchman prefab into a build — the babushka/weighbridge
    /// provider pattern. The prefab itself deliberately lives outside
    /// Resources.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CemeteryWatchmanProvider",
        menuName = "Bar Promenade/Cemetery Watchman Provider")]
    public sealed class CemeteryWatchmanProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/CemeteryWatchmanProvider";
        public const string DesignId = "cemetery_watchman_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static CemeteryWatchmanProvider Load()
        {
            return Resources.Load<CemeteryWatchmanProvider>(
                ResourcePath);
        }
    }
}
