using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Three reward performances share the positioned hero, item transaction and camera lifecycles.</summary>
    [DefaultExecutionOrder(470)]
    [DisallowMultipleComponent]
    public sealed class StairwellPickupCinematicController : MonoBehaviour
    {
        private readonly ContextualCameraDirector cameraDirector = new ContextualCameraDirector();
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController animation;
        private Player3DAssetRegistry registry;
        private VillageWorkroomHandContacts hands;
        private StairwellInteriorRoot host;
        private PlayerChestFlashlightController flashlight;
        private Action completed;
        private Transform heldModel, originalParent;
        private Vector3 gripPosition, originalScale;
        private Quaternion gripRotation;
        private bool attached, released, bodyCompleted, cameraStarted, restoring, contactCue, weightCue;
        private int shotSide = 1;
        private float lastProgress;
        private CharacterController body;
        private Collider[] clearanceHits = new Collider[32];
        public string LastFailureReason { get; private set; } = string.Empty;
        public bool IsActive { get; private set; }
        public InventoryItemId ItemId => IsActive || Plan != null ? Plan.ItemId : InventoryItemId.None;
        public StairwellPickupCinematicPlan Plan { get; private set; }
        public float Progress => IsActive ? lastProgress : 0f;
        public PlayerAnimatedInteractionPhase Phase => IsActive && animation != null ? animation.Phase : PlayerAnimatedInteractionPhase.Idle;
        public Transform HeldModel => heldModel;
        public Transform RightGrip => hands?.RightGrip;
        public float ContactWeight { get; private set; }
        public bool IsCameraReturning => IsActive && cameraDirector.IsReturning;

        internal void Initialize(PlayerRuntime runtime, PlayerAnimatedInteractionController shared)
        {
            player = runtime; animation = shared;
            registry = runtime.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            host = runtime.GameObject.GetComponentInParent<StairwellInteriorRoot>();
            if (registry == null || host == null || host.CameraFollow == null || shared == null)
                throw new InvalidOperationException("Reward performances require the production hero and Stairwell camera.");
            hands = new VillageWorkroomHandContacts(runtime.GameObject.transform, registry);
            body = runtime.GameObject.GetComponent<CharacterController>();
            flashlight = runtime.GameObject.GetComponent<PlayerChestFlashlightController>();
            animation.InteractionCompleted += OnBodyCompleted;
            animation.PhaseChanged += OnPhase;
        }

        internal void Bind(WorldItemPickup pickup, Vector3 contact, float floorHeight, Vector3 facing)
        {
            if (pickup == null) return;
            var plan = new StairwellPickupCinematicPlan(pickup.Plan.ItemId, contact, floorHeight, facing);
            pickup.ConfigurePresentation(interactor => Prepare(interactor, pickup, plan),
                (interactor, model, done) => Begin(interactor, model, plan, done), Cancel);
        }

        private bool Prepare(PlayerInteractor interactor, WorldItemPickup pickup, StairwellPickupCinematicPlan plan)
        {
            LastFailureReason = string.Empty;
            if (!isActiveAndEnabled || IsActive || host == null || !host.isActiveAndEnabled ||
                interactor != player.Interactor || player.GameObject == null || animation == null ||
                !animation.isActiveAndEnabled || SceneTransitionService.IsTransitioning ||
                Mathf.Abs(player.GameObject.transform.position.y - plan.EntryPose.RootPosition.y) > .35f ||
                Vector3.Distance(player.GameObject.transform.position, plan.EntryPose.RootPosition) > 2.6f ||
                !PlayerStairwellPickupActions.TryAttach(registry)) return false;
            var definition = PlayerStairwellPickupActions.CreateDefinition(plan.ItemId);
            if (!animation.TryPrepare(definition, plan.EntryPose, plan.ActionHipPosition, plan.ExitPose)) return false;
            if (!player.Motor.IsInteractionApproachSupported(plan.EntryPose.RootPosition))
                return Reject("unsupported_dock", plan);
            if (!ApproachClear(plan, out string obstacle)) return Reject("capsule_blocked:" + obstacle, plan);
            // Both sides are finite authored alternatives; every point along the chosen arc is checked before credit.
            bool clear = false;
            for (int side = 1; side >= -1 && !clear; side -= 2)
            {
                clear = true;
                for (int sample = 0; sample <= 8 && clear; sample++)
                {
                    plan.Shot(sample / 8f, out Pose pose, out Vector3 focus, out _, side);
                    clear = cameraDirector.CanPrepareAuthored(host.CameraFollow, pickup.transform,
                        player.GameObject.transform, pose, focus);
                }
                if (clear) shotSide = side;
            }
            if (!clear) return Reject("camera_preflight", plan);
            if (plan.ItemId == InventoryItemId.ChestFlashlight && (flashlight == null || flashlight.EnsureInitialized() == null)) return false;
            Plan = plan;
            return true;
        }

        private bool ApproachClear(StairwellPickupCinematicPlan plan, out string obstacle)
        {
            obstacle = string.Empty;
            if (body == null) { obstacle = "missing_controller"; return false; }
            Vector3 start = player.GameObject.transform.position, end = plan.EntryPose.RootPosition;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(start, end) / .10f), 1, 32);
            float radius = body.radius + .02f;
            Vector3 half = Vector3.up * (body.height * .5f - body.radius);
            for (int sample = 1; sample <= steps; sample++)
            {
                Vector3 center = Vector3.Lerp(start, end, sample / (float)steps) + body.center;
                int count;
                while (true)
                {
                    count = Physics.OverlapCapsuleNonAlloc(center - half, center + half, radius, clearanceHits,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (count < clearanceHits.Length) break;
                    Array.Resize(ref clearanceHits, clearanceHits.Length * 2);
                }
                for (int index = 0; index < count; index++)
                {
                    Collider hit = clearanceHits[index];
                    if (hit == null || hit == body || hit.transform.IsChildOf(player.GameObject.transform) ||
                        Physics.GetIgnoreLayerCollision(body.gameObject.layer, hit.gameObject.layer)) continue;
                    obstacle = hit.name + " at " + center;
                    return false;
                }
            }
            return true;
        }

        private bool Reject(string reason, StairwellPickupCinematicPlan plan)
        {
            LastFailureReason = reason;
            Debug.Log($"Stairwell pickup {plan.ItemId} refused: {reason}; root={player.GameObject.transform.position}, dock={plan.EntryPose.RootPosition}.", this);
            return false;
        }

        private void Abort(string reason)
        {
            if (!IsActive) return;
            LastFailureReason = reason;
            Debug.Log($"Stairwell pickup {ItemId} interrupted: {reason}; phase={Phase}, progress={lastProgress:F3}, root={player.GameObject.transform.position}, dock={Plan.EntryPose.RootPosition}, camera={host.CameraFollow.Camera.transform.position}.", this);
            Restore();
        }

        private bool Begin(PlayerInteractor interactor, Transform model, StairwellPickupCinematicPlan plan, Action done)
        {
            if (!isActiveAndEnabled || IsActive || Plan != plan || interactor != player.Interactor || model == null ||
                player.GameObject == null || !player.GameObject.activeInHierarchy || SceneTransitionService.IsTransitioning ||
                !host.FixedCamera.TryAcquirePuzzleView(this)) return false;
            if (!modal.TryCaptureAndDisable(interactor, host.CameraFollow, host.IntoxicationHud,
                BarMinigameModalLockOptions.FullscreenWithPause))
            { host.FixedCamera.ReleasePuzzleView(this); return false; }
            completed = done;
            heldModel = model; originalParent = model.parent; originalScale = model.localScale;
            attached = released = bodyCompleted = cameraStarted = contactCue = weightCue = false;
            ContactWeight = lastProgress = 0f;
            IsActive = true;
            try
            {
                model.gameObject.SetActive(true);
                if (plan.ItemId == InventoryItemId.ChestFlashlight) flashlight.SetPresentationSuppressed(true);
                if (!animation.BeginPositioned(PlayerStairwellPickupActions.CreateDefinition(plan.ItemId),
                    plan.EntryPose, plan.ActionHipPosition, plan.ExitPose, .35f))
                { Restore(); return true; }
                return true;
            }
            catch { Restore(); throw; }
        }

        private void Update()
        {
            if (!IsActive) return;
            if (player.GameObject == null || animation == null || !animation.isActiveAndEnabled ||
                heldModel == null || host == null || !host.isActiveAndEnabled || SceneTransitionService.IsTransitioning)
            { Restore(); return; }
            bool paused = GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused;
            host.Audio?.SetPresentationPaused(this, paused);
            if (paused) return;
            if (bodyCompleted && cameraDirector.IsFinished) Restore();
        }

        private void LateUpdate() => RefreshPresentation(Time.deltaTime);

        /// <summary>The capture path uses the same contact and camera pass as ordinary rendering.</summary>
        public void RefreshPresentation(float deltaTime)
        {
            if (!IsActive || animation == null || heldModel == null || registry == null) return;
            if (GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused) deltaTime = 0f;
            if (!bodyCompleted && animation.Phase != PlayerAnimatedInteractionPhase.Positioning)
            {
                VillageWorkroomHandContacts.RefreshPresentation(player, animation);
                lastProgress = ResolveProgress();
                float phase = animation.PhaseProgress;
                ContactWeight = 0f;
                if (animation.Phase == PlayerAnimatedInteractionPhase.Entering && !attached)
                {
                    ContactWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.20f, .65f, phase));
                    hands.Apply(Plan.SourceContact, null, ContactWeight);
                    if (phase >= .65f) Attach();
                }
                if (attached && !released)
                {
                    // Bone-only authored clips move this exact model. Its socket offset is captured at real contact.
                    heldModel.SetPositionAndRotation(hands.RightGrip.TransformPoint(gripPosition), hands.RightGrip.rotation * gripRotation);
                    ContactWeight = 1f;
                    if (Plan.ItemId != InventoryItemId.ChestFlashlight && animation.Phase == PlayerAnimatedInteractionPhase.Looping)
                        hands.Apply(null, hands.RightGrip.position - player.GameObject.transform.right * .075f,
                            Mathf.Sin(phase * Mathf.PI) * .7f);
                    if (animation.Phase == PlayerAnimatedInteractionPhase.Exiting)
                    {
                        if (Plan.ItemId == InventoryItemId.ChestFlashlight)
                        {
                            Transform chest = flashlight.Flashlight.ModelRoot.transform;
                            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.05f, .55f, phase));
                            heldModel.SetPositionAndRotation(Vector3.Lerp(heldModel.position, chest.position, t),
                                Quaternion.Slerp(heldModel.rotation, chest.rotation, t));
                            hands.Apply(heldModel.position + Vector3.up * .0275f, null, 1f - Mathf.SmoothStep(.55f, .95f, phase));
                        }
                        if (phase >= .55f) ReleaseToEquipment();
                    }
                }
                if (!weightCue && animation.Phase == PlayerAnimatedInteractionPhase.Looping && phase >= .25f)
                {
                    weightCue = true;
                    if (Plan.ItemId == InventoryItemId.CombatCrowbar)
                        PlayFoley(RetroSfxId.HomeBedCloth, hands.RightGrip.position, .35f);
                }
                if (!cameraStarted)
                {
                    Plan.Shot(lastProgress, out Pose pose, out Vector3 focus, out float fov, shotSide);
                    cameraStarted = cameraDirector.BeginAuthored(host.CameraFollow, originalParent, player.GameObject.transform,
                        pose, fov, focus, .30f, StairwellPickupCinematicPlan.CameraReturnSeconds, true, .05f);
                    if (!cameraStarted) { Abort("camera_acquire"); return; }
                }
            }
            if (cameraStarted && !cameraDirector.IsReturning && !cameraDirector.IsFinished)
            {
                Plan.Shot(lastProgress, out Pose pose, out Vector3 focus, out float fov, shotSide);
                // The full arc was checked before credit; revalidate its live lens against moving obstructions.
                if (!cameraDirector.TrySetAuthoredShot(pose, fov, focus, nearClipPlane: .05f))
                { Abort("camera_obstructed"); return; }
            }
            cameraDirector.Tick(Mathf.Max(0f, deltaTime));
        }

        private void Attach()
        {
            if (attached || heldModel == null) return;
            attached = true;
            gripPosition = hands.RightGrip.InverseTransformPoint(heldModel.position);
            gripRotation = Quaternion.Inverse(hands.RightGrip.rotation) * heldModel.rotation;
            if (!contactCue)
            {
                contactCue = true;
                PlayFoley(Plan.ItemId == InventoryItemId.CombatCrowbar ? RetroSfxId.SpadeGlance : RetroSfxId.HomeBedCloth,
                    Plan.SourceContact, Plan.ItemId == InventoryItemId.CombatCrowbar ? .22f : .40f);
            }
        }

        private void ReleaseToEquipment()
        {
            if (released) return;
            released = true; ContactWeight = 0f;
            if (heldModel != null) heldModel.gameObject.SetActive(false);
            GameSessionState.TrySetInventoryItemEquipped(Plan.ItemId, true);
            if (Plan.ItemId == InventoryItemId.ChestFlashlight)
            {
                flashlight?.SetPresentationSuppressed(false);
                PlayFoley(RetroSfxId.PistolMagazineLatch, registry.Anchors.Chest.position, .25f);
            }
        }

        private void PlayFoley(RetroSfxId cue, Vector3 position, float gain)
        {
            if (host == null || host.Audio == null) return;
            object previous = RetroAudio.SetPresentationCueOwner(this);
            try { host.Audio.TryPlay(cue, position, gain); }
            finally { RetroAudio.SetPresentationCueOwner(previous); }
        }

        private float ResolveProgress() => animation.Phase == PlayerAnimatedInteractionPhase.Entering ? animation.PhaseProgress * 1.25f / 4f :
            animation.Phase == PlayerAnimatedInteractionPhase.Looping ? (1.25f + animation.PhaseProgress * 2f) / 4f :
            animation.Phase == PlayerAnimatedInteractionPhase.Exiting ? (3.25f + animation.PhaseProgress * .75f) / 4f : lastProgress;

        private void OnPhase(PlayerAnimatedInteractionPhase phase)
        {
            if (!IsActive) return;
            if (phase == PlayerAnimatedInteractionPhase.Looping)
                animation.RequestExitAtLoopBoundaryWithClip(PlayerStairwellPickupActions.CreateDefinition(Plan.ItemId).ExitClipName);
            else if (phase == PlayerAnimatedInteractionPhase.Idle && !bodyCompleted) Abort("positioning_or_action_cancelled");
        }

        private void OnBodyCompleted()
        {
            if (!IsActive) return;
            bodyCompleted = true; lastProgress = 1f;
            ReleaseToEquipment();
            if (cameraStarted) cameraDirector.BeginExit();
        }

        public void Cancel() { if (IsActive) Restore(); }
        private void Restore()
        {
            if (restoring || !IsActive) return;
            restoring = true;
            try
            {
                IsActive = false;
                animation?.CancelActiveInteraction();
                cameraDirector.RestoreImmediate();
                // Credit predates the performance: interrupted receipts still leave their unique item equipped and owned.
                ReleaseToEquipment();
                host?.Audio?.ReleasePresentation(this);
                if (heldModel != null)
                {
                    heldModel.SetParent(originalParent, true);
                    heldModel.localScale = originalScale;
                    heldModel.gameObject.SetActive(false);
                }
                heldModel = null; originalParent = null;
                ContactWeight = 0f;
                host?.FixedCamera?.ReleasePuzzleView(this);
                modal.Restore();
                Action finish = completed; completed = null;
                finish?.Invoke();
            }
            finally { restoring = false; }
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            Cancel();
            if (animation != null)
            { animation.InteractionCompleted -= OnBodyCompleted; animation.PhaseChanged -= OnPhase; }
        }
    }
}
