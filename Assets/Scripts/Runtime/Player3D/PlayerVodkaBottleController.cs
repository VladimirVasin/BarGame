using UnityEngine;

namespace BarPromenade
{
    /// <summary>One equipped session bottle on the production rig, above ordinary gait.</summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class PlayerVodkaBottleController : MonoBehaviour
    {
        public enum BottlePhase { Hidden, Drawing, Opening, Carrying, MouthAccess, Sipping, Stowing, Throwing }
        private PlayerRuntime player;
        private Player3DCharacterPresentation visual;
        private PlayerAnimatedInteractionController interaction;
        private CityBusRideController busRide;
        private CombatActor combat;
        private RetroAudioService audio;
        private PlayerEquipmentContacts equipmentContacts;
        private PlayerJacketCloth jacket;
        private Transform coatDock;
        private Renderer[] bottleRenderers;
        private bool[] originalForcedOff;
        private NpcHandPose hands;
        private AnatomicalHandFrame rightFrame, leftFrame;
        private Transform upper, lower, hand, leftUpper, leftLower, leftHand;
        private PlayerScarfController.MouthAccess mouthAccess;
        private IInteractable pendingInteraction;
        private float elapsed, displayedMl, beforeSipMl;
        private int bottleId, resumeAfterFrame;
        private bool freshClick = true, repeatWhileHeld, gulpPending, committed, released, rightCaptured, leftCaptured;
        private Vector3 gripLocal, mouthLocal, capLocal;
        private Quaternion capRotation;
        private bool equipmentOverlay;
        private string activeActionClip;
        public BottlePhase Phase { get; private set; }
        public VodkaBottleView Bottle { get; private set; }
        public float PhaseProgress => Mathf.Clamp01(elapsed / Duration);
        public bool IsBottleConcealed { get; private set; }
        public float MouthContactError { get; private set; }
        public int CompletedSips { get; private set; }
        public int ThrownBottles { get; private set; }
        public bool OccupiesHand => Phase != BottlePhase.Hidden;
        public void BindBusRide(CityBusRideController ride) => busRide = ride;
        public static bool IsHandOccupied(GameObject actor) => actor != null &&
            actor.TryGetComponent(out PlayerVodkaBottleController owner) && owner.isActiveAndEnabled && owner.OccupiesHand;

        public static PlayerVodkaBottleController Install(PlayerRuntime runtime)
        {
            if (!(runtime.Visual is Player3DCharacterPresentation presentation)) return null;
            var owner = runtime.GameObject.AddComponent<PlayerVodkaBottleController>();
            owner.player = runtime; owner.visual = presentation;
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
            owner.jacket = presentation.Registry.GetComponentInParent<PlayerJacketCloth>() ??
                presentation.Registry.GetComponentInChildren<PlayerJacketCloth>();
            return owner;
        }

        private Transform Bone(Player3DAnatomicalPart part) => visual.Registry.TryGetPart(part, out var binding) ? binding.Bone : null;
        private bool Equipped => GameSessionState.IsInventoryItemEquipped(InventoryItemId.VodkaBottle);
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
            bool held = GameInput.IsHeld(GameInputAction.VodkaSip, GameInputContext.Menu);
            if (!CanRead || Paused) { freshClick = true; repeatWhileHeld = false; }
            else if (!held) { freshClick = false; repeatWhileHeld = false; }
            if (Paused || !GameCursor.ApplicationFocused) return;
            if (!CanPresent)
            {
                // Higher-priority actions own this same rig. Never refund a swallowed dose.
                Suspend(); return;
            }
            if (!SwitchPresentationMode(SeatedEquipment)) { Suspend(); return; }
            if (SeatedEquipment && !SeatSipReady && (Phase == BottlePhase.Sipping || Phase == BottlePhase.MouthAccess))
            {
                repeatWhileHeld = false;
                displayedMl = GameSessionState.VodkaBottleRemainingMl;
                if (committed && displayedMl <= 0f) StartPhase(BottlePhase.Throwing, PlayerVodkaActions.ThrowClip);
                else Carry();
            }
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
            if ((!Equipped || pendingInteraction != null) && Phase != BottlePhase.Stowing &&
                Phase != BottlePhase.Throwing && !(Phase == BottlePhase.Sipping && committed && GameSessionState.VodkaBottleRemainingMl == 0))
                StartPhase(BottlePhase.Stowing, PlayerVodkaActions.StowClip);
            if (Phase == BottlePhase.Carrying)
            {
                if (!repeatWhileHeld) { mouthAccess?.Dispose(); mouthAccess = null; }
                elapsed += Time.deltaTime;
                if (equipmentOverlay) visual.SampleEquipmentPose(this, elapsed);
                else visual.UpdateCarryPose(this, elapsed);
                if (!freshClick && CanRead && (GameInput.WasPressed(GameInputAction.VodkaSip, GameInputContext.Gameplay) ||
                    repeatWhileHeld && held))
                    TrySip();
                return;
            }
            if (Phase == BottlePhase.MouthAccess)
            {
                if (mouthAccess == null || mouthAccess.IsReady) StartPhase(BottlePhase.Sipping, PlayerVodkaActions.SipClip);
                return;
            }
            elapsed += Mathf.Max(0f, Time.deltaTime);
            float progress = Mathf.Clamp01(elapsed / Duration);
            if (equipmentOverlay) visual.SampleEquipmentPose(this, elapsed);
            else visual.SampleOwnedClip(this, progress);
            if (Phase == BottlePhase.Sipping && !committed && progress >= PlayerVodkaActions.SipCommitProgress)
            {
                committed = true;
                if (GameSessionState.TryDrinkVodkaSip(bottleId, Mathf.RoundToInt(beforeSipMl), out var result))
                { CompletedSips++; displayedMl = result.RemainingMl; gulpPending = true; }
                else { displayedMl = GameSessionState.VodkaBottleRemainingMl; repeatWhileHeld = false; }
            }
            // Release is applied after this frame's final IK/contact pose in LateUpdate.
            if (Phase == BottlePhase.Throwing && progress >= .6f) released = true;
            if (progress < 1f) return;
            if (Phase == BottlePhase.Drawing) StartPhase(BottlePhase.Opening, PlayerVodkaActions.OpenClip);
            else if (Phase == BottlePhase.Opening) Carry();
            else if (Phase == BottlePhase.Sipping)
            {
                if (!repeatWhileHeld || !GameSessionState.CanDrinkVodkaSip)
                { mouthAccess?.Dispose(); mouthAccess = null; }
                if (displayedMl <= 0f) StartPhase(BottlePhase.Throwing, PlayerVodkaActions.ThrowClip);
                else Carry();
            }
            else if (Phase == BottlePhase.Stowing)
            {
                FinishHandoff();
            }
            else if (Phase == BottlePhase.Throwing && Bottle == null) FinishHandoff();
        }

