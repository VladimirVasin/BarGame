using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the small fixed courtyard cast from generic, unlimited
    /// pedestrian archetypes. These residents never join the route director:
    /// they own no actor capsule, interaction, speech, sound or light.
    /// </summary>
    public static class CityCourtyardResidentFactory
    {
        public const string RuntimeRootName = "Courtyard Residents";

        public static IReadOnlyList<CityCourtyardResidentPresentation> Create(
            Transform parent,
            CityCourtyardResidentPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return Array.Empty<CityCourtyardResidentPresentation>();
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);
            var presentations =
                new List<CityCourtyardResidentPresentation>(plan.Count);
            var prefabs = new Dictionary<string, GameObject>(
                StringComparer.Ordinal);
            try
            {
                for (int index = 0; index < plan.Residents.Count; index++)
                {
                    CityCourtyardResidentDescriptor descriptor =
                        plan.Residents[index];
                    CityPedestrianArchetype archetype =
                        ResolveArchetype(descriptor.DesignId);
                    if (!prefabs.TryGetValue(
                            archetype.DesignId,
                            out GameObject prefab))
                    {
                        prefab = CityPedestrianResources.LoadPrefab(archetype);
                        if (prefab == null)
                        {
                            throw new InvalidOperationException(
                                $"Generic pedestrian prefab for " +
                                $"'{archetype.DesignId}' is missing at " +
                                $"Resources/{archetype.PrefabResourcePath}.");
                        }

                        prefabs.Add(archetype.DesignId, prefab);
                    }

                    if (!CityPedestrianResources.TryInstantiate(
                            prefab,
                            root,
                            out CityPedestrianAssetRegistry registry))
                    {
                        throw new InvalidOperationException(
                            $"Generic pedestrian prefab for " +
                            $"'{archetype.DesignId}' has no " +
                            nameof(CityPedestrianAssetRegistry) +
                            " on its root.");
                    }

                    registry.gameObject.name =
                        $"Courtyard Resident {index + 1:00} " +
                        $"({descriptor.Activity})";
                    ValidatePassivePresentation(registry.gameObject);
                    var presentation = registry.gameObject.AddComponent<
                        CityCourtyardResidentPresentation>();
                    presentation.Initialize(
                        registry,
                        archetype,
                        descriptor);
                    presentations.Add(presentation);
                }

                GameLog.Info(
                    "city",
                    "courtyard_residents_spawned",
                    GameLog.Field("count", presentations.Count));
                return presentations;
            }
            catch
            {
                CityPedestrianResources.DestroyObject(root.gameObject);
                throw;
            }
        }

        private static CityPedestrianArchetype ResolveArchetype(
            string designId)
        {
            if (!CityPedestrianResources.TryGetArchetype(
                    designId,
                    out CityPedestrianArchetype archetype) ||
                archetype.MaximumPoolInstances !=
                    CityPedestrianArchetype.UnlimitedPoolInstances ||
                archetype.CarriesBoilingKettle ||
                !CityCourtyardResidentPlan.IsAllowedDesignId(designId))
            {
                throw new InvalidOperationException(
                    $"Courtyard resident design '{designId}' is not an " +
                    "allowed unlimited, passive generic archetype.");
            }

            return archetype;
        }

        private static void ValidatePassivePresentation(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null ||
                instance.GetComponentInChildren<Collider2D>(true) != null ||
                instance.GetComponentInChildren<Rigidbody>(true) != null ||
                instance.GetComponentInChildren<Rigidbody2D>(true) != null ||
                instance.GetComponentInChildren<AudioSource>(true) != null ||
                instance.GetComponentInChildren<Light>(true) != null ||
                instance.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException(
                    "Courtyard resident presentations must stay " +
                    "colliderless, silent and unlit.");
            }

            MonoBehaviour[] behaviours =
                instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IInteractable)
                {
                    throw new InvalidOperationException(
                        "Courtyard resident presentations must not be " +
                        "interactive.");
                }
            }
        }
    }
}
