using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared seated and speaking clips for this park resident.</summary>
    [CreateAssetMenu(
        fileName = "ParkChessPlayerProvider",
        menuName = "Bar Promenade/Park Chess Player Provider")]
    public sealed class ParkChessPlayerProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/ParkChessPlayerProvider";
        public const string DesignId = "park_chess_player_v1";

        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip actionClip;
        public AnimationClip IdleClip => idleClip;
        public AnimationClip ActionClip => actionClip;

        public static ParkChessPlayerProvider Load()
        {
            return Resources.Load<ParkChessPlayerProvider>(ResourcePath);
        }
    }
}
