using System;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageOutdoorTask { TakeBasket, PutBasket, TakeShovel, ReturnShovel, ClearSnow, HoldLid, HoldGate }

    /// <summary>Shared, positioned actions transfer the same household objects to ordinary free locomotion.</summary>
    [DefaultExecutionOrder(400)]
    public sealed class VillageOutdoorHelpController : MonoBehaviour
    {
        private AlpineVillageRoot village;
        private AlpineVillageLifeController life;
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private Player3DCharacterPresentation presentation;
        private VillageWorkroomHandContacts hands;
        private bool ready, owns, committed, exiting, carryShovel, cancelAtSeam, lidOpenSound, lidCloseSound;
        private int basketIndex = -1, actionBasketIndex = -1, actionIndex, clearingStage, gatePassages;
        private float carryTime, holdTime;
        private VillageOutdoorPlayerAction enter, loop, exit;
        private Transform carried;
        private Vector3 leftTarget, rightTarget, canonicalLeftTarget, canonicalRightTarget;
        private Quaternion lidRestRotation;
        private Vector3 lidRestPosition;
        private Vector3 supportDelta;
        private Quaternion supportRotationDelta;
        public bool HasCarry => carried != null;
        public bool IsBusy => owns;
        public Transform CarriedProp => carried;
        public PlayerDoorActionPlan ActivePlan { get; private set; }
        public VillageOutdoorTask ActiveTask { get; private set; }
        public int CompletedActionCount { get; private set; }
        public Transform LeftGrip => hands?.LeftGrip;
        public Transform RightGrip => hands?.RightGrip;
        public Vector3 LeftTarget => leftTarget;
        public Vector3 RightTarget => rightTarget;
        public float LeftContactWeight { get; private set; }
        public float RightContactWeight { get; private set; }
        public float ContactWeight => Mathf.Max(LeftContactWeight, RightContactWeight);
        public float RightWristDistance => hands?.RightWristDistance ?? 0f;
        public float RightReachLimit => hands?.RightReachLimit ?? 0f;
        public VillageOutdoorPlayerAction CurrentAction { get; private set; }
        public float ActionSeconds { get; private set; }

        public static VillageOutdoorHelpController Create(AlpineVillageRoot owner)
        {
            var host = new GameObject("Village voluntary outdoor help");
            host.transform.SetParent(owner.transform, false);
            var result = host.AddComponent<VillageOutdoorHelpController>();
            result.Initialize(owner); return result;
        }

        private void Initialize(AlpineVillageRoot owner)
        {
            village = owner; life = owner.Life; player = owner.Player;
            controller = player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            presentation = player.Visual as Player3DCharacterPresentation;
            ready = presentation != null && VillageOutdoorPlayerActions.TryAttach(presentation.Registry);
            if (!ready) throw new InvalidOperationException("Outdoor help requires the authored production hero actions.");
            hands = new VillageWorkroomHandContacts(player.GameObject.transform, presentation.Registry);
            controller.PhaseChanged += PhaseChanged;
            controller.InteractionCompleted += Completed;
            for (int i = 0; i < 2; i++)
            {
                Install(VillageOutdoorTask.TakeBasket, i, life.Plan.Pickups[i]);
                Install(VillageOutdoorTask.PutBasket, i, life.Plan.Deliveries[i]);
                Install(VillageOutdoorTask.PutBasket, i + 2, life.Plan.Pickups[i]);
            }
            Install(VillageOutdoorTask.TakeShovel, 0, life.ShovelRestPosition);
            Install(VillageOutdoorTask.ReturnShovel, 0, life.ShovelRestPosition);
            for (int i = 0; i < life.Clearing.Patches.Count; i++)
                Install(VillageOutdoorTask.ClearSnow, i, life.Clearing.Patches[i].Center);
            Install(VillageOutdoorTask.HoldLid, 0, life.Plan.StationWork);
            Install(VillageOutdoorTask.HoldGate, 0, life.GateFrame.position);
        }

        private void Install(VillageOutdoorTask task, int index, Vector3 point)
        {
            var host = new GameObject(task + " " + index);
            host.transform.SetParent(transform, false); host.transform.position = point + Vector3.up * .75f;
            var trigger = host.AddComponent<SphereCollider>(); trigger.isTrigger = true; trigger.radius = .6f;
            host.AddComponent<VillageOutdoorInteraction>().Initialize(this, task, index);
        }

        public PlayerDoorActionPlan GetPlan(VillageOutdoorTask task, int index = 0)
        {
            Vector3 ground, facing, interaction;
            if (task == VillageOutdoorTask.TakeBasket || task == VillageOutdoorTask.PutBasket)
            {
                Vector3 support = task == VillageOutdoorTask.TakeBasket || index >= 2 ?
                    life.Plan.Pickups[index % 2] : life.Plan.Deliveries[index];
                facing = -life.Plan.Forward;
                // Keep the production capsule beyond the stand's .255 m front
                // edge, including its radius and skin. The support delta below
                // keeps the hands and the basket on the actual sloping support.
                ground = life.Plan.Ground(support - facing * .64f); interaction = support + Vector3.up * .7f;
            }
            else if (task == VillageOutdoorTask.TakeShovel || task == VillageOutdoorTask.ReturnShovel)
            {
                facing = life.ShovelRestRotation * Vector3.forward;
                ground = life.Plan.Ground(life.ShovelRestPosition - facing * .48f);
                interaction = life.ShovelRestPosition + Vector3.up * .8f;
            }
            else if (task == VillageOutdoorTask.ClearSnow)
            {
                VillageSnowPatch patch = life.Clearing.Patches[index];
                ground = patch.Dock; facing = patch.Facing; interaction = patch.Center + Vector3.up * .6f;
            }
            else if (task == VillageOutdoorTask.HoldLid)
            { ground = life.Plan.StationWork + life.Plan.StationForward * .06f; facing = -life.Plan.StationForward; interaction = life.StationLid.position; }
            else
            {
                interaction = life.Gate.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.GateLeaf, "Handle"));
                facing = life.Gate.forward;
                ground = interaction - Quaternion.LookRotation(facing) * new Vector3(.18f, .82f, .52f);
                ground = life.Plan.Ground(ground);
            }
            // The capsule stands on the triangulated ground, not the higher
            // analytic envelope used to support scenery between coarse vertices.
            // The station action instead stands on its real raised platform.
            if (task != VillageOutdoorTask.HoldLid)
                ground.y = AlpineVillageTerrainSampler.SampleMeshHeight(village.Plan, new Vector2(ground.x, ground.z));
            return PlayerDoorActionPlan.CreateStationary(interaction,
                ground + Vector3.up * PlayerFactory.GroundedRootOffset, facing);
        }

        public bool CanBegin(VillageOutdoorTask task, int index = 0)
        {
            if (!ready || !isActiveAndEnabled || owns || controller.IsActive || !player.Interactor.InputEnabled ||
                player.Interactor.InteractKeyClaimed || GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning ||
                CounterMenuInput.IsBlockedByOtherUi()) return false;
            if (Mathf.Abs(player.GameObject.transform.position.y - GetPlan(task, index).EntryRootPosition.y) > InitialHeightTolerance(task)) return false;
            switch (task)
            {
                case VillageOutdoorTask.TakeBasket: return !HasCarry && life.CanReserveFirewood(index);
                case VillageOutdoorTask.PutBasket: return HasCarry && !carryShovel &&
                    (index >= 2 ? index - 2 == basketIndex : life.CanReserveFirewoodStand(index));
                case VillageOutdoorTask.TakeShovel: return !HasCarry && life.CanReserveShovel;
                case VillageOutdoorTask.ReturnShovel: return HasCarry && carryShovel;
                case VillageOutdoorTask.ClearSnow: return HasCarry && carryShovel &&
                    !life.Clearing.IsComplete(life.Clearing.Patches[index].StableId);
                case VillageOutdoorTask.HoldLid: return !HasCarry && life.CanReserveStationHelp;
                case VillageOutdoorTask.HoldGate: return !HasCarry && life.CanReserveGateHelp;
                default: return false;
            }
        }

        public bool TryBegin(VillageOutdoorTask task, int index = 0)
        {
            if (!CanBegin(task, index)) return false;
            ActivePlan = GetPlan(task, index); ActiveTask = task; actionIndex = index;
            actionBasketIndex = task == VillageOutdoorTask.TakeBasket ? index : basketIndex;
            if (task == VillageOutdoorTask.TakeBasket && !life.ReserveFirewood(index)) return false;
            if (task == VillageOutdoorTask.TakeShovel && !life.ReserveShovel(true)) return false;
            if (task == VillageOutdoorTask.PutBasket && index < 2 && !life.ReserveFirewoodStand(index)) return false;
            if (task == VillageOutdoorTask.HoldLid && !life.ReserveStationHelp(true)) return false;
            if (task == VillageOutdoorTask.HoldGate && !life.ReserveGateHelp(true)) return false;
            owns = true; committed = false; exiting = false; cancelAtSeam = false; holdTime = 0f;
            lidOpenSound = lidCloseSound = false;
            gatePassages = life.GatePassages;
            if (task == VillageOutdoorTask.HoldLid)
            { lidRestPosition = life.StationLid.position; lidRestRotation = life.StationLid.rotation; }
            if (task == VillageOutdoorTask.ClearSnow)
                clearingStage = life.Clearing.Stage(life.Clearing.Patches[index].StableId);
            SelectClips(task);
            CurrentAction = enter; ActionSeconds = 0f;
            supportDelta = Vector3.zero; supportRotationDelta = Quaternion.identity;
            if (task == VillageOutdoorTask.TakeBasket || task == VillageOutdoorTask.PutBasket ||
                task == VillageOutdoorTask.TakeShovel || task == VillageOutdoorTask.ReturnShovel)
            {
                bool shovel = task == VillageOutdoorTask.TakeShovel || task == VillageOutdoorTask.ReturnShovel;
                var rest = VillageOutdoorPlayerActions.Sample(shovel ? VillageOutdoorPlayerAction.ShovelPickup : VillageOutdoorPlayerAction.BasketPickup, 0f).PropFromGround;
                Vector3 actual = shovel ? life.ShovelRestPosition :
                    (task == VillageOutdoorTask.TakeBasket || index >= 2 ? life.Plan.Pickups[index % 2] : life.Plan.Deliveries[index]) +
                    Vector3.up * AlpineVillageLifePlan.StandHeight;
                Quaternion actualRotation = shovel ? life.ShovelRestRotation : Quaternion.LookRotation(-life.Plan.Forward);
                supportDelta = actual - (ActivePlan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset + ActivePlan.EntryRotation * rest.position);
                supportRotationDelta = actualRotation * Quaternion.Inverse(ActivePlan.EntryRotation * rest.rotation);
            }
            if (HasCarry) presentation.UpdateCarryPose(this, 0f);
            try
            {
                Vector3[] approach = GateApproach(task);
                bool accepted = approach == null ? controller.BeginPositioned(Definition(), ActivePlan.EntryPose,
                    ActivePlan.ActionHipPosition, ActivePlan.ExitPose, InitialHeightTolerance(task)) :
                    controller.BeginPositioned(Definition(), ActivePlan.EntryPose, ActivePlan.ActionHipPosition, ActivePlan.ExitPose,
                        new PlayerAnimatedInteractionPelvisTransition(ActivePlan.ActionHipPosition, .5f, .5f, .5f, .5f),
                        InitialHeightTolerance(task), approach, approach.Length);
                if (!accepted) Finish(false);
                return accepted;
            }
            catch { Finish(false); throw; }
        }

        private Vector3[] GateApproach(VillageOutdoorTask task)
        {
            if (task != VillageOutdoorTask.HoldGate) return null;
            Vector3 local = life.GateFrame.InverseTransformPoint(player.GameObject.transform.position);
            if (local.x < -.36f || local.z < -1.55f) return null;
            Vector3 Corner(float x, float z)
            {
                Vector3 point = life.GateFrame.TransformPoint(new Vector3(x, 0f, z));
                point.y = AlpineVillageTerrainSampler.SampleMeshHeight(village.Plan, new Vector2(point.x, point.z));
                return point + Vector3.up * PlayerFactory.GroundedRootOffset;
            }
            // The open leaf and its short fence returns separate the working
            // dock from the yard. Go beyond both authored outer posts on the
            // street side. A returning visitor waits outside the gate: leave
            // that actual body clear too while its passage is reserved.
            Vector3 visitor = life.GateFrame.InverseTransformPoint(life.GateVisitor.Actor.transform.position);
            float outsideRight = Mathf.Max(2.25f, local.x, visitor.x + .9f);
            float outsideFront = Mathf.Max(1.8f, visitor.z + .9f);
            return new[] { Corner(outsideRight, local.z), Corner(outsideRight, outsideFront),
                // This gap also borders house 09: moving farther left enters
                // its side wall. These centres leave .38 m to either solid.
                Corner(-1.035f, outsideFront), Corner(-1.035f, -.60f) };
        }

        // Going around the fence spans the sloping yard; this admits only
        // initial approach height. The shared final grounded tolerance stays exact.
        private static float InitialHeightTolerance(VillageOutdoorTask task) => task == VillageOutdoorTask.HoldGate ? .75f : .35f;

        private void SelectClips(VillageOutdoorTask task)
        {
            switch (task)
            {
                case VillageOutdoorTask.TakeBasket: enter = VillageOutdoorPlayerAction.BasketPickup; loop = exit = VillageOutdoorPlayerAction.BasketCarry; break;
                case VillageOutdoorTask.PutBasket: enter = loop = VillageOutdoorPlayerAction.BasketCarry; exit = VillageOutdoorPlayerAction.BasketPlace; break;
                case VillageOutdoorTask.TakeShovel: enter = VillageOutdoorPlayerAction.ShovelPickup; loop = exit = VillageOutdoorPlayerAction.ShovelCarry; break;
                case VillageOutdoorTask.ReturnShovel: enter = loop = VillageOutdoorPlayerAction.ShovelCarry; exit = VillageOutdoorPlayerAction.ShovelPutBack; break;
                case VillageOutdoorTask.ClearSnow: enter = exit = VillageOutdoorPlayerAction.ShovelCarry; loop = VillageOutdoorPlayerAction.ShovelWork; break;
                case VillageOutdoorTask.HoldLid: enter = VillageOutdoorPlayerAction.LidEnter; loop = VillageOutdoorPlayerAction.LidHold; exit = VillageOutdoorPlayerAction.LidExit; break;
                default: enter = VillageOutdoorPlayerAction.GateEnter; loop = VillageOutdoorPlayerAction.GateHold; exit = VillageOutdoorPlayerAction.GateExit; break;
            }
        }

        private PlayerAnimatedInteractionDefinition Definition()
        {
            bool heldAction = ActiveTask == VillageOutdoorTask.ClearSnow || ActiveTask == VillageOutdoorTask.HoldLid || ActiveTask == VillageOutdoorTask.HoldGate;
            int Count(VillageOutdoorPlayerAction action) => Mathf.RoundToInt(VillageOutdoorPlayerActions.Duration(action) * 24f);
            bool IsCarry(VillageOutdoorPlayerAction action) => action == VillageOutdoorPlayerAction.BasketCarry || action == VillageOutdoorPlayerAction.ShovelCarry;
            return new PlayerAnimatedInteractionDefinition(VillageOutdoorPlayerActions.ClipName(enter),
                VillageOutdoorPlayerActions.ClipName(loop), VillageOutdoorPlayerActions.ClipName(exit),
                enterFrameCount: IsCarry(enter) ? 1 : Count(enter), enterFramesPerSecond: 24f,
                loopFrameCount: heldAction ? Count(loop) : 1, loopFramesPerSecond: 24f,
                exitFrameCount: IsCarry(exit) ? 1 : Count(exit), exitFramesPerSecond: 24f);
        }

        private void PhaseChanged(PlayerAnimatedInteractionPhase phase)
        {
            if (!owns) return;
            if (phase == PlayerAnimatedInteractionPhase.Idle) { Finish(false); return; }
            if (phase != PlayerAnimatedInteractionPhase.Looping) return;
            if (ActiveTask == VillageOutdoorTask.TakeBasket) StartCarry(life.Baskets[actionIndex], false, actionIndex);
            if (ActiveTask == VillageOutdoorTask.TakeShovel) StartCarry(life.Shovel, true, -1);
            if (ActiveTask == VillageOutdoorTask.HoldLid) life.SetStationHelperHolding(true);
            if (ActiveTask == VillageOutdoorTask.HoldGate) life.SetGateHelperHolding(true);
            if (cancelAtSeam)
            {
                exiting = true;
                controller.RequestExitAtLoopBoundaryWithClip(VillageOutdoorPlayerActions.ClipName(exit));
                return;
            }
            if (ActiveTask == VillageOutdoorTask.ClearSnow)
                controller.RequestExitAtLoopBoundaryWithClip(VillageOutdoorPlayerActions.ClipName(exit));
            else if (ActiveTask != VillageOutdoorTask.HoldLid && ActiveTask != VillageOutdoorTask.HoldGate)
                controller.RequestExit();
        }

        private void Update()
        {
            if (GameTimeScaleRuntime.IsPaused) return;
            if (HasCarry)
            { carryTime += Time.deltaTime; presentation.UpdateCarryPose(this, carryTime); }
            if (!owns || controller.Phase != PlayerAnimatedInteractionPhase.Looping) return;
            holdTime += Time.deltaTime;
            if (exiting) return;
            if (ActiveTask == VillageOutdoorTask.HoldLid && (life.StationHelpCompleted || holdTime > 18f) ||
                ActiveTask == VillageOutdoorTask.HoldGate && (life.GatePassages > gatePassages || holdTime > 20f))
            { exiting = true; controller.RequestExitAtLoopBoundaryWithClip(VillageOutdoorPlayerActions.ClipName(exit)); }
        }

        private void LateUpdate() => RefreshContacts();
        public void RefreshContacts()
        {
            LeftContactWeight = RightContactWeight = 0f;
            if (!ready) return;
            bool playing = owns && controller.Phase != PlayerAnimatedInteractionPhase.Positioning && controller.Phase != PlayerAnimatedInteractionPhase.Idle;
            if (!playing && !HasCarry) return;
            Quaternion facing = playing ? ActivePlan.EntryRotation : player.GameObject.transform.rotation;
            Vector3 ground = (playing ? ActivePlan.EntryRootPosition : player.GameObject.transform.position) - Vector3.up * PlayerFactory.GroundedRootOffset;
            CurrentAction = playing ? (controller.Phase == PlayerAnimatedInteractionPhase.Entering ? enter :
                controller.Phase == PlayerAnimatedInteractionPhase.Exiting ? exit : loop) :
                carryShovel ? VillageOutdoorPlayerAction.ShovelCarry : VillageOutdoorPlayerAction.BasketCarry;
            ActionSeconds = playing ? controller.PhaseProgress * VillageOutdoorPlayerActions.Duration(CurrentAction) : carryTime;
            VillageOutdoorPlayerFrame frame = VillageOutdoorPlayerActions.Sample(CurrentAction, ActionSeconds);
            VillageWorkroomHandContacts.RefreshPresentation(player, controller.IsActive ? controller : null);
            Transform prop = HasCarry ? carried : playing && (ActiveTask == VillageOutdoorTask.TakeBasket || ActiveTask == VillageOutdoorTask.PutBasket) ? life.Baskets[actionBasketIndex] :
                playing && (ActiveTask == VillageOutdoorTask.TakeShovel || ActiveTask == VillageOutdoorTask.ReturnShovel) ? life.Shovel :
                playing && ActiveTask == VillageOutdoorTask.HoldLid ? life.StationLid : null;
            if (!playing && prop != null)
            {
                // Gait moves/turns the pelvis below the carried torso. The real
                // authored grips therefore own the free load's frame, not a
                // ground-space target that would stretch the arms on each step.
                Vector3 rightHand = hands.RightGrip.position, leftHand = hands.LeftGrip.position;
                if (carryShovel)
                {
                    Vector3 up = (rightHand - leftHand).normalized;
                    Vector3 forward = Vector3.ProjectOnPlane(player.GameObject.transform.forward, up).normalized;
                    Quaternion rotation = Quaternion.LookRotation(forward, up);
                    prop.SetPositionAndRotation(rightHand - rotation * new Vector3(0f, 1.05f, 0f), rotation);
                }
                else
                {
                    Vector3 right = (rightHand - leftHand).normalized;
                    Vector3 forward = Vector3.ProjectOnPlane(player.GameObject.transform.forward, right).normalized;
                    Quaternion rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
                    prop.SetPositionAndRotation((rightHand + leftHand) * .5f - rotation * Vector3.up * .43f, rotation);
                }
            }
            if (prop != null)
            {
                bool move = playing && (HasCarry || frame.Held || ActiveTask == VillageOutdoorTask.HoldLid);
                if (move)
                {
                    float supportWeight = 0f;
                    if (playing && (CurrentAction == VillageOutdoorPlayerAction.BasketPickup || CurrentAction == VillageOutdoorPlayerAction.ShovelPickup))
                        supportWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((ActionSeconds - 1.5f) / 1.5f));
                    else if (playing && (CurrentAction == VillageOutdoorPlayerAction.BasketPlace || CurrentAction == VillageOutdoorPlayerAction.ShovelPutBack))
                        supportWeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ActionSeconds / 1.5f));
                    prop.SetPositionAndRotation(ground + facing * frame.PropFromGround.position + supportDelta * supportWeight,
                        Quaternion.Slerp(Quaternion.identity, supportRotationDelta, supportWeight) * facing * frame.PropFromGround.rotation);
                }
            }
            leftTarget = canonicalLeftTarget = ground + facing * frame.LeftGripFromGround;
            rightTarget = canonicalRightTarget = ground + facing * frame.RightGripFromGround;
            if (playing && ActiveTask == VillageOutdoorTask.HoldGate)
                rightTarget = life.Gate.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.GateLeaf, "Handle"));
            LeftContactWeight = frame.LeftContactWeight; RightContactWeight = frame.RightContactWeight;
            // The sampled object owns the contact targets even when locomotion bobs the pelvis.
            if (prop != null && ActiveTask != VillageOutdoorTask.HoldLid)
            {
                bool shovel = prop == life.Shovel;
                var kind = shovel ? VillageLifePropKind.Shovel : VillageLifePropKind.Basket;
                if (frame.ContactWeight > 0f)
                {
                    leftTarget = prop.TransformPoint(VillageLifePropLibrary.GetAnchor(kind, "LeftGrip"));
                    rightTarget = prop.TransformPoint(VillageLifePropLibrary.GetAnchor(kind, "RightGrip"));
                }
            }
            if (!playing) { ApplyContacts(); return; }
            if (ActiveTask == VillageOutdoorTask.HoldLid)
            {
                if (CurrentAction == VillageOutdoorPlayerAction.LidEnter && ActionSeconds >= 1f && !lidOpenSound)
                { lidOpenSound = true; life.PlayHouseholdHinge(life.StationLid.position); }
                if (CurrentAction == VillageOutdoorPlayerAction.LidExit && !lidCloseSound)
                { lidCloseSound = true; life.PlayHouseholdHinge(life.StationLid.position); }
            }
            // A slow render frame can span the contact window. Commit the
            // authored stroke when its first blade-contact time has elapsed.
            if (ActiveTask == VillageOutdoorTask.ClearSnow && CurrentAction == VillageOutdoorPlayerAction.ShovelWork &&
                ActionSeconds >= .7f && !committed)
            {
                committed = life.Clearing.Advance(life.Clearing.Patches[actionIndex].StableId, clearingStage);
                if (committed) life.PlayHouseholdScrape(life.Shovel.position);
            }
            bool putting = ActiveTask == VillageOutdoorTask.PutBasket || ActiveTask == VillageOutdoorTask.ReturnShovel;
            if (putting && controller.Phase == PlayerAnimatedInteractionPhase.Exiting &&
                ActionSeconds >= VillageOutdoorPlayerActions.PickupContactSeconds && !committed)
            {
                committed = true;
                if (ActiveTask == VillageOutdoorTask.PutBasket)
                {
                    if (actionIndex < 2 && GameSessionState.VillageHousehold.TryDeliverBasket(basketIndex, actionIndex))
                        life.Thank(VillageResidentRole.WoodWoman, "village.life.wood.thanks", false);
                    life.PlayHouseholdWood(carried.position);
                    life.ReleaseFirewood(basketIndex, true);
                }
                else
                {
                    life.Shovel.SetPositionAndRotation(life.ShovelRestPosition, life.ShovelRestRotation);
                    life.ReserveShovel(false);
                }
                StopCarry();
            }
            // Releasing the carry layer evaluates the graph. Final contacts
            // belong after that evaluation, including the exact support frame.
            ApplyContacts();
        }

        private void ApplyContacts()
        {
            // The clips already contain the reach. Blend only the difference
            // from the authored support while reaching, then own the real grip.
            // This avoids applying the reach twice or snapping the hand down a
            // sloping support's offset on the first fully held frame.
            Vector3? Correct(Transform grip, Vector3 actual, Vector3 canonical, float weight)
            {
                if (weight >= .9999f) return actual;
                Vector3 offset = actual - canonical;
                return weight > 0f && offset.sqrMagnitude > .00000001f ? grip.position + offset * weight : (Vector3?)null;
            }
            hands.Apply(Correct(hands.RightGrip, rightTarget, canonicalRightTarget, RightContactWeight),
                Correct(hands.LeftGrip, leftTarget, canonicalLeftTarget, LeftContactWeight), 1f);
        }

        private void StartCarry(Transform prop, bool shovel, int index)
        {
            carried = prop; carryShovel = shovel; basketIndex = index; carryTime = 0f;
            if (!presentation.TryAcquireCarryPose(this, VillageOutdoorPlayerActions.ClipName(shovel ?
                VillageOutdoorPlayerAction.ShovelCarry : VillageOutdoorPlayerAction.BasketCarry)))
                throw new InvalidOperationException("The hero's carry pose is already owned.");
            player.Interactor.SetInteractionFilter(this, candidate => candidate is VillageOutdoorInteraction || candidate is VillageResidentGreeting);
        }

        private void StopCarry()
        {
            presentation?.ReleaseCarryPose(this);
            player.Interactor.SetInteractionFilter(this, null);
            carried = null; basketIndex = -1;
        }

        public bool Cancel()
        {
            if (!owns) return false;
            // Once the hands have moved a thing, finish at its nearest authored
            // support/held seam. An in-flight object never jumps home on cancel.
            if (controller.Phase == PlayerAnimatedInteractionPhase.Exiting ||
                (controller.Phase == PlayerAnimatedInteractionPhase.Entering && ActionSeconds >= (ActiveTask == VillageOutdoorTask.HoldLid ? 1f : 1.5f)) ||
                controller.Phase == PlayerAnimatedInteractionPhase.Looping)
            {
                cancelAtSeam = true;
                if (controller.Phase == PlayerAnimatedInteractionPhase.Looping)
                { exiting = true; controller.RequestExitAtLoopBoundaryWithClip(VillageOutdoorPlayerActions.ClipName(exit)); }
                return true;
            }
            controller.CancelActiveInteraction(); Finish(false); return true;
        }
        private void Completed() { if (owns) Finish(true); }
        private void Finish(bool completed)
        {
            if (!owns) return;
            // If a resource left its support, cancellation keeps it in the hero's hands.
            if (!completed && !HasCarry && (ActiveTask == VillageOutdoorTask.TakeBasket || ActiveTask == VillageOutdoorTask.TakeShovel) &&
                CurrentAction == enter && ActionSeconds >= VillageOutdoorPlayerActions.PickupContactSeconds)
                StartCarry(ActiveTask == VillageOutdoorTask.TakeBasket ? life.Baskets[actionIndex] : life.Shovel,
                    ActiveTask == VillageOutdoorTask.TakeShovel, actionIndex);
            owns = false;
            if (completed) CompletedActionCount++;
            if (ActiveTask == VillageOutdoorTask.PutBasket && actionIndex < 2) life.ReleaseFirewoodStand(actionIndex);
            if (ActiveTask == VillageOutdoorTask.TakeBasket && !HasCarry) life.ReleaseFirewood(actionIndex, true);
            if (ActiveTask == VillageOutdoorTask.TakeShovel && !HasCarry) life.ReserveShovel(false);
            if (ActiveTask == VillageOutdoorTask.HoldLid)
            {
                life.StationLid.SetPositionAndRotation(lidRestPosition, lidRestRotation);
                life.ReserveStationHelp(false);
            }
            if (ActiveTask == VillageOutdoorTask.HoldGate)
            {
                if (completed && life.GatePassages > gatePassages)
                    life.Thank(VillageResidentRole.BasketVisitor, "village.life.visitor.thanks", false);
                life.ReserveGateHelp(false);
            }
            if (completed && ActiveTask == VillageOutdoorTask.ClearSnow && committed)
                life.Thank(VillageResidentRole.SnowNeighbor, "village.life.snow.thanks", false);
        }

        private void OnDisable()
        {
            if (owns) { controller?.CancelActiveInteraction(); Finish(false); }
            if (HasCarry)
            {
                if (carryShovel) { life.Shovel.SetPositionAndRotation(life.ShovelRestPosition, life.ShovelRestRotation); life.ReserveShovel(false); }
                else life.ReleaseFirewood(basketIndex, true);
                StopCarry();
            }
        }
        private void OnDestroy()
        {
            if (controller == null) return;
            controller.PhaseChanged -= PhaseChanged; controller.InteractionCompleted -= Completed;
        }
    }

    public sealed class VillageOutdoorInteraction : MonoBehaviour, IInteractable
    {
        private VillageOutdoorHelpController owner;
        private VillageOutdoorTask task;
        private int index;
        private static readonly string[] Keys = { "take_firewood_basket", "put_firewood_basket", "take_village_shovel",
            "return_village_shovel", "clear_village_snow", "hold_station_lid", "hold_village_gate" };
        public string PromptKey => "interaction." + Keys[(int)task];
        public Vector3 InteractionPosition => owner.GetPlan(task, index).InteractionPosition;
        public void Initialize(VillageOutdoorHelpController value, VillageOutdoorTask kind, int id)
        { owner = value; task = kind; index = id; }
        public bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled && owner.CanBegin(task, index);
        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) owner.TryBegin(task, index); }
    }
}
