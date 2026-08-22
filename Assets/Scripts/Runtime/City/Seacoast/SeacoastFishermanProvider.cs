using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged Lake
    /// Fisherman prefab into a build — the watchman/babushka provider
    /// pattern. The prefab itself deliberately lives outside Resources.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SeacoastFishermanProvider",
        menuName = "Bar Promenade/Seacoast Fisherman Provider")]
    public sealed class SeacoastFishermanProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/SeacoastFishermanProvider";
        public const string DesignId = "lake_fisherman_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static SeacoastFishermanProvider Load()
        {
            return Resources.Load<SeacoastFishermanProvider>(ResourcePath);
        }
    }
}
