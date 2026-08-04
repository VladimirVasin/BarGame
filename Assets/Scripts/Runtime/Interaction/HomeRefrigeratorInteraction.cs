using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// Owns the modal, first-person refrigerator inspection. The pure timeline
    /// supplies animation channels; this component binds them to the Home
    /// camera, generated refrigerator, player presentation, prompts and audio.
    /// </summary>
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class HomeRefrigeratorInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string OpenPromptKey =
            "interaction.open_refrigerator";
        public const string ClosePromptKey =
            "interaction.close_refrigerator";

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private HomeInteriorRoot home;
        private HomeRefrigeratorPlan plan;
        private HomeRefrigeratorView view;
        private HomeRefrigeratorInteractionTimeline timeline;
        private HomeRefrigeratorFirstPersonHand firstPersonHand;
        private HomeRefrigeratorItemInspectionController itemInspection;
        private Camera targetCamera;
        private Vector3 cameraStartPosition;
        private Quaternion cameraStartRotation;
        private float cameraStartFieldOfView;
        private Vector3 cameraControlPosition;
        private Vector3 cameraTargetPosition;
        private Quaternion cameraTargetRotation;
        private float cameraTargetFieldOfView;
        private bool ownsInteraction;
        private bool playerVisualStateCaptured;
        private bool playerVisualHidden;
        private IDisposable playerVisualHideLease;
        private bool openingSealPlayed;
        private bool openingHingePlayed;
        private bool closingRequested;
        private bool closingHingePlayed;
        private bool closingThunkPlayed;
        private Func<bool> closePromptAction;

        public event Action<HomeRefrigeratorInteractionPhase>
            PhaseChanged;

        public bool IsInitialized { get; private set; }
        public bool OwnsInteraction => ownsInteraction;
        public HomeRefrigeratorInteractionPhase Phase =>
            timeline != null
                ? timeline.Phase
                : HomeRefrigeratorInteractionPhase.Closed;
        public HomeRefrigeratorInteractionTimeline Timeline => timeline;
        public HomeRefrigeratorFirstPersonHand FirstPersonHand =>
            firstPersonHand;
        public HomeRefrigeratorItemInspectionController ItemInspection =>
            itemInspection;
        public HomeRefrigeratorPlan Plan => plan;
        public HomeRefrigeratorView View => view;
        public string PromptKey =>
            Phase == HomeRefrigeratorInteractionPhase.Inspecting
                ? ClosePromptKey
                : OpenPromptKey;
        public Vector3 InteractionPosition =>
            plan != null
                ? home.transform.TransformPoint(plan.ApproachPosition)
                : transform.position;

        public void Initialize(
            HomeInteriorRoot homeRoot,
            HomeRefrigeratorPlan refrigeratorPlan,
            HomeRefrigeratorView refrigeratorView)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            if (refrigeratorPlan == null)
            {
                throw new ArgumentNullException(
                    nameof(refrigeratorPlan));
            }

            if (refrigeratorView == null)
            {
                throw new ArgumentNullException(
                    nameof(refrigeratorView));
            }

            ValidateHome(homeRoot);
            CancelInteraction();
            home = homeRoot;
            plan = refrigeratorPlan;
            view = refrigeratorView;
            targetCamera = home.CameraFollow.GetComponent<Camera>();
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                throw new InvalidOperationException(
                    "The refrigerator interaction requires the Home camera.");
            }

            timeline = new HomeRefrigeratorInteractionTimeline();
            firstPersonHand =
                GetComponent<HomeRefrigeratorFirstPersonHand>();
            if (firstPersonHand == null)
            {
                firstPersonHand =
                    gameObject.AddComponent<
                        HomeRefrigeratorFirstPersonHand>();
            }

            firstPersonHand.Initialize(
                targetCamera,
                view.HandlePivot);
            itemInspection =
                GetComponent<
                    HomeRefrigeratorItemInspectionController>();
            if (itemInspection == null)
            {
                itemInspection =
                    gameObject.AddComponent<
                        HomeRefrigeratorItemInspectionController>();
            }

            itemInspection.Initialize(targetCamera, view);
            if (closePromptAction == null)
            {
                closePromptAction = RequestClose;
            }

            view.ResetPresentation();
            home.Soundscape?.SetRefrigeratorDoorOpenAmount(0f);
            IsInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return IsInitialized &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   interactor == home.Player.Interactor &&
                   timeline != null &&
                   timeline.CanBeginOpen &&
                   !ownsInteraction &&
                   !SceneTransitionService.IsTransitioning &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   home.AnimatedInteraction != null &&
                   !home.AnimatedInteraction.IsActive;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor))
            {
                BeginInteraction();
            }
        }

        public bool BeginInteraction()
        {
            if (!CanInteract(home != null
                    ? home.Player.Interactor
                    : null))
            {
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    home.Player.Interactor,
                    home.CameraFollow,
                    home.IntoxicationHud))
            {
                return false;
            }

            try
            {
                CapturePlayerVisualState();
                CaptureCameraPath();
                ResetCueState();
                if (!timeline.BeginOpen())
                {
                    RestoreOwnedState();
                    return false;
                }

                ownsInteraction = true;
                ApplyCurrentPresentation();
                PhaseChanged?.Invoke(timeline.Phase);
                return true;
            }
            catch
            {
                timeline?.Cancel();
                ownsInteraction = false;
                RestoreOwnedState();
                throw;
            }
        }

        public bool RequestClose()
        {
            if (itemInspection != null && itemInspection.IsActive)
            {
                itemInspection.RequestReturn();
                return false;
            }

            if (!ownsInteraction ||
                timeline == null ||
                !timeline.BeginClose())
            {
                return false;
            }

            closingRequested = true;
            PlayClosingHinge();
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(timeline.Phase);
            return true;
        }

        public void AdvanceInteraction(float unscaledDeltaTime)
        {
            if (!ownsInteraction || timeline == null)
            {
                return;
            }

            HomeRefrigeratorInteractionPhase previousPhase =
                timeline.Phase;
            timeline.Advance(unscaledDeltaTime);
            bool canBrowseItems = timeline.IsInspecting;
            itemInspection?.SetBrowsingEnabled(canBrowseItems);
            if (canBrowseItems)
            {
                itemInspection?.AdvanceInspection(unscaledDeltaTime);
            }

            PlayCrossedCues();
            ApplyCurrentPresentation();
            if (timeline.Phase ==
                HomeRefrigeratorInteractionPhase.Closed)
            {
                ownsInteraction = false;
                RestoreOwnedState();
            }

            if (timeline.Phase != previousPhase)
            {
                PhaseChanged?.Invoke(timeline.Phase);
            }
        }

        public bool CancelInteraction()
        {
            bool hadInteraction =
                ownsInteraction ||
                playerVisualStateCaptured ||
                modalLock.IsLocked;
            itemInspection?.CancelAndRestore();
            timeline?.Cancel();
            ownsInteraction = false;
            if (!hadInteraction)
            {
                view?.ResetPresentation();
                firstPersonHand?.Hide();
                return false;
            }

            RestoreOwnedState();
            PhaseChanged?.Invoke(
                HomeRefrigeratorInteractionPhase.Closed);

            return true;
        }

        private void Update()
        {
            if (!ownsInteraction)
            {
                return;
            }

            bool nestedInputConsumed =
                timeline.IsInspecting &&
                itemInspection != null &&
                itemInspection.HandleInput();
            if (timeline.IsInspecting &&
                !nestedInputConsumed &&
                WasClosePressed())
            {
                RequestClose();
            }

            AdvanceInteraction(Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (ownsInteraction && timeline != null)
            {
                ApplyCamera(timeline.CurrentFrame.CameraBlend);
            }
        }

        private void OnDisable()
        {
            CancelInteraction();
        }

        private void OnDestroy()
        {
            CancelInteraction();
            PhaseChanged = null;
        }

        private void CaptureCameraPath()
        {
            cameraStartPosition = targetCamera.transform.position;
            cameraStartRotation = targetCamera.transform.rotation;
            cameraStartFieldOfView = targetCamera.fieldOfView;

            cameraTargetPosition =
                home.transform.TransformPoint(plan.CameraPosition);
            Vector3 lookAt =
                home.transform.TransformPoint(plan.CameraLookAt);
            Vector3 forward = lookAt - cameraTargetPosition;
            cameraTargetRotation = Quaternion.LookRotation(
                forward.normalized,
                home.transform.up);
            cameraTargetFieldOfView = plan.CameraFieldOfView;
            cameraControlPosition = Vector3.Lerp(
                    cameraStartPosition,
                    cameraTargetPosition,
                    0.48f) +
                home.transform.up * 0.20f +
                cameraStartRotation * Vector3.right * 0.07f;
        }

        private void ApplyCurrentPresentation()
        {
            if (timeline == null)
            {
                return;
            }

            HomeRefrigeratorInteractionFrame frame =
                timeline.CurrentFrame;
            itemInspection?.SetBrowsingEnabled(
                frame.Phase ==
                HomeRefrigeratorInteractionPhase.Inspecting);
            view?.ApplyPresentation(
                frame.DoorOpen,
                frame.HandleTurn,
                frame.LightIntensity);
            home?.Soundscape?.SetRefrigeratorDoorOpenAmount(
                frame.DoorOpen);
            ApplyCamera(frame.CameraBlend);
            firstPersonHand?.ApplyReach(frame.HandReach);
            ApplyPlayerVisualForFrame(frame);
            if (home?.InteractionPrompt != null)
            {
                bool canRequestClose =
                    frame.Phase ==
                    HomeRefrigeratorInteractionPhase.Inspecting &&
                    (itemInspection == null ||
                     !itemInspection.IsActive);
                home.InteractionPrompt.SetPrompt(
                    canRequestClose ? ClosePromptKey : string.Empty,
                    canRequestClose ? closePromptAction : null);
            }
        }

        private void ApplyCamera(float blend)
        {
            if (home?.CameraFollow == null)
            {
                return;
            }

            float amount = Mathf.Clamp01(blend);
            float remaining = 1f - amount;
            Vector3 position =
                remaining * remaining * cameraStartPosition +
                2f * remaining * amount * cameraControlPosition +
                amount * amount * cameraTargetPosition;
            home.CameraFollow.SetFixedPose(
                position,
                Quaternion.Slerp(
                    cameraStartRotation,
                    cameraTargetRotation,
                    amount),
                Mathf.Lerp(
                    cameraStartFieldOfView,
                    cameraTargetFieldOfView,
                    amount));
        }

        private void CapturePlayerVisualState()
        {
            playerVisualHideLease?.Dispose();
            playerVisualHideLease = null;
            playerVisualStateCaptured = true;
            playerVisualHidden = false;
        }

        private void ApplyPlayerVisualForFrame(
            HomeRefrigeratorInteractionFrame frame)
        {
            // After Reach has elapsed, keep the first-person handoff latched
            // even if one long frame skipped every visible hand sample or the
            // hand has already retracted for inspection.
            bool shouldHide =
                frame.Phase ==
                    HomeRefrigeratorInteractionPhase.Reach
                    ? firstPersonHand != null &&
                      firstPersonHand.IsVisible
                    : frame.Phase >=
                          HomeRefrigeratorInteractionPhase.Unsealing &&
                      frame.Phase <=
                          HomeRefrigeratorInteractionPhase.Sealing;
            SetPlayerVisualHidden(shouldHide);
        }

        private void SetPlayerVisualHidden(bool hidden)
        {
            if (!playerVisualStateCaptured ||
                home == null ||
                playerVisualHidden == hidden)
            {
                return;
            }

            PlayerRuntime player = home.Player;
            if (hidden)
            {
                playerVisualHideLease =
                    player.PresentationVisibility?.AcquireHidden(this);
            }
            else
            {
                playerVisualHideLease?.Dispose();
                playerVisualHideLease = null;
            }

            playerVisualHidden = hidden;
        }

        private void RestorePlayerVisual()
        {
            if (!playerVisualStateCaptured || home == null)
            {
                return;
            }

            SetPlayerVisualHidden(false);
            playerVisualHideLease?.Dispose();
            playerVisualHideLease = null;
            playerVisualStateCaptured = false;
            playerVisualHidden = false;
        }

        private void RestoreOwnedState()
        {
            itemInspection?.SetBrowsingEnabled(false);
            itemInspection?.CancelAndRestore();
            view?.ResetPresentation();
            firstPersonHand?.Hide();
            home?.Soundscape?.SetRefrigeratorDoorOpenAmount(0f);
            if (home?.InteractionPrompt != null)
            {
                home.InteractionPrompt.SetPrompt(string.Empty);
            }

            RestorePlayerVisual();
            modalLock.Restore();
            home?.FixedCamera?.ReapplyActiveShot();
        }

        private void ResetCueState()
        {
            openingSealPlayed = false;
            openingHingePlayed = false;
            closingRequested = false;
            closingHingePlayed = false;
            closingThunkPlayed = false;
        }

        private void PlayCrossedCues()
        {
            HomeRefrigeratorInteractionPhase phase = timeline.Phase;
            if (!openingSealPlayed &&
                (int)phase >=
                (int)HomeRefrigeratorInteractionPhase.Unsealing)
            {
                openingSealPlayed = true;
                PlayCue(RetroSfxId.RefrigeratorSeal);
            }

            if (!openingHingePlayed &&
                (int)phase >=
                (int)HomeRefrigeratorInteractionPhase.Opening)
            {
                openingHingePlayed = true;
                PlayCue(RetroSfxId.RefrigeratorHinge);
            }

            if (closingRequested &&
                !closingThunkPlayed &&
                (phase == HomeRefrigeratorInteractionPhase.Sealing ||
                 phase == HomeRefrigeratorInteractionPhase.CameraReturn ||
                 phase == HomeRefrigeratorInteractionPhase.Closed))
            {
                closingThunkPlayed = true;
                PlayCue(RetroSfxId.RefrigeratorThunk);
            }
        }

        private void PlayClosingHinge()
        {
            if (closingHingePlayed)
            {
                return;
            }

            closingHingePlayed = true;
            PlayCue(RetroSfxId.RefrigeratorHinge);
        }

        private void PlayCue(RetroSfxId cue)
        {
            if (home?.Audio == null || plan == null)
            {
                return;
            }

            home.Audio.TryPlay(
                cue,
                home.transform.TransformPoint(plan.SoundAnchor));
        }

        private static bool WasClosePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.escapeKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   (gamepad.buttonSouth.wasPressedThisFrame ||
                    gamepad.buttonEast.wasPressedThisFrame);
        }

        private static void ValidateHome(HomeInteriorRoot homeRoot)
        {
            if (homeRoot.Player.GameObject == null ||
                homeRoot.Player.Interactor == null ||
                homeRoot.Player.Visual == null ||
                homeRoot.CameraFollow == null ||
                homeRoot.FixedCamera == null ||
                homeRoot.AnimatedInteraction == null)
            {
                throw new ArgumentException(
                    "The refrigerator interaction requires an initialized " +
                    "Home player, camera and interaction stack.",
                    nameof(homeRoot));
            }
        }
    }
}
