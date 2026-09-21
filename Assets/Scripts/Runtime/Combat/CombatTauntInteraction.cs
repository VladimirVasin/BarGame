using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The finished round's one contextual action. Over the settled, terminally
    /// defeated opponent `E` walks the standing winner to a dock beside the
    /// body with the ordinary constrained motor, then hosts the Home toilet's
    /// eye-level urination there, solved onto the body. The crowbar waits in
    /// the closed left hand. Nothing is said and nothing is rewarded: the wet
    /// marks stay on the ragdoll's bones and the floor until R clears the round.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class CombatTauntInteraction : MonoBehaviour, IInteractable, IHomeToiletViewHost, IHomeToiletGaugeSource
    {
        public const string PromptKeyName = "interaction.desecrate_body";
        public const string StopPromptKeyName = HomeToiletInteraction.StopPromptKeyName;
        public const string ResidueScope = "combat-test";
        /// <summary>
        /// Horizontal metres from the aim point to the dock: the bathroom's own
        /// bowl distance. For a body lying 0.1–0.3 m high the solved pitch lands
        /// around 30–40 degrees, under the angle at which the scrotum would stand
        /// in front of the shaft (<see cref="HomeToiletFirstPersonView.RestAimPitchDegrees"/>).
        /// </summary>
        public const float DockDistanceMetres = 0.9f;
        public const float DockClearanceRadius = 0.4f;
        public const float ExitInputDebounceSeconds = 0.12f;
        private static readonly float[] DockCandidateDegrees =
            { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f, 125f, -125f, 150f, -150f, 180f };

        public enum Phase { Idle, Approaching, Using }

        private readonly HomeToiletSceneTimeline timeline = new HomeToiletSceneTimeline();
        private readonly Collider[] clearanceBuffer = new Collider[24];
        private readonly RaycastHit[] rayBuffer = new RaycastHit[24];
        private CombatTestRoot root;
        private IWalkableArea arena;
        private HomeToiletFirstPersonView firstPerson;
        private HomeUrineEffect urine;
        private BarMinigameModalLock modalLock;
        private Func<bool> stopAction;
        private Vector3 dock, aim, resolvedBody, resolvedHero;
        private Quaternion dockRotation = Quaternion.identity;
        private bool dockResolved;
        private Vector3 cameraStartPosition, cameraControlPosition;
        private Quaternion cameraStartRotation = Quaternion.identity;
        private float cameraStartFieldOfView = 60f;
        private bool cameraPathCaptured, settleFrameHeld, terminalPresented;
        private float pendingUrineSeconds, pendingShakeSeconds;
        private bool previousHandoff, ownsHandoff;
        private bool usedThisRound, stopQueued, exitInputArmed, restoring;
        private float exitInputArmTime;

        public Phase CurrentPhase { get; private set; }
        public bool IsActive => CurrentPhase != Phase.Idle;
        public bool UsedThisRound => usedThisRound;
        public HomeToiletSceneTimeline Timeline => timeline;
        public HomeToiletFirstPersonView FirstPerson => firstPerson;
        public HomeUrineEffect Urine => urine;
        public Vector3 DockPosition => dock;
        public Quaternion DockRotation => dockRotation;
        public Vector3 AimPoint => aim;
        public bool GaugeVisible => CurrentPhase == Phase.Using && timeline.GaugeVisible;
        public string PromptKey => IsActive ? string.Empty : PromptKeyName;
        public Vector3 InteractionPosition
        {
            get
            {
                Rigidbody pelvis = root != null && root.Opponent != null && root.Opponent.Ragdoll != null
                    ? root.Opponent.Ragdoll.PelvisBody : null;
                return pelvis != null ? pelvis.position : transform.position;
            }
        }

        PlayerRuntime IHomeToiletViewHost.Player => root.Player;
        // Under the effect object: the surface map skips its own effect's
        // children, so the hero's kit can never receive its own stream.
        Transform IHomeToiletViewHost.AnatomyParent => urine.transform;
        HomePlayerOcclusionController IHomeToiletViewHost.PlayerOcclusion => null;
        bool IHomeToiletViewHost.TryGetAimTarget(Vector3 facing, out Vector3 worldTarget)
        {
            worldTarget = aim;
            return dockResolved;
        }

        public void Initialize(CombatTestRoot owner, IWalkableArea walkable)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (walkable == null) throw new ArgumentNullException(nameof(walkable));
            root = owner;
            arena = walkable;
            var effectObject = new GameObject("Combat Urine");
            effectObject.transform.SetParent(owner.transform, false);
            urine = effectObject.AddComponent<HomeUrineEffect>();
            urine.Initialize(owner.transform, ResidueScope, IsForeignReceiver);
            firstPerson = gameObject.AddComponent<HomeToiletFirstPersonView>();
            firstPerson.Initialize(this);
            gameObject.AddComponent<HomeToiletGaugeView>().Bind(this);
            stopAction = RequestStop;
        }

        /// <summary>The hero (his bar, his own ragdoll proxies) and the blood pools stay dry; the body and the floor receive.</summary>
        private bool IsForeignReceiver(Transform candidate)
        {
            Transform hero = root != null && root.Player.GameObject != null ? root.Player.GameObject.transform : null;
            for (Transform cursor = candidate; cursor != null && cursor != root.transform; cursor = cursor.parent)
                if (cursor == hero || cursor.name == "Combat Blood") return true;
            return false;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (root == null || !root.IsInitialized || IsActive || usedThisRound) return false;
            if (interactor == null || !interactor.InputEnabled) return false;
            CombatActor opponent = root.Opponent;
            if (opponent == null || !opponent.State.IsDefeated || root.Hero == null || root.Hero.State.IsDefeated) return false;
            if (opponent.Ragdoll == null || !opponent.Ragdoll.IsActive || !opponent.Ragdoll.IsSettled || !root.RoundCameraReleased) return false;
            if (BarMinigameModalLock.IsAnyLocked || SceneTransitionService.IsTransitioning || PauseMenuController.IsAnyPaused) return false;
            return TryResolveDock();
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor) || !firstPerson.Prepare()) return;
            modalLock ??= new BarMinigameModalLock();
            if (!modalLock.TryCaptureAndDisable(interactor, root.CameraFollow, null, BarMinigameModalLockOptions.Fullscreen)) return;
            if (!root.Hero.TryHoldWeaponInLeftHand())
            {
                modalLock.Restore();
                return;
            }
            timeline.Reset();
            pendingUrineSeconds = pendingShakeSeconds = 0f;
            stopQueued = exitInputArmed = settleFrameHeld = terminalPresented = cameraPathCaptured = false;
            exitInputArmTime = Time.unscaledTime + ExitInputDebounceSeconds;
            CurrentPhase = Phase.Approaching;
            root.Prompt?.SetPrompt(string.Empty, null);
        }

        /// <summary>
        /// The aim point is the lying body's upper surface over its lower torso; the dock
        /// stands the bowl distance away on the hero's side of it, on the arena floor,
        /// clear of the body and the arena's own masses. Re-solved when either moves.
        /// </summary>
        private bool TryResolveDock()
        {
            CombatRagdoll ragdoll = root.Opponent.Ragdoll;
            Rigidbody pelvis = ragdoll.PelvisBody;
            Rigidbody chest = ragdoll.PhysicsController != null ? ragdoll.PhysicsController.ChestBody : null;
            Transform hero = root.Player.GameObject != null ? root.Player.GameObject.transform : null;
            if (pelvis == null || chest == null || hero == null) return false;
            Vector3 body = Vector3.Lerp(pelvis.worldCenterOfMass, chest.worldCenterOfMass, 0.5f);
            if (dockResolved && (body - resolvedBody).sqrMagnitude < 0.0001f &&
                (hero.position - resolvedHero).sqrMagnitude < 0.0625f) return true;
            Vector3 top = body;
            float nearest = float.PositiveInfinity;
            int hits = Physics.RaycastNonAlloc(body + Vector3.up * 1.5f, Vector3.down, rayBuffer, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = rayBuffer[i];
                if (hit.collider == null || !hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                top = hit.point;
            }
            if (float.IsPositiveInfinity(nearest))
                top.y = ragdoll.HasGroundContact ? ragdoll.GroundContactPoint.y + 0.15f : body.y;
            float ground = hero.position.y;
            Vector3 toHero = hero.position - top;
            toHero.y = 0f;
            Vector3 baseDirection;
            if (toHero.sqrMagnitude > 0.0001f) baseDirection = toHero.normalized;
            else
            {
                baseDirection = Vector3.ProjectOnPlane(-root.Opponent.transform.forward, Vector3.up);
                baseDirection = baseDirection.sqrMagnitude > 0.0001f ? baseDirection.normalized : Vector3.back;
            }
            foreach (float degrees in DockCandidateDegrees)
            {
                Vector3 direction = Quaternion.AngleAxis(degrees, Vector3.up) * baseDirection;
                Vector3 candidate = top + direction * DockDistanceMetres;
                candidate.y = ground;
                if (!arena.Contains(candidate, 0.32f) || DockBlocked(candidate, ground, hero)) continue;
                dock = candidate;
                aim = top;
                dockRotation = Quaternion.LookRotation(-direction, Vector3.up);
                resolvedBody = body;
                resolvedHero = hero.position;
                dockResolved = true;
                return true;
            }
            dockResolved = false;
            return false;
        }

        private bool DockBlocked(Vector3 candidate, float ground, Transform hero)
        {
            int count = Physics.OverlapCapsuleNonAlloc(candidate + Vector3.up * 0.3f, candidate + Vector3.up * 1.3f,
                DockClearanceRadius, clearanceBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = clearanceBuffer[i];
                if (collider == null || collider.transform.IsChildOf(hero)) continue;
                // The floor itself: whatever does not rise above the hero's own feet.
                if (collider.bounds.max.y <= ground + 0.03f) continue;
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (!IsActive) return;
            if (SceneTransitionService.IsTransitioning)
            {
                Cancel();
                return;
            }
            float deltaTime = Time.deltaTime;
            if (CurrentPhase == Phase.Approaching)
            {
                PlayerMotor motor = root.Player.Motor;
                if (motor == null)
                {
                    Cancel();
                    return;
                }
                bool arrived = motor.MoveTowardsInteractionPose(dock, dockRotation, Mathf.Max(0f, deltaTime));
                if (motor.InteractionPoseMoveStalled)
                {
                    Cancel();
                    return;
                }
                if (!arrived) return;
                // The neutral endpoint is rendered once before the view takes the rig.
                if (!settleFrameHeld)
                {
                    settleFrameHeld = true;
                    return;
                }
                BeginUsing();
                return;
            }
            if (deltaTime > 0f)
            {
                float previousUrine = timeline.TotalUrinatingSeconds;
                float previousShake = timeline.TotalShakingSeconds;
                timeline.Advance(deltaTime);
                pendingUrineSeconds += timeline.TotalUrinatingSeconds - previousUrine;
                pendingShakeSeconds += timeline.TotalShakingSeconds - previousShake;
            }
            UpdateStopInput();
        }

        private void BeginUsing()
        {
            CurrentPhase = Phase.Using;
            IPlayerPresentation visual = root.Player.Visual;
            previousHandoff = visual.InteractionHandoffLocked;
            visual.SetInteractionHandoffLocked(true);
            ownsHandoff = true;
            timeline.Begin();
            pendingUrineSeconds = pendingShakeSeconds = 0f;
            firstPerson.Begin();
            urine.BeginEmission();
            CaptureCameraPath();
        }

        private void CaptureCameraPath()
        {
            Camera camera = root.CameraFollow != null ? root.CameraFollow.Camera : null;
            if (camera == null)
            {
                cameraPathCaptured = false;
                return;
            }
            cameraStartPosition = camera.transform.position;
            cameraStartRotation = camera.transform.rotation;
            cameraStartFieldOfView = Mathf.Clamp(camera.fieldOfView, 20f, 100f);
            firstPerson.EvaluateCamera(out Vector3 target, out _);
            // The bathroom's own lift over the straight line, so the lens
            // rises into the eyes instead of cutting through the shoulder.
            cameraControlPosition = Vector3.Lerp(cameraStartPosition, target, 0.52f) +
                Vector3.up * 0.22f + cameraStartRotation * Vector3.right * 0.06f;
            cameraPathCaptured = true;
        }

        private void LateUpdate()
        {
            if (CurrentPhase != Phase.Using) return;
            root.Hero.ReassertLeftHandHold();
            float deltaTime = Time.deltaTime;
            firstPerson.Tick(deltaTime, timeline.CameraBlend,
                timeline.Phase == HomeToiletScenePhase.Shaking ? timeline.PhaseElapsed : -1f,
                timeline.Phase == HomeToiletScenePhase.Urinating);
            if (pendingUrineSeconds > 0f)
            {
                float flow = HomeToiletSceneTimeline.AverageUrineFlow(
                    timeline.TotalUrinatingSeconds - pendingUrineSeconds, timeline.TotalUrinatingSeconds);
                urine.EmitStep(firstPerson.OutletPosition, firstPerson.OutletDirection, pendingUrineSeconds, flow, false);
            }
            if (pendingShakeSeconds > 0f)
                urine.EmitStep(firstPerson.OutletPosition, firstPerson.OutletDirection, pendingShakeSeconds, 0.7f, true);
            pendingUrineSeconds = pendingShakeSeconds = 0f;
            if (timeline.Phase >= HomeToiletScenePhase.Exiting) urine.StopEmission();
            ApplyStopPrompt();
            if (timeline.IsCompleted)
            {
                // The zero-blend exit endpoint is shown for one frame before the
                // camera and the rig are handed back.
                if (!terminalPresented)
                {
                    terminalPresented = true;
                    ApplyCamera(0f);
                    return;
                }
                Restore();
                return;
            }
            ApplyCamera(timeline.CameraBlend);
        }

        private void ApplyCamera(float blend)
        {
            if (!cameraPathCaptured || root.CameraFollow == null) return;
            firstPerson.EvaluateCamera(out Vector3 target, out Quaternion targetRotation);
            float amount = Mathf.Clamp01(blend);
            float remaining = 1f - amount;
            Vector3 position = remaining * remaining * cameraStartPosition +
                2f * remaining * amount * cameraControlPosition + amount * amount * target;
            Quaternion rotation = Quaternion.Slerp(cameraStartRotation, targetRotation, amount);
            root.CameraFollow.SetFixedPose(position, rotation,
                Mathf.Lerp(cameraStartFieldOfView, HomeToiletFirstPersonView.FieldOfView, amount));
        }

        private void UpdateStopInput()
        {
            if (!exitInputArmed)
            {
                // The press that started the action must be let go before it can end it.
                if (Time.unscaledTime < exitInputArmTime || IsStopHeld()) return;
                exitInputArmed = true;
                return;
            }
            if (!stopQueued && timeline.GaugeVisible && IsStopHeld()) RequestStop();
        }

        private bool RequestStop()
        {
            if (CurrentPhase != Phase.Using || stopQueued || !timeline.RequestFinish()) return false;
            stopQueued = true;
            return true;
        }

        private static bool IsStopHeld() => GameInput.IsHeld(GameInputAction.Interact, GameInputContext.Contextual);

        private void ApplyStopPrompt()
        {
            // The disabled interactor clears the panel every Update; the owner restates its silent line after it.
            bool show = CurrentPhase == Phase.Using && exitInputArmed && !stopQueued && timeline.GaugeVisible;
            root.Prompt?.SetPrompt(show ? StopPromptKeyName : string.Empty, show ? stopAction : null);
        }

        public void Cancel()
        {
            if (IsActive) Restore();
        }

        /// <summary>Idempotent: completion, a stop, a transition, disable and destroy all end here.</summary>
        private void Restore()
        {
            if (restoring) return;
            restoring = true;
            try
            {
                urine?.StopEmission();
                firstPerson?.End();
                if (ownsHandoff)
                {
                    root.Player.Visual?.SetInteractionHandoffLocked(previousHandoff);
                    ownsHandoff = false;
                }
                if (root != null && root.Hero != null) root.Hero.ReturnWeaponToRightHand();
                root?.Prompt?.SetPrompt(string.Empty, null);
                modalLock?.Restore();
                if (root != null && root.CameraFollow != null) root.CameraFollow.ClearFixedPose();
                timeline.Reset();
            }
            finally
            {
                CurrentPhase = Phase.Idle;
                usedThisRound = true;
                cameraPathCaptured = settleFrameHeld = terminalPresented = false;
                stopQueued = exitInputArmed = false;
                pendingUrineSeconds = pendingShakeSeconds = 0f;
                restoring = false;
            }
        }

        /// <summary>R: the action is offered again, and the round's marks go with the blood.</summary>
        public void ResetRound()
        {
            Cancel();
            usedThisRound = false;
            dockResolved = false;
            if (urine != null) urine.ClearResidue();
        }

        private void OnDisable() => Cancel();

        private void OnDestroy()
        {
            Cancel();
            HomeUrineResidue.RemoveScope(ResidueScope);
        }
    }
}
