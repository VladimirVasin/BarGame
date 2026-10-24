using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class RussianVocalizerLabPlayModeTests
    {
        [UnityTest]
        public IEnumerator MenuLab_PlaysTwelvePresetsComparesGenderedSlotsAndCancelsWithoutLeavingMenu()
        {
            SceneManager.LoadScene(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return null;
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu, Is.Not.Null);
            string captureDirectory = Path.Combine(Application.dataPath, "../Captures/vocalizer");
            bool capture = System.Environment.GetEnvironmentVariable("BAR_VOCALIZER_CAPTURE") == "1";
            var exportedWaves = capture ? new List<string>(12) : null;
            if (capture)
            {
                Directory.CreateDirectory(captureDirectory);
                yield return CaptureOptionalScreen(menu.BackdropCamera, Path.Combine(captureDirectory, "menu.png"));
            }
            Assert.That(menu.SelectOption(StartMenuOption.Vocalizer), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            RussianVocalizerLab lab = menu.VocalizerLab;
            try
            {
                Assert.That(menu.IsVocalizerActive && lab.IsOpen, Is.True);
                Assert.That(menu.MoveSelection(1), Is.False, "Text editing owns menu navigation.");
                Assert.That(menu.ConfirmSelection(), Is.False, "A typed Enter cannot launch another mode.");
                Assert.That(RussianVocalizerSynthesis.VoiceCount, Is.EqualTo(12));
                Assert.That(lab.VoiceFilter, Is.EqualTo(RussianVocalizerLabFilter.All));
                Assert.That(lab.VisibleVoiceCount, Is.EqualTo(12));
                Assert.That(lab.GameWindowMode, Is.False);
                lab.Phrase = "Бензин есть. Повода нет.";
                yield return null;
                if (capture) yield return CaptureOptionalScreen(menu.BackdropCamera, Path.Combine(captureDirectory, "lab.png"));

                var names = new HashSet<string>();
                int maleCount = 0, femaleCount = 0, maleIndex = -1, femaleIndex = -1;
                for (int voice = 0; voice < RussianVocalizerSynthesis.VoiceCount; voice++)
                {
                    string name = RussianVocalizerSynthesis.LabVoiceNameAt(voice);
                    Assert.That(name, Is.Not.Null.And.Not.Empty);
                    Assert.That(names.Add(name), Is.True);
                    Assert.That(RussianVocalizerSynthesis.LabVoiceDescriptionAt(voice), Is.Not.Null.And.Not.Empty);
                    RussianVocalizerGender gender = RussianVocalizerSynthesis.LabVoiceGenderAt(voice);
                    Assert.That(RussianVocalizerSynthesis.LabVoiceAt(voice).Gender, Is.EqualTo(gender));
                    if (gender == RussianVocalizerGender.Male) { maleCount++; if (maleIndex < 0) maleIndex = voice; }
                    else { femaleCount++; if (femaleIndex < 0) femaleIndex = voice; }
                    lab.SelectVoice(voice);
                    lab.Play();
                    yield return WaitForLab(lab);
                    Assert.That(lab.IsPlaying, Is.True, name);
                    Assert.That(lab.PlayingVoice, Is.EqualTo(voice));
                    Assert.That(lab.PlaybackPlan.TextLength, Is.EqualTo(lab.Phrase.Length));
                    Assert.That(lab.PlaybackPlan.Voice.Gender, Is.EqualTo(gender));
                    AudioSource source = lab.GetComponentInChildren<AudioSource>();
                    Assert.That(lab.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(1));
                    Assert.That(source.spatialBlend, Is.Zero);
                    Assert.That(source.volume, Is.EqualTo(RussianVocalizerLab.PreviewVolume));
                    Assert.That(source.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.UiGroup));
                    Assert.That(source.clip.samples, Is.EqualTo(lab.PlaybackPlan.SampleCount));
                    if (capture)
                    {
                        var samples = new float[source.clip.samples];
                        Assert.That(source.clip.GetData(samples, 0), Is.True);
                        string filename = (voice + 1).ToString("00") + "-" +
                            (gender == RussianVocalizerGender.Male ? "М-" : "Ж-") + SafeFileName(name) + ".wav";
                        string wavePath = Path.Combine(captureDirectory, filename);
                        WriteWave(wavePath, samples);
                        exportedWaves.Add(wavePath);
                    }
                    lab.Stop();
                    Assert.That(lab.IsPlaying, Is.False);
                    Assert.That(source.clip, Is.Null);
                }
                Assert.That(maleCount, Is.GreaterThan(0));
                Assert.That(femaleCount, Is.GreaterThan(0));
                Assert.That(maleCount + femaleCount, Is.EqualTo(12));

                lab.SetVoiceFilter(RussianVocalizerLabFilter.Male);
                Assert.That(lab.VisibleVoiceCount, Is.EqualTo(maleCount));
                Assert.That(RussianVocalizerSynthesis.LabVoiceGenderAt(lab.SelectedVoice), Is.EqualTo(RussianVocalizerGender.Male));
                Assert.That(lab.IsVoiceVisible(femaleIndex), Is.False);
                lab.SetVoiceFilter(RussianVocalizerLabFilter.Female);
                Assert.That(lab.VisibleVoiceCount, Is.EqualTo(femaleCount));
                Assert.That(RussianVocalizerSynthesis.LabVoiceGenderAt(lab.SelectedVoice), Is.EqualTo(RussianVocalizerGender.Female));
                Assert.That(lab.IsVoiceVisible(maleIndex), Is.False);
                lab.SetVoiceFilter(RussianVocalizerLabFilter.All);

                // The same preset retains its pitch when a long phrase must fit.
                lab.SelectVoice(maleIndex);
                lab.Phrase = "Последний рейс уже был. Или ещё будет. Маршрут отменили. Я всё равно жду.";
                lab.Play();
                yield return WaitForLab(lab);
                double freeDuration = lab.PlaybackPlan.DurationSeconds;
                Assert.That(freeDuration, Is.GreaterThan(RussianVocalizerLab.GameWindowSeconds));
                lab.SetGameWindowMode(true);
                Assert.That(lab.IsPlaying, Is.False);
                Assert.That(lab.GetComponentInChildren<AudioSource>().clip, Is.Null);
                lab.Play();
                yield return WaitForLab(lab);
                Assert.That(lab.PlaybackPlan.DurationSeconds,
                    Is.LessThanOrEqualTo(RussianVocalizerLab.GameWindowSeconds + 1d / RussianVocalizerSynthesis.SampleRate));
                Assert.That(lab.PlaybackPlan.DurationSeconds, Is.LessThan(freeDuration));
                Assert.That(lab.PlaybackPlan.Voice.FundamentalHz,
                    Is.EqualTo(RussianVocalizerSynthesis.LabVoiceAt(maleIndex).FundamentalHz));
                Assert.That(lab.GetComponentInChildren<AudioSource>().pitch, Is.EqualTo(1f));

                // A and B retain explicit identities even when the other gender is filtered out.
                lab.SetGameWindowMode(false);
                lab.Phrase = "Бензин есть. Повода нет.";
                lab.SetComparisonVoice(0, maleIndex);
                lab.SetComparisonVoice(1, femaleIndex);
                lab.SetVoiceFilter(RussianVocalizerLabFilter.Male);
                for (int slot = 0; slot < 2; slot++)
                {
                    lab.PlayComparison(slot);
                    yield return WaitForLab(lab);
                    Assert.That(lab.PlayingVoice, Is.EqualTo(slot == 0 ? maleIndex : femaleIndex));
                    Assert.That(lab.PlaybackPlan.TextLength, Is.EqualTo(lab.Phrase.Length));
                    Assert.That(lab.GetComponentInChildren<AudioSource>().volume, Is.EqualTo(RussianVocalizerLab.PreviewVolume));
                    lab.Stop();
                }
                Assert.That(lab.ComparisonVoiceA, Is.EqualTo(maleIndex));
                Assert.That(lab.ComparisonVoiceB, Is.EqualTo(femaleIndex));

                // A replacement, mode change and gender filter all cancel prior work.
                lab.Phrase = new string('я', RussianSpeechPhonetics.MaximumTextLength);
                lab.Play();
                lab.SetGameWindowMode(true);
                lab.SetVoiceFilter(RussianVocalizerLabFilter.Female);
                lab.Phrase = "Хорошо. Сейчас подам.";
                lab.SelectVoice(femaleIndex);
                lab.Play();
                yield return WaitForLab(lab);
                Assert.That(lab.PlayingVoice, Is.EqualTo(femaleIndex));
                Assert.That(lab.PlaybackPlan.TextLength, Is.EqualTo(lab.Phrase.Length));
                lab.Play();
                Assert.That(menu.ReturnToMainMenu(), Is.True);
                Assert.That(lab.IsOpen || lab.IsPlaying, Is.False);
                Assert.That(menu.SelectedOption, Is.EqualTo(StartMenuOption.Vocalizer));
                yield return WaitForLab(lab);
                Assert.That(lab.GetComponentInChildren<AudioSource>().clip, Is.Null);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.MainMenu));
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(lab.IsOpen, Is.True);
                Assert.That(lab.IsPlaying, Is.False);
                lab.Play();
                menu.gameObject.SetActive(false);
                Assert.That(lab.IsOpen || lab.IsPlaying, Is.False);
                menu.gameObject.SetActive(true);
                yield return WaitForLab(lab);
                Assert.That(lab.IsPlaying, Is.False);
                Assert.That(lab.GetComponentInChildren<AudioSource>().clip, Is.Null);
                if (capture)
                {
                    Assert.That(exportedWaves.Count, Is.EqualTo(12));
                    foreach (string path in exportedWaves) Assert.That(File.Exists(path), Is.True);
                }
            }
            finally
            {
                if (lab != null) lab.Close();
                if (menu != null) menu.gameObject.SetActive(true);
            }
        }

        private static IEnumerator WaitForLab(RussianVocalizerLab lab)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (lab.IsGenerating && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(lab.IsGenerating, Is.False, "The single bounded worker did not complete or drain.");
        }

        private static IEnumerator CaptureOptionalScreen(Camera camera, string path)
        {
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            if (File.Exists(path)) yield break;
            // Batch mode may have no GUI surface. A camera-only diagnostic is
            // explicitly named as a backdrop, never claimed to prove the IMGUI layout.
            string fallback = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-backdrop.png");
            var target = new RenderTexture(1280, 720, 24);
            var frame = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                frame.Apply();
                File.WriteAllBytes(fallback, frame.EncodeToPNG());
                Debug.LogWarning("The batch GUI did not supply a screenshot; saved only its camera backdrop: " + fallback);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(frame);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static string SafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-');
            return value.Replace('/', '-').Replace('\\', '-');
        }

        private static void WriteWave(string path, float[] samples)
        {
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + samples.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(RussianVocalizerSynthesis.SampleRate);
            writer.Write(RussianVocalizerSynthesis.SampleRate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(samples.Length * 2);
            foreach (float sample in samples)
                writer.Write((short)(Mathf.Clamp(sample * RussianVocalizerLab.PreviewVolume, -1f, 1f) * 32767f));
        }
    }
}
