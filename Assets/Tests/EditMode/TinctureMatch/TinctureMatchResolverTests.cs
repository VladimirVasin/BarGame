using System.Linq;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class TinctureMatchResolverTests
    {
        [Test]
        public void FindMatchesUnifiesCrossIntersection()
        {
            TinctureMatchSet matches =
                TinctureMatchResolver.FindMatches(
                    TinctureMatchTestBoards.CrossBoard());

            Assert.That(matches.Runs.Count, Is.EqualTo(2));
            Assert.That(matches.UniqueCellCount, Is.EqualTo(5));
            Assert.That(
                matches.Contains(new TinctureMatchCell(2, 2)),
                Is.True);
            Assert.That(
                matches.Runs.Count(
                    run => run.Orientation ==
                           TinctureMatchOrientation.Horizontal),
                Is.EqualTo(1));
            Assert.That(
                matches.Runs.Count(
                    run => run.Orientation ==
                           TinctureMatchOrientation.Vertical),
                Is.EqualTo(1));
        }

        [Test]
        public void EmptyAndMoonshineNeverFormOrdinaryMatches()
        {
            var board = new TinctureMatchBoard(
                3,
                3,
                new[]
                {
                    TinctureTileKind.Empty,
                    TinctureTileKind.Empty,
                    TinctureTileKind.Empty,
                    TinctureTileKind.Moonshine,
                    TinctureTileKind.Moonshine,
                    TinctureTileKind.Moonshine,
                    TinctureTileKind.Cherry,
                    TinctureTileKind.SeaBuckthorn,
                    TinctureTileKind.Blueberry
                });

            Assert.That(
                TinctureMatchResolver.FindMatches(board).HasMatches,
                Is.False);
        }

        [Test]
        public void ResolverFindsLongHorizontalAndVerticalRuns()
        {
            TinctureMatchSet matches =
                TinctureMatchResolver.FindMatches(
                    new TinctureMatchBoard(
                        4,
                        4,
                        new[]
                        {
                            TinctureTileKind.Cherry,
                            TinctureTileKind.Cherry,
                            TinctureTileKind.Cherry,
                            TinctureTileKind.Cherry,
                            TinctureTileKind.SeaBuckthorn,
                            TinctureTileKind.Blueberry,
                            TinctureTileKind.Mint,
                            TinctureTileKind.Horseradish,
                            TinctureTileKind.SeaBuckthorn,
                            TinctureTileKind.Blueberry,
                            TinctureTileKind.Mint,
                            TinctureTileKind.Horseradish,
                            TinctureTileKind.SeaBuckthorn,
                            TinctureTileKind.Blueberry,
                            TinctureTileKind.Mint,
                            TinctureTileKind.Horseradish
                        }));

            Assert.That(
                matches.Runs.Any(
                    run => run.Orientation ==
                               TinctureMatchOrientation.Horizontal &&
                           run.Length == 4),
                Is.True);
            Assert.That(
                matches.Runs.Any(
                    run => run.Orientation ==
                               TinctureMatchOrientation.Vertical &&
                           run.Length == 3),
                Is.True);
        }

        [Test]
        public void LegalNormalSwapMustBeAdjacentAndCreateMatch()
        {
            TinctureMatchBoard board =
                TinctureMatchTestBoards.LongMatchBoard();
            var validFrom = new TinctureMatchCell(1, 1);
            var validTo = new TinctureMatchCell(1, 2);

            Assert.That(
                TinctureMatchResolver.IsLegalNormalSwap(
                    board,
                    validFrom,
                    validTo),
                Is.True);
            Assert.That(
                TinctureMatchResolver.IsLegalNormalSwap(
                    board,
                    new TinctureMatchCell(0, 0),
                    new TinctureMatchCell(0, 2)),
                Is.False);
            Assert.That(
                TinctureMatchResolver.IsLegalNormalSwap(
                    board,
                    new TinctureMatchCell(-1, 0),
                    new TinctureMatchCell(0, 0)),
                Is.False);
        }

        [Test]
        public void LegalSwapEnumerationUsesEachEdgeOnce()
        {
            TinctureMatchBoard board =
                TinctureMatchGenerator.Generate(815);
            var legal =
                TinctureMatchResolver.GetLegalNormalSwaps(board);

            Assert.That(legal.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(
                legal.Distinct().Count(),
                Is.EqualTo(legal.Count));
            Assert.That(
                legal.All(
                    swap => swap.First.IsOrthogonallyAdjacentTo(
                        swap.Second)),
                Is.True);
            Assert.That(
                legal.All(
                    swap => TinctureMatchResolver.IsLegalNormalSwap(
                        board,
                        swap.First,
                        swap.Second)),
                Is.True);
        }

        [Test]
        public void CellValueHasStableEqualityOrderingAndAdjacency()
        {
            var first = new TinctureMatchCell(2, 3);
            var same = new TinctureMatchCell(2, 3);
            var right = new TinctureMatchCell(2, 4);
            var diagonal = new TinctureMatchCell(3, 4);

            Assert.That(first, Is.EqualTo(same));
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(first.IsOrthogonallyAdjacentTo(right), Is.True);
            Assert.That(
                first.IsOrthogonallyAdjacentTo(diagonal),
                Is.False);
            Assert.That(first.CompareTo(right), Is.LessThan(0));
        }
    }
}
