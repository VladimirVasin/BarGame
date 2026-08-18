using System;

namespace BarPromenade
{
    /// <summary>How hard the draughts player is trying. Same shape as
    /// the chess settings and the same intent: a real opponent who is
    /// not a good one.</summary>
    public readonly struct DraughtsEngineSettings
    {
        public DraughtsEngineSettings(
            int depth,
            int nodeBudget,
            int slack,
            int blunderOneIn,
            int blunderSlack)
        {
            Depth = depth;
            NodeBudget = nodeBudget;
            Slack = slack;
            BlunderOneIn = blunderOneIn;
            BlunderSlack = blunderSlack;
        }

        public int Depth { get; }
        public int NodeBudget { get; }

        /// <summary>In hundredths of a man, so the numbers read the
        /// same way the chess ones do.</summary>
        public int Slack { get; }

        public int BlunderOneIn { get; }
        public int BlunderSlack { get; }

        /// <summary>
        /// Compulsory taking keeps the tree narrow, so the same frame
        /// budget buys two more plies here than it does in chess.
        /// </summary>
        public static DraughtsEngineSettings Park =>
            new DraughtsEngineSettings(5, 60000, 40, 10, 130);
    }

    /// <summary>
    /// The draughts half of the park's opposition: negamax over the
    /// same compulsory-capture rules the board is played by, with a
    /// forced-capture extension so it never stops counting in the
    /// middle of a chain, and the same slack pick at the root that
    /// keeps the old man beatable.
    ///
    /// Positions are taken back by restoring a snapshot rather than by
    /// unwinding a move: a draughts move can lift five men off the
    /// board and put a king on, and sixty-four bytes is cheaper than
    /// getting that wrong.
    /// </summary>
    public sealed class DraughtsEngine
    {
        public const int WinScore = 20000;
        private const int MaximumSearchDepth = 16;
        private const int QuiescenceDepth = 6;
        private const int MaximumPly =
            MaximumSearchDepth + QuiescenceDepth + 2;

        private const int ManValue = 100;
        private const int KingValue = 320;

        private readonly DraughtsMoveList[] moveLists =
            new DraughtsMoveList[MaximumPly];
        private readonly byte[][] boards = new byte[MaximumPly][];
        private readonly int[] rootScores = new int[256];
        private readonly System.Collections.Generic.List<int>
            rootCandidates =
                new System.Collections.Generic.List<int>(64);
        private readonly byte[] rootBoard = new byte[64];

        private int nodesVisited;
        private int nodeBudget;

        public DraughtsEngine()
        {
            for (int index = 0; index < MaximumPly; index++)
            {
                moveLists[index] = new DraughtsMoveList();
                boards[index] = new byte[64];
            }
        }

        public int LastSearchNodes { get; private set; }
        public int LastRootScore { get; private set; }

