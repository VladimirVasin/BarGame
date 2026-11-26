using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Explicitly reusable base appearances, independent of a character's job.
    /// New NPCs use this catalogue unless a bespoke appearance was requested.
    /// </summary>
    public static class DefaultNpcCatalog
    {
        public const string OrdinaryWorker = "ordinary-worker-v1";
        public const string OrdinaryWorkerResourcePath = "VillageLife/OrdinaryWorker";
        public const string OrdinaryWoman = "ordinary-woman-v1";
        public const string OrdinaryWomanResourcePath = "City/Residents/OrdinaryWomanActor";
        public const string StreetChild = "street-child-v1";
        public const string StreetChildResourcePath = "City/FairChild/StreetChildActor";
        public const string StreetGirl = "street-girl-v1";
        public const string StreetGirlResourcePath = "City/FairChild/StreetGirlActor";

        // A prefab needs its own Resources address: the source StationWorker
        // FBX is also a GameObject, but has no wardrobe or appearance bindings.
        private static readonly Dictionary<string, string> ResourcePaths =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [OrdinaryWorker] = OrdinaryWorkerResourcePath,
                [OrdinaryWoman] = OrdinaryWomanResourcePath,
                [StreetChild] = StreetChildResourcePath,
                [StreetGirl] = StreetGirlResourcePath
            };

        public static IReadOnlyList<string> ModelIds { get; } =
            new List<string>(ResourcePaths.Keys).AsReadOnly();
        public static IReadOnlyList<string> AdultModelIds { get; } =
            Array.AsReadOnly(new[] { OrdinaryWorker, OrdinaryWoman });

        public static bool IsStreetChild(string modelId) => modelId == StreetChild || modelId == StreetGirl;

        public static string GetResourcePath(string modelId)
        {
            if (string.IsNullOrEmpty(modelId) || !ResourcePaths.TryGetValue(modelId, out string path))
                throw new ArgumentException("Unknown default NPC model: " + modelId +
                    ". Add an explicitly approved appearance to the catalogue.", nameof(modelId));
            return path;
        }

        public static GameObject GetPrefab(string modelId = OrdinaryWorker)
        {
            string path = GetResourcePath(modelId);
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("Missing default NPC model " + modelId + " at Resources/" + path + ".");
            return prefab;
        }
    }
}
