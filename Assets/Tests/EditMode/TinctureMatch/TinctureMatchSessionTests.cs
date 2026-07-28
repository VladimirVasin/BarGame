using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class TinctureMatchSessionTests
    {
        [Test]
        public void InvalidSwapChangesNothingAndReturnsReason()
        {
            var session = new TinctureMatchSession(11235);
            TinctureMatchBoard before = session.Board;
            TinctureMatchSwap invalid = FindInvalidAdjacentSwap(before);

            bool accepted = session.TrySwap(
                invalid.First,
                invalid.Second,
                out TinctureMatchMoveResult result);

            Assert.That(accepted, Is.False);
            Assert.That(result.Accepted, Is.False);
            Assert.That(
                result.RejectionReason,
                Is.EqualTo(
                    TinctureMatchMoveRejectionReason.NoMatchCreated));
            Assert.That(session.MovesCompleted, Is.Zero);
            Assert.That(session.MovesRemaining, Is.EqualTo(15));
            Assert.That(session.Score, Is.Zero);
            Assert.That(session.Board, Is.SameAs(before));
            Assert.That(result.BoardBeforeSwap, Is.SameAs(before));
            Assert.That(result.BoardAfterSwap, Is.SameAs(before));
            Assert.That(result.BoardFinal, Is.SameAs(before));
            Assert.That(result.Waves, Is.Empty);
        }

        [Test]
        public void RejectedInputDoesNotAdvanceDeterministicRandomStream()
        {
            var withRejectedInput =
                new TinctureMatchSession(78219);
            var cleanReplay =
                new TinctureMatchSession(78219);
            TinctureMatchSwap invalid =
                FindInvalidAdjacentSwap(withRejectedInput.Board);
            Assert.That(
                withRejectedInput.TrySwap(
                    invalid.First,
                    invalid.Second,
                    out _),
                Is.False);

            TinctureMatchSwap valid =
                TinctureMatchResolver
                    .GetLegalNormalSwaps(cleanReplay.Board)[0];
            Assert.That(
                withRejectedInput.TrySwap(
                    valid.First,
                    valid.Second,
                    out TinctureMatchMoveResult afterRejected),
                Is.True);
            Assert.That(
                cleanReplay.TrySwap(
                    valid.First,
                    valid.Second,
                    out TinctureMatchMoveResult clean),
                Is.True);

            Assert.That(afterRejected.BoardFinal, Is.EqualTo(clean.BoardFinal));
            Assert.That(afterRejected.ScoreAwarded, Is.EqualTo(clean.ScoreAwarded));
            Assert.That(afterRejected.CascadeDepth, Is.EqualTo(clean.CascadeDepth));
        }

        [Test]
        public void FourRunCreatesSingleMoonshineAndAwardsCreationScore()
        {
            var session = new TinctureMatchSession(
                19,
                TinctureMatchTestBoards.LongMatchBoard());

            bool accepted = session.TrySwap(
                new TinctureMatchCell(1, 1),
                new TinctureMatchCell(1, 2),
                out TinctureMatchMoveResult result);

            Assert.That(accepted, Is.True);
            Assert.That(result.CreatedMoonshine, Is.True);
            Assert.That(result.ActivatedMoonshine, Is.False);
            Assert.That(result.Waves[0].CreatedMoonshine, Is.True);
            Assert.That(
                result.Waves[0].CreatedMoonshineCell,
                Is.EqualTo(new TinctureMatchCell(1, 2)));
            Assert.That(result.Waves[0].ClearedTileCount, Is.EqualTo(3));
            Assert.That(result.Waves[0].ClearScore, Is.EqualTo(30));
            Assert.That(result.Waves[0].LongRunBonus, Is.EqualTo(20));
            Assert.That(
                result.Waves[0].SpecialBonus,
                Is.EqualTo(
                    TinctureMatchSession.MoonshineCreationBonus));
            Assert.That(result.Waves[0].ScoreAwarded, Is.EqualTo(100));
            Assert.That(
                result.BoardFinal.CountTiles(
                    TinctureTileKind.Moonshine),
                Is.EqualTo(1));
        }

        [Test]
        public void CrossPatternCreatesMoonshineAtIntersection()
        {
            var session = new TinctureMatchSession(
                73,
                TinctureMatchTestBoards.LongMatchBoard());

            Assert.That(
                session.TrySwap(
                    new TinctureMatchCell(2, 0),
                    new TinctureMatchCell(2, 1),
                    out TinctureMatchMoveResult result),
                Is.True);

            TinctureMatchWaveResult first = result.Waves[0];
            Assert.That(first.MatchedRuns.Count, Is.EqualTo(3));
            Assert.That(first.CreatedMoonshine, Is.True);
            Assert.That(
                first.CreatedMoonshineCell,
                Is.EqualTo(new TinctureMatchCell(2, 1)));
            Assert.That(first.ClearedTileCount, Is.EqualTo(8));
            Assert.That(
                first.ClearedTiles
                    .Select(tile => tile.Cell)
                    .Distinct()
                    .Count(),
                Is.EqualTo(first.ClearedTileCount));
        }

        [Test]
        public void ExistingMoonshinePreventsSecondAndAwardsFallbackBonus()
        {
            var session = new TinctureMatchSession(
                91,
                TinctureMatchTestBoards.LongMatchBoard(true));

            Assert.That(
                session.TrySwap(
                    new TinctureMatchCell(1, 1),
                    new TinctureMatchCell(1, 2),
                    out TinctureMatchMoveResult result),
                Is.True);

            TinctureMatchWaveResult first = result.Waves[0];
            Assert.That(result.CreatedMoonshine, Is.False);
            Assert.That(first.ClearedTileCount, Is.EqualTo(4));
            Assert.That(first.ClearScore, Is.EqualTo(40));
            Assert.That(first.LongRunBonus, Is.EqualTo(20));
            Assert.That(
                first.SpecialBonus,
                Is.EqualTo(
                    TinctureMatchSession
                        .ExistingMoonshineMatchBonus));
            Assert.That(
                result.BoardFinal.CountTiles(
                    TinctureTileKind.Moonshine),
                Is.EqualTo(1));
        }

        [Test]
        public void MoonshineSwapClearsChosenKindAndItself()
        {
            var session = new TinctureMatchSession(55119);
            TinctureMatchCell moonshine =
                FindTile(session.Board, TinctureTileKind.Moonshine);
            TinctureMatchCell neighbour =
                FindNormalNeighbour(session.Board, moonshine);
            TinctureTileKind activatedKind =
                session.Board[
                    neighbour.Row,
                    neighbour.Column];
            int expectedCleared =
                session.Board.CountTiles(activatedKind) + 1;

            Assert.That(
                session.TrySwap(
                    moonshine,
                    neighbour,
                    out TinctureMatchMoveResult result),
                Is.True);

            TinctureMatchWaveResult activation = result.Waves[0];
            Assert.That(result.ActivatedMoonshine, Is.True);
            Assert.That(result.ActivatedKind, Is.EqualTo(activatedKind));
            Assert.That(activation.ActivatedMoonshine, Is.True);
            Assert.That(activation.ActivatedKind, Is.EqualTo(activatedKind));
            Assert.That(
                activation.ClearedTileCount,
                Is.EqualTo(expectedCleared));
            Assert.That(
                activation.ClearScore,
                Is.EqualTo(
                    expectedCleared *
                    TinctureMatchSession.PointsPerClearedTile));
            Assert.That(
                activation.SpecialBonus,
                Is.EqualTo(
                    TinctureMatchSession.MoonshineActivationBonus));
            Assert.That(
                result.BoardFinal.CountTiles(
                    TinctureTileKind.Moonshine),
                Is.Zero);
        }

        [Test]
        public void AcceptedMoveReturnsCompleteImmutableAnimationSnapshots()
        {
            var session = new TinctureMatchSession(
                120,
                TinctureMatchTestBoards.LongMatchBoard());
            TinctureMatchBoard initial = session.Board;
            var from = new TinctureMatchCell(1, 1);
            var to = new TinctureMatchCell(1, 2);
            TinctureTileKind fromKind = initial[from.Row, from.Column];
            TinctureTileKind toKind = initial[to.Row, to.Column];

            Assert.That(
                session.TrySwap(
                    from,
                    to,
                    out TinctureMatchMoveResult result),
                Is.True);

            Assert.That(result.BoardBeforeSwap, Is.SameAs(initial));
            Assert.That(
                result.BoardAfterSwap[from.Row, from.Column],
                Is.EqualTo(toKind));
            Assert.That(
                result.BoardAfterSwap[to.Row, to.Column],
                Is.EqualTo(fromKind));
            Assert.That(
                result.Waves[0].BoardBeforeClear,
                Is.EqualTo(result.BoardAfterSwap));
            Assert.That(
                result.Waves[0].BoardAfterClear.CountTiles(
                    TinctureTileKind.Empty),
                Is.GreaterThan(0));
            Assert.That(
                result.Waves[0].BoardAfterGravity.CountTiles(
                    TinctureTileKind.Empty),
                Is.GreaterThan(0));
            Assert.That(
                result.Waves[0].BoardAfterRefill.CountTiles(
                    TinctureTileKind.Empty),
                Is.Zero);
            Assert.That(result.BoardFinal, Is.EqualTo(session.Board));
        }

        [Test]
        public void WaveScoringUsesUniqueCellsAndCappedDepthMultiplier()
        {
            TinctureMatchMoveResult cascade = FindCascadeResult();
            int expectedMoveScore = 0;

            foreach (TinctureMatchWaveResult wave in cascade.Waves)
            {
                int expectedMultiplier = Math.Min(
                    wave.Depth,
                    TinctureMatchSession.MaximumCascadeMultiplier);
                int expectedLongBonus = wave.MatchedRuns.Sum(
                    run => Math.Max(0, run.Length - 3) *
                           TinctureMatchSession.PointsPerLongRunCell);
                Assert.That(
                    wave.CascadeMultiplier,
                    Is.EqualTo(expectedMultiplier));
                Assert.That(
                    wave.ClearScore,
                    Is.EqualTo(
                        wave.ClearedTileCount *
                        TinctureMatchSession.PointsPerClearedTile *
                        expectedMultiplier));
                Assert.That(
                    wave.LongRunBonus,
                    Is.EqualTo(expectedLongBonus));
                Assert.That(
                    wave.ClearedTiles
                        .Select(tile => tile.Cell)
                        .Distinct()
                        .Count(),
                    Is.EqualTo(wave.ClearedTileCount));
                expectedMoveScore += wave.ScoreAwarded;
            }

            Assert.That(cascade.CascadeDepth, Is.GreaterThanOrEqualTo(2));
            Assert.That(cascade.ScoreAwarded, Is.EqualTo(expectedMoveScore));
        }

        [Test]
        public void SameSeedAndMovesProduceIdenticalFifteenMoveSession()
        {
            var first = new TinctureMatchSession(882731);
            var replay = new TinctureMatchSession(882731);

            for (int move = 0; move < 15; move++)
            {
                TinctureMatchSwap swap =
                    TinctureMatchResolver
                        .GetLegalNormalSwaps(first.Board)[0];
                Assert.That(
                    first.TrySwap(
                        swap.First,
                        swap.Second,
                        out TinctureMatchMoveResult firstResult),
                    Is.True);
                Assert.That(
                    replay.TrySwap(
                        swap.First,
                        swap.Second,
                        out TinctureMatchMoveResult replayResult),
                    Is.True);
                Assert.That(
                    replayResult.BoardFinal,
                    Is.EqualTo(firstResult.BoardFinal));
                Assert.That(
                    replayResult.ScoreAwarded,
                    Is.EqualTo(firstResult.ScoreAwarded));
            }

            Assert.That(first.IsFinished, Is.True);
            Assert.That(replay.IsFinished, Is.True);
            Assert.That(first.MovesCompleted, Is.EqualTo(15));
            Assert.That(first.MovesRemaining, Is.Zero);
            Assert.That(replay.Score, Is.EqualTo(first.Score));
            Assert.That(replay.BestCascade, Is.EqualTo(first.BestCascade));

            Assert.That(
                first.TrySwap(
                    new TinctureMatchCell(0, 0),
                    new TinctureMatchCell(0, 1),
                    out TinctureMatchMoveResult rejected),
                Is.False);
            Assert.That(
                rejected.RejectionReason,
                Is.EqualTo(
                    TinctureMatchMoveRejectionReason.SessionFinished));
            Assert.That(first.MovesCompleted, Is.EqualTo(15));
        }

        [Test]
        public void SessionNeverContainsMoreThanOneMoonshine()
        {
            for (int seed = 1; seed <= 12; seed++)
            {
                var session = new TinctureMatchSession(seed);
                while (!session.IsFinished)
                {
                    Assert.That(
                        session.Board.CountTiles(
                            TinctureTileKind.Moonshine),
                        Is.LessThanOrEqualTo(1));
                    TinctureMatchSwap swap =
                        ChooseActivationOrNormalSwap(session.Board);
                    Assert.That(
                        session.TrySwap(
                            swap.First,
                            swap.Second,
                            out TinctureMatchMoveResult result),
                        Is.True);
                    Assert.That(
                        result.BoardFinal.CountTiles(
                            TinctureTileKind.Moonshine),
                        Is.LessThanOrEqualTo(1));
                }
            }
        }

        [Test]
        public void DeadInjectedBoardIsDeterministicallyReshuffled()
        {
            TinctureMatchBoard dead =
                TinctureMatchTestBoards.NoMoveBoard(false);
            var first = new TinctureMatchSession(77, dead);
            var replay = new TinctureMatchSession(77, dead);

            Assert.That(first.Board, Is.EqualTo(replay.Board));
            Assert.That(first.Board, Is.Not.EqualTo(dead));
            Assert.That(
                TinctureMatchResolver.CountLegalNormalSwaps(
                    first.Board),
                Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void ConstructorRejectsMatchesEmptyCellsAndSecondMoonshine()
        {
            TinctureMatchBoard stable =
                TinctureMatchTestBoards.LongMatchBoard();
            TinctureMatchBoard empty =
                TinctureMatchTestBoards.WithTile(
                    stable,
                    new TinctureMatchCell(0, 0),
                    TinctureTileKind.Empty);
            TinctureMatchBoard oneMoonshine =
                TinctureMatchTestBoards.WithTile(
                    stable,
                    new TinctureMatchCell(0, 0),
                    TinctureTileKind.Moonshine);
            TinctureMatchBoard twoMoonshines =
                TinctureMatchTestBoards.WithTile(
                    oneMoonshine,
                    new TinctureMatchCell(0, 1),
                    TinctureTileKind.Moonshine);
            TinctureTileKind[] matchingTiles = stable.ToArray();
            matchingTiles[0] = TinctureTileKind.Cherry;
            matchingTiles[1] = TinctureTileKind.Cherry;
            matchingTiles[2] = TinctureTileKind.Cherry;
            var matching = new TinctureMatchBoard(7, 7, matchingTiles);

            Assert.Throws<ArgumentException>(
                () => new TinctureMatchSession(1, empty));
            Assert.Throws<ArgumentException>(
                () => new TinctureMatchSession(1, twoMoonshines));
            Assert.Throws<ArgumentException>(
                () => new TinctureMatchSession(1, matching));
        }

        [Test]
        public void InvalidGeometryReturnsSpecificReasons()
        {
            var session = new TinctureMatchSession(41);

            Assert.That(
                session.TrySwap(
                    new TinctureMatchCell(-1, 0),
                    new TinctureMatchCell(0, 0),
                    out TinctureMatchMoveResult outside),
                Is.False);
            Assert.That(
                outside.RejectionReason,
                Is.EqualTo(
                    TinctureMatchMoveRejectionReason.OutOfBounds));

            Assert.That(
                session.TrySwap(
                    new TinctureMatchCell(0, 0),
                    new TinctureMatchCell(1, 1),
                    out TinctureMatchMoveResult diagonal),
                Is.False);
            Assert.That(
                diagonal.RejectionReason,
                Is.EqualTo(
                    TinctureMatchMoveRejectionReason.NotAdjacent));
        }

        private static TinctureMatchMoveResult FindCascadeResult()
        {
            for (int seed = 1; seed <= 512; seed++)
            {
                var session = new TinctureMatchSession(seed);
                IReadOnlyList<TinctureMatchSwap> swaps =
                    TinctureMatchResolver.GetLegalNormalSwaps(
                        session.Board);
                for (int index = 0; index < swaps.Count; index++)
                {
                    var candidate = new TinctureMatchSession(seed);
                    TinctureMatchSwap swap = swaps[index];
                    if (candidate.TrySwap(
                            swap.First,
                            swap.Second,
                            out TinctureMatchMoveResult result) &&
                        result.CascadeDepth >= 2)
                    {
                        return result;
                    }
                }
            }

            Assert.Fail("Expected at least one deterministic cascade.");
            return null;
        }

        private static TinctureMatchSwap FindInvalidAdjacentSwap(
            TinctureMatchBoard board)
        {
            for (int row = 0; row < board.Rows; row++)
            {
                for (int column = 0;
                     column < board.Columns;
                     column++)
                {
                    var first =
                        new TinctureMatchCell(row, column);
                    var candidates = new[]
                    {
                        new TinctureMatchCell(row, column + 1),
                        new TinctureMatchCell(row + 1, column)
                    };
                    for (int index = 0;
                         index < candidates.Length;
                         index++)
                    {
                        TinctureMatchCell second =
                            candidates[index];
                        if (!board.Contains(second))
                        {
                            continue;
                        }

                        TinctureTileKind firstKind =
                            board[first.Row, first.Column];
                        TinctureTileKind secondKind =
                            board[second.Row, second.Column];
                        if (!TinctureMatchResolver.IsNormalTile(
                                firstKind) ||
                            !TinctureMatchResolver.IsNormalTile(
                                secondKind) ||
                            TinctureMatchResolver.IsLegalNormalSwap(
                                board,
                                first,
                                second))
                        {
                            continue;
                        }

                        return new TinctureMatchSwap(first, second);
                    }
                }
            }

            Assert.Fail("Expected at least one invalid adjacent swap.");
            return default;
        }

        private static TinctureMatchCell FindTile(
            TinctureMatchBoard board,
            TinctureTileKind kind)
        {
            for (int row = 0; row < board.Rows; row++)
            {
                for (int column = 0;
                     column < board.Columns;
                     column++)
                {
                    if (board[row, column] == kind)
                    {
                        return new TinctureMatchCell(row, column);
                    }
                }
            }

            Assert.Fail($"Expected a {kind} tile.");
            return default;
        }

        private static TinctureMatchCell FindNormalNeighbour(
            TinctureMatchBoard board,
            TinctureMatchCell cell)
        {
            var candidates = new[]
            {
                new TinctureMatchCell(cell.Row, cell.Column - 1),
                new TinctureMatchCell(cell.Row, cell.Column + 1),
                new TinctureMatchCell(cell.Row - 1, cell.Column),
                new TinctureMatchCell(cell.Row + 1, cell.Column)
            };
            for (int index = 0; index < candidates.Length; index++)
            {
                TinctureMatchCell candidate = candidates[index];
                if (board.Contains(candidate) &&
                    TinctureMatchResolver.IsNormalTile(
                        board[candidate.Row, candidate.Column]))
                {
                    return candidate;
                }
            }

            Assert.Fail("Moonshine must have a normal neighbour.");
            return default;
        }

        private static TinctureMatchSwap ChooseActivationOrNormalSwap(
            TinctureMatchBoard board)
        {
            if (board.CountTiles(TinctureTileKind.Moonshine) == 1)
            {
                TinctureMatchCell moonshine =
                    FindTile(board, TinctureTileKind.Moonshine);
                return new TinctureMatchSwap(
                    moonshine,
                    FindNormalNeighbour(board, moonshine));
            }

            return TinctureMatchResolver
                .GetLegalNormalSwaps(board)[0];
        }
    }
}
