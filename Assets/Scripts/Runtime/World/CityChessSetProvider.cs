using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Which man on the board, by shape rather than by side.</summary>
    public enum CityChessPieceKind
    {
        Pawn = 0,
        Rook = 1,
        Knight = 2,
        Bishop = 3,
        Queen = 4,
        King = 5,
        Draught = 6
    }

    /// <summary>
    /// The seven authored meshes of the park chess set, carried into a
    /// build by one serialized asset — the staged-provider pattern the
    /// fisherman and the two park players already use, with meshes in
    /// place of a prefab because nothing here needs a GameObject of its
    /// own. Fifty-six men on two boards reach the frame as four combined
    /// meshes.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CityChessSetProvider",
        menuName = "Bar Promenade/City Chess Set Provider")]
    public sealed class CityChessSetProvider : ScriptableObject
    {
        public const string ResourcePath = "City/CityChessSetProvider";
        public const string DesignId = "city_chess_set_v1";

        [SerializeField] private Mesh pawn;
        [SerializeField] private Mesh rook;
        [SerializeField] private Mesh knight;
        [SerializeField] private Mesh bishop;
        [SerializeField] private Mesh queen;
        [SerializeField] private Mesh king;
        [SerializeField] private Mesh draught;
        [SerializeField] private string buildSignature = string.Empty;

        public string BuildSignature => buildSignature;

        public static CityChessSetProvider Load()
        {
            return Resources.Load<CityChessSetProvider>(ResourcePath);
        }

        public Mesh GetMesh(CityChessPieceKind kind)
        {
            switch (kind)
            {
                case CityChessPieceKind.Pawn:
                    return pawn;
                case CityChessPieceKind.Rook:
                    return rook;
                case CityChessPieceKind.Knight:
                    return knight;
                case CityChessPieceKind.Bishop:
                    return bishop;
                case CityChessPieceKind.Queen:
                    return queen;
                case CityChessPieceKind.King:
                    return king;
                case CityChessPieceKind.Draught:
                    return draught;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        /// <summary>True only when every one of the seven is bound.</summary>
        public bool IsComplete()
        {
            foreach (CityChessPieceKind kind in
                     (CityChessPieceKind[])Enum.GetValues(
                         typeof(CityChessPieceKind)))
            {
                if (GetMesh(kind) == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
