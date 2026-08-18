using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Russian draughts on the park's right-hand board.
    ///
    /// Every rule pinned here is one a player would notice immediately
    /// if it were missing, and most of them are the ones an
    /// implementation written from memory gets wrong: taking is
    /// compulsory, a man takes backwards as well as forwards, a chain
    /// runs to its end rather than stopping where it likes, a king
    /// flies, a man taken stays on the board until the move is over and
    /// cannot be taken twice, and a man crowned in the middle of a chain
    /// finishes that chain as a king.
    /// </summary>
    public sealed class DraughtsRulesTests
    {
        [Test]
        public void Opening_IsTheSetTheTwoOldMenLeftOut()
        {
            var match = new DraughtsMatch();
            var expected = new HashSet<int>();
            IReadOnlyList<CityChessManPlacement> drawn =
                CityChessSetPlan.Create(
                    GameSessionState.DefaultCitySeed);
            int lightDrawn = 0;
            for (int index = 0; index < drawn.Count; index++)
            {
                CityChessManPlacement man = drawn[index];
                if (man.Table != CityChessSetPlan.DraughtsTable)
                {
                    continue;
                }

                expected.Add(Key(man.File, man.Rank, man.IsLight));
                if (man.IsLight)
                {
                    lightDrawn++;
                }
            }

            Assert.That(
                expected.Count,
                Is.EqualTo(CityChessSetPlan.DraughtsPerSide * 2),
                "The drawn set is twelve a side.");
            Assert.That(
                lightDrawn,
                Is.EqualTo(CityChessSetPlan.DraughtsPerSide));

            IReadOnlyList<BoardGamePiecePlacement> live =
                match.Pieces;
            Assert.That(live, Has.Count.EqualTo(expected.Count));
            for (int index = 0; index < live.Count; index++)
            {
                BoardGamePiecePlacement piece = live[index];
                Assert.That(
                    expected.Contains(
                        Key(piece.File, piece.Rank, piece.IsLight)),
                    Is.True,
                    $"A live man at {piece.File},{piece.Rank} stands " +
                    "where the drawn set had nobody.");
                Assert.That(piece.IsCrowned, Is.False);
                Assert.That(
                    piece.Kind,
                    Is.EqualTo(CityChessPieceKind.Draught));
            }

            Assert.That(
                match.SideToMove,
                Is.EqualTo(BoardGameSide.Light),
                "The old man opposite opens.");
        }

        [Test]
        public void Taking_IsCompulsoryAndShutsOutQuietMoves()
        {
            DraughtsMoveList moves = Generate(
                lightToMove: false,
                (2, 3, DraughtsPiece.DarkMan),
                (3, 4, DraughtsPiece.LightMan),
                (0, 1, DraughtsPiece.DarkMan),
                (7, 6, DraughtsPiece.LightMan));

            Assert.That(moves.Count, Is.EqualTo(1));
            Assert.That(moves[0].IsCapture, Is.True);
            Assert.That(moves[0].From, Is.EqualTo(Square(2, 3)));
            Assert.That(
                moves.Destination(moves[0]),
                Is.EqualTo(Square(4, 5)));
        }

        [Test]
        public void AMan_TakesBackwardsAsWellAsForwards()
        {
            DraughtsMoveList moves = Generate(
                lightToMove: false,
                (4, 5, DraughtsPiece.DarkMan),
                (3, 4, DraughtsPiece.LightMan),
                (7, 6, DraughtsPiece.LightMan));

            Assert.That(moves.Count, Is.EqualTo(1));
            Assert.That(moves[0].IsCapture, Is.True);
            Assert.That(
                moves.Destination(moves[0]),
                Is.EqualTo(Square(2, 3)),
                "Dark walks up the board and takes back down it.");
        }

        [Test]
        public void AChain_RunsToItsEndRatherThanStoppingHalfway()
        {
            DraughtsMoveList moves = Generate(
                lightToMove: false,
                (0, 1, DraughtsPiece.DarkMan),
                (1, 2, DraughtsPiece.LightMan),
                (3, 4, DraughtsPiece.LightMan),
                (7, 0, DraughtsPiece.LightMan));

            Assert.That(moves.Count, Is.EqualTo(1));
            Assert.That(moves[0].CaptureCount, Is.EqualTo(2));
            Assert.That(moves[0].StepCount, Is.EqualTo(2));
            Assert.That(
                moves.Landing(moves[0], 0),
                Is.EqualTo(Square(2, 3)));
            Assert.That(
                moves.Destination(moves[0]),
                Is.EqualTo(Square(4, 5)));
        }

        [Test]
        public void AKing_FliesTheWholeDiagonal()
        {
            DraughtsMoveList moves = Generate(
                lightToMove: false,
                (1, 0, DraughtsPiece.DarkKing),
                (0, 7, DraughtsPiece.LightMan));

            Assert.That(
                moves.Count,
                Is.EqualTo(7),
                "One square one way and six the other.");
            for (int index = 0; index < moves.Count; index++)
            {
                Assert.That(moves[index].IsCapture, Is.False);
            }
        }

        [Test]
        public void AKing_MayLandAnywherePastTheManHeTook()
        {
            DraughtsMoveList moves = Generate(
                lightToMove: false,
                (1, 0, DraughtsPiece.DarkKing),
                (4, 3, DraughtsPiece.LightMan));

            Assert.That(moves.Count, Is.EqualTo(3));
            var landings = new HashSet<int>();
            for (int index = 0; index < moves.Count; index++)
            {
                Assert.That(moves[index].CaptureCount, Is.EqualTo(1));
                landings.Add(moves.Destination(moves[index]));
            }

            Assert.That(
                landings,
                Is.EquivalentTo(new[]
                {
                    Square(5, 4),
                    Square(6, 5),
                    Square(7, 6)
                }));
        }

        /// <summary>
        /// The Turkish strike. A man that has been jumped stays where he
        /// is until the move is over, so he cannot be jumped a second
        /// time — and a king that ignored that would loop over the same
        /// man forever.
        /// </summary>
        [Test]
        public void AManTaken_IsNeverTakenTwice()
        {
            DraughtsMoveList moves = Generate(
                lightToMove: false,
                (1, 0, DraughtsPiece.DarkKing),
                (3, 2, DraughtsPiece.LightMan));

            Assert.That(moves.Count, Is.GreaterThan(0));
            for (int index = 0; index < moves.Count; index++)
            {
                Assert.That(
                    moves[index].CaptureCount,
                    Is.EqualTo(1),
                    "There is one man on the board to take.");
            }
        }

        /// <summary>
        /// Five jumps around a ring, which is what the compulsory-chain
        /// rule is actually for. The count is what matters: a generator
        /// that stopped early, or one that let a taken man be taken
        /// again, produces a different number.
        /// </summary>
        [Test]
        public void ALongChain_IsFoundWhole()
        {
            DraughtsMoveList moves = Generate(
                lightToMove: false,
                (1, 4, DraughtsPiece.DarkMan),
                (2, 3, DraughtsPiece.LightMan),
                (4, 1, DraughtsPiece.LightMan),
                (6, 1, DraughtsPiece.LightMan),
                (6, 3, DraughtsPiece.LightMan),
                (4, 3, DraughtsPiece.LightMan));

            int best = 0;
            for (int index = 0; index < moves.Count; index++)
            {
                best = Larger(best, moves[index].CaptureCount);
                AssertNoRepeatedVictim(moves, moves[index]);
            }

            Assert.That(
                best,
                Is.EqualTo(5),
                "Every man in the ring comes off in one move.");
        }

        [Test]
        public void AManCrownedMidChain_FinishesItAsAKing()
        {
            DraughtsMoveList moves = Generate(
                lightToMove: false,
                (2, 5, DraughtsPiece.DarkMan),
                (3, 6, DraughtsPiece.LightMan),
                (6, 5, DraughtsPiece.LightMan));

            Assert.That(moves.Count, Is.EqualTo(1));
            DraughtsMove move = moves[0];
            Assert.That(
                move.CaptureCount,
                Is.EqualTo(2),
                "A man could not have reached the second one; a king " +
                "flies to it.");
            Assert.That(move.Crowns, Is.True);
            Assert.That(
                moves.Destination(move),
                Is.EqualTo(Square(7, 4)));
        }

        [Test]
        public void AManCrownedOnAQuietMove_IsStillCrowned()
        {
            var position = new DraughtsPosition();
            position.SetUp(
                new List<(int, byte)>
                {
                    (Square(1, 6), DraughtsPiece.DarkMan),
                    (Square(0, 1), DraughtsPiece.LightMan)
                },
                lightToMove: false);
            var moves = new DraughtsMoveList();
            position.Generate(moves);

            int crowning = -1;
            for (int index = 0; index < moves.Count; index++)
            {
                if (moves[index].Crowns)
                {
                    crowning = index;
                }
            }

            Assert.That(crowning, Is.GreaterThanOrEqualTo(0));
            position.Apply(moves, crowning);
            Assert.That(
                DraughtsPiece.IsKing(
                    position[moves.Destination(moves[crowning])]),
                Is.True);
        }

        [Test]
        public void NoMove_LosesTheGame()
        {
            var match = new DraughtsMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    // Dark is boxed into the corner by two light men
                    // and has nothing to take.
                    (Square(0, 7), DraughtsPiece.DarkMan),
                    (Square(1, 6), DraughtsPiece.LightMan),
                    (Square(2, 5), DraughtsPiece.LightMan)
                },
                lightToMove: false);
            match.ResynchroniseFromPosition();

            Assert.That(match.LegalActions, Is.Empty);
            Assert.That(
                match.Status,
                Is.EqualTo(BoardGameStatus.LightWins));
            Assert.That(
                match.Ending,
                Is.EqualTo(BoardGameEnding.Blocked));
        }

        [Test]
        public void AnEmptySide_LosesTheGame()
        {
            var match = new DraughtsMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (Square(0, 1), DraughtsPiece.LightMan)
                },
                lightToMove: false);
            match.ResynchroniseFromPosition();

            Assert.That(
                match.Status,
                Is.EqualTo(BoardGameStatus.LightWins));
            Assert.That(
                match.Ending,
                Is.EqualTo(BoardGameEnding.Swept));
        }

        /// <summary>
        /// A chain has to reach the presentation as one carry per jump,
        /// with the man it lifted named on each of them, or the board
        /// keeps men that are no longer in the game.
        /// </summary>
        [Test]
        public void AnAppliedChain_ReportsEveryCarryAndEveryVictim()
        {
            var match = new DraughtsMatch();
            match.Position.SetUp(
                new List<(int, byte)>
                {
                    (Square(0, 1), DraughtsPiece.DarkMan),
                    (Square(1, 2), DraughtsPiece.LightMan),
                    (Square(3, 4), DraughtsPiece.LightMan),
                    (Square(7, 0), DraughtsPiece.LightMan)
                },
                lightToMove: false);
            match.ResynchroniseFromPosition();

            var turn = new BoardGameTurn();
            Assert.That(match.LegalActions, Has.Count.EqualTo(1));
            Assert.That(match.TryApply(0, turn), Is.True);
            Assert.That(turn.Steps, Has.Count.EqualTo(2));
            Assert.That(turn.Steps[0].HasCapture, Is.True);
            Assert.That(turn.Steps[0].CapturedFile, Is.EqualTo(1));
            Assert.That(turn.Steps[0].CapturedRank, Is.EqualTo(2));
            Assert.That(turn.Steps[1].HasCapture, Is.True);
            Assert.That(turn.Steps[1].CapturedFile, Is.EqualTo(3));
            Assert.That(turn.Steps[1].CapturedRank, Is.EqualTo(4));
            Assert.That(turn.EndFile, Is.EqualTo(4));
            Assert.That(turn.EndRank, Is.EqualTo(5));
            Assert.That(match.Pieces, Has.Count.EqualTo(2));
        }

        // ---- helpers ----------------------------------------------

        private static int Square(int file, int rank)
        {
            return DraughtsPosition.Square(file, rank);
        }

        private static int Key(int file, int rank, bool light)
        {
            return (rank * 8 + file) * 2 + (light ? 1 : 0);
        }

        private static int Larger(int left, int right)
        {
            return left > right ? left : right;
        }

        private static DraughtsMoveList Generate(
            bool lightToMove,
            params (int file, int rank, byte piece)[] placement)
        {
            var squares = new List<(int, byte)>(placement.Length);
            for (int index = 0; index < placement.Length; index++)
            {
                (int file, int rank, byte piece) = placement[index];
                Assert.That(
                    CityChessBoardGeometry.IsDarkSquare(file, rank),
                    Is.True,
                    $"{file},{rank} is a light square; nothing ever " +
                    "stands there.");
                squares.Add((Square(file, rank), piece));
            }

            var position = new DraughtsPosition();
            position.SetUp(squares, lightToMove);
            var moves = new DraughtsMoveList();
            position.Generate(moves);
            return moves;
        }

        private static void AssertNoRepeatedVictim(
            DraughtsMoveList moves,
            DraughtsMove move)
        {
            var seen = new HashSet<byte>();
            for (int step = 0; step < move.StepCount; step++)
            {
                byte victim = moves.Victim(move, step);
                if (victim == DraughtsMoveList.NoCapture)
                {
                    continue;
                }

                Assert.That(
                    seen.Add(victim),
                    Is.True,
                    "The same man was taken twice in one move.");
            }
        }
    }
}
