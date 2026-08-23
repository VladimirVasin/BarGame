using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized bridge to the bespoke stairwell cat prefab.
    /// The prefab itself lives outside Resources; only this
    /// ScriptableObject is addressable, and its single reference is
    /// what carries the prefab into a build. The cashier provider
    /// pattern.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StairwellCatProvider",
        menuName = "Bar Promenade/Stairwell Cat Provider")]
    public sealed class StairwellCatProvider : ScriptableObject
    {
        public const string ResourcePath =
            "Stairwell/StairwellCatProvider";
        public const string DesignId = "cheshire_stairwell_cat_v1";

        [SerializeField] private GameObject catPrefab;

        public GameObject CatPrefab => catPrefab;

        public static StairwellCatProvider Load()
        {
            return Resources.Load<StairwellCatProvider>(ResourcePath);
        }
    }
}
