using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The keystroke every spoken line ticks with. Shaped on
    /// <c>RetroSfxLibraryTests</c> and <c>CemeteryRavenVoiceTests</c>:
    /// the run is muted globally by <c>AutomaticTestAudioMute</c>, so
    /// what is asserted is the sample buffer and the catalog, never the
    /// audible output.
    /// </summary>
    public sealed class NpcSpeechVoiceTests
    {
        [Test]
        public void Catalog_HoldsEveryAuthoredSpeakerOnce()
        {
            // Eight NPCs and the hero, who is the ninth and last.
            Assert.That(NpcVoiceCatalog.Count, Is.EqualTo(9));
            Assert.That(
                NpcVoiceCatalog.DesignIdAt(
                    NpcVoiceCatalog.HeroMutterOrdinal),
                Is.EqualTo(NpcVoiceCatalog.HeroMutterDesignId));
            Assert.That(
                NpcVoiceCatalog.FallbackVoiceCount,
                Is.EqualTo(NpcVoiceCatalog.Count - 1));

            var seenIds = new HashSet<string>();
            var seenDesigns = new HashSet<string>();
            for (int index = 0; index < NpcVoiceCatalog.Count; index++)
            {
                NpcVoiceProfile profile =
                    NpcVoiceCatalog.ProfileAt(index);
                string designId = NpcVoiceCatalog.DesignIdAt(index);

                Assert.That(profile.IsValid, Is.True);
                Assert.That(seenIds.Add(profile.Id), Is.True,
                    "Two voices must never share a clip name.");
                Assert.That(seenDesigns.Add(designId), Is.True);
                Assert.That(
                    profile.FundamentalHz,
                    Is.InRange(100f, 400f),
                    "A keystroke is a click with a pitch, not a note.");
                Assert.That(
                    profile.TimbreRatio,
                    Is.InRange(1.5f, 3.5f));
                Assert.That(profile.NoiseShare, Is.InRange(0f, 0.3f));
                Assert.That(profile.JitterCents, Is.InRange(0f, 60f));
                Assert.That(profile.Volume, Is.InRange(0.05f, 0.5f));

                // A voice keyed on a design id that no longer exists is
                // a rename nobody would hear until two men swapped
                // tones, so it fails here instead. The hero is the one
                // exception and always will be: he is not an NPC, and the
                // appearance catalog deliberately excludes his models.
                if (index != NpcVoiceCatalog.HeroMutterOrdinal)
                {
                    Assert.That(
                        NpcDesignAppearanceCatalog.TryGet(
                            designId,
                            out _),
                        Is.True,
                        "'" + designId + "' is not a design in the game.");
                }
                Assert.That(
                    NpcVoiceCatalog.ResolveOrdinal(designId),
                    Is.EqualTo(index));
            }
        }

        [Test]
        public void Catalog_PlacesAnUnlistedDesignOnAnAuthoredVoice()
        {
            // A design nobody has written a voice for still gets a real
            // one, deterministically, rather than a ninth invented clip.
            const string unlisted = "some_future_speaker_v9";
            int first = NpcVoiceCatalog.ResolveOrdinal(unlisted);
            int second = NpcVoiceCatalog.ResolveOrdinal(unlisted);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(
                first,
                Is.InRange(0, NpcVoiceCatalog.FallbackVoiceCount - 1));

            // And never onto the hero's own mutter, however many designs
            // are added later: a stranger who sounds like him would be a
            // bug nobody could name.
            for (int index = 0; index < 4096; index++)
            {
                Assert.That(
                    NpcVoiceCatalog.ResolveOrdinal(
                        "generated_speaker_v" + index),
                    Is.Not.EqualTo(NpcVoiceCatalog.HeroMutterOrdinal));
            }
            Assert.That(
                NpcVoiceCatalog.ResolveOrdinal(string.Empty),
                Is.EqualTo(NpcVoiceCatalog.SilentOrdinal),
                "Nobody in particular is nobody, not a random man.");
        }

        [Test]
        public void Pitch_GivesALetterItsOwnNoteAndKeepsIt()
        {
            NpcVoiceProfile voice = NpcVoiceCatalog.Resolve(
                NpcVoiceCatalog.WatchmanDesignId);

            Assert.That(
                NpcVoiceCatalog.ResolveCharacterStep('а'),
                Is.EqualTo(NpcVoiceCatalog.ResolveCharacterStep('А')),
                "Upper and lower case are the same key.");

            var steps = new HashSet<int>();
            foreach (char letter in "абвгдеёжзийклмнопрстуфхцчшщыэюя")
            {
                int step = NpcVoiceCatalog.ResolveCharacterStep(letter);
                Assert.That(
                    step,
                    Is.InRange(0, NpcVoiceCatalog.SemitoneRange - 1));
                steps.Add(step);
            }

            Assert.That(
                steps.Count,
                Is.GreaterThan(6),
                "The alphabet must spread across the octave, or the " +
                "line reads as one flat tone.");

            for (uint ordinal = 0; ordinal < 40; ordinal++)
            {
                float pitch = NpcVoiceCatalog.ResolveBlipPitch(
                    voice,
                    'а',
                    ordinal);
                Assert.That(pitch, Is.InRange(0.5f, 4f));
                Assert.That(float.IsNaN(pitch), Is.False);
            }

            // The jitter moves the note, it does not replace it.
            float bare = Mathf.Pow(
                2f,
                NpcVoiceCatalog.ResolveCharacterStep('а') / 12f);
            float jittered =
                NpcVoiceCatalog.ResolveBlipPitch(voice, 'а', 7u);
            Assert.That(
                Mathf.Abs(jittered - bare),
                Is.LessThan(bare * 0.05f));
        }

        [Test]
        public void Blip_IsDeterministicFiniteAndAudible()
        {
            for (int index = 0; index < NpcVoiceCatalog.Count; index++)
            {
                NpcVoiceProfile voice =
                    NpcVoiceCatalog.ProfileAt(index);
                float[] first =
                    NpcSpeechBlipSynthesis.GenerateBlip(voice);
                float[] second =
                    NpcSpeechBlipSynthesis.GenerateBlip(voice);

                CollectionAssert.AreEqual(
                    first,
                    second,
                    voice.Id + " must synthesize byte-identically.");
                Assert.That(
                    first.Length,
                    Is.EqualTo(NpcSpeechBlipSynthesis.SampleCount));
                Assert.That(first[0], Is.Zero);
                Assert.That(first[first.Length - 1], Is.Zero);

                float peak = 0f;
                double energy = 0d;
                for (int sample = 0; sample < first.Length; sample++)
                {
                    float value = first[sample];
                    Assert.That(float.IsNaN(value), Is.False);
                    Assert.That(float.IsInfinity(value), Is.False);
                    peak = Mathf.Max(peak, Mathf.Abs(value));
                    energy += value * (double)value;
                }

                Assert.That(
                    peak,
                    Is.GreaterThan(0.04f),
                    voice.Id + " has to be audible at all.");
                Assert.That(
                    peak,
                    Is.LessThanOrEqualTo(
                        NpcSpeechBlipSynthesis.MaximumAmplitude +
                        0.0001f),
                    voice.Id + " must respect the family's cap.");
                Assert.That(energy, Is.GreaterThan(0.01d));
            }
        }

        [Test]
        public void Blip_TellsEverySpeakerApart()
        {
            var buffers = new float[NpcVoiceCatalog.Count][];
            for (int index = 0; index < buffers.Length; index++)
            {
                buffers[index] = NpcSpeechBlipSynthesis.GenerateBlip(
                    NpcVoiceCatalog.ProfileAt(index));
            }

            for (int first = 0; first < buffers.Length; first++)
            {
                for (int second = first + 1;
                     second < buffers.Length;
                     second++)
                {
                    Assert.That(
                        buffers[first],
                        Is.Not.EqualTo(buffers[second]),
                        NpcVoiceCatalog.ProfileAt(first).Id +
                        " and " +
                        NpcVoiceCatalog.ProfileAt(second).Id +
                        " must not be the same sound.");
                }
            }

            // The two park players are the pair heard alternating
            // inside ten seconds, so «different» has to mean audibly
            // different rather than merely not byte-equal.
            float chess = MeanAbsolute(buffers[
                NpcVoiceCatalog.ResolveOrdinal(
                    NpcVoiceCatalog.ChessPlayerDesignId)]);
            float checkers = MeanAbsolute(buffers[
                NpcVoiceCatalog.ResolveOrdinal(
                    NpcVoiceCatalog.CheckersPlayerDesignId)]);
            Assert.That(chess, Is.GreaterThan(0f));
            Assert.That(checkers, Is.GreaterThan(0f));
        }

        [Test]
        public void Blip_IsShorterThanTheThrottleThatSpacesIt()
        {
            // Two keystrokes must never overlap into one held tone.
            Assert.That(
                NpcSpeechBlipSynthesis.DurationSeconds,
                Is.LessThan(
                    SpeechDelivery.MinimumBlipIntervalSeconds));

            // And the throttle must be well clear of the typing rate,
            // or the line rattles instead of ticking.
            float letterInterval =
                1f / SpeechDelivery.CharactersPerSecond;
            Assert.That(
                SpeechDelivery.MinimumBlipIntervalSeconds,
                Is.GreaterThan(letterInterval * 2f));
        }

        [Test]
        public void ClipCache_SharesOneBankAndBuriesItOnTheLastRelease()
        {
            NpcSpeechBlipClipCache.Lease first =
                NpcSpeechBlipClipCache.Acquire();
            NpcSpeechBlipClipCache.Lease second =
                NpcSpeechBlipClipCache.Acquire();
            try
            {
                Assert.That(
                    first.Clips.Count,
                    Is.EqualTo(NpcVoiceCatalog.Count));
                for (int index = 0;
                     index < NpcVoiceCatalog.Count;
                     index++)
                {
                    AudioClip clip = first.ClipAt(index);
                    Assert.That(clip, Is.Not.Null);
                    Assert.That(
                        clip,
                        Is.SameAs(second.ClipAt(index)),
                        "Both leases play the same instances.");
                    Assert.That(clip.channels, Is.EqualTo(1));
                    Assert.That(
                        clip.frequency,
                        Is.EqualTo(NpcSpeechBlipSynthesis.SampleRate));
                    Assert.That(
                        clip.hideFlags,
                        Is.EqualTo(HideFlags.DontSave));
                }

                second.Dispose();
                second.Dispose();
                Assert.That(
                    first.ClipAt(0),
                    Is.Not.Null,
                    "A second Dispose must not underflow the count " +
                    "and bury a live consumer's clips.");
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }

            Assert.That(
                NpcSpeechBlipClipCache.LiveLeaseCount,
                Is.Zero);
        }

        private static float MeanAbsolute(float[] samples)
        {
            double total = 0d;
            for (int index = 0; index < samples.Length; index++)
            {
                total += Mathf.Abs(samples[index]);
            }

            return (float)(total / Mathf.Max(1, samples.Length));
        }
    }
}
