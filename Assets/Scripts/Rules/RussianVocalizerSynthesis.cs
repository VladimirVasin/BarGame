using System;
using System.Collections.Generic;
using System.Threading;
using Shape = BarPromenade.RussianVocalizerDsp.Shape;

namespace BarPromenade
{
    /// <summary>Bounded deterministic source/filter speech. Only canonical phone plans are cached.</summary>
    public static class RussianVocalizerSynthesis
    {
        public const int SampleRate = 22050;
        public const int VoiceCount = 12;
        public const float MaximumAmplitude = .72f;
        public const int MaximumDurationSeconds = 40;
        public const double MaximumProfileTempo = 1.5;
        private const double Tau = Math.PI * 2;
        private const int PlanCacheCapacity = 32;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, RussianSpeechPhone[]> Plans =
            new Dictionary<string, RussianSpeechPhone[]>(PlanCacheCapacity, StringComparer.Ordinal);
        private static readonly string[] PlanOrder = new string[PlanCacheCapacity];
        private static int nextPlan;

        private static readonly RussianVocalizerVoice[] LabVoices =
        {
            V(92, .83, 1.14, .025, 0, new RussianVocalizerStyle(pressed:.5, roughness:.28,
                nasality:.1, brightness:-.55, melody:.2, stress:.7, vowelLength:1.08, consonantLength:1.06,
                vowelReduction:.55, softness:.9)),
            V(265, 1.16, .82, .008, 1, new RussianVocalizerStyle(pressed:.9, brightness:.8,
                melody:.65, stress:.9, vowelLength:.8, consonantLength:.85, pauseLength:.75,
                rhythm:.6, articulation:.98, softness:1.2, rVariant:RussianSpeechRVariant.Tap), RussianVocalizerGender.Female),
            V(126, .97, 1.02, .014, 2, new RussianVocalizerStyle(pressed:.7, roughness:.25,
                brightness:.05, melody:.1, stress:.75, consonantLength:1.2, pauseLength:1.15,
                rhythm:.4, articulation:.88, vowelReduction:1)),
            V(180, .99, 1.1, .05, 3, new RussianVocalizerStyle(airiness:.88, pressed:.05,
                brightness:-.15, melody:.55, stress:.25, flutter:.45, vowelLength:1.15,
                consonantLength:.85, articulation:.25, vowelReduction:.6, softness:1.25), RussianVocalizerGender.Female),
            V(205, 1.04, .78, .015, 4, new RussianVocalizerStyle(pressed:.55, nasality:.92,
                brightness:.3, melody:.75, stress:.8, vowelLength:.88, pauseLength:.6,
                rhythm:.9, articulation:.85, rVariant:RussianSpeechRVariant.Tap)),
            V(116, .88, 1.24, .025, 5, new RussianVocalizerStyle(pressed:.2, brightness:-.25,
                melody:.55, stress:.85, vowelLength:1.35, consonantLength:.95, pauseLength:1.35,
                rhythm:.5, articulation:.35, vowelReduction:.15)),
            V(106, .96, 1.16, .04, 6, new RussianVocalizerStyle(pressed:.65, creak:.95,
                roughness:.5, brightness:-.4, melody:.15, stress:.4, flutter:.7, vowelLength:1.12,
                pauseLength:1.3, rhythm:.7, articulation:.5, vowelReduction:.9)),
            V(160, 1, 1.13, .008, 7, new RussianVocalizerStyle(pressed:.3, brightness:.1,
                melody:.4, stress:.65, consonantLength:1.25, pauseLength:1.2, rhythm:.05,
                articulation:1, vowelReduction:.08, softness:1.1, rVariant:RussianSpeechRVariant.Tap)),
            V(112, .93, .96, .08, 8, new RussianVocalizerStyle(airiness:.12, pressed:.75,
                creak:.2, roughness:.95, brightness:.2, melody:.35, stress:.65, flutter:.8,
                consonantLength:1.12, rhythm:.55, articulation:.72)),
            V(235, 1.1, 1.03, .014, 9, new RussianVocalizerStyle(airiness:.15, pressed:.2,
                nasality:.15, brightness:.45, melody:1, stress:1, flutter:.2, vowelLength:1.15,
                pauseLength:1.1, rhythm:.6, articulation:.55, vowelReduction:.4), RussianVocalizerGender.Female),
            V(172, .9, .86, .018, 10, new RussianVocalizerStyle(pressed:.55, brightness:-.85,
                melody:.12, stress:.8, vowelLength:.65, consonantLength:.7, pauseLength:1.1,
                rhythm:.95, articulation:.98, vowelReduction:.7, rVariant:RussianSpeechRVariant.Tap), RussianVocalizerGender.Female),
            V(141, .92, 1.13, .04, 11, new RussianVocalizerStyle(pressed:.35, roughness:.42,
                nasality:.25, brightness:-.15, melody:.25, stress:.55, vowelLength:1.2,
                consonantLength:1.1, rhythm:.65, articulation:.4, vowelReduction:.5,
                rVariant:RussianSpeechRVariant.Guttural))
        };
        private static readonly string[] Names =
        {
            "Гулкий низкий", "Высокий колкий", "Сухой ворчун", "Воздушный мягкий",
            "Носовой торопливый", "Грудной протяжный", "Скрипучий надломленный", "Чёткий размеренный",
            "Хриплый шершавый", "Звонкий певучий", "Глухой отрывистый", "Гортанный вязкий"
        };
        private static readonly string[] Descriptions =
        {
            "Низкий грудной гул, тяжёлые гласные и шероховатый край.",
            "Высокий, острый и собранный: короткие слоги и резкие согласные.",
            "Сухая подача, сжатый голос и почти ровная мелодика.",
            "Мягкое воздушное звучание, плавные переходы и лёгкое дрожание.",
            "Узкий носовой резонанс, быстрый неровный ритм и короткое р.",
            "Грудной тембр, растянутые гласные и отчётливое безударное о.",
            "Низкая скрипучая подача с нерегулярными голосовыми импульсами.",
            "Раздельные согласные, полные гласные и спокойные паузы.",
            "Хриплый шумный край, плотные импульсы и неровное звучание.",
            "Широкие перепады высоты, звонкий резонанс и выраженные ударения.",
            "Приглушённый тембр и короткие отрывистые слоги с паузами.",
            "Тягучая подача, мягкие переходы и гортанный вариант р."
        };

