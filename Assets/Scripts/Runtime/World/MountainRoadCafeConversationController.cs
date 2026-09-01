using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The private conversation between the two drinking patrons. It exists
    /// only while the hero is physically inside the cafe, preserves the ten
    /// authored statement/response pairs, and uses the same overhead bubble
    /// channel as the park chess-set quarrel. The active speaker turns to the
    /// other patron before the line appears and returns to Idle afterward.
    /// </summary>
    [DefaultExecutionOrder(330)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeConversationController : MonoBehaviour
    {
        public const string RuntimeObjectName = "Cafe Pair Conversation";

        // Her cigarette reaches the mouth at 0.26-0.36 and its plume is done
        // by 0.68. A line may start only after that and only when the complete
        // anticipation + visible interval still fits before the next 0.16
        // lift. This keeps the look from pulling her mouth away from the drag.
        public const float WomanRestStartNormalized = 0.68f;
        public const float WomanNextLiftNormalized = 0.16f;
        public const float WomanWindowSafetySeconds = 0.12f;

        private MountainRoadCafePlan plan;
        private Transform player;
        private NpcSpeechBubbleView bubbles;
        private MountainRoadCafeCastController cast;
        private MountainRoadCafeCastPresentation pairMan;
        private MountainRoadCafeCastPresentation pairWoman;
        private MountainRoadCafeConversationLook manLook;
        private MountainRoadCafeConversationLook womanLook;
        private MountainRoadCafeConversationTimeline timeline;
        private MountainRoadCafeConversationOrder lineOrder;

        private MountainRoadCafeConversationSpeaker pendingSpeaker;
        private MountainRoadCafeConversationSpeaker activeSpeaker;
        private bool hasPendingLine;
        private bool isPreparingLine;
        private bool hasActiveLine;
        private bool isReturningLine;
        private bool hasPairReservation;
        private bool isEngaged;
        private float preparationElapsedSeconds;
        private float activeLineElapsedSeconds;
        private float returnElapsedSeconds;

        public bool IsInitialized { get; private set; }
        public bool IsEngaged => isEngaged;
        public bool HasPendingLine => hasPendingLine || isPreparingLine;
        public bool HasActiveLine => hasActiveLine;
        public MountainRoadCafeConversationSpeaker ActiveSpeaker =>
            activeSpeaker;
        public MountainRoadCafeConversationSpeaker LastSpeaker
        {
            get;
            private set;
        }
        public string LastLineKey { get; private set; } = string.Empty;
        public NpcSpeechBubbleView Bubbles => bubbles;
        public MountainRoadCafeConversationTimeline Timeline => timeline;
        public MountainRoadCafeConversationLook ManLook => manLook;
        public MountainRoadCafeConversationLook WomanLook => womanLook;

        public static MountainRoadCafeConversationController Create(
            Transform parent,
            MountainRoadCafePlan cafePlan,
            MountainRoadCafeCastController cast,
            Transform playerTransform,
            Camera camera,
            int seed)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (cafePlan == null ||
                cast == null ||
                !cast.IsInitialized ||
                playerTransform == null ||
                camera == null)
            {
                return null;
            }

            Transform manRoot = cast.GetPresentationRoot(
                MountainRoadCafeCastRole.PairMan);
            Transform womanRoot = cast.GetPresentationRoot(
                MountainRoadCafeCastRole.PairWoman);
            MountainRoadCafeCastPresentation man = manRoot != null
                ? manRoot.GetComponent<MountainRoadCafeCastPresentation>()
                : null;
            MountainRoadCafeCastPresentation woman = womanRoot != null
                ? womanRoot.GetComponent<MountainRoadCafeCastPresentation>()
                : null;
            if (man == null || woman == null ||
                !man.IsInitialized || !woman.IsInitialized)
            {
                return null;
            }

            Transform manHead = man.Registry.FindModelTransform("head");
            Transform womanHead = woman.Registry.FindModelTransform("head");
            if (manHead == null || womanHead == null)
            {
                return null;
            }

            var host = new GameObject(RuntimeObjectName);
            host.transform.SetParent(parent, false);
            try
            {
                var bubbleView = host.AddComponent<NpcSpeechBubbleView>();
                bubbleView.Initialize(camera);
                MountainRoadCafeConversationLook configuredManLook =
                    GetOrAddLook(manRoot.gameObject);
                configuredManLook.Initialize(man, womanHead);
                MountainRoadCafeConversationLook configuredWomanLook =
                    GetOrAddLook(womanRoot.gameObject);
                configuredWomanLook.Initialize(woman, manHead);

                var controller = host.AddComponent<
                    MountainRoadCafeConversationController>();
                controller.plan = cafePlan;
                controller.player = playerTransform;
                controller.bubbles = bubbleView;
                controller.cast = cast;
                controller.pairMan = man;
                controller.pairWoman = woman;
                controller.manLook = configuredManLook;
                controller.womanLook = configuredWomanLook;
                controller.timeline =
                    new MountainRoadCafeConversationTimeline(seed);
                controller.lineOrder =
                    new MountainRoadCafeConversationOrder();
                controller.IsInitialized = true;
                return controller;
            }
            catch
            {
                DestroyObject(host);
                throw;
            }
        }

        /// <summary>
        /// True only when a complete turn-in and four-second line fit after
        /// the cigarette plume and before the next lift begins.
        /// </summary>
        public static bool CanBeginWomanLine(
            float normalizedIdlePhase,
            float idleClipLengthSeconds)
        {
            if (float.IsNaN(normalizedIdlePhase) ||
                float.IsInfinity(normalizedIdlePhase) ||
                float.IsNaN(idleClipLengthSeconds) ||
                float.IsInfinity(idleClipLengthSeconds) ||
                idleClipLengthSeconds <= 0f)
            {
                return false;
            }

            float phase = Mathf.Repeat(normalizedIdlePhase, 1f);
            if (phase < WomanRestStartNormalized)
            {
                return false;
            }

            float untilNextLift =
                (1f - phase + WomanNextLiftNormalized) *
                idleClipLengthSeconds;
            float needed = MountainRoadCafeConversationLook.TurnInSeconds +
                           NpcSpeechBubbleView.VisibleSeconds +
                           MountainRoadCafeConversationLook.TurnOutSeconds +
                           WomanWindowSafetySeconds;
            return untilNextLift >= needed;
        }

        /// <summary>
        /// Only authored Drink clips block speech. The man's tapping remains
        /// inside CafeManIdle, so it is deliberately allowed underneath a
        /// line rather than being treated as another action window.
        /// </summary>
        public static bool ArePairClipsAvailable(
            MountainRoadCafeCastClipKind manClip,
            MountainRoadCafeCastClipKind womanClip)
        {
            return manClip == MountainRoadCafeCastClipKind.Idle &&
                   womanClip == MountainRoadCafeCastClipKind.Idle;
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
                    nameof(deltaSeconds));
            }

            if (!plan.ContainsInterior(player.position, 0f))
            {
                Disengage();
                return;
            }

            if (!isEngaged)
            {
                isEngaged = true;
                timeline.Reset();
            }

            if (!PairIsIdle())
            {
                InterruptForAuthoredAction();
                AdvanceLineClock(deltaSeconds);
                return;
            }

            if (isReturningLine)
            {
                returnElapsedSeconds += deltaSeconds;
                if (returnElapsedSeconds >=
                    MountainRoadCafeConversationLook.TurnOutSeconds)
                {
                    isReturningLine = false;
                    returnElapsedSeconds = 0f;
                    ReleasePairReservation();
                }
                else
                {
                    return;
                }
            }

            if (isPreparingLine)
            {
                preparationElapsedSeconds += deltaSeconds;
                if (preparationElapsedSeconds >=
                    MountainRoadCafeConversationLook.TurnInSeconds)
                {
                    ShowPreparedLine();
                }
            }
            else if (hasPendingLine)
            {
                TryPreparePendingLine();
            }

            if (hasActiveLine)
            {
                activeLineElapsedSeconds += deltaSeconds;
                if (activeLineElapsedSeconds >=
                    NpcSpeechBubbleView.VisibleSeconds)
                {
                    FinishActiveLine();
                }
            }

            // A cue already waiting for a safe cigarette phase owns the
            // turn. Pausing the clock here preserves strict alternation and
            // avoids queuing another line behind it.
            if (hasPendingLine || isPreparingLine)
            {
                return;
            }

            AdvanceLineClock(deltaSeconds);
        }

        private void TryPreparePendingLine()
        {
            if (!hasPendingLine ||
                isPreparingLine ||
                hasActiveLine ||
                isReturningLine ||
                !PairIsIdle())
            {
                return;
            }

            // Nobody talks over her cigarette animation, regardless of who
            // owns the line. The whole turn-in, bubble and return must fit in
            // the settled interval before her next lift.
            if (!CanWomanBeginNow())
            {
                return;
            }

            // This succeeds only while neither cup is in CoupleDrink. Once
            // held, the cast lets any attendant action finish but cannot
            // cross Wiping into the pair's next Drink until return is done.
            if (!cast.TryReservePairConversation())
            {
                return;
            }

            hasPairReservation = true;
            LookOf(pendingSpeaker).SetSpeaking(true);
            LookOf(MountainRoadCafeConversationTimeline.Opposite(
                pendingSpeaker)).SetSpeaking(false);
            preparationElapsedSeconds = 0f;
            isPreparingLine = true;
            hasPendingLine = false;
        }

        private void AdvanceLineClock(float deltaSeconds)
        {
            // A controller-owned cue is already removed from the pure clock,
            // so it must also stop that clock until this exact turn begins.
            if (hasPendingLine || isPreparingLine)
            {
                return;
            }

            timeline.Advance(deltaSeconds);
            if (!timeline.ConsumeLineCue(
                    out MountainRoadCafeConversationSpeaker speaker))
            {
                return;
            }

            pendingSpeaker = speaker;
            hasPendingLine = true;
            TryPreparePendingLine();
        }

        private void ShowPreparedLine()
        {
            MountainRoadCafeConversationSpeaker speaker = pendingSpeaker;
            string key = lineOrder.ConsumeKey(speaker);

            MountainRoadCafeConversationSpeaker other =
                MountainRoadCafeConversationTimeline.Opposite(speaker);
            bubbles.Dismiss(OwnerOf(other));
            bubbles.Show(
                OwnerOf(speaker),
                LookOf(speaker).SpeechAnchor,
                LocalizationService.Get(key));

            activeSpeaker = speaker;
            activeLineElapsedSeconds = 0f;
            hasActiveLine = true;
            isPreparingLine = false;
            LastSpeaker = speaker;
            LastLineKey = key;
        }

        private void FinishActiveLine()
        {
            if (!hasActiveLine)
            {
                return;
            }

            bubbles.Dismiss(OwnerOf(activeSpeaker));
            LookOf(activeSpeaker).SetSpeaking(false);
            activeLineElapsedSeconds = 0f;
            hasActiveLine = false;
            returnElapsedSeconds = 0f;
            isReturningLine = true;
        }

        private void InterruptForAuthoredAction()
        {
            if (isPreparingLine)
            {
                // The cue remains pending and gets another safe attempt after
                // the cups are back down; the speaker turn is not skipped.
                hasPendingLine = true;
                isPreparingLine = false;
                preparationElapsedSeconds = 0f;
            }

            if (hasActiveLine)
            {
                RollBackShownLine(activeSpeaker);
                pendingSpeaker = activeSpeaker;
                hasPendingLine = true;
                bubbles.Dismiss(OwnerOf(activeSpeaker));
                hasActiveLine = false;
                activeLineElapsedSeconds = 0f;
            }

            manLook.CancelImmediately();
            womanLook.CancelImmediately();
            isReturningLine = false;
            returnElapsedSeconds = 0f;
            ReleasePairReservation();
        }

        private bool PairIsIdle()
        {
            return ArePairClipsAvailable(
                pairMan.CurrentClipKind,
                pairWoman.CurrentClipKind);
        }

        private bool CanWomanBeginNow()
        {
            AnimationClip idle = pairWoman.Registry.IdleClip;
            return idle != null && CanBeginWomanLine(
                pairWoman.DefaultClipNormalizedTime,
                idle.length);
        }

        private MountainRoadCafeConversationLook LookOf(
            MountainRoadCafeConversationSpeaker speaker)
        {
            return speaker == MountainRoadCafeConversationSpeaker.PairMan
                ? manLook
                : womanLook;
        }

        private UnityEngine.Object OwnerOf(
            MountainRoadCafeConversationSpeaker speaker)
        {
            return speaker == MountainRoadCafeConversationSpeaker.PairMan
                ? (UnityEngine.Object)pairMan
                : pairWoman;
        }

        private void Disengage()
        {
            if (!isEngaged)
            {
                return;
            }

            isEngaged = false;
            hasPendingLine = false;
            isPreparingLine = false;
            hasActiveLine = false;
            isReturningLine = false;
            preparationElapsedSeconds = 0f;
            activeLineElapsedSeconds = 0f;
            returnElapsedSeconds = 0f;
            lineOrder.Reset();
            timeline.Reset();
            bubbles.DismissAll();
            manLook.CancelImmediately();
            womanLook.CancelImmediately();
            ReleasePairReservation();
        }

        private void OnDisable()
        {
            if (!IsInitialized)
            {
                return;
            }

            bubbles.DismissAll();
            manLook.CancelImmediately();
            womanLook.CancelImmediately();
            ReleasePairReservation();
            isEngaged = false;
        }

        private void RollBackShownLine(
            MountainRoadCafeConversationSpeaker speaker)
        {
            lineOrder.UndoLast(speaker);
        }

        private void ReleasePairReservation()
        {
            if (!hasPairReservation)
            {
                return;
            }

            cast.ReleasePairConversation();
            hasPairReservation = false;
        }

        private static MountainRoadCafeConversationLook GetOrAddLook(
            GameObject target)
        {
            MountainRoadCafeConversationLook look =
                target.GetComponent<MountainRoadCafeConversationLook>();
            return look != null
                ? look
                : target.AddComponent<MountainRoadCafeConversationLook>();
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
