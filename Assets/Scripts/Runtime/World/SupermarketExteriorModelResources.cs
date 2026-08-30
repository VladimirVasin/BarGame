using UnityEngine;

namespace BarPromenade
{
    public static class SupermarketExteriorModelResources
    {
        public const string PrefabResourcePath =
            SupermarketExteriorAssetRegistry.PrefabResourcePath;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }
    }
}
