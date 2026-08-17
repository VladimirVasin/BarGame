using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged
    /// Weighbridge Attendant prefab into a build — the babushka/yard
    /// wheelchair provider pattern. The prefab itself deliberately
    /// lives outside Resources.
    /// </summary>
    [CreateAssetMenu(
        fileName = "WeighbridgeAttendantProvider",
        menuName = "Bar Promenade/Weighbridge Attendant Provider")]
    public sealed class WeighbridgeAttendantProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/WeighbridgeAttendantProvider";
        public const string DesignId = "weigh_attendant_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static WeighbridgeAttendantProvider Load()
        {
            return Resources.Load<WeighbridgeAttendantProvider>(
                ResourcePath);
        }
    }
}
