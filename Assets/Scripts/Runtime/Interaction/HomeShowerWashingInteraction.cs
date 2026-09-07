using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum HomeShowerSoapPhase { SelectSoap, Pickup, Washing, PutDown, Finished }

    /// <summary>The interactive soap action lives inside the existing shower ownership/exit.</summary>
    public sealed partial class HomeShowerInteraction
    {
        public const string SoapName = "Home Bathroom Shower Soap";
        public const string SoapPromptKey = "interaction.take_shower_soap";
        public const string ExitPromptKey = "interaction.exit_shower";
        public const float WashStrokeFrequency = 1.1f;
        private readonly HomeShowerWashingProgress washingProgress = new HomeShowerWashingProgress();
        private HomeShowerSoapPose soapPose;
        private HomeShowerSoapAffordance soapAffordance;
        private Transform soap;
        private HomeShowerSoapPhase soapPhase;
        private float soapPhaseElapsed;
        private bool soapStarted, soapStartRendered, soapEndRendered, washingFinishRequested;
        private bool washingLookHeld, scrubHeld, scrubSubmitted, previousWashingLook, pointerArmed;
        private bool gamepadWashingPointer, selectPointRequested;
        private Vector2 scrubPointer;
        private HomeShowerSoapTarget selectedWashTarget, previousStrokeTarget;
        private Vector3 strokeDirection;
        private float strokeRadius, strokeClock;

        public bool HasSelectedWashPoint => selectedWashTarget.IsValid;
        public HomeShowerSoapTarget SelectedWashTarget => selectedWashTarget;
        public string SelectionDiagnostic { get; private set; } = "No skin press received.";
        public bool SoapPromptVisible => OwnsScene && soapStarted && !washingFinishRequested &&
            !PauseMenuController.IsAnyPaused && soapPhase == HomeShowerSoapPhase.SelectSoap &&
            timeline.Phase == HomeShowerScenePhase.Wash && soapAffordance != null &&
            soapAffordance.TryGetSoapPromptPosition(out _);
        public string SoapPromptText => LocalizationService.Get(SoapPromptKey);
        public Vector2 SoapPromptScreenPosition => soapAffordance != null &&
            soapAffordance.TryGetSoapPromptPosition(out Vector2 point) ? point : Vector2.zero;
        public bool ExitPromptVisible => OwnsScene && !washingFinishRequested &&
            !PauseMenuController.IsAnyPaused && timeline.Phase == HomeShowerScenePhase.Wash &&
            soapAffordance != null && soapAffordance.TryGetExitPromptPosition(out _);
        public string ExitPromptText => LocalizationService.Get(ExitPromptKey);
        public Vector2 ExitPromptScreenPosition => soapAffordance != null &&
            soapAffordance.TryGetExitPromptPosition(out Vector2 point) ? point : Vector2.zero;
        public Rect ExitPromptRenderedScreenRect => soapAffordance != null
            ? soapAffordance.ExitPromptRenderedScreenRect : default;
        public int ExitPromptRenderedFrame => soapAffordance != null ? soapAffordance.ExitPromptRenderedFrame : -1;

        protected override bool TryHandleSceneAction()
        {
            if (timeline.Phase != HomeShowerScenePhase.Wash) return false;
            if (soapStarted && soapPhase == HomeShowerSoapPhase.SelectSoap) TryPickUpSoap();
            // E remains a pickup action throughout washing, never the fallback exit.
            return true;
        }

        public HomeShowerWashingProgress WashingProgress => washingProgress;
        public HomeShowerSoapPose SoapPose => soapPose;
        public Transform Soap => soap;
        public HomeShowerSoapPhase SoapPhase => soapPhase;
        public bool SoapHeld => soapPose != null && soapPose.HasSoap;
        public bool SoapHighlighted => soapAffordance != null && soapAffordance.Highlighted;
        public bool GaugeVisible => OwnsScene && soapStarted &&
            timeline.Phase == HomeShowerScenePhase.Wash && !washingFinishRequested &&
            soapPhase != HomeShowerSoapPhase.Finished;
        public bool WashingPointerAvailable => soapStarted && !washingFinishRequested &&
            (soapPhase == HomeShowerSoapPhase.SelectSoap || soapPhase == HomeShowerSoapPhase.Washing);
        public Camera WashingCamera => Home != null && Home.CameraFollow != null ? Home.CameraFollow.Camera : null;
        public bool ShowGamepadWashingPointer => gamepadWashingPointer && WashingPointerAvailable && !washingLookHeld;
        public Vector2 WashingPointer => scrubPointer;

        private void InitializeWashing(Transform room)
        {
            soap = room.Find(SoapName);
            soapPose = gameObject.AddComponent<HomeShowerSoapPose>();
            soapAffordance = gameObject.AddComponent<HomeShowerSoapAffordance>();
            soapAffordance.Initialize(this, soap, room.Find(ColdHandleName));
            gameObject.AddComponent<HomeShowerGaugeView>().Bind(this);
        }

        private bool PrepareWashing() => soap != null && soapPose != null &&
            soapPose.Initialize(Home, soap, washPose);

        private void ResetWashing()
        {
            washingProgress.Reset();
            soapStarted = soapStartRendered = soapEndRendered = washingFinishRequested = false;
            washingLookHeld = previousWashingLook = scrubHeld = scrubSubmitted = pointerArmed = false;
            gamepadWashingPointer = false;
            soapPhase = HomeShowerSoapPhase.SelectSoap;
            soapPhaseElapsed = 0f;
            scrubPointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            selectPointRequested = false;
            ClearWashSelection();
            soapAffordance?.Clear();
        }

        private void AdvanceWashing(float seconds)
        {
            if (timeline.Phase != HomeShowerScenePhase.Wash || PauseMenuController.IsAnyPaused) return;
            if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
            {
                RequestStopFromSceneInput();
                if (washingFinishRequested) return;
            }
            if (!soapStarted) return;
            if (soapEndRendered)
            {
                if (soapPhase == HomeShowerSoapPhase.Pickup)
                    SetSoapPhase(washingFinishRequested ? HomeShowerSoapPhase.PutDown : HomeShowerSoapPhase.Washing);
                else if (soapPhase == HomeShowerSoapPhase.PutDown)
                {
                    SetSoapPhase(HomeShowerSoapPhase.Finished);
                    timeline.RequestFinish();
                    return;
                }
            }
            if (soapStartRendered) soapPhaseElapsed += Mathf.Max(0f, seconds);
            if (scrubSubmitted) return;

            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            washingLookHeld = WashingPointerAvailable &&
                ((mouse != null && mouse.rightButton.isPressed) || (pad != null && pad.leftTrigger.isPressed));
            bool modeChanged = washingLookHeld != previousWashingLook;
            previousWashingLook = washingLookHeld;
            bool held = (mouse != null && mouse.leftButton.isPressed) || (pad != null && pad.rightTrigger.isPressed);
            if (!held) pointerArmed = true;
            Vector2 mouseDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            Vector2 stick = pad != null ? pad.rightStick.ReadValue() : Vector2.zero;
            if (!washingLookHeld && !modeChanged && mouse != null &&
                (mouseDelta.sqrMagnitude > 0.25f || mouse.leftButton.wasPressedThisFrame))
                gamepadWashingPointer = false;
            if (pad != null && (stick.sqrMagnitude > 0.01f || pad.rightTrigger.wasPressedThisFrame))
                gamepadWashingPointer = true;
            Vector2 pointer = !gamepadWashingPointer && mouse != null ? mouse.position.ReadValue() : scrubPointer;
            Vector2 delta = !gamepadWashingPointer ? mouseDelta : Vector2.zero;
            if (gamepadWashingPointer && stick.sqrMagnitude > 0.01f && !washingLookHeld)
            {
                delta = stick * (600f * seconds);
                pointer = scrubPointer + delta;
            }
            pointer.x = Mathf.Clamp(pointer.x, 0f, Mathf.Max(0, Screen.width - 1));
            pointer.y = Mathf.Clamp(pointer.y, 0f, Mathf.Max(0, Screen.height - 1));
            if (modeChanged || washingLookHeld) delta = Vector2.zero;
            SetScrubInput(pointer, delta, held && pointerArmed && !washingLookHeld && !modeChanged);
        }

        /// <summary>E takes the known soap from its shelf; neither the mouse nor the camera chooses the item.</summary>
        public bool TryPickUpSoap()
        {
            if (!OwnsScene || !soapStarted || PauseMenuController.IsAnyPaused ||
                timeline.Phase != HomeShowerScenePhase.Wash || soapPhase != HomeShowerSoapPhase.SelectSoap ||
                washingFinishRequested) return false;
            SetSoapPhase(HomeShowerSoapPhase.Pickup);
            washingLookHeld = previousWashingLook = false;
            soapAffordance.SetHighlight(false);
            pointerArmed = false;
            return true;
        }

        /// <summary>Screen-space input only: target selection and actual contact remain in the presentation path.</summary>
        public void SetScrubInput(Vector2 screenPosition, Vector2 mouseDelta, bool held)
        {
            if (!OwnsScene || PauseMenuController.IsAnyPaused || !WashingPointerAvailable ||
                !float.IsFinite(screenPosition.x) || !float.IsFinite(screenPosition.y) ||
                !float.IsFinite(mouseDelta.x) || !float.IsFinite(mouseDelta.y)) return;
            scrubPointer = screenPosition;
            bool pressed = held && !washingLookHeld;
            if (pressed && !scrubHeld && soapPhase == HomeShowerSoapPhase.Washing) selectPointRequested = true;
            scrubHeld = pressed;
            scrubSubmitted = true;
        }

        private void PresentWashing(float seconds)
        {
            if (!soapStarted)
            {
                soapPose.Begin();
                soapStarted = true;
            }
            bool paused = PauseMenuController.IsAnyPaused;
            float step = paused ? 0f : seconds;
            switch (soapPhase)
            {
                case HomeShowerSoapPhase.SelectSoap:
                    soapAffordance.SetHighlight(SoapPromptVisible);
                    break;
                case HomeShowerSoapPhase.Pickup:
                    PresentSoapTransfer(true);
                    break;
                case HomeShowerSoapPhase.PutDown:
                    PresentSoapTransfer(false);
                    break;
                case HomeShowerSoapPhase.Washing:
                    if (selectPointRequested && !paused && !washingLookHeld) TrySelectWashPoint(scrubPointer);
                    PresentAutomaticWashing(step);
                    break;
            }
            scrubSubmitted = false;
            selectPointRequested = false;
        }

        /// <summary>Press selects visible skin; holding the button repeats a short right-hand washing stroke there.</summary>
        public bool TrySelectWashPoint(Vector2 screenPosition)
        {
            SelectionDiagnostic = $"point={screenPosition:F2}, phase={soapPhase}, look={washingLookHeld}, paused={PauseMenuController.IsAnyPaused}";
            if (!OwnsScene || !soapStarted || washingFinishRequested || PauseMenuController.IsAnyPaused ||
                washingLookHeld || soapPhase != HomeShowerSoapPhase.Washing || WashingCamera == null ||
                !float.IsFinite(screenPosition.x) || !float.IsFinite(screenPosition.y)) return false;
            ClearWashSelection();
            if (!WashingCamera.pixelRect.Contains(screenPosition))
            { SelectionDiagnostic += "; outside camera rectangle"; return false; }
            if (!soapPose.TryPickBody(WashingCamera.ScreenPointToRay(screenPosition), out HomeShowerSoapTarget target))
            { SelectionDiagnostic += "; no exposed skin hit"; return false; }
            SelectionDiagnostic += $"; skin={target.Region}/{target.SurfaceName}, point={target.WorldPoint:F5}, normal={target.WorldNormal:F4}";
            if (!soapPose.TryPrepareWashStroke(target, out strokeDirection, out strokeRadius))
            { SelectionDiagnostic += "; no stroke fits skin"; return false; }
            selectedWashTarget = target;
            SelectionDiagnostic += $"; strokeRadius={strokeRadius:F4}";
            return true;
        }

        private void ClearWashSelection()
        {
            selectedWashTarget = previousStrokeTarget = default;
            strokeClock = strokeRadius = 0f;
            strokeDirection = Vector3.zero;
        }

        private void PresentAutomaticWashing(float step)
        {
            HomeShowerSoapTarget target = default;
            bool active = HasSelectedWashPoint && step > 0f && scrubSubmitted && scrubHeld && !washingLookHeld;
            if (HasSelectedWashPoint)
            {
                // Phase advances only while the game is running. Every sample
                // is projected back onto that same currently posed skin mesh.
                if (active) strokeClock += Mathf.Min(step, 0.05f);
                Vector3 offset = strokeDirection * (strokeRadius * Mathf.Sin(strokeClock * WashStrokeFrequency * 2f * Mathf.PI));
                if (!soapPose.TrySampleWashStroke(selectedWashTarget, offset, out target)) active = false;
            }
            float commandedTravel = target.IsValid && previousStrokeTarget.IsValid &&
                target.Anchor == previousStrokeTarget.Anchor
                ? target.Anchor.TransformVector(target.LocalPoint - previousStrokeTarget.LocalPoint).magnitude : 0f;
            soapPose.ApplyScrub(target, active, step);
            float credited = washingProgress.Credit(soapPose.CurrentRegion,
                active ? commandedTravel : 0f, soapPose.ContactTravelMetres,
                active && soapPose.IsContacting, step);
            previousStrokeTarget = target;
            if (target.IsValid)
            {
                Vector3 projected = WashingCamera.WorldToScreenPoint(target.WorldPoint);
                soapAffordance.ShowContact(projected, projected.z > 0f,
                    washingProgress.GetRegionAmount(target.Region));
                if (credited > 0f) soapAffordance.EmitFoam(target, credited);
            }
            else soapAffordance.ClearContact();
            if (!washingProgress.Complete) return;
            timeline.NotifyWashingCompleted();
            washingFinishRequested = true;
            SetSoapPhase(HomeShowerSoapPhase.PutDown);
        }

        private void PresentSoapTransfer(bool pickup)
        {
            float duration = pickup ? HomeShowerSoapPose.PickupSeconds : HomeShowerSoapPose.PutDownSeconds;
            float normalized = soapStartRendered ? Mathf.Clamp01(soapPhaseElapsed / duration) : 0f;
            if (pickup) soapPose.ApplyPickup(normalized);
            else soapPose.ApplyPutDown(normalized);
            // Hitches cannot skip either visible endpoint or the actual contact completion.
            soapEndRendered = normalized >= 1f && soapPose.IsTransferComplete;
            soapStartRendered = true;
            if (!soapEndRendered && soapPhaseElapsed > duration + 12f)
            {
                GameLog.Warning("home", "shower_soap_transfer_blocked",
                    GameLog.Field("phase", soapPhase.ToString()),
                    GameLog.Field("hand_error", soapPose.HandError));
                CancelScene();
            }
        }

        private void SetSoapPhase(HomeShowerSoapPhase value)
        {
            soapPhase = value;
            soapPhaseElapsed = 0f;
            soapStartRendered = soapEndRendered = false;
            selectPointRequested = false;
            scrubHeld = scrubSubmitted = false;
            ClearWashSelection();
            soapAffordance.ClearContact();
        }

        private bool RequestWashingStop()
        {
            if (timeline.Phase != HomeShowerScenePhase.Wash || washingFinishRequested) return false;
            washingFinishRequested = true;
            washingLookHeld = false;
            soapAffordance.SetHighlight(false);
            if (!soapStarted || soapPhase == HomeShowerSoapPhase.SelectSoap) return timeline.RequestFinish();
            if (soapPhase == HomeShowerSoapPhase.Washing) SetSoapPhase(HomeShowerSoapPhase.PutDown);
            // A pickup already in progress first finishes its measured grip, then returns the soap.
            return true;
        }

        private void RestoreWashing()
        {
            soapPose?.End();
            ResetWashing();
        }
    }
}
