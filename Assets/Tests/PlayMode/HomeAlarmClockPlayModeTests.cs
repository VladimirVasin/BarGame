using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeAlarmClockPlayModeTests
    {
        [UnityTest]
        public IEnumerator
            StartStop_UsesOwnedSpatialSourceRattleAndCleanup()
        {
            var root = new GameObject("Home Alarm Clock Test");
            root.transform.localPosition =
                new Vector3(2f, 0.8f, -1f);
            root.transform.localRotation =
                Quaternion.Euler(0f, 28f, 0f);
            HomeAlarmClock alarm =
                root.AddComponent<HomeAlarmClock>();
            yield return null;

            Assert.That(alarm.IsInitialized, Is.True);
            Assert.That(alarm.IsRinging, Is.False);
            Assert.That(HomeAlarmClock.OwnedSourceCount, Is.EqualTo(1));
            Assert.That(HomeAlarmClock.RuntimeClipCount, Is.EqualTo(1));
            Assert.That(
                alarm.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(1));

            AudioSource source = alarm.Source;
            AudioClip clip = alarm.ActiveClip;
            Assert.That(source, Is.Not.Null);
            Assert.That(source.gameObject, Is.Not.SameAs(root));
            Assert.That(
                source.transform.parent,
                Is.SameAs(root.transform));
            Assert.That(source.playOnAwake, Is.False);
            Assert.That(source.loop, Is.True);
            Assert.That(source.spatialBlend, Is.EqualTo(1f));
            Assert.That(source.dopplerLevel, Is.Zero);
            Assert.That(
                source.rolloffMode,
                Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(source.minDistance, Is.GreaterThan(0f));
            Assert.That(
                source.maxDistance,
                Is.GreaterThan(source.minDistance));
            Assert.That(
                source.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.SfxWorldGroup));

            Assert.That(clip, Is.Not.Null);
            Assert.That(
                clip.name,
                Is.EqualTo(HomeAlarmClock.RuntimeClipName));
            Assert.That(
                clip.frequency,
                Is.EqualTo(HomeAlarmClock.SampleRate));
            Assert.That(clip.channels, Is.EqualTo(1));
            Assert.That(
                clip.samples,
                Is.EqualTo(
                    Mathf.RoundToInt(
                        HomeAlarmClockSynthesis.SampleRate *
                        HomeAlarmClockSynthesis.LoopDuration)));

            Vector3 restPosition = alarm.RestLocalPosition;
            Quaternion restRotation = alarm.RestLocalRotation;
            alarm.StartRinging();
            Assert.That(alarm.IsRinging, Is.True);
            Assert.That(alarm.RattleElapsedSeconds, Is.Zero);

            alarm.AdvanceRattle(0.013f);
            Assert.That(
                alarm.RattleElapsedSeconds,
                Is.EqualTo(0.013f).Within(0.000001f));
            Assert.That(
                Vector3.Distance(
                    root.transform.localPosition,
                    restPosition),
                Is.GreaterThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    root.transform.localRotation,
                    restRotation),
                Is.GreaterThan(0.1f));

            alarm.StopRinging();
            Assert.That(alarm.IsRinging, Is.False);
            Assert.That(alarm.RattleElapsedSeconds, Is.Zero);
            Assert.That(
                root.transform.localPosition,
                Is.EqualTo(restPosition));
            Assert.That(
                root.transform.localRotation,
                Is.EqualTo(restRotation));

            alarm.StartRinging();
            alarm.StartRinging();
            Assert.That(alarm.Source, Is.SameAs(source));
            Assert.That(alarm.ActiveClip, Is.SameAs(clip));
            Assert.That(
                alarm.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(1));

            Object.Destroy(root);
            yield return null;
            yield return null;

            Assert.That(source == null, Is.True);
            Assert.That(clip == null, Is.True);
        }
    }
}
