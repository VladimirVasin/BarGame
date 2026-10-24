using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared whole-line Russian synthesis and retained non-Russian writing
    /// clicks, on bounded leased positional sources. Immutable phone plans and
    /// PCM rendering use pure code; Unity clips stay on the main thread.
    /// Sources belong to this service, never staged NPC prefabs. A line's lease
    /// cannot be stolen, and a cancelled worker cannot publish into a new line.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(10000)]
    public sealed class NpcSpeechVoice : MonoBehaviour
    {
        public const string RuntimeObjectName =
            "[Bar Promenade] NPC Speech";

        public const string VoiceObjectNamePrefix = "Speech Voice ";

        /// <summary>Every bubble slot, the prompt panel's one line, and
        /// the hero's own mutter, which hangs on a second view of its own
        /// so that his line and a quarrel in the park can never evict
        /// each other. Nothing in the game can be typing more lines than
        /// that at once, so a lease never has to be refused in practice.
        /// </summary>
        public const int VoiceCount = NpcSpeechBubbleView.Capacity + 2;

        /// <summary>
        /// The floor under a source's own rolloff, not the rolloff
        /// itself: <see cref="Blip"/> raises `minDistance` to the
        /// speaker's SOLID radius so the keystroke holds full strength
        /// for exactly as long as his words are drawn solid, and only
        /// then starts falling.
        ///
        /// The first build left it flat at `1.2 m` for everybody, and
        /// that was the real reason the voices felt cut short rather
        /// than distant: the linear rolloff began a stride away from the
        /// man and ran against the fade curve instead of with it, so the
        /// two attenuations multiplied and a blip was already most of
        /// the way gone before the line looked faint at all.
        /// </summary>
        public const float MinimumDistanceMeters = 1.2f;

        /// <summary>The same dull ceiling the rest of the synthesized
        /// world wears, a little above the raven's `2800` so the
        /// partial that tells two speakers apart survives it.</summary>
        public const float LowPassFrequencyHz = 3400f;
        public const float VocalizerLowPassFrequencyHz = IntoxicationPerceptionRules.SpeechSoberLowPassHz;

        private const int SourcePriority = 178;
        public const int MaximumSynthesisWorkers = 2;
        public const float SynthesisWindowReserveSeconds = 0.5f;

        private static NpcSpeechVoice instance;

        private AudioSource[] sources;
        private AudioLowPassFilter[] filters;
        private bool[] leased;
        private NpcSpeechBlipClipCache.Lease clipLease;
        private bool initialized;
        private IntoxicationPerceptionProfile perception;
        private SpeechLine[] lines;
        private readonly RenderJob[] jobs = new RenderJob[MaximumSynthesisWorkers];

        private sealed class SpeechLine
        {
            public uint Version;
            public string Text;
            public RussianVocalizerVoice Voice;
            public RussianVocalizerPlan Plan;
            public int VoiceOrdinal;
            public AudioClip Clip;
            public bool Requested, Ready, HasJob, Started, Paused, Finished;
            public float LastClock = -1f;
            public float PlayedSeconds;
            public float TargetSeconds;
            public int LastFrame = -1;
            public float BaseVolume;
            public float BaseLowPassFrequencyHz = LowPassFrequencyHz;
            public float BasePitch = 1f;
        }

        private sealed class RenderJob
        {
            public int Lease;
            public uint Version;
            public Task<float[]> Task;
            public CancellationTokenSource Cancellation;
        }

        public int RunningSynthesisCount
        {
            get { int count = 0; foreach (RenderJob job in jobs) if (job != null) count++; return count; }
        }
        public int ActiveSynthesizedClipCount
        {
            get
            {
                int count = 0;
                if (lines != null) foreach (SpeechLine line in lines) if (line.Clip != null) count++;
                return count;
            }
        }

        public static NpcSpeechVoice Instance => instance;
        public bool IsInitialized => initialized;
        public int SourceCount =>
            sources != null ? sources.Length : 0;

        public int LeasedCount
        {
            get
            {
                if (leased == null)
                {
                    return 0;
                }

                int count = 0;
                for (int index = 0; index < leased.Length; index++)
                {
                    if (leased[index])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        /// <summary>
        /// Raises the service on first use, and ONLY while the game is
        /// actually running. A keystroke is a play-mode thing: the
        /// EditMode suites step whole lines of dialogue to prove the
        /// typing, and every one of them would otherwise leave behind a
        /// persistent host, eight generated clips and five sources that
        /// nothing was ever going to hear.
        /// </summary>
        public static NpcSpeechVoice EnsureInstalled()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindAnyObjectByType<NpcSpeechVoice>();
            if (instance != null)
            {
                instance.Initialize();
                return instance;
            }

            if (!Application.isPlaying)
            {
                return null;
            }

            var serviceObject = new GameObject(RuntimeObjectName);
            instance = serviceObject.AddComponent<NpcSpeechVoice>();
            return instance;
        }

        /// <summary>
        /// Takes one source for the length of one line. Returns `-1`
        /// when every voice is busy, which a caller treats as «this
        /// line is silent» rather than as a failure: a missing tick is
        /// not worth cutting somebody else's line short for.
        /// </summary>
        public static int Lease()
        {
            NpcSpeechVoice service = EnsureInstalled();
            if (service == null || !service.isActiveAndEnabled || service.leased == null)
            {
                return -1;
            }

            for (int index = 0;
                 index < service.leased.Length;
                 index++)
            {
                if (!service.leased[index])
                {
                    service.leased[index] = true;
                    return index;
                }
            }

            return -1;
        }

        public static void Release(int lease)
        {
            NpcSpeechVoice service = instance;
            if (service == null ||
                service.leased == null ||
                lease < 0 ||
                lease >= service.leased.Length)
            {
                return;
            }

            service.ClearLine(lease);
            service.leased[lease] = false;
            AudioSource source = service.sources[lease];
            if (source != null && source.isPlaying)
            {
                source.Stop();
            }
        }

        /// <summary>
        /// Plan one complete phrase at admission. Only two workers can render;
        /// waiting work lives in the fixed voice slots, never an event backlog.
        /// Timing is fitted to the authored window in the synthesizer, so pitch
        /// and character identity do not change when a line needs to be quicker.
        /// </summary>
        public static void BeginLine(int lease, int voiceOrdinal, string text, float windowSeconds)
        {
            NpcSpeechVoice service = instance;
            if (service == null || !service.ValidLease(lease)) return;
            service.ClearLine(lease);
            SpeechLine line = service.lines[lease];
            line.Requested = true;
            line.VoiceOrdinal = voiceOrdinal;
            NpcVoiceProfile profile = NpcVoiceCatalog.ProfileAt(voiceOrdinal);
            if (!profile.IsValid || !float.IsFinite(windowSeconds) || windowSeconds <= 0f ||
                !RussianSpeechPhonetics.HasRussianSpeech(text))
            {
                line.Ready = true;
                return;
            }
            RussianVocalizerVoice voice = profile.VocalizerVoice;
            double budget = Math.Max(0.05, windowSeconds - SynthesisWindowReserveSeconds);
            line.Plan = RussianVocalizerSynthesis.Prepare(text, voice, budget);
            if (line.Plan.SampleCount == 0) { line.Ready = true; return; }
            line.Voice = line.Plan.Voice;
            line.Text = text;
            service.StartAvailableJobs();
        }

        public static bool IsLineReady(int lease) => instance != null &&
            instance.ValidLease(lease) && instance.lines[lease].Ready;

        public static float LineDuration(int lease) => instance != null && instance.ValidLease(lease) &&
            instance.lines[lease].Clip != null ? instance.lines[lease].Clip.length : 0f;

        public static RussianVocalizerPlan LinePlan(int lease) => instance != null && instance.ValidLease(lease)
            ? instance.lines[lease].Plan : null;

        /// <summary>Use the bubble's clock, position and opacity, including manual
        /// clocks. Repeated time pauses; a hitch seeks forward instead of replaying
        /// missed syllables. A reading tail can never restart a finished clip.</summary>
        public static float SyncLine(int lease, float elapsedSeconds, Vector3 worldPosition,
            float distanceGain, in NpcEarshotProfile earshot, float extraJitterCents = 0f)
        {
            NpcSpeechVoice service = instance;
            if (service == null || !service.ValidLease(lease) || !float.IsFinite(elapsedSeconds)) return 0f;
            SpeechLine line = service.lines[lease];
            AudioSource source = service.sources[lease];
            line.LastFrame = Time.frameCount;
            if (!line.Ready || line.Clip == null) return 0f;
            service.ConfigureSource(lease, line.VoiceOrdinal, worldPosition, distanceGain, earshot,
                VocalizerLowPassFrequencyHz);
            if (line.Finished) return line.PlayedSeconds;
            if (line.Started && elapsedSeconds + 0.001f < line.LastClock)
            {
                service.ClearLine(lease);
                line.Ready = true;
                return 0f;
            }
            float previousClock = line.LastClock;
            line.LastClock = Mathf.Max(0f, elapsedSeconds);
            if (GameTimeScaleRuntime.IsPaused || AudioListener.pause || distanceGain <= 0f ||
                line.Started && Mathf.Abs(previousClock - elapsedSeconds) < 0.00001f)
            {
                PauseLine(source, line);
                return line.PlayedSeconds;
            }
            // Integrate the owner's clock at the perceived tape rate. Comparing
            // against raw wall time would repeatedly seek past the slow syllables.
            float step = previousClock < 0f ? line.LastClock :
                Mathf.Max(0f, line.LastClock - previousClock);
            line.TargetSeconds += step * service.perception.PlaybackRate;
            if (line.TargetSeconds >= line.Clip.length)
            {
                source.Stop();
                line.Finished = true;
                line.PlayedSeconds = line.Clip.length;
                return line.PlayedSeconds;
            }
            int desired = Mathf.Clamp(Mathf.RoundToInt(line.TargetSeconds *
                RussianVocalizerSynthesis.SampleRate), 0, line.Clip.samples - 1);
            if (!line.Started)
            {
                source.clip = line.Clip;
                source.timeSamples = desired;
                source.Play();
                line.Started = true;
                line.Paused = false;
                line.PlayedSeconds = desired / (float)RussianVocalizerSynthesis.SampleRate;
            }
            else
            {
                if (!source.isPlaying && !line.Paused)
                {
                    line.Finished = true;
                    line.PlayedSeconds = line.Clip.length;
                    return line.PlayedSeconds;
                }
                // Slower owner clocks must never rewind and repeat syllables.
                // Reveal reads actual delivered PCM; real forward seeks may skip
                // ahead, while pause still freezes both sound and presentation.
                if (desired - source.timeSamples > RussianVocalizerSynthesis.SampleRate * 0.12f)
                    source.timeSamples = desired;
                if (line.Paused) { source.UnPause(); line.Paused = false; }
                line.PlayedSeconds = Mathf.Max(line.PlayedSeconds,
                    source.timeSamples / (float)RussianVocalizerSynthesis.SampleRate);
            }
            return line.PlayedSeconds;
        }

        private bool ValidLease(int lease) => initialized && leased != null &&
            lease >= 0 && lease < leased.Length && leased[lease];

        private void ConfigureSource(int lease, int ordinal, Vector3 position,
            float distanceGain, in NpcEarshotProfile earshot, float lowPassFrequencyHz,
            float basePitch = 1f)
        {
            AudioSource source = sources[lease];
            source.transform.position = position;
            source.minDistance = Mathf.Max(MinimumDistanceMeters, earshot.SolidRadiusMeters);
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, earshot.CullRadiusMeters);
            lines[lease].BaseVolume = NpcVoiceCatalog.ProfileAt(ordinal).Volume *
                (float.IsFinite(distanceGain) ? Mathf.Clamp01(distanceGain) : 0f);
            lines[lease].BaseLowPassFrequencyHz = lowPassFrequencyHz;
            lines[lease].BasePitch = basePitch;
            ApplyPerception(lease);
        }

        private void ApplyPerception(int lease)
        {
            perception = GameTimeScaleRuntime.AudioPerception;
            SpeechLine line = lines[lease];
            sources[lease].volume = line.BaseVolume * perception.SpeechGain;
            sources[lease].pitch = line.BasePitch * perception.PlaybackRate;
            filters[lease].cutoffFrequency = Mathf.Min(line.BaseLowPassFrequencyHz,
                perception.SpeechLowPassFrequencyHz);
        }

        private static void PauseLine(AudioSource source, SpeechLine line)
        {
            if (!line.Started || line.Paused || line.Finished) return;
            source.Pause();
            line.Paused = true;
        }

        private void ClearLine(int lease)
        {
            SpeechLine line = lines[lease];
            foreach (RenderJob job in jobs)
                if (job != null && job.Lease == lease && job.Version == line.Version)
                    job.Cancellation.Cancel();
            unchecked { line.Version++; }
            AudioSource source = sources[lease];
            if (source != null) { source.Stop(); source.clip = null; }
            if (line.Clip != null)
            {
                if (Application.isPlaying) Destroy(line.Clip);
                else DestroyImmediate(line.Clip);
            }
            line.Clip = null;
            line.Plan = null;
            line.Text = null;
            line.Requested = line.Ready = line.HasJob = line.Started = line.Paused = line.Finished = false;
            line.LastClock = -1f;
            line.PlayedSeconds = 0f;
            line.TargetSeconds = 0f;
            line.LastFrame = -1;
            line.BaseVolume = 0f;
            line.BaseLowPassFrequencyHz = LowPassFrequencyHz;
            line.BasePitch = 1f;
        }

        private void StartAvailableJobs()
        {
            for (int worker = 0; worker < jobs.Length; worker++)
            {
                if (jobs[worker] != null) continue;
                for (int lease = 0; lease < lines.Length; lease++)
                {
                    SpeechLine line = lines[lease];
                    if (!leased[lease] || !line.Requested || line.Ready || line.HasJob || line.Text == null) continue;
                    RussianVocalizerPlan plan = line.Plan;
                    var cancellation = new CancellationTokenSource();
                    CancellationToken token = cancellation.Token;
                    jobs[worker] = new RenderJob
                    {
                        Lease = lease, Version = line.Version, Cancellation = cancellation,
                        Task = Task.Run(() => RussianVocalizerSynthesis.Render(plan, token), token)
                    };
                    line.HasJob = true;
                    break;
                }
            }
        }

        private void Update()
        {
            if (!initialized) return;
            for (int worker = 0; worker < jobs.Length; worker++)
            {
                RenderJob job = jobs[worker];
                if (job == null || !job.Task.IsCompleted) continue;
                jobs[worker] = null;
                SpeechLine line = lines[job.Lease];
                bool accept = leased[job.Lease] && line.Version == job.Version && line.Requested;
                try
                {
                    float[] samples = job.Task.GetAwaiter().GetResult();
                    if (accept && samples.Length > 0)
                    {
                        AudioClip clip = AudioClip.Create("Speech_" + NpcVoiceCatalog.ProfileAt(line.VoiceOrdinal).Id,
                            samples.Length, 1, RussianVocalizerSynthesis.SampleRate, false);
                        clip.hideFlags = HideFlags.DontSave;
                        if (clip.SetData(samples, 0)) line.Clip = clip;
                        else Destroy(clip);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) { Debug.LogException(exception, this); }
                finally
                {
                    job.Cancellation.Dispose();
                    if (accept) { line.Ready = true; line.HasJob = false; line.Text = null; }
                }
            }
            StartAvailableJobs();
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            for (int lease = 0; lease < lines.Length; lease++)
            {
                SpeechLine line = lines[lease];
                // Covers already-playing fallback clicks too, whose owners only
                // call Blip at character onsets. Never regenerate PCM or restart it.
                if (leased[lease]) ApplyPerception(lease);
                if (leased[lease] && (line.LastFrame != Time.frameCount ||
                    GameTimeScaleRuntime.IsPaused || AudioListener.pause))
                    PauseLine(sources[lease], line);
            }
        }

        private void OnDisable()
        {
            if (!initialized || instance != this) return;
            for (int lease = 0; lease < lines.Length; lease++)
            {
                ClearLine(lease);
                leased[lease] = false;
            }
        }

        /// <summary>
        /// One keystroke. <paramref name="distanceGain"/> is the very
        /// number the bubble spends on its alpha — <see
        /// cref="NpcEarshotProfile.ResolveOpacity"/> — so a line that
        /// looks faint sounds faint, and the linear rolloff on top of
        /// it is what makes it sound DISTANT as well.
        /// </summary>
        /// <remarks>
        /// The voice is addressed by its CATALOG ORDINAL, not by a
        /// profile handed in by value. The clip bank is indexed by that
        /// ordinal, and a profile's own <c>Id</c> ("watchman") is not
        /// its design id ("cemetery_watchman_v1") — resolving one
        /// through the other would hash-fall onto a different
        /// speaker's clip and hand him somebody else's voice, silently.
        /// </remarks>
        public static bool Blip(
            int lease,
            int voiceOrdinal,
            char character,
            uint ordinal,
            Vector3 worldPosition,
            float distanceGain,
            in NpcEarshotProfile earshot,
            float extraJitterCents = 0f)
        {
            NpcSpeechVoice service = instance;
            if (service == null ||
                !service.initialized ||
                service.sources == null ||
                service.clipLease == null ||
                lease < 0 ||
                lease >= service.sources.Length || !service.leased[lease] || service.lines[lease].Requested)
            {
                return false;
            }

            if (float.IsNaN(distanceGain) || distanceGain <= 0f)
            {
                return false;
            }

            NpcVoiceProfile voice =
                NpcVoiceCatalog.ProfileAt(voiceOrdinal);
            AudioClip clip = service.clipLease.ClipAt(voiceOrdinal);
            AudioSource source = service.sources[lease];
            if (source == null || clip == null || !voice.IsValid)
            {
                return false;
            }

            service.ConfigureSource(lease, voiceOrdinal, worldPosition, distanceGain, earshot,
                LowPassFrequencyHz, NpcVoiceCatalog.ResolveBlipPitch(
                    voice, character, ordinal, extraJitterCents));
            source.clip = clip;
            source.Play();
            return true;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            if (sources != null)
            {
                for (int index = 0; index < sources.Length; index++)
                {
                    AudioSource source = sources[index];
                    if (source != null && source.isPlaying)
                    {
                        source.Stop();
                    }
                }
            }

            for (int index = 0; lines != null && index < lines.Length; index++) ClearLine(index);
            for (int worker = 0; worker < jobs.Length; worker++)
            {
                RenderJob job = jobs[worker];
                if (job == null) continue;
                job.Cancellation.Cancel();
                // A detached pure continuation observes cancellation/faults and
                // releases the CTS; it captures no Unity objects or PCM result.
                CancellationTokenSource cancellation = job.Cancellation;
                job.Task.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    cancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                jobs[worker] = null;
            }

            // Voices before lease: every source is stopped above, so
            // nothing is still reading a clip when the bank goes.
            clipLease?.Dispose();
            clipLease = null;
            instance = null;
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            clipLease = NpcSpeechBlipClipCache.Acquire();
            sources = new AudioSource[VoiceCount];
            filters = new AudioLowPassFilter[VoiceCount];
            leased = new bool[VoiceCount];
            lines = new SpeechLine[VoiceCount];
            for (int index = 0; index < VoiceCount; index++)
            {
                sources[index] = BuildVoiceSource(
                    transform,
                    VoiceObjectNamePrefix + index);
                filters[index] = sources[index].GetComponent<AudioLowPassFilter>();
                lines[index] = new SpeechLine();
            }

            initialized = true;
        }

        /// <summary>
        /// The one place a speech source's knobs are set. Routed to
        /// `SfxWorld` rather than `AmbienceDetails`, where the raven
        /// sits: a bird is a detail of the place, and a line somebody
        /// said is an event somebody caused. Not `Ui` either — it is
        /// positional, and the whole point is that it comes from over
        /// there.
        /// </summary>
        private static AudioSource BuildVoiceSource(
            Transform host,
            string name)
        {
            var voiceObject = new GameObject(name);
            voiceObject.transform.SetParent(host, false);

            AudioSource source =
                voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = MinimumDistanceMeters;
            source.maxDistance =
                NpcEarshotProfile.ShoutCullRadiusMeters;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.priority = SourcePriority;
            source.bypassReverbZones = true;
            GameAudioMixer.Route(source, GameAudioGroup.SfxWorld);

            AudioLowPassFilter filter =
                voiceObject.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = LowPassFrequencyHz;
            filter.lowpassResonanceQ = 1f;
            return source;
        }
    }
}
