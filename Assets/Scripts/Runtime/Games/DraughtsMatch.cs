using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// One game of draughts on the park's right-hand table.
    ///
    /// Unlike the chess side there is no mirror to apply: the rules
    /// already run in the lattice the set was laid out in, because a
    /// draughts board is symmetric across the files and naming one of
    /// them `a` would have been a fiction invented purely to convert
    /// back out of.
    ///
    /// Two different chains can start and finish on the same pair of
    /// squares, which is why a click on a destination is answered with
    /// an action index rather than with a square: the board offers both
    /// and takes whichever one the caller pointed at first.
    /// </summary>
    public sealed class DraughtsMatch : IBoardGameMatch
    {
        private readonly DraughtsPosition position =
            new DraughtsPosition();
        private readonly DraughtsEngine engine = new DraughtsEngine();
        private readonly DraughtsMoveList legalMoves =
            new DraughtsMoveList();
        private readonly List<BoardGameAction> actions =
            new List<BoardGameAction>(48);
        private readonly List<BoardGamePiecePlacement> pieces =
            new List<BoardGamePiecePlacement>(24);

        private DraughtsEngineSettings settings =
            DraughtsEngineSettings.Park;

        public DraughtsMatch()
        {
            Reset();
        }

        public CityBoardGameKind Kind => CityBoardGameKind.Draughts;

        public BoardGameSide SideToMove => position.LightToMove
            ? BoardGameSide.Light
            : BoardGameSide.Dark;

        public BoardGameStatus Status { get; private set; }
        public BoardGameEnding Ending { get; private set; }

        /// <summary>Nothing is ever in check in draughts.</summary>
        public bool CheckPending => false;

        public int PliesPlayed => position.PliesPlayed;

        public IReadOnlyList<BoardGamePiecePlacement> Pieces => pieces;
        public IReadOnlyList<BoardGameAction> LegalActions => actions;

        public DraughtsPosition Position => position;

        public DraughtsEngineSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        /// <summary>True while every offer on the board takes a man.
        /// The line under the board says so, because a player who does
        /// not know the rule will otherwise think the board is broken
        /// when his other men refuse to be picked up.</summary>
        public bool CaptureCompulsory { get; private set; }

        public void Reset()
        {
            position.Reset();
            Refresh();
        }

        public void ResynchroniseFromPosition()
        {
            Refresh();
        }

        public bool TryApply(int actionIndex, BoardGameTurn turn)
        {
            if (turn == null)
            {
                throw new ArgumentNullException(nameof(turn));
            }

            if (Status != BoardGameStatus.Playing ||
                actionIndex < 0 ||
                actionIndex >= legalMoves.Count)
            {
                return false;
            }

            DraughtsMove move = legalMoves[actionIndex];
            turn.Clear();
            int current = move.From;
            for (int step = 0; step < move.StepCount; step++)
            {
                int landing = legalMoves.Landing(move, step);
                byte victim = legalMoves.Victim(move, step);
                turn.AddStep(
                    DraughtsPosition.FileOf(current),
                    DraughtsPosition.RankOf(current),
                    DraughtsPosition.FileOf(landing),
                    DraughtsPosition.RankOf(landing),
                    victim != DraughtsMoveList.NoCapture
                        ? DraughtsPosition.FileOf(victim)
                        : -1,
                    victim != DraughtsMoveList.NoCapture
                        ? DraughtsPosition.RankOf(victim)
                        : -1);
                current = landing;
            }

            if (move.Crowns)
            {
                turn.SetPromotion(CityChessPieceKind.Draught, true);
            }

            position.Apply(legalMoves, actionIndex);
            Refresh();
            return true;
        }

        public int ChooseAction(ref uint randomState)
        {
            if (Status != BoardGameStatus.Playing)
            {
                return -1;
            }

            return engine.Choose(
                position,
                legalMoves,
                settings,
                ref randomState);
        }

        private void Refresh()
        {
            position.Generate(legalMoves);
            actions.Clear();
            CaptureCompulsory =
                legalMoves.Count > 0 && legalMoves[0].IsCapture;
            for (int index = 0; index < legalMoves.Count; index++)
            {
                DraughtsMove move = legalMoves[index];
                byte destination = legalMoves.Destination(move);
                actions.Add(new BoardGameAction(
                    index,
                    DraughtsPosition.FileOf(move.From),
                    DraughtsPosition.RankOf(move.From),
                    DraughtsPosition.FileOf(destination),
                    DraughtsPosition.RankOf(destination),
                    move.IsCapture,
                    move.Crowns,
                    move.CaptureCount));
            }

            pieces.Clear();
            for (int square = 0; square < 64; square++)
            {
                byte piece = position[square];
                if (piece == DraughtsPiece.None)
                {
                    continue;
                }

                pieces.Add(new BoardGamePiecePlacement(
                    DraughtsPosition.FileOf(square),
                    DraughtsPosition.RankOf(square),
                    CityChessPieceKind.Draught,
                    DraughtsPiece.IsLight(piece),
                    DraughtsPiece.IsKing(piece)));
            }

            EvaluateStatus();
        }

        private void EvaluateStatus()
        {
            if (legalMoves.Count == 0)
            {
                // Stuck and swept are the same result and different
                // sentences, and the board says which one it was.
                position.CountMaterial(
                    position.LightToMove,
                    out int men,
                    out int kings);
                Status = position.LightToMove
                    ? BoardGameStatus.DarkWins
                    : BoardGameStatus.LightWins;
                Ending = men + kings == 0
                    ? BoardGameEnding.Swept
                    : BoardGameEnding.Blocked;
                return;
            }

            if (position.QuietPlies >= DraughtsPosition.QuietPlyLimit)
            {
                Status = BoardGameStatus.Draw;
                Ending = BoardGameEnding.QuietMoveLimit;
                return;
            }

            Status = BoardGameStatus.Playing;
            Ending = BoardGameEnding.None;
        }
    }
}
