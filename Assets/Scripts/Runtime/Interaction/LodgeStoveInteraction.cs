using System;
using UnityEngine;

namespace BarPromenade
{
    public enum LodgeStovePhase { Closed, Approaching, Opening, Browsing, PlacingLog, Igniting, Withdrawing, Closing, Returning, Releasing }

    /// <summary>Owns the stove presentation; the shared controller owns the grounded hero.</summary>
    [DefaultExecutionOrder(310)]
    public sealed class LodgeStoveInteraction : MonoBehaviour, IInteractable
    {
        private AlpineVillageRoot village;
        private PlayerAnimatedInteractionController controller;
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private LodgeStoveFirstPerson props;
        private Camera camera;
        private Vector3 cameraStart;
        private Quaternion cameraRotation;
        private float cameraFov;
        private float elapsed;
        private float closingDoorStart;
        private int openedFrame;
        private int releaseFrame;
        private bool exitQueued;
        private bool restoring;
        private bool cameraOwned;
        private Vector3 placingStart;
        private Quaternion logRestRotation;
        private int nextClick;
        private float currentCameraBlend;
        private float exitCameraBlend;
        private bool returningLog;
        private Vector3 looseLogPosition;
        private Quaternion looseLogRotation;
        private static readonly float[] ClickTimes = { LodgeStovePlan.FirstClickSeconds,
            LodgeStovePlan.SecondClickSeconds, LodgeStovePlan.ThirdClickSeconds };
        private readonly RaycastHit[] sightHits = new RaycastHit[16];

        public LodgeStovePlan Plan { get; private set; }
        public LodgeStoveFire Fire { get; private set; }
        public StoveInventoryView InventoryView { get; private set; }
        public Transform LogModel { get; private set; }
        public Transform LighterModel => props?.Lighter;
        public LodgeStovePhase Phase { get; private set; }
        public bool OwnsInteraction => Phase != LodgeStovePhase.Closed;
        public float DoorOpenAmount { get; private set; }
        public string PromptKey => LodgeStoveSessionState.IsBurning
            ? "interaction.open_stove_door" : "interaction.light_stove";
        public Vector3 InteractionPosition => Plan != null ? Plan.EntryPose.RootPosition : transform.position;

