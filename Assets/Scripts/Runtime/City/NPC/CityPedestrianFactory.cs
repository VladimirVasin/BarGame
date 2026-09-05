using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityPedestrianCollision
    {
        public const string LayerName = "CityPedestrian";
        public const int LayerIndex = 8;
        public const int DefaultLayerIndex = 0;

        public static int NonPedestrianMask => ~(1 << LayerIndex);

        public static void EnsureRuntimePolicy()
        {
            int configuredLayer = LayerMask.NameToLayer(LayerName);
            if (configuredLayer != LayerIndex)
            {
                throw new InvalidOperationException(
                    $"The {LayerName} layer must occupy layer {LayerIndex}.");
            }

            Physics.IgnoreLayerCollision(
                DefaultLayerIndex,
                LayerIndex,
                false);
            Physics.IgnoreLayerCollision(
                LayerIndex,
                LayerIndex,
                true);
        }
    }

    public static class CityPedestrianFactory
    {
        public const string RuntimeRootName =
            "City Pedestrian Runtime";

        public static CityPedestrianDirector Create(
            Transform parent,
            CityPedestrianPlan plan,
            Transform player,
            RoadWalkableArea walkableArea)
        {
            return Create(
                parent,
                plan,
                player,
                walkableArea,
                CityPedestrianPopulationProfile.City);
        }

        public static CityPedestrianDirector Create(
            Transform parent,
            CityPedestrianPlan plan,
            Transform player,
            IWalkableArea walkableArea,
            CityPedestrianPopulationProfile profile,
            Func<bool> nightModeProvider = null)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            return Create(
                parent,
                plan,
                player,
                walkableArea,
                CityPedestrianResources.LoadPooledPrefabs(profile.PoolSize),
                nightModeProvider,
                profile,
                true);
        }

        public static CityPedestrianDirector Create(
            Transform parent,
            CityPedestrianPlan plan,
            Transform player,
            IWalkableArea walkableArea,
            GameObject presentationPrefab,
            Func<bool> nightModeProvider = null,
            CityPedestrianPopulationProfile profile = null)
        {
            CityPedestrianPopulationProfile resolved =
                profile ?? CityPedestrianPopulationProfile.City;
            int slotCount = plan != null
                ? Mathf.Min(plan.Count, resolved.DaytimePopulation)
                : 0;
            var presentationPrefabs =
                new GameObject[Mathf.Max(0, slotCount)];
            for (int index = 0; index < presentationPrefabs.Length; index++)
            {
                presentationPrefabs[index] = presentationPrefab;
            }

            return Create(
                parent,
                plan,
                player,
                walkableArea,
                presentationPrefabs,
                nightModeProvider,
                resolved,
                false);
        }

        public static CityPedestrianDirector Create(
            Transform parent,
            CityPedestrianPlan plan,
            Transform player,
            IWalkableArea walkableArea,
            IReadOnlyList<GameObject> presentationPrefabs,
            Func<bool> nightModeProvider = null,
            CityPedestrianPopulationProfile profile = null)
        {
            return Create(
                parent,
                plan,
                player,
                walkableArea,
                presentationPrefabs,
                nightModeProvider,
                profile ?? CityPedestrianPopulationProfile.City,
                false);
        }

        private static CityPedestrianDirector Create(
            Transform parent,
            CityPedestrianPlan plan,
            Transform player,
            IWalkableArea walkableArea,
            IReadOnlyList<GameObject> presentationPrefabs,
            Func<bool> nightModeProvider,
            CityPedestrianPopulationProfile profile,
            bool requireCatalogComposition)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            if (walkableArea == null)
            {
                throw new ArgumentNullException(nameof(walkableArea));
            }

            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            int slotCount = Mathf.Min(
                plan.Count,
                profile.DaytimePopulation);
            int presentationCount = presentationPrefabs != null
                ? presentationPrefabs.Count
                : 0;
            if (slotCount > 0 && presentationCount == 0)
            {
                throw new InvalidOperationException(
                    "At least one city pedestrian presentation prefab is " +
                    "required when the plan has an actor slot.");
            }

            CityPedestrianCollision.EnsureRuntimePolicy();

            GameObject runtimeRoot = new GameObject(RuntimeRootName);
            runtimeRoot.transform.SetParent(parent, false);
            try
            {
                GameObject routesRoot = new GameObject("Routes");
                routesRoot.transform.SetParent(
                    runtimeRoot.transform,
                    false);
                GameObject modelPoolRoot = new GameObject("Model Pool");
                modelPoolRoot.transform.SetParent(
                    runtimeRoot.transform,
                    false);

                var actors = new List<CityPedestrianActor>(slotCount);
                for (int index = 0; index < slotCount; index++)
                {
                    GameObject actorObject = new GameObject(
                        $"Pedestrian Slot {index + 1:00}");
                    actorObject.layer =
                        CityPedestrianCollision.LayerIndex;
                    actorObject.transform.SetParent(
                        routesRoot.transform,
                        false);
                    CityPedestrianActor actor =
                        actorObject.AddComponent<CityPedestrianActor>();
                    actor.Initialize(
                        walkableArea,
                        plan.AgentRadius);
                    actors.Add(actor);
                }

                var presentations =
                    new List<CityPedestrianPresentation>(
                        presentationPrefabs != null
                            ? presentationPrefabs.Count
                            : 0);
                for (int index = 0;
                     presentationPrefabs != null &&
                     index < presentationPrefabs.Count;
                     index++)
                {
                    GameObject presentationPrefab =
                        presentationPrefabs[index];
                    if (presentationPrefab == null)
                    {
                        throw new InvalidOperationException(
                            $"City pedestrian presentation prefab " +
                            $"{index + 1} is missing.");
                    }

                    if (!CityPedestrianResources.TryInstantiate(
                            presentationPrefab,
                            modelPoolRoot.transform,
                            out CityPedestrianAssetRegistry registry))
                    {
                        throw new InvalidOperationException(
                            "The city pedestrian presentation prefab has no " +
                            "CityPedestrianAssetRegistry on its root.");
                    }

                    ValidatePassivePresentation(registry);
                    if (string.IsNullOrWhiteSpace(registry.DesignId))
                    {
                        throw new InvalidOperationException(
                            "Every city pedestrian presentation requires a " +
                            "stable design ID.");
                    }

                    CityKettleHatRigAnchors kettle =
                        registry.GetComponent<CityKettleHatRigAnchors>();
                    if (requireCatalogComposition)
                    {
                        if (!CityPedestrianResources.TryGetArchetype(
                                registry.DesignId,
                                out CityPedestrianArchetype archetype))
                        {
                            throw new InvalidOperationException(
                                $"Pedestrian design '{registry.DesignId}' " +
                                "is not registered in the ordered catalog.");
                        }

                        // The boil is declared twice on purpose - on the
                        // catalog entry and on the prefab's rig anchors -
                        // and a prefab that carries one without the other
                        // is a build that went wrong, not a design choice.
                        if (archetype.CarriesBoilingKettle != (kettle != null))
                        {
                            throw new InvalidOperationException(
                                $"Pedestrian design '{registry.DesignId}' " +
                                (archetype.CarriesBoilingKettle
                                    ? "declares a boiling kettle but its " +
                                      "prefab carries no " +
                                      nameof(CityKettleHatRigAnchors) + "."
                                    : "carries " +
                                      nameof(CityKettleHatRigAnchors) +
                                      " but declares no boiling kettle."));
                        }

                        int existing = 0;
                        for (int previous = 0;
                             previous < presentations.Count;
                             previous++)
                        {
                            if (string.Equals(
                                    presentations[previous]
                                        .Registry.DesignId,
                                    registry.DesignId,
                                    StringComparison.Ordinal))
                            {
                                existing++;
                            }
                        }

                        // A design that carries a working light declares one
                        // pooled instance, which is what keeps the worn lights
                        // in the world bounded now that the pool repeats
                        // ordinary designs.
                        if (existing >= archetype.MaximumPoolInstances)
                        {
                            throw new InvalidOperationException(
                                $"Pedestrian design '{registry.DesignId}' " +
                                "allows at most " +
                                $"{archetype.MaximumPoolInstances} pooled " +
                                "instance(s).");
                        }
                    }

                    registry.gameObject.name =
                        $"Pedestrian Model {index + 1:00} " +
                        $"({registry.DesignId})";

                    // An anonymous body carries nothing in its hands by
                    // construction: every hand prop is a separate prefab a
                    // role attaches through CityPedestrianHandProps, so
                    // there is no beater to strip here any more.
                    CityPedestrianPresentation presentation =
                        registry.GetComponent<
                            CityPedestrianPresentation>();
                    if (presentation == null)
                    {
                        presentation = registry.gameObject.AddComponent<
                            CityPedestrianPresentation>();
                    }

                    presentation.Initialize(registry);
                    if (kettle != null)
                    {
                        // Every kettle instance starts with its lid seated,
                        // whether or not this run gives it an effect: the
                        // prefab's pivot is identity by contract and the
                        // pool must never inherit a lid left mid-vent.
                        kettle.ResetLid();
                        CityKettleHatBoilEffect boil =
                            registry.GetComponent<CityKettleHatBoilEffect>();
                        if (boil == null)
                        {
                            boil = registry.gameObject.AddComponent<
                                CityKettleHatBoilEffect>();
                        }

                        boil.Initialize(presentation, kettle, (uint)index);
                    }

                    presentation.gameObject.SetActive(false);
                    presentations.Add(presentation);
                }

                if (requireCatalogComposition)
                {
                    ValidateCatalogCoverage(presentations);
                }

                CityPedestrianDirector director =
                    runtimeRoot.AddComponent<CityPedestrianDirector>();
                director.Initialize(
                    plan,
                    actors,
                    presentations,
                    player,
                    modelPoolRoot.transform,
                    nightModeProvider,
                    profile);
                return director;
            }
            catch
            {
                CityPedestrianResources.DestroyObject(runtimeRoot);
                throw;
            }
        }

        private static void ValidateCatalogCoverage(
            IReadOnlyList<CityPedestrianPresentation> presentations)
        {
            IReadOnlyList<CityPedestrianArchetype> archetypes =
                CityPedestrianResources.Archetypes;
            for (int index = 0; index < archetypes.Count; index++)
            {
                string designId = archetypes[index].DesignId;
                bool found = false;
                for (int pooled = 0;
                     pooled < presentations.Count;
                     pooled++)
                {
                    if (string.Equals(
                            presentations[pooled].Registry.DesignId,
                            designId,
                            StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new InvalidOperationException(
                        $"The pedestrian pool is missing design '{designId}'.");
                }
            }
        }

        private static void ValidatePassivePresentation(
            CityPedestrianAssetRegistry registry)
        {
            if (registry.GetComponentsInChildren<Collider>(true).Length > 0 ||
                registry.GetComponentsInChildren<Collider2D>(true).Length > 0 ||
                registry.GetComponentsInChildren<Rigidbody>(true).Length > 0 ||
                registry.GetComponentsInChildren<Rigidbody2D>(true).Length > 0)
            {
                throw new InvalidOperationException(
                    "City pedestrian presentations must remain collider-free " +
                    "and must not own rigidbodies.");
            }

            MonoBehaviour[] behaviours =
                registry.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IInteractable)
                {
                    throw new InvalidOperationException(
                        "City pedestrian presentations must not be interactive.");
                }
            }
        }
    }
}
