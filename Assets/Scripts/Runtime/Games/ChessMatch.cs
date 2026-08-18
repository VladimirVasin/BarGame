using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// One game of chess on the park's left-hand table, stated in the
    /// lattice the set stands on rather than in chess coordinates.
    ///
    /// The conversion is the whole reason this class exists. The board
    /// was drawn with its `a` file at lattice file `7`, because both
    /// planks face along `+Forward` and the tangent runs to the left of
    /// a man facing that way; the engine underneath knows nothing about
    /// that and reads like any chess book. One mirror, in one place.
    ///
    /// Under-promotion is not offered. On this board a pawn that gets
    /// there becomes a queen, which is what both of these men would do
    /// anyway, and it keeps a click on a square from asking a question
    /// nobody sitting on a plank in a park wants to be asked.
    /// </summary>
    public sealed class ChessMatch : IBoardGameMatch
    {
        private readonly ChessPosition position = new ChessPosition();
        private readonly ChessEngine engine = new ChessEngine();
        private readonly List<ChessMove> legalMoves =
            new List<ChessMove>(72);
        private readonly List<BoardGameAction> actions =
            new List<BoardGameAction>(72);
        private readonly List<BoardGamePiecePlacement> pieces =
            new List<BoardGamePiecePlacement>(32);
        private readonly List<ulong> history = new List<ulong>(128);

        private ChessEngineSettings settings =
            ChessEngineSettings.Park;

        public ChessMatch()
        {
            Reset();
        }

        public CityBoardGameKind Kind => CityBoardGameKind.Chess;

        public BoardGameSide SideToMove => position.WhiteToMove
            ? BoardGameSide.Light
            : BoardGameSide.Dark;

        public BoardGameStatus Status { get; private set; }
        public BoardGameEnding Ending { get; private set; }
        public bool CheckPending { get; private set; }
        public int PliesPlayed => position.PliesPlayed;

        public IReadOnlyList<BoardGamePiecePlacement> Pieces => pieces;
        public IReadOnlyList<BoardGameAction> LegalActions => actions;

        /// <summary>The engine underneath, for a test that wants to set
        /// a position up rather than play forty moves to reach it.</summary>
        public ChessPosition Position => position;

        public ChessEngineSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        /// <summary>
        /// The lattice file a chess file stands on. Its own inverse, so
        /// the same call converts both ways and the mirror can never be
        /// applied once too often.
        /// </summary>
        public static int MirrorFile(int file)
        {
            return CityChessBoardGeometry.SquaresPerSide - 1 - file;
        }

        public void Reset()
        {
            position.Reset();
            history.Clear();
            history.Add(position.ComputeKey());
            Refresh();
        }

        /// <summary>Re-reads the board after a test set a position up
        /// underneath.</summary>
        public void ResynchroniseFromPosition()
        {
            history.Clear();
            history.Add(position.ComputeKey());
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

            ChessMove move = legalMoves[actionIndex];
            turn.Clear();

            int capturedSquare = -1;
            if (move.IsEnPassant)
            {
                capturedSquare = position.WhiteToMove
                    ? move.To - 8
                    : move.To + 8;
            }
            else if (position[move.To] != ChessPiece.None)
            {
                capturedSquare = move.To;
            }

            turn.AddStep(
                MirrorFile(ChessPosition.FileOf(move.From)),
                ChessPosition.RankOf(move.From),
                MirrorFile(ChessPosition.FileOf(move.To)),
                ChessPosition.RankOf(move.To),
                capturedSquare >= 0
                    ? MirrorFile(ChessPosition.FileOf(capturedSquare))
                    : -1,
                capturedSquare >= 0
                    ? ChessPosition.RankOf(capturedSquare)
                    : -1);

            if (move.IsCastle)
            {
                ChessPosition.DescribeCastleRook(
                    position.WhiteToMove,
                    (move.Flags & ChessMove.CastleKingSideFlag) != 0,
                    out int rookFrom,
                    out int rookTo);

                // The rook's travel is a second step in the same turn.
                // It is added after the king's, and the presentation
                // plays them in that order, because that is the order a
                // hand does it.
                turn.AddStep(
                    MirrorFile(ChessPosition.FileOf(rookFrom)),
                    ChessPosition.RankOf(rookFrom),
                    MirrorFile(ChessPosition.FileOf(rookTo)),
                    ChessPosition.RankOf(rookTo));

                // The mover is still the king: the rook was only the
                // second half of his one move.
                turn.SetEnd(
                    MirrorFile(ChessPosition.FileOf(move.To)),
                    ChessPosition.RankOf(move.To));
            }

            if (move.IsPromotion)
            {
                turn.SetPromotion(ToMeshKind(move.Promotion), false);
            }

            position.MakeMove(move);
            history.Add(position.ComputeKey());
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

        public static CityChessPieceKind ToMeshKind(byte type)
        {
            switch (ChessPiece.Type(type))
            {
                case ChessPiece.Pawn:
                    return CityChessPieceKind.Pawn;
                case ChessPiece.Knight:
                    return CityChessPieceKind.Knight;
                case ChessPiece.Bishop:
                    return CityChessPieceKind.Bishop;
                case ChessPiece.Rook:
                    return CityChessPieceKind.Rook;
                case ChessPiece.Queen:
                    return CityChessPieceKind.Queen;
                case ChessPiece.King:
                    return CityChessPieceKind.King;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private void Refresh()
        {
            position.GenerateLegal(legalMoves);
            for (int index = legalMoves.Count - 1; index >= 0; index--)
            {
                ChessMove move = legalMoves[index];
                if (move.IsPromotion &&
                    move.Promotion != ChessPiece.Queen)
                {
                    legalMoves.RemoveAt(index);
                }
            }

            actions.Clear();
            for (int index = 0; index < legalMoves.Count; index++)
            {
                ChessMove move = legalMoves[index];
                actions.Add(new BoardGameAction(
                    index,
                    MirrorFile(ChessPosition.FileOf(move.From)),
                    ChessPosition.RankOf(move.From),
                    MirrorFile(ChessPosition.FileOf(move.To)),
                    ChessPosition.RankOf(move.To),
                    move.IsCapture,
                    move.IsPromotion));
            }

            pieces.Clear();
            for (int square = 0; square < 64; square++)
            {
                byte piece = position[square];
                if (piece == ChessPiece.None)
                {
                    continue;
                }

                pieces.Add(new BoardGamePiecePlacement(
                    MirrorFile(ChessPosition.FileOf(square)),
                    ChessPosition.RankOf(square),
                    ToMeshKind(piece),
                    ChessPiece.IsWhite(piece),
                    false));
            }

            CheckPending = position.IsInCheck(position.WhiteToMove);
            EvaluateStatus();
        }

        private void EvaluateStatus()
        {
            if (legalMoves.Count == 0)
            {
                if (CheckPending)
                {
                    Status = position.WhiteToMove
                        ? BoardGameStatus.DarkWins
                        : BoardGameStatus.LightWins;
                    Ending = BoardGameEnding.Checkmate;
                }
                else
                {
                    Status = BoardGameStatus.Draw;
                    Ending = BoardGameEnding.Stalemate;
                }

                return;
            }

            if (position.IsInsufficientMaterial())
            {
                Status = BoardGameStatus.Draw;
                Ending = BoardGameEnding.InsufficientMaterial;
                return;
            }

            if (position.HalfmoveClock >= ChessPosition.QuietPlyLimit)
            {
                Status = BoardGameStatus.Draw;
                Ending = BoardGameEnding.QuietMoveLimit;
                return;
            }

            if (CountRepetitions() >= 3)
            {
                Status = BoardGameStatus.Draw;
                Ending = BoardGameEnding.Repetition;
                return;
            }

            Status = BoardGameStatus.Playing;
            Ending = BoardGameEnding.None;
        }

        private int CountRepetitions()
        {
            if (history.Count == 0)
            {
                return 0;
            }

            ulong current = history[history.Count - 1];
            int count = 0;
            for (int index = 0; index < history.Count; index++)
            {
                if (history[index] == current)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
