using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// A design's declared permission to ride Route 01, with the numbers that
    /// place it on a seat. Every walker shares the hero's 31-bone rig and its
    /// `0.835 m` rest pelvis, so seating is one pelvis alignment for all of
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

        // The six ordinary residents. Promoted to the street on 2026-09-02
        // and taken off it again on 2026-09-16, when the pool became the
        // default NPC catalog; each keeps its placed role - the babushka
        // still beats her carpet, the watchman still holds the gate - and the
        // balcony smokers still wear these bodies. See `OrdinaryResidents`.
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
        /// <summary>
        /// The man on the мостки. He is a STORY figure and nothing else -
        /// taken off the street 2026-09-02 on the user's instruction
        /// ("рыбак - сюжетный персонаж, удали его из пула
        /// случайных NPC"), which is also what the rest of the catalog
        /// already implied: a shouldered rod rising `1.9 m` above his pelvis
        /// is why he could never board the bus either.
        ///
        /// He carries NO prefab path here on purpose. `SeacoastFishermanProvider`
        /// binds his prefab by guid out of `Assets/Pedestrians/Staged/`, where
        /// `CityPedestrianAssetSetup.ValidateDescriptorScope` requires every
        /// non-roaming staged design to keep it - a design absent from this
        /// catalog whose prefab sat in `Resources` would fail the asset build.
        /// </summary>
        public const string FishermanDesignId = "lake_fisherman_v1";

        // The legacy single-prefab entry point: the babushka, the most
        // ordinary body in the pedestrian library. Since 2026-09-16 nothing
        // in this library roams - the street pool is built from the default
        // NPC catalog - so this is what a staged vignette or a test asking
        // for "a pedestrian body with a registry" gets, not what the street
        // shows.
        public const string PrefabResourcePath =
            BabushkaPrefabResourcePath;

        /// <summary>
        /// The pooled walkers' pace. Every default NPC walks its own authored
        /// village gait - `Walk` covers `0.82 m/s` at unit speed over a
        /// `1.25 s` cycle - so the band is the same for every catalog model
        /// and the animation band brackets `speed / 0.82`.
        /// </summary>
        public const float DefaultNpcMinimumMovementSpeed = 0.78f;
        public const float DefaultNpcMaximumMovementSpeed = 0.90f;
        public const float DefaultNpcMinimumAnimationSpeed = 0.95f;
        public const float DefaultNpcMaximumAnimationSpeed = 1.10f;

        /// <summary>
        /// The library design whose authored seated loop and personal-space
        /// pair the default NPC walkers borrow. A default NPC carries its own
        /// idle and walk, but no seated citizen and no guard or shove were
        /// ever authored for it; the weigher's are "an ordinary seated
        /// citizen on a bus bench" on the same 31-bone NpcHumanV2 rig and the
        /// same `ROOT_Player/RIG_Player` paths, so they bind without
        /// retargeting. The prefab asset is read, never instantiated.
        /// </summary>
        public const string StreetClipDonorDesignId = WeighAttendantDesignId;

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
                new CityPedestrianSeatedRide(0.056f, 0.21f, 0.915f);

        /// <summary>
        /// Bony hips under a telogreika: the least of any rider, which is why
        /// he needs the smallest lift of the five.
        /// </summary>
        private static readonly CityPedestrianSeatedRide WatchmanSeatedRide =
            new CityPedestrianSeatedRide(0.052f, 0.20f, 0.909f);

        /// <summary>
        /// The default NPC borrows the weigher's seated clip. Its own trousers
        /// need a higher pelvis: measured against the actual cushion throughout
        /// the seated loop and suspension tilt, rather than the donor's mesh.
        /// </summary>
        private static readonly CityPedestrianSeatedRide DefaultNpcSeatedRide =
            new CityPedestrianSeatedRide(0.076f, 0.21f, 0.915f);

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
        /// The six ordinary residents of the pedestrian library. They walked
        /// the street from 2026-09-02, when the four strange walkers, the
        /// fisherman and the Chair Carrier came off it, until 2026-09-16, when
        /// the user closed the pool to everything but the default NPC catalog
        /// («прохожие берутся только из default NPC»). They stay here because
        /// they are still bodies with a citizen gait, a seated ride and a
        /// personal-space pair: the balcony smokers wear them, the weigher
        /// lends her seated loop and her guard/shove to the pooled default
        /// NPCs, and every placed role still resolves through
        /// `TryGetArchetype`.
        /// </summary>
        private static readonly CityPedestrianArchetype[] OrdinaryResidents =
        {
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
        };

        /// <summary>
        /// Designs that exist, resolve and may be placed by hand, but never
        /// enter the roaming pool.
        ///
        /// They are not dead weight and must not be deleted, though what
        /// defends them changed on 2026-09-02: the courtyard vignettes used
        /// to cast the Lampshade, Long-Arm and Chair Carrier by name and no
        /// longer do, so what is left is `MothersHouseKettleProp`, which
        /// instantiates the Kettle Hat walker whole in order to borrow its
        /// ten kettle renderers for the mother's teapot - delete him and the
        /// teapot goes with him - and the plain fact that a design withdrawn
        /// from the street is not a design deleted from the world.
        /// </summary>
        private static readonly CityPedestrianArchetype[] NonRoamingArchetypes =
        {
            // A man walking the whole city with an upside-down cafe chair on
            // his shoulders, ruled STRANGE by the user on 2026-09-02
            // ("стулоносец не нормальный") against the catalog's own
            // body-versus-prop rule, which had filed him as the one ordinary
            // man in the pool. The verdict moved with him - see
            // `NpcDesignAppearanceCatalog`. He keeps his seated ride and his
            // unlimited instances so that nothing about him changes except
            // that nobody meets him.
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

        /// <summary>
        /// The designs that ROAM: one archetype per default NPC catalog model,
        /// derived from <see cref="DefaultNpcCatalog.ModelIds"/> at first use
        /// so a model added to the catalog is on the pavement with no
        /// registration here. Its design ID is the catalog model ID and its
        /// prefab path is the catalog's; the body itself is built by
        /// `CityPedestrianDefaultNpcBody`, not loaded from a registry prefab.
        /// </summary>
        private static CityPedestrianArchetype[] roamingArchetypes;

        private static CityPedestrianArchetype[] RoamingArchetypes
        {
            get
            {
                if (roamingArchetypes == null)
                {
                    IReadOnlyList<string> models = DefaultNpcCatalog.ModelIds;
                    var built = new CityPedestrianArchetype[models.Count];
                    for (int index = 0; index < models.Count; index++)
                    {
                        built[index] = new CityPedestrianArchetype(
                            models[index],
                            DefaultNpcCatalog.GetResourcePath(models[index]),
                            DefaultNpcMinimumMovementSpeed,
                            DefaultNpcMaximumMovementSpeed,
                            DefaultNpcMinimumAnimationSpeed,
                            DefaultNpcMaximumAnimationSpeed,
                            CityPedestrianArchetype.UnlimitedPoolInstances,
                            0f,
                            DefaultNpcSeatedRide);
                    }

                    roamingArchetypes = built;
                }

                return roamingArchetypes;
            }
        }

        private static readonly IReadOnlyList<CityPedestrianArchetype>
            ReadOnlyOrdinaryResidents = Array.AsReadOnly(OrdinaryResidents);

        private static IReadOnlyList<CityPedestrianArchetype>
            readOnlyArchetypes;

        private static IReadOnlyList<CityPedestrianArchetype>
            readOnlyAllArchetypes;

        private static CityPedestrianArchetype[] Concat(
            params CityPedestrianArchetype[][] tables)
        {
            int total = 0;
            for (int index = 0; index < tables.Length; index++)
            {
                total += tables[index].Length;
            }

            var all = new CityPedestrianArchetype[total];
            int cursor = 0;
            for (int index = 0; index < tables.Length; index++)
            {
                Array.Copy(
                    tables[index],
                    0,
                    all,
                    cursor,
                    tables[index].Length);
                cursor += tables[index].Length;
            }

            return all;
        }

        /// <summary>What walks the street: the default NPC catalog.</summary>
        public static IReadOnlyList<CityPedestrianArchetype> Archetypes =>
            readOnlyArchetypes ??
            (readOnlyArchetypes = Array.AsReadOnly(RoamingArchetypes));

        /// <summary>
        /// The six library residents with a citizen gait, a seated ride and
        /// a personal-space pair - the balcony smokers' bodies and the clip
        /// donor's table. Off the street since 2026-09-16.
        /// </summary>
        public static IReadOnlyList<CityPedestrianArchetype>
            OrdinaryResidentArchetypes => ReadOnlyOrdinaryResidents;

        /// <summary>
        /// Every design the runtime can resolve, in a stable order: the
        /// street pool first, then the ordinary residents, then the rest.
        /// This is what a contract test that means "the whole catalog" should
        /// read; <see cref="Archetypes"/> means "what walks the street".
        /// </summary>
        public static IReadOnlyList<CityPedestrianArchetype> AllArchetypes =>
            readOnlyAllArchetypes ??
            (readOnlyAllArchetypes = Array.AsReadOnly(Concat(
                RoamingArchetypes,
                OrdinaryResidents,
                NonRoamingArchetypes)));

        /// <summary>
        /// Whether a design is on the street, as opposed to merely being
        /// resolvable. Since 2026-09-16 only a default NPC catalog model
        /// roams; `TryGetArchetype` answers the second question and searches
        /// every table.
        /// </summary>
        public static bool Roams(string designId)
        {
            return TryFind(RoamingArchetypes, designId, out _);
        }

        /// <summary>Whether a design is one of the six library residents.</summary>
        public static bool IsOrdinaryResident(string designId)
        {
            return TryFind(OrdinaryResidents, designId, out _);
        }

        /// <summary>
        /// The registry of the design that lends the pooled default NPCs
        /// their seated loop and personal-space pair, read off the prefab
        /// asset, or <c>null</c> when the prefab is missing.
        /// </summary>
        public static CityPedestrianAssetRegistry LoadStreetClipDonor()
        {
            return TryGetArchetype(
                       StreetClipDonorDesignId,
                       out CityPedestrianArchetype donor)
                ? LoadPrefab(donor)?.GetComponent<CityPedestrianAssetRegistry>()
                : null;
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

        /// <summary>
        /// The design of each pooled slot, in slot order. The population
        /// decides it, not a round-robin: slot <c>i</c> is the permanent
        /// walker `DefaultNpcPopulation.PedestrianId(i)`, whose model the
        /// whole-world allocation chose least-used first, so every catalog
        /// model appears at least once whenever the pool is large enough to
        /// hold them all. Reading it prepares the population, which loads the
        /// catalog prefabs; a warm-up wanting paths alone reads
        /// <see cref="CollectPooledPrefabResourcePaths"/>.
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

            if (poolSize > DefaultNpcPopulation.PedestrianCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(poolSize),
                    "The population registers " +
                    $"{DefaultNpcPopulation.PedestrianCount} permanent " +
                    "walkers; a larger pool would spawn people it does not " +
                    "know.");
            }

            var composition =
                new List<CityPedestrianArchetype>(poolSize);
            for (int index = 0; index < poolSize; index++)
            {
                string modelId = DefaultNpcPopulation
                    .GetAssignment(DefaultNpcPopulation.PedestrianId(index))
                    .ModelId;
                if (!TryFind(RoamingArchetypes, modelId, out var archetype))
                {
                    throw new InvalidOperationException(
                        $"The population assigned model '{modelId}' to " +
                        "a pooled walker, but the street catalog does not " +
                        "know it.");
                }

                composition.Add(archetype);
            }

            return composition;
        }

        /// <summary>
        /// The distinct prefab resource paths the pool of
        /// <paramref name="poolSize"/> may build a body from: every catalog
        /// model's, because the population may hand any of them to any slot.
        /// Read-only and prefab-free, so a warm-up can fetch them ahead of
        /// the pool without preparing the population itself.
        /// </summary>
        public static IReadOnlyList<string>
            CollectPooledPrefabResourcePaths(int poolSize)
        {
            if (poolSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(poolSize),
                    "A pedestrian pool requires at least one instance.");
            }

            CityPedestrianArchetype[] roaming = RoamingArchetypes;
            var paths = new List<string>(roaming.Length);
            for (int index = 0; index < roaming.Length; index++)
            {
                string path = roaming[index].PrefabResourcePath;
                if (!paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        public static bool TryGetArchetype(
            string designId,
            out CityPedestrianArchetype archetype)
        {
            // EVERY table, and that is the whole point of the split. Resolving
            // a design is not the same question as spawning one: the placed
            // residents, the mother's teapot and the courtyard vignettes ask
            // this about designs that deliberately never roam, and answering
            // `false` for them would throw at `MothersHouseKettleProp.Create`
            // and `CityCourtyardResidentFactory.ResolveArchetype`.
            return TryFind(RoamingArchetypes, designId, out archetype) ||
                   TryFind(OrdinaryResidents, designId, out archetype) ||
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