        private bool Prepare()
        {
            if (!PlayerVodkaActions.TryAttach(visual.Registry)) return false;
            if (Bottle == null) Bottle = VodkaBottleView.Create(transform);
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
            equipmentContacts ??= visual.Registry.GetComponent<PlayerEquipmentContacts>() ??
                visual.Registry.gameObject.AddComponent<PlayerEquipmentContacts>();
            equipmentContacts.Register(this, new[] { coatDock }, Bottle.gameObject);
            bottleId = GameSessionState.VodkaBottleId;
            displayedMl = GameSessionState.VodkaBottleRemainingMl;
            gripLocal = Bottle.transform.InverseTransformPoint(Bottle.Grip.position);
            mouthLocal = Bottle.transform.InverseTransformPoint(Bottle.Mouth.position);
            if (Bottle.Cap != null)
            { capLocal = Bottle.Cap.localPosition; capRotation = Bottle.Cap.localRotation; }
            Bottle.SetOpened(false); Bottle.SetRemaining(displayedMl / 500f);
            rightCaptured = hands.TryCaptureGrip(this, false, NpcHandGripKind.Bottle, exclusive: true);
            return rightCaptured;
        }

        public bool TrySip()
        {
            if (Phase != BottlePhase.Carrying || !CanRead || !GameSessionState.CanDrinkVodkaSip) return false;
            beforeSipMl = displayedMl; committed = false;
            repeatWhileHeld = GameInput.IsHeld(GameInputAction.VodkaSip, GameInputContext.Menu);
            mouthAccess ??= PlayerScarfController.RequireMouthAccess(player, this);
            Phase = BottlePhase.MouthAccess;
            return true;
        }

