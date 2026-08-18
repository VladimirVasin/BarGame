using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The men of one live board, one object each, and the hand that
    /// moves them.
    ///
    /// The static set is four combined batches because nothing on it
    /// ever moves. A game does, so the table being played at trades
    /// those two batches for a piece per man — thirty-two at most, and
    /// only while a game exists on that table. They are built from the
    /// same seven meshes, wear the same timber sheet and stand on the
    /// same lattice, so the swap is invisible.
    ///
    /// Animation is deliberately literal: a man is lifted, carried and
    /// set down, one step at a time, and a chain of draughts jumps is
    /// as many carries as it has jumps. A move that resolved instantly
    /// would leave the player unable to see what the other man just
    /// did to him.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBoardGamePieces : MonoBehaviour
    {
        /// <summary>How long one carry takes.</summary>
        public const float StepSeconds = 0.30f;

        /// <summary>And how high the man is lifted on the way.</summary>
        public const float LiftMeters = 0.045f;

        /// <summary>A taken man is lifted clear and put away.</summary>
        public const float SweepSeconds = 0.34f;
        public const float SweepLiftMeters = 0.16f;

        private const float MaximumYawJitterDegrees = 5f;

        private readonly Dictionary<int, PieceView> bySquare =
            new Dictionary<int, PieceView>(64);
        private readonly List<PieceView> views = new List<PieceView>(48);
        private readonly List<PieceView> sweeping =
            new List<PieceView>(8);

        private CityBoardGameTable table;
        private CityChessSetProvider provider;
        private BoardGameTurn activeTurn;
        private int activeStep;
        private float stepElapsed;
        private PieceView carried;
        private Vector3 carryFrom;
        private Vector3 carryTo;
        private Quaternion carryFromRotation;
        private Quaternion carryToRotation;

        public bool IsAnimating => activeTurn != null;

        /// <summary>The square the last carried man was set down on,
        /// for the marker that reminds the player what just happened.
        /// `-1` before the first move.</summary>
        public int LastMoveFromSquare { get; private set; } = -1;
        public int LastMoveToSquare { get; private set; } = -1;

        public static int SquareKey(int file, int rank)
        {
            return rank * CityChessBoardGeometry.SquaresPerSide + file;
        }

        public static CityBoardGamePieces Create(
            Transform parent,
            CityBoardGameTable table,
            CityChessSetProvider provider)
        {
            if (parent == null ||
                !table.IsPresent ||
                provider == null ||
                !provider.IsComplete())
            {
                return null;
            }

            var host = new GameObject(
                $"Board Game Men {table.Game}");
            host.transform.SetParent(parent, false);
            var pieces = host.AddComponent<CityBoardGamePieces>();
            pieces.table = table;
            pieces.provider = provider;
            return pieces;
        }

        /// <summary>
        /// Puts every man where the match says he stands, creating and
        /// pooling views as the position needs them. Called once when
        /// the board is raised and again at the end of every move, so a
        /// presentation that ever drifted from the rules is corrected
        /// within one move rather than compounding.
        /// </summary>
        public void Sync(IReadOnlyList<BoardGamePiecePlacement> pieces)
        {
            if (pieces == null)
            {
                return;
            }

            for (int index = 0; index < views.Count; index++)
            {
                views[index].Claimed = false;
            }

            bySquare.Clear();
            for (int index = 0; index < pieces.Count; index++)
            {
                BoardGamePiecePlacement placement = pieces[index];
                PieceView view = Acquire(
                    placement.Kind,
                    placement.IsLight,
                    placement.IsCrowned);
                view.Claimed = true;
                Place(view, placement.File, placement.Rank);
                bySquare[SquareKey(placement.File, placement.Rank)] =
                    view;
            }

            for (int index = 0; index < views.Count; index++)
            {
                PieceView view = views[index];
                if (!view.Claimed && view.Root.activeSelf)
                {
                    view.Root.SetActive(false);
                }
            }

            sweeping.Clear();
        }

        /// <summary>Starts playing one applied turn. The board is not
        /// interactive again until <see cref="IsAnimating"/> clears.</summary>
        public void BeginTurn(BoardGameTurn turn)
        {
            if (turn == null || turn.Steps.Count == 0)
            {
                return;
            }

            activeTurn = turn;
            activeStep = -1;
            LastMoveFromSquare = SquareKey(
                turn.Steps[0].FromFile,
                turn.Steps[0].FromRank);
            LastMoveToSquare = turn.EndFile >= 0
                ? SquareKey(turn.EndFile, turn.EndRank)
                : LastMoveToSquare;
            AdvanceStep();
        }

        /// <summary>Ends any carry immediately, for a board that is
        /// being torn down or left mid-move.</summary>
        public void CancelTurn()
        {
            activeTurn = null;
            carried = null;
            sweeping.Clear();
        }

        public void Dispose()
        {
            CancelTurn();
            if (this != null && gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>The world position a man on this square stands
        /// on.</summary>
        public Vector3 SquarePosition(int file, int rank)
        {
            return table.SquareCenter(file, rank);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            UpdateSweeps(deltaTime);
            if (activeTurn == null || carried == null)
            {
                return;
            }

            stepElapsed += deltaTime;
            float amount = StepSeconds > 0f
                ? Mathf.Clamp01(stepElapsed / StepSeconds)
                : 1f;
            float smooth = Mathf.SmoothStep(0f, 1f, amount);
            Vector3 position = Vector3.Lerp(carryFrom, carryTo, smooth);
            position.y += Mathf.Sin(smooth * Mathf.PI) * LiftMeters;
            carried.Root.transform.SetPositionAndRotation(
                position,
                Quaternion.Slerp(
                    carryFromRotation,
                    carryToRotation,
                    smooth));
            if (amount < 1f)
            {
                return;
            }

            AdvanceStep();
        }

        private void AdvanceStep()
        {
            if (activeTurn == null)
            {
                return;
            }

            activeStep++;
            if (activeStep >= activeTurn.Steps.Count)
            {
                FinishTurn();
                return;
            }

            BoardGameStep step = activeTurn.Steps[activeStep];
            int fromKey = SquareKey(step.FromFile, step.FromRank);
            if (!bySquare.TryGetValue(fromKey, out PieceView view))
            {
                // Nothing on the square the rules say a man left. The
                // end-of-turn sync repairs it; skipping the carry is
                // better than carrying the wrong man.
                AdvanceStep();
                return;
            }

            bySquare.Remove(fromKey);
            if (step.HasCapture)
            {
                int capturedKey = SquareKey(
                    step.CapturedFile,
                    step.CapturedRank);
                if (bySquare.TryGetValue(
                        capturedKey,
                        out PieceView victim))
                {
                    bySquare.Remove(capturedKey);
                    victim.SweepElapsed = 0f;
                    victim.SweepOrigin =
                        victim.Root.transform.position;
                    sweeping.Add(victim);
                }
            }

            carried = view;
            carryFrom = view.Root.transform.position;
            carryTo = table.SquareCenter(step.ToFile, step.ToRank);
            carryFromRotation = view.Root.transform.rotation;
            carryToRotation = SquareRotation(
                step.ToFile,
                step.ToRank,
                view.IsLight);
            stepElapsed = 0f;
            bySquare[SquareKey(step.ToFile, step.ToRank)] = view;
        }

        private void FinishTurn()
        {
            if (activeTurn.Promoted &&
                activeTurn.EndFile >= 0 &&
                bySquare.TryGetValue(
                    SquareKey(activeTurn.EndFile, activeTurn.EndRank),
                    out PieceView promoted))
            {
                if (activeTurn.PromotedToCrown)
                {
                    SetCrowned(promoted, true);
                }
                else
                {
                    Rebuild(
                        promoted,
                        activeTurn.PromotedKind,
                        promoted.IsLight,
                        false);
                    Place(
                        promoted,
                        activeTurn.EndFile,
                        activeTurn.EndRank);
                }
            }

            activeTurn = null;
            carried = null;
        }

        private void UpdateSweeps(float deltaTime)
        {
            for (int index = sweeping.Count - 1; index >= 0; index--)
            {
                PieceView view = sweeping[index];
                view.SweepElapsed += deltaTime;
                float amount = SweepSeconds > 0f
                    ? Mathf.Clamp01(view.SweepElapsed / SweepSeconds)
                    : 1f;
                Vector3 position = view.SweepOrigin;
                position.y += amount * SweepLiftMeters;

                // Taken men leave over the edge of the slab on the side
                // whoever took them is sitting at, which is the way a
                // hand actually does it.
                position += table.SeatFacing *
                    (amount * -0.35f);
                view.Root.transform.position = position;
                if (amount < 1f)
                {
                    continue;
                }

                view.Root.SetActive(false);
                sweeping.RemoveAt(index);
            }
        }

        private PieceView Acquire(
            CityChessPieceKind kind,
            bool isLight,
            bool crowned)
        {
            for (int index = 0; index < views.Count; index++)
            {
                PieceView candidate = views[index];
                if (candidate.Claimed ||
                    candidate.Kind != kind ||
                    candidate.IsLight != isLight)
                {
                    continue;
                }

                candidate.Root.SetActive(true);
                SetCrowned(candidate, crowned);
                return candidate;
            }

            for (int index = 0; index < views.Count; index++)
            {
                PieceView candidate = views[index];
                if (candidate.Claimed)
                {
                    continue;
                }

                Rebuild(candidate, kind, isLight, crowned);
                return candidate;
            }

            var view = new PieceView();
            views.Add(view);
            Rebuild(view, kind, isLight, crowned);
            return view;
        }

        private void Rebuild(
            PieceView view,
            CityChessPieceKind kind,
            bool isLight,
            bool crowned)
        {
            if (view.Root != null)
            {
                Destroy(view.Root);
                view.Root = null;
                view.Crown = null;
            }

            view.Kind = kind;
            view.IsLight = isLight;
            view.Crowned = false;
            view.Root = CreateManObject(kind, isLight, transform);
            SetCrowned(view, crowned);
        }

        private void SetCrowned(PieceView view, bool crowned)
        {
            if (view.Crowned == crowned)
            {
                return;
            }

            view.Crowned = crowned;
            if (!crowned)
            {
                if (view.Crown != null)
                {
                    Destroy(view.Crown);
                    view.Crown = null;
                }

                return;
            }

            // A king is a second man set on top of the first, which is
            // exactly how the two of them would crown one.
            view.Crown = CreateManObject(
                view.Kind,
                view.IsLight,
                view.Root.transform);
            Mesh mesh = provider.GetMesh(view.Kind);
            float height = mesh != null ? mesh.bounds.size.y : 0.02f;
            view.Crown.transform.localPosition =
                Vector3.up * height;
            view.Crown.transform.localRotation = Quaternion.identity;
        }

        private GameObject CreateManObject(
            CityChessPieceKind kind,
            bool isLight,
            Transform parent)
        {
            Color color = isLight
                ? CityChessSetWorldBuilder.LightManColor
                : CityChessSetWorldBuilder.DarkManColor;
            var placement = new[]
            {
                new RuntimeMeshPlacement(
                    provider.GetMesh(kind),
                    Vector3.zero,
                    Quaternion.identity)
            };
            GameObject man = RuntimePrimitiveFactory.CreateCombinedMeshes(
                $"{(isLight ? "Light" : "Dark")} {kind}",
                parent,
                placement,
                color,
                false,
                CityChessSetWorldBuilder.ManSurfaceMetersPerTile,
                CityChessSetWorldBuilder.ManSurfaceUvMode,
                Vector3.zero);
            Renderer renderer = man.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            CityChessSetWorldBuilder.ApplyManAppearance(renderer, isLight);
            return man;
        }

        private void Place(PieceView view, int file, int rank)
        {
            view.Root.SetActive(true);
            view.Root.transform.SetPositionAndRotation(
                table.SquareCenter(file, rank),
                SquareRotation(file, rank, view.IsLight));
        }

        /// <summary>
        /// Which way a man on this square is turned. Light looks up the
        /// board and dark looks back down it, and both are set down a
        /// few degrees off square, because nobody has ever placed a
        /// chess piece exactly straight.
        /// </summary>
        private Quaternion SquareRotation(
            int file,
            int rank,
            bool isLight)
        {
            unchecked
            {
                uint hash = (uint)(file * 73856093 ^ rank * 19349663);
                hash ^= hash << 13;
                hash ^= hash >> 17;
                float jitter =
                    (hash / (float)uint.MaxValue * 2f - 1f) *
                    MaximumYawJitterDegrees;
                return Quaternion.LookRotation(
                        table.Forward,
                        Vector3.up) *
                    Quaternion.Euler(
                        0f,
                        (isLight ? 0f : 180f) + jitter,
                        0f);
            }
        }

        private sealed class PieceView
        {
            public GameObject Root;
            public GameObject Crown;
            public CityChessPieceKind Kind;
            public bool IsLight;
            public bool Crowned;
            public bool Claimed;
            public float SweepElapsed;
            public Vector3 SweepOrigin;
        }
    }
}
