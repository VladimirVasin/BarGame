using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The keystrokes of every line anybody says, on a small pool of
    /// leased positional sources.
    ///
    /// WHY IT IS NOT <see cref="RetroAudioService"/>. That pool is
    /// built for events: each effect carries a `CooldownSeconds` and a
    /// voice cap of one to three, `RetroAudio.PlayAt` has no per-play
    /// pitch, and a blip is due every `90 ms` at a pitch chosen by the
    /// letter. Routed through there, most of a line would be swallowed
    /// by its own cooldown and the rest would come out at whatever the
    /// global play counter happened to be on. Nothing in this file
    /// enters the `RetroSfx` definitions table or its pools, so the
    /// budget those tests pin is untouched.
    ///
    /// WHY THE SOURCES ARE NOT ON THE SPEAKERS. All three staged casts
    /// — <c>CemeteryWatchmanFactory</c>, <c>SeacoastFishermanFactory</c>
    /// and <c>LastRouteFerrymanFactory</c> — throw if their imported
    /// model contains an `AudioSource` anywhere, and that guard is
    /// worth keeping exactly as it is. A service host owns the sources
    /// and moves one to the speaker's position for the length of a
    /// line, which is the same sound with none of the ownership.
    ///
    /// A lease is held for a whole line rather than per blip, so a
    /// keystroke can never steal the voice out from under the line
    /// still being typed — the thing the pooled service cannot promise.
    /// </summary>
    [DisallowMultipleComponent]
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

        private const int SourcePriority = 178;

        private static NpcSpeechVoice instance;

        private AudioSource[] sources;
        private bool[] leased;
        private NpcSpeechBlipClipCache.Lease clipLease;
        private bool initialized;

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
            if (service == null || service.leased == null)
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

            service.leased[lease] = false;
            AudioSource source = service.sources[lease];
            if (source != null && source.isPlaying)
            {
                source.Stop();
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
                lease >= service.sources.Length)
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

            source.transform.position = worldPosition;
            // Full strength out to the solid radius, then falling in
            // step with the fade rather than against it. See
            // MinimumDistanceMeters for why a flat floor was wrong.
            source.minDistance = Mathf.Max(
                MinimumDistanceMeters,
                earshot.SolidRadiusMeters);
            source.maxDistance = Mathf.Max(
                source.minDistance + 0.1f,
                earshot.CullRadiusMeters);
            source.clip = clip;
            source.volume =
                voice.Volume * Mathf.Clamp01(distanceGain);
            source.pitch = NpcVoiceCatalog.ResolveBlipPitch(
                voice,
                character,
                ordinal,
                extraJitterCents);
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
            leased = new bool[VoiceCount];
            for (int index = 0; index < VoiceCount; index++)
            {
                sources[index] = BuildVoiceSource(
                    transform,
                    VoiceObjectNamePrefix + index);
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
