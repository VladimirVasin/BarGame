using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The chalk on the board: what the pointer is over, what the hero
    /// picked up, where it may go, and what the other man just did.
    ///
    /// Flat emissive plates rather than an overlay drawn in screen
    /// space, because the board is a real object seen at a real angle
    /// and a 2D overlay would slide off it the moment the hero turned
    /// his head. They sit a fraction of a millimetre over the square's
    /// own top face, which is enough on a board whose dark squares
    /// already stand `5 mm` proud.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBoardGameMarkers : MonoBehaviour
    {
        public const float PlateThickness = 0.002f;
        public const float PlateHoverMeters = 0.0015f;
        public const float SquarePlateSize = 0.132f;
        public const float DestinationDotSize = 0.048f;
        public const float CaptureRingSize = 0.108f;

        /// <summary>
        /// Extra lift per draw layer (last-move, check, destinations,
        /// selection, hover). Two marks on one square would otherwise
        /// be perfectly coplanar boxes of the same material and
        /// z-fight; 0.6 mm per layer settles the order and is
        /// invisible at reading distance.
        /// </summary>
        public const float PlateLayerStep = 0.0006f;

        private static readonly Color HoverColor =
            new Color(0.72f, 0.62f, 0.34f, 1f);
        private static readonly Color SelectedColor =
            new Color(0.90f, 0.65f, 0.29f, 1f);
        private static readonly Color DestinationColor =
            new Color(0.36f, 0.65f, 0.68f, 1f);
        private static readonly Color CaptureColor =
            new Color(0.79f, 0.30f, 0.41f, 1f);
        private static readonly Color LastMoveColor =
            new Color(0.34f, 0.28f, 0.20f, 1f);
        private static readonly Color CheckColor =
            new Color(0.86f, 0.22f, 0.28f, 1f);

        private readonly List<GameObject> pool = new List<GameObject>(48);

        private CityBoardGameTable table;
        private int used;

        public static CityBoardGameMarkers Create(
            Transform parent,
            CityBoardGameTable table)
        {
            if (parent == null || !table.IsPresent)
            {
                return null;
            }

            var host = new GameObject("Board Game Markers");
            host.transform.SetParent(parent, false);
            var markers = host.AddComponent<CityBoardGameMarkers>();
            markers.table = table;
            return markers;
        }

        /// <summary>
        /// Redraws every mark on the board in one pass. Called whenever
        /// the pointer, the selection or the position changes, which is
        /// cheap: the plates are pooled and only their transforms and
        /// colours are written.
        /// </summary>
        public void Redraw(
            int hoverFile,
            int hoverRank,
            int selectedFile,
            int selectedRank,
            IReadOnlyList<BoardGameAction> destinations,
            int lastFromSquare,
            int lastToSquare,
            int checkFile,
            int checkRank)
        {
            used = 0;
            if (lastFromSquare >= 0)
            {
                AddPlate(
                    lastFromSquare % CityChessBoardGeometry.SquaresPerSide,
                    lastFromSquare / CityChessBoardGeometry.SquaresPerSide,
                    SquarePlateSize,
                    LastMoveColor,
                    0);
            }

            if (lastToSquare >= 0)
            {
                AddPlate(
                    lastToSquare % CityChessBoardGeometry.SquaresPerSide,
                    lastToSquare / CityChessBoardGeometry.SquaresPerSide,
                    SquarePlateSize,
                    LastMoveColor,
                    0);
            }

            if (checkFile >= 0)
            {
                AddPlate(
                    checkFile,
                    checkRank,
                    SquarePlateSize,
                    CheckColor,
                    1);
            }

            if (destinations != null)
            {
                for (int index = 0; index < destinations.Count; index++)
                {
                    BoardGameAction action = destinations[index];
                    AddPlate(
                        action.ToFile,
                        action.ToRank,
                        action.IsCapture
                            ? CaptureRingSize
                            : DestinationDotSize,
                        action.IsCapture
                            ? CaptureColor
                            : DestinationColor,
                        2);
                }
            }

            if (selectedFile >= 0)
            {
                AddPlate(
                    selectedFile,
                    selectedRank,
                    SquarePlateSize,
                    SelectedColor,
                    3);
            }

            if (hoverFile >= 0 &&
                (hoverFile != selectedFile || hoverRank != selectedRank))
            {
                AddPlate(
                    hoverFile,
                    hoverRank,
                    SquarePlateSize,
                    HoverColor,
                    4);
            }

            for (int index = used; index < pool.Count; index++)
            {
                if (pool[index].activeSelf)
                {
                    pool[index].SetActive(false);
                }
            }
        }

        public void Clear()
        {
            Redraw(-1, -1, -1, -1, null, -1, -1, -1, -1);
        }

        public void Dispose()
        {
            if (this != null && gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        private void AddPlate(
            int file,
            int rank,
            float size,
            Color color,
            int layer)
        {
            if (!CityChessBoardGeometry.IsOnBoard(file, rank))
            {
                return;
            }

            GameObject plate;
            if (used < pool.Count)
            {
                plate = pool[used];
                plate.SetActive(true);
            }
            else
            {
                plate = RuntimePrimitiveFactory.CreateBox(
                    "Board Mark",
                    transform,
                    Vector3.zero,
                    Vector3.one,
                    color,
                    CityNightResources.EmissiveMaterial,
                    false);
                Renderer created = plate.GetComponent<Renderer>();
                created.shadowCastingMode = ShadowCastingMode.Off;
                created.receiveShadows = false;
                pool.Add(plate);
            }

            used++;
            Vector3 center = table.SquareCenter(file, rank) +
                Vector3.up * (PlateHoverMeters +
                              (PlateThickness * 0.5f) +
                              (layer * PlateLayerStep));
            plate.transform.SetPositionAndRotation(
                center,
                Quaternion.LookRotation(table.Forward, Vector3.up));
            plate.transform.localScale =
                new Vector3(size, PlateThickness, size);
            RuntimePrimitiveFactory.SetColor(
                plate.GetComponent<Renderer>(),
                color);
        }
    }
}
