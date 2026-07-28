using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class SplitTheGMinigameController :
        MonoBehaviour,
        IBarMinigame
    {
        public const float WastedDurationSeconds = 45f;
        public const float GulpRepeatSeconds = 0.34f;

        private static readonly Rect PointerDrinkRect =
            new Rect(116f, 48f, 408f, 270f);

        private enum DrinkInputSource
        {
            None = 0,
            Programmatic,
            Keyboard,
            Mouse,
            Gamepad
        }

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private SplitTheGMinigameView view;
        private IntoxicationHudView hud;
        private PlayerCameraFollow cameraFollow;
        private SplitTheGSession session;
        private DrinkInputSource activeInputSource;
        private SplitTheGPhase observedPhase;
        private int currentIntoxication;
        private int currentDrinksConsumed;
        private int committedAttempts;
        private int inputUnlockFrame;
        private float gulpElapsed;
        private bool persistSessionProgress = true;
        private bool completionRaised;
        private bool wastedApplied;
        private bool completeAfterSettling;
        private bool awaitingFreshRelease;

        public bool IsOpen { get; private set; }
        public event Action Completed;

        public SplitTheGPhase Phase => session == null
            ? SplitTheGPhase.Countdown
            : session.Phase;
        public SplitTheGSettings Settings => session?.Settings ??
            SplitTheGSettings.Normal;
        public float RemainingLevel => session == null
            ? 1f
            : (float)session.RemainingLevel;
        public float TargetLevel => (float)Settings.TargetLevel;
        public float DrinkElapsed => session == null
            ? 0f
            : (float)session.DrinkElapsed;
        public float CountdownRemaining => session == null ||
            session.Phase != SplitTheGPhase.Countdown
                ? 0f
                : Mathf.Max(
                    0f,
                    (float)(
                        session.Settings.CountdownTime -
                        session.PhaseElapsed));
        public float SettlingProgress => session == null ||
            session.Phase != SplitTheGPhase.Settling
                ? 0f
                : session.Settings.SettlingTime <= 0d
                    ? 1f
                    : Mathf.Clamp01(
                        (float)(
                            session.PhaseElapsed /
                            session.Settings.SettlingTime));
        public int AttemptsCompleted => session?.AttemptsCompleted ?? 0;
        public int CurrentAttemptNumber => session?.CurrentAttemptNumber ?? 1;
        public int MaximumAttempts => Settings.MaximumAttempts;
        public int BestScore => session?.BestScore ?? 0;
        public int IntoxicationLevel => currentIntoxication;
        public bool HasLastResult =>
            session != null && session.HasLastResult;
        public SplitTheGAttemptResult LastResult =>
            session != null && session.HasLastResult
                ? session.LastResult
                : default;
        public SplitTheGAttemptResult BestResult =>
            session != null && session.HasBestResult
                ? session.BestResult
                : default;
        public bool CanRetry =>
            session != null &&
            session.CanRetry &&
            !completeAfterSettling;
        public bool CanCompleteEarly =>
            session != null && session.CanCompleteEarly;
        public bool IsExactLevelHidden =>
            Phase == SplitTheGPhase.Drinking ||
            Phase == SplitTheGPhase.Settling;
        public bool IsAwaitingFreshPress => awaitingFreshRelease;

        public void Initialize(
            SplitTheGMinigameView minigameView,
            IntoxicationHudView intoxicationHud,
            PlayerRuntime player,
            PlayerCameraFollow follow,
            bool persistProgress = true)
        {
            view = minigameView;
            hud = intoxicationHud;
            cameraFollow = follow;
            persistSessionProgress = persistProgress;
            view?.Initialize(this);
        }

        public bool Open(PlayerInteractor interactor)
        {
            if (IsOpen ||
                interactor == null ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            var newSession =
                new SplitTheGSession(SplitTheGSettings.Normal);
            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            session = newSession;
            currentIntoxication = persistSessionProgress
                ? GameSessionState.IntoxicationLevel
                : 0;
            currentDrinksConsumed = persistSessionProgress
                ? GameSessionState.DrinksConsumed
                : 0;
            committedAttempts = 0;
            inputUnlockFrame = Time.frameCount + 1;
            gulpElapsed = 0f;
            completionRaised = false;
            wastedApplied = false;
            completeAfterSettling = false;
            awaitingFreshRelease = false;
            activeInputSource = DrinkInputSource.None;
            observedPhase = session.Phase;
            IsOpen = true;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool BeginDrink()
        {
            return BeginDrink(DrinkInputSource.Programmatic);
        }

        public bool ReleaseDrink()
        {
            if (!IsOpen ||
                session == null ||
                session.Phase != SplitTheGPhase.Drinking)
            {
                return false;
            }

            session.ReleaseDrink();
            activeInputSource = DrinkInputSource.None;
            gulpElapsed = 0f;
            CommitLatestAttempt();
            ObservePhaseChange(
                SplitTheGPhase.Drinking,
                true);
            RetroAudio.Play(RetroSfxId.Clink);
            return true;
        }

        public bool Retry()
        {
            if (!CanRetry)
            {
                return false;
            }

            session.Retry();
            activeInputSource = DrinkInputSource.None;
            awaitingFreshRelease = false;
            observedPhase = session.Phase;
            inputUnlockFrame = Time.frameCount + 1;
            gulpElapsed = 0f;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool CompleteSession()
        {
            if (!CanCompleteEarly)
            {
                return false;
            }

            session.CompleteEarly();
            observedPhase = session.Phase;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            RaiseCompleted();
            return true;
        }

        public bool CloseFinalResult()
        {
            if (!IsOpen ||
                session == null ||
                session.Phase != SplitTheGPhase.FinalResult)
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiConfirm);
            Close();
            return true;
        }

        public void AdvancePresentation(float unscaledDeltaTime)
        {
            if (!IsOpen || session == null)
            {
                return;
            }

            float deltaTime = Mathf.Max(0f, unscaledDeltaTime);
            SplitTheGPhase previousPhase = session.Phase;
            int previousAttempts = session.AttemptsCompleted;
            session.Advance(deltaTime);
            bool finishedAttempt =
                session.AttemptsCompleted > previousAttempts;

            if (previousPhase == SplitTheGPhase.Drinking)
            {
                AdvanceGulpAudio(deltaTime);
            }

            if (finishedAttempt ||
                session.AttemptsCompleted > committedAttempts)
            {
                CommitLatestAttempt();
            }

            ObservePhaseChange(
                previousPhase,
                finishedAttempt);
        }

        public void Cancel()
        {
            if (!IsOpen)
            {
                return;
            }

            if (session != null &&
                session.Phase == SplitTheGPhase.Drinking)
            {
                ReleaseDrink();
            }

            RetroAudio.Play(RetroSfxId.UiCancel);
            Close();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            AdvancePresentation(Time.unscaledDeltaTime);
            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (IsCancelPressed())
            {
                Cancel();
                return;
            }

            switch (Phase)
            {
                case SplitTheGPhase.Armed:
                    UpdateArmedInput();
                    break;
                case SplitTheGPhase.Drinking:
                    if (IsInitiatingInputReleased())
                    {
                        ReleaseDrink();
                    }

                    break;
                case SplitTheGPhase.AttemptResult:
                    if (IsRetryPressed())
                    {
                        Retry();
                    }
                    else if (IsContinuePressed())
                    {
                        CompleteSession();
                    }

                    break;
                case SplitTheGPhase.FinalResult:
                    if (IsContinuePressed() || IsRetryPressed())
                    {
                        CloseFinalResult();
                    }

                    break;
            }
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy()
        {
            Close();
        }

        private bool BeginDrink(DrinkInputSource inputSource)
        {
            if (!IsOpen ||
                session == null ||
                !session.CanBeginDrink)
            {
                return false;
            }

            session.BeginDrink();
            activeInputSource = inputSource;
            awaitingFreshRelease = false;
            observedPhase = session.Phase;
            gulpElapsed = 0f;
            RetroAudio.Play(RetroSfxId.DrinkGulp);
            return true;
        }

        private void UpdateArmedInput()
        {
            if (awaitingFreshRelease)
            {
                if (!IsAnyDrinkInputHeld())
                {
                    awaitingFreshRelease = false;
                    inputUnlockFrame = Time.frameCount;
                }

                return;
            }

            if (TryReadDrinkPress(out DrinkInputSource source))
            {
                BeginDrink(source);
            }
        }

        private void AdvanceGulpAudio(float deltaTime)
        {
            if (session == null ||
                session.Phase != SplitTheGPhase.Drinking)
            {
                gulpElapsed = 0f;
                return;
            }

            gulpElapsed += deltaTime;
            if (gulpElapsed < GulpRepeatSeconds)
            {
                return;
            }

            gulpElapsed %= GulpRepeatSeconds;
            RetroAudio.Play(RetroSfxId.DrinkGulp);
        }

        private void CommitLatestAttempt()
        {
            if (session == null ||
                !session.HasLastResult ||
                session.AttemptsCompleted <= committedAttempts)
            {
                return;
            }

            committedAttempts = session.AttemptsCompleted;
            double consumedFraction =
                session.LastResult.ConsumedFraction;
            if (consumedFraction <= 0.000001d)
            {
                return;
            }

            int fullGlassGain =
                DrinkRules.GetIntoxicationGain(DrinkId.DarkBeer);
            int intoxicationGain = (int)Math.Round(
                fullGlassGain * consumedFraction,
                MidpointRounding.AwayFromZero);
            currentIntoxication = Mathf.Clamp(
                currentIntoxication + intoxicationGain,
                0,
                100);
            currentDrinksConsumed++;
            if (persistSessionProgress)
            {
                GameSessionState.UpdateDrinkingProgress(
                    currentIntoxication,
                    DrinkId.DarkBeer,
                    currentDrinksConsumed);
            }

            if (currentIntoxication < 100)
            {
                return;
            }

            completeAfterSettling = true;
            if (!wastedApplied && persistSessionProgress)
            {
                GameSessionState.ApplyWasted(
                    WastedDurationSeconds);
                wastedApplied = true;
            }
        }

        private void ObservePhaseChange(
            SplitTheGPhase previousPhase,
            bool attemptFinished = false)
        {
            if (session == null)
            {
                return;
            }

            SplitTheGPhase currentPhase = session.Phase;
            if (currentPhase == observedPhase &&
                currentPhase == previousPhase)
            {
                return;
            }

            if (currentPhase == SplitTheGPhase.Armed &&
                previousPhase != SplitTheGPhase.Armed)
            {
                awaitingFreshRelease = IsAnyDrinkInputHeld();
                inputUnlockFrame = Time.frameCount;
            }

            if (previousPhase == SplitTheGPhase.Drinking &&
                currentPhase != SplitTheGPhase.Drinking)
            {
                activeInputSource = DrinkInputSource.None;
                gulpElapsed = 0f;
            }

            if ((previousPhase == SplitTheGPhase.Settling ||
                 attemptFinished) &&
                (currentPhase == SplitTheGPhase.AttemptResult ||
                 currentPhase == SplitTheGPhase.FinalResult))
            {
                PlayResultSound();
            }

            if (completeAfterSettling &&
                currentPhase == SplitTheGPhase.AttemptResult)
            {
                session.CompleteEarly();
                currentPhase = session.Phase;
            }

            if (currentPhase == SplitTheGPhase.FinalResult)
            {
                RaiseCompleted();
            }

            observedPhase = currentPhase;
        }

        private void PlayResultSound()
        {
            if (session == null || !session.HasLastResult)
            {
                return;
            }

            SplitTheGResultBand band = session.LastResult.Band;
            RetroAudio.Play(
                band == SplitTheGResultBand.Perfect ||
                band == SplitTheGResultBand.Excellent ||
                band == SplitTheGResultBand.Good
                    ? RetroSfxId.Good
                    : RetroSfxId.Bad);
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            modalLock.Restore();
            session = null;
            activeInputSource = DrinkInputSource.None;
            gulpElapsed = 0f;
            awaitingFreshRelease = false;
            completeAfterSettling = false;
        }

        private void RaiseCompleted()
        {
            if (completionRaised)
            {
                return;
            }

            completionRaised = true;
            Completed?.Invoke();
        }

        private bool IsInitiatingInputReleased()
        {
            switch (activeInputSource)
            {
                case DrinkInputSource.Keyboard:
                {
                    Keyboard keyboard = Keyboard.current;
                    return keyboard == null ||
                           !keyboard.spaceKey.isPressed;
                }
                case DrinkInputSource.Mouse:
                {
                    Mouse mouse = Mouse.current;
                    return mouse == null ||
                           !mouse.leftButton.isPressed;
                }
                case DrinkInputSource.Gamepad:
                {
                    Gamepad gamepad = Gamepad.current;
                    return gamepad == null ||
                           !gamepad.buttonSouth.isPressed;
                }
                default:
                    return false;
            }
        }

        private static bool TryReadDrinkPress(
            out DrinkInputSource source)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.spaceKey.wasPressedThisFrame)
            {
                source = DrinkInputSource.Keyboard;
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null &&
                mouse.leftButton.wasPressedThisFrame &&
                IsPointerInDrinkRect(mouse))
            {
                source = DrinkInputSource.Mouse;
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null &&
                gamepad.buttonSouth.wasPressedThisFrame)
            {
                source = DrinkInputSource.Gamepad;
                return true;
            }

            source = DrinkInputSource.None;
            return false;
        }

        private static bool IsAnyDrinkInputHeld()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.spaceKey.isPressed)
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null &&
                mouse.leftButton.isPressed)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.isPressed;
        }

        private static bool IsPointerInDrinkRect(Mouse mouse)
        {
            Vector2 pointer = mouse.position.ReadValue();
            pointer.y = Screen.height - pointer.y;
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(
                    Screen.width,
                    Screen.height);
            return PointerDrinkRect.Contains(
                canvas.ScreenToLogical(pointer));
        }

        private static bool IsRetryPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.spaceKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool IsContinuePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonNorth.wasPressedThisFrame;
        }

        private static bool IsCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }
    }
}
