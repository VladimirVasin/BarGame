using System;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(230)]
    [DisallowMultipleComponent]
    public sealed class CityPigeonKeeperSpeech : MonoBehaviour
    {
        private const float QuietGapSeconds = 20f;
        private static readonly string[] Keys =
        {
            "city.pigeon_keeper.line.01", "city.pigeon_keeper.line.02", "city.pigeon_keeper.line.03",
            "city.pigeon_keeper.line.04", "city.pigeon_keeper.line.05", "city.pigeon_keeper.line.06",
            "city.pigeon_keeper.line.07"
        };

        // The order belongs to this person across City visits in the same game.
        // Only a successful admission to the shared view spends an entry.
        private static int session = -1;
        private static readonly int[] order = new int[Keys.Length];
        private static System.Random random;
        private static int cursor, previous;
        private CityPigeonKeeper keeper;
        private NpcSpeechBubbleView bubbles;
        private float quietAfterSpeech, quietAfterGrief;
        private bool declared;

        public NpcSpeaker Speaker { get; private set; }
        public float SilenceRemaining { get; private set; }
        public string LastLineKey { get; private set; } = string.Empty;
        public bool IsSpeaking => declared && bubbles && bubbles.IsShowing(Speaker.Owner);
        public bool CanStartGrief => quietAfterSpeech <= 0f && !IsSpeaking &&
            !NpcSpeechBubbleView.IsPresentingAt(Speaker.Anchor);

        public void Initialize(CityPigeonKeeper owner, NpcSpeechBubbleView cityBubbles)
        {
            if (!owner || !owner.Worker || !owner.Worker.Head || !cityBubbles)
                throw new ArgumentException("The pigeon keeper needs his own head and the City speech view.");
            if (keeper) throw new InvalidOperationException("The pigeon keeper's speech is already bound.");
            keeper = owner;
            bubbles = cityBubbles;
            Speaker = new NpcSpeaker(keeper.Worker, keeper.Worker.Head, DefaultNpcPopulation.CityPigeonKeeper,
                NpcEarshotProfile.Conversation, RussianVocalizerGender.Male);
            keeper.BindGriefSpeech(this);
            if (random == null || session != GameSessionState.SessionGeneration)
            {
                session = GameSessionState.SessionGeneration;
                random = new System.Random(unchecked(GameSessionState.CitySeed * 397 ^ session));
                cursor = Keys.Length;
                previous = -1;
            }
            SilenceRemaining = random.Next(45, 76);
        }

        private void LateUpdate()
        {
            if (keeper && keeper.AutoAdvance) Advance(Mathf.Min(Time.unscaledDeltaTime, Time.maximumDeltaTime));
        }

        public void Advance(float activeRealSeconds)
        {
            if (!keeper || !bubbles || !isActiveAndEnabled) return;
            if (GameTimeScaleRuntime.IsPaused || AudioListener.pause) return;
            if (!float.IsFinite(activeRealSeconds) || activeRealSeconds < 0f) return;
            bool speaking = IsSpeaking;
            quietAfterSpeech = speaking ? QuietGapSeconds : Mathf.Max(0f, quietAfterSpeech - activeRealSeconds);
            quietAfterGrief = keeper.IsCrying ? QuietGapSeconds : Mathf.Max(0f, quietAfterGrief - activeRealSeconds);
            Transform listener = keeper.PresentationObserver;
            bool canHear = keeper.isActiveAndEnabled && !keeper.IsAtHome && !keeper.IsCommuting &&
                GameSessionState.GameTimeOfDayMinutes >= 360 && GameSessionState.GameTimeOfDayMinutes < 1260 &&
                bubbles.isActiveAndEnabled && bubbles.RenderEnabled && !SceneTransitionService.IsTransitioning &&
                !BarMinigameModalLock.IsAnyLocked && listener && Speaker.Anchor && Speaker.Anchor.gameObject.activeInHierarchy &&
                Speaker.Earshot.IsAudible(Speaker.ResolveDistance(listener, Vector3.zero));
            if (canHear && !speaking) SilenceRemaining = Mathf.Max(0f, SilenceRemaining - activeRealSeconds);
            if (!canHear || keeper.IsCrying)
            {
                ReleaseSpeaker();
                return;
            }
            if (speaking || activeRealSeconds <= 0f) return;
            if (SilenceRemaining > 0f || quietAfterGrief > 0f || !HasWorkWindow() ||
                NpcSpeechBubbleView.IsPresentingAt(Speaker.Anchor)) return;
            if (!declared) declared = bubbles.DeclareSpeaker(Speaker);
            if (!declared) return;
            if (cursor == order.Length) Refill();
            int candidate = order[cursor];
            string key = Keys[candidate];
            if (!bubbles.ShowAt(Speaker.Owner, LocalizationService.Get(key), Time.unscaledTime,
                NpcSpeechBubbleView.VisibleSeconds)) return;
            previous = candidate;
            cursor++;
            LastLineKey = key;
            quietAfterSpeech = QuietGapSeconds;
            SilenceRemaining = random.Next(120, 181);
        }

        private bool HasWorkWindow()
        {
            // The stop's measured work window excludes the approach and departure.
            var task = keeper.CurrentDayTask;
            if (!task.HasValue || task == CityPigeonKeeperDayTask.ReturnToDeck || keeper.DayTaskTime < 0f) return false;
            return keeper.DayTaskTime + NpcSpeechBubbleView.VisibleSeconds <= keeper.CurrentDayWorkSeconds;
        }

        private static void Refill()
        {
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (order[i], order[other]) = (order[other], order[i]);
            }
            if (order[0] == previous)
            {
                int other = random.Next(1, order.Length);
                (order[0], order[other]) = (order[other], order[0]);
            }
            cursor = 0;
        }

        private void ReleaseSpeaker()
        {
            if (declared && bubbles && Speaker.Owner) bubbles.WithdrawSpeaker(Speaker.Owner);
            declared = false;
        }

        private void OnDisable() => ReleaseSpeaker();
    }
}
