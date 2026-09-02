using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Whether one character design reads as an ordinary specimen of its own
    /// kind, or as something visibly wrong.
    /// </summary>
    public enum NpcDesignAppearance
    {
        /// <summary>
        /// An ordinary body. The design may still carry a strange object, a
        /// strange costume or a strange posture — a chair caged over the
        /// head, a chess king's tulle, an organ-piped wheelchair — and it
        /// stays <see cref="Normal"/> as long as the body under it is a
        /// believable one.
        /// </summary>
        Normal = 0,

        /// <summary>
        /// The BODY itself is wrong: proportions, limb count, a missing
        /// face, a neck that reaches across a room.
        /// </summary>
        Bizarre = 1,
    }

    /// <summary>
    /// Which of the game's character designs are ordinary and which are not.
    ///
    /// RUNTIME DOES NOT READ THIS YET. It is recorded now so that the day a
    /// pool wants only ordinary walkers, a reaction fires only on the wrong
    /// ones, or a per-zone tally needs it, the answer already exists and was
    /// decided once against the bibles rather than re-argued at the call site.
    /// Editor validation may assert the recorded verdict, but runtime model
    /// selection remains explicit in each provider.
    ///
    /// THE LINE IS THE ART BIBLE'S OWN, not a new invention. It names the
    /// axis exactly once, in the Long-Arm Walker's description
    /// (`ai/city-zones-art-bible.md:2712`):
    ///
    ///   «Он единственный, чья странность — само тело, а не надетый или
    ///   несомый предмет.»
    ///
    /// So: strangeness of the BODY is <see cref="NpcDesignAppearance.Bizarre"/>;
    /// a strange thing WORN OR CARRIED leaves the design
    /// <see cref="NpcDesignAppearance.Normal"/>. That is why the Chair
    /// Carrier and the Pipeback Roller are ordinary and the Kettle Hat
    /// Walker is not — the kettle is a hat, but the human mass under it
    /// stops at `1.40 m`.
    ///
    /// Animals are judged as ordinary specimens of their own species, which
    /// is why the cemetery raven is <see cref="NpcDesignAppearance.Normal"/>
    /// (the art bible insists on it: «Это обычные зимующие птицы… Никакой
    /// мистики») and the stairwell cat is not.
    ///
    /// WHY THIS IS A C# TABLE AND NOT A MANIFEST FIELD. The natural home is
    /// beside `signature_anatomy` in each model's own manifest, and that is
    /// where it should end up. It is not there yet because the generators
    /// have no manifest-only mode: `main()` always exports the FBX and saves
    /// the blend, so adding one JSON key would mean twenty-seven Blender
    /// runs rewriting twenty-eight tracked FBXs, blends and preview PNGs.
    /// Seventeen of the pedestrian manifests also carry a stale
    /// `generator_version`, which sits inside the build signature, so their
    /// signatures would move on rebuild whatever the new field did — and
    /// `CityPedestrianAssetSetup.ValidateDependencyStamp` would then dirty
    /// every pedestrian prefab. That is a large price for a marker nothing
    /// reads. Move it into `ArchetypeSpec` and the manifests on the day
    /// behaviour depends on it, when the rebuild buys something and a
    /// `GENERATOR_VERSION` bump is due anyway.
    ///
    /// Keyed on `design_id` because that is the one identifier that already
    /// appears in every manifest, every editor descriptor and every runtime
    /// registry. `NpcDesignAppearanceTests` asserts this table's key set
    /// equals the set of design ids actually on disk, so a design added
    /// without a verdict fails a test rather than defaulting silently.
    /// </summary>
    public static class NpcDesignAppearanceCatalog
    {
        private static readonly IReadOnlyDictionary<string, NpcDesignAppearance>
            ByDesignId = new Dictionary<string, NpcDesignAppearance>
            {
                // --- Pooled street walkers -------------------------------
                // No head geometry at all: the hood contains `GEO_FaceVoid`
                // and the manifest reports `head_height_m: 0`. The art bible
                // files the lampshade as worn, but what a player meets is a
                // figure with no face, and that is a fact about the body.
                ["lampshade_walker_v1"] = NpcDesignAppearance.Bizarre,

                // OVERRULED BY THE USER, 2026-09-02: «стулоносец не
                // нормальный». By the rule above he was the one design in
                // the pool with no bodily anomaly at all - an ordinary man
                // carrying an upside-down cafe chair - and the rule still
                // reads that way for the two park players, who wear a game
                // piece where a hat would be and stay ordinary. The
                // difference the user drew is that a chair is not worn and
                // not put down: a man who carries one through the whole city
                // is strange whatever his proportions are. He came off the
                // street in the same breath.
                ["chair_carrier_v1"] = NpcDesignAppearance.Bizarre,

                // The face is visible under the kettle and the kettle is a
                // hat — but the body is `10.9` heads tall with a shoulder
                // ratio of `1.64`, and the human mass ends at `1.40 m`.
                ["kettle_hat_walker_v1"] = NpcDesignAppearance.Bizarre,

                // The canonical case, and the one the bible's rule is
                // written about: mouthless, forearms to the ankles, hands
                // out of all proportion.
                ["long_arm_walker_v1"] = NpcDesignAppearance.Bizarre,

                // The helmet lamp is the ordinary worn object; the `0.46 m`
                // hind feet are not, and he never takes a step.
                ["helmet_lamp_hopper_v1"] = NpcDesignAppearance.Bizarre,

                // --- Staged residents ------------------------------------
                // Decided in `ai/architecture-notes.md:1532`: the strangeness
                // is the chair's organ pipes and bellows, never the rider's
                // disability. The rider is an ordinary seated man.
                ["pipeback_roller_v1"] = NpcDesignAppearance.Normal,

                ["yard_babushka_v1"] = NpcDesignAppearance.Normal,
                ["mother_v1"] = NpcDesignAppearance.Normal,
                ["weigh_attendant_v1"] = NpcDesignAppearance.Normal,
                ["cemetery_mourner_v1"] = NpcDesignAppearance.Normal,

                // The permanently raised brow is an expression, not anatomy.
                ["cemetery_watchman_v1"] = NpcDesignAppearance.Normal,

                ["lake_fisherman_v1"] = NpcDesignAppearance.Normal,

                // His eyes are never drawn, but by a cast shadow under a cap
                // brim — a costume fact. He is the near neighbour of the
                // Lampshade Walker and the reason the two verdicts differ:
                // one hides a face, the other has none.
                ["last_route_ferryman_v1"] = NpcDesignAppearance.Normal,

                // Both park players wear a game piece where a hat would be,
                // which is the Chair Carrier's category exactly.
                ["park_chess_player_v1"] = NpcDesignAppearance.Normal,
                ["park_checkers_player_v1"] = NpcDesignAppearance.Normal,

                // «Фигуры имеют обычные пропорции» — the art bible states it
                // outright for all three shelter residents.
                ["nightlife_shelter_standing_resident_v2"] =
                    NpcDesignAppearance.Normal,
                ["nightlife_shelter_seated_resident_v2"] =
                    NpcDesignAppearance.Normal,
                ["nightlife_shelter_sleeping_resident_v2"] =
                    NpcDesignAppearance.Normal,

                // Four ordinary adults in a Nighthawks tableau; everything
                // odd about them is social.
                ["cafe_lone_patron_v2"] = NpcDesignAppearance.Normal,
                ["cafe_couple_man_v2"] = NpcDesignAppearance.Normal,
                ["cafe_couple_woman_v2"] = NpcDesignAppearance.Normal,
                ["cafe_attendant_v2"] = NpcDesignAppearance.Normal,

                // --- Dedicated one-offs ----------------------------------
                ["six_armed_bartender_v1"] = NpcDesignAppearance.Bizarre,

                // The production supermarket clerk keeps the Watcher's
                // uniform and identity on an ordinary adult body, with a
                // fixed human neck instead of the pursuing periscope chain.
                ["supermarket_cashier_v1"] = NpcDesignAppearance.Normal,

                // Retained as a separate, dormant design rather than being
                // overwritten when the ordinary cashier becomes active.
                ["watcher_cashier_v1"] = NpcDesignAppearance.Bizarre,

                // Normal by the user's explicit call (2026-09-02), against a
                // first draft that had him bizarre for the eyes alone.
                // Everything else about the design argues their way: his
                // head is deliberately an "ordinary low-poly human" one,
                // the generator's own validator REFUSES any part that would
                // "replace or conceal the human head", and
                // `ai/architecture-notes.md:1109` files the long horizontal
                // eyes as "the slightly bizarre identity ... rather than
                // distorted anatomy". A stylised eye on an ordinary head is
                // nearer to a worn thing than to a wrong body.
                ["long_eyes_driver_v1"] = NpcDesignAppearance.Normal,

                // --- Animals ---------------------------------------------
                // «Это обычные зимующие птицы, какие живут при любом
                // провинциальном кладбище… Никакой мистики.»
                ["cemetery_raven_v1"] = NpcDesignAppearance.Normal,

                // A grin wider than his own head, and he talks.
                ["cheshire_stairwell_cat_v1"] = NpcDesignAppearance.Bizarre,
            };

        /// <summary>Every design id carrying a verdict.</summary>
        public static IEnumerable<string> DesignIds => ByDesignId.Keys;

        public static int Count => ByDesignId.Count;

        /// <summary>
        /// The verdict for one design, or <c>false</c> when the id is not a
        /// character design this catalog covers — the hero's two models and
        /// the park chess pieces among them.
        /// </summary>
        public static bool TryGet(
            string designId,
            out NpcDesignAppearance appearance)
        {
            if (string.IsNullOrEmpty(designId))
            {
                appearance = NpcDesignAppearance.Normal;
                return false;
            }

            return ByDesignId.TryGetValue(designId, out appearance);
        }

        /// <summary>
        /// Convenience for the common question, which is only ever asked
        /// about a design already known to be in the catalog. An unknown id
        /// answers <c>false</c> rather than throwing, because "is this one
        /// of the wrong ones" has a sane answer for a thing that is not a
        /// character at all.
        /// </summary>
        public static bool IsBizarre(string designId)
        {
            return TryGet(designId, out NpcDesignAppearance appearance) &&
                   appearance == NpcDesignAppearance.Bizarre;
        }
    }
}
