using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged Park Chess
    /// Player prefab into a build — the fisherman/watchman provider
    /// pattern. The prefab itself deliberately lives outside Resources.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ParkChessPlayerProvider",
        menuName = "Bar Promenade/Park Chess Player Provider")]
    public sealed class ParkChessPlayerProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/ParkChessPlayerProvider";
        public const string DesignId = "park_chess_player_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static ParkChessPlayerProvider Load()
        {
            return Resources.Load<ParkChessPlayerProvider>(ResourcePath);
        }
    }
}
