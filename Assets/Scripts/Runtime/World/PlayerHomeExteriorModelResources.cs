using UnityEngine;

namespace BarPromenade
{
    public static class PlayerHomeExteriorModelResources
    {
        public const string PrefabResourcePath =
            PlayerHomeExteriorAssetRegistry.PrefabResourcePath;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }
    }
}
