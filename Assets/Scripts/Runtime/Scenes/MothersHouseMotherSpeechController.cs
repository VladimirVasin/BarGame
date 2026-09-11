using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// His mother talking to herself, and to him when he comes in.
    ///
    /// The Ferryman's road speech in a room: a bubble over her head on
    /// a manual clock, a shuffled bag behind it, and a silence only
    /// visible unpaused time spends. Two differences, both because a
    /// room is not a journey — there is no per-visit quota, and the
    /// silence only runs while he is actually near her, so a hero who
    /// went upstairs does not come back down to a bag she emptied on
    /// an empty room.
    ///
    /// The greeting is not in the bag. It fires once per entry, as
    /// soon as the door fade is clear, and it is the same line every
    /// time on purpose: she does not hold that he already came in.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MothersHouseMotherSpeechController : MonoBehaviour
    {
        public const string RuntimeObjectName = "Mother Speech";

        /// <summary>How long after the room comes up she says hello.
        /// Long enough that the door fade is gone and he is standing,
        /// short enough to read as a greeting rather than a remark.
        /// </summary>
        public const float GreetingDelaySeconds = 1.2f;

        public const float ReadingTailSeconds = SpeechDelivery.ReadingTailSeconds;

        /// <summary>How close he has to be for her to be talking to
        /// him rather than to the room. The ground floor is about nine
        /// metres across, so this is most of it and none of upstairs.
        /// </summary>
        public const float SpeechRadiusMeters = 6f;

        /// <summary>Upstairs is `3.54 m` up. Anything past this is a
        /// different floor and out of the conversation.</summary>
        public const float SpeechHeightToleranceMeters = 2f;

        private NpcSpeechBubbleView bubbles;
        private MothersHouseMotherSpeechState state;
        private NpcSpeaker speaker = NpcSpeaker.None;
        private Transform hero;
        private Vector3 seatPosition;
        private float lineElapsed;
        private float greetingDelayRemaining = GreetingDelaySeconds;
        private bool greeted;

        public bool IsInitialized { get; private set; }
        public bool HasGreeted => greeted;

        /// <summary>A line of hers is on screen. Asked of the bubble by
        /// owner, because one view carries every speaker in the room
        /// and "is anything showing" is not the same question.
        /// </summary>
        public bool IsSpeaking =>
            bubbles != null &&
            speaker.Owner != null &&
            bubbles.IsShowing(speaker.Owner);
        public bool CanSpeak => IsInitialized && isActiveAndEnabled &&
            speaker.IsValid && speaker.Anchor != null && !IsSpeaking;
        public string LineKey { get; private set; } = string.Empty;
        public string FullText { get; private set; } = string.Empty;
        public NpcSpeechBubbleView Bubbles => bubbles;

        public static MothersHouseMotherSpeechController Create(
            Transform parent,
            Camera camera,
            Transform heroRoot,
            Vector3 seat,
            in NpcSpeaker source)
        {
            var host = new GameObject(RuntimeObjectName);
            host.transform.SetParent(parent, false);
            MothersHouseMotherSpeechController controller =
                host.AddComponent<MothersHouseMotherSpeechController>();
            controller.Initialize(camera, heroRoot, seat, source);
            return controller;
        }

        public void Initialize(
            Camera camera,
            Transform heroRoot,
            Vector3 seat,
            in NpcSpeaker source)
        {
            hero = heroRoot;
            seatPosition = seat;
            speaker = source;
            state = MothersHouseMotherSpeechSession.State;
            bubbles = gameObject.AddComponent<NpcSpeechBubbleView>();

            // A manual clock, fed a per-line elapsed that starts at
            // zero. The self-driving one compares against
            // `Time.unscaledTime`, which is already far past any
            // duration, and would expire every line on the frame it
            // appeared.
            bubbles.UseManualClock = true;
            bubbles.Initialize(camera, heroRoot);
            if (speaker.IsValid)
            {
                bubbles.DeclareSpeaker(speaker);
            }

            IsInitialized = true;
        }

        /// <summary>
        /// Whether she is close enough to him to be speaking at all.
        /// Planar, plus a floor check: upstairs is a different room
        /// however short the horizontal distance looks in plan.
        /// </summary>
        public bool IsHeroWithinEarshot()
        {
            if (hero == null)
            {
                return false;
            }

            Vector3 delta = hero.position - transform.TransformPoint(
                seatPosition);
            if (Mathf.Abs(delta.y) > SpeechHeightToleranceMeters)
            {
                return false;
            }

            delta.y = 0f;
            return delta.sqrMagnitude <=
                   SpeechRadiusMeters * SpeechRadiusMeters;
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            bool suppressed =
                PauseMenuController.IsAnyPaused ||
                JournalController.IsAnyOpen ||
                BarMinigameModalLock.IsAnyLocked ||
                SceneTransitionService.IsTransitioning;
            bubbles.RenderEnabled = !suppressed;
            if (suppressed || GameTimeScaleRuntime.IsPaused)
            {
                return;
            }

            // Speech runs on real seconds. Slowing the world down does
            // not slow down how fast somebody talks.
            float seconds = Time.unscaledDeltaTime;
            if (IsSpeaking)
            {
                lineElapsed += seconds;
                bubbles.AdvanceTo(lineElapsed);
                if (!IsSpeaking)
                {
                    FinishLine();
                }

                return;
            }

            if (!IsHeroWithinEarshot())
            {
                return;
            }

            if (!greeted)
            {
                greetingDelayRemaining -= seconds;
                if (greetingDelayRemaining <= 0f)
                {
                    greeted = true;
                    Say(MothersHouseMotherQuips.GreetingLineKey);
                }

                return;
            }

            int line = state.AdvanceSilence(seconds, IsScarfWorn());
            if (line >= 0)
            {
                Say(MothersHouseMotherQuips.LineKeys[line]);
            }
        }

        /// <summary>
        /// Puts one line up. Public because the talk stub speaks
        /// through the same bubble: two channels sharing one mouth,
        /// rather than a second bubble that could sit over the first.
        /// </summary>
        public bool Say(string key)
        {
            if (!CanSpeak || string.IsNullOrEmpty(key))
            {
                return false;
            }

            string text = LocalizationService.Get(key);
            bubbles.LineDurationSeconds =
                SpeechDelivery.ResolveSpokenDuration(
                    text,
                    ReadingTailSeconds);
            bubbles.DeclareSpeaker(speaker);
            if (!bubbles.ShowAt(speaker.Owner, text, 0f)) return false;
            LineKey = key;
            FullText = text;
            lineElapsed = 0f;
            return true;
        }

        /// <summary>
        /// Answer only when her current line has finished. The talk
        /// trigger uses the same gate before it activates the scarf
        /// request or spends an ordinary line from her bag.
        /// </summary>
        public bool SayOnDemand(string key)
        {
            return Say(key);
        }

        private void FinishLine()
        {
            state.FinishLine();
            LineKey = string.Empty;
            FullText = string.Empty;
            lineElapsed = 0f;
        }

        private static bool IsScarfWorn()
        {
            return GameSessionState.IsInventoryItemEquipped(
                InventoryItemId.Scarf);
        }

        private void OnDisable()
        {
            if (bubbles != null && speaker.Owner != null)
            {
                bubbles.WithdrawSpeaker(speaker.Owner);
            }

            LineKey = string.Empty;
            FullText = string.Empty;
        }
    }
}
