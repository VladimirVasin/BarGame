using UnityEngine;

namespace BarPromenade
{
    /// <summary>One equipped session bottle on the production rig, above ordinary gait.</summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class PlayerVodkaBottleController : MonoBehaviour
    {
        public enum BottlePhase
        { Hidden, Drawing, Opening, Carrying, MouthAccess, Raising, Sipping, Lowering, Closing, CheckingEmpty, Stowing, Throwing, Recoiling }
        private PlayerRuntime player;
        private Player3DCharacterPresentation visual;
        private PlayerAnimatedInteractionController interaction;
        private CityBusRideController busRide;
        private CombatActor combat;
        private Camera viewCamera;
        private RetroAudioService audio;
        private PlayerEquipmentContacts equipmentContacts;
        private PlayerJacketCloth jacket;
        private Transform coatDock;
        private Renderer[] bottleRenderers;
        private bool[] originalForcedOff;
        private NpcHandPose hands;
        private AnatomicalHandFrame rightFrame, leftFrame;
        private Transform upper, lower, hand, leftUpper, leftLower, leftHand;
        private Transform inspectionLeftEye, inspectionRightEye;
        private PlayerScarfController.MouthAccess mouthAccess;
        private IInteractable pendingInteraction;
        private float elapsed, displayedMl, beforeSipMl;
        private int bottleId, resumeAfterFrame;
        private object medicineRequestOwner;
        private bool medicineReleased, medicineUseStarted, changingMedicineEquipment;
        private CombatTestRoot medicineCombatRoot;
        private CombatWeaponId? medicineReturnWeapon;
        private GameObject medicineReturnWeaponModel;
        private InventoryItemId medicineReturnDrink;
        private int medicineReturnBottleId, medicineReturnMl, medicineReturnSession;
        private bool medicineReturnOpened, medicineReturnNeedsInventory;
        private InventoryItemId itemId = InventoryItemId.VodkaBottle;
        public InventoryItemId ItemId => itemId;
        private DrinkContainerProfile Profile => DrinkContainerCatalog.Get(itemId);
        private bool IsMedicine => itemId == InventoryItemId.HawthornTincture;
        private Vector3 SipLipPosition
        {
            get
            {
                Transform mouth = visual.Registry.Anchors.Mouth;
                if (!IsMedicine || inspectionLeftEye == null || inspectionRightEye == null)
                    return mouth.position + transform.forward * .008f;
                // Hero V2's older mouth socket sits 41.5 mm in front of and
                // 9.6 mm above the painted lips. A tiny vial exposes that gap.
                // Retarget in the live anatomical face frame, including head
                // pitch, with 2 mm clearance; do not alter other actions' socket.
                Vector3 faceForward = mouth.up;
                Vector3 eyeMidpoint = (inspectionLeftEye.position + inspectionRightEye.position) * .5f;
                Vector3 faceUp = Vector3.ProjectOnPlane(eyeMidpoint - mouth.position, faceForward).normalized;
                return mouth.position - faceForward * .0395f - faceUp * .0096f;
            }
        }
        private float StepSeconds => IsMedicine ? Time.unscaledDeltaTime : Time.deltaTime;
        private float ClipElapsed => IsMedicine && Phase != BottlePhase.Carrying && Phase != BottlePhase.MouthAccess
            ? PhaseProgress * PlayerVodkaActions.Duration(activeActionClip) : elapsed;
        private bool freshClick = true, repeatWhileHeld, gulpPending, committed, released, rightCaptured, leftCaptured;
        private Vector3 gripLocal, mouthLocal, capLocal;
        private Quaternion capRotation;
        private bool equipmentOverlay;
        private bool stowRequested, pendingSeatHandoff, emptyBottle, terminalPresented;
        private float presentedProgress;
        private string activeActionClip;
        private static readonly float[] EmptyCheckStops = { PlayerVodkaActions.EmptyCheckInspectProgress,
            PlayerVodkaActions.EmptyCheckInspectEndProgress, PlayerVodkaActions.EmptyCheckTurnProgress,
            PlayerVodkaActions.FirstShakeProgress, PlayerVodkaActions.ShakeRestProgress,
            PlayerVodkaActions.SecondShakeProgress, PlayerVodkaActions.EmptyCheckReturnProgress, 1f };
        private static readonly float[] OpenStops = { PlayerVodkaActions.CapContactProgress, .55f,
            PlayerVodkaActions.CapTwistEndProgress, PlayerVodkaActions.CapOpenProgress, .84f,
            PlayerVodkaActions.CapReleaseProgress, 1f };
        private static readonly float[] CloseStops = { 1f - PlayerVodkaActions.CapReleaseProgress, .16f,
            1f - PlayerVodkaActions.CapOpenProgress, 1f - PlayerVodkaActions.CapTwistEndProgress, .45f,
            1f - PlayerVodkaActions.CapContactProgress, 1f };
        private static readonly float[] ThrowStops = { PlayerVodkaActions.ThrowWindupProgress, PlayerVodkaActions.ThrowReleaseProgress, 1f };
        public BottlePhase Phase { get; private set; }
        public VodkaBottleView Bottle { get; private set; }
        public float PhaseProgress => Mathf.Clamp01(elapsed / Duration);
        public bool IsBottleConcealed { get; private set; }
        public float MouthContactError { get; private set; }
        public int CompletedSips { get; private set; }
        public int ThrownBottles { get; private set; }
        public int CompletedEmptyShakes { get; private set; }
        public bool OccupiesHand => Phase != BottlePhase.Hidden || medicineUseStarted;
        public bool HasPendingMedicineUse => medicineRequestOwner != null && !medicineUseStarted;
        public bool IsAutomaticMedicineUse => medicineUseStarted;
        public bool HasPendingMedicineReturn => medicineReturnWeapon.HasValue || medicineReturnDrink != InventoryItemId.None;
        private Vector3 MedicineRecoilHeadAngles => Phase == BottlePhase.Recoiling ? RecoilHeadAngles(PhaseProgress) : Vector3.zero;
        public bool CanQueueMedicineUse
        {
            get
            {
                if (combat == null) combat = GetComponent<CombatActor>();
                return isActiveAndEnabled && medicineRequestOwner == null && !HasPendingMedicineReturn && combat != null && combat.State.CanHeal &&
                    !combat.IsKnockedDown && !combat.IsRagdollActive && !combat.IsRestrained &&
                    GameSessionState.HasInventoryItem(InventoryItemId.HawthornTincture) && !SceneTransitionService.IsTransitioning;
            }
        }

        public bool TryQueueMedicineUse(object inventoryOwner)
        {
            if (inventoryOwner == null || !CanQueueMedicineUse || !InventoryController.IsAnyOpen ||
                !GameSessionState.TryQueueHawthornUse(this, combat.State)) return false;
            medicineRequestOwner = inventoryOwner;
            medicineReleased = medicineUseStarted = false;
            return true;
        }

        public bool ReleaseQueuedMedicineUse(object inventoryOwner)
        {
            if (!ReferenceEquals(medicineRequestOwner, inventoryOwner) || !HasPendingMedicineUse) return false;
            medicineReleased = true;
            return true;
        }

        public void CancelMedicineUse(object inventoryOwner = null)
        {
            if (inventoryOwner != null && !ReferenceEquals(medicineRequestOwner, inventoryOwner)) return;
            GameSessionState.CancelHawthornUse(this);
            medicineRequestOwner = null;
            medicineReleased = medicineUseStarted = false;
            if (IsMedicine && Phase != BottlePhase.Hidden && !emptyBottle) RequestContextualStow();
        }

        /// <summary>A deliberate replacement wins over a temporary medicine handoff, including debug weapons.</summary>
        public void CancelMedicineForHandSelection()
        {
            ClearMedicineReturn();
            CancelMedicineUse();
        }

        private void OnMedicineEquipmentChanged()
        {
            if (!changingMedicineEquipment && !(IsMedicine && Phase == BottlePhase.Sipping && committed) &&
                (medicineRequestOwner != null || HasPendingMedicineReturn)) CancelMedicineForHandSelection();
        }

        private void CaptureMedicineReturn()
        {
            ClearMedicineReturn();
            medicineReturnSession = GameSessionState.SessionGeneration;
            medicineCombatRoot = combat.GetComponentInParent<CombatTestRoot>();
            if (combat.HasWeaponInHand)
            {
                medicineReturnWeapon = combat.WeaponId;
                medicineReturnWeaponModel = combat.CarriedWeapon(combat.WeaponId);
                medicineReturnNeedsInventory = medicineCombatRoot == null || medicineCombatRoot.IsStoryContinuation ||
                    GameSessionState.HasInventoryItem(WeaponInventoryItem(combat.WeaponId));
            }
            else if (Phase != BottlePhase.Hidden && !IsMedicine && Bottle != null && !IsBottleConcealed &&
                GameSessionState.EquippedDrinkItem == itemId)
            {
                medicineReturnDrink = GameSessionState.EquippedDrinkItem;
                medicineReturnBottleId = GameSessionState.DrinkContainerId(medicineReturnDrink);
                medicineReturnMl = GameSessionState.DrinkRemainingMl(medicineReturnDrink);
                medicineReturnOpened = GameSessionState.DrinkOpened(medicineReturnDrink);
            }
            if (HasPendingMedicineReturn) combat.DamageReset += OnMedicineRecipientReset;
        }

        private static InventoryItemId WeaponInventoryItem(CombatWeaponId weapon) => weapon switch
        {
            CombatWeaponId.Pistol => InventoryItemId.CombatPistol,
            CombatWeaponId.Crowbar => InventoryItemId.CombatCrowbar,
            _ => InventoryItemId.None
        };

        private void ClearMedicineReturn()
        {
            if (combat != null) combat.DamageReset -= OnMedicineRecipientReset;
            medicineReturnWeapon = null;
            medicineReturnWeaponModel = null;
            medicineReturnDrink = InventoryItemId.None;
            medicineCombatRoot = null;
        }

        private void OnMedicineRecipientReset(CombatActor actor)
        {
            ClearMedicineReturn();
            CancelMedicineUse();
        }

        private bool MedicineReturnContextInvalid => combat == null || !combat.isActiveAndEnabled ||
            combat.State.IsDefeated || combat.IsKnockedDown || combat.IsRagdollActive || combat.IsRestrained ||
            SceneTransitionService.IsTransitioning || medicineReturnSession != GameSessionState.SessionGeneration;

        private void RestoreMedicineHandIfReady()
        {
            if (!HasPendingMedicineReturn || medicineRequestOwner != null || medicineUseStarted ||
                Phase != BottlePhase.Hidden || Time.frameCount <= resumeAfterFrame || !CanRead || interaction.IsActive ||
                combat.IsSwitchingWeapon) return;
            // A newer selection or a lost item invalidates this one-use return record.
            if (GameSessionState.EquippedDrinkItem != InventoryItemId.None ||
                GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatPistol) ||
                GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar))
            { ClearMedicineReturn(); return; }
            if (medicineReturnWeapon is CombatWeaponId weapon)
            {
                if (medicineReturnWeaponModel == null || combat.CarriedWeapon(weapon) != medicineReturnWeaponModel ||
                    combat.CarriedWeaponLocation(weapon) == CombatWeaponCarryLocation.Dropped ||
                    medicineReturnNeedsInventory && !GameSessionState.HasInventoryItem(WeaponInventoryItem(weapon)) ||
                    !combat.BodyDamage.CanUseRightHand || weapon != CombatWeaponId.Crowbar && !combat.BodyDamage.CanUseLeftHand ||
                    combat.HasWeaponInHand && combat.WeaponId != weapon)
                { ClearMedicineReturn(); return; }
                bool returned;
                changingMedicineEquipment = true;
                try
                {
                    returned = medicineCombatRoot != null
                        ? medicineCombatRoot.TryReturnHandWeaponAfterMedicine(combat, weapon)
                        : GameSessionState.TrySetInventoryItemEquipped(WeaponInventoryItem(weapon), true);
                }
                finally { changingMedicineEquipment = false; }
                if (returned) ClearMedicineReturn();
                return;
            }
            if (combat.HasWeaponInHand || !GameSessionState.HasInventoryItem(medicineReturnDrink) ||
                GameSessionState.DrinkContainerId(medicineReturnDrink) != medicineReturnBottleId ||
                GameSessionState.DrinkRemainingMl(medicineReturnDrink) != medicineReturnMl)
            { ClearMedicineReturn(); return; }
            changingMedicineEquipment = true;
            try
            {
                GameSessionState.SetDrinkOpened(medicineReturnDrink, medicineReturnBottleId, medicineReturnOpened);
                GameSessionState.TrySetInventoryItemEquipped(medicineReturnDrink, true);
            }
            finally { changingMedicineEquipment = false; ClearMedicineReturn(); }
        }
        public void BindBusRide(CityBusRideController ride) => busRide = ride;
        public static bool IsHandOccupied(GameObject actor) => actor != null &&
            actor.TryGetComponent(out PlayerVodkaBottleController owner) && owner.isActiveAndEnabled && owner.OccupiesHand;

        public static PlayerVodkaBottleController Install(PlayerRuntime runtime, Camera camera = null)
        {
            if (!(runtime.Visual is Player3DCharacterPresentation presentation)) return null;
            var owner = runtime.GameObject.AddComponent<PlayerVodkaBottleController>();
            owner.player = runtime; owner.visual = presentation;
            owner.viewCamera = camera;
            owner.interaction = runtime.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            owner.hands = presentation.Registry.GetComponent<NpcHandPose>();
            owner.equipmentContacts = presentation.Registry.GetComponent<PlayerEquipmentContacts>();
            owner.rightFrame = owner.hands.GetAnatomicalFrame(false, NpcHandGripKind.Bottle);
            owner.leftFrame = owner.hands.GetAnatomicalFrame(true);
            owner.upper = owner.Bone(Player3DAnatomicalPart.RightUpperArm);
            owner.lower = owner.Bone(Player3DAnatomicalPart.RightForearm);
            owner.hand = owner.Bone(Player3DAnatomicalPart.RightHand);
            owner.leftUpper = owner.Bone(Player3DAnatomicalPart.LeftUpperArm);
            owner.leftLower = owner.Bone(Player3DAnatomicalPart.LeftForearm);
            owner.leftHand = owner.Bone(Player3DAnatomicalPart.LeftHand);
            owner.inspectionLeftEye = presentation.Registry.Anchors.Head.Find("face.eye.L");
            owner.inspectionRightEye = presentation.Registry.Anchors.Head.Find("face.eye.R");
            owner.jacket = presentation.Registry.GetComponentInParent<PlayerJacketCloth>() ??
                presentation.Registry.GetComponentInChildren<PlayerJacketCloth>();
            return owner;
        }

        private Transform Bone(Player3DAnatomicalPart part) => visual.Registry.TryGetPart(part, out var binding) ? binding.Bone : null;
        private bool Equipped => Phase == BottlePhase.Hidden
            ? medicineUseStarted || GameSessionState.EquippedDrinkItem != InventoryItemId.None
            : IsMedicine ? medicineUseStarted && (GameSessionState.IsHawthornUseOwned(this) || emptyBottle)
            : GameSessionState.EquippedDrinkItem == itemId;
        private bool Paused => GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused;
        private bool SeatedEquipment => interaction != null && interaction.AllowsHandEquipment;
        private bool SeatSipReady => SeatedEquipment && interaction.Phase == PlayerAnimatedInteractionPhase.Looping &&
            !interaction.IsNestedLoopActionActive && player.Interactor.InputEnabled;
        private bool CanPresent => (SeatedEquipment || player.Motor.InputEnabled && player.Interactor.InputEnabled &&
            !interaction.IsActive) && !(player.Ragdoll != null && player.Ragdoll.IsActive) &&
            !(combat != null && (combat.IsKnockedDown || combat.IsRagdollActive || combat.IsRestrained || combat.State.IsDefeated)) &&
            !SceneTransitionService.IsTransitioning && !DebugFreeCamera.BlocksWorldInput;
        private bool CanRead => CanPresent && (!interaction.IsActive || SeatSipReady) && GameCursor.ApplicationFocused && GameCursor.GameplayMouseInputAllowed &&
            GameInput.CanRead(GameInputContext.Gameplay);

        private void Update()
        {
            if (visual == null) return;
            if (HasPendingMedicineReturn && MedicineReturnContextInvalid) ClearMedicineReturn();
            if (medicineRequestOwner != null &&
                (medicineRequestOwner is Object source && source == null ||
                 !GameSessionState.IsHawthornUseOwned(this) && !(IsMedicine && emptyBottle))) CancelMedicineUse();
            bool held = GameInput.IsHeld(GameInputAction.VodkaSip, GameInputContext.Menu);
            if (!CanRead || Paused) { freshClick = true; repeatWhileHeld = false; }
            else if (!held) { freshClick = false; repeatWhileHeld = false; }
            if (Paused || !GameCursor.ApplicationFocused) return;
            if (medicineReleased && !medicineUseStarted && !InventoryController.IsAnyOpen)
            {
                changingMedicineEquipment = true;
                medicineUseStarted = true;
                bool started;
                try
                {
                    if (CanPresent) CaptureMedicineReturn();
                    started = CanPresent && GameSessionState.StartHawthornUse(this, combat?.State);
                }
                finally { changingMedicineEquipment = false; }
                if (!started) { ClearMedicineReturn(); CancelMedicineUse(); }
            }
            if (!CanPresent)
            {
                // Higher-priority actions own this same rig. Never refund a swallowed dose.
                Suspend(); return;
            }
            RestoreMedicineHandIfReady();
            if (!SwitchPresentationMode(SeatedEquipment)) { Suspend(); return; }
            if (pendingInteraction is Object value && value == null) CancelPendingInteraction();
            if (Phase == BottlePhase.Hidden)
            {
                if (!Equipped || Time.frameCount <= resumeAfterFrame || !CanRead ||
                    !equipmentOverlay && !visual.CanAcquireClip(this)) return;
                if (combat == null) combat = GetComponent<CombatActor>();
                if (combat != null && (combat.HasWeaponInHand || combat.IsSwitchingWeapon)) return;
                if (!Prepare()) { Suspend(); return; }
                StartPhase(BottlePhase.Drawing, PlayerVodkaActions.DrawClip);
                freshClick = true;
            }
            if (equipmentOverlay ? !visual.OwnsEquipmentPose(this) :
                (Phase == BottlePhase.Carrying || Phase == BottlePhase.MouthAccess) ? !visual.OwnsCarryPose(this) : !visual.OwnsClip(this))
            { Suspend(); return; }
            if (!Equipped || pendingInteraction != null && !pendingSeatHandoff) RequestContextualStow();
            if (Phase == BottlePhase.Carrying)
            {
                if (pendingSeatHandoff) { FinishSeatHandoff(); return; }
                if (stowRequested) { BeginCloseOrStow(); return; }
                elapsed += StepSeconds;
                if (equipmentOverlay) visual.SampleEquipmentPose(this, elapsed);
                else visual.UpdateCarryPose(this, elapsed);
                if (IsMedicine && medicineUseStarted && CanRead)
                {
                    if (!TrySip()) CancelMedicineUse();
                }
                else if (!freshClick && CanRead && GameInput.WasPressed(GameInputAction.VodkaSip, GameInputContext.Gameplay))
                    TrySip();
                return;
            }
            if (Phase == BottlePhase.MouthAccess)
            {
                if (stowRequested) { BeginCloseOrStow(); return; }
                if (pendingSeatHandoff || SeatedEquipment && !SeatSipReady) { Carry(); return; }
                if (mouthAccess == null || mouthAccess.IsReady) StartPhase(BottlePhase.Raising, PlayerVodkaActions.RaiseClip);
                return;
            }
            // A phase may leave only after its terminal pose was actually
            // presented. Empty-check landmarks also survive a long frame.
            if (terminalPresented) { CompletePhase(held); return; }
            float next = Mathf.Min(Duration, elapsed + Mathf.Max(0f, StepSeconds));
            float[] stops = Phase == BottlePhase.CheckingEmpty ? EmptyCheckStops :
                Phase == BottlePhase.Opening ? OpenStops : Phase == BottlePhase.Closing ? CloseStops :
                Phase == BottlePhase.Throwing ? ThrowStops : null;
            if (stops != null)
                foreach (float stop in stops)
                    if (stop > presentedProgress + .00001f)
                    { next = Mathf.Min(next, stop * Duration); break; }
            elapsed = next;
            float progress = Mathf.Clamp01(elapsed / Duration);
            if (equipmentOverlay) visual.SampleEquipmentPose(this, ClipElapsed);
            else visual.SampleOwnedClip(this, progress);
            if (Phase == BottlePhase.Sipping && !committed && progress >= PlayerVodkaActions.SipCommitProgress)
            {
                committed = true;
                if (GameSessionState.TryDrinkContainerSip(itemId, bottleId, Mathf.RoundToInt(beforeSipMl), out var result, combat?.State, this))
                {
                    CompletedSips++; displayedMl = result.RemainingMl; emptyBottle = result.Emptied; gulpPending = true;
                    if (itemId == InventoryItemId.HawthornTincture)
                    {
                        Rendering.HawthornHealingPulse.Trigger(viewCamera);
                        HawthornHealingAudio.Play(viewCamera);
                    }
                }
                else
                {
                    displayedMl = GameSessionState.DrinkRemainingMl(itemId); repeatWhileHeld = false;
                    if (IsMedicine) CancelMedicineUse();
                }
            }
            // Release is applied after this frame's final IK/contact pose in LateUpdate.
            if (Phase == BottlePhase.Throwing && progress >= PlayerVodkaActions.ThrowReleaseProgress) released = true;
        }

        private void CompletePhase(bool held)
        {
            switch (Phase)
            {
                case BottlePhase.Drawing:
                    if (stowRequested) StartPhase(BottlePhase.Stowing, PlayerVodkaActions.StowClip);
                    else if (Bottle.IsOpened) Carry();
                    else StartPhase(BottlePhase.Opening, PlayerVodkaActions.OpenClip);
                    break;
                case BottlePhase.Opening:
                    Bottle.SetOpened(true);
                    GameSessionState.SetDrinkOpened(itemId, bottleId, true);
                    if (stowRequested) BeginCloseOrStow(); else Carry();
                    break;
                case BottlePhase.Raising:
                    if (stowRequested || pendingSeatHandoff || !CanRead) StartPhase(BottlePhase.Lowering, PlayerVodkaActions.LowerClip);
                    else BeginSip();
                    break;
                case BottlePhase.Sipping:
                    if (!IsMedicine && !emptyBottle && !stowRequested && !pendingSeatHandoff && repeatWhileHeld && held && CanRead && GameSessionState.CanDrinkContainerSip(itemId, combat?.State))
                        BeginSip();
                    else StartPhase(BottlePhase.Lowering, PlayerVodkaActions.LowerClip);
                    break;
                case BottlePhase.Lowering:
                    if (emptyBottle && IsMedicine)
                        StartPhase(BottlePhase.Recoiling, PlayerVodkaActions.CarryClip);
                    else if (emptyBottle)
                    { CompletedEmptyShakes = 0; StartPhase(BottlePhase.CheckingEmpty, PlayerVodkaActions.EmptyCheckClip); }
                    else if (stowRequested) BeginCloseOrStow();
                    else Carry();
                    break;
                case BottlePhase.Closing:
                    Bottle.Cap.localPosition = capLocal; Bottle.Cap.localRotation = capRotation;
                    Bottle.SetOpened(false);
                    GameSessionState.SetDrinkOpened(itemId, bottleId, false);
                    StartPhase(BottlePhase.Stowing, PlayerVodkaActions.StowClip);
                    break;
                case BottlePhase.CheckingEmpty:
                case BottlePhase.Recoiling:
                    StartPhase(BottlePhase.Throwing, PlayerVodkaActions.ThrowClip);
                    break;
                case BottlePhase.Stowing:
                case BottlePhase.Throwing:
                    FinishHandoff();
                    break;
            }
        }

        private void BeginSip()
        {
            beforeSipMl = displayedMl; committed = false;
            if (Phase != BottlePhase.Sipping) StartPhase(BottlePhase.Sipping, PlayerVodkaActions.SipClip);
            else
            {
                // Reuse the held loop's graph, hand and scarf ownership.
                elapsed = presentedProgress = 0f; terminalPresented = false;
                if (equipmentOverlay) visual.SampleEquipmentPose(this, 0f);
                else visual.SampleOwnedClip(this, 0f);
            }
        }

        private void BeginCloseOrStow()
        {
            bool close = Bottle.IsOpened && !Profile.IsCan;
            StartPhase(close ? BottlePhase.Closing : BottlePhase.Stowing,
                close ? PlayerVodkaActions.CloseClip : PlayerVodkaActions.StowClip);
        }

        private bool Prepare()
        {
            if (!PlayerVodkaActions.TryAttach(visual.Registry)) return false;
            itemId = medicineUseStarted ? InventoryItemId.HawthornTincture : GameSessionState.EquippedDrinkItem;
            if (!DrinkContainerCatalog.IsDrink(itemId)) return false;
            if (Bottle == null) Bottle = VodkaBottleView.Create(transform, itemId);
            if (Bottle == null) return false;
            if (coatDock == null)
            {
                var bindings = Resources.Load<CombatWeaponCarryBindings>(CombatWeaponCarryAssetProvider.ResourceFolder + "Bindings");
                if (bindings == null) return false;
                coatDock = new GameObject("Vodka coat dock").transform;
                coatDock.SetParent(visual.Registry.Anchors.Chest, false);
                Vector3 pistolDock = visual.Registry.Anchors.Chest.TransformPoint(bindings.For(CombatWeaponId.Pistol).BoneLocalPosition);
                Vector3 sourceOffset = PlayerVodkaActions.DrawNeckSource - new Vector3(.175f, -.072f, 1.34f);
                Vector3 outside = Vector3.ProjectOnPlane(upper.position - leftUpper.position, transform.up).normalized;
                // Same chest binding, with the bottle's measured neck outside
                // the shirt. Source +X is anatomical left and +Y is backward.
                coatDock.position = pistolDock - outside * sourceOffset.x - transform.forward * sourceOffset.y + transform.up * sourceOffset.z;
            }
            Bottle.transform.SetParent(coatDock, true);
            bottleRenderers = Bottle.GetComponentsInChildren<Renderer>(true);
            originalForcedOff = new bool[bottleRenderers.Length];
            for (int i = 0; i < bottleRenderers.Length; i++) originalForcedOff[i] = bottleRenderers[i].forceRenderingOff;
            SetConcealed(true);
            audio = RetroAudioService.EnsureInstalled();
            audio?.GetClip(RetroSfxId.DrinkGulp);
            if (IsMedicine) HawthornHealingAudio.Prepare(viewCamera);
            equipmentContacts ??= visual.Registry.GetComponent<PlayerEquipmentContacts>() ??
                visual.Registry.gameObject.AddComponent<PlayerEquipmentContacts>();
            equipmentContacts.Register(this, new[] { coatDock }, Bottle.gameObject);
            bottleId = GameSessionState.DrinkContainerId(itemId);
            emptyBottle = stowRequested = pendingSeatHandoff = false;
            displayedMl = GameSessionState.DrinkRemainingMl(itemId);
            gripLocal = Bottle.transform.InverseTransformPoint(Bottle.Grip.position);
            mouthLocal = Bottle.transform.InverseTransformPoint(Bottle.Mouth.position);
            if (Bottle.Cap != null)
            { capLocal = Bottle.Cap.localPosition; capRotation = Bottle.Cap.localRotation; }
            Bottle.SetOpened(GameSessionState.DrinkOpened(itemId)); Bottle.SetRemaining(displayedMl / Profile.FullMl);
            rightFrame = hands.GetAnatomicalFrame(false, Profile.GripKind);
            rightCaptured = hands.TryCaptureGrip(this, false, Profile.GripKind, exclusive: true);
            return rightCaptured;
        }

        public bool TrySip()
        {
            if (Phase != BottlePhase.Carrying || !CanRead || !GameSessionState.CanDrinkContainerSip(itemId, combat?.State, this)) return false;
            beforeSipMl = displayedMl; committed = false;
            repeatWhileHeld = !IsMedicine && GameInput.IsHeld(GameInputAction.VodkaSip, GameInputContext.Menu);
            mouthAccess ??= PlayerScarfController.RequireMouthAccess(player, this);
            Phase = BottlePhase.MouthAccess;
            return true;
        }

        public bool TryPrepareInteraction(IInteractable target)
        {
            if (target == null) return false;
            if (medicineRequestOwner != null) CancelMedicineUse();
            if (!OccupiesHand) return false;
            if (pendingInteraction != null) return true;
            bool compatibleSeat = target is IHandEquipmentSeat seat && seat.AllowsHandEquipment && !emptyBottle &&
                (Phase == BottlePhase.Carrying || Phase == BottlePhase.MouthAccess || IsDrinking);
            if (compatibleSeat && (Phase == BottlePhase.Carrying || Phase == BottlePhase.MouthAccess))
            {
                if (Phase != BottlePhase.Carrying) Carry();
                if (SwitchPresentationMode(true)) return false;
            }
            pendingInteraction = target;
            pendingSeatHandoff = compatibleSeat;
            repeatWhileHeld = false;
            player.Interactor.SetOwnedInteractKeyClaim(this, true);
            if (!compatibleSeat) RequestContextualStow();
            return true;
        }

        public void RequestContextualStow()
        {
            if (!OccupiesHand || emptyBottle || Phase == BottlePhase.Stowing || Phase == BottlePhase.Throwing) return;
            stowRequested = true;
            pendingSeatHandoff = false;
            repeatWhileHeld = false;
        }

        private bool IsDrinking => Phase == BottlePhase.Raising || Phase == BottlePhase.Sipping || Phase == BottlePhase.Lowering;

        private void FinishSeatHandoff()
        {
            IInteractable target = pendingInteraction;
            CancelPendingInteraction();
            if (target != null && (!(target is Object obj) || obj != null) && target.CanInteract(player.Interactor) && SwitchPresentationMode(true))
                target.Interact(player.Interactor);
        }

        private bool SwitchPresentationMode(bool overlay)
        {
            if (equipmentOverlay == overlay) return true;
            visual.ReleaseOwnedClip(this);
            visual.ReleaseCarryPose(this);
            visual.ReleaseEquipmentPose(this);
            equipmentOverlay = overlay;
            if (Phase == BottlePhase.Hidden) return true;
            bool carry = Phase == BottlePhase.Carrying || Phase == BottlePhase.MouthAccess;
            string clip = carry ? PlayerVodkaActions.CarryClip : activeActionClip;
            if (overlay)
                return visual.TryAcquireEquipmentPose(this, clip, rightArmOnly: carry) && visual.SampleEquipmentPose(this, ClipElapsed);
            if (carry)
                return visual.TryAcquireCarryPose(this, clip) && visual.UpdateCarryPose(this, elapsed);
            if (!visual.TryAcquireClip(this, clip)) return false;
            visual.SetOwnedClipLocomotion(this, true);
            return visual.SampleOwnedClip(this, PhaseProgress);
        }

        private void FinishHandoff()
        {
            IInteractable target = pendingInteraction;
            bool continueMedicine = medicineUseStarted && !IsMedicine && target == null;
            CancelPendingInteraction(); Suspend(continueMedicine); resumeAfterFrame = Time.frameCount + 1;
            if (target != null && (!(target is Object obj) || obj != null) && target.CanInteract(player.Interactor))
                target.Interact(player.Interactor);
        }

        public float PhaseDurationSeconds => Duration;
        private float Duration => IsMedicine ? Phase switch
        {
            BottlePhase.Drawing => .60f,
            BottlePhase.Opening or BottlePhase.Closing => .45f,
            BottlePhase.Raising or BottlePhase.Lowering => .22f,
            BottlePhase.Sipping => .30f,
            BottlePhase.Recoiling => .65f,
            BottlePhase.Stowing => .35f,
            BottlePhase.Throwing => .45f,
            _ => PlayerVodkaActions.CarrySeconds
        } : Phase switch
        {
            BottlePhase.Drawing => PlayerVodkaActions.DrawSeconds,
            BottlePhase.Opening => PlayerVodkaActions.OpenSeconds,
            BottlePhase.Raising => PlayerVodkaActions.RaiseSeconds,
            BottlePhase.Sipping => PlayerVodkaActions.SipSeconds,
            BottlePhase.Lowering => PlayerVodkaActions.LowerSeconds,
            BottlePhase.Closing => PlayerVodkaActions.CloseSeconds,
            BottlePhase.CheckingEmpty => PlayerVodkaActions.EmptyCheckSeconds,
            BottlePhase.Stowing => PlayerVodkaActions.StowSeconds,
            BottlePhase.Throwing => PlayerVodkaActions.ThrowSeconds,
            _ => PlayerVodkaActions.CarrySeconds
        };
        private void StartPhase(BottlePhase phase, string clip)
        {
            if (phase != BottlePhase.Raising && phase != BottlePhase.Sipping && phase != BottlePhase.Lowering)
            { mouthAccess?.Dispose(); mouthAccess = null; }
            if (phase != BottlePhase.Opening && phase != BottlePhase.Closing && leftCaptured)
            { hands.ReleaseGrip(this, true); leftCaptured = false; }
            if (phase != BottlePhase.Drawing && phase != BottlePhase.Stowing) jacket?.ClearWeaponAccess(this);
            visual.ReleaseCarryPose(this);
            bool acquired = equipmentOverlay ? visual.TryAcquireEquipmentPose(this, clip) :
                visual.TryAcquireClip(this, clip, preserveRecoveryPose: visual.OwnsClip(this));
            if (!acquired) { Suspend(); return; }
            if (!equipmentOverlay) visual.SetOwnedClipLocomotion(this, true);
            activeActionClip = clip;
            elapsed = presentedProgress = 0f; Phase = phase; released = terminalPresented = false;
            if (phase == BottlePhase.CheckingEmpty)
                visual.TrySetContextualFacialExpression(this, PlayerFacialExpression.Watchful);
            else if (phase == BottlePhase.Throwing || phase == BottlePhase.Recoiling)
                visual.TrySetContextualFacialExpression(this, PlayerFacialExpression.Tense);
            else visual.ReleaseContextualFacialExpression(this);
            if (equipmentOverlay) visual.SampleEquipmentPose(this, 0f);
            else visual.SampleOwnedClip(this, 0f);
        }
        private void Carry()
        {
            jacket?.ClearWeaponAccess(this);
            mouthAccess?.Dispose(); mouthAccess = null;
            if (leftCaptured) hands.ReleaseGrip(this, true);
            leftCaptured = false;
            visual.ReleaseOwnedClip(this);
            bool acquired = equipmentOverlay ? visual.TryAcquireEquipmentPose(this, PlayerVodkaActions.CarryClip, rightArmOnly: true) :
                visual.TryAcquireCarryPose(this, PlayerVodkaActions.CarryClip);
            if (!acquired) { Suspend(); return; }
            activeActionClip = PlayerVodkaActions.CarryClip;
            Phase = BottlePhase.Carrying; elapsed = 0f;
        }

        private float SeatedPoseWeight => !SeatedEquipment ? 0f : interaction.Phase switch
        {
            PlayerAnimatedInteractionPhase.Looping => 1f,
            PlayerAnimatedInteractionPhase.Entering => Smooth(.42f, .75f, interaction.PhaseProgress),
            PlayerAnimatedInteractionPhase.Exiting => 1f - Smooth(.35f, .75f, interaction.PhaseProgress),
            _ => 0f
        };

        private float SeatedCarryAmount(float progress) => Phase switch
        {
            BottlePhase.Carrying or BottlePhase.MouthAccess or BottlePhase.Recoiling => 1f,
            BottlePhase.Drawing => Smooth(.76f, 1f, progress),
            BottlePhase.Opening => 1f - Smooth(0f, .22f, progress) + Smooth(.76f, 1f, progress),
            BottlePhase.Closing => 1f - Smooth(0f, .22f, 1f - progress) + Smooth(.76f, 1f, 1f - progress),
            BottlePhase.Raising => 1f - Smooth(0f, 1f, progress),
            BottlePhase.Lowering => Smooth(0f, 1f, progress),
            BottlePhase.CheckingEmpty => 1f - EmptyCheckDownAmount(progress),
            BottlePhase.Stowing => 1f - Smooth(0f, .24f, progress),
            BottlePhase.Throwing => 1f - Smooth(0f, .20f, progress),
            _ => 0f
        };

        private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        private static Vector3 RecoilHeadAngles(float progress)
        {
            float envelope = Smooth(0f, .12f, progress) * (1f - Smooth(.7f, 1f, progress));
            float swing = Mathf.Sin(progress * Mathf.PI * 4f) * Mathf.Lerp(1f, .52f, progress);
            return new Vector3(-4f * envelope, 14f * swing * envelope, 2f * swing * envelope);
        }
        private static float EmptyCheckDownAmount(float progress) =>
            Smooth(0f, PlayerVodkaActions.EmptyCheckInspectProgress, progress) *
            (1f - Smooth(PlayerVodkaActions.EmptyCheckReturnProgress, 1f, progress));
        private static float EmptyCheckShakeAmount(float progress) =>
            Smooth(PlayerVodkaActions.EmptyCheckTurnProgress, PlayerVodkaActions.FirstShakeProgress, progress) *
            (1f - Smooth(PlayerVodkaActions.FirstShakeProgress, PlayerVodkaActions.ShakeRestProgress, progress)) +
            Smooth(PlayerVodkaActions.ShakeRestProgress, PlayerVodkaActions.SecondShakeProgress, progress) *
            (1f - Smooth(PlayerVodkaActions.SecondShakeProgress, PlayerVodkaActions.EmptyCheckReturnProgress, progress));
        private void SetConcealed(bool concealed)
        {
            if (IsBottleConcealed == concealed) return;
            IsBottleConcealed = concealed;
            for (int i = 0; i < bottleRenderers.Length; i++)
                if (bottleRenderers[i] != null) bottleRenderers[i].forceRenderingOff = concealed || originalForcedOff[i];
        }
        public void ReapplyPresentation() => LateUpdate();
        private void LateUpdate()
        {
            if (!OccupiesHand) return;
            if (Bottle == null) { if (Phase == BottlePhase.Throwing) MarkPresented(); return; }
            float progress = PhaseProgress;
            bool coatTransfer = Phase == BottlePhase.Drawing || Phase == BottlePhase.Stowing;
            float drawProgress = Phase == BottlePhase.Stowing ? 1f - progress : progress;
            if (coatTransfer)
            {
                float access = CombatWeaponCarryAssetProvider.JacketAccessAmount(CombatWeaponId.Pistol,
                    Phase == BottlePhase.Drawing, progress);
                if (access > 0f) jacket?.SetWeaponAccess(this, access);
                else jacket?.ClearWeaponAccess(this);
                bool concealed = drawProgress < PlayerVodkaActions.DrawTransferProgress;
                Transform parent = concealed ? coatDock : transform;
                if (Bottle.transform.parent != parent) Bottle.transform.SetParent(parent, true);
                SetConcealed(concealed);
                if (concealed)
                {
                    Quaternion upright = Quaternion.LookRotation(transform.right, Vector3.up);
                    Vector3 concealedGrip = coatDock.position;
                    float minimumY = visual.Registry.Anchors.Pelvis.position.y + gripLocal.y + .09f;
                    concealedGrip.y = Mathf.Lerp(concealedGrip.y, Mathf.Max(concealedGrip.y, minimumY), SeatedPoseWeight);
                    Bottle.transform.SetPositionAndRotation(concealedGrip - upright * gripLocal, upright);
                    hands.SetOwnedGrip(this, false, Smooth(PlayerVodkaActions.DrawTransferProgress - .08f,
                        PlayerVodkaActions.DrawTransferProgress, drawProgress));
                    MarkPresented();
                    return;
                }
            }
            else
            {
                // Update can finish drawing before a LateUpdate reaches the
                // transfer frame. Every held phase completes that ownership.
                if (Bottle.transform.parent != transform) Bottle.transform.SetParent(transform, true);
                SetConcealed(false);
            }
            float shown = Phase == BottlePhase.Sipping && committed ?
                Mathf.Lerp(beforeSipMl, displayedMl, Smooth(PlayerVodkaActions.SipCommitProgress, .65f, progress)) : displayedMl;
            float sip = Phase switch
            {
                BottlePhase.Raising => Smooth(0f, 1f, progress),
                BottlePhase.Sipping => 1f,
                BottlePhase.Lowering => 1f - Smooth(0f, 1f, progress),
                _ => 0f
            };
            float depletion = IsMedicine ? 1f : Smooth(Profile.FullMl, (Profile.FullMl + Profile.SipCount - 1) / Profile.SipCount, shown);
            if (sip > 0f) visual.ApplyEquipmentHeadPitch(this, PlayerVodkaActions.LastDrinkHeadPitchDegrees * depletion * sip * (IsMedicine ? .5f : 1f));
            else if (Phase == BottlePhase.Recoiling) visual.ApplyEquipmentHeadRotation(this, MedicineRecoilHeadAngles);
            else visual.ClearEquipmentHeadPitch(this);
            // The carried bottle is upright; the original wrist follows its
            // neck frame instead of inheriting a sideways relaxed thumb.
            Quaternion rotation = Quaternion.LookRotation(transform.right, Vector3.up);
            if (coatTransfer)
            {
                // The palm faces the coat during extraction. Forcing the
                // carry yaw here moves the wrist behind the safe neck path.
                Vector3 palm = Vector3.ProjectOnPlane(hand.rotation * rightFrame.PalmNormalInHand, Vector3.up);
                if (palm.sqrMagnitude > .0001f)
                    rotation = Quaternion.Slerp(Quaternion.LookRotation(-palm.normalized, Vector3.up),
                        rotation, Smooth(.76f, 1f, drawProgress));
            }
            Vector3 grip = hand.position + hand.rotation * rightFrame.CylinderCentreInHand;
            float seatedLift = 0f;
            float seatedWeight = SeatedPoseWeight;
            if (seatedWeight > 0f && Phase != BottlePhase.Sipping)
            {
                Vector3 outside = Vector3.ProjectOnPlane(upper.position - leftUpper.position, transform.up).normalized;
                float minimumY = visual.Registry.Anchors.Pelvis.position.y + gripLocal.y + .09f;
                Vector3 raisedGrip = grip;
                raisedGrip.y = Mathf.Max(raisedGrip.y, minimumY);
                Vector3 lapGrip = visual.Registry.Anchors.Chest.position + outside * .14f + transform.forward * .20f - transform.up * .18f;
                lapGrip.y = Mathf.Max(lapGrip.y, minimumY);
                Vector3 seatedGrip = Vector3.Lerp(raisedGrip, lapGrip, SeatedCarryAmount(progress));
                seatedLift = (seatedGrip.y - grip.y) * seatedWeight;
                grip = Vector3.Lerp(grip, seatedGrip, seatedWeight);
            }
            Vector3 position = grip - rotation * gripLocal;
            if (sip > 0f)
            {
                // The mouth stays anchored while the base rises with the remaining volume.
                Vector3 levelForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                float tilt = Mathf.Lerp(PlayerVodkaActions.FullDrinkTiltDegrees, PlayerVodkaActions.LastDrinkTiltDegrees, depletion);
                // Only the swallow tilts during a held loop; its endpoints
                // retain the same raised mouth contact as Raise and Lower.
                float gulp = Phase == BottlePhase.Sipping ? Smooth(.38f, PlayerVodkaActions.SipCommitProgress, progress) *
                    (1f - Smooth(.53f, .66f, progress)) : 0f;
                tilt += 5f * gulp * (1f - depletion);
                Vector3 drinkAxis = -levelForward * Mathf.Cos(tilt * Mathf.Deg2Rad) - Vector3.up * Mathf.Sin(tilt * Mathf.Deg2Rad);
                Quaternion drinkRotation = Quaternion.LookRotation(Vector3.Cross(transform.right, drinkAxis), drinkAxis);
                Vector3 lip = SipLipPosition;
                Vector3 drinkPosition = lip - drinkRotation * mouthLocal;
                rotation = Quaternion.Slerp(rotation, drinkRotation, sip);
                position = Vector3.Lerp(position, drinkPosition, sip);
                Bottle.transform.SetPositionAndRotation(position, rotation);
                MouthContactError = Vector3.Distance(Bottle.Mouth.position, lip);
            }
            if (Phase == BottlePhase.CheckingEmpty)
            {
                float down = EmptyCheckDownAmount(progress);
                float vertical = Smooth(PlayerVodkaActions.EmptyCheckInspectEndProgress, PlayerVodkaActions.EmptyCheckTurnProgress, progress);
                Quaternion inspect = Quaternion.AngleAxis(PlayerVodkaActions.EmptyCheckInspectTiltDegrees, transform.right) * rotation;
                Vector3 horizontalPalm = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                Quaternion straightDown = Quaternion.LookRotation(horizontalPalm, Vector3.down);
                rotation = Quaternion.Slerp(rotation, Quaternion.Slerp(inspect, straightDown, vertical), down);
                if (seatedWeight > 0f && inspectionLeftEye != null && inspectionRightEye != null)
                {
                    // The parent's seated pelvis pitch raises this contact
                    // above the authored eye line. Retarget its height to the
                    // live face, preserving the two axial shake impulses.
                    float eyeY = (inspectionLeftEye.position.y + inspectionRightEye.position.y) * .5f;
                    float openingY = eyeY + .078f + (rotation * Vector3.up).y *
                        (PlayerVodkaActions.EmptyCheckShakeDistance * EmptyCheckShakeAmount(progress));
                    float gripY = openingY + (rotation * (gripLocal - mouthLocal)).y;
                    grip.y = Mathf.Lerp(grip.y, gripY, seatedWeight * down);
                }
                // The authored neck path owns both .075 m impulses. Retain
                // that moving grip while the same bottle turns upside down.
                position = grip - rotation * gripLocal;
            }
            if (busRide != null) position = busRide.ConstrainHeldBottlePosition(position, rotation);
            Bottle.transform.SetPositionAndRotation(position, rotation);
            if (sip > 0f)
                MouthContactError = Vector3.Distance(Bottle.Mouth.position, SipLipPosition);
            // Imported contact empties retain the FBX authoring-root roll.
            // Their positions are measured anchors; the bottle wrapper owns
            // the physical cylinder axes used by both liquid and hand contact.
            Pose rightWrist = rightFrame.ResolveCylinderPose(Bottle.Grip.position, Bottle.transform.up, -Bottle.transform.forward);
            // Draw/open paths already own an elbow that clears the body.
            // Correct the neck contact without replacing that authored plane.
            Vector3 rightElbow = coatTransfer || Phase == BottlePhase.Opening || Phase == BottlePhase.Closing ||
                Phase == BottlePhase.CheckingEmpty || Phase == BottlePhase.Throwing ? lower.position :
                upper.position + transform.right * .30f - Vector3.up * .20f;
            if (seatedWeight > 0f && Phase != BottlePhase.CheckingEmpty && Phase != BottlePhase.Throwing)
            {
                Vector3 outside = Vector3.ProjectOnPlane(upper.position - leftUpper.position, transform.up).normalized;
                rightElbow = Vector3.Lerp(rightElbow, upper.position + outside * .14f + transform.forward * .35f - transform.up * .20f, seatedWeight);
            }
            LimbTwoBoneIk.Solve(upper, lower, hand, rightWrist.position, rightWrist.rotation,
                rightElbow, 1f, float.PositiveInfinity, true);
            if (sip <= 0f)
            {
                // Reach limits may shorten an authored transfer. Retain the
                // exact visible neck contact without rotating the bottle flat.
                grip = hand.position + hand.rotation * rightFrame.CylinderCentreInHand;
                Bottle.transform.position = grip - rotation * gripLocal;
            }
            hands.SetOwnedGrip(this, false, 1f);
            if (Phase == BottlePhase.Opening && Profile.IsCan)
            {
                PresentCanOpening(progress);
            }
            else if ((Phase == BottlePhase.Opening || Phase == BottlePhase.Closing) && Bottle.Cap != null)
            {
                bool closing = Phase == BottlePhase.Closing;
                float capProgress = closing ? 1f - progress : progress;
                float leftLift = seatedLift * Smooth(.08f, .35f, capProgress) * (1f - Smooth(.82f, 1f, capProgress));
                if (Mathf.Abs(leftLift) > .0001f)
                    LimbTwoBoneIk.Solve(leftUpper, leftLower, leftHand, leftHand.position + transform.up * leftLift,
                        leftHand.rotation, leftLower.position + transform.up * (leftLift * .5f), 1f, float.PositiveInfinity, true);
                Bottle.Cap.localPosition = capLocal;
                Bottle.Cap.localRotation = capRotation;
                bool touching = capProgress >= PlayerVodkaActions.CapContactProgress && capProgress < PlayerVodkaActions.CapReleaseProgress;
                // Logical openness and the carried cap's visibility have
                // separate owners while the hand retrieves/replaces it.
                Bottle.SetCapPresentation(capProgress >= PlayerVodkaActions.CapOpenProgress,
                    capProgress < PlayerVodkaActions.CapReleaseProgress);
                GameSessionState.SetDrinkOpened(itemId, bottleId, Bottle.IsOpened);
                if (touching)
                {
                    if (!leftCaptured) leftCaptured = hands.TryCaptureGrip(this, true, NpcHandGripKind.Cylindrical, exclusive: true);
                    float gripAmount = Smooth(PlayerVodkaActions.CapContactProgress, .49f, capProgress) *
                        (1f - Smooth(.82f, PlayerVodkaActions.CapReleaseProgress, capProgress));
                    Vector3 cap = Bottle.Cap.position;
                    if (capProgress < PlayerVodkaActions.CapOpenProgress)
                    {
                        cap += Bottle.transform.up * (.035f * Smooth(PlayerVodkaActions.CapTwistEndProgress,
                            PlayerVodkaActions.CapOpenProgress, capProgress));
                        Vector3 outside = Vector3.ProjectOnPlane(leftUpper.position - upper.position, transform.up).normalized;
                        Pose wrist = leftFrame.ResolveCylinderPose(cap, Bottle.transform.up, -outside);
                        // The authored approach/return stays in front of the
                        // chest. IK only closes the small cap-contact seam.
                        float contact = Smooth(PlayerVodkaActions.CapContactProgress, .49f, capProgress) *
                            (1f - Smooth(PlayerVodkaActions.CapTwistEndProgress, PlayerVodkaActions.CapOpenProgress, capProgress));
                        LimbTwoBoneIk.Solve(leftUpper, leftLower, leftHand, wrist.position, wrist.rotation,
                            leftUpper.position + outside * .30f + transform.forward * .24f - transform.up * .18f,
                            contact, float.PositiveInfinity, true);
                    }
                    else cap = leftHand.position + leftHand.rotation * leftFrame.CylinderCentreInHand;
                    hands.SetOwnedGrip(this, true, gripAmount);
                    Bottle.Cap.position = cap;
                    Bottle.Cap.rotation = Quaternion.AngleAxis(Smooth(PlayerVodkaActions.CapContactProgress,
                        PlayerVodkaActions.CapTwistEndProgress, capProgress) * 270f, Bottle.transform.up) *
                        Bottle.Cap.parent.rotation * capRotation;
                }
                else if (leftCaptured) { hands.ReleaseGrip(this, true); leftCaptured = false; }
            }
            Bottle.SetRemaining(shown / Profile.FullMl);
            Bottle.RefreshLiquid();
            if (gulpPending)
            {
                gulpPending = false;
                audio?.TryPlay(RetroSfxId.DrinkGulp, visual.Registry.Anchors.Mouth.position);
            }
            if (Phase == BottlePhase.Throwing && released)
            {
                var thrown = Bottle; Bottle = null;
                equipmentContacts?.Unregister(this);
                hands.ReleaseGrip(this, false); rightCaptured = false;
                Vector3 inheritedVelocity = player.Motor.PlanarVelocity;
                Collider vehicleHull = null;
                if (busRide != null && busRide.TryGetBottleReleaseContext(out Vector3 vehicleVelocity, out vehicleHull))
                    inheritedVelocity = vehicleVelocity;
                ThrownVodkaBottle.Release(thrown, inheritedVelocity + transform.right * 6.2f + transform.forward * .7f + Vector3.up * .8f,
                    transform.forward * 16f + transform.up * 4f, transform, vehicleHull);
                ThrownBottles++;
            }
            MarkPresented();
        }

        private void PresentCanOpening(float progress)
        {
            // The retained ring pivots on the lid. The existing left hand reaches
            // its actual position; there is no detached screw cap on a can.
            float contact = Smooth(.28f, .44f, progress) * (1f - Smooth(.76f, .92f, progress));
            bool open = progress >= .64f || GameSessionState.DrinkOpened(itemId);
            Bottle.SetCapPresentation(open, true);
            float lift = Smooth(.44f, .64f, progress) * (1f - Smooth(.70f, .88f, progress));
            Bottle.Cap.localRotation = capRotation * Quaternion.AngleAxis(-70f * lift - (open ? 14f * (1f - lift) : 0f), Vector3.right);
            if (open) GameSessionState.SetDrinkOpened(itemId, bottleId, true);
            if (contact > 0f)
            {
                if (!leftCaptured) leftCaptured = hands.TryCaptureGrip(this, true, NpcHandGripKind.Cylindrical, exclusive: true);
                Vector3 ring = Bottle.Cap.position + Bottle.Cap.TransformDirection(Vector3.forward) * .006f;
                Vector3 outside = Vector3.ProjectOnPlane(leftUpper.position - upper.position, transform.up).normalized;
                Quaternion wristRotation = leftFrame.ResolveHandRotation(-Bottle.transform.up, -Bottle.transform.forward);
                Vector3 fingerContact = leftFrame.PalmCentreInHand + leftFrame.FingerDirectionInHand * .055f;
                Vector3 wristPosition = ring - wristRotation * fingerContact;
                LimbTwoBoneIk.Solve(leftUpper, leftLower, leftHand, wristPosition, wristRotation,
                    leftUpper.position + outside * .24f + transform.forward * .22f - transform.up * .10f,
                    contact, float.PositiveInfinity, true);
                hands.SetOwnedGrip(this, true, .22f * contact);
            }
            else if (leftCaptured) { hands.ReleaseGrip(this, true); leftCaptured = false; }
        }

        private void MarkPresented()
        {
            if (Phase == BottlePhase.Carrying || Phase == BottlePhase.MouthAccess) return;
            presentedProgress = PhaseProgress;
            terminalPresented = presentedProgress >= 1f;
            if (Phase == BottlePhase.CheckingEmpty)
                CompletedEmptyShakes = presentedProgress + .00001f >= PlayerVodkaActions.SecondShakeProgress ? 2 :
                    presentedProgress + .00001f >= PlayerVodkaActions.FirstShakeProgress ? 1 : 0;
        }

        private void CancelPendingInteraction()
        { pendingInteraction = null; pendingSeatHandoff = false; player.Interactor?.SetOwnedInteractKeyClaim(this, false); }
        private void Suspend(bool preserveMedicineUse = false)
        {
            if (!preserveMedicineUse) CancelMedicineUse();
            visual?.ClearEquipmentHeadPitch(this);
            visual?.ReleaseOwnedClip(this); visual?.ReleaseCarryPose(this); visual?.ReleaseEquipmentPose(this);
            visual?.ReleaseContextualFacialExpression(this);
            jacket?.ClearWeaponAccess(this);
            mouthAccess?.Dispose(); mouthAccess = null;
            if (rightCaptured) hands?.ReleaseGrip(this, false);
            if (leftCaptured) hands?.ReleaseGrip(this, true);
            rightCaptured = leftCaptured = false;
            if (Bottle != null) Destroy(Bottle.gameObject);
            equipmentContacts?.Unregister(this);
            Bottle = null; bottleRenderers = null; originalForcedOff = null; IsBottleConcealed = false;
            Phase = BottlePhase.Hidden; freshClick = true;
            repeatWhileHeld = gulpPending = stowRequested = emptyBottle = terminalPresented = false;
            CancelPendingInteraction();
        }
        private void OnDisable()
        {
            ClearMedicineReturn();
            GameSessionState.InventoryEquipmentChanged -= OnMedicineEquipmentChanged;
            Rendering.HawthornHealingPulse.Clear(viewCamera);
            HawthornHealingAudio.Clear(viewCamera);
            if (visual != null) Suspend();
        }
        private void OnDestroy()
        {
            ClearMedicineReturn();
            if (visual != null) Suspend();
            if (coatDock != null) Destroy(coatDock.gameObject);
        }

        private void OnEnable() => GameSessionState.InventoryEquipmentChanged += OnMedicineEquipmentChanged;
    }
}
