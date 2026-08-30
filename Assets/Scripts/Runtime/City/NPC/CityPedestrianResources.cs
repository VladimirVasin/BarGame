using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// A design's declared permission to ride Route 01, with the numbers that
    /// place it on a seat. Every walker shares the hero's 31-bone rig and its
    /// `0.70 m` rest pelvis, so seating is one pelvis alignment for all of
    /// them; what a design owns here is how its own authored seated posture
    /// meets the cushion.
    /// </summary>
    public sealed class CityPedestrianSeatedRide
    {
        public CityPedestrianSeatedRide(
            float seatLift,
            float seatBackOffset,
            float seatedHeadroom)
        {
            if (!IsFinite(seatLift) ||
                !IsFinite(seatBackOffset) ||
                !IsFinite(seatedHeadroom) ||
                seatedHeadroom <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seatLift),
                    "A seated ride declaration must be finite and own a " +
                    "positive headroom.");
            }

            SeatLift = seatLift;
            SeatBackOffset = seatBackOffset;
            SeatedHeadroom = seatedHeadroom;
        }

        /// <summary>
        /// Metres the pelvis sits above the cushion anchor.
        /// </summary>
        public float SeatLift { get; }

        /// <summary>
        /// Metres the pelvis sits behind the cushion anchor, toward the
        /// backrest.
        /// </summary>
        public float SeatBackOffset { get; }

        /// <summary>
        /// Metres this design occupies above its seated pelvis, worn objects
        /// included. The cabin gives `2.05 m` from floor to ceiling and the
        /// cushion sits `0.41 m` up, so a declaration above `1.64 m` would
        /// push the design through the roof.
        /// </summary>
        public float SeatedHeadroom { get; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class CityPedestrianArchetype
    {
        public const int UnlimitedPoolInstances = int.MaxValue;

        public CityPedestrianArchetype(
            string designId,
            string prefabResourcePath,
            float minimumMovementSpeed,
            float maximumMovementSpeed,
            float minimumAnimationSpeed,
            float maximumAnimationSpeed)
            : this(
                designId,
                prefabResourcePath,
                minimumMovementSpeed,
                maximumMovementSpeed,
                minimumAnimationSpeed,
                maximumAnimationSpeed,
                UnlimitedPoolInstances)
        {
        }

        public CityPedestrianArchetype(
            string designId,
            string prefabResourcePath,
            float minimumMovementSpeed,
            float maximumMovementSpeed,
            float minimumAnimationSpeed,
            float maximumAnimationSpeed,
            int maximumPoolInstances)
            : this(
                designId,
                prefabResourcePath,
                minimumMovementSpeed,
                maximumMovementSpeed,
                minimumAnimationSpeed,
                maximumAnimationSpeed,
                maximumPoolInstances,
                0f)
        {
        }

        public CityPedestrianArchetype(
            string designId,
            string prefabResourcePath,
            float minimumMovementSpeed,
            float maximumMovementSpeed,
            float minimumAnimationSpeed,
            float maximumAnimationSpeed,
            int maximumPoolInstances,
            float groundTrim)
            : this(
                designId,
                prefabResourcePath,
                minimumMovementSpeed,
                maximumMovementSpeed,
                minimumAnimationSpeed,
                maximumAnimationSpeed,
                maximumPoolInstances,
                groundTrim,
                null)
        {
        }

        public CityPedestrianArchetype(
            string designId,
            string prefabResourcePath,
            float minimumMovementSpeed,
            float maximumMovementSpeed,
            float minimumAnimationSpeed,
            float maximumAnimationSpeed,
            int maximumPoolInstances,
            float groundTrim,
            CityPedestrianSeatedRide seatedRide)
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
            if (maximumPoolInstances <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPoolInstances),
                    "A pedestrian archetype must allow at least one pooled " +
                    "instance.");
            }

            DesignId = designId;
            PrefabResourcePath = prefabResourcePath;
            MinimumMovementSpeed = minimumMovementSpeed;
            MaximumMovementSpeed = maximumMovementSpeed;
            MinimumAnimationSpeed = minimumAnimationSpeed;
            MaximumAnimationSpeed = maximumAnimationSpeed;
            if (!IsFinite(groundTrim) || groundTrim < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(groundTrim),
                    "A ground trim must be finite and non-negative.");
            }

            MaximumPoolInstances = maximumPoolInstances;
            GroundTrim = groundTrim;
            SeatedRide = seatedRide;
        }

        public string DesignId { get; }
        public string PrefabResourcePath { get; }
        public float MinimumMovementSpeed { get; }
        public float MaximumMovementSpeed { get; }
        public float MinimumAnimationSpeed { get; }
        public float MaximumAnimationSpeed { get; }

        /// <summary>
        /// Pooled copies this design may own. A design that carries a working
        /// light declares `1`, which is what bounds the worn lights in the
        /// world once the pool grew past one instance per design.
        /// </summary>
        public int MaximumPoolInstances { get; }

        /// <summary>
        /// Metres this design is lowered at runtime. Only an airborne design
        /// needs one: every other walker has its lowest sole pinned to the
        /// pavement each frame, which already absorbs whatever height the
        /// shared Generic Avatar introduces when it retargets a skeleton whose
        /// proportions differ from the hero's. Pinning an airborne design the
        /// same way would flatten its arc, so the residual lift is declared
        /// here and tuned by eye against the rendered walker.
        /// </summary>
        public float GroundTrim { get; }

        /// <summary>
        /// Declared permission to ride Route 01, or `null` for a design that
        /// stays on the pavement. A blanket ban would be dishonest and a
        /// blanket allowance would seat a design that cannot sit: the hopper
        /// crosses ground in two-footed bounds and wears the one working light
        /// the pedestrian contract allows, neither of which belongs in a
        /// twelve-seat cabin.
        /// </summary>
        public CityPedestrianSeatedRide SeatedRide { get; }

        public bool CanRideBus => SeatedRide != null;

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

        // Headroom values below are the measured maxima the deterministic
        // generator reports for each design's own authored seated clip, and
        // the generator asserts the same numbers through
        // `ArchetypeSpec.seated_clearance_m`. The cushion sits `0.41 m` above
        // the cabin floor under a `2.05 m` ceiling, so `1.64 m` is the point
        // at which a design would pass through the roof; the whole catalog
        // clears it comfortably.
        //
        // The seat lift is measured too, and it is not a nominal value. The
        // runtime aligns the shared rest pelvis to the cushion anchor, so a
        // design rests on the seat only if it is lifted by the distance from
        // that bone down to the underside of its own seated hips and thighs -
        // `seated_contact_m` in the clip manifest. Nominal `0.015` lifts sank
        // every design into the cushion by `4.6-11.1 cm`, worst on the stout
        // Kettle Hat whose belly and wide hips reach furthest below the bone.
        // Each value below is that measurement less `0.01 m`, so the cushion
        // reads as compressed rather than the passenger as floating.

        /// <summary>
        /// The hunched design keeps its C-curve seated, which is what makes it
        /// the lowest seated silhouette of the four riders.
        /// </summary>
        private static readonly CityPedestrianSeatedRide LampshadeSeatedRide =
            new CityPedestrianSeatedRide(0.066f, 0.20f, 0.907f);

        /// <summary>
        /// An upright spine under the inverted cafe chair it never puts down.
        /// Seated it reads tallest, though the chair rides the shoulders
        /// rather than towering over the head.
        /// </summary>
        private static readonly CityPedestrianSeatedRide
            ChairCarrierSeatedRide =
                new CityPedestrianSeatedRide(0.071f, 0.22f, 0.918f);

        /// <summary>
        /// Short legs that do not reach the cabin floor, and an oversized
        /// kettle that owns everything above `1.40 m` standing. It sits a
        /// little further forward on the cushion than the others.
        /// </summary>
        private static readonly CityPedestrianSeatedRide KettleHatSeatedRide =
            new CityPedestrianSeatedRide(0.118f, 0.18f, 0.914f);

        /// <summary>
        /// A narrow still torso; the forearms that reach the pavement standing
        /// are folded onto the knees seated rather than through the floor.
        /// </summary>
        private static readonly CityPedestrianSeatedRide LongArmSeatedRide =
            new CityPedestrianSeatedRide(0.054f, 0.24f, 0.915f);

        private static readonly CityPedestrianArchetype[] OrderedArchetypes =
        {
            new CityPedestrianArchetype(
                LampshadeDesignId,
                LampshadePrefabResourcePath,
                1f,
                1.10f,
                0.84f,
                0.90f,
                CityPedestrianArchetype.UnlimitedPoolInstances,
                0f,
                LampshadeSeatedRide),
            new CityPedestrianArchetype(
                ChairCarrierDesignId,
                ChairCarrierPrefabResourcePath,
                1.18f,
                1.30f,
                0.98f,
                1.06f,
                CityPedestrianArchetype.UnlimitedPoolInstances,
                0f,
                ChairCarrierSeatedRide),
            // Short fast steps: the stout walker covers less ground per stride
            // than either taller design, so it moves slower while its shorter
            // clips play back faster.
            new CityPedestrianArchetype(
                KettleHatDesignId,
                KettleHatPrefabResourcePath,
                0.90f,
                1.02f,
                1.08f,
                1.18f,
                CityPedestrianArchetype.UnlimitedPoolInstances,
                0f,
                KettleHatSeatedRide),
            // The slowest walker in the catalog: a dragging shuffle whose
            // long clips play back slightly under authored pace.
            new CityPedestrianArchetype(
                LongArmDesignId,
                LongArmPrefabResourcePath,
                0.72f,
                0.84f,
                0.86f,
                0.94f,
                CityPedestrianArchetype.UnlimitedPoolInstances,
                0f,
                LongArmSeatedRide),
            // The fastest walker: one bound covers well over a metre, so the
            // hopper crosses ground quickly despite never taking a step. It is
            // also the only design wearing a working light, so it stays a
            // single pooled instance however large the pool grows. It declares
            // no seated ride: a design that hops on 0.46 m hind feet has no
            // seated posture to author, and its worn Spot has no business
            // inside the cabin.
            new CityPedestrianArchetype(
                HelmetLampDesignId,
                HelmetLampPrefabResourcePath,
                1.32f,
                1.48f,
                0.94f,
                1.06f,
                1,
                // Tuned by eye against the rendered walker. Automated
                // measurement cannot settle this one: a sole's true height
                // depends on foot rotation, and the two clips answer a single
                // world-space offset by different amounts, so no constant
                // grounds both exactly. This is the one number to nudge if the
                // hopper reads too high or sinks into the pavement.
                0.05f)
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
            return LoadPrefabs(OrderedArchetypes);
        }

        /// <summary>
        /// Spreads <paramref name="poolSize"/> pooled instances over the
        /// catalog in stable order: every design appears once, then the
        /// remainder is dealt round-robin while each design stays under its
        /// declared instance limit.
        /// </summary>
        public static IReadOnlyList<CityPedestrianArchetype>
            CreatePoolComposition(int poolSize)
        {
            if (poolSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(poolSize),
                    "A pedestrian pool requires at least one instance.");
            }

            if (poolSize < OrderedArchetypes.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(poolSize),
                    "A pedestrian pool must hold every registered design at " +
                    "least once.");
            }

            var counts = new int[OrderedArchetypes.Length];
            var composition =
                new List<CityPedestrianArchetype>(poolSize);
            for (int index = 0; index < OrderedArchetypes.Length; index++)
            {
                counts[index] = 1;
                composition.Add(OrderedArchetypes[index]);
            }

            int cursor = 0;
            while (composition.Count < poolSize)
            {
                bool dealt = false;
                for (int step = 0;
                     step < OrderedArchetypes.Length &&
                     composition.Count < poolSize;
                     step++)
                {
                    int index =
                        (cursor + step) % OrderedArchetypes.Length;
                    CityPedestrianArchetype archetype =
                        OrderedArchetypes[index];
                    if (counts[index] >= archetype.MaximumPoolInstances)
                    {
                        continue;
                    }

                    counts[index]++;
                    composition.Add(archetype);
                    cursor = index + 1;
                    dealt = true;
                    break;
                }

                if (!dealt)
                {
                    throw new InvalidOperationException(
                        $"A pool of {poolSize} pedestrian instances exceeds " +
                        "the total declared instance limits of the catalog.");
                }
            }

            return composition;
        }

        public static GameObject[] LoadPooledPrefabs(int poolSize)
        {
            return LoadPrefabs(CreatePoolComposition(poolSize));
        }

        private static GameObject[] LoadPrefabs(
            IReadOnlyList<CityPedestrianArchetype> archetypes)
        {
            var prefabs = new GameObject[archetypes.Count];
            for (int index = 0; index < archetypes.Count; index++)
            {
                CityPedestrianArchetype archetype = archetypes[index];
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
