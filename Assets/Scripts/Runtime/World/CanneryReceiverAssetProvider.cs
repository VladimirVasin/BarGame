using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The receiver's authored anatomy and outfit belong to this single factory role.</summary>
    public static class CanneryReceiverAssetProvider
    {
        public const string ResourcePath = "City/Cannery/Receiver/CanneryReceiverActor";
        public static GameObject LoadPrefab() => Resources.Load<GameObject>(ResourcePath);

        public static VillageResidentPresentation Create(Transform parent)
        {
            GameObject prefab = LoadPrefab();
            if (prefab == null || prefab.GetComponent<CanneryReceiverPresentation>() == null)
                throw new InvalidOperationException("Missing cannery receiver prefab. Run CanneryReceiverAssetSetup.BuildOrThrow.");
            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            VillageResidentPresentation motion = instance.GetComponent<VillageResidentPresentation>();
            motion.Initialize();
            instance.GetComponent<CanneryReceiverPresentation>().Initialize();
            return motion;
        }
    }
}