        public void Initialize(AlpineVillageRoot owner, Transform lodge)
        {
            village = owner;
            Plan = new LodgeStovePlan(lodge);
            camera = owner.CameraFollow.GetComponent<Camera>();
            controller = owner.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            InventoryView = gameObject.AddComponent<StoveInventoryView>();
            LogModel = VillageLifePropLibrary.Create(VillageLifePropKind.Log, lodge, "Stove Firewood").transform;
            logRestRotation = lodge.rotation;
            RestoreLog();
            var fireObject = new GameObject("Lodge Stove Fire");
            fireObject.transform.SetParent(lodge, false);
            Fire = fireObject.AddComponent<LodgeStoveFire>();
            Fire.Initialize(Plan.FireDock);
            SetDoor(0f);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (village == null || !isActiveAndEnabled || village.IsDormant || OwnsInteraction ||
                interactor != village.Player.Interactor || !interactor.InputEnabled ||
                controller == null || controller.IsActive || BarMinigameModalLock.IsAnyLocked ||
                SceneTransitionService.IsTransitioning || CounterMenuInput.IsBlockedByOtherUi()) return false;
            Vector3 difference = interactor.transform.position - Plan.EntryPose.RootPosition;
            if (Mathf.Abs(difference.y) > .3f || difference.sqrMagnitude > 2.5f * 2.5f ||
                Vector3.Dot(difference, Plan.Lodge.forward) > .55f) return false;
            Vector3 start = interactor.transform.position + Vector3.up * .85f;
            Vector3 ray = Plan.Handle.position - start;
            int count = Physics.RaycastNonAlloc(start, ray.normalized, sightHits, ray.magnitude,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!sightHits[i].transform.IsChildOf(Plan.Lodge) &&
                    !sightHits[i].transform.IsChildOf(interactor.transform)) return false;
            return true;
        }

        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) BeginInteraction(); }

        public bool BeginInteraction()
        {
            if (!CanInteract(village.Player.Interactor)) return false;
            // Prepare every model before taking ownership or committing inventory.
            try
            {
                props = new LodgeStoveFirstPerson(camera);
                if (!modal.TryCaptureAndDisable(village.Player.Interactor, village.CameraFollow, village.IntoxicationHud))
                { props.Dispose(); props = null; return false; }
                if (!controller.BeginPositioned(LodgeStovePlan.CreateDefinition(), Plan.EntryPose,
                        Plan.EntryPose.HipPosition, Plan.ExitPose, .3f))
                { Cleanup(); return false; }
                openedFrame = Time.frameCount;
                exitQueued = false;
                currentCameraBlend = 0f;
                SetPhase(LodgeStovePhase.Approaching);
                return true;
            }
            catch { Cleanup(); throw; }
        }

        public bool TryUseItem(InventoryItemId item)
        {
            if (Phase != LodgeStovePhase.Browsing || exitQueued || GameTimeScaleRuntime.IsPaused ||
                !CanUse(item)) return false;
            InventoryView.SetItemsEnabled(false);
            if (item == InventoryItemId.FirewoodLog)
            {
                placingStart = camera.transform.TransformPoint(new Vector3(.20f, -.35f, .62f));
                LogModel.gameObject.SetActive(true);
                LogModel.SetPositionAndRotation(placingStart, logRestRotation * Quaternion.Euler(0f, -18f, 12f));
                SetPhase(LodgeStovePhase.PlacingLog);
            }
            else
            {
                nextClick = 0;
                Fire.ResetClickCount();
                SetPhase(LodgeStovePhase.Igniting);
            }
            return true;
        }

        private bool CanUse(InventoryItemId item) =>
            GameSessionState.HasInventoryItem(item) &&
            (item == InventoryItemId.FirewoodLog && LodgeStoveSessionState.Stage == LodgeStoveStage.Empty ||
             item == InventoryItemId.Lighter && LodgeStoveSessionState.Stage == LodgeStoveStage.LogPlaced);

        public void RequestExit()
        {
            if (!OwnsInteraction || exitQueued) return;
            exitQueued = true;
            InventoryView.Close();
            if (Phase == LodgeStovePhase.Approaching) { CancelInteraction(); return; }
            // The lighter withdraws on its own short path. A log
            // not yet docked returns to inventory; a committed log stays put.
            returningLog = Phase == LodgeStovePhase.PlacingLog;
            looseLogPosition = LogModel.position;
            looseLogRotation = LogModel.rotation;
            exitCameraBlend = currentCameraBlend;
            closingDoorStart = DoorOpenAmount;
            props?.BeginWithdraw();
            SetPhase(LodgeStovePhase.Withdrawing);
        }

        private void Update()
        {
            if (!OwnsInteraction) return;
            if (village.IsDormant || SceneTransitionService.IsTransitioning || !controller.IsActive &&
                Phase != LodgeStovePhase.Releasing) { CancelInteraction(); return; }
            if (GameTimeScaleRuntime.IsPaused) return;
            if (Time.frameCount > openedFrame && !InventoryView.IsOpen &&
                GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Contextual)) RequestExit();
            AdvanceInteraction(Time.deltaTime);
        }

        public void AdvanceInteraction(float delta)
        {
            if (!OwnsInteraction || GameTimeScaleRuntime.IsPaused) return;
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f) throw new ArgumentOutOfRangeException(nameof(delta));
            if (Phase == LodgeStovePhase.Approaching)
            {
                if (controller.Phase != PlayerAnimatedInteractionPhase.Looping) return;
                cameraStart = camera.transform.position;
                cameraRotation = camera.transform.rotation;
                cameraFov = camera.fieldOfView;
                cameraOwned = true;
                SetPhase(LodgeStovePhase.Opening);
                return;
            }
            elapsed += delta;
            switch (Phase)
            {
                case LodgeStovePhase.Opening:
                    if (elapsed >= LodgeStovePlan.CameraSeconds + LodgeStovePlan.DoorSeconds + .45f)
                    {
                        SetDoor(1f);
                        props.Hide();
                        SetPhase(LodgeStovePhase.Browsing);
                        InventoryView.Open(CanUse, item => TryUseItem(item), RequestExit, Hint(),
                            LocalizationService.Get("stove.action.exit"));
                    }
                    break;
                case LodgeStovePhase.PlacingLog:
                    if (elapsed >= LodgeStovePlan.PlaceSeconds)
                    {
                        LodgeStoveSessionState.TryPlaceLog();
                        RestoreLog();
                        Browse();
                    }
                    break;
                case LodgeStovePhase.Igniting:
                    // One visible strike per frame, including a hitch; never
                    // collapse the dry attempts into a burst of three sounds.
                    if (nextClick < ClickTimes.Length && elapsed >= ClickTimes[nextClick])
                    {
                        elapsed = ClickTimes[nextClick];
                        Fire.PlayClick();
                        nextClick++;
                        if (nextClick == 3) LodgeStoveSessionState.TryIgnite();
                    }
                    if (elapsed >= LodgeStovePlan.IgnitionSeconds) Browse();
                    break;
                case LodgeStovePhase.Withdrawing:
                    if (elapsed >= .35f)
                    {
                        props.Hide();
                        RestoreLog();
                        SetPhase(closingDoorStart > .001f ? LodgeStovePhase.Closing : LodgeStovePhase.Returning);
                    }
                    break;
                case LodgeStovePhase.Closing:
                    if (elapsed >= .3f + LodgeStovePlan.DoorSeconds + .3f)
                    {
                        SetDoor(0f);
                        props.Hide();
                        SetPhase(LodgeStovePhase.Returning);
                    }
                    break;
                case LodgeStovePhase.Returning:
                    if (elapsed >= LodgeStovePlan.CameraSeconds)
                    {
                        controller.RequestExit();
                        releaseFrame = Time.frameCount;
                        SetPhase(LodgeStovePhase.Releasing);
                    }
                    break;
            }
        }

        private void LateUpdate()
        {
            if (!OwnsInteraction || Phase == LodgeStovePhase.Approaching) return;
            if (Phase == LodgeStovePhase.Releasing)
            {
                if (!controller.IsActive && Time.frameCount > releaseFrame) Cleanup();
                return;
            }
            float blend = Phase == LodgeStovePhase.Opening ? Smooth(elapsed / LodgeStovePlan.CameraSeconds) :
                Phase == LodgeStovePhase.Returning ? exitCameraBlend * (1f - Smooth(elapsed / LodgeStovePlan.CameraSeconds)) :
                exitQueued ? exitCameraBlend : 1f;
            currentCameraBlend = blend;
            Pose follow = village.CameraFollow.ResolveFollowPose(Plan.ExitPose.RootPosition);
            bool returning = Phase == LodgeStovePhase.Returning;
            Vector3 start = returning ? follow.position : cameraStart;
            Quaternion rotation = returning ? follow.rotation : cameraRotation;
            Quaternion close = Quaternion.LookRotation(Plan.CameraTarget - Plan.CameraDock.position, Vector3.up);
            village.CameraFollow.SetFixedPose(CameraPath(start, Plan.CameraDock.position, blend),
                Quaternion.Slerp(rotation, close, blend), Mathf.Lerp(returning ? village.CameraFollow.FollowFieldOfView : cameraFov, 58f, blend));
            if (Phase == LodgeStovePhase.Opening && elapsed >= LodgeStovePlan.CameraSeconds)
            {
                float t = elapsed - LodgeStovePlan.CameraSeconds;
                SetDoor(Smooth((t - .2f) / LodgeStovePlan.DoorSeconds));
            }
            else if (Phase == LodgeStovePhase.PlacingLog)
            {
                float t = Smooth(elapsed / LodgeStovePlan.PlaceSeconds);
                LogModel.position = Vector3.Lerp(placingStart, Plan.LogDock.position, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * .06f;
                LogModel.rotation = Quaternion.Slerp(logRestRotation * Quaternion.Euler(0f, -18f, 12f), logRestRotation, t);
            }
            else if (Phase == LodgeStovePhase.Igniting)
            {
                float reach = Mathf.Min(elapsed / .75f, (LodgeStovePlan.IgnitionSeconds - elapsed) / .55f);
                float clickTime = nextClick == 1 ? LodgeStovePlan.FirstClickSeconds : nextClick == 2 ? LodgeStovePlan.SecondClickSeconds : LodgeStovePlan.ThirdClickSeconds;
                float pulse = nextClick == 0 ? 0f : Mathf.Max(0f, 1f - (elapsed - clickTime) / .18f);
                props.ApplyLighter(Plan.LighterDock.position, Plan.Lodge.rotation * Quaternion.Euler(0f, 0f, -12f),
                    reach, pulse, nextClick == 3 && LodgeStoveSessionState.IsBurning && elapsed < 3.2f, elapsed);
            }
            else if (Phase == LodgeStovePhase.Closing)
            {
                SetDoor(closingDoorStart * (1f - Smooth((elapsed - .3f) / LodgeStovePlan.DoorSeconds)));
            }
            else if (Phase == LodgeStovePhase.Withdrawing)
            {
                props.Withdraw(elapsed / .35f);
                if (returningLog)
                {
                    LogModel.position = Vector3.Lerp(looseLogPosition, placingStart, Smooth(elapsed / .35f));
                    LogModel.rotation = Quaternion.Slerp(looseLogRotation, logRestRotation * Quaternion.Euler(0f, -18f, 12f), Smooth(elapsed / .35f));
                }
            }
        }

        private Vector3 CameraPath(Vector3 start, Vector3 target, float amount)
        {
            // Pass beside the visible world hero instead of through his torso.
            // The final first-person camera sits in front of him; no hand subset
            // or world visibility trick is needed for this prop-only action.
            Vector3 a = Vector3.Lerp(start, target, .35f) + Plan.Lodge.right * .8f + Vector3.up * .7f;
            Vector3 b = target + Plan.Lodge.right * .85f + Vector3.up * .12f - Plan.Lodge.forward * .2f;
            float inverse = 1f - amount;
            return inverse * inverse * inverse * start + 3f * inverse * inverse * amount * a +
                   3f * inverse * amount * amount * b + amount * amount * amount * target;
        }

        public bool ProvidesWarmth(Vector3 worldPosition)
        {
            if (!isActiveAndEnabled || village == null || village.IsDormant || !LodgeStoveSessionState.IsBurning) return false;
            // A sealed, lit lodge warms the whole room. The existing local
            // stove radius remains available when either entrance leaf is open.
            LodgeShelterController shelter = village.LodgeShelter;
            if (shelter != null && shelter.isActiveAndEnabled && shelter.OpenDoorCount == 0 &&
                shelter.ContainsInterior(worldPosition)) return true;
            Vector3 local = Plan.Lodge.InverseTransformPoint(worldPosition);
            return local.y >= -.2f && local.y <= 2.5f &&
                Mathf.Abs(local.x) < 8.5f && Mathf.Abs(local.z) < 5.7f &&
                new Vector2(local.x, local.z).sqrMagnitude <= LodgeStovePlan.WarmRadius * LodgeStovePlan.WarmRadius;
        }

        private string Hint() => LocalizationService.Get(LodgeStoveSessionState.IsBurning ? "stove.hint.burning" :
            LodgeStoveSessionState.HasLog ? "stove.hint.ignite" : "stove.hint.log");
        private void Browse() { props.Hide(); SetPhase(LodgeStovePhase.Browsing); InventoryView.SetHint(Hint()); InventoryView.SetItemsEnabled(true); }
        private void SetPhase(LodgeStovePhase phase) { Phase = phase; elapsed = 0f; }
        private void SetDoor(float amount) { DoorOpenAmount = Mathf.Clamp01(amount); if (Plan.Door != null) Plan.Door.localRotation = Quaternion.Euler(0f, LodgeStovePlan.DoorAngle * DoorOpenAmount, 0f); }
        private void RestoreLog()
        {
            if (LogModel == null || Plan.LogDock == null) return;
            LogModel.SetPositionAndRotation(Plan.LogDock.position, logRestRotation);
            LogModel.gameObject.SetActive(LodgeStoveSessionState.HasLog);
        }
        private static float Smooth(float amount) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount));
        public void CancelInteraction() { if (OwnsInteraction || modal.IsLocked) Cleanup(); }

        private void Cleanup()
        {
            if (restoring) return;
            restoring = true;
            Phase = LodgeStovePhase.Closed;
            InventoryView?.Close();
            props?.Dispose(); props = null;
            if (controller != null && modal.IsLocked) controller.CancelActiveInteraction();
            if (Plan != null) { SetDoor(0f); RestoreLog(); }
            if (cameraOwned && village != null && village.CameraFollow != null) village.CameraFollow.ClearFixedPose();
            cameraOwned = false;
            modal.Restore();
            restoring = false;
        }
        private void OnDisable() => CancelInteraction();
        private void OnDestroy() => CancelInteraction();
    }
}
