using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged Lake
    /// Fisherman prefab into a build — the watchman/babushka provider
    /// pattern. The prefab itself deliberately lives outside Resources.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LakeFishermanProvider",
        menuName = "Bar Promenade/Lake Fisherman Provider")]
    public sealed class LakeFishermanProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/LakeFishermanProvider";
        public const string DesignId = "lake_fisherman_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static LakeFishermanProvider Load()
        {
            return Resources.Load<LakeFishermanProvider>(ResourcePath);
        }
    }
}
