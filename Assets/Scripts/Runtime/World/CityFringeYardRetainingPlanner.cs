using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityFringeYardPlanner
    {
        private static bool TryGetRetainingCut(
            Rect footprint,
            Vector3 axis,
            Rect? gap,
            Rect reserved,
            out float minimum,
            out float maximum)
        {
            bool cutsReserved = footprint.Overlaps(reserved);
            bool cutsGap = gap.HasValue && footprint.Overlaps(gap.Value);
            if (!cutsReserved && !cutsGap)
            {
                minimum = 0f;
                maximum = 0f;
                return false;
            }

            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            if (cutsReserved)
            {
                IncludeLongRange(reserved, axis, ref minimum, ref maximum);
            }

            if (cutsGap)
            {
                IncludeLongRange(gap.Value, axis, ref minimum, ref maximum);
            }

            minimum -= RetainingOpeningMargin;
            maximum += RetainingOpeningMargin;
            return true;
        }

        private static void IncludeLongRange(
            Rect bounds,
            Vector3 axis,
            ref float minimum,
            ref float maximum)
        {
            float first = Mathf.Abs(axis.x) > 0.5f
                ? bounds.xMin
                : bounds.yMin;
            float second = Mathf.Abs(axis.x) > 0.5f
                ? bounds.xMax
                : bounds.yMax;
            minimum = Mathf.Min(minimum, first);
            maximum = Mathf.Max(maximum, second);
        }

        private static void AddRetainingFragment(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            string areaId,
            string stableId,
            CityFringeYardKind yardKind,
            CityFringeYardStyle style,
            Vector3 axis,
            Vector3 line,
            float minimum,
            float maximum,
            float height,
            Quaternion rotation,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            float fragmentLength = maximum - minimum;
            if (fragmentLength < MinimumRetainingFragmentLength)
            {
                return;
            }

            Vector3 center = SetLongCoordinate(
                line,
                axis,
                (minimum + maximum) * 0.5f);
            AddGroundedPart(
                layout,
                surfaces,
                areaId,
                stableId,
                yardKind == CityFringeYardKind.SouthFloodWorks
                    ? CityFringeYardPartKind.Gabion
                    : CityFringeYardPartKind.RetainingSection,
                style,
                center,
                rotation,
                new Vector3(0.76f, height, fragmentLength),
                true,
                0.18f,
                reserved,
                parts);
        }
    }
}
