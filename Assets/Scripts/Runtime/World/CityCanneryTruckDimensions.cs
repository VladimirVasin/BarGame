using UnityEngine;

namespace BarPromenade
{
    /// <summary>Fixed-metre envelope shared by the compact truck, its routes and crew contacts.</summary>
    public static class CityCanneryTruckDimensions
    {
        public const float Rear = -2f;
        public const float Front = 4.5f;
        public const float HalfWidth = 1.2f;
        public const float Wheelbase = 3.3f;
        public const float CabOffset = -.9f;
        public const float RearOffset = .6f;
        public const float Height = 3.2f;
        public const float Length = Front - Rear;
        public const float Width = HalfWidth * 2f;
        public const float TailLiftLoadZ = Rear - .68f;
        public const float TailLiftLength = 2f;
        public const float LiftOperatorOffset = 1f;
        public const float GroundOperatorOffset = 1.43f;
        public static Vector3 BodyCenter => new Vector3(0f, 1.67f, (Rear + Front) * .5f);
        public static Vector3 BodySize => new Vector3(Width, 3.06f, Length);
        public static Vector3 BodyHalfExtents => BodySize * .5f;
    }
}
