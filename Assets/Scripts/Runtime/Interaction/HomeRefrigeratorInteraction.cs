using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Owns the modal, first-person refrigerator inspection. The pure timeline
    /// supplies animation channels; the context binds the same presentation
    /// to a location's camera, player, prompts and audio.
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

        private RefrigeratorInteractionContext context;
        private bool preparedPlayer;
        private bool nestedInspectionFocus;
        private HomeRefrigeratorPlan plan;
        private HomeRefrigeratorView view;
        private HomeRefrigeratorInteractionTimeline timeline;
        private HomeRefrigeratorItemInspectionController itemInspection;
        private Camera targetCamera;
        private Vector3 cameraStartPosition;
        private Quaternion cameraStartRotation;
        private float cameraStartFieldOfView;
        private bool cameraStateCaptured;
        private bool cameraWasFixedPose;
        private Vector3 cameraControlPosition;
        private Vector3 depthOfFieldFocusPoint;
        private Vector3 cameraTargetPosition;
        private Quaternion cameraTargetRotation;
        private float cameraTargetFieldOfView;
        private bool ownsInteraction;
        private bool isPositioning;
        private int entrySettledFrame = -1;
        private bool entryHandoffLocked;
        private bool previousEntryHandoff;
        private Vector3 entryPosition;
        private Quaternion entryRotation;
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
        public bool IsPositioning => isPositioning;
        public HomeRefrigeratorInteractionPhase Phase =>
            timeline != null
                ? timeline.Phase
                : HomeRefrigeratorInteractionPhase.Closed;
        public HomeRefrigeratorInteractionTimeline Timeline => timeline;
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
                ? context.Space.TransformPoint(plan.ApproachPosition)
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
            var adapter = new RefrigeratorInteractionContext(homeRoot.transform, homeRoot.Player,
                homeRoot.CameraFollow, homeRoot.InteractionPrompt, homeRoot.IntoxicationHud)
            {
                Admission = () => homeRoot != null && homeRoot.AnimatedInteraction != null && !homeRoot.AnimatedInteraction.IsActive,
                RestoreCamera = () => homeRoot?.FixedCamera?.ReapplyActiveShot(),
                DoorAmountChanged = amount => homeRoot?.Soundscape?.SetRefrigeratorDoorOpenAmount(amount),
                PlayCue = (cue, position) => homeRoot?.Audio?.TryPlay(cue, position)
            };
            Initialize(adapter, refrigeratorPlan, refrigeratorView);
        }

        public void Initialize(RefrigeratorInteractionContext interactionContext,
            HomeRefrigeratorPlan refrigeratorPlan, HomeRefrigeratorView refrigeratorView)
        {
            if (interactionContext == null || refrigeratorPlan == null || refrigeratorView == null)
                throw new ArgumentNullException();
            CancelInteraction();
            context = interactionContext;
            plan = refrigeratorPlan;
            view = refrigeratorView;
            targetCamera = context.CameraFollow.GetComponent<Camera>();
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                throw new InvalidOperationException(
                    "The refrigerator interaction requires a camera.");
            }

            timeline = new HomeRefrigeratorInteractionTimeline();
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
            context.DoorAmountChanged?.Invoke(0f);
            IsInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return IsInitialized &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   context != null && interactor == context.Player.Interactor &&
                   timeline != null &&
                   timeline.CanBeginOpen &&
                   !ownsInteraction &&
                   !SceneTransitionService.IsTransitioning &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   (context.Admission == null || context.Admission());
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
            if (!CanInteract(context != null
                    ? context.Player.Interactor
                    : null))
            {
                return false;
            }

            if (context.PositionPlayerAtEntry && (context.Player.Motor == null ||
                Mathf.Abs(context.Player.GameObject.transform.position.y -
                    context.Space.TransformPoint(plan.ApproachPosition).y) > .65f)) return false;

            if (!modalLock.TryCaptureAndDisable(
                    context.Player.Interactor,
                    context.CameraFollow,
                    context.Hud))
            {
                return false;
            }

            try
            {
                preparedPlayer = true;
                context.PreparingPlayer?.Invoke();
                CapturePlayerVisualState();
                ResetCueState();
                ownsInteraction = true;
                if (context.PositionPlayerAtEntry)
                {
                    // The same physical move used by authored actions keeps the
                    // chest light and its hero at the cabinet's actual dock.
                    entryPosition = context.Space.TransformPoint(plan.ApproachPosition);
                    entryRotation = Quaternion.LookRotation(context.Space.forward, context.Space.up);
                    isPositioning = true;
                    entrySettledFrame = -1;
                    return true;
                }

                return BeginPreparedOpen();
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

        public void AdvanceInteraction(float deltaTime)
        {
            if (!ownsInteraction || timeline == null)
            {
                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                CancelInteraction();
                return;
            }
            if (GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused) return;
            if (isPositioning)
            {
                AdvanceEntryPose(deltaTime);
                return;
            }

            HomeRefrigeratorInteractionPhase previousPhase =
                timeline.Phase;
            timeline.Advance(deltaTime);
            bool canBrowseItems = timeline.IsInspecting;
            itemInspection?.SetBrowsingEnabled(canBrowseItems);
            if (canBrowseItems)
            {
                itemInspection?.AdvanceInspection(deltaTime);
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

            if (isPositioning && GameInput.WasPressed(GameInputAction.Cancel))
            {
                CancelInteraction();
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

            AdvanceInteraction(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (ownsInteraction && !isPositioning && timeline != null)
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

        private void AdvanceEntryPose(float seconds)
        {
            if (context.Player.Motor == null || context.Player.GameObject == null ||
                Mathf.Abs(context.Player.GameObject.transform.position.y - entryPosition.y) > .65f)
            {
                CancelInteraction();
                return;
            }
            if (entrySettledFrame >= 0)
            {
                if (Time.frameCount <= entrySettledFrame) return;
                ReleaseEntryHandoff();
                isPositioning = false;
                BeginPreparedOpen();
                return;
            }
            if (context.Player.Motor.MoveTowardsInteractionPose(entryPosition, entryRotation, seconds))
            {
                previousEntryHandoff = context.Player.Visual.InteractionHandoffLocked;
                context.Player.Visual.SetInteractionHandoffLocked(true);
                entryHandoffLocked = true;
                entrySettledFrame = Time.frameCount;
            }
            else if (context.Player.Motor.InteractionPoseMoveStalled) CancelInteraction();
        }

        private bool BeginPreparedOpen()
        {
            try
            {
                context.EntryPoseReady?.Invoke();
                CaptureCameraPath();
                if (!timeline.BeginOpen())
                {
                    ownsInteraction = false;
                    RestoreOwnedState();
                    return false;
                }
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

        private void ReleaseEntryHandoff()
        {
            if (!entryHandoffLocked) return;
            entryHandoffLocked = false;
            context?.Player.Visual.SetInteractionHandoffLocked(previousEntryHandoff);
        }

        private void CaptureCameraPath()
        {
            cameraStartPosition = targetCamera.transform.position;
            cameraStartRotation = targetCamera.transform.rotation;
            cameraStartFieldOfView = targetCamera.fieldOfView;
            cameraWasFixedPose = context.CameraFollow.FixedPoseActive;
            cameraStateCaptured = true;

            cameraTargetPosition =
                context.Space.TransformPoint(plan.CameraPosition);
            Vector3 lookAt =
                context.Space.TransformPoint(plan.CameraLookAt);
            Vector3 forward = lookAt - cameraTargetPosition;
            cameraTargetRotation = Quaternion.LookRotation(
                forward.normalized,
                context.Space.up);
            cameraTargetFieldOfView = plan.CameraFieldOfView;
            cameraControlPosition = Vector3.Lerp(
                    cameraStartPosition,
                    cameraTargetPosition,
                    0.48f) +
                context.Space.up * 0.20f +
                cameraStartRotation * Vector3.right * 0.07f;

            // The shelves at arm's length hold focus; the kitchen
            // behind the door melts away.
            depthOfFieldFocusPoint = lookAt;
            nestedInspectionFocus = false;
            CinematicDepthOfField.Begin(
                Vector3.Distance(
                    cameraStartPosition,
                    depthOfFieldFocusPoint),
                4f);
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
            context?.DoorAmountChanged?.Invoke(frame.DoorOpen);
            ApplyCamera(frame.CameraBlend);
            ApplyPlayerVisualForFrame(frame);
            if (context?.Prompt != null)
            {
                bool canRequestClose =
                    frame.Phase ==
                    HomeRefrigeratorInteractionPhase.Inspecting &&
                    (itemInspection == null ||
                     !itemInspection.IsActive);
                context.Prompt.SetPrompt(
                    canRequestClose ? ClosePromptKey : string.Empty,
                    canRequestClose ? closePromptAction : null);
            }
        }

        private void ApplyCamera(float blend)
        {
            if (context?.CameraFollow == null)
            {
                return;
            }

            float amount = Mathf.Clamp01(blend);
            float remaining = 1f - amount;
            Vector3 position =
                remaining * remaining * cameraStartPosition +
                2f * remaining * amount * cameraControlPosition +
                amount * amount * cameraTargetPosition;
            context.CameraFollow.SetFixedPose(
                position,
                Quaternion.Slerp(
                    cameraStartRotation,
                    cameraTargetRotation,
                    amount),
                Mathf.Lerp(
                    cameraStartFieldOfView,
                    cameraTargetFieldOfView,
                    amount));
            // The nested presenter owns focus on its camera-local model.
            // Applying the cabinet shot later in the frame must not blur it again.
            bool inspectingItem = itemInspection != null && itemInspection.IsActive;
            if (inspectingItem != nestedInspectionFocus)
            {
                nestedInspectionFocus = inspectingItem;
                CinematicDepthOfField.Begin(inspectingItem ? WorldItemInspectionPresenter.PreviewDistance :
                    Vector3.Distance(position, depthOfFieldFocusPoint), inspectingItem ? 16f : 4f, inspectingItem ? 35f : 50f);
            }
            if (!inspectingItem)
                CinematicDepthOfField.SetFocusDistance(Vector3.Distance(position, depthOfFieldFocusPoint));
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
            // The close-up owns body visibility directly. Refrigerator opening
            // creates no camera-local hero subset in any location.
            bool shouldHide =
                frame.Phase >= HomeRefrigeratorInteractionPhase.Reach &&
                frame.Phase <= HomeRefrigeratorInteractionPhase.Sealing;
            SetPlayerVisualHidden(shouldHide);
        }

        private void SetPlayerVisualHidden(bool hidden)
        {
            if (!playerVisualStateCaptured ||
                context == null ||
                playerVisualHidden == hidden)
            {
                return;
            }

            PlayerRuntime player = context.Player;
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
            if (!playerVisualStateCaptured || context == null)
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
            if (isPositioning) context?.Player.Motor?.CancelInteractionPoseMove();
            isPositioning = false;
            entrySettledFrame = -1;
            ReleaseEntryHandoff();
            CinematicDepthOfField.End();
            itemInspection?.SetBrowsingEnabled(false);
            itemInspection?.CancelAndRestore();
            view?.ResetPresentation();
            context?.DoorAmountChanged?.Invoke(0f);
            if (context?.Prompt != null)
            {
                context.Prompt.SetPrompt(string.Empty);
            }

            RestorePlayerVisual();
            modalLock.Restore();
            if (cameraStateCaptured)
            {
                cameraStateCaptured = false;
                if (context?.RestoreCamera != null) context.RestoreCamera();
                else if (context?.CameraFollow != null)
                {
                    if (cameraWasFixedPose)
                        context.CameraFollow.SetFixedPose(cameraStartPosition, cameraStartRotation, cameraStartFieldOfView);
                    else context.CameraFollow.ClearFixedPose();
                }
            }
            if (preparedPlayer)
            {
                preparedPlayer = false;
                context?.RestoringPlayer?.Invoke();
            }
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
            if (context?.PlayCue == null || plan == null)
            {
                return;
            }

            context.PlayCue(cue, context.Space.TransformPoint(plan.SoundAnchor));
        }

        private static bool WasClosePressed()
        {
            return GameInput.WasPressed(GameInputAction.Interact) ||
                   GameInput.WasPressed(GameInputAction.Cancel);
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
