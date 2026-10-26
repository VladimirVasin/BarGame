using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class RussianVocalizerTests
    {
        [Test]
        public void IotatedVowelsAndSeparatingSigns_PreserveStressAndSoftness()
        {
            IReadOnlyList<RussianSpeechPhone> hedgehog = RussianSpeechPhonetics.Plan("ЁЖ");
            AssertSounds(hedgehog, RussianSpeechSound.J, RussianSpeechSound.O, RussianSpeechSound.Sh);
            Assert.That(hedgehog[1].Stressed, Is.True);

            IReadOnlyList<RussianSpeechPhone> honey = RussianSpeechPhonetics.Plan("мёд");
            AssertSounds(honey, RussianSpeechSound.M, RussianSpeechSound.O, RussianSpeechSound.T);
            Assert.That(honey[0].Soft, Is.True);
            Assert.That(honey[1].Soft, Is.True);

            IReadOnlyList<RussianSpeechPhone> lift = RussianSpeechPhonetics.Plan("подъём");
            AssertSounds(lift, RussianSpeechSound.P, RussianSpeechSound.A, RussianSpeechSound.D,
                RussianSpeechSound.J, RussianSpeechSound.O, RussianSpeechSound.M);
            Assert.That(lift[2].Soft, Is.False, "ъ separates without palatalizing д");

            IReadOnlyList<RussianSpeechPhone> family = RussianSpeechPhonetics.Plan("семья");
            AssertSounds(family, RussianSpeechSound.S, RussianSpeechSound.I, RussianSpeechSound.M,
                RussianSpeechSound.J, RussianSpeechSound.A);
            Assert.That(family[2].Soft, Is.True, "ь softens м and retains a separate й");
        }

        [Test]
        public void ExistingPresets_UseAuthoredStressRatherThanLetterPitch()
        {
            AssertSounds(RussianSpeechPhonetics.Plan("Хорошо"), RussianSpeechSound.H,
                RussianSpeechSound.A, RussianSpeechSound.R, RussianSpeechSound.A,
                RussianSpeechSound.Sh, RussianSpeechSound.O);
            Assert.That(StressedSounds("Хорошо. Сейчас подам."), Is.EqualTo(new[]
                { RussianSpeechSound.O, RussianSpeechSound.A, RussianSpeechSound.A }));
            Assert.That(StressedSounds("Маршрут отменили. Я всё равно жду."), Is.EqualTo(new[]
                { RussianSpeechSound.U, RussianSpeechSound.I, RussianSpeechSound.A,
                  RussianSpeechSound.O, RussianSpeechSound.O, RussianSpeechSound.U }));
            Assert.That(StressedSounds("Бензин есть. Повода нет."), Is.EqualTo(new[]
                { RussianSpeechSound.I, RussianSpeechSound.E, RussianSpeechSound.O, RussianSpeechSound.E }));
            Assert.That(StressedSounds("Последний рейс уже был. Или ещё будет."), Is.EqualTo(new[]
                { RussianSpeechSound.E, RussianSpeechSound.E, RussianSpeechSound.E,
                  RussianSpeechSound.Y, RussianSpeechSound.I, RussianSpeechSound.O, RussianSpeechSound.U }));
        }

        [Test]
        public void WordObstruents_DevoiceAtEndAndAssimilateInsideWord()
        {
            AssertSounds(RussianSpeechPhonetics.Plan("дуб"),
                RussianSpeechSound.D, RussianSpeechSound.U, RussianSpeechSound.P);
            AssertSounds(RussianSpeechPhonetics.Plan("лодка"), RussianSpeechSound.L,
                RussianSpeechSound.O, RussianSpeechSound.T, RussianSpeechSound.K, RussianSpeechSound.A);
            AssertSounds(RussianSpeechPhonetics.Plan("сбор"), RussianSpeechSound.Z,
                RussianSpeechSound.B, RussianSpeechSound.O, RussianSpeechSound.R);
            IReadOnlyList<RussianSpeechPhone> light = RussianSpeechPhonetics.Plan("свет");
            Assert.That(light[0].Sound, Is.EqualTo(RussianSpeechSound.S),
                "в does not cause Russian regressive voicing");
            AssertSounds(RussianSpeechPhonetics.Plan("мыться"), RussianSpeechSound.M,
                RussianSpeechSound.Y, RussianSpeechSound.Ts, RussianSpeechSound.A);
        }

        [Test]
        public void ExplicitAcuteStress_ResolvesAmbiguousUnknownWords()
        {
            Assert.That(StressedSounds("за\u0301мок"), Is.EqualTo(new[] { RussianSpeechSound.A }));
            Assert.That(StressedSounds("замо\u0301к"), Is.EqualTo(new[] { RussianSpeechSound.O }));
            IReadOnlyList<RussianSpeechPhone> first = RussianSpeechPhonetics.Plan("за\u0301мок");
            IReadOnlyList<RussianSpeechPhone> second = RussianSpeechPhonetics.Plan("замо\u0301к");
            Assert.That(first[1].DurationSeconds, Is.GreaterThan(second[1].DurationSeconds));
        }

        [Test]
        public void TextPlanning_IsBoundedDeterministicAndKeepsPunctuationPauses()
        {
            string prefix = new string('я', RussianSpeechPhonetics.MaximumTextLength);
            AssertPhonesEqual(RussianSpeechPhonetics.Plan(prefix),
                RussianSpeechPhonetics.Plan(prefix + " Это уже за пределом."));
            Assert.That(RussianSpeechPhonetics.Plan(prefix).Count,
                Is.LessThanOrEqualTo(RussianSpeechPhonetics.MaximumTextLength * 2));
            Assert.That(RussianSpeechPhonetics.Plan("Latin 123 … ьъ!"), Is.Empty);
            Assert.That(RussianSpeechPhonetics.Plan(null), Is.Empty);
            Assert.That(RussianSpeechPhonetics.HasRussianSpeech("Latin 123 ЬЪ ьъ"), Is.False);
            Assert.That(RussianSpeechPhonetics.HasRussianSpeech("ЕЩЁ"), Is.True);
            Assert.That(RussianSpeechPhonetics.HasRussianSpeech(
                new string('a', RussianSpeechPhonetics.MaximumTextLength) + "я"), Is.False);
            AssertPhonesEqual(RussianSpeechPhonetics.Plan("Ещё немного."),
                RussianSpeechPhonetics.Plan("ЕЩЁ НЕМНОГО."));

            var pauses = new List<float>();
            IReadOnlyList<RussianSpeechPhone> line = RussianSpeechPhonetics.Plan("  дом, дом. дом!  ");
            foreach (RussianSpeechPhone phone in line)
            {
                Assert.That(phone.DurationSeconds, Is.InRange(.001f, .5f));
                if (phone.Sound == RussianSpeechSound.Pause) pauses.Add(phone.DurationSeconds);
            }
            Assert.That(pauses.Count, Is.EqualTo(2));
            Assert.That(pauses[1], Is.GreaterThan(pauses[0]));
            Assert.That(line[0].Sound, Is.Not.EqualTo(RussianSpeechSound.Pause));
            Assert.That(line[line.Count - 1].Sound, Is.Not.EqualTo(RussianSpeechSound.Pause));
        }

        [Test]
        public void TwoVoices_ProduceDifferentRepeatableFiniteBoundedAudio()
        {
            const string phrase = "Хорошо. Сейчас подам.";
            float[] low = RussianVocalizerSynthesis.Generate(phrase, 0);
            RussianVocalizerVoice lowVoice = RussianVocalizerSynthesis.LabVoiceAt(0);
            float[] repeat = RussianVocalizerSynthesis.Generate(phrase, lowVoice);
            float[] high = RussianVocalizerSynthesis.Generate(phrase, 1);
            Assert.That(RussianVocalizerSynthesis.SampleRate, Is.EqualTo(22050));
            Assert.That(RussianVocalizerSynthesis.VoiceCount, Is.EqualTo(12));
            Assert.That(low.Length, Is.InRange(RussianVocalizerSynthesis.SampleRate,
                (int)(RussianVocalizerSynthesis.SampleRate * RussianVocalizerSynthesis.MaximumDurationSeconds)));
            CollectionAssert.AreEqual(low, repeat);
            Assert.That(RussianVocalizerSynthesis.ResolveSampleCount(phrase, lowVoice), Is.EqualTo(low.Length));
            Assert.That(RussianVocalizerSynthesis.ResolveSampleCount(phrase, 1), Is.EqualTo(high.Length));
            Assert.That(RussianVocalizerSynthesis.EstimateDuration(phrase, lowVoice),
                Is.EqualTo(low.Length / (float)RussianVocalizerSynthesis.SampleRate));
            bool voicesDiffer = low.Length != high.Length;
            double energy = 0d;
            for (int i = 0; i < low.Length; i++)
            {
                Assert.That(float.IsNaN(low[i]) || float.IsInfinity(low[i]), Is.False);
                Assert.That(Math.Abs(low[i]), Is.LessThanOrEqualTo(
                    RussianVocalizerSynthesis.MaximumAmplitude + .000001f));
                energy += low[i] * low[i];
                if (i < high.Length && Math.Abs(low[i] - high[i]) > .0001f) voicesDiffer = true;
            }
            foreach (float sample in high)
            {
                Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
                Assert.That(Math.Abs(sample), Is.LessThanOrEqualTo(
                    RussianVocalizerSynthesis.MaximumAmplitude + .000001f));
            }
            Assert.That(voicesDiffer, Is.True);
            Assert.That(energy / low.Length, Is.GreaterThan(.00001d), "Speech must carry audible energy");
            Assert.That(low[0], Is.EqualTo(0f).Within(.000001f));
            Assert.That(low[low.Length - 1], Is.EqualTo(0f).Within(.000001f));
        }

        [Test]
        public void AuthoredVoices_RetainIdentityAndTimingEstimateMatchesWindowTempoPcm()
        {
            const string phrase = "Маршрут отменили. Я всё равно жду.";
            var identities = new HashSet<uint>();
            float conservative = RussianVocalizerSynthesis.EstimateDuration(phrase);
            for (int i = 0; i < NpcVoiceCatalog.Count; i++)
            {
                NpcVoiceProfile profile = NpcVoiceCatalog.ProfileAt(i);
                RussianVocalizerVoice voice = profile.VocalizerVoice;
                Assert.That(voice.IsValid, Is.True);
                Assert.That(identities.Add(voice.Seed), Is.True, "Authored speakers keep distinct stable identities");
                Assert.That(voice.Seed, Is.EqualTo(profile.Hash));
                Assert.That(voice.Gender, Is.EqualTo(profile.Gender));
                Assert.That(RussianVocalizerSynthesis.EstimateDuration(phrase, voice),
                    Is.LessThanOrEqualTo(conservative));
            }

            RussianVocalizerVoice natural = NpcVoiceCatalog.ProfileAt(0).VocalizerVoice;
            RussianVocalizerVoice quicker = natural.WithTempo(natural.Tempo * .4);
            float[] pcm = RussianVocalizerSynthesis.Generate(phrase, quicker);
            Assert.That(quicker.Seed, Is.EqualTo(natural.Seed));
            Assert.That(quicker.FundamentalHz, Is.EqualTo(natural.FundamentalHz));
            Assert.That(quicker.TractScale, Is.EqualTo(natural.TractScale));
            Assert.That(quicker.Breath, Is.EqualTo(natural.Breath));
            Assert.That(RussianVocalizerSynthesis.ResolveSampleCount(phrase, quicker), Is.EqualTo(pcm.Length));
            Assert.That(RussianVocalizerSynthesis.EstimateDuration(phrase, quicker),
                Is.EqualTo(pcm.Length / (float)RussianVocalizerSynthesis.SampleRate));
            Assert.That(pcm.Length, Is.LessThan(RussianVocalizerSynthesis.ResolveSampleCount(phrase, natural)));
        }

        [Test]
        public void Synthesis_RejectsInvalidVoiceAndHonorsCancellationWithoutAudio()
        {
            Assert.That(RussianVocalizerSynthesis.Generate("Latin 123", 0), Is.Empty);
            Assert.That(RussianVocalizerSynthesis.Generate(string.Empty, 1), Is.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => RussianVocalizerSynthesis.Generate("я", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RussianVocalizerSynthesis.Generate("я", RussianVocalizerSynthesis.VoiceCount));
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    RussianVocalizerSynthesis.Generate("Хорошо. Сейчас подам.", 0, canceled.Token));
            }
            float[] maximum = RussianVocalizerSynthesis.Generate(
                new string('я', RussianSpeechPhonetics.MaximumTextLength * 4), 0);
            Assert.That(maximum.Length, Is.LessThanOrEqualTo(
                (int)(RussianVocalizerSynthesis.SampleRate * RussianVocalizerSynthesis.MaximumDurationSeconds)));
        }

        [Test]
        public void TwelveAuthoredPresets_HaveExplicitGenderAndDistinctRepeatableFiniteAudio()
        {
            var signatures = new HashSet<ulong>();
            var names = new HashSet<string>();
            int female = 0;
            double quietest = double.MaxValue, loudest = 0;
            for (int voiceIndex = 0; voiceIndex < RussianVocalizerSynthesis.VoiceCount; voiceIndex++)
            {
                RussianVocalizerVoice voice = RussianVocalizerSynthesis.LabVoiceAt(voiceIndex);
                Assert.That(names.Add(RussianVocalizerSynthesis.LabVoiceNameAt(voiceIndex)), Is.True);
                Assert.That(RussianVocalizerSynthesis.LabVoiceDescriptionAt(voiceIndex), Is.Not.Empty);
                Assert.That(voice.Gender, Is.EqualTo(RussianVocalizerSynthesis.LabVoiceGenderAt(voiceIndex)));
                if (voice.Gender == RussianVocalizerGender.Female) female++;
                RussianVocalizerPlan plan = RussianVocalizerSynthesis.Prepare("Молоко", voice);
                float[] pcm = RussianVocalizerSynthesis.Render(plan);
                CollectionAssert.AreEqual(pcm, RussianVocalizerSynthesis.Render(plan));
                Assert.That(pcm.Length, Is.EqualTo(plan.SampleCount));
                bool finite = true;
                double energy = 0, peak = 0;
                ulong signature = 14695981039346656037ul;
                foreach (float sample in pcm)
                {
                    finite &= !float.IsNaN(sample) && !float.IsInfinity(sample);
                    energy += sample * sample;
                    peak = Math.Max(peak, Math.Abs(sample));
                    signature = unchecked((signature ^ (uint)sample.GetHashCode()) * 1099511628211ul);
                }
                Assert.That(finite, Is.True);
                Assert.That(peak, Is.InRange(.02, RussianVocalizerSynthesis.MaximumAmplitude + .000001));
                Assert.That(signatures.Add(signature), Is.True, "Presets must change the actual rendered signal");
                double rms = Math.Sqrt(energy / pcm.Length);
                Assert.That(rms, Is.InRange(.015, .2));
                quietest = Math.Min(quietest, rms); loudest = Math.Max(loudest, rms);
            }
            Assert.That(female, Is.EqualTo(4));
            Assert.That(loudest / quietest, Is.LessThan(2.5), "Comparison should not be driven by loudness alone");
        }

        [Test]
        public void PhoneTimeline_RevealsAtOnsetAndHoldsDuringPunctuationPause()
        {
            const string text = "Мёд, се\u0301мья.";
            RussianVocalizerPlan plan = RussianVocalizerSynthesis.Prepare(text, RussianVocalizerSynthesis.LabVoiceAt(5));
            Assert.That(plan.TextLength, Is.EqualTo(text.Length));
            Assert.That(plan.RevealedCharactersAt(0), Is.EqualTo(1));
            float previous = 0;
            for (int i = 0; i < text.Length; i++)
            {
                float time = plan.CharacterTimeSeconds(i);
                Assert.That(time, Is.GreaterThanOrEqualTo(previous));
                Assert.That(plan.RevealedCharactersAt(time), Is.GreaterThan(i));
                previous = time;
            }
            Assert.That(plan.CharacterTimeSeconds(6), Is.EqualTo(plan.CharacterTimeSeconds(7)),
                "The stress mark belongs to its vowel");
            Assert.That(plan.CharacterTimeSeconds(8), Is.EqualTo(plan.CharacterTimeSeconds(9)),
                "The soft sign belongs to its preceding consonant");
            float pauseMiddle = (plan.CharacterTimeSeconds(3) + plan.CharacterTimeSeconds(5)) * .5f;
            Assert.That(plan.IsSpeakingAt(pauseMiddle), Is.False);
            Assert.That(plan.RevealedCharactersAt(pauseMiddle), Is.EqualTo(5));
            Assert.That(plan.RevealedCharactersAt(plan.DurationSeconds), Is.EqualTo(text.Length));
            Assert.That(plan.IsSpeakingAt(plan.DurationSeconds), Is.False);
        }

        [Test]
        public void AccentAndWindowPreparation_KeepCanonicalVowelsAndExactCompletePcm()
        {
            RussianVocalizerVoice baseVoice = RussianVocalizerSynthesis.LabVoiceAt(5).WithIdentity(123u);
            RussianVocalizerVoice full = baseVoice.WithStyle(new RussianVocalizerStyle(vowelReduction: 0));
            RussianVocalizerVoice reduced = baseVoice.WithStyle(new RussianVocalizerStyle(vowelReduction: 1));
            IReadOnlyList<RussianSpeechPhone> phones = RussianSpeechPhonetics.Plan("Молоко");
            Assert.That(phones[1].Sound, Is.EqualTo(RussianSpeechSound.A));
            Assert.That(phones[1].CanonicalSound, Is.EqualTo(RussianSpeechSound.O));
            RussianVocalizerPlan fullPlan = RussianVocalizerSynthesis.Prepare("Молоко", full);
            float[] first = RussianVocalizerSynthesis.Render(fullPlan);
            float[] second = RussianVocalizerSynthesis.Render(RussianVocalizerSynthesis.Prepare("Молоко", reduced));
            CollectionAssert.AreNotEqual(first, second);
            CollectionAssert.AreEqual(first, RussianVocalizerSynthesis.Render(
                RussianVocalizerSynthesis.Prepare("Молоко", full)), "An accent must not mutate cached canonical phones");
            const string phrase = "Хорошо. Сейчас подам.";
            RussianVocalizerPlan natural = RussianVocalizerSynthesis.Prepare(phrase, baseVoice);
            RussianVocalizerPlan fitted = RussianVocalizerSynthesis.Prepare(phrase, baseVoice, 1.2);
            Assert.That(fitted.SampleCount, Is.LessThan(natural.SampleCount));
            Assert.That(fitted.SampleCount, Is.LessThanOrEqualTo((int)(1.2 * RussianVocalizerSynthesis.SampleRate)));
            Assert.That(RussianVocalizerSynthesis.Render(fitted).Length, Is.EqualTo(fitted.SampleCount));
            Assert.That(fitted.RevealedCharactersAt(fitted.DurationSeconds), Is.EqualTo(phrase.Length));
            Assert.That(fitted.Voice.FundamentalHz, Is.EqualTo(baseVoice.FundamentalHz));
            Assert.That(fitted.Voice.Seed, Is.EqualTo(baseVoice.Seed));
            Assert.Throws<ArgumentOutOfRangeException>(() => RussianVocalizerSynthesis.Prepare(phrase, baseVoice, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => RussianVocalizerSynthesis.Prepare(phrase, baseVoice, -.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => RussianVocalizerSynthesis.Prepare(phrase, baseVoice, .000001));
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                Assert.Throws<OperationCanceledException>(() => RussianVocalizerSynthesis.Render(fitted, canceled.Token));
            }
        }

        [Test]
        public void FullyRevealedFinalVowel_RemainsAnOpenSpeakingMouthUntilItsPcmEnds()
        {
            RussianVocalizerPlan plan = RussianVocalizerSynthesis.Prepare("А", RussianVocalizerSynthesis.LabVoiceAt(5));
            SpeechDelivery delivery = SpeechDelivery.Spoken("А", 0);
            delivery.VocalizerPlan = plan;
            float middle = plan.DurationSeconds * .5f;
            delivery.Step(middle, out _);
            Assert.That(delivery.RevealedCharacters, Is.EqualTo(1));
            Assert.That(plan.IsSpeakingAt(middle), Is.True);
            var sample = new SpeechFaceSample(1, "А", delivery.RevealedCharacters,
                plan.IsSpeakingAt(middle), middle, hasPhoneticTiming: true);
            Assert.That(sample.IsTyping, Is.True);
            Assert.That(SpeechFaceAnimation.Resolve(sample, SpeechFaceProfile.Hero, middle).Mouth,
                Is.EqualTo(SpeechMouthPose.Open));
        }

        [TestCase(22050)]
        [TestCase(48000)]
        public void ContinuousVowel_KeepsPhaseAcrossBuffersAndSmoothsPitchGate(int sampleRate)
        {
            RussianVocalizerVoice hero = NpcVoiceCatalog.ProfileAt(NpcVoiceCatalog.HeroMutterOrdinal).VocalizerVoice;
            var whole = new RussianVocalizerSustain(hero, sampleRate);
            var split = new RussianVocalizerSustain(hero, sampleRate);
            var reference = new float[sampleRate / 2];
            var buffered = new float[reference.Length];
            whole.Render(reference, 0, reference.Length, 110f, true);
            int cursor = 0;
            while (cursor < buffered.Length)
            {
                int count = Math.Min(257, buffered.Length - cursor);
                split.Render(buffered, cursor, count, 110f, true);
                cursor += count;
            }
            CollectionAssert.AreEqual(reference, buffered, "A PCM callback boundary must not reset phase, filters or envelopes");
            Assert.That(split.SampleRate, Is.EqualTo(sampleRate));
            Assert.That(split.Envelope, Is.GreaterThan(.99f));
            Assert.That(split.SmoothedFrequencyHz, Is.EqualTo(110f).Within(.001f));
            double energy = 0, mean = 0;
            foreach (float value in buffered)
            {
                Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False);
                Assert.That(Math.Abs(value), Is.LessThanOrEqualTo(RussianVocalizerSynthesis.MaximumAmplitude));
                energy += value * value;
                mean += value;
            }
            Assert.That(Math.Sqrt(energy / buffered.Length), Is.GreaterThan(.005), "Holding the gate must produce an audible vowel");
            Assert.That(Math.Abs(mean / buffered.Length), Is.LessThan(.015), "The held vowel must not accumulate DC");

            var glide = new float[sampleRate / 50];
            split.Render(glide, 0, 1, 440f, true);
            Assert.That(split.SmoothedFrequencyHz, Is.InRange(110.001f, 113f), "The first glide sample cannot jump an octave");
            split.Render(glide, 1, glide.Length - 1, 440f, true);
            Assert.That(split.SmoothedFrequencyHz, Is.InRange(423.5f, 439.999f),
                "The voice must cover at least 95% of a pitch change within 20 ms of source time.");
            var release = new float[sampleRate / 2];
            split.Render(release, 0, release.Length, 440f, false);
            Assert.That(split.Envelope, Is.Zero);
            for (int i = release.Length - 256; i < release.Length; i++)
                Assert.That(release[i], Is.Zero, "Mouse release settles at exact silence without a repeating tail");

            split.Reset();
            split.Render(buffered, 0, buffered.Length, 110f, true);
            CollectionAssert.AreEqual(reference, buffered, "A stopped voice can be reused without retaining the previous note");
        }

        [TestCase(22050)]
        [TestCase(48000)]
        public void OutputVowel_UsesLatestPitchAndPreservesWholeVoiceRate(int outputRate)
        {
            RussianVocalizerVoice hero = NpcVoiceCatalog.ProfileAt(NpcVoiceCatalog.HeroMutterOrdinal).VocalizerVoice;
            var voice = new RussianVocalizerSustainOutput(hero);
            var block = new float[512]; // One stereo output block; no future clip data.
            for (int i = 0; i < 20; i++) voice.Render(block, 2, outputRate, 110f, true, 1f);
            float before = voice.SmoothedFrequencyHz;
            voice.Render(block, 2, outputRate, 440f, true, 1f);
            Assert.That(voice.SmoothedFrequencyHz, Is.GreaterThan(before + 100f),
                "The very next DSP block uses the latest control, without a prefilled PCM queue.");
            int settleBlocks = (int)Math.Ceiling(outputRate * .020 / 256);
            for (int i = 1; i < settleBlocks; i++) voice.Render(block, 2, outputRate, 440f, true, 1f);
            Assert.That(voice.SmoothedFrequencyHz, Is.GreaterThanOrEqualTo(423.5f));
            for (int i = 0; i < block.Length; i += 2)
            {
                Assert.That(block[i], Is.EqualTo(block[i + 1]), "Channel layout must not speed up the mono voice.");
                Assert.That(float.IsFinite(block[i]), Is.True);
                Assert.That(Math.Abs(block[i]), Is.LessThanOrEqualTo(RussianVocalizerSynthesis.MaximumAmplitude));
            }

            var slowed = new RussianVocalizerSustainOutput(hero);
            var equivalent = new RussianVocalizerSustainOutput(hero);
            var actual = new float[4096];
            var expected = new float[4096];
            slowed.Render(actual, 2, outputRate, 220f, true, .25f);
            equivalent.Render(expected, 2, outputRate * 4, 220f, true, 1f);
            CollectionAssert.AreEqual(expected, actual, "Perception slows the whole vowel, including its formants.");

            var partitioned = new RussianVocalizerSustainOutput(hero);
            var collected = new float[4096];
            for (int offset = 0; offset < collected.Length; offset += block.Length)
            {
                partitioned.Render(block, 2, outputRate, 220f, true, .25f);
                Array.Copy(block, 0, collected, offset, block.Length);
            }
            CollectionAssert.AreEqual(actual, collected, "DSP block boundaries preserve resampler and vocal phase.");
            slowed.Reset();
            slowed.Render(actual, 2, outputRate, 220f, false, 1f);
            Assert.That(actual, Is.All.Zero, "Stop/reset cannot retain an audible interpolation tail.");
        }

        private static List<RussianSpeechSound> StressedSounds(string text)
        {
            var sounds = new List<RussianSpeechSound>();
            foreach (RussianSpeechPhone phone in RussianSpeechPhonetics.Plan(text))
                if (phone.Stressed) sounds.Add(phone.Sound);
            return sounds;
        }

        private static void AssertSounds(IReadOnlyList<RussianSpeechPhone> phones,
            params RussianSpeechSound[] expected)
        {
            Assert.That(phones.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++) Assert.That(phones[i].Sound, Is.EqualTo(expected[i]));
        }

        private static void AssertPhonesEqual(IReadOnlyList<RussianSpeechPhone> first,
            IReadOnlyList<RussianSpeechPhone> second)
        {
            Assert.That(first.Count, Is.EqualTo(second.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second[i].Sound, Is.EqualTo(first[i].Sound));
                Assert.That(second[i].Soft, Is.EqualTo(first[i].Soft));
                Assert.That(second[i].Stressed, Is.EqualTo(first[i].Stressed));
                Assert.That(second[i].DurationSeconds, Is.EqualTo(first[i].DurationSeconds));
            }
        }
    }
}
