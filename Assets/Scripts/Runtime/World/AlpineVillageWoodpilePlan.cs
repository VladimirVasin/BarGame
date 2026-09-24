using UnityEngine;

namespace BarPromenade
{
    /// <summary>One authored stack placement shared by geometry, snow and movement.</summary>
    public sealed class AlpineVillageWoodpilePlan
    {
        public const string LodgeName = "Lodge Woodpile";
        public const string MothersHouseName = "Mothers House Woodpile";
        public static readonly Vector2 Size = new Vector2(1.75f, .64f);

        internal AlpineVillageWoodpilePlan(string name, Vector3 center, Vector3 forward)
        {
            Name = name;
            Center = center;
            Forward = forward.normalized;
        }

        public string Name { get; }
        public Vector3 Center { get; }
        // The imported logs' reachable ends face local -Z.
        public Vector3 Forward { get; }
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        public Quaternion Rotation => Quaternion.LookRotation(Forward, Vector3.up);
        public Vector3 Approach => Center - Forward * 1.1f;

        internal static AlpineVillageWoodpilePlan AtMothersHouse(AlpineVillagePlotDescriptor house)
        {
            Vector3 right = Vector3.Cross(Vector3.up, house.Facing);
            // Beside the timber facade, clear of the doorstep and return dock.
            Vector3 center = house.GroundCenter - right * 3.2f +
                house.Facing * (house.FootprintSize.y * .5f + .5f);
            return new AlpineVillageWoodpilePlan(MothersHouseName, center, -house.Facing);
        }

        internal float LimitSnow(Vector2 point, float depth)
        {
            Vector3 delta = new Vector3(point.x - Center.x, 0f, point.y - Center.z);
            Vector2 half = Size * .5f + new Vector2(.08f, .12f);
            float distance = new Vector2(
                Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, Right)) - half.x),
                Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, Forward)) - half.y)).magnitude;
            return Mathf.Lerp(Mathf.Min(depth, .025f), depth,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / .65f)));
        }
    }
}
