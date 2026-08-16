using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged Yard
    /// Babushka prefab into a build — the cashier/yard-wheelchair
    /// provider pattern. The prefab itself deliberately lives outside
    /// Resources.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DryingYardBabushkaProvider",
        menuName = "Bar Promenade/Drying Yard Babushka Provider")]
    public sealed class DryingYardBabushkaProvider : ScriptableObject
    {
        public const string ResourcePath = "City/DryingYardBabushkaProvider";
        public const string DesignId = "yard_babushka_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static DryingYardBabushkaProvider Load()
        {
            return Resources.Load<DryingYardBabushkaProvider>(ResourcePath);
        }
    }
}
