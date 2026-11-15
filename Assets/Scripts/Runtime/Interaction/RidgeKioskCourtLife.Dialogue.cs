using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class RidgeKioskCourtLife
    {
        private const float PendingConversationSeconds = 14f;
        private DialogueSessionController tableDialogue;
        private PlayerInteractor tableHero;
        private PlayerMotor tableMotor;
        private Player3DAssetRegistry tableHeroRig;
        private PlayerAnimatedInteractionController tableAnimation;
        private PlayerVodkaBottleController tableHand;
        private WorldItemFoundScreen treatScreen;
        private Transform preview;
        private KioskTreat pendingTreat;
        private bool previewApproved, openingRound;
        private string requestEndReason;
        private float pendingSeconds;
        private int reservedTable = -1;
        private int reservedDay;

        internal DialogueSessionController TableDialogue => tableDialogue;
        internal bool TableIsPending => reservedTable >= 0;
        internal string RequestEndReason => requestEndReason;

        private void InitializeTableDialogue(PlayerRuntime player, Player3DAssetRegistry rig)
        {
            tableHero = player.Interactor;
            tableMotor = player.Motor;
            tableHeroRig = rig;
            tableAnimation = player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            tableHand = player.GameObject.GetComponent<PlayerVodkaBottleController>();
            tableDialogue = gameObject.AddComponent<DialogueSessionController>();
            treatScreen = WorldItemFoundScreen.For(tableHero);
            for (int table = 0; table < RidgeKioskCourtPlan.TableCount; table++)
                RidgeKioskTableInteraction.Install(this, court.Plan, table);
        }

        private Visit FirstAt(int table) => visits[table * RidgeKioskCourtPlan.PlacesPerTable];
        private Visit SecondAt(int table) => visits[table * RidgeKioskCourtPlan.PlacesPerTable + 1];

        internal int TableRound(int table) =>
            GameSessionState.KioskDialogueRound(table, FirstAt(table).CharacterId, SecondAt(table).CharacterId);

        private bool PairPresent(int table)
        {
            double now = GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes;
            return IsAvailable(FirstAt(table)) && IsAvailable(SecondAt(table));

            bool IsAvailable(Visit visit) => visit.Actor && visit.Actor.IsSpawned && visit.IsAtTable &&
                (visit.Phase == Phase.AtTable || IsReserved(visit) && visit.Gift &&
                    visit.Phase is Phase.Throwing or Phase.TakingGift) &&
                visit.Until > now && visit != aisleTrip;
        }

        private bool IsReserved(Visit visit) => reservedTable >= 0 &&
            (visit == FirstAt(reservedTable) || visit == SecondAt(reservedTable));

        internal bool CanInteractAt(int table, PlayerInteractor player)
        {
            if (!isActiveAndEnabled || player != tableHero || !player.InputEnabled ||
                reservedTable >= 0 || BarMinigameModalLock.IsAnyLocked ||
                SceneTransitionService.IsTransitioning || !GameSessionState.IsGameTimeRunning ||
                !PairPresent(table) || FirstAt(table).Gift || SecondAt(table).Gift || !tableAnimation || tableAnimation.IsActive)
                return false;
            int round = TableRound(table);
            return round < RidgeKioskDialogueCatalog.RoundCount && (round >= 0 ||
                GameSessionState.GameTimeOfDayMinutes < CityResidentLifeRules.KioskLastOrderMinute) &&
                InApproachRange(table);
        }

        private bool InApproachRange(int table)
        {
            Vector3 ground = court.Plan.ConversationGround(table);
            Vector3 offset = hero.position - ground;
            if (Mathf.Abs(offset.y) > .32f || offset.sqrMagnitude > 1.55f * 1.55f) return false;
            return tableMotor.IsInteractionApproachSupported(ground + Vector3.up * PlayerFactory.GroundedRootOffset);
        }

        internal void InteractAt(int table, PlayerInteractor player)
        {
            if (!CanInteractAt(table, player)) return;
            bool newTreat = TableRound(table) < 0;
            if (newTreat && !GameSessionState.TryPrepareKioskTreat(out pendingTreat))
            {
                tableHero.ShowFeedback("kiosk.dialogue.missing", 2f);
                return;
            }
            reservedTable = table;
            requestEndReason = null;
            reservedDay = GameSessionState.GameDayIndex;
            pendingSeconds = PendingConversationSeconds;
            openingRound = newTreat;
            previewApproved = !newTreat;
            if (tableHand && !tableHand.TryHoldContextualStow(this))
            {
                requestEndReason = "drink stow unavailable";
                ReleaseTable();
                return;
            }
            if (!newTreat)
            {
                // This pair has already agreed to talk. Keep that agreement
                // over the next incidental exchange when the hero returns.
                EndSpeechFor(FirstAt(table));
                EndSpeechFor(SecondAt(table));
                return;
            }

            Transform host = new GameObject("Kiosk treat preview").transform;
            preview = host;
            host.SetParent(transform, false);
            host.SetPositionAndRotation(tableHeroRig.Anchors.Chest.position + hero.forward * .25f,
                hero.rotation);
            VodkaBottleView.Create(host, pendingTreat.First).transform.localPosition = Vector3.left * .051f;
            VodkaBottleView.Create(host, pendingTreat.Second).transform.localPosition = Vector3.right * .051f;
            string title = pendingTreat.First != pendingTreat.Second ? "kiosk.dialogue.mixed" :
                pendingTreat.First == InventoryItemId.BeerCan ? "kiosk.dialogue.two_beers" : "kiosk.dialogue.two_vodkas";
            if (!treatScreen || !treatScreen.TryPresentAction(player, pendingTreat.First, preview,
                "kiosk.dialogue.give", title, "kiosk.dialogue.description", "kiosk.dialogue.unavailable",
                ConfirmTreat, FinishPreview))
                ReleaseTable();
        }

        private bool ConfirmTreat() => reservedTable >= 0 && reservedDay == GameSessionState.GameDayIndex &&
            PairPresent(reservedTable) && InApproachRange(reservedTable) &&
            GameSessionState.GameTimeOfDayMinutes < CityResidentLifeRules.KioskLastOrderMinute &&
            GameSessionState.HasKioskTreat(pendingTreat);

        private void FinishPreview(bool confirmed)
        {
            DestroyPreview();
            if (reservedTable < 0) return;
            if (confirmed && ConfirmTreat()) previewApproved = true;
            else ReleaseTable();
        }

        private void Update()
        {
            if (reservedTable < 0 || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            int table = reservedTable;
            if (!tableHero || !tableHero.isActiveAndEnabled || SceneTransitionService.IsTransitioning ||
                reservedDay != GameSessionState.GameDayIndex || !PairPresent(table))
            {
                requestEndReason = "visit ended";
                CloseTable();
                return;
            }
            if (tableDialogue.IsActive) return;
            if (!previewApproved)
            {
                if (!preview || !treatScreen || !treatScreen.isActiveAndEnabled) ReleaseTable();
                return;
            }
            if (!InApproachRange(table) || GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Gameplay) ||
                openingRound && !ConfirmTreat())
            {
                requestEndReason = "approach cancelled";
                ReleaseTable();
                return;
            }
            Visit first = FirstAt(table), second = SecondAt(table);
            bool handOccupied = tableHand && tableHand.isActiveAndEnabled && tableHand.OccupiesHand;
            bool busy = BarMinigameModalLock.IsAnyLocked || !tableHero.InputEnabled || tableAnimation.IsActive;
            bool drinking = !CanTalk(first) || !CanTalk(second);
            bool talking = SpeakingWith(first) || SpeakingWith(second);
            if (busy || handOccupied || drinking || talking)
            {
                if ((pendingSeconds -= Time.deltaTime) <= 0f)
                {
                    requestEndReason = busy ? "player busy" : handOccupied ? "drink still in hand" :
                        drinking ? "visitor still drinking" : "ambient speech did not finish";
                    ReleaseTable();
                }
                return;
            }

            Transform top = court.TableTop(table);
            Vector3 ground = court.Plan.ConversationGround(table);
            Quaternion facing = Quaternion.LookRotation(Vector3.back, Vector3.up);
            Vector3 root = ground + Vector3.up * PlayerFactory.GroundedRootOffset;
            var staging = new DialogueStagingPlan(
                new PlayerAnimatedInteractionPose(root + facing * PlayerDialogueActions.EntryGroundOffset, facing,
                    ground + facing * PlayerDialogueActions.EntryPelvisFromGround),
                ground + facing * PlayerDialogueActions.ActionPelvisFromGround,
                new PlayerAnimatedInteractionPose(root + facing * PlayerDialogueActions.ExitGroundOffset, facing,
                    ground + facing * PlayerDialogueActions.ExitPelvisFromGround));
            RidgeKioskTreatPresentation presentation = null;
            if (openingRound)
            {
                VodkaBottleView a = null, b = null;
                try
                {
                    a = CreateGift(pendingTreat.First, table, 0);
                    b = CreateGift(pendingTreat.Second, table, 1);
                    a.gameObject.SetActive(false);
                    b.gameObject.SetActive(false);
                    presentation = new RidgeKioskTreatPresentation(this, tableHeroRig, hero, top,
                        BarPatronDrinkProp.FromContainer(a), BarPatronDrinkProp.FromContainer(b),
                        () => CommitTreat(table, first, second, a, b),
                        () => first.Container == a && second.Container == b);
                }
                catch
                {
                    if (a) Destroy(a.gameObject);
                    if (b) Destroy(b.gameObject);
                    ReleaseTable();
                    throw;
                }
            }
            int round = TableRound(table);
            if (presentation is { IsReady: false } || !tableDialogue.BeginGroup(
                RidgeKioskDialogueCatalog.ForTable(table), staging, BindSpeaker(first), BindSpeaker(second), tableHero, top,
                round > 0 ? RidgeKioskDialogueCatalog.RoundEntry(round) : null,
                SetConversationFocus, CompleteRound, ReleaseTable, presentation))
            {
                requestEndReason = "shared session refused";
                presentation?.Release();
                ReleaseTable();
            }
            else
            {
                if (firstSpeaker != null) CancelExchange();
                SetConversationFocus(DialogueSpeaker.Hero);
            }
        }

        private static bool CanTalk(Visit visit) => !visit.Drinking || visit.Drinking.CanConverse;

        private static DialogueParticipant BindSpeaker(Visit visit)
        {
            CityPedestrianPresentation body = visit.Actor.Presentation;
            return new DialogueParticipant(NpcSpeaker.FromRegistry(body, body.Registry, NpcEarshotProfile.Conversation),
                visit.Actor.transform, visit.Face, SpeechFaceProfile.Foreman);
        }

        private void SetConversationFocus(DialogueSpeaker speaker)
        {
            if (reservedTable < 0) return;
            Visit first = FirstAt(reservedTable), second = SecondAt(reservedTable);
            var focus = speaker == DialogueSpeaker.Hero ? tableDialogue.HeroHead : speaker == DialogueSpeaker.Npc ? first.Head : second.Head;
            Aim(first, focus == first.Head ? tableDialogue.HeroHead : focus);
            Aim(second, focus == second.Head ? tableDialogue.HeroHead : focus);

            static void Aim(Visit visitor, Transform target)
            {
                if (visitor.Drinking) visitor.Drinking.SetConversationPartner(target);
                else visitor.Actor.Presentation.SetAttentionFocus(target.position);
            }
        }

        private void CompleteRound(DialogueNode next)
        {
            if (reservedTable < 0) return;
            int completed = next.Kind == DialogueNodeKind.End ? RidgeKioskDialogueCatalog.RoundCount :
                next.Id == RidgeKioskDialogueCatalog.RoundEntry(1) ? 1 :
                next.Id == RidgeKioskDialogueCatalog.RoundEntry(2) ? 2 : 0;
            if (completed == 0) return;
            GameSessionState.AdvanceKioskDialogue(reservedTable, FirstAt(reservedTable).CharacterId,
                SecondAt(reservedTable).CharacterId, completed);
        }

        private bool CommitTreat(int table, Visit first, Visit second, VodkaBottleView a, VodkaBottleView b)
        {
            if (reservedTable != table || !PairPresent(table) || first.Gift || second.Gift ||
                !CanDiscard(first) || !CanDiscard(second) ||
                !GameSessionState.TryGiveKioskTreat(table, first.CharacterId, second.CharacterId, pendingTreat))
                return false;
            first.Gift = a;
            second.Gift = b;
            openingRound = false;
            return true;

            bool CanDiscard(Visit visit) => !visit.Container ||
                TryFindThrowDirection(visit, visit.Place, out _, replacing: true);
        }

        private VodkaBottleView CreateGift(InventoryItemId item, int table, int side)
        {
            VodkaBottleView view = VodkaBottleView.Create(transform, item);
            view.SetOpened(false);
            view.transform.SetPositionAndRotation(RidgeKioskCourtPlan.GiftPoint(court.TableTop(table).position, side),
                Quaternion.identity);
            return view;
        }

        private void CloseTableFor(Visit visit)
        {
            if (IsReserved(visit)) CloseTable();
        }

        private void CloseTable()
        {
            if (tableDialogue && tableDialogue.IsActive) tableDialogue.RestoreImmediate();
            else ReleaseTable();
        }

        private void ReleaseTable()
        {
            if (reservedTable < 0) return;
            Visit first = FirstAt(reservedTable), second = SecondAt(reservedTable);
            reservedTable = -1;
            if (preview && treatScreen) treatScreen.Abandon();
            DestroyPreview();
            StopLook(first);
            StopLook(second);
            if (tableHand) tableHand.ReleaseContextualStow(this);
            previewApproved = openingRound = false;
            pendingTreat = default;

            static void StopLook(Visit visit)
            {
                if (visit.Drinking)
                {
                    visit.Drinking.SetConversationPartner(null);
                    visit.Drinking.HoldNewSips = visit.Sips == 0;
                }
                else if (visit.Actor && visit.Actor.IsSpawned) visit.Actor.Presentation.ClearAttention();
            }
        }

        private void DestroyPreview()
        {
            if (!preview) return;
            preview.gameObject.SetActive(false);
            Destroy(preview.gameObject);
            preview = null;
        }
    }
}
