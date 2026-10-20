using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeInteriorLayoutPlanner
    {
        public static HomeInteriorLayoutPlan Generate()
        {
            Rect walkableBounds =
                new Rect(-6.65f, -3.65f, 11.30f, 7.30f);
            Rect entryCorridor =
                new Rect(-0.80f, -3.65f, 1.60f, 2.15f);
            Rect bathroomBounds =
                new Rect(1.55f, 0.65f, 3.10f, 3.00f);
            Rect bathroomDoorway =
                new Rect(1.72f, 0.50f, 1.10f, 0.32f);

            var zones = new List<HomeInteriorZone>
            {
                new HomeInteriorZone(
                    "main-room",
                    HomeInteriorZoneKind.MainRoom,
                    walkableBounds),
                new HomeInteriorZone(
                    "bathroom",
                    HomeInteriorZoneKind.Bathroom,
                    bathroomBounds)
            };
            var paths = new List<HomeInteriorPath>
            {
                new HomeInteriorPath(
                    "entry",
                    HomeInteriorPathKind.Entry,
                    entryCorridor,
                    1.60f),
                new HomeInteriorPath(
                    "main",
                    HomeInteriorPathKind.Main,
                    new Rect(-0.80f, -3.65f, 3.62f, 4.47f),
                    1.20f),
                new HomeInteriorPath(
                    "bathroom-access",
                    HomeInteriorPathKind.BathroomAccess,
                    new Rect(1.72f, 0.50f, 1.10f, 2.65f),
                    1.10f)
            };
            var furniture = new List<HomeFurnitureFootprint>
            {
                new HomeFurnitureFootprint(
                    "sleeping-sofa",
                    HomeFurnitureKind.Bed,
                    new Rect(-6.86f, -1.45f, HomeBedInteractionPlan.FurnitureDepth,
                        HomeBedInteractionPlan.FurnitureLength),
                    1.08f,
                    true),
                new HomeFurnitureFootprint(
                    "kitchen",
                    HomeFurnitureKind.Kitchen,
                    new Rect(-6.65f, 2.48f, 3.37f, 0.82f),
                    1.08f,
                    true),
                new HomeFurnitureFootprint(
                    "table",
                    HomeFurnitureKind.Table,
                    new Rect(-2.65f, -0.45f, 1.45f, 1.20f),
                    0.83f,
                    true),
                new HomeFurnitureFootprint(
                    "bookcase",
                    HomeFurnitureKind.Bookcase,
                    new Rect(-6.86f, -3.76f, 1.10f, 0.65f),
                    2.35f,
                    true),
                new HomeFurnitureFootprint(
                    "toilet",
                    HomeFurnitureKind.Toilet,
                    new Rect(3.65f, 0.85f, 1.00f, 1.10f),
                    0.85f,
                    true),
                new HomeFurnitureFootprint(
                    "shower",
                    HomeFurnitureKind.Shower,
                    new Rect(3.35f, 2.35f, 1.15f, 1.15f),
                    2.20f,
                    false),
                new HomeFurnitureFootprint(
                    "sink",
                    HomeFurnitureKind.Sink,
                    new Rect(1.65f, 3.25f, 0.85f, 0.35f),
                    0.95f,
                    true)
            };
            var plan = new HomeInteriorLayoutPlan(
                new Vector2(12f, 8f),
                3.4f,
                walkableBounds,
                new Vector3(0f, 0.12f, -2.15f),
                new Vector3(0f, 1.05f, -3.48f),
                new Vector3(2.20f, 2.10f, 0.65f),
                entryCorridor,
                bathroomBounds,
                bathroomDoorway,
                zones,
                paths,
                furniture,
                new Vector2(-1f, 0f));
            HomeInteriorLayoutValidator.ValidateOrThrow(plan);
            return plan;
        }
    }
}