        /// <summary>
        /// Picks one of the moves in <paramref name="legal"/>, which
        /// must be the generated moves of <paramref name="position"/>.
        /// Returns its index, or `-1` when the side to move is stuck —
        /// which in draughts means he has lost.
        /// </summary>
        public int Choose(
            DraughtsPosition position,
            DraughtsMoveList legal,
            DraughtsEngineSettings settings,
            ref uint randomState)
        {
            if (position == null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            if (legal == null)
            {
                throw new ArgumentNullException(nameof(legal));
            }

            if (legal.Count == 0)
            {
                LastSearchNodes = 0;
                LastRootScore = 0;
                return -1;
            }

            nodesVisited = 0;
            nodeBudget = Math.Max(1024, settings.NodeBudget);
            int depth = Math.Clamp(
                settings.Depth,
                1,
                MaximumSearchDepth);
            int slack = Math.Max(0, settings.Slack);
            if (settings.BlunderOneIn > 1 &&
                ChessEngine.NextRandom(ref randomState) %
                    (uint)settings.BlunderOneIn == 0)
            {
                slack = Math.Max(slack, Math.Max(0, settings.BlunderSlack));
            }

            position.CopyTo(rootBoard);
            bool rootLightToMove = position.LightToMove;
            int rootQuietPlies = position.QuietPlies;
            int rootPliesPlayed = position.PliesPlayed;

            int best = int.MinValue;
            int count = Math.Min(legal.Count, rootScores.Length);
            for (int index = 0; index < count; index++)
            {
                position.Apply(legal, index);
                int alpha = best == int.MinValue
                    ? -WinScore * 2
                    : best - slack - 1;
                int score = -Search(position, depth - 1, 1, -WinScore * 2, -alpha);
                position.RestoreFrom(
                    rootBoard,
                    rootLightToMove,
                    rootQuietPlies,
                    rootPliesPlayed);
                rootScores[index] = score;
                if (score > best)
                {
                    best = score;
                }
            }

            rootCandidates.Clear();
            bool forcedWin = best >= WinScore - MaximumSearchDepth;
            for (int index = 0; index < count; index++)
            {
                if (forcedWin
                        ? rootScores[index] == best
                        : rootScores[index] >= best - slack)
                {
                    rootCandidates.Add(index);
                }
            }

            if (rootCandidates.Count == 0)
            {
                rootCandidates.Add(0);
            }

            int chosen = rootCandidates[
                (int)(ChessEngine.NextRandom(ref randomState) %
                      (uint)rootCandidates.Count)];
            LastSearchNodes = nodesVisited;
            LastRootScore = rootScores[chosen];
            return chosen;
        }

        /// <summary>
        /// Material first, then the three things that decide a park
        /// game: men that have walked up the board, men still standing
        /// on their own back row where a king cannot be made behind
        /// them, and kings that own the middle rather than a corner.
        /// Scored from the side to move's point of view.
        /// </summary>
        public static int Evaluate(DraughtsPosition position)
        {
            if (position == null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            int lightScore = 0;
            int darkScore = 0;
            for (int square = 0; square < 64; square++)
            {
                byte piece = position[square];
                if (piece == DraughtsPiece.None)
                {
                    continue;
                }

                int file = DraughtsPosition.FileOf(square);
                int rank = DraughtsPosition.RankOf(square);
                bool light = DraughtsPiece.IsLight(piece);
                int value;
                if (DraughtsPiece.IsKing(piece))
                {
                    int centreFile = 3 - Math.Abs(file * 2 - 7) / 2;
                    int centreRank = 3 - Math.Abs(rank * 2 - 7) / 2;
                    value = KingValue + (centreFile + centreRank) * 3;
                }
                else
                {
                    // Distance walked toward the far row, and a small
                    // bonus for the edge file where a man cannot be
                    // taken from both sides.
                    int advance = light ? 7 - rank : rank;
                    value = ManValue + advance * 4;
                    if (file == 0 || file == 7)
                    {
                        value += 4;
                    }

                    bool onOwnBackRank = light ? rank == 7 : rank == 0;
                    if (onOwnBackRank)
                    {
                        value += 6;
                    }
                }

                if (light)
                {
                    lightScore += value;
                }
                else
                {
                    darkScore += value;
                }
            }

            int score = lightScore - darkScore;
            return position.LightToMove ? score : -score;
        }

        private int Search(
            DraughtsPosition position,
            int depth,
            int ply,
            int alpha,
            int beta)
        {
            nodesVisited++;
            if (nodesVisited >= nodeBudget || ply >= MaximumPly - 1)
            {
                return Evaluate(position);
            }

            if (position.QuietPlies >= DraughtsPosition.QuietPlyLimit)
            {
                return 0;
            }

            DraughtsMoveList moves = moveLists[ply];
            position.Generate(moves);
            if (moves.Count == 0)
            {
                // No move at all is a loss in draughts, whether the
                // board is empty or merely blocked.
                return -WinScore + ply;
            }

            bool forcedCapture = moves[0].IsCapture;
            if (depth <= 0)
            {
                // A chain is never left half counted: while taking is
                // compulsory the search keeps going, up to a bound so
                // an engineered position cannot run away with a frame.
                if (!forcedCapture || ply >= MaximumSearchDepth + QuiescenceDepth)
                {
                    return Evaluate(position);
                }
            }

            byte[] snapshot = boards[ply];
            position.CopyTo(snapshot);
            bool lightToMove = position.LightToMove;
            int quietPlies = position.QuietPlies;
            int pliesPlayed = position.PliesPlayed;

            int best = -WinScore * 2;
            for (int index = 0; index < moves.Count; index++)
            {
                position.Apply(moves, index);
                int score = -Search(
                    position,
                    depth - 1,
                    ply + 1,
                    -beta,
                    -alpha);
                position.RestoreFrom(
                    snapshot,
                    lightToMove,
                    quietPlies,
                    pliesPlayed);
                if (score > best)
                {
                    best = score;
                }

                if (best > alpha)
                {
                    alpha = best;
                }

                if (alpha >= beta)
                {
                    break;
                }
            }

            return best;
        }
    }
}
