using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Three guest pairs and one service voice share bounded speech views; solitary tables stay silent.</summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class BarConversationController : MonoBehaviour
    {
        // Three service slots plus bounded completed-order headroom; guests never hold up this channel.
        public const int PendingCommentCapacity = BarDrinkShopController.MaximumPendingServiceOrders * 2 + 1;
        public const int BartenderRole = BarConversationCatalog.RoleCount;
        public const int MaximumSimultaneousLines = BarConversationCatalog.ActiveGroupCount + 1;
        private const int BartenderChannel = BarConversationCatalog.KindCount;
        private struct ActiveLine
        {
            public bool IsActive;
            public int Role;
            public string Key;
            public double Started;
            public float Duration;
            public bool Acknowledged;
        }
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
        private readonly BarPatron[] refreshedPatrons = new BarPatron[BartenderRole];
        private readonly Comment[] comments = new Comment[PendingCommentCapacity];
        private readonly BarSpeechVariantDeck[] commentDecks = new BarSpeechVariantDeck[BarBartenderCommentCatalog.KindCount];
        private Transform listener;
        private Transform roomSpace;
        private Rect hallBounds;
        private float hallHeight;
        private BarDrinkShopController shop;
        private BarKaraokeInteraction karaoke;
        private int commentCount;
        private long lastAcceptedId;
        private uint random;
        private static int visitOrdinal;
        private int cachedGroups;
        private double nextAdmission;
        private bool subscribed;
        // Preserve catalog channel indices, including the dormant high-table
        // bank, without admitting that channel to the live presentation.
        private readonly ActiveLine[] lines = new ActiveLine[BarConversationCatalog.KindCount + 1];
        private readonly BarConversationSchedule[] schedules = new BarConversationSchedule[BarConversationCatalog.KindCount];
        private long activeOrder;
        private int activeVariant;
        private BarBartenderCommentKind activeCommentKind;

        public bool IsInitialized { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public double LifeSeconds { get; private set; }
        public NpcSpeechBubbleView GuestBubbles { get; private set; }
        public NpcSpeechBubbleView BartenderBubbles { get; private set; }
        public int PendingCommentCount => commentCount;
        public int StartedLineCount { get; private set; }
        public string LastLineKey { get; private set; } = string.Empty;
        public int LastSpeakerRole { get; private set; } = -1;
        public int ActiveLineCount
        {
            get
            {
                int count = 0;
                for (int channel = 0; channel < lines.Length; channel++)
                    if (lines[channel].IsActive && View(lines[channel].Role).IsShowing(owners[lines[channel].Role])) count++;
                return count;
            }
        }
        public event Action<int, string> LineStarted;
        public BarConversationSchedule GetSchedule(BarConversationKind kind)
        {
            int group = (int)kind;
            if (group < 0 || group >= schedules.Length) throw new ArgumentOutOfRangeException(nameof(kind));
            return schedules[group];
        }
        public Object Speaker(int role) => owners[role];
        public Transform Head(int role) => heads[role];
        public void ConfigureKaraoke(BarKaraokeInteraction interaction) => karaoke = interaction;

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
            for (int group = 0; group < schedules.Length; group++)
                schedules[group] = new BarConversationSchedule(unchecked(seed + group * 15427), (BarConversationKind)group);
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
            RefreshPatrons(patrons);
            Connect();
        }

        /// <summary>Only settled guests speak; changing one place releases its pair without resetting other channels.</summary>
        public void RefreshPatrons(IReadOnlyList<BarPatron> patrons)
        {
            if (!IsInitialized) throw new InvalidOperationException("Initialize the bar speech channel before refreshing its cast.");
            if (patrons == null) throw new ArgumentNullException(nameof(patrons));
            Array.Clear(refreshedPatrons, 0, refreshedPatrons.Length);
            // Absence is valid. A present guest with a broken rig is still an authoring error.
            for (int role = 0; role < BartenderRole; role++)
            {
                for (int index = 0; index < patrons.Count; index++)
                {
                    BarPatron found = patrons[index];
                    if (found == null) throw new ArgumentException("The bar cast contains a null guest.", nameof(patrons));
                    if (found.Anchor.Id != BarConversationCatalog.RoleId(role)) continue;
                    if (refreshedPatrons[role] != null)
                        throw new InvalidOperationException("Two visitors occupy " + BarConversationCatalog.RoleId(role));
                    if (found.Registry == null || found.Presentation == null || found.Registry.HeadAnchor == null || found.Drinking == null)
                        throw new InvalidOperationException("Bar speech requires the actual visitor at " + BarConversationCatalog.RoleId(role));
                    refreshedPatrons[role] = found;
                }
            }

            int changedRoles = 0;
            for (int role = 0; role < BartenderRole; role++)
            {
                BarPatron found = refreshedPatrons[role];
                if (!ReferenceEquals(owners[role], found?.Presentation) ||
                    !ReferenceEquals(heads[role], found?.Registry.HeadAnchor)) changedRoles |= 1 << role;
            }
            for (int group = 0; group < schedules.Length; group++)
            {
                if ((changedRoles & (3 << (group * 2))) == 0) continue;
                EndLine(group);
                schedules[group].Cancel(LifeSeconds);
                ReservePartners(group, false);
                cachedGroups &= ~(1 << group);
            }
            for (int role = 0; role < BartenderRole; role++)
            {
                if ((changedRoles & (1 << role)) == 0) continue;
                GuestBubbles.WithdrawSpeaker(owners[role]);
                BarPatron found = refreshedPatrons[role];
                owners[role] = found?.Presentation;
                heads[role] = found?.Registry.HeadAnchor;
                voices[role] = found?.Registry.DesignId;
                drinkers[role] = found?.Drinking;
                if (subscribed) DeclareRole(role);
            }
            Array.Clear(refreshedPatrons, 0, refreshedPatrons.Length);
            if (changedRoles != 0) nextAdmission = LifeSeconds;
        }

        /// <summary>A departure waits for the whole owned exchange, including its reply gap.</summary>
        public bool CanPatronLeave(BarPatron patron)
        {
            if (patron == null) return false;
            for (int role = 0; role < BartenderRole; role++)
                if (ReferenceEquals(owners[role], patron.Presentation))
                    return !lines[role / 2].IsActive && !schedules[role / 2].Current.HasExchange;
            return true;
        }

        private void DeclareRole(int role)
        {
            if (owners[role] == null || role != BartenderRole && !BarConversationCatalog.CanSpeak(role)) return;
            if (!View(role).DeclareSpeaker(owners[role], heads[role], voices[role], NpcEarshotProfile.Conversation))
                throw new InvalidOperationException("The shared bar speech view refused role " + role);
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
                DeclareRole(role);
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
            // A missing partner is checked before reveal, so it cannot spend a
            // line on the frame its head or hearing disappears.
            for (int channel = 0; channel < lines.Length; channel++)
            {
                ActiveLine line = lines[channel];
                if (line.IsActive && (!SpeakerAudible(line.Role) || channel < BartenderChannel &&
                    (!schedules[channel].Current.HasExchange || !PairReady(channel)))) EndLine(channel);
            }
            GuestBubbles.AdvanceTo((float)LifeSeconds);
            BartenderBubbles.AdvanceTo((float)LifeSeconds);
            for (int channel = 0; channel < lines.Length; channel++)
            {
                ActiveLine line = lines[channel];
                if (line.IsActive && !line.Acknowledged &&
                    View(line.Role).TryGetSpeechFaceSample(owners[line.Role], out SpeechFaceSample firstGlyph) &&
                    firstGlyph.RevealedCharacters > 0) AcknowledgeLine(channel);
            }
            if (!SpeakerAudible(BartenderRole) && commentCount > 0)
            {
                EndLine(BartenderChannel);
                Array.Clear(comments, 0, commentCount);
                commentCount = 0;
            }
            if (LifeSeconds >= nextAdmission)
            {
                cachedGroups = 0;
                for (int group = 0; group < schedules.Length; group++)
                    if (PairReady(group)) cachedGroups |= 1 << group;
                nextAdmission = LifeSeconds + .25d;
            }
            for (int group = 0; group < schedules.Length; group++)
            {
                if (!BarConversationCatalog.IsEnabled((BarConversationKind)group)) continue;
                BarConversationSchedule schedule = schedules[group];
                int eligible = cachedGroups & (1 << group);
                if (schedule.Current.HasExchange && !PairReady(group)) eligible = 0;
                // Finish an owned exchange at closing, then leave its rest free for departure.
                if (!schedule.Current.HasExchange && !BarBusinessHours.IsOpen(GameSessionState.GameMinuteOfDay)) eligible = 0;
                // Service has its own lane. A queue never silences visitors,
                // and the other pairs cannot delay this pair's answer.
                BarConversationTurn turn = schedule.Advance(LifeSeconds, eligible, true);
                ActiveLine line = lines[group];
                if (line.IsActive && (!turn.HasExchange || !GuestBubbles.IsShowing(owners[line.Role]))) EndLine(group);
                if (!lines[group].IsActive && turn.NeedsLineStart) BeginLine(turn.SpeakerRole, turn.LineKey);
                ReservePartners(group, turn.HasExchange);
            }
            ActiveLine bartender = lines[BartenderChannel];
            if (bartender.IsActive && !BartenderBubbles.IsShowing(owners[BartenderRole])) EndLine(BartenderChannel);
            if (!lines[BartenderChannel].IsActive) TryStartComment();
        }

        private bool ListenerInHall()
        {
            bool karaokeOwns = karaoke != null && karaoke.OwnsInteraction;
            if (listener == null || !listener.gameObject.activeInHierarchy || roomSpace == null ||
                SceneTransitionService.IsTransitioning ||
                BarMinigameModalLock.IsAnyLocked && !karaokeOwns ||
                karaokeOwns && karaoke.IsPerforming) return false;
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
            if (!BarConversationCatalog.IsEnabled((BarConversationKind)group)) return false;
            int first = group * 2, second = first + 1;
            return SpeakerAudible(first) && SpeakerAudible(second) &&
                   drinkers[first] != null && drinkers[second] != null &&
                   drinkers[first].CanConverse && drinkers[second].CanConverse;
        }

        private void ReservePartners(int group, bool participating)
        {
            for (int role = group * 2; role < group * 2 + 2; role++)
                if (drinkers[role] != null)
                    drinkers[role].SetConversationPartner(participating ? heads[role ^ 1] : null);
        }

        private NpcSpeechBubbleView View(int role) => role == BartenderRole ? BartenderBubbles : GuestBubbles;

        private bool BeginLine(int role, string key)
        {
            if (role != BartenderRole && !BarConversationCatalog.CanSpeak(role)) return false;
            int channel = role == BartenderRole ? BartenderChannel : role / 2;
            if (lines[channel].IsActive || !SpeakerAudible(role) || NpcSpeechBubbleView.IsPresentingAt(heads[role])) return false;
            string text = LocalizationService.Get(key);
            float duration = Mathf.Max(NpcSpeechBubbleView.VisibleSeconds,
                SpeechDelivery.ResolveSpokenDuration(text, SpeechDelivery.ReadingTailSeconds));
            if (!View(role).ShowAt(owners[role], text, (float)LifeSeconds, duration)) return false;
            lines[channel] = new ActiveLine
            {
                IsActive = true, Role = role, Key = key, Started = LifeSeconds, Duration = duration
            };
            return true;
        }

        private void AcknowledgeLine(int channel)
        {
            ActiveLine line = lines[channel];
            if (channel == BartenderChannel)
            {
                commentDecks[(int)activeCommentKind].Commit(activeVariant);
                RemoveComment(activeOrder);
            }
            else if (!schedules[channel].StartLine(LifeSeconds, Math.Max(.001d, line.Duration - (LifeSeconds - line.Started))))
            {
                EndLine(channel);
                return;
            }
            lines[channel].Acknowledged = true;
            StartedLineCount++;
            LastLineKey = line.Key;
            LastSpeakerRole = line.Role;
            LineStarted?.Invoke(line.Role, line.Key);
        }

        private void EndLine(int channel)
        {
            ActiveLine line = lines[channel];
            if (!line.IsActive) return;
            View(line.Role).Dismiss(owners[line.Role]);
            lines[channel] = default;
            if (channel == BartenderChannel) activeOrder = 0;
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
                    if (lines[BartenderChannel].IsActive && activeOrder == order.Id && !lines[BartenderChannel].Acknowledged) EndLine(BartenderChannel);
                    comments[index].Delivered = comments[index].AtDelivery = true;
                    comments[index].Vessel = order.IsHero ? shop.HeroServiceVessel :
                        order.Recipient != null ? order.Recipient.GetComponent<BarCounterPatronRefill>()?.Vessel : null;
                    return;
                }
        }

        private void OrderCancelled(BarServiceOrder order)
        {
            if (lines[BartenderChannel].IsActive && activeOrder == order.Id) EndLine(BartenderChannel);
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
            for (int channel = 0; channel < lines.Length; channel++) EndLine(channel);
            for (int group = 0; group < schedules.Length; group++)
            {
                schedules[group].Cancel(LifeSeconds);
                ReservePartners(group, false);
            }
            GuestBubbles.DismissAll();
            BartenderBubbles.DismissAll();
            Array.Clear(comments, 0, commentCount);
            commentCount = cachedGroups = 0;
            nextAdmission = LifeSeconds;
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
