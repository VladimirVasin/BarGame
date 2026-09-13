using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Exactly one authored city actor; no additional village role or generic appearance recipe.</summary>
    public static class CanneryWomanAssetProvider
    {
        public const string ResourcePath = "City/Cannery/Woman/CanneryWomanActor";
        public static GameObject LoadPrefab() => Resources.Load<GameObject>(ResourcePath);

        public static VillageResidentPresentation Create(Transform parent)
        {
            GameObject prefab = LoadPrefab();
            if (prefab == null || prefab.GetComponent<CanneryWomanPresentation>() == null)
                throw new InvalidOperationException("Missing cannery woman prefab. Run CanneryWomanAssetSetup.BuildOrThrow.");
            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            VillageResidentPresentation motion = instance.GetComponent<VillageResidentPresentation>();
            motion.Initialize();
            instance.GetComponent<CanneryWomanPresentation>().Initialize();
            return motion;
        }
    }
}
