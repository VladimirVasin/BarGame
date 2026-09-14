using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two ordinary guards: grounded duty, one local speech channel and restrained mutual attention.</summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class CityEastGuardController : MonoBehaviour
    {
        private static readonly int[] FirstSpeakers = { 0, 1, 1, 0, 0, 0 };
        private readonly float[] speed = new float[2], gait = new float[2];
        private readonly float[] headYaw = new float[2];
        private readonly float[] listenWeight = new float[2];
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly EastGuardActor[] actors = new EastGuardActor[2];
        private readonly Deck[] replies = new Deck[2];
        private Deck pairs;
        private Transform listener;
        private Camera worldCamera;
        private WorldDistancePresentation distancePresentation;
        private NpcSpeechBubbleView bubbles;
        private int pair = -1, pairStage, activeSpeaker = -1, pendingReply = -1, replySpeaker = -1;
        private PlayerInteractor requester;
        private double nextExchange = 8d, replyAt = -1d, replyCooldown;
        private bool engaged;

        public CityEastGuardPlan Plan { get; private set; }
        public CityEastGuardDuty Duty { get; } = new CityEastGuardDuty();
        public double LifeSeconds { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public bool IsInitialized { get; private set; }
        public bool PresentationActive => distancePresentation == null || distancePresentation.IsVisible;
        public bool IsBlocked { get; private set; }
        public bool HasExchange => pair >= 0 || replySpeaker >= 0;
        public int PendingReply => pendingReply;
        public int LastSpeaker { get; private set; } = -1;
        public string LastLineKey { get; private set; } = string.Empty;
        public int CompletedExchanges { get; private set; }
        public NpcSpeechBubbleView Bubbles => bubbles;
        public EastGuardActor Actor(int index) => actors[index];
        public float Speed(int index) => speed[index];

        public void Initialize(CityEastGuardPlan plan, Transform hero, Camera camera, int seed)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            listener = hero; worldCamera = camera;
            pairs = new Deck(6, seed ^ 0x3f65);
            bubbles = gameObject.AddComponent<NpcSpeechBubbleView>();
            bubbles.UseManualClock = true;
            bubbles.Initialize(camera, hero);
            for (int i = 0; i < 2; i++)
            {
                actors[i] = EastGuardAssetProvider.Create(transform, i);
                actors[i].transform.SetPositionAndRotation(plan.Station(i) + Vector3.up * actors[i].GroundOffset,
                    plan.StationFacing(i));
                actors[i].Motion.Initialize();
                var solid = actors[i].gameObject.AddComponent<CapsuleCollider>();
                solid.radius = .31f; solid.height = actors[i].Height;
                solid.center = Vector3.up * (solid.height * .5f);
                actors[i].gameObject.AddComponent<CityEastGuardInteraction>().Initialize(this, i);
                replies[i] = new Deck(4, seed + 73 + i * 397);
            }
            distancePresentation = new WorldDistancePresentation(actors[0].transform, actors[1].transform);
            IsInitialized = true;
            DeclareSpeakers();
            Present(0f);
        }

        private void LateUpdate()
        {
            if (!IsInitialized) return;
            bool paused = GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning;
            bubbles.RenderEnabled = !paused;
            if (AutoAdvance && !paused) Advance(Time.deltaTime);
            if (pendingReply >= 0 && GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Gameplay))
                CancelRequestedReply();
        }

        /// <summary>Same bounded step in gameplay and the focused scene proof; pause freezes all owned time.</summary>
        public void Advance(float deltaTime)
        {
            if (!IsInitialized || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused ||
                !GameSessionState.IsGameTimeRunning || deltaTime <= 0f) return;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            // Hitches cannot teleport a patrol, skip a handoff or replay missed conversation.
            float dt = Mathf.Min(deltaTime, .1f);
            LifeSeconds += dt;
            bool visible = WorldDistancePresentation.ShouldShow(listener, Plan.Influence, PresentationActive);
            distancePresentation.SetVisible(visible);
            if (!bubbles.IsDeclared(actors[0].Motion)) DeclareSpeakers();
            bubbles.AdvanceTo((float)LifeSeconds);
            AdvanceSpeech();
            bool held = HasExchange || pendingReply >= 0 || BarMinigameModalLock.IsAnyLocked || SceneTransitionService.IsTransitioning;
            Duty.Advance(dt, held);
            IsBlocked = false;
            for (int i = 0; i < 2; i++)
            {
                bool walking = Duty.IsWalking(i) && !held;
                float desiredSpeed = walking ? Plan.Speed(i) : 0f;
                speed[i] = Mathf.MoveTowards(speed[i], desiredSpeed, dt * 1.3f);
                if (walking)
                {
                    Vector3 position = actors[i].transform.position;
                    Vector3 target = Plan.Target(i, Duty.Waypoint) + Vector3.up * actors[i].GroundOffset;
                    Vector3 direction = Vector3.ProjectOnPlane(target - position, Vector3.up);
                    float remaining = direction.magnitude;
                    if (remaining <= .055f) { Duty.Arrive(); speed[i] = 0f; }
                    else
                    {
                        direction /= remaining;
                        Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
                        actors[i].transform.rotation = Quaternion.RotateTowards(actors[i].transform.rotation, facing, dt * 105f);
                        float distance = Mathf.Min(remaining, speed[i] * dt);
                        if (Quaternion.Angle(actors[i].transform.rotation, facing) > 40f || !CanMove(i, direction, distance))
                        { speed[i] = 0f; IsBlocked = true; }
                        else
                        {
                            Vector3 next = position + direction * distance;
                            next.y = Plan.GroundTop(new Vector2(next.x, next.z)) + actors[i].GroundOffset;
                            actors[i].transform.position = next;
                            gait[i] += distance / (Plan.Speed(i) * actors[i].Motion.ClipLength(VillageResidentAction.Walk));
                        }
                    }
                }
                // Speech does not let the movement blend slide a standing body.
                else speed[i] = 0f;
            }
            if (visible) Present(dt);
        }

        private bool CanMove(int index, Vector3 direction, float distance)
        {
            Transform actor = actors[index].transform;
            Vector3 bottom = actor.position + Vector3.up * .36f;
            Vector3 top = actor.position + Vector3.up * (actors[index].Height - .31f);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, .30f, direction, hits, distance + .15f,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].collider != null && !hits[i].collider.transform.IsChildOf(actor) && hits[i].normal.y < .7f)
                    return false;
            return true;
        }

        private void Present(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                EastGuardActor actor = actors[i];
                bool talkingToHero = (replySpeaker == i || pendingReply == i) && requester != null;
                bool privatePair = pair >= 0;
                // Brief, asynchronous looks can outlast an ordinary exchange;
                // approaching the speaker returns his attention to the visitor.
                double glance = LifeSeconds % 41d;
                bool quietLook = !HasExchange && Duty.IsHome(i) && glance > (i == 0 ? 27d : 28.2d) &&
                    glance < (i == 0 ? 29.6d : 31d) && DistanceToListener(i) > 4f;
                Vector3? look = talkingToHero ? requester.transform.position + Vector3.up * 1.6f :
                    privatePair || quietLook ? actors[1 - i].Motion.Head.position : (Vector3?)null;
                if (speed[i] <= .01f)
                {
                    Quaternion facing = Duty.IsHome(i) ? Plan.StationFacing(i) : Quaternion.Euler(0f, 90f, 0f);
                    if (look.HasValue && Duty.IsHome(i))
                    {
                        Vector3 direction = Vector3.ProjectOnPlane(look.Value - actor.transform.position, Vector3.up);
                        if (direction.sqrMagnitude > .01f)
                            facing = talkingToHero ? Quaternion.LookRotation(direction) :
                                Quaternion.RotateTowards(facing, Quaternion.LookRotation(direction), 20f);
                    }
                    actor.transform.rotation = Quaternion.RotateTowards(actor.transform.rotation, facing, dt * (i == 0 ? 40f : 55f));
                }
                float desiredYaw = 0f;
                if (look.HasValue)
                {
                    Vector3 local = actor.transform.InverseTransformDirection(look.Value - actor.Motion.Head.position);
                    desiredYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -35f, 35f);
                }
                headYaw[i] = Mathf.MoveTowards(headYaw[i], desiredYaw, dt * (i == 0 ? 43f : 57f));
                Vector3 gaze = actor.Motion.Head.position + Quaternion.AngleAxis(headYaw[i], Vector3.up) * actor.transform.forward * 10f;
                listenWeight[i] = Mathf.MoveTowards(listenWeight[i], talkingToHero || privatePair ? 1f : 0f, dt * 3f);
                actor.ApplyAt(LifeSeconds + i * 3.7d, speed[i],
                    speed[i] > .01f ? gait[i] : (float)(LifeSeconds + i * 3.7d) / actor.Motion.ClipLength(VillageResidentAction.Walk),
                    gaze, bubbles, listenWeight[i]);
            }
        }

        public bool CanReply(int index, PlayerInteractor interactor) => index >= 0 && index < 2 && IsInitialized &&
            isActiveAndEnabled && PresentationActive && interactor != null && interactor.isActiveAndEnabled &&
            interactor.InputEnabled && pendingReply < 0 && replySpeaker < 0 && LifeSeconds >= replyCooldown &&
            !GameTimeScaleRuntime.IsPaused && GameSessionState.IsGameTimeRunning && !BarMinigameModalLock.IsAnyLocked &&
            !SceneTransitionService.IsTransitioning &&
            Vector3.Distance(interactor.transform.position, actors[index].transform.position) <= 3.5f;

        public bool RequestReply(int index, PlayerInteractor interactor)
        {
            if (!CanReply(index, interactor)) return false;
            pendingReply = index; requester = interactor;
            return true;
        }

        private float DistanceToListener(int index) => listener == null ? float.PositiveInfinity :
            Vector3.Distance(listener.position, actors[index].transform.position);

        private void AdvanceSpeech()
        {
            bool near = PresentationActive && Math.Min(DistanceToListener(0), DistanceToListener(1)) < 13f;
            if (!near || BarMinigameModalLock.IsAnyLocked || SceneTransitionService.IsTransitioning)
            { CancelSpeech(); engaged = false; return; }
            if (!engaged) { nextExchange = LifeSeconds + 8d; engaged = true; }
            if (requester != null && (pendingReply >= 0 || replySpeaker >= 0))
            {
                int role = pendingReply >= 0 ? pendingReply : replySpeaker;
                if (!requester.isActiveAndEnabled || !requester.InputEnabled ||
                    Vector3.Distance(requester.transform.position, actors[role].transform.position) > 5f)
                    CancelRequestedReply();
            }
            if (replySpeaker >= 0)
            {
                if (bubbles.IsShowing(actors[replySpeaker].Motion)) return;
                replySpeaker = -1; requester = null; replyCooldown = LifeSeconds + .8d;
                nextExchange = LifeSeconds + 18d;
            }
            if (pair >= 0)
            {
                if (activeSpeaker >= 0 && bubbles.IsShowing(actors[activeSpeaker].Motion)) return;
                if (pairStage == 1)
                { CompletedExchanges++; pair = activeSpeaker = -1; nextExchange = LifeSeconds + 27d; }
                else
                {
                    if (replyAt < 0d) replyAt = LifeSeconds + .7d;
                    if (LifeSeconds < replyAt) return;
                    int other = 1 - FirstSpeakers[pair];
                    if (ShowLine(other, PairKey(pair, true))) pairStage = 1;
                    return;
                }
            }
            if (pendingReply >= 0)
            {
                if (requester == null) { pendingReply = -1; return; }
                int role = pendingReply;
                string key = "city.east.guard." + (role == 0 ? "senior" : "junior") + ".reply." + (replies[role].Peek + 1).ToString("D2");
                if (ShowLine(role, key))
                { replies[role].Commit(); replySpeaker = role; pendingReply = -1; }
                return;
            }
            if (LifeSeconds < nextExchange || !Duty.IsHome(0) || !Duty.IsHome(1)) return;
            int candidate = pairs.Peek;
            // The quieter personal remarks need a little space around the two men.
            if (candidate < 3 && Math.Min(DistanceToListener(0), DistanceToListener(1)) < 4f) return;
            if (ShowLine(FirstSpeakers[candidate], PairKey(candidate, false)))
            { pair = candidate; pairStage = 0; replyAt = -1d; pairs.Commit(); }
        }

        private static string PairKey(int index, bool reply) => "city.east.guard.pair." +
            (index + 1).ToString("D2") + (reply ? ".b" : ".a");

        private bool ShowLine(int index, string key)
        {
            VillageResidentPresentation actor = actors[index].Motion;
            if (bubbles.IsShowing(actor) || NpcSpeechBubbleView.IsPresentingAt(actor.Head)) return false;
            string text = LocalizationService.Get(key);
            float duration = Mathf.Max(NpcSpeechBubbleView.VisibleSeconds,
                SpeechDelivery.ResolveSpokenDuration(text, SpeechDelivery.ReadingTailSeconds));
            if (!bubbles.ShowAt(actor, text, (float)LifeSeconds, duration)) return false;
            activeSpeaker = LastSpeaker = index; LastLineKey = key;
            return true;
        }

        private void DeclareSpeakers()
        {
            if (!IsInitialized || bubbles == null) return;
            for (int i = 0; i < 2; i++)
                bubbles.DeclareSpeaker(actors[i].Motion, actors[i].Motion.Head,
                    i == 0 ? NpcVoiceCatalog.CheckersPlayerDesignId : NpcVoiceCatalog.ChessPlayerDesignId,
                    NpcEarshotProfile.Conversation);
        }
        private void CancelRequestedReply()
        {
            if (replySpeaker >= 0) bubbles?.Dismiss(actors[replySpeaker].Motion);
            pendingReply = replySpeaker = -1; requester = null;
        }
        private void CancelSpeech()
        {
            bubbles?.DismissAll(); pair = activeSpeaker = -1; replyAt = -1d;
            CancelRequestedReply();
        }
        private void OnEnable() { DeclareSpeakers(); engaged = false; }
        private void OnDisable()
        {
            CancelSpeech();
            if (bubbles != null)
            {
                bubbles.RenderEnabled = false;
                foreach (EastGuardActor actor in actors) if (actor != null) bubbles.WithdrawSpeaker(actor.Motion);
            }
        }
        private void OnDestroy() { OnDisable(); distancePresentation?.Dispose(); }

        private sealed class Deck
        {
            private readonly int[] order;
            private readonly System.Random random;
            private int cursor, previous = -1;
            public Deck(int count, int seed) { order = new int[count]; random = new System.Random(seed); Shuffle(); }
            public int Peek => order[cursor];
            public void Commit() { previous = Peek; if (++cursor == order.Length) Shuffle(); }
            private void Shuffle()
            {
                for (int i = 0; i < order.Length; i++) order[i] = i;
                for (int i = order.Length - 1; i > 0; i--)
                { int j = random.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
                if (order[0] == previous) { int j = 1 + random.Next(order.Length - 1); (order[0], order[j]) = (order[j], order[0]); }
                cursor = 0;
            }
        }
    }
}
