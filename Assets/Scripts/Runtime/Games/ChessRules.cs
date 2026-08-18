using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// The six shapes, encoded so that colour is one bit above the type
    /// and an empty square is zero.
    /// </summary>
    public static class ChessPiece
    {
        public const byte None = 0;
        public const byte Pawn = 1;
        public const byte Knight = 2;
        public const byte Bishop = 3;
        public const byte Rook = 4;
        public const byte Queen = 5;
        public const byte King = 6;

        /// <summary>Black is the same six with this bit set.</summary>
        public const byte BlackFlag = 8;
        public const byte TypeMask = 7;

        public static byte Type(byte piece)
        {
            return (byte)(piece & TypeMask);
        }

        public static bool IsWhite(byte piece)
        {
            return piece != None && (piece & BlackFlag) == 0;
        }

        public static bool IsBlack(byte piece)
        {
            return (piece & BlackFlag) != 0;
        }

        public static byte Make(byte type, bool white)
        {
            return white ? type : (byte)(type | BlackFlag);
        }
    }

    /// <summary>
    /// One chess move in the position it was generated for. `From` and
    /// `To` are `rank * 8 + file` in ordinary chess coordinates — file
    /// `0` is the a-file, rank `0` is White's back rank — which is not
    /// the lattice the park board is drawn on. The conversion lives at
    /// the <see cref="ChessMatch"/> boundary and nowhere else, so this
    /// engine stays readable against any chess book.
    /// </summary>
    public readonly struct ChessMove : IEquatable<ChessMove>
    {
        public const byte CaptureFlag = 1;
        public const byte EnPassantFlag = 2;
        public const byte CastleKingSideFlag = 4;
        public const byte CastleQueenSideFlag = 8;
        public const byte DoublePushFlag = 16;

        public ChessMove(
            int from,
            int to,
            byte promotion = ChessPiece.None,
            byte flags = 0)
        {
            From = (byte)from;
            To = (byte)to;
            Promotion = promotion;
            Flags = flags;
        }

        public byte From { get; }
        public byte To { get; }
        public byte Promotion { get; }
        public byte Flags { get; }

        public bool IsCapture => (Flags & CaptureFlag) != 0;
        public bool IsEnPassant => (Flags & EnPassantFlag) != 0;
        public bool IsCastle =>
            (Flags & (CastleKingSideFlag | CastleQueenSideFlag)) != 0;
        public bool IsPromotion => Promotion != ChessPiece.None;

        public bool Equals(ChessMove other)
        {
            return From == other.From &&
                   To == other.To &&
                   Promotion == other.Promotion &&
                   Flags == other.Flags;
        }

        public override bool Equals(object obj)
        {
            return obj is ChessMove other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (From << 16) | (To << 8) | Promotion;
        }
    }

    /// <summary>What was on the board before a move, so it can be taken
    /// back without copying the whole position.</summary>
    public struct ChessUndo
    {
        public byte CapturedPiece;
        public sbyte CapturedSquare;
        public byte CastlingRights;
        public sbyte EnPassantSquare;
        public ushort HalfmoveClock;
    }

    /// <summary>
    /// A full legal-chess position: the board, whose turn it is, both
    /// pairs of castling rights, the en-passant square and the quiet
    /// move counter. Pure C# with no Unity in it at all, because the
    /// only way to know a chess implementation is right is to play
    /// positions through it in a test.
    ///
    /// Move generation is pseudo-legal followed by a make/attack/unmake
    /// filter. That is the slow way to write a chess engine and the
    /// hard way to get wrong, and the old man across the board is not
    /// searching deeply enough for the difference to be felt.
    /// </summary>
    public sealed class ChessPosition
    {
        public const byte WhiteKingSide = 1;
        public const byte WhiteQueenSide = 2;
        public const byte BlackKingSide = 4;
        public const byte BlackQueenSide = 8;
        public const byte AllCastlingRights = 15;

        /// <summary>Fifty moves a side, counted in plies.</summary>
        public const int QuietPlyLimit = 100;

        private static readonly int[] KnightFileSteps =
            { 1, 2, 2, 1, -1, -2, -2, -1 };
        private static readonly int[] KnightRankSteps =
            { 2, 1, -1, -2, -2, -1, 1, 2 };
        private static readonly int[] KingFileSteps =
            { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] KingRankSteps =
            { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] BishopFileSteps =
            { 1, 1, -1, -1 };
        private static readonly int[] BishopRankSteps =
            { 1, -1, -1, 1 };
        private static readonly int[] RookFileSteps =
            { 0, 1, 0, -1 };
        private static readonly int[] RookRankSteps =
            { 1, 0, -1, 0 };

        private static readonly byte[] PromotionOrder =
        {
            ChessPiece.Queen,
            ChessPiece.Rook,
            ChessPiece.Bishop,
            ChessPiece.Knight
        };

        private readonly byte[] squares = new byte[64];
        private readonly List<ChessMove> pseudoBuffer =
            new List<ChessMove>(256);

        private int whiteKingSquare;
        private int blackKingSquare;

        public ChessPosition()
        {
            Reset();
        }

        public bool WhiteToMove { get; private set; }
        public byte CastlingRights { get; private set; }

        /// <summary>The square a pawn may be captured on this move, or
        /// `-1`. Set only when the double push actually happened.</summary>
        public sbyte EnPassantSquare { get; private set; }

        public ushort HalfmoveClock { get; private set; }
        public int PliesPlayed { get; private set; }

        public byte this[int square] => squares[square];

        public static int Square(int file, int rank)
        {
            return rank * 8 + file;
        }

        public static int FileOf(int square)
        {
            return square & 7;
        }

        public static int RankOf(int square)
        {
            return square >> 3;
        }

        /// <summary>The ordinary opening array.</summary>
        public void Reset()
        {
            Array.Clear(squares, 0, squares.Length);
            byte[] backRank =
            {
                ChessPiece.Rook,
                ChessPiece.Knight,
                ChessPiece.Bishop,
                ChessPiece.Queen,
                ChessPiece.King,
                ChessPiece.Bishop,
                ChessPiece.Knight,
                ChessPiece.Rook
            };
            for (int file = 0; file < 8; file++)
            {
                squares[Square(file, 0)] =
                    ChessPiece.Make(backRank[file], true);
                squares[Square(file, 1)] =
                    ChessPiece.Make(ChessPiece.Pawn, true);
                squares[Square(file, 6)] =
                    ChessPiece.Make(ChessPiece.Pawn, false);
                squares[Square(file, 7)] =
                    ChessPiece.Make(backRank[file], false);
            }

            whiteKingSquare = Square(4, 0);
            blackKingSquare = Square(4, 7);
            WhiteToMove = true;
            CastlingRights = AllCastlingRights;
            EnPassantSquare = -1;
            HalfmoveClock = 0;
            PliesPlayed = 0;
        }

        /// <summary>
        /// Clears the board and takes the position from a caller who
        /// knows what it wants. Only tests use it, and only to state a
        /// position a game would take forty moves to reach.
        /// </summary>
        public void SetUp(
            IReadOnlyList<(int square, byte piece)> placement,
            bool whiteToMove,
            byte castlingRights = 0,
            int enPassantSquare = -1)
        {
            if (placement == null)
            {
                throw new ArgumentNullException(nameof(placement));
            }

            Array.Clear(squares, 0, squares.Length);
            whiteKingSquare = -1;
            blackKingSquare = -1;
            for (int index = 0; index < placement.Count; index++)
            {
                (int square, byte piece) = placement[index];
                squares[square] = piece;
                if (ChessPiece.Type(piece) != ChessPiece.King)
                {
                    continue;
                }

                if (ChessPiece.IsWhite(piece))
                {
                    whiteKingSquare = square;
                }
                else
                {
                    blackKingSquare = square;
                }
            }

            WhiteToMove = whiteToMove;
            CastlingRights = castlingRights;
            EnPassantSquare = (sbyte)enPassantSquare;
            HalfmoveClock = 0;
            PliesPlayed = 0;
        }

        public int KingSquare(bool white)
        {
            return white ? whiteKingSquare : blackKingSquare;
        }

        public bool IsInCheck(bool white)
        {
            int king = white ? whiteKingSquare : blackKingSquare;
            return king >= 0 && IsAttacked(king, !white);
        }

        /// <summary>
        /// Every move the side to move may actually play. Allocation
        /// happens in the caller's list, which the search reuses.
        /// </summary>
        public void GenerateLegal(List<ChessMove> moves)
        {
            if (moves == null)
            {
                throw new ArgumentNullException(nameof(moves));
            }

            moves.Clear();
            pseudoBuffer.Clear();
            GeneratePseudoLegal(pseudoBuffer, false);
            bool white = WhiteToMove;
            for (int index = 0; index < pseudoBuffer.Count; index++)
            {
                ChessMove move = pseudoBuffer[index];
                ChessUndo undo = MakeMove(move);
                if (!IsInCheck(white))
                {
                    moves.Add(move);
                }

                UnmakeMove(move, undo);
            }
        }

        /// <summary>
        /// The captures and promotions only, for the quiescence pass
        /// that keeps the engine from stopping its search in the middle
        /// of an exchange.
        /// </summary>
        public void GenerateLegalCaptures(List<ChessMove> moves)
        {
            moves.Clear();
            pseudoBuffer.Clear();
            GeneratePseudoLegal(pseudoBuffer, true);
            bool white = WhiteToMove;
            for (int index = 0; index < pseudoBuffer.Count; index++)
            {
                ChessMove move = pseudoBuffer[index];
                ChessUndo undo = MakeMove(move);
                if (!IsInCheck(white))
                {
                    moves.Add(move);
                }

                UnmakeMove(move, undo);
            }
        }

        public ChessUndo MakeMove(ChessMove move)
        {
            var undo = new ChessUndo
            {
                CapturedPiece = ChessPiece.None,
                CapturedSquare = -1,
                CastlingRights = CastlingRights,
                EnPassantSquare = EnPassantSquare,
                HalfmoveClock = HalfmoveClock
            };

            byte moving = squares[move.From];
            bool white = ChessPiece.IsWhite(moving);
            int capturedSquare = move.To;
            if (move.IsEnPassant)
            {
                capturedSquare = white ? move.To - 8 : move.To + 8;
            }

            byte captured = squares[capturedSquare];
            if (captured != ChessPiece.None)
            {
                undo.CapturedPiece = captured;
                undo.CapturedSquare = (sbyte)capturedSquare;
                squares[capturedSquare] = ChessPiece.None;
            }

            squares[move.From] = ChessPiece.None;
            byte landed = move.IsPromotion
                ? ChessPiece.Make(move.Promotion, white)
                : moving;
            squares[move.To] = landed;

            if (ChessPiece.Type(moving) == ChessPiece.King)
            {
                if (white)
                {
                    whiteKingSquare = move.To;
                }
                else
                {
                    blackKingSquare = move.To;
                }

                if ((move.Flags & ChessMove.CastleKingSideFlag) != 0)
                {
                    MoveRookForCastle(white, true);
                }
                else if ((move.Flags &
                          ChessMove.CastleQueenSideFlag) != 0)
                {
                    MoveRookForCastle(white, false);
                }
            }

            UpdateCastlingRights(move.From);
            UpdateCastlingRights(move.To);

            EnPassantSquare = (move.Flags & ChessMove.DoublePushFlag) != 0
                ? (sbyte)((move.From + move.To) / 2)
                : (sbyte)-1;
            HalfmoveClock =
                ChessPiece.Type(moving) == ChessPiece.Pawn ||
                undo.CapturedPiece != ChessPiece.None
                    ? (ushort)0
                    : (ushort)(HalfmoveClock + 1);
            WhiteToMove = !WhiteToMove;
            PliesPlayed++;
            return undo;
        }

        public void UnmakeMove(ChessMove move, ChessUndo undo)
        {
            WhiteToMove = !WhiteToMove;
            PliesPlayed--;
            byte landed = squares[move.To];
            bool white = ChessPiece.IsWhite(landed);
            byte moving = move.IsPromotion
                ? ChessPiece.Make(ChessPiece.Pawn, white)
                : landed;
            squares[move.From] = moving;
            squares[move.To] = ChessPiece.None;
            if (undo.CapturedSquare >= 0)
            {
                squares[undo.CapturedSquare] = undo.CapturedPiece;
            }

            if (ChessPiece.Type(moving) == ChessPiece.King)
            {
                if (white)
                {
                    whiteKingSquare = move.From;
                }
                else
                {
                    blackKingSquare = move.From;
                }

                if ((move.Flags & ChessMove.CastleKingSideFlag) != 0)
                {
                    UnmoveRookForCastle(white, true);
                }
                else if ((move.Flags &
                          ChessMove.CastleQueenSideFlag) != 0)
                {
                    UnmoveRookForCastle(white, false);
                }
            }

            CastlingRights = undo.CastlingRights;
            EnPassantSquare = undo.EnPassantSquare;
            HalfmoveClock = undo.HalfmoveClock;
        }

        /// <summary>
        /// Whether either side still has enough on the board to mate.
        /// King against king, king and one minor, and king and bishop
        /// against king and bishop of the same colour are the only ones
        /// this board will ever reach.
        /// </summary>
        public bool IsInsufficientMaterial()
        {
            int whiteMinors = 0;
            int blackMinors = 0;
            int whiteBishopParity = -1;
            int blackBishopParity = -1;
            for (int square = 0; square < 64; square++)
            {
                byte piece = squares[square];
                byte type = ChessPiece.Type(piece);
                switch (type)
                {
                    case ChessPiece.None:
                    case ChessPiece.King:
                        continue;
                    case ChessPiece.Knight:
                    case ChessPiece.Bishop:
                        if (ChessPiece.IsWhite(piece))
                        {
                            whiteMinors++;
                            if (type == ChessPiece.Bishop)
                            {
                                whiteBishopParity =
                                    (FileOf(square) + RankOf(square)) & 1;
                            }
                        }
                        else
                        {
                            blackMinors++;
                            if (type == ChessPiece.Bishop)
                            {
                                blackBishopParity =
                                    (FileOf(square) + RankOf(square)) & 1;
                            }
                        }

                        continue;
                    default:
                        return false;
                }
            }

            if (whiteMinors <= 1 && blackMinors == 0)
            {
                return true;
            }

            if (blackMinors <= 1 && whiteMinors == 0)
            {
                return true;
            }

            return whiteMinors == 1 &&
                   blackMinors == 1 &&
                   whiteBishopParity >= 0 &&
                   whiteBishopParity == blackBishopParity;
        }

        /// <summary>
        /// A cheap position key, good enough to notice that the same
        /// position has stood on the table three times. Not a Zobrist
        /// hash; it does not have to survive a transposition table.
        /// </summary>
        public ulong ComputeKey()
        {
            ulong hash = 1469598103934665603ul;
            for (int square = 0; square < 64; square++)
            {
                hash ^= squares[square];
                hash *= 1099511628211ul;
            }

            hash ^= (ulong)CastlingRights << 1;
            hash *= 1099511628211ul;
            hash ^= (ulong)(EnPassantSquare + 1);
            hash *= 1099511628211ul;
            if (WhiteToMove)
            {
                hash ^= 0x9E3779B97F4A7C15ul;
            }

            return hash;
        }

        public bool IsAttacked(int square, bool byWhite)
        {
            int file = FileOf(square);
            int rank = RankOf(square);

            // Pawns attack toward their own far rank, so the square is
            // probed backwards from the attacker's point of view.
            int pawnRank = byWhite ? rank - 1 : rank + 1;
            if (pawnRank >= 0 && pawnRank < 8)
            {
                byte pawn = ChessPiece.Make(ChessPiece.Pawn, byWhite);
                if (file > 0 &&
                    squares[Square(file - 1, pawnRank)] == pawn)
                {
                    return true;
                }

                if (file < 7 &&
                    squares[Square(file + 1, pawnRank)] == pawn)
                {
                    return true;
                }
            }

            byte knight = ChessPiece.Make(ChessPiece.Knight, byWhite);
            for (int index = 0; index < 8; index++)
            {
                int knightFile = file + KnightFileSteps[index];
                int knightRank = rank + KnightRankSteps[index];
                if (IsOnBoard(knightFile, knightRank) &&
                    squares[Square(knightFile, knightRank)] == knight)
                {
                    return true;
                }
            }

            byte king = ChessPiece.Make(ChessPiece.King, byWhite);
            for (int index = 0; index < 8; index++)
            {
                int kingFile = file + KingFileSteps[index];
                int kingRank = rank + KingRankSteps[index];
                if (IsOnBoard(kingFile, kingRank) &&
                    squares[Square(kingFile, kingRank)] == king)
                {
                    return true;
                }
            }

            if (IsAttackedBySlider(
                    file,
                    rank,
                    byWhite,
                    ChessPiece.Bishop,
                    BishopFileSteps,
                    BishopRankSteps))
            {
                return true;
            }

            return IsAttackedBySlider(
                file,
                rank,
                byWhite,
                ChessPiece.Rook,
                RookFileSteps,
                RookRankSteps);
        }

        private bool IsAttackedBySlider(
            int file,
            int rank,
            bool byWhite,
            byte straightType,
            int[] fileSteps,
            int[] rankSteps)
        {
            byte piece = ChessPiece.Make(straightType, byWhite);
            byte queen = ChessPiece.Make(ChessPiece.Queen, byWhite);
            for (int index = 0; index < fileSteps.Length; index++)
            {
                int currentFile = file;
                int currentRank = rank;
                while (true)
                {
                    currentFile += fileSteps[index];
                    currentRank += rankSteps[index];
                    if (!IsOnBoard(currentFile, currentRank))
                    {
                        break;
                    }

                    byte occupant =
                        squares[Square(currentFile, currentRank)];
                    if (occupant == ChessPiece.None)
                    {
                        continue;
                    }

                    if (occupant == piece || occupant == queen)
                    {
                        return true;
                    }

                    break;
                }
            }

            return false;
        }

        private void GeneratePseudoLegal(
            List<ChessMove> moves,
            bool capturesOnly)
        {
            bool white = WhiteToMove;
            for (int square = 0; square < 64; square++)
            {
                byte piece = squares[square];
                if (piece == ChessPiece.None ||
                    ChessPiece.IsWhite(piece) != white)
                {
                    continue;
                }

                switch (ChessPiece.Type(piece))
                {
                    case ChessPiece.Pawn:
                        GeneratePawnMoves(
                            moves,
                            square,
                            white,
                            capturesOnly);
                        break;
                    case ChessPiece.Knight:
                        GenerateStepMoves(
                            moves,
                            square,
                            white,
                            KnightFileSteps,
                            KnightRankSteps,
                            capturesOnly);
                        break;
                    case ChessPiece.Bishop:
                        GenerateSlideMoves(
                            moves,
                            square,
                            white,
                            BishopFileSteps,
                            BishopRankSteps,
                            capturesOnly);
                        break;
                    case ChessPiece.Rook:
                        GenerateSlideMoves(
                            moves,
                            square,
                            white,
                            RookFileSteps,
                            RookRankSteps,
                            capturesOnly);
                        break;
                    case ChessPiece.Queen:
                        GenerateSlideMoves(
                            moves,
                            square,
                            white,
                            KingFileSteps,
                            KingRankSteps,
                            capturesOnly);
                        break;
                    case ChessPiece.King:
                        GenerateStepMoves(
                            moves,
                            square,
                            white,
                            KingFileSteps,
                            KingRankSteps,
                            capturesOnly);
                        if (!capturesOnly)
                        {
                            GenerateCastles(moves, white);
                        }

                        break;
                }
            }
        }

        private void GeneratePawnMoves(
            List<ChessMove> moves,
            int square,
            bool white,
            bool capturesOnly)
        {
            int file = FileOf(square);
            int rank = RankOf(square);
            int forward = white ? 1 : -1;
            int promotionRank = white ? 7 : 0;
            int startRank = white ? 1 : 6;

            int aheadRank = rank + forward;
            if (IsOnBoard(file, aheadRank))
            {
                int ahead = Square(file, aheadRank);
                if (squares[ahead] == ChessPiece.None)
                {
                    if (aheadRank == promotionRank)
                    {
                        AddPromotions(moves, square, ahead, 0);
                    }
                    else if (!capturesOnly)
                    {
                        moves.Add(new ChessMove(square, ahead));
                        int doubleRank = rank + forward * 2;
                        if (rank == startRank &&
                            squares[Square(file, doubleRank)] ==
                                ChessPiece.None)
                        {
                            moves.Add(new ChessMove(
                                square,
                                Square(file, doubleRank),
                                ChessPiece.None,
                                ChessMove.DoublePushFlag));
                        }
                    }
                }
            }

            for (int side = -1; side <= 1; side += 2)
            {
                int captureFile = file + side;
                if (!IsOnBoard(captureFile, aheadRank))
                {
                    continue;
                }

                int target = Square(captureFile, aheadRank);
                byte occupant = squares[target];
                if (occupant != ChessPiece.None &&
                    ChessPiece.IsWhite(occupant) != white)
                {
                    if (aheadRank == promotionRank)
                    {
                        AddPromotions(
                            moves,
                            square,
                            target,
                            ChessMove.CaptureFlag);
                    }
                    else
                    {
                        moves.Add(new ChessMove(
                            square,
                            target,
                            ChessPiece.None,
                            ChessMove.CaptureFlag));
                    }

                    continue;
                }

                if (EnPassantSquare >= 0 && target == EnPassantSquare)
                {
                    moves.Add(new ChessMove(
                        square,
                        target,
                        ChessPiece.None,
                        (byte)(ChessMove.CaptureFlag |
                               ChessMove.EnPassantFlag)));
                }
            }
        }

        private static void AddPromotions(
            List<ChessMove> moves,
            int from,
            int to,
            byte flags)
        {
            for (int index = 0; index < PromotionOrder.Length; index++)
            {
                moves.Add(new ChessMove(
                    from,
                    to,
                    PromotionOrder[index],
                    flags));
            }
        }

        private void GenerateStepMoves(
            List<ChessMove> moves,
            int square,
            bool white,
            int[] fileSteps,
            int[] rankSteps,
            bool capturesOnly)
        {
            int file = FileOf(square);
            int rank = RankOf(square);
            for (int index = 0; index < fileSteps.Length; index++)
            {
                int targetFile = file + fileSteps[index];
                int targetRank = rank + rankSteps[index];
                if (!IsOnBoard(targetFile, targetRank))
                {
                    continue;
                }

                int target = Square(targetFile, targetRank);
                byte occupant = squares[target];
                if (occupant == ChessPiece.None)
                {
                    if (!capturesOnly)
                    {
                        moves.Add(new ChessMove(square, target));
                    }

                    continue;
                }

                if (ChessPiece.IsWhite(occupant) != white)
                {
                    moves.Add(new ChessMove(
                        square,
                        target,
                        ChessPiece.None,
                        ChessMove.CaptureFlag));
                }
            }
        }

        private void GenerateSlideMoves(
            List<ChessMove> moves,
            int square,
            bool white,
            int[] fileSteps,
            int[] rankSteps,
            bool capturesOnly)
        {
            int file = FileOf(square);
            int rank = RankOf(square);
            for (int index = 0; index < fileSteps.Length; index++)
            {
                int currentFile = file;
                int currentRank = rank;
                while (true)
                {
                    currentFile += fileSteps[index];
                    currentRank += rankSteps[index];
                    if (!IsOnBoard(currentFile, currentRank))
                    {
                        break;
                    }

                    int target = Square(currentFile, currentRank);
                    byte occupant = squares[target];
                    if (occupant == ChessPiece.None)
                    {
                        if (!capturesOnly)
                        {
                            moves.Add(new ChessMove(square, target));
                        }

                        continue;
                    }

                    if (ChessPiece.IsWhite(occupant) != white)
                    {
                        moves.Add(new ChessMove(
                            square,
                            target,
                            ChessPiece.None,
                            ChessMove.CaptureFlag));
                    }

                    break;
                }
            }
        }

        private void GenerateCastles(List<ChessMove> moves, bool white)
        {
            int backRank = white ? 0 : 7;
            int kingSquare = Square(4, backRank);
            if (squares[kingSquare] !=
                ChessPiece.Make(ChessPiece.King, white))
            {
                return;
            }

            byte kingSideRight = white ? WhiteKingSide : BlackKingSide;
            byte queenSideRight = white ? WhiteQueenSide : BlackQueenSide;
            byte rook = ChessPiece.Make(ChessPiece.Rook, white);
            if ((CastlingRights & kingSideRight) != 0 &&
                squares[Square(7, backRank)] == rook &&
                squares[Square(5, backRank)] == ChessPiece.None &&
                squares[Square(6, backRank)] == ChessPiece.None &&
                !IsAttacked(kingSquare, !white) &&
                !IsAttacked(Square(5, backRank), !white) &&
                !IsAttacked(Square(6, backRank), !white))
            {
                moves.Add(new ChessMove(
                    kingSquare,
                    Square(6, backRank),
                    ChessPiece.None,
                    ChessMove.CastleKingSideFlag));
            }

            if ((CastlingRights & queenSideRight) != 0 &&
                squares[Square(0, backRank)] == rook &&
                squares[Square(1, backRank)] == ChessPiece.None &&
                squares[Square(2, backRank)] == ChessPiece.None &&
                squares[Square(3, backRank)] == ChessPiece.None &&
                !IsAttacked(kingSquare, !white) &&
                !IsAttacked(Square(3, backRank), !white) &&
                !IsAttacked(Square(2, backRank), !white))
            {
                moves.Add(new ChessMove(
                    kingSquare,
                    Square(2, backRank),
                    ChessPiece.None,
                    ChessMove.CastleQueenSideFlag));
            }
        }

        /// <summary>
        /// The rook's half of a castle. Stated once for both directions
        /// so the make and the unmake cannot disagree about which rook
        /// went where.
        /// </summary>
        public static void DescribeCastleRook(
            bool white,
            bool kingSide,
            out int rookFrom,
            out int rookTo)
        {
            int backRank = white ? 0 : 7;
            rookFrom = Square(kingSide ? 7 : 0, backRank);
            rookTo = Square(kingSide ? 5 : 3, backRank);
        }

        private void MoveRookForCastle(bool white, bool kingSide)
        {
            DescribeCastleRook(
                white,
                kingSide,
                out int rookFrom,
                out int rookTo);
            squares[rookTo] = squares[rookFrom];
            squares[rookFrom] = ChessPiece.None;
        }

        private void UnmoveRookForCastle(bool white, bool kingSide)
        {
            DescribeCastleRook(
                white,
                kingSide,
                out int rookFrom,
                out int rookTo);
            squares[rookFrom] = squares[rookTo];
            squares[rookTo] = ChessPiece.None;
        }

        private void UpdateCastlingRights(int square)
        {
            switch (square)
            {
                case 0:
                    CastlingRights &= unchecked((byte)~WhiteQueenSide);
                    break;
                case 7:
                    CastlingRights &= unchecked((byte)~WhiteKingSide);
                    break;
                case 4:
                    CastlingRights &= unchecked(
                        (byte)~(WhiteKingSide | WhiteQueenSide));
                    break;
                case 56:
                    CastlingRights &= unchecked((byte)~BlackQueenSide);
                    break;
                case 63:
                    CastlingRights &= unchecked((byte)~BlackKingSide);
                    break;
                case 60:
                    CastlingRights &= unchecked(
                        (byte)~(BlackKingSide | BlackQueenSide));
                    break;
            }
        }

        private static bool IsOnBoard(int file, int rank)
        {
            return file >= 0 && file < 8 && rank >= 0 && rank < 8;
        }
    }
}
