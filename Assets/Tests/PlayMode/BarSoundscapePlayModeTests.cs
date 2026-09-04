using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarSoundscapePlayModeTests
    {
        [UnityTest]
        public IEnumerator Initialize_BuildsSixSourcePositionalBarMix()
        {
            GameObject root =
                new GameObject("Bar Audio Root");

            GameObject musicObject =
                new GameObject("Bar Music");
            musicObject.transform.SetParent(root.transform, false);
            musicObject.transform.position =
                new Vector3(6.4f, 0.5f, -6.5f);
            BarMusicPlayer music =
                musicObject.AddComponent<BarMusicPlayer>();
            music.ConfigureJukebox(26f, 1f);

            GameObject ambienceObject =
                new GameObject("Bar Ambience");
            ambienceObject.transform.SetParent(root.transform, false);
            BarAmbiencePlayer ambience =
                ambienceObject.AddComponent<BarAmbiencePlayer>();

            GameObject soundscapeObject =
                new GameObject("Bar Soundscape");
            soundscapeObject.transform.SetParent(root.transform, false);
            BarSoundscape soundscape =
                soundscapeObject.AddComponent<BarSoundscape>();
            Vector3 firstCrowdPosition =
                new Vector3(-6f, 1.2f, 2f);
            Vector3 secondCrowdPosition =
                new Vector3(3f, 1.2f, -2f);
            Vector3 servicePosition =
                new Vector3(2f, 1f, 4f);
            soundscape.Initialize(
                7231,
                firstCrowdPosition,
                secondCrowdPosition,
                servicePosition,
                16f,
                0.8f,
                15f,
                0.75f,
                11f,
                0.7f);
            yield return null;

            Assert.That(soundscape.IsInitialized, Is.True);
            Assert.That(music.ActiveClip, Is.Not.Null);
            Assert.That(ambience.ActiveClip, Is.Not.Null);
            Assert.That(
                root.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    BarSoundscape.CompatibleSceneSourceCount));
            Assert.That(
                soundscape.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    BarSoundscape.OwnedSourceCount));
            Assert.That(music.Source.spatialBlend, Is.EqualTo(1f));
            Assert.That(
                music.Source.rolloffMode,
                Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(
                music.Source.minDistance,
                Is.EqualTo(BarMusicPlayer.MinimumDistance));
            Assert.That(music.Source.maxDistance, Is.EqualTo(26f));
            Assert.That(
                music.Source.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.MusicGroup));
            Assert.That(
                music.ToneFilter.cutoffFrequency,
                Is.EqualTo(
                    BarMusicPlayer.SpeakerLowPassFrequency).Within(1f));
            Assert.That(
                music.SpeakerHighPass.cutoffFrequency,
                Is.EqualTo(
                    BarMusicPlayer.SpeakerHighPassFrequency).Within(1f));
            Assert.That(
                music.SpeakerDistortion.distortionLevel,
                Is.EqualTo(
                    BarMusicPlayer.SpeakerDistortionLevel).Within(0.001f));
            Assert.That(music.CabinetSource, Is.Not.Null);
            Assert.That(music.CabinetSource.loop, Is.True);
            Assert.That(music.CabinetSource.spatialBlend, Is.EqualTo(1f));
            Assert.That(
                music.CabinetSource.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.AmbienceDetailsGroup));
            AssertClip(
                music.CabinetClip,
                BarMusicPlayer.CabinetSampleRate);
            Assert.That(ambience.Source.spatialBlend, Is.Zero);
            Assert.That(ambience.Source.volume, Is.EqualTo(0.09f));
            Assert.That(soundscape.FirstCrowdSource.loop, Is.True);
            Assert.That(soundscape.SecondCrowdSource.loop, Is.True);
            Assert.That(soundscape.CueSource.loop, Is.False);
            Assert.That(
                soundscape.FirstCrowdSource.outputAudioMixerGroup,
                Is.SameAs(
                    GameAudioMixer.AmbienceDetailsGroup));
            Assert.That(
                soundscape.SecondCrowdSource.outputAudioMixerGroup,
                Is.SameAs(
                    GameAudioMixer.AmbienceDetailsGroup));
            Assert.That(
                soundscape.FirstCrowdSource
                    .outputAudioMixerGroup.name,
                Is.EqualTo("Details"));
            Assert.That(
                soundscape.CueSource.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.SfxWorldGroup));
            Assert.That(
                soundscape.CueSource
                    .outputAudioMixerGroup.name,
                Is.EqualTo("World"));
            Assert.That(
                soundscape.FirstCrowdSource.spatialBlend,
                Is.EqualTo(1f));
            Assert.That(
                soundscape.SecondCrowdSource.spatialBlend,
                Is.EqualTo(1f));
            Assert.That(soundscape.CueSource.spatialBlend, Is.EqualTo(1f));
            Assert.That(
                soundscape.FirstCrowdSource.transform.position,
                Is.EqualTo(firstCrowdPosition));
            Assert.That(
                soundscape.SecondCrowdSource.transform.position,
                Is.EqualTo(secondCrowdPosition));
            Assert.That(
                soundscape.FirstCrowdSource.maxDistance,
                Is.EqualTo(16f));
            Assert.That(
                soundscape.FirstCrowdSource.volume,
                Is.EqualTo(0.34f * 0.8f).Within(0.0001f));
            Assert.That(
                soundscape.SecondCrowdSource.maxDistance,
                Is.EqualTo(15f));
            Assert.That(
                soundscape.SecondCrowdSource.volume,
                Is.EqualTo(0.3f * 0.75f).Within(0.0001f));
            Assert.That(soundscape.CueSource.maxDistance, Is.EqualTo(11f));
            AssertClip(soundscape.FirstCrowdClip);
            AssertClip(soundscape.SecondCrowdClip);
            AssertClip(soundscape.GlassClinkClip);
            AssertClip(soundscape.ChairScrapeClip);
            AssertClip(soundscape.BottleSetDownClip);
            AssertClip(soundscape.CrowdReactionClip);
            Assert.That(
                soundscape.SecondsUntilNextCue,
                Is.InRange(
                    BarSoundscapeSchedule.MinimumDelaySeconds,
                    BarSoundscapeSchedule.MaximumDelaySeconds));
            Assert.That(
                soundscape.gameObject.scene,
                Is.EqualTo(root.scene));

            float firstDelay = soundscape.SecondsUntilNextCue;
            soundscape.AdvanceSoundscape(firstDelay + 0.01f);
            Assert.That(soundscape.HasPlayedCue, Is.True);
            Vector3 expectedCuePosition =
                ResolveExpectedCuePosition(
                    soundscape.LastPlayedCue.Kind,
                    firstCrowdPosition,
                    secondCrowdPosition,
                    servicePosition);
            Assert.That(
                soundscape.LastCuePosition,
                Is.EqualTo(expectedCuePosition));
            Assert.That(
                soundscape.CueSource.transform.position,
                Is.EqualTo(expectedCuePosition));

            AudioClip firstCrowd = soundscape.FirstCrowdClip;
            AudioClip secondCrowd = soundscape.SecondCrowdClip;
            AudioClip glass = soundscape.GlassClinkClip;
            AudioClip chair = soundscape.ChairScrapeClip;
            AudioClip bottle = soundscape.BottleSetDownClip;
            AudioClip reaction = soundscape.CrowdReactionClip;
            AudioClip cabinet = music.CabinetClip;
            Object.Destroy(root);
            yield return null;
            yield return null;

            Assert.That(firstCrowd == null, Is.True);
            Assert.That(secondCrowd == null, Is.True);
            Assert.That(glass == null, Is.True);
            Assert.That(chair == null, Is.True);
            Assert.That(bottle == null, Is.True);
            Assert.That(reaction == null, Is.True);
            Assert.That(cabinet == null, Is.True);
        }

        [UnityTest]
        public IEnumerator Reinitialize_ReusesSourcesAndResetsSchedule()
        {
            GameObject soundscapeObject =
                new GameObject("Reusable Bar Soundscape");
            BarSoundscape soundscape =
                soundscapeObject.AddComponent<BarSoundscape>();
            soundscape.Initialize(
                11,
                Vector3.zero,
                Vector3.forward);
            AudioSource firstCrowdSource =
                soundscape.FirstCrowdSource;
            AudioSource secondCrowdSource =
                soundscape.SecondCrowdSource;
            AudioSource cueSource = soundscape.CueSource;
            AudioClip firstCrowdClip = soundscape.FirstCrowdClip;
            AudioClip secondCrowdClip = soundscape.SecondCrowdClip;

            soundscape.AdvanceSoundscape(
                BarSoundscapeSchedule.MaximumDelaySeconds + 1f);
            Assert.That(soundscape.HasPlayedCue, Is.True);
            Assert.That(soundscape.CueSequence, Is.EqualTo(1));

            soundscape.Initialize(
                91,
                Vector3.left,
                Vector3.right,
                Vector3.forward,
                14f,
                0.8f,
                15f,
                0.7f,
                9f,
                0.6f);

            Assert.That(
                soundscape.FirstCrowdSource,
                Is.SameAs(firstCrowdSource));
            Assert.That(
                soundscape.SecondCrowdSource,
                Is.SameAs(secondCrowdSource));
            Assert.That(
                soundscape.CueSource,
                Is.SameAs(cueSource));
            Assert.That(
                soundscape.FirstCrowdClip,
                Is.SameAs(firstCrowdClip));
            Assert.That(
                soundscape.SecondCrowdClip,
                Is.SameAs(secondCrowdClip));
            Assert.That(soundscape.HasPlayedCue, Is.False);
            Assert.That(soundscape.CueSequence, Is.Zero);
            Assert.That(
                soundscape.FirstCrowdSource.transform.position,
                Is.EqualTo(Vector3.left));
            Assert.That(
                soundscape.SecondCrowdSource.transform.position,
                Is.EqualTo(Vector3.right));
            Assert.That(
                soundscape.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    BarSoundscape.OwnedSourceCount));
            Assert.That(
                soundscape.SecondsUntilNextCue,
                Is.EqualTo(
                    BarSoundscapeSchedule.GetCue(91, 0)
                        .DelaySeconds));

            Object.Destroy(soundscapeObject);
            yield return null;
        }

        private static Vector3 ResolveExpectedCuePosition(
            BarSoundscapeCueKind kind,
            Vector3 firstCrowdPosition,
            Vector3 secondCrowdPosition,
            Vector3 servicePosition)
        {
            if (kind == BarSoundscapeCueKind.ChairScrape)
            {
                return firstCrowdPosition;
            }

            return kind == BarSoundscapeCueKind.CrowdReaction
                ? secondCrowdPosition
                : servicePosition;
        }

        private static void AssertClip(
            AudioClip clip,
            int sampleRate = BarSoundscape.SampleRate)
        {
            Assert.That(clip, Is.Not.Null);
            Assert.That(
                clip.frequency,
                Is.EqualTo(sampleRate));
            Assert.That(clip.channels, Is.EqualTo(1));
        }
    }
}
