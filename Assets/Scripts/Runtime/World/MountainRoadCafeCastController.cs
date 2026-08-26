using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadCafeCastEpisode
    {
        None = 0,
        LonePatron = 1,
        Couple = 2,
        Attendant = 3
    }

    /// <summary>
    /// Schedules sparse, deterministic movement across the cafe tableau.
    /// Only one episode runs at once; the two halves of the couple are one
    /// synchronized episode rather than two competing ambient gestures.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeCastController : MonoBehaviour
    {
        public const float MinimumEpisodeDelaySeconds = 18f;
        public const float MaximumEpisodeDelaySeconds = 32f;
        public const float MinimumEpisodeCooldownSeconds = 35f;
        public const float MaximumEpisodeCooldownSeconds = 55f;
        public const float MaximumSchedulerStepSeconds = 0.25f;

        private readonly float[] cooldownUntilSeconds = new float[3];
        private System.Random random;
        private MountainRoadCafeCastPresentation lonePatron;
        private MountainRoadCafeCastPresentation pairMan;
        private MountainRoadCafeCastPresentation pairWoman;
        private MountainRoadCafeCastPresentation attendant;
        private float elapsedSeconds;
        private float nextEpisodeSeconds;

        public bool IsInitialized { get; private set; }
        public MountainRoadCafeCastEpisode ActiveEpisode { get; private set; }
        public float ElapsedSeconds => elapsedSeconds;
        public float NextEpisodeSeconds => nextEpisodeSeconds;

        public void Initialize(
            IReadOnlyList<MountainRoadCafeCastPresentation> presentations,
            int seed)
        {
            if (presentations == null)
            {
                throw new ArgumentNullException(nameof(presentations));
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The cafe cast controller is already initialized.");
            }

            for (int index = 0; index < presentations.Count; index++)
            {
                MountainRoadCafeCastPresentation presentation =
                    presentations[index];
                if (presentation == null || !presentation.IsInitialized)
                {
                    throw new InvalidOperationException(
                        "Every cafe cast presentation must be initialized.");
                }

                AssignRole(presentation);
            }

            if (presentations.Count !=
                    MountainRoadCafeWorldBuilder.TableauNpcCount ||
                lonePatron == null ||
                pairMan == null ||
                pairWoman == null ||
                attendant == null)
            {
                throw new InvalidOperationException(
                    "The cafe requires one member in each of its four roles.");
            }

            random = new System.Random(seed);
            elapsedSeconds = 0f;
            ActiveEpisode = MountainRoadCafeCastEpisode.None;
            nextEpisodeSeconds = NextDelay(
                MinimumEpisodeDelaySeconds,
                MaximumEpisodeDelaySeconds);
            IsInitialized = true;
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            elapsedSeconds += Mathf.Min(
                Time.deltaTime,
                MaximumSchedulerStepSeconds);
            if (ActiveEpisode != MountainRoadCafeCastEpisode.None)
            {
                if (IsActiveEpisodePlaying())
                {
                    return;
                }

                int completedIndex = EpisodeIndex(ActiveEpisode);
                cooldownUntilSeconds[completedIndex] =
                    elapsedSeconds + NextDelay(
                        MinimumEpisodeCooldownSeconds,
                        MaximumEpisodeCooldownSeconds);
                ActiveEpisode = MountainRoadCafeCastEpisode.None;
                nextEpisodeSeconds = elapsedSeconds + NextDelay(
                    MinimumEpisodeDelaySeconds,
                    MaximumEpisodeDelaySeconds);
                return;
            }

            if (elapsedSeconds < nextEpisodeSeconds)
            {
                return;
            }

            MountainRoadCafeCastEpisode episode = ChooseEligibleEpisode();
            if (episode == MountainRoadCafeCastEpisode.None)
            {
                nextEpisodeSeconds = EarliestCooldown();
                return;
            }

            if (TryStartEpisode(episode))
            {
                ActiveEpisode = episode;
                return;
            }

            nextEpisodeSeconds = elapsedSeconds + 1f;
        }

        /// <summary>
        /// One episode out of turn, for a reason rather than a timer: the
        /// hero has just sat down at the counter and the man behind it
        /// notices. It still obeys the one rule the tableau has - never
        /// two at once - and it still books the ordinary cooldown after
        /// itself, so being noticed costs the room its next idle beat
        /// instead of adding a beat on top of it.
        /// </summary>
        public bool TryRequestEpisode(MountainRoadCafeCastEpisode episode)
        {
            if (!IsInitialized ||
                episode == MountainRoadCafeCastEpisode.None ||
                ActiveEpisode != MountainRoadCafeCastEpisode.None)
            {
                return false;
            }

            if (!TryStartEpisode(episode))
            {
                return false;
            }

            ActiveEpisode = episode;
            return true;
        }

        private void AssignRole(
            MountainRoadCafeCastPresentation presentation)
        {
            switch (presentation.Role)
            {
                case MountainRoadCafeCastRole.LonePatron:
                    lonePatron = AssignOnce(lonePatron, presentation);
                    break;
                case MountainRoadCafeCastRole.PairMan:
                    pairMan = AssignOnce(pairMan, presentation);
                    break;
                case MountainRoadCafeCastRole.PairWoman:
                    pairWoman = AssignOnce(pairWoman, presentation);
                    break;
                case MountainRoadCafeCastRole.Attendant:
                    attendant = AssignOnce(attendant, presentation);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown cafe cast role.");
            }
        }

        private static MountainRoadCafeCastPresentation AssignOnce(
            MountainRoadCafeCastPresentation current,
            MountainRoadCafeCastPresentation value)
        {
            if (current != null)
            {
                throw new InvalidOperationException(
                    "A cafe cast role was assigned twice.");
            }

            return value;
        }

        private MountainRoadCafeCastEpisode ChooseEligibleEpisode()
        {
            var eligible = new MountainRoadCafeCastEpisode[3];
            int count = 0;
            for (int index = 0; index < cooldownUntilSeconds.Length; index++)
            {
                if (elapsedSeconds >= cooldownUntilSeconds[index])
                {
                    eligible[count++] = EpisodeFromIndex(index);
                }
            }

            return count > 0
                ? eligible[random.Next(0, count)]
                : MountainRoadCafeCastEpisode.None;
        }

        private bool TryStartEpisode(MountainRoadCafeCastEpisode episode)
        {
            switch (episode)
            {
                case MountainRoadCafeCastEpisode.LonePatron:
                    return lonePatron.CanBeginBeat &&
                           lonePatron.TryBeginBeat();
                case MountainRoadCafeCastEpisode.Couple:
                    if (!pairMan.CanBeginBeat ||
                        !pairWoman.CanBeginBeat)
                    {
                        return false;
                    }

                    return pairMan.TryBeginBeat() &&
                           pairWoman.TryBeginBeat();
                case MountainRoadCafeCastEpisode.Attendant:
                    return attendant.CanBeginBeat &&
                           attendant.TryBeginBeat();
                default:
                    return false;
            }
        }

        private bool IsActiveEpisodePlaying()
        {
            switch (ActiveEpisode)
            {
                case MountainRoadCafeCastEpisode.LonePatron:
                    return lonePatron.IsBeatPlaying;
                case MountainRoadCafeCastEpisode.Couple:
                    return pairMan.IsBeatPlaying ||
                           pairWoman.IsBeatPlaying;
                case MountainRoadCafeCastEpisode.Attendant:
                    return attendant.IsBeatPlaying;
                default:
                    return false;
            }
        }

        private float EarliestCooldown()
        {
            float earliest = float.PositiveInfinity;
            for (int index = 0; index < cooldownUntilSeconds.Length; index++)
            {
                earliest = Mathf.Min(
                    earliest,
                    cooldownUntilSeconds[index]);
            }

            return Mathf.Max(elapsedSeconds + 0.25f, earliest);
        }

        private float NextDelay(float minimum, float maximum)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                (float)random.NextDouble());
        }

        private static int EpisodeIndex(
            MountainRoadCafeCastEpisode episode)
        {
            return (int)episode - 1;
        }

        private static MountainRoadCafeCastEpisode EpisodeFromIndex(
            int index)
        {
            return (MountainRoadCafeCastEpisode)(index + 1);
        }
    }
}
