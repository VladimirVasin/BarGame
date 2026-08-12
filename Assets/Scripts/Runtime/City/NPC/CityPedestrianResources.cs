using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public sealed class CityPedestrianArchetype
    {
        public CityPedestrianArchetype(
            string designId,
            string prefabResourcePath,
            float minimumMovementSpeed,
            float maximumMovementSpeed,
            float minimumAnimationSpeed,
            float maximumAnimationSpeed)
        {
            if (string.IsNullOrWhiteSpace(designId))
            {
                throw new ArgumentException(
                    "A pedestrian archetype requires a design ID.",
                    nameof(designId));
            }

            if (string.IsNullOrWhiteSpace(prefabResourcePath))
            {
                throw new ArgumentException(
                    "A pedestrian archetype requires a prefab resource path.",
                    nameof(prefabResourcePath));
            }

            ValidateRange(
                minimumMovementSpeed,
                maximumMovementSpeed,
                nameof(minimumMovementSpeed));
            ValidateRange(
                minimumAnimationSpeed,
                maximumAnimationSpeed,
                nameof(minimumAnimationSpeed));

            DesignId = designId;
            PrefabResourcePath = prefabResourcePath;
            MinimumMovementSpeed = minimumMovementSpeed;
            MaximumMovementSpeed = maximumMovementSpeed;
            MinimumAnimationSpeed = minimumAnimationSpeed;
            MaximumAnimationSpeed = maximumAnimationSpeed;
        }

        public string DesignId { get; }
        public string PrefabResourcePath { get; }
        public float MinimumMovementSpeed { get; }
        public float MaximumMovementSpeed { get; }
        public float MinimumAnimationSpeed { get; }
        public float MaximumAnimationSpeed { get; }

        private static void ValidateRange(
            float minimum,
            float maximum,
            string parameterName)
        {
            if (!IsFinite(minimum) || !IsFinite(maximum) ||
                minimum <= 0f || maximum < minimum)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Pedestrian speed ranges must be finite, positive and " +
                    "ordered.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class CityPedestrianResources
    {
        public const string LampshadeDesignId =
            "lampshade_walker_v1";
        public const string ChairCarrierDesignId =
            "chair_carrier_v1";
        public const string KettleHatDesignId =
            "kettle_hat_walker_v1";
        public const string LongArmDesignId =
            "long_arm_walker_v1";
        public const string LampshadePrefabResourcePath =
            "Pedestrians/CityPedestrian3D";
        public const string ChairCarrierPrefabResourcePath =
            "Pedestrians/ChairCarrierPedestrian3D";
        public const string KettleHatPrefabResourcePath =
            "Pedestrians/KettleHatPedestrian3D";
        public const string LongArmPrefabResourcePath =
            "Pedestrians/LongArmPedestrian3D";
        public const string HelmetLampDesignId =
            "helmet_lamp_hopper_v1";
        public const string HelmetLampPrefabResourcePath =
            "Pedestrians/HelmetLampPedestrian3D";

        // Kept as the legacy single-prefab entry point.
        public const string PrefabResourcePath =
            LampshadePrefabResourcePath;

        private static readonly CityPedestrianArchetype[] OrderedArchetypes =
        {
            new CityPedestrianArchetype(
                LampshadeDesignId,
                LampshadePrefabResourcePath,
                1f,
                1.10f,
                0.84f,
                0.90f),
            new CityPedestrianArchetype(
                ChairCarrierDesignId,
                ChairCarrierPrefabResourcePath,
                1.18f,
                1.30f,
                0.98f,
                1.06f),
            // Short fast steps: the stout walker covers less ground per stride
            // than either taller design, so it moves slower while its shorter
            // clips play back faster.
            new CityPedestrianArchetype(
                KettleHatDesignId,
                KettleHatPrefabResourcePath,
                0.90f,
                1.02f,
                1.08f,
                1.18f),
            // The slowest walker in the catalog: a dragging shuffle whose
            // long clips play back slightly under authored pace.
            new CityPedestrianArchetype(
                LongArmDesignId,
                LongArmPrefabResourcePath,
                0.72f,
                0.84f,
                0.86f,
                0.94f),
            // The fastest walker: one bound covers well over a metre, so the
            // hopper crosses ground quickly despite never taking a step.
            new CityPedestrianArchetype(
                HelmetLampDesignId,
                HelmetLampPrefabResourcePath,
                1.32f,
                1.48f,
                0.94f,
                1.06f)
        };

        private static readonly IReadOnlyList<CityPedestrianArchetype>
            ReadOnlyArchetypes = Array.AsReadOnly(OrderedArchetypes);

        public static IReadOnlyList<CityPedestrianArchetype> Archetypes =>
            ReadOnlyArchetypes;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public static GameObject LoadPrefab(
            CityPedestrianArchetype archetype)
        {
            if (archetype == null)
            {
                throw new ArgumentNullException(nameof(archetype));
            }

            return Resources.Load<GameObject>(
                archetype.PrefabResourcePath);
        }

        public static GameObject[] LoadPrefabs()
        {
            var prefabs = new GameObject[OrderedArchetypes.Length];
            for (int index = 0; index < OrderedArchetypes.Length; index++)
            {
                CityPedestrianArchetype archetype =
                    OrderedArchetypes[index];
                prefabs[index] = LoadPrefab(archetype);
                if (prefabs[index] == null)
                {
                    throw new InvalidOperationException(
                        $"The '{archetype.DesignId}' city pedestrian prefab " +
                        $"is missing at Resources/" +
                        $"{archetype.PrefabResourcePath}.");
                }
            }

            return prefabs;
        }

        public static bool TryGetArchetype(
            string designId,
            out CityPedestrianArchetype archetype)
        {
            for (int index = 0; index < OrderedArchetypes.Length; index++)
            {
                CityPedestrianArchetype candidate =
                    OrderedArchetypes[index];
                if (string.Equals(
                        candidate.DesignId,
                        designId,
                        StringComparison.Ordinal))
                {
                    archetype = candidate;
                    return true;
                }
            }

            archetype = null;
            return false;
        }

        public static bool TryInstantiate(
            Transform parent,
            out CityPedestrianAssetRegistry registry)
        {
            return TryInstantiate(
                LoadPrefab(),
                parent,
                out registry);
        }

        public static bool TryInstantiate(
            GameObject prefab,
            Transform parent,
            out CityPedestrianAssetRegistry registry)
        {
            if (prefab == null)
            {
                registry = null;
                return false;
            }

            GameObject instance = Object.Instantiate(
                prefab,
                parent,
                false);
            registry = instance.GetComponent<
                CityPedestrianAssetRegistry>();
            if (registry != null)
            {
                return true;
            }

            DestroyObject(instance);
            return false;
        }

        public static CityPedestrianAssetRegistry Instantiate(
            Transform parent)
        {
            if (TryInstantiate(
                    parent,
                    out CityPedestrianAssetRegistry registry))
            {
                return registry;
            }

            throw new InvalidOperationException(
                "The city pedestrian prefab is missing or invalid at " +
                $"Resources/{PrefabResourcePath}.");
        }

        internal static void DestroyObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            CityPedestrianPresentation[] presentations =
                gameObject.GetComponentsInChildren<
                    CityPedestrianPresentation>(true);
            for (int index = 0; index < presentations.Length; index++)
            {
                presentations[index].Shutdown();
            }

            if (Application.isPlaying)
            {
                Object.Destroy(gameObject);
            }
            else
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
