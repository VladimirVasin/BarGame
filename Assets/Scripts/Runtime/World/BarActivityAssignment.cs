using System;
using UnityEngine;

namespace BarPromenade
{
    public static class BarActivityAssignment
    {
        public static readonly Vector2Int DefaultHomeBarCell =
            new Vector2Int(12, 6);

        public const BarActivityKind DefaultHomeBarActivity =
            BarActivityKind.SplitTheG;

        public static BarActivityKind Resolve(int rowMajorOrdinal)
        {
            if (rowMajorOrdinal < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rowMajorOrdinal));
            }

            switch (rowMajorOrdinal)
            {
                case 1:
                    return BarActivityKind.BeerPong;
                case 2:
                    return BarActivityKind.SplitTheG;
                case 3:
                    return BarActivityKind.TinctureMatch;
                default:
                    return BarActivityKind.Cocktail;
            }
        }

        public static BarActivityKind Resolve(
            string blueprintId,
            int citySeed,
            Vector2Int cell,
            int rowMajorOrdinal)
        {
            return IsDefaultHomeBar(blueprintId, citySeed, cell)
                ? DefaultHomeBarActivity
                : Resolve(rowMajorOrdinal);
        }

        public static bool IsDefaultHomeBar(
            string blueprintId,
            int citySeed,
            Vector2Int cell)
        {
            return string.Equals(
                       blueprintId,
                       CityBlueprintCatalog.DefaultBlueprintId,
                       StringComparison.Ordinal) &&
                   citySeed == GameSessionState.DefaultCitySeed &&
                   cell == DefaultHomeBarCell;
        }
    }
}
