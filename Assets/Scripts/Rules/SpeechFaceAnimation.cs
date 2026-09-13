using System;

namespace BarPromenade
{
    public enum SpeechFaceProfile { Hero, Foreman, CanneryWoman }
    public enum SpeechMouthPose { Closed = 0, Narrow = 1, Open = 2, Round = 3, Wide = 4, Teeth = 5 }
    public enum SpeechFaceExpression { Rest = 0, HalfBlink = 1, Blink = 2, Emphasis = 3, Skeptical = 4 }

    /// <summary>A borrowed snapshot of the actual shared delivery, never a second reveal clock.</summary>
    public readonly struct SpeechFaceSample
    {
        public readonly uint LineToken;
        public readonly string Text;
        public readonly int RevealedCharacters;
        public readonly bool IsTyping;
        public readonly double ElapsedSeconds;

        public SpeechFaceSample(uint lineToken, string text, int revealedCharacters, bool isTyping,
            double elapsedSeconds)
        {
            LineToken = lineToken;
            Text = text ?? string.Empty;
            RevealedCharacters = Math.Max(0, Math.Min(Text.Length, revealedCharacters));
            IsTyping = lineToken != 0 && isTyping && RevealedCharacters < Text.Length;
            ElapsedSeconds = double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)
                ? 0d : Math.Max(0d, elapsedSeconds);
        }

