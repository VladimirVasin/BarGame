using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>One bar channel over the shared speech views; service never waits for speech.</summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class BarConversationController : MonoBehaviour
    {
        // Three service slots plus completed orders waiting behind one A→B exchange.
        public const int PendingCommentCapacity = BarDrinkShopController.MaximumPendingServiceOrders * 2 + 1;
        public const int BartenderRole = BarConversationCatalog.RoleCount;
        private struct Comment
        {
            public BarServiceOrder Order;
            public bool AtDelivery;
            public bool Delivered;
            public BarDrinkVesselView Vessel;
        }

        private readonly Object[] owners = new Object[BartenderRole + 1];
        private readonly Transform[] heads = new Transform[BartenderRole + 1];
        private readonly BarPatronDrinkingArmPose[] drinkers = new BarPatronDrinkingArmPose[BartenderRole];
        private readonly string[] voices = new string[BartenderRole + 1];
        private readonly Comment[] comments = new Comment[PendingCommentCapacity];
        private readonly BarSpeechVariantDeck[] commentDecks = new BarSpeechVariantDeck[BarBartenderCommentCatalog.KindCount];
        private Transform listener;
        private Transform roomSpace;
        private Rect hallBounds;
        private float hallHeight;
        private BarDrinkShopController shop;
        private int commentCount;
        private long lastAcceptedId;
        private uint random;
        private static int visitOrdinal;
        private int cachedGroups;
        private double nextAdmission;
        private bool subscribed;
        private int activeRole = -1;
        private string activeKey;
        private double lineStarted;
        private float lineDuration;
        private bool lineAcknowledged;
        private long activeOrder;
        private int activeVariant;
        private BarBartenderCommentKind activeCommentKind;

        public bool IsInitialized { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public double LifeSeconds { get; private set; }
        public BarConversationSchedule Schedule { get; private set; }
        public NpcSpeechBubbleView GuestBubbles { get; private set; }
        public NpcSpeechBubbleView BartenderBubbles { get; private set; }
        public int PendingCommentCount => commentCount;
        public int StartedLineCount { get; private set; }
        public string LastLineKey { get; private set; } = string.Empty;
        public int LastSpeakerRole { get; private set; } = -1;
        public int ActiveLineCount => activeRole >= 0 && View(activeRole).IsShowing(owners[activeRole]) ? 1 : 0;
        public Object Speaker(int role) => owners[role];
        public Transform Head(int role) => heads[role];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVisits() => visitOrdinal = 0;

        public void Initialize(IReadOnlyList<BarPatron> patrons, BarBartenderPresentation bartender,
            BarDrinkShopController drinkShop, Transform hero, Camera camera, Transform space,
            BarInteriorLayoutPlan layout, int seed)
        {
            if (IsInitialized) throw new InvalidOperationException("The bar speech channel is already initialized.");
            if (patrons == null || bartender == null || drinkShop == null || hero == null || camera == null || space == null || layout == null)
                throw new ArgumentNullException("Bar speech requires the built cast, service, room and listener.");
            listener = hero;
            roomSpace = space;
            hallBounds = layout.RoomBounds;
            hallHeight = layout.RoomHeight;
            shop = drinkShop;
            seed = unchecked(seed ^ ++visitOrdinal * 48611);
            random = unchecked((uint)seed) | 1u;
            Schedule = new BarConversationSchedule(seed);
            for (int role = 0; role < BartenderRole; role++)
            {
                BarPatron found = null;
                for (int index = 0; index < patrons.Count; index++)
                    if (patrons[index].Anchor.Id == BarConversationCatalog.RoleId(role)) { found = patrons[index]; break; }
                if (found?.Registry == null || found.Registry.HeadAnchor == null || found.Drinking == null)
                    throw new InvalidOperationException("Bar speech requires the actual visitor at " + BarConversationCatalog.RoleId(role));
                owners[role] = found.Presentation;
                heads[role] = found.Registry.HeadAnchor;
                voices[role] = found.Registry.DesignId;
                drinkers[role] = found.Drinking;
            }
            owners[BartenderRole] = bartender;
            heads[BartenderRole] = bartender.Registry.Head;
            voices[BartenderRole] = bartender.Registry.DesignId;
            if (heads[BartenderRole] == null) throw new InvalidOperationException("The bartender needs his real speech anchor.");
            for (int kind = 0; kind < commentDecks.Length; kind++)
                commentDecks[kind] = new BarSpeechVariantDeck(BarBartenderCommentCatalog.Count((BarBartenderCommentKind)kind),
                    unchecked(seed + 104729 * (kind + 1)));
            GuestBubbles = CreateView("Visitor Speech", camera);
            BartenderBubbles = CreateView("Bartender Speech", camera);
            IsInitialized = true;
            Connect();
        }

        private NpcSpeechBubbleView CreateView(string name, Camera camera)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            var view = root.AddComponent<NpcSpeechBubbleView>();
            view.UseManualClock = true;
            view.Initialize(camera, listener);
            return view;
        }

        private void Connect()
        {
            if (!IsInitialized || subscribed || shop == null) return;
            for (int role = 0; role < owners.Length; role++)
                if (!View(role).DeclareSpeaker(owners[role], heads[role], voices[role], NpcEarshotProfile.Conversation))
                    throw new InvalidOperationException("The shared bar speech view refused role " + role);
            shop.ServiceOrderAccepted += OrderAccepted;
            shop.ServiceOrderDelivered += OrderDelivered;
            shop.ServiceOrderCancelled += OrderCancelled;
            subscribed = true;
            for (int index = 0; index < shop.PendingServiceOrderCount; index++) OrderAccepted(shop.GetPendingServiceOrder(index));
        }

        private void LateUpdate()
        {
            if (!IsInitialized) return;
            bool paused = GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning;
            GuestBubbles.RenderEnabled = BartenderBubbles.RenderEnabled = !paused;
            if (AutoAdvance && !paused) Advance(Time.deltaTime);
        }

        /// <summary>Manual stepping uses the same clock and admission as gameplay.</summary>
        public void Advance(float deltaTime)
        {
            if (!IsInitialized || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning) return;
            if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (deltaTime == 0f) return;
            LifeSeconds += deltaTime;
            if (!ListenerInHall() || deltaTime > BarConversationSchedule.MaximumContinuousStepSeconds)
            {
                CancelSpeech();
                return;
            }
            if (activeRole >= 0 && (!SpeakerAudible(activeRole) || activeRole < BartenderRole &&
                (!Schedule.Current.HasExchange || !PairReady((int)Schedule.Current.Exchange.Kind) ||
                 !Schedule.HasCommittedExchange && commentCount > 0)))
                EndLine();
            GuestBubbles.AdvanceTo((float)LifeSeconds);
            BartenderBubbles.AdvanceTo((float)LifeSeconds);
            // Commit the first glyph before service can cancel anticipation.
            if (activeRole >= 0 && !lineAcknowledged &&
                View(activeRole).TryGetSpeechFaceSample(owners[activeRole], out SpeechFaceSample firstGlyph) &&
                firstGlyph.RevealedCharacters > 0) AcknowledgeLine();
            if (!SpeakerAudible(BartenderRole) && commentCount > 0)
            {
                if (activeRole == BartenderRole) EndLine();
                Array.Clear(comments, 0, commentCount);
                commentCount = 0;
            }
            if (LifeSeconds >= nextAdmission)
            {
                cachedGroups = 0;
                for (int group = 0; group < BarConversationCatalog.KindCount; group++)
                    if (PairReady(group)) cachedGroups |= 1 << group;
                nextAdmission = LifeSeconds + .25d;
            }
            BarConversationTurn previous = Schedule.Current;
            int eligible = cachedGroups;
            if (previous.HasExchange && !PairReady((int)previous.Exchange.Kind))
                eligible &= ~(1 << (int)previous.Exchange.Kind);
            BarConversationTurn turn = Schedule.Advance(LifeSeconds, eligible, true, commentCount > 0 || activeRole == BartenderRole);
            if (activeRole >= 0)
            {
                if (!SpeakerAudible(activeRole) || activeRole < BartenderRole && !turn.HasExchange)
                    EndLine();
                else if (!View(activeRole).IsShowing(owners[activeRole])) EndLine();
            }
            // A queued purchase cannot cut between A and its answer.
            if (activeRole < 0 && !Schedule.HasCommittedExchange && TryStartComment())
                turn = Schedule.Current;
            else if (activeRole < 0 && turn.NeedsLineStart)
                BeginLine(turn.SpeakerRole, turn.LineKey);
            ReservePartners(Schedule.Current);
        }

        private bool ListenerInHall()
        {
            if (listener == null || !listener.gameObject.activeInHierarchy || roomSpace == null ||
                SceneTransitionService.IsTransitioning || BarMinigameModalLock.IsAnyLocked) return false;
            Vector3 local = roomSpace.InverseTransformPoint(listener.position);
            // Even the open doorway does not start conversations from inside the WC.
            return local.y >= -.5f && local.y <= hallHeight && BarWashroomLayout.Contains(hallBounds, local) &&
                   !BarWashroomLayout.Contains(BarWashroomLayout.Bounds, local);
        }

        private bool SpeakerAudible(int role) => owners[role] != null && heads[role] != null &&
            heads[role].gameObject.activeInHierarchy && listener != null &&
            (heads[role].position - listener.position).sqrMagnitude <=
                NpcEarshotProfile.ConversationFaintRadiusMeters * NpcEarshotProfile.ConversationFaintRadiusMeters;

        private bool PairReady(int group)
        {
            int first = group * 2, second = first + 1;
            return SpeakerAudible(first) && SpeakerAudible(second) &&
                   drinkers[first] != null && drinkers[second] != null &&
                   drinkers[first].CanConverse && drinkers[second].CanConverse;
        }

        private void ReservePartners(BarConversationTurn turn)
        {
            for (int role = 0; role < drinkers.Length; role++)
            {
                if (drinkers[role] == null) continue;
                bool participating = turn.HasExchange && role / 2 == (int)turn.Exchange.Kind;
                drinkers[role].SetConversationPartner(participating ? heads[role ^ 1] : null);
            }
        }

        private NpcSpeechBubbleView View(int role) => role == BartenderRole ? BartenderBubbles : GuestBubbles;

        private bool BeginLine(int role, string key)
        {
            if (!SpeakerAudible(role) || NpcSpeechBubbleView.IsPresentingAt(heads[role])) return false;
            string text = LocalizationService.Get(key);
            float duration = Mathf.Max(NpcSpeechBubbleView.VisibleSeconds,
                SpeechDelivery.ResolveSpokenDuration(text, SpeechDelivery.ReadingTailSeconds));
            if (!View(role).ShowAt(owners[role], text, (float)LifeSeconds, duration)) return false;
            activeRole = role;
            activeKey = key;
            lineStarted = LifeSeconds;
            lineDuration = duration;
            lineAcknowledged = false;
            return true;
        }

        private void AcknowledgeLine()
        {
            if (activeRole == BartenderRole)
            {
                commentDecks[(int)activeCommentKind].Commit(activeVariant);
                RemoveComment(activeOrder);
            }
            else if (!Schedule.StartLine(LifeSeconds, Math.Max(.001d, lineDuration - (LifeSeconds - lineStarted))))
            {
                EndLine();
                return;
            }
            lineAcknowledged = true;
            StartedLineCount++;
            LastLineKey = activeKey;
            LastSpeakerRole = activeRole;
        }

        private void EndLine()
        {
            if (activeRole < 0) return;
            bool service = activeRole == BartenderRole;
            View(activeRole).Dismiss(owners[activeRole]);
            activeRole = -1;
            activeKey = null;
            activeOrder = 0;
            if (service) Schedule.DeferAfterService(LifeSeconds);
        }

        private void OrderAccepted(BarServiceOrder order)
        {
            if (order.Id <= lastAcceptedId) return;
            lastAcceptedId = order.Id;
            if (!isActiveAndEnabled || !ListenerInHall() || !SpeakerAudible(BartenderRole)) return;
            if (commentCount == comments.Length)
                throw new InvalidOperationException("Bar speech exceeded its service/cadence bound.");
            // A majority of orders get a drink-specific delivery comment; the
            // remaining accepted/repeat confirmations have independent decks.
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            comments[commentCount++] = new Comment { Order = order, AtDelivery = random % 3u != 0u };
        }

        private void OrderDelivered(BarServiceOrder order)
        {
            for (int index = 0; index < commentCount; index++)
                if (comments[index].Order.Id == order.Id)
                {
                    if (activeRole == BartenderRole && activeOrder == order.Id && !lineAcknowledged) EndLine();
                    comments[index].Delivered = comments[index].AtDelivery = true;
                    comments[index].Vessel = order.IsHero ? shop.HeroServiceVessel :
                        order.Recipient != null ? order.Recipient.GetComponent<BarCounterPatronRefill>()?.Vessel : null;
                    return;
                }
        }

        private void OrderCancelled(BarServiceOrder order)
        {
            if (activeRole == BartenderRole && activeOrder == order.Id) EndLine();
            RemoveComment(order.Id);
        }

        private void RemoveComment(long id)
        {
            for (int index = 0; index < commentCount; index++)
            {
                if (comments[index].Order.Id != id) continue;
                commentCount--;
                for (int next = index; next < commentCount; next++) comments[next] = comments[next + 1];
                comments[commentCount] = default;
                return;
            }
        }

        private bool TryStartComment()
        {
            if (!SpeakerAudible(BartenderRole)) return false;
            for (int index = 0; index < commentCount; index++)
            {
                Comment comment = comments[index];
                if (comment.AtDelivery && !comment.Delivered) continue;
                BarBartenderCommentKind kind = comment.Delivered ? DeliveryKind(comment.Order.DrinkId) :
                    comment.Order.IsRepeat ? BarBartenderCommentKind.Repeat : BarBartenderCommentKind.Accepted;
                // No invented foam state. A handle remark uses the live vessel
                // and its recipient rather than assuming every dock faces right.
                int eligible = 0;
                for (int variant = 0; variant < BarBartenderCommentCatalog.Count(kind); variant++)
                {
                    BarBartenderCommentRequirement requirement = BarBartenderCommentCatalog.Requirements(kind, variant);
                    if (requirement == BarBartenderCommentRequirement.None ||
                        requirement == BarBartenderCommentRequirement.HandleRight && comment.Vessel != null &&
                        comment.Order.Recipient != null && Vector3.Dot(comment.Vessel.HandleDirection, comment.Order.Recipient.right) > .9f)
                        eligible |= 1 << variant;
                }
                if (!commentDecks[(int)kind].TryPeek(eligible, out int selected)) continue;
                if (!BeginLine(BartenderRole, BarBartenderCommentCatalog.Key(kind, selected))) return false;
                activeOrder = comment.Order.Id;
                activeVariant = selected;
                activeCommentKind = kind;
                return true;
            }
            return false;
        }

        private static BarBartenderCommentKind DeliveryKind(DrinkId drink) => drink switch
        {
            DrinkId.LightBeer => BarBartenderCommentKind.Beer,
            DrinkId.RedWine => BarBartenderCommentKind.Wine,
            DrinkId.CognacVs => BarBartenderCommentKind.Distillate,
            DrinkId.Vodka => BarBartenderCommentKind.Vodka,
            _ => BarBartenderCommentKind.Accepted
        };

        private void CancelSpeech()
        {
            EndLine();
            Schedule.Cancel(LifeSeconds);
            GuestBubbles.DismissAll();
            BartenderBubbles.DismissAll();
            Array.Clear(comments, 0, commentCount);
            commentCount = cachedGroups = 0;
            nextAdmission = LifeSeconds;
            ReservePartners(default);
        }

        private void OnEnable() { Connect(); }
        private void OnDisable()
        {
            if (!IsInitialized) return;
            if (subscribed && shop != null)
            {
                shop.ServiceOrderAccepted -= OrderAccepted;
                shop.ServiceOrderDelivered -= OrderDelivered;
                shop.ServiceOrderCancelled -= OrderCancelled;
            }
            subscribed = false;
            CancelSpeech();
            GuestBubbles.RenderEnabled = BartenderBubbles.RenderEnabled = false;
            for (int role = 0; role < owners.Length; role++) View(role).WithdrawSpeaker(owners[role]);
        }
    }
}