        private static RussianVocalizerVoice V(double f0, double tract, double tempo, double breath,
            int ordinal, in RussianVocalizerStyle style, RussianVocalizerGender gender = RussianVocalizerGender.Male) =>
            new RussianVocalizerVoice(f0, tract, tempo, breath, 0x6D2B79F5u + (uint)ordinal * 127u, style, gender);
        private static void CheckIndex(int index)
        {
            if (index < 0 || index >= VoiceCount) throw new ArgumentOutOfRangeException(nameof(index));
        }
        public static RussianVocalizerVoice LabVoiceAt(int index) { CheckIndex(index); return LabVoices[index]; }
        public static string LabVoiceNameAt(int index) { CheckIndex(index); return Names[index]; }
        public static string LabVoiceDescriptionAt(int index) { CheckIndex(index); return Descriptions[index]; }
        public static RussianVocalizerGender LabVoiceGenderAt(int index) => LabVoiceAt(index).Gender;

        public static float[] Generate(string text, int voiceIndex, CancellationToken cancellationToken = default) =>
            Generate(text, LabVoiceAt(voiceIndex), cancellationToken);
        public static float[] Generate(string text, in RussianVocalizerVoice voice, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Render(Prepare(text, voice), cancellationToken);
        }
        public static float EstimateDuration(string text, in RussianVocalizerVoice voice) => Prepare(text, voice).DurationSeconds;
        public static float EstimateDuration(string text)
        {
            if (!RussianSpeechPhonetics.HasRussianSpeech(text)) return 0;
            double total = 0;
            foreach (RussianSpeechPhone phone in GetPlan(text))
            {
                double longest = 0;
                foreach (RussianVocalizerVoice voice in LabVoices)
                {
                    RussianVocalizerStyle style = voice.Style;
                    bool pause = phone.Sound == RussianSpeechSound.Pause;
                    bool vowel = IsVowel(phone.CanonicalSound);
                    double multiplier = pause ? style.PauseLength : vowel ? style.VowelLength : style.ConsonantLength;
                    double rhythmBound = pause ? 1 : 1 + .22 * style.Rhythm;
                    double stress = vowel && phone.Stressed ? 1 + .18 * style.Stress : 1;
                    longest = Math.Max(longest, phone.DurationSeconds * voice.Tempo * multiplier * rhythmBound * stress);
                }
                total += longest;
            }
            return (float)(Math.Min(SampleRate * MaximumDurationSeconds, Math.Ceiling(total * SampleRate)) / SampleRate);
        }
        public static int ResolveSampleCount(string text, in RussianVocalizerVoice voice) => Prepare(text, voice).SampleCount;
        public static int ResolveSampleCount(string text, int voiceIndex) => ResolveSampleCount(text, LabVoiceAt(voiceIndex));

