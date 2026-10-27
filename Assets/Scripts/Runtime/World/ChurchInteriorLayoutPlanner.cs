using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class ChurchInteriorLayoutPlanner
    {
        public const float RoomWidth = 12.1f;
        public const float RoomDepth = 24.2f;
        public const float ModelMaximumHeight = 7.528624f;
        public const float RoomHeight = 7.6f;
        public const float WallThickness = 0.3f;
        public const float NarthexHalfWidth = 4.6f;
        public const float AisleHalfWidth = 5.75f;
        public const float NarthexEndZ = -8.55f;
        public const float SanctuaryBoundaryZ = 7.8f;
        public const float DoorPlaneZ = -12.1275f;
        public const float DoorOpeningWidth = 1.8f;
        public const float DoorOpeningHeight = 2.6f;
        public const float WindowApertureX = 6.06f;
        public const float WindowCenterY = 2.6675f;
        public const float WindowOpeningWidth = .6875f;
        public const float WindowOpeningHeight = 1.7875f;
        public const float RoseOpeningDiameter = 3.025f;
        public static readonly Vector3 RoseCenter = new Vector3(0f, 4.95f, -12.078f);
        public static readonly float[] WindowDepths = { -5.5f, -2.2f, 1.1f, 4.4f, 7.7f };
        public const string ModelResourcePath = "Church/ChurchInterior3D";
        public static readonly Bounds ModelLocalBounds = new Bounds(
            new Vector3(0f, 3.674312f, -.1338525f), new Vector3(12.105f, 7.708624f, 24.087295f));

        // The exterior wrapper faces +Z out of the west door; the interior
        // faces +Z toward the altar. Both are measured in placed world metres.
        public static Vector3 ToExteriorMetricPosition(Vector3 point) =>
            new Vector3(-point.x, point.y, -point.z);

        public static ChurchInteriorLayoutPlan Generate(int citySeed)
        {
            var plan = new ChurchInteriorLayoutPlan(
                citySeed, ComputeStableSeed(citySeed), new Vector2(RoomWidth, RoomDepth),
                RoomHeight, WallThickness, new Rect(-6.05f, -12.1f, RoomWidth, RoomDepth),
                ModelLocalBounds, new Rect(-AisleHalfWidth, -11.9f, AisleHalfWidth * 2f,
                    SanctuaryBoundaryZ + 11.9f),
                new Vector3(0f, PlayerFactory.GroundedRootOffset, -10.1f),
                new Vector3(0f, 0.95f, -11.2f), new Vector3(1.8f, 1.9f, 1.4f),
                ModelResourcePath, CreateZones(), CreatePaths(), CreateFixtures());
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

        private static List<ChurchInteriorZonePlan> CreateZones() => new List<ChurchInteriorZonePlan>
        {
            new ChurchInteriorZonePlan("narthex", ChurchInteriorZoneKind.Narthex,
                new Rect(-4.6f, -11.9f, 9.2f, 3.35f), 5.95f, true),
            new ChurchInteriorZonePlan("nave", ChurchInteriorZoneKind.Nave,
                new Rect(-5.75f, -8.55f, 11.5f, 11.55f), 7.4f, true),
            new ChurchInteriorZonePlan("crossing-and-choir", ChurchInteriorZoneKind.CrossingAndChoir,
                new Rect(-5.75f, 3f, 11.5f, 4.8f), 7.4f, true),
            new ChurchInteriorZonePlan("sanctuary", ChurchInteriorZoneKind.Sanctuary,
                new Rect(-5.75f, 8.14f, 11.5f, 3.66f), 7.4f, false)
        };

        private static List<ChurchInteriorPathPlan> CreatePaths() => new List<ChurchInteriorPathPlan>
        {
            new ChurchInteriorPathPlan("main-nave", ChurchInteriorPathKind.MainNave,
                new Rect(-.8f, -11.6f, 1.6f, 19.3f), 1.6f),
            new ChurchInteriorPathPlan("north-side-aisle", ChurchInteriorPathKind.NorthSideAisle,
                new Rect(-5.2f, -8.2f, 1.2f, 13.95f), 1.2f),
            new ChurchInteriorPathPlan("south-side-aisle", ChurchInteriorPathKind.SouthSideAisle,
                new Rect(4f, -8.2f, 1.2f, 13.95f), 1.2f),
            new ChurchInteriorPathPlan("narthex-crossing", ChurchInteriorPathKind.NarthexCrossing,
                new Rect(-5.2f, -8.2f, 10.4f, 1.2f), 1.2f),
            new ChurchInteriorPathPlan("transept-choir-crossing", ChurchInteriorPathKind.TranseptChoirCrossing,
                new Rect(-5.2f, 4.6f, 10.4f, 1.2f), 1.2f)
        };

        private static List<ChurchInteriorFixturePlan> CreateFixtures()
        {
            var fixtures = new List<ChurchInteriorFixturePlan>();
            float[] pierDepths = { -3.5f, 4f };
            int pier = 0;
            foreach (float z in pierDepths)
                for (int side = -1; side <= 1; side += 2)
                    fixtures.Add(Fixture($"pier-{++pier}", ChurchInteriorFixtureKind.Pier,
                        side * 3.5f, z, .84f, .84f, 0f, 4.9f));
            fixtures.Add(Fixture("communion-rail", ChurchInteriorFixtureKind.AltarRail,
                0f, 8f, 11.5f, .28f, 0f, .85f));
            fixtures.Add(Fixture("altar-table", ChurchInteriorFixtureKind.AltarTable,
                0f, 9.2f, 2.3f, 1.3f, 0f, 1f));
            fixtures.Add(Fixture("high-altar-tabernacle", ChurchInteriorFixtureKind.HighAltar,
                0f, 10.45f, 3.3f, 1.15f, 0f, 3.8f));
            fixtures.Add(Fixture("sanctuary-crucifix", ChurchInteriorFixtureKind.Crucifix,
                0f, 10.53f, 1.75f, .18f, 2.4f, 2.3f, false));
            float[] rows = { -6.1f, -4.7f, -2.3f, -.9f, .5f, 1.9f };
            for (int row = 0; row < rows.Length; row++)
                for (int side = -1; side <= 1; side += 2)
                    fixtures.Add(Fixture($"pew-{row + 1}-{(side < 0 ? "north" : "south")}",
                        ChurchInteriorFixtureKind.Pew, side * 2.05f, rows[row], 2.2f, .72f, 0f, 1.05f));
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "north" : "south";
                fixtures.Add(Fixture($"confessional-{suffix}", ChurchInteriorFixtureKind.Confessional,
                    side * 4.95f, 6.55f, 1.4f, 1.5f, 0f, 2.45f));
                fixtures.Add(Fixture($"votive-candle-stand-{suffix}", ChurchInteriorFixtureKind.VotiveCandleStand,
                    side * 3.2f, 6.3f, .7f, .7f, 0f, 1.35f));
            }
            fixtures.Add(Fixture("baptismal-font", ChurchInteriorFixtureKind.BaptismalFont,
                -3.5f, -8.8f, 1.1f, 1.1f, 0f, 1.1f));
            float[] supports = { -4.1f, -3.25f, 3.25f, 4.1f };
            for (int index = 0; index < supports.Length; index++)
                fixtures.Add(Fixture($"choir-loft-support-{index + 1}", ChurchInteriorFixtureKind.ChoirLoftSupport,
                    supports[index], -10.3f, .22f, .22f, 0f, 3f));
            fixtures.Add(Fixture("choir-loft", ChurchInteriorFixtureKind.ChoirLoft,
                0f, -10.75f, 8.7f, 2.4f, 3f, .22f));
            fixtures.Add(Fixture("pipe-organ", ChurchInteriorFixtureKind.Organ,
                0f, -10.3f, 4.8f, .9f, 3.22f, 2.7f));
            return fixtures;
        }

        private static ChurchInteriorFixturePlan Fixture(string id, ChurchInteriorFixtureKind kind,
            float x, float z, float width, float depth, float bottom, float height, bool blocks = true) =>
            new ChurchInteriorFixturePlan(id, kind,
                new Rect(x - width * .5f, z - depth * .5f, width, depth), bottom, height, blocks);
    }
}
