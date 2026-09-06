using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// The two park boards, once somebody actually sits down at one.
    ///
    /// Until then this does nothing: the set is four static batches and
    /// two men with their heads in their hands. The moment the hero
    /// takes the free plank at either table, that table's batches are
    /// swapped for live men, the camera drops to his own eyes over the
    /// stone, and the old man opposite — who has White at his own board
    /// and therefore always opens — plays a move.
    ///
    /// Everything about the position lives in the pure match underneath
    /// (`ChessMatch`, `DraughtsMatch`). This owns only what a scene has
    /// to own: the camera, the pointer, the wait while the other man
    /// thinks, and the two boards' persistence across getting up and
    /// coming back — a game left in the middle is still in the middle
    /// when the hero returns to it.
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public sealed class CityBoardGameController : MonoBehaviour
    {
        public const string RootName = "Park Board Games";

        /// <summary>How long the camera takes to fall to the board and
        /// to climb back out of it.</summary>
        public const float CameraBlendSeconds = 0.45f;

        /// <summary>The pause before the other man answers. He is old
        /// and he is not in a hurry, and an instant reply would read as
        /// a machine rather than as a man.</summary>
        public const float MinimumThinkSeconds = 0.75f;
        public const float MaximumThinkSeconds = 2.1f;

        /// <summary>Every board line the two of them have. Short, aimed
        /// at the hero rather than at each other — which is the one
        /// thing the quarrel repertoire never is.</summary>
        public const int LinesPerCue = 2;

        /// <summary>
        /// The shortest gap between two things the man opposite says.
        /// A little longer than a bubble stays up, so lines queue behind
        /// each other instead of cutting each other off.
        /// </summary>
        public const float SpeechCooldownSeconds = 3f;

        /// <summary>And how long after the result he offers another
        /// game — long enough that the two lines read as two
        /// sentences rather than as one wall of text.</summary>
        public const float AgainDelaySeconds = 4f;

        private enum CameraPhase
        {
            None = 0,
            Entering = 1,
            Playing = 2,
            Leaving = 3
        }

        /// <summary>
        /// Everything the board has to tell the player, said by the man
        /// across it. There is no panel: whose move it is, that a man
        /// is in check, that taking is compulsory and how the game
        /// ended all arrive as things he says, because a board in a
        /// park does not have a heads-up display and the old man was
        /// always going to have an opinion anyway.
        /// </summary>
        private enum BoardCue
        {
            Greet = 0,
            YourMove = 1,
            Check = 2,
            Crown = 3,
            Take = 4,
            MustTake = 5,
            Win = 6,
            Lose = 7,
            Draw = 8,
            Again = 9
        }

        private readonly List<Board> boards = new List<Board>(2);
        private readonly List<BoardGameAction> selectionActions =
            new List<BoardGameAction>(32);
        private readonly BoardGameTurn turn = new BoardGameTurn();

        private PlayerRuntime player;
        private Camera boardCamera;
        private PlayerCameraFollow cameraFollow;
        private CityChessSetMen staticMen;
        private NpcSpeechBubbleView bubbles;
        private ParkChessPlayerPresentation chessPlayer;
        private ParkCheckersPlayerPresentation checkersPlayer;
        private CityParkQuarrelController quarrel;

        private Board active;
        private uint randomState;
        private int selectedFile = -1;
        private int selectedRank = -1;
        private int hoverFile = -1;
        private int hoverRank = -1;
        private int cursorFile = 3;
        private int cursorRank = 0;
        private bool pointerDrivesHover = true;
        private float opponentThinkRemaining;
        private bool opponentPending;
        private bool syncPending;
        private bool heroHasMoved;
        private float speechCooldownRemaining;
        private float againDelayRemaining;

        private CameraPhase cameraPhase;
        private float cameraYawOffset;
        private float cameraPitch = CityBoardGamePlan.BasePitchDegrees;
        private float cameraBlendElapsed;
        private Vector3 cameraBlendPosition;
        private Quaternion cameraBlendRotation;
        private float cameraBlendFieldOfView;
        private bool cameraOwned;
        private bool previousCinematicMotion;
        private bool previousFixedPose;
        private Pose previousFixedCameraPose;
        private float previousFixedFieldOfView;
        private Player3DHeadVisibility hiddenHead;

        /// <summary>The board the hero is sitting at, or `null`.</summary>
        public bool IsPlaying => active != null;
        public CityBoardGameKind ActiveGame =>
            active != null
                ? active.Table.Game
                : CityBoardGameKind.Chess;
        public IBoardGameMatch ActiveMatch => active?.Match;
        public bool WaitingForOpponent =>
            active != null &&
            (opponentPending ||
             (active.Pieces != null && active.Pieces.IsAnimating));
        public bool HeroToMove =>
            active != null &&
            active.Match.Status == BoardGameStatus.Playing &&
            active.Match.SideToMove == active.Table.HeroSide &&
            !WaitingForOpponent;
        public int SelectedFile => selectedFile;
        public int SelectedRank => selectedRank;

        /// <summary>
        /// Raises the controller over whichever of the two tables the
        /// generated city actually grew. Returns `null` when it grew
        /// neither, when the chess art was never imported, or when the
        /// bench interactions the seats belong to are missing.
        /// </summary>
        public static CityBoardGameController Create(
            Transform parent,
            IReadOnlyList<CityBoardGameTable> tables,
            IReadOnlyList<CityBenchSitInteraction> benchSits,
            PlayerRuntime playerRuntime,
            Camera camera,
            PlayerCameraFollow follow,
            CityChessSetMen men,
            NpcSpeechBubbleView speechBubbles,
            ParkChessPlayerPresentation parkChessPlayer,
            ParkCheckersPlayerPresentation parkCheckersPlayer,
            CityParkQuarrelController parkQuarrel,
            int citySeed)
        {
            if (parent == null ||
                tables == null ||
                tables.Count == 0 ||
                benchSits == null ||
                playerRuntime.GameObject == null ||
                camera == null ||
                follow == null)
            {
                return null;
            }

            CityChessSetProvider provider = CityChessSetProvider.Load();
            if (provider == null || !provider.IsComplete())
            {
                GameLog.Warning(
                    "city",
                    "board_game_absent",
                    GameLog.Field("reason", "chess_art_unbound"));
                return null;
            }

            var host = new GameObject(RootName);
            host.transform.SetParent(parent, false);
            var controller =
                host.AddComponent<CityBoardGameController>();
            controller.player = playerRuntime;
            controller.boardCamera = camera;
            controller.cameraFollow = follow;
            controller.staticMen = men;
            controller.bubbles = speechBubbles;
            controller.chessPlayer = parkChessPlayer;
            controller.checkersPlayer = parkCheckersPlayer;
            controller.quarrel = parkQuarrel;
            controller.randomState = CreateRandomState(citySeed);
            // Declared again rather than relying on the quarrel having
            // run first. Re-declaring one owner replaces his entry, so
            // this is free, and a park that grew a board but no quarrel
            // still has an opponent who can speak.
            DeclarePlayers(
                speechBubbles,
                parkChessPlayer,
                parkCheckersPlayer);

            for (int index = 0; index < tables.Count; index++)
            {
                CityBoardGameTable table = tables[index];
                CityBenchSitInteraction seat =
                    FindSeat(benchSits, table.SeatId);
                if (seat == null)
                {
                    continue;
                }

                var board = new Board
                {
                    Table = table,
                    Seat = seat,
                    Provider = provider,
                    Match = table.Game == CityBoardGameKind.Chess
                        ? (IBoardGameMatch)new ChessMatch()
                        : new DraughtsMatch()
                };
                controller.boards.Add(board);
                seat.SeatedChanged += controller.HandleSeatedChanged;
                GameLog.Info(
                    "city",
                    "board_game_installed",
                    GameLog.Field("game", table.Game.ToString()),
                    GameLog.Field("seat_id", table.SeatId));
            }

            if (controller.boards.Count == 0)
            {
                Destroy(host);
                return null;
            }

            return controller;
        }

        /// <summary>
        /// The match at one table, whether or not it has ever been
        /// played. Tests and the debug window read it; nothing else has
        /// any business with the other board.
        /// </summary>
        public IBoardGameMatch GetMatch(CityBoardGameKind game)
        {
            for (int index = 0; index < boards.Count; index++)
            {
                if (boards[index].Table.Game == game)
                {
                    return boards[index].Match;
                }
            }

            return null;
        }

        private static CityBenchSitInteraction FindSeat(
            IReadOnlyList<CityBenchSitInteraction> benchSits,
            string seatId)
        {
            for (int index = 0; index < benchSits.Count; index++)
            {
                CityBenchSitInteraction candidate = benchSits[index];
                if (candidate != null &&
                    string.Equals(
                        candidate.Plan.Id,
                        seatId,
                        StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static uint CreateRandomState(int citySeed)
        {
            unchecked
            {
                uint state = ((uint)citySeed * 2654435761u) ^
                             0x42474d45u; // "BGME"
                return state == 0u ? 0x9E3779B9u : state;
            }
        }

        private void HandleSeatedChanged(
            CityBenchSitInteraction seat,
            bool seated)
        {
            Board board = FindBoard(seat);
            if (board == null)
            {
                return;
            }

            if (seated)
            {
                Open(board);
            }
            else if (active == board)
            {
                Close();
            }
        }

        private Board FindBoard(CityBenchSitInteraction seat)
        {
            for (int index = 0; index < boards.Count; index++)
            {
                if (boards[index].Seat == seat)
                {
                    return boards[index];
                }
            }

            return null;
        }

        private void Open(Board board)
        {
            if (active != null)
            {
                Close();
            }

            active = board;
            if (!board.Raised)
            {
                // The untouched opening on this table stops being
                // scenery and becomes a game. It never goes back.
                staticMen?.SetTableVisible(board.Table.Table, false);
                board.Pieces = CityBoardGamePieces.Create(
                    transform,
                    board.Table,
                    board.Provider);
                board.Markers = CityBoardGameMarkers.Create(
                    transform,
                    board.Table);
                board.Pieces?.Sync(board.Match.Pieces);
                board.Raised = true;
            }

            selectedFile = -1;
            selectedRank = -1;
            hoverFile = -1;
            hoverRank = -1;
            cursorFile = 3;
            cursorRank = 3;
            pointerDrivesHover = true;
            cameraYawOffset = 0f;
            cameraPitch = CityBoardGamePlan.BasePitchDegrees;
            BeginCameraBlend(CameraPhase.Entering);
            SetHeroHeadHidden(true);
            heroHasMoved = false;
            speechCooldownRemaining = 0f;
            againDelayRemaining = 0f;
            quarrel?.SetSuppressed(true);
            ScheduleOpponentIfDue();
            RedrawMarkers();

            // Sitting down at a game that is already over is answered
            // with the offer of another one rather than with hello.
            Speak(
                board.Match.Status == BoardGameStatus.Playing
                    ? BoardCue.Greet
                    : BoardCue.Again,
                true);
            GameLog.Info(
                "city",
                "board_game_opened",
                GameLog.Field("game", board.Table.Game.ToString()),
                GameLog.Field("plies", board.Match.PliesPlayed));
        }

        private void Close()
        {
            if (active == null)
            {
                return;
            }

            active.Pieces?.CancelTurn();
            active.Pieces?.Sync(active.Match.Pieces);
            active.Markers?.Clear();
            selectedFile = -1;
            selectedRank = -1;
            hoverFile = -1;
            hoverRank = -1;
            opponentPending = false;
            opponentThinkRemaining = 0f;
            againDelayRemaining = 0f;
            quarrel?.SetSuppressed(false);
            SetHeroHeadHidden(false);
            BeginCameraBlend(CameraPhase.Leaving);
            GameLog.Info(
                "city",
                "board_game_closed",
                GameLog.Field("game", active.Table.Game.ToString()),
                GameLog.Field("plies", active.Match.PliesPlayed),
                GameLog.Field("status", active.Match.Status.ToString()));
            active = null;
        }

        private void Update()
        {
            if (active == null ||
                SceneTransitionService.IsTransitioning ||
                // The pause menu can open over the board (it takes no
                // modal lock a bench game holds), so the whole session
                // - input, speech and think timers run on unscaled
                // time - must hold its breath while the game is paused.
                PauseMenuController.IsAnyPaused)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            UpdateSpeechTimers(deltaTime);
            ReadLookInput(deltaTime);
            if (active.Pieces != null && active.Pieces.IsAnimating)
            {
                return;
            }

            if (syncPending)
            {
                // The carry is over. The board is re-read from the
                // rules rather than trusted to have been animated
                // correctly, so a presentation bug costs one frame of
                // wrong men instead of a whole broken game.
                syncPending = false;
                active.Pieces?.Sync(active.Match.Pieces);
                RedrawMarkers();
            }

            if (opponentPending)
            {
                opponentThinkRemaining -= Time.deltaTime;
                if (opponentThinkRemaining <= 0f)
                {
                    PlayOpponentMove();
                }

                return;
            }

            if (active.Match.Status != BoardGameStatus.Playing)
            {
                if (WasRestartPressed())
                {
                    RestartActiveMatch();
                }

                return;
            }

            if (active.Match.SideToMove != active.Table.HeroSide)
            {
                ScheduleOpponentIfDue();
                return;
            }

            ReadBoardInput();
        }

        private void LateUpdate()
        {
            UpdateOwnedCamera(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            Close();
            RestoreCameraOwnership();
            SetHeroHeadHidden(false);
            quarrel?.SetSuppressed(false);
        }

        private void OnDestroy()
        {
            for (int index = 0; index < boards.Count; index++)
            {
                CityBenchSitInteraction seat = boards[index].Seat;
                if (seat != null)
                {
                    seat.SeatedChanged -= HandleSeatedChanged;
                }
            }
        }

        private void ReadLookInput(float deltaTime)
        {
            if (cameraFollow == null)
            {
                return;
            }

            // The board cursor owns the arrow keys while seated, so
            // the seated camera takes only mouse and stick look.
            Vector2 look =
                cameraFollow.SampleOrbitInputDegrees(
                    deltaTime,
                    includeKeyboard: false);
            cameraYawOffset = Mathf.Clamp(
                cameraYawOffset + look.x,
                -CityBoardGamePlan.MaximumYawOffsetDegrees,
                CityBoardGamePlan.MaximumYawOffsetDegrees);
            cameraPitch = Mathf.Clamp(
                cameraPitch + look.y,
                CityBoardGamePlan.MinimumPitchDegrees,
                CityBoardGamePlan.MaximumPitchDegrees);
        }

        private void ReadBoardInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && boardCamera != null)
            {
                // A click is always meant where the pointer is, even
                // when the last thing touched was the cursor keys.
                bool clicked = mouse.leftButton.wasPressedThisFrame;
                if (clicked ||
                    mouse.delta.ReadValue().sqrMagnitude > 0.01f)
                {
                    pointerDrivesHover = true;
                }

                if (pointerDrivesHover)
                {
                    SetHover(
                        CityBoardGamePlan.TryPickSquare(
                            active.Table,
                            boardCamera.ScreenPointToRay(
                                mouse.position.ReadValue()),
                            out int pointedFile,
                            out int pointedRank)
                            ? pointedFile
                            : -1,
                        pointedRank);
                }

                // The right button is the head turn, so only the left
                // one ever touches a man.
                if (clicked && hoverFile >= 0)
                {
                    Choose(hoverFile, hoverRank);
                    return;
                }
            }

            ReadCursorDelta(out int rightStep, out int awayStep);
            if (rightStep != 0 || awayStep != 0)
            {
                pointerDrivesHover = false;
                cursorFile = Mathf.Clamp(
                    cursorFile + rightStep * ScreenRightFileSign,
                    0,
                    CityChessBoardGeometry.SquaresPerSide - 1);
                cursorRank = Mathf.Clamp(
                    cursorRank + awayStep * ScreenAwayRankSign,
                    0,
                    CityChessBoardGeometry.SquaresPerSide - 1);
                SetHover(cursorFile, cursorRank);
                RetroAudio.Play(RetroSfxId.UiMove);
                return;
            }

            if (WasConfirmPressed())
            {
                pointerDrivesHover = false;
                SetHover(cursorFile, cursorRank);
                Choose(cursorFile, cursorRank);
                return;
            }

            if (WasCancelPressed() && selectedFile >= 0)
            {
                ClearSelection();
                RetroAudio.Play(RetroSfxId.UiCancel);
            }
        }

        /// <summary>
        /// One click or one confirm. A square that a picked-up man may
        /// go to plays the move; a square holding one of the hero's own
        /// men picks that one up instead; anything else puts the man
        /// back down where he was.
        /// </summary>
        private void Choose(int file, int rank)
        {
            if (selectedFile >= 0 &&
                TryFindAction(file, rank, out int actionIndex))
            {
                PlayAction(actionIndex, true);
                return;
            }

            if (HasActionsFrom(file, rank))
            {
                if (selectedFile == file && selectedRank == rank)
                {
                    ClearSelection();
                    RetroAudio.Play(RetroSfxId.UiCancel);
                    return;
                }

                selectedFile = file;
                selectedRank = rank;
                RefreshSelectionActions();
                RedrawMarkers();
                RetroAudio.Play(RetroSfxId.UiMove);
                return;
            }

            if (selectedFile >= 0)
            {
                ClearSelection();
                RetroAudio.Play(RetroSfxId.UiCancel);
                return;
            }

            // A man of his own who cannot move at all, most often
            // because taking is compulsory somewhere else on the board.
            // An empty square answers with nothing: a beep every time
            // the pointer misses would be unbearable.
            if (HasOwnPieceAt(file, rank))
            {
                RetroAudio.Play(RetroSfxId.Bad);
                Speak(BoardCue.MustTake);
            }
        }

        private bool TryFindAction(
            int file,
            int rank,
            out int actionIndex)
        {
            actionIndex = -1;
            int bestCaptures = -1;
            for (int index = 0; index < selectionActions.Count; index++)
            {
                BoardGameAction action = selectionActions[index];
                if (action.ToFile != file || action.ToRank != rank)
                {
                    continue;
                }

                // Two draughts chains can end on the same square. The
                // longer one is what a player pointing at that square
                // meant, every time.
                int captures = action.CaptureCount;
                if (captures > bestCaptures)
                {
                    bestCaptures = captures;
                    actionIndex = action.Index;
                }
            }

            return actionIndex >= 0;
        }

        private bool HasOwnPieceAt(int file, int rank)
        {
            IReadOnlyList<BoardGamePiecePlacement> pieces =
                active.Match.Pieces;
            bool heroIsLight =
                active.Table.HeroSide == BoardGameSide.Light;
            for (int index = 0; index < pieces.Count; index++)
            {
                BoardGamePiecePlacement piece = pieces[index];
                if (piece.File == file &&
                    piece.Rank == rank &&
                    piece.IsLight == heroIsLight)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasActionsFrom(int file, int rank)
        {
            IReadOnlyList<BoardGameAction> actions =
                active.Match.LegalActions;
            for (int index = 0; index < actions.Count; index++)
            {
                if (actions[index].FromFile == file &&
                    actions[index].FromRank == rank)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshSelectionActions()
        {
            selectionActions.Clear();
            if (selectedFile < 0)
            {
                return;
            }

            IReadOnlyList<BoardGameAction> actions =
                active.Match.LegalActions;
            for (int index = 0; index < actions.Count; index++)
            {
                BoardGameAction action = actions[index];
                if (action.FromFile == selectedFile &&
                    action.FromRank == selectedRank)
                {
                    selectionActions.Add(action);
                }
            }
        }

        private void ClearSelection()
        {
            selectedFile = -1;
            selectedRank = -1;
            selectionActions.Clear();
            RedrawMarkers();
        }

        private void PlayAction(int actionIndex, bool byHero)
        {
            BoardGameStatus before = active.Match.Status;
            bool wasHeroTurn = byHero;
            if (!active.Match.TryApply(actionIndex, turn))
            {
                return;
            }

            active.Pieces?.BeginTurn(turn);
            syncPending = true;
            ClearSelection();
            RedrawMarkers();
            RetroAudio.Play(
                turn.CapturedAnything
                    ? RetroSfxId.BoardPieceTake
                    : RetroSfxId.BoardPiecePlace);
            heroHasMoved |= wasHeroTurn;

            if (before == BoardGameStatus.Playing &&
                active.Match.Status != BoardGameStatus.Playing)
            {
                AnnounceResult();
                return;
            }

            if (!wasHeroTurn)
            {
                AnnounceOpponentMove();
            }

            if (active.Match.SideToMove != active.Table.HeroSide)
            {
                ScheduleOpponentIfDue();
            }
        }

        /// <summary>
        /// The one thing worth saying about the move he has just made,
        /// in the order a player would want to hear it. Check first,
        /// because a check missed is a game lost; then a new king, then
        /// a man taken. When none of that happened he says nothing at
        /// all — except on his very first move of a sitting, where
        /// "your move" is the only thing telling a new player that the
        /// board is now waiting on him.
        /// </summary>
        private void AnnounceOpponentMove()
        {
            // Check and the one-off "your move" are forced through the
            // cooldown. A check the player did not read is a game lost
            // to a message that was merely inconvenient to schedule,
            // and the greeting he is still reading is exactly what the
            // first of them would have been dropped behind.
            if (active.Match.CheckPending)
            {
                Speak(BoardCue.Check, true);
                return;
            }

            if (turn.Promoted)
            {
                Speak(BoardCue.Crown);
                return;
            }

            if (turn.CapturedAnything)
            {
                Speak(BoardCue.Take);
                return;
            }

            if (!heroHasMoved)
            {
                Speak(BoardCue.YourMove, true);
            }
        }

        private void UpdateSpeechTimers(float deltaTime)
        {
            if (speechCooldownRemaining > 0f)
            {
                speechCooldownRemaining -= deltaTime;
            }

            if (againDelayRemaining <= 0f)
            {
                return;
            }

            againDelayRemaining -= deltaTime;
            if (againDelayRemaining <= 0f)
            {
                againDelayRemaining = 0f;
                Speak(BoardCue.Again, true);
            }
        }

        private void AnnounceResult()
        {
            BoardGameStatus status = active.Match.Status;
            bool heroWon =
                (active.Table.HeroSide == BoardGameSide.Dark &&
                 status == BoardGameStatus.DarkWins) ||
                (active.Table.HeroSide == BoardGameSide.Light &&
                 status == BoardGameStatus.LightWins);
            bool draw = status == BoardGameStatus.Draw;
            RetroAudio.Play(
                draw
                    ? RetroSfxId.UiCancel
                    : heroWon
                        ? RetroSfxId.Good
                        : RetroSfxId.Bad);
            Speak(
                draw
                    ? BoardCue.Draw
                    : heroWon
                        ? BoardCue.Lose
                        : BoardCue.Win,
                true);
            againDelayRemaining = AgainDelaySeconds;

            GameLog.Info(
                "city",
                "board_game_finished",
                GameLog.Field("game", active.Table.Game.ToString()),
                GameLog.Field("status", status.ToString()),
                GameLog.Field("ending", active.Match.Ending.ToString()),
                GameLog.Field("plies", active.Match.PliesPlayed));
        }

        private void ScheduleOpponentIfDue()
        {
            if (active == null ||
                opponentPending ||
                active.Match.Status != BoardGameStatus.Playing ||
                active.Match.SideToMove == active.Table.HeroSide)
            {
                return;
            }

            opponentPending = true;
            float span = MaximumThinkSeconds - MinimumThinkSeconds;
            opponentThinkRemaining = MinimumThinkSeconds +
                ChessEngine.NextRandom(ref randomState) /
                (float)uint.MaxValue * span;
        }

        private void PlayOpponentMove()
        {
            opponentPending = false;
            int actionIndex = active.Match.ChooseAction(ref randomState);
            if (actionIndex < 0)
            {
                return;
            }

            PlayAction(actionIndex, false);
        }

        private void RestartActiveMatch()
        {
            active.Match.Reset();
            active.Pieces?.CancelTurn();
            active.Pieces?.Sync(active.Match.Pieces);
            ClearSelection();
            opponentPending = false;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            ScheduleOpponentIfDue();
            RedrawMarkers();
        }

        private void SetHover(int file, int rank)
        {
            if (hoverFile == file && hoverRank == rank)
            {
                return;
            }

            hoverFile = file;
            hoverRank = rank;
            RedrawMarkers();
        }

        private void RedrawMarkers()
        {
            if (active?.Markers == null)
            {
                return;
            }

            int checkFile = -1;
            int checkRank = -1;
            if (active.Match.CheckPending)
            {
                FindKingSquare(
                    active.Match,
                    active.Match.SideToMove,
                    out checkFile,
                    out checkRank);
            }

            active.Markers.Redraw(
                hoverFile,
                hoverRank,
                selectedFile,
                selectedRank,
                selectionActions,
                active.Pieces != null
                    ? active.Pieces.LastMoveFromSquare
                    : -1,
                active.Pieces != null
                    ? active.Pieces.LastMoveToSquare
                    : -1,
                checkFile,
                checkRank);
        }

        private static void FindKingSquare(
            IBoardGameMatch match,
            BoardGameSide side,
            out int file,
            out int rank)
        {
            file = -1;
            rank = -1;
            IReadOnlyList<BoardGamePiecePlacement> pieces =
                match.Pieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                BoardGamePiecePlacement piece = pieces[index];
                if (piece.Kind != CityChessPieceKind.King ||
                    piece.IsLight != (side == BoardGameSide.Light))
                {
                    continue;
                }

                file = piece.File;
                rank = piece.Rank;
                return;
            }
        }

        private void BeginCameraBlend(CameraPhase phase)
        {
            if (cameraFollow == null || boardCamera == null)
            {
                return;
            }

            CaptureCameraOwnership();
            cameraBlendPosition = boardCamera.transform.position;
            cameraBlendRotation = boardCamera.transform.rotation;
            cameraBlendFieldOfView = boardCamera.fieldOfView;
            cameraBlendElapsed = 0f;
            cameraPhase = phase;
            if (phase == CameraPhase.Leaving || active == null)
            {
                CinematicDepthOfField.End();
            }
            else
            {
                // The board holds focus; the park and the opponent's
                // bench soften behind it.
                CinematicDepthOfField.Begin(
                    Vector3.Distance(
                        cameraBlendPosition,
                        active.Table.BoardCenter),
                    4f);
            }
        }

        private void CaptureCameraOwnership()
        {
            if (cameraOwned)
            {
                return;
            }

            previousCinematicMotion =
                cameraFollow.CinematicMotionEnabled;
            previousFixedPose = cameraFollow.FixedPoseActive;
            previousFixedCameraPose = cameraFollow.FixedBasePose;
            previousFixedFieldOfView =
                cameraFollow.FixedBaseFieldOfView;
            cameraFollow.SetCinematicMotionEnabled(false);
            cameraOwned = true;
        }

        private void UpdateOwnedCamera(float deltaTime)
        {
            if (!cameraOwned || cameraFollow == null)
            {
                return;
            }

            if (cameraPhase == CameraPhase.Leaving)
            {
                cameraBlendElapsed += Mathf.Max(0f, deltaTime);
                float amount = CameraBlendSeconds > 0f
                    ? Mathf.Clamp01(
                        cameraBlendElapsed / CameraBlendSeconds)
                    : 1f;
                Pose followPose = cameraFollow.ResolveFollowPose(
                    player.GameObject.transform.position);
                float smooth = Mathf.SmoothStep(0f, 1f, amount);
                cameraFollow.SetFixedPose(
                    Vector3.Lerp(
                        cameraBlendPosition,
                        followPose.position,
                        smooth),
                    Quaternion.Slerp(
                        cameraBlendRotation,
                        followPose.rotation,
                        smooth),
                    Mathf.Lerp(
                        cameraBlendFieldOfView,
                        cameraFollow.FollowFieldOfView,
                        smooth));
                if (amount >= 1f)
                {
                    RestoreCameraOwnership();
                }

                return;
            }

            if (active == null)
            {
                return;
            }

            CityBoardGamePlan.EvaluateCamera(
                active.Table,
                cameraYawOffset,
                cameraPitch,
                out Vector3 seatedPosition,
                out Quaternion seatedRotation);
            if (cameraPhase == CameraPhase.Entering)
            {
                cameraBlendElapsed += Mathf.Max(0f, deltaTime);
                float amount = CameraBlendSeconds > 0f
                    ? Mathf.Clamp01(
                        cameraBlendElapsed / CameraBlendSeconds)
                    : 1f;
                float smooth = Mathf.SmoothStep(0f, 1f, amount);
                cameraFollow.SetFixedPose(
                    Vector3.Lerp(
                        cameraBlendPosition,
                        seatedPosition,
                        smooth),
                    Quaternion.Slerp(
                        cameraBlendRotation,
                        seatedRotation,
                        smooth),
                    Mathf.Lerp(
                        cameraBlendFieldOfView,
                        CityBoardGamePlan.FieldOfView,
                        smooth));
                CinematicDepthOfField.SetFocusDistance(
                    Vector3.Distance(
                        Vector3.Lerp(
                            cameraBlendPosition,
                            seatedPosition,
                            smooth),
                        active.Table.BoardCenter));
                if (amount >= 1f)
                {
                    cameraPhase = CameraPhase.Playing;
                }

                return;
            }

            cameraFollow.SetFixedPose(
                seatedPosition,
                seatedRotation,
                CityBoardGamePlan.FieldOfView);
            CinematicDepthOfField.SetFocusDistance(
                Vector3.Distance(
                    seatedPosition,
                    active.Table.BoardCenter));
        }

        private void RestoreCameraOwnership()
        {
            CinematicDepthOfField.End();
            if (!cameraOwned || cameraFollow == null)
            {
                return;
            }

            if (previousFixedPose)
            {
                cameraFollow.SetFixedPose(
                    previousFixedCameraPose.position,
                    previousFixedCameraPose.rotation,
                    previousFixedFieldOfView);
            }
            else
            {
                cameraFollow.ClearFixedPose();
            }

            cameraFollow.SetCinematicMotionEnabled(
                previousCinematicMotion);
            cameraOwned = false;
            cameraPhase = CameraPhase.None;
        }

        /// <summary>
        /// The hero's own head, from the inside. The camera sits where
        /// his eyes are, so the whole head comes off while he is
        /// looking at the board and goes back on the moment he stands.
        ///
        /// The whole head, not the skull: this rig wears its hair,
        /// ears, nose, stubble and face on twenty-two separate meshes,
        /// and hiding only the two anatomical parts left the player
        /// looking at the inside of his own hair.
        /// <see cref="Player3DHeadVisibility"/> owns that rule. His
        /// hands on the stone stay, because they are half of what makes
        /// the shot read as sitting there.
        /// </summary>
        private void SetHeroHeadHidden(bool hidden)
        {
            if (hidden == (hiddenHead != null))
            {
                return;
            }

            if (!hidden)
            {
                hiddenHead.Restore();
                hiddenHead = null;
                return;
            }

            if (!(player.Visual is Player3DCharacterPresentation
                    presentation))
            {
                return;
            }

            hiddenHead = Player3DHeadVisibility.Hide(
                presentation.Registry);
        }

        /// <summary>
        /// Says one line over the man opposite. Ordinary lines are
        /// dropped while a previous one is still on screen; the ones
        /// the player cannot be allowed to miss — the result and the
        /// offer of another game — are forced through.
        /// </summary>
        private void Speak(BoardCue cue, bool insist = false)
        {
            if (active == null ||
                bubbles == null ||
                (!insist && speechCooldownRemaining > 0f))
            {
                return;
            }

            bool chess = active.Table.Game == CityBoardGameKind.Chess;
            // The anchor and the voice were declared by the quarrel when
            // the two men were raised; a game says the words and nothing
            // else about who is saying them.
            UnityEngine.Object owner = chess
                ? (UnityEngine.Object)chessPlayer
                : checkersPlayer;
            if (owner == null)
            {
                return;
            }

            int line = (int)(ChessEngine.NextRandom(ref randomState) %
                             LinesPerCue) + 1;
            string key = ResolveLineKey(
                active.Table.Game,
                cue.ToString(),
                line);
            if (bubbles.Show(owner, LocalizationService.Get(key)))
            {
                speechCooldownRemaining = SpeechCooldownSeconds;
            }
        }

        private static void DeclarePlayers(
            NpcSpeechBubbleView speechBubbles,
            ParkChessPlayerPresentation chess,
            ParkCheckersPlayerPresentation checkers)
        {
            if (speechBubbles == null)
            {
                return;
            }

            if (chess != null)
            {
                speechBubbles.DeclareSpeaker(
                    chess,
                    chess.SpeechAnchor,
                    chess.SpeechDesignId,
                    NpcEarshotProfile.Shout);
            }

            if (checkers != null)
            {
                speechBubbles.DeclareSpeaker(
                    checkers,
                    checkers.SpeechAnchor,
                    checkers.SpeechDesignId,
                    NpcEarshotProfile.Shout);
            }
        }

        /// <summary>The key one cue is served from. Static so the
        /// catalog can be checked against the enum rather than against
        /// a list somebody has to remember to update.</summary>
        public static string ResolveLineKey(
            CityBoardGameKind game,
            string cue,
            int line)
        {
            return "park.board." +
                (game == CityBoardGameKind.Chess ? "chess" : "checkers") +
                "." + cue.ToLowerInvariant() + "." + line.ToString("00");
        }

        /// <summary>
        /// Which cues each board can actually raise. Draughts has no
        /// check in it, and nothing else differs — the list exists so
        /// the localization catalog carries exactly the lines that can
        /// be spoken and not one key more.
        /// </summary>
        public static IReadOnlyList<string> CueNames(
            CityBoardGameKind game)
        {
            var names = new List<string>(10);
            foreach (BoardCue cue in
                     (BoardCue[])Enum.GetValues(typeof(BoardCue)))
            {
                if (cue == BoardCue.Check &&
                    game != CityBoardGameKind.Chess)
                {
                    continue;
                }

                names.Add(cue.ToString());
            }

            return names;
        }

        /// <summary>
        /// Which way the lattice runs on screen from this plank. The
        /// two seats look at each other down the same board, so one of
        /// them reads the files backwards and the ranks the other way
        /// up, and a cursor that ignored that would move left when the
        /// player pressed right at exactly one of the two tables.
        /// </summary>
        private int ScreenRightFileSign =>
            active != null &&
            Vector3.Dot(
                active.Table.SeatFacing,
                active.Table.Forward) < 0f
                ? 1
                : -1;

        private int ScreenAwayRankSign =>
            active != null &&
            Vector3.Dot(
                active.Table.SeatFacing,
                active.Table.Forward) < 0f
                ? -1
                : 1;

        private static void ReadCursorDelta(
            out int right,
            out int away)
        {
            right = 0;
            away = 0;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame ||
                    keyboard.aKey.wasPressedThisFrame)
                {
                    right = -1;
                }
                else if (keyboard.rightArrowKey.wasPressedThisFrame ||
                         keyboard.dKey.wasPressedThisFrame)
                {
                    right = 1;
                }

                if (keyboard.upArrowKey.wasPressedThisFrame ||
                    keyboard.wKey.wasPressedThisFrame)
                {
                    away = 1;
                }
                else if (keyboard.downArrowKey.wasPressedThisFrame ||
                         keyboard.sKey.wasPressedThisFrame)
                {
                    away = -1;
                }
            }

            if (right != 0 || away != 0)
            {
                return;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return;
            }

            if (gamepad.dpad.left.wasPressedThisFrame ||
                gamepad.leftStick.left.wasPressedThisFrame)
            {
                right = -1;
            }
            else if (gamepad.dpad.right.wasPressedThisFrame ||
                     gamepad.leftStick.right.wasPressedThisFrame)
            {
                right = 1;
            }

            if (gamepad.dpad.up.wasPressedThisFrame ||
                gamepad.leftStick.up.wasPressedThisFrame)
            {
                away = 1;
            }
            else if (gamepad.dpad.down.wasPressedThisFrame ||
                     gamepad.leftStick.down.wasPressedThisFrame)
            {
                away = -1;
            }
        }

        /// <summary>
        /// Space, or the west face button. Neither `E` nor `Enter` nor
        /// the south button: all three of those already stand the hero
        /// up, and a confirm that also left the table would be unusable.
        /// </summary>
        private static bool WasConfirmPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.CounterConfirm, GameInputContext.Menu);
        }

        private static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.backspaceKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static bool WasRestartPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonWest.wasPressedThisFrame;
        }

        private sealed class Board
        {
            public CityBoardGameTable Table;
            public CityBenchSitInteraction Seat;
            public CityChessSetProvider Provider;
            public IBoardGameMatch Match;
            public CityBoardGamePieces Pieces;
            public CityBoardGameMarkers Markers;
            public bool Raised;
        }
    }
}
