using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// What stands on a dark square. Nothing ever stands on a light
    /// one, which is why the board only ever holds thirty-two men.
    /// </summary>
    public static class DraughtsPiece
    {
        public const byte None = 0;
        public const byte LightMan = 1;
        public const byte LightKing = 2;
        public const byte DarkMan = 3;
        public const byte DarkKing = 4;

        public static bool IsLight(byte piece)
        {
            return piece == LightMan || piece == LightKing;
        }

        public static bool IsDark(byte piece)
        {
            return piece == DarkMan || piece == DarkKing;
        }

        public static bool IsKing(byte piece)
        {
            return piece == LightKing || piece == DarkKing;
        }

        public static bool BelongsTo(byte piece, bool light)
        {
            return light ? IsLight(piece) : IsDark(piece);
        }

        public static byte Crown(byte piece)
        {
            if (piece == LightMan)
            {
                return LightKing;
            }

            return piece == DarkMan ? DarkKing : piece;
        }
    }

    /// <summary>
    /// One draughts move: where the man started, the squares he landed
    /// on in order, and what he lifted off the board on each landing.
    /// The squares themselves live in the list that generated the move,
    /// because a chain of five jumps is not a shape a struct can hold
    /// and a list per move would allocate through the whole search.
    /// </summary>
    public readonly struct DraughtsMove
    {
        public DraughtsMove(
            byte from,
            int stepOffset,
            byte stepCount,
            byte captureCount,
            bool crowns)
        {
            From = from;
            StepOffset = stepOffset;
            StepCount = stepCount;
            CaptureCount = captureCount;
            Crowns = crowns;
        }

        public byte From { get; }
        public int StepOffset { get; }
        public byte StepCount { get; }
        public byte CaptureCount { get; }

        /// <summary>The man ends the move a king. In Russian draughts
        /// that can happen in the middle of a chain, and he finishes
        /// the chain as a king.</summary>
        public bool Crowns { get; }

        public bool IsCapture => CaptureCount > 0;
    }

    /// <summary>
    /// The moves of one position, with their landings packed into two
    /// shared arrays. Cleared and refilled rather than reallocated: the
    /// search asks for this thousands of times a second.
    /// </summary>
    public sealed class DraughtsMoveList
    {
        public const byte NoCapture = 255;

        private readonly List<DraughtsMove> moves =
            new List<DraughtsMove>(48);
        private readonly List<byte> landings = new List<byte>(256);
        private readonly List<byte> victims = new List<byte>(256);

        public int Count => moves.Count;

        public DraughtsMove this[int index] => moves[index];

        public void Clear()
        {
            moves.Clear();
            landings.Clear();
            victims.Clear();
        }

        public void Add(
            byte from,
            IReadOnlyList<byte> stepLandings,
            IReadOnlyList<byte> stepVictims,
            bool crowns)
        {
            int offset = landings.Count;
            byte captureCount = 0;
            for (int index = 0; index < stepLandings.Count; index++)
            {
                landings.Add(stepLandings[index]);
                byte victim = stepVictims[index];
                victims.Add(victim);
                if (victim != NoCapture)
                {
                    captureCount++;
                }
            }

            moves.Add(new DraughtsMove(
                from,
                offset,
                (byte)stepLandings.Count,
                captureCount,
                crowns));
        }

        public byte Landing(DraughtsMove move, int step)
        {
            return landings[move.StepOffset + step];
        }

        public byte Victim(DraughtsMove move, int step)
        {
            return victims[move.StepOffset + step];
        }

        public byte Destination(DraughtsMove move)
        {
            return landings[move.StepOffset + move.StepCount - 1];
        }
    }

    /// <summary>
    /// Russian draughts on the park's own board, in the lattice the set
    /// was laid out in: `file` along the recipe tangent, `rank` along
    /// its forward, dark squares where the sum is odd.
    ///
    /// The rules that actually matter here, because they are the ones a
    /// player notices are missing: taking is compulsory and a chain runs
    /// to its end; men take backwards as well as forwards; a king flies
    /// the whole diagonal and may land anywhere past the man he took; a
    /// taken man stays on the board until the move is over and may not
    /// be jumped twice; and a man who reaches the far row inside a chain
    /// is crowned there and finishes the chain as a king. Which capture
    /// to take when several are offered is free — that is the Russian
    /// game rather than the international one.
    ///
    /// Nothing here needs a scene, a mesh or a frame: a whole match can
    /// be played out in a test. The one thing it does borrow from the
    /// world is `CityChessBoardGeometry.IsDarkSquare`, because which
    /// squares are dark is a fact about the drawn board and must not be
    /// restated.
    /// </summary>
    public sealed class DraughtsPosition
    {
        /// <summary>Both sides shuffling kings about with nothing taken
        /// and no man moved is a draw. Thirty plies is shorter than the
        /// tournament rule and longer than anybody's patience.</summary>
        public const int QuietPlyLimit = 30;

        private static readonly int[] DiagonalFileSteps =
            { 1, 1, -1, -1 };
        private static readonly int[] DiagonalRankSteps =
            { 1, -1, -1, 1 };

        private readonly byte[] squares = new byte[64];
        private readonly bool[] capturedFlags = new bool[64];
        private readonly List<byte> pathLandings = new List<byte>(16);
        private readonly List<byte> pathVictims = new List<byte>(16);

        public DraughtsPosition()
        {
            Reset();
        }

        public bool LightToMove { get; private set; }
        public int QuietPlies { get; private set; }
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

        /// <summary>
        /// The twelve and twelve the two old men left set up: light on
        /// the three rows nearest the draughts player, dark on the three
        /// nearest the empty plank across from him, and the two middle
        /// rows bare.
        /// </summary>
        public void Reset()
        {
            Array.Clear(squares, 0, squares.Length);
            for (int rank = 0; rank < 8; rank++)
            {
                if (rank > 2 && rank < 5)
                {
                    continue;
                }

                bool light = rank >= 5;
                for (int file = 0; file < 8; file++)
                {
                    if (!CityChessBoardGeometry.IsDarkSquare(file, rank))
                    {
                        continue;
                    }

                    squares[Square(file, rank)] = light
                        ? DraughtsPiece.LightMan
                        : DraughtsPiece.DarkMan;
                }
            }

            LightToMove = true;
            QuietPlies = 0;
            PliesPlayed = 0;
        }

        /// <summary>States a position outright. Tests only.</summary>
        public void SetUp(
            IReadOnlyList<(int square, byte piece)> placement,
            bool lightToMove)
        {
            if (placement == null)
            {
                throw new ArgumentNullException(nameof(placement));
            }

            Array.Clear(squares, 0, squares.Length);
            for (int index = 0; index < placement.Count; index++)
            {
                (int square, byte piece) = placement[index];
                squares[square] = piece;
            }

            LightToMove = lightToMove;
            QuietPlies = 0;
            PliesPlayed = 0;
        }

        public void CopyTo(byte[] target)
        {
            Array.Copy(squares, target, squares.Length);
        }

        public void RestoreFrom(
            byte[] source,
            bool lightToMove,
            int quietPlies,
            int pliesPlayed)
        {
            Array.Copy(source, squares, squares.Length);
            LightToMove = lightToMove;
            QuietPlies = quietPlies;
            PliesPlayed = pliesPlayed;
        }

        /// <summary>
        /// Everything the side to move may play. Captures shut out
        /// quiet moves entirely, which is most of what makes this game
        /// searchable to a useful depth in a frame.
        /// </summary>
        public void Generate(DraughtsMoveList target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            bool light = LightToMove;
            Array.Clear(capturedFlags, 0, capturedFlags.Length);
            for (int square = 0; square < 64; square++)
            {
                byte piece = squares[square];
                if (!DraughtsPiece.BelongsTo(piece, light))
                {
                    continue;
                }

                pathLandings.Clear();
                pathVictims.Clear();
                squares[square] = DraughtsPiece.None;
                ExploreCaptures(
                    square,
                    square,
                    DraughtsPiece.IsKing(piece),
                    light,
                    false,
                    target);
                squares[square] = piece;
            }

            if (target.Count > 0)
            {
                return;
            }

            for (int square = 0; square < 64; square++)
            {
                byte piece = squares[square];
                if (!DraughtsPiece.BelongsTo(piece, light))
                {
                    continue;
                }

                if (DraughtsPiece.IsKing(piece))
                {
                    GenerateKingSlides(square, target);
                }
                else
                {
                    GenerateManSteps(square, light, target);
                }
            }
        }

        /// <summary>
        /// Plays one generated move. The list must be the one the move
        /// came out of: the landings live in it.
        /// </summary>
        public void Apply(DraughtsMoveList list, int index)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            if (index < 0 || index >= list.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            DraughtsMove move = list[index];
            byte piece = squares[move.From];
            squares[move.From] = DraughtsPiece.None;
            for (int step = 0; step < move.StepCount; step++)
            {
                byte victim = list.Victim(move, step);
                if (victim != DraughtsMoveList.NoCapture)
                {
                    squares[victim] = DraughtsPiece.None;
                }
            }

            byte destination = list.Destination(move);
            squares[destination] = move.Crowns
                ? DraughtsPiece.Crown(piece)
                : piece;
            QuietPlies =
                move.IsCapture || !DraughtsPiece.IsKing(piece)
                    ? 0
                    : QuietPlies + 1;
            LightToMove = !LightToMove;
            PliesPlayed++;
        }

        /// <summary>How many men and kings a side still has, for the
        /// score line and for the engine.</summary>
        public void CountMaterial(
            bool light,
            out int men,
            out int kings)
        {
            men = 0;
            kings = 0;
            for (int square = 0; square < 64; square++)
            {
                byte piece = squares[square];
                if (!DraughtsPiece.BelongsTo(piece, light))
                {
                    continue;
                }

                if (DraughtsPiece.IsKing(piece))
                {
                    kings++;
                }
                else
                {
                    men++;
                }
            }
        }

        public ulong ComputeKey()
        {
            ulong hash = 1469598103934665603ul;
            for (int square = 0; square < 64; square++)
            {
                hash ^= squares[square];
                hash *= 1099511628211ul;
            }

            if (LightToMove)
            {
                hash ^= 0x9E3779B97F4A7C15ul;
            }

            return hash;
        }

        /// <summary>
        /// Walks every continuation of a capture chain from
        /// <paramref name="current"/>. Emits only when nothing more can
        /// be taken, because a chain that stops early is not a move.
        /// </summary>
        private void ExploreCaptures(
            int origin,
            int current,
            bool isKing,
            bool light,
            bool crowned,
            DraughtsMoveList target)
        {
            bool continued = false;
            int crownRank = light ? 0 : 7;
            for (int direction = 0; direction < 4; direction++)
            {
                int fileStep = DiagonalFileSteps[direction];
                int rankStep = DiagonalRankSteps[direction];
                int file = FileOf(current);
                int rank = RankOf(current);

                int victimFile = file + fileStep;
                int victimRank = rank + rankStep;
                if (isKing)
                {
                    while (IsOnBoard(victimFile, victimRank) &&
                           squares[Square(victimFile, victimRank)] ==
                               DraughtsPiece.None)
                    {
                        victimFile += fileStep;
                        victimRank += rankStep;
                    }
                }

                if (!IsOnBoard(victimFile, victimRank))
                {
                    continue;
                }

                int victimSquare = Square(victimFile, victimRank);
                byte victim = squares[victimSquare];
                if (victim == DraughtsPiece.None ||
                    DraughtsPiece.BelongsTo(victim, light) ||
                    capturedFlags[victimSquare])
                {
                    continue;
                }

                int landingFile = victimFile + fileStep;
                int landingRank = victimRank + rankStep;
                while (IsOnBoard(landingFile, landingRank) &&
                       squares[Square(landingFile, landingRank)] ==
                           DraughtsPiece.None)
                {
                    int landing = Square(landingFile, landingRank);
                    continued = true;
                    capturedFlags[victimSquare] = true;
                    pathLandings.Add((byte)landing);
                    pathVictims.Add((byte)victimSquare);
                    bool crownsHere =
                        !isKing && landingRank == crownRank;
                    ExploreCaptures(
                        origin,
                        landing,
                        isKing || crownsHere,
                        light,
                        crowned || crownsHere,
                        target);
                    pathLandings.RemoveAt(pathLandings.Count - 1);
                    pathVictims.RemoveAt(pathVictims.Count - 1);
                    capturedFlags[victimSquare] = false;

                    if (!isKing)
                    {
                        break;
                    }

                    landingFile += fileStep;
                    landingRank += rankStep;
                }
            }

            if (!continued && pathLandings.Count > 0)
            {
                target.Add(
                    (byte)origin,
                    pathLandings,
                    pathVictims,
                    crowned);
            }
        }

        private void GenerateManSteps(
            int square,
            bool light,
            DraughtsMoveList target)
        {
            int forward = light ? -1 : 1;
            int crownRank = light ? 0 : 7;
            int file = FileOf(square);
            int rank = RankOf(square);
            for (int side = -1; side <= 1; side += 2)
            {
                int targetFile = file + side;
                int targetRank = rank + forward;
                if (!IsOnBoard(targetFile, targetRank))
                {
                    continue;
                }

                int landing = Square(targetFile, targetRank);
                if (squares[landing] != DraughtsPiece.None)
                {
                    continue;
                }

                pathLandings.Clear();
                pathVictims.Clear();
                pathLandings.Add((byte)landing);
                pathVictims.Add(DraughtsMoveList.NoCapture);
                target.Add(
                    (byte)square,
                    pathLandings,
                    pathVictims,
                    targetRank == crownRank);
            }
        }

        private void GenerateKingSlides(
            int square,
            DraughtsMoveList target)
        {
            int file = FileOf(square);
            int rank = RankOf(square);
            for (int direction = 0; direction < 4; direction++)
            {
                int targetFile = file + DiagonalFileSteps[direction];
                int targetRank = rank + DiagonalRankSteps[direction];
                while (IsOnBoard(targetFile, targetRank))
                {
                    int landing = Square(targetFile, targetRank);
                    if (squares[landing] != DraughtsPiece.None)
                    {
                        break;
                    }

                    pathLandings.Clear();
                    pathVictims.Clear();
                    pathLandings.Add((byte)landing);
                    pathVictims.Add(DraughtsMoveList.NoCapture);
                    target.Add(
                        (byte)square,
                        pathLandings,
                        pathVictims,
                        false);
                    targetFile += DiagonalFileSteps[direction];
                    targetRank += DiagonalRankSteps[direction];
                }
            }
        }

        private static bool IsOnBoard(int file, int rank)
        {
            return file >= 0 && file < 8 && rank >= 0 && rank < 8;
        }
    }
}
