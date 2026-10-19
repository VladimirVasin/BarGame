using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatWarehouseAudioSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.AudioMixerAssetSetup, BarPromenade.Editor", true)
                .GetMethod("Run", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Real warehouse acoustic ownership, bounded impacts, post-fan recordings and silent PCM proof.")]
        [PrebuildSetup(typeof(CombatWarehouseAudioSetup))]
        public IEnumerator CombatWarehouseAudio()
        {
            bool previousPause = AudioListener.pause;
            GameAudioProfile previousProfile = GameAudioMixer.CurrentProfile;
            CombatTestRoot root = null;
            RetroAudioService service = null;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
                Assert.That(AudioListener.volume, Is.Zero,
                    "AutomaticTestAudioMute must keep the user's speakers silent.");
                AudioListener.pause = false;
                GameSessionState.BeginNewGame();
                Assert.That(GameAudioMixer.ApplyProfile(GameAudioProfile.Home), Is.True);
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized && root.WarehousePlan != null, Is.True);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                Assert.That(GameAudioMixer.CurrentProfile, Is.EqualTo(GameAudioProfile.CombatWarehouse));
                Assert.That(GameAudioMixer.CurrentSnapshot,
                    Is.SameAs(GameAudioMixer.GetSnapshot(GameAudioProfile.CombatWarehouse)));
                Assert.That(GameAudioMixer.HasCompleteConfiguration, Is.True);
                Assert.That(GameAudioMixer.Mixer.FindMatchingGroups("Master/Perception/Environment Reverb Return"),
                    Has.Length.EqualTo(1));
                Assert.That(GameAudioMixer.Mixer.FindMatchingGroups("Master/Perception/Echo Return"),
                    Has.Length.EqualTo(1));

                service = RetroAudioService.EnsureInstalled();
                Assert.That(service.GeneratedWarehouseClipCount, Is.EqualTo(11));
                Assert.That(service.SourceCount, Is.EqualTo(RetroAudioService.TotalPoolSize));
                AudioClip retainedStep = service.GetWarehouseClip(RetroSfxId.FootstepConcrete);
                AudioClip retainedShot = service.GetWarehouseClip(RetroSfxId.ShotgunFire);
                service.WarmWarehouseClips();
                Assert.That(service.GeneratedWarehouseClipCount, Is.EqualTo(11));
                Assert.That(service.GetWarehouseClip(RetroSfxId.FootstepConcrete), Is.SameAs(retainedStep));
                Assert.That(service.GetClip(RetroSfxId.FootstepConcrete), Is.Not.SameAs(retainedStep));
                Assert.That(service.GetWarehouseClip(RetroSfxId.UiMove), Is.Null);

                CombatWarehouseMusicPlayer music = root.Music;
                Assert.That(music, Is.Not.Null);
                for (int frame = 0; frame < 120 && (music.ActiveClip == null ||
                    music.ActiveClip.loadState != AudioDataLoadState.Loaded || !music.Source.isPlaying); frame++)
                    yield return null;
                Assert.That(music.ActiveClip, Is.Not.Null, "The supplied warehouse_theme must load.");
                Assert.That(music.ActiveClip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
                Assert.That(music.Source.isPlaying, Is.True);
                music.AdvanceFade(MusicMix.FadeInSeconds);
                Assert.That(music.PlaybackState, Is.EqualTo(SceneMusicPlaybackState.Playing));
                Assert.That(music.Source.pitch, Is.EqualTo(.8f).Within(.00001f));
                Assert.That(music.Source.loop, Is.True);
                Assert.That(music.Source.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.MusicGroup));
                Assert.That(music.GetComponents<AudioEchoFilter>(), Is.Empty,
                    "The native mixer return owns echo, rather than a second source filter.");
                Assert.That(music.GetComponents<AudioReverbFilter>(), Is.Empty);
                Assert.That(music.GetComponents<AudioLowPassFilter>(), Has.Length.EqualTo(1));
                Assert.That(music.ToneFilter.cutoffFrequency,
                    Is.EqualTo(MusicMix.ToneCutoffFrequency).Within(.001f));

                var sounds = new[] { RetroSfxId.FootstepConcrete, RetroSfxId.FootstepMetal,
                    RetroSfxId.PistolFire, RetroSfxId.ShotgunFire };
                foreach (RetroSfxId sound in sounds)
                {
                    service.StopAll();
                    Vector3 position = root.Hero.transform.position + new Vector3(1f, .3f, .5f);
                    Assert.That(service.TryPlay(sound, position), Is.True);
                    AudioClip expected = service.GetWarehouseClip(sound, service.GetLastVariant(sound));
                    AudioSource voice = FindWarehouseAudioVoice(service, expected);
                    Assert.That(voice, Is.Not.Null, sound + " must use the warehouse bank through the real pool.");
                    Assert.That(voice.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.SfxWorldGroup));
                    Assert.That(voice.spatialBlend, Is.EqualTo(1f));
                    Assert.That(voice.ignoreListenerPause, Is.False);
                    Assert.That(Vector3.Distance(voice.transform.position, position), Is.LessThan(.00001f));
                    RetroSfxDefinition definition = RetroSfxLibrary.GetDefinition(sound);
                    Assert.That(voice.volume, Is.EqualTo(definition.Volume).Within(.00001f));
                    float pitchScale = RetroSfxLibrary.GetWarehousePitchScale(sound);
                    Assert.That(voice.pitch, Is.InRange(pitchScale * (1f - definition.PitchVariation) - .00001f,
                        pitchScale * (1f + definition.PitchVariation) + .00001f));
                    Assert.That(voice.GetComponents<AudioEchoFilter>(), Is.Empty);
                    Assert.That(voice.GetComponents<AudioReverbFilter>(), Is.Empty);
                    yield return null;
                }

                service.StopAll();
                Assert.That(service.TryPlay(RetroSfxId.UiMove, Vector3.zero), Is.True);
                AudioSource uiVoice = FindWarehouseAudioVoice(service, service.GetClip(RetroSfxId.UiMove));
                Assert.That(uiVoice, Is.Not.Null);
                Assert.That(uiVoice.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.UiGroup));
                Assert.That(uiVoice.ignoreListenerPause, Is.True);
                Assert.That(uiVoice.pitch, Is.InRange(.97f, 1.03f));
                WriteWarehouseDryAudioProof(service, sounds);

                yield return new WaitForSecondsRealtime(.05f);
                Assert.That(root.PauseMenu.Open(), Is.True);
                Assert.That(AudioListener.pause, Is.True);
                for (int frame = 0; frame < 2; frame++) yield return null;
                int pausedPlayhead = music.Source.timeSamples;
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(music.Source.timeSamples, Is.EqualTo(pausedPlayhead));
                Assert.That(music.Source.pitch, Is.EqualTo(.8f).Within(.00001f));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                // Resume releases its owned audio pause on the following Update.
                for (int frame = 0; frame < 3 && root.PauseMenu.IsOpen; frame++) yield return null;
                Assert.That(root.PauseMenu.IsOpen, Is.False);
                Assert.That(AudioListener.pause, Is.False);
                yield return new WaitForSecondsRealtime(.05f);
                int resumedPlayhead = music.Source.timeSamples;
                Assert.That(resumedPlayhead, Is.GreaterThan(pausedPlayhead));
                AudioSource retainedMusicSource = music.Source;
                AudioClip retainedMusicClip = music.ActiveClip;
                root.ResetRound();
                Assert.That(root.NewWarehouseLayout(1), Is.True);
                Assert.That(root.Music, Is.SameAs(music));
                Assert.That(music.Source, Is.SameAs(retainedMusicSource));
                Assert.That(music.ActiveClip, Is.SameAs(retainedMusicClip));
                Assert.That(music.Source.timeSamples, Is.GreaterThanOrEqualTo(resumedPlayhead));
                Assert.That(music.Source.pitch, Is.EqualTo(.8f).Within(.00001f));
                Assert.That(service.GetWarehouseClip(RetroSfxId.FootstepConcrete), Is.SameAs(retainedStep));
                Assert.That(service.GetWarehouseClip(RetroSfxId.ShotgunFire), Is.SameAs(retainedShot));
                Assert.That(service.GeneratedWarehouseClipCount, Is.EqualTo(11));

                root.enabled = false;
                Assert.That(GameAudioMixer.CurrentProfile, Is.EqualTo(GameAudioProfile.Home));
                Assert.That(music.IsPlaybackSuppressed, Is.True);
                root.enabled = true;
                Assert.That(GameAudioMixer.CurrentProfile, Is.EqualTo(GameAudioProfile.CombatWarehouse));
                yield return null;
                Assert.That(music.IsPlaybackSuppressed, Is.False);
                Assert.That(music.Source.pitch, Is.EqualTo(.8f).Within(.00001f));
                Assert.That(music.Source.isPlaying, Is.True);

                yield return AssertWarehousePostFanRecordings(root);

                // An incoming area's adoption wins against a late old-root disable.
                Assert.That(GameAudioMixer.ApplyProfile(GameAudioProfile.City), Is.True);
                service.StopAll();
                Assert.That(service.TryPlay(RetroSfxId.FootstepConcrete, Vector3.zero), Is.True);
                AudioSource ordinary = FindWarehouseAudioVoice(service,
                    service.GetClip(RetroSfxId.FootstepConcrete, service.GetLastVariant(RetroSfxId.FootstepConcrete)));
                Assert.That(ordinary, Is.Not.Null, "The warehouse bank must not leak into other areas.");
                Assert.That(ordinary.pitch, Is.InRange(.93f, 1.07f));
                root.enabled = false;
                Assert.That(GameAudioMixer.CurrentProfile, Is.EqualTo(GameAudioProfile.City));
                Scene warehouseScene = root.gameObject.scene;
                Scene empty = SceneManager.CreateScene("Test warehouse audio cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(warehouseScene);
                root = null;
                Assert.That(Object.FindAnyObjectByType<CombatWarehouseMusicPlayer>(), Is.Null);
                Assert.That(Object.FindAnyObjectByType<CombatWarehouseAmbiencePlayer>(), Is.Null);
                Assert.That(Object.FindAnyObjectByType<WarehouseArenaAmbience>(), Is.Null);
                Assert.That(GameAudioMixer.CurrentProfile, Is.EqualTo(GameAudioProfile.City));
                Assert.That(AudioListener.volume, Is.Zero);
            }
            finally
            {
                if (root != null)
                {
                    root.AutomaticSimulation = false;
                    root.PauseMenu?.Cancel();
                    Object.DestroyImmediate(root.gameObject);
                }
                service?.StopAll();
                GameSessionState.BeginNewGame();
                AudioListener.pause = previousPause;
                if (previousProfile > GameAudioProfile.None && previousProfile < GameAudioProfile.Count)
                    GameAudioMixer.ApplyProfile(previousProfile);
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static IEnumerator AssertWarehousePostFanRecordings(CombatTestRoot root)
        {
            CombatWarehouseAmbiencePlayer ambience = root.Ambience;
            Assert.That(ambience, Is.Not.Null);
            GameInput.HandleApplicationFocus(true);
            for (int frame = 0; frame < 120 && (ambience.ClicksSource.clip == null || ambience.WhispersSource.clip == null); frame++)
            {
                ambience.Advance(0f);
                yield return null;
            }
            Assert.That(ambience.ClicksSource.clip, Is.Not.Null, "The supplied clicks recording must import and resolve.");
            Assert.That(ambience.WhispersSource.clip, Is.Not.Null, "The supplied whispers recording must import and resolve.");
            AudioClip suppliedClicks = ambience.ClicksSource.clip;
            AudioClip suppliedWhispers = ambience.WhispersSource.clip;
            foreach (AudioClip supplied in new[] { ambience.ClicksSource.clip, ambience.WhispersSource.clip })
            {
                Assert.That(supplied.channels, Is.EqualTo(1));
                Assert.That(supplied.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
                Assert.That(supplied.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            }
            ambience.AutomaticPlayback = false;
            AudioClip clicks = AudioClip.Create("Test warehouse insect clicks", 80000, 1, 8000, false);
            AudioClip whispers = AudioClip.Create("Test warehouse indistinct whispers", 72000, 1, 8000, false);
            AudioClip brief = AudioClip.Create("Test warehouse brief recording", 640, 1, 8000, false);
            try
            {
                ambience.SetClips(clicks, whispers);
                Assert.That(clicks.length, Is.EqualTo(10f).Within(.001f));
                Assert.That(whispers.length, Is.EqualTo(9f).Within(.001f));
                AudioSource clicksSource = ambience.ClicksSource;
                AudioSource whispersSource = ambience.WhispersSource;
                Assert.That(ambience.GetComponentsInChildren<AudioSource>(true), Has.Length.EqualTo(2));
                foreach (AudioSource source in new[] { clicksSource, whispersSource })
                {
                    Assert.That(source.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.SfxWorldGroup));
                    Assert.That(source.spatialBlend, Is.EqualTo(1f), "The recordings must sound from a world position.");
                    Assert.That(source.spread, Is.Zero);
                    Assert.That(source.dopplerLevel, Is.Zero);
                    Assert.That(source.minDistance, Is.EqualTo(2f));
                    Assert.That(source.maxDistance, Is.EqualTo(16f));
                    Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
                    Assert.That(source.pitch, Is.EqualTo(.8f));
                    Assert.That(source.loop || source.playOnAwake || source.ignoreListenerPause, Is.False);
                    Assert.That(source.GetComponents<AudioEchoFilter>(), Is.Empty);
                    Assert.That(source.GetComponents<AudioReverbFilter>(), Is.Empty);
                    Assert.That(source.GetComponents<AudioLowPassFilter>(), Has.Length.EqualTo(1));
                    Assert.That(source.GetComponent<AudioLowPassFilter>().cutoffFrequency, Is.EqualTo(2800f));
                    Assert.That(source.GetComponent<AudioLowPassFilter>().lowpassResonanceQ, Is.EqualTo(1f));
                }
                Assert.That(ambience.FanStopped, Is.False);
                ambience.Advance(100000f);
                Assert.That(ambience.ClicksPlayCount + ambience.WhispersPlayCount, Is.Zero);
                var items = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan,
                    InventoryItemId.RottenFishHeadCan };
                for (int floor = 0; floor < items.Length; floor++)
                    Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[floor].SourceId,
                        items[floor]), Is.True);
                WarehouseArenaMechanisms machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
                Assert.That(machines.FanShutdownStarted, Is.True);
                // An earlier loss of practical power must not hide the motor's final stop edge.
                Assert.That(GameSessionState.WarehouseFishLamp.TryBegin(true, true), Is.True);
                Assert.That(GameSessionState.WarehouseFishLamp.Advance(WarehouseFishLampState.HeatingSeconds), Is.True);
                machines.Advance(WarehouseArenaMechanisms.FanShutdownDuration - .1f);
                ambience.Advance(100000f);
                Assert.That(ambience.FanStopped, Is.False, "The coast-down still belongs to the fan.");
                Assert.That(ambience.ClicksPlayCount + ambience.WhispersPlayCount, Is.Zero);
                machines.Advance(.2f);
                Assert.That(machines.FanShutdownProgress, Is.EqualTo(1f));
                Assert.That(ambience.FanStopped, Is.True, "The production fan owner must open the recording gate.");
                AssertWarehouseRecordingGaps(ambience);
                float clicksGap = ambience.ClicksDelayRemaining;
                float whispersGap = ambience.WhispersDelayRemaining;
                Assert.That(GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning, Is.False);
                Assert.That(GameInput.MovementFocused, Is.True);
                ambience.Advance(1f);
                Assert.That(ambience.ClicksDelayRemaining, Is.EqualTo(clicksGap - 1f).Within(.0001f));
                Assert.That(ambience.WhispersDelayRemaining, Is.EqualTo(whispersGap - 1f).Within(.0001f));
                clicksGap = ambience.ClicksDelayRemaining;
                whispersGap = ambience.WhispersDelayRemaining;
                Assert.That(root.PauseMenu.Open(), Is.True);
                ambience.Advance(100000f);
                Assert.That(ambience.ClicksDelayRemaining, Is.EqualTo(clicksGap));
                Assert.That(ambience.WhispersDelayRemaining, Is.EqualTo(whispersGap));
                Assert.That(ambience.ClicksPlayCount + ambience.WhispersPlayCount, Is.Zero);
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                for (int frame = 0; frame < 3 && root.PauseMenu.IsOpen; frame++) yield return null;
                Assert.That(root.PauseMenu.IsOpen, Is.False);
                ambience.Advance(0f);
                Assert.That(ambience.ClicksDelayRemaining, Is.EqualTo(clicksGap));
                Assert.That(ambience.WhispersDelayRemaining, Is.EqualTo(whispersGap));

                // Both channels can overlap; a large advance never truncates a file or queues a burst.
                ambience.Advance(100000f);
                Assert.That(clicksSource.isPlaying && whispersSource.isPlaying, Is.True);
                Assert.That(ambience.ClicksPlayCount, Is.EqualTo(1));
                Assert.That(ambience.WhispersPlayCount, Is.EqualTo(1));
                AssertWarehouseRecordingPosition(root, clicksSource);
                AssertWarehouseRecordingPosition(root, whispersSource);
                Vector3 firstClicksPosition = clicksSource.transform.position;
                Vector3 firstWhispersPosition = whispersSource.transform.position;
                Vector3 originalHeroPosition = root.Hero.transform.position;
                root.Player.Motor.Teleport(originalHeroPosition + Vector3.right * .25f);
                ambience.Advance(100000f);
                Assert.That(ambience.ClicksPlayCount, Is.EqualTo(1));
                Assert.That(ambience.WhispersPlayCount, Is.EqualTo(1));
                Assert.That(clicksSource.transform.position, Is.EqualTo(firstClicksPosition),
                    "A playing sound stays in the world when the hero moves.");
                Assert.That(whispersSource.transform.position, Is.EqualTo(firstWhispersPosition));
                root.Player.Motor.Teleport(originalHeroPosition);
                Assert.That(clicksSource.clip, Is.SameAs(clicks));
                Assert.That(whispersSource.clip, Is.SameAs(whispers));
                yield return new WaitForSecondsRealtime(.05f);
                Assert.That(root.PauseMenu.Open(), Is.True);
                ambience.Advance(0f);
                for (int frame = 0; frame < 2; frame++) yield return null;
                int pausedClicks = clicksSource.timeSamples;
                int pausedWhispers = whispersSource.timeSamples;
                for (int frame = 0; frame < 3; frame++) yield return null;
                ambience.Advance(100000f);
                Assert.That(clicksSource.timeSamples, Is.EqualTo(pausedClicks));
                Assert.That(whispersSource.timeSamples, Is.EqualTo(pausedWhispers));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                for (int frame = 0; frame < 3 && root.PauseMenu.IsOpen; frame++) yield return null;
                ambience.Advance(0f);
                yield return new WaitForSecondsRealtime(.05f);
                Assert.That(clicksSource.timeSamples, Is.GreaterThan(pausedClicks));
                Assert.That(whispersSource.timeSamples, Is.GreaterThan(pausedWhispers));

                root.enabled = false;
                for (int frame = 0; frame < 2; frame++) yield return null;
                pausedClicks = clicksSource.timeSamples;
                pausedWhispers = whispersSource.timeSamples;
                for (int frame = 0; frame < 3; frame++) yield return null;
                ambience.Advance(100000f);
                Assert.That(clicksSource.timeSamples, Is.EqualTo(pausedClicks));
                Assert.That(whispersSource.timeSamples, Is.EqualTo(pausedWhispers));
                Assert.That(ambience.ClicksPlayCount + ambience.WhispersPlayCount, Is.EqualTo(2));
                root.enabled = true;
                ambience.Advance(0f);
                Assert.That(clicksSource.isPlaying && whispersSource.isPlaying, Is.True);

                root.ResetRound();
                Assert.That(root.NewWarehouseLayout(2), Is.True);
                Assert.That(root.Ambience, Is.SameAs(ambience));
                Assert.That(ambience.ClicksSource, Is.SameAs(clicksSource));
                Assert.That(ambience.WhispersSource, Is.SameAs(whispersSource));
                Assert.That(clicksSource.clip, Is.SameAs(clicks));
                Assert.That(whispersSource.clip, Is.SameAs(whispers));
                Assert.That(ambience.FanStopped, Is.True);
                Assert.That(ambience.ClicksPlayCount + ambience.WhispersPlayCount, Is.EqualTo(2));

                // Represent each native EOF, then prove the next pause starts after playback.
                clicksSource.Stop();
                whispersSource.Stop();
                ambience.Advance(100000f);
                Assert.That(ambience.ClicksPlayCount + ambience.WhispersPlayCount, Is.EqualTo(2));
                AssertWarehouseRecordingGaps(ambience);
                clicksGap = ambience.ClicksDelayRemaining;
                whispersGap = ambience.WhispersDelayRemaining;
                ambience.Advance(1f);
                Assert.That(ambience.ClicksDelayRemaining, Is.EqualTo(clicksGap - 1f).Within(.0001f));
                Assert.That(ambience.WhispersDelayRemaining, Is.EqualTo(whispersGap - 1f).Within(.0001f));
                ambience.Advance(100000f);
                Assert.That(ambience.ClicksPlayCount, Is.EqualTo(2));
                Assert.That(ambience.WhispersPlayCount, Is.EqualTo(2));
                AssertWarehouseRecordingPosition(root, clicksSource);
                AssertWarehouseRecordingPosition(root, whispersSource);
                Assert.That(Vector3.Distance(clicksSource.transform.position, firstClicksPosition), Is.GreaterThan(.1f),
                    "The next click recording must select a fresh position around the current hero.");
                Assert.That(Vector3.Distance(whispersSource.transform.position, firstWhispersPosition), Is.GreaterThan(.1f));

                // A brief native clip proves scheduling follows actual completion, not the user durations.
                ambience.SetClips(brief, null);
                int clicksBefore = ambience.ClicksPlayCount;
                int whispersBefore = ambience.WhispersPlayCount;
                ambience.Advance(100000f);
                Assert.That(ambience.ClicksPlayCount, Is.EqualTo(clicksBefore + 1));
                Assert.That(ambience.WhispersPlayCount, Is.EqualTo(whispersBefore));
                Assert.That(whispersSource.clip, Is.Null);
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(clicksSource.isPlaying, Is.False);
                ambience.Advance(100000f);
                Assert.That(ambience.ClicksPlayCount, Is.EqualTo(clicksBefore + 1));
                Assert.That(ambience.ClicksDelayRemaining, Is.InRange(12f, 25f));
                ambience.Advance(ambience.ClicksDelayRemaining + .01f);
                Assert.That(ambience.ClicksPlayCount, Is.EqualTo(clicksBefore + 2));
                ambience.SetClips(null, whispers);
                clicksBefore = ambience.ClicksPlayCount;
                ambience.Advance(100000f);
                Assert.That(clicksSource.isPlaying, Is.False);
                Assert.That(ambience.ClicksPlayCount, Is.EqualTo(clicksBefore));
                Assert.That(ambience.WhispersPlayCount, Is.EqualTo(whispersBefore + 1));

                yield return AssertWarehouseRecordingAudibility(root, suppliedClicks, suppliedWhispers);
                clicksBefore = ambience.ClicksPlayCount;
                whispersBefore = ambience.WhispersPlayCount;
                GameSessionState.ResetInventoryState();
                Assert.That(ambience.FanStopped, Is.False);
                Assert.That(clicksSource.isPlaying || whispersSource.isPlaying, Is.False);
                ambience.Advance(100000f);
                Assert.That(ambience.ClicksPlayCount, Is.EqualTo(clicksBefore));
                Assert.That(ambience.WhispersPlayCount, Is.EqualTo(whispersBefore));
            }
            finally
            {
                if (ambience != null) ambience.SetClips(null, null);
                Object.DestroyImmediate(clicks);
                Object.DestroyImmediate(whispers);
                Object.DestroyImmediate(brief);
            }
        }

        private static IEnumerator AssertWarehouseRecordingAudibility(CombatTestRoot root,
            AudioClip suppliedClicks, AudioClip suppliedWhispers)
        {
            CombatWarehouseAmbiencePlayer ambience = root.Ambience;
            Assert.That(ambience.FanStopped && root.Music.TapeStopped, Is.True);
            Assert.That(AudioListener.volume, Is.Zero, "Ordinary test playback must remain muted.");
            Assert.That(AudioSettings.GetConfiguration().speakerMode, Is.EqualTo(AudioSpeakerMode.Stereo));
            Assert.That(suppliedClicks.length, Is.EqualTo(10f).Within(.1f));
            Assert.That(suppliedWhispers.length, Is.InRange(9f, 10f));
            float listenerVolume = AudioListener.volume;
            bool listenerPause = AudioListener.pause;
            float captureDelta = Time.captureDeltaTime;
            bool automaticPlayback = ambience.AutomaticPlayback;
            bool capturing = false;
            var muted = new Dictionary<AudioSource, bool>();
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.GetComponent<AudioListener>(), Is.Not.Null);
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool followEnabled = follow != null && follow.enabled;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            GameObject referenceObject = null;
            try
            {
                if (follow != null) follow.enabled = false;
                camera.transform.SetPositionAndRotation(root.Hero.transform.position + Vector3.up * 1.35f,
                    Quaternion.identity);
                Time.captureDeltaTime = 1f / 30f;
                AudioListener.pause = false;
                ambience.AutomaticPlayback = false;
                ambience.SetClips(null, null);
                referenceObject = new GameObject("Test warehouse music reference");
                referenceObject.transform.SetParent(root.transform, false);
                AudioSource reference = referenceObject.AddComponent<AudioSource>();
                reference.playOnAwake = false;
                reference.clip = root.Music.ActiveClip;
                Assert.That(reference.clip, Is.Not.Null);
                reference.volume = MusicMix.DefaultOutputVolume;
                reference.pitch = CombatWarehouseMusicPlayer.PlaybackSpeed;
                reference.loop = true;
                reference.spatialBlend = 0f;
                reference.reverbZoneMix = 0f;
                GameAudioMixer.Route(reference, GameAudioGroup.Music);
                referenceObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = MusicMix.ToneCutoffFrequency;

                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CombatTest");
                Directory.CreateDirectory(folder);
                var report = new List<string>();
                int rate = AudioSettings.outputSampleRate;
                // Unity's first offline session can return no frames. The listener
                // may be unmuted only after Start and is muted before every Stop.
                Assert.That(AudioRenderer.Start(), Is.True);
                capturing = true;
                AudioListener.volume = 1f;
                yield return PumpProductionAudio(rate / 5, null, muted, Array.Empty<AudioSource>(), null, 30, false);
                AudioListener.volume = 0f;
                AudioRenderer.Stop();
                capturing = false;
                yield return null;
                Assert.That(AudioRenderer.Start(), Is.True);
                capturing = true;
                AudioListener.volume = 1f;
                yield return PumpProductionAudio(rate * 2, null, muted, Array.Empty<AudioSource>(), null, 180);

                reference.timeSamples = Mathf.Min(reference.clip.samples - 1, reference.clip.frequency * 20);
                reference.Play();
                var referencePcm = new List<float>();
                yield return PumpProductionAudio(rate * 2 * 10, referencePcm, muted, new[] { reference }, null, 480);
                reference.Stop();
                float[] referenceSamples = referencePcm.ToArray();
                double musicRms = ProductionAudioRms(referenceSamples);
                Assert.That(musicRms, Is.GreaterThan(.001d), "The real warehouse theme is the audible reference.");
                WritePcmWave(Path.Combine(folder, "Test warehouse music reference.wav"), referenceSamples, rate, 2, 1f);
                report.Add($"Warehouse theme: real mixer RMS={musicRms:F6}, source={reference.volume:F3}, pitch={reference.pitch:F2}");

                for (int channel = 0; channel < 2; channel++)
                {
                    ambience.AutomaticPlayback = false;
                    ambience.SetClips(null, null);
                    // Clear the prior native room returns before comparing another source.
                    yield return PumpProductionAudio(rate * 2 * 4, null, muted, Array.Empty<AudioSource>(), null, 180);
                    AudioClip recording = channel == 0 ? suppliedClicks : suppliedWhispers;
                    AudioSource source = channel == 0 ? ambience.ClicksSource : ambience.WhispersSource;
                    ambience.SetClips(channel == 0 ? recording : null, channel == 1 ? recording : null);
                    int playsBefore = channel == 0 ? ambience.ClicksPlayCount : ambience.WhispersPlayCount;
                    float gap = channel == 0 ? ambience.ClicksDelayRemaining : ambience.WhispersDelayRemaining;
                    // Approach the deadline manually, then let ordinary Update start
                    // the actual recording through the production scheduling gate.
                    ambience.Advance(gap - .00001f);
                    Assert.That(source.isPlaying, Is.False);
                    ambience.AutomaticPlayback = true;
                    var pcm = new List<float>();
                    float playbackSeconds = recording.length / source.pitch;
                    Assert.That(playbackSeconds, Is.EqualTo(channel == 0 ? 12.5f : 12.24f).Within(.1f));
                    float beforeEndSeconds = playbackSeconds - .2f;
                    yield return PumpProductionAudio(Mathf.CeilToInt(rate * 2f * beforeEndSeconds), pcm, muted,
                        new[] { source }, null, Mathf.CeilToInt(beforeEndSeconds * 30f) + 180);
                    Assert.That(source.isPlaying, Is.True, "The slowed recording must still play just before its full native duration.");
                    yield return PumpProductionAudio(Mathf.CeilToInt(rate * 2f * .95f), pcm, muted,
                        new[] { source }, null, 180);
                    ambience.AutomaticPlayback = false;
                    int playsAfter = channel == 0 ? ambience.ClicksPlayCount : ambience.WhispersPlayCount;
                    Assert.That(playsAfter, Is.EqualTo(playsBefore + 1), "Normal Update must start exactly one supplied recording.");
                    Assert.That(source.isPlaying, Is.False, "The full supplied recording must complete in the captured output.");
                    AssertWarehouseRecordingPosition(root, source);
                    float distance = Vector3.Distance(source.transform.position, camera.transform.position);
                    Assert.That(distance, Is.InRange(3f, 7.1f));
                    float[] samples = pcm.ToArray();
                    double rms = ProductionAudioRms(samples);
                    double ratio = rms / musicRms;
                    string name = channel == 0 ? "Test warehouse insect clicks" : "Test warehouse indistinct whispers";
                    report.Add($"{name}: real mixer RMS={rms:F6}, theme ratio={ratio:F4} " +
                        $"({20d * Math.Log10(Math.Max(ratio, 1e-12d)):F2}dB), distance={distance:F2}m, " +
                        $"source={source.volume:F3}, pitch={source.pitch:F2}, duration={playbackSeconds:F3}s");
                    TestContext.Out.WriteLine(report[report.Count - 1]);
                    WritePcmWave(Path.Combine(folder, name + ".wav"), samples, rate, 2, 1f);
                    Assert.That(rms, Is.GreaterThan(.0002d), name + " must reach quiet but audible native mixer output.");
                    Assert.That(ratio, Is.InRange(.02d, .12d),
                        name + " must sit quietly behind the actual warehouse theme, including distance and room returns.");
                }
                yield return CaptureWarehouseRecordingRoomProof(ambience, reference, rate, folder, muted, report);
                File.WriteAllLines(Path.Combine(folder, "Test warehouse recording balance.txt"), report);
                Assert.That(root.Music.TapeStopped && !root.Music.Source.isPlaying, Is.True,
                    "The reference must not restart the stopped production cassette.");
            }
            finally
            {
                AudioListener.volume = 0f;
                if (capturing) AudioRenderer.Stop();
                ambience.AutomaticPlayback = false;
                ambience.SetClips(null, null);
                foreach (var pair in muted) if (pair.Key != null) pair.Key.mute = pair.Value;
                if (referenceObject != null) Object.DestroyImmediate(referenceObject);
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                if (follow != null) follow.enabled = followEnabled;
                ambience.AutomaticPlayback = automaticPlayback;
                Time.captureDeltaTime = captureDelta;
                AudioListener.pause = listenerPause;
                AudioListener.volume = listenerVolume;
            }
        }

        private static IEnumerator CaptureWarehouseRecordingRoomProof(CombatWarehouseAmbiencePlayer ambience,
            AudioSource dryReference, int rate, string folder, Dictionary<AudioSource, bool> muted, List<string> report)
        {
            const int pulseRate = 24000;
            var signal = new float[Mathf.CeilToInt(pulseRate * 1.6f)];
            int audibleFrames = Mathf.RoundToInt(pulseRate * .12f);
            for (int frame = 0; frame < audibleFrames; frame++)
            {
                float window = .5f - .5f * Mathf.Cos(2f * Mathf.PI * frame / (audibleFrames - 1));
                float time = frame / (float)pulseRate;
                signal[frame] = window * .14f * (Mathf.Sin(2f * Mathf.PI * 650f * time) +
                    Mathf.Sin(2f * Mathf.PI * 9000f * time));
            }
            AudioClip pulse = AudioClip.Create("Test warehouse bright pulse and silence", signal.Length, 1, pulseRate, false);
            Assert.That(pulse.SetData(signal, 0), Is.True);
            try
            {
                ambience.AutomaticPlayback = false;
                ambience.SetClips(pulse, null);
                ambience.Advance(ambience.ClicksDelayRemaining + .01f);
                AudioSource wet = ambience.ClicksSource;
                Assert.That(wet.isPlaying, Is.True);
                wet.Stop();
                dryReference.clip = pulse;
                dryReference.loop = false;
                dryReference.pitch = wet.pitch;
                dryReference.volume = wet.volume * Mathf.Pow(10f, GameAudioMixer.SfxWorldGainDb / 20f);
                dryReference.spatialBlend = wet.spatialBlend;
                dryReference.spread = wet.spread;
                dryReference.dopplerLevel = 0f;
                dryReference.minDistance = wet.minDistance;
                dryReference.maxDistance = wet.maxDistance;
                dryReference.rolloffMode = wet.rolloffMode;
                dryReference.transform.position = wet.transform.position;
                dryReference.GetComponent<AudioLowPassFilter>().enabled = false;
                GameAudioMixer.Route(dryReference, GameAudioGroup.Master);
                double dryTail = 0d, dryBrightness = 0d;
                for (int pass = 0; pass < 2; pass++)
                {
                    wet.Stop();
                    dryReference.Stop();
                    yield return PumpProductionAudio(rate * 2 * 4, null, muted, Array.Empty<AudioSource>(), null, 180);
                    AudioSource source = pass == 0 ? dryReference : wet;
                    source.timeSamples = 0;
                    source.Play();
                    var pcm = new List<float>();
                    yield return PumpProductionAudio(Mathf.CeilToInt(rate * 2f * 2.1f), pcm, muted,
                        new[] { source }, null, 240);
                    float[] samples = pcm.ToArray();
                    double tail = ProductionAudioRms(samples, -1, Mathf.RoundToInt(rate * 2f * .45f),
                        Mathf.RoundToInt(rate * 2f * 1.1f));
                    double brightness = WarehouseRecordingBandEnergy(samples, rate, 7200d) /
                        Math.Max(1e-20d, WarehouseRecordingBandEnergy(samples, rate, 520d));
                    string name = pass == 0 ? "Test warehouse dry room pulse" : "Test warehouse muffled room pulse";
                    WritePcmWave(Path.Combine(folder, name + ".wav"), samples, rate, 2, 1f);
                    report.Add($"{name}: zero-input tail RMS={tail:F8}, high/low energy={brightness:F6}");
                    TestContext.Out.WriteLine(report[report.Count - 1]);
                    if (pass == 0) { dryTail = tail; dryBrightness = brightness; }
                    else
                    {
                        Assert.That(tail, Is.GreaterThan(.000002d), "The common warehouse echo/reverb must ring into exact source silence.");
                        Assert.That(tail, Is.GreaterThan(dryTail * 3d + 1e-8d), "Room returns must add a real tail beyond the dry source.");
                        Assert.That(brightness, Is.LessThan(dryBrightness * .35d),
                            "The production source and common warehouse tone must suppress high frequencies relative to the dry reference.");
                    }
                }
            }
            finally
            {
                ambience.SetClips(null, null);
                dryReference.Stop();
                dryReference.clip = null;
                Object.DestroyImmediate(pulse);
            }
        }

        private static double WarehouseRecordingBandEnergy(float[] stereo, int rate, double frequency)
        {
            int frames = Math.Min(stereo.Length / 2, Mathf.RoundToInt(rate * .22f));
            double leftReal = 0d, leftImaginary = 0d, rightReal = 0d, rightImaginary = 0d;
            for (int frame = 0; frame < frames; frame++)
            {
                double window = .5d - .5d * Math.Cos(2d * Math.PI * frame / (frames - 1));
                double angle = 2d * Math.PI * frequency * frame / rate;
                double real = window * Math.Cos(angle), imaginary = window * Math.Sin(angle);
                leftReal += stereo[frame * 2] * real;
                leftImaginary += stereo[frame * 2] * imaginary;
                rightReal += stereo[frame * 2 + 1] * real;
                rightImaginary += stereo[frame * 2 + 1] * imaginary;
            }
            return leftReal * leftReal + leftImaginary * leftImaginary +
                rightReal * rightReal + rightImaginary * rightImaginary;
        }

        private static void AssertWarehouseRecordingGaps(CombatWarehouseAmbiencePlayer ambience)
        {
            Assert.That(ambience.ClicksDelayRemaining, Is.InRange(12f, 25f));
            Assert.That(ambience.WhispersDelayRemaining, Is.InRange(25f, 50f));
        }

        private static void AssertWarehouseRecordingPosition(CombatTestRoot root, AudioSource source)
        {
            Vector3 difference = source.transform.position - root.Hero.transform.position;
            float planarDistance = new Vector2(difference.x, difference.z).magnitude;
            Assert.That(planarDistance, Is.InRange(3f - .001f, 7f + .001f),
                "Each recording must select a nearby world point around the current hero.");
            Assert.That(difference.y, Is.InRange(.95f - .001f, 1.75f + .001f));
            Assert.That(Mathf.Abs(source.transform.position.x), Is.LessThan(WarehouseArenaPlan.HalfWidth));
            Assert.That(Mathf.Abs(source.transform.position.z), Is.LessThan(WarehouseArenaPlan.HalfDepth));
            Assert.That(Physics.CheckSphere(source.transform.position, .15f, ~0, QueryTriggerInteraction.Ignore),
                Is.False, "A new sound point must clear the warehouse's actual colliders.");
        }

        private static AudioSource FindWarehouseAudioVoice(RetroAudioService service, AudioClip clip)
        {
            foreach (AudioSource voice in service.GetComponentsInChildren<AudioSource>(true))
                if (voice.clip == clip) return voice;
            return null;
        }

        private static void WriteWarehouseDryAudioProof(RetroAudioService service, RetroSfxId[] sounds)
        {
            var proof = new List<float>();
            var report = new List<string> { "Dry source PCM only; room echo and reverb belong to the native mixer.",
                "Order: concrete step, metal step, pistol, shotgun; each ordinary sound then its warehouse sound." };
            foreach (RetroSfxId sound in sounds)
            {
                AudioClip ordinaryClip = service.GetClip(sound);
                AudioClip heavyClip = service.GetWarehouseClip(sound);
                var ordinary = new float[ordinaryClip.samples];
                var heavy = new float[heavyClip.samples];
                Assert.That(ordinaryClip.GetData(ordinary, 0), Is.True);
                Assert.That(heavyClip.GetData(heavy, 0), Is.True);
                CollectionAssert.AreEqual(RetroSfxLibrary.GenerateWarehouseSamples(sound), heavy,
                    sound + " must retain deterministic generated PCM in the actual cached clip.");
                float peak = 0f;
                foreach (float sample in heavy)
                {
                    Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                }
                Assert.That(peak, Is.InRange(.05f, .979f));
                double referenceLow = WarehouseLowBandRatio(ordinary);
                double heavyLow = WarehouseLowBandRatio(heavy);
                Assert.That(heavyLow, Is.GreaterThan(referenceLow * 1.25d),
                    sound + " must increase low impact energy relative to high bite.");
                float pitch = RetroSfxLibrary.GetWarehousePitchScale(sound);
                float gain = RetroSfxLibrary.GetDefinition(sound).Volume;
                AppendWarehouseAudioPcm(proof, ordinary, 1f, gain);
                AppendWarehouseAudioPcm(proof, heavy, pitch, gain);
                report.Add($"{sound}: source pitch {pitch:F2}, peak {peak:F5}, " +
                    $"low/high energy reference {referenceLow:F3}, warehouse {heavyLow:F3}");
            }
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CombatTest");
            Directory.CreateDirectory(folder);
            WritePcmWave(Path.Combine(folder, "Test warehouse heavy sounds.wav"), proof.ToArray(),
                RetroSfxLibrary.SampleRate, 1, 1f);
            File.WriteAllLines(Path.Combine(folder, "Test warehouse audio proof.txt"), report);
            foreach (string line in report) TestContext.Out.WriteLine(line);
        }

        private static void AppendWarehouseAudioPcm(List<float> proof, float[] samples, float pitch, float gain)
        {
            int count = Mathf.CeilToInt(samples.Length / pitch);
            for (int index = 0; index < count; index++)
            {
                float at = index * pitch;
                int lower = Mathf.Min(samples.Length - 1, Mathf.FloorToInt(at));
                int upper = Mathf.Min(samples.Length - 1, lower + 1);
                proof.Add(Mathf.Lerp(samples[lower], samples[upper], at - lower) * gain);
            }
            for (int index = 0; index < RetroSfxLibrary.SampleRate / 3; index++) proof.Add(0f);
        }

        private static double WarehouseLowBandRatio(float[] samples)
        {
            // Fixed sparse DFT bands test the requested timbre rather than its
            // implementation coefficients; both signals receive the same window.
            int count = Mathf.Min(samples.Length, 4096);
            double low = 0d, high = 0d;
            for (int bin = 0; bin < 16; bin++)
            {
                low += WarehouseDftEnergy(samples, count, 25d + 175d * bin / 15d);
                high += WarehouseDftEnergy(samples, count, 600d + 3400d * bin / 15d);
            }
            return low / Math.Max(1e-20d, high);
        }

        private static double WarehouseDftEnergy(float[] samples, int count, double frequency)
        {
            double real = 0d, imaginary = 0d;
            double step = 2d * Math.PI * frequency / RetroSfxLibrary.SampleRate;
            for (int index = 0; index < count; index++)
            {
                double window = .5d - .5d * Math.Cos(2d * Math.PI * index / (count - 1));
                double sample = samples[index] * window;
                real += sample * Math.Cos(step * index);
                imaginary += sample * Math.Sin(step * index);
            }
            return real * real + imaginary * imaginary;
        }
    }
}
