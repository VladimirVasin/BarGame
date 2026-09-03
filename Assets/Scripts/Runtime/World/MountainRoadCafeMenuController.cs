using System;
using BarPromenade.Runtime.World;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Binds the hero's cafe stool to the attendant's one physical menu
    /// delivery and retrieval. It consumes only W/S and Space while the
    /// close-up is open; the ordinary interactor retains E/Enter/South for
    /// standing and the seat view alone owns the temporary camera lock.
    /// </summary>
    [DefaultExecutionOrder(120)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeMenuController : MonoBehaviour
    {
        private CityBenchSitInteraction seat;
        private MountainRoadCafeSeatView seatView;
        private MountainRoadCafeCastController cast;
        private MountainRoadCafeMenuPresentation presentation;
        private MountainRoadCafeMenuHintView hint;
        private MountainRoadCafeMenuModel model;
        private int inputUnlockFrame;

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
                cast.ServiceFrame.HeroMenuPlaced && model.Open())
            {
                inputUnlockFrame = Time.frameCount + 1;
                presentation.SetSelection(model.SelectedIndex, false);
                seatView.BeginMenuFocus(
                    presentation.ResolveCameraFocusPose(
                        seatView.CurrentCameraPosition));
            }

            if (model.State == MountainRoadCafeMenuState.Retrieving)
            {
                RequestPhysicalRetrievalWhenReady();
                if (cast.ServiceFrame.HeroMenuRetrieved)
                {
                    model.CompleteRetrieval();
                }
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
                BeginRetrieval();
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
                BeginDelivery();
            }
            else
            {
                hint.Hide();
                BeginRetrieval();
            }
        }

        private void BeginDelivery()
        {
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
            if (model == null || !model.BeginRetrieval())
            {
                return;
            }

            RequestPhysicalRetrievalWhenReady();
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

        private void OnEnable()
        {
            if (!IsInitialized || seat == null || !seat.IsSeated ||
                model == null ||
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
            seatView?.EndMenuFocus();
            hint?.Hide();
        }

        private void OnDestroy()
        {
            if (seat != null)
            {
                seat.SeatedChanged -= HandleSeatedChanged;
            }

            seatView?.EndMenuFocus();
            hint?.Hide();
        }
    }
}
