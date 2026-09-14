using System;
using UnityEngine;

namespace BarPromenade
{
    public static class EastGuardAssetProvider
    {
        public const string Folder = "City/EastExit/Guards/";
        public const string RifleResourcePath = Folder + "EastGuardRifleProp";
        public static string Name(int index) => index == 0 ? "EastGuardSenior" : index == 1 ? "EastGuardJunior" :
            throw new ArgumentOutOfRangeException(nameof(index));
        public static string OutfitId(int index) => index == 0 ? "east_guard_senior_duty" : index == 1 ? "east_guard_junior_duty" :
            throw new ArgumentOutOfRangeException(nameof(index));

        public static GameObject LoadOrThrow(int index)
        {
            string path = Folder + Name(index) + "Actor";
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null || prefab.GetComponent<EastGuardActor>() == null)
                throw new InvalidOperationException("Missing authored eastern guard: " + path + ". Run EastGuardAssetSetup.BuildOrThrow.");
            return prefab;
        }

        public static EastGuardActor Create(Transform parent, int index)
        {
            GameObject instance = UnityEngine.Object.Instantiate(LoadOrThrow(index), parent, false);
            EastGuardActor actor = instance.GetComponent<EastGuardActor>();
            actor.Motion.Initialize(); actor.Initialize();
            if (Application.isPlaying) NpcFootstepSources.Register(instance.transform);
            return actor;
        }
    }
}
