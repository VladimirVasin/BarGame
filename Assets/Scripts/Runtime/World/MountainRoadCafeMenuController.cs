using System;
using BarPromenade.Runtime.World;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Binds the hero's cafe stool to the attendant's reusable physical menu
    /// round trip. It consumes W/S and Space while the close-up is open; the
    /// seat keeps E as one context-sensitive action: close, reopen while
    /// looking at the booklet, or stand once the booklet rests on the counter.
    /// </summary>
    [DefaultExecutionOrder(120)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeMenuController :
        MonoBehaviour,
        ISeatedInteractionHandler
    {
        public const string CloseMenuPromptKey =
            "interaction.close_counter_menu";
        public const string OpenMenuPromptKey =
            "interaction.open_counter_menu";

        private CityBenchSitInteraction seat;
        private MountainRoadCafeSeatView seatView;
        private MountainRoadCafeCastController cast;
        private MountainRoadCafeMenuPresentation presentation;
        private MountainRoadCafeMenuHintView hint;
        private MountainRoadCafeMenuModel model;
        private Camera targetCamera;
        private int inputUnlockFrame;
        private bool waitingForSeatExit;
        private bool restingMenuGazeArmed;
        private bool retrievalPendingUntilMenuPlaced;
        private bool deliveryReservedForEnteringSeat;

        public bool IsInitialized { get; private set; }
        public MountainRoadCafeMenuState State => model?.State ??
            MountainRoadCafeMenuState.Hidden;
        public int SelectedIndex => model?.SelectedIndex ?? 0;
        public string SelectedItemId => model?.SelectedItemId ?? string.Empty;
        public string ConfirmedItemId => model?.ConfirmedItemId;
        public bool IsInputActive => IsInitialized && seat != null &&
            seat.IsSeated && model != null &&
            model.State == MountainRoadCafeMenuState.Open &&
            !CounterMenuInput.IsBlockedByOtherUi();
        public MountainRoadCafeMenuPresentation Presentation => presentation;
        public MountainRoadCafeMenuHintView Hint => hint;
        public bool IsLookingAtRestingMenu =>
            restingMenuGazeArmed &&
            presentation != null &&
            presentation.IsLookingAtRestingMenu(ResolveCamera());
        public string SeatedPromptKey =>
            State == MountainRoadCafeMenuState.Open
                ? CloseMenuPromptKey
                : State == MountainRoadCafeMenuState.Resting &&
                  IsLookingAtRestingMenu
                    ? OpenMenuPromptKey
                    : State == MountainRoadCafeMenuState.Resting
                        ? CityBenchSitInteraction.StandPromptKey
                        : string.Empty;

        public void Initialize(
            CityBenchSitInteraction configuredSeat,
            MountainRoadCafeSeatView configuredSeatView,
            MountainRoadCafeCastController configuredCast,
            MountainRoadCafeMenuPresentation configuredPresentation)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The mountain cafe menu is already initialized.");
            }

            seat = configuredSeat ??
                throw new ArgumentNullException(nameof(configuredSeat));
            seatView = configuredSeatView ??
                throw new ArgumentNullException(nameof(configuredSeatView));
            cast = configuredCast ??
                throw new ArgumentNullException(nameof(configuredCast));
            presentation = configuredPresentation ??
                throw new ArgumentNullException(
                    nameof(configuredPresentation));
            if (!seatView.IsInitialized || !cast.IsInitialized ||
                !presentation.IsConfigured)
            {
                throw new ArgumentException(
                    "Cafe menu bindings must already be configured.");
            }

            model = new MountainRoadCafeMenuModel();
            hint = MountainRoadCafeMenuHintView.Create(transform);
            seat.SeatedChanged += HandleSeatedChanged;
            seat.InteractionCompleted += HandleSeatInteractionCompleted;
            seat.Controller.PhaseChanged += HandleSeatPhaseChanged;
            seat.SetSeatedInteractionHandler(this);
            targetCamera = Camera.main;
            IsInitialized = true;
            presentation.SetSelection(0, false);
            if (seat.IsSeated)
            {
                BeginDelivery();
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (model.State == MountainRoadCafeMenuState.Delivering &&
                cast.ServiceFrame.HeroMenuPlaced)
            {
                CompleteDelivery();
            }

            if (model.State == MountainRoadCafeMenuState.Retrieving)
            {
                RequestPhysicalRetrievalWhenReady();
                if (cast.ServiceFrame.HeroMenuRetrieved)
                {
                    model.CompleteRetrieval();
                }
            }

            UpdateRestingMenuGazeArm();

            // The player can reach the stool again before the attendant has
            // finished carrying the previous booklet to the service dock.
            // Preserve that seating request and start a fresh round trip as
            // soon as the shared physical prop is actually available again.
            if (seat.IsSeated &&
                model.State == MountainRoadCafeMenuState.Closed)
            {
                BeginDelivery();
            }

            bool inputActive = IsInputActive;
            if (inputActive)
            {
                hint.Show();
            }
            else
            {
                hint.Hide();
            }

            if (!inputActive || Time.frameCount < inputUnlockFrame)
            {
                return;
            }

            if (CounterMenuInput.WasCancelPressed())
            {
                RestMenuOnCounter();
                return;
            }

            int selectionDelta = CounterMenuInput.ReadSelectionDelta();
            if (selectionDelta < 0)
            {
                MoveSelection(previous: true);
            }
            else if (selectionDelta > 0)
            {
                MoveSelection(previous: false);
            }

            if (CounterMenuInput.WasConfirmPressed() && model.Confirm())
            {
                presentation.SetSelection(model.SelectedIndex, true);
                hint.Hide();
                RestMenuOnCounter();
            }
        }

        public bool CanHandleSeatedInteraction(PlayerInteractor interactor)
        {
            if (!IsInitialized || !isActiveAndEnabled ||
                interactor == null ||
                CounterMenuInput.IsBlockedByOtherUi())
            {
                return false;
            }

            return model.State == MountainRoadCafeMenuState.Open ||
                   model.State == MountainRoadCafeMenuState.Resting;
        }

        public void HandleSeatedInteraction(PlayerInteractor interactor)
        {
            if (!CanHandleSeatedInteraction(interactor))
            {
                return;
            }

            if (model.State == MountainRoadCafeMenuState.Open)
            {
                RestMenuOnCounter();
                return;
            }

            if (IsLookingAtRestingMenu)
            {
                ReopenMenu();
                return;
            }

            waitingForSeatExit = true;
            if (!seat.RequestExit())
            {
                waitingForSeatExit = false;
            }
        }

        private void MoveSelection(bool previous)
        {
            bool moved = previous
                ? model.MovePrevious()
                : model.MoveNext();
            if (!moved)
            {
                return;
            }

            presentation.SetSelection(model.SelectedIndex, false);
        }

        private void HandleSeatedChanged(
            CityBenchSitInteraction changedSeat,
            bool seated)
        {
            if (changedSeat != seat)
            {
                return;
            }

            if (seated)
            {
                retrievalPendingUntilMenuPlaced = false;
                deliveryReservedForEnteringSeat = false;
                if (model.State == MountainRoadCafeMenuState.Open)
                {
                    FocusOpenMenu();
                    return;
                }

                BeginDelivery();
            }
            else
            {
                hint.Hide();
                seatView?.EndMenuFocus();
                if (model.State == MountainRoadCafeMenuState.Delivering &&
                    !cast.ServiceFrame.HeroMenuPlaced)
                {
                    retrievalPendingUntilMenuPlaced = true;
                }

                if (seat.Controller != null &&
                    seat.Controller.Phase ==
                        PlayerAnimatedInteractionPhase.Exiting)
                {
                    waitingForSeatExit = true;
                }
                else
                {
                    BeginRetrieval();
                }
            }
        }

        private void HandleSeatInteractionCompleted(
            CityBenchSitInteraction completedSeat)
        {
            if (completedSeat != seat || !waitingForSeatExit)
            {
                return;
            }

            waitingForSeatExit = false;
            BeginRetrieval();
        }

        private void HandleSeatPhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (phase != PlayerAnimatedInteractionPhase.Idle ||
                (!waitingForSeatExit &&
                 !deliveryReservedForEnteringSeat))
            {
                return;
            }

            // A cancelled exit has no InteractionCompleted callback. The same
            // is true when a pending delivery accepted a quick re-entry that
            // then aborts before SeatedChanged(true). Idle is authoritative:
            // without a sitter the placed booklet must close and go home.
            waitingForSeatExit = false;
            deliveryReservedForEnteringSeat = false;
            BeginRetrieval();
        }

        private void BeginDelivery()
        {
            if (model.State == MountainRoadCafeMenuState.Closed &&
                cast.TryResetHeroMenuRoundTrip())
            {
                model.Reset();
                presentation.SetSelection(0, false);
                restingMenuGazeArmed = false;
            }

            if (model.State != MountainRoadCafeMenuState.Hidden)
            {
                return;
            }

            if (cast.TryRequestHeroMenu())
            {
                model.BeginDelivery();
            }
        }

        private void BeginRetrieval()
        {
            seatView?.EndMenuFocus();
            deliveryReservedForEnteringSeat = false;
            if (model != null &&
                model.State == MountainRoadCafeMenuState.Delivering &&
                !cast.ServiceFrame.HeroMenuPlaced)
            {
                // A forced/external exit can finish before the attendant has
                // reached the counter. Let the one physical booklet arrive,
                // then close it before asking the attendant to take it back.
                // A quick re-entry clears this flag and accepts that same
                // delivery instead of racing it with a retrieval.
                retrievalPendingUntilMenuPlaced = true;
                return;
            }

            if (model != null &&
                (model.State == MountainRoadCafeMenuState.Open ||
                 model.State == MountainRoadCafeMenuState.Confirmed))
            {
                model.RestOnCounter();
                presentation.RestOnCounter();
            }

            if (model == null || !model.BeginRetrieval())
            {
                return;
            }

            RequestPhysicalRetrievalWhenReady();
        }

        private void CompleteDelivery()
        {
            // The presentation normally samples this frame in LateUpdate.
            // Resting requires its IsPlaced flag now, in the same Update that
            // observes HeroMenuPlaced, so synchronize it once explicitly.
            presentation.RefreshFromServiceFrame();
            if (!model.Open())
            {
                return;
            }

            bool enteringSeatOwnsIncomingMenu = seat != null &&
                !seat.IsSeated &&
                seat.OwnsActiveInteraction &&
                seat.Controller != null &&
                seat.Controller.Phase !=
                    PlayerAnimatedInteractionPhase.Exiting &&
                seat.Controller.Phase !=
                    PlayerAnimatedInteractionPhase.Idle;
            bool seatOwnsIncomingMenu = seat != null &&
                (seat.IsSeated || enteringSeatOwnsIncomingMenu);
            if (retrievalPendingUntilMenuPlaced &&
                !seatOwnsIncomingMenu)
            {
                retrievalPendingUntilMenuPlaced = false;
                deliveryReservedForEnteringSeat = false;
                if (!RestMenuOnCounter())
                {
                    throw new InvalidOperationException(
                        "The delivered cafe menu could not be closed " +
                        "before retrieval.");
                }

                if (!waitingForSeatExit)
                {
                    BeginRetrieval();
                }
                return;
            }

            deliveryReservedForEnteringSeat =
                retrievalPendingUntilMenuPlaced &&
                enteringSeatOwnsIncomingMenu;
            retrievalPendingUntilMenuPlaced = false;
            presentation.SetSelection(model.SelectedIndex, false);
            if (seat.IsSeated)
            {
                FocusOpenMenu();
            }
        }

        private void FocusOpenMenu()
        {
            inputUnlockFrame = Time.frameCount + 1;
            presentation.SetSelection(model.SelectedIndex, false);
            seatView.BeginMenuFocus(
                presentation.ResolveCameraFocusPose(
                    seatView.CurrentCameraPosition));
        }

        private void RequestPhysicalRetrievalWhenReady()
        {
            MountainRoadCafeServiceFrame frame = cast.ServiceFrame;
            if (!frame.HeroMenuPlaced ||
                frame.HeroMenuRetrievalRequested ||
                frame.HeroMenuRetrieved)
            {
                return;
            }

            cast.TryRequestHeroMenuRetrieval();
        }

        private bool RestMenuOnCounter()
        {
            if (model == null || !model.RestOnCounter())
            {
                return false;
            }

            if (!presentation.RestOnCounter())
            {
                throw new InvalidOperationException(
                    "The cafe menu could not rest at its counter dock.");
            }

            seatView?.EndMenuFocus();
            hint?.Hide();
            inputUnlockFrame = Time.frameCount + 1;
            restingMenuGazeArmed = false;
            return true;
        }

        private bool ReopenMenu()
        {
            if (model == null || !model.Reopen())
            {
                return false;
            }

            if (!presentation.ReopenOnCounter())
            {
                throw new InvalidOperationException(
                    "The cafe menu could not reopen at its counter dock.");
            }

            presentation.SetSelection(model.SelectedIndex, false);
            seatView.BeginMenuFocus(
                presentation.ResolveCameraFocusPose(
                    seatView.CurrentCameraPosition));
            inputUnlockFrame = Time.frameCount + 1;
            restingMenuGazeArmed = false;
            return true;
        }

        private void UpdateRestingMenuGazeArm()
        {
            if (model == null ||
                model.State != MountainRoadCafeMenuState.Resting)
            {
                restingMenuGazeArmed = false;
                return;
            }

            if (!restingMenuGazeArmed &&
                presentation != null &&
                !presentation.IsLookingAtRestingMenu(ResolveCamera()))
            {
                // Closing starts while the camera still faces the pages.
                // Require one deliberate look-away before that same gaze can
                // mean "open"; until then the second E correctly means stand.
                restingMenuGazeArmed = true;
            }
        }

        private Camera ResolveCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            return targetCamera;
        }

        private void OnEnable()
        {
            if (!IsInitialized || seat == null)
            {
                return;
            }

            seat.SetSeatedInteractionHandler(this);
            if (!seat.IsSeated || model == null ||
                model.State != MountainRoadCafeMenuState.Open)
            {
                return;
            }

            inputUnlockFrame = Time.frameCount + 1;
            seatView.BeginMenuFocus(
                presentation.ResolveCameraFocusPose(
                    seatView.CurrentCameraPosition));
        }

        private void OnDisable()
        {
            if (seat != null)
            {
                seat.SetSeatedInteractionHandler(null);
            }
            seatView?.EndMenuFocus();
            hint?.Hide();
        }

        private void OnDestroy()
        {
            if (seat != null)
            {
                seat.SeatedChanged -= HandleSeatedChanged;
                seat.InteractionCompleted -=
                    HandleSeatInteractionCompleted;
                if (seat.Controller != null)
                {
                    seat.Controller.PhaseChanged -=
                        HandleSeatPhaseChanged;
                }
                seat.SetSeatedInteractionHandler(null);
            }

            seatView?.EndMenuFocus();
            hint?.Hide();
        }
    }
}
