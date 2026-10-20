using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The apartment's dining chair uses the shared side-entry seat.
    /// The capsule waits in the east aisle while the world rig passes
    /// beside the backrest and settles under the table.
    /// </summary>
    public static class HomeDiningSeatPlan
    {
        public const string SeatId = "home-dining-chair";
        public const string VisualName = "Dining Chair";
        public const float SeatWidth = 0.50f;
        public const float SeatDepth = 0.44f;
        public const float FloorY = 0f;
        private const float CapsuleClearance = 0.36f;

        public static readonly Vector3 SeatTopCenter =
            new Vector3(-1.925f, 0.48f, -1.00f);
        public static readonly Vector3 ChairCenter =
            new Vector3(-1.925f, 0.51f, -1.015f);
        public static readonly Vector3 ChairSize =
            new Vector3(0.54f, 1.02f, 0.57f);

        public static CityBenchSitPlan Create(HomeInteriorLayoutPlan layout)
        {
            var seat = new CityBenchSeat(
                SeatId,
                SeatTopCenter,
                SeatWidth,
                SeatDepth,
                FloorY,
                Vector3.forward,
                kind: CityBenchSeatKind.DiningChair,
                sitPromptKey: CityBenchSitInteraction.DiningPromptKey,
                frontApproachOnly: true);
            var plan = new CityBenchSitPlan(seat);
            ValidateOrThrow(plan, layout);
            return plan;
        }

        public static CityBenchSitInteraction Build(
            Transform room,
            HomeInteriorLayoutPlan layout,
            PlayerRuntime player,
            Camera camera)
        {
            if (room == null)
            {
                throw new ArgumentNullException(nameof(room));
            }

            CityBenchSitPlan plan = Create(layout);
            GameObject chair = HomeAuthoredVisualFactory.CreateBox(
                VisualName,
                room,
                ChairCenter,
                ChairSize,
                new Color(0.26f, 0.16f, 0.09f));
            room.GetComponent<HomeOcclusionRegistry>()?.Register(
                "home.furniture.dining-chair",
                HomeOccluderKind.FurnitureBlocker,
                0.23f,
                chair);
            return CityBenchSitWorldBuilder.Build(
                room,
                new[] { plan },
                player,
                camera)[0];
        }

        public static void ValidateOrThrow(
            CityBenchSitPlan plan,
            HomeInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!plan.IsPresent ||
                plan.Kind != CityBenchSeatKind.DiningChair ||
                !layout.TryGetFurniture(
                    HomeFurnitureKind.Table,
                    out HomeFurnitureFootprint table))
            {
                throw new InvalidOperationException(
                    "The Home dining seat requires its authored chair and table.");
            }

            Vector3 dock = plan.EntryRootPosition;
            float groundedY = FloorY + PlayerFactory.GroundedRootOffset;
            Rect walkable = layout.WalkableBounds;
            if (Mathf.Abs(dock.y - groundedY) >
                PlayerMotor.InteractionVerticalTolerance ||
                dock.x < walkable.xMin + CapsuleClearance ||
                dock.x > walkable.xMax - CapsuleClearance ||
                dock.z < walkable.yMin + CapsuleClearance ||
                dock.z > walkable.yMax - CapsuleClearance ||
                !plan.IsWithinApproachLane(dock))
            {
                throw new InvalidOperationException(
                    "The Home dining dock must be grounded in its open aisle.");
            }

            Vector3 seat = plan.ActionHipPosition -
                Vector3.up * CityBenchSitPlan.SeatClearance;
            if (Mathf.Abs(seat.x - table.Bounds.center.x) > 0.01f ||
                table.Bounds.yMin - seat.z < 0.48f ||
                table.Bounds.yMin - seat.z > 0.65f)
            {
                throw new InvalidOperationException(
                    "The dining table moved away from its authored knee space.");
            }

            for (int index = 0; index < layout.Furniture.Count; index++)
            {
                HomeFurnitureFootprint fixture = layout.Furniture[index];
                if (!fixture.BlocksMovement)
                {
                    continue;
                }

                if (DistanceToRect(fixture.Bounds, dock) < CapsuleClearance)
                {
                    throw new InvalidOperationException(
                        $"The Home dining dock intersects '{fixture.Id}'.");
                }
            }

            var chairBounds = new Rect(
                ChairCenter.x - ChairSize.x * 0.5f,
                ChairCenter.z - ChairSize.z * 0.5f,
                ChairSize.x,
                ChairSize.z);
            if (DistanceToRect(chairBounds, dock) < CapsuleClearance)
            {
                throw new InvalidOperationException(
                    "The Home dining dock intersects its chair back or legs.");
            }
        }

        private static float DistanceToRect(Rect rect, Vector3 position)
        {
            float x = Mathf.Max(rect.xMin - position.x, position.x - rect.xMax, 0f);
            float z = Mathf.Max(rect.yMin - position.z, position.z - rect.yMax, 0f);
            return Mathf.Sqrt(x * x + z * z);
        }
    }
}