        public bool TryPrepareInteraction(IInteractable target)
        {
            if (!OccupiesHand || target == null) return false;
            if (pendingInteraction != null) return true;
            if (target is IHandEquipmentSeat seat && seat.AllowsHandEquipment &&
                (Phase == BottlePhase.Carrying || Phase == BottlePhase.MouthAccess || Phase == BottlePhase.Sipping && displayedMl > 0f))
            {
                if (Phase != BottlePhase.Carrying) Carry();
                if (SwitchPresentationMode(true)) return false;
            }
            pendingInteraction = target;
            player.Interactor.SetOwnedInteractKeyClaim(this, true);
            if (Phase == BottlePhase.Throwing || Phase == BottlePhase.Sipping && committed && displayedMl <= 0f) return true;
            StartPhase(BottlePhase.Stowing, PlayerVodkaActions.StowClip);
            return true;
        }

        public void RequestContextualStow()
        {
            if (!OccupiesHand || Phase == BottlePhase.Stowing || Phase == BottlePhase.Throwing) return;
            repeatWhileHeld = false;
            StartPhase(BottlePhase.Stowing, PlayerVodkaActions.StowClip);
        }

        private bool SwitchPresentationMode(bool overlay)
        {
            if (equipmentOverlay == overlay) return true;
            visual.ReleaseOwnedClip(this);
            visual.ReleaseCarryPose(this);
            visual.ReleaseEquipmentPose(this);
            equipmentOverlay = overlay;
            if (!OccupiesHand) return true;
            bool carry = Phase == BottlePhase.Carrying || Phase == BottlePhase.MouthAccess;
            string clip = carry ? PlayerVodkaActions.CarryClip : activeActionClip;
            if (overlay)
                return visual.TryAcquireEquipmentPose(this, clip, rightArmOnly: carry) && visual.SampleEquipmentPose(this, elapsed);
            if (carry)
                return visual.TryAcquireCarryPose(this, clip) && visual.UpdateCarryPose(this, elapsed);
            if (!visual.TryAcquireClip(this, clip)) return false;
            visual.SetOwnedClipLocomotion(this, true);
            return visual.SampleOwnedClip(this, PhaseProgress);
        }

        private void FinishHandoff()
        {
            IInteractable target = pendingInteraction;
            CancelPendingInteraction(); Suspend(); resumeAfterFrame = Time.frameCount + 1;
            if (target != null && (!(target is Object obj) || obj != null) && target.CanInteract(player.Interactor))
                target.Interact(player.Interactor);
        }

        private float Duration => Phase == BottlePhase.Drawing ? PlayerVodkaActions.DrawSeconds :
            Phase == BottlePhase.Opening ? PlayerVodkaActions.OpenSeconds :
            Phase == BottlePhase.Sipping ? PlayerVodkaActions.SipSeconds :
            Phase == BottlePhase.Stowing ? PlayerVodkaActions.StowSeconds : PlayerVodkaActions.ThrowSeconds;
        private void StartPhase(BottlePhase phase, string clip)
        {
            if (phase != BottlePhase.Sipping) { mouthAccess?.Dispose(); mouthAccess = null; }
            if (phase != BottlePhase.Opening && leftCaptured)
            { hands.ReleaseGrip(this, true); leftCaptured = false; }
            if (phase != BottlePhase.Drawing && phase != BottlePhase.Stowing) jacket?.ClearWeaponAccess(this);
            visual.ReleaseCarryPose(this);
            bool acquired = equipmentOverlay ? visual.TryAcquireEquipmentPose(this, clip) :
                visual.TryAcquireClip(this, clip, preserveRecoveryPose: visual.OwnsClip(this));
            if (!acquired) { Suspend(); return; }
            if (!equipmentOverlay) visual.SetOwnedClipLocomotion(this, true);
            activeActionClip = clip;
            elapsed = 0f; Phase = phase; released = false;
            if (equipmentOverlay) visual.SampleEquipmentPose(this, 0f);
            else visual.SampleOwnedClip(this, 0f);
        }
        private void Carry()
        {
            jacket?.ClearWeaponAccess(this);
            if (leftCaptured) hands.ReleaseGrip(this, true);
            leftCaptured = false;
            visual.ReleaseOwnedClip(this);
            bool acquired = equipmentOverlay ? visual.TryAcquireEquipmentPose(this, PlayerVodkaActions.CarryClip, rightArmOnly: true) :
                visual.TryAcquireCarryPose(this, PlayerVodkaActions.CarryClip);
            if (!acquired) { Suspend(); return; }
            // Complete the opening even if a long frame skipped its final
            // presentation sample and the cap release in LateUpdate.
            Bottle.SetOpened(true);
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
            BottlePhase.Carrying or BottlePhase.MouthAccess => 1f,
            BottlePhase.Drawing => Smooth(.76f, 1f, progress),
            BottlePhase.Opening => 1f - Smooth(0f, .22f, progress) + Smooth(.76f, 1f, progress),
            BottlePhase.Stowing => 1f - Smooth(0f, .24f, progress),
            BottlePhase.Throwing => 1f - Smooth(0f, .20f, progress),
            _ => 0f
        };

