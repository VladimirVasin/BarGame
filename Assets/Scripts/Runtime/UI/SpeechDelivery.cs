using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One line being said, mid-delivery. This is the whole typewriter,
    /// and it is the ONLY one: the bubble over a speaker's head and the
    /// prompt panel at the bottom of the screen both embed this struct
    /// rather than each keeping their own idea of how fast a character
    /// talks.
    ///
    /// It is a plain struct with no Unity object in it, so the timing
    /// of a line can be proved in EditMode without a camera, a canvas
    /// or a frame.
    ///
    /// The reveal is stepped by <see cref="Step"/> exactly once per
    /// frame rather than recomputed where it is drawn. That is not
    /// tidiness: `OnGUI` fires several times a frame for layout and
    /// repaint, so a blip emitted from there would fire two or three
    /// times per letter. The count itself still comes from the pure
    /// <see cref="ResolveRevealedCharacters"/>, which is what makes the
    /// stepping observable without making it the source of truth.
    /// </summary>
    public struct SpeechDelivery
    {
        /// <summary>Typing speed. Fast enough that a 46-character line
        /// is complete in under a second and a half, which leaves most
        /// of the time it is up for actually reading it. Moved here
        /// from the bubble because the speed belongs to the typewriter,
        /// not to one of the two panels it types into.</summary>
        public const float CharactersPerSecond = 34f;

        /// <summary>
        /// The shortest gap between two blips. At <see
        /// cref="CharactersPerSecond"/> a letter lands every `29 ms`,
        /// and a blip that often is not typing — it is a modem. Ninety
        /// milliseconds leaves a clear silence between two `45 ms`
        /// blips, so each one is heard as a separate keystroke, and on
        /// real Russian text (a fifth of which is spaces and
        /// punctuation, which never blip) it settles at about nine
        /// ticks a second: the tempo of a comfortable mechanical
        /// typewriter. A 46-character park taunt makes fifteen blips
        /// instead of forty-six.
        /// </summary>
        public const float MinimumBlipIntervalSeconds = 0.09f;

        /// <summary>The whole line, already localized and, where it
        /// carries a number, already composed.</summary>
        public string Text;

        public float StartedAt;

        /// <summary>How much of <see cref="Text"/> has been typed.
        /// Stepped once per frame and never runs backwards.</summary>
        public int RevealedCharacters;

        public float LastBlipAt;

        /// <summary>Narration and the hero's own prompts: whole at
        /// once, and no sound. A description of what he is looking at
        /// is not somebody talking.</summary>
        public bool IsSilent;

        /// <summary>Counts the blips this line has made, and is the
        /// per-blip salt that keeps a repeated letter from being
        /// machine-identical.</summary>
        public uint BlipOrdinal;

        /// <summary>A line somebody says: types out, and ticks.</summary>
        public static SpeechDelivery Spoken(string text, float startedAt)
        {
            return new SpeechDelivery
            {
                Text = text ?? string.Empty,
                StartedAt = startedAt,
                RevealedCharacters = 0,
                // Back-dated so the very first letter is allowed its
                // blip on its own frame instead of waiting out an
                // interval it never had.
                LastBlipAt = startedAt - MinimumBlipIntervalSeconds,
                IsSilent = false,
                BlipOrdinal = 0
            };
        }

        /// <summary>A line nobody says: whole, immediately, silent.
        /// </summary>
        public static SpeechDelivery Instant(string text)
        {
            string safeText = text ?? string.Empty;
            return new SpeechDelivery
            {
                Text = safeText,
                StartedAt = 0f,
                RevealedCharacters = safeText.Length,
                LastBlipAt = 0f,
                IsSilent = true,
                BlipOrdinal = 0
            };
        }

        public bool HasText => !string.IsNullOrEmpty(Text);

        public bool IsComplete =>
            string.IsNullOrEmpty(Text) ||
            RevealedCharacters >= Text.Length;

        /// <summary>What is on screen right now. The whole string when
        /// it is saturated, so a finished line costs no substring.
        /// </summary>
        public string RevealedText
        {
            get
            {
                if (string.IsNullOrEmpty(Text))
                {
                    return string.Empty;
                }

                int revealed = Mathf.Clamp(
                    RevealedCharacters,
                    0,
                    Text.Length);
                return revealed >= Text.Length
                    ? Text
                    : Text.Substring(0, revealed);
            }
        }

        /// <summary>
        /// How much of a line has been typed by now. Saturates at the
        /// whole line and never runs backwards. Pure: this is the
        /// source of truth both channels share, and the reason the
        /// stepping below can be observed without being trusted.
        /// </summary>
        public static int ResolveRevealedCharacters(
            string text,
            float elapsedSeconds)
        {
            if (string.IsNullOrEmpty(text) ||
                elapsedSeconds <= 0f ||
                float.IsNaN(elapsedSeconds))
            {
                return 0;
            }

            if (float.IsInfinity(elapsedSeconds))
            {
                return text.Length;
            }

            int revealed = Mathf.FloorToInt(
                elapsedSeconds * CharactersPerSecond);
            return Mathf.Clamp(revealed, 0, text.Length);
        }

        /// <summary>
        /// A character worth a blip. The blip marks a LETTER being
        /// written, so a space, a dash or a full stop passes in
        /// silence — which is also what gives a typed line its rhythm
        /// instead of a flat rattle.
        /// </summary>
        public static bool IsSpeakableCharacter(char value)
        {
            return char.IsLetterOrDigit(value);
        }

        /// <summary>
        /// The character a blip is pitched from when a frame reveals
        /// several at once. Walks the new run BACKWARDS and takes the
        /// newest speakable one — the letter the eye is on — so a
        /// dropped frame produces one keystroke rather than a burst.
        /// Returns `\0` when the whole run was spaces.
        /// </summary>
        public static char ResolveBlipCharacter(
            string text,
            int previous,
            int revealed)
        {
            if (string.IsNullOrEmpty(text))
            {
                return '\0';
            }

            int last = Mathf.Min(revealed, text.Length) - 1;
            int first = Mathf.Max(previous, 0);
            for (int index = last; index >= first; index--)
            {
                if (IsSpeakableCharacter(text[index]))
                {
                    return text[index];
                }
            }

            return '\0';
        }

        /// <summary>
        /// One frame of typing. Returns true exactly when a blip is
        /// due, and hands back the character to pitch it from. Call it
        /// once per frame from `Update`, never from `OnGUI`.
        /// </summary>
        public bool Step(float unscaledTime, out char blip)
        {
            blip = '\0';
            if (string.IsNullOrEmpty(Text) ||
                float.IsNaN(unscaledTime))
            {
                return false;
            }

            int revealed = ResolveRevealedCharacters(
                Text,
                unscaledTime - StartedAt);
            if (revealed <= RevealedCharacters)
            {
                return false;
            }

            int previous = RevealedCharacters;
            RevealedCharacters = revealed;
            if (IsSilent)
            {
                return false;
            }

            if (unscaledTime - LastBlipAt < MinimumBlipIntervalSeconds)
            {
                return false;
            }

            blip = ResolveBlipCharacter(Text, previous, revealed);
            if (blip == '\0')
            {
                return false;
            }

            LastBlipAt = unscaledTime;
            BlipOrdinal++;
            return true;
        }

        /// <summary>
        /// How long a spoken line needs on screen: the time it takes to
        /// type plus a tail to read it in. The bubble keeps its own
        /// fixed life, but the prompt panel is handed lines of very
        /// different lengths — the watchman's longest runs to
        /// sixty-seven characters in English, two seconds of typing
        /// inside a three-second window — and a constant would leave
        /// almost nothing to read.
        /// </summary>
        public static float ResolveSpokenDuration(
            string text,
            float readingTailSeconds)
        {
            float tail = Mathf.Max(0f, readingTailSeconds);
            if (string.IsNullOrEmpty(text))
            {
                return tail;
            }

            return text.Length / CharactersPerSecond + tail;
        }
    }
}
