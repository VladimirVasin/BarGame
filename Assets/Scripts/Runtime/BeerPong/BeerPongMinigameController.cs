using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum BeerPongPresentationPhase
    {
        Aiming = 0,
        Charging,
        BallInFlight,
        ThrowResult,
        FinalResult
    }

    public enum BeerPongImpactKind
    {
        None = 0,
        Table,
        Rim
    }

    [DisallowMultipleComponent]
    public sealed class BeerPongMinigameController :
        MonoBehaviour,
        IBarMinigame
    {
        public const float FullChargeSeconds = 1.15f;
        public const float MinimumChargePower = 0.18f;
        public const float ThrowResultDuration = 1.15f;
        public const float AimDegreesPerSecond = 46f;
        public const float ImpactPulseDuration = 0.14f;
        private static readonly Rect pointerAimRect =
            new Rect(24f, 58f, 592f, 264f);

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private readonly List<Vector3> ballTrail =
            new List<Vector3>(10);

        private BeerPongMinigameView view;
        private IntoxicationHudView hud;
        private PlayerCameraFollow cameraFollow;
        private BeerPongSession session;
        private BeerPongPhysicsSimulation physics;
        private BeerPongThrowResult? lastThrow;
        private Vector3 lastBallPosition;
        private float chargeElapsed;
        private float resultElapsed;
        private float impactPulseRemaining;
        private int observedTableBounces;
        private int observedRimBounces;
        private int inputUnlockFrame;
        private bool completionRaised;
        private bool persistSessionProgress = true;
        private string minigameRunId = string.Empty;

        public bool IsOpen { get; private set; }
        public event Action Completed;
        public BeerPongPresentationPhase PresentationPhase
        {
            get;
            private set;
        }
        public BeerPongImpactKind ImpactKind { get; private set; }
        public float AimYawDegrees { get; private set; }
        public float AimPitchDegrees { get; private set; } = 38f;
        public float ChargePower { get; private set; } =
            MinimumChargePower;
        public float ImpactPulse =>
            Mathf.Clamp01(
                impactPulseRemaining / ImpactPulseDuration);
        public bool HasLastThrow => lastThrow.HasValue;
        public BeerPongThrowResult LastThrow =>
            lastThrow ?? default;
        public IReadOnlyList<Vector3> BallTrail => ballTrail;
        public BeerPongTableLayout TableLayout =>
            physics == null
                ? BeerPongTableLayout.Default
                : physics.Layout;
        public BeerPongBallSnapshot BallSnapshot =>
            physics == null
                ? default
                : physics.Snapshot;
        public Vector3 BallPosition
        {
            get
            {
                if (physics != null && physics.IsInFlight)
                {
                    return physics.InterpolatedPosition;
                }

                if (PresentationPhase ==
                    BeerPongPresentationPhase.ThrowResult)
                {
                    return lastBallPosition;
                }

                return TableLayout.ThrowOrigin;
            }
        }
        public int StandingCupMask => session == null
            ? BeerPongTableLayout.Default.AllCupsMask
            : session.StandingCupMask;
        public int CupsRemaining => session == null
            ? BeerPongTableLayout.CupCount
            : session.CupsRemaining;
        public int ThrowsCompleted => session == null
            ? 0
            : session.ThrowsCompleted;
        public int ThrowsRemaining => session == null
            ? BeerPongSession.ThrowLimit
            : session.ThrowsRemaining;
        public int TotalScore => session == null
            ? 0
            : session.TotalScore;
        public int IntoxicationLevel => session == null
            ? GameSessionState.IntoxicationLevel
            : session.Intoxication;
        public BeerPongSessionOutcome Outcome => session == null
            ? BeerPongSessionOutcome.InProgress
            : session.Outcome;
        public bool ReachedMaxIntoxication =>
            Outcome ==
                BeerPongSessionOutcome.MaxIntoxicationReached;

        public void Initialize(
            BeerPongMinigameView minigameView,
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

            var newSession = new BeerPongSession(
                persistSessionProgress
                    ? GameSessionState.IntoxicationLevel
                    : 0,
                persistSessionProgress
                    ? GameSessionState.LastAlcoholicDrink
                    : DrinkId.None,
                persistSessionProgress
                    ? GameSessionState.DrinksConsumed
                    : 0);
            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            session = newSession;
            physics = new BeerPongPhysicsSimulation();
            lastThrow = null;
            lastBallPosition = physics.Layout.ThrowOrigin;
            AimYawDegrees = 0f;
            AimPitchDegrees = 38f;
            ChargePower = MinimumChargePower;
            chargeElapsed = 0f;
            resultElapsed = 0f;
            impactPulseRemaining = 0f;
            observedTableBounces = 0;
            observedRimBounces = 0;
            inputUnlockFrame = Time.frameCount + 1;
            completionRaised = false;
            ImpactKind = BeerPongImpactKind.None;
            ballTrail.Clear();
            PresentationPhase = session.IsFinished
                ? BeerPongPresentationPhase.FinalResult
                : BeerPongPresentationPhase.Aiming;
            IsOpen = true;
            minigameRunId = Guid.NewGuid().ToString("N");
            LogOpened();
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool SetAim(float yawDegrees, float pitchDegrees)
        {
            if (!IsOpen ||
                (PresentationPhase !=
                     BeerPongPresentationPhase.Aiming &&
                 PresentationPhase !=
                     BeerPongPresentationPhase.Charging) ||
                !BeerPongMath.IsFinite(yawDegrees) ||
                !BeerPongMath.IsFinite(pitchDegrees))
            {
                return false;
            }

            AimYawDegrees = Mathf.Clamp(
                yawDegrees,
                BeerPongAim.MinimumYawDegrees,
                BeerPongAim.MaximumYawDegrees);
            AimPitchDegrees = Mathf.Clamp(
                pitchDegrees,
                BeerPongAim.MinimumPitchDegrees,
                BeerPongAim.MaximumPitchDegrees);
            return true;
        }

        public bool BeginCharging()
        {
            if (!IsOpen ||
                PresentationPhase !=
                BeerPongPresentationPhase.Aiming ||
                session == null ||
                !session.CanBeginThrow)
            {
                return false;
            }

            chargeElapsed = 0f;
            ChargePower = MinimumChargePower;
            PresentationPhase =
                BeerPongPresentationPhase.Charging;
            return true;
        }

        public bool ReleaseThrow()
        {
            if (!IsOpen ||
                PresentationPhase !=
                BeerPongPresentationPhase.Charging ||
                session == null ||
                physics == null)
            {
                return false;
            }

            int standingCupMask = session.BeginThrow();
            physics.LaunchFromAim(
                AimYawDegrees,
                AimPitchDegrees,
                ChargePower,
                standingCupMask);
            PresentationPhase =
                BeerPongPresentationPhase.BallInFlight;
            observedTableBounces = 0;
            observedRimBounces = 0;
            impactPulseRemaining = 0f;
            ImpactKind = BeerPongImpactKind.None;
            lastThrow = null;
            ballTrail.Clear();
            ballTrail.Add(physics.Layout.ThrowOrigin);
            GameLog.Info(
                "beer_pong",
                "throw_started",
                GameLog.Field(
                    "minigame_run_id",
                    minigameRunId),
                GameLog.Field(
                    "throw",
                    session.ThrowsCompleted + 1),
                GameLog.Field(
                    "aim_yaw_degrees",
                    AimYawDegrees),
                GameLog.Field(
                    "aim_pitch_degrees",
                    AimPitchDegrees),
                GameLog.Field(
                    "charge_power",
                    ChargePower),
                GameLog.Field(
                    "standing_cup_mask",
                    standingCupMask));
            RetroAudio.Play(RetroSfxId.BeerPongThrow);
            return true;
        }

        public bool ResolveFlightForTests(
            BeerPongFlightResult flightResult)
        {
            if (!IsOpen ||
                PresentationPhase !=
                    BeerPongPresentationPhase.BallInFlight ||
                !flightResult.IsTerminal)
            {
                return false;
            }

            ResolveFlight(flightResult);
            return true;
        }

        public bool ContinueAfterResult()
        {
            if (!IsOpen ||
                PresentationPhase !=
                    BeerPongPresentationPhase.ThrowResult ||
                session == null)
            {
                return false;
            }

            resultElapsed = 0f;
            impactPulseRemaining = 0f;
            ImpactKind = BeerPongImpactKind.None;
            ballTrail.Clear();
            physics?.Reset();
            if (session.IsFinished)
            {
                PresentationPhase =
                    BeerPongPresentationPhase.FinalResult;
                RaiseCompleted();
            }
            else
            {
                lastThrow = null;
                ChargePower = MinimumChargePower;
                PresentationPhase =
                    BeerPongPresentationPhase.Aiming;
            }

            inputUnlockFrame = Time.frameCount + 1;
            return true;
        }

        public bool CloseFinalResult()
        {
            if (!IsOpen ||
                PresentationPhase !=
                    BeerPongPresentationPhase.FinalResult)
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiConfirm);
            Close();
            return true;
        }

        public void AdvancePresentation(float unscaledDeltaTime)
        {
            if (!IsOpen)
            {
                return;
            }

            float deltaTime = Mathf.Max(0f, unscaledDeltaTime);
            impactPulseRemaining = Mathf.Max(
                0f,
                impactPulseRemaining - deltaTime);
            if (impactPulseRemaining <= 0f)
            {
                ImpactKind = BeerPongImpactKind.None;
            }

            if (PresentationPhase ==
                BeerPongPresentationPhase.Charging)
            {
                chargeElapsed += deltaTime;
                ChargePower = Mathf.Lerp(
                    MinimumChargePower,
                    1f,
                    Mathf.Clamp01(
                        chargeElapsed / FullChargeSeconds));
                return;
            }

            if (PresentationPhase ==
                    BeerPongPresentationPhase.BallInFlight &&
                physics != null)
            {
                int steps = physics.Advance(deltaTime);
                ObservePhysicsImpacts();
                if (steps > 0)
                {
                    AppendTrail(physics.InterpolatedPosition);
                }

                if (physics.TryGetResult(
                        out BeerPongFlightResult result))
                {
                    ResolveFlight(result);
                }

                return;
            }

            if (PresentationPhase ==
                BeerPongPresentationPhase.ThrowResult)
            {
                resultElapsed += deltaTime;
                if (resultElapsed >= ThrowResultDuration)
                {
                    ContinueAfterResult();
                }
            }
        }

        public void Cancel()
        {
            if (!IsOpen)
            {
                return;
            }

            if (!completionRaised)
            {
                LogCancelled("user");
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

            float deltaTime = Time.unscaledDeltaTime;
            AdvancePresentation(deltaTime);
            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (IsCancelPressed())
            {
                Cancel();
                return;
            }

            if (PresentationPhase ==
                BeerPongPresentationPhase.FinalResult)
            {
                if (IsConfirmPressed())
                {
                    CloseFinalResult();
                }

                return;
            }

            if (PresentationPhase ==
                BeerPongPresentationPhase.ThrowResult)
            {
                if (IsConfirmPressed())
                {
                    ContinueAfterResult();
                }

                return;
            }

            if (PresentationPhase !=
                    BeerPongPresentationPhase.Aiming &&
                PresentationPhase !=
                    BeerPongPresentationPhase.Charging)
            {
                return;
            }

            UpdatePointerAim();
            Vector2 aimInput = ReadAimInput();
            if (aimInput.sqrMagnitude > 0.001f)
            {
                SetAim(
                    AimYawDegrees +
                    aimInput.x *
                    AimDegreesPerSecond *
                    deltaTime,
                    AimPitchDegrees +
                    aimInput.y *
                    AimDegreesPerSecond *
                    deltaTime);
            }

            if (PresentationPhase ==
                    BeerPongPresentationPhase.Aiming &&
                IsThrowPressed())
            {
                BeginCharging();
            }
            else if (PresentationPhase ==
                         BeerPongPresentationPhase.Charging &&
                     IsThrowReleased())
            {
                ReleaseThrow();
            }
        }

        private void OnDisable()
        {
            LogInterruptedClose("disabled");
            Close();
        }

        private void OnDestroy()
        {
            LogInterruptedClose("destroyed");
            Close();
        }

        private void ResolveFlight(BeerPongFlightResult flightResult)
        {
            lastBallPosition = flightResult.FinalPosition;
            BeerPongThrowResult throwResult =
                session.CompleteThrow(flightResult);
            lastThrow = throwResult;
            if (persistSessionProgress &&
                throwResult.ConsumedDrink == DrinkId.LightBeer)
            {
                GameSessionState.CommitDrinkingProgress(
                    session.Intoxication,
                    session.LastAlcoholicDrink,
                    session.DrinksConsumed,
                    DrinkRules.GetStressRelief(
                        throwResult.ConsumedDrink));
            }

            LogThrowResolved(flightResult, throwResult);
            if (flightResult.WasSunk)
            {
                RetroAudio.Play(RetroSfxId.BeerPongSink);
            }

            physics.Reset();
            resultElapsed = 0f;
            PresentationPhase =
                BeerPongPresentationPhase.ThrowResult;
            inputUnlockFrame = Time.frameCount + 1;
        }

        private void ObservePhysicsImpacts()
        {
            BeerPongBallSnapshot snapshot = physics.Snapshot;
            if (snapshot.TableBounceCount >
                observedTableBounces)
            {
                observedTableBounces =
                    snapshot.TableBounceCount;
                ImpactKind = BeerPongImpactKind.Table;
                impactPulseRemaining = ImpactPulseDuration;
                RetroAudio.Play(
                    RetroSfxId.BeerPongBounce);
            }

            if (snapshot.RimBounceCount >
                observedRimBounces)
            {
                observedRimBounces =
                    snapshot.RimBounceCount;
                ImpactKind = BeerPongImpactKind.Rim;
                impactPulseRemaining = ImpactPulseDuration;
                RetroAudio.Play(RetroSfxId.BeerPongRim);
            }
        }

        private void AppendTrail(Vector3 position)
        {
            const int maximumTrailPoints = 9;
            if (ballTrail.Count == maximumTrailPoints)
            {
                ballTrail.RemoveAt(0);
            }

            ballTrail.Add(position);
        }

        private void UpdatePointerAim()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.delta.ReadValue().sqrMagnitude <= 0.001f &&
                !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();
            pointer.y = Screen.height - pointer.y;
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(
                    Screen.width,
                    Screen.height);
            Vector2 logical = canvas.ScreenToLogical(pointer);
            if (!pointerAimRect.Contains(logical))
            {
                return;
            }

            float normalizedX = Mathf.InverseLerp(
                pointerAimRect.xMin,
                pointerAimRect.xMax,
                logical.x);
            float normalizedY = Mathf.InverseLerp(
                pointerAimRect.yMin,
                pointerAimRect.yMax,
                logical.y);
            SetAim(
                Mathf.Lerp(
                    BeerPongAim.MinimumYawDegrees,
                    BeerPongAim.MaximumYawDegrees,
                    normalizedX),
                Mathf.Lerp(
                    BeerPongAim.MaximumPitchDegrees,
                    BeerPongAim.MinimumPitchDegrees,
                    normalizedY));
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            session?.CancelThrow();
            physics?.Reset();
            IsOpen = false;
            modalLock.Restore();
            session = null;
            physics = null;
            lastThrow = null;
            ballTrail.Clear();
            resultElapsed = 0f;
            chargeElapsed = 0f;
            impactPulseRemaining = 0f;
            ImpactKind = BeerPongImpactKind.None;
            minigameRunId = string.Empty;
        }

        private void RaiseCompleted()
        {
            if (completionRaised)
            {
                return;
            }

            completionRaised = true;
            LogCompleted();
            Completed?.Invoke();
        }

        private void LogOpened()
        {
            GameLog.Info(
                "beer_pong",
                "opened",
                GameLog.Field(
                    "minigame_run_id",
                    minigameRunId),
                GameLog.Field(
                    "bar_id",
                    GameSessionState.ActiveBarId),
                GameLog.Field(
                    "persist_progress",
                    persistSessionProgress),
                GameLog.Field(
                    "initial_intoxication",
                    session.Intoxication),
                GameLog.Field(
                    "last_drink",
                    session.LastAlcoholicDrink.ToString()),
                GameLog.Field(
                    "drinks_consumed",
                    session.DrinksConsumed),
                GameLog.Field(
                    "cups_remaining",
                    session.CupsRemaining),
                GameLog.Field(
                    "throws_remaining",
                    session.ThrowsRemaining));
        }

        private void LogThrowResolved(
            BeerPongFlightResult flight,
            BeerPongThrowResult result)
        {
            GameLog.Info(
                "beer_pong",
                "throw_resolved",
                GameLog.Field(
                    "minigame_run_id",
                    minigameRunId),
                GameLog.Field("throw", result.ThrowNumber),
                GameLog.Field(
                    "status",
                    flight.Status.ToString()),
                GameLog.Field("cup_index", result.CupIndex),
                GameLog.Field(
                    "miss_reason",
                    result.MissReason.ToString()),
                GameLog.Field(
                    "bank_shot",
                    result.WasBankShot),
                GameLog.Field(
                    "flight_seconds",
                    flight.FlightTime),
                GameLog.Field(
                    "table_bounces",
                    flight.TableBounceCount),
                GameLog.Field(
                    "rim_bounces",
                    flight.RimBounceCount),
                GameLog.Field(
                    "score_awarded",
                    result.ScoreAwarded),
                GameLog.Field(
                    "early_clear_bonus",
                    result.EarlyClearBonus),
                GameLog.Field(
                    "total_score",
                    result.TotalScore),
                GameLog.Field(
                    "intoxication_delta",
                    result.IntoxicationDelta),
                GameLog.Field(
                    "intoxication",
                    result.CurrentIntoxication),
                GameLog.Field(
                    "drinks_consumed",
                    result.DrinksConsumed),
                GameLog.Field(
                    "cups_remaining",
                    result.CupsRemaining),
                GameLog.Field(
                    "throws_remaining",
                    result.ThrowsRemaining),
                GameLog.Field(
                    "outcome",
                    result.SessionOutcome.ToString()));
        }

        private void LogCompleted()
        {
            GameLog.Info(
                "beer_pong",
                "completed",
                GameLog.Field(
                    "minigame_run_id",
                    minigameRunId),
                GameLog.Field(
                    "persist_progress",
                    persistSessionProgress),
                GameLog.Field(
                    "outcome",
                    session.Outcome.ToString()),
                GameLog.Field(
                    "throws_completed",
                    session.ThrowsCompleted),
                GameLog.Field(
                    "cups_remaining",
                    session.CupsRemaining),
                GameLog.Field(
                    "total_score",
                    session.TotalScore),
                GameLog.Field(
                    "intoxication",
                    session.Intoxication),
                GameLog.Field(
                    "drinks_consumed",
                    session.DrinksConsumed));
        }

        private void LogInterruptedClose(string reason)
        {
            if (IsOpen && !completionRaised)
            {
                LogCancelled(reason);
            }
        }

        private void LogCancelled(string reason)
        {
            GameLog.Info(
                "beer_pong",
                "cancelled",
                GameLog.Field(
                    "minigame_run_id",
                    minigameRunId),
                GameLog.Field(
                    "persist_progress",
                    persistSessionProgress),
                GameLog.Field("close_reason", reason),
                GameLog.Field(
                    "phase",
                    PresentationPhase.ToString()),
                GameLog.Field(
                    "throw_in_flight",
                    physics != null && physics.IsInFlight),
                GameLog.Field(
                    "throws_completed",
                    session?.ThrowsCompleted ?? 0),
                GameLog.Field(
                    "cups_remaining",
                    session?.CupsRemaining ??
                    BeerPongTableLayout.CupCount),
                GameLog.Field(
                    "total_score",
                    session?.TotalScore ?? 0),
                GameLog.Field(
                    "intoxication",
                    session?.Intoxication ??
                    GameSessionState.IntoxicationLevel));
        }

        private static Vector2 ReadAimInput()
        {
            Vector2 input = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                input.x =
                    (keyboard.dKey.isPressed ||
                     keyboard.rightArrowKey.isPressed
                        ? 1f
                        : 0f) -
                    (keyboard.aKey.isPressed ||
                     keyboard.leftArrowKey.isPressed
                        ? 1f
                        : 0f);
                input.y =
                    (keyboard.wKey.isPressed ||
                     keyboard.upArrowKey.isPressed
                        ? 1f
                        : 0f) -
                    (keyboard.sKey.isPressed ||
                     keyboard.downArrowKey.isPressed
                        ? 1f
                        : 0f);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > input.sqrMagnitude)
                {
                    input = stick;
                }
            }

            return Vector2.ClampMagnitude(input, 1f);
        }

        private static bool IsThrowPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.spaceKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame))
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null &&
                mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 pointer = mouse.position.ReadValue();
                pointer.y = Screen.height - pointer.y;
                RetroUiCanvas canvas =
                    RetroUiTheme.CalculateCanvas(
                        Screen.width,
                        Screen.height);
                if (pointerAimRect.Contains(
                        canvas.ScreenToLogical(pointer)))
                {
                    return true;
                }
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool IsThrowReleased()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.spaceKey.wasReleasedThisFrame ||
                 keyboard.eKey.wasReleasedThisFrame))
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null &&
                mouse.leftButton.wasReleasedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasReleasedThisFrame;
        }

        private static bool IsConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame ||
                 keyboard.spaceKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
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
