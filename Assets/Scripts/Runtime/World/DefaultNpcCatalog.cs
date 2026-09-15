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

        // Keep the authored asset in place: its village name is a storage detail,
        // not the identity callers use when selecting a general-purpose model.
        private static readonly Dictionary<string, string> ResourcePaths =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [OrdinaryWorker] = "VillageLife/StationWorker"
            };

        public static IReadOnlyList<string> ModelIds { get; } =
            new List<string>(ResourcePaths.Keys).AsReadOnly();

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
