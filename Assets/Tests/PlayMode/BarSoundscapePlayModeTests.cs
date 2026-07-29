using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarSoundscapePlayModeTests
    {
        [UnityTest]
        public IEnumerator Initialize_CoexistsWithinFourSourceBudget()
        {
            GameObject root =
                new GameObject("Bar Audio Root");

            GameObject musicObject =
                new GameObject("Bar Music");
            musicObject.transform.SetParent(root.transform, false);
            BarMusicPlayer music =
                musicObject.AddComponent<BarMusicPlayer>();

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
            soundscape.Initialize(
                7231,
                new Vector3(-3f, 1.2f, 2f),
                new Vector3(2f, 1f, 4f),
                11f,
                0.7f,
                7f,
                0.6f);
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
            Assert.That(soundscape.CrowdSource.loop, Is.True);
            Assert.That(soundscape.CueSource.loop, Is.False);
            Assert.That(
                soundscape.CrowdSource.spatialBlend,
                Is.GreaterThan(0f));
            Assert.That(
                soundscape.CueSource.spatialBlend,
                Is.GreaterThan(0f));
            Assert.That(soundscape.CrowdSource.maxDistance, Is.EqualTo(11f));
            Assert.That(
                soundscape.CrowdSource.volume,
                Is.EqualTo(0.24f * 0.7f).Within(0.0001f));
            Assert.That(soundscape.CueSource.maxDistance, Is.EqualTo(7f));
            AssertClip(soundscape.CrowdClip);
            AssertClip(soundscape.GlassClinkClip);
            AssertClip(soundscape.ChairScrapeClip);
            Assert.That(
                soundscape.SecondsUntilNextCue,
                Is.InRange(
                    BarSoundscapeSchedule.MinimumDelaySeconds,
                    BarSoundscapeSchedule.MaximumDelaySeconds));
            Assert.That(
                soundscape.gameObject.scene,
                Is.EqualTo(root.scene));

            AudioClip crowd = soundscape.CrowdClip;
            AudioClip glass = soundscape.GlassClinkClip;
            AudioClip chair = soundscape.ChairScrapeClip;
            Object.Destroy(root);
            yield return null;
            yield return null;

            Assert.That(crowd == null, Is.True);
            Assert.That(glass == null, Is.True);
            Assert.That(chair == null, Is.True);
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
            AudioSource crowdSource = soundscape.CrowdSource;
            AudioSource cueSource = soundscape.CueSource;
            AudioClip crowdClip = soundscape.CrowdClip;

            soundscape.AdvanceSoundscape(
                BarSoundscapeSchedule.MaximumDelaySeconds + 1f);
            Assert.That(soundscape.HasPlayedCue, Is.True);
            Assert.That(soundscape.CueSequence, Is.EqualTo(1));

            soundscape.Initialize(
                91,
                Vector3.left,
                Vector3.right);

            Assert.That(
                soundscape.CrowdSource,
                Is.SameAs(crowdSource));
            Assert.That(
                soundscape.CueSource,
                Is.SameAs(cueSource));
            Assert.That(
                soundscape.CrowdClip,
                Is.SameAs(crowdClip));
            Assert.That(soundscape.HasPlayedCue, Is.False);
            Assert.That(soundscape.CueSequence, Is.Zero);
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

        private static void AssertClip(AudioClip clip)
        {
            Assert.That(clip, Is.Not.Null);
            Assert.That(
                clip.frequency,
                Is.EqualTo(BarSoundscape.SampleRate));
            Assert.That(clip.channels, Is.EqualTo(1));
        }
    }
}
