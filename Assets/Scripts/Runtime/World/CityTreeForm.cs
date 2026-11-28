using UnityEngine;

namespace BarPromenade
{
    // The crown stretches about the fork, so its lower lobes stay attached to the branches.
    internal readonly struct CityTreeForm
    {
        internal readonly Vector3 Wood, Crown;
        internal readonly int Variant;
        internal readonly Quaternion Rotation;
        internal readonly float Fork, CrownLift, Height, LeafHeight, Radius;
        internal float TrunkRadius => .34f * Wood.x;

        internal CityTreeForm(int seed, Vector3 root, int variant)
        {
            int x = Mathf.RoundToInt(root.x * 4), z = Mathf.RoundToInt(root.z * 4);
            float Pick(uint salt) => CityGrassPlan.Unit(CityGrassPlan.Hash(seed, x, z, salt));
            Variant = variant;
            Rotation = Quaternion.Euler(0, 360f * Pick(901), 0);
            bool veteran = Pick(902) < .18f;
            float height = veteran ? Mathf.Lerp(2f, 2.18f, Pick(903)) : Mathf.Lerp(.72f, 1.58f, Pick(903));
            float girth = veteran ? Mathf.Lerp(1.37f, 1.72f, Pick(904)) : Mathf.Lerp(.72f, 1.2f, Pick(904));
            Wood = new Vector3(girth, height, girth);
            float spread = (veteran ? 1.5f : 1f) * Mathf.Lerp(.76f, 1.38f, Pick(905));
            float taper = Mathf.Lerp(.63f, 1.18f, Pick(906));
            Fork = 3.13f + .24f * variant;
            Crown = new Vector3(spread, height * (veteran ? Mathf.Lerp(1f, 1.15f, Pick(907)) :
                Mathf.Lerp(.64f, 1.32f, Pick(907))), spread * taper);
            CrownLift = Fork * (Wood.y - Crown.y);
            Height = CrownLift + (5.23f + .24f * variant) * Crown.y;
            LeafHeight = CrownLift + (3.97f + .24f * variant) * Crown.y;
            Radius = 1.75f * Mathf.Max(Crown.x, Crown.z, Wood.x);
        }
    }
}
