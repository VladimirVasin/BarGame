using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Five physical speakers share one local, pause-aware bubble channel.</summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class CityPortConversationController : MonoBehaviour
    {
        public const float RestPairRangeMeters = 6f;
        public const float WorkingPairRangeMeters = 30f;
        private static readonly string[] voices =
        {
            NpcVoiceCatalog.FishermanDesignId, NpcVoiceCatalog.ChessPlayerDesignId,
            NpcVoiceCatalog.CheckersPlayerDesignId, NpcVoiceCatalog.CafeManDesignId,
            NpcVoiceCatalog.WatchmanDesignId
        };
        private CityPortController port;
        private CityPortCrew crew;
        private Camera worldCamera, lastCamera;
        private Transform explicitListener, lastListener;
        private NpcSpeechBubbleView bubbles;
        private CityPortConversationSchedule schedule;
        private int shownSerial = -1;
        private bool hasVisibleLine;

        public CityPortConversationSchedule Schedule => schedule;
        public NpcSpeechBubbleView Bubbles => bubbles;
        public string LastLineKey { get; private set; } = string.Empty;
        public int LastSpeakerRole { get; private set; } = -1;

        public void Initialize(CityPortController controller, CityPortCrew portCrew,
            Camera camera = null, Transform listener = null, int seed = 3197)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (portCrew == null) throw new ArgumentNullException(nameof(portCrew));
            ClearPresentation();
            if (crew != null && bubbles != null)
                for (int role = 0; role < CityPortConversationCatalog.RoleCount; role++)
                    bubbles.WithdrawSpeaker(crew.GetWorker(role));
            port = controller;
            crew = portCrew;
            worldCamera = camera;
            explicitListener = listener;
            schedule = new CityPortConversationSchedule(seed);
            bubbles = GetComponent<NpcSpeechBubbleView>();
            if (bubbles == null) bubbles = gameObject.AddComponent<NpcSpeechBubbleView>();
            bubbles.UseManualClock = true;
            bubbles.LineDurationSeconds = (float)CityPortConversationSchedule.LineSeconds;
            bubbles.Initialize(camera, listener);
            for (int role = 0; role < CityPortConversationCatalog.RoleCount; role++)
            {
                var actor = crew.GetWorker(role);
                if (actor != null) bubbles.DeclareSpeaker(actor, actor.Head, voices[role], NpcEarshotProfile.Shout);
            }
            shownSerial = -1;
        }

        private void LateUpdate()
        {
            if (crew == null || schedule == null || crew.UseManualClock) return;
            bool paused = !GameSessionState.IsGameTimeRunning || GameTimeScaleRuntime.IsPaused;
            bubbles.RenderEnabled = !paused;
            if (!paused) ApplyAt();
        }

        /// <summary>Consumes the crew's already-sampled life clock; also the manual capture path.</summary>
        public void ApplyAt()
        {
            if (port == null || crew == null || schedule == null) return;
            if (worldCamera == null) worldCamera = Camera.main;
            Transform listener = explicitListener != null ? explicitListener : port.PresentationObserver;
            if (listener == null && worldCamera != null) listener = worldCamera.transform;
            if (lastListener != listener || lastCamera != worldCamera)
            {
                lastListener = listener;
                lastCamera = worldCamera;
                bubbles.Initialize(worldCamera, listener);
            }
            int available = 0, working = 0, resting = 0, audible = 0;
            uint nearbyPairs = 0, closePairs = 0;
            for (int role = 0; role < CityPortConversationCatalog.RoleCount; role++)
            {
                var actor = crew.GetWorker(role);
                if (actor == null || !actor.gameObject.activeInHierarchy) continue;
                int bit = 1 << role;
                if (crew.IsRoleAvailableForSpeech(role)) available |= bit;
                if (crew.IsRoleWorking(role)) working |= bit;
                if (crew.IsRoleResting(role)) resting |= bit;
                if (listener != null && Vector3.SqrMagnitude(actor.Head.position - listener.position) <=
                    NpcEarshotProfile.ShoutCullRadiusMeters * NpcEarshotProfile.ShoutCullRadiusMeters)
                    audible |= bit;
                for (int partner = 0; partner < role; partner++)
                {
                    var other = crew.GetWorker(partner);
                    if (other == null || !other.gameObject.activeInHierarchy) continue;
                    float distance = Vector3.SqrMagnitude(actor.Head.position - other.Head.position);
                    uint pair = CityPortConversationCatalog.PairBit(role, partner);
                    if (distance <= WorkingPairRangeMeters * WorkingPairRangeMeters) nearbyPairs |= pair;
                    float restEarshot = NpcEarshotProfile.ConversationFaintRadiusMeters;
                    if (distance <= RestPairRangeMeters * RestPairRangeMeters && listener != null &&
                        Vector3.SqrMagnitude(actor.Head.position - listener.position) <= restEarshot * restEarshot &&
                        Vector3.SqrMagnitude(other.Head.position - listener.position) <= restEarshot * restEarshot)
                        closePairs |= pair;
                }
            }
            // Both turns must remain in earshot. A reply cannot arrive from a
            // missing/cull-hidden partner, nor be replayed after walking back.
            available &= audible;
            var turn = schedule.Advance(crew.LifeElapsedSeconds, port.ElapsedSeconds, port.Snapshot,
                available, working, resting, nearbyPairs, closePairs,
                listener != null && audible != 0 && (port.ShorePresentationActive || port.VesselPresentationActive));
            for (int role = 0; role < CityPortConversationCatalog.RoleCount; role++)
                crew.SetSpeech(role, -1, false, false);
            if (turn.HasExchange)
            {
                int first = turn.Exchange.FirstRole, second = turn.Exchange.SecondRole;
                bool salutation = turn.Exchange.Kind == CityPortConversationKind.Greeting ||
                    turn.Exchange.Kind == CityPortConversationKind.Farewell;
                crew.SetSpeech(first, second, turn.IsSpeaking && turn.SpeakerRole == first,
                    salutation && turn.IsSpeaking && turn.SpeakerRole == first);
                crew.SetSpeech(second, first, turn.IsSpeaking && turn.SpeakerRole == second,
                    salutation && turn.IsSpeaking && turn.SpeakerRole == second);
            }
            if (!turn.IsSpeaking)
            {
                if (hasVisibleLine) bubbles.DismissAll();
                hasVisibleLine = false;
            }
            else if (shownSerial != turn.LineSerial)
            {
                bubbles.DismissAll();
                var speaker = crew.GetWorker(turn.SpeakerRole);
                var earshot = turn.Exchange.Kind == CityPortConversationKind.Rest ?
                    NpcEarshotProfile.Conversation : NpcEarshotProfile.Shout;
                bubbles.DeclareSpeaker(speaker, speaker.Head, voices[turn.SpeakerRole], earshot);
                bubbles.LineDurationSeconds = (float)CityPortConversationSchedule.LineDuration(turn.Exchange.Kind);
                bubbles.ShowAt(speaker, LocalizationService.Get(turn.LineKey), (float)crew.LifeElapsedSeconds);
                shownSerial = turn.LineSerial;
                hasVisibleLine = true;
                LastLineKey = turn.LineKey;
                LastSpeakerRole = turn.SpeakerRole;
            }
            bubbles.AdvanceTo((float)crew.LifeElapsedSeconds);
        }

        private void OnDisable()
        {
            schedule?.Reset();
            ClearPresentation();
        }

        private void ClearPresentation()
        {
            if (bubbles != null) bubbles.DismissAll();
            hasVisibleLine = false;
            if (crew == null) return;
            for (int role = 0; role < CityPortConversationCatalog.RoleCount; role++)
                crew.SetSpeech(role, -1, false, false);
        }
    }
}
