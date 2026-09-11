using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One local pair at a time, using the shared bubble's actual line lifetime.</summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class CityCanneryConversationController : MonoBehaviour
    {
        public const double MaximumContinuousStepSeconds = 2d;
        public const double ReplyGapSeconds = .65d;
        public const double ArrivalSettleSeconds = 5d;
        private const double PoseReleaseSeconds = 1d;
        private static readonly string[] voices =
        {
            NpcVoiceCatalog.CheckersPlayerDesignId, NpcVoiceCatalog.ChessPlayerDesignId,
            NpcVoiceCatalog.CafeManDesignId, NpcVoiceCatalog.WatchmanDesignId
        };
        private readonly VillageResidentPresentation[] workers = new VillageResidentPresentation[4];
        private readonly Transform[] spines = new Transform[4];
        private readonly float[] headYaw = new float[4], bodyYaw = new float[4], gesture = new float[4];
        private CityCanneryController factory;
        private CityCanneryConversationDeck deck;
        private CityCanneryConversationExchange exchange;
        private NpcSpeechBubbleView bubbles;
        private Transform listener;
        private Camera worldCamera;
        private double previousLife = double.NaN, previousWork = double.NaN, previousPose = double.NaN;
        private double nextExchangeAt, replyAt;
        private int activeRole = -1;
        private bool hasExchange, replyPending, replyStarted, engaged;

        public bool UseManualClock { get; set; }
        public bool HasExchange => hasExchange;
        public NpcSpeechBubbleView Bubbles => bubbles;
        public int CompletedExchanges { get; private set; }
        public string LastLineKey { get; private set; } = string.Empty;
        public int LastSpeakerRole { get; private set; } = -1;
        public CityCanneryConversationExchange CurrentExchange => exchange;

        public void Initialize(CityCanneryController controller, Transform hero, int seed)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            ReleaseSpeakers();
            factory = controller;
            listener = hero;
            deck = new CityCanneryConversationDeck(seed);
            CompletedExchanges = 0;
            LastLineKey = string.Empty;
            LastSpeakerRole = -1;
            bubbles = GetComponent<NpcSpeechBubbleView>();
            if (bubbles == null) bubbles = gameObject.AddComponent<NpcSpeechBubbleView>();
            bubbles.UseManualClock = true;
            worldCamera = Camera.main;
            bubbles.Initialize(worldCamera, listener);
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = factory.GetFactoryWorker(i);
                if (workers[i] == null || workers[i].Head == null)
                    throw new InvalidOperationException("Cannery speech needs every existing worker's actual head.");
                spines[i] = CityPedestrianHandProps.FindSocket(workers[i].ModelRoot, "spine");
            }
            DeclareSpeakers();
            ResetContinuity();
        }

        private void LateUpdate()
        {
            if (factory == null || bubbles == null) return;
            bool paused = !GameSessionState.IsGameTimeRunning || GameTimeScaleRuntime.IsPaused;
            bubbles.RenderEnabled = !paused && factory.isActiveAndEnabled;
            if (!factory.isActiveAndEnabled) { Suspend(); return; }
            if (!paused && !UseManualClock) ApplyAt();
        }

        /// <summary>The already-sampled life clock also supports deterministic scene captures.</summary>
        public void ApplyAt()
        {
            if (factory == null || deck == null || bubbles == null) return;
            if (!bubbles.IsDeclared(workers[0])) DeclareSpeakers();
            double now = factory.LifeSeconds;
            double lifeStep = now - previousLife, workStep = factory.WorkingSeconds - previousWork;
            bool seek = double.IsNaN(previousLife) || lifeStep < 0d || lifeStep > MaximumContinuousStepSeconds ||
                workStep < 0d || workStep > Math.Max(0d, lifeStep) + MaximumContinuousStepSeconds;
            previousLife = now;
            previousWork = factory.WorkingSeconds;
            if (seek)
            {
                CancelExchange();
                ResetPose();
                nextExchangeAt = now + ArrivalSettleSeconds;
            }
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
                bubbles.Initialize(worldCamera, listener);
            }
            bool near = CanHearFactory();
            if (!near)
            {
                CancelExchange();
                engaged = false;
                return;
            }
            if (!engaged) nextExchangeAt = Math.Max(nextExchangeAt, now + ArrivalSettleSeconds);
            engaged = true;
            if (seek) return;
            bubbles.AdvanceTo((float)now);

            if (hasExchange)
            {
                if (!PairPresent(exchange)) { CancelExchange(); nextExchangeAt = now + ArrivalSettleSeconds; return; }
                if (activeRole >= 0 && bubbles.IsShowing(workers[activeRole])) return;
                if (replyStarted)
                {
                    CompletedExchanges++;
                    CancelExchange();
                    nextExchangeAt = now + 12d + exchange.Variant % 4 * 2d;
                    return;
                }
                if (!replyPending) { replyPending = true; replyAt = now + ReplyGapSeconds; }
                if (now < replyAt) return;
                // A blocked second speaker gets only this observed opportunity, not a backlog.
                if (ShowLine(exchange.SecondRole, exchange.Key(true), now)) replyStarted = true;
                else { CancelExchange(); nextExchangeAt = now + ArrivalSettleSeconds; }
                return;
            }

            if (now < nextExchangeAt) return;
            CityCanneryConversationKind kind = Context();
            uint eligible = 0;
            for (int i = 0; i < CityCanneryConversationCatalog.Count(kind); i++)
            {
                var candidate = CityCanneryConversationCatalog.Get(kind, i);
                if (!PairPresent(candidate)) continue;
                double duration = LineDuration(candidate.Key(false)) + ReplyGapSeconds + LineDuration(candidate.Key(true)) + PoseReleaseSeconds;
                if (SpeechWindow(candidate.FirstRole) >= duration && SpeechWindow(candidate.SecondRole) >= duration)
                    eligible |= 1u << i;
            }
            if (!deck.TryPeek(kind, eligible, out var selected)) { nextExchangeAt = now + 1d; return; }
            if (!ShowLine(selected.FirstRole, selected.Key(false), now)) { nextExchangeAt = now + 1d; return; }
            exchange = selected;
            hasExchange = true;
            replyPending = replyStarted = false;
            deck.Commit(selected);
        }

        private CityCanneryConversationKind Context() =>
            factory.Snapshot.Stage == CityFishSupplyStage.FactoryReverse || factory.Snapshot.Stage == CityFishSupplyStage.UnloadFish
                ? CityCanneryConversationKind.Receiving : factory.Production.IsActive
                    ? CityCanneryConversationKind.Work : CityCanneryConversationKind.Wait;

        private bool CanHearFactory()
        {
            if (listener == null || !factory.isActiveAndEnabled || !factory.FactoryPresentationActive) return false;
            // The room, open passage and nearby outside waiting places share
            // the same finite crew; individual earshot still limits each pair.
            Vector3 local = factory.Plan.Local(listener.position);
            return local.x >= -9f && local.x <= 4f && local.z >= -9f && local.z <= 9f && local.y >= -1f && local.y <= 5f;
        }

        private bool PairPresent(in CityCanneryConversationExchange pair) =>
            SpeakerPresent(pair.FirstRole) && SpeakerPresent(pair.SecondRole) &&
            Vector3.SqrMagnitude(workers[pair.FirstRole].Head.position - workers[pair.SecondRole].Head.position) <=
                NpcEarshotProfile.RoomFaintRadiusMeters * NpcEarshotProfile.RoomFaintRadiusMeters;

        private bool SpeakerPresent(int role)
        {
            var actor = workers[role];
            if (actor == null || actor.Head == null || !actor.gameObject.activeInHierarchy || listener == null) return false;
            if (actor.CurrentAction != VillageResidentAction.Idle && actor.CurrentAction != VillageResidentAction.StationWork)
                return false;
            return NpcEarshotProfile.Room.ResolveOpacity(Vector3.Distance(actor.Head.position, listener.position)) > 0f;
        }

        private double SpeechWindow(int role) => factory.FactoryWorkerAvailableSeconds(role);

        private static float LineDuration(string key) => Mathf.Max(NpcSpeechBubbleView.VisibleSeconds,
            SpeechDelivery.ResolveSpokenDuration(LocalizationService.Get(key), SpeechDelivery.ReadingTailSeconds));

        private bool ShowLine(int role, string key, double now)
        {
            if (bubbles.IsShowing(workers[role]) || NpcSpeechBubbleView.IsPresentingAt(workers[role].Head)) return false;
            if (!bubbles.ShowAt(workers[role], LocalizationService.Get(key), (float)now, LineDuration(key))) return false;
            activeRole = LastSpeakerRole = role;
            LastLineKey = key;
            return true;
        }

        public int PartnerFor(int role) => !hasExchange ? -1 : exchange.FirstRole == role ? exchange.SecondRole :
            exchange.SecondRole == role ? exchange.FirstRole : -1;

        /// <summary>Called after the factory's base pose and contacts. Work keeps the arms and spine.</summary>
        public void ApplyCrewPose()
        {
            if (factory == null || bubbles == null || !isActiveAndEnabled) return;
            double now = factory.LifeSeconds, step = now - previousPose;
            bool seek = double.IsNaN(previousPose) || step < 0d || step > MaximumContinuousStepSeconds;
            float dt = seek ? 0f : (float)step;
            previousPose = now;
            if (seek) ResetPose();
            for (int role = 0; role < workers.Length; role++)
            {
                var actor = workers[role];
                if (actor == null || !actor.gameObject.activeInHierarchy) continue;
                int partner = PartnerFor(role);
                bool available = SpeakerPresent(role);
                bool free = available && factory.FactoryWorkerHandsFree(role);
                float desired = 0f;
                if (partner >= 0 && available && workers[partner] != null)
                {
                    Vector3 direction = Vector3.ProjectOnPlane(workers[partner].Head.position - actor.Head.position, actor.transform.up);
                    desired = Mathf.Clamp(Vector3.SignedAngle(actor.transform.forward, direction, actor.transform.up), -55f, 55f);
                }
                bodyYaw[role] = Mathf.MoveTowards(bodyYaw[role], free ? desired * .45f : 0f, dt * 40f);
                // A newly needed tool wins immediately; only a free worker turns his shoulders.
                if (free && spines[role] != null)
                    spines[role].rotation = Quaternion.AngleAxis(bodyYaw[role], actor.transform.up) * spines[role].rotation;
                headYaw[role] = Mathf.MoveTowards(headYaw[role], available ? desired - (free ? bodyYaw[role] : 0f) : 0f, dt * 55f);
                actor.Head.rotation = Quaternion.AngleAxis(Mathf.Clamp(headYaw[role], -30f, 30f), actor.transform.up) * actor.Head.rotation;
                bool speaking = hasExchange && activeRole == role && bubbles.TryGetSpeechFaceSample(actor, out SpeechFaceSample sample) && sample.IsTyping;
                gesture[role] = Mathf.MoveTowards(gesture[role], free && speaking ? 1f : 0f, dt * 3f);
                if (!free || gesture[role] <= 0f) continue;
                float time = (float)(now % 60d) + role;
                Vector3 target = actor.transform.position + actor.transform.up * (1.06f + .03f * Mathf.Sin(time * 2.4f)) +
                    actor.transform.right * (.25f + .035f * Mathf.Sin(time * 1.7f)) + actor.transform.forward * .22f;
                actor.ApplyHandContacts(target, null, gesture[role] * .7f);
            }
        }

        private void CancelExchange()
        {
            bubbles?.DismissAll();
            hasExchange = replyPending = replyStarted = false;
            activeRole = -1;
        }

        private void ResetPose()
        {
            Array.Clear(headYaw, 0, headYaw.Length);
            Array.Clear(bodyYaw, 0, bodyYaw.Length);
            Array.Clear(gesture, 0, gesture.Length);
        }

        private void ResetContinuity()
        {
            CancelExchange();
            ResetPose();
            previousLife = previousWork = previousPose = double.NaN;
            engaged = false;
        }

        /// <summary>Factory component disable also releases this sibling component's speech leases.</summary>
        public void Suspend()
        {
            ReleaseSpeakers();
            ResetContinuity();
            if (bubbles != null) bubbles.RenderEnabled = false;
        }

        private void DeclareSpeakers()
        {
            if (bubbles == null) return;
            for (int i = 0; i < workers.Length; i++)
                if (workers[i] != null) bubbles.DeclareSpeaker(workers[i], workers[i].Head, voices[i], NpcEarshotProfile.Room);
        }

        private void ReleaseSpeakers()
        {
            CancelExchange();
            if (bubbles == null) return;
            foreach (var worker in workers) if (worker != null) bubbles.WithdrawSpeaker(worker);
        }

        private void OnEnable() { DeclareSpeakers(); ResetContinuity(); }
        private void OnDisable() { ReleaseSpeakers(); ResetContinuity(); }
        private void OnDestroy() => ReleaseSpeakers();
    }
}
