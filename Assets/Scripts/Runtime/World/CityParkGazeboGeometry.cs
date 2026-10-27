using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Fixed-metre contract shared with the Blender park assembly.</summary>
    public static class CityParkGazeboGeometry
    {
        public const float DeckHeightMeters = 0.54f;
        public const float BeamUndersideHeightMeters = 4f;
        public const float DeckRadiusX = 4.3f;
        public const float DeckRadiusZ = 3.7f;
        public const float PlinthRadiusX = 4.5f;
        public const float PlinthRadiusZ = 3.9f;
        public const float EaveRadiusX = 4.7f;
        public const float EaveRadiusZ = 4.1f;

        public static bool TryFind(CityDecorationPlan plan,
            out CityDecorationDescriptor gazebo)
        {
            if (plan != null)
                foreach (CityDecorationDescriptor descriptor in plan.Descriptors)
                    if (descriptor.Kind == CityDecorationKind.ParkBandstand)
                    {
                        gazebo = descriptor;
                        return true;
                    }
            gazebo = default;
            return false;
        }

        public static Vector3 Forward(CityDecorationDescriptor gazebo)
        {
            Vector3 forward = gazebo.Forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.001f) return Vector3.forward;
            // Decoration recipes resolve their frame to the same cardinal
            // axis. A diagonal roof frame would disagree with its furniture.
            return Mathf.Abs(forward.x) > Mathf.Abs(forward.z)
                ? new Vector3(Mathf.Sign(forward.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(forward.z));
        }

        public static Vector3 ToWorld(CityDecorationDescriptor gazebo, Vector3 local)
        {
            Vector3 forward = Forward(gazebo);
            Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
            return gazebo.Position + tangent * local.x + Vector3.up * local.y + forward * local.z;
        }

        public static bool TrySampleFloorTop(CityDecorationDescriptor gazebo,
            Vector3 point, out float top)
        {
            Vector3 forward = Forward(gazebo);
            Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
            Vector3 offset = point - gazebo.Position;
            float x = Vector3.Dot(offset, tangent);
            float z = Vector3.Dot(offset, forward);
            if (InsideRing(x, z, DeckRadiusX, DeckRadiusZ))
            {
                top = gazebo.Position.y + DeckHeightMeters;
                return true;
            }
            if (Mathf.Abs(x) <= 1.2f && z >= 2.9f && z <= 4.9f)
            {
                top = gazebo.Position.y + (z <= 3.8f ? 0.54f : z <= 4.35f ? 0.36f : 0.18f);
                return true;
            }
            top = 0f;
            return false;
        }

        public static bool InsideRing(float x, float z, float radiusX, float radiusZ)
        {
            // The true twelve-gon, rather than its larger enclosing ellipse.
            for (int side = 0; side < 12; side++)
            {
                float a = side * Mathf.PI / 6f;
                float b = (side + 1) * Mathf.PI / 6f;
                Vector2 from = new Vector2(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusZ);
                Vector2 to = new Vector2(Mathf.Cos(b) * radiusX, Mathf.Sin(b) * radiusZ);
                Vector2 edge = to - from;
                Vector2 relative = new Vector2(x, z) - from;
                if (edge.x * relative.y - edge.y * relative.x < -0.001f) return false;
            }
            return true;
        }

        // Only vertical obstructions enter route planning. The imported floor,
        // stairs and roof have their own exact physical mesh colliders.
        internal static void AppendRouteBlockers(CityDecorationDescriptor gazebo,
            ICollection<RuntimeOrientedBox> target)
        {
            Quaternion rotation = Quaternion.LookRotation(Forward(gazebo));
            for (int column = 0; column < 4; column++)
            {
                float angle = (45f + column * 90f) * Mathf.Deg2Rad;
                Vector3 local = new Vector3((DeckRadiusX - 0.4f) * Mathf.Cos(angle),
                    (DeckHeightMeters + 4.08f) * 0.5f,
                    (DeckRadiusZ - 0.4f) * Mathf.Sin(angle));
                target.Add(new RuntimeOrientedBox(ToWorld(gazebo, local), rotation,
                    new Vector3(0.28f, 4.08f - DeckHeightMeters, 0.28f)));
            }
            float[] angles = { 225f, 240f, 270f, 300f, 315f };
            for (int rail = 0; rail < angles.Length - 1; rail++)
            {
                Vector3 a = RailPoint(angles[rail]);
                Vector3 b = RailPoint(angles[rail + 1]);
                Vector3 worldA = ToWorld(gazebo, a);
                Vector3 worldB = ToWorld(gazebo, b);
                target.Add(new RuntimeOrientedBox((worldA + worldB) * 0.5f,
                    Quaternion.LookRotation(worldB - worldA),
                    new Vector3(0.16f, 0.9f, Vector3.Distance(worldA, worldB) + 0.12f)));
            }
        }

        private static Vector3 RailPoint(float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return new Vector3((DeckRadiusX - 0.4f) * Mathf.Cos(angle), 0.95f,
                (DeckRadiusZ - 0.4f) * Mathf.Sin(angle));
        }
    }
}
