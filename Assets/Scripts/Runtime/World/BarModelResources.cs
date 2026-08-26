using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Loads the two authored bar models built by
    /// `tools/build-bar-3d-model.py` and assembled into prefabs by
    /// `BarAssetSetup`: the interior room and the street facade.
    /// </summary>
    public static class BarModelResources
    {
        public const string InteriorPrefabResourcePath =
            BarAssetRegistry.InteriorPrefabResourcePath;
        public const string FacadePrefabResourcePath =
            BarAssetRegistry.FacadePrefabResourcePath;

        public static GameObject LoadInteriorPrefab()
        {
            return Resources.Load<GameObject>(InteriorPrefabResourcePath);
        }

        public static GameObject LoadFacadePrefab()
        {
            return Resources.Load<GameObject>(FacadePrefabResourcePath);
        }
    }
}
