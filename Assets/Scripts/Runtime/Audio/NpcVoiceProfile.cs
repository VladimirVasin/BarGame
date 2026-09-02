using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One speaker's tonality: the note his blip is generated at and
    /// how rough it is. Not a voice — there are no phonemes, no words
    /// and no intonation anywhere in this family. It is the sound of a
    /// letter being written, and the only thing it carries about the
    /// man is how high and how dry he is.
    /// </summary>
    public readonly struct NpcVoiceProfile
    {
        public NpcVoiceProfile(
            string id,
            float fundamentalHz,
            float timbreRatio,
            float noiseShare,
            float jitterCents,
            float volume)
        {
            Id = id ?? string.Empty;
            FundamentalHz = fundamentalHz;
            TimbreRatio = timbreRatio;
            NoiseShare = noiseShare;
            JitterCents = jitterCents;
            Volume = volume;
            Hash = CitySoundStableHash.String(Id);
        }

        /// <summary>Short stable name, also the generated clip's
        /// name.</summary>
        public string Id { get; }

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
    }

    /// <summary>
    /// Every speaking design in the game and the tone it writes in.
    ///
    /// The table is AUTHORED rather than derived, because the point is
    /// that two men are told apart by ear. A hash is used only to place
    /// an UNLISTED design on one of these eight known-good voices —
    /// never to invent a ninth. That keeps the clip bank at a fixed
    /// eight clips for the whole game, and gives any future speaking
    /// design a real voice on the day it first opens its mouth.
    ///
    /// Reading of the table worth keeping: the two park players are the
    /// one pair heard alternating inside ten seconds, so they have to
    /// differ by voice and not only by which head the panel sits over —
    /// the chess man higher and glassier, the draughts man lower and
    /// woodier. The husband is the lowest and grittiest in the game,
    /// because he is very drunk and face-down on his own forearms. The
    /// cafe woman is the only voice above `240 Hz`, and that is the
    /// whole of this system's gender coding, deliberately no more.
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

        private static readonly NpcVoiceProfile[] profiles =
        {
            //              id                Hz  timbre  noise  ¢    vol
            new NpcVoiceProfile(
                "watchman", 168f, 2.02f, 0.16f, 22f, 0.26f),
            new NpcVoiceProfile(
                "fisherman", 152f, 1.94f, 0.20f, 28f, 0.24f),
            new NpcVoiceProfile(
                "ferryman", 196f, 2.38f, 0.10f, 16f, 0.26f),
            new NpcVoiceProfile(
                "chess_player", 208f, 2.61f, 0.08f, 18f, 0.28f),
            new NpcVoiceProfile(
                "checkers_player", 178f, 2.14f, 0.12f, 18f, 0.28f),
            new NpcVoiceProfile(
                "cafe_man", 186f, 2.24f, 0.09f, 14f, 0.22f),
            new NpcVoiceProfile(
                "cafe_woman", 262f, 2.72f, 0.05f, 12f, 0.22f),
            new NpcVoiceProfile(
                "cafe_husband", 138f, 1.86f, 0.22f, 26f, 0.24f)
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
            CafeHusbandDesignId
        };

        /// <summary>The ordinal a silent speaker carries: narration,
        /// the hero's own prompts, and anybody who has not been given a
        /// voice.</summary>
        public const int SilentOrdinal = -1;

        public static int Count => profiles.Length;

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
        /// eight by FNV-1a, which is deterministic and platform-stable
        /// where <c>string.GetHashCode</c> is neither.
        /// </summary>
        public static int ResolveOrdinal(string designId)
        {
            if (string.IsNullOrEmpty(designId))
            {
                return SilentOrdinal;
            }

            if (TryGetOrdinal(designId, out int authored))
            {
                return authored;
            }

            uint hash = CitySoundStableHash.Combine(
                CitySoundStableHash.String(designId),
                FallbackSalt);
            return (int)(hash % (uint)profiles.Length);
        }

        public static NpcVoiceProfile Resolve(string designId)
        {
            return ProfileAt(ResolveOrdinal(designId));
        }

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
        /// </summary>
        public static float ResolveBlipPitch(
            in NpcVoiceProfile voice,
            char character,
            uint ordinal)
        {
            int step = ResolveCharacterStep(character);
            float centre = CitySoundStableHash.ToUnitFloat(
                               CitySoundStableHash.Combine(
                                   voice.Hash,
                                   ordinal)) *
                           2f -
                           1f;
            float semitones =
                step + voice.JitterCents * centre / 100f;
            return Mathf.Pow(2f, semitones / 12f);
        }
    }
}
