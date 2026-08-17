using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized reference that carries the staged Cemetery
    /// Mourner prefab into a build — the babushka/weighbridge provider
    /// pattern. The prefab itself deliberately lives outside Resources.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CemeteryMournerProvider",
        menuName = "Bar Promenade/Cemetery Mourner Provider")]
    public sealed class CemeteryMournerProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/CemeteryMournerProvider";
        public const string DesignId = "cemetery_mourner_v1";

        [SerializeField] private GameObject stagedPrefab;

        public GameObject StagedPrefab => stagedPrefab;

        public static CemeteryMournerProvider Load()
        {
            return Resources.Load<CemeteryMournerProvider>(
                ResourcePath);
        }
    }
}
