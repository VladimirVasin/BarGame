using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Metre contract for the staff opening and its two floor sections.</summary>
    internal static class CityIndustrialEntranceGeometry
    {
        // tools/city_industrial_parts.py exports the same openings.
        private static readonly float[] EntranceX = { 2.5f, 1.4f, 2.8f, 2.85f, 1.55f, 1.16f, 2.34f, -.90f };
        internal const float HalfWidth = .70f;
        internal const float Height = 2.4f;
        internal const float Depth = 4.15f;
        private const float TurnFront = 2.5f;
        private const float BayRight = 3.2f;
        private const float PassageCentreDepth = 3.32f;

        internal static Vector3 Threshold(int variant) =>
            new Vector3(EntranceX[variant], 0f,
                CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.Industrial, variant).z * .5f);

        internal static Vector3 Bend(Vector3 threshold) => threshold - Vector3.forward * PassageCentreDepth;

        internal static Vector3 Interior(Vector3 threshold) => Bend(threshold) + Vector3.right * 2.65f;

        internal static bool HidesBody(Vector3 camera, Vector3 feet)
        {
            // Coordinates relative to the threshold. From outside the shell,
            // every sight line must pass both ends of the straight door throat.
            // Allow for the bevels, head, sleeves and feet in the turn.
            const float shellOpening = .84f;
            const float bodyHalf = .49f;
            const float throatDepth = TurnFront - .06f;
            if (feet.x - bodyHalf < shellOpening || feet.z + bodyHalf >= -throatDepth ||
                feet.z - bodyHalf <= -Depth) return false;
            if (camera.z < 0f)
            {
                bool inVestibule = camera.y >= -.04f && camera.y <= 2.52f && camera.z >= -Depth - .05f &&
                    camera.x >= -shellOpening && camera.x <= (camera.z <= -throatDepth ? BayRight + .05f : shellOpening);
                return !inVestibule;
            }

            // Even a diagonal sight line through opposite jambs cannot see
            // farther sideways than this at the depth of the far shoulder.
            float farDepth = -feet.z + bodyHalf;
            float visibleRight = shellOpening + 2f * shellOpening * (farDepth - throatDepth) / throatDepth;
            return feet.x - bodyHalf > visibleRight;
        }

        // The two rectangles share an edge. Keeping them disjoint lets the
        // floor, collision and terrain support agree at the turn.
        internal static Rect Floor(Vector3 threshold, int section)
        {
            float back = threshold.z - Depth;
            return section == 0
                ? Rect.MinMaxRect(threshold.x - HalfWidth, back, threshold.x + HalfWidth, threshold.z)
                : Rect.MinMaxRect(threshold.x + HalfWidth, back, threshold.x + BayRight, threshold.z - TurnFront);
        }

        internal static IReadOnlyList<Bounds> Cut(IReadOnlyList<Bounds> solids, int variant)
        {
            Vector3 threshold = Threshold(variant);
            Rect entry = Floor(threshold, 0), bay = Floor(threshold, 1);
            var result = new List<Bounds>(solids.Count + 5);
            foreach (Bounds solid in solids)
            {
                Vector3 low = solid.min, high = solid.max;
                if (high.x <= entry.xMin || low.x >= bay.xMax || high.z <= entry.yMin || low.y >= Height)
                {
                    result.Add(solid);
                    continue;
                }
                if (!(low.x < entry.xMin && bay.xMax < high.x && low.z < entry.yMin && bay.yMax < high.z))
                    throw new InvalidOperationException("The industrial entrance must fit in one facade section.");

                Add(low.x, low.y, low.z, entry.xMin, high.y, high.z);
                Add(bay.xMax, low.y, low.z, high.x, high.y, high.z);
                Add(entry.xMin, low.y, low.z, bay.xMax, high.y, entry.yMin);
                Add(entry.xMax, low.y, bay.yMax, bay.xMax, high.y, high.z);
                Add(entry.xMin, Height, entry.yMin, entry.xMax, high.y, high.z);
                Add(entry.xMax, Height, entry.yMin, bay.xMax, high.y, bay.yMax);
            }
            return result;

            void Add(float x, float y, float z, float maxX, float maxY, float maxZ)
            {
                Bounds piece = default;
                piece.SetMinMax(new Vector3(x, y, z), new Vector3(maxX, maxY, maxZ));
                result.Add(piece);
            }
        }
    }
}