        public static RussianVocalizerPlan Prepare(string text, in RussianVocalizerVoice voice, double windowSeconds = 0)
        {
            if (!voice.IsValid) throw new ArgumentException("A valid vocalizer identity is required.", nameof(voice));
            if (double.IsNaN(windowSeconds) || double.IsInfinity(windowSeconds) || windowSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            int textLength = Math.Min(text?.Length ?? 0, RussianSpeechPhonetics.MaximumTextLength);
            RussianSpeechPhone[] phones = RussianSpeechPhonetics.HasRussianSpeech(text) ? GetPlan(text) : Array.Empty<RussianSpeechPhone>();
            if (phones.Length == 0) return new RussianVocalizerPlan(voice, phones, Array.Empty<int>(), new int[textLength]);
            var durations = new double[phones.Length];
            double total = 0;
            RussianVocalizerStyle style = voice.Style;
            for (int i = 0; i < phones.Length; i++)
            {
                RussianSpeechPhone phone = phones[i];
                bool pause = phone.Sound == RussianSpeechSound.Pause;
                bool vowel = IsVowel(phone.CanonicalSound);
                double length = pause ? style.PauseLength : vowel ? style.VowelLength : style.ConsonantLength;
                double rhythm = pause ? 1 : 1 + .22 * style.Rhythm *
                    Math.Sin(phone.WordIndex * 2.399963 + (voice.Seed & 255) * .0245);
                double emphasis = vowel && phone.Stressed ? 1 + .18 * style.Stress : 1;
                durations[i] = phone.DurationSeconds * voice.Tempo * length * rhythm * emphasis;
                total += durations[i];
            }
            int maximum = SampleRate * MaximumDurationSeconds;
            int budget = windowSeconds > 0 ? (int)Math.Min(maximum, Math.Floor(windowSeconds * SampleRate)) : maximum;
            if (budget < phones.Length) throw new ArgumentOutOfRangeException(nameof(windowSeconds), "The window cannot hold every phone.");
            double budgetSeconds = budget / (double)SampleRate;
            // Preserve identity/rhythm: reduce optional pauses, then vowel drawl,
            // before applying a common duration fit. No resampling or pitch shift.
            if (total > budgetSeconds) ReduceExcess(phones, durations, ref total, budgetSeconds, voice, true);
            if (total > budgetSeconds) ReduceExcess(phones, durations, ref total, budgetSeconds, voice, false);
            int samples = Math.Min(budget, Math.Max(phones.Length, (int)Math.Round(total * SampleRate)));
            var ends = new int[phones.Length];
            double cumulative = 0;
            int previous = 0;
            for (int i = 0; i < phones.Length; i++)
            {
                cumulative += durations[i];
                int end = (int)Math.Round(cumulative / total * samples);
                ends[i] = Math.Max(previous + 1, Math.Min(samples - (phones.Length - i - 1), end));
                previous = ends[i];
            }
            var characterSamples = new int[textLength];
            int revealed = 0;
            for (int i = 0; i < phones.Length; i++)
            {
                RussianSpeechPhone phone = phones[i];
                int at = i > 0 ? ends[i - 1] : 0;
                int reveal = Math.Max(revealed, Math.Min(textLength, phone.TextEnd));
                while (revealed < reveal) characterSamples[revealed++] = at;
            }
            while (revealed < textLength) characterSamples[revealed++] = samples;
            return new RussianVocalizerPlan(voice, phones, ends, characterSamples);
        }

        private static void ReduceExcess(RussianSpeechPhone[] phones, double[] durations, ref double total,
            double budget, in RussianVocalizerVoice voice, bool pauses)
        {
            double available = 0;
            for (int i = 0; i < phones.Length; i++)
            {
                bool eligible = pauses ? phones[i].Sound == RussianSpeechSound.Pause : IsVowel(phones[i].CanonicalSound);
                if (!eligible) continue;
                double floor = pauses ? Math.Min(durations[i], .025 * voice.Tempo) :
                    Math.Min(durations[i], phones[i].DurationSeconds * Math.Min(1, voice.Tempo));
                available += durations[i] - floor;
            }
            if (available <= 0) return;
            double fraction = Math.Min(1, (total - budget) / available);
            for (int i = 0; i < phones.Length; i++)
            {
                bool eligible = pauses ? phones[i].Sound == RussianSpeechSound.Pause : IsVowel(phones[i].CanonicalSound);
                if (!eligible) continue;
                double floor = pauses ? Math.Min(durations[i], .025 * voice.Tempo) :
                    Math.Min(durations[i], phones[i].DurationSeconds * Math.Min(1, voice.Tempo));
                double removed = (durations[i] - floor) * fraction;
                durations[i] -= removed;
                total -= removed;
            }
        }

        public static float[] Render(RussianVocalizerPlan plan, CancellationToken cancellationToken = default)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.SampleCount == 0) return Array.Empty<float>();
            RussianVocalizerVoice voice = plan.Voice;
            RussianVocalizerStyle style = voice.Style;
            var output = new float[plan.SampleCount];
            var dsp = new RussianVocalizerDsp(voice, SampleRate);
            double peak = 0, energy = 0;
            int activeSamples = 0, cursor = 0;
            for (int phoneIndex = 0; phoneIndex < plan.PhoneCount; phoneIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RussianSpeechPhone phone = plan.PhoneAt(phoneIndex);
                Shape shape = PhoneShape(phone, style);
                Shape previousShape = phoneIndex > 0 ? PhoneShape(plan.PhoneAt(phoneIndex - 1), style) : shape;
                Shape nextShape = phoneIndex + 1 < plan.PhoneCount ? PhoneShape(plan.PhoneAt(phoneIndex + 1), style) : shape;
                int length = plan.LengthAt(phoneIndex);
                dsp.SetPhone(shape, phone.Sound, phone.Stressed);
                bool pause = phone.Sound == RussianSpeechSound.Pause;
                for (int sample = 0; sample < length; sample++, cursor++)
                {
                    if ((sample & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double progress = sample / (double)Math.Max(1, length - 1);
                    double time = cursor / (double)SampleRate;
                    if (sample % 44 == 0)
                    {
                        double f1 = shape.F1, f2 = shape.F2, f3 = shape.F3;
                        double transition = Math.Min(.45, (.012 + .040 * (1 - style.Articulation)) * SampleRate / length);
                        double amount = .22 + .30 * (1 - style.Articulation);
                        if (progress < transition && previousShape.Voice > 0)
                        {
                            double blend = amount * (1 - progress / transition);
                            f1 = Lerp(f1, previousShape.F1, blend); f2 = Lerp(f2, previousShape.F2, blend); f3 = Lerp(f3, previousShape.F3, blend);
                        }
                        if (progress > 1 - transition && nextShape.Voice > 0)
                        {
                            double blend = amount * (progress - 1 + transition) / transition;
                            f1 = Lerp(f1, nextShape.F1, blend); f2 = Lerp(f2, nextShape.F2, blend); f3 = Lerp(f3, nextShape.F3, blend);
                        }
                        if (shape.Vowel && phone.Soft)
                            f2 = Lerp(f2, 2250, .35 * style.Softness * Math.Max(0, 1 - progress * 2.5));
                        dsp.Tune(f1, f2, f3);
                    }
                    double phrase = cursor / (double)Math.Max(1, plan.SampleCount - 1);
                    double contour = 1 + style.Melody * (.12 * Math.Sin(Math.PI * 1.35 * phrase) - .14 * phrase);
                    double stressPitch = phone.Stressed ? 1 + .025 + .13 * style.Stress : 1;
                    double flutter = 1 + (.001 + .022 * style.Flutter) * Math.Sin(Tau * 4.7 * time)
                        + .008 * style.Flutter * Math.Sin(Tau * 11.3 * time);
                    double attack = phoneIndex == 0 || plan.PhoneAt(phoneIndex - 1).Sound == RussianSpeechSound.Pause
                        ? Math.Min(1, sample / (SampleRate * .008)) : 1;
                    double release = phoneIndex == plan.PhoneCount - 1 || plan.PhoneAt(phoneIndex + 1).Sound == RussianSpeechSound.Pause
                        ? Math.Min(1, (length - 1 - sample) / (SampleRate * .012)) : 1;
                    double noiseEdge = Math.Min(1, sample / (SampleRate * .003)) * Math.Min(1, (length - 1 - sample) / (SampleRate * .004));
                    double dcRemoved = dsp.Process(voice.FundamentalHz * stressPitch * contour * flutter,
                        progress, time, attack, release, noiseEdge);
                    output[cursor] = (float)dcRemoved;
                    peak = Math.Max(peak, Math.Abs(dcRemoved));
                    if (!pause) { energy += dcRemoved * dcRemoved; activeSamples++; }
                }
            }
            double rms = activeSamples > 0 ? Math.Sqrt(energy / activeSamples) : 0;
            double scale = peak > .000001 && rms > .000001 ? Math.Min(MaximumAmplitude / peak, .13 / rms) : 0;
            for (int i = 0; i < output.Length; i++)
            {
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                output[i] = (float)Math.Max(-MaximumAmplitude, Math.Min(MaximumAmplitude, output[i] * scale));
            }
            output[0] = output[output.Length - 1] = 0;
            return output;
        }

        private static bool IsVowel(RussianSpeechSound sound) =>
            (int)sound >= (int)RussianSpeechSound.A && (int)sound <= (int)RussianSpeechSound.E;
        private static Shape PhoneShape(in RussianSpeechPhone phone, in RussianVocalizerStyle style)
        {
            Shape reduced = Shape.For(phone.Sound, phone.Soft);
            if (!IsVowel(phone.CanonicalSound)) return reduced;
            Shape full = Shape.For(phone.CanonicalSound, phone.Soft);
            reduced.F1 = Lerp(full.F1, reduced.F1, style.VowelReduction);
            reduced.F2 = Lerp(full.F2, reduced.F2, style.VowelReduction);
            reduced.F3 = Lerp(full.F3, reduced.F3, style.VowelReduction);
            return reduced;
        }

        private static RussianSpeechPhone[] GetPlan(string text)
        {
            string bounded = text.Length <= RussianSpeechPhonetics.MaximumTextLength ? text :
                text.Substring(0, RussianSpeechPhonetics.MaximumTextLength);
            lock (CacheLock) if (Plans.TryGetValue(bounded, out RussianSpeechPhone[] hit)) return hit;
            IReadOnlyList<RussianSpeechPhone> planned = RussianSpeechPhonetics.Plan(bounded);
            var candidate = new RussianSpeechPhone[planned.Count];
            for (int i = 0; i < candidate.Length; i++) candidate[i] = planned[i];
            lock (CacheLock)
            {
                if (Plans.TryGetValue(bounded, out RussianSpeechPhone[] hit)) return hit;
                string expired = PlanOrder[nextPlan]; if (expired != null) Plans.Remove(expired);
                PlanOrder[nextPlan] = bounded; nextPlan = (nextPlan + 1) % PlanCacheCapacity;
                Plans.Add(bounded, candidate); return candidate;
            }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
