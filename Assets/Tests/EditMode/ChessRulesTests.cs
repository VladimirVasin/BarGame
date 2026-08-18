using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The park's chess engine, checked the only way a chess
    /// implementation can honestly be checked: by counting.
    ///
    /// Perft walks every legal move to a fixed depth and compares the
    /// leaf count against numbers the chess world has agreed on for
    /// decades. It is a brutal test — a single wrong pin, a castle
    /// through check, an en-passant capture that should not exist and
    /// the count is off by thousands — and it is the reason none of the
    /// rules below are asserted one at a time.
    ///
    /// The five positions are the standard ones. Between them they
    /// cover castling on both sides for both colours, castling out of
    /// and through check, en passant including the pin that forbids it,
    /// promotion with and without capture, and a position with no
    /// castling rights at all.
    /// </summary>
    public sealed class ChessRulesTests
    {
        private const string StartFen =
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -";
        private const string KiwipeteFen =
            "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/" +
            "R3K2R w KQkq -";
        private const string EndgameFen =
            "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - -";
        private const string PromotionFen =
            "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/" +
            "R2Q1RK1 w kq -";
        private const string TacticalFen =
            "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ -";

        [TestCase(StartFen, 1, 20L)]
        [TestCase(StartFen, 2, 400L)]
        [TestCase(StartFen, 3, 8902L)]
        [TestCase(StartFen, 4, 197281L)]
        [TestCase(KiwipeteFen, 1, 48L)]
        [TestCase(KiwipeteFen, 2, 2039L)]
        [TestCase(KiwipeteFen, 3, 97862L)]
        [TestCase(EndgameFen, 1, 14L)]
        [TestCase(EndgameFen, 2, 191L)]
        [TestCase(EndgameFen, 3, 2812L)]
        [TestCase(EndgameFen, 4, 43238L)]
        [TestCase(PromotionFen, 1, 6L)]
        [TestCase(PromotionFen, 2, 264L)]
        [TestCase(PromotionFen, 3, 9467L)]
        [TestCase(TacticalFen, 1, 44L)]
        [TestCase(TacticalFen, 2, 1486L)]
        [TestCase(TacticalFen, 3, 62379L)]
        public void Perft_MatchesTheStandardCounts(
            string fen,
            int depth,
            long expected)
        {
            ChessPosition position = ParseFen(fen);
            Assert.That(
                Perft(position, depth),
                Is.EqualTo(expected),
                $"perft({depth}) of '{fen}'");
        }

        [Test]
        public void Perft_LeavesThePositionExactlyAsItFoundIt()
        {
            ChessPosition position = ParseFen(KiwipeteFen);
            ulong before = position.ComputeKey();
            Perft(position, 3);
            Assert.That(
                position.ComputeKey(),
                Is.EqualTo(before),
                "Every made move must be taken back.");
        }

        [Test]
        public void FoolsMate_IsCheckmateRatherThanMerelyCheck()
        {
            var match = new ChessMatch();
            // 1. f3 e5 2. g4 Qh4#. The shortest way to lose a game of
            // chess, and the shortest proof that mate is detected.
            PlayCoordinateMove(match, "f2", "f3");
            PlayCoordinateMove(match, "e7", "e5");
            PlayCoordinateMove(match, "g2", "g4");
            PlayCoordinateMove(match, "d8", "h4");

            Assert.That(
                match.Status,
                Is.EqualTo(BoardGameStatus.DarkWins));
            Assert.That(
                match.Ending,
                Is.EqualTo(BoardGameEnding.Checkmate));
            Assert.That(match.LegalActions, Is.Empty);
        }

        [Test]
        public void Stalemate_IsADrawRatherThanALoss()
        {
            var match = new ChessMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (Square("a8"),
                        ChessPiece.Make(ChessPiece.King, false)),
                    (Square("c7"),
                        ChessPiece.Make(ChessPiece.King, true)),
                    (Square("b6"),
                        ChessPiece.Make(ChessPiece.Queen, true))
                },
                whiteToMove: false);
            match.ResynchroniseFromPosition();

            Assert.That(match.LegalActions, Is.Empty);
            Assert.That(match.CheckPending, Is.False);
            Assert.That(
                match.Status,
                Is.EqualTo(BoardGameStatus.Draw));
            Assert.That(
                match.Ending,
                Is.EqualTo(BoardGameEnding.Stalemate));
        }

        [Test]
        public void BareKings_AreADrawByInsufficientMaterial()
        {
            var match = new ChessMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (Square("a1"),
                        ChessPiece.Make(ChessPiece.King, true)),
                    (Square("h8"),
                        ChessPiece.Make(ChessPiece.King, false))
                },
                whiteToMove: true);
            match.ResynchroniseFromPosition();

            Assert.That(
                match.Status,
                Is.EqualTo(BoardGameStatus.Draw));
            Assert.That(
                match.Ending,
                Is.EqualTo(BoardGameEnding.InsufficientMaterial));
        }

        /// <summary>
        /// The presentation moves men by following the turn's steps, so
        /// a castle that reported only the king would leave the rook
        /// standing in the corner for the rest of the game.
        /// </summary>
        [Test]
        public void Castling_ReportsBothTheKingAndTheRook()
        {
            var match = new ChessMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (Square("e1"),
                        ChessPiece.Make(ChessPiece.King, true)),
                    (Square("h1"),
                        ChessPiece.Make(ChessPiece.Rook, true)),
                    (Square("e8"),
                        ChessPiece.Make(ChessPiece.King, false))
                },
                whiteToMove: true,
                castlingRights: ChessPosition.WhiteKingSide);
            match.ResynchroniseFromPosition();

            int action = FindAction(match, "e1", "g1");
            var turn = new BoardGameTurn();
            Assert.That(match.TryApply(action, turn), Is.True);
            Assert.That(turn.Steps, Has.Count.EqualTo(2));
            Assert.That(
                turn.EndFile,
                Is.EqualTo(LatticeFile("g1")),
                "The mover is the king, not the rook that followed.");
            Assert.That(
                turn.EndRank,
                Is.EqualTo(LatticeRank("g1")));
            Assert.That(
                turn.Steps[1].FromFile,
                Is.EqualTo(LatticeFile("h1")));
            Assert.That(
                turn.Steps[1].ToFile,
                Is.EqualTo(LatticeFile("f1")));
            Assert.That(turn.CapturedAnything, Is.False);
        }

        /// <summary>
        /// En passant takes a man off a square the mover never lands
        /// on. A presentation that assumed otherwise would leave a ghost
        /// pawn standing.
        /// </summary>
        [Test]
        public void EnPassant_ReportsTheCaptureBehindTheDestination()
        {
            var match = new ChessMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (Square("e1"),
                        ChessPiece.Make(ChessPiece.King, true)),
                    (Square("e8"),
                        ChessPiece.Make(ChessPiece.King, false)),
                    (Square("e5"),
                        ChessPiece.Make(ChessPiece.Pawn, true)),
                    (Square("d7"),
                        ChessPiece.Make(ChessPiece.Pawn, false))
                },
                whiteToMove: false);
            match.ResynchroniseFromPosition();

            PlayCoordinateMove(match, "d7", "d5");
            int action = FindAction(match, "e5", "d6");
            var turn = new BoardGameTurn();
            Assert.That(match.TryApply(action, turn), Is.True);
            Assert.That(turn.Steps, Has.Count.EqualTo(1));
            Assert.That(turn.Steps[0].HasCapture, Is.True);
            Assert.That(
                turn.Steps[0].CapturedRank,
                Is.EqualTo(LatticeRank("d5")),
                "The pawn is lifted off d5, not off d6.");
            Assert.That(
                turn.Steps[0].CapturedFile,
                Is.EqualTo(LatticeFile("d5")));
        }

        [Test]
        public void UnderPromotion_IsNotOffered()
        {
            var match = new ChessMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (Square("e1"),
                        ChessPiece.Make(ChessPiece.King, true)),
                    (Square("e8"),
                        ChessPiece.Make(ChessPiece.King, false)),
                    (Square("a7"),
                        ChessPiece.Make(ChessPiece.Pawn, true))
                },
                whiteToMove: true);
            match.ResynchroniseFromPosition();

            int promotions = 0;
            for (int index = 0;
                 index < match.LegalActions.Count;
                 index++)
            {
                if (match.LegalActions[index].IsPromotion)
                {
                    promotions++;
                }
            }

            Assert.That(
                promotions,
                Is.EqualTo(1),
                "a8 is offered once, as a queen.");

            int action = FindAction(match, "a7", "a8");
            var turn = new BoardGameTurn();
            Assert.That(match.TryApply(action, turn), Is.True);
            Assert.That(turn.Promoted, Is.True);
            Assert.That(turn.PromotedToCrown, Is.False);
            Assert.That(
                turn.PromotedKind,
                Is.EqualTo(CityChessPieceKind.Queen));
        }

        // ---- helpers ----------------------------------------------

        internal static long Perft(ChessPosition position, int depth)
        {
            if (depth <= 0)
            {
                return 1;
            }

            var moves = new List<ChessMove>(72);
            position.GenerateLegal(moves);
            if (depth == 1)
            {
                return moves.Count;
            }

            long nodes = 0;
            for (int index = 0; index < moves.Count; index++)
            {
                ChessMove move = moves[index];
                ChessUndo undo = position.MakeMove(move);
                nodes += Perft(position, depth - 1);
                position.UnmakeMove(move, undo);
            }

            return nodes;
        }

        internal static int Square(string coordinate)
        {
            int file = coordinate[0] - 'a';
            int rank = coordinate[1] - '1';
            return ChessPosition.Square(file, rank);
        }

        internal static int LatticeFile(string coordinate)
        {
            return ChessMatch.MirrorFile(coordinate[0] - 'a');
        }

        internal static int LatticeRank(string coordinate)
        {
            return coordinate[1] - '1';
        }

        internal static int FindAction(
            ChessMatch match,
            string from,
            string to)
        {
            for (int index = 0;
                 index < match.LegalActions.Count;
                 index++)
            {
                BoardGameAction action = match.LegalActions[index];
                if (action.FromFile == LatticeFile(from) &&
                    action.FromRank == LatticeRank(from) &&
                    action.ToFile == LatticeFile(to) &&
                    action.ToRank == LatticeRank(to))
                {
                    return action.Index;
                }
            }

            Assert.Fail($"{from}{to} is not legal here.");
            return -1;
        }

        internal static void PlayCoordinateMove(
            ChessMatch match,
            string from,
            string to)
        {
            var turn = new BoardGameTurn();
            Assert.That(
                match.TryApply(FindAction(match, from, to), turn),
                Is.True,
                $"{from}{to} was refused.");
        }

        /// <summary>
        /// Hands a position built elsewhere to a match, through the
        /// public setup path the match owns its board behind.
        /// </summary>
        internal static void CopyPosition(
            ChessPosition source,
            ChessMatch target)
        {
            var placement = new List<(int, byte)>(32);
            for (int square = 0; square < 64; square++)
            {
                if (source[square] != ChessPiece.None)
                {
                    placement.Add((square, source[square]));
                }
            }

            target.Position.SetUp(
                placement,
                source.WhiteToMove,
                source.CastlingRights,
                source.EnPassantSquare);
            target.ResynchroniseFromPosition();
        }

        internal static ChessPosition ParseFen(string fen)
        {
            string[] parts = fen.Split(' ');
            var placement = new List<(int, byte)>(32);
            int file = 0;
            int rank = 7;
            foreach (char symbol in parts[0])
            {
                if (symbol == '/')
                {
                    rank--;
                    file = 0;
                    continue;
                }

                if (symbol >= '1' && symbol <= '8')
                {
                    file += symbol - '0';
                    continue;
                }

                bool white = char.IsUpper(symbol);
                byte type;
                switch (char.ToLowerInvariant(symbol))
                {
                    case 'p':
                        type = ChessPiece.Pawn;
                        break;
                    case 'n':
                        type = ChessPiece.Knight;
                        break;
                    case 'b':
                        type = ChessPiece.Bishop;
                        break;
                    case 'r':
                        type = ChessPiece.Rook;
                        break;
                    case 'q':
                        type = ChessPiece.Queen;
                        break;
                    default:
                        type = ChessPiece.King;
                        break;
                }

                placement.Add((
                    ChessPosition.Square(file, rank),
                    ChessPiece.Make(type, white)));
                file++;
            }

            byte rights = 0;
            if (parts.Length > 2 && parts[2] != "-")
            {
                foreach (char symbol in parts[2])
                {
                    switch (symbol)
                    {
                        case 'K':
                            rights |= ChessPosition.WhiteKingSide;
                            break;
                        case 'Q':
                            rights |= ChessPosition.WhiteQueenSide;
                            break;
                        case 'k':
                            rights |= ChessPosition.BlackKingSide;
                            break;
                        case 'q':
                            rights |= ChessPosition.BlackQueenSide;
                            break;
                    }
                }
            }

            int enPassant = -1;
            if (parts.Length > 3 && parts[3] != "-")
            {
                enPassant = Square(parts[3]);
            }

            var position = new ChessPosition();
            position.SetUp(
                placement,
                parts.Length > 1 && parts[1] == "w",
                rights,
                enPassant);
            return position;
        }
    }
}
