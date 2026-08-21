using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class InteriorSoundscapePlayModeTests
    {
        [UnityTest]
        public IEnumerator
            Stairwell_UsesThreeSpatialSourcesAndDeterministicCues()
        {
            var root = new GameObject("Stairwell Soundscape Test");
            root.AddComponent<AudioListener>();
            StairwellSoundscape soundscape =
                root.AddComponent<StairwellSoundscape>();
            StairwellSoundscapeAnchors anchors =
                CreateStairwellAnchors(Vector3.zero);
            const int seed = 913;
            soundscape.Initialize(seed, anchors);

            Assert.That(soundscape.IsInitialized, Is.True);
            Assert.That(
                soundscape.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    StairwellSoundscape.OwnedSourceCount));
            Assert.That(
                StairwellSoundscape.OwnedSourceCount,
                Is.EqualTo(3));
            AudioSource[] sources =
            {
                soundscape.VentilationSource,
                soundscape.ElectricalSource,
                soundscape.RareCueSource
            };
            AssertSourceConfiguration(sources[0], true);
            AssertSourceConfiguration(sources[1], true);
            AssertSourceConfiguration(sources[2], false);
            Assert.That(GameAudioMixer.IsAvailable, Is.True);
            for (int index = 0; index < sources.Length; index++)
            {
                Assert.That(
                    sources[index].outputAudioMixerGroup,
                    Is.SameAs(
                        GameAudioMixer.AmbienceDetailsGroup));
            }

            AudioClip[] clips = GetClips(soundscape);
            Assert.That(
                clips,
                Has.Length.EqualTo(
                    StairwellSoundscape.RuntimeClipCount));
            AssertClips(clips, StairwellSoundscape.SampleRate);
            Assert.That(
                soundscape.SecondsUntilNextCue,
                Is.EqualTo(
                    StairwellSoundscapeSchedule
                        .GetCue(seed, 0)
                        .DelaySeconds));

            StairwellSoundscapeCue expected =
                StairwellSoundscapeSchedule.GetCue(seed, 0);
            soundscape.AdvanceSoundscape(
                expected.DelaySeconds + 0.01f);
            Assert.That(soundscape.HasPlayedCue, Is.True);
            Assert.That(soundscape.CueSequence, Is.EqualTo(1));
            Assert.That(
                soundscape.LastPlayedCue.Kind,
                Is.EqualTo(expected.Kind));
            Assert.That(
                soundscape.LastPlayedPosition,
                Is.EqualTo(
                    soundscape.GetCuePosition(expected.Kind)));
            Assert.That(
                soundscape.RareCueSource.transform.position,
                Is.EqualTo(soundscape.LastPlayedPosition));
            Assert.That(
                soundscape.RareCueSource.clip,
                Is.SameAs(GetCueClip(soundscape, expected.Kind)));

            AudioSource ventilationSource =
                soundscape.VentilationSource;
            AudioSource electricalSource =
                soundscape.ElectricalSource;
            AudioSource cueSource = soundscape.RareCueSource;
            AudioClip ventilationClip =
                soundscape.VentilationClip;
            StairwellSoundscapeAnchors shifted =
                CreateStairwellAnchors(
                    new Vector3(20f, 1f, -8f));
            soundscape.Initialize(442, shifted);
            float reinitializedDelay =
                soundscape.SecondsUntilNextCue;

            Assert.That(
                soundscape.VentilationSource,
                Is.SameAs(ventilationSource));
            Assert.That(
                soundscape.ElectricalSource,
                Is.SameAs(electricalSource));
            Assert.That(
                soundscape.RareCueSource,
                Is.SameAs(cueSource));
            Assert.That(
                soundscape.VentilationClip,
                Is.SameAs(ventilationClip));
            Assert.That(soundscape.HasPlayedCue, Is.False);
            Assert.That(soundscape.CueSequence, Is.Zero);
            Assert.That(
                reinitializedDelay,
                Is.EqualTo(
                    StairwellSoundscapeSchedule
                        .GetCue(442, 0)
                        .DelaySeconds));
            Assert.That(
                soundscape.VentilationSource.transform.position,
                Is.EqualTo(shifted.Ventilation));
            Assert.That(
                soundscape.RareCueSource.transform.position,
                Is.EqualTo(shifted.PipeKnock));

            soundscape.Initialize(442, shifted);
            Assert.That(
                soundscape.SecondsUntilNextCue,
                Is.EqualTo(reinitializedDelay));
            Assert.That(
                soundscape.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    StairwellSoundscape.OwnedSourceCount));

            Object.Destroy(root);
            yield return null;
            yield return null;
            AssertDestroyed(clips, sources);
        }

        [UnityTest]
        public IEnumerator
            Home_UsesSixSpatialSourcesAndDeterministicCues()
        {
            var root = new GameObject("Home Soundscape Test");
            root.AddComponent<AudioListener>();
            HomeSoundscape soundscape =
                root.AddComponent<HomeSoundscape>();
            HomeSoundscapeAnchors anchors =
                CreateHomeAnchors(Vector3.zero);
            const int seed = -218;
            soundscape.Initialize(seed, anchors);

            Assert.That(soundscape.IsInitialized, Is.True);
            Assert.That(
                soundscape.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    HomeSoundscape.OwnedSourceCount));
            Assert.That(
                HomeSoundscape.OwnedSourceCount,
                Is.EqualTo(6));
            AudioSource[] sources =
            {
                soundscape.ClosedRefrigeratorSource,
                soundscape.OpenRefrigeratorSource,
                soundscape.BalconySource,
                soundscape.RareCueSource,
                soundscape.BathroomLightCrackleSource,
                soundscape.ShowerWaterSource
            };
            AssertSourceConfiguration(sources[0], true);
            AssertSourceConfiguration(sources[1], true);
            AssertSourceConfiguration(sources[2], true);
            AssertSourceConfiguration(sources[3], false);
            AssertSourceConfiguration(sources[4], false);
            AssertSourceConfiguration(sources[5], true);
            Assert.That(
                soundscape.RefrigeratorSource,
                Is.SameAs(soundscape.ClosedRefrigeratorSource));
            Assert.That(
                sources[0].transform.position,
                Is.EqualTo(sources[1].transform.position));
            Assert.That(
                sources[0].transform.position,
                Is.EqualTo(anchors.Refrigerator));
            Assert.That(
                sources[4].transform.position,
                Is.EqualTo(anchors.BathroomLight));
            Assert.That(
                sources[4],
                Is.Not.SameAs(soundscape.RareCueSource));
            AudioLowPassFilter closedRefrigeratorFilter =
                sources[0].GetComponent<AudioLowPassFilter>();
            AudioLowPassFilter openRefrigeratorFilter =
                sources[1].GetComponent<AudioLowPassFilter>();
            Assert.That(closedRefrigeratorFilter, Is.Not.Null);
            Assert.That(openRefrigeratorFilter, Is.Not.Null);
            Assert.That(
                sources[0].volume,
                Is.EqualTo(
                    HomeSoundscape.ClosedRefrigeratorVolume)
                    .Within(0.0001f));
            Assert.That(
                20f * Mathf.Log10(
                    HomeSoundscape.ClosedRefrigeratorVolume /
                    0.095f),
                Is.EqualTo(4f).Within(0.001f));
            Assert.That(
                20f * Mathf.Log10(
                    HomeSoundscape.OpenRefrigeratorVolume /
                    0.122f),
                Is.EqualTo(4f).Within(0.001f));
            Assert.That(
                HomeSoundscape.OpenRefrigeratorVolume /
                HomeSoundscape.ClosedRefrigeratorVolume,
                Is.EqualTo(0.122f / 0.095f).Within(0.0001f));
            Assert.That(sources[1].volume, Is.Zero.Within(0.0001f));
            Assert.That(
                closedRefrigeratorFilter.cutoffFrequency,
                Is.EqualTo(
                    HomeSoundscape.ClosedRefrigeratorCutoff)
                    .Within(0.1f));
            Assert.That(
                openRefrigeratorFilter.cutoffFrequency,
                Is.EqualTo(
                    HomeSoundscape.OpenRefrigeratorCutoff)
                    .Within(0.1f));
            Assert.That(
                sources[4].GetComponent<AudioLowPassFilter>()
                    .cutoffFrequency,
                Is.EqualTo(
                    HomeSoundscape.BathroomLightCrackleCutoff)
                    .Within(0.1f));
            Assert.That(
                sources[4].volume,
                Is.EqualTo(
                    HomeSoundscape.BathroomLightCrackleVolume)
                    .Within(0.0001f));
            Assert.That(sources[4].clip, Is.Null);

            soundscape.SetRefrigeratorDoorOpenAmount(0.5f);
            float equalPowerWeight = Mathf.Sqrt(0.5f);
            Assert.That(
                soundscape.ClosedRefrigeratorMixWeight,
                Is.EqualTo(equalPowerWeight).Within(0.0001f));
            Assert.That(
                soundscape.OpenRefrigeratorMixWeight,
                Is.EqualTo(equalPowerWeight).Within(0.0001f));
            Assert.That(
                sources[0].volume,
                Is.EqualTo(
                    HomeSoundscape.ClosedRefrigeratorVolume *
                    equalPowerWeight).Within(0.0001f));
            Assert.That(
                sources[1].volume,
                Is.EqualTo(
                    HomeSoundscape.OpenRefrigeratorVolume *
                    equalPowerWeight).Within(0.0001f));

            soundscape.SetRefrigeratorDoorOpenAmount(1f);
            Assert.That(
                soundscape.RefrigeratorDoorOpenAmount,
                Is.EqualTo(1f));
            Assert.That(
                sources[0].volume,
                Is.Zero.Within(0.0001f));
            Assert.That(
                sources[1].volume,
                Is.EqualTo(
                    HomeSoundscape.OpenRefrigeratorVolume)
                    .Within(0.0001f));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => soundscape.SetRefrigeratorDoorOpenAmount(
                    float.NaN));
            soundscape.SetRefrigeratorDoorOpenAmount(0f);
            Assert.That(GameAudioMixer.IsAvailable, Is.True);
            for (int index = 0; index < sources.Length; index++)
            {
                Assert.That(
                    sources[index].outputAudioMixerGroup,
                    Is.SameAs(
                        GameAudioMixer.AmbienceDetailsGroup));
            }

            AudioClip[] clips = GetClips(soundscape);
            Assert.That(
                clips,
                Has.Length.EqualTo(
                    HomeSoundscape.RuntimeClipCount));
            AssertClips(clips, HomeSoundscape.SampleRate);
            Assert.That(
                soundscape.ClosedRefrigeratorClip.samples,
                Is.EqualTo(
                    Mathf.RoundToInt(
                        HomeSoundscape.SampleRate *
                        HomeSoundscapeSynthesis.LoopDuration)));
            Assert.That(
                soundscape.OpenRefrigeratorClip.samples,
                Is.EqualTo(
                    soundscape.ClosedRefrigeratorClip.samples));
            Assert.That(
                soundscape.OpenRefrigeratorClip,
                Is.Not.SameAs(
                    soundscape.ClosedRefrigeratorClip));
            Assert.That(
                soundscape.SecondsUntilNextCue,
                Is.EqualTo(
                    HomeSoundscapeSchedule.GetCue(seed, 0)
                        .DelaySeconds));

            HomeSoundscapeCue expected =
                HomeSoundscapeSchedule.GetCue(seed, 0);
            soundscape.AdvanceSoundscape(
                expected.DelaySeconds + 0.01f);
            Assert.That(soundscape.HasPlayedCue, Is.True);
            Assert.That(soundscape.CueSequence, Is.EqualTo(1));
            Assert.That(
                soundscape.LastPlayedCue.Kind,
                Is.EqualTo(expected.Kind));
            Assert.That(
                soundscape.LastPlayedPosition,
                Is.EqualTo(
                    soundscape.GetCuePosition(expected.Kind)));
            Assert.That(
                soundscape.RareCueSource.transform.position,
                Is.EqualTo(soundscape.LastPlayedPosition));
            Assert.That(
                soundscape.RareCueSource.clip,
                Is.SameAs(GetCueClip(soundscape, expected.Kind)));

            AudioSource closedRefrigeratorSource =
                soundscape.ClosedRefrigeratorSource;
            AudioSource openRefrigeratorSource =
                soundscape.OpenRefrigeratorSource;
            AudioSource balconySource = soundscape.BalconySource;
            AudioSource cueSource = soundscape.RareCueSource;
            AudioSource bathroomLightSource =
                soundscape.BathroomLightCrackleSource;
            AudioClip closedRefrigeratorClip =
                soundscape.ClosedRefrigeratorClip;
            AudioClip openRefrigeratorClip =
                soundscape.OpenRefrigeratorClip;
            AudioClip bathroomLightCrackleClip =
                soundscape.BathroomLightCrackleClip;
            HomeSoundscapeAnchors shifted =
                CreateHomeAnchors(new Vector3(-11f, 2f, 17f));
            soundscape.Initialize(721, shifted);
            float reinitializedDelay =
                soundscape.SecondsUntilNextCue;

            Assert.That(
                soundscape.ClosedRefrigeratorSource,
                Is.SameAs(closedRefrigeratorSource));
            Assert.That(
                soundscape.OpenRefrigeratorSource,
                Is.SameAs(openRefrigeratorSource));
            Assert.That(
                soundscape.BalconySource,
                Is.SameAs(balconySource));
            Assert.That(
                soundscape.RareCueSource,
                Is.SameAs(cueSource));
            Assert.That(
                soundscape.BathroomLightCrackleSource,
                Is.SameAs(bathroomLightSource));
            Assert.That(
                soundscape.ClosedRefrigeratorClip,
                Is.SameAs(closedRefrigeratorClip));
            Assert.That(
                soundscape.OpenRefrigeratorClip,
                Is.SameAs(openRefrigeratorClip));
            Assert.That(
                soundscape.BathroomLightCrackleClip,
                Is.SameAs(bathroomLightCrackleClip));
            Assert.That(soundscape.HasPlayedCue, Is.False);
            Assert.That(soundscape.CueSequence, Is.Zero);
            Assert.That(
                reinitializedDelay,
                Is.EqualTo(
                    HomeSoundscapeSchedule.GetCue(721, 0)
                        .DelaySeconds));
            Assert.That(
                soundscape.ClosedRefrigeratorSource.transform.position,
                Is.EqualTo(shifted.Refrigerator));
            Assert.That(
                soundscape.OpenRefrigeratorSource.transform.position,
                Is.EqualTo(shifted.Refrigerator));
            Assert.That(
                soundscape.RareCueSource.transform.position,
                Is.EqualTo(shifted.SoftWood));
            Assert.That(
                soundscape.BathroomLightCrackleSource
                    .transform.position,
                Is.EqualTo(shifted.BathroomLight));

            soundscape.Initialize(721, shifted);
            Assert.That(
                soundscape.SecondsUntilNextCue,
                Is.EqualTo(reinitializedDelay));
            Assert.That(
                soundscape.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    HomeSoundscape.OwnedSourceCount));

            yield return new WaitForSecondsRealtime(0.05f);
            Assert.That(
                soundscape.ClosedRefrigeratorSource.isPlaying,
                Is.True);
            Assert.That(
                soundscape.OpenRefrigeratorSource.isPlaying,
                Is.True);
            root.SetActive(false);
            Assert.That(
                soundscape.ClosedRefrigeratorSource.isPlaying,
                Is.False);
            Assert.That(
                soundscape.OpenRefrigeratorSource.isPlaying,
                Is.False);
            Assert.That(soundscape.BalconySource.isPlaying, Is.False);
            root.SetActive(true);
            yield return new WaitForSecondsRealtime(0.05f);
            Assert.That(
                soundscape.ClosedRefrigeratorSource.isPlaying,
                Is.True);
            Assert.That(
                soundscape.OpenRefrigeratorSource.isPlaying,
                Is.True);
            Assert.That(soundscape.BalconySource.isPlaying, Is.True);

            Object.Destroy(root);
            yield return null;
            yield return null;
            AssertDestroyed(clips, sources);
        }

        [UnityTest]
        public IEnumerator
            Home_BathroomCrackleFollowsAppliedFlickerTransitions()
        {
            var root = new GameObject("Home Flicker Audio Test");
            root.AddComponent<AudioListener>();
            HomeSoundscape soundscape =
                root.AddComponent<HomeSoundscape>();
            soundscape.Initialize(
                82,
                CreateHomeAnchors(Vector3.zero));

            GameObject bathroomObject =
                new GameObject("Bathroom Flicker Test Light");
            bathroomObject.transform.SetParent(root.transform, false);
            Light bathroomLight =
                bathroomObject.AddComponent<Light>();
            bathroomLight.intensity = 2f;
            GameObject spillObject =
                new GameObject("Bathroom Flicker Test Spill");
            spillObject.transform.SetParent(root.transform, false);
            Light spillLight = spillObject.AddComponent<Light>();
            spillLight.intensity = 10f;
            HomeBathroomLightFlicker flicker =
                bathroomObject.AddComponent<
                    HomeBathroomLightFlicker>();
            flicker.Initialize(bathroomLight, spillLight);
            flicker.enabled = false;

            int eventCount = 0;
            float callbackFactor = 0f;
            float callbackBathroomIntensity = 0f;
            flicker.AppliedFactorChanged += factor =>
            {
                eventCount++;
                callbackFactor = factor;
                callbackBathroomIntensity = bathroomLight.intensity;
            };
            soundscape.BindBathroomFlicker(flicker);
            soundscape.BindBathroomFlicker(flicker);

            flicker.ApplyAtElapsedSeconds(4.72f);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(
                callbackFactor,
                Is.EqualTo(0.18f).Within(0.0001f));
            Assert.That(
                callbackBathroomIntensity,
                Is.EqualTo(2f * callbackFactor).Within(0.0001f),
                "The event must run after the discrete factor is applied.");
            Assert.That(
                soundscape.BathroomLightCracklePlayCount,
                Is.EqualTo(1));
            Assert.That(
                soundscape.BoundBathroomFlicker,
                Is.SameAs(flicker));

            flicker.ApplyAtElapsedSeconds(4.72f);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(
                soundscape.BathroomLightCracklePlayCount,
                Is.EqualTo(1),
                "Reapplying one factor must not trigger another crackle.");

            flicker.ApplyAtElapsedSeconds(4.82f);
            Assert.That(eventCount, Is.EqualTo(2));
            Assert.That(
                soundscape.BathroomLightCracklePlayCount,
                Is.EqualTo(2));

            float[] remainingVisualEdges =
            {
                4.90f,
                5.00f,
                5.08f,
                5.16f,
                5.24f
            };
            for (int index = 0;
                 index < remainingVisualEdges.Length;
                 index++)
            {
                flicker.ApplyAtElapsedSeconds(
                    remainingVisualEdges[index]);
            }

            Assert.That(
                eventCount,
                Is.EqualTo(7),
                "One callback is required for every visual flicker edge.");
            Assert.That(
                soundscape.BathroomLightCracklePlayCount,
                Is.EqualTo(7),
                "Every visual edge must trigger exactly one crackle.");
            Assert.That(
                soundscape.BathroomLightCrackleClip.length,
                Is.LessThanOrEqualTo(0.06f),
                "A crackle must finish before the next visual edge.");

            soundscape.UnbindBathroomFlicker();
            flicker.ApplyAtElapsedSeconds(4.72f);
            Assert.That(eventCount, Is.EqualTo(8));
            Assert.That(
                soundscape.BathroomLightCracklePlayCount,
                Is.EqualTo(7));

            soundscape.BindBathroomFlicker(flicker);
            AudioSource crackleSource =
                soundscape.BathroomLightCrackleSource;
            AudioClip crackleClip =
                soundscape.BathroomLightCrackleClip;
            Object.Destroy(soundscape);
            yield return null;
            yield return null;

            Assert.That(crackleSource == null, Is.True);
            Assert.That(crackleClip == null, Is.True);
            Assert.DoesNotThrow(
                () => flicker.ApplyAtElapsedSeconds(5.00f),
                "Destroy must unsubscribe from the surviving flicker.");

            Object.Destroy(root);
            yield return null;
        }

        private static void AssertSourceConfiguration(
            AudioSource source,
            bool loops)
        {
            Assert.That(source, Is.Not.Null);
            Assert.That(source.playOnAwake, Is.False);
            Assert.That(source.loop, Is.EqualTo(loops));
            Assert.That(source.spatialBlend, Is.EqualTo(1f));
            Assert.That(source.dopplerLevel, Is.Zero);
            Assert.That(
                source.rolloffMode,
                Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(source.minDistance, Is.GreaterThan(0f));
            Assert.That(
                source.maxDistance,
                Is.GreaterThan(source.minDistance));
            Assert.That(source.volume, Is.InRange(0f, 0.2f));
        }

        private static void AssertClips(
            AudioClip[] clips,
            int sampleRate)
        {
            for (int index = 0; index < clips.Length; index++)
            {
                Assert.That(clips[index], Is.Not.Null);
                Assert.That(
                    clips[index].frequency,
                    Is.EqualTo(sampleRate));
                Assert.That(clips[index].channels, Is.EqualTo(1));
                Assert.That(clips[index].samples, Is.GreaterThan(0));
            }
        }

        private static void AssertDestroyed(
            AudioClip[] clips,
            AudioSource[] sources)
        {
            for (int index = 0; index < clips.Length; index++)
            {
                Assert.That(clips[index] == null, Is.True);
            }

            for (int index = 0; index < sources.Length; index++)
            {
                Assert.That(sources[index] == null, Is.True);
            }
        }

        private static AudioClip[] GetClips(
            StairwellSoundscape soundscape)
        {
            return new[]
            {
                soundscape.VentilationClip,
                soundscape.ElectricalClip,
                soundscape.PipeKnockClip,
                soundscape.MetalStressClip,
                soundscape.DistantWaterClip,
                soundscape.DistantMovementClip
            };
        }

        private static AudioClip[] GetClips(
            HomeSoundscape soundscape)
        {
            return new[]
            {
                soundscape.ClosedRefrigeratorClip,
                soundscape.OpenRefrigeratorClip,
                soundscape.BalconyClip,
                soundscape.SoftWoodClip,
                soundscape.RadiatorTickClip,
                soundscape.RadioMurmurClip,
                soundscape.BathroomDetailClip,
                soundscape.BathroomLightCrackleClip,
                soundscape.ShowerWaterClip
            };
        }

        private static AudioClip GetCueClip(
            StairwellSoundscape soundscape,
            StairwellSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case StairwellSoundscapeCueKind.PipeKnock:
                    return soundscape.PipeKnockClip;
                case StairwellSoundscapeCueKind.MetalStress:
                    return soundscape.MetalStressClip;
                case StairwellSoundscapeCueKind.DistantWater:
                    return soundscape.DistantWaterClip;
                default:
                    return soundscape.DistantMovementClip;
            }
        }

        private static AudioClip GetCueClip(
            HomeSoundscape soundscape,
            HomeSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case HomeSoundscapeCueKind.SoftWood:
                    return soundscape.SoftWoodClip;
                case HomeSoundscapeCueKind.RadiatorTick:
                    return soundscape.RadiatorTickClip;
                case HomeSoundscapeCueKind.RadioMurmur:
                    return soundscape.RadioMurmurClip;
                default:
                    return soundscape.BathroomDetailClip;
            }
        }

        private static StairwellSoundscapeAnchors
            CreateStairwellAnchors(Vector3 offset)
        {
            return new StairwellSoundscapeAnchors(
                offset + new Vector3(-3f, 4f, 2f),
                offset + new Vector3(1f, 5f, 3f),
                offset + new Vector3(-2f, 2f, 1f),
                offset + new Vector3(0f, 4f, -2f),
                offset + new Vector3(2f, 1f, 3f),
                offset + new Vector3(3f, 3f, -4f));
        }

        private static HomeSoundscapeAnchors CreateHomeAnchors(
            Vector3 offset)
        {
            return new HomeSoundscapeAnchors(
                offset + new Vector3(-3f, 1f, 2f),
                offset + new Vector3(5f, 2f, 1f),
                offset + new Vector3(-2f, 0.4f, -1f),
                offset + new Vector3(4f, 1f, 2f),
                offset + new Vector3(1f, 2f, 3f),
                offset + new Vector3(3f, 0.2f, 3f),
                offset + new Vector3(3f, 2.1f, 3.7f));
        }
    }
}
