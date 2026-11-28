using UnityEngine;

namespace BarPromenade
{
    /// <summary>The unbuilt rear quadrant of the eastern L-shaped house, in metres.</summary>
    internal sealed class RidgeBasketballPlan
    {
        internal const float BallRadius = .118f;
        internal readonly Vector2 Wall;
        internal readonly Rect Asphalt;
        internal RidgeBasketballPlan(Vector2 wall)
        {
            Wall = wall;
            Asphalt = Rect.MinMaxRect(wall.x - 2.5f, wall.y - 4.85f, wall.x + 2.5f, wall.y - .02f);
        }

        internal static RidgeBasketballPlan ForLayout(CityLayout layout)
        {
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings)
                if (house.Lot.Cell == new Vector2Int(21, -7) && house.Lot.BuildingVariant == 2)
                    return new RidgeBasketballPlan(house.FoundationBounds.center + Vector2.right * 2.2f);
            return null;
        }
    }
}
