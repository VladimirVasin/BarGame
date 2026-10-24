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

        [UnityTest]
        public IEnumerator IntoxicationPlaybackRate_PreservesMusicOwnersAndAuthoredTransport()
        {
            GameTimeScaleRuntime previousTempo = Object.FindAnyObjectByType<GameTimeScaleRuntime>();
            int previousLevel = GameSessionState.IntoxicationLevel;
            float previousPresentation = GameTimeScaleRuntime.SmoothedPresentationLevel;
            float previousAudio = GameTimeScaleRuntime.SmoothedAudioLevel;
            DrinkId previousDrink = GameSessionState.LastAlcoholicDrink;
            int previousDrinks = GameSessionState.DrinksConsumed;
            bool hadVoice = NpcSpeechVoice.Instance != null;
            int speechLease = -1;
            AudioClip probe = null;
            try
            {
                SetMusicIntoxication(0);
                CityMusicPlayer city = CreatePlayer();
                city.SetPlaybackSuppressed(false);
                yield return WaitForThemeReady(city);
                city.AdvanceFade(MusicMix.FadeInSeconds);
                AudioClip cityClip = city.ActiveClip;

                var smokingHost = new GameObject("Smoking perception contract");
                smokingHost.transform.SetParent(musicObject.transform, false);
                var smoking = smokingHost.AddComponent<HomeSmokingMusicPlayer>();
                probe = AudioClip.Create("Music transport probe", 64000, 1, 8000, false);
                smoking.Source.clip = probe;
                smoking.BeginFromStart();
                smoking.ApplyNormalizedGain(1f);
                var radioHost = new GameObject("Radio perception contract");
                radioHost.transform.SetParent(musicObject.transform, false);
                var radio = radioHost.AddComponent<LastRouteRadioMusicPlayer>();
                radio.SetPlaybackSuppressed(true);
                var combatHost = new GameObject("Mechanical tape perception contract");
                combatHost.transform.SetParent(musicObject.transform, false);
                var combat = combatHost.AddComponent<CombatWarehouseMusicPlayer>();
                combat.ApplyFanShutdown(0.5f);
                float mechanicalPitch = combat.Source.pitch;
                Assert.That(mechanicalPitch, Is.GreaterThan(0f).And.LessThan(CombatWarehouseMusicPlayer.PlaybackSpeed));
                IntoxicationAudioDriver tape = IntoxicationAudioDriver.EnsureInstalled();
                NpcSpeechVoice voice = NpcSpeechVoice.EnsureInstalled();
                speechLease = NpcSpeechVoice.Lease();
                Assert.That(speechLease, Is.GreaterThanOrEqualTo(0));
                AudioSource speech = voice.transform.Find(NpcSpeechVoice.VoiceObjectNamePrefix + speechLease)
                    .GetComponent<AudioSource>();
                NpcVoiceProfile voiceProfile = NpcVoiceCatalog.ProfileAt(0);
                float speechPitch = NpcVoiceCatalog.ResolveBlipPitch(voiceProfile, 'A', 0, 0f);
                Assert.That(NpcSpeechVoice.Blip(speechLease, 0, 'A', 0, Vector3.zero, 1f,
                    NpcEarshotProfile.Conversation), Is.True);

                float previousRate = float.PositiveInfinity;
                foreach (int level in new[] { 0, 50, 100 })
                {
                    SetMusicIntoxication(level);
                    yield return null;
                    float rate = GameTimeScaleRuntime.PerceptionPlaybackRate;
                    Assert.That(rate, Is.LessThan(previousRate));
                    Assert.That(city.Source.pitch, Is.EqualTo(rate).Within(0.0001f));
                    Assert.That(smoking.Source.pitch, Is.EqualTo(rate).Within(0.0001f));
                    Assert.That(radio.Source.pitch, Is.InRange(rate * 0.997f, rate * 1.003f),
                        "The radio's own wow must multiply the shared transport instead of overwriting it.");
                    Assert.That(combat.Source.pitch, Is.EqualTo(mechanicalPitch * rate).Within(0.0001f),
                        "A partially stopped cassette retains its mechanical speed beneath perception.");
                    Assert.That(city.ActiveClip, Is.SameAs(cityClip));
                    Assert.That(smoking.ActiveClip, Is.SameAs(probe));
                    Assert.That(city.Source.volume, Is.EqualTo(CityMusicPlayer.ThemeOutputVolume).Within(0.0001f));
                    Assert.That(smoking.Source.volume, Is.EqualTo(HomeSmokingMusicPlayer.TargetVolume).Within(0.0001f));
                    Assert.That(city.Source.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.MusicGroup));
                    Assert.That(smoking.Source.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.MusicGroup));
                    previousRate = rate;
                }
                Assert.That(city.Source.pitch, Is.EqualTo(0.25f).Within(0.0001f));

                foreach (int start in new[] { 72, 80 })
                {
                    SetMusicIntoxication(start);
                    yield return null;
                    int target = start == 72 ? 80 : 100;
                    float initialRate = GameTimeScaleRuntime.PerceptionPlaybackRate;
                    float finalRate = IntoxicationPerceptionRules.Evaluate(target).PlaybackRate;
                    GameSessionState.UpdateDrinkingProgress(target,
                        GameSessionState.LastAlcoholicDrink, GameSessionState.DrinksConsumed);
                    Assert.That(GameTimeScaleRuntime.SmoothedAudioLevel, Is.EqualTo(start),
                        "A drinking transaction cannot jump the audible perception.");
                    yield return new WaitForSecondsRealtime(0.25f);
                    yield return null;
                    float level = GameTimeScaleRuntime.SmoothedAudioLevel;
                    IntoxicationPerceptionProfile heard = GameTimeScaleRuntime.AudioPerception;
                    Assert.That(level, Is.GreaterThan(start).And.LessThan(target - 0.1f),
                        "Even +8/+20 must still be approaching its target after the old visual ramp has ended.");
                    Assert.That(heard.PlaybackRate, Is.LessThan(initialRate).And.GreaterThan(finalRate));
                    Assert.That(city.Source.pitch, Is.EqualTo(heard.PlaybackRate).Within(0.0001f));
                    Assert.That(smoking.Source.pitch, Is.EqualTo(heard.PlaybackRate).Within(0.0001f));
                    Assert.That(radio.Source.pitch, Is.InRange(heard.PlaybackRate * 0.997f,
                        heard.PlaybackRate * 1.003f));
                    Assert.That(combat.Source.pitch, Is.EqualTo(mechanicalPitch * heard.PlaybackRate).Within(0.0001f));
                    Assert.That(NpcSpeechVoice.Blip(speechLease, 0, 'A', 0, Vector3.zero, 1f,
                        NpcEarshotProfile.Conversation), Is.True);
                    Assert.That(speech.pitch, Is.EqualTo(speechPitch * heard.PlaybackRate).Within(0.0001f));
                    Assert.That(speech.volume, Is.EqualTo(voiceProfile.Volume * heard.SpeechGain).Within(0.0001f));
                    Assert.That(speech.GetComponent<AudioLowPassFilter>().cutoffFrequency,
                        Is.EqualTo(Mathf.Min(NpcSpeechVoice.LowPassFrequencyHz,
                            heard.SpeechLowPassFrequencyHz)).Within(0.01f));
                    Assert.That(tape.AppliedIntensity,
                        Is.GreaterThan(IntoxicationPerceptionRules.Evaluate(start).Intensity)
                            .And.LessThan(IntoxicationPerceptionRules.Evaluate(target).Intensity),
                        "The LateUpdate tape bus also eases, rather than following the completed visual ramp.");
                    Assert.That(city.ActiveClip, Is.SameAs(cityClip));
                    Assert.That(smoking.ActiveClip, Is.SameAs(probe));
                }

                float heldAudio = GameTimeScaleRuntime.SmoothedAudioLevel;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    GameSessionState.UpdateDrinkingProgress(80,
                        GameSessionState.LastAlcoholicDrink, GameSessionState.DrinksConsumed);
                    GameTimeScaleRuntime.SetIntoxicationLevel(80f, synchronizeAudio: false);
                    yield return new WaitForSecondsRealtime(0.15f);
                    Assert.That(GameTimeScaleRuntime.SmoothedAudioLevel, Is.EqualTo(heldAudio),
                        "Pause and player reconstruction retain the in-flight audible transition.");
                }
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(GameTimeScaleRuntime.SmoothedAudioLevel,
                    Is.GreaterThan(80f).And.LessThan(heldAudio),
                    "Sobering resumes gradually from the held perception.");
                SetMusicIntoxication(100);
                yield return null;
                combat.ApplyFanShutdown(1f);
                Assert.That(combat.Source.pitch, Is.Zero);
                yield return null;
                Assert.That(combat.Source.pitch, Is.Zero, "Perception cannot restart a stopped physical motor.");

                city.FadeOutAndPause(0.4f);
                city.AdvanceFade(0.2f);
                Assert.That(city.NormalizedGain, Is.EqualTo(0.5f).Within(0.0001f),
                    "Slow playback must retain the real unscaled fade duration.");
                city.AdvanceFade(0.2f);
                Assert.That(city.IsPaused, Is.True);
                int pausedSample = city.Source.timeSamples;
                SetMusicIntoxication(0);
                city.ResumeWithFadeIn(0f);
                Assert.That(city.Source.pitch, Is.EqualTo(1f));
                Assert.That(city.Source.timeSamples, Is.EqualTo(pausedSample).Within(8),
                    "Sobriety and resume must preserve the paused clip position.");
                yield return null;
                Assert.That(smoking.Source.pitch, Is.EqualTo(1f));
                Assert.That(radio.Source.pitch, Is.InRange(0.997f, 1.003f));
                Assert.That(combat.Source.pitch, Is.Zero);

                Assert.That(city.RequestSceneExitFade(0.4f), Is.True);
                Assert.That(city.IsDetachedForSceneExit, Is.True);
                SetMusicIntoxication(100);
                yield return null;
                Assert.That(city.Source.pitch, Is.EqualTo(0.25f).Within(0.0001f),
                    "The retained exit tail still observes the current shared perception.");
                Assert.That(city.ActiveClip, Is.SameAs(cityClip));
                city.AdvanceFade(0.4f);
                Assert.That(city.IsSceneExitFadeComplete, Is.True);
                GameTimeScaleRuntime.ResetSession();
                Assert.That(GameTimeScaleRuntime.SmoothedAudioLevel, Is.Zero);
                Assert.That(GameTimeScaleRuntime.AudioPerception.Intensity, Is.Zero);
                Assert.That(GameTimeScaleRuntime.PerceptionPlaybackRate, Is.EqualTo(1f));
            }
            finally
            {
                NpcSpeechVoice.Release(speechLease);
                if (!hadVoice && NpcSpeechVoice.Instance != null)
                    Object.DestroyImmediate(NpcSpeechVoice.Instance.gameObject);
                if (musicObject != null) Object.DestroyImmediate(musicObject);
                if (probe != null) Object.DestroyImmediate(probe);
                MusicMix.ClearFadeOuts();
                GameSessionState.UpdateDrinkingProgress(previousLevel, previousDrink, previousDrinks);
                if (previousTempo != null)
                {
                    GameTimeScaleRuntime.SetIntoxicationLevel(previousAudio);
                    GameTimeScaleRuntime.SetIntoxicationLevel(previousPresentation, synchronizeAudio: false);
                }
                else
                {
                    GameTimeScaleRuntime tempo = Object.FindAnyObjectByType<GameTimeScaleRuntime>();
                    if (tempo != null) Object.DestroyImmediate(tempo.gameObject);
                }
            }
        }

        private static void SetMusicIntoxication(int level)
        {
            GameSessionState.UpdateDrinkingProgress(level,
                GameSessionState.LastAlcoholicDrink, GameSessionState.DrinksConsumed);
            GameTimeScaleRuntime.SetIntoxicationLevel(level);
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