        public bool HasLine => LineToken != 0 && !string.IsNullOrEmpty(Text);
        public char CurrentCharacter => HasLine && RevealedCharacters > 0
            ? Text[RevealedCharacters - 1] : '\0';
    }

    public readonly struct SpeechFacePose
    {
        public readonly SpeechMouthPose Mouth;
        public readonly SpeechFaceExpression Expression;
        public SpeechFacePose(SpeechMouthPose mouth, SpeechFaceExpression expression)
        { Mouth = mouth; Expression = expression; }
        public int AtlasCell => (int)Expression * SpeechFaceAnimation.MouthCount + (int)Mouth;
    }

    /// <summary>
    /// Discrete illustrated speech, not phonetic voice analysis. Every pose is resolved directly
    /// from an observed delivery and its owner's clock: pause, a missed frame and reconstruction
    /// cannot queue gestures or replay mouth movements. No Unity, audio, randomness or owned time.
    /// </summary>
    public static class SpeechFaceAnimation
    {
        public const int MouthCount = 6;
        public const int ExpressionCount = 5;
        public const int CleanCellCount = MouthCount * ExpressionCount;
        public const int DirtyCellOffset = 32;

        public static SpeechFacePose Resolve(in SpeechFaceSample sample, SpeechFaceProfile profile,
            double actorSeconds)
        {
            if (!sample.HasLine || !sample.IsTyping || sample.RevealedCharacters == 0)
                return ResolveListening(profile, actorSeconds);
            return new SpeechFacePose(ResolveMouth(sample, profile),
                ResolveSpeakingExpression(sample, profile, actorSeconds));
        }

        public static SpeechFacePose ResolveListening(SpeechFaceProfile profile, double actorSeconds)
        {
            SpeechFaceExpression blink = ResolveBlink(profile, actorSeconds);
            if (blink != SpeechFaceExpression.Rest)
                return new SpeechFacePose(SpeechMouthPose.Closed, blink);
            // Her ordinary attentive face stays quiet; warmth is an authored
            // social cue owned by the actual exchange, never a periodic smile.
            if (profile == SpeechFaceProfile.CanneryWoman)
                return new SpeechFacePose(SpeechMouthPose.Closed, SpeechFaceExpression.Rest);
            // A held, occasional listening squint; the foreman keeps it longer. It never mouths
            // silent choices, and is independent of the speaker's mouth and line boundaries.
            double phase = Phase(actorSeconds + (profile == SpeechFaceProfile.Hero ? .7d : 2.1d), 6.8d);
            bool squint = phase >= 4.25d && phase < (profile == SpeechFaceProfile.Hero ? 4.8d : 5.25d);
            return new SpeechFacePose(SpeechMouthPose.Closed,
                squint ? SpeechFaceExpression.Skeptical : SpeechFaceExpression.Rest);
        }

        private static SpeechMouthPose ResolveMouth(in SpeechFaceSample sample, SpeechFaceProfile profile)
        {
            if (!char.IsLetterOrDigit(sample.CurrentCharacter)) return SpeechMouthPose.Closed;
            // Hold a letter group instead of tracking every 24-Hz glyph. These strides yield
            // 8/12 articulation samples per second at the shared delivery rate, without copying
            // its CPS or estimating the reveal from time. Word/punctuation closures are immediate.
            // Short replies need a second articulation before their final letter closes the
            // mouth. In particular, "Хочу" must not hold its initial х for the entire reply.
            int stride = profile != SpeechFaceProfile.Foreman && sample.Text.Length > 12 ? 3 : 2;
            int current = sample.RevealedCharacters - 1;
            int cue = current - current % stride;
            // A new word can start inside a held group. Do not let its preceding space hold
            // the mouth shut, particularly on the hero's very short existing replies.
            while (cue < current && !char.IsLetterOrDigit(sample.Text[cue])) cue++;
            char value = char.ToLowerInvariant(sample.Text[cue]);
            switch (value)
            {
                case 'м': case 'б': case 'п': case 'm': case 'b': case 'p':
                    return SpeechMouthPose.Closed;
                case 'о': case 'у': case 'ю': case 'ё': case 'o': case 'u': case 'w': case 'q':
                    return SpeechMouthPose.Round;
                case 'а': case 'я': case 'х': case 'a': case 'h':
                    return SpeechMouthPose.Open;
                case 'е': case 'э': case 'и': case 'ы': case 'e': case 'i': case 'y':
                    return SpeechMouthPose.Wide;
                case 'ф': case 'в': case 'с': case 'з': case 'ц': case 'ч': case 'ш': case 'щ':
                case 'f': case 'v': case 's': case 'z': case 'j': case 'x':
                    return SpeechMouthPose.Teeth;
                default:
                    return SpeechMouthPose.Narrow;
            }
        }

        private static SpeechFaceExpression ResolveSpeakingExpression(in SpeechFaceSample sample,
            SpeechFaceProfile profile, double actorSeconds)
        {
            SpeechFaceExpression blink = ResolveBlink(profile, actorSeconds);
            if (blink != SpeechFaceExpression.Rest) return blink;
            if (profile == SpeechFaceProfile.CanneryWoman)
                return Phase(sample.ElapsedSeconds, 2.3d) < .48d
                    ? SpeechFaceExpression.Emphasis : SpeechFaceExpression.Rest;
            int current = sample.RevealedCharacters - 1;
            int phrase = 0;
            for (int index = 0; index < current; index++)
                if (IsPhraseEnd(sample.Text[index])) phrase++;
            char ending = '\0';
            for (int index = current; index < sample.Text.Length; index++)
                if (IsPhraseEnd(sample.Text[index])) { ending = sample.Text[index]; break; }

            bool foreman = profile == SpeechFaceProfile.Foreman;
            double beatSeconds = foreman ? 1.15d : 1.65d;
            long beat = (long)Math.Floor(sample.ElapsedSeconds / beatSeconds);
            double phase = sample.ElapsedSeconds - beat * beatSeconds;
            // Begin an actual spoken reply with an observable brow accent, even when the whole
            // line is four letters. A wider quiet interval keeps the hero recognisably tired;
            // the foreman's longer accents alternate a grumble with a raised-brow point.
            double heldSeconds = foreman ? .85d : .62d;
            if (phase >= heldSeconds) return SpeechFaceExpression.Rest;
            if (ending == '?') return SpeechFaceExpression.Skeptical;
            if (ending == '!') return SpeechFaceExpression.Emphasis;
            bool skeptical = foreman ? (beat + phrase) % 3 == 1 : beat > 0 && (beat + phrase) % 3 == 2;
            return skeptical ? SpeechFaceExpression.Skeptical : SpeechFaceExpression.Emphasis;
        }

        private static bool IsPhraseEnd(char value) => value == '.' || value == ',' || value == ';' ||
            value == ':' || value == '?' || value == '!' || value == '—' || value == '\n';

        private static SpeechFaceExpression ResolveBlink(SpeechFaceProfile profile, double seconds)
        {
            double phase = Phase(seconds + (profile == SpeechFaceProfile.Hero ? .43d : 1.27d),
                profile == SpeechFaceProfile.CanneryWoman ? 4.65d : profile == SpeechFaceProfile.Hero ? 4.1d : 3.55d);
            if (phase < .055d || phase >= .14d && phase < .205d) return SpeechFaceExpression.HalfBlink;
            return phase < .14d ? SpeechFaceExpression.Blink : SpeechFaceExpression.Rest;
        }

        private static double Phase(double seconds, double period)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return period * .5d;
            return ((seconds % period) + period) % period;
        }
    }
}
