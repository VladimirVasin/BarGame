using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum TinctureMatchPresentationPhase
    {
        AwaitingInput = 0,
        InvalidSwap,
        Swapping,
        Clearing,
        Falling,
        Refilling,
        Reshuffling,
        FinalResult
    }

    public enum TinctureMatchRank
    {
        Miss = 0,
        Close,
        Good,
        Excellent,
        Perfect
    }

    [DisallowMultipleComponent]
    public sealed class TinctureMatchMinigameController :
        MonoBehaviour,
        IBarMinigame
    {
        public const float InvalidSwapDuration = 0.18f;
        public const float SwapDuration = 0.14f;
        public const float ClearDuration = 0.18f;
        public const float FallDuration = 0.20f;
        public const float RefillDuration = 0.16f;
        public const float ReshuffleDuration = 0.48f;
        public const float LogicalCellSize = 36f;

        public static readonly Rect BoardRect =
            new Rect(30f, 62f, 252f, 252f);

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private TinctureMatchMinigameView view;
        private IntoxicationHudView hud;
        private PlayerCameraFollow cameraFollow;
        private TinctureMatchSession session;
        private TinctureMatchMoveResult activeMove;
        private TinctureMatchMoveResult lastAcceptedMove;
        private TinctureMatchCell? selectedCell;
        private TinctureMatchCell? pointerDownCell;
        private Vector2Int lastStickDirection;
        private TinctureMatchPresentationPhase presentationPhase;
        private float phaseElapsed;
        private float stickRepeatRemaining;
        private int activeWaveIndex;
        private int inputUnlockFrame;
        private int currentIntoxication;
        private int currentDrinksConsumed;
        private int moonshineActivations;
        private int sessionOrdinal;
        private bool hasActiveMove;
        private bool persistSessionProgress = true;
        private bool completionRaised;
        private bool finishAfterPresentation;
        private string minigameRunId = string.Empty;

        public bool IsOpen { get; private set; }
        public event Action Completed;

        public TinctureMatchPresentationPhase PresentationPhase =>
            presentationPhase;
        public TinctureMatchSettings Settings => session?.Settings ??
            TinctureMatchSettings.Normal;
        public TinctureMatchBoard Board => session?.Board;
        public int Rows => Settings.Rows;
        public int Columns => Settings.Columns;
        public int Score => session?.Score ?? 0;
        public int MovesCompleted => session?.MovesCompleted ?? 0;
        public int MovesRemaining => session?.MovesRemaining ??
            Settings.MoveLimit;
        public int BestCascade => session?.BestCascade ?? 0;
        public int IntoxicationLevel => currentIntoxication;
        public int MoonshineActivations => moonshineActivations;
        public int CursorRow { get; private set; }
        public int CursorColumn { get; private set; }
        public bool HasSelection => selectedCell.HasValue;
        public int SelectedRow => selectedCell?.Row ?? -1;
        public int SelectedColumn => selectedCell?.Column ?? -1;
        public bool IsResolving =>
            presentationPhase !=
                TinctureMatchPresentationPhase.AwaitingInput &&
            presentationPhase !=
                TinctureMatchPresentationPhase.FinalResult;
        public float PhaseProgress =>
            GetPhaseDuration(presentationPhase) <= 0f
                ? 1f
                : Mathf.Clamp01(
                    phaseElapsed /
                    GetPhaseDuration(presentationPhase));
        public int ActiveCascadeDepth =>
            TryGetActiveWave(out TinctureMatchWaveResult wave)
                ? wave.Depth
                : 0;
        public bool IsMoonshineEffect =>
            hasActiveMove &&
            activeMove != null &&
            activeMove.ActivatedMoonshine;
        public bool IsMoonshineActivationWave =>
            presentationPhase ==
                TinctureMatchPresentationPhase.Clearing &&
            IsMoonshineEffect &&
            activeWaveIndex == 0;
        public bool WasLastMoveMoonshine =>
            lastAcceptedMove != null &&
            lastAcceptedMove.ActivatedMoonshine;
        public int LastMoveScore =>
            lastAcceptedMove?.ScoreAwarded ?? 0;
        public int ActiveFromRow => activeMove?.From.Row ?? -1;
        public int ActiveFromColumn =>
            activeMove?.From.Column ?? -1;
        public int ActiveToRow => activeMove?.To.Row ?? -1;
        public int ActiveToColumn =>
            activeMove?.To.Column ?? -1;
        public TinctureTileKind ActiveFromTile =>
            GetActiveSwapTile(true);
        public TinctureTileKind ActiveToTile =>
            GetActiveSwapTile(false);
        public TinctureMatchRank Rank =>
            CalculateRank(Score);

        public void Initialize(
            TinctureMatchMinigameView minigameView,
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

            int generatedSessionSeed =
                CreateSessionSeed(sessionOrdinal);
            var newSession =
                new TinctureMatchSession(generatedSessionSeed);
            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            sessionOrdinal++;
            session = newSession;
            currentIntoxication = persistSessionProgress
                ? GameSessionState.IntoxicationLevel
                : 0;
            currentDrinksConsumed = persistSessionProgress
                ? GameSessionState.DrinksConsumed
                : 0;
            moonshineActivations = 0;
            CursorRow = Rows / 2;
            CursorColumn = Columns / 2;
            selectedCell = null;
            pointerDownCell = null;
            activeMove = null;
            lastAcceptedMove = null;
            activeWaveIndex = 0;
            phaseElapsed = 0f;
            inputUnlockFrame = Time.frameCount + 1;
            lastStickDirection = Vector2Int.zero;
            stickRepeatRemaining = 0f;
            completionRaised = false;
            finishAfterPresentation = false;
            hasActiveMove = false;
            presentationPhase =
                TinctureMatchPresentationPhase.AwaitingInput;
            IsOpen = true;
            minigameRunId = Guid.NewGuid().ToString("N");
            LogOpened(
                generatedSessionSeed,
                sessionOrdinal - 1);
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public TinctureTileKind GetTile(int row, int column)
        {
            if (session == null)
            {
                return TinctureTileKind.Empty;
            }

            return session.GetTile(row, column);
        }

        public TinctureTileKind GetDisplayedTile(
            int row,
            int column)
        {
            if (!hasActiveMove || activeMove == null)
            {
                return GetTile(row, column);
            }

            TinctureMatchBoard board = GetPresentationBoard();
            return board == null
                ? GetTile(row, column)
                : board.GetTile(row, column);
        }

        public bool IsCellClearing(int row, int column)
        {
            if (presentationPhase !=
                    TinctureMatchPresentationPhase.Clearing ||
                !TryGetActiveWave(
                    out TinctureMatchWaveResult wave))
            {
                return false;
            }

            return wave.BoardBeforeClear.GetTile(row, column) !=
                   TinctureTileKind.Empty &&
                   wave.BoardAfterClear.GetTile(row, column) ==
                   TinctureTileKind.Empty;
        }

        public bool IsSwapAnimationCell(int row, int column)
        {
            if (presentationPhase !=
                    TinctureMatchPresentationPhase.Swapping ||
                activeMove == null)
            {
                return false;
            }

            return
                (activeMove.From.Row == row &&
                 activeMove.From.Column == column) ||
                (activeMove.To.Row == row &&
                 activeMove.To.Column == column);
        }

        public bool TryGetFallingTile(
            int row,
            int column,
            out TinctureTileKind kind,
            out float sourceRow)
        {
            kind = TinctureTileKind.Empty;
            sourceRow = row;
            if (presentationPhase !=
                    TinctureMatchPresentationPhase.Falling ||
                !IsCellInBounds(row, column) ||
                !TryGetActiveWave(
                    out TinctureMatchWaveResult wave))
            {
                return false;
            }

            kind = wave.BoardAfterGravity.GetTile(row, column);
            if (kind == TinctureTileKind.Empty)
            {
                return false;
            }

            int destinationRow = Rows - 1;
            for (int candidateRow = Rows - 1;
                 candidateRow >= 0;
                 candidateRow--)
            {
                if (wave.BoardAfterClear.GetTile(
                        candidateRow,
                        column) == TinctureTileKind.Empty)
                {
                    continue;
                }

                if (destinationRow == row)
                {
                    sourceRow = candidateRow;
                    return true;
                }

                destinationRow--;
            }

            kind = TinctureTileKind.Empty;
            return false;
        }

        public bool TryGetRefillingTile(
            int row,
            int column,
            out TinctureTileKind kind,
            out float sourceRow,
            out bool isNew)
        {
            kind = TinctureTileKind.Empty;
            sourceRow = row;
            isNew = false;
            if (presentationPhase !=
                    TinctureMatchPresentationPhase.Refilling ||
                !IsCellInBounds(row, column) ||
                !TryGetActiveWave(
                    out TinctureMatchWaveResult wave))
            {
                return false;
            }

            kind = wave.BoardAfterRefill.GetTile(row, column);
            if (kind == TinctureTileKind.Empty)
            {
                return false;
            }

            isNew = wave.BoardAfterGravity.GetTile(row, column) ==
                    TinctureTileKind.Empty;
            if (!isNew)
            {
                return true;
            }

            int emptyCount = 0;
            for (int candidateRow = 0;
                 candidateRow < Rows;
                 candidateRow++)
            {
                if (wave.BoardAfterGravity.GetTile(
                        candidateRow,
                        column) == TinctureTileKind.Empty)
                {
                    emptyCount++;
                }
            }

            sourceRow = row - emptyCount;
            return true;
        }

        public bool MoveCursor(int rowDelta, int columnDelta)
        {
            if (!CanAcceptBoardInput() ||
                (rowDelta == 0 && columnDelta == 0))
            {
                return false;
            }

            CursorRow = Mathf.Clamp(
                CursorRow + Math.Sign(rowDelta),
                0,
                Rows - 1);
            CursorColumn = Mathf.Clamp(
                CursorColumn + Math.Sign(columnDelta),
                0,
                Columns - 1);
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool SelectCurrentCell()
        {
            return SelectCell(CursorRow, CursorColumn);
        }

        public bool SelectCell(int row, int column)
        {
            if (!CanAcceptBoardInput() ||
                !IsCellInBounds(row, column))
            {
                return false;
            }

            CursorRow = row;
            CursorColumn = column;
            var cell = new TinctureMatchCell(row, column);
            if (!selectedCell.HasValue)
            {
                selectedCell = cell;
                RetroAudio.Play(RetroSfxId.UiConfirm);
                return true;
            }

            TinctureMatchCell selected = selectedCell.Value;
            if (selected.Equals(cell))
            {
                selectedCell = null;
                RetroAudio.Play(RetroSfxId.UiCancel);
                return true;
            }

            if (!AreAdjacent(selected, cell))
            {
                selectedCell = cell;
                RetroAudio.Play(RetroSfxId.UiMove);
                return true;
            }

            selectedCell = null;
            return TrySwap(
                selected.Row,
                selected.Column,
                cell.Row,
                cell.Column);
        }

        public bool TrySwap(
            int fromRow,
            int fromColumn,
            int toRow,
            int toColumn)
        {
            if (!CanAcceptBoardInput() ||
                !IsCellInBounds(fromRow, fromColumn) ||
                !IsCellInBounds(toRow, toColumn))
            {
                return false;
            }

            var from = new TinctureMatchCell(
                fromRow,
                fromColumn);
            var to = new TinctureMatchCell(toRow, toColumn);
            int previousIntoxication = currentIntoxication;
            bool accepted = session.TrySwap(
                from,
                to,
                out TinctureMatchMoveResult result);
            activeMove = result;
            hasActiveMove = result != null;
            activeWaveIndex = 0;
            phaseElapsed = 0f;
            selectedCell = null;

            if (!accepted)
            {
                presentationPhase =
                    TinctureMatchPresentationPhase.InvalidSwap;
                LogMoveResolved(
                    result,
                    previousIntoxication);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
            }

            lastAcceptedMove = result;
            CommitMoonshineIfNeeded(result);
            LogMoveResolved(
                result,
                previousIntoxication);
            presentationPhase =
                TinctureMatchPresentationPhase.Swapping;
            RetroAudio.Play(RetroSfxId.ShotSwap);
            return true;
        }

        public void AdvancePresentation(float unscaledDeltaTime)
        {
            if (!IsOpen ||
                presentationPhase ==
                    TinctureMatchPresentationPhase.AwaitingInput ||
                presentationPhase ==
                    TinctureMatchPresentationPhase.FinalResult)
            {
                return;
            }

            float remaining = Mathf.Max(0f, unscaledDeltaTime);
            while (remaining > 0f)
            {
                float duration = GetPhaseDuration(
                    presentationPhase);
                if (duration <= 0f)
                {
                    AdvancePhase();
                    continue;
                }

                float step = Mathf.Min(
                    remaining,
                    duration - phaseElapsed);
                phaseElapsed += step;
                remaining -= step;
                if (phaseElapsed + 0.00001f < duration)
                {
                    return;
                }

                phaseElapsed = 0f;
                AdvancePhase();
                if (presentationPhase ==
                        TinctureMatchPresentationPhase.AwaitingInput ||
                    presentationPhase ==
                        TinctureMatchPresentationPhase.FinalResult)
                {
                    return;
                }
            }
        }

        public bool CloseFinalResult()
        {
            if (!IsOpen ||
                presentationPhase !=
                    TinctureMatchPresentationPhase.FinalResult)
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiConfirm);
            Close();
            return true;
        }

        public void Cancel()
        {
            if (!IsOpen)
            {
                return;
            }

            if (!completionRaised && !WillCompleteOnClose())
            {
                LogCancelled("user");
            }

            RetroAudio.Play(RetroSfxId.UiCancel);
            Close();
        }

        public static TinctureMatchRank CalculateRank(int score)
        {
            if (score >= 1600)
            {
                return TinctureMatchRank.Perfect;
            }

            if (score >= 1200)
            {
                return TinctureMatchRank.Excellent;
            }

            if (score >= 900)
            {
                return TinctureMatchRank.Good;
            }

            return score >= 600
                ? TinctureMatchRank.Close
                : TinctureMatchRank.Miss;
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

            if (presentationPhase ==
                TinctureMatchPresentationPhase.FinalResult)
            {
                if (IsContinuePressed())
                {
                    CloseFinalResult();
                }
                else if (IsCancelPressed())
                {
                    Cancel();
                }

                return;
            }

            if (IsCancelPressed())
            {
                if (selectedCell.HasValue &&
                    presentationPhase ==
                        TinctureMatchPresentationPhase.AwaitingInput)
                {
                    selectedCell = null;
                    RetroAudio.Play(RetroSfxId.UiCancel);
                }
                else
                {
                    Cancel();
                }

                return;
            }

            if (!CanAcceptBoardInput())
            {
                return;
            }

            UpdatePointerInput();
            UpdateNavigationInput(deltaTime);
            if (IsSelectPressed())
            {
                SelectCurrentCell();
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

        private void AdvancePhase()
        {
            switch (presentationPhase)
            {
                case TinctureMatchPresentationPhase.InvalidSwap:
                    CompleteMovePresentation(false);
                    break;
                case TinctureMatchPresentationPhase.Swapping:
                    if (activeMove != null &&
                        activeMove.Waves.Count > 0)
                    {
                        EnterClearingPhase();
                    }
                    else
                    {
                        presentationPhase =
                            TinctureMatchPresentationPhase.Refilling;
                    }

                    break;
                case TinctureMatchPresentationPhase.Clearing:
                    presentationPhase =
                        TinctureMatchPresentationPhase.Falling;
                    break;
                case TinctureMatchPresentationPhase.Falling:
                    presentationPhase =
                        TinctureMatchPresentationPhase.Refilling;
                    break;
                case TinctureMatchPresentationPhase.Refilling:
                    activeWaveIndex++;
                    if (activeMove != null &&
                        activeWaveIndex < activeMove.Waves.Count)
                    {
                        EnterClearingPhase();
                    }
                    else if (activeMove != null &&
                             activeMove.WasReshuffled)
                    {
                        presentationPhase =
                            TinctureMatchPresentationPhase.Reshuffling;
                        RetroAudio.Play(RetroSfxId.Shake);
                    }
                    else
                    {
                        CompleteMovePresentation(true);
                    }

                    break;
                case TinctureMatchPresentationPhase.Reshuffling:
                    CompleteMovePresentation(true);
                    break;
            }
        }

        private void EnterClearingPhase()
        {
            presentationPhase =
                TinctureMatchPresentationPhase.Clearing;
            RetroAudio.Play(
                IsMoonshineActivationWave
                    ? RetroSfxId.MoonshineBurst
                    : RetroSfxId.ShotMatch);
        }

        private void CompleteMovePresentation(bool accepted)
        {
            bool shouldFinish =
                accepted &&
                (finishAfterPresentation ||
                 session == null ||
                 session.IsFinished);
            hasActiveMove = false;
            activeMove = null;
            activeWaveIndex = 0;
            phaseElapsed = 0f;
            if (shouldFinish)
            {
                presentationPhase =
                    TinctureMatchPresentationPhase.FinalResult;
                RetroAudio.Play(
                    Rank == TinctureMatchRank.Miss
                        ? RetroSfxId.Bad
                        : RetroSfxId.Good);
                RaiseCompleted();
                return;
            }

            presentationPhase =
                TinctureMatchPresentationPhase.AwaitingInput;
        }

        private void CommitMoonshineIfNeeded(
            TinctureMatchMoveResult result)
        {
            if (result == null || !result.ActivatedMoonshine)
            {
                return;
            }

            int previousIntoxication = currentIntoxication;
            int requestedGain =
                DrinkRules.GetIntoxicationGain(
                    DrinkId.Moonshine);
            moonshineActivations++;
            currentIntoxication = Mathf.Clamp(
                currentIntoxication +
                requestedGain,
                0,
                100);
            currentDrinksConsumed++;
            if (persistSessionProgress)
            {
                GameSessionState.CommitDrinkingProgress(
                    currentIntoxication,
                    DrinkId.Moonshine,
                    currentDrinksConsumed,
                    DrinkRules.GetStressRelief(
                        DrinkId.Moonshine));
            }

            GameLog.Info(
                "tincture_match",
                "moonshine_consumed",
                GameLog.Field(
                    "minigame_run_id",
                    minigameRunId),
                GameLog.Field(
                    "activation",
                    moonshineActivations),
                GameLog.Field(
                    "activated_kind",
                    result.ActivatedKind.ToString()),
                GameLog.Field(
                    "previous_intoxication",
                    previousIntoxication),
                GameLog.Field(
                    "requested_gain",
                    requestedGain),
                GameLog.Field(
                    "applied_gain",
                    currentIntoxication -
                    previousIntoxication),
                GameLog.Field(
                    "intoxication",
                    currentIntoxication),
                GameLog.Field(
                    "drinks_consumed",
                    currentDrinksConsumed),
                GameLog.Field(
                    "persist_progress",
                    persistSessionProgress));
            if (currentIntoxication < 100)
            {
                return;
            }

            finishAfterPresentation = true;
        }

        private TinctureMatchBoard GetPresentationBoard()
        {
            if (activeMove == null)
            {
                return null;
            }

            switch (presentationPhase)
            {
                case TinctureMatchPresentationPhase.InvalidSwap:
                    return activeMove.BoardBeforeSwap;
                case TinctureMatchPresentationPhase.Swapping:
                    return activeMove.BoardAfterSwap;
                case TinctureMatchPresentationPhase.Clearing:
                    return TryGetActiveWave(
                        out TinctureMatchWaveResult clearWave)
                        ? clearWave.BoardBeforeClear
                        : activeMove.BoardFinal;
                case TinctureMatchPresentationPhase.Falling:
                    return TryGetActiveWave(
                        out TinctureMatchWaveResult fallWave)
                        ? fallWave.BoardAfterGravity
                        : activeMove.BoardFinal;
                case TinctureMatchPresentationPhase.Refilling:
                    return TryGetActiveWave(
                        out TinctureMatchWaveResult refillWave)
                        ? refillWave.BoardAfterRefill
                        : activeMove.BoardFinal;
                default:
                    return activeMove.BoardFinal;
            }
        }

        private TinctureTileKind GetActiveSwapTile(bool from)
        {
            if (activeMove == null)
            {
                return TinctureTileKind.Empty;
            }

            TinctureMatchCell cell =
                from ? activeMove.From : activeMove.To;
            return activeMove.BoardBeforeSwap.GetTile(
                cell.Row,
                cell.Column);
        }

        private bool TryGetActiveWave(
            out TinctureMatchWaveResult wave)
        {
            if (hasActiveMove &&
                activeMove != null &&
                activeWaveIndex >= 0 &&
                activeWaveIndex < activeMove.Waves.Count)
            {
                wave = activeMove.Waves[activeWaveIndex];
                return true;
            }

            wave = null;
            return false;
        }

        private bool CanAcceptBoardInput()
        {
            return IsOpen &&
                   session != null &&
                   !session.IsFinished &&
                   !finishAfterPresentation &&
                   presentationPhase ==
                       TinctureMatchPresentationPhase.AwaitingInput;
        }

        private void UpdatePointerInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                pointerDownCell = TryGetPointerCell(
                    mouse,
                    out TinctureMatchCell pressed)
                    ? pressed
                    : (TinctureMatchCell?)null;
                if (pointerDownCell.HasValue)
                {
                    CursorRow = pointerDownCell.Value.Row;
                    CursorColumn = pointerDownCell.Value.Column;
                }
            }

            if (!mouse.leftButton.wasReleasedThisFrame ||
                !pointerDownCell.HasValue)
            {
                return;
            }

            TinctureMatchCell origin = pointerDownCell.Value;
            pointerDownCell = null;
            if (!TryGetPointerCell(
                    mouse,
                    out TinctureMatchCell destination))
            {
                return;
            }

            CursorRow = destination.Row;
            CursorColumn = destination.Column;
            if (!origin.Equals(destination) &&
                AreAdjacent(origin, destination))
            {
                selectedCell = null;
                TrySwap(
                    origin.Row,
                    origin.Column,
                    destination.Row,
                    destination.Column);
                return;
            }

            SelectCell(destination.Row, destination.Column);
        }

        private void UpdateNavigationInput(float deltaTime)
        {
            if (TryReadPressedDirection(out Vector2Int pressed))
            {
                MoveCursor(-pressed.y, pressed.x);
                return;
            }

            Gamepad gamepad = Gamepad.current;
            Vector2 stick = gamepad == null
                ? Vector2.zero
                : gamepad.leftStick.ReadValue();
            Vector2Int direction = GetDominantDirection(stick);
            if (direction == Vector2Int.zero)
            {
                lastStickDirection = Vector2Int.zero;
                stickRepeatRemaining = 0f;
                return;
            }

            stickRepeatRemaining -= Mathf.Max(0f, deltaTime);
            if (direction != lastStickDirection ||
                stickRepeatRemaining <= 0f)
            {
                MoveCursor(-direction.y, direction.x);
                stickRepeatRemaining =
                    lastStickDirection == Vector2Int.zero
                        ? 0.28f
                        : 0.11f;
                lastStickDirection = direction;
            }
        }

        private static bool TryReadPressedDirection(
            out Vector2Int direction)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.wasPressedThisFrame ||
                    keyboard.leftArrowKey.wasPressedThisFrame)
                {
                    direction = Vector2Int.left;
                    return true;
                }

                if (keyboard.dKey.wasPressedThisFrame ||
                    keyboard.rightArrowKey.wasPressedThisFrame)
                {
                    direction = Vector2Int.right;
                    return true;
                }

                if (keyboard.wKey.wasPressedThisFrame ||
                    keyboard.upArrowKey.wasPressedThisFrame)
                {
                    direction = Vector2Int.up;
                    return true;
                }

                if (keyboard.sKey.wasPressedThisFrame ||
                    keyboard.downArrowKey.wasPressedThisFrame)
                {
                    direction = Vector2Int.down;
                    return true;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.left.wasPressedThisFrame)
                {
                    direction = Vector2Int.left;
                    return true;
                }

                if (gamepad.dpad.right.wasPressedThisFrame)
                {
                    direction = Vector2Int.right;
                    return true;
                }

                if (gamepad.dpad.up.wasPressedThisFrame)
                {
                    direction = Vector2Int.up;
                    return true;
                }

                if (gamepad.dpad.down.wasPressedThisFrame)
                {
                    direction = Vector2Int.down;
                    return true;
                }
            }

            direction = Vector2Int.zero;
            return false;
        }

        private static Vector2Int GetDominantDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.36f)
            {
                return Vector2Int.zero;
            }

            return Mathf.Abs(input.x) >= Mathf.Abs(input.y)
                ? new Vector2Int(input.x > 0f ? 1 : -1, 0)
                : new Vector2Int(0, input.y > 0f ? 1 : -1);
        }

        private static bool IsSelectPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.spaceKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool IsContinuePressed()
        {
            return IsSelectPressed();
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

        private static bool TryGetPointerCell(
            Mouse mouse,
            out TinctureMatchCell cell)
        {
            Vector2 pointer = mouse.position.ReadValue();
            pointer.y = Screen.height - pointer.y;
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(
                    Screen.width,
                    Screen.height);
            Vector2 logical = canvas.ScreenToLogical(pointer);
            if (!BoardRect.Contains(logical))
            {
                cell = default;
                return false;
            }

            int column = Mathf.Clamp(
                Mathf.FloorToInt(
                    (logical.x - BoardRect.x) /
                    LogicalCellSize),
                0,
                TinctureMatchSettings.Normal.Columns - 1);
            int row = Mathf.Clamp(
                Mathf.FloorToInt(
                    (logical.y - BoardRect.y) /
                    LogicalCellSize),
                0,
                TinctureMatchSettings.Normal.Rows - 1);
            cell = new TinctureMatchCell(row, column);
            return true;
        }

        private bool IsCellInBounds(int row, int column)
        {
            return row >= 0 &&
                   row < Rows &&
                   column >= 0 &&
                   column < Columns;
        }

        private static bool AreAdjacent(
            TinctureMatchCell first,
            TinctureMatchCell second)
        {
            return Math.Abs(first.Row - second.Row) +
                   Math.Abs(first.Column - second.Column) == 1;
        }

        private static float GetPhaseDuration(
            TinctureMatchPresentationPhase phase)
        {
            switch (phase)
            {
                case TinctureMatchPresentationPhase.InvalidSwap:
                    return InvalidSwapDuration;
                case TinctureMatchPresentationPhase.Swapping:
                    return SwapDuration;
                case TinctureMatchPresentationPhase.Clearing:
                    return ClearDuration;
                case TinctureMatchPresentationPhase.Falling:
                    return FallDuration;
                case TinctureMatchPresentationPhase.Refilling:
                    return RefillDuration;
                case TinctureMatchPresentationPhase.Reshuffling:
                    return ReshuffleDuration;
                default:
                    return 0f;
            }
        }

        private static int CreateSessionSeed(int sessionOrdinal)
        {
            unchecked
            {
                uint hash =
                    (uint)GameSessionState.CitySeed ^
                    0x544d3347u;
                string barId = GameSessionState.ActiveBarId ?? string.Empty;
                for (int index = 0; index < barId.Length; index++)
                {
                    hash ^= barId[index];
                    hash *= 16777619u;
                }

                hash ^=
                    (uint)sessionOrdinal *
                    0x9e3779b9u;
                hash *= 16777619u;
                return (int)hash;
            }
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            if (finishAfterPresentation ||
                (session != null && session.IsFinished))
            {
                RaiseCompleted();
            }

            IsOpen = false;
            modalLock.Restore();
            session = null;
            activeMove = null;
            lastAcceptedMove = null;
            selectedCell = null;
            pointerDownCell = null;
            hasActiveMove = false;
            phaseElapsed = 0f;
            activeWaveIndex = 0;
            finishAfterPresentation = false;
            lastStickDirection = Vector2Int.zero;
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

        private void LogOpened(
            int generatedSessionSeed,
            int generatedSessionOrdinal)
        {
            GameLog.Info(
                "tincture_match",
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
                    "session_seed",
                    generatedSessionSeed),
                GameLog.Field(
                    "session_ordinal",
                    generatedSessionOrdinal),
                GameLog.Field("rows", session.Settings.Rows),
                GameLog.Field(
                    "columns",
                    session.Settings.Columns),
                GameLog.Field(
                    "move_limit",
                    session.Settings.MoveLimit),
                GameLog.Field(
                    "minimum_legal_swaps",
                    session.Settings.MinimumInitialLegalSwaps),
                GameLog.Field(
                    "initial_board_hash",
                    session.Board.GetHashCode()),
                GameLog.Field(
                    "initial_intoxication",
                    currentIntoxication),
                GameLog.Field(
                    "drinks_consumed",
                    currentDrinksConsumed));
        }

        private void LogMoveResolved(
            TinctureMatchMoveResult result,
            int previousIntoxication)
        {
            if (result == null)
            {
                return;
            }

            int clearedTileCount = 0;
            for (int index = 0;
                 index < result.Waves.Count;
                 index++)
            {
                clearedTileCount +=
                    result.Waves[index].ClearedTileCount;
            }

            GameLog.Info(
                "tincture_match",
                "move_resolved",
                GameLog.Field(
                    "minigame_run_id",
                    minigameRunId),
                GameLog.Field(
                    "from",
                    result.From.ToString()),
                GameLog.Field("to", result.To.ToString()),
                GameLog.Field("accepted", result.Accepted),
                GameLog.Field(
                    "rejection_reason",
                    result.RejectionReason.ToString()),
                GameLog.Field(
                    "move",
                    result.MoveNumber),
                GameLog.Field(
                    "moves_remaining",
                    result.MovesRemaining),
                GameLog.Field(
                    "score_awarded",
                    result.ScoreAwarded),
                GameLog.Field(
                    "total_score",
                    result.TotalScore),
                GameLog.Field(
                    "cascade_depth",
                    result.CascadeDepth),
                GameLog.Field(
                    "cleared_tiles",
                    clearedTileCount),
                GameLog.Field(
                    "moonshine_activated",
                    result.ActivatedMoonshine),
                GameLog.Field(
                    "activated_kind",
                    result.ActivatedKind.ToString()),
                GameLog.Field(
                    "moonshine_created",
                    result.CreatedMoonshine),
                GameLog.Field(
                    "reshuffled",
                    result.WasReshuffled),
                GameLog.Field(
                    "board_hash",
                    result.BoardFinal.GetHashCode()),
                GameLog.Field(
                    "previous_intoxication",
                    previousIntoxication),
                GameLog.Field(
                    "intoxication",
                    currentIntoxication));
        }

        private void LogCompleted()
        {
            GameLog.Info(
                "tincture_match",
                "completed",
                GameLog.Field(
                    "minigame_run_id",
                    minigameRunId),
                GameLog.Field(
                    "persist_progress",
                    persistSessionProgress),
                GameLog.Field("score", session.Score),
                GameLog.Field(
                    "rank",
                    Rank.ToString()),
                GameLog.Field(
                    "moves_completed",
                    session.MovesCompleted),
                GameLog.Field(
                    "best_cascade",
                    session.BestCascade),
                GameLog.Field(
                    "moonshine_activations",
                    moonshineActivations),
                GameLog.Field(
                    "intoxication",
                    currentIntoxication),
                GameLog.Field(
                    "drinks_consumed",
                    currentDrinksConsumed));
        }

        private void LogInterruptedClose(string reason)
        {
            if (IsOpen &&
                !completionRaised &&
                !WillCompleteOnClose())
            {
                LogCancelled(reason);
            }
        }

        private bool WillCompleteOnClose()
        {
            return finishAfterPresentation ||
                   (session != null && session.IsFinished);
        }

        private void LogCancelled(string reason)
        {
            GameLog.Info(
                "tincture_match",
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
                    presentationPhase.ToString()),
                GameLog.Field(
                    "moves_completed",
                    session?.MovesCompleted ?? 0),
                GameLog.Field(
                    "score",
                    session?.Score ?? 0),
                GameLog.Field(
                    "best_cascade",
                    session?.BestCascade ?? 0),
                GameLog.Field(
                    "intoxication",
                    currentIntoxication),
                GameLog.Field(
                    "moonshine_activations",
                    moonshineActivations));
        }
    }
}
