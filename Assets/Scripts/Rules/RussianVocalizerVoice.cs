using System;

namespace BarPromenade
{
    public enum RussianVocalizerGender { Male, Female }
    public enum RussianSpeechRVariant { Trill, Tap, Guttural }

    /// <summary>Independent source, filter, pronunciation and delivery controls.</summary>
    public readonly struct RussianVocalizerStyle
    {
        public readonly double Airiness, Pressed, Creak, Roughness, Nasality, Brightness;
        public readonly double Melody, Stress, Flutter, VowelLength, ConsonantLength, PauseLength;
        public readonly double Rhythm, Articulation, VowelReduction, Softness;
        public readonly RussianSpeechRVariant RVariant;

        public RussianVocalizerStyle(double airiness = 0, double pressed = .25, double creak = 0,
            double roughness = 0, double nasality = 0, double brightness = 0, double melody = .35,
            double stress = .5, double flutter = .25, double vowelLength = 1, double consonantLength = 1,
            double pauseLength = 1, double rhythm = .2, double articulation = .65,
            double vowelReduction = 1, double softness = 1, RussianSpeechRVariant rVariant = RussianSpeechRVariant.Trill)
        {
            Check(airiness, 0, 1, nameof(airiness)); Check(pressed, 0, 1, nameof(pressed));
            Check(creak, 0, 1, nameof(creak)); Check(roughness, 0, 1, nameof(roughness));
            Check(nasality, 0, 1, nameof(nasality)); Check(brightness, -1, 1, nameof(brightness));
            Check(melody, 0, 1, nameof(melody)); Check(stress, 0, 1, nameof(stress));
            Check(flutter, 0, 1, nameof(flutter)); Check(rhythm, 0, 1, nameof(rhythm));
            Check(articulation, 0, 1, nameof(articulation)); Check(vowelReduction, 0, 1, nameof(vowelReduction));
            Check(softness, .65, 1.35, nameof(softness));
            Check(vowelLength, .35, 2, nameof(vowelLength));
            Check(consonantLength, .35, 2, nameof(consonantLength));
            Check(pauseLength, .35, 2, nameof(pauseLength));
            if ((int)rVariant < (int)RussianSpeechRVariant.Trill || (int)rVariant > (int)RussianSpeechRVariant.Guttural)
                throw new ArgumentOutOfRangeException(nameof(rVariant));
            Airiness = airiness; Pressed = pressed; Creak = creak; Roughness = roughness;
            Nasality = nasality; Brightness = brightness; Melody = melody; Stress = stress; Flutter = flutter;
            VowelLength = vowelLength; ConsonantLength = consonantLength; PauseLength = pauseLength;
            Rhythm = rhythm; Articulation = articulation; VowelReduction = vowelReduction;
            Softness = softness; RVariant = rVariant;
        }

        public static RussianVocalizerStyle Neutral => new RussianVocalizerStyle(pressed: .25);
        public bool IsValid => VowelLength >= .35 && VowelLength <= 2 &&
            ConsonantLength >= .35 && ConsonantLength <= 2 && PauseLength >= .35 && PauseLength <= 2 &&
            Softness >= .65 && Softness <= 1.35;

        private static void Check(double value, double minimum, double maximum, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>Immutable identity and manner; rendering owns all mutable DSP state.</summary>
    public readonly struct RussianVocalizerVoice
    {
        public readonly double FundamentalHz, TractScale, Tempo, Breath;
        public readonly uint Seed;
        public readonly RussianVocalizerStyle Style;
        public readonly RussianVocalizerGender Gender;

        public RussianVocalizerVoice(double fundamentalHz, double tractScale, double tempo,
            double breath, uint seed)
            : this(fundamentalHz, tractScale, tempo, breath, seed, RussianVocalizerStyle.Neutral) { }

        public RussianVocalizerVoice(double fundamentalHz, double tractScale, double tempo,
            double breath, uint seed, in RussianVocalizerStyle style,
            RussianVocalizerGender gender = RussianVocalizerGender.Male)
        {
            Check(fundamentalHz, 50, 500, nameof(fundamentalHz));
            Check(tractScale, .5, 2, nameof(tractScale)); Check(tempo, .01, 1.5, nameof(tempo));
            Check(breath, 0, .3, nameof(breath));
            if (!style.IsValid) throw new ArgumentException("A valid vocalizer style is required.", nameof(style));
            if (gender != RussianVocalizerGender.Male && gender != RussianVocalizerGender.Female)
                throw new ArgumentOutOfRangeException(nameof(gender));
            FundamentalHz = fundamentalHz; TractScale = tractScale; Tempo = tempo; Breath = breath;
            Seed = seed; Style = style; Gender = gender;
        }

        public bool IsValid => FundamentalHz >= 50 && FundamentalHz <= 500 && TractScale >= .5 &&
            TractScale <= 2 && Tempo >= .01 && Tempo <= 1.5 && Breath >= 0 && Breath <= .3 && Style.IsValid;
        public RussianVocalizerVoice WithTempo(double tempo) =>
            new RussianVocalizerVoice(FundamentalHz, TractScale, tempo, Breath, Seed, Style, Gender);
        public RussianVocalizerVoice WithIdentity(uint seed) =>
            new RussianVocalizerVoice(FundamentalHz, TractScale, Tempo, Breath, seed, Style, Gender);
        public RussianVocalizerVoice WithStyle(in RussianVocalizerStyle style) =>
            new RussianVocalizerVoice(FundamentalHz, TractScale, Tempo, Breath, Seed, style, Gender);

        private static void Check(double value, double minimum, double maximum, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
