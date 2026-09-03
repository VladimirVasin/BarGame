using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadCafeCastEpisode
    {
        None = 0,
        Couple = 2,
        Attendant = 3
    }

    /// <summary>
    /// Applies one pure service clock to all four authored figures and the
    /// optional environment-owned cup presentation. The lone patron owns one
    /// separately requested interjection and never enters the service clock;
    /// each member of the pair derives a stable role-specific drink window
    /// from that clock. No animation events, root motion or NPC audio
    /// participate in scheduling.
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
        public const float ActivationRadius = 16f;
        public const bool ServesHero = false;
        public const bool OffersHeroMenu = true;
        private const string LeftCupSocketName = "SOCKET_Vessel.L";

        // The shared Hero/NPC V2 rig exposes its cup-shaped right-hand
        // vertical grip as Bottle.R. It is the mirrored counterpart of
        // Vessel.L and follows the right-hand cafe drink animation.
        private const string RightCupSocketName = "SOCKET_Bottle.R";
        private const string MouthSocketName = "SOCKET_Mouth";

        private MountainRoadCafeCastPresentation lonePatron;
        private MountainRoadCafeCastPresentation pairMan;
        private MountainRoadCafeCastPresentation pairWoman;
        private MountainRoadCafeCastPresentation attendant;
        private MountainRoadCafeServicePresentation servicePresentation;
        private MountainRoadCafeServiceTimeline timeline;
        private Transform activationObserver;
        private Vector3 activationPoint;
        private float activationRadiusSquared;
        private float elapsedSeconds;
        private bool isPairConversationReserved;
        private bool isLonePatronInterjecting;
        private float lonePatronInterjectionElapsedSeconds;

        public bool IsInitialized { get; private set; }
        public bool IsTimelineArmed { get; private set; }
        public bool IsPairConversationReserved =>
            isPairConversationReserved;
        public bool IsLonePatronInterjecting =>
            isLonePatronInterjecting;
        public float LonePatronInterjectionElapsedSeconds =>
            lonePatronInterjectionElapsedSeconds;
        public float LonePatronInterjectionDurationSeconds =>
            lonePatron?.Registry.BeatClip != null
                ? lonePatron.Registry.BeatClip.length
                : 0f;
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
        public Transform AttendantMenuHandSocket => attendant?.Registry
            ?.FindModelTransform(RightCupSocketName);

        public Transform GetCupHandSocket(MountainRoadCafeCastRole role)
        {
            switch (role)
            {
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

        public Transform GetMouthSocket(MountainRoadCafeCastRole role)
        {
            MountainRoadCafeCastPresentation presentation =
                GetPresentation(role);
            return presentation?.Registry.FindModelTransform(
                MouthSocketName);
        }

        public Transform GetPresentationRoot(
            MountainRoadCafeCastRole role)
        {
            return GetPresentation(role)?.transform;
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
            activationObserver = null;
            activationPoint = Vector3.zero;
            activationRadiusSquared = ActivationRadius * ActivationRadius;
            elapsedSeconds = 0f;
            IsTimelineArmed = false;
            isPairConversationReserved = false;
            isLonePatronInterjecting = false;
            lonePatronInterjectionElapsedSeconds = 0f;
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
                    GetCupHandSocket(MountainRoadCafeCastRole.PairMan),
                    GetMouthSocket(MountainRoadCafeCastRole.PairMan),
                    GetPresentationRoot(MountainRoadCafeCastRole.PairMan),
                    GetCupHandSocket(MountainRoadCafeCastRole.PairWoman),
                    GetMouthSocket(MountainRoadCafeCastRole.PairWoman),
                    GetPresentationRoot(
                        MountainRoadCafeCastRole.PairWoman)))
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
        /// Delays the autonomous tableau until the hero reaches the terminal.
        /// Mountain Road is long enough that advancing from scene load would
        /// otherwise spend the first drink/refill cycles hundreds of metres
        /// offscreen.
        /// </summary>
        public bool BindActivationObserver(
            Transform observer,
            Vector3 configuredActivationPoint,
            float activationRadius = ActivationRadius)
        {
            if (!IsInitialized || observer == null ||
                activationObserver != null ||
                float.IsNaN(activationRadius) ||
                float.IsInfinity(activationRadius) ||
                activationRadius <= 0f)
            {
                return false;
            }

            activationObserver = observer;
            activationPoint = configuredActivationPoint;
            activationRadiusSquared = activationRadius * activationRadius;
            TryArmFromObserver();
            return true;
        }

        /// <summary>
        /// Explicit hero-exclusion hook used by the seat interaction. The
        /// request plays Notice only; the pure timeline cannot assign the
        /// hero a service target or alter either patron cup.
        /// </summary>
        public bool TryRequestHeroNotice()
        {
            if (!IsInitialized || !timeline.TryRequestHeroNotice())
            {
                return false;
            }

            IsTimelineArmed = true;
            ApplyFrame(timeline.Frame);
            return true;
        }

        /// <summary>
        /// Requests the accepted one-shot physical menu handoff. The pure
        /// service clock queues it behind any pour already in progress and
        /// never assigns the hero a cup target.
        /// </summary>
        public bool TryRequestHeroMenu()
        {
            if (!IsInitialized || !timeline.TryRequestHeroMenu())
            {
                return false;
            }

            IsTimelineArmed = true;
            ApplyFrame(timeline.Frame);
            return true;
        }

        /// <summary>
        /// Queues the attendant to collect the already placed physical menu.
        /// It shares the service clock with the pair refills, so no second
        /// attendant action can overlap it.
        /// </summary>
        public bool TryRequestHeroMenuRetrieval()
        {
            if (!IsInitialized ||
                !timeline.TryRequestHeroMenuRetrieval())
            {
                return false;
            }

            IsTimelineArmed = true;
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
                case MountainRoadCafeCastEpisode.Couple:
                    accepted = !isPairConversationReserved &&
                               timeline.TryRequestDrink(
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
                IsTimelineArmed = true;
                ApplyFrame(timeline.Frame);
            }

            return accepted;
        }

        /// <summary>
        /// Reserves the two Idle poses for one complete spoken beat. A
        /// reservation cannot begin during CoupleDrink; once accepted it
        /// holds the autonomous Wiping clock before the next drink while the
        /// attendant and every already-started service phase keep moving.
        /// </summary>
        public bool TryReservePairConversation()
        {
            if (!IsInitialized ||
                isPairConversationReserved ||
                timeline.Frame.Phase ==
                    MountainRoadCafeServicePhase.CoupleDrink)
            {
                return false;
            }

            isPairConversationReserved = true;
            return true;
        }

        public bool ReleasePairConversation()
        {
            if (!isPairConversationReserved)
            {
                return false;
            }

            isPairConversationReserved = false;
            return true;
        }

        /// <summary>
        /// Starts the sleeping husband's authored one-shot while the pair's
        /// conversation reservation still owns both Idle poses. The caller
        /// advances this separate clock so the spoken line and gesture share
        /// one deterministic schedule.
        /// </summary>
        public bool TryBeginLonePatronInterjection()
        {
            if (!IsInitialized ||
                !isPairConversationReserved ||
                isLonePatronInterjecting ||
                !lonePatron.CanBeginBeat ||
                lonePatron.Registry.BeatClip == null)
            {
                return false;
            }

            isLonePatronInterjecting = true;
            lonePatronInterjectionElapsedSeconds = 0f;
            ApplyFrame(timeline.Frame);
            return true;
        }

        public bool AdvanceLonePatronInterjection(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds),
                    "Cafe interjection time must be finite and non-negative.");
            }

            if (!IsInitialized || !isLonePatronInterjecting)
            {
                return false;
            }

            float duration = LonePatronInterjectionDurationSeconds;
            if (duration <= 0f)
            {
                CancelLonePatronInterjection();
                return false;
            }

            lonePatronInterjectionElapsedSeconds = Mathf.Min(
                lonePatronInterjectionElapsedSeconds + deltaSeconds,
                duration);
            lonePatron.ApplyClip(
                MountainRoadCafeCastClipKind.Interject,
                lonePatronInterjectionElapsedSeconds);
            if (lonePatronInterjectionElapsedSeconds < duration)
            {
                return true;
            }

            isLonePatronInterjecting = false;
            lonePatron.ApplyClip(
                MountainRoadCafeCastClipKind.Idle,
                elapsedSeconds);
            return false;
        }

        public bool CancelLonePatronInterjection()
        {
            if (!IsInitialized)
            {
                return false;
            }

            bool wasInterjecting = isLonePatronInterjecting;
            isLonePatronInterjecting = false;
            lonePatronInterjectionElapsedSeconds = 0f;
            lonePatron.ApplyClip(
                MountainRoadCafeCastClipKind.Idle,
                elapsedSeconds);
            return wasInterjecting;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaSeconds)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds),
                    "Cafe cast time must be finite and non-negative.");
            }

            if (!IsTimelineArmed && !TryArmFromObserver())
            {
                return;
            }

            elapsedSeconds += deltaSeconds;
            AdvanceServiceTimeline(deltaSeconds);
            ApplyFrame(timeline.Frame);
        }

        private void AdvanceServiceTimeline(float deltaSeconds)
        {
            if (!isPairConversationReserved)
            {
                timeline.Advance(deltaSeconds);
                return;
            }

            // While the pair speak, an already-started attendant action is
            // allowed to finish. Once it reaches Wiping, no part of the
            // remaining hitch may cross that phase into CoupleDrink.
            float remaining = deltaSeconds;
            int transitions = 0;
            while (remaining > 0f &&
                   timeline.Frame.Phase !=
                       MountainRoadCafeServicePhase.Wiping)
            {
                float step = Mathf.Min(
                    remaining,
                    timeline.RemainingPhaseSeconds);
                if (step <= 0f)
                {
                    break;
                }

                timeline.Advance(step);
                remaining -= step;
                transitions++;
                if (transitions > 16)
                {
                    throw new InvalidOperationException(
                        "Cafe service crossed too many phases while the " +
                        "pair conversation held its drink gate.");
                }
            }
        }

        private bool TryArmFromObserver()
        {
            if (IsTimelineArmed)
            {
                return true;
            }

            if (activationObserver == null)
            {
                return false;
            }

            Vector3 offset = activationObserver.position - activationPoint;
            offset.y = 0f;
            if (offset.sqrMagnitude > activationRadiusSquared)
            {
                return false;
            }

            IsTimelineArmed = true;
            ApplyFrame(timeline.Frame);
            return true;
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
            isPairConversationReserved = false;
            isLonePatronInterjecting = false;
            lonePatronInterjectionElapsedSeconds = 0f;
        }

        private void ApplyFrame(MountainRoadCafeServiceFrame frame)
        {
            lonePatron.ApplyClip(
                isLonePatronInterjecting
                    ? MountainRoadCafeCastClipKind.Interject
                    : MountainRoadCafeCastClipKind.Idle,
                isLonePatronInterjecting
                    ? lonePatronInterjectionElapsedSeconds
                    : elapsedSeconds);

            bool pairManDrinks = frame.IsDrinking(
                MountainRoadCafeCastRole.PairMan);
            pairMan.ApplyClip(
                pairManDrinks
                    ? MountainRoadCafeCastClipKind.Drink
                    : MountainRoadCafeCastClipKind.Idle,
                pairManDrinks
                    ? frame.GetDrinkElapsedSeconds(
                        MountainRoadCafeCastRole.PairMan)
                    : elapsedSeconds);
            bool pairWomanDrinks = frame.IsDrinking(
                MountainRoadCafeCastRole.PairWoman);
            pairWoman.ApplyClip(
                pairWomanDrinks
                    ? MountainRoadCafeCastClipKind.Drink
                    : MountainRoadCafeCastClipKind.Idle,
                pairWomanDrinks
                    ? frame.GetDrinkElapsedSeconds(
                        MountainRoadCafeCastRole.PairWoman)
                    : elapsedSeconds);

            MountainRoadCafeCastClipKind attendantClip;
            switch (frame.Phase)
            {
                case MountainRoadCafeServicePhase.Notice:
                case MountainRoadCafeServicePhase.MenuNotice:
                    attendantClip = MountainRoadCafeCastClipKind.Notice;
                    break;
                case MountainRoadCafeServicePhase.WalkToCup:
                case MountainRoadCafeServicePhase.WalkBack:
                case MountainRoadCafeServicePhase.WalkToHero:
                case MountainRoadCafeServicePhase.PlaceMenu:
                case MountainRoadCafeServicePhase.MenuWalkBack:
                case MountainRoadCafeServicePhase.WalkToMenu:
                case MountainRoadCafeServicePhase.TakeMenu:
                case MountainRoadCafeServicePhase.CarryMenuBack:
                    attendantClip = MountainRoadCafeCastClipKind.Walk;
                    break;
                case MountainRoadCafeServicePhase.Pour:
                    attendantClip = MountainRoadCafeCastClipKind.Pour;
                    break;
                default:
                    attendantClip = MountainRoadCafeCastClipKind.Wipe;
                    break;
            }

            if (frame.Phase != MountainRoadCafeServicePhase.PlaceMenu &&
                frame.Phase != MountainRoadCafeServicePhase.TakeMenu)
            {
                attendant.ApplyClip(
                    attendantClip,
                    attendantClip == MountainRoadCafeCastClipKind.Wipe
                        ? elapsedSeconds
                        : frame.PhaseElapsedSeconds,
                    frame.Phase ==
                        MountainRoadCafeServicePhase.CarryMenuBack);
            }
            // Keep the final Walk pose while the booklet moves between hand
            // and counter in either direction. Resampling another clip here
            // would snap the hand away from the prop at the contact edge.
            if (IsMenuBookletPhase(frame.Phase))
            {
                // Walk normally exposes the baked coffee pot. During the
                // menu handoff the same measured step cycle is reused with
                // the right hand carrying the booklet instead.
                attendant.Registry.SetCoffeePotVisible(false);
            }
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

        private MountainRoadCafeCastPresentation GetPresentation(
            MountainRoadCafeCastRole role)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.LonePatron:
                    return lonePatron;
                case MountainRoadCafeCastRole.PairMan:
                    return pairMan;
                case MountainRoadCafeCastRole.PairWoman:
                    return pairWoman;
                case MountainRoadCafeCastRole.Attendant:
                    return attendant;
                default:
                    return null;
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
                case MountainRoadCafeServicePhase.CoupleDrink:
                    return MountainRoadCafeCastEpisode.Couple;
                case MountainRoadCafeServicePhase.Notice:
                case MountainRoadCafeServicePhase.WalkToCup:
                case MountainRoadCafeServicePhase.Pour:
                case MountainRoadCafeServicePhase.WalkBack:
                case MountainRoadCafeServicePhase.MenuNotice:
                case MountainRoadCafeServicePhase.WalkToHero:
                case MountainRoadCafeServicePhase.PlaceMenu:
                case MountainRoadCafeServicePhase.MenuWalkBack:
                case MountainRoadCafeServicePhase.WalkToMenu:
                case MountainRoadCafeServicePhase.TakeMenu:
                case MountainRoadCafeServicePhase.CarryMenuBack:
                    return MountainRoadCafeCastEpisode.Attendant;
                default:
                    return MountainRoadCafeCastEpisode.None;
            }
        }

        private static bool IsMenuBookletPhase(
            MountainRoadCafeServicePhase phase)
        {
            return phase == MountainRoadCafeServicePhase.MenuNotice ||
                   phase == MountainRoadCafeServicePhase.WalkToHero ||
                   phase == MountainRoadCafeServicePhase.PlaceMenu ||
                   phase == MountainRoadCafeServicePhase.MenuWalkBack ||
                   phase == MountainRoadCafeServicePhase.WalkToMenu ||
                   phase == MountainRoadCafeServicePhase.TakeMenu ||
                   phase == MountainRoadCafeServicePhase.CarryMenuBack;
        }
    }
}
