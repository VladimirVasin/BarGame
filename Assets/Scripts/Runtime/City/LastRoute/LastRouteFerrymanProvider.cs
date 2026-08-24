using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged Ferryman prefab
    /// into a build - the watchman/babushka/fisherman provider pattern. The
    /// prefab itself deliberately lives outside Resources, because he is
    /// authored once and placed once and must never be reachable by the
    /// pedestrian pool.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LastRouteFerrymanProvider",
        menuName = "Bar Promenade/Last Route Ferryman Provider")]
    public sealed class LastRouteFerrymanProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/LastRouteFerrymanProvider";
        public const string DesignId = "last_route_ferryman_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static LastRouteFerrymanProvider Load()
        {
            return Resources.Load<LastRouteFerrymanProvider>(ResourcePath);
        }
    }
}
