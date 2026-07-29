using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeInteriorLayoutPlanner
    {
        public static HomeInteriorLayoutPlan Generate()
        {
            var furniture = new List<HomeFurnitureFootprint>
            {
                new HomeFurnitureFootprint(
                    HomeFurnitureKind.Bed,
                    new Rect(-4.25f, -1.25f, 2.55f, 1.75f)),
                new HomeFurnitureFootprint(
                    HomeFurnitureKind.Kitchen,
                    new Rect(-4.35f, 2.35f, 3.90f, 0.95f)),
                new HomeFurnitureFootprint(
                    HomeFurnitureKind.Sofa,
                    new Rect(2.75f, -0.90f, 1.10f, 2.65f)),
                new HomeFurnitureFootprint(
                    HomeFurnitureKind.Table,
                    new Rect(0.65f, 1.35f, 1.65f, 1.45f)),
                new HomeFurnitureFootprint(
                    HomeFurnitureKind.Bookcase,
                    new Rect(3.55f, 2.15f, 0.65f, 1.25f))
            };
            var plan = new HomeInteriorLayoutPlan(
                new Vector2(10f, 8f),
                3.4f,
                new Rect(-4.65f, -3.65f, 9.30f, 7.30f),
                new Vector3(0f, 0.12f, -2.15f),
                new Vector3(0f, 1.05f, -3.48f),
                new Vector3(2.20f, 2.10f, 0.65f),
                new Rect(-0.80f, -3.65f, 1.60f, 2.15f),
                furniture);
            HomeInteriorLayoutValidator.ValidateOrThrow(plan);
            return plan;
        }
    }
}
