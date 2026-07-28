using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class TinctureMatchGeneratorTests
    {
        [Test]
        public void StandardSettingsExposeSevenBySevenFifteenMoveGame()
        {
            TinctureMatchSettings settings =
                TinctureMatchSettings.Normal;

            Assert.That(settings.Rows, Is.EqualTo(7));
            Assert.That(settings.Columns, Is.EqualTo(7));
            Assert.That(settings.MoveLimit, Is.EqualTo(15));
            Assert.That(
                settings.MinimumInitialLegalSwaps,
                Is.EqualTo(3));
        }

        [Test]
        public void GenerateProducesStablePlayableBoardForManySeeds()
        {
            TinctureMatchSettings settings =
                TinctureMatchSettings.Normal;

            for (int seed = -64; seed <= 64; seed++)
            {
                TinctureMatchBoard board =
                    TinctureMatchGenerator.Generate(seed, settings);

                Assert.That(board.Rows, Is.EqualTo(7), $"seed {seed}");
                Assert.That(board.Columns, Is.EqualTo(7), $"seed {seed}");
                Assert.That(
                    board.CountTiles(TinctureTileKind.Empty),
                    Is.Zero,
                    $"seed {seed}");
                Assert.That(
                    board.CountTiles(TinctureTileKind.Moonshine),
                    Is.EqualTo(1),
                    $"seed {seed}");
                Assert.That(
                    TinctureMatchResolver
                        .FindMatches(board)
                        .HasMatches,
                    Is.False,
                    $"seed {seed}");
                Assert.That(
                    TinctureMatchResolver.CountLegalNormalSwaps(board),
                    Is.GreaterThanOrEqualTo(3),
                    $"seed {seed}");
            }
        }

        [Test]
        public void GenerateIsDeterministicAndSeedSensitive()
        {
            TinctureMatchBoard first =
                TinctureMatchGenerator.Generate(73021);
            TinctureMatchBoard replay =
                TinctureMatchGenerator.Generate(73021);
            TinctureMatchBoard other =
                TinctureMatchGenerator.Generate(73022);

            Assert.That(replay, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first));
        }

        [Test]
        public void BoardOwnsItsInputAndReturnedArrays()
        {
            TinctureTileKind[] source =
                TinctureMatchTestBoards.NoMoveBoard().ToArray();
            var board = new TinctureMatchBoard(7, 7, source);
            source[0] = TinctureTileKind.Moonshine;
            TinctureTileKind[] exported = board.ToArray();
            exported[1] = TinctureTileKind.Moonshine;

            Assert.That(
                board[0, 0],
                Is.Not.EqualTo(TinctureTileKind.Moonshine));
            Assert.That(
                board[0, 1],
                Is.Not.EqualTo(TinctureTileKind.Moonshine));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReshuffleRepairsDeadBoardAndPreservesMoonshineCount(
            bool withMoonshine)
        {
            TinctureMatchBoard dead =
                TinctureMatchTestBoards.NoMoveBoard(withMoonshine);
            Assert.That(
                TinctureMatchResolver.CountLegalNormalSwaps(dead),
                Is.Zero);

            TinctureMatchBoard reshuffled =
                TinctureMatchGenerator.Reshuffle(dead, 99117);

            Assert.That(
                TinctureMatchResolver
                    .FindMatches(reshuffled)
                    .HasMatches,
                Is.False);
            Assert.That(
                TinctureMatchResolver.CountLegalNormalSwaps(
                    reshuffled),
                Is.GreaterThanOrEqualTo(3));
            Assert.That(
                reshuffled.CountTiles(TinctureTileKind.Moonshine),
                Is.EqualTo(withMoonshine ? 1 : 0));
        }

        [Test]
        public void ReshuffleIsDeterministicEvenWithOneAttemptCaps()
        {
            var settings = new TinctureMatchSettings(
                generationAttemptLimit: 1,
                reshuffleAttemptLimit: 1);
            TinctureMatchBoard dead =
                TinctureMatchTestBoards.NoMoveBoard(true);

            TinctureMatchBoard first =
                TinctureMatchGenerator.Reshuffle(
                    dead,
                    145,
                    settings);
            TinctureMatchBoard second =
                TinctureMatchGenerator.Reshuffle(
                    dead,
                    145,
                    settings);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(
                TinctureMatchResolver.CountLegalNormalSwaps(first),
                Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void GeneratorRejectsMismatchedOrUnplayableBoards()
        {
            var wrongSize = new TinctureMatchBoard(
                3,
                3,
                new[]
                {
                    TinctureTileKind.Cherry,
                    TinctureTileKind.SeaBuckthorn,
                    TinctureTileKind.Blueberry,
                    TinctureTileKind.Mint,
                    TinctureTileKind.Horseradish,
                    TinctureTileKind.Cherry,
                    TinctureTileKind.SeaBuckthorn,
                    TinctureTileKind.Blueberry,
                    TinctureTileKind.Mint
                });

            Assert.Throws<ArgumentException>(
                () => TinctureMatchGenerator.Reshuffle(
                    wrongSize,
                    1));
        }
    }
}
