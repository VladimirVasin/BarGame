using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// How hard the man across the board is trying. The defaults are
    /// the park: he sees a hanging piece and a mate in one, he counts
    /// material, and he will drop a pawn for no reason often enough
    /// that beating him feels like beating somebody rather than
    /// solving a puzzle.
    /// </summary>
    public readonly struct ChessEngineSettings
    {
        public ChessEngineSettings(
            int depth,
            int nodeBudget,
            int slackCentipawns,
            int blunderOneIn,
            int blunderSlackCentipawns)
        {
            Depth = depth;
            NodeBudget = nodeBudget;
            SlackCentipawns = slackCentipawns;
            BlunderOneIn = blunderOneIn;
            BlunderSlackCentipawns = blunderSlackCentipawns;
        }

        public int Depth { get; }

        /// <summary>A hard ceiling on visited nodes. The search is run
        /// inside one frame, so it is allowed to be wrong before it is
        /// allowed to be slow.</summary>
        public int NodeBudget { get; }

        /// <summary>How far below the best move he still considers a
        /// move worth playing. A third of a pawn: enough to be
        /// inconsistent, not enough to hang a rook.</summary>
        public int SlackCentipawns { get; }

        /// <summary>And how often that widens to something a spectator
        /// would wince at.</summary>
        public int BlunderOneIn { get; }

        public int BlunderSlackCentipawns { get; }

        public static ChessEngineSettings Park =>
            new ChessEngineSettings(3, 60000, 35, 11, 140);
    }

    /// <summary>
    /// A small negamax engine: material, piece-square tables, alpha-beta
    /// with MVV-LVA ordering and a capture-only quiescence pass so it
    /// never stops counting in the middle of an exchange.
    ///
    /// It is deliberately not strong. The last stage picks at random
    /// among every root move within a slack window of the best, which
    /// is what turns a small correct engine into a plausible old man
    /// who has been playing the same three openings since 1974.
    ///
    /// Pure C# and allocation-free once warmed up: the buffers below
    /// are reused across every search of a match.
    /// </summary>
    public sealed class ChessEngine
    {
        public const int MateScore = 30000;
        private const int MaximumSearchDepth = 12;
        private const int QuiescenceDepth = 4;

        private static readonly int[] PieceValues =
        {
            0, 100, 320, 330, 500, 900, 20000
        };

        // Written from White's own back rank upward, so index
        // `rank * 8 + file` reads straight off the board rather than
        // upside down the way chess books print them.
        private static readonly int[] PawnTable =
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            5, 10, 10, -20, -20, 10, 10, 5,
            5, -5, -10, 0, 0, -10, -5, 5,
            0, 0, 0, 20, 20, 0, 0, 0,
            5, 5, 10, 25, 25, 10, 5, 5,
            10, 10, 20, 30, 30, 20, 10, 10,
            50, 50, 50, 50, 50, 50, 50, 50,
            0, 0, 0, 0, 0, 0, 0, 0
        };

        private static readonly int[] KnightTable =
        {
            -50, -40, -30, -30, -30, -30, -40, -50,
            -40, -20, 0, 5, 5, 0, -20, -40,
            -30, 5, 10, 15, 15, 10, 5, -30,
            -30, 0, 15, 20, 20, 15, 0, -30,
            -30, 5, 15, 20, 20, 15, 5, -30,
            -30, 0, 10, 15, 15, 10, 0, -30,
            -40, -20, 0, 0, 0, 0, -20, -40,
            -50, -40, -30, -30, -30, -30, -40, -50
        };

        private static readonly int[] BishopTable =
        {
            -20, -10, -10, -10, -10, -10, -10, -20,
            -10, 5, 0, 0, 0, 0, 5, -10,
            -10, 10, 10, 10, 10, 10, 10, -10,
            -10, 0, 10, 10, 10, 10, 0, -10,
            -10, 5, 5, 10, 10, 5, 5, -10,
            -10, 0, 5, 10, 10, 5, 0, -10,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -20, -10, -10, -10, -10, -10, -10, -20
        };

        private static readonly int[] RookTable =
        {
            0, 0, 0, 5, 5, 0, 0, 0,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            5, 10, 10, 10, 10, 10, 10, 5,
            0, 0, 0, 0, 0, 0, 0, 0
        };

        private static readonly int[] QueenTable =
        {
            -20, -10, -10, -5, -5, -10, -10, -20,
            -10, 0, 5, 0, 0, 0, 0, -10,
            -10, 5, 5, 5, 5, 5, 0, -10,
            0, 0, 5, 5, 5, 5, 0, -5,
            -5, 0, 5, 5, 5, 5, 0, -5,
            -10, 0, 5, 5, 5, 5, 0, -10,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -20, -10, -10, -5, -5, -10, -10, -20
        };

        private static readonly int[] KingMiddleTable =
        {
            20, 30, 10, 0, 0, 10, 30, 20,
            20, 20, 0, 0, 0, 0, 20, 20,
            -10, -20, -20, -20, -20, -20, -20, -10,
            -20, -30, -30, -40, -40, -30, -30, -20,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30
        };

        private static readonly int[] KingEndTable =
        {
            -50, -30, -30, -30, -30, -30, -30, -50,
            -30, -30, 0, 0, 0, 0, -30, -30,
            -30, -10, 20, 30, 30, 20, -10, -30,
            -30, -10, 30, 40, 40, 30, -10, -30,
            -30, -10, 30, 40, 40, 30, -10, -30,
            -30, -10, 20, 30, 30, 20, -10, -30,
            -30, -20, -10, 0, 0, -10, -20, -30,
            -50, -40, -30, -20, -20, -30, -40, -50
        };

        private readonly List<ChessMove>[] moveStacks =
            new List<ChessMove>[MaximumSearchDepth + QuiescenceDepth + 2];
        private readonly int[] orderingScores = new int[256];
        private readonly List<int> rootCandidates = new List<int>(64);
        private readonly int[] rootScores = new int[256];

        private int nodesVisited;
        private int nodeBudget;

        public ChessEngine()
        {
            for (int index = 0; index < moveStacks.Length; index++)
            {
                moveStacks[index] = new List<ChessMove>(72);
            }
        }

        /// <summary>Nodes the last search actually looked at, so a test
        /// can prove the budget is respected.</summary>
        public int LastSearchNodes { get; private set; }

        /// <summary>The score the chosen move was picked at, in
        /// centipawns from the mover's point of view.</summary>
        public int LastRootScore { get; private set; }

        /// <summary>
        /// Picks one of <paramref name="rootMoves"/> for the side to
        /// move. Returns its index, or `-1` when there is nothing to
        /// play. The list must be the legal moves of
        /// <paramref name="position"/>; it is never regenerated here,
        /// so the caller and the engine cannot disagree about what was
        /// on offer.
        /// </summary>
        public int Choose(
            ChessPosition position,
            IReadOnlyList<ChessMove> rootMoves,
            ChessEngineSettings settings,
            ref uint randomState)
        {
            if (position == null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            if (rootMoves == null)
            {
                throw new ArgumentNullException(nameof(rootMoves));
            }

            if (rootMoves.Count == 0)
            {
                LastSearchNodes = 0;
                LastRootScore = 0;
                return -1;
            }

            nodesVisited = 0;
            nodeBudget = Math.Max(1024, settings.NodeBudget);
            int depth = Math.Clamp(settings.Depth, 1, MaximumSearchDepth);
            int slack = Math.Max(0, settings.SlackCentipawns);
            if (settings.BlunderOneIn > 1 &&
                NextRandom(ref randomState) % (uint)settings.BlunderOneIn == 0)
            {
                slack = Math.Max(
                    slack,
                    Math.Max(0, settings.BlunderSlackCentipawns));
            }

            int best = int.MinValue;
            for (int index = 0; index < rootMoves.Count; index++)
            {
                ChessMove move = rootMoves[index];
                ChessUndo undo = position.MakeMove(move);

                // The window opens at the best score so far minus the
                // slack, so every move the last stage might still pick
                // comes back with an exact score rather than a bound.
                int alpha = best == int.MinValue
                    ? -MateScore * 2
                    : best - slack - 1;
                int score = -Search(
                    position,
                    depth - 1,
                    1,
                    -MateScore * 2,
                    -alpha);
                position.UnmakeMove(move, undo);
                rootScores[index] = score;
                if (score > best)
                {
                    best = score;
                }
            }

            rootCandidates.Clear();
            for (int index = 0; index < rootMoves.Count; index++)
            {
                if (rootScores[index] >= best - slack)
                {
                    rootCandidates.Add(index);
                }
            }

            // A forced mate is never thrown away for flavour: an old
            // man who misses mate in one every third game is not a
            // weak player, he is a broken one.
            if (best >= MateScore - MaximumSearchDepth)
            {
                rootCandidates.Clear();
                for (int index = 0; index < rootMoves.Count; index++)
                {
                    if (rootScores[index] == best)
                    {
                        rootCandidates.Add(index);
                    }
                }
            }

            if (rootCandidates.Count == 0)
            {
                rootCandidates.Add(0);
            }

            int chosen = rootCandidates[
                (int)(NextRandom(ref randomState) %
                      (uint)rootCandidates.Count)];
            LastSearchNodes = nodesVisited;
            LastRootScore = rootScores[chosen];
            return chosen;
        }

        /// <summary>
        /// The static score of a position, in centipawns, from the side
        /// to move's point of view.
        /// </summary>
        public static int Evaluate(ChessPosition position)
        {
            if (position == null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            int material = 0;
            int whiteScore = 0;
            int blackScore = 0;
            for (int square = 0; square < 64; square++)
            {
                byte piece = position[square];
                if (piece == ChessPiece.None)
                {
                    continue;
                }

                byte type = ChessPiece.Type(piece);
                if (type != ChessPiece.King && type != ChessPiece.Pawn)
                {
                    material += PieceValues[type];
                }
            }

            bool endgame = material <= 1300;
            for (int square = 0; square < 64; square++)
            {
                byte piece = position[square];
                if (piece == ChessPiece.None)
                {
                    continue;
                }

                byte type = ChessPiece.Type(piece);
                bool white = ChessPiece.IsWhite(piece);
                int mirrored = white
                    ? square
                    : (7 - ChessPosition.RankOf(square)) * 8 +
                      ChessPosition.FileOf(square);
                int value = PieceValues[type] +
                            ReadTable(type, mirrored, endgame);
                if (white)
                {
                    whiteScore += value;
                }
                else
                {
                    blackScore += value;
                }
            }

            int score = whiteScore - blackScore;
            return position.WhiteToMove ? score : -score;
        }

        private static int ReadTable(
            byte type,
            int square,
            bool endgame)
        {
            switch (type)
            {
                case ChessPiece.Pawn:
                    return PawnTable[square];
                case ChessPiece.Knight:
                    return KnightTable[square];
                case ChessPiece.Bishop:
                    return BishopTable[square];
                case ChessPiece.Rook:
                    return RookTable[square];
                case ChessPiece.Queen:
                    return QueenTable[square];
                case ChessPiece.King:
                    return endgame
                        ? KingEndTable[square]
                        : KingMiddleTable[square];
                default:
                    return 0;
            }
        }

        private int Search(
            ChessPosition position,
            int depth,
            int ply,
            int alpha,
            int beta)
        {
            nodesVisited++;
            if (nodesVisited >= nodeBudget)
            {
                return Evaluate(position);
            }

            if (position.HalfmoveClock >= ChessPosition.QuietPlyLimit ||
                position.IsInsufficientMaterial())
            {
                return 0;
            }

            if (depth <= 0)
            {
                return Quiesce(position, ply, QuiescenceDepth, alpha, beta);
            }

            List<ChessMove> moves = moveStacks[ply];
            position.GenerateLegal(moves);
            if (moves.Count == 0)
            {
                return position.IsInCheck(position.WhiteToMove)
                    ? -MateScore + ply
                    : 0;
            }

            OrderMoves(position, moves);
            int best = -MateScore * 2;
            for (int index = 0; index < moves.Count; index++)
            {
                ChessMove move = moves[index];
                ChessUndo undo = position.MakeMove(move);
                int score = -Search(
                    position,
                    depth - 1,
                    ply + 1,
                    -beta,
                    -alpha);
                position.UnmakeMove(move, undo);
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

        private int Quiesce(
            ChessPosition position,
            int ply,
            int depth,
            int alpha,
            int beta)
        {
            nodesVisited++;
            int standPat = Evaluate(position);
            if (depth <= 0 || nodesVisited >= nodeBudget)
            {
                return standPat;
            }

            if (standPat >= beta)
            {
                return standPat;
            }

            if (standPat > alpha)
            {
                alpha = standPat;
            }

            List<ChessMove> moves = moveStacks[ply];
            position.GenerateLegalCaptures(moves);
            OrderMoves(position, moves);
            int best = standPat;
            for (int index = 0; index < moves.Count; index++)
            {
                ChessMove move = moves[index];
                ChessUndo undo = position.MakeMove(move);
                int score = -Quiesce(
                    position,
                    ply + 1,
                    depth - 1,
                    -beta,
                    -alpha);
                position.UnmakeMove(move, undo);
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

        /// <summary>
        /// Most valuable victim, least valuable attacker, then
        /// promotions. Insertion sort because the lists are short and
        /// the alternative allocates a comparer per node.
        /// </summary>
        private void OrderMoves(
            ChessPosition position,
            List<ChessMove> moves)
        {
            int count = Math.Min(moves.Count, orderingScores.Length);
            for (int index = 0; index < count; index++)
            {
                ChessMove move = moves[index];
                int score = 0;
                if (move.IsCapture)
                {
                    byte victim = ChessPiece.Type(position[move.To]);
                    byte attacker = ChessPiece.Type(position[move.From]);
                    score = 1000 +
                            PieceValues[victim] * 8 -
                            PieceValues[attacker];
                }

                if (move.IsPromotion)
                {
                    score += 900 + PieceValues[move.Promotion];
                }

                orderingScores[index] = score;
            }

            for (int index = 1; index < count; index++)
            {
                ChessMove move = moves[index];
                int score = orderingScores[index];
                int slot = index - 1;
                while (slot >= 0 && orderingScores[slot] < score)
                {
                    moves[slot + 1] = moves[slot];
                    orderingScores[slot + 1] = orderingScores[slot];
                    slot--;
                }

                moves[slot + 1] = move;
                orderingScores[slot + 1] = score;
            }
        }

        /// <summary>The watchman's xorshift, shared by everything in
        /// this city that has to be random and repeatable.</summary>
        internal static uint NextRandom(ref uint state)
        {
            uint value = state == 0u ? 0x9E3779B9u : state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }
    }
}
