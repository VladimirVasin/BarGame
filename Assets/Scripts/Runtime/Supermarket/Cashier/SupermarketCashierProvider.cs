using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one serialized bridge to the bespoke cashier prefab. The
    /// prefab itself lives outside Resources (it is not path-loadable
    /// and never enters the pedestrian pool); only this ScriptableObject
    /// is addressable, and its single reference is what carries the
    /// prefab into a build. The yard wheelchair provider pattern.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SupermarketCashierProvider",
        menuName = "Bar Promenade/Supermarket Cashier Provider")]
    public sealed class SupermarketCashierProvider : ScriptableObject
    {
        public const string ResourcePath =
            "Supermarket/SupermarketCashierProvider";
        public const string DesignId = "watcher_cashier_v1";

        [SerializeField] private GameObject cashierPrefab;

        public GameObject CashierPrefab => cashierPrefab;

        public static SupermarketCashierProvider Load()
        {
            return Resources.Load<SupermarketCashierProvider>(
                ResourcePath);
        }
    }
}
