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
    /// Applies one pure service clock to all four authored figures and the
    /// optional environment-owned cup presentation. No animation events,
    /// root motion or NPC audio participate in synchronization.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeCastController : MonoBehaviour
    {
        public const float MinimumEpisodeDelaySeconds =
            MountainRoadCafeServiceTimeline.MinimumWipeSeconds;
        public const float MaximumEpisodeDelaySeconds =
            MountainRoadCafeServiceTimeline.MaximumWipeSeconds;
        public const float MinimumEpisodeCooldownSeconds = 35f;
        public const float MaximumEpisodeCooldownSeconds = 55f;
        public const float MaximumSchedulerStepSeconds = 0.25f;
        public const bool ServesHero = false;
        private const string LeftCupSocketName = "SOCKET_Vessel.L";

        // The shared Hero/NPC V2 rig exposes its cup-shaped right-hand
        // vertical grip as Bottle.R. It is the mirrored counterpart of
        // Vessel.L and follows the right-hand cafe drink animation.
        private const string RightCupSocketName = "SOCKET_Bottle.R";

        private MountainRoadCafeCastPresentation lonePatron;
        private MountainRoadCafeCastPresentation pairMan;
        private MountainRoadCafeCastPresentation pairWoman;
        private MountainRoadCafeCastPresentation attendant;
        private MountainRoadCafeServicePresentation servicePresentation;
        private MountainRoadCafeServiceTimeline timeline;
        private float elapsedSeconds;

        public bool IsInitialized { get; private set; }
        public MountainRoadCafeCastEpisode ActiveEpisode =>
            ResolveEpisode(timeline?.Frame.Phase ??
                           MountainRoadCafeServicePhase.Wiping);
        public float ElapsedSeconds => elapsedSeconds;
        public float NextEpisodeSeconds => timeline != null &&
                                           timeline.Frame.Phase ==
                                           MountainRoadCafeServicePhase.Wiping
            ? elapsedSeconds + timeline.RemainingPhaseSeconds
            : elapsedSeconds;
        public MountainRoadCafeServiceFrame ServiceFrame => timeline != null
            ? timeline.Frame
            : default;
        public MountainRoadCafeServicePresentation ServicePresentation =>
            servicePresentation;
        public Transform AttendantPourSpout => attendant?.Registry
            ?.FindModelTransform("SOCKET_CafePotSpout");
        public Transform AttendantMotionRoot => attendant != null
            ? attendant.transform
            : null;

        public Transform GetCupHandSocket(MountainRoadCafeCastRole role)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.LonePatron:
                    return lonePatron?.Registry.FindModelTransform(
                        RightCupSocketName);
                case MountainRoadCafeCastRole.PairMan:
                    return pairMan?.Registry.FindModelTransform(
                        RightCupSocketName);
                case MountainRoadCafeCastRole.PairWoman:
                    return pairWoman?.Registry.FindModelTransform(
                        LeftCupSocketName);
                default:
                    return null;
            }
        }

        public void Initialize(
            IReadOnlyList<MountainRoadCafeCastPresentation> presentations,
            int seed)
        {
            Initialize(presentations, seed, null);
        }

        public void Initialize(
            IReadOnlyList<MountainRoadCafeCastPresentation> presentations,
            int seed,
            MountainRoadCafeServicePresentation configuredServicePresentation)
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

            timeline = new MountainRoadCafeServiceTimeline(seed);
            elapsedSeconds = 0f;
            IsInitialized = true;
            if (configuredServicePresentation != null &&
                !BindServicePresentation(configuredServicePresentation))
            {
                throw new InvalidOperationException(
                    "The supplied cafe service presentation is invalid.");
            }

            ApplyFrame(timeline.Frame);
        }

        public bool BindServicePresentation(
            MountainRoadCafeServicePresentation presentation)
        {
            if (!IsInitialized ||
                presentation == null ||
                !presentation.IsConfigured ||
                presentation.IncludesHeroCup ||
                (servicePresentation != null &&
                 servicePresentation != presentation))
            {
                return false;
            }

            servicePresentation = presentation;
            if (!servicePresentation.BindDrinkSockets(
                    GetCupHandSocket(
                        MountainRoadCafeCastRole.LonePatron),
                    GetCupHandSocket(MountainRoadCafeCastRole.PairMan),
                    GetCupHandSocket(MountainRoadCafeCastRole.PairWoman)))
            {
                servicePresentation = null;
                return false;
            }

            servicePresentation.SetFrame(timeline.Frame);
            return true;
        }

        public bool TryGetCup(
            MountainRoadCafeCastRole role,
            out MountainRoadCafeCupView cup)
        {
            if (servicePresentation != null)
            {
                return servicePresentation.TryGetCup(role, out cup);
            }

            cup = null;
            return false;
        }

        /// <summary>
        /// Explicit hero-exclusion hook used by the seat interaction. The
        /// request plays Notice only; the pure timeline cannot assign the
        /// hero a service target or alter one of the three patron cups.
        /// </summary>
        public bool TryRequestHeroNotice()
        {
            if (!IsInitialized || !timeline.TryRequestHeroNotice())
            {
                return false;
            }

            ApplyFrame(timeline.Frame);
            return true;
        }

        public bool TryRequestEpisode(MountainRoadCafeCastEpisode episode)
        {
            if (!IsInitialized ||
                episode == MountainRoadCafeCastEpisode.None)
            {
                return false;
            }

            bool accepted;
            switch (episode)
            {
                case MountainRoadCafeCastEpisode.LonePatron:
                    accepted = timeline.TryRequestDrink(
                        MountainRoadCafeCastRole.LonePatron);
                    break;
                case MountainRoadCafeCastEpisode.Couple:
                    accepted = timeline.TryRequestDrink(
                        MountainRoadCafeCastRole.PairMan);
                    break;
                case MountainRoadCafeCastEpisode.Attendant:
                    accepted = timeline.TryRequestHeroNotice();
                    break;
                default:
                    accepted = false;
                    break;
            }

            if (accepted)
            {
                ApplyFrame(timeline.Frame);
            }

            return accepted;
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            float delta = Mathf.Max(0f, Time.deltaTime);
            elapsedSeconds += delta;
            timeline.Advance(delta);
            ApplyFrame(timeline.Frame);
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                ApplyFrame(timeline.Frame);
            }
        }

        private void OnDisable()
        {
            if (!IsInitialized)
            {
                return;
            }

            lonePatron.ApplyClip(
                MountainRoadCafeCastClipKind.Idle,
                0f);
            pairMan.ApplyClip(
                MountainRoadCafeCastClipKind.Idle,
                0f);
            pairWoman.ApplyClip(
                MountainRoadCafeCastClipKind.Idle,
                0f);
            attendant.ApplyClip(
                MountainRoadCafeCastClipKind.Wipe,
                0f);
            servicePresentation?.ResetExact();
        }

        private void ApplyFrame(MountainRoadCafeServiceFrame frame)
        {
            lonePatron.ApplyClip(
                frame.Phase == MountainRoadCafeServicePhase.LoneDrink
                    ? MountainRoadCafeCastClipKind.Drink
                    : MountainRoadCafeCastClipKind.Idle,
                frame.Phase == MountainRoadCafeServicePhase.LoneDrink
                    ? frame.PhaseElapsedSeconds
                    : 0f);

            bool coupleDrinks = frame.Phase ==
                                MountainRoadCafeServicePhase.CoupleDrink;
            float coupleClock = coupleDrinks
                ? frame.PhaseElapsedSeconds
                : 0f;
            pairMan.ApplyClip(
                coupleDrinks
                    ? MountainRoadCafeCastClipKind.Drink
                    : MountainRoadCafeCastClipKind.Idle,
                coupleClock);
            pairWoman.ApplyClip(
                coupleDrinks
                    ? MountainRoadCafeCastClipKind.Drink
                    : MountainRoadCafeCastClipKind.Idle,
                coupleClock);

            MountainRoadCafeCastClipKind attendantClip;
            switch (frame.Phase)
            {
                case MountainRoadCafeServicePhase.Notice:
                    attendantClip = MountainRoadCafeCastClipKind.Notice;
                    break;
                case MountainRoadCafeServicePhase.WalkToCup:
                case MountainRoadCafeServicePhase.WalkBack:
                    attendantClip = MountainRoadCafeCastClipKind.Walk;
                    break;
                case MountainRoadCafeServicePhase.Pour:
                    attendantClip = MountainRoadCafeCastClipKind.Pour;
                    break;
                default:
                    attendantClip = MountainRoadCafeCastClipKind.Wipe;
                    break;
            }

            attendant.ApplyClip(
                attendantClip,
                attendantClip == MountainRoadCafeCastClipKind.Wipe
                    ? elapsedSeconds
                    : frame.PhaseElapsedSeconds);
            servicePresentation?.SetFrame(frame);
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

        private static MountainRoadCafeCastEpisode ResolveEpisode(
            MountainRoadCafeServicePhase phase)
        {
            switch (phase)
            {
                case MountainRoadCafeServicePhase.LoneDrink:
                    return MountainRoadCafeCastEpisode.LonePatron;
                case MountainRoadCafeServicePhase.CoupleDrink:
                    return MountainRoadCafeCastEpisode.Couple;
                case MountainRoadCafeServicePhase.Notice:
                case MountainRoadCafeServicePhase.WalkToCup:
                case MountainRoadCafeServicePhase.Pour:
                case MountainRoadCafeServicePhase.WalkBack:
                    return MountainRoadCafeCastEpisode.Attendant;
                default:
                    return MountainRoadCafeCastEpisode.None;
            }
        }
    }
}
