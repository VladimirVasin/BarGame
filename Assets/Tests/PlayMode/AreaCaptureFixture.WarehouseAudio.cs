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
        [Explicit("Real warehouse acoustic ownership, bounded impacts, theme pitch and silent PCM proof.")]
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
