using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The explicit external registry for the staged Pipeback Roller.
    ///
    /// The NPC is deliberately kept out of the ambient pedestrian pool: its
    /// prefab lives outside <c>Resources</c>, is absent from
    /// <see cref="CityPedestrianResources"/> and must never be loaded by
    /// path. This asset holds the only serialized reference to it, so the
    /// prefab reaches a build through this provider and nothing else.
    /// </summary>
    [CreateAssetMenu(
        fileName = "YardWheelchairProvider",
        menuName = "Bar Promenade/Yard Wheelchair Provider")]
    public sealed class YardWheelchairProvider : ScriptableObject
    {
        public const string ResourcePath = "City/YardWheelchairProvider";
        public const string DesignId = "pipeback_roller_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static YardWheelchairProvider Load()
        {
            return Resources.Load<YardWheelchairProvider>(ResourcePath);
        }
    }
}
