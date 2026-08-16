using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized bridge to the bespoke bartender prefab. The
    /// prefab itself lives outside Resources (it is not path-loadable
    /// and never enters the pedestrian pool); only this ScriptableObject
    /// is addressable, and its single reference is what carries the
    /// prefab into a build. The cashier/yard-wheelchair provider
    /// pattern.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BarBartenderProvider",
        menuName = "Bar Promenade/Bar Bartender Provider")]
    public sealed class BarBartenderProvider : ScriptableObject
    {
        public const string ResourcePath = "Bar/BarBartenderProvider";
        public const string DesignId = "six_armed_bartender_v1";

        [SerializeField] private GameObject bartenderPrefab;

        public GameObject BartenderPrefab => bartenderPrefab;

        public static BarBartenderProvider Load()
        {
            return Resources.Load<BarBartenderProvider>(ResourcePath);
        }
    }
}
