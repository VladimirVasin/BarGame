using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum RussianSpeechSound
    {
        Pause, A, O, U, I, Y, E, P, B, T, D, K, G, F, V, S, Z, Sh, Zh,
        Sch, Ch, Ts, H, M, N, L, R, J
    }

    public readonly struct RussianSpeechPhone
    {
        public readonly RussianSpeechSound Sound;
        public readonly bool Soft;
        public readonly bool Stressed;
        public readonly float DurationSeconds;
        public readonly RussianSpeechSound CanonicalSound;
        public readonly int TextStart;
        public readonly int TextEnd;
        public readonly int WordIndex;

        public RussianSpeechPhone(RussianSpeechSound sound, bool soft, bool stressed,
            float durationSeconds)
            : this(sound, soft, stressed, durationSeconds, sound, -1, -1, 0) { }

        public RussianSpeechPhone(RussianSpeechSound sound, bool soft, bool stressed,
            float durationSeconds, RussianSpeechSound canonicalSound, int textStart, int textEnd, int wordIndex)
        {
            Sound = sound;
            Soft = soft;
            Stressed = stressed;
            DurationSeconds = durationSeconds;
            CanonicalSound = canonicalSound;
            TextStart = textStart;
            TextEnd = textEnd;
            WordIndex = wordIndex;
        }
    }

    /// <summary>
    /// Bounded, approximate Russian pronunciation shared by speech and the menu lab.
    /// No clock, audio source or NPC delivery is owned here. The small stress lexicon
    /// covers the existing sample lines; unknown words use the penultimate vowel.
    /// A combining acute accent after a vowel overrides that guess. This is not a
    /// dictionary of Russian pronunciation: cross-word assimilation and homographs
    /// without an explicit stress mark remain intentionally unresolved.
    /// </summary>
    public static class RussianSpeechPhonetics
    {
        public const int MaximumTextLength = 240;

        // Zero-based vowel ordinal, rather than a letter offset: ё is retained.
        private static readonly Dictionary<string, int> Stress =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "хорошо", 2 }, { "сейчас", 1 }, { "подам", 1 },
                { "маршрут", 1 }, { "отменили", 2 }, { "равно", 1 },
                { "бензин", 1 }, { "повода", 0 }, { "последний", 1 },
                { "уже", 1 }, { "или", 0 }, { "будет", 0 },
                { "привет", 1 }, { "спасибо", 1 }, { "пожалуйста", 1 },
                { "здравствуй", 0 }, { "здравствуйте", 0 }, { "сегодня", 1 },
                { "завтра", 0 }, { "вчера", 1 }, { "потом", 1 },
                { "теперь", 1 }, { "немного", 1 }, { "домой", 1 },
                { "говорит", 2 }, { "говорить", 2 }, { "человек", 2 },
                { "человека", 2 }, { "можно", 0 }, { "нельзя", 1 },
                { "нужно", 0 }, { "семья", 1 }, { "семя", 0 },
                { "меня", 1 }, { "тебя", 1 }, { "себя", 1 },
                { "меню", 1 }, { "голос", 0 }, { "голоса", 2 },
                { "первый", 0 }, { "второй", 1 }, { "фраза", 0 },
                { "фразы", 0 }, { "молоко", 2 }, { "дорога", 1 },
                { "работа", 1 }, { "работы", 1 }, { "ничего", 2 },
                { "чего", 1 }, { "кого", 1 }, { "того", 1 },
                { "этого", 0 }, { "его", 1 }, { "почему", 2 },
                { "потому", 2 }, { "ещё", 1 }, { "жду", 0 },
                { "есть", 0 }, { "нет", 0 }, { "рейс", 0 },
                { "был", 0 }, { "я", 0 }, { "всё", 0 }
            };

        /// <summary>
        /// Allocation-free admission check; silent signs alone are not speech.
        /// Ignored text beyond the input bound cannot admit audio.
        /// </summary>
        public static bool HasRussianSpeech(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int length = Math.Min(text.Length, MaximumTextLength);
            for (int i = 0; i < length; i++)
            {
                char value = text[i];
                if (((value >= 'а' && value <= 'я') || (value >= 'А' && value <= 'Я') ||
                    value == 'ё' || value == 'Ё') && value != 'ь' && value != 'ъ' &&
                    value != 'Ь' && value != 'Ъ') return true;
            }
            return false;
        }

        public static IReadOnlyList<RussianSpeechPhone> Plan(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<RussianSpeechPhone>();
            string bounded = text.Substring(0, Math.Min(text.Length, MaximumTextLength))
                .ToLowerInvariant();
            var phones = new List<RussianSpeechPhone>(bounded.Length + 8);
            float pendingPause = 0f;
            int pendingStart = 0;
            int wordIndex = 0;
            int index = 0;
            while (index < bounded.Length)
            {
                char value = bounded[index];
                if (!IsRussianLetter(value))
                {
                    if (pendingPause <= 0f) pendingStart = index;
                    pendingPause = Math.Max(pendingPause, PauseDuration(value));
                    index++;
                    continue;
                }

                int wordStart = index++;
                while (index < bounded.Length &&
                    (IsRussianLetter(bounded[index]) || bounded[index] == '\u0301')) index++;
                string markedWord = bounded.Substring(wordStart, index - wordStart);
                int start = phones.Count;
                bool hasPause = start > 0 && pendingPause > 0f;
                if (hasPause) phones.Add(new RussianSpeechPhone(
                    RussianSpeechSound.Pause, false, false, pendingPause,
                    RussianSpeechSound.Pause, pendingStart, wordStart, wordIndex));
                int wordPhoneStart = phones.Count;
                AppendWord(markedWord, phones, wordStart, wordIndex);
                if (phones.Count == wordPhoneStart)
                {
                    if (hasPause) phones.RemoveAt(start);
                }
                else
                {
                    AssimilateWord(phones, wordPhoneStart);
                    pendingPause = 0f;
                    wordIndex++;
                }
            }
            return phones;
        }

        private static void AppendWord(string markedWord, List<RussianSpeechPhone> phones,
            int textOffset, int wordIndex)
        {
            string word = markedWord.Replace("\u0301", string.Empty);
            var offsets = new int[word.Length];
            int compact = 0;
            for (int raw = 0; raw < markedWord.Length; raw++)
                if (markedWord[raw] != '\u0301') offsets[compact++] = textOffset + raw;
            int stress = FindStress(markedWord, word);
            int vowelOrdinal = 0;
            for (int i = 0; i < word.Length; i++)
            {
                char value = word[i];
                if (value == 'ь' || value == 'ъ') continue;
                int start = offsets[i];
                int nextLetter = i + 1;
                while (nextLetter < word.Length && (word[nextLetter] == 'ь' || word[nextLetter] == 'ъ')) nextLetter++;
                int end = nextLetter < word.Length ? offsets[nextLetter] : textOffset + markedWord.Length;
                if (IsVowel(value))
                {
                    char previous = i > 0 ? word[i - 1] : '\0';
                    bool iotated = value == 'е' || value == 'ё' || value == 'ю' || value == 'я';
                    bool addJ = iotated && (i == 0 || IsVowel(previous) ||
                        previous == 'ь' || previous == 'ъ');
                    // After a separating ь, и also begins with a short й (соловьи).
                    if (value == 'и' && previous == 'ь') addJ = true;
                    if (addJ) Add(phones, RussianSpeechSound.J, true, 0f, start, end, wordIndex);
                    bool soft = addJ || (i > 0 && !IsVowel(previous) &&
                        phones.Count > 0 && phones[phones.Count - 1].Soft);
                    bool stressed = vowelOrdinal++ == stress;
                    RussianSpeechSound vowel = Vowel(value, stressed, soft, previous);
                    phones.Add(new RussianSpeechPhone(vowel, soft, stressed,
                        stressed ? .145f : .087f, Vowel(value, true, soft, previous), start, end, wordIndex));
                    continue;
                }

                // The frequent infinitive/reflexive ending is one affricate, not т-с.
                if (value == 'т' && (Matches(word, i, "тся") || Matches(word, i, "ться")))
                {
                    int consumed = word[i + 1] == 'ь' ? 3 : 2;
                    Add(phones, RussianSpeechSound.Ts, false, .125f, start, offsets[i + consumed], wordIndex);
                    i += word[i + 1] == 'ь' ? 2 : 1;
                    continue;
                }
                if ((value == 'с' || value == 'з') && i + 1 < word.Length && word[i + 1] == 'ч')
                {
                    Add(phones, RussianSpeechSound.Sch, true, 0f, start,
                        i + 2 < word.Length ? offsets[i + 2] : textOffset + markedWord.Length, wordIndex);
                    i++;
                    continue;
                }

                RussianSpeechSound sound = Consonant(value);
                // A few ordinary lexical spellings are misleading in otherwise useful samples.
                if (value == 'ч' && (word == "что" || word == "чтобы")) sound = RussianSpeechSound.Sh;
                if (value == 'г' && (word == "его" || word == "этого" || word == "ничего" ||
                    word == "чего" || word == "кого" || word == "того" || word == "сегодня"))
                    sound = RussianSpeechSound.V;
                char next = i + 1 < word.Length ? word[i + 1] : '\0';
                bool softened = sound == RussianSpeechSound.Ch || sound == RussianSpeechSound.Sch ||
                    sound == RussianSpeechSound.J || (CanSoften(sound) &&
                    (next == 'ь' || next == 'е' || next == 'ё' || next == 'и' ||
                     next == 'ю' || next == 'я'));
                Add(phones, sound, softened, 0f, start, end, wordIndex);
            }
        }

        private static int FindStress(string markedWord, string word)
        {
            int ordinal = -1;
            for (int i = 0; i < markedWord.Length; i++)
            {
                if (IsVowel(markedWord[i])) ordinal++;
                else if (markedWord[i] == '\u0301' && i > 0 && IsVowel(markedWord[i - 1]))
                    return ordinal;
            }
            ordinal = 0;
            for (int i = 0; i < word.Length; i++)
            {
                if (word[i] == 'ё') return ordinal;
                if (IsVowel(word[i])) ordinal++;
            }
            if (Stress.TryGetValue(word, out int known)) return known;
            return Math.Max(0, ordinal - 2);
        }

        private static RussianSpeechSound Vowel(char value, bool stressed, bool soft, char previous)
        {
            bool hardHiss = previous == 'ж' || previous == 'ш' || previous == 'ц';
            switch (value)
            {
                case 'о': return stressed ? RussianSpeechSound.O : RussianSpeechSound.A;
                case 'ё': return RussianSpeechSound.O;
                case 'у': case 'ю': return RussianSpeechSound.U;
                case 'и': return hardHiss ? RussianSpeechSound.Y : RussianSpeechSound.I;
                case 'ы': return RussianSpeechSound.Y;
                case 'э': return RussianSpeechSound.E;
                case 'е': return stressed ? RussianSpeechSound.E :
                    hardHiss ? RussianSpeechSound.Y : RussianSpeechSound.I;
                default: return !stressed && soft ? RussianSpeechSound.I : RussianSpeechSound.A;
            }
        }

        private static RussianSpeechSound Consonant(char value)
        {
            switch (value)
            {
                case 'б': return RussianSpeechSound.B; case 'п': return RussianSpeechSound.P;
                case 'в': return RussianSpeechSound.V; case 'ф': return RussianSpeechSound.F;
                case 'г': return RussianSpeechSound.G; case 'к': return RussianSpeechSound.K;
                case 'д': return RussianSpeechSound.D; case 'т': return RussianSpeechSound.T;
                case 'ж': return RussianSpeechSound.Zh; case 'ш': return RussianSpeechSound.Sh;
                case 'з': return RussianSpeechSound.Z; case 'с': return RussianSpeechSound.S;
                case 'щ': return RussianSpeechSound.Sch; case 'ч': return RussianSpeechSound.Ch;
                case 'ц': return RussianSpeechSound.Ts; case 'х': return RussianSpeechSound.H;
                case 'м': return RussianSpeechSound.M; case 'н': return RussianSpeechSound.N;
                case 'л': return RussianSpeechSound.L; case 'р': return RussianSpeechSound.R;
                default: return RussianSpeechSound.J;
            }
        }

        private static void AssimilateWord(List<RussianSpeechPhone> phones, int start)
        {
            for (int i = phones.Count - 1; i >= start; i--)
            {
                RussianSpeechPhone phone = phones[i];
                if (!IsPairedObstruent(phone.Sound)) continue;
                RussianSpeechSound sound = phone.Sound;
                if (i == phones.Count - 1) sound = Devoice(sound);
                else
                {
                    RussianSpeechSound next = phones[i + 1].Sound;
                    if (IsUnvoicedObstruent(next)) sound = Devoice(sound);
                    else if (IsPairedObstruent(next) && next != RussianSpeechSound.V)
                        sound = Voice(sound);
                }
                phones[i] = new RussianSpeechPhone(sound, phone.Soft, phone.Stressed, phone.DurationSeconds,
                    sound, phone.TextStart, phone.TextEnd, phone.WordIndex);
            }
        }

        private static bool IsPairedObstruent(RussianSpeechSound sound) =>
            sound == RussianSpeechSound.P || sound == RussianSpeechSound.B ||
            sound == RussianSpeechSound.T || sound == RussianSpeechSound.D ||
            sound == RussianSpeechSound.K || sound == RussianSpeechSound.G ||
            sound == RussianSpeechSound.F || sound == RussianSpeechSound.V ||
            sound == RussianSpeechSound.S || sound == RussianSpeechSound.Z ||
            sound == RussianSpeechSound.Sh || sound == RussianSpeechSound.Zh;

        private static bool IsUnvoicedObstruent(RussianSpeechSound sound) =>
            (IsPairedObstruent(sound) && Devoice(sound) == sound) ||
            sound == RussianSpeechSound.Sch || sound == RussianSpeechSound.Ch ||
            sound == RussianSpeechSound.Ts || sound == RussianSpeechSound.H;

        private static RussianSpeechSound Devoice(RussianSpeechSound sound)
        {
            switch (sound)
            {
                case RussianSpeechSound.B: return RussianSpeechSound.P;
                case RussianSpeechSound.D: return RussianSpeechSound.T;
                case RussianSpeechSound.G: return RussianSpeechSound.K;
                case RussianSpeechSound.V: return RussianSpeechSound.F;
                case RussianSpeechSound.Z: return RussianSpeechSound.S;
                case RussianSpeechSound.Zh: return RussianSpeechSound.Sh;
                default: return sound;
            }
        }

        private static RussianSpeechSound Voice(RussianSpeechSound sound)
        {
            switch (sound)
            {
                case RussianSpeechSound.P: return RussianSpeechSound.B;
                case RussianSpeechSound.T: return RussianSpeechSound.D;
                case RussianSpeechSound.K: return RussianSpeechSound.G;
                case RussianSpeechSound.F: return RussianSpeechSound.V;
                case RussianSpeechSound.S: return RussianSpeechSound.Z;
                case RussianSpeechSound.Sh: return RussianSpeechSound.Zh;
                default: return sound;
            }
        }

        private static void Add(List<RussianSpeechPhone> phones, RussianSpeechSound sound,
            bool soft, float duration = 0f, int textStart = -1, int textEnd = -1, int wordIndex = 0)
        {
            if (duration <= 0f)
            {
                switch (sound)
                {
                    case RussianSpeechSound.P: case RussianSpeechSound.B:
                    case RussianSpeechSound.T: case RussianSpeechSound.D:
                    case RussianSpeechSound.K: case RussianSpeechSound.G: duration = .066f; break;
                    case RussianSpeechSound.J: duration = .045f; break;
                    case RussianSpeechSound.Ch: case RussianSpeechSound.Ts: duration = .105f; break;
                    case RussianSpeechSound.Sch: duration = .135f; break;
                    case RussianSpeechSound.Sh: case RussianSpeechSound.Zh: duration = .105f; break;
                    default: duration = .078f; break;
                }
            }
            phones.Add(new RussianSpeechPhone(sound, soft, false, duration, sound, textStart, textEnd, wordIndex));
        }

        private static bool CanSoften(RussianSpeechSound sound) =>
            sound != RussianSpeechSound.Sh && sound != RussianSpeechSound.Zh &&
            sound != RussianSpeechSound.Ts;

        private static bool Matches(string word, int start, string suffix) =>
            word.Length - start == suffix.Length &&
            string.CompareOrdinal(word, start, suffix, 0, suffix.Length) == 0;

        private static bool IsRussianLetter(char value) =>
            (value >= 'а' && value <= 'я') || value == 'ё';

        private static bool IsVowel(char value) => value == 'а' || value == 'о' || value == 'у' ||
            value == 'и' || value == 'ы' || value == 'э' || value == 'е' || value == 'ё' ||
            value == 'ю' || value == 'я';

        private static float PauseDuration(char value)
        {
            switch (value)
            {
                case '.': case '?': case '!': case '…': case '\n': return .30f;
                case ',': case ';': case ':': case '—': return .17f;
                default: return .065f;
            }
        }
    }
}
