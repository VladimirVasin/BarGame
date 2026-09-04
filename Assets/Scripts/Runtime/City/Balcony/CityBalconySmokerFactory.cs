using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Builds passive balcony smokers from compatible current roaming bodies
    /// and the literal production Hero V2 SmokeLoop clip. No navigation,
    /// interaction, collision, sound or light component is introduced; a
    /// missing cigarette is borrowed from YardBabushka3D as authored mesh.
    /// </summary>
    public static class CityBalconySmokerFactory
    {
        public const string RuntimeRootName = "Balcony Smokers";

        public static CityBalconySmokerRuntime Create(
            Transform parent,
            CityBalconySmokerPlan plan)
        {
            return Create(parent, plan, true);
        }

        internal static CityBalconySmokerRuntime CreateSingle(
            Transform parent,
            int seed,
            CityBalconySmokerDescriptor descriptor)
        {
            return Create(
                parent,
                CityBalconySmokerPlan.CreateSelection(
                    seed,
                    CityBalconySmokerSpace.CityWorld,
                    new[] { descriptor }),
                false);
        }

        private static CityBalconySmokerRuntime Create(
            Transform parent,
            CityBalconySmokerPlan plan,
            bool reportPopulation)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var rootObject = new GameObject(RuntimeRootName);
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            var presentations = new List<
                CityBalconySmokerPresentation>(plan.Count);
            try
            {
                if (!plan.IsPresent)
                {
                    return new CityBalconySmokerRuntime(
                        rootObject,
                        presentations);
                }

                PlayerAnimatedInteractionDefinition definition =
                    CityBalconySmokerPresentation
                        .CreateAnimationDefinition();
                ResolveHeroSmokeLoop(
                    definition,
                    out AnimationClip smokeLoop,
                    out Avatar heroAvatar);
                bool poseIsParentLocal =
                    plan.Space == CityBalconySmokerSpace.HomeLocal;
                var prefabs = new Dictionary<string, GameObject>(
                    StringComparer.Ordinal);
                for (int index = 0;
                     index < plan.Smokers.Count;
                     index++)
                {
                    CityBalconySmokerDescriptor descriptor =
                        plan.Smokers[index];
                    CityPedestrianArchetype archetype = ResolveArchetype(
                        descriptor.ArchetypeDesignId);
                    if (!prefabs.TryGetValue(
                            archetype.DesignId,
                            out GameObject prefab))
                    {
                        prefab = CityPedestrianResources.LoadPrefab(archetype);
                        if (prefab == null)
                        {
                            throw new InvalidOperationException(
                                $"Roaming pedestrian '{archetype.DesignId}' " +
                                "is missing at Resources/" +
                                archetype.PrefabResourcePath + ".");
                        }

                        prefabs.Add(archetype.DesignId, prefab);
                    }

                    if (!CityPedestrianResources.TryInstantiate(
                            prefab,
                            root,
                            out CityPedestrianAssetRegistry registry))
                    {
                        throw new InvalidOperationException(
                            $"Roaming pedestrian '{archetype.DesignId}' " +
                            "lost its " +
                            nameof(CityPedestrianAssetRegistry) + ".");
                    }

                    registry.gameObject.name =
                        $"Balcony Smoker {index + 1:00} " +
                        $"({archetype.DesignId})";
                    var presentation = registry.gameObject.AddComponent<
                        CityBalconySmokerPresentation>();
                    presentation.Initialize(
                        registry,
                        descriptor,
                        smokeLoop,
                        heroAvatar,
                        definition,
                        poseIsParentLocal);
                    ValidatePassivePresentation(registry.gameObject);
                    presentations.Add(presentation);
                }

                if (reportPopulation)
                {
                    GameLog.Info(
                        "city",
                        "balcony_smokers_spawned",
                        GameLog.Field("count", presentations.Count),
                        GameLog.Field("space", plan.Space.ToString()));
                }

                return new CityBalconySmokerRuntime(
                    rootObject,
                    presentations);
            }
            catch
            {
                for (int index = 0; index < presentations.Count; index++)
                {
                    if (presentations[index] != null)
                    {
                        presentations[index].Shutdown();
                    }
                }

                CityPedestrianResources.DestroyObject(rootObject);
                throw;
            }
        }

        private static CityPedestrianArchetype ResolveArchetype(
            string designId)
        {
            if (!CityPedestrianResources.TryGetArchetype(
                    designId,
                    out CityPedestrianArchetype archetype) ||
                !CityBalconySmokerArchetypeCatalog.IsEligible(designId))
            {
                throw new InvalidOperationException(
                    $"Pedestrian design '{designId}' is not technically " +
                    "eligible for the Hero V2 balcony-smoking pose.");
            }

            return archetype;
        }

        private static void ResolveHeroSmokeLoop(
            PlayerAnimatedInteractionDefinition definition,
            out AnimationClip smokeLoop,
            out Avatar heroAvatar)
        {
            GameObject heroPrefab = Player3DResources.LoadPrefab();
            Player3DAssetRegistry heroRegistry = heroPrefab != null
                ? heroPrefab.GetComponent<Player3DAssetRegistry>()
                : null;
            if (heroRegistry == null ||
                heroRegistry.Animator == null ||
                heroRegistry.Animator.avatar == null ||
                !heroRegistry.TryGetAnimation(
                    definition.LoopClipName,
                    out Player3DAnimationBinding binding) ||
                binding == null ||
                binding.Clip == null)
            {
                throw new InvalidOperationException(
                    "Production Hero V2 lost its registered SmokeLoop clip " +
                    "or Generic Avatar.");
            }

            smokeLoop = binding.Clip;
            heroAvatar = heroRegistry.Animator.avatar;
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
                    "Balcony smokers must stay colliderless, silent and " +
                    "unlit.");
            }

            MonoBehaviour[] behaviours =
                instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IInteractable)
                {
                    throw new InvalidOperationException(
                        "Balcony smokers must not be interactive.");
                }
            }
        }
    }
}
