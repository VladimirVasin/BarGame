using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The single inaccessible story room. All dimensions are Home-local metres.</summary>
    public static class HomeLockedRoomPlan
    {
        public static Rect Bounds => Rect.MinMaxRect(-0.10f, 0.82f, 1.46f, 3.88f);
        public static Vector3 DoorPosition => new Vector3(0.68f, 1.05f, 0.815f);
        public static Vector3 DockPosition => new Vector3(0.68f, PlayerFactory.GroundedRootOffset, 0.30f);
        public static Vector3 TriggerPosition => new Vector3(0.68f, 1.0f, 0.28f);
        public static Vector3 TriggerSize => new Vector3(1.10f, 2f, 0.90f);

        public static void ValidateOrThrow(HomeInteriorLayoutPlan plan)
        {
            foreach (HomeFurnitureFootprint furniture in plan.Furniture)
                if (Overlaps(Bounds, furniture.Bounds))
                    throw new InvalidOperationException($"'{furniture.Id}' intrudes into the locked room.");
            foreach (HomeInteriorPath path in plan.Paths)
                if (Overlaps(Bounds, path.Bounds))
                    throw new InvalidOperationException($"The locked room blocks '{path.Id}'.");
            if (!plan.WalkableBounds.Contains(new Vector2(DockPosition.x, DockPosition.z)))
                throw new InvalidOperationException("The locked-room dock must be walkable.");
        }

        private static bool Overlaps(Rect a, Rect b) =>
            Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 0.001f &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 0.001f;

        public static void BuildCollision(Transform room, HomeInteriorLayoutPlan plan)
        {
            ValidateOrThrow(plan);
            var solid = new GameObject("Home Locked Room Collision");
            solid.transform.SetParent(room, false);
            BoxCollider collider = solid.AddComponent<BoxCollider>();
            collider.center = new Vector3(Bounds.center.x, plan.RoomHeight * 0.5f, Bounds.center.y);
            collider.size = new Vector3(Bounds.width, plan.RoomHeight, Bounds.height);
        }
    }
}
