using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The opposition. The brief is a middling old man rather than a
    /// strong engine, so what is pinned is the floor rather than the
    /// ceiling: he never misses a mate in one, he never leaves a queen
    /// standing when he can take it for nothing, he plays only legal
    /// moves, and a whole game against himself reaches a result without
    /// the board losing track of a single man.
    ///
    /// The engine settings are turned down here so a full game runs in
    /// a test rather than in a minute. Strength is not what is being
    /// measured.
    /// </summary>
    public sealed class BoardGameEngineTests
    {
        private const int MaximumPlies = 400;

        [Test]
        public void ChessOpening_IsTheSetTheTwoOldMenLeftOut()
        {
            var match = new ChessMatch();
            var expected =
                new Dictionary<int, CityChessManPlacement>(32);
            IReadOnlyList<CityChessManPlacement> drawn =
                CityChessSetPlan.Create(
                    GameSessionState.DefaultCitySeed);
            for (int index = 0; index < drawn.Count; index++)
            {
                CityChessManPlacement man = drawn[index];
                if (man.Table == CityChessSetPlan.ChessTable)
                {
                    expected[man.Rank * 8 + man.File] = man;
                }
            }

            Assert.That(
                expected,
                Has.Count.EqualTo(CityChessSetPlan.ChessMenPerSide * 2));
            Assert.That(
                match.Pieces,
                Has.Count.EqualTo(expected.Count));

            for (int index = 0; index < match.Pieces.Count; index++)
            {
                BoardGamePiecePlacement piece = match.Pieces[index];
                int key = piece.Rank * 8 + piece.File;
                Assert.That(
                    expected.ContainsKey(key),
                    Is.True,
                    $"A live man at {piece.File},{piece.Rank} stands " +
                    "where the drawn set had nobody.");
                CityChessManPlacement man = expected[key];
                Assert.That(
                    piece.Kind,
                    Is.EqualTo(man.Kind),
                    $"The man on {piece.File},{piece.Rank} changed " +
                    "shape when the game started.");
                Assert.That(piece.IsLight, Is.EqualTo(man.IsLight));
            }

            Assert.That(
                match.SideToMove,
                Is.EqualTo(BoardGameSide.Light));
        }

        [Test]
        public void TheOpponent_NeverMissesAMateInOne()
        {
            var match = new ChessMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (ChessRulesTests.Square("h8"),
                        ChessPiece.Make(ChessPiece.King, false)),
                    (ChessRulesTests.Square("a7"),
                        ChessPiece.Make(ChessPiece.Rook, true)),
                    (ChessRulesTests.Square("b1"),
                        ChessPiece.Make(ChessPiece.Rook, true)),
                    (ChessRulesTests.Square("e1"),
                        ChessPiece.Make(ChessPiece.King, true))
                },
                whiteToMove: true);
            match.ResynchroniseFromPosition();

            // Ten different seeds, because the last stage of the search
            // picks at random among moves within a slack window and a
            // mate that only survives one seed has not survived at all.
            for (uint seed = 1; seed <= 10; seed++)
            {
                var probe = new ChessMatch();
                ChessRulesTests.CopyPosition(match.Position, probe);
                uint state = seed * 2654435761u;
                int action = probe.ChooseAction(ref state);
                Assert.That(action, Is.GreaterThanOrEqualTo(0));

                var turn = new BoardGameTurn();
                Assert.That(probe.TryApply(action, turn), Is.True);
                Assert.That(
                    probe.Status,
                    Is.EqualTo(BoardGameStatus.LightWins),
                    $"Seed {seed} did not play Rb8 mate.");
                Assert.That(
                    probe.Ending,
                    Is.EqualTo(BoardGameEnding.Checkmate));
            }
        }

        [Test]
        public void TheOpponent_TakesAQueenThatIsStandingThereForFree()
        {
            var match = new ChessMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (ChessRulesTests.Square("h1"),
                        ChessPiece.Make(ChessPiece.King, true)),
                    (ChessRulesTests.Square("a1"),
                        ChessPiece.Make(ChessPiece.Rook, true)),
                    (ChessRulesTests.Square("h8"),
                        ChessPiece.Make(ChessPiece.King, false)),
                    (ChessRulesTests.Square("a8"),
                        ChessPiece.Make(ChessPiece.Queen, false))
                },
                whiteToMove: true);
            match.ResynchroniseFromPosition();

            for (uint seed = 1; seed <= 10; seed++)
            {
                var probe = new ChessMatch();
                ChessRulesTests.CopyPosition(match.Position, probe);
                uint state = seed * 40503u + 7u;
                int action = probe.ChooseAction(ref state);
                BoardGameAction chosen = probe.LegalActions[action];
                Assert.That(
                    chosen.FromFile,
                    Is.EqualTo(ChessRulesTests.LatticeFile("a1")));
                Assert.That(
                    chosen.ToRank,
                    Is.EqualTo(ChessRulesTests.LatticeRank("a8")),
                    $"Seed {seed} left the queen standing.");
                Assert.That(chosen.IsCapture, Is.True);
            }
        }

        [Test]
        public void AWholeGameOfChess_StaysLegalAndKeepsItsMenStraight()
        {
            var match = new ChessMatch
            {
                Settings = new ChessEngineSettings(2, 6000, 35, 11, 140)
            };
            PlayOut(match, out int plies);
            Assert.That(
                plies,
                Is.GreaterThan(10),
                "A game that ends in ten plies is a broken board, not " +
                "a short game.");
        }

        /// <summary>
        /// Draughts always finishes. Taking is compulsory, so material
        /// only ever falls, and a pair of kings shuffling runs into the
        /// quiet-move limit.
        /// </summary>
        [Test]
        public void AWholeGameOfDraughts_ReachesAResult()
        {
            var match = new DraughtsMatch
            {
                Settings =
                    new DraughtsEngineSettings(3, 8000, 40, 10, 130)
            };
            PlayOut(match, out int plies);
            Assert.That(
                match.Status,
                Is.Not.EqualTo(BoardGameStatus.Playing),
                $"Still playing after {plies} plies.");
        }

        /// <summary>
        /// Plays a match out against itself, checking after every move
        /// that the board is still a board: no two men on a square, no
        /// man conjured out of nothing, and every offered action legal
        /// enough to be applied.
        /// </summary>
        private static void PlayOut(
            IBoardGameMatch match,
            out int plies)
        {
            var turn = new BoardGameTurn();
            var occupied = new HashSet<int>();
            uint state = 0x5EED1234u;
            int previousCount = match.Pieces.Count;
            plies = 0;

            while (match.Status == BoardGameStatus.Playing &&
                   plies < MaximumPlies)
            {
                Assert.That(
                    match.LegalActions,
                    Is.Not.Empty,
                    "A playing board always offers a move.");
                int action = match.ChooseAction(ref state);
                Assert.That(action, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    match.TryApply(action, turn),
                    Is.True,
                    $"The engine offered an illegal move at ply " +
                    $"{plies}.");
                plies++;

                Assert.That(
                    match.Pieces.Count,
                    Is.LessThanOrEqualTo(previousCount),
                    "Men are never conjured onto the board.");
                previousCount = match.Pieces.Count;

                occupied.Clear();
                for (int index = 0; index < match.Pieces.Count; index++)
                {
                    BoardGamePiecePlacement piece = match.Pieces[index];
                    Assert.That(
                        occupied.Add(piece.Rank * 8 + piece.File),
                        Is.True,
                        "Two men ended up on one square.");
                    Assert.That(piece.File, Is.InRange(0, 7));
                    Assert.That(piece.Rank, Is.InRange(0, 7));
                }

                Assert.That(
                    turn.Steps,
                    Is.Not.Empty,
                    "Every move moves something.");
                Assert.That(
                    turn.EndFile,
                    Is.InRange(0, 7),
                    "And ends somewhere on the board.");
            }
        }
    }
}
