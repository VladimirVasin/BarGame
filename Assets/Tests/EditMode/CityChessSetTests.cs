using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The park chess set is the one piece of city dressing that has to
    /// be literally correct rather than merely suggestive, and now that
    /// it has men on it "correct" means a great deal more than
    /// alternating squares. Everything a player would notice from the
    /// path is pinned here: which corner is light, which file the queen
    /// stands on, which way a knight looks, and that no man is standing
    /// on another man's square.
    /// </summary>
    public sealed class CityChessSetTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;
        private const int Size = CityChessBoardGeometry.SquaresPerSide;
        private const string ManifestPath =
            "Assets/City/Models/CityChessSet3D.json";

        // -- the lattice --------------------------------------------------

        /// <summary>
        /// The one rule the whole colouring follows: both men have a
        /// light square on their right. They sit at opposite ends, and
        /// on an even board a half turn maps corner to corner, so the
        /// two corners share a parity and one rule settles both.
        /// </summary>
        [Test]
        public void Board_GivesBothPlayersALightSquareOnTheirRight()
        {
            // The chess player faces +Forward with the tangent to his
            // left, so his near-right corner is file 0, rank 0. The man
            // opposite faces the other way and his is (7, 7).
            Assert.That(
                CityChessBoardGeometry.IsDarkSquare(0, 0),
                Is.False,
                "The near player's right-hand corner must be light.");
            Assert.That(
                CityChessBoardGeometry.IsDarkSquare(Size - 1, Size - 1),
                Is.False,
                "And so must the far player's.");
        }

        [Test]
        public void Board_PutsA1DarkAndTheWhiteQueenOnHerOwnColour()
        {
            // 'a' is the file furthest from the near player's right hand.
            int fileA = CityChessSetPlan.FileOfChessColumn(0);
            int fileD = CityChessSetPlan.FileOfChessColumn(3);
            int fileH = CityChessSetPlan.FileOfChessColumn(7);

            Assert.That(
                CityChessBoardGeometry.IsDarkSquare(fileA, 0),
                Is.True,
                "a1 is dark on every board ever made.");
            Assert.That(
                CityChessBoardGeometry.IsDarkSquare(fileH, 0),
                Is.False,
                "h1 is light.");
            Assert.That(
                CityChessBoardGeometry.IsDarkSquare(fileD, 0),
                Is.False,
                "The white queen stands on d1 and d1 is light.");
            Assert.That(
                CityChessBoardGeometry.IsDarkSquare(fileD, Size - 1),
                Is.True,
                "And the black queen's d8 is dark. Queen on her colour.");
        }

        [Test]
        public void Board_AlternatesAndKeepsHalfOfEverySquareDark()
        {
            int dark = 0;
            for (int file = 0; file < Size; file++)
            {
                for (int rank = 0; rank < Size; rank++)
                {
                    if (CityChessBoardGeometry.IsDarkSquare(file, rank))
                    {
                        dark++;
                    }

                    if (file + 1 < Size)
                    {
                        Assert.That(
                            CityChessBoardGeometry.IsDarkSquare(file + 1, rank),
                            Is.Not.EqualTo(
                                CityChessBoardGeometry.IsDarkSquare(file, rank)));
                    }
                }
            }

            Assert.That(dark, Is.EqualTo(Size * Size / 2));
        }

        [Test]
        public void Board_StandsADarkManHigherThanALightOne()
        {
            // The dark squares are inlaid proud of the light plate, and
            // a man who ignored that would float on half the board.
            Assert.That(
                CityChessBoardGeometry.SquareTopY(0, 1) -
                CityChessBoardGeometry.SquareTopY(0, 0),
                Is.EqualTo(CityChessBoardGeometry.DarkSquareProudMeters)
                    .Within(1e-6f));
        }

        // -- the chess setup -----------------------------------------------

        [Test]
        public void Chess_SetsUpThirtyTwoMenAndNothingInTheMiddle()
        {
            List<CityChessManPlacement> chess = Chessmen();

            Assert.That(chess, Has.Count.EqualTo(32));
            Assert.That(
                chess.Count(man => man.IsLight),
                Is.EqualTo(CityChessSetPlan.ChessMenPerSide));
            Assert.That(
                chess.Count(man => !man.IsLight),
                Is.EqualTo(CityChessSetPlan.ChessMenPerSide));
            Assert.That(
                chess.Select(man => man.Rank).Distinct().OrderBy(r => r),
                Is.EqualTo(new[] { 0, 1, 6, 7 }),
                "Ranks three to six are the empty middle of an opening.");
            Assert.That(
                chess.Count(man => man.Kind == CityChessPieceKind.Pawn),
                Is.EqualTo(16));
            Assert.That(
                chess.Count(man => man.Kind == CityChessPieceKind.King),
                Is.EqualTo(2));
            Assert.That(
                chess.Count(man => man.Kind == CityChessPieceKind.Queen),
                Is.EqualTo(2));
        }

        [Test]
        public void Chess_LaysTheBackRankInTheOrderAPlayerWouldLayIt()
        {
            var expected = new[]
            {
                CityChessPieceKind.Rook,
                CityChessPieceKind.Knight,
                CityChessPieceKind.Bishop,
                CityChessPieceKind.Queen,
                CityChessPieceKind.King,
                CityChessPieceKind.Bishop,
                CityChessPieceKind.Knight,
                CityChessPieceKind.Rook
            };

            foreach (bool light in new[] { true, false })
            {
                int rank = light ? 0 : Size - 1;
                for (int column = 0; column < Size; column++)
                {
                    int file = CityChessSetPlan.FileOfChessColumn(column);
                    CityChessManPlacement man = Chessmen().Single(
                        candidate => candidate.IsLight == light &&
                                     candidate.Rank == rank &&
                                     candidate.File == file);
                    Assert.That(
                        man.Kind,
                        Is.EqualTo(expected[column]),
                        $"The {(char)('a' + column)} file of the " +
                        $"{(light ? "light" : "dark")} back rank.");
                }
            }
        }

        [Test]
        public void Chess_StandsEachQueenOnHerOwnColour()
        {
            foreach (CityChessManPlacement queen in Chessmen()
                         .Where(man => man.Kind == CityChessPieceKind.Queen))
            {
                bool dark = CityChessBoardGeometry.IsDarkSquare(
                    queen.File,
                    queen.Rank);
                Assert.That(
                    dark,
                    Is.EqualTo(!queen.IsLight),
                    "A light queen stands light and a dark queen dark.");
            }
        }

        [Test]
        public void Chess_TurnsEveryKnightTowardTheOtherSide()
        {
            foreach (CityChessManPlacement knight in Chessmen()
                         .Where(man => man.Kind == CityChessPieceKind.Knight))
            {
                float yaw = Mathf.DeltaAngle(
                    knight.IsLight ? 0f : 180f,
                    knight.YawDegrees);
                Assert.That(
                    Mathf.Abs(yaw),
                    Is.LessThanOrEqualTo(
                        CityChessSetPlan.MaximumYawJitterDegrees + 0.001f),
                    "A knight looks up the board, give or take the hand " +
                    "that put it down.");
            }
        }

        // -- the draughts setup ---------------------------------------------

        [Test]
        public void Draughts_LaysTwelveASideOnTheDarkSquaresOnly()
        {
            List<CityChessManPlacement> draughts = Draughtsmen();

            Assert.That(draughts, Has.Count.EqualTo(24));
            Assert.That(
                draughts.All(man =>
                    man.Kind == CityChessPieceKind.Draught),
                Is.True);
            Assert.That(
                draughts.Count(man => man.IsLight),
                Is.EqualTo(CityChessSetPlan.DraughtsPerSide));
            foreach (CityChessManPlacement man in draughts)
            {
                Assert.That(
                    CityChessBoardGeometry.IsDarkSquare(man.File, man.Rank),
                    Is.True,
                    "A draught only ever stands on a dark square.");
            }

            Assert.That(
                draughts.Where(man => man.IsLight)
                    .Select(man => man.Rank).Distinct().OrderBy(r => r),
                Is.EqualTo(new[] { 5, 6, 7 }),
                "The light men are the three rows nearest their player.");
            Assert.That(
                draughts.Where(man => !man.IsLight)
                    .Select(man => man.Rank).Distinct().OrderBy(r => r),
                Is.EqualTo(new[] { 0, 1, 2 }));
        }

        // -- both boards ------------------------------------------------------

        [Test]
        public void Set_PutsAtMostOneManOnASquare()
        {
            var occupied = new HashSet<(int, int, int)>();
            foreach (CityChessManPlacement man in
                     CityChessSetPlan.Create(Seed))
            {
                Assert.That(
                    CityChessBoardGeometry.IsOnBoard(man.File, man.Rank),
                    Is.True);
                Assert.That(
                    occupied.Add((man.Table, man.File, man.Rank)),
                    Is.True,
                    $"Two men share {man.Table}/{man.File}/{man.Rank}.");
            }

            Assert.That(occupied, Has.Count.EqualTo(56));
        }

        [Test]
        public void Set_NeverPutsAManOverHisOwnSquaresEdge()
        {
            float widest = ManifestPieces().Max(piece => piece.radius_m);
            float half = CityChessBoardGeometry.SquareSizeMeters * 0.5f;

            foreach (CityChessManPlacement man in
                     CityChessSetPlan.Create(Seed))
            {
                Assert.That(
                    man.Offset.magnitude,
                    Is.LessThanOrEqualTo(
                        CityChessSetPlan.MaximumOffsetMeters *
                        Mathf.Sqrt(2f) + 1e-5f));
                Assert.That(
                    man.Offset.magnitude + widest,
                    Is.LessThan(half),
                    "The hand that set these down may be old, but no man " +
                    "hangs over the next square.");
            }
        }

        [Test]
        public void Set_IsTheSameSetEveryTimeAndADifferentHandEachSeed()
        {
            IReadOnlyList<CityChessManPlacement> first =
                CityChessSetPlan.Create(Seed);
            IReadOnlyList<CityChessManPlacement> again =
                CityChessSetPlan.Create(Seed);
            IReadOnlyList<CityChessManPlacement> other =
                CityChessSetPlan.Create(Seed + 1);

            for (int index = 0; index < first.Count; index++)
            {
                Assert.That(again[index].Offset, Is.EqualTo(first[index].Offset));
                Assert.That(
                    again[index].YawDegrees,
                    Is.EqualTo(first[index].YawDegrees));
                // The squares are the rules and never move; only the
                // hand that placed them differs.
                Assert.That(other[index].File, Is.EqualTo(first[index].File));
                Assert.That(other[index].Rank, Is.EqualTo(first[index].Rank));
                Assert.That(other[index].Kind, Is.EqualTo(first[index].Kind));
            }

            Assert.That(
                Enumerable.Range(0, first.Count).Any(index =>
                    other[index].Offset != first[index].Offset),
                Is.True,
                "A different city sets the same opening down differently.");
        }

        // -- the art -----------------------------------------------------------

        [Test]
        public void Art_ShipsSevenMenThatStandApartByHeight()
        {
            ChessSetManifestPiece[] pieces = ManifestPieces();
            Assert.That(pieces, Has.Length.EqualTo(7));

            var ladder = new[]
            {
                "pawn", "rook", "knight", "bishop", "queen", "king"
            };
            for (int index = 1; index < ladder.Length; index++)
            {
                float lower = Height(pieces, ladder[index - 1]);
                float upper = Height(pieces, ladder[index]);
                Assert.That(
                    upper - lower,
                    Is.GreaterThan(0.012f),
                    $"A {ladder[index]} has to read taller than a " +
                    $"{ladder[index - 1]} from the path.");
            }

            Assert.That(
                Height(pieces, "draught"),
                Is.LessThan(Height(pieces, "pawn") * 0.35f),
                "A draught is a disc, not a short pawn.");
            foreach (ChessSetManifestPiece piece in pieces)
            {
                Assert.That(
                    piece.radius_m * 2f,
                    Is.LessThanOrEqualTo(
                        CityChessBoardGeometry.SquareSizeMeters * 0.80f),
                    $"'{piece.mesh}' crowds its square.");
            }
        }

        [Test]
        public void Art_ImportsEveryMeshReadableAndStandingOnItsOrigin()
        {
            // The runtime combines these at load. An unreadable mesh
            // combines into nothing and the board comes up empty in a
            // player build with no error anywhere.
            foreach (ChessSetManifestPiece piece in ManifestPieces())
            {
                Mesh mesh = AssetDatabase
                    .LoadAllAssetsAtPath(
                        "Assets/City/Models/CityChessSet3D.fbx")
                    .OfType<Mesh>()
                    .FirstOrDefault(candidate =>
                        string.Equals(
                            candidate.name,
                            piece.mesh,
                            StringComparison.Ordinal));
                Assert.That(mesh, Is.Not.Null, $"'{piece.mesh}' is missing.");
                Assert.That(
                    mesh.isReadable,
                    Is.True,
                    $"'{piece.mesh}' must import readable.");
                Assert.That(
                    mesh.bounds.min.y,
                    Is.EqualTo(0f).Within(0.001f),
                    $"'{piece.mesh}' must stand on its own origin.");
                Assert.That(
                    mesh.bounds.size.y,
                    Is.EqualTo(piece.height_m).Within(0.002f));
            }
        }

        [Test]
        public void Art_IsBoundIntoTheProviderThatCarriesItIntoABuild()
        {
            CityChessSetProvider provider = CityChessSetProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "The chess set provider must live in Resources.");
            Assert.That(provider.IsComplete(), Is.True);
            Assert.That(
                provider.BuildSignature,
                Is.EqualTo(Manifest().build_signature),
                "The provider was bound against an older art build.");
        }

        // -- helpers -------------------------------------------------------------

        private static List<CityChessManPlacement> Chessmen()
        {
            return CityChessSetPlan.Create(Seed)
                .Where(man => man.Table == CityChessSetPlan.ChessTable)
                .ToList();
        }

        private static List<CityChessManPlacement> Draughtsmen()
        {
            return CityChessSetPlan.Create(Seed)
                .Where(man => man.Table == CityChessSetPlan.DraughtsTable)
                .ToList();
        }

        private static float Height(
            ChessSetManifestPiece[] pieces,
            string key)
        {
            return pieces.Single(piece =>
                string.Equals(piece.key, key, StringComparison.Ordinal))
                .height_m;
        }

        private static ChessSetManifestPiece[] ManifestPieces()
        {
            return Manifest().pieces;
        }

        private static ChessSetManifest Manifest()
        {
            var manifest = JsonUtility.FromJson<ChessSetManifest>(
                System.IO.File.ReadAllText(ManifestPath));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(
                manifest.square_size_m,
                Is.EqualTo(CityChessBoardGeometry.SquareSizeMeters)
                    .Within(1e-5f),
                "The set was built for a different board.");
            return manifest;
        }

        [Serializable]
        private sealed class ChessSetManifest
        {
            public string design_id;
            public float square_size_m;
            public string build_signature;
            public ChessSetManifestPiece[] pieces;
        }

        [Serializable]
        private sealed class ChessSetManifestPiece
        {
            public string key;
            public string mesh;
            public float height_m;
            public float radius_m;
        }
    }
}
