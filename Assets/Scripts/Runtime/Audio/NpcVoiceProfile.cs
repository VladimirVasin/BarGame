using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One stable authored speaker identity. Russian phrases use an explicit
    /// source/filter preset independent of the legacy writing-blip parameters.
    /// Neither route may assign a different speaker or borrow the hero.
    /// </summary>
    public readonly struct NpcVoiceProfile
    {
        private readonly int vocalizerPreset;

        public NpcVoiceProfile(
            string id,
            float fundamentalHz,
            float timbreRatio,
            float noiseShare,
            float jitterCents,
            float volume,
            int vocalizerPreset = -1)
        {
            if (vocalizerPreset < -1 || vocalizerPreset >= RussianVocalizerSynthesis.VoiceCount)
                throw new System.ArgumentOutOfRangeException(nameof(vocalizerPreset));
            Id = id ?? string.Empty;
            FundamentalHz = fundamentalHz;
            TimbreRatio = timbreRatio;
            NoiseShare = noiseShare;
            JitterCents = jitterCents;
            Volume = volume;
            Hash = CitySoundStableHash.String(Id);
            // The legacy constructor uses an ordinary NPC preset; the catalog
            // assigns every production speaker explicitly, including the hero.
            this.vocalizerPreset = vocalizerPreset < 0 ? 0 : vocalizerPreset;
        }

        /// <summary>Short stable name, also the generated clip's
        /// name.</summary>
        public string Id { get; }

        /// <summary>Fundamental of the legacy writing blip.</summary>
        public float FundamentalHz { get; }

        /// <summary>Where the second partial sits. Deliberately
        /// inharmonic — a whole-number ratio would read as a musical
        /// note, and nobody in this game is singing.</summary>
        public float TimbreRatio { get; }

        /// <summary>`0` clean, up to about `0.24` for a dry throat.
        /// </summary>
        public float NoiseShare { get; }

        /// <summary>Per-blip detune spread, in cents. Keeps a repeated
        /// letter from being machine-identical without ever moving it
        /// off its own note.</summary>
        public float JitterCents { get; }

        /// <summary>Loudness at the source's minimum distance.</summary>
        public float Volume { get; }

        public uint Hash { get; }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        public RussianVocalizerGender Gender =>
            RussianVocalizerSynthesis.LabVoiceGenderAt(vocalizerPreset);

        /// <summary>
        /// Authored Russian style with this speaker's stable synthesis identity.
        /// Invalid/silent profiles remain invalid instead of receiving a preset.
        /// </summary>
        public RussianVocalizerVoice VocalizerVoice => IsValid
            ? RussianVocalizerSynthesis.LabVoiceAt(vocalizerPreset).WithIdentity(Hash)
            : default;
    }

    /// <summary>
    /// Every speaking design in the game and its stable authored tone.
    ///
    /// The table is AUTHORED rather than derived, because the point is
    /// that two men are told apart by ear. A hash is used only to place
    /// an UNLISTED design on one of the authored NPC voices of its gender —
    /// never to invent a new one, and never onto the hero's, which is
    /// his alone. This also keeps the fallback blip bank at a fixed size
    /// and gives any future speaking design a voice on the
    /// day it first opens its mouth.
    ///
    /// Reading of the table worth keeping: the two park players are the
    /// one pair heard alternating inside ten seconds, so they have to
    /// differ by voice and not only by which head the panel sits over —
    /// the chess man higher and glassier, the draughts man lower and
    /// woodier. Russian presets give the husband the lowest, rumbling voice
    /// and the cafe woman the highest, sharpest one. Dryness, resonance,
    /// articulation and breath are authored independently of blip pitch.
    ///
    /// The hero is the last entry and the only one here who is not an
    /// NPC. He is silent everywhere else in the game — narration and his
    /// own prompts carry <see cref="NpcVoiceCatalog.SilentOrdinal"/> —
    /// and this voice exists for one channel only: the drunk muttering
    /// above the balance threshold. He is the quietest in the table on
    /// purpose, because his own bubble is measured against himself as the
    /// listener and so never fades with distance.
    /// </summary>
    public static class NpcVoiceCatalog
    {
        /// <summary>One octave of steps, so the same letter is always
        /// the same note for a given speaker. That repetition is what
        /// makes a typed line read as TEXT rather than as noise.
        /// </summary>
        public const int SemitoneRange = 12;

        private const uint CharacterSalt = 0x4C455454u; // "LETT"
        private const uint FallbackSalt = 0x564F4943u;  // "VOIC"

        public const string WatchmanDesignId = "cemetery_watchman_v1";
        public const string FishermanDesignId = "lake_fisherman_v1";
        public const string FerrymanDesignId = "last_route_ferryman_v1";
        public const string ChessPlayerDesignId = "park_chess_player_v1";
        public const string CheckersPlayerDesignId =
            "park_checkers_player_v1";
        public const string CafeManDesignId = "cafe_couple_man_v2";
        public const string CafeWomanDesignId = "cafe_couple_woman_v2";
        public const string CafeHusbandDesignId = "cafe_lone_patron_v2";
        public const string MotherDesignId = "mother_v1";
        public const string YardBabushkaDesignId = "yard_babushka_v1";
        public const string MournerDesignId = "cemetery_mourner_v1";

        /// <summary>The hero's muttering voice. Not a design in the
        /// appearance catalog: he is the one speaker who is not an NPC.
        /// </summary>
        public const string HeroMutterDesignId = "hero_mutter_v1";

        private static readonly NpcVoiceProfile[] profiles =
        {
            // Legacy blip parameters stay fixed; Russian presets are explicit.
            //              id                Hz  timbre  noise  ¢    vol  RU
            new NpcVoiceProfile(
                "watchman", 168f, 2.02f, 0.16f, 22f, 0.26f, 2),
            new NpcVoiceProfile(
                "fisherman", 152f, 1.94f, 0.20f, 28f, 0.24f, 8),
            new NpcVoiceProfile(
                "ferryman", 196f, 2.38f, 0.10f, 16f, 0.26f, 7),
            new NpcVoiceProfile(
                "chess_player", 208f, 2.61f, 0.08f, 18f, 0.28f, 4),
            new NpcVoiceProfile(
                "checkers_player", 178f, 2.14f, 0.12f, 18f, 0.28f, 5),
            new NpcVoiceProfile(
                "cafe_man", 186f, 2.24f, 0.09f, 14f, 0.22f, 11),
            new NpcVoiceProfile(
                "cafe_woman", 262f, 2.72f, 0.05f, 12f, 0.22f, 1),
            new NpcVoiceProfile(
                "cafe_husband", 138f, 1.86f, 0.22f, 26f, 0.24f, 0),
            // Real existing female designs also supply the female-only fallback
            // bank. A new voice identity does not grant a silent actor speech.
            new NpcVoiceProfile(
                "mother", 202f, 2.16f, 0.14f, 20f, 0.23f, 10),
            new NpcVoiceProfile(
                "yard_babushka", 232f, 2.44f, 0.12f, 16f, 0.22f, 3),
            new NpcVoiceProfile(
                "mourner", 244f, 2.63f, 0.06f, 12f, 0.22f, 9),
            // Hero last and quietest, with his own creaky Russian preset.
            // The English blip retains its dry throat and widest detune.
            new NpcVoiceProfile(
                "hero_mutter", 146f, 1.90f, 0.24f, 34f, 0.20f, 6)
        };

        private static readonly string[] designIds =
        {
            WatchmanDesignId,
            FishermanDesignId,
            FerrymanDesignId,
            ChessPlayerDesignId,
            CheckersPlayerDesignId,
            CafeManDesignId,
            CafeWomanDesignId,
            CafeHusbandDesignId,
            MotherDesignId,
            YardBabushkaDesignId,
            MournerDesignId,
            HeroMutterDesignId
        };

        // These are voice ordinals, not lab preset indices. Neither pool
        // contains the hero; expanding one gender never changes the other pool.
        private static readonly int[] MaleFallbackOrdinals = { 0, 1, 2, 3, 4, 5, 7 };
        private static readonly int[] FemaleFallbackOrdinals = { 6, 8, 9, 10 };

        /// <summary>The ordinal a silent speaker carries: narration,
        /// the hero's own prompts, and anybody who has not been given a
        /// voice.</summary>
        public const int SilentOrdinal = -1;

        public static int Count => profiles.Length;

        /// <summary>
        /// Voices an unlisted design may fall onto. The hero is last and is
        /// NOT one of them: without this a future speaking NPC would have a
        /// chance of being handed the hero's own mutter, and the
        /// only symptom would be a stranger who sounds like him.
        /// </summary>
        public static int FallbackVoiceCount => profiles.Length - 1;

        /// <summary>The hero's ordinal in this table.</summary>
        public static int HeroMutterOrdinal => profiles.Length - 1;

        public static NpcVoiceProfile ProfileAt(int ordinal)
        {
            return ordinal < 0 || ordinal >= profiles.Length
                ? default
                : profiles[ordinal];
        }

        public static string DesignIdAt(int ordinal)
        {
            return ordinal < 0 || ordinal >= designIds.Length
                ? string.Empty
                : designIds[ordinal];
        }

        public static bool TryGetOrdinal(
            string designId,
            out int ordinal)
        {
            for (int index = 0; index < designIds.Length; index++)
            {
                if (string.Equals(
                        designIds[index],
                        designId,
                        System.StringComparison.Ordinal))
                {
                    ordinal = index;
                    return true;
                }
            }

            ordinal = SilentOrdinal;
            return false;
        }

        /// <summary>
        /// Authored first; an unlisted design is placed on one of the
        /// gender-qualified NPC voices by FNV-1a, which is deterministic and
        /// platform-stable where <c>string.GetHashCode</c> is neither.
        /// Never on the hero's — see <see cref="FallbackVoiceCount"/>.
        /// </summary>
        public static int ResolveOrdinal(string designId)
        {
            return ResolveOrdinal(designId, ResolveGender(designId));
        }

        /// <summary>
        /// Callers with model/population gender metadata supply it explicitly.
        /// A mismatched authored voice falls into that gender's bounded pool.
        /// </summary>
        public static int ResolveOrdinal(string designId, RussianVocalizerGender gender)
        {
            if (gender != RussianVocalizerGender.Male && gender != RussianVocalizerGender.Female)
                throw new System.ArgumentOutOfRangeException(nameof(gender));
            if (string.IsNullOrEmpty(designId))
            {
                return SilentOrdinal;
            }

            if (TryGetOrdinal(designId, out int authored) && profiles[authored].Gender == gender)
            {
                return authored;
            }

            uint hash = CitySoundStableHash.Combine(
                CitySoundStableHash.String(designId),
                FallbackSalt);
            int[] pool = gender == RussianVocalizerGender.Female
                ? FemaleFallbackOrdinals : MaleFallbackOrdinals;
            return pool[(int)(hash % (uint)pool.Length)];
        }

        /// <summary>
        /// Exact authored design/voice aliases only; never infer gender from a
        /// name substring. Unknown legacy identities use the ordinary male pool;
        /// new female identities supply their declared gender to ResolveOrdinal.
        /// </summary>
        public static RussianVocalizerGender ResolveGender(string designId)
        {
            if (TryGetOrdinal(designId, out int authored)) return profiles[authored].Gender;
            switch (designId)
            {
                case "weigh_attendant_v1":
                case "village-wood-woman":
                case "village-sewing-woman":
                case "village-basket-visitor":
                    return RussianVocalizerGender.Female;
                default:
                    return RussianVocalizerGender.Male;
            }
        }

        public static NpcVoiceProfile Resolve(string designId)
        {
            return ProfileAt(ResolveOrdinal(designId));
        }

        public static NpcVoiceProfile Resolve(string designId, RussianVocalizerGender gender) =>
            ProfileAt(ResolveOrdinal(designId, gender));

        /// <summary>
        /// Which of the twelve steps a letter writes on. Stable for a
        /// given letter, case-folded so «А» and «а» are the same key
        /// on the same typewriter.
        /// </summary>
        public static int ResolveCharacterStep(char value)
        {
            uint hash = CitySoundStableHash.Combine(
                CharacterSalt,
                char.ToLowerInvariant(value));
            return (int)(hash % (uint)SemitoneRange);
        }

        /// <summary>
        /// The pitch one blip is played at: the letter's own step, plus
        /// a small per-blip detune so a repeated letter is not
        /// mechanically identical. The clip is generated at the
        /// speaker's fundamental, so this multiplier is the whole of
        /// the per-letter melody.
        ///
        /// <paramref name="extraJitterCents"/> widens that detune for one
        /// line without touching the profile. It is the ONE knob a
        /// speaker's state can turn: the fundamental, the timbre and the
        /// noise are baked into the shared clip bank, so a voice that
        /// degraded in those would need a clip per degree. The hero's
        /// muttering spends it, and the rest of his drunkenness is heard
        /// through the slurred letters themselves.
        /// </summary>
        public static float ResolveBlipPitch(
            in NpcVoiceProfile voice,
            char character,
            uint ordinal,
            float extraJitterCents = 0f)
        {
            int step = ResolveCharacterStep(character);
            float centre = CitySoundStableHash.ToUnitFloat(
                               CitySoundStableHash.Combine(
                                   voice.Hash,
                                   ordinal)) *
                           2f -
                           1f;
            float cents = voice.JitterCents;
            if (!float.IsNaN(extraJitterCents))
            {
                cents += Mathf.Max(0f, extraJitterCents);
            }

            float semitones = step + cents * centre / 100f;
            return Mathf.Pow(2f, semitones / 12f);
        }
    }
}
