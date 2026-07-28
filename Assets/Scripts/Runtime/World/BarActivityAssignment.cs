using System;

namespace BarPromenade
{
    public static class BarActivityAssignment
    {
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
                default:
                    return BarActivityKind.Cocktail;
            }
        }
    }
}
