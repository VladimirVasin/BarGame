using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One raven's voice: a child AudioSource beside the bird's host,
    /// built on the alpine village's voice idiom because the village
    /// dog is the project's one synthesized animal-call precedent. A
    /// plain class rather than a MonoBehaviour — the raven controller
    /// already polls every frame, so the voice is advanced from there,
    /// exactly as the village soundscape advances its own cursors.
    ///
    /// The voice rides the actor's host so the takeoff caw flies with
    /// the bird, and each bird schedules independently: a pair calling
    /// in lockstep would be one bird recorded twice. The perched
    /// schedule first shipped sparse — 26 to 70 seconds, in deference
    /// to the yard's silence — and the playtest verdict was that the
    /// birds seem mute, so it now runs 16 to 44, a shade denser than
    /// the dog's 21 to 48: a walker who pauses at the fence hears at
    /// least one dry call, while two independent clocks still leave
    /// whole quiet minutes. Missed events are never made up: a due
    /// moment that passes silenced re-arms from NOW, the village's
    /// no-catch-up rule.
    /// </summary>
    public sealed class CemeteryRavenVoice
    {
        public const string VoiceObjectName = "Raven Voice";

        /// <summary>0.16 was tuned as "quieter than the dog's yard
        /// bark" and the playtest lost it entirely under the 2.8 kHz
        /// low-pass and the ambience bed. A caw is a short dry
        /// transient, not a loop, so it can sit above the dog's 0.145
        /// without crowding the yard: loud enough to register as an
        /// event, still a detail of the place.</summary>
        public const float Volume = 0.30f;

        public const float MinimumDistanceMeters = 0.9f;

        /// <summary>Two metres past the dog's 12: under the linear
        /// rolloff the old radius left the call inaudible one plot
        /// away. 14 m is still two houses, not a district — «не на
        /// весь квартал» — and the roost planners that bind their
        /// point-of-interest clearances to this const grow with it
        /// on purpose: a silence zone should match what can actually
        /// be heard.</summary>
        public const float AudibleRadiusMeters = 14f;

        public const float LowPassFrequencyHz = 2800f;

        /// <summary>See the class doc: retuned from 26–70 after the
        /// playtest verdict that the birds seem silent.</summary>
        public const float MinimumCallIntervalSeconds = 16f;
        public const float MaximumCallIntervalSeconds = 44f;

        private readonly GameObject voiceObject;
        private readonly AudioSource source;
        private readonly AudioClip[] clips;

        /// <summary>True when the clips were generated for this voice
        /// alone and die with it in <see cref="Dispose"/>; false when
        /// they are the shared cache's, playable by many voices at
        /// once, and only the cache's last lease may destroy them.
        /// </summary>
        private readonly bool ownsClips;

        private readonly int seed;
        private double elapsedSeconds;
        private double nextDueSeconds;
        private uint scheduledEventOrdinal;
        private uint takeoffEventOrdinal;

        private CemeteryRavenVoice(
            GameObject configuredVoiceObject,
            AudioSource configuredSource,
            AudioClip[] configuredClips,
            int configuredSeed,
            bool configuredOwnsClips)
        {
            voiceObject = configuredVoiceObject;
            source = configuredSource;
            clips = configuredClips;
            seed = configuredSeed;
            ownsClips = configuredOwnsClips;
            nextDueSeconds = NextInterval();
        }

        public AudioSource Source => source;

        /// <summary>
        /// Builds the voice object beside one raven host. Every knob
        /// is the village dog's (rolloff, doppler, priority, reverb
        /// bypass, the AmbienceDetails route) except the low-pass and
        /// volume, which are the caw's own; all three variant clips
        /// are pre-generated here so playback never allocates.
        /// </summary>
        public static CemeteryRavenVoice Create(
            Transform host,
            int seed)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            AudioSource source = BuildVoiceSource(host);
            var clips = new AudioClip[
                CemeteryRavenCallSynthesis.VariantCount];
            for (int variant = 0; variant < clips.Length; variant++)
            {
                clips[variant] =
                    CemeteryRavenCallSynthesis.CreateRuntimeClip(
                        variant);
            }

            source.clip = clips[0];
            return new CemeteryRavenVoice(
                source.gameObject,
                source,
                clips,
                seed,
                true);
        }

        /// <summary>
        /// The shared-clips overload for the outdoor roost pairs. The
        /// caw synthesis takes no per-voice entropy — every voice's
        /// three clips are byte-identical — so dozens of roost voices
        /// over one <see cref="RavenCallClipCache"/> lease play the
        /// SAME clip instances instead of burning ~146 KB each on
        /// copies; a voice's own character lives entirely in its
        /// seeded variant and interval schedules. This voice does NOT
        /// own the clips: <see cref="Dispose"/> leaves them alone,
        /// and the caller must dispose all of its voices BEFORE the
        /// lease that keeps the clips alive.
        /// </summary>
        public static CemeteryRavenVoice Create(
            Transform host,
            int seed,
            IReadOnlyList<AudioClip> sharedClips)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (sharedClips == null)
            {
                throw new ArgumentNullException(nameof(sharedClips));
            }

            // Fail at creation rather than in a later hash-selected
            // Play: the variant selector assumes exactly the
            // synthesis family's clip count.
            if (sharedClips.Count !=
                CemeteryRavenCallSynthesis.VariantCount)
            {
                throw new ArgumentException(
                    "Expected exactly " +
                    CemeteryRavenCallSynthesis.VariantCount +
                    " clips, one per caw variant.",
                    nameof(sharedClips));
            }

            AudioSource source = BuildVoiceSource(host);
            var clips = new AudioClip[
                CemeteryRavenCallSynthesis.VariantCount];
            for (int variant = 0; variant < clips.Length; variant++)
            {
                clips[variant] = sharedClips[variant];
            }

            source.clip = clips[0];
            return new CemeteryRavenVoice(
                source.gameObject,
                source,
                clips,
                seed,
                false);
        }

        /// <summary>
        /// The one place the source's knobs are set, shared by both
        /// overloads so an owned and a shared voice can never drift
        /// apart in anything the ear could catch.
        /// </summary>
        private static AudioSource BuildVoiceSource(Transform host)
        {
            var voiceObject = new GameObject(VoiceObjectName);
            voiceObject.transform.SetParent(host, false);

            AudioSource source =
                voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = MinimumDistanceMeters;
            source.maxDistance = AudibleRadiusMeters;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.priority = 174;
            source.bypassReverbZones = true;
            source.volume = Volume;
            GameAudioMixer.Route(
                source,
                GameAudioGroup.AmbienceDetails);

            AudioLowPassFilter filter =
                voiceObject.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = LowPassFrequencyHz;
            filter.lowpassResonanceQ = 1f;
            return source;
        }

        /// <summary>
        /// One step of the perched schedule. <paramref name="canCall"/>
        /// is false outside PerchedIdle and during any grave-work
        /// session — a caw timed over a burial act would be a comment,
        /// and these birds comment on nothing. A silenced due moment
        /// still re-arms, so no backlog ever bursts out afterwards,
        /// and even a single 300-second chunk fires at most once.
        /// </summary>
        public void Advance(float deltaSeconds, bool canCall)
        {
            if (float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds <= 0f)
            {
                return;
            }

            elapsedSeconds += deltaSeconds;
            if (elapsedSeconds < nextDueSeconds)
            {
                return;
            }

            if (canCall)
            {
                Play(SelectVariant(0x5C41u));
            }

            nextDueSeconds = elapsedSeconds + NextInterval();
        }

        /// <summary>
        /// The one startle cry: fired by the controller exactly once
        /// per flush, at that bird's own takeoff. It also pushes the
        /// perched schedule back a full interval — a bird does not
        /// land on its next idle call seconds after crying out.
        /// </summary>
        public void PlayTakeoffCaw()
        {
            takeoffEventOrdinal++;
            Play(SelectVariant(0x7AFEu));
            nextDueSeconds = elapsedSeconds + NextInterval();
        }

        /// <summary>
        /// Tears the voice down edit-safely. The runtime clips carry
        /// DontSave and are destroyed by hand, the village's own
        /// OnDestroy rule — Destroy in play mode, DestroyImmediate in
        /// edit mode, where deferred destruction never runs. Only
        /// clips this voice generated for itself are destroyed here:
        /// leased cache clips are other voices' sound too, and the
        /// cache's own last lease is what buries them.
        /// </summary>
        public void Dispose()
        {
            if (ownsClips)
            {
                for (int index = 0; index < clips.Length; index++)
                {
                    AudioClip clip = clips[index];
                    if (clip == null)
                    {
                        continue;
                    }

                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(clip);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(clip);
                    }

                    clips[index] = null;
                }
            }

            if (voiceObject != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(voiceObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(voiceObject);
                }
            }
        }

        private void Play(int variant)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.clip = clips[variant];
            source.Play();
        }

        private int SelectVariant(uint salt)
        {
            uint ordinal = salt == 0x7AFEu
                ? takeoffEventOrdinal
                : scheduledEventOrdinal;
            uint hash = Hash(unchecked(
                (uint)seed ^ salt ^ (ordinal * 0x01000193u)));
            return (int)(hash %
                         (uint)CemeteryRavenCallSynthesis
                             .VariantCount);
        }

        private double NextInterval()
        {
            scheduledEventOrdinal++;
            uint hash = Hash(unchecked(
                (uint)seed ^ 0x1E37u ^
                (scheduledEventOrdinal * 0x9E3779B1u)));
            double normalized = (hash & 0x00FFFFFFu) / 16777215d;
            return MinimumCallIntervalSeconds +
                   normalized *
                   (MaximumCallIntervalSeconds -
                    MinimumCallIntervalSeconds);
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}
