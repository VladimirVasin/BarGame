using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bay and floor rhythm every city facade is built on.
    /// <para>
    /// These numbers used to live as literals inside
    /// <see cref="CityWorldBuilder"/>'s window loops and again inside
    /// <see cref="HomeExteriorViewBuilder"/>'s copy of them. That was
    /// survivable while the only thing reading them was the code that wrote
    /// them. It stopped being survivable once the facade albedo had to land
    /// its baked window band on the same grid: a texture whose pitch is
    /// derived from one copy and geometry built from another will drift the
    /// moment either is edited, and the failure looks like "the floors are
    /// wrong" rather than like a stale constant.
    /// </para>
    /// </summary>
    internal static class CityFacadeGrid
    {
        /// <summary>
        /// How far the building mass is lifted off the lot origin. Small, easy
        /// to forget, and the difference between window bands that line up and
        /// window bands that sit three centimetres proud of the glass.
        /// </summary>
        public const float MassBaseElevation = 0.08f;

        public const float FirstFloorCenterY = 1.50f;
        public const float FloorPitch = 2.35f;
        public const float FloorHeightDivisor = 2.60f;
        public const int MinimumFloorCount = 1;
        public const int MaximumFloorCount = 4;

        public const float RowWidthFraction = 0.68f;
        public const float PaneLengthDivisor = 1.90f;
        public const int MinimumPaneCount = 4;
        public const int MaximumPaneCount = 8;
        public const float PaneGap = 0.28f;
        public const float OrdinaryPaneHeight = 0.48f;
        public const float LandmarkPaneHeight = 0.60f;
        public const float FacadeProudOffset = 0.012f;
        public const float PaneThickness = 0.035f;

        public static int ResolveFloorCount(float height)
        {
            return Mathf.Clamp(
                Mathf.FloorToInt(height / FloorHeightDivisor),
                MinimumFloorCount,
                MaximumFloorCount);
        }

        public static float ResolveFloorCenterY(int floor)
        {
            return FirstFloorCenterY + (floor * FloorPitch);
        }

        public static bool IsFloorWithinHeight(int floor, float height)
        {
            return ResolveFloorCenterY(floor) < height - 0.35f;
        }

        /// <summary>
        /// Whether the windowed pair of faces is the one facing along X.
        /// Lots without road frontage fall back to the same -Z facing the
        /// window builder uses.
        /// </summary>
        public static bool FrontageRunsAlongX(BuildingLot lot)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            return lot.HasRoadFrontage &&
                   Mathf.Abs(lot.FrontageDirection.x) > 0.5f;
        }

        /// <summary>
        /// The width of the faces that actually carry windows, measured along
        /// the direction the pane row runs.
        /// </summary>
        public static float ResolveFrontageWidth(BuildingLot lot)
        {
            return FrontageRunsAlongX(lot)
                ? lot.Size.y
                : lot.Size.x;
        }

        public static float ResolveRowLength(float frontageWidth)
        {
            return frontageWidth * RowWidthFraction;
        }

        public static int ResolvePaneCount(float rowLength)
        {
            return Mathf.Clamp(
                Mathf.FloorToInt(rowLength / PaneLengthDivisor),
                MinimumPaneCount,
                MaximumPaneCount);
        }

        public static float ResolvePaneLength(float rowLength, int paneCount)
        {
            return (rowLength - ((paneCount - 1) * PaneGap)) / paneCount;
        }

        /// <summary>
        /// Centre-to-centre spacing of the panes, which is also the width of
        /// one authored bay cell in the facade albedo.
        /// </summary>
        public static float ResolveBayPitch(BuildingLot lot)
        {
            float rowLength = ResolveRowLength(ResolveFrontageWidth(lot));
            int paneCount = ResolvePaneCount(rowLength);
            return ResolvePaneLength(rowLength, paneCount) + PaneGap;
        }

        public static int ResolvePaneCount(BuildingLot lot)
        {
            return ResolvePaneCount(
                ResolveRowLength(ResolveFrontageWidth(lot)));
        }

        public static float ResolvePaneHeight(BuildingLot lot)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            return lot.IsBar || lot.IsPlayerHome || lot.IsSupermarket
                ? LandmarkPaneHeight
                : OrdinaryPaneHeight;
        }

        /// <summary>
        /// Offset of pane <paramref name="pane"/> from the centre of its face.
        /// <para>
        /// Algebraically this is <c>bayPitch * (pane - paneCount / 2 + 0.5)</c>,
        /// which is what makes the face centre fall on a bay boundary for an
        /// even pane count and on a bay centre for an odd one. The facade
        /// albedo's UV phase is derived from exactly that parity.
        /// </para>
        /// </summary>
        public static float ResolvePaneOffset(
            float rowLength,
            int paneCount,
            int pane)
        {
            float paneLength = ResolvePaneLength(rowLength, paneCount);
            return -rowLength * 0.5f +
                   (paneLength * 0.5f) +
                   (pane * (paneLength + PaneGap));
        }
    }
}
