using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>Which of the two park games a board is playing.</summary>
    public enum CityBoardGameKind
    {
        Chess = 0,
        Draughts = 1
    }

    /// <summary>
    /// Which army. `Light` is bone and `Dark` is bog oak, exactly as the
    /// set on the table was turned, and in both games light opens. The
    /// two old men are light at their own boards, so the hero is always
    /// dark and always answers rather than starts — which is the whole
    /// tone of walking up to a man who has been sitting there for years.
    /// </summary>
    public enum BoardGameSide
    {
        Light = 0,
        Dark = 1
    }

    public enum BoardGameStatus
    {
        Playing = 0,
        LightWins = 1,
        DarkWins = 2,
        Draw = 3
    }

    /// <summary>Why the game stopped, for the line under the board.</summary>
    public enum BoardGameEnding
    {
        None = 0,
        Checkmate = 1,
        Stalemate = 2,
        Blocked = 3,
        Swept = 4,
        InsufficientMaterial = 5,
        QuietMoveLimit = 6,
        Repetition = 7
    }

    /// <summary>
    /// One man where he currently stands, in the lattice
    /// `CityChessBoardGeometry` owns: `file` along the recipe tangent,
    /// `rank` along its forward. The presentation reads nothing else.
    /// </summary>
    public readonly struct BoardGamePiecePlacement
    {
        public BoardGamePiecePlacement(
            int file,
            int rank,
            CityChessPieceKind kind,
            bool isLight,
            bool isCrowned)
        {
            File = file;
            Rank = rank;
            Kind = kind;
            IsLight = isLight;
            IsCrowned = isCrowned;
        }

        public int File { get; }
        public int Rank { get; }
        public CityChessPieceKind Kind { get; }
        public bool IsLight { get; }

        /// <summary>A draughts king, which is drawn as a second man
        /// stacked on the first the way the two of them would do it.
        /// Never set in chess: a promoted pawn is simply a queen.</summary>
        public bool IsCrowned { get; }
    }

    /// <summary>
    /// One offer the side to move has, in lattice squares. `Index` is
    /// its place in the match's current legal list, which is what an
    /// apply takes: two draughts captures can share a start and an end
    /// and still be different moves.
    /// </summary>
    public readonly struct BoardGameAction
    {
        public BoardGameAction(
            int index,
            int fromFile,
            int fromRank,
            int toFile,
            int toRank,
            bool isCapture,
            bool isPromotion)
        {
            Index = index;
            FromFile = fromFile;
            FromRank = fromRank;
            ToFile = toFile;
            ToRank = toRank;
            IsCapture = isCapture;
            IsPromotion = isPromotion;
        }

        public int Index { get; }
        public int FromFile { get; }
        public int FromRank { get; }
        public int ToFile { get; }
        public int ToRank { get; }
        public bool IsCapture { get; }
        public bool IsPromotion { get; }
    }

    /// <summary>
    /// One hand movement inside a turn: a man travels from one square
    /// to another and, on the way, one square is emptied. A castle is
    /// two steps and no capture; a draughts chain is as many steps as
    /// it has jumps.
    /// </summary>
    public readonly struct BoardGameStep
    {
        public BoardGameStep(
            int fromFile,
            int fromRank,
            int toFile,
            int toRank,
            int capturedFile,
            int capturedRank)
        {
            FromFile = fromFile;
            FromRank = fromRank;
            ToFile = toFile;
            ToRank = toRank;
            CapturedFile = capturedFile;
            CapturedRank = capturedRank;
        }

        public int FromFile { get; }
        public int FromRank { get; }
        public int ToFile { get; }
        public int ToRank { get; }

        /// <summary>The square a man is lifted off, or `-1` when this
        /// step takes nothing. Not always the step's own destination:
        /// an en-passant pawn is taken off the square behind it.</summary>
        public int CapturedFile { get; }
        public int CapturedRank { get; }
        public bool HasCapture => CapturedFile >= 0;
    }

    /// <summary>
    /// What one applied move actually did, in the order a hand would do
    /// it. Reused between turns rather than reallocated: the board
    /// churns one of these every couple of seconds for as long as the
    /// hero sits there.
    /// </summary>
    public sealed class BoardGameTurn
    {
        private readonly List<BoardGameStep> steps =
            new List<BoardGameStep>(8);

        public IReadOnlyList<BoardGameStep> Steps => steps;

        /// <summary>Where the moved man ends the turn.</summary>
        public int EndFile { get; private set; } = -1;
        public int EndRank { get; private set; } = -1;

        /// <summary>What he turned into there, or `None` when a man is
        /// still what he was.</summary>
        public CityChessPieceKind PromotedKind { get; private set; }
        public bool Promoted { get; private set; }

        /// <summary>True when the promotion is a draughts crowning
        /// rather than a chess pawn becoming another piece.</summary>
        public bool PromotedToCrown { get; private set; }

        public bool CapturedAnything { get; private set; }

        public void Clear()
        {
            steps.Clear();
            EndFile = -1;
            EndRank = -1;
            PromotedKind = CityChessPieceKind.Pawn;
            Promoted = false;
            PromotedToCrown = false;
            CapturedAnything = false;
        }

        public void AddStep(
            int fromFile,
            int fromRank,
            int toFile,
            int toRank,
            int capturedFile = -1,
            int capturedRank = -1)
        {
            steps.Add(new BoardGameStep(
                fromFile,
                fromRank,
                toFile,
                toRank,
                capturedFile,
                capturedRank));
            EndFile = toFile;
            EndRank = toRank;
            CapturedAnything |= capturedFile >= 0;
        }

        /// <summary>
        /// Where the man who made the move actually ends up, when that
        /// is not the last step added. A castle moves the rook second
        /// and the king is still the mover.
        /// </summary>
        public void SetEnd(int file, int rank)
        {
            EndFile = file;
            EndRank = rank;
        }

        public void SetPromotion(
            CityChessPieceKind kind,
            bool toCrown)
        {
            Promoted = true;
            PromotedKind = kind;
            PromotedToCrown = toCrown;
        }
    }

    /// <summary>
    /// One game in progress on one of the two park tables. Both
    /// implementations are pure C# over a plain array, so a whole match
    /// can be played out in a test without a scene, a mesh or a camera.
    /// </summary>
    public interface IBoardGameMatch
    {
        CityBoardGameKind Kind { get; }
        BoardGameSide SideToMove { get; }
        BoardGameStatus Status { get; }
        BoardGameEnding Ending { get; }

        /// <summary>Chess only: the side to move stands in check.</summary>
        bool CheckPending { get; }

        /// <summary>How many full moves have been played from the
        /// opening position, for the line under the board.</summary>
        int PliesPlayed { get; }

        IReadOnlyList<BoardGamePiecePlacement> Pieces { get; }
        IReadOnlyList<BoardGameAction> LegalActions { get; }

        /// <summary>
        /// Plays one of <see cref="LegalActions"/> and describes it into
        /// <paramref name="turn"/>. False when the index is not a legal
        /// offer, which the caller is allowed to treat as a misclick.
        /// </summary>
        bool TryApply(int actionIndex, BoardGameTurn turn);

        /// <summary>
        /// What the man opposite would play. Returns an index into
        /// <see cref="LegalActions"/>, or `-1` when he has nothing.
        /// </summary>
        int ChooseAction(ref uint randomState);

        void Reset();
    }
}
