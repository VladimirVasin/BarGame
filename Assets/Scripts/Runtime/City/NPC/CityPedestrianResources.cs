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
            CityPedestrianSeatedRide seatedRide,
            bool carriesBoilingKettle = false)
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
            CarriesBoilingKettle = carriesBoilingKettle;
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

        /// <summary>
        /// True for the one design whose headwear is a kettle on the boil.
        /// Declared here, on the descriptor and in the model manifest alike,
        /// the way the hopper's lamp is: the factory attaches the always-on
        /// boil effect only to a design that says so, and refuses a prefab
        /// whose rig anchors disagree with its catalog entry.
        /// </summary>
        public bool CarriesBoilingKettle { get; }

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

        // The seven ordinary residents promoted to the street (2026-09-02).
        // Each keeps its placed role as well: the babushka still beats her
        // carpet in the drying yard, the watchman still holds the cemetery
        // gate. Only their roaming copies read the ambient gait.
        public const string BabushkaDesignId = "yard_babushka_v1";
        public const string BabushkaPrefabResourcePath =
            "Pedestrians/YardBabushka3D";
        public const string WeighAttendantDesignId = "weigh_attendant_v1";
        public const string WeighAttendantPrefabResourcePath =
            "Pedestrians/WeighbridgeAttendant3D";
        public const string WatchmanDesignId = "cemetery_watchman_v1";
        public const string WatchmanPrefabResourcePath =
            "Pedestrians/CemeteryWatchman3D";
        public const string ChessPlayerDesignId = "park_chess_player_v1";
        public const string ChessPlayerPrefabResourcePath =
            "Pedestrians/ParkChessPlayer3D";
        public const string CheckersPlayerDesignId =
            "park_checkers_player_v1";
        public const string CheckersPlayerPrefabResourcePath =
            "Pedestrians/ParkCheckersPlayer3D";
        public const string MournerDesignId = "cemetery_mourner_v1";
        public const string MournerPrefabResourcePath =
            "Pedestrians/CemeteryMourner3D";
        public const string FishermanDesignId = "lake_fisherman_v1";
        public const string FishermanPrefabResourcePath =
            "Pedestrians/LakeFisherman3D";

        // The legacy single-prefab entry point. It used to resolve to the
        // Lampshade Walker, which no longer roams - a caller asking for "a
        // pedestrian" with no further qualification must get one that is
        // actually on the street.
        public const string PrefabResourcePath =
            ChairCarrierPrefabResourcePath;

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

        /// <summary>
        /// The quilted jacket is the bulkiest thing on the cushion, so she
        /// sits a little further back than the rest.
        /// </summary>
        private static readonly CityPedestrianSeatedRide
            WeighAttendantSeatedRide =
                new CityPedestrianSeatedRide(0.044f, 0.21f, 0.915f);

        /// <summary>
        /// Bony hips under a telogreika: the least of any rider, which is why
        /// he needs the smallest lift of the five.
        /// </summary>
        private static readonly CityPedestrianSeatedRide WatchmanSeatedRide =
            new CityPedestrianSeatedRide(0.038f, 0.20f, 0.909f);

        // THREE ORDINARY RESIDENTS DELIBERATELY DO NOT RIDE. The band above
        // is measured from the design's own seated clip, and for these three
        // the measurement is dominated by something that is not a hip: the
        // mourner's coat hem hangs `0.426 m` below the pelvis bone, the
        // babushka's housecoat `0.335 m`, and the fisherman's shouldered rod
        // rises `1.905 m` above it. Lifting any of them by their own contact
        // distance would float the body; not lifting them would drive the
        // garment through the cushion. They walk the street and wait at the
        // stop, and the bus passes them by.

        /// <summary>
        /// The designs that ROAM. Ordinary people only, since 2026-09-02:
        /// the four strange walkers were taken off the street and seven
        /// ordinary residents took their places.
        ///
        /// This is no longer the whole catalog. Anything that merely needs to
        /// RESOLVE a design - the courtyard vignettes, the mother's teapot -
        /// must go through `TryGetArchetype`, which also searches
        /// `NonRoamingArchetypes`.
        /// </summary>
        private static readonly CityPedestrianArchetype[] OrderedArchetypes =
        {
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
            new CityPedestrianArchetype(
                BabushkaDesignId,
                BabushkaPrefabResourcePath,
                0.78f,
                0.90f,
                0.90f,
                0.98f),
            new CityPedestrianArchetype(
                WeighAttendantDesignId,
                WeighAttendantPrefabResourcePath,
                1.02f,
                1.16f,
                0.96f,
                1.04f,
                CityPedestrianArchetype.UnlimitedPoolInstances,
                0f,
                WeighAttendantSeatedRide),
            new CityPedestrianArchetype(
                WatchmanDesignId,
                WatchmanPrefabResourcePath,
                0.92f,
                1.04f,
                0.94f,
                1.02f,
                CityPedestrianArchetype.UnlimitedPoolInstances,
                0f,
                WatchmanSeatedRide),
            new CityPedestrianArchetype(
                ChessPlayerDesignId,
                ChessPlayerPrefabResourcePath,
                0.86f,
                0.98f,
                0.92f,
                1.00f),
            new CityPedestrianArchetype(
                CheckersPlayerDesignId,
                CheckersPlayerPrefabResourcePath,
                0.88f,
                1.00f,
                0.93f,
                1.01f),
            new CityPedestrianArchetype(
                MournerDesignId,
                MournerPrefabResourcePath,
                0.82f,
                0.94f,
                0.90f,
                0.98f),
            new CityPedestrianArchetype(
                FishermanDesignId,
                FishermanPrefabResourcePath,
                1.00f,
                1.14f,
                0.96f,
                1.04f),
        };

        /// <summary>
        /// Designs that exist, resolve and may be placed by hand, but never
        /// enter the roaming pool.
        ///
        /// They are not dead weight and must not be deleted: the courtyard
        /// vignettes cast the Lampshade, Long-Arm and Chair Carrier by name,
        /// and `MothersHouseKettleProp` instantiates the Kettle Hat walker
        /// whole in order to borrow its ten kettle renderers for the
        /// mother's teapot.
        /// </summary>
        private static readonly CityPedestrianArchetype[] NonRoamingArchetypes =
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
                KettleHatSeatedRide,
                carriesBoilingKettle: true),
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

        private static readonly IReadOnlyList<CityPedestrianArchetype>
            ReadOnlyAllArchetypes = Array.AsReadOnly(
                Concat(OrderedArchetypes, NonRoamingArchetypes));

        private static CityPedestrianArchetype[] Concat(
            CityPedestrianArchetype[] first,
            CityPedestrianArchetype[] second)
        {
            var all = new CityPedestrianArchetype[
                first.Length + second.Length];
            Array.Copy(first, 0, all, 0, first.Length);
            Array.Copy(second, 0, all, first.Length, second.Length);
            return all;
        }

        public static IReadOnlyList<CityPedestrianArchetype> Archetypes =>
            ReadOnlyArchetypes;

        /// <summary>
        /// Every design the library can resolve, roaming or not, in a stable
        /// order: the street pool first, then the rest. This is what a
        /// contract test that means "the whole catalog" should read;
        /// <see cref="Archetypes"/> means "what walks the street", and the
        /// two stopped being the same thing on 2026-09-02.
        /// </summary>
        public static IReadOnlyList<CityPedestrianArchetype> AllArchetypes =>
            ReadOnlyAllArchetypes;

        /// <summary>
        /// Whether a design is on the street, as opposed to merely being
        /// resolvable. `TryGetArchetype` answers the second question and
        /// searches both tables; this one answers the first.
        ///
        /// The distinction only started to matter when the two tables
        /// stopped being the same thing: the Kettle Hat walker still has to
        /// resolve, because the mother's teapot is built out of him, but he
        /// no longer roams.
        /// </summary>
        public static bool Roams(string designId)
        {
            return TryFind(OrderedArchetypes, designId, out _);
        }

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
            // BOTH lists, and that is the whole point of the split. Resolving
            // a design is not the same question as spawning one: the mother's
            // teapot and the courtyard vignettes ask this about designs that
            // deliberately never roam, and answering `false` for them would
            // throw at `MothersHouseKettleProp.Create` and
            // `CityCourtyardResidentFactory.ResolveArchetype`.
            return TryFind(OrderedArchetypes, designId, out archetype) ||
                   TryFind(NonRoamingArchetypes, designId, out archetype);
        }

        private static bool TryFind(
            CityPedestrianArchetype[] catalog,
            string designId,
            out CityPedestrianArchetype archetype)
        {
            for (int index = 0; index < catalog.Length; index++)
            {
                CityPedestrianArchetype candidate = catalog[index];
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
