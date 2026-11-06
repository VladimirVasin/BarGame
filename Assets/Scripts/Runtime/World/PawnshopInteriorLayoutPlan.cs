using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Fixed metre-space contract shared by the imported room and gameplay.</summary>
    public sealed class PawnshopInteriorLayoutPlan : IWalkableArea
    {
        public const float RoomWidth = 7.4f;
        public const float RoomDepth = 8.4f;
        public const float RoomHeight = 3f;
        public const float WallThickness = .15f;
        public const float PartitionZ = .6f;
        public const float DoorWidth = 1.4f;
        public const float DoorHeight = 2.35f;
        public Vector2 RoomSize => new Vector2(RoomWidth, RoomDepth);
        public Rect WalkableBounds => Rect.MinMaxRect(-3.7f, -4.2f, 3.7f, .5f);
        public Vector3 PlayerSpawn => new Vector3(0f, PlayerFactory.GroundedRootOffset, -3.15f);
        public Vector3 ExitPosition => new Vector3(.56f, 1.05f, -4.16f);
        public Vector3 ExitTriggerSize => new Vector3(1.45f, 2f, 1.2f);
        public Vector3 DoorPivot => new Vector3(-.7f, 0f, -4.2f);
        public Vector3 DoorHandle => new Vector3(.56f, 1.05f, -4.265f);
        public Vector3 DoorDock => new Vector3(.22f, PlayerFactory.GroundedRootOffset, -3.6f);
        public Vector3 ClerkPosition => new Vector3(0f, 0f, 2f);
        public Vector3 TradePosition => new Vector3(0f, 0f, -.45f);
        public Bounds CounterBounds => new Bounds(new Vector3(-.2f, .525f, .425f), new Vector3(5.12f, 1.05f, .84f));
        public Bounds SecurePartitionBounds => new Bounds(new Vector3(0f, 1.5f, .7f), new Vector3(7.4f, 3f, .2f));
        public Bounds LeftDisplayBounds => new Bounds(new Vector3(-2.55f, .5f, -1.9f), new Vector3(1.86f, 1f, .78f));
        public Bounds RightDisplayBounds => new Bounds(new Vector3(2.55f, .5f, -1.9f), new Vector3(1.86f, 1f, .78f));

        public bool Contains(Vector3 position, float radius = 0f)
        {
            Rect bounds = WalkableBounds;
            if (!Finite(position) || !Finite(radius) || radius < 0f ||
                position.x < bounds.xMin + radius || position.x > bounds.xMax - radius ||
                position.z < bounds.yMin + radius || position.z > bounds.yMax - radius) return false;
            return !Inside(CounterBounds, position, radius) &&
                !Inside(LeftDisplayBounds, position, radius) && !Inside(RightDisplayBounds, position, radius);
        }

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f)
        {
            if (!Finite(desiredPosition) || !Finite(radius) || radius < 0f) return currentPosition;
            Rect bounds = WalkableBounds;
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, bounds.xMin + radius, bounds.xMax - radius);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, bounds.yMin + radius, bounds.yMax - radius);
            if (Contains(desiredPosition, radius)) return desiredPosition;
            Vector3 alongX = new Vector3(desiredPosition.x, desiredPosition.y, currentPosition.z);
            if (Contains(alongX, radius)) return alongX;
            Vector3 alongZ = new Vector3(currentPosition.x, desiredPosition.y, desiredPosition.z);
            return Contains(alongZ, radius) ? alongZ : currentPosition;
        }

        private static bool Inside(Bounds bounds, Vector3 point, float radius) =>
            point.x > bounds.min.x - radius && point.x < bounds.max.x + radius &&
            point.z > bounds.min.z - radius && point.z < bounds.max.z + radius;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }

    public static class PawnshopInteriorLayoutPlanner
    {
        public static PawnshopInteriorLayoutPlan Generate()
        {
            var plan = new PawnshopInteriorLayoutPlan();
            PawnshopInteriorLayoutValidator.ValidateOrThrow(plan);
            return plan;
        }
    }

    public static class PawnshopInteriorLayoutValidator
    {
        public static void ValidateOrThrow(PawnshopInteriorLayoutPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            const float capsuleRadius = .32f;
            if (!plan.Contains(plan.PlayerSpawn, capsuleRadius) || !plan.Contains(plan.DoorDock, capsuleRadius) ||
                !plan.Contains(plan.TradePosition, capsuleRadius) || plan.Contains(plan.ClerkPosition, capsuleRadius) ||
                plan.CounterBounds.max.z < PawnshopInteriorLayoutPlan.PartitionZ ||
                plan.SecurePartitionBounds.size.x < PawnshopInteriorLayoutPlan.RoomWidth)
                throw new InvalidOperationException("Pawnshop entry and service must be reachable while the staff room stays closed.");
        }
    }
}