        private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
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
            if (Bottle == null || !OccupiesHand) return;
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
            float sip = Phase == BottlePhase.Sipping ? Smooth(.05f, .35f, progress) * (1f - Smooth(.68f, .98f, progress)) : 0f;
            if (sip > 0f)
            {
                // The mouth stays anchored while the base rises with the remaining volume.
                Vector3 drinkAxis = (-transform.forward + Vector3.up * Mathf.Lerp(-.04f, -.65f, 1f - shown / VodkaBottleState.FullMl)).normalized;
                // One deliberate tip at the swallow, with the lip and neck
                // contacts retained throughout the raise/drink/lower cycle.
                float gulp = Smooth(.38f, PlayerVodkaActions.SipCommitProgress, progress) * (1f - Smooth(.53f, .66f, progress));
                drinkAxis = Quaternion.AngleAxis(-5f * gulp, transform.right) * drinkAxis;
                Quaternion drinkRotation = Quaternion.LookRotation(Vector3.Cross(transform.right, drinkAxis), drinkAxis);
                Vector3 lip = visual.Registry.Anchors.Mouth.position + transform.forward * .008f;
                Vector3 drinkPosition = lip - drinkRotation * mouthLocal;
                rotation = Quaternion.Slerp(rotation, drinkRotation, sip);
                position = Vector3.Lerp(position, drinkPosition, sip);
                Bottle.transform.SetPositionAndRotation(position, rotation);
                MouthContactError = Vector3.Distance(Bottle.Mouth.position, lip);
            }
            if (busRide != null) position = busRide.ConstrainHeldBottlePosition(position, rotation);
            Bottle.transform.SetPositionAndRotation(position, rotation);
            if (sip > 0f)
                MouthContactError = Vector3.Distance(Bottle.Mouth.position, visual.Registry.Anchors.Mouth.position + transform.forward * .008f);
            // Imported contact empties retain the FBX authoring-root roll.
            // Their positions are measured anchors; the bottle wrapper owns
            // the physical cylinder axes used by both liquid and hand contact.
            Pose rightWrist = rightFrame.ResolveCylinderPose(Bottle.Grip.position, Bottle.transform.up, -Bottle.transform.forward);
            // Draw/open paths already own an elbow that clears the body.
            // Correct the neck contact without replacing that authored plane.
            Vector3 rightElbow = coatTransfer || Phase == BottlePhase.Opening ? lower.position :
                upper.position + transform.right * .30f - Vector3.up * .20f;
            if (seatedWeight > 0f)
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
            if (Phase == BottlePhase.Opening && Bottle.Cap != null)
            {
                float leftLift = seatedLift * Smooth(.08f, .35f, progress) * (1f - Smooth(.82f, 1f, progress));
                if (Mathf.Abs(leftLift) > .0001f)
                    LimbTwoBoneIk.Solve(leftUpper, leftLower, leftHand, leftHand.position + transform.up * leftLift,
                        leftHand.rotation, leftLower.position + transform.up * (leftLift * .5f), 1f, float.PositiveInfinity, true);
                Bottle.Cap.localPosition = capLocal;
                Bottle.Cap.localRotation = capRotation;
                bool touching = progress >= PlayerVodkaActions.CapContactProgress && progress < PlayerVodkaActions.CapReleaseProgress;
                if (touching)
                {
                    if (!leftCaptured) leftCaptured = hands.TryCaptureGrip(this, true, NpcHandGripKind.Cylindrical, exclusive: true);
                    float gripAmount = Smooth(PlayerVodkaActions.CapContactProgress, .49f, progress) *
                        (1f - Smooth(.82f, PlayerVodkaActions.CapReleaseProgress, progress));
                    Vector3 cap = Bottle.Cap.position;
                    if (progress < PlayerVodkaActions.CapOpenProgress)
                    {
                        cap += Bottle.transform.up * (.035f * Smooth(PlayerVodkaActions.CapTwistEndProgress,
                            PlayerVodkaActions.CapOpenProgress, progress));
                        Vector3 outside = Vector3.ProjectOnPlane(leftUpper.position - upper.position, transform.up).normalized;
                        Pose wrist = leftFrame.ResolveCylinderPose(cap, Bottle.transform.up, -outside);
                        // The authored approach/return stays in front of the
                        // chest. IK only closes the small cap-contact seam.
                        float contact = Smooth(PlayerVodkaActions.CapContactProgress, .49f, progress) *
                            (1f - Smooth(PlayerVodkaActions.CapTwistEndProgress, PlayerVodkaActions.CapOpenProgress, progress));
                        LimbTwoBoneIk.Solve(leftUpper, leftLower, leftHand, wrist.position, wrist.rotation,
                            leftUpper.position + outside * .30f + transform.forward * .24f - transform.up * .18f,
                            contact, float.PositiveInfinity, true);
                    }
                    else cap = leftHand.position + leftHand.rotation * leftFrame.CylinderCentreInHand;
                    hands.SetOwnedGrip(this, true, gripAmount);
                    Bottle.Cap.position = cap;
                    Bottle.Cap.rotation = Quaternion.AngleAxis(Smooth(PlayerVodkaActions.CapContactProgress,
                        PlayerVodkaActions.CapTwistEndProgress, progress) * 270f, Bottle.transform.up) *
                        Bottle.Cap.parent.rotation * capRotation;
                }
                if (progress >= PlayerVodkaActions.CapReleaseProgress) Bottle.SetOpened(true);
            }
            Bottle.SetRemaining(shown / VodkaBottleState.FullMl);
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
                ThrownVodkaBottle.Release(thrown, inheritedVelocity + transform.right * 4.2f + transform.forward * 1.2f + Vector3.up * 1.3f,
                    transform.forward * 11f + transform.up * 3f, transform, vehicleHull);
                ThrownBottles++;
            }
        }

        private void CancelPendingInteraction()
        { pendingInteraction = null; player.Interactor?.SetOwnedInteractKeyClaim(this, false); }
        private void Suspend()
        {
            visual?.ReleaseOwnedClip(this); visual?.ReleaseCarryPose(this); visual?.ReleaseEquipmentPose(this);
            jacket?.ClearWeaponAccess(this);
            mouthAccess?.Dispose(); mouthAccess = null;
            if (rightCaptured) hands?.ReleaseGrip(this, false);
            if (leftCaptured) hands?.ReleaseGrip(this, true);
            rightCaptured = leftCaptured = false;
            if (Bottle != null) Destroy(Bottle.gameObject);
            equipmentContacts?.Unregister(this);
            Bottle = null; bottleRenderers = null; originalForcedOff = null; IsBottleConcealed = false;
            Phase = BottlePhase.Hidden; freshClick = true; repeatWhileHeld = gulpPending = false;
            CancelPendingInteraction();
        }
        private void OnDisable() { if (visual != null) Suspend(); }
        private void OnDestroy()
        {
            if (visual != null) Suspend();
            if (coatDock != null) Destroy(coatDock.gameObject);
        }
    }
}
