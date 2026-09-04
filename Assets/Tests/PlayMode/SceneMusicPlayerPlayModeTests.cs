using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class SceneMusicPlayerPlayModeTests
    {
        private GameObject musicObject;
        private GameObject secondMusicObject;

        [SetUp]
        public void SetUp()
        {
            MusicMix.ClearFadeOuts();
        }

        [UnityTest]
        public IEnumerator Awake_StartsThemeAtZeroAndFadesToTargetGain()
        {
            CityMusicPlayer player = CreatePlayer();

            Assert.That(player.ActiveClip, Is.Not.Null);
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Loading)
                    .Or.EqualTo(
                        SceneMusicPlaybackState.FadingIn));
            yield return WaitForThemeReady(player);
            player.FadeOutAndPause(0f);
            player.ResumeWithFadeIn();

            Assert.That(player.Source.isPlaying, Is.True);
            Assert.That(player.NormalizedGain, Is.Zero.Within(0.0001f));
            Assert.That(player.Source.volume, Is.Zero.Within(0.0001f));
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.FadingIn));

            player.AdvanceFade(
                MusicMix.FadeInSeconds * 0.5f);
            Assert.That(
                player.NormalizedGain,
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                player.Source.volume,
                Is.EqualTo(
                    CityMusicPlayer.ThemeOutputVolume * 0.5f)
                    .Within(0.0001f));

            player.AdvanceFade(
                MusicMix.FadeInSeconds * 0.5f);
            Assert.That(player.NormalizedGain, Is.EqualTo(1f));
            Assert.That(
                player.Source.volume,
                Is.EqualTo(CityMusicPlayer.ThemeOutputVolume));
            Assert.That(player.IsFadeActive, Is.False);
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Playing));
        }

        [UnityTest]
        public IEnumerator FadeOutAndPause_ResumesThroughSameEnvelope()
        {
            CityMusicPlayer player = CreatePlayer();
            yield return WaitForThemeReady(player);
            player.AdvanceFade(
                MusicMix.FadeInSeconds);
            player.Source.timeSamples = 4096;
            int expectedPausedSample = player.Source.timeSamples;

            player.FadeOutAndPause(0.4f);
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.FadingOut));
            player.AdvanceFade(0.2f);
            Assert.That(
                player.NormalizedGain,
                Is.EqualTo(0.5f).Within(0.0001f));
            player.AdvanceFade(0.2f);

            Assert.That(player.NormalizedGain, Is.Zero.Within(0.0001f));
            Assert.That(player.Source.volume, Is.Zero.Within(0.0001f));
            Assert.That(player.IsPaused, Is.True);
            Assert.That(player.Source.isPlaying, Is.False);
            int pausedSample = player.Source.timeSamples;
            Assert.That(
                pausedSample,
                Is.EqualTo(expectedPausedSample).Within(8));

            player.ResumeWithFadeIn(0.4f);
            Assert.That(player.Source.isPlaying, Is.True);
            Assert.That(
                player.Source.timeSamples,
                Is.EqualTo(pausedSample).Within(8),
                "Resume must continue from the paused sample.");
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.FadingIn));
            player.AdvanceFade(0.4f);

            Assert.That(player.NormalizedGain, Is.EqualTo(1f));
            Assert.That(player.IsPaused, Is.False);
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Playing));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneExitFade_IsIdempotentAndReportsCompletion()
        {
            CityMusicPlayer player = CreatePlayer();
            yield return WaitForThemeReady(player);
            player.AdvanceFade(
                MusicMix.FadeInSeconds);

            Assert.That(player.RequestSceneExitFade(0.6f), Is.True);
            Assert.That(player.IsSceneExitFadeRequested, Is.True);
            Assert.That(player.IsSceneExitFadeComplete, Is.False);
            player.AdvanceFade(0.3f);

            Assert.That(player.RequestSceneExitFade(0.6f), Is.True);
            player.AdvanceFade(0.3f);
            Assert.That(player.IsSceneExitFadeComplete, Is.True);
            Assert.That(player.IsFadeActive, Is.False);
            Assert.That(player.NormalizedGain, Is.Zero.Within(0.0001f));
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Silent));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BarExitTail_PreservesBothSpatialLevels()
        {
            musicObject = new GameObject("Bar Exit Tail Test");
            BarMusicPlayer player =
                musicObject.AddComponent<BarMusicPlayer>();
            yield return WaitForThemeReady(player);
            player.AdvanceFade(MusicMix.FadeInSeconds);

            Vector3 listenerPosition = Vector3.right * 6.4f;
            float expectedThemeAttenuation =
                1f -
                (6.4f - BarMusicPlayer.MinimumDistance) /
                (BarMusicPlayer.DefaultMaximumDistance -
                 BarMusicPlayer.MinimumDistance);
            float expectedCabinetAttenuation =
                1f -
                (6.4f - player.CabinetSource.minDistance) /
                (BarMusicPlayer.CabinetMaximumDistance -
                 player.CabinetSource.minDistance);

            player.PrepareSpatialExitTail(listenerPosition);

            Assert.That(player.Source.spatialBlend, Is.Zero);
            Assert.That(player.CabinetSource.spatialBlend, Is.Zero);
            Assert.That(
                player.Source.volume,
                Is.EqualTo(
                    BarMusicPlayer.ThemeOutputVolume *
                    expectedThemeAttenuation).Within(0.0001f));
            Assert.That(
                player.CabinetSource.volume,
                Is.EqualTo(
                    BarMusicPlayer.CabinetOutputVolume *
                    expectedCabinetAttenuation).Within(0.0001f));
            Assert.That(
                expectedCabinetAttenuation,
                Is.LessThan(expectedThemeAttenuation),
                "The close cabinet texture must retain its own rolloff.");
        }

        [UnityTest]
        public IEnumerator MissingClip_CompletesSceneExitWithoutWaiting()
        {
            CityMusicPlayer player = CreatePlayer();
            yield return WaitForThemeReady(player);
            player.Source.Stop();
            player.Source.clip = null;

            Assert.That(player.RequestSceneExitFade(), Is.False);
            Assert.That(player.IsSceneExitFadeRequested, Is.True);
            Assert.That(player.IsSceneExitFadeComplete, Is.True);
            Assert.That(player.NormalizedGain, Is.Zero.Within(0.0001f));
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Unavailable));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledPlayer_CannotHoldSceneExitFadeOpen()
        {
            CityMusicPlayer player = CreatePlayer();
            yield return WaitForThemeReady(player);
            player.AdvanceFade(
                MusicMix.FadeInSeconds);

            Assert.That(player.RequestSceneExitFade(), Is.True);
            Assert.That(player.IsSceneExitFadeComplete, Is.False);
            player.enabled = false;

            Assert.That(player.IsSceneExitFadeComplete, Is.True);
            Assert.That(player.IsFadeActive, Is.False);
            Assert.That(player.NormalizedGain, Is.Zero.Within(0.0001f));
            Assert.That(
                player.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Silent));
            yield return null;
        }

        [UnityTest]
        public IEnumerator OutgoingTheme_HoldsTheNextThemeSilentUntilItEnds()
        {
            CityMusicPlayer outgoing = CreatePlayer();
            yield return WaitForThemeReady(outgoing);
            outgoing.AdvanceFade(MusicMix.FadeInSeconds);
            Assert.That(outgoing.NormalizedGain, Is.EqualTo(1f));

            Assert.That(outgoing.RequestSceneExitFade(), Is.True);
            Assert.That(
                outgoing.IsDetachedForSceneExit,
                Is.True,
                "The departing theme keeps fading outside its scene.");
            Assert.That(MusicMix.IsFadeOutActive, Is.True);

            secondMusicObject = new GameObject("Scene Music Rule Test");
            BarMusicPlayer incoming =
                secondMusicObject.AddComponent<BarMusicPlayer>();
            yield return WaitForThemeReady(incoming);

            Assert.That(
                incoming.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.WaitingForMix),
                "A new theme must not start over an unfinished fade-out.");
            Assert.That(incoming.IsFadeInDeferred, Is.True);
            Assert.That(incoming.Source.isPlaying, Is.False);
            Assert.That(incoming.NormalizedGain, Is.Zero.Within(0.0001f));

            outgoing.AdvanceFade(MusicMix.FadeOutSeconds);
            Assert.That(outgoing.IsSceneExitFadeComplete, Is.True);
            Assert.That(MusicMix.IsFadeOutActive, Is.False);
            yield return null;

            Assert.That(incoming.IsFadeInDeferred, Is.False);
            Assert.That(incoming.Source.isPlaying, Is.True);
            incoming.AdvanceFade(MusicMix.FadeInSeconds);
            Assert.That(
                incoming.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Playing));
            Assert.That(incoming.NormalizedGain, Is.EqualTo(1f));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (musicObject != null)
            {
                Object.Destroy(musicObject);
            }

            if (secondMusicObject != null)
            {
                Object.Destroy(secondMusicObject);
            }

            MusicMix.ClearFadeOuts();
            yield return null;
        }

        private CityMusicPlayer CreatePlayer()
        {
            musicObject = new GameObject("Scene Music Fade Test");
            return musicObject.AddComponent<CityMusicPlayer>();
        }

        private static IEnumerator WaitForThemeReady(
            SceneMusicPlayer player)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (player.PlaybackState ==
                       SceneMusicPlaybackState.Loading &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(player.ActiveClip, Is.Not.Null);
            Assert.That(
                player.PlaybackState,
                Is.Not.EqualTo(SceneMusicPlaybackState.Loading),
                "The streaming theme did not finish loading.");
            Assert.That(
                player.PlaybackState,
                Is.Not.EqualTo(SceneMusicPlaybackState.Unavailable),
                "The streaming theme failed to load.");
        }
    }
}
