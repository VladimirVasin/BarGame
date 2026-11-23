using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared seated and speaking clips for this park resident.</summary>
    [CreateAssetMenu(
        fileName = "ParkCheckersPlayerProvider",
        menuName = "Bar Promenade/Park Checkers Player Provider")]
    public sealed class ParkCheckersPlayerProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/ParkCheckersPlayerProvider";
        public const string DesignId = "park_checkers_player_v1";

        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip actionClip;
        public AnimationClip IdleClip => idleClip;
        public AnimationClip ActionClip => actionClip;

        public static ParkCheckersPlayerProvider Load()
        {
            return Resources.Load<ParkCheckersPlayerProvider>(ResourcePath);
        }
    }
}
