using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The two park tables as places the hero can actually sit down and
    /// play at: which plank belongs to him, which game is on it, where
    /// his eyes end up, and that the whole board is in front of them.
    ///
    /// The camera assertions are the ones worth having. A seated first
    /// person view over a `1.2 m` board from `1.10 m` away with eyes
    /// less than half a metre above the stone has almost no slack in it,
    /// and "the near corners of the board are off screen" is not
    /// something a test of the rules would ever catch.
    /// </summary>
    public sealed class CityBoardGameTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        /// <summary>The frame the retro canvas is laid out for.</summary>
        private const float Aspect = 16f / 9f;

        [Test]
        public void TheDefaultCity_GrowsOneChessAndOneDraughtsSeat()
        {
            IReadOnlyList<CityBoardGameTable> tables = CreateTables();
            Assert.That(tables, Has.Count.EqualTo(2));

            var games = new List<CityBoardGameKind>();
            for (int index = 0; index < tables.Count; index++)
            {
                CityBoardGameTable table = tables[index];
                Assert.That(table.IsPresent, Is.True);
                Assert.That(
                    table.HeroSide,
                    Is.EqualTo(BoardGameSide.Dark),
                    "Both free planks are the dark side of their own " +
                    "board, so the old man always opens.");
                games.Add(table.Game);
            }

            Assert.That(
                games,
                Is.EquivalentTo(new[]
                {
                    CityBoardGameKind.Chess,
                    CityBoardGameKind.Draughts
                }));
        }

        /// <summary>
        /// The hero takes the plank nobody is on. The two old men hold
        /// `-seat-a1` and `-seat-b2`, so an overlap here would seat the
        /// hero in somebody's lap.
        /// </summary>
        [Test]
        public void TheHeroSeats_AreTheOnesTheTwoOldMenAreNotOn()
        {
            IReadOnlyList<CityBoardGameTable> tables = CreateTables();
            for (int index = 0; index < tables.Count; index++)
            {
                string seatId = tables[index].SeatId;
                Assert.That(
                    seatId.EndsWith(ParkChessPlayerPlan.SeatIdSuffix),
                    Is.False,
                    "The chess player is already on that plank.");
                Assert.That(
                    seatId.EndsWith(
                        ParkCheckersPlayerPlan.SeatIdSuffix),
                    Is.False,
                    "And the draughts player on that one.");
            }
        }

        /// <summary>
        /// Each seat looks at its own board rather than at the other
        /// table or at the grass.
        /// </summary>
        [Test]
        public void EachSeat_LooksAcrossItsOwnBoard()
        {
            IReadOnlyList<CityBoardGameTable> tables = CreateTables();
            for (int index = 0; index < tables.Count; index++)
            {
                CityBoardGameTable table = tables[index];
                Vector3 toBoard = table.BoardCenter -
                    table.SeatTopCenter;
                toBoard.y = 0f;
                Assert.That(
                    Vector3.Dot(toBoard.normalized, table.SeatFacing),
                    Is.GreaterThan(0.99f),
                    "The seat faces the middle of its own board.");
                Assert.That(
                    toBoard.magnitude,
                    Is.EqualTo(
                        CityChessBoardGeometry.BenchCenterZMeters)
                        .Within(0.001f));
            }
        }

        [Test]
        public void TheSeatedEye_SitsOverThePlankAndAboveTheBoard()
        {
            IReadOnlyList<CityBoardGameTable> tables = CreateTables();
            for (int index = 0; index < tables.Count; index++)
            {
                CityBoardGameTable table = tables[index];
                CityBoardGamePlan.EvaluateCamera(
                    table,
                    0f,
                    CityBoardGamePlan.BasePitchDegrees,
                    out Vector3 position,
                    out Quaternion rotation);

                Assert.That(
                    position.y - table.SeatTopCenter.y,
                    Is.EqualTo(CityBoardGamePlan.EyeHeightAboveSeat)
                        .Within(0.0001f));
                Vector3 planar = position - table.SeatTopCenter;
                planar.y = 0f;
                Assert.That(
                    Vector3.Dot(planar, table.SeatFacing),
                    Is.EqualTo(CityBoardGamePlan.EyeForwardMeters)
                        .Within(0.0001f),
                    "The eye steps out of the skull, toward the board.");
                Assert.That(
                    Vector3.Dot(
                        planar,
                        new Vector3(
                            table.SeatFacing.z,
                            0f,
                            -table.SeatFacing.x)),
                    Is.EqualTo(0f).Within(0.0001f),
                    "And nowhere sideways.");
                Assert.That(
                    position.y - table.BoardCenter.y,
                    Is.GreaterThan(0.25f),
                    "A seated man looks down at the board.");
                Vector3 forward = rotation * Vector3.forward;
                Assert.That(
                    forward.y,
                    Is.LessThan(-0.2f),
                    "And the base pose is pitched down, not level.");
            }
        }

        /// <summary>
        /// The resting pose looks at the middle of the board rather than
        /// past it. The near edge of a board this close subtends far
        /// more angle than the far one, so the bisector is nowhere near
        /// the line to the board's centre — which is exactly why the
        /// pitch is derived rather than authored.
        /// </summary>
        [Test]
        public void TheSeatedPose_CentresTheFieldInTheFrame()
        {
            IReadOnlyList<CityBoardGameTable> tables = CreateTables();
            const float half =
                CityChessBoardGeometry.FieldSizeMeters * 0.5f;
            for (int index = 0; index < tables.Count; index++)
            {
                CityBoardGameTable table = tables[index];
                CityBoardGamePlan.EvaluateCamera(
                    table,
                    0f,
                    CityBoardGamePlan.BasePitchDegrees,
                    out Vector3 position,
                    out Quaternion rotation);
                Quaternion inverse = Quaternion.Inverse(rotation);
                Vector3 near = inverse *
                    (table.BoardCenter -
                     table.SeatFacing * half -
                     position);
                Vector3 far = inverse *
                    (table.BoardCenter +
                     table.SeatFacing * half -
                     position);

                float belowAxis = Mathf.Atan2(-near.y, near.z) *
                    Mathf.Rad2Deg;
                float aboveAxis = Mathf.Atan2(far.y, far.z) *
                    Mathf.Rad2Deg;
                Assert.That(
                    belowAxis,
                    Is.GreaterThan(1f),
                    "The near edge sits below the axis.");
                Assert.That(
                    aboveAxis,
                    Is.GreaterThan(1f),
                    "And the far edge above it.");
                Assert.That(
                    belowAxis,
                    Is.EqualTo(aboveAxis).Within(0.05f),
                    "The two are the same, which is what centred means.");
            }
        }

        /// <summary>
        /// Every corner of the board is inside the frame at the pose the
        /// hero is handed. Checked at the true outer corners of the
        /// outermost squares, not at their middles, because the corner
        /// is the part that falls off the edge first.
        /// </summary>
        [Test]
        public void TheWholeBoard_IsInsideTheFrameFromTheSeat()
        {
            IReadOnlyList<CityBoardGameTable> tables = CreateTables();
            float tanVertical = Mathf.Tan(
                CityBoardGamePlan.FieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHorizontal = tanVertical * Aspect;
            const int last =
                CityChessBoardGeometry.SquaresPerSide - 1;
            const float half =
                CityChessBoardGeometry.SquareSizeMeters * 0.5f;

            for (int index = 0; index < tables.Count; index++)
            {
                CityBoardGameTable table = tables[index];
                CityBoardGamePlan.EvaluateCamera(
                    table,
                    0f,
                    CityBoardGamePlan.BasePitchDegrees,
                    out Vector3 position,
                    out Quaternion rotation);
                Quaternion inverse = Quaternion.Inverse(rotation);

                for (int cornerFile = 0; cornerFile <= 1; cornerFile++)
                {
                    for (int cornerRank = 0;
                         cornerRank <= 1;
                         cornerRank++)
                    {
                        int file = cornerFile == 0 ? 0 : last;
                        int rank = cornerRank == 0 ? 0 : last;
                        Vector3 corner = table.SquareCenter(file, rank) +
                            table.Tangent *
                                (file == 0 ? -half : half) +
                            table.Forward *
                                (rank == 0 ? -half : half);
                        Vector3 local = inverse * (corner - position);

                        Assert.That(
                            local.z,
                            Is.GreaterThan(0.05f),
                            $"Corner {file},{rank} is behind the eye.");
                        Assert.That(
                            Mathf.Abs(local.y),
                            Is.LessThan(local.z * tanVertical),
                            $"Corner {file},{rank} is off the top or " +
                            "bottom of the frame.");
                        Assert.That(
                            Mathf.Abs(local.x),
                            Is.LessThan(local.z * tanHorizontal),
                            $"Corner {file},{rank} is off the side of " +
                            "the frame.");
                    }
                }
            }
        }

        /// <summary>
        /// The pointer reads the board as a plane. Every square must
        /// come back from a ray aimed at its own middle, or clicking a
        /// man picks up his neighbour.
        /// </summary>
        [Test]
        public void ThePointer_ReadsBackEverySquareItIsAimedAt()
        {
            IReadOnlyList<CityBoardGameTable> tables = CreateTables();
            for (int index = 0; index < tables.Count; index++)
            {
                CityBoardGameTable table = tables[index];
                CityBoardGamePlan.EvaluateCamera(
                    table,
                    0f,
                    CityBoardGamePlan.BasePitchDegrees,
                    out Vector3 position,
                    out _);

                for (int file = 0;
                     file < CityChessBoardGeometry.SquaresPerSide;
                     file++)
                {
                    for (int rank = 0;
                         rank < CityChessBoardGeometry.SquaresPerSide;
                         rank++)
                    {
                        Vector3 center = table.SquareCenter(file, rank);
                        var ray = new Ray(
                            position,
                            (center - position).normalized);
                        Assert.That(
                            CityBoardGamePlan.TryPickSquare(
                                table,
                                ray,
                                out int pickedFile,
                                out int pickedRank),
                            Is.True,
                            $"The ray at {file},{rank} missed the board.");
                        Assert.That(pickedFile, Is.EqualTo(file));
                        Assert.That(pickedRank, Is.EqualTo(rank));
                    }
                }
            }
        }

        [Test]
        public void ThePointer_MissesTheGrassBesideTheBoard()
        {
            IReadOnlyList<CityBoardGameTable> tables = CreateTables();
            CityBoardGameTable table = tables[0];
            CityBoardGamePlan.EvaluateCamera(
                table,
                0f,
                CityBoardGamePlan.BasePitchDegrees,
                out Vector3 position,
                out _);
            Vector3 beyond = table.BoardCenter +
                table.Tangent *
                    (CityChessBoardGeometry.FieldSizeMeters * 1.5f);
            Assert.That(
                CityBoardGamePlan.TryPickSquare(
                    table,
                    new Ray(position, (beyond - position).normalized),
                    out _,
                    out _),
                Is.False);
        }

        /// <summary>
        /// There is no panel over the board any more: everything the
        /// player has to be told arrives as something the man opposite
        /// says. So every cue either has its lines or the board goes
        /// silent about a rule at the exact moment it matters — and the
        /// lines have to fit the bubble, which is two rows of `48`
        /// characters over his head.
        /// </summary>
        [Test]
        public void EveryThingTheBoardHasToSay_HasLinesThatFitTheBubble()
        {
            foreach (CityBoardGameKind game in
                     (CityBoardGameKind[])System.Enum.GetValues(
                         typeof(CityBoardGameKind)))
            {
                IReadOnlyList<string> cues =
                    CityBoardGameController.CueNames(game);
                Assert.That(cues, Is.Not.Empty);
                for (int index = 0; index < cues.Count; index++)
                {
                    for (int line = 1;
                         line <= CityBoardGameController.LinesPerCue;
                         line++)
                    {
                        string key =
                            CityBoardGameController.ResolveLineKey(
                                game,
                                cues[index],
                                line);
                        string text = LocalizationService.Get(key);
                        Assert.That(
                            text,
                            Is.Not.EqualTo(key),
                            $"'{key}' has no line.");
                        Assert.That(
                            text.Trim(),
                            Is.Not.Empty,
                            $"'{key}' is blank.");
                        Assert.That(
                            text.Length,
                            Is.LessThanOrEqualTo(
                                ParkQuarrelTaunts.MaximumLineLength),
                            $"'{key}' would push the bubble to a " +
                            "third row.");
                    }
                }
            }
        }

        internal static IReadOnlyList<CityBoardGameTable> CreateTables()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityDecorationPlan decorations =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    RoadFencePlanner.CreatePlan(layout),
                    CityNightFixturePlanner.CreatePlan(layout));
            IReadOnlyList<CityBoardGameTable> tables =
                CityBoardGamePlan.Create(layout, decorations);
            Assert.That(
                tables,
                Is.Not.Empty,
                "The default city grows a park chess set.");
            return tables;
        }
    }
}
