using System;
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
    /// schedule is sparse — 26 to 70 seconds against the dog's 21 to
    /// 48 — because the cemetery is otherwise deliberately silent and
    /// this call is the first sound the yard has ever owned. Missed
    /// events are never made up: a due moment that passes silenced
    /// re-arms from NOW, the village's no-catch-up rule.
    /// </summary>
    public sealed class CemeteryRavenVoice
    {
        public const string VoiceObjectName = "Raven Voice";

        /// <summary>Quieter than the dog's yard bark: a dry corvid
        /// call across gravestones, not an alert.</summary>
        public const float Volume = 0.16f;

        public const float MinimumDistanceMeters = 0.9f;

        /// <summary>The dog's audible radius, kept: a caw should be a
        /// detail of the cemetery, not of the district.</summary>
        public const float AudibleRadiusMeters = 12f;

        public const float LowPassFrequencyHz = 2800f;

        public const float MinimumCallIntervalSeconds = 26f;
        public const float MaximumCallIntervalSeconds = 70f;

        private readonly GameObject voiceObject;
        private readonly AudioSource source;
        private readonly AudioClip[] clips;
        private readonly int seed;
        private double elapsedSeconds;
        private double nextDueSeconds;
        private uint scheduledEventOrdinal;
        private uint takeoffEventOrdinal;

        private CemeteryRavenVoice(
            GameObject configuredVoiceObject,
            AudioSource configuredSource,
            AudioClip[] configuredClips,
            int configuredSeed)
        {
            voiceObject = configuredVoiceObject;
            source = configuredSource;
            clips = configuredClips;
            seed = configuredSeed;
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
                voiceObject,
                source,
                clips,
                seed);
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
        /// edit mode, where deferred destruction never runs.
        /// </summary>
        public void Dispose()
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
