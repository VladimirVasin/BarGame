using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BarPromenade
{
    public enum RussianVocalizerLabFilter { All, Male, Female }

    /// <summary>Isolated menu audition: one cancellable worker, one owned clip, no PCM bank.</summary>
    [DisallowMultipleComponent]
    public sealed class RussianVocalizerLab : MonoBehaviour
    {
        public const double GameWindowSeconds = NpcSpeechBubbleView.VisibleSeconds -
            NpcSpeechVoice.SynthesisWindowReserveSeconds;
        public const float PreviewVolume = 0.8f;
        private static readonly string[] Presets =
        {
            "Хорошо. Сейчас подам.", "Маршрут отменили. Я всё равно жду.",
            "Последний рейс уже был. Или ещё будет."
        };
        private static readonly string[] PresetLabels = { "Бармен", "Перевозчик", "Последний рейс" };
        private string phrase = Presets[0], status = "Введите фразу или выберите пример.";
        private string characterCount, requestedText, requestedLabel, comparisonALabel, comparisonBLabel;
        private string[] voiceLabels;
        private bool discardGeneration, playbackRequested;
        private RetroUiTextField phraseField;
        private int requestedVoice;
        private double requestedWindowSeconds;
        private AudioSource source;
        private AudioClip clip;
        private Task<RenderedPhrase> generationTask;
        private CancellationTokenSource generationCancellation;
        private GUIStyle titleStyle, hintStyle, statusStyle, buttonStyle, selectedStyle, textStyle, descriptionStyle;

        private sealed class RenderedPhrase
        {
            public RussianVocalizerPlan Plan;
            public float[] Samples;
            public int VoiceIndex;
            public string Label;
            public bool Windowed;
        }

        public bool IsOpen { get; private set; }
        public bool IsGenerating => generationTask != null;
        public bool IsPlaying => source != null && source.isPlaying;
        public int SelectedVoice { get; private set; }
        public bool GameWindowMode { get; private set; }
        public RussianVocalizerLabFilter VoiceFilter { get; private set; }
        public int ComparisonVoiceA { get; private set; }
        public int ComparisonVoiceB { get; private set; } = 1;
        public int PlayingVoice { get; private set; } = -1;
        public RussianVocalizerPlan PlaybackPlan { get; private set; }
        public string Status => status;
        public int VisibleVoiceCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < RussianVocalizerSynthesis.VoiceCount; index++)
                    if (IsVoiceVisible(index)) count++;
                return count;
            }
        }
        public string Phrase
        {
            get => phrase;
            set
            {
                string next = value ?? string.Empty;
                if (next.Length > RussianSpeechPhonetics.MaximumTextLength)
                    next = next.Substring(0, RussianSpeechPhonetics.MaximumTextLength);
                if (next == phrase) return;
                phrase = next;
                characterCount = null;
            }
        }

        public void Open()
        {
            IsOpen = true;
            phraseField ??= new RetroUiTextField(RussianSpeechPhonetics.MaximumTextLength);
            phraseField.Open(phrase);
            UpdateComparisonLabels();
            status = "Выберите пресет. Кнопки А / Б читают одну фразу с одинаковой громкостью.";
        }

        public void Close()
        {
            IsOpen = false;
            phraseField?.Close();
            Stop();
        }

        public void SelectVoice(int index)
        {
            ValidateVoice(index);
            if (SelectedVoice == index) return;
            Stop();
            SelectedVoice = index;
            status = "Выбран голос «" + VoiceLabel(index) + "».";
        }

        public void SetVoiceFilter(RussianVocalizerLabFilter filter)
        {
            if (filter < RussianVocalizerLabFilter.All || filter > RussianVocalizerLabFilter.Female)
                throw new ArgumentOutOfRangeException(nameof(filter));
            if (VoiceFilter == filter) return;
            Stop();
            VoiceFilter = filter;
            if (!IsVoiceVisible(SelectedVoice))
                for (int index = 0; index < RussianVocalizerSynthesis.VoiceCount; index++)
                    if (IsVoiceVisible(index)) { SelectedVoice = index; break; }
            status = filter == RussianVocalizerLabFilter.All ? "Показаны все пресеты." :
                filter == RussianVocalizerLabFilter.Male ? "Показаны мужские пресеты." : "Показаны женские пресеты.";
        }

        public bool IsVoiceVisible(int index)
        {
            ValidateVoice(index);
            RussianVocalizerGender gender = RussianVocalizerSynthesis.LabVoiceGenderAt(index);
            return VoiceFilter == RussianVocalizerLabFilter.All ||
                VoiceFilter == RussianVocalizerLabFilter.Male && gender == RussianVocalizerGender.Male ||
                VoiceFilter == RussianVocalizerLabFilter.Female && gender == RussianVocalizerGender.Female;
        }

        public void SetGameWindowMode(bool enabled)
        {
            if (GameWindowMode == enabled) return;
            Stop();
            GameWindowMode = enabled;
            status = enabled ? "Игровое окно: вся фраза до 3,9 с, без изменения высоты голоса."
                : "Свободно: естественный темп выбранного пресета.";
        }

        public void SetComparisonVoice(int slot, int voiceIndex)
        {
            ValidateSlot(slot);
            ValidateVoice(voiceIndex);
            Stop();
            if (slot == 0) ComparisonVoiceA = voiceIndex;
            else ComparisonVoiceB = voiceIndex;
            UpdateComparisonLabels();
            status = (slot == 0 ? "А: " : "Б: ") + VoiceLabel(voiceIndex) +
                ". Одна фраза и одинаковая громкость для А / Б.";
        }

        public void Play() => RequestPlayback(SelectedVoice, string.Empty);

        public void PlayComparison(int slot)
        {
            ValidateSlot(slot);
            RequestPlayback(slot == 0 ? ComparisonVoiceA : ComparisonVoiceB, slot == 0 ? "А · " : "Б · ");
        }

        private static void ValidateVoice(int index)
        {
            if (index < 0 || index >= RussianVocalizerSynthesis.VoiceCount)
                throw new ArgumentOutOfRangeException(nameof(index));
        }
        private static void ValidateSlot(int slot)
        {
            if (slot < 0 || slot > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        }

        private void RequestPlayback(int voiceIndex, string label)
        {
            if (!IsOpen) return;
            Stop();
            if (string.IsNullOrWhiteSpace(phrase)) { status = "Введите русскую фразу."; return; }
            requestedText = phrase;
            requestedVoice = voiceIndex;
            requestedWindowSeconds = GameWindowMode ? GameWindowSeconds : 0d;
            requestedLabel = label;
            playbackRequested = true;
            status = "Подготовка звука…";
            // Retain only the latest request; a cancelled worker must finish
            // before its replacement can start. Unity objects never enter it.
            if (generationTask == null) StartRequestedGeneration();
        }

        public void Stop()
        {
            playbackRequested = false;
            requestedText = requestedLabel = null;
            discardGeneration = true;
            generationCancellation?.Cancel();
            ReleaseClip();
            status = "Остановлено.";
        }

        private void StartRequestedGeneration()
        {
            string text = requestedText, label = requestedLabel;
            int voice = requestedVoice;
            double window = requestedWindowSeconds;
            playbackRequested = false;
            requestedText = requestedLabel = null;
            discardGeneration = false;
            generationCancellation = new CancellationTokenSource();
            CancellationToken token = generationCancellation.Token;
            generationTask = Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                RussianVocalizerVoice profile = RussianVocalizerSynthesis.LabVoiceAt(voice);
                RussianVocalizerPlan plan = RussianVocalizerSynthesis.Prepare(text, profile, window);
                return new RenderedPhrase
                {
                    Plan = plan, Samples = RussianVocalizerSynthesis.Render(plan, token), VoiceIndex = voice,
                    Label = label, Windowed = window > 0d
                };
            }, token);
        }

        private void Update()
        {
            phraseField?.Tick();
            if (clip != null && !IsPlaying)
            {
                ReleaseClip();
                if (IsOpen && generationTask == null) status = "Готово. Сравните другой пресет или нажмите А / Б.";
            }
            if (generationTask == null || !generationTask.IsCompleted) return;
            Task<RenderedPhrase> completed = generationTask;
            bool useResult = IsOpen && !discardGeneration && !playbackRequested;
            generationTask = null;
            generationCancellation.Dispose();
            generationCancellation = null;
            try
            {
                RenderedPhrase rendered = completed.GetAwaiter().GetResult();
                if (useResult)
                {
                    if (rendered.Samples.Length == 0) status = "В этой фразе нет русских звуков.";
                    else BeginPlayback(rendered);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (useResult) status = "Не удалось подготовить звук.";
                Debug.LogException(exception, this);
            }
            if (IsOpen && playbackRequested) StartRequestedGeneration();
        }

        private void BeginPlayback(RenderedPhrase rendered)
        {
            if (source == null)
            {
                var audioObject = new GameObject("Russian Vocalizer Audio");
                audioObject.transform.SetParent(transform, false);
                source = audioObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                source.outputAudioMixerGroup = GameAudioMixer.UiGroup;
                source.volume = PreviewVolume;
            }
            clip = AudioClip.Create("Russian Vocalizer · " + VoiceLabel(rendered.VoiceIndex),
                rendered.Samples.Length, 1, RussianVocalizerSynthesis.SampleRate, false);
            clip.hideFlags = HideFlags.DontSave;
            if (!clip.SetData(rendered.Samples, 0))
            {
                ReleaseClip();
                status = "Не удалось подготовить звук.";
                return;
            }
            PlaybackPlan = rendered.Plan;
            PlayingVoice = rendered.VoiceIndex;
            source.volume = PreviewVolume;
            source.pitch = 1f;
            source.clip = clip;
            source.Play();
            status = rendered.Label + VoiceLabel(rendered.VoiceIndex) + " · " +
                rendered.Plan.DurationSeconds.ToString("0.00") + " с" +
                (rendered.Windowed ? " · игровое окно до 3,9 с" : " · темп пресета");
        }

        private void ReleaseClip()
        {
            PlaybackPlan = default;
            PlayingVoice = -1;
            if (source != null) { source.Stop(); source.clip = null; }
            if (clip == null) return;
            if (Application.isPlaying) Destroy(clip);
            else DestroyImmediate(clip);
            clip = null;
        }

        private void OnDisable() => Close();
        private void OnDestroy()
        {
            Close();
            if (generationTask == null) return;
            CancellationTokenSource cancellation = generationCancellation;
            generationTask.ContinueWith(completed =>
            {
                _ = completed.Exception;
                cancellation.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            generationTask = null;
            generationCancellation = null;
        }

        /// <returns>True when the player requests the main menu.</returns>
        public bool Draw()
        {
            if (!IsOpen) return false;
            EnsureStyles();
            RetroUiTheme.DrawPanel(new Rect(24f, 4f, 592f, 352f), RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter, false, 0f, 1f);
            GUI.Label(new Rect(36f, 12f, 568f, 20f), "Вокализатор · 12 голосов", titleStyle);
            Rect textRect = new Rect(36f, 38f, 568f, 50f);
            RetroUiTheme.DrawPanel(textRect, RetroUiTheme.Ink, RetroUiTheme.BorderMuted, false, 0f, 1f);
            Phrase = phraseField.Draw(new Rect(42f, 44f, 556f, 38f), phrase, textStyle,
                out bool submit, out bool cancel);
            if (cancel) return true;
            if (submit) Play();
            characterCount ??= phrase.Length + " / " + RussianSpeechPhonetics.MaximumTextLength +
                " · Ударения приблизительные · Ctrl+Enter — слушать · Esc — назад";
            GUI.Label(new Rect(36f, 90f, 568f, 12f), characterCount, hintStyle);
            for (int index = 0; index < Presets.Length; index++)
            {
                if (!DrawButton(new Rect(36f + index * 192f, 104f, 184f, 18f), PresetLabels[index])) continue;
                Stop(); Phrase = Presets[index]; status = "Пример выбран. Нажмите «Слушать выбранный».";
            }
            if (DrawButton(new Rect(36f, 126f, 276f, 20f), "Свободно · темп пресета", !GameWindowMode)) SetGameWindowMode(false);
            if (DrawButton(new Rect(328f, 126f, 276f, 20f), "Игровое окно · до 3,9 с", GameWindowMode)) SetGameWindowMode(true);
            if (DrawButton(new Rect(36f, 150f, 184f, 18f), "Все голоса", VoiceFilter == RussianVocalizerLabFilter.All))
                SetVoiceFilter(RussianVocalizerLabFilter.All);
            if (DrawButton(new Rect(228f, 150f, 184f, 18f), "Мужские", VoiceFilter == RussianVocalizerLabFilter.Male))
                SetVoiceFilter(RussianVocalizerLabFilter.Male);
            if (DrawButton(new Rect(420f, 150f, 184f, 18f), "Женские", VoiceFilter == RussianVocalizerLabFilter.Female))
                SetVoiceFilter(RussianVocalizerLabFilter.Female);
            int visible = 0;
            for (int index = 0; index < RussianVocalizerSynthesis.VoiceCount; index++)
            {
                if (!IsVoiceVisible(index)) continue;
                Rect rect = new Rect(36f + visible % 3 * 192f, 172f + visible / 3 * 22f, 184f, 20f);
                if (DrawButton(rect, VoiceLabel(index), SelectedVoice == index)) SelectVoice(index);
                visible++;
            }
            GUI.Label(new Rect(36f, 260f, 568f, 20f),
                RussianVocalizerSynthesis.LabVoiceDescriptionAt(SelectedVoice), descriptionStyle);
            if (DrawButton(new Rect(36f, 282f, 56f, 20f), "В А")) SetComparisonVoice(0, SelectedVoice);
            if (DrawButton(new Rect(100f, 282f, 212f, 20f), comparisonALabel)) PlayComparison(0);
            if (DrawButton(new Rect(328f, 282f, 56f, 20f), "В Б")) SetComparisonVoice(1, SelectedVoice);
            if (DrawButton(new Rect(392f, 282f, 212f, 20f), comparisonBLabel)) PlayComparison(1);
            GUI.Label(new Rect(36f, 304f, 568f, 14f), status, statusStyle);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !string.IsNullOrWhiteSpace(phrase);
            if (DrawButton(new Rect(36f, 322f, 176f, 22f), "Слушать выбранный")) Play();
            GUI.enabled = previousEnabled && (generationTask != null || clip != null || playbackRequested);
            if (DrawButton(new Rect(232f, 322f, 176f, 22f), "Стоп")) Stop();
            GUI.enabled = previousEnabled;
            bool back = DrawButton(new Rect(428f, 322f, 176f, 22f), "Назад");
            return back;
        }

        private string VoiceLabel(int index)
        {
            if (voiceLabels == null)
            {
                voiceLabels = new string[RussianVocalizerSynthesis.VoiceCount];
                for (int voice = 0; voice < voiceLabels.Length; voice++)
                    voiceLabels[voice] = (RussianVocalizerSynthesis.LabVoiceGenderAt(voice) == RussianVocalizerGender.Male
                        ? "М · " : "Ж · ") + RussianVocalizerSynthesis.LabVoiceNameAt(voice);
            }
            return voiceLabels[index];
        }
        private void UpdateComparisonLabels()
        {
            comparisonALabel = "А: " + VoiceLabel(ComparisonVoiceA);
            comparisonBLabel = "Б: " + VoiceLabel(ComparisonVoiceB);
        }
        private bool DrawButton(Rect rect, string label, bool selected = false)
        {
            RetroUiTheme.DrawPanel(rect, RetroUiTheme.PanelRaised, RetroUiTheme.BorderMuted, false, 0f, 1f);
            if (selected) RetroUiTheme.DrawSelection(rect, true);
            return RetroUiInput.Button(rect, label, selected ? selectedStyle : buttonStyle);
        }
        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = RetroUiTheme.CreateLabelStyle(14, TextAnchor.MiddleCenter, RetroUiTheme.Text, true);
            hintStyle = RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleCenter, RetroUiTheme.Muted);
            statusStyle = RetroUiTheme.CreateLabelStyle(10, TextAnchor.MiddleCenter, RetroUiTheme.Text);
            buttonStyle = RetroUiTheme.CreateButtonStyle(11, TextAnchor.MiddleCenter, RetroUiTheme.Text, false);
            selectedStyle = RetroUiTheme.CreateButtonStyle(11, TextAnchor.MiddleCenter, RetroUiTheme.SelectionText, true);
            descriptionStyle = RetroUiTheme.CreateLabelStyle(10, TextAnchor.MiddleCenter, RetroUiTheme.Text, false, true);
            textStyle = RetroUiTheme.CreateLabelStyle(10, TextAnchor.UpperLeft, RetroUiTheme.Text, false, true);
            textStyle.clipping = TextClipping.Clip;
            textStyle.padding = new RectOffset(0, 0, 0, 0);
        }
    }
}
