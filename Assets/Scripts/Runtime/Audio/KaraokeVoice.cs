using System;
using System.Threading;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One scene-owned live vowel at the hero's actual head. Controls cross into
    /// the current source DSP block; Unity objects stay on the main thread.
    /// Meter values describe the source before shared perception pitch and VHS.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KaraokeVoice : MonoBehaviour, IDisposable
    {
        public const float MinimumFrequencyHz = RussianVocalizerSustain.MinimumFrequencyHz;
        public const float MaximumFrequencyHz = RussianVocalizerSustain.MaximumFrequencyHz;
        public const float InitialFrequencyHz = RussianVocalizerSustain.InitialFrequencyHz;
        public const int SampleRate = RussianVocalizerSynthesis.SampleRate;
        public const float OutputVolume = .75f;
        public const float MaximumDistanceMeters = 18f;

        private Transform speakerHead;
        private AudioSource source;
        private AudioLowPassFilter toneFilter;
        private AudioClip clip;
        private KaraokeVoiceOutput outputFilter;
        private StreamState stream;
        private bool paused, playbackActive;

        public bool IsInitialized => stream != null && source != null && speakerHead != null;
        public bool IsSinging => IsInitialized && playbackActive && !paused && stream.Gate;
        public float Envelope => IsInitialized && playbackActive && !paused ? stream.Envelope : 0;
        public float SmoothedFrequencyHz => stream != null ? stream.FrequencyHz : InitialFrequencyHz;
        public AudioSource Source => source;
        public AudioLowPassFilter ToneFilter => toneFilter;
        public AudioClip Clip => clip;
        public float AppliedPlaybackRate => stream != null ? stream.PlaybackRate : 1f;

        private sealed class StreamState
        {
            private readonly RussianVocalizerSustainOutput synthesis;
            private int appliedResetEpoch;
            public int ResetEpoch;
            public volatile float TargetFrequencyHz = InitialFrequencyHz;
            public volatile float Envelope;
            public volatile float FrequencyHz = InitialFrequencyHz;
            public volatile bool Gate, Paused;
            public volatile bool Suppressed = true;
            public volatile float PlaybackRate = 1f;
            public int OutputSampleRate;

            public StreamState(in RussianVocalizerVoice voice, int outputSampleRate)
            {
                synthesis = new RussianVocalizerSustainOutput(voice);
                OutputSampleRate = outputSampleRate;
            }

            public void Render(float[] data, int channels)
            {
                if (Suppressed || Paused)
                {
                    Array.Clear(data, 0, data.Length);
                    Envelope = 0;
                    return;
                }
                int nextEpoch = Volatile.Read(ref ResetEpoch);
                if (nextEpoch != appliedResetEpoch)
                {
                    synthesis.Reset();
                    appliedResetEpoch = nextEpoch;
                }
                synthesis.Render(data, channels, Volatile.Read(ref OutputSampleRate), TargetFrequencyHz, Gate, PlaybackRate);
                // Stop/dispose may arrive while a block is rendering. It cannot publish
                // a surviving tail, meter value or late restart after ownership is lost.
                if (Suppressed || Paused || nextEpoch != Volatile.Read(ref ResetEpoch))
                {
                    Array.Clear(data, 0, data.Length);
                    Envelope = 0;
                    return;
                }
                FrequencyHz = synthesis.SmoothedFrequencyHz;
                Envelope = synthesis.Envelope;
            }
        }

        public void Initialize(Transform actualSpeakerHead)
        {
            if (actualSpeakerHead == null) throw new ArgumentNullException(nameof(actualSpeakerHead));
            if (IsInitialized && speakerHead == actualSpeakerHead) return;
            Dispose();
            speakerHead = actualSpeakerHead;
            NpcVoiceProfile profile = NpcVoiceCatalog.ProfileAt(NpcVoiceCatalog.HeroMutterOrdinal);
            stream = new StreamState(profile.VocalizerVoice, Math.Max(8000, AudioSettings.outputSampleRate));
            var output = new GameObject("Karaoke Vocal Output");
            output.transform.SetParent(speakerHead, false);
            source = output.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = NpcSpeechVoice.MinimumDistanceMeters;
            source.maxDistance = MaximumDistanceMeters;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.priority = 140;
            source.bypassReverbZones = false;
            source.ignoreListenerPause = false;
            GameAudioMixer.Route(source, GameAudioGroup.SfxWorld);
            outputFilter = output.AddComponent<KaraokeVoiceOutput>();
            outputFilter.Bind(ReadOutput);
            toneFilter = output.AddComponent<AudioLowPassFilter>();
            // A tiny silent mono carrier gives the source its channel/spatial
            // layout. The filter generates the current block, never queued PCM.
            clip = AudioClip.Create("Hero Karaoke Carrier", 256, 1, SampleRate, false);
            clip.hideFlags = HideFlags.DontSave;
            source.clip = clip;
            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfiguration;
            ApplyPerception();
        }

        public void SetControl(float frequencyHz, bool singing)
        {
            if (!IsInitialized || !isActiveAndEnabled) return;
            stream.TargetFrequencyHz = float.IsNaN(frequencyHz) || float.IsInfinity(frequencyHz)
                ? InitialFrequencyHz : Mathf.Clamp(frequencyHz, MinimumFrequencyHz, MaximumFrequencyHz);
            stream.Gate = singing && !paused;
            if (paused || !singing) return;
            stream.Suppressed = false;
            if (!playbackActive)
            {
                ApplyPerception();
                source.Play();
                playbackActive = true;
            }
        }

        public void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            if (!IsInitialized) return;
            stream.Paused = value;
            // A streaming source may already hold PCM ahead of the callback.
            // Discard that transport; returning focus never resumes a held vowel.
            // Only a fresh accepted gate can start playback through SetControl.
            Stop();
        }

        /// <summary>Immediate ownership cancellation; subsequent controls can reuse the prepared voice.</summary>
        public void Stop()
        {
            StreamState current = stream;
            if (current != null)
            {
                current.Suppressed = true;
                current.Gate = false;
                current.Envelope = 0;
                Interlocked.Increment(ref current.ResetEpoch);
            }
            if (source != null) source.Stop();
            playbackActive = false;
        }

        public void Dispose()
        {
            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfiguration;
            Stop();
            Volatile.Write(ref stream, null);
            if (outputFilter != null) outputFilter.Bind(null);
            if (source != null)
            {
                source.clip = null;
                Destroy(source.gameObject);
            }
            if (clip != null) Destroy(clip);
            source = null;
            toneFilter = null;
            clip = null;
            outputFilter = null;
            speakerHead = null;
            paused = false;
        }

        private void ReadOutput(float[] data, int channels)
        {
            StreamState current = Volatile.Read(ref stream);
            if (current == null) Array.Clear(data, 0, data.Length);
            else current.Render(data, channels);
        }

        private void HandleAudioConfiguration(bool deviceChanged)
        {
            StreamState current = Volatile.Read(ref stream);
            if (current != null) Volatile.Write(ref current.OutputSampleRate, Math.Max(8000, AudioSettings.outputSampleRate));
        }

        private void Update()
        {
            if (stream == null) return;
            if (!IsInitialized) { Dispose(); return; }
            ApplyPerception();
            if (playbackActive && !paused && !stream.Gate && stream.Envelope <= 0f)
                Stop();
        }

        private void ApplyPerception()
        {
            if (source == null) return;
            IntoxicationPerceptionProfile perception = GameTimeScaleRuntime.AudioPerception;
            source.volume = OutputVolume * perception.SpeechGain;
            // Source pitch precedes the DSP generator. Resample inside the
            // bounded output state so common slowdown still lowers formants too.
            source.pitch = 1f;
            stream.PlaybackRate = perception.PlaybackRate;
            toneFilter.cutoffFrequency = Mathf.Min(NpcSpeechVoice.VocalizerLowPassFrequencyHz,
                perception.SpeechLowPassFrequencyHz);
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Dispose();
    }
}
