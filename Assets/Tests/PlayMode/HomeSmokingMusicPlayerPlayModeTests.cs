using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeSmokingMusicPlayerPlayModeTests
    {
        private GameObject musicObject;
        private AudioClip testClip;

        [UnityTest]
        public IEnumerator Awake_ConfiguresSilentSceneLocalMusicSource()
        {
            HomeSmokingMusicPlayer player = CreatePlayer();
            yield return null;

            Assert.That(
                HomeSmokingMusicPlayer.ResourcePath,
                Is.EqualTo("Audio/SmokingMusic/smoking_theme"));
            Assert.That(player.Source, Is.Not.Null);
            Assert.That(player.Source.playOnAwake, Is.False);
            Assert.That(player.Source.loop, Is.True);
            Assert.That(player.Source.spatialBlend, Is.Zero);
            Assert.That(player.Source.dopplerLevel, Is.Zero);
            Assert.That(player.Source.priority, Is.EqualTo(64));
            Assert.That(player.Source.volume, Is.Zero);
            Assert.That(player.Source.isPlaying, Is.False);
            Assert.That(player.NormalizedGain, Is.Zero);
            Assert.That(
                player.Source.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.MusicGroup));
            Assert.That(player.ToneFilter, Is.Not.Null);
            Assert.That(
                player.ToneFilter.cutoffFrequency,
                Is.EqualTo(15500f).Within(1f));
            Assert.That(
                player.ToneFilter.lowpassResonanceQ,
                Is.EqualTo(1f).Within(0.01f));
            Assert.That(player.gameObject.scene.IsValid(), Is.True);
        }

        [UnityTest]
        public IEnumerator MissingClip_AllOperationsRemainSilentAndSafe()
        {
            HomeSmokingMusicPlayer player = CreatePlayer();
            player.Source.clip = null;

            Assert.DoesNotThrow(player.BeginFromStart);
            player.ApplyNormalizedGain(2f);

            Assert.That(player.ActiveClip, Is.Null);
            Assert.That(player.Source.isPlaying, Is.False);
            Assert.That(player.NormalizedGain, Is.EqualTo(1f));
            Assert.That(
                player.Source.volume,
                Is.EqualTo(HomeSmokingMusicPlayer.TargetVolume)
                    .Within(0.001f));

            Assert.DoesNotThrow(player.StopImmediate);
            Assert.That(player.Source.isPlaying, Is.False);
            Assert.That(player.NormalizedGain, Is.Zero);
            Assert.That(player.Source.volume, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlaybackControls_ResetAndClampGain()
        {
            HomeSmokingMusicPlayer player = CreatePlayer();
            testClip = AudioClip.Create(
                "Smoking Music Test Clip",
                44100,
                1,
                44100,
                false);
            player.Source.clip = testClip;
            player.Source.timeSamples = 5000;
            player.ApplyNormalizedGain(-1f);

            Assert.That(player.NormalizedGain, Is.Zero);
            Assert.That(player.Source.volume, Is.Zero);

            player.BeginFromStart();
            Assert.That(player.ActiveClip, Is.SameAs(testClip));
            Assert.That(player.Source.isPlaying, Is.True);
            Assert.That(player.Source.timeSamples, Is.LessThan(1024));

            player.ApplyNormalizedGain(0.4f);
            Assert.That(player.NormalizedGain, Is.EqualTo(0.4f));
            Assert.That(player.Source.volume, Is.EqualTo(0.2f).Within(0.001f));

            player.StopImmediate();
            Assert.That(player.Source.isPlaying, Is.False);
            Assert.That(player.Source.timeSamples, Is.Zero);
            Assert.That(player.NormalizedGain, Is.Zero);
            Assert.That(player.Source.volume, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisableAndDestroy_StopAndResetPlayback()
        {
            HomeSmokingMusicPlayer player = CreatePlayer();
            testClip = AudioClip.Create(
                "Smoking Music Lifecycle Test Clip",
                44100,
                1,
                44100,
                false);
            AudioSource source = player.Source;
            source.clip = testClip;

            player.BeginFromStart();
            player.ApplyNormalizedGain(1f);
            player.enabled = false;

            Assert.That(source.isPlaying, Is.False);
            Assert.That(source.timeSamples, Is.Zero);
            Assert.That(player.NormalizedGain, Is.Zero);
            Assert.That(source.volume, Is.Zero);

            player.enabled = true;
            player.BeginFromStart();
            player.ApplyNormalizedGain(1f);
            Object.Destroy(player);
            yield return null;

            Assert.That(source.isPlaying, Is.False);
            Assert.That(source.timeSamples, Is.Zero);
            Assert.That(source.volume, Is.Zero);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (musicObject != null)
            {
                Object.Destroy(musicObject);
            }

            if (testClip != null)
            {
                Object.Destroy(testClip);
            }

            yield return null;
        }

        private HomeSmokingMusicPlayer CreatePlayer()
        {
            musicObject = new GameObject("Home Smoking Music Test");
            return musicObject.AddComponent<HomeSmokingMusicPlayer>();
        }
    }
}
