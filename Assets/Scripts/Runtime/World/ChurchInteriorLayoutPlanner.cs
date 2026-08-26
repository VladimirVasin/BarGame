using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class ChurchInteriorLayoutPlanner
    {
        public const float RoomWidth = 23f;
        public const float RoomDepth = 44f;
        public const float ModelMaximumHeight = 14f;
        public const float RoomHeight = 14.25f;
        public const float WallThickness = 0.35f;
        public const float SanctuaryBoundaryZ = 12.2f;
        public const string ModelResourcePath =
            "Church/ChurchInterior3D";
        public static readonly Bounds ModelLocalBounds = new Bounds(
            new Vector3(0f, 6.88f, 0f),
            new Vector3(22.82f, 14.24f, 44.02f));

        public static ChurchInteriorLayoutPlan Generate(int citySeed)
        {
            var plan = new ChurchInteriorLayoutPlan(
                citySeed,
                ComputeStableSeed(citySeed),
                new Vector2(RoomWidth, RoomDepth),
                RoomHeight,
                WallThickness,
                new Rect(-11.5f, -22f, RoomWidth, RoomDepth),
                ModelLocalBounds,
                new Rect(-10.8f, -20.6f, 21.6f, 32.8f),
                new Vector3(
                    0f,
                    PlayerFactory.GroundedRootOffset,
                    -18.8f),
                new Vector3(0f, 0.95f, -21.0f),
                new Vector3(2.8f, 1.9f, 1.4f),
                ModelResourcePath,
                CreateZones(),
                CreatePaths(),
                CreateFixtures());
            ChurchInteriorLayoutValidator.ValidateOrThrow(plan);
            return plan;
        }

        public static uint ComputeStableSeed(int citySeed)
        {
            uint hash = unchecked((uint)citySeed) ^ 0x43485552u;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            return hash ^ (hash >> 16);
        }

        private static List<ChurchInteriorZonePlan> CreateZones()
        {
            return new List<ChurchInteriorZonePlan>
            {
                new ChurchInteriorZonePlan(
                    "narthex",
                    ChurchInteriorZoneKind.Narthex,
                    new Rect(-10.8f, -20.6f, 21.6f, 7.0f),
                    6.8f,
                    true),
                new ChurchInteriorZonePlan(
                    "nave",
                    ChurchInteriorZoneKind.Nave,
                    new Rect(-10.8f, -13.6f, 21.6f, 9.4f),
                    9f,
                    true),
                new ChurchInteriorZonePlan(
                    "crossing-and-choir",
                    ChurchInteriorZoneKind.CrossingAndChoir,
                    new Rect(-10.8f, -4.2f, 21.6f, 16.4f),
                    RoomHeight,
                    true),
                new ChurchInteriorZonePlan(
                    "sanctuary",
                    ChurchInteriorZoneKind.Sanctuary,
                    new Rect(-10.8f, 12.6f, 21.6f, 8.0f),
                    10f,
                    false)
            };
        }

        private static List<ChurchInteriorPathPlan> CreatePaths()
        {
            return new List<ChurchInteriorPathPlan>
            {
                new ChurchInteriorPathPlan(
                    "main-nave",
                    ChurchInteriorPathKind.MainNave,
                    new Rect(-1.0f, -20.2f, 2.0f, 32.0f),
                    2.0f),
                new ChurchInteriorPathPlan(
                    "north-side-aisle",
                    ChurchInteriorPathKind.NorthSideAisle,
                    new Rect(-8.3f, -13.2f, 2.0f, 24.7f),
                    2.0f),
                new ChurchInteriorPathPlan(
                    "south-side-aisle",
                    ChurchInteriorPathKind.SouthSideAisle,
                    new Rect(6.3f, -13.2f, 2.0f, 24.7f),
                    2.0f),
                new ChurchInteriorPathPlan(
                    "narthex-crossing",
                    ChurchInteriorPathKind.NarthexCrossing,
                    new Rect(-8.2f, -16.2f, 16.4f, 3.0f),
                    3.0f),
                new ChurchInteriorPathPlan(
                    "transept-choir-crossing",
                    ChurchInteriorPathKind.TranseptChoirCrossing,
                    new Rect(-8.4f, 6.3f, 16.8f, 2.4f),
                    2.4f)
            };
        }

        private static List<ChurchInteriorFixturePlan> CreateFixtures()
        {
            var fixtures = new List<ChurchInteriorFixturePlan>();
            Vector2[] piers =
            {
                new Vector2(-5.5f, -3.5f),
                new Vector2(5.5f, -3.5f),
                new Vector2(-5.5f, 5.5f),
                new Vector2(5.5f, 5.5f)
            };
            for (int index = 0; index < piers.Length; index++)
            {
                Vector2 center = piers[index];
                fixtures.Add(new ChurchInteriorFixturePlan(
                    $"pier-{index + 1}",
                    ChurchInteriorFixtureKind.Pier,
                    CenteredRect(center.x, center.y, 1.4f, 1.4f),
                    0f,
                    9.6f,
                    true));
            }

            fixtures.Add(new ChurchInteriorFixturePlan(
                "communion-rail",
                ChurchInteriorFixtureKind.AltarRail,
                new Rect(-10.8f, 12.2f, 21.6f, 0.4f),
                0f,
                0.92f,
                true));
            fixtures.Add(new ChurchInteriorFixturePlan(
                "altar-table",
                ChurchInteriorFixtureKind.AltarTable,
                CenteredRect(0f, 15.7f, 2.75f, 1.55f),
                0f,
                1.14f,
                true));
            fixtures.Add(new ChurchInteriorFixturePlan(
                "high-altar-tabernacle",
                ChurchInteriorFixtureKind.HighAltar,
                CenteredRect(0f, 18.0f, 4.2f, 2.5f),
                0f,
                6.2f,
                true));
            fixtures.Add(new ChurchInteriorFixturePlan(
                "sanctuary-crucifix",
                ChurchInteriorFixtureKind.Crucifix,
                CenteredRect(0f, 20.65f, 2.5f, 0.35f),
                3.5f,
                4.7f,
                false));

            float[] pewRows =
            {
                -12f,
                -10.45f,
                -8.9f,
                -7.35f,
                -5.8f,
                -4.25f
            };
            for (int row = 0; row < pewRows.Length; row++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    string suffix = side < 0 ? "north" : "south";
                    fixtures.Add(new ChurchInteriorFixturePlan(
                        $"pew-{row + 1}-{suffix}",
                        ChurchInteriorFixtureKind.Pew,
                        CenteredRect(
                            side * 2.9f,
                            pewRows[row],
                            3.8f,
                            0.72f),
                        0f,
                        1.5f,
                        true));
                }
            }

            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "north" : "south";
                fixtures.Add(new ChurchInteriorFixturePlan(
                    $"confessional-{suffix}",
                    ChurchInteriorFixtureKind.Confessional,
                    CenteredRect(side * 9.7f, 7.3f, 1.8f, 3.3f),
                    0f,
                    3.15f,
                    true));
                fixtures.Add(new ChurchInteriorFixturePlan(
                    $"votive-candle-stand-{suffix}",
                    ChurchInteriorFixtureKind.VotiveCandleStand,
                    CenteredRect(side * 8.8f, 10.5f, 0.8f, 0.8f),
                    0f,
                    1.35f,
                    true));
            }

            fixtures.Add(new ChurchInteriorFixturePlan(
                "baptismal-font",
                ChurchInteriorFixtureKind.BaptismalFont,
                CenteredRect(-8.8f, -16.8f, 1.1f, 1.1f),
                0f,
                1.11f,
                true));
            float[] choirLoftSupportX =
            {
                -8f,
                -5.3f,
                5.3f,
                8f
            };
            for (int index = 0;
                 index < choirLoftSupportX.Length;
                 index++)
            {
                fixtures.Add(new ChurchInteriorFixturePlan(
                    $"choir-loft-support-{index + 1}",
                    ChurchInteriorFixtureKind.ChoirLoftSupport,
                    CenteredRect(
                        choirLoftSupportX[index],
                        -18.2f,
                        0.32f,
                        0.32f),
                    0f,
                    4.4f,
                    true));
            }

            fixtures.Add(new ChurchInteriorFixturePlan(
                "choir-loft",
                ChurchInteriorFixtureKind.ChoirLoft,
                CenteredRect(0f, -18.4f, 17f, 4.2f),
                4.4f,
                0.4f,
                true));
            fixtures.Add(new ChurchInteriorFixturePlan(
                "pipe-organ",
                ChurchInteriorFixtureKind.Organ,
                CenteredRect(0f, -20.3f, 12f, 1.6f),
                4.8f,
                6.9f,
                true));
            return fixtures;
        }

        private static Rect CenteredRect(
            float x,
            float z,
            float width,
            float depth)
        {
            return new Rect(
                x - width * 0.5f,
                z - depth * 0.5f,
                width,
                depth);
        }
    }
}
