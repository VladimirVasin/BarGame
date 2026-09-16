using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// The three shelter residents share one local, pause-aware bubble channel
    /// the way the port crew does: a pure schedule picks authored pairs, this
    /// owner hangs each line over its real head through the shared view and
    /// reports the shared delivery's line length back. Nobody here reads or
    /// addresses the hero; he is only the listener whose distance decides
    /// whether anything is said at all.
    /// </summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class CityArchShelterConversationController : MonoBehaviour
    {
        public const float EarshotMeters = NpcEarshotProfile.ConversationFaintRadiusMeters;
        private static readonly string[] voices =
        {
            NpcVoiceCatalog.WatchmanDesignId,
            NpcVoiceCatalog.CheckersPlayerDesignId,
            NpcVoiceCatalog.CafeHusbandDesignId
        };
        private readonly Object[] owners = new Object[CityArchShelterConversationCatalog.RoleCount];
        private readonly Transform[] heads = new Transform[CityArchShelterConversationCatalog.RoleCount];
        private Transform listener;
        private Camera worldCamera;
        private NpcSpeechBubbleView bubbles;
        private CityArchShelterConversationSchedule schedule;
        private int shownSerial = -1;
        private bool hasVisibleLine;

        public bool IsInitialized { get; private set; }
        public double LifeSeconds { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public NpcSpeechBubbleView Bubbles => bubbles;
        public CityArchShelterConversationSchedule Schedule => schedule;
        public string LastLineKey { get; private set; } = string.Empty;
        public int LastSpeakerRole { get; private set; } = -1;
        public int StartedLineCount => schedule?.StartedLineCount ?? 0;
        public Object Speaker(int role) => owners[role];
        public Transform Head(int role) => heads[role];

        /// <summary>Raised on the built shelter root once the camera exists; absent shelters get no channel.</summary>
        public static CityArchShelterConversationController Create(
            CityArchShelterWorldResult world, Transform hero, Camera camera, int seed)
        {
            if (world == null || world.Root == null) return null;
            var controller = world.Root.AddComponent<CityArchShelterConversationController>();
            controller.Initialize(world.ResidentRoots, hero, camera, seed);
            return controller;
        }

        public void Initialize(IReadOnlyList<Transform> residentRoots, Transform hero, Camera camera, int seed)
        {
            if (residentRoots == null) throw new ArgumentNullException(nameof(residentRoots));
            if (IsInitialized) throw new InvalidOperationException("The shelter conversation channel is already initialized.");
            for (int index = 0; index < residentRoots.Count; index++)
            {
                Transform root = residentRoots[index];
                if (root == null) continue;
                var registry = root.GetComponentInChildren<CityArchShelterResidentAssetRegistry>(true);
                var presentation = root.GetComponentInChildren<CityArchShelterResidentPresentation>(true);
                if (registry == null || presentation == null || registry.Head == null) continue;
                int role = ResolveRole(registry.Role);
                if (owners[role] != null)
                    throw new InvalidOperationException($"The shelter has two {registry.Role} residents.");
                owners[role] = presentation;
                heads[role] = registry.Head;
            }
            if (owners[CityArchShelterConversationCatalog.StandingRole] == null ||
                owners[CityArchShelterConversationCatalog.SeatedRole] == null)
                throw new InvalidOperationException("The shelter conversation needs both warmers.");
            listener = hero;
            worldCamera = camera;
            schedule = new CityArchShelterConversationSchedule(seed ^ 0x5348454C);
            bubbles = GetComponent<NpcSpeechBubbleView>();
            if (bubbles == null) bubbles = gameObject.AddComponent<NpcSpeechBubbleView>();
            bubbles.UseManualClock = true;
            bubbles.Initialize(camera, hero);
            IsInitialized = true;
            DeclareSpeakers();
        }

        private static int ResolveRole(CityArchShelterResidentRole role)
        {
            switch (role)
            {
                case CityArchShelterResidentRole.StandingWarmer: return CityArchShelterConversationCatalog.StandingRole;
                case CityArchShelterResidentRole.SeatedWarmer: return CityArchShelterConversationCatalog.SeatedRole;
                case CityArchShelterResidentRole.Sleeper: return CityArchShelterConversationCatalog.SleeperRole;
                default: throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported shelter resident.");
            }
        }

        private void LateUpdate()
        {
            if (!IsInitialized) return;
            bool paused = GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning;
            bubbles.RenderEnabled = !paused;
            if (AutoAdvance && !paused) Advance(Time.deltaTime);
        }

        /// <summary>Same bounded step in gameplay and the focused proofs; pause freezes all owned time.</summary>
        public void Advance(float deltaTime)
        {
            if (!IsInitialized || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused ||
                !GameSessionState.IsGameTimeRunning || deltaTime <= 0f) return;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            // A hitch cannot replay a missed line or burst through a pause.
            LifeSeconds += Mathf.Min(deltaTime, .1f);
            if (worldCamera == null) worldCamera = Camera.main;
            if (!bubbles.IsDeclared(owners[0])) DeclareSpeakers();
            bubbles.AdvanceTo((float)LifeSeconds);

            int available = 0;
            for (int role = 0; role < owners.Length; role++)
            {
                Transform head = heads[role];
                if (owners[role] == null || head == null || !head.gameObject.activeInHierarchy) continue;
                if (listener != null && Vector3.SqrMagnitude(head.position - listener.position) <= EarshotMeters * EarshotMeters)
                    available |= 1 << role;
            }
            bool audible = listener != null && available != 0 &&
                !BarMinigameModalLock.IsAnyLocked && !SceneTransitionService.IsTransitioning;

            // The schedule owns order and timing; the shared delivery owns how
            // long each localized line stays up, measured here per line.
            double first = 0d, second = 0d;
            CityArchShelterConversationTurn current = schedule.Current;
            if (current.HasExchange)
            {
                first = ResolveLineSeconds(current.Exchange.FirstKey);
                second = ResolveLineSeconds(current.Exchange.SecondKey);
            }
            CityArchShelterConversationTurn turn = schedule.Advance(LifeSeconds, available, audible, first, second);
            if (!turn.IsSpeaking)
            {
                if (hasVisibleLine) bubbles.DismissAll();
                hasVisibleLine = false;
            }
            else if (shownSerial != turn.LineSerial)
            {
                bubbles.DismissAll();
                Object speaker = owners[turn.SpeakerRole];
                string text = LocalizationService.Get(turn.LineKey);
                float duration = (float)ResolveLineSeconds(turn.LineKey);
                if (bubbles.ShowAt(speaker, text, (float)LifeSeconds, duration))
                {
                    hasVisibleLine = true;
                    LastLineKey = turn.LineKey;
                    LastSpeakerRole = turn.SpeakerRole;
                }
                shownSerial = turn.LineSerial;
            }
        }

        public static double ResolveLineSeconds(string key)
        {
            string text = LocalizationService.Get(key);
            return Mathf.Max(NpcSpeechBubbleView.VisibleSeconds,
                SpeechDelivery.ResolveSpokenDuration(text, SpeechDelivery.ReadingTailSeconds));
        }

        private void DeclareSpeakers()
        {
            if (!IsInitialized || bubbles == null) return;
            for (int role = 0; role < owners.Length; role++)
            {
                if (owners[role] == null || heads[role] == null) continue;
                bubbles.DeclareSpeaker(owners[role], heads[role], voices[role], NpcEarshotProfile.Conversation);
            }
        }

        private void OnEnable() { DeclareSpeakers(); }

        private void OnDisable()
        {
            schedule?.Reset();
            shownSerial = -1;
            hasVisibleLine = false;
            if (bubbles == null) return;
            bubbles.DismissAll();
            bubbles.RenderEnabled = false;
            for (int role = 0; role < owners.Length; role++)
                if (owners[role] != null) bubbles.WithdrawSpeaker(owners[role]);
        }
    }
}
