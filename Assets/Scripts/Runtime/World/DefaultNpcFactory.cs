using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Builds default characters from shared world or visit appearance assignments.
    /// Placement, actions and interactions belong to the caller.
    /// </summary>
    public static class DefaultNpcFactory
    {
        public static VillageResidentPresentation CreateForCharacter(Transform parent, string characterId)
        {
            // Assign the whole population before spawning any scene's subset.
            // Scene order therefore cannot change a person's face or garments.
            return CreateForAssignment(parent, DefaultNpcPopulation.GetAssignment(characterId));
        }

        /// <summary>Builds one selection already allocated by the shared population owner.</summary>
        public static VillageResidentPresentation CreateForAssignment(
            Transform parent, DefaultNpcPopulation.Assignment assignment)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            VillageResidentPresentation actor = Create(parent, assignment.ModelId);
            try
            {
                DefaultNpcPopulation.Apply(actor, assignment);
                return actor;
            }
            catch
            {
                NpcFootstepSources.Unregister(actor.transform);
                actor.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(actor.gameObject);
                else UnityEngine.Object.DestroyImmediate(actor.gameObject);
                throw;
            }
        }

        /// <summary>Raw model creation for authoring/preview and legacy compatibility.</summary>
        public static VillageResidentPresentation Create(Transform parent,
            string modelId = DefaultNpcCatalog.OrdinaryWorker)
        {
            GameObject prefab = DefaultNpcCatalog.GetPrefab(modelId);
            if (prefab.GetComponent<VillageResidentPresentation>() == null)
                throw new InvalidOperationException("Default NPC model " + modelId + " has no compatible presentation.");

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            try
            {
                var presentation = instance.GetComponent<VillageResidentPresentation>();
                presentation.Initialize();
                NpcFootstepSources.Register(instance.transform);
                return presentation;
            }
            catch
            {
                instance.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
                else UnityEngine.Object.DestroyImmediate(instance);
                throw;
            }
        }
    }
}
