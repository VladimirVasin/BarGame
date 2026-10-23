using UnityEngine;

namespace BarPromenade
{
    /// <summary>Metre contract shared by the room plan, imported anchors and interactions.</summary>
    public static class BarWashroomLayout
    {
        public const float CeilingHeight = 3f;
        public const float WallThickness = .18f;
        public const float DoorWidth = 1.1f;
        public const float DoorHeight = 2.2f;
        public const float DoorOpenAngle = -90f;
        public static readonly Rect Bounds = Rect.MinMaxRect(-5.4f, 4.15f, -2.4f, 7.15f);
        // Overlaps the hall and room by a full capsule diameter. Eroding each
        // region therefore leaves a continuous, conservative traversal corridor.
        public static readonly Rect PassageBounds = Rect.MinMaxRect(-4.75f, 2.8f, -3.65f, 4.9f);
        public static readonly Vector3 DoorCenter = new Vector3(-4.2f, 0f, 3.9f);
        public static readonly Vector3 DoorPivot = new Vector3(-4.75f, 0f, 3.9f);
        public static readonly Vector3 LightPosition = new Vector3(-3.9f, 2.65f, 5.65f);
        public static readonly Rect ExteriorRange = Rect.MinMaxRect(-11.39f, -5.58f, -8.03f, -2.22f);

        public static bool Contains(Rect bounds, Vector3 point, float margin = 0f)
        {
            return point.x >= bounds.xMin + margin && point.x <= bounds.xMax - margin &&
                   point.z >= bounds.yMin + margin && point.z <= bounds.yMax - margin;
        }
    }
}
