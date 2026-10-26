using System;

namespace BarPromenade
{
    /// <summary>Stateful source/filter kernel shared by finite speech and continuous vowels.</summary>
    internal sealed class RussianVocalizerDsp
    {
        private const double Tau = Math.PI * 2;
        private readonly RussianVocalizerVoice voice;
        private readonly RussianVocalizerStyle style;
        private readonly int sampleRate;
        private readonly double gainAlpha, dcPole, darkAlpha;
        private Resonator first, second, third, nasal;
        private Shape shape;
        private RussianSpeechSound sound;
        private bool stressed, stop, affricate, pause;
        private uint random, cycleRandom;
        private double phase, noiseLow, noiseHigh, dark, voicedGain, stressGain, dcInput, dcOutput;
        private double cycleJitter, cycleGain, highAlpha, lowAlpha;
        private int pulse;

        public RussianVocalizerDsp(in RussianVocalizerVoice voice, int sampleRate)
        {
            if (!voice.IsValid) throw new ArgumentException("A valid vocalizer identity is required.", nameof(voice));
            if (sampleRate < 8000 || sampleRate > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            this.voice = voice;
            style = voice.Style;
            this.sampleRate = sampleRate;
            // Exactly retain the existing finite-render arithmetic at its authored rate.
            double rateScale = RussianVocalizerSynthesis.SampleRate / (double)sampleRate;
            gainAlpha = sampleRate == RussianVocalizerSynthesis.SampleRate ? .045 : 1 - Math.Pow(1 - .045, rateScale);
            dcPole = sampleRate == RussianVocalizerSynthesis.SampleRate ? .987 : Math.Pow(.987, rateScale);
            double darkness = Math.Max(0, -style.Brightness);
            double authoredDarkAlpha = .82 - .64 * darkness;
            darkAlpha = sampleRate == RussianVocalizerSynthesis.SampleRate ? authoredDarkAlpha :
                1 - Math.Pow(1 - authoredDarkAlpha, rateScale);
            nasal.Tune(300 * voice.TractScale, 80, sampleRate);
            Reset();
        }

        internal void SetPhone(in Shape nextShape, RussianSpeechSound nextSound, bool isStressed)
        {
            shape = nextShape;
            sound = nextSound;
            stressed = isStressed;
            stop = sound == RussianSpeechSound.P || sound == RussianSpeechSound.B ||
                sound == RussianSpeechSound.T || sound == RussianSpeechSound.D ||
                sound == RussianSpeechSound.K || sound == RussianSpeechSound.G;
            affricate = sound == RussianSpeechSound.Ch || sound == RussianSpeechSound.Ts;
            pause = sound == RussianSpeechSound.Pause;
            highAlpha = 1 - Math.Exp(-Tau * Math.Min(sampleRate * .46, shape.NoiseHigh * voice.TractScale) / sampleRate);
            lowAlpha = 1 - Math.Exp(-Tau * shape.NoiseLow * voice.TractScale / sampleRate);
        }

        internal void Tune(double f1, double f2, double f3)
        {
            first.Tune(Math.Min(sampleRate * .46, f1 * voice.TractScale), shape.Nasal ? 80 : 95 + style.Brightness * 12, sampleRate);
            second.Tune(Math.Min(sampleRate * .46, f2 * voice.TractScale), 130 + style.Brightness * 25, sampleRate);
            third.Tune(Math.Min(sampleRate * .46, f3 * voice.TractScale), 190 + style.Brightness * 30, sampleRate);
        }

        public void Reset()
        {
            random = voice.Seed == 0 ? 0x6D2B79F5u : voice.Seed;
            cycleRandom = random ^ 0xA511E9B3u;
            phase = noiseLow = noiseHigh = dark = voicedGain = dcInput = dcOutput = cycleJitter = 0;
            stressGain = cycleGain = 1;
            pulse = 0;
            first.Clear(); second.Clear(); third.Clear(); nasal.Clear();
        }

        internal double Process(double frequencyHz, double progress, double time,
            double attack, double release, double noiseEdge)
        {
            double noise = Noise(ref random);
            noiseHigh += highAlpha * (noise - noiseHigh);
            noiseLow += lowAlpha * (noiseHigh - noiseLow);
            double hiss = noiseHigh - noiseLow;
            phase += frequencyHz * (1 + cycleJitter * .055 * style.Roughness) / sampleRate;
            if (phase >= 1)
            {
                phase -= Math.Floor(phase); pulse++;
                cycleJitter = Noise(ref cycleRandom);
                cycleGain = 1 + .35 * style.Roughness * Noise(ref cycleRandom);
            }
            double duty = .77 - .30 * style.Pressed;
            double periodic = phase < duty ? Math.Sin(Tau * phase / duty) : 0;
            periodic += .14 * style.Pressed * Math.Sin(Tau * phase * 2);
            periodic *= cycleGain * ((pulse & 1) != 0 ? 1 - .78 * style.Creak : 1);
            double source = periodic * (1 - .84 * style.Airiness) + noise *
                (voice.Breath + .34 * style.Airiness + .035 * style.Roughness);
            double voiceAmount = shape.Voice;
            double noiseGain = shape.Noise * (1 + .22 * style.Brightness);
            double gain = 1;
            if (stop)
            {
                voiceAmount *= progress < .70 ? .09 : .40;
                noiseGain *= progress < .70 ? 0 : Math.Exp(-(progress - .70) * 11);
            }
            else if (affricate) noiseGain *= progress < .30 ? 0 : Math.Min(1, (progress - .30) * 18);
            else if (sound == RussianSpeechSound.R)
            {
                if (style.RVariant == RussianSpeechRVariant.Tap)
                    gain *= Math.Abs(progress - .45) < .09 ? .18 : .9;
                else if (style.RVariant == RussianSpeechRVariant.Guttural)
                {
                    gain *= .5 + .5 * Math.Max(0, Math.Sin(Tau * 43 * time));
                    noiseGain += .16; hiss += noise * .18;
                }
                else gain *= .34 + .66 * Math.Max(0, Math.Sin(Tau * 26 * time));
            }
            double oral = first.Step(source) * (shape.Nasal ? 1.15 : .9)
                + second.Step(source) * (shape.Nasal ? .16 : .55) * (1 + .35 * style.Brightness)
                + third.Step(source) * .24 * (1 + .8 * style.Brightness);
            double nasalSignal = nasal.Step(source);
            double vowel = Lerp(oral, nasalSignal * 1.6 + oral * .35, style.Nasality * .8);
            voicedGain += gainAlpha * (voiceAmount * gain - voicedGain);
            stressGain += gainAlpha * ((stressed ? 1.04 + .18 * style.Stress : .94) - stressGain);
            double value = (vowel * voicedGain + hiss * noiseGain * noiseEdge) * attack * release * stressGain;
            double darkness = Math.Max(0, -style.Brightness);
            dark += darkAlpha * (value - dark);
            value = Lerp(value, dark, darkness);
            if (pause) value = 0;
            double dcRemoved = value - dcInput + dcPole * dcOutput;
            dcInput = value; dcOutput = dcRemoved;
            if (pause) dcRemoved = 0;
            return dcRemoved / (1 + Math.Abs(dcRemoved) * 1.5);
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static double Noise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / 8388607.5 - 1.0;
        }

        private struct Resonator
        {
            private double one, two, gain, y1, y2;

            public void Tune(double frequency, double bandwidth, int sampleRate)
            {
                double radius = Math.Exp(-Math.PI * bandwidth / sampleRate);
                one = 2.0 * radius * Math.Cos(Tau * frequency / sampleRate);
                two = radius * radius;
                gain = 1.0 - radius;
            }

            public void Clear() { y1 = y2 = 0; }

            public double Step(double input)
            {
                double value = gain * input + one * y1 - two * y2;
                y2 = y1;
                y1 = value;
                return value;
            }
        }

        internal struct Shape
        {
            public double F1, F2, F3, Voice, Noise, NoiseLow, NoiseHigh;
            public bool Vowel, Nasal;

            private Shape(double f1, double f2, double f3, double voice = 1.0,
                double noise = 0.0, double low = 1000.0, double high = 6500.0,
                bool vowel = false, bool nasal = false)
            {
                F1 = f1; F2 = f2; F3 = f3; Voice = voice; Noise = noise;
                NoiseLow = low; NoiseHigh = high; Vowel = vowel; Nasal = nasal;
            }

            public static Shape For(RussianSpeechSound sound, bool soft)
            {
                Shape value;
                switch (sound)
                {
                    case RussianSpeechSound.A: value = new Shape(730, 1250, 2650, vowel: true); break;
                    case RussianSpeechSound.O: value = new Shape(500, 900, 2550, vowel: true); break;
                    case RussianSpeechSound.U: value = new Shape(330, 720, 2300, vowel: true); break;
                    case RussianSpeechSound.I: value = new Shape(280, 2250, 3100, vowel: true); break;
                    case RussianSpeechSound.Y: value = new Shape(390, 1500, 2600, vowel: true); break;
                    case RussianSpeechSound.E: value = new Shape(540, 1850, 2600, vowel: true); break;
                    case RussianSpeechSound.M: value = new Shape(260, 1050, 2200, 0.8, nasal: true); break;
                    case RussianSpeechSound.N: value = new Shape(290, 1600, 2600, 0.7, nasal: true); break;
                    case RussianSpeechSound.L: value = new Shape(400, 1100, 2800, 0.85); break;
                    case RussianSpeechSound.R: value = new Shape(460, 1350, 2500, 0.8); break;
                    case RussianSpeechSound.J: value = new Shape(280, 2250, 3100, 0.75); break;
                    case RussianSpeechSound.P: value = new Shape(320, 800, 2300, 0, 0.5, 500, 6000); break;
                    case RussianSpeechSound.B: value = new Shape(320, 800, 2300, 0.7, 0.4, 500, 6000); break;
                    case RussianSpeechSound.T: value = new Shape(400, 1700, 3000, 0, 0.65, 3800, 8500); break;
                    case RussianSpeechSound.D: value = new Shape(400, 1700, 3000, 0.65, 0.5, 3800, 8500); break;
                    case RussianSpeechSound.K: value = new Shape(400, 1800, 2600, 0, 0.6, 1600, 6200); break;
                    case RussianSpeechSound.G: value = new Shape(400, 1800, 2600, 0.65, 0.45, 1600, 6200); break;
                    case RussianSpeechSound.S: value = new Shape(350, 1800, 3000, 0, 0.40, 4200, 9000); break;
                    case RussianSpeechSound.Z: value = new Shape(350, 1800, 3000, 0.55, 0.27, 4200, 9000); break;
                    case RussianSpeechSound.Sh: value = new Shape(400, 1400, 2500, 0, 0.42, 1700, 5400); break;
                    case RussianSpeechSound.Zh: value = new Shape(400, 1400, 2500, 0.55, 0.28, 1700, 5400); break;
                    case RussianSpeechSound.Sch: value = new Shape(350, 2100, 3100, 0, 0.40, 2600, 6800); break;
                    case RussianSpeechSound.Ch: value = new Shape(350, 2100, 3100, 0, 0.48, 2400, 6800); break;
                    case RussianSpeechSound.Ts: value = new Shape(350, 1800, 3000, 0, 0.48, 4000, 9000); break;
                    case RussianSpeechSound.F: value = new Shape(350, 1250, 2600, 0, 0.20, 700, 7000); break;
                    case RussianSpeechSound.V: value = new Shape(350, 1250, 2600, 0.55, 0.14, 700, 7000); break;
                    case RussianSpeechSound.H: value = new Shape(500, 1500, 2600, 0, 0.25, 800, 4300); break;
                    default: value = new Shape(400, 1400, 2600, 0); break;
                }
                if (soft && !value.Vowel)
                {
                    value.F2 = Math.Max(value.F2, 2050.0);
                    value.F3 = Math.Max(value.F3, 2950.0);
                    value.NoiseLow *= 1.12;
                }
                return value;
            }
        }
    }
}
