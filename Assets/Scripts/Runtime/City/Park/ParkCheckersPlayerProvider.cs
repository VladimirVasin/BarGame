using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged Park Checkers
    /// Player prefab into a build — the same provider pattern his
    /// neighbour at the other table uses. The prefab itself deliberately
    /// lives outside Resources.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ParkCheckersPlayerProvider",
        menuName = "Bar Promenade/Park Checkers Player Provider")]
    public sealed class ParkCheckersPlayerProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/ParkCheckersPlayerProvider";
        public const string DesignId = "park_checkers_player_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static ParkCheckersPlayerProvider Load()
        {
            return Resources.Load<ParkCheckersPlayerProvider>(ResourcePath);
        }
    }
}
